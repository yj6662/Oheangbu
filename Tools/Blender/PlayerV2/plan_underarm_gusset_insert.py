"""Measure actual fabric slack required before adding a folded local gusset."""
import ast,json,hashlib
from pathlib import Path
import numpy as np
ROOT=Path(__file__).resolve().parents[3];OUT=ROOT/'Art/PlayerV2/Inspect/ClothBlender/CoherentArmGusset';OUT.mkdir(parents=True,exist_ok=True)
DATA=ROOT/'Art/PlayerV2/Inspect/ClothBlender/PosedArmFit9f4cd412';SOLUTION=ROOT/'Art/PlayerV2/Inspect/ClothBlender/GussetAnchor/structural-weight-solution.json'
HELPER=ROOT/'Art/PlayerV2/Inspect/ClothBlender/GussetAnchor/candidate-script-r3.py'
nodes=[n for n in ast.parse(HELPER.read_text()).body if isinstance(n,ast.FunctionDef) and n.name in ['geometry','coherent','skin','smooth']]
exec(compile(ast.Module(body=nodes,type_ignores=[]),str(HELPER),'exec'))
t=json.loads((DATA/'topology-rest.json').read_text());solution=json.loads(SOLUTION.read_text());poses=[]
for directory in [DATA,ROOT/'Art/PlayerV2/Inspect/ClothBlender/FeasibleGusset/IntermediatePoseData']:
    for p in directory.glob('*.json'):
        data=json.loads(p.read_text())
        if 'poseId' in data:poses.append(data)
for p in (ROOT/'Art/PlayerV2/Inspect/ClothBlender/ContinuousGusset/IntermediatePoseData').glob('combined_reach_*.json'):poses.append(json.loads(p.read_text()))
models=[{n:np.array(m)@np.linalg.inv(np.array(t['boneRestMatrices'][n])) for n,m in pose['bonePoseMatrices'].items()} for pose in poses]
rows=[]
for name in ['DosaV2_SleeveOuter_L','DosaV2_SleeveOuter_R']:
    m=t['topology'][name];rest=geometry(np.array(m['restVertices']),.145);weights=coherent(rest,m['deformWeights'],1.24,1.34,.28)
    for row in solution['variables']:
        if row['mesh']==name:weights[row['vertex']]=row['afterWeights']
    mob=np.array(m['mobilityMeters']);triangles=m['triangles'];edges=np.array(sorted({tuple(sorted((a,b))) for tr in triangles for a,b in zip(tr,tr[1:]+tr[:1])}))
    length=np.linalg.norm(rest[edges[:,0]]-rest[edges[:,1]],axis=1);max_required=np.zeros(len(edges));max_distance=np.zeros(len(edges));point_samples=[]
    for matrices in models:
        p=skin(rest,weights,matrices);point_samples.append(p);d=np.linalg.norm(p[edges[:,0]]-p[edges[:,1]],axis=1)
        max_required=np.maximum(max_required,np.maximum(0,d-mob[edges[:,0]]-mob[edges[:,1]]));max_distance=np.maximum(max_distance,d)
    ratio=max_required/length;bad=np.flatnonzero(ratio>1.30);records=[]
    for i in bad:
        a,b=edges[i];mid=(rest[a]+rest[b])*.5;direction=(rest[b]-rest[a])/length[i]
        normal=np.array([0.,-1. if mid[1]<0 else 1.,0.]);normal-=direction*np.dot(normal,direction)
        if np.linalg.norm(normal)<.1:
            normal=np.array([1. if mid[0]>0 else -1.,0.,0.]);normal-=direction*np.dot(normal,direction)
        normal/=np.linalg.norm(normal);required=max_required[i]/1.10
        height=np.sqrt(max(0,(required*.5)**2-(length[i]*.5)**2));height=max(.004,height)
        folded=mid+normal*height
        records.append({'sourceVertices':[int(a),int(b)],'bothOriginalPins':bool(mob[a]==0 and mob[b]==0),
          'sourceRestLengthMeters':float(length[i]),'originalMaximumRequiredStretch':float(ratio[i]),
          'maximumRequiredEndpointDistanceMeters':float(max_required[i]),'maximumSkinEndpointDistanceMeters':float(max_distance[i]),
          'newFoldedPathLengthMeters':float(np.linalg.norm(folded-rest[a])+np.linalg.norm(folded-rest[b])),
          'foldHeightMeters':float(height),'foldDirection':normal.tolist(),'midpoint':folded.tolist(),
          'restPoints':[rest[a].tolist(),rest[b].tolist()],'oldMobilityMeters':[float(mob[a]),float(mob[b])]})
    rows.append({'surface':name,'originalVertices':len(rest),'originalTriangles':len(triangles),'newRidgeVertices':len(records),
      'expectedAdditionalTriangles':2*len(records),'maximumFoldHeightMeters':max([0.]+[r['foldHeightMeters'] for r in records]),
      'restBounds':[rest.min(axis=0).tolist(),rest.max(axis=0).tolist()],
      'unmodifiedMaximumRequiredStretch':float(max([0.]+ratio[ratio<=1.30].tolist())), 'folds':records})
report={'status':'PHYSICAL_SLACK_GEOMETRY_PREFLIGHT_NOT_AUTHORING_OR_PHYSICS_PASS','solutionSha256':hashlib.sha256(SOLUTION.read_bytes()).hexdigest(),
  'poseCount':len(poses),'method':'Every original edge with necessary stretch >1.30 receives proposed real folded path whose rest length covers max posed endpoint distance minus unchanged original endpoint mobility, at target1.10. This checks total path as well as individual edges, so adding free midpoint constraints cannot hide an impossible endpoint span.',
  'rows':rows}
(OUT/'folded-gusset-preflight.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print(json.dumps([{k:v for k,v in row.items() if k!='folds'} for row in rows],indent=2))
