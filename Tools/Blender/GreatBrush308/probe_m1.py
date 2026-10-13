"""#308 GreatBrush motion1: 모캡 FBX · 플레이어 리그 조사(읽기만)."""
import bpy, os, glob, json
from mathutils import Vector
R = 'C:/Users/yj666/Oheangbu'
MOC = R + '/Art/Characters308/GreatBrush308/mocap'
FBX = R + '/Oheangbu/Assets/_Project/Art/Characters/PlaytestReRig/Models/Player_C02_ReRig.fbx'
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=FBX, automatic_bone_orientation=False)
arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
print('M1 player', arm.name, 'scale', tuple(arm.scale), 'rot', tuple(arm.rotation_euler), 'bones', len(arm.data.bones))
AW = arm.matrix_world
print('M1 player_bones', json.dumps({b.name: [round(x, 3) for x in (AW @ b.head_local)] for b in arm.data.bones if 'Hand' not in b.name or b.name.endswith('Hand')}))
print('M1 player_all', [b.name for b in arm.data.bones])
print('M1 objs', [(o.name, o.type) for o in bpy.data.objects])
for i, f in enumerate(sorted(glob.glob(MOC + '/*.fbx'))):
    before = set(bpy.data.objects); bpy.ops.import_scene.fbx(filepath=f, automatic_bone_orientation=False)
    new = [o for o in bpy.data.objects if o not in before]; src = next(o for o in new if o.type == 'ARMATURE'); a = src.animation_data.action
    SW = src.matrix_world
    info = {'file': os.path.basename(f), 'objs': [(o.name, o.type) for o in new], 'scale': [round(x, 4) for x in src.scale], 'range': list(a.frame_range), 'fps': bpy.context.scene.render.fps, 'nb': len(src.data.bones)}
    print('M1 src', json.dumps(info))
    if i == 0:
        print('M1 src_bones', json.dumps({b.name: [round(x, 3) for x in (SW @ b.head_local)] for b in src.data.bones if 'Hand' not in b.name or b.name.endswith('Hand')}))
        print('M1 src_all', [b.name for b in src.data.bones])
    sc = bpy.context.scene; pts = {}
    for fr in (int(a.frame_range[0]), int((a.frame_range[0] + a.frame_range[1]) / 2), int(a.frame_range[1])):
        sc.frame_set(fr); pb = src.pose.bones
        g = lambda n: SW @ pb['mixamorig:' + n].head
        pts[fr] = {'hips': [round(x, 3) for x in g('Hips')], 'LF': [round(x, 3) for x in g('LeftFoot')], 'RF': [round(x, 3) for x in g('RightFoot')], 'handdist': round((g('LeftHand') - g('RightHand')).length, 3)}
    print('M1 src_pts', json.dumps(pts))
    for o in new: bpy.data.objects.remove(o, do_unlink=True)
print('M1 DONE')
