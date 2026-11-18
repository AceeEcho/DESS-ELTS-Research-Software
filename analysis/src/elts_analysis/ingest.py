"""Version-aware, read-only ingest of completed ELTS logging runs."""
from __future__ import annotations
import argparse, hashlib, json, math, statistics, re, sys
from contextlib import closing
from pathlib import Path
import sqlite3
from typing import Any, Iterator

ROOT = Path(__file__).resolve().parents[3]
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))
from tools.logging.verify_run import VerificationError, strict_json, verify_run
PRODUCTS = ("samples.ndjson", "events.ndjson", "targets.ndjson", "session-summary.json")
SAMPLE_SCHEMA = "elts.samples.v2"
EVENT_SCHEMA = "elts.events.v1"
TARGET_SCHEMAS = ("elts.targets.v1", "elts.targets.v2")
SUMMARY_SCHEMA = "elts.session-summary.v2"
BLOCK_TICKS = 300 * 10_000_000
SHA256_RE = re.compile(r"^[0-9a-f]{64}$")

class IngestError(ValueError):
    """Input is incomplete, malformed, unsupported, or violates analysis invariants."""

def _load_json(path: Path) -> Any:
    try:
        return strict_json(path.read_text(encoding="utf-8"), str(path))
    except (OSError, UnicodeError, json.JSONDecodeError, ValueError, VerificationError) as exc:
        raise IngestError(f"cannot parse {path.name}: {exc}") from exc

def _schema(value: Any, expected: str | tuple[str, ...], where: str) -> None:
    allowed = (expected,) if isinstance(expected, str) else expected
    if not isinstance(value, dict) or value.get("schemaVersion") not in allowed:
        got = value.get("schemaVersion") if isinstance(value, dict) else None
        raise IngestError(f"unsupported schema at {where}: expected {expected}, got {got!r}")

def _sha(path: Path) -> tuple[str, int]:
    digest = hashlib.sha256(); size = 0
    try:
        with path.open("rb") as f:
            for chunk in iter(lambda: f.read(1024 * 1024), b""): digest.update(chunk); size += len(chunk)
    except OSError as exc: raise IngestError(f"cannot read {path}: {exc}") from exc
    return digest.hexdigest(), size

def _require_string(value: Any, name: str) -> str:
    if not isinstance(value, str) or not value:
        raise IngestError(f"{name} must be a non-empty string")
    return value

def _require_sha(value: Any, name: str) -> str:
    if not isinstance(value, str) or not SHA256_RE.fullmatch(value):
        raise IngestError(f"{name} must be a lowercase SHA-256 hex digest")
    return value

def _finite_vector(value: Any, name: str) -> tuple[float, float, float]:
    if not isinstance(value, (list, tuple)) or len(value) != 3: raise IngestError(f"{name} must contain three numbers")
    try:
        if any(type(x) not in (int, float) for x in value): raise TypeError
        result = tuple(float(x) for x in value)
    except (TypeError, ValueError) as exc: raise IngestError(f"{name} must contain three numbers") from exc
    if not all(math.isfinite(x) for x in result): raise IngestError(f"{name} must be finite")
    return result

def _finite_quat(value: Any, name: str) -> tuple[float, float, float, float]:
    if not isinstance(value, (list, tuple)) or len(value) != 4: raise IngestError(f"{name} must contain four numbers")
    try:
        if any(type(x) not in (int, float) for x in value): raise TypeError
        q = tuple(float(x) for x in value)
    except (TypeError, ValueError) as exc: raise IngestError(f"{name} must contain four numbers") from exc
    n = math.sqrt(sum(x*x for x in q))
    if not math.isfinite(n) or n <= 1e-12: raise IngestError(f"{name} must be a nonzero finite quaternion")
    if abs(n - 1.0) > 1e-8: raise IngestError(f"{name} must be unit length")
    return q

def _qmul(a, b):
    ax, ay, az, aw = a; bx, by, bz, bw = b
    return (aw*bx+ax*bw+ay*bz-az*by, aw*by-ax*bz+ay*bw+az*bx, aw*bz+ax*by-ay*bx+az*bw, aw*bw-ax*bx-ay*by-az*bz)

