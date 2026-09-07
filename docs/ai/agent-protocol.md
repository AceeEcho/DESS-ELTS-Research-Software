# Event-first agent protocol

## Purpose and authority

[AGENTS.md](../../AGENTS.md) is the canonical entry point. Original baseline
artifacts plus accepted PC-001 define the active catalog. Model adapters contain
links only. The repository event store is authoritative; Trello/Issues are mirrors.

## Start an assigned step

Validate plan/catalog and generated state using the README commands. Inspect the
exact approved atomic action, eligibility predicate, source contracts, and required
evidence. Follow `execution` for global selection and retain the baseline `current`
anchor. An active delegated step may run while the coordinator begins independent
eligible work; do not duplicate its paths or investigation.

Before editing, submit a `task_started` request to `tools/progress/event.py` with
an actor, one owned branch, claimed repository-relative paths, expected checks,
and stop conditions. The reducer checks task ownership, branch exclusivity,
dependencies and causal history. Use a separate branch/worktree for independent
tasks. A coordinator may retain task ownership while explicitly delegating a
bounded subassignment; record the worker, owned paths, revision and evidence.
Workers must not mutate shared contracts, event history or generated state.

## Verify, complete, hand off

Retain command outputs and source/config/fixture hashes in immutable evidence
files. Record `verification_pending` before `step_completed`; bind evidence to
that exact step and the `outcome`/`verification` criteria. Only passing, relevant
evidence can justify completion. All active child steps plus a separate
`task-acceptance` receipt and `task_completed` event are needed to close a parent.
DEV completion never completes a supported baseline task automatically.

Use the schema-defined handoff transition before changing an owned task's actor
or branch. Stop dependent edits on ownership/conflict/stale-state errors. If an
event was published but state replacement failed, regenerate state and inspect
the existing event; do not emit a duplicate transition. A stale writer lock may
be removed only after verifying that no writer is active.

## Blockers and external authority

Record exact missing inputs, responsible role and required action in a `blocked`
event. Continue other eligible work. Reopening requires an explicit event and
reopening completed dependents first. Do not weaken prerequisites to advance state.

Hardware tests must name the unavailable equipment and follow-up. Synthetic tests
cannot pass physical criteria. G0–G8 passage, research decisions, and physical/human
completion require authenticated, evidence-backed human approval. The current
CLI fails closed for these privileged approvals until a trusted identity provider
is integrated; self-authored human receipts are insufficient. File hashes bind
retained evidence; they do not establish that an external measurement occurred.

Use the schema-backed handoff template for durable next actions. Never edit
accepted event files, baseline binaries, or PROJECT_STATE.json by hand.
