# -*- coding: utf-8 -*-
"""SPEC-MAP-OVERHAUL-308 (DECISIONS D308-14 / 14b / 15) - shared library of the offline map bakers (track A).

Deterministic: numpy + PIL only, no clock, no unseeded RNG. Read-only on the Unity project; every writer goes through
guard_out(), which refuses a path inside Oheangbu/ (the editor command `Map308 import` stages the bundle, not these tools).

    paths / constants      the asset contract of the Spec, section 7 (names, sizes, coordinate rules)
    parse_map / parse_layout   the main scene's data COPIES, read as text (Map.asset, WorldLayout_Main.asset)
    geometry               Douglas-Peucker, resample, Chaikin, point-to-polyline distance
    grids                  box blur, limited Euclidean distance, tileable value noise
    brush                  a small coverage rasteriser (discs along a polyline, polygons) used by the atlas and icon bakers
"""
import hashlib, json, math, re, struct, sys, zlib
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

TOOLS = Path(__file__).resolve().parent
ROOT = TOOLS.parents[1]
A = ROOT / 'Oheangbu/Assets/_Project'
RB = ROOT / 'Art/World/Compact/Rebuild'
STAGE_DIR = ROOT / 'Tools/Unity/Stage308_map'
OUT = STAGE_DIR / '_ProjectAssets/Art/UI/UI308/Map'        # the bundle of stage 'base' (what the scenes carry until the cliff ledger applies 1a)
BUNDLES = STAGE_DIR / 'Bundles'                             # Bundles/<stage>/ : the same eight files baked for another height stage
CONCEPT = ROOT / 'Art/UI308/Map'
THEME_DIR = ROOT / 'Tools/Unity/Stage308_theme/_ProjectAssets/Art/UI/UI308/Theme'

CAPITAL_GATE_HALF_M = 6.7                                   # half of the opening left in the capital wall stroke at each of its gates
                                                            # (= the long wall's gate block, 13.4 m between the wing ends)
CONTRACT_VERSION = 1
SEED = 308
WORLD_X, WORLD_Z = 4000.0, 6000.0
HH, WW, CELL = 1501, 1001, 4.0                 # height lattice nodes (rows = z / 4, cols = x / 4)
TH, TW = 1500, 1000                            # terrain texels (4 m cells, value at the cell centre)
FOG_W, FOG_H, FOG_CELL = 125, 188, 32.0        # discovery grid (WorldMapDiscoveryGrid), south row first
BIN_M, BINS_X, BINS_Z = 250.0, 16, 24

MAP_ASSET = A / ('Art/World/Architecture296/Data/0618cb855c06bed4a8d3b56091cf4899_67d99b22219f96b4dba38b733e6fe08d_'
                 '19434fb48c65a0749a52af52499c4a39_Map.asset')
LAYOUT_MAIN = A / 'Scenes/World/Main/WorldLayout_Main.asset'
LAYOUT_CAND = A / ('Art/World/Architecture296/Data/211377a1cf4052f47905e2d4c976f7c4_566363b0e71d6e5499a8f6599e450127_'
                   '782f53e1c87540e449a7951bb733ff7a_WorldLayout.asset')
