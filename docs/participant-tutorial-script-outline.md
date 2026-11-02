# Participant tutorial script outline

Status: initial outline October 19, 2026; revised November 2, 2026 with detailed planning through Part 11, explicit playback transitions, and the ELTS arrow/test cue. This is a planning document, not a finalized participant script or implemented tutorial.

## Voice and delivery

Narration source: ElevenLabs, Eryn - Genuine, Friendly and Natural, using eleven_v4. Follow the working script and audio workflow linked below. Selected audio is saved for Parts 1–3; Parts 4 and 5 now have two requested takes per chunk pending selection. Parts 6–11 await an audio request.

- Use one consistent voice and a calm, clear delivery.
- Generate short clips for individual steps rather than one uninterrupted video.
- Keep spoken narration separate from on-screen text, operator cues and completion conditions.
- Advance interactive steps on the appropriate software event; provide replay and clarification where needed.
- Keep timing values configurable and match narration to the approved procedure.

## Outline

| Segment | Narration purpose | Visual / interaction cue | Completion or transition |
| --- | --- | --- | --- |
| 1. Welcome and orientation | Introduce the study session and explain that guided practice precedes the procedure. | Show a simple overview. | Participant ready; operator proceeds. |
| 2. Participant setup | Announce equipment fitting, then explain comfort adjustments and equipment checks. | Equipment overview, comfort message, then check message. | Manual hold for fitting; comfort message advances automatically to check message; manual hold for checks before continuing. |
| 3. Head-tracker calibration | Ask the participant to stand upright and look straight ahead at a screen dot. | Upright posture illustration, then a stationary calibration dot. | Manual hold for operator capture, review, and continuation. |
| 3.1. Holding the study replica | Identify the non-firing replica and demonstrate the stock-to-shoulder position after head calibration. | Operator demonstration and reference-based holding illustration. | Manual hold while the participant tries the position and the operator checks comfort. |
| 3.2. Gun-tracker calibration | Ask the participant to hold the replica, look down its sights, and aim at a screen dot. | Holding reminder, then stationary gun-calibration dot. | Manual hold for operator capture, review, and continuation. |
| 3.3. Calibration complete | Announce completion after both calibrations are accepted. | Completion message and next-section label. | Automatically continue to interface familiarization; operator pause remains available. |
| 4. Interface familiarization | Introduce targets, try aiming, then operator-led firing/reload and relevant indicator demonstrations. | Actual participant interface; one highlighted element/action at a time. Physical control details still need confirmation. | Automatic introduction to aiming instruction; manual holds for trying aiming and the demonstrated controls. |
| 5. Guided practice | Start separate practice, offer help, and announce completion. | Persistent Practice label, actual practice interaction and configured timer, then completion screen. | Manual practice start; real completion event; manual readiness confirmation before ELTS introduction. |
| 6. ELTS introduction and test activation | Identify the apparatus centered physically beneath the display; announce and cue one brief test fire. | Arrow from the middle of the screen straight down toward the actual ELTS below the display. Keep it visible during the test. | Operator confirms readiness before entry; one test request at the end of the announcement with the arrow visible; await actual demonstration-end event. Replaying audio never refires. |
| 7. Demonstration complete and readiness check | Announce the end of the Part 6 test and invite questions. | Remove the arrow and show completion only after the demonstration ends. No second test activation. | Manual continue to Part 8 after questions and readiness confirmation. |
| 8. Procedure overview | Explain four randomized scenarios, time limits, and two-minute breaks between scenarios and after the final scenario. | Four ordinal cards, time-limit symbols, and four break markers; example countdown remains static. | Automatically advance the explanatory chunks to Part 9 readiness hold. |
| 9. Transition to procedure | Announce tutorial completion and readiness for the first scenario; reuse only the start prompt for later scenarios. | Ready screen, then the actual start prompt and configured timer once armed. | Manual operator prepare/arm, then participant start event. No automatic scenario start from narration. |
| 10. Breaks between scenarios | Announce scenario completion, actual break start, and readiness after the break. | Waiting-for-break screen, real two-minute countdown, then readiness screen. | Manual break start after saving; automatic break-end message; manual preparation/arm before the next participant start. |
| 11. Completion, final break, and post-study form | Announce four-scenario completion, final break, form instructions, and thanks. | Completion/wait screen, actual final-break countdown, form instruction, then thank-you screen. | Manual final-break start; actual break-end event; confirmed form completion; final screen holds without automatic restart. |

## Example neutral narration fragments

- Welcome: “Before the study begins, we will guide you through setup and practice.”
- Calibration: “Please follow the instructions on the screen while we check the equipment.”
- Procedure: “The procedure consists of four scenarios presented in randomized order. Each scenario has a time limit. There will be a two-minute break between scenarios and after the final scenario.”
- Completion: “The scenarios are complete. After the final break, please complete the post-study form.”

## Details to finalize

- Final narration wording; keep the selected Eryn voice and v4 delivery consistent.
- Reference images, remaining holding-position details, reference-dot configuration, physical capture controls, durations, acceptance criteria, and retry mechanics.
- Exact physical firing/reload actions and participant-visible status indicators; configured practice duration.
- Exact scenario time limits and approved ELTS test duration/readiness settings. The former approximately five-second / at-most-ten-second proposal remains unconfirmed; do not use it as an implemented exposure setting.
- Final arrow placement on the real display and mapping of the Part 6 cue to the device lifecycle interface, including completion, pause, and explicit-repeat behavior.
- Interactive completion events, replay behavior and operator intervention points.
- Confirm physical participant-start mapping, exclusion of the start action from scoring, break/save event mapping, and exception behavior for stopped/skipped/repeated/aborted attempts.
- Post-study form content, delivery, completion confirmation, and NASA-TLX timing; add per-scenario questionnaires to breaks if the final procedure requires them.
- Associate NASA-TLX responses and sensor recordings with the corresponding participant, session and scenario.

## Working files

- [Working spoken script and screen directions](participant-tutorial-script.md).
- [Reading script: narration and action/wait cues only](participant-tutorial-reading-script.md).
- [Audio generation, selection, and local file workflow](participant-tutorial-audio-workflow.md).

Confirmed sequence: equipment setup → head-tracker calibration → holding demonstration → gun-tracker calibration → interface familiarization. The user specified upright posture and forward gaze at a dot for the head tracker, and stock-to-shoulder holding with sighted aim at a dot for the gun tracker. Main sections 4 onward retain their numbers. Detailed physical capture settings and acceptance criteria remain to be confirmed.

Part 6 visual/event plan: reveal a downward arrow on the display centerline when identifying ELTS below the screen; keep the arrow visible and request the single test activation after the demonstration announcement. Part 7 only announces completion and checks readiness. This document specifies planned behavior; it does not activate hardware or establish physical validation. Additional audio beyond the explicitly requested sections remains deferred.

Detailed writing now covers Parts 1–11. The normal final sequence is scenario completion → manual break start after saving → actual break countdown → manual next-scenario preparation and participant start; after the fourth scenario, final break → form completion confirmation → thank-you screen. Narration replay never starts or repeats a physical/session action. Remaining procedure decisions above must be resolved before final audio and implementation.
