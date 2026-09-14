"""Retarget inspected Meshy sources to the refined rig, preserving root authority.
Mirroring swaps anatomical sides and reflects rotations; no reversed walk playback.
"""
import bpy,math,json,os,statistics
from mathutils import Vector,Matrix,Quaternion
ROOT='C:/Users/yj666/Oheangbu';OUT=ROOT+'/Oheangbu/Assets/_Project/Art/Characters/Dosa/Animations'
os.makedirs(OUT,exist_ok=True)
source_scene=bpy.data.scenes['Dosa_Motion_Source'];scene=bpy.data.scenes['Dosa_Player_Workshop'];bpy.context.window.scene=scene
rig=bpy.data.objects['Dosa_Rig'];body=bpy.data.objects['Dosa_Body'];scene.render.fps=30;scene.render.fps_base=1
SRC=json.load(open(ROOT+'/Art/Player/motion-source-report.json',encoding='utf-8'))
BONES=sorted(rig.data.bones,key=lambda b:len(b.parent_recursive))
REST={b.name:b.matrix_local.copy() for b in BONES};RESTINV={n:m.inverted() for n,m in REST.items()}
MIRROR=Matrix.Diagonal((-1,1,1))
ALIAS={'Spine':'Spine02','Spine02':'Spine','Spine01':'Spine01'}
SPECS={
 'Idle':('Idle',1,121,4,False), 'CombatReady':('CombatReady',1,51,1.6666667,False),
 'WalkForward':('WalkForward',1,32,31/30,False), 'RunForward':('RunForward',1,20,19/30,False),
 'WalkBack':('WalkBackSource',1,28,27/30,False),
 'WalkLeft':('RunLeftSource',50,89,1.6,False), 'WalkRight':('RunLeftSource',50,89,1.6,True),
 'RunBack':('RunBackSource',142,184,42/30,False),
 'RunLeft':('RunLeftSource',50,89,39/30,False), 'RunRight':('RunLeftSource',50,89,39/30,True),
 'DodgeForward':('RunForward',1,20,.40,False),
 'DodgeBack':('DodgeSource162',19,60,.50,False),
 'DodgeLeft':('DodgeSource164',36,119,.50,False), 'DodgeRight':('DodgeSource164',36,119,.50,True)
}
ALL={};REPORT={}
def mirrored_name(n):return n.replace('Left','_TMP_').replace('Right','Left').replace('_TMP_','Right')
def build_pose(arm,hip_xy,srcstart,srcend,t,mirror=False,forward_dodge=False):
    mats={};hips=arm.matrix_world@arm.pose.bones['Hips'].head
    trajectory=srcstart.lerp(srcend,t)
    delta=hips-trajectory
    # Retain vertical action but remove only planar travel/offset. Actor movement is CC-owned.
    hip=REST['Hips'].translation.copy();hip.x+=delta.x*(-1 if mirror else 1);hip.y+=delta.y
    hip.z+=hips.z-(arm.matrix_world@arm.data.bones['Hips'].head_local).z
    if forward_dodge:hip.z-=.035*math.sin(math.pi*t)
    for b in BONES:
        n=b.name;sn=ALIAS.get(n,n);sn=mirrored_name(sn) if mirror else sn
        if sn in arm.pose.bones:
            sq=(arm.matrix_world@arm.pose.bones[sn].matrix).to_quaternion();sr=(arm.matrix_world@arm.data.bones[sn].matrix_local).to_quaternion()
            dq=sq@sr.inverted()
            if mirror:dq=(MIRROR@dq.to_matrix()@MIRROR).to_quaternion()
            q=dq@REST[n].to_quaternion()
            if forward_dodge and n in ['Spine01','Spine02']:q=Quaternion((1,0,0),math.radians(7)*math.sin(math.pi*t))@q
        elif b.parent:
            q=mats[b.parent.name].to_quaternion()@REST[b.parent.name].to_quaternion().inverted()@REST[n].to_quaternion()
            if any(f in n for f in ['Thumb','Index','Middle','Ring','Pinky']):
                relaxed=[8,12,7] if 'Thumb' not in n else [4,6,4];q=q@Quaternion((1,0,0),-math.radians(relaxed[int(n[-1])-1]))
        else:q=REST[n].to_quaternion()
        head=hip if n=='Hips' else ((mats[b.parent.name]@RESTINV[b.parent.name])@b.head_local if b.parent else b.head_local.copy())
        mat=q.to_matrix().to_4x4();mat.translation=head;mats[n]=mat
    return mats

