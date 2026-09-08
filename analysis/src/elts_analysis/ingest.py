"""Version-aware, read-only ingest of completed ELTS logging runs."""
from __future__ import annotations
import argparse, hashlib, json, math, statistics, re, sys
from pathlib import Path
from typing import Any

ROOT = Path(__file__).resolve().parents[3]
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))
from tools.logging.verify_run import VerificationError, strict_json, verify_run
PRODUCTS = ("samples.ndjson", "events.ndjson", "targets.ndjson", "session-summary.json")
SAMPLE_SCHEMA = "elts.samples.v1"
EVENT_SCHEMA = "elts.events.v1"
TARGET_SCHEMA = "elts.targets.v1"
SUMMARY_SCHEMA = "elts.session-summary.v1"
BLOCK_TICKS = 300 * 10_000_000
SHA256_RE = re.compile(r"^[0-9a-f]{64}$")

class IngestError(ValueError):
    """Input is incomplete, malformed, unsupported, or violates analysis invariants."""

def _load_json(path: Path) -> Any:
    try:
        return strict_json(path.read_text(encoding="utf-8"), str(path))
    except (OSError, UnicodeError, json.JSONDecodeError, ValueError, VerificationError) as exc:
        raise IngestError(f"cannot parse {path.name}: {exc}") from exc

def _schema(value: Any, expected: str, where: str) -> None:
    if not isinstance(value, dict) or value.get("schemaVersion") != expected:
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
    return tuple(x/n for x in q)

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
    return {"muzzleOffsetMeters": _finite_vector(value.get("muzzleOffsetMeters"), "muzzleOffsetMeters"),
            "boreDirectionLocal": _norm(_finite_vector(value.get("boreDirectionLocal"), "boreDirectionLocal")),
            "zeroCorrectionQuaternion": _finite_quat(value.get("zeroCorrectionQuaternion"), "zeroCorrectionQuaternion"),
            "calibrationId": value.get("calibrationId", "unspecified")}

