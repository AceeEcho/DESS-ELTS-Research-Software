# Synthetic tracking source

`SyntheticTrackingSource` implements `ITrackingSource` without Unity, SteamVR,
OpenVR, or hardware dependencies. It receives one injected `ISharedClock` and
emits paired head/weapon observations with one `TrackingAcquisitionStamp`.
Samples are room-frame synthetic values in Unity's +X right, +Y up, +Z forward
meter convention. They are raw fixture observations: no prediction or native
handedness conversion occurs here.

`SyntheticTrackingSettings` copies and exposes immutable fixture settings,
including seed, sample rate, distinct tracker identities, dropout windows, and
trigger windows. A held trigger produces one falling-edge event on release;
lockout prevents repeated events. Polls never catch up missed wall-clock time:
each call emits one pair at the current shared-clock timestamp. `Dispose`
clears pending samples and trigger events.

Run the standalone checks with .NET 10:

```text
dotnet run --project tools/runtime-tests/SyntheticTrackingChecks.csproj
```

The fixture is synthetic and unmeasured. Passing these checks provides
deterministic development evidence only; it does not validate physical
tracking, calibration, or study readiness.
