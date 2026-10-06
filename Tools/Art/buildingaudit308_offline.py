"""#308 building audit — offline cross-checks (SPEC-WORLD-BUILDING-AUDIT-308 §B). Read only.

Scene YAML and zip entries are read line by line (never loaded whole); nothing under Oheangbu/Assets is written.

    python Tools/Art/buildingaudit308_offline.py refs    [--scene main]   C1 static: null-mesh MeshFilters, missing GUIDs
    python Tools/Art/buildingaudit308_offline.py dups    [--scene main]   C4 static: same parent + name + local TRS
    python Tools/Art/buildingaudit308_offline.py heights [--scene main]   C2 cross-check: 295 height field x unit pivots
    python Tools/Art/buildingaudit308_offline.py sheets                   C3 cross-check: sheet trees/rocks x Finish297 mesh JSON
    python Tools/Art/buildingaudit308_offline.py offsets [--scene main]   F3 input: baseline (Candidate296.zip) sibling offsets
    python Tools/Art/buildingaudit308_offline.py find --name <substr> [--scene main]   world positions of matching objects
    python Tools/Art/buildingaudit308_offline.py all     [--scene main]

--scene takes an alias from BuildingAudit308/config.json (architecture296 | folklore298 | main) or a scene path.
Output: Art/World/Compact/Rebuild/BuildingAudit308/offline/<utc>/<sub>-<alias>.{json,txt}; `offsets` also writes
BuildingAudit308/offline/dup-offsets.json (the file BuildingFix308 F3 reads). Offline numbers are estimates: when the
editor scan disagrees, the editor value wins and the difference is recorded (Spec §B).
"""
import argparse, datetime, hashlib, io, json, math, re, struct, sys, zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
PROJECT = ROOT / 'Oheangbu'
AUDIT = ROOT / 'Art/World/Compact/Rebuild/BuildingAudit308'
CONFIG = AUDIT / 'config.json'
HEIGHT = ROOT / 'Art/World/Compact/Rebuild/Watershed295/Generated/height.bytes'
HEIGHT_LIVE = PROJECT / 'Assets/_Project/Art/World/Watershed295/Surface/height.bytes'
FINISH = ROOT / 'Art/World/Compact/Rebuild/Finish297'
BASELINE_ZIP = FINISH / 'Baseline/Candidate296.zip'
BASELINE_ENTRY = 'Oheangbu/Assets/_Project/Art/World/Architecture296/W_Demo_Compact_Architecture296.unity'
H_ROWS, H_COLS, H_CELL = 1501, 1001, 4.0
BUILTIN = re.compile(r'^0000000000000000[0-9a-f]000000000000000$')
HDR = re.compile(r'^--- !u!(\d+) &(-?\d+)( stripped)?')
NUM = r'(-?[0-9.eE+\-]+)'
VEC3 = re.compile(r'\{x: ' + NUM + r', y: ' + NUM + r', z: ' + NUM + r'\}')
VEC4 = re.compile(r'\{x: ' + NUM + r', y: ' + NUM + r', z: ' + NUM + r', w: ' + NUM + r'\}')
FILEID = re.compile(r'fileID: (-?\d+)')
GUID = re.compile(r'guid: ([0-9a-f]{32})')


def utc():
    return datetime.datetime.now(datetime.timezone.utc).strftime('%Y%m%dT%H%M%SZ')


def sha256(path):
    h = hashlib.sha256()
    with open(path, 'rb') as f:
        for chunk in iter(lambda: f.read(1 << 20), b''): h.update(chunk)
    return h.hexdigest()


def load_config():
    if CONFIG.exists():
        return json.loads(CONFIG.read_text(encoding='utf-8-sig'))
    return {}


def scene_path(arg, cfg):
    for s in cfg.get('scenes', []):
        if s['alias'] == arg: return s['alias'], PROJECT / s['path']
    p = Path(arg)
    if not p.is_absolute(): p = PROJECT / arg
    return p.stem, p


# ---------------------------------------------------------------------------------------------- quaternion math (Unity)

def qmul(a, b):
    ax, ay, az, aw = a; bx, by, bz, bw = b
    return (aw * bx + ax * bw + ay * bz - az * by, aw * by - ax * bz + ay * bw + az * bx, aw * bz + ax * by - ay * bx + az * bw, aw * bw - ax * bx - ay * by - az * bz)


def qrot(q, v):
    x, y, z, w = q
    vx, vy, vz = v
    tx, ty, tz = 2 * (y * vz - z * vy), 2 * (z * vx - x * vz), 2 * (x * vy - y * vx)
    return (vx + w * tx + (y * tz - z * ty), vy + w * ty + (z * tx - x * tz), vz + w * tz + (x * ty - y * tx))


def yaw_of(q):
    f = qrot(q, (0.0, 0.0, 1.0))
    return math.degrees(math.atan2(f[0], f[2])) % 360.0


def qangle(a, b):
    d = abs(sum(x * y for x, y in zip(a, b)))
    return math.degrees(2 * math.acos(min(1.0, d)))


