"""#308 cliff way (벼랑 잔도, D308-38, SPEC-WORLD-CLIFFWAY-308) — a path cut into a near-vertical granite shell that stands in
front of the steep lake slope of the Hyeongang cove (work id hyeongang_cove).

Nothing here edits terrain or water: the shell is a mesh skin outside the 4 m height field (read only), the lake below is the
existing one (surface 150 m). Geometry is written in Unity world coordinates (x, y up, z); triangles are wound so that
cross(b - a, c - a) points along the vertex normals (Unity front face).

Polar frame around the cove centre C: a station is an angle th; r grows from the water toward the land.
Per station profile (r, y), lake side first:
  A base under water -> B path outer edge -> [tread] -> C path inner edge -> E wall top -> F cap back to the real slope.
The path leaves the plateau rim (west), goes down the face on cut steps, runs 40-45 m above the water and lands on the east spur.

  python Tools/Art/cliffway308.py            -> Art/World/Compact/Rebuild/CliffWay308/{Meshes/*.json, layout.json, Preview/*.png}
Numbers: Tools/Unity/Stage308_cliffway/cliffway308.json
"""
from __future__ import annotations
import json, math, sys
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[2]
CFG = json.loads((ROOT / 'Tools/Unity/Stage308_cliffway/cliffway308.json').read_text(encoding='utf-8'))
OUT = ROOT / 'Art/World/Compact/Rebuild/CliffWay308'
HEIGHT = ROOT / 'Oheangbu/Assets/_Project/Art/World/CliffBoundary308/Surface/height_t1.bytes'
CELL, GW, GH = 4.0, 1001, 1501
HF = np.fromfile(HEIGHT, dtype=np.float32).reshape(GH, GW)


def _cell(x, z):
    gx = np.clip(np.asarray(x, float) / CELL, 0, GW - 1.001); gz = np.clip(np.asarray(z, float) / CELL, 0, GH - 1.001)
    i = gx.astype(int); j = gz.astype(int); fx = gx - i; fz = gz - j
    return HF[j, i], HF[j, i + 1], HF[j + 1, i], HF[j + 1, i + 1], fx, fz


def ground(x, z):
    a, b, c, d, fx, fz = _cell(x, z)
    return a * (1 - fx) * (1 - fz) + b * fx * (1 - fz) + c * (1 - fx) * fz + d * fx * fz


def ground_hi(x, z):
    """Upper bound of the game terrain in the cell (either triangulation)."""
    a, b, c, d, fx, fz = _cell(x, z)
    return a * (1 - fx) * (1 - fz) + b * fx * (1 - fz) + c * (1 - fx) * fz + d * fx * fz + np.abs(a + d - b - c) / 4


def smooth(a, k):
    k = max(1, int(round(k)))
    if k <= 1: return np.asarray(a, float).copy()
    w = np.hanning(2 * k + 1); w /= w.sum()
    return np.convolve(np.pad(np.asarray(a, float), k, mode='edge'), w, mode='valid')


