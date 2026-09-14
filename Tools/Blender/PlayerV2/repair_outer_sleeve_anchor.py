"""Extend the existing torso/arm field over four skipped lower axilla vertices.

No pin, topology, UV, rest position, bone, hand or lining change. The source point
is an outlier because the original refinement skipped z < 1.10 m exactly.
"""
import bpy,hashlib,importlib.util,json,math
from pathlib import Path
from mathutils import Matrix,Vector,Euler
from mathutils.bvhtree import BVHTree
ROOT=Path(__file__).resolve().parents[3]
SOURCE=ROOT/'Art/PlayerV2/Inspect/ClothBlender/Inputs/Assembled-final-46655083.blend'
OUT=ROOT/'Art/PlayerV2/Inspect/ClothBlender/OuterSeamV2';OUT.mkdir(parents=True,exist_ok=True)
def module(name,file):
 spec=importlib.util.spec_from_file_location(name,Path(__file__).with_name(file));m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m);return m
physics=module('cloth_physics','diagnose_cloth_blender.py');helper=module('cloth_render','build_brush.py')
def sha(path):return hashlib.sha256(Path(path).read_bytes()).hexdigest()
source_hash=sha(SOURCE);(OUT/'rebuild-source.py').write_bytes(Path(__file__).read_bytes())
bpy.ops.wm.open_mainfile(filepath=str(SOURCE));scene=bpy.context.scene;rig=bpy.data.objects['DosaV2_Rig']
for o in scene.objects:
 if o.name.startswith('CTRL_') and 'AuthoringMode' in o:o['AuthoringMode']=False;o.update_tag()
physics.direct_pose(rig,'rest_settle')
obj=bpy.data.objects['DosaV2_SleeveOuter_R'];indices=[797,815,834,840]
for index in indices:
 assert obj.data.vertices[index].co.z<1.10
 assert obj.data.color_attributes['ClothMobility'].data[index].color[0]==0
def weights(v):return {g.group:g.weight for g in v.groups if g.weight>1e-8}
before_weights={i:weights(obj.data.vertices[i]) for i in indices}
neighbours={i:sorted({v for e in obj.data.edges if i in e.vertices for v in e.vertices if v!=i}) for i in indices}
def nearest(p,names,radius):
 pairs=[]
 for name in names:
  b=rig.data.bones[name];d=b.tail_local-b.head_local;t=max(0,min(1,(p-b.head_local).dot(d)/d.length_squared))
  pairs.append((name,(p-b.head_local-d*t).length))
 pairs.sort(key=lambda v:v[1]);minimum=pairs[0][1]
 values=[(n,math.exp(-((d-minimum)/radius)**2*6)) for n,d in pairs[:4]]
 total=sum(w for n,w in values);return {n:w/total for n,w in values}
def original_field(p):
 torso=nearest(p,['Spine','Spine01','Spine02','Neck'],.075)
 arm=nearest(p,['RightShoulder','RightArm','RightForeArm','RightHand'],.09)
 t=max(0,min(1,(abs(p.x)-.085)/.18));t=t*t*(3-2*t)
 values={n:w*(1-t) for n,w in torso.items()}
 for n,w in arm.items():values[n]=values.get(n,0)+w*t
 values=sorted(((n,w) for n,w in values.items() if w>1e-7),key=lambda v:v[1],reverse=True)[:4]
 total=sum(w for n,w in values)
 return {obj.vertex_groups[n].index:w/total for n,w in values}
new_weights={i:original_field(obj.data.vertices[i].co.copy()) for i in indices}
def assign(values):
 for index,mapping in values.items():
  for g in obj.vertex_groups:g.remove([index])
  for group,weight in mapping.items():obj.vertex_groups[group].add([index],weight,'REPLACE')
def geometry_signature(o):
 d={'vertices':[list(v.co) for v in o.data.vertices],'polygons':[list(p.vertices) for p in o.data.polygons],
    'uvs':[[list(x.uv) for x in layer.data] for layer in o.data.uv_layers],
    'colours':{a.name:[list(x.color) for x in a.data] for a in o.data.color_attributes}}
 return hashlib.sha256(json.dumps(d,sort_keys=True).encode()).hexdigest()
before_geometry={o.name:geometry_signature(o) for o in scene.objects if o.type=='MESH'}
before_allweights={o.name:[[(g.group,g.weight) for g in v.groups] for v in o.data.vertices] for o in scene.objects if o.type=='MESH'}
bone_rest={b.name:[list(row) for row in b.matrix_local] for b in rig.data.bones}
poses=json.loads((ROOT/'Art/PlayerV2/Validation/static-pose-definitions.json').read_text())['poses']
cases=[(p['id'],p) for p in poses]+[(n,None) for n in ['rest_settle','raised_arms_settle','grip_settle']]
def apply(name,definition):
 if definition is None:physics.direct_pose(rig,name);return
 for p in rig.pose.bones:p.matrix_basis=Matrix.Identity(4)
 for r in definition['boneRotations']:
  p=rig.pose.bones[r['bone']];p.rotation_mode='XYZ';p.rotation_euler=Euler([math.radians(r[c]) for c in ['x','y','z']],'XYZ')
 bpy.context.view_layer.update()
