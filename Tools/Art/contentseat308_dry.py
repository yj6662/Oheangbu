# -*- coding: utf-8 -*-
"""ContentSeat308 offline dry run (#308 seat fix, SEATFIX308_SCOPE.md). Pure Python: scene YAML + the stage-1a height field + sheets.
Nothing in the Unity project is touched and the editor queue is never used.

  python Tools/Art/contentseat308_dry.py dry [--scene main|arch296|folk298|all] [--data <contentseat308.json>] [--height <height .bytes>]
      per op (D1 .. D6) the predicted result; the same names ContentSeat308 plan / verify print as "num <name> <value>".
      The limits the editor plan enforces (D1 trunk / burial / first step / route / respawn feet, D2 feet / spec place / plinth,
      D5 slope / end gaps / route / logger) are judged here too: a violation is printed as "BLOCKED …", listed under "blocked" in
      the JSON, and the exit code is 2.
      -> Art/Playtest308/Pacing/seat308_dry_<alias>.json (+ .txt); --out <dir> writes somewhere else (trial data)
  python Tools/Art/contentseat308_dry.py patch-data [--candidate 0] [--write]
      the two rows of content308_scene.json that have to follow the repair (inn part offset / yaw, the three firewood rows off).
      Without --write it only prints the change. With --write it copies the file to Art/Playtest308/Pacing/Backups/seat308-data-<utc>/
      first and edits those rows in place (text edit, the rest of the file keeps its bytes).
  python Tools/Art/contentseat308_dry.py unpatch-data [--write]
      puts the two rows back to the values the content pass used (inn.expect_before in the data, firewood rows on).

Limits: terrain = the 4 m height grid (triangle interpolation), up to 0.10 m off the editor's colliders; trunks = sheet rows of
Tree / Shrub prototypes (trunk positions, not canopies); the grass count is a heuristic scan of the binary field asset.

D308-16 relayout (308.seat.3 data, Tools/Unity/Stage308_relayout): the default terrain is the live stage-1b field
(Surface/height_1b.bytes = plan/Stage/height_p1b.bytes); --height takes a path or one of height_1a | height_1b | height_p1b.
D5 reads its "before" places from the prop rows of content308_scene.json ("expect_before": "scene_row"); D7 (door hinges) and
D8 (per-piece re-seat move) are predicted when the data carries "hinges" / "reseat". D8 uses the pieces' box colliders as their
bounds (the editor uses the renderer bounds). The tool before this change: contentseat308_dry.py.pre_relayout."""
import argparse, datetime, hashlib, json, math, os, re, shutil, struct, sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import buildingaudit308_offline as B

ROOT = Path(__file__).resolve().parents[2]
PROJECT = ROOT / 'Oheangbu'
STAGE_DATA = ROOT / 'Tools/Unity/Stage308_relayout/Data/contentseat308.json'
LIVE_DATA = ROOT / 'Art/World/Compact/Rebuild/CliffBoundary308/contentseat308.json'
OUT = ROOT / 'Art/Playtest308/Pacing'
HEIGHT_1A = PROJECT / 'Assets/_Project/Art/World/CliffBoundary308/Surface/height_1a.bytes'
HEIGHT_1B = PROJECT / 'Assets/_Project/Art/World/CliffBoundary308/Surface/height_1b.bytes'
HEIGHT_P1B = ROOT / 'Art/World/Compact/Rebuild/CliffBoundary308/plan/Stage/height_p1b.bytes'
HEIGHTS = {'height_1a': HEIGHT_1A, 'height_1b': HEIGHT_1B, 'height_p1b': HEIGHT_P1B}
ROUTES = ROOT / 'Art/World/Compact/Rebuild/Architecture296/Generated/routes.json'
SCENES = {
    'arch296': 'Assets/_Project/Art/World/Architecture296/W_Demo_Compact_Architecture296.unity',
    'folk298': 'Assets/_Project/Art/Characters/Folklore298/W_Demo_Compact_Folklore298.unity',
    'main': 'Assets/_Project/Scenes/World/W_Demo_Main.unity',
}
CONTENT = {
    'arch296': 'Assets/_Project/Art/World/Architecture296/Data/a3cb428dc79f23e45a4caf7729b52193_7a0b9698e5728bd4ea84bd5116570b9f_2b30a7723e5289c47a4fd3cde0d788cf_Content.asset',
    'folk298': 'Assets/_Project/Art/Characters/Folklore298/Data/WorldContent298.asset',
    'main': 'Assets/_Project/Scenes/World/Main/WorldContent_Main.asset',
}
H_ROWS, H_COLS, H_CELL = 1501, 1001, 4.0
CLS = {23: 'MeshRenderer', 33: 'MeshFilter', 64: 'MeshCollider', 65: 'BoxCollider', 136: 'CapsuleCollider', 135: 'SphereCollider', 108: 'Light'}


def sha(path):
    h = hashlib.sha256()
    with open(path, 'rb') as f:
        for chunk in iter(lambda: f.read(1 << 20), b''): h.update(chunk)
    return h.hexdigest()


# ---------------------------------------------------------------------------------------------- terrain

class Height:
    def __init__(self, path):
        data = Path(path).read_bytes()
        if len(data) != H_ROWS * H_COLS * 4: raise SystemExit('unexpected height field size %d: %s' % (len(data), path))
        self.h = struct.unpack('<%df' % (H_ROWS * H_COLS), data)
        self.path = str(path); self.sha = hashlib.sha256(data).hexdigest()

    def __call__(self, x, z):
        j = x / H_CELL; i = z / H_CELL; j0 = int(math.floor(j)); i0 = int(math.floor(i)); fx = j - j0; fz = i - i0
        h = self.h; W = H_COLS
        h00 = h[i0 * W + j0]; h10 = h[i0 * W + j0 + 1]; h01 = h[(i0 + 1) * W + j0]; h11 = h[(i0 + 1) * W + j0 + 1]
        return h00 + (h10 - h00) * fx + (h01 - h00) * fz if fx + fz <= 1 else h11 + (h01 - h11) * (1 - fx) + (h10 - h11) * (1 - fz)

    def normal(self, x, z, d):
        gx = (self(x + d, z) - self(x - d, z)) / (2 * d); gz = (self(x, z + d) - self(x, z - d)) / (2 * d)
        return norm((-gx, 1.0, -gz))


def norm(v):
    l = math.sqrt(sum(c * c for c in v)); return tuple(c / l for c in v)


def dot(a, b): return sum(x * y for x, y in zip(a, b))


def cross(a, b): return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])


def add(a, b, k=1.0): return tuple(x + y * k for x, y in zip(a, b))


def fwd(yaw, lx, lz):
    """house / rest frame -> world delta (Unity yaw, Quaternion.Euler(0, yaw, 0) * (lx, 0, lz))."""
    c = math.cos(math.radians(yaw)); s = math.sin(math.radians(yaw))
    return lx * c + lz * s, -lx * s + lz * c


def inv(yaw, dx, dz):
    c = math.cos(math.radians(yaw)); s = math.sin(math.radians(yaw))
    return dx * c - dz * s, dx * s + dz * c


def rect_out(rect, lx, lz):
    """signed distance of a house-frame point to a rect (x0, x1, z0, z1): > 0 outside, < 0 inside."""
    x0, x1, z0, z1 = rect
    ex = max(x0 - lx, 0, lx - x1); ez = max(z0 - lz, 0, lz - z1)
    return math.hypot(ex, ez) if (ex > 0 or ez > 0) else -min(lx - x0, x1 - lx, lz - z0, z1 - lz)


def seg_dist(p, a, b):
    dx, dz = b[0] - a[0], b[1] - a[1]; l2 = dx * dx + dz * dz
    t = 0.0 if l2 < 1e-8 else max(0.0, min(1.0, ((p[0] - a[0]) * dx + (p[1] - a[1]) * dz) / l2))
    return math.hypot(p[0] - a[0] - dx * t, p[1] - a[1] - dz * t)


# ---------------------------------------------------------------------------------------------- scene (keys + components)

# the two session references ContentSeat308.Hinge.cs HingeOf() knows (hinges.rows[].session_field)
SESSION_HINGE_FIELDS = ('InnRestPresentation', 'VillageRestPresentation')


