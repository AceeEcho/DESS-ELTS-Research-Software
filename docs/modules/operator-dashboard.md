# Administrator dashboard

For a step-by-step walkthrough with screenshots, see the [simple user guide](../operator/user-guide.md).

## Two-window desktop station

The `ELTSDesktop` player opens a browser administrator dashboard and a separate
participant game window. A fresh station opens the dashboard without forcing
participant entry. First use the gear icon to arrange tests and configure timing;
then choose **New participant** and enter the participant number/ID, optional name
or test alias, and initial observations.
The existing timestamped `OperatorNote` stream stores these details before arming;
names and observations are not saved in browser preferences or used as folder names.
Reloading the dashboard during a session does not create another recording.

The gear icon opens settings for condition order, windowed/fullscreen mode, preview
refresh rate, reduced interface motion, and camera zoom/reset. Condition order locks
while a participant is active. The dashboard stacks its panes on narrow windows;
wider windows retain the draggable divider and keyboard resizing. Drag the outside
view to orbit, scroll to zoom, or use arrow keys and +/- while the view has focus.

The outside preview captures at 1920 × 1200 with JPEG quality 90 by default,
at 10 frames per second. `DevelopmentTestStation` exposes preview width, JPEG
quality and cadence as serialized settings; height preserves the 16:10 aspect
ratio. Higher resolution increases capture and encoding work. Preview images
and overlay text cannot be dragged or selected, so pointer drags control orbit.

### Prepare the test order and reuse setups

Drag a test's dotted grab handle to reorder it. The lifted tile has a drop shadow,
neighboring tiles slide into place, and a placeholder shows the drop position.
Release to save the order to the station. Escape cancels the move; focused handles
also support Up/Down keyboard reordering. The list
scrolls at its edges during a drag and respects reduced-motion preferences.
Order changes lock during an active participant session.

**Saved setups** stores named presets containing the applied order, individual
test durations, practice duration and shared break duration. Test-duration changes
save automatically; practice and break changes use their shared save action. Presets
live in this browser's local storage and contain no participant
details or observations. Loading validates the complete preset before applying it;
it is available before a participant or after the previous recording closes.

### Calibration, practice and readiness

Creating a recording runs the existing two-second synthetic tracking preflight.
The station then waits for explicit administrator calibration review: **Generate
fixture**, review the reported provisional residuals, **Accept fixture**, then
**Start practice**. **Redo** discards an unaccepted fixture. Acceptance saves the
synthetic fixture and its audit note; it does not change the startup rig calibration.

Practice is a timed movement/aim familiarization period with no scored shots.
The administrator can restart it before the first test or finish it early. The
participant still starts each armed shooting test with their own trigger.
**Readiness & diagnostics** shows writer health, sample drops, fresh synthetic
tracking and observed rate, setup-time disk space, configuration and fixture status.
Controller/display/SteamVR substitutions remain explicitly labeled as emulated.

Each shooting test has its own duration under **Settings → Test durations**, in
seconds from 1 to 3600 (default 300). A committed value saves automatically before
creating the recording, before that test starts, or while that test is paused. Upcoming tests can be edited
while another is running. A new total duration must exceed the active time already
played. Completed tests in an active session remain locked. After completing or
aborting the participant session, duration edits configure the next recording.
These values last for the running station; they reset when the application closes.

**Pause test** freezes the current timer, target motion, target lifetimes, and
respawn delays. Scores and target identities are retained. **Resume test** continues
that same test with its remaining active time. Held clicks are canceled on either
transition. Raw tracking acquisition and timestamped logging continue through the
pause, using the unchanged shared clock. The simulated WE link is stopped while
paused and prepared with the remaining duration on resume.

**Stop test** confirms an early finish, retains results so far, and marks that test
as **Stopped early**. The administrator starts the break after saving finishes. A stopped test cannot resume;
use Pause for a temporary hold.
**Abort participant session** remains the separate action for ending the entire
attempt. A participant-triggered start is still required for each new test.

### Breaks, skips and repeats

Every finished or skipped test saves and waits, including the final test.
Press **Start break** to begin the countdown. Both windows then show the remaining break time. **Settings → Every break** is
one shared duration (0–3600 seconds); changing it during a break preserves elapsed
rest time. **Skip break** becomes available once the test's save checkpoint succeeds.
When the timer expires, the next test becomes ready automatically. The administrator
must still arm it. After the final break the participant recording closes.

**Skip this test** is available for a disarmed, ready test and requires a reason.
It records a skipped disposition without fabricating a zero-score shooting block.
**Repeat a test** selects a finished or skipped condition and requires a reason.
During an open session it inserts a distinct attempt next, preserving previous
attempts and all remaining tests. An already queued condition cannot be queued twice.
After session completion, repeating opens a separate uniquely suffixed recording
folder linked by an audit note; the previous raw recording is never reopened for writes.

