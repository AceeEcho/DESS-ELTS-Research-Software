import json
import shutil
import tempfile
import unittest
from pathlib import Path
from tools.dependencies.verify_unity_packages import ROOT, verify


class UnityPackageTests(unittest.TestCase):
    def fixture(self, destination):
        for name in ["config/unity-packages.json", "schemas/build/unity-packages.schema.json", "unity/Packages/manifest.json", "unity/Packages/packages-lock.json", "unity/ProjectSettings/ProjectVersion.txt"]:
            path = destination / name
            path.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(ROOT / name, path)
        return destination

    def test_installed_pins_match(self):
        self.assertEqual(len(verify()), 3)

    def test_lock_drift_and_xr_are_rejected(self):
        with tempfile.TemporaryDirectory(prefix="UPM audit ") as temporary:
            root = self.fixture(Path(temporary))
            path = root / "unity/Packages/packages-lock.json"
            value = json.loads(path.read_text())
            value["dependencies"]["com.unity.nuget.newtonsoft-json"]["version"] = "0.0.0"
            path.write_text(json.dumps(value))
            with self.assertRaisesRegex(ValueError, "pin mismatch"): verify(root)
            value["dependencies"]["com.unity.nuget.newtonsoft-json"]["version"] = "3.2.2"
            value["dependencies"]["com.unity.xr.management"] = {"version": "1.0.0"}
            path.write_text(json.dumps(value))
            with self.assertRaisesRegex(ValueError, "XR packages"): verify(root)


if __name__ == "__main__": unittest.main()
