"""Explicit diagnostic FK poses; creates JSON, never an Action or production clip."""
import bpy,json,math,hashlib
from pathlib import Path
from mathutils import Vector,Matrix,Quaternion,Euler
ROOT=Path(__file__).resolve().parents[3];ART=ROOT/'Art/PlayerV2'
source=ART/'DosaV2_Assembled.blend'
bpy.ops.wm.open_mainfile(filepath=str(source));rig=bpy.data.objects['DosaV2_Rig']
assert not bpy.data.objects['CTRL_DosaV2_Root']['AuthoringMode']
cal=json.loads((ART/'Calibration/hand-brush-contact-report.json').read_text())['sides']
def update():bpy.context.view_layer.update()
def reset():
    for p in rig.pose.bones:p.matrix_basis=Matrix.Identity(4)
    update()
def aim(name,direction):
    p=rig.pose.bones[name];m=p.matrix.copy();q=m.to_quaternion()
    q=(q@Vector((0,1,0))).rotation_difference(Vector(direction).normalized())@q
    changed=q.to_matrix().to_4x4();changed.translation=m.translation;p.matrix=changed;update()
def twist(name,axis,degrees):
    p=rig.pose.bones[name];m=p.matrix.copy()
    q=Quaternion(Vector(axis).normalized(),math.radians(degrees))@m.to_quaternion()
    changed=q.to_matrix().to_4x4();changed.translation=m.translation;p.matrix=changed;update()
def lower():
    for side,sign in [('Left',1),('Right',-1)]:aim(side+'Arm',(sign*.12,0,-1))
def bend_elbows(degrees):
    lower();a=math.radians(degrees)
    for side in ('Left','Right'):aim(side+'ForeArm',(0,-math.sin(a),-math.cos(a)))
def grip(up=False):
    lower()
    for side,sign in [('Left',1),('Right',-1)]:
        aim(side+'Arm',(sign*.08,-.45,-.88));aim(side+'ForeArm',(sign*.04,-.75,.66))
        for finger,angles in cal[side]['finger_euler_xyz_degrees'].items():
            for joint,xyz in enumerate(angles,1):
                p=rig.pose.bones[side+'Hand'+finger+str(joint)]
                q=Euler([math.radians(v) for v in xyz],'XYZ').to_quaternion()
                if up and finger in ('Ring','Pinky'):q=Quaternion().slerp(q,.9)
                p.rotation_mode='QUATERNION';p.rotation_quaternion=q
        update()
pose_names=['rest','open_hand','grip_down','grip_up','shoulder_45','shoulder_90','shoulder_120',
    'elbow_45','elbow_90','elbow_120','forearm_minus90','forearm_plus90','wrist_flex','wrist_extend',
    'torso_twist','hip_flex','knee_flex','ankle_flex','combined_reach',
    'hip_flex_right','knee_flex_right','ankle_flex_right']
out=[]
for name in pose_names:
    reset()
    if name=='open_hand':lower()
    elif name.startswith('grip_'):grip(name.endswith('up'))
    elif name.startswith('shoulder_'):
        a=math.radians(int(name.split('_')[1]))
        for side,sign in [('Left',1),('Right',-1)]:aim(side+'Arm',(sign*math.sin(a),0,-math.cos(a)))
    elif name.startswith('elbow_'):bend_elbows(int(name.split('_')[1]))
    elif name.startswith('forearm_'):
        bend_elbows(65)
        for side in ('Left','Right'):
            p=rig.pose.bones[side+'ForeArm'];twist(p.name,p.matrix.to_quaternion()@Vector((0,1,0)), -90 if 'minus' in name else 90)
    elif name.startswith('wrist_'):
        grip()
        for side in ('Left','Right'):
            p=rig.pose.bones[side+'Hand'];twist(p.name,p.matrix.to_quaternion()@Vector((1,0,0)),30 if name.endswith('flex') else -30)
    elif name=='torso_twist':lower();twist('Spine01',(0,0,1),35)
    elif name=='hip_flex':lower();aim('LeftUpLeg',(0,-.7071068,-.7071068))
    elif name=='knee_flex':lower();aim('LeftLeg',(0,.9848078,.1736482))
    elif name=='ankle_flex':lower();twist('LeftFoot',(1,0,0),25)
    elif name=='hip_flex_right':lower();aim('RightUpLeg',(0,-.7071068,-.7071068))
    elif name=='knee_flex_right':lower();aim('RightLeg',(0,.9848078,.1736482))
    elif name=='ankle_flex_right':lower();twist('RightFoot',(1,0,0),25)
    elif name=='combined_reach':
        grip();twist('Spine01',(0,0,1),20)
        for side,sign in [('Left',1),('Right',-1)]:
            aim(side+'Arm',(-sign*.4,-.88,.1));aim(side+'ForeArm',(-sign*.6,-.78,.15))
    rotations=[]
    for p in rig.pose.bones:
        xyz=[math.degrees(v) for v in p.matrix_basis.to_euler('XYZ')]
        if p.name=='Root' or max(abs(v) for v in xyz)>1e-5:
            rotations.append(dict(bone=p.name,x=xyz[0],y=xyz[1],z=xyz[2]))
    closed=name.startswith(('grip_','wrist_')) or name=='combined_reach'
    out.append({'id':name,'boneRotations':rotations,
        'handGripCorrectives':{'right':1.0 if closed else 0.0,'left':1.0 if closed else 0.0},
        'boneMatricesRigLocal':{p.name:[list(row) for row in p.matrix] for p in rig.pose.bones}})
reset();assert len(bpy.data.actions)==0
target=ART/'Validation/static-pose-definitions.json'
target.write_text(json.dumps({'mode':'DIRECT_BONE_POSES','axisConvention':'Blender meters, Z up, character forward -Y. boneRotations are rest-relative LOCAL XYZ Euler degrees; full rig-space matrices preserve exact basis for Unity conversion.',
    'shoulderAngles':'Abduction from arm-down:90 degrees equals the authored T-pose. Forearm tests are axial rotation, elbows are flexion from extension.',
    'source':str(source.relative_to(ROOT)).replace('\\','/'),'sourceSha256':hashlib.sha256(source.read_bytes()).hexdigest(),'productionActions':0,'poses':out},indent=2),encoding='utf-8')
print('DIRECT_POSE_DEFINITIONS',len(out),target)
