# Rev C - exposed gold contacts

Print insert_front.stl and insert_rear.stl to upgrade the newest fixed-base M3
attachment. Reuse its body and wire cover; their geometry is unchanged.
The complete STEP assembly and full STL set are also supplied.

The entire 2 mm diameter gold faces now stand 0.5 mm above the plastic seats.
Removed the old lips that stood 0.4 mm above the gold and the 1.6 mm windows.
Contact height remains Z=2.515 mm: this removes the plastic recess rather than
extending pins into the tracker housing. The reported approximate 4 mm physical
gap is not a measured dimension; inspect seating if a gap remains.

## Retention and assembly

The open seats no longer capture the flanges from above. Use a small amount of
electrically nonconductive adhesive compatible with PETG and plated metal on the
stationary barrels inside the split holder. Adhesive choice, cure and bond
strength require a real fit check. Do not rely on loose pins or wires for
retention. Keep adhesive off gold faces, flange undersides, moving tips and the
body pocket. Hold both flange undersides fully seated during the full cure.

Dry-fit first. Wide flat gold ends face the tracker; moving tips face down.
Solder wires to lower stationary barrels outside the plastic, let cool and
insulate joints separately before bonding. Refit inserts and cover after cure;
verify exposed gold faces and that neither pin moves under gentle handling.
Check isolation and switch continuity before mounting. Seat squarely without
forcing; verify both contacts actually touch.

Print at 100% scale. PETG, 0.16 mm layers, 0.4 mm nozzle and five walls are
starting settings, not verified print parameters. Print plastic STLs only;
STEP metal contacts and fasteners are reference solids.

Reuse two M3 x 16 cover screws and M3 nuts. The 1/4-20 x 5/8 inch main screw
still requires the existing 3.175 mm effective bearing spacer/additional mount
for nominal 4.5 mm thread projection. Do not use that screw without the reserved
bearing thickness. The mounting coupon is hand-fit only, without a screw.

Wire ground contact 2 and trigger contact 4 through the isolated normally open
switch. No external voltage or soldering onto the tracker.

## Checks
Build with build_connector.py and check with validate_connector.py using
CadQuery 2.8.0 and VTK. Checks cover solids, meshes, interference and gold exposure
against the adjacent official HTC STEP. These are CAD checks only. Physical fit,
adhesive retention, electrical reliability, strength and calibration are untested.
The original fixed-base Rev B files are preserved alongside this folder.
See ../connector-treedix-fixed-base/README.md for original hardware sources.
