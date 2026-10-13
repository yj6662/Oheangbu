"""#308 GreatBrush motion2: 원본 클립의 몸 방향(엉덩이 · 가슴 요, 도) · 엉덩이 XY 를 프레임마다 찍는다(창 · 시간표를 정하는 데 쓴다)."""
import bpy, math, sys
from mathutils import Vector
R = 'C:/Users/yj666/Oheangbu/Art/Characters308/GreatBrush308/mocap/'
for clip in sys.argv[sys.argv.index('--') + 1:]:
    bpy.ops.wm.read_factory_settings(use_empty=True); bpy.ops.import_scene.fbx(filepath=R + clip + '.fbx', automatic_bone_orientation=False)
    src = next(o for o in bpy.data.objects if o.type == 'ARMATURE'); a = src.animation_data.action; SW = src.matrix_world; sc = bpy.context.scene
    def yaw(n):
        b = src.pose.bones['mixamorig:' + n]; dq = (SW @ b.matrix).to_quaternion() @ (SW @ b.bone.matrix_local).to_quaternion().inverted(); f = dq @ Vector((0, -1, 0)); return math.degrees(math.atan2(-f.x, -f.y))
    out = []
    for f in range(int(a.frame_range[0]), int(a.frame_range[1]) + 1, 2):
        sc.frame_set(f); h = SW @ src.pose.bones['mixamorig:Hips'].head; out.append('%d:%.0f/%.0f(%.2f,%.2f)' % (f, yaw('Hips'), yaw('Spine2'), h.x, h.y))
    print('M1 yaw', clip, ' '.join(out))
