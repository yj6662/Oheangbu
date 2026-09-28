"""#297 bridges — Korean crossings rebuilt as visuals over the four retained #296 crossing routes (Architecture296_Crossings).

The #296 physics stays: every walking/driving surface (continuous supports, approach supports, dry-founded abutments,
bank aprons), rail/post/pier boxes and all route ledgers are kept byte-for-byte, so vehicles, routes and #296 checks are
unaffected. This compound replaces the visuals that were assembled from cropped/warped SM_CW masonry and pier planks:
  stone crossings  capital_shared_stone_bridge (Hwanggyeong, 2 routes share the deck) and hyeongang__temple_bridge:
                   홍예교 — a run of semicircular (or, where the water leaves too little room, segmental) arches of
                   voussoirs (홍예석) with a raised keystone, dressed spandrel walls, piers with cutwaters (물가름) at the
                   #296 pier stations (9 m), a 박석 deck with dressed cornice, and a 돌난간 (지대석 base course, 동자주
                   posts, 돌란대 rail, 법수 end posts) exactly where the retained parapet colliders run.
  timber crossings jeokro__cheolong_bridge (raised vehicle road) and mountain_hwanggyeong_main_bridge (gorge trestle):
                   timber bents at the #296 pier stations (6 m) on dressed stone footings (braced when tall; raked legs
                   on the gorge trestle), cap beams (멍에), stringers (장선), plank deck (청판) with gaps and a post-and-rail
                   난간 at the retained rail-post positions; the jeokro road's dry causeway gets paving, battered retaining
                   walls and a coping; stone abutment walls where timber meets masonry/ground.
Deck visuals sit 7 mm under the retained collider tops (ribbon top = receipt point + .04; plan rows = SurfaceY), so feet
never sink. Pier bodies stand around the retained PierCollision boxes (stations reproduced from the #296 code).
Hidden (#296 visual children only): <id>_deck_*, <id>_loadbearing_*, LOD1_*, LOD2_*, and the causeway/approach masonry
visuals <id>_0_vehicle_aprons_* / <id>_0_temple_aprons_* (rebuilt here). Kept: bank aprons (they carry MeshColliders) and
their cut caps; because the holder's LOD1_/LOD2_ go, the kept aprons render only in the holder's LOD0 range, so this
compound's LOD1/LOD2 decks include flat apron patches.
Outputs Finish297/Bridges/: Meshes/*_LOD{0,1,2}.json (+ *_Collision.json for pier/footing bodies below the decks),
unity.json (meshes, hide, routes), layout.json (per-bridge spans, stations, arches, stats).
"""
from __future__ import annotations
import json, math, re, sys
from pathlib import Path
import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
from hanok297 import MB, beam, nz
import hanok297_wall as HW
from cliffpath297 import obox

ROOT = Path(__file__).resolve().parents[2]
A296 = ROOT / 'Art/World/Compact/Rebuild/Architecture296'
OUT = ROOT / 'Art/World/Compact/Rebuild/Finish297/Bridges'
SCENE = ROOT / 'Oheangbu/Assets/_Project/Scenes/World/W_Demo_Main.unity'
UP = np.array([0., 1., 0.])
DECK_DROP = .007          # visual deck top below the retained collider top
PW = 2.4                  # stone pier thickness along the route
RING = .55                # voussoir depth
HALF_EXTRA = .22          # stone wall plane beyond the deck half-width

BRIDGES = {
    'capital_shared_stone_bridge': dict(kind='stone', realm='Hwanggyeong', step=9.0),
    'hyeongang__temple_bridge': dict(kind='stone', realm='Hyeongang', step=9.0),
    'jeokro__cheolong_bridge': dict(kind='timber', realm='Cheolong', step=6.0, posts=(-1.92, -.64, .64, 1.92),
                                    stringers=(-2.6, -1.3, 0., 1.3, 2.6)),
    'mountain_hwanggyeong_main_bridge': dict(kind='timber', realm='Hwanggyeong', step=6.0, posts=(-1.088, 1.088), raked=True,
                                             stringers=(-1.35, -.45, .45, 1.35)),
}


def v3(p): return np.array([p['x'], p['y'], p['z']], float)


def H(x, z): return HW.H(x, z)


class Water:
    def __init__(self):
        self.w = np.fromfile(HW.SURF / 'waterlevel.bytes', '<f4').reshape(1501, 1001).astype(float)

    def at(self, x, z):
        gx = np.clip(np.asarray(x, float) / 4, 0, 999.999); gz = np.clip(np.asarray(z, float) / 4, 0, 1499.999)
        i = gx.astype(int); j = gz.astype(int); w = self.w
        return np.max([w[j, i], w[j, i + 1], w[j + 1, i], w[j + 1, i + 1]], axis=0)


WATER = Water()


# ------------------------------------------------------------------------------------------------------------------
# walking surfaces (the retained colliders)
# ------------------------------------------------------------------------------------------------------------------
class Polyline:
    """Ribbon support: top = point (+ lift), flat across; 3-D arc length like BridgeSample296."""
    def __init__(self, pts, lift=0.0):
        self.P = np.asarray(pts, float) + [0, lift, 0]
        self.d = np.r_[0, np.cumsum(np.linalg.norm(np.diff(self.P, axis=0), axis=1))]; self.L = float(self.d[-1])
        t = np.gradient(self.P[:, [0, 2]], axis=0); self.t2 = t / np.linalg.norm(t, axis=1, keepdims=True)

    def centre(self, d):
        d = np.asarray(d, float); return np.stack([np.interp(d, self.d, self.P[:, k]) for k in range(3)], -1)

    def frame(self, d):
        d = np.asarray(d, float); tx = np.interp(d, self.d, self.t2[:, 0]); tz = np.interp(d, self.d, self.t2[:, 1])
        n = np.hypot(tx, tz); tx, tz = tx / n, tz / n
        z0 = np.zeros_like(tx)
        return np.stack([tx, z0, tz], -1), np.stack([tz, z0, -tx], -1)          # forward, right = cross(up, forward)

    def top(self, d, a):
        d = np.asarray(d, float); a = np.asarray(a, float)
        c = self.centre(d); _, r = self.frame(d)
        return c + r * a[..., None] if a.ndim else c + r * a

    def ground(self, d, a=0.0):
        p = self.top(d, a); return H(p[..., 0], p[..., 2])


