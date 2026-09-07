"""Isolated adversarial fixtures; never emit a production progress event."""
from __future__ import annotations

import copy
import json
import shutil
import tempfile
import unittest
from datetime import datetime, timezone
from pathlib import Path
from unittest.mock import patch

from core import digest, file_hash, reduce_state
from schema import ValidationError
from store import append_event, generate, json_bytes, make_event, publish_new, writer_lock

ROOT = Path(__file__).resolve().parents[2]
ACTOR = {"type": "agent", "id": "fixture-agent", "tool": "unittest"}
BRANCH = "fixture/bootstrap"


def fixture_root(destination):
    destination.mkdir(parents=True, exist_ok=True)
    for folder in ("schemas/progress", "tools/plan", "docs/plan", "deliverables", "project-management/baseline"):
        shutil.copytree(ROOT / folder, destination / folder, ignore=shutil.ignore_patterns("__pycache__"))
    (destination / "project-management/progress").mkdir(parents=True)
    shutil.copyfile(ROOT / "project-management/task-catalog.json", destination / "project-management/task-catalog.json")
    shutil.copyfile(ROOT / "project-management/progress/authorities.json", destination / "project-management/progress/authorities.json")
    (destination / "fixture-evidence.json").write_text('{"fixtureOnly":true,"result":"pass"}\n', encoding="utf-8")


class History:
    def __init__(self, root):
        self.root = root
        self.catalog = json.loads((root / "project-management/task-catalog.json").read_text(encoding="utf-8"))
        self.events = []
        self.approval_verifier = None

    def state(self):
        return reduce_state(self.catalog, self.events, self.root, approval_verifier=self.approval_verifier)

    def evidence(self, target, criteria=("outcome", "verification"), kind="automated_test"):
        return {"id": "fixture-proof", "path": "fixture-evidence.json", "sha256": file_hash(self.root / "fixture-evidence.json"),
                "targetIds": [target], "criteria": list(criteria), "class": kind,
                "command": "isolated unittest fixture", "result": "pass", "sourceRevision": "fixture-snapshot",
                "recordedAtUtc": datetime.now(timezone.utc).isoformat(), "limitations": ["Synthetic unit-test evidence only"]}

    def request(self, target, event_type, status, **extra):
        kind = extra.pop("kind", "atomic_step")
        return {"target": {"kind": kind, "id": target}, "eventType": event_type,
                "toStatus": status, "actor": ACTOR, "branch": BRANCH,
                "claimedPaths": ["tools/progress/"], "expectedChecks": ["isolated fixtures"],
                "stopConditions": ["contract violation"], "note": "Isolated fixture; not observed production work", **extra}

    def add(self, target, event_type, status, **extra):
        request = self.request(target, event_type, status, **extra)
        event = make_event(self.catalog, self.events, self.root, request, state=self.state())
        state = reduce_state(self.catalog, self.events + [event], self.root, approval_verifier=self.approval_verifier)
        self.events.append(event)
        return state

    def done(self, target, **extra):
        self.add(target, "task_started", "in_progress", **extra)
        self.add(target, "verification_pending", "verification_pending", evidence=[self.evidence(target)], **extra)
        return self.add(target, "step_completed", "done", evidence=[self.evidence(target)], **extra)


class CoreTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="ELTS progress fixture ")
        self.root = Path(self.temporary.name) / "path with spaces"
        fixture_root(self.root)
        self.h = History(self.root)

    def tearDown(self):
        self.temporary.cleanup()

    def test_baseline_and_active_verification_done_selection(self):
        self.assertEqual(self.h.state()["execution"]["primaryStepId"], "P0.3.S001")
        state = self.h.add("P0.3.S001", "task_started", "in_progress")
        self.assertEqual(state["activeWork"][0]["owner"], "agent:fixture-agent")
        state = self.h.add("P0.3.S001", "verification_pending", "verification_pending", evidence=[self.h.evidence("P0.3.S001")])
        self.assertEqual(state["execution"]["mode"], "verification")
        state = self.h.add("P0.3.S001", "step_completed", "done", evidence=[self.h.evidence("P0.3.S001")])
        self.assertEqual(state["execution"]["primaryStepId"], "BOOT.S002")
        self.assertNotEqual(state["tasks"]["P0.3"]["status"], "done")
        self.assertEqual(state["gates"]["G0"]["status"], "open")

    def test_chain_conflict_duplicate_and_out_of_order_files(self):
        self.h.done("P0.3.S001")
        self.assertEqual(self.h.state(), reduce_state(self.h.catalog, list(reversed(self.h.events)), self.root))
        conflict = copy.deepcopy(self.h.events[0])
        conflict["eventId"] = "00000000-0000-0000-0000-000000000001"
        with self.assertRaisesRegex(ValidationError, "Conflicting"):
            reduce_state(self.h.catalog, self.h.events + [conflict], self.root)
        with self.assertRaisesRegex(ValidationError, "Duplicate event"):
            reduce_state(self.h.catalog, self.h.events + [self.h.events[0]], self.root)

    def test_missing_dependency_and_superseded_id_rejected(self):
        with self.assertRaisesRegex(ValidationError, "Illegal step transition|unmet dependencies"):
            self.h.add("BOOT.S002", "task_started", "in_progress")
        with self.assertRaisesRegex(ValidationError, "Unknown active"):
            self.h.add("P0.3.S002", "task_started", "in_progress")

    def test_no_skip_no_missing_evidence_no_early_task_closure(self):
        self.h.add("P0.3.S001", "task_started", "in_progress")
        with self.assertRaisesRegex(ValidationError, "Illegal step transition"):
            self.h.add("P0.3.S001", "step_completed", "done", evidence=[self.h.evidence("P0.3.S001")])
        with self.assertRaisesRegex(ValidationError, "Acceptance evidence missing"):
            self.h.add("P0.3.S001", "verification_pending", "verification_pending")
        with self.assertRaises(ValidationError):
            self.h.add("P0.3", "task_completed", "done", kind="task", evidence=[self.h.evidence("P0.3", ["task-acceptance"])])

    def test_evidence_hash_binding_result_and_path(self):
        self.h.add("P0.3.S001", "task_started", "in_progress")
        for field, value in (("sha256", "0" * 64), ("targetIds", ["BOOT.S002"]),
                             ("result", "fail"), ("path", "../outside.json")):
            proof = self.h.evidence("P0.3.S001")
            proof[field] = value
            with self.subTest(field=field), self.assertRaises(ValidationError):
                self.h.add("P0.3.S001", "verification_pending", "verification_pending", evidence=[proof])

    def test_owner_branch_and_overlapping_path_conflict(self):
        self.h.add("P0.3.S001", "task_started", "in_progress")
        other = {"type": "agent", "id": "other", "tool": "unittest"}
        with self.assertRaisesRegex(ValidationError, "current owner"):
            self.h.add("P0.3.S001", "verification_pending", "verification_pending", actor=other, evidence=[self.h.evidence("P0.3.S001")])
        with self.assertRaisesRegex(ValidationError, "Path claim overlaps"):
            self.h.add("P0.4.S001", "task_started", "in_progress", branch="fixture/mirror", claimedPaths=["TOOLS/PROGRESS/child.py"])
        with self.assertRaisesRegex(ValidationError, "Branch"):
            self.h.add("P0.4.S001", "task_started", "in_progress", claimedPaths=["docs/mirror/"])

    def test_block_and_unblock_require_followup_and_evidence(self):
        with self.assertRaisesRegex(ValidationError, "exact blockers"):
            self.h.add("P0.3.S001", "blocked", "blocked")
        self.h.add("P0.3.S001", "blocked", "blocked", blockers=[{"id": "EXT-fixture", "reason": "fixture unavailable",
                   "requiredAction": "Provide fixture", "responsibleRole": "test owner"}])
        self.assertIn("EXT-fixture", self.h.state()["execution"]["blockingIds"])
        with self.assertRaisesRegex(ValidationError, "Acceptance evidence"):
            self.h.add("P0.3.S001", "unblocked", "ready")
        state = self.h.add("P0.3.S001", "unblocked", "ready", evidence=[self.h.evidence("P0.3.S001", ["blocker-resolution"])])
        self.assertEqual(state["execution"]["primaryStepId"], "P0.3.S001")

    def test_reopen_leaf_and_protect_completed_dependents(self):
        self.h.done("P0.3.S001")
        self.h.add("P0.3.S001", "reopened", "in_progress")
        self.h.add("P0.3.S001", "verification_pending", "verification_pending", evidence=[self.h.evidence("P0.3.S001")])
        self.h.add("P0.3.S001", "step_completed", "done", evidence=[self.h.evidence("P0.3.S001")])
        self.h.done("BOOT.S002")
        with self.assertRaisesRegex(ValidationError, "Reopen completed dependents"):
            self.h.add("P0.3.S001", "reopened", "in_progress")

    def test_handoff_is_explicit_and_previous_owner_loses_claim(self):
        self.h.add("P0.3.S001", "task_started", "in_progress")
        other = {"type": "agent", "id": "recipient", "tool": "unittest"}
        self.h.add("P0.3", "handoff", "in_progress", kind="task", handoffTo=other, branch="fixture/recipient")
        with self.assertRaisesRegex(ValidationError, "current owner"):
            self.h.add("P0.3.S001", "verification_pending", "verification_pending", evidence=[self.h.evidence("P0.3.S001")])
        state = self.h.add("P0.3.S001", "verification_pending", "verification_pending", actor=other,
                          branch="fixture/recipient", evidence=[self.h.evidence("P0.3.S001")])
        self.assertEqual(state["activeWork"][0]["owner"], "agent:recipient")

    def test_gate_cannot_pass_without_all_criteria_and_human_authority(self):
        with self.assertRaisesRegex(ValidationError, "Acceptance evidence"):
            self.h.add("G0", "gate_verification_pending", "verification_pending", kind="gate")
        criteria = [c["Criterion ID"] for c in self.h.catalog["gateCriteria"] if c["Gate"] == "G0"]
        proof = self.h.evidence("G0", criteria)
        self.h.add("G0", "gate_verification_pending", "verification_pending", kind="gate", evidence=[proof])
        with self.assertRaisesRegex(ValidationError, "authorized human"):
            self.h.add("G0", "gate_passed", "passed", kind="gate", evidence=[proof])
        self.assertNotEqual(self.h.state()["current"]["mode"], "complete")

    def test_synthetic_evidence_cannot_close_physical_step(self):
        self.h.add("P0.2.S001", "task_started", "in_progress", branch="fixture/pc", claimedPaths=["docs/environment/"])
        self.h.add("P0.2.S001", "verification_pending", "verification_pending", branch="fixture/pc", evidence=[self.h.evidence("P0.2.S001")])
        with self.assertRaisesRegex(ValidationError, "synthetic/software"):
            self.h.add("P0.2.S001", "step_completed", "done", branch="fixture/pc", evidence=[self.h.evidence("P0.2.S001")])

    def test_writer_atomicity_no_overwrite_and_stale_state_detection(self):
        request = self.h.request("P0.3.S001", "task_started", "in_progress")
        event, state = append_event(self.root, request)
        self.assertEqual(generate(self.root), state)
        state_bytes = (self.root / "PROJECT_STATE.json").read_bytes()
        generate(self.root, write=True)
        self.assertEqual((self.root / "PROJECT_STATE.json").read_bytes(), state_bytes)
        path = next((self.root / "project-management/progress/events").rglob("*.json"))
        with self.assertRaises(FileExistsError):
            publish_new(path, b"overwrite")
        self.assertEqual(json.loads(path.read_text()), event)
        (self.root / "PROJECT_STATE.json").write_text('{}', encoding="utf-8")
        with self.assertRaisesRegex(ValidationError, "state is stale"):
            append_event(self.root, request)
        generate(self.root, write=True)
        self.assertEqual((self.root / "PROJECT_STATE.json").read_bytes(), state_bytes)

    def test_writer_lock_and_failed_candidate_leave_no_event(self):
        request = self.h.request("BOOT.S002", "task_started", "in_progress")
        with self.assertRaises(ValidationError):
            append_event(self.root, request)
        self.assertFalse((self.root / "project-management/progress/events").exists())
        with writer_lock(self.root), self.assertRaisesRegex(ValidationError, "locked"):
            append_event(self.root, self.h.request("P0.3.S001", "task_started", "in_progress"))

    def test_crash_after_event_publication_recovers_without_duplicate(self):
        request = self.h.request("P0.3.S001", "task_started", "in_progress")
        with patch("store.replace_generated", side_effect=OSError("simulated disk failure")):
            with self.assertRaisesRegex(ValidationError, "Event was published"):
                append_event(self.root, request)
        state = generate(self.root, write=True)
        self.assertEqual(state["generatedFrom"]["eventCount"], 1)
        self.assertEqual(state["atomicSteps"]["P0.3.S001"]["status"], "in_progress")


if __name__ == "__main__":
    unittest.main()
