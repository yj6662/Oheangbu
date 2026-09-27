"""#297 timber building generator (전각 / 행랑 / 문 / 창고 / 누각) on top of hanok297 (mesh builder + roofs).

Local origin: footprint centre at the platform base (ground level), front = +z. Returns (visual MB, collision MB).
Human scale (SPEC-WORLD-FINISH-297): risers <= .19 m, treads >= .26 m (interior) / .32 m (platform), doors >= 2.1 m,
rails .9 m. Collision stairs are ramps (<= 38 deg) so the player controller (step .3, slope 45) glides up.
"""
from __future__ import annotations
import math
import numpy as np
from hanok297 import MB, RoofSpec, build_roof, beam, nz

STYLES = {
    # column/door wood, beam band, bracket band height (0 = plain 민도리), platform stone
    'palace':     dict(col='wood_red', beam='dancheong_beam', bracket=.85, bracket_mat='dancheong_bracket', plinth='stone_dressed', palace=True),
    'official':   dict(col='wood_red', beam='dancheong_beam', bracket=.45, bracket_mat='dancheong_bracket', plinth='stone_dressed', palace=False),
    'military':   dict(col='wood_dark', beam='wood_dark', bracket=.28, bracket_mat='wood_dark', plinth='stone_rough', palace=False),
    'vernacular': dict(col='wood_dark', beam='wood_dark', bracket=0.0, bracket_mat='wood_dark', plinth='stone_rough', palace=False),
}


class BuildingSpec:
    def __init__(self, name, bays_x, bays_z, col_h=3.6, style='official', roof='palzak', platform_h=.9, platform_margin=1.1,
                 stairs=('front',), walls=None, storeys=None, round_cols=True, enterable=False, open_bays=(), floor='floor_wood',
                 eave=None, rise_ratio=.52, lift=None, bow=None, interior_stairs=False, gable_inset=None):
        self.name, self.bx, self.bz = name, list(bays_x), list(bays_z)
        self.col_h, self.style, self.roof, self.ph, self.pm = col_h, style, roof, platform_h, platform_margin
        self.stairs, self.round_cols, self.enterable, self.open_bays, self.floor = stairs, round_cols, enterable, set(open_bays), floor
        self.walls = walls or dict(front='doors', back='plaster', left='plaster', right='plaster')
        self.storeys = storeys or []           # upper storeys: dict(h=, inset=metres per side, balcony=bool, walls=...)
        self.eave, self.rise_ratio, self.lift, self.bow = eave, rise_ratio, lift, bow
        self.interior_stairs, self.gable_inset = interior_stairs, gable_inset

    @property
    def L(self): return sum(self.bx)

    @property
    def D(self): return sum(self.bz)


def grid_lines(bays):
    x = np.concatenate([[0.0], np.cumsum(bays)]); return x - x[-1] / 2


