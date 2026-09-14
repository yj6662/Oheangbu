"""Freeze reviewed, measured derivatives; copy bytes, never modify the parent assembly."""
import bpy,json,hashlib,shutil
import numpy as np
from pathlib import Path
ROOT=Path(__file__).resolve().parents[3];ART=ROOT/'Art/PlayerV2';OUT=ART/'Inspect/HandsSelfFolds'
source=ART/'Inspect/ClothBlender/Inputs/Assembled-final-46655083.blend';source_sha=hashlib.sha256(source.read_bytes()).hexdigest()
validation=json.loads((OUT/'handoff-validation.json').read_text());contact=json.loads((ART/'Inspect/HandsWithSleeves/self-fold-corrected-dense-contact-validation.json').read_text())
assert contact['all_primary_contacts_pass'];assert validation['sourceSha256']==source_sha
bpy.ops.wm.open_mainfile(filepath=str(source));oldhand=bpy.data.objects['DosaV2_Hands'];oldshapes={k.name:np.asarray([v.co for v in k.data]) for k in oldhand.data.shape_keys.key_blocks}
frozen=[]
for old,new,names in [('DosaV2_HandsHarmonicProbe.blend','DosaV2_HandsSelfFoldFinal.blend',['DosaV2_Hands']),('DosaV2_WrappingForearmProbe.blend','DosaV2_WrappingBindingFinal.blend',['DosaV2_SleeveInner_L','DosaV2_SleeveInner_R'])]:
 src=ART/old;dst=ART/new;sha=hashlib.sha256(src.read_bytes()).hexdigest()
 if dst.exists():assert hashlib.sha256(dst.read_bytes()).hexdigest()==sha,'Frozen output differs; choose another version'
 else:shutil.copy2(src,dst)
 bpy.ops.wm.open_mainfile(filepath=str(dst));row={'path':str(dst),'sha256':sha,'mergeOnly':names,'meshes':[],'actions':len(bpy.data.actions)}
 for name in names:
  o=bpy.data.objects[name];o.data.calc_loop_triangles();m={'name':name,'vertices':len(o.data.vertices),'triangles':len(o.data.loop_triangles),'triangleDelta':0}
  if name=='DosaV2_Hands':
   m['shapeNames']=[k.name for k in o.data.shape_keys.key_blocks];m['shapes']={}
   for key in o.data.shape_keys.key_blocks:
    delta=np.linalg.norm(np.asarray([v.co for v in key.data])-oldshapes[key.name],axis=1);m['shapes'][key.name]={'changedVertices':int((delta>1e-8).sum()),'maxDeltaMeters':float(delta.max())}
   locked=json.loads((OUT/'repair-report.json').read_text())['protectedIndices'];sidekey=np.asarray([o.data.shape_keys.key_blocks['GripPalmRelax_'+('Right' if v.co.x<0 else 'Left')].data[i].co for i,v in enumerate(o.data.vertices)]);original=np.asarray([oldshapes['GripPalmRelax_'+('Right' if v.co.x<0 else 'Left')][i] for i,v in enumerate(o.data.vertices)]);m['shaftLockedVertices']=len(locked);m['shaftLockedMaxDeltaMeters']=float(np.linalg.norm(sidekey[locked]-original[locked],axis=1).max());assert m['shaftLockedMaxDeltaMeters']==0
  row['meshes'].append(m)
 frozen.append(row)
reviewed=[]
for folder in [OUT/'Views',OUT/'RootViews',ART/'Inspect/WrappingInnerWall/BindingViews']:
 report=json.loads((folder/'report.json').read_text());reviewed.extend(report['renders'])
