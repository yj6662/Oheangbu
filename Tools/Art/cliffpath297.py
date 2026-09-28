"""#297 cut cliff paths (벼랑 잔도길) — the #285 granite trail rebuilt in place on the steep stretches of the mountain MainPaths.

A narrow carved stone path runs along a jointed granite face: the uphill side is a tiered cliff (vertical joints, bedding
lines, overhang lips at the tier tops, ledges that run back until they meet the real slope), the valley side is a rocky
outer lip of set stones over a buried footing, and where the face is highest a short timber gallery (잔도) is hung off it.

Sites (TEST) — measured on the Watershed295 height field (hanok297_wall.H) across the MainPath, see `survey()`:
  hyeongang_gorge  mountain_hyeongang MainPath s 566-690: the path already runs on a ~5 m shelf; above it the wall rises
                   +24 m at 20 m and +35 m at 30 m (50-60°), the valley falls -13 m at 20 m. 3 tiers, 12 m gallery.
  hyeongang_stair  s 728-792: the path climbs (grade .32) under a 45-55° wall (+15..21 m at 20 m). Cut stone stair, 2 tiers.
  jeokro_crag      mountain_jeokro MainPath s 100-162 (main line): only ~27° (+10 m at 20 m, +15 m at 30 m); a low 2-tier crag
                   standing proud of the slope (the realm's 4-7 m block bands, stacked). Protected #293 cells: no terrain op.
None of these overlaps a #293 section (they coexist; hide = []). All three are unprotected except jeokro_crag.

World frame per station: c(s) centre (MainPath XZ, lightly smoothed), t tangent, n unit normal toward the cliff,
y(s) walk height = terrain maximum across the tread (+.10, triangle-safe, smoothed, grade-limited). Heights of every skin
that must hide the ground use the triangle-safe upper bound of the 4 m cell (Hhi); everything that must stay buried uses
the lower bound (Hlo) of the ground AFTER this compound's terrain ops (the gallery notch), so it is buried either way.

Walking: continuous ramp collider through the tread centres (risers <= .18 m, so the visible treads are within ±.09 m of
the feet); lip stones collide as boxes with gaps < the capsule diameter; the gallery deck and rail collide as thin slabs.
Outputs Finish297/CliffPath/: Meshes/*_LOD{0,1,2}.json + *_Collision.json, unity.json (meshes, routes, terrainOps),
layout.json (sites, stats, survey), Preview/*.png (plan + cross sections). Material slots: cliff_rock (face — NOT yet in
CompactFinish297.Kit Slots297: falls back to plain grey until mapped), stone_rough (lip, footing), stone_plain (treads),
wood_dark / wood_board / iron (gallery).
"""
from __future__ import annotations
import json, math, sys
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw

sys.path.insert(0, str(Path(__file__).resolve().parent))
from hanok297 import MB, beam, nz
import hanok297_wall as HW
import surface297

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Art/World/Compact/Rebuild/Finish297/CliffPath'
LAYOUT = ROOT / 'Oheangbu/Assets/_Project/Art/World/Watershed295/Surface/layout.json'
PROT = HW.SURF / 'protected.bytes'
CELL, GW, GH = 4.0, 1001, 1501
UP = np.array([0., 1., 0.])

SITES = [
    dict(id='hyeongang_gorge', mountain='mountain_hyeongang', realm='Hyeongang', s0=566., s1=690., lead=4., width=1.9,
         face=24., tiers=(.44, .31, .25), lean=(.03, .12), ledge=(5.0, 11.0), overhang=(.35, .85), joints=(2.2, 6.0),
         jdepth=(.18, .5), major=((7., 15.), (.8, 1.8), (.6, 1.2)), bulge=.30, seed=29701, gallery=(598., 610.)),
    dict(id='hyeongang_stair', mountain='mountain_hyeongang', realm='Hyeongang', s0=728., s1=792., lead=4., width=1.8,
         face=17., tiers=(.55, .45), lean=(.04, .13), ledge=(4.0, 9.0), overhang=(.3, .7), joints=(2.0, 5.2),
         jdepth=(.18, .45), major=((6., 12.), (.7, 1.5), (.5, 1.0)), bulge=.25, seed=29702, gallery=None),
    dict(id='jeokro_crag', mountain='mountain_jeokro', realm='Jeokro', s0=100., s1=162., lead=4., width=2.0,
         face=12., tiers=(.55, .45), lean=(.06, .16), ledge=(1.6, 3.6), overhang=(.12, .35), joints=(1.8, 4.2),
         jdepth=(.25, .55), major=((4.5, 9.), (.6, 1.3), (.5, .9)), bulge=.18, seed=29703, gallery=None),
]
TAPER = 9.0          # face height ramps up over this many metres inside [s0, s1]
FOOT = .45           # face foot buried below the walk
LIFT = .10           # walk surface above the triangle-safe terrain maximum across the tread
MAX_RISER = .17


# ------------------------------------------------------------------------------------------------------------------
# terrain
# ------------------------------------------------------------------------------------------------------------------
class Ground:
    """Bilinear 4 m field (same as CompactWorldSurface.Sample) with triangle-safe bounds: the game mesh triangulates each
    cell, which deviates from bilinear by at most |h00+h11-h10-h01|/4."""
    def __init__(self, h):
        self.h = h

    def _cell(self, x, z):
        gx = np.clip(np.asarray(x, float) / CELL, 0, GW - 1.001); gz = np.clip(np.asarray(z, float) / CELL, 0, GH - 1.001)
        i = gx.astype(int); j = gz.astype(int); fx = gx - i; fz = gz - j; h = self.h
        return h[j, i], h[j, i + 1], h[j + 1, i], h[j + 1, i + 1], fx, fz

    def b(self, x, z):
        a, b_, c, d, fx, fz = self._cell(x, z)
        return a * (1 - fx) * (1 - fz) + b_ * fx * (1 - fz) + c * (1 - fx) * fz + d * fx * fz

    def hi(self, x, z):
        a, b_, c, d, fx, fz = self._cell(x, z)
        return a * (1 - fx) * (1 - fz) + b_ * fx * (1 - fz) + c * (1 - fx) * fz + d * fx * fz + np.abs(a + d - b_ - c) / 4

    def lo(self, x, z):
        a, b_, c, d, fx, fz = self._cell(x, z)
        return a * (1 - fx) * (1 - fz) + b_ * fx * (1 - fz) + c * (1 - fx) * fz + d * fx * fz - np.abs(a + d - b_ - c) / 4


def protected_mask():
    return (np.fromfile(PROT, np.uint8).reshape(GH, GW) > 0) if PROT.exists() else np.zeros((GH, GW), bool)


