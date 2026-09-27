"""#297 terrain-following walls: 산성 (mountain fortress wall with walk + 여장 parapet), 궁장 / 담장 (tile-capped walls).

World space (Unity x/y/z). Terrain = #295/#296 height field (4 m grid, bilinear). The path is a polyline in XZ;
`outward` (+1/-1) selects which side of the path direction is the outer face.
Fortress wall cross-section (outward side = O):
    O parapet (여장, 1.4 m, crenels 타구 every 3.2 m) | walk (성벽길) | inner face / earth (내탁)
    outer face battered (1:0.12), buried 1 m below the outer ground; walk grade limited (steps where steeper).
Collision: walk strips (walkable), parapet + faces (blocking). Gates are gaps: split the path.
"""
from __future__ import annotations
import math
from pathlib import Path
import numpy as np
from hanok297 import MB, beam, nz

ROOT = Path(__file__).resolve().parents[2]
SURF = ROOT / 'Oheangbu/Assets/_Project/Art/World/Watershed295/Surface'
_H = None


def terrain():
    global _H
    if _H is None: _H = np.fromfile(SURF / 'height.bytes', '<f4').reshape(1501, 1001).astype(np.float64)
    return _H


def H(x, z):
    h = terrain(); x = np.asarray(x, float); z = np.asarray(z, float)
    gx = np.clip(x / 4, 0, 999.999); gz = np.clip(z / 4, 0, 1499.999); i = np.floor(gx).astype(int); j = np.floor(gz).astype(int)
    fx = gx - i; fz = gz - j
    return h[j, i] * (1 - fx) * (1 - fz) + h[j, i + 1] * fx * (1 - fz) + h[j + 1, i] * (1 - fx) * fz + h[j + 1, i + 1] * fx * fz


def resample(path, step):
    """Densify a polyline (XZ) with a Catmull-Rom spline so the wall curves follow the ridge smoothly."""
    P = np.asarray(path, float)
    pts = [P[0]]
    for i in range(len(P) - 1):
        p0 = P[max(i - 1, 0)]; p1 = P[i]; p2 = P[i + 1]; p3 = P[min(i + 2, len(P) - 1)]
        n = max(2, int(np.linalg.norm(p2 - p1) / step))
        for k in range(1, n + 1):
            t = k / n; t2 = t * t; t3 = t2 * t
            pts.append(.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (-p0 + 3 * p1 - 3 * p2 + p3) * t3))
    return np.array(pts)


def walk_profile(s, ground, max_grade=.30, smooth=6):
    """Walk height along s: follows the ground, smoothed, grade-limited (forward/backward passes)."""
    y = ground.copy()
    for _ in range(smooth): y[1:-1] = .25 * y[:-2] + .5 * y[1:-1] + .25 * y[2:]
    y = np.maximum(y, ground - .15)
    for _ in range(3):
        for i in range(1, len(y)): y[i] = max(y[i], y[i - 1] - max_grade * (s[i] - s[i - 1]))
        for i in range(len(y) - 2, -1, -1): y[i] = max(y[i], y[i + 1] - max_grade * (s[i + 1] - s[i]))
    return y


class FortressWallSpec:
    def __init__(self, name, path, outward=1, walk_w=3.0, min_outer=5.0, inner_rise=1.2, parapet_h=1.45, parapet_t=.55,
                 crenel=3.2, crenel_gap=.5, batter=.12, stone='stone_rough', parapet_mat='stone_rough', walk_mat='stone_plain',
                 max_grade=.30, closed=False, fixed_walk=None, walk_bias=0.0):
        self.name, self.path, self.outward = name, path, outward
        self.fixed_walk, self.walk_bias = fixed_walk, walk_bias     # fixed walk height (bastions match the main wall)
        self.walk_w, self.min_outer, self.inner_rise, self.parapet_h, self.parapet_t = walk_w, min_outer, inner_rise, parapet_h, parapet_t
        self.crenel, self.crenel_gap, self.batter = crenel, crenel_gap, batter
        self.stone, self.parapet_mat, self.walk_mat, self.max_grade, self.closed = stone, parapet_mat, walk_mat, max_grade, closed


def circuit_profile(path, outward, walk_w=3.0, inner_rise=1.2, min_outer=5.0, max_grade=.30, step=1.5):
    """Dense points, arc length and one continuous walk profile for a whole circuit (segments + gates share it)."""
    P = resample(path, step)
    d = nz(np.gradient(P, axis=0)); n = np.stack([d[:, 1], -d[:, 0]], -1) * outward
    s = np.concatenate([[0], np.cumsum(np.linalg.norm(np.diff(P, axis=0), axis=1))]); hw = walk_w / 2
    g_c = H(P[:, 0], P[:, 1]); g_o = H(*(P + n * (hw + 2.0)).T); g_i = H(*(P - n * (hw + 1.5)).T)
    ground = np.maximum(np.maximum(g_c, g_i) + inner_rise, g_o + min_outer)
    return P, s, walk_profile(s, ground, max_grade)


