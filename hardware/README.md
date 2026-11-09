# Hardware design files

CAD, print files and bench sketches for parts built around the ELTS software. None
of these designs has been physically fitted, strength-tested or electrically tested
unless its own README says so. Passing a CAD or mesh check does not establish
tracking, calibration, safety or study readiness; see the
[software safety boundaries](../docs/safety/README.md).

## VIVE Tracker 3.0 trigger connector

Start with the [wiring guide](vive-tracker-3.0/wiring-guide.md): the switch circuit,
tracker pins and bench checks apply to every revision. The guide still names
**Rev B fixed-base** as the package to build. Revisions C, D and E were designed
after it, but none has been printed or tested, so newer does not mean proven.

| Revision | Folder | What it is |
| --- | --- | --- |
| A | [`connector/`](vive-tracker-3.0/connector/README.md) | First printable connector for Mill-Max contacts, with a DigiKey buying list |
| B (M2.5) | [`connector-rev-b/`](vive-tracker-3.0/connector-rev-b/README.md) | Alternative for Treedix 6 mm pins and M2.5 hardware. Do not mix parts with the fixed-base variant |
| B fixed-base (M3) | [`connector-treedix-fixed-base/`](vive-tracker-3.0/connector-treedix-fixed-base/README.md) | Treedix pins with M3 hardware and a deeper body; the package the wiring guide names |
| C | [`connector-rev-c-exposed/`](vive-tracker-3.0/connector-rev-c-exposed/README.md) | Replacement inserts that expose the gold contacts on the fixed-base body |
| D | [`connector-rev-d-open-clamp/`](vive-tracker-3.0/connector-rev-d-open-clamp/README.md) | Two-piece open clamp that grips the stationary pin barrels |
| E | [`connector-rev-e/`](vive-tracker-3.0/connector-rev-e/README.md) | Newest design (November 6, 2026): upside-down Treedix contacts on pins 2 and 4, checked against HTC's tracker STEP |

Each revision folder holds its own `parameters.json`, the Python that builds and
validates it, and the generated `output/` files. Regenerate from those scripts
instead of editing the outputs by hand.

Also in [`vive-tracker-3.0/`](vive-tracker-3.0/):

- [`bolt-spacers/`](vive-tracker-3.0/bolt-spacers/README.md): ring spacers for the 1/4-20 tracker mounting screw.
- `official-model/` and `VIVE_Tracker_3.0_3D.zip`: HTC's published tracker model, used as the CAD reference.
- `HTC_Vive_Tracker_3.0_Developer_Guidelines_v1.1.pdf` and `Mill-Max_target_contacts.pdf`: vendor references.
- `ELTS_VIVE_Tracker_3_Trigger_Connector_Rev_B.zip`: packaged Rev B (M2.5) files.

The software side of the trigger is described in
[VIVE gun trigger input](../docs/developer/modules/vive-trigger.md).

## TF-Luna distance sensor

[`tf-luna/`](tf-luna/README.md) is an Arduino bench test for a TF-Luna LiDAR sensor on
an ESP32, with a host-side C++ check of the frame parser. It is not used by the ELTS
application.
