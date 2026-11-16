"""Open, two-piece tracker attachment. All dimensions are millimetres.

Mount-ring face is Z=0; +Z points into the tracker. The large fixed gold pin
bases face +Z. No adhesive, flange lips or enclosed solder cavity are used.
The model shows an untightened clamp; real grip requires a physical fit check.
"""
from pathlib import Path
import json
import math
import cadquery as cq

ROOT = Path(__file__).resolve().parent
P = json.loads((ROOT / 'parameters.json').read_text(encoding='utf-8'))
T, C, F, J, H, G = (P[k] for k in ('tracker', 'pin', 'frame', 'jaw', 'hardware', 'gauge'))
OUT = ROOT / 'output'
NAME = 'ELTS_VIVE_Tracker_3_Rev_' + P['revision']
BOTTOM = H['mount_engagement'] - H['mount_screw_length']


def box(w, d, low, high, x=0, y=0):
    return cq.Workplane('XY', origin=(x,y,low)).box(w,d,high-low,centered=(True,True,False))


def cyl(d, low, high, x=0, y=0):
    return cq.Workplane('XY', origin=(x,y,low)).circle(d/2).extrude(high-low)


def along_y(d, start, end, x, z):
    # The XZ workplane normal is -Y; start at the rear end.
    return cq.Workplane('XZ', origin=(x,end,z)).circle(d/2).extrude(end-start)


def hex_y(af, start, end, x, z):
    return cq.Workplane('XZ', origin=(x,end,z)).polygon(6,af/math.cos(math.pi/6)).extrude(end-start)


def rounded(w,d,low,high,x=0,y=0):
    return box(w,d,low,high,x,y).edges('|Z').fillet(F['corner_radius'])


def grip_features(part, rear):
    """A continuous raised bridge grips barrels; lower bores expose solder.

    The 0.3 mm split permits closing the 1.6 mm nominal bore around a 1.5 mm
    barrel. No calculated friction or retention force is claimed.
    """
    cy = T['contact_y']
    # A continuous band ties the grips into the body. Individual posts over
    # wire slots would otherwise be attached by a fragile thin roof.
    bridge = box(J['grip_bridge_width'], J['grip_bridge_depth'],
        F['wire_relief_top_z']-0.1, J['tower_top_z'],J['grip_bridge_center_x'],cy)
    part = part.union(bridge)
    for x in T['contact_x']:
        part = part.cut(cyl(J['grip_bore'], BOTTOM-1,J['tower_top_z']+1,x,cy))
        part = part.cut(cyl(F['wire_relief_diameter'],BOTTOM-1,F['wire_relief_top_z'],x,cy))
    # Keep the two halves independent, with room for actual clamp travel.
    if rear:
        clip = box(80,60,BOTTOM-1,10,0,cy+J['split_gap']/2+30)
    else:
        clip = box(80,60,BOTTOM-1,10,0,cy-J['split_gap']/2-30)
    return part.intersect(clip)


def frame():
    cy = T['contact_y']
    start = cy+J['split_gap']/2
    part = rounded(F['width'],F['rear_y']-start,BOTTOM,F['top_z'],y=(F['rear_y']+start)/2)
    part = grip_features(part, True)
    part = part.union(rounded(F['spine_width'],F['spine_rear_y']-F['spine_front_y'],
        BOTTOM,F['top_z'],y=(F['spine_rear_y']+F['spine_front_y'])/2))
    # One continuous bearing stack: 15.875 - 11.375 = 4.5 mm projection.
    # The bolt head is outside and accessible; no hidden spacer stack.
    part = part.union(cyl(T['mount_seat_diameter'],F['top_z'],0))
    part = part.cut(cyl(F['mount_bore'],BOTTOM-1,1))
    locator = cyl(T['locator_diameter'],F['top_z'],T['locator_height'],y=T['locator_y'])
    part = part.union(locator.edges('>Z').chamfer(0.35))
    for x in J['screw_x']:
        part = part.cut(along_y(J['screw_clearance'],cy-1,F['rear_y']+1,x,J['screw_z']))
        part = part.cut(hex_y(J['nut_pocket_af'],J['nut_pocket_front_y'],F['rear_y']+1,x,J['screw_z']))
    # Open-bottom slots allow insulated leads to leave the grip without a cover.
    for x in T['contact_x']:
        part = part.cut(box(F['wire_relief_diameter'],5,BOTTOM-1,F['wire_relief_top_z'],x,cy+1.5))
    for x in F['tie_slot_x']:
        part = part.cut(box(F['tie_slot_width'],F['tie_slot_length'],BOTTOM-1,F['top_z']+0.1,x,F['tie_slot_y']))
    return part.clean()


