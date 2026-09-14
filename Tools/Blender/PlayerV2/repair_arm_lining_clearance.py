"""Local closed ArmLining correction. No source overwrite, cloth/pin/skin edits or actions."""
import bpy, bmesh, json, math, hashlib, sys, importlib.util
from pathlib import Path
from mathutils import Vector, Matrix, Euler
from mathutils.bvhtree import BVHTree
ROOT=Path(__file__).resolve().parents[3]; ART=ROOT/'Art/PlayerV2'
SOURCE=ART/'Inspect/ClothBlender/Inputs/Assembled-final-46655083.blend'
OUT=ART/'Inspect/ArmLiningClearance';OUT.mkdir(parents=True,exist_ok=True)
DEST=ART/'DosaV2_ArmLiningClearance.blend'
SHA='466550837199954b9b8ad26daed1a0224841e5ff1d4379fc1cadef69269b3ef4'
TARGETS=['DosaV2_ArmLining_Left','DosaV2_ArmLining_Right']
def digest(p):return hashlib.sha256(Path(p).read_bytes()).hexdigest()
assert digest(SOURCE)==SHA
bpy.ops.wm.open_mainfile(filepath=str(SOURCE));scene=bpy.context.scene;rig=bpy.data.objects['DosaV2_Rig']
for o in scene.objects:
 if o.name.startswith('CTRL_') and 'AuthoringMode' in o:o['AuthoringMode']=False;o.update_tag()
for p in rig.pose.bones:p.matrix_basis=Matrix.Identity(4)
bpy.context.view_layer.update()
def mesh_snapshot(obj,include_positions=True):
 m=obj.data
 data={'topology':[list(p.vertices) for p in m.polygons], 'weights':[[(obj.vertex_groups[g.group].name,g.weight) for g in v.groups] for v in m.vertices],
 'uv':[[list(v.uv) for v in layer.data] for layer in m.uv_layers], 'colors':{a.name:[list(v.color) for v in a.data] for a in m.color_attributes},
 'materialIndices':[p.material_index for p in m.polygons], 'materials':[a.name for a in m.materials]}
 if include_positions:data['positions']=[list(v.co) for v in m.vertices]
 if m.shape_keys:data['shapes']={k.name:[list(v.co) for v in k.data] for k in m.shape_keys.key_blocks}
 return hashlib.sha256(json.dumps(data,separators=(',',':')).encode()).hexdigest()
protected={o.name:mesh_snapshot(o) for o in scene.objects if o.type=='MESH' and o.name not in TARGETS}
target_protected={n:mesh_snapshot(bpy.data.objects[n],False) for n in TARGETS}
bone_hash=hashlib.sha256(json.dumps({b.name:[list(r) for r in b.matrix_local] for b in rig.data.bones},sort_keys=True).encode()).hexdigest()
original={n:[v.co.copy() for v in bpy.data.objects[n].data.vertices] for n in TARGETS}
if '--inspect' in sys.argv:
 report={n:{'matrix':[list(r) for r in bpy.data.objects[n].matrix_world],'vertices':[{'i':v.index,'co':list(v.co),'weights':{bpy.data.objects[n].vertex_groups[g.group].name:g.weight for g in v.groups}} for v in bpy.data.objects[n].data.vertices]} for n in TARGETS}
 report['bones']={n:{'head':list(rig.data.bones[n].head_local),'tail':list(rig.data.bones[n].tail_local)} for n in ['LeftArm','LeftForeArm','RightArm','RightForeArm']}
 report['sleevePins']={o.name:[{'index':v.index,'co':list(o.matrix_world@v.co),'weights':{o.vertex_groups[g.group].name:g.weight for g in v.groups}} for v,c in zip(o.data.vertices,o.data.color_attributes['ClothMobility'].data) if round(c.color[0]*255)==0] for o in scene.objects if o.type=='MESH' and o.name.startswith('DosaV2_SleeveOuter_')}
 (OUT/'initial-geometry.json').write_text(json.dumps(report,indent=2));print('INSPECTED');sys.exit()
spec=importlib.util.spec_from_file_location('cloth_fixture',Path(__file__).with_name('diagnose_cloth_blender.py'))
fixture=importlib.util.module_from_spec(spec);spec.loader.exec_module(fixture)
definitions=json.loads((ART/'Validation/static-pose-definitions.json').read_text())
def pose(case):
 if case.endswith('_settle'):fixture.direct_pose(rig,case);return
 for p in rig.pose.bones:p.matrix_basis=Matrix.Identity(4)
 d=next(d for d in definitions['poses'] if d['id']==case)
 for entry in d['boneRotations']:
  rig.pose.bones[entry['bone']].matrix_basis=Euler([math.radians(entry[k]) for k in ('x','y','z')],'XYZ').to_matrix().to_4x4()
 bpy.context.view_layer.update()
def evaluated(obj):
 e=obj.evaluated_get(bpy.context.evaluated_depsgraph_get());m=e.to_mesh();m.calc_loop_triangles()
 pts=[e.matrix_world@v.co for v in m.vertices];tri=[tuple(t.vertices) for t in m.loop_triangles]
 e.to_mesh_clear();return pts,tri
def bvh_for(obj):
 pts,tri=evaluated(obj);return pts,tri,BVHTree.FromPolygons(pts,tri,all_triangles=True)