### Saving and reviewing data

Raw data is continuously appended to `samples.ndjson`, `targets.ndjson` and
`events.ndjson`, with the configured periodic flush (currently one second).
After each finished or skipped test, the station places a boundary in both logging
queues and requests a durable flush on the writer thread. Only after both queues
reach that boundary does it publish `test-checkpoint-<attempt>.json` and show **Saved**.
The checkpoint includes the attempt, disposition, descriptive counts, duration and
save time. It does not close the participant recording or claim a standard score.
Saving failures are visible and stop continued session use; raw files are retained.

At participant completion, abort or orderly application shutdown, the writer drains
and closes the streams and publishes `session-summary.json` with counts, provenance
and SHA-256 checksums. A per-test checkpoint is useful recovery evidence but is not a
replacement for this final integrity summary. The analysis reader still requires a
successfully finalized raw recording.

The **Session notes** timeline shows accepted timestamped observations. The small
**Data** button switches to the participant collection while administration keeps
running. Search by participant ID or name, select a session, inspect its tests and
notes, or page through original JSON records.

The collection folder and SQLite database are initialized on dashboard startup.
Raw streams are retained and indexed into SQLite after each checkpoint and final
closure. **Refresh** imports existing recordings and retries secondary-copy errors.
The viewer displays the actual configured folder and persistent errors. Optional
browser preference storage is never required for the controls to initialize.

**Export database** creates a whole-collection SQLite snapshot; **Export participant**
creates a fresh database containing only the selected participant. **Download export**
saves it through the browser. **Export original files** retains finalized raw files
in a ZIP, and **Download review CSV** saves the descriptive test table. Exports are
also retained in the configured folder's `exports` subdirectory.

The default folder is **collection data** at the project root. Existing custom or
older machine overlays retain their configured path, which **Open folder** opens.
A standalone copy outside a project saves alongside its executable. Automated
Unity test recordings use a separate temporary test directory.

See [local participant collection](data-collection.md) for schema, identity, recovery,
export guarantees and limitations. Synthetic data remains unsuitable for physical
or study-readiness claims.

Duration edits, pauses, resumes and early endings are recorded as explicit events.
The analysis reader retains flexible synthetic timing summaries but excludes those
tests from the standard uninterrupted 300-second score. Standard session plans keep
their fixed duration and reject the development-only flexible controls.

The participant window supports native edge resizing. F11 or Alt+Enter toggles
borderless fullscreen; Escape/F1 returns to the previous window size. Display
changes cancel held clicks, and session time continues. Settings also exposes both
display modes without needing focus in the game.

Startup stays windowed and leaves monitor placement to Windows, to avoid an
automatic monitor/fullscreen transition while using a wireless TV. Connect the TV
with Win+K before launching. In extended-desktop mode, move the participant window
to the TV with Win+Shift+Left/Right, then maximize it if desired. Test connection
stability before enabling fullscreen. A disconnected wireless display must be
reconnected through Windows; ELTS cannot guarantee or maintain the Miracast link.
The standalone participant view is capped at 60 fps (adjustable through the
`participantFramesPerSecond` component setting) to leave GPU capacity for display
encoding. This changes presentation cadence, not the shared recording clock.
Direct player launches can opt into `-eltsSecondaryDisplay` and `-eltsFullscreen`;
`-eltsWindowed` overrides both startup options. The one-click launcher uses
`-eltsWindowed` with a 1280×720 window.

The outside preview defaults to 640×400 at up to 10 frames/second. Settings offers
5, 10, 20, or 30 fps; higher preview rates cost more rendering and JPEG encoding.
The game renders independently of preview cadence. Preview capture uses asynchronous
GPU readback where supported, with a synchronous fallback, and the outside camera
renders only for requested captures. The hidden Unity workbench no longer refreshes
its labels every game frame. These changes reduce presentation work; actual frame
rates depend on the display, GPU, and chosen preview rate.

The following sections describe the embedded Unity workbench, which remains
available for development and existing runtime tests.

The Unity dashboard groups the existing synthetic testing controls into preparation,
calibration, practice and blocks, notes, recordings, devices, and administrator
checkpoints. The session action stays below the focused task; participant and
apparatus previews remain beside it. Use Unity **6000.3.23f1 LTS** for source work.

## Start and operate

For the integrated mouse/keyboard game, open `Assets/Scenes/ELTSDesktop.unity`
in the Unity project and press Play, or use `OPEN-DESKTOP-TEST.cmd` with its new
build. See [desktop input](desktop-input.md) for controls and the complete run.
Choose the input source in Preparation before creating each recording.

Run `OPEN-DASHBOARD.cmd` from the repository after building the Windows player into
`build/operator-dashboard`. The build command is `./scripts/build.ps1 -Output
build/operator-dashboard`; the output directory must be empty for a new build.
See the repository setup instructions for the configured Python and Unity paths.

