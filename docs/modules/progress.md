# Progress tooling contract

Owner: repository maintainer; bootstrap integration owner: Codex Astra.
Work items: P0.3.S001, BOOT.S002-BOOT.S011. Requirements: STATE-001 through
STATE-012, AI-003, AI-009, AI-012, REP-007. Authority: accepted PC-001.

## Purpose and boundaries

The importer preserves every workbook sheet and imports only the accepted active
definitions. The reducer produces progress from the catalog and immutable events.
It does not decide research outcomes, perform physical verification, or trust a
spreadsheet status, Trello card, or generated state as an event.

## Public interfaces and schema compatibility

Progress JSON uses schemaVersion 1. Incompatible changes require a new version and
an explicit migration. `tools/progress/build_schemas.py` is the single source for
the seven generated contracts in `schemas/progress/`; `--check` detects drift.
`schema.py` validates the documented local vocabulary and rejects unknown assertion
keywords, duplicate JSON keys, non-finite numbers, and remote schema references.
It is not a general-purpose implementation of the full JSON Schema specification.

## Inputs, outputs, configuration and ownership

Input authorities are the immutable deliverables, accepted PC-001 rules, approved
plan and evidence-backed human authority registry. Paths are repository-relative.
Only a maintainer with recorded human authority may enroll gate/decision approvers
in `project-management/progress/authorities.json`; it starts empty.

The coordinator owns shared catalog/state integration. Each task has one owner and
branch. Events targeting its atomic steps share a task revision/hash chain.
Different tasks may use disjoint branches and worktrees. Event causal hashes order
dependencies explicitly; file names and timestamps never choose a competing winner.
Concurrent claims to one prior revision are errors. Path claims are checked across
active tasks, using case-insensitive paths for portable Windows behavior.

## Failure and recovery

Validation errors stop publication with a non-zero exit code. Event files are never
rewritten. A crash after publishing an event but before replacing generated state
leaves recoverable stale state; rerun reduction after validating the event set.
Do not delete events or choose one side of a generated-state merge conflict.

## Evidence and protected boundaries

Evidence binds a repository file's SHA-256, source revision/snapshot, command,
result, target IDs, acceptance criteria and limitations. Physical/human acceptance
is not established by synthetic tests. Approval requires an enrolled human, a
matching target, and a separately resolvable approval record. No human is enrolled
by bootstrap and no gate or final D decision is approved by PC-001.

## Verification

Run the schema generator check and `python -m unittest discover -s tools/progress
-p "test_*.py"`. Runtime fixtures must cover transitions, evidence, ownership,
conflicting chains, readiness predicates, current/execution selection, reopening,
gate approval, no-overwrite publication, and deterministic regeneration. All
fixtures use isolated stores; they never seed the production event directory.
