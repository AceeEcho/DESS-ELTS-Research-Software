# Paused build checkpoint — 2026-09-07

The user requested stopping after this checkpoint. No background continuation is
scheduled. Resume only when asked. The ELTS apparatus is built; testing equipment
is unavailable. All development work remains synthetic, with physical and study
gates pending. This note is a handoff, not completion evidence.

## Accepted work

Generated progress records P0.3 bootstrap and DEV-01 through DEV-04 as done.
The latest acceptance commit is `c797076`; the subsequent claim commit is
`8515ee0`. Validate `PROJECT_STATE.json` through the reducer before continuing.
Baseline selection remains G0; global development selection is DEV-05.S001.

DEV-04 now records versioned sample/event/target/summary products with provenance,
bounded queues, overflow/failure reporting, no overwrite and terminal publication.
The final summary is durable-flushed under a pending name, then moved to its final
name under timeout arbitration. A timeout before publication prevents later
publication. `Completed` (thread joined) and `CompleteOutput` (published output)
are distinct. An OS metadata-move stall can extend the close wait.

Evidence: `project-management/progress/evidence/DEV-04.json`,
`DEV-04-publication.json`, and `DEV-04-soak.json` in that same directory.
Integrated checks passed: 28 logging, 1232 throughput, 12 publication, a fresh
499-sample recording with one event and one target, offline verification,
pending-summary rejection, baseline suites and exact Unity 6000.3.23f1 EditMode.

The 1800-second soak validated 439867 paired samples, 1759 events and 1759 target
records, with zero queue drops. It averaged 244.37038269 Hz against a 250 Hz target:
timing acceptance did not pass. Its source predates the terminal-publication fix;
the separate current-source publication checks cover that change. Do not claim
Study-PC/full-display-load timing, physical safety, or nominal acquisition counts.

## Open work and exact locations

DEV-05.S001, DEV-06.S001 and DEV-07.S001 are in progress, not accepted. Their
claims and branches are recorded in the 120-event generated state. No step should
be started again. Workers have stopped; their branches are clean and retained.

| Work | Location / revision | Current checks and remaining work |
| --- | --- | --- |
| DEV-05 analysis | Sibling worktree `../ELTS-worktrees/dev05`, branch `work/dev-05`, commit `fd60842789a226c53297fa620fbdf93005b269c8` | Worker reports 4 fixture tests, compile and whitespace checks passed. Not integrated or independently reviewed. Needs real generated-run ingest, contract review, raw immutability checks and broader boundary validation. |
| DEV-06 rendering/replay | Main checkout, WIP files under `unity/Assets/ELTS/Rendering` and `tools/runtime-tests/RenderingChecks.cs(.csproj)` | 809 pure projection/prediction/transport checks passed. Unity EditMode import/compilation passed after correcting cross-assembly use of internal Geometry helpers. UI/camera/diagnostic scene and exercised replay controls are not implemented. |
| DEV-07 scenario | Sibling worktree `../ELTS-worktrees/dev07`, branch `work/dev-07`, commit `615373d` | Worker reports 11 scenario checks passed. Not integrated or independently reviewed. Needs Unity metadata/import, block-marker emission and trigger/aim/log sequence integration. |

The workers' commits are intentionally retained in isolated branches for review;
do not assume their tests verify an integrated main-checkout snapshot. Inspect
their module documentation and diffs, then integrate and rerun affected checks.
Preserve their worktrees until that is complete.

## Resume details

- Analysis and scenario agreed to use `TargetDestroyed` events with scalar
  `blockId` and `targetId`; target lifecycle records are never the primary score.
  Require explicit `BlockStarted` timing, fixed 300-second half-open windows and
  duplicate-target protection. Review early/partial block handling: a shortened
  block must not be reported as a completed primary DV.
- Rendering has pure off-axis math and an initial v1 recorded-log reader. The
  reader is WIP: no functional reader tests were run. Review strict JSON/schema
  parity, bounded line reads, immutable constructor validation, raw-byte summary
  hashes, and efficient target seeking before exposing it through controls.
- Rendering needs actual Unity camera-matrix corner tests, operator diagnostics,
  editable synthetic layout, play/pause/scrub UI, and runtime control checks.
  Rendering prediction must never mutate raw/logged head data or weapon poses.
- The rendering assembly compiles, but its code is not attached to the running
  development player. The previously built player predates this WIP work.
- DEV-10 is eligible but not started. Verify exact protocol inputs before coding;
  missing protocol details are scoped blockers. Never send poses/targets/servo or
  individual LED commands to the apparatus.
- Physical baseline P0.2, human P0.5 and actual issue-tracker P0.4 obligations were
  not claimed complete. Do not assume this development host is the Study PC.

Checks performed at pause:

```text
dotnet run --project tools/runtime-tests/RenderingChecks.csproj
scripts/test.ps1 -Suite unity-edit -Report diagnostics/dev06-checkpoint-unity.json
python tools/progress/reduce.py --check
git diff --check
```

Python on this host is available through the bundled Codex dependency runtime;
use the documented script `-PythonExecutable` argument if system Python is absent.
The Unity suite uses the pinned 6000.3.23f1 Editor. The reducer check passed with
120 events. Keep generated diagnostic files as local supporting records; the
accepted DEV-04 receipts preserve their source hashes.