def build_building(b: BuildingSpec, lod=0):
    st = STYLES[b.style]; vis = MB(b.name); col = MB(b.name + '_collision')
    xs, zs = grid_lines(b.bx), grid_lines(b.bz); L, D = b.L, b.D; ph, pm = b.ph, b.pm
    PX, PZ = L / 2 + pm, D / 2 + pm
    # 기단 + 갑석 (platform with a capping course)
    if ph > .05:
        vis.box([0, ph / 2, 0], [2 * PX, ph, 2 * PZ], st['plinth'], faces='xXzZ', uvscale=2.0)
        vis.box([0, ph + .04, 0], [2 * PX + .12, .08, 2 * PZ + .12], 'stone_plain', faces='xXzZY', uvscale=2.0)
        col.box([0, ph / 2 + .04, 0], [2 * PX, ph + .08, 2 * PZ], 'c', faces='xXzZY')
        for side in b.stairs: platform_stairs(vis, col, side, PX, PZ, ph + .08, b, lod)
    top = ph + .08
    vis.box([0, top + .05, 0], [L + .3, .1, D + .3], b.floor, faces='Y', uvscale=1.2)
    levels = [dict(h=b.col_h, inset=0.0, walls=b.walls)] + list(b.storeys)
    y0 = top; inset = 0.0
    for k, lv in enumerate(levels):
        inset += lv.get('inset', 0.0); last = k == len(levels) - 1
        Lk, Dk = L - 2 * inset, D - 2 * inset
        lx = grid_lines(b.bx) * (Lk / L); lz = grid_lines(b.bz) * (Dk / D)
        h = lv['h']
        frame(vis, col, b, st, lx, lz, y0, h, lv.get('walls', b.walls), lod, k)
        if lv.get('balcony'): balcony(vis, col, lx, lz, y0, lod)
        eave_base = y0 + h + .30 + st['bracket']
        if not last:
            nxt = levels[k + 1]; ins = nxt.get('inset', 0.0)
            up_y = eave_base + max(1.1, (Dk / 2 - ins) * .30)
            eave = (b.eave or (.34 * h + .55)) * .85
            rs = RoofSpec('palzak', Lk, Dk, base=eave_base, eave=eave, corner_lift=.30, corner_bow=.36, sag=.06, thickness=.26)
            hx, hz = Lk / 2 - ins, Dk / 2 - ins
            build_roof(rs, lod, vis, hole=(hx + .15, hz + .15, up_y))
            bal = 1.2 if nxt.get('balcony') else 0.0
            SX, SZ = hx + bal + .1, hz + bal + .1
            if b.interior_stairs:
                interior_stair(vis, col, y0, up_y, lx, lz, k, lod)
                # stairwell: the slab (visual + collision) is open over the last 3.6 m of the flight — at its edge a
                # 1.75 m capsule on the 36 deg flight keeps ~.5 m headroom (2.9 m left 1.93 m: the descent stalled)
                sx_, z0_, run_ = stair_frame(y0, up_y, lx, lz, k)
                hole = (sx_ - .65, sx_ + .65, z0_ + run_ - 3.6, z0_ + run_ + .02)
                for mb, mat, faces in ((vis, b.floor, 'YyxXzZ'), (col, 'c', 'Yy')):
                    slab_with_hole(mb, up_y - .07, SX, SZ, hole, mat, faces)
            else:
                vis.box([0, up_y - .07, 0], [2 * SX, .14, 2 * SZ], b.floor, faces='YyxXzZ', uvscale=1.2)
                col.box([0, up_y - .07, 0], [2 * SX, .14, 2 * SZ], 'c', faces='Yy')
            y0 = up_y
        else:
            eave = b.eave or (.42 * h + .85)
            rise = (Dk / 2 + eave) * b.rise_ratio * (1.12 if b.roof == 'matbae' else 1.0)   # ~27-30 deg overall pitch
            rs = RoofSpec(b.roof, Lk, Dk, base=eave_base, eave=eave, rise=rise,
                          corner_lift=b.lift if b.lift is not None else (.18 + .13 * eave),
                          corner_bow=b.bow if b.bow is not None else (.22 + .18 * eave),
                          sag=.085, ridge_h=.42 + .02 * Lk, ridge_w=.34 + .008 * Lk, thickness=.30, palace=st['palace'],
                          gable_inset=b.gable_inset)
            build_roof(rs, lod, vis)
            if b.enterable: vis.box([0, y0 + h + .02, 0], [Lk, .08, Dk], 'ceiling', faces='y', uvscale=2.4)   # 우물천장 underside
    return vis, col


