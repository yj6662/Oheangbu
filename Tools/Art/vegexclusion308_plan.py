"""#308 vegetation exclusion - offline plan preview (imported by vegexclusion308_offline.py plan).

INFERRED only: geometry = the Finish297 compound mesh JSON (LOD0 + Collision, export positions) and the 4 m height field;
crowns are synthetic rings (no tree meshes offline). The editor command BuildingFix308.Veg "plan" is the authority:
it uses the live LOD0 renderers + colliders of every configured root, the real prototype crown vertices and Physics ground.
"""
import json, math
from pathlib import Path
from vegexclusion308_offline import (ROOT, PROJECT, AUDIT, FINISH, OUT, HEIGHT_LIVE, HEIGHT_GEN, KIND, Height, parse_sheet, sheet_files, sha256, utc)

CELL = 2.0
CROWN_PROFILE = [0.7, 1.0, 0.95, 0.75, 0.4]   # synthetic crown radius share per height band (deciduous-ish)


class Soup:
    """World triangles of the footprint sources with a 2 m XZ grid; unit index per triangle."""

    def __init__(self):
        self.tris = []; self.unit = []; self.units = []; self.grid = {}

    def add_unit(self, key, cls, root, tris):
        ui = len(self.units)
        xs = [p[0] for t in tris for p in t]; ys = [p[1] for t in tris for p in t]; zs = [p[2] for t in tris for p in t]
        self.units.append({'key': key, 'cls': cls, 'root': root, 'box': (min(xs), min(zs), max(xs), max(zs)), 'y': (min(ys), max(ys)), 'pts': [(p[0], p[2]) for t in tris for p in t]})
        for t in tris:
            i = len(self.tris); self.tris.append(t); self.unit.append(ui)
            x0 = min(t[0][0], t[1][0], t[2][0]); x1 = max(t[0][0], t[1][0], t[2][0]); z0 = min(t[0][2], t[1][2], t[2][2]); z1 = max(t[0][2], t[1][2], t[2][2])
            for gx in range(int(math.floor(x0 / CELL)), int(math.floor(x1 / CELL)) + 1):
                for gz in range(int(math.floor(z0 / CELL)), int(math.floor(z1 / CELL)) + 1):
                    self.grid.setdefault((gx, gz), []).append(i)

    def vertical(self, x, z, y0, y1):
        """(unit index, face y) of the first triangle the vertical line at (x, z) meets with y in [y0, y1], or None."""
        lst = self.grid.get((int(math.floor(x / CELL)), int(math.floor(z / CELL))))
        if not lst: return None
        for i in lst:
            (ax, ay, az), (bx, by, bz), (cx, cy, cz) = self.tris[i]
            d = (bz - cz) * (ax - cx) + (cx - bx) * (az - cz)
            if abs(d) < 1e-9: continue
            l1 = ((bz - cz) * (x - cx) + (cx - bx) * (z - cz)) / d
            if l1 < -1e-5: continue
            l2 = ((cz - az) * (x - cx) + (ax - cx) * (z - cz)) / d
            if l2 < -1e-5 or 1 - l1 - l2 < -1e-5: continue
            y = l1 * ay + l2 * by + (1 - l1 - l2) * cy
            if y0 <= y <= y1: return (self.unit[i], y)
        return None

    def segment(self, a, b):
        """(unit index, t) of the nearest crossing of segment a->b with a triangle, or None."""
        x0, x1 = (a[0], b[0]) if a[0] <= b[0] else (b[0], a[0]); z0, z1 = (a[2], b[2]) if a[2] <= b[2] else (b[2], a[2])
        dx, dy, dz = b[0] - a[0], b[1] - a[1], b[2] - a[2]; best = None; seen = set()
        for gx in range(int(math.floor(x0 / CELL)), int(math.floor(x1 / CELL)) + 1):
            for gz in range(int(math.floor(z0 / CELL)), int(math.floor(z1 / CELL)) + 1):
                lst = self.grid.get((gx, gz))
                if not lst: continue
                for i in lst:
                    if i in seen: continue
                    seen.add(i)
                    (ax, ay, az), (bx, by, bz), (cx, cy, cz) = self.tris[i]
                    e1x, e1y, e1z = bx - ax, by - ay, bz - az; e2x, e2y, e2z = cx - ax, cy - ay, cz - az
                    px, py, pz = dy * e2z - dz * e2y, dz * e2x - dx * e2z, dx * e2y - dy * e2x
                    det = e1x * px + e1y * py + e1z * pz
                    if abs(det) < 1e-12: continue
                    inv = 1.0 / det; sx, sy, sz = a[0] - ax, a[1] - ay, a[2] - az
                    u = (sx * px + sy * py + sz * pz) * inv
                    if u < 0 or u > 1: continue
                    qx, qy, qz = sy * e1z - sz * e1y, sz * e1x - sx * e1z, sx * e1y - sy * e1x
                    v = (dx * qx + dy * qy + dz * qz) * inv
                    if v < 0 or u + v > 1: continue
                    t = (e2x * qx + e2y * qy + e2z * qz) * inv
                    if 1e-5 < t < 1.0 and (best is None or t < best[1]): best = (self.unit[i], t)
        return best


