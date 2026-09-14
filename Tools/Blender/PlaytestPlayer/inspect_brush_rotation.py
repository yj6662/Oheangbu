import bpy,json
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath='C:/Users/yj666/Oheangbu/Oheangbu/Assets/_Project/Art/Characters/DosaV2/Models/SM_DosaBrushV2.fbx')
for n in ['GripSocket','TipSocket']:print(n,list(bpy.data.objects[n].matrix_world.to_quaternion()),list(bpy.data.objects[n].matrix_world.translation))