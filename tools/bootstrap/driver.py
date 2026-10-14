"""Portable developer entry points, driven by config/toolchain.json.

Prerequisites are detected, never silently installed or licensed. Reports describe
only checks actually run. Missing study approval cannot fall back to development.
"""
from __future__ import annotations

import argparse
import json
import os
import platform
import re
import shutil
import subprocess
import sys
import time
import stat
from tempfile import NamedTemporaryFile
import xml.etree.ElementTree as ET
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT))
from tools.config.stage import stage
from tools.validation.schema import load_json, validate_file
from tools.build.provenance import source_revision

# Engineering timeouts, not study timing or exposure limits.
PYTHON_TEST_TIMEOUT_SECONDS = 600
UNITY_TIMEOUT_SECONDS = 1800
ACTIONS = ("bootstrap", "doctor", "test", "build", "stage", "package-study", "setup-study-machine", "start-study")


def _has_reparse_component(path: Path) -> bool:
    current = Path(path.anchor) if path.is_absolute() else Path()
    for component in path.parts[1:] if path.is_absolute() else path.parts:
        current /= component
        try:
            attributes = current.lstat()
        except FileNotFoundError:
            continue
        if stat.S_ISLNK(attributes.st_mode) or getattr(attributes, "st_file_attributes", 0) & 0x400:
            return True
    return False


def relative_output(value: str, default: str) -> Path:
    """Keep generated reports/builds out of sources and private deployment data."""
    raw_text = value or default
    raw = Path(raw_text)
    if any(part in {".", ".."} for part in raw.parts):
        raise ValueError("Generated output may not contain lexical traversal components")
    path = raw if raw.is_absolute() else ROOT / raw
    if _has_reparse_component(path):
        raise ValueError("Generated output may not traverse symlink/reparse components")
    lexical = path.absolute()
    try:
        lexical.relative_to(ROOT)
    except ValueError as exc:
        raise ValueError("Generated output must stay inside this checkout") from exc
    resolved = path.resolve()
    if not resolved.is_relative_to(ROOT) or resolved == ROOT:
        raise ValueError("Generated output must stay inside this checkout")
    rel = resolved.relative_to(ROOT)
    if rel.parts[0] not in {"build", "diagnostics", "test-results", "release"}:
        raise ValueError("Generated output must be in build/, diagnostics/, test-results/ or release/")
    return resolved


def validate_unity_results(path: Path, started_at: float) -> None:
    """Require a fresh, successful Unity XML result from the current invocation."""
    if not path.is_file() or path.stat().st_mtime < started_at:
        raise ValueError("Unity exited without producing fresh test results")
    try:
        root = ET.parse(path).getroot()
    except ET.ParseError as exc:
        raise ValueError("Unity test results are not valid XML") from exc
    if root.tag.rsplit("}", 1)[-1] != "test-run" or root.attrib.get("result") not in {"Passed", "passed"}:
        raise ValueError("Unity test results did not report Passed")
    cases = [case for case in root.iter() if case.tag.rsplit("}", 1)[-1] == "test-case"]
    if not cases or not any(case.attrib.get("result") == "Passed" for case in cases):
        raise ValueError("Unity test results contain no passing test cases")
    if any(case.attrib.get("result") in {"Failed", "Inconclusive"} for case in cases):
        raise ValueError("Unity test results contain failed or inconclusive cases")


def atomic_json(path: Path, value: dict):
    data = (json.dumps(value, ensure_ascii=False, indent=2, allow_nan=False) + "\n").encode("utf-8")
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = None
    try:
        with NamedTemporaryFile(prefix="." + path.name, suffix=".tmp", dir=path.parent, delete=False) as stream:
            temporary = Path(stream.name)
            stream.write(data); stream.flush(); os.fsync(stream.fileno())
        os.replace(temporary, path)
    finally:
        if temporary is not None: temporary.unlink(missing_ok=True)


def publish_new(path: Path, data: bytes) -> None:
    """Create a first-run local file without overwriting an existing machine file."""
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = None
    try:
        with NamedTemporaryFile(prefix="." + path.name, suffix=".tmp", dir=path.parent, delete=False) as stream:
            temporary = Path(stream.name)
            stream.write(data); stream.flush(); os.fsync(stream.fileno())
        # Atomic create-new publication. Existing machine settings are preserved.
        os.link(temporary, path)
    finally:
        if temporary is not None: temporary.unlink(missing_ok=True)


