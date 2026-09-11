import bpy,json,hashlib
from pathlib import Path
from mathutils import Matrix
ROOT=Path('C:/Users/yj666/Oheangbu');OUT=ROOT/'Art/SpellVFX120/Blender'
B2U=Matrix(((1,0,0),(0,0,1),(0,-1,0)))

def export_spirits():
    assert 'SpellVFX120' in bpy.data.filepath
    manifest=[]
    for family,name in [('Beast','Beast_FolkTiger_B2'),('StoneGuardian','StoneGuardian_Jangseung_B1')]:
        ob=bpy.data.objects[name];ob.location=(0,0,0);ob.rotation_euler=(0,0,0);ob.scale=(1,1,1);ob.hide_set(False)
        for other in bpy.context.scene.objects:other.select_set(False)
        ob.select_set(True);bpy.context.view_layer.objects.active=ob
        with bpy.context.temp_override(selected_objects=[ob],selected_editable_objects=[ob],active_object=ob,object=ob):
            bpy.ops.export_scene.fbx(filepath=str(OUT/(name+'.fbx')),use_selection=True,object_types={'MESH'},use_mesh_modifiers=True,mesh_smooth_type='FACE',use_tspace=True,colors_type='LINEAR',axis_forward='-Z',axis_up='Y',apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',bake_space_transform=False,bake_anim=False,add_leaf_bones=False,path_mode='AUTO')
        me=ob.data;me.calc_loop_triangles();col=me.color_attributes['Color'];uv=me.uv_layers.active
        lines=['# Blender VFX sculpt; exported Unity Y-up +Z-forward coordinates.', '# OBJ extended vertex RGB included. FBX is the canonical vertex-color transport.', 'o '+name]
        for v,cd in zip(me.vertices,col.data):
            p=B2U@v.co;c=cd.color;lines.append('v '+' '.join(f'{x:.9g}' for x in [*p,*c[:3]]))
        # One UV and normal per loop lets the OBJ preserve seams correctly.
        for loop in me.loops:
            t=uv.data[loop.index].uv if uv else (0,0);lines.append('vt '+' '.join(f'{x:.9g}' for x in t))
        for loop in me.loops:
            n=B2U@loop.normal;lines.append('vn '+' '.join(f'{x:.9g}' for x in n))
        for tri in me.loop_triangles:lines.append('f '+' '.join(f'{vi+1}/{li+1}/{li+1}' for vi,li in zip(tri.vertices,tri.loops)))
        objpath=OUT/(name+'.obj');objpath.write_text('\n'.join(lines)+'\n',encoding='utf-8')
        points=[list(v.co) for v in me.vertices]
        colors=[list(c.color) for c in col.data]
        source=OUT/(name+'_source_samples.json');source.write_text(json.dumps({'vertices_blender':points,'colors':colors}),encoding='utf-8')
        manifest.append({'family':family,'mesh_object':name,'fbx':str(OUT/(name+'.fbx')),'obj':str(objpath),'source_samples':str(source),'vertices':len(me.vertices),'triangles':len(me.loop_triangles),'material_slots':len(me.materials),'vertex_color_attribute':col.name,'vertex_color_domain':col.domain,'distinct_rgb_rounded_3':len({tuple(round(c.color[i],3) for i in range(3)) for c in col.data}),'blender_to_unity':'(x,y,z)->(x,z,-y)','local_pivot':'bounds center, max dimension1','fbx_sha256':hashlib.sha256((OUT/(name+'.fbx')).read_bytes()).hexdigest(),'rig':'none; effect mesh only','textures_required':False})
    (OUT/'spirit_export_manifest.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8')
    bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'SpellVFX120_MeshLab.blend'))
    print(json.dumps(manifest))
