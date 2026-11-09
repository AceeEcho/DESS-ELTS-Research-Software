# Documentation map

Start with the [project README](../README.md) to install ELTS and run a session.
The folders below are grouped by who reads them.

## Running sessions (`operator/`)

| Document | Use it to |
| --- | --- |
| [User guide](operator/user-guide.md) | Run a participant session from the dashboard, step by step |
| [Troubleshooting](operator/troubleshooting.md) | Fix setup, startup, session, export, analysis and hardware problems |
| [Setup on more than one computer](operator/multi-machine-setup.md) | Install with GitHub Desktop and keep several PCs in sync |
| [Hardware follow-up](operator/hardware-follow-up.md) | See what physical validation and room measurements are still needed |
| [Pupil timing follow-up](operator/pupil-time-synchronization.md) | Plan eye-tracker clock alignment (planning note) |

## Working on the software (`developer/`)

| Document | Use it to |
| --- | --- |
| [Architecture](developer/architecture.md) | See how configuration, tracking, sessions, rendering and logging fit together |
| [Developer quick start](developer/quick-start.md) | Build, test and verify from the command line |
| [Toolchain](developer/toolchain.md) | Check pinned tool versions and what setup installs |
| [Development package](developer/development-package.md) | Package a development player with its provenance |
| [Operator design language](developer/operator-design-language.md) | Keep the dashboard and participant screens visually consistent |
| [`modules/`](developer/modules/) | Read one page per subsystem, from `analysis.md` to `windows-build.md` |

## Participant tutorial (`participant-tutorial/`)

| Document | Use it to |
| --- | --- |
| [Detailed script](participant-tutorial/script.md) | Edit narration, on-screen steps and audio links (the source of truth) |
| [Reading script](participant-tutorial/reading-script.md) | Read the narration only. Generated from the detailed script; do not edit |
| [Outline](participant-tutorial/outline.md) | See the agreed sequence of parts |
| [Audio workflow](participant-tutorial/audio-workflow.md) | Generate, select, save and label narration recordings |
| [`voice-prompts/`](participant-tutorial/voice-prompts/) | Look up the exact prompt and take behind each recording |
| [`audio-archive/`](participant-tutorial/audio-archive/) | Find superseded takes. The active clips are bundled in `unity/Assets/ELTS/Operator/Resources/ELTS/TutorialAudio/` |

## Everyone

- [Safety boundaries](safety/README.md): what the software must never do, and what remains unvalidated.
- [Decision and work-item IDs](decisions.md): what labels such as D-12, DEV-10 and P8.4 refer to.
- [Hardware design files](../hardware/README.md): trigger connector CAD and bench tests.
