import numpy as np,json,hashlib
from pathlib import Path
ROOT=Path(__file__).resolve().parents[3];BASE=ROOT/'Art/PlayerV2/Inspect/ClothBlender/FoldedGusset';OUT=ROOT/'Art/PlayerV2/Inspect/ClothBlender/GlobalGusset';DEST=OUT/'DenseData';DEST.mkdir(parents=True,exist_ok=True)
g=json.loads((ROOT/'Art/PlayerV2/Inspect/ClothBlender/ShoulderConnection/geometry-rest.json').read_text());rinv={n:np.linalg.inv(np.array(m)) for n,m in g['boneRestMatrices'].items()};pose_matrices=[]
for d in json.loads((BASE/'Inputs/static-pose-definitions.json').read_text())['poses']:pose_matrices.append({n:np.array(m)@rinv[n] for n,m in d['boneMatricesRigLocal'].items()})
for fname in ['fixture_grip_settle_00','fixture_grip_settle_100','fixture_raised_arms_settle_100']:
    p=json.loads((BASE/'IntermediatePoseData'/(fname+'.json')).read_text());pose_matrices.append({n:np.array(m)@rinv[n] for n,m in p['bonePoseMatrices'].items()})
for file in sorted((BASE/'IntermediatePoseData').glob('*.json')):
    p=json.loads(file.read_text());pose_matrices.append({n:np.array(m)@rinv[n] for n,m in p['bonePoseMatrices'].items()})
for name in ['DosaV2_SleeveOuter_L','DosaV2_SleeveOuter_R']:
    before=np.load(ROOT/'Art/PlayerV2/Inspect/ClothBlender/GlobalPathFeasibility'/(name+'.npz'));after=np.load(OUT/(name+'-candidate.npz'));delta=after['rest']-before['rest'];weights=g['models'][name]['weights'];change=np.zeros_like(before['posePoints'])
    for bone in set(n for w in weights for n in w):
        w=np.array([r.get(bone,0) for r in weights]);matrix=np.array([p[bone][:3,:3] for p in pose_matrices]);change+=np.einsum('pij,vj->pvi',matrix,delta)*w[None,:,None]
    np.savez_compressed(DEST/(name+'.npz'),rest=after['rest'],edges=before['edges'],mobility=before['mobility'],posePoints=before['posePoints']+change,poseNames=before['poseNames'])
    print('DENSE '+name,flush=True)
(DEST/'manifest.json').write_text(json.dumps({'sourceSha256':hashlib.sha256((OUT/'DosaV2_GlobalClothSlack.blend').read_bytes()).hexdigest(),'poseCount':len(pose_matrices),'method':'Unchanged normalized skin weights and fixed actual bone matrices; all dense poses recomputed from changed rest garment vertices. Actual Blender25 output independently compared in source-report.'},indent=2),encoding='utf-8')
