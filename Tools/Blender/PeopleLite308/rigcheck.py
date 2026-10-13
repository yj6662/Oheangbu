"""변환 규약 확인: Idle 0 의 뼈 로컬이 가져온 정지 자세와 얼마나 다른가(위치는 같아야 한다). blender -b -P rigcheck.py -- <fbx> <idle.anim> <root>"""
import bpy, sys, os, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import rig
fbx, an, root = sys.argv[sys.argv.index('--') + 1:][:3]
bpy.ops.wm.read_factory_settings(use_empty=True)
arm, meshes, _ = rig.import_fbx(fbx); clip = rig.Clip(an, root)
worst_t = 0; rows = []
for b in arm.data.bones:
    L = clip.local(rig.bone_path(b, root), 0.0); R = rig.rest_local(b)
    if L is None: rows.append((b.name, 'NO CURVE')); continue
    dt = (L.translation - R.translation).length; ang = math.degrees((R.to_quaternion().rotation_difference(L.to_quaternion())).angle)
    worst_t = max(worst_t, dt); rows.append((b.name, round(dt, 3), round(ang, 1)))
print('RIGCHECK worst translation diff (cm)', round(worst_t, 4)); print('RIGCHECK', rows[:14]); print('RIGCHECK big angles', [(n, a) for n, *r in rows for a in r[1:] if isinstance(a, float) and a > 20])
print('RIGCHECK arm world', [tuple(round(v, 4) for v in r) for r in arm.matrix_world], 'root0', [tuple(round(v, 3) for v in r) for r in clip.local(root, 0.0)])
