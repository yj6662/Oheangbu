"""#308 PackLite: generated modules for the kinds the owned packs lack (plain, real proportions, plain metre UVs).

  blender -b --factory-startup -P Tools/Blender/PackLite308/pl_modules.py --        (or: python run.py --gen)

Model space while building: x = width (right), y = depth (FRONT at -y), z = up, metres, origin = ground centre.
Exported so that Unity sees X = x, Y = z, Z = y  ->  the front (door side) faces Unity -Z.
Materials (the builder assigns project materials): PLG_Stone, PLG_Wood, PLG_Plaster, PLG_Tile, PLG_Earth, PLG_Paper, PLG_Brick.
UVs: box projection in metres (1 UV unit = 1 m); roofs: u along the eave, v up the slope.
Dimension sources are listed in Tools/Unity/Stage308_hanyang/packlite_compose.md (EST = estimate).
"""
import bpy, bmesh, sys, json, math, time, hashlib
from pathlib import Path
import numpy as np
HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import pl_view as V
import packlite308 as PK
OUT = PK.OUT
MATS = ['PLG_Stone', 'PLG_Wood', 'PLG_Plaster', 'PLG_Tile', 'PLG_Earth', 'PLG_Paper', 'PLG_Brick']
COL = {'PLG_Stone': (0.62, 0.60, 0.56), 'PLG_Wood': (0.42, 0.20, 0.14), 'PLG_Plaster': (0.86, 0.83, 0.76), 'PLG_Tile': (0.20, 0.21, 0.23), 'PLG_Earth': (0.63, 0.52, 0.38),
       'PLG_Paper': (0.90, 0.88, 0.80), 'PLG_Brick': (0.36, 0.36, 0.38)}
S, W, P, T, E, PA, B = range(7)


class MB:
    def __init__(self): self.v = []; self.f = []; self.m = []; self.uv = []

    def face(self, pts, mat, uv=None):
        b = len(self.v); self.v.extend(pts); self.f.append(tuple(range(b, b + len(pts)))); self.m.append(mat); self.uv.append(uv)

    def box(self, x0, y0, z0, x1, y1, z1, mat, bottom=True):
        if x1 < x0: x0, x1 = x1, x0
        if y1 < y0: y0, y1 = y1, y0
        if z1 < z0: z0, z1 = z1, z0
        a, b, c, d = (x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0)
        e, f, g, h = (x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)
        for q in ((a, b, f, e), (b, c, g, f), (c, d, h, g), (d, a, e, h), (e, f, g, h)): self.face(list(q), mat)
        if bottom: self.face([d, c, b, a], mat)

    def cyl(self, x, y, z0, z1, r, mat, n=8, square=False):
        if square: return self.box(x - r, y - r, z0, x + r, y + r, z1, mat)
        ring = [(x + r * math.cos(2 * math.pi * i / n), y + r * math.sin(2 * math.pi * i / n)) for i in range(n)]
        for i in range(n):
            p, q = ring[i], ring[(i + 1) % n]
            self.face([(p[0], p[1], z0), (q[0], q[1], z0), (q[0], q[1], z1), (p[0], p[1], z1)], mat)
        self.face([(p[0], p[1], z1) for p in ring], mat); self.face([(p[0], p[1], z0) for p in reversed(ring)], mat)

    def stairs(self, xc, y_front, z_top, width, steps, run=0.36, mat=S, direction=(0, -1)):
        """steps descending from (edge at y_front, height z_top) toward `direction` (unit, axis aligned)."""
        rise = z_top / steps
        dx, dy = direction
        for i in range(steps):
            d0, d1 = i * run, (i + 1) * run
            zt = z_top - (i + 1) * rise + rise
            zt = z_top - i * rise - rise * 0.0
            h = z_top - (i + 1) * rise + rise
            top = z_top - i * rise
            if dy != 0: self.box(xc - width / 2, y_front + dy * d0, 0, xc + width / 2, y_front + dy * d1, top - rise * 0 if i == 0 else top, mat)
            else: self.box(xc + dx * d0, y_front - width / 2, 0, xc + dx * d1, y_front + width / 2, top, mat)

    def build(self, name):
        me = bpy.data.meshes.new(name)
        me.from_pydata([(-x, -y, z) for x, y, z in self.v], [], self.f)   # model -> Blender work space (front -> +Y == Unity -Z)
        for m in MATS: me.materials.append(bpy.data.materials.get(m) or bpy.data.materials.new(m))
        me.polygons.foreach_set('material_index', np.array(self.m, dtype=np.int32))
        me.update()
        bm = bmesh.new(); bm.from_mesh(me)
        bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        bmesh.ops.triangulate(bm, faces=bm.faces)
        bm.to_mesh(me); bm.free(); me.update()
        # box-projected UVs in metres (roof faces: along-eave / up-slope handled by the same projection on the dominant plane)
        uvl = me.uv_layers.new(name='UVMap')
        nl = len(me.loops)
        lv = np.empty(nl, dtype=np.int32); me.loops.foreach_get('vertex_index', lv)
        co = np.empty(len(me.vertices) * 3, dtype=np.float32); me.vertices.foreach_get('co', co); co = co.reshape(-1, 3)
        pn = np.empty(len(me.polygons) * 3, dtype=np.float32); me.polygons.foreach_get('normal', pn); pn = pn.reshape(-1, 3)
        lt = np.empty(len(me.polygons), dtype=np.int32); me.polygons.foreach_get('loop_total', lt)
        ln = np.repeat(pn, lt, axis=0); c = co[lv]
        ax = np.argmax(np.abs(ln), axis=1)
        u = np.where(ax == 0, c[:, 1], c[:, 0]); v = np.where(ax == 2, c[:, 1], c[:, 2])
        # sloped roof faces: stretch v by 1/cos so tiles keep their size up the slope
        sl = (ax == 2) & (np.abs(ln[:, 2]) < 0.97)
        slope_dir_y = np.abs(ln[:, 1]) >= np.abs(ln[:, 0])
        vv = np.where(slope_dir_y, c[:, 1], c[:, 0]) / np.maximum(np.abs(ln[:, 2]), 0.3)
        uu = np.where(slope_dir_y, c[:, 0], c[:, 1])
        u = np.where(sl, uu, u); v = np.where(sl, vv, v)
        uvl.data.foreach_set('uv', np.stack([u, v], 1).astype(np.float32).reshape(-1))
        o = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(o)
        PK.activate(o); bpy.ops.object.material_slot_remove_unused()
        PK.cleanup(o, 35.0)
        return o


