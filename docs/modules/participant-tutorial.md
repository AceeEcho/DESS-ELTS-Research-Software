# Participant walkthrough

The desktop participant game now includes the visual walkthrough described in
[the working script](../participant-tutorial-script.md). Create a participant
recording, then select **Begin walkthrough** in the administrator
dashboard. The participant screen contains no operator buttons.

## Operator flow

1. Welcome and questions advance after their narration. Confirm readiness,
   equipment fitting and equipment checks separately.
2. Follow seven stationary head-calibration dots: center, look left, look right,
   look up, step left, step right, then return to center. Capture each observed
   pose separately; review the synthetic fixture after the final capture. Redo
   restarts the directions. Confirm the holding demonstration,
   then capture and review the gun step. Each accepted fixture is saved separately.
3. Introduce the real game target and confirm aiming. Firing requires an observed
   participant trigger; reloading requires an actual magazine refill. Neither
   requires a hit. An accepted shot opens “Well done”; an actual reload opens
   “Good job.” Each confirmation finishes before the next instruction.
4. Start practice explicitly. Its entire introduction plays before the clock
   starts, so a ten-second setting gives ten active seconds. Practice uses the same target anatomy and magazine
   rules, with separate unscored events. Pause holds its timer while recording
   continues. Restart preserves completed setup and controls checks.
5. Confirm readiness for the ELTS introduction. The centerline arrow remains
   visible while one **in-memory simulated** lifecycle demonstration runs.
   Completion requires acknowledged stop/disarm. An interruption holds for an
   explicit repeat. Instruction replay never requests another activation.
6. After the overview and readiness cue, the first scenario arms automatically. The participant's actual start
   action starts it; that action is not scored. Subsequent prompts follow the
   real scenario, save checkpoint and break events.
7. Each break starts after saving and its completion cue. The final break leads to the operator-provided
   form. Confirm completion to show the thank-you screen and allow another
   participant. Form confirmation saves a separate receipt associated with the
   participant and recording; it never rewrites closed sensor streams.

Skipped/stopped attempts use neutral exception wording. Four ordinary completions
must represent four distinct conditions before the screen claims all four are
complete. Repeats retain earlier attempts. Aborted or failed sessions interrupt
normal prompts.

## Visuals and settings

The charcoal screens use warm-white text and muted blue accents. Every screen
follows the same reading order for a participant who has never seen the study:

1. A progress tracker (Welcome · Equipment · Calibration · Controls · Practice ·
   ELTS · Overview) shows where they are. Past chapters are filled, the current
   one is ringed, and nothing is ticked. It is hidden during calibration so no
   second dot appears near the reference dots, and during the study itself.
2. A short heading and one large picture with at most one- or two-word labels.
3. A cue badge says what to do now: **Listen** while narration plays, then
   **Your turn**, **Hold still**, **Tell the operator**, **Watch below the
   screen**, **Please wait** or **Rest**. The badge follows the cue clock only,
   so replay returns it to Listen and it never invites an action before the
   instruction ends.
4. One instruction line, plus a status or desktop-rehearsal hint when relevant.
   The badge, instruction and hint share one dock, so they cannot overlap. In
   the practice room the dock is a single opaque panel below the target.

The opening route shows Setup, Practice and Study, with Setup outlined as next.
Questions, readiness, comfort and the post-demonstration check reuse one
"You → Operator" picture whose speech bubble shows a question mark, a tick for
"ready" or an adjustment arrow. Equipment fitting shows the three devices with
one-word labels; equipment review shows them leading to the operator's
clipboard. These drawings do not show simulated sensor-health results.

The holding schematic labels the stock and shoulder directly. The operator
supplies detailed positioning; the drawing does not prescribe a device model or
fitting position. Each head direction shows an outline pictogram above the
clear central field: a head seen from above with a curved turning arrow (or a
side view for look up), or a person stepping sideways. The pictograms contain
no filled dots, and progress is shown as "Step N of 7" text, so the configured
reference dot is the only dot on screen. A static ring around it makes it easy
to find. The gun preparation cue gives way to the same ringed dot.

A 3D practice room uses the real game target for aiming, firing, reloading
and practice. Textured flooring, lane edges, wall panels, rear windows, benches
and overhead lights make its depth readable while keeping the target lane clear.
Firing and reload lessons show a generic replica silhouette with the trigger or
magazine ringed and highlight the actual ammunition display. A "Your ammo"
callout is measured against that display each frame and centred above it. Their
observed-action messages require actual input events. Ordinary practice removes
these teaching cues. Accepted hits in both practice and scenarios produce a
brief spark at the hit intersection and a small visual body recoil. A killed
target falls; practice waits `targetRespawnSeconds` before spawning its replacement.
The visual response does not change hit geometry or scoring. The library scenery
returns before the scenarios.