def editor_path(manifest: dict, explicit: str | None) -> tuple[Path | None, str | None]:
    expected = manifest["unity"]["version"]
    raw = explicit or os.environ.get("ELTS_UNITY_EDITOR")
    path = Path(raw) if raw else Path(os.environ.get("ProgramFiles", "C:/Program Files")) / "Unity/Hub/Editor" / expected / "Editor/Unity.exe"
    if not path.is_file():
        return None, None
    # Read PE metadata through Windows' existing PowerShell. The path is passed
    # as an environment value, never interpolated into shell code.
    shell = shutil.which("powershell") or shutil.which("pwsh")
    if not shell:
        raise ValueError("PowerShell is required to verify the Unity executable version")
    env = dict(os.environ, ELTS_PROBE_EDITOR=str(path.resolve()))
    result = subprocess.run([shell, "-NoProfile", "-NonInteractive", "-Command",
                             "(Get-Item -LiteralPath $env:ELTS_PROBE_EDITOR).VersionInfo.ProductVersion"],
                            capture_output=True, text=True, env=env, timeout=30)
    version = result.stdout.strip().split("_", 1)[0]
    if result.returncode or version != expected:
        raise ValueError(f"Unity version mismatch: expected {expected}, observed {version or 'unknown'}. Supply the exact Editor path.")
    return path.resolve(), version


class CommandFailure(ValueError):
    """A failed command with its current log and actionable operator message."""
    def __init__(self, message: str, log: Path):
        super().__init__(message)
        self.log = log


def command_failure_message(identifier: str, reason: str, output: str, log: Path) -> str:
    if 'timed out' in reason:
        advice = ('Check the log for a stalled operation or sign-in prompt. Check for remaining Unity/build processes '
                  'and close them normally before retrying. Do not launch overlapping builds.')
    elif 'No valid Unity Editor license' in output or ('unity' in identifier and reason == 'exit 198'):
        advice = ('Open Unity Hub and sign in. Go to Settings > Licenses > Refresh, or Add license to activate '
                  'your eligible license. For an organization license, ask its administrator to assign your account '
                  'a seat. If activation fails, check network/proxy access. Close the Editor, then rerun '
                  'SETUP-DEV.cmd followed by START-ELTS.cmd. '
                  'Help: https://docs.unity.com/en-us/hub/manage-license')
    elif re.search(r'another Unity instance|already open in another|multiple Unity instances', output, re.I):
        advice = 'Save your work and close this project in Unity, then retry. Do not delete active Unity lock files.'
    elif re.search(r'error CS\d+|compilation failed|scripts have compiler errors', output, re.I):
        advice = ('Fix the compiler errors listed in the log, or update to a corrected repository revision, then '
                  'rerun SETUP-DEV.cmd. If you do not maintain the code, send this log to the project maintainer.')
    elif re.search(r'FAIL:|Assertion|test.*failed|Failed\s+\w+', output, re.I):
        advice = ('A software verification check failed. Send the failed assertion and this log to the project '
                  'maintainer, or update to a revision that fixes it. Keep your local configuration and recordings. '
                  'Rerun SETUP-DEV.cmd after the fix; do not skip verification to mark setup ready.')
    else:
        advice = ('Review the error below and the full log. Correct the reported problem, then rerun the command. '
                  'If the cause is unclear, send this failed step, log, and diagnostic report to the project maintainer.')
    lines = output.splitlines()
    # Include the first error with context, even when cleanup fills the log tail.
    first = next((i for i, line in enumerate(lines) if re.search(r'error|exception|FAIL:|assert', line, re.I)), None)
    excerpt = lines[-20:]
    if first is not None and first < len(lines) - 20:
        excerpt = lines[first:first + 8] + ['...'] + excerpt
    excerpt_text = '\n'.join(line[:500] for line in excerpt)
    return (f'Step: {identifier} ({reason})\nHow to resolve: {advice}\n'
            f'Log: {log}\nLatest error output:\n{excerpt_text or "No output was captured from this command."}')


