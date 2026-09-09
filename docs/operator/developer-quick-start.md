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

   The driver writes structured reports under `diagnostics/`; a failed check is
   actionable evidence, not a reason to hand-edit generated state. Run the
   five read-only verification commands in [README.md](../../README.md) as
   their project-specific prerequisites become available.

   The portable console identities are `geometry`, `config`, `runtime`,
   `unity-edit`, and `unity-play`; `-Suite all` runs the available subset and
   reports deferred Unity work without claiming a study result. Unity XML is
   accepted only when it is fresh and reports `Passed`.
4. Read [the current state](../../PROJECT_STATE.json) and the exact active step in
   [the approved plan](../plan/approved-plan.json). Use `execution.primaryStepId`.
5. For implementation, follow [START-HERE.md](../ai/START-HERE.md). Do not hand-edit
   generated state or append an event until its dependencies, claims, and evidence
   validate. The writer rejects competing task branches and stale state.

If Python is not available, install a supported interpreter or invoke an existing
supported interpreter by its full executable path. Do not assume a Codex-specific
runtime path exists on another machine. Portable bootstrap/build scripts are added
and verified by their own steps; consult state before relying on them.

A schema or state mismatch is a stop condition for dependent work. Preserve the
error and reconcile the source/events; do not delete history or make the generated
state match an expected status manually. A missing device blocks its physical
checks while eligible synthetic work can continue under PC-001.

## Development build and study entry points

Run `scripts/build.ps1 -Output build/new-output` for a Windows synthetic player;
the output directory must be empty. See [build provenance](../modules/windows-build.md).
The standalone numerical/runtime checks additionally use the .NET SDK declared
in `config/toolchain.json`; that SDK is not required to run a built Unity player.

The root `FIRST-RUN.cmd` and `START-ELTS.cmd` are study entry-point skeletons.
They currently fail closed with a structured report because study configuration,
physical acceptance and release approval are pending. Use `bootstrap-dev.ps1`
for development setup. `CHECK-SYSTEM.cmd` invokes development diagnostics; a pass
does not authorize a study session. Every entry resolves its own repository path.