def main_path(mid):
    L = json.loads(LAYOUT.read_text(encoding='utf-8-sig'))
    m = next(x for x in L['Mountains'] if x['Id'] == mid)
    P = np.array([[p['x'], p['y'], p['z']] for p in m['MainPath']], float)
    d = np.r_[0, np.cumsum(np.linalg.norm(np.diff(P[:, [0, 2]], axis=0), axis=1))]
    return P, d


def smooth_line(xz, k):
    """Gaussian smoothing (sigma k samples) with odd reflection at both ends: kinks of the 2 m MainPath go, the ends stay."""
    if k < 1: return xz
    r = int(3 * k); pad_l = 2 * xz[0] - xz[1:r + 1][::-1]; pad_r = 2 * xz[-1] - xz[-r - 1:-1][::-1]
    Q = np.concatenate([pad_l, xz, pad_r]); w = np.exp(-.5 * (np.arange(-r, r + 1) / k) ** 2); w /= w.sum()
    return np.stack([np.convolve(Q[:, i], w, 'valid') for i in range(2)], -1)


def smooth1(a, k):
    if k < 1: return a
    r = int(3 * k); Q = np.concatenate([np.full(r, a[0]), a, np.full(r, a[-1])])
    w = np.exp(-.5 * (np.arange(-r, r + 1) / k) ** 2); w /= w.sum()
    return np.convolve(Q, w, 'valid')


class Noise:
    """Deterministic smooth 1-D fields (sums of sines with seeded phases)."""
    def __init__(self, seed): self.rng = np.random.default_rng(seed)

    def field(self, s, scales=(29., 12., 5.5), amps=(1., .55, .3)):
        out = np.zeros_like(np.asarray(s, float))
        for L, a in zip(scales, amps): out = out + a * np.sin(2 * math.pi * np.asarray(s) / (L * self.rng.uniform(.8, 1.25)) + self.rng.uniform(0, 6.283))
        return out / sum(amps)


# ------------------------------------------------------------------------------------------------------------------
# site frame, walk profile, steps
# ------------------------------------------------------------------------------------------------------------------
class Site:
    def __init__(self, spec, g0, gp, ds=.25):
        self.spec = spec; self.id = spec['id']; self.g0 = g0; self.gp = gp
        P, d = main_path(spec['mountain'])
        a, b = spec['s0'] - spec['lead'], spec['s1'] + spec['lead']
        sm = np.arange(a, b + 1e-6, ds)                               # MainPath arc of every station
        xz = np.stack([np.interp(sm, d, P[:, 0]), np.interp(sm, d, P[:, 2])], -1)
        xz = smooth_line(xz, 1.2 / ds)
        self.sm = sm; self.xz = xz
        self.s = np.r_[0, np.cumsum(np.linalg.norm(np.diff(xz, axis=0), axis=1))]
        t = np.gradient(xz, axis=0); t /= np.linalg.norm(t, axis=1, keepdims=True)
        right = np.stack([t[:, 1], -t[:, 0]], -1)
        core = (sm >= spec['s0']) & (sm <= spec['s1'])
        rel = [float(np.mean(g0.b(*(xz[core] + side * right[core] * 18).T) - g0.b(*(xz[core] - side * right[core] * 18).T))) for side in (1,)]
        self.sgn = 1.0 if rel[0] > 0 else -1.0
        self.t = t; self.n = right * self.sgn                         # toward the cliff
        self.hw = spec['width'] / 2
        self.N = len(sm)
        self.core = core
        self.s_core = (float(np.interp(spec['s0'], sm, self.s)), float(np.interp(spec['s1'], sm, self.s)))
        g = spec.get('gallery')
        self.gal = (float(np.interp(g[0], sm, self.s)), float(np.interp(g[1], sm, self.s))) if g else None
        self._walk()

    def at(self, s, u, y=None):
        """World points for arc s (local), offset u along n, height y."""
        s = np.asarray(s, float)
        x = np.interp(s, self.s, self.xz[:, 0]); z = np.interp(s, self.s, self.xz[:, 1])
        nx = np.interp(s, self.s, self.n[:, 0]); nzz = np.interp(s, self.s, self.n[:, 1])
        nn = np.stack([nx, nzz], -1); nn /= np.linalg.norm(nn, axis=-1, keepdims=True)
        u = np.asarray(u, float)
        X = x + nn[..., 0] * u; Z = z + nn[..., 1] * u
        if y is None: return X, Z
        return np.stack(np.broadcast_arrays(X, np.asarray(y, float), Z), -1)

    def frame(self, s):
        """(t3, n3) unit vectors in world space at arc s."""
        tx = np.interp(s, self.s, self.t[:, 0]); tz = np.interp(s, self.s, self.t[:, 1])
        nx = np.interp(s, self.s, self.n[:, 0]); nzz = np.interp(s, self.s, self.n[:, 1])
        t3 = nz(np.array([tx, 0., tz])); n3 = nz(np.array([nx, 0., nzz]))
        return t3, n3

    def _walk(self):
        hw = self.hw
        us = [-hw - .3, -hw, -hw / 2, 0, hw / 2, hw, hw + .12]
        # cuts only lower vertices, so any triangulation of the cut ground stays under g0.hi: one bound serves both
        gmax = np.max([self.g0.hi(*(self.xz + self.n * u).T) for u in us], axis=0)
        raw = gmax + LIFT
        y = smooth1(raw, 1.0 / .25)
        y = np.maximum(y, raw - .015)
        ds = np.diff(self.s)
        for _ in range(3):                                            # grade limit (lift only)
            for i in range(1, self.N): y[i] = max(y[i], y[i - 1] - .5 * ds[i - 1])
            for i in range(self.N - 2, -1, -1): y[i] = max(y[i], y[i + 1] - .5 * ds[i])
        self.y = y
        self.gmax = gmax

    def Y(self, s): return np.interp(s, self.s, self.y)

    def in_gallery(self, s, pad=0.):
        return self.gal is not None and (self.gal[0] - pad) <= s <= (self.gal[1] + pad)

    def steps(self):
        """Stair treads [a, b, top] (visual only; the collider is the ramp through the tread centres). Stepped runs are the
        contiguous stretches steeper than .10 (gaps < .8 m merged, gallery excluded); inside a run the tread edges are put
        at equal rises (<= MAX_RISER), so each tread is .17/grade long wherever the grade changes."""
        grade = np.gradient(smooth1(self.y, 3), self.s)
        steep = (np.abs(grade) > .10) & ~np.array([self.in_gallery(s, .3) for s in self.s])
        runs = []; i = 0
        while i < self.N:
            if not steep[i]: i += 1; continue
            j = i
            while j + 1 < self.N and steep[j + 1]: j += 1
            a, b = float(self.s[i]), float(self.s[j])
            if runs and a - runs[-1][1] < .8 and np.sign(grade[i]) == runs[-1][2]: runs[-1][1] = b
            else: runs.append([a, b, float(np.sign(grade[i]))])
            i = j + 1
        out = []
        for a, b, sg in runs:
            ss = np.linspace(a, b, max(3, int((b - a) / .05)))
            yy = self.Y(ss) * sg; yy = np.maximum.accumulate(yy)       # monotonic in the run's own direction
            R = float(yy[-1] - yy[0])
            if R < .07 or b - a < .5: continue
            n = int(math.ceil(R / MAX_RISER)); levels = yy[0] + R * np.arange(n + 1) / n
            edges = np.interp(levels, yy + np.arange(len(yy)) * 1e-9, ss)
            run = []
            for k, (e0, e1) in enumerate(zip(edges[:-1], edges[1:])):
                # tread top = the equal-rise mid level (risers exactly R/n); the ramp at the tread centre is within a few cm
                top = float((levels[k] + levels[k + 1]) / 2 * sg)
                if run and e1 - e0 < .2: run[-1][1] = float(e1)           # sliver: extend the previous tread (no gap)
                else: run.append([float(e0), float(e1), top])
            out += run
        return out