def class_of(cfg, name, kind=''):
    for c in cfg['classes']:
        if any(tok and tok.lower() in name.lower() for tok in c['tokens']): return c
    if kind in ('wall', 'gate', 'path'):
        for c in cfg['classes']:
            if c['name'] == kind: return c
    return cfg['classes'][-1]


def finish_soup(cfg):
    soup = Soup()
    for unity in sorted(FINISH.glob('*/unity.json')):
        u = json.loads(unity.read_text(encoding='utf-8-sig')); root = u.get('root', 'Finish297_' + unity.parent.name)
        if not any(r['name'] == root for r in cfg['roots']): continue
        for m in u.get('meshes', []):
            tris = []
            for part in ('LOD0', 'Collision'):
                f = unity.parent / 'Meshes' / ('%s_%s.json' % (m['name'], part))
                if not f.exists(): continue
                d = json.loads(f.read_text(encoding='utf-8'))
                v = d['v']; verts = [(v[i], v[i + 1], v[i + 2]) for i in range(0, len(v), 3)]
                if not m.get('world', False):
                    px, py, pz = m['position']; yw = math.radians(m.get('yaw', 0.0)); c, s = math.cos(yw), math.sin(yw)
                    verts = [(px + x * c + z * s, py + y, pz - x * s + z * c) for (x, y, z) in verts]
                for sub in d.get('sub', []):
                    t = sub['t']; tris.extend((verts[t[i]], verts[t[i + 1]], verts[t[i + 2]]) for i in range(0, len(t) - 2, 3))
            if tris: soup.add_unit(root + '/' + m['name'], class_of(cfg, m['name'], m.get('kind', '')), root, tris)
    return soup


def ring(r, arc):
    n = max(6, int(math.ceil(2 * math.pi * r / arc)))
    return [(r * math.cos(2 * math.pi * k / n), r * math.sin(2 * math.pi * k / n)) for k in range(n)]


def trunk_test(soup, cfg, p, h, rt, rock, extra=0.0, sky=0.0):
    """Nearest footprint within the class buffer: (unit index, distance, face height above base) or None. Rings outward from the base."""
    x, y, z = p; y0, y1 = y - cfg['belowBand'], y + max(.5, h, sky)
    maxbuf = max((c['rockBuffer'] if rock else c['buffer']) for c in cfg['classes']) + rt + extra
    hit = soup.vertical(x, z, y0, y1)
    if hit: return (hit[0], 0.0, hit[1] - y)
    r = cfg['ringStep']
    while r <= maxbuf + 1e-6:
        for (ox, oz) in ring(r, cfg['ringArc']):
            hit = soup.vertical(x + ox, z + oz, y0, y1)
            if hit:
                c = soup.units[hit[0]]['cls']; buf = (c['rockBuffer'] if rock else c['buffer']) + rt + extra
                if r <= buf + 1e-6: return (hit[0], r, hit[1] - y)
        r += cfg['ringStep']
    return None


def crown_samples(cfg, p, h, rc, yaw_deg):
    """Synthetic crown: (axis point, hull point) pairs on sectors x bands."""
    out = []; n = cfg['crownSectors']; bands = cfg['crownBands']; f0 = cfg['crownFrom']; yaw = math.radians(yaw_deg)
    for b in range(bands):
        f = f0 + (1 - f0) * (b + .5) / bands; r = rc * cfg['crownSyntheticShare'] * CROWN_PROFILE[min(b, len(CROWN_PROFILE) - 1)]
        for k in range(n):
            a = yaw + 2 * math.pi * k / n
            out.append(((p[0], p[1] + f * h, p[2]), (p[0] + r * math.cos(a), p[1] + f * h, p[2] + r * math.sin(a))))
    return out


def crown_test(soup, cfg, p, h, rc, yaw, strict=False):
    """(hits, deepest penetration, unit index of the deepest). strict = any crossing counts (move target)."""
    hits = 0; deep = 0.0; unit = None
    for a, b in crown_samples(cfg, p, h, rc, yaw):
        c = soup.segment(a, b)
        if not c: continue
        depth = (1 - c[1]) * math.dist(a, b)
        if strict: return (1, depth, c[0])
        if depth >= cfg['crownDepth']:
            hits += 1
            if depth > deep: deep = depth; unit = c[0]
    return (hits, deep, unit)