def fortress_wall(w: FortressWallSpec, lod=0, P=None, y=None, s0=0.0):
    """Returns (visual MB, collision MB, info dict with the walk polyline for route checks).
    P/y: precomputed dense XZ points and walk heights (from circuit_profile) — used as-is (subsampled for LOD1/2)."""
    step = [1.5, 3.0, 6.0][lod]
    if P is None:
        P = resample(w.path, step)
    elif lod > 0:
        k = [1, 2, 4][lod]; keep = np.unique(np.r_[np.arange(0, len(P), k), len(P) - 1]); P = P[keep]; y = None if y is None else y[keep]
    d = np.gradient(P, axis=0); d = nz(d); n = np.stack([d[:, 1], -d[:, 0]], -1) * w.outward   # outward normal (XZ)
    s = np.concatenate([[0], np.cumsum(np.linalg.norm(np.diff(P, axis=0), axis=1))])
    hw = w.walk_w / 2
    g_c = H(P[:, 0], P[:, 1]); g_o = H(*(P + n * (hw + 2.0)).T); g_i = H(*(P - n * (hw + 1.5)).T)
    if y is None:
        ground = np.maximum(g_c, g_i) + w.inner_rise
        ground = np.maximum(ground, g_o + w.min_outer)                 # outer face at least min_outer tall
        y = walk_profile(s, ground, w.max_grade) + w.walk_bias
    if w.fixed_walk is not None: y = np.full(len(P), float(w.fixed_walk))
    vis = MB(w.name); col = MB(w.name + '_collision')
    X = lambda off, yy: np.stack([P[:, 0] + n[:, 0] * off, yy, P[:, 1] + n[:, 1] * off], -1)
    outer_top = X(hw, y); inner_top = X(-hw, y)
    base_o = g_o - 1.2; base_i = np.minimum(g_i, y) - .8
    outer_foot = X(hw + w.batter * (y - base_o), base_o); inner_foot = X(-hw - .08 * (y - base_i), base_i)
    uv_s = (s + s0) / 2.0                                              # s0: arc length of this chunk on the circuit
    def strip(A, B, mat, out_sign, uvh, collide=True):
        """Ruled surface between polylines A (lower) and B (upper)."""
        Pq = np.stack([A, B], 1)
        uv = np.stack([np.repeat(uv_s[:, None], 2, 1), np.stack([A[:, 1], B[:, 1]], 1) / uvh], -1)
        o = lambda Q: np.concatenate([n[:, None, 0:1].repeat(2, 1) * out_sign, np.zeros((len(n), 2, 1)), n[:, None, 1:2].repeat(2, 1) * out_sign], -1)
        vis.grid(Pq, mat, uv, o)
        if collide: col.grid(Pq, 'c', uv, o)
    strip(outer_foot, outer_top, w.stone, 1, 2.0)
    strip(inner_foot, inner_top, w.stone, -1, 2.0)
    # walk surface (inner edge -> parapet inner face)
    walk_in = X(-hw, y + .02); walk_out = X(hw - w.parapet_t, y + .02)
    Pq = np.stack([walk_in, walk_out], 1)
    uvw = np.stack([np.repeat(uv_s[:, None], 2, 1), np.repeat(np.array([[0, 1.5]]), len(P), 0)], -1)
    vis.grid(Pq, w.walk_mat, uvw, np.array([0, 1, 0])); col.grid(Pq, 'c', uvw, np.array([0, 1, 0]))
    # parapet (여장) with crenels: blocks between gaps
    ph, pt = w.parapet_h, w.parapet_t
    seg_len = w.crenel; gap = w.crenel_gap
    starts = np.arange(-((s0) % seg_len), s[-1], seg_len)              # crenel rhythm continues across chunks
    for a in starts:
        b = min(a + seg_len - gap, s[-1])
        if b - a < .6: continue
        idx = np.where((s >= a) & (s <= b))[0]
        if len(idx) < 2: continue
        A_out = X(hw, y)[idx]; A_in = X(hw - pt, y)[idx]
        top_out = A_out + [0, ph, 0]; top_in = A_in + [0, ph, 0]
        sub = lambda Q: Q
        for (L0, L1, sign) in ((A_out, top_out, 1), (A_in, top_in, -1)):
            Pq = np.stack([L0, L1], 1)
            uv = np.stack([np.repeat(uv_s[idx][:, None], 2, 1), np.stack([L0[:, 1], L1[:, 1]], 1) / 2.0], -1)
            nn = n[idx]
            vis.grid(Pq, w.parapet_mat, uv, lambda Q, nn=nn, sign=sign: np.concatenate([nn[:, None, 0:1].repeat(2, 1) * sign, np.zeros((len(nn), 2, 1)), nn[:, None, 1:2].repeat(2, 1) * sign], -1))
        Pq = np.stack([top_in, top_out], 1)
        vis.grid(Pq, 'stone_plain', np.stack([np.repeat(uv_s[idx][:, None], 2, 1), np.repeat(np.array([[0, .5]]), len(idx), 0)], -1), np.array([0, 1, 0]))
        for e, sgn in ((idx[0], -1), (idx[-1], 1)):   # crenel cheeks
            vis.quad(X(hw, y)[e], X(hw - pt, y)[e], X(hw - pt, y)[e] + [0, ph, 0], X(hw, y)[e] + [0, ph, 0], w.parapet_mat,
                     np.array([d[e, 0], 0, d[e, 1]]) * sgn, uvscale=1.0)
    # parapet collision: one continuous low wall (gaps are too narrow to pass; keeps players on the walk)
    Pq = np.stack([X(hw - pt / 2, y), X(hw - pt / 2, y + ph + .2)], 1)
    col.grid(Pq, 'c', np.zeros(Pq.shape[:2] + (2,)), lambda Q: np.concatenate([-n[:, None, 0:1].repeat(2, 1), np.zeros((len(n), 2, 1)), -n[:, None, 1:2].repeat(2, 1)], -1))
    # steps where the walk is steep (visual cue; the walk collider stays a ramp <= max_grade)
    if lod < 2:
        grade = np.gradient(y, s)
        for i in np.where(np.abs(grade) > .16)[0][::2]:
            q = X(0, y)[i]; vis.box([q[0], q[1] + .08, q[2]], [.35, .16, w.walk_w - pt - .1], 'stone_plain',
                                     yaw=math.degrees(math.atan2(d[i, 0], d[i, 1])) + 90, faces='xXzZY', uvscale=1.0)
    info = dict(walk=[[float(a), float(b), float(c)] for a, b, c in X(-hw / 2 + .2, y + .05)[::max(1, int(4 / step))]],
                length=float(s[-1]), top=[float(y.min()), float(y.max())], outer_height=[float((y - g_o).min()), float((y - g_o).max())])
    return vis, col, info


