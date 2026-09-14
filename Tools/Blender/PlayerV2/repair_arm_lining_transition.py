"""Compare local lining skin sampling against the actual outer-sleeve weight field."""
import bpy,bmesh,json,math,hashlib,importlib.util
from pathlib import Path
from mathutils import Matrix,Vector,Euler
from mathutils.bvhtree import BVHTree
from mathutils.geometry import barycentric_transform
ROOT=Path(__file__).resolve().parents[3];ART=ROOT/'Art/PlayerV2';OUT=ART/'Inspect/ArmLiningTransition';OUT.mkdir(parents=True,exist_ok=True)
SOURCE=ART/'DosaV2_ArmLiningClearance.blend';DEST=ART/'DosaV2_ArmLiningTransition.blend'
old=json.loads((ART/'Inspect/ArmLiningClearance/initial-geometry.json').read_text());definitions=json.loads((ART/'Validation/static-pose-definitions.json').read_text())
spec=importlib.util.spec_from_file_location('cloth_fixture',Path(__file__).with_name('diagnose_cloth_blender.py'));fixture=importlib.util.module_from_spec(spec);spec.loader.exec_module(fixture)
names=['DosaV2_ArmLining_Left','DosaV2_ArmLining_Right'];cases=['rest_settle','grip_settle','elbow_120','forearm_minus90','forearm_plus90']
digest=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
def evaluated(obj):
 e=obj.evaluated_get(bpy.context.evaluated_depsgraph_get());m=e.to_mesh();m.calc_loop_triangles();pts=[e.matrix_world@v.co for v in m.vertices];tri=[tuple(t.vertices) for t in m.loop_triangles];e.to_mesh_clear();return pts,tri
directions=[Vector(v).normalized() for v in [(1,.371,.219),(.173,1,.293),(.271,.123,1)]]
def inside(tree,point):
 votes=[]
 for d in directions:
  origin=point+d*.000001;count=0
  for _ in range(64):
   hit=tree.ray_cast(origin,d,10)
   if hit[0] is None:break
   count+=1;origin=hit[0]+d*.000005
  votes.append(count%2)
 return sum(votes)>=2
def nearest(rig,p,names,radius):
 pairs=[]
 for n in names:
  b=rig.data.bones[n];d=b.tail_local-b.head_local;t=max(0,min(1,(p-b.head_local).dot(d)/d.length_squared));pairs.append((n,(p-b.head_local-d*t).length))
 pairs.sort(key=lambda v:v[1]);minimum=pairs[0][1];weights=[(n,math.exp(-((d-minimum)/radius)**2*6)) for n,d in pairs[:4]];total=sum(w for n,w in weights);return {n:w/total for n,w in weights}
def assign(obj,index,weights):
 for g in obj.vertex_groups:g.remove([index])
 vals=sorted([(n,w) for n,w in weights.items() if w>1e-7],key=lambda v:v[1],reverse=True)[:4];total=sum(w for n,w in vals)
 for n,w in vals:(obj.vertex_groups.get(n) or obj.vertex_groups.new(name=n)).add([index],w/total,'REPLACE')
def apply_pose(rig,case):
 for p in rig.pose.bones:p.matrix_basis=Matrix.Identity(4)
 if case.endswith('_settle'):fixture.direct_pose(rig,case)
 else:
  d=next(d for d in definitions['poses'] if d['id']==case)
  for e in d['boneRotations']:rig.pose.bones[e['bone']].matrix_basis=Euler([math.radians(e[k]) for k in ('x','y','z')],'XYZ').to_matrix().to_4x4()
 bpy.context.view_layer.update()
def measure(rig):
 output=[]
 for case in cases:
  apply_pose(rig,case)
  for name in names:
   side=name.split('_')[-1];obj=bpy.data.objects[name];pts,tri=evaluated(obj);tree=BVHTree.FromPolygons(pts,tri,all_triangles=True);sleeve=bpy.data.objects['DosaV2_SleeveOuter_'+side[0]];points,_=evaluated(sleeve);rows=[]
   for i,c in enumerate(sleeve.data.color_attributes['ClothMobility'].data):
    if round(c.color[0]*255)!=0:continue
    loc,n,t,d=tree.find_nearest(points[i]);ins=inside(tree,points[i]);rows.append({'vertex':i,'signedMeters':-d if ins else d})
   output.append({'poseId':case,'lining':name,'insidePins':sum(r['signedMeters']<0 for r in rows),'minimumGapMeters':min(r['signedMeters'] for r in rows),'under2mm':[r for r in rows if r['signedMeters']<.002]})
 return output
report={'source':str(SOURCE),'sourceSha256':digest(SOURCE),'candidates':[],'productionActions':0,'status':'COMPARING_LOCAL_WEIGHTS_AND_SKIN_SEGMENTATION'}
for cuts in [0,1,2,3]:
 bpy.ops.wm.open_mainfile(filepath=str(SOURCE));scene=bpy.context.scene;rig=bpy.data.objects['DosaV2_Rig']
 for o in scene.objects:
  if o.name.startswith('CTRL_') and 'AuthoringMode' in o:o['AuthoringMode']=False;o.update_tag()
 apply_pose(rig,'rest_settle');changed=[]
 for name in names:
  obj=bpy.data.objects[name];side=name.split('_')[-1];obj.data.calc_loop_triangles();basepts=[v.co.copy() for v in obj.data.vertices];base_tri=[tuple(t.vertices) for t in obj.data.loop_triangles];tree=BVHTree.FromPolygons(basepts,base_tri,all_triangles=True);refpts=[Vector(v['co']) for v in old[name]['vertices']]
  if cuts:
   bm=bmesh.new();bm.from_mesh(obj.data)
   edges=[e for e in bm.edges if min(abs(v.co.x) for v in e.verts)<.34 and max(abs(v.co.x) for v in e.verts)>.42 and max(abs(v.co.x) for v in e.verts)<.46]
   bmesh.ops.subdivide_edges(bm,edges=edges,cuts=cuts,use_grid_fill=True);bm.to_mesh(obj.data);bm.free();obj.data.update()
  count=0
  for v in obj.data.vertices:
   if not(.30<abs(v.co.x)<.475):continue
   loc,n,t,d=tree.find_nearest(v.co);ids=base_tri[t];p=barycentric_transform(loc,*[basepts[i] for i in ids],*[refpts[i] for i in ids])
   assign(obj,v.index,nearest(rig,p,[side+'Shoulder',side+'Arm',side+'ForeArm',side+'Hand'],.09));count+=1
  obj.data.calc_loop_triangles();changed.append({'name':name,'vertices':len(obj.data.vertices),'triangles':len(obj.data.loop_triangles),'reweighted':count})
 results=measure(rig);candidate={'insertedRings':cuts,'meshes':changed,'measurements':results,'totalPenetratingPinsOverCases':sum(r['insidePins'] for r in results),'minimumGapMeters':min(r['minimumGapMeters'] for r in results)}
 report['candidates'].append(candidate)
 print(json.dumps({k:v for k,v in candidate.items() if k!='measurements'}),flush=True)
 apply_pose(rig,'rest_settle');file=ART/('DosaV2_ArmLiningTransition_'+str(cuts)+'.blend');bpy.ops.wm.save_as_mainfile(filepath=str(file));candidate['derivative']=str(file);candidate['sha256']=digest(file)
 (OUT/'transition-comparison.json').write_text(json.dumps(report,indent=2))
 if candidate['minimumGapMeters']>=.002:break
