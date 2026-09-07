# ELTS pre-build readiness review

**Resolution:** The user subsequently approved the recommendations. Accepted
`../../plan/PC-001-approved-build-amendment.md` and `../../ai/START-HERE.md` now
govern the build handoff. The findings below are historical; do not request the
same plan approval again. Physical evidence and runtime bootstrap remain pending.

Correction record: `user-correction.json` supersedes the initial blanket no-parts
statements retained as historical before/after evidence in `trello-audit.json`.
The ELTS is built; testing equipment is unavailable. Unity is 6000.3.23f1 LTS.

Original review on 2026-09-06 found the project ready for inventory but not yet for
broad implementation. PC-001 now resolves the planning blockers for its approved
scope. The ELTS apparatus is completed and built, but testing
equipment is unavailable (user correction after the initial review). Bootstrap remains incomplete and no task or gate was completed by
this review. Current constraints are in `../../ai/BUILD-CONSTRAINTS.md` and root
`AGENTS.md`.

## Scope and acceptance

Review the actual specification/workbook, dependency logic, current workspace,
and every open Trello card/checklist. Preserve the original deliverables. Record
hardware constraints where agents will read them; correct coordination drift;
leave a concrete implementation boundary and evidence. This is a planning review,
not execution of P0.3 or a physical validation.

## Findings requiring a reviewed plan amendment

1. **Bootstrap IDs disagree.** Specification Appendix C defines S002 as reading/
   hashing sources and S011 as generating state. Workbook Atomic Steps rows 11-14
   instead define P0.3.S002 as creating the reducer/state before preserving,
   hashing, or exporting its catalog inputs. Its S003-S026 also differ from the
   specification. Start Here repeats the workbook ordering. Preserve both originals
   and publish a versioned, explicit mapping and canonical bootstrap sequence.
   Do not silently change stable ID meanings. Both agree on S001 inventory.
2. **Synthetic development has a self-dependency and cycles.** Dependencies row 86,
   DEP-0081, requires P2.9 done before P2.9. DEP-0039 requires P2.9 before P2.1,
   while DEP-0082 requires P2.1 before P2.9. DEP-0042 and DEP-0083 similarly cycle
   P2.2/P2.9. Atomic Steps and Work Items repeat the problematic prerequisites.
   Conditional G1 OR (G0 plus synthetic) entry needs explicit alternatives, not
   an AND of every edge. Correct all representations together.
3. **D-12 blocks too early.** DEP-0044 requires decided D-12 for P2.2, while the
   decision's latest blocking point is P8.4. Specification 18.2 allows interfaces
   before final decisions. Separate the shared monotonic clock/UTC anchor from
   final cross-device synchronization approval; retain D-12 at the affected
   integration/rehearsal boundary.
4. **No broad pre-hardware execution lane exists.** Phase 2 synthetic exceptions
   still require G0. Missing parts alone do not make G0 impossible (orders and
   other preparations may precede delivery), but this review has no evidence that
   G0 criteria are satisfied. P3-P8 also retain physical/gate prerequisites.
   User intent to maximize software work should be encoded as bounded development
   tasks with explicit dependencies and synthetic evidence, not an invented gate
   pass or blanket waiver.
5. **Current-step reporting must expose eligible parallel work.** Specification
   17.4 limits current-step selection to the earliest unpassed phase, while 18.1
   permits explicitly eligible downstream synthetic work. Specify how the reducer
   reports that work without advancing the primary gate or hiding eligible tasks.

The architecture requires conflicts to be resolved by the appropriate human owner
(Authority and precedence; sections 15.3 and 19.1). This review does not self-approve
an ADR, research decision, or gate exception. A correction draft follows so the
next action is concrete.

## Proposed correction and software sequence

Preserve source hashes and baseline IDs; record a plan revision explaining every
changed dependency and bootstrap mapping. Prefer the specification Appendix C
bootstrap ordering: inventory; source reads/hashes and isolated branch ownership;
preserve baselines; schemas/import; event writer/reducer/validation fixtures;
truthful initial state; repository/toolchain/config/scripts; Unity/dependency
provenance; smoke tests/build/CI; portability verification. Do not emit fictional
completed events to bridge bootstrap before the progress system exists.

