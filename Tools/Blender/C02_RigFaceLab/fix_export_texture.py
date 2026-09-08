"""Repair lazy-image path ordering from the first interrupted export; no model candidate edit."""
from pathlib import Path
import bpy,json,hashlib,shutil
ROOT=Path('C:/Users/yj666/Oheangbu');LAB=ROOT/'Art/PlayerPhase1/C02_RigFaceLab';OUT=LAB/'Final'

def repair():
    bpy.ops.wm.open_mainfile(filepath=str(OUT/'Integrated_B1_C3.blend'))
    source=LAB/'Face/Textures/texture_0.png';destination=OUT/'Textures/C02_original_texture.png'
    shutil.copy2(source,destination)
    image=bpy.data.images.load(str(destination),check_existing=True)
    image.colorspace_settings.name='sRGB';_ = image.pixels[0];image.pack()
    rig=bpy.data.objects['Armature'];meshes=[bpy.data.objects[n] for n in ['C02_Mesh_0','FaceLid_Left','FaceLid_Right']]
    for o in meshes:
        for material in o.data.materials:
            for node in material.node_tree.nodes:
                if node.type=='TEX_IMAGE' and node.image:node.image=image
    for im in bpy.data.images:
        if im!=image and im.users==0 and im.type=='IMAGE':bpy.data.images.remove(im)
    bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'Integrated_B1_C3.blend'))
    fbx=OUT/'Integrated_B1_C3.fbx'
    with bpy.context.temp_override(selected_objects=[rig]+meshes,selected_editable_objects=[rig]+meshes,active_object=rig,object=rig):
        bpy.ops.export_scene.fbx(filepath=str(fbx),use_selection=True,object_types={'ARMATURE','MESH'},add_leaf_bones=False,
          axis_forward='-Z',axis_up='Y',use_mesh_modifiers=True,mesh_smooth_type='FACE',bake_anim=True,
          bake_anim_use_nla_strips=False,bake_anim_use_all_actions=True,bake_anim_step=1.0,bake_anim_simplify_factor=0.0,
          path_mode='COPY',embed_textures=True,use_armature_deform_only=False)
    p=OUT/'integration_manifest.json';d=json.loads(p.read_text(encoding='utf8'))
    d['prior_texture_export_fbx_sha256']=d['fbx_sha256'];d['fbx_sha256']=hashlib.sha256(fbx.read_bytes()).hexdigest()
    d['textures']=[{'file':'Final/Textures/C02_original_texture.png','sha256':hashlib.sha256(destination.read_bytes()).hexdigest(),'used_by':['Material_1','Material_1.001'],'byte_identical_to_original':True}]
    d['texture_export_repair']={'reason':'A lazily loaded C3 image loaded a stale transformed PNG after filepath reassignment. Source C3 and B1 pixels were identical. Both materials now share exact original packed PNG bytes.','evidence':'Face/TextureProbe/result.json','geometry_uv_weights_keys_animations_changed':False}
    d['blendshape_route']='Blender 0..1 -> Unity 0..100. Blender exporter includes constant-zero facial curves in all takes; Unity experimental body clip copies remove these two curves so explicit facial control has ownership. Original imported takes remain intact.'
    p.write_text(json.dumps(d,ensure_ascii=False,indent=2),encoding='utf8')
    return {'fbx_sha256':d['fbx_sha256'],'original_texture':str(destination),'texture_sha256':d['textures'][0]['sha256']}