def _qrotate(q, v):
    p = (v[0], v[1], v[2], 0.0)
    r = _qmul(_qmul(q, p), (-q[0], -q[1], -q[2], q[3]))
    return r[:3]

def _norm(v):
    n = math.sqrt(sum(x*x for x in v))
    if not math.isfinite(n) or n <= 1e-12: raise IngestError("zero or nonfinite direction")
    return tuple(x/n for x in v)

def _sub(a,b): return tuple(a[i]-b[i] for i in range(3))
def _dot(a,b): return sum(a[i]*b[i] for i in range(3))
def _cross(a,b): return (a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0])
def _angle(a,b):
    aa, bb = _norm(a), _norm(b)
    return math.degrees(math.atan2(math.sqrt(sum(x*x for x in _cross(aa,bb))), _dot(aa,bb)))

def _calibration(path: Path) -> dict[str, Any]:
    value = _load_json(path)
    if not isinstance(value, dict): raise IngestError("calibration must be a JSON object")
    calibration_id = value.get("calibrationId")
    if not isinstance(calibration_id, str) or not calibration_id: raise IngestError("calibrationId must be a non-empty string")
    return {"muzzleOffsetMeters": _finite_vector(value.get("muzzleOffsetMeters"), "muzzleOffsetMeters"),
            "boreDirectionLocal": _norm(_finite_vector(value.get("boreDirectionLocal"), "boreDirectionLocal")),
            "zeroCorrectionQuaternion": _finite_quat(value.get("zeroCorrectionQuaternion"), "zeroCorrectionQuaternion"),
            "calibrationId": calibration_id}

def _records(path: Path, expected: str | tuple[str, ...], checksum: str) -> Iterator[dict[str, Any]]:
    """Parse ordered NDJSON incrementally and bind this pass to its verified bytes."""
    previous_sequence, previous_ticks = 0, -1
    version = None
    digest = hashlib.sha256()
    try:
        with path.open("rb") as handle:
            for line_no, raw in enumerate(handle, 1):
                digest.update(raw)
                location = f"{path.name}:{line_no}"
                if not raw.strip():
                    raise IngestError(f"blank line at {location}")
                try:
                    value = strict_json(raw.decode("utf-8"), location)
                except (UnicodeError, ValueError, VerificationError) as exc:
                    raise IngestError(f"malformed JSON at {location}: {exc}") from exc
                _schema(value, expected, location)
                if version is not None and value["schemaVersion"] != version:
                    raise IngestError(f"mixed target schema versions in {path.name}")
                version = value["schemaVersion"]
                sequence, ticks = value.get("sequence"), value.get("monotonicTicks")
                if type(sequence) is not int or sequence <= previous_sequence:
                    raise IngestError(f"ordering violation at {location}: sequence must increase")
                if type(ticks) is not int or ticks < 0 or ticks < previous_ticks:
                    raise IngestError(f"ordering violation at {location}: monotonicTicks must be nondecreasing")
                previous_sequence, previous_ticks = sequence, ticks
                yield value
    except OSError as exc:
        raise IngestError(f"cannot read {path.name}: {exc}") from exc
    if digest.hexdigest() != checksum:
        raise IngestError(f"{path.name} changed during analysis")


def _output_path(directory: Path, calibration: Path, output: str | Path | None) -> Path | None:
    if output is None:
        return None
    destination = Path(output).resolve(strict=False)
    run_root = directory.resolve(strict=True)
    cal_path = calibration.resolve(strict=True)
    if destination == cal_path or run_root == destination or run_root in destination.parents:
        raise IngestError("derived output must not replace a raw product or calibration")
    if destination.exists():
        raise IngestError(f"derived output already exists; refusing overwrite: {destination}")
    return destination

def _sample_valid(sample: dict[str, Any]) -> bool:
    """Return true only when both paired trackers have usable valid poses.

    In samples v2 a pose array is present exactly when a tracker is Connected and Valid."""
    return all(isinstance(sample.get(role, {}).get("pose"), list) for role in ("head", "weapon"))

