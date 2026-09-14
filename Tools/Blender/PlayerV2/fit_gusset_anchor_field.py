"""Joint garment shape/anchor coherence candidates; native constraints unchanged."""
import bpy,json,math,hashlib
from pathlib import Path
import numpy as np
from mathutils import Matrix,Vector
from mathutils.bvhtree import BVHTree
ROOT=Path(__file__).resolve().parents[3];DATA=ROOT/'Art/PlayerV2/Inspect/ClothBlender/PosedArmFit9f4cd412'
OUT=ROOT/'Art/PlayerV2/Inspect/ClothBlender/GussetAnchor';OUT.mkdir(parents=True,exist_ok=True)
SOURCE=ROOT/'Art/PlayerV2/Inspect/ClothBlender/Inputs/Assembled-final-9f4cd412.blend'
topology=json.loads((DATA/'topology-rest.json').read_text());poses=[]
for path in DATA.glob('*.json'):
    obj=json.loads(path.read_text())
    if 'poseId' in obj:poses.append(obj)
bpy.ops.wm.open_mainfile(filepath=str(SOURCE));rig=bpy.data.objects['DosaV2_Rig']
for o in bpy.context.scene.objects:
    if o.name.startswith('CTRL_') and 'AuthoringMode' in o:o['AuthoringMode']=False;o.update_tag()
for p in rig.pose.bones:p.matrix_basis=Matrix.Identity(4)
bpy.context.view_layer.update()
names=['DosaV2_BodyCore','DosaV2_Robe_Combined','DosaV2_SleeveOuter_L','DosaV2_SleeveOuter_R']
meshes={}
for name in names:
    obj=bpy.data.objects[name];mob=obj.data.color_attributes.get('ClothMobility');obj.data.calc_loop_triangles()
    meshes[name]={'rest':np.array([v.co[:] for v in obj.data.vertices]),
        'weights':[{obj.vertex_groups[g.group].name:g.weight for g in v.groups} for v in obj.data.vertices],
        'edges':np.array([list(e.vertices) for e in obj.data.edges]),
        'mobility':np.array([c.color[0] for c in mob.data]) if mob else None}
directions=[Vector(d).normalized() for d in [(1,.371,.219),(.173,1,.293),(.271,.123,1)]]
def smooth(lo,hi,x):
    t=np.clip((x-lo)/(hi-lo),0,1);return t*t*(3-2*t)
def geometry(points,target):
    result=points.copy();x=np.abs(points[:,0]);z=points[:,2]
    under=np.maximum(0,x-target)*smooth(.13,.20,x)*(1-smooth(.26,.33,x))*smooth(.98,1.10,z)*(1-smooth(1.24,1.31,z))
    waist=np.maximum(0,x-.195)*smooth(.17,.24,x)*(1-smooth(.28,.34,x))*smooth(.87,.93,z)*(1-smooth(.99,1.06,z))
    result[:,0]-=np.sign(points[:,0])*np.maximum(under,waist);return result
def coherent(points,weights,zlo,zhi=1.34,xhi=.28):
    x=np.abs(points[:,0]);z=points[:,2]
    alpha=(1-smooth(.20,xhi,x))*(1-smooth(zlo,zhi,z))*smooth(1.0,1.08,z)
    output=[]
    for p,before,a in zip(points,weights,alpha):
        if a<1e-8:output.append(before.copy());continue
        result={n:w*(1-a) for n,w in before.items()};result['Spine02']=result.get('Spine02',0)+a
        result=dict(sorted(result.items(),key=lambda r:r[1],reverse=True)[:4]);s=sum(result.values());output.append({n:w/s for n,w in result.items() if w>1e-8})
    return output
def inside(tree,point):
    countvotes=0
    for d in directions:
        origin=point+d*.000001;n=0
        for _ in range(64):
            hit=tree.ray_cast(origin,d,10)
            if hit[0] is None:break
            n+=1;origin=hit[0]+d*.000005
        countvotes+=n%2
    return countvotes>=2
models={}
for pose in poses:
    trees=[]
    for name,data in topology['topology'].items():
        if 'Lining' in name:trees.append((name,BVHTree.FromPolygons(pose['vertices'][name],data['triangles'],all_triangles=True)))
    matrices={n:np.array(m)@np.linalg.inv(np.array(topology['boneRestMatrices'][n])) for n,m in pose['bonePoseMatrices'].items()}
    models[pose['poseId']]={'matrices':matrices,'trees':trees}
