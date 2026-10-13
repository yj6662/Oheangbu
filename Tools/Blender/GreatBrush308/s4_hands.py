"""#308 s4: 손 모양 확인(읽기만). 클립 몇 프레임에서 손바닥 법선(손바닥 → 자루 방향, 월드)과 엄지 끝이 자루 축 위/아래 어디에 있는지 잰다.
    blender -b --factory-startup -P s4_hands.py -- StanceIdle:0 SwingSweep:5,12 ..."""
import bpy, sys, os
from mathutils import Vector
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
import common_m1 as C, common_m2 as C2
g = C.init(C.BLEND); arm = g['arm']; AW = g['AW']; sc = bpy.context.scene; RQ = g['REST_ROT']
GRP = {'Right': C.Grip('Right'), 'Left': C.Grip('Left')}
for a in sys.argv[sys.argv.index('--') + 1:]:
    clip, fs = a.split(':'); assert C.assign_clip(clip)
    for f in [int(x) for x in fs.split(',')]:
        sc.frame_set(f); bpy.context.view_layer.update(); rig = g['brush_rig']; d = (rig.matrix_world.to_3x3() @ Vector((0, 0, 1))).normalized(); G = rig.matrix_world.translation; out = []
        for s in ('Right', 'Left'):
            pb = arm.pose.bones[s + 'Hand']; Q = (AW @ pb.matrix).to_3x3().normalized() @ (AW.to_3x3().normalized() @ RQ[s + 'Hand']).inverted(); N = Q @ GRP[s].N
            tb = arm.pose.bones[s + 'HandThumb3']; tip = AW @ tb.tail; rel = tip - G; rad = rel - d * rel.dot(d); wr = AW @ pb.head; rw = wr - G; rwr = rw - d * rw.dot(d)
            out.append('%s palm->shaft n=(%.2f,%.2f,%.2f) thumbtip off-axis=(%.3f,%.3f,%.3f) wrist off-axis z=%.3f' % (s[0], N.x, N.y, N.z, rad.x, rad.y, rad.z, rwr.z))
        print('HAND', clip, f, 'd=(%.2f,%.2f,%.2f)' % tuple(d), ' | '.join(out))