def gatehouse_arch(name, centre, yaw, width=4.2, height=4.6, depth=None, wall_top=None, lod=0):
    """홍예문: stone block with an arched passage through the wall line; returns (vis, col). centre = (x, ground y, z)."""
    cx, cy, cz = centre; depth = depth or 9.0; top = wall_top if wall_top is not None else cy + height + 2.4
    vis = MB(name); col = MB(name + '_collision')
    half = width / 2; W = width + 6.0
    ca, sa = math.cos(math.radians(yaw)), math.sin(math.radians(yaw))
    L = lambda x, y, z: np.array([cx + x * ca + z * sa, y, cz - x * sa + z * ca])
    seg = [16, 10, 6][lod]
    # piers (left/right masses) + lintel mass above the arch
    for sx in (-1, 1):
        xc = sx * (half + (W / 2 - half) / 2); wx = W / 2 - half
        vis.box(L(xc, (cy - 1 + top) / 2, 0), [wx, top - cy + 1, depth], 'stone_dressed', yaw=yaw, faces='xXzZY', uvscale=2.0)
        col.box(L(xc, (cy - 1 + top) / 2, 0), [wx, top - cy + 1, depth], 'c', yaw=yaw, faces='xXzZY')
    spring = cy + height - half                                      # arch springs here, semicircle radius = half
    ang = np.linspace(0, math.pi, seg + 1)
    for zf in (-depth / 2, depth / 2):                               # arch faces (front/back): spandrel as a fan
        o = np.array([sa, 0, ca]) * (1 if zf > 0 else -1)
        for a0, a1 in zip(ang[:-1], ang[1:]):
            p0 = L(half * math.cos(a0), spring + half * math.sin(a0), zf); p1 = L(half * math.cos(a1), spring + half * math.sin(a1), zf)
            t0 = L(np.sign(math.cos(a0)) * half if abs(math.cos(a0)) > .7 else half * math.cos(a0), top, zf)
            t1 = L(np.sign(math.cos(a1)) * half if abs(math.cos(a1)) > .7 else half * math.cos(a1), top, zf)
            vis.quad(p0, p1, t1, t0, 'stone_dressed', o, uvscale=2.0)
    for a0, a1 in zip(ang[:-1], ang[1:]):                            # vault underside
        p0 = L(half * math.cos(a0), spring + half * math.sin(a0), -depth / 2); p1 = L(half * math.cos(a1), spring + half * math.sin(a1), -depth / 2)
        q0 = L(half * math.cos(a0), spring + half * math.sin(a0), depth / 2); q1 = L(half * math.cos(a1), spring + half * math.sin(a1), depth / 2)
        c_ = L(0, spring, 0); vis.quad(p0, p1, q1, q0, 'stone_plain', c_ - (p0 + q1) / 2, uvscale=1.5)
    for sx in (-1, 1):                                               # passage side walls up to the spring line
        a = L(sx * half, cy - .2, -depth / 2); b = L(sx * half, cy - .2, depth / 2); c_ = L(sx * half, spring, depth / 2); dd = L(sx * half, spring, -depth / 2)
        vis.quad(a, b, c_, dd, 'stone_dressed', L(-sx, 0, 0) - L(0, 0, 0), uvscale=2.0)
    vis.box(L(0, top + .06, 0), [W + .2, .12, depth + .2], 'stone_plain', yaw=yaw, faces='xXzZY', uvscale=2.0)   # top slab (walk level)
    col.box(L(0, (spring + half + top) / 2 + .1, 0), [width, top - spring - half + .2, depth], 'c', yaw=yaw, faces='xXzZyY')
    return vis, col, dict(top=top, passage=[L(0, cy, -depth / 2).tolist(), L(0, cy, depth / 2).tolist()])