class Rows:
    """Vehicle/temple road plan: SurfaceY across 25 samples (-3..3), rows .25 m apart; parameter = 3-D arc of the
    row centres (as the #296 code samples plan.Points)."""
    def __init__(self, plan):
        R = plan['Rows']
        self.C = np.array([v3(r['Centre']) for r in R]); self.Rt = np.array([v3(r['Right']) for r in R])
        self.S = np.array([r['SurfaceY'] for r in R], float); self.T = np.array([r['TerrainY'] for r in R], float)
        self.dry = np.array([bool(r['DryNext']) for r in R])
        P = np.array([v3(p) for p in plan['Points']])
        self.d = np.r_[0, np.cumsum(np.linalg.norm(np.diff(P, axis=0), axis=1))]; self.L = float(self.d[-1]); self.P = P
        self.n = len(R)

    def _q(self, d):
        q = np.interp(np.asarray(d, float), self.d, np.arange(self.n)); i = np.minimum(q.astype(int), self.n - 2); return i, q - i

    def centre(self, d):
        i, t = self._q(d); c = self.C[i] * (1 - t)[..., None] + self.C[i + 1] * t[..., None]
        c[..., 1] = self.S[i, 12] * (1 - t) + self.S[i + 1, 12] * t; return c

    def frame(self, d):
        i, t = self._q(d); r = self.Rt[i] * (1 - t)[..., None] + self.Rt[i + 1] * t[..., None]; r = r / np.linalg.norm(r, axis=-1, keepdims=True)
        f = np.stack([-r[..., 2], np.zeros_like(r[..., 0]), r[..., 0]], -1)
        return f, r

    def top(self, d, a):
        d = np.asarray(d, float); a = np.asarray(a, float); shp = np.broadcast_shapes(d.shape, a.shape)
        d = np.broadcast_to(d, shp); a = np.broadcast_to(a, shp)
        i, t = self._q(d); col = np.clip((a + 3) / .25, 0, 24); j = np.minimum(col.astype(int), 23); u = col - j
        c = self.C[i] * (1 - t)[..., None] + self.C[i + 1] * t[..., None]
        r = self.Rt[i] * (1 - t)[..., None] + self.Rt[i + 1] * t[..., None]
        Sa = self.S[i, j] * (1 - u) + self.S[i, j + 1] * u; Sb = self.S[i + 1, j] * (1 - u) + self.S[i + 1, j + 1] * u
        p = c + r * a[..., None]; p[..., 1] = Sa * (1 - t) + Sb * t
        return p

    def ground(self, d, a=0.0):
        d = np.asarray(d, float); a = np.asarray(a, float); shp = np.broadcast_shapes(d.shape, a.shape)
        d = np.broadcast_to(d, shp); a = np.broadcast_to(a, shp)
        i, t = self._q(d); col = np.clip((a + 3) / .25, 0, 24); j = np.minimum(col.astype(int), 23); u = col - j
        Ta = self.T[i, j] * (1 - u) + self.T[i, j + 1] * u; Tb = self.T[i + 1, j] * (1 - u) + self.T[i + 1, j + 1] * u
        out = Ta * (1 - t) + Tb * t
        far = np.abs(a) > 3.0                                              # beyond the plan: the height field
        if np.any(far):
            p = self.top(d, a); out = np.where(far, H(p[..., 0], p[..., 2]), out)
        return out

    def wet(self, d):
        i, _ = self._q(d); return ~self.dry[i]


class Composite:
    """Temple route: the retained 86-segment prefix ribbon, then the 55.25 m plan road."""
    def __init__(self, a: Polyline, b: Rows):
        self.a, self.b = a, b; self.La = a.L; self.L = a.L + b.L

    def _pick(self, d, fa, fb):
        d = np.asarray(d, float)
        if d.ndim == 0: return fa(d) if d <= self.La else fb(d - self.La)
        m = d <= self.La; ra = fa(np.where(m, d, 0.)); rb = fb(np.where(m, 0., d - self.La))
        mm = m.reshape(m.shape + (1,) * (np.ndim(ra) - m.ndim))
        return np.where(mm, ra, rb)

    def centre(self, d): return self._pick(d, self.a.centre, self.b.centre)

    def frame(self, d):
        fa = self._pick(d, lambda x: self.a.frame(x)[0], lambda x: self.b.frame(x)[0]); ra = self._pick(d, lambda x: self.a.frame(x)[1], lambda x: self.b.frame(x)[1])
        return fa, ra

    def top(self, d, a):
        d = np.asarray(d, float); a = np.asarray(a, float); shp = np.broadcast_shapes(d.shape, a.shape)
        d = np.broadcast_to(d, shp); a = np.broadcast_to(a, shp)
        ra = self.a.top(np.minimum(d, self.La), a); rb = self.b.top(np.maximum(d - self.La, 0.), a)
        return np.where((d <= self.La)[..., None], ra, rb)

    def ground(self, d, a=0.0):
        d = np.asarray(d, float); a = np.asarray(a, float); shp = np.broadcast_shapes(d.shape, a.shape)
        d = np.broadcast_to(d, shp); a = np.broadcast_to(a, shp)
        return np.where(d <= self.La, self.a.ground(np.minimum(d, self.La), a), self.b.ground(np.maximum(d - self.La, 0.), a))


def polyline_distance(p, P):
    """Distance from points p (...,3) to the polyline P (n,3) — 3-D, like DistanceBridgePolyline296."""
    p = np.asarray(p, float); sh = p.shape[:-1]; q = p.reshape(-1, 3); best = np.full(len(q), np.inf)
    for i in range(1, len(P)):
        a, b = P[i - 1], P[i]; dd = b - a; t = np.clip(((q - a) @ dd) / max(dd @ dd, 1e-8), 0, 1)
        best = np.minimum(best, np.linalg.norm(q - (a + t[:, None] * dd), axis=1))
    return best.reshape(sh)


def runs_of(mask, d, L, min_true=1.0):
    """Abutting (true_runs, false_runs) covering [0, L]: boundaries at sample midpoints; true runs < min_true dropped."""
    m = np.asarray(mask, bool).copy(); i = 0; n = len(m)
    while i < n:
        if not m[i]: i += 1; continue
        j = i
        while j + 1 < n and m[j + 1]: j += 1
        if (d[j] - d[i]) < min_true: m[i:j + 1] = False
        i = j + 1
    cuts = [0.0] + [float((d[k] + d[k + 1]) / 2) for k in range(n - 1) if m[k] != m[k + 1]] + [L]
    runs = [(cuts[k], cuts[k + 1], bool(m[min(n - 1, int(np.searchsorted(d, (cuts[k] + cuts[k + 1]) / 2)))])) for k in range(len(cuts) - 1)]
    return [(a, b) for a, b, v in runs if v and b - a > .05], [(a, b) for a, b, v in runs if not v and b - a > .05]


