# Development calibration module

`Elts.Calibration` provides pure, synthetic development math for the future operator-panel wizards. It does not connect to tracking hardware, edit rig files, or establish physical calibration. Every accepted record has `mode: synthetic` and `studyReady: false`.

The pivot solver needs at least 200 valid, diversified room-frame poses. It solves the tracker-local tip offset and fixed world pivot jointly by least squares, reports an RMS residual in metres, and rejects rank-deficient rotations. The caller is responsible for validity and capture timing.

The corner solver consumes four one-second capture means supplied by a caller. It uses top-left, top-right, and bottom-left to form `O`, `U`, and an orthogonalized `V` that keeps the captured V length; bottom-right produces a consistency error. Thresholds live in `CalibrationDevelopmentThresholds` and remain configurable development values, not D-17 approval criteria.

Weapon zero maps a valid observed bore direction to a valid sighting direction, including a stable antiparallel case. Eye input records Left/Right and a finite three-component tracker-to-eye offset. The verification summary requires exactly nine finite angular residuals and reports mean and maximum degrees.

`CalibrationWizard` supplies the pure state sequence `Idle → Capturing → Review → Accepted`, plus `Redo` from review. UI code owns instructions, real capture duration, hardware validity, and persistence. `Accept` is only a state helper; its caller must evaluate the configured development thresholds first. Synthetic acceptance cannot allow a session or study to proceed.

Run the focused checks with:

```powershell
dotnet run --project tools/runtime-tests/CalibrationChecks.csproj
```
