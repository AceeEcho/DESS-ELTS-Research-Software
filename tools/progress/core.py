"""Deterministic progress reduction and semantic validation.

Truth flows only from the accepted catalog and append-only events. Task chains
serialize steps belonging to one task; explicit causal hashes order independent
chains. Neither wall-clock timestamps nor filenames arbitrate conflicts.
"""
from __future__ import annotations

import copy
import hashlib
import heapq
import json
import re
from datetime import datetime
from pathlib import Path, PurePosixPath

from schema import ValidationError, load_json, validate_file

VERSION = "1.0.0"
PROJECT = "DESS-ELTS-Research-Software"
PLAN_VERSION = "ELTS-build-plan-1.0+PC-001"
EPOCH = "1970-01-01T00:00:00Z"  # Stable empty-store generation value, not work time.
ACTIVE = {"in_progress", "verification_pending"}
STEP_TRANSITIONS = {
    "task_started": {("ready", "in_progress")},
    "step_ready": {("not_started", "ready")},
    "verification_pending": {("in_progress", "verification_pending")},
    "step_completed": {("verification_pending", "done")},
    "blocked": {(s, "blocked") for s in ("ready", "in_progress", "verification_pending")},
    "unblocked": {("blocked", "ready"), ("blocked", "in_progress")},
    "reopened": {("done", "in_progress"), ("verification_pending", "in_progress")},
}


def canonical(value) -> bytes:
    return json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(",", ":"),
                      allow_nan=False).encode("utf-8")


def digest(value) -> str:
    return hashlib.sha256(canonical(value)).hexdigest()


