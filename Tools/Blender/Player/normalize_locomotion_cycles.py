"""Make each locomotion asset one anatomical stride with aligned left/right support."""
import bpy,math,json,os,statistics,hashlib
from mathutils import Matrix,Vector,Quaternion
ROOT='C:/Users/yj666/Oheangbu';OUT=ROOT+'/Oheangbu/Assets/_Project/Art/Characters/Dosa/Animations';CACHE=ROOT+'/Art/Player/LocomotionPhaseSources';os.makedirs(CACHE,exist_ok=True)
s=bpy.data.scenes['Dosa_Player_Workshop'];bpy.context.window.scene=s;r=bpy.data.objects['Dosa_Rig'];body=bpy.data.objects['Dosa_Body'];s.render.fps=30;s.render.fps_base=1
bones=sorted(r.data.bones,key=lambda b:len(b.parent_recursive));rest={b.name:b.matrix_local.copy() for b in bones};inv={n:m.inverted() for n,m in rest.items()};N=240
# Measured foot-axis maxima/velocity reversals from the unaligned local actions.
# Each triplet is Left heel-strike phase, following Right, following Left.
# Source sole height lags those events, so clean_plant also corrects vertical reach.
events={'WalkForward':(.195833333,.679166667,1.195833333),'WalkBack':(.179166667,.670833333,1.179166667),
 'WalkLeft':(.295833333,.575,.783333333),'WalkRight':(.045833333,.3,.575),
 'RunForward':(.2125,.7375,1.2125),'RunBack':(.475,.733333333,.9375),
 'RunLeft':(.291666667,.575,.783333333),'RunRight':(.045833333,.3,.579166667)}
directions={'Forward':Vector((0,-1,0)),'Back':Vector((0,1,0)),'Left':Vector((1,0,0)),'Right':Vector((-1,0,0))}
report=json.load(open(ROOT+'/Art/Player/motion-export-report.json',encoding='utf-8'));normreport={}
sensors={}
for side in ['Left','Right']:
 sensors[side]=[]
 for v in body.data.vertices:
  ww=[(body.vertex_groups[g.group].name,g.weight) for g in v.groups if body.vertex_groups[g.group].name in rest]
  if v.co.z<.055 and sum(w for n,w in ww if n in [side+'Foot',side+'ToeBase'])>.5:sensors[side].append((v.co.copy(),ww))
def blend(a,b,t):
 out={}
 for bone in bones:
  n=bone.name;q=a[n].to_quaternion().slerp(b[n].to_quaternion(),t);mat=q.to_matrix().to_4x4()
  mat.translation=(out[bone.parent.name]@inv[bone.parent.name])@bone.head_local if bone.parent else a[n].translation.lerp(b[n].translation,t);out[n]=mat
 return out
def apply(pose):
 for bone in bones:
  n=bone.name;r.pose.bones[n].matrix_basis=inv[n]@rest[bone.parent.name]@pose[bone.parent.name].inverted()@pose[n] if bone.parent else inv[n]@pose[n]
def sole(pose,side):
 mm={n:pose[n]@inv[n] for v,ww in sensors[side] for n,w in ww};heights=sorted(sum(((mm[n]@v)*w for n,w in ww),Vector()).z for v,ww in sensors[side]);return heights[int(len(heights)*.08)]
def smooth(t):
 t=max(0,min(1,t));return t*t*(3-2*t)
