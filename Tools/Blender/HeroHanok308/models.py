"""#308 HeroHanok: the hero models (근정전, 경회루, 광화문 문루, ...).  One Blender process per model:
  blender -b --factory-startup -P Tools/Blender/HeroHanok308/models.py -- <Name> [<Name> ...]
  (normal use: python Tools/Blender/HeroHanok308/run.py [names])
Dimension sources + EST marks: Tools/Unity/Stage308_hanyang/hero_models.md.  1척 = 0.3081 m.
"""
import sys, math
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
import hanok as H
from hanok import grid, platform, stairs, rail, rail_rect, columns, walls, beams_brackets, RectRoof, PolyRoof, gate_base, two_storey, mbox
CH = 0.3081
ALL = lambda k: {'f': k, 'b': k, 'l': k, 'r': k}


def sub(hb, fn, rot=0.0, tx=0.0, ty=0.0, tz=0.0):
    """build fn into hb rotated by rot (rad, about z) and translated."""
    t = H.HB(hb.d); fn(t); c, s_ = math.cos(rot), math.sin(rot); b = len(hb.v)
    hb.v.extend([(x * c - y * s_ + tx, x * s_ + y * c + ty, z + tz) for x, y, z in t.v]); hb.f.extend([tuple(i + b for i in f) for f in t.f]); hb.m.extend(t.m); hb.uv.extend(t.uv)


def hall1(hb, xs, ys, z0, col_h, r, wspec, br, kind, ov, roof=None, which='perim', base=True, ceiling=False, yang=False, ridge=True, gable_out=0.9, figures=3, ends=True, br_sides='fblr'):
    columns(hb, xs, ys, z0, col_h, r, which=which, base=base)
    walls(hb, xs, ys, z0 + (0.12 if base else 0), z0 + col_h - br.get('cb', 0.36), wspec, r)
    op = ''.join(k for k in 'fblr' if wspec.get(k, 'open') == 'open')
    zw = beams_brackets(hb, xs, ys, z0 + col_h, br, sides=br_sides, open_sides=op)
    if ceiling and hb.d < 2: hb.face([(xs[0], ys[0], zw - 0.04), (xs[-1], ys[0], zw - 0.04), (xs[-1], ys[-1], zw - 0.04), (xs[0], ys[-1], zw - 0.04)], 'green', n=(0, 0, -1))
    L = xs[-1] - xs[0] + 2 * (ov if kind != 'gable' else gable_out); D = ys[-1] - ys[0] + 2 * ov
    rr = RectRoof(L, D, zw, ov, kind=kind, cx=(xs[0] + xs[-1]) / 2, cy=(ys[0] + ys[-1]) / 2, gable_out=gable_out, **(roof or {}))
    return rr.build(hb, ridge=ridge, yangseong=yang, figures=figures, ends=ends)


def door_line(hb, xs, y, zb, zt, kind='panmun', r=0.3, open_bays=()):
    """door leaves on an interior column line (gates, 종묘 퇴칸).  open_bays: leaves swung inward (+y), passage clear."""
    if hb.d == 2: return
    for i in range(len(xs) - 1):
        a, b = xs[i] + r * 0.6, xs[i + 1] - r * 0.6
        if i in open_bays:
            zd = min(zt, zb + 3.25); w = (b - a) / 2 * 0.96
            for x in (a, b): hb.box(x - 0.045, y, zb, x + 0.045, y + w, zd, 'wood')
            if zt - zd > 0.2:
                for sd in 'fb': H.panel(hb, sd, (xs[0], y, xs[-1], y), a, b, zd, zt, 'board', inset=0.0)
            continue
        for sd in 'fb': H.panel(hb, sd, (xs[0], y, xs[-1], y), a, b, zb, zt, kind, inset=0.0)


