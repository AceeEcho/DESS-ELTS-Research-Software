# Required follow-up before study use

Status: synthetic development only. The ELTS apparatus is completed and built.
Physical validation remains pending. The available tracking equipment is recorded
below; other required test/metrology equipment must be confirmed rather than
assumed available. No synthetic pass completes a physical or safety criterion or
authorizes a study.

Session symptoms, log paths, and the hardware facts this repository does not yet
establish are in [session troubleshooting](troubleshooting.md).

## Current inventory and starter room survey

Inventory confirmed October 21, 2026:

- Exactly **one base station** and **two Vive trackers** are available.
- Tracker mounting posts and a headband with mounts are already available.
- A second base station is not part of the current plan.
- The mock rifle is an airsoft platform with a printed pogo-pin cradle for the gun
  tracker; a rail mount for the cradle is in progress. Its trigger is read in
  software ([VIVE gun trigger](../developer/modules/vive-trigger.md)). Cradle and
  mount CAD is kept locally, outside this repository.
- Availability is not evidence of fit, rigidity, calibration, serial binding,
  tracking coverage, timing, or physical/safety acceptance. Keep those checks open.

**Starter room survey: done (November 2026).** The screen is a Samsung QB65R-B
with a visible area of 56 1/4 in × 31 5/8 in (1.42875 m × 0.803275 m),
its bottom edge 41 3/8 in (1.050925 m) above the floor. These are the defaults in
ELTS and the calibration workspace. The 5, 6 and 7 ft floor marks are placed.
The steps below are kept for repeating the survey in another room or after
moving the screen.

The starter task is deliberately limited to measurement and floor marking:

1. Measure and record the actual screen width and height, with units. Identify
   the measured screen edges; do not substitute an advertised diagonal size.
2. Identify the floor projection of the screen's horizontal center. Measure
   perpendicular out from the screen plane along that centerline, not diagonally
   from an elevated screen-center point.
3. Place small blue-tape marks at **5, 6, and 7 feet**, labeling each distance.
   Record the reference point and marks in a simple sketch or photograph.
4. Return the measurements and marked-position record for review. These are
   candidate positions for later comparison, not a confirmed optimal distance or
   an approved participant operating position.

This starter task includes no CAD, fabrication, software changes, tracker
calibration, or powered apparatus testing. It does not complete the broader room
survey, safety paperwork, or physical-validation gates. Preserve clear walkways
and leave equipment placement and test approval to the responsible operators.

## Remaining validation

| Area | Missing evidence or input | Required follow-up | Responsible role |
|---|---|---|---|
| Rig identity and placement | Measured geometry, serial bindings, mounting-rigidity evidence | Bind actual trackers; measure positions/orientations/dimensions; calibrate the installed rig; repeat after repositioning or replacement | Rig and calibration operator |
| Pivot, corners, eye and weapon zero | Physical capture, daily laser checks and approved acceptance limits | Run the specified capture/verification procedures with available metrology equipment and retain measurements | Calibration operator and research lead |
| Tracking and display | Real tracking/occlusion and display mapping/luminance tests | Exercise physical trackers, dropout/recovery, projector/display assignment and visibility under lab conditions | Tracking/display engineer |
| Controller integration | Actual controller contract, Unity–Jetson cycles and approved WE/NE behavior | Reconcile with the built controller; verify command/ACK/loss/reconnect and approved condition behavior on the rig | Controller engineer and research lead |
| Physical safety | E-stop, output shutdown, maximum-on-time and exposure/fail-safe measurements | Execute approved bench procedures with independent observation; retain failure-mode evidence | Safety lead and controller engineer |
| Shared timing | Approved synchronization method and measured endpoint timing | Resolve D-12, including the [Pupil timing integration follow-up](pupil-time-synchronization.md); measure offset/drift/latency and validate alignment in P8.4; mock offset metadata is insufficient | Timing engineer and research lead |
| Study-machine performance | Full rig soak, real load, native input and active-player quit behavior | Repeat timing, throughput, loss, closure and operator-control checks on the actual study machine | Test engineer and operator |
| Portability | Second machine unavailable | Copy the complete package to a second Windows x64 machine; verify hashes, launch, record, close, analyze and replay | Deployment/test engineer |
| Study readiness and release | Pilots, named reviewers, documented research/safety approvals and approved freeze | Complete physical validation, review the evidence and approve the study configuration and release | Research lead, safety lead and maintainer |

The shipped package does not install device software, open a controller endpoint,
energize hardware, or authorize physical work. The mock's permission flag is a
simulation value only. Approval must come from the responsible research/safety personnel; a file hash cannot substitute for that authority.

Remote repository publication/protection and the named review controls are still
separate from local development validation. This package is not a study release.
