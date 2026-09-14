"""Mixamo source actions -> unchanged C02 bind, animation-only derivative.
Run with Blender in background. Source objects/FBXs are never overwritten.
"""
import bpy,math,json,hashlib,statistics
from pathlib import Path
from mathutils import Matrix,Vector,Quaternion
R=Path('C:/Users/yj666/Oheangbu');O=R/'Art/PlaytestRecovery/Motion';O.mkdir(parents=True,exist_ok=True)
source=R/'Art/PlaytestPolish/Locomotion/Blender/Player_Locomotion_Derivative.blend'
bpy.ops.wm.open_mainfile(filepath=str(source));rig=bpy.data.objects['Armature'];scene=bpy.context.scene;scene.render.fps=60
for a in list(bpy.data.actions):bpy.data.actions.remove(a)
rig.animation_data_clear()
for b in rig.pose.bones:b.matrix_basis.identity()
bpy.context.view_layer.update()
bones=sorted(rig.data.bones,key=lambda b:len(b.parent_recursive));rest={b.name:(rig.matrix_world@b.matrix_local).copy() for b in bones}
rest={n:Matrix.LocRotScale(m.translation,m.to_quaternion(),Vector((1,1,1))) for n,m in rest.items()}
names=[b.name for b in rig.data.bones]
alias={'Spine02':'Spine','Spine01':'Spine1','Spine':'Spine2','neck':'Neck'}
specs=[('WalkForward','Walk With Briefcase',None),('RunForward','Run With Sword',None),('StartForward','Start Walking',(.80,1.12)),
 ('CrouchForward','Crouch Walk Forward',None),('CrouchBack','Crouch Walk Back',None),('CrouchLeft','Crouch Walk Left',None),('CrouchRight','Crouch Walk Right',None),
 ('TurnLeft','Left Turn',None),('TurnRight','Right Turn',None)]
def frame_of(mats):
 up=(mats['Head'].translation-mats['Hips'].translation).normalized();right=(mats['RightArm'].translation-mats['LeftArm'].translation).normalized()
 f=right.cross(up).normalized();toes=mats['LeftToeBase'].translation-mats['LeftFoot'].translation
 if f.dot(toes)<0:f=-f
 # Preserve handedness even where Blender anatomical right points negative X.
 right=up.cross(f).normalized()
 return Matrix((right,f,up)).transposed().to_quaternion()
targetFrame=frame_of(rest)
def assign(a):
 rig.animation_data_create();rig.animation_data.action=a
 if a.slots:rig.animation_data.action_slot=a.slots[0]
def apply(pose):
 localposes={n:Matrix.LocRotScale(rig.matrix_world.inverted()@m.translation,rig.matrix_world.to_quaternion().inverted()@m.to_quaternion(),Vector((1,1,1))) for n,m in pose.items()}
 for b in bones:
  local=localposes[b.name]
  parent=localposes[b.parent.name] if b.parent else Matrix.Identity(4)
  basis=b.matrix_local.inverted()@(b.parent.matrix_local if b.parent else Matrix.Identity(4))@parent.inverted()@local
  rig.pose.bones[b.name].matrix_basis=basis
 bpy.context.view_layer.update()
 assert all(max(abs(s-1) for s in b.scale)<.0001 for b in rig.pose.bones),'Bone scale changed'
