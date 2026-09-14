import bpy,json,hashlib,shutil
from pathlib import Path
R=Path('C:/Users/yj666/Oheangbu');O=R/'Art/PlaytestRecovery/Hands'
bpy.ops.wm.open_mainfile(filepath=str(O/'Work/Player_C02_GripA_Rebuilt.blend'))
rig=bpy.data.objects['Armature'];names=set(b.name for b in rig.data.bones);changed={};maxRemoved=0
for obj in bpy.data.objects:
 if obj.type!='MESH' or not any(m.type=='ARMATURE' and m.object==rig for m in obj.modifiers):continue
 count=0
 for v in obj.data.vertices:
  ws=sorted([(g.weight,obj.vertex_groups[g.group]) for g in v.groups if obj.vertex_groups[g.group].name in names and g.weight>1e-8],key=lambda x:-x[0])
  if len(ws)<=4:continue
  maxRemoved=max(maxRemoved,sum(w for w,g in ws[4:]));total=sum(w for w,g in ws[:4])
  for w,g in ws:g.remove([v.index])
  for w,g in ws[:4]:g.add([v.index],w/total,'REPLACE')
  count+=1
 changed[obj.name]=count
for b in rig.pose.bones:b.matrix_basis.identity()
bpy.context.view_layer.update();bpy.ops.wm.save_as_mainfile(filepath=str(O/'Work/Player_C02_GripA_Rebuilt.blend'))
bpy.ops.object.select_all(action='DESELECT')
for obj in bpy.data.objects:
 if obj.type in ['ARMATURE','MESH','EMPTY'] and obj.name not in ['Ground','Floor']:obj.select_set(True)
bpy.context.view_layer.objects.active=rig
fbx=O/'Exports/Player_C02_GripA_Rebuilt.fbx'
bpy.ops.export_scene.fbx(filepath=str(fbx),use_selection=True,object_types={'ARMATURE','MESH','EMPTY'},add_leaf_bones=False,bake_anim=False,axis_forward='-Z',axis_up='Y',path_mode='COPY',embed_textures=True)
shutil.copy2(fbx,O/'Exports/Player_C02_GripA.fbx')
(O/'Validation/export_weight_cleanup.json').write_text(json.dumps({'status':'NORMALIZED_TO_FOUR','changedByMesh':changed,'maxDiscardedWeight':maxRemoved,'fbxSha256':hashlib.sha256(fbx.read_bytes()).hexdigest()},indent=2))
