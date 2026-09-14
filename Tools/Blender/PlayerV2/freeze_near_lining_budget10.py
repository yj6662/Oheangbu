"""Record visual limitations and freeze a near-only mesh-data override."""
import json,hashlib,shutil
from pathlib import Path
import numpy as np
from PIL import Image
ROOT=Path(__file__).resolve().parents[3];BASE=ROOT/'Art/PlayerV2/Inspect/ClothBlender';OUT=BASE/'NearLiningBudget10';VIEW=OUT/'Views'
def sha(p):return hashlib.sha256(Path(p).read_bytes()).hexdigest()
report=json.loads((OUT/'near-lod-report.json').read_text());integrity=json.loads((OUT/'integrity-report.json').read_text());comparisons=[]
for pose in ['rest','forearm_plus90','combined_reach']:
    for mode in ['Isolated','Clothed']:
        a=np.asarray(Image.open(VIEW/('World24-'+pose+'-'+mode+'.png')).convert('RGB'),dtype=float);b=np.asarray(Image.open(VIEW/('Near10-'+pose+'-'+mode+'.png')).convert('RGB'),dtype=float)
        diff=np.abs(a-b);comparisons.append({'pose':pose,'mode':mode,'meanAbsoluteRGBDifferenceOnByteScale':float(np.mean(diff)),'maxChannelDifference':float(np.max(diff)),'pixelsWithAnyChannelDifferenceAbove5':int(np.sum(np.max(diff,axis=2)>5))})
visual={'status':'RECOMMENDED_NEAR_LINING_OVERRIDE_WITH_VISIBLE_ISOLATED_LOD_TRADEOFF',
 'reviewed':['World24/Near18/Near10 forearm_plus90 isolated','World24/Near10 rest isolated','World24/Near10 forearm_plus90 and combined_reach clothed'],
 'observations':['Ten-angle isolated lining has a slightly more angular outline and larger sawtooth intersection with the unchanged shoulder lining. Both layers remain closed; this is their overlap boundary, not an open hole.',
 'With actual93f74 cloth and wraps present, no new hand/sleeve silhouette break or through-hole was identified in the inspected matching closeups. Hands, wraps and garment geometry are exact originals.',
 'World body/collision anatomy stays24-angle. Only near rendering uses10-angle lining. Dynamic cloth could expose more lining than these static captures; actual Unity evaluation remains separate.'],
 'pixelComparisons':comparisons,'surfaceErrorScope':'Finite bidirectional samples of all source/near vertices, triangle centroids and triangle-edge midpoints at429poses; not a continuous Hausdorff certificate.'}
(OUT/'visual-review.json').write_text(json.dumps(visual,indent=2),encoding='utf-8')
file=OUT/'DosaV2_NearArmLining10.blend';assert sha(file)==report['outputSha256']==integrity['nearSha256']
handoff={'status':'NEAR_ONLY_BUDGET_AND_STATIC_INTEGRITY_READY_NOT_RIG_PASS','modelSha256':sha(file),
 'sourceWorldSha256':report['sourceSha256'],'overrideObjects':['DosaV2_ArmLining_Left','DosaV2_ArmLining_Right'],
 'onlyMeshDataOverride':True,'verticesPerObject':90,'trianglesPerObject':176,'currentNear18TrianglesPerObject':320,'triangleReduction':288,'projectedFullNearTriangles':25982,
 'shapeKeys':0,'maximumInfluences':2,'groupNameIndexMapping':'Exact original7group definitions/order per object, preserved for mesh-data-only swapping.',
 'unchangedOtherRendererCount':len(integrity['unchangedOtherRenderers']),'boneCount':98,'productionActions':0,
 'poseCount':429,'properSelfCrossings':report['maximumProperSelfCrossings'],'fixedPinIntersections':report['pinIntersectionCases'],
 'maximumSampledSurfaceErrorMeters':report['maximumSampledSurfaceErrorMeters'],'worstPose':'Left forearm_plus90','material':'DosaV2_InnerLinen',
 'recommendation':'Use these two mesh-data blocks only for near rendering. Preserve24-angle world anatomy/collisions and original18-angle files. Confirm dynamic cloth exposure in Unity; no performance claim from concurrent Blender runs.'}
