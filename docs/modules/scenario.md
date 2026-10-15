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

Fixed targets hold an assigned pose at one of 50 numbered floor stations around the service area,
collections, cross aisles and window-side reading area. Thirty positions are occupied at once. The replacement
allocator walks the position list and skips occupied positions and positions
vacated during that update, so a kill never puts the replacement straight back
in the same spot. Surviving targets keep their positions. Open aisles, reading
desks, low bookcases and offset cabinets provide open, partly covered and
between-furniture views. The shot model uses the same solid furniture bounds
that are rendered; shelf details are visual only.

`library-fixed-poses-v2` assigns standing, crouched, over-cover peek, side peek,
and frozen mid-stride poses according to station furniture and spawn ordinal.
Small lateral/depth offsets vary the authored positions; eight facing angles include side-on
and turned-away dummies. Every fixed skeleton holds its sampled pose as session
time advances. Crouch height and offsets affect authoritative positions; yaw and
stride remain presentation using the existing approximate synthetic hit anatomy,
not mesh-accurate hitboxes. `DevelopmentGameSettings` exposes offset limits,
peek crouch fraction and the yaw list. Per-target assignment events record the
pose version, name, crouch, yaw, clip phase, stride flag and offsets. The current
position-only replay viewer does not reconstruct these visual pose fields.

Moving targets consume `library-routes-v3` in **spawn order**. The first 30
assignments are routes 0–29; each kill assigns the next route. Every block,
including each participant's repeat of a condition, restarts at route 0.
`DevelopmentScenarioLayout.Route` is an indexable list of 100,000 entries
(default), represented by a fixed integer recipe rather than a large duplicated
waypoint file. It does not use participant IDs, timestamps or runtime randomness.
The list wraps only after its configured length. The test suite exports the full
list to `test-results/library/library-routes-v3.csv` for inspection; that export
uses the default synthetic center and floor, while runtime positions use current
configuration. Route entries include the absolute station, behavior, entry duration
and cycle duration. The opening population is already inside the room at staggered,
deterministic route ages. Replacements approach through side-door openings and
cross aisles, then follow one of ten five-station circulation loops. Narrow stack
routes travel mainly along shelf aisles; reading-area loops go around tables and
chairs. Brief eased stops and a direction reversal every two completed circuits
keep motion continuous. Ducking variants retain their deterministic posture timing.
These are illustrative synthetic movements, not a validated model of human behavior.


Routes are never relocated or skipped to accommodate a different kill order. If
a destination is still occupied, that target waits outside its entry door until
the destination becomes free. The 30-target limit includes entering, hidden and
waiting targets: it is a maximum visible population, not a guarantee that all 30
are visible at every instant. Different participants therefore share the same
route prefix **by spawn number and route age**, while entry times and concurrently
visible actors depend on which targets they kill. Kill order is not prescribed.

`DevelopmentGameSettings` on `DevelopmentView` holds the provisional magazine
size, whole-plan scale, maximum travel speed, waypoint pause and crouch timing.
A validated copy is taken for each view/session; population settings are read from
the shared configuration. `LibraryFloorPlan` owns furniture and circulation paths
in virtual metres. Furniture rendering and shot-blocking boxes share that plan;
table tops/legs and chair parts leave the same openings in both. Decorative books,
lamps and plants are visual details, not mesh-accurate collision geometry. Layout
scale moves furnishings, stations and routes together. The shared floor still
uses `Rig.VirtualFloorY`, with no change to physical display assumptions.

The dashboard starts centered on the participant and physical screen, with the
library visible behind them. The near wall is cut away in this outside view so it
cannot obstruct the station; the participant camera retains the wall. A screen
surface shows the current participant camera image; amber lines show the corrected
bore and its actual screen intersection. The body silhouette is inferred from the
head pose. It is not body tracking. Missing head data hides the screen feed, and
missing weapon data hides the bore/aim marker. Reset restores this station view.


Each block records `DevelopmentLayoutConfigured` with the version, counts,
floor-plan version, geometry and timing values. Each spawn records `DevelopmentTargetAssigned` with
its spawn ordinal, library zone, position index, route index, behavior, `initiallyPresent` flag
and `initialRouteAgeSeconds` offset. Moving targets record
`DevelopmentRouteEntered` when their destination is reserved and their entry
starts (immediately for the opening population, whose offset skips the entry).
Actual target positions continue to be logged on the acquisition clock.
Pause freezes choreography without stopping acquisition. Character animation
follows those positions and cannot move target roots or change scoring.

This is synthetic development choreography, not physical validation or an
approved study-motion decision. `DevelopmentScenarioAdapter` retains the legacy
configured `sine` motion as provenance. `IDesktopControls` still owns the reload
action; the keyboard adapter binds it to R.

Participant feedback toggles default to disabled because D-06 remains unresolved. `SessionPlan` requires a participant identifier and a permutation of `WE_MT`, `NE_MT`, `WE_FT`, and `NE_FT` before a session can start.
