#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""#308 relayout fix 4 (three places that do not read as their texts say: 적재장 / 금줄 꼬는 노인 / 옹기장수) - shared offline
geometry. READ ONLY, everything here is [O] offline.

  height   = the 1b stage height field (4 m lattice, bilinear; relayout_fix3_lib.h)                       tri_slope = the lattice triangle
  rows     = FixedPlacements of the four vegetation sheets (prototype Size / Radius x placement Scale)     trees = trunk + crown ellipsoid
  seeds    = the shared grass field (one seed = one clump)
  scene    = W_Demo_Main.unity YAML (object poses, mesh GUIDs), content / layout / campaign assets
  meshes   = the generated dressing meshes of the stage (Data/meshes/*.json) and the REAL mesh assets of the things that stand there
  views    = a z-buffer software rasteriser with an item buffer: the share of a thing that is NOT hidden is counted in pixels,
             against terrain AND vegetation rows AND every other drawn object (the lantern fix learned that terrain alone lies)
Tree crowns are solid ellipsoids here ([I]: the real crowns are leaf cards with gaps), so "hidden by a crown" is the cautious reading.
"""
import json, math, re, sys, hashlib, os
from pathlib import Path
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(Path(__file__).resolve().parent))
import relayout_fix3_lib as L               # noqa: E402
import contentseat308_dry as S              # noqa: E402
import content308_relayout_dry as R         # noqa: E402
import buildingaudit308_offline as B        # noqa: E402
import mesh308_offline as M                 # noqa: E402
import meshbounds308 as MBD                # noqa: E402

PROJECT = ROOT / 'Oheangbu'
CB = ROOT / 'Art/World/Compact/Rebuild/CliffBoundary308'
OUT = ROOT / 'Art/Playtest308/Relayout/fix4'
STAGE = ROOT / 'Tools/Unity/Stage308_relayout_fix4'
h, hv, slope = L.h, L.hv, L.slope
TREE_RX = re.compile(r'(Pinus|Ulmus|Salix|Melia)')
CROWN_FROM = 0.35      # [I] as farlight_geom.py: the crown starts at 35 % of the tree height


def tri_slope(x, z):
    """slope (deg) of the 4 m lattice triangle under (x, z): what one ray on the height mesh reads."""
    j0 = math.floor(x / 4); i0 = math.floor(z / 4); fx = x / 4 - j0; fz = z / 4 - i0
    h00 = L.HN[i0, j0]; h10 = L.HN[i0, j0 + 1]; h01 = L.HN[i0 + 1, j0]; h11 = L.HN[i0 + 1, j0 + 1]
    gx, gz = ((h10 - h00) / 4, (h01 - h00) / 4) if fx + fz <= 1 else ((h11 - h01) / 4, (h11 - h10) / 4)
    return math.degrees(math.atan(math.hypot(gx, gz)))


def sheets():
    return json.loads((CB / 'content308_relayout.json').read_text(encoding='utf-8-sig'))['dry']['trunk_sheets']


def veg_rows(window):
    """placements of every sheet inside (x0, x1, z0, z1): dict(sheet, id, proto, pos, scale, size, radius, cat, tree, H, R, trunk)."""
    num = r'([-0-9.eE+]+)'; rows = []
    for rel in sheets():
        protos = {}; sec = None; cur = None; pl = None
        with open(PROJECT / rel, encoding='utf-8') as f:
            for line in f:
                if line.startswith('  ') and line[2:3].isalpha() and ':' in line: sec = line.strip().split(':')[0]
                if sec == 'Prototypes':
                    if line.startswith('  - Id:'): cur = line.split(':', 1)[1].strip(); protos[cur] = {}
                    elif cur and line.startswith('    Size:'):
                        m = re.search(r'x: %s, y: %s, z: %s' % (num, num, num), line); protos[cur]['size'] = tuple(float(v) for v in m.groups())
                    elif cur and line.startswith('    Radius:'): protos[cur]['radius'] = float(line.split(':')[1])
                    elif cur and line.startswith('    Category:'): protos[cur]['cat'] = int(line.split(':')[1])
                elif sec == 'FixedPlacements':
                    if line.startswith('  - Id:'): pl = {'id': line.split(':', 1)[1].strip()}
                    elif pl is not None and line.startswith('    PrototypeId:'): pl['proto'] = line.split(':', 1)[1].strip()
                    elif pl is not None and line.startswith('    Position:'):
                        m = re.search(r'x: %s, y: %s, z: %s' % (num, num, num), line); pl['pos'] = tuple(float(v) for v in m.groups())
                    elif pl is not None and line.startswith('    Scale:'):
                        sc = float(line.split(':')[1]); p = pl.get('pos')
                        if p and window[0] <= p[0] <= window[1] and window[2] <= p[2] <= window[3]:
                            pr = protos.get(pl.get('proto'), {}); sz = pr.get('size') or (0, 0, 0)
                            if pr.get('cat', 2) != 2:
                                rows.append(dict(sheet=rel, id=pl['id'], proto=pl.get('proto', ''), pos=p, scale=sc, cat=pr.get('cat', -1), tree=bool(TREE_RX.search(pl.get('proto', ''))),
                                                 H=sz[1] * sc, R=max(sz[0], sz[2]) * .5 * sc, trunk=pr.get('radius', .2) * sc))
                        pl = None
    return rows


def seeds(window):
    """seed positions (n, 3) of the shared grass field inside the window, on the ground of the height field (|dy| < 3 m)."""
    seat = json.loads((CB / 'contentseat308.json').read_text(encoding='utf-8-sig'))
    raw = (PROJECT / seat['grass']['asset']).read_bytes(); arr = np.frombuffer(raw[:len(raw) // 4 * 4], '<f4')
    x = arr[:-4]; y = arr[1:-3]; z = arr[2:-2]; nx = arr[3:-1]; nz_ = arr[4:]
    with np.errstate(invalid='ignore'):
        m = (x > window[0]) & (x < window[1]) & (z > window[2]) & (z < window[3]) & (np.abs(nx) <= 1.0) & (np.abs(nz_) <= 1.0) & (y > 0) & (y < 500)
    gx, gy, gz = x[m], y[m], z[m]
    ok = np.abs(gy - hv(gx, gz)) < 3.0 if len(gx) else np.array([], bool)
    return np.stack([gx[ok], gy[ok], gz[ok]], axis=1).astype(float)


# ------------------------------------------------------------------------------------------------ content / layout / scene

def P3(v): return (float(v['x']), float(v['y']), float(v['z']))


class World:
    def __init__(self, alias='main'):
        self.alias = alias
        self.con = R.load_asset(PROJECT / R.CONTENT[alias]); self.lay = R.load_asset(PROJECT / R.LAYOUT[alias]); self.camp = R.load_asset(PROJECT / R.CAMPAIGN)
        self.points = {p['Id']: p for p in self.con['Points']}; self.feet = {c['Id']: P3(c['Feet']) for c in self.con.get('Checkpoints') or []}
        self.enc = {e['Id']: e for e in self.con.get('Encounters') or []}
        self.routes = {r['Id']: [(float(p['x']), float(p['y'])) for p in r['Bends']] for r in self.lay['Routes'] if len(r.get('Bends') or []) >= 2}
        self.width = {r['Id']: float(r['Width']) for r in self.lay['Routes']}
        self.runs = []
        for nm in ('MainPath', 'BranchPath'):
            run = []
            for p in self.con.get(nm) or []:
                q = (float(p['x']), float(p['z']))
                if run and math.hypot(q[0] - run[-1][0], q[1] - run[-1][1]) > R.RUN_GAP:
                    if len(run) >= 2: self.runs.append((nm, run))
                    run = []
                run.append(q)
            if len(run) >= 2: self.runs.append((nm, run))
        self.stages = {s['Id']: s for s in self.camp['Stages']}

    def pt(self, pid): return P3(self.points[pid]['Position'])

    def path_d(self, q):
        """nearest content MainPath / BranchPath centre line (the line Texts308 'road' and Content308 corridors read)."""
        return min(S.seg_dist(q, a, b) for _, run in self.runs for a, b in zip(run, run[1:]))

    def route_d(self, q, only=None):
        best = (1e9, '')
        for rid, pl in self.routes.items():
            if only and rid not in only: continue
            d = min(S.seg_dist(q, a, b) for a, b in zip(pl, pl[1:]))
            if d < best[0]: best = (d, rid)
        return best

    def line_d(self, q):
        """nearest of every route centre line and the content paths."""
        return min(self.path_d(q), self.route_d(q)[0])


_scene = {}


def scene(alias='main'):
    if alias not in _scene: _scene[alias] = S.SceneK(PROJECT / S.SCENES[alias])
    return _scene[alias]


_guid = None


def mesh_path(guid):
    """guid -> asset path, from a small cache beside the pictures (built once by scanning the .meta files)."""
    global _guid
    f = OUT / 'cache' / 'mesh_paths.json'
    if _guid is None: _guid = json.loads(f.read_text()) if f.exists() else {}
    if guid not in _guid:
        hit = None
        for dp, dn, fn in os.walk(PROJECT / 'Assets' / '_Project' / 'Art'):
            for n in fn:
                if not n.endswith('.asset.meta'): continue
                p = os.path.join(dp, n)
                with open(p, 'rb') as fh: head = fh.read(120)
                if guid.encode() in head: hit = os.path.relpath(p, PROJECT)[:-5].replace('\\', '/'); break
            if hit: break
        _guid[guid] = hit; f.parent.mkdir(parents=True, exist_ok=True); f.write_text(json.dumps(_guid, indent=1))
    return _guid[guid]


def scene_mesh(key, alias='main'):
    """(world triangles (m, 3, 3)) of the MeshFilter of one scene object, or None when its mesh is not a readable .asset."""
    sk = scene(alias); t = sk.get(key)
    if t is None: return None
    n = sk.sc.nodes[t]; mf = [i for (c, _, i) in sk.comps.get(n.go, []) if c == 33 and i.get('mesh') and i['mesh'][1]]
    if not mf: return None
    path = mesh_path(mf[0]['mesh'][1])
    if not path or not path.endswith('.asset'): return None
    pos, tri = M.load_mesh(str(PROJECT / path)); p, q, s_ = sk.sc.world(t)
    W = np.array([B.qrot(q, (v[0] * s_[0], v[1] * s_[1], v[2] * s_[2])) for v in pos]) + np.array(p)
    return W[tri]


def scene_pose(key, alias='main'):
    sk = scene(alias); t = sk.get(key)
    if t is None: return None
    p, q, s_ = sk.sc.world(t); return (p[0], p[1], p[2], B.yaw_of(q))


_fbx = {}


def scene_box(key, alias='main'):
    """the bounds of what ONE scene object draws / blocks, as 8 world corners (8, 3) + how it was read, or None (a bare point).
    Order of trust: the FBX vertices or the .asset AABB of its MeshFilter, else its BoxCollider (a primitive cube has no readable
    mesh). Corner i = (x of bit 0, y of bit 1, z of bit 2): corners 0, 1, 5, 4 are the bottom ring. (rev 2: the object-clearance
    rule used pivots, so the 4 m stone platform of the 국 revisit was one dot.)"""
    sk = scene(alias); t = sk.get(key)
    if t is None: return None
    n = sk.sc.nodes[t]; p, q, s_ = sk.sc.world(t); lo = hi = None; how = ''
    comps = sk.comps.get(n.go, [])
    for (c, _, i) in comps:
        if c != 33 or not i.get('mesh') or not i['mesh'][1]: continue
        src = MBD.guid_path(i['mesh'][1], str(OUT / 'cache' / 'guid_cache.json'))
        if src and src.lower().endswith('.fbx'):
            if src not in _fbx: _fbx[src] = MBD.fbx_boxes(PROJECT / src)[0]
            boxes = _fbx[src]; pick = boxes.get(n.name) or (list(boxes.values())[0] if len(boxes) == 1 else None)
            if pick is None:
                lod0 = [b for nm, b in boxes.items() if 'LOD0' in nm]; pick = lod0[0] if lod0 else max(boxes.values(), key=lambda b: b[2])
            lo, hi = pick[0], pick[1]; how = 'fbx ' + src.split('/')[-1]
        elif src and src.endswith('.asset'):
            b = MBD.asset_box(PROJECT / src)
            if b: lo, hi = b; how = 'asset ' + src.split('/')[-1]
    if lo is None:
        for (c, _, i) in comps:
            if c == 65 and i.get('size'):
                ce = i.get('center') or (0.0, 0.0, 0.0); sz = i['size']; lo = tuple(ce[a] - sz[a] / 2 for a in range(3)); hi = tuple(ce[a] + sz[a] / 2 for a in range(3)); how = 'BoxCollider'
    if lo is None: return None
    C = np.array([[(hi if (i >> a) & 1 else lo)[a] * s_[a] for a in range(3)] for i in range(8)])
    return np.array([B.qrot(q, tuple(c)) for c in C]) + np.array(p), how


def box_tris(corners):
    """12 triangles of a box given as scene_box corners."""
    c = np.asarray(corners); quads = ((0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)); T = []
    for a, b, d, e in quads: T += [[c[a], c[b], c[d]], [c[a], c[d], c[e]]]
    return np.array(T)


def box_footprint(corners):
    """the XZ polygon of a scene_box (its bottom ring; a tilted box gives the ring's shadow)."""
    c = np.asarray(corners); return [(float(c[i][0]), float(c[i][2])) for i in (0, 1, 5, 4)]


def objects_near(centre, radius, alias='main', skip=('Places308',)):
    """(key, pivot (x, y, z), component class ids) of every scene transform that carries a renderer or a collider within radius (XZ)."""
    sk = scene(alias); out = []
    for tid, n in sk.sc.nodes.items():
        if n.name is None: continue
        comps = sk.comps.get(n.go, [])
        if not comps: continue
        p, q, s_ = sk.sc.world(tid)
        if math.hypot(p[0] - centre[0], p[2] - centre[1]) > radius: continue
        k = sk.key_of(tid)
        if any(k == s or k.startswith(s + '/') for s in skip): continue
        out.append((k, (p[0], p[1], p[2]), [c for (c, _, i) in comps]))
    return out


def ground_plane(x, z, d):
    """the plane Texts308.PropTilt lays a prop on: through the ground d metres to +x / -x / +z / -z of (x, z).
    Returns (unit normal (nx, ny, nz), f(px, pz) -> the plane's height there, with the centre's own ground height kept)."""
    gx = (h(x + d, z) - h(x - d, z)) / (2 * d); gz = (h(x, z + d) - h(x, z - d)) / (2 * d); y0 = h(x, z)
    n = np.array([-gx, 1.0, -gz]); n /= np.linalg.norm(n)
    return n, (lambda px, pz: y0 + gx * (px - x) + gz * (pz - z))


# ------------------------------------------------------------------------------------------------ generated meshes

_gen = {}


def gen_mesh(name, folder=None):
    """{slot: local triangles (m, 3, 3)} of a generated mesh json."""
    folder = Path(folder) if folder else STAGE / 'Data' / 'meshes'; k = (str(folder), name)
    if k not in _gen:
        d = json.loads((folder / (name + '.json')).read_text(encoding='utf-8')); V = np.array(d['v'], float).reshape(-1, 3)
        _gen[k] = {s['m']: V[np.array(s['t'], int).reshape(-1, 3)] for s in d['sub']}
    return _gen[k]


def placed(tris, x, y, z, yaw, scale=1.0):
    """local triangles -> world (Unity yaw about +y)."""
    c = math.cos(math.radians(yaw)); s = math.sin(math.radians(yaw)); T = np.asarray(tris, float) * scale; out = np.empty_like(T)
    out[..., 0] = x + T[..., 0] * c + T[..., 2] * s; out[..., 1] = y + T[..., 1]; out[..., 2] = z - T[..., 0] * s + T[..., 2] * c
    return out


def local_box_world(box, x, z, yaw):
    """the four XZ corners of a mesh-frame box (centre, size, yaw) of an object at (x, z, yaw)."""
    cx, cy, cz = box['center']; sx, sy, sz = box['size']; by = box.get('yaw', 0.0); out = []
    for ux, uz in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
        lx, lz = ux * sx / 2, uz * sz / 2; c = math.cos(math.radians(by)); s = math.sin(math.radians(by)); px, pz = cx + lx * c + lz * s, cz - lx * s + lz * c
        c2 = math.cos(math.radians(yaw)); s2 = math.sin(math.radians(yaw)); out.append((x + px * c2 + pz * s2, z - px * s2 + pz * c2))
    return out


SLOT = {'bark': (88, 66, 50), 'cut': (224, 206, 170), 'timber': (150, 140, 128), 'rope': (150, 128, 84), 'straw': (176, 152, 98), 'paper': (246, 244, 236), 'iron': (52, 50, 48)}


# ------------------------------------------------------------------------------------------------ stand-in solids

def ellipsoid(c, rx, ry, rz, n=10, m=6):
    """triangles of an ellipsoid (stand-in for a tree crown / a jar)."""
    th = np.linspace(0, 2 * math.pi, n + 1); ph = np.linspace(-math.pi / 2, math.pi / 2, m + 1); T = []
    P = lambda i, j: np.array([c[0] + rx * math.cos(ph[j]) * math.cos(th[i]), c[1] + ry * math.sin(ph[j]), c[2] + rz * math.cos(ph[j]) * math.sin(th[i])])
    for i in range(n):
        for j in range(m):
            a, b, cc, d = P(i, j), P(i + 1, j), P(i + 1, j + 1), P(i, j + 1)
            T += [[a, d, b], [b, d, cc]]
    return np.array(T)


def cylinder(a, b, r, n=7):
    a = np.array(a, float); b = np.array(b, float); ax = b - a; ax /= np.linalg.norm(ax); ref = np.array([0, 1., 0]) if abs(ax[1]) < .9 else np.array([1., 0, 0])
    e1 = np.cross(ref, ax); e1 /= np.linalg.norm(e1); e2 = np.cross(ax, e1); th = np.linspace(0, 2 * math.pi, n + 1); T = []
    for i in range(n):
        u0 = e1 * math.cos(th[i]) + e2 * math.sin(th[i]); u1 = e1 * math.cos(th[i + 1]) + e2 * math.sin(th[i + 1])
        T += [[a + u0 * r, b + u0 * r, a + u1 * r], [a + u1 * r, b + u0 * r, b + u1 * r]]
    return np.array(T)


def tree_tris(row):
    """(trunk triangles, crown triangles) of one vegetation row; a rock / shrub is one body ellipsoid."""
    x, y, z = row['pos']; H = row['H']; Rr = row['R']
    if not row['tree']: return np.zeros((0, 3, 3)), ellipsoid((x, y + H / 2, z), Rr, H / 2, Rr, 8, 5)
    c0 = y + CROWN_FROM * H
    return cylinder((x, y - .3, z), (x, c0 + .3, z), max(row['trunk'], .06), 6), ellipsoid((x, (c0 + y + H) / 2, z), Rr, (y + H - c0) / 2, Rr, 10, 6)


def person_tris(x, y, z, yaw=0.0, height=1.66):
    """a standing man as three boxes (legs, coat, head): the silhouette size only."""
    T = [L.box_tris((x, z), (.34, .82, .24), yaw, y), L.box_tris((x, z), (.52, .62, .30), yaw, y + .78), L.box_tris((x, z), (.22, height - 1.40, .22), yaw, y + 1.40)]
    return np.concatenate(T)


def jar_tris(x, y, z, size):
    """a pot of the measured prefab box (w, h, d) standing on y."""
    return ellipsoid((x, y + size[1] / 2, z), size[0] / 2, size[1] / 2, size[2] / 2, 9, 5)


# ------------------------------------------------------------------------------------------------ rasteriser with an item buffer

def render(layers, eye, target, fov, w=960, hgt=540, sky=(226, 222, 212), light=(0.35, 0.8, -0.45)):
    """layers = [(triangles (n, 3, 3) world, colour, two_sided, item id)]. Returns (rgb, depth, item buffer int16; 0 = sky)."""
    cam = L.look_at(eye, target, fov, w, hgt); img = np.empty((hgt, w, 3), np.uint8); img[:] = sky; zb = np.full((hgt, w), np.inf); ib = np.zeros((hgt, w), np.int16)
    Lg = np.array(light, float); Lg /= np.linalg.norm(Lg)
    for P, col, two, iid in layers:
        P = np.asarray(P, float)
        if len(P) == 0: continue
        n = np.cross(P[:, 1] - P[:, 0], P[:, 2] - P[:, 0]); ln = np.linalg.norm(n, axis=1) + 1e-12; n = n / ln[:, None]
        cen = P.mean(axis=1); facing = ((cam[0] - cen) * n).sum(axis=1) > 0
        keep = np.ones(len(P), bool) if two else facing
        nn = np.where(facing[:, None], n, -n); shade = 0.45 + 0.55 * np.clip(nn @ Lg, 0, 1)
        flat = P.reshape(-1, 3); sp, z = L.project(cam, flat, w, hgt); sp = sp.reshape(-1, 3, 2); z = z.reshape(-1, 3)
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
            sub[hit] = zz[hit]; img[y0:y1 + 1, x0:x1 + 1][hit] = np.clip(np.array(col, float) * shade[t], 0, 255).astype(np.uint8); ib[y0:y1 + 1, x0:x1 + 1][hit] = iid
    return img, zb, ib


def shown(layers, ids, eye, target, fov, w=960, hgt=540):
    """(pixels of the items `ids` that are seen, pixels they would cover with nothing else drawn, share seen, share of the frame)."""
    _, _, ib = render(layers, eye, target, fov, w, hgt); seen = int(np.isin(ib, list(ids)).sum())
    _, _, ia = render([l for l in layers if l[3] in ids], eye, target, fov, w, hgt); alone = int((ia != 0).sum())
    return seen, alone, (seen / alone if alone else 0.0), seen / float(w * hgt)


def eye_on(x, z, eye_m=1.6): return (x, h(x, z) + eye_m, z)


def along_back(line, end_index, metres):
    """the point `metres` back along a polyline from its vertex end_index (toward index 0)."""
    left = metres; i = end_index
    while i > 0:
        a = line[i]; b = line[i - 1]; d = math.hypot(a[0] - b[0], a[1] - b[1])
        if d >= left: t = left / d; return (a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t)
        left -= d; i -= 1
    return line[0]


def sha12(p): return hashlib.sha256(Path(p).read_bytes()).hexdigest()[:12]
