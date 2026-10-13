"""#308 PackLite: offline Unity prefab resolver (pure Python, no editor).

Reads prefab YAML + .meta GUIDs and flattens a prefab (nested prefab instances included) into a list of mesh parts:
    {name, fbx, mesh_file_id, mesh_name, matrix (4x4 row-major, Unity space, relative to the prefab root), materials[guid]}
Unity space = left-handed, Y up, metres.  Also resolves materials -> albedo texture path.

usage:
    python Tools/Blender/PackLite308/unity_prefab.py index                 # (re)build the GUID index
    python Tools/Blender/PackLite308/unity_prefab.py parts <prefab path>   # print the flattened parts
"""
import json, math, os, re, sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[3]
PROJ = REPO / 'Oheangbu'
OUT = REPO / 'Art/World/PackLite308'
CACHE = OUT / '_cache'
PACK_DIRS = ['HwaseongHaenggung', 'JejumokGwana', 'HwaseongForteressGate', 'House_1', 'House_2', 'TemplesKor',
             'Korea_TreasureProps', 'KTinteractiveProp', 'KoreanTraditionalFestival', 'SeyeonjeongPavilion', 'StonePagoda']
EXTRA_ROOTS = []   # (root dir, label) for extracted .unitypackage sources (Art/World/PackLite308/_src/<pack>)

_index = None


def build_index():
    idx = {}
    roots = [(PROJ / 'Assets' / d) for d in PACK_DIRS]
    src = OUT / '_src'
    if src.exists(): roots += [p for p in src.iterdir() if p.is_dir()]
    for root in roots:
        for dp, _, fns in os.walk(root):
            for fn in fns:
                if not fn.endswith('.meta'): continue
                p = os.path.join(dp, fn)
                try:
                    with open(p, 'r', encoding='utf-8', errors='ignore') as f: head = f.read(400)
                except OSError: continue
                m = re.search(r'^guid: ([0-9a-f]{32})', head, re.M)
                if m: idx[m.group(1)] = p[:-5].replace('\\', '/')
    CACHE.mkdir(parents=True, exist_ok=True)
    (CACHE / 'guid_index.json').write_text(json.dumps(idx), encoding='utf-8')
    return idx


def index():
    global _index
    if _index is None:
        p = CACHE / 'guid_index.json'
        _index = json.loads(p.read_text(encoding='utf-8')) if p.exists() else build_index()
    return _index


# ---------- maths (Unity conventions, column vectors, row-major lists) ----------
def mat_identity(): return [[1, 0, 0, 0], [0, 1, 0, 0], [0, 0, 1, 0], [0, 0, 0, 1]]


def mat_mul(a, b): return [[sum(a[i][k] * b[k][j] for k in range(4)) for j in range(4)] for i in range(4)]


