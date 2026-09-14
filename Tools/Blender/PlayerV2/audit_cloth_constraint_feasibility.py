"""Necessary edge-length bounds from actual skin targets and authored motion balls."""
import ast,json,hashlib,math
from pathlib import Path
import numpy as np
ROOT=Path(__file__).resolve().parents[3];DATA=ROOT/'Art/PlayerV2/Inspect/ClothBlender/PosedArmFit9f4cd412'
OUT=ROOT/'Art/PlayerV2/Inspect/ClothBlender/UnderarmTailoring'
helper=Path(__file__).with_name('author_underarm_gusset.py')
nodes=[n for n in ast.parse(helper.read_text()).body if isinstance(n,ast.FunctionDef) and n.name in ['smooth','tailored']]
exec(compile(ast.Module(body=nodes,type_ignores=[]),str(helper),'exec'))
topology=json.loads((DATA/'topology-rest.json').read_text());poses=[]
for path in DATA.glob('*.json'):
    d=json.loads(path.read_text())
    if 'poseId' in d:poses.append(d)
rows=[]
for name,data in topology['topology'].items():
    if not data['mobilityMeters']:continue
    original=np.array(data['restVertices']);rest=tailored(original);mobility=np.array(data['mobilityMeters'])
    edges=np.array(sorted({tuple(sorted((a,b))) for t in data['triangles'] for a,b in zip(t,t[1:]+t[:1])}))
    rest_lengths=np.linalg.norm(rest[edges[:,0]]-rest[edges[:,1]],axis=1)
    original_lengths=np.linalg.norm(original[edges[:,0]]-original[edges[:,1]],axis=1)
    allowed=mobility[edges[:,0]]+mobility[edges[:,1]]
    for pose in poses:
        matrices={n:np.array(m)@np.linalg.inv(np.array(topology['boneRestMatrices'][n])) for n,m in pose['bonePoseMatrices'].items()}
        points=[]
        for p,weights in zip(rest,data['deformWeights']):
            hp=np.r_[p,1];points.append(sum((matrices[n]@hp)[:3]*w for n,w in weights.items()))
        points=np.array(points);distance=np.linalg.norm(points[edges[:,0]]-points[edges[:,1]],axis=1)
        minimum=np.maximum(0,distance-allowed);ratio=np.divide(minimum,rest_lengths,out=np.zeros_like(minimum),where=rest_lengths>1e-12)
        baseline=np.array(pose['vertices'][name]);bd=np.linalg.norm(baseline[edges[:,0]]-baseline[edges[:,1]],axis=1)
        br=np.maximum(0,bd-allowed)/np.maximum(original_lengths,1e-30)
        records=[]
        for i in np.argsort(ratio)[-20:][::-1]:
            a,b=edges[i];records.append({'sourceVertices':[int(a),int(b)],'restLengthMeters':float(rest_lengths[i]),
                'skinTargetDistanceMeters':float(distance[i]),'mobilityMeters':[float(mobility[a]),float(mobility[b])],
                'minimumFeasibleLengthMeters':float(minimum[i]),'minimumRequiredStretchRatio':float(ratio[i]),
                'bothPinned':bool(mobility[a]==0 and mobility[b]==0),'restPoints':[rest[a].tolist(),rest[b].tolist()],
                'skinTargetPoints':[points[a].tolist(),points[b].tolist()]})
        rows.append({'poseId':pose['poseId'],'surface':name,'edges':len(edges),'zeroLengthEdges':int(np.sum(rest_lengths<=1e-12)),
            'maximumMinimumRequiredStretchRatio':float(np.max(ratio)), 'edgesNecessarilyAbove1_35':int(np.sum(ratio>1.35)),
            'bothPinnedEdgesAbove1_35':int(np.sum((ratio>1.35)&(mobility[edges[:,0]]==0)&(mobility[edges[:,1]]==0))),
            'baseline9f4MaximumMinimumRequiredStretchRatio':float(np.max(br)),'baseline9f4EdgesAbove1_35':int(np.sum(br>1.35)),
            'worstEdges':records})
report={'status':'MEASURED_NECESSARY_CONSTRAINT_BOUNDS_NOT_PHYSICS_PASS','sourcePoseSha256':topology['sourceSha256'],
    'tailoredSourceSha256':hashlib.sha256((OUT/'DosaV2_GussetTailored.blend').read_bytes()).hexdigest(),
    'formula':'d=distance between actual linear-skinned source endpoints; m=exact maxDistanceA+maxDistanceB; requiredLength=max(0,d-m); minimumRequiredStretch=requiredLength/restEdgeLength.',
    'scope':'Necessary geometric feasibility bound for native maximum-distance balls, no capsule or stiffness assumptions. Ratio>1.35 proves this edge cannot satisfy the fixed physical stretch limit while both motion-ball constraints hold. Ratio<=1.35 does not prove a stable collision-free solve.',
    'worldScale':1,'rows':rows}
(OUT/'constraint-feasibility.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print(json.dumps([{k:v for k,v in r.items() if k!='worstEdges'} for r in rows if r['poseId'].startswith('fixture_')],indent=2))