def jaw():
    end = T['contact_y']-J['split_gap']/2
    part = rounded(F['width'],end-J['front_y'],J['bottom_z'],F['top_z'],y=(end+J['front_y'])/2)
    part = grip_features(part,False)
    for x in J['screw_x']:
        part = part.cut(along_y(J['screw_clearance'],J['front_y']-1,end+1,x,J['screw_z']))
    for x in T['contact_x']:
        # Leave a continuous front wall linking both screw ears. Wires exit
        # below the pins rather than cutting the jaw into thin hanging fingers.
        part = part.cut(box(F['wire_relief_diameter'],J['wire_exit_depth'],
            J['bottom_z']-1,F['wire_relief_top_z'],x,T['contact_y']-J['wire_exit_depth']/2))
    return part.clean()


def pin(x,lowering=0):
    face = T['contact_face_z']-lowering
    seat = face-C['flange_thickness']
    barrel = seat-C['barrel_length']
    return (cyl(C['flange_diameter'],seat,face,x,T['contact_y'])
        .union(cyl(C['barrel_diameter'],barrel,seat,x,T['contact_y']))
        .union(cyl(C['plunger_diameter'],face-C['overall_length'],barrel,x,T['contact_y'])).clean())


def gauge(lowering=0):
    """Temporary fork under the gold flanges. Pull toward -Y after tightening.

    Gauges lower the nominal contact setting; none extends it into the shell.
    They are removed before attaching the tracker.
    """
    bottom = J['tower_top_z']
    top = T['contact_face_z']-lowering-C['flange_thickness']
    cy = T['contact_y']
    part = box(G['width'],G['depth'],bottom,top,G['center_x'],cy+G['center_y_offset'])
    for x in T['contact_x']:
        part = part.cut(cyl(G['slot_diameter'],bottom-1,top+1,x,cy))
        part = part.cut(box(G['slot_diameter'],5,bottom-1,top+1,x,cy+2.5))
    return part.clean()


def hardware():
    head_bottom = BOTTOM-H['mount_head_height']
    main = cyl(H['mount_diameter'],BOTTOM,H['mount_engagement'])
    main = main.union(cyl(H['mount_head_diameter'],head_bottom,BOTTOM))
    socket = cq.Workplane('XY',origin=(0,0,head_bottom-0.1)).polygon(6,H['mount_hex_af']/math.cos(math.pi/6)).extrude(1.7)
    result = {'mount_screw_reference':main.cut(socket).clean()}
    for n,x in enumerate(J['screw_x']):
        front=J['front_y']; head=front-H['clamp_head_height']
        screw=along_y(H['clamp_diameter'],front,front+H['clamp_screw_length'],x,J['screw_z'])
        screw=screw.union(along_y(H['clamp_head_diameter'],head,front,x,J['screw_z']))
        screw=screw.cut(hex_y(H['clamp_hex_af'],head-0.1,head+1.6,x,J['screw_z']))
        result[f'clamp_screw_{n}_reference']=screw.clean()
        start=J['nut_pocket_front_y']
        nut=hex_y(H['nut_af'],start,start+H['nut_thickness'],x,J['screw_z'])
        result[f'clamp_nut_{n}_reference']=nut.cut(along_y(H['clamp_diameter'],start-1,start+H['nut_thickness']+1,x,J['screw_z'])).clean()
    return result


def parts():
    return {'mount_frame':frame(),'clamp_jaw':jaw()}


def gauge_name(lowering):
    return 'setup_gauge_lower_'+str(lowering).replace('.','p')+'mm'


def main():
    OUT.mkdir(exist_ok=True)
    assembly=cq.Assembly(name=NAME)
    printable=parts()
    printable.update({gauge_name(v):gauge(v) for v in G['lowering_options']})
    manifest={}
    for name,part in printable.items():
        shape=part.val()
        if not shape.isValid() or len(shape.Solids())!=1:
            raise ValueError(f'Invalid or disconnected part: {name}')
        cq.exporters.export(part,str(OUT/f'{name}.step'))
        cq.exporters.export(part.translate((0,0,-shape.BoundingBox().zmin)),str(OUT/f'{name}.stl'),
            tolerance=P['export']['mesh_tolerance'],angularTolerance=P['export']['angular_tolerance'])
        manifest[name]={'volume_mm3':shape.Volume(),'print_supports':'Inspect slicer; horizontal clamp holes may need local support'}
        if name in ('mount_frame','clamp_jaw'):
            assembly.add(part,name=name,color=cq.Color(.15,.25,.3) if name=='mount_frame' else cq.Color(.05,.6,.7))
    references=hardware()
    references.update({f'contact_{n}_reference':pin(x) for n,x in zip((2,4),T['contact_x'])})
    for name,part in references.items():
        cq.exporters.export(part,str(OUT/f'{name}.step'))
        assembly.add(part,name=name,color=cq.Color(.85,.63,.18) if name.startswith('contact') else cq.Color(.6,.64,.68))
    assembly.export(str(OUT/f'{NAME}.step'))
    (OUT/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
    print(json.dumps(manifest,indent=2))


if __name__=='__main__':
    main()
