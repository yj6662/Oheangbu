"""Custom non-human rig on the approved Meshy surface, with original UVs/materials preserved."""
import bpy,bmesh,math,json,hashlib
import numpy as np
from pathlib import Path
from mathutils import Vector,Matrix,Quaternion
ROOT=Path('C:/Users/yj666/Oheangbu');OUT=ROOT/'Art/Characters/Sinmok272/rig';ASSET=ROOT/'Oheangbu/Assets/_Project/Art/Characters/Sinmok273';ASSET.mkdir(parents=True,exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(OUT/'normalized.blend'));scene=bpy.context.scene;scene.render.fps=30
body=next(o for o in scene.objects if o.type=='MESH');body.name='Sinmok_GeneratedSurface'
data=bpy.data.armatures.new('SinmokSkeleton273');rig=bpy.data.objects.new('SinmokRig273',data);scene.collection.objects.link(rig)
bpy.ops.object.select_all(action='DESELECT');rig.select_set(True);bpy.context.view_layer.objects.active=rig;bpy.ops.object.mode_set(mode='EDIT');rows={}
def bone(n,h,t,parent=None):
 b=data.edit_bones.new(n);b.head=h;b.tail=t
 if parent:b.parent=data.edit_bones[parent]
 rows[n]={'head':h,'tail':t,'parent':parent}
bone('Root',(0,0,0),(0,0,.5))
bone('TrunkLower',(0,.8,1),(0,.4,4),'Root');bone('TrunkUpper',(0,.4,4),(0,-.3,8),'TrunkLower');bone('Crown',(0,-.3,8),(0,-1,12),'TrunkUpper')
bone('SupportUpper_R',(-2.3,-2.0,6.3),(-3.5,-3.8,3.4),'Root');bone('SupportForearm_R',(-3.5,-3.8,3.4),(-4.6,-4.5,.6),'SupportUpper_R');bone('SupportHand_R',(-4.6,-4.5,.6),(-5,-5.2,.15),'SupportForearm_R')
bone('AttackUpper_L',(2.4,-.1,5.8),(4.2,-.35,4.4),'TrunkUpper');bone('AttackForearm_L',(4.2,-.35,4.4),(5.5,-1.1,2.3),'AttackUpper_L');bone('AttackHand_L',(5.5,-1.1,2.3),(5.5,-1.6,1.6),'AttackForearm_L')
for side in [-1,1]:
 for k in range(3):
  bone(f'Bough_{side}_{k}',(side*1.5,k*1.2,7+k*.8),(side*(7+k),k*.7-1,8+k),'Crown')
for k in range(6):
 a=k*math.tau/6;bone('RootToe_'+str(k),(0,1,.8),(math.cos(a)*4,1+math.sin(a)*4,.2),'Root')
bpy.ops.object.mode_set(mode='OBJECT');rest={b.name:b.matrix_local.copy() for b in data.bones}
for p in rig.pose.bones:p.rotation_mode='QUATERNION'
points=np.array([v.co[:] for v in body.data.vertices]);names=list(rows);weights=np.zeros((len(points),len(names)))
def distance(n):
 a=np.array(rows[n]['head']);v=np.array(rows[n]['tail'])-a;t=np.clip(np.sum((points-a)*v,axis=1)/np.dot(v,v),0,1);return np.linalg.norm(points-a-t[:,None]*v,axis=1)
dists=np.stack([distance(n) for n in names],axis=1)
import heapq
key_ids={};weld=[];unique=[]
for p in points:
 key=tuple(np.round(p,4))
 if key not in key_ids:key_ids[key]=len(unique);unique.append(p)
 weld.append(key_ids[key])
unique=np.array(unique);adj=[{} for _ in unique]
for edge in body.data.edges:
 a,b=(weld[i] for i in edge.vertices)
 if a!=b:
  length=float(np.linalg.norm(unique[a]-unique[b]));adj[a][b]=length;adj[b][a]=length
geo=np.full((len(unique),len(names)),np.inf)
for j,n in enumerate(names):
 h=np.array(rows[n]['head']);t=np.array(rows[n]['tail']);queue=[]
 for u in [.2,.5,.8]:
  seed=h*(1-u)+t*u;idx=int(np.argmin(np.sum((unique-seed)**2,axis=1)));geo[idx,j]=0;heapq.heappush(queue,(0,idx))
 while queue:
  dist,i=heapq.heappop(queue)
  if dist>geo[i,j]:continue
  for nb,cost in adj[i].items():
   nd=dist+cost
   if nd<geo[nb,j]:geo[nb,j]=nd;heapq.heappush(queue,(nd,nb))
 print('GEODESIC_BIND',n,flush=True)
