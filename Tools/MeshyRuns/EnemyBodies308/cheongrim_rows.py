"""D308-120 청림 first hour, the rest: species rows made from a pack species (root_beast <- goat, moss_wolf <- wolf, forest_bear <- bear)
and their swaps[] rows, written into creatures308.json (read - change - write, short). Only these keys are touched.
  python cheongrim_rows.py <spec.json>
spec.json = {"species": {key: {"from": base, "drop": [base fields to remove], ...overrides incl. parts}}, "swaps": [swap rows]}; a key's row is rebuilt from its base
every run (so an override removed from spec.json goes back to the base value), a swap row is replaced by its match.
"""
import copy
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
DATA = ROOT / 'Tools/Unity/Stage308_creatures/creatures308.json'
KEYS = {'root_beast', 'moss_wolf', 'forest_bear', 'tree_dokkaebi'}

spec = json.loads(Path(sys.argv[1]).read_text(encoding='utf-8'))
assert set(spec.get('species', {})) <= KEYS, set(spec['species']) - KEYS
data = json.loads(DATA.read_text(encoding='utf-8'))
by = {s['key']: s for s in data['species']}
for key, row in spec.get('species', {}).items():
    base = copy.deepcopy(by[row['from']])
    base.update({k: v for k, v in row.items() if k not in ('from', 'drop')})
    for k in row.get('drop', []):   # base fields that do not belong to the new species (fox_pack_before on a dokkaebi)
        base.pop(k, None)
    base['key'] = key
    base.setdefault('parts', [])
    if key in by:
        data['species'][data['species'].index(by[key])] = base
    else:
        data['species'].append(base)
    print(key, '<-', row['from'], len(base['parts']), 'parts')
for sw in spec.get('swaps', []):
    old = next((w for w in data['swaps'] if w['match'] == sw['match']), None)
    if old:
        data['swaps'][data['swaps'].index(old)] = sw
    else:
        data['swaps'].append(sw)
    print('swap', sw['match'], '->', sw['species'])
DATA.write_text(json.dumps(data, indent=1, ensure_ascii=False), encoding='utf-8')
