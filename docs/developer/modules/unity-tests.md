# Unity test boundary

Run `scripts/test.ps1 -Suite unity-edit` and `scripts/test.ps1 -Suite unity-play`
with Unity 6000.3.23f1. Supply `-UnityEditor` if it is installed outside Unity Hub's
standard location. Bootstrap/staging prepares StreamingAssets configuration.
The wrapper requires a fresh, nonempty, passed NUnit XML result and process exit 0.
The wrapper runs the Editor in batch mode with `-noaudio`; add that flag yourself
when starting Unity tests from the command line. Automated runs disable hardware
reads, so tests never touch a connected tracker. Each Play Mode run records into
its own fresh folder under Unity's temporary cache
(`elts-automated-recordings/<run>`), so earlier test recordings are kept for
inspection but never imported into the next run.

Edit Mode loads the staged synthetic configuration, checks source provenance,
rejects study authorization, and rejects default tracking samples. Play Mode
creates a temporary GameObject whose Update consumes paired head/weapon samples
across frames using a manual shared clock. It checks paired timestamps, sequence
advance, main-thread execution and immutable prior observations. The object and
source are disposed after the test.

These are development smoke checks. Manual time does not measure acquisition
cadence, display latency, tracking hardware, physical calibration or safety.
The standalone runtime suites cover more numerical and failure cases.
