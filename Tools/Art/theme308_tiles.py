# -*- coding: utf-8 -*-
"""SPEC-UI-THEME-308 (D308-15) - kit part 1: the token file and the eight lattice (munsal) tiles. Deterministic.

Writes into the STAGE only (never into Oheangbu/Assets):
  Tools/Unity/Stage308_theme/_ProjectAssets/Art/UI/UI308/Theme/
    theme308_tokens.json          the single source of the theme's values (colours, nacre model, limits, sizes)
    Lattice/lat308_<family>.png   one period per family, white + alpha (alpha = bar coverage), authored at 2x design px
    Lattice/import308.json        import settings for the editor setup command (Sprite, FullRect, Repeat, Alpha8, mips)
    theme308_tiles.json           sizes, open ratio, seam error, crisp share and sha256 of every tile

The bars that sit on a period boundary are drawn fully at the LEFT and at the BOTTOM of the tile (not straddling the edge):
a Tiled Image starts at its bottom-left corner, so a rect of n periods + one bar ends on whole bars on every side.

usage (repo root):  python Tools/Art/theme308_tiles.py            write
                    python Tools/Art/theme308_tiles.py --check    regenerate in memory and compare with the files (exit 1)
"""
import hashlib
import io
import json
import os
import sys

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import theme308_lib as L   # noqa: E402

ROOT = os.path.normpath(os.path.join(HERE, '..', '..'))
OUT = os.path.join(ROOT, 'Tools', 'Unity', 'Stage308_theme', '_ProjectAssets', 'Art', 'UI', 'UI308', 'Theme')
EDGE = L.LATTICE_EDGE                # axis-aligned families: bars at the tile's left / bottom
LIMITS = dict(                       # Spec section 4 (TEST)
    MaxPatternContrastUnderText=1.15,
    StructureMaxContrast=2.2, StructureClearPx=8, OrnamentClearPx=16,
    NacreMaxScreenPctHud=0.20, NacreMaxScreenPctMenu=0.40, FigurativeMotifsPerScreen=1,
    MaxChannel=230, NacreMaxChannel=217, NacreChromaCap=0.034,
    ShimmerDefault=False, ShimmerMaxHueDeg=14.0, ShimmerMaxDegPerSecond=6.0,
)
SIZES = dict(                        # design px at 1080p (TEST); the consuming Specs own the layouts, not these
    NacreLineMin=1.5, InlayLineMin=1.0, LatticeBarMin=1.5, SlotBarV=4.0, SlotBarH=3.0,
    NacreLineThin=1.5, NacreLine=2.0, CollarLine=2.0, RoundelDiameter=44.0, RoundelRim=1.4, RoundelGlyphScale=0.74,
    CornerFret=14.0, PieceNorth=[7.0, 12.0], BoardStep=4.0,
    PlaqueInset=7.0, PlaqueLines=[2.0, 3.0, 1.0], PlaqueFoot=3.0, KeycapRim=2.0, KeycapLip=5.0,
    SelectLine=2.0, SelectOut=3.0, ChrysMin=40.0, PlumMin=20.0, PipPetal=[7.0, 10.0],
    # added with the atlas (theme308_assets.py bakes these; a consumer reads them to place the sprites)
    SlotFrame=6.0, SlotBarClear=8.0, MiniFrame=9.0, MiniLine=[7.0, 8.5], MiniEar=[3.75, 13.75], NorthSeat=2.0,
    NorthInset=7.0, NorthCell=[16.0, 18.0], BoardLineCentre=3.75, BoardBand=[6.0, 16.0], BoardBandModule=3.0, BoardBandBar=1.0,
    BoardBandBottomExtra=2.0, PlateRim=1.5,
    # pieces per Tiled period of the three frames (review 2026-10-04): a rect of slices + n pieces ends on a joint
    MiniPiece=12.0, MiniPeriod=36.0, SelectPiece=10.0, SelectPeriod=30.0, BoardPiece=9.0, BoardPeriod=27.0,
)


def tile_png(name):
    t = L.lattice_tile(name)
    a = np.round(np.clip(t, 0, 1) * 255).astype(np.uint8)
    la = np.dstack([np.full_like(a, 255), a])
    b = io.BytesIO()
    Image.fromarray(la, 'LA').save(b, 'PNG', optimize=False)
    crisp = float(((a == 0) | (a == 255)).mean())
    return b.getvalue(), t, crisp


