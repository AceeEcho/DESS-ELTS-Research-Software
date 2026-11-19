# Calibration Environment

A separate, local workspace for arranging the screen, trackers, person and gun,
editing mounting offsets, and inspecting head-position perspective.
It does not launch Unity, edit ELTS configuration, create participant sessions, or
send apparatus commands.

> **Tracker calibration has moved into ELTS.** To aim with the real trackers,
> follow [live tracking](../docs/operator/live-tracking.md): it needs no tape
> measurements and nothing from this workspace. This workspace is still used
> for the screen size, the station room and participant ID files, and for
> looking at the setup in 3D. Its head-direction guide, position reset and
> aim-at-dots zero below do not feed ELTS's live tracking.

## Start

1. Double-click **START-CALIBRATION.cmd** in the project root. The first time on a PC it
   offers the one-time live-tracking setup below; answer **N** to use manual preview only.
2. Keep the launcher window open. The dashboard opens at
   **http://127.0.0.1:8766**.
3. The dashboard connects to live tracking automatically. For a hardware-free
   preview choose **Manual preview** in Tracking. Included dimensions are examples;
   replace them with your measurements.
4. Use **Save calibration** to save under the participant's ID number, or **Profiles & export** to load a saved participant or
   import/export JSON.
5. Close the launcher with Ctrl+C or run **STOP-CALIBRATION.cmd** in this folder.
   The stop command verifies this app and this project copy before requesting shutdown.

Python 3.10+ and a desktop browser with WebGL 2 are required. The normal project
setup supplies Python. Manual preview needs no Python packages, Node, Unity,
SteamVR, internet connection, or build step. Three.js is bundled locally.

## Set up the room

1. In **Room**, the Samsung QB65R-B defaults to a visible width of 56 1/4 in
   and height of 31 5/8 in (1.42875 m by 0.803275 m), with the **bottom edge
   41 3/8 in (1.050925 m) above the floor**. For a different installation,
   enter its visible dimensions and bottom-edge height.
2. Set the screen's position and pitch/yaw/roll if needed. Its default position
   defines the room's X/Z reference. The bottom-edge measurement is the midpoint
   of the lower edge; screen tilt rotates the centre around that point.
3. Set room dimensions and display aids. Drag the overview to orbit, right-drag
   to pan, and scroll to zoom. Room/front/side/top buttons reset the camera angle.
   Focus the 3D view and use arrow keys to orbit or +/− to zoom.

The base station's position is not entered anywhere. SteamVR locates it itself,
and the head-direction guide and aim zero record how the tracking space lines up
with the screen. Place the station high with a clear view of both trackers,
mount it firmly, and do not move it after calibrating; if it moves, run the
head-direction guide and the aim zero again. Saved profiles still carry an
unused `baseStations` entry so older files load unchanged.

## Head and weapon offsets

**Gun trigger:** the indicator immediately above the 3D view shows pressed,
released, or unavailable, plus observed press/release counts. In live mode it
uses the tracker selected under **Weapon**. In SteamVR, that tracker needs
**Held in hand → Left Hand or Right Hand** for button input. Unavailable or stale
input is never displayed as released. See [main-app trigger setup](../docs/developer/modules/vive-trigger.md).

Each tracked object has independent three-axis position and three-axis rotation
offsets. Profiles store distances in **metres** and angles in **degrees**:

All offsets use the object's own axes: +X right, +Y up, +Z back toward the
participant (−Z is forward).

| Where | Setting | Measure |
| --- | --- | --- |
| Head | Where the tracker sits on the head | From the centre of the head to the centre of the tracker; plus the tracker's tilt on the head |
| Head | Tracker centre → viewing eye | Directly from the tracker centre to the selected eye or the midpoint between the eyes, in feet and inches with a direction |
| Weapon | 2 · Where the tracker sits on the gun | From the **muzzle tip** (where the bullet leaves) to the centre of the tracker, in feet and inches with a direction; plus the tracker's tilt on the gun. This is the measurement ELTS uses. |
| Weapon | 3 · Barrel aim correction | A small rotation of the shot line, only if shots land consistently off the barrel's aim |
| Weapon | Gun drawing (preview only) | Drawn length and the muzzle's position on the drawing. Moving the drawn muzzle keeps the tracker-to-muzzle measurement unchanged. |
| Head | Participant height / body radius | Body preview only. Enter standing height in feet and inches. |

