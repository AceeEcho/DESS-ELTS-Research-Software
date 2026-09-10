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
import stat
import sys
from pathlib import Path
from tempfile import NamedTemporaryFile

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT))
from tools.validation.schema import ValidationError, validate_file  # noqa: E402

SCHEMAS = {"runtime": "runtime.schema.json", "rig": "rig.schema.json", "scenario": "scenario.schema.json", "session": "session.schema.json", "local": "local.schema.json"}
STAGED_SCHEMAS = ("effective.schema.json", *SCHEMAS.values())
OUTPUT_NAMES = {"effective": "effective-config.json", "manifest": "manifest.json"}
# Dimensionless cosine guard; keep this predicate aligned with the runtime
# GeometryTolerance.Basis. This is numerical validation, not lab calibration.
BASIS_ORTHOGONALITY_TOLERANCE = 1e-10


def canonical(value: object) -> bytes:
    return (json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(",", ":"), allow_nan=False) + "\n").encode("utf-8")


def sha256_bytes(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def _safe_path(root: Path, raw: str) -> Path:
    """Check lexical components before resolve, including Windows junctions.

    Resolving first would hide a link into another generated directory. Inspect
    every destination component before any file is read or replaced.
    """
    if not isinstance(raw, str) or not raw or any(c in raw for c in '\\:<>"|?*'):
        raise ValueError(f"Non-portable relative path: {raw!r}")
    parts = raw.split("/")
    reserved = {"con", "prn", "aux", "nul", "clock$"} | {f"{p}{i}" for p in ("com", "lpt") for i in range(1, 10)}
    if any(p in ("", ".", "..") or p.endswith((".", " ")) or p.split(".")[0].casefold() in reserved
           or any(ord(c) < 32 for c in p) for p in parts):
        raise ValueError(f"Non-canonical relative path: {raw!r}")
    current = root
    for part in parts:
        current = current / part
        try:
            attributes = current.lstat()
        except FileNotFoundError:
            continue
        if stat.S_ISLNK(attributes.st_mode) or getattr(attributes, "st_file_attributes", 0) & 0x400:
            raise ValueError(f"Path is symlinked or a reparse point: {raw}")
    resolved = current.resolve()
    if root not in resolved.parents:
        raise ValueError(f"Path escapes repository: {raw}")
    return resolved


def _safe_source(root: Path, raw: str) -> Path:
    if not isinstance(raw, str) or not raw.startswith("config/"):
        raise ValueError(f"Source must be a repository-relative config path: {raw!r}")
    path = _safe_path(root, raw)
    if not path.is_file():
        raise ValueError(f"Missing config source: {raw}")
    return path


def _load_json(path: Path) -> tuple[dict, bytes]:
    raw = path.read_bytes()
    def pairs(items):
        value = {}
        for key, item in items:
            if key in value: raise ValueError(f"Duplicate JSON key in {path}: {key}")
            value[key] = item
        return value
    def invalid(number): raise ValueError(f"Non-finite JSON number in {path}: {number}")
    try:
        value = json.loads(raw.decode("utf-8"), object_pairs_hook=pairs, parse_constant=invalid)
    except ValueError:
        raise
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
    if not lu > 1e-12 or not lv > 1e-12 or abs(dot) > BASIS_ORTHOGONALITY_TOLERANCE * lu * lv:
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
    reserved = {"con", "prn", "aux", "nul", "clock$", *(f"com{i}" for i in range(1, 10)), *(f"lpt{i}" for i in range(1, 10))}
    parts = data_root.split("/")
    if (data_root.startswith(("/", "//")) or ":" in data_root or "\\" in local["dataRoot"] or
            any(part in ("", ".", "..") or part.endswith((".", " ")) or part.lower().split(".")[0] in reserved
                or any(ord(c) < 32 or c in '<>"|?*' for c in part) for part in parts)):
        raise ValueError("dataRoot must be relative and may not traverse")
    if local["participantDisplayIndex"] > 31 or local["operatorDisplayIndex"] > 31:
        raise ValueError("Display index exceeds development bound 31")
    if local["participantDisplayIndex"] == local["operatorDisplayIndex"]:
        raise ValueError("Participant and operator displays must differ")


def build_bundle(root: Path) -> tuple[dict, dict]:
    root = root.resolve()
    staging_path = _safe_source(root, "config/staging.json")
    staging, staging_raw = _load_json(staging_path)
    validate_file(staging, _safe_path(root, "schemas/config/staging.schema.json"))
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
        validate_file(value, _safe_path(root, "schemas/config/" + schema_name))
        values[key] = value
        raw_hashes[key] = sha256_bytes(raw)
    _validate_rig(values["rig"]); _validate_local(values["local"])
    effective = {"schemaVersion": 1, "mode": "synthetic", "studyReady": False, "runtime": values["runtime"], "rig": values["rig"], "scenario": values["scenario"], "session": values["session"], "machine": values["local"]}
    schema_bytes = {name: _safe_path(root, "schemas/config/" + name).read_bytes() for name in STAGED_SCHEMAS}
    validate_file(effective, root / "schemas/config/effective.schema.json")
    manifest = {"schemaVersion": 1, "mode": "synthetic", "schema": "schemas/effective.schema.json", "effectiveConfig": "effective-config.json", "effectiveConfigSha256": sha256_bytes(canonical(effective)), "sourceRawSha256": {"staging": sha256_bytes(staging_raw), **raw_hashes}, "schemaFilesSha256": {name: sha256_bytes(data) for name, data in schema_bytes.items()}}
    return effective, manifest


def _atomic_write(path: Path, data: bytes) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    if path.is_file() and path.read_bytes() == data:
        return
    temporary = None
    try:
        with NamedTemporaryFile(dir=path.parent, prefix=f".{path.name}.", delete=False) as handle:
            temporary = Path(handle.name)
            handle.write(data); handle.flush(); os.fsync(handle.fileno())
        os.replace(temporary, path)
    except Exception:
        if temporary is not None: temporary.unlink(missing_ok=True)
        raise


def stage(root: Path, output: Path | None = None, check: bool = False) -> list[Path]:
    root = root.resolve()
    output = output or Path("unity/Assets/StreamingAssets/config-generated")
    if not output.is_absolute():
        output = root / output
    try:
        rel = output.relative_to(root)
    except ValueError as exc:
        raise ValueError("Output must remain inside the repository") from exc
    allowed = rel.as_posix() == "unity/Assets/StreamingAssets/config-generated" or len(rel.parts) >= 2 and rel.parts[0] in {"build", "release"}
    if not rel.parts or not allowed:
        raise ValueError("Output must be a generated subdirectory inside the repository")
    output = _safe_path(root, rel.as_posix())
    effective, manifest = build_bundle(root)
    expected = {output / OUTPUT_NAMES["effective"]: canonical(effective), output / OUTPUT_NAMES["manifest"]: canonical(manifest)}
    for name in STAGED_SCHEMAS:
        data = _safe_path(root, "schemas/config/" + name).read_bytes()
        if sha256_bytes(data) != manifest["schemaFilesSha256"][name]:
            raise ValueError("Schema changed during staging; retry from a stable source snapshot")
        expected[output / "schemas" / name] = data
    allowed_meta = {path.with_name(path.name + ".meta") for path in expected} | {output.with_name(output.name + ".meta"), (output / "schemas").with_name("schemas.meta")}
    actual = set(output.glob("**/*")) if output.is_dir() else set()
    for path in set(expected) | actual:
        _safe_path(root, path.relative_to(root).as_posix())
    allowed_directories = {output / "schemas"}
    extras = [p for p in actual if p not in expected and p not in allowed_meta and p not in allowed_directories]
    if not check and extras: raise ValueError("Unexpected existing generated output: " + ", ".join(str(p) for p in extras))
    if check:
        actual = {p for p in output.glob("**/*")} if output.is_dir() else set()
        drift = [str(p.relative_to(root)) for p, data in expected.items() if not p.is_file() or p.read_bytes() != data]
        drift += [str(p.relative_to(root)) for p in sorted(extras)]
        if drift: raise ValueError("Generated configuration drift: " + ", ".join(drift))
    else:
        for path, data in expected.items(): _atomic_write(path, data)
    return list(expected)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__); group = parser.add_mutually_exclusive_group(); group.add_argument("--write", action="store_true"); group.add_argument("--check", action="store_true"); parser.add_argument("--output")
    args = parser.parse_args(); root = ROOT; output = (root / args.output) if args.output and not Path(args.output).is_absolute() else (Path(args.output) if args.output else None); stage(root, output, args.check); print("PASS: synthetic configuration validated and staged")


if __name__ == "__main__": main()
