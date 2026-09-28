"""Site-specific highland trail sections for #293 (Step 1 Cheongrim, Step 2 the other four realms).

The accepted 285/289 trail is a deep-valley cliff walk (its foundation reaches ~116m below the
path). Warping that scene onto a 292 mountainside exposed the foundation as a huge flat face, and
one warped copy per mountain repeated the same silhouette. Sections are therefore generated in
place from the actual slope: a shelf cut into the mountainside, a jointed rock band on the uphill
side, a short valley apron that meets the ground, and a designed 1m terrain. Terrain must stay
below every exposed rock/trail skin ("ceiling"); the refiner fits the 4m game grid under it.

Pure numpy so both Blender (mesh finishing) and the terrain refiner import it.
"""
from pathlib import Path
import json, math
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
H293 = ROOT / 'Art/World/Compact/Rebuild/Highlands293'
VARIANTS = H293 / 'Variants'
CELL, GW, GH = 4.0, 1001, 1501
FINE = 1.0  # designed terrain spacing; aligned with the 4m grid

# Lengths/heights are TEST values. Cheongrim is the Step 1 section set approved for art review (unchanged).
SPEC = {
    'mountain_cheongrim': dict(realm='Cheongrim', seed=2931, kinds=[('A', 60.), ('B', 72.), ('C', 54.)],
                               gap=24., margin=32.),
    # Step 2 (user-approved 2026-09-27): upper windows ≈197 / 156 / 279 / 531 m
    'mountain_jeokro': dict(realm='Jeokro', seed=2932, kinds=[('A', 52.), ('B', 58.), ('C', 42.)], gap=18., margin=30.,
                            # main-line repair: the #292 path climbs a 43–53° scarp (grade 0.71) between the junctions
                            repairs=[('A', 224., 254.)]),
    'mountain_cheolong': dict(realm='Cheolong', seed=2933, kinds=[('A', 58.), ('C', 50.)], gap=20., margin=28.),
    'mountain_hyeongang': dict(realm='Hyeongang', seed=2934, kinds=[('A', 50.), ('B', 66.), ('C', 64.)], gap=22., margin=30.),
    'mountain_hwanggyeong': dict(realm='Hwanggyeong', seed=2935, kinds=[('A', 64.), ('B', 70.), ('C', 46.)], gap=30., margin=32.),
}
# Realm geology and trail building (LDB realm table, #290 realm contract, ART-SKY seeds). Empty = Cheongrim granite.
#  cliff/protrude/apron scale the kind values · lean replaces the kind's face lean range · joints feed Joints()
#  sky scales the crest (swell, facet, joint steps) · facet scales fracture faces · round/round_len the crest roll-back
#  bedding: horizontal layers (thickness, step back, dip) · hjoints: horizontal cracks (spacing, depth, width)
#  sheets: exfoliation sheets (count range, depth) · overhang: (start height above the path, depth) · span: walkway length
#  stairs: natural | slab (long flat treads) | cut (old stone road, regular) · bridge=False turns C into a stone terrace
STYLE = {
    'Cheongrim': dict(),
    'Jeokro': dict(cliff=.62, protrude=.7, apron=1.25, lean=(.06, .16), facet=1.2, round=.9, round_len=2.4,
                   joints=dict(width=(1.8, 4.2), amp=1.25, edge=.55), hjoints=((1.1, 2.4), (.25, .6), .2),
                   sky=(.12, .40, .32), span=(4.2, 5.2), stones='broken'),
    'Cheolong': dict(cliff=1.0, lean=(-.02, .05), facet=.6, width=.2, joints=dict(width=(5., 11.), amp=.55, crack=.45, ledges=(0, 1)),
                     bedding=((.4, 1.2), (.12, .55), .035), sky=(.08, .16, .10), span=(5.6, 7.0), stairs='slab', stones='slab'),
    'Hyeongang': dict(cliff=1.12, lean=(.0, .07), facet=.9, overhang=(3.4, .95), span=(9.0, 11.5), stones='natural'),
    'Hwanggyeong': dict(cliff=.9, lean=(.16, .28), facet=.4, round=2.2, round_len=3.6, view_width=2.6,
                        joints=dict(width=(6., 14.), amp=.5, crack=.35, ledges=(0, 1)), sheets=((2, 5), (.25, .6)),
                        sky=(.30, .10, .05), stairs='cut', stones='cut', bridge=False),
}
STONE_SETS = {'natural': 16, 'broken': 12, 'slab': 12, 'cut': 10}  # Blender variants per set (build_highland293.py)
KIND = {
    # cliff: wanted rock band height (m) · protrude: allowed rise above the natural ground behind
    # apron: valley drop below the path edge · lean: face run per metre of height (cot of angle)
    'A': dict(cliff=10., protrude=5., apron=3.4, lean=(.14, .30), wiggle=.9, width=1.8),
    'B': dict(cliff=12., protrude=6., apron=3.0, lean=(.10, .24), wiggle=1.3, width=1.85),
    'C': dict(cliff=8., protrude=4., apron=6.5, lean=(.16, .32), wiggle=.8, width=1.9),
}


def smooth(a, b, x):
    t = np.clip((np.asarray(x, float) - a) / (b - a), 0, 1)
    return t * t * (3 - 2 * t)


def load_base():
    h = np.fromfile(H293 / 'baseline-height.bytes', dtype='<f4').reshape(GH, GW).astype(np.float64)
    layout = json.loads((H293 / 'baseline-layout.json').read_text(encoding='utf8'))
    return h, layout


def sample(a, x, z, cell=CELL, x0=0., z0=0.):
    xx = np.clip((np.asarray(x, float) - x0) / cell, 0, a.shape[1] - 1.001)
    zz = np.clip((np.asarray(z, float) - z0) / cell, 0, a.shape[0] - 1.001)
    ix = xx.astype(int); iz = zz.astype(int); u = xx - ix; v = zz - iz
    return (1 - v) * ((1 - u) * a[iz, ix] + u * a[iz, ix + 1]) + v * ((1 - u) * a[iz + 1, ix] + u * a[iz + 1, ix + 1])


def mountain(layout, mid):
    return next(m for m in layout['Mountains'] if m['Id'] == mid)


def polyline(points):
    p = np.array([[v['x'], v['y'], v['z']] for v in points], float)
    d = np.r_[0, np.cumsum(np.linalg.norm(np.diff(p[:, [0, 2]], axis=0), axis=1))]
    return p, d


def along(p, d, s):
    s = np.asarray(s, float)
    return np.stack([np.interp(s, d, p[:, i]) for i in range(3)], axis=-1)


def steepness(h):
    dz, dx = np.gradient(h, CELL)
    return np.degrees(np.arctan(np.hypot(dx, dz)))


