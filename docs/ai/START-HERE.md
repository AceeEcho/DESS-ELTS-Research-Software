# Begin building ELTS

The project user has authorized implementation using accepted amendment
[PC-001](../plan/PC-001-approved-build-amendment.md). A fresh **GPT-6 Astra with
medium reasoning** may begin. Do not repeat the pre-build planning review or ask
again whether to adopt its already-approved recommendations.

## First actions

1. Read root `AGENTS.md`, this guide, `BUILD-CONSTRAINTS.md`, PC-001, and both
   original deliverables in full, as required for the initial implementation.
   Use the context index to locate sources; read only assigned source sections
   after the initial reading. Preserve all existing files and any user changes.
2. Inspect the actual workspace, repository/remotes/history, competing owners and
   current state. The prior review found no local Git repository and no remote
   refs; verify again. The intended remote is
   `https://github.com/AceeEcho/DESS-ELTS-Research-Software`. An empty remote is
   handled by the local initialization/preservation flow authorized in PC-001.
3. Validate `docs/plan/approved-plan.json` with the commands below. It contains
   source-hashed workbook sheets and the effective active definitions. Import the
   active definitions, not the old superseded dependencies/statuses. Original
   counts are 91 work items and 243 steps; PC-001 has **103 active work items and
   267 active steps**, with all 62 gate criteria unchanged.
4. If no valid runtime progress exists, begin at **P0.3.S001**. Continue in the
   specification order using **BOOT.S002 through BOOT.S026**. Consult
   `docs/plan/bootstrap-map.md`; old workbook P0.3 suffixes have different meanings.
5. Until BOOT.S011, use the authorized durable bootstrap journal. Build/test the
   catalog importer, event writer/reducer and fixtures before importing truthful
   journal evidence into production events. No fictional PROJECT_STATE.json,
   backdated event creation, fabricated task completions, or gate passes.
6. Once valid progress exists, regenerate/validate it and follow
   `execution.primaryStepId`, including eligible DEV work when the baseline gate
   is blocked by equipment. Claim bounded work, respect ownership/worktrees,
   verify, record evidence, and regenerate state at each atomic checkpoint.

## Portable planning checks

Requires Python 3.10+; the plan tools use only the standard library. From the root:

```text
python tools/plan/build_plan.py --check
python -m unittest discover -s tools/plan -p "test_*.py"
```

On Windows, use `py -3` if that is the available interpreter. In Codex, if neither
command exists, resolve the bundled Python through `load_workspace_dependencies`
and invoke the returned executable. Do not assume a previous machine's absolute
runtime path. The builder resolves inputs from its own location and does not write
progress. `--write` regenerates only planning outputs after reviewed rules changes.

The approved catalog is an input to the future runtime progress implementation,
not that implementation itself. Validate baseline source hashes, rule hash and
catalog hash in generated-state provenance. The existing planner tests do not prove
the future runtime reducer handles claims, event transitions, evidence or gates.

## What is already decided

- Unity **6000.3.23f1 LTS** is selected. Pin that exact version; verify installation,
  license, project import, tests and builds. A different installed patch is not a
  substitute. Missing Unity may block Unity-specific work while eligible pure
  software work continues through DEV predicates.
- The ELTS apparatus is built. Testing equipment is not currently available.
  Use synthetic tracking, explicit fixture geometry and simulated links where
  permitted. Keep positions, dimensions, device bindings and calibration configurable.
- PC-001 is accepted: specification bootstrap ordering, explicit source mapping,
  corrected P2 predicates, early D-12 interface work and DEV-01 through DEV-12.
- Astra coordinates at medium effort. Use the current agent policy: Spark for
  small focused work, Luna/Terra as complexity and consequence warrant. Check the
  actual dispatch tool's model list; do not confuse desktop-task availability
  with subagent availability. Respect the total cap of eight agents and required
  isolated worktrees/ownership. No separate user task as a dispatch workaround.

## Scope and stop conditions

Build and test as much eligible software as possible. Continue independent work
when one task is blocked. Complete each atomic step with its specified evidence;
do not complete a mixed baseline task just because its DEV support item is done.
Keep shared clocks, raw logged poses, validity flags, world-space targets,
operator/participant separation and command-only ELTS boundaries intact.

Missing controller code/protocol documentation and the original source build-plan
appendices are not reasons to stop repository bootstrap. Inspect available sources
first; when an implementation needs a missing exact contract, record that blocker
and request the specific source while continuing other eligible work. Do not invent
a final protocol, measurement, serial number, study parameter or safety limit.

No final D-01 through D-18 outcome or G0-G8 passage is granted by this start prompt.
Physical testing, real equipment actuation/deployment, study release, participant
data collection, remote merge, and protected repository settings need their existing
evidence/authority. BOOT.S025 permits documentation of settings pending actual
configuration. Trello's unverified Butler rules do not block local build work;
Trello never supplies authoritative completion events.

When the session ends, leave a durable handoff with actual completed IDs, source
revision, changed paths, performed checks/evidence, missing inputs, current gate,
and exact next eligible step. Report software progress separately from physical
readiness. Never mark the project complete without authorized G8 passage.

## Paste into a fresh session

> You may begin building the ELTS project. Use GPT-6 Astra with medium reasoning.
> Read AGENTS.md and docs/ai/START-HERE.md and execute the accepted PC-001 amendment.
> Start at P0.3.S001 unless later valid event-derived progress exists. Preserve
> existing work, bootstrap the progress system, then build as much eligible software
> as possible using the approved development lane. Unity is 6000.3.23f1 LTS; ELTS is
> built but testing equipment is unavailable. Keep placement/configuration editable,
> retain pending physical tests and gates, and follow the current Spark/Luna/Terra
> dispatch policy. Do not repeat the planning review or seek approval again for
> PC-001. Leave verified progress and an exact next-step handoff.
