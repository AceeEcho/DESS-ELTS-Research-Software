"""Dependency audit corruption checks use isolated copies and never load native code."""
import json
import shutil
import tempfile
import unittest
from pathlib import Path
from tools.dependencies.verify_openvr import DEPENDENCY, REQUIRED_FILES, verify


class OpenVrTests(unittest.TestCase):
    def fixture(self, destination):
        destination.mkdir()
        for name in REQUIRED_FILES | {"dependency.json", "checksums.sha256", "NOTICE"}:
            out = destination / name
            out.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(DEPENDENCY / name, out)
        return destination

    def test_reviewed_source_and_binary_integrity(self):
        result = verify()
        self.assertEqual(result["filesVerified"], 4)
        self.assertEqual(result["nativeArchitecture"], "x86_64")
        self.assertFalse(result["nativeInitialized"])

    def test_modified_bytes_and_manifest_inventory_are_rejected(self):
        with tempfile.TemporaryDirectory(prefix="OpenVR audit ") as temporary:
            root = self.fixture(Path(temporary) / "fixture with spaces")
            binding = root / "openvr_api.cs"
            binding.write_bytes(binding.read_bytes() + b"\n")
            with self.assertRaisesRegex(ValueError, "byte/provenance"):
                verify(root)
            manifest = root / "dependency.json"
            data = json.loads(manifest.read_text())
            data["files"][0]["path"] = "../../outside"
            manifest.write_text(json.dumps(data))
            with self.assertRaisesRegex(ValueError, "exactly"):
                verify(root)

    def test_missing_notice_and_changed_checksum_list_are_rejected(self):
        with tempfile.TemporaryDirectory(prefix="OpenVR audit ") as temporary:
            root = self.fixture(Path(temporary) / "fixture")
            (root / "NOTICE").unlink()
            with self.assertRaisesRegex(ValueError, "notice"):
                verify(root)
            (root / "checksums.sha256").write_text("")
            with self.assertRaisesRegex(ValueError, "checksum file"):
                verify(root)


if __name__ == "__main__": unittest.main()
