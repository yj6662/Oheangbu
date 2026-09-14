"""Official Mixamo locomotion, C02 bone lengths and bind preserved.
Leg directions are reconstructed from animated source joint vectors, not just
rest-relative rotation deltas (which retained the C02 bind-pose abduction).
"""
from pathlib import Path
s=Path(__file__).with_name('recovery_retarget.py').read_text()
exec(compile(s[:s.index("report={'status'")],__file__,'exec'))
O=R/'Art/PlaytestRecovery/NaturalLocomotion/Motion';O.mkdir(parents=True,exist_ok=True)
specs=[('WalkForward','walking'),('WalkBack','Walking Backwards Standard'),('WalkLeft','left strafe walking'),('WalkRight','right strafe walking'),
       ('RunForward','running'),('RunBack','Running Backward'),('RunLeft','left strafe'),('RunRight','right strafe'),
       ('JumpFull','jump'),('IdleStill','idle')]
report={'sourceBlend':str(source),'method':'C02 unchanged hierarchy/bone lengths; source animated leg and foot joint directions; root trajectory removed; fixed phase support alignment','clips':[]}
stored={}
def write_action(name,poses):
 a=bpy.data.actions.new('PT_'+name);a.use_fake_user=True;assign(a)
 for i,p in enumerate(poses,1):
  scene.frame_set(i);apply(p)
  for b in rig.pose.bones:
   b.rotation_mode='QUATERNION';b.keyframe_insert('location',frame=i);b.keyframe_insert('rotation_quaternion',frame=i);b.keyframe_insert('scale',frame=i)
 return a
