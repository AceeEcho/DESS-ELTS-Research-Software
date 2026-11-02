# Participant tutorial script

Status: working draft, revised November 2, 2026; detailed planning through Part 11. For wording and visual planning; not a finalized procedure or an implemented tutorial.

Based on `participant-tutorial-script-outline.md`. Detailed narration and visual directions live here; the outline has been revised with the user's agreement to reflect the setup and tracker-calibration sequence.

Audio generation and selection follow [the participant tutorial audio workflow](participant-tutorial-audio-workflow.md): two takes per node, identified as Top and Bottom; download the user's chosen take and link it directly beneath the matching spoken chunk.

Current audio workflow: Parts 1–3 have selected, downloaded audio linked below. Parts 2 and 3 selections were confirmed on November 2, 2026. Parts 4–11 remain deferred until requested.

A [reading script](participant-tutorial-reading-script.md) contains only the narration and concise action/wait cues. Update the Spoken and Action / wait lines here, then regenerate that copy; see the audio workflow for the sync command.

## Format

Each numbered chunk pairs **Spoken** narration with **Screen / animation** directions that happen during the narration. Whole numbers begin a new topic; decimal numbers continue that topic. Each spoken chunk can be recorded as a separate clip.

Label each chunk's exit explicitly: **Manual continue** when the operator needs time to finish a physical action or check, and **Automatic** when the next chunk simply continues the explanation. Separate audio clips do not imply separate manual confirmations. Keep operator pause and replay available during automatic sequences.

**Production notes** are not spoken. They identify decisions or operator actions needed before production. Visuals below are proposed, not claims about features already implemented. Use a calm, clear voice, allow time to follow each instruction, and keep replay available. Advance interactive steps on their confirmed completion event rather than on narration length alone.

## Part 1 — Welcome and orientation

**Visual direction for all of Part 1:** Create a calm, full-screen introduction with a soft charcoal background, large warm-white text, and a muted blue accent. Keep the background still. Use a plain, readable sans-serif font and simple line illustrations. Avoid flashing, pulsing, camera movement, decorative particles, or simulated ELTS light effects. This is an orientation screen, not a demonstration of the apparatus.

Use a 1920 × 1080 reference canvas and scale proportionally to the participant display. Keep essential content inside a 10% margin on all sides. At this reference size, use approximately 64 px for the main heading, 40 px for stage labels, and 32 px for supporting text. Reserve the lower portion of the screen for captions so they never overlap the main content. These are production starting values; check readability on the actual display.

Use short fades of approximately 300–400 milliseconds, with at most 16 px of vertical movement. Elements should settle into a fixed position. Synchronize reveals to the corresponding words in the final voice recording rather than assigning fixed timestamps before the recording exists. All timings below describe visual pacing, not study or exposure durations.

**Spoken:** “Welcome to the ELTS Project Research Study. Thank you for being a participant. Before the study begins, we'll guide you through equipment setup and practice.”

**Action / wait:** [CONTINUE AUTOMATICALLY] Play Part 1.1.

**Audio:** Selected — Top take, latest warm welcome. [01-welcome.mp3](participant-tutorial/audio/part-01/01-welcome.mp3). Use for this spoken chunk only.

**Screen / animation:**

1. Begin on the charcoal background. Fade in “Welcome to the ELTS Project Research Study” near the upper center, split across two centered lines if needed, with a smaller “Participant tutorial” label above it. Let the heading become fully visible before narration starts. As the narrator thanks the participant, fade in “Thank you for being a participant” beneath the heading. Fade that supporting line out before revealing the three-stage overview.
2. As the narrator says “Before the study begins,” reveal a horizontal three-stage overview across the middle of the screen. Its labels, from left to right, are “Setup,” “Practice,” and “Study.” Keep all three equally neutral at first. A thin line connects the stages to communicate order; it is not a progress bar and should not fill.
3. Above Setup, draw a simple line illustration of equipment beside a person. Above Practice, use a target with a small circular arrow beside it. Above Study, use four small numbered panels grouped together. These are overview symbols, not equipment-fitting instructions or previews of the scenario order.
4. On “equipment setup,” give Setup a muted blue outline and brighten its label. On “practice,” briefly give Practice the same emphasis while keeping Setup visible. Do not add check marks: neither stage is complete.
5. At the end of the sentence, return Practice to its neutral style and leave Setup highlighted as the next stage. Hold the overview still for approximately one second before Part 1.1.

**Welcome delivery note:** Use the same warm, calm delivery as Parts 1.1 and 1.2, with `[warmly] [calmly]` in the voice prompt. Keep a clear speaking voice, steady volume, and normal sentence pauses. Avoid breathy, whispered, or drawn-out delivery; do not stretch the opening greeting.

### Part 1.1

**Spoken:** “We'll explain each step as you go. If you have a question or need an instruction repeated, please ask the operator.”

**Action / wait:** [CONTINUE AUTOMATICALLY] Leave a short pause for questions, then play Part 1.2. The operator can pause if needed.

**Audio:** Selected — Top (take 1). [01-01-guidance-and-questions.mp3](participant-tutorial/audio/part-01/01-01-guidance-and-questions.mp3). Use for this spoken chunk only.

**Screen / animation:**

1. Keep the heading, overview, and highlighted Setup stage in their existing positions. Do not move or replace the overview.
2. During “We'll explain each step as you go,” fade in the supporting line “One step at a time” beneath the overview and above the caption area.
3. As the narrator begins “If you have a question,” crossfade that supporting line to “Questions or need a repeat? Ask the operator.” Add a small speech-bubble outline beside it. The icon remains still and carries no extra meaning beyond the text.
4. Keep this message visible through the end of the sentence. Leave a short pause before the next chunk so the participant can ask a question.

### Part 1.2

**Spoken:** “When you're ready, let the operator know. We'll begin with equipment setup.”

**Action / wait:** [WAIT — OPERATOR CONTINUE] Confirm the participant is ready, then begin equipment setup.

**Audio:** Selected — Top (take 1). [01-02-ready-for-setup.mp3](participant-tutorial/audio/part-01/01-02-ready-for-setup.mp3). Use for this spoken chunk only.

**Screen / animation:**

1. Keep the three-stage overview in place. Crossfade the supporting line to “Let the operator know when you're ready.” Keep Setup highlighted; Practice and Study remain neutral.
2. On “equipment setup,” gently brighten Setup's existing outline over approximately 300 milliseconds, then leave it steady. Do not flash, pulse, or mark it complete.
3. After narration finishes, hold this exact screen until the operator proceeds. Show no countdown and no participant-facing Next button. Waiting must not start calibration, a scenario, or any apparatus action.
4. When the operator proceeds, fade out the welcome heading, overview, and supporting line together over approximately 400 milliseconds. Keep the background constant. Fade in Part 2's equipment setup content, then begin its narration once that content is readable.