class SceneK:
    def __init__(self, path):
        lines = list(B.read_scene_lines(path))
        self.sc = B.Scene(lines); self.comps = {}; self.roots = []; self.sha = sha(path); self.hinges = []
        self.session_refs = {}   # session field name -> [fileID of the component it references] (D7: the editor reaches the hinge this way)
        cur = None; cls = None; info = None; in_roots = False
        for line in lines:
            m = B.HDR.match(line)
            if m:
                cls = int(m.group(1)); cur = m.group(2); info = {'id': cur}; in_roots = (cls == 1660057539); continue
            if cur is None: continue
            s = line.rstrip('\n')
            if in_roots:
                if s.startswith('  - {fileID: '): self.roots.append(B.FILEID.search(s).group(1))
                continue
            if cls == 114:
                # WorldMacroInnRestPresentation: Door (a Transform), HingeWorld, PointId - found by its field names
                if s.startswith('  m_GameObject: '): info['go'] = B.FILEID.search(s).group(1)
                elif s.startswith('  Door: '): info['door'] = B.FILEID.search(s).group(1)
                elif s.startswith('  HingeWorld: '):
                    v = V3.search(s)
                    if v: info['hinge'] = tuple(float(x) for x in v.groups()); self.hinges.append(info)
                elif s.startswith('  PointId: '): info['point'] = s[len('  PointId: '):].strip()
                else:
                    for field in SESSION_HINGE_FIELDS:
                        if s.startswith('  %s: {fileID: ' % field): self.session_refs.setdefault(field, []).append(B.FILEID.search(s).group(1))
                continue
            if cls in CLS:
                if s.startswith('  m_GameObject: '): self.comps.setdefault(B.FILEID.search(s).group(1), []).append((cls, cur, info))
                elif s.startswith('  m_Enabled: '): info['on'] = s.endswith('1')
                elif s.startswith('  m_Mesh: '):
                    g = B.GUID.search(s); info['mesh'] = (B.FILEID.search(s).group(1), g.group(1) if g else None)
                elif cls == 65 and s.startswith('  m_Size: '):
                    v = V3.search(s); info['size'] = tuple(float(x) for x in v.groups()) if v else None
                elif cls == 65 and s.startswith('  m_Center: '):
                    v = V3.search(s); info['center'] = tuple(float(x) for x in v.groups()) if v else None
        self.by_key = {}
        for t in self.sc.nodes: self.by_key.setdefault(self.key_of(t), t)

    def key_of(self, tid):
        sc = self.sc; parts = []; t = tid; k = 0
        while t and t != '0' and t in sc.nodes and k < 200:
            n = sc.nodes[t]; seg = n.name or '?'
            if n.father in sc.nodes:
                sibs = [c for c in sc.children_of(n.father) if sc.nodes[c].name == n.name]
                if len(sibs) > 1: seg += '#%d' % sibs.index(t)
            else:
                same = [r for r in self.roots if r in sc.nodes and sc.nodes[r].name == n.name]
                if len(same) > 1: seg += '#%d' % same.index(t)
            parts.append(seg); t = n.father; k += 1
        return '/'.join(reversed(parts))

    def get(self, key):
        return self.by_key.get(key)

    def world(self, key):
        t = self.get(key)
        if t is None: return None
        p, q, s = self.sc.world(t)
        return p, B.yaw_of(q), q, s

    def comp(self, key, cls):
        t = self.get(key)
        if t is None: return []
        return [i for (c, _, i) in self.comps.get(self.sc.nodes[t].go, []) if c == cls]

    def subtree_keys(self, key):
        t = self.get(key)
        return [self.key_of(x) for x in self.sc.subtree(t)] if t else []

    def box_bounds(self, key):
        """world AABB (lo, hi) of every BoxCollider under a key, or None: the offline stand-in for the renderer bounds."""
        t = self.get(key)
        if t is None: return None
        lo = [float('inf')] * 3; hi = [float('-inf')] * 3; used = 0
        for x in self.sc.subtree(t):
            n = self.sc.nodes[x]; p, q, s = self.sc.world(x)
            for (c, _, info) in self.comps.get(n.go, []):
                if c != 65 or not info.get('size'): continue
                ce = info.get('center') or (0.0, 0.0, 0.0); sz = info['size']; used += 1
                for i in range(8):
                    v = tuple((ce[a] + sz[a] * (0.5 if (i >> a) & 1 else -0.5)) * s[a] for a in range(3)); w = B.qrot(q, v)
                    for a in range(3): lo[a] = min(lo[a], p[a] + w[a]); hi[a] = max(hi[a], p[a] + w[a])
        return (tuple(lo), tuple(hi)) if used else None


# ---------------------------------------------------------------------------------------------- content asset / routes / sheets

V3 = re.compile(r'\{x: ([-\d.eE+]+), y: ([-\d.eE+]+), z: ([-\d.eE+]+)\}')


def read_content(path):
    text = Path(path).read_text(encoding='utf-8', errors='replace')
    out = {'points': {}, 'feet': {}, 'paths': {}}
    sec = None
    cur = None
    for line in text.split('\n'):
        if re.match(r'^  [A-Za-z_]+:', line):
            sec = line.strip().split(':')[0]; cur = None
            if sec in ('MainPath', 'BranchPath'): out['paths'][sec] = []
            continue
        if sec == 'Points':
            m = re.match(r'^  - Id: (.*)$', line)
            if m: cur = m.group(1).strip(); continue
            if cur and line.startswith('    Position: '):
                v = V3.search(line); out['points'][cur] = tuple(float(x) for x in v.groups())
        elif sec == 'Checkpoints':
            m = re.match(r'^  - Id: (.*)$', line)
            if m: cur = m.group(1).strip(); continue
            if cur and line.startswith('    Feet: '):
                v = V3.search(line); out['feet'][cur] = tuple(float(x) for x in v.groups())
        elif sec in ('MainPath', 'BranchPath'):
            v = V3.search(line)
            if v and line.startswith('  - '): out['paths'][sec].append(tuple(float(x) for x in v.groups()))
    return out


def corridors(content, run_gap):
    """centre lines: routes.json + the content MainPath / BranchPath (a jump over run_gap starts a new run)."""
    lines = []
    if ROUTES.exists():
        for r in json.loads(ROUTES.read_text(encoding='utf-8-sig')).get('routes', []):
            pts = [(p['x'], p['z']) for p in r.get('points', [])]
            if len(pts) >= 2: lines.append((r['id'], pts))
    for name, pts in content['paths'].items():
        run = []
        for p in pts:
            q = (p[0], p[2])
            if run and math.hypot(q[0] - run[-1][0], q[1] - run[-1][1]) > run_gap:
                if len(run) >= 2: lines.append(('content-' + name, run))
                run = []
            run.append(q)
        if len(run) >= 2: lines.append(('content-' + name, run))
    return lines


def corridor_dist(p, lines):
    best = (float('inf'), '')
    for cid, pts in lines:
        for i in range(1, len(pts)):
            d = seg_dist(p, pts[i - 1], pts[i])
            if d < best[0]: best = (d, cid)
    return best


def read_trunks(window, sheets):
    """sheet rows of Tree (0) / Shrub (1) prototypes inside the window (x0, x1, z0, z1); sheets = data dry.trunk_sheets."""
    rows = []
    for rel in sheets:
        f = PROJECT / rel
        if not f.exists(): continue
        cat = {}; sec = None; cur = None; rid = None; proto = None
        with open(f, encoding='utf-8', errors='replace') as fh:
            for line in fh:
                if line.startswith('  ') and not line.startswith('   ') and not line.startswith('  - '):
                    sec = line.strip().split(':')[0]; cur = None; continue
                if sec == 'Prototypes':
                    if line.startswith('  - Id: '): cur = line[8:].strip()
                    elif cur and line.startswith('    Category: '): cat[cur] = int(line.split(':')[1])
                elif sec == 'FixedPlacements':
                    if line.startswith('  - Id: '): rid = line[8:].strip(); proto = None
                    elif line.startswith('    PrototypeId: '): proto = line[17:].strip()
                    elif line.startswith('    Position: ') and rid is not None:
                        v = V3.search(line)
                        if v:
                            x, y, z = (float(c) for c in v.groups())
                            if window[0] <= x <= window[1] and window[2] <= z <= window[3]:
                                rows.append((x, y, z, Path(rel).stem[-14:], rid, proto, cat.get(proto, -1)))
    return rows


# ---------------------------------------------------------------------------------------------- the dry run

class Out:
    def __init__(self):
        self.lines = []; self.num = {}; self.notes = []; self.blocked = []

    def block(self, step, why):
        self.blocked.append('%s: %s' % (step, why)); self.lines.append('  BLOCKED %s: %s' % (step, why))

    def say(self, s=''): self.lines.append(s)

    def n(self, name, value, unit=''):
        self.num[name] = round(float(value), 4); self.lines.append('    num %-34s %s%s' % (name, ('%.3f' % value), (' ' + unit) if unit else ''))


def rest_frame(content, H, rest_id):
    p = content['points'][rest_id]; feet = content['feet'][rest_id]
    g = (p[0], H(p[0], p[2]), p[2])
    yaw = math.degrees(math.atan2(feet[0] - g[0], feet[2] - g[2])) % 360.0
    return g, yaw, feet


def scene_part(scene_data, rest_id):
    for r in scene_data['rests']:
        if r['id'] == rest_id: return r['parts'][0]
    return None


def rotate_about(v, axis, deg):
    """Rodrigues: v turned about a unit axis (the same algebra as Unity's Quaternion.AngleAxis(deg, axis) * v)."""
    c = math.cos(math.radians(deg)); s = math.sin(math.radians(deg)); k = dot(axis, v); x = cross(axis, v)
    return tuple(v[i] * c + x[i] * s + axis[i] * k * (1 - c) for i in range(3))


