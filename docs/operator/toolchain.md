# Toolchain provenance

[config/toolchain.json](../../config/toolchain.json) is the single editable toolchain
manifest. Portable scripts read it. Unity package versions live in Unity's manifest
and lock; the toolchain points to them. Vendored OpenVR has its own exact upstream
origin/license/checksum manifest. Do not maintain a second editable pin in docs.

The selected Unity installation and binary version were verified, and the CLI
reports an active existing license. Runtime import, tests, build and new-machine
validation require separate evidence. The `validation` entries in the manifest
are baseline observations; live progress/evidence establishes subsequent results.
No automatic license/account agreement is part of bootstrap.

Python tools use the standard library. `SETUP-DEV.cmd` discovers a compatible
interpreter or installs the Windows version in `windowsSetup` in the manifest.
The standalone C# test harness uses the pinned .NET SDK; Unity
runtime builds use the Editor's own compiler/runtime. Source must remain within
the declared C# language version. .NET console tests alone do not prove Unity
compatibility. Optional Unity CLI is used for project creation; an existing checkout
can build using the exact Editor executable and repository scripts.

## Automatic Windows development setup

The [illustrated guide](multi-machine-setup.md) is the user entry point.
`scripts/setup-dev.ps1` orchestrates detection, missing-tool installation,
bootstrap, Unity import, and verification. It requires Windows x64 and a Git clone.
`-Check` detects tools without installing; it does not claim import or test success.
`-SkipVerification` still prepares/imports but reports `prepared-unverified`.
Use `-PythonExecutable` / `-UnityEditor` for custom existing installations.

Private installs use `%LOCALAPPDATA%/ELTS/Tools/`. Paths are recorded as JSON in
ignored `config/local/development-tools.json` and consumed by the existing wrappers
and `OPEN-DEV-SHELL.cmd`. The setup does not change Git identity, credentials,
branches or the global PATH. It preserves `config/local.json`; fresh clones create
that file from the existing synthetic template. Re-run setup after moving a clone
or changing toolchain pins. Do not distribute the local paths file.

Downloads are HTTPS and checked against hashes in `config/toolchain.json` before
execution/extraction. Sources: [Python Windows releases](https://www.python.org/downloads/windows/)
and their Sigstore digest, [Microsoft .NET release metadata](https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json),
[Node.js release checksums](https://nodejs.org/dist/v24.21.0/SHASUMS256.txt), and the
[Unity CLI version manifest](https://public-cdn.cloud.unity3d.com/hub/prod/cli/1.0.0-beta.6/latest.json).
Unity CLI handles the signed Hub/Editor downloads; setup never disables signature
checks or auto-accepts Unity licensing. Hub is reused or installed when an Editor
installation is needed. Windows Mono support is included in the Windows Editor;
this project does not request IL2CPP or Visual Studio workloads.

To update a pin, review the official release/checksum, update the version and its
hash together in the canonical manifest, and rerun the installer regression tests.
`global.json` is the .NET selector; CI checks it exactly matches the manifest so a
newer installed SDK cannot silently replace the selected one. Historical tested
versions in `validation` / `testedVersion` remain historical observations.

Study Windows/GPU/driver/SteamVR and Jetson OS/hardware baselines remain pending
actual commissioning. Do not reuse another machine's absolute install path,
device endpoints, serial bindings, display mapping or calibration implicitly.
