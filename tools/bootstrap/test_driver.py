from __future__ import annotations
import tempfile
import os
import subprocess
import sys
import time
import unittest
from unittest.mock import patch
from argparse import Namespace
from pathlib import Path
from datetime import datetime, timezone
from tools.bootstrap.driver import atomic_json, publish_new, relative_output, validate_unity_results
from tools.bootstrap import driver
from tools.validation.schema import load_json, validate_file

class DriverTests(unittest.TestCase):
    def test_failed_child_keeps_assertion_and_recovery_in_message_and_log(self):
        with tempfile.TemporaryDirectory(prefix="ELTS failure ") as tmp:
            log = Path(tmp) / 'output.log'
            with self.assertRaises(driver.CommandFailure) as caught:
                driver.run_logged_command('runtime-config', [sys.executable, '-c',
                    'import sys; print("FAIL: Editor saves at project root", flush=True); sys.exit(17)'], log, 10)
            self.assertIn('exit 17', str(caught.exception))
            self.assertIn('FAIL: Editor saves at project root', str(caught.exception))
            self.assertIn('do not skip verification', str(caught.exception))
            self.assertIn(str(log), str(caught.exception))
            self.assertIn('FAIL:', log.read_text())

    def test_unity_failure_uses_real_editor_log_not_empty_stdout(self):
        with tempfile.TemporaryDirectory(prefix="ELTS Unity log ") as tmp:
            log, editor_log = Path(tmp) / 'stdout.log', Path(tmp) / 'unity-build.log'
            code = ('import pathlib,sys; pathlib.Path(sys.argv[1]).write_text('
                    '"No valid Unity Editor license found. Please activate your license."); sys.exit(198)')
            with self.assertRaises(driver.CommandFailure) as caught:
                driver.run_logged_command('unity-build', [sys.executable, '-c', code, str(editor_log)],
                                          log, 10, editor_log=editor_log)
            self.assertEqual(caught.exception.log, editor_log)
            self.assertIn('Settings > Licenses', str(caught.exception))
            self.assertIn('No valid Unity Editor license', str(caught.exception))
            self.assertEqual(log.read_text(), '')

    def test_stale_unity_license_log_does_not_misdiagnose_new_failure(self):
        with tempfile.TemporaryDirectory(prefix="ELTS stale log ") as tmp:
            log, editor_log = Path(tmp) / 'stdout.log', Path(tmp) / 'unity-build.log'
            editor_log.write_text('No valid Unity Editor license found.')
            os.utime(editor_log, (1, 1))
            with self.assertRaises(driver.CommandFailure) as caught:
                driver.run_logged_command('unity-build', [sys.executable, '-c',
                    'import sys; print("error CS1002: ; expected"); sys.exit(1)'], log, 10, editor_log=editor_log)
            self.assertEqual(caught.exception.log, log)
            self.assertNotIn('Settings > Licenses', str(caught.exception))
            self.assertIn('Fix the compiler errors', str(caught.exception))
            self.assertIn('did not write a fresh Editor log', str(caught.exception))

    def test_timeout_preserves_partial_output_and_instructions(self):
        with tempfile.TemporaryDirectory(prefix="ELTS timeout ") as tmp:
            log = Path(tmp) / 'output.log'
            timeout = subprocess.TimeoutExpired(['fixture'], 12, output=b'import still running\n', stderr=b'waiting\n')
            with patch.object(driver.subprocess, 'run', side_effect=timeout), self.assertRaises(driver.CommandFailure) as caught:
                driver.run_logged_command('unity-build', ['fixture'], log, 12)
            self.assertIn('timed out after 12 seconds', str(caught.exception))
            self.assertIn('Do not launch overlapping builds', str(caught.exception))
            self.assertIn('import still running', log.read_text())
            self.assertIn('waiting', log.read_text())

    def test_missing_executable_and_wrong_harness_fail_with_logs(self):
        with tempfile.TemporaryDirectory(prefix="ELTS missing tool ") as tmp:
            log = Path(tmp) / 'output.log'
            with self.assertRaisesRegex(driver.CommandFailure, 'could not start'):
                driver.run_logged_command('fixture', [str(Path(tmp) / 'missing.exe')], log, 10)
            with self.assertRaisesRegex(driver.CommandFailure, 'expected test harness did not run'):
                driver.run_logged_command('fixture', [sys.executable, '-c', 'print("unrelated")'],
                                          log, 10, expected_output='configuration checks')

    def test_stage_without_git_reports_unversioned_and_preserves_validation(self):
        # Exercise the actual diagnostic/report path with Git absent, not just
        # the revision helper. Configuration validation still runs normally.
        with tempfile.TemporaryDirectory(prefix="ELTS archive diagnostics ") as tmp:
            report_path = Path(tmp) / "report.json"
            args = Namespace(action="stage", report=None, unity_editor=None, output=None, check=False)
            with patch("tools.build.provenance.shutil.which", return_value=None), \
                    patch.object(driver, "editor_path", return_value=(None, None)), \
                    patch.object(driver, "relative_output", return_value=report_path), \
                    patch.object(driver, "stage") as stage:
                report, _ = driver.run(args)
            self.assertEqual(report["result"], "pass")
            self.assertEqual(report["sourceRevision"], "unversioned")
            stage.assert_called_once_with(driver.ROOT, None, check=False)

    def test_output_paths_reject_lexical_escape_and_allow_expected_roots(self):
        self.assertEqual(relative_output("build/test spaces", "ignored").name, "test spaces")
        with self.assertRaises(ValueError): relative_output("build/../release/out", "ignored")
        with self.assertRaises(ValueError): relative_output("config/generated", "ignored")
        with self.assertRaises(ValueError): relative_output("C:/outside/out", "ignored")

    def test_unity_xml_accepts_fresh_pass_and_rejects_stale_or_failed(self):
        with tempfile.TemporaryDirectory(prefix="driver xml ") as temp:
            path = Path(temp) / "results.xml"
            path.write_text('<test-run result="Passed"><test-case result="Passed" /></test-run>', encoding="utf-8")
            started = time.time() - 1
            validate_unity_results(path, started)
            path.write_text('<test-run xmlns="urn:unity" result="Passed"><test-case result="Passed" /></test-run>', encoding="utf-8")
            validate_unity_results(path, started)
            path.write_text('<test-run result="Passed" />', encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "no passing"):
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
