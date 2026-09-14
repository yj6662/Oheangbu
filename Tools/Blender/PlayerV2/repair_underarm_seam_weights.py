"""Coherent torso attachment field on the shared lower axillary seam.

Derivative only: BodyCore and the two outer sleeve meshes. Rest shape, UV,
ClothMobility, hands, lining, skeleton and all other meshes remain untouched.
"""
import bpy,hashlib,importlib.util,json,math
from pathlib import Path
from mathutils import Matrix,Vector,Euler
from mathutils.bvhtree import BVHTree
ROOT=Path(__file__).resolve().parents[3]
SOURCE=ROOT/'Art/PlayerV2/Inspect/ClothBlender/OuterSeamV2/DosaV2_OuterSleeveAnchorRepair.blend'
OUT=ROOT/'Art/PlayerV2/Inspect/ClothBlender/UnderarmSeam';OUT.mkdir(parents=True,exist_ok=True)
def module(name,file):
 spec=importlib.util.spec_from_file_location(name,Path(__file__).with_name(file));m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m);return m
physics=module('cloth_physics','diagnose_cloth_blender.py');helper=module('cloth_render','build_brush.py')
def sha(path):return hashlib.sha256(Path(path).read_bytes()).hexdigest()
source_hash=sha(SOURCE);(OUT/'rebuild-source.py').write_bytes(Path(__file__).read_bytes())
bpy.ops.wm.open_mainfile(filepath=str(SOURCE));scene=bpy.context.scene;rig=bpy.data.objects['DosaV2_Rig']
for o in scene.objects:
 if o.name.startswith('CTRL_') and 'AuthoringMode' in o:o['AuthoringMode']=False;o.update_tag()
physics.direct_pose(rig,'rest_settle')
objects=[bpy.data.objects[n] for n in ['DosaV2_BodyCore','DosaV2_SleeveOuter_L','DosaV2_SleeveOuter_R']]
def weights(o,v):return {o.vertex_groups[g.group].name:g.weight for g in v.groups if g.weight>1e-8}
original={o.name:[weights(o,v) for v in o.data.vertices] for o in objects}
def smooth(lo,hi,x):
 t=max(0,min(1,(x-lo)/(hi-lo)));return t*t*(3-2*t)
def field(p):return smooth(.88,.98,p.z)*(1-smooth(1.18,1.31,p.z))*(1-smooth(.205,.285,abs(p.x)))
def nearest_torso(p):
 distances=[]
 for name in ['Spine','Spine01','Spine02']:
  b=rig.data.bones[name];d=b.tail_local-b.head_local;t=max(0,min(1,(p-b.head_local).dot(d)/d.length_squared))
  distances.append((name,(p-b.head_local-t*d).length))
 distances.sort(key=lambda r:r[1]);values=[(n,math.exp(-((d-distances[0][1])/.075)**2*6)) for n,d in distances[:2]]
 total=sum(w for n,w in values);return {n:w/total for n,w in values}
after={};change_rows=[]
for o in objects:
 after[o.name]=[]
 for vertex,before in zip(o.data.vertices,original[o.name]):
  p=vertex.co.copy();alpha=field(p)
  arm_names=[n for n in before if n.startswith(('Left','Right')) and any(n.endswith(suffix) for suffix in ['Shoulder','Arm','ForeArm','Hand'])]
  moved=sum(before[n] for n in arm_names)*alpha
  if moved<1e-6:after[o.name].append(before.copy());continue
  target={n:w for n,w in before.items() if n.startswith('Spine')};total=sum(target.values())
  target={n:w/total for n,w in target.items()} if total>.0001 else nearest_torso(p)
  new={n:w*(1-alpha) if n in arm_names else w for n,w in before.items()}
  for n,w in target.items():new[n]=new.get(n,0)+w*moved
  values=sorted(((n,w) for n,w in new.items() if w>1e-7),key=lambda r:r[1],reverse=True)[:4];total=sum(w for n,w in values)
  new={n:w/total for n,w in values};after[o.name].append(new)
  change_rows.append({'mesh':o.name,'sourceVertex':vertex.index,'sourceRestPosition':list(p),'alpha':alpha,'before':before,'after':new})
def apply_weights(data):
 for o in objects:
  for row in change_rows:
   if row['mesh']!=o.name:continue
   i=row['sourceVertex']
   for g in o.vertex_groups:g.remove([i])
   for n,w in data[o.name][i].items():
    group=o.vertex_groups.get(n) or o.vertex_groups.new(name=n);group.add([i],w,'REPLACE')
 bpy.context.view_layer.update()
def signature(o):
 d={'vertices':[list(v.co) for v in o.data.vertices],'polygons':[list(p.vertices) for p in o.data.polygons],
    'uvs':[[list(x.uv) for x in layer.data] for layer in o.data.uv_layers],
    'colours':{a.name:[list(x.color) for x in a.data] for a in o.data.color_attributes}}
 return hashlib.sha256(json.dumps(d,sort_keys=True).encode()).hexdigest()
