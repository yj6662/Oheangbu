"""#308 small dungeons - `dungeon308`. Offline, deterministic. SPEC-WORLD-DUNGEON-308 (D308-57, D308-58).

  python Tools/Art/dungeon308.py plan  Tools/Unity/Stage308_dungeon/dungeon308.json   # sites -> Art/World/Compact/Rebuild/Dungeon308/sites.json
  python Tools/Art/dungeon308.py build Tools/Unity/Stage308_dungeon/dungeon308.json   # -> Dungeon308/<id>/{Meshes/*.json, layout.json}

One grammar for every dungeon: a walled compound of cells in two rows on a level stone podium, entered on the uphill side.
The way runs along row A, turns, comes back along row B and ends at a door that is unbarred from the inside, next to the entrance:
  A1 -> A2 -> .. -> An -> Bn -> .. -> B1 =door=> A1
Each connection carries one feature (data): open | lift (a 2.3 m ledge: only the wood field glyph 국 gets up) | drop (back to the
entry level, one way) | vine | boulder | taint (a field gate the fire / metal / water glyph opens). What differs between realms is
data too: cell size, wall kind, floor, roofs, props. Nothing is cut into the terrain and no tile gets a hole.
World positions are final: the ground is the stage height field the play scene stands on (triangle surface).
"""
import hashlib, json, math, sys
from pathlib import Path
import numpy as np
from scipy import ndimage as ndi

sys.path.insert(0, str(Path(__file__).resolve().parent))
import cliff308_base as B                                                # noqa: E402
from gallery308 import Mesh                                              # noqa: E402

OUT = B.RB / 'Dungeon308'
UP = np.array([0.0, 1.0, 0.0])


ROCK = 'Assets/_Project/Art/World/Mine308/Materials/Mine308_Rock'
LOOSE = 'Assets/_Project/Art/World/Architecture296/Materials/1f9cec1d03d18cb4f86662acf431bddc_LooseStone'
CAVE_FLOOR = 'Assets/_Project/Art/World/Architecture296/Materials/41c9392d60a234842918bf0c347ecc70_Floor'
TIMBER = 'Assets/_Project/Art/World/Mine306/Materials/Mine306_TimberBoard'
CAVE = dict(wall='cave', wall_mat=ROCK, cope_mat=LOOSE, floor_mat=CAVE_FLOOR, roof_mat=ROCK, wood_mat=TIMBER, boulder_mat=LOOSE)


def vnoise(P, seed):
    """value noise in [0, 1] at points P (..., 3): a function of the place only."""
    P = np.asarray(P, float); i = np.floor(P).astype(np.int64); f = P - i; f = f * f * (3 - 2 * f)
    def h(dx, dy, dz):
        x = ((i[..., 0] + dx) * 73856093) ^ ((i[..., 1] + dy) * 19349663) ^ ((i[..., 2] + dz) * 83492791) ^ (int(seed) * 2654435761)
        x &= 0xffffffff; x = ((x ^ (x >> 13)) * 1274126177) & 0xffffffff; x ^= x >> 16; return (x & 0xffffff) / float(0xffffff)
    fx, fy, fz = f[..., 0], f[..., 1], f[..., 2]
    a = (h(0, 0, 0) * (1 - fx) + h(1, 0, 0) * fx) * (1 - fy) + (h(0, 1, 0) * (1 - fx) + h(1, 1, 0) * fx) * fy
    b = (h(0, 0, 1) * (1 - fx) + h(1, 0, 1) * fx) * (1 - fy) + (h(0, 1, 1) * (1 - fx) + h(1, 1, 1) * fx) * fy
    return a * (1 - fz) + b * fz


def rough(P, seed):
    return 0.65 * vnoise(np.asarray(P) / 2.6, seed) + 0.35 * vnoise(np.asarray(P) / 0.9, seed + 7)


def sheet(mesh, P, mat, toward, uvs=0.5):
    """a grid of points P (nu, nv, 3) as one smooth surface facing `toward`."""
    nu, nv = P.shape[:2]; n = np.cross(np.gradient(P, axis=0), np.gradient(P, axis=1)); n /= np.linalg.norm(n, axis=-1, keepdims=True) + 1e-12
    flip = float((n @ np.asarray(toward, float)).mean()) < 0
    if flip: n = -n
    b = len(mesh.v); tri = mesh.sub.setdefault(mat, [])
    for a in range(nu):
        for c in range(nv): mesh.v.append(P[a, c]); mesh.n.append(n[a, c]); mesh.uv.append((a * uvs, c * uvs))
    for a in range(nu - 1):
        for c in range(nv - 1):
            p00 = b + a * nv + c; p10 = p00 + nv; p01 = p00 + 1; p11 = p10 + 1
            tri += [p00, p10, p11, p00, p11, p01] if flip else [p00, p11, p10, p00, p01, p11]


_T = (1 + 5 ** 0.5) / 2
_ICO = np.array([(-1, _T, 0), (1, _T, 0), (-1, -_T, 0), (1, -_T, 0), (0, -1, _T), (0, 1, _T), (0, -1, -_T), (0, 1, -_T), (_T, 0, -1), (_T, 0, 1), (-_T, 0, -1), (-_T, 0, 1)], float); _ICO /= np.linalg.norm(_ICO[0])
_ICO_TRI = [0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8, 3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1]


def stone(mesh, c, size, mat, rng):
    """a rough stone: a pushed-about icosahedron with one normal per corner (the ink outline then draws its silhouette, not every facet)."""
    c = np.asarray(c, float); a, b, g = rng.uniform(0, 2 * math.pi, 3); ca, sa, cb, sb, cg, sg = math.cos(a), math.sin(a), math.cos(b), math.sin(b), math.cos(g), math.sin(g)
    R = np.array([[ca * cb, ca * sb * sg - sa * cg, ca * sb * cg + sa * sg], [sa * cb, sa * sb * sg + ca * cg, sa * sb * cg - ca * sg], [-sb, cb * sg, cb * cg]])
    P = (_ICO * rng.uniform(0.82, 1.18, (12, 1)) * (np.array([rng.uniform(0.75, 1.25), rng.uniform(0.5, 0.95), rng.uniform(0.75, 1.25)]) * size / 2)) @ R.T + c
    top = float(np.sort(P[:, 1])[-3]); k = len(mesh.v); tri = mesh.sub.setdefault(mat, [])
    if getattr(mesh, 'organic', False) and size >= 0.55:      # grammar organic: the same twelve corners, split and pushed about by value noise: a lump, not a die (no rng: the draws stay as they were)
        V, T = _lump(P - c, c, 2 if size >= 1.8 else 1); nrm = np.zeros_like(V)
        for a_, b_, d_ in T.reshape(-1, 3): fn = np.cross(V[b_] - V[a_], V[d_] - V[a_]); nrm[[a_, b_, d_]] += fn
        nrm /= np.linalg.norm(nrm, axis=1, keepdims=True) + 1e-12; out = (nrm * V).sum(1) < 0; nrm[out] *= -1; V = V + c
        for q, n_ in zip(V, nrm): mesh.v.append(q); mesh.n.append(n_); mesh.uv.append(((q[0] + q[1]) * 0.5, q[2] * 0.5))
        tri += [k + int(t) for t in T]; return top
    for q in P: mesh.v.append(q); n_ = q - c; mesh.n.append(n_ / (np.linalg.norm(n_) + 1e-12)); mesh.uv.append(((q[0] + q[1]) * 0.5, q[2] * 0.5))
    tri += [k + t for t in _ICO_TRI]
    return top


def _split(V, T):
    """one loop of midpoint subdivision of a closed triangle list (shared edges keep one midpoint)."""
    V = list(V); mid = {}; out = []
    def m(a, b):
        key = (min(a, b), max(a, b))
        if key not in mid: mid[key] = len(V); V.append((np.asarray(V[a]) + np.asarray(V[b])) / 2)
        return mid[key]
    for a, b, c in np.asarray(T).reshape(-1, 3):
        ab, bc, ca = m(a, b), m(b, c), m(c, a); out += [a, ab, ca, ab, b, bc, ca, bc, c, ab, bc, ca]
    return np.array(V, float), np.array(out, int)


def _lump(Q, c, levels=2):
    """the stone's corners Q (12, 3) about its centre c -> a rounded, lumpy surface: subdivide (twice for the big ones), pull every point out to the radius its
    direction would have on the corners' hull (so the size stays), then bumps of value noise at two scales (a function of the place)."""
    V, T = _split(Q, _ICO_TRI)
    for _ in range(levels - 1): V, T = _split(V, T)
    r = np.linalg.norm(V, axis=1, keepdims=True) + 1e-12; d = V / r
    R0 = np.linalg.norm(Q, axis=1); w = np.maximum(d @ (Q / R0[:, None]).T, 0.0) ** 6; base = (w @ R0) / (w.sum(1) + 1e-12)
    P = d * base[:, None]; s = float(np.mean(R0)); seed = int(abs(c[0] * 7.3 + c[1] * 13.1 + c[2] * 3.7) * 100) % 99991
    bump = 0.17 * (vnoise(P / (0.55 * s) + c, seed) - 0.5) * 2 + 0.07 * (vnoise(P / (0.22 * s) + c, seed + 5) - 0.5) * 2
    flat = np.clip(-d[:, 1] - 0.55, 0, None) * 0.5      # a little flatter where it sits
    return P * (1 + bump - flat)[:, None], T


def seed_of(*parts):
    return int.from_bytes(hashlib.sha256(':'.join(map(str, parts)).encode('utf-8')).digest()[:4], 'little')


# ------------------------------------------------------------------ the frame of a site and the way down into it
def solve(cfg, D, x, z, yaw, f, blocked=None):
    """an underground dungeon at (x, z) facing `yaw` (a multiple of 90: the stair pit must lie on lattice cells).
    -> None when the stair cannot surface, else dict(o2, ax, az, y0, ex, run, yl, steps, cells=[(i, j, hole, z_far, z_near)])."""
    G = dict(cfg['grammar'], **D.get('grammar', {})); n = D['cols']; cw, cd = D['cell']; W_ = n * cw; Dp = 2 * cd; yaw = round(yaw / 90.0) * 90.0 % 360.0
    c, s_ = round(math.cos(math.radians(yaw))), round(math.sin(math.radians(yaw))); ax = np.array([c, -s_], float); az = np.array([s_, c], float)
    o2 = np.array([x, z], float) - ax * W_ / 2 - az * Dp / 2; ex = cw / 2
    k = 0 if abs(ax[0]) > 0.5 else 1; cen = (o2 + ax * ex)[k]; o2[k] += (math.floor(cen / 4.0) * 4.0 + 2.0) - cen      # the stair centre runs down the middle of a lattice column
    gs = lambda u, v: float(B.sample_tri(f, *(o2 + ax * u + az * v)))
    grid = [gs(u, v) for u in np.linspace(-2, W_ + 2, 11) for v in np.linspace(-2, Dp + 2, 9)]
    lifts = 0; top_lv = 0
    for ft in D['features']:
        lifts = lifts + 1 if ft == 'lift' else (0 if ft == 'drop' else lifts); top_lv = max(top_lv, lifts)
    y0 = min(grid) - G['cover_m'] - (top_lv * G['ledge_m'] + G['roof_h'] + 0.3)
    ratio = G['step_rise'] / G['step_tread']; kz = 1 - k; d = None
    for dd in np.arange(2.0, G['stair_max_m'], 0.5):
        if y0 + ratio * dd >= gs(ex, -dd): d = dd; break
    if d is None: return None
    while d <= G['stair_max_m']:      # the top of the stair ends on a lattice line
        line = (o2 + ax * ex - az * d)[kz]; snap = math.floor(line / 4.0) * 4.0 if az[kz] > 0 else math.ceil(line / 4.0) * 4.0; d += abs(line - snap)
        yl = min(gs(ex + u, -d) for u in (-1.6, 0.0, 1.6)) - 0.05
        if y0 + 0.5 < yl <= y0 + ratio * d + 1e-6: break
        d += 4.0 if yl > y0 + ratio * d else 1e9
    else: return None
    if d > G['stair_max_m']: return None
    steps = int(math.ceil((yl - y0) / G['step_rise'])); cells = []
    for q in range(int(round(d / 4.0 + 0.49))):
        z_far, z_near = -(d - 4 * q), min(-(d - 4 * (q + 1)), 0.0); cc = o2 + ax * ex + az * (z_far + 2.0); j, i = int(math.floor(cc[0] / 4.0)), int(math.floor(cc[1] / 4.0))
        if not (1 <= i < B.HH - 2 and 1 <= j < B.W - 2): return None
        y_far = y0 + (yl - y0) * (-z_far) / d; gmin = float(f[i:i + 2, j:j + 2].min()); hole = gmin - (y_far + G['head_m']) < G['skin_m']
        if hole and blocked is not None and blocked[i:i + 2, j:j + 2].any(): return None
        cells.append((i, j, bool(hole), z_far, z_near))
    if not cells or not cells[0][2]: return None
    return dict(o2=o2, ax=ax, az=az, yaw=yaw, y0=y0, ex=ex, run=float(d), yl=float(yl), steps=steps, cells=cells, ground=[min(grid), max(grid)])