# ---------------------------------------------------------------- 1. 근정전 + 월대
def geunjeongjeon(hb):
    xs = grid([17 * CH, 21 * CH, 22 * CH, 21 * CH, 17 * CH]); ys = grid([17 * CH, 11 * CH, 11 * CH, 11 * CH, 17 * CH])
    platform(hb, xs[0] - 1.7, ys[0] - 1.7, xs[-1] + 1.7, ys[-1] + 1.7, 0, 0.3)
    if hb.d < 2: stairs(hb, 'f', 0, ys[0] - 1.7, 0.3, 0, 6.0, run=0.5)
    lo = dict(col_h=16 * CH, r=0.34, walls=ALL('door'), br=dict(n=3, h=1.5, proj=1.05, dapo=True, pitch=1.3))
    up = dict(col_h=2.5, r=0.3, walls=ALL('band'), br=dict(n=3, h=1.4, proj=1.0, dapo=True, pitch=1.3))
    two_storey(hb, xs, ys, 0.3, lo, up, 'hipgable', inset=2.6, ov=3.8, ov2=3.8, yangseong=True, figures=7,
               cap_roof=dict(s0=0.32, s1=0.58, lift=0.8, bow=0.42), top_roof=dict(g_in=6.2, s0=0.36, s1=1.06, lift=0.9, bow=0.48))


def woldae_geunjeong(hb):
    # as-built (palace308.json): lower 43.0 x 39.0 centred 2.0 m south of the hall, upper 37.2 x 30.9 centred 1.05 m south, 1.36 m each
    t = 1.36
    L0 = (-21.5, -21.5, 21.5, 17.5); L1 = (-18.6, -16.5, 18.6, 14.4)
    platform(hb, *L0, 0, t); platform(hb, *L1, t, t)
    st = [('f', 0.0, 7.0), ('b', 0.0, 4.0), ('l', -4.0, 3.6), ('r', -4.0, 3.6)]
    for (R, z) in ((L0, 0.0), (L1, t)):
        for side, c, w in st: stairs(hb, side, c, H.edge_of(side, R), z + t, z, w, run=2.86)
        g = 0.35
        rail_rect(hb, R[0] + g, R[1] + g, R[2] - g, R[3] - g, z + t, gaps=[(s_, c, w + 0.9) for s_, c, w in st], h=1.05, span=2.4, post=0.24)


# ---------------------------------------------------------------- 2. 경회루
def gyeonghoeru(hb):
    xs = grid([4.6, 4.9, 4.9, 5.6, 4.9, 4.9, 4.6]); ys = grid([5.4, 5.8, 6.1, 5.8, 5.4]); d = hb.d
    platform(hb, -18.7, -15.8, 18.7, 15.8, 0, 0.9)
    zp, zf = 0.9, 5.6
    columns(hb, xs, ys, zp, zf - zp, r=0.42, which='all', mat='stone', base=False, taper=0.84, square=lambda i, j: i in (0, len(xs) - 1) or j in (0, len(ys) - 1))
    if d == 2: hb.box(xs[0], ys[0], zp, xs[-1], ys[-1], zf, 'stone')
    m = 0.95
    hb.box(xs[0] - m, ys[0] - m, zf, xs[-1] + m, ys[-1] + m, zf + 0.45, 'dan', top='floor', bottom=True, lu=(0.3, 0.7))
    z1 = zf + 0.45
    rail_rect(hb, xs[0] - m + 0.1, ys[0] - m + 0.1, xs[-1] + m - 0.1, ys[-1] + m - 0.1, z1, h=0.95, span=1.6, post=0.13, knob=False, mat='wood', mid=False)
    if d == 2: hb.box(xs[0], ys[0], z1, xs[-1], ys[-1], z1 + 4.3, 'wood')
    columns(hb, xs, ys, z1, 4.3, 0.3, which='inner', base=False)
    if d < 2:   # 내진: hanging lattice band on the second column ring
        R = (xs[1], ys[1], xs[-2], ys[-2])
        for sd in 'fblr':
            cs = xs[1:-1] if sd in 'fb' else ys[1:-1]
            for i in range(len(cs) - 1): H.panel(hb, sd, R, cs[i] + 0.2, cs[i + 1] - 0.2, z1 + 3.1, z1 + 4.3 - 0.36, 'band', inset=0.0)
    hall1(hb, xs, ys, z1, 4.3, 0.3, ALL('open'), dict(n=2, h=0.8, proj=0.42), 'hipgable', 3.1, roof=dict(g_in=8.6, s0=0.34, s1=0.90, lift=0.85, bow=0.48), base=False, ceiling=True, yang=True, figures=7)


