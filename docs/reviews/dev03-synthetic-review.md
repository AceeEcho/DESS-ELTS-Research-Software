# DEV-03 synthetic tracking review

**Reviewed revision:** `736d0f3510e55afc54ffb2043aff53626b9d6e90` (`Bound synthetic tracking acquisitions and trigger queues`)

**Scope:** `SyntheticTrackingSource`, its deterministic check harness and module
contract, plus the imported shared tracking contracts. This review did not modify
runtime implementation, Unity project files, packages, state, or events.

## Verdict: request changes

Two related timing defects mean that a synthetic observation can be labeled with
one shared-clock time while its schedule and motion state use different time
values. This makes a delayed poll's raw pose inconsistent with its recorded
monotonic timestamp. It should be corrected before DEV-03 is treated as
reviewed implementation evidence.

| Priority | Location | Finding |
|---|---|---|
| P1 | `unity/Assets/ELTS/Tracking/SyntheticTrackingSource.cs:136-147` | `EnsurePending` reads `Clock.Now` to decide whether an acquisition is due, then calls `TrackingAcquisitionStamp.Capture`, which reads the clock a second time. In a clock that advances between reads, the first stamp is later than the schedule basis and the next poll counts an interval as skipped. The review harness reproduces this with a 4 ms stepping clock: stamps are 4 ms and 12 ms while `SkippedAcquisitionCount` becomes 1. Capture one timestamp per poll, and build the acquisition stamp from that captured timestamp instead of rereading the clock. |
| P1 | `unity/Assets/ELTS/Tracking/SyntheticTrackingSource.cs:157-162` | The motion phase uses `sampleNumber / SampleRateHz`, not the acquisition's monotonic timestamp. After a 100 ms delay at 250 Hz, the emitted sample has a 100 ms timestamp, reports 24 skipped acquisitions, but uses the 4 ms trajectory phase. This conflicts with raw pose provenance and the one-clock/synchronized-sample intent in SCI-005 and the samples product contract. Derive fixture motion time from the captured acquisition timestamp. Keep sequence numbering and skipped-count reporting separate from pose time. |

The source currently uses the injected clock object in both reads, so this is not
a second clock authority. The defect is two observations from the same authority
within one acquisition. The paired head/weapon samples correctly reuse the
single resulting acquisition stamp.

## What passed review

- The source exposes the required synthetic identity and produces paired samples
  with matching sequence and timestamp.
- The source bounds pending tracking observations to one pair and bounds trigger
  events by configured capacity, recording overflow counts.
- Disconnecting a pressed trigger resets edge history; the existing fixture
  proves that a dropout/reconnect does not synthesize a falling-edge event.
- Dropout and trigger windows reject their default structs. Sample-rate, NaN,
  infinity, upper-bound and zero trigger-capacity validations were exercised.
- A zero-amplitude synthetic fixture remains stationary, and the source carries
  raw room-frame poses only. It does not introduce a predicted pose.
- Source syntax and APIs compiled as C# 9 against `netstandard2.1`. This is a
  compatibility proxy, not proof of the selected Unity Editor's import/runtime.

## Evidence

Commands run against the reviewed revision:

```text
dotnet run --project tools/runtime-tests/SyntheticTrackingChecks.csproj
PASS: 73 synthetic tracking checks

dotnet run --project tools/runtime-tests/TrackingChecks.csproj
PASS: 25 tracking checks

dotnet run --project tools/runtime-tests/Dev03ReviewChecks.csproj
PASS: DEV-03 review counterexamples reproduce split-clock scheduling and sample-index motion conditions

dotnet build tools/runtime-tests/Dev03UnityCompatibility.csproj
Build succeeded. 0 Warning(s), 0 Error(s)
```

Reviewed source hashes:

```text
D1C86C504C40EE380F51C45814C6271E6DDB72007ACDA264C4D8919C5F450949  unity/Assets/ELTS/Tracking/SyntheticTrackingSource.cs
FD17D490FCFC4BE64C96BA685101D9D4CDDE050147EEB9C786B15C5300B848A3  tools/runtime-tests/SyntheticTrackingChecks.cs
F51C65202D9F8AD7B08B89844D31437278146C3BF968E383E6ED16CFC2979C14  tools/runtime-tests/TrackingChecks.cs
C89D38CECED018431834DCA16C4E393A936F76875315546A545FA44FD228F93D  unity/Assets/ELTS/Tracking/TrackingContracts.cs
```

The counterexample programs are review-only console projects. They do not
assert a real tracker rate, OpenVR behavior, Unity 6000.3.23f1 import, hardware
calibration, physical trigger timing, or cross-device synchronization.

## Remediation assessment — `17255e4737b7b6a400f745e0d9bc776e3732cdb2`

The timing remediation was cherry-picked for independent verification (review
worktree commit `e03d825`). Both P1 findings are closed for the corrected source
hash below. The original verdict above remains historical evidence for revision
`736d0f3`; the corrected candidate is approved for the timing findings reviewed
here.

- `EnsurePending` now creates `candidateStamp` once, derives `now` from that
  stamp, and uses the same value for due-time checks, skipped-interval accounting,
  sample sequence, paired samples, and trigger timing. The previous split-read
  counterexample now emits stamps at 0 ms and 4 ms with zero skipped intervals.
- Synthetic pose phase now derives from the acquisition timestamp and applies
  `2 * pi * Hz * seconds` consistently for head sway, weapon sway, and weapon
  yaw. A delayed 100 ms acquisition produces the 100 ms pose phase; a one-hertz
  fixture reaches its positive peak at 250 ms. An exact 4 Hz poll and a poll one
  tick late both report zero skipped acquisitions, while a 100 ms gap at 250 Hz
  reports 24 skipped intervals.
- The prior review's queue, dropout, invalid-sample, raw-pose, and C# 9
  compatibility observations remain unchanged. No predicted pose was added.

### Remediation evidence

```text
dotnet run --project tools/runtime-tests/Dev03ReviewChecks.csproj
PASS: DEV-03 review regressions verify single-capture scheduling and timestamp-based Hz motion

dotnet run --project tools/runtime-tests/SyntheticTrackingChecks.csproj
PASS: 87 synthetic tracking checks

dotnet run --project tools/runtime-tests/TrackingChecks.csproj
PASS: 25 tracking checks

dotnet build tools/runtime-tests/Dev03UnityCompatibility.csproj
Build succeeded. 0 Warning(s), 0 Error(s)
```

Corrected reviewed source hashes:

```text
5A319D24394CADB56609EB86EDF10F08A04143A806D95DB07F226C2B70B3C602  unity/Assets/ELTS/Tracking/SyntheticTrackingSource.cs
438206090CC917EFDC21E0614E4BF1C144E8B70BBEE80B22EDACCBACBC18EA3B  tools/runtime-tests/SyntheticTrackingChecks.cs
F51C65202D9F8AD7B08B89844D31437278146C3BF968E383E6ED16CFC2979C14  tools/runtime-tests/TrackingChecks.cs
C89D38CECED018431834DCA16C4E393A936F76875315546A545FA44FD228F93D  unity/Assets/ELTS/Tracking/TrackingContracts.cs
```

This assessment verifies only the software timing corrections under deterministic
clocks. Unity 6000.3.23f1 import/runtime, OpenVR operation, physical tracking,
trigger timing, calibration, and cross-device synchronization remain outside its
scope.
