# ELTS Research Software

A Windows Unity application, offline browser simulation and analysis tools for
the built ELTS apparatus. Current operation uses synthetic inputs; testing
equipment is unavailable and this is not a study-ready release.

## Start

**New to the dashboard? [Open the illustrated user guide](docs/operator/user-guide.md).**

1. Clone the repository. For detailed instructions, use the
   [setup and two-computer guide](docs/operator/multi-machine-setup.md).
2. Double-click [START-ELTS.cmd](START-ELTS.cmd). First use prepares the tools,
   builds the synthetic application, and opens the dashboard and participant game.
   Complete any installation or Unity account/license prompts. Open the `unity/`
   project manually in Unity **6000.3.23f1 LTS** when you want to edit it.
3. Later starts reuse the setup and unchanged build. Close Unity before a rebuild
   or Git pull. Startup errors are saved in `diagnostics/start-console.log`.

[OPEN-DEV-SHELL.cmd](OPEN-DEV-SHELL.cmd) opens PowerShell with detected tool paths.
[SETUP-DEV.cmd](SETUP-DEV.cmd) runs setup/verification without launching the app.
For multi-computer work: pull, edit, review, commit and push before switching PCs.

## Software and documentation

| Location | Contents |
| --- | --- |
| `unity/` | Unity application: tracking, calibration, sessions, rendering, logging and operator UI |
| `elts-simulation/` | [Offline browser simulation](elts-simulation/README.md) |
| `analysis/` | [Run ingestion and analysis](docs/modules/analysis.md) |
| `config/`, `schemas/` | Editable settings and data contracts |
| `scripts/`, `tools/` | Setup, build, validation, packaging and tests |
| `docs/` | [Architecture](docs/architecture.md), [developer commands](docs/operator/developer-quick-start.md), [operator dashboard](docs/modules/operator-dashboard.md), [desktop controls](docs/modules/desktop-input.md) |
| `jetson/` | [Controller integration limits](jetson/README.md) |

Make the requested change, run relevant checks, then review/commit. With Python
3.10+ available, the portable software checks are:

```powershell
python tools/ci/check.py --baseline
```

Use `scripts/test.ps1` for selected suites and `scripts/verify.ps1` for full
development verification, including available Unity/runtime tests.
See [CI behavior](docs/modules/ci.md) for automated checks.

Configuration, geometry, tracker bindings and endpoints remain adjustable.
Research recordings and machine-local settings stay out of Git. Follow
[safety boundaries](docs/safety/README.md) and
[required hardware validation](docs/operator/hardware-follow-up.md) before physical use.
