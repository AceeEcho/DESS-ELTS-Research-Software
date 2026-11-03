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
              'stationary_barrel_accessible_length_mm':I['solder_relief_top_z']-(T['contact_face_z']-C['flange_thickness']-C['barrel_length']),
              'maximum_target_height_with_axial_clearance_mm':T['contact_face_z']+I['flange_axial_clearance']}

        # A conservative cylindrical allowance surrounds the pre-soldered barrel
        # and lead. It is a routing envelope, not a model of an actual joint.
        joint=design.cylinder(I['finished_joint_max_diameter'], I['bottom_z']-0.4,
            I['solder_relief_top_z']-0.05, x, T['contact_y']).val()
        for printed in parts.values(): assert joint.intersect(printed).Volume()<1e-5
        report['contact_checks'][name]['joint_envelope_diameter_mm']=I['finished_joint_max_diameter']

    coupon=cq.importers.importStep(str(OUT/'mount_fit_coupon.step')).val()
    coupon_mesh=mesh_check(OUT/'mount_fit_coupon.stl')
    assert coupon.isValid() and len(coupon.Solids())==1
    assert coupon.intersect(tracker).Volume()<1e-5
    assert coupon_mesh['open_or_nonmanifold_edges']==0 and coupon_mesh['connected_regions']==1
    report['coupon_mesh']=coupon_mesh

    # Rigid fastener envelopes checked against every printed part and HTC shell.
    for name, part in design.hardware().items():
        solid=part.val()
        assert solid.isValid() and len(solid.Solids())==1, name
        for printed in parts.values(): assert solid.intersect(printed).Volume()<1e-5, name
        assert solid.intersect(tracker).Volume()<1e-4, name
    bearing=H['mount_screw_engagement']-H['mount_screw_length']
    assert bearing-design.F['main_head_height']>=H['body_bottom_z']
    nut_top=H['cover_nut_seat_z']+design.F['cover_nut_height']
    bolt_tip=H['body_bottom_z']-H['cover_thickness']+design.F['cover_length']
    assert bolt_tip>=nut_top and bolt_tip<H['body_top_z']
    assert T['contact_face_z']+I['flange_axial_clearance']<=T['maximum_contact_face_z']+1e-8
    # The fixed flange cannot pass through either retaining aperture.
    assert C['flange_diameter']>I['barrel_bore']
    assert C['flange_diameter']>I['contact_window_diameter']
    report['fasteners']={'mount_screw_exposed_thread_mm':H['mount_screw_engagement'],
                         'mount_head_recess_mm':bearing-design.F['main_head_height']-H['body_bottom_z'],
                         'cover_screws':'M2.5 x 12; no washers',
                         'nut_engagement_mm':design.F['cover_nut_height'],
                         'screw_projection_past_nut_mm':bolt_tip-nut_top,
                         'rigid_envelopes_clear':True}
    assembly=cq.importers.importStep(str(OUT/f'{design.ASSEMBLY_NAME}.step')).val()
    assert len(assembly.Solids())==11 and assembly.isValid()
    report['assembly_step_solid_count']=len(assembly.Solids())
    report['passed']=True
    (OUT/'verification.json').write_text(json.dumps(report,indent=2)+'\n')
    print(json.dumps(report,indent=2))


if __name__=='__main__': main()
