"""Record explicit review verdicts, checksums and cost; never infer art PASS from files."""
import argparse,json,hashlib
from pathlib import Path
from datetime import datetime,timezone
ROOT=Path('C:/Users/yj666/Oheangbu');LAB=ROOT/'Art/PlayerPhase1/C02_RigFaceLab'

def finalize(unity_status,unity_scope):
    def load(name):return json.loads((LAB/name).read_text(encoding='utf8'))
    def save(name,value):(LAB/name).write_text(json.dumps(value,ensure_ascii=False,indent=2),encoding='utf8')
    body=load('Body/body_result.json');motion=load('Motion/experiment_result.json');face=load('Face/D1/manifest.json')
    roundtrip_path='Final/fbx_roundtrip_B2_C3.json'
    report=load(roundtrip_path)
    if report['status']!='PASS_SERIALIZATION_ONLY':
        raise RuntimeError('B2 serialization gate has not passed')
    maximum_error=max(row['max_world_nearest_error_m'] for row in report['checks'])
    report['tested_fbx_sha256']=hashlib.sha256((LAB/'Final/Integrated_B2_C3.fbx').read_bytes()).hexdigest()
    save(roundtrip_path,report)
    verdicts={
      'body_clothing_deformation':{'status':'UNVERIFIED','scope':'A2 left lower axilla improvement on unchanged clips; full integrated garment penetration and naturalness not passed.'},
      'animation_naturalness':motion['verdicts']['animation_naturalness'],
      'secondary_motion':motion['verdicts']['secondary_motion'],
      'face_blink':{'status':'UNVERIFIED','scope':'Added bilateral eyelid coverage, neutral return and channel/binding verified. Residual intermediate contact and coarse appearance prevent full quality acceptance.'},
      'face_gaze':{'status':'FAIL','scope':'C2 local eyeballs were overexposed and failed closure. Retained C3 uses original fixed painted eyes.'},
      'face_jaw':{'status':'NOT_ATTEMPTED','scope':'Closed fused lip surface/no established oral cavity; three basic face candidates spent on blink and gaze. No claim that repair is impossible.'},
      'face_expressions':face['face_expressions'],
      'fbx_roundtrip':{'status':'PASS','scope':f"B2 final FBX empty-scene reimport, {report['cases']} pose/blink cases and {report['mesh_samples']} mesh comparisons; nearest world error {maximum_error:.9g}m, same triangles/bones/keys/five-weight counts. Not art/contact acceptance."},
      'unity_integration':{'status':unity_status,'scope':unity_scope},
      'art_review':{'status':'UNVERIFIED','scope':'Comparison media provided for user judgement; no automated design approval.'}}
    artifacts={
      'integrated_blend':'Final/Integrated_B2_C3.blend','integrated_fbx':'Final/Integrated_B2_C3.fbx',
      'original_texture':'Final/Textures/C02_original_texture.png','roundtrip':roundtrip_path,
      'body_comparison_before':'Body/Video_Baseline/Run_Axilla_back.mp4',
      'body_comparison_after':'Body/Video_A2/Run_Axilla_back.mp4',
      'previous_b1_motion_comparison':'Motion/B1/B1_Original_vs_Edited_Sequence.mp4',
      'face_video':'Face/D1/Face_D1_Idle_Run_Attack_30fps.mp4',
      'unity_body_video':'Unity/B2_Final_Sequence_Full/B2_Final_Sequence_Full.mp4',
      'unity_face_video':'Unity/B2_Final_Blink_Close/B2_Final_Blink_Close.mp4',
      'unity_report':'Unity/UNITY_REPORT.md'}
    for name,path in artifacts.items():
      p=LAB/path
      if not p.is_file():raise FileNotFoundError(path)
      artifacts[name]={'path':path,'bytes':p.stat().st_size,'sha256':hashlib.sha256(p.read_bytes()).hexdigest()}
    final={'schema_version':1,'experiment':'C02_RigFaceLab','status':'PARTIAL_UNVERIFIED',
      'execution_status':'COMPLETED_WITH_RECORDED_LIMITATIONS','completed_utc':datetime.now(timezone.utc).isoformat(),
      'canonical_player_replaced':False,'integrated_character_quality':'UNVERIFIED',
      'attempts_used':{'A':2,'B':2,'C':3,'D':1},'attempt_limits':{'A':4,'B':3,'C':3,'D':3},
      'best_files':{'A':'Body/A2.blend','B':'Motion/B2/Motion_B2.blend','C':'Face/C3/Face_C3.blend','D':'Face/D1/Face_D1.blend'},
      'best_integrated_file':'Final/Integrated_B2_C3.blend','verdicts':verdicts,'artifacts':artifacts,
      'actual_triangles':52784,'triangle_limit':60000,'deform_bones':24,'new_clothing_bones':0,
      'blendshape_count':2,'renderer_count':3,'material_count':2,
      'additional_paid_requests':0,'additional_paid_credits':0,'historical_autoplayer_v1_credits':123,
      'historical_credits_are_new_spend':False,
      'source_preservation':'review_source_verification.json',
      'known_limits':['Integrated garment/body lacks reliable full penetration target.',
        'B1 Run whole-mesh min -24.32mm and Attack support-window horizontal extent 147.56mm remain.',
        'B2 corrects inherited Idle Hips scale 1.1764704 to 1 and re-solves pelvis/feet with fixed root and floor. Legacy B1 clips remain comparison checkpoints.',
        'No new clothing secondary motion, finger articulation or brush-contact test.',
        'C3 intermediate right lid local depth up to -0.287mm; no successful gaze or jaw motion.',
        'Unity minimum imported influence clamps to .001; fifth-weight vertices 722 ->297 exactly match the original threshold distribution.']}
    save('final_summary.json',final)
    state=load('run_state.json');state.update(phase='COMPLETED_WITH_RECORDED_LIMITATIONS',status=final['status'],attempts_used=final['attempts_used'],best_files=final['best_files'],verdicts=verdicts)
    save('run_state.json',state)
    save('additional_cost_ledger.json',{'budget_credits':0,'paid_requests':[],'actual_additional_credits':0,'historical_autoplayer_v1_credits':123,'historical_is_current_cost':False})
    manifest=load('Final/integration_manifest_B2_C3.json')
    manifest['export_status']=manifest.get('export_status',manifest['status'])
    manifest.update(status='DATA_PIPELINE_VERIFIED_WITH_QUALITY_LIMITATIONS',integrated_quality='UNVERIFIED',
                    roundtrip_evidence=roundtrip_path,unity_evidence='Unity/unity_validation.json')
    save('Final/integration_manifest_B2_C3.json',manifest)
    print(json.dumps({'status':final['status'],'verdicts':{k:v['status'] for k,v in verdicts.items()},'additional_paid_credits':0}))

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--unity-status',choices=['PASS','FAIL','UNVERIFIED'],required=True);p.add_argument('--unity-scope',required=True)
    a=p.parse_args();finalize(a.unity_status,a.unity_scope)
