"""Actual inner-body mesh vs cloth-pin audit, independent of capsule fitting."""
import bpy,bmesh,hashlib,json,math,argparse,sys,importlib.util
import numpy as np
from pathlib import Path
from mathutils import Matrix,Vector
from mathutils.bvhtree import BVHTree
ROOT=Path(__file__).resolve().parents[3];OUT=ROOT/'Art/PlayerV2/Inspect/ClothBlender/BodyClearance'
SOURCE=ROOT/'Art/PlayerV2/Inspect/ClothBlender/Inputs/Assembled-final-46655083.blend'
PROXIES=ROOT/'Art/PlayerV2/Inspect/ClothBlender/Inputs/anatomy-capsules-final-46655083.json'
parser=argparse.ArgumentParser();parser.add_argument('--case',choices=['rest_settle','raised_arms_settle','grip_settle'],default='rest_settle')
parser.add_argument('--source',default=str(SOURCE));parser.add_argument('--proxies',default=str(PROXIES));parser.add_argument('--out',default=str(OUT))
parser.add_argument('--static-pose',default=None)
parser.add_argument('--include-unorm8-zero',action='store_true',help='Historical color transport only; final UV3 transport uses exact source zeros.')
parser.add_argument('--all-pins',action='store_true',help='Query actual inner geometry for every selected pin, independent of proxy broadphase.')
args=parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
SOURCE=Path(args.source).resolve();PROXIES=Path(args.proxies).resolve();OUT=Path(args.out).resolve()
if args.case!='rest_settle':OUT=OUT/args.case
if args.static_pose:OUT=OUT/args.static_pose
def digest(p):return hashlib.sha256(Path(p).read_bytes()).hexdigest()
OUT.mkdir(parents=True,exist_ok=True);bpy.ops.wm.open_mainfile(filepath=str(SOURCE));scene=bpy.context.scene
for o in scene.objects:
 if o.name.startswith('CTRL_') and 'AuthoringMode' in o:o['AuthoringMode']=False;o.update_tag()
for p in bpy.data.objects['DosaV2_Rig'].pose.bones:p.matrix_basis=Matrix.Identity(4)
bpy.context.view_layer.update()
rig=bpy.data.objects['DosaV2_Rig']
if args.case!='rest_settle':
 spec=importlib.util.spec_from_file_location('cloth_fixture',Path(__file__).with_name('diagnose_cloth_blender.py'))
 fixture=importlib.util.module_from_spec(spec);spec.loader.exec_module(fixture);fixture.direct_pose(rig,args.case)
if args.static_pose:
 definition=next(p for p in json.loads((ROOT/'Art/PlayerV2/Validation/static-pose-definitions.json').read_text())['poses'] if p['id']==args.static_pose)
 for row in definition['boneRotations']:
  p=rig.pose.bones[row['bone']];p.rotation_mode='XYZ';p.rotation_euler=[math.radians(row[c]) for c in ['x','y','z']]
 bpy.context.view_layer.update()
def mesh_data(obj):
 e=obj.evaluated_get(bpy.context.evaluated_depsgraph_get());m=e.to_mesh();m.calc_loop_triangles()
 pts=[e.matrix_world@v.co for v in m.vertices];tris=[tuple(t.vertices) for t in m.loop_triangles]
 edges={}
 for tri in tris:
  for i in range(3):
   key=tuple(sorted((tri[i],tri[(i+1)%3])));edges[key]=edges.get(key,0)+1
 result={'object':obj,'points':pts,'triangles':tris,'bvh':BVHTree.FromPolygons(pts,tris,all_triangles=True),
         'boundaryEdges':sum(v==1 for v in edges.values()),'nonManifoldEdges':sum(v>2 for v in edges.values())}
 e.to_mesh_clear();result['closed']=result['boundaryEdges']==0 and result['nonManifoldEdges']==0;return result
inner=[mesh_data(o) for o in scene.objects if o.type=='MESH' and o.name.startswith(('DosaV2_BodyLining','DosaV2_ArmLining_','DosaV2_LegLining_'))]
core=mesh_data(bpy.data.objects['DosaV2_BodyCore'])
directions=[Vector(v).normalized() for v in [(1,.371,.219),(.173,1,.293),(.271,.123,1)]]
def query(mesh,p):
 nearest=mesh['bvh'].find_nearest(p)
 if nearest[0] is None:return {'inside':False,'distance':None}
 loc,normal,index,distance=nearest;votes=[]
 for direction in directions:
  origin=p+direction*.000001;count=0
  for _ in range(64):
   hit=mesh['bvh'].ray_cast(origin,direction,10)
   if hit[0] is None:break
   count+=1;origin=hit[0]+direction*.000005
  votes.append(count%2)
 return {'inside':sum(votes)>=2 if mesh['closed'] else None,'parityVotes':votes,'closed':mesh['closed'],
         'distanceMeters':distance,'nearestPoint':list(loc),'signedNearestNormalHint':(p-loc).dot(normal),
         'triangle':index}
