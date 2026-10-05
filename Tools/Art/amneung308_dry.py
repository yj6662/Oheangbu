"""amneung308_dry.py - offline proof of the 금표 암릉 rock band + 국 stone (D308-16c), from the BUILD data.

Reads   Tools/Unity/Stage308_relayout_ext/Data/amneung308.json   (or --data <file>: the deployed copy
        Art/World/Compact/Rebuild/CliffBoundary308/amneung308.json)
        Art/World/Compact/Rebuild/CliffBoundary308/plan/Stage/height_p1b.bytes        (float32 [1501,1001], 4 m grid)
        Art/World/Compact/Rebuild/CliffBoundary308/plan/_work/lat308-*.npz            (inside / water / route masks)
        Art/World/Compact/Rebuild/Enclosure305/Stage305_4/a/segments305.json          (forest shell)
        Art/World/Compact/Rebuild/Rock275/granite-2-{near,far}.json                   (the visible rock mesh, unit box, pivot at the bottom)
        Oheangbu/Assets/_Project/Scenes/World/Main/WorldLayout_Main.asset             (routes: detour reference, return loop)
        the vegetation sheets named in the data
Writes  <out>/amneung308_dry.json + amneung308_dry.txt  (default out = Tools/Unity/Stage308_relayout_ext/Dry). Nothing else.

Its own reach model (written for this file; no code of the designer's proof/ or of review_1b is imported):
  surface   = max(height field triangle surface, pad tops, stone top, core tops) on a square grid; the visible rocks have NO collider
              and are not in the surface (they are only measured against the cores: core-fit)
  walker    = a point (no capsule inflation: it slips through more than the real controller, i.e. the test is against the band)
  walk      = 8 neighbours; down: always; up: natural ground <= 45 deg, or a step <= 0.30 m; never onto ground steeper than 45 deg uphill
  jump      = from standable ground to any cell within 2.0 m whose surface is <= 0.75 + 0.30 m higher, nothing higher in between
  guk       = from a standable cell to any cell within 2.0 m whose surface is <= 2.4 + 0.75 + 0.30 m higher (the conservative bound:
              lift, then a jump off the deck).  'guk_any' lets the lift stand ANYWHERE (even where FieldSpellService refuses the uneven
              ground); 'guk_rule' asks for the service's own flatness rule (four deck edges within 0.10 m of the base).
Every number printed carries [O] (computed here, offline) - the editor probe (Amneung308 verify / probe) is the final judge.
"""
import os, sys, io, re, json, math, heapq, hashlib, argparse
os.environ.setdefault('OMP_NUM_THREADS', '4'); os.environ.setdefault('OPENBLAS_NUM_THREADS', '4'); os.environ.setdefault('MKL_NUM_THREADS', '4')
import numpy as np

ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..')).replace('\\', '/')
CB = ROOT + '/Art/World/Compact/Rebuild/CliffBoundary308'
HEIGHT = CB + '/plan/Stage/height_p1b.bytes'
LAT = CB + '/plan/_work/lat308-576175e43abb-90f234b8da93.npz'
SHELL = ROOT + '/Art/World/Compact/Rebuild/Enclosure305/Stage305_4/a/segments305.json'
ROCKMESH = ROOT + '/Art/World/Compact/Rebuild/Rock275/granite-2-%s.json'
LAYOUT = ROOT + '/Oheangbu/Assets/_Project/Scenes/World/Main/WorldLayout_Main.asset'
DATA = ROOT + '/Tools/Unity/Stage308_relayout_ext/Data/amneung308.json'
OUT = ROOT + '/Tools/Unity/Stage308_relayout_ext/Dry'
NZ4, NX4 = 1501, 1001
STEP, JUMP, GUK, HOP_M, SLOPE_MAX = 0.30, 0.75, 2.4, 2.0, 45.0
RISE_JUMP = JUMP + STEP
RISE_GUK = GUK + JUMP + STEP
TAN = math.tan(math.radians(SLOPE_MAX))
DECK = (0.42, 0.35)          # FieldSpellService.DefaultDeckHalfSize
DECK_TOL = 0.10              # "발판 아래 지면이 고르지 않거나 가장자리" limit


def sha(path):
    h = hashlib.sha256()
    with open(path, 'rb') as f:
        for b in iter(lambda: f.read(1 << 20), b''): h.update(b)
    return h.hexdigest()


def load_height(path=HEIGHT):
    a = np.fromfile(path, '<f4')
    if a.size != NZ4 * NX4: raise SystemExit('height field has the wrong size: ' + path)
    return a.reshape(NZ4, NX4)


def surface(h4, X, Z):
    """height and slope (deg) of the tile triangle surface (00,10,01)/(10,01,11)"""
    fx = np.clip(np.asarray(X, float) / 4.0, 0, NX4 - 1 - 1e-6); fz = np.clip(np.asarray(Z, float) / 4.0, 0, NZ4 - 1 - 1e-6)
    j = np.floor(fx).astype(int); i = np.floor(fz).astype(int); u = fx - j; v = fz - i
    a, b, c, d = h4[i, j], h4[i, j + 1], h4[i + 1, j], h4[i + 1, j + 1]
    lower = (u + v) <= 1.0
    y = np.where(lower, a + u * (b - a) + v * (c - a), d + (1 - u) * (c - d) + (1 - v) * (b - d))
    gx = np.where(lower, b - a, d - c) / 4.0; gz = np.where(lower, c - a, d - b) / 4.0
    return y, np.degrees(np.arctan(np.hypot(gx, gz)))


def box_uv(X, Z, cx, cz, yaw_deg):
    """coordinates of world points in a box frame: Unity yaw about +y, local +x -> (cos, -sin), local +z -> (sin, cos)"""
    a = math.radians(yaw_deg); ca, sa = math.cos(a), math.sin(a)
    dx = X - cx; dz = Z - cz
    return dx * ca - dz * sa, dx * sa + dz * ca


def in_box(X, Z, cx, cz, yaw_deg, len_x, len_z, grow=0.0):
    u, v = box_uv(X, Z, cx, cz, yaw_deg)
    return (np.abs(u) <= len_x * 0.5 + grow) & (np.abs(v) <= len_z * 0.5 + grow)


def near_box(X, Z, cx, cz, yaw_deg, len_x, len_z, r):
    """points within r (Euclid) of the box footprint"""
    u, v = box_uv(X, Z, cx, cz, yaw_deg)
    return np.hypot(np.maximum(np.abs(u) - len_x * 0.5, 0), np.maximum(np.abs(v) - len_z * 0.5, 0)) <= r