# ---------------------------------------------------------------------------------------------- scene model (streaming)

class Node:
    __slots__ = ('id', 'go', 'name', 'father', 'children', 'pos', 'rot', 'scale', 'active', 'stripped', 'pi', 'src', 'world')

    def __init__(self, i):
        self.id = i; self.go = None; self.name = None; self.father = '0'; self.children = []
        self.pos = (0.0, 0.0, 0.0); self.rot = (0.0, 0.0, 0.0, 1.0); self.scale = (1.0, 1.0, 1.0)
        self.active = True; self.stripped = False; self.pi = None; self.src = None; self.world = None


class Scene:
    """Transforms, GameObjects, MeshFilters/Renderers and PrefabInstances of one scene file, plus every GUID reference."""

    def __init__(self, lines, want_guids=False, guid_meta=None):
        self.nodes = {}; self.go_name = {}; self.go_active = {}; self.go_tr = {}
        self.mf = {}; self.mr_enabled = {}; self.pis = {}; self.lods = set(); self.guid_refs = [] if want_guids else None
        self.guid_meta = guid_meta or {}; self.extra = None
        cur = None; cls = None; stripped = False; section = None; target = None; prop = None; pi = None
        for line in lines:
            m = HDR.match(line)
            if m:
                cls = int(m.group(1)); cur = m.group(2); stripped = bool(m.group(3)); section = None; target = None; prop = None
                if cls in (4, 224):
                    n = self.nodes.setdefault(cur, Node(cur)); n.stripped = stripped
                elif cls == 1001:
                    pi = self.pis.setdefault(cur, {'parent': '0', 'mods': [], 'src': None})
                continue
            if cur is None: continue
            if want_guids and 'guid:' in line:
                for g in GUID.findall(line):
                    key = line.strip().split(':')[0].lstrip('- ')
                    self.guid_refs.append((g, cls, cur, key))
            s = line.rstrip('\n')
            if cls == 1:
                if s.startswith('  m_Name: '): self.go_name[cur] = s[10:]
                elif s.startswith('  m_IsActive: '): self.go_active[cur] = s.endswith('1')
            elif cls in (4, 224):
                n = self.nodes[cur]
                if s.startswith('  m_GameObject: '): n.go = FILEID.search(s).group(1)
                elif s.startswith('  m_LocalRotation: '):
                    v = VEC4.search(s); n.rot = tuple(float(x) for x in v.groups()) if v else n.rot
                elif s.startswith('  m_LocalPosition: '):
                    v = VEC3.search(s); n.pos = tuple(float(x) for x in v.groups()) if v else n.pos
                elif s.startswith('  m_LocalScale: '):
                    v = VEC3.search(s); n.scale = tuple(float(x) for x in v.groups()) if v else n.scale
                elif s.startswith('  m_Father: '): n.father = FILEID.search(s).group(1)
                elif s.startswith('  m_Children:'): section = 'children'
                elif section == 'children' and s.startswith('  - {fileID: '): n.children.append(FILEID.search(s).group(1))
                elif s.startswith('  m_PrefabInstance: '): n.pi = FILEID.search(s).group(1)
                elif s.startswith('  m_CorrespondingSourceObject: '):
                    f = FILEID.search(s); n.src = f.group(1) if f else None
                elif s.startswith('  m_') and not s.startswith('  m_Children'): section = None
            elif cls == 33:
                if s.startswith('  m_GameObject: '): self.mf.setdefault(cur, {})['go'] = FILEID.search(s).group(1)
                elif s.startswith('  m_Mesh: '):
                    g = GUID.search(s); self.mf.setdefault(cur, {})['mesh'] = (FILEID.search(s).group(1), g.group(1) if g else None)
            elif cls == 23:
                if s.startswith('  m_GameObject: '): self.mr_enabled.setdefault(cur, {})['go'] = FILEID.search(s).group(1)
                elif s.startswith('  m_Enabled: '): self.mr_enabled.setdefault(cur, {})['on'] = s.endswith('1')
            elif cls == 205:
                if s.startswith('  m_GameObject: '): self.lods.add(FILEID.search(s).group(1))
            elif cls == 1001:
                if s.startswith('    m_TransformParent: '): pi['parent'] = FILEID.search(s).group(1)
                elif s.startswith('    - target: '): target = FILEID.search(s).group(1); prop = None
                elif s.startswith('      propertyPath: '): prop = s[len('      propertyPath: '):].strip("'")
                elif s.startswith('      value: ') and prop is not None: pi['mods'].append((target, prop, s[len('      value: '):])); prop = None
                elif s.startswith('  m_SourcePrefab: '):
                    g = GUID.search(s); pi['src'] = g.group(1) if g else None
        listed = set()
        for n in self.nodes.values(): listed.update(n.children)
        pi_root = {}
        for tid, n in self.nodes.items():
            if n.stripped and n.pi in self.pis and tid in listed: pi_root.setdefault(n.pi, tid)
        for tid, n in self.nodes.items():
            if n.go: self.go_tr[n.go] = tid
            if n.stripped and n.pi in self.pis:
                p = self.pis[n.pi]; mods = p['mods']
                if pi_root.get(n.pi) != tid:
                    # a transform inside the prefab (father of objects added in the scene): its real parent chain lives in the
                    # prefab file; hang it under the instance root so paths stay readable (positions approximate)
                    n.father = pi_root.get(n.pi, p['parent']); n.name = '(prefab inner %s)' % n.src
                    n.pos = (0.0, 0.0, 0.0); n.rot = (0.0, 0.0, 0.0, 1.0); n.scale = (1.0, 1.0, 1.0)
                    continue
                n.father = p['parent']
                names = [v for (t, k, v) in mods if k == 'm_Name']
                prefab = self.guid_meta.get(p['src'], '') if p['src'] else ''
                n.name = names[0] if names else (Path(prefab).stem if prefab else 'prefab:' + str(p['src']))
                def mod(key, default):
                    for (t, k, v) in mods:
                        if t == n.src and k == key:
                            try: return float(v)
                            except ValueError: return default
                    return default
                n.pos = (mod('m_LocalPosition.x', 0.0), mod('m_LocalPosition.y', 0.0), mod('m_LocalPosition.z', 0.0))
                n.rot = (mod('m_LocalRotation.x', 0.0), mod('m_LocalRotation.y', 0.0), mod('m_LocalRotation.z', 0.0), mod('m_LocalRotation.w', 1.0))
                n.scale = (mod('m_LocalScale.x', 1.0), mod('m_LocalScale.y', 1.0), mod('m_LocalScale.z', 1.0))
            elif n.go:
                n.name = self.go_name.get(n.go, '?'); n.active = self.go_active.get(n.go, True)
            else:
                n.name = n.name or '?'

    def path(self, tid):
        names = []; t = tid; k = 0
        while t and t != '0' and t in self.nodes and k < 200:
            names.append(self.nodes[t].name or '?'); t = self.nodes[t].father; k += 1
        return '/'.join(reversed(names))

    def world(self, tid):
        n = self.nodes.get(tid)
        if n is None: return ((0.0, 0.0, 0.0), (0.0, 0.0, 0.0, 1.0), (1.0, 1.0, 1.0))
        if n.world is not None: return n.world
        if n.father in self.nodes:
            pp, pr, ps = self.world(n.father)
            sp = (ps[0] * n.pos[0], ps[1] * n.pos[1], ps[2] * n.pos[2])
            rp = qrot(pr, sp)
            n.world = ((pp[0] + rp[0], pp[1] + rp[1], pp[2] + rp[2]), qmul(pr, n.rot), (ps[0] * n.scale[0], ps[1] * n.scale[1], ps[2] * n.scale[2]))
        else:
            n.world = (n.pos, n.rot, n.scale)
        return n.world

    def roots(self):
        return [t for t, n in self.nodes.items() if n.father == '0' or n.father not in self.nodes]

    def children_of(self, tid):
        n = self.nodes.get(tid)
        if n is None: return []
        if self.extra is None:
            # children only reachable through m_Father (prefab instance roots of older serialisations), computed once
            self.extra = {}
            for t, m in self.nodes.items():
                p = self.nodes.get(m.father)
                if p is not None and t not in p.children: self.extra.setdefault(m.father, []).append(t)
        return [c for c in n.children if c in self.nodes] + self.extra.get(tid, [])

    def subtree(self, tid):
        out = []; stack = [tid]
        while stack:
            t = stack.pop(); out.append(t); stack.extend(self.children_of(t))
        return out


