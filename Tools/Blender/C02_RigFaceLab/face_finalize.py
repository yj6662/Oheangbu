"""Record observed, scoped face experiment results. No Blender or source mutation."""
import json
from pathlib import Path
R=Path('C:/Users/yj666/Oheangbu/Art/PlayerPhase1/C02_RigFaceLab/Face')
def read(p):return json.loads(p.read_text(encoding='utf-8-sig'))
def write(p,d):p.write_text(json.dumps(d,indent=2,ensure_ascii=False),encoding='utf-8')
c=read(R/'C3/manifest.json')
c['face_blink']={'status':'UNVERIFIED','implementation_status':'PASS','reason':'Bilateral skin-coverage channels, neutral return, Blender sampled closure, FBX serialization and Unity numerical/runtime control verified. Full eyelid contact and unobscured bilateral Unity closure/art review are not passed. Right half-closure has 5 sampled vertices up to 0.286795mm behind skin.'}
c['retained_for_integration']=True
c['adopted']=True
c['adoption_scope']='Experimental blink geometry only; not whole-face quality or canonical player approval.'
c['unity']={'technical_status':'PASS','scope':'Integrated_B1_C3 actual Play + LAB_B1_Idle; five channel values, neutral return and no Animator overwrite per Unity agent. Art/complete closure remain limited.'}
c['evidence']=['skin_component_validation.json','fbx_roundtrip.json','video_manifest.json','../D1/manifest.json','../../Unity/Integrated_Blink_Close/capture.json']
write(R/'C3/manifest.json',c)
d=read(R/'D1/manifest.json');d['adopted']=True;d['adoption_scope']='Existing blink preset controller values only; no new emotion/anatomy channel.'
d['visual_review']={'status':'PASS_CONTROL_ONLY','static_images':['GentleSquint.png','FocusedSquint.png','EyesClosed.png'],'continuous_frames_inspected':[21,122,142,202,238],
 'scope':'Actual selected images show source motions and eyelid coverage/return; whole footage was decoded, not claimed to have been visually watched frame by frame. Coarse original facial surfaces and eyelid patch material remain.'}
d['face_blink']=c['face_blink'];d['face_gaze']=c['face_gaze'];d['face_jaw']=c['face_jaw'];d['fbx_roundtrip']=read(R/'D1/fbx_roundtrip.json')['status'];d['canonical_player_replaced']=False
write(R/'D1/manifest.json',d)
ledger=read(R/'experiment_ledger.json');ledger.update(expression_candidates_used=1,best_candidate='C3',best_preset_candidate='D1',latest_candidate='D1',status='COMPLETE_SCOPED_EXPERIMENT_FACE_QUALITY_UNVERIFIED',canonical_player_replaced=False)
ledger['best_files']={'geometry':'C3/Face_C3.blend','presets':'D1/Face_D1.blend','fbx':'D1/Face_D1.fbx','video':'D1/Face_D1_Idle_Run_Attack_30fps.mp4'}
ledger['limited_passes']=['fbx_serialization','blendshape_control','preset_values','head_skin_binding']
ledger['not_passed']=['full_eyelid_contact','gaze','jaw','whole_face_art','integrated_character_quality']
write(R/'experiment_ledger.json',ledger)
print('Face experiment reports finalized.')