def inverse_skin(obj,vertex):
 acc=Matrix(((0.,0.,0.),(0.,0.,0.),(0.,0.,0.)))
 for g in vertex.groups:
  name=obj.vertex_groups[g.group].name
  if name not in rig.pose.bones:continue
  mat=(rig.matrix_world@rig.pose.bones[name].matrix@rig.data.bones[name].matrix_local.inverted()@rig.matrix_world.inverted()@obj.matrix_world).to_3x3()
  for row in range(3):
   for col in range(3):acc[row][col]+=mat[row][col]*g.weight
 return acc.inverted_safe()
surfaces=[o for o in scene.objects if o.type=='MESH' and o.name.startswith('DosaV2_SleeveOuter_')]
pin_indices={o.name:[v.index for v,c in zip(o.data.vertices,o.data.color_attributes['ClothMobility'].data) if round(c.color[0]*255)==0] for o in surfaces}
def measure(case):
 pose(case);trees={n:bvh_for(bpy.data.objects[n]) for n in TARGETS};rows=[]
 for o in surfaces:
  points,_=evaluated(o)
  n=TARGETS[0] if o.name.endswith('_L') else TARGETS[1]
  pts,tri,tree=trees[n]
  for index in pin_indices[o.name]:
   point=points[index];loc,normal,t,dist=tree.find_nearest(point)
   signed=(point-loc).dot(normal)
   if dist<.04:
    rows.append({'case':case,'surface':o.name,'vertex':index,'lining':n,'point':list(point),'nearest':list(loc),'normal':list(normal),'signedMeters':signed,'distanceMeters':dist,'triangle':t,'triangleVertices':tri[t]})
 return rows,trees
CASES=['rest_settle','grip_settle','raised_arms_settle']
before={c:measure(c)[0] for c in CASES}
margin=.00215
iterations=[]
for it in range(40):
 violations=0;worst=1.
 for case in CASES:
  rows,trees=measure(case)
  for row in rows:
   signed=row['signedMeters'];worst=min(worst,signed)
   if signed>=.00205:continue
   # Correct only local sleeve/lining contact region. No lower arm or cuff shrink.
   obj=bpy.data.objects[row['lining']]; source_point=bpy.data.objects[row['surface']].matrix_world@bpy.data.objects[row['surface']].data.vertices[row['vertex']].co
   if not(.28<abs(source_point.x)<.43 and source_point.y<.012 and source_point.z>1.29):continue
   violations+=1
   normal=Vector(row['normal']);move=normal*(-min(.002,margin-signed))
   tri_indices=row['triangleVertices'];centers=[original[obj.name][i] for i in tri_indices]
   for v in obj.data.vertices:
    distance=min((original[obj.name][v.index]-p).length for p in centers)
    falloff=max(0.,1.-distance/.042);falloff=falloff*falloff*(3.-2.*falloff)
    if falloff<=0:continue
    v.co+=inverse_skin(obj,v)@move*falloff
   obj.data.update();bpy.context.view_layer.update()
 iterations.append({'iteration':it,'violationsBeforeStep':violations,'minSignedMeters':worst})
 if violations==0:break
after={c:measure(c)[0] for c in CASES}
pose('rest_settle')
changed=[]
for n in TARGETS:
 obj=bpy.data.objects[n];bm=bmesh.new();bm.from_mesh(obj.data)
 bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(obj.data);bm.free()
 distances=[(v.co-original[n][v.index]).length for v in obj.data.vertices]
 obj.data.calc_loop_triangles()
 changed.append({'name':n,'vertices':len(obj.data.vertices),'triangles':len(obj.data.loop_triangles),'changedVertices':sum(d>1e-8 for d in distances),'maxDisplacementMeters':max(distances),'changedIndices':[i for i,d in enumerate(distances) if d>1e-8], 'geometryHash':mesh_snapshot(obj),'nonPositionHashUnchanged':mesh_snapshot(obj,False)==target_protected[n]})
assert all(mesh_snapshot(bpy.data.objects[n])==h for n,h in protected.items()),'Protected geometry changed'
assert all(c['nonPositionHashUnchanged'] for c in changed),'Target non-position content changed'
assert not bpy.data.actions
bpy.ops.wm.save_as_mainfile(filepath=str(DEST))
report={'status':'LOCAL_CORRECTION_CANDIDATE_REQUIRES_RENDER_AND_PARITY_BVH','source':str(SOURCE),'sourceSha256':SHA,'derivative':str(DEST),'derivativeSha256':digest(DEST),
 'method':'Only local ArmLining vertex positions adjusted inward using evaluated nearest-face normals and inverse LBS vector transforms, smooth 42mm rest-space falloff, three static constraints. Cloth/pins/weights/topology/UV/bones/hands unchanged. Global radius scaling is not used.',
 'targetGapMeters':.002,'iterations':iterations,'meshes':changed,'protectedMeshCount':len(protected),'protectedMeshesUnchanged':True,'restBoneHash':bone_hash,'productionActions':0,'before':before,'after':after}
(OUT/'repair-report.json').write_text(json.dumps(report,indent=2));print(json.dumps({k:v for k,v in report.items() if k not in ['before','after','iterations']},indent=2))
