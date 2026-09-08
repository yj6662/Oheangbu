"""Use identical explicit camera/light frames for Baseline and A2.
The original clip renderer supplies complete integer-frame timing and isolated scene.
No source edits are performed. Run start(stage) inside Blender only.
"""
from pathlib import Path
import bpy
from mathutils import Vector
ROOT=Path('C:/Users/yj666/Oheangbu')
LAB=ROOT/'Art/PlayerPhase1/C02_RigFaceLab'
exec(compile((ROOT/'Tools/Blender/AutoPlayerV1/render_clips.py').read_text(encoding='utf8'),'render_clips.py','exec'))

def rc_frame_camera(state,job):
    close='Axilla' in job['label']
    center=Vector((.15,.02,1.18) if close else (0,0,1.05))
    direction=Vector((0,1,0) if close else (0,-1,0))
    camera=state['scene'].camera;camera.location=center+direction*5
    camera.rotation_euler=(-direction).to_track_quat('-Z','Y').to_euler()
    camera.data.ortho_scale=.65 if close else 4.4
    for light,offset,energy,size in state['lights']:
        light.location=Vector((0,0,.95))+Vector(offset)
        light.rotation_euler=(Vector((0,0,.95))-light.location).to_track_quat('-Z','Y').to_euler()
        light.data.energy,light.data.size=energy,size
    job['camera']={'location':list(camera.location),'rotation_euler':list(camera.rotation_euler),'ortho_scale':camera.data.ortho_scale,'target':list(center),'fixed_for_entire_clip':True,'same_for_baseline_and_A2':True,'lights_fixed':True}

def start(stage):
    if stage not in ('Baseline','A2'):raise ValueError(stage)
    expected=LAB/('Body/BaselineTests.blend' if stage=='Baseline' else 'Body/A2.blend')
    if Path(bpy.data.filepath).resolve()!=expected.resolve():raise ValueError('Load explicit source '+str(expected))
    jobs=[{'action':'C02_Run','label':'Run_Full','views':['front']},
          {'action':'C02_Run','label':'Run_Axilla','views':['back']},
          {'action':'C02_Idle','label':'Idle_Axilla','views':['back']},
          {'action':'C02_Attack','label':'Attack_Full','views':['front']}]
    result=start_render_jobs('Armature',['C02_Mesh_0'],jobs,LAB/'Body'/('Video_'+stage),samples=8)
    import json,hashlib
    (LAB/'Body'/('Video_'+stage)/'source.json').write_text(json.dumps({'stage':stage,'source':str(expected),'sha256':hashlib.sha256(expected.read_bytes()).hexdigest(),'actual_source_model':True,'actions_original_unchanged':True,'camera_protocol':__file__ if '__file__' in globals() else 'body_compare_video.py'},indent=2),encoding='utf8')
    return result
