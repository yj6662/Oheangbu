"""#302 ink graphics for the pitch deck: dry-brush glyphs, brush strokes, an ensō ring and graded game stills.

Everything is generated (no external art): the brush font is HY궁서 B, the dry-brush texture is value noise.
"""
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

BRUSH = r'C:\Windows\Fonts\H2GSRB.TTF'
HANJA = r'C:\Windows\Fonts\HANBatangB.ttf'     # HY궁서 has no hanja


def _hex(h):
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


def _noise(w, h, cw, ch, seed):
    """Value noise with cells of cw x ch pixels (anisotropic when cw != ch)."""
    g = np.random.default_rng(seed).random((max(2, h // ch + 2), max(2, w // cw + 2))).astype(np.float32)
    im = Image.fromarray((g * 255).astype(np.uint8), 'L').resize((w + cw * 2, h + ch * 2), Image.BICUBIC)
    return np.asarray(im, np.float32)[ch:ch + h, cw:cw + w] / 255.0


def _blur(a, r):
    return np.asarray(Image.fromarray((np.clip(a, 0, 1) * 255).astype(np.uint8), 'L').filter(ImageFilter.GaussianBlur(r)), np.float32) / 255.0


def _rgba(alpha, colour):
    h, w = alpha.shape
    out = np.zeros((h, w, 4), np.uint8)
    out[..., :3] = _hex(colour)
    out[..., 3] = (np.clip(alpha, 0, 1) * 255).astype(np.uint8)
    return Image.fromarray(out, 'RGBA')


def dry(mask, seed, size, dryness=.55, bleed=1.2):
    """Solid body; the dry-brush breaks only along the edges (飛白), a faint wet bleed outside."""
    h, w = mask.shape
    core = np.clip((_blur(mask, size * .02) - .7) / .2, 0, 1)
    grain = _noise(w, h, max(2, int(size * .012)), max(2, int(size * .012)), seed) * .6         + np.random.default_rng(seed + 2).random((h, w)).astype(np.float32) * .4
    tex = np.clip((grain - dryness * .6) / .2, 0, 1)
    alpha = mask * (core + (1 - core) * (.25 + .75 * tex))
    halo = _blur(mask, max(1, bleed * size * .01)) * .18
    return np.maximum(alpha, halo * (1 - mask))


def glyph(text, px, colour, seed=1, dryness=.55, vertical=False, gap=.05, font_path=None):
    """A brush-script word as RGBA. px = em size in pixels. vertical stacks the characters."""
    font = ImageFont.truetype(font_path or (HANJA if any('一' <= c <= '鿿' for c in text) else BRUSH), px)
    chars = list(text) if vertical else [text]
    boxes = [font.getbbox(c) for c in chars]
    pad = int(px * .12)
    if vertical:
        w = max(b[2] - b[0] for b in boxes) + pad * 2
        h = sum(b[3] - b[1] for b in boxes) + int(px * gap) * (len(chars) - 1) + pad * 2
    else:
        b = boxes[0]; w, h = b[2] - b[0] + pad * 2, b[3] - b[1] + pad * 2
    m = Image.new('L', (w, h), 0); d = ImageDraw.Draw(m)
    y = pad
    for c, b in zip(chars, boxes):
        x = (w - (b[2] - b[0])) // 2 - b[0] if vertical else pad - b[0]
        d.text((x, y - b[1]), c, font=font, fill=255)
        y += b[3] - b[1] + int(px * gap)
    mask = np.asarray(m, np.float32) / 255.0
    return _rgba(dry(mask, seed, px, dryness), colour)


def lines(texts, px, colour, seed=3, leading=1.35, dryness=.35):
    """Several brush-script lines, left aligned."""
    font = ImageFont.truetype(BRUSH, px)
    boxes = [font.getbbox(t) for t in texts]
    pad = int(px * .1)
    w = max(b[2] for b in boxes) + pad * 2
    h = int(px * leading * (len(texts) - 1) + px * 1.15) + pad * 2
    m = Image.new('L', (w, h), 0); d = ImageDraw.Draw(m)
    for i, t in enumerate(texts):
        d.text((pad, pad + i * px * leading), t, font=font, fill=255)
    return _rgba(dry(np.asarray(m, np.float32) / 255.0, seed, px, dryness, bleed=.8), colour)


def stroke(w, h, colour, seed=5, dry_tail=.8, wave=.08):
    """One horizontal brush stroke: pressed start, bristle streaks, a dry, broken tail."""
    rng = np.random.default_rng(seed)
    t = np.linspace(0, 1, w, dtype=np.float32)
    press = np.clip(t / .05, 0, 1) ** .5 * (1 - np.clip((t - .82) / .18, 0, 1) ** 1.6)
    wobble = 1 + .12 * (_noise(w, 1, max(8, w // 12), 1, seed)[0] - .5) * 2
    thick = h * .42 * press * wobble
    phase = rng.random() * 6.28
    cy = h / 2 + h * wave * np.sin(t * 5.2 + phase)
    y = np.arange(h, dtype=np.float32)[:, None]
    m = np.clip((thick[None, :] - np.abs(y - cy[None, :])) / 1.5, 0, 1)
    bristle = _noise(w, h, max(6, w // 30), 2, seed + 7) * .7 + _noise(w, h, max(4, w // 80), 1, seed + 9) * .3
    thr = .12 + dry_tail * .55 * t[None, :] ** 2
    alpha = m * np.clip((bristle - thr) / .12 + .25 * (1 - t[None, :]), 0, 1)
    return _rgba(alpha, colour)


def enso(px, colour, seed=11, thickness=.1, span=.9):
    """A brush ring (원상), open at the top right; px = diameter in pixels."""
    L = int(np.pi * px); T = int(px * thickness * 1.6)
    s = np.asarray(stroke(L, T, 'FFFFFF', seed, dry_tail=.9, wave=.05))[..., 3].astype(np.float32) / 255.0
    yy, xx = np.mgrid[0:px, 0:px].astype(np.float32)
    c = px / 2; dx, dy = xx - c, yy - c
    r = np.sqrt(dx * dx + dy * dy); a = (np.arctan2(dy, dx) + np.pi * .35) % (2 * np.pi)
    u = (a / (2 * np.pi * span)) * (L - 1)
    R = c - T / 2 - 4                                   # ring centre radius, kept inside the image
    v = r - (R - T / 2)
    inside = (u < L - 1) & (v >= 0) & (v < T - 1)
    out = np.zeros((px, px), np.float32)
    out[inside] = s[v[inside].astype(int), u[inside].astype(int)]
    return _rgba(out, colour)


def graded(src, aspect, width, focus=(.5, .5), dark=0., fade=None, fade_len=.55, fade_colour='141315', contrast=1.12, desat=.35):
    """Crop to aspect, pull toward ink (desaturate, contrast, darken) and fade one side into the page colour."""
    im = Image.open(src).convert('RGB')
    w, h = im.size
    if w / h > aspect:
        nw = int(h * aspect); x = int(min(max(focus[0] * w - nw / 2, 0), w - nw)); im = im.crop((x, 0, x + nw, h))
    else:
        nh = int(w / aspect); y = int(min(max(focus[1] * h - nh / 2, 0), h - nh)); im = im.crop((0, y, w, y + nh))
    im = im.resize((width, int(width / aspect)), Image.LANCZOS)
    a = np.asarray(im, np.float32) / 255.0
    lum = a @ np.array([.2126, .7152, .0722], np.float32)
    a = a * (1 - desat) + lum[..., None] * desat
    a = np.clip((a - .5) * contrast + .5, 0, 1) * (1 - dark)
    if fade:
        H, W = a.shape[:2]
        ramp = {'left': np.linspace(0, 1, W)[None, :], 'right': np.linspace(1, 0, W)[None, :],    # distance from the faded edge
                'bottom': np.linspace(1, 0, H)[:, None], 'top': np.linspace(0, 1, H)[:, None]}[fade]
        k = np.clip(ramp / fade_len, 0, 1)                  # 0 at the faded edge -> 1 inside
        k = k * k * (3 - 2 * k)
        fc = np.array(_hex(fade_colour), np.float32) / 255.0
        a = a * k[..., None] + fc * (1 - k[..., None])
    return Image.fromarray((a * 255).astype(np.uint8))


def save(im, path):
    path = Path(path); path.parent.mkdir(parents=True, exist_ok=True)
    im.save(path, quality=92) if path.suffix == '.jpg' else im.save(path)
    return path


def dry_card(w, h, colour, radius, seed=7, dry_from=.82, lift='2E2C29', vertical=None):
    """A card filled with a 갈필 gradient: dense ink that lightens slightly and breaks into dry bristle marks
    toward the end of the stroke. The stroke runs along the long side (down a tall card). w, h, radius in pixels."""
    if vertical is None:
        vertical = h > w
    if vertical:
        return dry_card(h, w, colour, radius, seed, dry_from, lift, False).transpose(Image.Transpose.ROTATE_270)
    m = Image.new('L', (w, h), 0)
    ImageDraw.Draw(m).rounded_rectangle((0, 0, w - 1, h - 1), radius=radius, fill=255)
    mask = np.asarray(m, np.float32) / 255.0
    t = np.linspace(0, 1, w, dtype=np.float32)[None, :]
    bristle = _noise(w, h, max(8, w // 14), max(2, h // 60), seed) * .65 + _noise(w, h, max(4, w // 40), 1, seed + 3) * .35
    dryness = np.clip((t - dry_from) / (1 - dry_from), 0, 1)
    alpha = mask * np.clip((bristle - dryness * .95) / .08 + (1 - dryness) * 2, 0, 1)
    tone = np.clip((t - .25) / .75, 0, 1) ** 1.5
    c0, c1 = np.array(_hex(colour), np.float32), np.array(_hex(lift), np.float32)
    rgbv = c0[None, None, :] * (1 - tone[..., None]) + c1[None, None, :] * tone[..., None]
    out = np.zeros((h, w, 4), np.uint8)
    rgbv = rgbv + (np.random.default_rng(seed + 5).random((h, w, 1)).astype(np.float32) - .5) * 2   # dither the tone ramp
    out[..., :3] = np.clip(np.broadcast_to(rgbv, (h, w, 3)), 0, 255).astype(np.uint8)
    out[..., 3] = (np.clip(alpha, 0, 1) * 255).astype(np.uint8)
    return Image.fromarray(out, 'RGBA')