def path_profile(path, width=2.4, lift=.12, max_grade=.62, step=1.0):
    """Dense points + surface heights for a terrain-following path (never buried; grade-limited where possible)."""
    P = resample(path, step)
    d = nz(np.gradient(P, axis=0)); n = np.stack([d[:, 1], -d[:, 0]], -1)
    s = np.concatenate([[0], np.cumsum(np.linalg.norm(np.diff(P, axis=0), axis=1))]); hw = width / 2
    g = np.maximum.reduce([H(*(P + n * o).T) for o in (-hw, 0, hw)]) + lift
    y = g.copy()
    for _ in range(4): y[1:-1] = .25 * y[:-2] + .5 * y[1:-1] + .25 * y[2:]
    y = np.maximum(y, g - .05)
    for i in range(1, len(y)):
        ds = s[i] - s[i - 1]; y[i] = min(y[i], y[i - 1] + max_grade * ds) if y[i] > y[i - 1] else max(y[i], y[i - 1] - max_grade * ds)
    return P, np.maximum(y, g - .05)


def stone_path(name, path=None, width=2.4, lift=.12, max_grade=.62, tread=.36, lod=0, curb=True, surface='stone_plain', edge='stone_rough',
               step=1.0, P=None, y=None, s0=0.0):
    """Walkable stone path/stair: surface = terrain + lift (grade-limited, never buried). Where the grade exceeds .14 the
    visual gets stone steps (riser <= .18 m) over a ramp collider. Curbs (끝돌) seat it on the slope. Returns (vis, col, info).
    P/y: precomputed dense points/heights (path_profile) for chunked export with arc-length offset s0."""
    if P is None: P, y = path_profile(path, width, lift, max_grade, step)
    if lod > 0:
        k = [1, 2, 4][lod]; keep = np.unique(np.r_[np.arange(0, len(P), k), len(P) - 1]); P = P[keep]; y = y[keep]
    d = nz(np.gradient(P, axis=0)); n = np.stack([d[:, 1], -d[:, 0]], -1)
    s = np.concatenate([[0], np.cumsum(np.linalg.norm(np.diff(P, axis=0), axis=1))]); hw = width / 2
    vis = MB(name); col = MB(name + '_collision')
    X = lambda off, yy: np.stack([P[:, 0] + n[:, 0] * off, yy, P[:, 1] + n[:, 1] * off], -1)
    L, R = X(-hw, y), X(hw, y)
    Pq = np.stack([L, R], 1); uv = np.stack([np.repeat(((s + s0) / 1.6)[:, None], 2, 1), np.repeat(np.array([[0, width / 1.6]]), len(P), 0)], -1)
    col.grid(Pq, 'c', uv, np.array([0, 1, 0]))
    vis.grid(Pq, surface, uv, np.array([0, 1, 0]))
    grade = np.gradient(y, s) if len(s) > 1 else np.zeros(len(s))
    if lod == 0:
        flat = np.abs(grade) <= .14; i = 0
        while i < len(P) - 1:
            if flat[i]: i += 1; continue
            j = i; ds = 0.0
            while j < len(P) - 1 and ds < tread: ds += s[j + 1] - s[j]; j += 1
            a, b = X(0, y)[i], X(0, y)[j]; top = max(a[1], b[1]); c = (a + b) / 2
            vis.box([c[0], top - .07, c[2]], [width - .1, .18, max(ds, .3)], edge, yaw=math.degrees(math.atan2(d[i, 0], d[i, 1])), faces='xXzZY', uvscale=1.0)
            i = j
    if curb and lod < 2:
        for side in (-1, 1):
            A = X(side * (hw + .12), y - .35); B = X(side * (hw + .12), y + .16)
            Pc = np.stack([A, B], 1); uvc = np.stack([np.repeat(((s + s0) / 1.2)[:, None], 2, 1), np.repeat(np.array([[0, .4]]), len(P), 0)], -1)
            vis.grid(Pc, edge, uvc, lambda Q, side=side: np.concatenate([n[:, None, 0:1].repeat(2, 1) * side, np.zeros((len(n), 2, 1)), n[:, None, 1:2].repeat(2, 1) * side], -1))
    info = dict(points=[[float(a), float(b), float(c)] for a, b, c in X(0, y + .05)[::max(1, int(3 / step))]], length=float(s[-1]),
                rise=float(y.max() - y.min()), max_grade=float(np.abs(grade).max()) if len(grade) else 0.0)
    return vis, col, info


