# Session runtime

`SessionEngine` is a pure, operator-driven state machine. It exposes the current state, condition, block index, rerun index, remaining logical-clock seconds, and whether targets may exist. It emits every transition through `ISessionEventSink`; a sink or WE-link failure enters `Failed` and prevents continued use.

`SessionReadinessReport` checks the required two displays, SteamVR, two serial-bound valid trackers for two seconds, configuration hashes, 2 GB of free space, live logging, and the WE link when a WE condition is assigned. Synthetic success never sets `StudyReady`.

`ISessionRecordingLifecycle` is an asynchronous seam for an adapter that reserves and starts an existing `LoggingRunDirectory`/`SessionLogWriter` run outside the UI thread. Reruns preserve the prior raw folder and carry an incremented rerun index; the engine never deletes or writes raw files.

## Operator development workflow

The Unity player opens the UI Toolkit session panel below the two emulated camera
views. Enter a synthetic test identifier and a permutation of `WE_MT`, `NE_MT`,
`WE_FT`, and `NE_FT`. The staged session and scenario templates and effective
configuration hash are shown before creation. Template choices remain in the
validated configuration pipeline; this panel does not read arbitrary JSON.

Choose **Create synthetic recording**. A background operation reserves a unique
output directory and starts the writer. A dedicated synthetic producer supplies
raw paired observations to the writer and immutable snapshots to rendering. The
preflight observes two seconds of fresh, valid synthetic data, configuration
identity, available disk space and writer health. Displays, SteamVR and the
DEV-08 lifecycle link are explicitly emulated. These substitutions never enable
study readiness.

**Continue** advances setup to the calibration placeholder and then practice.
Practice and break durations come from staged development session configuration.
**Start block** starts the selected condition; its clock deadline is fixed at
300 seconds. Targets appear only during `BlockRunning`; the operator test trigger
uses the same block-owned hitscan path as other synthetic trigger adapters.
Moving targets use an explicitly provisional random-walk policy while D-04 remains
unresolved. Spawn volume dimensions and other temporary presentation choices are
named constants in `DevelopmentSessionScenario`, in room-frame metres.

Every attempt records `BlockMetadata` with participant identifier, supplied order,
condition and rerun index. Only explicit `TargetDestroyed` events contribute to
the primary count. Target expiration is a v2 `Despawned` lifecycle record and
does not score. Rendering consumes the raw acquisition snapshot separately from
logging; no render prediction can rewrite a recorded pose.

**Abort** requires a reason and is available throughout an unfinished session.
Only a running block emits `BlockAborted`. At or beyond a block deadline the
engine first records its exact normal end, preserving monotonic event ordering.
A valid **Re-run block** closes the earlier recording, reserves a distinct
`-rNNNN` directory and uses a distinct attempt ID. The earlier files remain intact.
Notes and provisional sync markers use the same shared monotonic clock and event
sequence. D-12 synchronization approval remains outstanding.

The panel reports observed acquisition rate, dropped samples, disk space, tracking
validity and frames exceeding the explicit 33 ms development diagnostic threshold.
These counters are diagnostics, not a physical or full-load performance pass.
**Open rendering / replay tools** exposes the separate viewing controls; the
**Session controls** button returns to this panel.

Editor output defaults below `unity/data/`; packaged-player output is relative to
the extracted player directory, according to the validated machine `dataRoot`.
Both are local outputs and must remain outside source control. Copying a build
does not make its synthetic configuration a measured calibration.

## Checks

Run from the repository root:

```powershell
dotnet run --project tools/runtime-tests/SessionChecks.csproj
dotnet run --project tools/runtime-tests/SessionRecordingAdapterChecks.csproj
dotnet run --project tools/runtime-tests/SessionInteropFixture.csproj -- --output test-results/dev08-session-interop
./scripts/test.ps1 -Suite unity-play
```

The retained interop fixture uses a manual clock and actual source/configuration
hashes. It advances four fixed-duration blocks without waiting 20 minutes, writes
samples/targets/events through the real writer, then exercises close-before-rerun
and preservation of the earlier output. Reports and repeated runs receive unique
names. This is separate from a wall-clock acquisition or full player soak.

An opt-in standalone `-eltsSessionScreenshot ABSOLUTE_NEW_PNG_PATH` probe renders
the UI Toolkit panel to an offscreen texture. It does not start a recording or
validate native mouse/keyboard interaction.

## Deferred evidence

Actual two-display assignment, SteamVR and physical tracker stability, measured
calibration, real ELTS-link behavior, approved sync markers, full-load Study-PC
timing and the lab operator dry session remain deferred. The apparatus is built;
the testing equipment needed for these checks is unavailable. DEV completion does
not pass the baseline P4 tasks or any physical/safety gate.