def _validate_pose(tracker: dict[str, Any], role: str) -> tuple[tuple[float, float, float], tuple[float, float, float, float]]:
    """Split a v2 pose [x, y, z, qx, qy, qz, qw] into room-metre position and unit quaternion."""
    pose = tracker["pose"]
    if len(pose) != 7: raise IngestError(f"{role}.pose must contain seven numbers")
    return (_finite_vector(pose[:3], f"{role}.pose position"), _finite_quat(pose[3:], f"{role}.pose orientation"))

def ingest_run(run_directory: str | Path, calibration: str | Path, output: str | Path | None = None) -> dict[str, Any]:
    """Read a complete v1 run and write only a derived report when output is given."""
    directory=Path(run_directory); calibration_path=Path(calibration)
    if not directory.is_dir(): raise IngestError(f"run directory does not exist: {directory}")
    try:
        verify_run(directory)
    except VerificationError as exc:
        raise IngestError(f"logging verification failed: {exc}") from exc
    output_path = _output_path(directory, calibration_path, output)
    cal=_calibration(calibration_path)
    raw={}
    for name in PRODUCTS:
        path=directory/name
        if not path.is_file(): raise IngestError(f"missing required product: {name}")
        digest,size=_sha(path); raw[name]={"path":str(path),"sha256":digest,"bytes":size}
    summary=_load_json(directory/"session-summary.json"); _schema(summary,SUMMARY_SCHEMA,"session-summary.json")
    if summary.get("complete") is not True or summary.get("error") is not None: raise IngestError("session summary is not complete")
    provenance = summary.get("provenance")
    if not isinstance(provenance, dict): raise IngestError("session summary provenance is required")
    for key in ("configurationHash", "scenarioHash", "fixtureHash"):
        _require_sha(provenance.get(key), f"provenance.{key}")
    checksums = summary.get("checksumsSha256")
    if not isinstance(checksums, dict): raise IngestError("session summary checksumsSha256 is required")
    for name in ("samples.ndjson", "events.ndjson", "targets.ndjson"):
        expected = _require_sha(checksums.get(name), f"checksumsSha256.{name}")
        if expected != raw[name]["sha256"]: raise IngestError(f"{name} checksum does not match summary")
    def records(name, schema):
        return _records(directory/name, schema, checksums[name])

    # Primary scores still require the standard uninterrupted 300-second window.
    # Flexible synthetic tests retain explicit timing summaries without silently
    # presenting their counts as the standard dependent variable.
    markers = {}
    development_blocks = set()
    aborted_blocks = set()
    event_count = 0
    for event in records("events.ndjson", EVENT_SCHEMA):
        event_count += 1
        kind = event.get("eventType"); payload = event.get("payload", {})
        if kind == "BlockAborted" and isinstance(payload.get("blockId"), str):
            aborted_blocks.add(payload["blockId"])
        if kind in ("BlockPaused", "BlockResumed") or (
                kind in ("BlockStarted", "BlockEnded", "BlockDurationChanged") and payload.get("developmentTiming") is True):
            block_id = payload.get("blockId")
            if not isinstance(block_id, str) or not block_id:
                raise IngestError(f"{kind} requires a blockId for development timing")
            if summary.get("provenance", {}).get("synthetic") is not True:
                raise IngestError("Flexible development timing requires synthetic provenance")
            development_blocks.add(block_id)
        if kind in ("BlockStarted", "BlockEnded"):
            block_id = payload.get("blockId")
            if not isinstance(block_id, str) or not block_id: raise IngestError(f"{kind} event {event['sequence']} requires scalar blockId")
            item = markers.setdefault(block_id, {}); key = "start" if kind == "BlockStarted" else "end"
            if key in item: raise IngestError(f"duplicate {kind} marker for block {block_id!r}")
            item[key] = event["monotonicTicks"]
            if kind == "BlockEnded":
                item["activeSeconds"] = payload.get("activeSeconds")
                item["durationSeconds"] = payload.get("durationSeconds")
                item["reason"] = payload.get("reason", "elapsed")
    score_blocks = []
    for block_id, item in markers.items():
        if "start" not in item or "end" not in item: continue
        if item["end"] < item["start"]: raise IngestError(f"block {block_id!r} ends before it starts")
        if block_id in development_blocks: continue
        if item["end"] - item["start"] != BLOCK_TICKS: raise IngestError(f"block {block_id!r} must be exactly 300 seconds")
        if block_id in aborted_blocks: continue
        score_blocks.append((block_id, item["start"], item["end"]))
    ordered_blocks = sorted(score_blocks, key=lambda block: block[1])
    if any(previous[2] > current[1] for previous, current in zip(ordered_blocks, ordered_blocks[1:])):
        raise IngestError("complete block intervals overlap")
    blocks = {block_id: {"blockId": block_id, "startTicks": start, "endTicks": end,
                         "targetsDestroyed": 0, "shots": 0, "validSamples": 0, "invalidSamples": 0}
              for block_id, start, end in score_blocks}

    def active_block(tick):
        return next((blocks[key] for key, start, end in score_blocks if start <= tick < end), None)

    # SQLite creates a private temporary file and removes it on close. Exact medians
    # and duplicate checks do not require retaining one Python object per observation.
    with closing(sqlite3.connect("")) as scratch:
        scratch.execute("PRAGMA temp_store=FILE")
        scratch.execute("CREATE TABLE measurements (block TEXT, value REAL)")
        scratch.execute("CREATE TABLE destroyed (block TEXT, target TEXT, PRIMARY KEY(block,target))")
        for event in records("events.ndjson", EVENT_SCHEMA):
            kind, payload = event.get("eventType"), event.get("payload", {})
            block = active_block(event["monotonicTicks"])
            if kind == "TargetDestroyed":
                key, target = payload.get("blockId"), payload.get("targetId")
                if not isinstance(key, str) or not isinstance(target, str):
                    raise IngestError(f"TargetDestroyed event {event['sequence']} requires scalar targetId and blockId")
                if block is not None and key == block["blockId"]:
                    try:
                        scratch.execute("INSERT INTO destroyed VALUES (?,?)", (key, target))
                    except sqlite3.IntegrityError as exc:
                        raise IngestError(f"duplicate TargetDestroyed for {target!r} in block {key!r}") from exc
                    block["targetsDestroyed"] += 1
            elif kind == "ShotFired" and block is not None and payload.get("blockId", block["blockId"]) == block["blockId"]:
                block["shots"] += 1

        target_count = 0
        def target_records():
            nonlocal target_count
            for row in records("targets.ndjson", TARGET_SCHEMAS):
                _require_string(row.get("targetId"), "targetId")
                _require_string(row.get("blockId"), "blockId")
                _finite_vector([row.get("worldPositionMeters", {}).get(k) for k in ("x", "y", "z")], "worldPositionMeters")
                target_count += 1
                yield row

        sample_count = invalid = 0
        current_targets = {}
        with closing(target_records()) as target_stream:
            target = next(target_stream, None)
            for row in records("samples.ndjson", SAMPLE_SCHEMA):
                sample_count += 1
                block = active_block(row["monotonicTicks"])
                valid = _sample_valid(row)
                if block is not None:
                    block["validSamples" if valid else "invalidSamples"] += 1
                if not valid:
                    invalid += 1
                    continue
                _validate_pose(row["head"], "head")
                pos, ori = _validate_pose(row["weapon"], "weapon")
                corrected = _qmul(ori, cal["zeroCorrectionQuaternion"])
                offset = _qrotate(ori, cal["muzzleOffsetMeters"])
                muzzle = tuple(pos[i] + offset[i] for i in range(3))
                bore = _qrotate(corrected, cal["boreDirectionLocal"])
                while target is not None and target["monotonicTicks"] <= row["monotonicTicks"]:
                    key = (target["blockId"], target["targetId"])
                    if target.get("lifecycle") in ("Destroyed", "Despawned"):
                        current_targets.pop(key, None)
                    else:
                        point = target["worldPositionMeters"]
                        current_targets[key] = (point["x"], point["y"], point["z"])
                    target = next(target_stream, None)
                if current_targets:
                    error = min(_angle(bore, _sub(point, muzzle)) for point in current_targets.values())
                    if block is not None:
                        scratch.execute("INSERT INTO measurements VALUES (?,?)", (block["blockId"], error))
            # Validate and count trailing snapshots even if the sample stream ends first.
            for _ in target_stream:
                pass

        scratch.execute("CREATE INDEX by_block_and_value ON measurements(block,value)")
        block_reports = []
        for key, block in blocks.items():
            n = scratch.execute("SELECT COUNT(*) FROM measurements WHERE block=?", (key,)).fetchone()[0]
            block["rawSampleMeanAimErrorDegrees"] = None
            block["rawSampleMedianAimErrorDegrees"] = None
            if n:
                values = scratch.execute("SELECT value FROM measurements WHERE block=? ORDER BY rowid", (key,))
                block["rawSampleMeanAimErrorDegrees"] = statistics.fmean(value for (value,) in values)
                middle = scratch.execute("SELECT value FROM measurements WHERE block=? ORDER BY value LIMIT ? OFFSET ?",
                                         (key, 2 if n % 2 == 0 else 1, (n-1)//2))
                block["rawSampleMedianAimErrorDegrees"] = statistics.median(value for (value,) in middle)
            total = block["validSamples"] + block["invalidSamples"]
            block["validSampleFraction"] = block["validSamples"] / total if total else None
            block_reports.append(block)
    block_reports.sort(key=lambda x:x["startTicks"])
    for block_id in sorted(development_blocks):
        item = markers.get(block_id, {})
        if "start" not in item: continue
        block_reports.append({"blockId":block_id, "scoreStatus":"unavailable",
            "reason":"flexible synthetic test; excluded from the standard uninterrupted 300-second score",
            "startTicks":item["start"], "endTicks":item.get("end"),
            "activeSeconds":item.get("activeSeconds"), "durationSeconds":item.get("durationSeconds"),
            "termination":item.get("reason", "incomplete")})
    block_reports.sort(key=lambda x:x.get("startTicks", 0))
    if not block_reports: block_reports=[{"scoreStatus":"unavailable","reason":"no complete BlockStarted/BlockEnded interval in event stream"}]
    counts = summary.get("counts")
    if not isinstance(counts, dict): raise IngestError("session summary counts are required")
    expected_counts = {"writtenSamples": sample_count, "writtenEvents": event_count, "writtenTargets": target_count}
    for key, actual in expected_counts.items():
        if counts.get(key) != actual: raise IngestError(f"session summary {key} does not match input")
    # The official aim error is meanAimErrorDeg in the results export (elts.task-metrics.v1),
    # calculated from AimObserved events with the visible-target policy used for shots.
    # These raw 250 Hz sample figures are an independent cross-check with a different
    # target policy, named so the two cannot be mistaken for each other.
    report={"analysisVersion":"elts.analysis.v2","officialAimError":"results meanAimErrorDeg (elts.task-metrics.v1)","targetSelectionPolicy":"latest-nondestroyed-snapshot-per-block-target; nearest-angular-candidate","source":{"runDirectory":str(directory.resolve()),"sessionSummary":provenance,"rawInputs":raw},"calibration":{**cal,"sha256":_sha(calibration_path)[0]},"counts":{"samples":sample_count,"validSamples":sample_count-invalid,"invalidSamples":invalid,"events":event_count,"targets":target_count},"blocks":block_reports}
    if output_path is not None:
        try:
            output_path.parent.mkdir(parents=True, exist_ok=True)
            with output_path.open("x", encoding="utf-8", newline="\n") as handle:
                handle.write(json.dumps(report,indent=2,sort_keys=True,allow_nan=False)+"\n")
        except OSError as exc: raise IngestError(f"cannot write derived output: {exc}") from exc
    return report

def main(argv=None):
    p=argparse.ArgumentParser(description="Read-only ELTS v1 synthetic run ingest")
    p.add_argument("run_directory",type=Path); p.add_argument("--calibration",required=True,type=Path); p.add_argument("--output",type=Path)
    args=p.parse_args(argv)
    try: print(json.dumps(ingest_run(args.run_directory,args.calibration,args.output),indent=2,sort_keys=True,allow_nan=False)); return 0
    except IngestError as exc: print(f"INGEST FAILED: {exc}",file=__import__('sys').stderr); return 2
if __name__ == "__main__": raise SystemExit(main())
