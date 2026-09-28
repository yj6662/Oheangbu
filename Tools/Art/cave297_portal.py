"""#297 opening mine (폐광) portal — closes the V4 open trench under the #292/#295 hillside (SPEC-WORLD-FINISH-297 §2).

After the rigid relocation the V4 cave's last ~25 m ("open mouth" trench, originally roofed by the retired exterior shell)
sits 6-15 m under the new hillside; terrain seen from below is back-face culled, so the player saw sky. A 4 m height field
cannot hold a vertical cut, so the portal is made the way heightmap games make cave mouths:
  * the notch (surface297.py, portal.json) cuts the approach to the floor in front of the face and ends sharply behind it;
  * terrain cells whose surface would cross the gallery or the opening are listed as hole cells (removed from the private
    LOD0 tile mesh + collider in Unity);
  * Portal_Face is the granite outcrop the adit is driven into — a broad, low, bedded rock band (joint blocks, a brow bed
    overhanging the portal, a stepped-back cap, ledges dipping under the slope, partially buried boulders) built as a
    signed-distance field and polygonised with surface nets; it seals the hole cells behind the face and its edges dip
    under intact terrain (see outcrop());
  * Portal_Gallery — rock gallery (갱도) walls + roof from just inside the V4 tube (clip plane) to the face, its section
    morphing from the measured V4 section to a timbered adit arch; Portal_Floor under it and out over the hole cells;
  * Portal_Timbers — 갱목 sets in round wood (log posts, split caps, wedges, lagging between the sets) and a log sill;
    Portal_Debris — blast rubble, a talus fan at the foot of the face and a collapsed old set.
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


# -- Portal_Face: the granite outcrop the adit is driven into ---------------------------------------------------------
# A signed-distance rock mass in the portal frame (s along the axis, l to the right, h above FLOOR), polygonised with
# surface nets (numpy only). Bedded granite: strata beds cut into joint blocks (rounded boxes — bedding planes and vertical
# joints read as grooves), a brow bed overhanging the portal (1.6-2.6 m above the crown), a stepped-back cap bed, ledges
# outcropping up the slope and dipping under it, a filler body over the hole cells (the seal), and partially buried boulders.
# The face wraps forward into the notch walls; the outline is asymmetric (higher, wider to the uphill left).
# The gallery tunnel (the actual Portal_Gallery rings) is cut out: .15 m inside the gallery wall at the face, so the gallery
# rim is always behind rock (no crack), splayed in front, and .35 m outside the wall deeper in. Faces nobody can see —
# under intact terrain / the apron / the floor, or behind the gallery wall — are dropped.
VOX = .33
BOX = (-12.4, s_face + 8.8, -13.4, 12.8, -1.9, 14.8)          # s, l, h extent of the SDF grid
RLH = np.array([np.column_stack(frame(r)[1:]) for r in rings])  # gallery rings in (l, h)
SPLAY = .12                                                   # opening widens 12 %/m in front of the face


def lw_world(s, l):
    s = np.asarray(s, float); l = np.asarray(l, float)
    return A[0] + D[0] * s + R[0] * l, A[1] + D[1] * s + R[1] * l


def rel_ground(s, l):
    x, z = lw_world(s, l); return terrain(x, z) - FLOOR


def terrain_mesh(x, z):
    """Height of the LOD0 terrain tile mesh (CompactReworld292.Terrain: 4 m quads split along the (x+1,z)-(x,z+1) diagonal)."""
    fx = np.clip(np.asarray(x, float) / CELL, 0, W - 1); fz = np.clip(np.asarray(z, float) / CELL, 0, HH - 1)
    ix = np.minimum(fx.astype(int), W - 2); iz = np.minimum(fz.astype(int), HH - 2); u = fx - ix; v = fz - iz
    h00, h10, h01, h11 = H[iz, ix], H[iz, ix + 1], H[iz + 1, ix], H[iz + 1, ix + 1]
    return np.where(u + v <= 1, h00 + (h10 - h00) * u + (h01 - h00) * v, h11 + (h01 - h11) * (1 - u) + (h10 - h11) * (1 - v))


def hole_sd(x, z):
    """Signed plan distance to the union of hole cells (negative inside)."""
    x = np.asarray(x, float); z = np.asarray(z, float); d = np.full(x.shape, 1e9)
    for ix, iz in HOLES:
        x0, z0 = ix * CELL, iz * CELL
        dx = np.maximum(np.maximum(x0 - x, x - x0 - CELL), 0); dz = np.maximum(np.maximum(z0 - z, z - z0 - CELL), 0)
        ins = -np.minimum(np.minimum(x - x0, x0 + CELL - x), np.minimum(z - z0, z0 + CELL - z))
        d = np.minimum(d, np.where((dx == 0) & (dz == 0), ins, np.hypot(dx, dz)))
    return d


def apron_y(x, z):
    """Height of Portal_Apron (same rule as apron()) — the ground over the hole cells in front of the face."""
    q = np.stack([np.asarray(x, float) - A[0], np.asarray(z, float) - A[1]], -1); s = q @ D; l = q @ R
    w = smooth(4.4, 3.4, np.abs(l)) * smooth(s_face - .6, s_face + .2, s) * smooth(s_face + 7, s_face + 5.5, s)
    y = terrain(x, z) - .03; return np.minimum(y, y * (1 - w) + (FLOOR - .02) * w)


def ground_y(x, z):
    """Visible ground: the apron over hole cells in front of the face, else the terrain."""
    q = np.array([x - A[0], z - A[1]]); return float(apron_y(x, z)) if in_hole(np.array([x, 0, z])) and q @ D >= s_face - .45 else float(terrain(x, z))


def face_line(l):
    l = np.asarray(l, float)
    return s_face + .12 + .12 * np.maximum(np.abs(l) - 3.4, 0) ** 1.3 + .2 * vnoise(np.stack([l, l * 0 + 3.3, l * 0 + 1.7], -1), 2.6, 41)


def _tunnel_poly(s):
    """Per-point gallery section polygon (l, h), closed 3 m below the floor; splayed in front of the face."""
    i = np.clip(np.searchsorted(S, s) - 1, 0, len(S) - 2); t = np.clip((s - S[i]) / (S[i + 1] - S[i]), 0, 1)[:, None, None]
    P = RLH[i] * (1 - t) + RLH[i + 1] * t
    front = s > s_face
    if front.any():
        last = RLH[-1]; base = np.array([last[:, 0].mean(), -.5]); sc = 1 + SPLAY * (s[front] - s_face)
        P[front] = base + (last[None] - base) * sc[:, None, None]
    bot = np.full((len(s), 2, 2), -3.2); bot[:, 0, 0] = P[:, -1, 0]; bot[:, 1, 0] = P[:, 0, 0]
    return np.concatenate([P, bot], 1)


def _poly_sd(q, poly):
    a = poly; b = np.roll(poly, -1, axis=1); ab = b - a; aq = q[:, None, :] - a
    t = np.clip(np.sum(aq * ab, -1) / np.maximum(np.sum(ab * ab, -1), 1e-12), 0, 1)
    d = np.sqrt(np.sum((aq - ab * t[..., None]) ** 2, -1).min(1))
    ay, by = a[..., 1], b[..., 1]; qy = q[:, 1:2]; cross = (ay > qy) != (by > qy)
    xi = a[..., 0] + (qy - ay) * (b[..., 0] - a[..., 0]) / np.where(by == ay, 1e-12, by - ay)
    return np.where(np.sum(cross & (q[:, 0:1] < xi), 1) % 2 == 1, -d, d)


def tunnel_sd(s, l, h):
    """Signed distance to the gallery section at s (negative inside the gallery)."""
    out = np.full(s.shape, 9.0); idx = np.nonzero((np.abs(l) < 6.5) & (h < 8.5))[0]
    for c in range(0, len(idx), 6000):
        j = idx[c:c + 6000]; out[j] = _poly_sd(np.stack([l[j], h[j]], -1), _tunnel_poly(s[j]))
    return out


def tunnel_margin(s):
    """Rock stays this far outside the gallery wall: -.15 (inside, sealing the rim) near the face, +.35 behind."""
    return np.interp(s, [s_face - 1.7, s_face - .5], [.35, -.15])


def _rot(yaw, pitch=0., roll=0.):
    cy, sy, cp, sp, cr, sr = math.cos(yaw), math.sin(yaw), math.cos(pitch), math.sin(pitch), math.cos(roll), math.sin(roll)
    return np.array([[cy, -sy, 0], [sy, cy, 0], [0, 0, 1.]]) @ np.array([[cp, 0, -sp], [0, 1, 0], [sp, 0, cp]]) @ np.array([[1, 0, 0], [0, cr, -sr], [0, sr, cr]])


def _block(c, half, yaw=0., pitch=0., roll=0., r=.2):
    M = _rot(yaw, pitch, roll); ext = np.abs(M) @ np.asarray(half, float)
    return dict(c=np.asarray(c, float), half=np.asarray(half, float), M=M, r=r, lo=np.asarray(c) - ext - .1, hi=np.asarray(c) + ext + .1)


# face beds over the portal: (bottom, top, setback range (+ = forward of the face line), l extent, joint spacing range, skip)
# Thick, unequal beds with widely spaced vertical joints (granite sheeting), domain-warped in rock_sdf so no face is planar.
FACE_BEDS = [(-1.3, .9, (.2, .55), (-10.6, 10.2), (2.4, 4.8), 0), (.9, 2.6, (.05, .45), (-10.6, 10.0), (2.2, 4.6), 0),
             (2.6, 4.1, (.0, .4), (-10.2, 9.4), (2.0, 4.4), 0), (4.1, 5.2, (.1, .35), (-9.6, 8.4), (2.4, 5.0), 0),
             (5.2, 6.6, (.85, 1.25), (-8.4, 6.6), (3.0, 5.6), 0),     # brow: overhangs the portal
             (6.6, 7.3, (-.6, -.15), (-6.8, 4.4), (1.8, 3.6), .3)]    # cap bed, stepped back, patchy
BROW = 4
# ledges up the slope above the cap: (bottom, top, l extent, skip) — sparse, narrowing uphill, dipping under the terrain
LEDGES = [(7.0, 8.4, (-7.8, 5.2), .25), (8.4, 9.7, (-6.6, 3.8), .3), (9.7, 10.9, (-5.2, 2.2), .4)]
DIP = -.035                                                   # beds rise ~2° toward the uphill left (-l)


def bed_shift(l, k):
    return DIP * l + .2 * math.sin(.29 * l + 1.3 * k) + .1 * math.sin(.83 * l + 2.1 * k)


def rock_blocks():
    rng = np.random.default_rng(2974); blocks = []
    for k, (b0, b1, (sb0, sb1), (l0, l1), (w0, w1), skip) in enumerate(FACE_BEDS):
        l = l0 - rng.uniform(0, 2.0)
        while l < l1:
            w = rng.uniform(w0, w1); lc = l + w / 2; sh = bed_shift(lc, k); l += w + .04
            if rng.random() < skip: continue
            back = rng.uniform(sb0, sb1); side = smooth(4.4, 8.0, abs(lc + .6))
            if k == BROW: back = back * (1 - side) + rng.uniform(.3, .55) * side
            front = float(face_line(lc)) + back; depth = rng.uniform(3.0, 3.8) if k < 5 else rng.uniform(3.6, 4.4)
            hb, ht = b0 + sh + rng.normal(0, .14), b1 + sh + rng.normal(0, .14)
            r = min(rng.uniform(.28, .5), .42 * (ht - hb))
            blocks.append(_block([front - depth / 2, lc, (hb + ht) / 2], [depth / 2, w / 2 - .02, (ht - hb) / 2 + .04],
                                 rng.normal(0, .07), rng.normal(0, .04), rng.normal(0, .05) + DIP * .6, r))
    for k, (b0, b1, (l0, l1), skip) in enumerate(LEDGES):
        l = l0 + rng.uniform(-.8, .8)
        while l < l1:
            w = rng.uniform(2.2, 4.2); lc = l + w / 2; l += w + .08
            if rng.random() < skip and abs(lc + .6) > 3.8: continue       # never skip over the hole cells
            sh = bed_shift(lc, k + 7) + rng.normal(0, .22); top = b1 + sh; expo = rng.uniform(.25, .65)
            ss = np.arange(float(face_line(lc)) - .9, -13.0, -.1); g = rel_ground(ss, np.full_like(ss, lc))
            hit = np.nonzero(g >= top - expo)[0]
            if not len(hit): continue
            sf = ss[hit[0]]; e = .3
            gs = (rel_ground(sf + e, lc) - rel_ground(sf - e, lc)) / (2 * e); gl = (rel_ground(sf, lc + e) - rel_ground(sf, lc - e)) / (2 * e)
            dn = nz(np.array([-gs, -gl])); yaw = float(np.clip(math.atan2(dn[1], dn[0]), -.6, .6)) + rng.normal(0, .2)
            dv = np.array([math.cos(yaw), math.sin(yaw)]); depth = rng.uniform(2.2, 3.2); c = np.array([sf, lc]) - dv * depth / 2
            hb = b0 + sh + rng.normal(0, .08); r = min(rng.uniform(.3, .5), .42 * (top - hb))
            blocks.append(_block([c[0], c[1], (hb + top) / 2], [depth / 2, w / 2, (top - hb) / 2 + .03], yaw, rng.normal(0, .05), rng.normal(0, .05), r))
    # joint slabs pitched with the steep slope over the hole cells above the plateau (the seal's visible skin)
    for sc in np.arange(-6.4, -.4, 2.0):
        for lc in np.arange(-6.6, 5.6, 2.2):
            c = np.array([sc, lc]) + rng.uniform(-.5, .5, 2); x, z = lw_world(*c)
            g = float(rel_ground(*c))
            if hole_sd(np.array([x]), np.array([z]))[0] > .7 or g < 6.9: continue
            e = .3
            gs = (rel_ground(c[0] + e, c[1]) - rel_ground(c[0] - e, c[1])) / (2 * e); gl = (rel_ground(c[0], c[1] + e) - rel_ground(c[0], c[1] - e)) / (2 * e)
            dn = nz(np.array([-gs, -gl])); yaw = math.atan2(dn[1], dn[0]) + rng.normal(0, .15); slope = math.atan(math.hypot(gs, gl))
            th = rng.uniform(.8, 1.1); top = rng.uniform(.25, .5); nrm = np.array([-math.sin(slope) * dn[0], -math.sin(slope) * dn[1], math.cos(slope)])
            ctr = np.array([c[0], c[1], g + top]) - nrm * th / 2
            blocks.append(_block(ctr, [rng.uniform(1.2, 1.6), rng.uniform(1.2, 1.7), th / 2], yaw, -slope * rng.uniform(.85, 1.0), rng.normal(0, .06), rng.uniform(.3, .42)))
    return blocks


# partially buried boulders around the mouth (s, l, half-extents s/l/h, buried fraction); all clear of the |l| < 4.1 approach
BOULDERS = [(s_face + 1.0, -5.35, (.9, 1.1, .75), .42), (s_face + 3.1, -6.3, (.7, .8, .55), .38), (s_face + 5.9, -5.7, (.55, .65, .45), .35),
            (s_face + .9, 5.15, (.8, .95, .65), .4), (s_face + 2.8, 6.1, (1.0, 1.2, .8), .45), (s_face + 6.6, 5.4, (.5, .6, .42), .35),
            (s_face + .3, -8.5, (1.3, 1.5, 1.0), .45), (s_face + .8, 8.3, (1.1, 1.3, .85), .42),
            (-2.6, -10.9, (1.2, 1.4, .9), .5), (-4.2, 8.3, (1.0, 1.1, .8), .5), (-8.6, -3.8, (.9, 1.1, .7), .5)]


def boulder_blocks():
    rng = np.random.default_rng(2975); out = []
    for s, l, half, bury in BOULDERS:
        x, z = lw_world(s, l); g = ground_y(float(x), float(z)) - FLOOR
        out.append(_block([s, l, g + half[2] * (1 - 2 * bury)], half, rng.uniform(-math.pi, math.pi), rng.normal(0, .12), rng.normal(0, .12), min(half) * rng.uniform(.5, .7)))
    return out


BLOCKS = rock_blocks() + boulder_blocks()


def rock_sdf(s, l, h, G=None, hsd=None):
    """Rock mass SDF (negative inside) at flat arrays of frame points."""
    s = np.asarray(s, float); l = np.asarray(l, float); h = np.asarray(h, float)
    if G is None: G = rel_ground(s, l)
    if hsd is None: hsd = hole_sd(*lw_world(s, l))
    F = np.full(s.shape, 6.0); X = np.stack([s, l, h], -1)
    Xw = X + np.stack([.3 * fbm(X, 3.5, 2, 91), .3 * fbm(X, 3.5, 2, 92), .2 * fbm(X, 3.5, 2, 93)], -1)   # bent beds
    for b in BLOCKS:
        m = np.all((Xw >= b['lo']) & (Xw <= b['hi']), -1)
        if not m.any(): continue
        q = (Xw[m] - b['c']) @ b['M']; d = np.abs(q) - (b['half'] - b['r'])
        F[m] = np.minimum(F[m], np.linalg.norm(np.maximum(d, 0), axis=-1) + np.minimum(d.max(-1), 0) - b['r'])
    f = face_line(l)
    # filler body: a plateau behind the face beds (under the cap), and a rough swell at the would-be ground inside the hole
    # (the seal, mostly under the joint slabs) that dips under the intact terrain ~.5 m outside it
    P = 6.55 + DIP * l + .2 * fbm(np.stack([s, l, l * 0], -1), 4.0, 2, 61) - 3.1 * smooth(4.2, 10.4, np.abs(l + .7))
    F = np.minimum(F, np.maximum(h - P, s - (f - .45)))
    e = .05 - .45 * smooth(-.4, .9, hsd + .35 * fbm(X, 2.0, 2, 83)) + .15 * ridged(X, 1.8, 2, 81) * (1 - smooth(-.2, .8, hsd))
    F = np.minimum(F, np.maximum(np.maximum(h - (G + e), s - (f - .3)), hsd - 1.2))
    F = F + .11 * fbm(X, 2.2, 3, 71) + .06 * ridged(X, .9, 2, 77)                          # weathering
    F = np.maximum(F, -(tunnel_sd(s, l, h) - tunnel_margin(s)))                           # the adit
    bot = np.where(hsd < 1.0, np.minimum(G, 0) - 1.3, G - 1.0)
    return np.maximum(F, bot - h)


def surface_nets(F):
    """Naive surface nets on a sampled field (negative inside) -> vertices in grid-index space, triangles wound outward."""
    nx, ny, nzz = F.shape; ins = F < 0
    cnt = sum(ins[i:nx - 1 + i, j:ny - 1 + j, k:nzz - 1 + k].astype(np.int8) for i in (0, 1) for j in (0, 1) for k in (0, 1))
    active = (cnt > 0) & (cnt < 8); acc = np.zeros(active.shape + (3,)); num = np.zeros(active.shape)
    for a in ((0, 0, 0), (0, 1, 0), (0, 0, 1), (0, 1, 1), (1, 0, 0), (1, 1, 0), (1, 0, 1), (1, 1, 1)):
        for ax in range(3):
            if a[ax]: continue
            b = list(a); b[ax] = 1
            Fa = F[a[0]:nx - 1 + a[0], a[1]:ny - 1 + a[1], a[2]:nzz - 1 + a[2]]; Fb = F[b[0]:nx - 1 + b[0], b[1]:ny - 1 + b[1], b[2]:nzz - 1 + b[2]]
            cr = (Fa < 0) != (Fb < 0); t = np.where(cr, Fa / np.where(cr, Fa - Fb, 1), 0)
            p = np.broadcast_to(np.array(a, float), Fa.shape + (3,)).copy(); p[..., ax] += t
            acc += np.where(cr[..., None], p, 0); num += cr
    vid = -np.ones(active.shape, np.int64); vid[active] = np.arange(int(active.sum()))
    V = np.argwhere(active) + acc[active] / num[active][:, None]
    quads = []
    for ax in range(3):
        u, v = (ax + 1) % 3, (ax + 2) % 3
        sl_a = [slice(1, -1)] * 3; sl_b = [slice(1, -1)] * 3; sl_a[ax] = slice(0, -1); sl_b[ax] = slice(1, None)
        Ia = ins[tuple(sl_a)]; Ib = ins[tuple(sl_b)]; e = np.argwhere(Ia != Ib); out_pos = Ia[tuple(e.T)]
        g = e.copy(); g[:, u] += 1; g[:, v] += 1                          # edge origin in grid coordinates
        def cell(du, dv):
            c = g.copy(); c[:, u] += du; c[:, v] += dv; return vid[c[:, 0], c[:, 1], c[:, 2]]
        q = np.stack([cell(-1, -1), cell(0, -1), cell(0, 0), cell(-1, 0)], 1)
        quads.append(np.where(out_pos[:, None], q, q[:, ::-1]))
    Q = np.concatenate(quads); T = np.concatenate([Q[:, [0, 1, 2]], Q[:, [0, 2, 3]]])
    return V, T[np.all(T >= 0, 1)]


def outcrop():
    s0_, s1_, l0_, l1_, h0_, h1_ = BOX
    gs = np.arange(s0_, s1_ + 1e-6, VOX); gl = np.arange(l0_, l1_ + 1e-6, VOX); gh = np.arange(h0_, h1_ + 1e-6, VOX)
    Sg, Lg = np.meshgrid(gs, gl, indexing='ij'); G2 = rel_ground(Sg, Lg); HSD2 = hole_sd(*lw_world(Sg, Lg))
    shape = (len(gs), len(gl), len(gh))
    SS = np.broadcast_to(Sg[..., None], shape).ravel(); LL = np.broadcast_to(Lg[..., None], shape).ravel()
    HHg = np.broadcast_to(gh[None, None, :], shape).ravel()
    F = rock_sdf(SS, LL, HHg, np.broadcast_to(G2[..., None], shape).ravel(), np.broadcast_to(HSD2[..., None], shape).ravel()).reshape(shape)
    F[[0, -1]] = np.maximum(F[[0, -1]], .2); F[:, [0, -1]] = np.maximum(F[:, [0, -1]], .2); F[:, :, [0, -1]] = np.maximum(F[:, :, [0, -1]], .2)
    Vg, T = surface_nets(F); n_all = len(T)
    s = s0_ + Vg[:, 0] * VOX; l = l0_ + Vg[:, 1] * VOX; h = h0_ + Vg[:, 2] * VOX
    x, z = lw_world(s, l); lo = terrain_mesh(x, z); hsd = hole_sd(x, z); tsd = tunnel_sd(s, l, h)
    hidden = ((hsd > .05) & (h + FLOOR < lo - .1))                                          # under intact terrain
    hidden |= (hsd <= .05) & (s >= s_face - .3) & (h + FLOOR < apron_y(x, z) - .12)          # under the apron
    hidden |= (hsd <= 1.0) & (h < -.72)                                                      # under the gallery floor
    hidden |= (s < s_face - 1.6) & (np.abs(tsd - tunnel_margin(s)) < .12)                  # behind the gallery wall
    T = T[~np.all(hidden[T], 1)]
    used = np.unique(T); remap = -np.ones(len(Vg), np.int64); remap[used] = np.arange(len(used)); T = remap[T]
    s, l, h, x, z, lo = s[used], l[used], h[used], x[used], z[used], lo[used]
    Vw = np.stack([x, FLOOR + h, z], -1)
    fn = np.cross(Vw[T[:, 1]] - Vw[T[:, 0]], Vw[T[:, 2]] - Vw[T[:, 0]]); Nn = np.zeros_like(Vw)
    for k in range(3): np.add.at(Nn, T[:, k], fn)
    Nn = nz(Nn)
    # orientation check against the field gradient (surface nets winds from the sign pattern; this only reports)
    e = .08; grad = np.stack([rock_sdf(s + e, l, h) - rock_sdf(s - e, l, h), rock_sdf(s, l + e, h) - rock_sdf(s, l - e, h), rock_sdf(s, l, h + e) - rock_sdf(s, l, h - e)], -1)
    gw = nz(grad[:, 0:1] * D3 + grad[:, 1:2] * R3 + grad[:, 2:3] * UPV); agree = float(np.mean(np.sum(gw * Nn, -1) > 0))
    mb = MB('Portal_Face'); b = mb._add(Vw, Nn, np.stack([Vw[:, 0] * .25 + Vw[:, 2] * .25, Vw[:, 1] * .25], -1))
    mb.T['cliff_rock'] = (T + b).ravel().tolist()
    top = FLOOR + h; exposed = top > lo + .05
    stats = dict(grid=list(shape), trisAll=int(n_all), tris=int(len(T)), normalAgree=round(agree, 4), blocks=len(BLOCKS) - len(BOULDERS),
                 boulders=len(BOULDERS), extentL=[round(float(l[exposed].min()), 1), round(float(l[exposed].max()), 1)],
                 extentS=[round(float(s[exposed].min()), 1), round(float(s[exposed].max()), 1)], maxH=round(float(h.max()), 2))
    return mb, stats


def timbers():
    """Portal 갱목: three sets of round wood (tapered, battered log posts; split caps seated on them; wedges under the rock),
    lagging boards between the sets on the roof and the upper sides, the middle set's right post settled and propped, and a
    log sill. Set positions/spans are fitted to the gallery section as before."""
    from cave297_dressing import log, split_log, wedge
    mb = MB('Portal_Timbers'); sets = []; geo = []
    for idx, s in enumerate((s_face - .55, s_face - 3.6, s_face - 6.8)):
        lh = ring_lh(s); band = (lh[:, 1] > 1.0) & (lh[:, 1] < 2.6)
        lw = -lh[band & (lh[:, 0] < 0), 0].max() if (band & (lh[:, 0] < 0)).any() else 2.8
        rw = lh[band & (lh[:, 0] > 0), 0].min() if (band & (lh[:, 0] > 0)).any() else 2.8
        half = min(lw, rw) - .42; cap = 3.35 if s > s_face - 1 else min(3.55, 3.35 + .06 * (s_face - s))
        for sg in (-1, 1):
            q = axis(s, sg * half); head = q + UPV * (cap + .05) - R3 * sg * .06
            if idx == 1 and sg > 0:                                     # settled post, leaning toward the mouth, propped
                head = head + D3 * .2 - R3 * .03
                log(mb, axis(s - 1.05, half - .15) + UPV * -.03, q + UPV * 2.0 + D3 * .12 - R3 * .1, .1, .09, 6, 'wood_dark', rot=.4)
            log(mb, q + UPV * -.06, head, .17, .148, 8, 'wood_dark', rot=.3 * idx + .7 * (sg > 0))
        split_log(mb, axis(s, -half - .32) + UPV * cap, axis(s, half + .32) + UPV * cap, .19, .27, 5, 'wood_dark')
        for u, sg in ((-half + .3, 1), (half - .3, -1)):                # wedges over the cap ends, tight under the rock
            wedge(mb, axis(s, u) + UPV * (cap + .34), D3 * sg, .34, .13, .14, 'wood_board')
        sets.append(dict(s=round(s, 2), half=round(float(half), 2), cap=round(float(cap), 2))); geo.append((s, half, cap))
    for (sa, ha, ca), (sb, hb, cb) in zip(geo[:-1], geo[1:]):
        for u in np.linspace(-1.62, 1.62, 9):                           # roof lagging laid tight on the two caps
            beam(mb, axis(sa + .1, u) + UPV * (ca + .3), axis(sb - .1, u) + UPV * (cb + .3), .33, .045, 'wood_board')
        for sg in (-1, 1):                                              # side lagging behind the post tops
            for dh in (.35, .75, 1.15):
                beam(mb, axis(sa + .1, sg * (ha + .24)) + UPV * (ca - dh), axis(sb - .1, sg * (hb + .24)) + UPV * (cb - dh), .05, .22, 'wood_board')
    log(mb, axis(s_face + .15, -2.55) + UPV * .03, axis(s_face + .15, 2.55) + UPV * .03, .14, .13, 7, 'wood_dark', caps=(True, True), rot=.2)
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
    # talus fan: spalled granite at the foot of the face, fanning out on both sides of the approach (never on it)
    rng2 = np.random.default_rng(2976); talus = 0; tries = 0
    while talus < 38 and tries < 600:
        tries += 1; sg = 1 if rng2.random() < .5 else -1
        l = sg * (3.6 + rng2.gamma(1.5, 1.4)); s = float(face_line(l)) + .3 + rng2.exponential(1.25) + .6 * rng2.random() ** 3
        if abs(l) > 10.5: continue
        size = float(np.clip(rng2.lognormal(-1.3, .5), .1, .55)); x, z = lw_world(s, l); g = ground_y(float(x), float(z))
        if rock_sdf(np.array([s]), np.array([l]), np.array([g - FLOOR + .12 * size]))[0] < .05: continue   # inside the outcrop
        V = ico * size * np.array([1, .58, 1]) * (1 + .2 * rng2.standard_normal((len(ico), 1)))
        yaw = rng2.uniform(0, 2 * math.pi); cy, sy = math.cos(yaw), math.sin(yaw)
        V = np.stack([V[:, 0] * cy + V[:, 2] * sy, V[:, 1], -V[:, 0] * sy + V[:, 2] * cy], -1) + np.array([float(x), g - .3 * size, float(z)])
        b = mb._add(V, nz(V - V.mean(0)), V[:, [0, 2]] * .5)
        for f in _ICO_F: mb.T['rock_loose'] += [b + f[0], b + f[1], b + f[2]]
        talus += 1
    Vv = np.asarray(mb.V); t = mb.T['rock_loose']
    for k in range(0, len(t), 3):
        i, j, l = t[k:k + 3]
        if np.dot(np.cross(Vv[j] - Vv[i], Vv[l] - Vv[i]), np.asarray(mb.N[i])) < 0: t[k + 1], t[k + 2] = l, j
    # the collapsed old set: a broken post, a cap and a post end, as logs
    from cave297_dressing import log, split_log
    log(mb, axis(s_face + 2.2, -1.9) + UPV * .16, axis(s_face + 3.9, -.4) + UPV * .28, .16, .14, 8, 'wood_dark', caps=(True, True), rot=.3)
    split_log(mb, axis(s_face + 1.6, 1.2) + UPV * .0, axis(s_face + 2.9, 2.6) + UPV * -.02, .19, .26, 5, 'wood_dark')
    log(mb, axis(s_face + 3.0, .4) + UPV * .14, axis(s_face + 3.3, 2.1) + UPV * .3, .17, .15, 8, 'wood_dark', caps=(True, True), rot=1.1)
    return mb, talus


def plan(face_mb, path):
    from PIL import Image, ImageDraw
    sc = 14; o = axis(s_face); size = 44
    im = Image.new('RGB', (size * sc, size * sc), 'white'); d = ImageDraw.Draw(im)
    P = lambda x, z: ((x - o[0] + size / 2) * sc, (size / 2 - (z - o[2])) * sc)
    for (ix, iz) in HOLES:
        x0, z0 = ix * CELL, iz * CELL; d.polygon([P(x0, z0), P(x0 + CELL, z0), P(x0 + CELL, z0 + CELL), P(x0, z0 + CELL)], fill=(255, 210, 210), outline=(220, 120, 120))
    V = np.asarray(face_mb.V)
    for q in V[::3]: d.point(P(q[0], q[2]), fill=(60, 60, 60))
    for r in rings[::4]: d.line([P(q[0], q[2]) for q in r], fill=(90, 120, 200))
    d.line([P(*axis(s0)[[0, 2]]), P(*axis(s_face + 6)[[0, 2]])], fill=(0, 160, 0), width=2)
    d.text((6, 6), f'hole cells {len(HOLES)}  face s={s_face}  red=hole  grey=outcrop  blue=gallery', fill='black')
    im.save(path)


if __name__ == '__main__':
    origin = axis(s_face)
    g = gallery(); f, ostats = outcrop(); fl = floor_strip(); ap = apron(); tb, sets = timbers(); db, talus = debris()
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
    print('outcrop', json.dumps(ostats), 'talus', talus)
    print('meshes', meshes)