def run_logged_command(identifier, command_line, log, timeout, expected_output=None, editor_log=None):
    """Preserve child output on failure/timeout, including Unity's separate log."""
    started_at = time.time()
    reason = None
    try:
        result = subprocess.run(command_line, cwd=ROOT, capture_output=True, text=True,
                                encoding="utf-8", errors="replace", timeout=timeout)
        stdout, stderr = result.stdout, result.stderr
        if result.returncode:
            reason = f'exit {result.returncode}'
        elif expected_output is not None and expected_output not in stdout:
            reason = f'expected test harness did not run: {expected_output}'
    except subprocess.TimeoutExpired as exc:
        # TimeoutExpired can contain bytes even when text=True.
        def decoded(value):
            return value.decode('utf-8', errors='replace') if isinstance(value, bytes) else (value or '')
        stdout, stderr = decoded(exc.stdout), decoded(exc.stderr)
        reason = f'timed out after {timeout} seconds'
    except OSError as exc:
        stdout, stderr = '', str(exc)
        reason = 'could not start the command'
    output = (stdout + stderr).replace(str(ROOT), '<repository>')
    log.parent.mkdir(parents=True, exist_ok=True)
    log.write_text(output, encoding='utf-8')
    primary_log = log
    if editor_log is not None:
        # An old log may describe a different failure. Never diagnose from it.
        if editor_log.is_file() and editor_log.stat().st_mtime >= started_at:
            primary_log = editor_log
            if reason:
                output += '\n' + editor_log.read_text(encoding='utf-8', errors='replace').replace(str(ROOT), '<repository>')
        elif reason:
            output += '\nUnity did not write a fresh Editor log for this attempt.'
    if reason:
        raise CommandFailure(command_failure_message(identifier, reason, output, primary_log), primary_log)
    return primary_log