**Production notes:** Record Parts 1, 1.1, and 1.2 as separate clips. Provide captions matching the narration exactly. Operator replay should restart the selected chunk and its visual cues without advancing the tutorial or duplicating elements. A replay request or an operator pause should interrupt the hold or gap between chunks; do not let a fixed pause prevent clarification. In a reduced-motion version, use fades without positional movement. If operator controls are not yet connected, hold on Part 1.2 and expose continuation only in the production preview controls. Acceptance for this section is that every spoken cue has a matching visual, the overview stays readable, and Part 2 cannot begin without an explicit operator action.

## Part 2 — Participant setup

**Visual direction for all of Part 2:** Continue Part 1's charcoal background, warm-white text, muted blue emphasis, typography, margins, and caption area. Use a fixed heading, a central equipment overview, and one supporting instruction below it. Keep illustrations still after their entrance. Use 300–400 millisecond fades; synchronize visual cues to the spoken phrases when audio is produced later.

Illustrations must match the actual equipment selected for the session. Use verified photographs or reference-based line drawings. Until those references are available, use labeled equipment symbols in the production preview rather than inventing device shapes, attachment positions, wiring, or fitting procedures. This section introduces the equipment; the operator handles fitting and checks.

**Spoken:** “The operator will now fit you with the tracking equipment, eye tracker, and heart-rate monitor.”

**Action / wait:** [WAIT — OPERATOR CONTINUE] Fit all equipment before continuing to the comfort message.

**Audio:** Selected — Bottom (take 2). [02-participant-setup.mp3](participant-tutorial/audio/part-02/02-participant-setup.mp3). Use for this spoken chunk only.

**Screen / animation:**

1. After the operator advances from Part 1, fade in the heading “Equipment setup” near the upper center. Above it, place the smaller label “Participant tutorial.” Begin narration after the heading is readable.
2. Across the center, reserve three equal-width positions for the equipment illustrations. Label them “Tracking equipment,” “Eye tracker,” and “Heart-rate monitor,” from left to right. Keep positions fixed throughout Part 2; leave sufficient space for the labels to wrap cleanly onto two lines without shrinking them.
3. On “tracking equipment,” fade in the first illustration and its label. Give it a muted blue outline to identify the item being discussed.
4. On “eye tracker,” fade in the second illustration and label. Return the first illustration's outline to neutral gray, then emphasize the eye tracker in blue.
5. On “heart-rate monitor,” reveal and emphasize the third illustration in the same way. Keep the first two illustrations visible.
6. At the end of the sentence, return all three outlines to neutral. Fade in the supporting instruction “Please wait while the operator fits the equipment.” Hold this overview for as long as fitting takes. These reveals identify equipment; they do not prescribe a fitting order or indicate that anything has been checked.

**Transition — Manual continue:** Stop after this narration. The operator fits all the equipment, then manually continues to Part 2.1. Finishing the clip does not advance the tutorial. Keep the operator's Continue control separate from the participant-facing screen; do not show a countdown.

### Part 2.1

**Spoken:** “Please let the operator know if anything feels uncomfortable or needs adjusting.”

**Action / wait:** [CONTINUE AUTOMATICALLY] After a short pause, play the equipment-check message. Pause if an adjustment is requested.

**Audio:** Selected — Bottom (take 2). [02-01-comfort-and-adjustments.mp3](participant-tutorial/audio/part-02/02-01-comfort-and-adjustments.mp3). Use for this spoken chunk only.

**Screen / animation:**

1. Keep the heading and all three equipment illustrations in their existing positions. Crossfade the supporting instruction to “Let the operator know if you need an adjustment.”
2. Reveal a small speech-bubble outline beside the instruction, using the same style as Part 1.1. Use the muted blue accent for the icon and warm-white text for the message.
3. Keep the equipment illustrations neutral. Do not animate straps tightening, sensors moving onto the body, or the participant making adjustments; the operator provides any specific instructions needed.
4. Hold the message through the end of the narration. After a brief, configurable gap of approximately one second, move automatically to Part 2.2. The operator can pause or replay if the participant requests an adjustment; otherwise no manual Continue is required here.

**Transition — Automatic:** After the comfort message and brief gap, play Part 2.2. An operator pause suspends this transition until playback resumes. Do not insert another mandatory confirmation between these two explanations.

### Part 2.2

**Spoken:** “The operator will check the equipment before we continue. Once setup is complete, we'll move on to calibration.”

**Action / wait:** [WAIT — OPERATOR CONTINUE] Finish equipment checks and adjustments, then begin head-tracker calibration.

**Audio:** Selected — Bottom (take 2). [02-02-setup-check-and-calibration-transition.mp3](participant-tutorial/audio/part-02/02-02-setup-check-and-calibration-transition.mp3). Use for this spoken chunk only.

**Screen / animation:**

1. Keep the heading and equipment overview visible. On “check the equipment,” crossfade the supporting instruction to “Please wait while the operator checks the equipment.” Fade out the speech-bubble icon with the previous message.
2. On “move on to calibration,” fade in a smaller “Next: Calibration” label between the equipment overview and the supporting instruction. Keep it visually secondary so the waiting instruction remains the clearest action.
3. When narration ends, hold the entire screen still while the operator completes the equipment checks and any requested adjustments. Continue only when the operator confirms the checks are finished. Do not show animated progress, a countdown, sensor readings, a calibration target, or a success badge during this hold.
4. When the operator proceeds, fade the equipment illustrations and supporting text out together over approximately 400 milliseconds. Keep the background constant. Change the heading to “Head-tracker calibration” and reveal Part 3's opening content. Begin its narration only when that content is readable.

**Transition — Manual continue:** Once equipment checks and any adjustments are finished, the operator manually continues to Part 3 for head-tracker calibration. This is the second manual hold in Part 2.

**Production notes:** Parts 2, 2.1, and 2.2 are separate spoken chunks. Playback order: announce fitting → manual hold for fitting → comfort message → automatic equipment-check message → manual hold for checks → Part 3 head-tracker calibration. The holding demonstration now follows head calibration as Part 3.1. The selected audio for each chunk is downloaded and linked above. Use those files without adding operator-wait silences to the recordings. Use captions matching the spoken wording when audio is added. Keep comfort instructions available for replay during setup. Do not show equipment readiness or calibration acceptance based on narration ending. If operator controls are not connected, hold at each manual transition and provide continuation only in production preview controls.

