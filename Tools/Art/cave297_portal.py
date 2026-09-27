"""#297 opening mine (폐광) portal — closes the V4 open trench under the #292/#295 hillside (SPEC-WORLD-FINISH-297 §2).

After the rigid relocation the V4 cave's last ~25 m ("open mouth" trench, originally roofed by the retired exterior shell)
sits 6-15 m under the new hillside; terrain seen from below is back-face culled, so the player saw sky. A 4 m height field
cannot hold a vertical cut, so the portal is made the way heightmap games make cave mouths:
  * the notch (surface297.py, portal.json) cuts the approach to the floor in front of the face and ends sharply behind it;
  * terrain cells whose surface would cross the gallery or the opening are listed as hole cells (removed from the private
    LOD0 tile mesh + collider in Unity);
  * Portal_Face is a rock hood grown outward and backward from the opening edge until every path is buried in intact
    terrain outside the hole, so it covers the hole and reads as a quarried rock face over the adit;
  * Portal_Gallery — rock gallery (갱도) walls + roof from just inside the V4 tube (clip plane) to the face, its section
    morphing from the measured V4 section to a timbered adit arch; Portal_Floor under it and out over the hole cells;
  * Portal_Timbers — 갱목 sets (posts + cap + lagging) and a sill; Portal_Debris — blast rubble and a collapsed old set.
Unity side: CompactFinish297.CavePortal.cs (clip V4 interior/floor, hole cells, placement, colliders, vegetation clear).

Inputs: staged height field (Finish297/Stage/Surface/height.bytes — install with `finish297 surface` first), the scene slice
of Natural_Cave_Interior at the clip plane (Finish297/profile-3341_1993.txt from `finish297 profile:`), notch in Cave/portal.json.
python Tools/Art/cave297_portal.py      -> Art/World/Compact/Rebuild/Finish297/Cave/Portal/{Meshes/*.json, unity.json, plan.png}
"""
import json, math, sys
from pathlib import Path
import numpy as np
sys.path.insert(0, str(Path(__file__).resolve().parent))
from hanok297 import MB, beam, nz

ROOT = Path(__file__).resolve().parents[2]
F297 = ROOT / 'Art/World/Compact/Rebuild/Finish297'
OUT = F297 / 'Cave/Portal'
HEIGHT = F297 / 'Stage/Surface/height.bytes'
PROFILE = F297 / 'profile-3341_1993.txt'
PROFILE_AT = np.array([3341.0, 1993.0])
W, HH, CELL = 1001, 1501, 4.0
FLOOR = 168.25
FACE_S = 2.4                       # portal face plane, metres past the notch head `a` (the cut starts 1.8 m behind the face)
UPV = np.array([0., 1., 0.])

H = np.fromfile(HEIGHT, '<f4').reshape(HH, W).astype(np.float64)


def terrain(x, z):
    """Bilinear, identical to CompactWorldSurface.Sample."""
    fx = np.clip(np.asarray(x, float) / CELL, 0, W - 1); fz = np.clip(np.asarray(z, float) / CELL, 0, HH - 1)
    ix = np.minimum(fx.astype(int), W - 2); iz = np.minimum(fz.astype(int), HH - 2); u = fx - ix; v = fz - iz
    a = H[iz, ix] * (1 - u) + H[iz, ix + 1] * u; b = H[iz + 1, ix] * (1 - u) + H[iz + 1, ix + 1] * u
    return a * (1 - v) + b * v


# -- smooth 3D value noise (integer hash in uint64, wraps silently) ----------------------------------------------
def _hash(i, j, k, seed):
    h = (np.asarray(i, np.int64).astype(np.uint64) * np.uint64(73856093)) ^ (np.asarray(j, np.int64).astype(np.uint64) * np.uint64(19349663)) \
        ^ (np.asarray(k, np.int64).astype(np.uint64) * np.uint64(83492791)) ^ np.uint64(seed * 2654435761 % (1 << 63))
    h = (h ^ (h >> np.uint64(13))) * np.uint64(1274126177)
    return ((h ^ (h >> np.uint64(16))) & np.uint64(0xFFFFFF)).astype(np.float64) / float(0xFFFFFF) * 2 - 1


