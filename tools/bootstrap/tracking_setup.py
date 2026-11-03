"""Discover SteamVR and safely configure headset-free tracking on Windows.

Only user overrides are edited. Runtime defaults, tracker pairing, room setup,
calibration profiles and research data are left alone.
"""
from __future__ import annotations

import argparse
import copy
import csv
from datetime import datetime, timezone
import io
import json
import os
from pathlib import Path
import re
import subprocess
import tempfile


OVERRIDES = {
    "steamvr": {"requireHmd": False, "forcedDriver": "null", "activateMultipleDrivers": True},
    "driver_null": {"enable": True},
}


def read_json(path: Path) -> dict:
    value = json.loads(path.read_text(encoding="utf-8-sig"))
    if not isinstance(value, dict):
        raise ValueError(f"Expected a JSON object: {path}")
    return value


def steam_installation() -> Path | None:
    """Prefer registered installations; allow non-default drives."""
    import winreg
    for hive, key, name in [
        (winreg.HKEY_CURRENT_USER, r"Software\Valve\Steam", "SteamPath"),
        (winreg.HKEY_LOCAL_MACHINE, r"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"),
    ]:
        try:
            with winreg.OpenKey(hive, key) as handle:
                candidate = Path(winreg.QueryValueEx(handle, name)[0])
            if (candidate / "steam.exe").is_file():
                return candidate
        except OSError:
            pass
    candidate = Path(os.environ.get("ProgramFiles(x86)", r"C:\Program Files (x86)")) / "Steam"
    return candidate if (candidate / "steam.exe").is_file() else None


def steam_libraries(steam: Path) -> list[Path]:
    libraries = [steam]
    file = steam / "steamapps/libraryfolders.vdf"
    if file.is_file():
        # Steam's modern VDF stores escaped Windows paths in "path" fields.
        # Older files store them directly under numeric keys.
        for raw in re.findall(r'"(?:path|\d+)"\s+"([^"\r\n]+)"', file.read_text(encoding="utf-8-sig")):
            candidate = Path(raw.replace("\\\\", "\\"))
            if candidate.is_absolute() and candidate not in libraries:
                libraries.append(candidate)
    return libraries


def runtime_present(path: Path) -> bool:
    return all((path / item).is_file() for item in (
        "bin/win64/vrserver.exe", "bin/win64/vrpathreg.exe",
        "drivers/null/bin/win64/driver_null.dll", "drivers/lighthouse/bin/win64/driver_lighthouse.dll",
    ))


def discover(steam: Path | None, openvr_paths: Path, explicit_runtime: Path | None = None) -> dict:
    registry = read_json(openvr_paths) if openvr_paths.is_file() else {}
    for key in ("runtime", "config", "log"):
        entries = registry.get(key) or []
        if not isinstance(entries, list) or any(not isinstance(p, str) or not Path(p).is_absolute() for p in entries):
            raise ValueError(f"Invalid {key} paths in {openvr_paths}; repair OpenVR registration before setup.")
    registered = [Path(p) for p in registry.get("runtime", []) or []]
    candidates = ([explicit_runtime] if explicit_runtime else registered)
    if steam and not explicit_runtime:
        candidates += [library / "steamapps/common/SteamVR" for library in steam_libraries(steam)]
    runtime = next((p for p in candidates if runtime_present(p)), None)
    # Honour OpenVR's registered config directory, including custom locations.
    configs = registry.get("config", []) or []
    config = Path(configs[0]) if configs else (steam / "config" if steam else None)
    return {
        "steam": str(steam) if steam else None,
        "runtime": str(runtime) if runtime else None,
        "config": str(config / "steamvr.vrsettings") if config else None,
        "openvrPaths": str(openvr_paths),
        "registeredRuntime": bool(runtime and registered and runtime == registered[0]),
        "registeredConfig": bool(configs),
        "registeredLog": bool(registry.get("log")),
    }


def merged_settings(original: dict) -> dict:
    result = copy.deepcopy(original)
    for section, values in OVERRIDES.items():
        if section in result and not isinstance(result[section], dict):
            raise ValueError(f"{section} is not a settings object; repair the existing file before setup.")
        result.setdefault(section, {}).update(values)
    return result


def runtime_running() -> bool:
    output = subprocess.check_output(["tasklist.exe", "/FO", "CSV", "/NH"], text=True, errors="replace")
    return any(row and row[0].lower() in {"vrserver.exe", "vrmonitor.exe", "vrcompositor.exe"}
               for row in csv.reader(io.StringIO(output)))


def configure(path: Path, *, check: bool = False, running=runtime_running) -> dict:
    before = path.read_bytes() if path.is_file() else None
    original = read_json(path) if before is not None else {}
    desired = merged_settings(original)
    # Compare types as well as values: JSON 0 is not the Boolean false.
    correct = all(type(original.get(section, {}).get(key)) is type(value) and
                  original[section][key] == value
                  for section, values in OVERRIDES.items() for key, value in values.items())
    if correct or check:
        return {"configured": correct, "changed": False, "backup": None}
    if running():
        raise ValueError("Close SteamVR completely, then rerun SETUP-TRACKING.cmd. Settings were not changed.")
    path.parent.mkdir(parents=True, exist_ok=True)
    backup = None
    if before is not None:
        stamp = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S%fZ")
        backup = path.with_name(path.name + ".before-elts-tracking-" + stamp + ".bak")
        with backup.open("xb") as stream:
            stream.write(before)
    temporary = None
    try:
        with tempfile.NamedTemporaryFile(dir=path.parent, prefix=".elts-tracking-", suffix=".tmp", delete=False) as stream:
            temporary = Path(stream.name)
            stream.write((json.dumps(desired, indent=2, ensure_ascii=False, allow_nan=False) + "\n").encode("utf-8"))
            stream.flush()
            os.fsync(stream.fileno())
        # Refuse to overwrite settings changed by another process mid-setup.
        if running() or (path.read_bytes() if path.exists() else None) != before:
            raise ValueError("SteamVR started or settings changed during setup. Close SteamVR and rerun.")
        os.replace(temporary, path)
        if read_json(path) != desired:
            raise ValueError(f"Settings verification failed. Original backup: {backup}")
    finally:
        if temporary is not None:
            temporary.unlink(missing_ok=True)
    return {"configured": True, "changed": True, "backup": str(backup) if backup else None}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--steam", type=Path)
    parser.add_argument("--runtime", type=Path)
    parser.add_argument("--discover", action="store_true")
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    try:
        if args.steam and not (args.steam / "steam.exe").is_file():
            raise ValueError("The supplied Steam folder does not contain steam.exe.")
        paths_file = Path(os.environ["LOCALAPPDATA"]) / "openvr/openvrpaths.vrpath"
        result = discover(args.steam or steam_installation(), paths_file, args.runtime)
        if not args.discover:
            if not result["runtime"] or not result["config"]:
                raise ValueError("SteamVR is not fully installed or its config folder is unknown. Finish its installation in Steam.")
            result.update(configure(Path(result["config"]), check=args.check))
        print(json.dumps(result))
        return 0 if args.discover or result["configured"] else 2
    except (OSError, ValueError, subprocess.SubprocessError) as error:
        print(json.dumps({"error": str(error)}))
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
