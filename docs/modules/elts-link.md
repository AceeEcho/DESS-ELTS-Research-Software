# ELTS development link module

This module is a development-only, pure-C# boundary for `DEV-10.S001`. It provides a strict newline-delimited JSON codec, `IEltsLink`, a `NullEltsLink`, and a `MockEltsLink` backed by a `SimulatedEltsController`. The only modelled output is the controller's in-memory `LedPermission` Boolean. It has no serial, UDP, MQTT, network, device, process, GPIO, servo, camera, light, or physical E-stop access.

The fixture protocol version is `development-synthetic-v1`, documented by [`../../schemas/protocol/elts-development-synthetic-v1.schema.json`](../../schemas/protocol/elts-development-synthetic-v1.schema.json). Its command concepts and required content come from the specification section 11 message table: `HELLO`, `ARM`, `DISARM`, `START`, `STOP`, `PING`, `STATUS`, `ACK`, `NACK`, and `STATE`. The lower-camel field spellings and integer `*MonotonicTicks` representation are explicit fixture conventions, not a claim about the existing controller. Tick values are nonnegative `TimeSpan` ticks (100 ns) measured by the local endpoint's monotonic clock. They are never UTC and do not decide cross-device synchronization.

The codec accepts only the fields in the committed schema. Before Newtonsoft loads it, an RFC-JSON grammar gate rejects JavaScript extensions including comments, unquoted or single-quoted keys, trailing commas, and noncanonical numeric forms. The codec also rejects malformed JSON, duplicate fields, unknown fields, unsupported versions, and unlisted commands; therefore it cannot carry head pose, target coordinates, turret/servo angles, or individual LED controls. The mock state machine is `Idle → Armed → Active`; only `START` can make its simulated Boolean true. `STOP`, heartbeat timeout, duration-plus-fixture-guard timeout, and a simulated E-stop de-permit it. A simulated E-stop latch cannot be cleared by any command. Reconnect returns to `Idle`, requires `HELLO`, and never resumes `Active`.

The bounded replay cache retains historical replies only for active sequence entries. On a matching replay after the safety state has changed, it returns a current-state `NACK` (`replay_state_changed`) rather than replaying a historic `ACK` that claimed `Active` or permission. A conflicting retained sequence returns `sequence_conflict`. Once a sequence leaves the bounded cache, the per-connection monotonic watermark prevents it from executing again and returns `sequence_expired`. Reconnect begins a new sequence epoch after it has de-permitted and returned to `Idle`.

`EltsClockOffsetEstimate` preserves `unity sent`, `unity ack`, and `Jetson` raw ticks and computes `(sent + ack) / 2 - jetson` without replacing the raw evidence. The default 3-second heartbeat and 30-second extra duration are test-fixture defaults only. They are not approved safety values, exposure limits, or physical timing claims.

Run the deterministic development checks with a Unity-compatible Newtonsoft DLL from the selected Unity installation:

```text
dotnet run --project tools/runtime-tests/EltsLinkChecks.csproj -p:NewtonsoftPath="C:\Program Files\Unity\Hub\Editor\6000.3.23f1\Editor\Data\Managed\Newtonsoft.Json.dll"
```

The checks validate codec strictness, lifecycle and ACK semantics, duplicate replay/conflict handling, reconnect behavior, heartbeat and maximum-duration fixture exits, simulated E-stop latching, prohibited command fields, the null-link boundary, and raw clock-offset math. They cannot prove controller compatibility, a transport, safety behavior, physical E-stop operation, actual stimulus onset, or hardware timing. Those remain deferred to D-03, D-10, D-13, the ELTS code owner, and bench testing.
