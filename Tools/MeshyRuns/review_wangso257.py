"""Blender background geometry and native motion renders; no scene mutation."""
import bpy,sys,json,math
from pathlib import Path
from mathutils import Vector
args=sys.argv[sys.argv.index('--')+1:];name,mode=args
root=Path('C:/Users/yj666/Oheangbu/Art/Characters/Principal257')
paths={'geometry':'geometry/model_urls_glb.glb','texture':'texture/model_urls_glb.glb','walk':'rig/result_basic_animations_walking_glb_url.glb','run':'rig/result_basic_animations_running_glb_url.glb','body':'rig/result_rigged_character_glb_url.glb'}
paths['riginput']='rig_input/Wangso.glb'
paths.update({'preparedwalk':'prepared/Walk.glb','preparedrun':'prepared/Run.glb','preparedbody':'prepared/Body.glb'})
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=str(root/name/paths[mode]))
scene=bpy.context.scene;meshes=[o for o in scene.objects if o.type=='MESH' and not o.hide_render];arms=[o for o in scene.objects if o.type=='ARMATURE']

# Preview intended smooth normals; source files remain untouched.
for obj in meshes:
 for polygon in obj.data.polygons: polygon.use_smooth=True
if arms: meshes=[o for o in meshes if len(o.vertex_groups)>0]
scene.frame_set(1)
scene.render.engine='BLENDER_EEVEE';scene.render.resolution_x=1000;scene.render.resolution_y=1200;scene.render.resolution_percentage=100
world=bpy.data.worlds.new('ReviewWorld');world.use_nodes=True;bg=world.node_tree.nodes.new('ShaderNodeBackground');wo=world.node_tree.nodes.new('ShaderNodeOutputWorld');world.node_tree.links.new(bg.outputs[0],wo.inputs['Surface']);bg.inputs[0].default_value=(.32,.34,.33,1);bg.inputs[1].default_value=.6;scene.world=world;scene.view_settings.view_transform='AgX'
def bounds():
 dep=bpy.context.evaluated_depsgraph_get();pts=[]
 for obj in meshes:
  e=obj.evaluated_get(dep);m=e.to_mesh();pts.extend(e.matrix_world@v.co for v in m.vertices);e.to_mesh_clear()
 lo=Vector([min(p[i] for p in pts) for i in range(3)]);hi=Vector([max(p[i] for p in pts) for i in range(3)])
 return lo,hi
lo,hi=bounds();height=hi.z-lo.z;focus=(lo+hi)/2
bpy.ops.object.camera_add();camera=bpy.context.object;scene.camera=camera;camera.data.type='ORTHO';camera.data.ortho_scale=max(height*1.15,(hi.x-lo.x)*1.5)
for side,energy in [((2,-3,4),650),((-3,-1,2),400),((1,3,3),450)]:
 bpy.ops.object.light_add(type='AREA',location=focus+Vector(side)*height/1.8);lamp=bpy.context.object;lamp.data.energy=energy*(height/1.8)**2;lamp.data.size=height*2;lamp.rotation_euler=(focus-lamp.location).to_track_quat('-Z','Y').to_euler()
motion=mode in ('walk','run','preparedwalk','preparedrun');samples=[]
frames=[1,8,16,24] if motion else [1]
for frame in frames:
 scene.frame_set(frame);lo,hi=bounds();samples.append(dict(frame=frame,minimum=list(lo),maximum=list(hi)))
 for view,angle in ([('front',0),('side',math.pi/2)] if motion else [('front',0),('quarter',math.pi/5),('back',math.pi),('side',math.pi/2)]):
  camera.location=focus+Vector((math.sin(angle)*height*3,-math.cos(angle)*height*3,height*.03));camera.rotation_euler=(focus-camera.location).to_track_quat('-Z','Y').to_euler()
  scene.render.filepath=str(root/name/f'{mode}_{view}_{frame}.png');bpy.ops.render.render(write_still=True)
# Head closeups with actual material, and a clean clay pass to validate real facial relief.
if not motion:
 scene.render.resolution_x=1000;scene.render.resolution_y=1000
 focus=Vector(((lo.x+hi.x)/2,(lo.y+hi.y)/2,hi.z-height*.077))
 camera.data.ortho_scale=height*.23
 for layer in ['head','clay']:
  if layer=='clay':
   clay=bpy.data.materials.new('DiagnosticClay');clay.diffuse_color=(.35,.35,.35,1);clay.use_nodes=True
   principled=clay.node_tree.nodes.new('ShaderNodeBsdfPrincipled');outnode=clay.node_tree.nodes.new('ShaderNodeOutputMaterial');clay.node_tree.links.new(principled.outputs[0],outnode.inputs['Surface']);principled.inputs['Base Color'].default_value=(.35,.35,.35,1);principled.inputs['Roughness'].default_value=.85
   for obj in meshes:
    for i in range(len(obj.data.materials)):obj.data.materials[i]=clay
  for view,angle in [('front',0),('quarter',math.pi/5),('side',math.pi/2)]:
   camera.location=focus+Vector((math.sin(angle)*height*3,-math.cos(angle)*height*3,0));camera.rotation_euler=(focus-camera.location).to_track_quat('-Z','Y').to_euler()
   scene.render.filepath=str(root/name/f'{mode}_{layer}_{view}.png');bpy.ops.render.render(write_still=True)
report=dict(character=name,mode=mode,bones=[len(a.data.bones) for a in arms],triangles=sum(sum(len(p.vertices)-2 for p in o.data.polygons) for o in meshes),samples=samples,scope='Blender inspection only; not Unity runtime validation')
(root/name/f'{mode}_review.json').write_text(json.dumps(report,indent=2))