def vnoise(P, scale, seed=1):
    P = np.asarray(P, float) / scale; i0 = np.floor(P).astype(np.int64); f = P - i0; s = f * f * (3 - 2 * f)
    out = 0
    for dx in (0, 1):
        for dy in (0, 1):
            for dz in (0, 1):
                w = (s[..., 0] if dx else 1 - s[..., 0]) * (s[..., 1] if dy else 1 - s[..., 1]) * (s[..., 2] if dz else 1 - s[..., 2])
                out = out + w * _hash(i0[..., 0] + dx, i0[..., 1] + dy, i0[..., 2] + dz, seed)
    return out


def fbm(P, scale, octaves=3, seed=1):
    a, t, n = 1.0, 0.0, 0.0
    for o in range(octaves):
        t = t + a * vnoise(P, scale / 2 ** o, seed + o); n += a; a *= .5
    return t / n


# -- frame: notch axis (plan), s along the outward direction from the notch head, l to the right ----------------
portal = json.loads((F297 / 'Cave/portal.json').read_text(encoding='utf-8'))
op = portal['terrainOps'][0]
A = np.array(op['a'], float); D = nz(np.array(op['b'], float) - A); R = np.array([D[1], -D[0]])
D3 = np.array([D[0], 0, D[1]]); R3 = np.array([R[0], 0, R[1]])


def axis(s, l=0.0):
    p = A + D * s + R * l; return np.array([p[0], FLOOR, p[1]])


def frame(p):
    q = np.asarray(p, float)[..., [0, 2]] - A; return q @ D, q @ R, np.asarray(p, float)[..., 1] - FLOOR


def rel_terrain(s, l=0.0):
    p = A + D * s + R * l; return float(terrain(p[0], p[1])) - FLOOR


s_clip = float((PROFILE_AT - A) @ D); l_shift = float((PROFILE_AT - A) @ R); s_face = FACE_S


# -- sections ---------------------------------------------------------------------------------------------------
def v4_section():
    seg = np.loadtxt(PROFILE).reshape(-1, 4)
    pts = np.concatenate([seg[:, :2], seg[:, 2:]]); pts[:, 0] += l_shift
    c = pts.mean(0); foot = FLOOR - .5
    upper = pts[pts[:, 1] >= foot]
    a = np.arctan2(upper[:, 1] - c[1], upper[:, 0] - c[0]); a = np.where(a < -math.pi / 2, a + 2 * math.pi, a)
    upper = upper[np.argsort(-a)]
    arc = np.vstack([[upper[0, 0] - .05, foot], upper, [upper[-1, 0] + .05, foot]])
    ctr = np.array([arc[:, 0].mean(), FLOOR + 1.5]); arc = ctr + (arc - ctr) * 1.07; arc[[0, -1], 1] = foot
    return np.column_stack([arc[:, 0], arc[:, 1] - FLOOR])


ARCH_HALF = [(2.95, -.5), (2.92, .8), (2.88, 2.2), (2.82, 3.05), (2.6, 3.7), (2.1, 4.15), (1.25, 4.42), (0, 4.52)]
ARCH = np.array([(-x, y) for x, y in ARCH_HALF] + [(x, y) for x, y in ARCH_HALF[-2::-1]])


def resample(poly, n):
    d = np.r_[0, np.cumsum(np.linalg.norm(np.diff(poly, axis=0), axis=1))]; t = np.linspace(0, d[-1], n)
    return np.column_stack([np.interp(t, d, poly[:, 0]), np.interp(t, d, poly[:, 1])])


N = 72
SEC0 = resample(v4_section(), N); SEC1 = resample(ARCH, N)


def smooth(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0, 1); return t * t * (3 - 2 * t)


s0 = s_clip - .6
S = np.arange(s0, s_face + 1e-6, .4); S[-1] = s_face


def ring_lh(s):
    w = smooth(s0 + 1.2, s_face - 2.0, s); return SEC0 * (1 - w) + SEC1 * w


def ring_world(s):
    lh = ring_lh(s); P = np.array([axis(s, l) + UPV * h for l, h in lh])
    ctr = axis(s, float(lh[:, 0].mean())) + UPV * 1.6
    n = P - ctr; n[:, 1] *= .8; n = nz(n)
    fade = smooth(s0, s0 + 1.4, s); foot = np.clip((lh[:, 1] + .5) / 1.2, 0, 1)
    amp = .30 * fbm(P, 3.2, 2, 7) + .13 * fbm(P, .9, 2, 11)
    return P + n * (amp * fade * foot)[:, None]


rings = [ring_world(s) for s in S]
end_ring = rings[-1]