def _records(path: Path, expected: str) -> list[dict[str, Any]]:
    result=[]; previous_sequence=0; previous_ticks=-1
    try: handle=path.open("r", encoding="utf-8")
    except OSError as exc: raise IngestError(f"cannot read {path.name}: {exc}") from exc
    with handle:
        for line_no, line in enumerate(handle, 1):
            if not line.strip(): raise IngestError(f"blank line at {path.name}:{line_no}")
            try: value=strict_json(line, f"{path.name}:{line_no}")
            except (json.JSONDecodeError, VerificationError) as exc: raise IngestError(f"malformed JSON at {path.name}:{line_no}: {exc}") from exc
            _schema(value, expected, f"{path.name}:{line_no}")
            sequence = value.get("sequence")
            ticks = value.get("monotonicTicks")
            if not isinstance(sequence, int) or isinstance(sequence, bool) or sequence <= previous_sequence:
                raise IngestError(f"ordering violation at {path.name}:{line_no}: sequence must increase")
            if not isinstance(ticks, int) or isinstance(ticks, bool) or ticks < 0 or ticks < previous_ticks:
                raise IngestError(f"ordering violation at {path.name}:{line_no}: monotonicTicks must be nondecreasing")
            previous_sequence=sequence; previous_ticks=ticks; result.append(value)
    return result

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
    """Return true only when both paired trackers have usable valid poses."""
    for role in ("head", "weapon"):
        tracker=sample.get(role, {}); pose=tracker.get("pose")
        if tracker.get("validity") != "Valid" or tracker.get("connection") != "Connected" or not isinstance(pose, dict): return False
    return True

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
    samples=_records(directory/"samples.ndjson", SAMPLE_SCHEMA); events=_records(directory/"events.ndjson", EVENT_SCHEMA); targets=_records(directory/"targets.ndjson", TARGET_SCHEMA)
    target_by_tick=[]
    for row in targets:
        _require_string(row.get("targetId"), "targetId")
        _require_string(row.get("blockId"), "blockId")
        _finite_vector([row.get("worldPositionMeters", {}).get(k) for k in ("x", "y", "z")], "worldPositionMeters")
        target_by_tick.append((row["monotonicTicks"], row))
    valid=[]; invalid=0; errors=[]
    current_targets: dict[tuple[str, str], dict[str, Any]] = {}
    target_cursor = 0
    for row in samples:
        if not _sample_valid(row): invalid += 1; continue
        weapon=row["weapon"]; pose=weapon["pose"]; pos=tuple(float(pose["positionMeters"][k]) for k in ("x","y","z")); ori=tuple(float(pose["orientation"][k]) for k in ("x","y","z","w"))
        corrected=_qmul(ori, cal["zeroCorrectionQuaternion"]); muzzle=tuple(pos[i]+_qrotate(ori,cal["muzzleOffsetMeters"])[i] for i in range(3)); bore=_qrotate(corrected,cal["boreDirectionLocal"])
        tick=row["monotonicTicks"]
        candidates=[]
        # Fold target snapshots in timestamp order. A newer update replaces the
        # prior position; destruction removes that target from aim candidates.
        while target_cursor < len(target_by_tick) and target_by_tick[target_cursor][0] <= tick:
            _, target = target_by_tick[target_cursor]
            key = (target["blockId"], target["targetId"])
            if target.get("lifecycle") == "Destroyed":
                current_targets.pop(key, None)
            else:
                current_targets[key] = target
            target_cursor += 1
        for t in current_targets.values():
            tp=t["worldPositionMeters"]; target_pos=(tp["x"],tp["y"],tp["z"]); candidates.append((target_pos, _angle(bore,_sub(target_pos,muzzle)), t["targetId"]))
        if candidates:
            target_pos, error, target_id=min(candidates,key=lambda x:x[1]); errors.append(error); valid.append({"sequence":row["sequence"],"monotonicTicks":tick,"targetId":target_id,"aimErrorDegrees":error})
        else: valid.append({"sequence":row["sequence"],"monotonicTicks":tick,"targetId":None,"aimErrorDegrees":None})
    # Primary scores use explicit BlockStarted/BlockEnded markers. A fixed 300 s
    # window is measured from each marker; no timestamp-floor inference is used.
    markers = {}
    for event in events:
        kind = event.get("eventType"); payload = event.get("payload", {})
        if kind in ("BlockStarted", "BlockEnded"):
            block_id = payload.get("blockId")
            if not isinstance(block_id, str) or not block_id: raise IngestError(f"{kind} event {event['sequence']} requires scalar blockId")
            item = markers.setdefault(block_id, {}); key = "start" if kind == "BlockStarted" else "end"
            if key in item: raise IngestError(f"duplicate {kind} marker for block {block_id!r}")
            item[key] = event["monotonicTicks"]
    score_blocks = []
    for block_id, item in markers.items():
        if "start" not in item or "end" not in item: continue
        if item["end"] - item["start"] != BLOCK_TICKS: raise IngestError(f"block {block_id!r} must be exactly 300 seconds")
        score_blocks.append((block_id, item["start"], item["end"]))
    ordered_blocks = sorted(score_blocks, key=lambda block: block[1])
    if any(previous[2] > current[1] for previous, current in zip(ordered_blocks, ordered_blocks[1:])):
        raise IngestError("complete block intervals overlap")
    destroyed_seen = set()
    for event in events:
        if event.get("eventType") != "TargetDestroyed": continue
        payload = event.get("payload", {}); block_id, target_id = payload.get("blockId"), payload.get("targetId")
        if not isinstance(block_id, str) or not isinstance(target_id, str): raise IngestError(f"TargetDestroyed event {event['sequence']} requires scalar targetId and blockId")
        # A destruction in an incomplete/aborted block cannot complete a DV and
        # is retained in the raw stream without being assigned a score.
        if not [b for b in score_blocks if b[0] == block_id and b[1] <= event["monotonicTicks"] < b[2]]: continue
        key = (block_id, target_id)
        if key in destroyed_seen: raise IngestError(f"duplicate TargetDestroyed for {target_id!r} in block {block_id!r}")
        destroyed_seen.add(key)
    blocks = {block_id: {"blockId": block_id, "startTicks": start_tick, "endTicks": end_tick, "targetsDestroyed": 0, "shots": 0, "validSamples": 0, "invalidSamples": 0, "aimErrorsDegrees": []} for block_id, start_tick, end_tick in score_blocks}
    for event in events:
        for block_id, start_tick, end_tick in score_blocks:
            if start_tick <= event["monotonicTicks"] < end_tick:
                if event.get("eventType") == "TargetDestroyed":
                    if event.get("payload", {}).get("blockId") == block_id:
                        blocks[block_id]["targetsDestroyed"] += 1
                if event.get("eventType") == "ShotFired": blocks[block_id]["shots"] += 1
                break
    for row in samples:
        for block_id, start_tick, end_tick in score_blocks:
            if start_tick <= row["monotonicTicks"] < end_tick:
                b=blocks[block_id]
                if _sample_valid(row): b["validSamples"] += 1
                else: b["invalidSamples"] += 1
                break
    for row in valid:
        if row["aimErrorDegrees"] is None: continue
        for block_id, start_tick, end_tick in score_blocks:
            if start_tick <= row["monotonicTicks"] < end_tick: blocks[block_id]["aimErrorsDegrees"].append(row["aimErrorDegrees"]); break
    block_reports=[]
    for block_id,b in blocks.items():
        vals=b.pop("aimErrorsDegrees"); b["meanAimErrorDegrees"]=statistics.fmean(vals) if vals else None; b["medianAimErrorDegrees"]=statistics.median(vals) if vals else None; total=b["validSamples"]+b["invalidSamples"]; b["validSampleFraction"]=b["validSamples"]/total if total else None; block_reports.append(b)
    block_reports.sort(key=lambda x:x["startTicks"])
    if not block_reports: block_reports=[{"scoreStatus":"unavailable","reason":"no complete BlockStarted/BlockEnded interval in event stream"}]
    counts = summary.get("counts")
    if not isinstance(counts, dict): raise IngestError("session summary counts are required")
    expected_counts = {"writtenSamples": len(samples), "writtenEvents": len(events), "writtenTargets": len(targets)}
    for key, actual in expected_counts.items():
        if counts.get(key) != actual: raise IngestError(f"session summary {key} does not match input")
    report={"analysisVersion":"elts.analysis.v1","targetSelectionPolicy":"latest-nondestroyed-snapshot-per-block-target; nearest-angular-candidate","source":{"runDirectory":str(directory.resolve()),"sessionSummary":provenance,"rawInputs":raw},"calibration":{**cal,"sha256":_sha(calibration_path)[0]},"counts":{"samples":len(samples),"validSamples":sum(1 for x in samples if _sample_valid(x)),"invalidSamples":invalid,"events":len(events),"targets":len(targets)},"blocks":block_reports}
    if output_path is not None:
        try:
            output_path.parent.mkdir(parents=True, exist_ok=True)
            output_path.write_text(json.dumps(report,indent=2,sort_keys=True,allow_nan=False)+"\n",encoding="utf-8",newline="\n")
        except OSError as exc: raise IngestError(f"cannot write derived output: {exc}") from exc
    return report

def main(argv=None):
    p=argparse.ArgumentParser(description="Read-only ELTS v1 synthetic run ingest")
    p.add_argument("run_directory",type=Path); p.add_argument("--calibration",required=True,type=Path); p.add_argument("--output",type=Path)
    args=p.parse_args(argv)
    try: print(json.dumps(ingest_run(args.run_directory,args.calibration,args.output),indent=2,sort_keys=True,allow_nan=False)); return 0
    except IngestError as exc: print(f"INGEST FAILED: {exc}",file=__import__('sys').stderr); return 2
if __name__ == "__main__": raise SystemExit(main())
