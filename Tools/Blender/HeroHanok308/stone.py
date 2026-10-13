"""#308 HeroHanok: 해치(해태) — seated guardian beast on a pedestal, as a carved-stone mass.
  blender -b --factory-startup -P Tools/Blender/HeroHanok308/stone.py -- Hero_Haetae_W Hero_Haetae_E
Made from overlapping ellipsoids / tori (haunches, chest, forelegs, head with round eyes, horn bump, curled mane knobs,
collar + bell, tail curl) fused by a voxel remesh, lightly smoothed, then decimated.  Not a hand sculpt.
Model space: x right, front = -y, z up.  _W = the west statue (head turned toward +x = the gate axis), _E = its mirror.
"""
import bpy, sys, math
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
import hanok as H
PK = H.PK


def sculpt(sx, target):
    objs = []
    def ell(c, r, rot=(0, 0, 0), seg=20):
        bpy.ops.mesh.primitive_uv_sphere_add(segments=seg, ring_count=max(6, seg // 2), radius=1.0, location=(c[0] * sx, c[1], c[2]), rotation=(rot[0], rot[1] * sx, rot[2] * sx))
        o = bpy.context.active_object; o.scale = r; objs.append(o)
    def tor(c, R, r, rot):
        bpy.ops.mesh.primitive_torus_add(location=(c[0] * sx, c[1], c[2]), rotation=(rot[0], rot[1] * sx, rot[2] * sx), major_radius=R, minor_radius=r, major_segments=20, minor_segments=8)
        objs.append(bpy.context.active_object)
    def limb(p0, p1, r0, r1, n=14):
        for i in range(n + 1):
            t = i / n; ell((p0[0] + (p1[0] - p0[0]) * t, p0[1] + (p1[1] - p0[1]) * t, p0[2] + (p1[2] - p0[2]) * t), (r0 + (r1 - r0) * t,) * 3, seg=12)
    # body, seated on its haunches
    ell((0, 0.50, 0.46), (0.52, 0.56, 0.46))
    for s in (-1, 1):
        ell((s * 0.43, 0.28, 0.36), (0.24, 0.44, 0.36)); ell((s * 0.46, -0.10, 0.10), (0.17, 0.30, 0.11))
        for k in range(3): ell((s * (0.36 + 0.09 * k), -0.38, 0.09), (0.06, 0.08, 0.07), seg=10)                     # hind toes
    ell((0, 0.02, 0.88), (0.45, 0.48, 0.62), rot=(math.radians(-22), 0, 0))
    ell((0, -0.34, 0.98), (0.39, 0.30, 0.44))
    for s in (-1, 1):
        limb((s * 0.30, -0.42, 0.95), (s * 0.31, -0.60, 0.16), 0.17, 0.12)
        ell((s * 0.31, -0.70, 0.09), (0.17, 0.23, 0.10))
        for k in range(3): ell((s * (0.21 + 0.10 * k), -0.90, 0.08), (0.06, 0.08, 0.07), seg=10)                     # fore toes
    ell((0, -0.28, 1.36), (0.35, 0.34, 0.30))
    # head, turned toward +x by 16 deg about the neck
    psi = math.radians(16); px, py = 0.0, -0.28
    def hp(c):
        x, y = c[0] - px, c[1] - py
        return (px + x * math.cos(psi) - y * math.sin(psi), py + x * math.sin(psi) + y * math.cos(psi), c[2])
    def hell(c, r, seg=20): ell(hp(c), r, rot=(0, 0, psi), seg=seg)
    hell((0, -0.42, 1.63), (0.37, 0.38, 0.32))
    hell((0, -0.72, 1.54), (0.27, 0.22, 0.17)); hell((0, -0.90, 1.60), (0.10, 0.08, 0.07), 12)      # muzzle, nose
    hell((0, -0.70, 1.39), (0.23, 0.19, 0.08))                                                      # lower jaw
    for s in (-1, 1):
        hell((s * 0.17, -0.71, 1.73), (0.088, 0.088, 0.088), 14)                                    # round eyes
        hell((s * 0.17, -0.66, 1.80), (0.12, 0.09, 0.05), 12)                                       # brow
        hell((s * 0.31, -0.30, 1.80), (0.07, 0.11, 0.13), 12)                                       # ears
        hell((s * 0.13, -0.90, 1.47), (0.035, 0.035, 0.07), 8)                                      # fangs
    hell((0, -0.46, 1.96), (0.085, 0.11, 0.10), 12)                                                 # horn bump
    for row, (rad, z, n) in enumerate(((0.40, 1.55, 9), (0.42, 1.34, 10), (0.36, 1.78, 7))):        # mane: rows of curls
        for i in range(n):
            a = math.radians(-125 + 250 * i / (n - 1) + (12 if row % 2 else 0))
            c = (rad * math.sin(a), -0.34 - rad * math.cos(a) * -1.0 * 1.0 + 0.0, z)
            c = (rad * math.sin(a), -0.34 + rad * math.cos(a) * 1.0, z)       # behind and beside the head (cos > 0 = back)
            if math.cos(a) < -0.45: continue
            hp_c = hp(c)
            tor(hp_c, 0.075, 0.034, (math.pi / 2, 0, a + psi))
            ell(hp_c, (0.05, 0.05, 0.05), seg=8)
    tor((0, -0.30, 1.19), 0.37, 0.045, (math.radians(-18), 0, 0)); ell((0, -0.67, 1.06), (0.11, 0.11, 0.12), seg=12)   # collar + bell
    tor((0, 1.00, 0.62), 0.17, 0.065, (0, math.pi / 2, 0)); ell((0, 0.98, 0.86), (0.10, 0.10, 0.10), seg=10)           # tail curl
    for s in (-1, 1):       # flank spirals (shallow relief)
        tor((s * 0.50, 0.30, 0.50), 0.13, 0.03, (0, math.pi / 2, 0)); tor((s * 0.42, -0.30, 1.05), 0.10, 0.028, (0, math.pi / 2, 0))
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join(); o = bpy.context.active_object
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    m = o.modifiers.new('r', 'REMESH'); m.mode = 'VOXEL'; m.voxel_size = 0.02; m.use_smooth_shade = True
    bpy.ops.object.modifier_apply(modifier='r')
    m = o.modifiers.new('s', 'SMOOTH'); m.factor = 0.5; m.iterations = 3
    bpy.ops.object.modifier_apply(modifier='s')
    n = sum(len(p.vertices) - 2 for p in o.data.polygons)
    m = o.modifiers.new('d', 'DECIMATE'); m.ratio = min(1.0, target / max(1, n))
    bpy.ops.object.modifier_apply(modifier='d')
    me = o.data
    V_ = [tuple(v.co) for v in me.vertices]; F = [tuple(p.vertices) for p in me.polygons]
    bpy.data.objects.remove(o, do_unlink=True)
    return V_, F


def haetae(sx):
    def fn(hb):
        d = hb.d
        hb.box(-0.95, -1.55, 0, 0.95, 1.55, 0.30, 'ashlar', top='stone'); hb.box(-0.84, -1.44, 0.30, 0.84, 1.44, 1.22, 'stone'); hb.box(-0.93, -1.53, 1.22, 0.93, 1.53, 1.40, 'stone', bottom=True)
        V_, F = sculpt(sx, (7600, 2400, 500)[d])
        k = 1.12; zmin = min(v[2] for v in V_)
        for f in F: hb.face([(V_[i][0] * k, V_[i][1] * k + 0.05, (V_[i][2] - zmin) * k + 1.38) for i in f], 'stone')
    return fn


MODELS = {}
def reg(name, fn, ref, rep=''): MODELS[name] = (fn, ref, rep)


REF = '광화문 해치상 꼴: 엉덩이를 대고 앉아 앞다리를 세운 자세, 둥근 눈 · 뿔 혹 · 갈기 타래 · 목 방울, 고개를 문 축 쪽으로 16°; 석상 높이 2.1 m · 받침 1.9 × 3.1 × 1.4 m (크기 전부 EST — 실측값 못 찾음)'
reg('Hero_Haetae_W', haetae(1.0), REF + ' — 서쪽 상 (고개 +X)', 'H308_Haetae @ Haetae_W on Gen_Pedestal_Haetae (pivot = ground centre; yaw 180 as placed = facing south)')
reg('Hero_Haetae_E', haetae(-1.0), REF + ' — 동쪽 상 (거울상)', 'H308_Haetae @ Haetae_E')


if __name__ == '__main__':
    names = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    for n in names or list(MODELS):
        fn, ref, rep = MODELS[n]
        H.export(n, fn, ref, rep)