def file_hash(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def utc(value: str):
    return datetime.fromisoformat(value.replace("Z", "+00:00"))


def fail(message):
    raise ValidationError(message)


def repository_path(root: Path, value: str, *, must_exist=True) -> Path:
    """Reject aliases, traversal, Windows drives/ADS and symlink escapes."""
    if not isinstance(value, str) or not value or "\\" in value or ":" in value:
        fail(f"Not a repository-relative path: {value!r}")
    parts = value.rstrip("/").split("/")
    if any(p in ("", ".", "..") for p in parts) or PurePosixPath(value).is_absolute():
        fail(f"Non-canonical repository path: {value!r}")
    path = (root / value).resolve()
    if not path.is_relative_to(root.resolve()):
        fail(f"Path escapes repository: {value}")
    if must_exist and not path.is_file():
        fail(f"Evidence file is missing: {value}")
    return path


def references(predicate):
    if "id" in predicate:
        return {predicate["id"]}
    return set().union(*(references(p) for p in next(iter(predicate.values()))))


def eligible(predicate, facts):
    if "id" in predicate:
        return facts.get(predicate["id"]) == predicate["state"]
    if "allOf" in predicate:
        return all(eligible(p, facts) for p in predicate["allOf"])
    if "anyOf" in predicate:
        return any(eligible(p, facts) for p in predicate["anyOf"])
    fail("Invalid eligibility predicate")


def missing(predicate, facts):
    if eligible(predicate, facts):
        return set()
    if "id" in predicate:
        return {predicate["id"]}
    return set().union(*(missing(p, facts) for p in next(iter(predicate.values()))))


def empty_record(status="not_started"):
    return {"status": status, "revision": 0, "lastEventHash": None,
            "owner": None, "branch": None, "claimedPaths": [], "evidence": [], "blockers": []}


def actor_id(actor):
    return actor["type"] + ":" + actor["id"]


def chain_key(event):
    if event["target"]["kind"] in {"task", "atomic_step"}:
        return "task:" + str(event["taskId"])
    return event["target"]["kind"] + ":" + event["target"]["id"]


def ordered_events(events, schemas: Path):
    """Validate chains before topological sorting, including divergent claims."""
    by_hash, ids, chains = {}, set(), {}
    for event in events:
        validate_file(event, schemas / "progress-event.schema.json")
        if event["eventId"] in ids:
            fail(f"Duplicate event ID: {event['eventId']}")
        ids.add(event["eventId"])
        key, revision = chain_key(event), event["taskRevision"]
        revisions = chains.setdefault(key, {})
        if revision in revisions:
            fail(f"Conflicting event chain {key} revision {revision}; reconcile explicitly")
        hashed = digest(event)
        by_hash[hashed] = event
        revisions[revision] = hashed
        if utc(event["occurredAtUtc"]) > utc(event["recordedAtUtc"]):
            fail(f"Event {event['eventId']}: occurred time is after recorded time")
    for key, revisions in chains.items():
        if set(revisions) != set(range(1, len(revisions) + 1)):
            fail(f"Gap in event revisions: {key}")
        for revision, hashed in revisions.items():
            event = by_hash[hashed]
            expected = revisions.get(revision - 1)
            if event["previousTaskEventHash"] != expected:
                fail(f"Broken previous event hash: {key} revision {revision}")
            if expected and expected not in event["causalEventHashes"]:
                fail(f"Task predecessor missing from causal parents: {key}")
    children = {h: [] for h in by_hash}
    degree = {}
    for hashed, event in by_hash.items():
        parents = event["causalEventHashes"]
        degree[hashed] = len(parents)
        for parent in parents:
            if parent not in by_hash:
                fail(f"Missing causal event hash: {parent}")
            children[parent].append(hashed)
    ready = [h for h, n in degree.items() if n == 0]
    heapq.heapify(ready)
    ordered = []
    while ready:
        hashed = heapq.heappop(ready)
        ordered.append((hashed, by_hash[hashed]))
        for child in children[hashed]:
            degree[child] -= 1
            if degree[child] == 0:
                heapq.heappush(ready, child)
    if len(ordered) != len(events):
        fail("Cycle in event causal graph")
    return ordered


class Reducer:
    def __init__(self, catalog, root: Path, authorities=None):
        self.root = root.resolve()
        self.catalog = catalog
        self.schemas = self.root / "schemas/progress"
        self.steps = {s["id"]: s for s in catalog["atomicSteps"]}
        self.tasks = {t["id"]: t for t in catalog["tasks"]}
        self.task_step_ids = {key: [s["id"] for s in catalog["atomicSteps"] if s["parent"] == key] for key in self.tasks}
        self._facts = None
        self.step_records = {key: empty_record() for key in self.steps}
        self.task_records = {key: empty_record() for key in self.tasks}
        self.decisions = {r["Decision ID"]: empty_record("informational" if r["Decision ID"] == "D-15" else "open")
                          for r in catalog["decisions"]}
        self.gates = {"G" + str(i): empty_record("open" if i == 0 else "locked") for i in range(9)}
        self.chains = {}
        self.accepted_hashes = set()
        self.ancestors = {}
        self.event_ancestors = set()
        self.evidence_cache = {}
        self.catalog_hash = file_hash(self.root / "project-management/task-catalog.json")
        self.authorities = authorities if authorities is not None else load_json(self.root / "project-management/progress/authorities.json")
        validate_file(self.authorities, self.schemas / "authorities.schema.json")

    def task_status(self, task_id):
        raw = self.task_records[task_id]["status"]
        if raw == "done":
            return "done"
        statuses = [self.step_records[key]["status"] for key in self.task_step_ids[task_id]]
        if any(s == "in_progress" for s in statuses):
            return "in_progress"
        if any(s == "verification_pending" for s in statuses) or all(s == "done" for s in statuses):
            return "verification_pending"
        if any(s == "blocked" for s in statuses):
            return "blocked"
        if any(s == "done" for s in statuses):
            return "in_progress"
        return raw

    def facts(self):
        if self._facts is not None:
            return self._facts
        facts = {k: r["status"] for k, r in self.step_records.items()}
        facts.update({k: self.task_status(k) for k in self.tasks})
        facts.update({k: r["status"] for k, r in self.gates.items()})
        facts.update({k: r["status"] for k, r in self.decisions.items()})
        self._facts = facts
        return facts

    def effective_step_status(self, identifier):
        raw = self.step_records[identifier]["status"]
        if raw == "not_started" and eligible(self.steps[identifier]["eligibility"], self.facts()):
            return "ready"
        return raw

    def check_evidence(self, event, required=()):
        target = event["target"]["id"]
        coverage, classes = set(), set()
        seen = set()
        for item in event["evidence"]:
            if item["id"] in seen:
                fail(f"Duplicate evidence identifier: {item['id']}")
            seen.add(item["id"])
            if target not in item["targetIds"]:
                fail(f"Evidence {item['id']} is not bound to {target}")
            path = repository_path(self.root, item["path"])
            key = (str(path), item["sha256"])
            if key not in self.evidence_cache:
                if file_hash(path) != item["sha256"]:
                    fail(f"Evidence checksum mismatch: {item['path']}")
                self.evidence_cache[key] = True
            if utc(item["recordedAtUtc"]) > utc(event["recordedAtUtc"]):
                fail("Evidence was recorded after its event")
            if item["result"] == "pass":
                coverage.update(item["criteria"])
                classes.add(item["class"])
        uncovered = set(required) - coverage
        if uncovered:
            fail(f"Acceptance evidence missing for {target}: {sorted(uncovered)}")
        return classes

    def check_approval(self, event, expected_decision):
        approval = event["approval"]
        if event["actor"]["type"] != "human" or approval is None:
            fail("This transition requires an evidence-backed authorized human approval")
        authority = self.authorities["humans"].get(approval["authorityId"])
        target = event["target"]["id"]
        if not authority or authority["actorId"] != event["actor"]["id"] or target not in authority["allowedTargets"]:
            fail(f"No enrolled human authority for {target}")
        auth_path = repository_path(self.root, authority["authorityRecordPath"])
        if file_hash(auth_path) != authority["authorityRecordSha256"]:
            fail("Human authority record checksum mismatch")
        if approval["targetId"] != target or utc(approval["approvedAtUtc"]) > utc(event["recordedAtUtc"]):
            fail("Approval target/time mismatch")
        path = repository_path(self.root, approval["recordPath"])
        if file_hash(path) != approval["recordSha256"]:
            fail("Approval record checksum mismatch")
        record = load_json(path)
        expected = {"authorityId": approval["authorityId"], "actorId": event["actor"]["id"],
                    "targetId": target, "approvedAtUtc": approval["approvedAtUtc"], "decision": expected_decision,
                    "evidenceSha256": sorted({e["sha256"] for e in event["evidence"] if e["result"] == "pass"})}
        if any(record.get(k) != v for k, v in expected.items()):
            fail("Approval record does not attest this target, decision and exact evidence set")

    def active_records(self):
        return [(key, record) for key, record in self.step_records.items() if record["status"] in ACTIVE]

    def check_claim(self, event):
        if not event["claimedPaths"] or not event["expectedChecks"] or not event["stopConditions"]:
            fail("A work claim requires paths, expected checks and stop conditions")
        for path in event["claimedPaths"]:
            repository_path(self.root, path, must_exist=False)
        owner, task = actor_id(event["actor"]), event["taskId"]
        task_record = self.task_records[task]
        if task_record["owner"] and (task_record["owner"] != owner or task_record["branch"] != event["branch"]):
            fail(f"Task {task} ownership/branch transfer requires an explicit handoff")
        paths = [p.rstrip("/").casefold() for p in event["claimedPaths"]]
        for key, record in self.active_records():
            other_task = self.steps[key]["parent"]
            if other_task == task:
                if record["owner"] != owner or record["branch"] != event["branch"]:
                    fail(f"Task {task} already has another owner/branch")
                continue
            if record["branch"] == event["branch"]:
                fail(f"Branch {event['branch']} is already assigned to {other_task}")
            for claimed in record["claimedPaths"]:
                other = claimed.rstrip("/").casefold()
                if any(p == other or p.startswith(other + "/") or other.startswith(p + "/") for p in paths):
                    fail(f"Path claim overlaps active task {other_task}: {claimed}")

    def require_no_completed_dependents(self, identifiers):
        dependent = []
        for key, step in self.steps.items():
            if self.step_records[key]["status"] == "done" and references(step["eligibility"]) & identifiers:
                dependent.append(key)
        if dependent:
            fail("Reopen completed dependents first: " + ", ".join(dependent))

    def causal_eligibility(self, predicate):
        """A dependency must be true in this event's causal past, not merely
        encountered first by the stable topological tie-breaker."""
        if "id" in predicate:
            key = predicate["id"]
            if not eligible(predicate, self.facts()):
                return False
            records = (self.step_records if key in self.steps else self.task_records if key in self.tasks
                       else self.gates if key in self.gates else self.decisions)
            proof = records[key]["lastEventHash"]
            return proof is not None and proof in self.event_ancestors
        if "allOf" in predicate:
            return all(self.causal_eligibility(p) for p in predicate["allOf"])
        return any(self.causal_eligibility(p) for p in predicate["anyOf"])

    def apply_step(self, event):
        key = event["target"]["id"]
        if key not in self.steps or event["taskId"] != self.steps[key]["parent"]:
            fail(f"Unknown step or wrong parent task: {key}")
        step, record = self.steps[key], self.step_records[key]
        before, after = self.effective_step_status(key), event["toStatus"]
        if event["fromStatus"] != before:
            fail(f"{key}: stale fromStatus {event['fromStatus']}; expected {before}")
        if (before, after) not in STEP_TRANSITIONS.get(event["eventType"], set()):
            fail(f"Illegal step transition: {event['eventType']} {before} -> {after}")
        owner = actor_id(event["actor"])
        if record["owner"] and record["owner"] != owner:
            fail(f"{key}: only its current owner may transition the step")
        if record["owner"] and record["branch"] != event["branch"]:
            fail(f"{key}: branch change requires an explicit handoff")
        if after in {"in_progress", "verification_pending", "done", "ready"} and not eligible(step["eligibility"], self.facts()):
            fail(f"{key}: unmet dependencies {sorted(missing(step['eligibility'], self.facts()))}")
        if after in {"in_progress", "verification_pending", "done", "ready"} and not self.causal_eligibility(step["eligibility"]):
            fail(f"{key}: dependency evidence is missing from the event's causal history")
        if after == "in_progress":
            self.check_claim(event)
        if after == "blocked":
            if not event["blockers"]:
                fail("Blocked transition requires exact blockers and follow-up actions")
        elif event["blockers"]:
            fail("Unblocked state cannot retain unresolved blockers")
        required = ["outcome", "verification"] if after == "done" else (["outcome"] if after == "verification_pending" else [])
        if event["eventType"] == "unblocked":
            required.append("blocker-resolution")
        classes = self.check_evidence(event, required)
        execution_class = step.get("source", {}).get("Execution Class", "agent")
        if after == "done" and execution_class in {"lab_hardware", "mixed", "human"}:
            if not classes & {"physical_test", "human_approval"}:
                fail(f"{key}: synthetic/software evidence cannot complete {execution_class} acceptance")
            self.check_approval(event, "done")
        if before == "done":
            if any(g["status"] == "passed" for g in self.gates.values()) and step["lane"] != "synthetic_development_only":
                self.check_approval(event, "reopened")
            self.require_no_completed_dependents({key, step["parent"]})
            self.task_records[step["parent"]]["status"] = "in_progress"
        record.update(status=after, evidence=copy.deepcopy(event["evidence"]), blockers=copy.deepcopy(event["blockers"]))
        if after == "in_progress":
            record.update(owner=owner, branch=event["branch"], claimedPaths=list(event["claimedPaths"]))
            self.task_records[step["parent"]].update(owner=owner, branch=event["branch"])
        if after == "ready":
            record.update(owner=None, branch=None, claimedPaths=[])
        return record

    def apply_task(self, event):
        key = event["target"]["id"]
        if key not in self.tasks or event["taskId"] != key:
            fail("Unknown task or mismatched taskId")
        record = self.task_records[key]
        status = self.task_status(key)
        if event["fromStatus"] != status:
            fail(f"{key}: stale task status, expected {status}")
        children = [(s, r) for s, r in self.step_records.items() if self.steps[s]["parent"] == key]
        if event["eventType"] == "handoff":
            if event["toStatus"] != status or not event["handoffTo"]:
                fail("A handoff preserves status and names the recipient")
            owned = [(s, r) for s, r in children if r["owner"] and r["status"] != "done"]
            if record["owner"] != actor_id(event["actor"]):
                fail("Only the current task owner can hand off")
            if any(r["branch"] == event["branch"] for s, r in self.active_records() if self.steps[s]["parent"] != key):
                fail("Handoff branch belongs to another active task")
            for _, child in owned:
                child["owner"] = actor_id(event["handoffTo"])
                child["branch"] = event["branch"]
            record.update(owner=actor_id(event["handoffTo"]), branch=event["branch"])
            return record
        if event["eventType"] != "task_completed" or event["toStatus"] != "done" or status != "verification_pending":
            fail("Task closure requires verification_pending -> done")
        if not all(r["status"] == "done" for _, r in children):
            fail(f"{key}: all active atomic steps must be done before task closure")
        if not eligible(self.tasks[key]["eligibility"], self.facts()):
            fail(f"{key}: task dependencies are not satisfied")
        # Closure belongs to the task's actual owner; no implicit completion from
        # supporting DEV tasks, workbook statuses, or finished child counts.
        if record["owner"] != actor_id(event["actor"]) or record["branch"] != event["branch"]:
            fail(f"{key}: task closure requires reconciled step ownership")
        classes = self.check_evidence(event, ["task-acceptance"])
        if self.tasks[key].get("source", {}).get("Execution Class") in {"human", "mixed", "lab_hardware"}:
            if not classes & {"physical_test", "human_approval"}:
                fail("Task closure requires physical/human acceptance evidence")
            self.check_approval(event, "done")
        record.update(status="done", owner=actor_id(event["actor"]), branch=event["branch"],
                      evidence=copy.deepcopy(event["evidence"]), blockers=[])
        return record

    def apply_gate(self, event):
        key = event["target"]["id"]
        if key not in self.gates or event["taskId"] is not None:
            fail("Unknown gate or non-null taskId")
        record = self.gates[key]
        status = record["status"]
        if status == "locked" and (key == "G0" or self.gates["G" + str(int(key[1:]) - 1)]["status"] == "passed"):
            status = "open"
        if event["fromStatus"] != status:
            fail(f"{key}: stale gate status")
        pairs = {"gate_verification_pending": {("open", "verification_pending"), ("failed", "verification_pending")},
                 "gate_passed": {("verification_pending", "passed")},
                 "gate_failed": {("verification_pending", "failed"), ("open", "failed")},
                 "gate_reopened": {("passed", "open")}}
        if (status, event["toStatus"]) not in pairs.get(event["eventType"], set()):
            fail("Illegal gate transition")
        criteria = [c["Criterion ID"] for c in self.catalog["gateCriteria"] if c["Gate"] == key]
        self.check_evidence(event, criteria if event["toStatus"] in {"verification_pending", "passed"} else [])
        if event["toStatus"] in {"passed", "open", "failed"}:
            self.check_approval(event, event["toStatus"])
        if event["toStatus"] == "open":
            if any(r["status"] == "passed" for g, r in self.gates.items() if int(g[1:]) > int(key[1:])):
                fail("Reopen later passed gates first")
            self.require_no_completed_dependents({key})
        record.update(status=event["toStatus"], evidence=copy.deepcopy(event["evidence"]), blockers=copy.deepcopy(event["blockers"]))
        return record

    def apply_decision(self, event):
        key = event["target"]["id"]
        if key not in self.decisions or event["taskId"] is not None:
            fail("Unknown decision or non-null taskId")
        record = self.decisions[key]
        if event["fromStatus"] != record["status"]:
            fail("Stale decision status")
        pairs = {"decision_in_review": {("open", "in_review"), ("informational", "in_review")},
                 "decision_decided": {("in_review", "decided")},
                 "decision_reopened": {("decided", "open")}}
        if (event["fromStatus"], event["toStatus"]) not in pairs.get(event["eventType"], set()):
            fail("Illegal decision transition")
        self.check_evidence(event, ["decision"])
        if event["toStatus"] in {"decided", "open"}:
            self.check_approval(event, event["toStatus"])
        if event["toStatus"] == "open":
            self.require_no_completed_dependents({key})
        record.update(status=event["toStatus"], evidence=copy.deepcopy(event["evidence"]), blockers=[])
        return record

    def apply(self, hashed, event):
        if event["catalogSha256"] != self.catalog_hash or event["planVersion"] != self.catalog["planVersion"]:
            fail("Event catalog/plan provenance mismatch; explicit migration required")
        self.event_ancestors = set(event["causalEventHashes"])
        for parent in event["causalEventHashes"]:
            self.event_ancestors.update(self.ancestors[parent])
        kind = event["target"]["kind"]
        record = {"atomic_step": self.apply_step, "task": self.apply_task,
                  "gate": self.apply_gate, "decision": self.apply_decision}[kind](event)
        record["revision"], record["lastEventHash"] = event["taskRevision"], hashed
        key = chain_key(event)
        self.chains[key] = {"revision": event["taskRevision"], "hash": hashed}
        self.accepted_hashes.add(hashed)
        self.ancestors[hashed] = self.event_ancestors
        if kind in {"task", "atomic_step"}:
            task_record = self.task_records[event["taskId"]]
            task_record["revision"], task_record["lastEventHash"] = event["taskRevision"], hashed
        self._facts = None

    def selection(self, pool):
        facts = self.facts()
        candidates, active, ready, blockers = [], [], [], set()
        for step in sorted(pool, key=lambda s: s["executionOrder"]):
            key = step["id"]
            status = self.effective_step_status(key)
            record = self.step_records[key]
            if status == "done":
                continue
            blockers.update(b["id"] for b in record["blockers"])
            absent = missing(step["eligibility"], facts)
            blockers.update(absent)
            if absent or status == "blocked":
                continue
            if status == "in_progress":
                active.append(key)
            if status == "ready":
                ready.append(key)
            priority = {"in_progress": 0, "verification_pending": 1, "ready": 2}.get(status)
            if priority is not None:
                candidates.append((priority, step["executionOrder"], key))
        primary = min(candidates)[2] if candidates else None
        mode = "verification" if primary and self.effective_step_status(primary) == "verification_pending" else ("working" if primary else "blocked")
        if self.gates["G8"]["status"] == "passed":
            return {"mode": "complete", "primaryStepId": None, "activeStepIds": [], "nextEligibleStepIds": [], "blockingIds": []}
        return {"mode": mode, "primaryStepId": primary, "activeStepIds": active,
                "nextEligibleStepIds": [k for k in ready if k != primary], "blockingIds": sorted(blockers)}

    def state(self, ordered):
        facts = self.facts()
        for key, record in self.step_records.items():
            if record["status"] == "done" and not eligible(self.steps[key]["eligibility"], facts):
                fail(f"Completed step has invalidated dependencies; reopen dependents first: {key}")
        phase = next((i for i in range(9) if self.gates[f"G{i}"]["status"] != "passed"), 8)
        current = {**self.selection([s for s in self.steps.values() if s["phase"] == f"P{phase}"]),
                   "phaseId": f"P{phase}", "gateId": f"G{phase}"}
        execution = self.selection(self.steps.values())
        selected = self.steps.get(execution["primaryStepId"], {})
        execution.update(selectedPhaseId=selected.get("phase"), selectedLane=selected.get("lane"))
        steps = copy.deepcopy(self.step_records)
        for key, record in steps.items():
            record["status"] = self.effective_step_status(key)
        tasks = copy.deepcopy(self.task_records)
        warnings = []
        for key, record in tasks.items():
            record["status"] = self.task_status(key)
            if record["status"] == "not_started" and eligible(self.tasks[key]["eligibility"], facts):
                record["status"] = "ready"
            owned = [(s, r) for s, r in self.active_records() if self.steps[s]["parent"] == key]
            if owned:
                record.update(owner=owned[0][1]["owner"], branch=owned[0][1]["branch"],
                              claimedPaths=sorted({p for _, r in owned for p in r["claimedPaths"]}))
            if record["status"] == "verification_pending" and all(r["status"] == "done" for s, r in steps.items() if self.steps[s]["parent"] == key):
                warnings.append(f"{key}: explicit task acceptance/closure event is pending")
        gates = copy.deepcopy(self.gates)
        for key, record in gates.items():
            if record["status"] == "locked" and int(key[1:]) > 0 and gates[f"G{int(key[1:]) - 1}"]["status"] == "passed":
                record["status"] = "open"
        provenance = self.catalog["provenance"]
        source = provenance["sourceHashes"]
        state = {"$schema": "schemas/progress/progress-state.schema.json", "schemaVersion": 1,
                 "project": PROJECT, "planVersion": self.catalog["planVersion"],
                 "generatedAtUtc": max((e["recordedAtUtc"] for _, e in ordered), key=utc, default=EPOCH),
                 "generatedFrom": {"workbookSha256": source["ELTS_Implementation_and_Progress_Workbook.xlsx"],
                   "specificationSha256": source["ELTS_Repository_Portability_and_Multi_LLM_Architecture_Specification.docx"],
                   "amendmentRulesSha256": provenance["amendmentRulesSha256"], "approvedPlanSha256": provenance["approvedPlanSha256"],
                   "taskCatalogSha256": self.catalog_hash, "authoritiesSha256": digest(self.authorities),
                   "eventCount": len(ordered), "eventSetSha256": digest(sorted(h for h, _ in ordered))},
                 "current": current, "execution": execution,
                 "activeWork": [{"owner": r["owner"], "taskId": self.steps[k]["parent"], "atomicStepId": k,
                                 "branch": r["branch"], "claimedPaths": r["claimedPaths"]} for k, r in self.active_records()],
                 "tasks": tasks, "atomicSteps": steps, "decisions": copy.deepcopy(self.decisions), "gates": gates,
                 "chains": copy.deepcopy(self.chains), "integrity": {"reducerVersion": VERSION, "valid": True, "warnings": warnings}}
        validate_file(state, self.schemas / "progress-state.schema.json")
        return state


def reduce_state(catalog, events, root: Path, authorities=None):
    reducer = Reducer(catalog, root, authorities)
    ordered = ordered_events(events, reducer.schemas)
    for hashed, event in ordered:
        reducer.apply(hashed, event)
    return reducer.state(ordered)
