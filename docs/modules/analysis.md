# Synthetic analysis ingest (DEV-05)

`analysis/src/elts_analysis` provides a standard-library-only, read-only ingest for completed ELTS logging v1 runs. It consumes `samples.ndjson`, `events.ndjson`, `targets.ndjson`, and `session-summary.json`; it refuses missing, incomplete, tampered, or unsupported schema-version inputs. Raw products are never rewritten. The derived report records each raw product's absolute source path, byte count, SHA-256, and the session provenance copied from the closure summary.

Run it from the repository root with the bundled or system Python:

```text
python -m elts_analysis RUN_DIRECTORY --calibration calibration.json --output analysis.json
```

Set `PYTHONPATH=analysis/src` when invoking the source package directly, for example:

```text
PYTHONPATH=analysis/src python -m elts_analysis test-results/synthetic-soak/run-0001 --calibration fixtures/calibration.json
```

Calibration is an explicit input. It contains `muzzleOffsetMeters`, `boreDirectionLocal`, and `zeroCorrectionQuaternion`; these are configuration values and do not assert measured or physically validated calibration. Aim error uses the corrected world bore ray and the muzzle-to-target ray, with the smallest angular error among target snapshots available at or before the sample. Samples with either tracker disconnected, invalid, or missing a usable pose are excluded from aim-error summaries and counted as invalid; no invalid pose is interpolated.

The primary score is derived only from `TargetDestroyed` events. Each such event must carry scalar `blockId` and `targetId` fields. Scoring requires matching `BlockStarted` and `BlockEnded` events; the interval is half-open `[start, end)` and may not exceed 300 seconds. Duplicate target destruction within a block is rejected. A run without a complete block marker pair reports `scoreStatus: unavailable`; target lifecycle `Destroyed` records are retained as supporting state and are never used as the primary score source.

The development fixture tests cover known counts, invalid-pose exclusion, hand-computed zero-degree aim error, duplicate score rejection, unsupported schemas, and unavailable scores when block markers are incomplete:

```text
PYTHONPATH=analysis/src python -m unittest discover -s analysis/tests -v
```

This lane is synthetic development only. It does not establish hardware calibration, physical timing, participant or safety acceptance, or any study gate.
