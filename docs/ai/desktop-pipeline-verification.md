# Desktop pipeline checkpoint — 2026-09-09

Implemented under the user-authorized desktop work order on `codex/desktop-input`.
The named entry scene is `unity/Assets/Scenes/ELTSDesktop.unity`; the local Windows
build is `build/desktop-pipeline`, launched with `OPEN-DESKTOP-TEST.cmd`.
Operation and hardware substitution boundaries are in `docs/modules/desktop-input.md`.

## Performed checks

- Unity 6000.3.23f1 PlayMode: **9 passed, 0 failed**. The desktop integration test
  completed all four conditions using an injected clock and controls, logged four
  shots and four hits, verified raw-sample trigger identities, rejected held clicks
  after focus loss and shots after block expiry, closed the recording, and loaded
  replay. A separate test injected virtual Input System mouse/keyboard events.
- Registered runtime suite: **pass**, including 21 desktop tracking checks,
  7 acquisition checks, existing configuration/tracking/logging/rendering/replay/
  scenario/session/calibration/device-adapter checks, and 16 analysis tests.
- Recording integrity verifier: **pass**. Analysis ingest using the synthetic rig's
  muzzle offset, bore and zero correction: **pass**, one shot and one target
  destroyed in each of WE_MT, NE_MT, WE_FT and NE_FT.
- Windows player build and manifest: **pass**. Non-graphical startup probe: **pass**.
  Separate hidden-player render probe: **pass**, exit 0; its captured desktop
  preview was visually inspected. It requests camera renders explicitly and uses
  stationary injected input. The running-block screenshot comes from PlayMode.
- Scoped whitespace check: **pass**. No project progress events or gate status changed.

The regression run caught a blocked logger preventing acquisition shutdown.
Lifecycle synchronization now has its own lock, so cancellation and bounded join
remain reachable while the separate capture lock preserves sample/enqueue order.
The original bounded-stop test passes after this correction.

## Local evidence

These outputs are generated diagnostics and are not committed as study evidence:

- `diagnostics/desktop-play-final.json`, `test-results/unity-play.xml`
- `diagnostics/desktop-core-runtime.json`
- `diagnostics/desktop-recording-verify.json`, `diagnostics/desktop-pipeline-run.txt`
- `diagnostics/desktop-analysis-calibration.json`, `diagnostics/desktop-analysis-rig.json`
- `diagnostics/desktop-build.json`, `build/desktop-pipeline/build-info.json`
- `diagnostics/desktop-player-smoke.json`, `diagnostics/desktop-player-render.log`
- `diagnostics/desktop-play.png`, `diagnostics/desktop-player.png`
- `diagnostics/desktop-final-evidence.json` binds reports and implementation files
  by SHA-256. The build manifest records the pre-commit dirty source snapshot;
  it does not claim a clean release or physical validation.

Physical mouse/keyboard use in a visible Windows player has not been manually
verified here. Full gameplay was tested in PlayMode; the built-player probe covers
startup and presentation only. Hardware tracking, trigger timing, calibration,
ELTS actuation and study gates remain pending the required equipment and validation.
