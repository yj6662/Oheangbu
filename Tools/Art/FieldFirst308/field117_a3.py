"""D308-117 A3 4th try (SPEC-FIELD-FIRST-HOUR-308 §8.11, TEST): the ore vein made bigger and put along the road.

  - places308.json places[] E_ore_trail: two copies of Ore_Vein308_Trail.prefab (OreOutcrop308 trail-prefab: the third-try
    split-vein boulder - its two halves, the dark selvage and the glowing lode; no light, no collider) at rules.prefab_scale_max
    beside the 폐광 -> 성황당 -> 금표 주막 road (MainPath). Each copy is turned so a vein face looks back down the road at the
    walker 60 m behind (yaw searched offline in 5 deg steps, face error <= 22 deg) and sunk so no footprint point floats on the
    offline height field (the boulder was baked on the 12.1 deg ground of the deep fork). Applied by Places308 scene-apply:main.
  - The 광맥 is an ART-INK 인력 광원: the lode keeps its emission (EventWash308 AllowedGlow matches 'Ore_Vein' in the renderer name).
    No Light, no text, no content row. The T3 row of ore308.json is not touched.

Idempotent: the place with the same id is replaced. A byte copy of the file goes to Art/Playtest306/Backups/FieldFirst308 first.
python Tools/Art/FieldFirst308/field117_a3.py [--revert]   (revert = take the place of this script out again)
"""
import json, shutil, sys, time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
PLACES = ROOT / 'Art/World/Compact/Rebuild/CliffBoundary308/places308.json'
BACKUP = ROOT / 'Art/Playtest306/Backups/FieldFirst308'
sys.path.insert(0, str(ROOT / 'Tools/Art'))
import cliff308_base as B  # noqa: E402

PREFAB = 'Assets/_Project/Art/World/Pacing308/Places308/Ore_Vein308_Trail.prefab'
# id, x, z, yaw, sink_m, corner_gap_max_m, note   (s = MainPath arc length from the mine-yard exit)
# E_ore_1 (s 105, inside of the bend below the mine yard, (3305.0, 2101.7) yaw 155) was applied once and taken out the same day:
# from 60 / 45 / 30 m back the guide rock Guide2_308/G2_Rock_D1, the cairn and a pine hide the seam - the descent from the yard
# already has the L0 guide rocks. The outside of that bend (3290.4, 2092.6) is the guide rock G2_Rock_B1 itself.
SPOTS = [
    ('E_ore_2', 3314.7, 2193.3, 135.0, 0.25, 1.2,
     's 232: 22 m past the 비석, 35 m short of the 성황당 (not at the 비석 itself - the L0 guide keeps its own ground)'),
    ('E_ore_3', 3197.0, 2271.1, 290.0, 0.25, 1.4,
     's 395: between the 성황당 and the 금표 주막 - the last seam before the inn'),
]


def place():
    h = B.load_height()
    rows = []
    for i, x, z, yaw, sink, gap, note in SPOTS:
        rows.append({'id': i, 'kind': 'prefab', 'prefab': PREFAB, 'x': x, 'z': z, 'yaw': yaw, 'sink_m': sink, 'solid': False,
                     'max_slope_deg': 18.0, 'corner_gap_max_m': gap, 'scale': 2.0,
                     'y_offline': round(float(B.sample_tri(h, x, z)), 2), 'note': note})
    return {
        'id': 'E_ore_trail', 'enabled': True,
        'what': 'D308-117 A3 4th try (§8.11): the third-try ore vein (Ore_Vein308_Trail.prefab, x2) twice beside the road past the '
                '성황당 to the 금표 주막 - the seam the walker follows out of the mine. Glowing lode only '
                '(인력 광원), no light, no collider, no text.',
        'rows': rows,
    }


def main():
    revert = '--revert' in sys.argv
    p = json.loads(PLACES.read_text(encoding='utf-8'))
    places = [q for q in p['places'] if q.get('id') != 'E_ore_trail']
    if not revert:
        places.append(place())
    p['places'] = places
    BACKUP.mkdir(parents=True, exist_ok=True)
    dst = BACKUP / (PLACES.stem + '-' + time.strftime('%Y%m%dT%H%M%S') + PLACES.suffix)
    shutil.copy2(PLACES, dst)
    PLACES.write_text(json.dumps(p, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print('backup', dst.name, '|', ('reverted' if revert else 'wrote'), 'places', [q['id'] for q in places])


if __name__ == '__main__':
    main()
