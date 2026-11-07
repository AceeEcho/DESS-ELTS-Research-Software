"""Digital checks for the Rev E connector. Writes output/verification.json.

These are CAD/mesh checks against nominal geometry and HTC's tracker model. They are NOT physical fit,
print-accuracy, contact-reliability, strength or electrical tests; none of those have been performed.

Run after build_connector.py:  python validate_connector.py
"""
from __future__ import annotations

import json
import math
from pathlib import Path

import cadquery as cq
import trimesh
from OCP.BRepExtrema import BRepExtrema_DistShapeShape

import build_connector as B

HERE = Path(__file__).resolve().parent
OUT, PRINT = HERE / "output", HERE / "print"
VOL_TOL = 1e-3       # mm^3 below which an intersection counts as touching, not overlapping
RESULTS: list[dict] = []


def check(name, ok, category, **values):
    RESULTS.append({"check": name, "category": category, "result": ("PASS" if ok else "FAIL") if ok is not None else "INFO", **values})
    tag = RESULTS[-1]["result"]
    print(f"[{tag:4s}] {category:18s} {name}: " + ", ".join(f"{k}={v}" for k, v in values.items()))


def load(p: Path) -> cq.Shape:
    return cq.importers.importStep(str(p)).val()


def boxes_overlap(a: cq.Shape, b: cq.Shape, pad=0.01) -> bool:
    p, q = a.BoundingBox(), b.BoundingBox()
    return not (p.xmax < q.xmin - pad or q.xmax < p.xmin - pad or p.ymax < q.ymin - pad or q.ymax < p.ymin - pad
                or p.zmax < q.zmin - pad or q.zmax < p.zmin - pad)


def common_volume(a: cq.Shape, b: cq.Shape) -> float:
    if not boxes_overlap(a, b):
        return 0.0
    try:
        return a.intersect(b).Volume()
    except Exception:  # noqa: BLE001 - OCC may raise on empty results
        return 0.0


def distance(a: cq.Shape, b: cq.Shape) -> float:
    d = BRepExtrema_DistShapeShape(a.wrapped, b.wrapped)
    d.Perform()
    return d.Value()


def r(v, n=3):
    return round(float(v), n)


def point_in_poly(x, y, poly):
    inside = False
    j = len(poly) - 1
    for i in range(len(poly)):
        xi, yi = poly[i]
        xj, yj = poly[j]
        if (yi > y) != (yj > y) and x < (xj - xi) * (y - yi) / (yj - yi) + xi:
            inside = not inside
        j = i
    return inside