def read_scene_lines(path):
    with open(path, encoding='utf-8', errors='replace') as f:
        for line in f: yield line


def read_zip_lines(zpath, entry):
    with zipfile.ZipFile(zpath) as z:
        with z.open(entry) as raw:
            for line in io.TextIOWrapper(raw, encoding='utf-8', errors='replace'): yield line


def guid_map():
    """guid -> asset path for Assets/ and Packages/ (+ Library/PackageCache) .meta files."""
    m = {}
    for base in (PROJECT / 'Assets', PROJECT / 'Packages', PROJECT / 'Library/PackageCache'):
        if not base.exists(): continue
        for meta in base.rglob('*.meta'):
            try:
                with open(meta, encoding='utf-8', errors='replace') as f:
                    for _ in range(4):
                        line = f.readline()
                        if line.startswith('guid: '): m[line[6:].strip()] = str(meta.relative_to(PROJECT))[:-5].replace('\\', '/'); break
            except OSError:
                pass
    return m


def roots_of(scene, names):
    by = {}
    for t in scene.roots():
        by.setdefault(scene.nodes[t].name, []).append(t)
    return {n: by.get(n, []) for n in names}


def units(scene, cfg, names=None):
    """(root, tid) at each configured root's depth (a shallower leaf is its own unit)."""
    roots = {r['name']: r for r in cfg.get('roots', [])}
    names = names or list(roots.keys())
    out = []
    for name, tids in roots_of(scene, names).items():
        depth = max(1, roots.get(name, {}).get('depth', 1))
        for root in tids:
            stack = [(root, 0)]
            while stack:
                t, d = stack.pop()
                kids = scene.children_of(t)
                if d >= depth or (d > 0 and not kids): out.append((name, t)); continue
                stack.extend((c, d + 1) for c in kids)
    return out