def wall_stair(name, P, y, top_s, down_dir, outward, walk_w, width=2.4, grade=.62, batter=.08, lod=0, step=.5):
    """등성 계단: a stone stair against the inner face of a circuit wall, from the ground up to the walk at arc length top_s.
    P/y: the circuit's dense XZ points and walk heights; down_dir = +1/-1 along the circuit (the way the stair descends);
    outward: the circuit's outward sign. The stair hugs the battered inner face, meets the walk flush at the top, and gets a
    rough retaining face on its open side down to the ground. Returns (vis, col, info) like stone_path (info: foot/top)."""
    s = np.concatenate([[0], np.cumsum(np.linalg.norm(np.diff(P, axis=0), axis=1))])
    d = nz(np.gradient(P, axis=0)); n = np.stack([d[:, 1], -d[:, 0]], -1) * outward      # outward normal
    at = lambda arr, q: np.stack([np.interp(q, s, arr[:, k]) for k in range(arr.shape[1])], -1) if arr.ndim > 1 else np.interp(q, s, arr)
    y_top = float(np.interp(top_s, s, y)); hw = walk_w / 2
    pts, hs = [], []; k = 0
    while k < 400:
        q = top_s + down_dir * k * step; q = float(np.clip(q, 0, s[-1]))
        h = y_top - grade * k * step
        c = at(P, q); nn = nz(at(n, q)); off = hw + batter * max(0.0, y_top - h) + width / 2 + .05
        xz = c - nn * off; g = float(H(xz[0], xz[1]))
        pts.append(xz); hs.append(max(h, g + .12))
        if h <= g + .12 or q in (0.0, float(s[-1])): break
        k += 1
    # landing flush with the walk beyond the top (2.5 m): the only place a walker can step sideways onto the walk
    land_p, land_h = [], []
    for k in range(1, 6):
        q = float(np.clip(top_s - down_dir * k * step, 0, s[-1])); c = at(P, q); nn = nz(at(n, q))
        land_p.append(c - nn * (hw + width / 2 + .05)); land_h.append(float(np.interp(q, s, y)))
    Ps = np.concatenate([np.array(pts)[::-1], np.array(land_p)]); ys = np.concatenate([np.array(hs)[::-1], np.array(land_h)])   # foot -> top -> landing
    vis, col, info = stone_path(name, width=width, lod=lod, P=Ps, y=ys, curb=False)
    if lod < 2:
        dd = nz(np.gradient(Ps, axis=0)); nn = np.stack([dd[:, 1], -dd[:, 0]], -1)
        wall_side = np.sign(np.sum(nn * (at(P, top_s) - Ps[-1]))) or 1.0
        free = -wall_side * (width / 2 + .05)
        top = np.stack([Ps[:, 0] + nn[:, 0] * free, ys + .02, Ps[:, 1] + nn[:, 1] * free], -1)
        foot = np.stack([top[:, 0], H(top[:, 0], top[:, 2]) - .4, top[:, 2]], -1)
        Pq = np.stack([foot, top], 1); uv = np.stack([np.repeat(np.arange(len(Ps))[:, None] * step / 2.0, 2, 1), Pq[..., 1] / 2.0], -1)
        o = lambda Q: np.concatenate([nn[:, None, 0:1].repeat(2, 1) * -wall_side, np.zeros((len(nn), 2, 1)), nn[:, None, 1:2].repeat(2, 1) * -wall_side], -1)
        vis.grid(Pq, 'stone_rough', uv, o)
    n_st = len(pts); info.update(foot=[float(Ps[0, 0]), float(ys[0]), float(Ps[0, 1])], top=[float(Ps[n_st - 1, 0]), float(ys[n_st - 1]), float(Ps[n_st - 1, 1])],
                     landing=[float(Ps[-1, 0]), float(ys[-1]), float(Ps[-1, 1])], run=float(n_st * step))
    return vis, col, info


