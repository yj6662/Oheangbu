"""Separate source-albedo markings from geometry normals; diagnostic material copies only."""
from pathlib import Path
exec(compile(Path(__file__).with_name('compare_brush_bristle_materials.py').read_text().split('for condition,lights,world')[0],'material_setup','exec'))
background.inputs['Color'].default_value=(.12,.12,.12,1);background.inputs['Strength'].default_value=.4
light=bpy.data.lights.new('SurfaceDiagnosticKey','AREA');light.energy=180;light.size=1.3;ob=bpy.data.objects.new(light.name,light);scene.collection.objects.link(ob);ob.location=(-1,-1.5,2);ob.rotation_euler=(center-ob.location).to_track_quat('-Z','Y').to_euler();cam.location=center+Vector((0,-1,0));cam.rotation_euler=(center-cam.location).to_track_quat('-Z','Y').to_euler()
for mode in ['gray_diffuse_no_normal','source_albedo_unlit']:
 material=bpy.data.materials.new('Diagnostic_'+mode);material.use_nodes=True;nodes=material.node_tree.nodes;nodes.clear();out=nodes.new('ShaderNodeOutputMaterial')
 if mode=='gray_diffuse_no_normal':
  shader=nodes.new('ShaderNodeBsdfDiffuse');shader.inputs['Color'].default_value=(.32,.32,.32,1);shader.inputs['Roughness'].default_value=1
 else:
  shader=nodes.new('ShaderNodeEmission');image=nodes.new('ShaderNodeTexImage');image.image=next(n.image for n in original.node_tree.nodes if n.type=='TEX_IMAGE' and n.image.colorspace_settings.name=='sRGB');material.node_tree.links.new(image.outputs['Color'],shader.inputs['Color'])
 material.node_tree.links.new(shader.outputs[0],out.inputs['Surface']);hair.data.materials[0]=material;path=OUT/(mode+'.png');scene.render.filepath=str(path);bpy.ops.render.render(write_still=True)
print('Diagnostic only: no asset saved')
