# ELTS synthetic development package

This is a Windows x64 development player with generated tracking and an in-process
mock controller. It never connects to the physical apparatus. It is **not study
ready**. The apparatus is built, but the required testing equipment and physical
validation remain unavailable. See [the follow-up register](HARDWARE-FOLLOW-UP.md).

## Start the player

1. Extract the complete ZIP into a writable local directory. Paths with spaces
   are supported. Keep the player and its adjacent folders together.
2. Open `player/ELTS-Synthetic.exe`. Unity and .NET SDK installation are not needed
   to run this packaged player. Use a Windows x64 machine with a graphics driver;
   this package has only been exercised on the development machine.
3. In **Synthetic session control**, enter a test ID and choose **Create synthetic
   recording**. At least 2 GB free space is required. Use invented test IDs only.
4. After the two-second synthetic preflight, choose **Continue**. In the
   calibration wizard, generate a fixture, review it, accept it, and continue to
   practice. Scroll inside each column to reach its controls. Provisional limits
   and eye inputs can be adjusted before fixture generation; **Redo** discards
   an unaccepted review. This is a generated fixture, not a measured calibration.
5. After practice, choose **Start block**. Each condition lasts 300 seconds.
   **Synthetic trigger** exercises shooting; **Save note** writes an annotation.
   Abort ends the current attempt. A permitted rerun reserves a new directory.
   **Simulate reconnect and abort** ends the session; it never silently resumes.
6. Close the player normally and let its recording operation finish. Keep the
   resulting `player/data/synthetic/` files together. Incomplete writer closure
   is an error requiring inspection, not usable complete evidence.

The two camera views are emulated on the development display. Real two-display
assignment, tracker bindings and controller behavior are separate pending checks.
The development NE selector offers Idle and Armed alternatives; this does not
decide the final study behavior. Accepted fixture output does not replace the
startup rig configuration. More detail: [calibration](docs/calibration.md),
[sessions](docs/session.md), and [mock protocol](docs/elts-session.md).

## Verify the package and try the included sample

These optional checks require Python 3.10–3.14, with no extra Python packages.
Run from the extracted package root. On Windows, `py -3` may replace `python`.

```powershell
python tools/build/package.py --verify .
python -m tools.logging.verify_run samples/synthetic-run
python analysis/run_ingest.py samples/synthetic-run --calibration analysis/fixtures/synthetic-calibration.json --output ../synthetic-analysis.json
```

The included sample is generated from known geometry with a manual clock. It
contains four 300-second logical blocks, samples, targets, shots and simulated
protocol events. It does not demonstrate 20 minutes of real-time acquisition.
The analysis file is written outside the package so the original input stays
unchanged. Use the rendering/replay tools' path field to load the absolute path
to `samples/synthetic-run` for recorded replay.

`PACKAGE-MANIFEST.sha256` binds the player, documentation, tools, evidence and
sample files. Verification permits newly generated recordings only below
`player/data/synthetic/` and disposable Python bytecode caches. Changing packaged
code, configuration or sample data fails integrity verification. Hashes detect
changes; they are not signatures or research/safety approval.

`package-info.json` identifies the package recipe and player revision. The player
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
verified development build. Study provisioning and `package-study.ps1` remain
blocked until their separate approvals and physical prerequisites are satisfied.