# ---------------------------------------------------------------------------------------------- height field

class Height:
    def __init__(self, path):
        data = Path(path).read_bytes()
        if len(data) != H_ROWS * H_COLS * 4: raise ValueError('unexpected height field size %d' % len(data))
        self.h = struct.unpack('<%df' % (H_ROWS * H_COLS), data)

    def __call__(self, x, z):
        j = min(max(x / H_CELL, 0.0), H_COLS - 1.001); i = min(max(z / H_CELL, 0.0), H_ROWS - 1.001)
        j0, i0 = int(j), int(i); fj, fi = j - j0, i - i0; h = self.h; W = H_COLS
        return (h[i0 * W + j0] * (1 - fj) * (1 - fi) + h[i0 * W + j0 + 1] * fj * (1 - fi) + h[(i0 + 1) * W + j0] * (1 - fj) * fi + h[(i0 + 1) * W + j0 + 1] * fj * fi)


# ---------------------------------------------------------------------------------------------- sub-commands

def cmd_refs(alias, path, cfg, out):
    gm = guid_map()
    sc = Scene(read_scene_lines(path), want_guids=True, guid_meta=gm)
    null_mesh = []
    renderer_on = {}
    for rid, r in sc.mr_enabled.items():
        if 'go' in r: renderer_on[r['go']] = r.get('on', True)
    for fid, f in sc.mf.items():
        mesh = f.get('mesh')
        if mesh and mesh[0] == '0':
            go = f.get('go'); tid = sc.go_tr.get(go)
            null_mesh.append({'path': sc.path(tid) if tid else '?', 'rendererEnabled': renderer_on.get(go, None), 'world': list(sc.world(tid)[0]) if tid else None})
    missing = {}
    for g, cls, owner, key in sc.guid_refs:
        if BUILTIN.match(g) or g in gm: continue
        e = missing.setdefault(g, {'guid': g, 'count': 0, 'classes': set(), 'keys': set(), 'examples': []})
        e['count'] += 1; e['classes'].add(cls); e['keys'].add(key)
        if len(e['examples']) < 3:
            tid = sc.go_tr.get(owner) or (owner if owner in sc.nodes else None)
            e['examples'].append(sc.path(tid) if tid else 'block ' + owner)
    rows = sorted(({**e, 'classes': sorted(e['classes']), 'keys': sorted(e['keys'])} for e in missing.values()), key=lambda e: -e['count'])
    on = [n for n in null_mesh if n['rendererEnabled']]
    report = {'scene': str(path), 'sha256': sha256(path), 'nullMeshFilters': len(null_mesh), 'nullMeshEnabledRenderer': len(on), 'enabled': on, 'missingGuids': rows,
              'note': 'prefab-internal objects are not in the scene file; the editor C1 sees them'}
    write(out, 'refs-' + alias, report, ['refs %s: null-mesh MeshFilters %d (enabled renderer %d), missing GUIDs %d' % (alias, len(null_mesh), len(on), len(rows))]
          + ['  enabled+null ' + n['path'] for n in on] + ['  missing %s x%d %s %s' % (r['guid'], r['count'], r['keys'], r['examples']) for r in rows[:40]])


def dup_pairs(sc, tids_root, pos_tol=0.01, rot_tol=0.1):
    pairs = []
    for root in tids_root:
        for t in sc.subtree(root):
            kids = sc.children_of(t)
            if len(kids) < 2: continue
            groups = {}
            for c in kids:
                if sc.nodes[c].active: groups.setdefault(sc.nodes[c].name, []).append(c)
            for name, g in groups.items():
                for i in range(len(g)):
                    for j in range(i + 1, len(g)):
                        a, b = sc.nodes[g[i]], sc.nodes[g[j]]
                        if math.dist(a.pos, b.pos) <= pos_tol and qangle(a.rot, b.rot) <= rot_tol and math.dist(a.scale, b.scale) <= pos_tol:
                            pairs.append((t, g[i], g[j]))
    return pairs


def cmd_dups(alias, path, cfg, out):
    sc = Scene(read_scene_lines(path))
    names = [r['name'] for r in cfg.get('roots', [])] or [sc.nodes[t].name for t in sc.roots()]
    rows = []
    for name, tids in roots_of(sc, names).items():
        for parent, a, b in dup_pairs(sc, tids):
            rows.append({'root': name, 'parent': sc.path(parent), 'name': sc.nodes[a].name, 'local': list(sc.nodes[a].pos), 'yaw': yaw_of(sc.nodes[a].rot), 'world': list(sc.world(a)[0]), 'a': sc.path(a), 'b': sc.path(b)})
    write(out, 'dups-' + alias, {'scene': str(path), 'pairs': rows}, ['dups %s: %d same parent+name+local TRS pairs' % (alias, len(rows))]
          + ['  %s/%s @ (%.1f, %.1f, %.1f)' % (r['parent'], r['name'], *r['world']) for r in rows])


