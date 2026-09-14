import bpy,json,math
from pathlib import Path
from mathutils import Vector,Matrix
from mathutils.bvhtree import BVHTree
R=Path('C:/Users/yj666/Oheangbu');O=R/'Art/PlayerPhase1/PlaytestReRig'
bpy.ops.import_scene.fbx(filepath=str(O/'Exports/Player_C02_ReRig.fbx'))
r=bpy.data.objects['Armature'];body=bpy.data.objects['C02_Mesh_0'];r.animation_data_clear()
reference=bpy.data.objects['GripReference_R'].matrix_world.copy()
rotations={}
for b in r.data.bones:
 marker=bpy.data.objects.get('POSE_Grip_'+b.name)
 if b.name.startswith('RightHand') and marker:
  rotations[b.name]=(b.matrix_local.inverted()@r.matrix_world.inverted()@marker.matrix_world).to_quaternion()
for p in r.pose.bones:p.matrix_basis.identity()
for name,q in rotations.items():r.pose.bones[name].rotation_mode='QUATERNION';r.pose.bones[name].rotation_quaternion=q
before=set(bpy.data.objects)
bpy.ops.import_scene.fbx(filepath=str(R/'Oheangbu/Assets/_Project/Art/Characters/DosaV2/Models/SM_DosaBrushV2.fbx'))
g=bpy.data.objects['GripSocket'];tx=reference@g.matrix_world.inverted()
for o in [o for o in bpy.data.objects if o not in before and not o.parent]:o.matrix_world=tx@o.matrix_world
bpy.context.view_layer.update();h=bpy.data.objects['DosaBrushV2_Handle'];h.data.calc_loop_triangles()
bvh=BVHTree.FromPolygons([h.matrix_world@v.co for v in h.data.vertices],[t.vertices[:]for t in h.data.loop_triangles],all_triangles=True)
origin=g.matrix_world.translation;direction=(bpy.data.objects['DosaBrushV2_Rig'].matrix_world@bpy.data.objects['DosaBrushV2_Rig'].pose.bones['Bristle_01'].head-origin).normalized()
e=body.evaluated_get(bpy.context.evaluated_depsgraph_get());m=e.to_mesh();worst=0.;count=0;local=[]
for v in m.vertices:
 weights=body.data.vertices[v.index].groups
 if sum(w.weight for w in weights if body.vertex_groups[w.group].name.startswith('RightHand'))<.1:continue
 p=body.matrix_world@v.co;axis=origin+direction*(p-origin).dot(direction);d=p-axis;n=d.normalized();hit,*_=bvh.ray_cast(axis+n*.08,-n,.16)
 if hit is not None:worst=max(worst,(hit-axis).length-d.length);count+=1
e.to_mesh_clear();print(json.dumps({'roundtrip_marker_grip_max_vertex_penetration_m':worst,'samples':count,'axis':list(direction),'origin':list(origin),'marker_fingers':len(rotations)}),flush=True)
