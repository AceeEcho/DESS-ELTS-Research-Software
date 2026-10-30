# Participant tutorial script

Status: first working draft, October 30, 2026. For wording and visual planning; not a finalized procedure or an implemented tutorial.

Based on `participant-tutorial-script-outline.md`. Detailed narration and visual directions live here; the outline has been revised with the user's agreement to reflect the setup and tracker-calibration sequence.

Audio generation and selection follow [the participant tutorial audio workflow](participant-tutorial-audio-workflow.md): two takes per node, identified as Top and Bottom; download the user's chosen take and link it directly beneath the matching spoken chunk.

Current writing workflow: finish drafting the remaining script before generating more audio. Existing selected Part 1 audio is retained. Do not generate audio for new sections until the user requests the audio stage.

## Format

Each numbered chunk pairs **Spoken** narration with **Screen / animation** directions that happen during the narration. Whole numbers begin a new topic; decimal numbers continue that topic. Each spoken chunk can be recorded as a separate clip.

Label each chunk's exit explicitly: **Manual continue** when the operator needs time to finish a physical action or check, and **Automatic** when the next chunk simply continues the explanation. Separate audio clips do not imply separate manual confirmations. Keep operator pause and replay available during automatic sequences.

**Production notes** are not spoken. They identify decisions or operator actions needed before production. Visuals below are proposed, not claims about features already implemented. Use a calm, clear voice, allow time to follow each instruction, and keep replay available. Advance interactive steps on their confirmed completion event rather than on narration length alone.

## Part 1 — Welcome and orientation

**Visual direction for all of Part 1:** Create a calm, full-screen introduction with a soft charcoal background, large warm-white text, and a muted blue accent. Keep the background still. Use a plain, readable sans-serif font and simple line illustrations. Avoid flashing, pulsing, camera movement, decorative particles, or simulated ELTS light effects. This is an orientation screen, not a demonstration of the apparatus.

Use a 1920 × 1080 reference canvas and scale proportionally to the participant display. Keep essential content inside a 10% margin on all sides. At this reference size, use approximately 64 px for the main heading, 40 px for stage labels, and 32 px for supporting text. Reserve the lower portion of the screen for captions so they never overlap the main content. These are production starting values; check readability on the actual display.

Use short fades of approximately 300–400 milliseconds, with at most 16 px of vertical movement. Elements should settle into a fixed position. Synchronize reveals to the corresponding words in the final voice recording rather than assigning fixed timestamps before the recording exists. All timings below describe visual pacing, not study or exposure durations.

**Spoken:** “Welcome to the ELTS Project Research Study. Thank you for being a participant. Before the study begins, we'll guide you through equipment setup and practice.”

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

**Audio:** Selected — Top (take 1). [01-01-guidance-and-questions.mp3](participant-tutorial/audio/part-01/01-01-guidance-and-questions.mp3). Use for this spoken chunk only.

**Screen / animation:**

1. Keep the heading, overview, and highlighted Setup stage in their existing positions. Do not move or replace the overview.
2. During “We'll explain each step as you go,” fade in the supporting line “One step at a time” beneath the overview and above the caption area.
3. As the narrator begins “If you have a question,” crossfade that supporting line to “Questions or need a repeat? Ask the operator.” Add a small speech-bubble outline beside it. The icon remains still and carries no extra meaning beyond the text.
4. Keep this message visible through the end of the sentence. Leave a short pause before the next chunk so the participant can ask a question.

### Part 1.2

**Spoken:** “When you're ready, let the operator know. We'll begin with equipment setup.”

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

**Audio:** Not generated — waiting until script writing is complete. Planned folder: `participant-tutorial/audio/part-02/`; filename stem: `02-participant-setup`.

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

**Audio:** Not generated — waiting until script writing is complete. Planned folder: `participant-tutorial/audio/part-02/`; filename stem: `02-01-comfort-and-adjustments`.

**Screen / animation:**

1. Keep the heading and all three equipment illustrations in their existing positions. Crossfade the supporting instruction to “Let the operator know if you need an adjustment.”
2. Reveal a small speech-bubble outline beside the instruction, using the same style as Part 1.1. Use the muted blue accent for the icon and warm-white text for the message.
3. Keep the equipment illustrations neutral. Do not animate straps tightening, sensors moving onto the body, or the participant making adjustments; the operator provides any specific instructions needed.
4. Hold the message through the end of the narration. After a brief, configurable gap of approximately one second, move automatically to Part 2.2. The operator can pause or replay if the participant requests an adjustment; otherwise no manual Continue is required here.

