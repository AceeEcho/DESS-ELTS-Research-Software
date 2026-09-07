# Context index

To begin implementation, read [START-HERE.md](START-HERE.md),
[current hardware constraints](BUILD-CONSTRAINTS.md), and the
[accepted PC-001 amendment](../plan/PC-001-approved-build-amendment.md).
The ELTS is built; testing equipment is unavailable. Selected Unity: 6000.3.23f1 LTS.
The user approved the readiness recommendations. The historical review remains
under `docs/reviews/2026-09-06/`; it no longer blocks the approved build scope.

This workspace currently contains planning artifacts and an offline simulation.
The Unity repository and progress reducer are not yet implemented. The approved
planning catalog exists at `docs/plan/approved-plan.json`; it is not live progress
state. This document is navigation, not authoritative progress state.

| Work | Read first | Expand when needed |
| --- | --- | --- |
| Agent configuration | `config/ai/agent-policy.json` | `schemas/ai/agent-policy.v3.schema.json`, `tools/ai/sync-config.ps1`, `docs/ai/TOKEN-EFFICIENCY.md` |
| Simulation | `elts-simulation/README.md` | `elts-simulation/elts-environment.html`; `build.mjs` for packaging. `index.html` is generated. |
| Begin building / architecture bootstrap | `docs/ai/START-HERE.md`, accepted PC-001 | Original specification/workbook plus `docs/plan/approved-plan.json`; `bootstrap-map.md` resolves conflicting IDs. |
| Approved planning data | `docs/plan/amendment-rules.json` | `tools/plan/build_plan.py --check`; the generated catalog preserves source sheets and active task/step definitions. |
| Planning artifact generation | `artifact_work/build_elts_spec.py` or `artifact_work/build_elts_workbook.mjs` | Relevant generation function; rendered pages only for layout verification. Generator code does not replace the baseline authority. |

Baseline files:
- `deliverables/ELTS_Repository_Portability_and_Multi_LLM_Architecture_Specification.docx`
- `deliverables/ELTS_Implementation_and_Progress_Workbook.xlsx`

During bootstrap, import the approved planning catalog into project management,
preserve the baseline and amendment hashes, and implement validated event-derived
progress. Use its active definitions for eligibility; raw source rows are retained
for provenance and must not reintroduce superseded prerequisites.