def blend_pose(a,b,t):
    result={}
    for bone in BONES:
        n=bone.name;q=a[n].to_quaternion().slerp(b[n].to_quaternion(),t)
        head=a[n].translation.lerp(b[n].translation,t) if not bone.parent else (result[bone.parent.name]@RESTINV[bone.parent.name])@bone.head_local
        result[n]=q.to_matrix().to_4x4();result[n].translation=head
    return result

def apply_matrices(mats):
    for b in BONES:
        n=b.name
        basis=(RESTINV[n]@REST[b.parent.name]@mats[b.parent.name].inverted()@mats[n]) if b.parent else RESTINV[n]@mats[n]
        rig.pose.bones[n].matrix_basis=basis

def rebuild_heads(pose):
    for bone in BONES:
        if bone.parent:pose[bone.name].translation=(pose[bone.parent.name]@RESTINV[bone.parent.name])@bone.head_local

def orient_limb(pose,side,target,bend_override=None):
    upper=side+'Arm';lower=side+'ForeArm';hand=side+'Hand'
    shoulder=pose[upper].translation.copy();l1=(REST[lower].translation-REST[upper].translation).length;l2=(REST[hand].translation-REST[lower].translation).length
    v=target-shoulder;dist=min(v.length,(l1+l2)*.96);direction=v.normalized();target=shoulder+direction*dist;along=(l1*l1-l2*l2+dist*dist)/(2*dist)
    bend=Vector(bend_override) if bend_override else Vector((-.28,.10,-1) if side=='Right' else (.28,.10,-1));bend=(bend-direction*bend.dot(direction)).normalized()
    elbow=shoulder+direction*along+bend*math.sqrt(max(0,l1*l1-along*along))
    for n,c,t in [(upper,shoulder,elbow),(lower,elbow,target)]:
        restdir=(REST[lower].translation-REST[upper].translation) if n==upper else (REST[hand].translation-REST[lower].translation)
        q=restdir.normalized().rotation_difference((t-c).normalized())@REST[n].to_quaternion();pose[n]=q.to_matrix().to_4x4();pose[n].translation=c
    pose[hand].translation=target

def polish_pose(pose,name,t):
    """Keep acquired footwork; replace gun arms/extreme idle twist with courier posture."""
    stationary=name in ['Idle','CombatReady']
    if stationary:
        pose={n:m.copy() for n,m in REST.items()}
        for n in ['Spine','Spine01','Spine02']:
            pose[n]=Quaternion((1,0,0),math.radians(.45)*math.sin(t*math.tau)).to_matrix().to_4x4()@REST[n]
    else:
        for n in ['Spine','Spine01','Spine02','neck','Head','LeftShoulder','RightShoulder']:
            if n not in pose:continue
            dq=pose[n].to_quaternion()@REST[n].to_quaternion().inverted()
            strength=.12 if n=='Head' else .22
            q=Quaternion().slerp(dq,strength)@REST[n].to_quaternion()
            pose[n]=q.to_matrix().to_4x4()
    rebuild_heads(pose)
    for side in ['Right','Left']:
        hand=side+'Hand';target=REST[hand].translation.copy()
        if name=='CombatReady':
            target=Vector((-.28,-.13,1.02) if side=='Right' else (.27,-.12,1.02))
        elif not stationary:
            footdelta=pose[side+'Foot'].translation-REST[side+'Foot'].translation
            lateral='Left' in name or 'Right' in name
            run=name.startswith(('Run','Dodge'))
            target.y+=(-.10 if lateral else max(-.16,min(.16,-footdelta.y*(.7 if run else .45))))
            target.z+=.17 if run else .065
            target.x+=(-.025 if side=='Right' else .025)
        orient_limb(pose,side,target)
        # Hand orientation follows the forearm delta, with fingers relaxed in its local space.
        pose[hand]=pose[side+'ForeArm']@RESTINV[side+'ForeArm']@REST[hand]
        for b in BONES:
            if (b.name.startswith(side+'Hand') and b.name!=hand) or b.name==side+'BrushGrip':
                mat=pose[b.parent.name]@RESTINV[b.parent.name]@REST[b.name]
                if any(f in b.name for f in ['Thumb','Index','Middle','Ring','Pinky']):
                    vals=[4,6,4] if 'Thumb' in b.name else [8,12,7]
                    mat=mat@Quaternion((1,0,0),-math.radians(vals[int(b.name[-1])-1])).to_matrix().to_4x4()
                pose[b.name]=mat
    return pose

