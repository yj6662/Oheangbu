"""Inspect actual tuft topology and evaluated 8-way bend/splay self crossings."""
import bpy, ast, json, hashlib, math
import numpy as np
from collections import Counter
from pathlib import Path
from mathutils import Vector, Matrix, Quaternion
from mathutils.bvhtree import BVHTree
ROOT=Path(__file__).resolve().parents[3];ART=ROOT/'Art/PlayerV2';SOURCE=ART/'DosaBrushV2_TuftRefined.blend';OUT=ART/'Staging/BrushRefined'
module=ast.parse(Path(__file__).with_name('audit_triangle_crossings.py').read_text())
function=next(n for n in module.body if isinstance(n,ast.FunctionDef) and n.name=='proper_crossings')
exec(compile(ast.Module(body=[function],type_ignores=[]),'shared-proper-triangle-function','exec'))
bpy.ops.wm.open_mainfile(filepath=str(SOURCE));rig=bpy.data.objects['DosaBrushV2_Rig'];hair=bpy.data.objects['DosaBrushV2_Bristles'];me=hair.data
counts=Counter(tuple(sorted((p.vertices[i],p.vertices[(i+1)%len(p.vertices)]))) for p in me.polygons for i in range(len(p.vertices)))
report={'source':str(SOURCE),'sourceSha256':hashlib.sha256(SOURCE.read_bytes()).hexdigest(),'vertices':len(me.vertices),'edges':len(me.edges),'faces':len(me.polygons),
        'boundaryEdges':sum(v==1 for v in counts.values()),'nonmanifoldEdges':sum(v!=2 for v in counts.values()),'eulerCharacteristic':len(me.vertices)-len(me.edges)+len(me.polygons),
        'method':'Closed connected volume plus actual evaluated nonadjacent triangle proper segment crossings; shared vertices, coplanar contacts and cross-object contacts excluded.',
        'cases':[],'rigPass':False}
assert report['nonmanifoldEdges']==0 and report['eulerCharacteristic']==2
root=rig.data.bones['Bristle_01'].head_local.copy();tip=rig.data.bones['Bristle_06'].tail_local.copy();line=tip-root
for label,angle,bend,splay in [('rest',0,0,0),('splay',0,0,1)]+[(f'bend_{i*45}',i*math.pi/4,28,.65) for i in range(8)]:
 for b in rig.pose.bones:b.matrix_basis=Matrix.Identity(4)
 position=root.copy();axis=Vector((math.cos(angle),math.sin(angle),0)).cross(line.normalized()).normalized()
 for j in range(6):
  b=rig.data.bones[f'Bristle_{j+1:02d}'];q=Quaternion(axis,math.radians(bend)*(j/5)**1.7)
  rig.pose.bones[b.name].matrix=Matrix.Translation(position)@q.to_matrix().to_4x4()@b.matrix_local.to_quaternion().to_matrix().to_4x4();bpy.context.view_layer.update();position+=q@(line/6)
 hair.data.shape_keys.key_blocks['BristleSplay'].value=splay;bpy.context.view_layer.update()
 ev=hair.evaluated_get(bpy.context.evaluated_depsgraph_get());mesh=ev.to_mesh();mesh.calc_loop_triangles()
 co=np.array([tuple(ev.matrix_world@v.co) for v in mesh.vertices],dtype=np.float64);tris=np.array([list(t.vertices) for t in mesh.loop_triangles],dtype=np.int32)
 tree=BVHTree.FromPolygons(co.tolist(),tris.tolist(),all_triangles=True,epsilon=0)
 pairs=np.array([(a,b) for a,b in tree.overlap(tree) if a<b and not set(tris[a]).intersection(tris[b])],dtype=np.int32)
 hits=[]
 if len(pairs):hits=pairs[proper_crossings(co[tris[pairs[:,0]]],co[tris[pairs[:,1]]])].tolist()
 normals=np.cross(co[tris[:,1]]-co[tris[:,0]],co[tris[:,2]]-co[tris[:,0]])
 report['cases'].append({'id':label,'properCrossingPairs':len(hits),'pairs':hits,'minTriangleAreaM2':float(np.linalg.norm(normals,axis=1).min()/2)})
 ev.to_mesh_clear()
 assert not hits,label
report['testedCasesPassed']=len(report['cases'])
(OUT/'tuft-geometry-validation.json').write_text(json.dumps(report,indent=2))
print(json.dumps(report))
