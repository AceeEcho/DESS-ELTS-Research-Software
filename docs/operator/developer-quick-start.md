# Developer quick start

For an illustrated beginner walkthrough, use [setup on multiple computers](multi-machine-setup.md).
On Windows x64, clone the repository and run root `SETUP-DEV.cmd` to install
missing tools, import Unity packages, and run verification. Windows installation
prompts and Unity account/license steps remain interactive when required.
`OPEN-DEV-SHELL.cmd` loads this clone's detected tool paths into PowerShell.
Use the manual steps below when tools are already installed or in custom locations.

`scripts/verify.ps1` runs doctor and the complete currently available test set,
retaining separate diagnostic reports. A deferred Unity check is explicitly
reported and does not count as Unity runtime acceptance. Use `-PythonExecutable`
and `-UnityEditor` when the prerequisites are outside standard discovery paths.

This is a development setup for software connected to the already built ELTS.
Testing equipment is currently unavailable. No synthetic test establishes physical
calibration, safety, timing, display, or study-readiness acceptance.

1. Copy or clone the repository into a writable path; spaces are supported.
2. Provide Python 3.10+ and the exact Unity version declared in the root README.
   Full runtime verification also uses the pinned .NET SDK; the browser builder
   uses Node.js. Automatic setup reads all pins from `config/toolchain.json`.
3. From the repository root, run the first-run bootstrap once. It creates the
   local machine template without overwriting an existing local file and stages
   the synthetic generated configuration. From another directory, use the
   repository's full wrapper path instead:

   ```powershell
   & .\scripts\bootstrap-dev.ps1 -PythonExecutable C:\path\python.exe -UnityEditor C:\path\Unity.exe
   & .\scripts\verify.ps1 -PythonExecutable C:\path\python.exe -UnityEditor C:\path\Unity.exe
   & C:\path\to\checkout\scripts\test.ps1 -PythonExecutable C:\path\python.exe -Suite geometry
   ```

   `doctor.ps1` assumes staging has already completed; on a clean clone use
   `bootstrap-dev.ps1` first. The wrappers resolve the repository from their
   own location, so the full path is required when the current directory is
   elsewhere.

   The driver writes structured reports under `diagnostics/`. Fix the reported
   input or failure before rerunning the affected check.

   The portable console identities are `geometry`, `config`, `runtime`,
   `unity-edit`, and `unity-play`; `-Suite all` runs the available subset and
   reports deferred Unity work without claiming a study result. Unity XML is
   accepted only when it is fresh and reports `Passed`.
4. Work on the requested change. Read relevant
   source/contracts, edit, and run the affected checks. Setup is for a new machine
   or changed prerequisites, not a step to repeat before each edit.

If Python is not available, install a supported interpreter or invoke an existing
supported interpreter by its full executable path. Do not assume a Codex-specific
runtime path exists on another machine.

A configuration/schema mismatch must be fixed before using that configuration.
A missing device blocks its physical checks while synthetic work can continue.

## Development build and study entry points

Run `scripts/build.ps1 -Output build/new-output` for a Windows synthetic player;
the output directory must be empty. See [build provenance](../modules/windows-build.md).
The standalone numerical/runtime checks additionally use the .NET SDK declared
in `config/toolchain.json`; that SDK is not required to run a built Unity player.

The root `START-ELTS.cmd` now prepares development tools as needed, builds the
synthetic dashboard, and opens it alongside this Unity project in the pinned editor.
It reuses an unchanged build and saves startup output in `diagnostics/start-console.log`.
Close the project's editor first when setup or rebuilding is needed. Generated
startup builds live in distinct `build/start-elts-*` folders; older builds and data
are preserved. `FIRST-RUN.cmd` and `scripts/start-study.ps1` remain study entry-point
skeletons that fail closed while study configuration, physical acceptance and
release approval are pending. `CHECK-SYSTEM.cmd` invokes development diagnostics; a pass
does not authorize a study session. Every entry resolves its own repository path.