def ramp_stair(name, pts, y0, y1, width=2.6, lod=0, step=.5):
    """Free-standing stone stair between explicit heights (e.g. up to a raised hall entrance): stone_path over a straight
    height ramp y0 -> y1 along pts (XZ), rough retaining faces on both sides down to the ground. Returns (vis, col, info)."""
    Pp = resample(np.asarray(pts, float), step)
    s = np.concatenate([[0], np.cumsum(np.linalg.norm(np.diff(Pp, axis=0), axis=1))])
    ys = y0 + (y1 - y0) * s / max(s[-1], 1e-6)
    ys = np.maximum(ys, H(Pp[:, 0], Pp[:, 1]) + .08)
    vis, col, info = stone_path(name, width=width, lod=lod, P=Pp, y=ys, curb=False)
    if lod < 2:
        dd = nz(np.gradient(Pp, axis=0)); nn = np.stack([dd[:, 1], -dd[:, 0]], -1)
        for side in (-1, 1):
            top = np.stack([Pp[:, 0] + nn[:, 0] * side * (width / 2 + .05), ys + .02, Pp[:, 1] + nn[:, 1] * side * (width / 2 + .05)], -1)
            foot = np.stack([top[:, 0], H(top[:, 0], top[:, 2]) - .4, top[:, 2]], -1)
            Pq = np.stack([foot, top], 1); uv = np.stack([np.repeat(s[:, None] / 2.0, 2, 1), Pq[..., 1] / 2.0], -1)
            vis.grid(Pq, 'stone_rough', uv, lambda Q, side=side: np.concatenate([nn[:, None, 0:1].repeat(2, 1) * side, np.zeros((len(nn), 2, 1)), nn[:, None, 1:2].repeat(2, 1) * side], -1))
    return vis, col, info


def chunks(P, y, length=48.0):
    """Split dense circuit points into ~length pieces sharing their end points (per-chunk LOD/culling in Unity).
    Returns [(P_chunk, y_chunk, s0)]."""
    s = np.concatenate([[0], np.cumsum(np.linalg.norm(np.diff(P, axis=0), axis=1))])
    n = max(1, int(round(s[-1] / length))); edges = np.linspace(0, s[-1], n + 1); out = []
    for a, b in zip(edges[:-1], edges[1:]):
        idx = np.where((s >= a - 1e-6) & (s <= b + 1e-6))[0]
        if len(idx) < 2: continue
        lo, hi = max(idx[0] - 0, 0), min(idx[-1] + 1, len(P) - 1)       # overlap one sample so pieces meet
        out.append((P[lo:hi + 1], y[lo:hi + 1], float(s[lo])))
    return out


def multi_arch_gate(name, centre, yaw, openings=((-9.4, 4.2, 4.8), (0.0, 5.0, 5.4), (9.4, 4.2, 4.8)), depth=12.0, top=None, pad=4.0, lod=0):
    """One stone gatehouse (석축 대) with several 홍예 passages (e.g. 광화문: three). openings = (offset, width, height).
    yaw: 0/180 -> passages run along z (world). Returns (vis, col, info)."""
    cx, cy, cz = centre; top = top if top is not None else cy + 7.2
    ca, sa = math.cos(math.radians(yaw)), math.sin(math.radians(yaw))
    L = lambda x, y, z: np.array([cx + x * ca + z * sa, y, cz - x * sa + z * ca])
    vis = MB(name); col = MB(name + '_collision'); seg = [16, 10, 6][lod]
    xs = sorted([(o - w / 2, o + w / 2, w, h) for o, w, h in openings])
    lo = xs[0][0] - pad; hi = xs[-1][1] + pad
    # solid piers between/around the passages (full height) and spandrel masses above each arch
    edges = [lo] + [v for a in xs for v in (a[0], a[1])] + [hi]
    for i in range(0, len(edges), 2):
        a, b = edges[i], edges[i + 1]
        if b - a < .05: continue
        vis.box(L((a + b) / 2, (cy - 1 + top) / 2, 0), [b - a, top - cy + 1, depth], 'stone_dressed', yaw=yaw, faces='xXzZY', uvscale=2.0)
        col.box(L((a + b) / 2, (cy - 1 + top) / 2, 0), [b - a, top - cy + 1, depth], 'c', yaw=yaw, faces='xXzZY')
    for a, b, w, h in xs:
        half = w / 2; mid = (a + b) / 2; spring = cy + h - half; ang = np.linspace(0, math.pi, seg + 1)
        vis.box(L(mid, (spring + half + top) / 2 + .05, 0), [w, top - spring - half, depth], 'stone_dressed', yaw=yaw, faces='zZY', uvscale=2.0)
        col.box(L(mid, (spring + half + top) / 2 + .05, 0), [w, top - spring - half, depth], 'c', yaw=yaw, faces='xXzZyY')
        for zf in (-depth / 2, depth / 2):
            o = L(0, 0, 1 if zf > 0 else -1) - L(0, 0, 0)
            for a0, a1 in zip(ang[:-1], ang[1:]):
                p0 = L(mid + half * math.cos(a0), spring + half * math.sin(a0), zf); p1 = L(mid + half * math.cos(a1), spring + half * math.sin(a1), zf)
                t0 = L(mid + (half if math.cos(a0) > .7 else -half if math.cos(a0) < -.7 else half * math.cos(a0)), spring + half, zf)
                t1 = L(mid + (half if math.cos(a1) > .7 else -half if math.cos(a1) < -.7 else half * math.cos(a1)), spring + half, zf)
                vis.quad(p0, p1, t1, t0, 'stone_dressed', o, uvscale=2.0)
        for a0, a1 in zip(ang[:-1], ang[1:]):
            p0 = L(mid + half * math.cos(a0), spring + half * math.sin(a0), -depth / 2); p1 = L(mid + half * math.cos(a1), spring + half * math.sin(a1), -depth / 2)
            q0 = L(mid + half * math.cos(a0), spring + half * math.sin(a0), depth / 2); q1 = L(mid + half * math.cos(a1), spring + half * math.sin(a1), depth / 2)
            vis.quad(p0, p1, q1, q0, 'stone_plain', L(mid, spring, 0) - (p0 + q1) / 2, uvscale=1.5)
    vis.box(L((lo + hi) / 2, top + .08, 0), [hi - lo + .3, .16, depth + .3], 'stone_plain', yaw=yaw, faces='xXzZY', uvscale=2.0)
    passages = [[L((a + b) / 2, cy, -depth / 2).tolist(), L((a + b) / 2, cy, depth / 2).tolist()] for a, b, w, h in xs]
    return vis, col, dict(top=top, passages=passages, width=hi - lo)


