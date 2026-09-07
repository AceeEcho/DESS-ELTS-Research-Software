"""Validate and stage the synthetic configuration bundle.

Only files named by config/staging.json are read. Outputs are generated under
Unity's StreamingAssets directory and are written atomically. No live/local
file is modified; config/local.json is an optional machine overlay constrained
by the strict local schema.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
import sys
from pathlib import Path
from tempfile import NamedTemporaryFile

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT))
from tools.progress.schema import ValidationError, validate_file  # noqa: E402

SCHEMAS = {"runtime": "runtime.schema.json", "rig": "rig.schema.json", "scenario": "scenario.schema.json", "session": "session.schema.json", "local": "local.schema.json"}
OUTPUT_NAMES = {"effective": "effective-config.json", "manifest": "manifest.json"}


def canonical(value: object) -> bytes:
    return (json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(",", ":")) + "\n").encode("utf-8")


def sha256_bytes(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def _safe_source(root: Path, raw: str) -> Path:
    if not isinstance(raw, str) or not raw.startswith("config/") or "\\" in raw:
        raise ValueError(f"Source must be a repository-relative config path: {raw!r}")
    candidate = root / raw
    if candidate.is_symlink():
        raise ValueError(f"Missing, symlinked, or escaped config source: {raw}")
    path = candidate.resolve()
    config_root = (root / "config").resolve()
    if config_root not in path.parents or not path.is_file():
        raise ValueError(f"Missing, symlinked, or escaped config source: {raw}")
    return path


def _load_json(path: Path) -> tuple[dict, bytes]:
    raw = path.read_bytes()
    try:
        value = json.loads(raw.decode("utf-8"))
    except (UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise ValueError(f"Malformed JSON: {path}") from exc
    if not isinstance(value, dict):
        raise ValueError(f"Configuration root must be an object: {path}")
    return value, raw


def _finite_vec(value: list[float], name: str) -> None:
    if len(value) != 3 or not all(math.isfinite(float(x)) for x in value):
        raise ValueError(f"{name} must be a finite 3-vector")


def _validate_rig(rig: dict) -> None:
    display = rig["display"]
    a, b, c = (display[key] for key in ("lowerLeftM", "lowerRightM", "upperLeftM"))
    _finite_vec(a, "display.lowerLeftM"); _finite_vec(b, "display.lowerRightM"); _finite_vec(c, "display.upperLeftM")
    u = [b[i] - a[i] for i in range(3)]; v = [c[i] - a[i] for i in range(3)]
    dot = sum(u[i] * v[i] for i in range(3)); lu = math.sqrt(sum(x * x for x in u)); lv = math.sqrt(sum(x * x for x in v))
    if not lu > 1e-12 or not lv > 1e-12 or abs(dot) > 1e-9 * lu * lv:
        raise ValueError("Display edges must be nondegenerate and perpendicular")
    for key in ("headEyeOffsetM", "weaponMuzzleOffsetM", "weaponBoreLocalDirection"):
        _finite_vec(rig[key], key)
    bore = rig["weaponBoreLocalDirection"]
    if not math.isclose(math.sqrt(sum(x * x for x in bore)), 1.0, rel_tol=1e-9, abs_tol=1e-9):
        raise ValueError("weaponBoreLocalDirection must be unit length")
    q = rig["weaponZeroQuaternionXyzw"]
    if not math.isclose(math.sqrt(sum(float(x) * float(x) for x in q)), 1.0, rel_tol=1e-9, abs_tol=1e-9):
        raise ValueError("weaponZeroQuaternionXyzw must be unit length")
    serials = rig["trackerSerials"]
    if serials["head"] == serials["weapon"]:
        raise ValueError("Tracker serials must be distinct")
    if rig["calibration"]["rigId"] != rig["rigId"]:
        raise ValueError("Calibration rigId must match rigId")


def _validate_local(local: dict) -> None:
    data_root = local["dataRoot"].replace("\\", "/")
    if data_root.startswith("/") or any(part == ".." for part in data_root.split("/")):
        raise ValueError("dataRoot must be relative and may not traverse")
    if local["participantDisplayIndex"] == local["operatorDisplayIndex"]:
        raise ValueError("Participant and operator displays must differ")


def build_bundle(root: Path) -> tuple[dict, dict]:
    root = root.resolve()
    staging, _ = _load_json(root / "config/staging.json")
    validate_file(staging, root / "schemas/config/staging.schema.json")
    if staging["mode"] != "synthetic":
        raise ValueError("Only synthetic mode is supported by this bootstrap lane")
    values, raw_hashes = {}, {}
    for key, schema_name in SCHEMAS.items():
        source_name = staging[key]
        if key == "local" and (root / "config/local.json").is_file():
            source_name = "config/local.json"
            if staging[key] != "config/local.example.json":
                raise ValueError("staging local source must remain local.example.json")
        path = _safe_source(root, source_name)
        value, raw = _load_json(path)
        validate_file(value, root / "schemas/config" / schema_name)
        values[key] = value
        raw_hashes[key] = sha256_bytes(raw)
    _validate_rig(values["rig"]); _validate_local(values["local"])
    effective = {"schemaVersion": 1, "mode": "synthetic", "studyReady": False, "runtime": values["runtime"], "rig": values["rig"], "scenario": values["scenario"], "session": values["session"], "machine": values["local"]}
    manifest = {"schemaVersion": 1, "mode": "synthetic", "effectiveConfig": "effective-config.json", "effectiveConfigSha256": sha256_bytes(canonical(effective)), "sourceRawSha256": raw_hashes}
    return effective, manifest


def _atomic_write(path: Path, data: bytes) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    if path.is_file() and path.read_bytes() == data:
        return
    with NamedTemporaryFile(dir=path.parent, prefix=f".{path.name}.", delete=False) as handle:
        temporary = Path(handle.name)
        handle.write(data); handle.flush(); os.fsync(handle.fileno())
    os.replace(temporary, path)


def stage(root: Path, output: Path | None = None, check: bool = False) -> list[Path]:
    root = root.resolve(); output = (output or root / "unity/Assets/StreamingAssets/config-generated").resolve()
    if root not in output.parents:
        raise ValueError("Output must remain inside repository")
    effective, manifest = build_bundle(root)
    expected = {output / OUTPUT_NAMES["effective"]: canonical(effective), output / OUTPUT_NAMES["manifest"]: canonical(manifest)}
    if check:
        actual = {p for p in output.glob("*")} if output.is_dir() else set()
        drift = [str(p.relative_to(root)) for p, data in expected.items() if not p.is_file() or p.read_bytes() != data]
        drift += [str(p.relative_to(root)) for p in sorted(actual) if p not in expected]
        if drift: raise ValueError("Generated configuration drift: " + ", ".join(drift))
    else:
        for path, data in expected.items(): _atomic_write(path, data)
    return list(expected)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__); group = parser.add_mutually_exclusive_group(); group.add_argument("--write", action="store_true"); group.add_argument("--check", action="store_true"); parser.add_argument("--output")
    args = parser.parse_args(); root = ROOT; output = Path(args.output) if args.output else None; stage(root, output, args.check); print("PASS: synthetic configuration validated and staged")


if __name__ == "__main__": main()
