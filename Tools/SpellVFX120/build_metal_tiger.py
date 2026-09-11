"""Preserve the existing Meshy Tiger; author a separate charcoal/ember material."""
import bpy,bmesh,json,hashlib,shutil
from pathlib import Path
from mathutils import Vector
R=Path('C:/Users/yj666/Oheangbu');O=R/'Art/SpellVFX120/MetalTiger';O.mkdir(parents=True,exist_ok=True)
A=R/'Oheangbu/Assets/_Project/Art/SpellVFX120/MetalTiger';T=A/'Textures';T.mkdir(parents=True,exist_ok=True)
source=R/'Art/SpellVFX120/MeshySummons/Blender/MeshySummons_Source.blend'
sha=hashlib.sha256(source.read_bytes()).hexdigest()
bpy.ops.wm.open_mainfile(filepath=str(source))
body=bpy.data.objects['SM_Meshy_088_C19C'];body.hide_set(False);body.hide_render=False
for obj in list(bpy.data.objects):
 if obj!=body:bpy.data.objects.remove(obj,do_unlink=True)
body.name='MetalTiger_Body';body.data=body.data.copy();body.data.name=body.name
bpy.context.view_layer.objects.active=body;body.select_set(True)
original=[v.co.copy() for v in body.data.vertices];original_mat=body.data.materials[0]
bm=bmesh.new();bm.from_mesh(body.data);bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=1e-6);bm.to_mesh(body.data);bm.free()
if body.data.has_custom_normals:body.data.normals_split_custom_set([(0,0,0)]*len(body.data.loops))
for p in body.data.polygons:p.use_smooth=True
mat=bpy.data.materials.new('M_MetalTiger_BrushedSilver');mat.use_nodes=True;body.data.materials[0]=mat
n=mat.node_tree.nodes;l=mat.node_tree.links;n.clear();out=n.new('ShaderNodeOutputMaterial');bs=n.new('ShaderNodeBsdfPrincipled');l.new(bs.outputs[0],out.inputs[0]);bs.inputs['Roughness'].default_value=.43;bs.inputs['Metallic'].default_value=.65
tex=n.new('ShaderNodeTexImage');tex.image=bpy.data.images.load(str(R/'Oheangbu/Assets/_Project/Art/SpellVFX120/MeshySummons/088_C19C/Textures/T_088_C19C_BaseColor.png'))
hue=n.new('ShaderNodeHueSaturation');hue.inputs['Saturation'].default_value=.3;hue.inputs['Value'].default_value=1.15;l.new(tex.outputs['Color'],hue.inputs['Color']);l.new(hue.outputs[0],bs.inputs['Base Color'])
bs.inputs['Metallic'].default_value=0
scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=8;scene.render.threads_mode='FIXED';scene.render.threads=2;scene.render.bake.margin=8
images={}
for kind in ['BaseColor']:
 image=bpy.data.images.new('T_MetalTiger_'+kind,width=2048,height=2048,alpha=False);target=n.new('ShaderNodeTexImage');target.image=image;n.active=target
 if kind=='BaseColor':
  scene.render.bake.use_pass_direct=False;scene.render.bake.use_pass_indirect=False;scene.render.bake.use_pass_color=True;bpy.ops.object.bake(type='DIFFUSE')
 image.filepath_raw=str(T/(image.name+'.png'));image.file_format='PNG';image.save();images[kind]=image
shutil.copy2(R/'Oheangbu/Assets/_Project/Art/SpellVFX120/MeshySummons/088_C19C/Textures/T_088_C19C_Normal.png',T/'T_MetalTiger_Normal.png')
n.clear();out=n.new('ShaderNodeOutputMaterial');bs=n.new('ShaderNodeBsdfPrincipled');l.new(bs.outputs[0],out.inputs[0]);bs.inputs['Roughness'].default_value=.43;bs.inputs['Metallic'].default_value=.65
for kind,image in images.items():
 node=n.new('ShaderNodeTexImage');node.image=image
 if kind=='BaseColor':l.new(node.outputs[0],bs.inputs['Base Color'])
normal=n.new('ShaderNodeTexImage');normal.image=bpy.data.images.load(str(T/'T_MetalTiger_Normal.png'));normal.image.colorspace_settings.name='Non-Color';nm=n.new('ShaderNodeNormalMap');nm.inputs['Strength'].default_value=.65;l.new(normal.outputs[0],nm.inputs['Color']);l.new(nm.outputs[0],bs.inputs['Normal'])
for image in list(images.values())+[normal.image]:image.pack()
body.data.calc_loop_triangles();tris=len(body.data.loop_triangles)
assert tris<=18000
fbx=A/'SM_MetalTiger.fbx';bpy.ops.export_scene.fbx(filepath=str(fbx),use_selection=True,object_types={'MESH'},axis_forward='-Z',axis_up='Y',bake_anim=False,path_mode='COPY')
scene.cycles.samples=16;scene.cycles.use_denoising=True;scene.world.color=(.15,.15,.15)
for loc,power,size in [((3,-4,5),700,4),((-3,-2,3),500,3),((1,3,4),800,3)]:
 bpy.ops.object.light_add(type='AREA',location=loc);light=bpy.context.object;light.data.energy=power;light.data.shape='DISK';light.data.size=size;light.rotation_euler=(Vector((0,0,.8))-light.location).to_track_quat('-Z','Y').to_euler()
bpy.ops.object.camera_add(location=(3,-4,2.2));cam=bpy.context.object;cam.rotation_euler=(Vector((0,0,.85))-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.type='ORTHO';cam.data.ortho_scale=4.8;scene.camera=cam
scene.render.resolution_x=1920;scene.render.resolution_y=1080;scene.render.resolution_percentage=100
bpy.ops.wm.save_as_mainfile(filepath=str(O/'MetalTiger_Working.blend'))
from mathutils.kdtree import KDTree
tree=KDTree(len(body.data.vertices))
for i,v in enumerate(body.data.vertices):tree.insert(v.co,i)
tree.balance();error=max(tree.find(v)[2] for v in original)
report=dict(triangles=tris,vertices=len(body.data.vertices),materials=1,dimensions=list(body.dimensions),sourceHashBefore=sha,sourceHashAfter=hashlib.sha256(source.read_bytes()).hexdigest(),maxBodyPositionErrorM=error,meshyCredits=0,rigged=False)
(O/'model_report.json').write_text(json.dumps(report,indent=2))
scene.render.filepath=str(O/'model_after.png');bpy.ops.render.render(write_still=True)
body.data.materials[0]=original_mat;scene.render.filepath=str(O/'model_before.png');bpy.ops.render.render(write_still=True)
bpy.ops.wm.read_factory_settings(use_empty=True);bpy.ops.import_scene.fbx(filepath=str(fbx));rows=[]
for obj in bpy.context.scene.objects:
 if obj.type=='MESH':obj.data.calc_loop_triangles();rows.append(dict(name=obj.name,triangles=len(obj.data.loop_triangles),dimensions=list(obj.dimensions)))
assert sum(r['triangles'] for r in rows)==tris
(O/'fbx_roundtrip.json').write_text(json.dumps(rows,indent=2));print('METAL_TIGER_EXPORT_PASS',report,flush=True)