BASE_HEIGHT = A / 'Art/World/Finish297/Surface/height.bytes'
WET = RB / 'Watershed295/Generated/wet.bytes'
CROSSINGS = RB / 'Watershed295/Generated/crossings.json'
CLIFF_PLAN = RB / 'CliffBoundary308/plan'
BOUNDARY = CLIFF_PLAN / 'boundary308.json'
FOOTPRINT = RB / 'Enclosure305/Out/Seal308/footprint308.json'
WALL_LAYOUTS = [RB / 'Finish297/Capital/layout.json', RB / 'Finish297/Cheolong/layout.json', RB / 'Finish297/Hwanggyeong/layout.json']
SHEETS = {  # vegetation sheets of the main scene (cliff308_base.SHEETS)
    'DryLandscape': 'Art/World/Architecture296/Data/757de110844e3b4479c04f9c49d8a6b6_DryLandscape.asset',
    'River294Dressing': 'Art/World/Architecture296/Data/9be35ffe935cc2a48a2c9e57deed2af6_fb0262ee6f18ce44db0b6468d94e5e6f_Dressing.asset',
    'Banks': 'Art/World/Architecture296/Data/d47e502ebb4212d45aed27dbbc4bbb5e_Banks.asset',
    'Forest305': 'Art/World/Enclosure305/Forest305.asset',
}
STAGES = {  # stage name -> (height file, reach masks of the cliff ledger, boundary stage tags that are BUILT at this stage)
    'base': dict(height=BASE_HEIGHT, built=()),
    '1a': dict(height=CLIFF_PLAN / 'Stage/height_p1a.bytes', built=('1a',),
               reach=dict(S0=CLIFF_PLAN / '_work/reach_p1a_S0_as_built.npy', S2=CLIFF_PLAN / '_work/reach_p1a_S2.npy',
                          UP1=CLIFF_PLAN / '_work/reach_p1a_top_UP1.npy')),
    # 1b (ops v6): the as-built state of the 1b ledger = gate closed, long wall standing, seam runs kept (Enclosure305 v305.4a;
    # closure_p1b.json states.S0_wall_seam_kept). The lift of the cliff-top field stays OFF, so UP1 is in no reach set.
    # 'boundary' = the 1b plan lines in the old shape (Tools/Art/boundary308_segments.py); 'long_wall' = the wall ledger's layout.
    '1b': dict(height=CLIFF_PLAN / 'Stage/height_p1b.bytes', built=('1a', '1b'),
               reach=dict(S0=CLIFF_PLAN / '_work/reach_p1b_v6_S0_wall_seam_kept.npy', S2=CLIFF_PLAN / '_work/reach_p1b_v6_S2_wall_seam_kept.npy',
                          UP1=CLIFF_PLAN / '_work/reach_p1b_v6_top_UP1.npy'),
               boundary=CLIFF_PLAN / 'boundary308_1b_segments.json', long_wall=RB / 'CliffBoundary308/wall_1b/jangseong308.json',
               # what must be BUILT in the scenes before this bundle may be imported: the wall ledger of every scene (Jangseong308
               # LedgerFile = Out/cb308-wall-<key>.json, state 'applied'). The scenes' height stage alone picks the bundle, and the
               # height goes to 1b before the wall is built - without this the map would draw a wall that does not stand yet.
               requires=tuple(('wall.' + k, RB / f'CliffBoundary308/Out/cb308-wall-{k}.json') for k in ('main', 'arch296', 'folk298'))),
}
ABSENT_SHA = '0' * 64      # the sha recorded for a 'requires.*' input that was not applied at bake time (no file has it: the import refuses)

# value structure (Spec section 4); sRGB code values
PAPER = '#DFDBD0'; WASH = '#8F8B81'; INK = '#141413'; CINNABAR = '#B8392B'


def bundle_dir(stage):
    """where the bundle of a height stage lives: 'base' = the stage folder's mirror of Assets (OUT), any other stage beside it
    under Bundles/<stage>/. Both are kept; the editor import takes the one whose stage is the stage of the scenes."""
    return OUT if stage == 'base' else BUNDLES / stage


def concept_dir(stage):
    """where the twin sheets of a stage go (Art/UI308/Map for 'base', Art/UI308/Map/stage_<stage> otherwise)."""
    return CONCEPT if stage == 'base' else CONCEPT / ('stage_' + stage)


def ledger_state(p):
    """'absent' when the file is not there, else its "state" ('applied', 'applying', ...; 'unreadable' when it does not parse)."""
    p = Path(p)
    if not p.exists(): return 'absent'
    try:
        return str(json.loads(p.read_text(encoding='utf-8-sig')).get('state') or '?')
    except (ValueError, AttributeError):
        return 'unreadable'


def require_entries(stage):
    """inputs[] entries (role 'requires.<name>') of the ledgers that must be applied before the bundle of `stage` may be imported.
    Applied at bake time -> the ledger's sha (the import then accepts it while the ledger is unchanged). Not applied -> ABSENT_SHA:
    the editor import compares every inputs[] entry with the file on disk and refuses ('input missing' while there is no ledger,
    'input changed since the bake' once there is one), so a bundle baked ahead of the build can be checked offline but never imported."""
    out = []
    for name, p in STAGES[stage].get('requires', ()):
        st = ledger_state(p)
        out.append(dict(role='requires.' + name, path=rel(p), sha256=sha256(p) if st == 'applied' else ABSENT_SHA, state=st))
    return out


def requires_status(note):
    """-> (pending, stale): the 'requires.*' inputs recorded as not applied and still not applied (the bundle is consistent but
    cannot be imported yet), and those whose ledger changed since the bake (bake again)."""
    pending, stale = [], []
    for i in note['inputs']:
        if not i.get('role', '').startswith('requires.'): continue
        p = ROOT / i['path']; st = ledger_state(p)
        if i['sha256'] == ABSENT_SHA:
            (stale if st == 'applied' else pending).append(f"{i['role']}: {i['path']} ({'applied since the bake' if st == 'applied' else st})")
        elif st != 'applied' or sha256(p) != i['sha256']:
            stale.append(f"{i['role']}: {i['path']} ({st}, sha changed)" if st == 'applied' else f"{i['role']}: {i['path']} ({st})")
    return pending, stale


