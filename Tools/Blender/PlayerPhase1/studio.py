import bpy,math
from mathutils import Vector
from pathlib import Path
OUT=Path('C:/Users/yj666/Oheangbu/Art/PlayerPhase1');(OUT/'Previews').mkdir(exist_ok=True)
s=bpy.context.scene
s.render.engine='CYCLES';s.cycles.samples=16;s.cycles.use_denoising=True
s.render.resolution_x=1280;s.render.resolution_y=720;s.render.resolution_percentage=100
s.world=bpy.data.worlds.new('Phase1_Neutral');s.world.use_nodes=True;next(n for n in s.world.node_tree.nodes if n.type=='BACKGROUND').inputs[0].default_value=(0.28,0.28,0.28,1);next(n for n in s.world.node_tree.nodes if n.type=='BACKGROUND').inputs[1].default_value=0.5
def aim(o,p):o.rotation_euler=(Vector(p)-o.location).to_track_quat('-Z','Y').to_euler()
cam=bpy.data.objects.new('Phase1_Camera',bpy.data.cameras.new('Phase1_Camera'));s.collection.objects.link(cam);cam.location=(0,-4.2,1.15);aim(cam,(0,0,0.9));cam.data.type='ORTHO';cam.data.ortho_scale=3.4;s.camera=cam
for name,pos,power,size in [('Key',(-2,-3,4),450,4),('Fill',(3,-1,2),250,3),('Rim',(0,2,3),350,3)]:
 d=bpy.data.lights.new('Phase1_'+name,'AREA');d.energy=power;d.shape='DISK';d.size=size;o=bpy.data.objects.new(d.name,d);s.collection.objects.link(o);o.location=pos;aim(o,(0,0,1))
s.view_settings.view_transform='AgX'
s.render.image_settings.file_format='PNG'
s.render.filepath=str(OUT/'Previews/Body_initial.png');bpy.ops.render.render(write_still=True)