Profiles store distances in **metres** and angles in **degrees**. The gun is still
stored relative to its drawing centre (`weapon.mount.position` and
`weapon.muzzle`); the dashboard shows and edits their difference, so saved
profiles and ELTS are unaffected by the change in wording.

With the headband fitted, measure directly from the tracker centre to the viewing
eye (or eye midpoint) under **Head → Viewing eye**. Directions follow the head,
not the puck: +X right, +Y up, +Z back. For a rear-mounted tracker, the eye is in
**Front**. The head-centre mount translation locates the schematic head drawing;
editing it preserves the direct tracker-to-eye measurement. Profiles retain the
existing fields: the direct vector is `head.eye - head.mount.position`, expressed
in head axes. Existing profiles therefore keep the same rendered eye position.
Standing height sizes the body preview and cannot determine the tracker-to-eye
distance or the headband's mounting angle.

The yellow tracker models and local XYZ axes help check mounting relationships.
The teal ray shows head direction; the amber ray shows the configured bore.
The monitor reports object position, pitch/yaw/roll, and the bore's intersection
with the physical screen. Intersection X/Y are relative to screen centre.

**Manual preview:** edit the object's test pose under Head or Weapon. The object
stays at that pose while the tracker is drawn at its configured mounting offset.

**Live input:** a raw tracker pose drives the drawing. The object pose is recovered
as `roomTracker × inverse(trackerOnObject)`. Changing the mounting offset therefore
changes the recovered object pose while retaining the observed tracker pose.

## Connect VIVE / SteamVR trackers

1. Start **START-CALIBRATION.cmd** on each new Windows x64 PC and answer **Y** when
   it offers to set up live tracking (it asks whenever the tracking tools are not
   ready). Setup installs missing Python, creates this folder's `.venv`, installs the
   pinned OpenVR reader, and installs Steam if missing. It opens the SteamVR
   installer if required; finish sign-in and the download, then press Enter in
   the setup window. Close SteamVR when asked so setup can apply its settings.
   Internet access is required for missing packages. Unity and Git are not needed.
2. Setup registers the runtime with OpenVR and enables the null headset driver
   alongside real tracking. It backs up existing OpenVR path registration before
   changing it and backs up `steamvr.vrsettings` before merging only these overrides:
   `steamvr.requireHmd=false`, `steamvr.forcedDriver="null"`,
   `steamvr.activateMultipleDrivers=true`, and `driver_null.enable=true`.
   Runtime default files and existing pairing/room settings are preserved.
   Start SteamVR, power the base station, connect the dongles, and pair the trackers.
   Restart the calibration launcher afterward so it uses the installed environment.
3. In **Tracking**, select **Live SteamVR trackers**. The device list reports
   serial numbers, device type and pose validity. Connection failures include
   the runtime error; use **Connect / retry SteamVR** after correcting it.
4. Connected trackers automatically fill empty **Head**, then **Weapon** bindings.
   With one tracker only Head is assigned. Existing bindings are preserved;
   you can select a different serial under **Head** or **Weapon**. The
   same tracker cannot be assigned to both. Bindings persist by serial, not by
   a device index that can change between launches.
   In the Head pose card, choose whether distance and height are measured from
   the **Viewing eye / eye midpoint** or **Head centre**. Centre that point in
   front of the TV, enter its perpendicular distance from the display and its
   height above the floor, then click **Reset position**. New drafts use the eye;
   older browser drafts retain the head-centre reference. Reset translates the
   shared registration and clears forward references. It preserves room rotation,
   head rotation, mounting offsets and raw poses, and requires a fresh live pose.
   Nodding or rolling during reset cannot tilt the room. The **Eye → screen**
   readout shows the calculated perpendicular distance to compare with the
   physical measurement; it is not an independent measurement.
5. Configure **Tracking origin → room**: the position and rotation of SteamVR's
   standing-origin frame in this room. Check against known measured locations.
   Moving a base-station drawing does not perform this registration.
6. Enter measured mounting and eye/muzzle offsets. With the physical head or gun
   held forward, parallel to the screen normal and without roll, **Align … forward
   to screen** can set the three mounting angles from the current valid sample.
   This changes mount rotation only; translation still requires measurement.
7. Hold a known forward direction and click **Set looking forward** / **Set weapon
   forward**. The monitor now reports rotation relative to that captured reference.
   This readout zero does **not** rotate the geometry or change the barrel aim correction.
8. Save your profile. Recheck registration, offsets and forward references after
   moving equipment, changing SteamVR room setup, or restarting/recentering tracking.