def excluded(areas, x, y, z, kind):
    for a in areas:
        if not a.get('exclude', True) or not (a.get('kinds', 31) & (1 << kind)) or 'centre' not in a or 'half' not in a: continue
        yaw = math.radians(-a.get('yaw', 0.0)); dx, dz = x - a['centre'][0], z - a['centre'][2]
        qx = dx * math.cos(yaw) + dz * math.sin(yaw); qz = -dx * math.sin(yaw) + dz * math.cos(yaw)
        if abs(qx) < a['half'][0] + 2 and abs(qz) < a['half'][1] + 2: return a['id']
    return None


def corridors():
    f = ROOT / 'Art/World/Compact/Rebuild/Architecture296/Generated/routes.json'
    if not f.exists(): return []
    d = json.loads(f.read_text(encoding='utf-8-sig'))
    return [[(p['x'], p['z']) for p in r.get('points', [])] for r in d.get('routes', []) if len(r.get('points', [])) >= 2]


def corridor_clearance(routes, x, z, width=6.0):
    best = float('inf')
    for pts in routes:
        for i in range(1, len(pts)):
            ax, az = pts[i - 1]; bx, bz = pts[i]; dx, dz = bx - ax, bz - az; L = dx * dx + dz * dz
            t = 0 if L < 1e-8 else max(0.0, min(1.0, ((x - ax) * dx + (z - az) * dz) / L))
            d = math.hypot(x - (ax + dx * t), z - (az + dz * t)) - width * .5
            if d < best: best = d
    return best


def audit_c3_fails():
    """(placement id, unit path, face height above the base) of the newest editor scan's C3 FAIL rows (main scene)."""
    import csv, re
    out = []
    scans = sorted((AUDIT / 'main').glob('*/findings.csv'))
    if not scans: return out
    with open(scans[-1], encoding='utf-8-sig', newline='') as f:
        for row in csv.DictReader(f):
            if row['check'] != 'C3' or row['level'] != 'FAIL': continue
            m = re.match(r'(Tree|Rock) (\S+) \(', row['detail'])
            if m: out.append((m.group(2), row['unit'], float(row['value'])))
    return out


