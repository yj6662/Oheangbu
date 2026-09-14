"""Measure the rendered V4 comparisons, without treating screen RGB as physical luminance."""
import json
from pathlib import Path

import numpy as np
from PIL import Image

OUT = Path(__file__).resolve().parents[2] / 'Art/World/WorldMacro'


def compare(prefix, view):
    a = np.asarray(Image.open(OUT / f'{prefix}{view}.png').convert('RGB'), dtype=np.float32)
    b = np.asarray(Image.open(OUT / f'{view}.png').convert('RGB'), dtype=np.float32)
    assert a.shape == b.shape == (1080, 1920, 3)
    changed = np.max(np.abs(a-b), axis=2) > 2
    assert np.any(changed), f'No visible comparison change: {view}'
    return dict(view=view, before=f'{prefix}{view}.png', after=f'{view}.png',
                changedPixels=int(changed.sum()), changedPixelFraction=float(changed.mean()),
                meanBeforeSRGB=float(a[changed].mean()), meanAfterSRGB=float(b[changed].mean()),
                changedPixelsMeanRatio=float(b[changed].mean()/a[changed].mean()))


if __name__ == '__main__':
    report = dict(
        scope='Same V4 geometry, camera, lighting and distance wash. Previous V3 pigment temporarily restored for tone_before; tint alone disabled for tint_before. RGB averages over pixels changing by >2/255 are screenshot diagnostics, not physical luminance or an isolated terrain mask.',
        material='MacroGround', shader='Oheangbu/WorldMacroTerrain',
        before=dict(InkDensity=1.16, ToneCeiling=.62, PathTones=[.23, .57], RealmTintStrength=0),
        after=dict(InkDensity=1.75, ToneCeiling=.25, PathTones=[.07, .18], RealmTintStrength=.38),
        pairs=[compare('tone_before_', v) for v in ['eye_capital', 'eye_jeokro']],
        tintPairs=[compare('tint_before_', v) for v in ['eye_inn', 'eye_oldcapital']],
        realmPigment=dict(resolution=[256,384], storageMiB=.375, emissive=False,
                          boundaryBlendM=560, additionalLights=0, originalPaletteModified=False))
    (OUT/'tone_comparison.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps(report, ensure_ascii=False, indent=2))
