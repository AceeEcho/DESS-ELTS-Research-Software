# Scenario and session runtime

`ScenarioRuntime.cs` keeps synthetic target state in world coordinates (Unity-room meters), never in screen coordinates. `ScenarioDefinition` rejects default spawn volumes and non-finite numeric values. `ScenarioPopulation` owns the active target population: it preserves the configured count, expires targets, and applies the configured respawn delay before the seeded `MaintainCountSpawnRule` may create a visible replacement.

The desktop development session uses a library with **50 positions and 30 active
mannequins** in both fixed and moving conditions. These defaults are shared in
`config/development/scenario.json` (`spawnPositionCount`, `activeTargetCount`,
`pathSequenceLength`). Targets persist until killed or the block ends; the
legacy generic target lifetime and spawn interval do not expire or delay library
targets. A finishing shot records destruction before recording its replacement
on the shared acquisition clock. Pause and tracking-validity controls still apply.

`SessionEventSequence` is supplied explicitly to the block controller and shot model, preserving one strict session event sequence. `ScenarioBlockController.Fire` refuses trigger observations at or after the deadline. The development `HumanoidShotModel` intersects head, body and limb volumes and tests cover first when it is closer to the shooter. Every accepted shot records outcome, region, damage and remaining health. The target has three health; head/body/limb damage is 3/2/1. `TargetHit` records partial damage and `TargetDestroyed` records the health-zero transition. Empty-magazine attempts and manual reloads are separate events and do not count as shots. The generic `HitscanShotModel` remains for non-humanoid fixtures. `TargetsDestroyedCount` derives the primary score from logged destruction events within the monotonic half-open interval `[BlockStarted, BlockEnded)`; uninterrupted 300-second scoring requirements remain unchanged.

`ScenarioBlockController` uses the shared monotonic clock for boundary events.
The library population is owned by `LibraryTargetPopulation`; generic population
fixtures retain their existing lifetime/respawn behavior.

Fixed targets stand still at one of 50 numbered floor positions across five
reading-room aisles. Thirty positions are occupied at once. The replacement
allocator walks the position list and skips occupied positions and positions
vacated during that update, so a kill never puts the replacement straight back
in the same spot. Surviving targets keep their positions. Open aisles, reading
desks, low bookcases and offset cabinets provide open, partly covered and
between-furniture views. The shot model uses the same solid furniture bounds
that are rendered; shelf details are visual only.

Moving targets consume `library-routes-v1` in **spawn order**. The first 30
assignments are routes 0–29; each kill assigns the next route. Every block,
including each participant's repeat of a condition, restarts at route 0.
`DevelopmentScenarioLayout.Route` is an indexable list of 100,000 entries
(default), represented by a fixed integer recipe rather than a large duplicated
waypoint file. It does not use participant IDs, timestamps or runtime randomness.
The list wraps only after its configured length. The test suite exports the full
list to `test-results/library/library-routes-v1.csv` for inspection; that export
uses the default synthetic center and floor, while runtime positions use current
configuration. Route entries include the absolute station, behavior, entry duration
and cycle duration. Behavior variants are standing, crouch/peek, patrol and crouched
patrol. Each moving target enters horizontally from outside the default participant
frustum, then follows its assigned route using active time and eased transitions.

Routes are never relocated or skipped to accommodate a different kill order. If
a destination is still occupied, that target waits at its offscreen entry until
the destination becomes free. The 30-target limit includes entering, hidden and
waiting targets: it is a maximum visible population, not a guarantee that all 30
are visible at every instant. Different participants therefore share the same
route prefix **by spawn number and route age**, while entry times and concurrently
visible actors depend on which targets they kill. Kill order is not prescribed.

`DevelopmentGameSettings` on `DevelopmentView` holds the provisional magazine
size, library spacing, entry timing, patrol distance and peek timing. A validated
copy is taken for each view/session; population settings are read from the shared
configuration. Library geometry uses `Rig.VirtualFloorY`, with no change to
physical display assumptions. The enclosure includes four walls and a ceiling;
perimeter bookcases stay outside the target routes.

Each block records `DevelopmentLayoutConfigured` with the version, counts,
geometry and timing values. Each spawn records `DevelopmentTargetAssigned` with
its spawn ordinal, position index, route index and behavior. Moving targets record
`DevelopmentRouteEntered` when their destination is reserved and their entry
starts. Actual target positions continue to be logged on the acquisition clock.
Pause freezes choreography without stopping acquisition. Character animation
follows those positions and cannot move target roots or change scoring.

This is synthetic development choreography, not physical validation or an
approved study-motion decision. `DevelopmentScenarioAdapter` retains the legacy
configured `sine` motion as provenance. `IDesktopControls` still owns the reload
action; the keyboard adapter binds it to R.

Participant feedback toggles default to disabled because D-06 remains unresolved. `SessionPlan` requires a participant identifier and a permutation of `WE_MT`, `NE_MT`, `WE_FT`, and `NE_FT` before a session can start.
