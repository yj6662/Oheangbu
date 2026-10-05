# -*- coding: utf-8 -*-
"""Content308 relayout offline dry run (D308-16; Art/Playtest308/Relayout/relayout308.json 308.relayout.2).
Pure Python: scene YAML + the content / layout / campaign assets + the stage-1b height field + the data files.
Nothing in the Unity project is touched and the editor queue is never used.

  python Tools/Art/content308_relayout_dry.py dry [--scene main|folk298|arch296|all] [--data <content308_relayout.json>]
        [--scene-data <content308_scene.json>] [--height <height .bytes>] [--out <dir>]
      per op (D01 D02 M01 A01 M02 A02 M04 M06 M07 M09 M11 M12 M13 M14 A07 + the T9 follow-up data): what moves from where to
      where, the ground height and slope at the target, the distance to the roads / cliff lines / the long wall / other
      colliders / tree trunks, and the design's post-conditions as numbers ("num <op> <name> <value> [ok|FAIL|info]").
      A failed post-condition is listed under FAILED and the exit code is 2. Rows a scene does not have (fox / agwi in #296)
      are "skipped (not in this scene)", never a failure. With --scene all the per-op numbers of the three scenes are compared.
      -> Art/Playtest308/Pacing/relayout308_dry_<alias>.txt (+ .json)
  python Tools/Art/content308_relayout_dry.py follow-data [--write]
      T9: the two data files that follow the agwi move (content308_keepout.json rows, wall_1b/jangseong308.json limits.agwiX / Z).
      Without --write it prints the change; with --write it copies each file to Art/Playtest308/Pacing/Backups/relayout308-data-<utc>/
      and edits only those lines (text edit; the rest of the file keeps its bytes). Run it at deploy time, after the wall workflow.

Data: the live Art/World/Compact/Rebuild/CliffBoundary308/content308_relayout.json when it exists, else the stage copy
Tools/Unity/Stage308_relayout/Data/ (same for content308_scene.json: the stage copy is the one that carries the relayout rows).
Limits [O]: terrain = the 4 m height grid (bilinear), up to ~0.1 m off the editor's colliders and 3.2-3.7 m off around the first
inspection (P1); colliders = box / sphere / capsule of the scene YAML (prefab-internal and mesh colliders have no radius here);
trunks = sheet rows of Tree / Shrub prototypes; sight = terrain only. The editor's plan decides; this is the prediction."""
import argparse, datetime, hashlib, json, math, os, re, shutil, sys
os.environ.setdefault('OMP_NUM_THREADS', '4'); os.environ.setdefault('OPENBLAS_NUM_THREADS', '4'); os.environ.setdefault('MKL_NUM_THREADS', '4')
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import buildingaudit308_offline as B
import contentseat308_dry as S

ROOT = Path(__file__).resolve().parents[2]
PROJECT = ROOT / 'Oheangbu'
CB = ROOT / 'Art/World/Compact/Rebuild/CliffBoundary308'
STAGE = ROOT / 'Tools/Unity/Stage308_relayout/Data'
OUT = ROOT / 'Art/Playtest308/Pacing'
SCENES = S.SCENES
CONTENT = S.CONTENT
LAYOUT = {
    'arch296': 'Assets/_Project/Art/World/Architecture296/Data/211377a1cf4052f47905e2d4c976f7c4_566363b0e71d6e5499a8f6599e450127_782f53e1c87540e449a7951bb733ff7a_WorldLayout.asset',
    'folk298': 'Assets/_Project/Art/Characters/Folklore298/Data/WorldLayout298.asset',
    'main': 'Assets/_Project/Scenes/World/Main/WorldLayout_Main.asset',
}
CAMPAIGN = 'Assets/_Project/Art/World/Architecture296/Data/85ebc15b71d242841913ba8297c7b933_af510ae16a71e3d429dc77075aeb7371_839cb9257ad263442a19c248dffabd22_Campaign.asset'
ORDER = ['arch296', 'folk298', 'main']
RUN_GAP = 40.0
KEEP_NEAR = 12.0
NEW_RUN0 = (3031.875, 2803.0)


def pick(name):
    live = CB / name
    return live if live.exists() else STAGE / name


def load_asset(path):
    import yaml
    t = Path(path).read_text(encoding='utf-8')
    t = re.sub(r'^%.*\n', '', t, flags=re.M); t = re.sub(r'^--- !u!\d+ &\d+.*\n', '', t, flags=re.M)
    return yaml.safe_load(t)['MonoBehaviour']


def v2(p): return (float(p['x']), float(p['z']))
def d2(a, b): return math.hypot(a[0] - b[0], a[1] - b[1])
def yaw_to(a, b): return math.degrees(math.atan2(b[0] - a[0], b[1] - a[1])) % 360.0
def f1(v): return '%.1f' % v
def f2(v): return '%.2f' % v
def xz(p): return '(%.1f, %.1f)' % (p[0], p[1])


def seg_d(p, a, b): return S.seg_dist(p, a, b)
def poly_d(p, pl): return min(seg_d(p, a, b) for a, b in zip(pl, pl[1:]))


def resample(poly, step):
    """Content308.Resample308: every vertex stays, each segment is cut into ceil(len / step) equal parts."""
    o = [poly[0]]
    for a, b in zip(poly, poly[1:]):
        n = max(1, int(math.ceil(d2(a, b) / step - 1e-9)))
        for k in range(1, n + 1): o.append((a[0] + (b[0] - a[0]) * k / n, a[1] + (b[1] - a[1]) * k / n))
    return o


def tail(poly, metres):
    o = [poly[-1]]; left = metres
    for i in range(len(poly) - 1, 0, -1):
        if left <= 0: break
        seg = d2(poly[i], poly[i - 1])
        if seg >= left:
            t = 0 if seg < 1e-5 else left / seg
            o.append((poly[i][0] + (poly[i - 1][0] - poly[i][0]) * t, poly[i][1] + (poly[i - 1][1] - poly[i][1]) * t)); left = 0
        else:
            o.append(poly[i - 1]); left -= seg
    o.reverse(); return o if len(o) >= 2 else poly


def runs(path):
    out = []; cur = []
    for p in path:
        if cur and d2(cur[-1], p) > RUN_GAP: out.append(cur); cur = []
        cur.append(p)
    if cur: out.append(cur)
    return out


# ---------------------------------------------------------------------------------------------- world model

