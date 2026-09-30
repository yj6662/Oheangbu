"""#302 앞잡 textures: the magic-circle ring (white on alpha, tinted per element at runtime) and an ink splash blob.
Ring: two circles, a band of the five initials ㄱㄴㅁㅅㅇ written round it, ticks, and trigram-like bars inside;
the centre stays empty for the glyph. usage: python Tools/Art/riposte302_textures.py"""
import math, sys
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw, ImageFont
sys.path.insert(0, str(Path(__file__).parent))
import ink302 as ink

OUT = Path(__file__).resolve().parents[2] / 'Oheangbu/Assets/_Project/Art/SpellVFX120/Riposte301'
N = 1024
c = N / 2


def ring():
    m = Image.new('L', (N, N), 0); d = ImageDraw.Draw(m)
    for r, w in [(.485, 18), (.452, 8), (.30, 12), (.268, 6)]:
        R = r * N; d.ellipse((c - R, c - R, c + R, c + R), outline=255, width=w)
    font = ImageFont.truetype(ink.BRUSH, 118)
    band = 'ㄱㄴㅁㅅㅇ' * 3
    for i, ch in enumerate(band):
        a = 2 * math.pi * i / len(band)
        g = Image.new('L', (150, 150), 0); ImageDraw.Draw(g).text((75, 75), ch, font=font, fill=255, anchor='mm')
        g = g.rotate(-math.degrees(a), resample=Image.BICUBIC)
        R = .378 * N; x, y = c + R * math.sin(a), c - R * math.cos(a)
        m.paste(255, (int(x - 75), int(y - 75)), g)
    for i in range(60):                                       # ticks between the circles
        a = 2 * math.pi * i / 60; r0, r1 = (.455 if i % 5 else .43) * N, .472 * N
        d.line((c + r0 * math.sin(a), c - r0 * math.cos(a), c + r1 * math.sin(a), c - r1 * math.cos(a)), fill=255, width=7 if i % 5 == 0 else 4)
    for i in range(8):                                        # 괘-like bars inside the inner circle
        a = 2 * math.pi * (i + .5) / 8
        for k, broken in enumerate([(i >> j) & 1 for j in range(3)]):
            r = (.215 - k * .028) * N; half = .045 * N
            cx, cy = c + r * math.sin(a), c - r * math.cos(a)
            tx, ty = math.cos(a), math.sin(a)
            if broken:
                for s in (-1, 1):
                    d.line((cx + tx * half * s * .25, cy + ty * half * s * .25, cx + tx * half * s, cy + ty * half * s), fill=255, width=11)
            else:
                d.line((cx - tx * half, cy - ty * half, cx + tx * half, cy + ty * half), fill=255, width=11)
    mask = np.asarray(m, np.float32) / 255.0
    return ink._rgba(ink.dry(mask, 302, N * .35, dryness=.35, bleed=.6), 'FFFFFF')


def splash():
    n = 256; rng = np.random.default_rng(7)
    m = Image.new('L', (n, n), 0); d = ImageDraw.Draw(m)
    d.ellipse((n * .3, n * .3, n * .7, n * .7), fill=255)
    for _ in range(14):
        a = rng.random() * 6.283; r = n * (.22 + rng.random() * .22); s = n * (.02 + rng.random() * .05)
        x, y = n / 2 + r * math.cos(a), n / 2 + r * math.sin(a)
        d.ellipse((x - s, y - s, x + s, y + s), fill=255)
        d.line((n / 2, n / 2, x, y), fill=255, width=int(s * 1.2))
    return ink._rgba(ink.dry(np.asarray(m, np.float32) / 255.0, 9, n * .5, dryness=.3, bleed=.8), 'FFFFFF')


ink.save(ring(), OUT / 'MagicCircle302.png')
ink.save(splash(), OUT / 'InkSplash302.png')
print(OUT)


def orb():
    """도깨비불: a soft round flame, dense core, wavering rim (white on alpha, tinted at runtime)."""
    n = 256
    yy, xx = np.mgrid[0:n, 0:n].astype(np.float32)
    dx, dy = (xx - n / 2) / (n / 2), (yy - n / 2) / (n / 2)
    ang = np.arctan2(dy, dx); r = np.sqrt(dx * dx + dy * dy)
    wobble = 1 + .12 * np.sin(ang * 5 + 1.3) + .07 * np.sin(ang * 9 + .4)
    core = np.clip(1 - r / (.34 * wobble), 0, 1) ** .6
    halo = np.clip(1 - r / (.92 * wobble), 0, 1) ** 2.2 * .55
    a = np.clip(np.maximum(core, halo), 0, 1)
    return ink._rgba(a, 'FFFFFF')


ink.save(orb(), OUT / 'WispOrb302.png')
