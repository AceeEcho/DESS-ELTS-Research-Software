"""Build ELTS's printable VIVE Tracker 3.0 trigger adapter (millimetres).

Run with CadQuery 2.8.0. The STEP files are editable solids for Autodesk Fusion;
STLs are separately oriented for printing. All adjustable values live beside
this script in parameters.json. This is a CAD-checked prototype, not a fit test.

Coordinates: Z=0 is the tracker's threaded mounting-ring face, +Z enters the
tracker, +Y points toward its stabilizing hole. Viewed at the adapter's mating
face, pin 1 is on the left; ground 2 is X=-5.25 and trigger 4 is X=+1.75.
Revision C uses the FIXED flat bases of reversed Treedix pins as targets.
The moving plungers point into the wire cavity and are not mating surfaces.
"""
from __future__ import annotations

import json
import math
from pathlib import Path
import cadquery as cq

ROOT = Path(__file__).resolve().parent
P = json.loads((ROOT / "parameters.json").read_text())
T, C, H, I = (P[k] for k in ("tracker_interface", "contact", "housing", "contact_insert"))
F = P["fasteners"]
ASSEMBLY_NAME = f"ELTS_VIVE_Tracker_3_Trigger_Connector_Rev_{P['revision']}"
OUT = ROOT / "output"
OUT.mkdir(exist_ok=True)


def box(width, depth, bottom, top, x=0, y=0):
    return cq.Workplane("XY", origin=(x, y, bottom)).box(
        width, depth, top-bottom, centered=(True, True, False))


def rounded_box(width, depth, bottom, top, x=0, y=0, radius=3):
    return box(width, depth, bottom, top, x, y).edges("|Z").fillet(radius)


def cylinder(diameter, bottom, top, x=0, y=0):
    return cq.Workplane("XY", origin=(x, y, bottom)).circle(diameter/2).extrude(top-bottom)


def contact(x):
    """Reversed Treedix pin, seated on its fixed flange; dimensions in mm.

    Only the stationary barrel is soldered. The unused plunger is modelled at
    its full free extension so the enclosure also clears its entire movement.
    The rounded plunger tip is simplified to a conservative cylindrical envelope.
    """
    z = T["contact_face_z"]
    flange_bottom = z-C["flange_thickness"]
    barrel_bottom = flange_bottom-C["barrel_length"]
    y = T["contact_y"]
    pin = cylinder(C["flange_diameter"], flange_bottom, z, x, y)
    pin = pin.union(cylinder(C["barrel_diameter"], barrel_bottom, flange_bottom, x, y))
    return pin.union(cylinder(C["plunger_diameter"],
        z-C["overall_length"], barrel_bottom, x, y)).clean()


def make_body():
    bottom, top = H["body_bottom_z"], H["body_top_z"]
    body = rounded_box(H["front_width"], H["front_depth"], bottom, top,
                       y=H["front_center_y"], radius=H["corner_radius"])
    body = body.union(rounded_box(H["spine_width"], H["spine_depth"], bottom, top,
                                  y=H["spine_center_y"], radius=H["corner_radius"]))
    # A raised annulus seats on the metal mount; the broad face meets the rubber.
    body = body.union(cylinder(H["mount_seat_diameter"], top, 0))
    locator = cylinder(T["locator_diameter"], top, T["locator_height"], y=T["locator_y"])
    locator = locator.edges(">Z").chamfer(0.35)
    body = body.union(locator)
    boss = (cq.Workplane("XY", origin=(0, T["recess_center_y_from_step"], top))
            .slot2D(H["contact_boss_length"], H["contact_boss_width"])
            .extrude(H["contact_boss_top_z"]-top))
    body = body.union(boss)

    # Reserve the extra screw length for the user's additional mounting piece.
    # Its bearing thickness is specified here; its shape is not part of this CAD.
    bearing = (H["mount_screw_engagement"]-H["mount_screw_length"]
               +H["additional_mount_piece_thickness"])
    body = body.cut(cylinder(H["mount_bore"], bottom-1, 1))
    body = body.cut(cylinder(H["mount_head_pocket_diameter"], bottom-1, bearing))

    body = body.cut(rounded_box(H["wire_cavity_width"], H["wire_cavity_depth"],
        bottom-1, H["wire_cavity_roof_z"], y=H["wire_cavity_center_y"], radius=1.0))

    # The insert's flange is trapped between this shoulder and the cover rails.
    clearance = 2*I["pocket_clearance_per_side"]
    for width, depth, low, high in [
        (I["flange_width"], I["flange_depth"], I["bottom_z"]-0.1, I["flange_top_z"]),
        (I["stem_width"], I["stem_depth"], I["flange_top_z"]-0.1, I["top_z"]+1)]:
        body = body.cut(box(width+clearance, depth+clearance, low, high,
                            I["center_x"], T["contact_y"]))

    # Standard M3 nuts are loaded from the tracker-facing side, before attachment.
    for x in [-H["cover_screw_x"], H["cover_screw_x"]]:
        y = H["cover_screw_y"]
        body = body.cut(cylinder(H["m3_clearance_diameter"], bottom-1, top+0.1, x, y))
        nut = (cq.Workplane("XY", origin=(x, y, H["m3_nut_seat_z"]))
               .polygon(6, H["m3_nut_across_flats"] / math.cos(math.pi/6))
               .extrude(top-H["m3_nut_seat_z"]+0.2))
        body = body.cut(nut)

    # Open-bottom cable exit prints without a trapped horizontal support column.
    body = body.cut(box(H["cable_exit_width"], 6, bottom-0.1,
                         bottom+H["cable_exit_height"], H["cable_exit_x"], -25))
    return body.clean()