def cmd_plan(cfg):
    OUT.mkdir(parents=True, exist_ok=True)
    soup = finish_soup(cfg)
    print('Finish297 mesh JSON: %d unit(s), %d triangle(s)' % (len(soup.units), len(soup.tris)))
    hf = Height(HEIGHT_LIVE if HEIGHT_LIVE.exists() else HEIGHT_GEN)
    routes = corridors()
    named = {n['id']: n for n in cfg.get('named', [])}
    mv = cfg['move']
    sheets = []; allplaces = []; areas = []
    for sheet in sheet_files(cfg):
        protos, places, ar = parse_sheet(sheet); areas += ar
        sheets.append({'sheet': str(sheet.relative_to(PROJECT)).replace('\\', '/'), 'sha256': sha256(sheet), 'placements': len(places)})
        for p in places:
            pr = protos.get(p['proto'])
            if not pr or pr['category'] not in (0, 3) or p['pos'] is None or pr['size'] is None: continue
            if any(t in (p['id'] + ' ' + p['cluster'] + ' ' + p['proto']).lower() for t in cfg['keepTokens']): continue
            s = p['scale']; rock = pr['category'] == 3
            allplaces.append({'sheet': sheet.name, 'index': p['index'], 'id': p['id'], 'proto': p['proto'], 'kind': KIND[pr['category']], 'rock': rock, 'pos': p['pos'], 'yaw': p['euler'][1], 'scale': s,
                              'h': pr['size'][1] * s, 'rc': max(pr['size'][0], pr['size'][2]) * s * .5, 'rt': (max(pr['size'][0], pr['size'][2]) * s * .25) if rock else max(cfg['minTrunkRadius'], pr['radius'] * s),
                              'maxSlope': pr['maxSlope'], 'maxAlt': pr['maxAlt']})
    # spacing grid of every tree/rock
    sg = {}
    for q in allplaces: sg.setdefault((int(q['pos'][0] // 8), int(q['pos'][2] // 8)), []).append(q)

    def crowded(x, z, me, spacing, taken):
        gx, gz = int(x // 8), int(z // 8)
        for ix in (gx - 1, gx, gx + 1):
            for iz in (gz - 1, gz, gz + 1):
                for q in sg.get((ix, iz), ()):
                    if q is me or q.get('gone'): continue
                    qx, qz = (q['to'][0], q['to'][2]) if 'to' in q else (q['pos'][0], q['pos'][2])
                    if math.hypot(qx - x, qz - z) < spacing: return True
        return any(math.hypot(tx - x, tz - z) < spacing for (tx, tz) in taken)

    rows = []; reach = max(q['rc'] for q in allplaces) + 3 if allplaces else 0
    boxes = [u['box'] for u in soup.units]
    near = [q for q in allplaces if any(b[0] - reach <= q['pos'][0] <= b[2] + reach and b[1] - reach <= q['pos'][2] <= b[3] + reach for b in boxes)]
    print('tree/rock placements %d, near Finish297 footprints %d' % (len(allplaces), len(near)))
    taken = []
    for q in near:
        p = q['pos']; t1 = trunk_test(soup, cfg, p, q['h'], q['rt'], q['rock']); reason = None; unit = None; detail = {}
        if t1:
            reason = 'trunk'; unit = t1[0]; detail = {'footDistance': round(t1[1], 2), 'faceAbove': round(t1[2], 2)}
        c = crown_test(soup, cfg, p, q['h'], q['rc'], q['yaw'])
        if c[0] >= cfg['crownMinSamples']:
            detail.update({'crownHits': c[0], 'crownDepth': round(c[1], 2)})
            if reason is None: reason = 'crown'; unit = c[2]
        n = named.get(q['id'])
        if reason is None and (n is None or n.get('action') == 'check'): continue
        if reason is None: reason = 'named'
        if q['id'] in cfg.get('keepIds', []): continue
        row = {'sheet': q['sheet'], 'index': q['index'], 'id': q['id'], 'proto': q['proto'], 'kind': q['kind'], 'from': [round(v, 3) for v in p], 'reason': reason,
               'unit': soup.units[unit]['key'] if unit is not None else (n or {}).get('building', ''), 'cls': soup.units[unit]['cls']['name'] if unit is not None else '',
               'defect': ','.join((n or {}).get('defect', [])), 'detail': detail}
        action = (n or {}).get('action', 'auto')
        if q['id'] in cfg.get('removeIds', []) or action == 'remove':
            row['action'] = 'remove'; row['note'] = 'configured removal'; q['gone'] = True; rows.append(row); continue
        if reason == 'named' and action == 'auto' and (n or {}).get('minMove', 0.0) <= 0:
            row['action'] = 'editor-plan'; row['note'] = 'DEFECTS_308 name; its geometry is only in the editor'; rows.append(row); continue
        if action == 'reseat' and reason == 'named':
            g = hf(p[0], p[2]); row['action'] = 'reseat'; row['to'] = [round(p[0], 3), round(g, 3), round(p[2], 3)]; row['moved'] = round(abs(g - p[1]), 2); rows.append(row); continue
        # nearest valid spot
        lo = max(mv['minMove'], (n or {}).get('minMove', 0.0)); best = None; r = lo
        away = None
        if unit is not None:
            b = soup.units[unit]['box']; cx, cz = (b[0] + b[2]) * .5, (b[1] + b[3]) * .5; d = math.hypot(p[0] - cx, p[2] - cz)
            if d > 1e-3: away = ((p[0] - cx) / d, (p[2] - cz) / d)
        dirs = [(math.cos(2 * math.pi * k / mv['directions']), math.sin(2 * math.pi * k / mv['directions'])) for k in range(mv['directions'])]
        if away: dirs.sort(key=lambda d: -(d[0] * away[0] + d[1] * away[1]))
        spacing = mv['rockSpacing'] if q['rock'] else mv['treeSpacing']
        while r <= mv['maxMove'] + 1e-6 and best is None:
            for (ux, uz) in dirs:
                x, z = p[0] + ux * r, p[2] + uz * r; g = hf(x, z)
                if hf.slope(x, z) > min(mv['maxSlope'], q['maxSlope']) or g > q['maxAlt']: continue
                if crowded(x, z, q, spacing, taken): continue
                if excluded(areas, x, g, z, 3 if q['rock'] else 0): continue
                if corridor_clearance(routes, x, z) < q['rt'] + mv['corridorClearance']: continue
                c2 = (x, g, z)
                if trunk_test(soup, cfg, c2, q['h'], q['rt'], q['rock'], mv['margin'], mv.get('skyClear', 0.0)): continue
                if crown_test(soup, cfg, c2, q['h'], q['rc'], q['yaw'], strict=True)[0]: continue
                best = c2; break
            r += mv['step']
        if best:
            row['action'] = 'move'; row['to'] = [round(v, 3) for v in best]; row['moved'] = round(math.hypot(best[0] - p[0], best[2] - p[2]), 2); q['to'] = best; taken.append((best[0], best[2]))
        else:
            row['action'] = 'remove'; row['note'] = 'no valid spot within %.0f m' % mv['maxMove']; q['gone'] = True
        rows.append(row)
    # placements the offline geometry cannot judge (roots without mesh JSON): the editor scan's C3 FAIL rows and the DEFECTS names
    allid = {q['id']: q for q in allplaces}; have = {r['id'] for r in rows}
    for (fid, unit, above) in audit_c3_fails():
        q = allid.get(fid); n = named.get(fid)
        if fid in have or q is None: continue
        rows.append({'sheet': q['sheet'], 'index': q['index'], 'id': fid, 'proto': q['proto'], 'kind': q['kind'], 'from': [round(v, 3) for v in q['pos']], 'reason': 'audit-c3', 'unit': unit,
                     'cls': class_of(cfg, unit.split('/', 1)[-1])['name'], 'defect': ','.join((n or {}).get('defect', [])), 'detail': {'faceAbove': above},
                     'action': 'remove' if (n or {}).get('action') == 'remove' else 'editor-plan', 'note': 'trunk under a face in the editor scan; the editor plan picks the spot'})
        have.add(fid)
    for n in cfg.get('named', []):
        q = allid.get(n['id'])
        if n['id'] in have or q is None or n.get('action') == 'check': continue
        rows.append({'sheet': q['sheet'], 'index': q['index'], 'id': n['id'], 'proto': q['proto'], 'kind': q['kind'], 'from': [round(v, 3) for v in q['pos']], 'reason': 'named', 'unit': n.get('building', ''), 'cls': '',
                     'defect': ','.join(n.get('defect', [])), 'detail': {}, 'action': 'editor-plan', 'note': 'DEFECTS_308 name; its geometry is only in the editor'})
        have.add(n['id'])
    # named coverage
    cover = []
    byid = {r['id']: r for r in rows}
    for n in cfg.get('named', []):
        r = byid.get(n['id']); q = allid.get(n['id'])
        cover.append({'id': n['id'], 'defect': n.get('defect', []), 'building': n.get('building', ''), 'inSheet': q is not None, 'offlineGeometry': n.get('building', '').startswith('Finish297_'),
                      'flagged': r is not None and r['reason'] != 'named', 'reason': r['reason'] if r else '', 'action': r['action'] if r else ('check' if n.get('action') == 'check' else 'absent'),
                      'detail': r.get('detail', {}) if r else {}})
    counts = {}
    for r in rows: counts[r['action'] + '/' + r['reason']] = counts.get(r['action'] + '/' + r['reason'], 0) + 1
    doc = {'utc': utc(), 'note': 'INFERRED offline preview: Finish297 mesh JSON + 4 m height field + synthetic crowns; the editor Veg plan decides', 'config': cfg['version'], 'sheets': sheets,
           'units': len(soup.units), 'triangles': len(soup.tris), 'counts': counts, 'rows': rows, 'named': cover}
    (OUT / 'veg-plan-offline.json').write_text(json.dumps(doc, ensure_ascii=False, indent=1), encoding='utf-8')
    with open(OUT / 'veg-plan-offline.csv', 'w', encoding='utf-8-sig', newline='') as f:
        f.write('sheet,index,id,prototype,kind,action,reason,unit,class,from_x,from_y,from_z,to_x,to_y,to_z,moved,defect,detail\n')
        for r in rows:
            to = r.get('to', ['', '', ''])
            f.write('%s,%d,%s,%s,%s,%s,%s,%s,%s,%.3f,%.3f,%.3f,%s,%s,%s,%s,%s,"%s"\n' % (r['sheet'], r['index'], r['id'], r['proto'], r['kind'], r['action'], r['reason'], r['unit'], r['cls'], r['from'][0], r['from'][1], r['from'][2],
                    to[0], to[1], to[2], r.get('moved', ''), r['defect'].replace(',', ' '), json.dumps(r['detail']).replace('"', "'")))
    print('offline plan: %d row(s) %s' % (len(rows), counts))
    print('named coverage (Finish297 geometry only offline):')
    for c in cover:
        print('  %-32s %-10s %-7s %-7s %s %s' % (c['id'], ','.join(c['defect']), 'FLAG' if c['flagged'] else ('named' if c['reason'] == 'named' else '-'), c['action'], c['building'], c['detail']))
    print('-> %s' % (OUT / 'veg-plan-offline.json'))