def tokens():
    hs = np.linspace(L.NACRE['H_centre'] - L.NACRE['H_span'] / 2, L.NACRE['H_centre'] + L.NACRE['H_span'] / 2, 191)
    base = np.array([L.oklch(np.float64(L.NACRE['L']), L.NACRE['C'], h) for h in hs])
    Hr = np.radians(hs)
    G = np.stack([np.ones_like(Hr), np.cos(Hr), np.sin(Hr)], axis=1)
    coef, *_ = np.linalg.lstsq(G, base, rcond=None)
    return dict(
        spec='SPEC-UI-THEME-308', decision='D308-15', status='TEST', generator='Tools/Art/theme308_tiles.py',
        colours=dict(L.TOK308, Nacre=L.to_hex(L.nacre_token())),
        reads_from_UiStyle304=sorted(L.TOK304.keys()),
        nacre=dict(L.NACRE, families=list(L.NACRE['families']), family_share=list(L.NACRE_FAMILY_SHARE),
                   face_chroma=L.NACRE_FACE_CHROMA,
                   shader_fit=dict(N0=[round(float(v), 4) for v in coef[0]], A=[round(float(v), 4) for v in coef[1]],
                                   B=[round(float(v), 4) for v in coef[2]],
                                   form='rgb = N0 + A*cos(h) + B*sin(h), h on the arc, sRGB code values')),
        limits=LIMITS, sizes=SIZES,
        lattice={n: dict(ko=L.LATTICE[n]['ko'], unit_px=L.LATTICE[n]['u'], bar_px=L.LATTICE[n]['bar']) for n in L.LATTICE_ORDER},
    )


def dump(o):
    return (json.dumps(o, ensure_ascii=False, indent=1) + '\n').encode('utf-8')


def build():
    files, rep = {}, {}
    imp = dict(spec='SPEC-UI-THEME-308', generator='Tools/Art/theme308_tiles.py', textures=[])
    for n in L.LATTICE_ORDER:
        png, t, crisp = tile_png(n)
        fn = 'Lattice/lat308_%s.png' % n
        files[fn] = png
        rep[n] = dict(file=fn, ko=L.LATTICE[n]['ko'], size=[int(t.shape[1]), int(t.shape[0])],
                      open_ratio=round(L.open_ratio(t), 4), seam_error=round(L.seam_error(t), 6),
                      crisp_share=round(crisp, 4), edge_anchored=n in EDGE, sha256=hashlib.sha256(png).hexdigest())
        imp['textures'].append(dict(file='lat308_%s.png' % n, kind='Sprite', meshType='FullRect', border=[0, 0, 0, 0],
                                    pixelsPerUnit=100 * L.TILE_SCALE, sRGB=False, alphaSource='FromInput',
                                    alphaIsTransparency=True, mips=True, wrap='Repeat', filter='Trilinear',
                                    format='Alpha8', compression='None', npotScale='None', readable=False,
                                    maxSize=256 if max(t.shape) <= 256 else 512))   # never below the tile's own size
    files['Lattice/import308.json'] = dump(imp)
    files['theme308_tokens.json'] = dump(tokens())
    files['theme308_tiles.json'] = dump(rep)
    return files, rep


def main():
    files, rep = build()
    if '--check' in sys.argv:
        files2, _ = build()
        bad = [k for k in files if files[k] != files2[k]]
        for k, v in files.items():
            p = os.path.join(OUT, k.replace('/', os.sep))
            if not os.path.exists(p) or open(p, 'rb').read() != v:
                bad.append(k)
        print('check:', 'OK (%d files, two builds and the disk agree)' % len(files) if not bad else 'DIFFERENT ' + ', '.join(sorted(set(bad))))
        return 1 if bad else 0
    for k, v in files.items():
        p = os.path.join(OUT, k.replace('/', os.sep))
        os.makedirs(os.path.dirname(p), exist_ok=True)
        with open(p, 'wb') as f:
            f.write(v)
    for n, r in rep.items():
        print('%-8s %-4s %3dx%-3d open %.3f seam %.4f crisp %.3f %s' % (n, r['ko'], r['size'][0], r['size'][1], r['open_ratio'],
                                                                      r['seam_error'], r['crisp_share'], r['sha256'][:12]))
    return 0


if __name__ == '__main__':
    sys.exit(main())
