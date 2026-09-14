import bpy,json,math
from pathlib import Path
from mathutils import Vector,Matrix,Quaternion
OUT=Path('C:/Users/yj666/Oheangbu/Art/PlayerPhase1/ReRig');s=bpy.context.scene;a=bpy.data.objects['Dosa_Phase1_Rig'];data=json.loads((OUT/'rig_definition.json').read_text());N=data['cloth_panels']
rest={int(i):[Vector(p) for p in ps] for i,ps in data['cloth_rest'].items()}
def closest(p,u,v):
 t=max(0,min(1,(p-u).dot(v-u)/max(1e-12,(v-u).length_squared)));return u+(v-u)*t
def capsules():
 caps=[]
 for side in ['Left','Right']:
  hip=a.pose.bones[side+'UpLeg'].head.copy();knee=a.pose.bones[side+'Leg'].head.copy();ankle=a.pose.bones[side+'Foot'].head.copy()
  # Clearance includes the gap between a chain and the inner folds it skins.
  caps.append((hip,knee,.145));caps.append((knee,ankle,.135))
  toe=a.pose.bones[side+'ToeBase'].head.copy()
  caps.append((ankle,toe,.105))
 return caps
def collide(p,caps,fallback):
 for u,v,r in caps:
  c=closest(p,u,v);d=p-c
  if d.length<r:p=c+(d.normalized() if d.length>.00001 else fallback)*r
 return p
def solve():
 h=a.pose.bones['Hips'].matrix@a.data.bones['Hips'].matrix_local.inverted();caps=capsules();nodes={}
 for i,rs in rest.items():
  ps=[h@p for p in rs];root=ps[0].copy();lengths=[(rs[j+1]-rs[j]).length for j in range(4)];fallback=Vector((math.sin(2*math.pi*i/N),-math.cos(2*math.pi*i/N),0))
  # Seed drape over the thigh, preventing front panels from taking a path
  # through the gap between the two legs during deep flexion.
  pitches=[]
  for side in ['Left','Right']:
   direction=a.pose.bones[side+'Leg'].head-a.pose.bones[side+'UpLeg'].head
   pitches.append(math.atan2(-direction.y,-direction.z))
  front=math.cos(2*math.pi*i/N)
  tilt=-max(0,max(pitches))*(.85+.15*max(0,front)) if front>-.15 else max(0,-min(pitches))*.7
  rot=Quaternion((1,0,0),tilt)
  for j in range(1,5):ps[j]=root+rot@(ps[j]-root)
  for it in range(100):
   for j in range(1,5):ps[j]=collide(ps[j],caps,fallback)
   # Sample each segment to keep its middle from cutting through a thigh.
   for j in range(4):
    for t in [.25,.5,.75]:
     q=ps[j].lerp(ps[j+1],t);d=collide(q,caps,fallback)-q
     if j>0:ps[j]+=d*(1-t)
     ps[j+1]+=d*t
   ps[0]=root
   for j in range(4):
    delta=ps[j+1]-ps[j];dist=delta.length
    if dist<1e-8:continue
    err=delta*(1-lengths[j]/dist)
    if j==0:ps[j+1]-=err
    else:ps[j]+=.5*err;ps[j+1]-=.5*err
  nodes[i]=ps
 # Solve pose matrices in parent order. No bone scaling or arm stretching.
 for i,ps in nodes.items():
  for j in range(4):
   p=a.pose.bones[f'Cloth_{i:02d}_{j}'];b=p.bone
   q=(b.tail_local-b.head_local).normalized().rotation_difference((ps[j+1]-ps[j]).normalized())@b.matrix_local.to_quaternion()
   p.matrix=Matrix.LocRotScale(ps[j],q,Vector((1,1,1)))
   bpy.context.view_layer.update()
 return nodes
for label in ['Walk','Run','ArmsUp','Squat','BrushSwing']:
 act=bpy.data.actions['DIAG_'+label];a.animation_data.action=act;end=int(act.frame_range[1])
 for f in range(1,end+1):
  s.frame_set(f);bpy.context.view_layer.update();solve()
  for p in a.pose.bones:
   if p.name.startswith('Cloth_'):
    p.keyframe_insert('location',frame=f);p.keyframe_insert('rotation_quaternion',frame=f);p.keyframe_insert('scale',frame=f)
 print(label,'cloth response baked',flush=True)
a.animation_data.action=None
for p in a.pose.bones:p.rotation_quaternion=(1,0,0,0);p.location=(0,0,0);p.scale=(1,1,1)
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'Dosa_Phase1_Rerig.blend'),compress=True)