def make_inserts():
    x, y = I["center_x"], T["contact_y"]
    insert = box(I["flange_width"], I["flange_depth"], I["bottom_z"], I["flange_top_z"], x, y)
    insert = insert.union(box(I["stem_width"], I["stem_depth"], I["flange_top_z"], I["top_z"], x, y))
    face = T["contact_face_z"]
    seat = face-C["flange_thickness"]
    for pin_x in [T["contact_x_ground_2"], T["contact_x_trigger_4"]]:
        # Open pedestal supports the underside of the flange. Its full gold face
        # stands above the plastic; no printed lip covers the tracker contact.
        # Adhesive on the stationary barrel replaces the former axial capture.
        insert = insert.union(cylinder(I["collar_diameter"], I["top_z"]-0.1,
            seat, pin_x, y))
        insert = insert.cut(cylinder(I["barrel_bore"], I["bottom_z"]-0.1,
            seat+0.1, pin_x, y))
        # The lower stationary barrel is exposed for a small pre-soldered wire
        # and insulation. Never solder the flat mating base or moving plunger.
        insert = insert.cut(cylinder(I["solder_relief_diameter"], I["bottom_z"]-0.1,
                                     I["solder_relief_top_z"], pin_x, y))
    gap = I["split_gap"]
    front = insert.intersect(box(50, 20-gap/2, I["bottom_z"]-1, face+1, x, y-10-gap/4))
    rear = insert.intersect(box(50, 20-gap/2, I["bottom_z"]-1, face+1, x, y+10+gap/4))
    return front.clean(), rear.clean()


def hardware():
    """Nominal fastener envelopes, with simple head sockets for clear previews.

    Threads are represented as cylinders. This is not a thread/tolerance model.
    The pictured kits establish sizes only; head/nut dimensions are assumptions.
    """
    bearing = H["mount_screw_engagement"]-H["mount_screw_length"]
    shaft = cylinder(6.35, bearing, H["mount_screw_engagement"])
    head = cylinder(F["mount_head_diameter"], bearing-F["mount_head_height"], bearing)
    main = shaft.union(head)
    screw_socket = (cq.Workplane("XY", origin=(0, 0, bearing-F["mount_head_height"]-0.1))
        .polygon(6, F["mount_socket_across_flats"]/math.cos(math.pi/6))
        .extrude(F["mount_socket_depth"]+0.1))
    result = {"mount_screw_reference": main.cut(screw_socket).clean()}
    cover_bearing = H["body_bottom_z"]-H["cover_thickness"]
    for suffix, x in [("left", -H["cover_screw_x"]), ("right", H["cover_screw_x"])]:
        y = H["cover_screw_y"]
        bolt = cylinder(F["cover_screw_diameter"], cover_bearing,
            cover_bearing+F["cover_screw_length"], x, y)
        bolt = bolt.union(cylinder(F["cover_head_diameter"],
            cover_bearing-F["cover_head_height"], cover_bearing, x, y))
        socket = (cq.Workplane("XY", origin=(x, y, cover_bearing-F["cover_head_height"]-0.1))
            .polygon(6, 2.5/math.cos(math.pi/6)).extrude(1.6))
        result[f"cover_screw_{suffix}_reference"] = bolt.cut(socket).clean()
        nut = (cq.Workplane("XY", origin=(x, y, H["m3_nut_seat_z"]))
            .polygon(6, F["cover_nut_across_flats"]/math.cos(math.pi/6))
            .extrude(F["cover_nut_thickness"]))
        result[f"cover_nut_{suffix}_reference"] = nut.cut(cylinder(
            F["cover_screw_diameter"], H["m3_nut_seat_z"]-0.1,
            H["m3_nut_seat_z"]+F["cover_nut_thickness"]+0.1, x, y)).clean()
    return result


