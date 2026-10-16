#!/usr/bin/env python3
"""#308 노 fire tongues as red pigment ink (D308-89, Spec 11.33; user 2026-10-16 "현실적인 불같은 효과, 동양적인 이펙트 추가,
붉은 기운도 섞이도록" -> no light, red pigment only, ink-brush flame tongues; concept D "붓 획이 불붙음").

  python Tools/Art/SpellVFX308/fire308.py [name ...]      # default: tongue

Source = a Unity Labs 2016 CC0 flame flipbook (Art/SpellVFX308/Assets_CC0/unity_labs_2016, local only). Its picture is glowing
yellow fire, so only its shape is kept: alpha = the flame's density, heat = its brightness. The output is a non-emissive colour
sheet for the VFX Graph quad output (texture x particle colour, alpha blend): hot core -> vermilion pigment, cooler body -> deep
red-brown, edges -> soot ink, and the outer edge split into dry-brush streaks along the flame's rise (갈필). Nothing in it is
brighter than the pigment (ART-INK: the fire gives no light)."""
import sys
from pathlib import Path
import numpy as np
from PIL import Image

REPO = Path(__file__).resolve().parents[3]
CC0 = REPO / 'Art/SpellVFX308/Assets_CC0/unity_labs_2016'
GAME = REPO / 'Oheangbu/Assets/_Project/Art/SpellVFX308/V2/Textures'

VERMILION = np.array([.72, .26, .18])     # #B8432F
DEEP = np.array([.43, .16, .12])          # #6E2A1F
SOOT = np.array([.165, .15, .135])        # #2A2622

SHEETS = {
    # name: (source flipbook, cells x, cells y)
    'tongue': ('SmallFlame01/SmallFlame01_16x4.tga', 15, 4),   # named 16x4 but laid out 15 x 4 (empty columns every ~136.5 px)
}


def smooth(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0, 1)
    return t * t * (3 - 2 * t)


def bristles(h, w, cw, seed=7):
    """streak noise stretched along the flame's rise (y): split brush bristles, one field per cel so cels do not tile"""
    rng = np.random.default_rng(seed)
    cols = rng.random((1, w)).repeat(h, 0)
    # a few coarse bristle bundles + fine hairs, slowly wandering sideways up the cel
    x = np.arange(w)[None, :] + 3.0 * np.sin(np.arange(h)[:, None] / 37.0 + (np.arange(w)[None, :] // cw))
    bundle = .5 + .5 * np.sin(x / 2.3 + rng.random() * 6.28) * np.sin(x / 7.1)
    fine = np.take_along_axis(cols, np.clip(x.astype(int), 0, w - 1), 1)
    return np.clip(.55 * bundle + .45 * fine, 0, 1)


def make(name):
    src, cx, cy = SHEETS[name]
    img = np.asarray(Image.open(CC0 / src).convert('RGBA'), np.float32) / 255
    h, w = img.shape[:2]
    a = img[..., 3]
    heat = img[..., :3] @ np.array([.3, .59, .11])                         # luminance: core ~.9, body ~.5, licks ~.25
    # (first bake used max(rgb) as heat and a hard alpha step: every tongue became one flat vermilion lump)
    body = smooth(.03, .6, a)                                              # keeps the inner density variation
    # dry-brush edge: in the outer band the alpha breaks into bristle streaks (none in the core)
    edge = 1 - smooth(.15, .55, a)
    streak = bristles(h, w, w // cx)
    alpha = body * (1 - edge * smooth(.35, .75, 1 - streak))
    # colour: soot at the edges and cool parts, red-brown body, vermilion only where it was hottest
    t1 = smooth(.22, .5, heat)
    t2 = smooth(.62, .92, heat)
    col = SOOT[None, None] * (1 - t1[..., None]) + DEEP[None, None] * t1[..., None]
    col = col * (1 - t2[..., None]) + VERMILION[None, None] * t2[..., None]
    col = col * (1 - .6 * edge[..., None]) + SOOT[None, None] * (.6 * edge[..., None])   # edges sink into ink
    out = np.concatenate([col, alpha[..., None]], -1)
    # the source cels are tall (136.5 x 256) but the graph quad is square, so as baked 15 x 4 every tongue was stretched 1.9x
    # sideways into a squat red lump (lab_fire1-4). Re-pack each cel, centred and unscaled, into a square 256 cel on an 8 x 8 sheet
    # (60 used, the last 4 empty - the 30 fps flipbook reaches ~42 in a 1.4 s life)
    out = (np.clip(out, 0, 1) * 255 + .5).astype(np.uint8)
    cs = h // cy
    sq = np.zeros((cs * 8, cs * 8, 4), np.uint8)
    for k in range(cx * cy):
        r, c = divmod(k, cx)
        x0, x1 = int(round(c * w / cx)), int(round((c + 1) * w / cx))
        cel = out[r * cs:(r + 1) * cs, x0:x1]
        R, C = divmod(k, 8)
        ox = C * cs + (cs - cel.shape[1]) // 2
        sq[R * cs:(R + 1) * cs, ox:ox + cel.shape[1]] = cel
    dst = GAME / f'T_V308b_fire_{name}_vfx.png'
    Image.fromarray(sq, 'RGBA').save(dst)
    print('SHEET fire', name, f'{cx}x{cy} -> 8x8 square', sq.shape[1], sq.shape[0], 'alpha mean %.3f' % float(alpha.mean()), 'max rgb %.2f' % float(col.max()), dst.name)


if __name__ == '__main__':
    for nm in sys.argv[1:] or list(SHEETS):
        make(nm)
