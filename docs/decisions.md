# Decision and work-item IDs

Labels such as **D-12**, **DEV-10** or **P8.4** come from the project's planning
records, which are not in this repository. This page collects what the repository
itself says about each label and where it is used. It does not define any decision
beyond that, and an entry here is not an approval.

## Investigator decisions (D-xx)

These change what a "wrong" session looks like. They are open in the source. They are not hidden settings you can flip to match the hardware.

| Id | What the repo says | What you will see until it is decided |
| --- | --- | --- |
| **D-03** | Unity-to-Jetson transport. A real adapter "requires D-03 and controller-owner review" (`EltsLinkContracts`). | No socket or serial port. See [GAP-HW-JETSON-TRANSPORT](operator/troubleshooting.md#gap-register). |
| **D-04** | Study motion for moving targets. `ScenarioMovementFactory` says MT is a seeded provisional random walk until D-04 defines study motion. `config/development/scenario.json` sets `mtMotion` to `sine` and `provisionalDevelopmentValues` true. `DevelopmentScenarioAdapter` keeps that sine string as provenance and does not silently replace it with `RandomWalk`. `RequiresD04MotionResolution` is true when the configured name is not `RandomWalk`. Nothing in the player reads that flag. | The **visible** desktop session does not run sine or the generic random walk. `DevelopmentSessionScenario` passes movement name `LibraryRouteSequence` for `*_MT` and `FixedLibrary` for fixed blocks, then draws `LibraryTargetPopulation` (`library-routes-v3`, `library-fixed-poses-v2`). Dimensions are "not approved D-04 tuning." Generic `RandomWalk` runs only in `tools/runtime-tests/ScenarioChecks.cs`. If targets follow library routes, that is the current player, not a resolved D-04. |
| **D-05** | D-05 is not named in code, schemas or other docs. | [GAP-DEC-D05](operator/troubleshooting.md#gap-register). |
| **D-06** | "D-06 remains undecided." `ShowHitVisual`, `PlayShotSound`, and `ShowScore` default false. | No code reads those flags, so there is no hit flash, shot sound, or score overlay from them. Ammo and hit counts on the HUD are separate labels. Silence is the current build, not a failed audio device. |
| **D-07** | D-07 is not named in code, schemas or other docs. | [GAP-DEC-D07](operator/troubleshooting.md#gap-register). |
| **D-08** | D-08 is not named in code, schemas or other docs. | [GAP-DEC-D08](operator/troubleshooting.md#gap-register). |

Related decisions the code does name, because they change link and timing symptoms:

| Id | Effect on troubleshooting |
| --- | --- |
| **D-10** | NE behavior is not chosen. Default fixture mode `Idle` disarms the mock before NE. `Armed` is a workbench option only. A dark lamp during NE, even on a future controller, cannot be judged until D-10. The workbench shows `D-10 pending.` |
| **D-12** | Cross-device sync is not chosen. See [GAP-HW-SYNC](operator/troubleshooting.md#gap-register). |
| **D-13** | Exposure and maximum-on time are not chosen. The 30 s extra guard and the 3 s heartbeat are fixture defaults. A timeout in the mock is not a measured exposure limit. |
| **D-17** | Calibration approval limits are not the development thresholds. See [GAP-DEC-D17](operator/troubleshooting.md#gap-register). |

> **GAP-DEC-D05.** Investigator decision D-05 is not named in code, schemas, or docs, so this guide cannot say what it decides or which symptom it would explain. Who can answer: the investigators who hold the decision list. The repo does not point to that list.

> **GAP-DEC-D07.** D-07 is not named. Same limitation and same source.

> **GAP-DEC-D08.** D-08 is not named. Same limitation and same source.

## Work items and plan phases

| Id | What the repo says | Where it appears |
| --- | --- | --- |
| **DEV-01** | Baseline screen/eye geometry work; the geometry harness supports it without completing physical calibration or any gate. | [Geometry](developer/modules/geometry.md) |
| **DEV-02** (`DEV-02.S001`) | Tracking clock and sample contract; supports P2.2/P2.3 without completing either baseline task or any gate. | [Tracking](developer/modules/tracking.md) |
| **DEV-03** | Review regressions for single-capture scheduling and timestamp-based Hz motion. | `tools/runtime-tests/Dev03ReviewChecks.cs` |
| **DEV-10** (`DEV-10.S001`) | The development ELTS link: fixture protocol, mock link and simulated controller only. | [ELTS link](developer/modules/elts-link.md) |
| **DEV12** | The bounded, synthetic end-to-end recording fixture. | `tools/runtime-tests/DevelopmentInteropFixture.cs` |
| **P2**, **P2.2**, **P2.3** | P2 is the shared clock; P2.2 and P2.3 are baseline tasks that DEV-02 supports. | [Tracking](developer/modules/tracking.md), [Pupil timing](operator/pupil-time-synchronization.md) |
| **P6** | Endpoint timing. | [Pupil timing](operator/pupil-time-synchronization.md) |
| **P8.4** | Cross-device synchronization rehearsal and acceptance. | [Architecture](developer/architecture.md), [Pupil timing](operator/pupil-time-synchronization.md), [hardware follow-up](operator/hardware-follow-up.md) |
