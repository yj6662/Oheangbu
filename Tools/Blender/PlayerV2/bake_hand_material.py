"""Author and bake a dedicated skin material for the reconstructed V2 hand UVs.

This is Blender material baking, not reuse of the malformed generated hand atlas.
Geometry, skin weights, bone rest transforms and calibrated contact remain unchanged.
"""
import bpy, json, math, hashlib
from pathlib import Path
from mathutils import Vector, Matrix
ROOT=Path('C:/Users/yj666/Oheangbu'); OUT=ROOT/'Art/PlayerV2'
TEX=OUT/'Staging/Character/HandTextures'; TEX.mkdir(parents=True,exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(OUT/'DosaV2_Surfaces.blend'))
s=bpy.context.scene; hands=bpy.data.objects['DosaV2_Hands']; rig=bpy.data.objects['DosaV2_Rig']
def geometry_hash():
    return hashlib.sha256(json.dumps([[list(v.co),[(g.group,g.weight) for g in v.groups]] for v in hands.data.vertices]).encode()).hexdigest()
original=geometry_hash()
bpy.ops.object.select_all(action='DESELECT'); hands.hide_set(False);hands.select_set(True);bpy.context.view_layer.objects.active=hands
for layer in list(hands.data.uv_layers):hands.data.uv_layers.remove(layer)
hands.data.uv_layers.new(name='DosaV2_HandUV')
bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.uv.smart_project(angle_limit=math.radians(55),island_margin=.015,area_weight=.1)
bpy.ops.object.mode_set(mode='OBJECT')
material=bpy.data.materials.new('DosaV2_HandsSkin');material.use_nodes=True
hands.data.materials.clear();hands.data.materials.append(material)
nodes=material.node_tree.nodes;links=material.node_tree.links;bs=nodes.get('Principled BSDF')
bs.inputs['Roughness'].default_value=.68;bs.inputs['IOR'].default_value=1.42
bs.inputs['Subsurface Weight'].default_value=.025
coord=nodes.new('ShaderNodeTexCoord')
broad=nodes.new('ShaderNodeTexNoise');broad.inputs['Scale'].default_value=38;broad.inputs['Detail'].default_value=3
links.new(coord.outputs['Object'],broad.inputs['Vector'])
ramp=nodes.new('ShaderNodeValToRGB')
ramp.color_ramp.elements[0].position=.12;ramp.color_ramp.elements[0].color=(.115,.069,.043,1)
ramp.color_ramp.elements[1].position=.89;ramp.color_ramp.elements[1].color=(.25,.159,.105,1)
links.new(broad.outputs['Fac'],ramp.inputs['Fac']);links.new(ramp.outputs['Color'],bs.inputs['Base Color'])
pores=nodes.new('ShaderNodeTexNoise');pores.inputs['Scale'].default_value=1250;pores.inputs['Detail'].default_value=2
links.new(coord.outputs['Object'],pores.inputs['Vector'])
bump=nodes.new('ShaderNodeBump');bump.inputs['Strength'].default_value=.22;bump.inputs['Distance'].default_value=.000075
links.new(pores.outputs['Fac'],bump.inputs['Height']);links.new(bump.outputs['Normal'],bs.inputs['Normal'])
s.render.engine='CYCLES';s.cycles.samples=16;s.render.bake.margin=12;s.render.bake.use_clear=True
results={}
for suffix,bake_type in [('BaseColor','DIFFUSE'),('Normal','NORMAL'),('Roughness','ROUGHNESS')]:
    image=bpy.data.images.new('T_DosaV2_Hands_'+suffix,width=2048,height=2048,alpha=False)
    image.colorspace_settings.name='sRGB' if suffix=='BaseColor' else 'Non-Color'
    target=nodes.new('ShaderNodeTexImage');target.image=image;nodes.active=target
    for n in nodes:n.select=n==target
    if bake_type=='DIFFUSE':s.render.bake.use_pass_direct=False;s.render.bake.use_pass_indirect=False;s.render.bake.use_pass_color=True
    bpy.ops.object.bake(type=bake_type)
    image.filepath_raw=str(TEX/(image.name+'.png'));image.file_format='PNG';image.save()
    results[suffix]=image.filepath_raw
    nodes.remove(target)
# Use the exact baked maps in the saved authoring file and exported Unity material.
for socket in ['Base Color','Normal']:
    for link in list(bs.inputs[socket].links):links.remove(link)
for suffix,socket in [('BaseColor','Base Color'),('Roughness','Roughness')]:
    n=nodes.new('ShaderNodeTexImage');n.image=bpy.data.images.load(results[suffix],check_existing=True);links.new(n.outputs['Color'],bs.inputs[socket])
n=nodes.new('ShaderNodeTexImage');n.image=bpy.data.images.load(results['Normal'],check_existing=True)
normal=nodes.new('ShaderNodeNormalMap');links.new(n.outputs['Color'],normal.inputs['Color']);links.new(normal.outputs['Normal'],bs.inputs['Normal'])
assert original==geometry_hash(), 'Material work changed calibrated geometry or weights'
report={'method':'Blender procedural skin material baked to newly unwrapped V2 hand UVs',
        'why':'Original generated fingers were fused; projecting their atlas to reconstructed fingers crossed unrelated UV islands.',
        'palette':'Authored muted warm skin for the weathered low-saturation character; no V1 or Hyper3D atlas reused.',
        'geometry_and_weights_unchanged':True,'textures':results,'geometry_hash':original,'status':'MATERIAL_REVIEW_REQUIRED'}
(OUT/'Inspect/hand-material-bake.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
bpy.ops.file.pack_all();bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'DosaV2_Materials.blend'))
# A diagnostic still with the already measured right-hand pose; no Action data is made.
contact=json.loads((OUT/'Calibration/hand-brush-contact-report.json').read_text())['sides']['Right']
for finger,angles in contact['finger_euler_xyz_degrees'].items():
    for i,angle in enumerate(angles):
        pb=rig.pose.bones['RightHand'+finger+str(i+1)];pb.rotation_mode='XYZ';pb.rotation_euler=[math.radians(v) for v in angle]
with bpy.data.libraries.load(str(OUT/'DosaBrushV2.blend'),link=False) as (src,dst):
    dst.objects=[name for name in src.objects if name in ['DosaBrushV2_Rig','DosaBrushV2_Handle','DosaBrushV2_Bristles','GripSocket','TipSocket']]
for obj in dst.objects:
    if obj is not None and obj.name not in s.objects:s.collection.objects.link(obj)
brush=next(o for o in dst.objects if o and o.type=='ARMATURE')
grip=bpy.data.objects.get('GripSocket');bpy.context.view_layer.update()
grip_local=brush.matrix_world.inverted()@grip.matrix_world
brush.matrix_world=Matrix(contact['grip_matrix_rig_local'])@grip_local.inverted()
for obj in s.objects:
    if obj.type=='MESH':obj.hide_render=obj.name not in ['DosaV2_Hands','DosaBrushV2_Handle','DosaBrushV2_Bristles']
center=Matrix(contact['grip_matrix_rig_local']).translation
cam=s.camera;cam.location=center+Vector((.18,-.25,-.22));cam.rotation_euler=(center-cam.location).to_track_quat('-Z','Y').to_euler()
cam.data.type='ORTHO';cam.data.ortho_scale=.24
s.cycles.samples=24;s.render.resolution_x=1100;s.render.resolution_y=1000
s.render.filepath=str(OUT/'Inspect/Hand_Material_Right.png');bpy.ops.render.render(write_still=True)
print(json.dumps(report))