def select_sections(h, layout, mid):
    """Ordered, non-overlapping windows on the upper main path with the steepest surroundings."""
    spec = SPEC[mid]; m = mountain(layout, mid); p, d = polyline(m['MainPath'])
    junctions = []
    for key, index in (('TemplePath', 0), ('ReturnPath', -1)):
        q = np.array([m[key][index]['x'], m[key][index]['z']])
        junctions.append(float(d[np.argmin(np.linalg.norm(p[:, [0, 2]] - q, axis=1))]))
    lo, hi = max(junctions) + spec['margin'], d[-1] - spec['margin']
    slope = steepness(h)
    ss = np.arange(0, d[-1], 2.)
    offs = np.r_[np.arange(-28, -3, 3), np.arange(4, 29, 3)].astype(float)
    score = np.zeros(len(ss))
    for k, s in enumerate(ss):
        q = along(p, d, s); t = along(p, d, min(d[-1], s + 1.5)) - along(p, d, max(0, s - 1.5))
        t = t[[0, 2]] / (np.linalg.norm(t[[0, 2]]) + 1e-9); r = np.array([t[1], -t[0]])
        score[k] = sample(slope, q[0] + r[0] * offs, q[2] + r[1] * offs).max()
    prefix = np.r_[0, np.cumsum(score)]

    def mean(s0, length):
        a, b = np.searchsorted(ss, s0), np.searchsorted(ss, s0 + length)
        return (prefix[b] - prefix[a]) / max(1, b - a)
    protected = [pl for pl in layout['Places'] if not pl['Id'].startswith('mountain_')]

    def clear(s0, length):
        pts = along(p, d, np.arange(s0, s0 + length, 4.))
        return all(np.min(np.hypot(pts[:, 0] - pl['XZ']['x'], pts[:, 2] - pl['XZ']['y'])) > max(20, pl.get('GroundRadius', 18)) + 30
                   for pl in protected)
    kinds, gap = spec['kinds'], spec['gap']
    starts = np.arange(lo, hi, 4.)
    best = None
    for count in sorted({len(kinds), 2}, reverse=True):
        use = kinds if count == len(kinds) else [kinds[0], kinds[-1]]
        def place(i, begin, acc, chosen):
            nonlocal best
            if i == len(use):
                if best is None or acc / len(use) > best[0]:
                    best = (acc / len(use), list(chosen))
                return
            kind, length = use[i]
            for s0 in starts[starts >= begin]:
                if s0 + length > hi:
                    break
                if not clear(s0, length):
                    continue
                chosen.append((kind, float(s0), float(s0 + length)))
                place(i + 1, s0 + length + gap, acc + mean(s0, length), chosen)
                chosen.pop()
        place(0, lo, 0., [])
        if best is not None:
            break
    if best is None:
        raise RuntimeError('No section placement for ' + mid)
    return dict(mountain=mid, realm=spec['realm'], lo=lo, hi=hi, junctions=junctions, length=float(d[-1]),
                score=float(best[0]), sections=best[1])


# ---------------------------------------------------------------- deterministic rock detail
def hash2(x, t, seed):
    n = np.sin(x * 127.1 + t * 311.7 + seed * 74.7) * 43758.5453
    return n - np.floor(n)


def facet(u, v, scale, seed):
    """Piecewise planar fracture faces (285 facet with a per-section seed), vectorised."""
    u = np.asarray(u, float) / scale; v = np.asarray(v, float) / scale
    i = np.floor(u); j = np.floor(v); a = u - i; b = v - j
    lower = a + b < 1
    r1 = hash2(i, j, seed) * (1 - a - b) + hash2(i + 1, j, seed) * a + hash2(i, j + 1, seed) * b
    r2 = hash2(i + 1, j + 1, seed) * (a + b - 1) + hash2(i, j + 1, seed) * (1 - a) + hash2(i + 1, j, seed) * (1 - b)
    return np.where(lower, r1, r2)


class Joints:
    """Granite structure for one section face (all offsets recess into the rock, never toward the path).

    Vertical joint set: blocks/pillars 2.5–7m wide separated by terminating cracks.
    Sheeting ledges: broken shelves where the face steps back (pine/grass pockets).
    Flakes: detached plates that stand proud of a recess.
    """

    def __init__(self, rng, length, height, amp=1., width=(2.5, 7.), crack=.8, ledges=(1, 4), edge=.28):
        x, cuts = -6., []
        self.edge = edge
        while x < length + 6:
            x += rng.uniform(*width)
            cuts.append(x)
        self.cuts = np.array(cuts)
        self.depth = rng.uniform(0, 1.7, len(cuts) + 1) * amp          # block recess (piecewise constant)
        self.crack = [(c, rng.uniform(.35, 1.2) * amp, rng.uniform(.18, .42), rng.uniform(-.1, .35) * height,
                       rng.uniform(.55, 1.15) * height) for c in cuts if rng.random() < crack]
        self.skew = rng.uniform(.03, .10) * rng.choice([-1, 1])
        self.ledges = []
        for _ in range(int(rng.integers(*ledges)) if ledges[1] > ledges[0] else ledges[0]):
            a = rng.uniform(-4, length - 6); self.ledges.append((rng.uniform(2.4, max(3., height - 1.8)), rng.uniform(.5, 1.6) * amp,
                                                                a, a + rng.uniform(7, 24)))
        self.flakes = [(rng.uniform(.25, .75) * height, rng.uniform(0, length), rng.uniform(5, 11), rng.uniform(.8, 1.6) * amp)
                       for _ in range(max(2, int(length / 20)))]

    def __call__(self, s, h):
        s = np.asarray(s, float); h = np.asarray(h, float); q = s + self.skew * h
        e = self.edge
        blocks = self.depth[0] + sum((self.depth[k + 1] - self.depth[k]) * smooth(c - e, c + e, q) for k, c in enumerate(self.cuts))
        cracks = sum(a * np.exp(-((q - c) / w) ** 2) * smooth(lo - .8, lo + .8, h) * (1 - smooth(hi - .8, hi + .8, h))
                     for c, a, w, lo, hi in self.crack)
        ledges = sum(d * smooth(hl - .22, hl + .22, h) * smooth(a, a + 2.5, s) * (1 - smooth(b - 2.5, b, s))
                     for hl, d, a, b in self.ledges)
        flakes = 0
        for cy, cz, extent, depth in self.flakes:
            mask = np.maximum(0, 1 - np.abs((s - cz) / extent)); dy = h - cy - .24 * (s - cz)
            flakes = flakes + depth * mask * np.maximum(0, 1 - np.abs(dy / 5.5))
        return blocks + cracks + ledges - flakes


