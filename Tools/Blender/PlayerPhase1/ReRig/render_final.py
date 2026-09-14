import bpy,json
from pathlib import Path
from mathutils import Vector
OUT=Path('C:/Users/yj666/Oheangbu/Art/PlayerPhase1/ReRig');CODE=Path('C:/Users/yj666/Oheangbu/Tools/Blender/PlayerPhase1/ReRig')
exec(compile((CODE/'audit_solid.py').read_text(),'audit_solid.py','exec'))
exec(compile((CODE/'audit_full.py').read_text(),'audit_full.py','exec'))
s=bpy.context.scene;a=bpy.data.objects['Dosa_Phase1_Rig'];c=s.camera
s.render.resolution_x=960;s.render.resolution_y=540;s.render.resolution_percentage=100;s.cycles.samples=8
c.location=(2.4,-4.5,2.1);c.rotation_euler=(Vector((0,0,1.05))-c.location).to_track_quat('-Z','Y').to_euler();c.data.ortho_scale=4.15
for label in ['Walk','Run','ArmsUp','Squat','BrushSwing','HandFlex']:
 a.animation_data.action=bpy.data.actions['DIAG_'+label];end=int(a.animation_data.action.frame_range[1]);folder=OUT/'Previews'/label;folder.mkdir(exist_ok=True)
 if label=='HandFlex':
  c.location=(.78,-.10,2.0);c.rotation_euler=(Vector((.77,.05,1.39))-c.location).to_track_quat('-Z','Y').to_euler();c.data.ortho_scale=.32
  for name in ['InnerTop','Durumagi']:bpy.data.objects[name].hide_render=True
 for i,f in enumerate(range(1,end+1,2),1):
  s.frame_set(f);s.render.filepath=str(folder/f'{i:04d}.png');bpy.ops.render.render(write_still=True)
 print('RENDERED',label,flush=True)
for name in ['InnerTop','Durumagi']:bpy.data.objects[name].hide_render=False
a.animation_data.action=None
for p in a.pose.bones:p.rotation_quaternion=(1,0,0,0);p.location=(0,0,0);p.scale=(1,1,1)
s.render.resolution_x=1600;s.render.resolution_y=1000;s.cycles.samples=16;c.data.ortho_scale=3.25
for label,pos in [('front',(0,-4,1.1)),('back',(0,4,1.1)),('side',(4,0,1.1))]:
 c.location=pos;c.rotation_euler=(Vector((0,0,.9))-c.location).to_track_quat('-Z','Y').to_euler();s.render.filepath=str(OUT/'Previews'/('Final_'+label+'.png'));bpy.ops.render.render(write_still=True)
(OUT/'Previews/render_complete.json').write_text(json.dumps({'source_fps':24,'video_fps':12,'motions':6,'audit_frames':186}))
