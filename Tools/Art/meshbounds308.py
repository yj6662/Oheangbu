#!/usr/bin/env python3
"""Offline mesh bounds for prop choices (D308-16 relayout fix, F2 / F3).

  python Tools/Art/meshbounds308.py <prefab or mesh asset path under Oheangbu/> [...]

What is measured, and how far it can be trusted
  .asset mesh   m_LocalAABB of the serialized Mesh (what Unity itself stores)                                        [O exact]
  .fbx          every Geometry's Vertices of the binary FBX, each turned by its Model's Lcl Rotation / Scaling / PreRotation
                and GeometricTranslation / Rotation / Scaling, then by the file's unit scale (UnitScaleFactor / 100 when the
                .meta says useFileScale 1) x the .meta globalScale. A Z-up file (UpAxis 2) is turned to Y-up.          [O derived]
                Unity bakes or keeps the axis turn depending on the importer; a prefab built from the model carries the
                model's root rotation in its own Transform. So the three SIZES are right and "which one is up" is the
                derived part: bounds_of_prefab() reports the sizes sorted and the box it believes, and the caller's
                post-condition (stone top <= limit, measured again in the editor by scene-check) is the judge.
  .prefab       every MeshFilter of the prefab YAML (plain prefabs, no nested instances): the mesh box through the
                Transform chain of the prefab, as one box in the prefab root's frame.
Nothing is written.
"""
import json, math, os, re, struct, sys, zlib
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
PROJECT = ROOT / 'Oheangbu'
V3 = r'\{x: ([-\d.eE+]+), y: ([-\d.eE+]+), z: ([-\d.eE+]+)\}'
Q4 = r'\{x: ([-\d.eE+]+), y: ([-\d.eE+]+), z: ([-\d.eE+]+), w: ([-\d.eE+]+)\}'


def qrot(q, v):
    x, y, z, w = q
    tx = 2 * (y * v[2] - z * v[1]); ty = 2 * (z * v[0] - x * v[2]); tz = 2 * (x * v[1] - y * v[0])
    return (v[0] + w * tx + (y * tz - z * ty), v[1] + w * ty + (z * tx - x * tz), v[2] + w * tz + (x * ty - y * tx))


def qmul(a, b):
    ax, ay, az, aw = a; bx, by, bz, bw = b
    return (aw * bx + ax * bw + ay * bz - az * by, aw * by - ax * bz + ay * bw + az * bx, aw * bz + ax * by - ay * bx + az * bw, aw * bw - ax * bx - ay * by - az * bz)


# ---------------------------------------------------------------------------------------------- binary FBX

