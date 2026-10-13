"""#308 HeroHanok: parametric traditional Korean timber building generator (offline, Blender -b, CPU only).

Model space while building: x = width, y = depth (FRONT at -y), z = up, metres, origin = ground centre.
Exported like PackLite gen modules: Unity X = x, Y = z, Z = y -> the front faces Unity -Z.
detail 0 = LOD0 (tile rows, rafters, bracket clusters, door frames), 1 = LOD1 (smooth roof, stepped bracket band, flat panels,
<= 6 materials), 2 = LOD2 proxy (boxes + coarse roofs, one palette material).
Pieces: platform / stairs / rail, columns, beams_brackets, walls, RectRoof (hip / hipgable / gable, optional cap = lower ring of
a two-storey roof), PolyRoof (4 / 6 / 8-sided pavilion), gate_base (stone base with arches + parapet).
Roof geometry: every slope is a sector of columns running from the eave up to the ridge or the hip.  One analytic surface
z = ze + g(t) + lift * c(x, y), plan push bow * c (c = corner-ness) gives 앙곡 + 안허리곡; hips match because both slopes
evaluate the same function.  겹처마 = fascia + 부연 soffit + step + 서까래 soffit (+ real rafters at LOD0).
"""
import bpy, bmesh, sys, json, math, time
from pathlib import Path
import numpy as np
HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent / 'PackLite308'))
import pl_view as V
import packlite308 as PK
V.VIEWS['low'] = ((0.5, -0.74, 0.30), (0.0, 0.0, 1.0))      # from the front-right, looking UP under the eaves
BIGV = ('fromNegZ', 'fromPosX', 'top', 'q34')
OUT = PK.OUT
CLS = 'hero'
HH = 'HwaseongHaenggung'
# key: (material name, albedo, normal, tint, metres per texture repeat for box-projected UVs)
MATS = {
    'stone': ('PL_%s_T_KoreanStone_1_BC' % HH, '_textures/%s/T_KoreanStone_1_BC.png' % HH, '_textures/%s/T_KoreanStone_1_N.png' % HH, (1, 1, 1), 2.0),
    'ashlar': ('PL_HwaseongForteressGate_T_Fortification_001_BC', '_textures/HwaseongForteressGate/T_Fortification_001_BC.png', '_textures/HwaseongForteressGate/T_Fortification_001_N.png', (1, 1, 1), 3.2),
    'wood': ('PL_%s_T_KoreanWood_2_BC' % HH, '_textures/%s/T_KoreanWood_2_BC.png' % HH, '_textures/%s/T_KoreanWood_2_N.png' % HH, (1, 1, 1), 2.0),
    'green': ('PL_%s_T_KoreanWood_5_BC' % HH, '_textures/%s/T_KoreanWood_5_BC.png' % HH, '_textures/%s/T_KoreanWood_5_N.png' % HH, (1, 1, 1), 2.0),
    'dan': ('PL_Hero_Dancheong', 'hero/Hero_Dancheong_BC.png', None, (1, 1, 1), 1.0),
    'lattice': ('PL_Hero_Lattice', 'hero/Hero_Lattice_BC.png', 'hero/Hero_Lattice_N.png', (1, 1, 1), 1.0),
    'paper': ('PL_%s_T_KoreanPaper_2_BC' % HH, '_textures/%s/T_KoreanPaper_2_BC.png' % HH, '_textures/%s/T_KoreanPaper_2_N.png' % HH, (1, 1, 1), 2.0),
    'plaster': ('PL_%s_T_KoreanPaper_1_BC' % HH, '_textures/%s/T_KoreanPaper_1_BC.png' % HH, None, (1, 1, 1), 2.0),
    'tile': ('PL_Hero_Tile', 'hero/Hero_Tile_BC.png', 'hero/Hero_Tile_N.png', (1, 1, 1), 1.2),
    'floor': ('PL_%s_T_Koreanwood_1_BC' % HH, '_textures/%s/T_Koreanwood_1_BC.png' % HH, '_textures/%s/T_Koreanwood_1_N.png' % HH, (1, 1, 1), 2.0),
    'brick': ('PL_Hero_Jeondol', '_textures/HwaseongForteressGate/T_Fortification_001_BC.png', '_textures/HwaseongForteressGate/T_Fortification_001_N.png', (0.55, 0.56, 0.60), 1.4),
    'bronze': ('PL_Hero_Bronze', None, None, (0.24, 0.31, 0.27), 1.0),
}
KEYS = list(MATS)
FOLD = {'green': 'dan', 'plaster': 'stone', 'ashlar': 'stone', 'floor': 'wood', 'brick': 'stone'}    # LOD1: <= 6 materials
GREEN_UV = [(0.5, 0.5)] * 4


def sgn(v): return 1.0 if v >= 0 else -1.0