# ---------------------------------------------------------------- section
class Section:
    SPACING = .18

    def __init__(self, h, m, kind, s0, s1, index, realm, seed):
        self.h, self.kind, self.realm, self.s0, self.s1 = h, kind, realm, s0, s1
        self.id = m['Id'].replace('mountain_', '') + '_' + str(index) + kind
        self.rng = np.random.default_rng(seed + index * 101)
        self.style = st = STYLE.get(realm, {})
        self.K = dict(KIND[kind])
        for key in ('cliff', 'protrude', 'apron'):
            self.K[key] *= st.get(key, 1.)
        if 'lean' in st:
            self.K['lean'] = st['lean']
        self.K['width'] += st.get('width', 0.)
        p, d = polyline(m['MainPath']); self.orig = (p, d)
        # a window that descends along the main path is built from its lower end, so every kind ascends in its own
        # frame (stairs, walkway, viewpoint at the top); the driver splices it back in main-path order
        self.path_s = (s0, s1)
        self.reversed = float(along(p, d, s1)[1]) < float(along(p, d, s0)[1]) - .5
        if self.reversed:
            p, d = p[::-1].copy(), d[-1] - d[::-1]; s0, s1 = float(d[-1] - s1), float(d[-1] - s0); self.s0, self.s1 = s0, s1
        self._route(p, d)
        self._profile(p, d)
        self._frame()
        self._cliff()
        self._apron()

    # route in plan: smooth curve from A to B through resampled original points and a small wiggle
    def _route(self, p, d):
        s0, s1, rng = self.s0, self.s1, self.rng
        n = max(2, int(round((s1 - s0) / 20)))
        ctrl_s = np.r_[s0 - 20, np.linspace(s0, s1, n + 1), s1 + 20]
        ctrl = along(p, d, ctrl_s)[:, [0, 2]]
        tang = np.gradient(ctrl, axis=0); tang /= np.linalg.norm(tang, axis=1)[:, None]; right = np.c_[tang[:, 1], -tang[:, 0]]
        wig = rng.uniform(-1, 1, len(ctrl)) * self.K['wiggle']
        wig[:2] = 0; wig[-2:] = 0
        ctrl = ctrl + right * wig[:, None]
        # uniform Catmull-Rom through A..B (phantom end points keep the joins tangent-continuous)
        pts = []
        for i in range(1, len(ctrl) - 2):
            a, b, c, e = ctrl[i - 1], ctrl[i], ctrl[i + 1], ctrl[i + 2]
            for u in np.linspace(0, 1, 120, endpoint=False):
                pts.append(.5 * ((2 * b) + (-a + c) * u + (2 * a - 5 * b + 4 * c - e) * u * u + (-a + 3 * b - 3 * c + e) * u ** 3))
        pts.append(ctrl[-2]); pts = np.array(pts)
        arc = np.r_[0, np.cumsum(np.linalg.norm(np.diff(pts, axis=0), axis=1))]
        self.length = L = float(arc[-1])
        self.N = N = int(round(L / self.SPACING)) + 1
        self.s = np.linspace(0, L, N)
        self.xz = np.c_[np.interp(self.s, arc, pts[:, 0]), np.interp(self.s, arc, pts[:, 1])]

    # heights, terraces, widths and types along the route
    def _profile(self, p, d):
        L, rng, kind = self.length, self.rng, self.kind
        yA, yB = float(along(p, d, self.s0)[1]), float(along(p, d, self.s1)[1])
        R = yB - yA
        segs = []  # (type, a, b, rise)
        if kind == 'A':
            shelf = .28 * L; g = min(.03, max(0., R) / L); rs = g * shelf
            f = R - rs; f1 = .56 * f
            len1 = max(6., min(.36 * L, abs(f1) / .20 * .72)); len2 = max(6., min(.30 * L, abs(f - f1) / .20 * .72))
            a1 = shelf + 2; b1 = a1 + len1; a2 = b1 + rng.uniform(1.6, 2.6); b2 = min(L - 3, a2 + len2)
            # steep windows (main-line repairs): a flight averaging more than ~0.62 packs >0.26m per step and the walker
            # meets 45°+ contact; the shelf and landings then give their length to the flights (no extra random draw)
            if max(f1 / max(1e-6, b1 - a1), (f - f1) / max(1e-6, b2 - a2)) > .62:
                shelf = float(np.clip(L - f / .55 - 4., 3., shelf)); rs = g * shelf; f = R - rs; f1 = .56 * f
                a1 = shelf + 1.; span = L - 1.5 - a1 - 1.; b1 = a1 + span * .56; a2 = b1 + 1.; b2 = L - 1.5
            segs = [('ramp', 0, shelf, rs), ('ramp', shelf, a1, 0), ('flight', a1, b1, f1), ('flat', b1, a2, 0),
                    ('flight', a2, b2, f - f1), ('flat', b2, L, 0)]
        elif kind == 'B':
            fr = .42 * R; lenf = max(8., min(.30 * L, abs(fr) / .19 * .72)); c = .52 * L
            a1, b1 = c - lenf / 2, c + lenf / 2
            segs = [('ramp', 0, a1, (R - fr) * .5), ('flight', a1, b1, fr), ('ramp', b1, L, (R - fr) * .5)]
        else:  # C: ramp → timber walkway over a gully → stone flight → pine viewpoint
            view = 7.; span = rng.uniform(*self.style.get('span', (5.2, 6.8))); ba = .30 * L; bb = ba + span
            if span > 8:  # a long walkway starts earlier so the flight keeps its length
                ba = .22 * L; bb = ba + span
            r1 = min(.16 * ba, max(0., .3 * R)); fa = bb + 2.; fb = L - view - 2.5
            fr = max(0., R - r1 - .2 - .15)
            walk = 'bridge' if self.style.get('bridge', True) else 'ramp'  # Hwanggyeong: an old stone terrace, no timber
            segs = [('ramp', 0, ba, r1), (walk, ba, bb, .2), ('flat', bb, fa, 0), ('flight', fa, fb, fr),
                    ('ramp', fb, L - view, .15), ('flat', L - view, L, 0)]
            self.bridge = (ba, bb) if walk == 'bridge' else None
            self.span = span
            self.gully = 3. if span <= 7 else span / 2.2  # gully width under the walkway (Step 1 spans keep 3m)
        if kind != 'C':
            self.bridge = None
        if not self.bridge:
            self.gully = 3.
        stairs = self.style.get('stairs', 'natural')
        # height along s; stairs use 285-style terraces (sloped treads, skewed/bent risers)
        y = np.zeros(self.N); yg = np.zeros(self.N); base = yA; self.terraces = []; types = np.zeros(self.N, int)
        for typ, a, b, rise in segs:
            sel = (self.s >= a) & (self.s <= b + 1e-9)
            if typ == 'flight' and rise > .2:
                steps = []
                length = b - a
                # natural: irregular treads/risers · slab: long flat treads (bedding shelves) · cut: old stone road, regular
                tread = {'natural': .95, 'slab': 1.25, 'cut': .9}[stairs]
                count = int(np.clip(max(math.ceil(rise / .185), round(length / tread)), 2, int(length / .34)))
                count = min(count, int(length / .36))
                spare = length - count * .34
                conc, share_conc, wander = {'natural': (5., 12., 1.), 'slab': (4., 10., .8), 'cut': (60., 90., .12)}[stairs]
                depth = .34 + rng.dirichlet(np.full(count, conc)) * spare  # tread ≥ 34cm
                cuts = a + np.r_[0, np.cumsum(depth)]
                share = rng.dirichlet(np.full(count, share_conc)) * rise
                ya = base
                skew, bend = float(rng.uniform(-.2, .2)) * wander, float(rng.uniform(-.11, .11)) * wander
                for k in range(count):
                    yb = ya + share[k]
                    lo_j, hi_j = (.84, .92) if stairs == 'cut' else (.62, .86)
                    jump = float(np.clip(share[k] * rng.uniform(lo_j, hi_j), .06, .195)); jump = min(jump, share[k])
                    # consecutive riser lines may wander, but never so far that a side tread shrinks below 26cm
                    # (two risers stacked at the edge would exceed the player's step offset)
                    room = max(0., depth[k] - .26)
                    skew = float(np.clip(skew + rng.uniform(-.6, .6) * room * wander, -.2, .2))
                    bend = float(np.clip(bend + rng.uniform(-.3, .3) * room * wander, -.11, .11))
                    steps.append(dict(a=float(cuts[k]), b=float(cuts[k + 1]), ya=float(ya), yb=float(yb), jump=jump, skew=skew, bend=bend))
                    ya = yb
                for t in steps:
                    ss = (self.s >= t['a']) & (self.s < t['b'])
                    y[ss] = t['ya'] + (t['yb'] - t['ya'] - t['jump']) * (self.s[ss] - t['a']) / (t['b'] - t['a'])
                self.terraces += steps; types[sel] = 1
            else:
                y[sel] = base + rise * (self.s[sel] - a) / max(1e-6, b - a)
                if typ == 'bridge':
                    types[sel] = 2
            yg[sel] = base + rise * (self.s[sel] - a) / max(1e-6, b - a)  # geology never inherits stair steps
            base += rise
        y[-1] = yB; yg[-1] = yB
        self.yg = yg
        if kind == 'C':
            types[self.s >= L - 7] = 3
        self.y, self.types, self.yA, self.yB, self.R = y, types, yA, yB, R
        ph = rng.uniform(0, 6.3)
        w = self.K['width'] + .22 * np.sin(self.s * .13 + ph) ** 2 - .12
        if kind == 'B':
            w -= .45 * np.exp(-((self.s - .52 * L) / 5.) ** 2)  # narrow squeeze at the buttress bend
        if kind == 'C':
            w += self.style.get('view_width', 1.75) * smooth(L - 10, L - 5, self.s)  # pine viewpoint shelf / stone terrace
        self.w = np.maximum(w, 1.25)

    def surface(self, s):
        """Walking height (with terrace risers) at arc s."""
        return np.interp(s, self.s, self.y)

    def geo(self, s):
        """Smooth reference height for rock, apron and terrain (no stair quantisation)."""
        return np.interp(s, self.s, self.yg)

    def _frame(self):
        xz, s = self.xz, self.s
        t = np.gradient(xz, axis=0); t /= np.linalg.norm(t, axis=1)[:, None]
        # smoothed normals keep large lateral offsets from folding on bends
        k = int(6 / self.SPACING); ker = np.ones(2 * k + 1) / (2 * k + 1)
        ts = np.c_[np.convolve(np.pad(t[:, 0], k, mode='edge'), ker, 'valid'), np.convolve(np.pad(t[:, 1], k, mode='edge'), ker, 'valid')]
        ts /= np.linalg.norm(ts, axis=1)[:, None]
        right = np.c_[ts[:, 1], -ts[:, 0]]
        probe = sample(self.h, xz[:, 0] + right[:, 0] * 14, xz[:, 1] + right[:, 1] * 14) - sample(self.h, xz[:, 0] - right[:, 0] * 14, xz[:, 1] - right[:, 1] * 14)
        self.up_sign = 1 if probe.mean() >= 0 else -1
        self.tan, self.nup = ts, right * self.up_sign  # nup: horizontal unit toward the uphill side
        L = self.length
        self.we = smooth(0, 9, s) * (1 - smooth(L - 9, L, s))  # rock/terrain edit weight (0 at the joins)
        self.wt = smooth(0, 2, s) * (1 - smooth(L - 2, L, s))  # walking surface hand-over to the natural path
        t2 = np.gradient(self.xz, self.s, axis=0); t2 /= np.linalg.norm(t2, axis=1)[:, None]
        heading = np.unwrap(np.arctan2(t2[:, 0], t2[:, 1]))
        curvature = np.abs(np.gradient(heading, self.s))
        self.min_radius = float(1 / max(1e-6, np.convolve(curvature, np.ones(9) / 9, 'same').max()))

    def at(self, s, u, y):
        """World point at arc s, lateral u (+uphill), absolute height y."""
        s = np.asarray(s, float); u = np.asarray(u, float)
        x = np.interp(s, self.s, self.xz[:, 0]) + np.interp(s, self.s, self.nup[:, 0]) * u
        z = np.interp(s, self.s, self.xz[:, 1]) + np.interp(s, self.s, self.nup[:, 1]) * u
        return np.stack([x, np.broadcast_to(y, x.shape), z], axis=-1)

    def natural(self, s, u):
        q = self.at(s, u, 0.)
        return sample(self.h, q[..., 0], q[..., 2])

    # uphill rock band: height H(s), lean, joints, cap diving back into the designed ground
    CAP = np.array([.3, .7, 1.1, 1.6, 2.2, 3., 4., 5.2, 6.6, 8.2, 10., 12.])

    def _cliff(self):
        rng, K, s, L = self.rng, self.K, self.s, self.length
        self.fseed = float(rng.uniform(0, 1000))
        env = smooth(2, 14, s) * (1 - smooth(L - 14, L - 2, s))
        lam1, lam2 = rng.uniform(22, 36), rng.uniform(8, 13)
        st = self.style
        self.joints = Joints(rng, L, max(4., K['cliff']), **st.get('joints', {}))
        j = self.joints
        blocks = j.depth[0] + sum((j.depth[k + 1] - j.depth[k]) * smooth(c - j.edge, c + j.edge, s) for k, c in enumerate(j.cuts))
        # crest: long swell + angular facets + height steps where vertical joints part the blocks (realm scaled)
        swell, fac, step = st.get('sky', (.22, .26, .13))
        sky = (1 + swell * np.sin(2 * np.pi * s / lam1 + rng.uniform(0, 6.3)) + fac * (facet(s, 3.7, lam2, self.fseed) - .5)
               - step * blocks + .06)
        for c in rng.uniform(.18, .82, 2) * L:  # gullies notch the crest
            sky -= .38 * np.exp(-((s - c) / 3.2) ** 2)
        if self.bridge:
            c = .5 * (self.bridge[0] + self.bridge[1]); sky -= .55 * np.exp(-((s - c) / (self.gully + .5)) ** 2)
        want = K['cliff'] * env * sky
        uf = self.w / 2 + .11 + .07 * np.sin(s * .72)
        lean = np.interp(s, np.linspace(0, L, 7), rng.uniform(*K['lean'], 7))
        yy = self.yg
        need = self.natural(s, uf + 3) - yy + .8
        far = self.natural(s, uf + 3 + lean * want + 8) - yy + K['protrude']
        H = np.clip(want, np.maximum(1.2, need), np.maximum(need, far))
        self.H, self.uf, self.lean = H, uf, lean
        self.Lcap = float(self.CAP[-1])
        top = float(H.max()) + 2
        # realm structure (drawn after the Step 1 values so Cheongrim keeps its random sequence)
        self.beds = []
        if 'bedding' in st:  # horizontal layers stepping back: shelves along the face, each ledge edge broken
            (t0, t1), (b0, b1), dip = st['bedding']; h = rng.uniform(.3, .8)
            while h < top:
                self.beds.append((h, rng.uniform(b0, b1), rng.uniform(-dip, dip), rng.uniform(0, 6.3))); h += rng.uniform(t0, t1)
        self.hj = []
        if 'hjoints' in st:  # horizontal cracks: with the vertical set they part the face into weathered blocks
            (g0, g1), (a0, a1), wj = st['hjoints']; h = rng.uniform(.8, 1.6)
            while h < top:
                self.hj.append((h, rng.uniform(a0, a1), wj * rng.uniform(.8, 1.3), rng.uniform(-.04, .04))); h += rng.uniform(g0, g1)
        self.sheets = []
        if 'sheets' in st:  # exfoliation sheets: broad shallow steps whose edges run diagonally across the dome
            (n0, n1), (d0, d1) = st['sheets']
            for _ in range(int(rng.integers(n0, n1))):
                self.sheets.append((rng.uniform(1.4, max(1.6, top - 3)), rng.uniform(.08, .25) * rng.choice([-1, 1]), rng.uniform(d0, d1), rng.uniform(0, L)))

    def back(self, s):
        """Uneven distance over which a protruding spur falls back to the natural slope."""
        s = np.asarray(s, float)
        return 20 + 9 * np.sin(s * .11 + self.fseed) + 5 * np.sin(s * .31 + 2 * self.fseed)

    def face_u(self, s, h, H):
        """Lateral offset of the face at arc s and height h above the path (vectorised)."""
        s = np.asarray(s, float); h = np.asarray(h, float); H = np.asarray(H, float)
        lean = np.interp(s, self.s, self.lean) * (.4 + .6 * smooth(2, 10, h))
        st = self.style
        frac = (2.1 * facet(s + .18 * h, h - .13 * s, 7.3, self.fseed) + .7 * facet(s + 17, h, 2.8, self.fseed) +
                .15 * facet(s, h, 1.05, self.fseed)) * np.clip(h / 1.8, 0, 1) * st.get('facet', 1.)
        joint = np.maximum(0., self.joints(s, h)) * smooth(1.2, 5., h)
        # exfoliated granite rolls back at the crest (Hwanggyeong: a full dome shoulder)
        rounded = st.get('round', .55) * smooth(H - st.get('round_len', 1.6), H + .2, h)
        u = np.interp(s, self.s, self.uf) + lean * np.maximum(h, 0) + frac + .55 * joint + rounded
        L = self.length
        for hk, step, dip, ph in self.beds:  # each bedding plane steps the face back (ledges 0.1–0.7m)
            edge = hk + dip * (s - L / 2) + .12 * np.sin(s * .19 + ph)
            u = u + step * (.35 + 1.3 * facet(s, np.full_like(s, hk * 3.1), 5.5, self.fseed + hk)) * smooth(edge - .08, edge + .08, h)
        for hk, depth, width, dip in self.hj:  # horizontal joint grooves
            edge = hk + dip * (s - L / 2)
            u = u + depth * (.5 + facet(s, np.full_like(s, hk * 2.3), 4.2, self.fseed + 7)) * np.exp(-((h - edge) / width) ** 2)
        for h0, slope, depth, s0 in self.sheets:  # sheet edges: the outer sheet ends and the face steps back
            edge = h0 + slope * (s - s0)
            u = u + depth * smooth(edge - .35, edge + .35, h)
        if 'overhang' in st:  # a roof over the path well above head height (Hyeongang gorge wall)
            h0, depth = st['overhang']
            var = .5 + .8 * facet(s, np.full_like(s, 7.7), 9., self.fseed + 5)
            u = u - depth * var * smooth(h0, h0 + 2.2, h) * (1 - smooth(H - 1.4, H + .2, h))
        return u

    def behind(self, s, capd, top_u, H, y, nat=None):
        """Designed ground behind the band top: a spur that falls back to the natural slope.
        nat: the ground's own natural height (terrain cells pass it; resampling at at(s, u) drifts far from bends)."""
        level = y + H - .4
        nat = self.natural(s, top_u + capd) if nat is None else nat
        buried = nat >= level + .2  # the natural slope already covers the top edge
        T = np.where(buried, nat, nat + (np.maximum(level, nat) - nat) * (1 - smooth(1.5, 1.5 + self.back(s), capd)))
        return T, buried

    def cap_skin(self, s, capd, top_u, H, y, nat=None):
        T, buried = self.behind(s, capd, top_u, H, y, nat)
        lip = y + H - .30 * capd - .08                          # a short exposed crest edge
        under = T - .45 - .25 * np.maximum(0, capd - 1.5)          # then under the designed ground
        skin = np.where(capd < 1.0, lip, np.minimum(lip, under))
        return skin, T, buried, lip

    def face_grid(self, ds=.22, rows=58):
        """(cols, rows+cap, 3) world vertices; rows run foot→top then along the diving cap."""
        s = np.linspace(0, self.length, int(self.length / ds) + 1)
        H = np.interp(s, self.s, self.H); y = self.geo(s); we = np.interp(s, self.s, self.we)
        v = np.linspace(0, 1, rows)[None, :]
        h = -.45 + v * (H[:, None] + .45)
        if self.bridge:  # the foot drops into the gully under the walkway
            c = .5 * (self.bridge[0] + self.bridge[1]); g = 3.2 * np.exp(-((s - c) / self.gully) ** 2)
            h = h - g[:, None] * (1 - v)
        u = self.face_u(np.repeat(s[:, None], rows, 1), h, np.repeat(H[:, None], rows, 1))
        top_u = u[:, -1]
        capd = np.repeat(self.CAP[None, :], len(s), 0); Sc = np.repeat(s[:, None], capd.shape[1], 1)
        skin, _, _, _ = self.cap_skin(Sc, capd, top_u[:, None], H[:, None], y[:, None])
        skin = skin + .12 * (facet(Sc, capd * .8, 1.6, self.fseed) - .5) * smooth(1., 3., capd)
        U = np.c_[u, top_u[:, None] + capd]; Y = np.c_[y[:, None] + h, skin]
        Y = Y - ((1 - we) * (H + 1.6))[:, None]  # bury the band where the edit fades out
        S = np.repeat(s[:, None], U.shape[1], 1)
        return self.at(S, U, Y), S, U, Y

    # valley apron: a drop matched to the natural slope (slight ledge on gentle ground, a real drop on steep ground)
    def _apron(self):
        rng, s, w, y = self.rng, self.s, self.w, self.y
        drop6 = y - self.natural(s, -(w / 2 + 6)); drop14 = y - self.natural(s, -(w / 2 + 14))
        self.g = np.clip((drop14 - drop6) / 8, .03, 1.2)
        var = 1 + .25 * np.sin(s * .21 + rng.uniform(0, 6.3)) + .12 * np.sin(s * .67)
        self.D = np.clip(.9 * drop6 * var, .7, self.K['apron']) * self.we
        self.lam = rng.uniform(1.0, 2.0)
        self.E = np.array([0, .12, .35, .7, 1.2, 1.8, 2.6, 3.5, 4.6, 5.9, 7.4, 9., 10.6, 12.3, 14., 15.8, 17.6])
        self.Eband = 9.

    def valley(self, S, E, y, nat):
        """(design line, rock skin without detail, designed ground) for valley offsets E ≥ 0."""
        D = np.interp(S, self.s, self.D); g = np.interp(S, self.s, self.g)
        design = y - .06 - D * (1 - np.exp(-E / self.lam)) - g * E
        if self.bridge:
            c = .5 * (self.bridge[0] + self.bridge[1]); design = design - 3.4 * np.exp(-((S - c) / self.gully) ** 2) * smooth(0, 1.5, E)
        skin = np.minimum(design, nat + .25)
        Eb = 2 * self.lam + 1.2 + .4 * D  # rock is exposed across the actual drop only
        T = nat + (np.minimum(nat, design - .35) - nat) * (1 - smooth(Eb, Eb + 12, E))
        T = np.where(E <= Eb, np.minimum(T, skin - .3), T)
        return design, skin, T, Eb

    def apron_grid(self, ds=.25):
        s = np.linspace(0, self.length, int(self.length / ds) + 1)
        y = self.geo(s); w = np.interp(s, self.s, self.w); we = np.interp(s, self.s, self.we)
        E = np.repeat(self.E[None, :], len(s), 0); S = np.repeat(s[:, None], E.shape[1], 1)
        U = -(w[:, None] / 2 - .12) - E  # the top row tucks under the trail's valley edge (no slit)
        nat = self.natural(S, U)
        design, skin, T, Eb = self.valley(S, E, y[:, None], nat)
        skin = skin + .22 * (facet(S, E * 3.1, 1.7, self.fseed + 3) - .5) * smooth(.3, 1.5, E)
        skin = np.where(E > Eb, np.minimum(skin, T - .6 * (E - Eb)), skin)  # dive under the ground
        skin = skin - ((1 - we) * 1.6)[:, None]
        return self.at(S, U, skin), S, U, skin

    # walking surface: 285 Carved_trail construction; visual (soil/rock) and collision variants
    LEAD = 5.  # trail lead-in at each end: the 4m grid fit can sag the ground up to ~0.8m in the cell before a join

    def at_ext(self, s, u, y):
        """at() with straight run-outs past both ends along the end tangents (identical to at() inside [0, L])."""
        s = np.asarray(s, float); u = np.asarray(u, float); inside = np.clip(s, 0, self.length)
        before = np.minimum(s, 0.); after = np.maximum(s - self.length, 0.)
        x = np.interp(inside, self.s, self.xz[:, 0]) + np.interp(inside, self.s, self.nup[:, 0]) * u + self.tan[0, 0] * before + self.tan[-1, 0] * after
        z = np.interp(inside, self.s, self.xz[:, 1]) + np.interp(inside, self.s, self.nup[:, 1]) * u + self.tan[0, 1] * before + self.tan[-1, 1] * after
        return np.stack([x, np.broadcast_to(y, x.shape), z], axis=-1)

    def trail_rows(self, a, b, collision=False):
        # the visual soil has no risers: set stones form the visible steps, collision keeps the stepped walk
        breaks = {t['b']: t for t in self.terraces if a < t['b'] < b} if collision else {}
        step = .25 if collision else .10
        ss = sorted(set([float(x) for x in np.arange(a, b, step)] + [b] + list(breaks)))
        rows = []
        if a <= 1e-6:  # lead-in from the natural path before the section
            rows += [(float(x), None, 'lead') for x in np.arange(-self.LEAD, 0., step)]
        for s in ss:
            if s in breaks:
                t = breaks[s]; rows += [(s, t['yb'] - t['jump'], t), (s, t['yb'] - .025, t), (s + .035, t['yb'], t)]
            elif not any(x < s < x + .04 for x in breaks):
                rows.append((s, float(self.surface(s)), None))
        if b >= self.length - 1e-6:  # lead-out onto the natural path after it
            rows += [(float(x), None, 'lead') for x in np.arange(self.length + step, self.length + self.LEAD + 1e-6, step)]
        return rows

    def terrace_at(self, s):
        return next((t for t in self.terraces if t['a'] <= s < t['b']), None)

    def trail_grid(self, a, b, collision=False, nc=17):
        rows = self.trail_rows(a, b, collision); nc = 5 if collision else nc
        V = []
        wear = lambda s: .09 * math.sin(s * 1.73) + .055 * math.sin(s * 4.17 + 2) + .035 * math.sin(s * 8.71)
        for s, y, edge in rows:
            lead = edge == 'lead'
            if lead:  # from the section's edge height to the natural ground (collision 2cm under it at the far end)
                off = .02 if collision else .03; edge_s = 0. if s < 0 else self.length
                q = self.at_ext(s, 0., 0.); nat = float(sample(self.h, q[0], q[2])) - off
                qe = self.at_ext(edge_s, 0., 0.); edge_gap = float(self.surface(edge_s)) - .08 - (float(sample(self.h, qe[0], qe[2])) - off)
                y = nat + edge_gap * (1 - min(1., abs(s - edge_s) / self.LEAD))
                w = float(self.w[0] if s < 0 else self.w[-1]); wt = 1.; active = None
            else:
                w = float(np.interp(s, self.s, self.w)); wt = float(np.interp(s, self.s, self.wt))
                active = edge or self.terrace_at(s)
            row = []
            for j in range(nc):
                t = j / (nc - 1) * 2 - 1
                lateral = t * w / 2 + max(0, t) ** 3 * .45 + min(0, t) ** 3 * (wear(s) + .08)
                shift = 0.
                if active:
                    target = active['skew'] * t + active['bend'] * math.sin(t * 4)
                    prev = next((q for q in self.terraces if abs(q['b'] - active['a']) < 1e-3), None)
                    start = (prev['skew'] * t + prev['bend'] * math.sin(t * 4)) if prev else 0
                    alpha = 1 if edge else (s - active['a']) / (active['b'] - active['a'])
                    shift = start * (1 - alpha) + target * alpha
                elif not collision:
                    shift = .07 * math.sin(s * .63 + t * 3) * t
                yy = y
                if not collision:
                    yy += (.032 * math.sin(s * 1.31 + t * 4) + .015 * math.sin(s * 4.2 - t * 7)) * abs(t) - .023 * (1 - t * t)
                    if active:
                        # soil ramp under the stones: riser-free (±15cm average) and 12cm below their tops
                        yy += float(np.mean(self.surface(np.linspace(s - .15, s + .15, 7)))) - y - .12
                yy -= (1 - wt) * .08  # joins: begin just under the natural path surface
                row.append((s + shift, lateral, yy))
            if not collision:
                e = row[0]; row.insert(0, (e[0], e[1] - .03, e[2] - .5))  # fascia under the valley edge
            V.append(row)
        V = np.array(V)  # (rows, nc, 3): s, u, y
        return self.at_ext(V[..., 0], V[..., 1], V[..., 2]), V

    # designed 1m terrain and the ceiling every coarse cell must respect (only where a skin is exposed)
    def terrain_patch(self, pad=64.):
        pts = np.c_[self.xz[:, 0], self.xz[:, 1]]
        x0 = math.floor((pts[:, 0].min() - pad) / CELL) * CELL; x1 = math.ceil((pts[:, 0].max() + pad) / CELL) * CELL
        z0 = math.floor((pts[:, 1].min() - pad) / CELL) * CELL; z1 = math.ceil((pts[:, 1].max() + pad) / CELL) * CELL
        gx = np.arange(x0, x1 + .5, FINE); gz = np.arange(z0, z1 + .5, FINE)
        X, Z = np.meshgrid(gx, gz)
        nat = sample(self.h, X, Z)
        # project cells onto the route (sub-sampled nearest search, then along-tangent refinement)
        idx = np.arange(0, self.N, 5); rp = self.xz[idx]
        best = np.full(X.shape, 1e18); arg = np.zeros(X.shape, int)
        for k, q in enumerate(rp):
            dd = (X - q[0]) ** 2 + (Z - q[1]) ** 2
            m = dd < best; best[m] = dd[m]; arg[m] = k
        i = idx[arg]
        dx = X - self.xz[i, 0]; dz = Z - self.xz[i, 1]
        along_t = dx * self.tan[i, 0] + dz * self.tan[i, 1]
        raw = self.s[i] + along_t
        # refine onto the section's own normal lines (the exact inverse of at()): the nearest-sample projection
        # scattered far cells by up to ±1.5m of arc on bends, and where the band height changes quickly along s
        # (crest gullies) that scatter became a 1–3m comb in the ground behind the crest
        for _ in range(4):
            Sc = np.clip(raw, 0, self.length)
            tx = np.interp(Sc, self.s, self.tan[:, 0]); tz = np.interp(Sc, self.s, self.tan[:, 1]); tn = np.hypot(tx, tz)
            f = (X - np.interp(Sc, self.s, self.xz[:, 0])) * tx / tn + (Z - np.interp(Sc, self.s, self.xz[:, 1])) * tz / tn
            raw = raw + .85 * np.where((raw > 0) & (raw < self.length), f, 0)  # beyond the ends keep the linear run-out
        S = np.clip(raw, 0, self.length)
        ex = X - np.interp(S, self.s, self.xz[:, 0]); ez = Z - np.interp(S, self.s, self.xz[:, 1])
        U = ex * np.interp(S, self.s, self.nup[:, 0]) + ez * np.interp(S, self.s, self.nup[:, 1])
        past = np.maximum(0, np.maximum(-raw, raw - self.length))
        fade = 1 - smooth(0, 6, past)
        yt = self.surface(S); y = self.geo(S); w = np.interp(S, self.s, self.w)
        we = np.interp(S, self.s, self.we) * fade; wt = np.interp(S, self.s, self.wt) * fade
        H = np.interp(S, self.s, self.H)
        T = nat.copy(); ceil = np.full(X.shape, np.inf)
        # walking corridor (trail mesh on top); strict ceiling once the hand-over is complete
        trail = np.abs(U) < w / 2 + .45
        T = np.where(trail, nat + (np.minimum(nat, yt - 1.5) - nat) * wt, T)
        ceil = np.where(trail & (wt > .9), yt - .36, ceil)  # visual soil in flights sits up to ~0.22m under the tread tops
        # uphill: behind the band (first height at which it reaches this offset), cap, raise behind
        up = U > w / 2
        if up.any():
            Su, Uu, Hu, yu, nu, weu = S[up], U[up], H[up], y[up], nat[up], we[up]
            v = np.linspace(0, 1, 40)[None, :]
            hh = -.45 + v * (Hu[:, None] + .45)
            ur = np.maximum.accumulate(self.face_u(np.repeat(Su[:, None], hh.shape[1], 1), hh, np.repeat(Hu[:, None], hh.shape[1], 1)), axis=1)
            top_u = ur[:, -1]
            first = np.empty(len(Uu))
            for k in range(len(Uu)):
                first[k] = np.interp(Uu[k], ur[k], hh[k, :], left=-.45, right=np.nan)
            inside = Uu <= top_u
            capd = Uu - top_u
            skin_c, Tb, buried, lip = self.cap_skin(Su, np.maximum(capd, 0), top_u, Hu, yu, nu)
            exposed = (capd >= 0) & (capd <= self.Lcap) & (skin_c > Tb + .05)  # only the short crest lip
            Tu = np.where(inside, np.minimum(nu, yu - 1.2), Tb)
            cu = np.where(inside, yu + np.nan_to_num(first, nan=0.) - .2, np.where(exposed, skin_c - .15, np.inf))
            T[up] = nu + (Tu - nu) * weu
            ceil[up] = np.where(weu > .9, cu, np.inf)
        # valley apron (skin tucked under the trail edge)
        dn = U < -(w / 2 - .12)
        if dn.any():
            Sd, yd, nd, wed = S[dn], y[dn], nat[dn], we[dn]
            E = -U[dn] - (w[dn] / 2 - .12)
            design, skin, Tv, Eb = self.valley(Sd, E, yd, nd)
            keep_trail = np.abs(U[dn]) < w[dn] / 2 + .45
            T[dn] = np.where(keep_trail, np.minimum(T[dn], nd + (Tv - nd) * wed), nd + (Tv - nd) * wed)
            cd = np.where((E <= Eb) & (wed > .9), skin - .25, np.inf)
            ceil[dn] = np.minimum(ceil[dn], cd)
        if self.bridge:  # gully under the walkway, running downhill across the path
            c = .5 * (self.bridge[0] + self.bridge[1])
            g = 3.2 * np.exp(-((S - c) / self.gully) ** 2) * smooth(-30, -2, U) * (1 - smooth(4, 10, U))
            T = T - g * we
        T = np.minimum(T, ceil - .05)  # never above an exposed skin
        return dict(x0=x0, z0=z0, X=X, Z=Z, T=T, nat=nat, ceil=ceil, S=S, U=U, we=we)

    # natural stones: set steps on every riser, embedded paving on shelves and ramps
    def stones(self, variants):
        kind = self.style.get('stones', 'natural')
        if kind != 'natural':
            return self.realm_stones(kind, STONE_SETS[kind])
        rng = self.rng; out = []
        for t in self.terraces:
            nxt = next((q for q in self.terraces if abs(q['a'] - t['b']) < 1e-3), None)
            if nxt is None:
                continue
            s = t['b']; w = float(np.interp(s, self.s, self.w))
            depth = (nxt['b'] - nxt['a']) * rng.uniform(.8, 1.08) + .18
            n = 1 if w < 1.45 else int(rng.choice([1, 2, 2, 3]))
            parts = rng.dirichlet(np.full(n, 5.)) * (w + .16)
            left = -(w + .16) / 2
            for k in range(n):
                gapk = rng.uniform(.02, .10)
                wk = parts[k] - gapk; c = left + parts[k] / 2; left += parts[k]
                # follow the skewed/bent riser line of this step edge (trail rows use the same shift)
                tt = float(np.clip(c / (w / 2), -1, 1)); front = s + t['skew'] * tt + t['bend'] * math.sin(tt * 4)
                q = self.at(front + depth / 2, c, nxt['ya'] - rng.uniform(0, .012))
                flip = rng.random() < .5  # show a different side of the stone to the walker
                pitch = math.degrees(math.atan2(nxt['yb'] - nxt['ya'] - nxt['jump'], nxt['b'] - nxt['a']))
                out.append(dict(kind='step', v=int(rng.integers(variants)), p=q.tolist(), pitch=float(pitch if flip else -pitch),
                                yaw=float(self.heading(front) + rng.uniform(-9, 9) + (180 if flip else 0)),
                                size=[float(wk + .06), float(rng.uniform(.34, .52)), float(depth)]))
        # embedded paving outside flights and bridge
        count = int(self.length * 1.1)
        for _ in range(count):
            s = rng.uniform(1, self.length - 1)
            if self.terrace_at(s) or (self.bridge and self.bridge[0] - .5 < s < self.bridge[1] + .5):
                continue
            w = float(np.interp(s, self.s, self.w)); u = rng.uniform(-.46, .46) * w
            size = rng.uniform(.28, .75)
            q = self.at(s, u, float(self.surface(s)) + .012 - .02 * rng.random())
            out.append(dict(kind='pave', v=int(rng.integers(variants)), p=q.tolist(), yaw=float(rng.uniform(0, 360)),
                            size=[float(size), float(rng.uniform(.10, .18)), float(size * rng.uniform(.6, 1.1))]))
        return out

    def realm_stones(self, kind, variants):
        """broken: angular set stones + scree chips · slab: one or two big flat slabs per step · cut: an old stone road —
        long rectangular blocks on the risers, running-bond flagstones between them, a share of lighter repair stones."""
        rng = self.rng; out = []
        for t in self.terraces:
            nxt = next((q for q in self.terraces if abs(q['a'] - t['b']) < 1e-3), None)
            if nxt is None:
                continue
            s = t['b']; w = float(np.interp(s, self.s, self.w)); tread = nxt['b'] - nxt['a']
            if kind == 'cut':
                n = 1 if w < 1.6 else int(rng.choice([1, 2, 2])); parts = rng.dirichlet(np.full(n, 20.)) * (w + .10)
                depth = tread + .05; gaps = (.01, .025); height = (.26, .32); yawj = 1.5
            elif kind == 'slab':
                n = 1 if w < 1.7 else int(rng.choice([1, 2])); parts = rng.dirichlet(np.full(n, 8.)) * (w + .18)
                depth = tread * .95 + .10; gaps = (.02, .06); height = (.20, .30); yawj = 6.
            else:
                n = 1 if w < 1.45 else int(rng.choice([1, 2, 3, 3])); parts = rng.dirichlet(np.full(n, 4.)) * (w + .16)
                depth = tread * rng.uniform(.75, 1.05) + .16; gaps = (.04, .14); height = (.34, .56); yawj = 15.
            left = -parts.sum() / 2
            for k in range(n):
                gapk = rng.uniform(*gaps); wk = parts[k] - gapk; c = left + parts[k] / 2; left += parts[k]
                tt = float(np.clip(c / (w / 2), -1, 1)); front = s + t['skew'] * tt + t['bend'] * math.sin(tt * 4)
                q = self.at(front + depth / 2, c, nxt['ya'] - rng.uniform(0, .012))
                flip = kind != 'cut' and rng.random() < .5
                pitch = math.degrees(math.atan2(nxt['yb'] - nxt['ya'] - nxt['jump'], tread))
                out.append(dict(kind='step', v=int(rng.integers(variants)), p=q.tolist(), pitch=float(pitch if flip else -pitch),
                                yaw=float(self.heading(front) + rng.uniform(-yawj, yawj) + (180 if flip else 0)),
                                size=[float(wk + .04), float(rng.uniform(*height)), float(depth)], tone=int(kind == 'cut' and rng.random() < .18)))
        skip = lambda s: self.terrace_at(s) or (self.bridge and self.bridge[0] - .5 < s < self.bridge[1] + .5)
        if kind == 'cut':  # running-bond flagstone rows across the path
            s, row = .6, 0
            while s < self.length - .6:
                ds = rng.uniform(.42, .62)
                if not skip(s + ds / 2):
                    w = float(np.interp(s, self.s, self.w)); u = -w / 2 + (.18 if row % 2 else 0) * rng.uniform(.6, 1.4)
                    while u < w / 2 - .12:
                        du = min(rng.uniform(.38, .72), w / 2 - u)
                        q = self.at(s + ds / 2, u + du / 2, float(self.surface(s + ds / 2)) + .008 - .012 * rng.random())
                        out.append(dict(kind='pave', v=int(rng.integers(variants)), p=q.tolist(), yaw=float(self.heading(s) + rng.uniform(-2.5, 2.5)),
                                        size=[float(du - .025), float(rng.uniform(.10, .14)), float(ds - .025)], tone=int(rng.random() < .18)))
                        u += du
                s += ds; row += 1
        else:
            count = int(self.length * (.6 if kind == 'slab' else 1.4))
            for _ in range(count):
                s = rng.uniform(1, self.length - 1)
                if skip(s):
                    continue
                w = float(np.interp(s, self.s, self.w))
                if kind == 'slab':
                    u = rng.uniform(-.38, .38) * w; size = rng.uniform(.6, 1.3); h = rng.uniform(.10, .16)
                else:  # scree: small chips, more of them toward the edges
                    u = float(np.clip(rng.normal(0, .36) * w, -.5 * w, .5 * w)); size = rng.uniform(.15, .45); h = rng.uniform(.08, .16)
                q = self.at(s, u, float(self.surface(s)) + .010 - .02 * rng.random())
                out.append(dict(kind='pave', v=int(rng.integers(variants)), p=q.tolist(), yaw=float(rng.uniform(0, 360)),
                                size=[float(size), float(h), float(size * rng.uniform(.6, 1.1))], tone=0))
        return out

    def heading(self, s):
        t = np.array([np.interp(s, self.s, self.tan[:, 0]), np.interp(s, self.s, self.tan[:, 1])])
        return math.degrees(math.atan2(t[0], t[1]))

    def profile_json(self):
        pts = self.at(self.s, 0., self.y)
        return dict(points=[dict(x=float(a), y=float(b), z=float(c)) for a, b, c in pts], widths=self.w.tolist(),
                    distances=self.s.tolist(), types=self.types.tolist(), knots=[], length=self.length, rise=float(self.R),
                    bridgeStart=float(self.bridge[0]) if self.bridge else 0., bridgeEnd=float(self.bridge[1]) if self.bridge else 0.,
                    upSign=int(self.up_sign))


