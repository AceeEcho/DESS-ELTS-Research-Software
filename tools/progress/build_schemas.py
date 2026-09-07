"""Generate the version-1 local progress contracts. --check detects drift."""
from __future__ import annotations
import argparse
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCHEMAS = ROOT / "schemas/progress"
TEXT = {"type": "string", "minLength": 1}
SHA = {"type": "string", "pattern": "^[0-9a-f]{64}$"}
TIME = {"type": "string", "format": "date-time"}
PATH = {"type": "string", "minLength": 1, "pattern": r"^(?!/)(?!.*\\)(?!.*:)(?!.*(?:^|/)\.\.(?:/|$)).+$"}
ID = {"type": "string", "pattern": r"^(?:P\d+\.\d+(?:\.[SV]\d{3})?|BOOT\.S\d{3}|DEV-\d{2}(?:\.[SV]\d{3})?|G\d(?:\.\d{2})?|D-\d{2}|PC-\d{3})$"}


def array(item, minimum=0):
    return {"type": "array", "items": item, "minItems": minimum, "uniqueItems": True}


def obj(props, optional=()):
    return {"type": "object", "properties": props,
            "required": [p for p in props if p not in optional], "additionalProperties": False}


def ref(name):
    return {"$ref": name + ".schema.json"}


def contracts():
    actor = obj({"type": {"enum": ["agent", "human"]}, "id": TEXT, "tool": TEXT})
    blocker = obj({"id": TEXT, "reason": TEXT, "requiredAction": TEXT, "responsibleRole": TEXT})
    evidence = obj({"id": TEXT, "path": PATH, "sha256": SHA, "targetIds": array(ID, 1),
                    "criteria": array(TEXT, 1),
                    "class": {"enum": ["document", "source", "automated_test", "synthetic_test",
                                        "toolchain_runtime", "physical_test", "human_approval"]},
                    "command": TEXT, "result": {"enum": ["pass", "fail", "deferred"]},
                    "sourceRevision": TEXT, "recordedAtUtc": TIME, "limitations": array(TEXT)})
    approval = obj({"authorityId": TEXT, "recordPath": PATH, "recordSha256": SHA,
                    "targetId": ID, "approvedAtUtc": TIME})
    event = obj({"$schema": TEXT, "schemaVersion": {"const": 1}, "eventId": {"type": "string", "pattern": "^[0-9a-f-]{36}$"},
                 "eventType": {"enum": ["step_ready", "task_started", "verification_pending", "step_completed",
                                            "task_completed", "blocked", "unblocked", "reopened", "handoff",
                                            "gate_verification_pending", "gate_passed", "gate_failed", "gate_reopened",
                                            "decision_in_review", "decision_decided", "decision_reopened"]},
                 "recordedAtUtc": TIME, "occurredAtUtc": TIME, "actor": actor,
                 "planVersion": TEXT, "catalogSha256": SHA,
                 "target": obj({"kind": {"enum": ["atomic_step", "task", "gate", "decision"]}, "id": ID}),
                 "taskId": {"type": ["string", "null"]}, "fromStatus": TEXT, "toStatus": TEXT,
                 "taskRevision": {"type": "integer", "minimum": 1},
                 "previousTaskEventHash": {"anyOf": [SHA, {"type": "null"}]},
                 "causalEventHashes": array(SHA), "branch": TEXT, "claimedPaths": array(PATH),
                 "expectedChecks": array(TEXT), "stopConditions": array(TEXT),
                 "evidence": array(ref("evidence")), "blockers": array(blocker), "note": TEXT,
                 "approval": {"anyOf": [approval, {"type": "null"}]},
                 "handoffTo": {"anyOf": [actor, {"type": "null"}]}})
    selector = obj({"mode": {"enum": ["working", "blocked", "verification", "complete"]},
                    "primaryStepId": {"anyOf": [ID, {"type": "null"}]},
                    "activeStepIds": array(ID), "nextEligibleStepIds": array(ID), "blockingIds": array(TEXT)})
    current = {**selector, "properties": {**selector["properties"], "phaseId": TEXT, "gateId": TEXT},
               "required": selector["required"] + ["phaseId", "gateId"]}
    execution = {**selector, "properties": {**selector["properties"],
                 "selectedPhaseId": {"type": ["string", "null"]}, "selectedLane": {"type": ["string", "null"]}},
                 "required": selector["required"] + ["selectedPhaseId", "selectedLane"]}
    record = obj({"status": TEXT, "revision": {"type": "integer", "minimum": 0},
                  "lastEventHash": {"anyOf": [SHA, {"type": "null"}]},
                  "owner": {"type": ["string", "null"]}, "branch": {"type": ["string", "null"]},
                  "claimedPaths": array(PATH), "evidence": array(ref("evidence")), "blockers": array(blocker)})
    records = {"type": "object", "additionalProperties": record}
    state = obj({"$schema": TEXT, "schemaVersion": {"const": 1}, "project": TEXT, "planVersion": TEXT,
                 "generatedAtUtc": TIME, "generatedFrom": obj({"workbookSha256": SHA, "specificationSha256": SHA,
                 "amendmentRulesSha256": SHA, "approvedPlanSha256": SHA, "taskCatalogSha256": SHA,
                 "authoritiesSha256": SHA, "eventCount": {"type": "integer", "minimum": 0}, "eventSetSha256": SHA}),
                 "current": current, "execution": execution,
                 "activeWork": array(obj({"owner": TEXT, "taskId": ID, "atomicStepId": ID,
                 "branch": TEXT, "claimedPaths": array(PATH)})),
                 "tasks": records, "atomicSteps": records, "decisions": records, "gates": records,
                 "chains": {"type": "object", "additionalProperties": obj({"revision": {"type": "integer", "minimum": 1}, "hash": SHA})},
                 "integrity": obj({"reducerVersion": TEXT, "valid": {"const": True}, "warnings": array(TEXT)})})
    # Imported source fields are preserved verbatim; the importer reconciles them
    # byte-for-byte/structurally with the accepted plan rather than interpreting prose.
    predicate = {"anyOf": [obj({"id": ID, "state": {"enum": ["done", "passed", "decided"]}}),
                            obj({"allOf": array({"$ref": "#/$defs/predicate"})}),
                            obj({"anyOf": array({"$ref": "#/$defs/predicate"}, 1)})]}
    definition = {"type": "object", "required": ["id", "phase", "lane", "eligibility"],
                  "properties": {"id": ID, "phase": TEXT, "lane": TEXT, "eligibility": predicate}}
    catalog = obj({"$schema": TEXT, "schemaVersion": {"const": 1}, "project": TEXT, "planVersion": TEXT,
                   "provenance": obj({"sourceHashes": {"type": "object", "additionalProperties": SHA},
                   "amendmentId": {"const": "PC-001"}, "amendmentRulesSha256": SHA, "approvedPlanSha256": SHA}),
                   "activeCounts": obj({"workItems": {"const": 103}, "atomicSteps": {"const": 267}}),
                   "tasks": array(definition, 103), "atomicSteps": array(definition, 267),
                   "phases": array({"type": "object"}, 9), "decisions": array({"type": "object"}, 18),
                   "gateCriteria": array({"type": "object"}, 62), "requirements": array({"type": "object"}, 108),
                   "artifacts": array({"type": "object"}, 27), "risks": array({"type": "object"}, 26),
                   "dependencies": array({"type": "object"}), "bootstrapMappings": {"type": "object"},
                   "supersededBootstrapIds": array(ID, 25), "replacedDependencyIds": array(TEXT),
                   "progressSelection": {"type": "object"}})
    catalog["$defs"] = {"predicate": predicate}
    handoff = obj({"schemaVersion": {"const": 1}, "recordedAtUtc": TIME, "actor": actor,
                   "branch": TEXT, "sourceRevision": TEXT, "completedIds": array(ID),
                   "changedPaths": array(PATH), "checks": array(TEXT), "evidence": array(ref("evidence")),
                   "blockers": array(blocker), "currentGate": TEXT,
                   "nextStepId": {"anyOf": [ID, {"type": "null"}]}, "nextAction": TEXT})
    change = obj({"schemaVersion": {"const": 1}, "id": ID, "status": {"enum": ["proposed", "accepted", "rejected", "superseded"]},
                  "affectedIds": array(ID, 1), "reason": TEXT, "proposal": TEXT,
                  "authorityReference": PATH, "approval": {"anyOf": [approval, {"type": "null"}]}})
    authorities = obj({"schemaVersion": {"const": 1}, "description": TEXT,
                       "humans": {"type": "object", "additionalProperties": obj({"actorId": TEXT, "allowedTargets": array(ID, 1),
                       "authorityRecordPath": PATH, "authorityRecordSha256": SHA})}})
    return {"evidence": evidence, "progress-event": event, "progress-state": state,
            "task-catalog": catalog, "handoff": handoff, "plan-change": change, "authorities": authorities}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    SCHEMAS.mkdir(parents=True, exist_ok=True)
    for name, body in contracts().items():
        body = {"$schema": "https://json-schema.org/draft/2020-12/schema", "title": f"ELTS {name} v1", **body}
        text = json.dumps(body, indent=2) + "\n"
        path = SCHEMAS / (name + ".schema.json")
        if args.check:
            if not path.exists() or path.read_text(encoding="utf-8") != text:
                raise SystemExit(f"Schema drift: {path.name}")
        else:
            path.write_text(text, encoding="utf-8", newline="\n")
    print("PASS: seven progress contracts " + ("match generator" if args.check else "generated"))


if __name__ == "__main__":
    main()