before_geometry={o.name:signature(o) for o in scene.objects if o.type=='MESH'}
protected_weights={o.name:[weights(o,v) for v in o.data.vertices] for o in scene.objects if o.type=='MESH' and o not in objects}
rest_bones={b.name:[list(row) for row in b.matrix_local] for b in rig.data.bones}
poses=json.loads((ROOT/'Art/PlayerV2/Validation/static-pose-definitions.json').read_text())['poses']
cases=[(p['id'],p) for p in poses]+[(n,None) for n in ['rest_settle','raised_arms_settle','grip_settle']]
def pose(name,definition):
 if definition is None:physics.direct_pose(rig,name);return
 for p in rig.pose.bones:p.matrix_basis=Matrix.Identity(4)
 for row in definition['boneRotations']:
  p=rig.pose.bones[row['bone']];p.rotation_mode='XYZ';p.rotation_euler=Euler([math.radians(row[c]) for c in ['x','y','z']],'XYZ')
 bpy.context.view_layer.update()
def audit_pins():
 o=bpy.data.objects['DosaV2_BodyLining'];e=o.evaluated_get(bpy.context.evaluated_depsgraph_get());m=e.to_mesh();m.calc_loop_triangles()
 bvh=BVHTree.FromPolygons([e.matrix_world@v.co for v in m.vertices],[tuple(t.vertices) for t in m.loop_triangles],all_triangles=True);e.to_mesh_clear();rows=[]
 for o in objects[1:]:
  points=physics.evaluated_points(o)
  for i,c in enumerate(o.data.color_attributes['ClothMobility'].data):
   if c.color[0]!=0:continue
   p=Vector(points[i]);loc,normal,tri,distance=bvh.find_nearest(p)
   if (p-loc).dot(normal)>.000001:continue
   votes=[]
   for d in [(1,.371,.219),(.173,1,.293),(.271,.123,1)]:
    direction=Vector(d).normalized();origin=p+direction*.000001;count=0
    for _ in range(64):
     hit=bvh.ray_cast(origin,direction,10)
     if hit[0] is None:break
     count+=1;origin=hit[0]+direction*.000005
    votes.append(count%2)
   if sum(votes)>=2:rows.append({'mesh':o.name,'sourceVertex':i,'depth':distance,'point':list(p),'restPoint':list(o.data.vertices[i].co)})
 return {'count':len(rows),'maxDepth':max((r['depth'] for r in rows),default=0),'vertices':rows}
measurements=[]
for name,definition in cases:
 pose(name,definition);apply_weights(original);before=audit_pins();apply_weights(after);final=audit_pins()
 measurements.append({'case':name,'before':before,'after':final})
 print(name,before['count'],before['maxDepth'],final['count'],final['maxDepth'],flush=True)
physics.direct_pose(rig,'rest_settle');apply_weights(after)
assert before_geometry=={o.name:signature(o) for o in scene.objects if o.type=='MESH'}
assert protected_weights=={o.name:[weights(o,v) for v in o.data.vertices] for o in scene.objects if o.type=='MESH' and o not in objects}
assert rest_bones=={b.name:[list(row) for row in b.matrix_local] for b in rig.data.bones}
assert not bpy.data.actions
target=OUT/'DosaV2_UnderarmSeamWeights.blend';bpy.ops.wm.save_as_mainfile(filepath=str(target))
report={'status':'COHERENT_SEAM_WEIGHTS_CANDIDATE_NOT_RIG_PASS','sourceSha256':source_hash,'assembledBaseSha256':'466550837199954b9b8ad26daed1a0224841e5ff1d4379fc1cadef69269b3ef4',
 'sourceUnchanged':sha(SOURCE)==source_hash,'outputSha256':sha(target),'output':str(target),'rebuildScriptSha256':sha(OUT/'rebuild-source.py'),
 'policy':'Same spatial lower-axilla field on BodyCore and both outer sleeves. Transfer arm/clavicle influences to existing torso weighting; fade to unchanged upper/outer arm. All pins/positions/topology/UV/lining/hands/bone rest preserved.',
 'changedMeshes':{o.name:sum(r['mesh']==o.name for r in change_rows) for o in objects},'changedSourceVertices':change_rows,
 'allGeometryAndPinColoursUnchanged':True,'allProtectedWeightsUnchanged':True,'boneRestUnchanged':True,'actions':0,'measurements':measurements}
(OUT/'underarm-seam-report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
camera=scene.camera or helper.lighting(scene)
for name in ['grip_settle','combined_reach','raised_arms_settle']:
 for label,data in [('before',original),('after',after)]:
  apply_weights(data);pose(name,next((p for p in poses if p['id']==name),None))
  helper.render(scene,camera,OUT/(label+'-'+name+'.png'),(0,-.03,1.20),.79,math.pi*.15,width=1000,height=800)
