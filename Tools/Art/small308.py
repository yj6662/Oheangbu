"""#308 cliff boundary 1b, package 'pieces' - offline data and checks (SPEC-WORLD-CLIFF-BOUNDARY-308 design 8, D308-9c Q4, D308-9e).

Nothing here touches Unity, the Assets folder or the editor queue. Outputs go to
Art/World/Compact/Rebuild/CliffBoundary308/small_1b/ only.

  python Tools/Art/small308.py meshes        mesh facts of the rock sources (YAML mesh assets): triangles, bounds, guid
  python Tools/Art/small308.py guk           build small_1b/guk308.json from the declared ops numbers + kit data, then check it
  python Tools/Art/small308.py guk-check     check an existing guk308.json (no write except small_1b/guk308_check.json)
  python Tools/Art/small308.py escort        expected node heights of the escort start from the stage height field
  python Tools/Art/small308.py bell          the temple bell of 청림 산사: where it is, what stands beside it, the proposed host
  python Tools/Art/small308.py face0         collider candidates of CliffPath_jeokro_crag_Face_0 against LOD0 (gap table)
  python Tools/Art/small308.py veg           fallback rows for the BuildingFix308 Veg ledger (floating log, trunk through rock)
  python Tools/Art/small308.py all           everything above, in that order

Determinism: no clock value enters a data file (reports carry no timestamp); a second run writes byte-identical files.
Numbers the tool needs live in small_1b/small308.cfg.json (UP-1: kit choice, margins, tolerances) and small_1b/smallops308.cfg.json
(escort / bell / face0 / veg); the declared lift / ledge numbers
are READ from plan/ops308_v6.json (else v5) and never changed here.
"""
import os
for _k in ('OMP_NUM_THREADS', 'OPENBLAS_NUM_THREADS', 'MKL_NUM_THREADS', 'NUMEXPR_NUM_THREADS'):
    os.environ.setdefault(_k, '4')
import sys, json, math, re, hashlib, struct
import numpy as np

REPO = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..')).replace(os.sep, '/')
OFFLINE = REPO + '/Tools/Unity/Stage308_cliff1b/pieces/Offline'      # the modules of this package (small308_guk / _ops / _scene)
PROJECT = REPO + '/Oheangbu'
CB = REPO + '/Art/World/Compact/Rebuild/CliffBoundary308'
OUT = CB + '/small_1b'
CFG = OUT + '/small308.cfg.json'            # UP-1 pieces (guk308.json records its hash)
OPS_CFG = OUT + '/smallops308.cfg.json'     # escort / bell / face0 / veg
W, H, CELL = 1001, 1501, 4.0


# ---------------------------------------------------------------- io

def jload(path):
    with open(path, encoding='utf-8-sig') as f:
        return json.load(f)


def jdump(path, data):
    """Stable JSON: LF, UTF-8 without BOM, key order as built, floats rounded by the caller."""
    text = json.dumps(data, ensure_ascii=False, indent=1) + '\n'
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, 'w', encoding='utf-8', newline='\n') as f:
        f.write(text)
    return hashlib.sha256(text.encode('utf-8')).hexdigest()


def sha_file(path):
    h = hashlib.sha256()
    with open(path, 'rb') as f:
        for chunk in iter(lambda: f.read(1 << 20), b''):
            h.update(chunk)
    return h.hexdigest()


def r3(v, n=3):
    return round(float(v), n)


def cfg():
    return jload(CFG)


def ops_cfg():
    return jload(OPS_CFG)


def ops_file():
    for name in ('ops308_v6.json', 'ops308_v5.json'):
        p = CB + '/plan/' + name
        if os.path.exists(p):
            return p
    raise SystemExit('no plan/ops308_v6.json or ops308_v5.json')


# ---------------------------------------------------------------- height field (plan/Stage/FORMAT.md)

def height_load(path):
    b = open(path, 'rb').read()
    if len(b) != W * H * 4:
        raise SystemExit('height field %s has %d bytes (expected %d)' % (path, len(b), W * H * 4))
    return np.frombuffer(b, dtype='<f4').reshape(H, W)


def height_tri(h, x, z):
    """Tile-mesh triangulation: triangles (00, 10, 01) and (10, 01, 11) of each 4 m cell."""
    fx, fz = x / CELL, z / CELL
    j = min(max(int(math.floor(fx)), 0), W - 2)
    i = min(max(int(math.floor(fz)), 0), H - 2)
    u, v = fx - j, fz - i
    h00, h10, h01, h11 = float(h[i, j]), float(h[i, j + 1]), float(h[i + 1, j]), float(h[i + 1, j + 1])
    if u + v <= 1.0:
        return h00 + (h10 - h00) * u + (h01 - h00) * v
    return h11 + (h01 - h11) * (1 - u) + (h10 - h11) * (1 - v)


