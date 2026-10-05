"""#308 mesh track - offline evidence (read only, INFERRED; the editor command BuildingFix308.Meshes "probe:<alias>" decides).

DEFECTS_308 rows 0459 (torn rock shards over the jeokro crag cliff path) and 0479 (degenerate triangle sheet at the cheolong
sansa approach) from the mesh assets alone, no editor:
  - parses the Unity mesh YAML of the owner shells (every LOD + collider mesh) and of the terrain tile under them,
  - topology per mesh: zero-area / winding-flipped triangles, rim loops, non-manifold edges, LODn -> LOD0 deviation,
  - rim against the ground (terrain tile, plus the neighbouring collider meshes where the shell stands on a built trail),
  - the audit still's eye against the shell (14 rays: which hit a back face first),
  - a software projection of the meshes from the still's eye with back-face culling (reproduces the 'shards').
Writes Art/World/Compact/Rebuild/BuildingAudit308/offline/mesh308/ (mesh308-offline.json, <id>-inside.png, <id>-nocull.png).

    python Tools/resource_guard.py --wait && python Tools/Art/mesh308_offline.py
"""
import io, json, math, re, sys
from collections import deque
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[2]
WORLD = ROOT / 'Oheangbu/Assets/_Project/Art/World'
AUDIT = ROOT / 'Art/World/Compact/Rebuild/BuildingAudit308'
OUT = AUDIT / 'offline/mesh308'
FMT = {0: ('<f4', 4), 1: ('<f2', 2), 2: ('u1', 1), 3: ('i1', 1), 4: ('<u2', 2), 5: ('<i2', 2), 6: ('u1', 1), 7: ('i1', 1), 8: ('<u2', 2), 9: ('<i2', 2), 10: ('<u4', 4), 11: ('<i4', 4)}
DIRS = [(1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1)] + [(x, y, z) for x in (1, -1) for y in (1, -1) for z in (1, -1)]


def load_mesh(path, offset=None):
    """positions (n,3 float64, world when the object sits at `offset`) and triangles (m,3) of a Unity .asset mesh (YAML)."""
    txt = io.open(path, 'r', encoding='utf-8', errors='replace').read()
    fmt = int(re.search(r'm_IndexFormat: (\d+)', txt).group(1))
    idx = np.frombuffer(bytes.fromhex(re.search(r'm_IndexBuffer: ([0-9a-fA-F]*)', txt).group(1)), dtype='<u2' if fmt == 0 else '<u4').astype(np.int64)
    vc = int(re.search(r'm_VertexCount: (\d+)', txt).group(1))
    chans = [(int(a), int(b), int(c), int(d) & 0xF) for a, b, c, d in re.findall(r'- stream: (\d+)\s+offset: (\d+)\s+format: (\d+)\s+dimension: (\d+)', txt)]
    data = bytes.fromhex(re.search(r'_typelessdata: ([0-9a-fA-F]*)', txt).group(1))
    stride = 0
    for s, o, f, d in chans:
        if s == 0 and d: stride = max(stride, o + FMT[f][1] * d)
    stride = (stride + 3) // 4 * 4
    s, o, f, d = chans[0]
    raw = np.frombuffer(data, dtype=np.uint8, count=stride * vc).reshape(vc, stride)
    pos = raw[:, o:o + 12].copy().view('<f4').reshape(vc, 3).astype(np.float64)
    if offset is not None: pos = pos + np.array(offset, float)
    return pos, idx.reshape(-1, 3)


