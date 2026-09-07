# Scenario and session runtime

`ScenarioRuntime.cs` keeps synthetic target state in world coordinates (Unity-room meters), never in screen coordinates. Investigator-tunable values are injected through `ScenarioDefinition`; the included session contract preserves the fixed 300-second design duration unless an explicit development override is supplied.

`HitscanShotModel` accepts only a valid, trigger-edge `ShotContext`. It intersects active, currently visible target spheres and emits scalar `blockId` plus `targetId` payload fields for each `TargetDestroyed`. `TargetsDestroyedCount` computes the primary score solely from those logged events within the monotonic half-open interval `[BlockStarted, BlockEnded)`, refusing duplicate destruction identifiers.

Participant feedback toggles default to disabled because D-06 remains unresolved. `SessionPlan` requires a participant identifier and a permutation of `WE_MT`, `NE_MT`, `WE_FT`, and `NE_FT` before a session can start.
