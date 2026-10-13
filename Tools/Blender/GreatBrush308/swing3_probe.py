"""#308 swing3: 한 클립 몇 프레임의 관절 · 자루 끝 자리와 자루에 닿는 몸 꼭짓점을 찍어 본다(읽기만).  blender -b -P swing3_probe.py -- <이름> <f,f,...>"""
import bpy, json, os, sys
import numpy as np
from mathutils import Vector
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
import common_m1 as C, common_m2 as C2
A = sys.argv[sys.argv.index('--') + 1:]; g = C.init(C.BLEND); arm = g['arm']; AW = g['AW']; rig = g['brush_rig']; sc = bpy.context.scene; dom = g['dom']; ish = g['is_hand']
assert C.assign_clip(A[0]); r2 = lambda v: [round(float(x), 2) for x in v]
for f in [int(x) for x in A[1].split(',')]:
    sc.frame_set(f); bpy.context.view_layer.update(); G = rig.matrix_world.translation.copy(); d = (rig.matrix_world.to_3x3() @ Vector((0, 0, 1))).normalized(); J = lambda n: AW @ arm.pose.bones[n].head
    P = C.body_points(); tail = G - d * C.TAIL; neck = G + d * (C.TIPL - C.HEADL); ds, ts = C.dist_to_segment(P, tail, neck); pen = (ds < C.R_SHAFT - .004) & ~ish; along = ts[pen] * (neck - tail).length - C.TAIL + C2.SHIFT
    print('PR f', f, 'Rsho', r2(J('RightArm')), 'Relb', r2(J('RightForeArm')), 'Rhand', r2(J('RightHand')), 'Lsho', r2(J('LeftArm')), 'Lelb', r2(J('LeftForeArm')), 'Lhand', r2(J('LeftHand')), 'tail', r2(tail), 'd', r2(d), 'hips', r2(J('Hips')), 'spine', r2(J('Spine')),
          '| in', int(pen.sum()), 'along_from_right_hand_m', [round(float(along.min()), 2), round(float(along.max()), 2)] if pen.any() else None, dict(zip(*[x.tolist() for x in np.unique(dom[pen], return_counts=True)])))
