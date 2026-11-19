"""Checks for the SteamVR health report, using made-up log lines and a temporary database."""
import json
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import steamvr_health  # noqa: E402

LOG = """Mon Nov 16 2026 17:38:25.750 [Info] - lighthouse: Setting universe tilt from 396112F4 via transform to global: pitch 26.55 deg roll 0.02 deg
Mon Nov 16 2026 17:38:54.328 [Info] - lighthouse: Setting universe tilt from 396112F4 via transform to global: pitch 27.09 deg roll 1.05 deg
Mon Nov 16 2026 18:19:45.811 [Info] - lighthouse: Base 396112F4 reset count changed.  Base may have been bumped or power cycled.
Mon Nov 16 2026 18:28:43.573 [Info] - lighthouse: LHR-AAAA1111 C: Resetting tracking: IMU misalignment unreasonably large (8.2, 2, -12) deg sigma 0.58
Mon Nov 16 2026 18:30:00.000 [Info] - lighthouse: LHR-BBBB2222 C: Resetting tracking: no optical samples from base 396112F4 for 2000ms
Mon Nov 16 2026 18:56:27.035 [Info] - lighthouse: LHR-AAAA1111 C: IMU went off scale.
""".splitlines()


class SteamVrHealthTests(unittest.TestCase):
    def test_summary_counts_the_events_that_invalidate_a_calibration(self):
        summary = steamvr_health.summarize_log(LOG)
        self.assertEqual(summary["day"], "Nov 16 2026")
        self.assertEqual(summary["frameRedefinitions"], 2)
        self.assertAlmostEqual(summary["largestStepDegrees"], 1.163, places=2)
        self.assertEqual(summary["baseStationDisturbed"], 1)
        # A misalignment reset is counted as a mismatch, not also as lost sight.
        self.assertEqual(summary["opticalInertialMismatch"], {"LHR-AAAA1111": 1})
        self.assertEqual(summary["trackerLostSight"], {"LHR-BBBB2222": 1})
        self.assertEqual(summary["imuSaturated"], {"LHR-AAAA1111": 1})

    def test_only_the_most_recent_day_is_summarized(self):
        older = ["Sun Nov 15 2026 21:17:26.581 [Info] - lighthouse: Setting universe tilt from 396112F4 via transform to global: pitch 25.96 deg roll 1.70 deg"]
        self.assertEqual(steamvr_health.summarize_log(older + LOG)["frameRedefinitions"], 2)
        self.assertIsNone(steamvr_health.summarize_log(["no dated lines"])["day"])

    def _database(self, folder):
        lighthouse = Path(folder) / "lighthouse"
        lighthouse.mkdir()
        database = lighthouse / "lighthousedb.json"
        database.write_text(json.dumps({"known_objects": [
            {"serialNumber": "LHR-AAAA1111", "imu": {"acc_scale": [1.24, 1.0, 1.48], "acc_bias": [-1.2, -0.5, 0.4]}},
            {"serialNumber": "LHR-BBBB2222", "imu": {"acc_scale": [1.001, 1.005, 0.993], "acc_bias": [0.16, 0.11, -0.12]}},
        ], "revision": 68}), encoding="utf-8")
        return database

    def test_report_flags_an_implausible_learned_calibration(self):
        with tempfile.TemporaryDirectory() as folder:
            self._database(folder)
            report = {item["serial"]: item["implausible"] for item in steamvr_health.imu_report(Path(folder))}
        self.assertEqual(report, {"LHR-AAAA1111": True, "LHR-BBBB2222": False})

    def test_reset_keeps_a_backup_and_removes_only_the_named_tracker(self):
        with tempfile.TemporaryDirectory() as folder:
            database = self._database(folder)
            original = database.read_bytes()
            backup = steamvr_health.reset_imu(Path(folder), "LHR-AAAA1111", running=lambda: False)
            self.assertEqual(backup.read_bytes(), original)
            kept = json.loads(database.read_text(encoding="utf-8"))
            self.assertEqual([entry["serialNumber"] for entry in kept["known_objects"]], ["LHR-BBBB2222"])
            self.assertEqual(kept["revision"], 68)

    def test_reset_refuses_while_steamvr_runs_or_for_an_unknown_tracker(self):
        with tempfile.TemporaryDirectory() as folder:
            database = self._database(folder)
            original = database.read_bytes()
            with self.assertRaises(ValueError):
                steamvr_health.reset_imu(Path(folder), "LHR-AAAA1111", running=lambda: True)
            with self.assertRaises(ValueError):
                steamvr_health.reset_imu(Path(folder), "LHR-CCCC3333", running=lambda: False)
            self.assertEqual(database.read_bytes(), original)
            self.assertEqual(len(list(database.parent.glob("*.bak"))), 0)


if __name__ == "__main__":
    unittest.main()
