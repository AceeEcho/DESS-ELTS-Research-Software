# Required follow-up before study use

Status: synthetic development only. The ELTS apparatus is completed and built.
Testing equipment is unavailable. No synthetic pass completes a physical or
safety criterion or authorizes a study.

| Area | Missing evidence or input | Required follow-up | Responsible role |
|---|---|---|---|
| Rig identity and placement | Measured geometry, serial bindings, mounting-rigidity evidence | Bind actual trackers; measure positions/orientations/dimensions; calibrate the installed rig; repeat after repositioning or replacement | Rig and calibration operator |
| Pivot, corners, eye and weapon zero | Physical capture, daily laser checks and approved acceptance limits | Run the specified capture/verification procedures with available metrology equipment and retain measurements | Calibration operator and research lead |
| Tracking and display | Real tracking/occlusion and display mapping/luminance tests | Exercise physical trackers, dropout/recovery, projector/display assignment and visibility under lab conditions | Tracking/display engineer |
| Controller integration | Actual controller contract, Unity–Jetson cycles and approved WE/NE behavior | Reconcile with the built controller; verify command/ACK/loss/reconnect and approved condition behavior on the rig | Controller engineer and research lead |
| Physical safety | E-stop, output shutdown, maximum-on-time and exposure/fail-safe measurements | Execute approved bench procedures with independent observation; retain failure-mode evidence | Safety lead and controller engineer |
| Shared timing | Approved synchronization method and measured endpoint timing | Measure offset/drift/latency and validate the approved method; mock offset metadata is insufficient | Timing engineer and research lead |
| Study-machine performance | Full rig soak, real load, native input and active-player quit behavior | Repeat timing, throughput, loss, closure and operator-control checks on the actual study machine | Test engineer and operator |
| Portability | Second machine unavailable | Copy the complete package to a second Windows x64 machine; verify hashes, launch, record, close, analyze and replay | Deployment/test engineer |
| Study readiness and release | Pilots, named reviewers, documented research/safety approvals and approved freeze | Complete physical validation, review the evidence and approve the study configuration and release | Research lead, safety lead and maintainer |

The shipped package does not install device software, open a controller endpoint,
energize hardware, or authorize physical work. The mock's permission flag is a
simulation value only. Approval must come from the responsible research/safety personnel; a file hash cannot substitute for that authority.

Remote repository publication/protection and the named review controls are still
separate from local development validation. This package is not a study release.