def quat_rot(q, v):
    x, y, z, w = q; u = (x, y, z); t = cross(u, v); t = (2 * t[0], 2 * t[1], 2 * t[2]); c = cross(u, t)
    return (v[0] + w * t[0] + c[0], v[1] + w * t[1] + c[1], v[2] + w * t[2] + c[2])


def prefab_bounds(D):
    """every mesh of the bundle prefab as one box in the prefab root's frame (prefab YAML + each mesh asset's m_LocalAABB)."""
    bd = D['firewood']['bundle']; text = (PROJECT / bd['prefab']).read_text(encoding='utf-8', errors='replace')
    docs = re.split(r'^--- !u!(\d+) &(\d+)\s*$', text, flags=re.M)
    tr = {}; mf = {}
    for i in range(1, len(docs) - 2, 3):
        cls, body = int(docs[i]), docs[i + 2]
        go = re.search(r'm_GameObject: \{fileID: (\d+)\}', body)
        if cls == 4 and go:
            q = re.search(r'm_LocalRotation: \{x: ([-\d.eE+]+), y: ([-\d.eE+]+), z: ([-\d.eE+]+), w: ([-\d.eE+]+)\}', body)
            p = re.search(r'm_LocalPosition: ' + V3.pattern, body); sc = re.search(r'm_LocalScale: ' + V3.pattern, body)
            fa = re.search(r'm_Father: \{fileID: (\d+)\}', body)
            tr[go.group(1)] = (tuple(float(c) for c in q.groups()), tuple(float(c) for c in p.groups()), tuple(float(c) for c in sc.groups()), fa.group(1))
        elif cls == 33 and go:
            g = re.search(r'm_Mesh: \{fileID: \d+, guid: ([0-9a-f]{32})', body)
            if g: mf[go.group(1)] = g.group(1)
    aabb = {}
    for rel in D['dry']['mesh_search_dirs']:
        for meta in (PROJECT / rel).glob('*.asset.meta'):
            g = re.search(r'^guid: ([0-9a-f]{32})', meta.read_text(encoding='utf-8', errors='replace'), flags=re.M)
            if g and g.group(1) in mf.values():
                m = re.search(r'm_LocalAABB:\s+m_Center: ' + V3.pattern + r'\s+m_Extent: ' + V3.pattern, Path(str(meta)[:-5]).read_text(encoding='utf-8', errors='replace'))
                if m: v = [float(c) for c in m.groups()]; aabb[g.group(1)] = (v[:3], v[3:])
    lo = [float('inf')] * 3; hi = [float('-inf')] * 3; used = 0
    for go, guid in mf.items():
        if guid not in aabb or go not in tr: continue
        q, p, sc, father = tr[go]; c, e = aabb[guid]; used += 1
        if father != '0':   # one level under the root (the root itself sits at identity)
            pass
        for i in range(8):
            v = tuple((c[a] + e[a] * (1 if (i >> a) & 1 else -1)) * sc[a] for a in range(3)); w = quat_rot(q, v)
            w = tuple(w[a] + (p[a] if father != '0' else 0.0) for a in range(3))
            for a in range(3): lo[a] = min(lo[a], w[a]); hi[a] = max(hi[a], w[a])
    if used == 0: return None
    return tuple(lo), tuple(hi)


def inn_pose(g, yaw, cand, embed, H):
    dx, dz = fwd(yaw, cand['offset'][0], cand['offset'][1])
    px, pz = g[0] + dx, g[2] + dz
    gp = H(px, pz)
    return (px, gp - embed, pz), (yaw + cand['yaw']) % 360.0, gp


def eval_inn(o, D, H, content, trunks, lines, cand, label):
    inn = D['inn']; g, yaw, feet = rest_frame(content, H, inn['rest_id'])
    (px, py, pz), hyaw, gp = inn_pose(g, yaw, cand, cand.get('embed_m', inn['embed_m']), H)
    plinth = inn['plinth_rect']; roof = inn['roof_rect']
    worst = (99.0, None)
    for t in trunks:
        if t[6] not in (0, 1): continue
        lx, lz = inv(hyaw, t[0] - px, t[2] - pz)
        d = min(rect_out(plinth, lx, lz), rect_out(roof, lx, lz))
        if d < worst[0]: worst = (d, t[4])
    corners = {'FL': (plinth[0], plinth[3]), 'FR': (plinth[1], plinth[3]), 'BL': (plinth[0], plinth[2]), 'BR': (plinth[1], plinth[2])}
    o.say('  %s offset (%.1f, %.1f) part yaw %+g -> pivot (%.2f, %.2f, %.2f) yaw %.2f, ground at the pivot %.2f' % (label, cand['offset'][0], cand['offset'][1], cand['yaw'], px, py, pz, hyaw, gp))
    res = {'pivot': (px, py, pz), 'yaw': hyaw, 'trunk': worst[0], 'trunk_id': worst[1]}
    res['corner'] = {}
    for nm, (lx, lz) in corners.items():
        wx, wz = fwd(hyaw, lx, lz); gg = H(px + wx, pz + wz)
        res['corner'][nm] = gg - py
    gs = []
    x = plinth[0]
    while x <= plinth[1] + 1e-6:
        z = plinth[2]
        while z <= plinth[3] + 1e-6:
            wx, wz = fwd(hyaw, x, z); gs.append(H(px + wx, pz + wz) - py); z += 0.95
        x += 1.03
    res['under'] = (min(gs), max(gs))
    fx, fz = fwd(hyaw, *inn['step_foot_local']); res['foot'] = H(px + fx, pz + fz) - py
    # outline to the route centre lines
    best = (float('inf'), '')
    for u in (-1, -0.5, 0, 0.5, 1):
        for w in (-1, -0.5, 0, 0.5, 1):
            if abs(u) < 0.99 and abs(w) < 0.99: continue
            lx = (plinth[0] + plinth[1]) / 2 + (plinth[1] - plinth[0]) / 2 * u; lz = (plinth[2] + plinth[3]) / 2 + (plinth[3] - plinth[2]) / 2 * w
            wx, wz = fwd(hyaw, lx, lz); d = corridor_dist((px + wx, pz + wz), lines)
            if d[0] < best[0]: best = d
    res['corridor'] = best
    res['rest_local'] = inv(hyaw, g[0] - px, g[2] - pz); res['feet_local'] = inv(hyaw, feet[0] - px, feet[2] - pz)
    res['feet_out'] = rect_out(plinth, *res['feet_local'])
    return res


def step_tops(S, inn_key, step_keys):
    """step top above the pivot and its house-frame place, from the scene as it stands (rigid under the move)."""
    w = S.world(inn_key)
    if w is None: return []
    (px, py, pz), yaw, _, _ = w
    out = []
    for k in step_keys:
        sw = S.world(k)
        if sw is None: continue
        (x, y, z), _, _, s = sw
        lx, lz = inv(yaw, x - px, z - pz)
        out.append((k.split('/')[-1], lx, lz, y - py + s[1] / 2))
    return out


