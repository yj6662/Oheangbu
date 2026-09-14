import bpy,bmesh,os,json,shutil
from mathutils import Matrix,Vector
ROOT='C:/Users/yj666/Oheangbu';OUT=globals().get('DOSA_EXPORT_ROOT',ROOT+'/Oheangbu/Assets/_Project/Art/Characters/Dosa')
for d in ['Models','Animations','Textures']:os.makedirs(OUT+'/'+d,exist_ok=True)
s=bpy.data.scenes['Dosa_Player_Workshop'];bpy.context.window.scene=s
a=bpy.data.objects['Dosa_Rig'];m=bpy.data.objects['Dosa_Body']
if not a.get('spine_names_normalized'):
    for src,tmp in [('Spine','_UpperSpineTmp'),('Spine02','_LowerSpineTmp')]:
        a.data.bones[src].name=tmp
        if m.vertex_groups.get(src):m.vertex_groups[src].name=tmp
    for src,dst in [('_UpperSpineTmp','Spine02'),('_LowerSpineTmp','Spine')]:
        a.data.bones[src].name=dst
        if m.vertex_groups.get(src):m.vertex_groups[src].name=dst
    a['spine_names_normalized']=True
a.animation_data_clear()
for pb in a.pose.bones:pb.matrix_basis=Matrix.Identity(4)
bpy.context.view_layer.update()
def export(path,rig,mesh):
    bpy.ops.object.select_all(action='DESELECT');rig.hide_set(False);mesh.hide_set(False);rig.select_set(True);mesh.select_set(True);bpy.context.view_layer.objects.active=rig
    bpy.ops.export_scene.fbx(filepath=path,use_selection=True,object_types={'ARMATURE','MESH'},add_leaf_bones=False,use_armature_deform_only=False,bake_anim=False,axis_forward='-Z',axis_up='Y',apply_unit_scale=True,apply_scale_options='FBX_SCALE_ALL',use_mesh_modifiers=True,path_mode='STRIP')
if not globals().get('ARMS_ONLY_EXPORT',False):export(OUT+'/Models/SM_DosaCourier_Refined.fbx',a,m)
for name in ['Dosa_Arms','Dosa_Rig_Arms']:
    if bpy.data.objects.get(name):bpy.data.objects.remove(bpy.data.objects[name],do_unlink=True)
ar=a.copy();ar.data=a.data.copy();ar.name='Dosa_Rig_Arms';s.collection.objects.link(ar)
am=m.copy();am.data=m.data.copy();am.name='Dosa_Arms';s.collection.objects.link(am)
for mod in am.modifiers:
    if mod.type=='ARMATURE':mod.object=ar
armgroups={g.index for g in am.vertex_groups if any(w in g.name for w in ['ForeArm','Hand'])}
handverts={i for p in am.data.polygons if p.material_index==1 for i in p.vertices}
def near_limb(point,side):
    upper=a.data.bones[side+'Arm'].head_local;elbow=a.data.bones[side+'ForeArm'].head_local;wrist=a.data.bones[side+'Hand'].head_local
    for start,end,radius,min_t in [(elbow,wrist,.068,.02)]:
        line=end-start;t=(point-start).dot(line)/line.length_squared
        if min_t<=t<=1.15 and (point-(start+line*max(0,min(1,t)))).length<radius:return True
    return False
# Auto-rig shoulder groups include chest straps/cape: a spatial limb mask is required
# for near-camera arms, otherwise that torso geometry crosses the drawing view.
remove=[v.index for v in am.data.vertices if v.index not in handverts and (sum(g.weight for g in v.groups if g.group in armgroups)<.60 or not any(near_limb(v.co,side) for side in ['Left','Right']))]
bm=bmesh.new();bm.from_mesh(am.data);bm.verts.ensure_lookup_table();bmesh.ops.delete(bm,geom=[bm.verts[i] for i in remove],context='VERTS');bm.to_mesh(am.data);bm.free()
export(OUT+'/Models/SM_DosaCourier_Arms.fbx',ar,am)
ar.hide_render=True;am.hide_render=True;ar.hide_set(True);am.hide_set(True)
shutil.copy2('C:/Users/yj666/MandateOfInk/MandateOfInk/Assets/_Project/Art/Models/Player/T_DosaCourier_base_color.png',OUT+'/Textures/T_DosaCourier_BaseColor.png')
shutil.copy2(ROOT+'/Art/Player/T_DosaCourier_HandsBaseColor.png',OUT+'/Textures/T_DosaCourier_HandsBaseColor.png')
m.data.calc_loop_triangles();am.data.calc_loop_triangles()
report={'body_triangles':len(m.data.loop_triangles),'arms_triangles':len(am.data.loop_triangles),'bones':len(a.data.bones),'materials':[x.name for x in m.data.materials],
        'spine_chain':['Hips','Spine','Spine01','Spine02','neck','Head'],'height_meters':1.75,'sole_y':0,
        'ankle_height_meters':{side:float(a.data.bones[side+'Foot'].head_local.z) for side in ['Left','Right']},
        'finger_curl_axis':[-1,0,0],'brush_axis':'socket local +Y toward tip; local +Z dorsal',
        'models':[OUT+'/Models/SM_DosaCourier_Refined.fbx',OUT+'/Models/SM_DosaCourier_Arms.fbx']}
open(ROOT+'/Art/Player/export-models-report.json','w',encoding='utf-8').write(json.dumps(report,indent=2))
if globals().get('DOSA_SAVE_REFINED',True):bpy.ops.wm.save_as_mainfile(filepath=ROOT+'/Art/Player/Dosa_Refined.blend')
print(json.dumps(report))
