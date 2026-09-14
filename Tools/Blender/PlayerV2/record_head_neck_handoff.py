"""Record reviewed collision candidates. Does not edit Blender/Unity source assets."""
from pathlib import Path
import hashlib,json,shutil
ROOT=Path(__file__).resolve().parents[3]
OUT=ROOT/'Art/PlayerV2/Inspect/HeadNeckCollision'
STAGE=ROOT/'Art/PlayerV2/Staging/Code/HeadNeckCollision';STAGE.mkdir(parents=True,exist_ok=True)
plan=json.loads((OUT/'head-neck-capsules.json').read_text())
def sha(path):return hashlib.sha256(path.read_bytes()).hexdigest()
for cap in plan['capsules']:
    shape={key:cap[key] for key in ['name','anchorBone','start','end','radius']}
    cap['shapeSha256']=hashlib.sha256(json.dumps(shape,sort_keys=True,separators=(',',':')).encode()).hexdigest()
for row in plan['tasselRestClearance']:
    row['conservativeWholeTriangleRestGapLowerBoundM']=row['minimumCapsuleGapM']-row['maximumSamplingEdgeM']
    row['boundRationale']='A barycentric grid with source-triangle subedges <=2mm is a <=2mm net. Capsule-union signed distance is 1-Lipschitz, so subtracting2mm bounds the unsampled rest face. No posed or dynamically simulated gap is inferred.'
plan['authoredFitMethod']='Six anatomical patches. Each candidate preserves complete original triangles; measured corner containment is exact for a convex capsule. Candidate axes include world XYZ/PCA and radial centers from minimax toward the interior. Selection scores sampled nearest-surface normal overhang plus volume. No source mesh/weights/UV/pins changed.'
plan['manualReview']={'reviewed':True,'sourceSha256':plan['sourceSha256'],'images':['source_front.png','source_side.png','semantic_front.png','semantic_side.png','capsules_front.png','capsules_side.png'],
 'findings':['The explicit cyan exclusion follows visible shoulder/collar cloth. The red central neck, face and all original Hair triangles remain included.',
 'Capsules cover all selected faces; their rounded enclosure remains visibly broader than the jaw/neck. This conservative overhang is reported, not accepted as dynamic cloth compatibility.',
 'Selected Head/Hair and both actual tassel surfaces remain unchanged. Extra brim fragments in the isolated tassel render are present in the source; this task did not alter them.'],
 'unresolved':['Actual Unity imported BakeMesh coverage has not been executed by this subagent.','Native fixed-pin and movement-envelope compatibility is pending separate read-only source-pose inspection.','Dynamic hat-string collision and native Cloth candidate selection remain parent-owned gates.']}
(OUT/'head-neck-capsules.json').write_text(json.dumps(plan,indent=2))
shutil.copy2(OUT/'head-neck-capsules.json',STAGE/'head-neck-capsules.json')
ledger={'status':'STAGED_AUTHORING_READY_DYNAMIC_COMPATIBILITY_PENDING','rigPass':False,'sourceSha256':plan['sourceSha256'],
 'sourceUnchanged':sha(Path(plan['source']))==plan['sourceSha256'],
 'files':{str(p.relative_to(ROOT)):sha(p) for p in [OUT/'head-neck-capsules.json',STAGE/'head-neck-capsules.json',STAGE/'DosaV2HeadNeckCollisionBuilder.cs',ROOT/'Tools/Blender/PlayerV2/author_head_neck_colliders.py']},
 'shapeHashes':{c['name']:c['shapeSha256'] for c in plan['capsules']},'candidateNames':[c['name'] for c in plan['capsules']],
 'offlineCompile':'Unity6000.3.9f1 DLLs, dotnet9.0: 0 warnings / 0 errors. RunMathChecks is provided but has not run inside Unity.',
 'actualSourceProof':{'includedWholeTriangles':plan['includedTriangles'],'excludedUpperGarmentTriangles':plan['excludedUpperGarmentTriangles'],'maximumOutsideM':plan['maximumWholeTriangleOutsideM']},
 'existingBodySnapshot':plan['existingRestBodySnapshot'],
 'scope':'No Unity MCP, live Assets mutation, source model save, animation creation, native Cloth selector mutation or existing27 capsule edits.'}
extra=[]
for label in ['CurrentGlobalEnvelope429','ShoulderComplete429']:
    file=OUT/'ClothEnvelope'/label/'report.json'
    if file.exists():
        d=json.loads(file.read_text());extra.append({'label':label,'path':str(file),'sha256':sha(file),'testedPlanSha256':d['planSha256'],
            'poseCount':d['poseCount'],'models':d['models'],'fixedPinInsideSamples':d['fixedPinInsideSamples'],
            'freeMobilityBallTrappedSamples':d['freeMobilityBallTrappedSamples'],
            'freeSkinTargetInsideSamples':sum(r['freeSkinTargetInsideSamples'] for r in d['reports']),
            'maximumFreeSkinTargetPenetrationM':max([0]+[-r['minimumFreeSkinTargetGapM'] for r in d['reports']]),
            'scope':'No fixed-pin or single-capsule-trapped mobility ball conflict. Free skin-target contacts require actual Cloth solving; neither collision-free dynamics nor joint edge feasibility is inferred.'})
