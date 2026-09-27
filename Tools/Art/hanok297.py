"""#297 hanok / fortress kit — pure numpy mesh generation in Unity space (x right, y up, z forward).

Front faces wind so that cross(b-a, c-a) points outward (Unity renders clockwise-from-viewer as front).
Every generator takes `lod` (0 near, 1 mid, 2 far) and returns MB builders; `export()` writes one JSON per mesh:
  {"name", "v":[x,y,z..], "n":[..], "uv":[..], "sub":[{"m": material slot, "t":[indices]}]}
Material slots are names; the Unity importer maps them to candidate materials (Finish297/Materials).

Korean roof (지붕) model, shared by 팔작(hip-and-gable) / 맞배(gable) / 우진각(hip):
  * eave line (처마선) in plan bows outward toward the corners (안허리곡) and rises toward them (앙곡);
  * each slope is a Coons patch between the eave curve and its top edge (ridge / gable base / hip apex),
    with a concave sag across the slope (지붕곡) so it is steep near the ridge and gentle at the eave;
  * hips (추녀마루) are shared boundary curves, so adjacent slopes are watertight;
  * 팔작: the end slopes stop at the gable base (합각 아래) and a vertical gable wall rises to the ridge.
"""
from __future__ import annotations
import json, math
from collections import defaultdict
from pathlib import Path
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
TILE_W, TILE_H = 1.20, 1.68            # metres per repeat of T_RoofTile297 (see hanok297_textures.py)
UP = np.array([0., 1., 0.])


def nz(v):
    v = np.asarray(v, float); n = np.linalg.norm(v, axis=-1, keepdims=True)
    return v / np.maximum(n, 1e-12)


