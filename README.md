# ELTS Research Software

Portable research software for the built ELTS apparatus: a Windows Unity application,
a command-only ELTS link, immutable research logging, and offline analysis.
The apparatus is built; testing equipment is currently unavailable. Current work
uses explicitly synthetic inputs and does not qualify a study session.

Start with the [developer quick start](docs/operator/developer-quick-start.md).
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
Unity must be exactly **6000.3.23f1 LTS**. Toolchain and portable script setup are
tracked by the active bootstrap steps rather than assumed complete here.

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
