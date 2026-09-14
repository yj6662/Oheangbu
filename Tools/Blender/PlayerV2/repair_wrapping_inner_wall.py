"""Add one local skin sample ring to the long hidden inner wall; retain visible cuff."""
import bpy,bmesh,json,math,hashlib,ast
import numpy as np
from pathlib import Path
from mathutils import Matrix,Euler,Vector
from mathutils.bvhtree import BVHTree
ROOT=Path(__file__).resolve().parents[3];ART=ROOT/'Art/PlayerV2';OUT=ART/'Inspect/WrappingInnerWall';OUT.mkdir(parents=True,exist_ok=True)
SOURCE=ART/'Inspect/ClothBlender/Inputs/Assembled-final-46655083.blend';DEST=ART/'DosaV2_WrappingInnerWall.blend'
defs=json.loads((ART/'Validation/static-pose-definitions.json').read_text());module=ast.parse(Path(__file__).with_name('audit_triangle_crossings.py').read_text());fn=next(n for n in module.body if isinstance(n,ast.FunctionDef) and n.name=='proper_crossings');exec(compile(ast.fix_missing_locations(ast.Module(body=[fn],type_ignores=[])),'proper_crossings','exec'))
bpy.ops.wm.open_mainfile(filepath=str(SOURCE));rig=bpy.data.objects['DosaV2_Rig'];obj=bpy.data.objects['DosaV2_SleeveInner_L'];m=obj.data
for p in rig.pose.bones:p.matrix_basis=Matrix.Identity(4)
bpy.context.view_layer.update();before_count=len(m.vertices);before_co=np.array([v.co for v in m.vertices]);m.calc_loop_triangles();before_tris=len(m.loop_triangles)
def smooth(a,b,x):t=max(0,min(1,(x-a)/(b-a)));return t*t*(3-2*t)
def set_weights(v):
 x=abs(v.co.x);h=smooth(.638,.665,x);f=smooth(.407,.478,x)*(1-h);w={'LeftArm':1-h-f,'LeftForeArm':f,'LeftHand':h}
 for group in obj.vertex_groups:group.remove([v.index])
 for n,value in w.items():
  if value>1e-8:(obj.vertex_groups.get(n) or obj.vertex_groups.new(name=n)).add([v.index],value,'REPLACE')
bm=bmesh.new();bm.from_mesh(m);original_verts=set(bm.verts)
edges=[e for e in bm.edges if e.calc_length()>.18 and min(v.co.x for v in e.verts)>.42 and max(v.co.x for v in e.verts)<.65 and all(m.materials[f.material_index].name=='DosaV2_WristWrapping' for f in e.link_faces)]
assert len(edges)==24,len(edges)
endpoints=[(e.verts[0].co.copy(),e.verts[1].co.copy()) for e in edges]
bmesh.ops.subdivide_edges(bm,edges=edges,cuts=1,use_grid_fill=True)
new=[v for v in bm.verts if min(np.linalg.norm(before_co-np.array(v.co),axis=1))>1e-7];assert len(new)==24,(len(new),[list(v.co) for v in new[:3]])
for v in new:
 a,b=min(endpoints,key=lambda ab:(v.co-(ab[0]+ab[1])*.5).length);t=(.470-a.x)/(b.x-a.x);v.co=a+(b-a)*t
bm.to_mesh(m);bm.free();m.update()
new_indices=[v.index for v in m.vertices if min(np.linalg.norm(before_co-np.array(v.co),axis=1))>1e-7]
assert len(new_indices)==24
for i in new_indices:set_weights(m.vertices[i])
def measure(pose_id):
 d=next(d for d in defs['poses'] if d['id']==pose_id)
 for b in rig.pose.bones:b.matrix_basis=Matrix.Identity(4)
 for e in d['boneRotations']:rig.pose.bones[e['bone']].matrix_basis=Euler([math.radians(e[k]) for k in ('x','y','z')],'XYZ').to_matrix().to_4x4()
 bpy.context.view_layer.update();e=obj.evaluated_get(bpy.context.evaluated_depsgraph_get());mesh=e.to_mesh();mesh.calc_loop_triangles();pts=np.array([e.matrix_world@v.co for v in mesh.vertices]);tris=np.array([t.vertices for t in mesh.loop_triangles]);materials=[obj.data.materials[mesh.polygons[t.polygon_index].material_index].name for t in mesh.loop_triangles]
 tree=BVHTree.FromPolygons(pts.tolist(),tris.tolist(),all_triangles=True);pairs=[(a,b) for a,b in tree.overlap(tree) if a<b and not set(tris[a]).intersection(tris[b])];arr=np.array(pairs,dtype=int);hits=arr[proper_crossings(pts[tris[arr[:,0]]],pts[tris[arr[:,1]]])] if len(arr) else arr;counts={};details=[]
 for a,b in hits:
  key=' x '.join(sorted([materials[a],materials[b]]));counts[key]=counts.get(key,0)+1
  if materials[a]==materials[b]=='DosaV2_WristWrapping':details.append({'a':int(a),'b':int(b),'center':pts[np.r_[tris[a],tris[b]]].mean(axis=0).tolist()})
 e.to_mesh_clear();return {'poseId':pose_id,'materialPairs':counts,'wrappingSelfCrossings':len(details),'selfPairs':details}
results=[measure(p) for p in ['rest','grip_down','grip_up','combined_reach','elbow_120','forearm_minus90','forearm_plus90']]
for p in rig.pose.bones:p.matrix_basis=Matrix.Identity(4)
bpy.context.view_layer.update();m.calc_loop_triangles();bpy.ops.wm.save_as_mainfile(filepath=str(DEST))
report={'status':'INNER_WALL_SEGMENTATION_CANDIDATE','sourceSha256':hashlib.sha256(SOURCE.read_bytes()).hexdigest(),'derivative':str(DEST),'derivativeSha256':hashlib.sha256(DEST.read_bytes()).hexdigest(),
 'changedMesh':obj.name,'beforeVertices':before_count,'afterVertices':len(m.vertices),'beforeTriangles':before_tris,'afterTriangles':len(m.loop_triangles),
 'newVertices':new_indices,'existingRestVerticesRetained':all(min((v.co-Vector(p)).length for v in m.vertices)<1e-7 for p in before_co),
 'method':'One inner-wall ring at x=.470m between .426/.645m; original outer wrapping geometry and all existing vertex weights retained. New hidden ring uses same forearm transition function as outer wall.',
 'results':results,'visibleReview':'PENDING','rigGate':'NOT_GRANTED'}
(OUT/'repair-report.json').write_text(json.dumps(report,indent=2));print(json.dumps(report,indent=2))
