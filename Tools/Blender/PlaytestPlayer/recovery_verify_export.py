import bpy,json,hashlib,collections
from pathlib import Path
from mathutils.kdtree import KDTree
R=Path('C:/Users/yj666/Oheangbu');O=R/'Art/PlaytestRecovery/Hands'
def read(path):
 bpy.ops.wm.open_mainfile(filepath=str(path));rig=bpy.data.objects['Armature'];body=bpy.data.objects['C02_Mesh_0']
 for b in rig.pose.bones:b.matrix_basis.identity()
 bpy.context.view_layer.update()
 outside=collections.Counter(tuple(round(x,5) for x in v.co) for v in body.data.vertices if v.co.x>=-64)
 bind={b.name:[round(x,6) for row in b.matrix_local for x in row] for b in rig.data.bones}
 return rig,body,outside,bind
_,_,originalOutside,originalBind=read(R/'Art/PlaytestPolish/Hands/Work/Player_C02_GripA.blend')
rig,body,outside,bind=read(O/'Work/Player_C02_GripA_Rebuilt.blend')
points=[rig.matrix_world @ v.co for v in body.data.vertices]
# Body and armature share the source centimetre transform; use the actual mesh matrix.
points=[body.matrix_world @ v.co for v in body.data.vertices]
tree=KDTree(len(points))
for i,p in enumerate(points):tree.insert(p,i)
tree.balance();body.data.calc_loop_triangles();sourceTris=len(body.data.loop_triangles)
sourceBones=set(bind);bpy.ops.wm.read_factory_settings(use_empty=True)
fbx=O/'Exports/Player_C02_GripA.fbx';bpy.ops.import_scene.fbx(filepath=str(fbx),automatic_bone_orientation=False)
rig=next(o for o in bpy.data.objects if o.type=='ARMATURE');body=bpy.data.objects['C02_Mesh_0']
body.data.calc_loop_triangles();errors=[tree.find(body.matrix_world @ v.co)[2] for v in body.data.vertices]
invalid=0;maxInfluences=0;maxSumError=0
for v in body.data.vertices:
 ws=[g.weight for g in v.groups if body.vertex_groups[g.group].name in sourceBones and g.weight>1e-8]
 maxInfluences=max(maxInfluences,len(ws));maxSumError=max(maxSumError,abs(sum(ws)-1));invalid+=int(not ws or any(w<0 for w in ws))
report={'status':'PASS' if outside==originalOutside and bind==originalBind and sourceBones==set(b.name for b in rig.data.bones) and max(errors)<1e-5 and sourceTris==len(body.data.loop_triangles) and maxInfluences<=4 and maxSumError<=.0001 and invalid==0 else 'FAIL',
 'scope':'Empty Blender scene FBX reimport; geometry/skin/bone names and unchanged outside-hand source vertices. Does not certify Unity runtime visuals.',
 'fbxSha256':hashlib.sha256(fbx.read_bytes()).hexdigest(),'outsideHandCoordinatesUnchanged':outside==originalOutside,'sourceBindUnchanged':bind==originalBind,
 'bones':len(rig.data.bones),'sourceBodyTriangles':sourceTris,'reimportBodyTriangles':len(body.data.loop_triangles),'maxGeometryDistanceMeters':max(errors),
 'maxWeightCount':maxInfluences,'maxWeightSumError':maxSumError,'unassignedOrInvalid':invalid}
(O/'Validation/fbx_roundtrip.json').write_text(json.dumps(report,indent=2));print(json.dumps(report),flush=True)
