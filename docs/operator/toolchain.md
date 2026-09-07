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

Python tools use the standard library. A compatible interpreter must be supplied
on each machine. The standalone C# test harness uses the pinned .NET SDK; Unity
runtime builds use the Editor's own compiler/runtime. Source must remain within
the declared C# language version. .NET console tests alone do not prove Unity
compatibility. Optional Unity CLI is used for project creation; an existing checkout
can build using the exact Editor executable and repository scripts.

Study Windows/GPU/driver/SteamVR and Jetson OS/hardware baselines remain pending
actual commissioning. Do not reuse another machine's absolute install path,
device endpoints, serial bindings, display mapping or calibration implicitly.
