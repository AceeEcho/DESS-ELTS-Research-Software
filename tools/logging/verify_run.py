"""Read-only verifier for a completed ELTS logging v1 run.

The verifier streams NDJSON products without retaining samples in memory. It uses
ELTS's fail-closed schema vocabulary and keeps one parsed copy of each committed
schema for the whole run, including multi-hour recordings.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Callable, Mapping

from tools.progress.schema import ValidationError, load_json, validate


class VerificationError(ValueError):
    """A run is incomplete, malformed, tampered, or incompatible with logging v1."""


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
SCHEMA_DIRECTORY = REPOSITORY_ROOT / "schemas" / "logs"
PRODUCTS: tuple[tuple[str, str, str, str], ...] = (
    ("samples", "samples.ndjson", "samples.v1.schema.json", "writtenSamples"),
    ("events", "events.ndjson", "events.v1.schema.json", "writtenEvents"),
    ("targets", "targets.ndjson", "", "writtenTargets"),
)
SUMMARY_FILE = "session-summary.json"
SUMMARY_SCHEMA = "session-summary.v1.schema.json"


@dataclass(frozen=True)
class ProductResult:
    product: str
    lines: int
    checksum_sha256: str
    first_sequence: int | None
    last_sequence: int | None
    first_monotonic_ticks: int | None
    last_monotonic_ticks: int | None


def _reject_pairs(items: list[tuple[str, Any]]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for key, value in items:
        if key in result:
            raise VerificationError(f"Duplicate JSON key: {key}")
        result[key] = value
    return result


def _reject_nonfinite(value: str) -> Any:
    raise VerificationError(f"Non-finite JSON number: {value}")


def strict_json(text: str, location: str) -> Any:
    """Parse one JSON value while rejecting duplicate keys, NaN, and Infinity."""
    try:
        return json.loads(text, object_pairs_hook=_reject_pairs, parse_constant=_reject_nonfinite)
    except VerificationError:
        raise
    except (UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise VerificationError(f"Malformed JSON at {location}: {exc.msg}") from exc


def _schemas() -> dict[str, Mapping[str, Any]]:
    """Load each committed schema exactly once; per-line validation reuses these documents."""
    try:
        return {
            "samples": load_json(SCHEMA_DIRECTORY / "samples.v1.schema.json"),
            "events": load_json(SCHEMA_DIRECTORY / "events.v1.schema.json"),
            "targets.v1": load_json(SCHEMA_DIRECTORY / "targets.v1.schema.json"),
            "targets.v2": load_json(SCHEMA_DIRECTORY / "targets.v2.schema.json"),
            "summary": load_json(SCHEMA_DIRECTORY / SUMMARY_SCHEMA),
        }
    except (OSError, ValidationError) as exc:
        raise VerificationError(f"Unable to load committed logging schemas: {exc}") from exc


def _validate(value: Any, schema: Mapping[str, Any], location: str) -> None:
    try:
        # document= avoids reloading local $defs for every NDJSON line.
        validate(value, schema, base=SCHEMA_DIRECTORY, document=schema, location=location)
    except ValidationError as exc:
        raise VerificationError(str(exc)) from exc


def _require_safe_sample(sample: Mapping[str, Any], location: str) -> None:
    """Defend the no-interpolation boundary even if a future schema is loosened."""
    for role in ("head", "weapon"):
        tracker = sample[role]
        validity = tracker["validity"]
        pose = tracker["pose"]
        if validity == "Valid":
            if tracker["connection"] != "Connected" or not isinstance(pose, dict):
                raise VerificationError(f"{location}.{role}: valid tracking requires connected raw pose")
        elif pose is not None:
            raise VerificationError(f"{location}.{role}: invalid tracking must have null pose")


def _stream_product(run_directory: Path, product: str, file_name: str, schema: Mapping[str, Any] | None,
                    semantic_check: Callable[[Mapping[str, Any], str], None] | None,
                    target_schemas: Mapping[str, Mapping[str, Any]] | None = None) -> ProductResult:
    path = run_directory / file_name
    if not path.is_file():
        raise VerificationError(f"Missing required product: {file_name}")
    checksum = hashlib.sha256()
    lines = 0
    prior_sequence: int | None = None
    prior_ticks: int | None = None
    first_sequence: int | None = None
    first_ticks: int | None = None
    target_schema_version: str | None = None

    try:
        with path.open("rb") as handle:
            for raw in handle:
                checksum.update(raw)
                lines += 1
                location = f"{file_name}:{lines}"
                try:
                    text = raw.decode("utf-8")
                except UnicodeDecodeError as exc:
                    raise VerificationError(f"{location}: not UTF-8") from exc
                if not text.strip():
                    raise VerificationError(f"{location}: blank NDJSON line")
                value = strict_json(text, location)
                if product == "targets":
                    version = value.get("schemaVersion") if isinstance(value, dict) else None
                    if version not in ("elts.targets.v1", "elts.targets.v2"):
                        raise VerificationError(f"{location}: unsupported target schemaVersion")
                    if target_schema_version is None: target_schema_version = version
                    elif version != target_schema_version: raise VerificationError(f"{location}: mixed target schema versions in one stream")
                    if target_schemas is None: raise VerificationError(f"{location}: target schema registry is unavailable")
                    _validate(value, target_schemas[version], location)
                else:
                    if schema is None: raise VerificationError(f"{location}: missing product schema")
                    _validate(value, schema, location)
                if not isinstance(value, dict):  # Schema validation should already provide this invariant.
                    raise VerificationError(f"{location}: expected JSON object")
                sequence = value["sequence"]
                ticks = value["monotonicTicks"]
                if prior_sequence is not None and sequence <= prior_sequence:
                    raise VerificationError(f"{location}: sequence must strictly increase within {file_name}")
                if prior_ticks is not None and ticks < prior_ticks:
                    raise VerificationError(f"{location}: monotonicTicks moved backward within {file_name}")
                if semantic_check is not None:
                    semantic_check(value, location)
                if first_sequence is None:
                    first_sequence, first_ticks = sequence, ticks
                prior_sequence, prior_ticks = sequence, ticks
    except OSError as exc:
        raise VerificationError(f"Could not read {file_name}: {exc}") from exc

    return ProductResult(product, lines, checksum.hexdigest(), first_sequence, prior_sequence, first_ticks, prior_ticks)



def verify_run(run_directory: Path | str) -> dict[str, Any]:
    """Validate a complete, local v1 run and return a compact report dictionary.

    This function never modifies the run directory and never opens a product for writing.
    """
    directory = Path(run_directory)
    if not directory.is_dir():
        raise VerificationError(f"Run directory does not exist: {directory}")
    schemas = _schemas()
    target_schemas = {"elts.targets.v1": schemas["targets.v1"], "elts.targets.v2": schemas["targets.v2"]}
    summary_path = directory / SUMMARY_FILE
    if not summary_path.is_file():
        raise VerificationError(f"Missing required closure record: {SUMMARY_FILE}")
    try:
        summary = strict_json(summary_path.read_text(encoding="utf-8"), SUMMARY_FILE)
    except OSError as exc:
        raise VerificationError(f"Could not read {SUMMARY_FILE}: {exc}") from exc
    _validate(summary, schemas["summary"], SUMMARY_FILE)
    if not isinstance(summary, dict):
        raise VerificationError("session-summary.json: expected JSON object")
    if summary["complete"] is not True or summary["error"] is not None:
        raise VerificationError("session-summary.json does not represent a complete successful run")
    provenance = summary["provenance"]
    if not isinstance(provenance, dict) or not provenance.get("sourceRevision"):
        raise VerificationError("session-summary.json is missing source provenance")

    results: list[ProductResult] = []
    for product, file_name, _schema_name, count_name in PRODUCTS:
        semantic = _require_safe_sample if product == "samples" else None
        result = _stream_product(directory, product, file_name, schemas.get(product), semantic, target_schemas)
        expected_checksum = summary["checksumsSha256"].get(file_name)
        if result.checksum_sha256 != expected_checksum:
            raise VerificationError(f"{file_name}: SHA-256 does not match session summary")
        expected_count = summary["counts"].get(count_name)
        if result.lines != expected_count:
            raise VerificationError(f"{file_name}: {result.lines} lines but summary reports {expected_count}")
        results.append(result)

    return {
        "runId": summary["runId"],
        "schemaVersion": summary["schemaVersion"],
        "sourceRevision": provenance["sourceRevision"],
        "synthetic": provenance["synthetic"],
        "complete": True,
        "products": [result.__dict__ for result in results],
    }


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Validate a complete ELTS logging v1 run without modifying it.")
    parser.add_argument("run_directory", type=Path, help="Directory containing the four v1 log products")
    parser.add_argument("--report", type=Path, help="Write compact JSON verification report; no raw log data is copied")
    args = parser.parse_args(argv)
    try:
        report = verify_run(args.run_directory)
        rendered = json.dumps(report, sort_keys=True, indent=2, allow_nan=False) + "\n"
        if args.report is not None:
            args.report.write_text(rendered, encoding="utf-8", newline="\n")
        sys.stdout.write(rendered)
        return 0
    except VerificationError as exc:
        print(f"VERIFY FAILED: {exc}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
