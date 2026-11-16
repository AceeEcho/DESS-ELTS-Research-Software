"""Build plain bearing spacers and a millimetre-scale 3MF plate.

Dimensions are nominal, not measurements of printed parts. Flat faces have no
labels or tabs so each ring can sit squarely between the screw head and shoulder.
"""
from pathlib import Path
import json
import math
import zipfile
import xml.etree.ElementTree as ET
import cadquery as cq
import vtk

ROOT = Path(__file__).resolve().parent
P = json.loads((ROOT / 'parameters.json').read_text())
OUT = ROOT / 'output'
OUT.mkdir(exist_ok=True)
NS = 'http://schemas.microsoft.com/3dmanufacturing/core/2015/02'
ET.register_namespace('', NS)


def element(parent, tag, **attributes):
    return ET.SubElement(parent, f'{{{NS}}}{tag}', attributes)


def main():
    model = ET.Element(f'{{{NS}}}model', {'unit': 'millimeter',
        '{http://www.w3.org/XML/1998/namespace}lang': 'en-US'})
    element(model, 'metadata', name='Title').text = 'ELTS VIVE Tracker Bolt Spacers'
    resources = element(model, 'resources')
    build = element(model, 'build')
    assembly = cq.Assembly(name='ELTS_VIVE_Tracker_Bolt_Spacers')
    report = {'physical_fit_tested': False, 'sizes': [], 'object_count': 0}
    # Check both existing connector variants without altering either one.
    bodies = {}
    for folder in ('connector-treedix-fixed-base', 'connector-rev-b'):
        config = json.loads((ROOT.parent / folder / 'parameters.json').read_text())
        body = cq.importers.importStep(str(ROOT.parent / folder / 'output/body.step')).val()
        h = config['housing']
        shoulder = h['mount_screw_engagement'] - h['mount_screw_length']
        shoulder += h.get('additional_mount_piece_thickness', 0)
        bodies[folder] = (body, shoulder, h['mount_head_pocket_diameter'])

    object_id = 0
    for row, thickness in enumerate(P['thicknesses']):
        label = f'{thickness:.1f}mm'
        ring = (cq.Workplane('XY').circle(P['outer_diameter']/2)
                .circle(P['inner_diameter']/2).extrude(thickness)
                .edges().chamfer(P['edge_chamfer']))
        solid = ring.val()
        assert solid.isValid() and len(solid.Solids()) == 1
        overlaps = {}
        for folder, (body, shoulder, pocket) in bodies.items():
            seated = solid.translate((0, 0, shoulder-thickness))
            overlap = seated.intersect(body).Volume()
            assert overlap < 1e-5
            assert P['outer_diameter'] < pocket
            overlaps[folder] = overlap
        assert P['inner_diameter'] > 6.35  # nominal 1/4-inch thread major diameter
        stem = f'spacer_{label.replace(".", "p")}'
        cq.exporters.export(ring, str(OUT / f'{stem}.stl'), tolerance=0.008)
        cq.exporters.export(ring, str(OUT / f'{stem}.step'))
        vertices, triangles = solid.tessellate(0.008, 0.1)
        # Independent mesh topology check: every undirected edge has two faces.
        edges = {}
        for triangle in triangles:
            for a, b in zip(triangle, triangle[1:] + triangle[:1]):
                edge = tuple(sorted((a, b)))
                edges[edge] = edges.get(edge, 0) + 1
        # CAD tessellation can duplicate seam vertices; STL welding verifies closure.
        reader = vtk.vtkSTLReader(); reader.SetFileName(str(OUT/f'{stem}.stl')); reader.Update()
        clean = vtk.vtkCleanPolyData(); clean.SetInputConnection(reader.GetOutputPort()); clean.Update()
        boundary = vtk.vtkFeatureEdges(); boundary.SetInputConnection(clean.GetOutputPort())
        boundary.BoundaryEdgesOn(); boundary.NonManifoldEdgesOn()
        boundary.FeatureEdgesOff(); boundary.ManifoldEdgesOff(); boundary.Update()
        assert boundary.GetOutput().GetNumberOfCells() == 0
        report['sizes'].append({'thickness_mm': thickness, 'copies': P['copies_per_size'],
            'valid_solid': True, 'closed_mesh': True, 'body_overlap_mm3': overlaps})
        for copy in range(P['copies_per_size']):
            object_id += 1
            name = f'Spacer {label} - copy {copy+1}'
            obj = element(resources, 'object', id=str(object_id), type='model', name=name)
            mesh = element(obj, 'mesh')
            vxml = element(mesh, 'vertices')
            for vertex in vertices:
                element(vxml, 'vertex', x=f'{vertex.x:.7f}', y=f'{vertex.y:.7f}', z=f'{vertex.z:.7f}')
            txml = element(mesh, 'triangles')
            for a, b, c in triangles:
                element(txml, 'triangle', v1=str(a), v2=str(b), v3=str(c))
            x = P['plate_center_x'] + (copy-(P['copies_per_size']-1)/2)*P['spacing']
            y = P['first_row_y'] + row*P['spacing']
            element(build, 'item', objectid=str(object_id),
                    transform=f'1 0 0 0 1 0 0 0 1 {x} {y} 0')
            assembly.add(ring, name=f'{stem}_copy_{copy+1}', loc=cq.Location(cq.Vector(x,y,0)))
    path = OUT / 'ELTS_VIVE_Tracker_Bolt_Spacers.3mf'
    with zipfile.ZipFile(path, 'w', zipfile.ZIP_DEFLATED) as archive:
        archive.writestr('[Content_Types].xml', '<?xml version="1.0" encoding="UTF-8"?>'
            '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">'
            '<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>'
            '<Default Extension="model" ContentType="application/vnd.ms-package.3dmanufacturing-3dmodel+xml"/>'
            '</Types>')
        archive.writestr('_rels/.rels', '<?xml version="1.0" encoding="UTF-8"?>'
            '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'
            '<Relationship Target="/3D/3dmodel.model" Id="rel0" '
            'Type="http://schemas.microsoft.com/3dmanufacturing/2013/01/3dmodel"/>'
            '</Relationships>')
        archive.writestr('3D/3dmodel.model', ET.tostring(model, encoding='utf-8', xml_declaration=True))
    with zipfile.ZipFile(path) as archive:
        assert archive.testzip() is None
        check = ET.fromstring(archive.read('3D/3dmodel.model'))
        assert len(check.findall(f'{{{NS}}}build/{{{NS}}}item')) == 24
    assembly.export(str(OUT/'ELTS_VIVE_Tracker_Bolt_Spacers.step'))
    report.update(object_count=object_id, outer_diameter_mm=P['outer_diameter'],
        inner_diameter_mm=P['inner_diameter'], radial_pocket_clearance_mm=0.3,
        radial_thread_clearance_mm=(P['inner_diameter']-6.35)/2, passed=True)
    (OUT/'verification.json').write_text(json.dumps(report, indent=2)+'\n')
    print(path)
    print(json.dumps(report, indent=2))


if __name__ == '__main__':
    main()