report={'status':'DERIVATIVE_REQUIRES_UNITY_AND_VISUAL_QA','sourceBlend':str(source),'sourceSha256':hashlib.sha256(source.read_bytes()).hexdigest(),'clips':[]}
for name,filename,trim in specs:
 existing=set(bpy.data.objects);path=R/'Art/PlaytestRecovery/MotionSources'/f'{filename}.fbx'
 bpy.ops.import_scene.fbx(filepath=str(path),automatic_bone_orientation=False)
 added=set(bpy.data.objects)-existing;src=next(o for o in added if o.type=='ARMATURE');action=src.animation_data.action
 sr={b.name.split(':')[-1]:(src.matrix_world@b.matrix_local).copy() for b in src.data.bones}
 by={b.name.split(':')[-1]:b for b in src.pose.bones};convert=targetFrame@frame_of(sr).inverted()
 scale=(rest['Hips'].translation.z-rest['LeftFoot'].translation.z)/(sr['Hips'].translation.z-sr['LeftFoot'].translation.z)
 start,end=map(float,action.frame_range)
 if trim:start+=trim[0]*60;end=min(end,start+(trim[1]-trim[0])*60)
 if name.startswith('Turn'):
  scene.frame_set(int(start));baseq=(src.matrix_world@by['Hips'].matrix).to_quaternion();best=end
  for f in range(int(start),int(end)+1):
   scene.frame_set(f);q=(src.matrix_world@by['Hips'].matrix).to_quaternion()@baseq.inverted()
   if abs(q.to_euler('XYZ').z)>=math.radians(90):best=f;break
  end=best
 scene.frame_set(int(start));first=(src.matrix_world@by['Hips'].matrix).translation.copy()
 print('SOURCE_FRAME',name,'target',list(rest['Hips'].translation),'sourceRest',list(sr['Hips'].translation),'sourcePose',list(first),'scale',scale,'object',list(src.scale),flush=True)
 scene.frame_set(int(end));last=(src.matrix_world@by['Hips'].matrix).translation.copy()
 frames=round(end-start)+1;poses=[]
 for i in range(frames):
  t=i/max(1,frames-1);f=start+(end-start)*t;scene.frame_set(math.floor(f),subframe=f%1)
  sm={n:(src.matrix_world@b.matrix).copy() for n,b in by.items()};pose={}
  # Locomotion trajectory remains metadata; the CharacterController owns translation.
  delta=convert@(sm['Hips'].translation-first.lerp(last,t))*scale
  hip=rest['Hips'].translation.copy()+Vector((delta.x,delta.y,0));hip.z+=(sm['Hips'].translation.z-sr['Hips'].translation.z)*scale
  yaw=Quaternion((0,0,1),-(sm['Hips'].to_quaternion()@sr['Hips'].to_quaternion().inverted()).to_euler('XYZ').z) if name.startswith('Turn') else Quaternion()
  for b in bones:
   n=b.name;sn=alias.get(n,n)
   if sn in sm:
    dq=convert@(sm[sn].to_quaternion()@sr[sn].to_quaternion().inverted())@convert.inverted()
    q=yaw@dq@rest[n].to_quaternion()
   else:q=pose[b.parent.name].to_quaternion()@rest[b.parent.name].to_quaternion().inverted()@rest[n].to_quaternion() if b.parent else rest[n].to_quaternion()
   head=hip if n=='Hips' else (pose[b.parent.name]@rest[b.parent.name].inverted())@rest[n].translation if b.parent else rest[n].translation.copy()
   pose[n]=Matrix.LocRotScale(head,q,Vector((1,1,1)))
  poses.append(pose)
 # The C02 proportions differ: place the lower stance envelope on its bind sole.
 # A constant correction preserves source vertical motion and fixed bone lengths.
 lows=[min(p['LeftFoot'].translation.z-rest['LeftFoot'].translation.z,p['RightFoot'].translation.z-rest['RightFoot'].translation.z) for p in poses]
 lift=-sorted(lows)[int((len(lows)-1)*.08)]
 assert abs(lift)<.3,(name,'unexpected unit/bind correction',lift)
 for p in poses:
  for m in p.values():m.translation.z+=lift
 a=bpy.data.actions.new('PT_'+name);a.use_fake_user=True;assign(a)
 for i,p in enumerate(poses,1):
  scene.frame_set(i);apply(p)
  for b in rig.pose.bones:
   b.rotation_mode='QUATERNION';b.keyframe_insert('location',frame=i);b.keyframe_insert('rotation_quaternion',frame=i);b.keyframe_insert('scale',frame=i)
 report['clips'].append({'name':name,'source':str(path),'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),'sourceFrames':[start,end],'duration':(frames-1)/60,'sourcePlanarTravel':list(last-first),'verticalCorrection':lift,'sourceUpperBodyPreserved':True})
 for o in added:bpy.data.objects.remove(o,do_unlink=True)
 if action.users==0:bpy.data.actions.remove(action)
 print('RETARGET',name,frames,lift,flush=True)
 # The stationary crouch holds a real source frame; it has no recovery or stealth effect.
 if name=='CrouchForward':
  idle=bpy.data.actions.new('PT_CrouchIdle');idle.use_fake_user=True;assign(idle)
  for f in [1,61]:
   scene.frame_set(f);apply(poses[0])
   for b in rig.pose.bones:
    b.rotation_mode='QUATERNION';b.keyframe_insert('location',frame=f);b.keyframe_insert('rotation_quaternion',frame=f);b.keyframe_insert('scale',frame=f)
assign(bpy.data.actions['PT_CrouchIdle']);scene.frame_set(1)
bpy.ops.wm.save_as_mainfile(filepath=str(O/'Player_C02_AttachedMotions.blend'))
rig.animation_data.action=None
for b in rig.pose.bones:b.matrix_basis.identity()
bpy.context.view_layer.update();bpy.ops.object.select_all(action='DESELECT');rig.select_set(True);bpy.context.view_layer.objects.active=rig
fbx=O/'Player_C02_AttachedMotions.fbx'
bpy.ops.export_scene.fbx(filepath=str(fbx),use_selection=True,object_types={'ARMATURE'},add_leaf_bones=False,bake_anim=True,bake_anim_use_all_actions=True,bake_anim_use_nla_strips=False,bake_anim_force_startend_keying=True,bake_anim_simplify_factor=0,axis_forward='-Z',axis_up='Y')
report['fbxSha256']=hashlib.sha256(fbx.read_bytes()).hexdigest();(O/'retarget_report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print('RETARGET_FINISHED',str(fbx),flush=True)
