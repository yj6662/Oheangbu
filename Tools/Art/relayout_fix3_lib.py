#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""#308 relayout fix 3 (D308-22 answer 1: the delivery stop house onto the end of the flat) - shared offline geometry. READ ONLY.

Everything here is [O] offline: the 1b stage height field (4 m lattice, bilinear = CompactWorldSurface.Sample), the scene YAML,
the layout / content assets and the REAL mesh assets of the house (15 VisualCorridor meshes x 1.144 + the GroundFit299 stone skirt).
No editor, no Play. Used by relayout_fix3_dry.py and relayout_fix3_draw.py.

Frames: Unity yaw about +Y. A point (lx, ly, lz) of an object at (px, py, pz) with yaw a:
    wx = px + lx cos a + lz sin a ,  wy = py + ly ,  wz = pz - lx sin a + lz cos a
"""
import io, json, math, re, sys, hashlib
from pathlib import Path
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(Path(__file__).resolve().parent))
import contentseat308_dry as S            # noqa: E402
import content308_relayout_dry as R       # noqa: E402
import buildingaudit308_offline as B      # noqa: E402
import mesh308_offline as M               # noqa: E402

PROJECT = ROOT / 'Oheangbu'
CB = ROOT / 'Art/World/Compact/Rebuild/CliffBoundary308'
OUT = ROOT / 'Art/Playtest308/Relayout/fix3'
STAGE = ROOT / 'Tools/Unity/Stage308_relayout_fix3'
MESH_DIR = PROJECT / 'Assets/_Project/Art/World/WorldMacro/Playtest/VisualCorridor/Meshes'
HOUSE_MESH = 'abc00000000016407137830406637880_L0_M%d.asset'
SKIRT_MESH = PROJECT / 'Assets/_Project/Art/World/Finish297/GroundFit299/Skirts/CapitalEscort252_cargo_delivery_Guesthouse252_Surface_0_1_skirt308.asset'
STOP = 'CapitalEscort252/cargo_delivery'
HOUSE = STOP + '/Guesthouse252'
CARGO = 'CapitalEscort252/SealedCargo252'      # the escort cargo box (session.DemoEscortCargo); its BoxCollider footprint is set down on CargoWait
ORDER = ['arch296', 'folk298', 'main']

HN = np.frombuffer(S.HEIGHT_P1B.read_bytes(), dtype='<f4').reshape(S.H_ROWS, S.H_COLS)


def h(x, z):
    fx = x / 4.0; fz = z / 4.0; ix = int(fx); iz = int(fz); u = fx - ix; v = fz - iz
    a = HN[iz, ix] + (HN[iz, ix + 1] - HN[iz, ix]) * u; b = HN[iz + 1, ix] + (HN[iz + 1, ix + 1] - HN[iz + 1, ix]) * u
    return float(a + (b - a) * v)


def hv(x, z):
    """vectorised h."""
    fx = np.asarray(x, float) / 4.0; fz = np.asarray(z, float) / 4.0; ix = fx.astype(int); iz = fz.astype(int); u = fx - ix; v = fz - iz
    a = HN[iz, ix] + (HN[iz, ix + 1] - HN[iz, ix]) * u; b = HN[iz + 1, ix] + (HN[iz + 1, ix + 1] - HN[iz + 1, ix]) * u
    return a + (b - a) * v


def slope(x, z, d=1.0):
    return math.degrees(math.atan(math.hypot((h(x + d, z) - h(x - d, z)) / (2 * d), (h(x, z + d) - h(x, z - d)) / (2 * d))))


def to_world(pose, lx, lz):
    px, py, pz, yaw = pose; c = math.cos(math.radians(yaw)); s = math.sin(math.radians(yaw))
    return (px + lx * c + lz * s, pz - lx * s + lz * c)


def to_local(pose, wx, wz):
    px, py, pz, yaw = pose; c = math.cos(math.radians(yaw)); s = math.sin(math.radians(yaw)); dx = wx - px; dz = wz - pz
    return (dx * c - dz * s, dx * s + dz * c)


def world_pts(pose, P):
    """(n, 3) local points -> world."""
    px, py, pz, yaw = pose; c = math.cos(math.radians(yaw)); s = math.sin(math.radians(yaw))
    out = np.empty_like(P); out[:, 0] = px + P[:, 0] * c + P[:, 2] * s; out[:, 1] = py + P[:, 1]; out[:, 2] = pz - P[:, 0] * s + P[:, 2] * c
    return out


def rect_poly(pose, r):
    """r = (x0, x1, z0, z1) local -> 4 world XZ corners."""
    return [to_world(pose, r[0], r[2]), to_world(pose, r[1], r[2]), to_world(pose, r[1], r[3]), to_world(pose, r[0], r[3])]


def seg_d(p, a, b): return S.seg_dist(p, a, b)


def poly_edges(poly): return [(poly[i], poly[(i + 1) % len(poly)]) for i in range(len(poly))]


def in_poly(p, poly):
    x, z = p; inside = False
    for (ax, az), (bx, bz) in poly_edges(poly):
        if (az > z) != (bz > z) and x < ax + (z - az) * (bx - ax) / (bz - az): inside = not inside
    return inside


def seg_seg_d(a, b, c, d):
    def ccw(p, q, r): return (r[1] - p[1]) * (q[0] - p[0]) - (q[1] - p[1]) * (r[0] - p[0])
    if (ccw(a, c, d) > 0) != (ccw(b, c, d) > 0) and (ccw(a, b, c) > 0) != (ccw(a, b, d) > 0): return 0.0
    return min(seg_d(a, c, d), seg_d(b, c, d), seg_d(c, a, b), seg_d(d, a, b))


def poly_line_d(poly, line):
    """smallest distance between a polygon outline (or its inside) and a polyline."""
    if any(in_poly(p, poly) for p in line): return 0.0
    return min(seg_seg_d(a, b, c, d) for a, b in poly_edges(poly) for c, d in zip(line, line[1:]))


def poly_poly_d(p, q):
    if any(in_poly(a, q) for a in p) or any(in_poly(a, p) for a in q): return 0.0
    return min(seg_seg_d(a, b, c, d) for a, b in poly_edges(p) for c, d in poly_edges(q))


def poly_pt_d(poly, p):
    if in_poly(p, poly): return 0.0
    return min(seg_d(p, a, b) for a, b in poly_edges(poly))


def sha12(path): return hashlib.sha256(Path(path).read_bytes()).hexdigest()[:12]


# ------------------------------------------------------------------------------------------------ the house: real meshes

def load_house(scale, child_off):
    """15 parts (positions in the Guesthouse252 root frame, triangles) + the skirt (root frame). Cached as .npz keyed by the asset shas."""
    files = [MESH_DIR / (HOUSE_MESH % i) for i in range(15)] + [SKIRT_MESH]
    key = hashlib.sha256(('|'.join(sha12(f) for f in files) + '|%r|%r' % (scale, tuple(child_off))).encode()).hexdigest()[:16]
    cache = OUT / 'cache' / ('house_%s.npz' % key)
    if cache.exists():
        z = np.load(cache); return [(z['p%d' % i], z['t%d' % i]) for i in range(15)], (z['sp'], z['st']), key
    parts = []
    for i in range(15):
        pos, tri = M.load_mesh(str(files[i])); parts.append((pos * scale + np.array(child_off, float), tri))
    sp, st = M.load_mesh(str(SKIRT_MESH))
    cache.parent.mkdir(parents=True, exist_ok=True)
    np.savez_compressed(cache, sp=sp, st=st, **{'p%d' % i: parts[i][0] for i in range(15)}, **{'t%d' % i: parts[i][1] for i in range(15)})
    return parts, (sp, st), key


def used(pos, tri): return pos[np.unique(tri)]


def house_frame(parts):
    """measured boxes of the real mesh, root frame (x0, x1, z0, z1) + heights."""
    base = used(*parts[1]); top = used(*parts[2]); body = used(*parts[0]); roof = np.concatenate([used(*parts[i]) for i in (8, 9, 10, 11, 12, 13)])
    low = base[base[:, 1] < 0.05]
    return dict(
        base=(float(low[:, 0].min()), float(low[:, 0].max()), float(low[:, 2].min()), float(low[:, 2].max())),
        plinth=(float(top[:, 0].min()), float(top[:, 0].max()), float(top[:, 2].min()), float(top[:, 2].max())),
        plinth_top=float(top[:, 1].max()), base_bottom=float(base[:, 1].min()),
        body=(float(body[:, 0].min()), float(body[:, 0].max()), float(body[:, 2].min()), float(body[:, 2].max())),
        roof=(float(roof[:, 0].min()), float(roof[:, 0].max()), float(roof[:, 2].min()), float(roof[:, 2].max())),
        eave_low=float(roof[:, 1].min()), roof_top=float(roof[:, 1].max()))


def entrance_evidence(parts):
    """which side of the pavilion is the entrance: wall panel (M4), door leaf (M5 / M6) and post (M0) area on the outermost wall plane
    of each side, band y 0.9 - 3.0 m. Returns {side: {...}} and the verdict."""
    out = {}
    body = used(*parts[0]); bx0, bx1, bz0, bz1 = body[:, 0].min(), body[:, 0].max(), body[:, 2].min(), body[:, 2].max()
    for side, ax, sgn, edge in (('+x', 0, 1, bx1), ('-x', 0, -1, bx0), ('+z', 2, 1, bz1), ('-z', 2, -1, bz0)):
        row = {}
        for name, ids in (('wall', (4,)), ('door', (5, 6)), ('post', (0,)), ('frame', (3,))):
            a_out = 0.0; a_in = 0.0
            for i in ids:
                pos, tri = parts[i]; a, b, c = pos[tri[:, 0]], pos[tri[:, 1]], pos[tri[:, 2]]
                n = np.cross(b - a, c - a); ar = 0.5 * np.linalg.norm(n, axis=1); nn = n / (2 * ar[:, None] + 1e-12); cen = (a + b + c) / 3
                m = (nn[:, ax] * sgn > .7) & (cen[:, 1] > 0.9) & (cen[:, 1] < 3.0)
                d = np.abs(cen[:, ax] - edge)
                a_out += float(ar[m & (d <= 0.45)].sum()); a_in += float(ar[m & (d > 0.45)].sum())
            row[name] = round(a_out, 2); row[name + '_set_back'] = round(a_in, 2)
        out[side] = row
    # the entrance = the side whose outermost plane has posts but (almost) no wall panel, with a door leaf on the wall set back behind it
    open_sides = [k for k, v in out.items() if v['wall'] < 0.5 and v['post'] > 0.5]
    verdict = dict(open_sides=open_sides, entrance=open_sides[0] if len(open_sides) == 1 and out[open_sides[0]]['door_set_back'] > 1.0 else None)
    return out, verdict


def perimeter_profile(pose, parts, skirt, fr, step=0.25, band=0.45):
    """along the four edges of the base: the lowest house / skirt geometry near each perimeter point against the ground under it.
    float_m > 0 = daylight under the house there. Returns rows (side, s, wx, wz, lowest_y, ground, float_m, skirt_here)."""
    px, py, pz, yaw = pose
    low = np.concatenate([used(*parts[1]), skirt[0]]); sk = skirt[0]
    x0, x1, z0, z1 = fr['base']; rows = []
    def sample(side, lx, lz, s):
        if side in ('+x', '-x'): m = (np.abs(low[:, 0] - lx) <= band) & (np.abs(low[:, 2] - lz) <= step); ms = (np.abs(sk[:, 0] - lx) <= band + 0.2) & (np.abs(sk[:, 2] - lz) <= step)
        else: m = (np.abs(low[:, 2] - lz) <= band) & (np.abs(low[:, 0] - lx) <= step); ms = (np.abs(sk[:, 2] - lz) <= band + 0.2) & (np.abs(sk[:, 0] - lx) <= step)
        lowest = float(low[m, 1].min()) if m.any() else fr['base_bottom']
        wx, wz = to_world(pose, lx, lz); g = h(wx, wz)
        rows.append((side, round(s, 2), round(wx, 2), round(wz, 2), round(py + lowest, 3), round(g, 3), round(py + lowest - g, 3), bool(ms.any())))
    for lz in np.arange(z0, z1 + 1e-6, step): sample('+x', x1, float(lz), float(lz)); sample('-x', x0, float(lz), float(lz))
    for lx in np.arange(x0, x1 + 1e-6, step): sample('+z', float(lx), z1, float(lx)); sample('-z', float(lx), z0, float(lx))
    return rows


# ------------------------------------------------------------------------------------------------ layout / content / scene

def load_world(alias='main'):
    lay = R.load_asset(PROJECT / R.LAYOUT[alias]); con = R.load_asset(PROJECT / R.CONTENT[alias])
    routes = {r['Id']: [(float(p['x']), float(p['y'])) for p in r['Bends']] for r in lay['Routes'] if len(r.get('Bends') or []) >= 2}
    width = {r['Id']: float(r['Width']) for r in lay['Routes']}
    place = next(p for p in lay['Places'] if p['Id'] == 'capital_delivery')
    return dict(lay=lay, con=con, routes=routes, width=width, place=place)


def stop_nodes(alias):
    """the stop's direct children from the scene YAML + the house pose."""
    sk = S.SceneK(PROJECT / S.SCENES[alias]); sc = sk.sc; t = sk.get(STOP); out = {}
    if t is None: raise SystemExit('no %s in %s' % (STOP, alias))
    rp, rq, rs = sc.world(t)
    for c in sc.children_of(t):
        n = sc.nodes[c]; p, q, s_ = sc.world(c)
        boxes = [dict(size=i['size'], center=i.get('center') or (0, 0, 0)) for (cl, _, i) in sk.comps.get(n.go, []) if cl == 65 and i.get('size')]
        out[n.name] = dict(pos=[round(v, 4) for v in p], local=[round(v, 4) for v in n.pos], yaw=round(B.yaw_of(q), 2), scale=[round(v, 4) for v in s_], boxes=boxes,
                           colliders=sum(1 for x in sc.subtree(c) for (cl, _, i) in sk.comps.get(sc.nodes[x].go, []) if cl in (64, 65, 135, 136)))
    children = {}
    for c in sc.children_of(sk.get(HOUSE)):
        n = sc.nodes[c]; children[n.name] = dict(local=[round(v, 4) for v in n.pos], scale=[round(v, 4) for v in n.scale], comps=sorted(S.CLS.get(cl, str(cl)) for (cl, _, i) in sk.comps.get(n.go, [])))
    # review F4 (integration): the collider FOOTPRINTS of the stop's solid nodes, in each node's own frame - a capsule (radius; the
    # shared scene reader keeps no capsule numbers, so the document is read here) or a box (half x, half z after the node's scale)
    text = None
    for c in sc.children_of(t):
        n = sc.nodes[c]; row = out[n.name]; row['solid'] = None
        for (cl, cur, i) in sk.comps.get(n.go, []):
            if cl == 65 and i.get('size') and i.get('on', True):
                row['solid'] = dict(shape='box', half=[abs(i['size'][0] * row['scale'][0]) / 2, abs(i['size'][2] * row['scale'][2]) / 2], centre=[(i.get('center') or (0, 0, 0))[0] * row['scale'][0], (i.get('center') or (0, 0, 0))[2] * row['scale'][2]])
            elif cl == 136 and i.get('on', True):
                if text is None: text = (PROJECT / S.SCENES[alias]).read_text(encoding='utf-8', errors='replace')
                a = text.find('--- !u!136 &%s\n' % cur); b = text.find('\n--- ', a + 1); m = re.search(r'm_Radius: ([-\d.eE]+)', text[a:b]) if a >= 0 else None
                if m: row['solid'] = dict(shape='capsule', radius=float(m.group(1)) * max(abs(row['scale'][0]), abs(row['scale'][2])))
    cargo = None; ct = sk.get(CARGO)
    if ct is not None:
        cp, cq, cs = sc.world(ct)
        for (cl, cur, i) in sk.comps.get(sc.nodes[ct].go, []):
            if cl == 65 and i.get('size'): cargo = dict(key=CARGO, half=[abs(i['size'][0] * cs[0]) / 2, abs(i['size'][2] * cs[2]) / 2])
    return dict(sha=sk.sha[:12], root=[round(v, 4) for v in rp], root_yaw=round(B.yaw_of(rq), 2), nodes=out, house_children=children, cargo=cargo)