for name,filename in specs:
 path=R/'Art/PlaytestRecovery/NaturalLocomotion/Sources'/f'{filename}.fbx'
 existing=set(bpy.data.objects);bpy.ops.import_scene.fbx(filepath=str(path),automatic_bone_orientation=False)
 added=set(bpy.data.objects)-existing;src=next(o for o in added if o.type=='ARMATURE');action=src.animation_data.action
 sr={b.name.split(':')[-1]:src.matrix_world@b.matrix_local for b in src.data.bones};by={b.name.split(':')[-1]:b for b in src.pose.bones}
 convert=targetFrame@frame_of(sr).inverted();scale=(rest['Hips'].translation.z-rest['LeftFoot'].translation.z)/(sr['Hips'].translation.z-sr['LeftFoot'].translation.z)
 start,end=map(float,action.frame_range)
 if name=='IdleStill':end=start+1
 scene.frame_set(int(start));first=(src.matrix_world@by['Hips'].matrix).translation.copy()
 scene.frame_set(int(end));last=(src.matrix_world@by['Hips'].matrix).translation.copy()
 poses=[];rows=[];frames=round(end-start)+1
 for i in range(frames):
  t=i/max(1,frames-1);f=start+i;scene.frame_set(math.floor(f),subframe=f%1)
  sm={n:src.matrix_world@b.matrix for n,b in by.items()};pose={}
  delta=convert@(sm['Hips'].translation-first.lerp(last,t))*scale
  hip=rest['Hips'].translation+Vector((delta.x,delta.y,(sm['Hips'].translation.z-sr['Hips'].translation.z)*scale))
  # Remove the source aerial root arc. PlayerMotor alone supplies world height.
  # Keep the source knee tuck and landing compression, with no bone stretching.
  if name=='JumpFull' and 46<=f<=80:
   u=(f-46)/34;hip.z-=.275*math.sin(math.pi*u)*scale
  for b in bones:
   n=b.name;sn=alias.get(n,n)
   q=(convert@(sm[sn].to_quaternion()@sr[sn].to_quaternion().inverted())@convert.inverted())@rest[n].to_quaternion() if sn in sm else pose[b.parent.name].to_quaternion()@rest[b.parent.name].to_quaternion().inverted()@rest[n].to_quaternion() if b.parent else rest[n].to_quaternion()
   child=n.replace('UpLeg','Leg') if n.endswith('UpLeg') else n.replace('Leg','Foot') if n.endswith('Leg') else n.replace('Foot','ToeBase') if n.endswith('Foot') else None
   if child and child in sm:
    old=q@rest[n].to_quaternion().inverted()@(rest[child].translation-rest[n].translation)
    goal=convert@(sm[child].translation-sm[sn].translation)
    q=old.rotation_difference(goal)@q
   head=hip if n=='Hips' else pose[b.parent.name]@rest[b.parent.name].inverted()@rest[n].translation if b.parent else rest[n].translation.copy()
   pose[n]=Matrix.LocRotScale(head,q,Vector((1,1,1)))
  poses.append(pose)
  rows.append(abs(pose['LeftFoot'].translation.x-pose['RightFoot'].translation.x))
 lows=[min(p['LeftFoot'].translation.z-rest['LeftFoot'].translation.z,p['RightFoot'].translation.z-rest['RightFoot'].translation.z) for p in poses]
 lift=-sorted(lows)[int((len(lows)-1)*.08)]
 assert abs(lift)<.3,(name,lift)
 for p in poses:
  for m in p.values():m.translation.z+=lift
 phase=0
 if name.startswith(('Walk','Run')):
  # A common left-foot contact phase prevents a diagonal blend mixing opposite steps.
  direction=Vector((1,0,0)) if name.endswith('Left') else Vector((-1,0,0)) if name.endswith('Right') else Vector((0,1,0)) if name.endswith('Back') else Vector((0,-1,0))
  unique=poses[:-1];phase=max(range(len(unique)),key=lambda j:unique[j]['LeftFoot'].translation.dot(direction))
  poses=unique[phase:]+unique[:phase];poses.append({n:m.copy() for n,m in poses[0].items()})
 if name=='IdleStill':poses=[{n:m.copy() for n,m in poses[0].items()} for _ in range(61)]
 stored[name]=poses
 if name!='JumpFull':write_action(name,poses)
 report['clips'].append({'name':name,'source':str(path),'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),'sourceFrames':[start,end],'duration':(frames-1)/60,'sourcePlanarTravel':list(last-first),'verticalCorrection':lift,'cycleStartSourceFrame':phase+1,'ankleWidthMedian':statistics.median(rows),'unchangedBoneLengths':True})
 for obj in added:bpy.data.objects.remove(obj,do_unlink=True)
 if action.users==0:bpy.data.actions.remove(action)
 print('NATURAL',name,frames,lift,phase,flush=True)
# Frame numbers are from the retained source jump, which includes a long anticipation.
# Space remains immediate: use takeoff->apex, apex->contact, contact->recovery.
for name,lo,hi in [('JumpRise',46,64),('JumpFall',64,80),('Land',80,120)]:
 write_action(name,stored['JumpFull'][lo-1:hi])
 report['clips'].append({'name':name,'source':'jump.fbx','sourceFrames':[lo,hi],'duration':(hi-lo)/60,'sourceArcRemoved':True})
write_action('StartForward',stored['WalkForward'][:13])
assign(bpy.data.actions['PT_IdleStill']);scene.frame_set(1)
bpy.ops.wm.save_as_mainfile(filepath=str(O/'Player_C02_NaturalLocomotion.blend'))
rig.animation_data.action=None
for b in rig.pose.bones:b.matrix_basis.identity()
bpy.context.view_layer.update();bpy.ops.object.select_all(action='DESELECT');rig.select_set(True);bpy.context.view_layer.objects.active=rig
fbx=O/'Player_C02_NaturalLocomotion.fbx'
bpy.ops.export_scene.fbx(filepath=str(fbx),use_selection=True,object_types={'ARMATURE'},add_leaf_bones=False,bake_anim=True,bake_anim_use_all_actions=True,bake_anim_use_nla_strips=False,bake_anim_force_startend_keying=True,bake_anim_simplify_factor=0,axis_forward='-Z',axis_up='Y')
report['fbxSha256']=hashlib.sha256(fbx.read_bytes()).hexdigest();(O/'retarget_report.json').write_text(json.dumps(report,indent=2))
