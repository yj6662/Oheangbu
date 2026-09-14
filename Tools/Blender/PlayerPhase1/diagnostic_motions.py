import bpy,math,json
from mathutils import Quaternion,Vector
from pathlib import Path
OUT=Path('C:/Users/yj666/Oheangbu/Art/PlayerPhase1');s=bpy.context.scene;a=bpy.data.objects['Dosa_Phase1_Rig'];s.render.fps=24
before=set(bpy.data.objects);bpy.ops.import_scene.gltf(filepath=str(OUT/'Source/Meshy/BodyRig/running_armature_glb.glb'))
run=next(o for o in set(bpy.data.objects)-before if o.type=='ARMATURE');run.name='Source_RunRig'
for o in set(bpy.data.objects)-before:
 if o.type=='MESH':o.hide_render=True
actions=[]
def reset():
 for p in a.pose.bones:p.rotation_mode='QUATERNION';p.rotation_quaternion=(1,0,0,0);p.location=(0,0,0);p.scale=(1,1,1)
def key(f):
 for p in a.pose.bones:
  p.keyframe_insert('rotation_quaternion',frame=f);p.keyframe_insert('location',frame=f)
def globalrot(name,axis,deg):
 p=a.pose.bones[name];q=p.bone.matrix_local.to_quaternion();p.rotation_quaternion=q.inverted()@Quaternion(axis,math.radians(deg))@q
for label,source in [('Walk',bpy.data.objects['Source_WalkRig']),('Run',run)]:
 act=bpy.data.actions.new('DIAG_'+label);act.use_fake_user=True;a.animation_data_create();a.animation_data.action=act
 end=int(math.ceil(source.animation_data.action.frame_range[1]))
 for f in range(1,end+1):
  s.frame_set(f);reset()
  for sp in source.pose.bones:
   if sp.name in a.pose.bones:
    p=a.pose.bones[sp.name];p.rotation_quaternion=sp.rotation_quaternion.copy();p.location=sp.location*.01
  # Small diagnostic hem response; no claim of simulated cloth.
  for p in a.pose.bones:
   if p.name.startswith('Hem_'):p.rotation_quaternion=Quaternion((1,0,0),math.radians(5)*math.sin(2*math.pi*f/end))
  key(f)
 actions.append(dict(name=act.name,frames=end,source='Meshy BodyRig included basic animation; diagnostic retarget'))
for label in ['ArmsUp','Squat','BrushSwing']:
 act=bpy.data.actions.new('DIAG_'+label);act.use_fake_user=True;a.animation_data.action=act
 for f in range(1,49):
  reset();t=(1-math.cos(2*math.pi*(f-1)/48))/2
  if label=='ArmsUp':
   globalrot('LeftArm',(0,1,0),-65*t);globalrot('RightArm',(0,1,0),65*t)
  elif label=='Squat':
   for side in ['Left','Right']:
    globalrot(side+'Arm',(0,1,0),(35 if side=='Left' else -35))
    globalrot(side+'UpLeg',(1,0,0),-75*t);globalrot(side+'Leg',(1,0,0),115*t);globalrot(side+'Foot',(1,0,0),-40*t)
   a.pose.bones['Hips'].location=a.data.bones['Hips'].matrix_local.to_3x3().inverted()@Vector((0,0,-.38*t))
   globalrot('Spine02',(1,0,0),22*t)
   for p in a.pose.bones:
    if p.name.startswith('Hem_Front'):p.rotation_quaternion=Quaternion((1,0,0),-.55*t)
  else:
   globalrot('RightArm',(0,1,0),-35+65*t);globalrot('RightForeArm',(0,0,1),75*t)
   globalrot('LeftArm',(0,1,0),55);globalrot('Spine',(0,0,1),-20+40*t)
   for p in a.pose.bones:
    if p.name.startswith('RightHand') and p.name!='RightHand':p.rotation_quaternion=Quaternion((1,0,0),-.55)
  key(f)
 actions.append(dict(name=act.name,frames=48,source='Blender authored diagnostic only'))
# Neutral and five representative motion frames.
c=s.camera;c.location=(2.4,-4.5,2.1);c.rotation_euler=(Vector((0,0,.87))-c.location).to_track_quat('-Z','Y').to_euler();c.data.ortho_scale=3.6
(OUT/'diagnostic_actions.json').write_text(json.dumps(actions,indent=2))
a.animation_data.action=bpy.data.actions['DIAG_Walk'];s.frame_set(7)
s.render.filepath=str(OUT/'Previews/Walk_initial.png');bpy.ops.render.render(write_still=True)
print(json.dumps(actions))