# ------------------------------------------------------------------------------------------------------------------
# geometry helpers
# ------------------------------------------------------------------------------------------------------------------
def obox(mb, c, ax, ay, az, size, mat, faces='xXyYzZ', uvscale=1.0):
    """Oriented box: centre c, unit axes ax/ay/az (need not be orthogonal to the world), size (sx, sy, sz)."""
    c = np.asarray(c, float); ax, ay, az = (np.asarray(v, float) for v in (ax, ay, az)); hx, hy, hz = np.asarray(size, float) / 2
    P = lambda i, j, k: c + ax * i * hx + ay * j * hy + az * k * hz
    spec = {'x': (-ax, [(-1, -1, -1), (-1, 1, -1), (-1, 1, 1), (-1, -1, 1)]), 'X': (ax, [(1, -1, -1), (1, -1, 1), (1, 1, 1), (1, 1, -1)]),
            'y': (-ay, [(-1, -1, -1), (-1, -1, 1), (1, -1, 1), (1, -1, -1)]), 'Y': (ay, [(-1, 1, -1), (1, 1, -1), (1, 1, 1), (-1, 1, 1)]),
            'z': (-az, [(-1, -1, -1), (1, -1, -1), (1, 1, -1), (-1, 1, -1)]), 'Z': (az, [(-1, -1, 1), (-1, 1, 1), (1, 1, 1), (1, -1, 1)])}
    for f in faces:
        o, cs = spec[f]; mb.poly([P(*k) for k in cs], mat, o, uvscale=uvscale)


def lumpy_stone(mb, c, ax, ay, az, size, rng, mat, nu=6, nv=4):
    """Set stone: a boxy superellipsoid with a flattened bed and top, low-order lumps (never an egg)."""
    th, ph = np.meshgrid(np.linspace(0, 2 * math.pi, nu + 1), np.linspace(-math.pi / 2, math.pi / 2, nv + 1), indexing='ij')
    e = .45
    sg = lambda v, p: np.sign(v) * np.abs(v) ** p
    f = 1 + sum(rng.uniform(-.09, .09) * np.cos(m * th + rng.uniform(0, 6.3)) * np.cos(ph) for m in (2, 3))
    X = sg(np.cos(ph), e) * sg(np.cos(th), e) * f; Yv = sg(np.sin(ph), e * .8); Z = sg(np.cos(ph), e) * sg(np.sin(th), e) * f
    hx, hy, hz = np.asarray(size, float) / 2
    P = c + X[..., None] * ax * hx + Yv[..., None] * ay * hy + Z[..., None] * az * hz
    uv = np.stack([th / (2 * math.pi) * (hx + hz) * 2 / 1.2, (ph + math.pi / 2) / math.pi * hy * 2 / 1.2], -1)
    mb.grid(P, mat, uv, lambda Q: Q - c)


def strip_grid(P, mat, mb, out, us=3.5):
    """P (ns, nr, 3) grid; UVs from arc lengths (texture repeat `us` metres)."""
    su = np.concatenate([np.zeros((1, P.shape[1])), np.cumsum(np.linalg.norm(np.diff(P, axis=0), axis=-1), 0)], 0)
    sv = np.concatenate([np.zeros((P.shape[0], 1)), np.cumsum(np.linalg.norm(np.diff(P, axis=1), axis=-1), 1)], 1)
    mb.grid(P, mat, np.stack([su / us, sv / us], -1), out)