def make_cover():
    top = H["body_bottom_z"]
    bottom = top-H["cover_thickness"]
    cover = rounded_box(H["front_width"], H["cover_depth"], bottom, top,
                        y=H["cover_center_y"], radius=3)
    # Two rails support the insert halves, leaving the solder tails exposed.
    for sign in [-1, 1]:
        cover = cover.union(box(I["flange_width"]-0.6, I["rail_width"], top,
            I["bottom_z"], I["center_x"], T["contact_y"]+sign*I["rail_offset_y"]))

    # Low locating lip keeps a sideways cable pull off the cover screw shanks.
    lip = rounded_box(H["wire_cavity_width"]-0.4, H["wire_cavity_depth"]-0.4,
                      top, top+0.8, y=H["wire_cavity_center_y"], radius=0.8)
    lip = lip.cut(rounded_box(H["wire_cavity_width"]-2.8, H["wire_cavity_depth"]-2.8,
                      top-0.1, top+1, y=H["wire_cavity_center_y"], radius=0.6))
    lip = lip.cut(box(H["cable_exit_width"]+1, 6, top-0.1, top+1,
                         H["cable_exit_x"], -25))
    cover = cover.union(lip)
    for x in [-H["cover_screw_x"], H["cover_screw_x"]]:
        cover = cover.cut(cylinder(H["m3_clearance_diameter"], bottom-0.1, top+0.1,
                                   x, H["cover_screw_y"]))
    # Feed a 2.5 mm cable tie through both slots around the sleeved wire pair.
    for x in [H["cable_exit_x"]-2.8, H["cable_exit_x"]+2.8]:
        cover = cover.cut(box(1.4, 3.2, bottom-0.1, top+1, x, -22.2))
    return cover.clean()


def fit_coupon():
    """Cheap check of both locating features before a full print or soldering."""
    coupon = rounded_box(12, 27, -3, H["body_top_z"], y=6, radius=2)
    coupon = coupon.union(cylinder(H["mount_seat_diameter"], H["body_top_z"], 0))
    coupon = coupon.cut(cylinder(H["mount_bore"], -4, 1))
    pin = cylinder(T["locator_diameter"], H["body_top_z"], T["locator_height"], y=T["locator_y"])
    return coupon.union(pin.edges(">Z").chamfer(0.35)).clean()


def main():
    front, rear = make_inserts()
    parts = {"body": make_body(), "insert_front": front,
             "insert_rear": rear, "wire_cover": make_cover()}
    colors = {"body": (0.12,0.16,0.19), "insert_front": (0.05,0.52,0.72),
              "insert_rear": (0.08,0.65,0.81), "wire_cover": (0.25,0.30,0.34)}
    assembly = cq.Assembly(name=ASSEMBLY_NAME)
    manifest = {}
    for name, part in parts.items():
        s = part.val()
        if not s.isValid() or len(part.solids().vals()) != 1:
            raise RuntimeError(f"Invalid or disconnected printable part: {name}")
        cq.exporters.export(part, str(OUT/f"{name}.step"))
        # Print the body and cover on their flat back faces. Small inserts stand
        # on their flange; their split faces expose the pin-pocket detail.
        print_part = part.translate((0, 0, -s.BoundingBox().zmin))
        cq.exporters.export(print_part, str(OUT/f"{name}.stl"),
                            tolerance=0.025, angularTolerance=0.1)
        cq.exporters.export(part, str(OUT/f"{name}.brep"))
        assembly.add(part, name=name, color=cq.Color(*colors[name]))
        b = s.BoundingBox()
        manifest[name] = {"volume_mm3": s.Volume(), "size_mm": [b.xlen,b.ylen,b.zlen]}

    for pin_name, x in [("contact_2_ground", T["contact_x_ground_2"]),
                        ("contact_4_trigger", T["contact_x_trigger_4"])]:
        pin = contact(x)
        assembly.add(pin, name=pin_name, color=cq.Color(0.83,0.61,0.18))
        cq.exporters.export(pin, str(OUT/f"{pin_name}.step"))
    for name, part in hardware().items():
        assembly.add(part, name=name, color=cq.Color(0.65, 0.68, 0.72))
        cq.exporters.export(part, str(OUT/f"{name}.step"))
    assembly.export(str(OUT/f"{ASSEMBLY_NAME}.step"))
    coupon = fit_coupon()
    cq.exporters.export(coupon.translate((0,0,3)), str(OUT/"mount_fit_coupon.stl"), tolerance=0.025)
    cq.exporters.export(coupon, str(OUT/"mount_fit_coupon.step"))
    (OUT/"cad_manifest.json").write_text(json.dumps(manifest, indent=2)+"\n")
    print(json.dumps(manifest, indent=2))


if __name__ == "__main__":
    main()
