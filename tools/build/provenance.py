"""Development build provenance. No release or study authorization is implied."""
from __future__ import annotations
import hashlib
import json
import subprocess
from datetime import datetime, timezone
from pathlib import Path


def sha(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def prepare(root: Path, output: Path, toolchain: dict) -> dict:
    if any(output.iterdir()):
        raise ValueError("Use an empty build output directory to avoid mixed or stale artifacts")
    def git(*args):
        return subprocess.check_output(["git", *args], cwd=root)
    commit = git("rev-parse", "HEAD").decode().strip()
    status = git("status", "--porcelain=v1", "-z")
    diff = git("diff", "HEAD", "--binary")
    untracked = git("ls-files", "--others", "--exclude-standard", "-z").decode().split("\0")
    version = "dev-" + commit[:12] + ("-dirty" if status else "-clean")
    data = {
        "schemaVersion": 1, "mode": "synthetic-development", "studyReady": False,
        "version": version, "commit": commit,
        "tags": git("tag", "--points-at", "HEAD").decode().splitlines(),
        "dirty": bool(status), "diffSha256": hashlib.sha256(diff).hexdigest(),
        "untrackedSha256": {p: sha(root / p) for p in untracked if p},
        "builtAtUtc": datetime.now(timezone.utc).isoformat(),
        "target": toolchain["unity"]["target"], "toolchain": toolchain,
        "schemasSha256": {p.relative_to(root).as_posix(): sha(p) for p in sorted((root / "schemas").rglob("*.json"))},
        "configurationSha256": {p.relative_to(root).as_posix(): sha(p) for p in sorted((root / "unity/Assets/StreamingAssets/config-generated").rglob("*.json"))},
        "packageManifestSha256": sha(root / "unity/Packages/manifest.json"),
        "packageLockSha256": sha(root / "unity/Packages/packages-lock.json"),
    }
    return data


def finalize(output: Path, data: dict) -> None:
    # Logs are diagnostics, not distributable provenance: they can contain credentials.
    products = sorted(p for p in output.rglob("*") if p.is_file() and p.suffix != ".log")
    data["artifactsSha256"] = {p.relative_to(output).as_posix(): sha(p) for p in products}
    info = output / "build-info.json"
    info.write_text(json.dumps(data, indent=2) + "\n", encoding="utf-8")
    products.append(info)
    (output / "MANIFEST.sha256").write_text("".join(
        sha(p) + "  " + p.relative_to(output).as_posix() + "\n" for p in sorted(products)), encoding="utf-8")
