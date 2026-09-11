# Synthetic rendering and replay

The development player opens two independent camera previews on one window.
The participant camera renders only layer 30 (stimulus); the operator camera
also sees layer 31 diagnostics. This is explicitly labeled display emulation,
not actual participant/operator display assignment. Study mode stays disabled.

The participant camera uses the staged rig's screen basis and a generalized
off-axis frustum. The projection tests include rotated screens and arbitrary eye
positions, and check all four corners through the actual Unity camera matrices.
The virtual floor, posts and corner rods are illustrative, in metres. The operator
view shows head/weapon markers, a separate eye marker, the screen, frustum rays,
zero-corrected bore, muzzle-to-target ray, and angular error. An out-of-screen
bore is red. Invalid tracking remains invalid, shows a warning and pauses the
participant camera when the head pose is unavailable. No base station is attached.

Use the bottom controls to select synthetic demo motion, simulate head/weapon
dropouts, edit temporary screen X/depth offsets, reset the layout, and adjust
render-only head prediction. Persistent layout changes belong in the existing rig
configuration under `config/rig/`; temporary sliders do not save calibration.
Camera margins and diagnostic dimensions are named constants in `DevelopmentView`.
Drag in the operator preview to orbit, shift-drag to pan, and use the wheel to zoom.

Load a completed v1 run folder to replay its raw paired samples and target
snapshots. Play, pause and the time slider use recorded monotonic ticks. Invalid
poses are never interpolated. Replay uses zero head prediction; demo prediction
returns a separate immutable pose and cannot modify the raw observation. The
weapon always remains raw. When the recorded configuration hash differs, a warning
states that the geometry uses the currently staged synthetic configuration.
Replay is a bounded development viewer, not primary analysis; see
`replay-reader.md` and `analysis.md` for their separate contracts.

From the repository root:

```powershell
dotnet run --project tools/runtime-tests/RenderingChecks.csproj
./scripts/test.ps1 -Suite unity-play
./scripts/build.ps1 -Output build/rendering-development
```

The build output must be new. Supply the wrapper's `-PythonExecutable` or
`-UnityEditor` argument when those prerequisites are not on the current machine's
default paths. Use the exact pinned Unity 6000.3.23f1 Editor.

For an opt-in standalone graphical probe, launch the resulting player with
`-eltsViewScreenshot ABSOLUTE_NEW_PNG_PATH`. It exercises layout, prediction and
dropout handlers, renders the two real cameras to an offscreen PNG and exits with an explicit log
marker. This is separate from the non-graphical bootstrap smoke probe. Runtime
tests exercise the same control methods as the UI; they do not establish native
mouse/keyboard interaction or physical display assignment. The PNG captures camera
geometry; the IMGUI controls are excluded from this offscreen render.

Deferred evidence includes measured screen geometry, physical corner rods at
multiple viewing positions, head/weapon mounts, real tracking and base station
diagnostics, actual output assignment, and full-load frame timing at the study
refresh rate. Synthetic rendering does not pass those physical criteria.

## Synthetic target appearance

Runtime targets use `DevelopmentTarget.mat` and `DevelopmentTarget.shader` under
`unity/Assets/ELTS/Operator/Resources/ELTS`. The shaded surface has a world-fixed
key-light direction, ambient fill, a view-dependent highlight and object-fixed
surface bands. The bands wrap around the sphere instead of following the camera,
so movement around a stationary target is visible. Material properties expose the
colors, marking width/strength and lighting values in the Unity Inspector.

A shared smooth sphere mesh has unit diameter; the existing scenario target radius
still determines its size in metres. `DevelopmentView` exposes longitude/latitude
resolution in the Inspector (defaults 64/32). Rendering changes do not alter target
positions, hit testing, timing or recorded coordinates. Both participant and
administrator cameras render the same target surface. This is synthetic visual
presentation, not a physical lighting or calibration model.