## Part 3 — Head-tracker calibration, holding position, and gun-tracker calibration

**Sequence:** Head-tracker calibration → manual confirmation → holding demonstration → manual confirmation → gun-tracker calibration → manual confirmation → interface familiarization. These are separate steps; the participant does not need to hold the replica during head-tracker calibration.

**Visual direction throughout:** Continue the charcoal background, warm-white text, muted blue accents, fixed heading, and reserved caption area. Use short 300–400 millisecond fades for explanatory content. During either calibration capture, keep the reference dot completely stationary and unobstructed. Its position must use the configured calibration reference for that step; the head and gun calibration steps need not share a position unless the confirmed procedure specifies this. Dot position, size, and contrast must be configurable and checked on the actual display. Do not use pulsing, drifting targets, aiming scores, decorative reticles, countdowns, or animated progress. Place instructions and captions away from the dot.

**Spoken:** “First, we'll calibrate the head tracker. Please stand up straight and look straight ahead at the dot on the screen. Keep this position while the operator calibrates the tracker.”

**Action / wait:** [WAIT — OPERATOR CONTINUE] The participant stands upright and looks forward at the screen dot. Complete and check head-tracker calibration before continuing.

**Audio:** Selected — Bottom (take 2). [03-head-tracker-calibration.mp3](participant-tutorial/audio/part-03/03-head-tracker-calibration.mp3). Use for this spoken chunk only.

**Screen / animation:**

1. Enter after the operator finishes the equipment checks in Part 2.2. Fade in the heading “Head-tracker calibration,” with “Participant tutorial” above it.
2. During “stand up straight,” show a simple side-view illustration of an upright person facing the display. Use the supporting instruction “Stand up straight.” Show no replica in the person's hands. The illustration explains posture without prescribing unconfirmed foot placement or joint angles.
3. Before “the dot on the screen” is spoken, fade out the posture illustration and reveal the actual head-calibration reference dot. Use a small, clearly visible solid dot with a clean edge. Keep the rest of the central area clear. Do not animate a line between the person's eyes and the target during capture.
4. Show “Look straight ahead at the dot” above or below the target area, with enough separation that the text and captions cannot obscure it. On “Keep this position,” change the instruction to “Keep looking at the dot while the operator calibrates the head tracker.”
5. When narration finishes, keep the dot and instruction unchanged. The operator positions the participant as needed, performs the head-tracker calibration, and reviews it. Capture starts through the actual calibration controls, not simply because narration has finished. Keep the target visible through capture and review; do not imply acceptance from the participant's gaze or posture alone.
6. If a repeat is needed, keep or restore the same reference dot and replay the instruction on the operator's request. After the operator confirms head calibration and proceeds, fade out the dot and instruction and reveal Part 3.1. Do not require the participant to keep staring at the dot once the operator ends this step.

**Transition — Manual continue:** Hold while the participant stands upright and looks forward at the dot. The operator completes and reviews head-tracker calibration, then explicitly continues to the holding demonstration. No automatic advance into gun handling.

### Part 3.1 — Holding the study replica

**Spoken:** “For this study, you'll use a replica that cannot fire. The operator will show you how to hold it with the stock against your shoulder. Please try this position, and let the operator know if it feels uncomfortable.”

**Action / wait:** [WAIT — OPERATOR CONTINUE] Demonstrate the stock-to-shoulder position, let the participant try it, and check comfort.

**Audio:** Selected — Top (take 1). [03-01-holding-the-study-replica.mp3](participant-tutorial/audio/part-03/03-01-holding-the-study-replica.mp3). Use for this spoken chunk only.

**Screen / animation:**

1. Fade in the heading “Holding the study replica,” with “Participant tutorial” above it. In the center, show a reference-based illustration of the actual non-firing study replica. During “a replica that cannot fire,” display the supporting label “Non-firing study replica.” Do not name its model or show ammunition, firing, recoil, or weapon-operation controls.
2. On “The operator will show you,” crossfade the central illustration to a large side-view drawing of a person holding the replica with its stock resting against their shoulder. Use a reference photograph of the actual operator-demonstrated position to create this drawing. Frame the upper body and the full replica so the stock-to-shoulder contact is easy to understand. Do not crop out the point being explained.
3. On “stock against your shoulder,” highlight the stock with a steady muted blue outline. Add a thin leader line from the word “Stock” to that part of the replica. Use a second short label, “Against your shoulder,” beside the contact area. Do not animate the replica sliding forcefully into the body. The final illustration should show a settled, comfortable position.
4. On “Please try this position,” fade in the supporting instruction “Try the position with the operator's help.” Keep the illustration and stock highlight visible. The illustration remains an example; the operator demonstrates the position in person and helps accommodate the participant.
5. On “if it feels uncomfortable,” crossfade the supporting line to “Let the operator know if you need an adjustment.” Add the same speech-bubble icon used earlier. Keep the shoulder contact visible and all other graphics still.
6. At the end of narration, hold this screen while the participant tries the position and the operator checks it. Do not show a countdown, automatic completion, or an aiming challenge. Do not infer completion from tracking data or the audio ending.
7. After the operator confirms the participant can hold the replica in the intended position and manually continues, fade out the illustration and labels over approximately 400 milliseconds. Keep the background constant, reveal Part 3.2's gun-tracker calibration heading, and begin its narration once the opening content is readable.

**Transition — Manual continue:** The operator demonstrates the position, lets the participant try it, and resolves any discomfort before continuing to Part 3.2. The head-tracker calibration has already been completed.

**Production notes:** The user confirmed a non-firing study prop replica and a stock-to-shoulder position. Exact hand placement, shoulder choice, stance, and other handling details have not been specified; use the operator's confirmed demonstration as the visual reference rather than inventing a prescribed technique. This chunk teaches the basic study holding position, with aiming at the calibration dot introduced next in Part 3.2. The selected narration is downloaded and linked above.

### Part 3.2 — Gun-tracker calibration

**Spoken:** “Now we'll calibrate the gun tracker. Hold the replica with the stock against your shoulder, as the operator showed you. Look down the sights and aim at the dot on the screen. Keep this position while the operator calibrates the tracker.”

**Action / wait:** [WAIT — OPERATOR CONTINUE] The participant looks down the sights and aims at the screen dot. Complete and check gun-tracker calibration, then let them relax the position.