**Transition — Automatic:** After the comfort message and brief gap, play Part 2.2. An operator pause suspends this transition until playback resumes. Do not insert another mandatory confirmation between these two explanations.

### Part 2.2

**Spoken:** “The operator will check the equipment before we continue. Once setup is complete, we'll move on to calibration.”

**Audio:** Not generated — waiting until script writing is complete. Planned folder: `participant-tutorial/audio/part-02/`; filename stem: `02-02-setup-check-and-calibration-transition`.

**Screen / animation:**

1. Keep the heading and equipment overview visible. On “check the equipment,” crossfade the supporting instruction to “Please wait while the operator checks the equipment.” Fade out the speech-bubble icon with the previous message.
2. On “move on to calibration,” fade in a smaller “Next: Calibration” label between the equipment overview and the supporting instruction. Keep it visually secondary so the waiting instruction remains the clearest action.
3. When narration ends, hold the entire screen still while the operator completes the equipment checks and any requested adjustments. Continue only when the operator confirms the checks are finished. Do not show animated progress, a countdown, sensor readings, a calibration target, or a success badge during this hold.
4. When the operator proceeds, fade the equipment illustrations and supporting text out together over approximately 400 milliseconds. Keep the background constant. Change the heading to “Head-tracker calibration” and reveal Part 3's opening content. Begin its narration only when that content is readable.

**Transition — Manual continue:** Once equipment checks and any adjustments are finished, the operator manually continues to Part 3 for head-tracker calibration. This is the second manual hold in Part 2.

**Production notes:** Parts 2, 2.1, and 2.2 are separate spoken chunks. Playback order: announce fitting → manual hold for fitting → comfort message → automatic equipment-check message → manual hold for checks → Part 3 head-tracker calibration. The holding demonstration now follows head calibration as Part 3.1. Audio production is deferred until the writing stage is finished; planned filename stems above are not existing files. Use captions matching the spoken wording when audio is added. Keep comfort instructions available for replay during setup. Do not show equipment readiness or calibration acceptance based on narration ending. If operator controls are not connected, hold at each manual transition and provide continuation only in production preview controls.

## Part 3 — Head-tracker calibration, holding position, and gun-tracker calibration

**Sequence:** Head-tracker calibration → manual confirmation → holding demonstration → manual confirmation → gun-tracker calibration → manual confirmation → interface familiarization. These are separate steps; the participant does not need to hold the replica during head-tracker calibration.

**Visual direction throughout:** Continue the charcoal background, warm-white text, muted blue accents, fixed heading, and reserved caption area. Use short 300–400 millisecond fades for explanatory content. During either calibration capture, keep the reference dot completely stationary and unobstructed. Its position must use the configured calibration reference for that step; the head and gun calibration steps need not share a position unless the confirmed procedure specifies this. Dot position, size, and contrast must be configurable and checked on the actual display. Do not use pulsing, drifting targets, aiming scores, decorative reticles, countdowns, or animated progress. Place instructions and captions away from the dot.

**Spoken:** “First, we'll calibrate the head tracker. Please stand up straight and look straight ahead at the dot on the screen. Keep this position while the operator calibrates the tracker.”

**Audio:** Not generated — waiting until script writing is complete. Planned folder: `participant-tutorial/audio/part-03/`; filename stem: `03-head-tracker-calibration`.

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

**Audio:** Not generated — waiting until script writing is complete. Planned folder: `participant-tutorial/audio/part-03/`; filename stem: `03-01-holding-the-study-replica`.

**Screen / animation:**