def stage_height(prefer):
    """First existing stage field of the list (repo-relative under the cliff folder). Returns (array, relative path, sha256)."""
    for rel in prefer:
        p = CB + '/' + rel
        if os.path.exists(p):
            return height_load(p), rel, sha_file(p)
    raise SystemExit('none of the stage height fields exists: ' + ', '.join(prefer))


# ---------------------------------------------------------------- Unity YAML mesh assets (text serialisation)

VFMT = {0: ('f', 4), 1: ('e', 2), 2: ('B', 1), 3: ('b', 1), 4: ('H', 2), 5: ('h', 2), 6: ('B', 1), 7: ('b', 1), 8: ('H', 2), 9: ('h', 2), 10: ('I', 4), 11: ('i', 4)}


def meta_guid(asset_path):
    m = re.search(r'^guid: ([0-9a-f]{32})', open(asset_path + '.meta', encoding='utf-8').read(), re.M)
    return m.group(1) if m else ''


def yaml_mesh(asset_path, want_triangles=True):
    """Positions (N,3 float64) and triangles (M,3 int) of a text-serialised Mesh asset; None when it is not one."""
    with open(asset_path, 'rb') as f:
        head = f.read(64)
        if not head.startswith(b'%YAML'):
            return None
        text = (head + f.read()).decode('utf-8', errors='replace')
    m = re.search(r'^--- !u!43 &(-?\d+)', text, re.M)
    if not m:
        return None
    file_id = int(m.group(1))
    name = re.search(r'^  m_Name: (.*)$', text, re.M).group(1).strip()
    if re.search(r'^  m_MeshCompression: [1-9]', text, re.M):
        return None
    head_end = text.find('m_IndexBuffer:')
    subs = [(int(a), int(b)) for a, b in re.findall(r'firstByte: (\d+)\n\s+indexCount: (\d+)', text[:head_end])]
    topo = [int(v) for v in re.findall(r'^    topology: (\d+)', text[:head_end], re.M)]
    vcount = int(re.search(r'm_VertexCount: (\d+)', text).group(1))
    chans = re.findall(r'- stream: (\d+)\n\s+offset: (\d+)\n\s+format: (\d+)\n\s+dimension: (\d+)', text)
    chans = [tuple(int(v) for v in c) for c in chans]
    # stream layout: every stream is one interleaved block, streams follow each other (16-byte alignment between streams)
    stream_size = {}
    for (s, off, fmt, dim) in chans:
        if dim & 0xF == 0:
            continue
        stream_size[s] = max(stream_size.get(s, 0), off + VFMT[fmt][1] * (dim & 0xF))
    for s in stream_size:
        stream_size[s] = (stream_size[s] + 3) // 4 * 4
    data_hex = re.search(r'_typelessdata: ([0-9a-f]*)', text).group(1)
    data = bytes.fromhex(data_hex)
    pos_stream, pos_off, pos_fmt, pos_dim = chans[0]
    if pos_fmt != 0 or (pos_dim & 0xF) != 3:
        return None
    start = 0
    for s in sorted(stream_size):
        if s == pos_stream:
            break
        start += stream_size[s] * vcount
        start = (start + 15) // 16 * 16
    stride = stream_size[pos_stream]
    raw = np.frombuffer(data, dtype=np.uint8, count=stride * vcount, offset=start).reshape(vcount, stride)
    pos = raw[:, pos_off:pos_off + 12].copy().view('<f4').reshape(vcount, 3).astype(np.float64)
    tris = None
    index_count = sum(c for _, c in subs)
    if want_triangles:
        idx_hex = re.search(r'm_IndexBuffer: ([0-9a-f]*)', text).group(1)
        idx_fmt = int(re.search(r'm_IndexFormat: (\d+)', text).group(1)) if re.search(r'm_IndexFormat: (\d+)', text) else 0
        size = 4 if idx_fmt == 1 else 2
        idx = np.frombuffer(bytes.fromhex(idx_hex), dtype='<u4' if idx_fmt == 1 else '<u2').astype(np.int64)
        parts = []
        for k, (first_byte, cnt) in enumerate(subs):
            if k < len(topo) and topo[k] != 0:
                continue
            st = first_byte // size
            parts.append(idx[st:st + cnt].reshape(-1, 3))
        tris = np.concatenate(parts) if parts else np.zeros((0, 3), dtype=np.int64)
    aabb = re.search(r'm_LocalAABB:\n\s+m_Center: \{x: ([-\d.e]+), y: ([-\d.e]+), z: ([-\d.e]+)\}\n\s+m_Extent: \{x: ([-\d.e]+), y: ([-\d.e]+), z: ([-\d.e]+)\}', text)
    centre = [float(aabb.group(i)) for i in (1, 2, 3)] if aabb else pos.mean(axis=0).tolist()
    extent = [float(aabb.group(i)) for i in (4, 5, 6)] if aabb else ((pos.max(axis=0) - pos.min(axis=0)) / 2).tolist()
    return {'name': name, 'file_id': file_id, 'vertices': vcount, 'triangles': index_count // 3, 'sub_meshes': len(subs),
            'centre': centre, 'extent': extent, 'pos': pos, 'tris': tris}


