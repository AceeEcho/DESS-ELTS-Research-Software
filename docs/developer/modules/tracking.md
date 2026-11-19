# Tracking and shared-clock module

`Elts.Clock.SharedMonotonicClock` is the one timestamp authority for runtime samples and events. It anchors UTC once during process startup, but ordering and duration use only the nonnegative `MonotonicTimestamp` returned by `Now`. UTC is alignment metadata and is never recalculated for a sample, so a wall-clock correction cannot reorder observations. `ManualSharedClock` supplies an explicit UTC anchor and advances only when a test or deterministic fixture requests it.

`Elts.Tracking.ITrackingSource` receives that clock by dependency injection and declares a `TrackingSourceIdentity` for later diagnostics and log provenance. Each native poll creates one `TrackingAcquisitionStamp`: a source-order sequence beginning at one plus one monotonic timestamp. Head and weapon observations from the same poll reuse the stamp, so they can be paired without separate clock reads. Every sample has a configured tracker identity, a connection state, and an explicit `TrackingValidity`. A valid sample contains a Unity-room `RigidPose`; invalid samples are timestamped but contain no pose that rendering, scoring, or logging consumers can accidentally use. The all-default struct value is explicitly unusable. This boundary provides explicit treatment for unavailable, out-of-range, driver-fault, and rejected-native-pose observations. It does not define a final D-12 cross-device synchronization method.

`TrackingCoordinateConverter.Convert` is the only native OpenVR-to-Unity handedness conversion. It reflects native Z once and converts the orientation consistently, producing meters in the Unity room frame: +X right, +Y up, +Z forward. Downstream code receives only `RigidPose` and must not use native coordinates or apply another conversion. Tracker-local offsets and corrections remain Unity-convention values after this boundary. The contract is pure C# 9 and has no UnityEngine, OpenVR, device, or synthetic-motion dependency.

## Live trackers

`LiveTrackerReader` (Operator assembly) reads the gun and head trackers from
SteamVR once per frame on the main thread. It asks for the **raw, uncalibrated**
universe with zero added prediction, so a SteamVR room setup or "reset standing
position" cannot move the frame under a saved calibration. A tracker counts as
tracked only when SteamVR reports it connected, valid and `Running_OK`.

`DevelopmentLiveTracking` turns those poses into the head pose and sight-line
weapon pose described in [calibration](calibration.md#sight-line-calibration-for-live-tracking)
and publishes them through `DesktopTrackingSource`, the same path the mouse uses.
An untracked head or gun is published as unavailable. For the picture only, the
last tracked head is kept for up to `renderHoldSeconds` (0.5 s by default) so a
brief dropout does not blank the screen; the recorded sample for that moment
stays unavailable and no shot is scored on it.

`SteamVrHealthWatch` follows SteamVR's `vrserver.txt`, read-only. SteamVR has no
API for the moments it re-sets its frame, resets a tracker, or sees the base
station bumped; the log is the only place they appear. Each is written to the
recording as a `LiveTrackingHealth` event and prompts the operator for a quick
aim check. The log's wording is undocumented, so this advises and never gates
data.

Poses are sampled at the display frame rate, not at the 250 Hz recording rate,
and the trigger is read once per frame. Neither is a hardware timing
measurement. Nothing in this section has been checked against physical
measurements.

Run the deterministic checks with the installed .NET SDK:

```text
dotnet run --project tools/runtime-tests/TrackingChecks.csproj
```

The console harness shares the source files used by Unity, pins C# 9, and tests UTC anchoring, monotonic ordering, deterministic clock advancement, paired capture stamps, default-value safety, explicit invalid observations, position basis conversion, a converted quarter turn, degenerate native orientations, and production-clock ordering. The contract deliberately stores raw converted poses only: it has no predicted pose or motion behavior. A future rendering-only head-prediction adapter must remain separate, and the weapon pose must remain raw. Its values are synthetic and do not verify OpenVR availability, tracking hardware, physical timing, trigger timing, or cross-device alignment. Work item: `DEV-02.S001`; it supports P2.2/P2.3 without completing either baseline task or any gate.
