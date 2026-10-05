# -*- coding: utf-8 -*-
"""#308 D308-16b / D308-16c - offline dry run of the three earth road enemies (relayout308_ext.json XE2-XE4). Read only.

  python Tools/Art/earth308_dry.py [--data <earth308.json>] [--out <dir>]

Reads the earth rows (default Tools/Unity/Stage308_relayout_ext/Data/earth308.json, else the deployed copy), the offline height
field height_p1b, the layout roads (WorldLayout_Main + Architecture296 routes.json), the four reach masks and the rest places:
the live WorldContent_Main checkpoints / Rest points with the BASE relayout rows (content308_relayout.json rests / rests_escort /
encounter_moves / points) laid over them, plus the road inn of the ext design (relayout308_ext.json XI1).
Every number is OFFLINE ([O]): the editor's physical ground and NavMesh decide (Content308.Earth scene-check, QE1).
Exit code 1 when a FAIL row exists. Writes earth308_dry.json / earth308_dry.txt into --out.
"""
import os
os.environ.setdefault('OMP_NUM_THREADS', '4'); os.environ.setdefault('OPENBLAS_NUM_THREADS', '4'); os.environ.setdefault('MKL_NUM_THREADS', '4')
import argparse, json, math, re, sys
from pathlib import Path
import numpy as np

try: sys.stdout.reconfigure(encoding='utf-8')
except Exception: pass
ROOT = Path(__file__).resolve().parents[2]
CB = ROOT / 'Art/World/Compact/Rebuild/CliffBoundary308'
STAGE = ROOT / 'Tools/Unity/Stage308_relayout_ext'
BASE_STAGE = ROOT / 'Tools/Unity/Stage308_relayout'
CELL = 4.0


def load_height():
    h = np.fromfile(CB / 'plan/Stage/height_p1b.bytes', dtype='<f4')
    assert h.size == 1501 * 1001, h.size
    return h.reshape(1501, 1001)


H = load_height()


def tri_cell(x, z):
    c0 = int(math.floor(x / CELL)); r0 = int(math.floor(z / CELL)); u = x / CELL - c0; v = z / CELL - r0
    return H[r0, c0], H[r0, c0 + 1], H[r0 + 1, c0], H[r0 + 1, c0 + 1], u, v


def tri_h(x, z):
    h00, h10, h01, h11, u, v = tri_cell(x, z)
    if u + v <= 1: return float(h00 + (h10 - h00) * u + (h01 - h00) * v)
    return float(h11 + (h01 - h11) * (1 - u) + (h10 - h11) * (1 - v))


def tri_slope(x, z):
    h00, h10, h01, h11, u, v = tri_cell(x, z)
    if u + v <= 1: gx = (h10 - h00) / CELL; gz = (h01 - h00) / CELL
    else: gx = (h11 - h01) / CELL; gz = (h11 - h10) / CELL
    return math.degrees(math.atan(math.hypot(gx, gz)))


def win_slope(x, z, w=4.0):
    gx = (tri_h(x + w / 2, z) - tri_h(x - w / 2, z)) / w; gz = (tri_h(x, z + w / 2) - tri_h(x, z - w / 2)) / w
    return math.degrees(math.atan(math.hypot(gx, gz)))


def footprint(x, z, r=2.0):
    s = []; hs = []
    for i in range(-4, 5):
        for j in range(-4, 5):
            dx = i * r / 4; dz = j * r / 4
            if dx * dx + dz * dz <= r * r + 1e-6: s.append(tri_slope(x + dx, z + dz)); hs.append(tri_h(x + dx, z + dz))
    return max(s), max(hs) - min(hs)


def segd(p, a, b):
    vx = b[0] - a[0]; vz = b[1] - a[1]; l2 = vx * vx + vz * vz
    t = 0 if l2 == 0 else max(0, min(1, ((p[0] - a[0]) * vx + (p[1] - a[1]) * vz) / l2))
    return math.hypot(p[0] - a[0] - t * vx, p[1] - a[1] - t * vz)


