# PC-001 Build readiness and development before testing equipment

**Status:** Accepted, 2026-09-06.
**Decider:** Project user, explicitly approving the review recommendations and
requesting all changes needed for a fresh Astra session to begin building.
**Scope:** Execution planning and bootstrap governance only. This is not a research
decision, physical acceptance record, gate pass, or study release approval.

## Authority and preserved sources

Read the original specification and workbook in `deliverables/` in full on first
implementation, followed by this amendment. Preserve them byte-for-byte during
bootstrap. This user-approved amendment overrides only the conflicting bootstrap
sequence, the P2 entry dependencies, premature D-12 dependencies, and
the progress selection/development permissions described below. All other
scientific, safety, privacy, ownership, evidence, and gate requirements remain.

This resolves the planning blockers in the 2026-09-06 readiness review; that review
is historical evidence, not an instruction to seek the same approval again.
`approved-plan.json` is generated from the original workbook, specification
Appendix C, and `amendment-rules.json` by `tools/plan/build_plan.py`. The JSON rules
are the structured form of this amendment. Any disagreement must fail validation
and be reconciled; do not silently choose one representation.

## Bootstrap order and stable identities

Use specification Appendix C's sequence. `P0.3.S001` remains the first inventory
step because both originals agree on it. The later specification steps receive
unambiguous execution IDs `BOOT.S002` through `BOOT.S026`. Preserve the original
workbook P0.3.S002-S026 rows as superseded planning definitions, with their exact
actions and mapping to the replacement steps in `bootstrap-map.md`. These are
plan lifecycle labels, not task progress statuses. Do not mark superseded rows
done or count them twice. All other baseline atomic IDs remain unchanged.

Where a workbook row maps to multiple BOOT steps, its action/evidence is retained
for traceability and distributed across those outcomes. It is not a requirement
to complete the entire old row at each mapped step (for example, BOOT.S002 hashes
sources but cannot yet record those hashes in generated state). Check aggregate
coverage at P0.3 closure. BOOT.S025's documented-ruleset route is sufficient for
bootstrap; actual GitHub settings validation remains a separate pending control,
not silently claimed as performed.

Always qualify historical ambiguous IDs as `workbook:P0.3.S002` or
`spec:P0.3.S002`. New events use the active execution IDs. Never transfer a done
status by matching a historical numeric suffix. P0.3 completion requires all 26
active bootstrap steps and the retained baseline completion requirements.

The initial inventory must inspect the actual directory and remote again. If the
remote has no commits, local repository initialization in this preserved workspace,
an initial preservation commit on main, and a bootstrap branch are authorized.
If commits exist, fetch/clone without overwriting this directory and reconcile
files before branching. Do not force-push, delete user work, or treat a missing
remote main ref as a reason to loop indefinitely. Remote publication, protected
branch changes, merge, and release approval remain separate actions.

There is a narrow pre-reducer exception for P0.3.S001 through BOOT.S010. Record
actor, branch, owned paths, actual work times, source hashes, checks and evidence
in a durable bootstrap journal; keep one coordinator responsible for shared
progress artifacts. The journal is evidence, not PROJECT_STATE.json or proof of
task completion. Implement and test the writer/reducer before emitting production
progress events. At BOOT.S011, import the journal with truthful recorded-at and
occurred-at provenance, normal ordered transitions and evidence; do not backdate
event creation or fabricate earlier completions. Reconcile competing claims first.
After this point the normal event-first workflow applies. This expressly resolves
the source rules that otherwise demand a working reducer before it can be built.

BOOT.S010 tests use isolated fixtures, never the production event store. BOOT.S011
must demonstrate reproducible state and semantic no-op regeneration. No state or
events have been created by this planning amendment.

## P2 dependency repair

The generated plan replaces all incoming baseline dependency rows DEP-0037 through
DEP-0083 with explicit `allOf`/`anyOf` eligibility predicates, and removes the
premature D-12 decided edges DEP-0002 and DEP-0146. The replaced rows
remain in the baseline registry as provenance and must not also be ANDed into the
effective graph. Apply each P2 predicate to its task and atomic steps; preserve
within-task ordering. The old Work Items entry/direct dependency prose and first
atomic-step task prerequisites are superseded for these nine tasks.

Configuration precedes clock, which precedes synthetic tracking. Remove P2.9's
self-dependency and remove the synthetic-source prerequisites on P2.1/P2.2.
P2.1, P2.2 and P2.9 permit G1 or G0 entry with their actual software prerequisites.
Other P2 tasks permit the real path through G1 or the G0-plus-synthetic path, with
their remaining software prerequisites. P2.3's P1 tracking-spike prerequisites
apply to its real path. A synthetic entry does not prove real-path acceptance or
automatically complete P2.3; all required physical evidence remains outstanding.

