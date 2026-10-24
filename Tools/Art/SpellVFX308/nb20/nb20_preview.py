#!/usr/bin/env python3
"""Preview of NB20 source renders as they will read: ink (tone L* 10 - 30) / body pigment (L* 60) / coverage, on paper and on a dark soil colour.
python Tools/Art/SpellVFX308/nb20/nb20_preview.py <png> [<png> ...] --out <file.png>   (each input: an RGBA 16-bit Blender render or a packed cell)"""
import sys
from pathlib import Path
import numpy as np
from PIL import Image

HUE = {'Wood': (0x4B, 0x9F, 0x88), 'Fire': (0xDF, 0x71, 0x55), 'Earth': (0xB5, 0x89, 0x3C), 'Metal': (0x89, 0x93, 0x90), 'Water': (0x78, 0x93, 0xAF)}


def load(p):
    im = Image.open(p)
    a = np.asarray(im).astype(np.float32)
    a /= 65535.0 if a.max() > 255 else 255.0
    if a.shape[2] == 3:
        a = np.concatenate([a, np.ones_like(a[..., :1])], 2)
    return a


def compose(a, hue, back):
    tone, pig, cov = a[..., 0], a[..., 1], a[..., 3]
    ink = (12 + 40 * tone)[..., None] * np.ones(3)
    col = np.where((pig > 0.5)[..., None], np.array(hue, np.float32)[None, None, :], ink)
    m = (cov > 0.5)[..., None]
    return np.where(m, col, np.array(back, np.float32)).astype(np.uint8)


def main():
    args = sys.argv[1:]
    out = args[args.index('--out') + 1]; ins = [x for x in args[:args.index('--out')] if not x.startswith('--')]
    rows = []
    for p in ins:
        a = load(p)
        body = next((b for b in HUE if b in Path(p).stem), 'Wood')
        h = 300; w = int(a.shape[1] * h / a.shape[0]); w = min(w, 1400)
        if '--game' in sys.argv:
            # what the engine shows at play distance: the element ~180 px long (mips average the data, then the cut) - upscaled for viewing
            gw = 180; gh = max(8, int(a.shape[0] * gw / a.shape[1]))
            small = np.stack([np.asarray(Image.fromarray((a[..., c] * 255).astype(np.uint8)).resize((gw, gh), Image.BOX)).astype(np.float32) / 255.0 for c in range(4)], 2)
            a = small
        tiles = [Image.fromarray(compose(a, HUE[body], back)).resize((w, h), Image.NEAREST if '--game' in sys.argv else Image.LANCZOS) for back in ((236, 230, 214), (88, 62, 52))]
        row = Image.new('RGB', (w * 2 + 10, h), (0, 0, 0)); row.paste(tiles[0], (0, 0)); row.paste(tiles[1], (w + 10, 0))
        rows.append(row)
    W = max(r.width for r in rows); H = sum(r.height + 6 for r in rows)
    sheet = Image.new('RGB', (W, H), (20, 20, 20)); y = 0
    for r in rows:
        sheet.paste(r, (0, y)); y += r.height + 6
    sheet.save(out)
    print(out)


if __name__ == '__main__':
    main()

