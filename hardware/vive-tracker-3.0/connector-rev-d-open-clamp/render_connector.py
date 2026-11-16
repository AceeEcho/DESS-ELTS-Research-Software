"""Actual exported STEP previews; never a substitute for a physical fit test."""
import cadquery as cq
import vtk
from PIL import Image, ImageDraw, ImageFont
import build_connector as d

PREVIEW=d.ROOT/'previews'


def render(name,offsets,position,center,scale):
    renderer=vtk.vtkRenderer(); renderer.SetBackground(.95,.96,.975)
    for part,offset in offsets.items():
        shape=cq.importers.importStep(str(d.OUT/f'{part}.step'))
        tmp=PREVIEW/f'{part}.stl'; cq.exporters.export(shape,str(tmp),tolerance=.025)
        reader=vtk.vtkSTLReader(); reader.SetFileName(str(tmp)); reader.Update()
        mapper=vtk.vtkPolyDataMapper(); mapper.SetInputConnection(reader.GetOutputPort())
        actor=vtk.vtkActor(); actor.SetMapper(mapper); actor.SetPosition(*offset)
        color=(.15,.25,.3)
        if part=='clamp_jaw': color=(.04,.6,.69)
        elif part.startswith('setup_gauge'): color=(.94,.48,.12)
        elif part.startswith('contact'): color=(.88,.64,.18)
        elif part.endswith('_reference'): color=(.62,.66,.7)
        actor.GetProperty().SetColor(*color); actor.GetProperty().SetSpecular(.2)
        renderer.AddActor(actor); tmp.unlink()
    window=vtk.vtkRenderWindow(); window.SetOffScreenRendering(1); window.SetSize(1100,900); window.AddRenderer(renderer)
    camera=renderer.GetActiveCamera(); camera.ParallelProjectionOn()
    camera.SetPosition(*position); camera.SetFocalPoint(*center); camera.SetViewUp(0,0,1); camera.SetParallelScale(scale)
    renderer.ResetCameraClippingRange(); window.Render()
    capture=vtk.vtkWindowToImageFilter(); capture.SetInput(window); capture.Update()
    writer=vtk.vtkPNGWriter(); writer.SetFileName(str(PREVIEW/f'{name}.png')); writer.SetInputConnection(capture.GetOutputPort()); writer.Write()
    window.Finalize()


def main():
    PREVIEW.mkdir(exist_ok=True)
    names=['mount_frame','clamp_jaw',*d.hardware(),'contact_2_reference','contact_4_reference']
    assembled={n:(0,0,0) for n in names}
    render('assembled',assembled,(55,-75,65),(0,-3,-3),27)
    exploded=dict(assembled); exploded['clamp_jaw']=(0,-13,0)
    for n in (0,1): exploded[f'clamp_screw_{n}_reference']=(0,-21,0)
    for n in (2,4): exploded[f'contact_{n}_reference']=(0,0,9)
    render('exploded',exploded,(55,-85,70),(0,-10,-2),34)
    detail={n:(0,0,0) for n in names if n!='mount_screw_reference'}
    render('contact_detail',detail,(12,-50,24),(-1.75,d.T['contact_y'],0),9)
    with_gauge=dict(assembled); with_gauge[d.gauge_name(0.0)]=(0,0,0)
    render('height_gauge',with_gauge,(22,-55,35),(-1.75,-15,0),15)
    canvas=Image.new('RGB',(2200,1150),'#f2f5f9')
    canvas.paste(Image.open(PREVIEW/'assembled.png'),(0,140))
    canvas.paste(Image.open(PREVIEW/'exploded.png'),(1100,140))
    draw=ImageDraw.Draw(canvas)
    title=ImageFont.truetype('C:/Windows/Fonts/segoeuib.ttf',48)
    text=ImageFont.truetype('C:/Windows/Fonts/segoeui.ttf',30)
    draw.text((45,25),'VIVE Tracker / Rev D open clamp',font=title,fill='#193640')
    draw.text((45,90),'Two printed mount parts | Exposed contacts | Removable height gauge | Accessible screws',font=text,fill='#42616b')
    draw.text((45,1050),'ASSEMBLED - gold faces 2 mm above the clamp',font=text,fill='#193640')
    draw.text((1145,1050),'EXPLODED - reused Treedix pins and M3 hardware',font=text,fill='#193640')
    draw.text((45,1100),'Rendered CAD. Physical grip, electrical contact and mounting strength remain untested.',font=text,fill='#42616b')
    canvas.save(PREVIEW/'design_overview.png')
    print(PREVIEW/'design_overview.png')


if __name__=='__main__':
    main()
