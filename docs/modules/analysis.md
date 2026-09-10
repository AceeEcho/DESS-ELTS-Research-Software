# Synthetic analysis ingest

`analysis/src/elts_analysis` provides a standard-library-only, read-only ingest for completed ELTS logging v1 runs. It consumes `samples.ndjson`, `events.ndjson`, `targets.ndjson`, and `session-summary.json`; it refuses missing, incomplete, tampered, or unsupported schema-version inputs. Raw products are never rewritten. The derived report records each raw product's absolute source path, byte count, SHA-256, the calibration SHA-256, and the session provenance copied from the closure summary. Summary counts and all three product hashes are checked before parsing.

Run it from the repository root with the bundled or system Python (the source package is intentionally not installed):

```text
python analysis/run_ingest.py RUN_DIRECTORY --calibration analysis/fixtures/synthetic-calibration.json --output analysis-output.json

On Windows PowerShell from the repository root, the same portable command is:

```text
python .\analysis\run_ingest.py .\test-results\dev04-publication\synthetic-soak --calibration .\analysis\fixtures\synthetic-calibration.json --output .\analysis-output.json
```
```

Set `PYTHONPATH=analysis/src` when invoking the source package directly, for example:

```text
PYTHONPATH=analysis/src python -m elts_analysis test-results/synthetic-soak/run-0001 --calibration analysis/fixtures/synthetic-calibration.json
```

Calibration is an explicit JSON input with required non-empty `calibrationId`, numeric finite `muzzleOffsetMeters`, numeric finite `boreDirectionLocal`, and a numeric unit-length `zeroCorrectionQuaternion` (within `1e-8`); numeric strings, booleans, and arbitrary quaternion normalization are rejected. These are configuration values and do not assert measured or physically validated calibration. Aim error uses the corrected world bore ray and the muzzle-to-target ray. Target snapshots are folded by `(blockId, targetId)` in timestamp order; the latest non-destroyed snapshot replaces older positions, destruction removes the candidate, and the report records the nearest-angular-candidate policy. Samples with either tracker disconnected, invalid, or missing a usable pose are excluded from aim-error summaries and counted as invalid; no invalid pose is interpolated.

The primary score is derived only from `TargetDestroyed` events. Each such event must carry scalar `blockId` and `targetId` fields. Scoring requires matching `BlockStarted` and `BlockEnded` events for an exact 300-second interval; the scoring interval is half-open `[start, end)`. Duplicate target destruction within a block is rejected. A run without a complete marker pair reports `scoreStatus: unavailable`; an explicit `BlockAborted` prevents that block from producing a DV, while a paired block with an inconsistent duration is rejected. Target lifecycle records are retained as supporting state and are never used as the primary score source. No global-clock bucketing or target-lifecycle inference is used.

The development fixture tests cover known counts, invalid-pose exclusion, hand-computed zero-degree aim error, duplicate score rejection, unsupported schemas, and unavailable scores when block markers are incomplete:

```text
PYTHONPATH=analysis/src python -m unittest discover -s analysis/tests -v
```

This lane is synthetic development only. It does not establish hardware calibration, physical timing, participant or safety acceptance, or any study gate.
