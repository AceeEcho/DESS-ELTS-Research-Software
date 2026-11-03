"""Temporary-folder regression checks; never modify the real Steam installation."""
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

from tools.bootstrap.tracking_setup import configure, discover, merged_settings, read_json


class TrackingSetupTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.settings = self.root / "config/steamvr.vrsettings"

    def write_settings(self, value):
        self.settings.parent.mkdir(parents=True, exist_ok=True)
        self.settings.write_text(json.dumps(value), encoding="utf-8")

    def test_preserves_unrelated_settings_with_exact_backup_and_idempotence(self):
        self.write_settings({"steamvr": {"ipd": 0.064}, "driver_null": {"renderWidth": 1024},
                             "trackers": {"LHR-device": "TrackerRole_Head"}, "custom": [1, 2, 3]})
        before = self.settings.read_bytes()
        result = configure(self.settings, running=lambda: False)
        self.assertEqual(Path(result["backup"]).read_bytes(), before)
        actual = read_json(self.settings)
        self.assertEqual(actual["steamvr"]["ipd"], 0.064)
        self.assertEqual(actual["driver_null"]["renderWidth"], 1024)
        self.assertEqual(actual["trackers"], {"LHR-device": "TrackerRole_Head"})
        self.assertEqual(actual["custom"], [1, 2, 3])
        after = self.settings.read_bytes()
        self.assertFalse(configure(self.settings, running=lambda: True)["changed"])
        self.assertEqual(after, self.settings.read_bytes())
        self.assertEqual(len(list(self.settings.parent.glob("*.bak"))), 1)

    def test_check_does_not_create_files(self):
        self.assertFalse(configure(self.settings, check=True)["configured"])
        self.assertFalse(self.settings.parent.exists())

    def test_first_run_creates_only_user_overrides(self):
        result = configure(self.settings, running=lambda: False)
        self.assertTrue(result["configured"])
        self.assertIsNone(result["backup"])
        self.assertEqual(read_json(self.settings), merged_settings({}))

    def test_running_runtime_does_not_modify_settings(self):
        self.write_settings({"steamvr": {"requireHmd": True}})
        before = self.settings.read_bytes()
        with self.assertRaisesRegex(ValueError, "Close SteamVR"):
            configure(self.settings, running=lambda: True)
        self.assertEqual(self.settings.read_bytes(), before)
        self.assertEqual(len(list(self.settings.parent.glob("*.bak"))), 0)

    def test_invalid_existing_file_is_not_replaced(self):
        for original in ('{"bad json"', '[]', '{"steamvr": false}'):
            self.settings.parent.mkdir(parents=True, exist_ok=True)
            self.settings.write_text(original, encoding="utf-8")
            with self.assertRaises(ValueError):
                configure(self.settings, running=lambda: False)
            self.assertEqual(self.settings.read_text(encoding="utf-8"), original)

    def test_conflicting_writer_and_failed_replace_preserve_original(self):
        self.write_settings({"original": True})
        calls = 0
        def writer():
            nonlocal calls
            calls += 1
            if calls == 2:
                self.write_settings({"newExternalSetting": True})
            return False
        with self.assertRaisesRegex(ValueError, "changed during setup"):
            configure(self.settings, running=writer)
        self.assertEqual(read_json(self.settings), {"newExternalSetting": True})
        with patch("tools.bootstrap.tracking_setup.os.replace", side_effect=PermissionError("locked")):
            with self.assertRaises(PermissionError):
                configure(self.settings, running=lambda: False)
        self.assertEqual(read_json(self.settings), {"newExternalSetting": True})
        self.assertFalse(list(self.settings.parent.glob("*.tmp")))

    def make_runtime(self, path):
        for name in ("bin/win64/vrserver.exe", "bin/win64/vrpathreg.exe", "drivers/null/bin/win64/driver_null.dll",
                     "drivers/lighthouse/bin/win64/driver_lighthouse.dll"):
            file = path / name
            file.parent.mkdir(parents=True, exist_ok=True)
            file.touch()

    def test_custom_library_and_registered_config_are_discovered(self):
        steam = self.root / "Steam installation"
        library = self.root / "Other drive/SteamLibrary"
        runtime = library / "steamapps/common/SteamVR"
        self.make_runtime(runtime)
        (steam / "steamapps").mkdir(parents=True)
        escaped = str(library).replace("\\", "\\\\")
        (steam / "steamapps/libraryfolders.vdf").write_text(f'"libraryfolders" {{ "1" {{ "path" "{escaped}" }} }}')
        registry = self.root / "openvrpaths.vrpath"
        registry.write_text(json.dumps({"config": [str(self.root / "Custom settings")], "runtime": [str(self.root / "missing")]}))
        result = discover(steam, registry)
        self.assertEqual(result["runtime"], str(runtime))
        self.assertEqual(result["config"], str(self.root / "Custom settings/steamvr.vrsettings"))
        self.assertFalse(result["registeredRuntime"])

    def test_missing_paths_and_incomplete_runtime_do_not_report_ready(self):
        steam = self.root / "Steam"
        runtime = steam / "steamapps/common/SteamVR"
        (runtime / "bin/win64").mkdir(parents=True)
        (runtime / "bin/win64/vrserver.exe").touch()
        result = discover(steam, self.root / "missing.json")
        self.assertIsNone(result["runtime"])
        self.assertFalse(result["registeredConfig"])


if __name__ == "__main__":
    unittest.main()
