"""D1: three presets of existing blink channels, plus source body-motion replay.
No new geometry, shape keys, rig bones, altered source Actions or anatomy claim.
"""
import bpy,json,math,subprocess
from pathlib import Path
from mathutils import Vector,Matrix
ROOT=Path('C:/Users/yj666/Oheangbu');FACE=ROOT/'Art/PlayerPhase1/C02_RigFaceLab/Face';OUT=FACE/'D1';OUT.mkdir(exist_ok=True)
rig=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE');lids=[bpy.data.objects['FaceLid_Left'],bpy.data.objects['FaceLid_Right']]
presets={'GentleSquint':{'BlinkLeft':.35,'BlinkRight':.35},'FocusedSquint':{'BlinkLeft':.65,'BlinkRight':.65},'EyesClosed':{'BlinkLeft':1.,'BlinkRight':1.}}
def set_channels(left=0,right=0):
 lids[0].data.shape_keys.key_blocks['BlinkLeft'].value=left;lids[1].data.shape_keys.key_blocks['BlinkRight'].value=right;bpy.context.view_layer.update()
def neutral():
 rig.animation_data_clear()
 for p in rig.pose.bones:p.matrix_basis.identity()
 set_channels();bpy.context.view_layer.update()
neutral();rig.data.pose_position='POSE';s=bpy.context.scene
s.render.engine='BLENDER_EEVEE';s.render.fps=30;s.render.fps_base=1
if hasattr(s.eevee,'taa_render_samples'):s.eevee.taa_render_samples=8
s.render.resolution_x=1280;s.render.resolution_y=720;s.render.resolution_percentage=100;s.render.image_settings.file_format='PNG'
target=Vector((0,-.025,1.595));camera=s.camera;camera.data.type='ORTHO';camera.data.ortho_scale=.55
def aim(at):
 camera.location=at+Vector((0,-2,0));camera.rotation_euler=(at-camera.location).to_track_quat('-Z','Y').to_euler()
aim(target);offset=target-(rig.matrix_world@rig.pose.bones['Head'].head)
rig['face_presets_json']=json.dumps(presets);rig['face_presets_scope']='Blink only; no brow, lip, jaw or gaze channel.'
for name,vals in presets.items():
 set_channels(vals['BlinkLeft'],vals['BlinkRight']);s.render.filepath=str(OUT/(name+'.png'));bpy.ops.render.render(write_still=True)
neutral();aim(target);bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'Face_D1.blend'))
source_actions=['C02_Idle','C02_Run','C02_Attack']
missing=[a for a in source_actions if not bpy.data.actions.get(a)]
if missing:raise RuntimeError('Source actions missing: '+str(missing))
frames=OUT/'CombinedFrames';frames.mkdir(exist_ok=True);timeline=[];max_error=0;index=0;ranges=[]
for name in source_actions:
 action=bpy.data.actions[name];first,last=map(lambda v:int(round(v)),action.frame_range)
 rig.animation_data_create();rig.animation_data.action=action
 slots=[slot for slot in action.slots if slot.target_id_type=='OBJECT']
 if len(slots)!=1:raise RuntimeError('Ambiguous source action slots')
 rig.animation_data.action_slot=slots[0]
 start=index+1;repetitions=3 if name=='C02_Run' else 1
 for cycle in range(repetitions):
  for f in range(first,last+1):
   s.frame_set(f);local=(index-start+1);phase=local%45
   blink=max(0,1-abs(phase-20)/5) if 15<=phase<=25 else 0
   base=[0,.35,.65][(local//45)%3];value=max(base,blink);set_channels(value,value)
   aim((rig.matrix_world@rig.pose.bones['Head'].head)+offset)
   skin=rig.matrix_world@rig.pose.bones['Head'].matrix@rig.data.bones['Head'].matrix_local.inverted()@rig.matrix_world.inverted()
   for o in lids:
    e=o.evaluated_get(bpy.context.evaluated_depsgraph_get());m=e.to_mesh();key=o.data.shape_keys.key_blocks[1];basis=o.data.shape_keys.key_blocks[0]
    for vi,v in enumerate(m.vertices):
     local_co=basis.data[vi].co+value*(key.data[vi].co-basis.data[vi].co)
     expected=skin@o.matrix_world@local_co;actual=e.matrix_world@v.co;max_error=max(max_error,(expected-actual).length)
    e.to_mesh_clear()
   index+=1;s.render.filepath=str(frames/f'frame_{index:06d}.png');bpy.ops.render.render(write_still=True)
   timeline.append({'output_frame':index,'action':name,'source_frame':f,'cycle':cycle+1,'blink_left':value,'blink_right':value})
 ranges.append({'action':name,'source_range':[first,last],'output_range':[start,index],'repetitions':repetitions,'source_fps':30})
neutral();aim(target)
ffmpeg='C:/Users/yj666/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/Lib/site-packages/imageio_ffmpeg/binaries/ffmpeg-win-x86_64-v7.1.exe'
video=OUT/'Face_D1_Idle_Run_Attack_30fps.mp4';command=[ffmpeg,'-y','-hide_banner','-loglevel','error','-framerate','30','-i',str(frames/'frame_%06d.png'),'-frames:v',str(index),'-an','-c:v','libx264','-crf','18','-pix_fmt','yuv420p','-movflags','+faststart',str(video)]
encoded=subprocess.run(command,capture_output=True,text=True);decoded=subprocess.run([ffmpeg,'-hide_banner','-loglevel','error','-xerror','-i',str(video),'-f','null','-'],capture_output=True,text=True) if encoded.returncode==0 else None
manifest={'candidate':'D1','stage':'D','attempt':1,'source_face_candidate':'C3','presets':presets,'channels':['BlinkLeft','BlinkRight'],'added_meshes':[],'added_shape_keys':[],'geometry_modified':False,'body_weights_modified':False,
 'face_expressions':{'status':'PASS','scope':'Three presets drive existing bilateral eyelid-coverage channels only; facial emotion/art quality is not passed.'},
 'body_face_binding':{'status':'PASS' if max_error<1e-5 else 'FAIL','max_head_skin_formula_error_m':max_error,'scope':'Every rendered frame and every eyelid vertex matches the single Head-bone skin transform; no double head transform'},
 'video':str(video),'video_status':'PASS_ENCODING' if encoded.returncode==0 and decoded and decoded.returncode==0 else 'FAIL','frames':index,'fps':30,'duration_seconds':index/30,'resolution':[1280,720],
 'clips':ranges,'source_clips_edited':False,'camera':'Head-position-following external face-review camera, fixed front orientation. Clip cuts are not claimed to be smoothed gameplay transitions.',
 'source_body':'Unmodified C02 Repair2, not the root task body A/B improvements','timeline':timeline,'paid_requests':0,'adopted':False}
(OUT/'manifest.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8');print('FACE_D1_COMPLETE',json.dumps({k:v for k,v in manifest.items() if k!='timeline'}))
