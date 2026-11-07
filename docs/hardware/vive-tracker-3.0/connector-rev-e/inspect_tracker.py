"""Derive the VIVE Tracker 3.0 underside interface from HTC's official STEP model.

Writes ``tracker_interface.json``. Every value is measured from the CAD by
recognising analytic faces (planes, cylinders, cones, spheres); nothing here is
typed in by hand. Units are millimetres in HTC's model frame:

* +Z points from the mounting face up into the tracker body.
* The 1/4-20 socket axis is the Z axis (X = Y = 0).
* The stabilizing recess lies on +Y, the pogo pin row on -Y.

Run (from this folder):  python inspect_tracker.py
"""
from __future__ import annotations

import hashlib
import json
import math
from pathlib import Path

import cadquery as cq
from OCP.BRepAdaptor import BRepAdaptor_Surface
from OCP.GeomAbs import GeomAbs_Cone, GeomAbs_Cylinder, GeomAbs_Plane, GeomAbs_Sphere, GeomAbs_Torus

HERE = Path(__file__).resolve().parent
TRACKER_STEP = HERE.parent / "official-model" / "VIVE_Tracker_3.0_3D_STP" / "VIVE_Tracker_3.0_3D.stp"
OUT = HERE / "tracker_interface.json"


def surface(face):
    return BRepAdaptor_Surface(face.wrapped)


def load_tracker() -> cq.Shape:
    return cq.importers.importStep(str(TRACKER_STEP)).val()


def classify(tracker: cq.Shape):
    """Collect the analytic faces near the underside (Z < 8 mm)."""
    planes, cylinders, cones, spheres, tori = [], [], [], [], []
    for f in tracker.Faces():
        bb = f.BoundingBox()
        if bb.zmin > 8.0:
            continue
        s = surface(f)
        t = s.GetType()
        if t == GeomAbs_Plane:
            planes.append((f, bb))
        elif t == GeomAbs_Cylinder:
            c = s.Cylinder()
            p, d = c.Axis().Location(), c.Axis().Direction()
            cylinders.append(dict(face=f, bb=bb, r=c.Radius(), x=p.X(), y=p.Y(), axis_z=abs(d.Z()) > 0.99))
        elif t == GeomAbs_Cone:
            c = s.Cone()
            a = c.Apex()
            cones.append(dict(face=f, bb=bb, semi_deg=math.degrees(c.SemiAngle()), x=a.X(), y=a.Y()))
        elif t == GeomAbs_Sphere:
            sp = s.Sphere()
            c = sp.Location()
            spheres.append(dict(face=f, bb=bb, r=sp.Radius(), x=c.X(), y=c.Y(), z=c.Z()))
        elif t == GeomAbs_Torus:
            to = s.Torus()
            c = to.Location()
            tori.append(dict(face=f, bb=bb, R=to.MajorRadius(), r=to.MinorRadius(), x=c.X(), y=c.Y(), z=c.Z()))
    return planes, cylinders, cones, spheres, tori


def r3(v):
    return round(float(v), 3)