def footprint(solid, xz, yaw):
    """world XZ outline of a solid node's collider standing at xz with yaw: ('circle', centre, r) or ('poly', corners)."""
    if solid['shape'] == 'capsule': return ('circle', (xz[0], xz[1]), solid['radius'])
    hx, hz = solid['half']; cx, cz = solid.get('centre', [0, 0]); pose = (xz[0], 0.0, xz[1], yaw)
    return ('poly', [to_world(pose, cx - hx, cz - hz), to_world(pose, cx + hx, cz - hz), to_world(pose, cx + hx, cz + hz), to_world(pose, cx - hx, cz + hz)])


def seg_footprint_d(a, b, fp):
    """smallest distance between the segment a-b and a footprint (0 when the segment enters it)."""
    if fp[0] == 'circle': return max(0.0, seg_d(fp[1], a, b) - fp[2])
    return poly_line_d(fp[1], [a, b])


def poly_footprint_d(poly, fp):
    if fp[0] == 'circle': return max(0.0, poly_pt_d(poly, fp[1]) - fp[2])
    return poly_poly_d(poly, fp[1])


def nearby_objects(alias, centres, radius):
    """every scene transform (with a renderer or a collider) within radius (XZ) of any centre: (key, pos, comps)."""
    sk = S.SceneK(PROJECT / S.SCENES[alias]); sc = sk.sc; rows = []
    for tid, n in sc.nodes.items():
        if n.name is None: continue
        comps = [S.CLS.get(cl, str(cl)) for (cl, _, i) in sk.comps.get(n.go, [])]
        if not comps: continue
        p, q, s_ = sc.world(tid)
        if min(math.hypot(p[0] - c[0], p[2] - c[1]) for c in centres) > radius: continue
        rows.append((sk.key_of(tid), (round(p[0], 2), round(p[1], 2), round(p[2], 2)), comps))
    return rows