def cmd_heights(alias, path, cfg, out):
    src = HEIGHT if HEIGHT.exists() else HEIGHT_LIVE
    hf = Height(src)
    sc = Scene(read_scene_lines(path))
    rows = []
    for root, t in units(sc, cfg):
        p = sc.world(t)[0]
        if not (0 <= p[0] <= (H_COLS - 1) * H_CELL and 0 <= p[2] <= (H_ROWS - 1) * H_CELL): continue
        if abs(p[0]) + abs(p[2]) < 1e-3 or abs(sc.nodes[t].pos[0]) + abs(sc.nodes[t].pos[2]) < 1e-3 and abs(hf(p[0], p[2]) - p[1]) > 30: continue   # containers at the origin
        ring = [hf(p[0] + 5 * math.cos(k * math.pi / 8), p[2] + 5 * math.sin(k * math.pi / 8)) for k in range(16)]
        g = hf(p[0], p[2])
        rows.append({'root': root, 'unit': sc.path(t), 'pivot': list(p), 'terrain': g, 'terrainMinusPivot': g - p[1], 'ring5Min': min(ring) - p[1], 'ring5Max': max(ring) - p[1], 'south8': hf(p[0], p[2] - 8) - p[1]})
    rows.sort(key=lambda r: -r['ring5Max'])
    houses = [h['path'] for h in cfg.get('fix', {}).get('f1', {}).get('houses', [])]
    write(out, 'heights-' + alias, {'scene': str(path), 'heightField': str(src), 'heightSha256': sha256(src), 'rows': rows, 'note': 'pivot vs 295 height field (bilinear, 4 m); INFERRED — the editor C2 measures plates'},
          ['heights %s: %d unit pivots (height field %s)' % (alias, len(rows), src.name)]
          + ['  %-60s terrain-pivot %+6.2f ring5 %+6.2f..%+6.2f south8 %+6.2f' % (r['unit'][-60:], r['terrainMinusPivot'], r['ring5Min'], r['ring5Max'], r['south8']) for r in rows if r['unit'] in houses or r['ring5Max'] > 1.0][:80])


def parse_sheet(path):
    """(prototypes {id: (category, size)}, placements [(index, id, proto, pos, scale)]) from a sheet asset, line by line."""
    protos = {}; places = []; section = None; cur = None; proto_id = None
    with open(path, encoding='utf-8', errors='replace') as f:
        for line in f:
            s = line.rstrip('\n')
            if s.startswith('  ') and not s.startswith('   ') and not s.startswith('  - '):
                section = s.strip().split(':')[0]; cur = None; continue
            if section == 'Prototypes':
                if s.startswith('  - Id: '): proto_id = s[8:].strip(); protos[proto_id] = [None, None]
                elif proto_id and s.startswith('    Category: '): protos[proto_id][0] = int(s.split(':')[1])
                elif proto_id and s.startswith('    Size: '):
                    v = VEC3.search(s); protos[proto_id][1] = tuple(float(x) for x in v.groups())
            elif section == 'FixedPlacements':
                if s.startswith('  - Id: '):
                    cur = {'index': len(places), 'id': s[8:].strip(), 'proto': None, 'pos': None, 'scale': 1.0}; places.append(cur)
                elif cur is not None and s.startswith('    PrototypeId: '): cur['proto'] = s[17:].strip()
                elif cur is not None and s.startswith('    Position: '):
                    v = VEC3.search(s); cur['pos'] = tuple(float(x) for x in v.groups())
                elif cur is not None and s.startswith('    Scale: '): cur['scale'] = float(s[11:])
    return protos, places


def finish_meshes():
    """World triangles of every Finish297 compound mesh (LOD0 and Collision) from the offline mesh JSON."""
    out = []
    for unity in sorted(FINISH.glob('*/unity.json')):
        u = json.loads(unity.read_text(encoding='utf-8-sig'))
        for m in u.get('meshes', []):
            for part in ('LOD0', 'Collision'):
                f = unity.parent / 'Meshes' / ('%s_%s.json' % (m['name'], part))
                if not f.exists(): continue
                d = json.loads(f.read_text(encoding='utf-8'))
                v = d['v']; verts = [(v[i], v[i + 1], v[i + 2]) for i in range(0, len(v), 3)]
                if not m.get('world', False):
                    px, py, pz = m['position']; yw = math.radians(m.get('yaw', 0.0)); c, s = math.cos(yw), math.sin(yw)
                    verts = [(px + x * c + z * s, py + y, pz - x * s + z * c) for (x, y, z) in verts]
                tris = []
                for sub in d.get('sub', []):
                    t = sub['t']; tris.extend((verts[t[i]], verts[t[i + 1]], verts[t[i + 2]]) for i in range(0, len(t) - 2, 3))
                if not tris: continue
                xs = [p[0] for tr in tris for p in tr]; zs = [p[2] for tr in tris for p in tr]
                out.append({'root': u.get('root', unity.parent.name), 'name': m['name'], 'kind': m.get('kind', ''), 'part': part, 'tris': tris, 'box': (min(xs), min(zs), max(xs), max(zs))})
    return out


