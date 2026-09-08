# Development ELTS session adapter

`SessionEltsLinkAdapter` is a pure, synthetic-only bridge from the session state
machine to the existing `IEltsLink` fixture protocol. It is not a Unity-to-Jetson
integration and cannot energize hardware. A real controller integration remains
subject to the controller contract, D-03 review, physical fail-safe validation,
and the approved WE/NE study behavior.

The session engine calls `PrepareBlock` for every condition. For WE it requires
successful synthetic `HELLO`, `ARM`, and then `START` acknowledgement before the
block starts. `Tick` advances the mock controller, records heartbeat state, and
fails the session if permission is lost or the controller reaches its time limit.
Failure retains the engine's existing best-effort stop behavior.

The default `DevelopmentNeLinkMode.Idle` disarms the mock link before an NE block.
`Armed` is available only as an explicit fixture option. Neither mode determines
D-10 or authorizes a study behavior.

Each sent command and reply is a typed `SessionEvent`: `EltsCommand`, `EltsAck`,
`EltsNack`, `EltsState`, `EltsOffset`, or `EltsLinkLoss`. These use the injected
shared monotonic clock and an injected sequence. Command retries are bounded and
reuse one protocol sequence, allowing the mock controller's idempotence rules to
apply. A reconnect produces a loss; the adapter never resumes the previous block.

Run the synthetic check from the repository root with:

```text
dotnet run --project tools/runtime-tests/EltsSessionChecks.csproj -p:NewtonsoftPath="C:\Program Files\Unity\Hub\Editor\6000.3.23f1\Editor\Data\Managed\Newtonsoft.Json.dll"
```

The check verifies only the synthetic adapter and mock controller. It does not
verify Unity-Jetson communication, physical output, E-stop behavior, timing, or
the final WE/NE study decision.
