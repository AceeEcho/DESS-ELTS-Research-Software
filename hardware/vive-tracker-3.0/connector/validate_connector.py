"""Check exported CAD/mesh integrity and nominal fit against HTC's STEP model.

These checks establish digital geometry only. They cannot establish printer
accuracy, spring force, electrical contact reliability, or a physical fit.
"""
from pathlib import Path
import hashlib
import json
import cadquery as cq
import vtk
import build_connector as design

ROOT = Path(__file__).resolve().parent
OUT = ROOT / 'output'
NAMES = ['body', 'insert_front', 'insert_rear', 'wire_cover']
T, C, H, I = design.T, design.C, design.H, design.I


def mesh_check(path):
    reader=vtk.vtkSTLReader(); reader.SetFileName(str(path)); reader.Update()
    clean=vtk.vtkCleanPolyData(); clean.SetInputConnection(reader.GetOutputPort()); clean.Update()
    edges=vtk.vtkFeatureEdges(); edges.SetInputConnection(clean.GetOutputPort())
    edges.BoundaryEdgesOn(); edges.NonManifoldEdgesOn(); edges.FeatureEdgesOff(); edges.ManifoldEdgesOff(); edges.Update()
    regions=vtk.vtkPolyDataConnectivityFilter(); regions.SetInputConnection(clean.GetOutputPort())
    regions.SetExtractionModeToAllRegions(); regions.Update()
    mass=vtk.vtkMassProperties(); mass.SetInputConnection(clean.GetOutputPort()); mass.Update()
    return {'open_or_nonmanifold_edges':edges.GetOutput().GetNumberOfCells(),
            'connected_regions':regions.GetNumberOfExtractedRegions(),
            'volume_mm3':mass.GetVolume(), 'triangles':clean.GetOutput().GetNumberOfCells(),
            'minimum_z_mm':clean.GetOutput().GetBounds()[4]}


def main():
    tracker_path=ROOT.parent/'official-model/VIVE_Tracker_3.0_3D_STP/VIVE_Tracker_3.0_3D.stp'
    tracker=cq.importers.importStep(str(tracker_path)).val().translate((0,0,-T['mount_face_in_official_step_z']))
    parts={name:cq.importers.importStep(str(OUT/f'{name}.step')).val() for name in NAMES}
    report={'status':'CAD checks only; physical fit and electrical operation untested',
            'tracker_step_sha256':hashlib.sha256(tracker_path.read_bytes()).hexdigest(),
            'printed_parts':{}, 'pair_overlaps_mm3':{}, 'contact_checks':{}}
    for name,s in parts.items():
        mesh=mesh_check(OUT/f'{name}.stl')
        hit=s.intersect(tracker).Volume()
        report['printed_parts'][name]={'valid_solid':s.isValid(), 'solids':len(s.Solids()),
                                      'tracker_overlap_mm3':hit, 'mesh':mesh}
        assert s.isValid() and len(s.Solids())==1, name
        assert hit<1e-4, (name,'intersects tracker',hit)
        assert mesh['open_or_nonmanifold_edges']==0 and mesh['connected_regions']==1, name
        assert abs(mesh['volume_mm3']/s.Volume()-1)<0.01, (name,'mesh volume')
        assert abs(mesh['minimum_z_mm'])<1e-5, (name,'not on print bed')
    for a,n in enumerate(NAMES):
        for m in NAMES[a+1:]:
            volume=parts[n].intersect(parts[m]).Volume()
            report['pair_overlaps_mm3'][f'{n}/{m}']=volume
            assert volume<1e-5, (n,m,volume)
    for name,x in [('pin_2_ground',T['contact_x_ground_2']),('pin_4_trigger',T['contact_x_trigger_4'])]:
        pin=design.contact(x).val()
        for part in parts.values(): assert pin.intersect(part).Volume()<1e-5
        hit=pin.intersect(tracker)
        # HTC's model contains the unloaded spring. Deliberate overlap is allowed
        # only at that contact, not at the surrounding plastic or adjacent pins.
        allowed=design.box(1.5,1.5,1,3,x,-16.520189036345).val()
        outside=hit.cut(allowed).Volume()
        assert hit.Volume()>0 and outside<1e-5, name
        report['contact_checks'][name]={'free_spring_overlap_mm3':hit.Volume(),
              'overlap_outside_intended_contact_mm3':outside,
              'face_height_mm':T['contact_face_z'],
              'solder_tail_accessible_length_mm':I['solder_relief_top_z']-(T['contact_face_z']-C['base_to_face_height']-C['tail_length'])}

    # Envelope check of the bought fasteners. Threads are intentionally simplified.
    bearing=H['mount_screw_engagement']-H['mount_screw_length']
    main_shaft=design.cylinder(6.35,bearing,H['mount_screw_engagement']).val()
    main_head=design.cylinder(11.1,bearing-3.36,bearing).val()
    for part in parts.values():
        assert main_shaft.intersect(part).Volume()<1e-5
        assert main_head.intersect(part).Volume()<1e-5
    assert main_shaft.intersect(tracker).Volume()<1e-4
    for x in [-H['cover_screw_x'],H['cover_screw_x']]:
        bolt=design.cylinder(3,-14,-2,x,H['cover_screw_y']).val()
        for part in parts.values(): assert bolt.intersect(part).Volume()<1e-5
    report['fasteners']={'mount_screw_exposed_thread_mm':H['mount_screw_engagement'],
                         'cover_screws':'M3 x 12; 2.4 mm nut engagement nominal',
                         'rigid_envelopes_clear':True}
    assembly=cq.importers.importStep(str(OUT/'ELTS_VIVE_Tracker_3_Trigger_Connector_Rev_A.step')).val()
    assert len(assembly.Solids())==6 and assembly.isValid()
    report['assembly_step_solid_count']=len(assembly.Solids())
    report['passed']=True
    (OUT/'verification.json').write_text(json.dumps(report,indent=2)+'\n')
    print(json.dumps(report,indent=2))


if __name__=='__main__': main()