def vertical_hit(tri, x, z):
    (ax, ay, az), (bx, by, bz), (cx, cy, cz) = tri
    d = (bz - cz) * (ax - cx) + (cx - bx) * (az - cz)
    if abs(d) < 1e-9: return None
    l1 = ((bz - cz) * (x - cx) + (cx - bx) * (z - cz)) / d; l2 = ((cz - az) * (x - cx) + (ax - cx) * (z - cz)) / d; l3 = 1 - l1 - l2
    if l1 < -1e-5 or l2 < -1e-5 or l3 < -1e-5: return None
    return l1 * ay + l2 * by + l3 * cy


def sheet_files(cfg):
    base = PROJECT / 'Assets/_Project/Art/World'
    files = []
    for name in cfg.get('sheets', ['DryLandscape', 'Dressing', 'Banks', 'Forest305']):
        files += [p for p in list((base / 'Architecture296/Data').glob('*%s*.asset' % name)) + list((base / 'Enclosure305').glob('*%s*.asset' % name)) if p.suffix == '.asset']
    return sorted(set(files))


def cmd_sheets(alias, path, cfg, out):
    meshes = finish_meshes()
    lift = cfg.get('thresholds', {}).get('c3BaseLift', 0.2)
    rows = []; info = []
    for sheet in sheet_files(cfg):
        protos, places = parse_sheet(sheet)
        info.append({'sheet': str(sheet.relative_to(PROJECT)), 'sha256': sha256(sheet), 'placements': len(places), 'prototypes': len(protos)})
        for p in places:
            pr = protos.get(p['proto'])
            if not pr or pr[0] not in (0, 3) or p['pos'] is None or pr[1] is None: continue   # Tree = 0, Rock = 3
            x, y, z = p['pos']; height = max(.5, pr[1][1] * p['scale'])
            for m in meshes:
                bx0, bz0, bx1, bz1 = m['box']
                if not (bx0 <= x <= bx1 and bz0 <= z <= bz1): continue
                ys = [h for h in (vertical_hit(t, x, z) for t in m['tris']) if h is not None and y + lift <= h <= y + lift + height]
                if ys:
                    rows.append({'sheet': sheet.name, 'index': p['index'], 'id': p['id'], 'proto': p['proto'], 'kind': 'Tree' if pr[0] == 0 else 'Rock', 'pos': [x, y, z], 'root': m['root'], 'building': m['name'], 'part': m['part'], 'faceAbove': min(ys) - y})
    seen = {}
    for r in rows: seen.setdefault((r['sheet'], r['index']), r)
    uniq = list(seen.values())
    write(out, 'sheets-' + alias, {'sheets': info, 'hits': rows, 'unique': uniq, 'note': 'INFERRED: offline mesh JSON at export positions; the editor C3 / sheet-plan decide'},
          ['sheets: %d placement(s) under Finish297 LOD0/Collision faces' % len(uniq)] + ['  %s %s %s #%d %s under %s/%s (%s) +%.2f m' % (r['sheet'][:24], r['kind'], r['id'], r['index'], r['proto'], r['root'], r['building'], r['part'], r['faceAbove']) for r in uniq]
          + ['sheet %s sha256 %s (%d placements)' % (i['sheet'], i['sha256'], i['placements']) for i in info])


