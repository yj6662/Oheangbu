"""B1 local motion edit: cloned actions, support-aware two-bone IK, no mesh/weight edit.
Run headless with --background Body/A2.blend --python this_file. Original actions stay intact.
"""
import bpy, json, math, hashlib
import numpy as np
from pathlib import Path
from mathutils import Vector, Matrix, Quaternion

ROOT=Path('C:/Users/yj666/Oheangbu');LAB=ROOT/'Art/PlayerPhase1/C02_RigFaceLab';OUT=LAB/'Motion/B1'
FPS=30

def set_action(rig,action,frame):
    for p in rig.pose.bones:p.matrix_basis.identity()
    rig.animation_data_create();rig.animation_data.action=action
    if action.slots:rig.animation_data.action_slot=action.slots[0]
    rig.data.pose_position='POSE';bpy.context.scene.frame_set(frame);bpy.context.view_layer.update()

def vertices(obj):
    ev=obj.evaluated_get(bpy.context.evaluated_depsgraph_get());m=ev.to_mesh();a=np.empty((len(m.vertices),3));m.vertices.foreach_get('co',a.ravel())
    mat=np.array(ev.matrix_world);a=a@mat[:3,:3].T+mat[:3,3];ev.to_mesh_clear();return a

def capture(rig):
    return {p.name:(p.location.copy(),p.rotation_quaternion.copy() if p.rotation_mode=='QUATERNION' else p.matrix_basis.to_quaternion(),p.scale.copy()) for p in rig.pose.bones}

def materialize(rig,poses,name):
    if bpy.data.actions.get(name):raise RuntimeError('Action already exists: '+name)
    a=bpy.data.actions.new(name);a.use_fake_user=True;rig.animation_data_create();rig.animation_data.action=a
    for index,pose in enumerate(poses,1):
        for bone,(loc,q,scale) in pose.items():
            p=rig.pose.bones[bone];p.rotation_mode='QUATERNION';p.location=loc;p.rotation_quaternion=q;p.scale=scale
            p.keyframe_insert('location',frame=index,group=bone);p.keyframe_insert('rotation_quaternion',frame=index,group=bone);p.keyframe_insert('scale',frame=index,group=bone)
    for layer in a.layers:
        for strip in layer.strips:
            for bag in strip.channelbags:
                for fc in bag.fcurves:
                    for key in fc.keyframe_points:key.interpolation='LINEAR'
    return a

def rotate_pose(p,q):
    loc,rot,sc=p.matrix.decompose();p.matrix=Matrix.LocRotScale(loc,q@rot,sc);bpy.context.view_layer.update()

def foot_ik(rig,side,delta_world):
    upper=rig.pose.bones[side+'UpLeg'];lower=rig.pose.bones[side+'Leg'];foot=rig.pose.bones[side+'Foot']
    hip=upper.head.copy();knee=lower.head.copy();ankle=foot.head.copy();qfoot=foot.matrix.to_quaternion().copy()
    delta=rig.matrix_world.inverted().to_3x3()@Vector(delta_world)
    target=ankle+delta;l1=(knee-hip).length;l2=(ankle-knee).length;direction=target-hip;distance=direction.length
    if distance<1e-8:return 0.0
    d=min(distance,(l1+l2)*.9998);direction.normalize()
    bend=knee-hip-direction*(knee-hip).dot(direction)
    if bend.length<1e-7:bend=Vector((0,-1,0))-direction*direction.dot(Vector((0,-1,0)))
    bend.normalize();along=(l1*l1-l2*l2+d*d)/(2*d);newknee=hip+direction*along+bend*math.sqrt(max(0,l1*l1-along*along))
    rotate_pose(upper,(knee-hip).rotation_difference(newknee-hip))
    currentknee=lower.head.copy();currentankle=foot.head.copy();rotate_pose(lower,(currentankle-currentknee).rotation_difference(target-currentknee))
    loc,_,scale=foot.matrix.decompose();foot.matrix=Matrix.LocRotScale(loc,qfoot,scale);bpy.context.view_layer.update()
    return (rig.matrix_world@foot.head-rig.matrix_world@target).length

def groups(mask):
    ids=np.flatnonzero(mask);return [g for g in np.split(ids,np.flatnonzero(np.diff(ids)>1)+1) if len(g)]

