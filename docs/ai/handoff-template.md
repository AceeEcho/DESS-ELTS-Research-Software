# Durable handoff template

Use [handoff.schema.json](../../schemas/progress/handoff.schema.json) for the
machine-readable record, saved as a new file under
`project-management/progress/handoffs/`. Populate actual values, not placeholders.
Validate it with the repository schema validator before referencing it.

- `schemaVersion`: 1; `recordedAtUtc`: actual UTC; `actor`: type/id/tool.
- `branch` and tested `sourceRevision`; explain any separate dirty snapshot in checks.
- `completedIds`: only valid event-derived completions; `changedPaths`: relative paths.
- `checks`: commands and results actually observed; `evidence`: schema-bound receipts.
- `blockers`: each has `id`, `reason`, `requiredAction`, `responsibleRole`.
- `currentGate`: baseline anchor; `nextStepId`: exact selected step or null.
- `nextAction`: one concrete continuation including required input if blocked.

For a delegated subassignment also record selected model/routing, isolated worktree,
owned paths, integrated commits and snapshot tested. Keep parent task ownership and
branch transitions explicit through events. Synthetic completion never passes a
physical criterion or parent baseline acceptance automatically.
