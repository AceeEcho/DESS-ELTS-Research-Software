# Administrator dashboard

The Unity dashboard groups the existing synthetic testing controls into preparation,
calibration, practice and blocks, notes, recordings, devices, and administrator
checkpoints. The session action stays below the focused task; participant and
apparatus previews remain beside it. Use Unity **6000.3.23f1 LTS** for source work.

## Start and operate

Run `OPEN-DASHBOARD.cmd` from the repository after building the Windows player into
`build/operator-dashboard`. The build command is `./scripts/build.ps1 -Output
build/operator-dashboard`; the output directory must be empty for a new build.
See the repository setup instructions for the configured Python and Unity paths.

1. In Preparation, enter a synthetic test identifier. Arrange the four conditions
   using their handles or Move up / Move down buttons. Save a named preset to reuse
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
long list. Escape cancels a drag. Focus a handle and use arrow keys to reorder;
conditions and checkpoints also have visible movement buttons.

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