def stale_inputs(note):
    """inputs[] entries whose file changed since the bake -> [(role, path, what)] ; `what` names the change: the recorded and the
    present sha, the file's time, and for a vegetation sheet / layout copy what in it moved (tree count, which block).
    'requires.*' entries are not files the bake read: requires_status() reports them."""
    import time
    out = []
    for i in note['inputs']:
        if i.get('role', '').startswith('requires.'): continue
        p = ROOT / i['path']
        if not p.exists(): out.append((i.get('role', ''), i['path'], 'file missing')); continue
        now = sha256(p)
        if now == i['sha256']: continue
        what = f"sha {i['sha256'][:10]} -> {now[:10]}, written {time.strftime('%m-%d %H:%M', time.localtime(p.stat().st_mtime))}, {p.stat().st_size} B"
        role = i.get('role', '')
        if role in ('layoutMain', 'layoutCandidate') and 'blocks' in i:
            try:
                cur = layout_block_shas(p); moved = [k for k in cur if cur[k] != i['blocks'].get(k)]
                what += '; blocks changed: ' + (', '.join(moved) if moved else 'none of Routes / River / Drainages / Realms (another part of the file)')
            except SystemExit:
                what += '; a layout block is missing'
        if role.startswith('sheet.'):
            n = sum(1 for line in open(p, encoding='utf-8') if line.startswith('    PrototypeId:') and TREE_RX.search(line))
            was = (note.get('stats', {}).get('terrain', {}).get('trees', {}) or {}).get(role[6:])
            what += f'; trees {was} -> {n}'
        out.append((role, i['path'], what))
    return out


def utf8():
    try:
        sys.stdout.reconfigure(encoding='utf-8')
    except Exception:
        pass


def rel(p):
    try:
        return str(Path(p).resolve().relative_to(ROOT)).replace('\\', '/')
    except ValueError:
        return str(p).replace('\\', '/')


def sha256_bytes(b):
    return hashlib.sha256(b).hexdigest()


def sha256(path):
    return sha256_bytes(Path(path).read_bytes())


def guard_out(path):
    q = Path(path).resolve()
    try:
        q.relative_to((ROOT / 'Oheangbu').resolve())
    except ValueError:
        return q
    raise SystemExit(f'REFUSED: {q} is inside the Unity project; the map308 bakers write under Tools/Unity/Stage308_map or Art/UI308/Map only')


def write_bytes(path, data):
    q = guard_out(path)
    q.parent.mkdir(parents=True, exist_ok=True)
    q.write_bytes(data)
    return sha256_bytes(data)


def write_text(path, text):
    return write_bytes(path, text.replace('\r\n', '\n').encode('utf-8'))


def _js(o):
    if isinstance(o, np.floating): return float(o)
    if isinstance(o, np.integer): return int(o)
    if isinstance(o, np.bool_): return bool(o)
    if isinstance(o, np.ndarray): return o.tolist()
    if isinstance(o, Path): return rel(o)
    raise TypeError(type(o))


def dumps(obj, indent=1):
    return json.dumps(obj, ensure_ascii=False, indent=indent, default=_js) + '\n'


def png_bytes(arr, mode=None):
    """uint8 array -> PNG bytes (no metadata, fixed zlib level: the same array always gives the same bytes)."""
    import io
    a = np.ascontiguousarray(arr, np.uint8)
    if mode is None:
        mode = {2: 'L', 3: {3: 'RGB', 4: 'RGBA', 2: 'LA'}.get(a.shape[2] if a.ndim == 3 else 0)}[a.ndim]
    b = io.BytesIO()
    Image.fromarray(a, mode).save(b, 'PNG', optimize=False, compress_level=9)
    return b.getvalue()


def write_png(path, arr, mode=None):
    return write_bytes(path, png_bytes(arr, mode))


def hexc(s):
    s = s.lstrip('#')
    return np.array([int(s[i:i + 2], 16) for i in (0, 2, 4)], float) / 255.0


def fnv1a32(s):
    h = 0x811C9DC5
    for b in s.encode('utf-8'):
        h = ((h ^ b) * 0x01000193) & 0xFFFFFFFF
    return h


# ---- colour maths (WCAG relative luminance, sRGB-coded 'over') ---------------------------------------------------------
def srgb_to_lin(c):
    c = np.asarray(c, float)
    return np.where(c <= .04045, c / 12.92, ((c + .055) / 1.055) ** 2.4)


def lin_to_srgb(c):
    c = np.clip(np.asarray(c, float), 0, 1)
    return np.where(c <= .0031308, c * 12.92, 1.055 * c ** (1 / 2.4) - .055)


def lum(c):
    l = srgb_to_lin(c)
    return .2126 * l[..., 0] + .7152 * l[..., 1] + .0722 * l[..., 2]