def main():
    tracker = load_tracker()
    planes, cylinders, cones, spheres, tori = classify(tracker)

    # 1. Broad flat underside: largest horizontal plane at the lowest Z.
    # (Bounding boxes carry a small tolerance, so "horizontal" allows 1e-3 mm of Z spread.)
    horiz = [(f, bb) for f, bb in planes if bb.zmax - bb.zmin < 1e-3]
    broad = [(f, bb) for f, bb in horiz if f.Area() > 500.0]
    base_face, base_bb = min(broad, key=lambda fb: fb[1].zmin)
    base_z = base_bb.zmin
    outline = base_face.outerWire()
    outline_pts = [outline.positionAt(i / 180.0) for i in range(180)]
    holes = []
    for w in base_face.innerWires():
        b = w.BoundingBox()
        holes.append(dict(xmin=r3(b.xmin), xmax=r3(b.xmax), ymin=r3(b.ymin), ymax=r3(b.ymax)))

    # 2. 1/4-20 socket: vertical cylinder at the origin with r ~ 3.175 (6.35 mm major diameter).
    bore = [c for c in cylinders if c["axis_z"] and abs(c["x"]) < 1e-3 and abs(c["y"]) < 1e-3 and abs(c["r"] - 3.175) < 0.05]
    bore_zmin = min(c["bb"].zmin for c in bore)
    bore_zmax = max(c["bb"].zmax for c in bore)
    # Datum A: HTC defines it as the top surface of the ring around the 1/4" nut.
    # In the CAD that is the annular plane between r 3.75 and r 4.8 at the socket.
    ring = [bb for f, bb in horiz if abs(bb.xmax - 4.8) < 0.05 and abs(bb.xmin + 4.8) < 0.05]
    datum_a_z = ring[0].zmin
    nut_pocket_r = max(c["r"] for c in cylinders if c["axis_z"] and abs(c["x"]) < 1e-3 and abs(c["y"]) < 1e-3 and c["r"] < 6)

    # 3. Stabilizing recess: tapered cone (small semi-angle) on +Y.
    recess_cone = [c for c in cones if c["y"] > 5 and c["semi_deg"] < 10]
    recess_y = recess_cone[0]["y"]
    recess_floor = [bb for f, bb in horiz if bb.ymin > 5 and abs(0.5 * (bb.ymin + bb.ymax) - recess_y) < 0.1 and bb.xmax - bb.xmin < 5.0]
    recess_floor_z = max(bb.zmin for bb in recess_floor)
    cone_zmin = min(c["bb"].zmin for c in recess_cone)
    cone_zmax = max(c["bb"].zmax for c in recess_cone)
    r_top = max(c["bb"].xmax for c in recess_cone)  # radius at the cone's lower (entry) end
    semi = math.radians(recess_cone[0]["semi_deg"])
    r_bottom = r_top - (cone_zmax - cone_zmin) * math.tan(semi)

    # 4. Pogo pins: outer spherical tips (largest spheres on -Y).
    tips = [s for s in spheres if s["y"] < -10]
    r_tip = max(s["r"] for s in tips)
    tips = sorted({(r3(s["x"]), r3(s["y"]), r3(s["z"])) for s in tips if abs(s["r"] - r_tip) < 1e-6})
    pins = [dict(x=x, y=y, tip_lowest_z=r3(z - r_tip)) for x, y, z in tips]

    # 5. Pogo pocket: stadium formed by vertical cylinders on -Y at x = +/- c.
    pocket_cyl = [c for c in cylinders if c["axis_z"] and c["y"] < -10 and 3.0 < c["r"] < 6.0]
    pocket_center_y = pocket_cyl[0]["y"]
    pocket_end_x = abs(pocket_cyl[0]["x"])
    pocket_radii = sorted({r3(c["r"]) for c in pocket_cyl})

    # 6. Friction-pad pocket = the base-plane hole that is neither socket, recess nor pogo pocket.
    pad = [h for h in holes if h["xmax"] < -3]

    data = {
        "_about": "Measured from HTC VIVE_Tracker_3.0_3D.stp by inspect_tracker.py. HTC model frame, mm. Do not hand-edit.",
        "source_step": str(TRACKER_STEP.relative_to(HERE.parent.parent.parent.parent)).replace("\\", "/"),
        "source_step_sha256": hashlib.sha256(TRACKER_STEP.read_bytes()).hexdigest(),
        "base_plane_z": r3(base_z),
        "base_plane_outline_xy": [[r3(p.x), r3(p.y)] for p in outline_pts],
        "base_plane_holes": holes,
        "datum_a_z": r3(datum_a_z),
        "socket": {"bore_radius": 3.175, "bore_start_z": r3(bore_zmin), "bore_bottom_z": r3(bore_zmax),
                   "nut_pocket_radius": r3(nut_pocket_r)},
        "stabilizing_recess": {"x": 0.0, "y": r3(recess_y), "entry_radius": r3(r_top),
                               "bottom_radius": r3(r_bottom), "taper_semi_angle_deg": r3(math.degrees(semi)),
                               "floor_z": r3(recess_floor_z)},
        "pogo_pins": pins,
        "pogo_pin_tip_radius": r3(r_tip),
        "pogo_pocket": {"center_y": r3(pocket_center_y), "end_center_x": r3(pocket_end_x), "radii": pocket_radii},
        "friction_pad_pocket": pad[0] if pad else None,
        "tracker_bbox": {k: r3(getattr(tracker.BoundingBox(), k)) for k in ("xmin", "xmax", "ymin", "ymax", "zmin", "zmax")},
    }
    OUT.write_text(json.dumps(data, indent=1), encoding="utf-8")
    print(json.dumps({k: v for k, v in data.items() if k != "base_plane_outline_xy"}, indent=1))


if __name__ == "__main__":
    main()
