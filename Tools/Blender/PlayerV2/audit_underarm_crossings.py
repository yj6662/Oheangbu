"""Reuse the parent's pure triangle predicate on the three repaired meshes."""
import ast,bpy,json,hashlib,math
import numpy as np
from pathlib import Path
from mathutils import Matrix,Euler
from mathutils.bvhtree import BVHTree
ROOT=Path(__file__).resolve().parents[3]
SOURCE=ROOT/'Art/PlayerV2/Inspect/ClothBlender/SleeveAttachments/DosaV2_SleeveAttachments.blend'
BASE=ROOT/'Art/PlayerV2/Inspect/ClothBlender/Inputs/Assembled-final-46655083.blend'
OUT=ROOT/'Art/PlayerV2/Inspect/ClothBlender/SleeveAttachments/Crossings';OUT.mkdir(parents=True,exist_ok=True)
parent_script=Path(__file__).with_name('audit_triangle_crossings.py')
tree=ast.parse(parent_script.read_text(encoding='utf-8'))
function=next(n for n in tree.body if isinstance(n,ast.FunctionDef) and n.name=='proper_crossings')
exec(compile(ast.Module(body=[function],type_ignores=[]),str(parent_script),'exec'))
bpy.ops.wm.open_mainfile(filepath=str(BASE))
rest={b.name:[list(row) for row in b.matrix_local] for b in bpy.data.objects['DosaV2_Rig'].data.bones}
bpy.ops.wm.open_mainfile(filepath=str(SOURCE));rig=bpy.data.objects['DosaV2_Rig']
assert rest=={b.name:[list(row) for row in b.matrix_local] for b in rig.data.bones}
assert not bpy.data.actions and not bpy.data.objects['CTRL_DosaV2_Root']['AuthoringMode']
definitions=json.loads((ROOT/'Art/PlayerV2/Validation/static-pose-definitions.json').read_text())
objects=[bpy.data.objects[n] for n in ['DosaV2_BodyCore','DosaV2_SleeveOuter_L','DosaV2_SleeveOuter_R']]
report={'status':'MEASURED_SELF_CROSSING_CANDIDATES_NOT_CLOTH_SIMULATION','sourceSha256':hashlib.sha256(SOURCE.read_bytes()).hexdigest(),
        'poseDefinitionSourceSha256':definitions['sourceSha256'],'verifiedDeformRestIdenticalToFrozenPoseSource':True,
        'predicateSourceSha256':hashlib.sha256(parent_script.read_bytes()).hexdigest(),'poses':[]}
for definition in definitions['poses']:
 if definition['id'] not in ['rest','combined_reach','hip_flex_right']:continue
 for p in rig.pose.bones:p.matrix_basis=Matrix.Identity(4)
 for row in definition['boneRotations']:rig.pose.bones[row['bone']].matrix_basis=Euler([math.radians(row[c]) for c in ['x','y','z']],'XYZ').to_matrix().to_4x4()
 bpy.context.view_layer.update();rows=[]
 for obj in objects:
  e=obj.evaluated_get(bpy.context.evaluated_depsgraph_get());m=e.to_mesh();m.calc_loop_triangles()
  points=np.array([list(e.matrix_world@v.co) for v in m.vertices],dtype=np.float64);tris=np.array([list(t.vertices) for t in m.loop_triangles],dtype=np.int32)
  bvh=BVHTree.FromPolygons(points.tolist(),tris.tolist(),all_triangles=True,epsilon=0)
  candidates=[(a,b) for a,b in bvh.overlap(bvh) if a<b and not set(tris[a]).intersection(tris[b])]
  hits=[]
  if candidates:
   pairs=np.array(candidates,dtype=np.int32);mask=proper_crossings(points[tris[pairs[:,0]]],points[tris[pairs[:,1]]])
   hits=[{'triangleA':int(a),'triangleB':int(b),'center':points[np.r_[tris[a],tris[b]]].mean(axis=0).tolist()} for a,b in pairs[mask]]
  rows.append({'name':obj.name,'properCrossingPairs':len(hits),'pairs':hits});e.to_mesh_clear()
 report['poses'].append({'id':definition['id'],'objects':rows})
 print(definition['id'],[(r['name'],r['properCrossingPairs']) for r in rows],flush=True)
(OUT/'report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