The three overview lessons keep four numbered scenarios in the same positions:
first the sequence, then static time-limit symbols, then a break after every
scenario, including the fourth. Break examples use the configured duration and
do not count down. Later screens distinguish the start action, recording
checkpoint, actual break timer, next scenario, form handoff and confirmed finish.

Explanatory motion plays once and settles. Route stops and equipment appear in
reading order, review paths draw toward the operator, holding callouts draw
toward the relevant contact points, and the trigger/reload arrows appear once. Pausing freezes these drawings; instruction replay
restarts them without performing an operational action. Reduced motion shows
the complete diagrams immediately. There are no looping equipment checks,
decorative pulses, fake hits or animated calibration-success indicators.

Presentation settings live in
`unity/Assets/ELTS/Operator/Resources/ELTS/ParticipantTutorialSettings.json`.
They include explanation reading times, fades, `diagramSeconds` for finite
explanatory drawings, reduced motion, whether to show
the holding diagram, normalized head/gun dot positions, `edgeDotMarginPx` (the
same distance from the left, right or top edge for the three look dots),
`headStepOffsetX` (step-left and step-right dots placed symmetrically about the
centre dot), dot colour, size and `dotRingDiameter`, arrow geometry, and
synthetic target/demo settings. The 1920 × 1080 canvas
scales proportionally to the desktop window. During calibration the dot is fixed;
replaying the instruction does not move it or accept a capture.

Practice/scenario/break durations come from the actual session settings. The
overview's break example shows that configured duration and stays static. The
script's planned two-minute breaks require **120 seconds** in session settings;
the walkthrough does not silently change an existing setup.

The selected recordings are bundled in Unity Resources. The mapping in
`Resources/ELTS/ParticipantTutorialAudio.json` follows the detailed script's
selected audio links and bundles all 39 recordings. The November 3 revision adds
six directional head cues and separates firing, reloading, and their confirmations.
See [the selected takes and exact prompts](../participant-tutorial-revision-2026-11-03-voice-prompts.md).

The first scenario arms after the tutorial readiness cue. The participant starts
it using the displayed input. After each scenario, the recording checkpoint and
completion cue finish before the break begins. At the end of the break, the next
scenario starts after its announcement and a valid recorded tracking sample.
The last break leads to the form instructions. Skip controls remain available
through their existing operator workflow. The default break is 120 seconds.
Part 10's recordings are reused after scenarios 1–3.

Narration plays once on entering a step. Pause/resume follows the tutorial;
Repeat instruction restarts the current recording. Each firing/reload instruction
and confirmation has its own recording. Leaving a step stops stale narration. Explanations with
recordings advance after the clip duration plus `narrationGapSeconds` (0.25 seconds by default); all participant,
calibration, practice and scenario checks still require their original events.
Manual continues, calibration captures, practice starts and participant scenario
starts wait for both the current recording and diagram to finish. An early
trigger is ignored; a fresh input is required after the cue. A short break holds
at zero until its cue finishes. Explicit operator skip/stop actions can still
interrupt an obsolete transition, but the new scenario's introduction must finish.
Set `narrationEnabled` or `narrationVolume` in the presentation settings to control
playback. Audio integration checks run with `-noaudio` to avoid audible playback.
Neutral transition recordings remain applicable after shortened tests or skipped
breaks. Changing a break duration no longer silences its recording. The next
state always replaces the previous clip, including after replay and pause.

## Boundaries and verification

This implementation remains synthetic development software. The two calibration
reviews exercise the existing known-transform fixture. The seven head cues log
admitted desktop poses and their acquisition references; they do not apply the
separate dashboard's physical-direction solution or replace the startup rig.
The demonstration owns a concrete `MockEltsLink`, with no hardware transport.
Its configurable simulation duration is not an approved exposure setting.
Physical capture controls, replica input mapping, display placement and device
integration still require the confirmed procedure and actual equipment checks.

`ParticipantTutorial` owns progression and revision checks. The station adapter
owns recording and actions; `ParticipantTutorialView` only presents them.
Tutorial/practice events retain `scored=false`, their own interaction IDs and raw
trigger observation references. The existing scored scenario owner is unchanged.

Run `dotnet run --project tools/runtime-tests/TutorialChecks.csproj` for progression
checks. Unity's `ParticipantTutorialTests` exercises calibration, real synthetic
input, practice controls, one-shot demonstration, all four scenarios, breaks and
form receipt association. Set `ELTS_TUTORIAL_CAPTURE_DIR` for rendered screen
captures while running those Play Mode tests; held steps also save a
`<Stage>-action.png` showing the badge after the narration. Use `-noaudio` for visual verification.

The copied-player storage check (`tools/browser-tests/data-player-smoke.py`) now
walks through the same preparation flow. It opens a muted test copy and needs a
click at **Try firing** and **R** at **Try reloading**, for each of its two test
recordings. It does not offer an administrator bypass for those input checks.
