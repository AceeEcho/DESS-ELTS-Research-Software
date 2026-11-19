"""Summarize what SteamVR recorded about tracking, and optionally clear one
tracker's learned IMU calibration.

SteamVR has no API for the moments that invalidate a tracking calibration: it
re-estimating which way is up (which turns its whole frame about the base
station), a tracker being reset, or the base station being bumped. They appear
only in its server log. This reads that log and SteamVR's lighthouse database.

    py -3 tools/diagnostics/steamvr_health.py
    py -3 tools/diagnostics/steamvr_health.py --reset-imu LHR-XXXXXXXX

The report is read-only. --reset-imu changes SteamVR's own database (after a
timestamped backup) and refuses to run while SteamVR is open. These are log
readings, not physical measurements of the rig.
"""
from __future__ import annotations

import argparse
import csv
import io
import json
import math
import os
import re
import subprocess
import sys
from datetime import datetime, timezone
from pathlib import Path

# A learned accelerometer scale this far from 1, or a bias this large (m/s^2), is
# not something a healthy tracker shows; SteamVR's estimate has probably been
# corrupted by bad tracking. Adjustable judgement values, not acceptance limits.
IMPLAUSIBLE_SCALE_ERROR = 0.05
IMPLAUSIBLE_BIAS = 0.5

TILT = re.compile(r"Setting universe tilt .*pitch (-?[0-9.]+) deg roll (-?[0-9.]+) deg")
SERIAL = re.compile(r"(LHR-[0-9A-F]+)")
STAMP = re.compile(r"^\w{3} (\w{3} \d{2} \d{4} \d{2}:\d{2}:\d{2})")


def steamvr_folders() -> tuple[Path, Path]:
    """SteamVR's registered config and log folders, or Steam's defaults."""
    config = log = None
    registry = Path(os.environ.get("LOCALAPPDATA", "")) / "openvr" / "openvrpaths.vrpath"
    try:
        paths = json.loads(registry.read_text(encoding="utf-8-sig"))
        config = Path(paths["config"][0])
        log = Path(paths["log"][0])
    except (OSError, ValueError, KeyError, IndexError, TypeError):
        pass
    steam = Path(os.environ.get("ProgramFiles(x86)", r"C:\Program Files (x86)")) / "Steam"
    return config or steam / "config", log or steam / "logs"


