# Tracking and shared-clock module

`Elts.Clock.SharedMonotonicClock` is the one timestamp authority for runtime samples and events. It anchors UTC once during process startup, but ordering and duration use only the nonnegative `MonotonicTimestamp` returned by `Now`. UTC is alignment metadata and is never recalculated for a sample, so a wall-clock correction cannot reorder observations. `ManualSharedClock` supplies an explicit UTC anchor and advances only when a test or deterministic fixture requests it.

`Elts.Tracking.ITrackingSource` receives that clock by dependency injection and emits `TrackingSample` values. Every sample has a tracker identity, monotonic timestamp, and an explicit `TrackingValidity`. A valid sample contains a Unity-room `RigidPose`; invalid samples are timestamped but contain no pose that rendering, scoring, or logging consumers can accidentally use. This boundary provides explicit treatment for unavailable, out-of-range, driver-fault, and rejected-native-pose observations. It does not define a final D-12 cross-device synchronization method.

`TrackingCoordinateConverter.Convert` is the only native OpenVR-to-Unity handedness conversion. It reflects native Z once and converts the orientation consistently, producing meters in the Unity room frame: +X right, +Y up, +Z forward. Downstream code receives only `RigidPose` and must not use native coordinates or apply another conversion. Tracker-local offsets and corrections remain Unity-convention values after this boundary. The contract is pure C# 9 and has no UnityEngine, OpenVR, device, or synthetic-motion dependency.

Run the deterministic checks with the installed .NET SDK:

```text
dotnet run --project tools/runtime-tests/TrackingChecks.csproj
```

The console harness shares the source files used by Unity, pins C# 9, and tests UTC anchoring, monotonic ordering, deterministic clock advancement, explicit invalid observations, position basis conversion, a converted quarter turn, degenerate native orientations, and production-clock ordering. Its values are synthetic and do not verify OpenVR availability, tracking hardware, physical timing, trigger timing, or cross-device alignment. Work item: `DEV-02.S001`; it supports P2.2/P2.3 without completing either baseline task or any gate.