def build():
    OUT.mkdir(parents=True,exist_ok=True)
    if (OUT/'B1.json').exists():
        recorded=json.loads((OUT/'B1.json').read_text(encoding='utf8'))
        if recorded.get('source_sha256')==hashlib.sha256(Path(bpy.data.filepath).read_bytes()).hexdigest() and (OUT/'Motion_B1.blend').is_file():
            print('MOTION_B1_REUSED',str(OUT/'Motion_B1.blend'));return
        raise RuntimeError('B1 attempt exists for a different or incomplete source; do not silently repeat')
    rig=bpy.data.objects['Armature'];obj=bpy.data.objects['C02_Mesh_0'];source=Path(bpy.data.filepath)
    sourcehash=hashlib.sha256(source.read_bytes()).hexdigest();rig.animation_data_clear();rig.data.pose_position='REST';bpy.context.view_layer.update();rest=vertices(obj)
    probes={}
    for side in ('Left','Right'):
        g=obj.vertex_groups[side+'Foot'];ids=np.array([v.index for v in obj.data.vertices if any(w.group==g.index and w.weight>=.7 for w in v.groups)])
        probes[side]=ids[rest[ids,2]<=np.percentile(rest[ids,2],15)]
    report={'stage':'B','attempt':1,'source_blend':str(source),'source_sha256':sourcehash,'hypothesis':'Preserve already-closed loops; remove Attack terminal presentation drift, ground Idle feet and soften support trajectories with rotational leg IK; keep original timing and torso/arm source motion.',
            'method':'Clone sampled original actions. Two-bone rotational IK only, capped 6cm correction. Native-speed stance fit; 5mm sole target. Attack root planar drift removed with smoothstep. No scale animation changes.',
            'geometry_uv_weights_rest_bones_changed':False,'new_clothing_bones':0,'secondary_motion':{'status':'NOT_ATTEMPTED','reason':'A2 has residual integrated-cloth uncertainty; no separate stable cloth region has been validated'},
            'animation_naturalness':{'status':'UNVERIFIED','reason':'Pending numerical comparison and continuous rendered review'},'paid_requests':0,'clips':[],'original_actions':[]}
    pose_sets={};original_sets={}
    for label,native in [('Idle',0.0),('Walk',1.5537527974691157),('Run',5.362285039688777),('Attack',0.0)]:
        a=bpy.data.actions['C02_'+label];start,end=map(lambda x:int(round(x)),a.frame_range);n=end-start+1; samples=[];original=[]
        for f in range(start,end+1):
            set_action(rig,a,f);v=vertices(obj);original.append(capture(rig));samples.append({'hips':np.array(rig.matrix_world@rig.pose.bones['Hips'].head),'feet':{s:(np.mean(v[ids],axis=0),float(np.min(v[ids,2]))) for s,ids in probes.items()}})
        original_sets[label]=original;report['original_actions'].append(a.name)
        t=np.linspace(0,1,n);drift=samples[-1]['hips']-samples[0]['hips'];drift[2]=0
        shift=np.outer(-(t*t*(3-2*t)),drift) if label=='Attack' else np.zeros((n,3))
        corrections={};details={}
        for side in probes:
            pos=np.array([s['feet'][side][0] for s in samples])+shift;low=np.array([s['feet'][side][1] for s in samples]);minimum=low.min()
            weight=np.clip(1-(low-minimum)/.04,0,1);weight=weight*weight*(3-2*weight)
            if label=='Idle':weight[:]=1
            delta=np.zeros_like(pos);delta[:,2]=(.005-low)*weight
            support=weight>.25
            for group in groups(support):
                if len(group)<2:continue
                ideal_x=float(np.median(pos[group,0]));ideal_y=native*(group/FPS)+float(np.median(pos[group,1]-native*group/FPS))
                delta[group,0]=(ideal_x-pos[group,0])*weight[group]
                delta[group,1]=(ideal_y-pos[group,1])*weight[group]
            lengths=np.linalg.norm(delta,axis=1);delta*=np.minimum(1,.06/np.maximum(lengths,1e-12))[:,None]
            corrections[side]=delta;details[side]={'before_floor_min_m':float(low.min()),'before_floor_max_m':float(low.max()),'maximum_requested_correction_m':float(np.linalg.norm(delta,axis=1).max()),'support_frames':[int(x+start) for x in np.flatnonzero(support)]}
        poses=[];ik_errors=[];afterlow={s:[] for s in probes};aftersoles={s:[] for s in probes}
        for i,f in enumerate(range(start,end+1)):
            set_action(rig,a,f)
            if label=='Attack':
                hips=rig.pose.bones['Hips'];m=hips.matrix.copy();m.translation+=rig.matrix_world.inverted().to_3x3()@Vector(shift[i]);hips.matrix=m;bpy.context.view_layer.update()
            for side in probes:
                if np.linalg.norm(corrections[side][i])>1e-7:ik_errors.append(foot_ik(rig,side,corrections[side][i]))
            poses.append(capture(rig));v=vertices(obj)
            for side,ids in probes.items():afterlow[side].append(float(v[ids,2].min()));aftersoles[side].append(np.mean(v[ids],axis=0).tolist())
        if label in ('Idle','Walk','Run'):poses[-1]={bone:(loc.copy(),q.copy(),sc.copy()) for bone,(loc,q,sc) in poses[0].items()}
        action=materialize(rig,poses,'LAB_B1_'+label);pose_sets[label]=poses
        for side in probes:
            details[side]['after_floor_min_m']=float(min(afterlow[side]));details[side]['after_floor_max_m']=float(max(afterlow[side]));details[side]['after_sole_centroids']=aftersoles[side]
        report['clips'].append({'source_action':a.name,'action':action.name,'frames':[1,n],'fps':FPS,'duration_seconds':(n-1)/FPS,'native_speed_mps':native,
                                'hips_net_removed_m':drift.tolist() if label=='Attack' else [0,0,0], 'ik_max_target_error_m':max(ik_errors,default=0),'feet':details})
    def blend_pose(a,b,w):
        return {k:(a[k][0].lerp(b[k][0],w),a[k][1].slerp(b[k][1],w),a[k][2].lerp(b[k][2],w)) for k in a}
    recipe=[]
    def hold(clip,count):
        for f in range(count):recipe.append({'a':clip,'fa':f%(len(pose_sets[clip])-1),'b':None,'weight':0})
    def transition(a,b,count):
        for f in range(count):
            w=(f+1)/count;w=w*w*(3-2*w);recipe.append({'a':a,'fa':f%(len(pose_sets[a])-1),'b':b,'fb':f%(len(pose_sets[b])-1),'weight':w})
    hold('Idle',30);transition('Idle','Run',12);hold('Run',6*(len(pose_sets['Run'])-1));transition('Run','Idle',12);hold('Idle',30);transition('Idle','Attack',10)
    for f in range(len(pose_sets['Attack'])):recipe.append({'a':'Attack','fa':f,'b':None,'weight':0})
    transition('Attack','Idle',12);hold('Idle',30)
    for sets,name in [(pose_sets,'LAB_B1_Sequence'),(original_sets,'LAB_Original_Sequence')]:
        sequence=[blend_pose(sets[r['a']][r['fa']],sets[r['b']][r['fb']],r['weight']) if r['b'] else sets[r['a']][r['fa']] for r in recipe]
        materialize(rig,sequence,name)
    report['sequence']={'actions':['LAB_Original_Sequence','LAB_B1_Sequence'],'frames':len(recipe),'fps':30,'recipe':'Idle → Run (6 original-speed cycles) → Idle → Attack → Idle; both comparison versions use identical explicitly-created smooth transition policy',
                        'run_cycles':6,'run_cycle_frames':len(pose_sets['Run'])-1,'notes':'Generated diagnostic sequence, not existing game controller playback. Source clip keys retained unchanged.'}
    report['speed_mapping']={'game_max_mps':4.5,'game_config_changed':False,'walk_native_mps':1.5537527974691157,'run_native_mps':5.362285039688777,'run_animator_speed_at_4_5':4.5/5.362285039688777,
                             'render_policy':'Source motion is 30fps at 1x. Moving ground reference uses native clip speed, not 4.5m/s. Runtime speed mapping is a recommendation pending Unity verification.'}
    report['finger_open_close']={'status':'NOT_ATTEMPTED','reason':'24-bone source has no finger bones; actual finger articulation cannot be tested without a separate structural change'}
    report['weapon_grip']={'status':'NOT_ATTEMPTED','reason':'Unarmed source Attack; no finger-contact or brush attachment test'}
    (OUT/'sequence_recipe.json').write_text(json.dumps(recipe),encoding='utf8')
    report['blend']='Motion/B1/Motion_B1.blend';report['status']='AWAITING_RENDER_AND_COMPARISON'
    set_action(rig,bpy.data.actions['LAB_B1_Idle'],1);bpy.context.scene.render.fps=30
    bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'Motion_B1.blend'))
    (OUT/'B1.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf8')
    print('MOTION_B1_BUILT',json.dumps({'blend':report['blend'],'clips':[{k:c[k] for k in ('action','duration_seconds','ik_max_target_error_m')} for c in report['clips']],'sequence_frames':len(recipe)}))

if __name__=='__main__':build()
