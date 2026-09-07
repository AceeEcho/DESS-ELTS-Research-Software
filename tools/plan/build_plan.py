"""Build/check the approved planning catalog; never write project progress.

Python 3.10+, standard library only. Paths resolve from this file, not the caller's
working directory. The original Office files are read as OOXML and never modified.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import posixpath
import re
from pathlib import Path
import xml.etree.ElementTree as ET
from zipfile import ZipFile

ROOT = Path(__file__).resolve().parents[2]
PLAN_DIR = ROOT / "docs" / "plan"
RULES_PATH = PLAN_DIR / "amendment-rules.json"
OUTPUT_PATH = PLAN_DIR / "approved-plan.json"
MAP_PATH = PLAN_DIR / "bootstrap-map.md"
X = "{http://schemas.openxmlformats.org/spreadsheetml/2006/main}"
W = "{http://schemas.openxmlformats.org/wordprocessingml/2006/main}"
R = "{http://schemas.openxmlformats.org/officeDocument/2006/relationships}"
IDS = re.compile(r"(?:P\d+\.\d+(?:\.[SV]\d+)?|BOOT\.S\d+|DEV-\d+(?:\.[SV]\d+)?|G\d+|D-\d+)")
ID_COLUMNS = {"Work Items": "Work Item ID", "Atomic Steps": "Atomic Step ID",
              "Decisions": "Decision ID", "Gate Criteria": "Criterion ID",
              "Dependencies": "Dependency ID", "Requirements Traceability": "Requirement ID",
              "Risks": "Risk ID"}


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def read_workbook(path: Path) -> dict:
    """Preserve every cell value/formula, including unused sheets, without Excel."""
    with ZipFile(path) as z:
        strings = []
        if "xl/sharedStrings.xml" in z.namelist():
            strings = ["".join(si.itertext()) for si in ET.fromstring(z.read("xl/sharedStrings.xml"))]
        rels = {e.attrib["Id"]: e.attrib["Target"] for e in ET.fromstring(z.read("xl/_rels/workbook.xml.rels"))}
        sheets = {}
        for sheet in ET.fromstring(z.read("xl/workbook.xml")).find(X + "sheets"):
            target = rels[sheet.attrib[R + "id"]]
            target = target.lstrip("/") if target.startswith("/") else posixpath.normpath("xl/" + target)
            rows = []
            for row in ET.fromstring(z.read(target)).iter(X + "row"):
                while len(rows) < int(row.attrib["r"]):
                    rows.append([])
                values = rows[int(row.attrib["r"]) - 1]
                for cell in row:
                    letters = re.match(r"[A-Z]+", cell.attrib["r"]).group()
                    col = 0
                    for letter in letters:
                        col = col * 26 + ord(letter) - 64
                    while len(values) < col:
                        values.append(None)
                    kind = cell.attrib.get("t")
                    raw = cell.findtext(X + "v")
                    if kind == "s":
                        value = strings[int(raw)]
                    elif kind == "inlineStr":
                        value = "".join(cell.find(X + "is").itertext())
                    elif kind in ("str", "e", "d"):
                        value = raw
                    elif kind == "b":
                        value = raw == "1"
                    elif raw is None:
                        value = None
                    else:
                        number = float(raw)
                        value = int(number) if number.is_integer() else number
                    formula = cell.findtext(X + "f")
                    if formula is not None:
                        value = {"formula": "=" + formula, "cachedValue": value}
                    values[col - 1] = value
            sheets[sheet.attrib["name"]] = rows
        return sheets


def registry(sheets: dict, name: str) -> list[dict]:
    rows = sheets[name]
    headers = rows[4]
    return [{str(h): row[i] if i < len(row) else None for i, h in enumerate(headers) if h}
            for row in rows[5:] if any(v is not None for v in row)]


def specification_bootstrap(path: Path) -> list[tuple[str, str]]:
    with ZipFile(path) as z:
        document = ET.fromstring(z.read("word/document.xml"))
    result = []
    for row in document.iter(W + "tr"):
        cells = ["".join(t.text or "" for t in c.iter(W + "t")) for c in row.findall(W + "tc")]
        if len(cells) >= 2 and re.fullmatch(r"P0\.3\.S\d{3}", cells[0]):
            result.append((cells[0], cells[1]))
    if [r[0] for r in result] != [f"P0.3.S{i:03}" for i in range(1, 27)]:
        raise ValueError("Specification Appendix C bootstrap identities changed")
    return result


def fact(identifier: str, state: str = "done") -> dict:
    return {"id": identifier, "state": state}


def conjunction(predicates) -> dict:
    return {"allOf": list(predicates)}


def references(predicate: dict) -> set[str]:
    if set(predicate) == {"id", "state"}:
        if predicate["state"] not in {"done", "passed", "decided"}:
            raise ValueError("Unknown required state")
        return {predicate["id"]}
    if len(predicate) != 1 or next(iter(predicate)) not in {"allOf", "anyOf"}:
        raise ValueError(f"Malformed eligibility predicate: {predicate}")
    children = next(iter(predicate.values()))
    if not isinstance(children, list) or ("anyOf" in predicate and not children):
        raise ValueError("Malformed predicate children")
    return set().union(*(references(p) for p in children))


def eligible(predicate: dict, facts: dict[str, str]) -> bool:
    """Pure planning predicate evaluation for fixtures, not a progress reducer."""
    if "id" in predicate:
        return facts.get(predicate["id"]) == predicate["state"]
    if "allOf" in predicate:
        return all(eligible(p, facts) for p in predicate["allOf"])
    return any(eligible(p, facts) for p in predicate["anyOf"])


def assert_acyclic(graph: dict[str, set[str]]) -> None:
    done, visiting = set(), []

    def visit(node):
        if node in visiting:
            raise ValueError("Dependency cycle: " + " -> ".join(visiting[visiting.index(node):] + [node]))
        if node in done:
            return
        visiting.append(node)
        for predecessor in sorted(graph.get(node, set())):
            visit(predecessor)
        visiting.pop()
        done.add(node)

    for node in sorted(graph):
        visit(node)


def select_execution(steps: list[dict], facts: dict, step_statuses: dict) -> str | None:
    """Demonstrate global selection independent of the baseline phase anchor."""
    candidates = []
    for step in steps:
        status = step_statuses.get(step["id"], "not_started")
        if status in {"done", "blocked"} or not eligible(step["eligibility"], facts):
            continue
        priority = {"in_progress": 0, "verification_pending": 1, "ready": 2, "not_started": 2}[status]
        candidates.append((priority, step["executionOrder"], step["id"]))
    return min(candidates)[2] if candidates else None


def build(rules: dict | None = None) -> dict:
    rules = rules or json.loads(RULES_PATH.read_text(encoding="utf-8"))
    if rules.get("status") != "accepted" or rules.get("amendmentId") != "PC-001" or rules.get("version") != 1:
        raise ValueError("Expected accepted PC-001 version 1 rules; review a new amendment before changing this contract")
    for name, expected in rules["sourceHashes"].items():
        if sha256(ROOT / "deliverables" / name) != expected:
            raise ValueError(f"Baseline hash changed: {name}; review before regeneration")
    workbook = ROOT / "deliverables/ELTS_Implementation_and_Progress_Workbook.xlsx"
    specification = ROOT / "deliverables/ELTS_Repository_Portability_and_Multi_LLM_Architecture_Specification.docx"
    sheets = read_workbook(workbook)
    regs = {name: registry(sheets, name) for name in rules["baselineCounts"]}
    for name, count in rules["baselineCounts"].items():
        rows = regs[name]
        # Column labels are retained exactly; IDs also appear in fixed baseline columns.
        column = ID_COLUMNS[name]
        if column not in rows[0]:
            raise ValueError(f"Missing expected ID header {name}: {column}; found {list(rows[0])}")
        ids = [r[column] for r in rows]
        if len(ids) != count or len(set(ids)) != count:
            raise ValueError(f"Count/identity mismatch in {name}")
    original_steps = regs["Atomic Steps"]
    wb_bootstrap = [s for s in original_steps if s["Parent Work Item"] == "P0.3"]
    mappings = rules["bootstrapWorkbookMappings"]
    if set(mappings) != {s["Atomic Step ID"] for s in wb_bootstrap}:
        raise ValueError("Bootstrap mapping must cover every baseline P0.3 atomic ID")
    bootstrap = []
    for n, (source_id, action) in enumerate(specification_bootstrap(specification), 1):
        identifier = source_id if n == 1 else f"BOOT.S{n:03}"
        previous = [] if n == 1 else [fact(bootstrap[-1]["id"])]
        # Requirement mapping retains full baseline action/evidence, not just a label.
        mapped = [s for s in wb_bootstrap if identifier in mappings[s["Atomic Step ID"]]]
        bootstrap.append({"id": identifier, "parent": "P0.3", "phase": "P0", "lane": "bootstrap",
                          "executionOrder": n, "sourceId": "spec:" + source_id,
                          "action": action, "retainedWorkbookRequirements": mapped,
                          "eligibility": conjunction(previous),
                          "verification": "Prove this specification outcome; retain evidence and limitations. Mapped workbook rows are traceability only: their distributed acceptance is checked at P0.3 closure, never required in full at every mapped step."})
    active_boot_ids = {s["id"] for s in bootstrap}
    if any(i not in active_boot_ids for values in mappings.values() for i in values):
        raise ValueError("Mapping target is not an active bootstrap action")
    # Mappings allocate requirements; they do not inherit conflicting order or old
    # progress-schema-before-import instructions. PC-001 supplies the new ordering.
    replaced = set(rules["replacedDependencyIds"])
    source_edges = regs["Dependencies"]
    if not replaced <= {e["Dependency ID"] for e in source_edges}:
        raise ValueError("Replacement references an unknown baseline edge")
    remaining = [e for e in source_edges if e["Dependency ID"] not in replaced]
    overrides = rules["p2TaskEligibility"]
    decision_scope = rules["deferredDecisionOverrides"]["D-12"]["removeDecidedPrerequisiteFrom"]
    tasks = []
    for row in regs["Work Items"]:
        identifier = row["Work Item ID"]
        predicate = overrides.get(identifier, conjunction(fact(e["Predecessor ID"], e["Required State"])
                    for e in remaining if e["Successor ID"] == identifier))
        tasks.append({"id": identifier, "phase": row["Phase"], "lane": "baseline",
                      "title": row["Title"], "eligibility": predicate, "source": row,
                      "decisionScopeOverride": rules["deferredDecisionOverrides"]["D-12"]["requiredScope"]
                      if identifier in decision_scope else None,
                      "completion": "All active atomic steps, baseline evidence and applicable decisions; no automatic completion from DEV support."})
    by_task = {t["id"]: t for t in tasks}
    steps = list(bootstrap)
    for row in original_steps:
        parent = row["Parent Work Item"]
        if parent == "P0.3":
            continue
        local_deps = IDS.findall(row["Depends On"] or "")
        if parent in overrides:
            local_deps = [i for i in local_deps if i.startswith(parent + ".")]
        predicates = [by_task[parent]["eligibility"]]
        predicates.extend(fact(i, "passed" if i.startswith("G") else "decided" if i.startswith("D-") else "done") for i in local_deps)
        decisions = IDS.findall(row["Blocking Decisions"] or "")
        predicates.extend(fact(i, "decided") for i in decisions if not (i == "D-12" and parent in decision_scope))
        steps.append({"id": row["Atomic Step ID"], "parent": parent, "phase": row["Phase"],
                      "lane": "baseline", "executionOrder": 1000 + row["Sequence"],
                      "action": row["Action"], "verification": row["Validation"],
                      "eligibility": conjunction(predicates), "source": row})
    for n, task in enumerate(rules["developmentTasks"], 1):
        if not task["completionDoesNotAdvanceParentsOrGates"]:
            raise ValueError("DEV tasks cannot complete baseline parents/gates")
        tasks.append({**task, "phase": "DEV", "completion": "Both DEV steps and their own synthetic evidence only."})
        first = task["id"] + ".S001"
        steps.extend([
            {"id": first, "parent": task["id"], "phase": "DEV", "lane": task["lane"],
             "executionOrder": 100 + n * 2, "action": task["action"],
             "verification": "Implementation and meaningful tests are reviewable; record source/config/fixture hashes.",
             "eligibility": task["eligibility"]},
            {"id": task["id"] + ".V001", "parent": task["id"], "phase": "DEV", "lane": task["lane"],
             "executionOrder": 101 + n * 2, "action": "Verify development work and record deferred physical evidence.",
             "verification": task["verification"],
             "eligibility": conjunction([task["eligibility"], fact(first)])}])
    steps.sort(key=lambda s: s["executionOrder"])
    known = {t["id"] for t in tasks} | {s["id"] for s in steps} | {f"G{i}" for i in range(9)} | {f"D-{i:02}" for i in range(1, 19)}
    if len({s["id"] for s in steps}) != len(steps) or len({t["id"] for t in tasks}) != len(tasks):
        raise ValueError("Duplicate active IDs")
    graph = {s["id"]: references(s["eligibility"]) for s in steps}
    for task in tasks:
        graph[task["id"]] = references(task["eligibility"]) | {s["id"] for s in steps if s["parent"] == task["id"]}
        if not set(task.get("supports", [])) <= set(by_task):
            raise ValueError("Unknown DEV support reference")
    unknown = set().union(*graph.values()) - known
    if unknown:
        raise ValueError(f"Unresolved effective dependency IDs: {sorted(unknown)}")
    assert_acyclic(graph)
    return {"formatVersion": 1, "kind": "approved_execution_plan_not_progress_state",
            "amendmentId": rules["amendmentId"], "amendmentVersion": rules["version"],
            "approval": {"status": rules["status"], "approvedBy": rules["approvedBy"], "date": rules["approvalDate"]},
            "sourceHashes": rules["sourceHashes"], "rulesSha256": sha256(RULES_PATH),
            "unityVersion": rules["unityVersion"], "baselineCounts": rules["baselineCounts"],
            "activeCounts": {"workItems": len(tasks), "atomicSteps": len(steps)},
            "baselineWorkbookSheets": sheets, "baselineRegistries": regs,
            "supersededBootstrapIds": [s["Atomic Step ID"] for s in wb_bootstrap if s["Atomic Step ID"] != "P0.3.S001"],
            "replacedDependencyIds": sorted(replaced), "taskDefinitions": tasks, "atomicStepDefinitions": steps,
            "bootstrapMappings": mappings, "progressSelection": rules["progressSelection"],
            "gateCriteria": regs["Gate Criteria"],
            "importRules": ["Do not seed baseline status columns as observed live completion.",
                            "Use active definitions for eligibility; raw source rows are provenance only.",
                            "Never infer completion through supports mappings or superseded IDs.",
                            "Import PC-001 as accepted plan provenance, never as a gate or task completion."]}


def mapping_markdown(plan: dict) -> str:
    lines = ["# Bootstrap source mapping", "", "Generated by tools/plan/build_plan.py. PC-001 is the approved authority.",
             "Old workbook rows remain preserved; mappings never transfer completion status.", "",
             "| Workbook ID | Active replacement IDs | Preserved workbook action |", "| --- | --- | --- |"]
    for row in plan["baselineRegistries"]["Atomic Steps"]:
        identifier = row["Atomic Step ID"]
        if identifier in plan["bootstrapMappings"]:
            action = row["Action"].replace("|", "\\|").replace("\n", " ")
            lines.append(f"| workbook:{identifier} | {', '.join(plan['bootstrapMappings'][identifier])} | {action} |")
    lines.extend(["", "## Active order", "", "| Execution ID | Specification source | Required outcome |", "| --- | --- | --- |"])
    for step in plan["atomicStepDefinitions"]:
        if step["lane"] == "bootstrap":
            lines.append(f"| {step['id']} | {step['sourceId']} | {step['action'].replace('|', '/')} |")
    return "\n".join(lines) + "\n"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    group = parser.add_mutually_exclusive_group()
    group.add_argument("--write", action="store_true", help="Regenerate only the two planning outputs")
    group.add_argument("--check", action="store_true", help="Check hashes, graph and committed output drift (default)")
    args = parser.parse_args()
    plan = build()
    outputs = {OUTPUT_PATH: json.dumps(plan, ensure_ascii=False, indent=2) + "\n", MAP_PATH: mapping_markdown(plan)}
    for path, expected in outputs.items():
        if args.write:
            path.write_text(expected, encoding="utf-8")
        elif not path.exists() or path.read_text(encoding="utf-8") != expected:
            raise SystemExit(f"Planning output stale: {path.relative_to(ROOT)}; run build_plan.py --write after reviewing source changes")
    print(f"PASS: source hashes/counts, bootstrap mapping, references and dependency DAG; {plan['activeCounts']}. No progress state written.")


if __name__ == "__main__":
    main()
