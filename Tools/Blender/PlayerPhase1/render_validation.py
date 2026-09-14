import bpy,json,math,subprocess
from pathlib import Path
from mathutils import Vector
OUT=Path('C:/Users/yj666/Oheangbu/Art/PlayerPhase1');s=bpy.context.scene;a=bpy.data.objects['Dosa_Phase1_Rig']
# Audit every integer frame, independent of video subsampling.
code=Path('C:/Users/yj666/Oheangbu/Tools/Blender/PlayerPhase1/audit_poses.py').read_text(encoding='utf-8').replace('sorted(set([1,end//4,end//2,3*end//4,end]))','range(1,end+1)').replace('print(json.dumps(results))','print("Full frame audit saved",flush=True)').replace('5 sampled frames per action.','Every integer frame of all five diagnostic actions.')
exec(compile(code,'audit_all_frames.py','exec'))
s.render.resolution_x=960;s.render.resolution_y=540;s.cycles.samples=8
c=s.camera;c.location=(2.4,-4.5,2.1);c.rotation_euler=(Vector((0,0,1.05))-c.location).to_track_quat('-Z','Y').to_euler();c.data.ortho_scale=4.15
for label in ['Walk','Run','ArmsUp','Squat','BrushSwing']:
 a.animation_data.action=bpy.data.actions['DIAG_'+label];end=int(a.animation_data.action.frame_range[1]);folder=OUT/'Previews'/label;folder.mkdir(exist_ok=True)
 for i,f in enumerate(range(1,end+1,2),1):
  s.frame_set(f);s.render.filepath=str(folder/f'{i:04d}.png');bpy.ops.render.render(write_still=True)
 print('DONE',label,flush=True)
a.animation_data.action=None
for p in a.pose.bones:p.rotation_quaternion=(1,0,0,0);p.location=(0,0,0);p.scale=(1,1,1)
s.frame_set(1);s.render.resolution_x=1600;s.render.resolution_y=900;s.cycles.samples=16;c.data.ortho_scale=3.5
for label,pos in [('front',(0,-4,1.1)),('back',(0,4,1.1)),('side',(4,0,1.1))]:
 c.location=pos;c.rotation_euler=(Vector((0,0,.9))-c.location).to_track_quat('-Z','Y').to_euler();s.render.filepath=str(OUT/'Previews'/('Final_'+label+'.png'));bpy.ops.render.render(write_still=True)
(OUT/'Previews/render_complete.json').write_text(json.dumps({'videos_source_fps':12,'audit_fps':24,'motions':5}))