# ------------------------------------------------------------------ roof
def roof(mb, L, D, z0, k, hg, seg=(10, 8), curve=0.18, lift=0.35, thick=0.28, mat=T, ridge=True, ridge_mat=T, cap=None):
    """Roof over the rectangle L x D (eave to eave), eave height z0, slope k (rise/run).
    hg = height of the hip part: 0 -> gable (맞배), big -> hip (우진각), between -> hip-and-gable (팔작).  cap = flat-top clamp (lower tier of a 2-tier roof)."""
    hx, hy = L / 2, D / 2
    zmax = k * hy
    xg = hx - hg / k if hg > 0 else hx
    hip_only = xg <= hy * 0.0 or hg >= zmax - 1e-6

    def zf(x, y, inner):
        dx, dy = hx - abs(x), hy - abs(y)
        if hg <= 0: z = k * dy
        elif hip_only or not inner: z = min(k * min(dx, dy), hg if not hip_only else 1e9)
        else: z = k * dy
        if hip_only: z = k * min(dx, dy)
        if cap is not None: z = min(z, cap)
        zz = z * (1 - curve + curve * min(1.0, z / max(zmax, 1e-6)))        # concave slope (steeper near the ridge)
        e = (1 - min(1.0, z / max(0.35 * zmax, 1e-6))) ** 2                  # near the eave
        zz += lift * e * max(abs(x) / hx, abs(y) / hy) ** 4 * (1.0 if hg != 0 else 0.4)   # corner lift (앙곡)
        return z0 + zz
    nx, ny = seg
    ys = [-hy + D * j / ny for j in range(ny + 1)]
    cols = []   # (x, inner flag)
    if hg > 0 and not hip_only:
        no = max(2, int(round(nx * (hx - xg) / L)) + 1); ni = max(2, nx - 2 * no + 2)
        cols += [(-hx + (hx - xg) * i / no, False) for i in range(no + 1)]
        cols += [(-xg + 2 * xg * i / ni, True) for i in range(ni + 1)]
        cols += [(xg + (hx - xg) * i / no, False) for i in range(no + 1)]
    else:
        cols = [(-hx + L * i / nx, True) for i in range(nx + 1)]
    top = [[(x, y, zf(x, y, inn)) for y in ys] for x, inn in cols]
    bot = [[(x, y, z - thick) for x, y, z in col] for col in top]
    nc = len(cols)
    for i in range(nc - 1):
        if abs(cols[i][0] - cols[i + 1][0]) < 1e-9:      # the 합각 wall (vertical gable triangle)
            for j in range(ny):
                a, b, c, d = top[i][j], top[i][j + 1], top[i + 1][j + 1], top[i + 1][j]
                if abs(a[2] - d[2]) + abs(b[2] - c[2]) > 1e-6: mb.face([a, b, c, d], P)
            continue
        for j in range(ny):
            mb.face([top[i][j], top[i + 1][j], top[i + 1][j + 1], top[i][j + 1]], mat)
            mb.face([bot[i][j + 1], bot[i + 1][j + 1], bot[i + 1][j], bot[i][j]], W)
    for i in range(nc - 1):
        if abs(cols[i][0] - cols[i + 1][0]) < 1e-9: continue
        mb.face([bot[i][0], bot[i + 1][0], top[i + 1][0], top[i][0]], W); mb.face([top[i][ny], top[i + 1][ny], bot[i + 1][ny], bot[i][ny]], W)
    for j in range(ny):
        mb.face([top[0][j], top[0][j + 1], bot[0][j + 1], bot[0][j]], W if hg != 0 else P)
        mb.face([bot[nc - 1][j], bot[nc - 1][j + 1], top[nc - 1][j + 1], top[nc - 1][j]], W if hg != 0 else P)
    if ridge and cap is None:
        rl = (hx - hy) if hip_only else (xg if hg > 0 else hx)
        zr = z0 + zmax
        if rl > 0.2: mb.box(-rl, -0.2, zr - 0.1, rl, 0.2, zr + 0.32, ridge_mat)
    return z0 + zmax


def roof_simple(mb, L, D, z0, k, hg, thick=0.28, mat=T):
    """exact flat-plane roof for the far LOD (no curve): gable / hip / hip-and-gable."""
    hx, hy = L / 2, D / 2
    zr = z0 + k * hy
    e = [(-hx, -hy, z0), (hx, -hy, z0), (hx, hy, z0), (-hx, hy, z0)]
    if hg <= 0:
        mb.face([e[0], e[1], (hx, 0, zr), (-hx, 0, zr)], mat); mb.face([e[2], e[3], (-hx, 0, zr), (hx, 0, zr)], mat)
        mb.face([e[3], e[0], (-hx, 0, zr)], P); mb.face([e[1], e[2], (hx, 0, zr)], P)
    elif hg >= k * hy - 1e-6:
        a = max(0.0, hx - hy)
        mb.face([e[0], e[1], (a, 0, zr), (-a, 0, zr)], mat); mb.face([e[2], e[3], (-a, 0, zr), (a, 0, zr)], mat)
        mb.face([e[3], e[0], (-a, 0, zr)], mat); mb.face([e[1], e[2], (a, 0, zr)], mat)
    else:
        xi, yi, zh = hx - hg / k, hy - hg / k, z0 + hg
        i = [(-xi, -yi, zh), (xi, -yi, zh), (xi, yi, zh), (-xi, yi, zh)]
        for a in range(4): mb.face([e[a], e[(a + 1) % 4], i[(a + 1) % 4], i[a]], mat)
        mb.face([i[0], i[1], (xi, 0, zr), (-xi, 0, zr)], mat); mb.face([i[2], i[3], (-xi, 0, zr), (xi, 0, zr)], mat)
        mb.face([i[3], i[0], (-xi, 0, zr)], P); mb.face([i[1], i[2], (xi, 0, zr)], P)
    b = [(x, y, z - thick) for x, y, z in e]
    mb.face([b[3], b[2], b[1], b[0]], W)
    for a in range(4): mb.face([b[a], b[(a + 1) % 4], e[(a + 1) % 4], e[a]], W)
    return zr