def joint(a, b, step=0.05):
    """overlap of two footprint boxes (cx, cz, yaw, len_x, len_z): its extent along and across box a (0, 0 = they do not touch)"""
    us = np.arange(-a[3] * 0.5, a[3] * 0.5 + 1e-9, step); vs = np.arange(-a[4] * 0.5, a[4] * 0.5 + 1e-9, step)
    U, V = np.meshgrid(us, vs); t = math.radians(a[2]); ca, sa = math.cos(t), math.sin(t)
    X = a[0] + U * ca + V * sa; Z = a[1] - U * sa + V * ca
    m = in_box(X, Z, b[0], b[1], b[2], b[3], b[4])
    if not m.any(): return 0.0, 0.0
    return float(U[m].max() - U[m].min() + step), float(V[m].max() - V[m].min() + step)


def band_joints(D):
    """the chain of footprints along the band (cores in order, the stone between the two cores that enter it) and every joint's overlap"""
    st = D['stone']; sb = (st['x'], st['z'], st['yaw_box'], st['half_m'] * 2, st['half_m'] * 2)
    cs = [(c['id'], (c['x'], c['z'], c['yaw'], c['size'][0], c['size'][2])) for c in D['cores']]
    dist = [math.hypot(b[0] - st['x'], b[1] - st['z']) for _, b in cs]
    k = int(np.argmin([dist[i] + dist[i + 1] for i in range(len(cs) - 1)]))      # the stone sits between cores k and k + 1
    chain = cs[:k + 1] + [('stone', sb)] + cs[k + 1:]
    return [dict(a=chain[i][0], b=chain[i + 1][0], along_m=round(joint(chain[i][1], chain[i + 1][1])[0], 2), across_m=round(joint(chain[i][1], chain[i + 1][1])[1], 2)) for i in range(len(chain) - 1)]


class Grid:
    def __init__(s, h4, x0, x1, z0, z1, cell):
        s.x0, s.z0, s.c = x0, z0, cell
        s.nx = int(round((x1 - x0) / cell)); s.nz = int(round((z1 - z0) / cell))
        s.xs = x0 + (np.arange(s.nx) + 0.5) * cell; s.zs = z0 + (np.arange(s.nz) + 0.5) * cell
        s.X, s.Z = np.meshgrid(s.xs, s.zs)
        s.ground, s.slope = surface(h4, s.X, s.Z)
        s.ground = s.ground.astype(np.float64)

    def cell(s, x, z):
        return min(s.nz - 1, max(0, int((z - s.z0) / s.c))), min(s.nx - 1, max(0, int((x - s.x0) / s.c)))


def build(g, D, with_band=True, grow=0.0):
    """composite surface H, masks: core / stone / pad (footprints), built (flat man-made or core), stand (a body can stand and push off)"""
    H = g.ground.copy()
    core = np.zeros(H.shape, bool); stone = np.zeros(H.shape, bool); pad = np.zeros(H.shape, bool)
    if with_band:
        st = D['stone']
        for p in st['pads']:
            m = in_box(g.X, g.Z, p['x'], p['z'], p['yaw'], p['size'][0], p['size'][2], grow)
            raise_ = m & (H < p['top_y']); H[raise_] = p['top_y']; pad |= raise_
        m = in_box(g.X, g.Z, st['x'], st['z'], st['yaw_box'], st['half_m'] * 2, st['half_m'] * 2, grow)
        H[m] = np.maximum(H[m], st['top_y']); stone |= m
        for c in D['cores']:
            m = in_box(g.X, g.Z, c['x'], c['z'], c['yaw'], c['size'][0], c['size'][2], grow)
            H[m] = np.maximum(H[m], c['top_y']); core |= m
        stone &= ~core; pad &= ~core & ~stone
    built = core | stone | pad
    stand = built | (g.slope <= SLOPE_MAX)
    return H, core, stone, pad, built, stand


def flood(g, H, stand, built, seeds, blocked=None, jump=True, guk=None, castable=None):
    """seeds: bool mask. guk: None | rise. castable: bool mask of cells a lift may be raised from (None = every standable cell)."""
    nz, nx = H.shape; c = g.c
    bl = np.zeros(H.shape, bool) if blocked is None else blocked
    reach = seeds & ~bl
    R = int(HOP_M / c + 1e-9)
    hops = [(dz, dx) for dz in range(-R, R + 1) for dx in range(-R, R + 1) if max(abs(dz), abs(dx)) > 1 and c * math.hypot(dz, dx) <= HOP_M + 1e-9]
    natural = ~built
    front = reach.copy()
    while front.any():
        new = np.zeros_like(reach)
        fi, fj = np.nonzero(front)
        for dz in (-1, 0, 1):
            for dx in (-1, 0, 1):
                if not (dz or dx): continue
                ti, tj = fi + dz, fj + dx
                ok = (ti >= 0) & (ti < nz) & (tj >= 0) & (tj < nx)
                si, sj, ti, tj = fi[ok], fj[ok], ti[ok], tj[ok]
                ok = ~bl[ti, tj] & ~reach[ti, tj]
                if dz and dx: ok &= ~bl[si + dz, sj] & ~bl[si, sj + dx]          # no squeezing between two blocked corners
                si, sj, ti, tj = si[ok], sj[ok], ti[ok], tj[ok]
                dh = H[ti, tj] - H[si, sj]; run = c * math.hypot(dz, dx)
                down = dh <= 0
                step = stand[si, sj] & stand[ti, tj] & (dh <= STEP)
                slope = stand[si, sj] & stand[ti, tj] & natural[si, sj] & natural[ti, tj] & (dh <= run * TAN + 0.02)
                can = down | step | slope
                new[ti[can], tj[can]] = True
        m = stand[fi, fj]; hi, hj = fi[m], fj[m]
        if jump or guk:
            if guk:
                cm = np.ones(hi.shape, bool) if castable is None else castable[hi, hj]
            for dz, dx in hops:
                ti, tj = hi + dz, hj + dx
                ok = (ti >= 0) & (ti < nz) & (tj >= 0) & (tj < nx)
                si, sj, ti, tj = hi[ok], hj[ok], ti[ok], tj[ok]
                d = H[ti, tj] - H[si, sj]
                free = ~bl[ti, tj] & ~reach[ti, tj] & (d > 0) & stand[ti, tj]
                if jump:
                    can = free & (d <= RISE_JUMP)
                    n = max(abs(dz), abs(dx))
                    for k in range(1, n):
                        mi = si + int(round(dz * k / n)); mj = sj + int(round(dx * k / n))
                        can &= ~bl[mi, mj] & (H[mi, mj] - H[si, sj] <= RISE_JUMP)
                    new[ti[can], tj[can]] = True
                if guk:
                    can = free & (d <= guk) & cm[ok]
                    new[ti[can], tj[can]] = True
        new &= ~reach; reach |= new; front = new
    return reach


