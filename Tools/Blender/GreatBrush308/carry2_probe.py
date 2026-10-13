"""#308 carry2: 쉬는 자세 · 옛 CarryIdle 에서 어깨 둘레 치수를 잰다(읽기만).  blender -b --factory-startup -P carry2_probe.py"""
import bpy, os, sys
import numpy as np
from mathutils import Vector
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
import common_m1 as C, common_m2 as C2
g = C.init(C.BLEND); arm = g['arm']; AW = g['AW']; RH = g['REST_HEAD']; sc = bpy.context.scene
r3 = lambda v: [round(x, 3) for x in v]
for n in ('Hips', 'Spine02', 'Spine01', 'Spine', 'neck', 'Head', 'RightShoulder', 'RightArm', 'RightForeArm', 'RightHand', 'LeftArm'):
    print('C2 rest', n, r3(RH[n]), 'tail', r3(g['REST_TAIL'][n]))
print('C2 bones', g['ORDER'])
assert C.assign_clip('CarryIdle'); sc.frame_set(0); bpy.context.view_layer.update()
for n in ('neck', 'Head', 'RightShoulder', 'RightArm', 'RightForeArm', 'RightHand', 'Spine'):
    pb = arm.pose.bones[n]; print('C2 carry', n, r3(AW @ pb.head), 'tail', r3(AW @ pb.tail))
P = C.body_points(); dom = g['dom']
print('C2 body z max', round(float(P[:, 2].max()), 3), 'groups', sorted(set(dom.tolist())))
# 오른 어깨 위 표면: x 띠마다 y 띠마다 가장 높은 z
for x0 in (-.10, -.13, -.16, -.19, -.22, -.25):
    row = []
    for y0 in (-.15, -.10, -.05, 0, .05, .10, .15):
        m = (abs(P[:, 0] - x0) < .015) & (abs(P[:, 1] - y0) < .025) & (P[:, 2] < 1.62) & ~g['is_hand']
        mm = m & np.array([('Head' not in d and 'ead' not in d) for d in dom])
        row.append(round(float(P[mm, 2].max()), 3) if mm.any() else None)
    print('C2 top x=%.2f' % x0, row)
hd = np.array([('ead' in d) for d in dom]); nk = np.array([d == 'neck' for d in dom])
for nm, m in (('head', hd), ('neck', nk)):
    Q = P[m]; print('C2', nm, 'verts', len(Q), 'min', r3(Q.min(axis=0)), 'max', r3(Q.max(axis=0)))
for o in ('TipSocket', 'GripSocket', 'ShaftEndSocket'): print('C2 sock', o, r3(bpy.data.objects[o].matrix_world.translation))
print('C2 DONE')
