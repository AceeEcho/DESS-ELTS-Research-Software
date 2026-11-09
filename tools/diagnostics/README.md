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
