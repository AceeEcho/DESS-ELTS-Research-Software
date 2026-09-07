# Synthetic tracking source

`SyntheticTrackingSource` implements `ITrackingSource` without Unity, SteamVR,
OpenVR, or hardware dependencies. It receives one injected `ISharedClock` and
emits paired head/weapon observations with one `TrackingAcquisitionStamp`.
Samples are room-frame synthetic values in Unity's +X right, +Y up, +Z forward
meter convention. They are raw fixture observations: no prediction or native
handedness conversion occurs here.

`SyntheticTrackingSettings` copies and exposes immutable fixture settings,
including seed, sample rate, distinct tracker identities, dropout windows, and
trigger windows. Motion base positions, amplitudes, and frequencies are also
settings, with centralized synthetic defaults. A held trigger produces one
falling-edge event on release; lockout prevents repeated events. Disconnecting
an active dropout resets trigger edge history, so reconnecting cannot create a
synthetic release shot. Trigger queues are bounded and expose overflow counts.
Polls are gated by the injected clock interval: an unchanged clock emits at
most one pending pair, and skipped intervals are counted rather than replayed
with invented timestamps. `Dispose` clears pending samples and trigger events.
Motion phase is derived from the captured monotonic timestamp, and frequency
settings are in cycles per second (`Hz`) with an explicit `2*pi` conversion.

Run the standalone checks with .NET 10:

```text
dotnet run --project tools/runtime-tests/SyntheticTrackingChecks.csproj
```

The fixture is synthetic and unmeasured. Passing these checks provides
deterministic development evidence only; it does not validate physical
tracking, calibration, or study readiness.
