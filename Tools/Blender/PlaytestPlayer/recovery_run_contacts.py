"""Retain sword-run upper body; retarget source foot trajectories with fixed C02 limb lengths."""
import bpy,math,json,hashlib
from pathlib import Path
from mathutils import Matrix,Vector
R=Path('C:/Users/yj666/Oheangbu');O=R/'Art/PlaytestRecovery/Motion'
bpy.ops.wm.open_mainfile(filepath=str(O/'Player_C02_AttachedMotions.blend'))
rig=bpy.data.objects['Armature'];scene=bpy.context.scene;action=bpy.data.actions['PT_RunForward']
rig.animation_data.action=action
if action.slots:rig.animation_data.action_slot=action.slots[0]
before=set(bpy.data.objects);bpy.ops.import_scene.fbx(filepath=str(R/'Art/PlaytestRecovery/MotionSources/Run With Sword.fbx'),automatic_bone_orientation=False)
added=set(bpy.data.objects)-before;src=next(o for o in added if o.type=='ARMATURE');by={b.name.split(':')[-1]:b for b in src.pose.bones}
sr={b.name.split(':')[-1]:src.matrix_world@b.matrix_local for b in src.data.bones};tr={b.name:rig.matrix_world@b.matrix_local for b in rig.data.bones}
def frame(m):
 up=(m['Head'].translation-m['Hips'].translation).normalized();right=(m['RightArm'].translation-m['LeftArm'].translation).normalized();f=right.cross(up).normalized()
 if f.dot(m['LeftToeBase'].translation-m['LeftFoot'].translation)<0:f=-f
 return Matrix((up.cross(f).normalized(),f,up)).transposed().to_quaternion()
convert=frame(tr)@frame(sr).inverted();scale=(tr['Hips'].translation.z-tr['LeftFoot'].translation.z)/(sr['Hips'].translation.z-sr['LeftFoot'].translation.z)
start,end=src.animation_data.action.frame_range;scene.frame_set(int(start));first=(src.matrix_world@by['Hips'].matrix).translation.copy();scene.frame_set(int(end));last=(src.matrix_world@by['Hips'].matrix).translation.copy()
source=[];poses=[]
for i in range(int(end-start)+1):
 scene.frame_set(int(start)+i);source.append({s:(src.matrix_world@by[s+'Foot'].matrix).translation.copy() for s in ['Left','Right']})
 poses.append({b.name:b.matrix_basis.copy() for b in rig.pose.bones})
floor={s:min(row[s].z for row in source) for s in ['Left','Right']};clamped=0;maxError=0
def point(n):return (rig.matrix_world@rig.pose.bones[n].matrix).translation
def rotate(n,q):
 b=rig.pose.bones[n];p,r,s=(rig.matrix_world@b.matrix).decompose();b.matrix=rig.matrix_world.inverted()@Matrix.LocRotScale(p,q@r,s);bpy.context.view_layer.update()
new=[]
for i in range(len(source)):
 scene.frame_set(i+1)
 for b in rig.pose.bones:b.matrix_basis=poses[i][b.name]
 bpy.context.view_layer.update()
 for side in ['Left','Right']:
  upper,lower,foot=side+'UpLeg',side+'Leg',side+'Foot';u,l,f=point(upper),point(lower),point(foot);q=(rig.matrix_world@rig.pose.bones[foot].matrix).to_quaternion()
  delta=convert@(source[i][side]-first.lerp(last,i/(len(source)-1))-(sr[foot].translation-sr['Hips'].translation))*scale
  target=tr[foot].translation+delta;target.z=tr[foot].translation.z+(source[i][side].z-floor[side])*scale
  al=(l-u).length;bl=(f-l).length;axis=(target-u).normalized();distance=(target-u).length
  if distance>(al+bl)*.999:clamped+=1;distance=(al+bl)*.999;target=u+axis*distance
  along=(al*al-bl*bl+distance*distance)/(2*distance);bend=l-u; bend=(bend-axis*bend.dot(axis)).normalized()
  knee=u+axis*along+bend*math.sqrt(max(0,al*al-along*along))
  rotate(upper,(point(lower)-point(upper)).rotation_difference(knee-point(upper)));rotate(lower,(point(foot)-point(lower)).rotation_difference(target-point(lower)))
  b=rig.pose.bones[foot];p,r,s=(rig.matrix_world@b.matrix).decompose();b.matrix=rig.matrix_world.inverted()@Matrix.LocRotScale(p,q,s);bpy.context.view_layer.update();maxError=max(maxError,(point(foot)-target).length)
 new.append({b.name:b.matrix_basis.copy() for b in rig.pose.bones})
for obj in added:bpy.data.objects.remove(obj,do_unlink=True)
for a in list(bpy.data.actions):
 if not a.name.startswith('PT_') and a.users==0:bpy.data.actions.remove(a)
for i,pose in enumerate(new,1):
 scene.frame_set(i)
 for b in rig.pose.bones:
  b.matrix_basis=pose[b.name];b.keyframe_insert('location',frame=i);b.keyframe_insert('rotation_quaternion',frame=i);b.keyframe_insert('scale',frame=i)
bpy.ops.wm.save_as_mainfile(filepath=str(O/'Player_C02_AttachedMotions.blend'));rig.animation_data.action=None
for b in rig.pose.bones:b.matrix_basis.identity()
bpy.context.view_layer.update();bpy.ops.object.select_all(action='DESELECT');rig.select_set(True);bpy.context.view_layer.objects.active=rig
fbx=O/'Player_C02_AttachedMotions.fbx'
bpy.ops.export_scene.fbx(filepath=str(fbx),use_selection=True,object_types={'ARMATURE'},add_leaf_bones=False,bake_anim=True,bake_anim_use_all_actions=True,bake_anim_use_nla_strips=False,bake_anim_force_startend_keying=True,bake_anim_simplify_factor=0,axis_forward='-Z',axis_up='Y')
(O/'run_contact_retarget.json').write_text(json.dumps({'method':'Source ankle trajectories, C02 fixed-length two-bone IK. Original upper body keys retained.','clampedSamples':clamped,'maxSolveErrorMeters':maxError,'fbxSha256':hashlib.sha256(fbx.read_bytes()).hexdigest()},indent=2))