class HB:
    def __init__(s, detail=0): s.v = []; s.f = []; s.m = []; s.uv = []; s.d = detail

    def face(s, pts, mat, n=None, uv=None):
        if n is not None:
            nx = ny = nz = 0.0; k = len(pts)
            for i in range(k):
                a = pts[i]; b = pts[(i + 1) % k]
                nx += (a[1] - b[1]) * (a[2] + b[2]); ny += (a[2] - b[2]) * (a[0] + b[0]); nz += (a[0] - b[0]) * (a[1] + b[1])
            if nx * n[0] + ny * n[1] + nz * n[2] < 0:
                pts = pts[::-1]; uv = uv[::-1] if uv else None
        if s.d >= 1 and mat in FOLD:
            if mat == 'green': uv = GREEN_UV[:len(pts)] if len(pts) <= 4 else [(0.5, 0.5)] * len(pts)
            mat = FOLD[mat]
        b = len(s.v); s.v.extend(pts); s.f.append(tuple(range(b, b + len(pts)))); s.m.append(KEYS.index(mat)); s.uv.append(uv)

    def box(s, x0, y0, z0, x1, y1, z1, mat, top=None, bottom=False, lu=None, notop=False):
        if x1 < x0: x0, x1 = x1, x0
        if y1 < y0: y0, y1 = y1, y0
        if z1 < z0: z0, z1 = z1, z0
        a, b, c, d = (x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0)
        e, f, g, h = (x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)
        lx = (x1 - x0) >= (y1 - y0)
        for i, q in enumerate(((a, b, f, e), (b, c, g, f), (c, d, h, g), (d, a, e, h))):
            uv = None
            if lu:
                if (i % 2 == 0) == lx: u0, u1 = (lu[0], lu[1]) if i < 2 else (lu[1], lu[0])
                else: u0, u1 = lu[0], lu[0] + 0.03
                uv = [(u0, 0.0), (u1, 0.0), (u1, 1.0), (u0, 1.0)]
            s.face(list(q), mat, uv=uv)
        if lu: tuv = [(lu[0], 0), (lu[1], 0), (lu[1], 1), (lu[0], 1)] if lx else [(lu[0], 0), (lu[0], 1), (lu[1], 1), (lu[1], 0)]
        else: tuv = None
        if not notop: s.face([e, f, g, h], top or mat, uv=tuv if not top else None)
        if bottom: s.face([d, c, b, a], mat, uv=[tuv[3], tuv[2], tuv[1], tuv[0]] if tuv else None)

    def beam(s, p0, p1, w, h, mat, down=False, caps=(True, True), lu=None, bottom=True):
        """sheared box along p0 -> p1 (axis on its bottom centre, or top centre when down)."""
        dx, dy, dz = p1[0] - p0[0], p1[1] - p0[1], p1[2] - p0[2]
        L = math.hypot(dx, dy)
        if L < 1e-6: rx, ry = w / 2, 0.0
        else: rx, ry = -dy / L * w / 2, dx / L * w / 2
        zo = -h if down else 0.0
        A = [(p0[0] - rx, p0[1] - ry, p0[2] + zo), (p0[0] + rx, p0[1] + ry, p0[2] + zo), (p0[0] + rx, p0[1] + ry, p0[2] + zo + h), (p0[0] - rx, p0[1] - ry, p0[2] + zo + h)]
        B = [(p1[0] - rx, p1[1] - ry, p1[2] + zo), (p1[0] + rx, p1[1] + ry, p1[2] + zo), (p1[0] + rx, p1[1] + ry, p1[2] + zo + h), (p1[0] - rx, p1[1] - ry, p1[2] + zo + h)]
        u = lu or None
        def uvq(): return [(u[0], 0), (u[1], 0), (u[1], 1), (u[0], 1)] if u else None
        s.face([A[0], B[0], B[3], A[3]], mat, n=(-rx, -ry, 0), uv=uvq())
        s.face([A[1], B[1], B[2], A[2]], mat, n=(rx, ry, 0), uv=uvq())
        if not down: s.face([A[3], B[3], B[2], A[2]], mat, n=(0, 0, 1), uv=uvq())
        if bottom or down: s.face([A[0], B[0], B[1], A[1]], mat, n=(0, 0, -1), uv=uvq())
        if caps[0]: s.face(A, mat, n=(-dx, -dy, -dz * 0.0 - 1e-9))
        if caps[1]: s.face(B, mat, n=(dx, dy, 1e-9))

    def cyl(s, x, y, z0, z1, r0, r1, mat, n=10, top=True, bot=False, rot=0.0):
        r0x = [(x + r0 * math.cos(rot + 2 * math.pi * i / n), y + r0 * math.sin(rot + 2 * math.pi * i / n), z0) for i in range(n)]
        r1x = [(x + r1 * math.cos(rot + 2 * math.pi * i / n), y + r1 * math.sin(rot + 2 * math.pi * i / n), z1) for i in range(n)]
        per = 2 * math.pi * max(r0, r1) / MATS[mat][4]; hv = (z1 - z0) / MATS[mat][4]
        for i in range(n):
            j = (i + 1) % n
            s.face([r0x[i], r0x[j], r1x[j], r1x[i]], mat, uv=[(per * i / n, 0), (per * (i + 1) / n, 0), (per * (i + 1) / n, hv), (per * i / n, hv)])
        if top and r1 > 1e-4: s.face(r1x, mat)
        if bot: s.face(r0x[::-1], mat)

    def build(s, name):
        me = bpy.data.meshes.new(name)
        me.from_pydata([(-x, -y, z) for x, y, z in s.v], [], s.f)      # model -> Blender work space (front -> +Y == Unity -Z)
        for k in KEYS: me.materials.append(bpy.data.materials.get(MATS[k][0]) or bpy.data.materials.new(MATS[k][0]))
        me.polygons.foreach_set('material_index', np.array(s.m, dtype=np.int32))
        uvl = me.uv_layers.new(name='UVMap')
        flat = np.zeros((len(me.loops), 2), dtype=np.float32); li = 0
        for fi, f in enumerate(s.f):
            uv = s.uv[fi]; k = len(f)
            if uv is None:
                pts = [s.v[i] for i in f]
                nx = ny = nz = 0.0
                for i in range(k):
                    a = pts[i]; b = pts[(i + 1) % k]
                    nx += (a[1] - b[1]) * (a[2] + b[2]); ny += (a[2] - b[2]) * (a[0] + b[0]); nz += (a[0] - b[0]) * (a[1] + b[1])
                sc = 1.0 / MATS[KEYS[s.m[fi]]][4]
                ax, ay, az = abs(nx), abs(ny), abs(nz)
                if az >= ax and az >= ay: uv = [(p[0] * sc, p[1] * sc) for p in pts]
                elif ax >= ay: uv = [(p[1] * sc, p[2] * sc) for p in pts]
                else: uv = [(p[0] * sc, p[2] * sc) for p in pts]
            flat[li:li + k] = uv[:k]; li += k
        uvl.data.foreach_set('uv', flat.reshape(-1))
        me.update()
        bm = bmesh.new(); bm.from_mesh(me)
        bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=2e-4)
        bm.to_mesh(me); bm.free(); me.update()
        o = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(o)
        PK.activate(o); bpy.ops.object.material_slot_remove_unused()
        PK.cleanup(o, 40.0)
        return o


# ------------------------------------------------------------------ side mapping (f = front -y, b = back +y, l = -x, r = +x)
def mp(side, c, e, s_, o):
    if side == 'f': return (c + s_, e - o)
    if side == 'b': return (c + s_, e + o)
    if side == 'l': return (e - o, c + s_)
    return (e + o, c + s_)


def mbox(hb, side, c, e, s0, s1, o0, o1, z0, z1, mat, **kw):
    p = mp(side, c, e, s0, o0); q = mp(side, c, e, s1, o1)
    hb.box(p[0], p[1], z0, q[0], q[1], z1, mat, **kw)


def mquad(hb, side, c, e, s0, s1, o, z0, z1, mat, uv=None):
    p = mp(side, c, e, s0, o); q = mp(side, c, e, s1, o); n = mp(side, 0, 0, 0, 1)
    hb.face([(p[0], p[1], z0), (q[0], q[1], z0), (q[0], q[1], z1), (p[0], p[1], z1)], mat, n=(n[0], n[1], 0), uv=uv)


def edge_of(side, R): return {'f': R[1], 'b': R[3], 'l': R[0], 'r': R[2]}[side]
def grid(bays, c=0.0):
    L = sum(bays); return [c - L / 2 + sum(bays[:i]) for i in range(len(bays) + 1)]


# ------------------------------------------------------------------ stone: platform, stairs, rails
def platform(hb, x0, y0, x1, y1, z0, h, cap=0.18, over=0.07, side='ashlar', top='stone'):
    d = hb.d
    if d == 2: hb.box(x0, y0, z0, x1, y1, z0 + h, 'stone'); return
    hb.box(x0, y0, z0, x1, y1, z0 + h - cap, side, notop=True)
    hb.box(x0 - over, y0 - over, z0 + h - cap, x1 + over, y1 + over, z0 + h, 'stone', top=top, bottom=True)


def stairs(hb, side, c, e, z_top, z_bot, width, run=None, cheek=0.32):
    """steps descending outward from the edge `e` of side `side`, centred at `c` along the side; with 소맷돌."""
    d = hb.d
    rise = z_top - z_bot; n = max(1, int(round(rise / 0.19))); run = run or n * 0.34
    lat = mp(side, 0, 0, 1, 0); out = mp(side, 0, 0, 0, 1)
    P = lambda s_, o, z: mp(side, c, e, s_, o) + (z,)
    if d == 2:
        hb.face([P(-width / 2, 0, z_top), P(width / 2, 0, z_top), P(width / 2, run, z_bot), P(-width / 2, run, z_bot)], 'stone', n=(out[0], out[1], 1))
        for sg in (-1, 1): hb.face([P(sg * width / 2, 0, z_bot), P(sg * width / 2, run, z_bot), P(sg * width / 2, 0, z_top)], 'stone', n=(sg * lat[0], sg * lat[1], 0))
        return
    for i in range(n - 1):
        mbox(hb, side, c, e, -width / 2, width / 2, i * run / (n - 1), (i + 1) * run / (n - 1), z_bot, z_top - (i + 1) * rise / n, 'stone')
    # top landing strip is the platform itself; 소맷돌 (sloped side stones)
    for sg in (-1, 1):
        s0 = sg * width / 2; s1 = sg * (width / 2 + cheek); zl = 0.22
        hb.face([P(s0, 0, z_top + zl), P(s1, 0, z_top + zl), P(s1, run + 0.1, z_bot + zl), P(s0, run + 0.1, z_bot + zl)], 'stone', n=(out[0], out[1], 1))
        for ss, nn in ((s0, -sg), (s1, sg)):
            hb.face([P(ss, 0, z_bot), P(ss, run + 0.1, z_bot), P(ss, run + 0.1, z_bot + zl), P(ss, 0, z_top + zl)], 'stone', n=(nn * lat[0], nn * lat[1], 0))
        hb.face([P(s0, run + 0.1, z_bot), P(s1, run + 0.1, z_bot), P(s1, run + 0.1, z_bot + zl), P(s0, run + 0.1, z_bot + zl)], 'stone', n=(out[0], out[1], 0))


