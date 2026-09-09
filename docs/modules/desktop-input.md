# Mouse and keyboard pipeline rehearsal

Open `unity/Assets/Scenes/ELTSDesktop.unity` in Unity **6000.3.23f1 LTS** and press
Play. The scene contains the runtime entry components; configured apparatus geometry,
cameras and the dashboard are created at runtime. Use the Game tab to interact.
The Windows shortcut is `OPEN-DESKTOP-TEST.cmd` after building with
`./scripts/build.ps1 -Output build/desktop-pipeline`. Output must be empty for a new
build. Prerequisites and local tool discovery follow the repository setup guide.

## Run the pipeline

1. In Preparation select **Mouse and keyboard** before creating a recording.
   Enter a unique synthetic test identifier and arrange the four conditions.
2. Create the synthetic recording and wait for readiness. Continue to calibration,
   generate the development fixture, review it, and accept it.
3. Continue through the ten-second practice timer. Open desktop play from Preparation
   or the practice/blocks module. Choose **Start block** in the play view when ready.
4. Point at targets and click. A shot occurs when the left button is released after
   a press inside the image during the same running block. The HUD shows hits and
   shots. The standard four 300-second blocks and five-second development breaks
   still use the session controller; the desktop mode does not shorten them.
5. Use the play view's phase action between blocks. After completion, use Recordings
   to find the run or open replay. Files remain under `data/synthetic` relative to
   the player (under `unity/data/synthetic` in the Editor).

| Control | Action |
| --- | --- |
| Mouse | Aim at a point on the fitted participant image |
| Left click and release | Fire during a running block |
| W / S | Move the virtual eye toward / away from the screen |
| A / D | Move the virtual eye left / right |
| Q / E | Move the virtual eye down / up |
| R | Reset the virtual pose |
| Esc / F1 | Return to administrator controls |

Leaving the view or losing focus cancels a held click. Session time continues;
returning to the administrator is not a pause. Letterbox bars and menu clicks do
not fire. Before a recording, the view is a geometry preview. Scored firing is
enabled during running blocks. The existing practice phase remains a timed
preparation phase. Select **Automated synthetic motion** before a new recording to
use the earlier automatic fixture instead.

## Source and future hardware boundary

`DesktopInputModel` derives room-space poses from the configured screen basis,
head-eye offset, muzzle offset, weapon zero and bore direction. Its named constants
define movement speed (0.35 metres/second), initial eye distance (2 metres), and
bounded movement. Geometry is synthetic and unmeasured. The virtual muzzle is
coincident with the virtual eye so a conventional mouse reticle predicts the shot;
this is a development convenience, not a physical weapon model or study display.

`MouseKeyboardControls` reads Unity Input System devices. `IDesktopControls` allows
deterministic input injection for tests. `DesktopTrackingSource` implements the
existing `ITrackingSource` interface, publishing head/weapon pairs under a shared
acquisition stamp. Focus loss publishes unavailable poses and stale input expires.
The acquisition worker serializes raw admission with trigger capture. Each desktop
`InputTrigger` identifies its raw acquisition sequence; scoring uses that exact pair.
`DevelopmentInputSource` records the source identity and synthetic muzzle policy.

Future equipment should supply an `ITrackingSource` and a device trigger adapter
at this boundary. Keep calibration, shared clock, session controller, condition
rendering, hitscan, recording and replay downstream. Hardware timestamps, coordinate
transforms, tracker bindings, trigger timing, calibration and physical validation
must be established separately before replacing the desktop source for research.
The apparatus is built; the required tracking/testing equipment is unavailable.
This mode provides no physical calibration, safety approval or G0-G8 acceptance.

## Verification

`DesktopTrackingChecks` exercises source validity, ordering, stale-input behavior
and paired timestamps. Unity `DesktopPipelineTests` advances an injected shared
clock through all four conditions, moves the virtual head, fires and hits targets,
checks focus cancellation and expired-block rejection, closes real recordings,
matches triggers to raw samples, and loads replay. A separate fixture injects
virtual Unity mouse/keyboard events to test device mappings. These are synthetic
tests; physical mouse/keyboard operation in a visible Windows player is a separate
manual check. Test clocks accelerate the test, not normal gameplay.

Run `./scripts/test.ps1 -Suite unity-play` and `dotnet run --project
tools/runtime-tests/DesktopTrackingChecks.csproj`. Reports and retained test runs
are local diagnostics, not study evidence. An opt-in built-player render probe uses
`-eltsSessionScreenshot ABSOLUTE_NEW_PNG -eltsShowDesktop`; it injects stationary
presentation input and requests camera renders for hidden-window capture. It does
not create a recording or prove physical input operation.
