"""D308-117 C3 (SPEC-FIELD-FIRST-HOUR-308 §8.6, TEST): the front sections of 뿌리 굴 (cr_root) and 숯가마 골 땅굴 (cr_kiln), opened before 청룡.

The 국 ledge (2.3 m) of each dungeon stays where it is, so the jar on its high stand and the celadon fragment stay post-청룡 rewards.
What the front section (the rooms reached without 국) gets:
  - the fork: the barred shortcut door between row A and row B of the entrance column becomes two-sided (point in the door plane +
    the door's own content point, Dungeon308 BuildDoor) - from the entrance room one way runs on along row A to the ledge (the jar /
    the upper room shows above it), the other through the door into row B (the enemies and the small reward)
  - the enemies: a pack of two in row B, clones of live Cheongrim field actors (Content308 Populate front:apply, prefix dg308f/)
  - the small reward: a white porcelain bottle with 40 coins on the floor of row B, at the foot of the drop ledge
  - cr_kiln only: 업동's charcoal sack (C2 F6, EvidenceId of his commission) just inside the entrance room ("굴 어귀 안쪽")
No shrine: Cheongrim already has its LDB-46 budget of 4 - 6 shrines (Spec §8.6).

Writes the BUILT data (git-ignored, like field117_c1.py does for places308.json): Art/World/Compact/Rebuild/Dungeon308/<id>/layout.json
(door + reward rows; a later `dungeon308.py build` drops them - rerun this script after it) and
Tools/Unity/Stage308_populate/dungeon_front308.json. Applied by `Dungeon308 refit:cr_root`, `refit:cr_kiln`, `Content308 Populate front:apply`.
Idempotent; a byte copy of each file goes to Art/Playtest306/Backups/FieldFirst308 first.
python Tools/Art/FieldFirst308/field117_c3.py [--revert | --texts]
  --texts = only the evidence rows' prompt / locked / text, in place (C2: the layout is shared with dungeon308_dress.py)
"""
import json, shutil, sys, time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
DG = ROOT / 'Art/World/Compact/Rebuild/Dungeon308'
FRONT = ROOT / 'Tools/Unity/Stage308_populate/dungeon_front308.json'
BACKUP = ROOT / 'Art/Playtest306/Backups/FieldFirst308'
COINS_TEXT = '백자 밑에 엽전 몇 꾸러미가 깔려 있다.'

# per dungeon: the reward rows this script owns (pos = stand: on the floor), checked against the layout's lamps and rocks (>= 1.2 m)
ROWS = {
    'cr_root': [  # floor 199.782; row B x 3319 - 3330, B2 z 1768 - 1780 (B3 above the drop ledge at z 1768 holds the celadon)
        {'id': 'dg308_cr_root_cache', 'kind': 'coins', 'currency': 40, 'prefab': 'SM_040_A_Ceramic_Bottle', 'text': COINS_TEXT,
         'pos': [3325.2, 199.782, 1769.8], 'stand': [3325.2, 199.782, 1769.8]},
    ],
    'cr_kiln': [  # floor 233.494; A1 x 2749 - 2756 z 2703 - 2709 (entrance from +x), B2 x 2742 - 2749 z 2709 - 2715 (drop ledge at z 2715)
        {'id': 'dg308_cr_kiln_cache', 'kind': 'coins', 'currency': 40, 'prefab': 'SM_051_A_Ceramic_Bottle', 'text': COINS_TEXT,
         'pos': [2744.0, 233.494, 2713.8], 'stand': [2744.0, 233.494, 2713.8]},
        {'id': 'dg308_cr_kiln_sack', 'kind': 'evidence', 'prefab': 'Assets/KoreanTraditionalFestival/Prefabs/SM_SackOfRice',
         'prompt': '숯자루', 'locked': None, 'text': None,   # from field117.json c2.evidence (D308-117 F6 row lines 1 / 2)
         'pos': [2754.2, 233.494, 2705.0], 'stand': [2754.2, 233.494, 2705.0]},
    ],
}
FIELD117 = ROOT / 'Art/World/Compact/Rebuild/FieldFirst308/field117.json'


def evidence_texts():
    """C2 (2026-10-20): an evidence row's words come from the built field117.json (field117_build.py, D308-117 by position):
    prompt = the row name, locked = row line 1 (seen before the request), text = row line 2 (the pick-up)."""
    ev = {e['id']: e for e in json.loads(FIELD117.read_text(encoding='utf-8'))['c2']['evidence']}
    for rows in ROWS.values():
        for r in rows:
            if r['kind'] != 'evidence': continue
            e = ev.get(r['id'])
            if e is None: raise SystemExit('%s: no c2 evidence row in %s - run field117_build.py first' % (r['id'], FIELD117.name))
            r['prompt'], r['locked'], r['text'] = e['label'], e['locked'], e['text']