class Fbx:
    def __init__(self, path):
        self.b = Path(path).read_bytes()
        if not self.b.startswith(b'Kaydara FBX Binary'): raise ValueError('not a binary FBX: %s' % path)
        self.ver = struct.unpack_from('<I', self.b, 23)[0]; self.wide = self.ver >= 7500
        self.top = self._nodes(27, len(self.b))

    def _nodes(self, at, end):
        out = []; b = self.b; head = 25 if self.wide else 13
        while at + head <= end:
            if self.wide: eo, np_, pl = struct.unpack_from('<QQQ', b, at); nl = b[at + 24]; p = at + 25
            else: eo, np_, pl = struct.unpack_from('<III', b, at); nl = b[at + 12]; p = at + 13
            if eo == 0: break
            name = b[p:p + nl].decode('ascii', 'replace'); p += nl
            props = self._props(p, np_); p += pl
            kids = self._nodes(p, eo) if p < eo else []
            out.append((name, props, kids)); at = eo
        return out

    def _props(self, p, n):
        b = self.b; out = []
        for _ in range(n):
            t = chr(b[p]); p += 1
            if t in 'YCIFDL':
                fmt = {'Y': '<h', 'C': '<b', 'I': '<i', 'F': '<f', 'D': '<d', 'L': '<q'}[t]; out.append(struct.unpack_from(fmt, b, p)[0]); p += struct.calcsize(fmt)
            elif t in 'fdlib':
                ln, enc, cl = struct.unpack_from('<III', b, p); p += 12; raw = b[p:p + cl]; p += cl
                if enc == 1: raw = zlib.decompress(raw)
                fmt = {'f': 'f', 'd': 'd', 'l': 'q', 'i': 'i', 'b': 'b'}[t]; out.append(struct.unpack('<%d%s' % (ln, fmt), raw[:ln * struct.calcsize(fmt)]))
            elif t in 'SR':
                ln = struct.unpack_from('<I', b, p)[0]; p += 4; out.append(b[p:p + ln]); p += ln
            else:
                raise ValueError('FBX property type %r' % t)
        return out

    def find(self, nodes, name): return [n for n in nodes if n[0] == name]

    def props70(self, node):
        out = {}
        for p70 in self.find(node[2], 'Properties70'):
            for p in self.find(p70[2], 'P'):
                key = p[1][0].decode('ascii', 'replace'); out[key] = p[1][4:]
        return out

    def scene(self):
        gs = {}
        for g in self.find(self.top, 'GlobalSettings'): gs = self.props70(g)
        unit = float(gs.get('UnitScaleFactor', [1.0])[0]); up = int(gs.get('UpAxis', [1])[0])
        objs = self.find(self.top, 'Objects')[0][2]
        geo = {}; model = {}
        for n in objs:
            if n[0] == 'Geometry':
                v = self.find(n[2], 'Vertices')
                if v: geo[n[1][0]] = v[0][1][0]
            elif n[0] == 'Model':
                model[n[1][0]] = (n[1][1].split(b'\x00')[0].decode('utf-8', 'replace'), self.props70(n))
        link = {}; parent = {}
        for c in self.find(self.top, 'Connections')[0][2]:
            if c[0] != 'C' or c[1][0] != b'OO': continue
            a, b_ = c[1][1], c[1][2]
            if a in geo and b_ in model: link[a] = b_
            elif a in model and b_ in model: parent[a] = b_
        return unit, up, geo, model, link, parent


def euler_q(e):
    """FBX default rotation order XYZ (degrees): R = Rz * Ry * Rx applied to a column vector."""
    x, y, z = (math.radians(a) / 2 for a in e)
    qx = (math.sin(x), 0, 0, math.cos(x)); qy = (0, math.sin(y), 0, math.cos(y)); qz = (0, 0, math.sin(z), math.cos(z))
    return qmul(qz, qmul(qy, qx))


