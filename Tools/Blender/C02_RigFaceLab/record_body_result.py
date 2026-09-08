"""Record explicit decisions from the evaluated body experiments; no Blender mutation."""
from pathlib import Path
import json, shutil
ROOT=Path('C:/Users/yj666/Oheangbu')
LAB=ROOT/'Art/PlayerPhase1/C02_RigFaceLab'

def run():
    summaries={n:json.loads((LAB/'Body'/n/'summary.json').read_text(encoding='utf8')) for n in ('BaselineAudit','A1Audit','A2Audit')}
    result={
      'stage':'A','attempts_used':2,'limit':4,'selected_attempt':'A2',
      'best_file':'Body/A2.blend','status':'UNVERIFIED',
      'scope':'Limited left lower axilla skinning improvement. Whole garment quality and reliable penetration remain unverified.',
      'actual_triangles':51760,'actual_render_vertices':87148,'actual_deformation_bones':24,'new_deformation_bones':0,
      'geometry_uv_rest_skeleton_original_actions_changed':False,
      'weights_exception':{'vertices_with_five':722,'all_other_vertices_maximum':4,'ratio_of_render_vertices':722/87148,'requires':'Unity importer Custom maxBonesPerVertex=5; runtime Unlimited skinning; renderer Auto'},
      'cause':'Across 11mm and neighboring axilla edges, the four-influence palette abruptly substituted Hips for LeftArm. A1 smoothing followed by top-four projection moved the discontinuity. Retaining the fifth influence in the same small connected field avoided that truncation.',
      'attempts':[
       {'attempt':1,'status':'FAIL','decision':'REJECTED','method':'Bounded edge-connected weight diffusion, duplicate-position consistency, normalized top-four palette. 245 unique positions / 803 render vertices changed.',
        'result':'Some >2x edges reduced, but Attack worst ratio 3.8496 to 4.6441 and new >4x edges appeared. No geometry or pose edits.'},
       {'attempt':2,'status':'UNVERIFIED','decision':'RETAINED_AS_BEST_EXPERIMENT','method':'Restore baseline weights, repeat the bounded field using five influences maximum.',
        'result':'578 integer samples across nine unchanged original/diagnostic clips. All >4x qualified edge warnings are zero; Run worst 4.0792 to 3.9343; Idle 4.4057 to 3.9491. Attack worst slightly increases 3.8496 to 3.9184; counts of >2x warnings decrease for all nine clips. Same-camera gray close-up shows reduced localized fold stretch, but remaining folds and whole-garment naturalness are not automatically passed.'}
      ],
      'comparisons':summaries,
      'limitations':['Integrated garment has no trustworthy separate full inner body; full penetration metric UNVERIFIED.',
                     'The numerical threshold is diagnostic, not a quality acceptance threshold.',
                     'Static ragged edges and broad sleeve folds remain. No cloth physics exists in Stage A.',
                     'Standalone A2 five-influence exception still needs final merged FBX/Unity preservation check.'],
      'additional_paid_requests':0,'additional_paid_credits':0}
    (LAB/'Body/body_result.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf8')
    state_path=LAB/'run_state.json'
    state=json.loads((state_path if state_path.exists() else LAB/'run_state.draft.json').read_text(encoding='utf8'))
    state.pop('attempts_used_draft',None);state['is_draft']=False;state['note']='Live experimental state; no integrated character quality approval.'
    state['phase']='BODY_COMPARISON_MOTION_AND_FACE_TESTS'
    state['attempts_used']={'A':2,'B':0,'C':3,'D':0}
    state['best_files']={'A':'Body/A2.blend','B':None,'C':None,'D':None}
    state['additional_paid_credits']=0;state['additional_paid_requests']=0
    state['verdicts'].update(body_clothing_deformation='UNVERIFIED',animation_naturalness='UNVERIFIED',face_blink='UNVERIFIED',face_gaze='FAIL',face_jaw='NOT_ATTEMPTED',fbx_roundtrip='UNVERIFIED',unity_integration='UNVERIFIED')
    state_path.write_text(json.dumps(state,ensure_ascii=False,indent=2),encoding='utf8')
    print('Body experiment verdict and live state recorded')

if __name__=='__main__':run()