**Audio:** Selected — Top (take 1). [03-02-gun-tracker-calibration.mp3](participant-tutorial/audio/part-03/03-02-gun-tracker-calibration.mp3). Use for this spoken chunk only.

**Screen / animation:**

1. Enter only after the operator confirms the holding demonstration in Part 3.1. Fade in the heading “Gun-tracker calibration.”
2. During “stock against your shoulder,” briefly show the same reference-based holding illustration used in Part 3.1. Emphasize the stock-to-shoulder contact with its steady blue outline, retaining the same demonstrated position.
3. Before “aim at the dot on the screen” is spoken, fade out the illustration and reveal the actual gun-calibration reference dot at its configured screen position. Keep it still and clear of all text. Display the instruction “Look down the sights and aim at the dot.” The operator demonstrates sight alignment in person if clarification is needed; do not invent a sight picture for an unconfirmed replica sight.
4. During “Keep this position,” change the instruction to “Keep aiming at the dot while the operator calibrates the gun tracker.” Hold the target and instruction unchanged after narration ends.
5. Keep the dot visible while the operator performs and reviews gun-tracker calibration. Do not display hits, scores, simulated muzzle motion, or an automatic alignment-success cue. The tutorial's dot is a reference for calibration, not a scored practice target.
6. If calibration needs repeating, retain or restore the same target and let the operator replay the instruction. After the operator completes and accepts this step, they tell the participant they can relax the position and manually continue to Part 3.3. Fade out the dot only when this step has ended.

**Transition — Manual continue:** Hold while the participant looks down the sights and aims at the reference dot. The operator completes and reviews gun-tracker calibration before continuing. An audio ending, elapsed time, or apparent on-screen alignment must not automatically accept calibration.

### Part 3.3 — Calibration complete

**Spoken:** “Calibration is complete. Next, we'll show you the study interface.”

**Action / wait:** [CONTINUE AUTOMATICALLY] Introduce the study interface.

**Audio:** Selected — Bottom (take 2). [03-03-calibration-complete.mp3](participant-tutorial/audio/part-03/03-03-calibration-complete.mp3). Use for this spoken chunk only.

**Screen / animation:** After the operator has confirmed both tracker calibrations, display “Calibration complete” with “Next: Study interface” beneath it. Keep the screen still while this short narration plays. Then crossfade into Part 4's opening view. Do not play this chunk on an unsuccessful or incomplete calibration.

**Transition — Automatic:** Once the operator has explicitly ended Part 3.2, this short transition message leads automatically to Part 4. No additional mandatory Continue is needed between the completion message and the interface introduction. Operator pause and replay remain available.

**Production notes:** The participant actions and order above were supplied by the user. Exact capture controls, capture duration, reference-dot coordinates, acceptance criteria, and retry mechanics still need to be matched to the physical calibration procedure. Do not invent these values or treat synthetic development calibration as physical acceptance. Keep preview states clearly identified when real capture is not connected. Selected narration for each chunk is downloaded and linked above.

## Part 4 — Interface familiarization

**Purpose:** Introduce the actual participant view and let the participant try each required control before free practice. Use the confirmed physical replica controls; mouse/keyboard development controls are not participant instructions.

**Visual direction throughout:** Transition from the calibration screen into a separate tutorial scene with a persistent “Tutorial” label. Keep captions and the current instruction outside the target area. Present one element or action at a time using a steady muted blue outline. Do not add a crosshair, hit marker, scoring display, or status indicator unless it belongs in the confirmed participant interface. Tutorial highlights must disappear before ordinary practice. ELTS remains inactive during Parts 4 and 5.

**Spoken:** “Let's look at the screen you'll use during the study. These are the practice targets.”

**Action / wait:** [CONTINUE AUTOMATICALLY] Play the aiming instruction.

**Audio:** Not generated — deferred until writing is complete. Planned folder: `participant-tutorial/audio/part-04/`; filename stem: `04-interface-and-targets`.

**Screen / animation:**

1. After Part 3.3, fade in the tutorial scene with an example of the actual target design. Keep the target stationary for this introduction. Begin narration once the scene is readable.
2. On “practice targets,” outline one example target and place a short “Practice target” label beside it. Keep the label clear of the target's outline and sights. Use the real scene composition and target appearance rather than an unrelated generic illustration.
3. Hold the view still until the sentence ends, then fade out the label and highlight over approximately 300 milliseconds. Keep the target visible for Part 4.1.

**Transition — Automatic:** Continue to the aiming instruction after the clip and a brief configurable gap. No physical task has been requested yet, so no mandatory Continue is needed here.

### Part 4.1 — Try aiming

**Spoken:** “Hold the replica as the operator showed you. Look down the sights and aim at the practice target.”

**Action / wait:** [WAIT — OPERATOR CONTINUE] Let the participant try aiming and confirm they understand.

**Audio:** Not generated — deferred until writing is complete. Planned folder: `participant-tutorial/audio/part-04/`; filename stem: `04-01-try-aiming`.

**Screen / animation:**

1. Keep the same tutorial target in place. Show “Aim at the practice target” above the target area, with captions below the scene.
2. Briefly emphasize the target as it is named, then remove the outline so the participant sees the ordinary target while trying the action. Do not move the target or add an artificial aiming dot over it.
3. Hold after narration while the participant tries aiming. Keep the scene responsive to the real participant tracking. The operator can demonstrate again or request an adjustment without starting any timed study scenario.

**Transition — Manual continue:** Continue after the operator confirms the participant has tried the aiming action and understands the instruction. Do not infer understanding merely from a tracker pose or an elapsed timeout.

### Part 4.2 — Firing, reloading, and interface indicators

**Spoken:** “The operator will show you how to fire at the practice targets and reload. You'll have a chance to try both before practice begins.”

**Action / wait:** [WAIT — OPERATOR CONTINUE] Demonstrate firing, let the participant try it, then demonstrate reloading and let them try it. Continue when both actions are understood.

**Audio:** Not generated — deferred until writing is complete. Planned folder: `participant-tutorial/audio/part-04/`; filename stem: `04-02-controls-demonstration`.

**Screen / animation:**

1. Keep the tutorial scene and target visible. During “fire,” show “Try firing” as the current instruction. The operator demonstrates the confirmed replica firing action and then lets the participant try it. Use the actual tutorial response rather than a prerecorded fake hit.
2. Once that action is complete, the operator advances to “Try reloading” within this chunk's interactive sequence. Show only the confirmed physical reload action or operator-assisted reload instruction. Let the participant try it before continuing.
3. If the final participant interface includes ammunition, remaining time, or another necessary status indicator, briefly highlight the relevant existing indicator during the operator's explanation. Do not introduce development-only statistics or enable feedback settings as part of the tutorial plan.
4. Keep the scene available for replay and clarification. Narration does not need to loop while the participant performs the actions.

