# Close viewing and the fixed-window illusion

Investigation date: 2026-11-16. The reported symptom occurs while physically
approaching the TV with live tracking. This investigation reads the current
browser and Unity code, inspects the active calibration UI, and exercises
synthetic geometry. It does not measure the physical screen, eye position,
tracker accuracy or end-to-end latency. The findings below describe the code and
active settings before the correction. The implementation and software checks
are recorded at the end; existing physical calibration values were not rewritten.

## Findings

The new observation points to a mismatch between the physical viewing geometry
and the geometry supplied to the renderer. It does not, by itself, establish an
incorrect off-axis projection formula. A concrete related defect is reproducible:
**Reset head in front of screen can rotate the entire tracking-to-room coordinate
system to cancel the current head pitch and roll.** The active unsaved profile
also has significant registration tilt, despite an upright configured screen.

That is a stronger lead than adding a close-distance clamp or changing scenery.
The actual size and sign of any physical eye-distance error remain unknown.

### Projection and scene placement

- `Calibration Environment/web/math.js`, `offAxis`: the camera position is the
  calculated eye; its orientation follows the screen; the frustum uses physical
  screen dimensions and perpendicular eye distance. The near/distance factor is
  part of the standard construction, not an extra zoom applied afterward.
- `Calibration Environment/web/scene.js`: `update` calculates the eye from the
  head pose and eye offset, then applies `offAxis`. The virtual room and target
  are placed by screen/room configuration, not moved or scaled with the head.
  Resizing the preview does not replace the custom projection matrix.
- `unity/Assets/ELTS/Rendering/ProjectionModel.cs` and `OffAxisCamera.cs` use the
  same window geometry in Unity's coordinate convention. Unity places the near
  plane just before the screen; the browser uses a fixed 0.02 m near distance
  by default. These different clipping choices do not change the screen
  coordinates of visible points.
- The detached browser screen fills its viewport. Correct physical mapping
  still requires it to fill the TV's actual illuminated area, with matching
  configured dimensions. That physical mapping was not measured here.