def topology(pos, tri, weld=1e-3):
    key = np.round(pos / weld).astype(np.int64)
    _, inv = np.unique(key, axis=0, return_inverse=True); inv = inv.reshape(-1)
    wp = np.zeros((inv.max() + 1, 3)); wp[inv] = pos
    t = inv[tri]
    a, b, c = pos[tri[:, 0]], pos[tri[:, 1]], pos[tri[:, 2]]
    area = .5 * np.linalg.norm(np.cross(b - a, c - a), axis=1)
    good = (t[:, 0] != t[:, 1]) & (t[:, 1] != t[:, 2]) & (t[:, 0] != t[:, 2])
    edges = {}
    for ti in np.nonzero(good)[0]:
        x, y, z = t[ti]
        for u, v in ((x, y), (y, z), (z, x)):
            edges.setdefault((min(u, v), max(u, v)), []).append((int(ti), bool(u < v)))
    rim = [k for k, l in edges.items() if len(l) == 1]
    nonman = sum(1 for l in edges.values() if len(l) > 2)
    parent = {}
    def find(x):
        while parent.setdefault(x, x) != x:
            parent[x] = parent[parent[x]]; x = parent[x]
        return x
    for u, v in rim: parent[find(int(u))] = find(int(v))
    loops = len({find(k) for k in list(parent)})
    adj = {}
    for l in edges.values():
        if len(l) == 2:
            (t1, d1), (t2, d2) = l
            adj.setdefault(t1, []).append((t2, d1 == d2)); adj.setdefault(t2, []).append((t1, d1 == d2))
    flip = np.zeros(len(t), bool); seen = np.zeros(len(t), bool); flipped = np.zeros(len(t), bool)
    for s in np.nonzero(good)[0]:
        s = int(s)
        if seen[s]: continue
        comp = [s]; seen[s] = True; q = deque([s])
        while q:
            x = q.popleft()
            for y, same in adj.get(x, ()):
                if not seen[y]: seen[y] = True; flip[y] = flip[x] ^ same; comp.append(y); q.append(y)
        comp = np.array(comp); f = flip[comp]
        invert = area[comp][f].sum() > area[comp][~f].sum()
        flipped[comp[f != invert]] = True
    rim_v = np.unique(np.array(rim).reshape(-1)) if rim else np.zeros(0, int)
    return dict(vertices=len(pos), welded=len(wp), triangles=len(tri), zeroArea=int((~good).sum() + ((area < 1e-8) & good).sum()), flipped=int(flipped.sum()),
                flippedArea=round(float(area[flipped].sum()), 3), area=round(float(area.sum()), 1), boundaryEdges=len(rim), loops=loops, nonManifold=nonman,
                maxEdge=round(float(max(np.linalg.norm(a - b, axis=1).max(), np.linalg.norm(b - c, axis=1).max(), np.linalg.norm(c - a, axis=1).max())), 2)), wp[rim_v]


def ray_hits(orig, d, pos, tri):
    a, b, c = pos[tri[:, 0]], pos[tri[:, 1]], pos[tri[:, 2]]
    e1 = b - a; e2 = c - a; h = np.cross(d, e2); det = (e1 * h).sum(1)
    ok = np.abs(det) > 1e-12; f = np.where(ok, 1.0 / np.where(ok, det, 1), 0)
    s = orig - a; u = f * (s * h).sum(1); q = np.cross(s, e1); v = f * (q @ d); tt = f * (e2 * q).sum(1)
    m = ok & (u >= 0) & (v >= 0) & (u + v <= 1) & (tt > 1e-6)
    return tt[m], (np.cross(e1, e2)[m] @ d)      # distance; > 0 = the ray leaves through the front = a back-face hit


def inside(eye, pos, tri):
    hits = back = 0; up = False
    for d in DIRS:
        dd = np.array(d, float); dd /= np.linalg.norm(dd)
        tt, sg = ray_hits(eye, dd, pos, tri)
        if not len(tt): continue
        hits += 1
        if sg[np.argmin(tt)] > 0:
            back += 1; up = up or d == (0, 1, 0)
    return dict(rayHits=hits, rayBack=back, upBack=up, inside=bool(hits and up and back >= .5 * hits))


class Ground:
    def __init__(self, meshes, lo, hi):
        P = []; T = []; off = 0
        for pos, tri in meshes:
            a = pos[tri]; mn = a.min(1); mx = a.max(1)
            k = (mx[:, 0] >= lo[0]) & (mn[:, 0] <= hi[0]) & (mx[:, 2] >= lo[1]) & (mn[:, 2] <= hi[1])
            P.append(pos); T.append(tri[k] + off); off += len(pos)
        p = np.concatenate(P); t = np.concatenate(T)
        self.a, self.b, self.c = p[t[:, 0]], p[t[:, 1]], p[t[:, 2]]
    def at(self, x, z, below=None):
        a, b, c = self.a, self.b, self.c
        d = (b[:, 2] - c[:, 2]) * (a[:, 0] - c[:, 0]) + (c[:, 0] - b[:, 0]) * (a[:, 2] - c[:, 2])
        ok = np.abs(d) > 1e-12; dd = np.where(ok, d, 1)
        l1 = ((b[:, 2] - c[:, 2]) * (x - c[:, 0]) + (c[:, 0] - b[:, 0]) * (z - c[:, 2])) / dd
        l2 = ((c[:, 2] - a[:, 2]) * (x - c[:, 0]) + (a[:, 0] - c[:, 0]) * (z - c[:, 2])) / dd
        m = ok & (l1 >= -1e-6) & (l2 >= -1e-6) & (1 - l1 - l2 >= -1e-6)
        if not m.any(): return math.nan
        y = l1[m] * a[m, 1] + l2[m] * b[m, 1] + (1 - l1[m] - l2[m]) * c[m, 1]
        if below is not None: y = y[y <= below]
        return float(y.max()) if len(y) else math.nan