def export_clip(name,poses,duration,sourceinfo):
    frames=len(poses);rig.animation_data_clear();rig.animation_data_create()
    old=bpy.data.actions.get('Dosa_'+name)
    if old:bpy.data.actions.remove(old)
    action=bpy.data.actions.new('Dosa_'+name);action.use_fake_user=True;rig.animation_data.action=action
    for pb in rig.pose.bones:pb.rotation_mode='QUATERNION'
    for f,mats in enumerate(poses,1):
        apply_matrices(mats)
        for pb in rig.pose.bones:
            pb.keyframe_insert('location',frame=f,group=pb.name);pb.keyframe_insert('rotation_quaternion',frame=f,group=pb.name);pb.keyframe_insert('scale',frame=f,group=pb.name)
    scene.frame_start=1;scene.frame_end=frames;scene.frame_set(1)
    bpy.ops.object.select_all(action='DESELECT');rig.hide_set(False);rig.select_set(True);bpy.context.view_layer.objects.active=rig
    bpy.ops.export_scene.fbx(filepath=OUT+'/A_DosaCourier_'+name+'.fbx',use_selection=True,object_types={'ARMATURE'},add_leaf_bones=False,use_armature_deform_only=False,bake_anim=True,bake_anim_use_all_actions=False,bake_anim_use_nla_strips=False,bake_anim_force_startend_keying=True,bake_anim_simplify_factor=0,axis_forward='-Z',axis_up='Y',apply_unit_scale=True,apply_scale_options='FBX_SCALE_ALL',path_mode='STRIP')
    feet={side:[list(p[side+'Foot'].translation) for p in poses] for side in ['Left','Right']}
    speeds=[];axis=Vector((1,0,0)) if 'Left' in name or 'Right' in name else Vector((0,1,0))
    for side,ff in feet.items():
        low=min(v[2] for v in ff)
        for p,q in zip(ff,ff[1:]):
            if min(p[2],q[2])<low+.025:speeds.append(abs((Vector(q)-Vector(p)).dot(axis))*30)
    native=statistics.median([v for v in speeds if v>.05]) if any(v>.05 for v in speeds) else 0
    REPORT[name]={'duration':(frames-1)/30,'frames':frames,'source':sourceinfo,'root_motion':'planar translation removed; CharacterController authoritative',
        'measured_stance_foot_speed_mps':native,'speed_match_1_6':1.6/native if native else 1,'speed_match_4_5':4.5/native if native else 1,
        'bone_count':len(BONES),'source_horizontal_travel_baked':False}
    ALL[name]=poses;print(name,'frames',frames,'stance speed',round(native,3))