def clean_plant(pose,t,run):
 targets={};weights={}
 for side,offset in [('Left',0),('Right',.5)]:
  phase=(t-offset)%1;duty=.32 if run else .53
  weight=1 if phase<=duty else 1-smooth((phase-duty)/.10)
  if phase>.9:weight=max(weight,smooth((phase-.9)/.1))
  p=pose[side+'Foot'].translation.copy();p.z-=sole(pose,side)*weight;targets[side]=p;weights[side]=weight
 # Lower the pelvis only as much as necessary for a planted ankle to be reachable.
 lower=0
 for side,target in targets.items():
  if weights[side]<.8:continue
  upper=pose[side+'UpLeg'].translation;length=(rest[side+'Leg'].translation-rest[side+'UpLeg'].translation).length+(rest[side+'Foot'].translation-rest[side+'Leg'].translation).length
  horizontal=(Vector((upper.x,upper.y,0))-Vector((target.x,target.y,0))).length
  allowed=math.sqrt(max(.001,(length*.992)**2-horizontal**2));lower=max(lower,upper.z-target.z-allowed)
 lower=max(0,min(.14,lower))
 for mat in pose.values():mat.translation.z-=lower
 for side,target in targets.items():
  upper=side+'UpLeg';lowerbone=side+'Leg';foot=side+'Foot';toe=side+'ToeBase'
  hip=pose[upper].translation.copy();oldknee=pose[lowerbone].translation.copy();oldankle=pose[foot].translation.copy()
  l1=(rest[lowerbone].translation-rest[upper].translation).length;l2=(rest[foot].translation-rest[lowerbone].translation).length
  delta=target-hip;distance=min(delta.length,(l1+l2)*.998);direction=delta.normalized();target=hip+direction*distance
  along=(l1*l1-l2*l2+distance*distance)/(2*distance);pole=oldknee-hip;pole-=direction*pole.dot(direction)
  if pole.length<.001:pole=Vector((0,-1,0));pole-=direction*pole.dot(direction)
  pole.normalize();knee=hip+direction*along+pole*math.sqrt(max(0,l1*l1-along*along))
  for n,start,end,oldend in [(upper,hip,knee,oldknee),(lowerbone,knee,target,oldankle)]:
   oldstart=pose[n].translation.copy();olddir=(oldend-oldstart).normalized();newdir=(end-start).normalized();q=olddir.rotation_difference(newdir)@pose[n].to_quaternion();pose[n]=q.to_matrix().to_4x4();pose[n].translation=start
  pose[foot].translation=target;pose[toe].translation=(pose[foot]@inv[foot])@r.data.bones[toe].head_local
 return lower
