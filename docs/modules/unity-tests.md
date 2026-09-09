# Unity test boundary

Run `scripts/test.ps1 -Suite unity-edit` and `scripts/test.ps1 -Suite unity-play`
with Unity 6000.3.23f1. Supply `-UnityEditor` if it is installed outside Unity Hub's
standard location. Bootstrap/staging prepares StreamingAssets configuration.
The wrapper requires a fresh, nonempty, passed NUnit XML result and process exit 0.

Edit Mode loads the staged synthetic configuration, checks source provenance,
rejects study authorization, and rejects default tracking samples. Play Mode
creates a temporary GameObject whose Update consumes paired head/weapon samples
across frames using a manual shared clock. It checks paired timestamps, sequence
advance, main-thread execution and immutable prior observations. The object and
source are disposed after the test.

These are development smoke checks. Manual time does not measure acquisition
cadence, display latency, tracking hardware, physical calibration or safety.
The standalone runtime suites cover more numerical and failure cases.