def contrast(a, b):
    la, lb = float(lum(np.asarray(a, float))), float(lum(np.asarray(b, float)))
    return (max(la, lb) + .05) / (min(la, lb) + .05)


def over(fg, bg, a):
    return np.asarray(fg, float) * a + np.asarray(bg, float) * (1 - a)


CVD = dict(  # Machado, Oliveira, Fernandes 2009, severity 1.0 (linear RGB)
    protan=np.array([[0.152286, 1.052583, -0.204868], [0.114503, 0.786281, 0.099216], [-0.003882, -0.048116, 1.051998]]),
    deutan=np.array([[0.367322, 0.860646, -0.227968], [0.280085, 0.672501, 0.047413], [-0.011820, 0.042940, 0.968881]]),
    tritan=np.array([[1.255528, -0.076749, -0.178779], [-0.078411, 0.930809, 0.147602], [0.004733, 0.691367, 0.303900]]),
)


def cvd(c, kind):
    return lin_to_srgb(np.clip(srgb_to_lin(np.asarray(c, float)) @ CVD[kind].T, 0, 1))


# ---- parsers (Unity YAML read as text; the assets are never imported or written) ---------------------------------------
_XY = re.compile(r'\{x: ([-\d.eE+]+), y: ([-\d.eE+]+)\}')


def _unquote(s):
    s = s.strip()
    if len(s) >= 2 and s[0] == '"' and s[-1] == '"':
        s = s[1:-1]
        s = re.sub(r'\\u([0-9A-Fa-f]{4})', lambda m: chr(int(m.group(1), 16)), s)
        s = re.sub(r'\\x([0-9A-Fa-f]{2})', lambda m: chr(int(m.group(1), 16)), s)
        s = s.replace('\\"', '"').replace('\\\\', '\\')
        s = re.sub(r'\s*\n\s*', ' ', s)
    return s


def _pts(block):
    return np.array([(float(a), float(b)) for a, b in _XY.findall(block)], float).reshape(-1, 2)


def parse_map(path=MAP_ASSET):
    """Map.asset -> dict(lines=[{id, kind, pts, pixel_width}], markers=[{id, label, kind, x, z, ...}], zones=[...]).
    kind of a line: 0 River, 1 Road, 2 Trail, 3 DetailFill, 4 DetailOutline (WorldMapLineKind)."""
    t = Path(path).read_text(encoding='utf-8')
    i, j, k = t.find('\n  Lines:'), t.find('\n  Markers:'), t.find('\n  Zones:')
    lines = []
    for blk in t[i:j].split('\n  - Id: ')[1:]:
        lines.append(dict(id=blk.split('\n')[0].strip(), kind=int(re.search(r'Kind: (\d+)', blk).group(1)), pts=_pts(blk),
                          pixel_width=float(re.search(r'PixelWidth: ([\d.]+)', blk).group(1))))
    markers = []
    for blk in t[j:k].split('\n  - Id: ')[1:]:
        m = re.search(r'WorldXZ: \{x: ([-\d.eE+]+), y: ([-\d.eE+]+)\}', blk)
        lab = re.search(r'Label: (.*(?:\n      .*)*)', blk).group(1)
        markers.append(dict(id=blk.split('\n')[0].strip(), label=_unquote(lab), kind=int(re.search(r'Kind: (\d+)', blk).group(1)),
                            x=float(m.group(1)), z=float(m.group(2)),
                            requires_arrival=int(re.search(r'RequiresArrival: (\d)', blk).group(1)),
                            initially_discovered=int(re.search(r'InitiallyDiscovered: (\d)', blk).group(1))))
    zones = []
    for blk in t[k:].split('\n  - ExploreWalkedPassages')[1:]:
        poly = blk[blk.find('Polygon:'):blk.find('DetailPath:')]
        zones.append(dict(id=re.search(r'\n    Id: (.*)', blk).group(1).strip(), label=_unquote(re.search(r'Label: (.*)', blk).group(1)),
                          polygon=_pts(poly)))
    bmin = re.search(r'BoundsMin: \{x: ([-\d.]+), y: ([-\d.]+)\}', t); bmax = re.search(r'BoundsMax: \{x: ([-\d.]+), y: ([-\d.]+)\}', t)
    return dict(lines=lines, markers=markers, zones=zones, revision=re.search(r'Revision: (.*)', t).group(1).strip(),
                bounds=(float(bmin.group(1)), float(bmin.group(2)), float(bmax.group(1)), float(bmax.group(2))))


def layout_block(text, a, b):
    i = text.find('\n  ' + a + ':'); j = text.find('\n  ' + b + ':')
    if i < 0 or j < 0:
        raise SystemExit(f'REFUSED: layout block {a}..{b} not found')
    return text[i:j]