def fbx_boxes(path):
    """{model name: (lo, hi)} in metres, Y-up, per mesh object (its own node rotation / scale applied, translation not)."""
    f = Fbx(path); unit, up, geo, model, link, parent = f.scene()
    meta = Path(str(path) + '.meta'); gscale = 1.0; file_scale = True
    if meta.exists():
        t = meta.read_text(encoding='utf-8', errors='replace')
        m = re.search(r'globalScale: ([-\d.eE+]+)', t); gscale = float(m.group(1)) if m else 1.0
        m = re.search(r'useFileScale: (\d)', t); file_scale = (m.group(1) == '1') if m else True
    k = gscale * (unit / 100.0 if file_scale else 1.0)
    out = {}
    for gid, verts in geo.items():
        mid = link.get(gid); name, pr = model.get(mid, ('?', {}))
        def P(key, d): return tuple(float(x) for x in pr.get(key, d)[:3])
        # the chain of this model and its parents (rotation and scale only: translation moves the box, not its size)
        chain = []; cur = mid; depth = 0
        while cur in model and depth < 32:
            p = model[cur][1]
            def Q(key, d, p=p): return tuple(float(x) for x in p.get(key, d)[:3])
            chain.append((euler_q(Q('PreRotation', (0, 0, 0))), euler_q(Q('Lcl Rotation', (0, 0, 0))), Q('Lcl Scaling', (1, 1, 1)), Q('Lcl Translation', (0, 0, 0))))
            cur = parent.get(cur); depth += 1
        gt = P('GeometricTranslation', (0, 0, 0)); gr = euler_q(P('GeometricRotation', (0, 0, 0))); gsc = P('GeometricScaling', (1, 1, 1))
        lo = [float('inf')] * 3; hi = [float('-inf')] * 3
        for i in range(0, len(verts), 3):
            v = (verts[i] * gsc[0], verts[i + 1] * gsc[1], verts[i + 2] * gsc[2]); v = qrot(gr, v); v = (v[0] + gt[0], v[1] + gt[1], v[2] + gt[2])
            for n, (pre, rot, sc, tr) in enumerate(chain):
                v = (v[0] * sc[0], v[1] * sc[1], v[2] * sc[2]); v = qrot(qmul(pre, rot), v)
                if n > 0 or True: v = (v[0] + tr[0], v[1] + tr[1], v[2] + tr[2])
            if up == 2: v = (v[0], v[2], -v[1])
            v = (v[0] * k, v[1] * k, v[2] * k)
            for a in range(3): lo[a] = min(lo[a], v[a]); hi[a] = max(hi[a], v[a])
        out[name] = (tuple(lo), tuple(hi), len(verts) // 3)
    return out, {'unit': unit, 'up_axis': up, 'global_scale': gscale, 'use_file_scale': file_scale, 'k': k, 'fbx_version': f.ver}


def asset_box(path):
    t = Path(path).read_text(encoding='utf-8', errors='replace')
    m = re.search(r'm_LocalAABB:\s+m_Center: ' + V3 + r'\s+m_Extent: ' + V3, t)
    if not m: return None
    v = [float(x) for x in m.groups()]
    return tuple(v[i] - v[i + 3] for i in range(3)), tuple(v[i] + v[i + 3] for i in range(3))


_guid = None


def guid_path(guid, cache=None):
    """guid -> 'Assets/...' ; a cache file (json) is used when given, else the .meta files are scanned once (slow disk: minutes)."""
    global _guid
    if _guid is None:
        if cache and Path(cache).exists(): _guid = json.loads(Path(cache).read_text())
        else:
            _guid = {}
            for dp, dn, fn in os.walk(PROJECT / 'Assets'):
                for f in fn:
                    if not f.endswith('.meta'): continue
                    p = os.path.join(dp, f)
                    try:
                        with open(p, 'rb') as fh: head = fh.read(160)
                    except OSError: continue
                    i = head.find(b'guid: ')
                    if i >= 0: _guid[head[i + 6:i + 38].decode()] = os.path.relpath(p, PROJECT)[:-5].replace('\\', '/')
            if cache: Path(cache).write_text(json.dumps(_guid))
    return _guid.get(guid)


def prefab_box(prefab, cache=None, mesh_dir_hint=None):
    """(lo, hi, notes) of a plain prefab in its root frame; None when no mesh could be measured."""
    text = (PROJECT / prefab).read_text(encoding='utf-8', errors='replace')
    docs = re.split(r'^--- !u!(\d+) &(-?\d+).*$', text, flags=re.M)
    tr = {}; mf = []; go_name = {}
    for i in range(1, len(docs) - 2, 3):
        cls, fid, body = int(docs[i]), docs[i + 1], docs[i + 2]
        go = re.search(r'm_GameObject: \{fileID: (-?\d+)\}', body)
        if cls == 1:
            m = re.search(r'm_Name: (.*)', body); go_name[fid] = m.group(1).strip() if m else '?'
        elif cls == 4 and go:
            q = re.search(r'm_LocalRotation: ' + Q4, body); p = re.search(r'm_LocalPosition: ' + V3, body); s = re.search(r'm_LocalScale: ' + V3, body)
            fa = re.search(r'm_Father: \{fileID: (-?\d+)\}', body)
            tr[fid] = {'go': go.group(1), 'q': tuple(float(c) for c in q.groups()), 'p': tuple(float(c) for c in p.groups()), 's': tuple(float(c) for c in s.groups()), 'father': fa.group(1) if fa else '0'}
        elif cls == 33 and go:
            g = re.search(r'm_Mesh: \{fileID: (-?\d+), guid: ([0-9a-f]{32})', body)
            if g: mf.append((go.group(1), g.group(1), g.group(2)))
    by_go = {t['go']: fid for fid, t in tr.items()}
    lo = [float('inf')] * 3; hi = [float('-inf')] * 3; notes = []; used = 0
    for go, mesh_fid, guid in mf:
        src = guid_path(guid, cache)
        if src is None: notes.append('mesh guid %s not found' % guid); continue
        if src.endswith('.asset'):
            box = asset_box(PROJECT / src); how = 'asset m_LocalAABB'
        elif src.lower().endswith('.fbx'):
            boxes, info = fbx_boxes(PROJECT / src)
            name = go_name.get(go, '?')
            pick = boxes.get(name) or (list(boxes.values())[0] if len(boxes) == 1 else None)
            if pick is None:
                # several meshes in the file and no name match: the union (largest possible box)
                l = [min(b[0][a] for b in boxes.values()) for a in range(3)]; h = [max(b[1][a] for b in boxes.values()) for a in range(3)]; pick = (l, h, 0)
                notes.append('%s: %d meshes in the file, none named %s - union used' % (src, len(boxes), name))
            box = (pick[0], pick[1]); how = 'fbx vertices (unit %.4g, up axis %d, k %.4g)' % (info['unit'], info['up_axis'], info['k'])
        else:
            notes.append('mesh source %s not readable offline' % src); continue
        if box is None: notes.append('no bounds in %s' % src); continue
        notes.append('%s <- %s [%s] size %.2f x %.2f x %.2f' % (go_name.get(go, '?'), src, how, box[1][0] - box[0][0], box[1][1] - box[0][1], box[1][2] - box[0][2]))
        used += 1
        for i in range(8):
            v = tuple(box[(i >> a) & 1][a] for a in range(3)); t = by_go.get(go); depth = 0
            while t in tr and depth < 32:
                n = tr[t]; v = (v[0] * n['s'][0], v[1] * n['s'][1], v[2] * n['s'][2]); v = qrot(n['q'], v)
                if n['father'] != '0': v = (v[0] + n['p'][0], v[1] + n['p'][1], v[2] + n['p'][2])
                t = n['father']; depth += 1
            for a in range(3): lo[a] = min(lo[a], v[a]); hi[a] = max(hi[a], v[a])
    if used == 0: return None, None, notes
    return tuple(lo), tuple(hi), notes


def main():
    cache = None
    args = sys.argv[1:]
    if args and args[0] == '--cache': cache = args[1]; args = args[2:]
    for a in args:
        if a.endswith('.prefab'):
            lo, hi, notes = prefab_box(a, cache)
            if lo is None: print('%s: no measurable mesh' % a)
            else: print('%s: x %.2f..%.2f  y %.2f..%.2f  z %.2f..%.2f  (size %.2f x %.2f x %.2f)' % (a, lo[0], hi[0], lo[1], hi[1], lo[2], hi[2], hi[0] - lo[0], hi[1] - lo[1], hi[2] - lo[2]))
            for n in notes: print('   ' + n)
        elif a.lower().endswith('.fbx'):
            boxes, info = fbx_boxes(PROJECT / a); print(a, info)
            for k, (lo, hi, n) in boxes.items(): print('   %s: %d verts  x %.2f..%.2f  y %.2f..%.2f  z %.2f..%.2f' % (k, n, lo[0], hi[0], lo[1], hi[1], lo[2], hi[2]))
        else:
            print(a, asset_box(PROJECT / a))


if __name__ == '__main__':
    main()
