"""Crash-recoverable filesystem store and event construction. No network I/O."""
from __future__ import annotations

import copy
import json
import os
import uuid
from contextlib import contextmanager
from datetime import datetime, timezone
from pathlib import Path

from core import chain_key, digest, file_hash, reduce_state, repository_path
from schema import ValidationError, load_json


def json_bytes(value):
    return (json.dumps(value, ensure_ascii=False, indent=2, allow_nan=False) + "\n").encode("utf-8")


def load_catalog(root: Path):
    # A fresh import verifies immutable sources, exact active definitions and all
    # provenance hashes. The generated state cannot bless a modified catalog.
    from import_plan import build_catalog
    expected = build_catalog(root)
    path = root / "project-management/task-catalog.json"
    if not path.is_file() or path.read_bytes() != json_bytes(expected):
        raise ValidationError("Task catalog missing/stale; run import_plan.py --write after reconciling sources")
    return expected


def load_events(root: Path):
    folder = root / "project-management/progress/events"
    result = []
    if folder.exists():
        for path in sorted(folder.rglob("*.json")):
            repository_path(root, path.relative_to(root).as_posix())
            result.append(load_json(path))
    return result


def replace_generated(path: Path, data: bytes):
    """Atomic state publication; a failure leaves the prior complete file."""
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name("." + path.name + "." + uuid.uuid4().hex + ".tmp")
    try:
        with temporary.open("xb") as stream:
            stream.write(data)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary, path)
    finally:
        temporary.unlink(missing_ok=True)


def publish_new(path: Path, data: bytes):
    """Publish a complete immutable file without an overwrite race.

    Hard-link publication is atomic and create-new on supported local filesystems
    (including NTFS). Unsupported filesystems fail clearly instead of falling back
    to a partially visible write or an overwrite-capable rename.
    """
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name("." + path.name + "." + uuid.uuid4().hex + ".tmp")
    try:
        with temporary.open("xb") as stream:
            stream.write(data)
            stream.flush()
            os.fsync(stream.fileno())
        os.link(temporary, path)
    finally:
        temporary.unlink(missing_ok=True)


@contextmanager
def writer_lock(root: Path):
    folder = root / "project-management/progress"
    folder.mkdir(parents=True, exist_ok=True)
    lock = folder / ".writer-lock"
    try:
        lock.mkdir()
    except FileExistsError as exc:
        raise ValidationError("Progress writer is locked; verify no writer is active before removing a stale .writer-lock") from exc
    try:
        yield
    finally:
        lock.rmdir()


def generate(root: Path, *, write=False):
    root = root.resolve()
    state = reduce_state(load_catalog(root), load_events(root), root)
    path = root / "PROJECT_STATE.json"
    if path.is_symlink():
        raise ValidationError("PROJECT_STATE.json must be a regular generated file")
    data = json_bytes(state)
    if write:
        if not path.exists() or path.read_bytes() != data:
            replace_generated(path, data)
    elif not path.exists() or path.read_bytes() != data:
        raise ValidationError("PROJECT_STATE.json missing/stale; validate sources/events and run reduce.py --write")
    return state


def make_event(catalog, events, root: Path, request, *, state=None):
    state = state or reduce_state(catalog, events, root)
    allowed = {"target", "eventType", "toStatus", "actor", "branch", "claimedPaths", "expectedChecks",
               "stopConditions", "evidence", "blockers", "note", "approval", "handoffTo", "occurredAtUtc",
               "expectedTaskEventHash", "fromStatus"}
    unknown = set(request) - allowed
    if unknown:
        raise ValidationError(f"Unknown event request fields: {sorted(unknown)}")
    target = request["target"]
    kind, identifier = target["kind"], target["id"]
    mappings = {"atomic_step": "atomicSteps", "task": "tasks", "gate": "gates", "decision": "decisions"}
    if kind not in mappings or identifier not in state[mappings[kind]]:
        raise ValidationError(f"Unknown active progress target: {target}")
    record = state[mappings[kind]][identifier]
    task_id = None
    if kind == "atomic_step":
        task_id = next(s["parent"] for s in catalog["atomicSteps"] if s["id"] == identifier)
    elif kind == "task":
        task_id = identifier
    key = "task:" + task_id if task_id else kind + ":" + identifier
    head = state["chains"].get(key, {"revision": 0, "hash": None})
    if "expectedTaskEventHash" in request and request["expectedTaskEventHash"] != head["hash"]:
        raise ValidationError("Optimistic task head check failed; reread/reconcile current work")
    now = datetime.now(timezone.utc).isoformat()
    result = {"$schema": "../../../../../schemas/progress/progress-event.schema.json", "schemaVersion": 1,
              "eventId": str(uuid.uuid4()), "eventType": request["eventType"],
              "recordedAtUtc": now, "occurredAtUtc": request.get("occurredAtUtc", now),
              "actor": request["actor"], "planVersion": catalog["planVersion"],
              "catalogSha256": file_hash(root / "project-management/task-catalog.json"),
              "target": target, "taskId": task_id,
              "fromStatus": request.get("fromStatus", record["status"]), "toStatus": request["toStatus"],
              "taskRevision": head["revision"] + 1, "previousTaskEventHash": head["hash"],
              "causalEventHashes": sorted({h["hash"] for h in state["chains"].values()}),
              "branch": request["branch"], "claimedPaths": request.get("claimedPaths", record["claimedPaths"]),
              "expectedChecks": request.get("expectedChecks", []), "stopConditions": request.get("stopConditions", []),
              "evidence": request.get("evidence", []), "blockers": request.get("blockers", []),
              "note": request["note"], "approval": request.get("approval"), "handoffTo": request.get("handoffTo")}
    return copy.deepcopy(result)


def append_event(root: Path, request):
    """Validate the whole candidate history, create one event, then reduce state.

    Publication may succeed even if subsequent state replacement fails. In that
    case do not retry the transition: inspect events and regenerate state first.
    """
    root = root.resolve()
    with writer_lock(root):
        catalog, events = load_catalog(root), load_events(root)
        state = reduce_state(catalog, events, root)
        state_path = root / "PROJECT_STATE.json"
        if state_path.exists() and state_path.read_bytes() != json_bytes(state):
            raise ValidationError("Stored state is stale; regenerate before appending another event")
        if state_path.is_symlink():
            raise ValidationError("Generated state path cannot be a symlink")
        event = make_event(catalog, events, root, request, state=state)
        candidate = reduce_state(catalog, events + [event], root)
        recorded = datetime.fromisoformat(event["recordedAtUtc"])
        filename = recorded.strftime("%Y%m%dT%H%M%S.%fZ") + "_" + event["eventId"] + ".json"
        path = root / "project-management/progress/events" / recorded.strftime("%Y/%m") / filename
        publish_new(path, json_bytes(event))
        try:
            replace_generated(state_path, json_bytes(candidate))
        except OSError as exc:
            raise ValidationError(f"Event was published at {path.relative_to(root)} but state publication failed; run reduce.py --write; do not repeat this transition") from exc
        return event, candidate
