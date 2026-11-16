# Software architecture

ELTS currently runs as synthetic development software. Most numerical and session
logic is plain C# under `unity/Assets/ELTS/`; Unity adapters provide input,
rendering and the operator interface. The browser simulation is a separate
illustrative application, not a validated hardware model.

## Data flow

1. **Configuration** is loaded from `config/`, validated against `schemas/config/`
   and staged into generated Unity files. Machine-local values are separate from
   shared defaults and synthetic rig geometry. On each `START-ELTS.cmd`, the most
   recently saved [calibration workspace](../../Calibration%20Environment/README.md)
   profile supplies screen geometry, mount offsets and the gun tracker serial.
   See [configuration staging](modules/config-staging.md).
2. **Clock and tracking** provide a shared process monotonic timestamp and explicit
   validity with converted room-frame poses. Invalid observations remain invalid;
   logged data is not predicted or interpolated. Poses currently come from the
   desktop/synthetic source; the gun tracker's trigger button is read through
   OpenVR. See [tracking](modules/tracking.md), [desktop input](modules/desktop-input.md)
   and [VIVE gun trigger](modules/vive-trigger.md).
   Cross-device timing remains subject to D-12; account for the
   [Pupil timing integration follow-up](../operator/pupil-time-synchronization.md)
   before the P8.4 synchronization rehearsal.
3. **Geometry and calibration** compute screen transforms, pivot offsets, weapon
   zero and eye offsets. Units and transform directions are explicit. Synthetic
   calibration never qualifies a physical rig. See [geometry](modules/geometry.md)
   and [calibration](modules/calibration.md).
4. **Session and scenario** control preparation, practice, conditions, targets and
   recording. The narrated [participant walkthrough](modules/participant-tutorial.md)
   drives setup, calibration and practice, then hands over to the automatic
   scenario → break → form sequence. Failures stop continued session use. See
   [sessions](modules/session.md) and [scenarios](modules/scenario.md).
5. **Rendering and operator UI** display the participant scene and administrator
   controls. The player hosts the browser dashboard through a local
   `AdministratorServer` and opens it on start. Targets are represented in world coordinates; rendering-only
   adjustments must not alter primary logged tracking data. See
   [rendering](modules/rendering.md) and [dashboard](modules/operator-dashboard.md).
6. **Logging and analysis** retain samples, targets, events and closure summaries.
   The writer owns output files; checksums detect modified streams. Readers
   validate schemas and closure before analysis. The local collection
   (`collection.sqlite`) indexes recordings for the dashboard's **Data** view and
   Excel export without modifying raw files. See [logging](modules/logging.md),
   [data collection](modules/data-collection.md), [analysis](modules/analysis.md)
   and [replay](modules/replay-reader.md).

## Apparatus interface

The development link uses `IEltsLink` with null/mock implementations and an
in-memory controller model. It does not operate physical outputs. Integration must
use lifecycle commands, never head/weapon poses, target coordinates, turret angles
or individual LED commands. Device-owned safeguards stay independent of Unity.
See [link protocol](modules/elts-link.md), [session integration](modules/elts-session.md)
and [hardware follow-up](../operator/hardware-follow-up.md).

## Build and support tools

- `scripts/` exposes the Windows commands; `tools/bootstrap/driver.py` handles
  setup diagnostics, test selection and build entry points.
- `tools/validation/schema.py` is the shared, dependency-free local JSON validator.
  It supports a deliberate subset of JSON Schema and rejects unsupported assertions.
- `tools/config/` stages configuration; `tools/dependencies/` verifies pinned inputs.
- `tools/build/` verifies and packages players with source/configuration provenance.
- `tools/logging/` verifies recorded runs; `tools/runtime-tests/` tests shared C#.
- `elts-simulation/build.mjs` packages the offline simulation.

Unity is pinned to **6000.3.23f1 LTS**. Other tool versions live in
`config/toolchain.json`. Builds, caches, diagnostics and recordings are local
outputs. [Developer commands](quick-start.md) explain how to
build/test; [safety boundaries](../safety/README.md) describe what remains unavailable.
