"""Write factual handoff metadata without modifying frozen candidate geometry."""
import json,hashlib,shutil
from pathlib import Path
ROOT=Path(__file__).resolve().parents[3];BASE=ROOT/'Art/PlayerV2/Inspect/ClothBlender';OUT=BASE/'TorsoSupportedPanel/SeamLineCandidate'
ledger=json.loads((OUT/'authoring-ledger.json').read_text());geometry=json.loads((OUT/'geometry-audit.json').read_text())
for s in ledger['surfaces']:
    original_count=910 if s['mesh'].endswith('_L') else 930
    s['oldExactPins']=230 if s['mesh'].endswith('_L') else 307
    s['newlyTransferredVerticesWithoutPriorCloth']=s['vertices']-original_count
    for r in s['verticesLedger']:
        transferred=r['sourceIdentity']['mesh']=='DosaV2_BodyCore'
        r['previousClothState']='BodyCore_LBS_NoCloth' if transferred else 'ExistingOuterCloth'
        if transferred:r['oldMaxDistanceMeters']=None
ledger['metadataCorrection']='Transferred BodyCore vertices had no previous Cloth coefficient. Their oldMaxDistance is null, not an invented zero pin. Geometry and frozen output SHA are unchanged.'
(OUT/'authoring-ledger.json').write_text(json.dumps(ledger,indent=2),encoding='utf-8')
file=OUT/'DosaV2_FreeHemPanel_Candidate.blend';sha=hashlib.sha256(file.read_bytes()).hexdigest();assert sha==ledger['outputSha256']
recipes=OUT/'Recipes';recipes.mkdir(exist_ok=True)
for name in ['build_free_hem_panel.py','inspect_extended_underarm_panel.py','audit_free_hem_panel.py','render_free_hem_panel.py']:
    shutil.copyfile(Path(__file__).with_name(name),recipes/name)
solvers={}
for side in ['L','R']:
    p=OUT/('convex-'+side+'-429.json')
    if p.exists():
        d=json.loads(p.read_text());cases=d.get('cases',[]);solvers[side]={'path':str(p.relative_to(ROOT)),'sha256':hashlib.sha256(p.read_bytes()).hexdigest(),'summary':{
          'completed':d.get('completed'),'requestedCases':d.get('requestedCases'),
          'statusCounts':{status:sum(r['solverStatus']==status for r in cases) for status in sorted({r['solverStatus'] for r in cases})},
          'maximumActualStretch':max([0]+[r.get('actualMaximumStretch',0) for r in cases]),
          'casesRequiringSeparateReview':[r['pose'] for r in cases if not r.get('withinInitialStretchLimitAndResiduals',False)]}}
frozen={'status':'MODEL_FROZEN_FOR_NATIVE_COMPARISON_NOT_RIG_PASS','modelSha256':sha,'baseSourceSha256':ledger['sourceSha256'],
 'replacementMeshes':ledger['counts'],'exactPins':{s['mesh']:s['newExactPins'] for s in ledger['surfaces']},
 'worldTriangles':74934,'nearTrianglesWithCurrent18AngleLining':26270,'nearBudgetExcess':270,
 'boneRestParentDeformExact':True,'productionActions':0,'unchangedOtherRenderers':len(geometry['unchangedMeshes']),
 'geometry429':{'pinAnatomyIntersections':geometry['pinAnatomyIntersectionCases'],'maximumNecessaryEdgeStretch':geometry['maximumNecessaryEdgeStretch'],'maximumHistoricalAttachmentGapMeters':geometry['maximumAttachmentGapMeters']},
 'solverReports':solvers}
