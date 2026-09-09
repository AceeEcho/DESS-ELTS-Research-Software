# Completed development lane — 2026-09-08

The user requested stopping when all DEV cards are finished. DEV-01 through
DEV-12 are now accepted in event-derived progress, and all twelve Trello cards
have their completion flag set. No further baseline work is started at this
checkpoint. The earlier pause is preserved in Git history.

## Verified state

`tools/progress/reduce.py --check` and `tools/progress/validate.py` pass with
173 events. There are no active atomic steps. The next execution selection is
`P0.2.S001` in the baseline lane; validate state and its predicates before any
future work. Do not restart bootstrap or request PC-001 approval again.

Immutable acceptance receipts are in `project-management/progress/evidence/`.
The Trello readback and two reconciled flags (DEV-09 and DEV-11) are recorded in
`project-management/progress/trello-dev-completion-2026-09-08.json`. Historical
card descriptions still say NOT STARTED and list positions are unchanged; use
the completion flags and canonical repository evidence for current progress.

## Deliverable and evidence

- Portable Windows package: `release/ELTS-Synthetic-Development-20260908.zip`
  (69,203,744 bytes), with an extracted sibling directory.
  SHA-256: `f816ba3fdbcead26c1fe4046aeed90d2feb47602e66cb80122ebab8f46653d8f`.
- Setup and launch: `docs/operator/development-package.md`, also included as
  `README.md`. Deferred equipment work: `docs/operator/hardware-follow-up.md`,
  included as `HARDWARE-FOLLOW-UP.md`.
- Exact Unity version: **6000.3.23f1 LTS**. Player source revision is
  `003035f135a0e117928af7095d526d1bf87fb51f`; its identity truthfully retains
  `-dirty`. Package recipe revision is `8d2688f`. Package metadata records
  source/build hashes and the evidence binding; later fixture tests are bound
  through unchanged versioned runtime inputs, not claimed as same-binary tests.
- Five final Unity PlayMode tests pass (`diagnostics/dev09-wrap-play.json`).
  Expanded standalone calibration UI was visually inspected after fixing label
  wrapping and overlap (`diagnostics/dev09-calibration-complete.png`).
- Runtime driver now includes the later C# suites and analysis checks; all pass
  in `diagnostics/dev12-runtime.json`. Packaging/provenance checks pass (10),
  as do driver tests (3); reports are retained under `diagnostics/dev12-*`.
- Integrated session/logging/mock-link fixture executes four 300-second blocks
  with a manual clock, verifies known shot/target outcomes and recorded replay,
  and exercises abort/reconnect plus explicit rerun without overwriting output.
  See `diagnostics/dev12-end-to-end.json` and `test-results/dev12-integrated/`.
- A ZIP was extracted to a path containing spaces. Its actual copied player
  passed startup; package verification, run verification and analysis commands
  from the copied README passed. See `diagnostics/dev12-portability.json`.
  The final ZIP passed full entry/hash readback and directory verification
  (`diagnostics/dev12-final-package.json`, `diagnostics/dev12-final-verify.json`).

## Limits and next action

The apparatus is built; testing equipment is unavailable. This is a synthetic
software development package with `studyReady:false`. No physical or safety
gate, measured calibration, final study decision, or mixed baseline parent task
is completed by these DEV acceptances. No second machine was available.

Calibration uses a known-transform virtual fixture and configurable provisional
thresholds; accepting its sidecar does not overwrite the startup rig geometry.
Manual-clock sessions are not real-time 20-minute apparatus trials. Native
operator controls, active-player quit, physical displays/SteamVR, real controller
behavior and study-machine performance still need the follow-up register.

Resume only when requested. Revalidate generated state, inspect `P0.2.S001` and
its dependencies, and preserve physical deferrals. No remote source publication,
merge, deployment or study release was performed. Existing original deliverables
remain architecture authorities. Unrelated `elts-simulation/` edits are preserved.