def rail(hb, pa, pb, z, h=1.0, span=2.3, post=0.2, knob=True, mat='stone', mid=True):
    """stone (or wood) balustrade from pa to pb (xy), posts + 돌란대 + 하엽동자."""
    d = hb.d
    if d == 2: return
    L = math.hypot(pb[0] - pa[0], pb[1] - pa[1])
    if L < 0.3: return
    n = max(1, int(round(L / span)))
    ux, uy = (pb[0] - pa[0]) / L, (pb[1] - pa[1]) / L
    for i in range(n + 1):
        x, y = pa[0] + ux * L * i / n, pa[1] + uy * L * i / n
        hb.box(x - post / 2, y - post / 2, z, x + post / 2, y + post / 2, z + h, mat)
        if d == 0 and knob: hb.cyl(x, y, z + h, z + h + 0.16, post * 0.42, post * 0.2, mat, n=6)
    r = post * 0.36
    hb.beam((pa[0], pa[1], z + h * 0.80), (pb[0], pb[1], z + h * 0.80), 2 * r, 2 * r, mat, caps=(False, False))
    if d == 0:
        hb.beam((pa[0], pa[1], z), (pb[0], pb[1], z), post * 0.9, 0.1, mat, caps=(False, False), bottom=False)
        if mid:
            for i in range(n):
                t = (i + 0.5) / n; x, y = pa[0] + ux * L * t, pa[1] + uy * L * t
                hb.beam((x - ux * 0.11, y - uy * 0.11, z + 0.1), (x + ux * 0.11, y + uy * 0.11, z + 0.1), post * 0.55, h * 0.70 - 0.02, mat, bottom=False)


def rail_rect(hb, x0, y0, x1, y1, z, gaps=(), **kw):
    """rail around a rectangle; gaps = [(side, centre, width)] left open (stairs)."""
    for side, a, b, e in (('f', x0, x1, y0), ('b', x0, x1, y1), ('l', y0, y1, x0), ('r', y0, y1, x1)):
        cuts = sorted([(c - w / 2, c + w / 2) for sd, c, w in gaps if sd == side])
        s = a
        for c0, c1 in cuts + [(b, b)]:
            if c0 - s > 0.4:
                pa = (s, e) if side in 'fb' else (e, s); pb = (c0, e) if side in 'fb' else (e, c0)
                rail(hb, pa, pb, z, **kw)
            s = max(s, c1)


# ------------------------------------------------------------------ timber frame
def columns(hb, xs, ys, z0, h, r=0.28, which='perim', mat='wood', square=False, base=True, taper=0.9, skip=None):
    d = hb.d
    if d == 2: return
    for i, x in enumerate(xs):
        for j, y in enumerate(ys):
            per = i in (0, len(xs) - 1) or j in (0, len(ys) - 1)
            if which == 'perim' and not per: continue
            if which == 'inner' and per: continue
            if skip and skip(i, j): continue
            sq = square(i, j) if callable(square) else square
            zb = z0
            if base:
                bh = 0.12; hb.box(x - r * 1.5, y - r * 1.5, z0, x + r * 1.5, y + r * 1.5, z0 + bh, 'stone') if d == 0 else None
                zb = z0 + (bh if d == 0 else 0)
            if sq: hb.cyl(x, y, zb, z0 + h, r * 1.25, r * 1.25 * taper, mat, n=4, rot=math.pi / 4, top=False)
            else: hb.cyl(x, y, zb, z0 + h, r, r * taper, mat, n=10 if d == 0 else 6, top=False)


def cluster(hb, side, R, s_, z0, h, proj, n):
    e = edge_of(side, R)
    mbox(hb, side, 0, e, s_ - 0.24, s_ + 0.24, -0.2, 0.26, z0, z0 + 0.2, 'dan', lu=(0.3, 0.7))            # 주두
    hs = (h - 0.2 - 0.24) / n
    for k in range(n):
        z = z0 + 0.2 + k * hs; o = proj * (k + 1) / n
        mbox(hb, side, 0, e, s_ - 0.075, s_ + 0.075, -0.1, o + 0.3, z + hs * 0.05, z + hs * 0.62, 'dan', lu=(0.5, 1.0), bottom=True)          # 살미 (쇠서 tip)
        hw = 0.48 + 0.14 * k; oc = proj * k / n
        mbox(hb, side, 0, e, s_ - hw, s_ + hw, oc - 0.07, oc + 0.07, z + hs * 0.35, z + hs * 0.95, 'dan', lu=(0.0, 1.0), bottom=True)       # 첨차


def beams_brackets(hb, xs, ys, z0, br, sides='fblr', open_sides=''):
    """창방 (+ 평방) at the column heads and the 공포 band above. br = dict(n, h, proj, dapo).  Returns the band top z."""
    d = hb.d
    R = (xs[0], ys[0], xs[-1], ys[-1]); n, h, proj = br.get('n', 2), br['h'], br['proj']
    cb = br.get('cb', 0.36)
    if d == 2:
        hb.box(R[0] - proj * 0.6, R[1] - proj * 0.6, z0 - cb, R[2] + proj * 0.6, R[3] + proj * 0.6, z0 + h, 'dan', lu=(0.3, 0.7)); return z0 + h
    for side in sides:
        cs = xs if side in 'fb' else ys; e = edge_of(side, R)
        for i in range(len(cs) - 1):
            mbox(hb, side, 0, e, cs[i], cs[i + 1], -0.13, 0.13, z0 - cb, z0, 'dan', lu=(0.0, 1.0), bottom=True)                # 창방
        ext = proj + 0.12
        if br.get('dapo', False): mbox(hb, side, 0, e, cs[0] - 0.25, cs[-1] + 0.25, -0.22, 0.22, z0, z0 + 0.14, 'dan', lu=(0.3, 0.7), bottom=True)   # 평방
        zb = z0 + (0.14 if br.get('dapo') else 0.0)
        if side not in open_sides: mquad(hb, side, 0, e, cs[0], cs[-1], -0.02, zb, z0 + h, 'paper')                                   # 포벽
        if d == 0:
            pos = []
            for i in range(len(cs) - 1):
                w = cs[i + 1] - cs[i]; m = max(1, int(round(w / br.get('pitch', 1.3)))) if br.get('dapo') else 1
                pos += [cs[i] + w * k / m for k in range(m)]
            pos.append(cs[-1])
            for p in pos: cluster(hb, side, R, p, zb, h - (zb - z0) - 0.02, proj, n)
        else:
            hh = (h - (zb - z0)) / 2
            mbox(hb, side, 0, e, cs[0] - proj * 0.5, cs[-1] + proj * 0.5, -0.05, proj * 0.5, zb, zb + hh, 'dan', lu=(0.3, 0.7), bottom=True)
            mbox(hb, side, 0, e, cs[0] - proj, cs[-1] + proj, -0.05, proj, zb + hh, z0 + h, 'dan', lu=(0.3, 0.7), bottom=True)
        if d == 0:   # 출목도리 + 장여
            mbox(hb, side, 0, e, cs[0] - ext, cs[-1] + ext, proj - 0.1, proj + 0.1, z0 + h - 0.3, z0 + h, 'dan', lu=(0.0, 1.0), bottom=True)
    return z0 + h