ledger['additionalClothEnvelopeEvidence']=extra
(OUT/'handoff.json').write_text(json.dumps(ledger,indent=2))
rows='\n'.join('| '+c['name'].replace('__DosaSecondaryProxy_HeadNeck_','')+' | '+f"{c['radius']*1000:.3f}"+' | '+c['shapeSha256']+' |' for c in plan['capsules'])
(OUT/'handoff.md').write_text(f'''# Head/neck collision candidate handoff

Source: `{plan['sourceSha256']}`; original source unchanged. This is a static authoring handoff, **not RIG_PASS or dynamic Cloth approval**.

The selected Head/Hair surface contains **{plan['includedTriangles']} original triangles**, each wholly inside one convex capsule. Maximum corner excess is {plan['maximumWholeTriangleOutsideM']*1000:.5f} mm. **{plan['excludedUpperGarmentTriangles']} collar/shoulder garment triangles** are excluded by the exact recorded semantic mask, with their original indices/points/weights in the JSON. The source central neck follows Head100%, so all six capsules parent to Head; no Neck transform approximation is used.

| Candidate | Radius mm | Shape SHA256 |
|---|---:|---|
{rows}

The shape digest is SHA256 of compact sorted-key JSON containing `name, anchorBone, start, end, radius`. Coordinates are Blender rig-local meters; Unity representation coordinates are `(-x,z,-y)`.

## Integration

Copy only the staged helper when parent is ready for domain reload:
`Art/PlayerV2/Staging/Code/HeadNeckCollision/DosaV2HeadNeckCollisionBuilder.cs`.

Call `TryBind(worldRepresentation, worldAnimator, absolutePlanPath, out CapsuleCollider[] additionalCapsules, out BindingReport report)` on the fresh **Edit-mode rest** instance. The matching plan is `Art/PlayerV2/Staging/Code/HeadNeckCollision/head-neck-capsules.json`. A returned false is a WAIT with an actual error, and created candidates are rolled back. The helper validates actual readable imported Head/Hair BakeMesh complete triangles and semantic counts before creating any collider. It checks that every eligible corner follows the actual Head bone and that one capsule encloses the complete triangle; vertices in different capsules do not constitute coverage.

Append the returned exact six names to the existing full collision candidate set for the custom secondary rig. Existing27 body colliders are unchanged. The parent retains native Cloth's measured16-capsule selector and must record omissions/compatibility; this helper never assigns `Cloth.capsuleColliders`. It also never registers these rigid Head candidates as dynamic arm-fit regions. Head bone animation automatically moves them before late secondary evaluation.

Each new collider is an explicit visual descendant, layer2 trigger, no Rigidbody, includeLayers0/excludeLayersEverything. Reusing an instance with existing HeadNeck names is rejected instead of deleting or duplicating them. No root/gameplay/Camera/input changes occur.

`RunMathChecks()` is a six-case parent-callable pure geometry guard (including the invalid corner-only union case and rigid-pose invariance). The helper compiled against Unity6000.3.9f1 DLLs with **0 warnings / 0 errors**. This subagent has not invoked Unity.

## Measured limits

Actual rest tassel triangles were sampled at <=2mm grid spacing: left minimum {plan['tasselRestClearance'][0]['minimumCapsuleGapM']*1000:.3f}mm, right {plan['tasselRestClearance'][1]['minimumCapsuleGapM']*1000:.3f}mm. Subtract2mm for a conservative continuous-face lower bound. At those worst samples the actual Head/Hair is roughly12.9mm farther away. This is rest-only and is not dynamic approval.

Sampled normal-based outward overhang is largest near the open neck/jaw cut: raw **{max(c['maximumOutwardNormalOverhangM'] for c in plan['capsules'])*1000:.3f}mm**, or **{max(c['maximumOutwardOverhangOutsideExistingRestBodyM'] for c in plan['capsules'])*1000:.3f}mm** outside the separately hashed existing rest body union. These are sample maxima with nearest source normals, not certified continuous Hausdorff bounds. Wire front/side images visibly show the conservative jaw/neck region; fixed Cloth pins and posed sleeves must be checked before enabling the candidates in production. No such compatibility is inferred from triangle containment.

Existing body snapshot provenance, all exposed-surface metrics, rest tassel samples, semantic exclusions and candidate hashes are in `head-neck-capsules.json` / `handoff.json`. Exact source-pose cloth compatibility is the next separate diagnostic.
''',encoding='utf-8')
if extra:
    with (OUT/'handoff.md').open('a',encoding='utf-8') as f:
        f.write('''
## Completed additional source-pose check

The separate `ClothEnvelope/CurrentGlobalEnvelope429/report.json` and `ClothEnvelope/ShoulderComplete429/report.json` now supersede the pending **static target compatibility** item above. Both datasets cover429 poses and all3 actual Cloth surfaces. Fixed pin penetrations are0; minimum gaps are L41.123mm, R78.350mm and Robe436.431mm. No complete free mobility ball is contained by one capsule.

This does **not** mean the free cloth is clear. Both datasets contain503 left-sleeve skin-target contacts, maximum43.080mm at `intermediate_combined_reach_97`, source468, mobility180mm. Global free-envelope potential contacts are L26,914/R20,644; the new shoulder candidate has L26,862/R20,644. Those points retain motion room but require native collision solving and edge-compatible cloth movement. The reports preserve all free skin-target hits and exact source-data hashes. They do not prove complement feasibility against the union or connected Cloth constraints, and they do not replace the parent-owned native collision/selector tests. No gate was promoted to RIG_PASS.
''')
print(json.dumps(ledger,indent=2))