def gate_door(width, height, lod=0, overlap=.10, thick=.14):
    """판문 pair for a 홍예 passage, mounted just in front of the passage's inner face (they swing inward, clear of the
    vault). Door-root frame: origin on the floor at the passage centre line, x across, +z = inside (swing side).
    Returns dict(left, right, bar) of MBs + info. Leaves are built about their hinge (leaf-local x from the hinge
    toward the centre, z = thickness centre). The 빗장 is a short bar across the meeting stiles, carried in iron keepers:
    it slides onto the left leaf (clearing the right leaf's keeper) and swings open with it (its parent in Unity).
    Outline = rectangle up to the spring + the arch arc enlarged by `overlap` (no light gaps)."""
    half = width / 2; R = half + overlap; spring = height - half; hinge = R
    seg = [10, 6, 3][lod]
    def top(x):                                                    # leaf top at door-root x
        return spring + math.sqrt(max(R * R - x * x, 0.0)) - .02
    def leaf(side):                                                # side -1 = left (hinge at -R), +1 = right
        mb = MB('leaf'); w = R - .006
        us = np.linspace(0, w, seg + 1)                            # leaf-local u from the hinge
        xs = -R + us if side < 0 else R - us                       # door-root x of each sample
        outline = [(u, .02) for u in (us[0], us[-1])] + [(u, top(x)) for u, x in zip(us[::-1], xs[::-1])]
        U = lambda u: u if side < 0 else -u                        # right leaf extends toward -x from its hinge
        for zf in (-thick / 2, thick / 2):
            pts = [[U(u), y, zf] for u, y in outline]
            mb.poly(pts, 'wood_board', [0, 0, 1 if zf > 0 else -1], uvscale=1.2)
        ring = outline + outline[:1]
        for (u0, y0), (u1, y1) in zip(ring[:-1], ring[1:]):        # edge strip
            a = [U(u0), y0, -thick / 2]; b = [U(u1), y1, -thick / 2]; c = [U(u1), y1, thick / 2]; d = [U(u0), y0, thick / 2]
            mid = np.array([U((u0 + u1) / 2), (y0 + y1) / 2, 0]); cen = np.array([U(w / 2), spring * .6, 0])
            mb.quad(a, b, c, d, 'wood_board', mid - cen, uvscale=1.2)
        if lod < 2:                                                # 띠장 (battens) on the inside face, iron bands outside
            for yb in (.45, 1.15 - .22, spring * .92):
                umax = w - .03
                mb.box([U(umax / 2), yb, thick / 2 + .035], [umax, .16, .07], 'wood_dark', faces='xXyYZ', uvscale=1.0)
            if lod == 0:
                for yb in (.35, spring * .55, spring * .95):
                    mb.box([U((w - .02) / 2), yb, -thick / 2 - .012], [w - .02, .07, .024], 'iron', faces='xXyYz')
                for k in range(3):                                 # 돌쩌귀 (hinge straps)
                    yh = .3 + k * (spring - .3) / 2
                    mb.box([U(.28), yh, thick / 2 + .02], [.56, .09, .04], 'iron', faces='xXyYZ')
                for uk in ((w - .3, w - 1.3) if side < 0 else (w - .35,)):             # bar keepers (고리)
                    mb.box([U(uk), 1.15, thick / 2 + .07 + .01], [.1, .26, .14], 'iron', faces='xXyYZz')
        return mb
    # bar mesh in door-root coordinates (centred on the meeting stiles); Unity parents it to the left leaf
    bar = MB("bar"); by = 1.15; bz = thick / 2 + .07 + .09; blen = min(1.6, width * .7)
    bar.box([0, by, bz], [blen, .18, .16], 'wood_dark', faces='xXyYzZ', uvscale=1.0)
    if lod == 0:
        for xb in (-blen / 2 + .1, blen / 2 - .1): bar.box([xb, by, bz], [.05, .2, .18], 'iron', faces='xXyYzZ')
    slide = -(blen / 2 + .05)                                       # fully onto the left leaf, clear of the right keeper
    info = dict(hinge=hinge, thick=thick, bar=[0, by, bz], barOpen=[slide, 0, 0], barParent='left',
                blocker=dict(centre=[0, (height + overlap + .3) / 2 - .1, 0], size=[2 * R + .2, height + overlap + .3, .34]), interaction=[0, 0, 1.5])
    return dict(left=leaf(-1), right=leaf(1), bar=bar), info