def panel(hb, side, R, s0, s1, zb, zt, kind, inset=0.06):
    """one bay between two columns."""
    d = hb.d; e = edge_of(side, R); w = s1 - s0
    if kind in ('open', None) or w < 0.3: return
    Q = lambda a, b, z0, z1, mat, uv=None, o=-inset: mquad(hb, side, 0, e, a, b, o, z0, z1, mat, uv)
    FULL = [(0, 0), (1, 0), (1, 1), (0, 1)]
    def frame(a, b, z0, z1, mat='wood', t=0.07, o1=0.05): mbox(hb, side, 0, e, a, b, -inset - 0.02, -inset + o1, z0, z1, mat)
    if kind == 'wall': Q(s0, s1, zb, zt, 'paper'); return
    if kind == 'plaster': Q(s0, s1, zb, zt, 'plaster'); return
    if kind == 'brick': Q(s0, s1, zb, zt, 'brick'); return
    if kind == 'board':   # 판벽 + 광창 strip
        Q(s0, s1, zb, zt, 'wood')
        if d == 0:
            frame(s0, s1, zb, zb + 0.1); frame(s0, s1, zt - 0.1, zt)
            m = max(1, int(round(w / 1.5)))
            for k in range(1, m): frame(s0 + w * k / m - 0.04, s0 + w * k / m + 0.04, zb, zt)
        return
    if kind == 'panmun':  # heavy board doors (2 leaves) under a board transom
        zt2 = min(zt, zb + 3.2)
        Q(s0, s1, zb, zt, 'wood')
        if d == 0:
            frame(s0, s1, zt2 - 0.08, zt2 + 0.08, o1=0.08); frame(s0 + w / 2 - 0.05, s0 + w / 2 + 0.05, zb, zt2); frame(s0, s1, zb, zb + 0.14, o1=0.1)
            for k in (0.25, 0.75): frame(s0 + w * k - 0.03, s0 + w * k + 0.03, zb, zt2, o1=0.03)
        return
    sill = 0.0; trans = 0.0
    if kind == 'window': sill = min(0.75, (zt - zb) * 0.3)
    if kind == 'band': sill = 0.0
    if kind in ('door', 'window') and (zt - zb) > 3.3: trans = min(0.9, (zt - zb) * 0.2)
    if sill > 0:      # 머름
        Q(s0, s1, zb, zb + sill, 'wood')
        if d == 0: frame(s0, s1, zb + sill - 0.09, zb + sill + 0.03, o1=0.09); frame(s0, s1, zb, zb + 0.1)
    z0, z1 = zb + sill, zt - trans
    n = max(1, int(round(w / (1.25 if kind != 'band' else 1.0))))
    if kind == 'door' and n % 2: n += 1
    for k in range(n):
        a, b = s0 + w * k / n, s0 + w * (k + 1) / n
        uvv = FULL if kind != 'band' else [(0, 0.3), (1, 0.3), (1, 0.7), (0, 0.7)]
        if kind == 'window': uvv = [(0, 0.26), (1, 0.26), (1, 1), (0, 1)]
        Q(a, b, z0, z1, 'lattice', uvv)
        if d == 0 and k > 0: frame(a - 0.035, a + 0.035, z0, zt)
    if trans > 0:
        m = max(1, int(round(w / 1.25)))
        for k in range(m): Q(s0 + w * k / m, s0 + w * (k + 1) / m, z1, zt, 'lattice', [(0, 0.3), (1, 0.3), (1, 0.62), (0, 0.62)])
        if d == 0: frame(s0, s1, z1 - 0.05, z1 + 0.05, o1=0.07)
    if d == 0: frame(s0, s1, zt - 0.08, zt, o1=0.06)


def walls(hb, xs, ys, zb, zt, spec, r=0.28):
    """spec: {side: kind | [kind per bay]}"""
    d = hb.d; R = (xs[0], ys[0], xs[-1], ys[-1])
    if d == 2:
        if any(v not in ('open', None) for v in (spec.values() if not isinstance(spec, str) else [spec])): hb.box(R[0], R[1], zb, R[2], R[3], zt, 'wood')
        return
    for side in 'fblr':
        k = spec.get(side, 'open'); cs = xs if side in 'fb' else ys
        for i in range(len(cs) - 1):
            kk = k[i] if isinstance(k, (list, tuple)) else k
            panel(hb, side, R, cs[i] + r * 0.6, cs[i + 1] - r * 0.6, zb, zt, kk)