# ---------------------------------------------------------------- 4m grid fitting
def fit_coarse(h, patch, sec=None, margin=.06, iters=80):
    """Write a section's designed 1m terrain into the 4m game grid.

    Corners start at the design (exactly the base where the design leaves the ground alone); then
    only the corners of cells whose rendered surface (bilinear or either triangle split) rises above
    an exposed skin are lowered, by the smallest amount that clears it. Returns the grid window,
    the fitted heights, rock/walk coverage for the surface mask and the residual violation count.
    """
    T, ceil, x0, z0 = patch['T'], patch['ceil'], patch['x0'], patch['z0']
    step = int(round(CELL / FINE))
    ix0, iz0 = int(round(x0 / CELL)), int(round(z0 / CELL))
    nz, nx = (T.shape[0] - 1) // step + 1, (T.shape[1] - 1) // step + 1
    Tc = T[::step, ::step][:nz, :nx]; natc = patch['nat'][::step, ::step][:nz, :nx]
    base = h[iz0:iz0 + nz, ix0:ix0 + nx].copy()
    coarse = base + (Tc - natc)
    zz, xx = np.nonzero(np.isfinite(ceil))
    limit = ceil[zz, xx] - margin
    cx, cz = xx / step, zz / step
    i = np.minimum(cx.astype(int), nx - 2); j = np.minimum(cz.astype(int), nz - 2)
    u, v = cx - i, cz - j
    bad = np.zeros(len(zz), bool)
    for _ in range(iters):
        a, b, c, d = coarse[j, i], coarse[j, i + 1], coarse[j + 1, i], coarse[j + 1, i + 1]
        bil = (1 - v) * ((1 - u) * a + u * b) + v * ((1 - u) * c + u * d)
        t1 = np.where(u >= v, a + u * (b - a) + v * (d - b), a + v * (c - a) + u * (d - c))                 # split a–d
        t2 = np.where(u + v <= 1, a + u * (b - a) + v * (c - a), d + (1 - u) * (c - d) + (1 - v) * (b - d))  # split b–c
        excess = np.maximum(bil, np.maximum(t1, t2)) - limit
        bad = excess > 1e-3
        if not bad.any():
            break
        low = np.zeros_like(coarse)
        for dj, di in ((0, 0), (0, 1), (1, 0), (1, 1)):
            np.maximum.at(low, (j[bad] + dj, i[bad] + di), excess[bad])
        coarse = coarse - low
    rock = np.zeros((nz, nx)); walk = np.zeros((nz, nx))
    if sec is not None:
        S, U = patch['S'], patch['U']; w = np.interp(S, sec.s, sec.w)
        # terrain shows rock only where the edit left it steep; mesh-covered ground is rock already
        dz_, dx_ = np.gradient(T, FINE); steep = np.degrees(np.arctan(np.hypot(dx_, dz_))) > 40
        rockf = (steep & (np.abs(T - patch['nat']) > .6) & (np.abs(U) > w / 2 + .6)).astype(float)
        walkf = ((np.abs(U) < w / 2 + .6) & (patch['we'] > .02)).astype(float)
        for arr, src in ((rock, rockf), (walk, walkf)):
            # coverage fraction of each 4m cell (no dilation: bilinear filtering already spreads it)
            pad = np.pad(src, ((0, step), (0, step)), mode='edge')
            acc = np.zeros((nz, nx))
            for dj in range(step):
                for di in range(step):
                    acc += pad[dj:dj + nz * step:step, di:di + nx * step:step][:nz, :nx]
            arr[:] = np.clip(acc / (step * step) * 1.6, 0, 1)
    return dict(ix0=ix0, iz0=iz0, coarse=coarse, base=base, rock=rock, walk=walk, residual=int(bad.sum()),
                constraints=int(len(zz)), lowered=int((coarse < base + (Tc - natc) - 1e-6).sum()))
