import bpy,json,math
from pathlib import Path
from mathutils import Vector
ROOT=Path('C:/Users/yj666/Oheangbu');OUT=ROOT/'Art/Characters/Sinmok272/rig';OUT.mkdir(parents=True,exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=str(ROOT/'Art/Characters/Sinmok272/sinmok/model/model_urls_glb.glb'))
scene=bpy.context.scene;meshes=[o for o in scene.objects if o.type=='MESH']
points=[o.matrix_world@v.co for o in meshes for v in o.data.vertices];lo=Vector([min(p[i] for p in points) for i in range(3)]);hi=Vector([max(p[i] for p in points) for i in range(3)])
height=hi.z-lo.z;center=Vector(((hi.x+lo.x)/2,(hi.y+lo.y)/2,lo.z))
for o in meshes:
 for v in o.data.vertices:v.co=(o.matrix_world@v.co-center)*13/height
 o.matrix_world.identity()
lo=(lo-center)*13/height;hi=(hi-center)*13/height
(OUT/'bounds.json').write_text(json.dumps({'lo':list(lo),'hi':list(hi),'meshes':[{'name':o.name,'vertices':len(o.data.vertices),'triangles':sum(len(p.vertices)-2 for p in o.data.polygons)}for o in meshes]},indent=2))
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'normalized.blend'))
scene.render.engine='BLENDER_EEVEE';scene.render.resolution_x=1100;scene.render.resolution_y=1000;scene.render.resolution_percentage=100
scene.world=bpy.data.worlds.new('Review');scene.world.color=(.35,.35,.35);scene.view_settings.view_transform='AgX'
focus=Vector((0,0,6.5));bpy.ops.object.camera_add();cam=bpy.context.object;scene.camera=cam;cam.data.type='ORTHO';cam.data.ortho_scale=max(15,(hi.x-lo.x)*1.1)
for pos,energy in [((10,-20,22),18000),((-16,-12,13),12000),((4,15,20),16000)]:
 bpy.ops.object.light_add(type='AREA',location=pos);o=bpy.context.object;o.data.energy=energy;o.data.size=14;o.rotation_euler=(focus-o.location).to_track_quat('-Z','Y').to_euler()
for name,angle in [('front',0),('back',math.pi),('side',math.pi/2)]:
 cam.location=focus+Vector((math.sin(angle)*35,-math.cos(angle)*35,0));cam.rotation_euler=(focus-cam.location).to_track_quat('-Z','Y').to_euler();scene.render.filepath=str(OUT/(name+'.png'));bpy.ops.render.render(write_still=True)
