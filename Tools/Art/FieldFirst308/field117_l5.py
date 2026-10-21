"""SPEC-FIELD-FIRST-HOUR-308 §8.15 (A4 L5, TEST): the stumps of the two elms taken out at the deep fork rest altar.

  - The elms 292_tree_3266_3074 and 292_tree_3278_3062 of the shared 청림 sheet (DryLandscape) were the last blockers of the
    벌목장 -> 깊은 숲 갈림 leg (field308-sight, sheet trees counted). They leave the sheet through FieldFirst308
    field308-rows:apply (data: field117_build.py sight_rows, BuildingFix308.VegRows ledger).
  - places308.json places[] C_fork: a fresh stump on each of their spots (existing Flora308 prefabs, colliders stripped, like
    C_crest), so the gap reads as felled - the clearing the altar keepers cut, not a hole in the forest. No text, no light.
    Applied by Places308 scene-apply:main --place C_fork.

Idempotent: the place with the same id is replaced. A byte copy of the file goes to Art/Playtest306/Backups/FieldFirst308 first.
python Tools/Art/FieldFirst308/field117_l5.py [--revert]   (revert = take the place of this script out again)
"""
import json, shutil, sys, time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
PLACES = ROOT / 'Art/World/Compact/Rebuild/CliffBoundary308/places308.json'
BACKUP = ROOT / 'Art/Playtest306/Backups/FieldFirst308'
FLORA = 'Assets/_Project/Art/World/Flora308/Prefabs/'

FORK = {
    'id': 'C_fork', 'enabled': True,
    'what': 'SPEC-FIELD-FIRST-HOUR-308 §8.15 / A4 L5: the deep fork rest altar (deep_fork_rest308). The two elms 6 and 16 m from '
            'it left the sheet (field308-rows); a fresh stump stands on each spot so the opening reads as cut. Seen with the altar '
            'from +350 m of the logging camp road (field308-sight).',
    'rows': [
        {'id': 'C_fork_stump_1', 'kind': 'prefab', 'prefab': FLORA + 'Pack_CharStump_fresh.prefab', 'x': 3269.9, 'z': 3071.8,
         'yaw': 75.0, 'sink_m': 0.25, 'solid': False, 'max_slope_deg': 26.0, 'corner_gap_max_m': 0.8,
         'note': 'on 292_tree_3266_3074 (elm, scale .79)'},
        {'id': 'C_fork_stump_2', 'kind': 'prefab', 'prefab': FLORA + 'Pack_CharStump_two.prefab', 'x': 3275.1, 'z': 3063.3,
         'yaw': 320.0, 'sink_m': 0.1, 'solid': False, 'max_slope_deg': 26.0, 'corner_gap_max_m': 0.8,
         'note': 'on 292_tree_3278_3062 (elm, scale 1.07)'},
        # the 1.6 m altar sits below the crest from the +350 m eye (stills fhl5_fork): a 4.8 m white cloth pole beside it is
        # the vertical mark - a 서낭 pole, no light. 2.7 m from the altar toward the eye, cloth broadside to it (yaw 59).
        {'id': 'C_fork_flag', 'kind': 'prefab', 'prefab': FLORA + 'Pack_P_Flag_Wind_1.prefab', 'x': 3268.2, 'z': 3080.4,
         'yaw': 59.0, 'sink_m': 0.3, 'solid': False, 'max_slope_deg': 26.0, 'corner_gap_max_m': 0.8,
         'note': 'deep_fork_rest308 altar mark'},
    ],
}


def backup(p):
    BACKUP.mkdir(parents=True, exist_ok=True)
    dst = BACKUP / (p.stem + '-' + time.strftime('%Y%m%dT%H%M%S') + p.suffix)
    shutil.copy2(p, dst)
    return dst


def main():
    revert = '--revert' in sys.argv
    p = json.loads(PLACES.read_text(encoding='utf-8'))
    places = [q for q in p['places'] if q.get('id') != FORK['id']]
    if not revert:
        places.append(FORK)
    p['places'] = places
    print('backup', backup(PLACES).name)
    PLACES.write_text(json.dumps(p, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(('reverted' if revert else 'wrote'), 'places', [q['id'] for q in places])


if __name__ == '__main__':
    main()