# ------------------------------------------------------------------------------------------------------------------
# the cliff face
# ------------------------------------------------------------------------------------------------------------------
class Face:
    """Tiered face profile per station. Rows: [foot] + tiers*[f15 f33 f49 f51 f70 f88 top lip_out lip_top ledge_mid ledge_back]
    + [dive_mid, dive]. Major joints (gullies) split the wall into blocks whose tiers have their own heights; minor joints
    split those into planar facets; a bedding crack (f49|f51) steps each tier's upper half. Face rows are displaced into
    the rock (never nearer than the walk edge below 3.2 m; above it a block may bulge by `bulge`)."""
    TIER_ROWS = ('f15', 'f33', 'f49', 'f51', 'f70', 'f88', 'top', 'lip_out', 'lip_top', 'ledge_mid', 'ledge_back')
    FRACS = (('f15', .15), ('f33', .33), ('f49', .49), ('f51', .51), ('f70', .70), ('f88', .88))
    FACE_ROWS = {'f15', 'f33', 'f49', 'f51', 'f70', 'f88', 'top', 'lip_out', 'lip_top'}
    UPPER = {'f51', 'f70', 'f88', 'top', 'lip_out', 'lip_top'}

    def __init__(self, site: Site):
        self.site = site; sp = site.spec; nz_ = Noise(sp['seed'])
        self.T = T = len(sp['tiers'])
        a, b = site.s_core
        self.s = site.s[(site.s >= a - 1e-6) & (site.s <= b + 1e-6)]
        s = self.s
        f = np.clip((s - a) / TAPER, 0, 1) * np.clip((b - s) / TAPER, 0, 1); self.f = f * f * (3 - 2 * f)
        rng = np.random.default_rng(sp['seed'] + 7)
        # major joints (gullies) and the blocks between them: each block has its own tier heights
        mj = []; x = a + rng.uniform(.3, 1.) * sp['major'][0][0]
        while x < b - 2: mj.append((x, rng.uniform(*sp['major'][1]), rng.uniform(*sp['major'][2]))); x += rng.uniform(*sp['major'][0])
        mn = []; x = a + rng.uniform(0, sp['joints'][1])
        while x < b:
            if all(abs(x - m[0]) > 1.4 * m[2] + .3 for m in mj): mn.append((x, rng.uniform(*sp['jdepth']), rng.uniform(.22, .5)))
            x += rng.uniform(*sp['joints'])
        self.majors, self.minors = mj, mn
        J = sorted(mj + mn); self.joints = J
        self.jx = np.array([j[0] for j in J]); self.jd = np.array([j[1] for j in J]); self.jw = np.array([j[2] for j in J])
        edges = [a] + [j[0] for j in J] + [b]
        self.be = np.array(edges[:-1]); self.bm = np.array([(e0 + e1) / 2 for e0, e1 in zip(edges[:-1], edges[1:])]); nb = len(self.be)
        self.ba = rng.uniform(0, .38, nb); self.bb = rng.uniform(-.09, .09, nb); self.bc = rng.uniform(-.04, .025, nb)
        self.bo = rng.uniform(-.3, .3, (nb, T)); self.bE = rng.uniform(-.28, .28, (nb, T))
        medges = [a] + [m[0] for m in mj] + [b]; mult = rng.uniform(.68, 1.32, (len(medges) - 1, T))
        hm = np.ones((T, len(s)))
        for i in range(T):
            xs, vs = [], []
            for k in range(len(medges) - 1):
                w0 = mj[k - 1][2] * .6 if k > 0 else 0.; w1 = mj[k][2] * .6 if k < len(mj) else 0.
                xs += [medges[k] + w0, medges[k + 1] - w1]; vs += [mult[k, i], mult[k, i]]
            hm[i] = np.interp(s, xs, vs)
        Hf = sp['face'] * (1 + .12 * nz_.field(s))
        h = np.array([Hf * fr * (1 + .2 * nz_.field(s)) * hm[i] for i, fr in enumerate(sp['tiers'])])
        tot = h.sum(0); h *= np.minimum(1, 27.8 / np.maximum(tot, 1e-6))[None]                        # total (with lips) <= ~30 m
        self.h = h
        self.lean = np.array([np.interp(nz_.field(s, (17., 7.)), [-1, 1], sp['lean']) for _ in range(T)])
        self.L = np.array([np.interp(nz_.field(s, (21., 9.)), [-1, 1], sp['ledge']) for _ in range(T)])
        self.ov = np.array([np.clip(np.interp(nz_.field(s, (15., 6.)), [-1, 1], (-sp['overhang'][1] * .4, sp['overhang'][1])), 0, None)
                            for _ in range(T)])
        self.u_in = site.hw + .03
        self.rows = ['foot'] + [f'{r}{i}' for i in range(T) for r in self.TIER_ROWS] + ['dive_mid', 'dive']
        self.kind = np.array([r.rstrip('0123456789') for r in self.rows])
        self.tier = np.array([int(r[-1]) if r[-1].isdigit() else 0 for r in self.rows])
        self._profiles()

    def disp(self, s, yrel, tier, upper):
        """+u (into the rock) for face rows at arcs s, yrel metres above the walk (vectorised)."""
        s = np.atleast_1d(np.asarray(s, float)); yrel = np.broadcast_to(np.asarray(yrel, float), s.shape)
        g = np.zeros_like(s)
        if len(self.jx):
            q = 1 - np.abs(s[:, None] - self.jx[None]) / self.jw[None]
            g = np.max(np.where(q > 0, self.jd[None] * np.clip(q, 0, 1) ** 1.3, 0.), axis=1)
        bi = np.clip(np.searchsorted(self.be, s, side='right') - 1, 0, len(self.be) - 1)
        fac = self.ba[bi] + self.bb[bi] * (s - self.bm[bi]) + self.bc[bi] * yrel + self.bo[bi, tier] + (self.bE[bi, tier] if upper else 0.)
        fac = np.where(yrel < 3.2, np.maximum(fac, 0.), np.maximum(fac, -self.site.spec['bulge']))
        return g + fac

    def _profiles(self):
        site = self.site; n = len(self.s); R = len(self.rows)
        U = np.zeros((n, R)); Y = np.zeros((n, R))
        ug = np.arange(0, 46.0, .25)
        for k, s in enumerate(self.s):
            f = self.f[k]; y0 = float(site.Y(s))
            X, Z = site.at(np.full_like(ug, s), ug)
            ghi = site.g0.hi(X, Z)                                                  # ground to hide (ops only cut)
            glo = site.gp.lo(X, Z)                                                  # ground to bury into (after ops)
            G = lambda u: float(np.interp(u, ug, ghi)); Gl = lambda u: float(np.interp(u, ug, glo))
            row = [(self.u_in + .02, min(y0 - FOOT, Gl(self.u_in) - .7))]
            ub, yb = self.u_in, y0 - FOOT * (1 - f)
            for i in range(self.T):
                h = float(self.h[i, k]) * f; ln = float(self.lean[i, k])
                for _, fr in self.FRACS: row.append((ub + ln * h * fr, yb + h * fr))
                ut, yt = ub + ln * h, yb + h
                row.append((ut, yt))
                o = float(self.ov[i, k]) * f if yt - y0 > 3.2 else 0.0
                o = min(o, ut - self.u_in + .9)
                row.append((ut - o, yt + .28 * f)); row.append((ut - o + .2 * f, yt + .5 * f))
                yl = yt + .5 * f + .02
                umax = ut + max(.7, float(self.L[i, k]) * max(f, .15))
                cand = np.where((ug > ut + .6) & (ug <= umax) & (ghi >= yl - .35))[0]
                u_back = max(ut + .7, float(ug[cand[0]]) if len(cand) else umax)
                y_back = yl + .05 * (u_back - ut) * f
                row.append((ut + .45 * (u_back - ut), yl - .06 * f)); row.append((u_back, y_back))
                ub, yb = u_back, y_back
            # crest: dive into the ground behind the last ledge (steep: right behind; gentle: back slope ~37°)
            if G(ub + .8) >= yb - .5:
                ud = ub + 1.4; row.append((ub + .55, yb + .08 * f)); row.append((ud, min(Gl(ud) - .9, yb - .4)))
            else:
                uu = ug[ug > ub]; back = yb - .75 * (uu - ub)
                hit = np.where(back <= np.interp(uu, ug, ghi) + .2)[0]
                ud = float(uu[hit[0]]) if len(hit) else ub + 12.0
                row.append((ub + .45 * (ud - ub), yb - .75 * .45 * (ud - ub) + .35 * f)); row.append((ud + .6, Gl(ud + .6) - .9))
            row = np.array(row)
            U[k] = row[:, 0]; Y[k] = row[:, 1]
        self.U0, self.Y = U, Y
        self.U = self._displace(self.s, U, Y, range(R))

    def _displace(self, ss, U0, Yr, rows):
        U = U0.copy(); y0 = self.site.Y(ss); ff = np.interp(ss, self.s, self.f)
        for b, r in enumerate(rows):
            if self.kind[r] not in self.FACE_ROWS: continue
            yrel = Yr[:, b] - y0
            U[:, b] = U0[:, b] + self.disp(ss, yrel, self.tier[r], self.kind[r] in self.UPPER) * ff
            U[:, b] = np.where(yrel < 3.2, np.maximum(U[:, b], self.u_in + .02), U[:, b])
        return U

    def rows_for(self, lod):
        keep = {0: None, 1: {'foot', 'f33', 'f49', 'f51', 'top', 'lip_top', 'ledge_back', 'dive'}, 2: {'foot', 'top', 'ledge_back', 'dive'}}[lod]
        return [r for r, k in enumerate(self.kind) if keep is None or k in keep]

    def grid(self, s0, s1, ds, lod, collision=False):
        site = self.site; ss = np.arange(s0, s1 + 1e-6, ds)
        if ss[-1] < s1 - 1e-6: ss = np.r_[ss, s1]
        if lod < 2 and not collision:                                  # joint edges in the column set
            extra = [v for sj, dj, wj in self.majors for v in (sj - wj, sj - wj * .45, sj, sj + wj * .45, sj + wj)]
            if lod == 0: extra += [v for sj, dj, wj in self.minors for v in (sj - wj, sj, sj + wj)]
            ss = np.unique(np.r_[ss, [v for v in extra if s0 < v < s1]])
        rows = self.rows_for(1 if collision else lod)
        U0 = np.stack([np.interp(ss, self.s, self.U0[:, r]) for r in rows], -1)
        Yg = np.stack([np.interp(ss, self.s, self.Y[:, r]) for r in rows], -1)
        U = self._displace(ss, U0, Yg, rows)                            # exact displacement on these columns
        X, Z = site.at(ss[:, None], U)
        return np.stack([X, Yg, Z], -1), ss

    def build(self, s0, s1, lod, name):
        ds = [1.0, 1.6, 3.2][lod]
        P, ss = self.grid(s0, s1, ds, lod)
        mb = MB(name); n3 = np.stack([self.site.frame(s)[1] for s in ss])
        strip_grid(P, 'cliff_rock', mb, lambda Q: -n3[:, None, :] + UP * .45, us=3.5)
        return mb

    def collision(self, s0, s1, name):
        P, ss = self.grid(s0, s1, 1.0, 1, collision=True)
        mb = MB(name); n3 = np.stack([self.site.frame(s)[1] for s in ss])
        mb.grid(P, 'c', np.zeros(P.shape[:2] + (2,)), lambda Q: -n3[:, None, :] + UP * .45)
        return mb


