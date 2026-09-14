"""Independent actual closed ArmLining vs exact sleeve pins in exported poses."""
import ast, json, math, hashlib
from pathlib import Path
import numpy as np
ROOT=Path(__file__).resolve().parents[3]
DIR=ROOT/'Art/PlayerV2/Inspect/ClothBlender/PosedArmFit9f4cd412'
HELPER=Path(__file__).with_name('fit_posed_arm_capsules.py')
# Extract only the pure exact triangle distance/winding routine. Never execute
# the helper's expensive capsule fitting pipeline or use its fitted proxies.
node=next(n for n in ast.parse(HELPER.read_text()).body if isinstance(n,ast.FunctionDef) and n.name=='point_surface_distance_and_inside')
exec(compile(ast.Module(body=[node],type_ignores=[]),str(HELPER),'exec'))
topology=json.loads((DIR/'topology-rest.json').read_text())
rows=[]
for file in sorted(DIR.glob('*.json')):
    pose=json.loads(file.read_text())
    if 'poseId' not in pose:continue
    for side in ['Left','Right']:
        lining='DosaV2_ArmLining_'+side;cloth='DosaV2_SleeveOuter_'+side[0]
        vertices=np.array(pose['vertices'][lining]);triangles=np.array(topology['topology'][lining]['triangles'])
        indices=np.flatnonzero(np.array(topology['topology'][cloth]['mobilityMeters'])==0)
        points=np.array(pose['vertices'][cloth])[indices]
        distances,inside=point_surface_distance_and_inside(points,vertices,triangles)
        rows.append({'poseId':pose['poseId'],'lining':lining,'totalExactPins':len(indices),
            'actualInsidePins':int(np.sum(inside)),'actualInsideDeeperThanHalfMm':int(np.sum(inside&(distances>.0005))),
            'maxActualInsideDepthMeters':max([0.]+distances[inside].tolist()),
            'records':[{'sourceVertex':int(indices[i]),'pointBlender':points[i].tolist(),'depthMeters':float(distances[i]),
                'sourceRestPointBlender':topology['topology'][cloth]['restVertices'][int(indices[i])],
                'deformWeights':topology['topology'][cloth]['deformWeights'][int(indices[i])]} for i in np.flatnonzero(inside)]})
    print('ACTUAL '+pose['poseId'],flush=True)
report={'sourceSha256':topology['sourceSha256'],'method':'Every exact-source sleeve pin against corresponding actual closed evaluated ArmLining triangles: generalized solid-angle winding abs > .5. Triangle nearest distance independent of capsule.',
    'helperSha256':hashlib.sha256(HELPER.read_bytes()).hexdigest(),'rows':rows}
(DIR/'actual-arm-pin-clearance-allposes.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print(json.dumps([{k:r[k] for k in ['poseId','lining','actualInsidePins','maxActualInsideDepthMeters']} for r in rows if r['actualInsidePins']],indent=2))
