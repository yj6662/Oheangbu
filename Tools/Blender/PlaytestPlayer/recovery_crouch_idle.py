import bpy,math,json,hashlib
from pathlib import Path
from mathutils import Vector,Matrix
R=Path('C:/Users/yj666/Oheangbu/Art/PlaytestRecovery/Motion');bpy.ops.wm.open_mainfile(filepath=str(R/'Player_C02_AttachedMotions.blend'))
rig=bpy.data.objects['Armature'];scene=bpy.context.scene
a=bpy.data.actions['PT_CrouchIdle'];rig.animation_data.action=a
if a.slots:rig.animation_data.action_slot=a.slots[0]
scene.frame_set(1);bpy.context.view_layer.update()
def point(n):return (rig.matrix_world@rig.pose.bones[n].matrix).translation
def rotate(n,q):
 b=rig.pose.bones[n];m=rig.matrix_world@b.matrix;p,r,s=m.decompose();b.matrix=rig.matrix_world.inverted()@Matrix.LocRotScale(p,q@r,s);bpy.context.view_layer.update()
for side in ['Left','Right']:
 upper,lower,foot=side+'UpLeg',side+'Leg',side+'Foot';u,l,f=point(upper),point(lower),point(foot)
 target=(rig.matrix_world@rig.data.bones[foot].matrix_local).translation
 aLen=(l-u).length;bLen=(f-l).length;axis=(target-u).normalized();distance=(target-u).length
 assert distance<aLen+bLen-.001
 along=(aLen*aLen-bLen*bLen+distance*distance)/(2*distance);bend=Vector((0,-1,0));bend=(bend-axis*bend.dot(axis)).normalized()
 knee=u+axis*along+bend*math.sqrt(max(0,aLen*aLen-along*along))
 rotate(upper,(point(lower)-point(upper)).rotation_difference(knee-point(upper)))
 rotate(lower,(point(foot)-point(lower)).rotation_difference(target-point(lower)))
 b=rig.pose.bones[foot];m=rig.matrix_world@b.matrix;p,q,s=m.decompose();b.matrix=rig.matrix_world.inverted()@Matrix.LocRotScale(p,(rig.matrix_world@rig.data.bones[foot].matrix_local).to_quaternion(),s);bpy.context.view_layer.update()
poses={b.name:b.matrix_basis.copy() for b in rig.pose.bones}
for f in [1,61]:
 scene.frame_set(f)
 for b in rig.pose.bones:
  b.matrix_basis=poses[b.name];b.keyframe_insert('location',frame=f);b.keyframe_insert('rotation_quaternion',frame=f);b.keyframe_insert('scale',frame=f)
scene.frame_set(1);bpy.ops.wm.save_as_mainfile(filepath=str(R/'Player_C02_AttachedMotions.blend'))
rig.animation_data.action=None
for b in rig.pose.bones:b.matrix_basis.identity()
bpy.context.view_layer.update();bpy.ops.object.select_all(action='DESELECT');rig.select_set(True);bpy.context.view_layer.objects.active=rig
fbx=R/'Player_C02_AttachedMotions.fbx'
bpy.ops.export_scene.fbx(filepath=str(fbx),use_selection=True,object_types={'ARMATURE'},add_leaf_bones=False,bake_anim=True,bake_anim_use_all_actions=True,bake_anim_use_nla_strips=False,bake_anim_force_startend_keying=True,bake_anim_simplify_factor=0,axis_forward='-Z',axis_up='Y')
d=json.loads((R/'retarget_report.json').read_text());d['crouchIdle']='Source crouched torso retained, fixed-length C02 legs planted at both bind foot positions; knees forward. No root movement.';d['fbxSha256']=hashlib.sha256(fbx.read_bytes()).hexdigest();(R/'retarget_report.json').write_text(json.dumps(d,indent=2))