# ---------------------------------------------------------------- 3 / 4 / 5. gate towers
def munru2(hb, xs, ys, z0=0.0, col1=3.0, col2=1.9, inset=1.0, ov=2.9, rs=None, n=2, bh=1.0, proj=0.7, lower='panmun', doors_mid=False, yang=True, figures=7, lower_sides=None):
    lw = ALL(lower)
    if lower_sides: lw['l'] = lw['r'] = lower_sides
    lo = dict(col_h=col1, r=0.3, base=False, walls=lw, br=dict(n=n, h=bh, proj=proj, dapo=True, pitch=1.25))
    up = dict(col_h=col2, r=0.26, walls=ALL('board'), br=dict(n=n, h=bh * 0.95, proj=proj, dapo=True, pitch=1.25, cb=0.3))
    if doors_mid:
        ym = ys[len(ys) // 2]
        columns(hb, xs, [ym], z0, col1, 0.3, which='all', base=False)
        door_line(hb, xs, ym, z0, z0 + col1 - 0.36, open_bays=(len(xs) // 2 - 1,))
        if hb.d < 2: hb.face([(xs[0], ys[0], z0 + col1 + 0.3), (xs[-1], ys[0], z0 + col1 + 0.3), (xs[-1], ys[-1], z0 + col1 + 0.3), (xs[0], ys[-1], z0 + col1 + 0.3)], 'green', n=(0, 0, -1))
        else:
            c = len(xs) // 2 - 1
            hb.box(xs[0], ym - 0.3, z0, xs[c], ym + 0.3, z0 + col1, 'wood'); hb.box(xs[c + 1], ym - 0.3, z0, xs[-1], ym + 0.3, z0 + col1, 'wood')
    tr = dict(s0=0.36, s1=1.0, lift=0.6, bow=0.32)
    if rs: tr['rs'] = rs
    return two_storey(hb, xs, ys, z0, lo, up, 'hip', inset=inset, ov=ov, yangseong=yang, figures=figures, cap_roof=dict(s0=0.32, s1=0.58, lift=0.55, bow=0.3), top_roof=tr)


GH_X = [25 * CH, 27 * CH, 25 * CH]; GH_Y = [10 * CH, 10 * CH]
def gwanghwamun_munru(hb, z0=0.0): munru2(hb, grid(GH_X), grid(GH_Y), z0, col1=3.1, col2=2.0, inset=1.0, ov=2.9, rs=4.3, n=2, bh=1.05, proj=0.75)


def gwanghwamun(hb):
    gate_base(hb, 40.0, 13.0, 7.0, [(-9.5, 4.5, 4.9), (0.0, 5.5, 5.4), (9.5, 4.5, 4.9)], batter=0.3, parapet=1.35)
    gwanghwamun_munru(hb, 7.0)


def heungnyemun(hb):
    xs = grid([16 * CH, 18 * CH, 16 * CH]); ys = grid([15 * CH, 15 * CH])
    munru2(hb, xs, ys, 0.0, col1=15 * CH, col2=2.0, inset=1.1, ov=2.7, rs=4.3, n=2, bh=1.1, proj=0.75, lower='open', doors_mid=True, lower_sides='plaster')


def sungnyemun_munru(hb): munru2(hb, grid([4.2, 4.6, 5.3, 4.6, 4.2]), grid([3.95, 3.95]), 0.0, col1=3.3, col2=2.1, inset=1.15, ov=3.2, rs=5.2, n=2, bh=1.05, proj=0.75, lower_sides='board')


def munru1(hb):
    xs = grid([2.9, 3.6, 2.9]); ys = grid([2.7, 2.7])
    hall1(hb, xs, ys, 0.0, 2.7, 0.24, {'f': 'panmun', 'b': 'panmun', 'l': 'board', 'r': 'board'}, dict(n=1, h=0.55, proj=0.32, cb=0.3), 'hip', 1.7,
          roof=dict(s0=0.36, s1=0.95, lift=0.4, bow=0.22, rs=2.9, pitch=0.3), base=False, figures=3)


# ---------------------------------------------------------------- 6. 향원정 · 십자각
def poly(n, R, rot=0.0): return [(R * math.cos(rot + 2 * math.pi * i / n), R * math.sin(rot + 2 * math.pi * i / n)) for i in range(n)]


def prism(hb, pts, z0, z1, mat, top=None, bottom=False):
    n = len(pts)
    for i in range(n):
        a, b = pts[i], pts[(i + 1) % n]
        hb.face([(a[0], a[1], z0), (b[0], b[1], z0), (b[0], b[1], z1), (a[0], a[1], z1)], mat, n=((a[0] + b[0]), (a[1] + b[1]), 0))
    hb.face([(p[0], p[1], z1) for p in pts], top or mat, n=(0, 0, 1))
    if bottom: hb.face([(p[0], p[1], z0) for p in pts], mat, n=(0, 0, -1))


def hyangwonjeong(hb):
    d = hb.d; n = 6; R = 2.6; zb = 0.7; f1 = 2.9; f2 = 2.7
    prism(hb, poly(n, R + 1.0), 0, zb - 0.15, 'ashlar'); prism(hb, poly(n, R + 1.08), zb - 0.15, zb, 'stone', bottom=True)
    V_ = poly(n, R); z2 = zb + f1; zt = z2 + f2
    if d == 2: prism(hb, V_, zb, zt + 0.5, 'wood')
    else:
        for (x, y) in V_: hb.cyl(x, y, zb, zt, 0.17, 0.16, 'wood', n=6, top=False)
        B = poly(n, R + 0.75); C = poly(n, R + 0.55)           # 2층 툇마루 + 계자난간, 1층 난간
        prism(hb, B, z2 - 0.22, z2, 'dan', top='floor', bottom=True)
        for i in range(n):
            j = (i + 1) % n; a, b = V_[i], V_[j]
            rail(hb, (B[i][0] * 0.97, B[i][1] * 0.97), (B[j][0] * 0.97, B[j][1] * 0.97), z2, h=0.8, span=1.1, post=0.09, knob=False, mat='wood', mid=False)
            if i != 4: rail(hb, C[i], C[j], zb, h=0.7, span=1.1, post=0.09, knob=False, mat='wood', mid=False)
            nx, ny = (a[0] + b[0]) / 2, (a[1] + b[1]) / 2; L = math.hypot(nx, ny); nx, ny = nx / L, ny / L
            ins = 0.04; t = 0.10
            p = (a[0] + (b[0] - a[0]) * t - nx * ins, a[1] + (b[1] - a[1]) * t - ny * ins); q = (a[0] + (b[0] - a[0]) * (1 - t) - nx * ins, a[1] + (b[1] - a[1]) * (1 - t) - ny * ins)
            for (za, zc) in ((zb, z2 - 0.25), (z2, zt - 0.3)):
                for k in range(2):
                    p0 = (p[0] + (q[0] - p[0]) * k / 2, p[1] + (q[1] - p[1]) * k / 2); p1 = (p[0] + (q[0] - p[0]) * (k + 1) / 2, p[1] + (q[1] - p[1]) * (k + 1) / 2)
                    hb.face([(p0[0], p0[1], za), (p1[0], p1[1], za), (p1[0], p1[1], zc), (p0[0], p0[1], zc)], 'lattice', n=(nx, ny, 0), uv=[(0, 0), (1, 0), (1, 1), (0, 1)])
            for z in (z2 - 0.25 - 0.3, zt - 0.3): hb.beam((a[0], a[1], z), (b[0], b[1], z), 0.16, 0.3, 'dan', caps=(False, False), lu=(0, 1))
            if d == 0:   # 익공 at each column
                x, y = a; L2 = math.hypot(x, y); ux, uy = x / L2, y / L2
                hb.beam((x - ux * 0.1, y - uy * 0.1, zt), (x + ux * 0.55, y + uy * 0.55, zt), 0.12, 0.22, 'dan', lu=(0.5, 1))
                hb.beam((x - ux * 0.1, y - uy * 0.1, zt + 0.24), (x + ux * 0.8, y + uy * 0.8, zt + 0.24), 0.12, 0.22, 'dan', lu=(0.5, 1))
        prism(hb, poly(n, R + 0.22), zt, zt + 0.5, 'dan')
        stairs(hb, 'f', 0.0, -(R + 1.0) * math.cos(math.pi / 6), zb, 0, 1.4)
    zw = zt + 0.5; ov = 1.75
    PolyRoof(n, (R * math.cos(math.pi / n) + ov) / math.cos(math.pi / n), zw, ov, s0=0.40, s1=1.15, lift=0.5, bow=0.3, pitch=0.28, rafter_pitch=0.36).build(hb, fin=1.0)


def sipjagak(hb):
    Hb = 4.6
    gate_base(hb, 8.4, 8.4, Hb, [], batter=0.3, parapet=1.1)
    xs = grid([1.75, 1.75, 1.75]); ys = grid([1.75, 1.75, 1.75])
    hall1(hb, xs, ys, Hb, 2.5, 0.2, ALL('window'), dict(n=2, h=0.65, proj=0.4, cb=0.28), 'hip', 1.7, roof=dict(s0=0.40, s1=1.08, lift=0.5, bow=0.28, pitch=0.3), base=False, figures=3, yang=True)


# ---------------------------------------------------------------- 7. 종묘 정전
JB = 4.04; JY = [3.6, 4.5, 4.5]; JBR = dict(n=1, h=0.55, proj=0.3, cb=0.34)


def jong_block(hb, nb, col_h, x0, ends=True, plat=0.9, tight=False):
    xs = [x0 + JB * i for i in range(nb + 1)]; ys = grid(JY)
    spec = {'f': 'open', 'b': 'brick', 'l': ['open', 'brick', 'brick'], 'r': ['open', 'brick', 'brick']}
    if not ends: spec['l'] = spec['r'] = 'open'
    hall1(hb, xs, ys, plat, col_h, 0.3, spec, JBR, 'gable', 1.9, roof=dict(s0=0.56, s1=0.86, lift=0.25, pitch=0.32), base=True, ridge=True,
          gable_out=0.0 if not ends else (0.35 if tight else 1.0), figures=0, ends=ends, br_sides='fblr' if ends else 'fb')
    if hb.d < 2:
        columns(hb, xs, [ys[1]], plat, col_h, 0.3, which='all')
        door_line(hb, xs, ys[1], plat + 0.12, plat + col_h - 0.36)
        hb.face([(xs[0], ys[0], plat + col_h + 0.5), (xs[-1], ys[0], plat + col_h + 0.5), (xs[-1], ys[1], plat + col_h + 0.5), (xs[0], ys[1], plat + col_h + 0.5)], 'green', n=(0, 0, -1))
    else: hb.box(xs[0], ys[1], plat, xs[-1], ys[-1], plat + col_h, 'wood')


def jongmyo(hb, n_main=19, n_side=3, wing=5):
    plat = 0.9; ys = grid(JY); Lm = n_main * JB; Ls = n_side * JB; xe = Lm / 2 + Ls
    platform(hb, -xe - 1.2, ys[0] - 1.6, xe + 1.2, ys[-1] + 1.0, 0, plat)
    jong_block(hb, n_main, 4.3, -Lm / 2)
    for sg in (-1, 1): jong_block(hb, n_side, 3.5, Lm / 2 if sg > 0 else -xe, tight=True)
    if hb.d < 2:
        for c in (0.0, -Lm / 3, Lm / 3): stairs(hb, 'f', c, ys[0] - 1.6, plat, 0, 3.2)
    wb = 3.3; wy = [wb] * wing; wd = 4.2       # 월랑: bays running south from both ends (east one open, west one walled)
    for sg in (-1, 1):
        def W(h2, sg=sg):
            xs = grid(wy); ys2 = grid([wd])
            platform(h2, xs[0] - 0.9, ys2[0] - 0.9, xs[-1] + 0.9, ys2[-1] + 0.9, 0, 0.6)
            sp = ALL('open') if sg > 0 else {'f': 'plaster', 'b': 'plaster', 'l': 'plaster', 'r': 'open'}
            hall1(h2, xs, ys2, 0.6, 3.0, 0.24, sp, dict(n=1, h=0.4, proj=0.22, cb=0.3), 'gable', 1.3, roof=dict(s0=0.5, s1=0.7, lift=0.2, pitch=0.32), base=True, gable_out=0.8, figures=0)
            if h2.d == 2: h2.box(xs[0], ys2[0], 0.6, xs[-1], ys2[-1], 3.6, 'wood')
        sub(hb, W, rot=math.pi / 2, tx=sg * (xe - wd / 2 + 0.2), ty=ys[0] - 1.6 - wb * wing / 2 - 0.9)


def jongmyo_bay(hb):
    platform(hb, -JB / 2, grid(JY)[0] - 1.6, JB / 2, grid(JY)[-1] + 1.0, 0, 0.9)
    jong_block(hb, 1, 4.3, -JB / 2, ends=False)


# ---------------------------------------------------------------- 8. 강녕전 / 교태전 class (무량각)
def gangnyeongjeon(hb):
    xs = grid([3.1] * 4 + [3.7] + [3.1] * 4); ys = grid([2.5, 3.4, 3.4, 2.5]); pz = 0.9
    platform(hb, xs[0] - 1.5, ys[0] - 1.5, xs[-1] + 1.5, ys[-1] + 1.5, 0, pz)
    platform(hb, -9.0, ys[0] - 1.5 - 6.0, 9.0, ys[0] - 1.5 + 0.05, 0, pz - 0.12)          # 월대
    if hb.d < 2:
        for c in (-5.0, 0.0, 5.0): stairs(hb, 'f', c, ys[0] - 7.5, pz - 0.12, 0, 2.6)
        for c in (xs[0] + 3.0, xs[-1] - 3.0): stairs(hb, 'b', c, ys[-1] + 1.5, pz, 0, 2.2)
    f = ['window'] * 3 + ['door'] * 3 + ['window'] * 3
    hall1(hb, xs, ys, pz, 3.4, 0.26, {'f': f, 'b': f, 'l': 'window', 'r': 'window'}, dict(n=2, h=0.7, proj=0.4, cb=0.32), 'hipgable', 2.3,
          roof=dict(g_in=3.6, s0=0.36, s1=0.92, lift=0.55, bow=0.3), ridge='low', figures=5)


# ---------------------------------------------------------------- 9. small stone / bronze pieces
def pumgyeseok(hb):
    pr = [(-0.115, 0.0), (-0.105, 0.40), (-0.075, 0.50), (0.0, 0.545), (0.075, 0.50), (0.105, 0.40), (0.115, 0.0)]
    hb.box(-0.27, -0.16, 0, 0.27, 0.16, 0.07, 'stone')
    wb, wt = 0.22, 0.20
    L = [(-(wb + (wt - wb) * z / 0.545), y, z + 0.07) for y, z in pr]; R = [((wb + (wt - wb) * z / 0.545), y, z + 0.07) for y, z in pr]
    for i in range(len(pr) - 1): hb.face([L[i], R[i], R[i + 1], L[i + 1]], 'stone', n=(0, pr[i][0] + pr[i + 1][0], 0.6 if i in (2, 3) else 0.05))
    hb.face(L, 'stone', n=(-1, 0, 0)); hb.face(R, 'stone', n=(1, 0, 0))


def deumeu(hb):
    n = 12 if hb.d == 0 else 8
    hb.box(-0.55, -0.55, 0, 0.55, 0.55, 0.22, 'stone'); hb.box(-0.42, -0.42, 0.22, 0.42, 0.42, 0.42, 'stone')
    pr = [(0.36, 0.42), (0.55, 0.62), (0.58, 0.80), (0.50, 0.93), (0.60, 0.98)]
    for i in range(len(pr) - 1): hb.cyl(0, 0, pr[i][1], pr[i + 1][1], pr[i][0], pr[i + 1][0], 'bronze', n=n, top=False)
    ring = lambda r, z: [(r * math.cos(2 * math.pi * i / n), r * math.sin(2 * math.pi * i / n), z) for i in range(n)]
    A, B, C = ring(0.60, 0.98), ring(0.52, 0.98), ring(0.50, 0.84)
    for i in range(n):
        j = (i + 1) % n
        hb.face([A[i], A[j], B[j], B[i]], 'bronze', n=(0, 0, 1))
        hb.face([B[i], B[j], C[j], C[i]], 'bronze', n=(-(B[i][0] + B[j][0]), -(B[i][1] + B[j][1]), 0))
    hb.face(C, 'bronze', n=(0, 0, 1))
    if hb.d == 0:
        for sg in (-1, 1): hb.box(sg * 0.58 - 0.05, -0.12, 0.72, sg * 0.58 + 0.05, 0.12, 0.84, 'bronze', bottom=True)


MODELS = {}
def reg(name, fn, ref, rep=''): MODELS[name] = (fn, ref, rep)


reg('Hero_Geunjeongjeon', geunjeongjeon, '경복궁 근정전: 정면 5칸 (17·21·22·21·17척 = 30.2 m) · 측면 5칸 (17·11·11·11·17척 = 20.6 m) 중층 팔작 다포, 기둥 16척 (4.93 m), 양성바름 마루, 잡상 7',
    'Gen_Hall_2Tier_5x5 @ Geunjeongjeon (same pivot: upper 월대 top)')
reg('Hero_Woldae_Geunjeong', woldae_geunjeong, '근정전 2단 월대: 합 2.72 m (1.36 m × 2), 하월대 43.0 × 39.0 · 상월대 37.2 × 30.9 m (as-built 배치와 같은 발자국), 사방 계단 + 돌난간',
    'P_Geunjeongjeon_stone_dressed/_paving solids (pivot = hall centre on the court ground)')
reg('Hero_Gyeonghoeru', gyeonghoeru, '경회루: 정면 7칸 34.4 × 측면 5칸 28.5 m 중층 누각 팔작 이익공, 돌기둥 48 (바깥 네모 민흘림 24 · 안 둥근 24), 돌기둥 4.7 m EST, 기단 0.9 m',
    'Gen_Gyeonghoeru (same pivot / yaw 90 / 기단 37.4 × 31.6)')
reg('Hero_Gwanghwamun_Munru', gwanghwamun_munru, '광화문 문루: 정면 3칸 (25·27·25척 = 23.7 m) · 측면 2칸 (10척 × 2 = 6.2 m) 중층 우진각 다포, 양성바름', 'Fort_Munru2C @ Gwanghwamun_Munru (pivot = 석축 윗면, scale 1)')
reg('Hero_Gwanghwamun', gwanghwamun, '광화문 한 덩이: 석축 40 × 13 × 7.0 m (너비 · 깊이 EST) + 홍예 셋 (5.5 × 5.4 · 4.5 × 4.9 m) + 전돌 여장 + 문루', 'Gen_GateBase_3Arch + Fort_Munru2C (pivot = ground centre)')
reg('Hero_Gate_Heungnye', heungnyemun, '흥례문 · 근정문 꼴: 정면 3칸 (16·18·16척 = 15.4 m) · 측면 2칸 (15척 × 2 = 9.2 m) 중층 우진각 다포, 기둥 15척, 가운데 줄 판문', 'Gate_Sinpungru @ Heungnyemun / Geunjeongmun (pivot = 기단 윗면)')
reg('Hero_Munru_5x2', sungnyemun_munru, '숭례문 · 흥인지문 문루: 정면 5칸 · 측면 2칸 중층 우진각 다포, 하층 22.9 × 7.9 m EST, 용마루 16.6 m', 'Fort_Munru2C (city gates; pivot = 석축 윗면)')
reg('Hero_Munru_3x2', munru1, '소문 문루: 정면 3칸 · 측면 2칸 단층 우진각, 9.4 × 5.4 m EST', 'Gen_Munru_1F_3x2')
reg('Hero_Hyangwonjeong', hyangwonjeong, '향원정: 정육각 2층 (아래위 같은 크기, 기둥이 두 층을 관통), 육모지붕 + 절병통, 높이 약 10.5 m; 한 변 2.6 m EST', 'Pav_Octagonal @ Hyangwonjeong (scale 1, pivot = island top)')
reg('Hero_Sipjagak', sipjagak, '동 · 서십자각: 석축 8.4 × 8.4 × 4.6 m EST + 전돌 여장 + 3 × 3칸 사모지붕 정자 (5.25 m EST)', 'P_Sipjagak_* solids (pivot = ground centre)')
reg('Hero_Jongmyo_Jeongjeon', jongmyo, '종묘 정전: 신실 19칸 + 협실 3 + 3 = 25칸 101 m (칸 4.04 m EST), 측면 3칸 (앞 퇴칸 트임), 맞배 익공, 동 · 서 월랑 5칸', 'Gen_Jongmyo_Jeongjeon')
reg('Hero_Jongmyo_Jeongjeon_11', lambda hb: jongmyo(hb, 11, 2, 3), '종묘 정전 줄인 본: 신실 11칸 + 협실 2 + 2 (60.6 m), 월랑 3칸 — 칸 폭 · 높이는 그대로', 'jeongjeon_11x3 plot (PLAN)')
reg('Hero_Jongmyo_Bay', jongmyo_bay, '종묘 정전 한 칸 모듈 4.04 m (박공 없음 — 이어 붙이는 용도)', 'Gen_Jongmyo_Bay')
reg('Hero_Hall_Muryanggak9', gangnyeongjeon, '강녕전 · 교태전 꼴: 정면 9칸 28.5 × 측면 4칸 11.8 m (EST) 단층 팔작 무량각 (용마루 없음) 익공, 앞 월대 18 × 6 m', 'Hall_Bongsudang @ Gangnyeongjeon / Hall_Boknaedang @ Gyotaejeon')
reg('Hero_Pumgyeseok', pumgyeseok, '품계석: 0.44 × 0.23 × 0.62 m (EST), 위가 둥근 비석 꼴 + 받침', 'Gen_Pumgyeseok / cube markers')
reg('Hero_Deumeu', deumeu, '드므: 청동 물독 지름 1.2 m (EST) + 돌 받침', '(new)')


if __name__ == '__main__':
    names = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    for n in names or list(MODELS):
        fn, ref, rep = MODELS[n]
        H.export(n, fn, ref, rep)
