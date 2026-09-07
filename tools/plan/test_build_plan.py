"""Acceptance fixtures for the approved planning amendment, not project progress."""
import copy
import json
import unittest

import build_plan as plan


class ApprovedPlanTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.rules = json.loads(plan.RULES_PATH.read_text(encoding="utf-8"))
        cls.catalog = plan.build(cls.rules)
        cls.tasks = {t["id"]: t for t in cls.catalog["taskDefinitions"]}
        cls.steps = {s["id"]: s for s in cls.catalog["atomicStepDefinitions"]}

    def test_sources_and_active_identity_counts(self):
        self.assertEqual(self.catalog["activeCounts"], {"workItems": 103, "atomicSteps": 267})
        self.assertEqual(len(self.catalog["supersededBootstrapIds"]), 25)
        self.assertIn("P0.3.S001", self.steps)
        self.assertNotIn("P0.3.S002", self.steps)
        self.assertIn("BOOT.S002", self.steps)
        self.assertEqual(len(self.catalog["gateCriteria"]), 62)
        self.assertEqual(self.catalog["gateCriteria"], self.catalog["baselineRegistries"]["Gate Criteria"])

    def test_changed_baseline_fails_closed(self):
        unapproved = copy.deepcopy(self.rules)
        unapproved["status"] = "proposed"
        with self.assertRaisesRegex(ValueError, "Expected accepted"):
            plan.build(unapproved)
        rules = copy.deepcopy(self.rules)
        name = next(iter(rules["sourceHashes"]))
        rules["sourceHashes"][name] = "0" * 64
        with self.assertRaisesRegex(ValueError, "Baseline hash changed"):
            plan.build(rules)

    def test_config_clock_source_can_bootstrap_without_self_dependency(self):
        facts = {"G0": "passed"}
        self.assertTrue(plan.eligible(self.tasks["P2.1"]["eligibility"], facts))
        self.assertFalse(plan.eligible(self.tasks["P2.2"]["eligibility"], facts))
        facts["P2.1"] = "done"
        self.assertTrue(plan.eligible(self.tasks["P2.2"]["eligibility"], facts))
        self.assertFalse(plan.eligible(self.tasks["P2.9"]["eligibility"], facts))
        facts["P2.2"] = "done"
        self.assertTrue(plan.eligible(self.tasks["P2.9"]["eligibility"], facts))
        self.assertNotIn("P2.9", plan.references(self.tasks["P2.9"]["eligibility"]))

    def test_real_and_synthetic_routes_are_alternatives(self):
        software = {"P2.1": "done", "P2.2": "done"}
        predicate = self.tasks["P2.3"]["eligibility"]
        self.assertFalse(plan.eligible(predicate, software))
        self.assertTrue(plan.eligible(predicate, {**software, "G0": "passed", "P2.9": "done"}))
        self.assertFalse(plan.eligible(predicate, {**software, "G1": "passed"}))
        self.assertTrue(plan.eligible(predicate, {**software, "G1": "passed", **{f"P1.{i}": "done" for i in [5, 6, 7, 8, 9]}}))

    def test_d12_remains_at_rehearsal_not_at_early_interfaces(self):
        for task_id in ["P0.5", "P2.2", "P4.11"]:
            self.assertNotIn("D-12", plan.references(self.tasks[task_id]["eligibility"]))
            for step in self.steps.values():
                if step["parent"] == task_id:
                    self.assertNotIn("D-12", plan.references(step["eligibility"]))
        self.assertIn("D-12", plan.references(self.tasks["P8.4"]["eligibility"]))

    def test_development_allowed_before_g0_but_not_before_progress_bootstrap(self):
        predicate = self.tasks["DEV-01"]["eligibility"]
        self.assertFalse(plan.eligible(predicate, {}))
        self.assertTrue(plan.eligible(predicate, {"BOOT.S011": "done"}))
        self.assertFalse(plan.eligible(self.tasks["P2.1"]["eligibility"], {"BOOT.S011": "done"}))

    def test_unity_work_requires_verified_bootstrap_smoke_tests(self):
        for task in self.rules["developmentTasks"]:
            if task["requiresVerifiedUnity"]:
                self.assertIn("BOOT.S022", plan.references(task["eligibility"]))
        self.assertEqual(self.catalog["unityVersion"], "6000.3.23f1")

    def test_global_work_continues_when_unity_bootstrap_is_blocked(self):
        completed = ["P0.3.S001"] + [f"BOOT.S{i:03}" for i in range(2, 19)]
        facts = {i: "done" for i in completed}
        statuses = {**facts, "BOOT.S019": "blocked"}
        self.assertEqual(plan.select_execution(list(self.steps.values()), facts, statuses), "DEV-01.S001")
        # The helper does not mutate facts, set a gate pass, or complete a parent.
        self.assertEqual(facts, {i: "done" for i in completed})
        self.assertNotIn("G0", facts)

    def test_development_done_never_implies_baseline_acceptance(self):
        facts = {f"DEV-{i:02}": "done" for i in range(1, 13)}
        before = dict(facts)
        self.assertFalse(plan.eligible(self.tasks["P8.4"]["eligibility"], facts))
        self.assertEqual(facts, before)
        for task in self.rules["developmentTasks"]:
            self.assertTrue(task["completionDoesNotAdvanceParentsOrGates"])
            self.assertTrue(task["deferredPhysicalEvidence"])

    def test_cycles_and_unknown_references_are_rejected(self):
        with self.assertRaisesRegex(ValueError, "Dependency cycle"):
            plan.assert_acyclic({"A": {"B"}, "B": {"A"}})
        rules = copy.deepcopy(self.rules)
        rules["developmentTasks"][0]["eligibility"] = plan.fact("DEV-01")
        with self.assertRaisesRegex(ValueError, "Dependency cycle"):
            plan.build(rules)
        rules["developmentTasks"][0]["eligibility"] = plan.fact("DEV-99")
        with self.assertRaisesRegex(ValueError, "Unresolved effective dependency"):
            plan.build(rules)

    def test_baseline_import_matches_prior_independent_office_extraction(self):
        # This independent extraction was made during the original review using
        # python-docx/openpyxl. Compare all non-formula registry content to detect
        # mistakes in the portable OOXML reader. The original XLSX hash is pinned.
        prior = json.loads((plan.ROOT / "docs/reviews/2026-09-06/workbook-extract.json").read_text())
        current = self.catalog["baselineWorkbookSheets"]
        for name in self.rules["baselineCounts"]:
            for row_no, row in enumerate(prior[name]):
                for col, value in enumerate(row):
                    actual_row = current[name][row_no] if row_no < len(current[name]) else []
                    actual = actual_row[col] if col < len(actual_row) else None
                    if isinstance(value, str) and value.startswith("="):
                        self.assertIsInstance(actual, dict)
                        self.assertEqual(actual["formula"], value)
                    else:
                        self.assertEqual(actual, value, (name, row_no + 1, col + 1))


if __name__ == "__main__":
    unittest.main()
