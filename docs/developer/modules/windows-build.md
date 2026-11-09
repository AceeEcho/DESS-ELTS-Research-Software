# Windows development build

Run `scripts/build.ps1 -Output build/my-new-build`. The output directory must be
empty. Unity 6000.3.23f1 with Windows Mono build support is required; the wrapper
accepts `-UnityEditor` for another installation path. Configuration staging is
automatic. The Editor entry explicitly selects Windows x64, Mono, enabled build
scenes, Development and StrictMode. Failed builds retain diagnostics for review.

`build-info.json` records commit/tags, dirty state and diff hash, untracked file
hashes, exact toolchain/package pins, schema hashes, staged configuration hashes,
UTC build time and artifact hashes. `MANIFEST.sha256` also covers build-info.
For extracted ZIPs or copies without accessible Git history, `sourceKind` is
`unversioned-copy`: commit and dirty state are unknown (`null`), and the record
includes `sourceFilesSha256` and their combined `sourceSha256`. The version is
`dev-copy-<hash prefix>`. Generated caches and recordings are excluded from source
hashing. Packaging evidence for these builds must match the player binary hash.
Run `python -m tools.build.verify build/my-new-build` to verify the products.
The checksum manifest detects changes; it is not a release signature.

Every player has a synthetic-development version: a commit with a dirty/clean
suffix, or the source-copy hash above. Neither authorizes study data collection. The
bootstrap scene is a build shell; later development steps add session behavior.
Raw Unity logs are excluded from the checksum product list because diagnostics
can contain account or machine details. Keep them local. Study packaging remains
blocked by release and physical acceptance requirements.

For an actual Windows player startup check, run
`python -m tools.build.smoke build/my-new-build --report diagnostics/player-smoke.json`.
It verifies products first, then explicitly starts the player in non-graphical
batch mode. The probe loads staged configuration after three Update frames,
checks the synthetic/study boundary, emits its versioned completion marker and
exits. Normal startup does not run the probe. This check does not validate the
visible banner, display mapping, rendering latency or physical devices.