class World:
    """What one scene's dry run reads: height, reach, roads, cliff lines, wall, colliders, trunks."""

    def __init__(self, alias, data, height):
        import numpy as np
        self.np = np; self.alias = alias; dry = data['dry']
        self.H = S.Height(height)
        self.Hn = np.frombuffer(Path(height).read_bytes(), dtype='<f4').reshape(S.H_ROWS, S.H_COLS)
        self.win = dry['slope_window_m']; self.foot = dry['footprint_m']; self.eye = dry['eye_m']
        self.reach_arr = np.load(ROOT / dry['reach'])
        seg = json.loads((ROOT / dry['cliff_plan']).read_text(encoding='utf-8'))
        self.cliffs = [[tuple(p[:2]) for p in s['polyline']] for s in seg['segments'] if len(s.get('polyline') or []) >= 2]
        wall = json.loads((ROOT / dry['wall']).read_text(encoding='utf-8'))
        self.walls = [w for w in (wall['wings']['west'].get('built'), wall['wings']['east'].get('built') or seg['gate_wall']['east']['line']) if w]
        self.walls = [[tuple(p[:2]) for p in w] for w in self.walls]
        self.layout = load_asset(PROJECT / LAYOUT[alias])
        self.routes = {}; self.width = {}
        for r in self.layout['Routes']:
            b = r.get('Bends') or []
            if len(b) >= 2: self.routes[r['Id']] = [(float(p['x']), float(p['y'])) for p in b]; self.width[r['Id']] = float(r['Width'])
        self.places = {p['Id']: p for p in self.layout['Places']}
        self.realms = [(r['Id'], [(float(p['x']), float(p['y'])) for p in r['Polygon']]) for r in self.layout['Realms']]
        self.sk = S.SceneK(PROJECT / SCENES[alias])
        self.cols = self._colliders(dry['skip_collider_roots'])
        self.sheets = dry['trunk_sheets']; self._trunks = {}

    # terrain
    def h(self, x, z): return self.H(x, z)

    def slope(self, x, z):
        d = self.win
        gx = (self.h(x + d, z) - self.h(x - d, z)) / (2 * d); gz = (self.h(x, z + d) - self.h(x, z - d)) / (2 * d)
        return math.degrees(math.atan(math.hypot(gx, gz)))

    def slope_max(self, x, z, r=None):
        r = self.foot if r is None else r
        return max(self.slope(x + dx, z + dz) for dx in (-r, 0, r) for dz in (-r, 0, r))

    def relief(self, x, z, r=None):
        r = self.foot if r is None else r
        v = [self.h(x + dx, z + dz) for dx in (-r, 0, r) for dz in (-r, 0, r)]
        return max(v) - min(v)

    def reach9(self, x, z):
        c, r = int(round(x / 4)), int(round(z / 4))
        return bool(self.reach_arr[r - 1:r + 2, c - 1:c + 2].all())

    def clearance(self, a, b, hb, step=1.0):
        """min over the ray of (ray height - ground); > 0 = terrain does not hide b (ground + hb) from the eye at a."""
        ya = self.h(*a) + self.eye; yb = self.h(*b) + hb; L = d2(a, b); n = max(2, int(L / step))
        return min(ya + (yb - ya) * i / n - self.h(a[0] + (b[0] - a[0]) * i / n, a[1] + (b[1] - a[1]) * i / n) for i in range(1, n))

    # lines
    def road(self, p, only=None):
        best = (float('inf'), '-', 0.0)
        for rid, pl in self.routes.items():
            if only is not None and rid not in only: continue
            d = poly_d(p, pl)
            if d < best[0]: best = (d, rid, self.width[rid])
        return best

    def cliff(self, p): return min(poly_d(p, pl) for pl in self.cliffs)
    def wall(self, p): return min(poly_d(p, pl) for pl in self.walls)

    def realm(self, p):
        for rid, poly in self.realms:
            inside = False; j = len(poly) - 1
            for i in range(len(poly)):
                if (poly[i][1] > p[1]) != (poly[j][1] > p[1]) and p[0] < (poly[j][0] - poly[i][0]) * (p[1] - poly[i][1]) / (poly[j][1] - poly[i][1]) + poly[i][0]: inside = not inside
                j = i
            if inside: return rid
        return None

    # colliders of the scene YAML (non-trigger, enabled, active; box / sphere / capsule get a circumscribed XZ radius)
    def _colliders(self, skip_roots):
        sc = self.sk.sc; HDR = re.compile(r'^--- !u!(\d+) &(-?\d+)'); FID = re.compile(r'fileID: (-?\d+)')
        V3 = re.compile(r'\{x: ([-\d.e]+), y: ([-\d.e]+), z: ([-\d.e]+)\}')
        cols = []; c = None
        for line in B.read_scene_lines(PROJECT / SCENES[self.alias]):
            m = HDR.match(line)
            if m:
                if c: cols.append(c)
                cls = int(m.group(1))
                c = {'cls': cls, 'go': None, 'size': None, 'center': (0, 0, 0), 'radius': None, 'height': None, 'on': True, 'trigger': False} if cls in (64, 65, 135, 136) else None
                continue
            if c is None: continue
            s = line.rstrip('\n')
            if s.startswith('  m_GameObject: '): c['go'] = FID.search(s).group(1)
            elif s.startswith('  m_Size: '):
                v = V3.search(s); c['size'] = tuple(float(x) for x in v.groups()) if v else None
            elif s.startswith('  m_Center: '):
                v = V3.search(s); c['center'] = tuple(float(x) for x in v.groups()) if v else (0, 0, 0)
            elif s.startswith('  m_Radius: '): c['radius'] = float(s.split(': ')[1])
            elif s.startswith('  m_Height: '): c['height'] = float(s.split(': ')[1])
            elif s.startswith('  m_Enabled: '): c['on'] = s.endswith('1')
            elif s.startswith('  m_IsTrigger: '): c['trigger'] = s.endswith('1')
        if c: cols.append(c)
        skip = tuple(skip_roots); out = []

        def active(tid):
            k = 0
            while tid in sc.nodes and k < 200:
                if not sc.nodes[tid].active: return False
                tid = sc.nodes[tid].father; k += 1
            return True
        for c in cols:
            tid = sc.go_tr.get(c['go'])
            if tid is None or not c['on'] or c['trigger'] or not active(tid): continue
            path = sc.path(tid)
            if path.startswith(skip): continue
            pos, rot, scl = sc.world(tid)
            ctr = B.qrot(rot, (c['center'][0] * scl[0], c['center'][1] * scl[1], c['center'][2] * scl[2]))
            wx, wz = pos[0] + ctr[0], pos[2] + ctr[2]
            if not (1600 < wx < 3900 and 1600 < wz < 3800): continue
            if c['cls'] == 65 and c['size']:
                r = 0.5 * math.sqrt(sum((c['size'][i] * scl[i]) ** 2 for i in range(3)))
            elif c['cls'] in (135, 136) and c['radius'] is not None:
                r = abs(c['radius']) * max(abs(scl[0]), abs(scl[2]))
                if c['cls'] == 136 and c['height'] and abs(rot[0]) + abs(rot[2]) > 0.2: r = max(r, 0.5 * abs(c['height']) * max(abs(v) for v in scl))
            else:
                r = 0.0   # mesh collider: origin only
            out.append((wx, wz, r, path, c['cls']))
        return out

    def col_gap(self, p, ignore=()):
        best = (float('inf'), '-')
        for (x, z, r, path, _cls) in self.cols:
            if ignore and any(g in path for g in ignore): continue
            d = math.hypot(x - p[0], z - p[1]) - r
            if d < best[0]: best = (d, path)
        return best

    def trunk(self, p, reach=30.0):
        key = (int(p[0] // 40), int(p[1] // 40))
        if key not in self._trunks:
            self._trunks[key] = S.read_trunks((p[0] - 60, p[0] + 60, p[1] - 60, p[1] + 60), self.sheets)
        rows = self._trunks[key]
        return min([math.hypot(r[0] - p[0], r[2] - p[1]) for r in rows] or [float('inf')])

    def site(self, p, ignore=()):
        rd, rid, rw = self.road(p); cg, cp = self.col_gap(p, ignore)
        return {'y': self.h(*p), 'slope': self.slope(*p), 'slope_max': self.slope_max(*p), 'relief': self.relief(*p), 'reach': self.reach9(*p),
                'cliff': self.cliff(p), 'wall': self.wall(p), 'road': rd, 'road_id': rid, 'road_w': rw, 'trunk': self.trunk(p), 'col': cg, 'col_path': cp}


# ---------------------------------------------------------------------------------------------- report

class Rep:
    def __init__(self):
        self.lines = []; self.failed = []; self.ops = {}; self.cur = None; self.encounters = set()

    def op(self, op, title):
        self.cur = op; self.ops.setdefault(op, []); self.lines.append(''); self.lines.append('== %s  %s' % (op, title))

    def say(self, s): self.lines.append('  ' + s)

    def num(self, name, value, ok=None, note='', dep=None):
        """dep = an encounter id the number depends on: scenes without that encounter are not compared on it."""
        tag = 'info' if ok is None else 'ok' if ok else 'FAIL'
        self.lines.append('  num %s %s %s [%s]%s' % (self.cur, name, value, tag, (' ' + note) if note else ''))
        self.ops[self.cur].append((name, str(value), tag, dep))
        if ok is False: self.failed.append('%s %s = %s%s' % (self.cur, name, value, (' (' + note + ')') if note else ''))

    def site(self, w, p, label='target', ignore=()):
        s = w.site(p, ignore)
        self.say('%s %s  y %.2f [O height_p1b]  slope %.1f° (4 m window) / footprint max %.1f° / relief %.2f m' % (label, xz(p), s['y'], s['slope'], s['slope_max'], s['relief']))
        self.say('  road centre %.1f m (%s, width %.1f)  cliff line %.0f m  long wall %.0f m  collider %.1f m (%s)  trunk %s m  EA reach 3x3 %s' % (
            s['road'], s['road_id'], s['road_w'], s['cliff'], s['wall'], s['col'], s['col_path'][-70:], ('%.1f' % s['trunk']) if s['trunk'] < 1e8 else '> 60', s['reach']))
        return s


def keepout(rep, w, dry, s, radius):
    """Content308 keepout prediction: the cliff line and the long wall stay outside the item's radius + the tool's tolerance."""
    rep.num('ea_reach', s['reach'], bool(s['reach']))
    rep.num('cliff_line_m', f1(s['cliff']), s['cliff'] >= dry['cliff_min_m'] + radius, '>= %d + keep-out %d' % (dry['cliff_min_m'], radius))
    rep.num('long_wall_m', f1(s['wall']), s['wall'] >= dry['wall_min_m'] + radius, '>= %d + keep-out %d' % (dry['wall_min_m'], radius))
    rep.num('ground_gap_m', '0.00', None, 'y is taken from the ground by the tool; the editor measures |y - physical ground| <= 0.16')


# ---------------------------------------------------------------------------------------------- the dry run of one scene

def dry_scene(alias, data, scene_data, height):
    dry = data['dry']; rules = data['rules']; off = set(data.get('groups_off') or [])
    w = World(alias, data, height); rep = Rep()
    content = load_asset(PROJECT / CONTENT[alias]); campaign = load_asset(PROJECT / CAMPAIGN)
    def on(row): return row.get('enabled', True) is not False and (row.get('group') or '') not in off
    def srow(x): return bool(x.get('enabled')) and (x.get('group') or '') not in off   # scene-data row the pass handles
    pts = {p['Id']: p for p in content['Points']}; cps = {c['Id']: c for c in content['Checkpoints']}; enc = {e['Id']: e for e in content['Encounters']}
    rep.encounters = set(enc)
    rep.lines.append('relayout dry  %s  scene sha %s  content sha %s  layout sha %s' % (alias, w.sk.sha[:12], S.sha(PROJECT / CONTENT[alias])[:12], S.sha(PROJECT / LAYOUT[alias])[:12]))
    rep.lines.append('height %s sha %s' % (Path(height).name, w.H.sha[:12]))

    # ---- the after state (what content-apply would leave): XZ from the data, y from the height field
    A = {'pt': {k: v2(p['Position']) for k, p in pts.items()}, 'feet': {k: v2(c['Feet']) for k, c in cps.items()}, 'yaw': {k: float(c['Yaw']) for k, c in cps.items()},
         'enc': {k: v2(e['Feet']) for k, e in enc.items()}}
    before = json.loads(json.dumps(A))
    rest_rows = {r['id']: r for r in data.get('rests', []) if on(r)}
    point_rows = {r['id']: r for r in data.get('points', []) if on(r)}
    move_rows = {r['id']: r for r in data.get('point_moves', []) if on(r)}
    enc_rows = {r['id']: r for r in data.get('encounter_moves', []) if on(r)}
    esc_rows = {r['id']: r for r in data.get('rests_escort', []) if on(r)}
    for i, r in rest_rows.items():
        A['pt'][i] = (r['x'], r['z'])
        if 'feet' in r: A['feet'][i] = (r['feet']['x'], r['feet']['z']); A['yaw'][i] = r['feet'].get('yaw', yaw_to((r['x'], r['z']), (r['feet']['x'], r['feet']['z'])))
    for i, r in point_rows.items():
        if i in A['pt']:
            far = d2(A['pt'][i], (r['x'], r['z']))
            if far > KEEP_NEAR or (r.get('force') and far > rules['force_tol_m']): A['pt'][i] = (r['x'], r['z'])
    for i, r in move_rows.items():
        if i in A['pt']: A['pt'][i] = (r['x'], r['z'])
    for i, r in enc_rows.items():
        if i in A['enc'] and d2(A['enc'][i], (r['x'], r['z'])) > 3.0: A['enc'][i] = (r['x'], r['z'])
    for i, r in esc_rows.items():
        if i in A['pt'] and i in A['feet']: A['pt'][i] = (r['x'], r['z']); A['feet'][i] = (r['feet']['x'], r['feet']['z']); A['yaw'][i] = r['feet']['yaw']
    reach_of = {i: float(e['Detection']) + float(e['Leash']) + dry['enemy_pad_m'] for i, e in enc.items()}

    def margin(feet):
        """the worst (smallest) margin of a rest's feet against every encounter of the after state."""
        return min(((d2(feet, p) - reach_of[i], i, d2(feet, p)) for i, p in A['enc'].items()), default=(float('inf'), '-', 0.0))

    # ---- D01 paths
    pd = data['paths']; rep.op('D01', 'MainPath / BranchPath from the layout roads (paths-regen)')
    if not on(pd): rep.say('paths are off in data; skipped')
    else:
        missing = [l['route'] for l in pd['main_legs'] + pd['branch_chain'] if l['route'] not in w.routes]
        rep.num('layout_roads_missing', len(missing), len(missing) == 0, ', '.join(missing))

        def build(legs):
            o = []
            for l in legs:
                if l['route'] not in w.routes: continue
                poly = list(w.routes[l['route']])
                if l.get('reverse'): poly.reverse()
                if 'tail_m' in l: poly = tail(poly, l['tail_m'])
                for p in resample(poly, pd['step_m']):
                    if o and d2(o[-1], p) < pd['join_dedupe_m']: continue
                    o.append(p)
            return o
        main_old = [v2(p) for p in content['MainPath']]; branch_old = [v2(p) for p in content['BranchPath']]
        main_new = build(pd['main_legs']); chain = build(pd['branch_chain'])
        chain_roads = [w.routes[l['route']] for l in pd['branch_chain'] if l['route'] in w.routes]
        kept = [r for r in runs(branch_old) if not any(min(poly_d(p, pl) for pl in chain_roads) <= pd['branch_replace_within_m'] for p in r)]
        branch_new = [p for r in kept for p in r] + chain
        allr = list(w.routes.values())

        def off_road(path):
            ds = [min(poly_d(p, pl) for pl in allr) for p in path]
            return sum(1 for d in ds if d > pd['max_off_road_m']), (max(ds) if ds else 0.0)
        ob, wb = off_road(main_old); oa, wa = off_road(main_new)

        def length(path): return sum(d2(a, b) for a, b in zip(path, path[1:]) if d2(a, b) <= RUN_GAP)
        rep.say('MainPath %d points (%.0f m, %d runs) -> %d points (%.0f m, %d runs), step %.1f m' % (len(main_old), length(main_old), len(runs(main_old)), len(main_new), length(main_new), len(runs(main_new)), pd['step_m']))
        rep.num('mainpath_off_road_before', '%d of %d (worst %.1f m)' % (ob, len(main_old), wb))
        rep.num('mainpath_off_road_after', oa, oa == 0, 'points > %.0f m from every layout road' % pd['max_off_road_m'])
        rep.say('BranchPath %d points (%d runs) -> %d points (%d runs): %d old run(s) kept, the chain is %d points' % (len(branch_old), len(runs(branch_old)), len(branch_new), len(runs(branch_new)), len(kept), len(chain)))
        for l in pd['branch_chain']:
            if l['route'] not in w.routes: continue
            ends = (w.routes[l['route']][0], w.routes[l['route']][-1])
            rep.num('branchpath_holds_' + l['route'], all(any(d2(q, e) < 1.0 for q in branch_new) for e in ends), all(any(d2(q, e) < 1.0 for q in branch_new) for e in ends))
        rep.num('ac_c18_run_start_in_branchpath', any(d2(q, NEW_RUN0) < 1.0 for q in branch_new), any(d2(q, NEW_RUN0) < 1.0 for q in branch_new), 'content-apply keeps the run when a BranchPath point is within 1 m of (3031.9, 2803)')
        ys = [w.h(*p) for p in main_new]
        rep.num('mainpath_y_range', '%.1f..%.1f' % (min(ys), max(ys)) if ys else '-', None, 'the tool takes y from the physical ground')
        corridor = [main_new] + runs(branch_new)
        A['corridor'] = corridor

    # ---- D02 campaign destinations
    rep.op('D02', 'campaign Destinations (campaign-apply)')
    main_content = load_asset(PROJECT / CONTENT['main']); mp = {p['Id']: p for p in main_content['Points']}; me = {e['Id']: e for e in main_content['Encounters']}
    stages = {s['Id']: s for s in campaign['Stages']}
    for r in data['campaign']['destinations']:
        if not on(r): continue
        st = stages.get(r['stage'])
        if st is None: rep.num('stage_' + r['stage'], 'missing', False); continue
        src = mp.get(r['point']) if r.get('point') else me.get(r['encounter'])
        if src is None: rep.num(r['stage'] + '_source', 'missing in WorldContent_Main', False); continue
        pos = src['Position'] if r.get('point') else src['Feet']; old = st['Destination']
        gap = abs(float(pos['y']) - w.h(float(pos['x']), float(pos['z'])))
        rep.say('%s (stage %s): (%.1f, %.2f, %.1f) -> (%.1f, %.2f, %.1f) on %s; XZ moves %.1f m, y %.2f m' % (r['stage'], r.get('n', '?'), old['x'], old['y'], old['z'], pos['x'], pos['y'], pos['z'],
                r.get('point') or r.get('encounter'), d2(v2(old), v2(pos)), float(pos['y']) - float(old['y'])))
        tol = scene_data['checks']['dest_ground_tol_m']; seat = None
        if gap > tol:
            # the point may stand on a built surface (a cave floor, a deck): a scene object at the point's own place and height
            p3 = (float(pos['x']), float(pos['y']), float(pos['z']))
            for k, t in w.sk.by_key.items():
                q = w.sk.sc.world(t)[0]
                if abs(q[0] - p3[0]) <= 0.5 and abs(q[2] - p3[2]) <= 0.5 and abs(q[1] - p3[1]) <= tol: seat = k; break
        rep.num(r['stage'] + '_dest_y_minus_height_m', f2(gap), gap <= tol or seat is not None,
                ('<= %.1f (height_p1b; the editor check uses the physical ground)' % tol) if seat is None else 'off the height field, but the point stands on the scene object %s at the same height (a built surface; the editor ground decides)' % seat)
    sig = campaign_sig(campaign)
    rep.num('campaign_gate_signature_sha', hashlib.sha256(sig.encode('utf-8')).hexdigest()[:12], None, '%d stages: order, prerequisites, Optional, rewards, facts, triggers - this edit writes Destination only' % len(campaign['Stages']))
    for line in sig.split('\n'):
        if line: rep.say('  ' + line)

    cliff_keep = {'rest': 30, 'point': 10, 'enc': None}

    def enc_move(op, i, title, extra):
        rep.op(op, title)
        r = enc_rows.get(i)
        if r is None: rep.say('row off in data; skipped'); return
        if i not in enc: rep.say('skipped (not in this scene): encounter %s is not in this content' % i); return
        to = (r['x'], r['z']); rep.say('encounter %s feet %s -> %s (%.1f m), yaw -> %.1f' % (i, xz(before['enc'][i]), xz(to), d2(before['enc'][i], to), r.get('yaw', float('nan'))))
        s = rep.site(w, to); keepout(rep, w, dry, s, int(reach_of[i]))
        rep.num('y_offline_vs_data', '%.2f vs %.2f' % (s['y'], r.get('y_offline', float('nan'))), abs(s['y'] - r['y_offline']) <= 0.05 if 'y_offline' in r else None)
        rep.num('navmesh_within_2m', 'editor (P2)', None, 'no NavMesh offline: scene-check after the shared bake decides; Play stays off until then')
        extra(to, r)

    def m01(to, r):
        rep.num('dist_to_mine_beast/1_m', f1(d2(to, A['enc']['mine_beast/1'])), d2(to, A['enc']['mine_beast/1']) >= reach_of['mine_beast/1'], '>= %.0f' % reach_of['mine_beast/1'])
        if 'entry_rest308' in A['feet']: rep.num('dist_to_entry_rest308_feet_m', f1(d2(to, A['feet']['entry_rest308'])), d2(to, A['feet']['entry_rest308']) >= reach_of['mine_fire/0'], '>= %.0f' % reach_of['mine_fire/0'])
        rep.num('road_centre_m', f1(w.road(to)[0]))
    enc_move('M01', 'mine_fire/0', 'the mine ranged enemy to the road bend', m01)

    def rest(op, i, title, extra):
        rep.op(op, title)
        r = rest_rows.get(i)
        if r is None: rep.say('row off in data; skipped'); return
        to = (r['x'], r['z']); feet = (r['feet']['x'], r['feet']['z'])
        rep.say('rest %s %s -> %s; feet %s -> %s yaw %.1f; altar front yaw %.1f' % (i, xz(before['pt'][i]) if i in before['pt'] else '(new)', xz(to), xz(before['feet'][i]) if i in before['feet'] else '(new)', xz(feet), r['feet']['yaw'], yaw_to(to, feet)))
        own = ('Content308/Rests/' + i,)
        s = rep.site(w, to, 'altar', own); sf = rep.site(w, feet, 'feet', own); keepout(rep, w, dry, s, cliff_keep['rest'])
        rep.num('altar_slope_deg', f1(s['slope']), s['slope'] <= r['max_slope'], '<= max_slope %s (pin: the editor refuses above it; footprint max %.1f)' % (r['max_slope'], s['slope_max']))
        rep.num('feet_slope_deg', f1(sf['slope']), sf['slope'] <= rules['feet_max_slope_deg'], '<= %s' % rules['feet_max_slope_deg'])
        need = scene_data['checks']['feet_road_min_wide_m'] if sf['road_w'] >= scene_data['checks']['wide_road_m'] else scene_data['checks']['feet_road_min_m']
        rep.num('feet_road_centre_m', f1(sf['road']), sf['road'] >= need, '>= %.1f (%s)' % (need, sf['road_id']))
        rep.num('feet_collider_gap_m', f1(sf['col']), sf['col'] - 0.3 >= dry['collider_gap_min_m'], 'capsule 0.3 m + gap >= %.1f [O YAML colliders]' % dry['collider_gap_min_m'])
        m, who, dist = margin(feet)
        rep.num('nearest_enemy_margin_m', '%.1f (%s, %.1f m)' % (m, who, dist), m >= 0, '>= 0 (detection + leash + %d)' % dry['enemy_pad_m'], dep='folklore298/agwi' if op in ('M11', 'M12') else None)
        rep.num('y_offline_vs_data', '%.2f vs %.2f' % (s['y'], r.get('y_offline', float('nan'))), abs(s['y'] - r['y_offline']) <= 0.05 if 'y_offline' in r else None)
        rep.num('candle_lights_in_source', len(lights_in(w, 'Finish297_Hwanggyeong/RestAltar')), len(lights_in(w, 'Finish297_Hwanggyeong/RestAltar')) <= 2, '<= 2 (RestAltar clone)')
        for sl in dry['sightlines']:
            if sl['op'] != op: continue
            c = w.clearance(tuple(sl['from']), tuple(sl['to']), sl['target_h'])
            rep.num('terrain_clearance_m ' + sl['what'].replace(' ', '_'), '%.2f (%.0f m)' % (c, d2(sl['from'], sl['to'])), c > 0, '> 0 (terrain only)')
        extra(to, feet, r)
    rest('A01', 'entry_rest308', 'the entry-road shrine (new rest)', lambda to, feet, r: None)

    def point(op, i, title, extra, radius=10):
        rep.op(op, title)
        r = point_rows.get(i)
        if r is None: rep.say('row off in data; skipped'); return None
        if i not in pts: rep.say('skipped (not in this scene): point %s is not in this content' % i); return None
        to = (r['x'], r['z']); rep.say('point %s %s -> %s (%.1f m)%s' % (i, xz(before['pt'][i]), xz(to), d2(before['pt'][i], to), ' force' if r.get('force') else ''))
        rep.num('moves', A['pt'][i] == to, A['pt'][i] == to, 'the keep rule (12 m) must not hold this row')
        s = rep.site(w, to); keepout(rep, w, dry, s, radius)
        rep.num('y_offline_vs_data', '%.2f vs %.2f' % (s['y'], r.get('y_offline', float('nan'))), abs(s['y'] - r['y_offline']) <= 0.05 if 'y_offline' in r else None)
        extra(to, s); return to
    point('M02', 'geumpyo_stele308', 'the 금표 stele point beside its stone', lambda to, s: rep.num('road_centre_m', f1(s['road']), s['road'] >= 3.0, '>= 3.0'))

    def prop(pid, extra):
        row = next((p for p in scene_data['props'] if p['id'] == pid), None)
        if row is None: rep.num('prop_row_' + pid, 'missing in the scene data', False); return None
        if not row.get('enabled') or (row.get('group') or '') in off: rep.say('prop %s: off in data' % pid); return None
        if 'anchor' not in row: rep.num('prop_' + pid + '_anchor', 'missing', False, 'a props row without anchor refuses the whole scene pass'); return None
        a = A['pt'].get(row['anchor'])
        if a is None: rep.say('skipped (not in this scene): anchor %s' % row['anchor']); return None
        at = (a[0] + row['offset'][0], a[1] + row['offset'][1]); rep.say('prop %s at %s (anchor %s + %s) yaw %s from %s' % (pid, xz(at), row['anchor'], row['offset'], row['yaw'], row.get('source_asset') or row.get('source_scene')))
        s = rep.site(w, at, 'prop', ('Content308/Props/',))
        if row.get('solid') and 'corridor' in A:
            cd = min(poly_d(at, r) for r in A['corridor'] if len(r) >= 2)
            rep.num(pid + '_corridor_m', f1(cd), cd >= scene_data['ground']['corridor_clear_m'], '>= %.1f from MainPath / BranchPath (else the pass does not place it)' % scene_data['ground']['corridor_clear_m'])
        if row.get('no_light'):
            g = glow_of(w, row); rep.num(pid + '_light_or_emission', g or 'none', g is None, 'no Light / no emissive material [O prefab / scene YAML]')
        if row.get('source_scene'):
            n = len([k for k in w.sk.by_key if k == row['source_scene']])
            rep.num(pid + '_source_in_scene', n, n == 1)
        extra(at, s, row); return at

    rep.op('A02', 'the 금표 stele stone (props row on)')
    prop('geumpyo_stele308_stone', lambda at, s, row: (
        rep.num('stone_slope_deg', f1(s['slope'])),
        rep.num('stone_to_entry_altar_m', f1(d2(at, A['pt']['entry_rest308'])), d2(at, A['pt']['entry_rest308']) >= dry['stone_altar_min_m'], '>= %s' % dry['stone_altar_min_m']) if 'entry_rest308' in A['pt'] else None,
        rep.num('stone_road_centre_m', f1(s['road'])), keepout(rep, w, dry, s, 10)))

    rep.op('M04', 'the logger to the 금표 주막 road mouth')
    r = move_rows.get('logger')
    if r is None: rep.say('row off in data; skipped')
    elif 'logger' not in pts: rep.say('skipped (not in this scene)')
    else:
        to = (r['x'], r['z']); rep.say('point logger %s -> %s (%.1f m), body yaw -> %.1f (T4)' % (xz(before['pt']['logger']), xz(to), d2(before['pt']['logger'], to), r['yaw']))
        body = w.sk.world('Rebuild_InnPeople/logger')
        if body: rep.say('scene body Rebuild_InnPeople/logger at (%.1f, %.2f, %.1f) yaw %.1f' % (body[0][0], body[0][1], body[0][2], body[1]))
        s = rep.site(w, to, 'target', ('Rebuild_InnPeople/logger', 'Content308/Props/')); keepout(rep, w, dry, s, 10)
        rep.num('slope_deg', f1(s['slope']), s['slope'] <= r['max_slope'], '<= max_slope %s (pin)' % r['max_slope'])
        rep.num('trunk_m', f1(s['trunk']), s['trunk'] >= dry['trunk_min_m'], '>= %.1f' % dry['trunk_min_m'])
        rep.num('collider_gap_m', f1(s['col']), s['col'] - 0.3 >= dry['collider_gap_min_m'], 'capsule 0.3 m + gap >= %.1f; the inn prefab-internal colliders are not in the YAML (P4)' % dry['collider_gap_min_m'])
        rep.num('road_centre_m', '%.1f (%s)' % (s['road'], s['road_id']))
        rep.num('y_offline_vs_data', '%.2f vs %.2f' % (s['y'], r['y_offline']), abs(s['y'] - r['y_offline']) <= 0.05)
        for pid in ('logger_firewood_0', 'logger_firewood_1', 'logger_firewood_2'):
            prop(pid, lambda at, s2, row: rep.num(row['id'] + '_to_logger_m', f1(d2(at, to))))

    point('M06', 'village_empty_house308', 'the empty-house point to the ruin set', lambda to, s: (
        rep.num('collider_gap_m', f1(s['col']), s['col'] >= dry['collider_gap_min_m'], '>= %.1f [O YAML colliders; the editor re-checks]' % dry['collider_gap_min_m']),
        rep.num('slope_deg', f1(s['slope']), s['slope'] <= dry['walk_slope_max_deg'], '<= %s' % dry['walk_slope_max_deg'])))

    def m07(to, feet, r):
        if 'demo_growth_lesson' in A['enc']:
            d = d2(feet, A['enc']['demo_growth_lesson']); rep.num('feet_to_demo_growth_lesson_m', f1(d), d >= reach_of['demo_growth_lesson'], '>= %.0f' % reach_of['demo_growth_lesson'])
        rep.num('altar_from_design_xz_m', f2(d2(to, (3375.5, 2635.3))), d2(to, (3375.5, 2635.3)) <= 0.5, '<= 0.5')
    rest('M07', 'logging_front_rest308', 'the logging-front shrine to the roadside', m07)

    def m09(to, r):
        if 'sanctuary_rest251' in A['feet']: rep.num('dist_to_sanctuary_rest251_feet_m', f1(d2(to, A['feet']['sanctuary_rest251'])), d2(to, A['feet']['sanctuary_rest251']) >= reach_of['folklore298/fox_spirit'], '>= %.0f' % reach_of['folklore298/fox_spirit'])
        if 'cheongryong' in A['enc']: rep.num('dist_to_cheongryong_m', f1(d2(to, A['enc']['cheongryong'])), d2(to, A['enc']['cheongryong']) > dry['cheongryong_field_m'], '> %s (boss field)' % dry['cheongryong_field_m'])
        for cid in ('deep_fork_rest308', 'hunter_inn308'):
            if cid in A['feet']: rep.num('dist_to_%s_feet_m' % cid, f1(d2(to, A['feet'][cid])))
    enc_move('M09', 'folklore298/fox_spirit', 'the fox off the deep fork onto the sanctuary road', m09)

    def escort(op, i, title, extra):
        rep.op(op, title)
        r = esc_rows.get(i)
        if r is None: rep.say('row off in data; skipped'); return
        if i not in pts or i not in cps: rep.say('skipped (not in this scene)'); return
        to = (r['x'], r['z']); feet = (r['feet']['x'], r['feet']['z']); n = i[-1]
        rep.say('escort rest %s: point %s -> %s (%.1f m); checkpoint feet %s -> %s yaw %.1f -> %.1f' % (i, xz(before['pt'][i]), xz(to), d2(before['pt'][i], to), xz(before['feet'][i]), xz(feet), before['yaw'][i], r['feet']['yaw']))
        stop = 'CapitalEscort252/checkpoint_' + n
        for node in ('Checkpoint', i, 'Interaction', 'Parking', 'CompanionWait', 'CargoWait'):
            wd = w.sk.world(stop + '/' + node)
            if wd: rep.say('scene node %s/%s at (%.2f, %.2f, %.2f) yaw %.1f' % (stop, node, wd[0][0], wd[0][1], wd[0][2], wd[1]))
        ck = w.sk.world(stop + '/Checkpoint'); bench = w.sk.world(stop + '/' + i)
        rep.num('scene_stop_nodes_found', bool(ck and bench), bool(ck and bench), stop + '/Checkpoint and /' + i)
        if ck: rep.num('checkpoint_node_moves_m', f2(d2((ck[0][0], ck[0][2]), feet)), None, 'the node goes onto the content checkpoint (Escort303 audit A1: <= 0.01 m)')
        if bench: rep.num('bench_node_moves_m', f2(d2((bench[0][0], bench[0][2]), to)), None, 'node.offset %s, lift %.2f m' % (r['node']['offset'], r['node']['lift_m']))
        own = (stop + '/' + i, 'Content308/Rests/' + i)
        s = rep.site(w, to, 'altar', own); sf = rep.site(w, feet, 'feet', own); keepout(rep, w, dry, s, cliff_keep['rest'])
        rep.num('altar_slope_deg', f1(s['slope']), s['slope'] <= r['max_slope'], '<= max_slope %s (footprint max %.1f)' % (r['max_slope'], s['slope_max']))
        need = scene_data['checks']['feet_road_min_wide_m'] if sf['road_w'] >= scene_data['checks']['wide_road_m'] else scene_data['checks']['feet_road_min_m']
        rep.num('feet_road_centre_m', f1(sf['road']), sf['road'] >= need, '>= %.1f (%s, width %.1f)' % (need, sf['road_id'], sf['road_w']))
        rep.num('feet_collider_gap_m', f1(sf['col']), sf['col'] - 0.3 >= dry['collider_gap_min_m'], 'capsule 0.3 m + gap >= %.1f' % dry['collider_gap_min_m'])
        m, who, dist = margin(feet); rep.num('nearest_enemy_margin_m', '%.1f (%s, %.1f m)' % (m, who, dist), m >= 0, '>= 0', dep='folklore298/agwi')
        p = pts[i]; c = cps[i]; keep = r['keep']
        same = (p.get('Prompt') or '') == keep['prompt'] and (p.get('Text') or '') == keep['text'] and abs(float(p['Radius']) - keep['radius']) < 1e-4 and (c.get('Label') or '') == keep['label'] and bool(c.get('Shop')) == keep['shop']
        rep.num('prompt_text_radius_label_as_keep', same, same, "prompt '%s' text '%s' radius %s label '%s' (never written)" % (p.get('Prompt') or '', p.get('Text') or '', p['Radius'], c.get('Label')))
        if ck: rep.num('existing_objects_y_vs_height_m', '%.2f vs %.2f' % (ck[0][1], w.h(ck[0][0], ck[0][2])), None, 'P1: the editor ground decides (objects stand %.1f m off the height field here)' % (w.h(ck[0][0], ck[0][2]) - ck[0][1]))
        rep.num('scene_rests_row', any(x['id'] == i and srow(x) for x in scene_data['rests']), any(x['id'] == i and srow(x) for x in scene_data['rests']), 'the altar is the rests[] row of the same id')
        sg = next((x.get('group') for x in scene_data['rests'] if x['id'] == i), None)
        rep.num('scene_rests_row_group', sg, sg == r.get('group'), "== the rests_escort row's group '%s' (else groups_off would not stop the altar + two candles from being placed again)" % r.get('group'))
        extra(to, feet, r, stop)

    def m11(to, feet, r, stop):
        desk = w.sk.world(stop + '/InspectionDesk252')
        if desk: d = d2(to, (desk[0][0], desk[0][2])); rep.num('altar_to_inspection_desk_m', f1(d), d >= dry['desk_min_m'], '>= %s' % dry['desk_min_m'])
    escort('M11', 'road_rest_1', 'the escort rest behind the first inspection (bundle)', m11)

    def m12(to, feet, r, stop):
        for node in ('Parking', 'CompanionWait', 'CargoWait'):
            wd = w.sk.world(stop + '/' + node)
            if wd: d = d2(to, (wd[0][0], wd[0][2])); rep.num('altar_to_%s_m' % node, f1(d), d >= dry['stop_nodes_min_m'], '>= %s' % dry['stop_nodes_min_m'])
        if 'folklore298/agwi' in A['enc']: rep.num('feet_to_agwi_m', f1(d2(feet, A['enc']['folklore298/agwi'])), d2(feet, A['enc']['folklore298/agwi']) >= dry['agwi_rest_m'][0], '>= %s' % dry['agwi_rest_m'][0], dep='folklore298/agwi')
        rep.num('feet_from_old_feet_m', f1(d2(feet, before['feet']['road_rest_2'])), d2(feet, before['feet']['road_rest_2']) <= 12, '<= 12 (escort respawn stays by the stop)')
    escort('M12', 'road_rest_2', 'the escort rest at the second inspection (bundle)', m12)

    def m13(to, r):
        escort_roads = ('merchant__road_pass', 'road_pass__inspection_one', 'inspection_one__road_hamlet', 'road_hamlet__inspection_two', 'inspection_two__capital_delivery', 'capital_delivery__south_gate')
        rd = w.road(to, escort_roads)
        rep.num('escort_road_m', '%.1f (%s)' % (rd[0], rd[1]), rd[0] >= dry['agwi_road_min_m'], '>= %s (detection + leash + 10)' % dry['agwi_road_min_m'])
        for n in ('1', '2'):
            wd = w.sk.world('CapitalEscort252/checkpoint_%s/Parking' % n)
            if wd: d = d2(to, (wd[0][0], wd[0][2])); rep.num('parking_%s_m' % n, f1(d), d >= dry['agwi_road_min_m'], '>= %s' % dry['agwi_road_min_m'])
        near = min(((d2(to, f), i) for i, f in A['feet'].items()), default=(float('inf'), '-'))
        rep.num('nearest_rest_feet_m', '%.1f (%s)' % near, dry['agwi_rest_m'][0] <= near[0] <= dry['agwi_rest_m'][1], '%s..%s (AC-C12 revised: the nearest rest place)' % tuple(dry['agwi_rest_m']))
        want = r.get('respawn', -1); rep.num('respawn_on_rest_after', want if want >= 0 else int(enc['folklore298/agwi']['RespawnOnRest']), (want if want >= 0 else int(enc['folklore298/agwi']['RespawnOnRest'])) == 0, '0 (field boss)')
        if 'corridor' in A:
            mp_min = min(d2(p, to) for p in A['corridor'][0]); rep.num('mainpath_nearest_point_m', f1(mp_min), mp_min >= scene_data['checks']['agwi_road_min_m'], '>= %s (checks:content AC-C12)' % scene_data['checks']['agwi_road_min_m'])
        # T9 follow-up data, as they would be after content-apply (layout) and follow-data --write (the two json files)
        place = w.places.get('folklore298/agwi'); row = next((x for x in data.get('layout_places', []) if x['id'] == 'folklore298/agwi' and on(x)), None)
        if place is None: rep.say('layout place folklore298/agwi: not in this layout; skipped')
        elif row is None: rep.num('t9_layout_place_row', 'missing', False)
        else:
            rep.say('layout place folklore298/agwi XZ (%.0f, %.0f) realm %s -> (%.0f, %.0f) realm %s' % (place['XZ']['x'], place['XZ']['y'], place['Realm'], row['x'], row['z'], w.realm((row['x'], row['z']))))
            rep.num('t9_layout_place_follows', d2((row['x'], row['z']), to) < 0.01, d2((row['x'], row['z']), to) < 0.01)
        fd = follow_plan(data)
        rep.num('t9_wall_limits_agwi', '%s -> %s' % (fd['wall_before'], fd['wall_after']), fd['wall_after'] == (to[0], to[1]), 'jangseong308.json limits.agwiX / agwiZ after follow-data')
        rep.num('t9_wall_to_agwi_m', f1(w.wall(to)), w.wall(to) >= fd['agwi_min'], '>= limits.agwiMin %s' % fd['agwi_min'])
        ko = fd['keepout_after'].get('folklore298/agwi')
        rep.num('t9_keepout_row_agwi', ko, ko is not None and abs(ko[0] - to[0]) < 0.06 and abs(ko[1] - to[1]) < 0.06, 'content308_keepout.json after follow-data')
        missing = [i for i in fd['need_rows'] if i not in fd['keepout_after']]
        rep.num('t9_keepout_rows_for_scene_pass', 'missing ' + ', '.join(missing) if missing else 'all present', not missing, 'every rest / actor the scene pass places needs a keep-out row')
    enc_move('M13', 'folklore298/agwi', 'the field boss to the hill east of the second inspection', m13)

    def m14(to, s):
        if 'folklore298/agwi' in A['enc']:
            d = d2(to, A['enc']['folklore298/agwi']); lo, hi = dry['yeomak_agwi_m']
            rep.num('dist_to_agwi_m', f1(d), lo < d <= hi, '> %s (detection) and <= %s' % (lo, hi), dep='folklore298/agwi')
        else: rep.say('agwi is not in this scene: the distance rule is not judged')
    point('M14', 'road_yeomak308', 'the 여막 터 point onto the agwi hill', m14)

    rep.op('A07', 'the 여막 터 foundation stones and the collapsed hut (props rows)')
    for pid in ('road_yeomak308_base_0', 'road_yeomak308_base_1', 'road_yeomak308_base_2'):
        prop(pid, lambda at, s, row: keepout(rep, w, dry, s, 10))
    prop('road_yeomak308_hut', lambda at, s, row: (
        rep.num('hut_ground_slope_deg', f1(s['slope'])), rep.num('hut_footprint_relief_m', f2(s['relief']), None, 'corner gap <= 0.15 m is an editor / still check (the hut mesh size is not in the YAML)'),
        rep.num('hut_colliders_in_source', len([1 for (_, _, _, path, _) in w.cols if path.startswith(row['source_scene'])]), None, 'solid false: it does not block'), keepout(rep, w, dry, s, 10)))

    # ---- every rest of the after state against every encounter of the after state (BUILD_BRIEF 6: all >= 0)
    rep.op('ALL', 'rest feet <-> enemies after the relayout; new sustained lights')
    worst = sorted(((margin(f)[0], i, margin(f)[1]) for i, f in A['feet'].items() if 1600 < f[0] < 3900 and 1600 < f[1] < 3800))
    # the tool's rule (Content308 keepout): the rests the scene pass places (rests[] of the scene data) are judged; the older rests
    # (boss-front cairns, the cave start) are listed only
    mine = {r['id'] for r in scene_data['rests'] if srow(r)}
    for m, i, who in worst[:8]: rep.say('margin %+.1f m  %s <-> %s%s' % (m, i, who, '' if i in mine else '  (not a scene-pass rest: listed only)'))
    judged = [x for x in worst if x[1] in mine]
    rep.num('worst_scene_pass_rest_enemy_margin_m', '%.1f (%s <-> %s)' % judged[0] if judged else '-', (judged[0][0] >= 0) if judged else None, '>= 0 (detection + leash + %d)' % dry['enemy_pad_m'])
    light_ids = scene_data['checks'].get('relayout_rest_light_ids') or []
    rep.num('relayout_rest_light_ids', ', '.join(light_ids), set(light_ids) == {'entry_rest308', 'road_rest_1', 'road_rest_2'}, 'the rests whose candles scene-check counts (checks.relayout_rest_light_ids)')
    new_shrines = [r['id'] for r in scene_data['rests'] if srow(r) and r['id'] in light_ids and r['id'] in A['pt']]
    per = len(lights_in(w, 'Finish297_Hwanggyeong/RestAltar')); n_light = len(new_shrines) * per
    caps = [r.get('lights_max') for r in scene_data['rests'] if r['id'] in light_ids]
    rep.num('altar_source_lights', per, per == 2 and all(c == 2 for c in caps), '== 2 == lights_max of every relayout rest row (scene-check wants exactly lights_max enabled Lights per relayout rest)')
    want = 6 if not off else 2 * len(new_shrines)
    rep.num('new_sustained_point_lights', n_light, n_light == want, '%d = %s shrines x two candles (%s); stele / stones / hut carry none' % (want, 'three' if not off else 'the switched-on', ', '.join(new_shrines)))
    # T7 data agreement: every scene-data row that names a group names one the relayout data knows, and the relayout-only rest /
    # actor rows carry the group of their relayout row (a row without it could not be switched off by groups_off)
    known = {r.get('group') for k in ('rests', 'points', 'point_moves', 'encounter_moves', 'rests_escort', 'layout_places') for r in data.get(k, []) if r.get('group')}
    named = [(k, x['id'], x['group']) for k, rows_ in (('rests', scene_data['rests']), ('actors.moves', scene_data['actors']['moves']), ('npcs.moves', scene_data['npcs']['moves']), ('props', scene_data['props'])) for x in rows_ if x.get('group')]
    stray = ['%s %s -> %s' % t for t in named if t[2] not in known]
    rep.num('scene_row_groups_known', len(named) - len(stray), not stray, 'of %d scene rows that name a group%s' % (len(named), '' if not stray else ' - unknown: ' + '; '.join(stray)))
    want_g = {r['id']: r.get('group') for r in data.get('rests', []) if r['id'] in light_ids}
    want_g.update({r['id']: r.get('group') for r in data.get('rests_escort', [])})
    for mv in data.get('encounter_moves', []):
        if mv['id'] in ('folklore298/dokkaebi', 'folklore298/agwi'): continue   # #308 base rows of the scene data: they follow the content, group or not
        want_g[mv['id']] = mv.get('group')
    got_g = {x['id']: x.get('group') for x in scene_data['rests'] + scene_data['actors']['moves']}
    wrong = ['%s: scene row %s, relayout row %s' % (i, got_g.get(i), g) for i, g in sorted(want_g.items()) if got_g.get(i) != g]
    rep.num('relayout_scene_rows_grouped', len(want_g) - len(wrong), not wrong, 'of %d relayout-only rest / actor rows carry their relayout group%s' % (len(want_g), '' if not wrong else ' - ' + '; '.join(wrong)))
    if data.get('encounter_adds'): rep.num('encounter_adds_rows', len(data['encounter_adds']), False, 'encounter_adds[] is not implemented: Content308 refuses the file')
    return rep, w


def lights_in(w, key):
    return [k for k in w.sk.subtree_keys(key) if w.sk.comp(k, 108)]


_MATS = None


def mat_index():
    global _MATS
    if _MATS is None:
        _MATS = {}
        for base, _dirs, files in os.walk(PROJECT / 'Assets'):
            for f in files:
                if not f.endswith('.mat.meta'): continue
                try:
                    with open(os.path.join(base, f), encoding='utf-8', errors='replace') as fh:
                        for _ in range(3):
                            line = fh.readline()
                            if line.startswith('guid: '): _MATS[line[6:].strip()] = os.path.join(base, f[:-5]); break
                except OSError: pass
    return _MATS


def mat_glows(path):
    t = Path(path).read_text(encoding='utf-8', errors='replace')
    kw = re.search(r'm_ValidKeywords:(.*?)m_InvalidKeywords', t, re.S); legacy = re.search(r'm_ShaderKeywords: (.*)', t)
    has = ('_EMISSION' in (kw.group(1) if kw else '')) or ('_EMISSION' in (legacy.group(1) if legacy else ''))
    col = re.search(r'- _EmissionColor: \{r: ([-\d.e]+), g: ([-\d.e]+), b: ([-\d.e]+)', t)
    lit = col is not None and max(float(x) for x in col.groups()) > 1e-3
    return has and lit


def glow_of(w, row):
    """None = the source carries no Light component and no emissive material (as far as the YAML shows)."""
    lights = 0; guids = set()
    if row.get('source_asset'):
        t = (PROJECT / row['source_asset']).read_text(encoding='utf-8', errors='replace')
        lights = len(re.findall(r'^--- !u!108 ', t, re.M)); guids = set(re.findall(r'guid: ([0-9a-f]{32}), type: 2', t))
    elif row.get('source_scene'):
        keys = w.sk.subtree_keys(row['source_scene']); lights = sum(1 for k in keys if w.sk.comp(k, 108))
        gos = {w.sk.sc.nodes[w.sk.get(k)].go for k in keys}; cur = None; cls = None; mine = False
        for line in B.read_scene_lines(PROJECT / SCENES[w.alias]):
            m = B.HDR.match(line)
            if m: cls = int(m.group(1)); mine = False; continue
            if cls != 23: continue
            if line.startswith('  m_GameObject: '): mine = B.FILEID.search(line).group(1) in gos
            elif mine and 'guid:' in line and 'type: 2' in line: guids.update(re.findall(r'guid: ([0-9a-f]{32})', line))
    idx = mat_index(); glowing = sorted(Path(idx[g]).name for g in guids if g in idx and mat_glows(idx[g]))
    if lights == 0 and not glowing: return None
    return '%d Light(s)%s' % (lights, (', emissive ' + ', '.join(glowing)) if glowing else '')


def campaign_sig(c):
    def J(a): return '[' + ','.join(a or []) + ']'
    out = ['explicit=%d;initial=%s' % (int(c.get('UseExplicitPrerequisites') or 0), J(c.get('InitialCompletedIds')))]
    for i, s in enumerate(c['Stages']):
        out.append('%d:%s|pre=%s|optional=%d|tongbo=%d|implemented=%d|event=%d|trigger=%s|defeated=%s|facts=%s|grants=%s|act=%s' % (
            i, s['Id'], J(s.get('PrerequisiteIds')), int(s.get('Optional') or 0), int(s.get('TongboReward') or 0), int(s.get('Implemented') or 0), int(s.get('Event') or 0),
            s.get('TriggerId') or '', J(s.get('RequiredDefeatedIds')), J(s.get('RequiredFacts')), J(s.get('GrantedFacts')), s.get('ActId') or ''))
    return '\n'.join(out) + '\n'


# ---------------------------------------------------------------------------------------------- T9 follow-up data

def follow_plan(data):
    """The edits of the two data files that follow the relayout, computed from the live files (nothing written).
    content308_keepout.json is a json.dump(indent=1) file (it round-trips byte for byte), so its rows are edited as JSON;
    jangseong308.json is edited as text (two numbers)."""
    dry = data['dry']; off = set(data.get('groups_off') or [])
    def on(row): return row.get('enabled', True) is not False and (row.get('group') or '') not in off
    h = S.Height(ROOT / dry['height'])
    target = {}   # keep-out id -> (kind, x, z): rests record their respawn feet, points their place, encounters their feet
    for r in data.get('rests', []):
        if on(r): target[r['id']] = ('rest', r['feet']['x'], r['feet']['z'])
    for r in data.get('rests_escort', []):
        if on(r): target[r['id']] = ('rest', r['feet']['x'], r['feet']['z'])
    for r in data.get('points', []) + data.get('point_moves', []):
        if on(r): target[r['id']] = ('point', r['x'], r['z'])
    for r in data.get('encounter_moves', []):
        if on(r): target[r['id']] = ('enc', r['x'], r['z'])
    kf = ROOT / dry['keepout']; text = kf.read_text(encoding='utf-8'); doc = json.loads(text)
    if json.dumps(doc, indent=1, ensure_ascii=False) != text: raise SystemExit('%s is no longer a json.dump(indent=1) file: edit it by hand (rows %s)' % (kf, ', '.join(target)))
    radius = {'rest': 30, 'point': 10, 'enc': 54}; seen = {}; changes = []; adds = []
    for it in doc['items']:
        iid = it.get('id')
        if iid in target and it.get('kind') == target[iid][0]:
            _, x, z = target[iid]; y = round(h(x, z), 1)
            if (it.get('x'), it.get('y'), it.get('z')) != (x, y, z):
                changes.append('%s %s: (%s, %s, %s) -> (%s, %s, %s)' % (it['kind'], iid, it.get('x'), it.get('y'), it.get('z'), x, y, z))
                it['x'] = x; it['y'] = y; it['z'] = z
                it['relayout308'] = 'x / y / z follow the D308-16 relayout; border_m .. wall_m of this row are the values of the old place'
        if iid is not None and it.get('x') is not None: seen[iid] = (it['x'], it['z'])
    first_geo = next((i for i, it in enumerate(doc['items']) if it.get('kind') == 'geo'), len(doc['items']))
    for iid, (kind, x, z) in target.items():
        if iid in seen: continue
        row = {'kind': kind, 'id': iid, 'x': x, 'y': round(h(x, z), 1), 'z': z, 'keepout_m': radius[kind], 'relayout308': 'row added with the D308-16 relayout (the scene pass places this item); distances not measured'}
        doc['items'].insert(first_geo, row); first_geo += 1; adds.append('%s %s (%s, %s, %s) keepout_m %d' % (kind, iid, x, row['y'], z, radius[kind])); seen[iid] = (x, z)
    ktext = json.dumps(doc, indent=1, ensure_ascii=False)
    wf = ROOT / dry['wall']; wt = wf.read_text(encoding='utf-8')
    mx = re.search(r'("agwiX": )([-\d.]+)', wt); mz = re.search(r'("agwiZ": )([-\d.]+)', wt); mm = re.search(r'"agwiMin": ([-\d.]+)', wt)
    agwi = target.get('folklore298/agwi'); wall_before = (float(mx.group(2)), float(mz.group(2))); wall_after = wall_before; wt_new = wt
    if agwi:
        wt_new = wt[:mx.start(2)] + ('%.1f' % agwi[1]) + wt[mx.end(2):]
        mz2 = re.search(r'("agwiZ": )([-\d.]+)', wt_new); wt_new = wt_new[:mz2.start(2)] + ('%.1f' % agwi[2]) + wt_new[mz2.end(2):]
        wall_after = (agwi[1], agwi[2])
    need = [i for i, (k, _, _) in target.items() if k in ('rest', 'enc')]
    return {'keepout_file': kf, 'keepout_text': ktext, 'keepout_changed': changes, 'keepout_added': adds, 'keepout_after': seen, 'keepout_same': ktext == text,
            'wall_file': wf, 'wall_text': wt_new, 'wall_before': wall_before, 'wall_after': wall_after, 'wall_same': wt_new == wt, 'agwi_min': float(mm.group(1)) if mm else 0.0, 'need_rows': need}


def cmd_follow(args, data):
    fd = follow_plan(data)
    print('follow-data (T9)%s' % ('' if args.write else '  [print only; --write edits the files]'))
    print('  %s sha %s' % (fd['keepout_file'], S.sha(fd['keepout_file'])[:12]))
    for c in fd['keepout_changed']: print('    row ' + c)
    for a in fd['keepout_added']: print('    new row ' + a)
    if fd['keepout_same']: print('    no change (already follows the relayout data)')
    print('  %s sha %s' % (fd['wall_file'], S.sha(fd['wall_file'])[:12]))
    print('    limits.agwiX / agwiZ %s -> %s%s' % (fd['wall_before'], fd['wall_after'], '  (no change)' if fd['wall_same'] else ''))
    print('    note: the wall file keeps its measured "agwiDistance"; run the wall check (Tools/Art/jangseong308.py) again after this edit')
    if not args.write: return 0
    json.loads(fd['keepout_text']); json.loads(fd['wall_text'])
    stamp = datetime.datetime.now(datetime.timezone.utc).strftime('%Y%m%dT%H%M%S'); bk = OUT / 'Backups' / ('relayout308-data-' + stamp)
    for f, text, same in ((fd['keepout_file'], fd['keepout_text'], fd['keepout_same']), (fd['wall_file'], fd['wall_text'], fd['wall_same'])):
        if same: continue
        bk.mkdir(parents=True, exist_ok=True); shutil.copy2(f, bk / f.name)
        f.write_bytes(text.encode('utf-8')); print('  wrote %s (backup %s) sha %s' % (f, bk / f.name, S.sha(f)[:12]))
    return 0


# ---------------------------------------------------------------------------------------------- main

def main():
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
    ap = argparse.ArgumentParser(); ap.add_argument('cmd', choices=['dry', 'follow-data'])
    ap.add_argument('--scene', default='all'); ap.add_argument('--data'); ap.add_argument('--scene-data'); ap.add_argument('--height'); ap.add_argument('--out'); ap.add_argument('--write', action='store_true')
    a = ap.parse_args()
    data_file = Path(a.data) if a.data else pick('content308_relayout.json')
    data = json.loads(data_file.read_text(encoding='utf-8'))
    if a.cmd == 'follow-data': return cmd_follow(a, data)
    stage_scene = STAGE / 'content308_scene.json'
    scene_file = Path(a.scene_data) if a.scene_data else (stage_scene if stage_scene.exists() else CB / 'content308_scene.json')
    scene_data = json.loads(scene_file.read_text(encoding='utf-8'))
    height = Path(a.height) if a.height else ROOT / data['dry']['height']
    out = Path(a.out) if a.out else OUT; out.mkdir(parents=True, exist_ok=True)
    aliases = ORDER if a.scene == 'all' else [a.scene]
    head = ['data %s sha %s' % (data_file, S.sha(data_file)[:12]), 'scene data %s sha %s (version %s)' % (scene_file, S.sha(scene_file)[:12], scene_data.get('version')),
            'groups off: %s' % (', '.join(data.get('groups_off') or []) or 'none')]
    results = {}; failed_any = False
    for alias in aliases:
        rep, w = dry_scene(alias, data, scene_data, height)
        text = '\n'.join(head + rep.lines + ['', 'FAILED %d' % len(rep.failed)] + ['  ' + f for f in rep.failed]) + '\n'
        (out / ('relayout308_dry_%s.txt' % alias)).write_text(text, encoding='utf-8')
        (out / ('relayout308_dry_%s.json' % alias)).write_text(json.dumps({'alias': alias, 'scene_sha': w.sk.sha, 'data_sha': S.sha(data_file), 'ops': {k: [list(x[:3]) for x in v] for k, v in rep.ops.items()}, 'failed': rep.failed}, ensure_ascii=False, indent=1), encoding='utf-8')
        print(text); print('-> %s' % (out / ('relayout308_dry_%s.txt' % alias)))
        results[alias] = rep; failed_any = failed_any or bool(rep.failed)
    if len(results) > 1:
        print('\n== the scenes side by side (per-op numbers; a number that depends on an encounter one scene does not have is left out)')
        ops = []
        for rep in results.values():
            for op in rep.ops:
                if op not in ops: ops.append(op)
        for op in ops:
            common = set.intersection(*[rep.encounters for rep in results.values()])
            rows = {al: tuple((n_, v_, t_) for (n_, v_, t_, dep) in rep.ops.get(op, []) if dep is None or dep in common) for al, rep in results.items()}
            have = {al: r for al, r in rows.items() if r}
            if len(set(have.values())) <= 1:
                print('  %s identical in %s%s' % (op, ', '.join(have) or '-', ''.join('; skipped in ' + al for al in rows if al not in have)))
            else:
                ref = next(iter(have.values()))
                for al, r in have.items():
                    diff = [(a, b) for a, b in zip(ref, r) if a != b] + ([('row count', '%d vs %d' % (len(ref), len(r)))] if len(ref) != len(r) else [])
                    if diff: print('  %s DIFFERS in %s: %s' % (op, al, '; '.join('%s -> %s' % (x[0], x[1]) for x in diff[:4])))
    print('\nRESULT %s' % ('FAILED post-conditions: exit 2' if failed_any else 'every offline post-condition holds: exit 0'))
    return 2 if failed_any else 0


if __name__ == '__main__':
    sys.exit(main())