# ------------------------------------------------------------------------------------------------------------------
# trail, lip, footing, gallery
# ------------------------------------------------------------------------------------------------------------------
def free_intervals(L, blocks, min_len=.02):
    """Complement of the (sorted, possibly touching) block intervals within [0, L]."""
    out = []; cur = 0.0
    for a, b in sorted(blocks):
        if a > cur + min_len: out.append((cur, a))
        cur = max(cur, b)
    if L > cur + min_len: out.append((cur, L))
    return out


def trail_parts(site: Site, steps, lod, name):
    """Visible carved trail: flagged paving where it is level, stone treads where it climbs. Gallery span excluded; the
    paving ribbons abut the treads and the gallery exactly."""
    mb = MB(name); hw = site.hw; L = site.s[-1]
    blocks = [(a, b) for a, b, _ in steps] + ([site.gal] if site.gal else [])
    ds = [.5, 1.0, 2.5][lod]
    us = np.array([-hw - .1, 0., hw + .15])
    for a, b in free_intervals(L, blocks):
        ss = np.unique(np.r_[np.arange(a, b, ds), b])
        P = site.at(ss[:, None], us[None, :], (site.Y(ss) - .005)[:, None])
        strip_grid(P, 'stone_plain', mb, UP, us=1.6)
    rng = np.random.default_rng(site.spec['seed'] + 3)
    for a, b, top in steps:                                          # treads (riser face on the downhill side)
        m = (a + b) / 2; t3, n3 = site.frame(m)
        riser = 'z' if site.Y(b) >= site.Y(a) else 'Z'               # climbing toward +s: the riser faces -s
        cx, cz = site.at(m, (.15 - .1) / 2)
        if lod == 2:
            obox(mb, [cx, top - .15, cz], n3, UP, t3, [2 * hw + .25, .3, b - a + .02], 'stone_plain', faces='Y' + riser, uvscale=1.2)
            continue
        jit = rng.uniform(-.012, .012) if lod == 0 else 0.0
        obox(mb, [cx, top - .16 + jit, cz], n3, UP, t3, [2 * hw + .25, .32, b - a + .015], 'stone_plain',
             faces='Y' + riser + ('x' if lod == 0 else ''), uvscale=1.2)
    return mb


def trail_collision(site: Site, name):
    """Continuous ramp through the tread centres (walk profile), under the face foot and the lip; the gallery deck takes
    over between gal[0]+.05 and gal[1]-.05 (the two colliders overlap by .17 m at each end)."""
    mb = MB(name); hw = site.hw
    blocks = [(site.gal[0] + .05, site.gal[1] - .05)] if site.gal else []
    us = np.array([-hw - .15, hw + .2])
    for a, b in free_intervals(site.s[-1], blocks):
        ss = np.unique(np.r_[np.arange(a, b, .5), b])
        P = site.at(ss[:, None], us[None, :], site.Y(ss)[:, None])
        mb.grid(P, 'c', np.zeros(P.shape[:2] + (2,)), UP)
    return mb


def lip_stones(site: Site, lod, name, col_name):
    """Set stones on the valley edge (gaps < the .56 m capsule), buried .3 m; one collision box each."""
    rng = np.random.default_rng(site.spec['seed'] + 11)
    vis = MB(name); col = MB(col_name); hw = site.hw; s = 1.0; L = site.s[-1] - 1.0; count = 0
    seg = [(5, 3), (4, 2), None][lod]
    while s < L:
        ln = rng.uniform(.7, 1.3); gap = rng.uniform(.1, .4); m = s + ln / 2
        if site.in_gallery(m, .9) or site.in_gallery(s, .2) or site.in_gallery(s + ln, .2):
            s += ln + gap; continue
        t3, n3 = site.frame(m); y = float(site.Y(m))
        th = rng.uniform(.45, .62); above = rng.uniform(.34, .72); hgt = above + .32
        cx, cz = site.at(m, -hw - .06 - th / 2)
        c = np.array([cx, y + above - hgt / 2, cz])
        yaw = rng.uniform(-.12, .12); tt = nz(t3 + n3 * yaw); nn = nz(np.cross(tt, UP)) * (1 if np.dot(np.cross(tt, UP), n3) > 0 else -1)
        if seg is None: obox(vis, c, nn, UP, tt, [th, hgt, ln], 'stone_rough', faces='xXYzZ', uvscale=1.0)
        else: lumpy_stone(vis, c, nn, UP, tt, [th * 1.08, hgt, ln * 1.04], rng, 'stone_rough', *seg)
        obox(col, c, nn, UP, tt, [th * .9, hgt, ln * .96 + gap * .1], 'c')
        count += 1; s += ln + gap
    return vis, col, count


