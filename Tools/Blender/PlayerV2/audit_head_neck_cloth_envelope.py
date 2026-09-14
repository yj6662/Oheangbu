"""Read-only: source-bound actual cloth skin targets versus additional rigid Head capsules."""
from pathlib import Path
import numpy as np,json,hashlib,argparse
ROOT=Path(__file__).resolve().parents[3]
BASE=ROOT/'Art/PlayerV2/Inspect/ClothBlender/FoldedGusset'
DEFAULT=ROOT/'Art/PlayerV2/Inspect/ClothBlender/GlobalEnvelope/DenseData'
OUT=ROOT/'Art/PlayerV2/Inspect/HeadNeckCollision/ClothEnvelope'
p=argparse.ArgumentParser();p.add_argument('--data',default=str(DEFAULT));p.add_argument('--label',default='CurrentGlobalEnvelope429');args=p.parse_args()
DATA=Path(args.data);DEST=OUT/args.label;DEST.mkdir(parents=True,exist_ok=True)
def sha(path):return hashlib.sha256(path.read_bytes()).hexdigest()
plan_path=ROOT/'Art/PlayerV2/Staging/Code/HeadNeckCollision/head-neck-capsules.json'
plan=json.loads(plan_path.read_text());gpath=ROOT/'Art/PlayerV2/Inspect/ClothBlender/ShoulderConnection/geometry-rest.json'
g=json.loads(gpath.read_text());rest=np.array(g['boneRestMatrices']['Head']);rinv=np.linalg.inv(rest)
head_rest_error=float(np.abs(rest-np.array(plan['sourceHeadRestMatrix'])).max());assert head_rest_error<1e-6,head_rest_error
definitions=BASE/'Inputs/static-pose-definitions.json';matrices=[];names=[]
for i,d in enumerate(json.loads(definitions.read_text())['poses']):
    matrices.append(np.array(d['boneMatricesRigLocal']['Head'])@rinv);names.append(d.get('name',d.get('id',str(i))))
extras=['fixture_grip_settle_00','fixture_grip_settle_100','fixture_raised_arms_settle_100']
pose_paths=[BASE/'IntermediatePoseData'/(n+'.json') for n in extras]+sorted((BASE/'IntermediatePoseData').glob('*.json'))
for file in pose_paths:
    d=json.loads(file.read_text());matrices.append(np.array(d['bonePoseMatrices']['Head'])@rinv);names.append(file.stem)
matrices=np.array(matrices);scale_error=float(np.abs(np.linalg.svd(matrices[:,:3,:3],compute_uv=False)-1).max());assert scale_error<1e-5,scale_error
caps=[]
for cap in plan['capsules']:
    start=np.einsum('pij,j->pi',matrices,np.r_[cap['start'],1])[:,:3]
    end=np.einsum('pij,j->pi',matrices,np.r_[cap['end'],1])[:,:3]
    caps.append((cap,start,end))
