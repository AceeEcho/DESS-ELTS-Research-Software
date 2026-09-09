"""Run bootstrap checks and retain immutable, source-hashed software evidence.

This runner never emits completion events. It records what actually ran, including
failures; a successful report is evidence for a later reviewed transition.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import subprocess
import sys
from datetime import datetime, timezone
from pathlib import Path

from core import digest, file_hash
from store import json_bytes, publish_new

ROOT = Path(__file__).resolve().parents[2]
CHECKS = [
    ["tools/plan/build_plan.py", "--check"],
    ["-m", "unittest", "discover", "-s", "tools/plan", "-p", "test_*.py"],
    ["tools/progress/build_schemas.py", "--check"],
    ["tools/progress/import_plan.py", "--check"],
    ["-m", "unittest", "discover", "-s", "tools/progress", "-p", "test_*.py", "-v"],
]
SOURCE_FOLDERS = ("tools/progress", "tools/plan", "schemas/progress", "docs/plan", "project-management/baseline")


def snapshot(root):
    return {p.relative_to(root).as_posix(): file_hash(p)
            for folder in SOURCE_FOLDERS for p in sorted((root / folder).rglob("*"))
            if p.is_file() and "__pycache__" not in p.parts}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--label", default="bootstrap")
    args = parser.parse_args()
    if not args.label.replace("-", "").isalnum():
        raise SystemExit("Label must contain letters, numbers or hyphens")
    started = datetime.now(timezone.utc)
    report_dir = ROOT / "project-management/progress/evidence" / args.label / started.strftime("%Y%m%dT%H%M%S%fZ")
    source = snapshot(ROOT)
    revision = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip()
    results = []
    for number, command in enumerate(CHECKS, 1):
        ran = subprocess.run([sys.executable, "-X", "utf8", *command], cwd=ROOT, capture_output=True, text=True, encoding="utf-8")
        output = (ran.stdout + ran.stderr).replace(str(ROOT), "<repository>")
        log = report_dir / f"check-{number:02}.txt"
        publish_new(log, output.encode("utf-8"))
        results.append({"command": "python " + " ".join(command), "exitCode": ran.returncode,
                        "outputPath": log.relative_to(ROOT).as_posix(), "outputSha256": file_hash(log)})
        print(f"{'PASS' if ran.returncode == 0 else 'FAIL'}: {results[-1]['command']}", flush=True)
    unchanged = snapshot(ROOT) == source
    report = {"schemaVersion": 1, "evidenceClass": "automated_test", "startedAtUtc": started.isoformat(),
              "recordedAtUtc": datetime.now(timezone.utc).isoformat(), "sourceRevision": revision,
              "sourceSnapshotSha256": digest(source), "sourceFilesSha256": source, "sourceUnchangedDuringChecks": unchanged,
              "pythonVersion": sys.version.split()[0], "checks": results,
              "result": "pass" if unchanged and all(r["exitCode"] == 0 for r in results) else "fail",
              "limitations": ["Local software fixtures only; no physical criterion or gate approval.",
                              "Unity runtime/build validation is separate from progress bootstrap."]}
    path = report_dir / "report.json"
    publish_new(path, json_bytes(report))
    print(path.relative_to(ROOT).as_posix(), flush=True)
    if report["result"] != "pass":
        raise SystemExit(1)


if __name__ == "__main__":
    main()