**Transition — Manual continue:** The operator advances between the firing and reload demonstrations and continues to Part 5 once both required actions have been tried. A missed target is not, by itself, a reason to block familiarization; confirm comprehension rather than invent an accuracy threshold.

**Production decisions still needed:** Confirm the replica's firing event, physical reload mapping or operator-assisted reload procedure, and the participant-visible indicators. The general narration above supports an operator-led demonstration without inventing those controls. Before making a self-guided version, replace the general demonstration with separate exact spoken instructions for each confirmed action. Do not import R-to-reload, mouse clicks, a 20-round capacity, damage values, or desktop HUD assumptions into the physical study script.

## Part 5 — Guided practice

**Purpose:** Move from individual demonstrations into a separate practice scenario. Practice completion does not start the study or activate ELTS.

**Spoken:** “Now you'll have time to practice using the interface. This is practice, and your shots will not count toward the study results. Let the operator know when you're ready to begin.”

**Action / wait:** [WAIT — OPERATOR START] Confirm readiness, then start practice.

**Audio:** Not generated — deferred until writing is complete. Planned folder: `participant-tutorial/audio/part-05/`; filename stem: `05-practice-introduction`.

**Screen / animation:**

1. Fade out the tutorial overlays and reveal the practice scene in a ready state. Display a persistent “Practice” label in the upper corner and “Let the operator know when you're ready” in the instruction area.
2. Keep the practice timer stopped and do not begin the interactive practice merely because this introduction finishes.
3. When the operator starts practice, remove the ready message and enter Part 5.1. Keep the Practice label visible throughout.

**Transition — Manual start:** The operator starts practice after confirming participant readiness. Use the configured practice duration rather than a duration inferred from the narration or development defaults.

### Part 5.1 — Practice running

**Spoken:** “Use this time to practice aiming, firing, and reloading. If you need help or another demonstration, please ask the operator.”

**Action / wait:** [WAIT — PRACTICE ENDS] Let practice run until its timer finishes or the operator ends it. Help or repeat demonstrations as needed.

**Audio:** Not generated — deferred until writing is complete. Planned folder: `participant-tutorial/audio/part-05/`; filename stem: `05-01-practice-running`.

**Screen / animation:**

1. Play this instruction once when interactive practice begins. Show the real practice targets and the confirmed participant interface. Do not replay introductory narration on every target or action.
2. Present a short text reminder for the current practice action only when needed. Keep reminders outside the aiming area; remove them when the participant no longer needs them.
3. Show target responses and other feedback only as provided by the configured practice experience. If a practice timer is displayed, bind it to the actual practice state. Do not invent a score or change study feedback settings.
4. The operator can pause, clarify, replay an instruction, or restart practice using the available practice controls. Restarting practice must not restart equipment fitting, calibration, or the ELTS demonstration.

**Transition — Practice completion event:** Stay in practice until its configured timer finishes or the operator explicitly finishes it. Then enter Part 5.2. Audio completion alone does not finish practice. Interrupted or restarted practice must not play a false completion message.

### Part 5.2 — Practice complete

**Spoken:** “Practice is complete. Let the operator know if you'd like another explanation before we continue.”

**Action / wait:** [WAIT — OPERATOR CONTINUE] Answer questions and confirm readiness for the ELTS demonstration.

**Audio:** Not generated — deferred until writing is complete. Planned folder: `participant-tutorial/audio/part-05/`; filename stem: `05-02-practice-complete`.

**Screen / animation:**

1. On the real practice-complete event, stop practice interaction and fade the scene to the neutral tutorial background. Show “Practice complete” with “Next: ELTS introduction” beneath it.
2. Play this narration once and hold the screen. The operator can answer questions or return to practice if needed.
3. When the participant is ready and the operator is ready for the upcoming ELTS demonstration, manually continue to Part 6.

**Transition — Manual continue:** Hold for questions and operator readiness before the ELTS introduction and test activation. No apparatus activation is tied to practice completion.

**Production notes:** Practice is separate from scored study scenarios; preserve its identity in logging rather than deleting practice recordings or treating them as study scores. The operator needs real practice start, finish, pause, and restart behavior; preview stand-ins must be labeled. Confirm practice duration and final control wording before audio production. No ELTS activation occurs in Parts 4 or 5.

## Part 6 — ELTS introduction and test activation

**Purpose:** Point to the actual apparatus, centered physically beneath the display, and give one brief ELTS demonstration at the downward-arrow cue. This replaces the previous plan to start a separate demonstration in Part 7.

**Spoken:** “The ELTS apparatus is directly below the middle of the screen. Its lights will be active during some of the study conditions. The operator will now demonstrate the lights briefly.”

**Action / wait:** [ACTION — ELTS DEMONSTRATION] With the operator ready and the downward arrow visible, cue one approved ELTS test at the end of the announcement. Wait for the demonstration to end, then play Part 7. Replaying narration does not repeat the test.

**Audio:** Not generated — deferred until writing is complete. Planned folder: `participant-tutorial/audio/part-06/`; filename stem: `06-elts-introduction-and-test`.

**Screen / animation:**

1. Enter only after the operator continues from Part 5.2 and confirms readiness for this demonstration. Show the heading “ELTS introduction” near the upper center on the same charcoal background. Remove the practice scene, practice targets, and aiming dot completely.
2. On “directly below the middle of the screen,” reveal a single large, muted blue downward arrow. Its shaft starts at the center of the display: 50% of screen width and approximately 50% of screen height. Run it straight down the vertical centerline, ending in a clear arrowhead just inside the bottom edge. The arrow points toward the real ELTS unit centered beneath the physical screen. Do not draw a virtual ELTS unit as though it were part of the screen or point diagonally toward a corner.
3. At a 1920 × 1080 reference size, use a shaft approximately 12 pixels wide and an arrowhead approximately 100 pixels wide, ending around 94% of display height. Treat these as adjustable layout values, not measured apparatus coordinates. The lower-center placement is an intentional exception to the normal content margin. Check the visible display bounds so the arrowhead is never clipped.
4. Reveal the arrow with a short fade or a single downward line-draw animation of approximately 400 milliseconds. Keep the completed arrow steady afterward: no bouncing, pulsing, blinking, or repeated sweeps. Display “ELTS below the screen” beside the shaft, rather than over its arrowhead.
5. During this section, place captions in a wide band above the arrow's starting point, below the heading, so full captions remain visible without crossing the arrow. Reflow text rather than covering or shifting the vertical centerline. Keep caption font size consistent with the earlier sections.
6. Keep the arrow visible through the explanation of the study lights and the demonstration announcement. At the end of “demonstrate the lights briefly,” enter the test-activation cue while the arrow is fully visible. This is the point for the single ELTS test fire; do not wait until a separate later demonstration section.
7. Hold the arrow and heading steady during the physical demonstration. Show “ELTS demonstration” in the supporting-text area. The visible scene must not fabricate a light effect or suggest that an animated screen effect is the apparatus response.
8. Stay on this screen until the demonstration has actually ended through the device/operator workflow. Then move to Part 7's completion message. Do not declare completion based solely on the audio length, a screen animation ending, or a guessed exposure time.

