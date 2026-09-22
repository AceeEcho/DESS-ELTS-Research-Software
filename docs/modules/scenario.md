# Scenario and session runtime

`ScenarioRuntime.cs` keeps synthetic target state in world coordinates (Unity-room meters), never in screen coordinates. `ScenarioDefinition` rejects default spawn volumes and non-finite numeric values. `ScenarioPopulation` owns the active target population: it preserves the configured count, expires targets, and applies the configured respawn delay before the seeded `MaintainCountSpawnRule` may create a visible replacement.

The desktop development session maintains three active clothed humanoid targets. Its default
`config/development/scenario.json` sets `spawnIntervalSeconds` to `0`, so destroying
or expiring a target replaces it without a waiting interval. A finishing shot
records the destruction before recording the replacement spawn, on the shared
acquisition clock. Surviving targets retain their identities and positions;
pause, tracking validity and block-end controls still apply. A nonzero configured
interval remains available for development scenarios that intentionally wait.

`SessionEventSequence` is supplied explicitly to the block controller and shot model, preserving one strict session event sequence. `ScenarioBlockController.Fire` refuses trigger observations at or after the deadline. The development `HumanoidShotModel` intersects head, body and limb volumes and tests cover first when it is closer to the shooter. Every accepted shot records outcome, region, damage and remaining health. The target has three health; head/body/limb damage is 3/2/1. `TargetHit` records partial damage and `TargetDestroyed` records the health-zero transition. Empty-magazine attempts and manual reloads are separate events and do not count as shots. The generic `HitscanShotModel` remains for non-humanoid fixtures. `TargetsDestroyedCount` derives the primary score from logged destruction events within the monotonic half-open interval `[BlockStarted, BlockEnded)`; uninterrupted 300-second scoring requirements remain unchanged.

`ScenarioBlockController` uses the shared monotonic clock for boundary events. Fixed tests place actors on the virtual floor in front of cover. Moving tests place them outside the participant view, run them into one of three cover positions, then repeat a crouch/peek sequence using active test time. A pause freezes the choreography and each new target follows the same path for its lane. This is a repeatable synthetic development pattern, not a resolved D-04 study motion. `DevelopmentScenarioAdapter` retains the staged scenario configuration and the configured `sine` motion as provenance.

`DevelopmentGameSettings` on `DevelopmentView` holds the provisional magazine size, lane spacing, cover depths, run-in timing, entry stagger and peek timing. A validated copy is taken for each view/session, so the visible cover and shot model use the same layout during a run. `IDesktopControls` owns the reload action; the current mouse/keyboard adapter binds it to R and keeps the weapon logic independent of that key.

Participant feedback toggles default to disabled because D-06 remains unresolved. `SessionPlan` requires a participant identifier and a permutation of `WE_MT`, `NE_MT`, `WE_FT`, and `NE_FT` before a session can start.
