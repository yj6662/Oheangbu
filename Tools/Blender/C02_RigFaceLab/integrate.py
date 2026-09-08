"""Explicit integration of retained body/motion and face candidates. No canonical writes."""
from pathlib import Path
import bpy,json,hashlib
ROOT=Path('C:/Users/yj666/Oheangbu');LAB=ROOT/'Art/PlayerPhase1/C02_RigFaceLab';OUT=LAB/'Final'

def integrate(motion_stage='B1'):
    OUT.mkdir(exist_ok=True)
    tag=motion_stage+'_C3'
    destination=OUT/('Integrated_'+tag+'.blend')
    if (OUT/('Integrated_'+tag+'.fbx')).exists():raise RuntimeError('Completed integrated candidate exists; refusing overwrite')
    source=LAB/'Motion'/motion_stage/('Motion_'+motion_stage+'.blend')
    bpy.ops.wm.open_mainfile(filepath=str(source))
    ns={};script=ROOT/'Tools/Blender/C02_RigFaceLab/face_merge.py'
    exec(compile(script.read_text(encoding='utf8'),str(script),'exec'),ns)
    merged=ns['merge_face_c3']('Armature')
    rig=bpy.data.objects['Armature'];meshes=[bpy.data.objects[n] for n in ['C02_Mesh_0','FaceLid_Left','FaceLid_Right']]
    # Facial channels are controlled explicitly at runtime, independently from Animator body tracks.
    for o in meshes[1:]:
        o.data.shape_keys.animation_data_clear()
        for key in list(o.data.shape_keys.key_blocks)[1:]:key.value=0
        for modifier in o.modifiers:
            if modifier.type=='ARMATURE' and modifier.object!=rig:raise RuntimeError('Wrong target skin')
    rig['LabFacePresets']=json.dumps({'GentleSquint':{'BlinkLeft':.35,'BlinkRight':.35},'FocusedSquint':{'BlinkLeft':.65,'BlinkRight':.65},'EyesClosed':{'BlinkLeft':1.,'BlinkRight':1.}})
    rig.data.pose_position='POSE';rig.animation_data.action=bpy.data.actions['LAB_'+motion_stage+'_Idle']
    rig.animation_data.action_slot=rig.animation_data.action.slots[0]
    bpy.context.scene.frame_set(1);bpy.context.scene.render.fps=30;bpy.context.view_layer.update()
    stats=[]
    for o in meshes:
        o.data.calc_loop_triangles()
        stats.append({'name':o.name,'vertices':len(o.data.vertices),'triangles':len(o.data.loop_triangles),
          'max_influences':max(sum(g.weight>1e-8 for g in v.groups) for v in o.data.vertices),
          'shapes':list(o.data.shape_keys.key_blocks.keys())[1:] if o.data.shape_keys else [],
          'material_slots':len(o.data.materials),'modifiers':[m.type for m in o.modifiers]})
    if sum(s['triangles'] for s in stats)>60000:raise RuntimeError('Triangle budget exceeded')
    texture_dir=OUT/'Textures';texture_dir.mkdir(exist_ok=True)
    import shutil
    original_texture=LAB/'Face/Textures/texture_0.png'
    target_texture=texture_dir/'C02_original_texture.png'
    shutil.copy2(original_texture,target_texture)
    original_image=bpy.data.images.load(str(target_texture),check_existing=True)
    original_image.colorspace_settings.name='sRGB';_=original_image.pixels[0];original_image.pack()
    used_images=set()
    for o in meshes:
        for mat in o.data.materials:
            if mat and mat.use_nodes:
                for n in mat.node_tree.nodes:
                    if n.type=='TEX_IMAGE' and n.image:n.image=original_image
    textures=[{'image':original_image.name,'file':str(target_texture.relative_to(LAB)),'sha256':hashlib.sha256(target_texture.read_bytes()).hexdigest(),'original_png_bytes_unchanged':True}]
    bpy.ops.wm.save_as_mainfile(filepath=str(destination))
    bpy.ops.object.select_all(action='DESELECT');rig.select_set(True)
    for o in meshes:o.select_set(True)
    bpy.context.view_layer.objects.active=rig;fbx=OUT/('Integrated_'+tag+'.fbx')
    with bpy.context.temp_override(selected_objects=[rig]+meshes,selected_editable_objects=[rig]+meshes,active_object=rig,object=rig):
        bpy.ops.export_scene.fbx(filepath=str(fbx),use_selection=True,object_types={'ARMATURE','MESH'},add_leaf_bones=False,
          axis_forward='-Z',axis_up='Y',use_mesh_modifiers=True,mesh_smooth_type='FACE',bake_anim=True,
          bake_anim_use_nla_strips=False,bake_anim_use_all_actions=True,bake_anim_step=1.0,bake_anim_simplify_factor=0.0,
          path_mode='COPY',embed_textures=True,use_armature_deform_only=False)
    manifest={'status':'EXPORTED_AWAITING_ROUNDTRIP_AND_UNITY','body_source':str(source.relative_to(LAB)),
      'body_source_sha256':hashlib.sha256(source.read_bytes()).hexdigest(),'face_source':merged,'blend':'Final/'+destination.name,
      'fbx':'Final/'+fbx.name,'fbx_sha256':hashlib.sha256(fbx.read_bytes()).hexdigest(),'meshes':stats,
      'total_triangles':sum(s['triangles'] for s in stats),'bones':len(rig.data.bones),'new_cloth_bones':0,'new_face_bones':0,
      'textures':textures,'fps':30,'clips':{a.name:list(a.frame_range) for a in bpy.data.actions if a.name.startswith(('C02_','LAB_','DIAG_'))},
      'blendshape_route':'BlinkLeft/Right 0..1 Blender -> 0..100 Unity, independent runtime controller; remove exporter constant-zero shape curves from Unity experimental body clip copies',
      'skin_import_requirement':'Custom maxBonesPerVertex=5, renderer Auto, runtime Unlimited (temporary lab scope)',
      'integrated_quality':'UNVERIFIED','canonical_replaced':False,'additional_paid_credits':0}
    (OUT/('integration_manifest_'+tag+'.json')).write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf8')
    return {'blend':str(destination),'fbx':str(fbx),'triangles':manifest['total_triangles'],'meshes':stats}