LAYOUT_BLOCKS = (('Routes', 'Ridges'), ('River', 'Drainages'), ('Drainages', 'Hydrology'), ('Realms', 'RiverWidth'))


def layout_block_shas(path):
    t = Path(path).read_text(encoding='utf-8')
    return {a: sha256_bytes(layout_block(t, a, b).encode('utf-8')) for a, b in LAYOUT_BLOCKS}


_ROUTE = re.compile(r'- Id: (.*)\n\s+From: .*\n\s+To: .*\n\s+Role: (\d+)\n\s+RequiredAbility: ?.*\n\s+OneWay: (\d)\n\s+GradeForVehicle: (\d)\n'
                    r'\s+Traversal: (\d)\n\s+Bends:((?:\n\s+- \{x: [-\d.eE+]+, y: [-\d.eE+]+\})*)\n\s+Width: ([\d.]+)')


def parse_layout(path=LAYOUT_MAIN, read_role=False):
    """WorldLayout -> dict(routes={id: {vehicle, foot_only, width, bends}}, drainages=[{id, pts, half_width}]).
    The story role of a route is NOT returned (Spec section 2: a class by role would draw a recommended route, #214);
    read_role=True exists only for the AC-O2 self test, which proves the bake does not depend on it."""
    t = Path(path).read_text(encoding='utf-8')
    routes = {}
    for m in _ROUTE.finditer(layout_block(t, 'Routes', 'Ridges')):
        r = dict(vehicle=int(m.group(4)), foot_only=int(m.group(5)), width=float(m.group(7)), bends=_pts(m.group(6)))
        if read_role: r['role'] = int(m.group(2))
        routes[m.group(1).strip()] = r
    drains = []
    for blk in layout_block(t, 'Drainages', 'Hydrology').split('\n  - Id: ')[1:]:
        hw = re.search(r'HalfWidth: ([\d.]+)', blk)
        drains.append(dict(id=blk.split('\n')[0].strip(), pts=_pts(blk), half_width=float(hw.group(1)) if hw else 12.0))
    return dict(routes=routes, drainages=drains)


def road_class(width, vehicle, foot_only):
    """Spec section 2: physical attributes only. 9 daero, 8 gil, 7 soro."""
    if vehicle or width >= 6: return 9
    if width >= 3 and not foot_only: return 8
    return 7


def tree_points(log=print):
    """float32 [n, 2] (x, z) of the TREES of the vegetation sheets (FixedPlacements whose PrototypeId is a tree species)."""
    rx = re.compile(r'Position: \{x: ([-0-9.eE+]+), y: ([-0-9.eE+]+), z: ([-0-9.eE+]+)\}')
    out = []; counts = {}
    for name, p in SHEETS.items():
        sec = None; keep = False; n = 0
        with open(A / p, encoding='utf-8') as f:
            for line in f:
                if line.startswith('  ') and not line.startswith('   ') and line.rstrip().endswith(':'): sec = line.strip()[:-1]
                if sec != 'FixedPlacements': continue
                if line.startswith('    PrototypeId:'):
                    keep = TREE_RX.search(line) is not None
                elif keep and line.startswith('    Position:'):
                    m = rx.search(line)
                    if m: out.append((float(m.group(1)), float(m.group(3)))); n += 1
        counts[name] = n
    log(f'  trees per sheet: {counts}')
    return np.array(out, np.float32).reshape(-1, 2), counts


TREE_RX = re.compile(r'(Pinus|Ulmus|Salix|Melia)')     # tree species of the sheets; grass / fern / rock / log / fill are not trees


def load_height(path):
    a = np.fromfile(path, '<f4')
    if a.size != HH * WW:
        raise SystemExit(f'REFUSED: {path} holds {a.size} floats, expected {HH * WW}')
    return a.reshape(HH, WW).astype(np.float64)


def load_wet():
    return np.fromfile(WET, np.uint8).reshape(HH, WW) > 0


def stage_boundary(stage):
    """the plan file whose segments[] are the cliff lines of a stage: 'boundary' of the stage entry, else boundary308.json (plan.3)."""
    return STAGES[stage].get('boundary', BOUNDARY)


def long_wall_lines(path):
    """[(id, Nx2)] of the phase-1b long wall (Jangseong308 layout): one line per wing along the AS-BUILT module joints
    (wings.<w>.built: the wall as it stands, the gate-span modules included), ending at the two faces of the gate block.
    The 13.4 m between the two wing ends is the gate block - left open, as the city walls are open at their gates; the gate
    itself is an icon on the road (Spec section 2 '문'), never a stroke."""
    j = json.loads(Path(path).read_text(encoding='utf-8'))
    return [('jangseong.' + w, np.asarray(j['wings'][w]['built'], float)) for w in ('west', 'east')]


