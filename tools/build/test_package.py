"""Integrity/portability regressions; fixture executables are never launched."""
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

from tools.build.package import ROOT, create_package, package_files, verify_package
from tools.build.provenance import finalize, sha


class PackageTests(unittest.TestCase):
    def setUp(self):
        parent = ROOT / "test-results"
        parent.mkdir(exist_ok=True)
        self.temporary = tempfile.TemporaryDirectory(prefix="package test with spaces ", dir=parent)
        self.root = Path(self.temporary.name)
        self.build = self.root / "source build"
        self.build.mkdir()
        (self.build / "ELTS-Synthetic.exe").write_bytes(b"unit-test-only-not-executable")
        config = self.build / "ELTS-Synthetic_Data/StreamingAssets/config-generated/effective-config.json"
        config.parent.mkdir(parents=True)
        config.write_text('{"machine":{"dataRoot":"data/synthetic"}}', encoding="utf-8")
        finalize(self.build, {"mode": "synthetic-development", "studyReady": False,
                             "version": "fixture", "commit": "fixture-revision", "dirty": False})
        self.report = self.root / "passing-report.json"
        self.report.write_text(json.dumps({"result":"pass", "mode":"synthetic-development",
                                         "playerSha256":sha(self.build / "ELTS-Synthetic.exe")}), encoding="utf-8")
        self.output = self.root / "copied package with spaces"

    def tearDown(self):
        self.assertTrue(self.root.resolve().is_relative_to((ROOT / "test-results").resolve()))
        self.temporary.cleanup()

    def create(self):
        return create_package(self.build, self.output, [self.report])

    def rehash(self):
        files = package_files(self.output)
        (self.output / "PACKAGE-MANIFEST.sha256").write_text(
            "".join(value + "  " + name + "\n" for name, value in files.items()), encoding="utf-8")

    def test_new_package_in_space_path_binds_docs_tools_and_player(self):
        result = self.create()
        self.assertEqual(result["result"], "pass")
        self.assertTrue(self.output.with_suffix(".zip").is_file())
        info = verify_package(self.output)
        self.assertFalse(info["studyReady"])
        self.assertTrue((self.output / "analysis/run_ingest.py").is_file())
        self.assertEqual((self.output / "player/ELTS-Synthetic.exe").read_bytes(), b"unit-test-only-not-executable")
        # A copied package must resolve the shared validator without this
        # checkout, PYTHONPATH, or the removed management tooling on its path.
        result = subprocess.run(
            [sys.executable, "-I", str(self.output / "tools/build/package.py"),
             "--verify", str(self.output)], cwd=self.root, capture_output=True,
            text=True, timeout=30)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(json.loads(result.stdout)["result"], "pass")

    def test_changed_document_and_unexpected_code_fail(self):
        self.create()
        original = (self.output / "README.md").read_bytes()
        (self.output / "README.md").write_text("modified", encoding="utf-8")
        with self.assertRaises(ValueError):
            verify_package(self.output)
        (self.output / "README.md").write_bytes(original)
        (self.output / "player/extra.dll").write_bytes(b"unexpected")
        with self.assertRaises(ValueError):
            verify_package(self.output)

    def test_runtime_recording_output_is_allowed_but_player_change_is_not(self):
        self.create()
        runtime = self.output / "player/data/synthetic/new-run"
        runtime.mkdir(parents=True)
        (runtime / "events.ndjson").write_text("runtime output", encoding="utf-8")
        verify_package(self.output)
        (runtime / "unexpected.dll").write_bytes(b"code is not a recording")
        with self.assertRaises(ValueError):
            verify_package(self.output)
        (runtime / "unexpected.dll").unlink()
        (self.output / "player/ELTS-Synthetic.exe").write_bytes(b"changed")
        with self.assertRaises(ValueError):
            verify_package(self.output)

    def test_rehashed_study_authority_still_rejects(self):
        self.create()
        path = self.output / "package-info.json"
        info = json.loads(path.read_text(encoding="utf-8"))
        info["studyReady"] = True
        path.write_text(json.dumps(info), encoding="utf-8")
        self.rehash()
        with self.assertRaises(ValueError):
            verify_package(self.output)

    def test_duplicate_json_and_traversal_manifest_reject(self):
        self.create()
        path = self.output / "package-info.json"
        path.write_text('{"mode":"synthetic-development","mode":"study"}', encoding="utf-8")
        self.rehash()
        with self.assertRaises(ValueError):
            verify_package(self.output)
        (self.output / "PACKAGE-MANIFEST.sha256").write_text("0" * 64 + "  ../outside\n", encoding="utf-8")
        with self.assertRaises(ValueError):
            verify_package(self.output)

    def test_existing_output_and_archive_are_preserved(self):
        self.create()
        manifest = (self.output / "PACKAGE-MANIFEST.sha256").read_bytes()
        with self.assertRaises(ValueError):
            self.create()
        self.assertEqual(manifest, (self.output / "PACKAGE-MANIFEST.sha256").read_bytes())
        another = self.root / "another"
        another.with_suffix(".zip").write_bytes(b"preserve")
        with self.assertRaises(ValueError):
            create_package(self.build, another, [self.report])
        self.assertFalse(another.exists())

    def test_failing_evidence_prevents_any_output(self):
        self.report.write_text('{"result":"fail"}', encoding="utf-8")
        with self.assertRaises(ValueError):
            self.create()
        self.assertFalse(self.output.exists())

    def test_unbound_passing_evidence_is_rejected(self):
        self.report.write_text('{"result":"pass"}', encoding="utf-8")
        with self.assertRaises(ValueError):
            self.create()
        self.assertFalse(self.output.exists())


if __name__ == "__main__":
    unittest.main()
