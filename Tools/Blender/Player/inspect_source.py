import bpy, math, os, json
from mathutils import Vector

ROOT='C:/Users/yj666/Oheangbu'
OUT=ROOT+'/Art/Player'
s=bpy.context.scene
bpy.context.preferences.view.use_translate_new_dataname=True
m=next(o for o in s.objects if o.type=='MESH')
a=next(o for o in s.objects if o.type=='ARMATURE')
mat=bpy.data.materials.get('Dosa_Source_Surface') or bpy.data.materials.new('Dosa_Source_Surface')
mat.use_nodes=True
nodes=mat.node_tree.nodes
bs=next(n for n in nodes if n.type=='BSDF_PRINCIPLED');bs.inputs['Roughness'].default_value=.85
tex=nodes.new('ShaderNodeTexImage')
tex.image=bpy.data.images.load('C:/Users/yj666/MandateOfInk/MandateOfInk/Assets/_Project/Art/Models/Player/T_DosaCourier_base_color.png',check_existing=True)
mat.node_tree.links.new(tex.outputs['Color'],bs.inputs['Base Color'])
m.data.materials.clear();m.data.materials.append(mat)
s.render.engine='CYCLES';s.cycles.samples=24
s.render.resolution_x=1000;s.render.resolution_y=1100;s.render.resolution_percentage=100
s.world=bpy.data.worlds.new('Dosa_Studio_World');s.world.use_nodes=True
bg=next(n for n in s.world.node_tree.nodes if n.type=='BACKGROUND')
bg.inputs[0].default_value=(.35,.35,.35,1);bg.inputs[1].default_value=.65
camdata=bpy.data.cameras.new('Dosa_QA_Camera');cam=bpy.data.objects.new('Dosa_QA_Camera',camdata)
s.collection.objects.link(cam);s.camera=cam;camdata.lens=60
cam.location=(2,-4,1.7);cam.rotation_euler=(Vector((0,0,.9))-cam.location).to_track_quat('-Z','Y').to_euler()
for name,pos,energy,size in [('Key',(2,-3,4),450,3),('Fill',(-3,-1,2),300,3),('Rim',(0,3,3),450,2)]:
    d=bpy.data.lights.new('Dosa_'+name,'AREA');d.energy=energy;d.shape='DISK';d.size=size
    o=bpy.data.objects.new(d.name,d);s.collection.objects.link(o);o.location=pos
    o.rotation_euler=(Vector((0,0,1))-o.location).to_track_quat('-Z','Y').to_euler()
s.view_settings.view_transform='AgX'
s.render.filepath=OUT+'/source-body.png';bpy.ops.render.render(write_still=True)
bpy.ops.wm.save_as_mainfile(filepath=OUT+'/Dosa_Workshop.blend')
print('source-body.png rendered')