def rim_gap(rim, terrain, others, window=2.5):
    gaps = []
    for x, y, z in rim:
        g = terrain.at(x, z)
        o = others.at(x, z, below=y + window) if others is not None else math.nan
        g = np.nanmax([g, o]) if not (math.isnan(g) and math.isnan(o)) else math.nan
        if not math.isnan(g): gaps.append(y - g)
    gaps = np.array(gaps)
    return dict(vertices=len(rim), gapMin=round(float(gaps.min()), 2), gapMedian=round(float(np.median(gaps)), 2), gapMax=round(float(gaps.max()), 2),
                above=int((gaps > .05).sum()), enterable=int((gaps > .5).sum()))


def deviation(pos, pos0, tri0, samples=400):
    a, b, c = pos0[tri0[:, 0]], pos0[tri0[:, 1]], pos0[tri0[:, 2]]
    ab = b - a; ac = c - a; best = []
    for p in pos[::max(1, len(pos) // samples)]:
        ap = p - a; d1 = (ab * ap).sum(1); d2 = (ac * ap).sum(1)
        bp = p - b; d3 = (ab * bp).sum(1); d4 = (ac * bp).sum(1)
        cp = p - c; d5 = (ab * cp).sum(1); d6 = (ac * cp).sum(1)
        va = d3 * d6 - d5 * d4; vb = d5 * d2 - d1 * d6; vc = d1 * d4 - d3 * d2
        den = va + vb + vc; den = np.where(np.abs(den) < 1e-12, 1e-12, den)
        q = a + ab * (vb / den)[:, None] + ac * (vc / den)[:, None]
        safe = lambda n, d: n / np.where(np.abs(d) < 1e-12, 1e-12, d)
        mA = (d1 <= 0) & (d2 <= 0); mB = (d3 >= 0) & (d4 <= d3); mC = (d6 >= 0) & (d5 <= d6)
        mAB = (vc <= 0) & (d1 >= 0) & (d3 <= 0) & ~mA & ~mB; mAC = (vb <= 0) & (d2 >= 0) & (d6 <= 0) & ~mA & ~mC
        mBC = (va <= 0) & ((d4 - d3) >= 0) & ((d5 - d6) >= 0) & ~mB & ~mC
        q = np.where(mAB[:, None], a + ab * safe(d1, d1 - d3)[:, None], q); q = np.where(mAC[:, None], a + ac * safe(d2, d2 - d6)[:, None], q)
        q = np.where(mBC[:, None], b + (c - b) * safe(d4 - d3, (d4 - d3) + (d5 - d6))[:, None], q)
        q = np.where(mA[:, None], a, q); q = np.where(mB[:, None], b, q); q = np.where(mC[:, None], c, q)
        best.append(np.linalg.norm(p - q, axis=1).min())
    return round(float(max(best)), 3)


def render(path, eye, target, layers, cull, w=1600, h=900, fov=60.0):
    eye = np.array(eye, float); f = np.array(target, float) - eye; f /= np.linalg.norm(f)
    r = np.cross([0, 1, 0], f); r /= np.linalg.norm(r); u = np.cross(f, r); t = math.tan(math.radians(fov) / 2)
    img = Image.new('RGB', (w, h), (235, 230, 215)); draw = ImageDraw.Draw(img)
    for pos, tri, colour in layers:
        d = pos - eye; z = d @ f
        sx = ((d @ r) / (z * t * (w / h)) * .5 + .5) * w; sy = (.5 - (d @ u) / (z * t) * .5) * h
        a, b, c = pos[tri[:, 0]], pos[tri[:, 1]], pos[tri[:, 2]]
        front = (np.cross(b - a, c - a) * (eye - a)).sum(1) > 0
        for i in np.argsort(-z[tri].mean(1)):
            if cull and not front[i]: continue
            A, B, C = tri[i]
            if min(z[A], z[B], z[C]) < .3: continue
            draw.polygon([(sx[A], sy[A]), (sx[B], sy[B]), (sx[C], sy[C])], fill=colour, outline=(0, 0, 0))
    img.save(path)


CLIFF = WORLD / 'Finish297/CliffPath/Meshes'
BAND = WORLD / 'Reworld292/Highlands293/Sections/cheolong_0A'
SITES = [
    dict(id='0459', still='b308_p1_0459_C5_CliffPath_jeokro_crag_Trail_fail_eye', owner='Finish297_CliffPath/CliffPath_jeokro_crag_Face_0',
         eye=(1896.94, 265.43, 837.69), target=(1896.94, 263.38, 856.19),
         lods=[('LOD0', CLIFF / 'CliffPath_jeokro_crag_Face_0_LOD0.asset'), ('LOD1', CLIFF / 'CliffPath_jeokro_crag_Face_0_LOD1.asset'), ('LOD2', CLIFF / 'CliffPath_jeokro_crag_Face_0_LOD2.asset')],
         collider=CLIFF / 'CliffPath_jeokro_crag_Face_0_Collision.asset',
         terrain=(WORLD / 'Watershed295/Meshes/Terrain_03_01_LOD0.asset', (1500, 0, 500)), others=[],
         extras=[(CLIFF / 'CliffPath_jeokro_crag_Footing_LOD0.asset', (160, 120, 80)), (CLIFF / 'CliffPath_jeokro_crag_Trail_LOD0.asset', (200, 200, 255)), (CLIFF / 'CliffPath_jeokro_crag_Lip_LOD0.asset', (120, 160, 120))]),
    dict(id='0479', still='b308_p1_0479_C7_mountain_cheolong_sansa_fail_eye', owner='Highlands293/mountain_cheolong/cheolong_0A/Band',
         eye=(515.23, 193.03, 3203.25), target=(507.21, 189.36, 3186.58),
         lods=[('LOD0', BAND / 'Band_LOD0.asset'), ('LOD1', BAND / 'Band_LOD1.asset'), ('LOD2', BAND / 'Band_LOD2.asset')],
         collider=BAND / 'Band_LOD2.asset',
         terrain=(WORLD / 'Finish297/Meshes/Terrain/Terrain_01_06_LOD0.asset', (500, 0, 3000)), others=[BAND / 'Collision_0.asset', BAND / 'Apron_LOD2.asset'],
         extras=[(BAND / 'Apron_LOD0.asset', (150, 110, 70)), (BAND / 'Trail_0.asset', (120, 200, 220))]),
]


def main():
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
    OUT.mkdir(parents=True, exist_ok=True)
    report = dict(note='offline, INFERRED from mesh assets; BuildingFix308.Meshes probe:<alias> is the authority', sites=[])
    for s in SITES:
        eye = np.array(s['eye'])
        meshes = [(name, load_mesh(path)) for name, path in s['lods']] + [('collider', load_mesh(s['collider']))]
        box = np.concatenate([m[0] for _, m in meshes]); lo = box.min(0)[[0, 2]] - 5; hi = box.max(0)[[0, 2]] + 5
        terrain = Ground([load_mesh(s['terrain'][0], s['terrain'][1])], lo, hi)
        others = Ground([load_mesh(p) for p in s['others']], lo, hi) if s['others'] else None
        ground = terrain.at(eye[0], eye[2])
        site = dict(id=s['id'], still=s['still'], owner=s['owner'], eye=list(s['eye']), terrainUnderEye=round(ground, 2), eyeOverTerrain=round(float(eye[1] - ground), 2), rows=[])
        pos0, tri0 = meshes[0][1]
        for name, (pos, tri) in meshes:
            topo, rim = topology(pos, tri)
            row = dict(mesh=name, **topo, **inside(eye, pos, tri))
            if name in ('LOD0', 'collider'): row['rim'] = rim_gap(rim, terrain, others)
            if name in ('LOD1', 'LOD2'): row['deviationMax'] = deviation(pos, pos0, tri0)
            up = ray_hits(eye, np.array([0., 1., 0.]), pos, tri)[0]
            row['shellOverEye'] = round(float(up.min()), 2) if len(up) else None
            site['rows'].append(row)
            print(s['id'], name, json.dumps(row, ensure_ascii=False))
        sealed = all(r['rim']['enterable'] == 0 for r in site['rows'] if r['mesh'] == 'collider')
        broken = any(r['nonManifold'] > 0 or r['flippedArea'] > .02 * r['area'] or r.get('deviationMax', 0) > 1.0 for r in site['rows'])
        site['verdict'] = 'REAL (broken mesh)' if broken else 'FALSE-POSITIVE' if site['rows'][0]['inside'] and sealed else 'REAL (open rim)' if site['rows'][0]['inside'] else 'UNEXPLAINED'
        layers = [(pos0, tri0, (90, 90, 90))] + [load_mesh(p) + (c,) for p, c in s['extras']]
        render(OUT / (s['id'] + '-inside.png'), s['eye'], s['target'], layers, True)
        render(OUT / (s['id'] + '-nocull.png'), s['eye'], s['target'], layers, False)
        print(s['id'], 'verdict', site['verdict'], '| eye', site['eyeOverTerrain'], 'm over the terrain')
        report['sites'].append(site)
    (OUT / 'mesh308-offline.json').write_text(json.dumps(report, ensure_ascii=False, indent=1), encoding='utf-8')
    print('->', OUT)


if __name__ == '__main__':
    main()