def footing(site: Site, lod, name, col_name=None):
    """Buried valley-side footing: battered rough face from the tread edge down into the ground (after terrain ops)."""
    vis = MB(name); col = MB(col_name or name + '_c'); hw = site.hw
    ds = [.75, 1.5, 3.5][lod]; rng_n = Noise(site.spec['seed'] + 5)
    gal = np.array([site.in_gallery(s, .05) for s in site.s]); i = 0
    while i < site.N:
        if gal[i]: i += 1; continue
        j = i
        while j + 1 < site.N and not gal[j + 1]: j += 1
        ss = np.unique(np.r_[np.arange(site.s[i], site.s[j], ds), site.s[j]])
        if len(ss) > 1:
            y = site.Y(ss); rows = []
            wob = .12 * rng_n.field(ss, (7., 3.))
            for frac, uo in ((0., 0.), (.35, .25), (1., .6)):
                u = -hw - .08 - uo
                X, Z = site.at(ss, u)
                gl = site.gp.lo(X, Z)
                depth = np.maximum(y - .05 - (gl - .6), .05)
                yy = (y - .04) - depth * frac
                uu = u - .32 * depth * frac + (wob if 0 < frac < 1 else 0)
                X, Z = site.at(ss, uu)
                if frac == 1.:
                    yy = np.minimum(yy, site.gp.lo(X, Z) - .6)
                rows.append(np.stack([X, yy, Z], -1))
            P = np.stack(rows, 1)
            n3 = np.stack([site.frame(s)[1] for s in ss])
            strip_grid(P, 'stone_rough', vis, lambda Q: n3[:, None, :] * -1, us=2.2)
            if lod == 0: col.grid(P[:, ::2], 'c', np.zeros((len(ss), 2, 2)), lambda Q: n3[:, None, :] * -1)
        i = j + 1
    return vis, col


def gallery(site: Site, lod, name, col_name):
    """잔도: plank deck on two stringers, cantilever beams socketed into the face with raking struts, valley-side rail.
    Collision: deck slab (top = walk) and a continuous rail wall. Gallery span ±.1 m overlaps the stone trail ends."""
    vis = MB(name); col = MB(col_name); hw = site.hw; a, b = site.gal; u_in = site.hw + .03
    rng = np.random.default_rng(site.spec['seed'] + 17)
    uo, ui = -hw - .06, u_in + .14                                   # deck outer / inner (into the rock) edges
    s = a - .1
    # planks (pitched along the grade)
    while s < b + .1:
        pw = rng.uniform(.24, .3) if lod == 0 else .9 if lod == 1 else (b - a + .2)
        e = min(b + .1, s + pw); m = (s + e) / 2
        p0 = site.at(s, 0., site.Y(s)); p1 = site.at(e, 0., site.Y(e))
        tt = nz(p1 - p0); t3, n3 = site.frame(m); ay = nz(np.cross(n3, tt)); ay = ay if ay[1] > 0 else -ay
        cx, cz = site.at(m, (uo + ui) / 2)
        c = np.array([cx, site.Y(m) - .004 - .033, cz])
        obox(vis, c, n3, ay, tt, [ui - uo + rng.uniform(-.04, .04), .066, max(e - s - .012, .05)], 'wood_board',
             faces='yYzZxX' if lod == 0 else 'yYzZ', uvscale=1.0)
        s = e
    # stringers along, cantilever beams across, struts, rail
    if lod < 2:
        for u in (uo + .22, ui - .45):
            ss = np.linspace(a - .1, b + .1, max(2, int((b - a) / 1.5) + 2))
            for s0, s1 in zip(ss[:-1], ss[1:]):
                A = site.at(s0, u, site.Y(s0) - .07 - .09); B = site.at(s1, u, site.Y(s1) - .07 - .09)
                beam(vis, A, B, .14, .18, 'wood_dark')
    k = 0
    for s in np.arange(a + .2, b + 1e-6, 1.4):
        y = float(site.Y(s)); t3, n3 = site.frame(s)
        yb = y - .07 - .18 - .1
        A = site.at(s, ui + .45, yb); B = site.at(s, uo - .16, yb)
        beam(vis, A, B, .2, .2, 'wood_dark')
        if lod < 2:
            beam(vis, site.at(s, u_in + .05, y - 1.9), site.at(s, uo + .12, yb - .08), .13, .13, 'wood_dark')
            if lod == 0:
                cxx, czz = site.at(s, u_in + .02); obox(vis, [cxx, yb, czz], n3, UP, t3, [.05, .28, .28], 'iron', faces='xXyYzZ')
        # rail posts at the beam ends
        P0 = site.at(s, uo - .08, yb - .1); P1 = site.at(s, uo - .08, y + 1.06)
        beam(vis, P0, P1, .11, .11, 'wood_dark') if lod < 2 else beam(vis, P0, P1, .1, .1, 'wood_dark')
        k += 1
    posts = np.r_[a - .1, np.arange(a + .2, b + 1e-6, 1.4), b + .1]
    for s in (posts[0], posts[-1]):                                   # end posts (the stone lip starts beyond them)
        y = float(site.Y(s)); beam(vis, site.at(s, uo - .08, y - .35), site.at(s, uo - .08, y + 1.1), .13, .13, 'wood_dark')
    for s0, s1 in zip(posts[:-1], posts[1:]):
        for hgt, sec in ((1.0, (.09, .1)), (.55, (.07, .08))):
            if lod == 2 and hgt < 1: continue
            beam(vis, site.at(s0, uo - .08, site.Y(s0) + hgt), site.at(s1, uo - .08, site.Y(s1) + hgt), sec[0], sec[1], 'wood_dark')
    # collision: deck slab + rail wall
    ss = np.unique(np.r_[np.arange(a - .12, b + .12, .5), b + .12])
    top = site.at(ss[:, None], np.array([uo, ui])[None, :], site.Y(ss)[:, None])
    col.grid(top, 'c', np.zeros(top.shape[:2] + (2,)), UP)
    bot = top - UP * .12; col.grid(bot, 'c', np.zeros(bot.shape[:2] + (2,)), -UP)
    rail = np.stack([site.at(ss, uo - .08, site.Y(ss) - .1), site.at(ss, uo - .08, site.Y(ss) + 1.1)], 1)
    n3 = np.stack([site.frame(s)[1] for s in ss]); col.grid(rail, 'c', np.zeros(rail.shape[:2] + (2,)), lambda Q: n3[:, None, :])
    return vis, col