# ------------------------------------------------------------------ roofs
def mesh_sector(hb, sec, o):
    d = hb.d; ha = sec['ha']; pt = sec['pt']; out = sec['out']; pitch = o.get('pitch', 0.32)
    if d == 0: step = pitch / 2; ts = o.get('ts', [0, 0.1, 0.3, 0.6, 1.0]); crest = o.get('crest', 0.075)
    elif d == 1: step = max(0.8, ha / 12); ts = [0, 0.15, 0.5, 1.0]; crest = 0.0
    else: step = max(ha / 2.01, 0.5); ts = [0, 0.3, 1.0]; crest = 0.0
    vs = 1.0 / 0.33
    for a0, a1, dfn in sec['runs']:
        brk = sorted({round(b, 3) for b in sec.get('breaks', []) if a0 + 0.03 < b < a1 - 0.03})
        k0 = int(math.ceil((a0 + 0.02) / step)); k1 = int(math.floor((a1 - 0.02) / step))
        cols = [(a0, False)] + [(k * step, d == 0 and k % 2 != 0) for k in range(k0, k1 + 1) if a0 + 0.02 < k * step < a1 - 0.02 and all(abs(k * step - b) > 0.03 for b in brk)] + [(b, False) for b in brk] + [(a1, False)]
        cols.sort()
        G = []
        for a, cr in cols:
            de = max(0.0, dfn(a)); col = []
            for t in ts:
                p = pt(a, de * t, 0.0)
                col.append((p[0], p[1], p[2] + (crest if cr else 0.0)))
            G.append((a, de, col))
        for i in range(len(G) - 1):
            a, de, c0 = G[i]; b, db, c1 = G[i + 1]
            if de < 1e-6 and db < 1e-6: continue
            for j in range(len(ts) - 1):
                uv = [(a / pitch, de * ts[j] * vs), (b / pitch, db * ts[j] * vs), (b / pitch, db * ts[j + 1] * vs), (a / pitch, de * ts[j + 1] * vs)]
                hb.face([c0[j], c1[j], c1[j + 1], c0[j + 1]], 'tile', n=(0, 0, 1), uv=uv)
        if d == 0:
            for i in range(1, len(G) - 1):
                if cols[i][1]: hb.face([G[i - 1][2][0], G[i + 1][2][0], G[i][2][0]], 'tile', n=out(G[i][0]))
    # 겹처마 underside
    dend = sec['dend']; t1, t2, db_, dov = o['t1'], o['t2'], o['db'], o['ov']; zw = o.get('zwall')
    n = max(3, int(2 * ha / (1.3 if d < 2 else ha)))
    As = [-ha + 2 * ha * i / n for i in range(n + 1)]
    for b in sec.get('ebreaks', []):
        if all(abs(b - a) > 0.05 for a in As): As.append(b)
    As.sort()
    D = lambda a, dd: min(dd, max(0.0, dend(a)))
    for i in range(len(As) - 1):
        a, b = As[i], As[i + 1]; nrm = out((a + b) / 2); dn = (0, 0, -1)
        if D(a, dov) < 1e-6 and D(b, dov) < 1e-6: continue
        hb.face([pt(a, 0, 0), pt(b, 0, 0), pt(b, 0, t1), pt(a, 0, t1)], 'tile', n=nrm)
        if d == 2:
            hb.face([pt(a, 0, t1), pt(b, 0, t1), pt(b, D(b, dov), t2), pt(a, D(a, dov), t2)], 'green', n=dn)
        else:
            hb.face([pt(a, 0, t1), pt(b, 0, t1), pt(b, D(b, db_), t1), pt(a, D(a, db_), t1)], 'green', n=dn)
            hb.face([pt(a, D(a, db_), t1), pt(b, D(b, db_), t1), pt(b, D(b, db_), t2), pt(a, D(a, db_), t2)], 'green', n=nrm)
            hb.face([pt(a, D(a, db_), t2), pt(b, D(b, db_), t2), pt(b, D(b, dov), t2), pt(a, D(a, dov), t2)], 'green', n=dn)
        if zw is not None:
            pa = pt(a, D(a, dov), t2); pb = pt(b, D(b, dov), t2)
            if min(pa[2], pb[2]) > zw + 0.03: hb.face([pa, pb, (pb[0], pb[1], zw), (pa[0], pa[1], zw)], 'dan', n=nrm, uv=GREEN_UV)
    if d == 0 and o.get('rafters', True):
        rp = o.get('rafter_pitch', 0.45); k = int(ha / rp)
        for i in range(-k, k + 1):
            a = i * rp + (rp / 2 if o.get('rafter_odd') else 0.0); de = dend(a)
            if abs(a) > ha - 0.1: continue
            if de > 0.35: hb.beam(pt(a, 0.05, t1), pt(a, min(db_ + 0.05, de), t1), 0.10, 0.09, 'green', down=True, caps=(True, False))
            if de > db_ + 0.25: hb.beam(pt(a, db_ - 0.12, t2), pt(a, min(dov, de), t2), 0.13, 0.12, 'green', down=True, caps=(True, False))


def strip(hb, pts, w, h, mat, sink=0.06):
    for i in range(len(pts) - 1):
        a, b = pts[i], pts[i + 1]
        hb.beam((a[0], a[1], a[2] - sink), (b[0], b[1], b[2] - sink), w, h + sink, mat, caps=(i == 0, i == len(pts) - 2), bottom=False)


