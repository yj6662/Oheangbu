"""Export static diagnostic FBX only. Does not approve rig or install canonical player."""
import bpy,json,shutil,sys,hashlib
from pathlib import Path
from mathutils import Matrix
sys.path.insert(0,str(Path(__file__).resolve().parent))
from cloth_export_channels import preserve_cloth_mobility
from brush_asset_selection import apply_selected_brush
from authored_material_manifest import append_missing_authored_materials
ROOT=Path('C:/Users/yj666/Oheangbu');OUT=ROOT/'Art/PlayerV2';STAGE=OUT/'Staging/Character'
STAGE.mkdir(parents=True,exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(OUT/'DosaV2_Assembled.blend'))
s=bpy.context.scene;rig=bpy.data.objects['DosaV2_Rig'];source=bpy.data.objects['DosaV2_SourceSurface']
meshes=[o for o in s.objects if o.type=='MESH' and o!=source and o.name.startswith(('DosaV2_','DosaPackV2_'))]
cloth_transport=preserve_cloth_mobility(meshes)
rig.animation_data_clear()
for p in rig.pose.bones:p.matrix_basis=Matrix.Identity(4)
bpy.context.view_layer.update()
def export(name,arm,objects):
    bpy.ops.object.select_all(action='DESELECT')
    arm.hide_set(False);arm.select_set(True);bpy.context.view_layer.objects.active=arm
    for o in objects:o.hide_set(False);o.select_set(True)
    bpy.ops.export_scene.fbx(filepath=str(STAGE/name),use_selection=True,object_types={'ARMATURE','MESH'},
        add_leaf_bones=False,use_armature_deform_only=False,bake_anim=False,axis_forward='-Z',axis_up='Y',
        apply_unit_scale=True,apply_scale_options='FBX_SCALE_ALL',use_mesh_modifiers=True,path_mode='STRIP',use_custom_props=True,
        colors_type='LINEAR')
export('SM_DosaV2_Rigged.fbx',rig,meshes)
near_names=[o.name for o in meshes if o.name=='DosaV2_Hands' or o.name.startswith(('DosaV2_Sleeve','DosaV2_ArmLining','DosaV2_ShoulderLining'))]
near_override=OUT/'Inspect/ClothBlender/NearLiningBudget10/DosaV2_NearArmLining10.blend'
near_override_sha='a89f198c0f2f9925cbc078a26a14fbb75fe0770c76c9ce7d0cf0a8baec3c40ef'
assert hashlib.sha256(near_override.read_bytes()).hexdigest()==near_override_sha
override_names=['DosaV2_ArmLining_Left','DosaV2_ArmLining_Right']
with bpy.data.libraries.load(str(near_override),link=False) as (src,dst):dst.objects=list(override_names)
original_data={}
try:
    for name,replacement in zip(override_names,dst.objects):
        target=bpy.data.objects[name]
        assert replacement is not None
        assert [(g.index,g.name) for g in target.vertex_groups]==[(g.index,g.name) for g in replacement.vertex_groups]
        original_data[name]=target.data
        target.data=replacement.data.copy()
        target.data.materials.clear()
        for material in original_data[name].materials:target.data.materials.append(material)
    bpy.context.view_layer.update()
    near_triangles=0
    for obj in meshes:
        if obj.name in near_names:obj.data.calc_loop_triangles();near_triangles+=len(obj.data.loop_triangles)
    assert near_triangles<=26000, ('Near budget',near_triangles)
    export('SM_DosaV2_Arms.fbx',rig,[o for o in meshes if o.name in near_names])
finally:
    for name,data in original_data.items():bpy.data.objects[name].data=data
    bpy.context.view_layer.update()
