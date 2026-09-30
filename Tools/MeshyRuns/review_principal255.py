import bpy,sys,json,math
from pathlib import Path
from mathutils import Vector
root=Path('C:/Users/yj666/Oheangbu/Art/Characters/Principal255')
report=[]
for name in ('wangso','jeongdam'):
 bpy.ops.wm.read_factory_settings(use_empty=True)
 p=root/name/'rig/result_basic_animations_walking_glb_url.glb'
 bpy.ops.import_scene.gltf(filepath=str(p))
 meshes=[o for o in bpy.context.scene.objects if o.type=='MESH' and any(m.type=='ARMATURE' for m in o.modifiers)]
 arms=[o for o in bpy.context.scene.objects if o.type=='ARMATURE']
 scene=bpy.context.scene
 scene.render.engine='BLENDER_EEVEE';scene.render.resolution_x=900;scene.render.resolution_y=1100;scene.render.resolution_percentage=100
 scene.world=bpy.data.worlds.new('World');scene.world.use_nodes=True;bg=scene.world.node_tree.nodes.new('ShaderNodeBackground');out=scene.world.node_tree.nodes.new('ShaderNodeOutputWorld');scene.world.node_tree.links.new(bg.outputs[0],out.inputs['Surface']);bg.inputs[0].default_value=(.2,.22,.21,1);bg.inputs[1].default_value=.7
 scene.view_settings.view_transform='AgX'
 bpy.ops.object.camera_add();camera=bpy.context.object;scene.camera=camera;camera.data.type='ORTHO';camera.data.ortho_scale=2.2
 # Meshy glTF faces -Y after import. Slightly oblique to expose limb separation.
 camera.location=(2.5,-6,1.7);focus=Vector((0,0,.95));camera.rotation_euler=(focus-camera.location).to_track_quat('-Z','Y').to_euler()
 for loc,power,size in [((2,-4,4),600,4),((-3,-1,2),350,3)]:
  bpy.ops.object.light_add(type='AREA',location=loc);light=bpy.context.object;light.data.energy=power;light.data.shape='DISK';light.data.size=size;light.rotation_euler=(focus-light.location).to_track_quat('-Z','Y').to_euler()
 samples=[]
 for frame in (1,12,24):
  scene.frame_set(frame);deps=bpy.context.evaluated_depsgraph_get();points=[]
  for obj in meshes:
   evaluated=obj.evaluated_get(deps);mesh=evaluated.to_mesh();points.extend([evaluated.matrix_world@v.co for v in mesh.vertices]);evaluated.to_mesh_clear()
  lo=[min(p[i] for p in points) for i in range(3)];hi=[max(p[i] for p in points) for i in range(3)]
  samples.append({'frame':frame,'bounds_min':lo,'bounds_max':hi,'vertices':len(points)})
  scene.render.filepath=str(root/name/f'walk_{frame}.png');bpy.ops.render.render(write_still=True)
 report.append({'name':name,'bones':[len(a.data.bones) for a in arms],'triangles':sum(sum(len(p.vertices)-2 for p in m.data.polygons) for m in meshes),'samples':samples,'scope':'Meshy native walk sampling, not Unity locomotion/retarget validation'})
(root/'rig-review.json').write_text(json.dumps(report,indent=2))
