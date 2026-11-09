"""Render assembled, exploded and detail previews (VTK, off-screen) from the exported STEP files.

Run after build_connector.py:  python render_connector.py
Writes previews/*.png. Renders are illustrations of the CAD, not photographs of printed parts.
"""
from __future__ import annotations

import json
from pathlib import Path

import cadquery as cq
import vtk

HERE = Path(__file__).resolve().parent
OUT, PREV = HERE / "output", HERE / "previews"
T = json.loads((HERE / "tracker_interface.json").read_text(encoding="utf-8"))
TRACKER_STEP = HERE.parent / "official-model" / "VIVE_Tracker_3.0_3D_STP" / "VIVE_Tracker_3.0_3D.stp"

COLORS = {"mount_base": (0.50, 0.56, 0.64), "contact_carrier": (0.95, 0.55, 0.15), "bottom_cover": (0.70, 0.75, 0.82),
          "pogo": (0.98, 0.80, 0.25), "screw": (0.35, 0.35, 0.38), "nut": (0.45, 0.45, 0.48), "tracker": (0.20, 0.20, 0.22)}
# Exploded offsets along Z (mm) per part group.
EXPLODE = {"mount_base": 0, "contact_carrier": -22, "pogo": 10, "bottom_cover": -48, "screw_m3": -70, "nut": 0, "screw_mount": -36}


def polydata(shape: cq.Shape, tol=0.05) -> vtk.vtkPolyData:
    verts, tris = shape.tessellate(tol, 0.2)
    pts = vtk.vtkPoints()
    for v in verts:
        pts.InsertNextPoint(v.x, v.y, v.z)
    cells = vtk.vtkCellArray()
    for a, b, c in tris:
        cells.InsertNextCell(3)
        for i in (a, b, c):
            cells.InsertCellPoint(i)
    pd = vtk.vtkPolyData()
    pd.SetPoints(pts)
    pd.SetPolys(cells)
    n = vtk.vtkPolyDataNormals()
    n.SetInputData(pd)
    n.SetFeatureAngle(35)
    n.Update()
    return n.GetOutput()


def actor(pd, color, opacity=1.0, dz=0.0, dx=0.0):
    m = vtk.vtkPolyDataMapper()
    m.SetInputData(pd)
    a = vtk.vtkActor()
    a.SetMapper(m)
    a.GetProperty().SetColor(*color)
    a.GetProperty().SetOpacity(opacity)
    a.GetProperty().SetAmbient(0.30)
    a.GetProperty().SetDiffuse(0.80)
    a.GetProperty().SetSpecular(0.15)
    a.SetPosition(dx, 0, dz)
    return a


def load(path: Path) -> cq.Shape:
    return cq.importers.importStep(str(path)).val()


def render(actors, path: Path, cam_pos, focal, up=(0, 0, 1), size=(1400, 1050), title=""):
    ren = vtk.vtkRenderer()
    ren.SetBackground(0.97, 0.97, 0.96)
    for a in actors:
        ren.AddActor(a)
    if title:
        t = vtk.vtkTextActor()
        t.SetInput(title)
        t.GetTextProperty().SetFontSize(26)
        t.GetTextProperty().SetColor(0.15, 0.15, 0.15)
        t.SetPosition(20, size[1] - 50)
        ren.AddViewProp(t)
    win = vtk.vtkRenderWindow()
    win.SetOffScreenRendering(1)
    win.AddRenderer(ren)
    win.SetSize(*size)
    cam = ren.GetActiveCamera()
    cam.SetPosition(*cam_pos)
    cam.SetFocalPoint(*focal)
    cam.SetViewUp(*up)
    ren.ResetCameraClippingRange()
    ren.SetAutomaticLightCreation(False)
    for pos, inten in (((1, -1, 2), 0.75), ((-1, 1, -1), 0.45)):   # key light + fill light, camera-relative
        light = vtk.vtkLight()
        light.SetLightTypeToCameraLight()
        light.SetPosition(*pos)
        light.SetIntensity(inten)
        ren.AddLight(light)
    win.Render()
    w2i = vtk.vtkWindowToImageFilter()
    w2i.SetInput(win)
    w2i.Update()
    wr = vtk.vtkPNGWriter()
    wr.SetFileName(str(path))
    wr.SetInputConnection(w2i.GetOutputPort())
    wr.Write()
    print("wrote", path.name)


def group(name: str) -> str:
    if name.startswith("pogo"):
        return "pogo"
    if name.startswith("m3x16"):
        return "screw_m3"
    if name.startswith("m3_nut"):
        return "nut"
    if name.startswith("mount_screw"):
        return "screw_mount"
    return name


def main():
    PREV.mkdir(exist_ok=True)
    parts = {p.stem: polydata(load(p)) for p in sorted((OUT / "parts").glob("*.step"))}
    hw = {p.stem: polydata(load(p), 0.02) for p in sorted((OUT / "hardware").glob("*.step"))}
    tracker = polydata(load(TRACKER_STEP).translate(cq.Vector(0, 0, -T["base_plane_z"])), 0.3)

    def col(name):
        g = group(name)
        return COLORS.get(g, COLORS.get(g.split("_")[0], COLORS["screw"])) if g not in COLORS else COLORS[g]

    # 1. Assembled on the tracker (tracker translucent), seen from below/side.
    acts = [actor(pd, COLORS[n]) for n, pd in parts.items()] + [actor(pd, col(n)) for n, pd in hw.items()]
    render(acts + [actor(tracker, COLORS["tracker"], 0.28)], PREV / "assembled_on_tracker.png",
           (95, -120, -60), (0, -2, 5), title="Rev E connector assembled on HTC VIVE Tracker 3.0 (tracker translucent)")
    # 2. Assembled connector alone, top view of the tracker-facing side.
    render(acts, PREV / "assembled_top.png", (55, -75, 80), (0, -4, -6),
           title="Assembled connector - tracker-facing side (pin, nose, contacts)")
    # 3. Assembled from below: fastener access.
    render(acts, PREV / "assembled_bottom.png", (60, -70, -95), (0, -4, -10),
           title="Underside - 1/4-20 key access, M3 heads, cable exit and tie anchor")
    # 4. Exploded view.
    ex = [actor(pd, COLORS[n], dz=EXPLODE[n]) for n, pd in parts.items()]
    # Nuts are shown pulled out sideways along their insertion slots.
    ex += [actor(pd, col(n), dz=EXPLODE[group(n)], dx=(-22 if "minusX" in n else 22) if group(n) == "nut" else 0.0) for n, pd in hw.items()]
    render(ex, PREV / "exploded.png", (150, -170, 10), (0, -5, -25), size=(1300, 1500),
           title="Exploded - contacts, carrier, base, nuts, 1/4-20 screw, cover, M3 screws")
    # 5. Carrier with contacts (soldering side, +Y open).
    car = [actor(parts["contact_carrier"], COLORS["contact_carrier"])] + [actor(pd, COLORS["pogo"]) for n, pd in hw.items() if n.startswith("pogo")]
    render(car, PREV / "carrier_solder_access.png", (-10, 30, 14), (-1.75, -16.5, -2),
           title="Contact carrier seen from +Y (open solder side): pin 4 left, pin 2 right")


if __name__ == "__main__":
    main()
