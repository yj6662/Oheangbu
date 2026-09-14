import bpy,json,math
from pathlib import Path
from mathutils import Matrix,Vector,Quaternion
R=Path('C:/Users/yj666/Oheangbu');O=R/'Art/PlayerPhase1/PlaytestReRig'
bpy.ops.wm.open_mainfile(filepath=str(O/'Work/Player_C02_ReRig.blend'));r=bpy.data.objects['Armature'];fit=json.loads((O/'Validation/contact_fit.json').read_text());par=fit['parameters']
for p in r.pose.bones:p.matrix_basis.identity()
for n,m in fit['bone_matrices'].items():r.pose.bones[n].matrix_basis=Matrix(m)
before=set(bpy.data.objects);bpy.ops.import_scene.fbx(filepath=str(R/'Oheangbu/Assets/_Project/Art/Characters/DosaV2/Models/SM_DosaBrushV2.fbx'));tx=Matrix.Translation((par[16],-.025,par[17]))@Matrix.Rotation(math.pi/2,4,'X')
for o in [o for o in bpy.data.objects if o not in before and not o.parent]:
 world=tx@o.matrix_world;o.parent=r;o.parent_type='BONE';o.parent_bone='RightHand';o.matrix_world=world
for side,angle in [('Right',-68),('Left',68)]:
 name=side+'Arm';p=r.pose.bones[name];p.rotation_mode='QUATERNION';axis=r.data.bones[name].matrix_local.to_3x3().inverted()@Vector((0,1,0));p.rotation_quaternion=Quaternion(axis,math.radians(angle))
bpy.context.view_layer.update()
text=bpy.data.texts.new('README_C02_HAND_RIG')
text.write('C02 player hand rig + actual 0.9m brush.\nThe original approved file is untouched.\n54 deform bones, 30 finger segments, 59,246 player triangles.\nBrush uses its own 6-segment bristle armature and follows RightHand bone parenting.\nPOSE_Grip/Relax empties store local quaternion calibration, GripReference_R stores the real brush socket frame.\nThis file is an editable posed assembly; Exports/Player_C02_ReRig.fbx contains only the neutral player and markers.\nRuntime drawing and harvest poses are in the Unity presentation rig.\nNo new motion action has been baked into this file.\n')
bpy.ops.file.pack_all();bpy.ops.wm.save_as_mainfile(filepath=str(O/'Work/Player_C02_Assembly.blend'));print('EDITABLE_ASSEMBLY_SAVED')