def run(args) -> tuple[dict, Path]:
    manifest = load_json(ROOT / "config/toolchain.json")
    validate_file(manifest, ROOT / "schemas/build/toolchain.schema.json")
    report_path = relative_output(args.report, f"diagnostics/{args.action}-report.json")
    report = {"schemaVersion": 1, "action": args.action, "recordedAtUtc": datetime.now(timezone.utc).isoformat(),
              "result": "pass", "sourceRevision": "unversioned",
              "environment": {"os": platform.system(), "python": platform.python_version(), "unityVersion": None},
              "checks": [], "limitations": ["Development diagnostics only; no physical, safety or gate acceptance."]}

    def check(identifier, result, message, **details):
        report["checks"].append({"id": identifier, "result": result, "message": message, "details": details})
        print(f"{result.upper()}: {identifier}: {message}", flush=True)

    def command(identifier, command_line, timeout=PYTHON_TEST_TIMEOUT_SECONDS, expected_output=None, editor_log=None):
        log = report_path.parent / (args.action + "-" + identifier + ".log")
        try:
            primary_log = run_logged_command(identifier, command_line, log, timeout, expected_output, editor_log)
        except CommandFailure as exc:
            check(identifier, 'fail', str(exc), command=[str(x) for x in command_line],
                  output=exc.log.relative_to(ROOT).as_posix())
            raise
        check(identifier, 'pass', 'exit 0', command=[str(x) for x in command_line],
              output=primary_log.relative_to(ROOT).as_posix())

    try:
        report["sourceRevision"] = source_revision(ROOT) or "unversioned"
        minimum = tuple(map(int, manifest["python"]["minimumVersion"].split(".")))
        maximum = tuple(map(int, manifest["python"]["maximumExclusiveVersion"].split(".")))
        if not minimum <= sys.version_info[:2] < maximum:
            raise ValueError("Unsupported Python; use the range in config/toolchain.json")
        check("python", "pass", platform.python_version())
        editor, version = editor_path(manifest, args.unity_editor)
        report["environment"]["unityVersion"] = version
        check("unity-installation", "pass" if editor else "deferred", version or "Exact Editor not found; supply --unity-editor or ELTS_UNITY_EDITOR")
        py = [sys.executable, "-X", "utf8"]

        if args.action in {"bootstrap", "doctor"}:
            for audit in ("verify_openvr.py", "verify_unity_packages.py"):
                if (ROOT / "tools/dependencies" / audit).is_file():
                    command(audit.removesuffix(".py"), py + ["tools/dependencies/" + audit])
            if args.action == "bootstrap":
                # Never overwrite a machine's local configuration. The stager
                # validates an existing file or rejects it with an exact error.
                local = ROOT / "config/local.json"
                if not local.exists():
                    publish_new(local, (ROOT / "config/local.example.json").read_bytes())
                    check("local-config", "pass", "Created config/local.json from the whitelist template")
                else:
                    check("local-config", "pass", "Preserved existing config/local.json")
                stage(ROOT)
                check("staging", "pass", "Synthetic configuration validated and staged")
            else:
                stage(ROOT, check=True)
                check("staging", "pass", "Generated configuration matches source")
            if not editor:
                raise ValueError("Install the exact selected Unity Editor through Unity Hub, then rerun; no license agreement was accepted here")
            if not (ROOT / "unity/ProjectSettings/ProjectVersion.txt").is_file():
                check("unity-project", "deferred", "Unity project creation/import is a later bootstrap step")
            else:
                project_version = (ROOT / "unity/ProjectSettings/ProjectVersion.txt").read_text(encoding="utf-8")
                if f"m_EditorVersion: {manifest['unity']['version']}\n" not in project_version.replace("\r\n", "\n"):
                    raise ValueError("Unity ProjectVersion.txt disagrees with toolchain manifest")
                check("unity-project-version", "pass", "Project and toolchain versions agree; runtime tests are separate")

        elif args.action == "stage":
            output = Path(args.output) if args.output else None
            stage(ROOT, output, check=args.check)
            check("staging", "pass", "Source and generated configuration verified")

        elif args.action == "test":
            selected = args.suite
            for suite, folder in (("validation", "tools/validation"), ("config", "tools/config"), ("bootstrap", "tools/bootstrap"), ("dependencies", "tools/dependencies"), ("build-provenance", "tools/build"), ("logging", "tools/logging")):
                if selected in {"all", "baseline", suite}:
                    command(suite, py + ["-m", "unittest", "discover", "-s", folder, "-p", "test_*.py", "-v"])
            if selected in {"all", "geometry", "runtime"}:
                dotnet = shutil.which("dotnet")
                if not dotnet:
                    raise ValueError("Install the standalone test SDK recorded in config/toolchain.json")
                command("geometry", [dotnet, "run", "--project", "tools/runtime-tests/GeometryChecks.csproj"], expected_output=" geometry checks")
                if selected != "geometry":
                    if not editor:
                        raise ValueError("Runtime config console tests need the verified Editor Newtonsoft assembly")
                    stage(ROOT)
                    json_dll = editor.parent / "Data/Managed/Newtonsoft.Json.dll"
                    pins_path = ROOT / "config/unity-packages.json"
                    if pins_path.is_file():
                        pins = load_json(pins_path)
                        version = next(p["version"] for p in pins["required"] if p["id"] == "com.unity.nuget.newtonsoft-json")
                        candidates = [p for p in (ROOT / "unity/Library/PackageCache").glob("com.unity.nuget.newtonsoft-json@*/Runtime/Newtonsoft.Json.dll")
                                      if load_json(p.parent.parent / "package.json")["version"] == version]
                        if len(candidates) != 1:
                            raise ValueError("Import the project to resolve exactly one pinned Newtonsoft runtime assembly")
                        json_dll = candidates[0]
                    command("runtime-config", [dotnet, "run", "--project", "tools/runtime-tests/ConfigChecks.csproj",
                            "-p:NewtonsoftPath=" + str(json_dll), "--", str(ROOT / "unity/Assets/StreamingAssets/config-generated")], expected_output=" configuration checks")
                    # Explicit registry excludes fixture generators and soak
                    # tools that need arguments or longer execution budgets.
                    runtime_checks = (
                        ("TrackingChecks", " tracking checks"),
                        ("SyntheticTrackingChecks", " synthetic tracking checks"),
                        ("DesktopTrackingChecks", " desktop tracking checks"),
                        ("Dev03ReviewChecks", "DEV-03 review regressions"),
                        ("LoggingChecks", " logging checks"),
                        ("LoggingPublicationChecks", " logging publication checks"),
                        ("LoggingThroughputChecks", " logging throughput checks"),
                        ("RenderingChecks", " rendering checks"),
                        ("ReplayChecks", " replay checks"),
                        ("ScenarioChecks", " scenario checks"),
                        ("SessionChecks", " session checks"),
                        ("CollectionChecks", " collection checks"),
                        ("SessionRecordingAdapterChecks", " session recording adapter checks"),
                        ("SessionAcquisitionChecks", " session acquisition checks"),
                        ("CalibrationChecks", " calibration checks"),
                        ("EltsLinkChecks", " synthetic ELTS-link checks"),
                        ("EltsSessionChecks", " ELTS session adapter checks"),
                    )
                    for project, banner in runtime_checks:
                        command(project, [dotnet, "run", "--configuration", "Release", "--project",
                                "tools/runtime-tests/" + project + ".csproj", "-p:NewtonsoftPath=" + str(json_dll)], expected_output=banner)
                    command("analysis", py + ["-m", "unittest", "discover", "-s", "analysis/tests", "-p", "test_*.py", "-v"])
            if selected in {"all", "unity-edit", "unity-play"}:
                smoke = ROOT / "unity/Assets/Tests/EditMode/Elts.EditModeTests.asmdef"
                if not editor or not smoke.is_file():
                    if selected != "all":
                        raise ValueError("Unity project/test assemblies are not ready; complete BOOT.S022")
                    check("unity-tests", "deferred", "Unity project/test assemblies not yet available")
                else:
                    for suite, platform_name in (("unity-edit", "EditMode"), ("unity-play", "PlayMode")):
                        if selected in {"all", suite}:
                            result_file = relative_output("", "test-results/" + suite + ".xml")
                            result_file.parent.mkdir(parents=True, exist_ok=True)
                            started_at = time.time()
                            log = report_path.parent / (suite + ".log")
                            command(suite, [str(editor), "-batchmode", "-projectPath", str(ROOT / "unity"),
                                    "-runTests", "-testPlatform", platform_name, "-testResults", str(result_file),
                                    "-logFile", str(log)], UNITY_TIMEOUT_SECONDS, editor_log=log)
                            try:
                                validate_unity_results(result_file, started_at)
                            except ValueError as exc:
                                raise ValueError(f'{exc}\nHow to resolve: Inspect the failed or missing test results, '
                                                 'then fix the reported test or send these files to the maintainer. '
                                                 f'Rerun SETUP-DEV.cmd after the fix.\nResults: {result_file}\nLog: {log}') from exc

        elif args.action == "build":
            if not editor or not (ROOT / "unity/Assets/Editor/EltsBuild/BuildEntry.cs").is_file():
                raise ValueError("Explicit Unity Windows build entry is not ready; complete BOOT.S023")
            stage(ROOT)
            output = relative_output(args.output, "build/windows-synthetic")
            output.mkdir(parents=True, exist_ok=True)
            from tools.build.provenance import prepare, finalize
            provenance = prepare(ROOT, output, manifest)
            command("unity-build", [str(editor), "-batchmode", "-quit", "-projectPath", str(ROOT / "unity"),
                    "-buildTarget", manifest["unity"]["target"], "-executeMethod", "Elts.Editor.BuildEntry.BuildWindows",
                    "-eltsBuildOutput", str(output), "-eltsEditorVersion", manifest["unity"]["version"],
                    "-eltsBuildVersion", provenance["version"], "-logFile", str(output / "unity-build.log")], UNITY_TIMEOUT_SECONDS,
                    editor_log=output / "unity-build.log")
            if not (output / "ELTS-Synthetic.exe").is_file():
                raise ValueError("Unity build exited without the expected Windows executable")
            finalize(output, provenance)

        else:
            raise ValueError("Study provisioning/packaging/startup is unavailable: approved study configuration, physical acceptance and release gates remain pending. Use development instructions.")
    except Exception as exc:
        report["result"] = "fail"
        check("failure", "fail", str(exc), remediation="Fix the stated input/check, preserve diagnostics, and rerun.")
    validate_file(report, ROOT / "schemas/diagnostics/doctor-report.schema.json")
    atomic_json(report_path, report)
    return report, report_path


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--action", required=True, choices=ACTIONS)
    parser.add_argument("--unity-editor")
    parser.add_argument("--suite", default="all", choices=("all", "baseline", "validation", "config", "geometry", "runtime", "unity-edit", "unity-play"))
    parser.add_argument("--output")
    parser.add_argument("--report")
    parser.add_argument("--check", action="store_true")
    report, path = run(parser.parse_args())
    print("Report: " + str(path.relative_to(ROOT)))
    raise SystemExit(0 if report["result"] == "pass" else 1)


if __name__ == "__main__":
    main()
