"""Record bounded shoulder evidence and independently inspect saved primal retries."""
import hashlib,json,math,shutil
from pathlib import Path
import numpy as np
ROOT=Path(__file__).resolve().parents[3];OUT=ROOT/'Art/PlayerV2/Inspect/ClothBlender/ShoulderComplete'
def sha(p):return hashlib.sha256(Path(p).read_bytes()).hexdigest()
rows=[]
for side in ['L','R']:
    report=json.loads((OUT/(side+'-fixed-retry.json')).read_text());data=np.load(OUT/('DosaV2_SleeveOuter_'+side+'.npz'));rest=data['rest'].tolist();mob=data['mobility'].tolist()
    for case in report['cases']:
        file=Path(case['witnessPath']);assert sha(file)==case['witnessSha256'];q=np.load(file).tolist();p=data['posePoints'][case['poseIndex']].tolist()
        ratios=[math.dist(q[int(a)],q[int(b)])/math.dist(rest[int(a)],rest[int(b)]) for a,b in data['edges']]
        balls=[math.dist(a,b)-m for a,b,m in zip(q,p,mob)];pins=[math.dist(a,b) for a,b,m in zip(q,p,mob) if m==0]
        row={'mesh':side,'pose':case['pose'],'witnessSha256':sha(file),'actualMaximumEdgeRatio':max(ratios),'maximumMotionBallExcessMeters':max(0,max(balls)),
          'maximumExactPinErrorMeters':max(pins),'method':'Independent scalar Python math.dist loops over every saved vertex and triangle edge; no CVXPY constraint expressions or reported residual values reused.'}
        assert row['actualMaximumEdgeRatio']<=1.35 and row['maximumMotionBallExcessMeters']<=1e-6 and row['maximumExactPinErrorMeters']<=1e-6;rows.append(row)
(OUT/'retry-independent-primal-check.json').write_text(json.dumps({'status':'FOUR_FIXED_LIMIT_PRIMAL_WITNESSES_VERIFIED_NOT_PHYSICS','cases':rows},indent=2),encoding='utf-8')
recipes=OUT/'Recipes';recipes.mkdir(exist_ok=True)
script_names=['complete_shoulder_connection_candidate.py','build_near_shoulder_lining_lod.py','render_shoulder_complete.py','render_near_lining_comparison.py','diagnose_near_lining_error.py','audit_shoulder_handoff_consistency.py','audit_global_cloth_paths.py','audit_cloth_convex_feasibility.py','freeze_shoulder_candidate.py']
for name in script_names:shutil.copyfile(Path(__file__).with_name(name),recipes/name)
inputs=[ROOT/'Art/PlayerV2/Inspect/ClothBlender/GlobalEnvelope/DosaV2_GlobalClothEnvelope.blend',ROOT/'Art/PlayerV2/Inspect/ClothBlender/ShoulderConnection/DosaV2_ShoulderConnection_Candidate.blend',
 ROOT/'Art/PlayerV2/Inspect/ClothBlender/FoldedGusset/Inputs/static-pose-definitions.json']
inputs+=sorted((ROOT/'Art/PlayerV2/Inspect/ClothBlender/FoldedGusset/IntermediatePoseData').glob('*.json'))
inputs+=[Path(__file__).with_name(n) for n in ['fit_gusset_anchor_field.py','audit_triangle_crossings.py','build_brush.py','diagnose_cloth_blender.py']]
files=[p for p in OUT.rglob('*') if p.is_file() and p.name!='frozen-manifest.json' and p.suffix in ['.blend','.json','.npz','.npy','.png','.md','.py']]
manifest={'status':'FROZEN_BOUNDED_SHOULDER_CANDIDATE_NOT_NATIVE_CLOTH_OR_RIG_PASS','files':[{'path':str(p.relative_to(ROOT)).replace('\\','/'),'sha256':sha(p),'bytes':p.stat().st_size} for p in sorted(files)],
 'readOnlyInputs':[{'path':str(p.relative_to(ROOT)).replace('\\','/'),'sha256':sha(p)} for p in inputs],
 'rebuild':'Run original Tools/Blender/PlayerV2 paths after matching Recipes hashes; recipe copies retain project-root-relative paths and are not standalone executables. Do not rebuild over these frozen files; use a new output directory.'}
(OUT/'frozen-manifest.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8');print(json.dumps({'files':len(files),'frozenManifestSha256':sha(OUT/'frozen-manifest.json'),'independentRetryCases':len(rows)},indent=2))