class MB:
    def __init__(self, name):
        self.name = name; self.V = []; self.N = []; self.UV = []; self.T = defaultdict(list)

    # -- primitives -------------------------------------------------------------------------------------------
    def _add(self, P, Nn, UV):
        b = len(self.V); self.V.extend(np.asarray(P, float).tolist()); self.N.extend(np.asarray(Nn, float).tolist())
        self.UV.extend(np.asarray(UV, float).tolist()); return b

    def poly(self, P, mat, out, uv=None, uvscale=1.0):
        """Planar convex polygon (fan). `out` = outward direction used to fix the winding."""
        P = np.asarray(P, float); n = np.zeros(3)
        for i in range(1, len(P) - 1): n += np.cross(P[i] - P[0], P[i + 1] - P[0])
        if np.dot(n, out) < 0: P = P[::-1]; uv = None if uv is None else np.asarray(uv)[::-1]; n = -n
        n = nz(n)
        if uv is None: uv = planar_uv(P, n, uvscale)
        b = self._add(P, np.repeat(n[None], len(P), 0), uv)
        for i in range(1, len(P) - 1): self.T[mat] += [b, b + i, b + i + 1]

    def quad(self, a, b, c, d, mat, out, uv=None, uvscale=1.0):
        self.poly([a, b, c, d], mat, out, uv, uvscale)

    def grid(self, P, mat, uv, out, smooth=True):
        """P: (nu, nv, 3) patch. Normals from the surface tangents, oriented by `out` (vector or callable(p)->vector)."""
        P = np.asarray(P, float); nu, nv = P.shape[:2]
        du = np.gradient(P, axis=0); dv = np.gradient(P, axis=1); n = nz(np.cross(du, dv))
        o = out(P) if callable(out) else np.broadcast_to(np.asarray(out, float), P.shape)
        flip = np.sum(n * o) < 0
        if flip: n = -n
        b = self._add(P.reshape(-1, 3), n.reshape(-1, 3), np.asarray(uv, float).reshape(-1, 2))
        idx = lambda i, j: b + i * nv + j
        for i in range(nu - 1):
            for j in range(nv - 1):
                a, bb, c, d = idx(i, j), idx(i + 1, j), idx(i + 1, j + 1), idx(i, j + 1)
                # winding: tangent frame (du, dv) gives normal cross(du, dv); flip keeps it outward
                if not flip: self.T[mat] += [a, d, c, a, c, bb]
                else: self.T[mat] += [a, c, d, a, bb, c]
        self._fix_winding(mat, b)

    def _fix_winding(self, mat, start):
        """Make every triangle added since `start` agree with its vertex normals (robust against patch orientation)."""
        V = np.asarray(self.V); N = np.asarray(self.N); t = self.T[mat]
        for k in range(len(t) - 3, -1, -3):
            i, j, l = t[k], t[k + 1], t[k + 2]
            if i < start: break
            fn = np.cross(V[j] - V[i], V[l] - V[i])
            if np.dot(fn, N[i] + N[j] + N[l]) < 0: t[k + 1], t[k + 2] = l, j

    def box(self, c, size, mat, yaw=0.0, faces='xyzXYZ', uvscale=1.0, mats=None):
        """Axis box rotated about Y. faces: x/-x=X?, lower=neg, upper=pos: 'x' -x, 'X' +x, 'y' bottom, 'Y' top, 'z' -z, 'Z' +z."""
        c = np.asarray(c, float); hx, hy, hz = np.asarray(size, float) / 2
        cy, sy = math.cos(math.radians(yaw)), math.sin(math.radians(yaw))
        R = lambda p: np.array([c[0] + p[0] * cy + p[2] * sy, c[1] + p[1], c[2] - p[0] * sy + p[2] * cy])
        corners = {(i, j, k): R((i * hx, j * hy, k * hz)) for i in (-1, 1) for j in (-1, 1) for k in (-1, 1)}
        spec = {'x': ((-1, 0, 0), [(-1, -1, -1), (-1, 1, -1), (-1, 1, 1), (-1, -1, 1)]),
                'X': ((1, 0, 0), [(1, -1, -1), (1, -1, 1), (1, 1, 1), (1, 1, -1)]),
                'y': ((0, -1, 0), [(-1, -1, -1), (-1, -1, 1), (1, -1, 1), (1, -1, -1)]),
                'Y': ((0, 1, 0), [(-1, 1, -1), (1, 1, -1), (1, 1, 1), (-1, 1, 1)]),
                'z': ((0, 0, -1), [(-1, -1, -1), (1, -1, -1), (1, 1, -1), (-1, 1, -1)]),
                'Z': ((0, 0, 1), [(-1, -1, 1), (-1, 1, 1), (1, 1, 1), (1, -1, 1)])}
        for f in faces:
            nrm, cs = spec[f]; o = R(np.asarray(nrm, float)) - R(np.zeros(3))
            self.poly([corners[k] for k in cs], (mats or {}).get(f, mat), o, uvscale=uvscale)

    def cyl(self, p0, p1, r0, r1, sides, mat, uvscale=1.0, caps=False):
        p0 = np.asarray(p0, float); p1 = np.asarray(p1, float); ax = nz(p1 - p0)
        ref = np.array([1., 0, 0]) if abs(ax[0]) < .9 else np.array([0, 0, 1.])
        e1 = nz(np.cross(ax, ref)); e2 = np.cross(ax, e1); L = np.linalg.norm(p1 - p0)
        ang = np.linspace(0, 2 * math.pi, sides + 1)
        ring = lambda p, r: np.array([p + r * (math.cos(a) * e1 + math.sin(a) * e2) for a in ang])
        A = ring(p0, r0); B = ring(p1, r1)
        P = np.stack([A, B], 1)                                         # (sides+1, 2, 3)
        uv = np.stack(np.meshgrid(ang / (2 * math.pi) * 2 * math.pi * max(r0, r1) / uvscale, [0, L / uvscale], indexing='ij'), -1)
        self.grid(P, mat, uv, lambda Q: Q - (p0 + np.clip(np.einsum('...k,k->...', Q - p0, ax), 0, L)[..., None] * ax))
        if caps:
            self.poly(B[:-1], mat, ax); self.poly(A[:-1][::-1], mat, -ax)

    def merge(self, other: 'MB'):
        b = len(self.V); self.V += other.V; self.N += other.N; self.UV += other.UV
        for m, t in other.T.items(): self.T[m] += [i + b for i in t]
        return self

    def transformed(self, yaw=0.0, offset=(0, 0, 0)):
        out = MB(self.name); cy, sy = math.cos(math.radians(yaw)), math.sin(math.radians(yaw)); off = np.asarray(offset, float)
        V = np.asarray(self.V); N = np.asarray(self.N)
        rot = lambda A: np.stack([A[:, 0] * cy + A[:, 2] * sy, A[:, 1], -A[:, 0] * sy + A[:, 2] * cy], -1)
        out.V = (rot(V) + off).tolist(); out.N = rot(N).tolist(); out.UV = list(self.UV); out.T = defaultdict(list, {k: list(v) for k, v in self.T.items()})
        return out

    def tris(self):
        return sum(len(t) for t in self.T.values()) // 3

    def export(self, path):
        path = Path(path); path.parent.mkdir(parents=True, exist_ok=True)
        r = lambda a: [round(float(x), 5) for x in np.asarray(a).reshape(-1)]
        d = dict(name=self.name, v=r(self.V), n=r(self.N), uv=r(self.UV), sub=[dict(m=m, t=[int(i) for i in t]) for m, t in self.T.items() if t])
        path.write_text(json.dumps(d, separators=(',', ':')), encoding='utf-8')
        return path