Mount alignment and forward capture use one current sample. They are conveniences
for setup, not a multi-sample calibration solver or physical acceptance test.
Forward references are specific to input source and tracker serial; changes to
mounts, bindings, screen geometry or room registration clear dependent references.

### Guided head directions

Use **Calibrate head directions…** in the Head pose card with a valid live tracker.
Keep the tracker in its final head mounting throughout the guide:

1. Face forward at your centre position.
2. Look left, then right, keeping your head upright.
3. Look up from the forward direction.
4. Move left, then right of the original centre while facing the same forward
   direction (do not turn to look at the screen centre).
5. Return to centre and face forward.
6. Review the captured movements and the selected point's screen distance in feet
   and inches, then **Apply directions**. The guide uses the eye/head reference
   and floor height entered in the Head pose card when the guide was opened.

Each capture gives a three-second preparation countdown, then collects about one
second of distinct live samples. Hold still during capture. Lost/stale tracking,
repeated frames, or excessive movement require a retry. Use **Back** to recapture
an earlier step; later samples are then discarded. Cancel leaves settings intact.

Sideways movement determines room yaw using SteamVR's standing-frame up axis.
Screen pitch or roll does not redefine room up. A tracking-axis tilt warning
indicates that the direction guide should be repeated; position reset deliberately
preserves existing alignment, including an old incorrect tilt.
The forward pose determines the head tracker mounting rotation; left/right/up
turns check that the resulting axes agree with the requested gestures. The final
centre pose places the selected measurement point at the entered height and distance.
Apply changes the shared room registration and head mounting rotation, clears
dependent forward references, and updates this browser's station draft. It preserves measured
mount translations, eye/muzzle offsets, raw tracking and weapon mounting settings.
The changed registration also moves any live weapon in the room. Save the profile
to retain the alignment in a named file.

This is guided preview alignment, not measured tracking accuracy or physical
acceptance. Distance, screen geometry and eye/mount translations still require
measurement. It assumes SteamVR's standing-frame up axis is correct. Editable
capture and gesture thresholds are in `web/head-calibration.js` (`DIRECTION_LIMITS`).

### Zero the gun by aiming at dots

Use **Weapon → 3 · Zero the aim → Aim at dots to zero the gun…** with Live input,
both trackers selected, the screen geometry entered, the head tracker's mounting and
viewing-eye offsets entered, and the head-direction guide already done (it sets the
room's up direction). Enter **2 · Where the tracker sits on the gun** first.

1. The screen window opens. Move it to the TV and press **F** for fullscreen; the
   dots are drawn at their real positions on the configured screen.
2. Wear the head tracker. Stand where participants stand, hold the gun the way you
   normally shoot (stock in the shoulder), and aim through the sights at the centre
   of each of the nine dots.
3. Capture each dot by pulling the trigger, pressing **Space**, or holding steady
   for 1.5 s (the ring fills). A trigger capture uses the pose from just before
   the pull. **Backspace** redoes the previous dot, **R** restarts, **Esc** cancels.
   A gun left resting on a table does not auto-capture.
4. The dashboard applies the result and shows it under **3 · Zero the aim**.
   **Undo last aim zero** restores the previous values. Save the calibration to
   keep it.

**What it solves.** When you aim, your eye, the rear and front sights and the dot
are on one line. With the head tracker worn, each capture gives that sight line
from the tracked eye to the dot, expressed in the gun tracker's own axes, so it
does not matter how the gun is held: canted, angled in across the body, or with
the cheek slightly forward or back. The result sets:

- **Tracker tilt on the gun** so the gun's forward axis is the sight line and its
  up is SteamVR's gravity up, averaged over the dots (a slight cant only tilts
  the drawing).
- **Barrel aim correction** so the shot, which starts at the muzzle, crosses the
  sight line at the target distance — like zeroing a rifle at range. The distance
  is the screen distance plus `zeroDepthBehindScreenM` (7.5 m, matching
  `targetDistanceM` in `config/development/scenario.json`). Elsewhere the shot is
  never further from the sight line than the sight height.

It reports the sight height it found (many AR-style rifles are about 6–7 cm; a very
different value usually means the muzzle or eye measurement needs checking), the
average and worst disagreement between dots, and warnings for a dot more than 2°
from the rest, an eye that moves more than 3 cm across the sight line, or a room
registration tilted more than 2° from SteamVR's up.

