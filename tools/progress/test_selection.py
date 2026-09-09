"""Development-lane and authorized-gate fixtures using only temporary evidence."""
import copy
import json
import tempfile
import unittest
from datetime import datetime, timezone
from pathlib import Path

from core import file_hash, reduce_state
from schema import ValidationError
from store import make_event
from test_core import History, fixture_root


class SelectionTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(prefix="ELTS selection fixture ")
        self.root = Path(self.tmp.name) / "test repository"
        fixture_root(self.root)
        self.h = History(self.root)

    def tearDown(self):
        self.tmp.cleanup()

    def bootstrap(self):
        for target in ["P0.3.S001"] + [f"BOOT.S{i:03}" for i in range(2, 12)]:
            self.h.done(target)

    def test_dev_continues_while_bootstrap_blocked_without_parent_or_gate_credit(self):
        self.bootstrap()
        self.h.add("BOOT.S012", "blocked", "blocked", blockers=[{"id": "EXT-fixture", "reason": "Simulated bootstrap obstacle",
                    "requiredAction": "Resolve fixture obstacle", "responsibleRole": "test owner"}])
        state = self.h.state()
        self.assertEqual(state["current"]["phaseId"], "P0")
        self.assertEqual(state["execution"]["primaryStepId"], "DEV-01.S001")
        self.assertEqual(state["execution"]["selectedPhaseId"], "DEV")
        self.assertEqual(state["execution"]["selectedLane"], "synthetic_development_only")
        self.h.done("DEV-01.S001", branch="fixture/dev01", claimedPaths=["config/defaults/"])
        self.h.done("DEV-01.V001", branch="fixture/dev01", claimedPaths=["config/defaults/"])
        state = self.h.state()
        self.assertEqual(state["tasks"]["DEV-01"]["status"], "verification_pending")
        self.assertNotEqual(state["atomicSteps"]["DEV-02.S001"]["status"], "ready")
        state = self.h.add("DEV-01", "task_completed", "done", kind="task", branch="fixture/dev01",
                           evidence=[self.h.evidence("DEV-01", ["task-acceptance"])])
        self.assertEqual(state["execution"]["primaryStepId"], "DEV-02.S001")
        self.assertNotEqual(state["tasks"]["P2.1"]["status"], "done")
        self.assertEqual(state["gates"]["G0"]["status"], "open")
        self.assertNotEqual(state["execution"]["mode"], "complete")

    def test_foreign_chain_dependencies_require_causal_proof(self):
        self.bootstrap()
        event = make_event(self.h.catalog, self.h.events, self.root,
                           self.h.request("DEV-01.S001", "task_started", "in_progress", branch="fixture/dev01", claimedPaths=["config/"]))
        event["causalEventHashes"] = []
        with self.assertRaises(ValidationError):
            reduce_state(self.h.catalog, self.h.events + [event], self.root)

    def enroll_fixture_human(self):
        path = self.root / "fixture-authority.json"
        path.write_text('{"fixtureOnly":true,"purpose":"test human approval enforcement"}', encoding="utf-8")
        registry = {"schemaVersion": 1, "description": "UNIT TEST ONLY - no real authority",
                    "humans": {"fixture-authority": {"actorId": "fixture-human", "allowedTargets": [f"G{i}" for i in range(9)],
                    "authorityRecordPath": path.name, "authorityRecordSha256": file_hash(path)}}}
        (self.root / "project-management/progress/authorities.json").write_text(json.dumps(registry), encoding="utf-8")
        # Test seam only. No CLI or production event can select this verifier.
        self.h.approval_verifier = lambda event, receipt, authority: True

    def approval(self, target, evidence, decision="passed"):
        now = datetime.now(timezone.utc).isoformat()
        receipt = {"authorityId": "fixture-authority", "actorId": "fixture-human", "targetId": target,
                   "approvedAtUtc": now, "decision": decision, "evidenceSha256": sorted({e["sha256"] for e in evidence})}
        path = self.root / (target + "-fixture-approval.json")
        path.write_text(json.dumps(receipt), encoding="utf-8")
        return {"authorityId": "fixture-authority", "recordPath": path.name, "recordSha256": file_hash(path),
                "targetId": target, "approvedAtUtc": now}

    def test_only_explicit_authorized_g8_event_can_complete_project(self):
        self.enroll_fixture_human()
        human = {"type": "human", "id": "fixture-human", "tool": "isolated unittest"}
        for number in range(9):
            target = f"G{number}"
            criteria = [c["Criterion ID"] for c in self.h.catalog["gateCriteria"] if c["Gate"] == target]
            proof = self.h.evidence(target, criteria, "human_approval")
            self.h.add(target, "gate_verification_pending", "verification_pending", kind="gate", evidence=[proof])
            self.assertNotEqual(self.h.state()["current"]["mode"], "complete")
            approved = self.approval(target, [proof])
            state = self.h.add(target, "gate_passed", "passed", kind="gate", actor=human, evidence=[proof], approval=approved)
        self.assertEqual(state["current"]["mode"], "complete")
        self.assertEqual(state["execution"]["mode"], "complete")
        self.assertIsNone(state["execution"]["primaryStepId"])
        self.assertEqual(state, reduce_state(self.h.catalog, list(reversed(self.h.events)), self.root,
                                            approval_verifier=self.h.approval_verifier))

    def test_self_enrollment_and_receipt_do_not_authenticate_human(self):
        self.enroll_fixture_human()
        self.h.approval_verifier = None  # Production behavior.
        criteria = [c["Criterion ID"] for c in self.h.catalog["gateCriteria"] if c["Gate"] == "G0"]
        proof = self.h.evidence("G0", criteria, "physical_test")
        self.h.add("G0", "gate_verification_pending", "verification_pending", kind="gate", evidence=[proof])
        with self.assertRaisesRegex(ValidationError, "Privileged approvals unavailable"):
            self.h.add("G0", "gate_passed", "passed", kind="gate",
                       actor={"type": "human", "id": "fixture-human", "tool": "unittest"},
                       evidence=[proof], approval=self.approval("G0", [proof]))

    def test_gate_requires_predecessor_in_causal_history(self):
        self.enroll_fixture_human()
        human = {"type": "human", "id": "fixture-human", "tool": "unittest"}
        criteria = [c["Criterion ID"] for c in self.h.catalog["gateCriteria"] if c["Gate"] == "G0"]
        proof = self.h.evidence("G0", criteria, "human_approval")
        self.h.add("G0", "gate_verification_pending", "verification_pending", kind="gate", evidence=[proof])
        self.h.add("G0", "gate_passed", "passed", kind="gate", actor=human,
                   evidence=[proof], approval=self.approval("G0", [proof]))
        criteria = [c["Criterion ID"] for c in self.h.catalog["gateCriteria"] if c["Gate"] == "G1"]
        request = self.h.request("G1", "gate_verification_pending", "verification_pending", kind="gate",
                                 evidence=[self.h.evidence("G1", criteria)])
        candidate = make_event(self.h.catalog, self.h.events, self.root, request, state=self.h.state())
        candidate["causalEventHashes"] = []
        with self.assertRaises(ValidationError):
            reduce_state(self.h.catalog, self.h.events + [candidate], self.root, approval_verifier=self.h.approval_verifier)

    def test_approval_receipt_must_bind_exact_evidence_and_enrolled_target(self):
        self.enroll_fixture_human()
        criteria = [c["Criterion ID"] for c in self.h.catalog["gateCriteria"] if c["Gate"] == "G0"]
        proof = self.h.evidence("G0", criteria, "human_approval")
        self.h.add("G0", "gate_verification_pending", "verification_pending", kind="gate", evidence=[proof])
        approval = self.approval("G0", [proof])
        for field, value in (("authorityId", "unenrolled"), ("targetId", "G1"), ("recordSha256", "0" * 64)):
            bad = {**approval, field: value}
            with self.subTest(field=field), self.assertRaises(ValidationError):
                self.h.add("G0", "gate_passed", "passed", kind="gate",
                           actor={"type": "human", "id": "fixture-human", "tool": "unittest"}, evidence=[proof], approval=bad)


if __name__ == "__main__":
    unittest.main()