geodists=geo[np.array(weld)]
# Anatomical masks avoid cross-binding fingers, rear boughs and nearby trunk surfaces.
for i,p in enumerate(points):
 x,y,z=p
 # Continuous distance envelopes across every joint: hard coordinate masks tore
 # triangles spanning the generated arm/trunk and hanging foliage junctions.
 ds=geodists[i]
 if not np.isfinite(ds).any():ds=dists[i]
 ids=np.argsort(ds)[:4]
 w=1/(1.0+ds[ids])**4;w/=sum(w);weights[i,ids]=w
 # The generated fingers approach the foot roots. Keep the sternum and grounded
 # roots out of the free-arm chain even where the remesher fused their surfaces.
 free=max(0,min(1,(x-1.4)/1.6))*max(0,min(1,(z-.8)/1.1))
 for n in ('AttackUpper_L','AttackForearm_L','AttackHand_L'):weights[i,names.index(n)]*=free
 weights[i]/=max(1e-12,weights[i].sum())
 # All vertices below the support wrist keep exactly the same grounded hand transform.
 if x<-2 and y<-2 and z<1.6:
  blend=max(0,min(1,(1.6-z)/.7));weights[i,:]*=1-blend;weights[i,names.index('SupportHand_R')]+=blend
for j,n in enumerate(names):
 group=body.vertex_groups.new(name=n)
 for i in np.flatnonzero(weights[:,j]>1e-6):group.add([int(i)],float(weights[i,j]),'REPLACE')
mod=body.modifiers.new('AuthoredTreeSkin','ARMATURE');mod.object=rig;body.parent=rig;body.matrix_parent_inverse=Matrix.Identity(4)
# Split along actual surface faces while copying identical seam weights, never regenerate geometry.
categories=[]
for p in body.data.polygons:
 x,y,z=p.center
 categories.append(1 if x<-2 and y<-2 and z<6.5 else 2 if x>2.6 and y<1.6 and 1.65<z<6.1 else 3 if z>7 else 0)
parts=[]
for k,label in enumerate(['TrunkAndRoots','PlantedRightArm','AttackingLeftArm','CrownAndBoughs']):
 obj=body.copy();obj.data=body.data.copy();obj.name=label;scene.collection.objects.link(obj)
 bm=bmesh.new();bm.from_mesh(obj.data);bm.faces.ensure_lookup_table();bmesh.ops.delete(bm,geom=[f for f in bm.faces if categories[f.index]!=k],context='FACES');bm.to_mesh(obj.data);bm.free();parts.append(obj)
bpy.data.objects.remove(body,do_unlink=True)
def assign(action):
 rig.animation_data_create();rig.animation_data.action=action
 if action.slots:rig.animation_data.action_slot=action.slots[0]
def smooth(v):v=max(0,min(1,v));return v*v*(3-2*v)
def rotate(n,axis,deg):rig.pose.bones[n].rotation_quaternion=Quaternion(rest[n].to_quaternion().inverted()@Vector(axis),math.radians(deg))
def segment(n,start,end):
 a=Vector(rows[n]['head']);b=Vector(rows[n]['tail']);q=(b-a).rotation_difference(end-start)@rest[n].to_quaternion();rig.pose.bones[n].matrix=Matrix.Translation(start)@q.to_matrix().to_4x4()
def solve_left(target):
 upper='AttackUpper_L';lower='AttackForearm_L';parent=rig.pose.bones['TrunkUpper'].matrix@rest['TrunkUpper'].inverted();h=parent@Vector(rows[upper]['head']);k=Vector(rows[upper]['tail']);f=Vector(rows[lower]['tail']);rh=Vector(rows[upper]['head']);a=(k-rh).length;b=(f-k).length
 axis=target-h;raw=axis.length;axis.normalize();dist=max(abs(a-b)+1e-4,min(a+b-1e-4,raw));target=h+axis*dist
 rest_axis=(f-rh).normalized();bend=k-rh-rest_axis*(k-rh).dot(rest_axis);bend=rest_axis.rotation_difference(axis)@bend;bend-=axis*bend.dot(axis);bend.normalize();along=(a*a-b*b+dist*dist)/(2*dist);knee=h+axis*along+bend*math.sqrt(max(0,a*a-along*along))
 segment(upper,h,knee);bpy.context.view_layer.update();segment(lower,knee,target);bpy.context.view_layer.update()
 return max(0,raw-(a+b))