Without the head tracker the shot line is aimed from the muzzle through each dot
instead. That zeroes it at the screen, so targets behind the screen land high by
about the sight height × (target distance ÷ screen distance − 1) — roughly 30 cm at
the default distances — and the result says so. Editable limits are in
`web/aim-calibration.js` (`AIM_LIMITS`). This is a software alignment from your own
aiming, not a measured accuracy check.

**Tracker drawing.** The yellow tracker model uses SteamVR's raw tracker axes:
the puck lies in its local X–Y plane with the domed face toward −Z and the screw
mount toward +Z. This was checked on 2026-11-16 by pointing the gun (puck face
toward the muzzle) straight up and confirming the tracker's −Z pointed up.

### Tracking setup troubleshooting

- **Check without installing or editing settings:** from the project root run
  `powershell -ExecutionPolicy Bypass -File scripts\setup-tracking.ps1 -Check`.
- **Run setup again on purpose:** `START-CALIBRATION.cmd -SetupTracking`.
- **Custom installations:** run `scripts\setup-tracking.ps1 -SteamPath "D:\Steam"` the same way
  or add `-SteamVRPath "E:\SteamLibrary\steamapps\common\SteamVR"`.
  Normal Steam library folders and OpenVR's registered paths are detected automatically.
- **Setup interrupted:** start START-CALIBRATION.cmd again and answer **Y**. Missing dependencies are installed,
  unrelated settings are preserved, and correct settings are not rewritten.
- **Log:** `diagnostics/tracking-setup.log` in the project root records the latest
  installation attempt. The read-only check does not replace this log.
- **Existing VR headset configuration:** this setup intentionally selects the null
  headset for tracker-only work. Original settings are saved beside
  `steamvr.vrsettings` with a `.before-elts-tracking-...bak` suffix. To restore them,
  close SteamVR and copy the desired backup over `steamvr.vrsettings`. If setup
  changed OpenVR path registration, its original file has a matching backup too.
- **New PC/room:** software setup does not pair hardware or transfer measured room
  calibration. Pair trackers in SteamVR and check/recreate your profile on that PC.

The reader uses OpenVR **standing-origin** poses with **zero added prediction**.
It respects connected/valid/Running_OK status, checks rigid matrices, timestamps
each acquisition with a monotonic clock, and never substitutes a manual pose for
an invalid live pose. Invalid/unassigned/stale models and rays disappear; capture
is disabled. The freshness threshold is adjustable from 100 to 500 ms. Display
updates are approximately 20 Hz; acquisition is approximately 60 Hz, not measured
hardware timing. SteamVR/driver filtering is outside this app's control.

## Perspective testing

The lower preview shows a reference room behind the physical screen, with a
solid floor, walls/baseboards, an open doorway, and contact shadows under the
floor-standing objects. Floor-depth posts mark 1, 2, and 4 metres (also labeled
in feet), and the screen-plane reticle provides a zero-depth reference. Room
width, depth, and height size this scene. The target has a floor stand.
This scene uses one-to-one eye movement so its spatial cues can be evaluated
without a movement-gain adjustment. It uses an
off-axis frustum derived from the measured screen rectangle and configured eye
position. The virtual floor uses **Room width** and **Room depth**, extends
behind the screen, and stays at room Y=0. Floor-standing objects rest on that
same plane. Screen bottom height and visible dimensions determine where the
physical display cuts through this virtual room. Screen tilt does not tilt the floor.
The reset distance, floor height and selected measurement reference are retained
in the draft. Older drafts keep their head-centre reference; drafts with
no saved height initially show screen-centre height, so enter a measured value.
Move the manual head position or a valid live head tracker to inspect
parallax. Head direction is visible in the overview; it does not rotate the TV.

Choose **Open screen**, move that window onto the TV, and choose **Fullscreen**.
The window follows its originating dashboard's settings and input source. If the
dashboard closes, it hides the view instead of retaining an old pose. The
configured screen corners map to the window corners at any window size. For
physical alignment, use fullscreen on the configured display and check for
browser or OS overscan and scaling.
The fullscreen toolbar appears on pointer hover at the bottom left; Escape exits.

The near clipping plane moves closer when needed to stay before the display.
This changes clipping only; the measured eye position still determines perspective.
The preview is unavailable if the eye is on/behind the screen or the head pose
is invalid. Close viewing also magnifies any eye-position calibration error;
use the perpendicular eye-distance readout to check its inputs. The amber dot marks the bore/screen-plane intersection;
it is not a scored shot or a simulated physical projectile.

