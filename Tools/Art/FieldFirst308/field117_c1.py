"""D308-117 C1 (SPEC-FIELD-FIRST-HOUR-308 §8.5, TEST): the data rows of the side-path end fill.

  - content308_relayout.json encounter_moves[]: the moss jackal pack (field308/cheongrim_moss_jackal_1_*) from the wood 92 m
    north of the 신목 to the approach, 23 - 27 m south of the path (populate308 road_min_m 22), pack spacing 4 - 6 m,
    slope <= 14 deg (FieldFirst308 field308-flat). Facing the approach. Applied by Content308 content-apply:main.
  - places308.json places[] C_crest: the crest of the 주막 갈림 -> 벌목장 road (A4 L4b) - one dry tree on the NW bank and
    fresh stumps (existing Flora308 prefabs, colliders stripped). Applied by Places308 scene-apply:main.
  The two content points (herb_cache308, sinmok_bark308) are rows of Content308.Content.cs Points308 (text in code).

Idempotent: rows with the same id are replaced. A byte copy of each file goes to Art/Playtest306/Backups/FieldFirst308 first.
python Tools/Art/FieldFirst308/field117_c1.py [--revert]   (revert = take the rows of this script out again)
"""
import json, shutil, sys, time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
RELAYOUT = ROOT / 'Art/World/Compact/Rebuild/CliffBoundary308/content308_relayout.json'
PLACES = ROOT / 'Art/World/Compact/Rebuild/CliffBoundary308/places308.json'
BACKUP = ROOT / 'Art/Playtest306/Backups/FieldFirst308'
GROUP = 'F117_C1_gate'

# 2nd placement (user, 2026-10-19 night): the end gatekeepers are exempt from road_min_m 22 - "길에서 10 m 안팎 · 감지 범위
# 안" - so the walker on the approach passes inside their Detection 16 m. Feet 9.1 / 9.5 / 13.0 m from the approach path,
# 26 - 30 m short of the bark point, slope <= 14 deg, NavMesh at the feet and the patrol seconds (field308-nav 6 / 6).
JACKALS = [  # id, x, z, yaw (toward the approach path at (3682, 3236))
    ('field308/cheongrim_moss_jackal_1_0', 3683.0, 3227.0, 354.0),
    ('field308/cheongrim_moss_jackal_1_1', 3679.0, 3227.0, 0.0),
    ('field308/cheongrim_moss_jackal_1_2', 3681.0, 3223.0, 4.0),
]
FLORA = 'Assets/_Project/Art/World/Flora308/Prefabs/'
CREST = {
    'id': 'C_crest', 'enabled': True,
    'what': 'D308-117 C1 / A4 L4b: the crest of the inn -> logging camp road. A dry tree left standing on the NW bank and fresh '
            'stumps round it: the felling starts here (no sign, no text). Seen from the inn fork at +15 m (field308-sight).',
    'approach_route': 'geumpyo_inn__logging',
    'rows': [
        {'id': 'C_snag', 'kind': 'prefab', 'prefab': FLORA + 'Pack_DryTree_Dry4910.prefab', 'x': 3290.0, 'z': 2522.0, 'yaw': 30.0,
         'sink_m': 0.3, 'solid': False, 'max_slope_deg': 26.0, 'corner_gap_max_m': 1.2,
         'note': 'bank 11 m off the road centre, 2.6 m over it (slope 24 deg): the silhouette over the crest'},
        {'id': 'C_stump_1', 'kind': 'prefab', 'prefab': FLORA + 'Pack_CharStump_fresh.prefab', 'x': 3294.0, 'z': 2522.0, 'yaw': 70.0,
         'sink_m': 0.25, 'solid': False, 'max_slope_deg': 18.0, 'corner_gap_max_m': 0.8},
        {'id': 'C_stump_2', 'kind': 'prefab', 'prefab': FLORA + 'Pack_CharStump_two.prefab', 'x': 3292.0, 'z': 2518.0, 'yaw': 200.0,
         'sink_m': 0.1, 'solid': False, 'max_slope_deg': 22.0, 'corner_gap_max_m': 0.5},
        {'id': 'C_stump_3', 'kind': 'prefab', 'prefab': FLORA + 'Pack_CharStump_fresh.prefab', 'x': 3284.0, 'z': 2526.0, 'yaw': 310.0,
         'sink_m': 0.25, 'solid': False, 'max_slope_deg': 25.0, 'corner_gap_max_m': 0.8},
    ],
}