def planar_uv(P, n, scale):
    P = np.asarray(P, float)
    if abs(n[1]) > .7: return P[:, [0, 2]] / scale
    h = nz(np.cross(UP, n)); return np.stack([P @ h, P[:, 1]], -1) / scale


# ------------------------------------------------------------------------------------------------------------------
# Roof
# ------------------------------------------------------------------------------------------------------------------
def coons(bottom, top, left, right):
    """bottom/top: (nu,3) curves (u direction), left/right: (nv,3) (v from bottom to top). Returns (nu,nv,3)."""
    nu, nv = len(bottom), len(left); u = np.linspace(0, 1, nu)[:, None, None]; v = np.linspace(0, 1, nv)[None, :, None]
    Lc = (1 - v) * bottom[:, None] + v * top[:, None]
    Ld = (1 - u) * left[None] + u * right[None]
    B = ((1 - u) * (1 - v) * bottom[0] + u * (1 - v) * bottom[-1] + (1 - u) * v * top[0] + u * v * top[-1])
    return Lc + Ld - B


def arclen_uv(P, du_scale, dv_scale):
    """Tile UV for a patch: u = distance along rows / du_scale, v = distance up the slope / dv_scale."""
    su = np.concatenate([np.zeros((1, P.shape[1])), np.cumsum(np.linalg.norm(np.diff(P, axis=0), axis=-1), 0)], 0)
    sv = np.concatenate([np.zeros((P.shape[0], 1)), np.cumsum(np.linalg.norm(np.diff(P, axis=1), axis=-1), 1)], 1)
    su = su - su[len(su) // 2]                                       # centre the tile columns on the slope's middle
    return np.stack([su / du_scale, sv / dv_scale], -1)


class RoofSpec:
    def __init__(self, kind='palzak', L=12.0, D=7.0, base=4.2, eave=1.6, rise=None, corner_lift=.45, corner_bow=.55,
                 sag=.10, gable_inset=None, ridge_h=.55, ridge_w=.42, thickness=.32, lifts=1.7, bows=1.6, palace=False):
        self.kind, self.L, self.D, self.base, self.eave = kind, L, D, base, eave
        self.rise = rise if rise is not None else D * .50                # ridge above the eave-centre height
        self.corner_lift, self.corner_bow, self.sag = corner_lift, corner_bow, sag
        self.gable_inset = gable_inset if gable_inset is not None else min(L * .22, D * .42)
        self.ridge_h, self.ridge_w, self.thickness = ridge_h, ridge_w, thickness
        self.lifts, self.bows, self.palace = lifts, bows, palace


def eave_point(r: RoofSpec, side, t):
    """Point on the eave line. side in {'front','back','left','right'}; t in [-1,1] along the side (left->right as seen
    from outside is not needed: callers pick consistent directions). Front = +z."""
    Xe, Ze = r.L / 2 + r.eave, r.D / 2 + r.eave
    a = abs(t); lift = r.corner_lift * a ** r.lifts; bow = r.corner_bow * a ** r.bows
    if side in ('front', 'back'):
        s = 1 if side == 'front' else -1
        return np.array([t * (Xe + bow), r.base + lift, s * (Ze + bow)])     # corners (|t|=1) = (Xe+b, Ze+b) from every side
    s = 1 if side == 'right' else -1
    return np.array([s * (Xe + bow), r.base + lift, t * (Ze + bow)])


def build_roof(r: RoofSpec, lod=0, mb=None, hole=None):
    """Tile surfaces + ridges + eave fascia/soffit. hole=(hx,hz,top_y) makes a skirt roof around an upper storey."""
    mb = mb or MB('roof')
    nu = [24, 12, 6][lod]; nv = [10, 6, 3][lod]
    Xe, Ze = r.L / 2 + r.eave, r.D / 2 + r.eave
    ridge_y = r.base + r.rise
    top_x = r.L / 2 - r.gable_inset if r.kind == 'palzak' else (r.L / 2 - r.D / 2 * .92 if r.kind == 'ujingak' else Xe + .25)
    top_x = max(top_x, .6)
    gable_y = r.base + r.rise * .58                                   # 합각 base height on the hip line (팔작)
    zg = Ze * (1 - .58) if r.kind == 'palzak' else 0.0                 # plan depth where the hip meets the gable base

    def sagged(P, amount, keep_sides=True):
        """Push interior rows down (concave slope) — zero at the eave and the top edge. The side columns are the shared
        hip/barge curves (already sagged in hip_line), so they are kept exactly: adjacent slopes stay watertight."""
        nu_, nv_ = P.shape[:2]; v = np.linspace(0, 1, nv_)[None, :]
        span = np.linalg.norm(P[:, -1] - P[:, 0], axis=-1)[:, None]
        w = 1 - np.abs(np.linspace(-1, 1, nu_))[:, None] ** 6 if keep_sides else 1.0
        P = P.copy(); P[..., 1] -= amount * span * np.sin(math.pi * v) * (1 - v * .35) * w
        return P

    t = np.linspace(-1, 1, nu)
    if hole is None:
        # --- long slopes (front/back) ---
        for s, side in ((1, 'front'), (-1, 'back')):
            bottom = np.array([eave_point(r, side, ti) for ti in t])
            top = np.stack([np.linspace(-top_x, top_x, nu), np.full(nu, ridge_y), np.zeros(nu)], -1)
            if r.kind == 'matbae':
                bottom[:, 0] = np.linspace(-Xe - .25, Xe + .25, nu)
                left = np.stack([np.full(nv, -Xe - .25), np.linspace(bottom[0, 1], ridge_y, nv), np.linspace(bottom[0, 2], 0, nv)], -1)
                k = np.linspace(0, 1, nv); span = np.linalg.norm(left[-1] - left[0])
                left[:, 1] -= r.sag * span * np.sin(math.pi * k) * (1 - k * .35)
                right = left.copy(); right[:, 0] = Xe + .25
            else:
                left = hip_line(r, -1, s, nv, top_x, ridge_y, gable_y, zg)
                right = hip_line(r, 1, s, nv, top_x, ridge_y, gable_y, zg)
            P = sagged(coons(bottom, top, left, right), r.sag)   # coons() takes nv from the side curves
            mb.grid(P, 'tile', arclen_uv(P, TILE_W, TILE_H), out=np.array([0, 1, s * .8]))
        # --- end slopes (left/right) ---
        if r.kind in ('palzak', 'ujingak'):
            for s, side in ((1, 'right'), (-1, 'left')):
                bottom = np.array([eave_point(r, side, ti) for ti in t])
                if r.kind == 'palzak':
                    top_pts = np.stack([np.full(nu, s * top_x), np.full(nu, gable_y), np.linspace(-zg, zg, nu)], -1)
                    left = hip_line(r, s, -1, nv, top_x, ridge_y, gable_y, zg, stop_at_gable=True)
                    right = hip_line(r, s, 1, nv, top_x, ridge_y, gable_y, zg, stop_at_gable=True)
                else:
                    top_pts = np.repeat(np.array([[s * top_x, ridge_y, 0.]]), nu, 0)
                    left = hip_line(r, s, -1, nv, top_x, ridge_y, gable_y, zg)
                    right = hip_line(r, s, 1, nv, top_x, ridge_y, gable_y, zg)
                P = sagged(coons(bottom, top_pts, left, right), r.sag * .8)
                mb.grid(P, 'tile', arclen_uv(P, TILE_W, TILE_H), out=np.array([s * .8, 1, 0]))
        if r.kind == 'palzak':
            gable_walls(r, mb, top_x, ridge_y, gable_y, zg)
        if r.kind == 'matbae':
            for s in (-1, 1):   # 박공: vertical gable triangle under the barge boards
                x = s * (r.L / 2 + .15)
                mb.poly([[x, r.base - .2, -r.D / 2 - .2], [x, r.base - .2, r.D / 2 + .2], [x, ridge_y - .15, 0]], 'wood_board', [s, 0, 0], uvscale=1.2)
        ridges(r, mb, lod, top_x, ridge_y, gable_y, zg)
    else:
        skirt_roof(r, mb, lod, hole, nu, nv)
    eave_edge(r, mb, lod, nu)
    return mb


def hip_line(r, sx, sz, nv, top_x, ridge_y, gable_y, zg, stop_at_gable=False):
    """Hip boundary from the eave corner (sx, sz) upward, bottom->top.
    팔작: the diagonal to the gable base is ONE shared curve (nv points, own sag) for both the long slope and the end
    slope, so the hip is watertight; the long slope appends the barge line (gable base -> ridge end) above it.
    우진각: the diagonal runs to the ridge end (apex)."""
    corner = eave_point(r, 'front' if sz > 0 else 'back', sx)
    end = np.array([sx * top_x, gable_y, sz * zg]) if r.kind == 'palzak' else np.array([sx * top_x, ridge_y, 0.])
    k = np.linspace(0, 1, nv)[:, None]; P = corner * (1 - k) + end * k
    kk = k[:, 0]; span = np.linalg.norm(end - corner)
    P[:, 1] -= r.sag * .7 * span * np.sin(math.pi * kk) * (1 - kk * .35)
    if r.kind == 'palzak' and not stop_at_gable:
        nb = max(3, nv // 2); kb = np.linspace(0, 1, nb)[1:, None]
        ridge_end = np.array([sx * top_x, ridge_y, 0.])
        B = end * (1 - kb) + ridge_end * kb
        P = np.concatenate([P, B], 0)
    return P


def gable_walls(r, mb, top_x, ridge_y, gable_y, zg):
    """합각: vertical board wall between the gable base and the ridge, trimmed by barge boards (박공)."""
    for s in (-1, 1):
        x = s * (top_x - .05)
        pts = [[x, gable_y - .05, -zg], [x, gable_y - .05, zg], [x, ridge_y - .1, .15], [x, ridge_y - .1, -.15]]
        mb.poly(pts, 'gable', [s, 0, 0], uvscale=1.0)


def ridges(r, mb, lod, top_x, ridge_y, gable_y, zg):
    """용마루 (white 양성 sides, tile cap), 내림마루 along the barge lines, 추녀마루 on the hips with upturned tips."""
    h, w = r.ridge_h, r.ridge_w
    mb.box([0, ridge_y + h / 2 - .06, 0], [2 * top_x + .5, h, w], 'ridge', faces='xXzZy', uvscale=1.0)
    mb.box([0, ridge_y + h + .02, 0], [2 * top_x + .56, .1, w + .12], 'tile_dark', faces='xXyYzZ', uvscale=1.0)
    if lod < 2:   # 망와/취두: the ridge end swells and curls up a little (not a separate post)
        for s in (-1, 1):
            a = np.array([s * (top_x + .05), ridge_y + h * .55, 0.]); b = np.array([s * (top_x + .42), ridge_y + h * 1.05, 0.])
            beam(mb, a, b, w + .08, h * .9, 'tile_dark')
    if r.kind == 'palzak' and lod < 2:
        for sx in (-1, 1):
            for sz in (-1, 1):
                a = np.array([sx * top_x, ridge_y + .05, 0.]); b = np.array([sx * top_x, gable_y + .1, sz * zg])
                beam(mb, a, b, .22, .18, 'ridge'); beam(mb, a + [0, .1, 0], b + [0, .1, 0], .26, .05, 'tile_dark')
    if r.kind in ('palzak', 'ujingak') and lod < 2:
        for sx in (-1, 1):
            for sz in (-1, 1):
                pts = hip_line(r, sx, sz, 8, top_x, ridge_y, gable_y, zg, stop_at_gable=(r.kind == 'palzak'))[::-1]
                for a, b in zip(pts[:-1], pts[1:]):
                    beam(mb, a + [0, .10, 0], b + [0, .10, 0], .20, .16, 'ridge')
                    beam(mb, a + [0, .19, 0], b + [0, .19, 0], .24, .05, 'tile_dark')
                tip = pts[-1] + np.array([sx * .22, .30, sz * .22])
                beam(mb, pts[-1] + [0, .12, 0], tip, .20, .16, 'tile_dark')
                if r.palace and lod == 0:   # 잡상 (a few figures on palace hips)
                    for k in range(3):
                        q = pts[-2 - k] * .5 + pts[-3 - k] * .5
                        mb.box(q + [0, .42, 0], [.14, .26, .14], 'tile_dark', faces='xXzZY')
    if r.kind == 'matbae' and lod < 2:
        for sx in (-1, 1):
            x = sx * (r.L / 2 + r.eave + .25)
            for sz in (-1, 1):
                beam(mb, np.array([x, ridge_y + .05, 0.]), np.array([x, r.base + r.corner_lift * .3, sz * (r.D / 2 + r.eave)]), .26, .22, 'ridge')


def beam(mb, a, b, w, h, mat):
    """Square beam from a to b (w across, h up)."""
    a = np.asarray(a, float); b = np.asarray(b, float); d = nz(b - a)
    side = nz(np.cross(UP if abs(d[1]) < .95 else np.array([1., 0, 0]), d)); up = np.cross(d, side)
    c = [(-1, -1), (1, -1), (1, 1), (-1, 1)]
    A = [a + side * sx * w / 2 + up * sy * h / 2 for sx, sy in c]; B = [b + side * sx * w / 2 + up * sy * h / 2 for sx, sy in c]
    for i in range(4):
        j = (i + 1) % 4; o = side * (c[i][0] + c[j][0]) + up * (c[i][1] + c[j][1])
        mb.quad(A[i], A[j], B[j], B[i], mat, o, uvscale=1.0)
    mb.poly(B, mat, d); mb.poly(A, mat, -d)


def eave_edge(r, mb, lod, nu):
    """Fascia (평고대/막새 line) along the whole eave and the rafter soffit back to the wall line."""
    loop = []
    for side, rng in (('front', np.linspace(-1, 1, nu)), ('right', np.linspace(1, -1, nu)), ('back', np.linspace(1, -1, nu)), ('left', np.linspace(-1, 1, nu))):
        pts = [eave_point(r, side, t) for t in rng]
        loop += pts[:-1]
    if r.kind == 'matbae':   # gable roofs: fascia only along the long sides
        loop = None
    th = r.thickness
    sides = [('front', 1), ('back', -1)] + ([] if r.kind == 'matbae' else [('right', 1), ('left', -1)])
    for side, s in sides:
        t = np.linspace(-1, 1, nu); E = np.array([eave_point(r, side, ti) for ti in t])
        if r.kind == 'matbae' and side in ('front', 'back'): E[:, 0] = np.linspace(-(r.L / 2 + r.eave + .25), r.L / 2 + r.eave + .25, nu)
        low = E - [0, th, 0]
        out = np.array([0, 0, s]) if side in ('front', 'back') else np.array([s, 0, 0])
        P = np.stack([low, E], 1); uv = np.stack(np.meshgrid(np.linspace(0, np.linalg.norm(E[-1] - E[0]) / .3, nu), [0, 1], indexing='ij'), -1)
        mb.grid(P, 'fascia', uv, out)
        if lod < 2:   # soffit: from the fascia bottom back to the wall line under the eave
            inner = low.copy()
            if side in ('front', 'back'): inner[:, 2] = s * (r.D / 2 - .1); inner[:, 0] = np.clip(inner[:, 0], -r.L / 2, r.L / 2)
            else: inner[:, 0] = s * (r.L / 2 - .1); inner[:, 2] = np.clip(inner[:, 2], -r.D / 2, r.D / 2)
            inner[:, 1] = r.base - th - .25
            P = np.stack([inner, low], 1)
            uvs = np.stack(np.meshgrid(np.linspace(0, np.linalg.norm(E[-1] - E[0]) / 2.4, nu), [0, 1], indexing='ij'), -1)
            mb.grid(P, 'soffit', uvs, np.array([0, -1, 0]))


def skirt_roof(r, mb, lod, hole, nu, nv):
    """중층 아래 지붕(차양): four slopes from the eave up to the upper storey's wall line (hx, hz) at top_y."""
    hx, hz, top_y = hole
    t = np.linspace(-1, 1, nu)
    for side, s in (('front', 1), ('back', -1), ('right', 1), ('left', -1)):
        bottom = np.array([eave_point(r, side, ti) for ti in t])
        if side in ('front', 'back'):
            top = np.stack([np.linspace(-hx, hx, nu), np.full(nu, top_y), np.full(nu, s * hz)], -1)
        else:
            top = np.stack([np.full(nu, s * hx), np.full(nu, top_y), np.linspace(-hz, hz, nu)], -1)
        left = np.stack([np.linspace(bottom[0, 0], top[0, 0], nv), np.linspace(bottom[0, 1], top_y, nv), np.linspace(bottom[0, 2], top[0, 2], nv)], -1)
        right = np.stack([np.linspace(bottom[-1, 0], top[-1, 0], nv), np.linspace(bottom[-1, 1], top_y, nv), np.linspace(bottom[-1, 2], top[-1, 2], nv)], -1)
        P = coons(bottom, top, left, right)
        v = np.linspace(0, 1, nv)[None, :]; span = np.linalg.norm(P[:, -1] - P[:, 0], axis=-1)[:, None]
        P[..., 1] -= r.sag * .8 * span * np.sin(math.pi * v) * (1 - v * .35)
        o = np.array([0, 1, s * .8]) if side in ('front', 'back') else np.array([s * .8, 1, 0])
        mb.grid(P, 'tile', arclen_uv(P, TILE_W, TILE_H), out=o)
        if lod < 2:
            for a, b in ((left[0], left[-1]), (right[0], right[-1])):
                beam(mb, a + [0, .1, 0], b + [0, .1, 0], .26, .2, 'ridge')