# ------------------------------------------------------------------ plan
def plan(cfg):
    P = cfg['plan']; f = B.load_height(B.ROOT / cfg['height']); X, Z = B.grid_xz()
    keep = B.wet() | B.protected()
    sc = json.loads((B.ROOT / P['scan']).read_text(encoding='utf-8')); occ = np.zeros(sc['cols'] * sc['rows'], bool); occ[sc['occupied']] = True; occ = occ.reshape(sc['rows'], sc['cols'])
    keep |= occ[np.clip((Z / sc['cell']).astype(int), 0, sc['rows'] - 1), np.clip((X / sc['cell']).astype(int), 0, sc['cols'] - 1)]
    base = B.load_height(); keep |= np.abs(f - base) > 0.05                    # cliff bodies, ledges, the cove mountain
    lat = json.loads((B.ROOT / P['routes']).read_text(encoding='utf-8')); road = np.zeros(X.shape, bool)
    for r in lat['routes']: road |= B.stamp(np.asarray(r['pts'], float), r['width'] / 2 + P['route_margin_m'], X, Z)
    for it in json.loads((B.ROOT / P['keepout']).read_text(encoding='utf-8'))['items']: keep |= np.hypot(X - it['x'], Z - it['z']) <= max(float(it.get('keepout_m', 10)), 10) + 25
    keep |= road; dk = ndi.distance_transform_edt(~keep, sampling=4.0); droad = ndi.distance_transform_edt(~road, sampling=4.0)
    gz, gx = np.gradient(f, 4.0); sites = {}; taken = []
    if cfg['grammar'].get('underground'):
        hm = B.RB / 'Hamlet308/layout.json'
        if hm.exists():
            for h_ in json.loads(hm.read_text(encoding='utf-8'))['houses']: keep |= np.hypot(X - h_['x'], Z - h_['z']) <= 14
        dk = ndi.distance_transform_edt(~keep, sampling=4.0); slope = np.degrees(np.arctan(np.hypot(gx, gz)))
        for D in cfg['dungeons']:
            rng = np.random.default_rng(seed_of(cfg['seed'], 'site', D['id'])); half = math.hypot(D['cols'] * D['cell'][0] + 4, 2 * D['cell'][1] + 4) / 2; found = None
            for relax in (1.0, 1.6, 2.6):
                m = B.poly_mask(B.REALMS[D['realm']], X, Z) & (dk >= P['pit_clear_m']) & (droad <= P['road_far_m'] * relax) & (X > 90) & (X < 3910) & (Z > 90) & (Z < 5910) & (slope >= P['slope_deg'][0]) & (slope <= P['slope_deg'][1])
                if 'box' in D: x0, z0, x1, z1 = D['box']; m &= (X >= x0) & (X <= x1) & (Z >= z0) & (Z <= z1)
                for tx, tz in taken: m &= np.hypot(X - tx, Z - tz) >= P['apart_m'] / relax
                ii, jj = np.nonzero(m); order = rng.permutation(len(ii))[:P['tries']]
                for k in order:      # the cell is where the stair pit opens; the rooms lie behind it, under the hill
                    i, j = int(ii[k]), int(jj[k]); down = math.degrees(math.atan2(-gx[i, j], -gz[i, j])); yaw = (round((down + 180.0) / 90.0) * 90.0) % 360.0      # the front (-z) looks downhill
                    c_, s_ = round(math.cos(math.radians(yaw))), round(math.sin(math.radians(yaw))); az = np.array([s_, c_], float); n_ = D['cols']; cw, cd = D['cell']
                    for back in (10.0, 14.0, 18.0, 24.0):
                        ax_ = np.array([c_, -s_], float); cx, cz = np.array([j * 4.0 + 2, i * 4.0 + 2]) + az * (back + cd) + ax_ * (n_ * cw / 2 - cw / 2)
                        if not (120 < cx < 3880 and 120 < cz < 5880): continue
                        sol = solve(cfg, D, cx, cz, yaw, f, blocked=keep)
                        if sol and sol['run'] <= P['run_max_m']: found = (cx, cz, yaw, sol); break
                    if found: break
                if found: break
            if not found: sites[D['id']] = dict(error='no site'); continue
            cx, cz, yaw, sol = found; taken.append((cx, cz))
            sites[D['id']] = dict(x=float(cx), z=float(cz), yaw=float(yaw), run_m=round(sol['run'], 1), depth_m=round(sol['yl'] - sol['y0'], 1), holes=sum(1 for c in sol['cells'] if c[2]), ground=[round(v, 1) for v in sol['ground']], road_m=round(float(droad[int(cz // 4), int(cx // 4)]), 1))
        OUT.mkdir(parents=True, exist_ok=True); B.write_json(OUT / 'sites.json', sites)
        for k, v in sites.items(): print(k, v)
        return
    for D in cfg['dungeons']:
        if 'site' in D:
            st = dict(D['site'], fixed=True); i, j = int(st['z'] // 4), int(st['x'] // 4)
            if 'yaw' not in st: st['yaw'] = round(math.degrees(math.atan2(-gx[i, j], -gz[i, j])), 1)
            sites[D['id']] = st; taken.append((st['x'], st['z']))
    for D in cfg['dungeons']:
        if D['id'] in sites: continue
        n = D['cols']; w = n * D['cell'][0] + 6; d_ = 2 * D['cell'][1] + 6; half = math.hypot(w, d_) / 2; r = int(math.ceil(half / 4))
        rel = ndi.maximum_filter(f, size=2 * r + 1) - ndi.minimum_filter(f, size=2 * r + 1)
        for relax in (1.0, 1.5, 2.2, 3.0):      # a realm of steep ground gets a looser search, never a looser keep-out
            m = B.poly_mask(B.REALMS[D['realm']], X, Z) & (dk >= half + P['clear_m']) & (droad <= P['road_far_m'] * relax) & (rel <= P['relief_max_m'] * (1 + (relax - 1) * 0.5))
            m &= (X > 60) & (X < 3940) & (Z > 60) & (Z < 5940)
            if 'box' in D: x0, z0, x1, z1 = D['box']; m &= (X >= x0) & (X <= x1) & (Z >= z0) & (Z <= z1)
            for tx, tz in taken: m &= np.hypot(X - tx, Z - tz) >= P['apart_m'] / relax
            if m.any(): break
        if not m.any(): sites[D['id']] = dict(error='no site'); continue
        score = rel + droad * P['w_road'] ; score = np.where(m, score, np.inf); i, j = np.unravel_index(np.argmin(score), score.shape)
        up_ = np.array([gx[i, j], gz[i, j]]); yaw = math.degrees(math.atan2(-up_[0], -up_[1])) if np.linalg.norm(up_) > 1e-4 else 0.0      # local -z (the front) faces uphill
        sites[D['id']] = dict(x=float(j * 4), z=float(i * 4), yaw=round(yaw, 1), relief_m=round(float(rel[i, j]), 1), clear_m=round(float(dk[i, j]), 1), road_m=round(float(droad[i, j]), 1), relax=relax)
        taken.append((j * 4.0, i * 4.0))
    OUT.mkdir(parents=True, exist_ok=True); B.write_json(OUT / 'sites.json', sites)
    for k, v in sites.items(): print(k, v)


# ------------------------------------------------------------------ build
class Kit:
    """the drawn pieces of one dungeon + its collider boxes, in world space."""

    def __init__(self, did, origin, yaw, rng, th):
        self.id = did; self.o = np.asarray(origin, float); c, s = math.cos(math.radians(yaw)), math.sin(math.radians(yaw)); self.yaw = yaw
        self.ax = np.array([c, 0.0, -s]); self.az = np.array([s, 0.0, c]); self.rng = rng; self.th = th
        self.m = {}; self.cols = []; self.cave = False; self.seed = 0; self.roof_amp = 0.8; self.bulge = 0.5; self.organic = 0.0; self.grid = None; self.wall_h = 4.3; self.dome = 0.0; self.jamb = False; self.sides = []

    def W(self, x, y, z):
        return self.o + self.ax * np.asarray(x, float)[..., None] + UP * np.asarray(y, float)[..., None] + self.az * np.asarray(z, float)[..., None]

    def cave_floor(self, x0, x1, y, z0, z1, mat):
        X, Z = np.meshgrid(np.linspace(x0, x1, max(2, int((x1 - x0) / 0.6) + 1)), np.linspace(z0, z1, max(2, int((z1 - z0) / 0.6) + 1)), indexing='ij')
        P = self.W(X, np.full(X.shape, y), Z); P[..., 1] += -0.02 + 0.11 * rough(P, self.seed + 11); sheet(self.mesh('Floor'), P, mat, UP)

    def cave_roof(self, x0, x1, ry, z0, z1, mat, dome):
        X, Z = np.meshgrid(np.linspace(x0, x1, max(2, int((x1 - x0) / 0.7) + 1)), np.linspace(z0, z1, max(2, int((z1 - z0) / 0.7) + 1)), indexing='ij')
        e = np.maximum(np.abs(X - (x0 + x1) / 2) / ((x1 - x0) / 2), np.abs(Z - (z0 + z1) / 2) / ((z1 - z0) / 2))
        P = self.W(X, np.full(X.shape, ry), Z); P[..., 1] -= self.roof_n(P) + dome * e ** 2.5; sheet(self.mesh('Roof'), P, mat, -UP)

    def wall_amp(self, P):
        """how far the rough rock face stands out of the flat wall face at P (away from jambs and corners): the same numbers as cave_wall."""
        return 0.05 + self.bulge * np.clip((0.6 * vnoise(np.asarray(P) / 5.5, self.seed + 3) + 0.4 * rough(P, self.seed) - 0.5) * 2.8 + 0.5, 0.0, 1.0)

    def roof_n(self, P):
        return 0.05 + self.roof_amp * np.clip((0.55 * vnoise(np.asarray(P) / 4.5, self.seed + 29) + 0.45 * rough(P, self.seed + 23) - 0.5) * 2.6 + 0.5, 0.0, 1.0)

    def ceil_y(self, x, z, ry, rc, dome):
        """the height (local) of the drawn cave roof over a point: the same numbers as cave_roof."""
        x0, x1, z0, z1 = rc; e = max(abs(x - (x0 + x1) / 2) / ((x1 - x0) / 2), abs(z - (z0 + z1) / 2) / ((z1 - z0) / 2)); P = self.w(x, ry, z)[None]
        return ry - (float(self.roof_n(P)[0]) + dome * min(e, 1.0) ** 2.5)

    def cave_wall(self, alongx, a0, a1, c_, y0, y1, t, bx):
        """rock: a flat backing block, a rough face on each side that bulges into the room (calm at the ends: jambs and corners), a collider a little thicker than the block."""
        th = self.th; q = bx(a0, a1, c_ - t / 2, c_ + t / 2); self.box('Wall', q[0], q[1], y0, y1, q[2], q[3], th['wall_mat'])
        U, V = np.meshgrid(np.linspace(a0, a1, max(2, int((a1 - a0) / 0.55) + 1)), np.linspace(y0, y1, max(2, int((y1 - y0) / 0.55) + 1)), indexing='ij')
        sm = lambda x: x * x * (3 - 2 * x); e0 = sm(np.clip((U - a0) / 0.7, 0, 1)); e1 = sm(np.clip((a1 - U) / 0.7, 0, 1)); ends = np.minimum(e0, e1)
        low = V - y0 < 2.2; stp = max(1, int(round(1.5 / 0.55))); nU = U.shape[0]
        org = self.organic; g_ = (self.grid[0] if alongx else self.grid[1]) if self.grid else 0.0
        corner = lambda a: org > 0 and g_ > 0 and abs(a / g_ - round(a / g_)) < 1e-6      # a cell corner (a doorway jamb is never on the grid)
        if org > 0:      # the drawn roof meets the wall at ry - roof_n - dome (e = 1 on every cell edge): curl in under that line, not at a fixed height
            ry = y1 - 0.18; Pr = self.W(U, np.full(U.shape, ry), np.full(U.shape, c_)) if alongx else self.W(np.full(U.shape, c_), np.full(U.shape, ry), U)
            ceil = ry - self.roof_n(Pr) - self.dome; ramp = sm(np.clip((V - (ceil - 1.9)) / 1.6, 0, 1))      # 0 up to 1.9 m under the roof line, 1 from 0.3 m under it
        else: ramp = np.zeros(U.shape)
        for sd in (-1, 1):
            C = np.full(U.shape, c_ + sd * t / 2); P = self.W(U, V, C) if alongx else self.W(C, V, U); nrm = (self.az if alongx else self.ax) * sd
            base = self.bulge * np.clip((0.6 * vnoise(P / 5.5, self.seed + 3) + 0.4 * rough(P, self.seed) - 0.5) * 2.8 + 0.5, 0.0, 1.0)
            amp = 0.05 + base * ends      # value noise sits near its mean: stretch it, or the wall is only pushed out, not shaped
            draw = amp
            if org > 0:      # grammar organic (drawn only, the colliders below keep `amp`): over head height the wall leans into the room and
                # the corners fill in to a rounded hollow, so no straight vertical edge and no crisp ceiling line; at the foot only a little
                d0 = e0 + (1 - e0) * (0.25 + 0.75 * ramp) if corner(a0) else e0; d1 = e1 + (1 - e1) * (0.25 + 0.75 * ramp) if corner(a1) else e1
                fine = 0.25 * (vnoise(P / 2.2, self.seed + 53) - 0.5) * 2 * np.minimum(e0, e1)      # a shorter octave: one period of P/5.5 over a 7 m wall only pushes the plane out (+-0.25 m: the colliders stay near)
                draw = 0.05 + base * np.minimum(d0, d1) + fine + org * (0.3 + 0.9 * vnoise(P / 7.0, self.seed + 41)) * ramp ** 1.5
                R = 0.35 + 1.15 * ramp
                for a_, dist in ((a0, U - a0 - t / 2), (a1, a1 - U - t / 2)):
                    if corner(a_): draw = draw + org * R * np.clip(1 - np.maximum(dist, 0) / R, 0, 1) ** 2
            P = P + nrm * draw[..., None]; sheet(self.mesh('Wall'), P, th['wall_mat'], nrm)
            for i0 in range(0, nU - 1, stp):
                i1 = min(nU - 1, i0 + stp); m_ = low[i0:i1 + 1]; depth = max(0.12, float(amp[i0:i1 + 1][m_].mean() if m_.any() else amp[i0:i1 + 1].mean()) - 0.12); e_ = c_ + sd * (t / 2 + depth)
                q = bx(float(U[i0, 0]), float(U[i1, 0]), min(c_, e_), max(c_, e_)); self.collide(q[0], q[1], y0, y1, q[2], q[3])
        if org > 0 and self.jamb:      # a doorway jamb (not a cell corner, not a door): a rounded noisy cap over the flat end of the block, drawn only (0.15-0.3 m into the way)
            Vv = V[0]; th_ = np.linspace(-math.pi / 2, math.pi / 2, 9)
            for a_, out in ((a0, -1.0), (a1, 1.0)):
                if corner(a_): continue
                TH, VV = np.meshgrid(th_, Vv, indexing='ij'); A_ = np.full(TH.shape, a_); C0 = np.full(TH.shape, c_)
                Pn = self.W(A_, VV, C0) if alongx else self.W(C0, VV, A_); pro = (0.15 + 0.15 * rough(Pn, self.seed + 59)) * np.cos(TH)
                UU = a_ + out * pro; CC = c_ + np.sin(TH) * (t / 2 + 0.05)
                P = self.W(UU, VV, CC) if alongx else self.W(CC, VV, UU); sheet(self.mesh('Wall'), P, th['wall_mat'], (self.ax if alongx else self.az) * out)

    def cave_arch(self, alongx, mid, c_, base, top, t, dw):
        """grammar organic: the rock closes over a doorway slot of a cave in an uneven arch from a little over head height. One sheet each side:
        the underside from the wall's middle, a rounded lip, then the face up to the wall top, tapering back to the wall face at both ends.
        Drawn only: nothing there to walk into (the arch springs 2.8 m over the higher floor)."""
        org = self.organic; half = dw / 2 + 0.3; U = np.linspace(mid - half, mid + half, 13); q = np.clip(np.abs(U - mid) / (dw / 2), 0, 1)
        soff = base + 2.8 + (0.7 + 0.5 * org) * np.sqrt(np.clip(1 - q ** 2, 0, 1)); taper = np.clip(1 - np.abs(U - mid) / half, 0, 1) ** 0.6
        nV = max(4, int((top - soff.min()) / 0.5)); r = 0.45
        for sd in (-1, 1):
            nrm = (self.az if alongx else self.ax) * sd; w = t / 2 + 0.05 + 0.4 * taper; rows = []
            prof = [(k / 3 * (w - r), np.zeros_like(U)) for k in range(4)] + [((w - r) + r * math.sin(f_), r - r * math.cos(f_)) for f_ in np.linspace(0, math.pi / 2, 5)[1:]]
            for co, yo in prof: rows.append((np.broadcast_to(co, U.shape), soff + yo))
            for k in range(1, nV + 1): rows.append((w, soff + r + (top - soff - r) * k / nV))
            CO = np.stack([np.broadcast_to(a_, U.shape) for a_, _ in rows], 1); Y = np.stack([b_ for _, b_ in rows], 1); UU = np.broadcast_to(U[:, None], CO.shape)
            C = c_ + sd * CO; P = self.W(UU, Y, C) if alongx else self.W(C, Y, UU)
            n_ = 0.32 * (rough(P, self.seed + 67) - 0.5) * 2 * taper[:, None]; P = P + nrm * n_[..., None] - UP * (0.18 * (rough(P, self.seed + 71) - 0.5) * 2)[..., None] * (CO < w[:, None] - 0.01)[..., None]
            sheet(self.mesh('Wall'), P, self.th['wall_mat'], nrm)

    def mesh(self, name):
        if name not in self.m: self.m[name] = Mesh(f'{self.id}_{name}'); self.m[name].organic = self.organic > 0
        return self.m[name]

    def w(self, x, y, z):
        return self.o + self.ax * x + UP * y + self.az * z

    def box(self, name, x0, x1, y0, y1, z0, z1, mat, collide=False):
        c = self.w((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2); self.mesh(name).box(c, self.ax, UP, self.az, (x1 - x0, y1 - y0, z1 - z0), mat)
        if collide: self.collide(x0, x1, y0, y1, z0, z1)

    def collide(self, x0, x1, y0, y1, z0, z1):
        self.cols.append(dict(center=[round(float(v), 3) for v in self.w((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2)], size=[round(x1 - x0, 3), round(y1 - y0, 3), round(z1 - z0, 3)], yaw=self.yaw))

    def wall(self, x0, z0, x1, z1, y0, y1, t):
        """a wall along a local axis (x0 == x1 or z0 == z1), thickness t, drawn in the theme's kind; one collider inside it."""
        rng = self.rng; th = self.th; kind = th['wall']; L = abs(x1 - x0) + abs(z1 - z0)
        if L < 0.05: return
        alongx = abs(x1 - x0) > abs(z1 - z0); a0, a1 = (min(x0, x1), max(x0, x1)) if alongx else (min(z0, z1), max(z0, z1)); c_ = z0 if alongx else x0
        bx = (lambda u0, u1, v0, v1: (u0, u1, v0, v1)) if alongx else (lambda u0, u1, v0, v1: (v0, v1, u0, u1))
        if self.cave: self.cave_wall(alongx, a0, a1, c_, y0, y1, t, bx); return
        def put(name, u0, u1, ya, yb, v0, v1, mat):
            q = bx(u0, u1, v0, v1); self.box(name, q[0], q[1], ya, yb, q[2], q[3], mat)
        hanok = kind == 'hanok'
        if hanok: real = self.mesh('Wall'); self.m['Wall'] = Mesh('_discard'); self.m['Wall'].organic = False; kind = 'rubble'      # the rubble wall's rng draws, thrown away: nothing else in the dungeon moves
        if kind in ('rubble', 'ashlar', 'earth'):
            put('Wall', a0, a1, y0, y1, c_ - t / 2, c_ + t / 2, th['wall_mat'])
            if kind == 'ashlar': put('Wall', a0 - 0.04, a1 + 0.04, y1, y1 + 0.16, c_ - t / 2 - 0.08, c_ + t / 2 + 0.08, th['cope_mat'])
            else:      # uneven coping stones and a few stones standing proud of the face
                u = a0
                while u < a1 - 0.2:
                    ln = min(rng.uniform(0.5, 1.3), a1 - u); cc = self.w(*( (u + ln / 2, y1 + rng.uniform(0.0, 0.12), c_) if alongx else (c_, y1 + rng.uniform(0.0, 0.12), u + ln / 2) ))
                    self.mesh('Wall').slab(cc, self.ax if alongx else self.az, self.az if alongx else self.ax, ln / 2, t / 2 + rng.uniform(0.02, 0.12), rng.uniform(0.14, 0.3), th['cope_mat'], rng, tilt=0.08); u += ln
                for _ in range(int(L * (y1 - y0) * 0.12)):
                    u = rng.uniform(a0 + 0.3, a1 - 0.3) if a1 - a0 > 0.7 else (a0 + a1) / 2; yy = rng.uniform(y0 + 0.3, y1 - 0.3); sd = rng.choice([-1, 1]); sz_ = rng.uniform(0.25, 0.6)
                    put('Wall', u - sz_ / 2, u + sz_ / 2, yy - sz_ * 0.3, yy + sz_ * 0.3, c_ + sd * (t / 2 - 0.02), c_ + sd * (t / 2 + rng.uniform(0.03, 0.09)), th['cope_mat'])
        elif kind == 'palisade':
            u = a0 + 0.14
            while u < a1:
                r_ = rng.uniform(0.11, 0.17); top = y1 + rng.uniform(-0.35, 0.3); lean = rng.uniform(-0.05, 0.05)
                p0 = self.w(*((u, y0 - 0.2, c_) if alongx else (c_, y0 - 0.2, u))); p1 = self.w(*((u + lean, top, c_ + rng.uniform(-0.05, 0.05)) if alongx else (c_ + rng.uniform(-0.05, 0.05), top, u + lean)))
                self.mesh('Wall').log(p0, p1, r_, r_ * 0.8, th['wall_mat'], rng=rng, wob=0.1); u += r_ * 2 + rng.uniform(0.0, 0.04)
            for yy in (y0 + (y1 - y0) * 0.3, y0 + (y1 - y0) * 0.75):
                put('Wall', a0, a1, yy - 0.06, yy + 0.06, c_ + t / 2 - 0.02, c_ + t / 2 + 0.08, th['cope_mat'])
        elif kind == 'plank':
            u = a0
            while u < a1 + 0.01:
                put('Wall', u - 0.09, u + 0.09, y0, y1 + rng.uniform(0.0, 0.2), c_ - t / 2, c_ + t / 2, th['cope_mat']); u += rng.uniform(1.6, 2.3)
            yy = y0
            while yy < y1 - 0.05:
                hb = min(rng.uniform(0.2, 0.32), y1 - yy); put('Wall', a0 + rng.uniform(0.0, 0.08), a1 - rng.uniform(0.0, 0.08), yy + 0.01, yy + hb - 0.01, c_ - 0.04 + rng.uniform(-0.015, 0.015), c_ + 0.04, th['wall_mat']); yy += hb
        if hanok: self.m['Wall'] = real; put('Wall', a0, a1, y0, y1, c_ - t / 2, c_ + t / 2, th['wall_mat']); self.hanok_face(alongx, a0, a1, c_, y0, y1, t, put)
        q = bx(a0, a1, c_ - t / 2 + 0.02, c_ + t / 2 - 0.02); self.collide(q[0], q[1], y0, y1, q[2], q[3])

    def hanok_face(self, alongx, a0, a1, c_, y0, y1, t, put):
        """wall kind 'hanok' (cr_kiln, D308-117 C3c): the room faces of the stone wall dressed as the 심벽 of a Joseon 부엌. Drawn only, all within 0.5 m
        of the face (the collider is the wall's own). Over the floor of that side: 고막이 stones to 0.3, 하인방 0.3-0.46, 중인방 1.0-1.15, 상인방 2.0-2.15,
        창방 2.7-2.98; square posts 0.2 on 덤벙주초 stones at bays of about 2.4 m; plaster between; now and then a 살창 in a bay or a 시렁 over the posts.
        Sourced: 심벽 = posts and 인방 framing plaster/clay panels, 살창, 시렁 (encykorea 벽 · 살창 · 부엌). Estimated: every height and size above."""
        import random
        g_ = self.grid[0] if alongx else self.grid[1]; corner = lambda a: g_ > 0 and abs(a / g_ - round(a / g_)) < 1e-6
        hr = random.Random(int(hashlib.md5(f'{self.id}|{alongx}|{a0:.2f}|{a1:.2f}|{c_:.2f}|{y0:.2f}'.encode()).hexdigest()[:8], 16)); wood = 'wood_dark'
        for a_, out in ((a0, -1.0), (a1, 1.0)):      # a doorway jamb: a board over the flat stone end (문설주 lining)
            if not corner(a_): put('Wall', min(a_, a_ + out * 0.04), max(a_, a_ + out * 0.04), y0, y1, c_ - t / 2 - 0.02, c_ + t / 2 + 0.02, wood)
        for sd, f, ry in self.sides:
            face = c_ + sd * t / 2
            def fb(u0, u1, ya, yb, d0, d1, mat):
                ya, yb = max(ya, y0), min(yb, y1, ry)
                if yb - ya > 0.02 and u1 - u0 > 0.02: put('Wall', u0, u1, ya, yb, min(face + sd * d0, face + sd * d1), max(face + sd * d0, face + sd * d1), mat)
            ua = a0 + (t / 2 if corner(a0) else 0.0); ub = a1 - (t / 2 if corner(a1) else 0.0)
            fb(ua, ub, f, ry, 0.0, 0.03, 'plaster'); fb(ua, ub, f, f + 0.3, 0.0, 0.08, 'fieldstone')
            for ya, yb, d in ((0.3, 0.46, 0.12), (1.0, 1.15, 0.11), (2.0, 2.15, 0.11), (2.7, 2.98, 0.16)): fb(ua, ub, f + ya, f + yb, 0.0, d, wood)
            if not (y0 <= f + 0.05 and y1 >= f + 2.9): continue      # a riser or the wall over a door: bands only, no posts
            pa, pb = ua + 0.1, ub - 0.1; n = max(1, round((pb - pa) / 2.4)); us = [pa + (pb - pa) * k / n for k in range(n + 1)]
            for u in us:
                fb(u - 0.1, u + 0.1, f + 0.24, f + 2.98, 0.0, 0.2, wood); w_ = hr.uniform(0.17, 0.22); fb(u - w_, u + w_, f, f + hr.uniform(0.22, 0.3), 0.0, hr.uniform(0.3, 0.4), 'fieldstone')
            if n >= 2 and hr.random() < 0.55:      # 살창: a dark slot behind upright bars in one bay, between the 중인방 and the 상인방
                k = hr.randrange(n); uc = (us[k] + us[k + 1]) / 2; hw = min(0.55, (us[k + 1] - us[k]) / 2 - 0.3)
                fb(uc - hw, uc + hw, f + 1.2, f + 1.95, 0.0, 0.035, 'iron')
                for ya, yb in ((1.15, 1.2), (1.95, 2.0)): fb(uc - hw - 0.05, uc + hw + 0.05, f + ya, f + yb, 0.0, 0.1, wood)
                for e_ in (-1, 1): fb(uc + e_ * hw - (0.05 if e_ > 0 else 0.0), uc + e_ * hw + (0.05 if e_ < 0 else 0.0), f + 1.15, f + 2.0, 0.0, 0.1, wood)
                bu = uc - hw + 0.11
                while bu < uc + hw - 0.05: fb(bu - 0.022, bu + 0.022, f + 1.2, f + 1.95, 0.04, 0.085, wood); bu += 0.12
            elif hr.random() < 0.4:      # 시렁: two poles on brackets from the posts, over head height
                for u in (us[1:-1] or us): fb(u - 0.03, u + 0.03, f + 2.2, f + 2.27, 0.2, 0.48, wood)
                for d in (0.26, 0.42):
                    q0 = (pa + 0.15, face + sd * d); q1 = (pb - 0.15, face + sd * d)
                    P0 = self.w(q0[0], f + 2.3, q0[1]) if alongx else self.w(q0[1], f + 2.3, q0[0]); P1 = self.w(q1[0], f + 2.3, q1[1]) if alongx else self.w(q1[1], f + 2.3, q1[0])
                    self.mesh('Wall').log(P0, P1, 0.035, 0.035, wood)

    def hanok_roof(self, x0, x1, z0, z1, f, ry, t, broken):
        """wall kind 'hanok': the 연등천장 of a 부엌 under the stone roof slab (drawn only). Ridge (종도리) along x over the cell's middle, 대들보 across at the
        inner post lines of the long walls, a 동자주 on each, round rafters (서까래) every 0.33 m from the wall plates (도리, 3.05 m) to the ridge, plastered 앙토
        over them. `broken` (every second cell, the danger form): one 대들보 snapped and sagging to 2.45 m, a few rafters hanging. Sourced: 연등천장, 서까래
        ~15 cm at ~30 cm. Estimated: the heights, the slope (about 22 deg in a 4.7 m room)."""
        import random
        hr = random.Random(int(hashlib.md5(f'{self.id}|roof|{x0:.2f}|{z0:.2f}|{f:.2f}'.encode()).hexdigest()[:8], 16)); wood = 'wood_dark'; M = self.mesh('Roof')
        xa, xb, za, zb = x0 + t / 2, x1 - t / 2, z0 + t / 2, z1 - t / 2; zc = (za + zb) / 2; yp, yr = f + 3.05, ry - 0.18
        slope = lambda z: yp + (yr - yp) * (1 - abs(z - zc) / (zc - za))
        pa, pb = xa + 0.1, xb - 0.1; n = max(1, round((pb - pa) / 2.4)); inner = [pa + (pb - pa) * k / n for k in range(1, n)] or [(xa + xb) / 2]
        M.log(self.w(xa, yr - 0.02, zc), self.w(xb, yr - 0.02, zc), 0.12, 0.12, wood, sides=8)
        for zz in (za + 0.13, zb - 0.13): M.log(self.w(xa, yp - 0.04, zz), self.w(xb, yp - 0.04, zz), 0.11, 0.11, wood, sides=8)
        snap = hr.randrange(len(inner)) if broken else -1
        for k, x in enumerate(inner):
            if k != snap:
                M.box(self.w(x, f + 3.15, zc), self.ax, UP, self.az, (0.28, 0.34, zb - za), wood); M.box(self.w(x, (f + 3.32 + yr) / 2, zc), self.ax, UP, self.az, (0.18, yr - f - 3.32, 0.18), wood)
            else:
                for zz, low in ((za, 2.45), (zb, 2.75)):
                    M.log(self.w(x, f + 3.15, zz), self.w(x + hr.uniform(-0.25, 0.25), f + low, zc + (0.15 if zz == za else -0.25)), 0.15, 0.13, wood, sides=6, rng=hr, wob=0.04)
        xs = list(np.arange(xa + 0.17, xb - 0.1, 0.33)); hang = set(hr.sample(range(len(xs)), 4)) if broken else set()
        for i, x in enumerate(xs):
            for zz in (za, zb):
                if i in hang and zz == za:      # snapped: the low end let go and swung down
                    zt = zc - 0.4; M.log(self.w(x, slope(zt), zt), self.w(x + hr.uniform(-0.3, 0.3), f + hr.uniform(2.35, 2.7), zt - hr.uniform(1.0, 1.6)), 0.065, 0.055, wood, sides=6); continue
                M.log(self.w(x, yp + 0.06, zz), self.w(x, yr + 0.02, zc), 0.065, 0.065, wood, sides=6)
        for zz in (za, zb):
            X, Z = np.meshgrid(np.array([xa, xb]), np.array([zz, zc]), indexing='ij'); Y = yp + 0.14 + (yr + 0.1 - yp - 0.14) * (Z == zc)
            sheet(M, self.W(X, Y, Z), 'plaster', -UP, uvs=3.0)


def dress_cave(K, G, st, chain, rect, level, n, avoid, rng):
    """what stands and lies in a cave dungeon. Common to every realm: broken timber sets along the way, falls of rock at the walls, two lanterns a cell.
    The realm's own (st['features'], D308-62): roots (Cheongrim) | dressed stone, paving, steles, brick (Hwanggyeong) | charred timber, cairns, ash, spear
    shafts (Jeokro) | iron bands, grates, a stone channel, brick (Cheolong) | standing water, dripstone, plank walk (Hyeongang).
    Drawn only, except what a body cannot pass (posts, heaps, floor roots, stalagmites, stones): one collider box each. The way and the rewards stay clear."""
    F = st.get('features', {}); wood = K.th['wood_mat']; rockm = K.th['wall_mat']; ph = 3.0; lamps = []; rocks = []; pools = []; dome = float(G.get('dome_m', 1.6)); avoid = list(avoid)
    p_gone = F.get('post_gone', 0.18); p_snap = F.get('post_snapped', 0.22); step = F.get('frame_step', [2.6, 3.6]); iron = bool(F.get('iron'))
    near = lambda u, v, r: any(math.hypot(u - a_, v - b_) < r for a_, b_ in avoid)
    def fallen(u, v, yl, ln):
        a_ = rng.uniform(0, 2 * math.pi); p0 = K.w(u + rng.uniform(-1, 1), yl + 0.1, v + rng.uniform(-0.9, 0.9)); K.mesh('Prop').log(p0, p0 + (K.ax * math.cos(a_) + K.az * math.sin(a_)) * ln + UP * rng.uniform(-0.02, 0.12), 0.11, 0.09, wood, rng=rng, wob=0.12)
    for ci, cell in enumerate(chain):
        x0, x1, z0, z1 = rect(cell); yl = level[cell]; zc = (z0 + z1) / 2; xc = (x0 + x1) / 2; turn = cell[1] in (0, n - 1); ry = yl + G['roof_h']; area = (x1 - x0) * (z1 - z0)
        way = lambda u, v, m=1.7: abs(v - zc) < m or (turn and abs(u - xc) < m)
        def off_way(m=1.3, clear=1.9):
            for _ in range(14):
                u, v = rng.uniform(x0 + m, x1 - m), rng.uniform(z0 + m, z1 - m)
                if not way(u, v) and not near(u, v, clear): return u, v
            return None
        def wall_side(m=0.9):      # a spot at the foot of a wall, off every doorway line
            k = int(rng.integers(0, 4)); t_ = rng.uniform(0.15, 0.85); t_ += (0.25 if t_ >= 0.5 else -0.25) if abs(t_ - 0.5) < 0.2 else 0.0
            return [(x0 + (x1 - x0) * t_, z0 + m, True), (x0 + (x1 - x0) * t_, z1 - m, True), (x0 + m, z0 + (z1 - z0) * t_, False), (x1 - m, z0 + (z1 - z0) * t_, False)][k]
        ceil = lambda u, v: K.ceil_y(u, v, ry, (x0, x1, z0, z1), dome)
        # ---- two lanterns, on opposite sides of the way (first, so nothing else lands on them)
        ins = min(2.5, (z1 - z0) / 2 - 1.3); far_a, far_b = (z0 + ins, z1 - ins) if cell[0] == 'A' else (z1 - ins, z0 + ins)
        for lx, lz in ((x1 - 3.0, far_a), (x0 + 3.2, far_b)):
            if near(lx, lz, 2.6): lx = xc + (2.6 if lx < xc else -2.6)
            if near(lx, lz, 2.0): continue
            ly = yl + 0.07
            if len(lamps) % 2 == 1: ly = stone(K.mesh('Rubble'), K.w(lx, yl + 0.12, lz), rng.uniform(0.65, 0.95), LOOSE, rng) - K.o[1] - 0.02
            lamps.append(dict(pos=[round(float(v_), 3) for v_ in K.w(lx, ly, lz)], yaw=round(float(rng.uniform(0, 360)), 1))); avoid.append((lx, lz))
        # ---- timber sets along the way
        u = x0 + rng.uniform(1.2, 2.2)
        while u < x1 - 1.0:
            if not (turn and abs(u - xc) < 1.8):
                tops = {}
                for sz in (-1, 1):
                    v = zc + sz * rng.uniform(1.8, 2.1); r = rng.random()
                    if near(u, v, 1.7): continue
                    if r < p_gone:
                        if rng.random() < 0.7: fallen(u, v, yl, ph * rng.uniform(0.6, 1.0))
                    elif r < p_gone + p_snap:
                        hh = rng.uniform(0.7, 1.6); K.mesh('Prop').log(K.w(u, yl - 0.1, v), K.w(u + rng.uniform(-0.2, 0.2), yl + hh, v + rng.uniform(-0.2, 0.2)), 0.12, 0.1, wood, rng=rng, wob=0.14); K.collide(u - 0.13, u + 0.13, yl, yl + hh, v - 0.13, v + 0.13); fallen(u, v, yl, ph - hh)
                    else:
                        l0, l1 = rng.normal(0, 0.3, 2); top = (u + l0, yl + ph + rng.uniform(-0.45, 0.3), v + l1); K.mesh('Prop').log(K.w(u, yl - 0.1, v), K.w(*top), 0.12, 0.1, wood, rng=rng, wob=0.1); K.collide(u - 0.13, u + 0.13, yl, yl + ph, v - 0.13, v + 0.13); tops[sz] = top
                        if iron:
                            for hb in (0.45, 2.35): K.box('Prop', u - 0.15, u + 0.15, yl + hb, yl + hb + 0.07, v - 0.15, v + 0.15, 'iron')
                            if rng.random() < 0.5: K.mesh('Prop').log(K.w(u, yl + 1.9, v), K.w(top[0], top[1] + 0.05, v - sz * 1.0), 0.025, 0.025, 'iron')
                if len(tops) == 2:
                    if rng.random() > 0.15: a_, b_ = np.array(tops[-1]), np.array(tops[1]); e_ = (b_ - a_) / np.linalg.norm(b_ - a_) * 0.3; K.mesh('Prop').log(K.w(*(a_ - e_ + [0, 0.1, 0])), K.w(*(b_ + e_ + [0, 0.1, 0])), 0.11, 0.1, wood, rng=rng, wob=0.08)
                    else: fallen(u, zc, yl, 3.6)
                elif len(tops) == 1:      # the cap hangs from the post that still stands (never below headroom)
                    sz = next(iter(tops)); a_ = np.array(tops[sz]); K.mesh('Prop').log(K.w(*(a_ + [0, 0.1, 0])), K.w(u + rng.uniform(-0.5, 0.5), max(yl + 2.0, a_[1] - rng.uniform(0.4, 1.1)), zc - sz * rng.uniform(1.2, 2.2)), 0.11, 0.1, wood, rng=rng, wob=0.08)
                elif rng.random() < 0.6: fallen(u, zc, yl, 3.4)
            u += rng.uniform(step[0], step[1])
        # ---- falls of rock at the foot of a wall; a few of the realm's own boulders in each
        for _ in range(int(rng.integers(2, 4))):
            hx, hz, _along = wall_side(0.9)
            if near(hx, hz, 2.6): continue
            hr = rng.uniform(1.1, 1.9); hh = rng.uniform(0.7, 1.3)
            for q in range(int(26 * hr * hr)):
                a_ = rng.uniform(0, 2 * math.pi); rad = hr * math.sqrt(rng.random()); top = yl + hh * (1 - (rad / hr) ** 1.5) * rng.uniform(0.0, 1.0); sz_ = (0.3 + 0.6 * rng.random() ** 1.6) * (1.15 - 0.4 * rad / hr)
                stone(K.mesh('Rubble'), K.w(hx + math.cos(a_) * rad, top + sz_ * 0.15, hz + math.sin(a_) * rad), sz_, LOOSE, rng)
            K.collide(hx - hr * 0.55, hx + hr * 0.55, yl, yl + hh * 0.6, hz - hr * 0.55, hz + hr * 0.55); avoid.append((hx, hz))
            for _q in range(int(rng.integers(1, 3)) if st.get('rocks') else 0):
                rocks.append(dict(prefab=str(rng.choice(st['rocks'])), pos=[round(float(v_), 3) for v_ in K.w(hx + rng.uniform(-0.8, 0.8), yl, hz + rng.uniform(-0.8, 0.8))], yaw=round(float(rng.uniform(0, 360)), 1), size=round(float(rng.uniform(0.9, 1.7)), 2)))
        # ---- D308-64 ore: where a vein was cut out of the wall (stubs of ore left along its line, chips under it), ore sorted into a heap,
        # ground in a mortar, burnt out to slag. Nothing glows except a short stretch of vein nobody finished cutting, on the reward's side.
        if st.get('ore'):
            MD = 'Assets/_Project/Art/World/Dungeon308/Materials/'; ore_m = MD + 'CaveOre_' + st['realm']; vein_m = MD + 'CaveOre_Vein_' + st['realm']; powder_m = MD + 'CavePowder_' + st['realm']; slag_m = MD + 'CaveSlag'
            reward_here = x0 <= avoid[0][0] <= x1 and z0 <= avoid[0][1] <= z1
            for run in range(int(rng.integers(1, 3)) + (1 if reward_here else 0)):
                k = int(rng.integers(0, 4)); along = k < 2; lo_, hi_ = (x0, x1) if along else (z0, z1); ln = rng.uniform(2.5, 5.0); half = (hi_ - lo_) / 2
                a0_ = rng.uniform(lo_ + 1.0, lo_ + half - 1.4 - ln) if rng.random() < 0.5 and half - 2.4 - ln > 0 else rng.uniform(lo_ + half + 1.4, max(lo_ + half + 1.5, hi_ - 1.0 - ln))
                live = reward_here and run == 0; yv = yl + rng.uniform(1.0, 1.8); ph_ = rng.uniform(0, 6.0); t_ = 0.0
                while t_ < ln:
                    q_ = a0_ + t_; face = [(q_, z0 + 0.3), (q_, z1 - 0.3), (x0 + 0.3, q_), (x1 - 0.3, q_)][k]; nrm = [(0, 1), (0, -1), (1, 0), (-1, 0)][k]; yy = yv + 0.35 * math.sin(t_ * 1.3 + ph_) + rng.uniform(-0.12, 0.12)
                    amp = float(K.wall_amp(K.w(face[0], yy, face[1])[None])[0]); u, v = face[0] + nrm[0] * (amp - 0.03), face[1] + nrm[1] * (amp - 0.03)
                    if live and t_ < 1.6: stone(K.mesh('Ore_Vein'), K.w(u, yy, v), rng.uniform(0.14, 0.3), vein_m, rng)
                    elif rng.random() < 0.45: stone(K.mesh('Ore'), K.w(u, yy, v), rng.uniform(0.07, 0.18), ore_m, rng)
                    if rng.random() < 0.6: d_ = rng.uniform(0.25, 1.0); stone(K.mesh('Ore'), K.w(face[0] + nrm[0] * (amp + d_), yl + 0.05, face[1] + nrm[1] * (amp + d_)), rng.uniform(0.05, 0.14), ore_m, rng)
                    t_ += rng.uniform(0.18, 0.4)
            if rng.random() < float(st['ore']):      # sorted ore, a basket beside it
                sp_ = off_way(1.6, 1.8)
                if sp_ is not None:
                    u, v = sp_
                    for q in range(30): a_ = rng.uniform(0, 2 * math.pi); rad = 0.7 * math.sqrt(rng.random()); sz_ = rng.uniform(0.14, 0.32); stone(K.mesh('Ore'), K.w(u + math.cos(a_) * rad, yl + 0.45 * (1 - rad / 0.7) * rng.random() + sz_ * 0.3, v + math.sin(a_) * rad), sz_, ore_m, rng)
                    bu, bv = u + rng.uniform(0.9, 1.2), v + rng.uniform(-0.4, 0.4); K.mesh('Prop').log(K.w(bu, yl, bv), K.w(bu + 0.03, yl + 0.42, bv), 0.3, 0.36, 'wood_board', sides=9)
                    for q in range(6): stone(K.mesh('Ore'), K.w(bu + rng.uniform(-0.18, 0.18), yl + 0.42 + rng.uniform(0.0, 0.08), bv + rng.uniform(-0.18, 0.18)), rng.uniform(0.12, 0.2), ore_m, rng)
                    K.collide(u - 0.5, u + 0.5, yl, yl + 0.4, v - 0.5, v + 0.5); avoid.append((u, v))
            if rng.random() < float(st['ore']) * 0.7:      # ground to powder: a stone mortar, its pestle, the stain around it
                sp_ = off_way(1.4, 1.6)
                if sp_ is not None:
                    u, v = sp_; K.mesh('Prop').log(K.w(u, yl, v), K.w(u, yl + 0.42, v), 0.34, 0.3, 'stone_rough', sides=10); K.mesh('Prop').log(K.w(u + 0.05, yl + 0.3, v), K.w(u + 0.5, yl + 0.95, v + 0.25), 0.05, 0.035, 'wood_dark')
                    K.mesh('Ore').slab(K.w(u + rng.uniform(-0.2, 0.2), yl + 0.105, v + rng.uniform(-0.2, 0.2)), K.ax, K.az, rng.uniform(0.7, 1.1), rng.uniform(0.5, 0.9), 0.03, powder_m, rng, tilt=0.0, sides=(7, 10))
                    K.collide(u - 0.3, u + 0.3, yl, yl + 0.45, v - 0.3, v + 0.3); avoid.append((u, v))
            if rng.random() < float(st['ore']) * 0.8:      # burnt-out ore: grey slag swept against the wall, soot under it
                hx, hz, _al = wall_side(1.1)
                if not near(hx, hz, 1.6):
                    K.mesh('Ore').slab(K.w(hx, yl + 0.1, hz), K.ax, K.az, 1.1, 0.9, 0.03, 'tile_dark', rng, tilt=0.0, sides=(7, 10))
                    for q in range(22): a_ = rng.uniform(0, 2 * math.pi); rad = 0.6 * math.sqrt(rng.random()); sz_ = rng.uniform(0.08, 0.24); stone(K.mesh('Ore'), K.w(hx + math.cos(a_) * rad, yl + 0.25 * (1 - rad / 0.6) * rng.random() + sz_ * 0.3, hz + math.sin(a_) * rad), sz_, slag_m, rng)
        # ---- rock fills the corners and stands out of the walls in ribs: the room is not a box
        def mass(u, v, top_y, s0, s1):
            yy = yl - 0.3
            while yy < top_y:
                sz_ = rng.uniform(s0, s1); js = 0.35 if K.organic > 0 else 1.0; stone(K.mesh('Rubble'), K.w(u + rng.uniform(-0.3, 0.3) * js, yy + sz_ * 0.35, v + rng.uniform(-0.3, 0.3) * js), sz_, rockm, rng); yy += sz_ * rng.uniform(0.45, 0.65)
        for cx_, cz_ in ((x0 + 0.8, z0 + 0.8), (x1 - 0.8, z0 + 0.8), (x0 + 0.8, z1 - 0.8), (x1 - 0.8, z1 - 0.8)):
            if near(cx_, cz_, 2.3) or rng.random() < 0.15: continue
            mass(cx_, cz_, ceil(cx_, cz_) + 0.3, 1.7, 2.7); K.collide(cx_ - 0.75, cx_ + 0.75, yl, yl + 2.4, cz_ - 0.75, cz_ + 0.75); avoid.append((cx_, cz_))
        for _ in range(int(rng.integers(2, 5))):
            hx, hz, _al = wall_side(0.55)
            if near(hx, hz, 2.0): continue
            mass(hx, hz, min(ceil(hx, hz) + 0.3, yl + rng.uniform(2.2, 6.0)), 1.1, 1.9); K.collide(hx - 0.5, hx + 0.5, yl, yl + 2.2, hz - 0.5, hz + 0.5); avoid.append((hx, hz))
        for ex_, ez_, alx in ((xc, z0, True), (xc, z1, True), (x0, zc, False), (x1, zc, False)):      # rock stands proud beside every doorway line: no flat jamb in sight
            for sd_ in (-1, 1):
                mu, mv = (ex_ + sd_ * rng.uniform(1.8, 2.3), ez_ + (0.55 if ez_ == z0 else -0.55)) if alx else (ex_ + (0.55 if ex_ == x0 else -0.55), ez_ + sd_ * rng.uniform(1.8, 2.3))
                if not near(mu, mv, 1.3): mass(mu, mv, min(ceil(mu, mv) + 0.3, yl + rng.uniform(2.6, 5.5)), 0.9, 1.5)
        # ---- what holds the roof for now: tall props wedged under it, raking shores against the wall, a leaning crib of logs with a block on top
        for _ in range(int(rng.integers(2, 4))):
            sp_ = off_way(1.6, 1.7)
            if sp_ is None: continue
            u, v = sp_; tu, tv = u + rng.uniform(-0.55, 0.55), v + rng.uniform(-0.55, 0.55); ty = ceil(tu, tv) + 0.1
            K.mesh('Prop').log(K.w(u, yl - 0.1, v), K.w(tu, ty, tv), 0.14, 0.11, wood, rng=rng, wob=0.12); stone(K.mesh('Rubble'), K.w(tu, ty - 0.1, tv), rng.uniform(0.4, 0.7), LOOSE, rng)
            K.collide(u - 0.16, u + 0.16, yl, yl + 2.2, v - 0.16, v + 0.16); avoid.append((u, v))
        for _ in range(int(rng.integers(2, 4))):
            hx, hz, along = wall_side(0.5)
            if near(hx, hz, 1.6): continue
            out_ = np.array([0.0, 1.0 if hz < zc else -1.0]) if along else np.array([1.0 if hx < xc else -1.0, 0.0]); reach_ = rng.uniform(1.6, 2.6); fx, fz = hx + out_[0] * reach_, hz + out_[1] * reach_
            if way(fx, fz, 1.2) or near(fx, fz, 1.3): continue
            K.mesh('Prop').log(K.w(fx, yl - 0.1, fz), K.w(hx + rng.uniform(-0.3, 0.3), yl + rng.uniform(2.4, 3.6), hz + rng.uniform(-0.3, 0.3)), 0.12, 0.1, wood, rng=rng, wob=0.1); stone(K.mesh('Rubble'), K.w(fx, yl + 0.1, fz), rng.uniform(0.35, 0.55), LOOSE, rng)
            K.collide(min(fx, hx) - 0.12, max(fx, hx) + 0.12, yl, yl + 1.1, min(fz, hz) - 0.12, max(fz, hz) + 0.12)
        if rng.random() < 0.7:
            sp_ = off_way(1.9, 2.1)
            if sp_ is not None:
                u, v = sp_; top_y = min(ceil(u, v) - 0.9, yl + 3.6); yy = yl; du, dv = 0.0, 0.0; k = 0; drift = rng.uniform(-0.035, 0.035, 2)
                while yy < top_y:
                    for o_ in (-0.42, 0.42):
                        j_ = rng.uniform(-0.08, 0.08, 4); a_, b_ = ((u + du - 0.7 + j_[0], v + dv + o_ + j_[1]), (u + du + 0.7 + j_[2], v + dv + o_ + j_[3])) if k % 2 == 0 else ((u + du + o_ + j_[0], v + dv - 0.7 + j_[1]), (u + du + o_ + j_[2], v + dv + 0.7 + j_[3]))
                        K.mesh('Prop').log(K.w(a_[0], yy + 0.11, a_[1]), K.w(b_[0], yy + 0.11 + rng.uniform(-0.03, 0.03), b_[1]), 0.11, 0.1, wood, rng=rng, wob=0.08)
                    yy += 0.21; du += drift[0]; dv += drift[1]; k += 1
                stone(K.mesh('Rubble'), K.w(u + du, yy + 0.45, v + dv), rng.uniform(1.1, 1.5), rockm, rng); K.collide(u - 0.7, u + 0.7, yl, yl + 2.4, v - 0.7, v + 0.7); avoid.append((u, v))
        for _ in range(int(area * 0.55)):      # loose stone at the foot of the walls
            t_ = rng.random(); d_ = rng.uniform(0.5, 2.4) * rng.random(); k = int(rng.integers(0, 4)); u, v = [(x0 + (x1 - x0) * t_, z0 + 0.6 + d_), (x0 + (x1 - x0) * t_, z1 - 0.6 - d_), (x0 + 0.6 + d_, z0 + (z1 - z0) * t_), (x1 - 0.6 - d_, z0 + (z1 - z0) * t_)][k]
            if not way(u, v, 1.1): stone(K.mesh('Rubble'), K.w(u, yl + 0.06, v), rng.uniform(0.1, 0.38), LOOSE, rng)
        # ---- Cheongrim: roots come down through the roof; the thick ones reach the floor
        for _ in range(int(F.get('roots', 0) * area / 12)):
            u, v = rng.uniform(x0 + 0.8, x1 - 0.8), rng.uniform(z0 + 0.8, z1 - 0.8); top = ceil(u, v) + 0.3; reach = rng.random() < 0.3 and not way(u, v) and not near(u, v, 1.8)
            end = yl - 0.1 if reach else max(yl + 2.3, top - rng.uniform(0.8, 3.6)); r0 = rng.uniform(0.09, 0.2) if reach else rng.uniform(0.03, 0.1)
            if top - end < 0.4: continue
            k = 5; pt = np.array([u, top, v]); pts = [pt]
            for q in range(1, k + 1): pt = np.array([pt[0] + rng.uniform(-0.22, 0.22), top + (end - top) * q / k, pt[2] + rng.uniform(-0.22, 0.22)]); pts.append(pt)
            for q in range(k): K.mesh('Prop').log(K.w(*pts[q]), K.w(*pts[q + 1]), r0 * (1 - 0.55 * q / k), r0 * (1 - 0.55 * (q + 1) / k), 'wood_dark', rng=rng, wob=0.18)
            for q in range(int(rng.integers(0, 3))): m_ = pts[int(rng.integers(1, k))]; K.mesh('Prop').log(K.w(*m_), K.w(m_[0] + rng.uniform(-0.9, 0.9), m_[1] - rng.uniform(0.3, 1.0), m_[2] + rng.uniform(-0.9, 0.9)), r0 * 0.4, 0.012, 'wood_dark', rng=rng, wob=0.2)
            if reach: K.collide(u - 0.25, u + 0.25, yl, yl + 2.2, v - 0.25, v + 0.25); avoid.append((u, v))
        # ---- Hwanggyeong: dressed stone - a stone gate over the way, paving, steles, a stretch of brick
        if rng.random() < F.get('stone_gate', 0):
            u = rng.uniform(x0 + 2.6, x1 - 2.6)
            if not (turn and abs(u - xc) < 2.3) and not near(u, zc - 1.7, 1.6) and not near(u, zc + 1.7, 1.6):
                for sz in (-1, 1): K.box('Prop', u - 0.22, u + 0.22, yl, yl + 2.75, zc + sz * 1.7 - 0.22, zc + sz * 1.7 + 0.22, 'stone_dressed', collide=True)
                K.box('Prop', u - 0.27, u + 0.27, yl + 2.75, yl + 3.1, zc - 2.2, zc + 2.2 - (1.3 if rng.random() < 0.3 else 0.0), 'stone_dressed')
        for _ in range(int(F.get('paving', 0) * area / 8)):
            u = rng.uniform(x0 + 0.8, x1 - 0.8); v = zc + rng.uniform(-1.6, 1.6); K.mesh('Prop').slab(K.w(u, yl + 0.12, v), K.ax, K.az, rng.uniform(0.4, 0.8), rng.uniform(0.35, 0.6), 0.12, LOOSE, rng, tilt=0.03)
        for _ in range(int(F.get('steles', 0))):
            sp_ = off_way()
            if sp_ is None: continue
            u, v = sp_; hs = rng.uniform(1.1, 1.7)
            if rng.random() < 0.55: K.box('Prop', u - 0.36, u + 0.36, yl, yl + 0.2, v - 0.2, v + 0.2, 'stone_dressed'); K.box('Prop', u - 0.26, u + 0.26, yl + 0.2, yl + 0.2 + hs, v - 0.08, v + 0.08, 'stone_dressed'); K.collide(u - 0.3, u + 0.3, yl, yl + hs, v - 0.15, v + 0.15)
            else: K.box('Prop', u - 0.26, u + 0.26, yl + 0.04, yl + 0.2, v - hs / 2, v + hs / 2, 'stone_dressed')
            avoid.append((u, v))
        for _ in range(int(F.get('brick', 0))):
            hx, hz, along = wall_side(1.0)
            if near(hx, hz, 2.2): continue
            ln = rng.uniform(1.6, 3.0); hb = rng.uniform(0.6, 1.5); q = (hx - ln / 2, hx + ln / 2, hz - 0.2, hz + 0.2) if along else (hx - 0.2, hx + 0.2, hz - ln / 2, hz + ln / 2)
            K.box('Prop', q[0], q[1], yl, yl + hb, q[2], q[3], 'brick_dark', collide=True); K.box('Prop', q[0] + 0.3, q[1] - 0.5, yl + hb, yl + hb + 0.25, q[2], q[3], 'brick_dark'); avoid.append((hx, hz))
        # ---- Jeokro: what the fire left - cairns, ash, spear shafts, stubs of a palisade
        for _ in range(int(F.get('cairns', 0))):
            sp_ = off_way()
            if sp_ is None: continue
            u, v = sp_; yy = yl
            for q in range(int(rng.integers(4, 8))): hh = rng.uniform(0.12, 0.26); K.mesh('Prop').slab(K.w(u + rng.uniform(-0.08, 0.08), yy + hh, v + rng.uniform(-0.08, 0.08)), K.ax, K.az, rng.uniform(0.3, 0.55) * (1 - q * 0.09), rng.uniform(0.25, 0.45) * (1 - q * 0.09), hh, LOOSE, rng, tilt=0.1); yy += hh
            K.collide(u - 0.3, u + 0.3, yl, yy, v - 0.3, v + 0.3); avoid.append((u, v))
        for _ in range(int(F.get('ash', 0) * area / 30)):
            u, v = rng.uniform(x0 + 1.5, x1 - 1.5), rng.uniform(z0 + 1.5, z1 - 1.5); r_ = rng.uniform(0.7, 1.6); K.mesh('Prop').slab(K.w(u, yl + 0.105, v), K.ax, K.az, r_, r_ * rng.uniform(0.6, 1.0), 0.03, 'tile_dark', rng, tilt=0.0, sides=(7, 10))
        for _ in range(int(F.get('spears', 0))):
            sp_ = off_way(1.0, 1.2)
            if sp_ is None: continue
            u, v = sp_; tip = K.w(u + rng.uniform(-0.5, 0.5), yl + rng.uniform(1.5, 2.1), v + rng.uniform(-0.5, 0.5)); K.mesh('Prop').log(K.w(u, yl - 0.1, v), tip, 0.022, 0.02, wood); K.mesh('Prop').box(tip, K.ax, UP, K.az, (0.05, 0.22, 0.02), 'iron')
        for _ in range(int(F.get('stubs', 0))):
            hx, hz, along = wall_side(1.3)
            if near(hx, hz, 2.2): continue
            cnt = int(rng.integers(4, 8)); top_ = 0.0
            for q in range(cnt):
                o_ = (q - cnt / 2) * 0.3; hh = rng.uniform(0.4, 1.5); top_ = max(top_, hh); uu, vv = (hx + o_, hz) if along else (hx, hz + o_); K.mesh('Prop').log(K.w(uu, yl - 0.1, vv), K.w(uu + rng.uniform(-0.1, 0.1), yl + hh, vv + rng.uniform(-0.1, 0.1)), 0.11, 0.08, wood, rng=rng, wob=0.16)
            q = (hx - cnt * 0.15, hx + cnt * 0.15, hz - 0.12, hz + 0.12) if along else (hx - 0.12, hx + 0.12, hz - cnt * 0.15, hz + cnt * 0.15); K.collide(q[0], q[1], yl, yl + top_ * 0.8, q[2], q[3]); avoid.append((hx, hz))
        # ---- Cheolong: a stone channel beside the way, iron grates against the wall
        if F.get('channel'):
            vch = zc + min(2.9, (z1 - z0) / 2 - 1.0); uu = x0 + 0.6
            while uu < x1 - 1.6:
                if not near(uu + 0.6, vch, 1.6) and not (turn and abs(uu + 0.6 - xc) < 1.8) and rng.random() < 0.88:
                    for o_ in (-0.45, 0.25): K.box('Prop', uu, uu + 1.15, yl, yl + rng.uniform(0.16, 0.22), vch + o_, vch + o_ + 0.2, 'stone_dressed')
                    K.box('Prop', uu, uu + 1.15, yl + 0.02, yl + 0.07, vch - 0.25, vch + 0.25, 'tile_dark')
                uu += 1.2
        for _ in range(int(F.get('grates', 0))):
            hx, hz, along = wall_side(0.75)
            if near(hx, hz, 1.8): continue
            for q in range(6):
                o_ = -0.7 + q * 0.28; K.box('Prop', *((hx + o_ - 0.02, hx + o_ + 0.02, yl, yl + 2.0, hz - 0.02, hz + 0.02) if along else (hx - 0.02, hx + 0.02, yl, yl + 2.0, hz + o_ - 0.02, hz + o_ + 0.02)), 'iron')
            for hb in (0.25, 1.85): K.box('Prop', *((hx - 0.74, hx + 0.74, yl + hb, yl + hb + 0.05, hz - 0.03, hz + 0.03) if along else (hx - 0.03, hx + 0.03, yl + hb, yl + hb + 0.05, hz - 0.74, hz + 0.74)), 'iron')
        # ---- Hyeongang: water stands in the low places of the floor; dripstone; planks over the wet way
        if F.get('water'):
            pools.append(dict(centre=[round(float(v_), 3) for v_ in K.w(xc, yl + float(F['water']), zc)], size=[round(x1 - x0 - 1.5, 2), round(z1 - z0 - 1.5, 2)], yaw=K.yaw))
            uu = x0 + 0.8
            while uu < x1 - 0.8:
                if rng.random() < 0.7 and not (turn and abs(uu - xc) < 1.2): K.box('Prop', uu, uu + 0.26, yl + 0.1, yl + 0.14, zc - 0.8 + rng.uniform(-0.15, 0.15), zc + 0.8 + rng.uniform(-0.15, 0.15), wood)
                uu += 0.34
        for _ in range(int(F.get('stalactites', 0) * area / 10)):
            u, v = rng.uniform(x0 + 0.8, x1 - 0.8), rng.uniform(z0 + 0.8, z1 - 0.8); top = ceil(u, v) + 0.25; tip = max(yl + 2.3, top - rng.uniform(0.5, 2.4))
            if top - tip > 0.3: K.mesh('Prop').log(K.w(u, top, v), K.w(u + rng.uniform(-0.05, 0.05), tip, v + rng.uniform(-0.05, 0.05)), rng.uniform(0.1, 0.24), 0.015, rockm, rng=rng, wob=0.15)
        for _ in range(int(F.get('stalagmites', 0))):
            sp_ = off_way(1.0, 1.6)
            if sp_ is None: continue
            u, v = sp_; hh = rng.uniform(0.5, 1.7); K.mesh('Prop').log(K.w(u, yl - 0.1, v), K.w(u + rng.uniform(-0.06, 0.06), yl + hh, v + rng.uniform(-0.06, 0.06)), rng.uniform(0.2, 0.32), 0.03, rockm, rng=rng, wob=0.15); K.collide(u - 0.18, u + 0.18, yl, yl + hh * 0.7, v - 0.18, v + 0.18); avoid.append((u, v))
    return lamps, rocks, pools


def build_one(cfg, D, site, f):
    th = dict(cfg['themes'][D['theme']]); G = dict(cfg['grammar'], **D.get('grammar', {})); cave = bool(D.get('cave')); rng = np.random.default_rng(seed_of(cfg['seed'], D['id']))
    n = D['cols']; cw, cd = D['cell']; t = G['wall_t']; dw = G['door_w']; wall_h = G['wall_h']; led = G['ledge_m']
    W_ = n * cw; Dp = 2 * cd; under = bool(G.get('underground')); sol = None
    if under:
        sol = solve(cfg, D, site['x'], site['z'], site['yaw'], f)
        if sol is None: raise SystemExit(f"{D['id']}: the stair does not surface at the planned site (plan again)")
        ax, az, o2 = sol['ax'], sol['az'], sol['o2']; site = dict(site, yaw=sol['yaw']); th['roof_mat'] = th['cope_mat']
        if th['wall'] == 'palisade': th['wall'] = 'earth'; th['wall_mat'] = 'fieldstone'
        wall_h = G['roof_h'] + 0.18      # walls meet the rock ceiling
    else:
        c, s = math.cos(math.radians(site['yaw'])), math.sin(math.radians(site['yaw'])); ax = np.array([c, -s]); az = np.array([s, c]); o2 = np.array([site['x'], site['z']]) - ax * W_ / 2 - az * Dp / 2      # local origin = the front left corner
    gs = lambda x, z: float(B.sample_tri(f, *(o2 + ax * x + az * z)))
    samples = [gs(x, z) for x in np.linspace(-1, W_ + 1, 9) for z in np.linspace(-1, Dp + 1, 9)]
    y0 = sol['y0'] if under else max(samples) + G['podium_lift']; yb = y0 - 0.6 if under else min(samples) - 1.0
    style = next((c_ for c_ in cfg.get('caveStyles', []) if c_['realm'] == D['realm']), {}) if cave else {}
    if cave: th.update(CAVE); th.update(wall_mat=style.get('rockMaterial', ROCK), roof_mat=style.get('rockMaterial', ROCK), wood_mat=style.get('woodMaterial', TIMBER))
    K = Kit(D['id'], [o2[0], y0, o2[1]], site['yaw'], rng, th); K.cave = cave; K.seed = seed_of(cfg['seed'], D['id'], 'rock') % 100000; K.roof_amp = float(G.get('roof_amp_m', 1.3)); K.bulge = float(G.get('bulge_m', 1.0)); K.organic = float(G.get('organic', 0.0)); K.grid = (cw, cd); K.wall_h = wall_h; K.dome = float(G.get('dome_m', 1.6)); K.ax = np.array([ax[0], 0.0, ax[1]]); K.az = np.array([az[0], 0.0, az[1]])
    # ---- the chain of cells and the level of each
    feats = D['features']; assert len(feats) == 2 * n - 1, f"{D['id']}: features must have {2 * n - 1} entries"
    chain = [('A', k) for k in range(n)] + [('B', k) for k in range(n - 1, -1, -1)]; level = {chain[0]: 0.0}; lv = 0.0
    for k, ft in enumerate(feats):
        if ft == 'lift': lv += led
        elif ft == 'drop': lv = 0.0
        level[chain[k + 1]] = lv
    if lv != 0.0: raise SystemExit(f"{D['id']}: the way must be back on the entry level at B1 (add a drop)")
    rect = lambda cell: (cell[1] * cw, (cell[1] + 1) * cw, 0.0 if cell[0] == 'A' else cd, cd if cell[0] == 'A' else Dp)
    roofed = set(tuple(x) for x in D.get('roofed', []))
    for cell in chain:      # floor blocks down to under the lowest ground: the podium
        x0, x1, z0, z1 = rect(cell)
        if cave: K.collide(x0, x1, yb - y0, level[cell], z0, z1); K.cave_floor(x0, x1, level[cell], z0, z1, th['floor_mat'])
        else: K.box('Floor', x0, x1, yb - y0, level[cell], z0, z1, th['floor_mat'], collide=True)
        if under or (cell[0], cell[1]) in roofed or D.get('all_roofed'):
            ry = level[cell] + G['roof_h']; K.box('Roof', x0 - 0.3, x1 + 0.3, ry, ry + 0.18, z0 - 0.3, z1 + 0.3, th['roof_mat']); K.collide(x0, x1, ry, ry + 0.18, z0, z1)
            if cave: K.cave_roof(x0, x1, ry, z0, z1, th['roof_mat'], float(G.get('dome_m', 1.6))); continue
            hanok = th['wall'] == 'hanok'; junk = Mesh('_discard')      # hanok: the slab's log draws still taken (into nothing), then the 연등천장
            if hanok: K.hanok_roof(x0, x1, z0, z1, level[cell], ry, t, chain.index(cell) % 2 == 1)
            for u in np.arange(x0 + 0.5, x1, rng.uniform(1.4, 2.0)): (junk if hanok else K.mesh('Roof')).log(K.w(u, ry - 0.12, z0 - 0.4), K.w(u + rng.uniform(-0.1, 0.1), ry - 0.12, z1 + 0.4), 0.09, 0.08, th['cope_mat'], rng=rng, wob=0.08)
    # ---- walls: every cell edge, with a doorway where the way passes
    conn = {}
    for k, ft in enumerate(feats): conn[frozenset((chain[k], chain[k + 1]))] = ft
    conn[frozenset((('A', 0), ('B', 0)))] = 'door'; gates = []; ledges = []; door = None
    def edge(a, b, x0, z0, x1, z1):
        """wall between cells a and b (b may be None = outside) along the segment; a doorway in its middle when they connect."""
        top = max(level[a], level[b] if b else 0.0) + wall_h; bot = yb - y0 if b is None else min(level[a], level[b])
        ft = conn.get(frozenset((a, b))) if b else None; entrance = b is None and a == ('A', 0) and z0 == 0.0 and z1 == 0.0
        if th['wall'] == 'hanok':      # which side of the wall faces which room: its floor and roof (D308-117 C3c)
            ax_ = abs(x1 - x0) > abs(z1 - z0); c0 = z0 if ax_ else x0; K.sides = []
            for cell in (a, b):
                if cell is None: continue
                rc = rect(cell); mid_ = (rc[2] + rc[3]) / 2 if ax_ else (rc[0] + rc[1]) / 2; K.sides.append((1 if mid_ > c0 else -1, level[cell], level[cell] + G['roof_h']))
        if not ft and not entrance: K.wall(x0, z0, x1, z1, bot, top, t); return
        K.jamb = bool(ft) and ft != 'door'      # grammar organic: round the jambs of the open ways (never a door's: its leaves close on them)
        alongx = abs(x1 - x0) > abs(z1 - z0); mid = (x0 + x1) / 2 if alongx else (z0 + z1) / 2
        if alongx: K.wall(x0, z0, mid - dw / 2, z1, bot, top, t); K.wall(mid + dw / 2, z0, x1, z1, bot, top, t)
        else: K.wall(x0, z0, x1, mid - dw / 2, bot, top, t); K.wall(x0, mid + dw / 2, x1, z1, bot, top, t)
        pos = (mid, z0) if alongx else (x0, mid); lo = min(level[a], level[b]) if b else 0.0; hi = max(level[a], level[b]) if b else 0.0
        if hi > lo: K.wall(*( (mid - dw / 2, z0, mid + dw / 2, z1) if alongx else (x0, mid - dw / 2, x1, mid + dw / 2) ), bot, hi, t)      # the riser of a ledge fills the doorway up to the upper floor
        K.jamb = False
        if th['wall'] == 'hanok' and ft and ft != 'door' and hi == lo:      # an open way through a 심벽: a lintel at 2.25 m and plaster over it (drawn only)
            ua_, ub_ = mid - dw / 2 - 0.1, mid + dw / 2 + 0.1; cc_ = z0 if alongx else x0
            for (ya_, yb_, d_, m_) in ((lo + 2.25, lo + 2.45, t / 2 + 0.06, 'wood_dark'), (lo + 2.45, top, t / 2 - 0.02, 'plaster')):
                q_ = (ua_, ub_, cc_ - d_, cc_ + d_) if alongx else (cc_ - d_, cc_ + d_, ua_, ub_); K.box('Wall', q_[0], q_[1], ya_, yb_, q_[2], q_[3], m_)
        if cave and K.organic > 0 and ft and ft != 'door': K.cave_arch(alongx, mid, z0 if alongx else x0, hi, top, t, dw)
        rec = dict(a=list(a), b=list(b) if b else None, feature=ft or 'entrance', pos=[round(float(v), 3) for v in K.w(pos[0], lo, pos[1])], yaw=round(site['yaw'] + (0.0 if alongx else 90.0), 1), width=dw, across='x' if alongx else 'z', lo=lo, hi=hi)
        if ft in ('vine', 'boulder', 'taint'): gates.append(rec)
        elif ft == 'lift' or ft == 'drop': ledges.append(rec)
        elif ft == 'door':      # the wall goes on above the leaves: nobody gets over the door with a lift
            K.wall(*((mid - dw / 2, z0, mid + dw / 2, z1) if alongx else (x0, mid - dw / 2, x1, mid + dw / 2)), lo + G['door_h'], top, t); nonlocal_door.append(rec)
    nonlocal_door = []
    for k in range(n):
        A, Bc = ('A', k), ('B', k)
        edge(A, None, k * cw, 0.0, (k + 1) * cw, 0.0); edge(Bc, None, k * cw, Dp, (k + 1) * cw, Dp); edge(A, Bc, k * cw, cd, (k + 1) * cw, cd)
        if k == 0: edge(A, None, 0.0, 0.0, 0.0, cd); edge(Bc, None, 0.0, cd, 0.0, Dp)
        if k == n - 1: edge(A, None, W_, 0.0, W_, cd); edge(Bc, None, W_, cd, W_, Dp)
        else: edge(A, ('A', k + 1), (k + 1) * cw, 0.0, (k + 1) * cw, cd); edge(Bc, ('B', k + 1), (k + 1) * cw, cd, (k + 1) * cw, Dp)
    door = nonlocal_door[0] if nonlocal_door else None
    # ---- entrance steps down the uphill front (if the podium stands above the ground there)
    ex = cw / 2; ge = gs(ex, -2.0); rise = y0 - ge; steps = 0; holes = []
    if under:      # the way down: an open stair pit where the hill is thin, then a stair tunnel, to the door of A1
        d = sol['run']; yl = sol['yl'] - y0; steps = sol['steps']; tread = d / steps; side = 2.0; inner = 1.2
        for k in range(steps):
            z1 = -t / 2 - k * tread if k else 0.0; z0 = -(k + 1) * tread - (t / 2 if k < steps - 1 else 0.0); z0 = max(z0, -d)
            K.box('Floor', ex - inner, ex + inner, -0.6, (k + 1) * yl / steps, min(z0, z1 - 0.05), z1, th['floor_mat'], collide=True)
        for (i_, j_, hole, z_far, z_near) in sol['cells']:
            y_far = yl * (-z_far) / d; y_near = yl * (-z_near) / d; ground_top = float(f[i_:i_ + 2, j_:j_ + 2].max()) - y0
            top = ground_top + G['curb_m'] if hole else y_far + G['head_m'] + 0.3
            for x0_, x1_ in ((ex - side, ex - inner), (ex + inner, ex + side)): K.box('Wall', x0_, x1_, y_near - 0.6, top, z_far, z_near, th['wall_mat'], collide=True)
            if hole: holes.append([int(i_), int(j_)])
            else: K.box('Roof', ex - side, ex + side, y_far + G['head_m'], y_far + G['head_m'] + 0.3, z_far, z_near, th['cope_mat'], collide=True)
        for u in np.arange(1.0, d, 2.2):      # timber sets down the stair
            yy = yl * u / d
            if any((not c_[2]) and c_[3] <= -u <= c_[4] for c_ in sol['cells']):
                for sx in (-inner + 0.1, inner - 0.1): K.mesh('Prop').log(K.w(ex + sx, yy - 0.2, -u), K.w(ex + sx * 0.92, yy + G['head_m'] - 0.25, -u), 0.1, 0.085, th['wood_mat'], rng=rng, wob=0.1)
                K.mesh('Prop').log(K.w(ex - inner, yy + G['head_m'] - 0.2, -u), K.w(ex + inner, yy + G['head_m'] - 0.2 + rng.uniform(-0.06, 0.06), -u), 0.1, 0.09, th['wood_mat'], rng=rng, wob=0.1)
        # D308-67: the way down must be seen from the field - a tall head frame over the top of the stair, spoil thrown out beside it, a lantern
        gtop = lambda u, v: gs(u, v) - y0; fy = max(gtop(ex - 1.9, -d + 0.4), gtop(ex + 1.9, -d + 0.4)); wood_ = th['wood_mat']; tops_ = []
        for sx in (-1.9, 1.9):
            base_ = gtop(ex + sx, -d + 0.4); top_ = (ex + sx * 0.82 + rng.uniform(-0.1, 0.1), fy + 4.3 + rng.uniform(-0.2, 0.2), -d + 0.4 + rng.uniform(-0.15, 0.15)); tops_.append(top_)
            K.mesh('Mark').log(K.w(ex + sx, base_ - 0.3, -d + 0.4), K.w(*top_), 0.16, 0.12, wood_, rng=rng, wob=0.1); K.collide(ex + sx - 0.17, ex + sx + 0.17, base_, base_ + 2.4, -d + 0.23, -d + 0.57)
            K.mesh('Mark').log(K.w(ex + sx * 1.9, gtop(ex + sx * 1.9, -d + 1.6) - 0.2, -d + 1.6), K.w(ex + sx * 0.9, fy + 3.0, -d + 0.45), 0.1, 0.08, wood_, rng=rng, wob=0.1)      # a raking strut
        K.mesh('Mark').log(K.w(tops_[0][0] - 0.5, tops_[0][1] + 0.12, tops_[0][2]), K.w(tops_[1][0] + 0.5, tops_[1][1] + 0.12, tops_[1][2]), 0.15, 0.13, wood_, rng=rng, wob=0.08)
        K.mesh('Mark').log(K.w(ex, tops_[0][1] + 0.1, -d + 0.4), K.w(ex + 0.05, tops_[0][1] - 1.1, -d + 0.4), 0.02, 0.02, wood_)      # the rope of the hoist
        hx_, hz_ = ex + rng.choice([-1, 1]) * rng.uniform(3.6, 4.6), -d + rng.uniform(-1.5, 1.0); hb_ = gtop(hx_, hz_)
        for q in range(46): a_ = rng.uniform(0, 2 * math.pi); rad = 1.9 * math.sqrt(rng.random()); sz_ = 0.3 + 0.7 * rng.random() ** 1.6; stone(K.mesh('Mark'), K.w(hx_ + math.cos(a_) * rad, gtop(hx_ + math.cos(a_) * rad, hz_ + math.sin(a_) * rad) + 0.9 * (1 - rad / 1.9) * rng.random() + sz_ * 0.2, hz_ + math.sin(a_) * rad), sz_, LOOSE, rng)
        mark_lamp = dict(pos=[round(float(v_), 3) for v_ in K.w(ex - 2.6, gtop(ex - 2.6, -d - 0.6) + 0.04, -d - 0.6)], yaw=round(float(rng.uniform(0, 360)), 1))
    elif rise > 0.2:
        steps = int(math.ceil(rise / G['step_rise']))
        for k in range(steps):
            top = -(k + 1) * rise / steps; z1 = -t / 2 - k * G['step_tread']; K.box('Floor', ex - dw / 2 - 0.3, ex + dw / 2 + 0.3, yb - y0, top, z1 - G['step_tread'], z1, th['floor_mat'], collide=True)
    # ---- gate bodies (drawn closed state + what is left when open); the editor adds SpellFieldGate308 and the collider
    gate_rows = []
    for gi, g in enumerate(gates):
        mesh = Mesh(f"{D['id']}_Gate{gi}_Closed"); left = Mesh(f"{D['id']}_Gate{gi}_Open"); p = np.asarray(g['pos']); yaw = math.radians(g['yaw']); a_ = np.array([math.cos(yaw), 0, -math.sin(yaw)]); b_ = np.cross(UP, a_)
        h_ = wall_h - 0.4
        if g['feature'] == 'vine':
            for _ in range(26):
                u0, u1 = rng.uniform(-dw / 2, dw / 2, 2); mesh.log(p + a_ * u0 + b_ * rng.uniform(-0.25, 0.25), p + a_ * u1 + UP * h_ * rng.uniform(0.7, 1.0) + b_ * rng.uniform(-0.25, 0.25), rng.uniform(0.03, 0.07), 0.025, th['vine_mat'], rng=rng, wob=0.2)
            for _ in range(10):
                yy = rng.uniform(0.3, h_); mesh.log(p - a_ * dw / 2 + UP * yy, p + a_ * dw / 2 + UP * (yy + rng.uniform(-0.5, 0.5)), 0.035, 0.03, th['vine_mat'], rng=rng, wob=0.2)
            for _ in range(6): left.log(p + a_ * rng.uniform(-dw / 2, dw / 2) + b_ * rng.uniform(-0.6, 0.6), p + a_ * rng.uniform(-dw / 2, dw / 2) + b_ * rng.uniform(-0.6, 0.6) + UP * 0.08, 0.04, 0.03, th['vine_mat'], rng=rng, wob=0.2)
        elif g['feature'] == 'boulder':
            yy = 0.0
            while yy < h_ - 0.3:
                hh = rng.uniform(0.7, 1.2); mesh.slab(p + UP * (yy + hh) + a_ * rng.uniform(-0.15, 0.15), a_, b_, dw / 2 + rng.uniform(0.0, 0.2), rng.uniform(0.6, 0.95), hh, th['boulder_mat'], rng, tilt=0.12, sides=(6, 9)); yy += hh * 0.92
            for _ in range(5): left.slab(p + a_ * rng.uniform(-dw / 2, dw / 2) * 1.3 + b_ * rng.choice([-1, 1]) * rng.uniform(0.9, 1.6) + UP * rng.uniform(0.25, 0.5), a_, b_, rng.uniform(0.3, 0.6), rng.uniform(0.3, 0.5), 0.5, th['boulder_mat'], rng, tilt=0.3)
        else:
            mesh.box(p + UP * h_ / 2, a_, UP, b_, (dw + 0.1, h_, 0.5), th['taint_mat'])
            for _ in range(14): mesh.box(p + a_ * rng.uniform(-dw / 2, dw / 2) + UP * rng.uniform(0.2, h_) + b_ * rng.choice([-1, 1]) * 0.27, a_, UP, b_, (rng.uniform(0.3, 0.9), rng.uniform(0.3, 1.1), 0.06), th['taint_mat'])
            left.box(p + UP * 0.02, a_, UP, b_, (dw * 0.8, 0.03, 1.4), th['taint_mat'])
        K.m[mesh.name[len(D['id']) + 1:]] = mesh; K.m[left.name[len(D['id']) + 1:]] = left
        gate_rows.append(dict(id=f"{D['id']}_g{gi}", kind={'vine': 'Vine', 'boulder': 'Boulder', 'taint': 'Pollution'}[g['feature']], pos=g['pos'], yaw=g['yaw'], size=[dw, h_, 0.9], closed=mesh.name, open=left.name))
    # ---- rewards: a celadon piece = a stone-scripture fragment at the far end of the way; a white porcelain jar with coins on a high stand (국)
    R = cfg['rewards']; rewards = []; top_cell = max((c_ for c_ in chain[1:-1]), key=lambda c_: (level[c_], chain.index(c_)))
    x0, x1, z0, z1 = rect(top_cell); px, pz = (x0 + x1) / 2, (z0 + z1) / 2 + (0.25 * cd if top_cell[0] == 'B' else -0.25 * cd)
    K.box('Prop', px - 0.45, px + 0.45, level[top_cell], level[top_cell] + 0.72, pz - 0.45, pz + 0.45, th['cope_mat'], collide=True)
    rewards.append(dict(id=f"dg308_{D['id']}_fragment", kind='fragment', prefab=R['celadon'][seed_of(D['id'], 'c') % len(R['celadon'])], pos=[round(float(v), 3) for v in K.w(px, level[top_cell] + 0.72, pz)], stand=[round(float(v), 3) for v in K.w(px + 0.9, level[top_cell], pz)]))
    jar_cell = chain[max(1, len(chain) // 2 - 1)] if chain[max(1, len(chain) // 2 - 1)] != top_cell else chain[1]
    x0, x1, z0, z1 = rect(jar_cell); jx, jz = x0 + 1.5, (z0 + 1.5 if jar_cell[0] == 'A' else z1 - 1.5); jy = level[jar_cell] + led
    K.box('Prop', jx - 1.1, jx + 1.1, level[jar_cell], jy, jz - 1.1, jz + 1.1, th['wall_mat'] if th['wall'] in ('rubble', 'ashlar', 'earth', 'hanok') else th['cope_mat'], collide=True)
    rewards.append(dict(id=f"dg308_{D['id']}_jar", kind='coins', currency=int(R['coins']), prefab=R['white'][seed_of(D['id'], 'w') % len(R['white'])], pos=[round(float(v), 3) for v in K.w(jx, jy, jz)], stand=[round(float(v), 3) for v in K.w(jx, jy, jz)]))
    # ---- props that say where this is (drawn only, small)
    for pr in D.get('props', []):
        for _ in range(int(pr.get('count', 3))):
            cell = chain[int(rng.integers(0, len(chain)))]; x0, x1, z0, z1 = rect(cell); u = rng.uniform(x0 + 1.2, x1 - 1.2); v = rng.uniform(z0 + 1.2, z1 - 1.2); yl = level[cell]
            if abs(u - (x0 + x1) / 2) < dw and (abs(v - z0) < 2.0 or abs(v - z1) < 2.0): continue      # never in a doorway line
            if pr['kind'] == 'cairn':
                yy = yl
                for q in range(int(rng.integers(3, 7))): hh = rng.uniform(0.12, 0.26); K.mesh('Prop').slab(K.w(u + rng.uniform(-0.08, 0.08), yy + hh, v + rng.uniform(-0.08, 0.08)), K.ax, K.az, rng.uniform(0.25, 0.5) * (1 - q * 0.1), rng.uniform(0.22, 0.42) * (1 - q * 0.1), hh, th['cope_mat'], rng, tilt=0.1); yy += hh
            elif pr['kind'] == 'stele':
                K.box('Prop', u - 0.22, u + 0.22, yl, yl + rng.uniform(0.9, 1.5), v - 0.07, v + 0.07, th['cope_mat'])
            elif pr['kind'] == 'beam':
                a_ = K.w(u, yl + 0.1, v); K.mesh('Prop').log(a_, a_ + K.ax * rng.uniform(-2, 2) + K.az * rng.uniform(-2, 2) + UP * rng.uniform(0.2, 1.6), rng.uniform(0.08, 0.14), 0.07, th['wood_mat'], rng=rng, wob=0.12)
            elif pr['kind'] == 'post':
                K.mesh('Prop').log(K.w(u, yl - 0.1, v), K.w(u + rng.uniform(-0.15, 0.15), yl + rng.uniform(1.4, 2.6), v + rng.uniform(-0.15, 0.15)), rng.uniform(0.08, 0.13), 0.07, th['wood_mat'], rng=rng, wob=0.1)
            elif pr['kind'] == 'rack':
                K.box('Prop', u - 0.9, u + 0.9, yl + 0.9, yl + 0.98, v - 0.25, v + 0.25, th['wood_mat']); K.box('Prop', u - 0.9, u + 0.9, yl + 1.7, yl + 1.78, v - 0.25, v + 0.25, th['wood_mat'])
                for sx in (-0.85, 0.85):
                    for sz in (-0.2, 0.2): K.box('Prop', u + sx - 0.05, u + sx + 0.05, yl, yl + 1.9, v + sz - 0.05, v + sz + 0.05, th['wood_mat'])
    lamps = []; rocks = []; pools = []
    if cave: lamps, rocks, pools = dress_cave(K, G, style, chain, rect, level, n, [(px, pz), (jx, jz)], rng)
    if under: lamps = lamps + [mark_lamp]
    out = OUT / D['id']; (out / 'Meshes').mkdir(parents=True, exist_ok=True)
    for old in (out / 'Meshes').glob('*.json'): old.unlink()
    for name, m in K.m.items():
        if m.v: B.write_json(out / 'Meshes' / f'{m.name}.json', m.dump(), indent=None)
    drawn = [m.name for nm, m in K.m.items() if m.v and 'Gate' not in nm]
    lay = dict(id=D['id'], realm=D['realm'], name=D['name'], theme=D['theme'], site=site, podium_y=round(y0, 3), podium_over_ground_m=[round(y0 - max(samples), 2), round(y0 - min(samples), 2)], entrance_steps=steps,
               cols=n, cell=D['cell'], levels={f'{c_[0]}{c_[1] + 1}': level[c_] for c_ in chain}, features=feats, drawn=drawn, colliders=K.cols, gates=gate_rows, ledges=ledges,
               door=dict(id=f"dg308_{D['id']}_door", pos=door['pos'], yaw=door['yaw'], width=dw, height=G['door_h'], inside=[round(float(v), 3) for v in (np.asarray(door['pos']) + K.az * 1.1)]) if door else None,
               rewards=rewards, lamps=lamps, rocks=rocks, pools=pools, cave=cave, entrance=[round(float(v), 3) for v in (K.w(ex, sol['yl'] - y0, -sol['run']) if under else K.w(ex, 0.0, -t / 2))], underground=under, holes=holes, holesFlat=[v for h_ in holes for v in h_], depth_m=round(sol['yl'] - y0, 2) if under else 0.0, stair_run_m=round(sol['run'], 1) if under else 0.0,
               vertices=int(sum(len(m.v) for m in K.m.values())))
    B.write_json(out / 'layout.json', lay)
    return lay


def build(cfg):
    sites = json.loads((OUT / 'sites.json').read_text(encoding='utf-8')); f = B.load_height(B.ROOT / cfg['height']); rows = []
    for D in cfg['dungeons']:
        st = sites.get(D['id'])
        if not st or 'error' in st: print(D['id'], 'SKIPPED (no site)'); continue
        lay = build_one(cfg, D, st, f); rows.append(lay['id'])
        print(f"{lay['id']:22s} {lay['realm']:11s} at ({st['x']:.0f}, {st['z']:.0f}) depth {lay['depth_m']} run {lay['stair_run_m']} holes {len(lay['holes'])} steps {lay['entrance_steps']} levels {list(lay['levels'].values())} gates {[g['kind'] for g in lay['gates']]} colliders {len(lay['colliders'])} vertices {lay['vertices']}")
    B.write_json(OUT / 'index.json', dict(version=cfg['version'], height=cfg['height'], height_sha256=B.sha256(B.ROOT / cfg['height']), dungeons=rows))


def main():
    B.utf8()
    if len(sys.argv) != 3 or sys.argv[1] not in ('plan', 'build'): print(__doc__); return 2
    cfg = json.loads(Path(sys.argv[2]).read_text(encoding='utf-8'))
    plan(cfg) if sys.argv[1] == 'plan' else build(cfg)
    return 0


if __name__ == '__main__':
    sys.exit(main())
