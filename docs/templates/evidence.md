# Evidence receipt authoring

Use [evidence.schema.json](../../schemas/progress/evidence.schema.json).
Store an immutable output/receipt before referencing it in an event. Required fields:

- `id`, repository-relative file `path`, exact byte `sha256`, and exact `targetIds`.
- `criteria`: the actual criteria covered; step completion uses `outcome` and
  `verification`, task closure uses `task-acceptance`.
- `class`: document/source/automated_test/synthetic_test/toolchain_runtime or
  authenticated physical_test/human_approval where applicable.
- `command`, truthful `result` (pass/fail/deferred), tested `sourceRevision`,
  actual `recordedAtUtc`, and `limitations`.

The referenced output must retain source/config/fixture hashes, command output,
exit status, observed assertions and unresolved follow-up. Separate software,
synthetic, runtime and physical evidence. Do not relabel synthetic data as measured.
Hashes verify retained bytes; independent approval establishes external authority.
