"""Inspect anatomically fitted garment gusset candidates without source mutation.

Source closed lining is not shrunk. Fixed attachment, UV and all weights remain
unchanged. A shared rest-position field tailors the garment in the actual space
between torso and adducted arm, on BodyCore, both sleeves and waistband alike.
"""
import bpy,json,math,hashlib
from pathlib import Path
import numpy as np
from mathutils import Matrix,Vector
from mathutils.bvhtree import BVHTree
ROOT=Path(__file__).resolve().parents[3]
DIR=ROOT/'Art/PlayerV2/Inspect/ClothBlender/PosedArmFit9f4cd412'
OUT=ROOT/'Art/PlayerV2/Inspect/ClothBlender/UnderarmTailoring';OUT.mkdir(parents=True,exist_ok=True)
SOURCE=ROOT/'Art/PlayerV2/Inspect/ClothBlender/Inputs/Assembled-final-9f4cd412.blend'
topology=json.loads((DIR/'topology-rest.json').read_text())
poses=[]
for p in sorted(DIR.glob('*.json')):
    value=json.loads(p.read_text())
    if 'poseId' in value:poses.append(value)
bpy.ops.wm.open_mainfile(filepath=str(SOURCE));rig=bpy.data.objects['DosaV2_Rig']
for o in bpy.context.scene.objects:
    if o.name.startswith('CTRL_') and 'AuthoringMode' in o:o['AuthoringMode']=False;o.update_tag()
for bone in rig.pose.bones:bone.matrix_basis=Matrix.Identity(4)
bpy.context.view_layer.update()

def smooth(lo,hi,x):
    t=np.clip((x-lo)/(hi-lo),0,1);return t*t*(3-2*t)

def tailored(points,underarm_x,waist_x):
    result=points.copy();x=np.abs(points[:,0]);z=points[:,2]
    under=np.maximum(0,x-underarm_x)*smooth(.13,.20,x)*(1-smooth(.26,.33,x))*smooth(.98,1.10,z)*(1-smooth(1.24,1.31,z))
    waist=np.maximum(0,x-waist_x)*smooth(.17,.24,x)*(1-smooth(.28,.34,x))*smooth(.87,.93,z)*(1-smooth(.99,1.06,z))
    result[:,0]-=np.sign(points[:,0])*np.maximum(under,waist)
    return result

cloth_names=['DosaV2_Robe_Combined','DosaV2_SleeveOuter_L','DosaV2_SleeveOuter_R']
inner_names=[n for n in topology['topology'] if 'Lining' in n]
pins=[]
for name in cloth_names:
    obj=bpy.data.objects[name]
    for i,c in enumerate(obj.data.color_attributes['ClothMobility'].data):
        if c.color[0]!=0:continue
        pins.append({'mesh':name,'vertex':i,'point':list(obj.data.vertices[i].co),
                     'weights':{obj.vertex_groups[g.group].name:g.weight for g in obj.data.vertices[i].groups}})
rest=np.array([p['point'] for p in pins]);directions=[Vector(d).normalized() for d in [(1,.371,.219),(.173,1,.293),(.271,.123,1)]]
models={}
for pose in poses:
    trees=[]
    for name in inner_names:
        points=pose['vertices'][name];triangles=topology['topology'][name]['triangles']
        if name.startswith('DosaV2_ArmLining_'):
            points=np.array(points);rest_lining=np.array(topology['topology'][name]['restVertices'])
            selected=np.flatnonzero(np.abs(rest_lining[:,0])<.335)
            bone=name.split('_')[-1]+'Arm'
            matrix=np.array(pose['bonePoseMatrices'][bone])@np.linalg.inv(np.array(topology['boneRestMatrices'][bone]))
            points[selected]=(np.column_stack((rest_lining[selected],np.ones(len(selected))))@matrix.T)[:,:3]
        trees.append((name,BVHTree.FromPolygons(points,triangles,all_triangles=True)))
    skin=[];arm_skin=[]
    for pin in pins:
        matrix=np.zeros((4,4))
        for bone,weight in pin['weights'].items():
            matrix+=weight*np.array(pose['bonePoseMatrices'][bone])@np.linalg.inv(np.array(topology['boneRestMatrices'][bone]))
        skin.append(matrix)
        arm=('Left' if pin['point'][0]>0 else 'Right')+'Arm'
        arm_skin.append(np.array(pose['bonePoseMatrices'][arm])@np.linalg.inv(np.array(topology['boneRestMatrices'][arm])))
    skin=np.array(skin);expected=np.array([pose['vertices'][p['mesh']][p['vertex']] for p in pins])
    predicted=np.einsum('nij,nj->ni',skin,np.column_stack((rest,np.ones(len(rest)))))[:,:3]
    err=np.max(np.linalg.norm(predicted-expected,axis=1));assert err<1e-6,err
    models[pose['poseId']]={'skin':skin,'armSkin':np.array(arm_skin),'trees':trees,'skinError':float(err)}