def platform_stairs(vis, col, side, PX, PZ, ph, b, lod):
    """Centre-bay steps (risers <= .18 m, treads .32 m) with 소맷돌 side blocks; collision = ramp."""
    n = max(2, int(math.ceil(ph / .18))); rise = ph / n; tread = .32; w = min(b.bx[len(b.bx) // 2] * .9, 3.6)
    s = 1 if side == 'front' else -1
    for i in range(n):
        y1 = rise * (i + 1); d = (n - i) * tread
        vis.box([0, y1 - rise / 2, s * (PZ + d / 2)], [w, rise, d], 'stone_plain', faces='xXzZY', uvscale=1.0)
    for sx in (-1, 1):
        vis.box([sx * (w / 2 + .18), ph / 2 + .06, s * (PZ + n * tread / 2)], [.36, ph + .12, n * tread], 'stone_dressed', faces='xXzZY', uvscale=1.0)
    a = [-w / 2, .02, s * (PZ + n * tread)]; bq = [w / 2, .02, s * (PZ + n * tread)]; c = [w / 2, ph, s * PZ]; d = [-w / 2, ph, s * PZ]
    col.quad(a, bq, c, d, 'c', [0, 1, s])


def frame(vis, col, b, st, xs, zs, y0, h, walls, lod, level):
    sides = 12 if lod == 0 else 8
    r = (.20 + .012 * max(b.bx)) if b.round_cols else (.17 + .01 * max(b.bx))
    X, Z = xs[-1], zs[-1]
    cols = [(x, z) for x in xs for z in zs if abs(abs(x) - X) < 1e-6 or abs(abs(z) - Z) < 1e-6]
    for x, z in cols:
        if lod < 2:
            if level == 0: vis.box([x, y0 + .09, z], [r * 3.2, .18, r * 3.2], 'stone_plain', faces='xXzZY', uvscale=1.0)   # 초석
            if b.round_cols: vis.cyl([x, y0 + .18, z], [x, y0 + h, z], r * 1.03, r * .92, sides, st['col'], uvscale=1.0)
            else: vis.box([x, y0 + .18 + (h - .18) / 2, z], [2 * r, h - .18, 2 * r], st['col'], faces='xXzZ', uvscale=1.0)
        col.box([x, y0 + h / 2, z], [2 * r, h, 2 * r], 'c', faces='xXzZ')
    band = .42; top = y0 + h
    for a, c, o, lines in (((-X, Z), (X, Z), (0, 0, 1), xs), ((X, -Z), (-X, -Z), (0, 0, -1), xs),
                           ((X, Z), (X, -Z), (1, 0, 0), zs), ((-X, -Z), (-X, Z), (-1, 0, 0), zs)):
        o = np.asarray(o, float); p0 = np.array([a[0], top, a[1]]) + o * r * .7; p1 = np.array([c[0], top, c[1]]) + o * r * .7
        dancheong_band(vis, p0, p1, top - band, top, st['beam'], o, len(lines) - 1)
        if lod < 2:   # beam underside + top so the band reads as a solid 창방
            vis.quad(p0 + [0, -band, 0] - o * .3, p1 + [0, -band, 0] - o * .3, p1 + [0, -band, 0], p0 + [0, -band, 0], 'wood_red' if st['palace'] or b.style == 'official' else 'wood_dark', [0, -1, 0], uvscale=1.0)
        if st['bracket'] > 0 and lod < 2:
            L_ = np.linalg.norm(p1 - p0); q0 = p0 + o * .12; q1 = p1 + o * .12
            vis.quad(q0, q1, q1 + [0, st['bracket'], 0], q0 + [0, st['bracket'], 0], st['bracket_mat'], o,
                     uv=np.array([[0, 0], [L_ / 2.2, 0], [L_ / 2.2, 1], [0, 1]]))
    for side, (line, axis, o) in dict(front=(Z, xs, (0, 0, 1)), back=(-Z, xs, (0, 0, -1)), right=(X, zs, (1, 0, 0)), left=(-X, zs, (-1, 0, 0))).items():
        kind = walls.get(side, 'plaster') if isinstance(walls, dict) else walls
        for i in range(len(axis) - 1):
            if kind == 'open' or (side, i) in b.open_bays: continue
            bay(vis, col, side, line, axis[i], axis[i + 1], y0, top - band, kind, np.asarray(o, float), lod, r, b)


def dancheong_band(vis, p0, p1, y_lo, y_hi, mat, o, n_bays):
    """창방 face: one atlas strip (머리초 motifs at both ends) per bay so the motifs sit at the columns."""
    for i in range(n_bays):
        a = p0 + (p1 - p0) * i / n_bays; c = p0 + (p1 - p0) * (i + 1) / n_bays
        A = [a[0], y_lo, a[2]]; B = [c[0], y_lo, c[2]]; C = [c[0], y_hi, c[2]]; Dd = [a[0], y_hi, a[2]]
        if mat == 'dancheong_beam': uv = np.array([[.03, .907], [.97, .907], [.97, .941], [.03, .941]])
        else:
            seg = np.linalg.norm(c - a); uv = np.array([[0, 0], [seg, 0], [seg, y_hi - y_lo], [0, y_hi - y_lo]]) / 1.2
        vis.quad(A, B, C, Dd, mat, o, uv=uv)


def bay(vis, col, side, line, a, c, y0, y1, kind, o, lod, r, b):
    inset = .09
    P = (lambda u, y: np.array([u, y, line])) if side in ('front', 'back') else (lambda u, y: np.array([line, y, u]))
    lo_u, hi_u = min(a, c) + r, max(a, c) - r
    if hi_u - lo_u <= .2: return
    ua, uc = lo_u, hi_u
    q = lambda u0, u1, ya, yb, mat, uv=None, sc=1.0: vis.quad(P(u0, ya) - o * inset, P(u1, ya) - o * inset, P(u1, yb) - o * inset, P(u0, yb) - o * inset, mat, o, uv=uv, uvscale=sc)
    if kind in ('doors', 'window'):
        lo = y0 + (.28 if kind == 'doors' else 1.05)
        q(ua, uc, y0 + .18, lo, 'wood_board')                                    # 하방/머름
        lat = 'lattice_red' if b.style in ('palace', 'official') and kind == 'doors' else ('window_grid' if kind == 'window' else 'lattice')
        q(ua, uc, lo, y1 - .28, lat, uv=np.array([[0, 0], [1, 0], [1, 1], [0, 1]]))
        q(ua, uc, y1 - .28, y1, 'plaster', sc=1.5)                                # 인방 위 회벽
    elif kind == 'plaster':
        q(ua, uc, y0 + .18, y0 + .75, 'wood_board'); q(ua, uc, y0 + .75, y1, 'plaster', sc=1.5)
    elif kind == 'planks':
        q(ua, uc, y0 + .18, y1, 'wood_board')
    elif kind == 'stone':
        q(ua, uc, y0, y1, 'stone_dressed', sc=2.0)
    elif kind == 'rail':
        railing(vis, col, P(ua, y0), P(uc, y0), lod); return
    m = (P(ua, 0) + P(uc, 0)) / 2
    if side in ('front', 'back'): col.box([m[0], (y0 + y1) / 2, m[2] - o[2] * inset], [uc - ua, y1 - y0, .2], 'c', faces='xXzZ')
    else: col.box([m[0] - o[0] * inset, (y0 + y1) / 2, m[2]], [.2, y1 - y0, uc - ua], 'c', faces='xXzZ')


def railing(vis, col, a, c, lod, h=.9):
    a = np.asarray(a, float); c = np.asarray(c, float)
    beam(vis, a + [0, h, 0], c + [0, h, 0], .09, .07, 'wood_dark'); beam(vis, a + [0, .14, 0], c + [0, .14, 0], .08, .1, 'wood_dark')
    if lod == 0:
        n = max(2, int(np.linalg.norm(c - a) / .9))
        for k in range(n + 1):
            p = a + (c - a) * k / n; beam(vis, p + [0, .1, 0], p + [0, h, 0], .07, .07, 'wood_dark')
    m = (a + c) / 2; d = np.abs(c - a)
    col.box([m[0], m[1] + h / 2 + .1, m[2]], [max(d[0], .12), h + .2, max(d[2], .12)], 'c', faces='xXzZ')


def balcony(vis, col, xs, zs, y0, lod):
    X, Z = xs[-1] + 1.2, zs[-1] + 1.2
    for a, c in (((-X, Z), (X, Z)), ((X, -Z), (-X, -Z)), ((X, Z), (X, -Z)), ((-X, -Z), (-X, Z))):
        railing(vis, col, [a[0], y0, a[1]], [c[0], y0, c[1]], lod)


def stair_frame(y0, y1, xs, zs, level):
    """(x, z0, run) of the interior flight between floors y0 -> y1 (shared by the stair, its stairwell and routes)."""
    n = max(3, int(math.ceil((y1 - y0) / .19)))
    return (1 if level % 2 == 0 else -1) * (xs[-1] - .95), zs[0] + .7, n * .26


def slab_with_hole(mb, y, X, Z, hole, mat, faces, t=.14):
    """Floor slab (half extents X, Z) at height y with a rectangular opening hole=(xa, xb, za, zb)."""
    xa, xb, za, zb = hole
    for x0, x1, z0, z1 in ((-X, xa, -Z, Z), (xb, X, -Z, Z), (xa, xb, -Z, za), (xa, xb, zb, Z)):
        if x1 - x0 > .01 and z1 - z0 > .01:
            mb.box([(x0 + x1) / 2, y, (z0 + z1) / 2], [x1 - x0, t, z1 - z0], mat, faces=faces, uvscale=1.2)


def storey_route(b):
    """Building-local walk route from the front stair foot up the first interior flight to the upper floor centre
    (same formulas as build_building). Only for specs with storeys + interior_stairs."""
    st = STYLES[b.style]; xs, zs = grid_lines(b.bx), grid_lines(b.bz); PZ = b.D / 2 + b.pm; top = b.ph + .08
    lv = b.storeys[0]; ins = lv.get('inset', 0.0)
    up_y = top + b.col_h + .30 + st['bracket'] + max(1.1, (b.D / 2 - ins) * .30)
    x, z0, run = stair_frame(top, up_y, xs, zs, 0)
    n_front = max(2, int(math.ceil((b.ph + .08) / .18)))
    xa = x - math.copysign(1.6, x)                                  # aisle beside the flight (never under it)
    return [[0, 0.0, PZ + n_front * .32 + .8], [0, top, PZ - .4], [xa, top, zs[-1] - .8], [xa, top, z0 - .5], [x, top, z0 - .5],
            [x, top, z0 + .15], [x, up_y, z0 + run - .05], [x, up_y, z0 + run + .3], [xa, up_y, z0 + run + .2], [0, up_y, 0],
            [0, up_y, zs[-1] - .9]], up_y


def interior_stair(vis, col, y0, y1, xs, zs, level, lod):
    """Wooden stair in a side bay: risers ~.19 m, treads .26 m (~36 deg). Collision = ramp."""
    rise = y1 - y0; n = max(3, int(math.ceil(rise / .19))); tread = .26; run = n * tread; w = 1.1
    x, z0, _ = stair_frame(y0, y1, xs, zs, level)
    for i in range(n):
        yy = y0 + rise * (i + 1) / n; zz = z0 + tread * (i + .5)
        vis.box([x, yy - .05, zz], [w, .1, tread + .02], 'wood_board', faces='xXyYzZ', uvscale=1.0)
    for sx in (-1, 1): beam(vis, [x + sx * w / 2, y0, z0], [x + sx * w / 2, y1, z0 + run], .08, .24, 'wood_dark')
    col.quad([x - w / 2, y0 + .02, z0], [x + w / 2, y0 + .02, z0], [x + w / 2, y1, z0 + run], [x - w / 2, y1, z0 + run], 'c', [0, 1, -1])