def dry_scene(alias, D, H, scene_data):
    o = Out()
    S = SceneK(PROJECT / SCENES[alias]); content = read_content(PROJECT / CONTENT[alias])
    lines = corridors(content, D['corridor_run_gap_m'])
    o.say('ContentSeat308 dry run %s (%s) scene sha %s' % (alias, SCENES[alias], S.sha[:12]))
    o.say('  terrain %s sha %s | data %s' % (Path(H.path).name, H.sha[:12], D['version']))
    inn = D['inn']
    # ------------------------------------------------------------ D1
    o.say(''); o.say('D1 hunter inn - move and lower')
    g, yaw, feet = rest_frame(content, H, inn['rest_id'])
    trunks = read_trunks((g[0] - 40, g[0] + 40, g[2] - 40, g[2] + 40), D['dry']['trunk_sheets'])
    hw = S.world(inn['holder_key']); iw = S.world(inn['inn_key'])
    o.say('  rest point ground (%.2f, %.2f, %.2f) front yaw %.3f, feet (%.2f, %.2f) | holder in the scene %s | inn now %s' % (g[0], g[1], g[2], yaw, feet[0], feet[2], ('(%.2f, %.2f, %.2f) yaw %.1f' % (*hw[0], hw[1])) if hw else 'MISSING', ('(%.2f, %.2f, %.2f) yaw %.1f' % (*iw[0], iw[1])) if iw else 'MISSING'))
    part = scene_part(scene_data, inn['rest_id'])
    o.say('  content308_scene.json part: offset %s yaw %s' % (part['offset'], part['yaw']))
    cands = inn['candidates'] if inn['mode'] == 'move' else [inn['in_place']]
    chosen = None
    for i, c in enumerate(cands):
        if chosen is None and abs(part['offset'][0] - c['offset'][0]) < 1e-3 and abs(part['offset'][2] - c['offset'][1]) < 1e-3 and abs(part['yaw'] - c['yaw']) < 1e-3 and abs(part['offset'][1] + c.get('embed_m', inn['embed_m'])) < 1e-3: chosen = i
    if chosen is None:
        o.say('  DATA MISMATCH: content308_scene.json names none of the candidates (offset[1] must be -embed) -> ContentSeat308 plan answers "refused: data mismatch"; run patch-data --write. The prediction below uses candidate 0.')
        o.block('data', 'content308_scene.json names no inn candidate (patch-data --write, or dry --assume-patched)')
    steps = step_tops(S, inn['inn_key'], inn['step_keys'])
    now = eval_inn(o, D, H, content, trunks, lines, {'offset': inn['expect_before']['offset'], 'yaw': inn['expect_before']['yaw']}, 'in place (-%.2f)' % inn['embed_m'])
    feet_clear = D['keeper']['feet_clear_m']
    o.say('      trunk clearance %+.2f m (%s) -> lower_in_place %s' % (now['trunk'], now['trunk_id'], 'refused' if now['trunk'] < inn['trunk_margin_m'] else 'possible'))
    use = chosen if chosen is not None else 0
    for i, c in enumerate(cands):
        r = eval_inn(o, D, H, content, trunks, lines, c, 'candidate %d%s' % (i, ' (chosen)' if i == use else ''))
        top = inn['plinth_top_m']
        bury = max(v - top for v in r['corner'].values())
        rises = sorted(t[3] - r['foot'] for t in steps if t[3] - r['foot'] > 0)
        first = rises[0] if rises else float('nan')
        # the same limits, in the same order, as ContentSeat308.EvalHouse (the collider overlap test is editor only)
        fails = []
        if r['trunk'] < inn['trunk_margin_m']: fails.append('trunk %s %.2f m from the outline (< %.2f)' % (r['trunk_id'], r['trunk'], inn['trunk_margin_m']))
        if bury > inn['bury_max_m']: fails.append('the ground stands %.2f m over the plinth top at a corner (> %.2f)' % (bury, inn['bury_max_m']))
        if first != first: fails.append('every entry step is under the ground at the step foot')
        elif first > inn['step_limit_m']: fails.append('first step %.2f m above the ground (> %.2f)' % (first, inn['step_limit_m']))
        if r['corridor'][0] < inn['corridor_clear_m']: fails.append('outline %.2f m from the centre line of %s (< %.2f)' % (r['corridor'][0], r['corridor'][1], inn['corridor_clear_m']))
        if r['feet_out'] < feet_clear: fails.append('the respawn feet are %.2f m from the plinth outline (< keeper.feet_clear_m %.2f)' % (r['feet_out'], feet_clear))
        ok = not fails
        if i == use:
            for f in fails: o.block('D1', 'candidate %d - %s' % (i, f))
        o.say('      trunk clearance %+.2f m (%s) | ground - pivot under the plinth %+.2f .. %+.2f | corners ground - pivot FL %+.2f FR %+.2f BL %+.2f BR %+.2f' % (r['trunk'], r['trunk_id'], r['under'][0], r['under'][1], r['corner']['FL'], r['corner']['FR'], r['corner']['BL'], r['corner']['BR']))
        o.say('      embed %.2f | plinth exposed (top %+.2f [I]) FL %.2f FR %.2f BL %.2f BR %.2f, deepest bury %+.2f (limit %.2f) | step foot ground - pivot %+.2f, first rise above the ground %.2f (limit %.2f)' % (c.get('embed_m', inn['embed_m']), top, top - r['corner']['FL'], top - r['corner']['FR'], top - r['corner']['BL'], top - r['corner']['BR'], bury, inn['bury_max_m'], r['foot'], first, inn['step_limit_m']))
        o.say('      outline to the nearest route centre line %.2f m (%s, need >= %.1f) | rest point in the house frame (%.2f, %.2f), feet (%.2f, %.2f), feet %.2f m outside the plinth (need >= %.2f) -> %s' % (r['corridor'][0], r['corridor'][1], inn['corridor_clear_m'], *r['rest_local'], *r['feet_local'], r['feet_out'], feet_clear, 'OK' if ok else 'FAILS: ' + '; '.join(fails)))
        if i == use:
            o.n('D1.pivot_x', r['pivot'][0]); o.n('D1.pivot_y', r['pivot'][1]); o.n('D1.pivot_z', r['pivot'][2]); o.n('D1.yaw', r['yaw'], 'deg')
            o.n('D1.trunk_clearance', r['trunk'])
            for nm in ('FL', 'FR', 'BL', 'BR'): o.n('D1.corner_%s' % nm, r['corner'][nm])
            o.n('D1.step_foot', r['foot']); o.n('D1.first_rise', first); o.n('D1.corridor', r['corridor'][0])
            o.n('D1.bury', bury); o.n('D1.feet_out', r['feet_out'])
            sel = r
    o.say('  entry steps (top above the pivot, house frame): ' + ', '.join('%s (%.2f, %.2f) top %+.2f' % t for t in steps))
    sk = S.comp(inn['old_skirt_key'], 23)
    o.say('  StoneSkirt299 renderer now %s -> off; StoneSkirt308 is generated in the editor (stone count / max gap are not computable offline)' % ('on' if sk and sk[0].get('on', True) else 'off' if sk else 'MISSING'))
    # ------------------------------------------------------------ D2
    kp = D['keeper']; o.say(''); o.say('D2 inn keeper - content point and body')
    kx, kz = fwd(sel['yaw'], *kp['inn_local']); kx += sel['pivot'][0]; kz += sel['pivot'][2]; ky = H(kx, kz)
    cp = content['feet'][kp['face_checkpoint']]; kyaw = math.degrees(math.atan2(cp[0] - kx, cp[2] - kz)) % 360.0
    old = content['points'].get(kp['id']); bw = S.world(kp['body_key'])
    nt = min((math.hypot(t[0] - kx, t[2] - kz), t[4]) for t in trunks if t[6] in (0, 1))
    o.say('  content point now %s, body now %s' % (('(%.2f, %.2f, %.2f)' % old) if old else 'MISSING', ('(%.2f, %.2f, %.2f) yaw %.1f' % (*bw[0], bw[1])) if bw else 'MISSING'))
    o.say('  new point (%.2f, %.2f, %.2f) yaw %.1f | to the respawn feet %.2f m (need >= %.1f) | from the spec place %.2f m (keep_near %.0f) | from the old point %.2f m | slope %.1f deg | nearest trunk %.1f m (%s) | outside the plinth %.2f m' % (
        kx, ky, kz, kyaw, math.hypot(kx - cp[0], kz - cp[2]), kp['feet_clear_m'], math.hypot(kx - kp['spec_xz'][0], kz - kp['spec_xz'][1]), kp['keep_near_m'],
        math.hypot(kx - old[0], kz - old[2]) if old else float('nan'), math.degrees(math.acos(H.normal(kx, kz, 0.6)[1])), nt[0], nt[1], rect_out(inn['plinth_rect'], *kp['inn_local'])))
    o.n('D2.x', kx); o.n('D2.y', ky); o.n('D2.z', kz); o.n('D2.yaw', kyaw, 'deg'); o.n('D2.feet_gap', math.hypot(kx - cp[0], kz - cp[2]))
    # the limits of ContentSeat308.PlanD2
    for cid, ft in content['feet'].items():
        d = math.hypot(kx - ft[0], kz - ft[2])
        if d < kp['feet_clear_m']: o.block('D2', '%.2f m from the respawn feet of %s (< %.2f)' % (d, cid, kp['feet_clear_m']))
    d_spec = math.hypot(kx - kp['spec_xz'][0], kz - kp['spec_xz'][1])
    if d_spec > kp['keep_near_m']: o.block('D2', '%.2f m from the spec place (> keep_near_m %.1f: content-apply would write the spec place again)' % (d_spec, kp['keep_near_m']))
    out_plinth = rect_out(inn['plinth_rect'], *kp['inn_local'])
    if out_plinth < kp['plinth_clear_m']: o.block('D2', 'only %.2f m outside the plinth outline (< %.2f)' % (out_plinth, kp['plinth_clear_m']))
    # dry only: the keeper must not stand on the straight walk lines to the entry steps
    foot = inn['step_foot_local']; kl = tuple(kp['inn_local'])
    d_feet = seg_dist(kl, sel['feet_local'], foot); d_rest = seg_dist(kl, sel['rest_local'], foot)
    o.say('  walk lines (house frame): respawn feet -> step foot %.2f m from the keeper, rest point -> step foot %.2f m (need >= %.2f)' % (d_feet, d_rest, kp['walk_line_clear_m']))
    o.n('D2.walk_line_feet', d_feet); o.n('D2.walk_line_rest', d_rest)
    if min(d_feet, d_rest) < kp['walk_line_clear_m']: o.block('D2', 'the keeper stands %.2f m from a walk line to the entry steps (< %.2f)' % (min(d_feet, d_rest), kp['walk_line_clear_m']))
    # ------------------------------------------------------------ D3
    ln = D['lanterns']; o.say(''); o.say('D3 porch lantern caps')
    n_on = n_off = 0; fixes = 0
    for lrel in ln['lantern_rel']:
        lk = inn['inn_key'] + '/' + lrel
        for key in S.subtree_keys(lk):
            mf = S.comp(key, 33); mr = S.comp(key, 23)
            if not mf or mf[0].get('mesh', ('0', None))[0] != '0': continue
            on = bool(mr and mr[0].get('on', True)); rel = key[len(lk) + 1:]
            if on: n_on += 1
            else: n_off += 1
            if on:
                src = S.comp(ln['source_inn_key'] + '/' + lrel + '/' + rel, 33)
                guid = src[0].get('mesh', ('0', None)) if src else None
                listed = rel in ln['surface_rel']
                cs = S.world(lk + '/' + rel.split('/')[0]); os_ = S.world(ln['source_inn_key'] + '/' + lrel + '/' + rel.split('/')[0])
                same = cs and os_ and max(abs(a - b) for a, b in zip(cs[3], os_[3])) < ln['size_tol_m']
                o.say('  DEFECT %s/%s: renderer on, no mesh -> the original uses mesh guid %s, support size %s%s' % (lrel, rel, guid[1] if guid and guid[0] != '0' else 'NONE (fallback: BuildCapMesh)', 'equal' if same else 'DIFFERENT (fallback)', '' if listed else ' (NOT in lanterns.surface_rel: left alone)'))
                if listed: fixes += 1
    o.say('  MeshFilters with no mesh under the two lanterns: renderer off %d (not a defect: retired primitives, the original has the same), renderer on %d -> %d repaired, %d left' % (n_off, n_on, fixes, n_on - fixes))
    o.n('D3.null_disabled', n_off); o.n('D3.null_enabled_before', n_on); o.n('D3.null_enabled_after', n_on - fixes)
    # ------------------------------------------------------------ D5
    fw = D['firewood']; bundles = fw['visual'] == 'bundles'; o.say(''); o.say('D5 firewood - one stack on the slope (visual %s)' % fw['visual'])
    if fw['visual'] not in ('boxes', 'bundles'): o.block('D5', "firewood.visual '%s' is not implemented (boxes | bundles)" % fw['visual'])
    cx, cz = fw['centre_xz']; n = H.normal(cx, cz, fw['normal_sample_m']); gy = H(cx, cz)
    down = norm((n[0], 0.0, n[2])); contour = cross((0.0, 1.0, 0.0), down)
    tang = norm(add(down, n, -dot(down, n))); right = norm(add(contour, n, -dot(contour, n))); fwdv = cross(right, n)
    m = [[right[0], n[0], fwdv[0]], [right[1], n[1], fwdv[1]], [right[2], n[2], fwdv[2]]]
    qw = math.sqrt(max(0.0, 1 + m[0][0] + m[1][1] + m[2][2])) / 2
    q = ((m[2][1] - m[1][2]) / (4 * qw), (m[0][2] - m[2][0]) / (4 * qw), (m[1][0] - m[0][1]) / (4 * qw), qw)
    slope = math.degrees(math.acos(n[1]))
    o.say('  stack centre (%.2f, %.2f, %.2f) normal (%.3f, %.3f, %.3f) slope %.1f deg (limit %.0f) | long axis bearing %.0f | stack rotation (%.4f, %.4f, %.4f, %.4f)' % (cx, gy, cz, *n, slope, fw['max_slope_deg'], math.degrees(math.atan2(right[0], right[2])) % 360, *q))
    o.n('D5.normal_x', n[0]); o.n('D5.normal_y', n[1]); o.n('D5.normal_z', n[2]); o.n('D5.ground', gy)
    if slope > fw['max_slope_deg']: o.block('D5', 'slope %.1f deg at the stack centre (> %.0f)' % (slope, fw['max_slope_deg']))
    logger = content['points'].get(fw['logger_id'])
    # the editor refuses D5 while the logger point is not near the stack (firewood.anchor_max_m): content-apply moves it first.
    # Offline the content asset may still hold the old place; the limits are then judged against firewood.logger_planned_xz.
    amax = fw.get('anchor_max_m', 0.0)
    if logger and amax > 0 and math.hypot(logger[0] - cx, logger[2] - cz) > amax:
        lp = fw.get('logger_planned_xz')
        if lp is None: o.block('D5', 'the logger point is %.1f m from the stack centre (> anchor_max_m %.1f) and the data has no logger_planned_xz' % (math.hypot(logger[0] - cx, logger[2] - cz), amax))
        else:
            o.say('  the logger content point is still at (%.1f, %.1f), %.1f m from the stack centre: Content308 content-apply (M04) has not run - the limits below use firewood.logger_planned_xz (%.1f, %.1f); the editor plan refuses D5 until the point is within %.1f m' % (logger[0], logger[2], math.hypot(logger[0] - cx, logger[2] - cz), lp[0], lp[1], amax))
            o.notes.append('D5 judged against logger_planned_xz (content-apply M04 not applied yet)')
            logger = (lp[0], H(lp[0], lp[1]), lp[1])
            if math.hypot(logger[0] - cx, logger[2] - cz) > amax: o.block('D5', 'logger_planned_xz is %.1f m from the stack centre (> anchor_max_m %.1f)' % (math.hypot(logger[0] - cx, logger[2] - cz), amax))
    lp_always = fw.get('logger_planned_xz')
    if lp_always is not None and amax > 0:
        far = math.hypot(lp_always[0] - cx, lp_always[1] - cz); o.n('D5.planned_logger_to_centre', far)
        o.say('  stack centre to firewood.logger_planned_xz (%.1f, %.1f): %.1f m (anchor_max_m %.1f) - judged whatever place the content asset holds today' % (lp_always[0], lp_always[1], far, amax))
        if far > amax: o.block('D5', 'the stack centre (%.1f, %.1f) is %.1f m from logger_planned_xz (> anchor_max_m %.1f): after the relayout (M04) the editor refuses D5' % (cx, cz, far, amax))
    part_rows = {p['id']: p for p in scene_data.get('props', [])}
    lay = fw['bundle']['collider'] if bundles else fw
    glo, ghi = fw['end_gap_m']

    def limits(name, p):
        cd = corridor_dist((p[0], p[2]), lines); ld = math.hypot(p[0] - logger[0], p[2] - logger[2]) if logger else float('inf')
        if cd[0] < fw['corridor_clear_m']: o.block('D5', '%s %.2f m from the centre line of %s (< %.1f)' % (name, cd[0], cd[1], fw['corridor_clear_m']))
        if ld < fw['logger_clear_m']: o.block('D5', '%s %.2f m from the logger point (< %.1f)' % (name, ld, fw['logger_clear_m']))
        return cd, ld

    for pc in fw['pieces']:
        w = S.world(pc['key'])
        if w is None: o.say('  %s MISSING in the scene' % pc['key']); o.block('D5', 'object not found: ' + pc['key']); continue
        scale = lay['scale'] if bundles else w[3]
        half_y = scale[1] / 2; half_x = scale[0] / 2
        along = 0.0 if bundles else pc.get('along_m', 0.0); twist = 0.0 if bundles else pc.get('twist_deg', 0.0)
        h = half_y - fw['sink_m'] + (lay['top_rise_m'] if pc['layer'] else 0.0)
        p = add(add(add((cx, gy, cz), right, along), tang, pc['side'] * lay['side_m']), n, h)
        pr = rotate_about(right, n, twist)
        ends = []
        for sgn in (-half_x, 0.0, half_x):
            u = add(add(p, pr, sgn), n, -half_y); ends.append(u[1] - H(u[0], u[2]))
        cd, ld = limits(pc['id'], p)
        row = part_rows.get(pc['id'], {})
        o.say('  %s now (%.2f, %.2f, %.2f) yaw %.0f -> (%.3f, %.3f, %.3f)%s | underside - ground at the ends %+.2f / mid %+.2f / %+.2f%s | route centre line %.1f m (%s) | logger %.1f m | scene data row enabled=%s%s' % (
            pc['id'], *w[0], w[1], *p, (' size %.2f x %.2f x %.2f (collider only, renderer off)' % tuple(scale)) if bundles else (' along %+.2f twist %+g' % (along, twist)),
            ends[0], ends[1], ends[2], '' if pc['layer'] else (' (limit %+.2f .. %+.2f)' % (glo, ghi)), cd[0], cd[1], ld, row.get('enabled'), '' if row.get('enabled') is False else '  <- must be false (patch-data), else scene-apply puts it back level'))
        eb = pc.get('expect_before', 'scene_row')
        if isinstance(eb, list): o.say('      expected before (pinned in the data): (%.2f, %.2f) yaw %g' % (eb[0], eb[1], eb[2]))
        elif not row or logger is None: o.block('D5', '%s: expect_before is scene_row but the prop row / its anchor point is missing' % pc['id'])
        else:
            ex, ez = logger[0] + row['offset'][0], logger[2] + row['offset'][1]
            o.say('      expected before (scene row: anchor %s + offset %s, yaw %g): (%.2f, %.2f) - %s' % (row.get('anchor'), row['offset'], row['yaw'], ex, ez, 'the piece stands there' if math.hypot(w[0][0] - ex, w[0][2] - ez) <= D['tolerances']['before_xz_m'] else 'the piece is %.1f m from it now (Content308 scene-apply moves it there before D5)' % math.hypot(w[0][0] - ex, w[0][2] - ez)))
            o.n('D5.%s.before_x' % pc['id'], ex); o.n('D5.%s.before_z' % pc['id'], ez)
            if row.get('anchor') != fw['logger_id']: o.block('D5', '%s: the prop row anchor %s is not firewood.logger_id %s' % (pc['id'], row.get('anchor'), fw['logger_id']))
        if not pc['layer'] and not (glo <= ends[0] <= ghi and glo <= ends[2] <= ghi): o.block('D5', '%s end underside - ground %.2f / %.2f outside %.2f .. %.2f' % (pc['id'], ends[0], ends[2], glo, ghi))
        if row.get('enabled') is not False: o.block('data', 'the firewood row %s is still enabled in content308_scene.json (patch-data --write, or dry --assume-patched)' % pc['id'])
        o.n('D5.%s.x' % pc['id'], p[0]); o.n('D5.%s.y' % pc['id'], p[1]); o.n('D5.%s.z' % pc['id'], p[2])
        o.n('D5.%s.end_a' % pc['id'], ends[0]); o.n('D5.%s.end_b' % pc['id'], ends[2])
    if not bundles:
        # are the end faces still in one plane? (the review's "one black beam")
        al = [pc.get('along_m', 0.0) for pc in fw['pieces']]; tw = [pc.get('twist_deg', 0.0) for pc in fw['pieces']]
        o.say('  end faces: along spread %.2f m, twist spread %.0f deg (0 / 0 = the three end faces share one plane)' % (max(al) - min(al), max(tw) - min(tw)))
    else:
        bd = fw['bundle']; pb = prefab_bounds(D)
        if pb is None: o.block('D5', 'bundle prefab or its mesh not readable offline: ' + bd['prefab'])
        else:
            lo, hi = pb; sc = bd['scale']; ext = [(hi[a] - lo[a]) / 2 for a in range(3)]; cen = [(hi[a] + lo[a]) / 2 for a in range(3)]
            o.say('  bundle prefab box (root frame) min (%.3f, %.3f, %.3f) max (%.3f, %.3f, %.3f) x scale %s -> %.2f m long, %.2f m wide, %.2f m high [O prefab YAML + mesh AABB]' % (*lo, *hi, sc, 2 * ext[2] * sc[2], 2 * ext[0] * sc[0], 2 * ext[1] * sc[1]))
            o.n('D5.bundle_len', 2 * ext[2] * sc[2]); o.n('D5.bundle_width', 2 * ext[0] * sc[0]); o.n('D5.bundle_height', 2 * ext[1] * sc[1])
            x0 = cross(n, right); blo, bhi = bd['end_gap_m']; names = set()
            for row in bd['rows']:
                if row['name'] in names: o.block('D5', 'bundle row name twice: ' + row['name'])
                names.add(row['name'])
                if S.get(bd['parent_key'] + '/' + row['name']) is not None: o.say('  %s already exists in the scene' % row['name'])
                up = row.get('up_m', 0.0); tw = row.get('twist_deg', 0.0)
                bx = rotate_about(x0, n, tw); bz = rotate_about(right, n, tw)
                c = add(add(add((cx, gy, cz), right, row.get('along_m', 0.0)), tang, row.get('side_m', 0.0)), n, up - (bd['sink_m'] if up <= 0 else 0.0))
                gaps = []
                for sx in (-1, 1):
                    for sz in (-1, 1):
                        wpt = add(add(c, bx, sx * ext[0] * sc[0]), bz, sz * ext[2] * sc[2]); gaps.append(wpt[1] - H(wpt[0], wpt[2]))
                cd, ld = limits(row['name'], c)
                o.say('  %s bottom centre (%.3f, %.3f, %.3f) twist %+g up %.2f | underside - ground at the box corners %+.2f .. %+.2f%s | route centre line %.1f m (%s) | logger %.1f m' % (
                    row['name'], *c, tw, up, min(gaps), max(gaps), (' (limit %+.2f .. %+.2f)' % (blo, bhi)) if up <= 0 else ' (upper row)', cd[0], cd[1], ld))
                if up <= 0 and not (blo <= min(gaps) and max(gaps) <= bhi): o.block('D5', '%s underside - ground %.2f .. %.2f outside %.2f .. %.2f' % (row['name'], min(gaps), max(gaps), blo, bhi))
                o.n('D5.%s.x' % row['name'], c[0]); o.n('D5.%s.y' % row['name'], c[1]); o.n('D5.%s.z' % row['name'], c[2])
                o.n('D5.%s.gap_lo' % row['name'], min(gaps)); o.n('D5.%s.gap_hi' % row['name'], max(gaps))
            if not bd['rows']: o.block('D5', 'firewood.bundle.rows is empty')
    # ------------------------------------------------------------ D6
    rb = D['ribbons']; o.say(''); o.say('D6 old road ribbons - renderer off (the vertex numbers are in post_1a_verify.md, not measured again)')
    on_now = 0
    for r in rb['renderers']:
        mr = S.comp(r['key'], 23); cols = sum(len(S.comp(r['key'], c)) for c in (64, 65, 135, 136))
        state = 'MISSING' if S.get(r['key']) is None else ('on' if mr and mr[0].get('on', True) else 'off')
        if r['enabled'] and state == 'on': on_now += 1
        o.say('  %-58s renderer %s, colliders %d -> %s' % (r['key'], state, cols, 'off' if r['enabled'] else 'listed only (enabled false in data)'))
    o.n('D6.renderers_on_before', on_now); o.n('D6.renderers_on_after', 0)
    if 'hinges' in D: dry_hinges(o, D, S, content)
    if 'reseat' in D: dry_reseat(o, D, S, H, lines)
    # ------------------------------------------------------------ D4
    gr = D['grass']; o.say(''); o.say('D4 grass seeds at the rest places (shared field asset, once)%s' % ('' if gr.get('apply_enabled') else ' - HELD: grass.apply_enabled is false (PROPOSED), counts are for the decision only'))
    regions = []
    closed = [c['id'] for c in gr['clear'] if c.get('closed')]
    if closed: o.say('  closed rows (not read): ' + ', '.join(closed))
    for c in gr['clear']:
        if c.get('closed'): continue
        if c['kind'] == 'rest':
            w = S.world(c['altar_key']); ft = content['feet'].get(c['id'])
            if w is None or ft is None: o.say('  %s: altar or checkpoint MISSING' % c['id']); continue
            fp = c['footprint']; dx, dz = fwd(w[1], (fp[0] + fp[1]) / 2, (fp[2] + fp[3]) / 2)
            a = (w[0][0] + dx, w[0][2] + dz); b = (ft[0], ft[2])
            regions.append((c, 'capsule', a, b, w[0][1]))
        elif c['kind'] == 'disc':
            regions.append((c, 'disc', tuple(c['centre_xz']), None, H(*c['centre_xz'])))
        elif c['kind'] == 'rect':
            if c['key'] == inn['inn_key']: regions.append((c, 'rect', (sel['pivot'][0], sel['pivot'][2]), sel['yaw'], H(sel['pivot'][0], sel['pivot'][2])))
            else:
                w = S.world(c['key'])
                if w is None: o.say('  %s: object MISSING' % c['id']); continue
                regions.append((c, 'rect', (w[0][0], w[0][2]), w[1], w[0][1]))
    counts = grass_scan(PROJECT / gr['asset'], regions, gr)
    for (c, kind, a, b, y), cnt in zip(regions, counts):
        if cnt is None: o.say('  %-26s %s: not computable offline (field asset unreadable)' % (c['id'], kind)); continue
        full, thin = cnt
        o.say('  %-26s %s at (%.1f, %.1f): seeds in the full zone %d, in the thin band %d (about %d of them removed)%s%s' % (c['id'], kind, a[0], a[1], full, thin, round(thin * (1 - gr['thin_keep'])), '' if c['enabled'] else ' - measured only', '  <- BLOCKED: no seed here, the band is not this field' if c.get('required') and full == 0 else ''))
        o.n('D4.%s.full' % c['id'], full); o.n('D4.%s.thin' % c['id'], thin)
    o.say('  [heuristic] counted by scanning the binary asset for float triples inside each zone (20-byte seed records); the editor grass-plan is the measurement.')
    return o


