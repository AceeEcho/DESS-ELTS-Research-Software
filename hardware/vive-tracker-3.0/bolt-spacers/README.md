# VIVE Tracker mounting-bolt spacers

24 plain ring spacers for the existing 1/4-20 UNC mounting screw: **three each of 0.5, 1, 1.5, 2, 2.5, 3, 3.2 and 3.5 mm thickness**.

- Outside diameter: **11.2 mm**, inside the connector's 11.8 mm recess.
- Bore: **6.9 mm**, around the bolt's nominal 6.35 mm thread diameter.
- Edge chamfer: 0.08 mm. The bearing faces stay flat, with no raised labels.
- Each thickness is a separate named object in the 3MF. Three identical copies form each row; thickness increases along +Y. Check the object name or measure before use.

Open `output/ELTS_VIVE_Tracker_Bolt_Spacers.3mf` in Bambu Studio at 100% scale. The rings are already flat on the build plate. Individual STEP/STL files and an editable combined STEP are also included.

Print as solid rings, with supports off. A 0.10 mm layer height preserves all requested thicknesses. Measure the finished thickness and check that the hole slides freely over the actual bolt and that the ring seats inside the printed recess. Digital geometry checks passed against both connector bodies; physical printer and hardware tolerances remain untested.

## Choosing a thickness

For the previously selected **5/8-inch screw**, the original connector shoulder leaves 7.675 mm of thread projection without any added thickness. Subtract the total thickness of spacers and any additional mounting piece to calculate projection. The earlier nominal target was 4.5 mm; a **3.2 mm spacer alone gives about 4.475 mm**. If another mounting piece already consumes that allowance, do not add another full 3.2 mm automatically. The thinner rings are adjustment options, not individually approved mounting stacks.

All ring diameters clear both connector variants. With the deeper fixed-base body and nominal 3.2 mm-tall button head, individual spacers through 3.5 mm also keep the head recessed. The shallower M2.5 variant can leave the head projecting below the body when spacers are added. Confirm actual engagement and seating before tightening; do not use a bottomed-out screw to pull the mount together.

`parameters.json` holds dimensions and quantity. Rebuild with `python build_spacers.py` in an environment containing CadQuery and VTK. `output/verification.json` records solid, closed-mesh and nominal body-clearance checks. No original connector parts are changed.