# 2nd pass (user, 2026-10-19 night): the two filled ends get something to see - points alone show nothing until 2.5 m
ENDS = {
    'id': 'D_ends', 'enabled': True,
    'what': 'D308-117 C1: the props of the two filled side-path ends - the herb gatherer bag at herb_cache308 (약초길 갈림) and '
            'the bark pile at sinmok_bark308 (신목 밑동). Existing props only, colliders stripped, no text.',
    'rows': [
        {'id': 'D_herb_bag', 'kind': 'prefab', 'prefab': 'Assets/KoreanTraditionalFestival/Prefabs/SM_JolongtaegiBag.prefab',
         'x': 3121.8, 'z': 2576.6, 'yaw': 40.0, 'sink_m': 0.02, 'solid': False, 'max_slope_deg': 12.0, 'corner_gap_max_m': 0.3,
         'note': 'the bag of herb_cache308 (F8), 1 m beside the point'},
        {'id': 'D_herb_basket', 'kind': 'prefab', 'prefab': 'Assets/_Project/Art/World/Flora308/Prefabs/Pack_P_Basket.prefab',
         'x': 3120.4, 'z': 2577.2, 'yaw': 300.0, 'sink_m': 0.02, 'solid': False, 'max_slope_deg': 12.0, 'corner_gap_max_m': 0.3,
         'note': "a second basket: a gatherer's stop, not a dropped item"},
        {'id': 'D_herb_jige', 'kind': 'prefab', 'prefab': 'Assets/_Project/Art/World/Hanyang308/Prefabs/Prop_Jige.prefab',
         'x': 3122.6, 'z': 2577.6, 'yaw': 220.0, 'sink_m': 0.02, 'solid': False, 'max_slope_deg': 12.0, 'corner_gap_max_m': 0.3,
         'note': 'still 1: bag and basket (0.3 - 0.4 m) vanish past ~10 m; the A-frame carrier is the silhouette from the path'},
        {'id': 'D_bark_1', 'kind': 'mesh', 'mesh': 'BarkLitter', 'x': 3698.6, 'z': 3249.2, 'yaw': 20.0, 'sink_m': 0.0, 'solid': False,
         'max_slope_deg': 8.0, 'scale': 1.3, 'note': 'bark of sinmok_bark308 (F9)'},
        {'id': 'D_bark_2', 'kind': 'mesh', 'mesh': 'BarkLitter', 'x': 3697.0, 'z': 3247.6, 'yaw': 130.0, 'sink_m': -0.05, 'solid': False,
         'max_slope_deg': 8.0, 'scale': 1.1},
        {'id': 'D_bark_bundle', 'kind': 'prefab', 'prefab': 'Assets/_Project/Art/Characters/Npc306/Tools/PF_NpcTool306_Firewood.prefab',
         'x': 3698.2, 'z': 3247.0, 'yaw': 75.0, 'sink_m': 0.0, 'solid': False, 'max_slope_deg': 8.0, 'corner_gap_max_m': 0.3,
         'note': 'a tied bundle on the bark: someone came for it before'},
    ],
}


def backup(p):
    BACKUP.mkdir(parents=True, exist_ok=True)
    dst = BACKUP / (p.stem + '-' + time.strftime('%Y%m%dT%H%M%S') + p.suffix)
    shutil.copy2(p, dst)
    return dst


def main():
    revert = '--revert' in sys.argv
    r = json.loads(RELAYOUT.read_text(encoding='utf-8'))
    ids = {j[0] for j in JACKALS}
    moves = [m for m in r['encounter_moves'] if m.get('id') not in ids]
    if not revert:
        for i, x, z, yaw in JACKALS:
            moves.append({'id': i, 'op': 'C1', 'group': GROUP, 'x': x, 'z': z, 'yaw': yaw, 'force': True,
                          'note': 'D308-117 C1 gatekeeper of the 신목 end (sinmok_bark308), road_min exception: 9 - 13 m off the approach, inside Detection 16 m'})
    r['encounter_moves'] = moves
    # sanctuary_trace308: main held (3215, 3422) while the code row, both candidate scenes and content308_keepout.json say
    # (3196, 3500); no ledger names the writer. C1 lets content-apply put main back on the code value (§8.5) - an earlier run
    # of this script pinned it and is taken out here
    r['points'] = [q for q in r.get('points', []) if not (q.get('id') == 'sanctuary_trace308' and q.get('op') == 'C1-keep')]
    p = json.loads(PLACES.read_text(encoding='utf-8'))
    places = [q for q in p['places'] if q.get('id') not in (CREST['id'], ENDS['id'])]
    if not revert:
        places += [CREST, ENDS]
    p['places'] = places
    print('backup', backup(RELAYOUT).name, backup(PLACES).name)
    RELAYOUT.write_text(json.dumps(r, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    PLACES.write_text(json.dumps(p, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(('reverted' if revert else 'wrote'), 'encounter_moves', len(moves), 'places', [q['id'] for q in places])


if __name__ == '__main__':
    main()
