import bpy,json
from pathlib import Path
from mathutils import Vector
R=Path('C:/Users/yj666/Oheangbu');O=R/'Art/SpellVFX120/StoneDokkaebi'
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=str(O/'Meshy/064_BAB8/preview/model_urls_glb.glb'))
obj=next(o for o in bpy.context.scene.objects if o.type=='MESH')
bpy.context.view_layer.objects.active=obj;obj.select_set(True);bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
v=[v.co for v in obj.data.vertices];lo=Vector(tuple(min(p[i] for p in v) for i in range(3)));hi=Vector(tuple(max(p[i] for p in v) for i in range(3)))
factor=2.05/(hi.z-lo.z)
for p in obj.data.vertices:p.co=(p.co-Vector(((lo.x+hi.x)/2,(lo.y+hi.y)/2,lo.z)))*factor
print('BOUNDS',list(lo),list(hi),'NORMALIZED',list(obj.dimensions),flush=True)
scene=bpy.context.scene;scene.world=bpy.data.worlds.new('ReviewWorld');scene.render.engine='CYCLES';scene.cycles.samples=8;scene.cycles.use_denoising=True;scene.render.threads_mode='FIXED';scene.render.threads=2;scene.world.color=(.2,.2,.2)
for loc in [(3,-4,5),(-3,-2,4),(1,3,4)]:
 bpy.ops.object.light_add(type='AREA',location=loc);l=bpy.context.object;l.data.energy=700;l.data.size=4;l.rotation_euler=(Vector((0,0,1))-l.location).to_track_quat('-Z','Y').to_euler()
bpy.ops.object.camera_add(location=(3,-5,2.7));cam=bpy.context.object;cam.rotation_euler=(Vector((0,0,1))-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.type='ORTHO';cam.data.ortho_scale=4;scene.camera=cam;scene.render.resolution_x=1280;scene.render.resolution_y=720;scene.render.resolution_percentage=100;scene.render.filepath=str(O/'geometry_inspect.png');bpy.ops.render.render(write_still=True)
bpy.ops.wm.save_as_mainfile(filepath=str(O/'GeometryInspection.blend'))