def dry_hinges(o, D, S, content):
    """D7: WorldMacroInnRestPresentation.HingeWorld -> the door leaf's present world position (relayout D03 / T8)."""
    hg = D['hinges']; o.say(''); o.say('D7 door hinges - HingeWorld to the door leaf (relayout D03)')
    if not hg.get('enabled'): o.say('  off in data'); return
    tol = hg['tol_m']; mx = hg['max_from_door_m']; doors = {}
    if not (mx > tol): o.block('D7', 'hinges.max_from_door_m %g must be larger than hinges.tol_m %g (verify could never pass)' % (mx, tol))
    for row in hg['rows']:
        field = row.get('session_field')
        if field not in SESSION_HINGE_FIELDS: o.block('D7', "%s: unknown session_field '%s' (%s)" % (row['id'], field, ' | '.join(SESSION_HINGE_FIELDS))); continue
        anchor = row.get('anchor', 'leaf')
        if anchor not in ('leaf', 'edge'): o.block('D7', "%s: unknown anchor '%s' (leaf | edge)" % (row['id'], anchor)); continue
        # the editor reaches the component through the session's own reference (HingeOf), then compares its PointId with the row
        refs = [r for r in set(S.session_refs.get(field, [])) if r != '0']
        if not refs: o.say('  %s: the session has no %s in this scene (skipped, as the editor does)' % (row['id'], field)); continue
        if len(refs) > 1: o.block('D7', '%s: %d different components are referenced as %s in this scene file' % (row['id'], len(refs), field)); continue
        found = [h for h in S.hinges if h.get('id') == refs[0]]
        if len(found) != 1: o.block('D7', '%s: the %s reference (fileID %s) is not a component with HingeWorld in this scene file' % (row['id'], field, refs[0])); continue
        h = found[0]
        if h.get('point') != row['point_id']:
            o.block('D7', "%s: the component behind %s has PointId '%s', the data says '%s' (the editor plan blocks this row)" % (row['id'], field, h.get('point'), row['point_id'])); continue
        same_point = [x for x in S.hinges if x.get('point') == row['point_id']]
        if len(same_point) > 1: o.block('D7', '%s: %d components carry PointId %s' % (row['id'], len(same_point), row['point_id'])); continue
        if h.get('door') not in S.sc.nodes: o.block('D7', '%s: the Door reference does not resolve in the scene file' % row['id']); continue
        if h['door'] in doors: o.block('D7', '%s: its door is the door of row %s (two rows on one door leaf)' % (row['id'], doors[h['door']])); continue
        doors[h['door']] = row['id']
        p, q, s = S.sc.world(h['door']); old = h['hinge']
        if anchor != 'leaf':
            o.say('  %s: anchor %s is measured in the editor only (renderer bounds); offline shows the leaf position and does NOT judge max_from_door_m for this row' % (row['id'], anchor))
            o.notes.append('D7 %s: anchor %s not judged offline' % (row['id'], anchor))
        else:
            # after the write the hinge IS the leaf position: its distance to the leaf is 0, which must be within max_from_door_m
            o.n('D7.%s.after_from_door_m' % row['id'], 0.0)
        stale = math.sqrt(sum((a - b) ** 2 for a, b in zip(old, p)))
        o.say('  %s: door %s at (%.3f, %.3f, %.3f) | HingeWorld now (%.3f, %.3f, %.3f): %.2f m from the leaf (flat %.2f, dy %+.2f) -> (%.3f, %.3f, %.3f)' % (
            row['id'], S.key_of(h['door']), *p, *old, stale, math.hypot(old[0] - p[0], old[2] - p[2]), old[1] - p[1], *p))
        o.n('D7.%s.stale_m' % row['id'], stale); o.n('D7.%s.x' % row['id'], p[0]); o.n('D7.%s.y' % row['id'], p[1]); o.n('D7.%s.z' % row['id'], p[2])