D-12 finalization is not a prerequisite for P0.5 owner/deadline register work,
P2.2 shared monotonic clock/UTC anchor, or P4.11 provisional SyncMarker/operator
controls. Remove its decided requirement from the corresponding task and atomic
definitions, not just their graph edges. P0.5 still requires acknowledged owners
and deadlines. D-12 remains open, with DEP-0344 retained at P8.4. Any earlier action
that actually selects or validates final cross-device synchronization must wait
for that decision. This amendment does not select a synchronization method.

## Authorized development lane

The generated plan adds twelve `DEV-*` work items with implementation and
verification steps. Their explicit predicates allow development before G0-G8;
their `supports` references are traceability links, never inherited dependencies
or automatic parent completion. Only these development items and active bootstrap
steps receive the new permissions. Unmodified baseline tasks retain their gates.

DEV-01 can begin after BOOT.S011, once the validated progress system exists.
This allows configuration and pure software work to continue if Unity installation
or other later bootstrap checks are blocked. Unity-dependent development tasks
also require BOOT.S022 (actual Unity project/dependencies and smoke tests).
Use exact Unity **6000.3.23f1 LTS**, selected by the user. Do not substitute another
patch. Toolchain checks, known prerequisite setup, local builds and tests are part
of authorized implementation; a license/account agreement still needs its owner.

The ELTS apparatus is already built. Testing equipment is unavailable. Keep its
positions, dimensions, bindings and calibration configurable. DEV protocol work
uses simulated adapters; inspect existing controller code when available and do
not rewrite the built controller or energize hardware on the basis of this plan.
Missing controller source, detailed source-plan appendices, measurements or final
decisions block only work that needs them. Record the exact missing input; continue
independent eligible work. Do not invent a measured value or a final contract.

Development-only fixture values must be explicit, reproducible, segregated from
study configuration and incapable of silently enabling a study session. Open
D-01 through D-18 remain open. Development may exercise configurable alternatives
without selecting a study outcome or exposure limit. Synthetic-only builds/data
must identify their mode and must not masquerade as study-ready output.

Each DEV item has its own evidence and completion. Parent P tasks are completed
only through their separate acceptance checks and events. Reuse valid development
evidence deliberately after checking revision/configuration provenance; never
copy a DEV done status to a parent. Every physical criterion and all 62 gate
criteria remain intact. G0-G8 require explicit evidence-backed human approval.

## Progress selection and completion

Keep the existing `current.*` fields anchored to the earliest unpassed baseline
phase/gate and its steps. Add a separate `execution` object for global work
selection, with `mode`, `primaryStepId`, `selectedPhaseId`, `selectedLane`,
`activeStepIds`, `nextEligibleStepIds`, and `blockingIds`. Its eligible pool includes
approved DEV work, active bootstrap replacements and explicitly eligible baseline
parallel work. Select in-progress first, then verification-pending, then ready;
break ties by stable execution order in the generated plan. This prevents a DEV
step from being mislabeled as a P0 step and preserves the old anchor semantics.

Agents follow `execution.primaryStepId`; a blocked baseline `current` is not a
global stop while `execution` has eligible work. With no active/verification/ready
work, report the specific missing inputs and stop. All DEV work done does not mean project complete:
only authorized G8 passage can produce project completion. New progress schemas
must record baseline hashes, amendment rules hash and effective catalog hash.

## Alternatives and consequences

Keeping the baseline unchanged would retain contradictions and cycles. Waiving
gates globally would erase required physical evidence. The selected amendment
keeps immutable provenance and adds explicit software permissions, at the cost of
an importer that understands replacements and additional work items.

Trello is an optional coordination mirror. Actual Butler rules remain unverified;
this does not block local implementation because board completion has no authority.
Do not use automatic checklist/card changes as inputs to the progress reducer.

## Acceptance and next action

Run `python tools/plan/build_plan.py --check` and
`python -m unittest discover -s tools/plan -p 'test_*.py'` using Python 3.10+.
The builder uses only the standard library and repository-relative paths. It checks
source hashes, source counts, identity/mapping coverage, dependency references,
cycles, explicit route fixtures and output drift. The runtime progress reducer
must independently test event ownership, transitions, evidence classes and gates.

The user may now start a fresh Astra medium session with `docs/ai/START-HERE.md`.
Begin at P0.3.S001 unless a later valid event-derived state exists. Do not repeat
the completed planning review or request approval again for this amendment.