def intervals(mask, d, min_len=.3):
    out = []; i = 0; n = len(mask)
    while i < n:
        if not mask[i]: i += 1; continue
        j = i
        while j + 1 < n and mask[j + 1]: j += 1
        if d[j] - d[i] >= min_len: out.append((float(d[i]), float(d[j])))
        i = j + 1
    return out


# ------------------------------------------------------------------------------------------------------------------
# shared builders
# ------------------------------------------------------------------------------------------------------------------
def strip(mb, P, mat, out, us=2.0):
    su = np.concatenate([np.zeros((1, P.shape[1])), np.cumsum(np.linalg.norm(np.diff(P, axis=0), axis=-1), 0)], 0)
    sv = np.concatenate([np.zeros((P.shape[0], 1)), np.cumsum(np.linalg.norm(np.diff(P, axis=1), axis=-1), 1)], 1)
    mb.grid(P, mat, np.stack([su / us, sv / us], -1), out)


def columns(a, b, ds, extra=()):
    ss = np.r_[np.arange(a, b, ds), b, [e for e in extra if a < e < b]]
    return np.unique(ss)


def deck_paving(mb, route, a, b, W, mat, ds, inset=.05, drop=DECK_DROP):
    drop = drop + getattr(route, 'extra_drop', 0.0)
    ss = columns(a, b, ds); acr = np.array([-W / 2 + inset, 0., W / 2 - inset])
    P = route.top(ss[:, None], acr[None, :]); P[..., 1] -= drop
    strip(mb, P, mat, UP, us=1.8)


def apron_patch(mb, route, end, W, mat):
    """Flat stand-in for a retained bank apron (6 m behind the route end), 4 cm under it — LOD1/LOD2 only."""
    d0 = 0.0 if end == 0 else route.L
    f, r = route.frame(d0); c = route.centre(d0); back = -f if end == 0 else f
    rows = []
    for k in np.linspace(0, 6, 4):
        row = []
        for a in (-W / 2, 0., W / 2):
            p = c + back * k + r * a; g = float(H(p[0], p[2])) + .03
            t = np.clip((6 - k) / 6, 0, 1); t = t * t * (3 - 2 * t)
            row.append([p[0], max(g, g * (1 - t) + c[1] * t) - .04, p[2]])
        rows.append(row)
    strip(mb, np.array(rows), mat, UP, us=1.8)


# ------------------------------------------------------------------------------------------------------------------
# stone arch bridge
# ------------------------------------------------------------------------------------------------------------------
def stone_stations(route, step, test):
    st = []; d = 6.0
    while d < route.L:
        if test(d): st.append(d)
        d += step
    return st


def plan_arches(route, stations, W):
    """Bays between consecutive stations -> arch (semicircular if the water leaves room, else segmental) or solid."""
    bays = []
    for a, b in zip(stations[:-1], stations[1:]):
        sA, sB = a + PW / 2, b - PW / 2; c = sB - sA; mid = (sA + sB) / 2
        ds = np.linspace(sA, sB, 24); deck = route.centre(ds)[:, 1]
        cx = route.centre(ds)
        g = np.max([route.ground(ds, aa) for aa in (-W / 2, 0., W / 2)], axis=0)
        wl = WATER.at(cx[:, 0], cx[:, 2]); ref = np.maximum(g, np.where(wl > g, wl, -1e9))
        yc = float(deck.min()) - .30 - RING - .15
        ys = max(yc - c / 2, float(ref.max()) + .25)
        r = yc - ys
        if b - a > 13.5 or r < .9:
            bays.append(dict(a=a, b=b, arch=False)); continue
        R = (c * c / 4 + r * r) / (2 * r); cy = yc - R
        bays.append(dict(a=a, b=b, arch=True, sA=sA, sB=sB, mid=mid, R=R, cy=cy, ys=ys, yc=yc, rise=r, span=c,
                         semicircle=bool(abs(R - c / 2) < .02)))
    return bays


def intrados(bay, d):
    return bay['cy'] + np.sqrt(np.maximum(bay['R'] ** 2 - (np.asarray(d) - bay['mid']) ** 2, 0))


def pier_tops(stations, bays):
    tops = {}
    for k, s in enumerate(stations):
        adj = [bays[k - 1]] if k > 0 else []
        if k < len(bays): adj.append(bays[k])
        arch = [b for b in adj if b['arch']]
        tops[s] = max(b['ys'] for b in arch) if arch else None
    return tops


def wall_bottom(route, d, stations, bays, tops, half):
    d = np.asarray(d, float); out = np.min([route.ground(d, aa) for aa in (-half, 0., half)], axis=0) - .5
    for b in bays:
        if not b['arch']: continue
        m = (d >= b['sA']) & (d <= b['sB']); out = np.where(m, intrados(b, d) + RING, out)
    for s in stations:
        t = tops[s]
        if t is None: continue
        m = (d > s - PW / 2) & (d < s + PW / 2); out = np.where(m, t, out)
    return out


