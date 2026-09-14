"""Record directly inspected evidence; this does not infer an automatic rig pass."""
import json, hashlib
from pathlib import Path
ROOT = Path(__file__).resolve().parents[3]
ART = ROOT / 'Art/PlayerV2/Inspect'
folders = {'blender': ART/'StaticRigBlender', 'unity': ROOT/'Oheangbu/Screenshots/PlayerDosaV2/static-poses-20260908_042038_442'}
seen = json.loads((ART/'final-static-review-inventory.json').read_text())
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
definitions=json.loads((ROOT/'Art/PlayerV2/Validation/static-pose-definitions.json').read_text())
source_sha='466550837199954b9b8ad26daed1a0224841e5ff1d4379fc1cadef69269b3ef4'
findings=[
 {'id':'HAND-01','status':'RESOLVED_IN_VISIBLE_FINAL_VIEWS','region':'hands/wrists','observation':'Dedicated hand surface, palm grip correctives, forearm wrapping and imported blendshape normals remove the prior broad gray wrist wedge, exposed lining cylinder and widespread dark triangular finger endcaps. Grip/open/up and wrist/forearm close views remain attached.','evidence':['near_grip_down_right_hand.png','near_grip_up_left_hand.png','near_wrist_flex_left_hand.png'],'limits':'A small joint fold/polygon form at extreme close distance remains; this is not a global skin self-intersection proof.'},
 {'id':'UPPER-01','status':'NO_GROSS_DEFECT_VISIBLE_IN_REVIEWED_VIEWS','region':'head/neck/shoulders/elbows','observation':'Head, hat, neck and arm volumes remain attached across lowered, shoulder 45/90/120, elbow 45/90/120 and forearm +/-90 poses. No former head/shoulder shard burst or bare skin through the outer coat was visually identified.','evidence':['world_open_hand_front.png','world_shoulder_120_front.png','near_forearm_plus90_front.png'],'limits':'Occluded inner surfaces remain unmeasured.'},
 {'id':'CLOTH-01','status':'RECLASSIFIED_OVERLAPPING_INDEPENDENT_TATTERED_EDGES','region':'crossed sleeves','observation':'The horizontal strip below crossed forearms consists of independently colored left/right sleeve tails. The orange sleeve has a long thin pointed hem and overlaps/approaches the blue hem. Three close angles show both separate cuff openings. The supplied bone weight totals contain no opposite-arm weights on either outer sleeve; a single cross-arm weighted bridge is therefore not supported. The long pointed overlap remains a visual silhouette candidate.','evidence':['world_combined_reach_front.png','near_combined_reach_front.png','../CrossedSleeves/front_left.png','../CrossedSleeves/front_right.png','../CrossedSleeves/below.png','../CrossedSleeves/part_colors.png'],'limits':'No global triangle intersection count was performed. Independent meshes can still contact/intersect; their visual overlap is not proof of an invalid bridge.'},
 {'id':'CLOTH-02','status':'REMAINING_TATTERED_SILHOUETTE_CANDIDATE','region':'lateral sleeve/waist','observation':'A pointed ragged cloth tail remains near the lateral waist/sleeve border at high shoulder elevation. No exposed skin or missing limb is visible at that point.','evidence':['world_shoulder_120_front.png','world_shoulder_120_quarter.png'],'limits':'Authored tatter versus local deformation severity needs art judgment; a face inversion or wrong bone is not established.'},
 {'id':'LOWER-01','status':'STRUCTURAL_VISUAL_REPAIR_VISIBLE_ART_COHESION_REMAINS','region':'hips/trousers/knees/ankles','observation':'Both left and right hip/knee/ankle side and rear-quarter views retain continuous trouser and boot volumes. The old large black thigh holes and isolated cylindrical shin/upper-leg underlining presentation are absent in these views. Hip flexion exposes a broad smooth dark-gray upper trouser area compared with the textured robe.','evidence':['world_hip_flex_leg_side.png','world_hip_flex_right_leg_back_quarter.png','world_knee_flex_right_leg_side.png','world_ankle_flex_right_leg_back_quarter.png'],'limits':'Material cohesion remains an art candidate. Hidden inner-thigh contacts are not globally validated.'},
 {'id':'LOWER-02','status':'SMALL_FRAGMENT_VISIBLE_CAUSE_UNCONFIRMED','region':'right raised-thigh outer hem','observation':'A small dark rectangular/triangular fragment appears beyond the raised right thigh in the Blender rear-quarter hip view; another thin hem fragment is visible around a bent-leg view. These are tiny compared with the repaired main trouser silhouette.','evidence':['world_hip_flex_right_leg_back_quarter.png'],'limits':'Detached topology, authored torn hem and occlusion cannot be distinguished reliably from the image; unresolved boundary-defect count remains null.'},
 {'id':'RIGID-01','status':'STATIC_SHAPE_RETAINED_IN_VISIBLE_VIEWS','region':'pack/bottles/tubes/waist ornaments','observation':'Pack and hanging props keep recognizable rigid forms throughout the viewed torso/arm/lower-body static poses; no visible arm-driven prop stretching was identified.','evidence':['world_torso_twist_front.png','world_rest_back.png','world_combined_reach_front.png'],'limits':'This review does not validate independent physics, collisions, or moving-cloth behavior.'},
 {'id':'ARM-LINING-01','status':'MEASURED_LOCAL_INTERPENETRATION_REPAIR_PENDING','region':'upper arm lining beneath pinned cloth','observation':'Separate exact closed-mesh BVH evidence for this same source reports 6 pinned vertices inside ArmLining at rest (maximum 4.161 mm), and 9 during grip pose (maximum 5.29 mm). BodyLining rest intrusion count is zero in that measured subset. This is numerical evidence, not a visually localized skin finding.','evidence':['../ClothBlender/BodyClearance/actual-body-pin-clearance.json','../ClothBlender/BodyClearance/grip_settle/actual-body-pin-clearance.json'],'limits':'Only reported tested pins/closed inner volumes are covered; outer cloth/whole-body penetration totals remain null.'}
]
pose_note={
 'combined_reach':['CLOTH-01'], 'shoulder_120':['CLOTH-02'], 'hip_flex':['LOWER-01'], 'hip_flex_right':['LOWER-01','LOWER-02'],
 'knee_flex':['LOWER-01'], 'knee_flex_right':['LOWER-01'], 'ankle_flex':['LOWER-01'], 'ankle_flex_right':['LOWER-01'],
 'grip_down':['HAND-01','ARM-LINING-01'], 'grip_up':['HAND-01'], 'wrist_flex':['HAND-01'], 'wrist_extend':['HAND-01'], 'rest':['UPPER-01','ARM-LINING-01'], 'torso_twist':['RIGID-01']}
