# Software architecture

ELTS currently runs as synthetic development software. Most numerical and session
logic is plain C# under `unity/Assets/ELTS/`; Unity adapters provide input,
rendering and the operator interface. The browser simulation is a separate
illustrative application, not a validated hardware model.

## Data flow

1. **Configuration** is loaded from `config/`, validated against `schemas/config/`
   and staged into generated Unity files. Machine-local values are separate from
   shared defaults and synthetic rig geometry. See [configuration staging](modules/config-staging.md).
2. **Clock and tracking** provide a shared process monotonic timestamp and explicit
   validity with converted room-frame poses. Invalid observations remain invalid;
   logged data is not predicted or interpolated. See [tracking](modules/tracking.md).
3. **Geometry and calibration** compute screen transforms, pivot offsets, weapon
   zero and eye offsets. Units and transform directions are explicit. Synthetic
   calibration never qualifies a physical rig. See [geometry](modules/geometry.md)
   and [calibration](modules/calibration.md).
4. **Session and scenario** control preparation, practice, conditions, targets and
   recording. Failures stop continued session use. See [sessions](modules/session.md)
   and [scenarios](modules/scenario.md).
5. **Rendering and operator UI** display the participant scene and administrator
   controls. Targets are represented in world coordinates; rendering-only
   adjustments must not alter primary logged tracking data. See
   [rendering](modules/rendering.md) and [dashboard](modules/operator-dashboard.md).
6. **Logging and analysis** retain samples, targets, events and closure summaries.
   The writer owns output files; checksums detect modified streams. Readers
   validate schemas and closure before analysis. See [logging](modules/logging.md),
   [analysis](modules/analysis.md) and [replay](modules/replay-reader.md).

## Apparatus interface

The development link uses `IEltsLink` with null/mock implementations and an
in-memory controller model. It does not operate physical outputs. Integration must
use lifecycle commands, never head/weapon poses, target coordinates, turret angles
or individual LED commands. Device-owned safeguards stay independent of Unity.
See [link protocol](modules/elts-link.md), [session integration](modules/elts-session.md)
and [hardware follow-up](operator/hardware-follow-up.md).

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
outputs. [Developer commands](operator/developer-quick-start.md) explain how to
build/test; [safety boundaries](safety/README.md) describe what remains unavailable.
