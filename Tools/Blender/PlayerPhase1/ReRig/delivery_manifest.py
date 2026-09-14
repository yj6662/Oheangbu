from pathlib import Path
import hashlib,json,subprocess,datetime
import imageio_ffmpeg
OUT=Path('C:/Users/yj666/Oheangbu/Art/PlayerPhase1/ReRig');ff=imageio_ffmpeg.get_ffmpeg_exe()
files=['Dosa_Phase1_Rerig.blend','Dosa_Phase1_Rerig.fbx','README.md','rig_structure_audit.json','fbx_roundtrip.json','animation_roundtrip.json','unity_import_audit.json','before_solid_audit.json','solid_audit.json','pose_audit.json','Previews/Rerig_before_after.mp4','Previews/Rerig_motion_validation.mp4','Previews/HandFlex.mp4']
rows=[]
for name in files:
 p=OUT/name
 if p.suffix=='.mp4':subprocess.run([ff,'-v','error','-i',str(p),'-f','null','-'],check=True)
 rows.append({'path':name,'bytes':p.stat().st_size,'sha256':hashlib.sha256(p.read_bytes()).hexdigest()})
unity=Path('C:/Users/yj666/Oheangbu/Oheangbu/Assets/_Project/Art/Models/PlayerPhase1Validation/ReRig/Dosa_Phase1_Rerig.fbx')
report={'timestamp_utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'status':'STRUCTURE_AND_IMPORT_PASS_WITH_RESIDUAL_CONTACT_NOT_RIG_PASS','additional_meshy_credits':0,'files':rows,'unity_fbx_matches_export':hashlib.sha256(unity.read_bytes()).hexdigest()==next(r['sha256'] for r in rows if r['path'].endswith('.fbx')),'video_decode':'PASS','canonical_player':'capsule_unchanged'}
(OUT/'delivery_manifest.json').write_text(json.dumps(report,indent=2))
print(json.dumps({'files':len(rows),'video_decode':report['video_decode'],'unity_fbx_matches_export':report['unity_fbx_matches_export']}))