report={'status':'BOUNDED_HAND_AND_WRAPPING_REPAIR_READY_FOR_PARENT_MERGE','rigGate':'NOT_GRANTED','source':str(source),'sourceSha256':source_sha,'frozen':frozen,
 'method':{'hand':'Existing two GripPalmRelax corrective shapes: contact-locked local posed-space harmonic fairing around actual crossing roots, inverse linear blend skinning. Open Basis, weights, UVs, bone lengths, sockets, topology and material remain unchanged. Max posed displacement cap10mm, 200 relaxation iterations. No additional blendshape or runtime change.',
 'wrapping':'Authored forearm wrapping vertices only: remove UpperArm contribution and retain the original smooth ForeArm-to-Hand wrist transition. Original scanned cuff geometry/weights unchanged. The closed inner wall and outside wrapping retain their exact rest coordinates, UV and topology; no deletion, collapse, proxy cover or additional triangles.'},
 'validation':{'staticPoseCount':validation['poseCount'],'handProperCrossingsAllPoses':0,'handOpenToGripSamples':21,'handOpenToGripProperCrossings':0,'wrappingSelfCrossingsBothSidesAllPoses':0,'denseActualBrushContactCases':4,'denseSurfaceSamples':sum(case['triangle_surface_samples'] for side in contact['sides'].values() for case in side.values()),'maxTriangleBrushPenetrationMeters':max(case['triangle_surface_penetration_m'] for side in contact['sides'].values() for case in side.values()),'primaryContactPass':True,'primaryGapRangeMeters':[min(v['minimum_signed_surface_distance_m'] for side in contact['sides'].values() for case in side.values() for n,v in case['fingers'].items() if n in ['Thumb','Index','Middle']),max(v['minimum_signed_surface_distance_m'] for side in contact['sides'].values() for case in side.values() for n,v in case['fingers'].items() if n in ['Thumb','Index','Middle'])]},
 'visualReview':{'directlyViewedPngCount':len(reviewed),'hand':'Actual sharp skin self-folds at right index/thumb roots and left ring root are removed. The volume is continuous in palm/side views, primary brush grip remains and ring/pinky release is visible. Low triangular shading/creases remain on broad palm regions; they are recorded separately from proper nonadjacent triangle crossings. This is not a claim of final skin art polish.',
 'wrapping':'Both sides: grip, elbow120, forearm±90 and combined compare20PNGs. New wrapping keeps a coherent forearm volume and wrist coverage. A thin band of the lighter wrapping becomes visible between the older dark scanned cuff folds during extreme forearm twist; classified as the existing intentionally layered cuff assembly. No new exposed skin or through-hole was seen at the wrap-to-wrist boundary. Shoulder cuts visible in isolated renders are fixture object exclusions, not the full character.',
 'images':reviewed},
 'unresolved':{'wholeCharacterSelfIntersectionCount':None,'faceFlipCount':None,'allCrossObjectPenetrationCount':None,'oldLiningCombinedProximalCapPairs':{'Left':77,'Right':2},'newCanonicalOpenHandOuterSleeveToArmLiningClearance':'Separate integration issue reported by asset agent; not touched or passed by these derivatives.'},
 'reports':{'geometryIntegrity':str(OUT/'handoff-validation.json'),'denseShaftContact':str(ART/'Inspect/HandsWithSleeves/self-fold-corrected-dense-contact-validation.json'),'originalCrossingLocalization':str(ART/'Inspect/TriangleCrossings/RegionReview/report.json'),'baselineWrappingExpanded':str(ART/'Inspect/WrappingInnerWall/baseline-expanded.json')},
 'limitations':['Proper triangle test excludes shared-vertex pairs, coplanar contact and other objects. Intentional source-cuff/material overlaps retain their actual measured counts.','The original assembled source, prior HandsArmsFinal and two-ring ArmLining derivatives remain frozen.','Parent must merge only the named objects, retain imported blendshape normals, and recheck Unity source-bound renders. Production animations remain behind RIG_PASS.']}
(OUT/'final-handoff.json').write_text(json.dumps(report,indent=2,ensure_ascii=False))
print(json.dumps({'frozen':frozen,'validation':report['validation'],'viewedPngs':len(reviewed)},indent=2))
