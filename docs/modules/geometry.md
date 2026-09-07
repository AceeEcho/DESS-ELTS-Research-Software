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