def dry_reseat(o, D, S, H, lines):
    """D8: rigid move of a prop set's root, then every piece back onto the ground with its own 'bottom - ground' (relayout M10 / T6)."""
    rs = D['reseat']; o.say(''); o.say('D8 per-piece re-seat move (relayout M10) [bounds = box colliders offline, renderer bounds in the editor]')
    if not rs.get('enabled'): o.say('  off in data'); return
    for st in rs['sets']:
        sid = st['id']
        if not st.get('enabled'): o.say('  %s: off in data' % sid); continue
        w = S.world(st['root_key'])
        if w is None: o.say('  %s: %s is not in this scene (skipped, as the editor does)' % (sid, st['root_key'])); continue
        (rx, ry, rz), ryaw, _, _ = w; tx, tz = st['target_xz']; yaw_t = st['yaw']; eb = st['expect_before']
        gy = H(tx, tz); slope = math.degrees(math.acos(H.normal(tx, tz, 2.0)[1])); new_y = gy + st.get('root_y_offset_m', 0.0)
        at_before = math.hypot(rx - eb[0], rz - eb[1]) <= D['tolerances']['before_xz_m'] and abs((ryaw - eb[2] + 180) % 360 - 180) <= D['tolerances']['before_yaw_deg']
        at_target = math.hypot(rx - tx, rz - tz) <= 0.02
        o.say('  %s root now (%.2f, %.2f, %.2f) yaw %.1f [%s] -> (%.2f, %.2f, %.2f) yaw %.1f | ground there %.2f (expected %.2f, tol %.2f) slope %.1f deg (limit %.0f) | old place: ground %.2f, slope %.1f deg' % (
            sid, rx, ry, rz, ryaw, 'the expected before place' if at_before else 'the target' if at_target else 'NEITHER the before place nor the target', tx, new_y, tz, yaw_t, gy, st['expect_ground_y'], rs['ground_y_tol_m'], slope, rs['max_slope_deg'],
            H(rx, rz), math.degrees(math.acos(H.normal(rx, rz, 2.0)[1]))))
        o.n('D8.%s.root_x' % sid, tx); o.n('D8.%s.root_y' % sid, new_y); o.n('D8.%s.root_z' % sid, tz); o.n('D8.%s.slope' % sid, slope, 'deg')
        if not at_before and not at_target: o.block('D8', '%s: the root is neither at expect_before nor at the target' % sid)
        if abs(gy - st['expect_ground_y']) > rs['ground_y_tol_m']: o.block('D8', '%s: ground %.2f at the target is not within %.2f of expect_ground_y %.2f' % (sid, gy, rs['ground_y_tol_m'], st['expect_ground_y']))
        if slope > rs['max_slope_deg']: o.block('D8', '%s: slope %.1f deg at the target (> %.0f)' % (sid, slope, rs['max_slope_deg']))
        cd = corridor_dist((tx, tz), lines)
        o.say('      root to the nearest route centre line %.2f m (%s, need >= %.1f)' % (cd[0], cd[1], st['road_clear_m'])); o.n('D8.%s.road' % sid, cd[0])
        if cd[0] < st['road_clear_m']: o.block('D8', '%s: root %.2f m from the centre line of %s (< %.1f)' % (sid, cd[0], cd[1], st['road_clear_m']))
        trunks = [t for t in read_trunks((tx - 30, tx + 30, tz - 30, tz + 30), D['dry']['trunk_sheets']) if t[6] in (0, 1)]
        if at_target: o.say('      the root already stands on the target: the per-piece numbers are the editor verify\'s (ledger offsets)'); continue
        dyaw = yaw_t - ryaw; root_t = S.get(st['root_key']); worst_rigid = 0.0
        for child in S.sc.children_of(root_t):
            key = S.key_of(child); name = key.split('/')[-1]; bb = S.box_bounds(key)
            if bb is None: o.say('      %-22s no box collider: bounds not computable offline (the editor measures the renderers)' % name); continue
            lo, hi = bb; c = ((lo[0] + hi[0]) / 2, (lo[2] + hi[2]) / 2)
            before = lo[1] - H(c[0], c[1]); off = before; src = 'kept'
            rng = rs.get('offset_range_m')
            if rng and not (rng[0] <= before <= rng[1]):
                auth = st.get('authored_offsets', {})
                if name in auth: off = auth[name]; src = 'authored %+.2f (before %+.2f is outside %+.2f .. %+.2f)' % (off, before, rng[0], rng[1])
                else: o.block('D8', '%s/%s: bottom - ground before %+.2f is outside %+.2f .. %+.2f and the set has no authored_offsets row' % (sid, name, before, rng[0], rng[1])); src = 'OUT OF RANGE, no authored row'
            dx, dz = fwd(dyaw, c[0] - rx, c[1] - rz); nc = (tx + dx, tz + dz)
            rigid_bottom = lo[1] + (new_y - ry); gn = H(nc[0], nc[1]); rigid_gap = rigid_bottom - gn - off
            shift = -rigid_gap; cap = rs['max_piece_shift_m']; applied = max(-cap, min(cap, shift)); gap = rigid_gap + applied
            nt = min([(math.hypot(t[0] - nc[0], t[2] - nc[1]), t[4]) for t in trunks] or [(float('inf'), '-')])
            corners = [fwd(dyaw, x - rx, z - rz) for x in (lo[0], hi[0]) for z in (lo[2], hi[2])]
            relief_new = [H(tx + a, tz + b) for a, b in corners]; relief_old = [H(x, z) for x in (lo[0], hi[0]) for z in (lo[2], hi[2])]
            worst_rigid = max(worst_rigid, abs(rigid_gap))
            o.say('      %-22s box %.2f x %.2f x %.2f m | bottom - ground before %+.2f, offset used %+.2f [%s] | rigid move would leave %+.2f off it | height correction %+.2f -> gap %+.2f (limit %.2f) | ground relief under the box %.2f -> %.2f m | nearest trunk %.1f m (%s)' % (
                name, hi[0] - lo[0], hi[1] - lo[1], hi[2] - lo[2], before, off, src, rigid_gap, applied, gap, rs['gap_max_m'], max(relief_old) - min(relief_old), max(relief_new) - min(relief_new), nt[0], nt[1]))
            o.n('D8.%s.%s.before' % (sid, name), before); o.n('D8.%s.%s.offset' % (sid, name), off); o.n('D8.%s.%s.rigid_gap' % (sid, name), rigid_gap); o.n('D8.%s.%s.shift' % (sid, name), applied); o.n('D8.%s.%s.gap' % (sid, name), gap)
            if abs(gap) > rs['gap_max_m']: o.block('D8', '%s/%s: gap %.2f m after the height correction (> %.2f; correction cut at max_piece_shift_m %.2f)' % (sid, name, gap, rs['gap_max_m'], cap))
            if nt[0] < rs['trunk_clear_m']: o.block('D8', '%s/%s: trunk %s %.2f m from the piece centre (< %.1f)' % (sid, name, nt[1], nt[0], rs['trunk_clear_m']))
        o.say('      without the per-piece correction the worst piece would float / sink %.2f m' % worst_rigid)


