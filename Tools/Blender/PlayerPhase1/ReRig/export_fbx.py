import bpy,json
from pathlib import Path
OUT=Path('C:/Users/yj666/Oheangbu/Art/PlayerPhase1/ReRig');s=bpy.context.scene;a=bpy.data.objects['Dosa_Phase1_Rig']
a.animation_data.action=None
for p in a.pose.bones:p.rotation_quaternion=(1,0,0,0);p.location=(0,0,0);p.scale=(1,1,1)
for o in bpy.context.selected_objects:o.select_set(False)
for name in ['Body','InnerTop','Durumagi','Dosa_Phase1_Rig']:bpy.data.objects[name].select_set(True)
bpy.context.view_layer.objects.active=a
# Delete unreferenced legacy/source actions in this new file only.
allowed={'DIAG_Walk','DIAG_Run','DIAG_ArmsUp','DIAG_Squat','DIAG_BrushSwing','DIAG_HandFlex'}
for act in list(bpy.data.actions):
 if act.name not in allowed:bpy.data.actions.remove(act)
for act in bpy.data.actions:
 for layer in act.layers:
  for strip in layer.strips:
   for bag in strip.channelbags:
    for fc in list(bag.fcurves):
     try:a.path_resolve(fc.data_path)
     except Exception:bag.fcurves.remove(fc)
bpy.ops.export_scene.fbx(filepath=str(OUT/'Dosa_Phase1_Rerig.fbx'),use_selection=True,object_types={'MESH','ARMATURE'},add_leaf_bones=False,use_armature_deform_only=True,bake_anim=True,bake_anim_use_all_actions=True,bake_anim_use_nla_strips=False,bake_anim_simplify_factor=0.0,path_mode='COPY',embed_textures=True,axis_forward='-Z',axis_up='Y')
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'Dosa_Phase1_Rerig.blend'),compress=True)
before=set(bpy.data.objects);bpy.ops.import_scene.fbx(filepath=str(OUT/'Dosa_Phase1_Rerig.fbx'))
new=set(bpy.data.objects)-before;rows=[]
for o in new:
 if o.type=='MESH':
  o.data.calc_loop_triangles();rows.append({'name':o.name,'vertices':len(o.data.vertices),'faces':len(o.data.polygons),'triangles':len(o.data.loop_triangles),'materials':len(o.data.materials),'unweighted':sum(not v.groups for v in o.data.vertices),'max_weights':max(len(v.groups) for v in o.data.vertices)})
result={'meshes':rows,'total_triangles':sum(r['triangles'] for r in rows),'armatures':sum(o.type=='ARMATURE' for o in new),'status':'PASS' if len(rows)==3 and sum(r['triangles'] for r in rows)<=60000 else 'FAIL'}
(OUT/'fbx_roundtrip.json').write_text(json.dumps(result,indent=2))
for o in new:bpy.data.objects.remove(o,do_unlink=True)
print(json.dumps(result))