class Way:
    def __init__(self, c):
        self.c = c
        C = np.array(c['centre'], float); self.C = C
        water = c['water']; hw = c['width'] / 2; self.hw = hw
        # ---- waterline radius per fine angle (first r where the ground rises above the water)
        th = np.radians(np.arange(c['theta0'], c['theta1'] - 1e-6, -.25)); self.th = th
        d = np.stack([np.cos(th), np.sin(th)], -1); self.d = d
        rr = np.arange(20., 260., .5)
        G = ground(C[0] + d[:, None, 0] * rr[None], C[1] + d[:, None, 1] * rr[None])
        rwl = rr[np.argmax(G >= water + .5, axis=1)]
        rwl = smooth(rwl, 30)
        # ---- path radius: shore offset in the traverse, out to the rim start and in to the spur landing
        n = len(th); off = np.full(n, c['shoreOffset'], float)
        rp = rwl + off
        r_start = c['start']['r']; a0 = c['start']['blendDeg']; a1 = c['landing']['blendDeg']
        t0 = np.clip((np.degrees(th[0]) - np.degrees(th)) / a0, 0, 1); e0 = t0 * t0 * (3 - 2 * t0)
        rp = r_start * (1 - e0) + rp * e0
        t1 = np.clip((np.degrees(th) - np.degrees(th[-1])) / a1, 0, 1); e1 = t1 * t1 * (3 - 2 * t1)
        rp = (rwl + c['landing']['offset']) * (1 - e1) + rp * e1
        rp = smooth(rp, 60)
        xz = C + d * rp[:, None]
        s = np.r_[0, np.cumsum(np.linalg.norm(np.diff(xz, axis=0), axis=1))]
        # ---- resample by arc length
        ds = c['station']; S = np.arange(0, s[-1], ds); self.S = S; N = len(S); self.N = N
        self.thS = np.interp(S, s, th); self.rp = np.interp(S, s, rp); self.rwl = np.interp(S, s, rwl)
        self.dS = np.stack([np.cos(self.thS), np.sin(self.thS)], -1)             # radial unit (toward the land)
        self.xz = C + self.dS * self.rp[:, None]
        t = np.gradient(self.xz, axis=0); t /= np.linalg.norm(t, axis=1, keepdims=True); self.t = t
        # ---- walk height: designed profile (key points by arc fraction), never under the ground across the tread
        key = np.array(c['profile'], float)                                       # [fraction, height]
        yd = np.interp(S / S[-1], key[:, 0], key[:, 1])
        us = np.linspace(-hw - .2, hw + .2, 7)
        gmax = np.max([ground_hi(*(self.xz + self.dS * u).T) for u in us], axis=0)
        y = np.maximum(smooth(yd, 2.0 / ds), gmax + .12)
        g_end0 = gmax[0] + .12; g_end1 = gmax[-1] + .12
        y[0] = g_end0; y[-1] = g_end1
        for _ in range(4):                                                        # grade limit (lift only)
            for i in range(1, N): y[i] = max(y[i], y[i - 1] - c['maxGrade'] * ds)
            for i in range(N - 2, -1, -1): y[i] = max(y[i], y[i + 1] - c['maxGrade'] * ds)
        y = np.maximum(smooth(y, .8 / ds), gmax + .12)
        self.y = y; self.gmax = gmax
        self.fill = y - gmax                                                      # path above the hidden ground
        # ---- wall top: designed height above the path, never above the hill behind it (so the cap can run back into it)
        back = np.arange(2., c['capReach'], 2.)
        rC = self.rp + hw
        Gb = ground(self.C[0] + self.dS[:, None, 0] * (rC[:, None] + back[None]), self.C[1] + self.dS[:, None, 1] * (rC[:, None] + back[None]))
        hill = Gb.max(axis=1)
        frac = S / S[-1]
        wk = np.array(c['wallAbove'], float)
        W = np.interp(frac, wk[:, 0], wk[:, 1])
        T = np.minimum(y + W, hill - 1.0)
        T = np.maximum(T, y + .05)
        T = smooth(T, 6.0 / ds); T = np.minimum(T, hill - .5); T = np.maximum(T, y + .05)
        self.T = T; self.back = back; self.Gb = Gb
        self.rE = rC + c['batterUp'] * (T - y)
        # where the cap meets the hill: first r behind E where the ground reaches the cap height
        rj = np.zeros(N)
        for i in range(N):
            rb = np.arange(self.rE[i] + 1.0, self.rE[i] + c['capReach'], 1.0)
            g = ground(self.C[0] + self.dS[i, 0] * rb, self.C[1] + self.dS[i, 1] * rb)
            k = np.argmax(g >= T[i] - .8)
            rj[i] = rb[k] if g[k] >= T[i] - .8 else rb[-1]
        self.rj = smooth(rj, 4.0 / ds)
        self.drop3 = y - ground(*(self.xz - self.dS * (hw + 3.0)).T)             # to the hidden ground / lake bed 3 m outside
        self.drop3w = y - np.maximum(ground(*(self.xz - self.dS * (hw + 3.0)).T), water)

    # world point for station arrays
    def P(self, i, r, y):
        return np.stack([self.C[0] + self.dS[i, 0] * r, np.asarray(y, float) + 0 * r, self.C[1] + self.dS[i, 1] * r], -1)