class RectRoof:
    """L x D = eave to eave at mid span.  kind: hip (우진각) | hipgable (팔작) | gable (맞배).  z_wall = soffit height at the wall line
    (top of the bracket band), ov = eave overhang from the wall line.  cap = distance from the eave where a lower ring stops."""
    def __init__(s, L, D, z_wall, ov, kind='hip', s0=0.42, s1=0.92, rs=None, g_in=None, lift=0.6, bow=0.35, p=3.0, cap=None, cx=0.0, cy=0.0, **o):
        s.hx, s.hy = L / 2, D / 2; s.kind = kind; s.s0, s.s1 = s0, s1; s.run = s.hy
        s.rs = rs or s.hy; s.g_in = g_in if g_in is not None else s.hy * 0.45
        s.lift, s.bow, s.p = (lift, bow, p) if kind != 'gable' else (lift * 0.25, 0.0, p)
        s.cap = cap; s.cx, s.cy = cx, cy; s.ov = ov
        s.o = dict(t1=0.16, t2=0.34, db=min(ov * 0.36, 1.2), ov=ov, zwall=z_wall); s.o.update(o)
        s.ze = z_wall + s.o['t2'] - s.g(ov / s.hy)
        s.H = s.g(1.0)

    def g(s, t): return (s.s0 * t + (s.s1 - s.s0) * t * t / 2) * s.run

    def P(s, x, y, tn, off=0.0):
        c = min(1.0, abs(x) / s.hx) ** s.p * min(1.0, abs(y) / s.hy) ** s.p
        return (s.cx + x + s.bow * c * sgn(x), s.cy + y + s.bow * c * sgn(y), s.ze + s.g(tn) + s.lift * c - off)

    def top_z(s, d): return s.ze + s.g(d / s.hy)       # roof surface height at plan distance d from the long eave (mid span)

    def sectors(s):
        hx, hy, rs, k = s.hx, s.hy, s.rs, s.kind; xg = hx - s.g_in; dj = hy * s.g_in / rs
        capf = s.cap if s.cap is not None else 1e9; caps_ = capf * rs / hy
        out = []
        for side in range(4):
            if k == 'gable' and side >= 2: continue
            if side < 2:
                ha = hx
                def pt(a, d, off, side=side): return s.P(a, -hy + d, d / hy, off) if side == 0 else s.P(-a, hy - d, d / hy, off)
                hipf = lambda a: min(capf, hy * (hx - abs(a)) / rs)
                if k == 'hip': runs = [(-hx, hx, lambda a: min(hy, hipf(a)))]; dend = runs[0][2]; brk = [-(hx - rs), hx - rs]
                elif k == 'gable': runs = [(-hx, hx, lambda a: min(hy, capf))]; dend = runs[0][2]; brk = []
                else:
                    full = lambda a: min(hy, capf)
                    runs = [(-hx, -xg, hipf), (-xg, xg, full), (xg, hx, hipf)]; brk = []
                    dend = lambda a: full(a) if abs(a) <= xg else hipf(a)
                if s.cap is not None and k != 'gable': brk += [-(hx - rs * capf / hy), hx - rs * capf / hy]
                nv = (0, -1, 0) if side == 0 else (0, 1, 0)
            else:
                ha = hy
                def pt(a, d, off, side=side): return s.P(hx - d, a, d / rs, off) if side == 2 else s.P(-hx + d, -a, d / rs, off)
                lim = rs if k == 'hip' else s.g_in
                f = lambda a, lim=lim: min(lim, caps_, rs * (hy - abs(a)) / hy)
                runs = [(-hy, hy, f)]; dend = f
                e = hy - hy * min(lim, caps_) / rs
                brk = [-e, e] if e > 0.05 else [0.0]
                nv = (1, 0, 0) if side == 2 else (-1, 0, 0)
            out.append(dict(ha=ha, pt=pt, runs=runs, dend=dend, breaks=brk, ebreaks=brk, out=lambda a, nv=nv: nv))
        return out

    def build(s, hb, ridge=True, yangseong=False, gable_mat='paper', figures=5, rw=0.42, rh=0.42, yh=0.6, ends=True):
        d = hb.d; hx, hy, rs, k = s.hx, s.hy, s.rs, s.kind
        for sec in s.sectors(): mesh_sector(hb, sec, s.o)
        xg = hx - s.g_in; dj = hy * s.g_in / rs; capped = s.cap is not None
        rm = 'plaster' if yangseong else 'tile'
        nseg = 8 if d == 0 else (4 if d == 1 else 2)
        # 추녀마루 (hips)
        if k != 'gable':
            a0 = xg if k == 'hipgable' else hx - rs
            if capped: a0 = max(a0, hx - rs * s.cap / hy)
            for sx in (-1, 1):
                for sy in (-1, 1):
                    pts = []
                    for i in range(nseg + 1):
                        a = a0 + (hx - a0) * i / nseg; dd = hy * (hx - a) / rs
                        pts.append(s.P(sx * a, sy * (hy - dd), dd / hy))
                    strip(hb, pts, rw, rh, rm)
                    if d < 2:
                        e = pts[-1]; q = pts[-2]; L = math.hypot(e[0] - q[0], e[1] - q[1]) or 1.0
                        ux, uy = (e[0] - q[0]) / L, (e[1] - q[1]) / L
                        hb.beam((e[0] - ux * 0.05, e[1] - uy * 0.05, e[2] + rh * 0.5), (e[0] + ux * 0.4, e[1] + uy * 0.4, e[2] + rh * 0.9), rw * 0.8, rh * 0.8, 'tile')     # 용두 / 토수
                    if d == 0 and figures:
                        for f in range(figures):
                            t = 0.60 + 0.34 * f / max(1, figures - 1); i = min(nseg - 1, int(t * nseg)); u = t * nseg - i
                            x = pts[i][0] + (pts[i + 1][0] - pts[i][0]) * u; y = pts[i][1] + (pts[i + 1][1] - pts[i][1]) * u; z = pts[i][2] + (pts[i + 1][2] - pts[i][2]) * u + rh
                            hb.cyl(x, y, z - 0.03, z + 0.3, 0.09, 0.05, 'tile', n=5)
        if capped: return
        zt = s.ze + s.H
        # 내림마루 + 합각 / 박공
        if k == 'hipgable' or (k == 'gable' and ends):
            xe = xg if k == 'hipgable' else hx; ylim = hy - dj if k == 'hipgable' else hy
            for sx in (-1, 1):
                for sy in (-1, 1):
                    pts = [s.P(sx * xe, sy * ylim * i / nseg, 1 - (ylim * i / nseg) / hy) for i in range(nseg + 1)]
                    pts = [(p[0] - sx * rw * 0.5, p[1], p[2]) for p in pts]
                    strip(hb, pts, rw, rh, rm)
                    if d < 2:      # 박공 board under the roof edge
                        bp = [(sx * xe + s.cx, p[1], p[2] - 0.5) for p in pts]
                        for i in range(len(bp) - 1): hb.beam(bp[i], bp[i + 1], 0.1, 0.45, 'wood', caps=(False, False))
                # gable wall, slightly recessed
                xi = sx * (xe - (0.35 if k == 'hipgable' else s.o.get('gable_out', 1.0) * 0.5))
                if k == 'hipgable':
                    zb = s.ze + s.g(s.g_in / rs) - 0.05; yl = ylim
                else:
                    zb = s.o['zwall'] + 0.25; yl = hy - s.ov
                m = 8 if d < 2 else 2
                for i in range(m):
                    ya, yb = -yl + 2 * yl * i / m, -yl + 2 * yl * (i + 1) / m
                    za = max(zb + 0.02, s.ze + s.g(1 - abs(ya) / hy) - 0.2); zb2 = max(zb + 0.02, s.ze + s.g(1 - abs(yb) / hy) - 0.2)
                    hb.face([(s.cx + xi, s.cy + ya, zb), (s.cx + xi, s.cy + yb, zb), (s.cx + xi, s.cy + yb, zb2), (s.cx + xi, s.cy + ya, za)], gable_mat if k == 'hipgable' else 'wood', n=(sx, 0, 0))
                for i in range(m):   # underside of the gable overhang
                    ya, yb = -hy + 2 * hy * i / m, -hy + 2 * hy * (i + 1) / m
                    if k == 'hipgable': ya, yb = -yl + 2 * yl * i / m, -yl + 2 * yl * (i + 1) / m
                    q = [s.P(sx * xe, ya, 1 - abs(ya) / hy, 0.2), s.P(sx * xe, yb, 1 - abs(yb) / hy, 0.2), s.P(xi, yb, 1 - abs(yb) / hy, 0.2), s.P(xi, ya, 1 - abs(ya) / hy, 0.2)]
                    hb.face(q, 'green', n=(0, 0, -1))
                    hb.face([s.P(sx * xe, ya, 1 - abs(ya) / hy, 0.0), s.P(sx * xe, yb, 1 - abs(yb) / hy, 0.0), q[1], q[0]], 'tile', n=(sx, 0, 0))
                if k == 'hipgable':   # shelf from the side slope top to the wall + soffit of the overhang
                    hb.face([(s.cx + sx * xe, s.cy - yl, zb + 0.05), (s.cx + sx * xe, s.cy + yl, zb + 0.05), (s.cx + xi, s.cy + yl, zb + 0.05), (s.cx + xi, s.cy - yl, zb + 0.05)], 'tile', n=(0, 0, 1))
        # 용마루
        if ridge is not False:
            xr = (xg if k == 'hipgable' else hx if k == 'gable' else hx - rs)
            if xr > 0.2:
                er = s.o.get('ridge_rise', 0.22)
                pts = [(s.cx - xr + 2 * xr * i / nseg, s.cy, zt + er * abs(-1 + 2 * i / nseg) ** 3) for i in range(nseg + 1)]
                if ridge == 'low': strip(hb, pts, rw * 2.0, 0.14, 'tile')            # 무량각: 곡와 saddle, no ridge wall
                else:
                    strip(hb, pts, rw * 1.1, yh, rm)
                    if d < 2 and (ends or k != 'gable'):
                        for sx in (-1, 1):   # 취두
                            x = s.cx + sx * xr; z = pts[0][2]
                            hb.box(x - 0.32, s.cy - rw * 0.6, z, x + 0.32, s.cy + rw * 0.6, z + yh + 0.45, 'tile')
                            hb.box(x + sx * 0.05 - 0.2, s.cy - rw * 0.45, z + yh + 0.45, x + sx * 0.05 + 0.2, s.cy + rw * 0.45, z + yh + 0.75, 'tile')
            elif d < 2:   # pyramid: 절병통
                finial(hb, s.cx, s.cy, zt - 0.1)
        return zt


def finial(hb, x, y, z, sc=1.0):
    hb.cyl(x, y, z, z + 0.25 * sc, 0.42 * sc, 0.34 * sc, 'tile', n=8)
    hb.cyl(x, y, z + 0.25 * sc, z + 0.7 * sc, 0.22 * sc, 0.3 * sc, 'tile', n=8)
    hb.cyl(x, y, z + 0.7 * sc, z + 1.15 * sc, 0.3 * sc, 0.04 * sc, 'tile', n=8)


