"""Portable developer entry points, driven by config/toolchain.json.

Prerequisites are detected, never silently installed or licensed. Reports describe
only checks actually run. Missing study approval cannot fall back to development.
"""
from __future__ import annotations

import argparse
import json
import os
import platform
import shutil
import subprocess
import sys
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT))
from tools.config.stage import stage
from tools.progress.schema import load_json, validate_file

# Engineering timeouts, not study timing or exposure limits.
PYTHON_TEST_TIMEOUT_SECONDS = 600
UNITY_TIMEOUT_SECONDS = 1800
ACTIONS = ("bootstrap", "doctor", "test", "build", "stage", "package-study", "setup-study-machine")


def relative_output(value: str, default: str) -> Path:
    """Keep generated reports/builds out of sources and private deployment data."""
    raw = Path(value or default)
    path = raw if raw.is_absolute() else ROOT / raw
    resolved = path.resolve()
    if not resolved.is_relative_to(ROOT) or resolved == ROOT:
        raise ValueError("Generated output must stay inside this checkout")
    rel = resolved.relative_to(ROOT)
    if rel.parts[0] not in {"build", "diagnostics", "test-results", "release"}:
        raise ValueError("Generated output must be in build/, diagnostics/, test-results/ or release/")
    return resolved


def atomic_json(path: Path, value: dict):
    from tools.progress.store import replace_generated, json_bytes
    replace_generated(path, json_bytes(value))


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


def run(args) -> tuple[dict, Path]:
    manifest = load_json(ROOT / "config/toolchain.json")
    validate_file(manifest, ROOT / "schemas/build/toolchain.schema.json")
    report_path = relative_output(args.report, f"diagnostics/{args.action}-report.json")
    revision = subprocess.run(["git", "rev-parse", "HEAD"], cwd=ROOT, capture_output=True, text=True)
    report = {"schemaVersion": 1, "action": args.action, "recordedAtUtc": datetime.now(timezone.utc).isoformat(),
              "result": "pass", "sourceRevision": revision.stdout.strip() or "unversioned",
              "environment": {"os": platform.system(), "python": platform.python_version(), "unityVersion": None},
              "checks": [], "limitations": ["Development diagnostics only; no physical, safety or gate acceptance."]}

    def check(identifier, result, message, **details):
        report["checks"].append({"id": identifier, "result": result, "message": message, "details": details})
        print(f"{result.upper()}: {identifier}: {message}", flush=True)

    def command(identifier, command_line, timeout=PYTHON_TEST_TIMEOUT_SECONDS):
        result = subprocess.run(command_line, cwd=ROOT, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=timeout)
        log = report_path.parent / (args.action + "-" + identifier + ".log")
        log.parent.mkdir(parents=True, exist_ok=True)
        log.write_bytes((result.stdout + result.stderr).replace(str(ROOT), "<repository>").encode("utf-8"))
        check(identifier, "pass" if result.returncode == 0 else "fail", "exit " + str(result.returncode),
              command=[str(x) for x in command_line], output=log.relative_to(ROOT).as_posix())
        if result.returncode:
            raise ValueError(f"{identifier} failed; inspect {log.relative_to(ROOT)}")

    try:
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
            command("plan", py + ["tools/plan/build_plan.py", "--check"])
            command("progress", py + ["tools/progress/validate.py"])
            if args.action == "bootstrap":
                # Never overwrite a machine's local configuration. The stager
                # validates an existing file or rejects it with an exact error.
                local = ROOT / "config/local.json"
                if not local.exists():
                    from tools.progress.store import publish_new
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
            for suite, folder in (("plan", "tools/plan"), ("progress", "tools/progress"), ("config", "tools/config")):
                if selected in {"all", "baseline", suite}:
                    command(suite, py + ["-m", "unittest", "discover", "-s", folder, "-p", "test_*.py", "-v"])
            if selected in {"all", "geometry", "runtime"}:
                dotnet = shutil.which("dotnet")
                if not dotnet:
                    raise ValueError("Install the standalone test SDK recorded in config/toolchain.json")
                command("geometry", [dotnet, "run", "--project", "tools/runtime-tests/GeometryChecks.csproj"])
                if selected != "geometry":
                    if not editor:
                        raise ValueError("Runtime config console tests need the verified Editor Newtonsoft assembly")
                    stage(ROOT)
                    json_dll = editor.parent / "Data/Managed/Newtonsoft.Json.dll"
                    command("runtime-config", [dotnet, "run", "--project", "tools/runtime-tests/ConfigChecks.csproj",
                            "-p:NewtonsoftPath=" + str(json_dll), "--", str(ROOT / "unity/Assets/StreamingAssets/config-generated")])
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
                            log = report_path.parent / (suite + ".log")
                            command(suite, [str(editor), "-batchmode", "-projectPath", str(ROOT / "unity"),
                                    "-runTests", "-testPlatform", platform_name, "-testResults", str(result_file),
                                    "-logFile", str(log)], UNITY_TIMEOUT_SECONDS)
                            if not result_file.is_file():
                                raise ValueError("Unity exited without producing test results")

        elif args.action == "build":
            if not editor or not (ROOT / "unity/Assets/Editor/EltsBuild/BuildEntry.cs").is_file():
                raise ValueError("Explicit Unity Windows build entry is not ready; complete BOOT.S023")
            stage(ROOT)
            output = relative_output(args.output, "build/windows-synthetic")
            output.mkdir(parents=True, exist_ok=True)
            command("unity-build", [str(editor), "-batchmode", "-quit", "-projectPath", str(ROOT / "unity"),
                    "-buildTarget", manifest["unity"]["target"], "-executeMethod", "Elts.Editor.BuildEntry.BuildWindows",
                    "-eltsBuildOutput", str(output), "-logFile", str(output / "unity-build.log")], UNITY_TIMEOUT_SECONDS)
            if not (output / "ELTS-Synthetic.exe").is_file():
                raise ValueError("Unity build exited without the expected Windows executable")

        else:
            raise ValueError("Study provisioning/packaging is unavailable: approved study configuration, physical acceptance and release gates remain pending. Use development instructions.")
    except Exception as exc:
        report["result"] = "fail"
        check("failure", "fail", str(exc), remediation="Reconcile the stated input/check, preserve diagnostics, and rerun. Do not change progress manually.")
    validate_file(report, ROOT / "schemas/diagnostics/doctor-report.schema.json")
    atomic_json(report_path, report)
    return report, report_path


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--action", required=True, choices=ACTIONS)
    parser.add_argument("--unity-editor")
    parser.add_argument("--suite", default="all", choices=("all", "baseline", "plan", "progress", "config", "geometry", "runtime", "unity-edit", "unity-play"))
    parser.add_argument("--output")
    parser.add_argument("--report")
    parser.add_argument("--check", action="store_true")
    report, path = run(parser.parse_args())
    print("Report: " + str(path.relative_to(ROOT)))
    raise SystemExit(0 if report["result"] == "pass" else 1)


if __name__ == "__main__":
    main()
