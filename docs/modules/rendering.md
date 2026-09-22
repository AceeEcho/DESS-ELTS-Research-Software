# Synthetic rendering and replay

The development player opens two independent camera previews on one window.
The participant camera renders only layer 30 (stimulus); the operator camera
also sees layer 31 diagnostics. This is explicitly labeled display emulation,
not actual participant/operator display assignment. Study mode stays disabled.

The participant camera uses the staged rig's screen basis and a generalized
off-axis frustum. The projection tests include rotated screens and arbitrary eye
positions, and check all four corners through the actual Unity camera matrices.
The virtual training room is illustrative, in metres. The operator
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

Runtime targets are one consistent clothed character assembled from rounded Unity
meshes: jacket, shirt, trousers, shoes, hands, hair and face. The standing and
crouched silhouettes use the same virtual-metre dimensions as the head, body and
limb hit regions. Three visible cover styles are stacked crates, a concrete barrier
and steel drums. Fixed targets stand in front of the cover; moving targets run
behind it and repeatedly crouch and peek. Both participant and administrator
cameras render the same objects. Cover and actors have no active Unity colliders;
the deterministic shot model owns hit and occlusion geometry. This is synthetic
visual presentation, not a physical lighting or calibration model.

`DevelopmentView` exposes `Training Room` settings in the Inspector: width, height,
depth, floor offset, tile spacing, surface colors and daylight direction/intensity.
The default 12 x 26 x 6 metre room has pale walls, a tiled floor, overhead beams and
high daylight panels. A shadow-casting directional light shares its direction with
the target material. The old edit-mode floor scaffold is hidden during runtime.
The shell has no active colliders and does not change hit geometry or camera
projection. The open near end lets the administrator inspect the scene by orbiting.
