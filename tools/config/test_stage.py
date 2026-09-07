from __future__ import annotations
import json
import shutil
import tempfile
import unittest
import uuid
from pathlib import Path
from tools.config.stage import stage

ROOT = Path(__file__).resolve().parents[2]

def fixture_root(parent: Path) -> Path:
    root = parent / f"config fixture with spaces {uuid.uuid4().hex}"
    for rel in ("config", "schemas/config"):
        shutil.copytree(ROOT / rel, root / rel)
    (root / "unity/Assets/StreamingAssets").mkdir(parents=True)
    return root

class StageTests(unittest.TestCase):
    def test_write_check_is_deterministic_and_manifest_is_consumer_contract(self):
        with tempfile.TemporaryDirectory() as temp:
            root = fixture_root(Path(temp)); out = root / "unity/Assets/StreamingAssets/config-generated"
            first = stage(root); bytes1 = {p: p.read_bytes() for p in first}; mtimes = {p: p.stat().st_mtime_ns for p in first}; stage(root)
            stage(root, check=True)
            self.assertEqual(bytes1, {p: p.read_bytes() for p in first}); self.assertEqual(mtimes, {p: p.stat().st_mtime_ns for p in first})
            manifest = json.loads((out / "manifest.json").read_text())
            self.assertEqual(set(manifest), {"schemaVersion", "mode", "schema", "effectiveConfig", "effectiveConfigSha256", "sourceRawSha256", "schemaFilesSha256"})
            self.assertEqual(manifest["effectiveConfig"], "effective-config.json")
            self.assertIn("effective.schema.json", manifest["schemaFilesSha256"])

    def test_missing_stale_and_extra_outputs_rejected(self):
        with tempfile.TemporaryDirectory() as temp:
            root = fixture_root(Path(temp)); outputs = stage(root); outputs[0].unlink()
            with self.assertRaisesRegex(ValueError, "drift"): stage(root, check=True)
            stage(root); outputs[0].write_bytes(outputs[0].read_bytes() + b"x")
            with self.assertRaisesRegex(ValueError, "drift"): stage(root, check=True)
            stage(root); (outputs[0].parent / "unexpected.json").write_text("{}")
            with self.assertRaisesRegex(ValueError, "drift"): stage(root, check=True)

    def test_strict_unknown_invalid_mode_and_local_whitelist(self):
        with tempfile.TemporaryDirectory() as temp:
            root = fixture_root(Path(temp)); staging = root / "config/staging.json"
            value = json.loads(staging.read_text()); value["mode"] = "study"; staging.write_text(json.dumps(value))
            with self.assertRaisesRegex(ValueError, "synthetic"): stage(root)
            value["mode"] = "synthetic"; value["unknown"] = 1; staging.write_text(json.dumps(value))
            with self.assertRaises(Exception): stage(root)
            value.pop("unknown"); staging.write_text(json.dumps(value)); local = json.loads((root / "config/local.example.json").read_text()); local["unsafe"] = 1; (root / "config/local.json").write_text(json.dumps(local))
            with self.assertRaises(Exception): stage(root)

    def test_missing_source_geometry_order_and_path_fail_closed(self):
        with tempfile.TemporaryDirectory() as temp:
            root = fixture_root(Path(temp)); (root / "config/development/scenario.json").unlink()
            with self.assertRaisesRegex(ValueError, "Missing"): stage(root)
            root = fixture_root(Path(temp)); rig = root / "config/rig/templates/synthetic-rig.json"; value = json.loads(rig.read_text()); value["display"]["upperLeftM"] = value["display"]["lowerLeftM"]; rig.write_text(json.dumps(value))
            with self.assertRaisesRegex(ValueError, "perpendicular"): stage(root)
            root = fixture_root(Path(temp)); session = root / "config/defaults/session.json"; value = json.loads(session.read_text()); value["conditionOrder"] = value["conditionOrder"][:3]; session.write_text(json.dumps(value))
            with self.assertRaises(Exception): stage(root)

    def test_duplicate_nonfinite_and_no_write_on_invalid_source(self):
        with tempfile.TemporaryDirectory(prefix="catalog importer ") as temp:
            root = fixture_root(Path(temp)); output = root / "unity/Assets/StreamingAssets/config-generated"
            runtime = root / "config/defaults/runtime.json"; runtime.write_text('{"schemaVersion":1,"schemaVersion":1}')
            with self.assertRaisesRegex(ValueError, "Duplicate"):
                stage(root)
            self.assertFalse(output.exists())
            runtime.write_text('{"schemaVersion":1,"trackingSource":"synthetic","sampleRateHz":NaN}')
            with self.assertRaisesRegex(ValueError, "Non-finite"):
                stage(root)
            self.assertFalse(output.exists())

    def test_data_root_and_symlink_fail_closed(self):
        with tempfile.TemporaryDirectory(prefix="catalog importer ") as temp:
            root = fixture_root(Path(temp)); local = root / "config/local.example.json"; value = json.loads(local.read_text()); value["dataRoot"] = "C:/escape"; local.write_text(json.dumps(value))
            with self.assertRaisesRegex(ValueError, "dataRoot"):
                stage(root)
            root = fixture_root(Path(temp)); target = root / "config/defaults/runtime.json"; link = root / "config/defaults/runtime-link.json"
            try:
                link.symlink_to(target)
            except (OSError, NotImplementedError):
                self.skipTest("Host does not permit file symlink creation")
            staging = root / "config/staging.json"; value = json.loads(staging.read_text()); value["runtime"] = "config/defaults/runtime-link.json"; staging.write_text(json.dumps(value))
            with self.assertRaisesRegex(ValueError, "symlinked"):
                stage(root)

    def test_generated_destinations_and_exact_unity_meta_allowance(self):
        with tempfile.TemporaryDirectory() as temp:
            root = fixture_root(Path(temp))
            for output in (Path("config/defaults"), Path("build"), Path("release"), root, root.parent):
                with self.assertRaises(ValueError):
                    stage(root, output)
            outputs = stage(root, Path("build/config"))
            for path in outputs:
                path.with_name(path.name + ".meta").write_text("Unity metadata fixture", encoding="utf-8")
            stage(root, Path("build/config"), check=True)
            (root / "build/config/unexpected-empty").mkdir()
            with self.assertRaisesRegex(ValueError, "Unexpected"):
                stage(root, Path("build/config"))

    def test_output_links_are_checked_before_resolve_and_before_any_write(self):
        with tempfile.TemporaryDirectory() as temp:
            root = fixture_root(Path(temp))
            target = root / "build/real"
            target.mkdir(parents=True)
            link = root / "build/link"
            try:
                link.symlink_to(target, target_is_directory=True)
            except (OSError, NotImplementedError):
                self.skipTest("Host does not permit directory symlink creation")
            with self.assertRaisesRegex(ValueError, "symlinked"):
                stage(root, link)
            self.assertEqual(list(target.iterdir()), [])
            # An expected filename pointing outside the bundle is also rejected.
            outside = root / "sentinel.json"
            outside.write_text("keep", encoding="utf-8")
            (target / "effective-config.json").symlink_to(outside)
            with self.assertRaisesRegex(ValueError, "symlinked"):
                stage(root, target)
            self.assertEqual(outside.read_text(), "keep")

if __name__ == "__main__": unittest.main()