def gallery_ends(site: Site, lod, mb):
    """Stone abutment walls closing the footing wedge where the stone trail meets the gallery."""
    if site.gal is None: return
    hw = site.hw; u_in = site.hw + .03
    for s, sgn in ((site.gal[0] - .05, -1), (site.gal[1] + .05, 1)):
        t3, n3 = site.frame(s); y = float(site.Y(s))
        us = np.linspace(-hw - .12, u_in + .1, 5)
        X, Z = site.at(np.full_like(us, s), us)
        gl = site.gp.lo(X, Z) - .6
        P = np.stack([np.stack([X, np.full_like(us, y - .06), Z], -1), np.stack([X, gl, Z], -1)], 1)
        strip_grid(P, 'stone_rough', mb, t3 * sgn, us=2.0)


def notch_ops(site: Site):
    """Gallery terrain cut (surface297 'notch', cut-only): the shelf under the gallery drops ~3 m toward the valley."""
    if site.gal is None: return []
    a, b = site.gal[0] - 1.5, site.gal[1] + 1.5; ops = []
    ks = np.linspace(a, b, max(2, int(math.ceil((b - a) / 6.0)) + 1))
    for s0, s1 in zip(ks[:-1], ks[1:]):
        A = site.at(s0, -1.2); B = site.at(s1, -1.2)
        ops.append(dict(type='notch', a=[round(float(A[0]), 3), round(float(A[1]), 3)], b=[round(float(B[0]), 3), round(float(B[1]), 3)],
                        width=5.0, ya=round(float(site.Y(s0)) - 3.6, 3), yb=round(float(site.Y(s1)) - 3.6, 3), blend=4.0,
                        site=site.id, note='gallery shelf cut'))
    return ops


# ------------------------------------------------------------------------------------------------------------------
# survey + previews
# ------------------------------------------------------------------------------------------------------------------
def survey(site: Site, face: Face):
    core = site.core
    X, Z = site.xz[core].T
    prof = {u: float(np.mean(site.g0.b(*(site.xz[core] + site.n[core] * u).T) - site.y[core])) for u in (5, 10, 15, 20, 30)}
    down = {u: float(np.mean(site.g0.b(*(site.xz[core] - site.n[core] * u).T) - site.y[core])) for u in (5, 10, 20)}
    slope = math.degrees(math.atan((prof[30] - prof[10]) / 20.0))
    top = face.Y.max(axis=1) - site.Y(face.s)
    # protrusion: rock skin above the natural ground at the same XZ
    Xg, Zg = site.at(face.s[:, None], face.U)
    prot = float(np.max(face.Y - site.g0.b(Xg, Zg)))
    grade = np.abs(np.gradient(site.y, site.s))
    pm = protected_mask()
    pr = pm[np.clip(np.round(Z / 4).astype(int), 0, GH - 1), np.clip(np.round(X / 4).astype(int), 0, GW - 1)].mean()
    mid = site.xz[np.argmin(np.abs(site.sm - (site.spec['s0'] + site.spec['s1']) / 2))]
    return dict(centre=[round(float(mid[0]), 1), round(float(mid[1]), 1)], start=[round(float(v), 1) for v in site.at(site.s_core[0], 0., site.Y(site.s_core[0]))],
                end=[round(float(v), 1) for v in site.at(site.s_core[1], 0., site.Y(site.s_core[1]))],
                uphillRelief={f'{u}m': round(v, 1) for u, v in prof.items()}, valleyDrop={f'{u}m': round(v, 1) for u, v in down.items()},
                slope10to30deg=round(slope, 1), faceHeightMax=round(float(top.max()), 1), faceHeightMedian=round(float(np.median(top[face.f > .99])), 1),
                protrusionMax=round(prot, 1), walkGradeMax=round(float(grade.max()), 3), rise=round(float(site.y[core].max() - site.y[core].min()), 1),
                length=round(float(site.s_core[1] - site.s_core[0]), 1), protectedFraction=round(float(pr), 2), upSign=site.sgn)


def previews(site: Site, face: Face, steps):
    (OUT / 'Preview').mkdir(parents=True, exist_ok=True)
    # plan: rock footprint rows over the terrain height
    img = Image.new('RGB', (900, 900), (246, 243, 236)); d = ImageDraw.Draw(img)
    X, Z = site.at(face.s[:, None], face.U)
    x0, x1 = X.min() - 10, X.max() + 10; z0, z1 = Z.min() - 10, Z.max() + 10; sc = 880 / max(x1 - x0, z1 - z0)
    P = lambda x, z: (10 + (x - x0) * sc, 890 - (z - z0) * sc)
    for r in range(0, X.shape[1], 1):
        d.line([P(a, b) for a, b in zip(X[:, r], Z[:, r])], fill=(120, 120, 120) if face.kind[r] != 'ledge_back' else (40, 40, 40), width=1)
    d.line([P(a, b) for a, b in site.xz], fill=(200, 30, 30), width=3)
    if site.gal:
        g = [P(*site.at(s, 0.)) for s in np.linspace(*site.gal, 20)]; d.line(g, fill=(150, 90, 20), width=6)
    d.text((10, 10), f'{site.id} plan: red=trail brown=gallery grey=face rows black=ledge backs', fill=(0, 0, 0))
    img.save(OUT / 'Preview' / f'{site.id}-plan.png')
    for k, frac in enumerate((.25, .5, .78)):
        s = site.s_core[0] + frac * (site.s_core[1] - site.s_core[0])
        if site.gal and k == 1: s = sum(site.gal) / 2
        W, Hh = 900, 620; img = Image.new('RGB', (W, Hh), (246, 243, 236)); d = ImageDraw.Draw(img)
        us = np.linspace(-20, 36, 500); Xs, Zs = site.at(np.full_like(us, s), us)
        y0 = float(site.Y(s)); lo, hi = y0 - 16, y0 + 40
        TX = lambda u: (u + 20) / 56 * W; TY = lambda y: Hh - (y - lo) / (hi - lo) * Hh
        d.line([(TX(u), TY(v)) for u, v in zip(us, site.g0.b(Xs, Zs))], fill=(150, 150, 150), width=2)
        d.line([(TX(u), TY(v)) for u, v in zip(us, site.gp.b(Xs, Zs))], fill=(90, 150, 90), width=1)
        kk = int(np.argmin(np.abs(face.s - s)))
        d.line([(TX(u), TY(v)) for u, v in zip(face.U[kk], face.Y[kk])], fill=(40, 40, 40), width=3)
        d.line([(TX(-site.hw), TY(y0)), (TX(site.hw), TY(y0))], fill=(200, 30, 30), width=5)
        d.text((10, 10), f'{site.id} s={s:.1f} (uphill right) grey=ground green=after ops black=face red=walk', fill=(0, 0, 0))
        img.save(OUT / 'Preview' / f'{site.id}-cross-{k}.png')


