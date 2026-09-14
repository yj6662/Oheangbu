"""Measure actual Unity imported/skinned triangle surfaces against the actual handle."""
import bpy,json,math
from pathlib import Path
from mathutils import Vector
from mathutils.bvhtree import BVHTree
ROOT=Path('C:/Users/yj666/Oheangbu');QA=ROOT/'Oheangbu/Screenshots/PlayerDosaV2'
data=json.loads((QA/'imported-static-grip-surfaces.json').read_text())
def vec(p):return Vector((p['x'],p['y'],p['z']))
handle=next(s for s in data['surfaces'] if s['name']=='DosaBrushV2_Handle')
hand=next(s for s in data['surfaces'] if s['name']=='DosaV2_Hands')
verts=[vec(p) for p in handle['vertices']];indices=handle['triangles']
tree=BVHTree.FromPolygons(verts,[indices[i:i+3] for i in range(0,len(indices),3)],all_triangles=True)
def signed(point):
    hit=tree.find_nearest(point);distance=hit[3]
    radial=Vector((point.x,0,point.z));length=radial.length
    if length<1e-9:return -distance
    outer=tree.ray_cast(Vector((0,point.y,0)),radial/length,.15)
    if outer[0] is not None and length<math.hypot(outer[0].x,outer[0].z)-1e-7:return -distance
    return distance
positions=[vec(p) for p in hand['vertices']];bones=hand['dominantBones'];tris=hand['triangles']
fingers={}
for name in ['Thumb','Index','Middle','Ring','Pinky']:
    sample=[signed(p) for p,b in zip(positions,bones) if b.startswith('RightHand'+name)]
    fingers[name]={'minimum_gap_m':max(0,min(sample)),'maximum_vertex_penetration_m':max(0,-min(sample))}
penetration=0;count=0
for i in range(0,len(tris),3):
    ids=tris[i:i+3]
    if not all(bones[index].startswith('RightHand') for index in ids):continue
    a,b,c=[positions[index] for index in ids]
    # Only the actual finite gripping neighbourhood is eligible for shaft contact.
    if min((a+b+c).length/3, min(a.length,b.length,c.length))>.12:continue
    n=min(48,max(2,math.ceil(max((a-b).length,(b-c).length,(c-a).length)/.0005)))
    for u in range(n+1):
        for v in range(n-u+1):
            p=a+(b-a)*(u/n)+(c-a)*(v/n);d=signed(p);penetration=max(penetration,-d);count+=1
report={'status':'PASS_CONTACT_ONLY' if penetration<=.0005 and all(fingers[f]['minimum_gap_m']<=.0015 for f in ['Thumb','Index','Middle']) else 'FAIL_CONTACT',
    'source':'Actual Unity BakeMesh triangles and actual Meshy handle triangles in GripSocket frame',
    'fingers':fingers,'maximum_surface_penetration_m':penetration,'surface_samples':count,
    'sample_spacing_m':.0005,'note':'Does not certify visual hand deformation, garment quality or the complete RIG_PASS gate.'}
(QA/'imported-static-grip-contact.json').write_text(json.dumps(report,indent=2));print(json.dumps(report))
