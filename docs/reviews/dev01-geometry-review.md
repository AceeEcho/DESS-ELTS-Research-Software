# DEV-01 geometry review

**Review target:** `38796f04430a02fd716ec814aa0ac291e1fe2542`  
**Reviewed snapshot:** `2af5c92` in the isolated review worktree  
**Scope:** Pure `Elts.Geometry` primitives and `GeometryChecks` only. No
production progress event, state, calibration, hardware, or Unity acceptance is
claimed by this review.

## Verdict: request changes before DEV-01 acceptance

The module correctly keeps targets in room/world coordinates and derives screen
coordinates from eye/display geometry, which is consistent with SCI-001 and
SCI-002. The bore calculation composes the weapon orientation with a local zero
correction before deriving a world ray. However, one Unity compilation blocker
and two invariant failures make the current implementation unsuitable as the
shared geometry foundation.

## Findings

| Priority | Location | Finding |
| --- | --- | --- |
| P0 | `unity/Assets/ELTS/Geometry/Geometry.cs:3` | The file-scoped namespace syntax requires C# 10. Unity 6's compiler documentation specifies Roslyn C# 9, so the configured Unity 6000.3.23f1 toolchain cannot compile this source as written. The standalone project targets .NET 10 and therefore does not validate Unity syntax compatibility. Use a block namespace. |
| P1 | `Geometry.cs:14-19`, `:47-53` | Large but finite inputs overflow the squared-norm calculation. `new Vector3d(1e308, 0, 0).Normalized()` returns `(0, 0, 0)` and `new Quaterniond(1e308, 0, 0, 0)` constructs `(0, 0, 0, 0)` instead of rejecting the invalid normalization result. `Ray3d` can consequently accept a zero direction. This violates the claimed finite/degenerate rejection. |
| P1 | `Geometry.cs:76-85` | `default(Quaterniond)` bypasses the constructor and is accepted by `RigidPose`, whose transformation methods then silently apply a zero quaternion. Public value types cannot prevent `default`; consumers need validation at use/construction boundaries, or the quaternion needs a unit-validity property that `RigidPose` enforces. |
| P2 | `Geometry.cs:103-109`, `:124-136` | `ScreenProjection.Distance` is not a distance in meters. `Project` stores the unnormalised line parameter `t`; in the supplied geometry test, eye `(0,0,0)`, screen plane `z=2`, and world point `z=3` yield `t = 2/3`, whereas `Intersect` returns a meter distance of `2` for the equivalent normalized ray. Rename it `RayParameter`, or project through a normalized ray and expose a clearly documented meter distance. |
| P2 | `tools/runtime-tests/GeometryChecks.cs:11-19` | The 19 checks do not cover the above overflow/default cases, a non-identity zero correction, exact lower-left/top-right screen corners, `Project` parallel/behind behavior, or the documented `Distance` unit. These are concise synthetic unit cases and should be added before accepting the primitive layer. |

## Reproduction and assessment

The compiled console assembly was loaded after its normal run. Reflection against
the public values produced:

```text
new Vector3d(1e308, 0, 0).Normalized() -> (0, 0, 0)
new Quaterniond(1e308, 0, 0, 0) -> (0, 0, 0, 0)
```

All source components are finite at input, so rejecting them solely because they
are non-finite is insufficient. Use a scale-safe norm (for example, divide by
the greatest absolute component before summing squares), then verify a finite,
non-degenerate normalized result. `RigidPose` must also reject an invalid
orientation supplied through a default value.

The C# compatibility conclusion follows Unity's Unity 6 compiler documentation,
which identifies its Roslyn language version as C# 9.0:
<https://docs.unity3d.com/6000.0/Documentation/Manual/csharp-compiler.html>.
File-scoped namespaces were introduced in C# 10. The specified 6000.3.23f1
Editor is installed locally, but this snapshot contains no Unity project in
which to perform an Editor compilation; the versioned compiler contract makes
the incompatibility actionable now.

## Validation performed

- `dotnet run --project tools/runtime-tests/GeometryChecks.csproj` completed:
  `PASS: 19 geometry checks`.
- Reviewed SCI-001, SCI-002, SCI-006 and SCI-008 plus the room-frame rule in
  `docs/reviews/2026-09-06/spec-extract.txt`. This pure module does not ingest
  tracking samples or logged data, so SCI-006/SCI-008 remain future tracking and
  rendering integration obligations.
- Checked ordinary synthetic vector, pose, bore, plane intersection and
  projection behavior through the committed checks. No physical measurement,
  calibration, Unity Editor compile, or hardware validation was performed.