def grass_scan(path, regions, gr):
    """seeds = (Vector3 position, Vector2 normalXZ) records; scan 4-byte aligned float triples whose (x, y, z) fall in a zone."""
    try:
        import numpy as np
    except ImportError:
        return [None] * len(regions)
    if not Path(path).exists(): return [None] * len(regions)
    raw = Path(path).read_bytes(); out = []
    for off in (0,):
        arr = np.frombuffer(raw[off:off + (len(raw) - off) // 4 * 4], '<f4')
    x = arr[:-4]; y = arr[1:-3]; z = arr[2:-2]; nx = arr[3:-1]; nz = arr[4:]
    for (c, kind, a, b, gy) in regions:
        if kind == 'capsule': reach = c['thin_m']; x0, x1 = min(a[0], b[0]) - reach, max(a[0], b[0]) + reach; z0, z1 = min(a[1], b[1]) - reach, max(a[1], b[1]) + reach
        elif kind == 'disc': reach = c['radius_m']; x0, x1, z0, z1 = a[0] - reach, a[0] + reach, a[1] - reach, a[1] + reach
        else: reach = max(abs(v) for v in c['rect']) * 1.5 + c['margin_m']; x0, x1, z0, z1 = a[0] - reach, a[0] + reach, a[1] - reach, a[1] + reach
        m = (x >= x0) & (x <= x1) & (z >= z0) & (z <= z1) & (np.abs(y - gy) <= gr['y_window_m'] + 3.0) & (np.abs(nx) <= 1.0) & (np.abs(nz) <= 1.0)
        idx = np.nonzero(m)[0]; full = thin = 0
        for i in idx:
            px, pz = float(x[i]), float(z[i])
            if kind == 'capsule':
                d = seg_dist((px, pz), a, b)
                if d <= c['full_m']: full += 1
                elif d <= c['thin_m']: thin += 1
            elif kind == 'disc':
                if math.hypot(px - a[0], pz - a[1]) <= c['radius_m']: full += 1
            else:
                lx, lz = inv(b, px - a[0], pz - a[1])
                if rect_out(c['rect'], lx, lz) <= c['margin_m']: full += 1
        out.append((full, thin))
    return out


# ---------------------------------------------------------------------------------------------- content308_scene.json rows

def patch_text(text, D, cand, undo):
    inn = D['inn']; changes = []
    # the inn part row: the first "offset": [...], "yaw": n after the hunter_inn308 rest id
    at = text.find('"id": "%s"' % inn['rest_id'])
    if at < 0: raise SystemExit('rest %s not found in the scene data' % inn['rest_id'])
    m = re.compile(r'"offset": \[([^\]]*)\], "yaw": (-?[\d.]+)').search(text, at)
    if not m: raise SystemExit('the inn part row has no "offset": [...], "yaw": n')
    eb = inn['expect_before']
    want = '"offset": [%s, %s, %s], "yaw": %s' % ((fmt(eb['offset'][0]), fmt(eb['offset_y']), fmt(eb['offset'][1]), fmt(eb['yaw'])) if undo else (fmt(cand['offset'][0]), fmt(-cand.get('embed_m', inn['embed_m'])), fmt(cand['offset'][1]), fmt(cand['yaw'])))
    if m.group(0) != want: changes.append((m.group(0), want)); text = text[:m.start()] + want + text[m.end():]
    for pc in D['firewood']['pieces']:
        rx = re.compile(r'(\{"id": "%s", "enabled": )(true|false)' % re.escape(pc['id']))
        m = rx.search(text)
        if not m: raise SystemExit('prop row %s not found' % pc['id'])
        want = 'true' if undo else 'false'
        if m.group(2) != want: changes.append((m.group(0), m.group(1) + want)); text = text[:m.start(2)] + want + text[m.end(2):]
    return text, changes


def fmt(v):
    s = ('%.4f' % v).rstrip('0').rstrip('.')
    return s if s not in ('-0', '') else '0'


def cmd_patch(D, a, undo):
    f = ROOT / D['scene_data']; text = f.read_bytes().decode('utf-8')   # bytes: line endings stay as they are
    cand = D['inn']['candidates'][a.candidate] if D['inn']['mode'] == 'move' else D['inn']['in_place']
    new, changes = patch_text(text, D, cand, undo)
    json.loads(new)   # must still parse
    print('%s %s (sha %s)' % ('unpatch-data' if undo else 'patch-data', f, sha(f)[:12]))
    if not changes: print('  already in that state: nothing to change'); return
    for old, want in changes: print('  %s\n    -> %s' % (old, want))
    if not a.write: print('  (dry: add --write to back the file up and write it)'); return
    stamp = datetime.datetime.now(datetime.timezone.utc).strftime('%Y%m%dT%H%M%SZ')
    bdir = OUT / 'Backups' / ('seat308-data-' + stamp); bdir.mkdir(parents=True, exist_ok=True)
    shutil.copy2(f, bdir / f.name)
    f.write_bytes(new.encode('utf-8'))
    print('  written; sha %s; backup %s' % (sha(f)[:12], bdir / f.name))


def main():
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('sub', choices=['dry', 'patch-data', 'unpatch-data'])
    ap.add_argument('--scene', default='main'); ap.add_argument('--data'); ap.add_argument('--height'); ap.add_argument('--candidate', type=int, default=0)
    ap.add_argument('--write', action='store_true'); ap.add_argument('--assume-patched', action='store_true', help='dry: read content308_scene.json as patch-data would leave it')
    ap.add_argument('--out', help='dry: write seat308_dry_<alias>.json / .txt here instead of Art/Playtest308/Pacing (trial data)')
    ap.add_argument('--scene-data', help='dry: read this content308_scene.json instead of the live one (the stage copy before the deploy)')
    a = ap.parse_args()
    data_file = Path(a.data) if a.data else (LIVE_DATA if LIVE_DATA.exists() else STAGE_DATA)
    D = json.loads(data_file.read_text(encoding='utf-8-sig'))
    if a.sub == 'patch-data': return cmd_patch(D, a, False)
    if a.sub == 'unpatch-data': return cmd_patch(D, a, True)
    # default = the stage-1b field the three scenes carry now; a name (height_1a | height_1b | height_p1b) or a path
    H = Height(HEIGHTS.get(a.height, a.height) if a.height else HEIGHT_1B)
    text = (Path(a.scene_data) if a.scene_data else ROOT / D['scene_data']).read_text(encoding='utf-8')
    if a.assume_patched: text, _ = patch_text(text, D, D['inn']['candidates'][a.candidate] if D['inn']['mode'] == 'move' else D['inn']['in_place'], False)
    scene_data = json.loads(text)
    out = Path(a.out) if a.out else OUT
    out.mkdir(parents=True, exist_ok=True); bad = 0
    for alias in (list(SCENES) if a.scene == 'all' else [a.scene]):
        o = dry_scene(alias, D, H, scene_data)
        o.say(''); o.say('verdict %s: %s' % (alias, 'every offline limit holds (the collider overlap, the plinth top and the skirt are editor only)' if not o.blocked else '%d BLOCKED - the editor plan would refuse: %s' % (len(o.blocked), ' | '.join(o.blocked))))
        print('\n'.join(o.lines))
        doc = {'alias': alias, 'scene': SCENES[alias], 'utc': datetime.datetime.now(datetime.timezone.utc).strftime('%Y%m%dT%H%M%SZ'), 'data': str(data_file), 'data_version': D['version'],
               'height': H.path, 'height_sha': H.sha, 'assume_patched': a.assume_patched, 'ok': not o.blocked, 'blocked': o.blocked, 'numbers': o.num}
        (out / ('seat308_dry_%s.json' % alias)).write_text(json.dumps(doc, ensure_ascii=False, indent=1), encoding='utf-8')
        (out / ('seat308_dry_%s.txt' % alias)).write_text('\n'.join(o.lines) + '\n', encoding='utf-8')
        print('-> %s' % (out / ('seat308_dry_%s.json' % alias)))
        bad += len(o.blocked)
    if bad: sys.exit(2)


if __name__ == '__main__':
    main()