(OUT/'handoff.json').write_text(json.dumps(frozen,indent=2),encoding='utf-8')
text=f'''# Torso-supported, free outer hem comparison candidate

**Frozen model for native cloth comparison. This is not RIG_PASS or a native cloth pass.**

`DosaV2_FreeHemPanel_Candidate.blend` SHA256 `{sha}` derives from immutable d979 source. Replace only `DosaV2_BodyCore`, `DosaV2_SleeveOuter_L`, and `DosaV2_SleeveOuter_R`.

| Object | Vertices | Triangles | Exact cloth pins |
| --- | ---: | ---: | ---: |
| BodyCore | 3,095 | 5,697 | No Cloth |
| SleeveOuter_L | 910 | 1,704 | 66 |
| SleeveOuter_R | 1,081 | 2,033 | 108 |

World stays 74,934 triangles. Current near18-angle lining would yield26,270, exceeding26,000 by270; preserve the correct cloth surface now and separately derive the near lining/culling budget. Hands17,506 triangles remain protected. Rig98 bones/rest/parents, all existing source skin weights, hands, wraps, anatomy geometry, and43 other renderers remain unchanged. There are no production Actions.

## Actual clothing role

The reference's broad ragged outer cuff is a free overlapping hem above the inner wrap. The old shared Inner/Outer scan cut is not a stitch. Those30Left/34Right cut vertices are free fabric now. Old rest-X/height blanket pins are also not treated as attachment evidence.

The original31-face underarm proposal ended inside the source's upper-arm skin gradient. Its new boundary falsely made11.9mm of body cloth a fixed39.2mm posed span. That candidate and unsuccessful common-field probes are preserved under `FreeHemPanel`; none were merged into d979.

This candidate transfers304 connected original front/axilla garment triangles into OuterR, extending to a torso-supported line. The new interface has36vertices and maximum original Arm weight0.008166. Every transferred source triangle retains its corner positions and UV exactly. No anatomy, skin, or separate hard accessory mesh was transferred. `../panel-10-rest.png` records the inspected cyan region. BodyCore vertices no longer used by any retained face are removed with an explicit source-index map.

All actual retained Body/Outer joining-line seeds are fixed0. The adjacent cloth seam allowance remains cloth and receives continuous movement from that line, rather than becoming an automatically rigid6mm skin band. Free movement is `max(previous, 0.18*smoothstep(0,0.05,geodesicDistanceToJoin))` meters, capped at0.18m. Acceptance limits are unchanged. `Candidate/` preserves the separate6mm-band comparison; `SeamLineCandidate/` is the current proposed authoring.

## Evidence and limitations

`authoring-ledger.json` maps every fixed seed to its retained BodyCore source vertex, the original304faces to their new OuterR indices, and every old pin/new fabric role. Newly transferred Body vertices had no old cloth constraint; their prior maxDistance is null. Full429 static/mid-angle anatomy checks report0fixed-point intersections across the7unchanged closed body/arm/leg/shoulder linings.

Necessary local edge bound remains1.250006 at Left803–810, two actual body-interface points with different source skin weights. This still creates unavoidable tension; it is not a claim of healthy native behavior. Full simultaneous SOCP results are separate reports and exclude collision, bending, tethers, and actual solver convergence.

The maximum historical Left cut gap is8.2595mm in combined reach (source784, pre-existing rest gap3.724mm); this report does not relabel that gap as closed. Direct static renders retain the source's raised-arm UV stretch and combined-reach waist defects. No new rigid/Cloth ownership slit appears in rest or grip, and the original wraps/lining remain behind the free outer hem. Only actual native simulation can establish drape, overlap retention, texture behavior, body contact, and strain. Native and Blender solver gates remain outstanding.

## Rebuild

Use the immutable source at `VerifiedAttachments/Inputs/Assembled-d979b9bc.blend`. Set `DOSA_PANEL_VARIANT=torso10line`, run `build_free_hem_panel.py`, then `audit_free_hem_panel.py` and `render_free_hem_panel.py` in independent factory-startup background Blender. The selected source faces are `TorsoSupportedPanel/panel-10.json`. Recipes are copied for provenance; canonical scripts resolve their workspace-relative paths.

Run `audit_cloth_convex_feasibility.py` against both `DosaV2_SleeveOuter_*-429.npz` using the feasibility Python environment. Preserve failed or numerically inaccurate results as such. Root imports the three named meshes and rebuilds source-index/UV3 constraints and the actual per-Cloth collider selection before native evaluation. No Unity imports or source exports were performed by this agent.
'''
(OUT/'HANDOFF.md').write_text(text,encoding='utf-8');print(json.dumps(frozen,indent=2))