caps=json.loads(PROXIES.read_text(encoding='utf-8-sig'))['capsules'];surfaces=[o for o in scene.objects if o.type=='MESH' and o.name.startswith(('DosaV2_Robe_','DosaV2_SleeveOuter_'))]
for cap in caps:
 matrix=rig.matrix_world@rig.pose.bones[cap['anchorBone']].matrix@rig.data.bones[cap['anchorBone']].matrix_local.inverted()
 cap['startBlender']=list(matrix@Vector(cap['startBlender']));cap['endBlender']=list(matrix@Vector(cap['endBlender']))
rows=[];summaries=[]
for o in surfaces:
 data=mesh_data(o);colors=o.data.color_attributes['ClothMobility'];exact=quant=0;affected=0
 for index,(point,color) in enumerate(zip(data['points'],colors.data)):
  value=color.color[0];is_exact=value==0;is_quant=round(value*255)==0
  exact+=is_exact;quant+=is_quant
  if not (is_quant if args.include_unorm8_zero else is_exact):continue
  penetrated=[]
  for cap in caps:
   a,b=Vector(cap['startBlender']),Vector(cap['endBlender']);axis=b-a;t=max(0,min(1,(point-a).dot(axis)/axis.length_squared)) if axis.length_squared>1e-18 else 0
   depth=cap['radiusMeters']-(point-(a+axis*t)).length
   if depth>.0005:penetrated.append({'capsule':cap['name'],'depthMeters':depth})
  if not penetrated and not args.all_pins:continue
  actual=[{'mesh':mesh['object'].name,**query(mesh,point)} for mesh in inner]
  if not penetrated and not any(r['inside'] is True for r in actual):continue
  rows.append({'surface':o.name,'vertex':index,'pointBlender':list(point),'sourceRestPointBlender':list(o.matrix_world@o.data.vertices[index].co),
               'deformWeights':{o.vertex_groups[g.group].name:g.weight for g in o.data.vertices[index].groups if g.weight>1e-8},
               'mobilityMetres':value,'exactSourcePin':is_exact,
               'capsules':penetrated,'actualInnerMeshes':actual,'actualClosedInnerUnionInside':any(r['inside'] is True for r in actual),
               'bodyCoreNearest':query(core,point)})
  affected+=bool(penetrated)
 summaries.append({'surface':o.name,'exactPins':exact,'unityUnorm8Pins':quant,'pinsInsideCapsules':affected})
rows.sort(key=lambda r:max((p['depthMeters'] for p in r['capsules']),default=0),reverse=True)
sections=[]
for label,z,point in [('SleevePin',1.20238,(.162958,.0102546)),('WaistPin',.920106,(-.15312,.0249285))]:
 layers=[]
 for mesh in inner+[core]+[mesh_data(o) for o in surfaces]:
  lines=[]
  for tri in mesh['triangles']:
   pts=[mesh['points'][i] for i in tri];crossings=[]
   for i in range(3):
    a,b=pts[i],pts[(i+1)%3]
    if (a.z-z)*(b.z-z)<0:
     t=(z-a.z)/(b.z-a.z);p=a+(b-a)*t;crossings.append([p.x,p.y])
   if len(crossings)==2:lines.append(crossings)
  layers.append({'mesh':mesh['object'].name,'segments':lines})
 sections.append({'label':label,'z':z,'pinXY':point,'layers':layers})
report={'status':'MEASURED_ACTUAL_INNER_BODY_PIN_CLEARANCE','case':args.static_pose or args.case,'source_sha256':digest(SOURCE),'proxy_sha256':digest(PROXIES),
 'pinSelection':'historical UNorm8 zeros' if args.include_unorm8_zero else 'exact source zeros, preserved by final float UV3 transport',
 'actualGeometryQueriesAllSelectedPins':args.all_pins,
 'innerMeshes':[{'name':m['object'].name,'vertices':len(m['points']),'triangles':len(m['triangles']),'closed':m['closed'],'boundaryEdges':m['boundaryEdges'],'nonManifoldEdges':m['nonManifoldEdges']} for m in inner],
 'bodyCoreClosed':core['closed'],'summaries':summaries,'pinsInsideCapsules':sum(bool(r['capsules']) for r in rows),'pinsInsideActualClosedInnerMeshes':sum(r['actualClosedInnerUnionInside'] for r in rows),
 'records':rows,'sections':sections,
 'method':'Exact evaluated Blender geometry at declared static pose. Closed volume inside by majority of three non-axis-aligned parity rays; signed nearest normal is a hint only for open scan BodyCore. Cloth pins include both exact source zero and actual Unity UNorm8 quantization zero, labelled separately.'}
(OUT/'actual-inner-mesh-geometry.json').write_text(json.dumps({'source_sha256':digest(SOURCE),'case':args.case,'space':'Blender world meters; exact evaluated triangle mesh at declared static pose, no Cloth modifier.',
 'meshes':[{'name':m['object'].name,'closed':m['closed'],'vertices':[list(v) for v in m['points']],'triangles':m['triangles']} for m in inner]},indent=2),encoding='utf-8')
(OUT/'actual-body-pin-clearance.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print(json.dumps({k:v for k,v in report.items() if k not in ['records','sections']},indent=2))