def capital_wall_lines(cap, half=None):
    """[(id, Nx2)] of the capital loop, OPEN at its gates (map3, D308-18 #8): the corner polygon is cut `half` m to either side
    of every aperture of the layout (apertures[].centre projected on the polygon), one line per run between two gates - the same
    rule as the long wall and the palace walls, whose lines already end at their gates. A road through a gate then crosses no
    wall stroke; the gate itself is an icon (a marker), never a stroke. Without apertures the closed loop is returned."""
    half = CAPITAL_GATE_HALF_M if half is None else half
    cc = np.asarray([list(c) for c in cap['loop']['corners']] + [list(cap['loop']['corners'][0])], float)
    aps = cap.get('apertures') or []
    if not aps: return [('capital_wall', cc)]
    s = cum_len(cc); total = float(s[-1]); at = []
    for a in aps:
        q = np.array([a['centre']['x'], a['centre']['z']], float); best = (np.inf, 0.0)
        for k in range(len(cc) - 1):
            ab = cc[k + 1] - cc[k]; t = float(np.clip(np.dot(q - cc[k], ab) / np.dot(ab, ab), 0.0, 1.0)); d = float(np.hypot(*(cc[k] + t * ab - q)))
            if d < best[0]: best = (d, float(s[k] + t * (s[k + 1] - s[k])))
        if best[0] > 12.0: raise SystemExit(f"REFUSED: capital gate '{a['id']}' is {best[0]:.1f} m from the wall polygon")
        at.append((best[1], a['id']))
    at.sort()

    def cut(s0, s1):                                                    # the polygon between two arc lengths (s1 may pass the seam)
        if s1 < s0: s1 += total
        ss = [s0] + [float(v) + w for w in (0.0, total) for v in s[:-1] if s0 < float(v) + w < s1] + [s1]
        return np.array([[np.interp(v % total, s, cc[:, 0]), np.interp(v % total, s, cc[:, 1])] for v in ss], float)
    out = []
    for i, (sa, ida) in enumerate(at):
        sb, idb = at[(i + 1) % len(at)]
        out.append((f'capital_wall.{ida}__{idb}', cut((sa + half) % total, (sb - half) % total)))
    return out


def wall_lines(stage=None):
    """[(id, Nx2)] of the BUILT walls (capital loop, cheolong walls, hwanggyeong palace walls) - cliff308_base.wall_lines().
    A stage whose entry carries 'long_wall' (1b on) adds the two wings of the long wall; without a stage, or for base / 1a,
    the list is exactly the three city layouts."""
    jl = lambda p: json.loads(Path(p).read_text(encoding='utf-8'))
    cap, che, hwa = (jl(p) for p in WALL_LAYOUTS)
    out = capital_wall_lines(cap)
    out += [('cheolong.' + w['name'], np.asarray([(p[0], p[2]) for p in w['walk']], float)) for w in che['walls']]
    out += [('hwanggyeong.' + w['name'], np.asarray([w['a'], w['b']], float)) for w in hwa['wallLines']]
    if stage is not None and 'long_wall' in STAGES[stage]: out += long_wall_lines(STAGES[stage]['long_wall'])
    return out


# ---- geometry ----------------------------------------------------------------------------------------------------------
def seg_len(P):
    P = np.asarray(P, float)
    return np.hypot(*np.diff(P, axis=0).T) if len(P) > 1 else np.zeros(0)


def poly_len(P):
    return float(seg_len(P).sum())


def cum_len(P):
    return np.concatenate([[0.0], np.cumsum(seg_len(P))])


def dedupe(P, eps=1e-3):
    P = np.asarray(P, float)
    if len(P) < 2: return P
    keep = np.concatenate([[True], seg_len(P) > eps])
    return P[keep]


def dp_simplify(P, tol):
    """Douglas-Peucker (iterative). The result is a subset of the input vertices; every input vertex lies within tol."""
    P = np.asarray(P, float); n = len(P)
    if n < 3: return P.copy()
    keep = np.zeros(n, bool); keep[0] = keep[-1] = True
    stack = [(0, n - 1)]
    while stack:
        a, b = stack.pop()
        if b <= a + 1: continue
        d = dist_point_seg(P[a + 1:b], P[a], P[b])
        k = int(np.argmax(d))
        if d[k] > tol:
            keep[a + 1 + k] = True
            stack.append((a, a + 1 + k)); stack.append((a + 1 + k, b))
    return P[keep]