def cmd_meshes(write=True):
    c = cfg()
    rows = []
    for rel in c['kit']['survey']:
        p = PROJECT + '/' + rel
        if not os.path.exists(p):
            rows.append({'asset': rel, 'error': 'missing'})
            continue
        m = yaml_mesh(p, want_triangles=False)
        if m is None:
            rows.append({'asset': rel, 'guid': meta_guid(p), 'error': 'not a text mesh asset (binary or compressed): facts need the editor'})
            continue
        lo = [m['centre'][i] - m['extent'][i] for i in range(3)]
        hi = [m['centre'][i] + m['extent'][i] for i in range(3)]
        rows.append({'asset': rel, 'guid': meta_guid(p), 'file_id': m['file_id'], 'name': m['name'], 'vertices': m['vertices'], 'triangles': m['triangles'],
                     'sub_meshes': m['sub_meshes'], 'bounds_min': [r3(v) for v in lo], 'bounds_max': [r3(v) for v in hi], 'size': [r3(2 * v) for v in m['extent']]})
    out = {'version': '308.small.meshes.1', 'doc': 'mesh facts read offline from the YAML mesh assets [O]; no asset was opened by Unity', 'rows': rows}
    if write:
        jdump(OUT + '/kit_meshes.json', out)
    for r in rows:
        print('%-88s %s' % (r['asset'][-88:], r.get('error') or ('%6d tris  size %s  min %s max %s' % (r['triangles'], r['size'], r['bounds_min'], r['bounds_max']))))
    return out


def cmd_guk(write=True):
    if OFFLINE not in sys.path: sys.path.insert(0, OFFLINE)
    import small308_guk as G
    c = cfg()
    if write:
        data = G.build(c, True)
    else:
        data = jload(OUT + '/guk308.json')
        h, rel, sha = stage_height(c['height_candidates'])
        if sha != data['sources']['height']['sha256']:
            print('STALE: guk308.json was built on height %s, the stage field is now %s (%s) - run `guk` again' % (data['sources']['height']['sha256'][:12], sha[:12], rel))
        data['checks'] = G.check(c, data, h)
        jdump(OUT + '/guk308_check.json', dict(of='small_1b/guk308.json', sha256=sha_file(OUT + '/guk308.json'), checks=data['checks'], budget=data['budget']))
    ck = data['checks']
    print('guk308: ops %s, height %s, %d pieces (%d standable), %d dressing' % (data['sources']['ops']['path'], data['sources']['height']['path'], len(data['pieces']), len(data['standable']), len(data['dressing'])))
    for r in ck['rows']:
        print('  %-7s [%s] %-20s %s' % ('ok' if r['ok'] else ('PENDING' if r['tag'] == 'PENDING' else 'FAIL'), r['tag'], r['id'], r['text']))
    print('  failed %s, pending %s' % (ck['failed'] or 'none', ck['pending'] or 'none'))
    return data


def main(argv):
    for p in (os.path.dirname(os.path.abspath(__file__)), OFFLINE):
        if p not in sys.path: sys.path.insert(0, p)
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
    cmd = argv[1] if len(argv) > 1 else ''
    if cmd not in ('meshes', 'guk', 'guk-check', 'escort', 'bell', 'face0', 'veg', 'all'):
        print(__doc__); return 2
    if cmd in ('meshes', 'all'): cmd_meshes()
    if cmd in ('guk', 'all'): cmd_guk(True)
    if cmd == 'guk-check': cmd_guk(False)
    if cmd in ('escort', 'bell', 'face0', 'veg', 'all'):
        import small308_ops as O
        c = ops_cfg()
        if cmd in ('escort', 'all'): O.escort(c)
        if cmd in ('bell', 'all'): O.bell(c)
        if cmd in ('face0', 'all'): O.face0(c)
        if cmd in ('veg', 'all'): O.veg(c)
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
