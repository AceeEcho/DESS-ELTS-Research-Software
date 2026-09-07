"""Verify an intact development player directory without executing it."""
import argparse
import json
from pathlib import Path, PurePosixPath
from tools.build.provenance import sha


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
    actual = {p.relative_to(root).as_posix() for p in root.rglob("*")
              if p.is_file() and p.suffix != ".log" and p.name != "MANIFEST.sha256"}
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
