# Development calibration module

`Elts.Calibration` provides pure, synthetic development math for the operator-panel fixture wizard. It does not connect to tracking hardware, edit rig files, or establish physical calibration. Every accepted record has `mode: synthetic` and `studyReady: false`.

The pivot solver needs at least 200 valid, diversified room-frame poses. It solves the tracker-local tip offset and fixed world pivot jointly by least squares, reports an RMS residual in metres, and rejects rank-deficient rotations. The caller is responsible for validity and capture timing.

The corner solver consumes four one-second capture means supplied by a caller. It uses top-left, top-right, and bottom-left to form `O`, `U`, and an orthogonalized `V` that keeps the captured V length; bottom-right produces a consistency error. Thresholds live in `CalibrationDevelopmentThresholds` and remain configurable development values, not D-17 approval criteria.

Weapon zero maps a valid observed bore direction to a valid sighting direction, including a stable antiparallel case. Eye input records Left/Right and a finite three-component tracker-to-eye offset. The verification summary requires exactly nine finite angular residuals and reports mean and maximum degrees.

`CalibrationWizard` supplies the pure state sequence `Idle → Capturing → Review → Accepted`, plus `Redo` from review. `Accept` is only a state helper; its caller must evaluate the configured development thresholds first. Synthetic acceptance permits only the development practice workflow, never study readiness.

The operator panel generates a deterministic known-transform fixture with 240 poses,
virtual one-second stationary corner means and a virtual two-second zero hold. These
durations describe fixture data; no real capture or stability measurement occurs.
It checks pivot RMS, fourth-corner error, raw corner orthogonality and nine angular
residuals against editable provisional limits. Eye side and tracker-local offset
are recorded inputs. Accepted results do not overwrite the startup rig configuration.

An accepted fixture is written with create-new semantics beside the recording.
It retains all pivot poses, corner means, known transforms, outputs, thresholds,
configuration hash and build identity token. The operator-note event records the
sidecar filename and SHA-256. The normal run verifier does not yet automatically
verify this auxiliary sidecar; compare it with that note when auditing a fixture.
Acceptance means reviewed and queued for logging, not a durable recording-close
acknowledgement. Final writer closure must succeed separately.

Run the focused checks with:

```powershell
dotnet run --project tools/runtime-tests/CalibrationChecks.csproj
```
