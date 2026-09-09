"""Contract tests for the BOOT.S006 planning catalog importer."""
from __future__ import annotations

import json
import os
import shutil
import tempfile
import unittest
from pathlib import Path

from tools.progress.import_plan import _verify_sources, build_catalog, export_outputs

ROOT = Path(__file__).resolve().parents[2]


def copy_import_inputs(destination: Path) -> Path:
    """Copy only the importer contract inputs into a path containing spaces."""
    root = destination / "project copy with spaces"
    for relative in ("deliverables", "project-management/baseline", "docs/plan",
                     "tools/plan", "schemas/progress"):
        shutil.copytree(ROOT / relative, root / relative)
    return root


class ImportPlanTests(unittest.TestCase):
    def test_catalog_counts_ids_and_does_not_import_status_as_progress(self):
        catalog = build_catalog(ROOT)
        self.assertEqual(catalog["activeCounts"], {"workItems": 103, "atomicSteps": 267})
        self.assertEqual(len({x["id"] for x in catalog["tasks"]}), 103)
        self.assertEqual(len({x["id"] for x in catalog["atomicSteps"]}), 267)
        self.assertTrue(all("Status" not in x for x in catalog["tasks"]))
        self.assertEqual(catalog["provenance"]["amendmentId"], "PC-001")
        approved = json.loads((ROOT / "docs/plan/approved-plan.json").read_text(encoding="utf-8"))
        self.assertEqual(catalog["tasks"], approved["taskDefinitions"])
        self.assertEqual(catalog["atomicSteps"], approved["atomicStepDefinitions"])

    def test_source_and_baseline_tamper_rejected(self):
        rules = json.loads((ROOT / "docs/plan/amendment-rules.json").read_text(encoding="utf-8"))
        with tempfile.TemporaryDirectory() as temporary:
            base = Path(temporary)
            (base / "deliverables").mkdir()
            (base / "project-management/baseline").mkdir(parents=True)
            for name in rules["sourceHashes"]:
                source = ROOT / "deliverables" / name
                (base / "deliverables" / name).write_bytes(source.read_bytes())
                (base / "project-management/baseline" / name).write_bytes(source.read_bytes())
            sums = "".join(f"{value}  {name}\n" for name, value in rules["sourceHashes"].items())
            (base / "project-management/baseline/SHA256SUMS").write_text(sums, encoding="utf-8")
            _verify_sources(base, rules)
            target = base / "project-management/baseline" / next(iter(rules["sourceHashes"]))
            target.write_bytes(target.read_bytes() + b"tamper")
            with self.assertRaisesRegex(ValueError, "Immutable baseline"):
                _verify_sources(base, rules)
            source_target = base / "deliverables" / next(iter(rules["sourceHashes"]))
            source_target.write_bytes(source_target.read_bytes() + b"tamper")
            with self.assertRaisesRegex(ValueError, "Source hash"):
                _verify_sources(base, rules)

    def test_exports_are_repeatable_and_formulas_are_preserved(self):
        with tempfile.TemporaryDirectory(prefix="catalog importer ") as temporary:
            root = copy_import_inputs(Path(temporary))
            first = export_outputs(root)
            snapshots = {path: path.read_bytes() for path in first}
            second = export_outputs(root, check=True)
            self.assertEqual(first, second)
            self.assertTrue(all(path.read_bytes() == snapshots[path] for path in first))
            sheets = json.loads((root / "project-management/exports/workbook-dashboard.json").read_text(encoding="utf-8"))
            formula_cells = [cell for row in sheets for cell in row if isinstance(cell, dict) and "formula" in cell]
            self.assertTrue(formula_cells)
            self.assertTrue(any(cell["formula"].startswith("=") for cell in formula_cells))
            original_cwd = Path.cwd()
            try:
                os.chdir(root)  # path resolution must not depend on caller's cwd
                self.assertEqual(build_catalog(root)["planVersion"], "ELTS-build-plan-1.0+PC-001")
            finally:
                os.chdir(original_cwd)

    def test_missing_and_stale_exports_are_rejected(self):
        with tempfile.TemporaryDirectory(prefix="catalog importer ") as temporary:
            root = copy_import_inputs(Path(temporary))
            outputs = export_outputs(root)
            outputs[0].unlink()
            with self.assertRaisesRegex(ValueError, "Export drift"):
                export_outputs(root, check=True)
            export_outputs(root)
            outputs[0].write_bytes(outputs[0].read_bytes() + b"drift")
            with self.assertRaisesRegex(ValueError, "Export drift"):
                export_outputs(root, check=True)

    def test_approved_plan_and_rules_drift_are_rejected(self):
        with tempfile.TemporaryDirectory(prefix="catalog importer ") as temporary:
            root = copy_import_inputs(Path(temporary))
            plan = root / "docs/plan/approved-plan.json"
            plan.write_bytes(plan.read_bytes() + b" ")
            with self.assertRaisesRegex(ValueError, "Approved plan output is stale"):
                build_catalog(root)
            shutil.copyfile(ROOT / "docs/plan/approved-plan.json", plan)
            rules = root / "docs/plan/amendment-rules.json"
            changed = json.loads(rules.read_text(encoding="utf-8"))
            changed["amendmentId"] = "TAMPERED"
            rules.write_text(json.dumps(changed, indent=2) + "\n", encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "Expected accepted PC-001"):
                build_catalog(root)


if __name__ == "__main__":
    unittest.main()