def stone_bridge(route, W, stations, bays, lod, vis, col, skip=None, main=True):
    half = W / 2 + HALF_EXTRA
    ds = [.45, 1.0, 2.5][lod]
    crit = [v for b in bays if b['arch'] for v in (b['sA'] - 1e-3, b['sA'] + 1e-3, b['sB'] - 1e-3, b['sB'] + 1e-3)]
    crit += [v for s in stations for v in (s - PW / 2 - 1e-3, s - PW / 2 + 1e-3, s + PW / 2 - 1e-3, s + PW / 2 + 1e-3)]
    ss = columns(0., route.L, ds, crit)
    keep = np.ones(len(ss), bool) if skip is None else skip(ss)
    tops = pier_tops(stations, bays) if main else {}
    for a, b in intervals(keep, ss, .2):
        sub = ss[(ss >= a) & (ss <= b)]
        deck_paving(vis, route, a, b, W, 'stone_plain', ds)
        for side in (-1, 1):
            # cornice (끝돌): top flush with the deck, face .3 m, underside back to the wall plane
            acr = np.array([side * (W / 2 - .05), side * (half + .12), side * (half + .12), side * half])
            P = route.top(sub[:, None], acr[None, :]); y = route.centre(sub)[:, 1]
            P[:, 0, 1] = y - DECK_DROP; P[:, 1, 1] = y - DECK_DROP; P[:, 2, 1] = y - .3; P[:, 3, 1] = y - .3
            _, r = route.frame(sub)
            strip(vis, P, 'stone_dressed', lambda Q, r=r, side=side: r[:, None, :] * side + UP * .6, us=1.2)
            # spandrel / side wall down to the arch extrados, the pier head or the ground
            bot = wall_bottom(route, sub, stations, bays, tops, half) if main else np.min([route.ground(sub, aa) for aa in (-half, 0., half)], axis=0) - .5
            topw = route.top(sub, side * half); topw[:, 1] = y - .3
            botw = topw.copy(); botw[:, 1] = np.minimum(bot, y - .32)
            strip(vis, np.stack([botw, topw], 1), 'stone_dressed', lambda Q, r=r, side=side: r[:, None, :] * side, us=2.0)
    if not main: return
    for b in bays:
        if not b['arch']: continue
        n = [16, 10, 5][lod]; dd = np.linspace(b['sA'], b['sB'], n + 1); yi = intrados(b, dd)
        # vault soffit
        acr = np.array([-half + .02, half - .02]); P = route.top(dd[:, None], acr[None, :]); P[..., 1] = yi[:, None]
        cen = route.top(np.array(b['mid']), 0.); cen = np.array([cen[0], b['cy'], cen[2]])
        strip(vis, P, 'stone_plain', lambda Q, cen=cen: cen - Q, us=1.6)
        # voussoirs on both faces (LOD0 blocks, LOD1/2 a ring band)
        th0 = math.atan2(float(intrados(b, b['sA']) - b['cy']), b['sA'] - b['mid']); th1 = math.atan2(float(intrados(b, b['sB']) - b['cy']), b['sB'] - b['mid'])
        arc = b['R'] * abs(th0 - th1); nv = max(9, int(round(arc / .52)) | 1)
        for side in (-1, 1):
            if lod == 0:
                for k in range(nv):
                    t0 = th0 + (th1 - th0) * k / nv; t1 = th0 + (th1 - th0) * (k + 1) / nv; g = .006
                    key = k == nv // 2; ro = b['R'] + RING + (.14 if key else 0.)
                    q = lambda t, rr: (b['mid'] + rr * math.cos(t), b['cy'] + rr * math.sin(t))
                    (d00, y00), (d01, y01) = q(t0 + g, b['R']), q(t1 - g, b['R']); (d10, y10), (d11, y11) = q(t0 + g, ro), q(t1 - g, ro)
                    face = side * (half + (.12 if key else .06)); back = side * (half - .25)
                    Wp = lambda dd_, yy, aa: (lambda q: np.array([q[0], yy, q[2]]))(route.top(np.array(dd_), aa))
                    A0, A1, B0, B1 = Wp(d00, y00, face), Wp(d01, y01, face), Wp(d10, y10, face), Wp(d11, y11, face)
                    a0, a1, b0, b1 = Wp(d00, y00, back), Wp(d01, y01, back), Wp(d10, y10, back), Wp(d11, y11, back)
                    _, rr_ = route.frame(np.array((d00 + d01) / 2)); outv = rr_ * side
                    vis.quad(A0, A1, B1, B0, 'stone_dressed', outv, uvscale=.6)                      # face
                    vis.quad(A0, A1, a1, a0, 'stone_dressed', cen - (A0 + A1) / 2, uvscale=.6)      # intrados edge
                    vis.quad(B0, B1, b1, b0, 'stone_dressed', (B0 + B1) / 2 - cen, uvscale=.6)      # extrados edge
                    tm = (A0 + B0) / 2 - (A1 + B1) / 2
                    vis.quad(A0, B0, b0, a0, 'stone_dressed', tm, uvscale=.6); vis.quad(A1, B1, b1, a1, 'stone_dressed', -tm, uvscale=.6)
            else:
                m = [10, 6][lod - 1]; tt = np.linspace(th0, th1, m + 1)
                inner = np.array([[b['mid'] + b['R'] * math.cos(t), b['cy'] + b['R'] * math.sin(t)] for t in tt])
                outer = np.array([[b['mid'] + (b['R'] + RING) * math.cos(t), b['cy'] + (b['R'] + RING) * math.sin(t)] for t in tt])
                rows = []
                for pr in (inner, outer):
                    p = route.top(pr[:, 0], side * (half + .05)); p[:, 1] = pr[:, 1]; rows.append(p)
                _, rr_ = route.frame(np.array(b['mid'])); strip(vis, np.stack(rows, 1), 'stone_dressed', rr_ * side, us=1.0)
    # piers with cutwaters
    for s in stations:
        t = tops[s]
        if t is None: continue
        f, r = route.frame(np.array(s)); c = route.centre(np.array(s))
        foot = float(np.min([route.ground(np.array(s + o), aa) for o in (-PW / 2, 0, PW / 2) for aa in (-half - 1.1, 0., half + 1.1)])) - .6
        cc = np.array([c[0], (foot + t) / 2, c[2]])
        obox(vis, cc, r, UP, f, [2 * half, t - foot, PW], 'stone_dressed', faces='zZ', uvscale=2.0)
        obox(col, cc, r, UP, f, [2 * half, t - foot - .1, PW], 'c', faces='xXyYzZ')
        for side in (-1, 1):
            base0 = c + r * side * half - f * PW / 2; base1 = c + r * side * half + f * PW / 2; apex = c + r * side * (half + 1.15)
            ytop = t - .35
            B0l, B1l, Al = [np.array([p[0], foot, p[2]]) for p in (base0, base1, apex)]
            B0h, B1h, Ah = [np.array([p[0], ytop, p[2]]) for p in (base0, base1, apex)]
            n0 = nz(np.cross(Ah - B0h, UP)); n0 = n0 if np.dot(n0, r * side - f) > 0 else -n0
            vis.quad(B0l, Al, Ah, B0h, 'stone_dressed', r * side - f, uvscale=2.0)
            vis.quad(Al, B1l, B1h, Ah, 'stone_dressed', r * side + f, uvscale=2.0)
            T0, T1 = np.array([base0[0], t, base0[2]]), np.array([base1[0], t, base1[2]])
            vis.poly([B0h, Ah, T0], 'stone_dressed', r * side * .6 - f + UP, uvscale=1.0)
            vis.poly([Ah, B1h, T1], 'stone_dressed', r * side * .6 + f + UP, uvscale=1.0)
            vis.poly([T0, Ah, T1], 'stone_dressed', r * side + UP, uvscale=1.0)
            nc = c + r * side * (half + .45)
            obox(col, [nc[0], (foot + ytop) / 2, nc[2]], r, UP, f, [.9, ytop - foot, PW * .5], 'c', faces='xXyYzZ')