unknown={'invertedJointFaces':None,'unresolvedSelfIntersections':None,'unintendedBridgeFaces':None,'unresolvedBoundaryDefects':None,'wholeBodySkinVsClothPenetrations':None}
cross=ART/'CrossedSleeves'
extra=[{'path':str((cross/f).relative_to(ROOT)).replace('\\','/'),'sha256':sha(cross/f),'directlyViewed':True} for f in ['front_left.png','front_right.png','below.png','part_colors.png']]
cross_report=json.loads((cross/'report.json').read_text())
assert cross_report['sourceSha256']==source_sha
for env,folder in folders.items():
 original=json.loads((folder/'report.json').read_text())
 actual_source=original.get('sourceSha256') if env=='blender' else original['sources']['Art/PlayerV2/DosaV2_Assembled.blend']
 assert actual_source==source_sha
 images=[]
 for f in seen[env]:
  p=folder/f; assert p.exists(); images.append({'file':f,'sha256':sha(p),'directlyViewed':True})
 samples=[]
 for d in definitions['poses']:
  for variant in ['world','near']:
   if variant=='near' and d['id'].startswith(('hip_flex','knee_flex','ankle_flex')): continue
   pose_ids=sorted([p['id'] for p in definitions['poses']],key=len,reverse=True)
   def image_pose(file):return next((p for p in pose_ids if file.startswith(variant+'_'+p+'_')),None)
   related=[i['file'] for i in images if image_pose(i['file'])==d['id']]
   assert related,(env,variant,d['id'])
   samples.append({'variant':variant,'poseId':d['id'],'reviewed':True,'evidence':related,'findings':pose_note.get(d['id'],['UPPER-01']),'counts':unknown.copy()})
 assert len(samples)==38
 report={'status':'MANUAL_VISIBLE_REVIEW_COMPLETE_WITH_REMAINING_FINDINGS','environment':env,'rigGate':'NOT_GRANTED','productionAnimationApproved':False,
  'sourceSha256':source_sha,'sourceReportSha256':sha(folder/'report.json'),'poseDefinitionsSha256':sha(ROOT/'Art/PlayerV2/Validation/static-pose-definitions.json'),
  'sourceFilesStableAtCapture':original.get('sourceFilesStable',None),'method':'Every listed original PNG was directly opened and visually reviewed; no image classifier or geometry metric was substituted for viewing. Crossed sleeve close views and material part colors were additionally reviewed. Report is bound to captured source, not any later derivative.',
  'poseCount':38,'worldPoseCount':22,'nearPoseCount':16,'directlyViewedImageCount':len(images),'images':images,'additionalEvidence':extra,
  'findings':findings,'findingEvidenceConvention':'Findings are paired cross-environment observations; filenames refer to directly reviewed images in either of the two reviewed folders. Per-pose evidence and images below are exact files of this environment. CrossedSleeves and BodyClearance evidence is under Art/PlayerV2/Inspect.', 'samples':samples,'unresolvedCounts':unknown,
  'coverageLimits':['Still images only; no production animation, Cloth simulation or secondary-motion gate.','Occluded/rear-facing faces are not proven valid by a visible silhouette.','Exact contact diagnostics and imported normal tests are separately sourced; a GripSocket match alone does not prove finger contact.','Near-only arm-root cutoffs are intentional representation boundaries, not missing torso defects.'],
  'recheckAfterGeometryChanges':['source hashes and original 38-pose evidence must be regenerated or explicitly limited to unchanged regions','local ArmLining repair must check rest and grip closed-mesh clearance plus five deformation views','crossed sleeve tails require art/physics review; no inferred global bridge or penetration zero']}
 (folder/'visual-review.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
 lines=['# 최종 정적 리그 수동 시각 검수', '', f'환경: {env}. 38개 포즈(전신 22, 근접 16), 원본 PNG {len(images)}장과 교차 소매 확대 4장을 직접 확인했습니다.', '',f'소스 SHA256: `{source_sha}`', '', '결론: 기존 손목 회색 쐐기·널리 보이던 손가락 마개 음영·큰 하체 빈 공간은 이번 캡처에서 해결된 모습입니다. 교차 소매는 좌우 독립된 긴 천 끝의 겹침으로 분류했습니다. 국소 안감 침투와 작은 천 실루엣 후보가 남아 있으며, 이 문서는 RIG_PASS나 제작 애니메이션 승인이 아닙니다.', '', '| 항목 | 상태 | 관찰 |','|---|---|---|']
 for f in findings: lines.append('| '+f['id']+' | '+f['status']+' | '+f['observation']+' |')
 lines += ['', '## 검수 범위와 한계','', '실제 면 뒤집힘, 전신 자기 교차, 의도하지 않은 연결면, 전체 피부/천 관통 및 경계 결함 개수는 확인되지 않아 JSON에서 null을 유지했습니다. 소매가 다른 색·다른 메시이며 반대 팔 웨이트가 없다는 것은 잘못된 단일 브리지 가설을 약화하지만, 두 천의 접촉이나 교차를 전부 배제하지 않습니다.', '', '안감 6점(rest)/9점(grip) 침투는 별도 폐곡면 BVH 결과이며 수동 시각 관찰과 구분했습니다. 해당 국소 수정 후에는 새 소스 해시에 연결된 검증이 필요합니다. 정적 배낭 모양 유지는 장식 물리 검수 통과를 뜻하지 않습니다.', '', '38개 포즈별 근거 파일·모든 PNG SHA256·추가 확대 근거는 visual-review.json에 기록했습니다.']
 (folder/'visual-review.md').write_text('\n'.join(lines)+'\n',encoding='utf-8')
 print(env,len(images),len(samples),folder/'visual-review.json')