1. Fade in the heading “Holding the study replica,” with “Participant tutorial” above it. In the center, show a reference-based illustration of the actual non-firing study replica. During “a replica that cannot fire,” display the supporting label “Non-firing study replica.” Do not name its model or show ammunition, firing, recoil, or weapon-operation controls.
2. On “The operator will show you,” crossfade the central illustration to a large side-view drawing of a person holding the replica with its stock resting against their shoulder. Use a reference photograph of the actual operator-demonstrated position to create this drawing. Frame the upper body and the full replica so the stock-to-shoulder contact is easy to understand. Do not crop out the point being explained.
3. On “stock against your shoulder,” highlight the stock with a steady muted blue outline. Add a thin leader line from the word “Stock” to that part of the replica. Use a second short label, “Against your shoulder,” beside the contact area. Do not animate the replica sliding forcefully into the body. The final illustration should show a settled, comfortable position.
4. On “Please try this position,” fade in the supporting instruction “Try the position with the operator's help.” Keep the illustration and stock highlight visible. The illustration remains an example; the operator demonstrates the position in person and helps accommodate the participant.
5. On “if it feels uncomfortable,” crossfade the supporting line to “Let the operator know if you need an adjustment.” Add the same speech-bubble icon used earlier. Keep the shoulder contact visible and all other graphics still.
6. At the end of narration, hold this screen while the participant tries the position and the operator checks it. Do not show a countdown, automatic completion, or an aiming challenge. Do not infer completion from tracking data or the audio ending.
7. After the operator confirms the participant can hold the replica in the intended position and manually continues, fade out the illustration and labels over approximately 400 milliseconds. Keep the background constant, reveal Part 3.2's gun-tracker calibration heading, and begin its narration once the opening content is readable.

**Transition — Manual continue:** The operator demonstrates the position, lets the participant try it, and resolves any discomfort before continuing to Part 3.2. The head-tracker calibration has already been completed.

**Production notes:** The user confirmed a non-firing study prop replica and a stock-to-shoulder position. Exact hand placement, shoulder choice, stance, and other handling details have not been specified; use the operator's confirmed demonstration as the visual reference rather than inventing a prescribed technique. This chunk teaches the basic study holding position, with aiming at the calibration dot introduced next in Part 3.2. Do not generate audio yet.

### Part 3.2 — Gun-tracker calibration

**Spoken:** “Now we'll calibrate the gun tracker. Hold the replica with the stock against your shoulder, as the operator showed you. Look down the sights and aim at the dot on the screen. Keep this position while the operator calibrates the tracker.”

**Audio:** Not generated — waiting until script writing is complete. Planned folder: `participant-tutorial/audio/part-03/`; filename stem: `03-02-gun-tracker-calibration`.

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

**Audio:** Not generated — waiting until script writing is complete. Planned folder: `participant-tutorial/audio/part-03/`; filename stem: `03-03-calibration-complete`.

**Screen / animation:** After the operator has confirmed both tracker calibrations, display “Calibration complete” with “Next: Study interface” beneath it. Keep the screen still while this short narration plays. Then crossfade into Part 4's opening view. Do not play this chunk on an unsuccessful or incomplete calibration.

**Transition — Automatic:** Once the operator has explicitly ended Part 3.2, this short transition message leads automatically to Part 4. No additional mandatory Continue is needed between the completion message and the interface introduction. Operator pause and replay remain available.

**Production notes:** The participant actions and order above were supplied by the user. Exact capture controls, capture duration, reference-dot coordinates, acceptance criteria, and retry mechanics still need to be matched to the physical calibration procedure. Do not invent these values or treat synthetic development calibration as physical acceptance. Keep preview states clearly identified when real capture is not connected. Audio production remains deferred.

## Part 4 — Interface familiarization

**Spoken:** “Let's look at the screen you'll use during the study. These are the practice targets.”

**Screen / animation:** Show the practice interface and highlight a practice target. Keep other instructional overlays hidden so the target is easy to identify.

### Part 4.1

**Spoken:** “We'll introduce the controls one step at a time, so you can try each action before moving on.”

**Screen / animation:** Present one control prompt, demonstrate its action, and then let the participant try it. Repeat for each required control.

**Production note:** Add a separate spoken-and-screen chunk for each confirmed participant control. Exact aiming, firing, reloading, movement, and status-indicator instructions remain to be drafted for the final equipment and interface. Do not substitute desktop development controls.

## Part 5 — Guided practice

**Spoken:** “Now you'll have time to practice using the interface before the study begins.”

**Screen / animation:** Transition into the separate practice scenario. Keep a clear “Practice” label visible.

### Part 5.1

**Spoken:** “Follow the prompts on the screen, and ask the operator if you need help.”

**Screen / animation:** Guide the participant through the confirmed practice actions. Show feedback for each action when supported by the practice software. Allow replay or operator clarification before continuing.

