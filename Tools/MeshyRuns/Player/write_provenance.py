"""Local evidence only; intentionally excludes credentials and signed asset URLs."""
import json,pathlib,hashlib,datetime
ROOT=pathlib.Path(__file__).resolve().parents[3];ART=ROOT/'Art/Player';SRC=ART/'MeshySources';UNITY=ROOT/'Oheangbu/Assets/_Project/Art/Characters/Dosa'
def read(p):return json.loads(p.read_text(encoding='utf-8-sig'))
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
tasks=[]
for task in read(SRC/'tasks.json'):
    response=SRC/('rig-refreshed-response.json' if task['name']=='Dosa_RigRefresh' else task['name']+'-response.json')
    result=read(response)
    tasks.append({'name':task['name'],'type':task.get('type','preset'),'task_id':task['task_id'],'expected_credits':task['expected_credits'],'consumed_credits':result.get('consumed_credits'),'status':result.get('status'),'request':task.get('request')})
legacy=pathlib.Path('C:/Users/yj666/MandateOfInk/MandateOfInk/Assets/_Project/Art/Models/Player')
sources=[legacy/'SM_DosaCourier_Rigged.fbx',legacy/'T_DosaCourier_base_color.png']+[legacy/('A_DosaCourier_'+n+'.fbx') for n in ['Idle','CombatReady','Walk','Run']]
files=[p for sub in ['Models','Animations','Textures'] for p in (UNITY/sub).glob('*') if p.suffix.lower() in ['.fbx','.png']]
provenance={'updated_utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'canonical_appearance':'Legacy Meshy Dosa Courier; Hyper3D variant intentionally excluded','legacy_sources':[{'path':str(p),'sha256':sha(p)} for p in sources],
'generated_motion_sources':[{'path':str(p.relative_to(ROOT)),'sha256':sha(p)} for p in SRC.iterdir() if p.suffix.lower() in ['.fbx','.bvh','.glb']],
'meshy':{'old_rig_task':'019fccf0-0200-7037-a1e5-582052a5824b','old_task_status':'404 unavailable; no continuity assumed','tasks':tasks,'balance_before':read(SRC/'balance-start.json')['balance'],'balance_after':read(SRC/'balance-final.json')['balance'],'actual_credits_used':sum(t['consumed_credits'] for t in tasks),'authorized_cap':45},
'model':read(ART/'export-models-report.json'),'motions':read(ART/'motion-export-report.json'),'files':[{'path':str(p.relative_to(ROOT)),'sha256':sha(p),'bytes':p.stat().st_size} for p in files],
'model_weight_corrections':{'waist_arm_contamination':{'report':'Art/Player/waist-arm-weight-report.json','report_sha256':sha(ART/'waist-arm-weight-report.json'),'changed_vertices':read(ART/'waist-arm-weight-report.json')['changed_vertices'],'validation':'Art/Player/waist-weight-export-validation.json','validation_sha256':sha(ART/'waist-weight-export-validation.json')}} if (ART/'waist-arm-weight-report.json').exists() else {},
'validation':{'blender_states_rendered':17,'body_pose_images':'Art/Player/MotionQA/*-final.png','final_wrist_image':'Art/Player/MotionQA/DrawGrip-wrap-textured.png','unity_validation_owner':'root and drawing_pose_legacy agents','visual_limitations':['Body retains source scan style and baked lighting.','New hand colour is sampled from the original clean skin patch; fine anatomical texture detail is limited.','Final runtime wrist/elbow pole and near-camera visibility require Unity capture verification.']}}
if (ART/'waist-sleeve-separation-report.json').exists():
    correction=read(ART/'waist-sleeve-separation-report.json')
    provenance['model_weight_corrections']['residual_scan_bridge_separation']={
        'report':'Art/Player/waist-sleeve-separation-report.json','report_sha256':sha(ART/'waist-sleeve-separation-report.json'),
        'changed_original_vertices':correction['changed_vertices'],'removed_scan_bridge_faces':correction['removed_scan_bridge_faces'],
        'added_lining_vertices':correction['added_lining_vertices'],'added_lining_triangles':correction['added_lining_triangles'],
        'validation':'Art/Player/waist-sleeve-export-validation.json','validation_sha256':sha(ART/'waist-sleeve-export-validation.json')}
    provenance['validation']['waist_pose_evidence']='Art/Player/waist-separation-preview-validation.json'
    provenance['validation']['waist_pose_scope']='Frozen DrawHold, High, Across, Idle are distinct evaluated meshes and rendered images; Unity final judge remains separate.'
provenance['validation']['runtime_grip_authority']='Oheangbu/Assets/_Project/Data/Player/DrawingPose_Dosa.asset; actual path and skin contact evidence are maintained by runtime owner.'
(ART/'PLAYER_ASSET_PROVENANCE.json').write_text(json.dumps(provenance,ensure_ascii=False,indent=2),encoding='utf-8')
print('provenance saved',len(files),'Unity files;',provenance['meshy']['actual_credits_used'],'credits')
