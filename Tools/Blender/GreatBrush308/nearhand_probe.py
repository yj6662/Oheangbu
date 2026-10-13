"""#308 1인칭 손 전용 모델 0단계: 재료 읽기(읽기만).
    blender -b --factory-startup -P nearhand_probe.py
플레이어 FBX 의 메시 · 재질 · 오른팔 정점 수, 붓 자루의 실제 반지름(쥐는 자리 둘레)을 적는다."""
import bpy, os, sys
import numpy as np
from mathutils import Vector
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
import common_m1 as C
g = C.init(C.GB + '/_none_.blend'); arm = g['arm']; AW = g['AW']
print('NH arm', arm.name, 'scale', tuple(round(x, 4) for x in arm.scale), 'bones', len(arm.data.bones))
for o in bpy.data.objects:
    if o.type != 'MESH': continue
    gi = {v.index: v.name for v in o.vertex_groups}; n = 0; hand = 0
    for v in o.data.vertices:
        best = max(v.groups, key=lambda x: x.weight, default=None)
        if best is None: continue
        d = gi[best.group]
        if d.startswith('Right') and ('Arm' in d or 'Hand' in d): n += 1
        if d.startswith('RightHand'): hand += 1
    print('NH mesh', o.name, 'verts', len(o.data.vertices), 'right arm', n, 'right hand', hand, 'mats', [m.name for m in o.data.materials], 'parent', o.parent.name if o.parent else None,
          'mods', [m.type for m in o.modifiers], 'uv', len(o.data.uv_layers))
RH = g['REST_HEAD']
for n in ('RightShoulder', 'RightArm', 'RightForeArm', 'RightHand', 'RightHandMiddle1', 'RightHandIndex1', 'RightHandPinky1', 'RightHandThumb1', 'RightHandThumb2', 'RightHandThumb3'):
    print('NH bone', n, tuple(round(x, 4) for x in RH[n]), 'parent', g['PARENT'][n])
print('NH children of RightHand', [b.name for b in arm.data.bones['RightHand'].children], 'forearm children', [b.name for b in arm.data.bones['RightForeArm'].children])
# 붓 자루 반지름: 붓 리그 +Z 축 둘레, GripSocket 앞뒤 0.5 m 구간의 정점 반지름
rig = g['brush_rig']; br = g['brush']; M = br.matrix_world; R = rig.matrix_world; d = (R.to_3x3() @ Vector((0, 0, 1))).normalized(); G0 = R.translation
rows = {}
for v in br.data.vertices:
    p = M @ v.co - G0; z = p.dot(d); r = (p - d * z).length; k = round(z / .05) * .05
    if -.75 <= k <= .35: rows.setdefault(k, []).append(r)
for k in sorted(rows): print('NH shaft z=%+.2f  r max %.4f  median %.4f  n %d' % (k, max(rows[k]), float(np.median(rows[k])), len(rows[k])))
print('NH sockets', [(o.name, tuple(round(x, 3) for x in o.matrix_world.translation)) for o in bpy.data.objects if 'Socket' in o.name])
print('NH rig bones', [b.name for b in rig.data.bones][:14])