def dist_point_seg(Q, a, b):
    Q = np.asarray(Q, float).reshape(-1, 2); a = np.asarray(a, float); b = np.asarray(b, float)
    d = b - a; L2 = float(d @ d)
    if L2 < 1e-12: return np.hypot(*(Q - a).T)
    t = np.clip(((Q - a) @ d) / L2, 0, 1)
    return np.hypot(*(Q - (a + t[:, None] * d)).T)


def dist_to_polyline(Q, P):
    """min distance of every point of Q to polyline P (brute force over segments; fine for a few thousand points)."""
    Q = np.asarray(Q, float).reshape(-1, 2); P = np.asarray(P, float)
    if len(P) == 1: return np.hypot(*(Q - P[0]).T)
    best = np.full(len(Q), np.inf)
    for i in range(len(P) - 1):
        best = np.minimum(best, dist_point_seg(Q, P[i], P[i + 1]))
    return best


def resample(P, step):
    """uniform resample with spacing <= step (both ends kept)."""
    P = np.asarray(P, float); s = cum_len(P)
    if s[-1] < 1e-6: return P[:1].copy()
    n = max(2, int(math.ceil(s[-1] / step)) + 1); t = np.linspace(0, s[-1], n)
    return np.stack([np.interp(t, s, P[:, 0]), np.interp(t, s, P[:, 1])], 1)


def densify(P, max_seg):
    """insert points on segments longer than max_seg (the new points lie ON the polyline)."""
    P = np.asarray(P, float); out = [P[0]]
    for a, b in zip(P[:-1], P[1:]):
        L = math.hypot(*(b - a)); n = max(1, int(math.ceil(L / max_seg)))
        for k in range(1, n + 1): out.append(a + (b - a) * (k / n))
    return np.array(out)


def chaikin(P, iters=2):
    P = np.asarray(P, float)
    for _ in range(iters):
        if len(P) < 3: break
        Q = np.empty((2 * (len(P) - 1), 2)); Q[0::2] = .75 * P[:-1] + .25 * P[1:]; Q[1::2] = .25 * P[:-1] + .75 * P[1:]
        P = np.concatenate([P[:1], Q, P[-1:]])
    return P


def smooth_poly(P, win=5):
    """centred moving average with pinned ends."""
    P = np.asarray(P, float); n = len(P)
    if n < win: return P.copy()
    k = win // 2; pad = np.concatenate([np.repeat(P[:1], k, 0), P, np.repeat(P[-1:], k, 0)])
    c = np.cumsum(np.concatenate([np.zeros((1, 2)), pad]), axis=0); out = (c[win:] - c[:-win]) / win
    out[0] = P[0]; out[-1] = P[-1]
    return out


def normals(P):
    """unit LEFT normals per point (left of the direction of travel, x east / z north seen from above)."""
    P = np.asarray(P, float)
    t = np.gradient(P, axis=0) if len(P) > 2 else np.repeat((P[-1] - P[0])[None], len(P), 0)
    L = np.maximum(np.hypot(t[:, 0], t[:, 1]), 1e-9); t = t / L[:, None]
    return np.stack([-t[:, 1], t[:, 0]], 1)


def sample_grid(g, x, z):
    """bilinear sample of a node lattice [HH, WW] at world (x, z)."""
    x = np.clip(np.asarray(x, float), 0, WORLD_X - 1e-3); z = np.clip(np.asarray(z, float), 0, WORLD_Z - 1e-3)
    j = x / CELL; i = z / CELL; j0 = np.floor(j).astype(int); i0 = np.floor(i).astype(int); fj = j - j0; fi = i - i0
    return g[i0, j0] * (1 - fj) * (1 - fi) + g[i0, j0 + 1] * fj * (1 - fi) + g[i0 + 1, j0] * (1 - fj) * fi + g[i0 + 1, j0 + 1] * fj * fi


# ---- grids -------------------------------------------------------------------------------------------------------------
def box(a, r):
    """separable box mean of radius r cells (edge padded)."""
    if r <= 0: return np.asarray(a, float)
    k = 2 * r + 1
    p = np.pad(a, ((0, 0), (r, r)), mode='edge'); c = np.cumsum(np.pad(p, ((0, 0), (1, 0))), axis=1); a = (c[:, k:] - c[:, :-k]) / k
    p = np.pad(a, ((r, r), (0, 0)), mode='edge'); c = np.cumsum(np.pad(p, ((1, 0), (0, 0))), axis=0); return (c[k:, :] - c[:-k, :]) / k


def box_wrap(a, r):
    """box mean with wrap-around (tileable textures)."""
    out = np.asarray(a, float)
    for ax in (0, 1):
        acc = np.zeros_like(out)
        for d in range(-r, r + 1): acc += np.roll(out, d, axis=ax)
        out = acc / (2 * r + 1)
    return out