reports=[]
for model in ['DosaV2_SleeveOuter_L','DosaV2_SleeveOuter_R','DosaV2_Robe_Combined']:
    file=DATA/(model+'.npz')
    if not file.exists() and model=='DosaV2_Robe_Combined':file=ROOT/'Art/PlayerV2/Inspect/ClothBlender/GlobalPathFeasibility'/(model+'.npz')
    if not file.exists():continue
    file_sha=sha(file);data=np.load(file);points=np.array(data['posePoints'],dtype=float);mobility=np.array(data['mobility'],dtype=float);pose_names=data['poseNames'].astype(str)
    assert points.shape[0]==len(matrices),(model,points.shape,len(matrices))
    assert points.shape[1]==len(mobility);assert np.all(np.isfinite(points)) and np.all(mobility>=0)
    signed=[]
    for cap,a,b in caps:
        delta=b-a;d=points-a[:,None,:];t=np.clip(np.einsum('pvi,pi->pv',d,delta)/np.maximum(np.einsum('pi,pi->p',delta,delta),1e-20)[:,None],0,1)
        signed.append(np.linalg.norm(d-t[:,:,None]*delta[:,None,:],axis=2)-cap['radius'])
    signed=np.array(signed);union=signed.min(axis=0);closest=signed.argmin(axis=0);fixed=mobility==0;free=~fixed
    pin_hits=(union<0)&fixed[None,:];free_center_hits=(union<0)&free[None,:]
    free_envelope_overlap=(union-mobility[None,:]<0)&free[None,:]
    # This detects a proven impossible mobility ball inside at least one convex capsule. Its converse is not claimed.
    trapped=(signed+mobility[None,None,:]<0).any(axis=0)&free[None,:]
    rows=[]
    for pi in range(len(points)):
        ids=np.where(pin_hits[pi]|trapped[pi])[0]
        for vi in ids:
            ci=int(closest[pi,vi]);cap,a,b=caps[ci]
            rows.append({'poseIndex':pi,'poseName':str(pose_names[pi]),'matrixDefinition':str(names[pi]),'sourceVertex':int(vi),
                'kind':'FIXED_PIN_INSIDE' if fixed[vi] else 'MOBILITY_BALL_FULLY_INSIDE_ONE_CAPSULE',
                'signedGapM':float(union[pi,vi]),'mobilityM':float(mobility[vi]),'capsule':cap['name'],
                'skinTargetBlender':points[pi,vi].tolist(),'restPointBlender':data['rest'][vi].tolist(),
                'capsuleStartBlender':a[pi].tolist(),'capsuleEndBlender':b[pi].tolist(),'radiusM':cap['radius']})
    rows.sort(key=lambda r:r['signedGapM'])
    free_hits=[]
    for pi,vi in np.argwhere(free_center_hits):
        ci=int(closest[pi,vi]);cap,a,b=caps[ci]
        free_hits.append({'poseIndex':int(pi),'poseName':str(pose_names[pi]),'matrixDefinition':str(names[pi]),'sourceVertex':int(vi),
            'signedGapM':float(union[pi,vi]),'mobilityM':float(mobility[vi]),'singleCapsuleEscapeMarginM':float(union[pi,vi]+mobility[vi]),
            'capsule':cap['name'],'skinTargetBlender':points[pi,vi].tolist(),'restPointBlender':data['rest'][vi].tolist()})
    free_hits.sort(key=lambda r:r['signedGapM'])
    perpose=[{'poseIndex':pi,'poseName':str(pose_names[pi]),'matrixDefinition':str(names[pi]),
        'fixedPinsInside':int(pin_hits[pi].sum()),'freeSkinTargetsInside':int(free_center_hits[pi].sum()),
        'freeEnvelopeOverlaps':int(free_envelope_overlap[pi].sum()),'freeMobilityBallsTrapped':int(trapped[pi].sum()),
        'minimumFixedPinGapM':float(union[pi,fixed].min()) if fixed.any() else None,
        'minimumFreeTargetGapM':float(union[pi,free].min()) if free.any() else None} for pi in range(len(points))]
    maximum=max([0]+[-r['signedGapM'] for r in rows if r['kind']=='FIXED_PIN_INSIDE'])
    report={'model':model,'dataPath':str(file),'dataSha256':file_sha,'poseCount':len(points),'sourceVertices':len(mobility),'fixedPins':int(fixed.sum()),
        'fixedPinInsideSamples':int(pin_hits.sum()),'fixedPinInsideUniqueVertices':int(pin_hits.any(axis=0).sum()),
        'maximumFixedPinPenetrationM':maximum,'minimumFixedPinGapM':float(union[:,fixed].min()) if fixed.any() else None,
        'freeSkinTargetInsideSamples':int(free_center_hits.sum()),'freeEnvelopeOverlapSamples':int(free_envelope_overlap.sum()),
        'freeMobilityBallTrappedSamples':int(trapped.sum()),'fixedPinPerCapsule':{c[0]['name']:int(((signed[i]<0)&fixed[None,:]).sum()) for i,c in enumerate(caps)},
        'minimumFreeSkinTargetGapM':float(union[:,free].min()) if free.any() else None,
        'freeSkinTargetInsideRecords':free_hits,'records':rows,'perPose':perpose}
    assert sha(file)==file_sha,'Concurrent source data write detected'
    reports.append(report)
summary={'status':'NO_FIXED_PIN_OR_SINGLE_CAPSULE_TRAPPED_BALL_CONFLICT' if reports and all(r['fixedPinInsideSamples']==0 and r['freeMobilityBallTrappedSamples']==0 for r in reports) else 'WAIT_FIXED_PIN_OR_MOBILITY_CONSTRAINT_CONFLICT',
    'rigPass':False,'label':args.label,'planPath':str(plan_path),'planSha256':sha(plan_path),'headSourceSha256':plan['sourceSha256'],
    'restMatricesPath':str(gpath),'restMatricesSha256':sha(gpath),'headRestMatrixMaximumDifference':head_rest_error,'posedHeadMaximumScaleError':scale_error,
    'poseDefinitionsSha256':sha(definitions),'poseCount':len(matrices),'models':[r['model'] for r in reports],
    'missingModels':[m for m in ['DosaV2_SleeveOuter_L','DosaV2_SleeveOuter_R','DosaV2_Robe_Combined'] if not any(r['model']==m for r in reports)],
    'fixedPinInsideSamples':sum(r['fixedPinInsideSamples'] for r in reports),
    'freeMobilityBallTrappedSamples':sum(r['freeMobilityBallTrappedSamples'] for r in reports),
    'maximumFixedPinPenetrationM':max([0]+[r['maximumFixedPinPenetrationM'] for r in reports]),
    'method':'Read-only actual float32-authored rest, normalized original skin weights and stored429 bone poses from GlobalEnvelope DenseData. Each new capsule follows HeadPose*inverseHeadRest. Fixed pins use exact mobility==0; all negative signed gaps are retained without a pass tolerance. Free envelope overlap is reported as potential contact, while a whole mobility ball inside one capsule proves a forbidden target constraint. Does not simulate Cloth or prove union-complement feasibility.',
    'reports':reports}
(DEST/'report.json').write_text(json.dumps(summary,indent=2));print(json.dumps({k:v for k,v in summary.items() if k!='reports'},indent=2))
