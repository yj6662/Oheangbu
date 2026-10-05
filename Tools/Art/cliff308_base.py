"""#308 cliff boundary - shared offline loaders for scarp308.py / cliff308_align.py / cliff308_closure.py / cliff308_eval.py.

Read-only on the project. Grid = the Finish297 final surface: 4 m lattice, 1501 rows (z / 4) x 1001 cols (x / 4), float32 LE,
row-major, no header (see plan/Stage/FORMAT.md). Every array is [i = z / 4, j = x / 4].
The walkability lattice (deep water, terrain rectangle, route corridors, routes, content) is emptiness305.py up to its
reachability step - the same header seal308_closure.py / closure305.py execute - cached under plan/_work keyed by the base sha.
Nothing here writes outside Art/World/Compact/Rebuild/CliffBoundary308/plan.
"""
import hashlib, json, math, sys
from collections import deque
from pathlib import Path
import numpy as np

TOOLS = Path(__file__).resolve().parent
ROOT = TOOLS.parents[1]
A = ROOT / 'Oheangbu/Assets/_Project'
RB = ROOT / 'Art/World/Compact/Rebuild'
ENC = RB / 'Enclosure305'
CB = RB / 'CliffBoundary308'
PL = CB / 'plan'
WORK = PL / '_work'
BASE_HEIGHT = A / 'Art/World/Finish297/Surface/height.bytes'
HH, W, CELL = 1501, 1001, 4.0
TILE_M, TILE_COLS, TILE_ROWS = 500.0, 8, 12
SEED_NODE = (505, 841)            # Seal308 new-game node (3364, 2020) as (i, j)

REALMS = {  # WorldLayout_Main.asset Realms == Geography Regions (scout/realms_passes.json)
    'Cheongrim': [[4000, 1025.6], [4000, 4806.4], [2659.7, 4093.5], [2497.2, 1899.3]],
    'Hwanggyeong': [[2497.2, 1899.3], [2659.7, 4093.5], [1175, 4159.4], [1175, 1840.6]],
    'Jeokro': [[0, 0], [4000, 0], [4000, 1025.6], [2497.2, 1899.3], [1175, 1840.6], [0, 1031]],
    'Cheolong': [[1175, 4159.4], [0, 4969], [0, 1031], [1175, 1840.6]],
    'Hyeongang': [[4000, 4806.4], [4000, 6000], [0, 6000], [0, 4969], [1175, 4159.4], [2659.7, 4093.5]],
}
SHEETS = {  # vegetation sheets of the main scene's CompactRebuildArtRenderer (FixedPlacements)
    'DryLandscape': 'Art/World/Architecture296/Data/757de110844e3b4479c04f9c49d8a6b6_DryLandscape.asset',
    'River294Dressing': 'Art/World/Architecture296/Data/9be35ffe935cc2a48a2c9e57deed2af6_fb0262ee6f18ce44db0b6468d94e5e6f_Dressing.asset',
    'Banks': 'Art/World/Architecture296/Data/d47e502ebb4212d45aed27dbbc4bbb5e_Banks.asset',
    'Forest305': 'Art/World/Enclosure305/Forest305.asset',
}


def utf8():
    try:
        sys.stdout.reconfigure(encoding='utf-8')
    except Exception:
        pass


