import bpy
from pathlib import Path
OUT=Path('C:/Users/yj666/Oheangbu/Art/PlayerPhase1/FitRigV3/Textures')
def prepare_material_bake():
    OUT.mkdir(exist_ok=True)
    target=bpy.data.objects['Durumagi']
    source=bpy.data.objects.new('Preserved_Meshy_Robe_BakeSource',bpy.data.meshes['Preserved_Meshy_Robe_LocalFit'])
    bpy.context.scene.collection.objects.link(source)
    source.hide_render=True;source.hide_set(True)
    target.data.uv_layers.new(name='FitRigV3_BakedUV')
    target.data.uv_layers.active_index=len(target.data.uv_layers)-1
    target.data.uv_layers.active.active_render=True
    for o in bpy.context.selected_objects:o.select_set(False)
    target.select_set(True);bpy.context.view_layer.objects.active=target
    bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=1.15192,island_margin=.012)
    bpy.ops.object.mode_set(mode='OBJECT')
    mat=bpy.data.materials.new('M_Durumagi_FitRigV3');mat.use_nodes=True
    target.data.materials.clear();target.data.materials.append(mat)
    s=bpy.context.scene;s.render.engine='CYCLES';s.cycles.samples=4
    s.render.bake.use_selected_to_active=True;s.render.bake.cage_extrusion=.045;s.render.bake.max_ray_distance=.12;s.render.bake.margin=12
    print('Prepared bake source/UV/material')
def bake_map(kind):
    target=bpy.data.objects['Durumagi'];source=bpy.data.objects['Preserved_Meshy_Robe_BakeSource'];mat=target.active_material
    source.hide_render=False;source.hide_set(False)
    old={n:bpy.data.objects[n].hide_render for n in ['Body','InnerTop']}
    for n in old:bpy.data.objects[n].hide_render=True
    for o in bpy.context.selected_objects:o.select_set(False)
    source.select_set(True);target.select_set(True);bpy.context.view_layer.objects.active=target
    im=bpy.data.images.new('T_Durumagi_FitRigV3_'+kind,width=2048,height=2048,alpha=False)
    if kind!='BaseColor':im.colorspace_settings.name='Non-Color'
    node=mat.node_tree.nodes.new('ShaderNodeTexImage');node.image=im
    for n in mat.node_tree.nodes:n.select=False
    node.select=True;mat.node_tree.nodes.active=node
    s=bpy.context.scene
    if kind=='BaseColor':
        s.render.bake.use_pass_direct=False;s.render.bake.use_pass_indirect=False;s.render.bake.use_pass_color=True
        bpy.ops.object.bake(type='DIFFUSE')
        mat.node_tree.links.new(node.outputs['Color'],next(n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED').inputs['Base Color'])
    elif kind=='Normal':
        bpy.ops.object.bake(type='NORMAL')
        normal=mat.node_tree.nodes.new('ShaderNodeNormalMap');mat.node_tree.links.new(node.outputs['Color'],normal.inputs['Color']);mat.node_tree.links.new(normal.outputs['Normal'],next(n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED').inputs['Normal'])
    elif kind=='Roughness':
        bpy.ops.object.bake(type='ROUGHNESS');mat.node_tree.links.new(node.outputs['Color'],next(n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED').inputs['Roughness'])
    im.filepath_raw=str(OUT/(im.name+'.png'));im.file_format='PNG';im.save();im.pack()
    source.hide_render=True;source.hide_set(True)
    for n,v in old.items():bpy.data.objects[n].hide_render=v
    print('Baked',kind,im.filepath_raw)

