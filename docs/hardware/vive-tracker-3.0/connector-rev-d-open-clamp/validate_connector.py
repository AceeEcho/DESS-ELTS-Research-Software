"""Digital geometry checks only; no claim about friction, printing or real fit."""
import hashlib
import itertools
import json
import cadquery as cq
import vtk
import build_connector as d


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
        'volume_mm3':mass.GetVolume(),'bed_z_mm':clean.GetOutput().GetBounds()[4]}


def main():
    report={'revision':d.P['revision'],'status':'CAD only; physical retention, contact and fit untested',
            'printed_parts':{},'contact_settings':{},'pair_overlaps_mm3':{}}
    tracker_path=d.ROOT.parent/'official-model/VIVE_Tracker_3.0_3D_STP/VIVE_Tracker_3.0_3D.stp'
    print('Loading official tracker geometry...',flush=True)
    tracker=cq.importers.importStep(str(tracker_path)).val().translate((0,0,-d.T['official_mount_face_z']))
    tracker=tracker.intersect(d.box(120,120,-30,8).val())
    parts={name:cq.importers.importStep(str(d.OUT/f'{name}.step')).val() for name in d.parts()}
    for name in [*parts,*(d.gauge_name(v) for v in d.G['lowering_options'])]:
        shape=cq.importers.importStep(str(d.OUT/f'{name}.step')).val()
        mesh=mesh_check(d.OUT/f'{name}.stl')
        assert shape.isValid() and len(shape.Solids())==1,name
        assert mesh['open_or_nonmanifold_edges']==0 and mesh['connected_regions']==1,name
        assert abs(mesh['volume_mm3']/shape.Volume()-1)<.01,name
        assert abs(mesh['bed_z_mm'])<1e-5,name
        result={'valid_solid':True,'mesh':mesh}
        if name in parts:
            hit=shape.intersect(tracker).Volume()
            assert hit<1e-4,(name,'tracker collision',hit)
            result['tracker_overlap_mm3']=hit
        else:
            result['removed_before_tracker_mounting']=True
        report['printed_parts'][name]=result
    hardware={name:cq.importers.importStep(str(d.OUT/f'{name}.step')).val() for name in d.hardware()}
    allparts={**parts,**hardware}
    for name,shape in hardware.items():
        assert shape.isValid() and len(shape.Solids())==1,name
        hit=shape.intersect(tracker).Volume()
        assert hit<1e-4,(name,'tracker collision',hit)
    for (a,shape),(b,other) in itertools.combinations(allparts.items(),2):
        hit=shape.intersect(other).Volume()
        assert hit<1e-5,(a,b,hit)
        report['pair_overlaps_mm3'][a+'/'+b]=hit
    for lower in d.G['lowering_options']:
        gauge=cq.importers.importStep(str(d.OUT/f'{d.gauge_name(lower)}.step')).val()
        for name,part in allparts.items():
            assert gauge.intersect(part).Volume()<1e-5,('gauge',lower,name)
        setting=[]
        for x in d.T['contact_x']:
            pin=d.pin(x,lower).val()
            for name,part in allparts.items():
                assert pin.intersect(part).Volume()<1e-5,('pin',lower,name)
            assert pin.intersect(gauge).Volume()<1e-5,('gauge and pin',lower)
            # Verify the gauge can pull forward clear of the pins and clamp.
            for distance in [0.5,1,2,4,8]:
                moved=gauge.translate((0,-distance,0))
                assert moved.intersect(pin).Volume()<1e-5,('gauge withdrawal',lower,distance)
                for part in parts.values():
                    assert moved.intersect(part).Volume()<1e-5,('gauge withdrawal and frame',lower,distance)
            allowed=d.box(1.5,1.5,1,3,x,d.T['contact_y']).val()
            hit=pin.intersect(tracker)
            assert hit.cut(allowed).Volume()<1e-5,('pin outside intended contact',lower)
            if lower==0:
                assert hit.Volume()>0,'nominal setting misses free spring'
            setting.append({'x_mm':x,'free_tracker_spring_overlap_mm3':hit.Volume(),
                            'exposed_height_mm':d.T['contact_face_z']-lower-d.J['tower_top_z']})
        report['contact_settings'][str(lower)]={'lowering_mm':lower,'pins':setting}
    # Allen-key access from the front, not through the tracker or a cover.
    for x in d.J['screw_x']:
        tool=d.along_y(d.H['clamp_hex_af'],d.J['front_y']-25,d.J['front_y']-d.H['clamp_head_height'],x,d.J['screw_z']).val()
        for part in parts.values():
            assert tool.intersect(part).Volume()<1e-5,'blocked clamp tool access'
    # Jaw can approach the fixed half without bottoming on printed faces.
    travel=d.J['split_gap']-.05
    assert parts['clamp_jaw'].translate((0,travel,0)).intersect(parts['mount_frame']).Volume()<1e-5
    assert d.H['clamp_screw_length']+d.J['front_y']>=d.J['nut_pocket_front_y']+d.H['nut_thickness']
    assert abs(d.H['mount_screw_length']+d.BOTTOM-d.H['mount_engagement'])<1e-8
    assembly=cq.importers.importStep(str(d.OUT/f'{d.NAME}.step')).val()
    assert assembly.isValid() and len(assembly.Solids())==9
    report['assembly_solids']=len(assembly.Solids())
    report['nominal_mount_projection_mm']=d.H['mount_engagement']
    report['available_jaw_closing_travel_mm']=travel
    report['limits']=['Nominal pin and fastener sizes, not measured tolerances',
        'Printed barrel grip and clamp force untested; no adhesive or positive axial pin stop',
        'Gauge and print dimensions require measurement; gauges removed before mounting',
        'No physical contact, tracking calibration or mounting strength validation']
    report['tracker_sha256']=hashlib.sha256(tracker_path.read_bytes()).hexdigest()
    report['files_sha256']={str(p.relative_to(d.ROOT)):hashlib.sha256(p.read_bytes()).hexdigest()
        for p in [d.ROOT/'parameters.json',d.ROOT/'build_connector.py',d.ROOT/'validate_connector.py',
                  *sorted(d.OUT.glob('*.step')),*sorted(d.OUT.glob('*.stl'))]}
    report['passed']=True
    (d.OUT/'verification.json').write_text(json.dumps(report,indent=2)+'\n')
    print(json.dumps({k:v for k,v in report.items() if k!='files_sha256'},indent=2))


if __name__=='__main__':
    main()
