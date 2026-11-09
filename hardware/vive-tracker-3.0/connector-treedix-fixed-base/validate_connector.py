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
F = design.F


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
    # Every adapter part and fastener ends below Z=5. Crop the irrelevant upper
    # shell once, retaining all tracker geometry in the adapter's swept bounds.
    tracker=tracker.intersect(design.box(120,120,-30,6).val())
    parts={name:cq.importers.importStep(str(OUT/f'{name}.step')).val() for name in NAMES}
    report={'status':'CAD checks only; physical fit and electrical operation untested',
            'revision':design.P['revision'],
            'tracker_step_sha256':hashlib.sha256(tracker_path.read_bytes()).hexdigest(),
            'parameters_sha256':hashlib.sha256((ROOT/'parameters.json').read_bytes()).hexdigest(),
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
        allowed=design.box(1.5,1.5,1,3,x,T['contact_y']).val()
        outside=hit.cut(allowed).Volume()
        assert hit.Volume()>0 and outside<1e-5, name
        report['contact_checks'][name]={'free_spring_overlap_mm3':hit.Volume(),
              'overlap_outside_intended_contact_mm3':outside,
              'seated_fixed_base_face_height_mm':T['contact_face_z'],
              'maximum_fixed_base_face_height_mm':T['maximum_contact_face_z'],
              'solder_barrel_accessible_length_mm':I['solder_relief_top_z']-(T['contact_face_z']-C['flange_thickness']-C['barrel_length']),
              'unused_plunger_lowest_z_mm':T['contact_face_z']-C['overall_length'],
              'contact_window_diameter_mm':I['contact_window_diameter']}
        # Check both ends of the allowed flange float, not just one pose.
        raised=pin.translate((0,0,I['flange_axial_clearance']))
        for part in parts.values(): assert raised.intersect(part).Volume()<1e-5
        assert raised.intersect(tracker).cut(allowed).Volume()<1e-5

    coupon=cq.importers.importStep(str(OUT/'mount_fit_coupon.step')).val()
    coupon_mesh=mesh_check(OUT/'mount_fit_coupon.stl')
    assert coupon.isValid() and len(coupon.Solids())==1
    assert coupon.intersect(tracker).Volume()<1e-4
    assert coupon_mesh['open_or_nonmanifold_edges']==0 and coupon_mesh['connected_regions']==1
    assert abs(coupon_mesh['minimum_z_mm'])<1e-5
    report['mount_fit_coupon']=coupon_mesh

    # Envelope check of the bought fasteners. Threads are intentionally simplified.
    bearing=H['mount_screw_engagement']-H['mount_screw_length']
    main_shaft=design.cylinder(6.35,bearing,H['mount_screw_engagement']).val()
    main_head=design.cylinder(F['mount_head_diameter'],bearing-F['mount_head_height'],bearing).val()
    for part in parts.values():
        assert main_shaft.intersect(part).Volume()<1e-5
        assert main_head.intersect(part).Volume()<1e-5
    assert main_shaft.intersect(tracker).Volume()<1e-4
    hardware={name:part.val() for name,part in design.hardware().items()}
    for name,item in hardware.items():
        assert item.isValid() and len(item.Solids())==1, name
        for part_name,part in parts.items():
            assert item.intersect(part).Volume()<1e-5, (name,part_name)
        assert item.intersect(tracker).Volume()<1e-4, name
    for index,(name,item) in enumerate(hardware.items()):
        for other_name,other in list(hardware.items())[index+1:]:
            assert item.intersect(other).Volume()<1e-5, (name,other_name)
    screw_tip=H['body_bottom_z']-H['cover_thickness']+F['cover_screw_length']
    nut_top=H['m3_nut_seat_z']+F['cover_nut_thickness']
    assert screw_tip>=nut_top and screw_tip<H['body_top_z']
    main_head_recess=bearing-F['mount_head_height']-H['body_bottom_z']
    assert main_head_recess>0
    # Check the reserved annular stack envelope, without claiming a mount design.
    spacer=design.cylinder(F['mount_head_diameter'],bearing,
        bearing+H['additional_mount_piece_thickness']).cut(
        design.cylinder(H['mount_bore'],bearing-0.1,
            bearing+H['additional_mount_piece_thickness']+0.1)).val()
    for part in parts.values(): assert spacer.intersect(part).Volume()<1e-5
    assert abs(H['mount_screw_length']-(H['additional_mount_piece_thickness']
        -(bearing+H['additional_mount_piece_thickness']))-H['mount_screw_engagement'])<1e-6
    report['fasteners']={'mount_screw_exposed_thread_mm':H['mount_screw_engagement'],
                         'mount_socket_head_recess_below_body_mm':main_head_recess,
                         'mount_screw_length_mm':H['mount_screw_length'],
                         'reserved_additional_mount_thickness_mm':H['additional_mount_piece_thickness'],
                         'body_bearing_z_mm':bearing+H['additional_mount_piece_thickness'],
                         'additional_mount_piece_included':False,
                         'cover_screws':F['cover_screw'],
                         'cover_nut_engagement_mm':F['cover_nut_thickness'],
                         'cover_screw_tip_z_mm':screw_tip,
                         'cover_screw_protrusion_past_nut_mm':screw_tip-nut_top,
                         'rigid_envelopes_clear':True}
    assembly=cq.importers.importStep(str(OUT/f'{design.ASSEMBLY_NAME}.step')).val()
    assert len(assembly.Solids())==11 and assembly.isValid()
    report['assembly_step_solid_count']=len(assembly.Solids())
    report['limits']=['Nominal seller and kit hardware dimensions; received parts unmeasured',
        'Small contact windows and lips need slicer inspection and an insert fit check',
        'Tracker spring force, print strength, electrical contact and physical alignment untested']
    report['passed']=True
    (OUT/'verification.json').write_text(json.dumps(report,indent=2)+'\n')
    print(json.dumps(report,indent=2))


if __name__=='__main__': main()
