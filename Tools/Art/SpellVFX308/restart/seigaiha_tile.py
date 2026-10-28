#!/usr/bin/env python3
"""D308-153 NPR test (2026-10-29): a seamless seigaiha (청해파) tile for the stylized 3D ring wave (u_ring_npr.py).
The user: "문양만 사용해서 만들려고 해서 그런 거 같기도 해. 실제 시안은 문양가지고 만든 게 아니거든." - the pattern becomes surface decoration on real 3D waves.

  python Tools/Art/SpellVFX308/restart/seigaiha_tile.py [out.png]

Tile = 2 scales wide x 2 row-pairs tall (scale radius R; rows R/2 apart, odd rows shifted R): painter's order, lower rows in front.
Deep indigo fill darker toward the scale centre, two thin white inner arcs, a cream outline."""
import sys
from pathlib import Path
from PIL import Image, ImageDraw

REPO = Path(__file__).resolve().parents[4]
OUT = Path(sys.argv[1]) if len(sys.argv) > 1 else REPO / 'Art/SpellVFX308/Restart/npr_test/seigaiha.png'
R = 256                       # px per scale radius
W, H = 4 * R, 2 * R           # 2 scales across, 4 rows (2 row pairs) down
SS = 2                        # supersampling
IND_DEEP, IND, CREAM, WHITE = (18, 36, 70), (36, 72, 118), (236, 228, 205), (232, 238, 240)


def main():
    S = SS
    im = Image.new('RGB', (W * S, H * S), IND); d = ImageDraw.Draw(im)
    rows = range(-4, 9)          # extra rows above/below so the tile wraps
    for k in rows:               # top row first: lower rows are drawn later = in front
        cy = k * R / 2
        ox = R if k % 2 else 0
        for c in range(-2, 4):
            cx = c * 2 * R + ox
            for rr, col in ((1.00, CREAM), (0.94, IND_DEEP), (0.92, IND), (0.70, WHITE), (0.665, IND), (0.42, WHITE), (0.385, IND_DEEP), (0.18, IND_DEEP)):
                # concentric discs from the outside in: cream outline, indigo body (rim side lighter), two white lines, the deep centre
                r = rr * R
                fill = col
                if col == IND and rr > 0.9:
                    fill = (32, 64, 108)
                d.ellipse(((cx - r) * S, (cy - r) * S, (cx + r) * S, (cy + r) * S), fill=fill)
    im = im.resize((W, H), Image.LANCZOS)
    OUT.parent.mkdir(parents=True, exist_ok=True)
    im.save(OUT)
    print(OUT, im.size)


if __name__ == '__main__':
    main()