# -- clear volume and terrain hole cells ------------------------------------------------------------------------
def inside_clear(p, margin=.35):
    """Gallery interior (behind the face) or the opening approach (in front); p world points (..., 3)."""
    s, l, h = frame(p); s = np.atleast_1d(s); l = np.atleast_1d(l); h = np.atleast_1d(h); out = np.zeros(s.shape, bool)
    front = (s >= s_face) & (s <= s_face + 4.5) & (np.abs(l) < 3.3 + margin) & (h > .25) & (h < 5.0 + margin)
    out |= front
    back = (s >= s0) & (s < s_face)
    for idx in np.nonzero(back)[0]:
        lh = ring_lh(float(s[idx])); half = np.interp(h[idx], lh[:N // 2, 1], -lh[:N // 2, 0]) if h[idx] < lh[:, 1].max() else -1
        if h[idx] > -.2 and h[idx] < lh[:, 1].max() + margin and abs(l[idx]) < half + margin: out[idx] = True
    return out


def hole_cells():
    cells = []
    lo = np.floor((np.minimum(axis(s0 - 2, -9), axis(s_face + 8, 9)) - 8) / CELL).astype(int)
    hi = np.ceil((np.maximum(axis(s0 - 2, -9), axis(s_face + 8, 9)) + 8) / CELL).astype(int)
    for ix in range(min(lo[0], hi[0]), max(lo[0], hi[0]) + 1):
        for iz in range(min(lo[2], hi[2]), max(lo[2], hi[2]) + 1):
            x0, z0 = ix * CELL, iz * CELL
            u, v = np.meshgrid(np.linspace(0, 1, 9), np.linspace(0, 1, 9)); x = x0 + u.ravel() * CELL; z = z0 + v.ravel() * CELL
            P = np.stack([x, terrain(x, z), z], -1)
            if inside_clear(P).any(): cells.append((ix, iz))
    return cells


HOLES = hole_cells()
HOLE_SET = set(HOLES)


def in_hole(p, margin=0.0):
    p = np.asarray(p, float)
    for dx in (-margin, 0, margin):
        for dz in (-margin, 0, margin):
            if (int((p[0] + dx) // CELL), int((p[2] + dz) // CELL)) in HOLE_SET: return True
    return False


# -- meshes -----------------------------------------------------------------------------------------------------
def gallery():
    mb = MB('Portal_Gallery'); P = np.array(rings)
    uv = np.stack(np.meshgrid(S, np.arange(N) * .25, indexing='ij'), -1)
    centre = lambda Q: np.stack([axis(s, 0) + UPV * 1.8 for s in S])[:, None, :] - Q
    mb.grid(P, 'cave_rock', uv, centre)
    return mb


def floor_strip():
    """Walkable floor inside the gallery (section feet), ending just past the face under the apron."""
    mb = MB('Portal_Floor'); Sf = np.arange(s_clip, s_face + .31, .4); rows = []
    for s in Sf:
        lh = ring_lh(min(s, s_face)); half = min(-lh[0, 0], lh[-1, 0]) + .15
        rows.append([[*axis(s, l)[[0]], FLOOR - .04 + .025 * float(fbm(axis(s, l), .7, 2, 3)), axis(s, l)[2]] for l in np.linspace(-half, half, 17)])
    P = np.array(rows); uv = P[..., [0, 2]] * .25
    mb.grid(P, 'cave_floor', uv, UPV); return mb


def apron():
    """Terrain patch over the hole cells in front of the face: follows the (bilinear) terrain, overlaps the intact terrain by
    .6 m slightly below it, and is clipped down to the floor inside the approach so the opening stays clear."""
    mb = MB('Portal_Apron')
    xs = [c[0] * CELL for c in HOLES]; zs = [c[1] * CELL for c in HOLES]
    X = np.arange(min(xs) - .6, max(xs) + CELL + .61, .5); Z = np.arange(min(zs) - .6, max(zs) + CELL + .61, .5)
    grid = np.zeros((len(Z), len(X), 3)); keep = np.zeros((len(Z), len(X)), bool)
    for i, z in enumerate(Z):
        for j, x in enumerate(X):
            s, l, _ = frame(np.array([x, 0, z])); y = float(terrain(x, z)) - .03
            w = smooth(4.4, 3.4, abs(l)) * smooth(s_face - .6, s_face + .2, s) * smooth(s_face + 7, s_face + 5.5, s)
            y = min(y, y * (1 - w) + (FLOOR - .02) * w)
            grid[i, j] = [x, y, z]; keep[i, j] = s >= s_face - .45 and in_hole(np.array([x, 0, z]), .6)
    b = mb._add(grid.reshape(-1, 3), np.tile(UPV, (grid.shape[0] * grid.shape[1], 1)), grid.reshape(-1, 3)[:, [0, 2]] * .25)
    nx = len(X)
    for i in range(len(Z) - 1):
        for j in range(nx - 1):
            if keep[i, j] and keep[i, j + 1] and keep[i + 1, j] and keep[i + 1, j + 1]:
                q = [b + i * nx + j, b + i * nx + j + 1, b + (i + 1) * nx + j + 1, b + (i + 1) * nx + j]
                mb.T['terrain'] += [q[0], q[2], q[1], q[0], q[3], q[2]]          # +y up in Unity winding
    Vv = np.asarray(mb.V); t = mb.T['terrain']
    for k in range(0, len(t), 3):
        i, j, l = t[k:k + 3]
        if np.cross(Vv[j] - Vv[i], Vv[l] - Vv[i])[1] < 0: t[k + 1], t[k + 2] = l, j
    # smooth normals from the faces
    Nn = np.zeros_like(Vv)
    for k in range(0, len(t), 3):
        i, j, l = t[k:k + 3]; fn = np.cross(Vv[j] - Vv[i], Vv[l] - Vv[i]); Nn[[i, j, l]] += fn
    mb.N = nz(np.where(np.linalg.norm(Nn, axis=1, keepdims=True) > 0, Nn, UPV)).tolist()
    return mb


def ridged(P, scale, octaves=3, seed=1):
    """Angular rock relief: 1-|noise| folds, sharpened, in [-1, 1]."""
    a, tot, n = 1.0, 0.0, 0.0
    for o in range(octaves):
        r = 1 - np.abs(vnoise(P, scale / 2 ** o, seed + o)); tot = tot + a * r * r; n += a; a *= .5
    return tot / n * 2 - 1


def _exit_hole(start, direction, margin=.45, limit=16.0):
    """March a plan ray from `start` until it has left every hole cell by `margin`; returns the plan point (x, z)."""
    q = np.array(start, float); d = nz(np.array(direction, float)); t = 0.0
    while t < limit:
        t += .1; q2 = np.array(start) + d * t
        if not in_hole(np.array([q2[0], 0, q2[1]]), margin): return q2
    return np.array(start) + d * limit


def hood():
    """Rock hood spanning the opening edge to the hole outline. For every opening-edge point a plan ray (back over the roof
    for the upper arc, sideways for the walls/feet) finds where the hole ends (+.45 m under intact terrain); the hood runs
    from the edge (continuing the cut face briefly) to that point .35 m under the terrain edge, so the hole is sealed and the
    rock reads as the hillside broken open above the adit. It keeps .6 m over the gallery roof. Angular relief + bedding."""
    mb = MB('Portal_Face'); lh_in = ring_lh(s_face); c = np.array([0., 1.9]); K = 10
    crown = float(lh_in[:, 1].max())
    P = np.zeros((K + 1, N, 3)); reach = []
    for j, (l, h) in enumerate(lh_in):
        E = end_ring[j]; u = nz(np.array([l, h]) - c); up = max(0.0, u[1])
        plan_dir = R * u[0] + (-D) * (1.25 * up + .12)
        if h < .2: plan_dir = R * (np.sign(l) if l else 1) + (-D) * .15
        Bp = _exit_hole(E[[0, 2]], plan_dir); B = np.array([Bp[0], float(terrain(Bp[0], Bp[1])) - .35, Bp[1]])
        face_out = R3 * u[0] + UPV * u[1]
        C1 = E + face_out * (1.0 + .6 * up) + UPV * (.4 * up)
        C2 = B + UPV * (.9 + .8 * up) + np.array([E[0] - B[0], 0, E[2] - B[2]]) * .25
        for k in range(K + 1):
            s_ = (k / K) ** 1.1; m = 1 - s_
            q = m ** 3 * E + 3 * m * m * s_ * C1 + 3 * m * s_ * s_ * C2 + s_ ** 3 * B
            if 0 < k < K:
                ss, ll, hh = frame(q)
                if ss < s_face - .2 and abs(ll) < 3.4: q[1] = max(q[1], FLOOR + crown + .6)       # over the gallery roof
            P[k, j] = q
        reach.append(float(np.linalg.norm(B - E)))
    du = np.gradient(P, axis=0); dv = np.gradient(P, axis=1); gn = nz(np.cross(du, dv))
    ref = P - (axis(s_face - 2) + UPV * 1.5); gn = np.where((np.sum(gn * ref, -1) < 0)[..., None], -gn, gn)
    for k in range(1, K):
        w = min(1.0, k / 2.5) * min(1.0, (K - k) / 2.0)
        amp = .36 * ridged(P[k], 2.4, 3, 21) + .09 * np.sin(P[k][:, 1] * 2.1 + 1.7 * vnoise(P[k], 3.0, 9)) + .11 * fbm(P[k], .8, 2, 5)
        P[k] += gn[k] * (amp * w)[:, None]
    uv = np.stack([P[..., 0] * .25 + P[..., 2] * .25, P[..., 1] * .25], -1)
    mb.grid(P, 'cliff_rock', uv, lambda Q: Q - (axis(s_face - 2) + UPV * 1.5))
    return mb, reach


def timbers():
    mb = MB('Portal_Timbers'); sets = []
    for s in (s_face - .55, s_face - 3.6, s_face - 6.8):
        lh = ring_lh(s); band = (lh[:, 1] > 1.0) & (lh[:, 1] < 2.6)
        lw = -lh[band & (lh[:, 0] < 0), 0].max() if (band & (lh[:, 0] < 0)).any() else 2.8
        rw = lh[band & (lh[:, 0] > 0), 0].min() if (band & (lh[:, 0] > 0)).any() else 2.8
        half = min(lw, rw) - .42; cap = 3.35 if s > s_face - 1 else min(3.55, 3.35 + .06 * (s_face - s))
        for q in (axis(s, -half), axis(s, half)):
            beam(mb, q + UPV * -.05, q + UPV * cap, .30, .30, 'wood_dark')
        beam(mb, axis(s, -half - .32) + UPV * (cap + .17), axis(s, half + .32) + UPV * (cap + .17), .36, .34, 'wood_dark')
        for k in range(-2, 3):
            off = D3 * k * .23
            beam(mb, axis(s, -half - .1) + UPV * (cap + .40) + off, axis(s, half + .1) + UPV * (cap + .40) + off, .2, .07, 'wood_board')
        sets.append(dict(s=round(s, 2), half=round(float(half), 2), cap=round(float(cap), 2)))
    beam(mb, axis(s_face + .15, -2.5) + UPV * .06, axis(s_face + .15, 2.5) + UPV * .06, .26, .18, 'wood_dark')
    return mb, sets


_ICO_F = [(0, 11, 5), (0, 5, 1), (0, 1, 7), (0, 7, 10), (0, 10, 11), (1, 5, 9), (5, 11, 4), (11, 10, 2), (10, 7, 6), (7, 1, 8),
          (3, 9, 4), (3, 4, 2), (3, 2, 6), (3, 6, 8), (3, 8, 9), (4, 9, 5), (2, 4, 11), (6, 2, 10), (8, 6, 7), (9, 8, 1)]


def _icosphere():
    t = (1 + 5 ** .5) / 2
    return nz(np.array([(-1, t, 0), (1, t, 0), (-1, -t, 0), (1, -t, 0), (0, -1, t), (0, 1, t), (0, -1, -t), (0, 1, -t), (t, 0, -1), (t, 0, 1), (-t, 0, -1), (-t, 0, 1)], float))


def debris():
    mb = MB('Portal_Debris'); rng = np.random.default_rng(297); ico = _icosphere()
    for i in range(34):
        s = s_face + .9 + rng.gamma(2.0, 1.6); l = rng.normal(0, 1.3 + .25 * (s - s_face))
        size = float(np.clip(rng.lognormal(-1.1, .5), .12, .85))
        p = axis(s, l); y = FLOOR if in_hole(p) else min(FLOOR, float(terrain(p[0], p[2]))) if s < s_face + 1.5 else float(terrain(p[0], p[2]))
        V = ico * size * np.array([1, .62, 1]) * (1 + .18 * rng.standard_normal((len(ico), 1)))
        yaw = rng.uniform(0, 2 * math.pi); cy, sy = math.cos(yaw), math.sin(yaw)
        V = np.stack([V[:, 0] * cy + V[:, 2] * sy, V[:, 1], -V[:, 0] * sy + V[:, 2] * cy], -1) + np.array([p[0], y - .28 * size, p[2]])
        b = mb._add(V, nz(V - V.mean(0)), V[:, [0, 2]] * .5)
        for f in _ICO_F: mb.T['rock_loose'] += [b + f[0], b + f[1], b + f[2]]
    Vv = np.asarray(mb.V); t = mb.T['rock_loose']
    for k in range(0, len(t), 3):
        i, j, l = t[k:k + 3]
        if np.dot(np.cross(Vv[j] - Vv[i], Vv[l] - Vv[i]), np.asarray(mb.N[i])) < 0: t[k + 1], t[k + 2] = l, j
    beam(mb, axis(s_face + 2.2, -1.9) + UPV * .16, axis(s_face + 3.9, -.4) + UPV * .28, .28, .28, 'wood_dark')
    beam(mb, axis(s_face + 1.6, 1.2) + UPV * .14, axis(s_face + 2.9, 2.6) + UPV * .12, .30, .30, 'wood_dark')
    beam(mb, axis(s_face + 3.0, .4) + UPV * .12, axis(s_face + 3.3, 2.1) + UPV * .30, .34, .32, 'wood_dark')
    return mb


def plan(hood_mb, path):
    from PIL import Image, ImageDraw
    sc = 14; o = axis(s_face); size = 44
    im = Image.new('RGB', (size * sc, size * sc), 'white'); d = ImageDraw.Draw(im)
    P = lambda x, z: ((x - o[0] + size / 2) * sc, (size / 2 - (z - o[2])) * sc)
    for (ix, iz) in HOLES:
        x0, z0 = ix * CELL, iz * CELL; d.polygon([P(x0, z0), P(x0 + CELL, z0), P(x0 + CELL, z0 + CELL), P(x0, z0 + CELL)], fill=(255, 210, 210), outline=(220, 120, 120))
    V = np.asarray(hood_mb.V)
    for q in V[::3]: d.point(P(q[0], q[2]), fill=(60, 60, 60))
    for r in rings[::4]: d.line([P(q[0], q[2]) for q in r], fill=(90, 120, 200))
    d.line([P(*axis(s0)[[0, 2]]), P(*axis(s_face + 6)[[0, 2]])], fill=(0, 160, 0), width=2)
    d.text((6, 6), f'hole cells {len(HOLES)}  face s={s_face}  red=hole  grey=hood  blue=gallery', fill='black')
    im.save(path)


if __name__ == '__main__':
    origin = axis(s_face)
    g = gallery(); f, reach = hood(); fl = floor_strip(); ap = apron(); tb, sets = timbers(); db = debris()
    OUT.mkdir(parents=True, exist_ok=True); plan(f, OUT / 'plan.png')
    meshes = []
    for mb in (g, f, fl, ap, tb, db):
        mb.V = (np.asarray(mb.V) - origin).tolist()
        mb.export(OUT / 'Meshes' / f'{mb.name}.json'); meshes.append(dict(name=mb.name, tris=mb.tris()))
    unity = dict(note='#297 cave portal (TEST) — world-aligned meshes around origin; parent = mine (world position kept)',
                 origin=origin.round(3).tolist(), axisA=A.round(3).tolist(), axisD=D.round(4).tolist(), floor=FLOOR,
                 clipS=round(s_clip + .3, 3), floorClipS=round(s_clip + .3, 3), faceS=round(s_face, 3),
                 clipHalfWidth=12.0, clipMaxY=FLOOR + 14, cell=CELL, holeCells=[c for cell in HOLES for c in cell],
                 meshes=[dict(name=m['name'], collider=m['name'] in ('Portal_Gallery', 'Portal_Floor', 'Portal_Apron', 'Portal_Face')) for m in meshes],
                 retire=['mine/Playtest_NaturalCave/Continuous_Approach_Soil'], timberSets=sets)
    (OUT / 'unity.json').write_text(json.dumps(unity, ensure_ascii=False, indent=1), encoding='utf-8')
    print(f's_clip {s_clip:.2f} (l_shift {l_shift:.2f})  face s {s_face}  gallery {s_face - s0:.1f} m  hole cells {len(HOLES)} {HOLES}')
    print('terrain above axis:', ' '.join(f'{s:.0f}:{rel_terrain(s):.1f}' for s in np.arange(-10, 6, 1)))
    print('hood reach min/max %.1f / %.1f' % (min(reach), max(reach)))
    print('meshes', meshes)
