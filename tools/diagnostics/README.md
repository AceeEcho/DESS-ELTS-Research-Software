# SteamVR tracking health

SteamVR has no API for the moments that invalidate a tracking calibration: it
re-estimating which way is up (which turns its whole frame about the base
station), a tracker being reset, or the base station being bumped. They appear
only in its server log. From the project root:

```powershell
py -3 tools/diagnostics/steamvr_health.py
```

It reports, for the most recent day in SteamVR's log, how many times the frame
was re-set and by how much, tracker resets by serial, and whether each tracker's
learned accelerometer calibration is plausible beside its factory values. It
reads only. Run it after a tracking session that felt wrong, and before
calibrating the station for [live tracking](../../docs/operator/live-tracking.md).

If a tracker's learned calibration is flagged as implausible, close SteamVR and
remove it so SteamVR relearns from the factory values:

```powershell
py -3 tools/diagnostics/steamvr_health.py --reset-imu LHR-XXXXXXXX
```

This edits SteamVR's own `lighthousedb.json` after writing a timestamped backup
beside it, and refuses to run while SteamVR is open. Afterwards start SteamVR and
move the tracker in view of the base station for a minute. These are readings of
SteamVR's records; they are not physical measurements of the rig.

# VIVE trigger input test

Start SteamVR and connect the gun tracker and its USB receiver. In SteamVR's
**Manage Trackers**, assign the gun tracker **Held in hand → Right Hand** (or Left
Hand). Record its previous role so you can restore it afterward. Changing a role
can change the pose convention seen by software; do not save calibration during
this button test.

From the project root, use the live-tracking environment installed by
`START-CALIBRATION.cmd`:

```powershell
& '.\Calibration Environment\.venv\Scripts\python.exe' tools/diagnostics/vive_trigger_test.py --serial YOUR-TRACKER-SERIAL
```

The window waits for that exact serial, retries after connection loss, and shows
input availability separately from the trigger state. Pull and release five
times slowly, then hold for three seconds and release. Look for one press and
one release per pull, with a steady PRESSED indication during the hold. A trigger
already held when input first becomes available establishes a baseline; it does
not count as a newly observed press.

JSONL files in `diagnostics/trigger-test` retain observed raw button masks, touched
masks and axes when those values change. No debounce is added. Polling runs at a
nominal 10 ms interval, so short transitions can be missed; these host observation
times cannot establish switch timing, bounce characteristics or physical latency.
Unavailable periods clear the baseline and never create press/release counts.

Close the window and restore the original tracker role when finished. The monitor
itself only reads input: it does not change roles, launch study sessions, send
haptics or apparatus commands, or modify calibration and participant recordings.
