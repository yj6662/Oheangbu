"""Export diagnostic finger matrices, not production animation or actions."""
import bpy,json,math
from pathlib import Path
from mathutils import Matrix
from mathutils import Quaternion,Vector
ROOT=Path('C:/Users/yj666/Oheangbu');OUT=ROOT/'Art/PlayerV2'
bpy.ops.wm.open_mainfile(filepath=str(OUT/'DosaV2_Refined.blend'))
rig=bpy.data.objects['DosaV2_Rig'];report=json.loads((OUT/'Calibration/hand-brush-contact-report.json').read_text())
def rows(m):return [list(row) for row in m]
rest={p.name:p.matrix.copy() for p in rig.pose.bones}
for side,entry in report['sides'].items():
    for finger,angles in entry['finger_euler_xyz_degrees'].items():
        for i,angle in enumerate(angles):
            p=rig.pose.bones[side+'Hand'+finger+str(i+1)];p.rotation_mode='XYZ';p.rotation_euler=[math.radians(v) for v in angle]
bpy.context.view_layer.update()
data={'status':'DIAGNOSTIC_POSE_ONLY','blender_to_unity_position':[[-1,0,0],[0,0,1],[0,-1,0]],'bones':[],'hands':[]}
for p in rig.pose.bones:
    if not any(p.name.startswith(side+'Hand') for side in ['Left','Right']):continue
    data['bones'].append({'name':p.name,'parent':p.parent.name,'rest':rows(rest[p.name]),'posed':rows(p.matrix)})
for side,entry in report['sides'].items():
    data['hands'].append({'side':side,'grip':entry['grip_matrix_rig_local']})
path=OUT/'Calibration/blender-static-grip-matrices.json';path.write_text(json.dumps(data,indent=2))
unity_file=ROOT/'Oheangbu/Screenshots/PlayerDosaV2/import-bones-and-surfaces.json'
if unity_file.exists():
    entries={b['name']:b for b in json.loads(unity_file.read_text())['bones']}
    C=Matrix(((-1,0,0),(0,0,1),(0,-1,0)))
    def uq(d):return Quaternion((d['w'],d['x'],d['y'],d['z']))
    targets={}
    for p in rig.pose.bones:
        if p.name not in entries:continue
        transformed=C@rest[p.name].translation
        u=entries[p.name]['position'];assert (transformed-Vector((u['x'],u['y'],u['z']))).length<.00001,p.name
        delta=C@(p.matrix.to_3x3()@rest[p.name].to_3x3().inverted())@C.inverted()
        targets[p.name]=delta.to_quaternion()@uq(entries[p.name]['rotation'])
    offsets=[]
    for side in ['Right','Left']:
        for finger in ['Thumb','Index','Middle','Ring','Pinky']:
            for i in range(3):
                name=side+'Hand'+finger+str(i+1);e=entries[name]
                local=targets[e['parent']].inverted()@targets[name]
                q=uq(e['localRotation']).inverted()@local;q.normalize()
                offsets.append({'name':name,'offset':{'x':q.x,'y':q.y,'z':q.z,'w':q.w}})
    (OUT/'Calibration/unity-grip-offsets.json').write_text(json.dumps({'status':'DIAGNOSTIC_IMPORT_BASIS_CALIBRATED','offsets':offsets},indent=2))
print(str(path))