def summarize_log(lines: list[str]) -> dict:
    """Counts of the events that matter, for the most recent day in the log."""
    days: dict[str, list[str]] = {}
    for line in lines:
        stamp = STAMP.match(line)
        if stamp:
            days.setdefault(stamp.group(1)[:11], []).append(line)
    if not days:
        return {"day": None}
    day, recent = list(days.items())[-1]
    tilts, resets, mismatches, saturated, bumped = [], {}, {}, {}, 0
    for line in recent:
        tilt = TILT.search(line)
        serial = SERIAL.search(line)
        name = serial.group(1) if serial else "unknown"
        if tilt:
            tilts.append((float(tilt.group(1)), float(tilt.group(2))))
        elif "IMU misalignment unreasonably large" in line:
            mismatches[name] = mismatches.get(name, 0) + 1
        elif "Resetting tracking" in line:
            resets[name] = resets.get(name, 0) + 1
        elif "IMU went off scale" in line:
            saturated[name] = saturated.get(name, 0) + 1
        elif "reset count changed" in line:
            bumped += 1
    steps = [math.hypot(b[0] - a[0], b[1] - a[1]) for a, b in zip(tilts, tilts[1:])]
    return {
        "day": day.strip(), "frameRedefinitions": len(tilts),
        "largestStepDegrees": max(steps) if steps else 0.0,
        "typicalStepDegrees": sorted(steps)[len(steps) // 2] if steps else 0.0,
        "trackerLostSight": resets, "opticalInertialMismatch": mismatches,
        "imuSaturated": saturated, "baseStationDisturbed": bumped,
    }


def imu_report(config: Path) -> list[dict]:
    """Each tracker's learned accelerometer calibration beside its factory values."""
    database = config / "lighthouse" / "lighthousedb.json"
    try:
        learned = json.loads(database.read_text(encoding="utf-8-sig")).get("known_objects", [])
    except (OSError, ValueError):
        return []
    report = []
    for entry in learned:
        serial = entry.get("serialNumber", "")
        imu = entry.get("imu", {})
        scale, bias = imu.get("acc_scale", []), imu.get("acc_bias", [])
        factory = {}
        try:
            factory = json.loads((config / "lighthouse" / serial.lower() / "config.json").read_text(encoding="utf-8-sig")).get("imu", {})
        except (OSError, ValueError):
            pass
        suspect = (any(abs(value - 1) > IMPLAUSIBLE_SCALE_ERROR for value in scale)
                   or any(abs(value) > IMPLAUSIBLE_BIAS for value in bias))
        report.append({"serial": serial, "learnedScale": scale, "learnedBias": bias,
                       "factoryScale": factory.get("acc_scale"), "factoryBias": factory.get("acc_bias"),
                       "implausible": suspect})
    return report


def steamvr_running() -> bool:
    output = subprocess.check_output(["tasklist.exe", "/FO", "CSV", "/NH"], text=True, errors="replace")
    return any(row and row[0].lower() in {"vrserver.exe", "vrmonitor.exe", "vrcompositor.exe"}
               for row in csv.reader(io.StringIO(output)))


def reset_imu(config: Path, serial: str, running=steamvr_running) -> Path:
    """Remove one tracker's learned IMU calibration so SteamVR relearns it from the factory values."""
    database = config / "lighthouse" / "lighthousedb.json"
    if running():
        raise ValueError("Close SteamVR completely first; it rewrites this file when it exits.")
    original = database.read_bytes()
    data = json.loads(original.decode("utf-8-sig"))
    kept = [entry for entry in data.get("known_objects", []) if entry.get("serialNumber") != serial]
    if len(kept) == len(data.get("known_objects", [])):
        raise ValueError(f"SteamVR has no learned calibration for {serial}.")
    backup = database.with_name(database.name + ".before-elts-imu-reset-" +
                                datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ") + ".bak")
    with backup.open("xb") as stream:
        stream.write(original)
    data["known_objects"] = kept
    temporary = database.with_suffix(".elts-tmp")
    temporary.write_text(json.dumps(data, indent=3), encoding="utf-8")
    os.replace(temporary, database)
    return backup


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--reset-imu", metavar="SERIAL",
                        help="remove this tracker's learned IMU calibration (SteamVR must be closed; a backup is kept)")
    args = parser.parse_args()
    config, log = steamvr_folders()
    try:
        if args.reset_imu:
            backup = reset_imu(config, args.reset_imu)
            print(f"Removed the learned IMU calibration for {args.reset_imu}. Backup: {backup}")
            print("Start SteamVR and move the tracker in view of the base station for a minute so it relearns.")
            return 0
        server_log = log / "vrserver.txt"
        if not server_log.is_file():
            print(f"No SteamVR server log at {server_log}. Start SteamVR once, then run this again.")
            return 1
        summary = summarize_log(server_log.read_text(encoding="utf-8", errors="replace").splitlines())
        print(f"SteamVR log: {server_log}")
        if summary["day"] is None:
            print("  No dated lines found.")
        else:
            print(f"  Most recent day in the log: {summary['day']}")
            print(f"  SteamVR re-set its tracking frame {summary['frameRedefinitions']} times "
                  f"(typical step {summary['typicalStepDegrees']:.2f} deg, largest {summary['largestStepDegrees']:.2f} deg).")
            print(f"  Base station bumped or power cycled: {summary['baseStationDisturbed']}")
            for label, key in (("Tracker lost sight of the base station", "trackerLostSight"),
                               ("Optical and inertial poses disagreed", "opticalInertialMismatch"),
                               ("IMU saturated (hard knock)", "imuSaturated")):
                counts = ", ".join(f"{serial} x{count}" for serial, count in sorted(summary[key].items())) or "none"
                print(f"  {label}: {counts}")
        for tracker in imu_report(config):
            verdict = "IMPLAUSIBLE: consider --reset-imu " + tracker["serial"] if tracker["implausible"] else "plausible"
            print(f"  Learned accelerometer calibration, {tracker['serial']}: scale {tracker['learnedScale']}, "
                  f"bias {tracker['learnedBias']} (factory scale {tracker['factoryScale']}) -> {verdict}")
        print("These are readings of SteamVR's own records, not physical measurements.")
        return 0
    except (OSError, ValueError, subprocess.SubprocessError) as error:
        print(f"ERROR: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