manifest_path=OUT/'Staging/texture-manifest.json'
manifest=json.loads(manifest_path.read_text(encoding='utf-8-sig'))
manifest['materials']=[m for m in manifest['materials'] if m['name'] not in ('DosaV2_HandsSkin','DosaV2_Backpack','DosaV2_TrouserUndercloth','DosaV2_WristWrapping')]
manifest['materials'].append({'name':'DosaV2_WristWrapping',
    'baseColor':'Character/WrappingTextures/T_DosaV2_Wrapping_BaseColor.png',
    'normal':'Character/WrappingTextures/T_DosaV2_Wrapping_Normal.png',
    'metallicFallback':0,'roughnessFallback':.93,'doubleSided':False})
manifest['materials'].append({'name':'DosaV2_HandsSkin','baseColor':'Character/HandTextures/T_DosaV2_Hands_BaseColor.png',
    'normal':'Character/HandTextures/T_DosaV2_Hands_Normal.png','roughness':'Character/HandTextures/T_DosaV2_Hands_Roughness.png',
    'roughnessFallback':.68,'doubleSided':False,'baseTint':{'r':1,'g':1,'b':1,'a':1}})
manifest['materials'].append({'name':'DosaV2_Backpack',
    'baseColor':'Backpack/Textures/T_DosaPackV2_BaseColor.png',
    'normal':'Backpack/Textures/T_DosaPackV2_Normal.png',
    'metallic':'Backpack/Textures/T_DosaPackV2_Metallic.png',
    'roughness':'Backpack/Textures/T_DosaPackV2_Roughness.png','doubleSided':True})
(STAGE/'LowerTextures').mkdir(exist_ok=True)
for channel in ('BaseColor','Normal'):
    filename='T_DosaV2_Trousers_'+channel+'.png'
    shutil.copy2(OUT/'LowerRepairTextures'/filename,STAGE/'LowerTextures'/filename)
manifest['materials'].append({'name':'DosaV2_TrouserUndercloth',
    'baseColor':'Character/LowerTextures/T_DosaV2_Trousers_BaseColor.png',
    'normal':'Character/LowerTextures/T_DosaV2_Trousers_Normal.png',
    'metallicFallback':0,'roughnessFallback':.94,'doubleSided':False})
manifest['rendererMaterials']=[{'renderer':o.name,'materials':['DosaV2_Source' if m.name=='Material_0' else m.name for i,m in enumerate(o.data.materials) if i in {p.material_index for p in o.data.polygons}]} for o in meshes]
manifest['rendererMaterials'] += [{'renderer':name,'materials':['DosaBrushV2']} for name in ['DosaBrushV2_Handle','DosaBrushV2_Bristles']]
# Renderer enumeration above is rebuilt on each character export. Reapply the
# explicitly selected brush and paired UV maps so it cannot fall back to Meshy hair.
brush_selection=apply_selected_brush(manifest,OUT/'Staging')
authored_materials=append_missing_authored_materials(manifest,meshes,OUT/'Staging')
manifest_path.write_text(json.dumps(manifest,indent=2),encoding='utf-8')
textures=[]
for image in ['base_color','normal','metallic','roughness']:
    path=OUT/'MeshySources/character-01'/('T_0_'+image+'.png')
    if path.exists():
        target=STAGE/('T_DosaV2_'+image+'.png');shutil.copy2(path,target);textures.append(target.name)
report={'status':'STATIC_CANDIDATE_NOT_RIG_PASS','world':'Character/SM_DosaV2_Rigged.fbx',
        'sourceSha256':hashlib.sha256((OUT/'DosaV2_Assembled.blend').read_bytes()).hexdigest(),
        'near_lining_override':{'source':str(near_override),'sha256':near_override_sha,'meshes':override_names,'nearTriangles':near_triangles},
        'near':'Character/SM_DosaV2_Arms.fbx','world_renderers':[o.name for o in meshes],
        'near_renderers':near_names,'textures':textures,'production_animation_count':0,'backpack_included':True,
        'source':'DosaV2_Assembled.blend','exported_control_objects':False,'cloth_float_transport':cloth_transport,
        'selected_brush_fbx':brush_selection['activeFbx'],'selected_brush_sha256':brush_selection['activeFbxSha256'],
        'new_authored_materials':authored_materials}
(STAGE/'export-candidate.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print(json.dumps(report))
