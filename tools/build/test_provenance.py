import tempfile
import unittest
from unittest.mock import patch
from pathlib import Path
from tools.build.provenance import finalize, prepare, source_revision
from tools.build.verify import verify


class BuildProvenanceTests(unittest.TestCase):
    def test_source_copy_builds_without_git_and_hashes_actual_inputs(self):
        with tempfile.TemporaryDirectory(prefix="ELTS source copy ") as tmp:
            root = Path(tmp)
            for name in ("unity/Packages/manifest.json", "unity/Packages/packages-lock.json",
                         "unity/Assets/example.cs", "config/local.json", "config/local/tools.json",
                         "collection data/private/events.ndjson", "unity/Library/cache"):
                path = root / name
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text("fixture", encoding="utf-8")
            output = root / "build/player"
            output.mkdir(parents=True)
            with patch("tools.build.provenance.subprocess.check_output", side_effect=AssertionError("Git must not run")):
                first = prepare(root, output, {"unity": {"target": "StandaloneWindows64"}})
                self.assertIsNone(source_revision(root))
                self.assertEqual(first["sourceKind"], "unversioned-copy")
                self.assertIsNone(first["commit"])
                self.assertIsNone(first["dirty"])
                self.assertEqual(set(first["sourceFilesSha256"]), {
                    "unity/Packages/manifest.json", "unity/Packages/packages-lock.json", "unity/Assets/example.cs"})
                (root / "unity/Assets/example.cs").write_text("changed input", encoding="utf-8")
                second = prepare(root, output, {"unity": {"target": "StandaloneWindows64"}})
                self.assertNotEqual(first["version"], second["version"])
                # Even a copied .git folder must not make Git an installation dependency.
                (root / ".git").mkdir()
                with patch("tools.build.provenance.shutil.which", return_value=None):
                    self.assertIsNone(source_revision(root))
            (output / "ELTS-Synthetic.exe").write_bytes(b"fixture player")
            finalize(output, second)
            self.assertEqual(verify(output)["sourceSha256"], second["sourceSha256"])

    def test_recording_checkpoints_and_exports_do_not_invalidate_the_player(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            (root / "ELTS-Synthetic.exe").write_bytes(b"fixture player")
            config = root / "ELTS-Synthetic_Data/StreamingAssets/config-generated/effective-config.json"
            config.parent.mkdir(parents=True)
            config.write_text('{"machine":{"dataRoot":"data/synthetic"}}', encoding="utf-8")
            finalize(root, {"mode": "synthetic-development", "studyReady": False, "version": "fixture"})
            products = [".run-1.reservation", "run-1/events.ndjson", "run-1/participant.json", "collection.sqlite", "collection.sqlite-wal", "collection.sqlite-shm",
                        "exports/participant-" + "b" * 32 + "-" + "c" * 32 + ".sqlite", "run-1/test-checkpoint-WE_FT-attempt-01.json",
                        "run-1/test-checkpoint-WE_FT-attempt-02.json.pending", "exports/run-1-" + "a" * 32 + ".zip"]
            for name in products:
                product = root / "data/synthetic" / name
                product.parent.mkdir(parents=True, exist_ok=True)
                product.write_text("fixture output", encoding="utf-8")
            self.assertEqual(verify(root)["version"], "fixture")
            injected = root / "data/synthetic/run-1/unexpected.dll"
            injected.write_bytes(b"not recording data")
            with self.assertRaises(ValueError): verify(root)
            injected.unlink()
            outside = root / "other/run-1/events.ndjson"
            outside.parent.mkdir(parents=True)
            outside.write_text("wrong output directory", encoding="utf-8")
            with self.assertRaises(ValueError): verify(root)

    def test_round_trip_detects_corruption_and_excludes_raw_logs(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            (root / "ELTS-Synthetic.exe").write_bytes(b"fixture player")
            (root / "unity-build.log").write_text("private diagnostic", encoding="utf-8")
            finalize(root, {"mode": "synthetic-development", "studyReady": False, "version": "fixture"})
            self.assertEqual(verify(root)["version"], "fixture")
            self.assertNotIn("unity-build.log", (root / "MANIFEST.sha256").read_text())
            extra = root / "unexpected.dll"
            extra.write_bytes(b"extra")
            with self.assertRaises(ValueError): verify(root)
            extra.unlink()
            (root / "ELTS-Synthetic.exe").write_bytes(b"altered")
            with self.assertRaises(ValueError): verify(root)

    def test_manifest_cannot_escape_product_root(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            for path in ("../outside", "C:/outside", "a\\outside"):
                (root / "MANIFEST.sha256").write_text("0" * 64 + "  " + path + "\n")
                with self.assertRaises(ValueError): verify(root)


if __name__ == "__main__": unittest.main()
