"""#308 s4: 붓 소켓 조사 / GripSocketL 옮기기.  blender -b --factory-startup -P s4_sock.py -- [--setL z]  (z = GripSocket 에서 붓끝 쪽 +m, 주면 저장)"""
import bpy, sys, os
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
import common_m1 as C, common_m2 as C2
A = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
g = C.init(C.BLEND); rig = g['brush_rig']
def show():
    for k in ('GripSocket', 'TipSocket', 'GripSocketL', 'ShaftEndSocket'):
        o = bpy.data.objects[k]; loc = rig.matrix_world.inverted() @ o.matrix_world.translation
        print('S4 sock', k, 'parent', o.parent.name if o.parent else None, o.parent_type, o.parent_bone, 'local', [round(x, 4) for x in o.location], 'in_rig', [round(x, 4) for x in loc])
show()
if '--setL' in A:
    z = float(A[A.index('--setL') + 1]); o = bpy.data.objects['GripSocketL']; gs = bpy.data.objects['GripSocket']; bpy.context.view_layer.update()
    want = rig.matrix_world @ ((rig.matrix_world.inverted() @ gs.matrix_world.translation) + C.Z * z)
    o.matrix_world = o.matrix_world.copy() @ o.matrix_world.inverted() @ __import__('mathutils').Matrix.Translation(want) @ o.matrix_world.to_3x3().to_4x4()
    bpy.context.view_layer.update(); show(); bpy.ops.wm.save_as_mainfile(filepath=C.BLEND); print('S4 saved')