class PolyRoof:
    """n-sided pavilion roof (육모 / 팔모 / 사모).  R = eave circumradius, z_wall / ov as RectRoof."""
    def __init__(s, n, R, z_wall, ov, s0=0.5, s1=1.1, lift=0.45, bow=0.25, p=3.0, cx=0.0, cy=0.0, rot=0.0, **o):
        s.n = n; s.R = R; s.apo = R * math.cos(math.pi / n); s.ha = R * math.sin(math.pi / n); s.s0, s.s1 = s0, s1
        s.lift, s.bow, s.p = lift, bow, p; s.cx, s.cy = cx, cy; s.rot = rot; s.ov = ov
        s.o = dict(t1=0.14, t2=0.3, db=min(ov * 0.36, 1.0), ov=ov, zwall=z_wall); s.o.update(o)
        s.ze = z_wall + s.o['t2'] - s.g(ov / s.apo); s.H = s.g(1.0)

    def g(s, t): return (s.s0 * t + (s.s1 - s.s0) * t * t / 2) * s.apo

    def build(s, hb, fin=1.0, rw=0.3, rh=0.3):
        d = hb.d; n = s.n; tn_ = math.tan(math.pi / n)
        for i in range(n):
            th = s.rot + 2 * math.pi * (i + 0.5) / n; cs, sn = math.cos(th), math.sin(th)
            def pt(a, dd, off, cs=cs, sn=sn, th=th):
                c = min(1.0, abs(a) / s.ha) ** s.p * max(0.0, 1 - dd / s.apo) ** s.p
                va = th + sgn(a) * math.pi / n
                x = (s.apo - dd) * cs - a * sn + s.bow * c * math.cos(va); y = (s.apo - dd) * sn + a * cs + s.bow * c * math.sin(va)
                return (s.cx + x, s.cy + y, s.ze + s.g(dd / s.apo) + s.lift * c - off)
            f = lambda a: s.apo - abs(a) / tn_
            mesh_sector(hb, dict(ha=s.ha, pt=pt, runs=[(-s.ha, s.ha, f)], dend=f, breaks=[0.0], ebreaks=[0.0], out=lambda a, cs=cs, sn=sn: (cs, sn, 0)), s.o)
            va = s.rot + 2 * math.pi * i / n; m = 6 if d == 0 else 3
            pts = []
            for k in range(m + 1):
                rr = s.R * k / m; c = (k / m) ** (2 * s.p)
                pts.append((s.cx + (rr + s.bow * c) * math.cos(va), s.cy + (rr + s.bow * c) * math.sin(va), s.ze + s.g(1 - k / m) + s.lift * c))
            strip(hb, pts, rw, rh, 'tile')
        if d < 2 and fin: finial(hb, s.cx, s.cy, s.ze + s.H - 0.15, fin)
        return s.ze + s.H


# ------------------------------------------------------------------ stone gate base
def gate_base(hb, W, Dp, H, arches, batter=0.35, parapet=1.3, par_mat='brick', par_sides='fblr'):
    """stone base W x Dp x H with arched passages [(xc, width, height)] through y; battered front/back; 여장 on top."""
    d = hb.d; hw, hd = W / 2, Dp / 2
    def yb(z, sg): return sg * (hd + batter * (1 - z / H))
    xs = {-hw, hw}
    prof = {}
    for xc, w, h in arches:
        r = w / 2; n = 10 if d == 0 else (6 if d == 1 else 2)
        for i in range(n + 1):
            x = xc - r + w * i / n; xs.add(round(x, 4))
        prof[(xc - r, xc + r)] = (xc, r, h)
    xs = sorted(xs)
    def zo(x):
        for (a, b), (xc, r, h) in prof.items():
            if a - 1e-6 <= x <= b + 1e-6:
                return (h - r) + math.sqrt(max(0.0, r * r - (x - xc) ** 2))
        return 0.0
    def inside(x):
        return any(a + 1e-6 < x < b - 1e-6 for (a, b) in prof)
    for i in range(len(xs) - 1):
        a, b = xs[i], xs[i + 1]; op = inside((a + b) / 2)
        za, zb_ = (zo(a), zo(b)) if op else (0.0, 0.0)
        for sg in (-1, 1):
            hb.face([(a, yb(za, sg), za), (b, yb(zb_, sg), zb_), (b, yb(H, sg), H), (a, yb(H, sg), H)], 'ashlar', n=(0, sg, 0.2))
        if op:
            xm = (a + b) / 2
            hb.face([(a, yb(za, -1), za), (b, yb(zb_, -1), zb_), (b, yb(zb_, 1), zb_), (a, yb(za, 1), za)], 'ashlar', n=(0, 0, -1))
    for (a, b), (xc, r, h) in prof.items():
        for x, sg in ((a, 1), (b, -1)):
            hb.face([(x, yb(0, -1), 0), (x, yb(0, 1), 0), (x, yb(h - r, 1), h - r), (x, yb(h - r, -1), h - r)], 'ashlar', n=(sg, 0, 0))
        if d == 0:   # 홍예석 ring as a slightly proud band
            for sg in (-1, 1):
                n = 10
                for i in range(n):
                    t0, t1 = math.pi * i / n, math.pi * (i + 1) / n
                    p = lambda t, rr: (xc - rr * math.cos(t), (h - r) + rr * math.sin(t))
                    q = [p(t0, r), p(t1, r), p(t1, r + 0.45), p(t0, r + 0.45)]
                    hb.face([(x, yb(z, sg) + sg * 0.05, z) for x, z in q], 'stone', n=(0, sg, 0))
    for sg in (-1, 1): hb.face([(sg * hw, yb(0, -1), 0), (sg * hw, yb(0, 1), 0), (sg * hw, hd, H), (sg * hw, -hd, H)], 'ashlar', n=(sg, 0, 0))
    hb.face([(-hw, -hd, H), (hw, -hd, H), (hw, hd, H), (-hw, hd, H)], 'stone', n=(0, 0, 1))
    if parapet and d < 2:
        t = 0.55
        for side, a, b, e in (('f', -hw, hw, -hd), ('b', -hw, hw, hd), ('l', -hd, hd, -hw), ('r', -hd, hd, hw)):
            if side not in par_sides: continue
            L = b - a; n = max(1, int(round(L / 3.2))) if d == 0 else 1
            for i in range(n):
                s0 = a + L * i / n + (0.08 if d == 0 else 0); s1 = a + L * (i + 1) / n - (0.08 if d == 0 else 0)
                mbox(hb, side, 0, e, s0, s1, -t, 0.0, H, H + parapet - 0.12, par_mat)
                if d == 0: mbox(hb, side, 0, e, s0 - 0.03, s1 + 0.03, -t - 0.06, 0.06, H + parapet - 0.12, H + parapet, 'stone')
    elif parapet: hb.box(-hw, -hd, H, hw, hd, H + parapet * 0.5, 'stone')


# ------------------------------------------------------------------ composite: one or two storeys on a bay grid
def two_storey(hb, xs, ys, z0, lo, up, roof_kind='hip', inset=1.2, ov=3.0, ov2=None, cap_roof=None, top_roof=None, yangseong=False, xs2=None, ys2=None, figures=5):
    """lo / up = dict(col_h, r, walls, br).  Lower ring roof + upper storey + main roof.  Returns top z."""
    d = hb.d
    columns(hb, xs, ys, z0, lo['col_h'], lo.get('r', 0.3), base=lo.get('base', True))
    walls(hb, xs, ys, z0 + (0.12 if lo.get('base', True) else 0), z0 + lo['col_h'] - lo['br'].get('cb', 0.36), lo['walls'], lo.get('r', 0.3))
    zw = beams_brackets(hb, xs, ys, z0 + lo['col_h'], lo['br'])
    L, D = xs[-1] - xs[0], ys[-1] - ys[0]
    cr = dict(s0=0.40, s1=0.62, lift=0.55, bow=0.3); cr.update(cap_roof or {})
    r1 = RectRoof(L + 2 * ov, D + 2 * ov, zw, ov, kind='hip', cap=ov + inset + 0.12, **cr)
    r1.build(hb, yangseong=yangseong, figures=figures)
    zu = r1.top_z(ov + inset) - 0.15
    xs2 = xs2 or [xs[0] + inset] + list(xs[1:-1]) + [xs[-1] - inset]; ys2 = ys2 or [ys[0] + inset] + list(ys[1:-1]) + [ys[-1] - inset]
    if d < 2:
        columns(hb, xs2, ys2, zu, up['col_h'], up.get('r', 0.26), base=False)
        walls(hb, xs2, ys2, zu, zu + up['col_h'] - up['br'].get('cb', 0.3), up['walls'], up.get('r', 0.26))
    else: hb.box(xs2[0], ys2[0], zu, xs2[-1], ys2[-1], zu + up['col_h'], 'wood')
    zw2 = beams_brackets(hb, xs2, ys2, zu + up['col_h'], up['br'])
    ov2 = ov2 or ov
    tr = dict(s0=0.42, s1=0.95, lift=0.6, bow=0.32); tr.update(top_roof or {})
    r2 = RectRoof(xs2[-1] - xs2[0] + 2 * ov2, ys2[-1] - ys2[0] + 2 * ov2, zw2, ov2, kind=roof_kind, **tr)
    return r2.build(hb, yangseong=yangseong, figures=figures)


