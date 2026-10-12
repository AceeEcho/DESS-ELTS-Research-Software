"""Development build provenance. No release or study authorization is implied."""
from __future__ import annotations
import hashlib
import json
import shutil
import subprocess
from datetime import datetime, timezone
from pathlib import Path


def sha(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def source_revision(root: Path) -> str | None:
    """Read this folder's history only; a ZIP may be inside an unrelated repo."""
    if not (root / ".git").exists() or not shutil.which("git"):
        return None
    return subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=root).decode().strip()


def source_snapshot(root: Path) -> dict[str, str]:
    """Hash build inputs for copies without history, excluding caches/recordings.

    Local effective configuration is recorded separately below. Do not walk the
    whole folder: a copied installation can also contain private session data.
    """
    files = {}
    for folder in ("unity/Assets", "unity/Packages", "unity/ProjectSettings",
                   "config", "schemas", "tools/build", "tools/bootstrap", "scripts"):
        for path in sorted((root / folder).rglob("*")):
            relative = path.relative_to(root)
            if not path.is_file() or "__pycache__" in relative.parts:
                continue
            if relative.as_posix().startswith("config/local/") or relative.as_posix() == "config/local.json":
                continue
            files[relative.as_posix()] = sha(path)
    return files


def prepare(root: Path, output: Path, toolchain: dict) -> dict:
    if any(output.iterdir()):
        raise ValueError("Use an empty build output directory to avoid mixed or stale artifacts")
    def git(*args):
        return subprocess.check_output(["git", *args], cwd=root)
    commit = source_revision(root)
    if commit:
        status = git("status", "--porcelain=v1", "-z")
        diff = git("diff", "HEAD", "--binary")
        untracked = git("ls-files", "--others", "--exclude-standard", "-z").decode().split("\0")
        source = {
            "sourceKind": "git", "commit": commit,
            "tags": git("tag", "--points-at", "HEAD").decode().splitlines(),
            "dirty": bool(status), "diffSha256": hashlib.sha256(diff).hexdigest(),
            "untrackedSha256": {p: sha(root / p) for p in untracked if p},
        }
        version = "dev-" + commit[:12] + ("-dirty" if status else "-clean")
    else:
        snapshot = source_snapshot(root)
        digest = hashlib.sha256(json.dumps(snapshot, sort_keys=True).encode("utf-8")).hexdigest()
        # No invented commit or clean-worktree claim for a downloaded/copied folder.
        source = {"sourceKind": "unversioned-copy", "commit": None, "tags": [],
                  "dirty": None, "diffSha256": None, "untrackedSha256": {},
                  "sourceFilesSha256": snapshot, "sourceSha256": digest}
        version = "dev-copy-" + digest[:12]
    data = {
        "schemaVersion": 1, "mode": "synthetic-development", "studyReady": False,
        "version": version, **source,
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
