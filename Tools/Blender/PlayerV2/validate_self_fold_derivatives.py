"""Source-bound geometry, integrity, full static-pose and opening-transition audits."""
from pathlib import Path
import ast,collections,hashlib,json,math
import bpy,numpy as np
from mathutils import Matrix,Euler
from mathutils.bvhtree import BVHTree
ROOT=Path(__file__).resolve().parents[3];ART=ROOT/'Art/PlayerV2';OUT=ART/'Inspect/HandsSelfFolds';SOURCE=ART/'Inspect/ClothBlender/Inputs/Assembled-final-46655083.blend'
defs_path=ART/'Validation/static-pose-definitions.json';defs=json.loads(defs_path.read_text());module=ast.parse(Path(__file__).with_name('audit_triangle_crossings.py').read_text());fn=next(n for n in module.body if isinstance(n,ast.FunctionDef) and n.name=='proper_crossings');exec(compile(ast.fix_missing_locations(ast.Module(body=[fn],type_ignores=[])),'proper_crossings','exec'))
def mesh_hash(o,shapes=True):
 h=hashlib.sha256()
 for v in o.data.vertices:
  h.update(np.asarray(v.co,dtype=np.float32).tobytes());h.update(json.dumps(sorted((o.vertex_groups[g.group].name,round(g.weight,8)) for g in v.groups)).encode())
 for p in o.data.polygons:h.update(np.asarray(p.vertices,dtype=np.int32).tobytes());h.update(str(p.material_index).encode())
 for layer in o.data.uv_layers:
  for uv in layer.data:h.update(np.asarray(uv.uv,dtype=np.float32).tobytes())
 if shapes and o.data.shape_keys:
  for key in o.data.shape_keys.key_blocks:
   h.update(key.name.encode())
   for v in key.data:h.update(np.asarray(v.co,dtype=np.float32).tobytes())
 return h.hexdigest()
def bone_hash(rig):
 return hashlib.sha256(json.dumps([(b.name,b.parent.name if b.parent else None,[list(v) for v in b.matrix_local]) for b in rig.data.bones]).encode()).hexdigest()
bpy.ops.wm.open_mainfile(filepath=str(SOURCE));baseline={o.name:mesh_hash(o) for o in bpy.context.scene.objects if o.type=='MESH'};baseline_hand=mesh_hash(bpy.data.objects['DosaV2_Hands'],False);baseline_bones=bone_hash(bpy.data.objects['DosaV2_Rig'])
def apply(rig,d,alpha=1):
 for b in rig.pose.bones:b.matrix_basis=Matrix.Identity(4)
 keys=bpy.data.objects['DosaV2_Hands'].data.shape_keys
 for side in ['Right','Left']:keys.key_blocks['GripPalmRelax_'+side].value=d.get('handGripCorrectives',{}).get(side.lower(),0.)*alpha
 for e in d['boneRotations']:rig.pose.bones[e['bone']].matrix_basis=Euler([math.radians(e[k])*alpha for k in ['x','y','z']],'XYZ').to_matrix().to_4x4()
 bpy.context.view_layer.update()
def measure(o):
 e=o.evaluated_get(bpy.context.evaluated_depsgraph_get());m=e.to_mesh();m.calc_loop_triangles();pts=np.asarray([e.matrix_world@v.co for v in m.vertices]);tri=np.asarray([t.vertices for t in m.loop_triangles]);mat=[o.data.materials[m.polygons[t.polygon_index].material_index].name for t in m.loop_triangles]
 tree=BVHTree.FromPolygons(pts.tolist(),tri.tolist(),all_triangles=True);pairs=[(a,b) for a,b in tree.overlap(tree) if a<b and not set(tri[a]).intersection(tri[b])];arr=np.asarray(pairs,dtype=int);hits=arr[proper_crossings(pts[tri[arr[:,0]]],pts[tri[arr[:,1]]])] if len(arr) else arr
 counts=collections.Counter(' x '.join(sorted((mat[a],mat[b]))) for a,b in hits);e.to_mesh_clear();return {'properCrossingPairs':len(hits),'materialPairs':dict(counts)}
report={'source':str(SOURCE),'sourceSha256':hashlib.sha256(SOURCE.read_bytes()).hexdigest(),'poseDefinitionsSha256':hashlib.sha256(defs_path.read_bytes()).hexdigest(),'poseCount':len(defs['poses']),'derivatives':[],'rigGate':'NOT_GRANTED','exclusions':['Shared-vertex triangle pairs, coplanar contact, cross-object contacts, and untested motion are not measured by proper crossing counts.']}
for candidate,allowed in [(ART/'DosaV2_HandsHarmonicProbe.blend',['DosaV2_Hands']),(ART/'DosaV2_WrappingForearmProbe.blend',['DosaV2_SleeveInner_L','DosaV2_SleeveInner_R'])]:
 bpy.ops.wm.open_mainfile(filepath=str(candidate));rig=bpy.data.objects['DosaV2_Rig'];record={'path':str(candidate),'sha256':hashlib.sha256(candidate.read_bytes()).hexdigest(),'changedMeshes':[o.name for o in bpy.context.scene.objects if o.type=='MESH' and mesh_hash(o)!=baseline.get(o.name)],'bonesUnchanged':bone_hash(rig)==baseline_bones,'handBasisTopologyWeightsUVUnchanged':mesh_hash(bpy.data.objects['DosaV2_Hands'],False)==baseline_hand,'actions':len(bpy.data.actions),'poses':[]}
 for d in defs['poses']:
  apply(rig,d);record['poses'].append({'poseId':d['id'],'objects':{name:measure(bpy.data.objects[name]) for name in allowed}})
 if allowed==['DosaV2_Hands']:
  d=next(p for p in defs['poses'] if p['id']=='grip_down');record['gripOpeningTransition']=[]
  for alpha in np.linspace(0,1,21):apply(rig,d,float(alpha));record['gripOpeningTransition'].append({'gripFraction':float(alpha),**measure(bpy.data.objects['DosaV2_Hands'])})
 assert set(record['changedMeshes'])==set(allowed),record['changedMeshes'];assert record['bonesUnchanged'] and record['actions']==0;report['derivatives'].append(record)
(OUT/'handoff-validation.json').write_text(json.dumps(report,indent=2));print(json.dumps([{k:v for k,v in r.items() if k not in ['poses','gripOpeningTransition']} for r in report['derivatives']],indent=2))
