from __future__ import annotations
import tempfile
import time
import unittest
from pathlib import Path
from datetime import datetime, timezone
from tools.bootstrap.driver import atomic_json, publish_new, relative_output, validate_unity_results
from tools.progress.schema import load_json, validate_file

class DriverTests(unittest.TestCase):
    def test_output_paths_reject_lexical_escape_and_allow_expected_roots(self):
        self.assertEqual(relative_output("build/test spaces", "ignored").name, "test spaces")
        with self.assertRaises(ValueError): relative_output("build/../release/out", "ignored")
        with self.assertRaises(ValueError): relative_output("config/generated", "ignored")
        with self.assertRaises(ValueError): relative_output("C:/outside/out", "ignored")

    def test_unity_xml_accepts_fresh_pass_and_rejects_stale_or_failed(self):
        with tempfile.TemporaryDirectory(prefix="driver xml ") as temp:
            path = Path(temp) / "results.xml"
            path.write_text('<test-run result="Passed" />', encoding="utf-8")
            started = time.time() - 1
            validate_unity_results(path, started)
            path.write_text('<test-run result="Failed" />', encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "Passed"):
                validate_unity_results(path, started)
            with self.assertRaisesRegex(ValueError, "fresh"):
                validate_unity_results(path, time.time() + 1)

    def test_first_run_preserves_local_and_failure_report_is_structured(self):
        with tempfile.TemporaryDirectory(prefix="driver report ") as temp:
            local = Path(temp) / "config/local.json"
            publish_new(local, b'{"machine":"first"}\n')
            with self.assertRaises(FileExistsError):
                publish_new(local, b'{"machine":"second"}\n')
            report = Path(temp) / "diagnostics/doctor.json"
            atomic_json(report, {"schemaVersion": 1, "action": "doctor", "recordedAtUtc": datetime.now(timezone.utc).isoformat(), "result": "fail", "sourceRevision": "test", "environment": {}, "checks": [{"id": "failure", "result": "fail", "message": "synthetic failure", "details": {}}], "limitations": ["development only"]})
            validate_file(load_json(report), Path("schemas/diagnostics/doctor-report.schema.json"))

if __name__ == "__main__": unittest.main()