def skin(points,weights,matrices):
    result=np.zeros_like(points);homogeneous=np.column_stack((points,np.ones(len(points))))
    for bone in set(n for row in weights for n in row):
        w=np.array([row.get(bone,0) for row in weights]);result+=(homogeneous@matrices[bone].T)[:,:3]*w[:,None]
    return result
trials=[]
for zhi in [1.34,1.36]:
    for zlo,xhi in [(lo,hi) for lo in [1.18,1.20,1.22,1.24] for hi in [.26,.28,.30]]:
        target=.145
        altered={n:{'rest':geometry(m['rest'],target)} for n,m in meshes.items()}
        for n,m in meshes.items():altered[n]['weights']=m['weights'] if 'Robe' in n else coherent(altered[n]['rest'],m['weights'],zlo,zhi,xhi)
        rows=[]
        for poseid,model in models.items():
            for name,m in meshes.items():
                if m['mobility'] is None:continue
                rest=altered[name]['rest'];points=skin(rest,altered[name]['weights'],model['matrices']);edges=m['edges'];mob=m['mobility']
                lengths=np.linalg.norm(rest[edges[:,0]]-rest[edges[:,1]],axis=1)
                distance=np.linalg.norm(points[edges[:,0]]-points[edges[:,1]],axis=1)
                ratio=np.maximum(0,distance-mob[edges[:,0]]-mob[edges[:,1]])/np.maximum(lengths,1e-30)
                records=[]
                for i in np.flatnonzero(mob==0):
                    p=Vector(points[i])
                    for lining,tree in model['trees']:
                        near,normal,face,d=tree.find_nearest(p)
                        if inside(tree,p):records.append({'vertex':int(i),'lining':lining,'depthMeters':d,'point':list(p)})
                worst=[]
                for i in np.argsort(ratio)[-8:][::-1]:
                    a,b=edges[i];worst.append({'vertices':[int(a),int(b)],'minimumRequiredStretchRatio':float(ratio[i]),'restLengthMeters':float(lengths[i]),
                        'skinTargetDistanceMeters':float(distance[i]),'mobilityMeters':[float(mob[a]),float(mob[b])],
                        'restPoints':[rest[a].tolist(),rest[b].tolist()],'weights':[altered[name]['weights'][a],altered[name]['weights'][b]]})
                rows.append({'poseId':poseid,'surface':name,'exactPinsInside':len(records),'deepPinsInside':sum(r['depthMeters']>.0005 for r in records),
                    'maxInsideDepthMeters':max([0.]+[r['depthMeters'] for r in records]),'records':records,
                    'maximumMinimumRequiredStretchRatio':float(np.max(ratio)),'edgesNecessarilyAbove1_35':int(np.sum(ratio>1.35)),'worstEdges':worst})
        trial={'targetUnderarmX':target,'torsoPlateauEndZ':zlo,'torsoFadeEndZ':zhi,'torsoFadeEndX':xhi,
            'deepPinCases':sum(r['deepPinsInside'] for r in rows),'maxInsideDepthMeters':max(r['maxInsideDepthMeters'] for r in rows),
            'infeasibleEdgeCases':sum(r['edgesNecessarilyAbove1_35'] for r in rows),'maxMinimumRequiredStretchRatio':max(r['maximumMinimumRequiredStretchRatio'] for r in rows),'rows':rows}
        trials.append(trial);print(json.dumps({k:v for k,v in trial.items() if k!='rows'}),flush=True)
        (OUT/'candidate-report-r3-smooth-anchor.json').write_text(json.dumps({'sourceSha256':hashlib.sha256(SOURCE.read_bytes()).hexdigest(),
            'status':'CANDIDATE_SEARCH_GEOMETRY_AND_SKIN_CONSTRAINTS_UNCHANGED','policy':'Same rest geometry and upper torso attachment field on BodyCore/Robe/OuterSleeves; no pin motion-limit, lining, skeleton or solver threshold changes. Top4 skin normalization applied before measurement. No arm influence introduced into waistband.',
            'scriptSha256':hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),'trials':trials},indent=2),encoding='utf-8')
