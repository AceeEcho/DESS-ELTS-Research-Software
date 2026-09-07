"""Import verified PC-001 bootstrap observations with truthful event times.

Only P0.3.S001 through BOOT.S010 may be imported. Earlier occurredAtUtc is
preserved; each production event is created at the actual current recorded time.
Partial imports can resume only when their existing transitions match this exact
journal entry hash. Unrelated existing work requires explicit reconciliation.
"""
from __future__ import annotations

import argparse
import json
from pathlib import Path

from core import digest, ordered_events, reduce_state
from schema import ValidationError, load_json
from store import append_event, load_catalog, load_events, make_event

BOOTSTRAP_IDS = ["P0.3.S001"] + [f"BOOT.S{i:03}" for i in range(2, 11)]
PHASES = [("task_started", "in_progress"), ("verification_pending", "verification_pending"), ("step_completed", "done")]


def import_journal(root: Path, *, write=False):
    root = root.resolve()
    journal_path = root / "project-management/bootstrap/journal.jsonl"
    entries = [json.loads(line) for line in journal_path.read_text(encoding="utf-8").splitlines() if line.strip()]
    if [entry["stepId"] for entry in entries] != BOOTSTRAP_IDS:
        raise ValidationError("Bootstrap journal must contain exactly the ordered verified P0.3.S001..BOOT.S010 observations")
    evidence = load_json(root / "project-management/bootstrap/evidence-index.json")
    if set(evidence) != set(BOOTSTRAP_IDS):
        raise ValidationError("Bootstrap evidence index does not match the journal IDs")
    catalog = load_catalog(root)
    events = load_events(root)
    expected = []
    for entry in entries:
        if entry["result"] != "verified_observation" or entry["actor"] != "agent:codex-astra":
            raise ValidationError("Only verified observations from the bootstrap owner can be imported")
        if not entry["checks"] or not entry["evidence"] or not evidence[entry["stepId"]]:
            raise ValidationError("Journal observation lacks checks/evidence")
        for event_type, status in PHASES:
            expected.append((entry, event_type, status, "PC-001 journal " + digest(entry) + ": " + entry["work"]))
    ordered = ordered_events(events, root / "schemas/progress")
    if len(ordered) > len(expected):
        raise ValidationError("Production history extends beyond journal import; use normal event-first workflow")
    for (_, actual), (entry, event_type, status, note) in zip(ordered, expected):
        if (actual["target"] != {"kind": "atomic_step", "id": entry["stepId"]}
                or actual["eventType"] != event_type or actual["toStatus"] != status
                or actual["note"] != note or actual["occurredAtUtc"] != entry["occurredAtUtc"]):
            raise ValidationError("Existing event does not match this exact journal import; reconcile instead of duplicating")
    pending = expected[len(ordered):]
    requests = []
    for entry, event_type, status, note in pending:
        requests.append({"target": {"kind": "atomic_step", "id": entry["stepId"]},
                   "eventType": event_type, "toStatus": status,
                   "actor": {"type": "agent", "id": "codex-astra", "tool": "Codex"},
                   "branch": "bootstrap/repository-governance", "claimedPaths": entry["ownedPaths"],
                   "expectedChecks": entry["checks"], "stopConditions": ["Evidence or ownership mismatch", "Authority conflict"],
                   "occurredAtUtc": entry["occurredAtUtc"], "note": note,
                   "evidence": evidence[entry["stepId"]] if status != "in_progress" else []})
    # Validate the entire intended import, including evidence and dependencies,
    # before publishing the first event. A bad final receipt cannot leave an
    # avoidable partial import. Crash recovery still handles interrupted writes.
    provisional = list(events)
    for request in requests:
        candidate = make_event(catalog, provisional, root, request)
        reduce_state(catalog, provisional + [candidate], root)
        provisional.append(candidate)
    if not write:
        print(f"Journal checked: {len(ordered)} imported transitions, {len(pending)} remaining; no events written")
        return
    for request in requests:
        _, state = append_event(root, request)
        print(f"Imported {request['target']['id']} -> {request['toStatus']}; next {state['execution']['primaryStepId']}", flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--write", action="store_true")
    args = parser.parse_args()
    import_journal(Path(__file__).resolve().parents[2], write=args.write)


if __name__ == "__main__":
    main()
