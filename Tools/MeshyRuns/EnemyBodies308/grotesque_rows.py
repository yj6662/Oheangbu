"""D308-120 field enemies, grotesque pass: write the parts[] rows of six species into creatures308.json (read - change - write, short).

Only the six field species are touched (jackal ember_dog bandit boar fox turtle); bear_boss and the rest stay as they are.
  python grotesque_rows.py <rows.json>      rows.json = {species: [part rows]}; a species' rows replace its parts[] (bandit keeps its axe first)
"""
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
DATA = ROOT / 'Tools/Unity/Stage308_creatures/creatures308.json'
SIX = {'jackal', 'ember_dog', 'bandit', 'boar', 'fox', 'turtle'}
AXE = 'Assets/_Project/Art/Characters/Npc306/Tools/PF_NpcTool306_Axe.prefab'

rows = json.loads(Path(sys.argv[1]).read_text(encoding='utf-8'))
assert set(rows) <= SIX, set(rows) - SIX
data = json.loads(DATA.read_text(encoding='utf-8'))
for s in data['species']:
    if s['key'] not in rows:
        continue
    keep = [p for p in s.get('parts', []) if p.get('prefab') == AXE] if s['key'] == 'bandit' else []
    s['parts'] = keep + rows[s['key']]
    print(s['key'], len(s['parts']), 'parts')
DATA.write_text(json.dumps(data, indent=1, ensure_ascii=False), encoding='utf-8')
