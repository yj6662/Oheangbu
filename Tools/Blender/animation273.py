"""Versioned motion derivatives of the five approved rigs. Blender background only."""
import bpy, math, json, hashlib
from pathlib import Path
from mathutils import Vector, Quaternion, Matrix
ROOT=Path('C:/Users/yj666/Oheangbu')
OUT=ROOT/'Art/Characters/Animation273';OUT.mkdir(parents=True,exist_ok=True)
DEST=ROOT/'Oheangbu/Assets/_Project/Art/Characters/Animation273';DEST.mkdir(parents=True,exist_ok=True)
families=['WoodDeer','FireHaetae','MetalTiger','DokkaebiClub','WaterTurtle']
report=[]
def assign(rig,action):
 rig.animation_data_create();rig.animation_data.action=action
 if action.slots:rig.animation_data.action_slot=action.slots[0]
def linear(action):
 for layer in action.layers:
  for strip in layer.strips:
   for bag in strip.channelbags:
    for fc in bag.fcurves:
     for k in fc.keyframe_points:k.interpolation='LINEAR'
def smooth(v):v=max(0,min(1,v));return v*v*(3-2*v)
for family in families:
 src=ROOT/f'Art/Demo/Summons/{family}/{family}_Combat.blend';sha=hashlib.sha256(src.read_bytes()).hexdigest()
 bpy.ops.wm.open_mainfile(filepath=str(src));scene=bpy.context.scene;scene.render.fps=30
 rig=next(o for o in scene.objects if o.type=='ARMATURE');mesh=[o for o in scene.objects if o.type=='MESH' and any(m.type=='ARMATURE' and m.object==rig for m in o.modifiers)]
 originals=list(bpy.data.actions);idle=next(a for a in originals if 'Idle' in a.name)
 attack=next(a for a in originals if any(n in a.name for n in ['HornAttack','FlameAttack','ClawLeft','Swing','WaterCast']))
 assign(rig,idle);scene.frame_set(1);base={p.name:p.matrix_basis.copy() for p in rig.pose.bones};rest={b.name:b.matrix_local.to_quaternion() for b in rig.data.bones}
 def rotate(name,axis,degrees):
  if name not in rig.pose.bones:return
  p=rig.pose.bones[name];p.rotation_mode='QUATERNION';p.rotation_quaternion=Quaternion(rest[name].inverted()@Vector(axis),math.radians(degrees))@p.rotation_quaternion
 actions=[]
 for kind,duration in [('Form',1.2),('Dissolve',1.0),('Hit',.55),('Stun',1.5),('Death',1.5),('EnemyAttack',2.0)]:
  poses=[];end=round(duration*30)
  # The existing family attack is retimed, preserving its real IK contacts and hand/paw shapes.
  if kind=='EnemyAttack':
   contact={'WoodDeer':.5,'FireHaetae':.4,'MetalTiger':.375,'DokkaebiClub':.53,'WaterTurtle':.4}[family]
   assign(rig,attack);a,b=attack.frame_range
   for i in range(end+1):
    u=i/end;v=(u/.5*contact) if u<.5 else contact+(u-.5)/.5*(1-contact)
    frame=a+(b-a)*v;scene.frame_set(int(frame),subframe=frame%1)
    poses.append({p.name:p.matrix_basis.copy() for p in rig.pose.bones})
  action=bpy.data.actions.new('A273_'+kind);action.use_fake_user=True;assign(rig,action)
  for i in range(end+1):
   u=i/end
   for p in rig.pose.bones:p.matrix_basis=(poses[i] if poses else base)[p.name].copy();p.rotation_mode='QUATERNION'
   if kind in ('Form','Dissolve'):
    amount=1-smooth(u) if kind=='Form' else smooth(u)
    rotate('Neck',(1,0,0),-13*amount);rotate('Head',(1,0,0),-11*amount)
    rotate('Head',(0,0,1),4*amount);rotate('Tail',(0,0,1),-9*amount)
   elif kind in ('Hit','Stun'):
    a=math.sin(math.pi*u)**1.1 if kind=='Hit' else .6+.06*math.sin(math.tau*u)
    rotate('Neck',(1,0,0),10*a);rotate('Head',(0,0,1),-12*a);rotate('Head',(1,0,0),7*a)
   elif kind=='Death':
    a=smooth(u);rotate('Root',(0,1,0),70*a);rotate('Neck',(1,0,0),-18*a);rotate('Tail',(0,0,1),15*a)
    bpy.context.view_layer.update();dep=bpy.context.evaluated_depsgraph_get();low=float('inf')
    for o in mesh:
     ev=o.evaluated_get(dep);me=ev.to_mesh();low=min(low,min((ev.matrix_world@v.co).z for v in me.vertices));ev.to_mesh_clear()
    # Grounded collapse, never translate the gameplay actor.
    rig.pose.bones['Root'].location+=rest['Root'].inverted()@Vector((0,0,-low))
   for p in rig.pose.bones:
    for channel in ('location','rotation_quaternion','scale'):p.keyframe_insert(channel,frame=i+1,group=p.name)
  linear(action);actions.append({'name':action.name,'seconds':duration,'loop':kind=='Stun'})
 assign(rig,bpy.data.actions['A273_Form']);scene.frame_set(37)
 for a in originals:bpy.data.actions.remove(a)
 for o in list(bpy.data.objects):
  if o!=rig and o not in mesh:bpy.data.objects.remove(o,do_unlink=True)
 bpy.ops.wm.save_as_mainfile(filepath=str(OUT/f'{family}_273.blend'))
 bpy.ops.object.select_all(action='DESELECT');rig.select_set(True)
 for o in mesh:o.select_set(True)
 bpy.context.view_layer.objects.active=rig
 bpy.ops.export_scene.fbx(filepath=str(DEST/f'{family}_273.fbx'),use_selection=True,object_types={'MESH','ARMATURE'},apply_unit_scale=True,apply_scale_options='FBX_SCALE_NONE' if family=='WaterTurtle' else 'FBX_SCALE_UNITS',add_leaf_bones=False,bake_anim=True,bake_anim_use_all_actions=True,bake_anim_use_nla_strips=False,bake_anim_step=1,bake_anim_simplify_factor=0,axis_forward='-Z',axis_up='Y',path_mode='AUTO')
 assert hashlib.sha256(src.read_bytes()).hexdigest()==sha
 report.append({'family':family,'source':str(src),'sourceSha256':sha,'bones':list(rig.pose.bones.keys()),'actions':actions,'preservedSource':True})
 (OUT/'motion-manifest.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
 print('ANIMATION273_EXPORTED',family,flush=True)
