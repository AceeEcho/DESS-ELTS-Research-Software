# Participant tutorial script outline

Status: initial outline October 19, 2026; revised October 30, 2026 for tutorial playback and proposed handling familiarization. This is a planning document, not a finalized participant script or implemented tutorial.

## Voice and delivery

Narration source: ElevenLabs, Eryn - Genuine, Friendly and Natural, using eleven_v4. Follow the working script and audio workflow linked below. Finish writing before generating additional audio.

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
| 4. Interface familiarization | Introduce the study interface, practice targets and relevant status indicators. | Present one interface element at a time; detailed control wording remains to be drafted. | Required familiarization steps complete. |
| 5. Guided practice | Explain that the participant can become familiar with the interface and receive feedback before the study begins. | Interactive prompts and feedback; transition into a separate practice scenario. | Practice completed; readiness confirmed. |
| 6. ELTS introduction | Identify the apparatus and explain that its lights are active in some conditions. | Point out ELTS without implying physical validation is complete. | Operator ready for demonstration. |
| 7. Brief ELTS demonstration | Announce the brief demonstration and its end. | Proposed exposure approximately five seconds, at most ten seconds; final duration follows the study protocol and hardware validation. | Demonstration ends and readiness is checked. |
| 8. Procedure overview | Explain four scenarios in randomized order, each with its configured time limit; describe two-minute breaks between scenarios and after the final scenario. | Show the sequence and break countdown concept. | Briefing complete. |
| 9. Transition to procedure | Clearly announce that practice is complete and the study procedure will begin. | Operator readiness and session controls; automatic versus manual transition remains to be resolved. | Operator starts the procedure according to the final workflow. |
| 10. Break prompts | Announce the break and the upcoming transition consistently. | Two-minute countdown; prompts aligned with actual software state. | Break ends; follow the approved next-scenario workflow. |
| 11. Completion and post-study form | Announce completion, the final two-minute break and the additional form. | Show completion and form instructions. | Final break and post-study form complete. |

## Example neutral narration fragments

- Welcome: “Before the study begins, we will guide you through setup and practice.”
- Calibration: “Please follow the instructions on the screen while we check the equipment.”
- Procedure: “The procedure consists of four scenarios presented in randomized order. Each scenario has a time limit. There will be a two-minute break between scenarios and after the final scenario.”
- Completion: “The scenarios are complete. After the final break, please complete the post-study form.”

## Details to finalize

- Final narration wording; keep the selected Eryn voice and v4 delivery consistent.
- Reference images, remaining holding-position details, reference-dot configuration, physical capture controls, durations, acceptance criteria, and retry mechanics.
- Exact scenario time limits and final demonstration duration.
- Interactive completion events, replay behavior and operator intervention points.
- Final practice-to-procedure and break-to-scenario transitions.
- Post-study form content and delivery.
- Associate NASA-TLX responses and sensor recordings with the corresponding participant, session and scenario.

## Working files

- [Working spoken script and screen directions](participant-tutorial-script.md).
- [Audio generation, selection, and local file workflow](participant-tutorial-audio-workflow.md).

Confirmed sequence: equipment setup → head-tracker calibration → holding demonstration → gun-tracker calibration → interface familiarization. The user specified upright posture and forward gaze at a dot for the head tracker, and stock-to-shoulder holding with sighted aim at a dot for the gun tracker. Main sections 4 onward retain their numbers. Detailed physical capture settings and acceptance criteria remain to be confirmed.
