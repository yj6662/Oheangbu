"""#308 vegetation exclusion pass - offline preview (read only, INFERRED; the editor command BuildingFix308.Veg decides).

Usage:
    python Tools/Art/vegexclusion308_offline.py named      look the DEFECTS_308 named placements up in the sheets (index, position)
    python Tools/Art/vegexclusion308_offline.py plan       named + Finish297 mesh JSON footprint tests + nearest valid spot preview
Outputs: Art/World/Compact/Rebuild/BuildingAudit308/offline/veg308/ (never the editor's Fix/ folder).
Sheets are read line by line; nothing under Oheangbu/Assets is written.
"""
import json, math, struct, sys, hashlib, datetime, re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
PROJECT = ROOT / 'Oheangbu'
AUDIT = ROOT / 'Art/World/Compact/Rebuild/BuildingAudit308'
CONFIG = AUDIT / 'veg308.json'
FINISH = ROOT / 'Art/World/Compact/Rebuild/Finish297'
HEIGHT_LIVE = PROJECT / 'Assets/_Project/Art/World/Watershed295/Surface/height.bytes'
HEIGHT_GEN = ROOT / 'Art/World/Compact/Rebuild/Watershed295/Generated/height.bytes'
OUT = AUDIT / 'offline' / 'veg308'
H_ROWS, H_COLS, H_CELL = 1501, 1001, 4.0
NUM = r'(-?[0-9.eE+\-]+)'
VEC3 = re.compile(r'\{x: ' + NUM + r', y: ' + NUM + r', z: ' + NUM + r'\}')
VEC2 = re.compile(r'\{x: ' + NUM + r', y: ' + NUM + r'\}')
KIND = {0: 'Tree', 1: 'Shrub', 2: 'Grass', 3: 'Rock', 4: 'Prop'}


def utc():
    return datetime.datetime.now(datetime.timezone.utc).strftime('%Y%m%dT%H%M%SZ')


def sha256(path):
    h = hashlib.sha256()
    with open(path, 'rb') as f:
        for chunk in iter(lambda: f.read(1 << 20), b''): h.update(chunk)
    return h.hexdigest()


def load_config():
    return json.loads(CONFIG.read_text(encoding='utf-8-sig'))


def parse_sheet(path):
    """prototypes {id: dict}, placements [dict], preserved areas [dict] of one sheet asset, line by line."""
    protos = {}; places = []; areas = []; section = None; cur = None; pid = None; area = None
    with open(path, encoding='utf-8', errors='replace') as f:
        for line in f:
            s = line.rstrip('\n')
            if s.startswith('  ') and not s.startswith('   ') and not s.startswith('  - '):
                section = s.strip().split(':')[0]; cur = None; continue
            if section == 'Prototypes':
                if s.startswith('  - Id: '):
                    pid = s[8:].strip(); protos[pid] = {'category': None, 'size': None, 'radius': .2, 'maxAlt': 10000.0, 'maxSlope': 90.0}
                elif pid and s.startswith('    Category: '): protos[pid]['category'] = int(s.split(':')[1])
                elif pid and s.startswith('    Size: '): protos[pid]['size'] = tuple(float(x) for x in VEC3.search(s).groups())
                elif pid and s.startswith('    Radius: '): protos[pid]['radius'] = float(s.split(':')[1])
                elif pid and s.startswith('    MaximumAltitude: '): protos[pid]['maxAlt'] = float(s.split(':')[1])
                elif pid and s.startswith('    MaximumSlope: '): protos[pid]['maxSlope'] = float(s.split(':')[1])
            elif section == 'PreservedAreas':
                if s.startswith('  - Id: '):
                    area = {'id': s[8:].strip(), 'exclude': True, 'kinds': 31}; areas.append(area)
                elif area is not None and s.startswith('    Centre: '): area['centre'] = tuple(float(x) for x in VEC3.search(s).groups())
                elif area is not None and s.startswith('    HalfSize: '):
                    m = VEC2.search(s); area['half'] = (float(m.group(1)), float(m.group(2)))
                elif area is not None and s.startswith('    Yaw: '): area['yaw'] = float(s.split(':')[1])
                elif area is not None and s.startswith('    ExcludeProcedural: '): area['exclude'] = s.endswith('1')
                elif area is not None and s.startswith('    AffectedKinds: '): area['kinds'] = int(s.split(':')[1])
            elif section == 'FixedPlacements':
                if s.startswith('  - Id: '):
                    cur = {'index': len(places), 'id': s[8:].strip(), 'cluster': '', 'proto': None, 'pos': None, 'euler': (0.0, 0.0, 0.0), 'scale': 1.0}; places.append(cur)
                elif cur is not None and s.startswith('    ClusterId: '): cur['cluster'] = s[15:].strip()
                elif cur is not None and s.startswith('    PrototypeId: '): cur['proto'] = s[17:].strip()
                elif cur is not None and s.startswith('    Position: '): cur['pos'] = tuple(float(x) for x in VEC3.search(s).groups())
                elif cur is not None and s.startswith('    Euler: '): cur['euler'] = tuple(float(x) for x in VEC3.search(s).groups())
                elif cur is not None and s.startswith('    Scale: '): cur['scale'] = float(s[11:])
    return protos, places, areas


