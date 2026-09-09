# ELTS Research Software

Portable research software for the built ELTS apparatus: a Windows Unity application,
a command-only ELTS link, immutable research logging, and offline analysis.
The apparatus is built; testing equipment is currently unavailable. Current work
uses explicitly synthetic inputs and does not qualify a study session.

**New computer or new to Git? Start with the
[illustrated setup and two-computer guide](docs/operator/multi-machine-setup.md).**
It walks through GitHub Desktop, cloning this repository, installing the tools,
editing both projects, and sending changes between computers.

1. Clone this repository with GitHub Desktop into a normal local folder.
2. Double-click [`START-ELTS.cmd`](START-ELTS.cmd). On first use it prepares and
   verifies the development setup, builds the synthetic dashboard, then opens
   both the dashboard and the Unity project in Unity **6000.3.23f1 LTS**.
   Complete any installation or Unity account/license prompts. Later starts reuse
   the setup and unchanged build. Errors stay visible in the window and are saved
   in `diagnostics/start-console.log`.
3. Use `main` on each computer: **Pull → edit → review → commit → push**.
   Close Unity before pulling. Push your changes before switching computers.

[`OPEN-DEV-SHELL.cmd`](OPEN-DEV-SHELL.cmd) opens PowerShell with the detected tool
paths. The editable Unity project is in `unity/`; the editable browser simulation
is in `elts-simulation/`. You get both in one clone. GitHub synchronizes committed
source; caches, installed tools, local machine settings and private data stay local.

If startup needs to rebuild while this project is open in Unity, close the editor
and click `START-ELTS.cmd` again. Existing build folders and recordings are preserved.
`SETUP-DEV.cmd` remains available for setup/verification alone. This launcher opens
development software; it does not authorize a study or connect physical hardware.

Existing developers can use the [developer quick start](docs/operator/developer-quick-start.md).
The validated [PROJECT_STATE.json](PROJECT_STATE.json) is the current work record;
its `execution` section selects eligible work while `current` preserves the baseline
gate anchor. Do not infer progress from this README or a checklist.

## Authorities and navigation

- [Original architecture specification](deliverables/ELTS_Repository_Portability_and_Multi_LLM_Architecture_Specification.docx)
  and [readable architecture export](docs/architecture/ELTS_ARCHITECTURE_SPECIFICATION.txt).
- [Original execution workbook](deliverables/ELTS_Implementation_and_Progress_Workbook.xlsx),
  [approved PC-001 amendment](docs/plan/PC-001-approved-build-amendment.md),
  and [generated runtime catalog](project-management/task-catalog.json).
- [Safety boundaries and pending physical validation](docs/safety/README.md).
- [Agent entry point](AGENTS.md), [context index](docs/ai/CONTEXT-INDEX.md),
  and [progress protocol](docs/modules/progress.md).

## First verification

With Python 3.10+ available as `python`, run from this repository:

```powershell
python tools/plan/build_plan.py --check
python tools/progress/import_plan.py --check
python tools/progress/validate.py
python tools/progress/reduce.py --check
python tools/progress/report.py
```

These commands inspect local software state. Unity import, EditMode/PlayMode tests,
Windows build, clean-copy validation, and physical acceptance have separate evidence.
Unity must be exactly **6000.3.23f1 LTS**. New-machine setup results are retained in
`diagnostics/setup-report.json`; detection alone is not a runtime test pass.

The [verified bootstrap checkpoint](docs/ai/bootstrap-handoff.md) records the
completed clean-clone, Unity test, Windows build and copied-player startup checks.
It also identifies the validation limits and pending physical/remote controls.

## Configuration and contribution

`config/` is the sole hand-edited configuration namespace. Units, placement,
tracker bindings, endpoints, and calibration must remain explicit and adjustable.
Generated Unity/release copies are staged from that source. Development fixtures
must identify synthetic/unmeasured geometry and must never become a study fallback.

Keep each change on an owned branch, follow the exact approved atomic step, retain
source/config/fixture provenance, and attach meaningful verification. Raw participant
data and deployment credentials do not belong in Git. Baseline files are preserved
unchanged. Remote publication, protected settings, merge, and study release are
separate actions; local bootstrap does not perform them.
