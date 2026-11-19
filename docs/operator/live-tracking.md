# Live tracking: setup and calibration

How to aim with the real gun and head trackers in ELTS. Everything happens in the
participant window; the separate calibration workspace is not needed for this.

> [!IMPORTANT]
> This is a development build. The numbers ELTS reports during calibration say how
> well your own aimed captures agree with each other. They are not a measurement
> of physical accuracy, and nothing here approves the rig for a study. See the
> [safety boundaries](../safety/README.md).

## How it works, in one paragraph

You aim through the sights at dots drawn on the screen and squeeze the trigger.
From those captures ELTS works out two facts about the station (where the screen
is in SteamVR's tracking space, and where the sight line runs on the gun tracker)
and one fact about each shooter (where their aiming eye is relative to the head
tracker). A shot is the sight line as the shooter sees it: from the eye through
the point on the glass under the sights. Nothing is measured with a tape, the
base station's position is never entered, and no printed parts are needed.

## Before you start

1. **Base station.** Mount it rigidly, high, in front of the shooter (above the
   screen), looking back at them. Both trackers' faces should point toward it.
   A base station beside or behind the shooter sees the trackers edge-on and
   tracks badly. Do not touch it after calibrating.
2. **SteamVR.** Start SteamVR. Wait until both trackers show as tracked, then move
   each tracker around in view of the base station for about a minute. SteamVR
   settles its own estimates during that minute; calibrating earlier is wasted.
   If tracking felt wrong last time, run the
   [SteamVR health report](../../tools/diagnostics/README.md) first: it reads
   SteamVR's own log and flags a tracker whose learned calibration has gone bad.
3. **Gun tracker.** In SteamVR **Manage Trackers** it must be **Held in hand**
   (left or right), as for the [trigger](../developer/modules/vive-trigger.md).
   ELTS takes the hand-held tracker as the gun and the other connected tracker
   as the head. Nothing has to be typed in. (`weaponTriggerSerial` in
   `config/local.json` is optional and only needed to force a particular
   tracker; that file is not copied between machines.)
4. **Screen.** Start ELTS with **START-ELTS.cmd**, move the participant window to
   the TV and press **F11**. The dots are placed as fractions of the window, so
   the window must fill the screen.

## 1 · Calibrate the station (about three minutes, 24 trigger pulls)

Do this once, and again whenever the base station, the screen or the gun tracker's
mount has moved. One person does it, holding the gun the way shooters will.

Press **F6** on the participant window. For each dot: look down the sights, settle
on the dot, squeeze gently. ELTS uses the pose from just before the squeeze.

| Spot | Where to stand | Dots |
| --- | --- | --- |
| 1 | On the centre floor mark, square to the screen | 9 |
| 2 | On the far floor mark | 5 |
| 3 | About 4 ft from the screen, one big step left of centre | 5 |
| 4 | About 4 ft from the screen, one big step right of centre | 5 |

Spots 1 and 2 fix the aim. Spots 3 and 4 look at the screen from the sides, which
is what pins down the screen's tilt and so the head position. Exact standing
positions do not matter; different positions do.

When the last dot is captured ELTS shows the fit, for example
*typical miss 0.20°, worst 0.45° over 24 dots*. Read it, then squeeze the trigger
or press **Enter** to save. **Backspace** goes back one dot; **Esc** discards.

- A capture that disagrees wildly with the rest (a slipped pull) is set aside and
  reported.
- If the typical miss is above about half a degree, ELTS says the fit is loose.
  Repeat it; a loose fit usually means the sights were not on a dot, or the gun
  tracker was not tracked well.
- The fit is saved in `Calibration Environment/station-tracking.json`. It is tied
  to this gun tracker and this screen size.

From now on the trackers drive the aim. A red ring with a centre point shows
where the sights point (**F9** hides or shows it). Aim around the screen: the
ring should sit under the sights everywhere. It is shown before a participant
starts, during the walkthrough's target and practice scenes, and during tests;
hide it when shooters must rely on the sights alone.

## 2 · Each participant (nothing to calibrate)

Start the participant under **New participant** by typing any whole-number ID;
nothing has to be saved in the calibration workspace first. Then click
**Begin walkthrough**.

The station was calibrated in step 1, so the walkthrough does not calibrate
again. Its head and gun calibration steps are left out: after the equipment
check it shows the holding position and goes straight on to the practice target,
firing, reloading and practice.

- Every participant aims on the saved station fit. One shooter's aim zero never
  carries over to the next.
- **Optional aim zero.** If one shooter's marker sits a steady distance off their
  sights, press **F7** between tests: five dots, about twenty seconds. It re-aims
  the sight line and places the eye for that shooter only.
- **Skip walkthrough** on the dashboard goes straight to the first test, before
  the walkthrough or from any step of it.
- A trigger pull while a tracker is not tracked is ignored and reported on the
  dashboard. It does not stop the session.

> [!NOTE]
> The eye position comes from the person who calibrated the station. A head
> tracker worn differently shifts the picture's perspective slightly; the point
> on the glass under the sights does not depend on it. Run **F7** for a shooter
> when that matters.

## 3 · During a session

ELTS follows SteamVR's own log. When SteamVR re-sets its tracking frame, a tracker
is reset, or the base station reports being bumped, ELTS writes it to the
recording and, between tests, shows a line at the top of the participant window
asking for a **quick aim check**: press **F8**, three dots, ten seconds. The check
re-aims the sight line and clears the message.

If a tracker is not tracked, the line names it. Shots are never scored on an
untracked or stale pose.

The dashboard has the same actions under **Settings → Live tracking**
(**Calibrate station**, **Quick aim check**, **Aim marker on/off**,
**Trackers / mouse**), with a status line for SteamVR, both trackers and the
saved station fit. Use those when the keyboard focus is on the dashboard.

## Keys (participant window)

| Key | Action |
| --- | --- |
| **F6** | Calibrate the station (press again to cancel) |
| **F7** | Aim zero for the current shooter, five dots |
| **F8** | Quick aim check, three dots |
| **F9** | Show or hide the aim marker |
| **F10** | Switch between trackers and mouse |
| **Space** | Capture the current dot without the trigger |
| **Backspace** | Go back one dot |
| **Enter** | Save a station fit |
| **Esc** | Cancel the current step |
| **F11** | Fullscreen |

## Adjustable values

Create `Calibration Environment/live-tracking.json` to change defaults; anything
left out keeps its default. The names and meanings are in `LiveTrackingSettings`
(`unity/Assets/ELTS/Operator/LiveTrackerReader.cs`). Useful ones:

| Setting | Default | Meaning |
| --- | --- | --- |
| `headSerial` | empty | Head tracker serial; empty takes the other connected tracker |
| `showAimMarker` | `true` | Draw the aim marker at start-up |
| `eyeTowardTargetM` | `0` | Moves the eye along the sight line; use a positive value (metres) for a head tracker worn behind the eye |
| `dotDiameterM` | `0.025` | Dot size on the glass |
| `looseFitDegrees` | `0.5` | Typical miss above which a fit is called loose |

## What is recorded

- The head pose in each sample is the tracked head. The weapon pose is the
  sight-line ray (from the eye through the point under the sights), the same form
  the mouse rehearsal records.
- `LiveCalibrationApplied` events hold every calibration in force, in full, from
  the start of the recording and at each zero or check.
- `LiveTrackerPose` events hold the raw tracker poses: twenty times a second
  while a test, practice or the walkthrough's target scene is running, and at
  every trigger pull.
- `LiveTrackingHealth` events hold what SteamVR's log reported.
- `DevelopmentInputSource` says whether live tracking was driving the session.

## If something looks wrong

| What you see | What to do |
| --- | --- |
| The dashboard says **Start SteamVR** | Start SteamVR on this machine and wait a few seconds |
| The gun shows **not found** | In SteamVR **Manage Trackers** set the gun tracker to **Held in hand** |
| *The gun tracker was not tracked* on a capture | Turn the tracker's face toward the base station; check nothing blocks it |
| *The gun moved during the squeeze* | Settle on the dot first, then squeeze gently; or press **Space** with a second person |
| Trigger pulls do nothing during a step | The trigger is unavailable: check **Held in hand** in SteamVR; use **Space** meanwhile |
| The marker sits a steady distance off the sights | Run a quick check (**F8**). If it comes back, recalibrate the station (**F6**) |
| The marker jumps or drifts while the gun is still | SteamVR is losing the tracker. Fix the base station view before calibrating again |
| The picture goes dark for a moment | The head tracker was not tracked; the banner names it |
| The window is not fullscreen | Press **F11**, then recalibrate: the dots were not where the fit assumed |