def layout_routes():
    txt = (ROOT / 'Oheangbu/Assets/_Project/Scenes/World/Main/WorldLayout_Main.asset').read_text(encoding='utf-8')
    routes = {}; widths = {}; cur = None
    for line in txt[txt.index('\n  Routes:'):].split('\n')[1:]:
        if line.startswith('  - Id: '): cur = line[8:].strip(); routes[cur] = []
        elif line.startswith('    Width:') and cur: widths[cur] = float(line.split(':')[1])
        elif line.startswith('    - {x:') and cur:
            m = re.match(r'\s*- \{x: ([-\d.e]+), y: ([-\d.e]+)\}', line)
            if m: routes[cur].append((float(m.group(1)), float(m.group(2))))
        elif line and not line.startswith('  '): break
    return routes, widths


def generated_routes():
    f = ROOT / 'Art/World/Compact/Rebuild/Architecture296/Generated/routes.json'
    if not f.exists(): return {}
    return {r['id']: [(p['x'], p['z']) for p in r['points']] for r in json.loads(f.read_text(encoding='utf-8'))['routes']}


def road(p, table):
    best = (1e9, None)
    for rid, b in table.items():
        for a, c in zip(b, b[1:]):
            d = segd(p, a, c)
            if d < best[0]: best = (d, rid)
    return best


def content_places():
    """Checkpoint feet, Rest points and encounters of the live WorldContent_Main (YAML text; no Unity)."""
    c = (ROOT / 'Oheangbu/Assets/_Project/Scenes/World/Main/WorldContent_Main.asset').read_text(encoding='utf-8')

    def block(name, nxt):
        a = c.index('\n  %s:' % name); b = c.index('\n  %s:' % nxt, a); return c[a:b]
    enc = {}
    for m in re.finditer(r'- Id: (\S+)\n\s+ContentId: (\S*)\n\s+Feet: \{x: ([-\d.e]+), y: ([-\d.e]+), z: ([-\d.e]+)\}', block('Encounters', 'MainPath')):
        enc[m.group(1)] = (float(m.group(3)), float(m.group(5)))
    cp = {}
    for m in re.finditer(r'- Id: (\S+)\n(?:.*\n)*?\s+Feet: \{x: ([-\d.e]+), y: ([-\d.e]+), z: ([-\d.e]+)\}', block('Checkpoints', 'Encounters')):
        cp[m.group(1)] = (float(m.group(2)), float(m.group(4)))
    rest = {}
    # one match per list entry, anchored at the line start: the old pattern began with "\n", which the body of the entry before had
    # already consumed, so every second Point was skipped (29 of 57 read; fixed 2026-10-05)
    for m in re.finditer(r'^  - Id: (\S+)\n((?:    .*\n)*)', block('Points', 'Checkpoints') + '\n', re.M):
        body = m.group(2); q = re.search(r'Position: \{x: ([-\d.e]+), y: ([-\d.e]+), z: ([-\d.e]+)\}', body); k = re.search(r'Kind: (\d+)', body)
        if q and k and k.group(1) == '2': rest[m.group(1)] = (float(q.group(1)), float(q.group(3)))   # PrologueInteractionKind.Rest
    return enc, cp, rest


POINTS = {}


def base_overlay(enc, cp, rest, notes):
    """The BASE relayout rows laid over the live content (the base may or may not be applied yet: both give the final places)."""
    f = CB / 'content308_relayout.json'
    if not f.exists(): f = BASE_STAGE / 'Data/content308_relayout.json'
    if not f.exists(): notes.append('no content308_relayout.json (base relayout rows not laid over the live content)'); return
    d = json.loads(f.read_text(encoding='utf-8')); off = set(d.get('groups_off', []))
    on = lambda r: r.get('enabled', True) and r.get('group', '') not in off
    for key in ('rests', 'rests_escort'):
        for r in d.get(key, []):
            if not on(r): continue
            rest[r['id']] = (r['x'], r['z'])
            if 'feet' in r: cp[r['id']] = (r['feet']['x'], r['feet']['z'])
    for r in d.get('encounter_moves', []):
        if on(r): enc[r['id']] = (r['x'], r['z'])
    for r in d.get('point_moves', []):
        if on(r) and 'x' in r: POINTS[r['id']] = (r['x'], r['z'])
    notes.append('base relayout rows from ' + str(f.relative_to(ROOT)).replace('\\', '/') + ' (version ' + str(d.get('version')) + ')')
    return d