1. In Preparation, enter a synthetic test identifier. Arrange the four conditions
   using their grab handles (or the arrow keys while a handle is focused). Save a named preset to reuse
   an order. Identity and order lock while a recording is active.
2. Choose **Create synthetic recording** in the persistent action area. Follow its
   readiness feedback, then Continue to the synthetic calibration fixture.
3. Generate, review and accept the development fixture. Continue through practice,
   breaks and blocks using the session action. **Go to current task** returns to
   the relevant controls without forcing navigation while you are editing elsewhere.
4. Use Notes for timestamped observations and provisional sync markers. Draft text
   survives switching tasks during the current application run.
5. Use Recordings to open the output folder or existing rendering and replay tools.
   Previous recordings remain intact when starting a rerun.

Abort requires a reason. The dashboard calls the existing session controller;
session timing, event ordering, logging and scoring remain owned by that controller.
See [session runtime](session.md) for these invariants.

## Arrange your workspace

Drag the `::` handles to reorder navigation, live cards, condition rows and local
checkpoints. The actual row follows the pointer, with a same-size placeholder and
animated neighboring rows. Hold near the scroll edge to continue moving through a
long list. Escape cancels a drag. Focus a handle and use arrow keys to reorder.

Drag a divider to resize the side panes, or use the width sliders in Workspace
settings. Keyboard arrows resize a focused divider. Escape cancels a resize.
Reduced motion disables reflow animation and visual transitions. Reset layout
restores pane sizes and navigation/live-card order.

Administrator checkpoints support names, instructions, completion marks, ordering,
removal and undo of the last removal. They are personal procedure reminders.
They do not change the accepted execution plan, progress events or study gates.

Layout, selected task, condition order, presets, checkpoints and motion preference
are stored in `operator-workspace-v1.json` under Unity's `Application.persistentDataPath`.
They are local to the Windows user, separate from raw recording output. Writes are
debounced and atomically replaced. Invalid saved data is preserved; Workspace
settings offers explicit recovery that archives the unreadable file before saving
fresh settings. Unsaved recording-note drafts are not retained across application restarts.

## Extend the interface

- `DashboardWorkspace.cs` defines the versioned, UI-independent local model and
  validation. Add a migration before changing the persisted schema.
- `OperatorDashboard.cs` owns navigation, live previews, settings and persistence;
  `OperatorDashboardLists.cs` owns editable lists and condition presets.
- `DashboardManipulators.cs` owns pointer capture, cancellation, row reflow, edge
  scrolling, pane resizing and apparatus-view interaction. Motion and geometry
  constants include units and remain centralized there.
- `DevelopmentDashboardAdapter.cs` translates the existing controller into a
  dashboard snapshot. Add engine operations through the controller's action path,
  not from workspace persistence or a list callback.
- `SessionPanel.uxml` declares controls; `SessionPanel.uss` centralizes appearance.
  Add a module descriptor and stable identifier alongside its UXML content. Preserve
  existing control names used by the controller and its tests.

The [design brief](../design/operator-design-language.md) records interaction intent.
UI elements remain alive when changing tasks, preserving draft text and focus rather
than rebuilding the whole panel on each edit.

## Verification and scope

`AdministratorStationTests` covers explicit calibration/practice, all four shooting
tests, pause/resume, administrator-started breaks, skips/repeats, per-test checkpoints, archive
review/export and continuation in a separate recording. The standalone session and
logging checks cover the shared timer, attempt identities, two-queue save boundaries
and injected durable-flush failure. `tools/browser-tests/administrator.cjs` exercises
real dashboard assets against a synthetic HTTP fixture, including pointer/keyboard
dragging, cancellation, presets, timing, escaped notes, CSV download and mobile layout.
Run it with Node and Playwright available (`node tools/browser-tests/administrator.cjs`);
Chrome is the default browser, with `ELTS_TEST_BROWSER` available to choose another
installed Playwright browser channel.

`OperatorDashboardTests.cs` exercises layout, previews, local editing/persistence,
drag footprint and cancellation, reorder commits, reduced motion and edge scrolling
in Unity Play Mode. Existing session tests exercise controller actions through the
dashboard. `DashboardWorkspaceChecks.csproj` checks local-model validation and files.
The opt-in `-eltsSessionScreenshot <absolute-new-png-path>` player argument captures
the dashboard and exits without creating a recording. The hidden-player probe uses
explicit camera render requests and also saves the two raw camera images beside the
panel image. It verifies offscreen rendering; it does not establish native window
input behavior or continuous rendering in a visible Windows window.

This is a synthetic development interface. The apparatus is built, but testing
equipment is unavailable. Hardware actuation, measured calibration, physical
display/tracker mapping, timing, E-stop behavior and study readiness remain pending.
Custom checkpoints and simulated passes cannot satisfy those requirements.