def mat_trs(p, q, s):
    x, y, z, w = q
    n = math.sqrt(x * x + y * y + z * z + w * w) or 1.0
    x, y, z, w = x / n, y / n, z / n, w / n
    r = [[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
         [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
         [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]]
    return [[r[0][0] * s[0], r[0][1] * s[1], r[0][2] * s[2], p[0]],
            [r[1][0] * s[0], r[1][1] * s[1], r[1][2] * s[2], p[1]],
            [r[2][0] * s[0], r[2][1] * s[1], r[2][2] * s[2], p[2]],
            [0, 0, 0, 1]]


# ---------- YAML (regex; Unity's subset) ----------
_DOC = re.compile(r'^--- !u!(\d+) &(-?\d+)( stripped)?\s*$', re.M)
_VEC = re.compile(r'\{x: ([-\d.e+]+), y: ([-\d.e+]+), z: ([-\d.e+]+)(?:, w: ([-\d.e+]+))?\}')
_REF = re.compile(r'\{fileID: (-?\d+)(?:, guid: ([0-9a-f]{32}))?[^}]*\}')


def _vec(body, key, default):
    m = re.search(r'^\s*' + key + r': (\{[^}]*\})', body, re.M)
    if not m: return default
    v = _VEC.match(m.group(1))
    return [float(x) for x in v.groups() if x is not None] if v else default


def _ref(body, key):
    m = re.search(r'^\s*' + key + r': (\{[^}]*\})', body, re.M)
    if not m: return (0, None)
    r = _REF.match(m.group(1))
    return (int(r.group(1)), r.group(2)) if r else (0, None)


def _reflist(body, key):
    m = re.search(r'^(\s*)' + key + r':\s*\n((?:\1\s*- .*\n?)+)', body, re.M)
    if not m: return []
    return [(int(a), b or None) for a, b in _REF.findall(m.group(2))]


_prefab_cache = {}


def load_prefab(path):
    path = str(path).replace('\\', '/')
    if path in _prefab_cache: return _prefab_cache[path]
    txt = open(path, 'r', encoding='utf-8', errors='ignore').read()
    docs = {}
    ms = list(_DOC.finditer(txt))
    for i, m in enumerate(ms):
        body = txt[m.end(): ms[i + 1].start() if i + 1 < len(ms) else len(txt)]
        docs[int(m.group(2))] = (int(m.group(1)), bool(m.group(3)), body)
    P = {'go': {}, 'tr': {}, 'mf': {}, 'mr': {}, 'pi': {}, 'lod': [], 'stripped_tr': {}, 'path': path}
    for fid, (cls, stripped, body) in docs.items():
        if cls == 1 and not stripped:
            nm = re.search(r'^\s*m_Name: (.*)$', body, re.M)
            act = re.search(r'^\s*m_IsActive: (\d)', body, re.M)
            P['go'][fid] = {'name': nm.group(1).strip() if nm else '', 'active': (act.group(1) == '1') if act else True}
        elif cls in (4, 224):
            if stripped:
                P['stripped_tr'][fid] = {'src': _ref(body, 'm_CorrespondingSourceObject'), 'pi': _ref(body, 'm_PrefabInstance')[0]}
            else:
                P['tr'][fid] = {'go': _ref(body, 'm_GameObject')[0], 'p': _vec(body, 'm_LocalPosition', [0, 0, 0]),
                                'q': _vec(body, 'm_LocalRotation', [0, 0, 0, 1]), 's': _vec(body, 'm_LocalScale', [1, 1, 1]),
                                'father': _ref(body, 'm_Father')[0]}
        elif cls == 33 and not stripped:
            P['mf'][_ref(body, 'm_GameObject')[0]] = _ref(body, 'm_Mesh')
        elif cls == 23 and not stripped:
            en = re.search(r'^\s*m_Enabled: (\d)', body, re.M)
            P['mr'][_ref(body, 'm_GameObject')[0]] = {'mats': [g for _, g in _reflist(body, 'm_Materials')], 'fid': fid,
                                                     'enabled': (en.group(1) == '1') if en else True}
        elif cls == 205 and not stripped:
            lods = re.split(r'^\s*- screenRelativeHeight:', body, flags=re.M)[1:]
            P['lod'].append([[int(a) for a, _ in re.findall(r'renderer: \{fileID: (-?\d+)()', l)] for l in lods])
        elif cls == 1001:
            mods = []
            for t, pp, val, oref in re.findall(r'- target: (\{[^}]*\})\s*\n\s*propertyPath: (.*)\n\s*value: ?(.*)\n\s*objectReference: (\{[^}]*\})', body):
                r = _REF.match(t); o = _REF.match(oref)
                mods.append((int(r.group(1)), pp.strip(), val.strip(), (int(o.group(1)), o.group(2)) if o else (0, None)))
            P['pi'][fid] = {'parent': _ref(body, 'm_TransformParent')[0], 'mods': mods, 'src': _ref(body, 'm_SourcePrefab')[1]}
    _prefab_cache[path] = P
    return P


def _world(P, tid, memo):
    if tid in memo: return memo[tid]
    if tid in P['tr']:
        t = P['tr'][tid]
        m = mat_trs(t['p'], t['q'], t['s'])
        if t['father'] and (t['father'] in P['tr'] or t['father'] in P['stripped_tr']): m = mat_mul(_world(P, t['father'], memo), m)
    else:
        m = mat_identity()   # stripped transform of a nested instance used as a parent: not resolved (counted by caller)
    memo[tid] = m
    return m


def resolve(path, parent=None, depth=0, warn=None, mat_override=None):
    """Flatten. Returns list of part dicts."""
    warn = warn if warn is not None else []
    parent = parent or mat_identity()
    path = str(path).replace('\\', '/')
    if path.lower().endswith('.fbx'):
        return [{'name': Path(path).stem, 'fbx': path, 'mode': 'model', 'mesh_file_id': None, 'matrix': parent, 'materials': mat_override or []}]
    P = load_prefab(path)
    memo = {}
    parts = []
    lod_skip = set()
    for lods in P['lod']:
        for l in lods[1:]: lod_skip.update(l)
    go_tr = {t['go']: tid for tid, t in P['tr'].items()}

    def active(go):
        tid = go_tr.get(go)
        while tid in P['tr']:
            g = P['tr'][tid]['go']
            if g in P['go'] and not P['go'][g]['active']: return False
            tid = P['tr'][tid]['father']
        return True

    for go, (mfid, mguid) in P['mf'].items():
        mr = P['mr'].get(go)
        if not mr or not mr['enabled'] or mr['fid'] in lod_skip or not active(go) or not mguid: continue
        fbx = index().get(mguid)
        if not fbx: warn.append('mesh guid not found %s in %s' % (mguid, path)); continue
        parts.append({'name': P['go'][go]['name'], 'fbx': fbx, 'mode': 'mesh', 'mesh_file_id': mfid,
                      'matrix': mat_mul(parent, _world(P, go_tr[go], memo)), 'materials': mr['mats']})
    for pid, pi in P['pi'].items():
        src = index().get(pi['src'])
        if not src: warn.append('source prefab guid not found %s in %s' % (pi['src'], path)); continue
        # root transform modifications
        p, q, s = [0.0, 0.0, 0.0], [0.0, 0.0, 0.0, 1.0], [1.0, 1.0, 1.0]
        is_model = src.lower().endswith(('.fbx', '.obj'))
        root_tid = None
        if not is_model:
            S = load_prefab(src)
            roots = [tid for tid, t in S['tr'].items() if not t['father']]
            if roots:
                root_tid = roots[0]; p, q, s = list(S['tr'][root_tid]['p']), list(S['tr'][root_tid]['q']), list(S['tr'][root_tid]['s'])
        active_inst = True
        other = 0
        for tfid, pp, val, oref in pi['mods']:
            try:
                if pp.startswith('m_LocalPosition.') and (root_tid is None or tfid == root_tid): p['xyz'.index(pp[-1])] = float(val)
                elif pp.startswith('m_LocalRotation.') and (root_tid is None or tfid == root_tid): q['xyzw'.index(pp[-1])] = float(val)
                elif pp.startswith('m_LocalScale.') and (root_tid is None or tfid == root_tid): s['xyz'.index(pp[-1])] = float(val)
                elif pp == 'm_IsActive' and val == '0': active_inst = False
                elif pp.startswith(('m_LocalPosition', 'm_LocalRotation', 'm_LocalScale', 'm_Materials', 'm_Mesh', 'm_Enabled')): other += 1
            except ValueError: pass
        if other: warn.append('%d unresolved non-root overrides on %s in %s' % (other, Path(src).name, Path(path).name))
        if not active_inst: continue
        par = _world(P, pi['parent'], memo) if pi['parent'] else mat_identity()
        if pi['parent'] and pi['parent'] not in P['tr']: warn.append('instance parented to nested transform in %s' % Path(path).name)
        m = mat_mul(parent, mat_mul(par, mat_trs(p, q, s)))
        if is_model:
            parts.append({'name': Path(src).stem, 'fbx': src, 'mode': 'model', 'mesh_file_id': None, 'matrix': m, 'materials': [],
                          'root_trs_given': True})
        elif depth < 6:
            if root_tid is not None:
                # child resolve() multiplies the root's own TRS again: pre-divide by using a patched copy
                S = load_prefab(src); keep = (S['tr'][root_tid]['p'], S['tr'][root_tid]['q'], S['tr'][root_tid]['s'])
                S['tr'][root_tid]['p'], S['tr'][root_tid]['q'], S['tr'][root_tid]['s'] = p, q, s
                sub = resolve(src, mat_mul(parent, par), depth + 1, warn)
                for q_ in sub: q_.setdefault('via', []).insert(0, Path(src).stem)
                parts += sub
                S['tr'][root_tid]['p'], S['tr'][root_tid]['q'], S['tr'][root_tid]['s'] = keep
            else:
                sub = resolve(src, m, depth + 1, warn)
                for q_ in sub: q_.setdefault('via', []).insert(0, Path(src).stem)
                parts += sub
    if depth == 0:
        for w in sorted(set(warn)): print('WARN', w, file=sys.stderr)
    return parts


# ---------- FBX meta / materials ----------
def fbx_meta(fbx):
    """globalScale, useFileScale, bakeAxisConversion, fileID -> mesh name table (when present)."""
    out = {'globalScale': 1.0, 'useFileScale': 1, 'bakeAxisConversion': 0, 'names': {}}
    p = fbx + '.meta'
    if not os.path.exists(p): return out
    t = open(p, 'r', encoding='utf-8', errors='ignore').read()
    for k in ('globalScale', 'useFileScale', 'bakeAxisConversion'):
        m = re.search(r'^\s*' + k + r': ([-\d.e]+)', t, re.M)
        if m: out[k] = float(m.group(1))
    for a, b in re.findall(r'^\s*(43\d+): (.+)$', t, re.M): out['names'][int(a)] = b.strip()
    for a, b in re.findall(r'- first:\s*\n\s*43: (-?\d+)\s*\n\s*second: (.+)', t): out['names'][int(a)] = b.strip()
    out['ext'] = {a.strip(): b for a, b in re.findall(r'type: UnityEngine:Material\s+assembly: \S+\s+name: (.+)\s+second: \{fileID: \d+, guid: ([0-9a-f]{32})', t)}
    return out


def material_info(guid):
    p = index().get(guid)
    if not p or not os.path.exists(p): return {'guid': guid, 'name': None}
    t = open(p, 'r', encoding='utf-8', errors='ignore').read()
    sh = re.search(r'm_Shader: \{fileID: (-?\d+)(?:, guid: ([0-9a-f]{32}))?', t)
    info = {'guid': guid, 'name': Path(p).stem, 'path': p, 'shader_fileID': sh.group(1) if sh else None, 'shader_guid': sh.group(2) if sh else None, 'tex': {}}
    for key, tg in re.findall(r'- (_\w+):\s*\n\s*m_Texture: \{fileID: \d+, guid: ([0-9a-f]{32})', t):
        info['tex'][key] = index().get(tg, 'guid:' + tg)
    col = re.search(r'- _BaseColor: \{r: ([-\d.e]+), g: ([-\d.e]+), b: ([-\d.e]+)', t) or re.search(r'- _Color: \{r: ([-\d.e]+), g: ([-\d.e]+), b: ([-\d.e]+)', t)
    if col: info['color'] = [float(x) for x in col.groups()]
    m = re.search(r'- _Cutoff: ([-\d.e]+)', t); info['cutoff'] = float(m.group(1)) if m else None
    info['alphaclip'] = bool(re.search(r'- _AlphaClip: 1', t)) or '_ALPHATEST_ON' in t
    for k in ('_BaseMap', '_MainTex', '_BaseColorMap', '_Albedo', '_Diffuse'):
        if k in info['tex']: info['albedo'] = info['tex'][k]; break
    else:
        cands = [v for k, v in info['tex'].items() if re.search(r'(?i)base|albedo|diff|color|_d\b|_bc', k + ' ' + v)]
        info['albedo'] = cands[0] if cands else None
    return info


if __name__ == '__main__':
    if sys.argv[1] == 'index':
        print(len(build_index()), 'guids')
    elif sys.argv[1] == 'parts':
        ps = resolve(sys.argv[2])
        from collections import Counter
        c = Counter((Path(p['fbx']).name, p['mode']) for p in ps)
        print(len(ps), 'parts,', len(c), 'distinct fbx')
        for (f, m), n in c.most_common(40): print('  %4d %s %s' % (n, m, f))