def main():
    parts = {p.stem: load(p) for p in sorted((OUT / "parts").glob("*.step"))}
    hw = {p.stem: load(p) for p in sorted((OUT / "hardware").glob("*.step"))}
    tracker = load(B.TRACKER_STEP).translate(cq.Vector(0, 0, -B.T["base_plane_z"]))

    # 1. Valid solids --------------------------------------------------------------------------
    for name, s in {**parts, **hw}.items():
        n = len(s.Solids())
        check(f"valid solid: {name}", s.isValid() and n == 1 and s.Volume() > 0, "solid", solids=n, volume_mm3=r(s.Volume(), 2))

    # 2. Printable meshes (print-oriented STL and 3MF) -----------------------------------------
    for name, s in parts.items():
        for ext in ("stl", "3mf"):
            m = trimesh.load(PRINT / f"{B.P['name']}_{name}.{ext}", force="mesh")
            ok = (m.is_watertight and m.is_winding_consistent and m.volume > 0 and abs(m.volume - s.Volume()) / s.Volume() < 0.01
                  and len(m.split(only_watertight=False)) == 1 and abs(m.bounds[0][2]) < 1e-6)
            ext_xy = m.bounds[1][:2] - m.bounds[0][:2]
            check(f"mesh: {name}.{ext}", ok, "mesh", watertight=m.is_watertight, winding_consistent=m.is_winding_consistent,
                  bodies=len(m.split(only_watertight=False)), mesh_volume=r(m.volume, 1), cad_volume=r(s.Volume(), 1),
                  sits_on_bed=bool(abs(m.bounds[0][2]) < 1e-6), size_xyz=[r(v, 1) for v in m.extents], fits_180x180_bed=bool(max(ext_xy) < 180))

    # 3. Interference between all assembled components -----------------------------------------
    comps = {**{f"PRINT_{k}": v for k, v in parts.items()}, **{f"HW_{k}": v for k, v in hw.items()}}
    names = sorted(comps)
    # Intentional overlaps: one-layer print bridges that the builder pierces before fitting the screws.
    mem = B.A["sacrificial_bridge_layer"]
    expected = {("HW_mount_screw_1_4_20x5_8", "PRINT_mount_base"): math.pi * (B.MS["major_diameter"] / 2) ** 2 * mem}
    for k in [k for k in hw if k.startswith("m3x16")]:
        expected[(f"HW_{k}", "PRINT_bottom_cover")] = math.pi * (B.M3["diameter"] / 2) ** 2 * mem
    worst = 0.0
    overlaps, bridges = [], []
    for i, a in enumerate(names):
        for b in names[i + 1:]:
            v = common_volume(comps[a], comps[b])
            exp = expected.get((a, b))
            if exp is not None and abs(v - exp) < 0.01 * exp:
                bridges.append([a, b, r(v, 3)])
                continue
            worst = max(worst, v)
            if v > VOL_TOL:
                overlaps.append([a, b, r(v, 4)])
    check("component interference (all pairs, assembled)", not overlaps and len(bridges) == len(expected), "interference",
          pairs=len(names) * (len(names) - 1) // 2, overlapping=overlaps, max_unexpected_common_volume_mm3=r(worst, 5),
          sacrificial_bridges_to_pierce=bridges)

    # 4. Interference with HTC's tracker model -------------------------------------------------
    for name, s in parts.items():
        v = common_volume(s, tracker)
        check(f"tracker interference: {name}", v <= VOL_TOL, "interference", common_volume_mm3=r(v, 5))
    # Tracker pins 2 and 4 are modelled extended; in use the contact bases push them back. Remove just
    # those two pin bodies (r 0.7 around their axes) before measuring clearance to everything else.
    trk_wo = tracker
    for n in B.G["connected_pins"]:
        trk_wo = trk_wo.cut(cq.Solid.makeCylinder(0.7, 3.0, cq.Vector(B.pins[n]["x"], B.pins[n]["y"], B.free_tip - 0.1)))
    d = distance(parts["contact_carrier"], trk_wo)
    check("min clearance carrier to tracker (excl. pins 2/4 bodies)", d >= 0.3, "interference", min_distance_mm=r(d))
    # Stabilizing pin alone (rebuild just the pin for a clean distance).
    pin = B.cyl(B.G["stabilizing_pin_diameter"], 0.0, B.stab_top, 0, B.stab_y).faces(">Z").edges().chamfer(B.D["stabilizing_pin_tip_chamfer"]).val()
    check("stabilizing pin clearance in recess", distance(pin, tracker) > 0.15, "interference", min_distance_mm=r(distance(pin, tracker)),
          pin_top_z=r(B.stab_top), recess_floor_z=B.DERIVED["stabilizing_recess_floor_z"]["value"])
    # Seating: the base top face must actually meet the tracker's flat underside (distance zero, no overlap).
    check("base top seats on tracker flat underside", distance(parts["mount_base"], tracker) < 1e-6, "interference",
          min_distance_mm=r(distance(parts["mount_base"], tracker), 6))

    # 5. Contact alignment and pin mapping -----------------------------------------------------
    pins = B.pins
    pad = next(h for h in B.T["base_plane_holes"] if h["xmax"] < -3)
    check("pin numbering orientation", pins[1]["x"] < 0 and 0.5 * (pad["xmin"] + pad["xmax"]) < 0, "contact",
          rule="HTC p.13/14: viewed from below, pin 1 is on the friction-pad side; friction pad is at -X in HTC CAD",
          pin1_x=pins[1]["x"], pin2_x=pins[2]["x"], pin4_x=pins[4]["x"])
    for n in B.G["connected_pins"]:
        cname = next(k for k in hw if k.startswith(f"pogo_contact_pin{n}_"))
        c = hw[cname]
        bb = c.BoundingBox()
        cx, cy = 0.5 * (bb.xmin + bb.xmax), 0.5 * (bb.ymin + bb.ymax)
        off = math.hypot(cx - pins[n]["x"], cy - pins[n]["y"])
        comp = bb.zmax - B.free_tip
        # Overlap with the tracker must be the pin tip only (expected compression), located on this pin's axis.
        inter = c.intersect(tracker)
        ib = inter.BoundingBox() if inter.Volume() > 0 else None
        only_this_pin = ib is not None and math.hypot(0.5 * (ib.xmin + ib.xmax) - pins[n]["x"], 0.5 * (ib.ymin + ib.ymax) - pins[n]["y"]) < 0.05 \
            and (ib.xmax - ib.xmin) <= 2 * B.T["pogo_pin_tip_radius"] + 0.01
        check(f"contact pin {n} alignment", off < 0.01 and only_this_pin and abs(bb.zmax - B.pad_plane) < 1e-6, "contact",
              lateral_offset_mm=r(off), lateral_acceptance_mm=r(B.C["base_diameter"] / 2 - off),
              contact_face_z=r(bb.zmax), htc_pad_plane_z=r(B.pad_plane), modelled_pin_compression_mm=r(comp),
              overlap_is_this_pin_tip_only=only_this_pin)
    heads = [hw[k] for k in hw if k.startswith("pogo")]
    check("contact-to-contact separation", distance(heads[0], heads[1]) > 4.0, "contact", min_metal_gap_mm=r(distance(heads[0], heads[1])))
    for n in [k for k in pins if k not in B.G["connected_pins"]]:
        tip = cq.Solid.makeSphere(B.T["pogo_pin_tip_radius"], cq.Vector(pins[n]["x"], pins[n]["y"], B.free_tip + B.T["pogo_pin_tip_radius"]))
        dmin = min(distance(tip, h) for h in heads)
        dpl = distance(tip, parts["contact_carrier"])
        check(f"unconnected pin {n} untouched", dmin > 1.0 and dpl > 0.2, "contact", nearest_contact_mm=r(dmin), nearest_plastic_mm=r(dpl))

    # 6. Fastener engagement --------------------------------------------------------------------
    ms = hw["mount_screw_1_4_20x5_8"].BoundingBox()
    eng = ms.zmax - B.thread_start
    check("1/4-20 thread engagement", eng >= 3 * B.MS["pitch"], "fastener", engagement_mm=r(eng), threads=r(eng / B.MS["pitch"], 2),
          bottoming_margin_mm=r(B.bore_bottom - ms.zmax), note="tracker thread depth taken from HTC CAD bore (thread form not modelled)")
    check("1/4-20 bottoming margin", B.bore_bottom - ms.zmax >= 2.0, "fastener", margin_mm=r(B.bore_bottom - ms.zmax))
    check("1/4-20 head bears on base", distance(hw["mount_screw_1_4_20x5_8"], parts["mount_base"]) < 1e-6, "fastener",
          bearing_stack_mm=r(-B.bearing_z), stack="screw head -> printed base -> tracker flat underside")
    drop = B.ms_head_bottom - (B.ms_head_bottom - B.A["mount_head_drop_gap"])
    check("1/4-20 screw captive by cover", B.A["allen_access_diameter"] < B.MS["head_diameter"], "fastener",
          axial_play_mm=r(drop), access_hole_mm=B.A["allen_access_diameter"], head_mm=B.MS["head_diameter"])
    for k in [k for k in hw if k.startswith("m3x16")]:
        sb = hw[k].BoundingBox()
        nut = hw[k.replace("m3x16_screw", "m3_nut")].BoundingBox()
        past = sb.zmax - nut.zmax
        check(f"M3 engagement {k}", past >= 1.0 and sb.zmax < 0 and nut.zmin >= sb.zmin, "fastener",
              tip_past_nut_mm=r(past), full_nut_engaged=nut.zmin > B.m3_bearing, tip_z=r(sb.zmax), skin_above_tip_mm=r(-B.blind_top),
              head_recess_below_cover_mm=r(B.DERIVED["m3_head_recess_below_cover"]["value"]))

    # 7. Tool access and assembly paths -------------------------------------------------------
    hexkey = cq.Solid.makeCylinder(B.MS["hex_key"] / math.cos(math.pi / 6) / 2 + 0.05, B.ms_head_bottom + 60, cq.Vector(0, 0, -60))
    v = sum(common_volume(hexkey, p) for p in parts.values())
    check("5/32 in hex key reaches 1/4-20 screw from below", v <= VOL_TOL, "tool access", key_envelope_dia_mm=r(2 * hexkey.BoundingBox().xmax))
    for x, y in B.D["m3_axes_xy"]:
        key = cq.Solid.makeCylinder(B.M3["hex_key"] / math.cos(math.pi / 6) / 2 + 0.05, (B.m3_bearing - B.M3["head_height"]) + 60, cq.Vector(x, y, -60))
        head = cq.Solid.makeCylinder(B.M3["head_diameter"] / 2, B.m3_bearing + 60, cq.Vector(x, y, -60))
        v1 = sum(common_volume(key, p) for p in parts.values())
        v2 = common_volume(head, parts["bottom_cover"])
        check(f"M3 key and head path at x={x}", v1 <= VOL_TOL and v2 <= VOL_TOL, "tool access", key_overlap=r(v1, 4), head_path_overlap=r(v2, 4))
        s = 1 if x > 0 else -1
        nutsweep = B.hexagon(B.NUT["across_flats"], B.nut_bottom, B.nut_top, x, y).val()
        sweep = nutsweep.fuse(B.box(min(x, s * 25), max(x, s * 25), y - B.NUT["across_flats"] / 2, y + B.NUT["across_flats"] / 2, B.nut_bottom, B.nut_top).val())
        check(f"M3 nut slides in from side at x={x}", common_volume(sweep, parts["mount_base"]) <= VOL_TOL, "tool access")
    car = parts["contact_carrier"]
    path = max(common_volume(car.translate(cq.Vector(0, 0, -dz)), parts["mount_base"]) for dz in (0.5, 2, 5, 8, 11, 14, 20))
    check("carrier inserts into base from below", path <= VOL_TOL, "tool access", max_overlap_along_path=r(path, 5))
    for n in B.G["connected_pins"]:
        x, y = B.contacts[n]
        zone_lo, zone_hi = B.barrel_bottom, B.plate_bottom
        iron = B.box(x - 1.0, x + 1.0, y, y + 30, zone_lo, zone_hi).val()
        dia = B.D["insulation_envelope_diameter"]
        sleeve = cq.Solid.makeCylinder(dia / 2, zone_hi - zone_lo, cq.Vector(x, y, zone_lo))
        others = [cq.Solid.makeCylinder(dia / 2, zone_hi - zone_lo, cq.Vector(ox, oy, zone_lo)) for m, (ox, oy) in B.contacts.items() if m != n]
        apart = all(common_volume(sleeve, o) <= VOL_TOL for o in others)
        check(f"solder and insulation room, pin {n}", common_volume(iron, car) <= VOL_TOL and common_volume(sleeve, car) <= VOL_TOL and apart,
              "tool access", exposed_barrel_mm=r(zone_hi - zone_lo), iron_approach="+Y side and both X ends, 2 mm wide",
              insulation_envelope_dia_mm=dia, sleeves_kept_apart=apart, note="envelope is a design allowance; sleeve size not established")

    # 8. Tracker sensors / footprint ----------------------------------------------------------
    poly = B.T["base_plane_outline_xy"]
    fp = [(p.x, p.y) for p in (B.FOOT.positionAt(t / 400) for t in range(400))]
    inside = all(point_in_poly(x, y, poly) for x, y in fp)
    check("footprint inside tracker flat underside", inside, "sensors", inset_mm=B.D["footprint_inset_from_tracker_flat"])
    above = cq.Solid.makeBox(200, 200, 50, cq.Vector(-100, -100, 0))
    holes = B.T["base_plane_holes"]
    ok = True
    detail = {}
    for name, s in {**parts, **{k: v for k, v in hw.items() if k.startswith(("pogo", "mount"))}}.items():
        part = common_volume(s, above)
        if part <= VOL_TOL:
            continue
        bb = s.intersect(above).BoundingBox()
        within = any(h["xmin"] <= bb.xmin and bb.xmax <= h["xmax"] and h["ymin"] <= bb.ymin and bb.ymax <= h["ymax"] for h in holes)
        sub = [s.intersect(above).intersect(cq.Solid.makeBox(h["xmax"] - h["xmin"], h["ymax"] - h["ymin"], 50, cq.Vector(h["xmin"], h["ymin"], 0))).Volume() for h in holes]
        within = within or abs(sum(sub) - part) < 1e-3
        ok &= within
        detail[name] = r(part, 2)
    check("everything above the seat plane is inside tracker recesses", ok, "sensors", volumes_above_seat_mm3=detail,
          note="nothing rises beside the tracker; the connector stays below its flat underside")

    summary = {
        "scope": "Digital CAD/mesh checks against nominal hardware dimensions and HTC's tracker STEP. No part has been printed, fitted, or electrically tested.",
        "passed": sum(1 for x in RESULTS if x["result"] == "PASS"),
        "failed": sum(1 for x in RESULTS if x["result"] == "FAIL"),
        "not_performed_physical": [
            "print dimensional accuracy, hole sizes and fit of carrier/nose/pin on a real tracker",
            "actual tracker pogo-pin free length, stroke and force (compression above uses the CAD state)",
            "Treedix contact, screw, nut and cable-tie dimensions on the received parts",
            "1/4-20 thread engagement, tightening torque and printed-plastic creep under the screw head",
            "contact resistance, continuity, isolation between contacts, switch press/release recognition in SteamVR",
            "strain relief holding force; cable/sleeve fit in the 7 x 5 mm channel",
        ],
        "checks": RESULTS,
    }
    (OUT / "verification.json").write_text(json.dumps(summary, indent=1), encoding="utf-8")
    print(f"\n{summary['passed']} passed, {summary['failed']} failed (digital only)")


if __name__ == "__main__":
    main()
