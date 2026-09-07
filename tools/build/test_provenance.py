import tempfile
import unittest
from pathlib import Path
from tools.build.provenance import finalize
from tools.build.verify import verify


class BuildProvenanceTests(unittest.TestCase):
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
