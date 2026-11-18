# ELTS synthetic development package

This is a Windows x64 development player with generated tracking and an in-process
mock controller. It never connects to the physical apparatus. It is **not study
ready**. The apparatus is built. One base station, two Vive trackers, mounting
posts, and a headband with mounts are available, but physical validation remains
pending; availability of other test/metrology equipment must still be confirmed. See [the follow-up register](HARDWARE-FOLLOW-UP.md).

## Start the player

1. Extract the complete ZIP into a writable local directory. Paths with spaces
   are supported. Keep the player and its adjacent folders together.
2. Double-click **PREPARE-REHEARSAL.cmd** once to create fictional participant **0**
   and launch ELTS. Subsequent launches use **START-ELTS.cmd**. Both use the same
   private development directory under `%LOCALAPPDATA%/ELTS/DevelopmentRehearsal`
   by default. Set `ELTS_STATION_HOME` to another absolute directory to use a restored station.
   Only explicit preparation creates the example. A regular start after removing
   participant data opens an empty station. **ADD-REHEARSAL-ID.cmd** opens a number
   picker for another fictional participant. It suggests the first unused ID.
   Click **Add ID** and select it from the dashboard. The package requires no
   Unity Editor or .NET SDK.
3. In the browser dashboard, open **Settings**, choose the plan, then select **New
   participant**, choose **0**, and create the recording. At least 2 GB free space
   is required. Choose **Begin walkthrough** or **Manual mode**. The example uses
   fictional development settings.
4. Scenarios last 300 seconds each by default. The mouse fires on click-and-release;
   **Stop test** ends an attempt early and
   **Repeat a test…** reserves a new attempt. **Abort participant session** ends
   the session; it never silently resumes.
5. Use **Data** to review the recording and export results. For a fresh rehearsal,
   finish or abort the current recording and open **Settings**. Choose **Remove
   all participant data…**, type `CONFIRM` exactly, then choose **Remove**.
   The reset erases data in the selected rehearsal station without making a backup.
6. Close the player normally and let its recording operation finish. Keep the
   recordings together in the station folder shown by **Data**. Incomplete writer closure
   is an error requiring inspection, not usable complete evidence.

Real two-display assignment, pose tracking and controller behavior are separate
pending checks. Accepted fixture output does not replace the startup rig
configuration. More detail: [calibration](docs/calibration.md),
[sessions](docs/session.md), and [mock protocol](docs/elts-session.md).

## Verify the package or a rehearsal recording

These optional checks use Python 3.10–3.14. From the extracted package folder:

```powershell
py -3 tools/build/package.py --verify .
```

To check a recording you created, open **Data**, find its recording folder and
pass that folder to the verifier:

```powershell
py -3 -m tools.logging.verify_run "C:/path/to/recording"
```

When `package-info.json` lists an optional `sampleRun`, the included sample
can also be inspected independently:

```powershell
py -3 analysis/run_ingest.py samples/synthetic-run --calibration analysis/fixtures/synthetic-calibration.json --output ../synthetic-analysis.json
```

`PACKAGE-MANIFEST.sha256` binds the player, documentation, tools, evidence and
sample files. Verification permits only expected recording, reservation and
calibration filenames below the player’s configured data directory, plus disposable analysis/tool
Python bytecode caches. Changing packaged
code, configuration or sample data fails integrity verification. Hashes detect
changes; they are not signatures or research/safety approval.

`package-info.json` identifies the package recipe and player revision. Each evidence
report is bound either to the player binary hash or to a Git revision with the same
versioned runtime/configuration sources. This source comparison does not turn a
console fixture into a standalone or hardware test. The player
retains its own `build-info.json` and `MANIFEST.sha256`. A `dirty` source marker is
reported truthfully; consult its recorded source snapshot hashes. Evidence in
`evidence/` states which checks ran and their limits. Copied-output testing on this
machine does not establish second-machine compatibility.

## Change configuration for development

Build a new package from the source checkout after editing its canonical
`config/development/`, `config/rig/`, or machine-local configuration and running
the documented staging checks. Positions and offsets are in metres in the stated
Unity coordinate frame; rotations and identifiers must satisfy the schemas.
Do not hand-edit the packaged effective configuration or its hashes. Never treat
synthetic geometry as measured calibration.

The source checkout's `scripts/package-development.ps1` assembles an already
verified development build. Packaging rejects player folders containing existing participant data. The package manifest records the build’s configured relative data directory. Study provisioning and `package-study.ps1` remain
blocked until their separate approvals and physical prerequisites are satisfied.