def near_dist(mask, rmax):
    """distance (cells) from every cell to the nearest True cell, exact up to rmax, rmax + 1 beyond. Brute force over offsets."""
    H, W = mask.shape; R = int(math.ceil(rmax))
    d = np.full((H, W), float(rmax + 1)); d[mask] = 0.0
    offs = sorted(((math.hypot(dx, dy), dx, dy) for dx in range(-R, R + 1) for dy in range(-R, R + 1) if 0 < math.hypot(dx, dy) <= rmax))
    pad = np.pad(mask, R, mode='constant')
    for dist, dx, dy in offs:
        sh = pad[R + dy:R + dy + H, R + dx:R + dx + W]
        np.minimum(d, np.where(sh, dist, np.inf), out=d)
    return d


def dilate(mask, r):
    return near_dist(mask, r) <= r


def tnoise(h, w, period, seed, octaves=1):
    """tileable value noise in [0, 1] on an (h, w) texture: a random lattice of `period` texels, smooth interpolation, wrap."""
    out = np.zeros((h, w)); amp = 1.0; tot = 0.0
    for o in range(octaves):
        p = max(2, int(round(period / (2 ** o))))
        gh, gw = max(1, h // p), max(1, w // p)
        g = np.random.default_rng([SEED, seed, o]).random((gh, gw))
        y = np.arange(h) / h * gh; x = np.arange(w) / w * gw
        y0 = np.floor(y).astype(int); x0 = np.floor(x).astype(int); fy = y - y0; fx = x - x0
        fy = fy * fy * (3 - 2 * fy); fx = fx * fx * (3 - 2 * fx)
        y1 = (y0 + 1) % gh; x1 = (x0 + 1) % gw; y0 %= gh; x0 %= gw
        v = (g[np.ix_(y0, x0)] * (1 - fx)[None] + g[np.ix_(y0, x1)] * fx[None]) * (1 - fy)[:, None] + \
            (g[np.ix_(y1, x0)] * (1 - fx)[None] + g[np.ix_(y1, x1)] * fx[None]) * fy[:, None]
        out += v * amp; tot += amp; amp *= .5
    return out / tot


def down(a, ss):
    """box downsample by an integer factor (last two spatial axes first)."""
    H, W = a.shape[0] // ss, a.shape[1] // ss
    return a[:H * ss, :W * ss].reshape(H, ss, W, ss, *a.shape[2:]).mean(axis=(1, 3))


# ---- brush rasteriser (coverage, supersampled by the caller) -----------------------------------------------------------
class Canvas:
    """float coverage canvas drawn with PIL at ss x supersampling; wrap=True draws every primitive at the 9 torus copies."""

    def __init__(self, w, h, ss=4, wrap=False):
        self.w, self.h, self.ss, self.wrap = w, h, ss, wrap
        self.im = Image.new('L', (w * ss, h * ss), 0); self.d = ImageDraw.Draw(self.im)

    def _offs(self):
        if not self.wrap: return ((0, 0),)
        return tuple((ox * self.w, oy * self.h) for ox in (-1, 0, 1) for oy in (-1, 0, 1))

    def disc(self, x, y, r, fill=255):
        k = self.ss
        for ox, oy in self._offs():
            self.d.ellipse([(x + ox - r) * k, (y + oy - r) * k, (x + ox + r) * k, (y + oy + r) * k], fill=fill)

    def poly(self, pts, fill=255):
        k = self.ss
        for ox, oy in self._offs():
            self.d.polygon([((x + ox) * k, (y + oy) * k) for x, y in pts], fill=fill)

    def stroke(self, pts, width, w_fn=None, fill=255, step=.35):
        """brush stroke = discs along the polyline; width (px) x w_fn(t in 0..1) is the local diameter."""
        P = np.asarray(pts, float); s = cum_len(P)
        if s[-1] < 1e-6:
            self.disc(P[0, 0], P[0, 1], width / 2 * (w_fn(0.0) if w_fn else 1.0), fill); return
        n = max(2, int(s[-1] / step) + 1)
        for t in np.linspace(0, s[-1], n):
            x = float(np.interp(t, s, P[:, 0])); y = float(np.interp(t, s, P[:, 1]))
            self.disc(x, y, width / 2 * (w_fn(t / s[-1]) if w_fn else 1.0), fill)

    def array(self):
        return np.asarray(self.im, float) / 255.0

    def result(self):
        return down(self.array(), self.ss)


def arc(cx, cy, r, a0, a1, n=24, ry=None):
    """points on an ellipse arc, degrees, screen coordinates (y down): 0 = east, 90 = south."""
    ry = r if ry is None else ry
    return [(cx + r * math.cos(math.radians(a)), cy + ry * math.sin(math.radians(a))) for a in np.linspace(a0, a1, n)]
