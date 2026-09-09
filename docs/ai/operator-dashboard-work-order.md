# Operator dashboard — authorized enhancement, 2026-09-09

The user requested an administrator dashboard inside the existing Unity
application, using `docs/design/operator-design-language.md`. This is a new
supplemental interface enhancement after DEV-01 through DEV-12 acceptance, not a
claim that P0.2 or a physical baseline task has become eligible or complete.
The validated baseline remains 173 events, G0 pending, P0.2.S001 selected.

Owner: Astra, branch `codex/operator-dashboard`. Owned paths: the operator UI,
its new workspace model/tests, dashboard documentation, and a new development
build. Preserve unrelated shell launcher and `elts-simulation/` edits.

Result: a three-pane administrator workbench with a persistent task index,
focused controls, and live participant/operator views. Use the existing engine,
recording, calibration fixture and mock link. Add local editable reminders and
condition-order presets, persistent layout, direct reorder with keyboard access,
and reduced motion. No new rig configuration authority or physical control path.

Acceptance: existing session/calibration tests still pass; model roundtrip,
validation and recovery checks pass; UI phase navigation, locked setup during a
run, notes/abort, reorder/cancel/persistence, long labels and reduced motion are
exercised in Unity; standalone dashboard renders with clear layout and live views.
Document all untested native input and hardware behavior truthfully.

Delegation: Luna reviews existing integration read-only; a separate Luna worker
owns only DashboardWorkspace.cs and its standalone tests. Root owns shared UI,
source integration, Unity validation and final evidence. No progress events or
physical acceptance claims are created for this supplemental work order.

## Verified delivery checkpoint — 2026-09-09

- Windows player: `build/operator-dashboard/ELTS-Synthetic.exe`, launched by
  `OPEN-DASHBOARD.cmd`; exact Unity 6000.3.23f1 LTS. Final build report is
  `diagnostics/dashboard-build-capture.json`; that output was promoted from
  `build/operator-dashboard-capture` to the launcher destination unchanged.
- Build manifest verification passes (`diagnostics/dashboard-build-manifest-check.log`).
  Build provenance records base revision `3fc16ce9f015` plus dirty-diff and untracked
  file hashes, so the parent revision alone is not the delivered source identity.
- Seven Unity Play Mode tests pass (`diagnostics/dashboard-regression.xml` and
  `dashboard-regression.json`), including existing session actions and the new UI
  behavior. Nineteen local-model checks pass (`dashboard-workspace-tests.log`).
- Thirty-eight progress tests pass (`dashboard-progress.json`); the authoritative
  state validator passes with P0.2.S001 still selected (`dashboard-state-validation.log`).
- Editor screenshots verify dark fields, full checkpoint labels and both cameras.
  The standalone offscreen capture is `diagnostics/dashboard-player-capture.png`;
  its probe uses explicit camera render requests because normal hidden-player
  camera targets stayed blank. A screen-backdrop-camera experiment was removed.
  The capture is evidence of offscreen rendering, not a visible-window runtime pass.
- Native mouse/keyboard delivery, visible-window continuous previews, multi-display
  behavior and physical hardware remain unverified. Automated UI tests exercise
  UI Toolkit events and manipulators; they do not substitute for those checks.

Administrator and extension instructions: `docs/modules/operator-dashboard.md`.