class Mesh:
    def __init__(self, name): self.name = name; self.V = []; self.Nn = []; self.UV = []; self.T = {}; self.n = 0

    def add(self, P, Nn, UV, tris, mat):
        P = np.asarray(P, float).reshape(-1, 3); Nn = np.asarray(Nn, float).reshape(-1, 3); UV = np.asarray(UV, float).reshape(-1, 2)
        tris = np.asarray(tris, int).reshape(-1, 3)
        a, b, c = P[tris[:, 0]], P[tris[:, 1]], P[tris[:, 2]]
        fn = np.cross(b - a, c - a); nm = Nn[tris[:, 0]] + Nn[tris[:, 1]] + Nn[tris[:, 2]]
        flip = np.einsum('ij,ij->i', fn, nm) < 0
        tris = np.where(flip[:, None], tris[:, ::-1], tris)
        keep = np.linalg.norm(fn, axis=1) > 1e-9
        self.V.append(P); self.Nn.append(Nn); self.UV.append(UV)
        self.T.setdefault(mat, []).append(tris[keep] + self.n); self.n += len(P)

    def grid(self, P, out, mat, uvscale=4.0, uv=None):
        """P (nu, nv, 3); normals from tangents, oriented to `out` (nu, nv, 3) or a vector."""
        P = np.asarray(P, float); nu, nv = P.shape[:2]
        du = np.gradient(P, axis=0); dv = np.gradient(P, axis=1); n = np.cross(du, dv)
        ln = np.linalg.norm(n, axis=-1, keepdims=True); n = n / np.maximum(ln, 1e-9)
        o = np.broadcast_to(np.asarray(out, float), P.shape)
        sgn = np.sign(np.einsum('ijk,ijk->ij', n, o)); sgn[sgn == 0] = 1; n = n * sgn[..., None]
        if uv is None:
            su = np.r_[0, np.cumsum(np.linalg.norm(np.diff(P[:, nv // 2], axis=0), axis=1))]
            sv = np.concatenate([np.zeros((nu, 1)), np.cumsum(np.linalg.norm(np.diff(P, axis=1), axis=2), axis=1)], 1)
            uv = np.stack([np.broadcast_to(su[:, None], (nu, nv)), sv], -1) / uvscale
        ii, jj = np.meshgrid(np.arange(nu - 1), np.arange(nv - 1), indexing='ij')
        a = (ii * nv + jj).ravel(); b = a + nv; c = b + 1; d = a + 1
        tris = np.concatenate([np.stack([a, b, c], -1), np.stack([a, c, d], -1)])
        self.add(P, n, uv, tris, mat)

    def box(self, c, ax, ay, az, size, mat, faces='xXyYzZ', uvscale=1.5):
        c = np.asarray(c, float); A = [np.asarray(ax, float), np.asarray(ay, float), np.asarray(az, float)]; h = np.asarray(size, float) / 2
        for k, (lo, hi) in enumerate((('x', 'X'), ('y', 'Y'), ('z', 'Z'))):
            for sgn, f in ((-1, lo), (1, hi)):
                if f not in faces: continue
                u, v = A[(k + 1) % 3], A[(k + 2) % 3]; hu, hv = h[(k + 1) % 3], h[(k + 2) % 3]
                o = c + A[k] * h[k] * sgn
                P = [o - u * hu - v * hv, o + u * hu - v * hv, o + u * hu + v * hv, o - u * hu + v * hv]
                uv = np.array([[0, 0], [2 * hu, 0], [2 * hu, 2 * hv], [0, 2 * hv]]) / uvscale
                self.add(P, np.repeat((A[k] * sgn)[None], 4, 0), uv, [[0, 1, 2], [0, 2, 3]], mat)

    def data(self):
        V = np.concatenate(self.V) if self.V else np.zeros((0, 3)); Nn = np.concatenate(self.Nn) if self.V else V; UV = np.concatenate(self.UV) if self.V else np.zeros((0, 2))
        return dict(name=self.name, vertices=np.round(V, 4).ravel().tolist(), normals=np.round(Nn, 4).ravel().tolist(), uvs=np.round(UV, 4).ravel().tolist(),
                    submeshes=[dict(material=m, triangles=np.concatenate(t).ravel().tolist()) for m, t in self.T.items()])

    def count(self): return self.n, sum(len(np.concatenate(t)) for t in self.T.values())


def rock(S, Y, seed, c):
    """Radial displacement (m, + = toward the lake) of the face at arc S and height Y (arrays of equal shape)."""
    rng = np.random.default_rng(seed); d = np.zeros_like(S, float)
    for wl_s, wl_y, amp in c['bulges']:                                           # big slabs
        for _ in range(3):
            d += amp / 3 * np.sin(S * 2 * math.pi / (wl_s * rng.uniform(.7, 1.3)) + rng.uniform(0, 6.28)) * np.sin(Y * 2 * math.pi / (wl_y * rng.uniform(.7, 1.3)) + rng.uniform(0, 6.28))
    warp = 1.2 * np.sin(Y * .21 + 1.3) + .6 * np.sin(Y * .57)                     # joints lean and wander with height
    for wl, depth, sharp in c['joints']:                                          # vertical joints
        ph = rng.uniform(0, 6.28)
        d -= depth * (1 - np.abs(np.sin((S + warp) * math.pi / wl + ph))) ** sharp
    for wl, depth in c['beds']:                                                   # bedding ledges (saw step in height)
        ph = rng.uniform(0, wl)
        f = ((Y + ph + .8 * np.sin(S * .05)) % wl) / wl
        d += depth * (f - .5)
    return d


def build_shell(w: Way, lod):
    c = w.c; water = c['water']; hw = w.hw
    step = (c['station'] * 2, 1.0) if lod == 0 else (c['station'] * 10, 5.0)
    st = max(1, int(round(step[0] / c['station']))); I = np.arange(0, w.N, st)
    if I[-1] != w.N - 1: I = np.r_[I, w.N - 1]
    S = w.S[I]; y = w.y[I]; T = w.T[I]; rp = w.rp[I]; nI = len(I)
    m = Mesh('CliffWay_Shell_LOD%d' % lod)
    radial = np.stack([w.dS[I, 0], np.zeros(nI), w.dS[I, 1]], -1)
    # ---- lower wall: from under the water up to the outer edge
    base = water - c['baseDepth']
    nv = max(4, int(round((np.max(y) - base) / step[1])))
    f = np.linspace(0, 1, nv)[None]                                               # 0 = base, 1 = path edge
    Y = base + (y[:, None] - .14 - base) * f
    R = (rp[:, None] - hw) - c['batterDown'] * (y[:, None] - Y)
    D = rock(np.broadcast_to(S[:, None], Y.shape), Y, c['seed'], c)
    fade = np.clip((y[:, None] - Y) / 1.6, 0, 1)                                  # no displacement at the path edge
    R = R - D * fade
    Pl = np.stack([w.C[0] + w.dS[I, 0][:, None] * R, Y, w.C[1] + w.dS[I, 1][:, None] * R], -1)
    m.grid(Pl, -radial[:, None, :], 'cliff_rock', uvscale=6.0)
    # ---- upper wall: from the inner edge up to the top
    nv2 = max(3, int(round(np.max(T - y) / step[1])))
    f = np.linspace(0, 1, nv2)[None]
    Y = (y[:, None] - .14) + (T[:, None] - y[:, None] + .14) * f
    R = (rp[:, None] + hw) + c['batterUp'] * (Y - y[:, None])
    D = rock(np.broadcast_to(S[:, None], Y.shape), Y, c['seed'] + 1, c)
    above = Y - y[:, None]
    lim = np.where(above < c['headroom'], 0.0, c['overhang'])                     # never into the walkway under the headroom
    D = np.minimum(D, lim) * np.clip(above / .5, 0, 1) * np.clip((T[:, None] - Y) / 2.0 + .25, 0, 1)
    R = R - D
    Pu = np.stack([w.C[0] + w.dS[I, 0][:, None] * R, Y, w.C[1] + w.dS[I, 1][:, None] * R], -1)
    m.grid(Pu, -radial[:, None, :], 'cliff_rock', uvscale=6.0)
    # ---- cap: from the wall top back into the hill (dome, never under the ground)
    nc = 14 if lod == 0 else 7
    g = np.linspace(0, 1, nc)[None]
    rE = Pu[:, -1]; rE_r = (rE[:, 0] - w.C[0]) * w.dS[I, 0] + (rE[:, 2] - w.C[1]) * w.dS[I, 1]
    rj = np.maximum(w.rj[I], rE_r + 3.0)
    R = rE_r[:, None] + (rj[:, None] + 5.0 - rE_r[:, None]) * g ** 1.3
    X = w.C[0] + w.dS[I, 0][:, None] * R; Z = w.C[1] + w.dS[I, 1][:, None] * R
    G = ground_hi(X, Z)
    span = np.maximum(rj - rE_r, 3.0)[:, None]
    dome = c['dome'] * np.minimum(span / 30.0, 1.0) * np.sin(np.clip((R - rE_r[:, None]) / span, 0, 1) * math.pi) ** .8
    Yc = np.maximum(T[:, None] + dome, G + .5)
    Yc[:, -1] = G[:, -1] - 1.2; Yc[:, -2] = np.minimum(Yc[:, -2], G[:, -2] + .5)
    Yc[:, 0] = rE[:, 1]
    Pc = np.stack([X, Yc, Z], -1)
    m.grid(Pc, np.array([0., 1., 0.]), 'cliff_cap', uvscale=6.0)
    return m, dict(lower=Pl, upper=Pu, cap=Pc, I=I)


def stair_runs(w: Way):
    """Tread boxes [s0, s1, top]: equal risers <= maxRiser where the grade is over .10, 1.6 m flags elsewhere."""
    c = w.c; S = w.S; y = w.y; out = []
    grade = np.gradient(smooth(y, 3), S)
    i = 0
    while i < w.N - 1:
        steep = abs(grade[i]) > .10; j = i
        while j + 1 < w.N - 1 and (abs(grade[j + 1]) > .10) == steep and np.sign(grade[j + 1]) == np.sign(grade[i]): j += 1
        a, b = S[i], S[j + 1]
        if steep:
            rise = y[j + 1] - y[i]; n = max(1, int(math.ceil(abs(rise) / c['maxRiser'])))
            lv = y[i] + rise * np.arange(n + 1) / n
            ss = np.linspace(a, b, 200); yy = np.interp(ss, S, y)
            mono = np.maximum.accumulate(yy * np.sign(rise)) * np.sign(rise)
            ed = np.interp(lv * np.sign(rise), mono * np.sign(rise) + np.arange(200) * 1e-9, ss)
            for k in range(n): out.append([float(ed[k]), float(ed[k + 1]), float((lv[k] + lv[k + 1]) / 2)])
        else:
            n = max(1, int(round((b - a) / 1.6))); ed = np.linspace(a, b, n + 1)
            for k in range(n): out.append([float(ed[k]), float(ed[k + 1]), float(np.interp((ed[k] + ed[k + 1]) / 2, S, y))])
        i = j + 1
    return [t for t in out if t[1] - t[0] > .03]


def frame(w: Way, s):
    x = np.interp(s, w.S, w.xz[:, 0]); z = np.interp(s, w.S, w.xz[:, 1])
    dx = np.interp(s, w.S, w.dS[:, 0]); dz = np.interp(s, w.S, w.dS[:, 1]); n = np.array([dx, 0, dz]); n /= np.linalg.norm(n)
    tx = np.interp(s, w.S, w.t[:, 0]); tz = np.interp(s, w.S, w.t[:, 1]); t = np.array([tx, 0, tz]); t /= np.linalg.norm(t)
    return np.array([x, 0, z]), t, n


def build_steps(w: Way, runs):
    m = Mesh('CliffWay_Steps'); hw = w.hw; up = np.array([0., 1., 0.])
    for a, b, top in runs:
        p, t, n = frame(w, (a + b) / 2)
        L = (b - a) + .04; th = .5
        m.box(p + up * (top - th / 2) + n * .04, t, up, n, (L, th, 2 * hw + .2), 'stone_plain', faces='xXYz')
    return m


def build_rail(w: Way):
    c = w.c; hw = w.hw; up = np.array([0., 1., 0.]); rng = np.random.default_rng(c['seed'] + 7)
    m = Mesh('CliffWay_Rail'); col = Mesh('CliffWay_Rail_Collision')
    S = w.S; gaps = [(g[0] * S[-1], g[0] * S[-1] + g[1]) for g in c['railGaps']]
    lead = c['railLead']
    spans = []; a = lead
    for g0, g1 in sorted(gaps):
        spans.append((a, g0)); a = g1
    spans.append((a, S[-1] - lead))
    posts = []
    for a, b in spans:
        if b - a < 1.0: continue
        n = max(1, int(round((b - a) / c['postSpacing']))); ss = np.linspace(a, b, n + 1)
        prev = None
        for s in ss:
            p, t, nrm = frame(w, s); yb = float(np.interp(s, S, w.y))
            base = p + up * yb - nrm * (hw - .10)
            m.box(base + up * .56, t, up, nrm, (.065, 1.2, .065), 'iron')
            m.box(base + up * 1.18, t, up, nrm, (.10, .05, .10), 'iron')
            if prev is not None:
                for hgt, sag in ((1.02, .10), (.58, .13)):
                    k = 7; f = np.linspace(0, 1, k + 1)
                    pts = prev[None] * (1 - f[:, None]) + base[None] * f[:, None] + up * hgt - up * (sag * 4 * f * (1 - f))[:, None]
                    for q0, q1 in zip(pts[:-1], pts[1:]):
                        ax = q1 - q0; L = np.linalg.norm(ax); ax /= L
                        side = np.cross(ax, up); side /= np.linalg.norm(side); v = np.cross(side, ax)
                        m.box((q0 + q1) / 2, ax, v, side, (L + .01, .026, .026), 'iron', faces='yYzZ')
                    # red cloth strips and locks hung on the upper chain
                    if hgt > 1.0:
                        for _ in range(rng.integers(1, 7)):
                            ff = rng.uniform(.1, .9); q = prev * (1 - ff) + base * ff + up * (hgt - sag * 4 * ff * (1 - ff))
                            ln = rng.uniform(.3, .8); tw = rng.uniform(-.6, .6)
                            a2 = t * math.cos(tw) + nrm * math.sin(tw)
                            m.box(q - up * (ln / 2 + .02), a2, up, np.cross(a2, up), (.085, ln, .008), 'cloth_red', faces='zZ')
                        for _ in range(rng.integers(0, 3)):
                            ff = rng.uniform(.1, .9); q = prev * (1 - ff) + base * ff + up * (hgt - sag * 4 * ff * (1 - ff))
                            m.box(q - up * .06, t, up, nrm, (.05, .07, .025), 'brass')
                # collider: one thin slab per bay, inside the chain's plane
                mid = (prev + base) / 2; ax = base - prev; L = np.linalg.norm(ax); ax /= L
                side = np.cross(ax, up); side /= np.linalg.norm(side); v = np.cross(side, ax)
                col.box(mid + v * .56, ax, v, side, (L, 1.12, .05), 'collision')
            prev = base; posts.append(base.tolist())
    # warning rope (금줄) posts before each gap, both sides
    for g0, g1 in gaps:
        for s in (g0 - 6.0, g1 + 6.0):
            if s < 2 or s > S[-1] - 2: continue
            p, t, nrm = frame(w, s); yb = float(np.interp(s, S, w.y))
            q = p + up * yb + nrm * (hw - .18)
            m.box(q + up * .9, t, up, nrm, (.07, 1.8, .07), 'wood_dark')
            for k in range(5): m.box(q + up * (1.65 - .02 * k) - nrm * (.12 + .1 * k), t, up, nrm, (.06, .28, .006), 'cloth_red', faces='xX')
    return m, col, posts, gaps


def build_collision(w: Way, shell1):
    c = w.c; hw = w.hw
    tread = Mesh('CliffWay_Tread_Collision')
    I = np.arange(0, w.N, 2)
    if I[-1] != w.N - 1: I = np.r_[I, w.N - 1]
    us = np.array([-hw - .1, hw + .12])
    P = np.stack([w.C[0] + w.dS[I, 0][:, None] * (w.rp[I][:, None] + us[None]), np.broadcast_to(w.y[I][:, None], (len(I), 2)).copy(), w.C[1] + w.dS[I, 1][:, None] * (w.rp[I][:, None] + us[None])], -1)
    tread.grid(P, np.array([0., 1., 0.]), 'collision')
    return tread


def previews(w: Way, parts):
    (OUT / 'Preview').mkdir(parents=True, exist_ok=True)
    x0, x1, z0, z1 = 1820, 2180, 4780, 5080; sc = 3
    xs = np.arange(x0, x1, 1.0 / sc * 3); zs = np.arange(z0, z1, 1.0 / sc * 3)
    X, Z = np.meshgrid(xs, zs); G = ground(X, Z)
    img = np.clip((G - 130) / 150 * 200 + 40, 0, 255).astype(np.uint8); rgb = np.stack([img] * 3, -1); rgb[G < w.c['water']] = (60, 90, 150)
    im = Image.fromarray(rgb[::-1]).resize((len(xs) * 3, len(zs) * 3)); d = ImageDraw.Draw(im)
    P2 = lambda x, z: ((x - x0) * sc, (z1 - z) * sc)
    for name, col in (('lower', (200, 60, 60)), ('cap', (240, 200, 60))):
        Pm = parts[name]
        edge = Pm[:, 0] if name == 'lower' else Pm[:, -1]
        d.line([P2(p[0], p[2]) for p in edge], fill=col, width=2)
    d.line([P2(p[0], p[1]) for p in w.xz], fill=(255, 255, 255), width=3)
    for k in range(0, w.N, int(20 / w.c['station'])):
        p = w.xz[k]; d.text(P2(p[0] + 3, p[1]), '%d m y%.0f' % (w.S[k], w.y[k]), fill=(255, 255, 0))
    im.save(OUT / 'Preview/plan.png')
    # sections
    W, Hh = 520, 520; secs = [.08, .25, .45, .65, .85, .97]
    sheet = Image.new('RGB', (W * 3, Hh * 2), (245, 242, 235)); dd = ImageDraw.Draw(sheet)
    Iarr = parts['I']
    for q, fr in enumerate(secs):
        k = int(np.argmin(np.abs(w.S[Iarr] - fr * w.S[-1]))); i = Iarr[k]
        ox, oy = (q % 3) * W, (q // 3) * Hh
        r0 = w.rp[i] - 40; y0 = w.c['water'] - 20; s = 3.2
        T2 = lambda r, y: (ox + (r - r0) * s, oy + Hh - 10 - (y - y0) * s)
        rr = np.arange(r0, r0 + 160, 1.0); gg = ground(w.C[0] + w.dS[i, 0] * rr, w.C[1] + w.dS[i, 1] * rr)
        dd.rectangle([ox, T2(r0, w.c['water'])[1], ox + W - 1, T2(r0, y0)[1]], fill=(150, 180, 215))
        dd.polygon([T2(rr[0], y0)] + [T2(a, b) for a, b in zip(rr, gg)] + [T2(rr[-1], y0)], fill=(205, 198, 185))
        for name in ('lower', 'upper', 'cap'):
            Pm = parts[name][k]; rad = (Pm[:, 0] - w.C[0]) * w.dS[i, 0] + (Pm[:, 2] - w.C[1]) * w.dS[i, 1]
            dd.line([T2(a, b) for a, b in zip(rad, Pm[:, 1])], fill=(40, 40, 40), width=2)
        dd.line([T2(w.rp[i] - w.hw, w.y[i]), T2(w.rp[i] + w.hw, w.y[i])], fill=(200, 40, 40), width=3)
        dd.text((ox + 8, oy + 8), 's %.0f m  path y %.1f  top %.1f  above water %.1f  fill %.1f' % (w.S[i], w.y[i], w.T[i], w.y[i] - w.c['water'], w.fill[i]), fill=(0, 0, 0))
    sheet.save(OUT / 'Preview/sections.png')


def main():
    c = CFG; w = Way(c)
    (OUT / 'Meshes').mkdir(parents=True, exist_ok=True)
    shell0, parts = build_shell(w, 0); shell1, _ = build_shell(w, 1)
    runs = stair_runs(w); steps = build_steps(w, runs)
    rail, railcol, posts, gaps = build_rail(w)
    tread = build_collision(w, shell1)
    shellcol = Mesh('CliffWay_Shell_Collision')
    for mat, tl in shell1.T.items():
        shellcol.V = shell1.V; shellcol.Nn = shell1.Nn; shellcol.UV = shell1.UV; shellcol.n = shell1.n; shellcol.T = {'collision': sum((list(t) for t in shell1.T.values()), [])}
    meshes = [shell0, shell1, steps, rail, railcol, tread, shellcol]
    stats = {}
    for m in meshes:
        (OUT / 'Meshes' / (m.name + '.json')).write_text(json.dumps(m.data()), encoding='utf-8')
        v, t = m.count(); stats[m.name] = dict(vertices=int(v), triangles=int(t))
    # face slope of the visible walls (undisplaced design): atan(1 / batter)
    grade = np.abs(np.gradient(w.y, w.S))
    above_water = w.y - c['water']
    risers = [abs(b[2] - a[2]) for a, b in zip(runs[:-1], runs[1:])]
    layout = dict(
        id=c['id'], length=float(w.S[-1]), stations=int(w.N), width=c['width'],
        start=[float(w.xz[0, 0]), float(w.y[0]), float(w.xz[0, 1])], end=[float(w.xz[-1, 0]), float(w.y[-1]), float(w.xz[-1, 1])],
        heightMin=float(w.y.min()), heightMax=float(w.y.max()), aboveWaterMax=float(above_water.max()),
        gradeMaxDeg=float(np.degrees(np.arctan(grade.max()))), riserMax=float(max(risers) if risers else 0),
        drop3ShareOver30=float(np.mean(w.drop3w >= 30.0)), drop3Median=float(np.median(w.drop3w)),
        wallAboveMax=float((w.T - w.y).max()), wallAboveMedian=float(np.median(w.T - w.y)), topAboveWaterMax=float((w.T - c['water']).max()),
        faceSlopeDeg=dict(below=float(np.degrees(np.arctan(1 / c['batterDown']))), above=float(np.degrees(np.arctan(1 / c['batterUp'])))),
        fillMax=float(w.fill.max()), railGaps=[[float(a), float(b)] for a, b in gaps], posts=len(posts), treads=len(runs), meshes=stats,
        path=[[float(w.xz[i, 0]), float(w.y[i]), float(w.xz[i, 1])] for i in range(0, w.N, max(1, int(2 / c['station'])))],
        materials=['cliff_rock', 'cliff_cap', 'stone_plain', 'iron', 'brass', 'cloth_red', 'wood_dark'])
    # shell outside the ground: sample the visible skins against the terrain upper bound
    poke = 0; tot = 0
    for name in ('lower', 'upper'):
        Pm = parts[name]; g = ground_hi(Pm[..., 0], Pm[..., 2]); vis = Pm[..., 1] > c['water']
        inside = (g > Pm[..., 1] + .05) & vis; tot += int(vis.sum()); poke += int(inside.sum())
    layout['skinUnderGroundShare'] = poke / max(1, tot)
    (OUT / 'layout.json').write_text(json.dumps(layout, indent=1), encoding='utf-8')
    previews(w, parts)
    print(json.dumps({k: v for k, v in layout.items() if k not in ('path',)}, indent=1))


if __name__ == '__main__':
    main()
