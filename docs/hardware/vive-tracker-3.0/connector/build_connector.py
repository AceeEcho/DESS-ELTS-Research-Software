"""Build ELTS's printable VIVE Tracker 3.0 trigger adapter (millimetres).

Run with CadQuery 2.8.0. The STEP files are editable solids for Autodesk Fusion;
STLs are separately oriented for printing. All adjustable values live beside
this script in parameters.json. This is a CAD-checked prototype, not a fit test.

Coordinates: Z=0 is the tracker's threaded mounting-ring face, +Z enters the
tracker, +Y points toward its stabilizing hole. Viewed at the adapter's mating
face, pin 1 is on the left; ground 2 is X=-5.25 and trigger 4 is X=+1.75.
"""
from __future__ import annotations

import json
import math
from pathlib import Path
import cadquery as cq

ROOT = Path(__file__).resolve().parent
P = json.loads((ROOT / "parameters.json").read_text())
T, C, H, I = (P[k] for k in ("tracker_interface", "contact", "housing", "contact_insert"))
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
    """Nominal purchased metal contact, including its two retaining shoulders.

    The intermediate lower taper is illustrative; all fit checks use conservative
    stepped pockets. Face, base, neck and tail dimensions come from Mill-Max.
    """
    z = T["contact_face_z"]
    base = z-C["base_to_face_height"]
    lower_taper_top = base+C["base_thickness"]+C["upper_taper_height"]
    neck_top = z-C["face_thickness"]-C["upper_taper_height"]
    profile = [
        (0, base-C["tail_length"]),
        (C["tail_diameter"]/2, base-C["tail_length"]),
        (C["tail_diameter"]/2, base),
        (C["base_diameter"]/2, base),
        (C["base_diameter"]/2, base+C["base_thickness"]),
        (C["neck_diameter"]/2, lower_taper_top),
        (C["neck_diameter"]/2, neck_top),
        (C["face_diameter"]/2, z-C["face_thickness"]),
        (C["face_diameter"]/2, z), (0, z)]
    return (cq.Workplane("XZ").polyline(profile).close()
            .revolve(360, (0, 0), (0, 1)).translate((x, T["contact_y"], 0)))


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

    # A 1/2-inch button-head screw has 4.5 mm exposed thread at the seating ring.
    bearing = H["mount_screw_engagement"]-H["mount_screw_length"]
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
    seat = T["contact_face_z"]-C["base_to_face_height"]
    for pin_x in [T["contact_x_ground_2"], T["contact_x_trigger_4"]]:
        # Splitting this stepped hole lets the gold pin be captured without glue,
        # force-fitting tiny printed holes, or heating a pin inside the housing.
        for d, low, high in [
            (I["tail_bore"], I["bottom_z"]-0.1, seat),
            (I["base_bore"], seat, 0.35),
            (I["neck_bore"], 0.35, 1.60),
            (I["upper_bore"], 1.60, I["top_z"]+0.1)]:
            if high > low:
                insert = insert.cut(cylinder(d, low, high, pin_x, y))
        # This open-back relief exposes about 2 mm of tail for a pre-soldered,
        # insulated joint. Keep the joint below the relief's upper shoulder.
        insert = insert.cut(cylinder(I["solder_relief_diameter"], I["bottom_z"]-0.1,
                                     I["solder_relief_top_z"], pin_x, y))
    gap = I["split_gap"]
    front = insert.intersect(box(50, 20-gap/2, -5, 5, x, y-10-gap/4))
    rear = insert.intersect(box(50, 20-gap/2, -5, 5, x, y+10+gap/4))
    return front.clean(), rear.clean()


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
    assembly = cq.Assembly(name="ELTS_VIVE_Tracker_3_Trigger_Connector_Rev_A")
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
    assembly.export(str(OUT/"ELTS_VIVE_Tracker_3_Trigger_Connector_Rev_A.step"))
    coupon = fit_coupon()
    cq.exporters.export(coupon.translate((0,0,3)), str(OUT/"mount_fit_coupon.stl"), tolerance=0.025)
    cq.exporters.export(coupon, str(OUT/"mount_fit_coupon.step"))
    (OUT/"cad_manifest.json").write_text(json.dumps(manifest, indent=2)+"\n")
    print(json.dumps(manifest, indent=2))


if __name__ == "__main__":
    main()