def measure(index):
 points=physics.evaluated_points(obj);p=Vector(points[index]);lining=bpy.data.objects['DosaV2_BodyLining']
 e=lining.evaluated_get(bpy.context.evaluated_depsgraph_get());m=e.to_mesh();m.calc_loop_triangles()
 bvh=BVHTree.FromPolygons([e.matrix_world@v.co for v in m.vertices],[tuple(t.vertices) for t in m.loop_triangles],all_triangles=True)
 loc,normal,tri,distance=bvh.find_nearest(p);e.to_mesh_clear();votes=[]
 for d in [(1,.371,.219),(.173,1,.293),(.271,.123,1)]:
  direction=Vector(d).normalized();origin=p+direction*.000001;count=0
  for _ in range(64):
   hit=bvh.ray_cast(origin,direction,10)
   if hit[0] is None:break
   count+=1;origin=hit[0]+direction*.000005
  votes.append(count%2)
 edge_rows=[{'other':i,'rest':(obj.data.vertices[i].co-obj.data.vertices[index].co).length,'posed':(Vector(points[i])-p).length} for i in neighbours[index]]
 return {'point':list(p),'insideActualBodyLining':sum(votes)>=2,'bodySurfaceDistanceMeters':distance,'parityVotes':votes,
         'edges':edge_rows,'maxIncidentEdgeRatio':max(r['posed']/r['rest'] for r in edge_rows)}
measurements=[]
for name,definition in cases:
 apply(name,definition);assign(before_weights);bpy.context.view_layer.update();before={i:measure(i) for i in indices}
 assign(new_weights);bpy.context.view_layer.update();after={i:measure(i) for i in indices}
 measurements.append({'case':name,'before':before,'after':after})
physics.direct_pose(rig,'rest_settle');assign(new_weights);bpy.context.view_layer.update()
after_geometry={o.name:geometry_signature(o) for o in scene.objects if o.type=='MESH'}
changes=[]
for o in scene.objects:
 if o.type!='MESH':continue
 for v,previous in zip(o.data.vertices,before_allweights[o.name]):
  if [(g.group,g.weight) for g in v.groups]!=previous:changes.append((o.name,v.index))
assert before_geometry==after_geometry
assert changes==[(obj.name,i) for i in indices]
assert bone_rest=={b.name:[list(row) for row in b.matrix_local] for b in rig.data.bones}
assert not bpy.data.actions
target=OUT/'DosaV2_OuterSleeveAnchorRepair.blend';bpy.ops.wm.save_as_mainfile(filepath=str(target))
report={'status':'LOCAL_WEIGHT_REPAIR_MEASURED_PENDING_PARENT_IMPORT','sourceSha256':source_hash,'sourceUnchanged':sha(SOURCE)==source_hash,
 'output':str(target),'outputSha256':sha(target),'rebuildScriptSha256':sha(OUT/'rebuild-source.py'),'changedMeshes':[obj.name],
 'changedVertices':changes,'sameSeamNeighbours':neighbours,'originalFieldSource':'Tools/Blender/PlayerV2/refine_character_weights.py base_arm / nearest; identical function extended to four vertices skipped by z<1.10',
 'beforeWeights':{i:{obj.vertex_groups[g].name:w for g,w in values.items()} for i,values in before_weights.items()},
 'afterWeights':{i:{obj.vertex_groups[g].name:w for g,w in values.items()} for i,values in new_weights.items()},'allMeshTopologyUVPositionsAndColoursUnchanged':before_geometry==after_geometry,
 'allOtherVertexWeightsUnchanged':len(changes)==4,'boneRestUnchanged':True,'actions':0,'measurements':measurements}
(OUT/'outer-sleeve-anchor-report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print(json.dumps({k:v for k,v in report.items() if k!='measurements'},indent=2),flush=True)
camera=scene.camera or helper.lighting(scene)
for name in ['grip_settle','combined_reach']:
 for label,w in [('before',before_weights),('after',new_weights)]:
  assign(w);apply(name,next((p for p in poses if p['id']==name),None))
  helper.render(scene,camera,OUT/(label+'-anchor-'+name+'.png'),(0,-.03,1.20),.79,math.pi*.15,width=1000,height=800)