def hall(mb, bx, by, bay=(3.0, 3.0), col_h=3.4, plat=0.6, plat_margin=1.2, eave=1.5, k=0.62, kind='hipgable', closed=('back', 'left', 'right'), open_front_depth=0,
         z_base=0.0, detail=0, pillar_r=0.2, round_pillars=True, bracket=0.5, wall_mat=P, steps=True, roof_seg=(12, 8), palace=False, stone_pillars=False, xo=0.0, yo=0.0,
         roof_mat=T, ridge_mat=None, lift=0.35):
    """generic post-and-lintel hall.  bx, by = bays; bay = (x size, y size) or lists.  Returns (L, D, top z)."""
    wx = bay[0] if isinstance(bay[0], (list, tuple)) else [bay[0]] * bx
    wy = bay[1] if isinstance(bay[1], (list, tuple)) else [bay[1]] * by
    L, D = sum(wx), sum(wy)
    if kind != 'hip' and D > L + 1e-6:      # ridge must run along the long side: build turned by 90 deg and rotate back
        turn = {'left': 'back', 'right': 'front', 'front': 'left', 'back': 'right'}
        tmp = MB()
        r = hall(tmp, by, bx, bay=(list(wy), list(reversed(wx))), col_h=col_h, plat=plat, plat_margin=plat_margin, eave=eave, k=k, kind=kind, closed=tuple(turn[c] for c in closed),
                 open_front_depth=0, z_base=z_base, detail=detail, pillar_r=pillar_r, round_pillars=round_pillars, bracket=bracket, wall_mat=wall_mat, steps=False, roof_seg=roof_seg,
                 palace=palace, stone_pillars=stone_pillars, roof_mat=roof_mat, ridge_mat=ridge_mat, lift=lift)
        b = len(mb.v); mb.v.extend([(-y + xo, x + yo, z) for x, y, z in tmp.v]); mb.f.extend([tuple(i + b for i in f) for f in tmp.f]); mb.m.extend(tmp.m); mb.uv.extend(tmp.uv)
        return D, L, r[2]
    xs = [xo - L / 2 + sum(wx[:i]) for i in range(bx + 1)]; ys = [yo - D / 2 + sum(wy[:j]) for j in range(by + 1)]
    z = z_base
    if plat > 0:
        mb.box(xs[0] - plat_margin, ys[0] - plat_margin, z, xs[-1] + plat_margin, ys[-1] + plat_margin, z + plat, S)
        if steps and detail < 2: mb.stairs(xo, ys[0] - plat_margin, z + plat, min(3.0, wx[bx // 2]), max(2, int(round(plat / 0.2))), mat=S)
        z += plat
    zt = z + col_h
    if detail < 2:
        for i, x in enumerate(xs):
            for j, y in enumerate(ys):
                if i in (0, bx) or j in (0, by) or (open_front_depth and j <= open_front_depth):
                    mb.cyl(x, y, z, zt, pillar_r, S if stone_pillars else W, n=8 if detail == 0 else 6, square=not round_pillars)
        mb.box(xs[0] - 0.15, ys[0] - 0.15, zt - 0.3, xs[-1] + 0.15, ys[0] + 0.15, zt, W); mb.box(xs[0] - 0.15, ys[-1] - 0.15, zt - 0.3, xs[-1] + 0.15, ys[-1] + 0.15, zt, W)
        mb.box(xs[0] - 0.15, ys[0], zt - 0.3, xs[0] + 0.15, ys[-1], zt, W); mb.box(xs[-1] - 0.15, ys[0], zt - 0.3, xs[-1] + 0.15, ys[-1], zt, W)
    t = 0.12
    yf = ys[open_front_depth] if open_front_depth else ys[0]
    if detail == 2:
        mb.box(xs[0], yf if 'front' in closed or open_front_depth else ys[0], z, xs[-1], ys[-1], zt, wall_mat if closed else W)
    else:
        if 'front' in closed or open_front_depth: mb.box(xs[0], yf - t, z, xs[-1], yf + t, zt - 0.3, wall_mat)
        if 'back' in closed: mb.box(xs[0], ys[-1] - t, z, xs[-1], ys[-1] + t, zt - 0.3, wall_mat)
        if 'left' in closed: mb.box(xs[0] - t, yf, z, xs[0] + t, ys[-1], zt - 0.3, wall_mat)
        if 'right' in closed: mb.box(xs[-1] - t, yf, z, xs[-1] + t, ys[-1], zt - 0.3, wall_mat)
        if ('front' in closed or open_front_depth) and detail == 0:     # door leaves in the middle bay
            mb.box(xs[bx // 2] + 0.25, yf - t - 0.04, z + 0.1, xs[bx // 2 + 1] - 0.25 if bx % 2 else xs[bx // 2] + wx[bx // 2] - 0.25, yf - t, z + min(2.6, col_h - 0.6), W)
    if bracket > 0:
        mb.box(xs[0] - 0.35, ys[0] - 0.35, zt, xs[-1] + 0.35, ys[-1] + 0.35, zt + bracket, W)
        zt += bracket
    hy = D / 2 + eave
    hg = {'gable': 0.0, 'hip': 1e9, 'hipgable': 0.68 * k * hy}[kind]
    seg = roof_seg if detail == 0 else ((6, 4) if detail == 1 else (4, 2))
    mbx = MB() if (xo or yo) else mb
    if detail == 2: ztop = roof_simple(mbx, L + 2 * (eave if kind != 'gable' else 0.6), D + 2 * eave, zt - 0.05, k, hg, mat=roof_mat)
    else: ztop = roof(mbx, L + 2 * (eave if kind != 'gable' else 0.6), D + 2 * eave, zt - 0.05, k, hg, seg=seg, lift=lift, curve=0.18,
                      mat=roof_mat, ridge_mat=ridge_mat if ridge_mat is not None else (P if palace else roof_mat))
    if mbx is not mb:
        b = len(mb.v); mb.v.extend([(x + xo, y + yo, zz) for x, y, zz in mbx.v]); mb.f.extend([tuple(i + b for i in f) for f in mbx.f]); mb.m.extend(mbx.m); mb.uv.extend(mbx.uv)
    return L, D, ztop


# ------------------------------------------------------------------ stone
def gate_base(mb, width, depth, height, arches, parapet=True, detail=0):
    """석축(육축) with 홍예 tunnels along y.  arches = [(x centre, opening width, total height)]"""
    n = 10 if detail == 0 else (6 if detail == 1 else 3)
    hw, hd = width / 2, depth / 2
    arches = sorted(arches)
    for ys, flip in ((-hd, False), (hd, True)):
        xcur = -hw
        for (xc, w, h) in arches:
            r = w / 2; zj = h - r
            pts = [(xc - r, 0.0), (xc - r, zj)] + [(xc - r * math.cos(math.pi * i / n), zj + r * math.sin(math.pi * i / n)) for i in range(1, n)] + [(xc + r, zj), (xc + r, 0.0)]
            q = [(xcur, ys, 0), (xc - r, ys, 0), (xc - r, ys, height), (xcur, ys, height)]
            mb.face(q[::-1] if flip else q, S)
            for i in range(1, len(pts) - 2):
                a, b2 = pts[i], pts[i + 1]
                q = [(a[0], ys, a[1]), (b2[0], ys, b2[1]), (b2[0], ys, height), (a[0], ys, height)]
                mb.face(q[::-1] if flip else q, S)
            xcur = xc + r
        q = [(xcur, ys, 0), (hw, ys, 0), (hw, ys, height), (xcur, ys, height)]
        mb.face(q[::-1] if flip else q, S)
    for (xc, w, h) in arches:
        r = w / 2; zj = h - r
        pts = [(xc - r, 0.0), (xc - r, zj)] + [(xc - r * math.cos(math.pi * i / n), zj + r * math.sin(math.pi * i / n)) for i in range(1, n)] + [(xc + r, zj), (xc + r, 0.0)]
        for i in range(len(pts) - 1):
            a, b2 = pts[i], pts[i + 1]
            mb.face([(a[0], -hd, a[1]), (a[0], hd, a[1]), (b2[0], hd, b2[1]), (b2[0], -hd, b2[1])], S)
    mb.face([(-hw, -hd, height), (hw, -hd, height), (hw, hd, height), (-hw, hd, height)], S)
    mb.face([(-hw, hd, 0), (-hw, -hd, 0), (-hw, -hd, height), (-hw, hd, height)], S); mb.face([(hw, -hd, 0), (hw, hd, 0), (hw, hd, height), (hw, -hd, height)], S)
    if parapet: yeojang(mb, -hw, -hd + 0.4, hw, -hd + 0.4, height, detail=detail); yeojang(mb, -hw, hd - 0.4, hw, hd - 0.4, height, detail=detail)


def yeojang(mb, x0, y0, x1, y1, z, h=1.2, t=0.75, ta=3.6, gap=0.25, detail=0, mat=B):
    """여장 along an axis-aligned line: 타 blocks (length ta) separated by 타구 gaps, on a continuous low course."""
    along_x = abs(x1 - x0) >= abs(y1 - y0)
    length = abs(x1 - x0) if along_x else abs(y1 - y0)
    a0 = min(x0, x1) if along_x else min(y0, y1); c = y0 if along_x else x0

    def bx(s0, s1, z0, z1):
        if along_x: mb.box(s0, c - t / 2, z0, s1, c + t / 2, z1, mat)
        else: mb.box(c - t / 2, s0, z0, c + t / 2, s1, z1, mat)
    if detail == 2: return bx(a0, a0 + length, z, z + h)
    bx(a0, a0 + length, z, z + h * 0.45)
    n = max(1, int(round(length / ta))); step = length / n
    for i in range(n): bx(a0 + i * step + gap / 2, a0 + (i + 1) * step - gap / 2, z + h * 0.45, z + h)


def woldae(mb, w, d, h, tiers=1, inset=4.0, stairs_front=1, stair_w=6.0, detail=0, rail=False):
    """월대: stacked platforms with front stairs (and side stairs on the top tier)."""
    z = 0.0
    for t in range(tiers):
        ww, dd = w - 2 * inset * t, d - 2 * inset * t
        mb.box(-ww / 2, -dd / 2, z, ww / 2, dd / 2, z + h, S)
        if detail < 2:
            st = max(3, int(round(h / 0.18)))
            run = 0.36
            for sx in ([0.0] if stairs_front == 1 else [-stair_w * 1.2, 0.0, stair_w * 1.2]):
                for i in range(st): mb.box(sx - stair_w / 2, -dd / 2 - (i + 1) * run, z, sx + stair_w / 2, -dd / 2 - i * run, z + h - (i + 1) * h / st + h / st * 0, S) if False else None
                for i in range(st): mb.box(sx - stair_w / 2, -dd / 2 - (i + 1) * run, z, sx + stair_w / 2, -dd / 2 - i * run, z + h * (st - i) / (st + 1), S)
            for side in (-1, 1):
                for i in range(st): mb.box(side * (ww / 2 + i * run), -stair_w / 4, z, side * (ww / 2 + (i + 1) * run), stair_w / 4, z + h * (st - i) / (st + 1), S)
            if rail and detail == 0:
                for (a, b2, c, d2) in ((-ww / 2, -dd / 2, ww / 2, -dd / 2 + 0.25), (-ww / 2, dd / 2 - 0.25, ww / 2, dd / 2), (-ww / 2, -dd / 2, -ww / 2 + 0.25, dd / 2), (ww / 2 - 0.25, -dd / 2, ww / 2, dd / 2)):
                    mb.box(a, b2, z + h + 0.55, c, d2, z + h + 0.75, S)
        z += h
    return z


def city_wall(mb, length=10.0, h=6.5, top_w=3.0, base_extra=1.2, detail=0):
    """성벽 한 구간: 바깥면(-y)은 거의 수직 석축, 안쪽은 흙 내탁 비탈, 위에 걷는 길 + 여장."""
    hl = length / 2
    yo, yi = -top_w / 2, top_w / 2
    mb.face([(-hl, yo - 0.35, 0), (hl, yo - 0.35, 0), (hl, yo, h), (-hl, yo, h)], S)                    # outer face (slight batter)
    mb.face([(-hl, yo, h), (hl, yo, h), (hl, yi, h), (-hl, yi, h)], E)                                    # walkway
    mb.face([(-hl, yi, h), (hl, yi, h), (hl, yi + h * 1.2 + base_extra, 0), (-hl, yi + h * 1.2 + base_extra, 0)], E)   # inner earth slope (내탁)
    for sx, fl in ((-hl, False), (hl, True)):
        q = [(sx, yo - 0.35, 0), (sx, yo, h), (sx, yi, h), (sx, yi + h * 1.2 + base_extra, 0)]
        mb.face(q if fl else q[::-1], S)
    yeojang(mb, -hl, yo + 0.38, hl, yo + 0.38, h, detail=detail, mat=S)


def plain_wall(mb, length, h, t, body, cap='tile', detail=0):
    """담: body + 지붕 덮개.  cap: tile | thatch | none"""
    hl = length / 2
    mb.box(-hl, -t / 2, 0, hl, t / 2, h, body)
    if cap != 'none':
        m = T if cap == 'tile' else E
        o = t / 2 + 0.18
        for q in ([(-hl, -o, h), (hl, -o, h), (hl, 0, h + 0.3), (-hl, 0, h + 0.3)], [(-hl, 0, h + 0.3), (hl, 0, h + 0.3), (hl, o, h), (-hl, o, h)], [(-hl, o, h), (hl, o, h), (hl, -o, h), (-hl, -o, h)]):
            mb.face(q, m)
        mb.face([(-hl, -o, h), (-hl, 0, h + 0.3), (-hl, o, h)], m); mb.face([(hl, o, h), (hl, 0, h + 0.3), (hl, -o, h)], m)


def bridge(mb, length, width, deck_z, piers_x, piers_y, rail=True, detail=0, deck_t=0.45):
    """돌다리 (널다리): 귀틀석 + 청판 deck on rows of stone piers.  length along y (the way people cross), width along x."""
    mb.box(-width / 2, -length / 2, deck_z - deck_t, width / 2, length / 2, deck_z, S)
    if detail < 2:
        for i in range(piers_y):
            y = -length / 2 + length * (i + 0.5) / piers_y
            if detail == 1: mb.box(-width / 2 + 0.3, y - 0.3, 0, width / 2 - 0.3, y + 0.3, deck_z - deck_t, S); continue
            for j in range(piers_x):
                x = -width / 2 + width * (j + 0.5) / piers_x
                mb.box(x - 0.3, y - 0.3, 0, x + 0.3, y + 0.3, deck_z - deck_t, S)
    else: mb.box(-width / 2 + 0.4, -length / 2 + 0.6, 0, width / 2 - 0.4, length / 2 - 0.6, deck_z - deck_t, S)
    if rail and detail < 2:
        n = max(2, int(round(length / 2.4)))
        for side in (-1, 1):
            x = side * (width / 2 - 0.2)
            mb.box(x - 0.1, -length / 2, deck_z + 0.55, x + 0.1, length / 2, deck_z + 0.75, S)
            for i in range(n + 1):
                y = -length / 2 + length * i / n
                mb.box(x - 0.15, y - 0.15, deck_z, x + 0.15, y + 0.15, deck_z + (1.1 if detail == 0 else 0.9), S)


# ------------------------------------------------------------------ the modules
def M_gate_base_large(mb, d): gate_base(mb, 32.0, 12.0, 6.6, [(0.0, 5.2, 6.0)], detail=d)
def M_gate_base_small(mb, d): gate_base(mb, 16.0, 7.5, 4.8, [(0.0, 3.6, 4.2)], detail=d)
def M_gate_base_3arch(mb, d): gate_base(mb, 40.0, 13.0, 7.0, [(-9.5, 4.5, 4.9), (0.0, 5.5, 5.4), (9.5, 4.5, 4.9)], detail=d)


def M_munru_1f_3x2(mb, d): hall(mb, 3, 2, bay=([2.9, 3.6, 2.9], 2.7), col_h=2.7, plat=0.0, eave=1.3, kind='hip', closed=(), detail=d, bracket=0.35, palace=True)


def M_munru_2f_5x2(mb, d):
    wx = [3.9, 4.6, 5.0, 4.6, 3.9]
    hall(mb, 5, 2, bay=(wx, 3.95), col_h=3.3, plat=0.0, eave=1.9, kind='hip', closed=(), detail=d, bracket=0.6, palace=True, roof_seg=(12, 6))
    lower_top = 3.3 + 0.6
    mb.box(-9.6, -3.0, lower_top - 0.1, 9.6, 3.0, lower_top + 1.9, W)
    hall(mb, 5, 2, bay=([3.3, 4.0, 4.6, 4.0, 3.3], 3.0), col_h=2.6, plat=0.0, eave=1.8, kind='hip', closed=('front', 'back', 'left', 'right'), wall_mat=W, detail=d, bracket=0.6, palace=True,
         z_base=lower_top + 1.9, roof_seg=(12, 6))


def M_jongmyo_bay(mb, d): hall(mb, 1, 3, bay=(4.0, [3.6, 4.5, 4.5]), col_h=4.2, plat=0.9, plat_margin=0.0, eave=1.7, k=0.55, kind='gable', closed=('back',), open_front_depth=1,
                               detail=d, bracket=0.3, steps=False, wall_mat=W, roof_seg=(2, 8), lift=0.0)


def M_jongmyo_jeongjeon(mb, d):
    hall(mb, 25, 3, bay=(4.04, [3.6, 4.5, 4.5]), col_h=4.2, plat=0.9, plat_margin=1.5, eave=1.7, k=0.55, kind='gable', closed=('back', 'left', 'right'), open_front_depth=1,
         detail=d, bracket=0.3, steps=False, wall_mat=W, roof_seg=(25 if d == 0 else 8, 8), lift=0.0)
    for side in (-1, 1):   # 동 · 서 월랑 5칸, at right angles (ㄷ자)
        hall(mb, 2, 5, bay=(3.4, 3.4), col_h=3.2, plat=0.6, plat_margin=0.6, eave=1.2, k=0.55, kind='gable', closed=('left',) if side < 0 else ('right',), detail=max(d, 1), bracket=0.0,
             steps=False, xo=side * (50.5 + 4.4), yo=-(6.3 + 9.5), wall_mat=W, lift=0.0, roof_seg=(4, 5))


def M_woldae_jongmyo(mb, d): woldae(mb, 109.0, 69.0, 1.0, tiers=1, stairs_front=3, stair_w=5.0, detail=d)
def M_woldae_2tier(mb, d): woldae(mb, 58.0, 50.0, 1.36, tiers=2, inset=5.5, stairs_front=1, stair_w=7.0, detail=d, rail=True)
def M_woldae_gate(mb, d): woldae(mb, 29.7, 48.7, 0.7, tiers=1, stairs_front=1, stair_w=7.0, detail=d, rail=True)


def M_hall_2tier_5x5(mb, d):
    wx = [5.24, 6.47, 6.78, 6.47, 5.24]; wy = [5.24, 3.39, 3.39, 3.39, 5.24]
    hall(mb, 5, 5, bay=(wx, wy), col_h=4.9, plat=0.0, eave=2.6, kind='hip', closed=('front', 'back', 'left', 'right'), wall_mat=W, detail=d, bracket=1.0, palace=True, roof_seg=(14, 10))
    zt = 4.9 + 1.0
    mb.box(-13.1, -8.3, zt - 0.2, 13.1, 8.3, zt + 3.0, W)
    hall(mb, 5, 5, bay=([3.3, 6.47, 6.78, 6.47, 3.3], [3.3, 3.39, 3.39, 3.39, 3.3]), col_h=3.0, plat=0.0, eave=2.6, k=0.66, kind='hipgable', closed=('front', 'back', 'left', 'right'), wall_mat=W, detail=d, bracket=1.0,
         palace=True, z_base=zt + 3.0, roof_seg=(14, 10))


def M_gyeonghoeru(mb, d):
    bx_, by_ = 34.4 / 7, 28.5 / 5
    mb.box(-18.7, -15.8, 0, 18.7, 15.8, 0.9, S)
    if d < 2:
        for i in range(8):
            for j in range(6):
                outer = i in (0, 7) or j in (0, 5)
                mb.cyl(-17.2 + i * bx_, -14.25 + j * by_, 0.9, 5.6, 0.42, S, n=8 if d == 0 else 4, square=outer)
    else: mb.box(-17.2, -14.25, 0.9, 17.2, 14.25, 5.6, S)
    mb.box(-17.9, -14.95, 5.6, 17.9, 14.95, 6.1, W)
    hall(mb, 7, 5, bay=(bx_, by_), col_h=4.4, plat=0.0, eave=2.6, k=0.6, kind='hipgable', closed=(), detail=d, bracket=0.7, palace=True, z_base=6.1, roof_seg=(14, 10), pillar_r=0.3)


def M_corridor_bay(mb, d): hall(mb, 1, 2, bay=(3.0, 2.6), col_h=3.0, plat=0.45, plat_margin=0.0, eave=1.1, k=0.55, kind='gable', closed=('back',), detail=d, bracket=0.0, steps=False,
                                roof_seg=(2, 6), lift=0.0)


def M_corridor_10(mb, d): hall(mb, 10, 2, bay=(3.0, 2.6), col_h=3.0, plat=0.45, plat_margin=0.3, eave=1.1, k=0.55, kind='gable', closed=('back',), detail=d, bracket=0.0, steps=False,
                               roof_seg=(10 if d == 0 else 4, 6), lift=0.0)


def M_sijeon_row(mb, d):
    hall(mb, 8, 2, bay=(2.6, [4.2, 4.5]), col_h=2.7, plat=0.3, plat_margin=0.2, eave=1.0, k=0.5, kind='gable', closed=('back', 'left', 'right'), detail=d, bracket=0.0, steps=False,
         roof_seg=(8 if d == 0 else 4, 6), lift=0.0, round_pillars=False, pillar_r=0.12)
    if d < 2:
        for i in range(8):   # shop fronts: low counter board + shutter above in every other bay
            x = -10.4 + i * 2.6
            mb.box(x + 0.15, -4.35 - 0.06, 0.3, x + 2.45, -4.35 + 0.06, 1.1, W)
            if i % 2 == 0 and d == 0: mb.box(x + 0.15, -4.35 - 0.05, 2.0, x + 2.45, -4.35 + 0.05, 2.7, W)
        mb.box(-10.4, -0.15 - 0.06, 0.3, 10.4, -0.15 + 0.06, 2.7, P)


def M_soseuldaemun(mb, d):
    for sx in (-1, 1):
        hall(mb, 2, 1, bay=(2.5, 2.6), col_h=2.5, plat=0.3, plat_margin=0.2, eave=0.9, k=0.55, kind='gable', closed=('front', 'back', 'left', 'right'), detail=d, bracket=0.0, steps=False,
             xo=sx * 4.1, roof_seg=(2, 4), lift=0.0, round_pillars=False, pillar_r=0.12)
    hall(mb, 1, 1, bay=(3.2, 2.6), col_h=3.7, plat=0.3, plat_margin=0.2, eave=0.9, k=0.55, kind='gable', closed=(), detail=d, bracket=0.0, steps=False, roof_seg=(2, 4), lift=0.0,
         round_pillars=False, pillar_r=0.15)
    if d < 2:
        mb.box(-1.45, -0.08, 0.3, -0.03, 0.02, 3.2, W); mb.box(0.03, -0.08, 0.3, 1.45, 0.02, 3.2, W)


def M_giwa_4kan(mb, d): hall(mb, 4, 2, bay=(2.46, 2.46), col_h=2.6, plat=0.45, plat_margin=0.6, eave=1.2, k=0.58, kind='hipgable', closed=('back', 'left', 'right'), open_front_depth=1, detail=d,
                             bracket=0.0, round_pillars=False, pillar_r=0.11, roof_seg=(10, 6))


def M_giwa_L(mb, d):
    hall(mb, 4, 2, bay=(2.46, 2.46), col_h=2.6, plat=0.45, plat_margin=0.6, eave=1.2, k=0.58, kind='hipgable', closed=('back', 'left', 'right'), open_front_depth=1, detail=d, bracket=0.0,
         round_pillars=False, pillar_r=0.11, roof_seg=(10, 6))
    hall(mb, 2, 3, bay=(2.46, 2.46), col_h=2.6, plat=0.45, plat_margin=0.6, eave=1.2, k=0.58, kind='gable', closed=('front', 'back', 'left', 'right'), detail=d, bracket=0.0, steps=False,
         xo=-(4.92 - 2.46), yo=-(2.46 + 3.69), round_pillars=False, pillar_r=0.11, roof_seg=(4, 6), lift=0.0)


def M_giwa_shop(mb, d): hall(mb, 3, 2, bay=(2.46, 2.2), col_h=2.5, plat=0.3, plat_margin=0.3, eave=1.0, k=0.55, kind='gable', closed=('back', 'left', 'right'), detail=d, bracket=0.0, steps=False,
                             round_pillars=False, pillar_r=0.11, roof_seg=(4, 6), lift=0.0)


def M_wall_city(mb, d): city_wall(mb, 10.0, 6.5, 3.0, detail=d)
def M_wall_yeojang(mb, d): yeojang(mb, -5.0, 0, 5.0, 0, 0.0, detail=d, mat=S)
def M_wall_gungjang(mb, d): plain_wall(mb, 6.0, 4.2, 0.9, S, 'tile', d)
def M_wall_todam(mb, d): plain_wall(mb, 4.0, 1.8, 0.45, E, 'tile', d)
def M_wall_makdol(mb, d): plain_wall(mb, 4.0, 1.5, 0.6, S, 'thatch', d)
def M_bridge_gwangtong(mb, d): bridge(mb, 12.3, 14.4, 3.4, 6, 3, detail=d)
def M_bridge_supyo(mb, d): bridge(mb, 27.0, 7.0, 3.0, 5, 9, detail=d)
def M_bridge_yeongje(mb, d): bridge(mb, 13.85, 9.8, 2.2, 4, 3, detail=d)


def M_sajikdan(mb, d):
    for sx in (-1, 1):
        cx = sx * 9.0
        mb.box(cx - 3.85, -3.85, 0, cx + 3.85, 3.85, 0.92, S)
        if d < 2:
            for dx, dy in ((0, -1), (0, 1), (-1, 0), (1, 0)):
                for i in range(3):
                    a = 3.85 + i * 0.4
                    if dy: mb.box(cx - 1.2, dy * a, 0, cx + 1.2, dy * (a + 0.4), 0.92 * (3 - i) / 4, S)
                    else: mb.box(cx + dx * a, -1.2, 0, cx + dx * (a + 0.4), 1.2, 0.92 * (3 - i) / 4, S)
    if d < 2:   # 유 (낮은 담) round both altars with an opening in the middle of each side
        for (a, b2, c, e) in ((-19, -11, -1.5, -10.4), (1.5, -11, 19, -10.4), (-19, 10.4, -1.5, 11), (1.5, 10.4, 19, 11), (-19, -11, -18.4, -1.5), (-19, 1.5, -18.4, 11), (18.4, -11, 19, -1.5), (18.4, 1.5, 19, 11)):
            mb.box(a, b2, 0, c, e, 1.0, S)


def M_pumgyeseok(mb, d): mb.box(-0.22, -0.11, 0, 0.22, 0.11, 0.55, S); mb.box(-0.26, -0.15, 0, 0.26, 0.15, 0.08, S)


def M_pedestal_haetae(mb, d):
    mb.box(-0.95, -1.55, 0, 0.95, 1.55, 0.35, S); mb.box(-0.8, -1.4, 0.35, 0.8, 1.4, 1.15, S); mb.box(-0.9, -1.5, 1.15, 0.9, 1.5, 1.4, S)


def M_hamabi(mb, d): mb.box(-0.3, -0.2, 0, 0.3, 0.2, 0.2, S); mb.box(-0.22, -0.1, 0.2, 0.22, 0.1, 1.45, S)


MODULES = [
    ('Gen_GateBase_Large', M_gate_base_large, '숭례문 · 흥인지문 격 석축: 너비 32 × 깊이 12 × 높이 6.6 m, 홍예 너비 5.2 · 높이 6.0 m (육축 높이 6.4–6.67 m · 홍예 16–18척, 너비 · 깊이는 EST)'),
    ('Gen_GateBase_Small', M_gate_base_small, '소문 석축: 16 × 7.5 × 4.8 m, 홍예 3.6 × 4.2 m (EST — 실측값 못 찾음)'),
    ('Gen_GateBase_3Arch', M_gate_base_3arch, '광화문 석축: 높이 7.0 m, 홍예 셋 (가운데 5.5 × 5.4, 좌우 4.5 × 4.9 m), 너비 40 · 깊이 13 m EST'),
    ('Gen_Munru_1F_3x2', M_munru_1f_3x2, '소문 문루: 정면 3칸 · 측면 2칸 단층 우진각 (창의문 꼴), 9.4 × 5.4 m EST'),
    ('Gen_Munru_2F_5x2', M_munru_2f_5x2, '대문 문루: 정면 5칸 · 측면 2칸 중층 우진각, 하층 22.0 × 7.9 m EST (숭례문 · 흥인지문)'),
    ('Gen_Jongmyo_Bay', M_jongmyo_bay, '종묘 정전 한 칸: 폭 4.0 m, 측면 3칸 (앞 퇴칸 트임), 맞배, 기단 0.9 m (101 m ÷ 25칸 EST)'),
    ('Gen_Jongmyo_Jeongjeon', M_jongmyo_jeongjeon, '종묘 정전: 신실 19칸 + 협실 좌우 3칸 = 25칸 101 m 맞배 + 동 · 서 월랑 5칸'),
    ('Gen_Woldae_Jongmyo', M_woldae_jongmyo, '종묘 정전 월대: 동서 109 × 남북 69 m, 높이 1.0 m EST'),
    ('Gen_Woldae_2Tier', M_woldae_2tier, '근정전 2단 월대: 합 2.72 m (각 1.36 m), 하월대 58 × 50 m EST, 돌난간'),
    ('Gen_Woldae_Gate', M_woldae_gate, '광화문 월대: 남북 48.7 × 동서 29.7 m, 높이 0.7 m EST'),
    ('Gen_Hall_2Tier_5x5', M_hall_2tier_5x5, '근정전 꼴: 정면 5칸 30.2 m · 측면 5칸 20.6 m 중층 팔작, 기둥 4.9 m (칸 치수 = 척 환산)'),
    ('Gen_Gyeonghoeru', M_gyeonghoeru, '경회루 꼴: 정면 7칸 34.4 × 측면 5칸 28.5 m 중층 팔작, 돌기둥 48 (바깥 네모 24 · 안 둥근 24), 돌기둥 높이 4.7 m EST'),
    ('Gen_Corridor_Bay', M_corridor_bay, '행각 한 칸: 3.0 × 5.2 m 맞배, 바깥벽 막힘 · 안쪽 트임 (EST)'),
    ('Gen_Corridor_10', M_corridor_10, '행각 10칸 30 m'),
    ('Gen_Sijeon_Row8', M_sijeon_row, '시전행랑 8칸: 깊이 8.7 m (청진동 발굴), 칸 폭 2.6 m EST, 맞배, 앞 가게 · 뒤 방'),
    ('Gen_Soseuldaemun', M_soseuldaemun, '솟을대문 + 좌우 행랑 2칸씩: 문칸 3.2 m, 문칸 기둥 3.7 m · 행랑 2.5 m (EST)'),
    ('Gen_Giwa_4kan', M_giwa_4kan, '기와 민가 一자: 정면 4칸 · 측면 2칸 (8자 = 2.46 m 칸), 팔작, 앞 툇마루'),
    ('Gen_Giwa_L', M_giwa_L, '기와 민가 ㄱ자: 4칸 몸채 + 3칸 날개'),
    ('Gen_Giwa_Shop3', M_giwa_shop, '기와 가게 3칸: 7.4 × 4.4 m 맞배, 앞 트임'),
    ('Gen_Wall_City10', M_wall_city, '도성 성벽 10 m: 바깥 높이 6.5 m, 걷는 길 3.0 m, 안쪽 내탁 비탈, 여장 1.2 m (높이 5–8 m 범위 · 여장 치수 EST)'),
    ('Gen_Wall_Yeojang10', M_wall_yeojang, '여장만 10 m: 타 3.3 m · 타구 0.25 m · 높이 1.2 m · 두께 0.75 m (EST)'),
    ('Gen_Wall_Gungjang6', M_wall_gungjang, '궁장 6 m: 높이 4.2 + 기와 덮개, 두께 0.9 m (EST)'),
    ('Gen_Wall_Todam4', M_wall_todam, '토담 4 m: 높이 1.8 m, 기와 덮개'),
    ('Gen_Wall_Makdol4', M_wall_makdol, '막돌담 4 m: 높이 1.5 m, 이엉 덮개'),
    ('Gen_Bridge_Gwangtong', M_bridge_gwangtong, '광통교: 길이 12.3 × 너비 14.4 m, 난간'),
    ('Gen_Bridge_Supyo', M_bridge_supyo, '수표교: 27 × 7 m, 교각 9줄 × 5열, 난간'),
    ('Gen_Bridge_Yeongje', M_bridge_yeongje, '영제교: 13.85 × 9.8 m'),
    ('Gen_Sajikdan', M_sajikdan, '사직단: 사단 · 직단 각 한 변 7.7 m (2장 5척) · 높이 0.92 m (3척), 사방 3단 계단, 유(낮은 담)'),
    ('Gen_Pumgyeseok', M_pumgyeseok, '품계석: 0.44 × 0.22 × 0.55 m (EST)'),
    ('Gen_Pedestal_Haetae', M_pedestal_haetae, '해태상 받침: 1.9 × 3.1 × 1.4 m (EST, 계획서의 받침 높이 1.4 m)'),
    ('Gen_Hamabi', M_hamabi, '하마비: 0.44 × 0.2 × 1.45 m (EST)'),
]


def main():
    cls = 'gen'
    odir = OUT / cls; odir.mkdir(parents=True, exist_ok=True)
    sdir = OUT / '_sheets' / cls; sdir.mkdir(parents=True, exist_ok=True)
    for name, fn, note in MODULES:
        t0 = time.time()
        bpy.ops.wm.read_factory_settings(use_empty=True)
        lods = []
        for d in range(3):
            mb = MB(); fn(mb, d); lods.append(mb.build('%s_LOD%d' % (name, d)))
        mn, mx = PK.bounds(lods[0].data)
        corners = np.array([[x, y, z] for x in (mn[0], mx[0]) for y in (mn[1], mx[1]) for z in (mn[2], mx[2])], dtype=np.float64)
        px = float(max(mx - mn)) / 512.0
        umn, umx = PK.unity_box(mn, mx)
        entry = {'name': name, 'class': cls, 'kind': 'generated', 'source': 'Tools/Blender/PackLite308/pl_modules.py', 'reference': note, 'front': 'Unity -Z (door side); long axis = X',
                 'pivot': 'ground centre', 'source_tris': 0, 'bounds_unity': {'min': umn, 'max': umx, 'size': [round(umx[i] - umn[i], 3) for i in range(3)]}, 'below_ground_m': 0.0,
                 'materials': [{'name': m.name, 'albedo': None, 'note': 'plain metre UVs; assign a project material'} for m in lods[0].data.materials], 'lods': []}
        tiles = {v: [] for v in V.VIEWS}
        for i, lo in enumerate(lods):
            fbx = odir / ('%s_LOD%d.fbx' % (name, i))
            sha = PK.export_fbx(lo, fbx)
            zn, dg = PK.normal_check(lo.data)
            A = V.arrays(lo.data)
            Pi, Ci, Ni = V.sample(A, px, [{'tex': None, 'color': np.array(COL.get(m.name, (0.6, 0.6, 0.6)), dtype=np.float32)} for m in lo.data.materials], seed=3 + i)
            for v in V.VIEWS: tiles[v].append(V.splat(Pi, Ci, Ni, V.frame(corners, v, (360, 270)))[0])
            entry['lods'].append({'lod': i, 'fbx': '%s/%s' % (cls, fbx.name), 'tris': int(len(A['tri'])), 'verts': len(lo.data.vertices), 'materials': len(lo.data.materials),
                                  'iou': {}, 'zero_normals': zn, 'degenerate_tris': dg, 'bytes': fbx.stat().st_size, 'sha256': sha})
        V.save_png(str(sdir / (name + '.png')), np.concatenate([np.concatenate(tiles[v], axis=1) for v in ('fromNegZ', 'fromPosX', 'top', 'q34')], axis=0))
        entry['sheet'] = '_sheets/%s/%s.png' % (cls, name); entry['secs'] = round(time.time() - t0, 1)
        (odir / (name + '.json')).write_text(json.dumps(entry, ensure_ascii=False, indent=1), encoding='utf-8')
        PK.log('GEN', name, 'tris', [l['tris'] for l in entry['lods']], 'size', entry['bounds_unity']['size'], 'zn/dg', [(l['zero_normals'], l['degenerate_tris']) for l in entry['lods']])


if __name__ == '__main__':
    main()
