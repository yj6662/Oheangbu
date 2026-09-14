"""Read-only pose sampling; emits phase/speed evidence without changing any asset."""
import bpy,math,json,statistics
from mathutils import Vector
ROOT='C:/Users/yj666/Oheangbu';N=240
s=bpy.data.scenes['Dosa_Player_Workshop'];r=bpy.data.objects['Dosa_Rig']
body=bpy.data.objects['Dosa_Body'];sensors={}
for side in ['Left','Right']:
    sensors[side]=[]
    for v in body.data.vertices:
        weights=[(body.vertex_groups[g.group].name,g.weight) for g in v.groups if body.vertex_groups[g.group].name in r.data.bones]
        if v.co.z<.055 and sum(w for n,w in weights if n in [side+'Foot',side+'ToeBase'])>.50:sensors[side].append((v.co.copy(),weights))
rest_inverse={n:b.matrix_local.inverted() for n,b in r.data.bones.items()}
previous_scene=bpy.context.window.scene;previous_action=r.animation_data.action;previous_frame=s.frame_current;previous_subframe=s.frame_subframe
bpy.context.window.scene=s
directions={'Forward':Vector((0,-1,0)),'Back':Vector((0,1,0)),'Left':Vector((1,0,0)),'Right':Vector((-1,0,0))}
def vec(v):return [float(x) for x in v]
def circular_segments(mask):
    starts=[i for i,b in enumerate(mask) if b and not mask[(i-1)%N]]
    out=[]
    for start in starts:
        length=0
        while length<N and mask[(start+length)%N]:length+=1
        if length>=max(3,round(N*.025)):out.append({'start_phase':start/N,'end_phase':((start+length)%N)/N,'duration_phase':length/N,'wraps':start+length>N,'start_index':start,'samples':length})
    return sorted(out,key=lambda x:x['duration_phase'],reverse=True)
result={}
try:
 for name in ['WalkForward','WalkBack','WalkLeft','WalkRight','RunForward','RunBack','RunLeft','RunRight']:
    direction=next(v for n,v in directions.items() if name.endswith(n));action=bpy.data.actions['Dosa_'+name];start,end=action.frame_range;duration=(end-start)/30
    r.animation_data.action=action;r.animation_data.action_slot=action.slots[0];points={side:[] for side in ['Left','Right']};toes={side:[] for side in points};skin_sole={side:[] for side in points}
    for i in range(N):
        frame=start+(end-start)*i/N;s.frame_set(math.floor(frame),subframe=frame-math.floor(frame))
        for side in points:
            points[side].append(r.pose.bones[side+'Foot'].head.copy());toes[side].append(r.pose.bones[side+'ToeBase'].head.copy())
            matrices={n:r.pose.bones[n].matrix@rest_inverse[n] for pos,ww in sensors[side] for n,w in ww}
            heights=sorted(sum(((matrices[n]@pos)*w for n,w in weights),Vector()).z for pos,weights in sensors[side])
            skin_sole[side].append(heights[max(0,int(len(heights)*.08))])
    row={'duration_sec':duration,'frames_including_duplicate_endpoint':int(end-start)+1,'desired_root_direction_blender_xyz':vec(direction),'feet':{}}
    for side,pp in points.items():
        dt=duration/N;vel=[(pp[(i+1)%N]-pp[(i-1)%N])/(2*dt) for i in range(N)]
        sole=skin_sole[side]
        low=sorted(sole)[int(N*.05)];threshold=low+.035
        heightmask=[x<=threshold for x in sole];supportmask=[heightmask[i] and vel[i].dot(direction)<-.05 for i in range(N)]
        segments=circular_segments(supportmask);heightsegments=circular_segments(heightmask)
        lowvel=[vel[i] for i in range(N) if heightmask[i]]
        stancevel=[vel[i] for i in range(N) if supportmask[i]]
        mean=sum(lowvel,Vector())/len(lowvel) if lowvel else Vector()
        stance_mean=sum(stancevel,Vector())/len(stancevel) if stancevel else Vector()
        for segment in segments:
            ids=[(segment['start_index']+j)%N for j in range(segment['samples'])]
            segment['stance_travel_m']=sum(max(0,-vel[j].dot(direction))*dt for j in ids)
            segment['stance_speed_mps']=statistics.median([-vel[j].dot(direction) for j in ids])
        signed=[v.dot(direction) for v in vel];total_negative=sum(max(0,-v)*dt for v in signed)
        projected=[p.dot(direction) for p in pp]
        row['feet'][side]={'support_segments':segments,'height_contact_segments':heightsegments,'lowest_sole_estimate_m':low,'sole_height_range_m':[min(sole),max(sole)],
            'lowest_35mm_mean_velocity_blender_xyz':vec(mean),'lowest_35mm_mean_velocity_unity_xyz':[-mean.x,mean.z,-mean.y],
            'lowest_35mm_signed_speed_along_root_mps':mean.dot(direction),'support_mean_velocity_blender_xyz':vec(stance_mean),
            'signed_speed_range_along_root_mps':[min(signed),max(signed)],'projected_foot_excursion_m':max(projected)-min(projected),'negative_foot_travel_per_cycle_m':total_negative,
            'pose_samples':[{'phase':i/N,'foot_xyz':vec(pp[i]),'toe_xyz':vec(toes[side][i]),'sole_estimate_m':sole[i],'velocity_xyz':vec(vel[i]),'signed_axis_speed':signed[i],'support':supportmask[i]} for i in range(0,N,4)]}
    result[name]=row
finally:
    r.animation_data.action=previous_action
    if previous_action:r.animation_data.action_slot=previous_action.slots[0]
    s.frame_set(previous_frame,subframe=previous_subframe);bpy.context.window.scene=previous_scene
reference=result['WalkForward']['feet']['Left']['support_segments'][0]['start_phase']
for name,row in result.items():
    left=row['feet']['Left']['support_segments'];right=row['feet']['Right']['support_segments']
    row['diagnostic_cycle_offset_align_left_to_walk_forward']=(left[0]['start_phase']-reference)%1 if left else None
    row['dominant_left_right_start_separation_phase']=(right[0]['start_phase']-left[0]['start_phase'])%1 if left and right else None
    row['cycle_offset_caveat']='Aligns the dominant left support onset only; does not fix unequal/multiple steps, source trajectory or seam artifacts.'
out={'samples_per_clip':N,'phase_reference_walk_forward_left_support_start':reference,'coordinate_note':'Blender forward -Y/right -X; Unity forward +Z/right +X','support_detection':'Actual skinned rest-sole vertices, eighth-percentile height within 35mm of per-foot fifth-percentile cycle height; foot velocity opposite desired actor movement >0.05m/s','clips':result}
open(ROOT+'/Art/Player/locomotion-phase-diagnostic-skin.json','w',encoding='utf-8').write(json.dumps(out,indent=2))
for name,row in result.items():
 print(name,'duration',round(row['duration_sec'],3),'offset',row['diagnostic_cycle_offset_align_left_to_walk_forward'],'LR phase',row['dominant_left_right_start_separation_phase'])
 for side,f in row['feet'].items():print(side,'support',[(round(x['start_phase'],3),round(x['duration_phase'],3),round(x['stance_travel_m'],3)) for x in f['support_segments']],'low signed speed',round(f['lowest_35mm_signed_speed_along_root_mps'],3),'excursion',round(f['projected_foot_excursion_m'],3),'negative travel',round(f['negative_foot_travel_per_cycle_m'],3))
