# VIVE Tracker 3.0 trigger connector

**Current package for this assembly:** [Rev B fixed-base connector](connector-treedix-fixed-base/README.md), updated October 19, 2026 for Treedix 6 mm contacts, Senyard M3 cover hardware, and the selected 5/8-inch mounting screw with 3.175 mm reserved for an additional mounting piece. Use that folder's matching parts and instructions. The separate `connector-rev-b` folder is an alternative M2.5 design; do not mix the variants.

Research checked 2026-10-14. A [Revision A printable connector](connector/README.md) is now available with Fusion-compatible STEP, print STLs, and an [exact buying list](connector/BUYING-LIST.md). No physical assembly or fit test has been performed.

## What ELTS needs

The current software has distinct head and mock-rifle tracker identities. Head pose supports the eye/view geometry; weapon pose supports muzzle/bore geometry. A trigger event enters the session/scoring/recording pipeline separately. See [tracking](../../docs/developer/modules/tracking.md), [desktop input](../../docs/developer/modules/desktop-input.md), and `config/rig/templates/synthetic-rig.json`.

The working sources are currently synthetic or mouse/keyboard. A contact adapter does not by itself implement or validate the physical SteamVR trigger input. Existing desktop/synthetic behavior fires on release of the logical pressed state. An active-low electrical press must not be confused with that logical release; the hardware adapter must make this mapping explicit.

## Verified interface

HTC identifies Tracker 3.0 as supporting SteamVR base stations 1.0/2.0, USB-C, and a 1/4-inch UNC attachment. Tracker version and tracking-system version are different labels. [HTC product resources](https://developer.vive.com/us/hardware/tracker3/)

The v1.1 guide identifies pin 4 as an active-low trigger input with internal pull-up; pin 2 is ground. Pin 1 is an output, not the trigger. The accessory drawings specify 3.5 mm contact pitch and nominal 2.0 x 4.1 mm mating pads. Use the illustrated orientation, not an assumed left/right pin order. [HTC developer guide](https://developer.vive.com/documents/850/HTC_Vive_Tracker_3.0_Developer_Guidelines_v1.1_06022021.pdf), printed pages 13-17 (PDF pages 16-20).

## Wiring

```text
TRACKER CONTACT ADAPTER                  TRIGGER SWITCH

pin 4 / trigger ---- insulated wire ---- NO
                                        /  closes when pressed
pin 2 / ground  ---- insulated wire ---- COM
```

Use an electrically isolated, normally open momentary switch. For a three-terminal microswitch, use COM and NO; leave NC disconnected. This proposed passive circuit needs no Arduino, external supply, or added pull-up. Leave the other tracker contacts unconnected. Do not apply 5 V to the trigger input or solder directly onto the tracker.

My harness recommendation is a short pair of flexible stranded wires, insulated solder joints, and a keyed two-position locking connector if the cable needs to detach. Keep connector pin assignments labeled at both ends. Cable strain should be carried by the housing, not by the contact tails or solder joints.

## Two practical ways to make contact

**Ready-made, quickest wiring route:** Immersion lists a [Vive Tracker Connector](https://cornershop-immersion.com/en/vr-ar-accessories/172-vive-tracker-connector.html), reference F100/10844, explicitly compatible with Trackers 2.0 and 3.0. It includes a board and strap and exposes the interface at the side. Listed price was EUR 120 excluding VAT, with status "To order". US shipping, lead time, and the mating cable/connector pinout were not verified. Obtain its connector pinout before making the harness; its side-connector numbering need not equal HTC's pad numbering. This is an electrical breakout option; its strap is not evidence of adequate rifle-mount rigidity.

**Custom printable connector:** [Revision B](connector-rev-b/README.md) uses the selected Treedix 6 mm pins with their fixed flat bases facing the tracker, captured in a split printed insert. It uses M2.5 screws and nuts from the HAPRIC kit and a 1/4-20 x 1/2-inch Allen-drive button-head mounting screw. [Revision A](connector/README.md), for Mill-Max solder-tail contacts, remains available separately. A recessed 1/4-20 screw and locating post attach the body to the tracker. The removable cover supports the insert, encloses the solder joints, and provides cable-tie strain relief. This replaces the earlier contact-PCB proposal. The main screw is not captive when removed from the tracker.

The STEP solids and STL meshes passed nominal CAD checks against the official tracker model. Start with the supplied hand-fit coupon before printing the full housing. Actual print accuracy, contact reliability and mounting strength remain untested. Any later change to tracker mounting requires the corresponding ELTS geometry/calibration work.

## Downloaded reference files / Fusion

- `HTC_Vive_Tracker_3.0_Developer_Guidelines_v1.1.pdf`: current linked v1.1 guide, including revised GPIO specifications.
- `VIVE_Tracker_3.0_3D.zip`: unchanged [official HTC model archive](https://dl.vive.com/Tracker/3D_model/VIVE_Tracker_3.0_3D.zip).
- `official-model/VIVE_Tracker_3.0_3D_STP/VIVE_Tracker_3.0_3D.stp`: extracted official STEP reference for Fusion. It is the tracker, not a designed connector.

Open the current assembly at `connector-treedix-fixed-base/output/ELTS_VIVE_Tracker_3_Trigger_Connector_Rev_B_Fixed_Base.step` in Fusion. It contains editable solids rather than a native Fusion feature timeline. The official tracker STEP remains a separate reference; neither digital model is evidence of physical fit.

## Checks when the parts are available

With the adapter off the tracker, verify that the switch connects only the intended two contacts when pressed, and opens when released. Inspect for adjacent-pad shorts and test cable strain relief. Fit without forcing the screw or contacts. Then check press/release recognition through the actual SteamVR binding and ELTS hardware adapter, including disconnect/reconnect and switch bounce. Hardware timing and calibrated tracking remain separate validation work; see the [existing safety boundary](../../docs/safety/README.md).
