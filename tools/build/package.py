"""Assemble or verify a portable synthetic-development handoff (standard library).

The player keeps its original build manifest. This outer manifest binds the
instructions, analysis utilities, sample run and evidence to that player.
Hashes detect accidental changes; they are not signatures or study approval.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import shutil
import subprocess
import sys
import zipfile
from datetime import datetime, timezone
from pathlib import Path, PurePosixPath

ROOT = Path(__file__).resolve().parents[2]
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))
from tools.build.provenance import sha
from tools.build.verify import verify as verify_player
from tools.logging.verify_run import verify_run
from tools.progress.schema import load_json

PACKAGE_SCHEMA = "elts.development-package.v1"
# Only this documented output directory may grow after the operator runs the
# player. Packaged code/configuration/evidence remain checked byte-for-byte.
RUNTIME_OUTPUT = "player/data/synthetic/"
FILES = {
    "docs/operator/development-package.md": "README.md",
    "docs/operator/hardware-follow-up.md": "HARDWARE-FOLLOW-UP.md",
    "docs/modules/calibration.md": "docs/calibration.md",
    "docs/modules/session.md": "docs/session.md",
    "docs/modules/elts-session.md": "docs/elts-session.md",
    "analysis/run_ingest.py": "analysis/run_ingest.py",
    "analysis/fixtures/synthetic-calibration.json": "analysis/fixtures/synthetic-calibration.json",
    "tools/progress/schema.py": "tools/progress/schema.py",
    "tools/logging/verify_run.py": "tools/logging/verify_run.py",
    "tools/build/provenance.py": "tools/build/provenance.py",
    "tools/build/verify.py": "tools/build/verify.py",
    "tools/build/package.py": "tools/build/package.py",
}


def safe_relative(name: str) -> PurePosixPath:
    path = PurePosixPath(name)
    if not name or path.is_absolute() or ".." in path.parts or "\\" in name or ":" in name:
        raise ValueError("Unsafe manifest path: " + name)
    if path.as_posix() != name or any(part in {"", "."} for part in name.split("/")):
        raise ValueError("Noncanonical manifest path: " + name)
    return path


def package_files(root: Path) -> dict[str, str]:
    files = {}
    for path in sorted(root.rglob("*")):
        if path.is_symlink() or (hasattr(path, "is_junction") and path.is_junction()):
            raise ValueError("Package must not contain symlinks or junctions")
        if path.is_file():
            name = path.relative_to(root).as_posix()
            # Interpreter bytecode is disposable runtime output, not source.
            generated_bytecode = "__pycache__" in path.parts and path.suffix == ".pyc" and name.startswith(("analysis/", "tools/"))
            if generated_bytecode or name.startswith(RUNTIME_OUTPUT):
                continue
            if name != "PACKAGE-MANIFEST.sha256":
                files[name] = sha(path)
    return files


def verify_package(directory: Path) -> dict:
    root = directory.resolve(strict=True)
    expected = {}
    for line in (root / "PACKAGE-MANIFEST.sha256").read_text(encoding="utf-8").splitlines():
        digest, name = line.split("  ", 1)
        safe_relative(name)
        if name in expected or len(digest) != 64 or any(c not in "0123456789abcdef" for c in digest):
            raise ValueError("Invalid or duplicate package digest")
        expected[name] = digest
    actual = package_files(root)
    if expected != actual:
        changed = sorted(k for k in expected.keys() | actual.keys() if expected.get(k) != actual.get(k))
        raise ValueError("Package files missing, unexpected or changed: " + ", ".join(changed[:8]))
    required = {"README.md", "HARDWARE-FOLLOW-UP.md", "package-info.json", "player/build-info.json", "player/MANIFEST.sha256", "player/ELTS-Synthetic.exe"}
    if not required.issubset(expected):
        raise ValueError("Required package products missing")
    info = load_json(root / "package-info.json")
    if info.get("schemaVersion") != PACKAGE_SCHEMA or info.get("mode") != "synthetic-development" or info.get("studyReady") is not False:
        raise ValueError("Package is not an authorized synthetic-development format")
    if info["sourceBuildInfoSha256"] != sha(root / "player/build-info.json"):
        raise ValueError("Source player provenance changed")
    build_info = load_json(root / "player/build-info.json")
    if build_info.get("studyReady") is not False or build_info.get("mode") != "synthetic-development":
        raise ValueError("Player authority conflicts with package")
    if info.get("sampleRun") is not None:
        safe_relative(info["sampleRun"])
        if verify_run(root / info["sampleRun"])["synthetic"] is not True:
            raise ValueError("Only synthetic sample recordings may be packaged")
    return info


def create_package(build: Path, output: Path, evidence: list[Path], sample_run: Path | None = None) -> dict:
    build = build.resolve(strict=True)
    output = output.resolve()
    # A package is an independent snapshot, never an overlay on a player tree.
    if output.exists() or output == build or build in output.parents or output in build.parents:
        raise ValueError("Use a new, separate package output directory")
    if not output.is_relative_to(ROOT) or output.relative_to(ROOT).parts[0] not in {"release", "test-results"}:
        raise ValueError("Package output must be under release/ or test-results/ in this checkout")
    archive = output.with_suffix(".zip")
    if archive.exists():
        raise ValueError("Package archive already exists; choose a new output name")
    build_info = verify_player(build)
    if not evidence:
        raise ValueError("At least one passing evidence report is required")
    reports = []
    for path in evidence:
        path = path.resolve(strict=True)
        report = load_json(path)
        if not isinstance(report, dict) or report.get("result") != "pass":
            raise ValueError("Evidence must be a passing JSON report: " + path.name)
        if path.name in {p.name for p in reports}:
            raise ValueError("Evidence filenames must be unique")
        reports.append(path)
    if sample_run is not None:
        sample_run = sample_run.resolve(strict=True)
        if verify_run(sample_run)["synthetic"] is not True:
            raise ValueError("Sample run must be synthetic")
    # Validate the allowlist before creating anything. No logs, credentials or
    # arbitrary workspace trees are copied into the distributable.
    for source in FILES:
        if not (ROOT / source).is_file():
            raise ValueError("Required package source is missing: " + source)
    output.mkdir(parents=True)
    shutil.copytree(build, output / "player", ignore=shutil.ignore_patterns("*.log"))
    for source, destination in FILES.items():
        path = output / destination
        path.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(ROOT / source, path)
    for source_dir, pattern in (("analysis/src/elts_analysis", "*.py"), ("schemas/logs", "*.json")):
        for source in sorted((ROOT / source_dir).glob(pattern)):
            destination = output / source.relative_to(ROOT)
            destination.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(source, destination)
    for report in reports:
        (output / "evidence").mkdir(exist_ok=True)
        shutil.copy2(report, output / "evidence" / report.name)
    if sample_run is not None:
        destination = output / "samples/synthetic-run"
        destination.mkdir(parents=True)
        for name in ("samples.ndjson", "targets.ndjson", "events.ndjson", "session-summary.json"):
            shutil.copy2(sample_run / name, destination / name)
    revision = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip()
    info = {
        "schemaVersion": PACKAGE_SCHEMA, "mode": "synthetic-development", "studyReady": False,
        "createdAtUtc": datetime.now(timezone.utc).isoformat(), "recipeSourceRevision": revision,
        "recipeSha256": sha(Path(__file__)), "playerVersion": build_info["version"],
        "playerSourceRevision": build_info["commit"], "playerSourceDirty": build_info["dirty"],
        "sourceBuildInfoSha256": sha(build / "build-info.json"),
        "evidenceSha256": {p.name: sha(p) for p in reports},
        "sampleRun": "samples/synthetic-run" if sample_run else None,
        "runtimeOutput": RUNTIME_OUTPUT, "secondMachineTest": "unavailable; same-machine copied-output tests only",
        "limitations": ["Synthetic and unmeasured configuration only", "No study release or physical safety acceptance", "Hashes are integrity records, not authenticated signatures"],
    }
    (output / "package-info.json").write_text(json.dumps(info, indent=2, allow_nan=False) + "\n", encoding="utf-8")
    files = package_files(output)
    (output / "PACKAGE-MANIFEST.sha256").write_text("".join(digest + "  " + name + "\n" for name, digest in files.items()), encoding="utf-8")
    verify_package(output)
    # Exclusive archive creation also protects an earlier handoff of this name.
    with zipfile.ZipFile(archive, "x", compression=zipfile.ZIP_DEFLATED, compresslevel=6) as bundle:
        for path in sorted(output.rglob("*")):
            if path.is_file():
                bundle.write(path, output.name + "/" + path.relative_to(output).as_posix())
    return {"result": "pass", "mode": "synthetic-development", "studyReady": False,
            "packageDirectory": str(output), "archive": str(archive), "archiveSha256": sha(archive), **info}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--verify", type=Path, help="Read-only check of an existing package")
    parser.add_argument("--build", type=Path)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--evidence", action="append", type=Path, default=[])
    parser.add_argument("--sample-run", type=Path)
    args = parser.parse_args()
    if args.verify:
        result = {"result": "pass", **verify_package(args.verify)}
    elif args.build and args.output:
        if args.sample_run is None:
            parser.error("Development handoffs require --sample-run with a verified synthetic recording")
        result = create_package(args.build, args.output, args.evidence, args.sample_run)
    else:
        parser.error("Use --verify DIRECTORY or --build DIRECTORY --output NEW_DIRECTORY --evidence REPORT")
    print(json.dumps(result, indent=2, allow_nan=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
