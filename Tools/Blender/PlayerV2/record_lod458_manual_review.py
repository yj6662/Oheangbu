"""Record human/model direct visual review of the frozen actual-FBX PNG evidence."""
from pathlib import Path
import json,hashlib
ROOT=Path(__file__).resolve().parents[3];OUT=ROOT/'Art/PlayerV2/Inspect/LOD458ActualFBX/RefinedCapture'
r=json.loads((OUT/'report.json').read_text())
review={'status':'FAIL_LOD_HAND_AND_LOD2_CUFF_DEFORMATION','rigPass':False,'sourceSha256':r['sourceSha256'],
 'actualFbxHashes':{m['label']:m['sha256'] for m in r['models']},
 'directlyViewedPngs':[{'path':x['path'],'sha256':x['sha256']} for x in r['renders']],
 'observations':[
  {'region':'hands','lod':'lod0','result':'No new visible skin wedge in six palm closeups; actual three grip-pose proper crossings are zero. Contact is the separately recorded source f94 result, not remeasured by these PNGs.'},
  {'region':'hands','lod':'lod1','result':'FAIL: grip left thumb root has a small sharp fold; actual imported three grip poses each have38 proper triangle crossings. Both corrective names exist; interpolation alone did not preserve a valid deformed surface.'},
  {'region':'hands','lod':'lod2','result':'FAIL: left grip thumb-root skin forms a sharp wedge; actual imported three grip poses each have10 proper triangle crossings. Lower triangle density is not an exemption from the deformation gate.'},
  {'region':'cuff','lod':'lod1','result':'Six hand closeups show no LOD2-like long cuff spike; original ForeArm/Hand-only wrapping binding survives import.'},
  {'region':'cuff','lod':'lod2','result':'FAIL: long dark triangular protrusion on right open/grip cuff and a smaller left cuff defect. The same spike is visible on the crossed-arm external view; UpperArm weight remains zero, identifying reduction topology rather than a reverted source binding.'},
  {'region':'face','lod':'all','result':'Shoulder120 face closeups retain facial landmarks and readable UV mapping. LOD2 hair/hat are visibly more angular; no new long facial skin spike observed in the three supplied face views.'},
  {'region':'boots','lod':'all','result':None,'reason':'Six nominal knee/boot closeups are occluded by the hanging robe. They prove robe appearance only. New isolated-boot views are required before a boot deformation verdict.'},
  {'region':'robe_and_props','lod':'all','result':'Crossed-arm and knee views show the source folded/overlapping robe silhouette; LOD2 scan folds and prop edges become coarse angular sheets. Cross-object cloth contact is outside this numeric hand audit and remains under the parent cloth repair gate.'},
  {'region':'actual_pixel_heights','lod':'all','result':'Four directly viewed full frames, actual projected240/120px with identical illumination, retain broad identity. At120px fine finger/cloth failures are not resolvable, so the close-view failures remain failures.'}],
 'limitations':['No Unity MCP or production scene change.','No production animation or native Cloth simulation assessed.','Shared-vertex/coplanar/cross-object contacts excluded by proper crossing test.','The earlier parent directory capture attempt had an occluded camera; only RefinedCapture files are eligible.'],
 'next':'History frozen; generator-only corrective/ring repair authorized and underway.'}
assert len(review['directlyViewedPngs'])==34
(OUT/'manual-review.json').write_text(json.dumps(review,indent=2))
(OUT/'manual-review.md').write_text('''# Actual LOD FBX review - source458

Result: **FAIL / RIG_PASS not granted**. All34 PNGs in the accompanying JSON were directly opened. Input FBX copies and hashes are bound to the report.

- LOD0 hands retain the corrected appearance and zero measured grip self-crossings.
- LOD1 and LOD2 retain both named correctives and valid source-derived deltas, but reduced skins reintroduce38 and10 proper hand triangle intersections respectively. Visible thumb-root folds confirm a deformation defect.
- LOD2 loses cuff ring topology and exposes a long dark triangle in both close and crossed-arm external views. The corrected ForeArm/Hand binding survives, so the source binding was not reverted.
- Face closeups preserve landmarks/UV identity. Hair and hat naturally become coarser; no new long face spike was observed.
- Boot verdict is **unconfirmed**: all six nominal boot frames are blocked by the robe. A camera/visibility-only diagnostic will cover the actual boots.
- Full frames were rendered at measured240/120px height. Fine defects cannot be exonerated from these small frames.

Source and existing LOD outputs were preserved in History before the authorized generator repair. No production animation, native Cloth simulation, Unity scene, or overall rig gate was assessed here.
''')