ENEMIES = [  # 3-D feet on the cave floor; patrol2 inside the same room
    {'id': 'dg308f/cr_root_0', 'name': '뿌리 짐승', 'source': 'field308/cheongrim_root_beast_4_0', 'dungeon': 'cr_root',
     'feet': [3324.0, 199.782, 1774.5], 'patrol2': [3321.0, 199.782, 1773.0]},
    {'id': 'dg308f/cr_root_1', 'name': '뿌리 짐승', 'source': 'field308/cheongrim_root_beast_4_1', 'dungeon': 'cr_root',
     'feet': [3326.5, 199.782, 1772.0], 'patrol2': [3323.8, 199.782, 1772.0]},
    {'id': 'dg308f/cr_kiln_0', 'name': '이끼 승냥이', 'source': 'field308/cheongrim_moss_jackal_1_0', 'dungeon': 'cr_kiln',
     'feet': [2745.0, 233.494, 2705.0], 'patrol2': [2746.5, 233.494, 2708.0]},
    {'id': 'dg308f/cr_kiln_1', 'name': '이끼 승냥이', 'source': 'field308/cheongrim_moss_jackal_1_1', 'dungeon': 'cr_kiln',
     'feet': [2746.0, 233.494, 2711.5], 'patrol2': [2744.0, 233.494, 2713.0]},
]


def backup(p):
    BACKUP.mkdir(parents=True, exist_ok=True)
    dst = BACKUP / (p.parent.name + '-' + p.stem + '-' + time.strftime('%Y%m%dT%H%M%S') + p.suffix)
    shutil.copy2(p, dst)
    return dst.name


def near(a, b):
    return ((a[0] - b[0]) ** 2 + (a[2] - b[2]) ** 2) ** 0.5


def texts_only():
    """--texts: rewrite prompt / locked / text of the evidence rows already in the layout and nothing else (the layout is also
    written by dungeon308.py / dungeon308_dress.py; a full rerun of this script would put its own copy of the other rows back)."""
    for did, rows in ROWS.items():
        ev = {r['id']: r for r in rows if r['kind'] == 'evidence'}
        if not ev: continue
        p = DG / did / 'layout.json'; lay = json.loads(p.read_text(encoding='utf-8')); changed = []
        for r in lay['rewards']:
            w = ev.get(r['id'])
            if w is None: continue
            for k in ('prompt', 'locked', 'text'):
                if r.get(k) != w[k]: changed.append('%s.%s %r -> %r' % (r['id'], k, r.get(k), w[k])); r[k] = w[k]
        missing = set(ev) - {r['id'] for r in lay['rewards']}
        if missing: raise SystemExit('%s: %s not in the layout - run this script without --texts' % (did, sorted(missing)))
        if not changed: print(did, '변경 없음'); continue
        print('backup', backup(p)); p.write_text(json.dumps(lay, ensure_ascii=False, indent=1) + '\n', encoding='utf-8')
        for c in changed: print(' ', did, c)


def main():
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
    evidence_texts()
    if '--texts' in sys.argv: return texts_only()
    revert = '--revert' in sys.argv
    for did, rows in ROWS.items():
        p = DG / did / 'layout.json'; lay = json.loads(p.read_text(encoding='utf-8')); print('backup', backup(p))
        mine = {r['id'] for r in rows}
        lay['rewards'] = [r for r in lay['rewards'] if r['id'] not in mine]
        d = lay['door']
        if revert:
            if 'insideBefore' in d: d['inside'] = d.pop('insideBefore')
            d.pop('twoSided', None)
        else:
            for r in rows:   # clearance from the lamps and the realm rocks of the layout
                for o in [x['pos'] for x in lay.get('lamps', [])] + [x['pos'] for x in lay.get('rocks', [])]:
                    if abs(o[1] - r['pos'][1]) < 1.0 and near(o, r['pos']) < 1.2: raise SystemExit('%s stands %.2f m from %s' % (r['id'], near(o, r['pos']), o))
            lay['rewards'] += rows
            if 'insideBefore' not in d: d['insideBefore'] = d['inside']
            d['inside'] = list(d['pos']); d['twoSided'] = True
        p.write_text(json.dumps(lay, ensure_ascii=False, indent=1) + '\n', encoding='utf-8')
        print(('reverted' if revert else 'wrote'), did, 'rewards', [r['id'] for r in lay['rewards']], 'door twoSided', d.get('twoSided', False))
    if revert:
        if FRONT.exists(): print('backup', backup(FRONT)); FRONT.write_text(json.dumps({'version': 'f117.c3.revert', 'enemies': []}, indent=1) + '\n', encoding='utf-8')
        print('front enemies: none (run Content308 Populate front:revert)')
        return
    if FRONT.exists(): print('backup', backup(FRONT))
    FRONT.write_text(json.dumps({'version': 'f117.c3.1', 'doc': 'D308-117 C3: dungeon front enemies (Content308 Populate front:apply). Written by Tools/Art/FieldFirst308/field117_c3.py',
                                 'nav_radius_m': 1.5, 'keep_m': 1.0, 'enemies': ENEMIES}, ensure_ascii=False, indent=1) + '\n', encoding='utf-8')
    print('wrote', FRONT.name, len(ENEMIES), 'enemies')


if __name__ == '__main__':
    main()
