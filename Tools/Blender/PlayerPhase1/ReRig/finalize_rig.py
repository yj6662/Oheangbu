import bpy,math,json
from pathlib import Path
from mathutils import Vector,Quaternion
OUT=Path('C:/Users/yj666/Oheangbu/Art/PlayerPhase1/ReRig')
s=bpy.context.scene;a=bpy.data.objects['Dosa_Phase1_Rig']
def reset():
 a.animation_data.action=None
 for p in a.pose.bones:p.rotation_mode='QUATERNION';p.rotation_quaternion=(1,0,0,0);p.location=(0,0,0);p.scale=(1,1,1)
def fingers(amount,frame):
 for side,sign in [('Left',1),('Right',-1)]:
  for digit in ['Thumb','Index','Middle','Ring','Little']:
   for j,angle in enumerate([.55,.85,.55],1):
    p=a.pose.bones[side+'Hand'+digit+str(j)];inv=p.bone.matrix_local.to_quaternion().inverted()
    curl=Quaternion(inv@Vector((0,1,0)),sign*angle*amount)
    if digit=='Thumb':
     curl=Quaternion(inv@Vector((0,1,0)),sign*angle*amount*.6)
     if j==1:curl=Quaternion(inv@Vector((0,0,1)),sign*.45*amount)@curl
    p.rotation_quaternion=curl;p.keyframe_insert('rotation_quaternion',frame=frame)
for label in ['Walk','Run','ArmsUp','Squat','BrushSwing']:
 reset();act=bpy.data.actions['DIAG_'+label];a.animation_data.action=act;end=int(act.frame_range[1])
 for f in range(1,end+1):
  s.frame_set(f);amount=.23 if label!='BrushSwing' else .35+.4*math.sin(math.pi*(f-1)/max(1,end-1))
  fingers(amount,f)
reset()
old=bpy.data.actions.get('DIAG_HandFlex')
if old:bpy.data.actions.remove(old)
act=bpy.data.actions.new('DIAG_HandFlex');a.animation_data.action=act
for f in range(1,49):
 s.frame_set(f)
 for p in a.pose.bones:
  p.rotation_quaternion=(1,0,0,0);p.location=(0,0,0);p.scale=(1,1,1)
  p.keyframe_insert('rotation_quaternion',frame=f);p.keyframe_insert('location',frame=f);p.keyframe_insert('scale',frame=f)
 fingers(math.sin(math.pi*(f-1)/47),f)
act.use_fake_user=True
reset();a.show_in_front=True;a.data.display_type='OCTAHEDRAL'
for label,match in [('Hands',lambda n:'Hand' in n),('Robe',lambda n:n.startswith('Cloth_')),('Body',lambda n:'Hand' not in n and not n.startswith('Cloth_'))]:
 c=a.data.collections.get(label) or a.data.collections.new(label)
 for b in a.data.bones:
  if match(b.name):c.assign(b)
a['revision']='Phase1 ReRig: independent digit landmarks; barycentric cloth transfer; 12 x 4 robe chains'
a['cloth_response']='Diagnostic pose-aware capsule solve baked to the five test actions. Runtime cloth is not implemented.'
a['rig_status']='VALIDATION_PENDING'
for im in bpy.data.images:
 if im.source=='FILE' and im.has_data and not im.packed_file:
  try:im.pack()
  except Exception:pass
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'Dosa_Phase1_Rerig.blend'),compress=True)
print('Finger flexion axes and diagnostic action saved')