**Transition — Operator-ready test cue, then demonstration completion event:** The operator's readiness confirmation before entry permits this demonstration cue. When the arrow is visible and the announcement has finished, the intended workflow requests one approved ELTS test activation through the apparatus lifecycle interface. If the operator pauses before that point, the cue must wait. If the device is unavailable or not ready, hold for the operator; do not silently skip to “complete.” When the demonstration ends, continue automatically to Part 7's completion message, which then holds for participant questions.

**Replay and retry behavior:** Replaying narration or rebuilding this screen must not fire ELTS again. A pause/resume, duplicate completion callback, scene reload, or returning from another section must not create a second activation. A new test requires an explicit operator decision to repeat the demonstration. Retrying audio is different from repeating the apparatus test.

**Production notes:** This is a plan for synchronization, not hardware execution. Unity continues to send lifecycle commands only; the device owns its lighting and safeguards. The visual arrow supplies no aiming coordinates, poses, turret angles, or individual LED commands. Preserve the existing software safety boundary and device-owned stop behavior. Actual exposure duration and readiness criteria come from the approved physical procedure; the old proposed five/ten-second values are not finalized settings. Preview this section with a clearly labeled simulated cue until the validated device path is available. No hardware is activated and no audio is generated by this document edit.

## Part 7 — Demonstration complete and readiness check

**Spoken:** “The demonstration is complete. Please let the operator know if you have any questions before we continue.”

**Action / wait:** [WAIT — OPERATOR CONTINUE] Answer questions before the study overview.

**Audio:** Not generated — deferred until writing is complete. Planned folder: `participant-tutorial/audio/part-07/`; filename stem: `07-demonstration-complete`.

**Screen / animation:**

1. Enter only after the Part 6 demonstration-end event. The narration ending or an estimated duration must not trigger this screen while ELTS is still active.
2. Fade out the downward arrow and its label together over approximately 400 milliseconds. Keep the charcoal background constant. Replace “ELTS introduction” with “Demonstration complete.”
3. During “questions,” fade in “Questions? Ask the operator” beneath the heading, with the same small speech-bubble icon used earlier. Keep all elements still once visible.
4. Hold after narration while the operator answers questions and confirms readiness. Show no countdown, participant Next button, or additional demonstration cue.
5. When the operator proceeds, fade out the message and reveal Part 8's scenario overview before beginning its narration.

**Transition — Manual continue:** Wait for the operator to continue to Part 8. Replaying this completion message never requests another ELTS test. Any repeated demonstration is a separate explicit operator action under Part 6's repeat rules.

## Part 8 — Procedure overview

**Visual direction throughout:** Use the same neutral background, typography, accent, and caption area as the earlier tutorial sections. Keep a fixed heading “Study overview.” The central visual is a horizontal sequence of four scenario cards with a break marker after each card, including the last. Use ordinary ordinal labels, not condition names, hidden assignments, or development condition codes. This is an explanation, not a live session dashboard.

**Spoken:** “The study has four scenarios. You'll complete them in a randomized order.”

**Action / wait:** [CONTINUE AUTOMATICALLY] Explain the scenario time limits.

**Audio:** Not generated — deferred until writing is complete. Planned folder: `participant-tutorial/audio/part-08/`; filename stem: `08-four-scenarios`.

**Screen / animation:**

1. Fade in the heading, then reveal four equal-size cards across the center labeled “Scenario 1,” “Scenario 2,” “Scenario 3,” and “Scenario 4.” Their numbers mean first through fourth in the participant's sequence, not specific conditions.
2. During “randomized order,” show “Scenario order varies” beneath the sequence. Do not shuffle the actual assigned session order or animate ordinal cards into a confusing order such as 3, 1, 4, 2.
3. Keep every card neutral: none has started or finished. Leave space between cards and after the final card for the break markers introduced in Part 8.2.

**Transition — Automatic:** After narration and a brief configurable gap, continue to Part 8.1. Keep the scenario cards in their existing positions.

### Part 8.1 — Scenario time limits

**Spoken:** “Each scenario has a time limit. The operator will prepare each one before you begin.”

**Action / wait:** [CONTINUE AUTOMATICALLY] Explain the breaks.

**Audio:** Not generated — deferred until writing is complete. Planned folder: `participant-tutorial/audio/part-08/`; filename stem: `08-01-scenario-time-limits`.

**Screen / animation:**

1. On “time limit,” fade a small timer symbol into each card. Replace the order explanation with “Each scenario has a time limit.” Do not animate a running countdown during the overview.
2. A specific duration may appear only if it comes from the confirmed session configuration and is appropriate to disclose. If durations differ, do not imply a shared duration. The narration deliberately avoids an unconfirmed number.
3. On “operator will prepare,” add “Operator prepares each scenario” beneath the cards. Do not show any card as armed or running merely because this explanatory animation plays.

**Transition — Automatic:** Continue to Part 8.2 after the explanation. No operator confirmation is required between these informational chunks.

### Part 8.2 — Breaks in the sequence

**Spoken:** “You'll have a two-minute break between scenarios and another two-minute break after the final scenario.”

**Action / wait:** [CONTINUE AUTOMATICALLY] Move to the first-scenario readiness message.

**Audio:** Not generated — deferred until writing is complete. Planned folder: `participant-tutorial/audio/part-08/`; filename stem: `08-02-break-overview`.

**Screen / animation:**