def castable_rule(g, H):
    """FieldSpellService.TryPrepare: the four deck edge points within 0.10 m of the base point (world axes)"""
    ok = np.ones(H.shape, bool)
    for off_x, off_z in ((DECK[0], 0), (-DECK[0], 0), (0, DECK[1]), (0, -DECK[1])):
        dj = int(round(off_x / g.c)); di = int(round(off_z / g.c))
        sh = np.full(H.shape, np.inf)
        src = H[max(0, di):H.shape[0] + min(0, di), max(0, dj):H.shape[1] + min(0, dj)]
        sh[max(0, -di):H.shape[0] + min(0, -di), max(0, -dj):H.shape[1] + min(0, -dj)] = src
        ok &= np.abs(sh - H) <= DECK_TOL
    return ok


# ---- review 2: a running jump off a 국 deck (ballistic), and how deep the eye goes into the visible rock ----
RUN_SPEED, GRAVITY, EYE_M = 5.5, 20.0, 1.6        # walker run speed (m/s), gravity magnitude, first-person eye over the feet
V_JUMP = math.sqrt(2.0 * JUMP * GRAVITY)
BALLISTIC_MAX_M = 6.5


def ballistic(g, H, src, target):
    """For every `target` cell: the highest surface of a `src` cell from which a body standing on a 국 deck (surface + GUK) lands on
    it with a running jump (RUN_SPEED, jump apex JUMP, no obstacle between: conservative). -inf = not reachable.
    A landing up to STEP under the target still counts. At horizontal distance d the feet are at most deck + JUMP while d <= RUN_SPEED * t_up, else deck + V t - g t^2 / 2 (t = d / RUN_SPEED)."""
    best = np.full(H.shape, -np.inf); c = g.c; R = int(BALLISTIC_MAX_M / c); t_up = V_JUMP / GRAVITY
    Hs = np.where(src, H, -np.inf); nz, nx = H.shape
    for di in range(-R, R + 1):
        for dj in range(-R, R + 1):
            d = c * math.hypot(di, dj)
            if d == 0 or d > BALLISTIC_MAX_M: continue
            t = d / RUN_SPEED; rise = GUK + (JUMP if t <= t_up else V_JUMP * t - 0.5 * GRAVITY * t * t)
            # source at (i - di, j - dj) -> target at (i, j)
            sh = np.full(H.shape, -np.inf)
            sh[max(0, di):nz + min(0, di), max(0, dj):nx + min(0, dj)] = Hs[max(0, -di):nz + min(0, -di), max(0, -dj):nx + min(0, -dj)]
            ok = target & (sh + rise + STEP >= H - 1e-6)      # + STEP: a landing a step under the edge still gets up
            if ok.any(): best[ok] = np.maximum(best[ok], sh[ok])
    return best


def eye_depth(g, D, top):
    """Per long core face: walking at the face (0.3 m capsule radius outside it), how far outside the collider the first-person eye
    (ground + EYE_M) is still UNDER the visible rock top = how deep the eye is inside the rock silhouette when the body is stopped.
    Sampled every 0.5 m along the face, marching outward 0.2 m at a time (3.4 m at most)."""
    rows = []
    for c in D['cores']:
        a = math.radians(c['yaw']); ux, uz = math.cos(a), -math.sin(a); vx, vz = math.sin(a), math.cos(a)
        for side in (-1, 1):
            ds = []
            for t in np.arange(-c['size'][0] * 0.5 + 0.25, c['size'][0] * 0.5, 0.5):
                depth = 3.4
                for k in range(0, 18):
                    off = 0.2 * k; px = c['x'] + ux * t + vx * side * (c['size'][2] * 0.5 + off); pz = c['z'] + uz * t + vz * side * (c['size'][2] * 0.5 + off)
                    i, j = g.cell(px, pz)
                    if not (top[i, j] > g.ground[i, j] + EYE_M): depth = off; break
                ds.append(depth)
            rows.append(dict(id=c['id'], side=side, mean_m=round(float(np.mean(ds)), 2), max_m=round(float(np.max(ds)), 2)))
    return rows


def routes():
    T = open(LAYOUT, encoding='utf-8').read().split('\n')
    i0 = next(k for k, l in enumerate(T) if l.startswith('  Routes:'))
    out = {}; cur = None
    for l in T[i0 + 1:]:
        if l.startswith('  - Id:'):
            cur = []; out[l.split(':', 1)[1].strip()] = cur; continue
        if not l.startswith('    ') and not l.startswith('  - '): break
        m = re.match(r'\s+- \{x: ([-\d.e]+), y: ([-\d.e]+)\}', l)
        if m and cur is not None: cur.append((float(m.group(1)), float(m.group(2))))
    return {k: np.asarray(v, float) for k, v in out.items() if len(v) >= 2}


def place_xz(name):
    T = open(LAYOUT, encoding='utf-8').read()
    m = re.search(r'- Id: ' + re.escape(name) + r'\n(?:\s+\w+:.*\n)*?\s+Position: \{x: ([-\d.e]+), y: ([-\d.e]+)\}', T)
    return (float(m.group(1)), float(m.group(2))) if m else None


def resample(P, step=1.0):
    P = np.asarray(P, float); seg = np.hypot(*(P[1:] - P[:-1]).T); cum = np.concatenate([[0], np.cumsum(seg)])
    s = np.arange(0, cum[-1] + 1e-9, step)
    return np.stack([np.interp(s, cum, P[:, 0]), np.interp(s, cum, P[:, 1])], 1)


def length(P):
    P = np.asarray(P, float); return float(np.hypot(*(P[1:] - P[:-1]).T).sum())


def rock_mesh(lod):
    d = json.load(open(ROCKMESH % lod))
    return np.array([[v['x'], v['y'], v['z']] for v in d['vertices']], float), np.array(d['triangles']).reshape(-1, 3)


def rock_top(g, rocks, lod='far'):
    """highest point of the visible rocks over every grid cell (-inf where there is no rock)"""
    V, T = rock_mesh(lod); top = np.full(g.ground.shape, -np.inf)
    for r in rocks:
        a = math.radians(r['yaw']); ca, sa = math.cos(a), math.sin(a); sx, sy, sz = r['scale']
        lx, lz = V[:, 0] * sx, V[:, 2] * sz
        X = r['x'] + lx * ca + lz * sa; Z = r['z'] - lx * sa + lz * ca; Y = r['y'] + V[:, 1] * sy
        for t in T:
            x, z, y = X[t], Z[t], Y[t]
            j0 = max(0, int((x.min() - g.x0) / g.c)); j1 = min(g.nx, int((x.max() - g.x0) / g.c) + 1)
            i0 = max(0, int((z.min() - g.z0) / g.c)); i1 = min(g.nz, int((z.max() - g.z0) / g.c) + 1)
            if j1 <= j0 or i1 <= i0: continue
            den = (z[1] - z[2]) * (x[0] - x[2]) + (x[2] - x[1]) * (z[0] - z[2])
            if abs(den) < 1e-12: continue
            GX, GZ = g.X[i0:i1, j0:j1], g.Z[i0:i1, j0:j1]
            l1 = ((z[1] - z[2]) * (GX - x[2]) + (x[2] - x[1]) * (GZ - z[2])) / den
            l2 = ((z[2] - z[0]) * (GX - x[2]) + (x[0] - x[2]) * (GZ - z[2])) / den
            l3 = 1 - l1 - l2
            m = (l1 >= -1e-6) & (l2 >= -1e-6) & (l3 >= -1e-6)
            yy = l1 * y[0] + l2 * y[1] + l3 * y[2]
            sub = top[i0:i1, j0:j1]; sub[m] = np.maximum(sub[m], yy[m])
    return top


