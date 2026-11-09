# Geometry module

`Elts.Geometry` is a pure, allocation-free-at-call-site geometry layer with
immutable double-precision values. It uses the ELTS room convention: Unity
left-handed coordinates, +X right, +Y up, +Z forward, and meters. The library
does not reference `UnityEngine`, OpenVR, or hardware APIs; tracking conversion
belongs to the Tracking module.

`Vector3d`, `Quaterniond`, `RigidPose`, and `Ray3d` provide finite checked
vector, rotation, pose, and ray operations. `BoreRay` applies a weapon pose,
muzzle offset, local bore direction, and zero correction to produce a world
ray. `GeometryMath.AngularErrorDegrees` computes the stable `atan2(|cross|,
dot)` world-space angle between the corrected bore ray and a muzzle-to-target
ray. `ScreenPlane` validates an orthonormal display basis, intersects rays, and
projects world points to bounded meter coordinates; parallel rays, behind-ray
intersections, zero vectors, nonfinite values, degenerate dimensions, and
invalid bases are rejected or reported as an off-screen result.

The APIs are pure and thread-safe after construction. Tolerances are centralized
in `GeometryTolerance`; they are numerical guards rather than physical
calibration claims. Geometry and standalone checks use synthetic coordinates
only. Run the executable checks with the installed .NET SDK:

```text
dotnet run --project tools/runtime-tests/GeometryChecks.csproj
```

## Review corrections and numerical contract

Vector and quaternion normalization scale components before computing a norm,
so large finite values remain valid directions instead of overflowing to zero.
A default quaternion is invalid and is rejected when constructing or using a pose.
Vector construction rejects nonfinite components, including arithmetic overflow.
ScreenProjection.Distance and ray intersection distance are both meters from the
eye/ray origin. Screen U/V are meter offsets from the lower-left origin, with a
centralized numerical edge tolerance. None of these values asserts calibration.

The console harness pins C# 9 and covers all four screen corners from an off-axis
eye, distance units, nonidentity zero correction, large finite normalization,
default-value rejection, transforms, known angles and invalid inputs. Actual Unity
compilation/import is still separately required. Work item: DEV-01; supports the
baseline geometry work without completing physical calibration or any gate.