## Participant calibrations and ELTS

- **Save calibration** opens a six-digit picker for IDs from 0 to 999999.
  Choose each digit or click **Use next unused ID**. Leading zeros normalize on save. It writes
  `profiles/participant-<ID>.json` atomically. Saving over an existing ID asks
  for confirmation. An unreadable file reserves its ID and appears as **Unreadable**.
  Select another ID to save. Profiles are ignored by Git.
- When an operator starts that participant ID in the ELTS dashboard, ELTS reads
  this file and applies the participant's tracker-local
  eye, muzzle and bore settings. No ELTS restart is needed after saving. A saved
  calibration is optional: ELTS starts any numeric ID, and uses the station
  settings when none is saved or the file cannot be read. The recording notes the
  file name and SHA-256 when one applied, and the settings used for that visit.
- The station stores each browser's unsaved draft under `profiles/`. The browser
  keeps its opaque draft ID. Refresh restores the draft and reconnects live input;
  choose Manual preview explicitly for hardware-free use.
- Import validates structure, dimensions, finite values, reference quaternions,
  distinct tracker bindings and the coordinate-system identifier before applying.
- Screen geometry is shared room setup. On the next **START-ELTS.cmd**, the
  selected station room supplies screen settings and the equipment binding to the
  synthetic development build. Saving another participant with the same screen
  does not trigger a rebuild. If a participant's saved screen differs from the
  running build, ELTS keeps the build's screen and says so in the dashboard and
  recording. Use **Use these screen settings for the station** to change the room,
  then restart ELTS. The first participant save preserves the existing legacy room or
  adopts the displayed room on a new station. Later participant saves keep that room.
- **Remove all participant data…** in ELTS Settings erases the station's drafts
  and invalidates open calibration tabs. An older browser-local draft is discarded
  when that browser next opens after a reset. Exported JSON saved outside the
  station stays at its chosen destination. Live tracking registration, device bindings, forward
  references and preview settings are dashboard-only. ELTS retains synthetic
  inputs and development calibration status.
- If startup reports a room settings error, explicitly save the station room from
  the calibration workspace and restart ELTS.
  Older name-based profiles in `profiles/` are still read for screen geometry;
  import one and save it under a participant ID to use it for a participant.
- This app uses **right-handed X-right, Y-up, Z-toward-viewer** coordinates, with
  **−Z forward**. Angles are pitch X, yaw Y, roll Z in **YXZ** Euler order.
  Positive yaw turns left; positive pitch looks up. Euler readouts become
  ambiguous near ±90° pitch. Startup reflects Z and converts object-space
  offsets through the saved mounts before staging the Unity configuration.

## Troubleshooting and checks

- **Python missing:** start START-CALIBRATION.cmd and answer **Y** (setup installs it), or install Python 3.10+ with its launcher.
- **Port in use:** use `START-CALIBRATION.cmd -Port 8767`; stop it with
  `STOP-CALIBRATION.cmd -Port 8767`. A running copy in this folder is reused.
- **Blank/paused 3D view:** enable browser graphics acceleration, refresh, or try
  another current desktop browser. Software-rendered test captures do not establish
  graphics performance on the experiment machine.
- **Live unavailable:** confirm the optional setup completed, restart this app,
  check SteamVR, power/pairing, line of sight and runtime-reported validity. A base
  station that does not expose a valid pose can still be entered manually.
- **Orientation wrong:** confirm mount direction is tracker-on-object, local
  −Z is forward, and room registration has been applied exactly once.

Developer checks (from this folder):

```powershell
npm test
py -3 -m unittest discover -s tests -p 'test_*.py'
# Browser checks require Playwright, its full Chromium browser, and a local server.
# Set CALIBRATION_BASE_URL for an isolated test server (defaults to port 8766).
node tests/browser-check.mjs
node tests/head-calibration-browser.mjs
node tests/head-reset-browser.mjs
node tests/perspective-audit-browser.mjs
node tests/trigger-indicator-browser.mjs
```

Checks cover transform inversion, coordinate axes, tilted/off-centre projection,
ray misses, reference identity, stale/invalid data, profile validation/persistence,
and desktop interactions. They use synthetic fixtures. No mobile verification is
performed. Actual tracker accuracy, mounting, calibration quality, runtime latency,
screen alignment, safety and study readiness remain unverified without equipment.

See [THIRD-PARTY.md](THIRD-PARTY.md) for bundled library and API references.
