# Calibration Environment

A separate, local workspace for arranging the screen, trackers, person, gun and
base stations, editing mounting offsets, and inspecting head-position perspective.
It does not launch Unity, edit ELTS configuration, create participant sessions, or
send apparatus commands.

## Start

1. Double-click **START-CALIBRATION.cmd** in the project root. The first time on a PC it
   offers the one-time live-tracking setup below; answer **N** to use manual preview only.
2. Keep the launcher window open. The dashboard opens at
   **http://127.0.0.1:8766**.
3. The dashboard connects to live tracking automatically. For a hardware-free
   preview choose **Manual preview** in Tracking. Included dimensions are examples;
   replace them with your measurements.
4. Use **Profiles & export** to save a named profile or import/export JSON.
5. Close the launcher with Ctrl+C or run **STOP-CALIBRATION.cmd** in this folder.
   The stop command verifies this app and this project copy before requesting shutdown.

Python 3.10+ and a desktop browser with WebGL 2 are required. The normal project
setup supplies Python. Manual preview needs no Python packages, Node, Unity,
SteamVR, internet connection, or build step. Three.js is bundled locally.

## Set up the room

1. In **Room**, the Samsung QB65R-B defaults to a visible width of 56 1/4 in
   and height of 31 5/8 in (1.42875 m by 0.803275 m). Measure and enter the
   **bottom edge height from the floor** separately. For a different display,
   enter its visible dimensions or use the diagonal/aspect-ratio calculator.
2. Set the screen's position and pitch/yaw/roll if needed. Its default position
   defines the room's X/Z reference. The bottom-edge measurement is the midpoint
   of the lower edge; screen tilt rotates the centre around that point.
3. Enter each base station's position and orientation. **Y is its centre height
   above the floor.** A second station can be enabled. When SteamVR supplies a
   valid station pose, **Copy current station pose** copies it through the current
   tracking-origin registration.
4. Set room dimensions and display aids. Drag the overview to orbit, right-drag
   to pan, and scroll to zoom. Room/front/side/top buttons reset the camera angle.
   Focus the 3D view and use arrow keys to orbit or +/− to zoom.

Base-station placement here changes the drawing and saved profile. It does not
change SteamVR's tracking solution or stand in for tracking-origin registration.

## Head and weapon offsets

Each tracked object has independent three-axis position and three-axis rotation
offsets, expressed in **metres and degrees**:

| Setting | Meaning |
| --- | --- |
| Head object origin | Centre of the schematic head |
| Weapon object origin | Centre of the rifle receiver |
| Tracker centre on object | Vector from the object origin to the tracker centre, in object-local axes |
| Tracker orientation on object | Orientation of the tracker axes relative to the object axes |
| Viewing eye | Vector from head centre to the selected eye or midpoint between eyes |
| Muzzle | Vector from receiver centre to the muzzle |
| Bore correction | Rotation applied to the weapon's local forward direction |
| Model length / person size | Visual dimensions; they do not rescale measured offsets |

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
   For a quick preview, face the screen and click **Reset head in front of screen**
   in the Head pose card. This maps the current head pose to screen-centre height,
   facing the screen, at the adjustable distance (2 m initially). It changes the
   shared tracking-origin registration and clears forward references; mounting
   offsets and raw poses are preserved. It requires a fresh valid live pose.
   This convenient preview reset is not a measured physical calibration.
5. Configure **Tracking origin → room**: the position and rotation of SteamVR's
   standing-origin frame in this room. Check against known measured locations.
   Moving a base-station drawing does not perform this registration.
6. Enter measured mounting and eye/muzzle offsets. With the physical head or gun
   held forward, parallel to the screen normal and without roll, **Align … forward
   to screen** can set the three mounting angles from the current valid sample.
   This changes mount rotation only; translation still requires measurement.
7. Hold a known forward direction and click **Set looking forward** / **Set weapon
   forward**. The monitor now reports rotation relative to that captured reference.
   This readout zero does **not** rotate the geometry or change the bore correction.
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
6. Review the captured movements, choose the preview distance, and **Apply directions**.