# ------------------------------------------------------------------ export, manifest entry, sheet
def previews(o):
    out = []
    for m in o.data.materials:
        k = next((kk for kk in KEYS if MATS[kk][0] == m.name), None)
        if k is None: out.append({'tex': None, 'color': np.array((0.6, 0.6, 0.6), dtype=np.float32), 'mean': np.array((0.6, 0.6, 0.6))}); continue
        name, alb, nrm, tint, _ = MATS[k]
        te = PK.tex_entry(str(OUT / alb)) if alb else None
        tint = np.array(tint, dtype=np.float32)
        out.append({'tex': te['small'] if te else None, 'color': tint, 'mean': (te['mean'] * tint) if te else tint})
    return out


def human(rng, at):
    """1.8 m figure as sample points (not exported)."""
    n = 6000
    P = np.empty((n, 3), dtype=np.float32)
    P[:, 0] = at[0] + (rng.random(n) - 0.5) * 0.46; P[:, 1] = at[1] + (rng.random(n) - 0.5) * 0.25; P[:, 2] = rng.random(n) * 1.8
    head = P[:, 2] > 1.52; P[head, 0] = at[0] + (P[head, 0] - at[0]) * 0.5
    C = np.tile(np.array((0.85, 0.1, 0.55), dtype=np.float32), (n, 1)); N = np.tile(np.array((0, 0, 1), dtype=np.float32), (n, 1))
    return P, C, N


def export(name, fn, reference, replaces='', extra=None, human_at=None):
    """fn(hb) builds the model at hb.d.  Writes hero/<name>_LOD{0,1,2}.fbx, <name>.json, _sheets/hero/<name>.png."""
    t0 = time.time()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    odir = OUT / CLS; odir.mkdir(parents=True, exist_ok=True); sdir = OUT / '_sheets' / CLS; sdir.mkdir(parents=True, exist_ok=True)
    lods = []
    for d in range(3):
        hb = HB(d); fn(hb); bpy.ops.object.select_all(action='DESELECT'); lods.append(hb.build('%s_LOD%d' % (name, d)))
    mn, mx = PK.bounds(lods[0].data)
    corners = np.array([[x, y, z] for x in (mn[0], mx[0]) for y in (mn[1], mx[1]) for z in (mn[2], mx[2])], dtype=np.float64)
    umn, umx = PK.unity_box(mn, mx)
    mats = []
    for m in lods[0].data.materials:
        k = next(kk for kk in KEYS if MATS[kk][0] == m.name); _, alb, nrm, tint, sc = MATS[k]
        mats.append({'name': m.name, 'albedo': alb, 'normal': nrm, 'tint': [float(x) for x in tint], 'alphaclip': False, 'slot': k,
                     'uv': 'explicit 0..1' if k in ('dan', 'lattice') else ('u = tile rows (1 = one 수키와 pitch), v = up the slope' if k == 'tile' else 'box projection, 1 UV = %.1f m' % sc)})
    entry = {'name': name, 'class': CLS, 'kind': 'generated', 'source': 'Tools/Blender/HeroHanok308/models.py', 'reference': reference, 'replaces': replaces,
             'front': 'Unity -Z (door side); long axis = X', 'pivot': 'ground centre (XZ = plan centre of the column grid / body, Y 0 = base)', 'source_tris': 0,
             'bounds_unity': {'min': umn, 'max': umx, 'size': [round(umx[i] - umn[i], 3) for i in range(3)]}, 'below_ground_m': 0.0, 'materials': mats,
             'lod1_materials': [m.name for m in lods[1].data.materials], 'lods': []}
    if extra: entry.update(extra)
    prevs = [previews(lo) for lo in lods]
    pal_rel, pal_prev = PK.palette_lod2(lods[2], prevs[2], name, CLS)
    entry['lod2_palette'] = pal_rel; prevs[2] = pal_prev
    rng = np.random.default_rng(1)
    size = mx - mn
    hat = human_at or (float(-mx[0]) + 0.0, float(-mx[1]) - 0.0)       # work space: front = +y, model x = -x
    hp = human(rng, (float(mn[0]) + 0.6, float(mx[1]) - 0.3)) if human_at is None else human(rng, (-human_at[0], -human_at[1]))
    big = {}; small = []
    for i, lo in enumerate(lods):
        fbx = odir / ('%s_LOD%d.fbx' % (name, i))
        sha = PK.export_fbx(lo, fbx)
        zn, dg = PK.normal_check(lo.data)
        A = V.arrays(lo.data)
        px = float(max(size)) / (640.0 if i == 0 else 330.0)
        Pi, Ci, Ni = V.sample(A, px, prevs[i], seed=3 + i)
        Pi = np.concatenate([Pi, hp[0]]); Ci = np.concatenate([Ci, hp[1]]); Ni = np.concatenate([Ni, hp[2]])
        if i == 0:
            for v in BIGV: big[v] = V.splat(Pi, Ci, Ni, V.frame(corners, v, (510, 380)))[0]
        small.append(V.splat(Pi, Ci, Ni, V.frame(corners, 'low' if i == 0 else 'q34', (510 if i == 0 else 255, 300)))[0])
        entry['lods'].append({'lod': i, 'fbx': '%s/%s' % (CLS, fbx.name), 'tris': int(len(A['tri'])), 'verts': len(lo.data.vertices), 'materials': len(lo.data.materials),
                              'iou': {}, 'zero_normals': zn, 'degenerate_tris': dg, 'bytes': fbx.stat().st_size, 'sha256': sha})
    img = np.concatenate([np.concatenate([big['fromNegZ'], big['fromPosX']], 1), np.concatenate([big['q34'], big['top']], 1), np.concatenate(small, 1)], 0)
    V.save_png(str(sdir / (name + '.png')), img)
    entry['sheet'] = '_sheets/%s/%s.png' % (CLS, name); entry['secs'] = round(time.time() - t0, 1); entry['peak_mb'] = PK.peak_mb()
    (odir / (name + '.json')).write_text(json.dumps(entry, ensure_ascii=False, indent=1), encoding='utf-8')
    PK.log('HERO', name, 'tris', [l['tris'] for l in entry['lods']], 'size', entry['bounds_unity']['size'], 'min', umn, 'zn/dg', [(l['zero_normals'], l['degenerate_tris']) for l in entry['lods']],
           'mats', [l['materials'] for l in entry['lods']], 'secs', entry['secs'], 'peak_mb', entry['peak_mb'])
    return entry
