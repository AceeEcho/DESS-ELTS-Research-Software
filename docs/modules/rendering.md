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

## Window and floor perspective

The development rig assumes the display's lower edge is **4 feet (1.2192 m)**
above the floor. `screenBottomHeightAboveFloorM` in the rig configuration is the
single source for this assumption. `RigConfiguration.VirtualFloorY` derives the
horizontal floor from the configured screen-bottom midpoint; existing tracked
poses and screen corners are not shifted or rescaled.

The participant camera still uses the physical screen rectangle and eye position
to form an off-axis window. It does not widen a game-camera field of view to make
targets look smaller. A ray through the lower screen edge continues to the same
virtual floor plane, and moving the eye changes parallax around that window.
The room, bot feet, crouch interpretation, shot regions and cover bases all use
the same floor height. This is a synthetic room assumption, not a measured
registration of the physical room.

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

Runtime targets use the sourced Quaternius CC0 skinned mannequin with an ivory
shell and charcoal joints. Standing height is fitted to the synthetic anatomy.
Idle, walk, jog, crouched idle and crouched walk clips blend on its skeleton;
crouching bends joints rather than scaling the character. Foot-height correction
keeps the supporting foot at the fitted floor height, and the visual child turns
toward travel. Distance drives stride phase and speed drives gait weights.
Reading desks and low bookcases provide cover between open aisle positions.
Both participant and administrator cameras render the same objects. Cover and
actors have no active Unity colliders; the deterministic shot model owns hit and
occlusion geometry. This is synthetic visual presentation, not a physical lighting
or calibration model.

`DevelopmentView` exposes `Mannequin Animation` settings for gait speeds in
metres/second, blend duration and turn speed. Session active time drives animation:
pauses retain the exact pose and resumes continue from it. Replay seeks reset
the presentation blend; skeletal poses are illustrative and are not recorded
tracking data. The scenario root stays at the supplied position. The existing
analytic head/body/limb volumes remain authoritative, so the animated mesh is
not a pixel-exact hit mask. Root motion, autonomous animation playback and visual
colliders are disabled.

`DevelopmentView` exposes `Training Room` settings in the Inspector: dimensions,
surface colors, ambient fill, ceiling bounce and daylight direction/intensity.
The enclosed 30 x 52 x 4.8 metre library uses scanned oak, woven carpet and plaster,
three circulation aisles, perimeter study tables/chairs, a quiet-study alcove,
framed glazing, detailed collections and suspended lights below acoustic rafts.
Poly Haven CC0 asset sources and hashes are in `licenses/PolyHaven-Library/README.md`
and included with the player. The room is still procedural virtual scenery.

URP renders soft directional shadows, ambient occlusion, a soft ceiling fill and
neutral tonemapping with restrained bloom/color adjustment (`Resources/ELTS/LibraryColor`).
The shell receives lighting but does not cast shadows: this is a daylight/bounce
approximation, not baked or physically measured illumination. Existing scene lights
are temporarily disabled and restored when the development view is destroyed.
The old floor scaffold is hidden during runtime. All four walls and the ceiling
are closed; decorative windows are luminous surfaces. Camera projection is unchanged.
The serialized `LibrarySurface` and `LibraryEmission` resource materials retain
normal-map and emissive shader variants in player builds. Rebuild those templates
with **ELTS > Development > Rebuild library material templates** if their source
shader changes.

Reading desks have separate tabletop, support and modesty-panel hit volumes so
the visible gap underneath remains open to shots. Bookcases retain closed backing.

Thirty dummies use 50 positions among open aisles, low bookcases and reading desks.
Furniture in the target field is rendered from the same bounds used by the shot
model; perimeter decorations remain presentation only. See [scenario behavior](scenario.md)
for fixed replacement and the versioned moving-route sequence.