Each capture gives a three-second preparation countdown, then collects about one
second of distinct live samples. Hold still during capture. Lost/stale tracking,
repeated frames, or excessive movement require a retry. Use **Back** to recapture
an earlier step; later samples are then discarded. Cancel leaves settings intact.

Sideways movement determines room yaw using SteamVR's standing-frame up axis.
The forward pose determines the head tracker mounting rotation; left/right/up
turns check that the resulting axes agree with the requested gestures. The final
centre pose is placed at screen-centre height and the chosen preview distance.
Apply changes the shared room registration and head mounting rotation, clears
dependent forward references, and updates the browser draft. It preserves measured
mount translations, eye/muzzle offsets, raw tracking and weapon mounting settings.
The changed registration also moves any live weapon in the room. Save the profile
to retain the alignment in a named file.

This is guided preview alignment, not measured tracking accuracy or physical
acceptance. Distance, screen geometry and eye/mount translations still require
measurement. It assumes SteamVR's standing-frame up axis is correct. Editable
capture and gesture thresholds are in `web/head-calibration.js` (`DIRECTION_LIMITS`).

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

The lower preview shows a simple 3D scene behind the physical screen. It uses an
off-axis frustum derived from the measured screen rectangle and configured eye
position. Move the manual head position or a valid live head tracker to inspect
parallax. Head direction is visible in the overview; it does not rotate the TV.

Choose **Open screen**, move that window onto the TV, and choose **Fullscreen**.
The window follows its originating dashboard's settings and input source. If the
dashboard closes, it hides the view instead of retaining an old pose. Match the
configured aspect ratio to the actual screen; a mismatch is letterboxed. Check
for browser/OS overscan, scaling or borders before interpreting physical alignment.
The fullscreen toolbar appears on pointer hover at the bottom left; Escape exits.

The preview is unavailable if the eye is behind/too close to the screen or the
head pose is invalid. The amber dot marks the bore/screen-plane intersection;
it is not a scored shot or a simulated physical projectile.

## Profiles and ELTS startup

- **Save profile** writes an atomic JSON file in this folder's `profiles/`. Saving
  over an existing name asks for confirmation. Profiles are ignored by Git.
- A separate browser draft recovers unsaved settings after refresh. The header
  distinguishes a draft from a saved profile. Refresh reconnects live input;
  choose Manual preview explicitly for hardware-free use.
- Import validates structure, dimensions, finite values, reference quaternions,
  distinct tracker bindings and the coordinate-system identifier before applying.
- On the next **START-ELTS.cmd**, the most recently modified saved profile
  supplies screen geometry and tracker-local eye, muzzle and bore settings to
  the synthetic development build. Save the profile you want to use, then
  restart ELTS. With no saved profiles, the launcher uses the default synthetic rig.
- Browser drafts and exported JSON outside `profiles/` stay local to their
  respective workflows. Live tracking registration, device bindings, forward
  references and preview settings are dashboard-only. ELTS retains synthetic
  inputs and development calibration status.
- If startup reports a profile error, correct and save that profile, or move it
  out of `profiles/` to let the launcher select the next most recent save.
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
node --test tests/geometry.test.mjs
node --test tests/head-calibration.test.mjs
py -3 -m unittest discover -s tests -p 'test_*.py'
# Optional desktop browser checks require Playwright and the running local server.
node tests/browser-check.mjs
node tests/head-calibration-browser.mjs
```

Checks cover transform inversion, coordinate axes, tilted/off-centre projection,
ray misses, reference identity, stale/invalid data, profile validation/persistence,
and desktop interactions. They use synthetic fixtures. No mobile verification is
performed. Actual tracker accuracy, mounting, calibration quality, runtime latency,
screen alignment, safety and study readiness remain unverified without equipment.

See [THIRD-PARTY.md](THIRD-PARTY.md) for bundled library and API references.