actions=[];samples={};base=Vector(rows['AttackForearm_L']['tail'])
for name,duration in [('Idle',4),('LeftSlam',2),('LeftSweep',2),('RootPulse',2),('BoughCast',2),('SupportRelease',2.8)]:
 action=bpy.data.actions.new('Sinmok_'+name);action.use_fake_user=True;assign(action);stats=[];last={}
 for i in range(round(duration*30)+1):
  u=i/(duration*30)
  for p in rig.pose.bones:p.matrix_basis=Matrix.Identity(4)
  if name=='Idle':
   rotate('Crown',(0,1,0),.45*math.sin(u*math.tau))
   for k in range(3):rotate(f'Bough_1_{k}',(0,1,0),.5*math.sin(u*math.tau+k))
  elif name in ('LeftSlam','LeftSweep'):
   coil=Vector((4.8,-.8,7.4)) if name=='LeftSlam' else Vector((6,.8,4.4))
   contact=Vector((3.8,-3.8,1.8)) if name=='LeftSlam' else Vector((2,-4.0,3.2))
   follow=Vector((3.6,-3.5,1.7)) if name=='LeftSlam' else Vector((1.5,-3.4,3.0))
   target=base.lerp(coil,smooth(u/.5)) if u<.5 else coil.lerp(contact,smooth((u-.5)/.15)) if u<.65 else contact.lerp(follow,smooth((u-.65)/.2)) if u<.85 else follow.lerp(base,smooth((u-.85)/.15))
   # Full extension comes from the elbow chain; only a small trunk lean is added.
   rotate('TrunkUpper',(1,0,0),6*math.sin(math.pi*u)**2);bpy.context.view_layer.update();solve_left(target)
  elif name=='RootPulse':
   pulse=math.sin(math.pi*max(0,min(1,(u-.45)/.45)))
   for k in range(6):rotate('RootToe_'+str(k),(0,1,0),6*pulse*((-1)**k))
   rotate('TrunkUpper',(1,0,0),-2*math.sin(math.pi*u))
  elif name=='BoughCast':
   pulse=math.sin(math.pi*u)**2
   rotate('Crown',(1,0,0),5*pulse)
   for s in [-1,1]:
    for k in range(3):rotate(f'Bough_{s}_{k}',(0,1,0),s*(4+k)*pulse)
  elif name=='SupportRelease':
   pulse=math.sin(math.pi*u)**2;rotate('SupportUpper_R',(1,0,0),-18*pulse);rotate('SupportForearm_R',(1,0,0),-9*pulse)
  for p in rig.pose.bones:
   q=p.rotation_quaternion.copy()
   if p.name in last and q.dot(last[p.name])<0:q.negate();p.rotation_quaternion=q
   last[p.name]=q
   for channel in ('location','rotation_quaternion','scale'):p.keyframe_insert(channel,frame=i+1,group=p.name)
  bpy.context.view_layer.update();stats.append({'u':u,'supportWrist':list(rig.pose.bones['SupportHand_R'].head),'attackWrist':list(rig.pose.bones['AttackHand_L'].head)})
 for layer in action.layers:
  for strip in layer.strips:
   for bag in strip.channelbags:
    for fc in bag.fcurves:
     for key in fc.keyframe_points:key.interpolation='LINEAR'
 samples[name]=stats;actions.append({'name':action.name,'seconds':duration})
assign(bpy.data.actions['Sinmok_Idle']);scene.frame_set(1)
# Copy source PBR maps once into a versioned dedicated material folder.
import shutil
for src in (ROOT/'Art/Characters/Sinmok272/sinmok/model').glob('texture_urls_0_*.png'):shutil.copy2(src,ASSET/src.name)
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'Sinmok273.blend'))
bpy.ops.object.select_all(action='DESELECT');rig.select_set(True)
for o in parts:o.select_set(True)
bpy.context.view_layer.objects.active=rig
bpy.ops.export_scene.fbx(filepath=str(ASSET/'Sinmok273.fbx'),use_selection=True,object_types={'MESH','ARMATURE'},add_leaf_bones=False,bake_anim=True,bake_anim_use_all_actions=True,bake_anim_use_nla_strips=False,bake_anim_step=1,bake_anim_simplify_factor=0,axis_forward='-Z',axis_up='Y',path_mode='AUTO')
support=max((Vector(s['supportWrist'])-Vector(samples['Idle'][0]['supportWrist'])).length for n,ss in samples.items() if n!='SupportRelease' for s in ss)
assert support<1e-5
(OUT/'rig-report.json').write_text(json.dumps({'bones':rows,'parts':[{'name':o.name,'vertices':len(o.data.vertices),'triangles':sum(len(p.vertices)-2 for p in o.data.polygons)}for o in parts],'actions':actions,'maxSupportWristDriftM':support,'maxWeightSumError':float(np.max(abs(weights.sum(axis=1)-1))),'generatedSourceSha256':hashlib.sha256((ROOT/'Art/Characters/Sinmok272/sinmok/model/model_urls_glb.glb').read_bytes()).hexdigest(),'samples':samples},indent=2))
print('SINMOK273_EXPORTED',len(rows),'bones, support drift',support)