1. During “between scenarios,” reveal a small “Break” marker between cards 1 and 2, 2 and 3, and 3 and 4. Connect the cards and markers with a thin, static line.
2. During “after the final scenario,” reveal the fourth marker after Scenario 4, labeled “Final break.” Arrange all four cards and four markers to fit within the content margins; do not let the final marker fall off-screen.
3. Fade in a small example panel below the sequence reading “Break — 2:00,” with a visible “Example” label. Keep the value static. This demonstrates the countdown's appearance without starting a real break or implying that time has already elapsed.
4. Hold the complete sequence briefly after narration, then fade into Part 9's readiness screen.

**Transition — Automatic:** Continue to the first-scenario readiness explanation in Part 9, which supplies the mandatory readiness hold.

**Production notes:** Four scenarios, randomized order, and two-minute breaks follow the outline. Check the session plan against this narration before use: planned break duration is 120 seconds, including the final break. Do not silently substitute development defaults, change a live session, or play two-minute narration over a different configured duration. Scenario durations and any disclosure of condition identities remain procedure decisions. The cards explain order; they do not implement randomization.

## Part 9 — Transition to the study

**Spoken:** “The tutorial is complete. Let the operator know when you're ready for the first scenario.”

**Action / wait:** [WAIT — OPERATOR PREPARE/ARM] Confirm readiness, then prepare and arm the first scenario.

**Audio:** Not generated — deferred until writing is complete. Planned folder: `participant-tutorial/audio/part-09/`; filename stem: `09-ready-for-first-scenario`.

**Screen / animation:**

1. Fade out the overview cards and example countdown. Show “Ready for the first scenario?” centered on the neutral background, with “Let the operator know when you're ready” underneath.
2. Keep the scenario timer stopped. Do not show active study targets or activate ELTS as a consequence of entering this screen.
3. Hold after narration while the operator checks readiness and prepares the actual first assigned scenario. The participant may ask questions or request clarification here.
4. Once the scenario is prepared and armed through the confirmed controls, move to Part 9.1. Operator continuation does not, by itself, count as the participant's start action.

**Transition — Manual prepare/arm:** The operator prepares and arms the first scenario after participant readiness. Do not arm it automatically when the tutorial clip ends.

### Part 9.1 — Start a prepared scenario

**Spoken:** “When you're ready, follow the instruction on the screen to start this scenario.”

**Action / wait:** [WAIT — PARTICIPANT START] Start only on the actual participant start action. Let the scenario run. After scenarios 1–3 go to Part 10; after scenario 4 go to Part 11. Reuse this start prompt for each newly armed scenario.

**Audio:** Not generated — deferred until writing is complete. Planned folder: `participant-tutorial/audio/part-09/`; filename stem: `09-01-start-prepared-scenario`.

**Screen / animation:**

1. Enter only when the current assigned scenario is actually armed and ready. Display its ordinal position, such as “Scenario 1 of 4,” and the actual participant start instruction provided by the confirmed input workflow.
2. Keep the time limit at its configured starting value until the real start event occurs. Do not animate a countdown or fade in a fake running scene during narration.
3. Play the start instruction once. If the participant starts while the clip is still playing, stop any remaining start narration and remove the instructional overlay so it does not continue over the scenario.
4. On the actual start event, show the live scenario and actual timer. Tutorial playback must not command a second start, change the assigned condition, or separately control the apparatus lights.
5. If the operator disarms the scenario, remove the start prompt and hold for preparation. A replay of the narration does not rearm or start anything.

**Transition — Participant start event:** Wait for the actual configured start action. At a normal scenario end, route to Part 10 when more scenarios remain and Part 11 after the fourth scenario. Do not replay the whole tutorial between scenarios; reuse only Part 9.1 after each new operator arm.

**Production notes:** Existing development software uses an operator arm followed by a participant trigger start. Confirm the physical replica's exact start action and whether that action is excluded from scored shots before replacing the general spoken wording with a specific instruction. Do not substitute mouse clicks or infer physical behavior from the development player. A pause/resume continues the current scenario rather than starting the next one. A stopped, skipped, repeated, failed, or aborted attempt follows the operator's exception workflow; it must not blindly advance the ordinary four-scenario tutorial count or play false completion narration.

## Part 10 — Breaks between scenarios

**Routing:** Use Parts 10–10.2 after each normally completed scenario that has another scenario following it. After the fourth scenario, use Part 11's final-break sequence instead. A break begins on the real break-start event, not on scenario completion or audio playback.

**Spoken:** “This scenario is complete. Please wait while the operator prepares your break.”

**Action / wait:** [WAIT — OPERATOR START BREAK] Wait for saving to finish, then start the break.

**Audio:** Not generated — deferred until writing is complete. Planned folder: `participant-tutorial/audio/part-10/`; filename stem: `10-scenario-complete`.

**Screen / animation:**

1. On the actual scenario-end event, remove the active target scene and timer and show “Scenario complete.” Do not show a score summary unless the study's feedback policy requires one.
2. Fade in “Your break will begin shortly.” Keep the countdown hidden while the system finishes saving and the operator prepares the break. Do not imply this waiting interval is already part of the two-minute rest timer.
3. Hold this screen after narration until saving is complete and the operator starts the break. If break start occurs before this clip finishes, stop its waiting narration and enter Part 10.1 without overlapping clips.

**Transition — Manual break start:** The operator starts the break when the recording checkpoint is ready. The planned tutorial does not shorten saving or start the countdown by itself.

### Part 10.1 — Break running

**Spoken:** “Your two-minute break begins now.”

**Action / wait:** [WAIT — BREAK ENDS] Show the actual two-minute countdown. At break end, automatically play Part 10.2.

**Audio:** Not generated — deferred until writing is complete. Planned folder: `participant-tutorial/audio/part-10/`; filename stem: `10-01-break-running`.

**Screen / animation:**

1. Trigger this clip from the real break-start event. Show “Break” above a large, centrally aligned countdown starting from the configured two-minute duration.
2. Bind the countdown to actual remaining break time; do not run a separate animation timer based on the narration. Keep surrounding graphics still. Do not add alerts or repeated spoken announcements as the timer approaches zero.
3. Keep study targets absent and do not present a start prompt during the break. A paused tutorial narrator must not independently pause or extend a live break; operator controls must distinguish narration playback from session timing.
4. At the real break-end event, stop any stale break-start narration and transition to Part 10.2. Do not display negative time or loop the countdown.

**Transition — Break-end event:** Continue automatically to the readiness message when the configured break ends. This does not arm or start the next scenario.

### Part 10.2 — Ready for the next scenario

**Spoken:** “Your break is complete. Let the operator know when you're ready for the next scenario.”

**Action / wait:** [WAIT — OPERATOR PREPARE/ARM] Confirm readiness and arm the next scenario, then return to Part 9.1. The break ending does not start a scenario.