def sha256(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def guard_out(path):
    """the offline tools never write inside the Unity project (Assets, protected trees, scenes): staging into the project is the
    job of the editor ledger command. Refuses with a message instead of writing."""
    q = Path(path).resolve()
    try:
        q.relative_to((ROOT / 'Oheangbu').resolve())
    except ValueError:
        return q
    raise SystemExit(f'REFUSED: {q} is inside the Unity project; offline cliff308 tools write under Art/World/Compact/Rebuild/CliffBoundary308/plan (or a path outside Oheangbu/)')


def write_text(path, text):
    """LF text, binary mode (no CRLF translation on Windows)."""
    guard_out(path)
    Path(path).parent.mkdir(parents=True, exist_ok=True)
    Path(path).write_bytes(text.replace('\r\n', '\n').encode('utf-8'))


def write_json(path, obj, indent=1):
    write_text(path, json.dumps(obj, ensure_ascii=False, indent=indent, default=_js) + '\n')


def _js(o):
    if isinstance(o, (np.floating,)): return float(o)
    if isinstance(o, (np.integer,)): return int(o)
    if isinstance(o, (np.bool_,)): return bool(o)
    if isinstance(o, np.ndarray): return o.tolist()
    if isinstance(o, Path): return str(o)
    raise TypeError(type(o))


def rel(path):
    try:
        return str(Path(path).resolve().relative_to(ROOT)).replace('\\', '/')
    except ValueError:
        return str(path).replace('\\', '/')


def load_height(path=None):
    """float64 [HH, W] from a raw float32 LE height file (base when path is None). Refuses a wrong size."""
    p = Path(path) if path else BASE_HEIGHT
    a = np.fromfile(p, '<f4')
    if a.size != HH * W:
        raise SystemExit(f'REFUSED: {p} holds {a.size} floats, expected {HH * W} (1501 rows x 1001 cols float32 LE)')
    return a.reshape(HH, W).astype(np.float64)


def save_height(path, h):
    guard_out(path)
    Path(path).parent.mkdir(parents=True, exist_ok=True)
    np.ascontiguousarray(h, dtype='<f4').tofile(path)
    return sha256(path)


def grid_xz():
    return np.meshgrid(np.arange(W) * CELL, np.arange(HH) * CELL)


def protected():
    return np.fromfile(A / 'Art/World/Watershed295/Surface/protected.bytes', np.uint8).reshape(HH, W) > 0


def wet():
    return np.fromfile(RB / 'Watershed295/Generated/wet.bytes', np.uint8).reshape(HH, W) > 0


def seal(state):
    w = ENC / 'Out/Seal308/closure/_work'
    return np.load(w / f'reach-{state}.npy'), np.load(w / f'solid-{state}.npy')


def lattice(log=print):
    """dict(water4, inside4, route4, wall4, routes=[{id, pts, width, source}], content={Points, MainPath, BranchPath, ...}).
    Built once from emptiness305.py (header only) and cached under plan/_work, keyed by the base height sha."""
    base_sha = sha256(BASE_HEIGHT); src_path = ENC / 'emptiness305.py'; key = base_sha[:12] + '-' + sha256(src_path)[:12]
    npz = WORK / f'lat308-{key}.npz'; js = WORK / f'lat308-{key}.json'
    if not (npz.exists() and js.exists()):
        log(f'[cliff308_base] building the lattice cache {rel(npz)} (emptiness305 header) ...')
        src = src_path.read_text(encoding='utf-8'); cut = src.index('# ------------------------------------------------------------------ reachability')
        g = {'__file__': str(src_path), '__name__': 'cliff308_import'}; argv = sys.argv; sys.argv = ['emptiness305.py']
        import io, contextlib
        try:
            with contextlib.redirect_stdout(io.StringIO()):
                exec(compile(src[:cut], 'emptiness305.py', 'exec'), g)
        finally:
            sys.argv = argv
        WORK.mkdir(parents=True, exist_ok=True)
        np.savez_compressed(npz, water4=g['water4'], inside4=g['inside4'], route4=g['route4'], wall4=g['wall4'])
        C = g['C']
        content = {k: C.get(k) for k in ('Points', 'MainPath', 'BranchPath', 'Checkpoints', 'Encounters', 'StartFeet', 'InnCheckpointFeet')}
        routes = [dict(id=r['id'], pts=np.asarray(r['pts']).tolist(), width=r['width'], source=r['source']) for r in g['routes']]
        write_json(js, dict(base_sha256=base_sha, routes=routes, content=content), indent=None)
    d = np.load(npz); M = json.loads(js.read_text(encoding='utf-8'))
    out = {k: d[k] for k in d.files}; out['routes'] = M['routes']; out['content'] = M['content']; out['base_sha256'] = M['base_sha256']
    return out


def sheets(log=print):
    """dict(name -> float32 [n, 3] world positions) of the vegetation sheets' FixedPlacements (cached by file sha)."""
    import re
    key = hashlib.sha256('|'.join(sha256(A / p) for p in SHEETS.values()).encode()).hexdigest()[:12]; npz = WORK / f'sheets308-{key}.npz'
    if not npz.exists():
        log(f'[cliff308_base] caching vegetation sheets {rel(npz)} ...')
        rx = re.compile(r'Position: \{x: ([-0-9.eE+]+), y: ([-0-9.eE+]+), z: ([-0-9.eE+]+)\}'); out = {}
        for name, p in SHEETS.items():
            pts = []; section = None
            for line in open(A / p, encoding='utf-8'):
                if line.startswith('  ') and not line.startswith('   ') and line.strip().endswith(':'): section = line.strip()[:-1]
                if section == 'FixedPlacements':
                    m = rx.search(line)
                    if m: pts.append((float(m.group(1)), float(m.group(2)), float(m.group(3))))
            out[name] = np.array(pts, np.float32).reshape(-1, 3)
        WORK.mkdir(parents=True, exist_ok=True); np.savez_compressed(npz, **out)
    d = np.load(npz); return {k: d[k] for k in d.files}


def runs305():
    """forest runs the cliff ops name in `replaces` = Enclosure305 305.2 (also what the Seal308 lattice was stamped from).
    From the stage-1a link on, segments305.json is 305.3 (Tools/Art/enclosure305_cliff308.py: replaced runs gone, kept runs cut
    at the rock), so the frozen byte copy segments305_v305_2.json is read instead. A live file that is itself 305.2 wins over
    the frozen copy (review 2026-10-04: after `revert` + a regenerated 305.2 the frozen copy is the stale one)."""
    frozen = ENC / 'segments305_v305_2.json'
    S = json.loads((ENC / 'segments305.json').read_text(encoding='utf-8'))
    if str(S.get('version')) != '305.2' and frozen.exists(): S = json.loads(frozen.read_text(encoding='utf-8'))
    return {s['id']: s for s in S['segments']}, S.get('version', '?')


SEAL_BAND = 3.0                   # Seal308 barrier half band on the lattice (seal308_closure.py BAND)


def wall_lines():
    """[(id, polyline Nx2)] of the BUILT walls the Seal308 lattice holds solid - the same sources the emptiness305 header reads
    (capital loop, 철옹 walls, 황경 궁성 wall lines). Read-only. Apertures are not cut here: callers intersect with the Seal308
    solid of the state they test, which already has the doors open or shut."""
    fin = RB / 'Finish297'; jl = lambda p: json.loads(Path(p).read_text(encoding='utf-8'))
    cap = jl(fin / 'Capital/layout.json'); che = jl(fin / 'Cheolong/layout.json'); hwa = jl(fin / 'Hwanggyeong/layout.json')
    cc = [list(c) for c in cap['loop']['corners']]; out = [('capital_wall', np.asarray(cc + [cc[0]], float))]
    out += [('cheolong.' + w['name'], np.asarray([(p[0], p[2]) for p in w['walk']], float)) for w in che['walls']]
    out += [('hwanggyeong.' + w['name'], np.asarray([w['a'], w['b']], float)) for w in hwa['wallLines']]
    return out


def structure_mask(X, Z):
    """lattice cells of the built structures that stay solid whatever forest run is replaced: the walls of wall_lines() and the
    Seal308 gate footprint (wings, end bastions, closed panel - Enclosure305/Out/Seal308/footprint308.json), at the Seal308 half band."""
    m = np.zeros(X.shape, bool)
    for _, P in wall_lines(): m |= stamp(P, SEAL_BAND, X, Z)
    fp = ENC / 'Out/Seal308/footprint308.json'
    if fp.exists():
        F = json.loads(fp.read_text(encoding='utf-8'))
        for P in list(F.get('wings', [])) + ([F['panel_closed']] if F.get('panel_closed') else []): m |= stamp(P, SEAL_BAND, X, Z)
        for bx, bz, br in F.get('bastions', []): m |= np.hypot(X - bx, Z - bz) <= max(br, SEAL_BAND)
    return m


def stamp(P, half, X, Z):
    """lattice cells within `half` of polyline P (discs every 2 m - the emptiness305 / seal308_closure stamp)."""
    m = np.zeros(X.shape, bool); D, _ = resample(np.asarray(P, float), 2.0); r = int(math.ceil(half / CELL)) + 1
    for x, z in D:
        j0, i0 = int(round(x / CELL)), int(round(z / CELL)); js = slice(max(j0 - r, 0), min(j0 + r + 1, W)); is_ = slice(max(i0 - r, 0), min(i0 + r + 1, HH))
        m[is_, js] |= np.hypot(X[is_, js] - x, Z[is_, js] - z) <= half
    return m


# ------------------------------------------------------------------ geometry
def poly_mask(poly, X, Z):
    P = np.asarray(poly, float); n = len(P); inside = np.zeros(X.shape, bool)
    for k in range(n):
        x1, z1 = P[k]; x2, z2 = P[(k + 1) % n]
        cond = ((z1 > Z) != (z2 > Z))
        with np.errstate(divide='ignore', invalid='ignore'):
            xint = (x2 - x1) * (Z - z1) / (z2 - z1 + 1e-12) + x1
        inside ^= cond & (X < xint)
    return inside


def sample(h, x, z):
    """bilinear field sample."""
    x = np.clip(np.asarray(x, float), 0, 3999.99); z = np.clip(np.asarray(z, float), 0, 5999.99)
    j = x / 4; i = z / 4; j0 = np.floor(j).astype(int); i0 = np.floor(i).astype(int); fj = j - j0; fi = i - i0
    j1 = np.minimum(j0 + 1, W - 1); i1 = np.minimum(i0 + 1, HH - 1)
    return h[i0, j0] * (1 - fj) * (1 - fi) + h[i0, j1] * fj * (1 - fi) + h[i1, j0] * (1 - fj) * fi + h[i1, j1] * fj * fi


def sample_tri(h, x, z):
    """height of the tile MESH surface: each 4 m quad = triangles (00, 01, 10) and (10, 01, 11), as the tiles are built."""
    x = np.clip(np.asarray(x, float), 0, 3999.99); z = np.clip(np.asarray(z, float), 0, 5999.99)
    j = x / 4; i = z / 4; j0 = np.floor(j).astype(int); i0 = np.floor(i).astype(int); fx = j - j0; fz = i - i0
    h00 = h[i0, j0]; h10 = h[i0, j0 + 1]; h01 = h[i0 + 1, j0]; h11 = h[i0 + 1, j0 + 1]
    return np.where(fx + fz <= 1, h00 + (h10 - h00) * fx + (h01 - h00) * fz, h11 + (h01 - h11) * (1 - fx) + (h10 - h11) * (1 - fz))


def slope_deg(h):
    gz, gx = np.gradient(h, CELL)
    return np.degrees(np.arctan(np.hypot(gx, gz)))


def polyline_len(P):
    P = np.asarray(P, float); return float(np.sum(np.hypot(*np.diff(P, axis=0).T)))


def resample(P, step):
    P = np.asarray(P, float); seg = np.hypot(*np.diff(P, axis=0).T); s = np.concatenate([[0], np.cumsum(seg)])
    n = max(2, int(math.ceil(s[-1] / step)) + 1); t = np.linspace(0, s[-1], n)
    return np.stack([np.interp(t, s, P[:, 0]), np.interp(t, s, P[:, 1])], 1), t


def signed_distance(P, X, Z, maxd):
    """distance from grid cells to polyline P, sign by the left normal (left = +) and arclength of the foot; +inf beyond maxd of the bbox."""
    P = np.asarray(P, float)
    x0, z0 = P.min(0) - maxd; x1, z1 = P.max(0) + maxd
    j0, j1 = max(0, int(x0 // 4)), min(W - 1, int(x1 // 4) + 1); i0, i1 = max(0, int(z0 // 4)), min(HH - 1, int(z1 // 4) + 1)
    xs = X[i0:i1 + 1, j0:j1 + 1]; zs = Z[i0:i1 + 1, j0:j1 + 1]
    best = np.full(xs.shape, np.inf); sgn = np.zeros(xs.shape); S = np.zeros(xs.shape); acc = 0.0
    for k in range(len(P) - 1):
        ax, az = P[k]; bx, bz = P[k + 1]; L = math.hypot(bx - ax, bz - az)
        if L < 1e-6: continue
        tx, tz = (bx - ax) / L, (bz - az) / L
        s = (xs - ax) * tx + (zs - az) * tz; sc = np.clip(s, 0, L)
        q = -(xs - ax) * tz + (zs - az) * tx
        d = np.hypot(xs - (ax + tx * sc), zs - (az + tz * sc))
        better = d < best
        best[better] = d[better]; sgn[better] = np.sign(q[better]) + (q[better] == 0); S[better] = acc + sc[better]
        acc += L
    D = np.full(X.shape, np.inf); SG = np.zeros(X.shape); SS = np.full(X.shape, np.nan)
    D[i0:i1 + 1, j0:j1 + 1] = best; SG[i0:i1 + 1, j0:j1 + 1] = sgn; SS[i0:i1 + 1, j0:j1 + 1] = S
    return D, SG, SS


def band(P, r, X, Z):
    D, _, _ = signed_distance(np.asarray(P, float), X, Z, r + 8); return D <= r


def flood(walk, seed_ij):
    """4-neighbour flood from one node (i, j) or from a bool seed mask."""
    reach = np.zeros(walk.size, bool); fw = walk.ravel()
    if isinstance(seed_ij, np.ndarray):
        s = np.flatnonzero((seed_ij & walk).ravel())
    else:
        k = seed_ij[0] * W + seed_ij[1]; s = np.array([k]) if fw[k] else np.array([], int)
    reach[s] = True; dq = deque(s.tolist())
    while dq:
        k = dq.popleft(); j = k % W
        if j > 0 and fw[k - 1] and not reach[k - 1]: reach[k - 1] = True; dq.append(k - 1)
        if j < W - 1 and fw[k + 1] and not reach[k + 1]: reach[k + 1] = True; dq.append(k + 1)
        n = k - W
        if n >= 0 and fw[n] and not reach[n]: reach[n] = True; dq.append(n)
        n = k + W
        if n < fw.size and fw[n] and not reach[n]: reach[n] = True; dq.append(n)
    return reach.reshape(walk.shape)


def flood8(walk, seed_ij):
    """8-neighbour flood (sensitivity model: a diagonal squeeze between two unwalkable nodes counts as a way through)."""
    reach = np.zeros(walk.shape, bool)
    if not walk[seed_ij]: return reach
    reach[seed_ij] = True; fr = reach.copy()
    while fr.any():
        nb = np.zeros_like(fr)
        nb[1:, :] |= fr[:-1, :]; nb[:-1, :] |= fr[1:, :]; nb[:, 1:] |= fr[:, :-1]; nb[:, :-1] |= fr[:, 1:]
        nb[1:, 1:] |= fr[:-1, :-1]; nb[:-1, :-1] |= fr[1:, 1:]; nb[1:, :-1] |= fr[:-1, 1:]; nb[:-1, 1:] |= fr[1:, :-1]
        fr = nb & walk & ~reach; reach |= fr
    return reach


def flood_edges(h, ok, seed_ij, max_step_m, diag=False):
    """edge-slope flood (sensitivity model): a step between two lattice nodes is allowed when both are `ok` and |dy| < max_step_m
    (4 m over a 4 m edge = 45 deg). Unlike the node-slope model, the unraised FOOT vertex of a one-cell face is standable here -
    which is what the tile mesh really is. diag = also the two quad diagonals (the 00-11 one only if the quad centre agrees)."""
    reach = np.zeros(h.shape, bool)
    if not ok[seed_ij]: return reach
    reach[seed_ij] = True; fr = reach.copy()
    ex = np.abs(np.diff(h, axis=1)) < max_step_m; ez = np.abs(np.diff(h, axis=0)) < max_step_m
    if diag:
        lim = max_step_m * math.sqrt(2); c = (h[:-1, 1:] + h[1:, :-1]) / 2      # the quad centre lies on the (10)-(01) diagonal of the tile triangulation
        e11 = (np.abs(h[1:, 1:] - h[:-1, :-1]) < lim) & (np.abs(c - h[:-1, :-1]) < lim / 2 + 1e-9) & (np.abs(c - h[1:, 1:]) < lim / 2 + 1e-9)
        e1m = np.abs(h[1:, :-1] - h[:-1, 1:]) < lim
    while fr.any():
        nb = np.zeros_like(fr)
        nb[:, 1:] |= fr[:, :-1] & ex; nb[:, :-1] |= fr[:, 1:] & ex; nb[1:, :] |= fr[:-1, :] & ez; nb[:-1, :] |= fr[1:, :] & ez
        if diag:
            nb[1:, 1:] |= fr[:-1, :-1] & e11; nb[:-1, :-1] |= fr[1:, 1:] & e11; nb[1:, :-1] |= fr[:-1, 1:] & e1m; nb[:-1, 1:] |= fr[1:, :-1] & e1m
        fr = nb & ok & ~reach; reach |= fr
    return reach


def dilate(m, n=1):
    for _ in range(n):
        q = m.copy(); q[1:, :] |= m[:-1, :]; q[:-1, :] |= m[1:, :]; q[:, 1:] |= m[:, :-1]; q[:, :-1] |= m[:, 1:]; m = q
    return m


def clusters(mask, d=60, cap=40):
    cl = []
    for i, j in np.argwhere(mask):
        x, z = int(j) * 4, int(i) * 4
        for L in cl:
            if abs(x - L[0]) < d and abs(z - L[1]) < d: L[2] += 1; break
        else: cl.append([x, z, 1])
    return sorted(cl, key=lambda c: -c[2])[:cap]


def tiles_of(mask, margin=0):
    """500 m tiles x<col 0-7>_z<row 0-11> touched by a cell mask (+margin m: the editor op box)."""
    ii, jj = np.nonzero(mask); out = set()
    for dx in (-margin, 0, margin):
        for dz in (-margin, 0, margin):
            x = jj * 4 + dx; z = ii * 4 + dz
            cx = np.clip(x // 500, 0, TILE_COLS - 1).astype(int); cz = np.clip(z // 500, 0, TILE_ROWS - 1).astype(int)
            out |= set(zip(cx.tolist(), cz.tolist()))
            # a vertex on a shared tile edge belongs to both tiles (x % 500 == 0)
            for sh_x, sh_z in ((1, 0), (0, 1), (1, 1)):
                m = np.ones(len(x), bool)
                if sh_x: m &= (x % 500 == 0) & (x > 0)
                if sh_z: m &= (z % 500 == 0) & (z > 0)
                if m.any():
                    out |= set(zip(np.clip(x[m] // 500 - sh_x, 0, TILE_COLS - 1).astype(int).tolist(), np.clip(z[m] // 500 - sh_z, 0, TILE_ROWS - 1).astype(int).tolist()))
    return sorted(out)


def tile_names(ts):
    return [f'x{a}_z{b}' for a, b in ts]
