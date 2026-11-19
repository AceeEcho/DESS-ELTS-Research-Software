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

## Sight-line calibration for live tracking

`SightLineCalibration.cs` is the calibration used with real trackers. It is pure
C# like the rest of this module. Operator steps are in
[live tracking](../../operator/live-tracking.md).

**What is solved.** Three facts, and nothing else:

| Fact | Numbers | Found from |
| --- | --- | --- |
| Screen pose in SteamVR's tracking frame | 6 | Aimed captures from four standing spots |
| Sight line in the gun tracker's axes | 4 | The same captures, solved together |
| Aiming eye in the head tracker's axes | 3 | Any aimed captures: the eye is on the sight line |

Each capture says "the sight line passes through this dot". `SolveRig` fits the
first two facts together by least squares; `SolveZero` re-aims only the line's
direction for one shooter or after SteamVR shifts its frame; `EyeInHeadTracker`
places the eye. Residuals are angles at the gun, in degrees.

**Frames.** The screen frame has its origin at the lower-left corner of the lit
screen, +X right, +Y up, +Z into the screen, in metres. The tracking frame is
SteamVR's raw frame after the Z reflection in `TrackingCoordinateConverter`. Its
origin is the base station and SteamVR re-estimates its tilt while running, so
the solver never treats tracking +Y as gravity. It is used only as a rough hint
for the first guess and to reject an upside-down solution.

**No starting values are needed.** The solver tries 26 candidate line directions
at three distances, refines the best eight and keeps the best plausible fit, so
any tracker orientation on the gun and any base-station placement work.

**Why four spots.** Two spots at different distances fix where the sights point.
They leave the screen's tilt loose, because a line may pivot about its own dot
without missing it; that shows up as several centimetres of head-position error.
Two more spots, close and off to each side, look at the screen obliquely and pin
the tilt.

**Shot model.** `DevelopmentLiveTracking.cs` publishes the tracked head and a
weapon pose whose bore runs from the eye through the point on the glass under
the sights. That is the construction `DesktopInputModel` already uses for the
mouse, so scoring, logging and replay are unchanged. An error in the eye
position then moves the picture slightly but cannot move a shot off the target
under the sights.

**Limits.** A small residual means the captures agree with each other. It is
not a measurement of the rig, and these values are not D-17 acceptance limits.
The checks below use generated poses with invented noise.

```powershell
dotnet run --project tools/runtime-tests/SightLineChecks.csproj
```

The pivot, corner and weapon-zero pieces above are still the synthetic fixture
the walkthrough saves beside each recording. They are not used by live tracking,
apart from `WeaponZeroSolver`, which builds the published weapon rotation.
