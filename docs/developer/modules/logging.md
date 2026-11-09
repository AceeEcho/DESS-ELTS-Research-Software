# ELTS logging v1

`Elts.Logging` records synthetic and future study sessions through four versioned products. It has no UnityEngine dependency and accepts only already-converted room-frame tracking poses. All timestamps are `MonotonicTimestamp` ticks (100 ns elapsed time from the shared process clock); the UTC startup anchor is provenance only.

| Product | File | Purpose |
| --- | --- | --- |
| Samples | `samples.ndjson` | Paired head and weapon observations from one capture stamp (`elts.samples.v2`). Each tracker is either `{"pose":[x,y,z,qx,qy,qz,qw]}`, the raw converted pose in room metres with a unit quaternion, meaning `Connected` and `Valid`, or an explicit `connection` and `validity` with no pose. Each tracker's `trackerId` is written on the stream's first line and again whenever it changes; it holds until its next record. Numbers keep full round-trip precision. |
| Targets | `targets.ndjson` | Target lifecycle records with canonical Unity room-frame world position and velocity in metres and metres/second. Screen coordinates are never stored. |
| Events | `events.ndjson` | Typed session events with a flat, typed scalar payload. Callers cannot inject arbitrary JSON. |
| Closure | `session-summary.json` | `elts.session-summary.v2`: completion state, errors, source/config/scenario/fixture hashes, counts, SHA-256 checksums of all three data streams, and `supplementaryChecksumsSha256` for every other file in the run folder at closure (participant, calibration and checkpoint files). A file written after closure is not listed. |

Schemas live in `schemas/logs/`. A reader must reject an unsupported schema identity, a missing stream, or a checksum mismatch. Invalid tracking poses remain invalid; analysis must exclude them and must never interpolate them into a primary measure.

## Writer behavior

`SessionLogWriter` is the only owner of open output files. Tracking calls use a bounded queue and never wait for disk I/O. A full sample queue increments `DroppedSampleCount`; callers should surface that count as an operator warning. Target and event records use a separate bounded critical queue. If it cannot accept one, the writer becomes unhealthy and the session must fail closed.

`LoggingRunDirectory.ReserveUnique(dataRoot, runId)` refuses a run folder too deep for a 259-character Windows path plus the longest ELTS run file name (48 characters), so the problem appears before recording rather than when a file is saved at the end. It validates a portable run ID, rejects reparse points in the path, creates a retained reservation file, and chooses `-r0001`, `-r0002`, and so on for reruns. Files use `FileMode.CreateNew`; existing raw data is never opened for writing. Managed .NET does not expose a portable atomic directory-create primitive, so the reservation serializes compliant ELTS writers; deployment storage must not be shared with arbitrary writers.

`Close()` drains both queues, performs a durable flush, closes the product files, and computes checksums. It writes and durably flushes `.session-summary.pending.json` before publishing `session-summary.json` with a same-directory move that cannot overwrite an existing final file. Readers ignore the pending file. A failure or close timeout before publication cancels publication, so partial output cannot become a completed run later.

`LogCloseResult.Completed` means the writer thread joined within the requested wait. `CompleteOutput` means successful final publication won. Publication can win just before the thread exits, giving `Completed=false` and `CompleteOutput=true`. Terminal publication and cancellation share a lock; an operating-system stall during the final move can extend the close wait, so this is not a hard real-time bound.

## Per-test durable checkpoints

The browser station calls `SessionRecordingAdapter.CheckpointAsync()` after each
test. The writer places one FIFO marker in the sample queue and another in the
critical queue. Once both reach the writer, every record accepted before the
checkpoint has been written; the writer durably flushes all streams before
acknowledging success. Later acquisition continues in the same recording. A queue,
write, durable-flush or timeout failure cannot acknowledge a successful checkpoint.

The administrator then publishes a supplementary `test-checkpoint-<attempt>.json`
through a durable pending-file write and no-overwrite move. It records which test
was saved, its status, timing, save time and dropped samples, and no result counts:
results are calculated once, from the recording's events. Its `finalized:false`
field distinguishes this recovery/review evidence from `session-summary.json`.
Only ordinary session closure computes final stream checksums and enables the
existing completed-run analysis path. Checkpoints never overwrite or truncate raw
data, and an interrupted session without a final summary still requires inspection.

## Focused checks and soak

From the repository root:

```text
dotnet run --project tools/runtime-tests/LoggingChecks.csproj
dotnet run --project tools/runtime-tests/LoggingThroughputChecks.csproj
dotnet run --project tools/runtime-tests/LoggingPublicationChecks.csproj
dotnet run --project tools/runtime-tests/LoggingChecks.csproj -- --soak-seconds 1800 --output test-results/dev04-soak
```

The normal harness validates output JSON round-trips and file checksums, run-ID/no-overwrite behavior, sample drops, critical-event fail-closed behavior, injected write failure, and injected stalled close. The soak entry point produces samples at the configured 250 Hz cadence for real wall-clock duration, prints its output directory and counters, and does not claim physical device or Study-PC disk validation.

The test seams are `ILogSinkFactory` and `ISessionSummaryPublisher`. They support deterministic failure/stall tests; production uses the default file implementations and does not permit a caller to supply log-file paths.

Soak evidence also records the actual Git revision, runtime-source hashes, compiled harness hash, fixture/configuration hashes, elapsed duration, effective cadence, scheduling delays and producer enqueue timings. Fixture unit tests use explicit fixture provenance; the sustained run records its actual inputs. Busy waiting in this diagnostic producer aims for 250 Hz on Windows and can use one CPU core. It does not establish full Unity/display-load or hardware timing acceptance.
## Target lifecycle compatibility

New writers emit `elts.targets.v2`. Version 2 adds `Despawned` for a target that expires without a `TargetDestroyed` event. A target's state holds until its next record: the synthetic scenario writes `Updated` only when the target's world position or velocity changes exactly, so a stationary target has just its `Spawned` and terminal rows. Readers already use the latest snapshot at or before each timestamp, so this loses no information. Readers accept an entirely v1 or entirely v2 `targets.ndjson` stream and reject a mixed stream. V1 remains strict and does not accept `Despawned`. Replay and analysis remove both `Destroyed` and `Despawned` snapshots from active aim candidates, but only a `TargetDestroyed` event contributes to the scored target-destruction count.
