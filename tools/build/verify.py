"""Verify an intact development player directory without executing it."""
import argparse
import json
import re
from pathlib import Path, PurePosixPath
from tools.build.provenance import sha


def is_runtime_product(relative: str) -> bool:
    """Allow known recording products, never executable code, beneath dataRoot."""
    run_id = r"[A-Za-z0-9][A-Za-z0-9_-]{0,79}"
    reservation = re.fullmatch(r"\." + run_id + r"\.reservation", relative)
    product = re.fullmatch(run_id + r"/(samples\.ndjson|events\.ndjson|targets\.ndjson|session-summary\.json|"
                          r"participant\.json(\.pending)?|\.session-summary\.pending\.json|synthetic-calibration-[0-9a-f]{32}\.json|"
                          r"test-checkpoint-(WE|NE)_(FT|MT)-attempt-[0-9]{2,}\.json(\.pending)?)", relative)
    export = re.fullmatch(r"exports/" + run_id + r"-[0-9a-f]{32}\.zip(\.pending)?", relative)
    database = re.fullmatch(r"collection\.sqlite(?:-wal|-shm|-journal)?|exports/(?:collection|participant-[0-9a-f]{32})-[0-9a-f]{32}\.sqlite(?:\.pending)?(?:-wal|-shm|-journal)?", relative)
    return bool(reservation or product or export or database)


def verify(directory: Path) -> dict:
    root = directory.resolve()
    entries = {}
    for line in (root / "MANIFEST.sha256").read_text(encoding="utf-8").splitlines():
        digest, name = line.split("  ", 1)
        path = PurePosixPath(name)
        if path.is_absolute() or ".." in path.parts or "\\" in name or ":" in name or name in entries:
            raise ValueError("Unsafe or repeated manifest path")
        file = root / name
        if not file.resolve().is_relative_to(root) or not file.is_file() or sha(file) != digest:
            raise ValueError("Missing or changed artifact: " + name)
        entries[name] = digest
    if "build-info.json" not in entries or "ELTS-Synthetic.exe" not in entries:
        raise ValueError("Missing required build products")
    # Read dataRoot only after its packaged configuration passed the manifest
    # hash check. A recording must not make the launcher discard a healthy build,
    # but unexpected code still fails verification even inside that directory.
    config_name = "ELTS-Synthetic_Data/StreamingAssets/config-generated/effective-config.json"
    runtime_prefix = None
    if config_name in entries:
        config = json.loads((root / config_name).read_text(encoding="utf-8"))
        data_root = config["machine"]["dataRoot"].replace("\\", "/")
        relative = PurePosixPath(data_root)
        if relative.is_absolute() or ":" in data_root or any(part in {"", ".", ".."} for part in data_root.split("/")):
            raise ValueError("Unsafe runtime data root")
        runtime_prefix = relative.as_posix() + "/"
    actual = set()
    for file in root.rglob("*"):
        if file.is_symlink() or (hasattr(file, "is_junction") and file.is_junction()):
            raise ValueError("Build directory must not contain links or junctions")
        if not file.is_file() or file.suffix == ".log" or file.name == "MANIFEST.sha256":
            continue
        name = file.relative_to(root).as_posix()
        if name not in entries and runtime_prefix and name.startswith(runtime_prefix) and is_runtime_product(name[len(runtime_prefix):]):
            continue
        actual.add(name)
    if actual != set(entries):
        raise ValueError("Unexpected or missing build products")
    info = json.loads((root / "build-info.json").read_text(encoding="utf-8"))
    if info["studyReady"] is not False or info["mode"] != "synthetic-development":
        raise ValueError("Unsupported build authority")
    if info["artifactsSha256"] != {k: v for k, v in entries.items() if k != "build-info.json"}:
        raise ValueError("Artifact provenance and manifest disagree")
    return info


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    args = parser.parse_args()
    result = verify(args.directory)
    print("PASS: development build " + result["version"])
