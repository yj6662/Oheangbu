import bpy,json,math,ast
import numpy as np
from pathlib import Path
from mathutils import Matrix,Euler
from mathutils.bvhtree import BVHTree
ROOT=Path(__file__).resolve().parents[3];ART=ROOT/'Art/PlayerV2';OUT=ART/'Inspect/HandsSelfFolds';defs=json.loads((ART/'Validation/static-pose-definitions.json').read_text());module=ast.parse(Path(__file__).with_name('audit_triangle_crossings.py').read_text());fn=next(n for n in module.body if isinstance(n,ast.FunctionDef) and n.name=='proper_crossings');exec(compile(ast.fix_missing_locations(ast.Module(body=[fn],type_ignores=[])),'proper_crossings','exec'))
bpy.ops.wm.open_mainfile(filepath=str(ART/'Inspect/ClothBlender/Inputs/Assembled-final-46655083.blend'));rig=bpy.data.objects['DosaV2_Rig'];o=bpy.data.objects['DosaV2_Hands'];o.data.calc_loop_triangles();tri=np.array([t.vertices for t in o.data.loop_triangles]);rows=[]
for pose in ['grip_down','grip_up']:
 for b in rig.pose.bones:b.matrix_basis=Matrix.Identity(4)
 for e in next(d for d in defs['poses'] if d['id']==pose)['boneRotations']:rig.pose.bones[e['bone']].matrix_basis=Euler([math.radians(e[k]) for k in ('x','y','z')],'XYZ').to_matrix().to_4x4()
 for amount in [-.5,0,.25,.5,.75,1,1.25,1.5,2]:
  for name in ['GripPalmRelax_Right','GripPalmRelax_Left']:o.data.shape_keys.key_blocks[name].slider_min=-1;o.data.shape_keys.key_blocks[name].slider_max=2;o.data.shape_keys.key_blocks[name].value=amount
  bpy.context.view_layer.update();e=o.evaluated_get(bpy.context.evaluated_depsgraph_get());m=e.to_mesh();pts=np.array([e.matrix_world@v.co for v in m.vertices]);e.to_mesh_clear();tree=BVHTree.FromPolygons(pts.tolist(),tri.tolist(),all_triangles=True);arr=np.array([(a,b) for a,b in tree.overlap(tree) if a<b and not set(tri[a]).intersection(tri[b])],dtype=int);count=int(proper_crossings(pts[tri[arr[:,0]]],pts[tri[arr[:,1]]]).sum()) if len(arr) else 0;rows.append({'pose':pose,'amount':amount,'crossings':count})
(OUT/'corrective-amount-sweep.json').write_text(json.dumps(rows,indent=2));print(json.dumps(rows))