def cmd_offsets(alias, path, cfg, out):
    f3 = cfg.get('fix', {}).get('f3', {})
    parent_name = f3.get('parent', 'Village245')
    radius = 5.0
    cur = Scene(read_scene_lines(path))
    tids = roots_of(cur, [parent_name])[parent_name]
    if not tids: raise SystemExit('no %s in %s' % (parent_name, path))
    pairs = [(p, a, b) for (p, a, b) in dup_pairs(cur, tids) if p in tids]
    base = Scene(read_zip_lines(BASELINE_ZIP, BASELINE_ENTRY))
    btids = roots_of(base, [parent_name])[parent_name]
    if not btids: raise SystemExit('no %s in the baseline' % parent_name)
    bkids = base.children_of(btids[0])
    rows = []
    # 2026-10-04: a pair is one sibling dragged onto another by a name-key collision (an earlier tool addressed "Parent/Name" and
    # hit the first child of that name). The child order is the same in both scenes, so each member's own baseline place is known:
    # the member whose displacement no other child shares is the dragged one, and its home is its own baseline place.
    ckids = cur.children_of(tids[0])
    same_order = len(ckids) == len(bkids) and all(cur.nodes[x].name == base.nodes[y].name for x, y in zip(ckids, bkids))
    disp = {}
    if same_order:
        for x, y in zip(ckids, bkids):
            disp[x] = (round(cur.nodes[x].pos[0] - base.nodes[y].pos[0], 2), round(cur.nodes[x].pos[2] - base.nodes[y].pos[2], 2))
    shared = {}
    for x, d in disp.items(): shared[d] = shared.get(d, 0) + 1
    base_of = dict(zip(ckids, bkids)) if same_order else {}

    def order_of(t):
        k = 0
        for x in ckids:
            if x == t: return k
            if cur.nodes[x].name == cur.nodes[t].name: k += 1
        return -1

    for parent, a, b in pairs:
        an = cur.nodes[a]
        same = [c for c in bkids if base.nodes[c].name == an.name]
        same.sort(key=lambda c: math.hypot(base.nodes[c].pos[0] - an.pos[0], base.nodes[c].pos[2] - an.pos[2]))
        near = [c for c in same if math.hypot(base.nodes[c].pos[0] - an.pos[0], base.nodes[c].pos[2] - an.pos[2]) <= radius]
        row = {'parent': parent_name, 'name': an.name, 'current': list(an.pos), 'currentYaw': yaw_of(an.rot), 'candidates': len(near), 'status': 'ok', 'note': '', 'a': [], 'b': [], 'delta': [], 'aYaw': 0.0, 'bYaw': 0.0, 'dyaw': 0.0,
               'mover': '', 'stayer': '', 'home': [], 'homeRot': [], 'homeY': 0.0, 'homeYNeighbours': 0}
        if same_order:
            # dragged = displaced by an amount no other child shares (a group move such as the #299 yard shift is shared)
            lone = [t for t in (a, b) if disp[t] != (0.0, 0.0) and shared[disp[t]] == 1]
            if len(lone) == 1:
                mv = lone[0]; st = b if mv == a else a
                home = base.nodes[base_of[mv]]
                taken = [x for x in ckids if x != mv and cur.nodes[x].name == an.name and math.hypot(cur.nodes[x].pos[0] - home.pos[0], cur.nodes[x].pos[2] - home.pos[2]) <= 0.25]
                row['mover'] = '%s#%d' % (an.name, order_of(mv)); row['stayer'] = '%s#%d' % (an.name, order_of(st))
                row['home'] = list(home.pos); row['homeRot'] = list(home.rot)
                # height: the parts around the home place kept their relation to each other (a rail hangs on its posts, a cart bed
                # rides on its wheels), so the home height is the baseline height plus what the neighbours' heights changed by.
                # Neighbours = children within 3.5 m of the home place (baseline XZ) that were not dragged; a ridge fit of their
                # height change over XZ follows a row laid on a slope and stays flat across it.
                near_home = []
                for x, y in zip(ckids, bkids):
                    if x == mv or (disp[x] != (0.0, 0.0) and shared[disp[x]] == 1): continue
                    if disp[x] != (0.0, 0.0): continue   # a group that moved elsewhere says nothing about this place
                    bn = base.nodes[y]
                    d = math.hypot(bn.pos[0] - home.pos[0], bn.pos[2] - home.pos[2])
                    if d <= 3.5: near_home.append((bn.pos[0] - home.pos[0], bn.pos[2] - home.pos[2], cur.nodes[x].pos[1] - bn.pos[1]))
                if near_home:
                    # normal equations of dy = c0 + cx*dx + cz*dz with a small ridge on the slopes
                    lam = 0.02
                    n = len(near_home)
                    sx = sum(p[0] for p in near_home); sz = sum(p[1] for p in near_home); sy = sum(p[2] for p in near_home)
                    sxx = sum(p[0] * p[0] for p in near_home) + lam; szz = sum(p[1] * p[1] for p in near_home) + lam; sxz = sum(p[0] * p[1] for p in near_home)
                    sxy = sum(p[0] * p[2] for p in near_home); szy = sum(p[1] * p[2] for p in near_home)
                    m = [[n, sx, sz, sy], [sx, sxx, sxz, sxy], [sz, sxz, szz, szy]]
                    for i in range(3):
                        piv = max(range(i, 3), key=lambda r: abs(m[r][i])); m[i], m[piv] = m[piv], m[i]
                        for r in range(3):
                            if r != i and abs(m[i][i]) > 1e-12:
                                f = m[r][i] / m[i][i]
                                m[r] = [m[r][k] - f * m[i][k] for k in range(4)]
                    c0 = m[0][3] / m[0][0] if abs(m[0][0]) > 1e-12 else sy / n
                    row['homeY'] = home.pos[1] + c0; row['homeYNeighbours'] = n
                row['a'] = list(cur.nodes[st].pos); row['b'] = list(home.pos); row['aYaw'] = yaw_of(cur.nodes[st].rot); row['bYaw'] = yaw_of(home.rot)
                row['delta'] = [home.pos[0] - an.pos[0], home.pos[2] - an.pos[2]]
                row['dyaw'] = ((row['bYaw'] - row['aYaw'] + 180.0) % 360.0) - 180.0
                row['note'] = 'own baseline place of %s (dragged here by %.1f, %.1f); height %s' % (row['mover'], disp[mv][0], disp[mv][1], ('%.2f from %d neighbour(s)' % (row['homeY'], row['homeYNeighbours'])) if row['homeYNeighbours'] else 'from the terrain (no neighbour within 3.5 m)')
                if taken: row['status'] = 'ambiguous'; row['note'] = 'home place of %s already holds %s#%d' % (row['mover'], an.name, order_of(taken[0]))
                rows.append(row); continue
            row['note'] = 'no single dragged member (displacements %s / %s) - nearest-slot rule below' % (disp[a], disp[b])
        if len(same) < 2:
            row['status'] = 'missing'; row['note'] = 'baseline has %d sibling(s) named %s' % (len(same), an.name)
        elif len(near) >= 3:
            row['status'] = 'ambiguous'; row['note'] = '%d same-name baseline siblings within %.0f m' % (len(near), radius)
        else:
            A, B = base.nodes[same[0]], base.nodes[same[1]]
            row['a'] = list(A.pos); row['b'] = list(B.pos); row['aYaw'] = yaw_of(A.rot); row['bYaw'] = yaw_of(B.rot)
            row['delta'] = [B.pos[0] - A.pos[0], B.pos[2] - A.pos[2]]
            row['dyaw'] = ((row['bYaw'] - row['aYaw'] + 180.0) % 360.0) - 180.0
            if math.hypot(*row['delta']) < 0.01: row['status'] = 'ambiguous'; row['note'] = 'baseline siblings overlap as well'
            elif math.hypot(*row['delta']) > 10.0: row['note'] = 'partner %.1f m away in the baseline (name-key collision moved it here?) — check in capture' % math.hypot(*row['delta'])
        rows.append(row)
    doc = {'utc': utc(), 'baseline': str(BASELINE_ZIP.relative_to(ROOT)) + '!' + BASELINE_ENTRY, 'scene': str(path), 'parent': parent_name, 'pairs': rows}
    write(out, 'offsets-' + alias, doc, ['offsets %s: %d duplicate pair(s) under %s' % (alias, len(rows), parent_name)]
          + ['  %-22s %-9s cur (%.2f, %.2f) delta %s dyaw %.1f %s %s' % (r['name'], r['status'], r['current'][0], r['current'][2], ['%.2f' % d for d in r['delta']], r['dyaw'], ('mover ' + r['mover']) if r['mover'] else '', r['note']) for r in rows])
    (AUDIT / 'offline').mkdir(parents=True, exist_ok=True)
    (AUDIT / 'offline/dup-offsets.json').write_text(json.dumps(doc, ensure_ascii=False, indent=1), encoding='utf-8')
    print('F3 input ->', AUDIT / 'offline/dup-offsets.json')