The physical display dimensions and observer position are the required inputs
to generalized perspective projection; see the official
[PsychoPy view-transformation documentation](https://psychopy.org/api/tools/viewtools.html#psychopy.tools.viewtools.generalizedPerspectiveProjection).
Head-coupled off-axis views depend on the actual viewer position, as described
in [Rekimoto's fish-tank VR paper](https://www2.sonycsl.co.jp/person/rekimoto/papers/vrais95.pdf).

### Why shrinking in pixels is not the same as receding from the eye

For a centred, fronto-parallel object of width `H`, at depth `Z` behind the screen,
and an eye `d` in front of it, similar triangles give:

```text
physical width of the image on the TV = H * d / (d + Z)
angle subtended at the physical eye  = 2 * atan(H / (2 * (d + Z)))
```

As the eye approaches, the image takes up fewer millimetres/pixels on the TV.
The TV itself is closer, so the angular size at the eye increases. Both are
necessary for a stationary world behind a real window.

Synthetic example: a 1 m object, 5 m behind the TV.

| Eye-to-TV distance | Image width on TV | Angular width at eye |
| --- | --- | --- |
| 2.00 m | 28.57 cm | 8.17 degrees |
| 0.50 m | 9.09 cm | 10.39 degrees |
| 0.25 m | 4.76 cm | 10.88 degrees |

Changing the virtual head while keeping the real eye still would remove this
compensation, but the user has explicitly confirmed physical movement with live
tracking. That makes the actual registered eye position the relevant question.

### How a distance error reproduces the reported pattern

Let `d_actual` be the true perpendicular eye-to-TV distance and `d_render` the
distance used by the camera. Assume matching physical screen dimensions, a
centred eye, and no other errors. The image's angular size corresponds to a
same-sized object at this equivalent depth behind the TV:

```text
Z_equivalent = Z * d_actual / d_render
```

If the rendered eye is too close by a fixed amount, its distance can approach
zero while the real eye is still a finite distance from the screen. Equivalent
depth then increases without bound in the mathematical limit. The running
browser stops rendering at its near limit, so it does not literally render at
zero distance.

The diagnostic reproduces this with an **invented 0.20 m error**, not an estimate
of this apparatus:

| Actual distance | Rendered distance | Equivalent depth for the 5 m object |
| --- | --- | --- |
| 2.00 m | 1.80 m | 5.56 m |
| 0.50 m | 0.30 m | 8.33 m |
| 0.25 m | 0.05 m | 25.00 m |
| 0.23 m | 0.03 m | 38.33 m |

At 0.25 m, the correct object subtends 10.88 degrees; the incorrect one subtends
only 2.27 degrees. “Equivalent depth” is a geometric size comparison, not a
measurement of subjective perception. This is a mechanism matching the symptom,
not proof that this exact error exists on the TV.

Clamping distance or field of view would conceal the approach to the erroneous
plane, while leaving the physical and rendered rays mismatched elsewhere.

## Active settings and the reset defect

Read-only inspection of the most recently active calibration tab found:

| Setting | Observed value |
| --- | --- |
| Input source | Live SteamVR trackers; head pose valid |
| Screen width / height | 1.44 m / 0.81 m |
| Screen pitch / yaw / roll | 0 / 0 / 0 degrees |
| Active registration pitch / yaw / roll | 1.3166 / 81.7645 / -10.2219 degrees |
| Saved participant profile registration | 0 / 83.6928 / 0 degrees |
| Head-centre-to-tracker translation | 0 / 0 / 0 m |
| Head-centre-to-eye translation | 0 / 0 / -0.09 m |
| Reset distance field | 5 ft, interpreted as head-centre distance |
| Reset height field | 2 ft 6 in, interpreted as head-centre height |

The page explicitly indicated unsaved changes. Saved-profile inspection alone
would therefore have missed the active tilt. These are configuration values,
not verified physical dimensions or an assertion about how the tracker was
being worn during the original symptom. No values were edited or saved.

The active transform sends native gravity-up about **10.31 degrees** away from
configured room-up. Applying that transform to a hypothetical native 30 cm
upward movement produces approximately:

```text
room right: +1.43 cm
room up:    +29.52 cm
room depth: -5.17 cm (toward the configured screen)
```

Thus vertical movement also changes perspective distance. This is a direct
mathematical consequence of the observed settings, assuming SteamVR's standing
up direction represents gravity as the application expects.

In `math.js`, `recenteredRegistration` composes the target head pose with the
inverse of the full current head pose and the existing registration. The target
orientation is the screen orientation. As a result, resetting with temporary
pitch/roll, or an incorrect mount orientation, can transfer that discrepancy
into the entire room registration. It rotates translations for both trackers.

The diagnostic starts with a level registered room, supplies a head pitched
15 degrees, and calls the current reset. The room acquires a 15-degree tilt;
a synthetic 30 cm crouch then changes screen distance by 7.76 cm. This is a
reproducer of current undesirable behavior, not a desired regression invariant.

The active tilt is consistent with this path, but there is no captured action
history proving how that particular unsaved transform was created. The guided
direction calibration derives axes from standing-up and lateral movement for
an upright screen; a subsequent ordinary reset can undo that alignment.

Zero head-mount translation also means that the renderer treats the tracker
origin as the head centre and places the eye only 9 cm ahead in the recovered
head frame. Whether that matches the actual fit is unknown. A measurement to
the eye entered into a field interpreted as head-centre distance would subtract
the eye offset again; the opposite convention creates the opposite error.
Offsets must be assessed by their sign and reference point, not blindly enlarged.

## Connection to the earlier realism problem

The room should remain fixed relative to the real TV and room while the visible
portion changes with the observer. Previously added floor markings, contact
shadows and familiar-size objects help depth cues, but they cannot repair an
incorrect eye position or a tilted tracking coordinate system.

The older corner and ray-intersection checks establish that projection works
for the coordinates supplied to it. Even a badly misregistered eye can pass
every screen-corner check. They do not establish that the supplied coordinates
match the person and TV. Close viewing is particularly revealing because a
fixed position error becomes a large fraction of the remaining distance.

There are two secondary limitations:

- The browser polls poses every 50 ms after each request finishes, giving at most
  about 20 fresh pose responses per second even if rendering runs faster.
  Sampling and display delay can make a moving scene feel detached. They do not
  explain a persistent depth collapse after the head stops. Actual latency was
  not measured.
- A single ordinary TV image cannot supply a separate correct perspective for
  both eyes or optical focus at each virtual depth. Correct geometry can improve
  the window effect without reproducing every binocular cue of a real window.

## Recommended correction

1. **Separate room registration from head recentering.** Once the screen and room
   axes are calibrated, a position reset should preserve that orientation.
   Head pitch/roll and mount orientation must not redefine gravity or the TV.
   If heading alignment is needed, make it a distinct operation with an explicit
   reference; simply zeroing Euler angles in an arbitrary transform is not a
   complete correction.
2. **Make the eye reference explicit.** Fit the tracker-to-eye transform using
   clearly identified physical reference points. Expose the rendered perpendicular
   eye-to-screen distance beside the reset measurement, and distinguish eye,
   head centre and tracker origin. A direction-only wizard cannot independently
   establish the screen plane's absolute distance.
3. **Validate physical and rendered distance together.** When measurement is
   available, compare at several known positions and check horizontal, vertical
   and forward movement separately. Confirm the image fills the measured active
   screen area. This distinguishes an additive distance bias, axis error, and
   output-size mismatch. Do not tune movement sensitivity or a minimum distance
   to compensate for those errors.

The screen-projection model should be retained unless these checks produce
evidence against it. Fixing reset alone prevents one source of corruption but
does not supply missing physical eye/screen measurements or repair an already
incorrect calibration automatically.

## Implemented correction and verification

- Ordinary position reset now changes registration translation only. Room and
  head rotation are preserved, including when the user nods or rolls during reset.
- Reset and the direction guide explicitly use either the eye or head centre as
  the distance/height reference. New drafts use the eye; older drafts retain the
  head-centre meaning of their stored measurements.
- The eye controls expose the direct tracker-to-eye vector in head axes. Moving
  the schematic head-centre reference preserves that vector and the live eye
  position. The profile format and existing profile geometry are unchanged.
- The dashboard shows signed perpendicular eye-to-screen distance and flags
  tilted room registration. The direction guide keeps room up aligned with
  SteamVR standing up even when the configured screen is pitched or rolled.
- Both browser and Unity projection models accept positive eye distances below
  their usual near-plane setting. The near plane moves in front of the display;
  the eye position and image magnification are not clamped.

For the 1 m object 5 m behind the screen, the model's visual angle approaches
`2 * atan(0.5 / 5)`, about **11.42 degrees**, as eye distance approaches zero.
It does not approach zero or infinity. Exactly on the plane the single-plane
projection is degenerate; finite display pixels also limit very close viewing.
The test checks the positive-distance limit, not an eye physically touching a TV.

Run from the repository root:

```powershell
node tools/diagnostics/audit_window_projection.mjs
npm --prefix "Calibration Environment" test
dotnet run --project tools/runtime-tests/RenderingChecks.csproj
```

Initial investigation results (before fixes):

- New diagnostic: **278 numerical comparisons**, plus visibility and monotonicity
  assertions. Includes eyes down to 2.1 cm, offset eyes, rotated screens,
  independent ray/plane intersections, retinal angular size, clip-plane
  independence, the distance-bias example and the reset-tilt reproducer.
- Existing browser geometry/perspective tests: **12 passed**.
- Existing C# rendering checks: **809 passed**.

The initial C# checks exercised the plain numerical model. No new Unity player
was built. These results do not constitute physical validation of the apparatus.

After the correction:

- `npm test`: **25 tests passed**, including the eye/head reference, tilted-screen
  direction guide, nod/roll reset and finite close-view angular-size regressions.
- Projection diagnostic: **282 numerical comparisons passed**, plus visibility
  and monotonicity assertions; model distances extend down to 1 micrometre.
- C# numerical rendering suite: **827 checks passed**.
- Unity **6000.3.23f1** Play Mode: **3 camera tests passed**, including the actual
  camera matrices at eye distances down to 1 mm. Report:
  `test-results/window-fixes/playmode-tests.xml`.
- Chromium interaction checks passed for reset, all seven direction-guide steps,
  draft migration, direct eye measurement, profile round-trip, detached screen
  fullscreen and desktop layout. The general browser check reports 17 checks
  and no page errors. Tests used an isolated API with temporary profiles and
  synthetic tracker responses; they did not change active participant profiles.

Refresh the dashboard and detached screen to load the browser correction. Old
tilted registration needs the direction guide repeated; reset alone intentionally
does not flatten it. Enter measured tracker-to-eye offsets and eye/head reset
measurements. The Unity source was tested in the Editor; an existing standalone
player was not rebuilt. Physical accuracy and realism remain unverified.