for name,(src,begin,end,duration,mirror) in SPECS.items():
    arm=bpy.data.objects[SRC[src]['armature']];bpy.context.window.scene=source_scene
    source_scene.frame_set(begin);first=arm.matrix_world@arm.pose.bones['Hips'].head
    source_scene.frame_set(end);last=arm.matrix_world@arm.pose.bones['Hips'].head
    count=round(duration*30)+1;poses=[]
    for i in range(count):
        t=i/(count-1);frame=begin+(end-begin)*t;source_scene.frame_set(int(frame),subframe=frame-int(frame))
        poses.append(polish_pose(build_pose(arm,None,first,last,t,mirror,name=='DodgeForward'),name,t))
    # Place a contact sample on the floor, retaining all action vertical variation.
    soles=[min(p[side+'Foot'].translation.z-REST[side+'Foot'].translation.z for side in ['Left','Right']) for p in poses]
    shift=-sorted(soles)[max(0,int(len(soles)*.05))]
    for p in poses:
        for mat in p.values():mat.translation.z+=shift
    loop=name.startswith(('Walk','Run')) or name in ['Idle','CombatReady']
    if loop:
        seam=min(4,max(2,count//8))
        for j in range(seam):
            idx=count-seam+j;poses[idx]=blend_pose(poses[idx],poses[0],(j+1)/seam)
    bpy.context.window.scene=scene
    export_clip(name,poses,duration,{'name':src,'sample_frames':[begin,end],'mirrored_anatomical_sides':mirror,'authored_forward_step':name=='DodgeForward','courier_upper_body_authored':True,'stationary_pose_authored':name in ['Idle','CombatReady']})

# Authored draw poses keep the legacy look and add a real reachable brush-ready posture.
base=ALL['CombatReady'][0]
draw={n:m.copy() for n,m in base.items()}
orient_limb(draw,'Right',Vector((-.25,-.35,1.20)),(1,0,-.3));orient_limb(draw,'Left',Vector((.27,-.22,1.13)),(-1,0,-.3))
for side in ['Right','Left']:
    hand=side+'Hand';grip=side+'BrushGrip'
    y=Vector((0,-1,.22)).normalized();z=Vector((0,0,1));z=(z-y*z.dot(y)).normalized();x=y.cross(z).normalized()
    gripq=Matrix((x,y,z)).transposed().to_quaternion();local=REST[hand].inverted()@REST[grip]
    draw[hand]= (gripq@local.to_quaternion().inverted()).to_matrix().to_4x4();draw[hand].translation=Vector((-.25,-.35,1.20) if side=='Right' else (.27,-.22,1.13))
    for b in BONES:
        if b.name.startswith(side+'Hand') and b.name!=hand or b.name==grip:
            parent=draw[b.parent.name];mat=parent@RESTINV[b.parent.name]@REST[b.name]
            if any(f in b.name for f in ['Thumb','Index','Middle','Ring','Pinky']):
                vals=[15,28,18] if 'Thumb' in b.name else [40,58,35]
                if side=='Left':vals=[10,16,10]
                mat=mat@Quaternion((1,0,0),-math.radians(vals[int(b.name[-1])-1])).to_matrix().to_4x4()
            draw[b.name]=mat
export_clip('DrawReady',[blend_pose(base,draw,(i/9)**2*(3-2*i/9)) for i in range(10)],.3,{'authored':'reachable two-bone FK ready pose, measured fixed grip'})
export_clip('DrawHold',[{n:m.copy() for n,m in draw.items()} for i in range(31)],1,{'authored':'stable loop; runtime endpoint IK supplies drawing motion'})
export_clip('DrawRelease',[blend_pose(draw,base,(i/8)**2*(3-2*i/8)) for i in range(9)],8/30,{'authored':'return from grip pose to combat ready'})
open(ROOT+'/Art/Player/motion-export-report.json','w',encoding='utf-8').write(json.dumps(REPORT,indent=2))
# Leave a useful idle pose in the authoring file; cameras/other source scenes stay separate.
rig.animation_data.action=bpy.data.actions['Dosa_Idle'];scene.frame_start=1;scene.frame_end=121;scene.frame_set(1)
bpy.ops.wm.save_as_mainfile(filepath=ROOT+'/Art/Player/Dosa_Animated.blend')
print('EXPORTED',len(REPORT),'clips')
