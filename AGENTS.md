# ELTS working instructions

Build modular, portable software. Centralize adjustable values in configuration
or clearly named constants. Use repository-relative paths; document prerequisites.
Comment intent, units, invariants, and non-obvious decisions in human-readable code.

When asked to begin building, read `docs/ai/START-HERE.md` and follow accepted
amendment `docs/plan/PC-001-approved-build-amendment.md`. The user has approved
the bootstrap reconciliation and bounded development-only lane. Do not ask for
that approval again or restart the historical readiness review. Follow the exact
active step IDs and predicates in `docs/plan/approved-plan.json` after validating it.

Read `docs/ai/CONTEXT-INDEX.md` once to locate the relevant source. Load only the
assigned task, applicable contracts, dependencies, and required governance.
Preserve existing work. The specification and workbook in `deliverables/` remain
the architecture and execution authorities; this efficiency setup does not pass
project gates or resolve study decisions. Follow their initial-agent reading and
bootstrap requirements before starting architecture implementation.

When the progress reducer and `PROJECT_STATE.json` exist, validate generated state
and inspect the assigned atomic step before editing. Until then, report bootstrap
as incomplete; never invent progress events or mark workbook tasks complete.
PC-001 defines the narrow pre-reducer journal exception and truthful event import;
after bootstrap, use its global `execution` selection alongside the baseline gate
anchor so missing testing equipment does not stop eligible software work.

## Current build constraints (user confirmed 2026-09-06)

- The ELTS apparatus is completed and built. Testing equipment is not currently
  available (user correction, 2026-09-06). Do not describe the ELTS itself as unbuilt.
- Use Unity **6000.3.23f1 LTS**, selected by the user. Carry this exact version into
  the canonical toolchain manifest and Unity project during bootstrap. Verify its
  installation and runtime separately; do not substitute another installed patch.
- Build as much software as the approved dependency plan allows using synthetic
  tracking, simulated devices, fixtures, and interchangeable hardware interfaces.
- Keep positions, orientations, dimensions, tracker bindings, device endpoints,
  and calibration configurable. Document units and coordinate frames. Clearly
  identify synthetic/unmeasured geometry; never treat it as measured calibration.
- Record hardware-dependent tests as deferred or blocked, with missing equipment
  and required follow-up. Simulated passes do not pass physical or safety criteria.
  Installing or repositioning parts requires applicable calibration and validation.
- Before implementation read `docs/ai/BUILD-CONSTRAINTS.md` and the accepted PC-001
  amendment. The historical review's plan conflicts are resolved by that amendment;
  physical criteria and gates remain pending. Bootstrap is still incomplete.

## Efficient execution

- Define the intended result, scope, and acceptance checks before substantial work.
  Use `docs/ai/TASK-TEMPLATE.md` for complex tasks or handoffs; skip ceremony for
  small edits. Make routine reversible choices within the user's authorization.
- Search with `rg` in likely paths; inspect matching sections before entire files.
  Avoid loading generated HTML, rendered pages, binary artifacts, and build output
  unless needed for the task. Expand context when evidence requires it.
- Batch independent reads. Keep dependent actions sequential. Return compact tool
  summaries; retain full diagnostics in files and inspect relevant failure excerpts.
  Truncated output is incomplete evidence: narrow the query or read another chunk.
- Astra coordinates. Dispatch `gpt-5.3-codex-spark` for small, targeted code fixes,
  focused tests, and similarly bounded tasks. Use `gpt-5.6-luna` for higher-priority
  or moderately complex work and `gpt-5.6-terra` for more complex or consequential
  work as needed. Choose by scope, uncertainty, and consequence, not urgency alone.
  Read concurrency and effort defaults from `config/ai/agent-policy.json`. The current
  cap is eight agents total (one coordinator plus seven subagents), not a target.
  These instructions authorize bounded delegation for independent test suites,
  focused failure diagnosis, disjoint module fixes, and independent review when
  useful work can proceed in parallel. Prefer Spark for independent small tasks;
  keep tightly coupled work local when delegation would duplicate effort.
  Preserve the specification's ownership, worktree, dependency, and gate rules.
  Give each worker an objective, owned paths, checks, and expected evidence; avoid
  duplicate investigation and simultaneous writes to shared contracts or outputs.
  Tests must identify the revision/snapshot tested; rerun affected checks after
  integration. Astra owns integration, critical contract decisions, and final
  verification. Workers return blockers to Astra rather than expanding scope.
  Where the spawn tool accepts a model, explicitly select the routed model; use
  concise context (`fork_turns="none"` or a bounded turn count) when a full-history
  fork would inherit Astra and prevent a model override. Check the host-supported
  model list before dispatch. If the selected model is unavailable, report that
  limitation and keep the task local, or explain an appropriate Luna/Terra
  escalation within this policy. Never silently substitute a model or create a
  separate user task as a workaround. Reuse workers where practical.
- Use the lowest reasoning effort that reliably satisfies the task. The project
  default is configured in `config/ai/agent-policy.json`; use medium for uncertain
  multi-file work and high for difficult diagnosis or critical contract review.
  Instructions alone cannot change the running model's effort setting.
- Run required checks and relevant regression tests. After they pass, finish;
  repeat or broaden checks only for changed code, failures, or unresolved risks.
  Token savings never justify omitting evidence, scientific invariants, or safety gates.
- Report outcome, changed files, verification, and material remaining issues briefly.
  Keep handoffs to durable facts, evidence paths, blockers, and the next action.
  Avoid repeating plans, long transcripts, or requests for narrated reasoning.

Read `docs/ai/TOKEN-EFFICIENCY.md` only when changing or troubleshooting this setup.