### Part 5.2

**Spoken:** “Practice is complete. Let the operator know when you're ready to continue.”

**Screen / animation:** After practice actually ends, replace the practice prompts with “Practice complete.” Hold for the operator to confirm readiness.

## Part 6 — ELTS introduction

**Spoken:** “This is the ELTS apparatus. Its lights will be active during some of the study conditions.”

**Screen / animation:** Show a photograph or illustration of the apparatus. Highlight the light locations without activating the hardware.

**Production note:** Review participant-facing descriptions against the approved study procedure. An illustration is not evidence of physical validation.

## Part 7 — Brief ELTS demonstration

**Spoken:** “The operator will now demonstrate the lights briefly.”

**Screen / animation:** Show a demonstration title and point out the apparatus. Hold until the operator initiates the approved demonstration.

### Part 7.1

**Spoken:** “The demonstration is complete. Please let the operator know if you have any questions before we continue.”

**Screen / animation:** Display this message only after the demonstration has ended. Hold while the operator checks readiness.

**Production note:** This section is conditional on an approved, physically validated demonstration procedure. The outline proposes approximately five seconds, with an upper bound of ten seconds; final duration and exposure limits require confirmation. Do not use this draft as authorization to activate the apparatus or hard-code those proposed values.

## Part 8 — Procedure overview

**Spoken:** “The study has four scenarios. You'll complete them in a randomized order.”

**Screen / animation:** Reveal four scenario cards. Animate them into an example order, labeled “Example order,” without revealing condition details that the approved procedure does not permit.

### Part 8.1

**Spoken:** “Each scenario has a time limit.”

**Screen / animation:** Add a timer symbol beside a scenario card. Show a numeric duration only when it matches the confirmed session configuration.

### Part 8.2

**Spoken:** “You'll have a two-minute break between scenarios and another two-minute break after the final scenario.”

**Screen / animation:** Place a break marker after each of the four cards, including the last. Highlight one break marker and show an illustrative “2:00” countdown labeled “Break.”

**Production note:** Two-minute breaks and randomized order come from the outline. Confirm that the approved procedure and actual session configuration match this wording before recording or use.

## Part 9 — Transition to the study

**Spoken:** “The tutorial is complete. Let the operator know when you're ready for the first scenario.”

**Screen / animation:** Show “Ready for the first scenario?” Hold while the operator confirms readiness and prepares the scenario.

### Part 9.1

**Spoken:** “Follow the instruction on the screen to begin.”

**Screen / animation:** Present the actual start prompt only when the scenario is ready to start. Keep the scenario timer stopped until the required start event occurs.

**Production note:** Final start wording depends on the confirmed operator and participant workflow. Replace the general instruction with the exact action once agreed. The tutorial must not imply that finishing a clip automatically starts the study.

## Part 10 — Break prompts

**Spoken:** “This scenario is complete. Your two-minute break begins now.”

**Screen / animation:** When the break actually starts, replace the scenario view with the live break countdown. Play this chunk for breaks between scenarios; use Part 11 after the final scenario.

### Part 10.1

**Spoken:** “Your break is complete. Let the operator know when you're ready for the next scenario.”

**Screen / animation:** When the countdown ends, show “Break complete.” Hold for the confirmed next-scenario workflow, then use the start instruction from Part 9.1 when appropriate.

**Production note:** Align “begins now” with the actual break-start event. A finished countdown must not imply that the next scenario has automatically started.

## Part 11 — Completion and post-study form

**Spoken:** “You've completed all four scenarios. Your final two-minute break begins now.”

**Screen / animation:** Confirm scenario completion and show the final live break countdown when that break starts. Keep the label “Final break” visible.

### Part 11.1

**Spoken:** “Your final break is complete. Please complete the post-study form.”

**Screen / animation:** When the final break ends, display the confirmed form instructions or direct the participant to the form provided by the operator.

### Part 11.2

**Spoken:** “Thank you for taking part.”

**Screen / animation:** Show a simple thank-you screen after the form is complete and the operator confirms the session has finished.

**Production note:** Confirm the form content and delivery method before replacing these general directions. Preserve the outline's requirement to associate NASA-TLX responses and sensor recordings with the correct participant, session, and scenario; questionnaire timing still needs confirmation.
