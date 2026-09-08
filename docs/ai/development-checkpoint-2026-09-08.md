# User-requested development stopping point — 2026-09-08

The user asked to find a stopping point. Resume only when requested; no background
build, soak, automation or worker is intended to continue after this handoff.

## Recorded progress

Validate `PROJECT_STATE.json` and the event store before editing. Bootstrap P0.3
and DEV-01 through DEV-08 plus DEV-10 are accepted. DEV-09.S001 and DEV-11.S001
remain **in progress** under their existing Astra claims. Their verification
steps and task acceptance have not been recorded. DEV-12 has not started.
This pause is not a fabricated blocker or task completion. G0 remains unpassed.

The apparatus is built. Testing equipment is unavailable. The entire development
lane remains synthetic; measured calibration, hardware safety and study readiness
are not established. Unity is pinned to and tested with **6000.3.23f1 LTS**.

## Integrated source and evidence

- Calibration solvers and state helper: `a476f63`, `25b54cb`, `3dfc25a`, `3843e07`.
  Focused calibration checks pass (11). Known/noisy and degenerate pivot inputs,
  corner orthogonalization, zeroing, angular residuals, input validation and JSON
  control escaping are covered.
- Operator calibration fixture and mock-link integration: `e6916a1`.
  The actual Unity UI Toolkit test exercises create recording, calibration
  rejection under tight limits, redo, acceptance/sidecar persistence, practice,
  note and abort/recording closure. These are generated UI events, not native
  mouse/keyboard evidence.
- Session protocol integration: `a527c6d`, hardened by `ad7659d` and `e8321cc`.
  Focused checks pass: 39 adapter checks and 51 session checks at `e8321cc`.
  Tests cover WE/NE alternatives, typed
  transcript, ACK correlation, retry, loss/reconnect, logging rejection and
  exact 300-second endings. Clock jumps are manual-clock evidence, not a
  20-minute physical or real-time session.
  The final repair ensures a rejected transcript after START still sends a
  best-effort simulated STOP and leaves the engine Failed with permission false.
- Initial integrated Unity PlayMode evidence (five passing tests) is retained in
  `diagnostics/dev09-11-initial-unity-play.xml` and `.log`, with report
  `diagnostics/dev09-11-integration-play.json`. The final checkpoint run is
  `diagnostics/dev09-11-checkpoint-play.json` with copied XML/log alongside it.
- Progress validation passed with 154 events: catalog, exports, event chain,
  generated state and handoff schemas. No progress events were added for this
  pause.

## Resume work

1. Read the current DEV-09/DEV-11 claims and relevant module docs; do not restart
   bootstrap or re-request PC-001 authorization.
2. Finish their evidence review and create immutable acceptance receipts through
   `docs/ai/agent-protocol.md`. Do not mark them complete from this handoff alone.
3. Run a fresh standalone build and inspect the expanded calibration panel. The
   last verified standalone graphical output is `build/dev08-layout` at
   `50e796a69efd` (see its full build-info manifest); it predates the calibration
   and protocol integration. Current code has Unity runtime checks but no new
   standalone build at this stopping point.
4. Only after DEV-09 and DEV-11 acceptance makes DEV-12 eligible, package and test
   copied-output/path-with-spaces/end-to-end provenance, write the final setup
   and hardware follow-up register, and report second-machine testing unavailable.

## Explicit limits and follow-up

Calibration UI generates a virtual known-transform fixture. It does not acquire
real pivot/corner/zero captures or apply accepted fixture output to the startup
rig configuration. Thresholds are editable, provisional development values.
The sidecar records inputs/results/config hash and has a SHA-256 in the operator
note; the ordinary run verifier does not yet automatically validate sidecars.
UI acceptance means reviewed and queued; final writer closure is a separate
durability check. Editor recordings identify source as `editor-unversioned`;
packaged identity tokens must be resolved against the verified build manifest.

The common runtime test driver still lists only earlier runtime suites. Use
explicit later suite commands until that driver is extended during packaging.
Do not assume `-Suite runtime` includes Session, Calibration, Replay or Elts tests.
Actual active-player quit, native controls, physical two-display/SteamVR behavior,
real controller integration and study-machine performance remain unverified.

All changes are local. No remote publication, merge, deployment or study release
was performed. Existing original deliverables remain the architecture authorities.