# ------------------------------------------------------------------------------------------------------------------
def main():
    OUT.mkdir(parents=True, exist_ok=True); (OUT / 'Meshes').mkdir(exist_ok=True)
    for f in (OUT / 'Meshes').glob('*.json'): f.unlink()
    h0 = HW.terrain().copy(); g0 = Ground(h0)
    prot = protected_mask()
    # this compound's terrain ops first (the gallery notch): "down" features are sized on the ground after them
    pre = [Site(sp, g0, g0) for sp in SITES]
    ops = [o for s in pre for o in notch_ops(s)]
    hp, op_stats = surface297.apply(h0.copy(), prot, [dict(o, source='CliffPath') for o in ops])
    gp = Ground(hp)
    meshes, routes, layout, stats = [], [], dict(note=__doc__.strip().splitlines()[0], sites=[]), {}

    def export(name, kind, lods, col):
        for lod, mb in enumerate(lods): mb.export(OUT / 'Meshes' / f'{name}_LOD{lod}.json')
        if col is not None and col.V: col.export(OUT / 'Meshes' / f'{name}_Collision.json')
        meshes.append(dict(name=name, kind=kind, world=True))
        stats[name] = dict(tris=[mb.tris() for mb in lods], collision=col.tris() if col is not None else 0)

    for sp in SITES:
        site = Site(sp, g0, gp); face = Face(site); steps = site.steps(); nm = 'CliffPath_' + site.id
        a, b = site.s_core; nch = max(1, int(round((b - a) / 42.0))); edges = np.linspace(a, b, nch + 1)
        for c, (e0, e1) in enumerate(zip(edges[:-1], edges[1:])):
            export(f'{nm}_Face_{c}', 'rock', [face.build(e0, e1, lod, f'{nm}_Face_{c}') for lod in (0, 1, 2)], face.collision(e0, e1, f'{nm}_Face_{c}_collision'))
        tr = [trail_parts(site, steps, lod, f'{nm}_Trail') for lod in (0, 1, 2)]
        gallery_ends(site, 0, tr[0]); gallery_ends(site, 1, tr[1])
        export(f'{nm}_Trail', 'path', tr, trail_collision(site, f'{nm}_Trail_collision'))
        lips = [lip_stones(site, lod, f'{nm}_Lip', f'{nm}_Lip_collision') for lod in (0, 1, 2)]
        export(f'{nm}_Lip', 'prop', [l[0] for l in lips], lips[0][1])
        ft = [footing(site, lod, f'{nm}_Footing', f'{nm}_Footing_collision') for lod in (0, 1, 2)]
        export(f'{nm}_Footing', 'rock', [f[0] for f in ft], ft[0][1])
        if site.gal:
            gl = [gallery(site, lod, f'{nm}_Gallery', f'{nm}_Gallery_collision') for lod in (0, 1, 2)]
            export(f'{nm}_Gallery', 'path', [g[0] for g in gl], gl[0][1])
        # walk route: 4 m of natural path on each side, then the trail centre (gallery included)
        ss = np.unique(np.r_[np.arange(0, site.s[-1], 1.5), site.s[-1]])
        pts = site.at(ss, 0., site.Y(ss) + .05)
        routes.append(dict(id=f'cliffpath_{site.id}', points=[round(float(v), 3) for p in pts for v in p]))
        sv = survey(site, face)
        risers = [abs(s1[2] - s0[2]) for s0, s1 in zip(steps[:-1], steps[1:]) if abs(s1[0] - s0[1]) < .02]
        treads = [s1 - s0 for s0, s1, _ in steps]
        site_tris = sum(v['tris'][0] for k, v in stats.items() if k.startswith(nm + '_'))
        sv.update(steps=len(steps), riserMax=round(max(risers), 3) if risers else 0.0, treadMin=round(min(treads), 3) if treads else 0.0,
                  width=sp['width'], tiers=len(sp['tiers']), lipStones=lips[0][2], gallery=[round(v, 1) for v in site.gal] if site.gal else None,
                  galleryMainPathArc=list(sp['gallery']) if sp.get('gallery') else None, joints=len(face.joints), LOD0tris=site_tris,
                  meshes=[k for k in stats if k.startswith(nm + '_')])
        # rock footprint (face foot line + dive line): trees/props standing inside it need culling or re-seating
        fx, fz = site.at(face.s, face.U[:, 0]); bx, bz = site.at(face.s, face.U[:, -1])
        poly = np.r_[np.stack([fx, fz], -1)[::8], np.stack([bx, bz], -1)[::-8]]
        layout['sites'].append(dict(id=site.id, mountain=sp['mountain'], realm=sp['realm'], mainPathArc=[sp['s0'], sp['s1']], survey=sv,
                                    rockFootprint=[round(float(v), 2) for q in poly for v in q]))
        previews(site, face, steps)
        print(json.dumps(dict(site=site.id, **{k: v for k, v in sv.items() if k != 'meshes'}), ensure_ascii=False))
    layout['terrainOps'] = ops; layout['terrainOpStats'] = op_stats; layout['stats'] = stats
    layout['materials'] = dict(cliff_rock='NEW slot — map in CompactFinish297.Kit Slots297 (e.g. MountainTrail285 rock_face_03 / HighlandGranite293)',
                               stone_rough='lip stones, footing, gallery abutments', stone_plain='paving + treads', wood_dark='gallery frame',
                               wood_board='gallery planks', iron='beam sockets')
    (OUT / 'layout.json').write_text(json.dumps(layout, indent=1, ensure_ascii=False), encoding='utf-8')
    unity = dict(compound='CliffPath', root='Finish297_CliffPath', meshes=meshes, hide=[], clip=[], markers=[], gates=[], routes=routes, doors=[],
                 terrainOps=[{k: v for k, v in o.items() if k in ('type', 'a', 'b', 'width', 'ya', 'yb', 'blend')} for o in ops])
    (OUT / 'unity.json').write_text(json.dumps(unity, indent=1), encoding='utf-8')
    print(json.dumps(dict(meshes=len(meshes), routes=[r['id'] for r in routes], terrainOps=len(ops),
                          opCells=[o['cells'] for o in op_stats], opProtectedTouched=sum(o['protectedTouched'] for o in op_stats)), indent=1))


if __name__ == '__main__':
    main()