for name,(left,right,nextleft) in events.items():
 backup='Dosa_Unaligned_'+name
 if backup not in bpy.data.actions:
  src=bpy.data.actions['Dosa_'+name].copy();src.name=backup;src.use_fake_user=True
 src=bpy.data.actions[backup];first,last=src.frame_range;r.animation_data.action=src;r.animation_data.action_slot=src.slots[0];samples=[]
 for i in range(N):
  frame=first+(last-first)*i/N;s.frame_set(math.floor(frame),subframe=frame-math.floor(frame));samples.append({b.name:r.pose.bones[b.name].matrix.copy() for b in bones})
 cache={'action':backup,'duration':(last-first)/30,'poses':[{n:[list(row) for row in m] for n,m in p.items()} for p in samples]};cachepath=CACHE+'/'+name+'.json'
 if not os.path.exists(cachepath):open(cachepath,'w').write(json.dumps(cache,separators=(',',':')))
 def at(phase):
  f=(phase%1)*N;i=int(f)%N;return blend(samples[i],samples[(i+1)%N],f-math.floor(f))
 firstpose=at(left);lastpose=at(nextleft);duration=31/30 if name.startswith('Walk') else 19/30;count=round(duration*30)+1;poses=[]
 for i in range(count):
  t=i/(count-1);phase=left+(right-left)*t*2 if t<=.5 else right+(nextleft-right)*(t-.5)*2;p=at(phase)
  # Remove the crop's residual planar trajectory; retain vertical gait and local sway.
  trajectory=firstpose['Hips'].translation.lerp(lastpose['Hips'].translation,t);delta=Vector((rest['Hips'].translation.x-trajectory.x,rest['Hips'].translation.y-trajectory.y,-(lastpose['Hips'].translation.z-firstpose['Hips'].translation.z)*t))
  for mat in p.values():mat.translation+=delta
  poses.append(p)
 # Short endpoint blend closes source capture mismatch without adding an extra step.
 for j in range(2):
  idx=count-2+j;poses[idx]=blend(poses[idx],poses[0],(j+1)/2)
 # Put the lowest sampled sole on the floor without per-bone root-motion tracks.
 minimum=min(sole(p,side) for p in poses for side in ['Left','Right'])
 for p in poses:
  for mat in p.values():mat.translation.z-=minimum
 lowers=[clean_plant(p,i/(len(poses)-1),name.startswith('Run')) for i,p in enumerate(poses)]
 final_floor=min(sole(p,side) for p in poses for side in ['Left','Right'])
 for p in poses:
  for mat in p.values():mat.translation.z-=final_floor
 old=bpy.data.actions['Dosa_'+name];r.animation_data_clear();bpy.data.actions.remove(old);action=bpy.data.actions.new('Dosa_'+name);action.use_fake_user=True;r.animation_data_create();r.animation_data.action=action
 for pb in r.pose.bones:pb.rotation_mode='QUATERNION'
 for f,p in enumerate(poses,1):
  apply(p)
  for pb in r.pose.bones:
   pb.keyframe_insert('location',frame=f,group=pb.name);pb.keyframe_insert('rotation_quaternion',frame=f,group=pb.name);pb.keyframe_insert('scale',frame=f,group=pb.name)
 s.frame_start=1;s.frame_end=count;s.frame_set(1);bpy.ops.object.select_all(action='DESELECT');r.hide_set(False);r.select_set(True);bpy.context.view_layer.objects.active=r
 path=OUT+'/A_DosaCourier_'+name+'.fbx'
 bpy.ops.export_scene.fbx(filepath=path,use_selection=True,object_types={'ARMATURE'},add_leaf_bones=False,use_armature_deform_only=False,bake_anim=True,bake_anim_use_all_actions=False,bake_anim_use_nla_strips=False,bake_anim_force_startend_keying=True,bake_anim_simplify_factor=0,axis_forward='-Z',axis_up='Y',apply_unit_scale=True,apply_scale_options='FBX_SCALE_ALL',path_mode='STRIP')
 direction=next(v for k,v in directions.items() if name.endswith(k));speeds=[];sideinfo={}
 for side in ['Left','Right']:
  heights=[sole(p,side) for p in poses];low=sorted(heights)[int(len(heights)*.05)];ss=[];excursion=[]
  for i in range(len(poses)-1):
   v=(poses[i+1][side+'Foot'].translation-poses[i][side+'Foot'].translation)*30;speed=-v.dot(direction)
   if min(heights[i],heights[i+1])<low+.035 and speed>.05:ss.append(speed)
   excursion.append(poses[i][side+'Foot'].translation.dot(direction))
  speeds.extend(ss);sideinfo[side]={'median_stance_speed_mps':statistics.median(ss) if ss else 0,'foot_excursion_m':max(excursion)-min(excursion),'sole_height_range_m':[min(heights),max(heights)]}
 native=statistics.median(speeds) if speeds else 0;details={'one_stride_cycle':True,'left_support_target_phase':0,'right_support_target_phase':.5,'required_cycle_offset':0,'source_action':backup,'source_phase_events':[left,right,nextleft],'source_cycle_fraction':nextleft-left,'plant_cleanup':'source foot axis reversal aligned; sole height corrected with two-bone leg IK; pelvis lowered within reach','max_pelvis_lowering_m':max(lowers),'source_cache_sha256':hashlib.sha256(open(cachepath,'rb').read()).hexdigest(),'output_sha256':hashlib.sha256(open(path,'rb').read()).hexdigest(),'feet':sideinfo}
 report[name].update({'duration':duration,'frames':count,'measured_stance_foot_speed_mps':native,'speed_match_1_6':1.6/native if native else 1,'speed_match_4_5':4.5/native if native else 1,'phase_normalization':details})
 normreport[name]=details;print(name,'duration',round(duration,3),'stance speed',round(native,4),'L/R',sideinfo)
open(ROOT+'/Art/Player/motion-export-report.json','w',encoding='utf-8').write(json.dumps(report,indent=2));open(ROOT+'/Art/Player/locomotion-cycle-normalization.json','w').write(json.dumps(normreport,indent=2))
r.animation_data.action=bpy.data.actions['Dosa_Idle'];r.animation_data.action_slot=r.animation_data.action.slots[0];s.frame_start=1;s.frame_end=121;s.frame_set(1);bpy.ops.wm.save_as_mainfile(filepath=ROOT+'/Art/Player/Dosa_Animated.blend')
