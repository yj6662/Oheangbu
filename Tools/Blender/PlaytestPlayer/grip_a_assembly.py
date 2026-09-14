"""Editable A grip + real brush assembly, original textures; inexpensive geometry evidence only."""
import bpy,json,math,hashlib,ctypes
from pathlib import Path
from mathutils import Matrix,Vector,Quaternion
R=Path('C:/Users/yj666/Oheangbu');O=R/'Art/PlaytestPolish/Hands'
bpy.ops.wm.open_mainfile(filepath=str(O/'Work/Player_C02_GripA.blend'));r=bpy.data.objects['Armature'];fit=json.loads((O/'Validation/contact_fit.json').read_text());par=fit['parameters']
for p in r.pose.bones:p.matrix_basis.identity()
for n,m in fit['bone_matrices'].items():r.pose.bones[n].matrix_basis=Matrix(m)
before=set(bpy.data.objects);bpy.ops.import_scene.fbx(filepath=str(R/'Oheangbu/Assets/_Project/Art/Characters/DosaV2/Models/SM_DosaBrushV2.fbx'));tx=Matrix.Translation((par[16],-.025,par[17]))@Matrix.Rotation(math.pi/2,4,'X')
for o in [o for o in bpy.data.objects if o not in before and not o.parent]:
 world=tx@o.matrix_world;o.parent=r;o.parent_type='BONE';o.parent_bone='RightHand';o.matrix_world=world
bpy.context.view_layer.update()
textures=[]
for image in bpy.data.images:
 if image.type!='IMAGE':continue
 if image.packed_file:
  name=Path(image.filepath.replace('\\','/')).name or (image.name+'.png');out=O/'Textures'/name
  out.write_bytes(image.packed_file.data);textures.append({'name':image.name,'path':str(out.relative_to(O)),'sha256':hashlib.sha256(out.read_bytes()).hexdigest()})
 elif image.filepath:
  source=Path(bpy.path.abspath(image.filepath))
  if source.is_file():
   out=O/'Textures'/source.name;out.write_bytes(source.read_bytes());textures.append({'name':image.name,'path':str(out.relative_to(O)),'sha256':hashlib.sha256(out.read_bytes()).hexdigest()})
(O/'Validation/textures.json').write_text(json.dumps({'status':'COPIED_EXISTING_TEXTURES','textures':textures},indent=2))
for side,angle in [('Right',-68),('Left',68)]:
 name=side+'Arm';p=r.pose.bones[name];p.rotation_mode='QUATERNION';axis=r.data.bones[name].matrix_local.to_3x3().inverted()@Vector((0,1,0));p.rotation_quaternion=Quaternion(axis,math.radians(angle))
bpy.context.view_layer.update()
text=bpy.data.texts.new('README_SELECTED_A')
text.write('Selected A hand derivative. 54-bone skeleton and bind pose preserved.\nThe right hand wraps the actual 0.9m brush; 33 skin vertices locally corrected <=0.541mm.\nNeutral export + POSE_Grip markers are in Exports/Player_C02_GripA.fbx.\nThis posed assembly is editable; no new gait or cloth simulation is baked.\nSee Validation/hand_surface_sampling.json for the NEW measured 0.5mm sample grid.\nUnity runtime validation and visual comparison must be recorded separately.\n')
bpy.ops.file.pack_all();bpy.ops.wm.save_as_mainfile(filepath=str(O/'Work/Player_C02_GripA_Assembly.blend'));print('A_ASSEMBLY_AND_TEXTURES_SAVED',len(textures),flush=True)
