"""Render the actual exported CAD for assembly instructions (requires VTK/Pillow)."""
from pathlib import Path
import cadquery as cq
import vtk
from PIL import Image, ImageDraw, ImageFont
import build_connector as design

ROOT = Path(__file__).resolve().parent
OUT = ROOT / 'output'
PREVIEW = ROOT / 'previews'
PREVIEW.mkdir(exist_ok=True)
COLORS = {'body':(.22,.28,.32), 'wire_cover':(.38,.45,.49),
          'insert_front':(.02,.58,.78), 'insert_rear':(.05,.72,.86),
          'contact_2_ground':(.88,.64,.20), 'contact_4_trigger':(.88,.64,.20)}
COLORS.update({name:(.65,.68,.72) for name in design.hardware()})


def render(name, offsets, position, center, scale):
    renderer = vtk.vtkRenderer()
    renderer.SetBackground(.96,.97,.98)
    for part, offset in offsets.items():
        # Tessellate the STEP in assembly coordinates, not the bed-aligned STL.
        shape = cq.importers.importStep(str(OUT/f'{part}.step'))
        temporary = PREVIEW/f'{part}.stl'
        cq.exporters.export(shape, str(temporary), tolerance=.025)
        reader = vtk.vtkSTLReader(); reader.SetFileName(str(temporary)); reader.Update()
        mapper = vtk.vtkPolyDataMapper(); mapper.SetInputConnection(reader.GetOutputPort())
        actor = vtk.vtkActor(); actor.SetMapper(mapper); actor.SetPosition(*offset)
        actor.GetProperty().SetColor(*COLORS[part]); actor.GetProperty().SetSpecular(.23)
        actor.GetProperty().SetSpecularPower(25)
        renderer.AddActor(actor)
        temporary.unlink()
    window = vtk.vtkRenderWindow(); window.SetOffScreenRendering(1)
    window.SetSize(900,780); window.AddRenderer(renderer)
    camera = renderer.GetActiveCamera(); camera.ParallelProjectionOn()
    camera.SetPosition(*position); camera.SetFocalPoint(*center)
    camera.SetViewUp(0,1,0); camera.SetParallelScale(scale)
    renderer.ResetCameraClippingRange(); window.Render()
    capture=vtk.vtkWindowToImageFilter(); capture.SetInput(window); capture.Update()
    writer=vtk.vtkPNGWriter(); writer.SetFileName(str(PREVIEW/f'{name}.png'))
    writer.SetInputConnection(capture.GetOutputPort()); writer.Write()
    window.Finalize()


def main():
    assembled={name:(0,0,0) for name in COLORS}
    render('assembled', assembled, (60,-85,100), (0,-3,-3), 32)
    exploded={'body':(0,0,0), 'wire_cover':(0,-5,-24),
              'insert_front':(0,-3,12), 'insert_rear':(0,3,12),
              'contact_2_ground':(0,0,19), 'contact_4_trigger':(0,0,19)}
    exploded.update({'mount_screw_reference':(0,0,-15),
        'cover_screw_left_reference':(0,-5,-31), 'cover_screw_right_reference':(0,-5,-31),
        'cover_nut_left_reference':(0,0,8), 'cover_nut_right_reference':(0,0,8)})
    render('exploded', exploded, (75,-95,75), (0,-7,-13), 50)
    interior={name:(0,0,0) for name in COLORS if name not in (
        'wire_cover','cover_screw_left_reference','cover_screw_right_reference')}
    render('wire_cavity', interior, (25,-65,-100), (0,-6,-4), 31)
    detail={name:(0,0,0) for name in ('insert_rear','contact_2_ground','contact_4_trigger')}
    render('contact_detail', detail, (8,-40,18), (-1.75,design.T['contact_y'],0), 11)
    canvas=Image.new('RGB',(1800,1100),'#f5f7fa')
    for i,name in enumerate(['assembled','exploded']):
        canvas.paste(Image.open(PREVIEW/f'{name}.png'),(i*900,160))
    draw=ImageDraw.Draw(canvas)
    fonts=Path('C:/Windows/Fonts')
    title=ImageFont.truetype(str(fonts/'segoeuib.ttf'),46)
    heading=ImageFont.truetype(str(fonts/'segoeuib.ttf'),27)
    text=ImageFont.truetype(str(fonts/'segoeui.ttf'),25)
    draw.text((55,30),'VIVE Tracker 3.0 / trigger connector',font=title,fill='#152936')
    draw.text((57,96),'ELTS  â€¢  Rev C / exposed gold bases  â€¢  Treedix 6 mm pins + your M3 hardware',font=text,fill='#4d616e')
    draw.text((55,170),'ASSEMBLED â€” tracker-facing side',font=heading,fill='#152936')
    draw.text((955,170),'EXPLODED â€” parts separated for clarity',font=heading,fill='#152936')
    draw.text((60,945),'5/8-inch Allen screw + locating post',font=heading,fill='#152936')
    draw.text((60,987),'3.175 mm reserved for additional mount piece',font=text,fill='#4d616e')
    draw.text((955,945),'Fixed gold bases face the tracker',font=heading,fill='#152936')
    draw.text((955,987),'Unused spring tips point into the enclosed cavity.',font=text,fill='#4d616e')
    draw.text((60,1050),'Rendered from exported STEP. Wires omitted. Nominal CAD prototype; physical fit and contact untested.',font=text,fill='#4d616e')
    canvas.save(PREVIEW/'connector_overview.png')
    print(PREVIEW/'connector_overview.png')


if __name__=='__main__': main()