def grass_seeds(window):
    """seed XZ of the shared grass field inside (x0, x1, z0, z1) - contentseat308.json grass.asset, the same scan as contentseat308_dry."""
    seat = json.loads((CB / 'contentseat308.json').read_text(encoding='utf-8-sig'))
    f = PROJECT / seat['grass']['asset']; raw = f.read_bytes(); arr = np.frombuffer(raw[:len(raw) // 4 * 4], '<f4')
    x = arr[:-4]; y = arr[1:-3]; z = arr[2:-2]; nx = arr[3:-1]; nz = arr[4:]
    with np.errstate(invalid='ignore'):
        m = (x > window[0]) & (x < window[1]) & (z > window[2]) & (z < window[3]) & (y > 60) & (y < 160) & (np.abs(nx) <= 1.0) & (np.abs(nz) <= 1.0)
    gx, gy, gz = x[m], y[m], z[m]
    ok = np.abs(gy - hv(gx, gz)) < 1.5 if len(gx) else np.array([], bool)
    return np.stack([gx[ok], gz[ok]], axis=1), seat['grass']


def trunks(window):
    rel = json.loads((CB / 'content308_relayout.json').read_text(encoding='utf-8-sig'))
    return S.read_trunks(window, rel['dry']['trunk_sheets']), rel['dry']['trunk_sheets']


# ------------------------------------------------------------------------------------------------ the flat

def flat_mask(x0, x1, z0, z1, level, tol, max_deg, step=0.25):
    xs = np.arange(x0, x1 + 1e-6, step); zs = np.arange(z0, z1 + 1e-6, step); X, Z = np.meshgrid(xs, zs)
    G = hv(X, Z); d = 1.0
    sl = np.degrees(np.arctan(np.hypot((hv(X + d, Z) - hv(X - d, Z)) / (2 * d), (hv(X, Z + d) - hv(X, Z - d)) / (2 * d))))
    return xs, zs, G, sl, (np.abs(G - level) <= tol) & (sl <= max_deg)


def inscribed_radius(mask, xs, zs, blocked=None):
    """largest circle of True cells (brute force distance transform): (radius, cx, cz)."""
    free = mask.copy()
    if blocked is not None: free &= ~blocked
    pts = np.argwhere(~free); best = (0.0, 0.0, 0.0)
    if len(pts) == 0: return (float('inf'), 0.0, 0.0)
    step = xs[1] - xs[0]; cand = np.argwhere(free)
    px = pts[:, 1].astype(np.float32); pz = pts[:, 0].astype(np.float32)
    for k in range(0, len(cand), 1):
        j, i = cand[k]; d = np.sqrt(((px - i) ** 2 + (pz - j) ** 2).min()) * step
        if d > best[0]: best = (float(d), float(xs[i]), float(zs[j]))
    return best


# ------------------------------------------------------------------------------------------------ software view (real meshes)

def look_at(eye, target, fov_deg, w, hgt):
    e = np.array(eye, float); f = np.array(target, float) - e; f /= np.linalg.norm(f)
    r = np.cross(np.array([0.0, 1.0, 0.0]), f); r /= np.linalg.norm(r); u = np.cross(f, r)
    k = (hgt / 2) / math.tan(math.radians(fov_deg) / 2)
    return e, r, u, f, k


def project(cam, P, w, hgt):
    e, r, u, f, k = cam; d = P - e; z = d @ f
    return np.stack([w / 2 + (d @ r) / np.maximum(z, 1e-6) * k, hgt / 2 - (d @ u) / np.maximum(z, 1e-6) * k], axis=1), z


def render(tris_layers, eye, target, fov, w=960, hgt=540, sky=(226, 222, 212), light=(0.35, 0.8, -0.45)):
    """painter's algorithm + per-pixel z-buffer (numpy) of triangle layers [(P (n,3,3) world, colour (3,), two_sided)]. Returns an RGB array."""
    cam = look_at(eye, target, fov, w, hgt); img = np.empty((hgt, w, 3), np.uint8); img[:] = sky; zb = np.full((hgt, w), np.inf)
    L = np.array(light, float); L /= np.linalg.norm(L)
    for P, col, two in tris_layers:
        if len(P) == 0: continue
        n = np.cross(P[:, 1] - P[:, 0], P[:, 2] - P[:, 0]); ln = np.linalg.norm(n, axis=1) + 1e-12; n = n / ln[:, None]
        cen = P.mean(axis=1); facing = ((cam[0] - cen) * n).sum(axis=1) > 0
        keep = facing | two if isinstance(two, np.ndarray) else (facing if not two else np.ones(len(P), bool))
        nn = np.where(facing[:, None], n, -n)
        shade = 0.45 + 0.55 * np.clip(nn @ L, 0, 1)
        flat = P.reshape(-1, 3); sp, z = project(cam, flat, w, hgt); sp = sp.reshape(-1, 3, 2); z = z.reshape(-1, 3)
        ok = keep & (z.min(axis=1) > 0.3)
        for t in np.nonzero(ok)[0]:
            a, b, c = sp[t]; x0 = max(int(math.floor(min(a[0], b[0], c[0]))), 0); x1 = min(int(math.ceil(max(a[0], b[0], c[0]))), w - 1)
            y0 = max(int(math.floor(min(a[1], b[1], c[1]))), 0); y1 = min(int(math.ceil(max(a[1], b[1], c[1]))), hgt - 1)
            if x1 < x0 or y1 < y0: continue
            den = (b[1] - c[1]) * (a[0] - c[0]) + (c[0] - b[0]) * (a[1] - c[1])
            if abs(den) < 1e-9: continue
            xs = np.arange(x0, x1 + 1) + 0.5; ys = np.arange(y0, y1 + 1) + 0.5; X, Y = np.meshgrid(xs, ys)
            w0 = ((b[1] - c[1]) * (X - c[0]) + (c[0] - b[0]) * (Y - c[1])) / den; w1 = ((c[1] - a[1]) * (X - c[0]) + (a[0] - c[0]) * (Y - c[1])) / den; w2 = 1 - w0 - w1
            inside = (w0 >= 0) & (w1 >= 0) & (w2 >= 0)
            if not inside.any(): continue
            zz = 1.0 / (w0 / z[t, 0] + w1 / z[t, 1] + w2 / z[t, 2] + 1e-12)
            sub = zb[y0:y1 + 1, x0:x1 + 1]; hit = inside & (zz < sub)
            if not hit.any(): continue
            sub[hit] = zz[hit]; c3 = np.clip(np.array(col, float) * shade[t], 0, 255).astype(np.uint8)
            img[y0:y1 + 1, x0:x1 + 1][hit] = c3
    return img, zb


def ground_tris(x0, x1, z0, z1, step=2.0):
    xs = np.arange(x0, x1 + 1e-6, step); zs = np.arange(z0, z1 + 1e-6, step); X, Z = np.meshgrid(xs, zs); Y = hv(X, Z)
    V = np.stack([X, Y, Z], axis=2); a = V[:-1, :-1].reshape(-1, 3); b = V[:-1, 1:].reshape(-1, 3); c = V[1:, :-1].reshape(-1, 3); d = V[1:, 1:].reshape(-1, 3)
    return np.concatenate([np.stack([a, c, b], axis=1), np.stack([b, c, d], axis=1)])


def box_tris(centre, size, yaw, y0):
    """a box standing on y0 (feet), centre XZ, size (sx, sy, sz), yaw: 12 triangles."""
    hx, hz = size[0] / 2, size[2] / 2; pose = (centre[0], 0, centre[1], yaw)
    c = [to_world(pose, sx * hx, sz * hz) for sx, sz in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
    lo = [np.array([p[0], y0, p[1]]) for p in c]; hi = [np.array([p[0], y0 + size[1], p[1]]) for p in c]; T = []
    for i in range(4):
        j = (i + 1) % 4; T += [[lo[i], hi[i], lo[j]], [lo[j], hi[i], hi[j]]]
    T += [[hi[0], hi[2], hi[1]], [hi[0], hi[3], hi[2]]]
    return np.array(T)


def house_tris(pose, parts, skirt):
    """[(triangles world, part id)] of the 15 parts + ('skirt')."""
    out = []
    for i, (pos, tri) in enumerate(parts):
        W = world_pts(pose, pos); out.append((W[tri], i))
    W = world_pts(pose, skirt[0]); out.append((W[skirt[1]], 'skirt'))
    return out


PART_COLOURS = {0: (126, 66, 52), 1: (150, 148, 140), 2: (160, 158, 150), 3: (92, 62, 44), 4: (214, 204, 182), 5: (110, 78, 50), 6: (196, 186, 160), 7: (128, 100, 70), 8: (96, 92, 88), 9: (70, 70, 72),
                10: (120, 96, 70), 11: (60, 60, 62), 12: (88, 106, 92), 13: (140, 70, 50), 14: (136, 134, 126), 'skirt': (122, 120, 112)}