def rail_runs_sided(route, W, others=(), skip=None, lo=4.0, hi=4.0):
    ss = np.arange(lo, route.L - hi, .25); out = {}
    for side in (-1, 1):
        keep = np.ones(len(ss), bool)
        e = route.top(ss, side * (W / 2 + .12))
        for o in others: keep &= polyline_distance(e, o) >= W / 2 - .18
        if skip is not None: keep &= skip(ss)
        out[side] = intervals(keep, ss, .8)
    return out


# ------------------------------------------------------------------------------------------------------------------
# timber bridge
# ------------------------------------------------------------------------------------------------------------------
def timber_deck(route, W, a, b, lod, vis, rng):
    over = .15; aa = W / 2 + over
    if lod == 0:
        s = a
        while s < b - .02:
            pw = rng.uniform(.26, .31); e = min(b, s + pw); m = (s + e) / 2
            L_ = route.top(np.array(m), -aa); R_ = route.top(np.array(m), aa); ax = nz(R_ - L_)
            p0 = route.centre(np.array(s)); p1 = route.centre(np.array(e)); az = nz(p1 - p0); ay = nz(np.cross(az, ax))
            if ay[1] < 0: ay = -ay
            c = (L_ + R_) / 2 - ay * (DECK_DROP + .035) + ay * rng.uniform(-.006, .006)
            obox(vis, c, ax, ay, az, [2 * aa + rng.uniform(-.05, .05), .07, max(e - s - .014, .05)], 'wood_board', faces='yYzZxX', uvscale=1.0)
            s = e
    else:
        ss = columns(a, b, [.5, 1.0, 3.0][lod]); P = route.top(ss[:, None], np.array([-aa, 0, aa])[None, :]); P[..., 1] -= DECK_DROP
        strip(vis, P, 'wood_board', UP, us=1.2); Pb = P.copy(); Pb[..., 1] -= .07; strip(vis, Pb, 'wood_board', -UP, us=1.2)


def timber_stringers(route, W, a, b, pos, lod, vis):
    if lod == 2: return
    ss = columns(a, b, [2.0, 3.0][lod])
    for u in pos:
        for s0, s1 in zip(ss[:-1], ss[1:]):
            A = route.top(np.array(s0), u); B = route.top(np.array(s1), u)
            A = A - UP * (DECK_DROP + .07 + .15); B = B - UP * (DECK_DROP + .07 + .15)
            beam(vis, A, B, .22, .3, 'wood_dark')


def bent(route, W, s, spec, lod, vis, col):
    f, r = route.frame(np.array(s)); c = route.centre(np.array(s)); deck = float(c[1])
    cap_y = deck - DECK_DROP - .07 - .3 - .225
    ext = W / 2 + .45
    A = route.top(np.array(s), -ext); B = route.top(np.array(s), ext); A[1] = B[1] = cap_y
    beam(vis, A, B, .4, .45, 'wood_dark')                                               # 멍에
    tops, bots = [], []
    for u in spec['posts']:
        p = route.top(np.array(s), u); g = float(route.ground(np.array(s), u)); wl = float(WATER.at(p[0], p[2]))
        fy = max(g + .45, wl + .45 if wl > g else -1e9)                                 # footing top above water
        obox(vis, [p[0], (g - .4 + fy) / 2, p[2]], r, UP, f, [.95, fy - g + .4, .95], 'stone_dressed', faces='xXzZY', uvscale=1.0)
        obox(col, [p[0], (g - .4 + fy) / 2, p[2]], r, UP, f, [.95, fy - g + .4, .95], 'c')
        top = np.array([p[0], cap_y - .22, p[2]]); bot = np.array([p[0], fy, p[2]])
        vis.cyl(bot, top, .18, .17, [8, 6, 4][lod], 'wood_dark', caps=False)
        obox(col, (top + bot) / 2, r, UP, f, [.34, top[1] - bot[1], .34], 'c')
        tops.append(top); bots.append(bot)
    hgt = float(min(t[1] - b_[1] for t, b_ in zip(tops, bots)))
    if spec.get('raked') and hgt > 2.5:                                                  # raked outer legs (gorge trestle)
        for side in (-1, 1):
            u = spec['posts'][0 if side < 0 else -1]; lean = .13 * hgt
            p = route.top(np.array(s), u + side * lean); g = float(route.ground(np.array(s), u + side * lean)); wl = float(WATER.at(p[0], p[2]))
            fy = max(g + .45, wl + .45 if wl > g else -1e9)
            obox(vis, [p[0], (g - .4 + fy) / 2, p[2]], r, UP, f, [.85, fy - g + .4, .85], 'stone_dressed', faces='xXzZY', uvscale=1.0)
            topl = route.top(np.array(s), side * (ext - .15)); topl[1] = cap_y - .22
            vis.cyl(np.array([p[0], fy, p[2]]), topl, .16, .15, [8, 6, 4][lod], 'wood_dark', caps=False)
            obox(col, [p[0], (g - .4 + fy) / 2, p[2]], r, UP, f, [.85, fy - g + .4, .85], 'c')
    if lod < 2 and hgt > 2.2:                                                           # ties + X braces
        lo = max(b_[1] for b_ in bots) + .3; hi = cap_y - .5; lv = max(1, int(math.ceil((hi - lo) / 3.6)))
        ys = np.linspace(lo, hi, lv + 1)
        pa, pb = tops[0], tops[-1]
        for k in range(lv):
            y0, y1 = ys[k], ys[k + 1]
            P0 = np.array([pa[0], y0, pa[2]]); P1 = np.array([pb[0], y1, pb[2]]); Q0 = np.array([pb[0], y0, pb[2]]); Q1 = np.array([pa[0], y1, pa[2]])
            beam(vis, P0 - r * .2, Q0 + r * .2, .18, .18, 'wood_dark')
            if lod == 0 or k % 2 == 0: beam(vis, P0, P1, .16, .16, 'wood_dark'); beam(vis, Q0, Q1, .16, .16, 'wood_dark')
    return hgt


