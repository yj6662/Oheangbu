#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""D308-16 relayout FIX (F1-F12) - offline dry run. Read only unless a command says --write.

  python Tools/Art/relayout_fix308_dry.py dry [--scene all|arch296|folk298|main] [--mutate <name>] [--out <dir>] [--deployed]
        predicts every post-condition of the fix for the three scenes and exits 2 when a number is wrong
  python Tools/Art/relayout_fix308_dry.py mutations [--scene main]
        proves the dry can fail: every mutation of MUTATIONS must turn at least one line red (exit 2 when one does not)
  python Tools/Art/relayout_fix308_dry.py stills
        the Presentation297 shot lines of the fix stills (F11), eye heights from height_p1b
  python Tools/Art/relayout_fix308_dry.py grass-unlock [--write]
        F9 (a): contentseat308.json grass.apply_enabled false -> true in the DEPLOYED file (after the read-only diag said yes)
  python Tools/Art/relayout_fix308_dry.py f9-fallback [--write]
        F9 (b): moves the woodcutter + pile numbers of the DEPLOYED data to the nearest bare ground (when the diag said no)

What it reads: the fix stage's data (Tools/Unity/Stage308_relayout_fix/Data) - or the deployed copies once they are equal -,
the live scene YAML / content / layout assets, the offline height field height_p1b, mesh assets and binary FBX files.
Everything is OFFLINE ([O]); the editor's physics decides (scene-check, stops:verify, verify D8, paths-check, Jangseong308 check).
Lines: PASS / FAIL / INFO / OPEN (OPEN = known not fixed, named with its reason; does not fail the run).
"""
import argparse, copy, datetime, hashlib, json, math, os, re, shutil, struct, sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import buildingaudit308_offline as B
import contentseat308_dry as S
import content308_relayout_dry as R
import meshbounds308 as MB

ROOT = Path(__file__).resolve().parents[2]
PROJECT = ROOT / 'Oheangbu'
CB = ROOT / 'Art/World/Compact/Rebuild/CliffBoundary308'
FIX = ROOT / 'Tools/Unity/Stage308_relayout_fix'
ORDER = ['arch296', 'folk298', 'main']
DATA = {'relayout': 'content308_relayout.json', 'scene': 'content308_scene.json', 'seat': 'contentseat308.json', 'smallops': 'smallops308.cfg.json', 'fix': 'relayout_fix308.json'}
LIVE = {'relayout': CB / 'content308_relayout.json', 'scene': CB / 'content308_scene.json', 'seat': CB / 'contentseat308.json', 'smallops': CB / 'small_1b/smallops308.cfg.json'}


def sha(p): return hashlib.sha256(Path(p).read_bytes()).hexdigest() if Path(p).is_file() else ''
def d2(a, b): return math.hypot(a[0] - b[0], a[1] - b[1])
def f2(v): return '%.2f' % v
def f3(v): return '%.3f' % v


class Rep:
    def __init__(self, alias): self.alias = alias; self.lines = []; self.fail = 0; self.passed = 0; self.open = 0; self.nums = {}
    def head(self, s): self.lines.append(''); self.lines.append('== ' + s)
    def info(self, s): self.lines.append('INFO ' + s)
    def opened(self, s): self.open += 1; self.lines.append('OPEN ' + s)
    def c(self, ok, s, num=None):
        if ok: self.passed += 1
        else: self.fail += 1
        self.lines.append(('PASS ' if ok else 'FAIL ') + s)
        if num is not None: self.nums[num[0]] = num[1]
        return ok


# ---------------------------------------------------------------------------------------------- data

def load_data(data_dir=None, deployed=False):
    d = {}; src = {}
    for k, name in DATA.items():
        f = (Path(data_dir) if data_dir else FIX / 'Data') / name
        if deployed and k in LIVE: f = LIVE[k]   # the DEPLOYED copies (after fix_copy.py --apply; they differ from the stage once grass-unlock / f9-fallback --write edited them)
        d[k] = json.loads(f.read_text(encoding='utf-8')); src[k] = f
    d['_src'] = src
    orig = FIX / 'Original/content308_scene.json.orig'
    d['scene_orig'] = json.loads(orig.read_text(encoding='utf-8')) if orig.exists() else None
    fd = d['fix']
    ext = ROOT / fd['ext_stage'] / 'Data'
    def ext_or_live(name):
        for f in (CB / name, ext / name):
            if f.exists(): return json.loads(f.read_text(encoding='utf-8')), f
        return None, None
    d['earth'], src['earth'] = ext_or_live('earth308.json')
    d['roadinn'], src['roadinn'] = ext_or_live('roadinn308.json')
    return d


def mesh_asset(path):
    """(vertices, indices) of a YAML mesh asset (positions = first 12 bytes of each vertex of stream 0)."""
    t = Path(path).read_text(encoding='utf-8', errors='replace')
    vc = int(re.search(r'm_VertexCount: (\d+)', t).group(1))
    data = bytes.fromhex(re.search(r'_typelessdata: ([0-9a-f]+)', t).group(1)); idx = bytes.fromhex(re.search(r'm_IndexBuffer: ([0-9a-f]+)', t).group(1))
    fmt = int(re.search(r'm_IndexFormat: (\d)', t).group(1)); stride = len(data) // vc
    V = [struct.unpack_from('<3f', data, i * stride) for i in range(vc)]
    I = struct.unpack('<%d%s' % (len(idx) // (4 if fmt == 1 else 2), 'I' if fmt == 1 else 'H'), idx)
    return V, I


def meta_guid(asset):
    m = re.search(r'^guid: ([0-9a-f]{32})', (PROJECT / (asset + '.meta')).read_text(encoding='utf-8', errors='replace'), flags=re.M)
    return m.group(1) if m else ''


def pack_guid_map(pack_dir):
    """guid -> asset path for one pack folder (fast: a few hundred .meta files)."""
    m = {}
    for dp, dn, fn in os.walk(PROJECT / pack_dir):
        for f in fn:
            if not f.endswith('.meta'): continue
            p = os.path.join(dp, f)
            try:
                with open(p, 'rb') as fh: head = fh.read(160)
            except OSError: continue
            i = head.find(b'guid: ')
            if i >= 0: m[head[i + 6:i + 38].decode()] = os.path.relpath(p, PROJECT)[:-5].replace('\\', '/')
    return m


_PREFAB = {}


def prefab_box(prefab, fd):
    """(lo, hi, notes) of a pack prefab, its meshes found inside its own pack folder."""
    if prefab in _PREFAB: return _PREFAB[prefab]
    pack = next((v for k, v in fd['f2']['pack_dirs'].items() if prefab.startswith(k)), None)
    if pack is None: _PREFAB[prefab] = (None, None, ['no pack_dirs entry for %s (relayout_fix308.json f2.pack_dirs)' % prefab]); return _PREFAB[prefab]
    MB._guid = pack_guid_map(pack)
    _PREFAB[prefab] = MB.prefab_box(prefab); return _PREFAB[prefab]


def catalogue_size(prefab, fd):
    f = ROOT / fd['f2']['catalogue']
    if not f.exists(): return None
    t = f.read_text(encoding='utf-8', errors='replace'); i = t.find('"path":"%s"' % prefab)
    if i < 0: return None
    m = re.search(r'"size":\[([-\d.eE+]+),([-\d.eE+]+),([-\d.eE+]+)\]', t[i:i + 3000])
    return tuple(float(x) for x in m.groups()) if m else None


def rest_rect(yaw, origin, boxes):
    """XZ rect (x0, x1, z0, z1) in the rest frame of world boxes given as (pos, yaw, scale, lo, hi)."""
    x0 = z0 = float('inf'); x1 = z1 = float('-inf')
    for (pos, byaw, scl, lo, hi) in boxes:
        for i in range(8):
            v = tuple((hi if (i >> a) & 1 else lo)[a] * scl[a] for a in range(3))
            wx, wz = S.fwd(byaw, v[0], v[2]); wx += pos[0]; wz += pos[2]
            lx, lz = S.inv(yaw, wx - origin[0], wz - origin[1])
            x0 = min(x0, lx); x1 = max(x1, lx); z0 = min(z0, lz); z1 = max(z1, lz)
    return (x0, x1, z0, z1)


def rect_gap(a, b):
    gx = max(b[0] - a[1], a[0] - b[1]); gz = max(b[2] - a[3], a[2] - b[3])
    return math.hypot(gx, gz) if gx > 0 and gz > 0 else max(gx, gz)


_KEEP_MESH = {}


def keep_bounds(sk, stop_key, keep, fd):
    """[(kept child, lo, hi, source)]: world AABB of every collider under the kept children of a stop - box colliders from the scene
    YAML, mesh colliders from the mesh asset of the same object (guid looked up in f5.keep_mesh_dirs)."""
    sc = sk.sc; out = []; t = sk.get(stop_key)
    if t is None: return out
    for c in sc.children_of(t):
        name = sc.nodes[c].name
        if name not in keep: continue
        bb = sk.box_bounds(sk.key_of(c))
        if bb: out.append((name, bb[0], bb[1], 'box colliders'))
        for x in sc.subtree(c):
            key = sk.key_of(x)
            if not sk.comp(key, 64): continue
            for info in sk.comp(key, 33):
                if not info.get('mesh') or not info['mesh'][1]: continue
                if not _KEEP_MESH:
                    for d_ in fd['f5'].get('keep_mesh_dirs', []): _KEEP_MESH.update(pack_guid_map(d_))
                path = _KEEP_MESH.get(info['mesh'][1])
                box = MB.asset_box(PROJECT / path) if path else None
                if not box: continue
                p, q, s = sc.world(x); lo = [float('inf')] * 3; hi = [float('-inf')] * 3
                for i in range(8):
                    v = tuple((box[1] if (i >> a) & 1 else box[0])[a] * s[a] for a in range(3)); wv = B.qrot(q, v)
                    for a in range(3): lo[a] = min(lo[a], p[a] + wv[a]); hi[a] = max(hi[a], p[a] + wv[a])
                out.append((name, tuple(lo), tuple(hi), 'mesh collider ' + sc.nodes[x].name))
    return out


def bilinear(Hn, x, z):
    """CompactWorldSurface.Sample (the editor's height field sampler)."""
    rows, cols = Hn.shape; fx = min(max(x / S.H_CELL, 0), cols - 1); fz = min(max(z / S.H_CELL, 0), rows - 1)
    ix = min(int(fx), cols - 2); iz = min(int(fz), rows - 2); u = fx - ix; v = fz - iz
    a = Hn[iz, ix] + (Hn[iz, ix + 1] - Hn[iz, ix]) * u; b = Hn[iz + 1, ix] + (Hn[iz + 1, ix + 1] - Hn[iz + 1, ix]) * u
    return float(a + (b - a) * v)


def in_ring(p, ring):
    inside = False; j = len(ring) - 1
    for i in range(len(ring)):
        xi, zi = ring[i]; xj, zj = ring[j]
        if (zi > p[1]) != (zj > p[1]) and p[0] < (xj - xi) * (p[1] - zi) / (zj - zi) + xi: inside = not inside
        j = i
    return inside


def build_path(w, legs, step, dedupe):
    """Content308.BuildPath308 XZ: legs in order, each resampled, a repeated joint dropped."""
    xz = []
    for leg in legs:
        poly = list(w.routes[leg['route']])
        if leg.get('reverse'): poly.reverse()
        if leg.get('tail_m') is not None: poly = R.tail(poly, leg['tail_m'])
        for p in R.resample(poly, step):
            if xz and d2(xz[-1], p) < dedupe: continue
            xz.append(p)
    return xz


# ---------------------------------------------------------------------------------------------- the dry run of one scene

def dry_scene(alias, D, grass=None):
    rel = D['relayout']; sd = D['scene']; seat = D['seat']; so = D['smallops']; fd = D['fix']; rep = Rep(alias)
    w = R.World(alias, rel, ROOT / fd['height']); H = w.H; sk = w.sk; sc = sk.sc
    content = R.load_asset(PROJECT / S.CONTENT[alias])
    pts = {p['Id']: p for p in content['Points']}; cps = {c['Id']: c for c in content['Checkpoints']}; enc = {e['Id']: e for e in content['Encounters']}
    rep.lines.append('relayout FIX dry  %s  scene sha %s  content sha %s  height %s sha %s' % (alias, sk.sha[:12], S.sha(PROJECT / S.CONTENT[alias])[:12], Path(fd['height']).name, H.sha[:12]))
    for k in ('relayout', 'scene', 'seat', 'smallops', 'fix'): rep.lines.append('data %-9s %s sha %s (%s)' % (k, D['_src'][k], sha(D['_src'][k])[:12], D[k].get('version')))
    off = set(rel.get('groups_off') or [])
    def on(row): return row.get('enabled', True) is not False and (row.get('group') or '') not in off
    ck = sd['checks']

    # ---------------------------------------------------------------- F1 bench beside the altar
    rep.head('F1  the escort rest bench beside the altar (rests_escort[].node)')
    f1 = fd['f1']; altar_boxes = []
    for a in f1['altar_meshes']:
        box = MB.asset_box(PROJECT / a)
        if box: altar_boxes.append((a, box))
    stops = {'road_rest_1': 'checkpoint_1', 'road_rest_2': 'checkpoint_2'}
    for r in rel.get('rests_escort', []):
        if not on(r): rep.info('%s: row off' % r['id']); continue
        rid = r['id']; P = (r['x'], r['z']); F = (r['feet']['x'], r['feet']['z']); yaw = R.yaw_to(P, F); node = r['node']; offx, offz = node['offset']
        akey = 'Content308/Rests/%s/Altar' % rid; aw = sk.world(akey)
        if aw is None: rep.c(False, 'F1 %s: the altar %s is not in this scene (the base relayout is not applied?)' % (rid, akey)); continue
        rep.c(d2((aw[0][0], aw[0][2]), P) <= 0.05 and abs((aw[1] - yaw + 180) % 360 - 180) <= f1['altar_yaw_tol_deg'], 'F1 %s: the altar stands on the row\'s point with the rest yaw (%.2f m off, yaw %.2f vs rest %.2f)' % (rid, d2((aw[0][0], aw[0][2]), P), aw[1], yaw))
        # the meshes the altar really carries are the ones measured
        guids = set()
        for k2 in sk.subtree_keys(akey):
            for info in sk.comp(k2, 33):
                if info.get('mesh') and info['mesh'][1]: guids.add(info['mesh'][1])
        known = {meta_guid(a): a for a, _ in altar_boxes}
        rep.c(guids and guids <= set(known), 'F1 %s: the altar\'s %d mesh(es) are the measured RestAltar LOD meshes' % (rid, len(guids)))
        altar = rest_rect(yaw, P, [((P[0], 0, P[1]), yaw, (1, 1, 1), lo, hi) for _, (lo, hi) in altar_boxes])
        bkey = 'CapitalEscort252/%s/%s' % (stops.get(rid, '?'), rid); bw = sk.world(bkey)
        if bw is None: rep.c(False, 'F1 %s: bench node %s missing' % (rid, bkey)); continue
        scl = bw[3]; h = f1['bench_cube_half']; dx, dz = S.fwd(yaw, offx, offz); bpos = (P[0] + dx, P[1] + dz)
        byaw = (yaw + node['yaw']) % 360 if 'yaw' in node else bw[1]
        bench = rest_rect(yaw, P, [((bpos[0], 0, bpos[1]), byaw, scl, (-h, -h, -h), (h, h, h))])
        gap = rect_gap(altar, bench); need = f1['clear_min_m']
        rep.c(node.get('clear_min_m') == need, 'F1 %s: the row carries the decision limit node.clear_min_m %s (decision F1: %.2f; the data may not loosen it)' % (rid, node.get('clear_min_m'), need))
        rep.c(gap >= need, 'F1 %s: bench %.2f m clear of the altar bounds (>= %.2f; rest frame: altar x %.2f .. %.2f z %.2f .. %.2f, bench x %.2f .. %.2f z %.2f .. %.2f; bench size %.2f x %.2f x %.2f)' % (
            rid, gap, need, altar[0], altar[1], altar[2], altar[3], bench[0], bench[1], bench[2], bench[3], scl[0], scl[1], scl[2]), ('F1.%s.gap' % rid, round(gap, 3)))
        candles = []
        for k2 in sk.subtree_keys(akey):
            if sk.comp(k2, 108):
                cp_ = sk.world(k2)[0]; candles.append(S.inv(yaw, cp_[0] - P[0], cp_[2] - P[1])[1])
        rep.c(len(candles) == 2 and bench[3] <= min(candles), 'F1 %s: bench front edge z %.2f is not in front of the candle line z %.2f (%d candles; +Z = toward the feet)' % (rid, bench[3], min(candles) if candles else float('nan'), len(candles)))
        # the side away from the approach: farther from the road than its mirror, and on the far side of the stop
        mx, mz = S.fwd(yaw, -offx, offz); mirror = (P[0] + mx, P[1] + mz); rd, rid_road, _ = w.road(bpos); rm = w.road(mirror)[0]
        park = sk.world('CapitalEscort252/%s/Parking' % stops.get(rid, '?'))
        far_stop = park is None or d2(bpos, (park[0][0], park[0][2])) >= d2(mirror, (park[0][0], park[0][2]))
        rep.c(offx != 0 and rd >= rm and far_stop, 'F1 %s: the bench side is the one away from the approach (road centre %.2f m vs %.2f m on the other side; farther from the stop\'s Parking: %s)' % (rid, rd, rm, far_stop))
        rep.c(abs(node['lift_m'] - 0.5 * scl[1]) <= f1['bench_bottom_tol_m'], 'F1 %s: lift %.2f m = half the bench height %.2f (its bottom on the ground under its centre)' % (rid, node['lift_m'], scl[1]))
        pr = float(pts[rid]['Radius']) if rid in pts else 2.0
        rep.info('F1 %s: bench centre %s, ground %.2f, slope %.1f deg, %.2f m from the content point (radius %.1f), %.2f m from the respawn feet; now at (%.2f, %.2f, %.2f) yaw %.1f' % (
            rid, R.xz(bpos), H(*bpos), w.slope(*bpos), d2(bpos, P), pr, d2(bpos, F), bw[0][0], bw[0][1], bw[0][2], bw[1]))
        rep.c(d2(bpos, F) >= 0.9 + 0.3 + h * max(scl[0], scl[2]), 'F1 %s: the respawn feet stay clear of the bench (%.2f m to its centre)' % (rid, d2(bpos, F)))

    # ---------------------------------------------------------------- F2 foundation stones
    rep.head('F2  the 여막 터 foundation stones (props rows with top_max_m)')
    f2d = fd['f2']; props = {p['id']: p for p in sd['props']}
    anchor = {r['id']: (r['x'], r['z']) for r in rel.get('points', []) if on(r)}
    stones = [p for p in sd['props'] if 'top_max_m' in p]
    rep.c(len([p for p in stones if p.get('enabled')]) == 3, 'F2 three stone rows are on (%s)' % ', '.join(p['id'] for p in stones if p.get('enabled')))
    for p in sd['props']:
        if p['id'].startswith('road_yeomak308_base_'):
            there = sk.get('Content308/Props/' + p['id']) is not None
            rep.c(not p.get('enabled') and bool(p.get('group')), 'F2 %s (the 2.68 m pillar): its row is off and carries a group, so scene-apply takes the placed object out (in the scene now: %s)' % (p['id'], there))
    for p in stones:
        if not p.get('enabled'): continue
        pid = p['id']; src = p.get('source_asset', '')
        rep.c(p['top_max_m'] == f2d['top_max_m'] and p.get('footprint_m') == f2d['footprint_m'], 'F2 %s: the row carries the decision limits (top_max_m %s, footprint_m %s)' % (pid, p['top_max_m'], p.get('footprint_m')))
        rep.c(not any(x in src for x in f2d['protected']) and (PROJECT / src).is_file(), 'F2 %s: source %s exists and is outside the protected trees' % (pid, src))
        lo, hi, notes = prefab_box(src, fd)
        if lo is None: rep.c(False, 'F2 %s: the prefab box could not be measured (%s)' % (pid, '; '.join(notes))); continue
        size = tuple(hi[a] - lo[a] for a in range(3)); cat = catalogue_size(src, fd)
        rep.c(cat is not None and all(abs(size[a] - cat[a]) <= f2d['catalogue_tol_m'] for a in range(3)), 'F2 %s: prefab box %.3f x %.3f x %.3f m [O FBX] = the asset catalogue size %s' % (pid, size[0], size[1], size[2], ('%.3f x %.3f x %.3f' % cat) if cat else 'MISSING'))
        ptext = (PROJECT / src).read_text(encoding='utf-8', errors='replace')
        rep.c('--- !u!108 ' not in ptext, 'F2 %s: the prefab has no Light component (발광 상한)' % pid)
        A = anchor.get(p['anchor']) or ((float(pts[p['anchor']]['Position']['x']), float(pts[p['anchor']]['Position']['z'])) if p['anchor'] in pts else None)
        if A is None: rep.c(False, 'F2 %s: anchor %s unknown' % (pid, p['anchor'])); continue
        at = (A[0] + p['offset'][0], A[1] + p['offset'][1]); sclp = p.get('scale', 1.0); yawp = p['yaw'] % 360
        xs = []; zs = []
        for lx in (lo[0], hi[0]):
            for lz in (lo[2], hi[2]):
                dx, dz = S.fwd(yawp, lx * sclp, lz * sclp); xs.append(at[0] + dx); zs.append(at[1] + dz)
        low = min([H(x, z) for x in (min(xs), max(xs)) for z in (min(zs), max(zs))] + [H((min(xs) + max(xs)) / 2, (min(zs) + max(zs)) / 2)])
        top = H(*at) + p['y_offset'] + hi[1] * sclp - low
        rep.c(top <= p['top_max_m'], 'F2 %s: top %.3f m over the lowest ground under it (<= %.2f; at %s ground %.2f, y_offset %.2f, box top %.3f, slope %.1f deg)' % (pid, top, p['top_max_m'], R.xz(at), H(*at), p['y_offset'], hi[1] * sclp, w.slope(*at)), ('F2.%s.top' % pid, round(top, 3)))
        sx, sz = size[0] * sclp, size[2] * sclp
        rep.c(min(sx, sz) >= p['footprint_m'][0] and max(sx, sz) <= p['footprint_m'][1], 'F2 %s: footprint %.3f x %.3f m (each side %.1f .. %.1f)' % (pid, sx, sz, p['footprint_m'][0], p['footprint_m'][1]))
        rep.c(p['y_offset'] + hi[1] * sclp >= 0.10 and -p['y_offset'] <= 0.5 * size[1] * sclp, 'F2 %s: %.2f m of the stone shows over its pivot ground and at most half of it is sunk (sunk %.2f of %.3f)' % (pid, p['y_offset'] + hi[1] * sclp, -p['y_offset'], size[1] * sclp))
        rep.c(abs(p['y_offset']) <= ck['prop_pivot_gap_max_m'] + abs(p['y_offset']), 'F2 %s: pivot gap = 0 by construction (ground + y_offset; scene-check |gap| <= %.2f)' % (pid, ck['prop_pivot_gap_max_m']))
        rd = w.road(at)[0]
        rep.c(rd >= sd['ground']['corridor_clear_m'], 'F2 %s: %.1f m from the nearest route centre (>= %.1f, solid row)' % (pid, rd, sd['ground']['corridor_clear_m']))

    # ---------------------------------------------------------------- F3 the hut + the moved point
    rep.head('F3  the 여막 터 group (point M14 + hut + stones) and its neighbours')
    f3 = fd['f3']; hut = props.get('road_yeomak308_hut'); hbox = MB.asset_box(PROJECT / f3['hut_mesh'])
    M = anchor.get('road_yeomak308'); old = tuple(f3['old_point_xz'])
    if hut is None or hbox is None or M is None: rep.c(False, 'F3 hut row / hut mesh / points[road_yeomak308] missing')
    else:
        lo, hi = hbox
        srcw = sk.world(f3['hut_source_key'])
        rep.c(srcw is not None and abs(srcw[2][0]) < 1e-4 and abs(srcw[2][2]) < 1e-4 and all(abs(v - 1) < 1e-3 for v in srcw[3]), 'F3 the hut source %s is upright and unscaled (so the row\'s yaw is the clone\'s world yaw)' % f3['hut_source_key'])
        hk = 'Content308/Props/road_yeomak308_hut'; guids = {i['mesh'][1] for k2 in sk.subtree_keys(hk) for i in sk.comp(k2, 33) if i.get('mesh')}
        rep.c(guids == {meta_guid(f3['hut_mesh'])}, 'F3 the placed hut carries the measured mesh %s' % Path(f3['hut_mesh']).name)
        rep.c(hut['corner_gap_m'] == f3['corner_gap_m'] and (D['scene_orig'] is None or next(p for p in D['scene_orig']['props'] if p['id'] == 'road_yeomak308_hut')['corner_gap_m'] == hut['corner_gap_m']),
              'F3 the corner-gap range is UNCHANGED: %s (first apply: %s)' % (hut['corner_gap_m'], f3['corner_gap_m']))

        def corner_gaps(px, pz, yaw, yoff):
            xs = []; zs = []
            for lx in (lo[0], hi[0]):
                for lz in (lo[2], hi[2]):
                    dx, dz = S.fwd(yaw, lx, lz); xs.append(px + dx); zs.append(pz + dz)
            base = H(px, pz) + yoff + lo[1]; g = [base - H(x, z) for x in (min(xs), max(xs)) for z in (min(zs), max(zs))]
            return min(g), max(g), (max(xs) - min(xs), max(zs) - min(zs))
        at = (M[0] + hut['offset'][0], M[1] + hut['offset'][1]); glo, ghi, bsz = corner_gaps(at[0], at[1], hut['yaw'], hut['y_offset']); lim = hut['corner_gap_m']
        rep.c(glo >= lim[0] + f3['corner_margin_min_m'] and ghi <= lim[1] - f3['corner_margin_min_m'], 'F3 hut at %s yaw %.1f y_offset %.2f: bounds bottom - ground at the four bounds corners %.2f .. %.2f m (allowed %.2f .. %.2f; bounds %.2f x %.2f m; margins %.2f / %.2f)' % (
            R.xz(at), hut['yaw'], hut['y_offset'], glo, ghi, lim[0], lim[1], bsz[0], bsz[1], glo - lim[0], lim[1] - ghi), ('F3.hut.gap', (round(glo, 3), round(ghi, 3))))
        ob = corner_gaps(old[0] + 2.5, old[1] + 2.5, 312.6, 0.0)
        rep.info('F3 reproduction of the first apply: hut at (%.1f, %.1f) yaw 312.6 y_offset 0 -> %.2f .. %.2f m, bounds %.2f x %.2f (editor [M]: -0.86 .. +0.18, 5.7 x 5.7)' % (old[0] + 2.5, old[1] + 2.5, ob[0], ob[1], ob[2][0], ob[2][1]))
        # review S1: the hut has no collider - its box must stay outside the detection ring of the enemy it announces
        hec = f3['hut_enemy_clear']; foe = enc.get(hec['encounter']); hb_ = (min(at[0] + S.fwd(hut['yaw'], lx, lz)[0] for lx in (lo[0], hi[0]) for lz in (lo[2], hi[2])), max(at[0] + S.fwd(hut['yaw'], lx, lz)[0] for lx in (lo[0], hi[0]) for lz in (lo[2], hi[2])),
                                                                   min(at[1] + S.fwd(hut['yaw'], lx, lz)[1] for lx in (lo[0], hi[0]) for lz in (lo[2], hi[2])), max(at[1] + S.fwd(hut['yaw'], lx, lz)[1] for lx in (lo[0], hi[0]) for lz in (lo[2], hi[2])))
        rep.c((hut.get('enemy_clear') or {}).get('encounter') == hec['encounter'], 'F3 the hut row carries enemy_clear {encounter %s} (scene-check F3 judges the placed renderer bounds in the editor)' % hec['encounter'])
        A_ = (float(foe['Feet']['x']), float(foe['Feet']['z'])) if foe else tuple(hec['feet_xz']); det = float(foe['Detection']) if foe else 16.0
        hd = math.hypot(max(hb_[0] - A_[0], 0, A_[0] - hb_[1]), max(hb_[2] - A_[1], 0, A_[1] - hb_[3]))
        rep.c(hd >= det, 'F3 hut box (x %.2f .. %.2f, z %.2f .. %.2f) to %s %.1f m (>= its detection %.0f%s; the first place of this fix read 14.4 m)' % (hb_[0], hb_[1], hb_[2], hb_[3], hec['encounter'], hd, det, '' if foe else '; the encounter is not in this scene\'s content - judged against the data feet'), ('F3.hut.enemy', round(hd, 2)))
        ring = d2(M, A_) - 2.5 - det
        rep.info('F3 point ring (r 2.5) to the detection ring of %s: %+.1f m (the first apply: %+.1f)' % (hec['encounter'], ring, d2(old, A_) - 2.5 - det))
        tr = min(w.trunk(q) for q in [(x, z) for x in (hb_[0], (hb_[0] + hb_[1]) / 2, hb_[1]) for z in (hb_[2], (hb_[2] + hb_[3]) / 2, hb_[3])])
        rep.c(tr >= rel['dry']['trunk_min_m'], 'F3 no vegetation-sheet trunk stands in the hut box: nearest %.1f m from its nine box points (>= %.1f)' % (tr, rel['dry']['trunk_min_m']))
        rep.c(d2(M, old) <= f3['move_max_m'], 'F3 the group moves %.2f m (<= %.1f): point %s -> %s; the stones and the hut keep their offsets' % (d2(M, old), f3['move_max_m'], R.xz(old), R.xz(M)))
        # the first choice of F3 (ground <= 4 deg within 8 m where the check passes) - searched, so the fallback is a finding and not a choice
        win = f3['slope_window_m']; step = f3['search_step_m']; n = int(f3['move_max_m'] / step); flat = []; best = None

        def slope(x, z):
            gx = (H(x + win, z) - H(x - win, z)) / (2 * win); gz = (H(x, z + win) - H(x, z - win)) / (2 * win); return math.degrees(math.atan(math.hypot(gx, gz)))
        for i in range(-n, n + 1):
            for j in range(-n, n + 1):
                dx, dz = i * step, j * step
                if math.hypot(dx, dz) > f3['move_max_m'] + 1e-9: continue
                hx, hz = old[0] + dx + hut['offset'][0], old[1] + dz + hut['offset'][1]; sl = slope(hx, hz)
                if best is None or sl < best[0]: best = (sl, dx, dz)
                if sl <= f3['flat_slope_deg']: flat.append((dx, dz))
        rep.c(not flat, 'F3 first choice not available: no hut place within %.0f m of the first one has a slope <= %.0f deg (flattest %.1f deg at d(%.1f, %.1f)) - so the group goes to the place where the UNCHANGED check passes; slope there %.1f deg' % (
            f3['move_max_m'], f3['flat_slope_deg'], best[0], best[1], best[2], slope(*at)))
        rep.info('F3 tilt (the decision\'s fallback) is NOT used: bounds bottom is one number, so the spread of the four corner readings is the spread of the GROUND under the world box whatever the tilt; a tilted hut reads -0.30 - spread at the uphill corners. The spread shrinks only with the box (yaw) and the place.')
        # neighbours
        ag = next((r for r in rel.get('encounter_moves', []) if r['id'] == 'folklore298/agwi' and on(r)), None)
        if ag:
            A = (ag['x'], ag['z']); rng = rel['dry']['yeomak_agwi_m']
            rep.c(rng[0] < d2(M, A) <= rng[1], 'F3 여막 point to agwi %.1f m (%s .. %s, relayout dry.yeomak_agwi_m; was %.1f)' % (d2(M, A), rng[0], rng[1], d2(old, A)))
            # AC-C12: agwi to the nearest rest place <= agwi_rest_max_m, to the main road >= agwi_road_min_m
            feet = {cid: (float(c['Feet']['x']), float(c['Feet']['z'])) for cid, c in cps.items()}
            for r in rel.get('rests', []) + rel.get('rests_escort', []):
                if on(r): feet[r['id']] = (r['feet']['x'], r['feet']['z'])
            rest_pts = [(cid, f) for cid, f in feet.items() if cid in pts and int(pts[cid].get('Kind', -1)) == 2] or list(feet.items())
            near = min(rest_pts, key=lambda x: d2(x[1], A))
            rep.c(d2(near[1], A) <= ck['agwi_rest_max_m'], 'AC-C12 agwi to the nearest rest %s %.1f m (<= %s)' % (near[0], d2(near[1], A), ck['agwi_rest_max_m']), ('ACC12.rest', round(d2(near[1], A), 1)))
            mp = [(float(q['x']), float(q['z'])) for q in content['MainPath']]; dm = min(d2(q, A) for q in mp)
            rep.c(dm >= ck['agwi_road_min_m'], 'AC-C12 agwi to the main road (MainPath) %.1f m (>= %s)' % (dm, ck['agwi_road_min_m']), ('ACC12.road', round(dm, 1)))
        else: rep.info('agwi row off / absent: AC-C12 not judged')
        rep.c(w.reach9(*M), 'F3 the moved point is on EA-reachable ground (3x3 reach cells)')
        rep.info('F3 moved point %s: ground %.2f, slope %.1f deg, road centre %.1f m, cliff line %.0f m, long wall %.0f m' % (R.xz(M), H(*M), w.slope(*M), w.road(M)[0], w.cliff(M), w.wall(M)))
        # the extension's earth enemies (watch line + the 54 m rule for what this fix moved)
        earth = D.get('earth')
        if earth:
            radius = next((x.get('radius_m', 2.5) for x in earth['checks'].get('watch', []) if x.get('point') == 'road_yeomak308'), 2.5)
            for e in earth['encounters']:
                if not e.get('enabled', True) or alias not in e.get('scenes', ORDER): continue
                E = (e['x'], e['z']); wd = d2(E, M) - radius; wo = d2(E, old) - radius
                if d2(E, old) < 150:
                    rep.c(wd >= e['detection'], 'earth %s <-> 여막 조사 점: ring edge %.1f m >= detection %s (margin %+.1f; before the move %+.1f)' % (e['id'], wd, e['detection'], wd - e['detection'], wo - e['detection']), ('earth.watch.' + e['id'], round(wd - e['detection'], 2)))
                # benches are not rest places; the stops this fix seats keep their XZ: the 54 m rule cannot change - stated, with the number
            rep.info('earth: the stops / rest feet keep their XZ in this fix (F5 moves Y only, F1 moves a bench), so the extension\'s 54 m margins (+3.1 / +4.8 / +7.6 m) are unchanged; run `python Tools/Art/earth308_dry.py` after the copy for the full table')
        else: rep.info('no earth308.json (extension not staged / deployed): earth margins not judged')

    # ---------------------------------------------------------------- F4 T3 destinations
    rep.head('F4  T3 stage destinations (terrain-judged unless the row carries support "built")')
    if alias != 'main': rep.info('T3 is judged in the play scene only (campaign source = WorldContent_Main)')
    else:
        tolT = ck['dest_ground_tol_m']; tolB = ck['built_floor_tol_m']
        rep.c(tolB == sd['ground']['max_dy_vs_content_m'], 'F4 checks.built_floor_tol_m %.2f = ground.max_dy_vs_content_m %.2f (the decision\'s 0.16 m)' % (tolB, sd['ground']['max_dy_vs_content_m']))
        encs = enc
        for r in rel['campaign']['destinations']:
            if not on(r): continue
            src = pts.get(r['point']) if r.get('point') else encs.get(r['encounter'])
            if src is None: rep.c(False, 'T3 %s: source missing in the content' % r['stage']); continue
            q = src['Position'] if r.get('point') else src['Feet']; P3 = (float(q['x']), float(q['y']), float(q['z'])); gap = P3[1] - H(P3[0], P3[2])
            ev = fd['f4']['evidence'].get(r['stage']); col = []
            if ev:
                fw = sk.world(ev['frame_key'])
                for kind in ('colliders', 'visual_only'):
                    for m in ev.get(kind, []):
                        nw = sk.world(m['key']); has_col = bool(sk.comp(m['key'], 64))
                        if nw is None: continue
                        V, I = mesh_asset(PROJECT / m['mesh'])
                        for k3 in range(0, len(I) - 2, 3):
                            tri = tuple((V[I[k3 + t]][0] + nw[0][0], V[I[k3 + t]][1] + nw[0][1], V[I[k3 + t]][2] + nw[0][2]) for t in range(3))
                            y = B.vertical_hit(tri, P3[0], P3[2])
                            if y is None: continue
                            a, b, c3 = tri; ny = (b[2] - a[2]) * (c3[0] - a[0]) - (b[0] - a[0]) * (c3[2] - a[2])
                            col.append((y, ny > 0, has_col, Path(m['mesh']).stem))
                col.sort()
            floors = [c for c in col if c[1] and c[2] and -1e-3 <= P3[1] - c[0] <= tolB]
            column = '; '.join('%.2f (%+.2f) %s %s%s' % (c[0], c[0] - P3[1], c[3], 'up' if c[1] else 'down', '' if c[2] else ' NO COLLIDER') for c in col) or 'no evidence meshes listed'
            if r.get('support') == 'built':
                rep.c(bool(floors), 'T3 %s [support built]: a collider floor within %.2f m under the point (%.2f, %.2f, %.2f) - column: %s' % (r['stage'], tolB, P3[0], P3[1], P3[2], column))
            elif abs(gap) <= tolT:
                rep.c(True, 'T3 %s: |y - height| %.2f <= %.2f (terrain-judged)' % (r['stage'], abs(gap), tolT))
            else:
                rep.opened('T3 %s: %.2f m over the height field and NOT flagged "built": the editor line stays FAIL. Offline column at the point (y %.2f): %s. No walkable collider floor lies within %.2f m under it, so the flag is not proven (README F4)' % (r['stage'], gap, P3[1], column, tolB))

    # ---------------------------------------------------------------- F5 escort stops
    rep.head('F5  escort stops on their own ground (SmallOps308 stops)')
    es = so['escort_stops']; f5 = fd['f5']; was = []; n_nodes = 0
    rep.c(es['expect_tol_m'] <= f5['expect_tol_max_m'] and es['max_shift_m'] <= f5['max_shift_max_m'], 'F5 the height-field witness is on: escort_stops.expect_tol_m %.2f <= %.2f, max_shift_m %.1f <= %.1f' % (es['expect_tol_m'], f5['expect_tol_max_m'], es['max_shift_m'], f5['max_shift_max_m']))
    for row in es['stops']:
        t = sk.get(row['stop'])
        if t is None: rep.c(False, 'F5 %s: stop %s not in this scene' % (row['id'], row['stop'])); continue
        keep = set(row.get('keep', [])); lifts = row.get('lifts', {}); P = pts.get(row['point']); kids = {sc.nodes[c].name: c for c in sc.children_of(t)}
        if P is None: rep.c(False, 'F5 %s: content point %s missing' % (row['id'], row['point'])); continue
        ip = sc.world(kids['Interaction'])[0] if 'Interaction' in kids else None
        rep.c(ip is not None and d2((ip[0], ip[2]), (float(P['Position']['x']), float(P['Position']['z']))) <= es['pose_tol_m'], 'F5 %s: Interaction is over the content point in XZ (the op never moves XZ)' % row['id'])
        if row.get('checkpoint'):
            C = cps.get(row['checkpoint']); cn = sc.world(kids['Checkpoint'])[0] if 'Checkpoint' in kids else None
            rep.c(C is not None and cn is not None and 'Checkpoint' not in keep and d2((cn[0], cn[2]), (float(C['Feet']['x']), float(C['Feet']['z']))) <= es['pose_tol_m'], 'F5 %s: the Checkpoint node is over the feet of its own checkpoint %s' % (row['id'], row['checkpoint']))
        rep.c(sorted(keep) == sorted(f5['keep_required'].get(row['id'], [])), 'F5 %s: keep[] is exactly the children another ledger / a building owns (%s; required %s)' % (row['id'], ', '.join(sorted(keep)) or '-', ', '.join(sorted(f5['keep_required'].get(row['id'], []))) or '-'))
        # review (safety S5): the op's ground rule ignores the whole stop subtree - a node inside the XZ bounds of a kept child's collider would be seated UNDER it (the editor plan refuses)
        kb = keep_bounds(sk, row['stop'], keep, fd)
        for name, c in sorted(kids.items()):
            if name in keep: continue
            p = sc.world(c)[0]; hit = [(kn, src) for (kn, lo_, hi_, src) in kb if lo_[0] <= p[0] <= hi_[0] and lo_[2] <= p[2] <= hi_[2]]
            if hit: rep.c(False, 'F5 %s/%s (%.2f, %.2f) lies inside the collider bounds of the kept child %s [%s]: stops:plan refuses it' % (row['id'], name, p[0], p[2], hit[0][0], hit[0][1]))
        if kb:
            near = min((math.hypot(max(lo_[0] - sc.world(c)[0][0], 0, sc.world(c)[0][0] - hi_[0]), max(lo_[2] - sc.world(c)[0][2], 0, sc.world(c)[0][2] - hi_[2])), name, kn) for name, c in kids.items() if name not in keep for (kn, lo_, hi_, src) in kb)
            rep.c(near[0] > 0, 'F5 %s: no seated node stands inside a kept child\'s collider bounds (%d bounds measured; nearest: %s %.2f m outside %s)' % (row['id'], len(kb), near[1], near[0], near[2]))
        unknown = [k for k in keep if k not in kids] + [k for k in lifts if k not in kids]
        rep.c(not unknown, 'F5 %s: every keep / lifts name is a child of the stop (%s)' % (row['id'], ', '.join(unknown) or 'all found'))
        worst = 0.0; slopes = []
        for name, c in sorted(kids.items()):
            p = sc.world(c)[0]; g = H(p[0], p[2])
            if name in keep:
                rep.info('F5 %s/%s kept at (%.2f, %.3f, %.2f) (ground %.3f, %+.3f)' % (row['id'], name, p[0], p[1], p[2], g, p[1] - g)); continue
            lift = lifts.get(name, 0.0); target = g + lift; shift = target - p[1]; n_nodes += 1; worst = max(worst, abs(shift))
            if abs(p[1] - lift - g) > f5['under_ground_report_m']: was.append('%s/%s %+.2f' % (row['id'], name, p[1] - lift - g))
            ok = abs(shift) <= es['max_shift_m']
            if name in lifts: ok = ok and abs(lift - 0.5 * f5['bench_scale_y']) <= 0.01 and abs(sc.world(c)[2][1] - f5['bench_scale_y']) <= 0.01
            slopes.append((name, w.slope(p[0], p[2])))
            rep.c(ok, 'F5 %s/%s: y %.3f -> %.3f (%+.3f; ground %.3f%s), XZ (%.2f, %.2f) unchanged' % (row['id'], name, p[1], target, shift, g, (' + lift %.2f = half the bench height' % lift) if name in lifts else '', p[0], p[2]), ('F5.%s.%s' % (row['id'], name), round(target, 3)))
        if slopes: rep.info('F5 %s ground slope under the seated nodes: %s (pivot-only seating: on a slope the ends of a long object float / sink by half its length x tan(slope); review S5)' % (row['id'], ', '.join('%s %.1f deg' % (n_, s_) for n_, s_ in slopes)))
        rp = sc.world(t)[0]; rep.info('F5 %s root stays at y %.3f (a marker, ground %.3f); content point %s y %.3f -> %.3f%s; largest shift %.2f m' % (row['id'], rp[1], H(rp[0], rp[2]), row['point'], float(P['Position']['y']), H(float(P['Position']['x']), float(P['Position']['z'])),
                 ('; checkpoint %s feet y %.3f -> %.3f' % (row['checkpoint'], float(cps[row['checkpoint']]['Feet']['y']), H(float(cps[row['checkpoint']]['Feet']['x']), float(cps[row['checkpoint']]['Feet']['z'])))) if row.get('checkpoint') and row['checkpoint'] in cps else '', worst))
    rep.info('F5 nodes off their ground by more than %.1f m today: %d of %d (%s); after stops:apply every listed node stands on height_p1b (the editor uses its physics ray and refuses a node whose ground is more than %.1f m off the field)' % (
        f5['under_ground_report_m'], len(was), n_nodes, ', '.join(was) or 'none', es['expect_tol_m']))
    all_stops = [sc.nodes[c].name for c in sc.children_of(sk.get('CapitalEscort252')) if sk.get('CapitalEscort252/%s/Parking' % sc.nodes[c].name)]
    listed = {row['id'] for row in es['stops']} | {so['escort']['stop'].split('/')[-1]}
    rep.c(set(all_stops) <= listed, 'F5 every stop of CapitalEscort252 that has a Parking node is covered (escort_start by the 1b op, the rest by escort_stops): %s' % ', '.join(sorted(all_stops)))

    # ---------------------------------------------------------------- F6 paths
    rep.head('F6  MainPath / BranchPath heights against the height field')
    pd = rel['paths']; f6 = fd['f6']; Hn = w.Hn; up = pd.get('max_above_terrain_m', float('inf')); down = pd.get('max_below_terrain_m', float('inf'))
    rep.c(pd.get('max_above_terrain_m') == f6['max_above_terrain_m'] and pd.get('max_below_terrain_m') == f6['max_below_terrain_m'], 'F6 the data carries the decision limits: paths.max_above_terrain_m %s / max_below_terrain_m %s (decision F6: %.1f / %.1f)' % (pd.get('max_above_terrain_m'), pd.get('max_below_terrain_m'), f6['max_above_terrain_m'], f6['max_below_terrain_m']))
    up = min(up, f6['max_above_terrain_m']); down = min(down, f6['max_below_terrain_m'])

    def in_box(rows, p): return any(b['x'][0] <= p[0] <= b['x'][1] and b['z'][0] <= p[1] <= b['z'][1] for b in rows)

    def judge(name, arr):
        above = [(i, q) for i, q in enumerate(arr) if q[1] - bilinear(Hn, q[0], q[2]) > up and not in_box(pd.get('decks', []), (q[0], q[2]))]
        below = [(i, q) for i, q in enumerate(arr) if bilinear(Hn, q[0], q[2]) - q[1] > down and not in_box(pd.get('underground', []), (q[0], q[2]))]
        inrun = sum(1 for q in arr if bilinear(Hn, q[0], q[2]) - q[1] > down and in_box(pd.get('underground', []), (q[0], q[2])))
        return above, below, inrun
    live_main = [(float(q['x']), float(q['y']), float(q['z'])) for q in content['MainPath']]; live_branch = [(float(q['x']), float(q['y']), float(q['z'])) for q in content['BranchPath']]
    a0, b0, _ = judge('MainPath', live_main)
    rep.info('F6 the MainPath in the asset today: %d points more than %.1f m above the field%s' % (len(a0), up, (' - [%d..%d] (%.0f, %.1f .. %.1f), +%.1f .. +%.1f m' % (a0[0][0], a0[-1][0], a0[0][1][0], a0[0][1][2], a0[-1][1][2], min(q[1] - bilinear(Hn, q[0], q[2]) for _, q in a0), max(q[1] - bilinear(Hn, q[0], q[2]) for _, q in a0))) if a0 else ''))
    if D.get('_mutate_no_regen'):
        new_main = live_main; new_chain = None
    else:
        xz = build_path(w, pd['main_legs'], pd['step_m'], pd['join_dedupe_m']); new_main = [(p[0], bilinear(Hn, p[0], p[1]), p[1]) for p in xz]
        rep.c(len(xz) == len(live_main) and max(d2(p, (q[0], q[2])) for p, q in zip(xz, live_main)) <= 1e-2, 'F6 the regenerated MainPath has the XZ of the present one (%d points): only heights change' % len(xz))
        cz = build_path(w, pd['branch_chain'], pd['step_m'], pd['join_dedupe_m']); new_chain = [(p[0], bilinear(Hn, p[0], p[1]), p[1]) for p in cz]
        diff = max(abs(bilinear(Hn, p[0], p[1]) - H(p[0], p[1])) for p in xz + cz)
        rep.c(diff <= f6['sample_diff_max_m'], 'F6 editor sampler (bilinear) vs offline sampler (triangles) along the paths: worst %.3f m (<= %.2f)' % (diff, f6['sample_diff_max_m']))
    a1, b1, _ = judge('MainPath', new_main)
    rep.c(not a1, 'F6 MainPath points more than %.1f m ABOVE the height field outside the listed decks: %d of %d%s' % (up, len(a1), len(new_main), (' (first [%d] (%.1f, %.2f, %.1f))' % (a1[0][0], a1[0][1][0], a1[0][1][1], a1[0][1][2])) if a1 else ''), ('F6.main.above', len(a1)))
    rep.c(not b1, 'F6 MainPath points more than %.1f m BELOW the height field outside the listed underground runs: %d' % (down, len(b1)))
    # BranchPath after the regen = the kept old runs (not on the chain's roads) + the chain
    if new_chain is None: new_branch = live_branch
    else:
        chain_roads = [list(reversed(w.routes[l['route']])) if l.get('reverse') else w.routes[l['route']] for l in pd['branch_chain']]; kept = []
        for run in R.runs([(q[0], q[2]) for q in live_branch]):
            if not any(R.poly_d(p, pl) <= pd['branch_replace_within_m'] for p in run for pl in chain_roads): kept += [q for q in live_branch if (q[0], q[2]) in set(run)]
        new_branch = kept + new_chain
    a2, b2, inrun = judge('BranchPath', new_branch)
    rep.c(not a2, 'F6 BranchPath points more than %.1f m ABOVE the height field outside the listed decks: %d of %d' % (up, len(a2), len(new_branch)))
    rep.c(not b2, 'F6 BranchPath points more than %.1f m BELOW the height field outside the listed underground runs: %d (in a listed run: %d%s)' % (down, len(b2), inrun, (', e.g. [%d] (%.1f, %.2f, %.1f) %.1f m under' % (b2[0][0], b2[0][1][0], b2[0][1][1], b2[0][1][2], bilinear(Hn, b2[0][1][0], b2[0][1][2]) - b2[0][1][1])) if b2 else ''), ('F6.branch.below', len(b2)))
    decks_ok = all(all(k in dk for k in ('id', 'x', 'z', 'window_m', 'evidence', 'collider_key')) and str(dk.get('evidence') or '').strip() and sk.get(dk['collider_key']) is not None and not re.search(r'roof', dk['collider_key'], flags=re.I) for dk in pd.get('decks', []))
    rep.c(decks_ok, 'F6 every listed deck carries id / x / z / window_m / a non-empty evidence / collider_key = a scene object that is not a roof (%d listed)' % len(pd.get('decks', [])))
    if not D.get('_mutate_no_regen'):
        phys = [(i, q[1] - n_[1]) for i, (q, n_) in enumerate(zip(live_main, new_main)) if abs(q[1] - n_[1]) > f6['physical_report_m']]; roof = {i for i, _ in a0}
        other = [(i, dlt) for i, dlt in phys if i not in roof]
        rep.info('F6 regenerated MainPath vs the physics-built heights in the asset today: %d point(s) differ by more than %.1f m - %d on the gate roof, %d elsewhere%s (where a walkable mesh lies over the field the path now runs under it by that much; N1 / N4)' % (
            len(phys), f6['physical_report_m'], len(phys) - len(other), len(other), (' (worst [%d] %+.2f m at (%.0f, %.0f))' % (max(other, key=lambda v: abs(v[1]))[0], max(other, key=lambda v: abs(v[1]))[1], live_main[max(other, key=lambda v: abs(v[1]))[0]][0], live_main[max(other, key=lambda v: abs(v[1]))[0]][2])) if other else ''))
    rep.c(all(str(u.get('evidence') or '').strip() for u in pd.get('underground', [])), 'F6 every listed underground run carries its evidence (%d listed)' % len(pd.get('underground', [])))

    # ---------------------------------------------------------------- F7 seal list + wall build
    rep.head('F7  seal rest list and the long-wall build record')
    f7 = fd['f7']; bey = json.loads((ROOT / f7['beyond']).read_text(encoding='utf-8')); have = list(bey['ea_rest_ids']); want = sorted(set(have) | set(D.get('_seal_add', f7['add_rest_ids'])))
    for rid in f7['add_rest_ids']:
        rep.c(rid in want, 'F7 %s is in the seal rest list after the copy (ea_rest_ids %d -> %d; in the file today: %s)' % (rid, len(have), len(want), rid in have))
    known = set(cps) | {r['id'] for r in rel.get('rests', []) + rel.get('rests_escort', [])} | ({D['roadinn']['rest']['id']} if D.get('roadinn') else set())
    stray = [rid for rid in D.get('_seal_add', f7['add_rest_ids']) if rid not in known]
    rep.c(not stray, 'F7 every id added to the seal list is a rest: a checkpoint of this scene\'s content, a rests[] row of the relayout data or the extension\'s inn rest (%s)' % (', '.join(stray) + ' unknown' if stray else 'all known'))
    rep.c(want == sorted(want), 'F7 the list stays in ordinal order (RoadInn308 seal-apply keeps that order, so the two writers agree)')
    rings = [[(float(q[0]), float(q[2] if len(q) >= 3 else q[1])) for q in (ring if isinstance(ring, list) else ring['points'])] for ring in bey['beyond_rings']]
    feet_new = {'entry_rest308': next(((r['feet']['x'], r['feet']['z']) for r in rel['rests'] if r['id'] == 'entry_rest308'), None)}
    if D.get('roadinn'): feet_new[D['roadinn']['rest']['id']] = (D['roadinn']['rest']['feet']['x'], D['roadinn']['rest']['feet']['z'])
    for rid, f in feet_new.items():
        if f is None: rep.c(False, 'F7 %s: no feet in the data' % rid); continue
        inside = any(in_ring(f, ring) for ring in rings)
        rep.c(not inside and w.reach9(*f), 'F7 %s feet %s: outside every beyond ring and on closed-state reachable ground (the rule cliff308_closure applies to ea_rest_ids_add)' % (rid, R.xz(f)))
        rep.info('F7 %s checkpoint in this scene\'s content: %s' % (rid, 'yes' if rid in cps else 'NOT YET (the extension\'s RoadInn308 content-apply writes it; a seal id that does not resolve is skipped at run time)'))
    wl = ROOT / f7['wall_layout']; led = ROOT / f7['wall_ledgers'][alias]
    if led.exists():
        lj = json.loads(led.read_text(encoding='utf-8')); now = sha(wl)
        rep.info('F7 wall ledger %s: state %s, built from layout sha %s; the layout file is %s -> %s' % (alias, lj.get('state'), (lj.get('layoutSha256') or '')[:12], now[:12],
                 'the build key differs: Jangseong308 apply rebuilds the root (the key is a hash of the layout sha + the seats) and writes the profile from %s' % Path(f7['beyond']).name if (lj.get('layoutSha256') or '') != now else 'same layout: apply only rewrites the profile'))
    rep.info('F7 Jangseong308 check expected after apply: NOT OK, PASS %d / FAIL %d with the recorded exceptions (%s) - the count must not get worse' % (f7['expected_check']['pass'], f7['expected_check']['fail'], f7['check_dispositions']))

    # ---------------------------------------------------------------- F8 kiln wreck pieces
    rep.head('F8  the kiln wreck: every piece against the road rule')
    f8 = fd['f8']; spec = (ROOT / f8['spec']).read_text(encoding='utf-8')
    rep.c(f8['spec_line'] in spec, 'F8 the rule is in %s: "%s"' % (f8['spec'], f8['spec_line']))
    for st in seat['reseat']['sets']:
        if not st.get('enabled'): continue
        rule = st.get('piece_road_clear_m')
        dsg = json.loads((ROOT / f8['design']).read_text(encoding='utf-8')); m10 = next((o for o in dsg['ops'] if o.get('id') == f8['design_op']), None)
        rep.c(m10 is not None and any(f8['design_line'] in str(x.get('expect', '')) for x in m10.get('post', [])), 'F8 the per-piece rule is in the design (%s ops %s post: "%s")' % (f8['design'], f8['design_op'], f8['design_line']))
        want_rule = w.width[f8['road']] / 2 + f8['beyond_half_width_m']
        rep.c(rule is not None and abs(rule - want_rule) < 1e-6 and rule >= f8['central_width_m'] / 2, 'F8 %s: piece_road_clear_m %s = half the width of %s (%.1f m) + %.1f m = %.1f (the #264 floor: %.1f)' % (st['id'], rule, f8['road'], w.width[f8['road']], f8['beyond_half_width_m'], want_rule, f8['central_width_m'] / 2))
        t = sk.get(st['root_key'])
        if t is None or rule is None: rep.c(False, 'F8 %s: root %s missing / no rule' % (st['id'], st['root_key'])); continue
        rp = sc.world(t)[0]; rroad = w.road((rp[0], rp[2]))[0]
        rep.c(rroad >= st['road_clear_m'] - fd['tol_m'], 'F8 %s root %.2f m from the road centre (>= %.1f, the design limit of the root)' % (st['id'], rroad, st['road_clear_m']))
        for c in sc.children_of(t):
            name = sc.nodes[c].name; bb = sk.box_bounds(sk.key_of(c))
            if bb is None: rep.info('F8 %s/%s: no box collider in the YAML (the editor measures its renderer bounds)' % (st['id'], name)); continue
            lo, hi = bb; qs = [(x, z) for x in (lo[0], hi[0]) for z in (lo[2], hi[2])] + [((lo[0] + hi[0]) / 2, (lo[2] + hi[2]) / 2)]
            near = min(w.road(q)[0] for q in qs); pv = sc.world(c)[0]; rid2, rw = w.road(qs[0])[1], w.road(qs[0])[2]
            rep.c(near >= rule, 'F8 %s/%s: collider bounds %.2f m from the nearest road centre (>= %.1f; pivot %.2f m; %s is %.0f m wide, so %.2f m outside its edge)' % (st['id'], name, near, rule, w.road((pv[0], pv[2]))[0], rid2, rw, near - rw / 2), ('F8.' + name, round(near, 2)))

    # ---------------------------------------------------------------- F9 grass
    rep.head('F9  grass: the field that draws it and the clearing zones')
    f9 = fd['f9']; gr = seat['grass']; ga = gr['asset']
    rep.c(not any(x in ga for x in f9['protected']) and meta_guid(ga) == gr['guid'], 'F9 the grass field %s is outside the protected trees and its guid is the data\'s' % ga)
    stext = (PROJECT / S.SCENES[alias]).read_text(encoding='utf-8', errors='replace') if grass is not None else ''
    if grass is not None:
        nr = len(re.findall(r'guid: 54eb284bb6c84f94c95679a00a98d69d', stext)); fields = set(re.findall(r'^  Field: \{fileID: \d+, guid: ([0-9a-f]{32})', stext, flags=re.M))
        terr = len(re.findall(r'^--- !u!(218|154) ', stext, flags=re.M))
        rep.c(nr == 1 and fields == {gr['guid']} and terr == 0, 'F9 this scene draws grass through %d CompactGrassRenderer266 with field(s) %s and has %d Terrain / TerrainCollider components (so the grass of the stills is this field)' % (nr, ', '.join(x[:8] for x in fields), terr))
        x, y, z = grass
        import numpy as np

        def seeds(cx, cz, r):
            gy = H(cx, cz); m = (np.abs(x - cx) <= r) & (np.abs(z - cz) <= r) & (np.abs(y - gy) <= gr['y_window_m'] + 3.0)
            idx = np.nonzero(m)[0]; return [(float(x[i]), float(z[i])) for i in idx if math.hypot(float(x[i]) - cx, float(z[i]) - cz) <= r]
        rep.c(gr.get('apply_enabled') is False, 'F9 grass.apply_enabled is false in the staged data (unlocked only after the read-only diag: grass-unlock --write)')
        zones = {c['id']: c for c in gr['clear']}; total = 0
        for zid in f9['zones']:
            c = zones.get(zid)
            if c is None: rep.c(False, 'F9 zone %s missing in grass.clear' % zid); continue
            if c['kind'] == 'disc':
                n = len(seeds(c['centre_xz'][0], c['centre_xz'][1], c['radius_m'])); ok = abs(c['radius_m'] - (c['footprint_radius_m'] + f9['margin_m'])) < 1e-6 and c.get('enabled') and not c.get('closed')
                rep.c(ok, 'F9 zone %s: disc r %.1f = footprint %.1f + %.1f m at (%.1f, %.1f): %d seed(s) [O]' % (zid, c['radius_m'], c['footprint_radius_m'], f9['margin_m'], c['centre_xz'][0], c['centre_xz'][1], n), ('F9.' + zid, n)); total += n
            else:
                kw = sk.world(c['key'])
                if kw is None: rep.c(False, 'F9 zone %s: object %s missing' % (zid, c['key'])); continue
                reach = max(abs(v) for v in c['rect']) * 1.5 + c['margin_m']; n = 0
                for (px, pz) in seeds(kw[0][0], kw[0][2], reach):
                    lx, lz = S.inv(kw[1], px - kw[0][0], pz - kw[0][2])
                    if S.rect_out(c['rect'], lx, lz) <= c['margin_m']: n += 1
                rep.c(abs(c['margin_m'] - f9['margin_m']) < 1e-6 and c.get('enabled') and not c.get('closed'), 'F9 zone %s: footprint rect + %.1f m around %s: %d seed(s) [O]' % (zid, c['margin_m'], c['key'], n), ('F9.' + zid, n)); total += n
        for pr in stones:
            zc = zones.get(pr['id'] + '_f9'); A9 = anchor.get(pr['anchor'])
            if zc and A9: rep.c(d2(zc['centre_xz'], (A9[0] + pr['offset'][0], A9[1] + pr['offset'][1])) <= 0.06, 'F9 zone %s_f9 is centred on its stone (%s vs anchor + offset %s)' % (pr['id'], zc['centre_xz'], R.xz((A9[0] + pr['offset'][0], A9[1] + pr['offset'][1]))))
        others = [c['id'] for c in gr['clear'] if c.get('enabled') and not c.get('closed') and c['id'] not in f9['zones']]
        rep.c(not others, 'F9 no zone outside the F9 list is on (grass-apply removes every enabled zone, once): %s' % (', '.join(others) or 'none'))
        ne = seeds(f9['entry_shrine_xz'][0], f9['entry_shrine_xz'][1], f9['entry_clear_m'])
        rep.c(not ne, 'F9 the entry shrine altar has %d seed(s) within %.1f m: no clearing is needed there' % (len(ne), f9['entry_clear_m']))
        rep.info('F9 (a) would take about %d seed(s) out of the shared field (zones overlap: the editor counts each seed once)' % total)
        # (b) the fallback place
        fb = f9['fallback']; lg = next((r for r in rel['point_moves'] if r['id'] == 'logger'), None)
        if lg:
            L = (lg['x'], lg['z']); best = None; n = int(fb['search_m'] / fb['step_m'])
            near = seeds(L[0], L[1], fb['search_m'] + 6)
            for i in range(-n, n + 1):
                for j in range(-n, n + 1):
                    q = (L[0] + i * fb['step_m'], L[1] + j * fb['step_m']); dd = d2(q, L)
                    if dd > fb['search_m'] or (best and dd >= best[0]): continue
                    pile = (q[0] + fb['pile_offset'][0], q[1] + fb['pile_offset'][1])
                    if any(d2(sd_, q) <= fb['bare_logger_m'] or d2(sd_, pile) <= fb['bare_pile_m'] for sd_ in near): continue
                    s = w.site(q, ('Rebuild_InnPeople/logger', 'Content308/Props/'))
                    if not (s['reach'] and s['slope'] <= fb['max_slope_deg'] and s['trunk'] >= rel['dry']['trunk_min_m'] and s['col'] - 0.3 >= rel['dry']['collider_gap_min_m'] and s['cliff'] >= rel['dry']['cliff_min_m'] + 10): continue
                    if w.road(pile)[0] < seat['firewood']['corridor_clear_m'] or w.slope(*pile) > seat['firewood']['max_slope_deg']: continue
                    best = (dd, q, pile, s)
            if best:
                rep.c(True, 'F9 (b) fallback place exists: woodcutter %s (%.1f m from the present point, slope %.1f deg, road centre %.1f m, trunk %.1f m, collider %.1f m), pile centre %s (road %.1f m); no seed within %.1f / %.1f m' % (
                    R.xz(best[1]), best[0], best[3]['slope'], best[3]['road'], best[3]['trunk'], best[3]['col'], R.xz(best[2]), w.road(best[2])[0], fb['bare_logger_m'], fb['bare_pile_m']), ('F9.fallback', (round(best[1][0], 1), round(best[1][1], 1))))
                rep.info('F9 (b) the pile centre %s: slope %.1f deg (firewood limit %.0f), nearest trunk %.1f m (the seat tool re-checks its 1.2 m trunk rule per piece in the editor; review S6)' % (R.xz(best[2]), w.slope(*best[2]), seat['firewood']['max_slope_deg'], w.trunk(best[2])))
                for cx_, cz_, lab in ((lg['x'], lg['z'], 'woodcutter'), (seat['firewood']['centre_xz'][0], seat['firewood']['centre_xz'][1], 'pile')):
                    rep.info('F9 seeds around the %s today: %d within 1.3 m, %d within 2.0 m, %d within 3.0 m, %d within 4.0 m (a clump reaches up to 1.9 m from its seed: the hiding grass may be seeded outside a footprint + 1.0 m disc)' % (lab, len(seeds(cx_, cz_, 1.3)), len(seeds(cx_, cz_, 2.0)), len(seeds(cx_, cz_, 3.0)), len(seeds(cx_, cz_, 4.0))))
            else:
                rep.c(False, 'F9 (b) no bare place within %.0f m of the woodcutter keeps the M04 post-conditions' % fb['search_m'])
    else: rep.info('F9 seed counts skipped (grass field not loaded)')

    # ---------------------------------------------------------------- F10 / F11
    rep.head('F10 / F11  the hinge note and the fix stills')
    hn = seat['hinges']; rows_anchor = {r['anchor'] for r in hn['rows']}
    rep.c(rows_anchor == {'edge'} and hn['anchor_note'].startswith('edge') and 'the D308-16 decision' not in hn['anchor_note'], 'F10 hinges.anchor_note describes what the rows use (%s)' % ', '.join(sorted(rows_anchor)))
    for row in fd['stills']['rows']:
        eye = tuple(row['eye']); tgt = tuple(row['target']); clear = w.clearance(eye, tgt, row['target_h']); cg, cpath = w.col_gap(eye)
        rep.c(clear > 0 and cg >= 0.3, 'F11 still %s: eye %s -> %s %.1f m, terrain clearance %+.2f m, nearest collider to the eye %.1f m' % (row['id'], R.xz(eye), R.xz(tgt), d2(eye, tgt), clear, cg))
    return rep


# ---------------------------------------------------------------------------------------------- mutations (each must turn a line red)

def _row(D, key, rid): return next(r for r in D['relayout'][key] if r['id'] == rid)
def _prop(D, pid): return next(p for p in D['scene']['props'] if p['id'] == pid)


def m_bench_on_point(D):
    for r in D['relayout']['rests_escort']: r['node']['offset'] = [0, 0]
def m_bench_in_front(D): _row(D, 'rests_escort', 'road_rest_2')['node']['offset'] = [-1.6, 0.6]
def m_bench_approach_side(D): _row(D, 'rests_escort', 'road_rest_1')['node']['offset'] = [1.6, -0.9]
def m_pillar_again(D): _prop(D, 'road_yeomak308_stone_0')['source_asset'] = 'Assets/HwaseongHaenggung/Prefabs/SM_S_Square_1.prefab'
def m_stone_not_sunk(D): _prop(D, 'road_yeomak308_stone_1')['y_offset'] = -0.05
def m_pillar_row_on(D): _prop(D, 'road_yeomak308_base_2')['enabled'] = True
def m_hut_old_yaw(D): _prop(D, 'road_yeomak308_hut')['yaw'] = 312.6
def m_hut_range_widened(D): _prop(D, 'road_yeomak308_hut')['corner_gap_m'] = [-0.9, 0.2]
def m_group_not_moved(D): r = _row(D, 'points', 'road_yeomak308'); r['x'] = 2056.0; r['z'] = 2208.0
def m_group_toward_earth(D): r = _row(D, 'points', 'road_yeomak308'); r['x'] = 2050.5; r['z'] = 2209.5
def m_built_flag_unproven(D): next(r for r in D['relayout']['campaign']['destinations'] if r['stage'] == 'root_evidence263')['support'] = 'built'
def m_stop_shift_cap(D): D['smallops']['escort_stops']['max_shift_m'] = 3.0
def m_stop_bench_lift(D): D['smallops']['escort_stops']['stops'][2]['lifts']['capital_escort_rest'] = 0.0
def m_stop_dropped(D): D['smallops']['escort_stops']['stops'] = D['smallops']['escort_stops']['stops'][:2]
def m_path_not_regenerated(D): D['_mutate_no_regen'] = True
def m_cave_run_unlisted(D): D['relayout']['paths']['underground'] = []
def m_seal_without_entry(D): D['_seal_add'] = ['road_inn308']
def m_kiln_rule_wrong(D): D['seat']['reseat']['sets'][0]['piece_road_clear_m'] = 3.0
def m_kiln_rule_far(D): D['seat']['reseat']['sets'][0]['piece_road_clear_m'] = 7.0
def m_hut_in_detection(D): r = _row(D, 'points', 'road_yeomak308'); r['x'] = 2063.5; r['z'] = 2210.0; _prop(D, 'road_yeomak308_hut')['y_offset'] = 0.06
def m_hut_no_enemy_rule(D): _prop(D, 'road_yeomak308_hut').pop('enemy_clear', None)
def m_bench_limit_loosened(D):
    for r in D['relayout']['rests_escort']: r['node']['clear_min_m'] = 0; r['node']['offset'] = [-1.2, -0.9]
def m_path_limit_loosened(D): D['relayout']['paths']['max_above_terrain_m'] = 30; D['_mutate_no_regen'] = True
def m_roof_as_deck(D): D['relayout']['paths']['decks'] = [{'id': 'south_gate_roof', 'x': [1990, 2010], 'z': [2540, 2570], 'window_m': 30, 'evidence': '', 'collider_key': 'SouthGate253'}]; D['_mutate_no_regen'] = True
def m_stop_witness_off(D): D['smallops']['escort_stops']['expect_tol_m'] = 30
def m_stop_keep_emptied(D):
    for s_ in D['smallops']['escort_stops']['stops'][:2]: s_['keep'] = []
def m_seal_unknown_id(D): D['_seal_add'] = ['entry_rest308', 'road_inn308', 'no_such_rest308']
def m_stone_disc_left_behind(D): next(c for c in D['seat']['grass']['clear'] if c['id'] == 'road_yeomak308_stone_0_f9')['centre_xz'] = [2064.4, 2210.7]
def m_grass_other_zone_on(D): next(c for c in D['seat']['grass']['clear'] if c['id'] == 'deep_fork_rest308')['enabled'] = True
def m_grass_unlocked(D): D['seat']['grass']['apply_enabled'] = True
def m_hinge_note_old(D): D['seat']['hinges']['anchor_note'] = 'leaf = the D308-16 decision'
def m_still_in_wall(D): D['fix']['stills']['rows'][0]['eye'] = [3067.5, 2336.7]


MUTATIONS = [
    ('bench_on_point', m_bench_on_point, 'F1 the bench back on the content point (under the altar)'),
    ('bench_in_front', m_bench_in_front, 'F1 the bench in front of the candles'),
    ('bench_approach_side', m_bench_approach_side, 'F1 the bench on the road / stop side'),
    ('pillar_again', m_pillar_again, 'F2 a stone row back to the 2.68 m pillar prefab'),
    ('stone_not_sunk', m_stone_not_sunk, 'F2 a stone sunk only 0.05 m (top 0.43 m)'),
    ('pillar_row_on', m_pillar_row_on, 'F2 an old pillar row left on'),
    ('hut_old_yaw', m_hut_old_yaw, 'F3 the hut at its first yaw 312.6'),
    ('hut_range_widened', m_hut_range_widened, 'F3 the corner-gap range widened'),
    ('group_not_moved', m_group_not_moved, 'F3 the point left at (2056, 2208)'),
    ('group_toward_earth', m_group_toward_earth, 'F3 the point moved west, toward earth_road308/0'),
    ('built_flag_unproven', m_built_flag_unproven, 'F4 support "built" set on root_evidence263 without a floor'),
    ('stop_shift_cap', m_stop_shift_cap, 'F5 max_shift_m 3.0 (the nodes need 3.2 - 3.8 m)'),
    ('stop_bench_lift', m_stop_bench_lift, 'F5 the delivery bench without its lift (half buried)'),
    ('stop_dropped', m_stop_dropped, 'F5 the delivery stop taken out of the list'),
    ('path_not_regenerated', m_path_not_regenerated, 'F6 MainPath left as the first regeneration wrote it'),
    ('cave_run_unlisted', m_cave_run_unlisted, 'F6 the cave run not listed as underground'),
    ('seal_without_entry', m_seal_without_entry, 'F7 entry_rest308 not added to the seal list'),
    ('kiln_rule_wrong', m_kiln_rule_wrong, 'F8 the #264 floor alone (3.0 m, on the 8 m carriageway) instead of the M10 rule'),
    ('kiln_rule_far', m_kiln_rule_far, 'F8 a road rule that is not the design\'s (7.0 m)'),
    ('hut_in_detection', m_hut_in_detection, 'F3 the first place of this fix: the hut box 14.4 m from agwi (detection 16)'),
    ('hut_no_enemy_rule', m_hut_no_enemy_rule, 'F3 the hut row without enemy_clear'),
    ('bench_limit_loosened', m_bench_limit_loosened, 'F1 clear_min_m 0 in the data + the bench 0.05 m from the altar'),
    ('path_limit_loosened', m_path_limit_loosened, 'F6 max_above_terrain_m 30 with the old path'),
    ('roof_as_deck', m_roof_as_deck, 'F6 the south gate roof listed as a deck (window 30, no evidence) with the old path'),
    ('stop_witness_off', m_stop_witness_off, 'F5 expect_tol_m 30 (the height-field witness off)'),
    ('stop_keep_emptied', m_stop_keep_emptied, 'F5 keep[] emptied on checkpoint_1 / 2 (two ledgers on one bench)'),
    ('seal_unknown_id', m_seal_unknown_id, 'F7 an id that is no rest added to the seal list'),
    ('stone_disc_left_behind', m_stone_disc_left_behind, 'F9 a stone disc left at the first place of this fix'),
    ('grass_other_zone_on', m_grass_other_zone_on, 'F9 a zone outside the F9 list left on'),
    ('grass_unlocked', m_grass_unlocked, 'F9 the staged data already unlocked'),
    ('hinge_note_old', m_hinge_note_old, 'F10 the old anchor_note'),
    ('still_in_wall', m_still_in_wall, 'F11 a still eye inside the inn'),
]


def load_grass(D):
    try:
        import numpy as np
    except ImportError:
        return None
    f = PROJECT / D['seat']['grass']['asset']
    if not f.exists(): return None
    raw = f.read_bytes(); arr = np.frombuffer(raw[:len(raw) // 4 * 4], '<f4')
    # a seed = Vector3 position + Vector2 normalXZ: five consecutive floats (the scan contentseat308_dry.grass_scan uses)
    x = arr[:-4]; y = arr[1:-3]; z = arr[2:-2]; nx = arr[3:-1]; nz = arr[4:]
    with np.errstate(invalid='ignore'):
        m = (x > 1500) & (x < 4000) & (z > 1500) & (z < 4000) & (y > 0) & (y < 400) & (np.abs(nx) <= 1.0) & (np.abs(nz) <= 1.0)
    return x[m], y[m], z[m]


def run(aliases, D, grass, out=None, quiet=False):
    reps = {}
    for alias in aliases:
        rep = dry_scene(alias, D, grass); reps[alias] = rep
        text = '\n'.join(rep.lines + ['', 'PASS %d / FAIL %d / OPEN %d' % (rep.passed, rep.fail, rep.open)]) + '\n'
        if out: (out / ('relayout_fix308_dry_%s.txt' % alias)).write_text(text, encoding='utf-8'); (out / ('relayout_fix308_dry_%s.json' % alias)).write_text(json.dumps({'alias': alias, 'pass': rep.passed, 'fail': rep.fail, 'open': rep.open, 'nums': rep.nums}, ensure_ascii=False, indent=1, default=str), encoding='utf-8')
        if not quiet: print(text)
    return reps


def cmd_dry(a):
    D = load_data(a.data_dir, a.deployed)
    if a.mutate:
        fn = next((m for m in MUTATIONS if m[0] == a.mutate), None)
        if fn is None: print('unknown mutation %s (%s)' % (a.mutate, ', '.join(m[0] for m in MUTATIONS))); return 2
        fn[1](D); print('MUTATED: %s' % fn[2])
    out = Path(a.out) if a.out else FIX / 'Dry'; out.mkdir(parents=True, exist_ok=True)
    aliases = ORDER if a.scene == 'all' else [a.scene]
    reps = run(aliases, D, load_grass(D), None if a.mutate else out)
    if len(reps) > 1:
        ref = reps[aliases[-1]].nums
        for al in aliases[:-1]:
            diff = [k for k in ref if k in reps[al].nums and reps[al].nums[k] != ref[k]]
            print('numbers of %s vs %s: %s' % (al, aliases[-1], 'identical (%d shared)' % len([k for k in ref if k in reps[al].nums]) if not diff else 'DIFFER: ' + ', '.join('%s %s vs %s' % (k, reps[al].nums[k], ref[k]) for k in diff[:6])))
    fails = sum(r.fail for r in reps.values()); opens = sum(r.open for r in reps.values())
    print('RESULT %s (PASS %d / FAIL %d / OPEN %d over %s)' % ('every offline post-condition of the fix holds: exit 0' if fails == 0 else 'FAILED post-conditions: exit 2', sum(r.passed for r in reps.values()), fails, opens, ', '.join(aliases)))
    return 0 if fails == 0 else 2


def cmd_mutations(a):
    base = load_data(a.data_dir); grass = load_grass(base); alias = a.scene if a.scene != 'all' else 'main'
    clean = run([alias], copy.deepcopy(base), grass, None, True)[alias]
    print('clean run (%s): PASS %d / FAIL %d / OPEN %d' % (alias, clean.passed, clean.fail, clean.open))
    bad = 0 if clean.fail == 0 else 1; lines = []
    for name, fn, what in MUTATIONS:
        D = copy.deepcopy(base); D['_src'] = base['_src']; fn(D)
        rep = run([alias], D, grass, None, True)[alias]; red = [l for l in rep.lines if l.startswith('FAIL')]
        ok = len(red) > 0
        if not ok: bad += 1
        lines.append('%s %-22s %s -> %d red line(s)%s' % ('OK  ' if ok else 'MISS', name, what, len(red), (': ' + red[0][:230]) if red else ''))
        print(lines[-1])
    out = Path(a.out) if a.out else FIX / 'Dry'; out.mkdir(parents=True, exist_ok=True)
    (out / 'relayout_fix308_mutations.txt').write_text('clean run (%s): PASS %d / FAIL %d / OPEN %d\n' % (alias, clean.passed, clean.fail, clean.open) + '\n'.join(lines) + '\n', encoding='utf-8')
    print('RESULT %d mutation(s), %d did not turn a line red%s' % (len(MUTATIONS), bad, '' if bad == 0 else ' - exit 2'))
    return 0 if bad == 0 else 2


def cmd_stills(a):
    D = load_data(a.data_dir); fd = D['fix']; H = S.Height(ROOT / fd['height'])
    for row in fd['stills']['rows']:
        e = row['eye']; t = row['target']; ey = H(e[0], e[1]) + fd['stills']['eye_m']; ty = H(t[0], t[1]) + row['target_h']
        print('shot:relayout308_%%s_%s:%.2f,%.2f,%.2f:%.2f,%.2f,%.2f:fov=60:w=1600:h=900:hideplayer    # %s' % (row['id'], e[0], ey, e[1], t[0], ty, t[1], row['what']))
    return 0


def _patch_live(edits, write, tag):
    """edits = [(file, [(old, new)])] on DEPLOYED files; every old text must be there exactly once."""
    plan = []
    for f, pairs in edits:
        raw = f.read_bytes(); t = raw.decode('utf-8'); crlf = '\r\n' in t; body = t.replace('\r\n', '\n')
        for old, new in pairs:
            if body.count(old) != 1: print('REFUSED: %s does not hold exactly one "%s" (found %d) - is the fix stage deployed?' % (f, old[:90], body.count(old))); return 2
            body = body.replace(old, new)
        json.loads(body); plan.append((f, (body.replace('\n', '\r\n') if crlf else body).encode('utf-8'), raw))
    for f, new, raw in plan: print('  %s sha %s -> %s%s' % (f, hashlib.sha256(raw).hexdigest()[:12], hashlib.sha256(new).hexdigest()[:12], '' if write else '  [print only; --write edits the file]'))
    if not write: return 0
    stamp = datetime.datetime.now(datetime.timezone.utc).strftime('%Y%m%dT%H%M%SZ'); bk = ROOT / 'Art/Playtest308/Pacing/Backups' / ('%s-%s' % (tag, stamp)); bk.mkdir(parents=True, exist_ok=True)
    for f, new, raw in plan: (bk / f.name).write_bytes(raw); f.write_bytes(new)
    print('  written; backup %s' % bk)
    return 0


def cmd_grass_unlock(a):
    print('grass-unlock (F9 a): grass.apply_enabled false -> true in the deployed seat data. Only after the read-only diag (base / nograss) showed that the grass hiding the pile and the altar legs goes away with the field off.')
    return _patch_live([(LIVE['seat'], [('"apply_enabled": false,', '"apply_enabled": true,')])], a.write, 'fix308-grass-unlock')


def cmd_f9_fallback(a):
    D = load_data(a.data_dir); rep = dry_scene('main', D, load_grass(D)); fbk = rep.nums.get('F9.fallback')
    if not fbk: print('REFUSED: the dry found no fallback place (see the F9 (b) line)'); return 2
    fb = D['fix']['f9']['fallback']; H = S.Height(ROOT / D['fix']['height']); lg = next(r for r in D['relayout']['point_moves'] if r['id'] == 'logger')
    pile = (round(fbk[0] + fb['pile_offset'][0], 1), round(fbk[1] + fb['pile_offset'][1], 1)); fw = D['seat']['firewood']
    print('f9-fallback (F9 b): woodcutter (%.1f, %.1f) -> (%.1f, %.1f), pile centre %s -> %s; the two grass zones of the old place go off. Afterwards: content-apply x3, scene-apply x3, NpcJobs306 author x3, ContentSeat308 revert:<scene>:D5 then apply:<scene>:D5 (DEPLOY_PLAN_fix.md 9-b).' % (lg['x'], lg['z'], fbk[0], fbk[1], fw['centre_xz'], list(pile)))
    e1 = [('"x": %s, "z": %s, "pin": true, "max_slope": 12, "yaw": 257.5, "y_offline": %s,' % (lg['x'], lg['z'], lg['y_offline']), '"x": %s, "z": %s, "pin": true, "max_slope": 12, "yaw": 257.5, "y_offline": %.2f,' % (fbk[0], fbk[1], H(fbk[0], fbk[1])))]
    e2 = [('"centre_xz": [%s, %s], "centre_note"' % (fw['centre_xz'][0], fw['centre_xz'][1]), '"centre_xz": [%s, %s], "centre_note"' % (pile[0], pile[1])),
          ('"logger_planned_xz": [%s, %s]' % (fw['logger_planned_xz'][0], fw['logger_planned_xz'][1]), '"logger_planned_xz": [%s, %s]' % (fbk[0], fbk[1])),
          ('{"id": "logger_firewood", "kind": "disc", "enabled": true,', '{"id": "logger_firewood", "kind": "disc", "enabled": false,'),
          ('{"id": "logger", "kind": "disc", "enabled": true,', '{"id": "logger", "kind": "disc", "enabled": false,')]
    return _patch_live([(LIVE['relayout'], e1), (LIVE['seat'], e2)], a.write, 'fix308-f9-fallback')


def main():
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
    ap = argparse.ArgumentParser(); ap.add_argument('cmd', choices=['dry', 'mutations', 'stills', 'grass-unlock', 'f9-fallback'])
    ap.add_argument('--scene', default='all'); ap.add_argument('--data-dir'); ap.add_argument('--deployed', action='store_true', help='dry: read the deployed data files (CliffBoundary308) instead of the stage copies'); ap.add_argument('--out'); ap.add_argument('--mutate'); ap.add_argument('--write', action='store_true')
    a = ap.parse_args()
    return {'dry': cmd_dry, 'mutations': cmd_mutations, 'stills': cmd_stills, 'grass-unlock': cmd_grass_unlock, 'f9-fallback': cmd_f9_fallback}[a.cmd](a)


if __name__ == '__main__':
    sys.exit(main())
