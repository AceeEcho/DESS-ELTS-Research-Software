# Required follow-up before study use

Status: synthetic development only. The ELTS apparatus is completed and built.
Testing equipment is unavailable. No synthetic pass completes a physical or
safety criterion, mixed baseline task, study decision, or G0–G8 gate.

| Area / baseline reference | Missing evidence or input | Required follow-up | Responsible role |
|---|---|---|---|
| Rig identity and placement, P1 / P5 | Measured geometry, serial bindings, mounting-rigidity evidence | Bind actual trackers; measure positions/orientations/dimensions; calibrate the installed rig; repeat after repositioning or replacement | Rig and calibration operator |
| Pivot, corners, eye and weapon zero, P5.2–P5.10 / D-17 | Physical capture, daily laser checks and approved acceptance limits | Run the specified capture/verification procedures with available metrology equipment and retain measurements | Calibration operator and research lead |
| Tracking and display, P2 / P3 | Real tracking/occlusion and display mapping/luminance tests | Exercise physical trackers, dropout/recovery, projector/display assignment and visibility under lab conditions | Tracking/display engineer |
| Controller integration, P6.5–P6.13 / D-03 / D-10 | Actual controller contract, Unity–Jetson cycles and approved WE/NE behavior | Reconcile with the built controller; verify command/ACK/loss/reconnect and approved condition behavior on the rig | Controller engineer and research lead |
| Physical safety, P6 / D-13 | E-stop, output shutdown, maximum-on-time and exposure/fail-safe measurements | Execute approved bench procedures with independent observation; retain failure-mode evidence | Safety lead and controller engineer |
| Shared timing / D-12 | Approved synchronization method and measured endpoint timing | Measure offset/drift/latency and validate the approved method; mock offset metadata is insufficient | Timing engineer and research lead |
| Study-machine performance, P7 / P8 | Full rig soak, real load, native input and active-player quit behavior | Repeat timing, throughput, loss, closure and operator-control checks on the actual study machine | Test engineer and operator |
| Portability, DEV-12 / P8 | Second machine unavailable | Copy the complete package to a second Windows x64 machine; verify hashes, launch, record, close, analyze and replay | Deployment/test engineer |
| Study readiness and release, G0–G8 / P8 | Pilots, named reviewers, authenticated gate approvals and approved freeze | Complete baseline evidence and approvals; integrate trusted approval identity verification before privileged progress transitions | Research lead, safety lead and maintainer |

The shipped package does not install device software, open a controller endpoint,
energize hardware, or authorize physical work. The mock's permission flag is a
simulation value only. The progress tool deliberately rejects privileged human
approvals until a trusted identity provider is integrated; a self-authored receipt
or file hash cannot substitute for that authority.

Remote repository publication/protection and the named review controls are still
separate from local development validation. This package is not a study release.
