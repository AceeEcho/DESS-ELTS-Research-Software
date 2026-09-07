"""Import the approved planning sources into the deterministic task catalog.

This module is deliberately separate from live progress.  Workbook status cells
are retained as provenance in normalized exports, but are never interpreted as
observed completion.  Python 3.10+, standard library only.
"""
from __future__ import annotations

import argparse
import csv
import hashlib
import json
import sys
from pathlib import Path
from typing import Any

# Permit direct CLI execution from any working directory.
sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from tools.plan import build_plan
from tools.progress.schema import validate_file

SCHEMA_VERSION = 1
PLAN_VERSION = "ELTS-build-plan-1.0+PC-001"
SCHEMA_RELATIVE = "schemas/progress/task-catalog.schema.json"


def _sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def _json_bytes(value: Any) -> bytes:
    return (json.dumps(value, ensure_ascii=False, indent=2) + "\n").encode("utf-8")


def _registry(sheets: dict[str, list[list[Any]]], name: str) -> list[dict[str, Any]]:
    rows = sheets[name]
    headers = rows[4]
    return [{str(h): row[i] if i < len(row) else None for i, h in enumerate(headers) if h}
            for row in rows[5:] if any(v is not None for v in row)]


def _normalize_predicate(value: Any) -> Any:
    """Remove repeated conjuncts required by the catalog schema's uniqueness rule."""
    if not isinstance(value, dict):
        return value
    if "allOf" in value or "anyOf" in value:
        key = "allOf" if "allOf" in value else "anyOf"
        items = [_normalize_predicate(item) for item in value[key]]
        unique: list[Any] = []
        seen: set[str] = set()
        for item in items:
            marker = json.dumps(item, sort_keys=True, separators=(",", ":"))
            if marker not in seen:
                seen.add(marker)
                unique.append(item)
        return {key: unique}
    return dict(value)


def _catalog_definitions(items: list[dict[str, Any]]) -> list[dict[str, Any]]:
    result = []
    for item in items:
        copied = dict(item)
        if "eligibility" in copied:
            copied["eligibility"] = _normalize_predicate(copied["eligibility"])
        result.append(copied)
    return result


def _verify_sources(root: Path, rules: dict[str, Any]) -> None:
    baseline_dir = root / "project-management" / "baseline"
    sums: dict[str, str] = {}
    sums_path = baseline_dir / "SHA256SUMS"
    if sums_path.exists():
        for line in sums_path.read_text(encoding="utf-8").splitlines():
            fields = line.split()
            if len(fields) == 2:
                sums[fields[1]] = fields[0]
    for name, expected in rules["sourceHashes"].items():
        source = root / "deliverables" / name
        baseline = baseline_dir / name
        if not source.is_file() or not baseline.is_file():
            raise ValueError(f"Missing immutable source or baseline copy: {name}")
        actual = _sha256(source)
        if actual != expected:
            raise ValueError(f"Source hash changed: {name}")
        if _sha256(baseline) != expected or sums.get(name) != expected:
            raise ValueError(f"Immutable baseline copy changed: {name}")


def build_catalog(root: Path) -> dict[str, Any]:
    """Build and validate a catalog from *root*, independent of current cwd."""
    root = Path(root).resolve()
    rules_path = root / "docs" / "plan" / "amendment-rules.json"
    rules = json.loads(rules_path.read_text(encoding="utf-8"))
    _verify_sources(root, rules)
    plan_path = root / "docs" / "plan" / "approved-plan.json"
    plan = build_plan.build(rules)
    expected_plan = _json_bytes(plan)
    # Git may materialize this generated JSON with CRLF; compare decoded text
    # like the planner CLI while retaining the raw file hash as provenance.
    if not plan_path.is_file() or plan_path.read_text(encoding="utf-8") != expected_plan.decode("utf-8"):
        raise ValueError("Approved plan output is stale; run tools/plan/build_plan.py --write after review")
    workbook = root / "deliverables" / "ELTS_Implementation_and_Progress_Workbook.xlsx"
    sheets = build_plan.read_workbook(workbook)
    registries = {name: _registry(sheets, name) for name in sheets if len(sheets[name]) >= 5}
    deps = [row for row in registries["Dependencies"]
            if row.get("Dependency ID") not in set(plan["replacedDependencyIds"])]
    catalog = {
        "$schema": SCHEMA_RELATIVE,
        "schemaVersion": SCHEMA_VERSION,
        "project": "DESS-ELTS-Research-Software",
        "planVersion": PLAN_VERSION,
        "provenance": {
            "sourceHashes": rules["sourceHashes"],
            "amendmentId": rules["amendmentId"],
            "amendmentRulesSha256": _sha256(rules_path),
            "approvedPlanSha256": _sha256(plan_path),
        },
        "activeCounts": plan["activeCounts"],
        "tasks": _catalog_definitions(plan["taskDefinitions"]),
        "atomicSteps": _catalog_definitions(plan["atomicStepDefinitions"]),
        "phases": registries["Phases"],
        "decisions": registries["Decisions"],
        "gateCriteria": plan["gateCriteria"],
        "requirements": registries["Requirements Traceability"],
        "artifacts": registries["Artifacts"],
        "risks": plan["baselineRegistries"]["Risks"],
        "dependencies": deps,
        "bootstrapMappings": plan["bootstrapMappings"],
        "supersededBootstrapIds": plan["supersededBootstrapIds"],
        "replacedDependencyIds": plan["replacedDependencyIds"],
        "progressSelection": plan["progressSelection"],
    }
    schema = root / SCHEMA_RELATIVE
    validate_file(catalog, schema)
    return catalog


def _csv_bytes(rows: list[list[Any]]) -> bytes:
    import io
    stream = io.StringIO(newline="")
    writer = csv.writer(stream, lineterminator="\n")
    for row in rows:
        writer.writerow([json.dumps(v, ensure_ascii=False, separators=(",", ":")) if isinstance(v, (dict, list)) else "" if v is None else v for v in row])
    return stream.getvalue().encode("utf-8")


def export_outputs(root: Path, check: bool = False) -> list[Path]:
    """Write (or check) catalog.json and normalized JSON/CSV source sheets."""
    root = Path(root).resolve()
    catalog = build_catalog(root)
    workbook = root / "deliverables" / "ELTS_Implementation_and_Progress_Workbook.xlsx"
    sheets = build_plan.read_workbook(workbook)
    out = root / "project-management" / "exports"
    expected: dict[Path, bytes] = {out / "task-catalog.json": _json_bytes(catalog)}
    for name, rows in sheets.items():
        safe = name.lower().replace(" ", "-")
        expected[out / f"workbook-{safe}.json"] = _json_bytes(rows)
        expected[out / f"workbook-{safe}.csv"] = _csv_bytes(rows)
    if check:
        drift = [str(path.relative_to(root)) for path, data in expected.items()
                 if not path.is_file() or path.read_bytes() != data]
        if drift:
            raise ValueError("Export drift: " + ", ".join(drift))
    else:
        out.mkdir(parents=True, exist_ok=True)
        for path, data in expected.items():
            path.write_bytes(data)
    return list(expected)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    group = parser.add_mutually_exclusive_group()
    group.add_argument("--write", action="store_true")
    group.add_argument("--check", action="store_true")
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[2]
    export_outputs(root, check=args.check)
    print("PASS: deterministic task catalog and normalized workbook exports")


if __name__ == "__main__":
    main()
