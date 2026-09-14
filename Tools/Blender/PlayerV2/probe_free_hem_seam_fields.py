"""Read-only local common skin-field candidates for an actual garment seam."""
import bpy,json,ast,math
import numpy as np
from pathlib import Path
from mathutils import Vector
from mathutils.bvhtree import BVHTree
ROOT=Path(__file__).resolve().parents[3];BASE=ROOT/'Art/PlayerV2/Inspect/ClothBlender';OUT=BASE/'FreeHemPanel';g=json.loads((OUT/'Trial01/rest-geometry.json').read_text());models=g['meshes'];ledger=json.loads((OUT/'Trial01/authoring-ledger.json').read_text())
for m in models.values():m['rest']=np.array(m['points']);m['triangles']=np.array(m['triangles']);m['mobility']=np.array(m['mobility']) if m['mobility'] is not None else None
rinv={n:np.linalg.inv(np.array(b['matrix'])) for n,b in g['bones'].items()};POSE=BASE/'FoldedGusset';poses=[]
for d in json.loads((POSE/'Inputs/static-pose-definitions.json').read_text())['poses']:poses.append((d['id'],{n:np.array(m)@rinv[n] for n,m in d['boneMatricesRigLocal'].items()}))
for file,label in [('fixture_grip_settle_00','fixture_rest_settle'),('fixture_grip_settle_100','fixture_grip_settle'),('fixture_raised_arms_settle_100','fixture_raised_arms_settle')]:
    d=json.loads((POSE/'IntermediatePoseData'/(file+'.json')).read_text());poses.append((label,{n:np.array(m)@rinv[n] for n,m in d['bonePoseMatrices'].items()}))
path=Path(__file__).with_name('fit_gusset_anchor_field.py');nodes=[n for n in ast.parse(path.read_text()).body if isinstance(n,ast.FunctionDef) and n.name in ['skin','inside']];exec(compile(ast.Module(body=nodes,type_ignores=[]),str(path),'exec'));directions=[Vector(d).normalized() for d in [(1,.371,.219),(.173,1,.293),(.271,.123,1)]]
anatomy=[]
for pose,mat in poses:
    a={}
    for n,m in models.items():
        if 'Lining' not in n:continue
        p=skin(m['rest'],m['weights'],mat);a[n]={'tree':BVHTree.FromPolygons(p.tolist(),m['triangles'].tolist(),all_triangles=True),'lo':np.min(p,axis=0),'hi':np.max(p,axis=0)}
    anatomy.append(a)
names=['DosaV2_BodyCore','DosaV2_SleeveOuter_L','DosaV2_SleeveOuter_R'];bone_names=sorted({n for name in names for w in models[name]['weights'] for n in w})
poolpoints=np.concatenate([models[n]['rest'] for n in names]);poolweights=np.array([[w.get(n,0) for n in bone_names] for name in names for w in models[name]['weights']])
centers=np.array([models['DosaV2_SleeveOuter_L']['rest'][818],models['DosaV2_SleeveOuter_R']['rest'][906],models['DosaV2_SleeveOuter_R']['rest'][946]])
core_ids=sorted({i for r in ledger['transferredFaces'] for i in r['outerVertices']});core_points=models['DosaV2_SleeveOuter_R']['rest'][core_ids]
def make(sigma,radius):
    result={}
    for name in names:
        m=models[name];result[name]=[]
        for p,orig in zip(m['rest'],m['weights']):
            distance=np.min(np.linalg.norm(core_points-p,axis=1));t=np.clip((distance-.012)/(radius-.012),0,1);alpha=1-t*t*(3-2*t)
            if alpha<1e-10:result[name].append(orig);continue
            if sigma<0:
                # A reinforced torso-to-shoulder sewing band is attached to the
                # chest/shoulder support, not to the rotating upper-arm volume.
                average=np.zeros(len(bone_names));side='Left' if p[0]>0 else 'Right'
                blend=1.
                average[bone_names.index('Spine02')]=1-blend+blend*(1-ARM)/3
                average[bone_names.index(side+'Shoulder')]=blend*(1-ARM)/3
                average[bone_names.index('Neck')]=blend*(1-ARM)/3
                average[bone_names.index(side+'Arm')]=blend*ARM
            else:
                d=np.linalg.norm(poolpoints-p,axis=1);idx=d<sigma*2.5;coeff=np.exp(-.5*(d[idx]/sigma)**2);average=(coeff@poolweights[idx])/sum(coeff)
            prior=np.array([orig.get(n,0) for n in bone_names]);w=prior*(1-alpha)+average*alpha;keep=np.argsort(w)[-4:];w2={bone_names[i]:float(w[i]/sum(w[keep])) for i in keep if w[i]>1e-9};result[name].append(w2)
    return result
trials=[];original_rest={n:models[n]['rest'].copy() for n in names}
for ARM,ZLOW in [(a,z) for a in [.15,.30,.42,.55] for z in [0.]]:
    shift=0.
    sigma=-1;radius=.055
    for name in names:
        p=original_rest[name].copy();d=np.linalg.norm(p-centers[2],axis=1);kernel=np.maximum(0,1-(d/.035)**2)**2;p[:,1]-=shift*kernel;models[name]['rest']=p
    weights=make(sigma,radius);rows=[]
    for k,(pose,mat) in enumerate(poses):
        for name in names[1:]:
            m=models[name];p=skin(m['rest'],weights[name],mat);e=np.array(sorted({tuple(sorted((int(a),int(b)))) for t in m['triangles'] for a,b in zip(t,np.roll(t,-1))}));mob=m['mobility'];rest=np.linalg.norm(m['rest'][e[:,0]]-m['rest'][e[:,1]],axis=1);ratio=np.maximum(0,np.linalg.norm(p[e[:,0]]-p[e[:,1]],axis=1)-mob[e[:,0]]-mob[e[:,1]])/rest
            hits=[];pins=np.flatnonzero(mob==0)
            for n,a in anatomy[k].items():
                for i in pins[np.all(p[pins]>=a['lo'],axis=1)&np.all(p[pins]<=a['hi'],axis=1)]:
                    q=Vector(p[i])
                    if inside(a['tree'],q):hits.append({'vertex':int(i),'anatomy':n,'depth':a['tree'].find_nearest(q)[3]})
            rows.append({'pose':pose,'cloth':name,'maxNecessaryEdge':float(np.max(ratio)),'edge':e[int(np.argmax(ratio))].tolist(),'pinsInside':hits})
    r={'armBlendMaximum':ARM,'blendStartZ':ZLOW,'blendEndZ':1.31,'frontDartMeters':shift,'sigmaMeters':sigma,'regionRadiusMeters':radius,'maxNecessaryEdge':max(r['maxNecessaryEdge'] for r in rows),'pinInsideCases':sum(len(r['pinsInside']) for r in rows),'maxInsideDepth':max([0]+[h['depth'] for r in rows for h in r['pinsInside']]),'rows':rows};trials.append(r);print(json.dumps({k:v for k,v in r.items() if k!='rows'}),flush=True)
    (OUT/'constant-segment-probe.json').write_text(json.dumps({'status':'READ_ONLY_COMMON_PALETTE_ATTACHMENT_SEGMENT_NO_MODEL_SAVED','segmentCoreOuterVertices':core_ids,'trials':trials},indent=2),encoding='utf-8')