def timber_railing(route, W, runs, lod, vis, spacing=3.92, first=4.04):
    edge = W / 2 + .12
    for side in (-1, 1):
        for a, b in runs[side]:
            posts = [p for p in np.arange(first, route.L, spacing) if a - .05 <= p <= b + .05]
            posts = sorted(set([a] + posts + [b]))
            if lod > 0:
                k = 2 if lod == 1 else 4; posts = sorted(set(posts[::k] + [posts[-1]]))
            for p in posts:
                f, r = route.frame(np.array(p)); c = route.top(np.array(p), side * edge)
                y = float(route.centre(np.array(p))[1])
                obox(vis, [c[0], y + .45, c[2]], r, UP, f, [.16, 1.1, .16], 'wood_dark', faces='xXzZY', uvscale=1.0)
            for p0, p1 in zip(posts[:-1], posts[1:]):
                for h, sec in ((.9, (.12, .14)), (.48, (.09, .1))):
                    if lod == 2 and h < .8: continue
                    A = route.top(np.array(p0), side * edge); B = route.top(np.array(p1), side * edge)
                    A[1] = float(route.centre(np.array(p0))[1]) + h; B[1] = float(route.centre(np.array(p1))[1]) + h
                    beam(vis, A, B, sec[0], sec[1], 'wood_dark')


def causeway(route, W, a, b, lod, vis):
    """Dry vehicle-road rows: paving on the retained surface, dressed coping, battered retaining walls to the ground."""
    ds = [.5, 1.0, 2.5][lod]; ss = columns(a, b, ds)
    P = route.top(ss[:, None], np.array([-W / 2, -W / 4, 0., W / 4, W / 2])[None, :]); P[..., 1] -= DECK_DROP
    strip(vis, P, 'stone_plain', UP, us=1.8)
    _, r = route.frame(ss)
    for side in (-1, 1):
        e = side * W / 2
        top = route.top(ss, e); y = top[:, 1].copy()
        cop = route.top(ss[:, None], np.array([e, e + side * .3, e + side * .3])[None, :])
        cop[:, 0, 1] = y - DECK_DROP; cop[:, 1, 1] = y - DECK_DROP; cop[:, 2, 1] = y - .28
        strip(vis, cop, 'stone_dressed', lambda Q, r=r, side=side: r[:, None, :] * side + UP * .5, us=1.2)
        g = route.ground(ss, e + side * .3)
        hh = np.maximum(y - .28 - (g - .4), .05)
        w0 = route.top(ss, e + side * .3); w0[:, 1] = y - .28
        w1 = route.top(ss, e + side * (.3 + .14 * hh)); w1[:, 1] = np.minimum(g - .4, y - .3)
        strip(vis, np.stack([w1, w0], 1), 'stone_rough', lambda Q, r=r, side=side: r[:, None, :] * side + UP * .1, us=2.4)


def end_wall(route, W, s, facing, vis):
    """Stone abutment face across the road where a timber deck meets the causeway / ground."""
    f, r = route.frame(np.array(s)); acr = np.linspace(-W / 2 - .3, W / 2 + .3, 5)
    top = route.top(np.full(len(acr), s), acr); y = float(route.centre(np.array(s))[1])
    top[:, 1] = y - .09
    g = route.ground(np.full(len(acr), s), acr); bot = top.copy(); bot[:, 1] = np.minimum(g - .5, y - .2)
    strip(vis, np.stack([bot, top], 1), 'stone_rough', f * facing, us=2.0)


# ------------------------------------------------------------------------------------------------------------------
# hide list from the scene (visual children only)
# ------------------------------------------------------------------------------------------------------------------
def scene_children(ids):
    """Names of the direct children of Architecture296_Crossings/<id> in W_Demo_Main (text YAML, read-only)."""
    if not SCENE.exists(): return None
    objs, cur = {}, None
    with open(SCENE, encoding='utf-8', errors='replace') as fh:
        for line in fh:
            if line.startswith('--- !u!'):
                m = re.match(r'--- !u!(\d+) &(-?\d+)', line); cur = dict(t=m.group(1), id=m.group(2)); objs[cur['id']] = cur; continue
            if cur is None: continue
            if cur['t'] == '1' and line.startswith('  m_Name: '): cur['name'] = line[10:].rstrip('\n')
            elif cur['t'] == '4':
                if line.startswith('  m_GameObject: {fileID: '): cur['go'] = line.split('fileID: ')[1].split('}')[0]
                elif line.startswith('  m_Father: {fileID: '): cur['father'] = line.split('fileID: ')[1].split('}')[0]
    tr = {o['go']: o for o in objs.values() if o['t'] == '4' and 'go' in o}
    name = lambda go: objs.get(go, {}).get('name', '?')
    def parent(go):
        t = tr.get(go); f = t.get('father', '0') if t else '0'
        return objs[f]['go'] if f in objs and 'go' in objs[f] else None
    roots = [go for go, o in objs.items() if o['t'] == '1' and o.get('name') == 'Architecture296_Crossings' and parent(go) is None]
    out = {i: [] for i in ids}
    for go, o in objs.items():
        if o['t'] != '1': continue
        p = parent(go)
        if p is None: continue
        pp = parent(p)
        if pp in roots and name(p) in out: out[name(p)].append(o.get('name', '?'))
    return out


def hide_paths(ids):
    kids = scene_children(ids); paths = []
    for i in ids:
        pats = [rf'^{re.escape(i)}_deck_\d+$', rf'^{re.escape(i)}_loadbearing_\d+$', r'^LOD1_\d+$', r'^LOD2_\d+$',
                rf'^{re.escape(i)}_0_vehicle_aprons_\d+$', rf'^{re.escape(i)}_0_temple_aprons_\d+$']
        names = sorted(n for n in (kids or {}).get(i, []) if any(re.match(p, n) for p in pats))
        paths += [f'Architecture296_Crossings/{i}/{n}' for n in names]
    return paths, kids


