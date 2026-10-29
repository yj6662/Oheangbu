#!/usr/bin/env python3
"""D308-153b route P (KCISA pattern 우): channel-packed sprites cut from the KCISA pattern pack (copies - the pack is never touched) + the ink stroke.

  python Tools/Art/SpellVFX308/restart/u_p_textures.py

Pattern sprites (Assets/_Project/Art/SpellVFX308/Restart/U/P/p_<id>.png), 1024 px, RGB = three masks:
  R = the pack's white line art, G = the motif's filled inside (everything not reachable from the border), B = the fill grown outward (the cream silhouette rim).
  The shader paints B in cream, G in indigo and R in white on top: the pack draws only lines, the fills are ours.
Ink: ink_stroke.png = fs_sumi_stroke's broad bottom row, inked left 60 % only (the r5 finding: the right 40 % is empty paper)."""
from pathlib import Path
from PIL import Image, ImageChops, ImageDraw, ImageFilter

REPO = Path(__file__).resolve().parents[4]
PACK = REPO / 'Oheangbu/Assets/KoreanTraditionalPattern_Effect/Textures/TraditionalTexture'
STROKE = REPO / 'Oheangbu/Assets/_Project/Art/SpellVFX308/Vol2Vfx/FreeShowcase/Resources/FreeShowcase/fs_sumi_stroke.png'
OUT = REPO / 'Oheangbu/Assets/_Project/Art/SpellVFX308/Restart/U/P'
IDS = {232: 'cloud scroll (crest cap)', 226: 'three-armed whirl (burst)', 247: 'swirl roundel (burst)'}
S, PAD, RIM = 1024, 40, 22


def sprite(n):
    a = Image.open(PACK / f'Pattern_{n}.png').convert('RGBA').getchannel('A')
    a.thumbnail((S - 2 * PAD - 2 * RIM, S - 2 * PAD - 2 * RIM), Image.LANCZOS)
    line = Image.new('L', (S, S), 0); line.paste(a, ((S - a.width) // 2, (S - a.height) // 2))
    # inside = not reachable from the border through non-line pixels (lines thickened a little so hairline gaps do not leak)
    wall = line.filter(ImageFilter.MaxFilter(5)).point(lambda v: 255 if v > 40 else 0)
    flood = wall.copy(); ImageDraw.floodfill(flood, (0, 0), 128)
    fill = flood.point(lambda v: 0 if v == 128 else 255)
    rim = fill.filter(ImageFilter.MaxFilter(2 * RIM + 1)).filter(ImageFilter.GaussianBlur(1.5))
    fill = fill.filter(ImageFilter.GaussianBlur(1.0))
    line = line.filter(ImageFilter.MaxFilter(5))      # the pack's hairlines vanish at game size: thicken the drawn line only
    Image.merge('RGB', (line, fill, rim)).save(OUT / f'p_{n}.png')
    return fill


def ink():
    im = Image.open(STROKE).convert('RGBA'); w, h = im.size
    band = im.crop((int(0.0 * w), int(h * (1 - 0.238)), int(0.6 * w), int(h * (1 - 0.012))))
    a = band.getchannel('A')
    out = Image.merge('RGBA', (Image.new('L', a.size, 255),) * 3 + (a,))
    out.save(OUT / 'ink_stroke.png')
    return a.size


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    for n, what in IDS.items():
        f = sprite(n)
        px = f.get_flattened_data() if hasattr(f, 'get_flattened_data') else f.getdata()
        print(f'p_{n}.png', what, 'fill %.0f %%' % (100 * sum(1 for v in px if v > 128) / (S * S)))
    print('ink_stroke.png', ink())


if __name__ == '__main__':
    main()