def sheet_files(cfg):
    base = PROJECT / 'Assets/_Project/Art/World'
    files = []
    for name in cfg.get('sheetTokens', []):
        files += [p for p in list((base / 'Architecture296/Data').glob('*%s*.asset' % name)) + list((base / 'Enclosure305').glob('*%s*.asset' % name)) if p.suffix == '.asset']
    seen = []; [seen.append(f) for f in files if f not in seen]
    return seen


class Height:
    def __init__(self, path):
        data = Path(path).read_bytes()
        if len(data) != H_ROWS * H_COLS * 4: raise ValueError('unexpected height field size %d' % len(data))
        self.h = struct.unpack('<%df' % (H_ROWS * H_COLS), data)

    def __call__(self, x, z):
        j = min(max(x / H_CELL, 0.0), H_COLS - 1.001); i = min(max(z / H_CELL, 0.0), H_ROWS - 1.001)
        j0, i0 = int(j), int(i); fj, fi = j - j0, i - i0; h = self.h; W = H_COLS
        return (h[i0 * W + j0] * (1 - fj) * (1 - fi) + h[i0 * W + j0 + 1] * fj * (1 - fi) + h[(i0 + 1) * W + j0] * (1 - fj) * fi + h[(i0 + 1) * W + j0 + 1] * fj * fi)

    def slope(self, x, z, d=1.0):
        dx = (self(x + d, z) - self(x - d, z)) / (2 * d); dz = (self(x, z + d) - self(x, z - d)) / (2 * d)
        return math.degrees(math.atan(math.hypot(dx, dz)))


def cmd_named(cfg, quiet=False):
    OUT.mkdir(parents=True, exist_ok=True)
    rows = []; info = []
    named = {n['id']: n for n in cfg.get('named', [])}
    for sheet in sheet_files(cfg):
        protos, places, areas = parse_sheet(sheet)
        info.append({'sheet': str(sheet.relative_to(PROJECT)).replace('\\', '/'), 'sha256': sha256(sheet), 'placements': len(places), 'prototypes': len(protos), 'preservedAreas': len(areas)})
        for p in places:
            if p['id'] in named:
                pr = protos.get(p['proto'], {})
                rows.append({'sheet': sheet.name, 'index': p['index'], 'id': p['id'], 'proto': p['proto'], 'kind': KIND.get(pr.get('category'), '?'), 'pos': p['pos'], 'scale': p['scale'],
                             'size': [round(v * p['scale'], 2) for v in (pr.get('size') or (0, 0, 0))], 'defect': named[p['id']].get('defect', []), 'building': named[p['id']].get('building', ''), 'action': named[p['id']].get('action', 'auto')})
    found = {r['id'] for r in rows}
    missing = [i for i in named if i not in found]
    doc = {'utc': utc(), 'sheets': info, 'named': rows, 'missing': missing}
    (OUT / 'named.json').write_text(json.dumps(doc, ensure_ascii=False, indent=1), encoding='utf-8')
    if not quiet:
        print('named: %d of %d found; missing %s' % (len(found), len(named), missing))
        for i in info: print('  sheet %s sha %s placements %d preserved %d' % (i['sheet'], i['sha256'][:12], i['placements'], i['preservedAreas']))
        for r in sorted(rows, key=lambda r: r['id']):
            print('  %-34s #%-6d %-44s %-5s (%.2f, %.2f, %.2f) scale %.2f size %s defect %s' % (r['id'], r['index'], r['proto'], r['kind'], r['pos'][0], r['pos'][1], r['pos'][2], r['scale'], r['size'], ','.join(r['defect'])))
    return doc


if __name__ == '__main__':
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
    cfg = load_config()
    cmd = sys.argv[1] if len(sys.argv) > 1 else 'named'
    if cmd == 'named':
        cmd_named(cfg)
    elif cmd == 'plan':
        sys.path.insert(0, str(Path(__file__).resolve().parent))
        from vegexclusion308_plan import cmd_plan
        cmd_plan(cfg)
    else:
        print(__doc__)