# ------------------------------------------------------------------------------------------------------------------
def main():
    OUT.mkdir(parents=True, exist_ok=True); (OUT / 'Meshes').mkdir(exist_ok=True)
    for f in (OUT / 'Meshes').glob('*.json'): f.unlink()
    receipt = json.loads((A296 / 'crossings.json').read_text(encoding='utf-8-sig'))
    B = {b['Id']: b for b in receipt['Bridges']}
    meshes, routes, stats, layout = [], [], {}, dict(note=__doc__.strip().splitlines()[0], bridges={})

    def export(name, kind, lods, col):
        for lod, mb in enumerate(lods): mb.export(OUT / 'Meshes' / f'{name}_LOD{lod}.json')
        if col is not None and col.V: col.export(OUT / 'Meshes' / f'{name}_Collision.json')
        meshes.append(dict(name=name, kind=kind, world=True)); stats[name] = dict(tris=[mb.tris() for mb in lods], collision=col.tris() if col is not None else 0)

    def route_points(route, rid):
        ss = np.unique(np.r_[np.arange(0, route.L, 1.5), route.L]); p = route.centre(ss); p[:, 1] += .05
        routes.append(dict(id=rid, points=[round(float(v), 3) for q in p for v in q]))

    # ---- stone: capital (two routes share the deck), temple ----------------------------------------------------------
    for bid in ('capital_shared_stone_bridge', 'hyeongang__temple_bridge'):
        b = B[bid]; W = float(b['Width']); spec = BRIDGES[bid]
        if bid == 'hyeongang__temple_bridge':
            plan = json.loads((A296 / 'BridgeApproach/hyeongang__temple.json').read_text(encoding='utf-8-sig'))
            pts = np.array([v3(p) for p in b['Routes'][0]['Points']])
            main_route = Composite(Polyline(pts[:87], 0.0), Rows(plan)); extra = []
            test = lambda d: float(main_route.centre(np.array(d))[1] - H(*main_route.centre(np.array(d))[[0, 2]])) >= 1.2 and any(
                float(main_route.centre(np.array(d))[1] - H(*main_route.top(np.array(d), sd * .32 * W)[[0, 2]])) >= 1.2 for sd in (-1, 1))
            aprons = [(main_route, 0)]
        else:
            r0 = b['Routes'][0]; r1 = b['Routes'][1]
            main_route = Polyline([v3(p) for p in r0['Points']], .04); second = Polyline([v3(p) for p in r1['Points']], .04)
            second.extra_drop = .008; extra = [(second, r1['Id'])]
            test = lambda d: float(main_route.centre(np.array(d))[1] - H(*main_route.centre(np.array(d))[[0, 2]])) > 1.2
            aprons = [(main_route, 0), (main_route, 1), (second, 0), (second, 1)]
        st = stone_stations(main_route, spec['step'], test); bays = plan_arches(main_route, st, W)
        lods_d, lods_r = [MB(f'Bridge297_{bid}_Deck') for _ in range(3)], [MB(f'Bridge297_{bid}_Rail') for _ in range(3)]
        col = MB(f'Bridge297_{bid}_Deck_collision')
        for lod in range(3):
            stone_bridge(main_route, W, st, bays, lod, lods_d[lod], col if lod == 0 else MB('x'))
            others = [e[0].P for e in extra]
            runs = rail_runs_sided(main_route, W, others=others)
            for side in (-1, 1): stone_railing_side(main_route, W, runs[side], lod, lods_r[lod], side)
            for rt, rid in extra:                                          # second route: its own bank part only
                skip = lambda ss, rt=rt: polyline_distance(rt.centre(ss), main_route.P) >= .25
                skip_deck = lambda ss, rt=rt: polyline_distance(rt.centre(ss), main_route.P) >= W / 2
                stone_bridge(rt, W, [], [], lod, lods_d[lod], MB('x'), skip=skip_deck, main=False)
                runs2 = rail_runs_sided(rt, W, others=[main_route.P], skip=skip)
                for side in (-1, 1): stone_railing_side(rt, W, runs2[side], lod, lods_r[lod], side)
            if lod > 0:
                for rt, end in aprons: apron_patch(lods_d[lod], rt, end, W, 'stone_plain')
        export(f'Bridge297_{bid}_Deck', 'bridge', lods_d, col)
        export(f'Bridge297_{bid}_Rail', 'bridge', lods_r, None)
        route_points(main_route, f'bridge297_{b["Routes"][0]["Id"]}')
        for rt, rid in extra: route_points(rt, f'bridge297_{rid}')
        layout['bridges'][bid] = dict(kind='stone 홍예교', width=W, length=round(main_route.L, 1), stations=[round(s, 2) for s in st],
                                      arches=[dict(span=round(x['span'], 2), rise=round(x['rise'], 2), radius=round(x['R'], 2), semicircle=x['semicircle'],
                                                   spring=round(x['ys'], 2), crown=round(x['yc'], 2)) for x in bays if x['arch']],
                                      solidBays=sum(1 for x in bays if not x['arch']))
    # ---- timber: jeokro vehicle road, hwanggyeong gorge trestle ---------------------------------------------------
    for bid in ('jeokro__cheolong_bridge', 'mountain_hwanggyeong_main_bridge'):
        b = B[bid]; W = float(b['Width']); spec = BRIDGES[bid]; rng = np.random.default_rng(2974 + len(bid))
        if bid == 'jeokro__cheolong_bridge':
            plan = json.loads((A296 / 'BridgeApproach/jeokro__cheolong.json').read_text(encoding='utf-8-sig')); route = Rows(plan)
            ss = np.arange(0, route.L, .25); timber_mask = route.wet(ss)
            def test(d):
                c = route.centre(np.array(d))
                for u in (-.32 * W, .32 * W):
                    p = route.top(np.array(d), u); g = float(route.ground(np.array(d), u)); wl = float(WATER.at(p[0], p[2]))
                    if wl > g + .2 and c[1] - g >= 1.2: return True
                return False
            aprons = []
        else:
            route = Polyline([v3(p) for p in b['Routes'][0]['Points']], .04)
            ss = np.arange(0, route.L, .25); timber_mask = (route.centre(ss)[:, 1] - np.max([route.ground(ss, a) for a in (-W / 2, 0, W / 2)], axis=0)) > .6
            test = lambda d: float(route.centre(np.array(d))[1] - H(*route.centre(np.array(d))[[0, 2]])) > 1.2
            aprons = [(route, 0), (route, 1)]
        st = stone_stations(route, spec['step'], test)
        spans, solid = runs_of(timber_mask, ss, route.L, 1.0)
        lods_d, lods_s, lods_r = [MB(f'Bridge297_{bid}_Deck') for _ in range(3)], [MB(f'Bridge297_{bid}_Structure') for _ in range(3)], [MB(f'Bridge297_{bid}_Rail') for _ in range(3)]
        col = MB(f'Bridge297_{bid}_Structure_collision'); heights = []
        runs = rail_runs_sided(route, W)
        for lod in range(3):
            for a, e in spans:
                timber_deck(route, W, a, e, lod, lods_d[lod], rng)
                timber_stringers(route, W, a, e, spec['stringers'], lod, lods_s[lod])
                for s_, fac in ((a, 1), (e, -1)): end_wall(route, W, s_, fac, lods_s[lod])
            for a, e in solid:
                if bid == 'jeokro__cheolong_bridge': causeway(route, W, a, e, lod, lods_d[lod])
                else:
                    deck_paving(lods_d[lod], route, a, e, W, 'stone_plain', [.5, 1., 2.5][lod], inset=0.)
            for s_ in st:
                if any(a - .5 <= s_ <= e + .5 for a, e in spans):
                    h = bent(route, W, s_, spec, lod, lods_s[lod], col if lod == 0 else MB('x'))
                    if lod == 0: heights.append(h)
            timber_railing(route, W, runs, lod, lods_r[lod])
            if lod > 0:
                for rt, end in aprons: apron_patch(lods_d[lod], rt, end, W, 'stone_plain')
        export(f'Bridge297_{bid}_Deck', 'bridge', lods_d, None)
        export(f'Bridge297_{bid}_Structure', 'bridge', lods_s, col)
        export(f'Bridge297_{bid}_Rail', 'bridge', lods_r, None)
        route_points(route, f'bridge297_{b["Routes"][0]["Id"]}')
        layout['bridges'][bid] = dict(kind='timber', width=W, length=round(route.L, 1), stations=[round(s, 2) for s in st],
                                      timberSpans=[[round(a, 1), round(e, 1)] for a, e in spans], solidSpans=[[round(a, 1), round(e, 1)] for a, e in solid],
                                      bentHeights=[round(h, 1) for h in heights])
    ids = list(BRIDGES)
    hide, kids = hide_paths(ids)
    layout['sceneChildren'] = kids; layout['hide'] = hide; layout['stats'] = stats
    kept = {i: sorted(n for n in (kids or {}).get(i, []) if f'Architecture296_Crossings/{i}/{n}' not in hide) for i in ids}
    layout['kept'] = {i: sorted(set(re.sub(r'_\d+$', '_*', n) for n in v)) for i, v in kept.items()}
    (OUT / 'layout.json').write_text(json.dumps(layout, indent=1, ensure_ascii=False), encoding='utf-8')
    unity = dict(compound='Bridges', root='Finish297_Bridges', meshes=meshes, hide=hide, clip=[], markers=[], gates=[], routes=routes, doors=[], terrainOps=[])
    (OUT / 'unity.json').write_text(json.dumps(unity, indent=1), encoding='utf-8')
    for bid in ids:
        t = [v['tris'][0] for k, v in stats.items() if k.startswith(f'Bridge297_{bid}_')]
        print(json.dumps(dict(bridge=bid, LOD0tris=sum(t), **{k: v for k, v in layout['bridges'][bid].items() if k in ('length', 'stations', 'solidBays', 'timberSpans', 'bentHeights')},
                              arches=len(layout['bridges'][bid].get('arches', []))), ensure_ascii=False))
    print(json.dumps(dict(meshes=len(meshes), hide=len(hide), routes=[r['id'] for r in routes]), indent=1))