def rest_altar(lod=0):
    """성황당 제단: stone offering slab on two legs + two iron candle stands (the candle flame is the only emissive part,
    ART 광원 상등 — 성황당 촛불). Local frame: origin on the ground at the slab centre, +z = the side the player stands."""
    vis = MB('altar'); col = MB('altar_collision')
    vis.box([0, .64, 0], [1.3, .14, .66], 'stone_plain', faces='xXyYzZ', uvscale=1.0)
    for sx in (-.42, .42): vis.box([sx, .1, 0], [.26, 1.0, .5], 'stone_plain', faces='xXzZ', uvscale=1.0)   # 고석 legs buried .4 m (slopes)
    col.box([0, .16, 0], [1.3, 1.12, .66], 'c', faces='xXyYzZ')
    # 서낭 돌탑 behind the altar: the readable 성황당 silhouette from the yard (stones rest on the ones below)
    rng = np.random.default_rng(297); cz = -1.25; nu, nv = [(8, 5), (6, 4), (4, 3)][lod]
    layers = [(7, .60, .0, .28), (6, .44, .30, .26), (5, .30, .56, .23), (3, .17, .80, .20), (1, 0.0, 1.0, .17)]
    for n, ring, y0, r in layers:
        for k in range(n if lod < 2 else min(n, 2)):
            a0 = 2 * math.pi * k / n + rng.uniform(-.3, .3); c = np.array([ring * math.cos(a0), y0 + r * .55, cz + ring * math.sin(a0)])
            rr = np.array([r * rng.uniform(.85, 1.15), r * rng.uniform(.62, .78), r * rng.uniform(.75, 1.0)]); yaw = rng.uniform(0, math.pi)
            th, ph = np.meshgrid(np.linspace(0, 2 * math.pi, nu + 1), np.linspace(-math.pi / 2, math.pi / 2, nv + 1), indexing='ij')
            # irregular field stone: low-order lumps + flat-ish bed and top (never a smooth egg)
            f = 1 + sum(rng.uniform(-.17, .17) * np.cos(m * th + rng.uniform(0, 6.3)) * np.cos(ph) for m in (2, 3, 5)) + rng.uniform(-.1, .1) * np.sin(2 * ph)
            f *= 1 - .12 * np.abs(np.sin(ph)) ** 3
            e = np.stack([np.cos(ph) * np.cos(th) * rr[0] * f, np.sin(ph) * rr[1] * f, np.cos(ph) * np.sin(th) * rr[2] * f], -1)
            P = np.stack([c[0] + e[..., 0] * math.cos(yaw) + e[..., 2] * math.sin(yaw), c[1] + e[..., 1], c[2] - e[..., 0] * math.sin(yaw) + e[..., 2] * math.cos(yaw)], -1)
            uv = np.stack([th / (2 * math.pi) * r * 2.5, (ph + math.pi / 2) / math.pi * r * 1.2], -1)
            vis.grid(P, 'fieldstone', uv, lambda Q, c=c: Q - c)
    col.box([0, .6, cz], [1.3, 1.2, 1.3], 'c', faces='xXyYzZ')
    if lod < 2:
        for sx in (-.4, .4):
            vis.cyl([sx, .71, -.1], [sx, .74, -.1], .07, .06, 8, 'iron', caps=True)
            vis.cyl([sx, .74, -.1], [sx, .95, -.1], .012, .012, 6, 'iron')
            vis.cyl([sx, .95, -.1], [sx, .97, -.1], .05, .05, 8, 'iron', caps=True)
            vis.cyl([sx, .97, -.1], [sx, 1.13, -.1], .03, .028, 10, 'candle', caps=True)
            vis.cyl([sx, 1.135, -.1], [sx, 1.20, -.1], .017, .002, 6, 'flame')
    else:
        for sx in (-.4, .4): vis.box([sx, .9, -.1], [.06, .4, .06], 'iron', faces='xXzZY')
    return vis, col