def inn_overlay(cp, rest, notes):
    f = ROOT / 'Art/Playtest308/Relayout/ext/relayout308_ext.json'
    if not f.exists(): return
    for o in json.loads(f.read_text(encoding='utf-8'))['ops']:
        if o['id'] == 'XI1':
            rest['road_inn308'] = (o['to']['x'], o['to']['z']); cp['road_inn308'] = (o['to']['feet']['x'], o['to']['feet']['z'])
            notes.append('road inn (XI1) from relayout308_ext.json')


def main():
    ap = argparse.ArgumentParser(); ap.add_argument('--data'); ap.add_argument('--out', default=str(STAGE / 'Dry'))
    a = ap.parse_args()
    data_file = Path(a.data) if a.data else (STAGE / 'Data/earth308.json' if (STAGE / 'Data/earth308.json').exists() else CB / 'earth308.json')
    d = json.loads(data_file.read_text(encoding='utf-8')); ck = d['checks']; rules = d['rules']
    notes = ['earth data ' + str(data_file).replace('\\', '/')]
    routes, widths = layout_routes(); gen = generated_routes()
    enc, cp, rest = content_places(); base = base_overlay(enc, cp, rest, notes); inn_overlay(cp, rest, notes)
    masks = {}
    for n in ('after_wall', 'as_built', 'wall_seam_kept', 'wall_seam_removed'):
        f = CB / ('plan/_work/reach_p1b_v6_S0_%s.npy' % n)
        if f.exists(): masks[n] = np.load(f)
    if len(masks) < 4: notes.append('reach masks found: %d of 4' % len(masks))
    rows = []; out = {'data': str(data_file).replace('\\', '/'), 'encounters': {}, 'notes': notes}

    def check(ok, what): rows.append(('PASS' if ok else 'FAIL', what))
    def info(what): rows.append(('INFO', what))

    # review 2: one element for the crystal and the profile; the profile numbers are the ones the design names (XE1)
    exp = d['profiles']['expect']; pe = exp['element'] if exp.get('elemental') else ''
    check(d['organ']['element'] == pe, 'organ.element %s == profiles.expect.element %s (crystal shape = actor element)' % (d['organ']['element'], pe))
    xf = ROOT / 'Art/Playtest308/Relayout/ext/relayout308_ext.json'
    if xf.exists():
        xe1 = next((o for o in json.loads(xf.read_text(encoding='utf-8'))['ops'] if o['id'] == 'XE1'), None)
        for row in d['profiles']['rows']:
            want = (xe1 or {}).get('to', {}).get(row['name'])
            check(want is not None and abs(want['Telegraph'] - row['telegraph']) < 1e-6 and abs(want['Damage'] - row['damage']) < 1e-6,
                  'profile %s telegraph %g / damage %g == the design row XE1 %s' % (row['name'], row['telegraph'], row['damage'], want))
        used = {r['attack'] for r in d['encounters'] if r.get('enabled', True)}
        check(used <= {row['name'] for row in d['profiles']['rows']}, 'every encounter attack names a profile row (%s)' % sorted(used))
    else: info('no relayout308_ext.json: profile numbers not compared with the design')
    stops = [(s['name'], (s['x'], s['z'])) for s in ck['stops']]
    places = [('feet ' + k, v) for k, v in cp.items()] + [('rest ' + k, v) for k, v in rest.items()] + [('stop ' + n, p) for n, p in stops]
    mine = {}
    for r in d['encounters']:
        if not r.get('enabled', True): continue
        p = (r['x'], r['z']); p2 = (r['patrol2']['x'], r['patrol2']['z']); rid = r['id']; mine[rid] = (p, r)
        y = tri_h(*p); y2 = tri_h(*p2); fs, rel = footprint(*p); fs2, _ = footprint(*p2, 1.0)
        rec = dict(x=p[0], z=p[1], y=round(y, 2), patrol2=dict(x=p2[0], z=p2[1], y=round(y2, 2)), slope_face=round(tri_slope(*p), 1), slope_win4=round(win_slope(*p), 1),
                   slope_max_r2=round(fs, 1), relief_r2=round(rel, 2), patrol2_slope_max_r1=round(fs2, 1))
        if 'y_offline' in r: check(abs(y - r['y_offline']) <= 0.05, '%s feet y %.2f vs the row y_offline %.2f (height_p1b)' % (rid, y, r['y_offline']))
        if 'patrol2_y_offline' in r: check(abs(y2 - r['patrol2_y_offline']) <= 0.05, '%s patrol2 y %.2f vs the row %.2f' % (rid, y2, r['patrol2_y_offline']))
        check(fs <= rules['feet_max_slope_deg'], '%s slope: face %.1f deg, 4 m window %.1f, footprint r2 max %.1f <= %.0f (relief %.2f m)' % (rid, rec['slope_face'], rec['slope_win4'], fs, rules['feet_max_slope_deg'], rel))
        check(fs2 <= rules['feet_max_slope_deg'], '%s patrol2 slope max (r 1 m) %.1f <= %.0f' % (rid, fs2, rules['feet_max_slope_deg']))
        check(abs(math.dist(p, p2) - 1.5) <= 0.05, '%s patrol leg %.2f m (1.5)' % (rid, math.dist(p, p2)))
        # road centre (layout and generated lines; the nearer one counts)
        for nm, q in (('feet', p), ('patrol2', p2)):
            a1 = road(q, routes); a2 = road(q, gen) if gen else (1e9, None); best = a1 if a1[0] <= a2[0] else a2
            rec['road_' + nm] = dict(m=round(best[0], 2), road=best[1], layout=round(a1[0], 2), generated=round(a2[0], 2) if gen else None, width=widths.get(best[1]))
            if r.get('road_min_m', 0) > 0: check(best[0] >= r['road_min_m'], '%s %s road centre %.2f m (%s) >= %.1f' % (rid, nm, best[0], best[1], r['road_min_m']))
            else: info('%s %s road centre %.1f m (%s; no road limit on this row)' % (rid, nm, best[0], best[1]))
        # 54 m rule: aggro + leash + 10 against every rest feet / rest point / mandatory stop
        need = max(ck['rule54_m'], r['detection'] + r['leash'] + 10)
        near = sorted((round(math.dist(p, v), 1), n) for n, v in places)
        rec['nearest_places'] = near[:5]; rec['rule54_margin'] = round(near[0][0] - need, 1)
        check(near[0][0] >= need, '%s nearest rest / stop: %s %.1f m >= %.0f (margin %+.1f; detection %g + leash %g + 10)' % (rid, near[0][1], near[0][0], need, near[0][0] - need, r['detection'], r['leash']))
        # points the player stands at without resting: the ring edge stays outside the detection range
        for wrow in ck.get('watch', []):
            at = POINTS.get(wrow.get('point', ''), (wrow['x'], wrow['z'])); wd = math.dist(p, at) - wrow['radius_m']
            if wd < r['detection'] + 40:
                rec.setdefault('watch', {})[wrow['name']] = round(wd - r['detection'], 2)
                check(wd >= r['detection'], '%s <-> %s: ring edge %.1f m >= detection %g (margin %+.1f)' % (rid, wrow['name'], wd, r['detection'], wd - r['detection']))
        # the other encounters named in the data (agwi at its BASE place, the general)
        for o in ck['others']:
            if o not in enc: info('%s: %s not in the content' % (rid, o)); continue
            od = math.dist(p, enc[o]); rec['to_' + o] = round(od, 1)
            if od < ck['enemy_gap_m'] + 200: check(od >= ck['enemy_gap_m'], '%s <-> %s %.1f m >= %.0f' % (rid, o, od, ck['enemy_gap_m']))
        # reach masks 3x3
        c0, r0 = int(round(p[0] / CELL)), int(round(p[1] / CELL)); reach = {n: bool(m[r0 - 1:r0 + 2, c0 - 1:c0 + 2].all()) for n, m in masks.items()}
        rec['reach3'] = reach
        if masks: check(all(reach.values()), '%s reach 3x3 in %d mask(s): %s' % (rid, len(masks), reach))
        # leash circle steepness, road length inside the detection circle
        st = np.array([tri_slope(p[0] + dx, p[1] + dz) for dx in np.arange(-r['leash'], r['leash'] + .1, 2.0) for dz in np.arange(-r['leash'], r['leash'] + .1, 2.0) if dx * dx + dz * dz <= r['leash'] ** 2])
        rec['leash_pct_over_30deg'] = round(float((st > 30).mean() * 100), 1); rec['leash_max_slope'] = round(float(st.max()), 1)
        tot = 0.0
        for rid2, bb in routes.items():
            for q, w in zip(bb, bb[1:]):
                ls = math.dist(q, w); n = max(1, int(ls / 0.5))
                for i in range(n):
                    t = (i + .5) / n
                    if math.dist((q[0] + (w[0] - q[0]) * t, q[1] + (w[1] - q[1]) * t), p) < r['detection']: tot += ls / n
        rec['road_centre_len_in_detection'] = round(tot, 1)
        info('%s leash %g m circle: %.1f %% over 30 deg (max %.1f); road centre inside detection %g m: %.1f m' % (rid, r['leash'], rec['leash_pct_over_30deg'], rec['leash_max_slope'], r['detection'], tot))
        # the yaw of the row looks at the place the design names (information: yaw to the nearest stop)
        out['encounters'][rid] = rec
    ids = list(mine)
    for i in range(len(ids)):
        for j in range(i + 1, len(ids)):
            dd = math.dist(mine[ids[i]][0], mine[ids[j]][0])
            if dd < ck['enemy_gap_m'] + 100: check(dd >= ck['enemy_gap_m'], '%s <-> %s %.1f m >= %.0f (one side detection + the other leash)' % (ids[i], ids[j], dd, ck['enemy_gap_m']))
            else: info('%s <-> %s %.1f m' % (ids[i], ids[j], dd))
    # every rest place vs its nearest earth enemy (the cross table of the design)
    cross = {}
    for n, v in sorted(cp.items()):
        ds = sorted((round(math.dist(v, q[0]), 1), k) for k, q in mine.items())
        if ds and ds[0][0] < 150: cross[n] = ds[0]
    out['rest_feet_nearest_earth'] = cross
    for n, (dist, k) in cross.items(): info('rest feet %s: nearest earth enemy %s %.1f m' % (n, k, dist))
    fails = sum(1 for s, _ in rows if s == 'FAIL'); passes = sum(1 for s, _ in rows if s == 'PASS')
    out['pass'] = passes; out['fail'] = fails; out['rows'] = [dict(status=s, what=w) for s, w in rows]
    text = 'earth308 dry run (offline, height_p1b) - PASS %d / FAIL %d\n' % (passes, fails) + ''.join('  note %s\n' % n for n in notes) + ''.join('%s %s\n' % r for r in rows)
    od = Path(a.out); od.mkdir(parents=True, exist_ok=True)
    (od / 'earth308_dry.json').write_text(json.dumps(out, ensure_ascii=False, indent=1), encoding='utf-8'); (od / 'earth308_dry.txt').write_text(text, encoding='utf-8')
    print(text); print('written', str(od / 'earth308_dry.json').replace('\\', '/'))
    return 1 if fails else 0


if __name__ == '__main__':
    sys.exit(main())