def stone_railing_side(route, W, runs, lod, vis, side):
    """One side of the 돌난간 along the given runs."""
    edge = W / 2 + .12; e = side * edge
    for a, b in runs:
        L = b - a
        if L < .8: continue
        ss = columns(a, b, [.5, 1.0, 3.0][lod]); y = route.centre(ss)[:, 1]; _, r = route.frame(ss)
        P = route.top(ss[:, None], np.array([e - side * .17, e - side * .17, e + side * .17])[None, :])
        P[:, 0, 1] = y - DECK_DROP; P[:, 1, 1] = y + .16; P[:, 2, 1] = y + .16
        strip(vis, P, 'stone_dressed', lambda Q, r=r: -r[:, None, :] * side + UP * 1.5, us=1.0)
        Po = route.top(ss[:, None], np.array([e + side * .17, e + side * .17])[None, :]); Po[:, 0, 1] = y + .16; Po[:, 1, 1] = y - .3
        strip(vis, Po, 'stone_dressed', lambda Q, r=r: r[:, None, :] * side, us=1.0)
        if lod == 2:
            for sg in (1, -1):
                Pw = route.top(ss[:, None], np.array([e + sg * .06, e + sg * .06])[None, :]); Pw[:, 0, 1] = y + .16; Pw[:, 1, 1] = y + .78
                strip(vis, Pw, 'stone_plain', lambda Q, r=r, sg=sg: r[:, None, :] * sg, us=1.0)
            continue
        n = max(1, int(math.ceil(L / (1.9 if lod == 0 else 3.8)))); dp = np.linspace(a, b, n + 1)
        for k, dk in enumerate(dp):
            f, rr = route.frame(np.array(dk)); c = route.top(np.array(dk), e); yk = float(route.centre(np.array(dk))[1])
            if k == 0 or k == n:                                          # 법수
                obox(vis, [c[0], yk + .16 + .5, c[2]], rr, UP, f, [.3, 1.0, .3], 'stone_plain', faces='xXzZ', uvscale=1.0)
                vis.cyl([c[0], yk + 1.16, c[2]], [c[0], yk + 1.25, c[2]], .19, .19, 8, 'stone_plain', caps=True)
                vis.cyl([c[0], yk + 1.25, c[2]], [c[0], yk + 1.42, c[2]], .13, .04, 8, 'stone_plain', caps=False)
            else:                                                         # 동자주 + 하엽
                obox(vis, [c[0], yk + .16 + .27, c[2]], rr, UP, f, [.19, .54, .19], 'stone_plain', faces='xXzZ', uvscale=1.0)
                if lod == 0: obox(vis, [c[0], yk + .16 + .58, c[2]], rr, UP, f, [.24, .08, .3], 'stone_plain', faces='xXzZY', uvscale=1.0)
        for k in range(n):                                                # 돌란대
            p0 = route.top(np.array(dp[k]), e); p1 = route.top(np.array(dp[k + 1]), e)
            y0 = float(route.centre(np.array(dp[k]))[1]); y1 = float(route.centre(np.array(dp[k + 1]))[1])
            A = np.array([p0[0], y0 + .86, p0[2]]); B_ = np.array([p1[0], y1 + .86, p1[2]])
            if lod == 0: vis.cyl(A, B_, .075, .075, 6, 'stone_plain')
            else: beam(vis, A, B_, .14, .14, 'stone_plain')


if __name__ == '__main__':
    main()
