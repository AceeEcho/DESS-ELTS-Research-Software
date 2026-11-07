"""Build the Rev E VIVE Tracker 3.0 trigger connector (CadQuery).

Inputs : parameters.json (editable), tracker_interface.json (generated from HTC CAD by inspect_tracker.py)
Outputs: output/   STEP parts, hardware references, assembly STEP (with/without tracker), derived dimensions
         print/    one STL and one 3MF per printable part, already in print orientation

Frame ("seat Z"): X/Y = HTC tracker-model X/Y. Z = 0 is the connector's top face, which seats on the
tracker's broad flat underside (HTC CAD z = -2.50). +Z points into the tracker. All units mm.

Part summary
  mount_base      seats on the tracker; integral stabilizing pin; captive 1/4-20 screw bearing; M3 nut slots;
                  vertical channel that receives the contact carrier from below.
  contact_carrier holds the two Treedix contacts with their flat bases up on a plate that rises through the
                  base into the tracker's pogo port. Removable, and open on three sides below the plate, so the
                  barrels can be soldered and sleeved on the bench. Prints on its flat back face.
  bottom_cover    closes the underside, supports the carrier, traps the 1/4-20 head, carries the cable channel and
                  cable-tie anchor. Fixed by two M3 x 16 screws into nuts in the base.

Run (from this folder):  python build_connector.py [--with-tracker]
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
from pathlib import Path

import cadquery as cq
import trimesh

HERE = Path(__file__).resolve().parent
P = json.loads((HERE / "parameters.json").read_text(encoding="utf-8"))
T = json.loads((HERE / "tracker_interface.json").read_text(encoding="utf-8"))
G, HW, D, A = P["sourced_htc_guide"], P["hardware_nominal"], P["design"], P["print_allowance"]
OUT, PRINT = HERE / "output", HERE / "print"
TRACKER_STEP = HERE.parent / "official-model" / "VIVE_Tracker_3.0_3D_STP" / "VIVE_Tracker_3.0_3D.stp"


# --------------------------------------------------------------------------------------------
# Derived dimensions. Each entry records value and formula so the numbers stay traceable.
# --------------------------------------------------------------------------------------------
DERIVED: dict[str, dict] = {}


def derive(name: str, value: float, formula: str) -> float:
    DERIVED[name] = {"value": round(value, 4), "formula": formula}
    return value


def seat(z_model: float) -> float:
    """Convert an HTC-model Z to seat Z."""
    return z_model - T["base_plane_z"]


C, M3, NUT, MS = HW["pogo_contact"], HW["m3_screw"], HW["m3_nut"], HW["mount_screw"]

datum_a = derive("datum_a_z", seat(T["datum_a_z"]), "HTC CAD Datum A ring z - base plane z")
pad_plane = derive("contact_face_z", datum_a + G["pad_height_above_datum_a"], "datum_a_z + HTC pad height 2.595")
head_seat = derive("contact_base_seat_z", pad_plane - C["base_thickness"], "contact_face_z - Treedix base thickness")
barrel_bottom = derive("contact_barrel_bottom_z", head_seat - C["barrel_length"], "contact_base_seat_z - barrel length")
tip_bottom = derive("contact_tip_bottom_z", barrel_bottom - C["tip_exposed_length"], "barrel bottom - exposed tip length")
free_tip = derive("tracker_pin_tip_z_as_modelled", seat(T["pogo_pins"][0]["tip_lowest_z"]), "HTC CAD pin sphere lowest point")
derive("tracker_pin_compression_nominal", pad_plane - free_tip, "contact_face_z - tracker pin tip as modelled")

pocket_y = T["pogo_pocket"]["center_y"]
pins = {i + 1: p for i, p in enumerate(sorted(T["pogo_pins"], key=lambda p: p["x"]))}  # pin 1 = most negative X (see README)
contacts = {n: (pins[n]["x"], pins[n]["y"]) for n in G["connected_pins"]}
unconnected = [n for n in pins if n not in contacts]

screw_tip = derive("mount_screw_tip_z", datum_a + D["mount_screw_tip_above_datum_a"], "datum_a_z + chosen tip height above Datum A")
bearing_z = derive("mount_screw_bearing_face_z", screw_tip - MS["length_under_head"], "screw tip - 15.875 under-head length")
ms_head_bottom = derive("mount_screw_head_bottom_z", bearing_z - MS["head_height"], "bearing face - head height")
thread_start = derive("tracker_thread_start_z", seat(T["socket"]["bore_start_z"]), "HTC CAD socket bore start")
bore_bottom = derive("tracker_socket_bottom_z", seat(T["socket"]["bore_bottom_z"]), "HTC CAD socket bore bottom")
derive("mount_screw_engagement", screw_tip - thread_start, "tip - thread start")
derive("mount_screw_bottoming_margin", bore_bottom - screw_tip, "socket bottom - tip")
derive("mount_screw_bearing_stack", -bearing_z, "printed base material between head and tracker face")

stab_top = derive("stabilizing_pin_top_z", datum_a + G["stabilizing_pin_height_above_datum_a"], "datum_a_z + HTC 4.700")
stab_y = G["stabilizing_pin_from_socket_axis"]
derive("stabilizing_recess_floor_z", seat(T["stabilizing_recess"]["floor_z"]), "HTC CAD recess floor")

base_bottom = derive("base_bottom_z", -D["base_thickness"], "-base_thickness")
cover_bottom = derive("cover_bottom_z", base_bottom - D["cover_thickness"], "base bottom - cover thickness")
skin_under = -D["top_skin_thickness"]

m3_tip = D["m3_tip_z"]
m3_bearing = derive("m3_head_bearing_z", m3_tip - M3["length"], "m3_tip_z - 16")
derive("m3_head_recess_below_cover", m3_bearing - M3["head_height"] - cover_bottom, "head bottom - cover bottom (>= 0 means recessed)")
nut_top = derive("m3_nut_top_z", m3_tip - D["m3_tip_past_nut"], "tip - required protrusion past nut")
nut_bottom = derive("m3_nut_bearing_z", nut_top - NUT["thickness"], "nut top - nut thickness")
slot_top = derive("m3_nut_slot_top_z", nut_bottom + A["nut_slot_height"], "nut bearing + slot height")
blind_top = derive("m3_blind_hole_top_z", -A["m3_blind_hole_skin"], "-skin above screw tip")
derive("m3_clamp_grip", nut_bottom - m3_bearing, "nut bearing - head bearing")

xs = [p[0] for p in contacts.values()]
# Contact plate: its top face IS the seat for the Treedix bases (no posts). It is centred on the pogo
# pocket in Y and narrow enough to stay clear of the pin-window corner fillets above it.
nose_x0 = derive("plate_x_min", min(xs) - D["nose_end_margin_x"], "min contact x - end margin")
nose_x1 = derive("plate_x_max", max(xs) + D["nose_end_margin_x"], "max contact x + end margin")
nose_y0 = derive("carrier_back_face_y", pocket_y - D["plate_half_width_y"], "pogo pocket centre y - plate half width")
nose_y1 = pocket_y + D["plate_half_width_y"]
plate_top = derive("plate_top_z", head_seat, "= contact_base_seat_z")
plate_bottom = D["plate_bottom_z"]
derive("barrel_exposed_length", plate_bottom - barrel_bottom, "plate bottom - barrel bottom (solder zone)")
collar_top = derive("carrier_collar_top_z", skin_under - A["carrier_vertical_float"], "-top skin - float")
collar_bottom = collar_top - D["collar_thickness"]
# The carrier's -Y face (back wall, collar, legs) is one flat plane: it is the print bed face.
cx0, cx1 = nose_x0 - D["collar_overhang_x"], nose_x1 + D["collar_overhang_x"]
cy0, cy1 = nose_y0, nose_y1 + D["collar_overhang_plus_y"]
in_x0, in_x1 = nose_x0, nose_x1                                         # leg inner faces = plate ends
in_y0 = nose_y0 + D["back_wall"]                                        # back wall inner face
clr = A["carrier_channel_clearance"]
ch_x0, ch_x1, ch_y0, ch_y1 = cx0 - clr, cx1 + clr, cy0 - clr, cy1 + clr  # base channel


# --------------------------------------------------------------------------------------------
# Geometry helpers
# --------------------------------------------------------------------------------------------
def box(x0, x1, y0, y1, z0, z1) -> cq.Workplane:
    return cq.Workplane("XY").box(x1 - x0, y1 - y0, z1 - z0, centered=False).translate((x0, y0, z0))


def rbox(x0, x1, y0, y1, z0, z1, r) -> cq.Workplane:
    """Box with vertical edges rounded by r."""
    b = box(x0, x1, y0, y1, z0, z1)
    return b.edges("|Z").fillet(r) if r > 0 else b


def cyl(d, z0, z1, x=0.0, y=0.0) -> cq.Workplane:
    return cq.Workplane("XY", origin=(x, y, z0)).circle(d / 2).extrude(z1 - z0)


def hexagon(af, z0, z1, x, y) -> cq.Workplane:
    """Hex prism, across-flats af, flats parallel to X."""
    return cq.Workplane("XY", origin=(x, y, z0)).polygon(6, af / math.cos(math.pi / 6)).extrude(z1 - z0)


def footprint_wire() -> cq.Wire:
    """Tracker flat-underside outline (HTC CAD), inset, clipped to a compact size, corners rounded."""
    pts = [cq.Vector(x, y, 0) for x, y in T["base_plane_outline_xy"]]
    outline = cq.Wire.assembleEdges([cq.Edge.makeSpline(pts, periodic=True)])
    inset = outline.offset2D(-D["footprint_inset_from_tracker_flat"], "arc")[0]
    slab = cq.Solid.extrudeLinear(cq.Face.makeFromWires(inset), cq.Vector(0, 0, 1))
    clip = box(-D["footprint_clip_x"], D["footprint_clip_x"], D["footprint_clip_y_min"], D["footprint_clip_y_max"], -1, 2).val()
    top = cq.Workplane(obj=slab.intersect(clip)).faces(">Z").val()
    w = top.outerWire().translate((0, 0, -top.Center().z))
    r = D["footprint_corner_radius"]
    rounded = w.offset2D(-r, "arc")[0].offset2D(r, "arc")[0]
    # offset2D yields OCC "offset curves", which STEP (and Fusion) do not round-trip reliably.
    # Resample the finished outline into one periodic B-spline: a single clean edge.
    n = 160
    pts = [rounded.positionAt(i / n, "length") for i in range(n)]
    return cq.Wire.assembleEdges([cq.Edge.makeSpline(pts, periodic=True)])


FOOT = footprint_wire()


def prism(z0, z1) -> cq.Workplane:
    face = cq.Face.makeFromWires(FOOT.translate((0, 0, z0)))
    s = cq.Workplane(obj=cq.Solid.extrudeLinear(face, cq.Vector(0, 0, z1 - z0)))
    ch = D["outer_edge_chamfer"]
    return s.faces("<Z or >Z").edges().chamfer(ch) if ch > 0 else s


# --------------------------------------------------------------------------------------------
# Printed parts
# --------------------------------------------------------------------------------------------
def mount_base() -> cq.Workplane:
    b = prism(base_bottom, 0.0)

    # Carrier channel (open below) and the window through the top skin for the contact plate.
    # The window's -Y edge follows the channel: the carrier's flat back face needs no stop there.
    b = b.cut(box(ch_x0, ch_x1, ch_y0, ch_y1, base_bottom - 1, skin_under))
    w = A["nose_window_clearance"]
    b = b.cut(box(nose_x0 - w, nose_x1 + w, ch_y0, nose_y1 + w, skin_under - 0.01, 1))

    # 1/4-20 screw: counterbore from below, one-layer sacrificial bridge (pierce after printing), clearance hole.
    mem = A["sacrificial_bridge_layer"]
    b = b.cut(cyl(A["mount_head_counterbore_diameter"], base_bottom - 1, bearing_z))
    b = b.cut(cyl(A["mount_screw_hole_diameter"], bearing_z + mem, 1))

    # Two M3 joints: clearance from below, side-entry nut slot running to the outer edge, blind tip hole.
    for x, y in D["m3_axes_xy"]:
        s = 1 if x > 0 else -1
        b = b.cut(cyl(A["m3_clearance_diameter"], base_bottom - 1, blind_top, x, y))
        corner = NUT["across_flats"] / math.cos(math.pi / 6) / 2
        x_in = x - s * (corner + 0.15)
        b = b.cut(box(min(x_in, s * 40), max(x_in, s * 40), y - A["nut_slot_width"] / 2, y + A["nut_slot_width"] / 2, nut_bottom, slot_top))

    # Relief over HTC's friction-pad pocket (pad protrusion is unknown, so do not bear on it).
    fp = next(h for h in T["base_plane_holes"] if h["xmax"] < -3)
    m = D["friction_pad_relief_margin"]
    b = b.cut(box(fp["xmin"] - m, fp["xmax"] + m, fp["ymin"] - m, fp["ymax"] + m, -D["friction_pad_relief_depth"], 1))

    # Stabilizing pin (HTC accessory pin: dia 4.20, 4.700 above Datum A).
    pin = cyl(G["stabilizing_pin_diameter"], -0.5, stab_top, 0, stab_y).faces(">Z").edges().chamfer(D["stabilizing_pin_tip_chamfer"])
    return b.union(pin)


def contact_carrier() -> cq.Workplane:
    # Two legs carry the carrier on the cover; between them the wires drop into the cover's bay.
    c = box(cx0, in_x0, cy0, cy1, base_bottom, collar_bottom).union(box(in_x1, cx1, cy0, cy1, base_bottom, collar_bottom))
    # Collar: frame that stops upward travel against the underside of the base's top skin (+Y side and X ends).
    collar = box(cx0, cx1, cy0, cy1, collar_bottom, collar_top)
    collar = collar.cut(box(in_x0, in_x1, in_y0, cy1 - D["collar_plus_y_bar"], collar_bottom - 1, collar_top + 1))
    c = c.union(collar)
    # Back wall (-Y) rising to the plate; the +Y side and both X ends stay open so each barrel can be
    # soldered and sleeved on the bench.
    c = c.union(box(nose_x0, nose_x1, nose_y0, in_y0, collar_top - 0.01, plate_top))
    # Contact plate: the Treedix flat bases sit on its top face; barrels pass through.
    c = c.union(box(nose_x0, nose_x1, nose_y0, nose_y1, plate_bottom, plate_top))
    for x, y in contacts.values():
        c = c.cut(cyl(A["barrel_hole_diameter"], plate_bottom - 1, plate_top + 1, x, y))
    # Relief holes for any unconnected tracker pin that lies over the plate.
    for n in unconnected:
        x, y = pins[n]["x"], pins[n]["y"]
        if nose_x0 < x < nose_x1:
            c = c.cut(cyl(D["unconnected_pin_relief_diameter"], plate_bottom - 1, plate_top + 1, x, y))
    # Pin labels on the outer faces of the legs (pin 2 at -X, pin 4 at +X for this tracker orientation).
    zc, yc, dep, size = 0.5 * (base_bottom + collar_bottom), 0.5 * (cy0 + cy1), D["label_depth"], D["label_text_size"]
    for n, (x, _) in contacts.items():
        if x < 0:
            pl = cq.Plane(origin=(cx0, yc, zc), xDir=(0, -1, 0), normal=(-1, 0, 0))
        else:
            pl = cq.Plane(origin=(cx1, yc, zc), xDir=(0, 1, 0), normal=(1, 0, 0))
        c = c.cut(cq.Workplane(pl).text(str(n), size, -dep, combine=False, halign="center", valign="center"))
    return c


def bottom_cover() -> cq.Workplane:
    v = prism(cover_bottom, base_bottom)
    top, floor = base_bottom, base_bottom - D["wire_bay_depth"]
    # Wire bay under the carrier channel, kept inside the carrier legs so the legs bear on solid cover.
    i = D["wire_bay_inset_from_legs"]
    v = v.cut(rbox(in_x0 + i, in_x1 - i, ch_y0, ch_y1, floor, top + 1, 1.0))
    # Cable channel to the -Y edge, widened into a pocket where the cable tie and its head sit.
    hw = D["cable_channel_width"] / 2
    v = v.cut(box(-hw, hw, -60, ch_y0 + 0.5, floor, top + 1))
    ty, tp = D["tie_anchor_y"], D["tie_pocket_half_width_x"]
    v = v.cut(box(-tp, tp, ty - 1.8, ch_y0 + 0.01, floor, top + 1))
    # Tie slots through the floor and a groove underneath so the tie strap stays below flush.
    sx, sy = A["tie_slot_x"], A["tie_slot_y"]
    for s in (-1, 1):
        v = v.cut(box(s * D["tie_slot_center_x"] - sx / 2, s * D["tie_slot_center_x"] + sx / 2, ty - sy / 2, ty + sy / 2, cover_bottom - 1, floor + 0.01))
    gx = D["tie_slot_center_x"] + sx / 2
    v = v.cut(box(-gx, gx, ty - sy / 2, ty + sy / 2, cover_bottom - 1, cover_bottom + A["tie_groove_depth"]))
    # 1/4-20 head pocket (keeps the screw captive) and hex-key access hole.
    pocket_floor = ms_head_bottom - A["mount_head_drop_gap"]
    v = v.cut(cyl(A["mount_head_cover_pocket_diameter"], pocket_floor, top + 1))
    v = v.cut(cyl(A["allen_access_diameter"], cover_bottom - 1, pocket_floor + 0.01))
    # M3 head counterbores from below, one-layer sacrificial bridge, clearance holes.
    mem = A["sacrificial_bridge_layer"]
    for x, y in D["m3_axes_xy"]:
        v = v.cut(cyl(A["m3_head_counterbore_diameter"], cover_bottom - 1, m3_bearing, x, y))
        v = v.cut(cyl(A["m3_clearance_diameter"], m3_bearing + mem, top + 1, x, y))
    return v


# --------------------------------------------------------------------------------------------
# Hardware references (simplified envelopes; threads not modelled)
# --------------------------------------------------------------------------------------------
def pogo_contact(x, y) -> cq.Workplane:
    h = cyl(C["base_diameter"], head_seat, pad_plane, x, y)
    h = h.union(cyl(C["barrel_diameter"], barrel_bottom, head_seat + 0.01, x, y))
    return h.union(cyl(C["tip_diameter"], tip_bottom, barrel_bottom + 0.01, x, y))


def mount_screw() -> cq.Workplane:
    hd, hh = MS["head_diameter"], MS["head_height"]
    # Button head approximated by a spherical cap whose base circle equals the head diameter.
    rs = (hh ** 2 + (hd / 2) ** 2) / (2 * hh)
    sphere = cq.Workplane("XY").sphere(rs).translate((0, 0, bearing_z - hh + rs))
    head = sphere.intersect(cyl(hd, bearing_z - hh, bearing_z))
    head = head.cut(hexagon(MS["hex_key"], bearing_z - hh - 1, bearing_z - hh + 2.0, 0, 0))
    return head.union(cyl(MS["major_diameter"], bearing_z - 0.01, screw_tip))


def m3_screw(x, y) -> cq.Workplane:
    head = cyl(M3["head_diameter"], m3_bearing - M3["head_height"], m3_bearing, x, y)
    head = head.cut(hexagon(M3["hex_key"], m3_bearing - M3["head_height"] - 1, m3_bearing - M3["head_height"] + 1.5, x, y))
    return head.union(cyl(M3["diameter"], m3_bearing - 0.01, m3_tip, x, y))


def m3_nut(x, y) -> cq.Workplane:
    # Flats parallel to X (across-flats measured in Y) so the nut slides along the X-running slot.
    n = hexagon(NUT["across_flats"], nut_bottom, nut_top, x, y)
    return n.cut(cyl(M3["diameter"], nut_bottom - 1, nut_top + 1, x, y))


# --------------------------------------------------------------------------------------------
def sha(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


PRINT_ROTATION = {"contact_carrier": ((1, 0, 0), 90.0)}   # carrier prints on its flat -Y back face
PRINT_NOTE = {"mount_base": "top face up (stabilizing pin up), as modelled",
              "bottom_cover": "bottom face down, as modelled",
              "contact_carrier": "lying on its flat back (-Y) face; contact seat face is vertical"}


def print_oriented(name: str, shape: cq.Workplane) -> cq.Shape:
    """Rotate into print orientation, put the lowest face on Z = 0 and centre in XY."""
    s = shape.val()
    if name in PRINT_ROTATION:
        axis, ang = PRINT_ROTATION[name]
        s = s.rotate(cq.Vector(0, 0, 0), cq.Vector(*axis), ang)
    bb = s.BoundingBox()
    return s.translate(cq.Vector(-0.5 * (bb.xmin + bb.xmax), -0.5 * (bb.ymin + bb.ymax), -bb.zmin))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--with-tracker", action="store_true", help="also write an assembly STEP that includes HTC's tracker")
    args = ap.parse_args()
    (OUT / "parts").mkdir(parents=True, exist_ok=True)
    (OUT / "hardware").mkdir(parents=True, exist_ok=True)
    PRINT.mkdir(parents=True, exist_ok=True)

    printed = {
        "mount_base": (mount_base(), cq.Color(0.20, 0.22, 0.25)),
        "contact_carrier": (contact_carrier(), cq.Color(0.85, 0.45, 0.10)),
        "bottom_cover": (bottom_cover(), cq.Color(0.30, 0.34, 0.40)),
    }
    pin_name = {2: "GND_to_COM", 4: "TRIGGER_to_NO"}
    hardware = {f"pogo_contact_pin{n}_{pin_name[n]}": (pogo_contact(*xy), cq.Color(0.85, 0.70, 0.20)) for n, xy in contacts.items()}
    hardware["mount_screw_1_4_20x5_8"] = (mount_screw(), cq.Color(0.55, 0.55, 0.58))
    for i, (x, y) in enumerate(D["m3_axes_xy"]):
        side = "minusX" if x < 0 else "plusX"
        hardware[f"m3x16_screw_{side}"] = (m3_screw(x, y), cq.Color(0.55, 0.55, 0.58))
        hardware[f"m3_nut_{side}"] = (m3_nut(x, y), cq.Color(0.65, 0.65, 0.68))

    manifest = {"name": P["name"], "frame": "seat Z: 0 = connector top = tracker flat underside (HTC z -2.50)", "printed": {}, "hardware": {}}
    asm = cq.Assembly(name=P["name"])
    hw_asm = cq.Assembly(name="HARDWARE_REFERENCE_not_printed")
    for name, (wp, col) in printed.items():
        solid = wp.val()
        assert solid.isValid(), name
        step = OUT / "parts" / f"{name}.step"
        cq.exporters.export(wp, str(step))
        asm.add(wp, name=f"PRINT_{name}", color=col)
        po = print_oriented(name, wp)
        stl, tmf = PRINT / f"{P['name']}_{name}.stl", PRINT / f"{P['name']}_{name}.3mf"
        cq.exporters.export(po, str(stl), tolerance=0.01, angularTolerance=0.1)
        # CadQuery's 3MF writer can leave unmerged seam vertices; write the 3MF from the welded STL mesh.
        mesh = trimesh.load(stl, force="mesh")
        mesh.merge_vertices()
        mesh.export(tmf)
        bb = solid.BoundingBox()
        manifest["printed"][name] = {"volume_mm3": round(solid.Volume(), 2),
                                     "bbox_seat_frame": [round(v, 3) for v in (bb.xmin, bb.ymin, bb.zmin, bb.xmax, bb.ymax, bb.zmax)],
                                     "print_orientation": PRINT_NOTE[name],
                                     "step": step.name, "stl": stl.name, "3mf": tmf.name, "stl_sha256": sha(stl)}
    for name, (wp, col) in hardware.items():
        assert wp.val().isValid(), name
        cq.exporters.export(wp, str(OUT / "hardware" / f"{name}.step"))
        hw_asm.add(wp, name=f"HW_{name}", color=col)
        manifest["hardware"][name] = {"volume_mm3": round(wp.val().Volume(), 3)}
    asm.add(hw_asm)
    asm_path = OUT / f"{P['name']}.step"
    asm.export(str(asm_path))
    manifest["assembly_step"] = asm_path.name

    if args.with_tracker:
        tracker = cq.importers.importStep(str(TRACKER_STEP)).translate((0, 0, -T["base_plane_z"]))
        asm.add(tracker, name="REFERENCE_HTC_VIVE_Tracker_3_0_not_printed", color=cq.Color(0.15, 0.15, 0.15, 0.6))
        p2 = OUT / f"{P['name']}_with_tracker_reference.step"
        asm.export(str(p2))
        manifest["assembly_with_tracker_step"] = p2.name

    (OUT / "derived_dimensions.json").write_text(json.dumps(DERIVED, indent=1), encoding="utf-8")
    (OUT / "manifest.json").write_text(json.dumps(manifest, indent=1), encoding="utf-8")
    for k, v in DERIVED.items():
        print(f"{k:40s} {v['value']:9.3f}   {v['formula']}")
    print(json.dumps(manifest["printed"], indent=1))


if __name__ == "__main__":
    main()