For software prerequisites, make configuration and clock interfaces available
before the synthetic source: configuration -> clock -> synthetic source ->
downstream tracking/logging/ingest integration. Remove the P2.9 self-edge and its
prerequisite on the configuration/clock tasks. Encode the real and synthetic
eligibility alternatives explicitly, and retain separate evidence for the real
tracking path. The final amendment must state which development tasks may run
before G0, with no automatic completion of their mixed parent tasks.

| Development work to schedule explicitly | Evidence that waits for equipment |
| --- | --- |
| Config schemas, geometry, clock, synthetic tracking, logging and ingest | Real rate/jitter/occlusion, serial binding, trigger behavior and durability on study PC |
| Projection math, editable rig layout, operator UI and replay using fixtures | Physical corner alignment, actual display assignment/performance and head mount rigidity |
| Scenario/session/scoring logic with explicit provisional fixture values | Investigator-selected parameters, operator dry runs and real participant calibration |
| Calibration solvers and wizard state machines with known synthetic inputs | Measured pivot/corners/zero, daily bore checks and repeatability |
| Protocol codec, mock transport and simulated controller state transitions | Actual Jetson/ESP32 timing, light shutdown, physical E-stop and exposure controls |
| Packaging scripts, documentation templates and synthetic end-to-end runs | Replacement-machine validation, full rig soak, cross-device rehearsal and pilots |

Acceptance for the correction: all expected IDs reconcile; no self-edge or task
cycle; predicate fixtures distinguish real/synthetic routes; open D-12 permits
only interface work; earliest gate remains truthful while eligible parallel work
is visible; synthetic passes cannot satisfy physical criteria; no gate passes
without authorized evidence. Reconcile exports and Trello only after plan approval.

## Trello reconciliation

Board: https://trello.com/b/C5karyEw/elts-development

Read all 74 original open cards across 14 lists and fetched every card's checklists.
All were incomplete. The 29 checklist entries were grouped/partial reminders, not
the full 62 workbook criteria; G5-G7 had no checklist. Updated all nine gate
descriptions with the exact 62 numbered workbook criteria and explicit approval
rules. Kept existing checklist items as reminders and changed no completion state.

Updated P0.3 to cover governance/portability bootstrap and its blocking conflict;
updated D-18 with the inventory status (subsequently corrected to built ELTS
and unavailable testing equipment); replaced automatic completion advice
on the AUTOMATION card with evidence-aware mirror rules. Added three visible
guidance/control cards for hardware constraints, plan correction, and actual
automation verification. Twelve existing descriptions were updated and three
cards added. Readback confirmed all twelve description changes, 77 total open
cards, and zero completed cards. No orders, decision acceptance, or gates were
claimed. Actual Butler verification is recorded separately in `trello-audit.json`.

## Verification and limitations

- Extracted the actual DOCX paragraphs/tables and all XLSX sheets for semantic
  review; source hashes and count/uniqueness results are in `validation.json`.
  Counts match: 91 work items, 243 atomic steps, 18 decisions, 62 criteria,
  360 dependency edges, 108 requirements, 26 risks. Matching counts do not prove
  dependency correctness; graph inspection found the cycles listed above.
- Independent Luna review corroborated bootstrap, cyclic dependency, decision
  timing, and missing development-lane issues. These were checked against source
  rows before inclusion here.
- `git status --short` reported this planning directory is not a Git repository.
  `git ls-remote` for the specified GitHub repository succeeded with no refs.
  Preserve current planning/simulation files; handle an empty remote explicitly.
- User selected **Unity 6000.3.23f1 LTS** after the initial review. The previously
  observed local folder `6000.3.10f1` is not the selected version. Installation of
  the selected version has not been checked. Launch, licensing, package resolution,
  Unity tests and builds were not exercised. Folder presence is not proof of a
  working pinned toolchain. No hardware/runtime/scientific acceptance is claimed.
- Source build-plan file was absent from the visible workspace inventory; its
  recorded historical hash could not be independently checked against that file.
- No DOCX/XLSX layout edits or visual revalidation were performed. Originals were
  left unchanged. Extracts are review evidence, not a normalized execution catalog.

## Next handoff

This historical handoff is superseded by `../../ai/START-HERE.md` and accepted
PC-001. Start with P0.3.S001 unless valid later progress exists, then follow the
active BOOT/DEV/baseline definitions and exact eligibility rules. Physical follow-up
and all study gates remain explicit; the planning blockers need no renewed approval.
