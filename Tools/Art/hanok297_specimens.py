"""Build #297 kit specimens (all LODs + collision) for look review. Output: Finish297/Kit/Specimens/*.json + stats."""
import json, sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
from hanok297_building import BuildingSpec, build_building

OUT = Path(__file__).resolve().parents[2] / 'Art/World/Compact/Rebuild/Finish297/Kit/Specimens'

SPECIMENS = [
    BuildingSpec('hall_palace_5x3', [3.3, 3.6, 4.2, 3.6, 3.3], [3.0, 3.3, 3.0], col_h=4.2, style='palace', roof='palzak', platform_h=1.3,
                 platform_margin=1.6, enterable=True, floor='floor_brick'),
    BuildingSpec('haengnang_7x1', [2.7] * 7, [3.0], col_h=2.9, style='official', roof='matbae', platform_h=.45, platform_margin=.6,
                 walls=dict(front='doors', back='plaster', left='plaster', right='plaster')),
    BuildingSpec('storehouse_3x2', [3.2, 3.2, 3.2], [3.0, 3.0], col_h=3.1, style='military', roof='matbae', platform_h=.6, platform_margin=.5,
                 round_cols=False, walls=dict(front='planks', back='planks', left='planks', right='planks')),
    BuildingSpec('jangdae_3x3', [3.0, 3.6, 3.0], [3.0, 3.6, 3.0], col_h=4.0, style='military', roof='palzak', platform_h=2.2, platform_margin=1.8,
                 stairs=('front',), walls='open', enterable=True, interior_stairs=True,
                 storeys=[dict(h=3.2, inset=0.0, balcony=True, walls=dict(front='window', back='window', left='plaster', right='plaster'))]),
    BuildingSpec('nugak_3storey', [3.0, 3.6, 3.0], [3.0, 3.6, 3.0], col_h=4.2, style='palace', roof='palzak', platform_h=1.6, platform_margin=2.0,
                 walls=dict(front='doors', back='plaster', left='plaster', right='plaster'), open_bays={('front', 1)}, enterable=True, interior_stairs=True,
                 storeys=[dict(h=3.4, inset=.9, balcony=True, walls=dict(front='window', back='window', left='window', right='window')),
                          dict(h=3.1, inset=.7, balcony=True, walls='open')]),
]

if __name__ == '__main__':
    stats = {}
    for spec in SPECIMENS:
        for lod in (0, 1, 2):
            vis, col = build_building(spec, lod)
            vis.export(OUT / f'{spec.name}_LOD{lod}.json')
            if lod == 0: col.export(OUT / f'{spec.name}_Collision.json')
            stats.setdefault(spec.name, {})[f'LOD{lod}'] = vis.tris()
            if lod == 0: stats[spec.name]['collision'] = col.tris()
        import numpy as np
        V = np.asarray(build_building(spec, 2)[0].V); stats[spec.name]['size'] = [round(float(x), 2) for x in (V.max(0) - V.min(0))]
        stats[spec.name]['height'] = round(float(V[:, 1].max()), 2)
    (OUT / 'stats.json').write_text(json.dumps(stats, indent=1), encoding='utf-8')
    print(json.dumps(stats, indent=1))
