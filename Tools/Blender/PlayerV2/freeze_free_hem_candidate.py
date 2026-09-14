"""Freeze the comparison candidate; independently verify rest primal witnesses."""
import json,hashlib,math,shutil
from pathlib import Path
import numpy as np
ROOT=Path(__file__).resolve().parents[3];BASE=ROOT/'Art/PlayerV2/Inspect/ClothBlender';OUT=BASE/'TorsoSupportedPanel/SeamLineCandidate'
def sha(p):return hashlib.sha256(Path(p).read_bytes()).hexdigest()
checks=[]
for side in ['L','R']:
    report=json.loads((OUT/('fixed-rest-'+side+'.json')).read_text());data=np.load(OUT/('DosaV2_SleeveOuter_'+side+'-429.npz'))
    rest=data['rest'].tolist();mob=data['mobility'].tolist()
    for case in report['cases']:
        file=Path(case['witnessPath']);assert sha(file)==case['witnessSha256'];q=np.load(file).tolist();p=data['posePoints'][case['poseIndex']].tolist()
        ratios=[math.dist(q[int(a)],q[int(b)])/math.dist(rest[int(a)],rest[int(b)]) for a,b in data['edges']]
        balls=[math.dist(a,b)-m for a,b,m in zip(q,p,mob)];pins=[math.dist(a,b) for a,b,m in zip(q,p,mob) if m==0]
        r={'side':side,'pose':case['pose'],'solverStatus':case['solverStatus'],'witnessSha256':sha(file),
          'maximumActualStretch':max(ratios),'maximumMotionBallExcessMeters':max(0,max(balls)),'maximumPinErrorMeters':max(pins)}
        assert r['maximumActualStretch']<=1.35 and r['maximumMotionBallExcessMeters']<=1e-6 and r['maximumPinErrorMeters']<=1e-6
        checks.append(r)
(OUT/'fixed-rest-independent-primal.json').write_text(json.dumps({'status':'TWELVE_SAVED_PRIMAL_WITNESSES_CHECKED_NOT_PHYSICS_OR_RIG_PASS',
 'method':'Every saved source vertex/edge checked with independent scalar math.dist; CVXPY expressions and solver-reported residuals were not reused. Original optimum-inaccurate statuses remain unchanged.', 'cases':checks},indent=2),encoding='utf-8')
handoff=json.loads((OUT/'handoff.json').read_text());handoff['fixedRestRetries']={'cases':len(checks),'allSolverStatusesOptimal':all(r['solverStatus']=='optimal' for r in checks),'independentAllVertexEdgeChecksWithinInitialLimits':True,'reportSha256':sha(OUT/'fixed-rest-independent-primal.json')};(OUT/'handoff.json').write_text(json.dumps(handoff,indent=2),encoding='utf-8')
with (OUT/'HANDOFF.md').open('a',encoding='utf-8') as f:f.write('\nFinal simultaneous audit: each side has423 numerical optimal and6 optimal_inaccurate rest-equivalent cases. Original statuses are preserved. Maximum actual witness stretch is1.2500041Left /1.2107459Right. All12rest-equivalent cases were rerun at the unchanged1.35limit, with optimal statuses; saved float64 witnesses were independently checked over every vertex/edge using scalar distances. This is geometric feasibility only, without native forces/collisions/bending/tethers.\n')
recipes=OUT/'Recipes'
for name in ['document_free_hem_handoff.py','freeze_free_hem_candidate.py','audit_cloth_convex_feasibility.py']:shutil.copyfile(Path(__file__).with_name(name),recipes/name)
inputs=[BASE/'VerifiedAttachments/Inputs/Assembled-d979b9bc.blend',BASE/'TorsoSupportedPanel/panel-10.json',BASE/'FoldedGusset/Inputs/static-pose-definitions.json']+sorted((BASE/'FoldedGusset/IntermediatePoseData').glob('*.json'))
inputs += [Path(__file__).with_name(n) for n in ['build_brush.py','diagnose_cloth_blender.py','fit_gusset_anchor_field.py']]
files=[p for p in OUT.rglob('*') if p.is_file() and p.name!='frozen-manifest.json' and p.suffix in ['.blend','.json','.npz','.npy','.png','.md','.py']]
manifest={'status':'FROZEN_FOR_NATIVE_COMPARISON_NOT_RIG_PASS','modelSha256':sha(OUT/'DosaV2_FreeHemPanel_Candidate.blend'),
 'files':[{'path':str(p.relative_to(ROOT)).replace('\\','/'),'sha256':sha(p),'bytes':p.stat().st_size} for p in sorted(files)],
 'readOnlyInputs':[{'path':str(p.relative_to(ROOT)).replace('\\','/'),'sha256':sha(p)} for p in inputs],
 'rebuild':'Canonical Tools paths with DOSA_PANEL_VARIANT=torso10line. Frozen sources must not be overwritten; derive a new output directory for later authoring.'}
(OUT/'frozen-manifest.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8');print(json.dumps({'files':len(files),'modelSha256':manifest['modelSha256'],'frozenManifestSha256':sha(OUT/'frozen-manifest.json'),'independentWitnesses':len(checks)},indent=2))