def cmd_find(alias, path, cfg, out, needle):
    sc = Scene(read_scene_lines(path))
    rows = []
    for t, n in sc.nodes.items():
        if n.name and needle.lower() in n.name.lower():
            rows.append({'path': sc.path(t), 'world': list(sc.world(t)[0]), 'yaw': yaw_of(sc.world(t)[1]), 'active': n.active})
    rows.sort(key=lambda r: r['path'])
    for r in rows[:300]: print('%-100s (%.1f, %.1f, %.1f) yaw %.0f%s' % (r['path'][-100:], *r['world'], r['yaw'], '' if r['active'] else ' (inactive)'))
    return rows


def write(out, name, doc, lines):
    out.mkdir(parents=True, exist_ok=True)
    (out / (name + '.json')).write_text(json.dumps(doc, ensure_ascii=False, indent=1, default=list), encoding='utf-8')
    (out / (name + '.txt')).write_text('\n'.join(lines) + '\n', encoding='utf-8')
    print('\n'.join(lines[:60])); print('->', out / (name + '.json'))


def main():
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('sub', choices=['refs', 'dups', 'heights', 'sheets', 'offsets', 'find', 'all'])
    ap.add_argument('--scene', default='main'); ap.add_argument('--out'); ap.add_argument('--name', default='')
    a = ap.parse_args()
    cfg = load_config()
    alias, path = scene_path(a.scene, cfg)
    if not path.exists(): raise SystemExit('no scene ' + str(path))
    out = Path(a.out) if a.out else AUDIT / 'offline' / utc()
    if a.sub == 'find':
        if not a.name: raise SystemExit('--name required');
        cmd_find(alias, path, cfg, out, a.name); return
    subs = ['refs', 'dups', 'heights', 'sheets', 'offsets'] if a.sub == 'all' else [a.sub]
    for s in subs:   # one at a time (memory): each pass re-reads the scene line by line
        {'refs': cmd_refs, 'dups': cmd_dups, 'heights': cmd_heights, 'sheets': cmd_sheets, 'offsets': cmd_offsets}[s](alias, path, cfg, out)


if __name__ == '__main__':
    main()