def core_fit(g, D, top=None):
    """per core cell: is the invisible box hidden by the visible rock?
       low  = cells where the rock is lower than the core top AND lower than ground + fit.low_rock_m (an invisible wall over a low rock)
       bare = points of the two long core faces, 1 m above the ground, with no rock that high over them (the wall stands outside the rock)"""
    top = rock_top(g, D['rocks'], 'far') if top is None else top
    fit = D['checks']
    rows = []
    for c in D['cores']:
        m = in_box(g.X, g.Z, c['x'], c['z'], c['yaw'], c['size'][0], c['size'][2])
        cells = int(m.sum())
        under = m & np.isfinite(top)
        low = m & (top < c['top_y'] - 1e-3) & (top < g.ground + fit['fit_low_rock_m'])
        a = math.radians(c['yaw']); ux, uz = math.cos(a), -math.sin(a); vx, vz = math.sin(a), math.cos(a)
        bare = 0; n = 0; worst = 0.0
        for side in (-1, 1):
            for t in np.arange(-c['size'][0] * 0.5, c['size'][0] * 0.5 + 1e-6, 0.25):
                px = c['x'] + ux * t + vx * side * c['size'][2] * 0.5; pz = c['z'] + uz * t + vz * side * c['size'][2] * 0.5
                i, j = g.cell(px, pz); n += 1
                if not (top[i, j] >= g.ground[i, j] + fit['fit_face_h_m']):
                    bare += 1
                    # how far the face stands outside the rock: walk inward until the rock is high enough
                    for k in range(1, 12):
                        qx = px - vx * side * 0.25 * k; qz = pz - vz * side * 0.25 * k; ii, jj = g.cell(qx, qz)
                        if top[ii, jj] >= g.ground[ii, jj] + fit['fit_face_h_m']: worst = max(worst, 0.25 * k); break
                    else: worst = max(worst, 3.0)
        rows.append(dict(id=c['id'], cells=cells, under_rock_pct=round(100.0 * under.sum() / max(1, cells), 1), low_cells=int(low.sum()),
                         face_points=n, bare_face_points=bare, worst_outside_m=round(worst, 2)))
    return rows, top


def sheet_rows(path):
    """FixedPlacements of a dressing sheet asset: list of (id, proto, x, y, z)"""
    out = []; cur = None
    rx = re.compile(r'\s+Position: \{x: ([-\d.e]+), y: ([-\d.e]+), z: ([-\d.e]+)\}')
    with open(path, encoding='utf-8') as f:
        inside = False
        for l in f:
            if l.startswith('  FixedPlacements:'): inside = True; continue
            if inside and l.startswith('  ') and not l.startswith('   ') and not l.startswith('  - '): inside = False
            if not inside: continue
            if l.startswith('  - Id:'): cur = [l.split(':', 1)[1].strip(), '', None]; out.append(cur); continue
            if cur is None: continue
            if l.startswith('    PrototypeId:'): cur[1] = l.split(':', 1)[1].strip()
            else:
                m = rx.match(l)
                if m and cur[2] is None: cur[2] = (float(m.group(1)), float(m.group(2)), float(m.group(3)))
    return [(a, b, c) for a, b, c in out if c is not None]