**Audio:** Not generated — deferred until writing is complete. Planned folder: `participant-tutorial/audio/part-10/`; filename stem: `10-02-ready-for-next-scenario`.

**Screen / animation:** Replace the countdown with “Break complete” and “Let the operator know when you're ready.” Keep the view still and the next scenario timer stopped. When the participant is ready, the operator prepares and arms the next assigned scenario. Then show Part 9.1's real start instruction with the correct ordinal number.

**Transition — Manual prepare/arm:** Hold for readiness and operator preparation; then wait for the participant start event in Part 9.1. No automatic scenario start at countdown zero.

**Production notes:** Repeat this same break sequence between scenarios; do not create different recordings of identical prompts for every repetition. If the operator skips a break, do not play “Your two-minute break begins now” or claim the participant completed the full break. Handle that exception explicitly according to the procedure and preserve it in the recording. A recording error, apparatus fault, or aborted session must interrupt ordinary transition prompts rather than continue to the next scenario.

## Part 11 — Completion, final break, and post-study form

**Spoken:** “You've completed all four scenarios. Please wait while the operator prepares your final break.”

**Action / wait:** [WAIT — OPERATOR START FINAL BREAK] After the final scenario is saved, start the final break.

**Audio:** Not generated — deferred until writing is complete. Planned folder: `participant-tutorial/audio/part-11/`; filename stem: `11-scenarios-complete`.

**Screen / animation:**

1. Enter only after the actual final required scenario completes. Show “All four scenarios complete.” Do not infer this from four audio plays, four trigger events, or an attempt counter that includes repeats and skips.
2. Fade in “Your final break will begin shortly.” Show no running countdown yet and no next-scenario prompt. Keep the screen neutral while saving finishes and the operator prepares the final break.
3. When the operator starts the final break, stop any remaining waiting narration and enter Part 11.1.

**Transition — Manual final-break start:** The operator starts the final break after the final scenario checkpoint is ready. An early stop, skip, abort, or failed recording needs the appropriate exception wording rather than the normal claim that all four scenarios were completed.

### Part 11.1 — Final break running

**Spoken:** “Your final two-minute break begins now. After the break, you'll complete the post-study form.”

**Action / wait:** [WAIT — FINAL BREAK ENDS] Show the actual two-minute countdown, then automatically play the form instructions.

**Audio:** Not generated — deferred until writing is complete. Planned folder: `participant-tutorial/audio/part-11/`; filename stem: `11-01-final-break-running`.

**Screen / animation:**

1. Play this clip on the actual final-break start event. Show “Final break” and a large live countdown, using the same layout as Part 10.1.
2. Place “Next: Post-study form” beneath the countdown. Keep the form itself closed throughout the break; do not ask the participant to fill it out while the final rest period is still running.
3. On the real final-break end event, replace the timer with the form instructions in Part 11.2. Do not arm a scenario or route back to Part 9.1.

**Transition — Final-break end event:** Continue to Part 11.2. Where the runtime closes the participant recording after the final break, preserve that closure and its save status. The tutorial can remain visible for the form without pretending that a closed sensor recording is still active.

### Part 11.2 — Post-study form

**Spoken:** “Your final break is complete. Please complete the post-study form provided by the operator. If you have any questions, please ask.”

**Action / wait:** [WAIT — FORM COMPLETION] Provide the form and confirm successful completion, or wait for the real submission confirmation.

**Audio:** Not generated — deferred until writing is complete. Planned folder: `participant-tutorial/audio/part-11/`; filename stem: `11-02-post-study-form`.

**Screen / animation:**

1. Show “Post-study form” as the heading and “Please complete the form provided by the operator” below it. Add the small speech-bubble icon beside “Questions? Ask the operator.”
2. Hold while the operator provides the confirmed form. Do not invent a URL, QR code, account sign-in, or external submission destination. If the approved form is integrated into the display, show its real interface only when it is ready.
3. Keep the instruction visible for as long as the participant needs. Do not apply a form countdown or infer submission from opening, scrolling, or closing the form.
4. When completion is confirmed by the actual submission workflow or the operator, proceed to Part 11.3. A failed or incomplete form submission stays here for assistance.

**Transition — Form completion confirmation:** Use a trustworthy submission confirmation when available; otherwise the operator manually confirms completion. Do not advance on elapsed time or narration ending.

### Part 11.3 — Thank you

**Spoken:** “Thank you for being a participant in the ELTS Project Research Study.”

**Action / wait:** [END] Hold the thank-you screen. The operator ends or resets the tutorial; do not restart automatically.

**Audio:** Not generated — deferred until writing is complete. Planned folder: `participant-tutorial/audio/part-11/`; filename stem: `11-03-thank-you`.

**Screen / animation:** Fade in a large “Thank you” heading with “ELTS Project Research Study” underneath. Keep the charcoal background and still layout. Show no new scenario, restart countdown, or promotional content. Hold the final screen after narration until the operator ends or resets the tutorial for the next participant.

**Transition — End:** No automatic restart. Resetting for another participant is an explicit operator action and must not reuse the previous participant's identity or form association.

**Production notes:** Confirm the post-study form content, delivery method, submission/completion signal, and questionnaire timing before final implementation. NASA-TLX responses and sensor recordings must retain the correct participant, session, and scenario associations. This draft does not decide whether NASA-TLX belongs after individual scenarios or in the final form; if per-scenario questionnaires are required, add them to the relevant break workflow before recording audio. Preserve raw recordings and prior attempts. Do not add questionnaire results to a closed sensor recording by rewriting it; use the intended association mechanism.

## Playback and handoff rules for Parts 7–11

- Explanations in Part 8 advance automatically. Questions, preparation, and physical checks use operator holds.
- Real scenario, break, apparatus, and form events control phase changes. Narration and animation completion only advance informational chunks.
- Play one narration clip at a time. On a phase change, cancel stale audio and pending visual callbacks so an old “begins now” or “ready to start” prompt cannot play in the wrong phase.
- Replay repeats the selected narration and its explanatory visuals only. It never rearms a scenario, restarts a timer, opens a duplicate form submission, or activates ELTS again.
- Keep remaining durations, scenario order, and ordinal labels bound to the current session. Never fabricate a completed phase to keep the script moving.
- Parts 1–3 have selected audio files linked to their spoken chunks. Parts 4–11 remain text plans with proposed filenames and no generated audio. These documents do not implement runtime behavior. Use the documented two-take selection workflow for further audio production when requested.