def inside(tree,p):
    votes=[]
    for direction in directions:
        origin=p+direction*.000001;count=0
        for _ in range(64):
            hit=tree.ray_cast(origin,direction,10)
            if hit[0] is None:break
            count+=1;origin=hit[0]+direction*.000005
        votes.append(count%2)
    return sum(votes)>=2

def query(points,model,indices,weight_alpha=None):
    skin=model['skin'] if weight_alpha is None else model['skin']*(1-weight_alpha[:,None,None])+model['armSkin']*weight_alpha[:,None,None]
    evaluated=np.einsum('nij,nj->ni',skin,np.column_stack((points,np.ones(len(points)))))[:,:3]
    rows=[]
    for index in indices:
        point=Vector(evaluated[index])
        for name,tree in model['trees']:
            loc,normal,tri,distance=tree.find_nearest(point)
            if inside(tree,point):
                rows.append({'mesh':pins[index]['mesh'],'vertex':pins[index]['vertex'],'lining':name,
                             'depthMeters':distance,'point':list(point),'pinIndex':int(index)})
    return rows

allids=np.arange(len(rest));baseline={}
for name,model in models.items():
    baseline[name]=query(rest,model,allids)
    print('BASELINE '+name+' '+str(len(baseline[name])),flush=True)
trials=[]
for xhi in [.185,.205,.225]:
    for zhi in [1.29,1.31,1.33]:
        ux=.17;wx=.195;points=tailored(rest,ux,wx)
        alpha=smooth(.12,xhi,np.abs(rest[:,0]))*smooth(1.24,zhi,rest[:,2])*(1-smooth(.32,.40,np.abs(rest[:,0])))
        changed=np.flatnonzero((np.linalg.norm(points-rest,axis=1)>1e-8)|(alpha>1e-8));changed_set=set(changed.tolist());measurements=[]
        for name,model in models.items():
            rows=[r for r in baseline[name] if r['pinIndex'] not in changed_set]+query(points,model,changed,alpha)
            measurements.append({'poseId':name,'insidePins':len({r['pinIndex'] for r in rows}),'deepInsidePins':len({r['pinIndex'] for r in rows if r['depthMeters']>.0005}),
                                 'maxDepthMeters':max([0.]+[r['depthMeters'] for r in rows]),'records':rows})
        trial={'underarmTargetX':ux,'waistTargetX':wx,'weightFullX':xhi,'weightFullZ':zhi,'changedExactPins':len(changed),'maximumRestShiftMeters':float(np.max(np.linalg.norm(points-rest,axis=1))),
               'totalDeepPinCases':sum(r['deepInsidePins'] for r in measurements),'maxDepthMeters':max(r['maxDepthMeters'] for r in measurements),'measurements':measurements}
        trials.append(trial);print(json.dumps({k:v for k,v in trial.items() if k!='measurements'}),flush=True)
        (OUT/'candidate-report-r4-coherent-upperarm.json').write_text(json.dumps({'sourceSha256':hashlib.sha256(SOURCE.read_bytes()).hexdigest(),'sourceUnchanged':True,
            'fitPolicy':'Shared garment position and anatomically coherent upper-arm skin field plus anatomy upper-arm rigid skin: ArmLining rest abs(x)<.335 assigned own Arm100 virtually. No lining rest shape/capsule/pin/bone changes. Candidates are not final source or RIG_PASS.',
            'baseline':baseline,'trials':trials},indent=2),encoding='utf-8')