def main():
    ap = argparse.ArgumentParser(); ap.add_argument('--data', default=DATA); ap.add_argument('--out', default=OUT); ap.add_argument('--skip-ea', action='store_true')
    args = ap.parse_args()
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')
    D = json.load(open(args.data, encoding='utf-8'))
    h4 = load_height(); ck = D['checks']; st = D['stone']
    R = dict(data=args.data.replace('\\', '/'), data_sha256=sha(args.data), height_sha256=sha(HEIGHT), model=dict(step=STEP, jump=JUMP, guk=GUK, hop_m=HOP_M, slope_max_deg=SLOPE_MAX, rise_jump=RISE_JUMP, rise_guk=RISE_GUK))
    fails = []; L = []

    def say(s): L.append(s); print(s)

    def check(ok, text):
        say(('PASS ' if ok else 'FAIL ') + text)
        if not ok: fails.append(text)

    say('amneung308_dry  data ' + R['data'] + ' sha ' + R['data_sha256'][:12] + '  height sha ' + R['height_sha256'][:12])
    # ---------------- local world, 0.25 m ----------------
    xs = [c['x'] for c in D['cores']]; zs = [c['z'] for c in D['cores']]
    g = Grid(h4, math.floor(min(xs)) - 26, math.ceil(max(xs)) + 26, math.floor(min(zs)) - 30, math.ceil(max(zs)) + 30, 0.25)
    H, core, stone, pad, built, stand = build(g, D)
    natural_seed = ~built & (g.slope <= SLOPE_MAX)
    say('-- A. reach, local window x %.0f..%.0f z %.0f..%.0f at 0.25 m; seeds = every natural standable cell on both sides (ends open) [O]' % (g.x0, g.x0 + g.nx * g.c, g.z0, g.z0 + g.nz * g.c))
    a2 = g.c * g.c
    wj = flood(g, H, stand, built, natural_seed, jump=True)
    R['walk_jump'] = dict(core_top_m2=round(float((wj & core).sum() * a2), 2), stone_top_m2=round(float((wj & stone).sum() * a2), 2), pad_m2=round(float((wj & pad).sum() * a2), 2),
                          pad_total_m2=round(float(pad.sum() * a2), 2))
    check((wj & core).sum() == 0, 'A1 walk + jump: core tops reached %.2f m2 (must be 0)' % R['walk_jump']['core_top_m2'])
    check((wj & stone).sum() == 0, 'A2 walk + jump: stone top reached %.2f m2 (must be 0: the stone needs 국)' % R['walk_jump']['stone_top_m2'])
    check((wj & pad).sum() > 0, 'A3 walk + jump: lift pads reached %.2f of %.2f m2 (must be > 0: the pads are walked onto)' % (R['walk_jump']['pad_m2'], R['walk_jump']['pad_total_m2']))
    cast = castable_rule(g, H)
    gr = flood(g, H, stand, built, natural_seed, jump=True, guk=RISE_GUK, castable=cast)
    ga = flood(g, H, stand, built, natural_seed, jump=True, guk=RISE_GUK, castable=None)
    gs = flood(g, H, stand, built, natural_seed, jump=True, guk=GUK + STEP, castable=cast)
    R['guk'] = dict(rule=dict(core_top_m2=round(float((gr & core).sum() * a2), 2), stone_top_m2=round(float((gr & stone).sum() * a2), 2)),
                    any_ground=dict(core_top_m2=round(float((ga & core).sum() * a2), 2), stone_top_m2=round(float((ga & stone).sum() * a2), 2)),
                    lift_and_step_only=dict(core_top_m2=round(float((gs & core).sum() * a2), 2), stone_top_m2=round(float((gs & stone).sum() * a2), 2)),
                    stone_total_m2=round(float(stone.sum() * a2), 2), castable_natural_m2=round(float((cast & ~built).sum() * a2), 1), castable_pad_m2=round(float((cast & pad).sum() * a2), 2))
    check((gr & core).sum() == 0, 'A4 국 (service flatness rule, lift 2.4 + jump): core tops reached %.2f m2 (must be 0)' % R['guk']['rule']['core_top_m2'])
    check((ga & core).sum() == 0, 'A5 국 raised from ANY standable cell (stronger than the game allows): core tops reached %.2f m2 (must be 0)' % R['guk']['any_ground']['core_top_m2'])
    check((gs & stone).sum() > 0, 'A6 국 from the pads, lift 2.4 + a 0.30 step only (no jump): stone top reached %.2f of %.2f m2 (must be > 0)' % (R['guk']['lift_and_step_only']['stone_top_m2'], R['guk']['stone_total_m2']))
    # both directions: with the two band ends capped, 국 must carry a walker from each side to the other
    cap = np.zeros(H.shape, bool)
    line = np.asarray(D['line'], float)
    for end, nxt in ((line[0], line[1]), (line[-1], line[-2])):
        d = (end - nxt) / np.hypot(*(end - nxt))
        for t in np.arange(0, 60, 0.1):
            p = end + d * t; i, j = g.cell(p[0], p[1])
            cap[max(0, i - 2):i + 3, max(0, j - 2):j + 3] = True
    cap &= ~built
    cx, cz = st['x'], st['z']; nrm = np.array([math.sin(math.radians(st['yaw_box'])), math.cos(math.radians(st['yaw_box']))])   # local +z of the stone box
    north = (cx + nrm[0] * 12, cz + nrm[1] * 12); south = (cx - nrm[0] * 12, cz - nrm[1] * 12)
    R['directions'] = {}
    for name, a, b in (('N_to_S', north, south), ('S_to_N', south, north)):
        seed = np.zeros(H.shape, bool); seed[g.cell(*a)] = True; tgt = g.cell(*b)
        w0 = flood(g, H, stand, built, seed, blocked=cap, jump=True)
        w1 = flood(g, H, stand, built, seed, blocked=cap, jump=True, guk=RISE_GUK, castable=cast)
        op = flood(g, H, stand, built, seed, jump=True)
        R['directions'][name] = dict(ends_capped_walk_jump_crosses=bool(w0[tgt]), ends_capped_guk_crosses=bool(w1[tgt]), ends_open_walk_jump_crosses=bool(op[tgt]),
                                     core_top_m2=round(float((w1 & core).sum() * a2), 2))
        check(not w0[tgt], 'A7 %s, band ends capped, walk + jump: does not cross' % name)
        check(bool(w1[tgt]), 'A8 %s, band ends capped, with 국: crosses (over the stone)' % name)
        check(bool(op[tgt]), 'A9 %s, band ends open, walk + jump: crosses (the band is a landmark, not a wall - D308-16c)' % name)
    # ---------------- geometry of the data ----------------
    say('-- B. data geometry [O]')
    rise = st['top_y'] - st['pads'][0]['top_y']
    check(ck['rise_m'][0] <= rise <= ck['rise_m'][1], 'B1 lift rise = stone top %.2f - pad top %.2f = %.2f m in [%.1f, %.1f]' % (st['top_y'], st['pads'][0]['top_y'], rise, ck['rise_m'][0], ck['rise_m'][1]))
    # no gap in the core: every joint of the chain core .. core, stone, core .. core overlaps by joint_min_m along and across
    J = band_joints(D); R['core_joints'] = J
    worst = min(J, key=lambda j: min(j['along_m'], j['across_m']))
    check(min(worst['along_m'], worst['across_m']) >= ck['joint_min_m'], 'B2 core chain (%d boxes + the stone, %.1f m line): every joint overlaps; the thinnest joint %s | %s = %.2f m along x %.2f m across (need %.2f)' %
          (len(D['cores']), length(line), worst['a'], worst['b'], worst['along_m'], worst['across_m'], ck['joint_min_m']))
    # core tops over the natural ground and over the stone / pads (rule v2 of the design)
    worst_nat = 1e9; worst_plat = 1e9; rows = []
    for c in D['cores']:
        m = near_box(g.X, g.Z, c['x'], c['z'], c['yaw'], c['size'][0], c['size'][2], ck['core_ground_ring_m']) & ~stone & ~pad
        gmax = float(g.ground[m].max()); over = c['top_y'] - gmax; worst_nat = min(worst_nat, over)
        near_stone = near_box(g.X, g.Z, c['x'], c['z'], c['yaw'], c['size'][0], c['size'][2], ck['core_ground_ring_m']) & (stone | pad)
        op = c['top_y'] - st['top_y'] if (near_stone & stone).any() else (c['top_y'] - st['pads'][0]['top_y'] if near_stone.any() else None)
        if op is not None: worst_plat = min(worst_plat, op)
        rows.append(dict(id=c['id'], top_y=c['top_y'], ground_max_ring=round(gmax, 2), over_ground_m=round(over, 2), over_stone_or_pad_m=None if op is None else round(op, 2),
                         bottom_below_ground_min_m=round(float(g.ground[in_box(g.X, g.Z, c['x'], c['z'], c['yaw'], c['size'][0], c['size'][2])].min()) - (c['y'] - c['size'][1] * 0.5), 2)))
    R['cores'] = rows
    check(worst_nat >= RISE_GUK + ck['core_margin_m'] - 1e-3, 'B3 core top over the highest natural ground within %.0f m of its footprint: min %.2f m (need %.2f = 국 2.4 + jump 0.75 + step 0.30 + margin %.2f)' % (ck['core_ground_ring_m'], worst_nat, RISE_GUK + ck['core_margin_m'], ck['core_margin_m']))
    check(worst_plat >= RISE_GUK + ck['core_margin_m'] - 1e-3, 'B4 core top over the stone / pad it stands next to: min %.2f m (need %.2f)' % (worst_plat, RISE_GUK + ck['core_margin_m']))
    check(min(r['bottom_below_ground_min_m'] for r in rows) >= ck['core_bury_min_m'], 'B5 core bottoms under the lowest ground of their footprint: min %.2f m (need %.2f)' % (min(r['bottom_below_ground_min_m'] for r in rows), ck['core_bury_min_m']))
    # rock bottoms buried
    rb = []
    for r in D['rocks']:
        m = in_box(g.X, g.Z, r['x'], r['z'], r['yaw'], r['scale'][0], r['scale'][2])
        rb.append(dict(id=r['id'], bottom_y=r['y'], ground_min=round(float(g.ground[m].min()), 2), buried_min_m=round(float(g.ground[m].min()) - r['y'], 2)))
    R['rocks'] = rb
    check(min(x['buried_min_m'] for x in rb) >= ck['rock_bury_min_m'], 'B6 rock bottoms under the lowest ground of their footprint box: min %.2f m at %s (need %.2f)' % (min(x['buried_min_m'] for x in rb), min(rb, key=lambda x: x['buried_min_m'])['id'], ck['rock_bury_min_m']))
    # pad geometry on the ground
    for p in st['pads']:
        m = in_box(g.X, g.Z, p['x'], p['z'], p['yaw'], p['size'][0], p['size'][2])
        lo, hi = float(g.ground[m].min()), float(g.ground[m].max())
        R.setdefault('pads', []).append(dict(id=p['id'], top_y=p['top_y'], ground=[round(lo, 2), round(hi, 2)], top_over_ground_max_m=round(p['top_y'] - lo, 2), top_over_ground_min_m=round(p['top_y'] - hi, 2), bottom_y=round(p['top_y'] - p['size'][1], 2)))
        check(hi - p['top_y'] <= ck['pad_ground_over_top_max_m'] and p['top_y'] - p['size'][1] < lo, 'B7 pad %s: top %.2f, ground under it %.2f..%.2f (ground over the top at most %.2f m; bottom %.2f under the lowest ground)' % (p['id'], p['top_y'], lo, hi, ck['pad_ground_over_top_max_m'], p['top_y'] - p['size'][1]))
    m = in_box(g.X, g.Z, st['x'], st['z'], st['yaw_box'], st['half_m'] * 2, st['half_m'] * 2)
    R['stone'] = dict(top_y=st['top_y'], ground=[round(float(g.ground[m].min()), 2), round(float(g.ground[m].max()), 2)], top_over_ground_min_m=round(st['top_y'] - float(g.ground[m].max()), 2),
                      crest_ground_y=round(float(surface(h4, np.array([st['x']]), np.array([st['z']]))[0][0]), 2))
    check(R['stone']['top_over_ground_min_m'] > RISE_JUMP, 'B8 stone top over the highest ground under it: %.2f m (> jump reach %.2f)' % (R['stone']['top_over_ground_min_m'], RISE_JUMP))
    # B9 (review 2): not a fixed 2 m hop - a running jump off a 국 deck raised on ANY standable, castable surface (stone, pads, ground)
    src = cast & stand & ~core
    bl = ballistic(g, H, src, core)
    hit = core & np.isfinite(bl); rows9 = []
    for c in D['cores']:
        m = in_box(g.X, g.Z, c['x'], c['z'], c['yaw'], c['size'][0], c['size'][2]) & hit
        if m.any():
            i, j = np.unravel_index(np.argmax(np.where(m, bl, -np.inf)), bl.shape)
            rows9.append(dict(id=c['id'], top_y=c['top_y'], cells=int(m.sum()), source_surface_y=round(float(bl[i, j]), 2), need_top_y=round(float(bl[i, j]) + RISE_GUK + ck['core_margin_m'], 2)))
    R['ballistic'] = dict(run_speed=RUN_SPEED, gravity=GRAVITY, max_m=BALLISTIC_MAX_M, reachable_core_m2=round(float(hit.sum() * a2), 2), cores=rows9)
    check(not hit.any(), 'B9 running jump (%.1f m/s) off a 국 deck on the stone / a pad / any castable ground: core top reached %.2f m2 (must be 0)%s' %
          (RUN_SPEED, hit.sum() * a2, ''.join('; %s top %.2f from a surface at %.2f' % (r['id'], r['top_y'], r['source_surface_y']) for r in rows9)))
    # the 국 places of the realm against the LDB:55 cap: this band adds one, the spare place stays free
    check(ck['gates_expected'] <= ck['gates_cap'] - ck['gates_reserved'], 'B10 국 places after the build %d <= cap %d - reserved %d (the spare place before the sanctuary stays empty)' % (ck['gates_expected'], ck['gates_cap'], ck['gates_reserved']))
    # ---------------- core-fit against the visible rock ----------------
    say('-- C. core-fit: the invisible cores against the visible rock (LOD1 mesh raster, 0.25 m) [O]')
    fit, top = core_fit(g, D)
    R['core_fit'] = fit
    ed = eye_depth(g, D, top); R['eye_depth'] = dict(eye_m=EYE_M, faces=ed, mean_m=round(float(np.mean([e['mean_m'] for e in ed])), 2), max_m=max(e['max_m'] for e in ed),
                                                     faces_over_half_m=sum(1 for e in ed if e['mean_m'] > 0.5), limit_mean_m=ck.get('eye_depth_mean_max_m'))
    if ck.get('eye_depth_mean_max_m') is not None:
        check(R['eye_depth']['mean_m'] <= ck['eye_depth_mean_max_m'], 'C0 eye (ground + %.1f m) under the visible rock outside the collider face: mean %.2f m over %d faces (max %.2f; faces over 0.5 m: %d) <= %.2f' %
              (EYE_M, R['eye_depth']['mean_m'], len(ed), R['eye_depth']['max_m'], R['eye_depth']['faces_over_half_m'], ck['eye_depth_mean_max_m']))
    else: say('INFO C0 eye depth inside the rock outside the collider face: mean %.2f m, max %.2f m (no limit in the data)' % (R['eye_depth']['mean_m'], R['eye_depth']['max_m']))
    low = sum(f['low_cells'] for f in fit); bare = sum(f['bare_face_points'] for f in fit); pts = sum(f['face_points'] for f in fit)
    R['core_fit_total'] = dict(low_cells=low, bare_face_points=bare, face_points=pts, under_rock_pct_min=min(f['under_rock_pct'] for f in fit), worst_outside_m=max(f['worst_outside_m'] for f in fit))
    check(low <= ck['fit_low_cells_max'], 'C1 core cells where the rock is lower than the core AND lower than ground + %.2f m: %d (limit %d)' % (ck['fit_low_rock_m'], low, ck['fit_low_cells_max']))
    check(bare <= ck['fit_bare_points_max'], 'C2 core face points (%.1f m above the ground) with no rock over them: %d of %d (limit %d; worst %.2f m outside the rock)' % (ck['fit_face_h_m'], bare, pts, ck['fit_bare_points_max'], R['core_fit_total']['worst_outside_m']))
    crest = top[core & np.isfinite(top)] - g.ground[core & np.isfinite(top)]
    R['rock_crest_over_ground_m'] = dict(min=round(float(crest.min()), 2), median=round(float(np.median(crest)), 2), max=round(float(crest.max()), 2))
    V, T = rock_mesh('near'); R['triangles'] = dict(lod0=int(len(T) * len(D['rocks'])), lod1=int(len(rock_mesh('far')[1]) * len(D['rocks'])))
    say('     rock crest over the ground on the core cells: min %.2f / median %.2f / max %.2f m; triangles LOD0 %d, LOD1 %d' % (crest.min(), np.median(crest), crest.max(), R['triangles']['lod0'], R['triangles']['lod1']))
    # ---------------- vegetation ----------------
    say('-- D. vegetation rows to clear [O]')
    veg = D['vegetation']; tol = veg['tolerance_m']; R['vegetation'] = dict(sheets=[])
    for sp in veg['sheets']:
        ap_ = ROOT + '/Oheangbu/' + sp['path']
        prot = any(t in sp['path'] for t in ('/Watershed295', '/Reworld292', '/MountainTrail285')) or sp['path'].endswith('/03_Content.asset')
        rows_ = sheet_rows(ap_); found = 0; missing = []
        for v in veg['rows']:
            hit = [r for r in rows_ if r[0] == v['id'] and math.dist(r[2], (v['x'], v['y'], v['z'])) <= tol]
            if len(hit) == 1: found += 1
            else: missing.append('%s x%d' % (v['id'], len(hit)))
        # what is left near the band once the listed rows are gone (independent of the list: every placement whose prototype name looks like a tree)
        listed = {(v['id'], round(v['x'], 2), round(v['z'], 2)) for v in veg['rows']}
        left = []
        for rid, proto, pos in rows_:
            if not (g.x0 < pos[0] < g.x0 + g.nx * g.c and g.z0 < pos[2] < g.z0 + g.nz * g.c): continue
            if (rid, round(pos[0], 2), round(pos[2], 2)) in listed: continue
            if not any(t in proto for t in veg['tree_tokens']): continue
            near_core = min(max(abs(u) - c['size'][0] * 0.5, abs(w) - c['size'][2] * 0.5, 0.0) if True else 0 for c in D['cores'] for u, w in [box_uv(pos[0], pos[2], c['x'], c['z'], c['yaw'])])
            u, w = box_uv(pos[0], pos[2], st['x'], st['z'], st['yaw_box']); near_stone = max(abs(u) - st['half_m'], abs(w) - st['half_m'], 0.0)
            near_pad = min(max(abs(u2) - p['size'][0] * 0.5, abs(w2) - p['size'][2] * 0.5, 0.0) for p in st['pads'] for u2, w2 in [box_uv(pos[0], pos[2], p['x'], p['z'], p['yaw'])])
            if near_core <= veg['keep_clear_core_m'] or near_stone <= veg['keep_clear_stone_m'] or near_pad <= veg['keep_clear_stone_m']:
                left.append(dict(id=rid, proto=proto, x=round(pos[0], 2), z=round(pos[2], 2), core_m=round(near_core, 2), stone_m=round(near_stone, 2), pad_m=round(near_pad, 2)))
        R['vegetation']['sheets'].append(dict(path=sp['path'], role=sp['role'], protected=prot, sha256=sha(ap_), placements=len(rows_), listed_found=found, listed_missing=missing, left_inside_keep_clear=left))
        check(not prot, 'D1 sheet %s is not a protected asset' % sp['path'])
        if sp['role'] == 'rendered':
            check(found == len(veg['rows']), 'D2 %s: %d of %d listed rows found once by Id + position (tolerance %.2f m)%s' % (os.path.basename(sp['path']), found, len(veg['rows']), tol, '' if not missing else ' - missing/duplicate: ' + ', '.join(missing)))
            check(len(left) == 0, 'D3 %s: tree placements left within %.1f m of a core / %.1f m of the stone and pads after the clearing: %d %s' % (os.path.basename(sp['path']), veg['keep_clear_core_m'], veg['keep_clear_stone_m'], len(left), '' if not left else json.dumps(left, ensure_ascii=False)))
        else:
            say('INFO %s (%s): %d of %d listed rows are also there (not written by the tool: %s)' % (os.path.basename(sp['path']), sp['role'], found, len(veg['rows']), sp.get('note', '')))
    # ---------------- EA reach, detour, return loop ----------------
    if not args.skip_ea:
        say('-- E. EA reach change, detour, return loop [O]')
        W = ck['ea_window']; ge = Grid(h4, W[0], W[1], W[2], W[3], 1.0)
        lat = np.load(LAT)
        up = lambda a: a[np.ix_(np.clip(np.rint(ge.zs / 4).astype(int), 0, NZ4 - 1), np.clip(np.rint(ge.xs / 4).astype(int), 0, NX4 - 1))]
        blocked = ~up(lat['inside4']) | up(lat['water4'])
        segs = json.load(open(SHELL, encoding='utf-8'))['segments']; shell = np.zeros(blocked.shape, bool)
        for sg in segs:
            Pq = np.asarray(sg['points'], float)
            if Pq[:, 0].max() < W[0] - 10 or Pq[:, 0].min() > W[1] + 10 or Pq[:, 1].max() < W[2] - 10 or Pq[:, 1].min() > W[3] + 10: continue
            Q = resample(Pq, 0.5); t = np.gradient(Q, axis=0); t /= np.maximum(np.hypot(t[:, 0], t[:, 1]), 1e-9)[:, None]
            Q = Q + np.stack([t[:, 1], -t[:, 0]], 1) * sg['block']
            for qx, qz in Q:
                i, j = ge.cell(qx, qz); shell[max(0, i - 1):i + 2, max(0, j - 1):j + 2] = True
        blocked |= shell
        route = up(lat['route4'])
        H0, _, _, _, b0, s0 = build(ge, D, with_band=False); s0 = s0 | route
        H1, c1, st1, p1, b1, s1 = build(ge, D, with_band=True, grow=ck['ea_grow_m']); s1 = s1 | route
        seed = np.zeros(blocked.shape, bool); seed[ge.cell(*ck['ea_seed'])] = True
        before = flood(ge, H0, s0, b0, seed, blocked=blocked, jump=True)
        after = flood(ge, H1, s1, b1, seed, blocked=blocked, jump=True)
        foot = c1 | st1
        lost = before & ~after; gained = after & ~before
        R['ea_reach'] = dict(window=W, cell_m=1.0, seed=ck['ea_seed'], before_ha=round(before.sum() / 1e4, 3), after_ha=round(after.sum() / 1e4, 3), lost_m2=int(lost.sum()), footprint_m2=int((foot & before).sum()),
                             lost_outside_footprint_m2=int((lost & ~foot).sum()), gained_m2=int(gained.sum()))
        check(R['ea_reach']['lost_outside_footprint_m2'] == 0 and R['ea_reach']['gained_m2'] == 0, 'E1 EA reach (walk + jump, 1 m cells, band boxes grown by ea_grow_m so that a 1.2 m core cannot leak between cells, %.1f ha window): %.3f -> %.3f ha; lost %d m2 = the band footprint (core + stone) %d m2; lost outside the footprint %d, gained %d (both must be 0)' %
              ((W[1] - W[0]) * (W[3] - W[2]) / 1e4, R['ea_reach']['before_ha'], R['ea_reach']['after_ha'], R['ea_reach']['lost_m2'], R['ea_reach']['footprint_m2'], R['ea_reach']['lost_outside_footprint_m2'], R['ea_reach']['gained_m2']))
        # detour: shortest walk between two points of the cut, 55 m either side of the crossing, without / with the band
        RT = routes(); cut = resample(RT[ck['cut_route']], 1.0)
        dcross = np.hypot(cut[:, 0] - st['x'], cut[:, 1] - st['z']); s_cross = int(dcross.argmin())
        A = cut[max(0, s_cross - int(ck['detour_ref_m']))]; B = cut[min(len(cut) - 1, s_cross + int(ck['detour_ref_m']))]

        def shortest(Hs, stand_s, built_s, solid):
            bl = blocked | solid
            si, sj = ge.cell(*A); ti, tj = ge.cell(*B)
            Dm = np.full(Hs.shape, np.inf); Dm[si, sj] = 0; pq = [(0.0, si, sj)]; par = {}
            while pq:
                dd, i, j = heapq.heappop(pq)
                if dd > Dm[i, j]: continue
                if (i, j) == (ti, tj): break
                for a in (-1, 0, 1):
                    for b in (-1, 0, 1):
                        if not (a or b): continue
                        ni, nj = i + a, j + b
                        if ni < 0 or nj < 0 or ni >= Hs.shape[0] or nj >= Hs.shape[1] or bl[ni, nj]: continue
                        if a and b and (bl[i + a, j] or bl[i, j + b]): continue
                        run = math.hypot(a, b); dh = Hs[ni, nj] - Hs[i, j]
                        if not stand_s[ni, nj] or dh > run * TAN + 0.02: continue
                        nd = dd + math.hypot(run, dh)
                        if nd < Dm[ni, nj]: Dm[ni, nj] = nd; par[(ni, nj)] = (i, j); heapq.heappush(pq, (nd, ni, nj))
            path = []; cur = (ti, tj)
            while cur in par: path.append((float(ge.xs[cur[1]]), float(ge.zs[cur[0]]))); cur = par[cur]
            return float(Dm[ti, tj]), path[::-1]
        d0, _ = shortest(H0, s0, b0, np.zeros(blocked.shape, bool)); d1, p1_ = shortest(H0, s0, b0, foot)
        side = 'west' if np.mean([q[0] for q in p1_]) < st['x'] else 'east'
        R['detour'] = dict(ref_points=[[round(float(A[0]), 1), round(float(A[1]), 1)], [round(float(B[0]), 1), round(float(B[1]), 1)]], crossing_station_m=s_cross, without_band_m=round(d0, 1), around_band_m=round(d1, 1), detour_m=round(d1 - d0, 1), around_end=side)
        check(math.isfinite(d1), 'E2 the band can be walked around: %.1f m -> %.1f m between the cut points %d m either side of the stone = detour +%.1f m (around the %s end)' % (d0, d1, ck['detour_ref_m'], d1 - d0, side))
        # return loop B: new ground share (samples farther than new_ground_m from every outbound leg)
        def chain(ids):
            out = []; gaps = []
            for rid in ids:
                Pr = RT[rid]
                if out and np.hypot(*(Pr[-1] - out[-1])) < np.hypot(*(Pr[0] - out[-1])): Pr = Pr[::-1]
                if out: gaps.append(round(float(np.hypot(*(Pr[0] - out[-1]))), 1))
                out.extend(Pr.tolist())
            return np.asarray(out), gaps
        lp = ck['loop']; first = RT[lp['return_routes'][0]]; start = np.asarray(lp['start_xz'])
        ids = lp['return_routes']
        P0 = first if np.hypot(*(first[0] - start)) <= np.hypot(*(first[-1] - start)) else first[::-1]
        ret = [P0]; gaps = []
        for rid in ids[1:]:
            Pr = RT[rid]
            if np.hypot(*(Pr[-1] - ret[-1][-1])) < np.hypot(*(Pr[0] - ret[-1][-1])): Pr = Pr[::-1]
            gaps.append(round(float(np.hypot(*(Pr[0] - ret[-1][-1]))), 1)); ret.append(Pr)
        retP = np.concatenate(ret); out_pts = np.concatenate([resample(RT[r], 1.0) for r in lp['outbound_routes']])
        S = resample(retP, 1.0)
        dmin = np.array([np.hypot(out_pts[:, 0] - x, out_pts[:, 1] - z).min() for x, z in S])
        R['return_loop'] = dict(routes=ids, joint_gaps_m=gaps, length_m=round(length(retP), 0), new_ground_m=int((dmin > lp['new_ground_m']).sum()), new_ground_pct=round(100.0 * float((dmin > lp['new_ground_m']).mean()), 1),
                                new_ground_far_pct=round(100.0 * float((dmin > lp['new_ground_far_m']).mean()), 1), same_way_back_m=round(sum(length(RT[r]) for r in lp['same_way_routes']), 0))
        say('INFO E3 return loop B (%s): %.0f m, joints %s m; ground farther than %.0f m from every outbound leg: %d m = %.1f %% (farther than %.0f m: %.1f %%); the same way back = %.0f m, 0 %% new' %
            (' > '.join(ids), R['return_loop']['length_m'], gaps, lp['new_ground_m'], R['return_loop']['new_ground_m'], R['return_loop']['new_ground_pct'], lp['new_ground_far_m'], R['return_loop']['new_ground_far_pct'], R['return_loop']['same_way_back_m']))
        check(R['return_loop']['new_ground_pct'] >= lp['new_ground_min_pct'], 'E3 return loop new-ground share %.1f %% >= %.0f %%' % (R['return_loop']['new_ground_pct'], lp['new_ground_min_pct']))
        # where the stone sits on the cut
        R['cut'] = dict(route=ck['cut_route'], length_m=round(length(RT[ck['cut_route']]), 1), stone_station_m=s_cross, stone_off_line_m=round(float(dcross.min()), 2))
        check(dcross.min() <= ck['cut_off_line_max_m'], 'E4 the stone stands on %s: %.2f m off the line at station %d m of %.1f m (limit %.1f)' % (ck['cut_route'], dcross.min(), s_cross, R['cut']['length_m'], ck['cut_off_line_max_m']))
    R['fails'] = fails; R['verdict'] = 'DRY OK (offline)' if not fails else 'DRY FAIL'
    say('== %s: %d fail(s)' % (R['verdict'], len(fails)))
    os.makedirs(args.out, exist_ok=True)
    json.dump(R, open(os.path.join(args.out, 'amneung308_dry.json'), 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
    open(os.path.join(args.out, 'amneung308_dry.txt'), 'w', encoding='utf-8').write('\n'.join(L) + '\n')
    return 1 if fails else 0


if __name__ == '__main__':
    sys.exit(main())
