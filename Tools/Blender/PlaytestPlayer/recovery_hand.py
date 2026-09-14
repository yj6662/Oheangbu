"""Local A-hand mesh derivative: joint tessellation, bounded smoothing and skin weights.
Never changes the C02 bind or geometry outside the selected wrist/hand region.
"""
import bpy,bmesh,json,hashlib,math
from pathlib import Path
from mathutils import Vector
from mathutils.bvhtree import BVHTree
from mathutils.interpolate import poly_3d_calc
R=Path('C:/Users/yj666/Oheangbu');O=R/'Art/PlaytestRecovery/Hands'
for sub in ['Work','Exports','Textures','Validation']:(O/sub).mkdir(parents=True,exist_ok=True)
source=R/'Art/PlaytestPolish/Hands/Work/Player_C02_GripA.blend'
bpy.ops.wm.open_mainfile(filepath=str(source));rig=bpy.data.objects['Armature'];body=bpy.data.objects['C02_Mesh_0'];rig.animation_data_clear()
for b in rig.pose.bones:b.matrix_basis.identity()
bpy.context.view_layer.update();bind={b.name:[list(row) for row in b.matrix_local] for b in rig.data.bones}
outside=[v.co.copy() for v in body.data.vertices if v.co.x>=-64];before=len(body.data.polygons)
body.data.calc_loop_triangles()
oldpoints=[v.co.copy() for v in body.data.vertices]
oldtris=[tuple(t.vertices) for t in body.data.loop_triangles]
oldnormals=[[body.data.corner_normals[i].vector.copy() for i in t.loops] for t in body.data.loop_triangles]
normalSurface=BVHTree.FromPolygons(oldpoints,oldtris,all_triangles=True)
bm=bmesh.new();bm.from_mesh(body.data);bm.verts.ensure_lookup_table()
original={v:v.co.copy() for v in bm.verts};joint=[b.head_local.copy() for b in rig.data.bones if b.name.startswith('RightHand')]
faces=[f for f in bm.faces if all(v.co.x<-69 for v in f.verts)]
joined=bmesh.ops.join_triangles(bm,faces=faces,angle_face_threshold=.45,angle_shape_threshold=.6,cmp_uvs=True,cmp_materials=True)
edges=[e for e in bm.edges if all(v.co.x<-64 for v in e.verts) and e.calc_length()>.65 and min(((e.verts[0].co+e.verts[1].co)*.5-p).length for p in joint)<1.05]
edges=sorted(edges,key=lambda e:-e.calc_length())[:120]
bmesh.ops.subdivide_edges(bm,edges=edges,cuts=1,use_grid_fill=True,smooth=0)
selected=[v for v in bm.verts if v.co.x<-64];base={v:v.co.copy() for v in selected}
# Smooth the hand back and wrist while keeping each change bounded to 0.45mm.
# Skin pads retain their radial detail; the later dense shaft-contact check is separate.
for _ in range(3):
 updates={}
 for v in selected:
  neighbors=[e.other_vert(v) for e in v.link_edges]
  if not neighbors or v.is_boundary:continue
  mean=sum((n.co for n in neighbors),Vector())/len(neighbors)
  amount=.13 if v.co.x<-69 else .08;delta=(v.co.lerp(mean,amount)-base[v])
  if delta.length>.045:delta=delta.normalized()*.045
  updates[v]=base[v]+delta
 for v,co in updates.items():v.co=co
maxchange=max((v.co-co).length*.01 for v,co in base.items())
bm.normal_update();bm.to_mesh(body.data);bm.free();body.data.update()
for polygon in body.data.polygons:
 if all(body.data.vertices[i].co.x<-64 for i in polygon.vertices):polygon.use_smooth=True
body.data.update();normals=[]
for loop in body.data.loops:
 v=body.data.vertices[loop.vertex_index];hit,n,index,d=normalSurface.find_nearest(v.co)
 points=[oldpoints[i] for i in oldtris[index]];weights=poly_3d_calc(points,hit)
 normal=sum((n*w for n,w in zip(oldnormals[index],weights)),Vector()).normalized()
 if v.co.x<-64:normal=normal.lerp(v.normal,.55).normalized()
 normals.append(normal)
body.data.normals_split_custom_set(normals)
# Preserve interpolated skinning from new vertices, then normalize and cap local influences.
changed=0;bad=0;maxerr=0;maxweights=0
for v in body.data.vertices:
 if v.co.x>=-64:continue
 weights=sorted([(g.weight,body.vertex_groups[g.group]) for g in v.groups if g.weight>1e-8],key=lambda p:-p[0])[:4]
 total=sum(w for w,g in weights)
 if total<=0:bad+=1;continue
 for g in list(v.groups):body.vertex_groups[g.group].remove([v.index])
 for w,g in weights:g.add([v.index],w/total,'REPLACE')
 maxweights=max(maxweights,len(weights));maxerr=max(maxerr,abs(sum(w/total for w,g in weights)-1));changed+=1
body.data.calc_loop_triangles();tris=len(body.data.loop_triangles)
assert bind=={b.name:[list(row) for row in b.matrix_local] for b in rig.data.bones}
assert bad==0 and maxerr<=1e-4
for image in bpy.data.images:
 if image.packed_file:
  name=Path(image.filepath.replace('\\','/')).name or image.name+'.png';(O/'Textures'/name).write_bytes(image.packed_file.data)
for p in ['contact_fit.json','hand_rig_draft.json','shaft_input.npz']:
 src=R/'Art/PlaytestPolish/Hands/Validation'/p
 if src.exists():(O/'Validation'/p).write_bytes(src.read_bytes())
bpy.ops.file.pack_all();bpy.ops.wm.save_as_mainfile(filepath=str(O/'Work/Player_C02_GripA_Local.blend'))
report={'status':'LOCAL_MESH_DERIVATIVE_REQUIRES_CONTACT_AND_VISUAL_QA','source':str(source),'sha256':hashlib.sha256(source.read_bytes()).hexdigest(),
 'method':'Merge compatible local hand triangle pairs; insert joint-support edges; bounded wrist/hand smoothing, UV corner interpolation and 4-weight normalization. This is local topology repair, not full hand replacement.',
 'sourceFaces':before,'bodyTriangles':tris,'jointEdgesSubdivided':len(edges),'localVerticesWeighted':changed,'maximumShapeChangeMeters':maxchange,'maximumWeightCount':maxweights,'weightSumError':maxerr,'bindUnchanged':True,'outsideRegion':'x>=-64 source cm untouched; face/torso/outfit design unchanged','visualApproval':'UNVERIFIED'}
(O/'Validation/mesh_repair.json').write_text(json.dumps(report,indent=2));print(json.dumps(report),flush=True)