(OUT/'handoff.json').write_text(json.dumps(handoff,indent=2),encoding='utf-8')
(OUT/'HANDOFF.md').write_text(f'''# Near lining budget10

Ready as a **near-only rendering override**, with a small isolated-silhouette tradeoff. This is not overall RIG_PASS or native cloth approval.

`DosaV2_NearArmLining10.blend` SHA256 `{sha(file)}` derives from frozen93f74 world source. Extract **only the mesh data** of `DosaV2_ArmLining_Left` and `DosaV2_ArmLining_Right`. Do not export this derivative as the world model. Existing24-angle world and18-angle near files were preserved.

Each override has90vertices /176triangles, retaining all9lengthwise sections. Compared with320triangles each for Near18, the pair saves288triangles: the new panel candidate's projected near count becomes**25,982**, within26,000. Hands, grip geometry, cloth faces, and shoulder lining96triangles per side remain untouched.

Independent integrity checks found44other renderers byte-equivalent in captured geometry/weights/UV/colors/material bindings,98unchanged rest/parent/deform bones,0Actions, shape0, maximum2influences (within4), and unchanged7group definitions/order on each object. This exact group-index contract is required when swapping only mesh data. Material remains `DosaV2_InnerLinen`; source lining has noUV and uses the same original material.

Across429static and interpolated poses: proper selfcrossings0, cloth fixed-point intersections0, finite positions, closed two-triangle-per-edge topology with Euler characteristic2, nonzero face areas, and positive signed volumes. Maximum weight-sum error2.98e-8. Minimum posed triangle area is1.63e-5m².

Bidirectional sampled surface error against World24 is at most**2.3909mm**, at Left forearm_plus90. Rest maximum is1.8567mm. These are vertex/edge-midpoint/centroid samples, not a formal Hausdorff bound.

Matching World24/Near18/Near10 renders use the same93f74body, actual materials, lighting and cameras. The isolated10-angle shell has a slightly more angular contour and a coarser zigzag where it overlaps the unchanged shoulder shell. It remains closed. In the inspected clothed closeups, the lining is covered and no new visible silhouette break/through-hole was identified. Clothed mean absolute byte-RGB difference versus World24 is0.00110(rest),0.000168(forearm_plus90),0.003714(combined_reach); these image metrics do not replace visual inspection or dynamic validation.

The original garment's stretching and waist defects remain outside this bounded change. Dynamic cloth may expose more lining, so final Unity camera/cloth review is still required. World collision proxies must continue using the unchanged World24 lining.

## Rebuild

Canonical scripts: `build_near_lining_budget10.py`, `audit_near_lining_budget10_integrity.py`, `render_near_lining_budget10.py`, then `freeze_near_lining_budget10.py`. The builder explicitly reuses the preserved `build_near_shoulder_lining_lod.py` algorithm with source, angular-count, cardinality and output-path substitutions. Base recipe SHA is stored. Recipe copies are provenance, not standalone paths. Use a new output directory for any later derivative; do not overwrite this frozen evidence.
''',encoding='utf-8')
recipes=OUT/'Recipes';recipes.mkdir(exist_ok=True)
for name in ['build_near_lining_budget10.py','build_near_shoulder_lining_lod.py','audit_near_lining_budget10_integrity.py','render_near_lining_budget10.py','freeze_near_lining_budget10.py']:shutil.copyfile(Path(__file__).with_name(name),recipes/name)
inputs=[BASE/'TorsoSupportedPanel/SeamLineCandidate/DosaV2_FreeHemPanel_Candidate.blend',BASE/'ShoulderComplete/NearOverrides/DosaV2_NearArmLining18.blend',BASE/'FoldedGusset/Inputs/static-pose-definitions.json']+sorted((BASE/'FoldedGusset/IntermediatePoseData').glob('*.json'))
files=[p for p in OUT.rglob('*') if p.is_file() and p.name!='frozen-manifest.json' and p.suffix not in ['.log','.blend1']]
manifest={'status':'FROZEN_NEAR_ONLY_RENDER_OVERRIDE','files':[{'path':str(p.relative_to(ROOT)).replace('\\','/'),'sha256':sha(p),'bytes':p.stat().st_size} for p in sorted(files)],'readOnlyInputs':[{'path':str(p.relative_to(ROOT)).replace('\\','/'),'sha256':sha(p)} for p in inputs]}
(OUT/'frozen-manifest.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8');print(json.dumps({'modelSha256':sha(file),'manifestSha256':sha(OUT/'frozen-manifest.json'),'files':len(files),'projectedNearTriangles':25982},indent=2))
