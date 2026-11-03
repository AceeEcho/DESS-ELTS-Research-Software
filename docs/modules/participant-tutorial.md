# Participant walkthrough

The desktop participant game now includes the visual walkthrough described in
[the working script](../participant-tutorial-script.md). Create a participant
recording, then select **Begin tutorial / walkthrough** in the administrator
dashboard. The participant screen contains no operator buttons.

## Operator flow

1. Welcome and questions advance as text-only explanations. Confirm readiness,
   equipment fitting and equipment checks separately.
2. During the head step, wait for the stationary dot, capture the synthetic
   fixture, review it, then accept or redo it. Confirm the holding demonstration,
   then capture and review the gun step. Each accepted fixture is saved separately.
3. Introduce the real game target and confirm aiming. Firing requires an observed
   participant trigger; reloading requires an actual magazine refill. Neither
   requires a hit. Confirm understanding after each action.
4. Start practice explicitly. Practice uses the same target anatomy and magazine
   rules, with separate unscored events. Pause holds its timer while recording
   continues. Restart preserves completed setup and controls checks.
5. Confirm readiness for the ELTS introduction. The centerline arrow remains
   visible while one **in-memory simulated** lifecycle demonstration runs.
   Completion requires acknowledged stop/disarm. An interruption holds for an
   explicit repeat. Instruction replay never requests another activation.
6. After the overview, arm the first scenario. The participant's actual start
   action starts it; that action is not scored. Subsequent prompts follow the
   real scenario, save checkpoint and break events.
7. Start each break after saving. The final break leads to the operator-provided
   form. Confirm completion to show the thank-you screen and allow another
   participant. Form confirmation saves a separate receipt associated with the
   participant and recording; it never rewrites closed sensor streams.

Skipped/stopped attempts use neutral exception wording. Four ordinary completions
must represent four distinct conditions before the screen claims all four are
complete. Repeats retain earlier attempts. Aborted or failed sessions interrupt
normal prompts.

## Visuals and settings

The charcoal screens use warm-white text, muted blue accents, line illustrations
and short fades. A plain 3D teaching stage uses the real game target for aiming,
firing, reloading and practice, without furniture obscuring it. The library
scenery returns before the scenarios. A simple holding diagram explains stock-to-shoulder contact; the
operator supplies detailed positioning. Equipment icons identify categories,
not device models or fitting positions.

Presentation settings live in
`unity/Assets/ELTS/Operator/Resources/ELTS/ParticipantTutorialSettings.json`.
They include explanation reading times, fades, reduced motion, whether to show
the holding diagram, separate normalized head/gun dot positions, dot contrast and
size, arrow geometry, and synthetic target/demo settings. The 1920 × 1080 canvas
scales proportionally to the desktop window. During calibration the dot is fixed;
replaying the instruction does not move it or accept a capture.

Practice/scenario/break durations come from the actual session settings. The
overview's break example shows that configured duration and stays static. The
script's planned two-minute breaks require **120 seconds** in session settings;
the walkthrough does not silently change an existing setup.

The selected recordings are bundled in Unity Resources. The mapping in
`Resources/ELTS/ParticipantTutorialAudio.json` follows the detailed script's
selected audio links and bundles all 30 recordings.

The first scenario arms after the tutorial readiness cue. The participant starts
it using the displayed input. After each scenario, the recording checkpoint and
completion cue finish before the break begins. At the end of the break, the next
scenario starts after its announcement and a valid recorded tracking sample.
The last break leads to the form instructions. Skip controls remain available
through their existing operator workflow. The default break is 120 seconds.
Part 10's recordings are reused after scenarios 1–3.

Narration plays once on entering a step. Pause/resume follows the tutorial;
Repeat instruction restarts the current recording. Part 4.2 plays once across
firing and reloading. Leaving a step stops stale narration. Explanations with
recordings advance after the clip duration plus 0.25 seconds; all participant,
calibration, practice and scenario checks still require their original events.
The operator may deliberately continue a manual step before narration ends.
Set `narrationEnabled` or `narrationVolume` in the presentation settings to control
playback. Audio integration checks run with `-noaudio` to avoid audible playback.

## Boundaries and verification

This implementation remains synthetic development software. The two calibration
reviews exercise the existing known-transform fixture; they are not separate
physical head/gun captures and do not replace the startup rig configuration.
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
captures while running those Play Mode tests. Use `-noaudio` for visual verification.

The copied-player storage check (`tools/browser-tests/data-player-smoke.py`) now
walks through the same preparation flow. It opens a muted test copy and needs a
click at **Try firing** and **R** at **Try reloading**, for each of its two test
recordings. It does not offer an administrator bypass for those input checks.
