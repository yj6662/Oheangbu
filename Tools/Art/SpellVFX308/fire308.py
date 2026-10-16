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

INK = np.array([.10, .09, .08])           # outline ink, darker than soot

SHEETS = {
    # name: (source flipbook, cells x, cells y, style)
    'tongue': ('SmallFlame01/SmallFlame01_16x4.tga', 15, 4, 'photo'),   # named 16x4 but laid out 15 x 4 (empty columns every ~136.5 px)
    # 11.33 method comparison (user 2026-10-16 "다양한 방식을 시도해보고 그 중 맞는 걸 선택"): same shapes, different technique
    'tongue_paint': ('SmallFlame01/SmallFlame01_16x4.tga', 15, 4, 'paint'),   # B: flat painted tones + ragged brushed edge + ink outline
    'tongue_soot': ('SmallFlame01/SmallFlame01_16x4.tga', 15, 4, 'soot'),     # C: soot streaks inside the red, tips turn to soot
    # 11.33 8th pass (user 2026-10-16 "여러 시트를 이용"): one sheet per role of concept D frame 3
    'curl': ('Flame03-hollow/Flame03-hollow_16x4.tga', 16, 5, 'curl'),       # ring around the target: bushy tongues (named 16x4, laid out 16 x 5, no alpha)
    'column': ('DiscSmoke01/DiscSmoke01_16x4.tga', 16, 4, 'column'),         # the thin soot column rising off the target
    'scorch': (None, 4, 4, 'scorch'),                                        # the burnt red-ink mark on the ground (from the ink splat sheet)
    # 9th pass: the DiscSmoke cels were a narrow flat-bottomed blob filling a quarter of the cel -> the column read as a strip of
    # rectangles (lab_fire8). Replaced by a procedural soft puff: round, warped, faded to zero on every side, thinning as it ages
    'puff': (None, 8, 8, 'puff'),
    # 11th pass: the scorch blots were round mid-grey washes that sank into the sepia ground and, spun at random, never ran along
    # the road (lab_fire10). Concept D frame 3's road marks are dark dragged brush streaks with red in their wet heads: 16 streak
    # variants, one per cel, each held for the particle's whole life (texIndex picked at birth, no flipbook play)
    'drag': (None, 4, 4, 'drag'),
    # 11.34 (user 2026-10-16 "네가 자유롭게 만든다면 … 그렇게 진행해줘"): every sheet above re-paints a photographed flame, whose
    # round blob shape piles into lava whatever the handles do. Concept D frame 3's fire is painted tongues - crisp S-curved
    # licks with hooked tips, vermilion streaks along the flow over a deep-red body, a broken ink rim, ink at the root. 4 tongue
    # variants x 16-frame lives (ink -> red soaks up from the root -> sway and curl -> split and thin -> the tip dries to soot)
    'brush': (None, 8, 8, 'brush'),
    # 16th pass: the red-ink pool on the ground the ring stands in (one 16-frame life, laid flat)
    'pool': (None, 4, 4, 'pool'),
}
ASH = np.array([.30, .27, .24])           # thin soot, the lightest tone of the column


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


def lowfreq(h, w, sigma, seed):
    from scipy import ndimage as nd
    n = nd.gaussian_filter(np.random.default_rng(seed).random((h, w)), sigma)
    return (n - n.min()) / max(1e-6, float(n.max() - n.min()))


def paint(a, heat, streak):
    """B: a painted flame - one crisp shape cut from the blurred density by a brushed threshold (ragged dry-brush rim), three flat
    pigment tones (dark red / deep red / vermilion core), an ink outline, a pale wet bleed outside the shape"""
    from scipy import ndimage as nd
    h, w = a.shape
    ab = nd.gaussian_filter(a, 2.0)
    thr = .2 + .16 * (streak - .5) + .12 * (lowfreq(h, w, 3, 11) - .5)
    mask = smooth(thr - .025, thr + .025, ab)
    inner = nd.minimum_filter(mask, size=7)
    outline = np.clip(mask - inner, 0, 1)
    hb = nd.gaussian_filter(heat, 3)
    t1 = smooth(.33, .37, hb); t2 = smooth(.6, .64, hb)                   # narrow steps = painted bands, not gradients
    dark = np.array([.30, .12, .10])
    col = dark[None, None] * (1 - t1[..., None]) + DEEP[None, None] * t1[..., None]
    col = col * (1 - t2[..., None]) + VERMILION[None, None] * t2[..., None]
    col = col * (1 - .2 * (1 - streak)[..., None])                         # bristle marks inside the flat tone
    col = col * (1 - .9 * outline[..., None]) + INK[None, None] * (.9 * outline[..., None])
    bleed = .3 * nd.gaussian_filter(mask, 4) * (1 - mask)
    col = col * mask[..., None] + SOOT[None, None] * (1 - mask[..., None])
    return col, np.maximum(mask, bleed)


def soot(a, heat, streak, cs, tipw=1.15):
    """C: one mixed mass - the same flame colour, but soot streaks run up through the red and the upper part of every tongue turns
    to soot, so the smoke is inside the fire rather than a separate grey carpet"""
    from scipy import ndimage as nd
    h, w = a.shape
    yy = (np.arange(h) % cs)[:, None] / cs + np.zeros((1, w))               # 0 at the cel top (tip), 1 at its base
    tip = 1 - smooth(.3, .72, yy)
    mott = lowfreq(h, w, 6, 23)
    hs = nd.gaussian_filter(heat, 2)
    t1 = smooth(.22, .5, hs); t2 = smooth(.62, .92, hs)
    col = SOOT[None, None] * (1 - t1[..., None]) + DEEP[None, None] * t1[..., None]
    col = col * (1 - t2[..., None]) + VERMILION[None, None] * t2[..., None]
    # (first soot bake: .85 x tip read the same as the photo sheet - the tongues are mostly below the cel middle)
    mix = np.clip(tipw * tip + .75 * smooth(.42, .68, mott) * (1 - .6 * t2) + .45 * (1 - streak) * (1 - t2), 0, 1)
    col = col * (1 - mix[..., None]) + SOOT[None, None] * mix[..., None]
    body = smooth(.03, .6, a)
    edge = 1 - smooth(.15, .55, a)
    alpha = body * (1 - edge * smooth(.35, .75, 1 - streak)) * (1 - .25 * tip)
    return col, alpha


def make(name):
    src, cx, cy, style = SHEETS[name]
    if style == 'brush':
        return make_brush(name)
    if style == 'pool':
        return make_pool(name)
    if style in ('curl', 'column', 'scorch', 'puff', 'drag'):
        return make_role(name, src, cx, cy, style)
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
    if style != 'photo':
        col, alpha = paint(a, heat, streak) if style == 'paint' else soot(a, heat, streak, h // cy)
        return pack(name, np.concatenate([col, alpha[..., None]], -1), h, w, cx, cy, alpha, col)
    # colour: soot at the edges and cool parts, red-brown body, vermilion only where it was hottest
    t1 = smooth(.22, .5, heat)
    t2 = smooth(.62, .92, heat)
    col = SOOT[None, None] * (1 - t1[..., None]) + DEEP[None, None] * t1[..., None]
    col = col * (1 - t2[..., None]) + VERMILION[None, None] * t2[..., None]
    col = col * (1 - .6 * edge[..., None]) + SOOT[None, None] * (.6 * edge[..., None])   # edges sink into ink
    pack(name, np.concatenate([col, alpha[..., None]], -1), h, w, cx, cy, alpha, col)


def pack(name, out, h, w, cx, cy, alpha, col):
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


def make_role(name, src, cx, cy, style):
    """8th pass role sheets. curl / column come from CC0 flipbooks with fractional or tall cels and are re-packed into square
    256 cels (8 x 8, 64 used); scorch re-colours the channel-packed ink splat (R heat, G wet heart, B pooled rim, A density) into
    a plain colour 4 x 4 sheet: a red-ink heart that dries to soot over the 16 cels"""
    if style == 'puff':
        return make_puff(name)
    if style == 'drag':
        return make_drag(name)
    if style == 'scorch':
        img = np.asarray(Image.open(GAME / 'T_V308b_ink_splat.png').convert('RGBA'), np.float32) / 255
        h, w = img.shape[:2]
        cs = h // cy
        k = (np.arange(h)[:, None] // cs) * cx + (np.arange(w)[None, :] // cs)   # cel index, frame 1 top-left
        dry = (k / (cx * cy - 1))[..., None]
        g, b, a = img[..., 1], img[..., 2], img[..., 3]
        heart = DEEP * .65 + VERMILION * .35
        col = INK[None, None] * (1 - smooth(.1, .6, g)[..., None]) + heart[None, None] * smooth(.1, .6, g)[..., None]
        col = col * (1 - .7 * dry) + SOOT[None, None] * (.7 * dry)
        alpha = np.clip(1.05 * a + .3 * b, 0, 1)
        out = (np.clip(np.concatenate([col, alpha[..., None]], -1), 0, 1) * 255 + .5).astype(np.uint8)
        dst = GAME / f'T_V308b_fire_{name}_vfx.png'
        Image.fromarray(out, 'RGBA').save(dst)
        print('SHEET fire', name, '4x4 colour', w, h, 'alpha mean %.3f' % float(alpha.mean()), 'max rgb %.2f' % float(col.max()), dst.name)
        return
    img = np.asarray(Image.open(CC0 / src).convert('RGBA'), np.float32) / 255
    h, w = img.shape[:2]
    lum = img[..., :3] @ np.array([.3, .59, .11])
    streak = bristles(h, w, w // cx)
    edge_brush = lambda a: 1 - (1 - smooth(.15, .55, a)) * smooth(.35, .75, 1 - streak)
    if style == 'curl':
        # additive flame on black: the density is the brightness
        a = smooth(.02, .4, lum)
        col, alpha = soot(a, lum, streak, h / cy, .55)   # the tall bushy tongues fill the cel: 1.15 turned their upper half grey
    else:
        a = img[..., 3]
        col = ASH[None, None] * (1 - smooth(.2, .7, a)[..., None]) + SOOT[None, None] * smooth(.2, .7, a)[..., None]
        col = col * (1 - .6 * smooth(.6, .95, a)[..., None]) + INK[None, None] * (.6 * smooth(.6, .95, a)[..., None])
        col = col * (.85 + .3 * (lum - .5))[..., None]                       # keep the source's light / shadow side as a value shift
        alpha = .9 * smooth(.02, .7, a) * edge_brush(a)
    out = (np.clip(np.concatenate([col, alpha[..., None]], -1), 0, 1) * 255 + .5).astype(np.uint8)
    S, n = 256, 8
    sq = np.zeros((S * n, S * n, 4), np.uint8)
    cw, ch = w / cx, h / cy
    for k in range(min(cx * cy, n * n)):                                     # curl: 80 cels, the first 64 (rows 1-4) kept
        r, c = divmod(k, cx)
        cel = Image.fromarray(out[int(round(r * ch)):int(round((r + 1) * ch)), int(round(c * cw)):int(round((c + 1) * cw))], 'RGBA')
        f = S / max(cel.size)
        if abs(f - 1) > 1e-3:
            cel = cel.resize((max(1, int(round(cel.size[0] * f))), max(1, int(round(cel.size[1] * f)))), Image.LANCZOS)
        cel = np.asarray(cel)
        R, C = divmod(k, n)
        oy, ox = R * S + (S - cel.shape[0]) // 2, C * S + (S - cel.shape[1]) // 2
        sq[oy:oy + cel.shape[0], ox:ox + cel.shape[1]] = cel
    dst = GAME / f'T_V308b_fire_{name}_vfx.png'
    Image.fromarray(sq, 'RGBA').save(dst)
    print('SHEET fire', name, f'{cx}x{cy} -> 8x8 square', sq.shape[1], sq.shape[0], 'alpha mean %.3f' % float(alpha.mean()), 'max rgb %.2f' % float(col.max()), dst.name)


def make_puff(name, S=256, n=8):
    """one soot puff ageing over 64 cels: dense ink heart that swells, tears at the rim and thins to ash. Noise fields are
    advected upward with age so the billows rise inside the puff; every cel is zero on its border (no cut edges)"""
    big = 3 * S
    warp_x, warp_y = lowfreq(big, big, 22, 11), lowfreq(big, big, 22, 12)
    fine, coarse = lowfreq(big, big, 9, 13), lowfreq(big, big, 24, 14)
    yy, xx = (np.mgrid[0:S, 0:S] + .5) / S * 2 - 1                          # cel coords in [-1, 1], y down
    streak = bristles(S, S, S, 21)
    sq = np.zeros((S * n, S * n, 4), np.float32)
    for k in range(n * n):
        t = k / (n * n - 1)
        oy, ox = int(S + S * .6 * (1 - t)), S                               # the window slides down = the billows rise
        win = lambda f: f[oy:oy + S, ox:ox + S]
        wx, wy = (win(warp_x) - .5) * (.35 + .25 * t), (win(warp_y) - .5) * (.35 + .25 * t)
        r = .8 + .12 * t
        d = np.sqrt((xx + wx) ** 2 + ((yy + wy) * 1.08 - .08 * t) ** 2) / r
        dens = (1 - smooth(.1, 1.0, d)) * (.45 + .3 * win(fine) + .5 * win(coarse))
        rim = smooth(.55, 1.0, d)
        dens *= 1 - .55 * rim * smooth(.4, .8, 1 - streak)                   # a dry-brush tear at the rim
        alpha = np.clip(1.7 * dens * (1 - .6 * t), 0, 1)
        alpha *= 1 - smooth(.78, .97, np.maximum(np.abs(xx), np.abs(yy)))    # never touches the cel border
        heart = smooth(.35, .8, dens) * (1 - .7 * t)
        col = ASH[None, None] * (1 - heart[..., None]) + INK[None, None] * heart[..., None]
        col = col * (1 - .35 * t) + ASH[None, None] * (.35 * t) * 1.1
        R, C = divmod(k, n)
        sq[R * S:(R + 1) * S, C * S:(C + 1) * S] = np.concatenate([col, alpha[..., None]], -1)
    out = (np.clip(sq, 0, 1) * 255 + .5).astype(np.uint8)
    dst = GAME / f'T_V308b_fire_{name}_vfx.png'
    Image.fromarray(out, 'RGBA').save(dst)
    print('SHEET fire', name, '8x8 procedural', out.shape[1], out.shape[0], 'alpha mean %.3f' % float(sq[..., 3].mean()), dst.name)

def make_drag(name, S=256, n=4):
    """16 dragged brush streaks laid along the cel's vertical (= the cast direction once the quad lies on the road, local Y
    forward): a pressed ink head at the cel's bottom with wet red pigment in it, a body that thins and splits into dry-brush
    hairs toward the top, a slight wander; zero on every cel border"""
    yy, xx = (np.mgrid[0:S, 0:S] + .5) / S                                  # 0..1, y down (row 0 = cel top = the streak's far end)
    sq = np.zeros((S * n, S * n, 4), np.float32)
    for k in range(n * n):
        rng = np.random.default_rng(100 + k)
        top, bot = .06 + .12 * rng.random(), .8 + .04 * rng.random()        # the streak's own length inside the cel
        u = np.clip((bot - yy) / (bot - top), 0, 1)                         # 0 head .. 1 tail along the streak
        cx = .5 + .06 * (rng.random() - .5) + .05 * np.sin(u * (1.5 + 1.5 * rng.random()) + rng.random() * 6.28) * u
        # fire12: .07-.115 was 8-13 cm on the road and vanished past 6 m; wide again, but the width split into parallel hair
        # strands (the cure for the broom-stub look is the split, not narrowness)
        # fire14: w0 is the half width - .2-.28 filled half the cel and read as broom stubs; ~.15-.2 of the cel across, and the
        # quad's 2.2-3.4 stretch along the road makes it a long thin stroke
        w0 = .075 + .025 * rng.random()
        m = 2 + int(rng.integers(0, 3))                                     # 2-4 strands across the brush
        # flat brush: even width over the first half, then the hairs splay a little and taper; the head end rounded (a pressed
        # brush tip, not a cut)
        wid = w0 * (1 - .35 * smooth(.45, 1.0, u)) * np.sqrt(np.clip((bot + .02 - yy) / .06, 0, 1))
        d = np.abs(xx - cx) / np.maximum(wid, 1e-3)
        inside = (yy > top - .02) * (1 - smooth(.9, 1.0, u)) * (yy < bot + .02)
        body = 1 - smooth(.75, 1.0, d)
        hair = bristles(S, S, S, 40 + k)                                    # streaks along y = the drag direction
        brk = lowfreq(S, S, 5, 500 + k)                                     # the hairs lift off in patches, not all at once
        mott = lowfreq(S, S, 7, 300 + k)
        dry = np.clip(smooth(.3, 1.15, u) + .35 * (brk - .5), 0, 1)          # the brush runs dry toward the tail
        body *= 1 - smooth(.3, .8, dry * (1.25 - hair))
        body *= 1 - .4 * smooth(.55, 1.0, d) * (1 - hair)                   # ragged rim
        # strands across the width, uneven (the phase wanders with the patch noise) and broken by the hairs - even lanes read as a comb
        lane = .5 + .5 * np.cos(np.pi * m * np.clip(d, 0, 1) * (1 + .3 * (brk - .5)) + 1.2 * (mott - .5) + rng.random() * .6)
        split = smooth(.25, .7, u)                                          # one pressed head, strands open toward the dry tail
        body *= 1 - split * smooth(.55, .9, 1 - lane) * (.55 + .45 * hair)
        alpha = np.clip(body * inside * (.95 - .2 * u), 0, 1)
        blob = smooth(.0, .22, .22 - u) * (1 - smooth(.3, .8, d))          # the pressed head
        mott = lowfreq(S, S, 7, 300 + k)
        # fire15: the near heads read as the same bright lozenge repeated - the head is no longer one solid rounded cap (hairs
        # and patches bite into it) and its alpha drops a little, so it stays a pressed brush end instead of a pill
        alpha *= 1 - blob * (.25 + .35 * (1 - hair)) * smooth(.35, .75, brk)
        # fire14: fire12's "red heads read as embers" was judged while the streaks were drawn off screen (11.34 13차) - void.
        # A wet red head again, and the red runs a third of the way down the body before the ink takes over (frame 3's road
        # marks are red-black, not brown)
        wet = .85 * np.clip(blob * smooth(.25, .6, mott) * 1.6
                            + (.45 + .4 * smooth(.4, .75, mott)) * (1 - smooth(.1, .55, u)) * (1 - smooth(.3, .9, d)), 0, 1)
        # fire15: softened - each streak carries its own amount of red (a quarter are plain ink, the rest .35-.8), the red
        # sits in patches rather than a full cap, and the pigment is the deep red more than vermilion (frame 3: red-black)
        red_k = (0., .35, .6, .8)[int(rng.integers(0, 4))]
        wet *= red_k * (.45 + .55 * smooth(.3, .7, lowfreq(S, S, 6, 700 + k)))
        col = INK[None, None] * (1 - wet[..., None]) + (DEEP * .72 + VERMILION * .28)[None, None] * wet[..., None]
        col = col * (1 - .25 * dry[..., None]) + SOOT[None, None] * (.25 * dry[..., None])
        edge = 1 - smooth(.86, .98, np.maximum(np.abs(xx - .5), np.abs(yy - .5)) * 2)
        R, C = divmod(k, n)
        sq[R * S:(R + 1) * S, C * S:(C + 1) * S] = np.concatenate([col, (alpha * edge)[..., None]], -1)
    out = (np.clip(sq, 0, 1) * 255 + .5).astype(np.uint8)
    dst = GAME / f'T_V308b_fire_{name}_vfx.png'
    Image.fromarray(out, 'RGBA').save(dst)
    print('SHEET fire', name, '4x4 procedural streaks', out.shape[1], out.shape[0], 'alpha mean %.3f' % float(sq[..., 3].mean()), dst.name)


HL = np.array([.90, .62, .50])            # thin flow streaks: the most diluted vermilion - still pigment on paper, no light
MAROON = np.array([.26, .09, .07])        # the dark pockets between the painted flame bands
BRICK = np.array([.66, .25, .17])         # the lit ribbon body, a little duller than VERMILION (concept D's red is brick, not orange)


def _spine(t, L, th0, A, k, phase, H, side, base):
    """a ribbon's centre line from its curvature, t 0 = root .. 1 = tip: an S whose sway grows toward the tip (the wave runs up
    as the phase advances) and a tip that curls over in a hook (H rad of turn packed into the last 45 %), integrated so the hook
    really turns instead of sliding sideways"""
    dt = t[1] - t[0]
    kap = A * np.sin(2 * np.pi * k * t - phase) * (.35 + t) + side * H * smooth(.55, 1., t)
    th = th0 + np.cumsum(kap) * dt
    P = np.stack([np.sin(th), -np.cos(th)], -1)
    P = np.cumsum(P, 0) * dt * L
    return P - P[0] + np.array(base)


def _stroke(P, t, wid, S, rag):
    """one painted band along spine P: per pixel the nearest spine parameter, the signed across-coordinate v (-1..1 inside)
    and a crisp alpha (~1.5 px edge, the width wobbled by `rag` so the silhouette is painted, not a tube)"""
    from scipy.spatial import cKDTree
    yy, xx = (np.mgrid[0:S, 0:S] + .5) / S
    q = np.stack([xx.ravel(), yy.ravel()], -1)
    d, i = cKDTree(P).query(q)
    tg = np.gradient(P, axis=0)
    tg /= np.maximum(np.linalg.norm(tg, axis=1, keepdims=True), 1e-6)
    rel = q - P[i]
    sgn = np.sign(tg[i, 0] * rel[:, 1] - tg[i, 1] * rel[:, 0])
    w = wid[i] * (1 + rag.ravel())
    a = np.clip((w - d) * S / 1.5 + .5, 0, 1)
    # past the tip the nearest point is the tip itself: no band there (keeps the point sharp)
    a = np.where((i == len(t) - 1) & (d > w * .5), 0, a)
    v = sgn * d / np.maximum(w, 1e-4)
    return a.reshape(S, S), v.reshape(S, S), t[i].reshape(S, S)


def make_brush(name, S=256, n=8):
    """11.34: 4 painted flame tongues x 16-frame lives on an 8x8 sheet (variant k = texIndex 16k .. 16k+15 = rows 2k, 2k+1).
    A tongue is not one tube but a braid of 4-5 thin brushed ribbons out of one root, painted back to front: a broad dark body
    behind (maroon-ink), then ribbons that curl into hooked tips, each lit on one edge (brick -> vermilion band + a pale broken
    streak along the flow) with a broken ink rim on the other, so dark pockets open wherever ribbons part - concept D frame 3's
    flame bands. Life: 0-3 a small ink tongue rises and the red soaks up from the root, 4-9 full, the S wave running up and the
    ribbons swaying apart, 10-12 the ribbons part and thin, 13-15 the tips dry to soot and break into hairs. Root at the cel's
    bottom centre (quad pivot y -.5); zero on every cel border"""
    yy, xx = (np.mgrid[0:S, 0:S] + .5) / S
    border = 1 - smooth(.9, .985, np.maximum(np.abs(xx - .5), np.abs(yy - .5)) * 2)
    base_y = .935
    N = 300
    t = np.linspace(0, 1, N)
    sq = np.zeros((S * n, S * n, 4), np.float32)
    for var in range(4):
        rng = np.random.default_rng(910 + var)
        side = 1 if var % 2 == 0 else -1
        ph0 = rng.random() * 6.28
        nr = 4
        sw_ = var % 2 == 1                                          # variants 1, 3 lick sideways and hook back (C)
        lean = -side * (.55 + .2 * rng.random()) if sw_ else 0.
        rib = []
        for r in range(nr):
            body = r == 0
            rib.append(dict(
                body=body,
                L=((.45 + .1 * rng.random()) if body else ((.74 + .08 * rng.random()) if r == 1 else (.48 + .18 * rng.random()))) * (.8 if sw_ else 1.),
                th0=(rng.random() - .5) * .2 + (r - (nr - 1) / 2) * .13 * (0 if body else 1) + lean * (.7 if body else 1.),
                A=1.2 if body else 2. + 1.5 * rng.random(),
                k=.7 + .5 * rng.random(),
                ph=ph0 + rng.random() * 2.,
                H=(4. if sw_ else 2.) if body else ((9. + 3. * rng.random()) if r == 1 else (5. + 3. * rng.random())),
                side=(side if (r % 3) else -side),
                W=(.15 + .03 * rng.random()) if body else (.075 + .035 * rng.random()),
                dx=(rng.random() - .5) * .07 - (np.sign(lean) * .13 if sw_ else 0.),
                tone=0. if body else min(1., .3 + .18 * r + .2 * rng.random()),
                lit=(-1 if rng.random() < .5 else 1),
                hairs=rng.random(64),
                hl=(-.8 + .08 * rng.random(), .06 + .04 * rng.random(), .15 + .25 * rng.random(), .7 + .25 * rng.random())))
        mott = lowfreq(S, S, 6, 3000 + var)
        for f in range(16):
            k = var * 16 + f
            grow = smooth(0, 3.5, f)                                    # 0-3 rising
            part = smooth(8.5, 12.5, f)                                 # 10-12 the ribbons part and thin
            dryf = smooth(12, 15.5, f)                                  # 13-15 drying
            col_acc = np.zeros((S, S, 3))
            a_acc = np.zeros((S, S))
            for ri, R_ in enumerate(rib):
                L = R_['L'] * (.35 + .65 * grow) * (1 - .2 * dryf)
                W = R_['W'] * (.55 + .45 * grow) * (1 - .45 * part) * (1 - .3 * dryf)
                if R_['body']:
                    W *= 1 - .4 * part
                phase = R_['ph'] + .6 * f                               # the wave runs up the tongue frame by frame
                th0 = R_['th0'] * (1 + .8 * part) + .05 * np.sin(.7 * f + R_['ph'])
                P = _spine(t, L, th0, R_['A'] * (.5 + .5 * grow), R_['k'], phase, R_['H'] * (.35 + .65 * grow),
                           R_['side'], (.5 + R_['dx'] * (.3 + .7 * grow), base_y))
                wid = W * (.75 + .45 * np.exp(-((t - .4) / .28) ** 2)) * np.clip(1 - t ** 3.2, 0, 1) ** .75
                wid *= 1 + .12 * np.sin(2 * np.pi * (2.1 * t + .05 * f) + R_['ph']) + .07 * np.sin(2 * np.pi * 4.7 * t + 2 * R_['ph'])
                rag = (.04 if R_['body'] else .08) * (lowfreq(S, S, 6 if R_['body'] else 3, 1000 + k * 8 + ri) - .5)
                a, v, tp = _stroke(P, t, wid, S, rag)
                a *= 1 - smooth(base_y - .012, base_y + .02, yy)        # cut flat on the ground line
                hv = np.interp((v + 1.2) * 24 + 2 * np.sin(5 * tp + R_['ph']), np.arange(64), R_['hairs'])
                # dry-brush tip: a little always, the whole upper ribbon when drying
                dry_tip = smooth(.62, 1., tp) * (.4 + .6 * dryf) + dryf * smooth(.2, .6, tp) * .6
                a *= 1 - smooth(.3, .62, dry_tip * (1.3 - hv))
                a *= 1 - smooth(.35, .65, smooth(.72, 1., np.abs(v)) * (1.25 - hv))   # dry-brush streaks along both edges
                # colour, in the ribbon's own stroke space
                vl = v * R_['lit']                                      # -1 = lit edge, +1 = ink edge
                tone = R_['tone']
                m = np.clip(.05 + .65 * tone + .6 * (mott - .5), 0, 1)
                col = MAROON[None, None] * (1 - m[..., None]) + DEEP[None, None] * m[..., None]
                band = smooth(-.3, -.45, vl + .3 * (mott - .5)) * tone
                col = col * (1 - band[..., None]) + BRICK[None, None] * band[..., None]
                edge = smooth(-.68, -.78, vl) * tone
                col = col * (1 - .8 * edge[..., None]) + VERMILION[None, None] * (.8 * edge[..., None])
                c0, sw, ta, tb_ = R_['hl']
                brk = lowfreq(S, S, 4, 5000 + k * 8 + ri)
                line = (1 - smooth(0, sw, np.abs(vl - c0 - .06 * np.sin(6 * tp + .4 * f)))) \
                    * smooth(ta, ta + .08, tp) * (1 - smooth(tb_ - .1, tb_, tp)) * smooth(.35, .5, np.interp(tp * 9 + 3 * ri + .3 * f, np.arange(64), R_['hairs'])) * (tone > .5)
                col = col * (1 - .8 * line[..., None]) + HL[None, None] * (.8 * line[..., None])
                rim = smooth(.55, .7, vl) * smooth(.4, .55, np.interp(tp * 4 + 5 * ri + .15 * f, np.arange(64), R_['hairs'][::-1]) * .6 + .4 * brk)
                col = col * (1 - .45 * rim[..., None]) + MAROON[None, None] * (.45 * rim[..., None])   # shadow side, no ink outline
                # bristle grain along the flow: +-7 % value stripes
                col *= (.93 + .14 * hv)[..., None]
                root = 1 - smooth(.05, .32, tp + .12 * (mott - .5))
                col = col * (1 - .5 * root[..., None]) + MAROON[None, None] * (.5 * root[..., None])
                a *= smooth(.0, .16, tp + .08 * (mott - .5))            # the base fades into the soot wash instead of a hard band
                # 0-3: born ink, the red soaks up from the root
                front = 1.25 * smooth(0, 3.2, f) + .12 * (mott - .5)
                soak = smooth(front, front - .15, tp)
                col = INK[None, None] * (1 - soak[..., None]) + col * soak[..., None]
                dry = dryf * smooth(.2, .75, tp)
                col = col * (1 - .85 * dry[..., None]) + SOOT[None, None] * (.85 * dry[..., None])
                col_acc = col * a[..., None] + col_acc * (1 - a[..., None])
                a_acc = a + a_acc * (1 - a)
            Rr, Cc = divmod(k, n)
            sq[Rr * S:(Rr + 1) * S, Cc * S:(Cc + 1) * S] = np.concatenate([col_acc, (a_acc * border)[..., None]], -1)
    out = (np.clip(sq, 0, 1) * 255 + .5).astype(np.uint8)
    dst = GAME / f'T_V308b_fire_{name}_vfx.png'
    Image.fromarray(out, 'RGBA').save(dst)
    print('SHEET fire', name, '8x8 procedural tongues (4 variants x 16)', out.shape[1], out.shape[0],
          'alpha mean %.3f' % float(sq[..., 3].mean()), 'max rgb %.2f' % float(sq[..., :3].max()), dst.name)

def make_pool(name, S=512, n=4):
    """11.34 16th pass: the red-black wash on the ground the fire ring stands in. Concept D frame 3's ground is not a round
    ink drop (v1-v2: a disc with splash pins read as a cartoon sun / an ink blot) but a pool laid down with the brush: broad
    dark smears dragged round the target, deep-red strokes over them, thin brick-red licks with hooked ends lying on top, all
    with dry-brush streaks along the drag. One 16-frame life, laid flat: 0-3 the strokes are painted on (plain ink first, the
    red soaking up), a soft maroon under-wash spreading between them; 4-11 it holds and creeps; 12-15 it dries to soot and
    lifts in patches from the thin rims inward. Zero on every cel border"""
    from scipy import ndimage as nd
    yy, xx = (np.mgrid[0:S, 0:S] + .5) / S
    rr = np.sqrt((xx - .5) ** 2 + (yy - .5) ** 2)
    border = 1 - smooth(.9, .985, np.maximum(np.abs(xx - .5), np.abs(yy - .5)) * 2)
    rng = np.random.default_rng(1600)
    gran = lowfreq(S, S, 1.3, 1604)                                         # paper grain: the dry break-up
    mott = lowfreq(S, S, 10, 1602)
    N = 240
    t = np.linspace(0, 1, N)
    strokes = []
    # layer, count, radius range, half width range, sweep range, tone (0 ink .. 1 maroon .. 2 deep .. 3 brick lick)
    for layer, cnt, (r0, r1), (w0, w1), (s0, s1), tone in (
            (0, 7, (.08, .33), (.06, .09), (.7, 1.5), 0),
            (1, 2, (0., .06), (.035, .05), (.4, .8), 0),                    # the middle under the target
            (2, 8, (.14, .38), (.03, .05), (.5, 1.2), 1),
            (3, 6, (.16, .36), (.02, .032), (.6, 1.3), 2),
            (4, 5, (.2, .38), (.011, .018), (.7, 1.4), 3)):
        for i in range(cnt):
            a0 = rng.random() * 2 * np.pi
            sw = (s0 + (s1 - s0) * rng.random()) * (1 if rng.random() < .5 else -1)
            r = r0 + (r1 - r0) * rng.random()
            strokes.append(dict(layer=layer, tone=tone, a0=a0, sw=sw, r=r, dr=(rng.random() - .5) * .08,
                                W=w0 + (w1 - w0) * rng.random(), st=(-.9 + rng.random() if layer < 2 else .6 * layer - .6 + 1.2 * rng.random()),
                                hook=(rng.random() - .3) * 2.5 if tone == 3 else 0., hairs=rng.random(64), ph=rng.random() * 6.28,
                                lit=(-1 if rng.random() < .5 else 1)))
    sq = np.zeros((S * n, S * n, 4), np.float32)
    for f in range(n * n):
        dryf = smooth(11, 15.5, f)
        soakf = smooth(1.2, 4.5, f)
        creep = 1 + .04 * smooth(4, 12, f)
        col_acc = np.zeros((S, S, 3))
        a_acc = np.zeros((S, S))
        for si, k in enumerate(strokes):
            vis = smooth(k['st'], k['st'] + 1.6, f)                         # painted on along its length
            if vis <= .02:
                continue
            nv = max(3, int(N * vis))
            tt = t[:nv]
            ang = k['a0'] + k['sw'] * tt + k['hook'] * smooth(.75, 1., tt) * np.sign(k['sw'])
            rad = (k['r'] + k['dr'] * tt + .012 * np.sin(5 * tt + k['ph'])) * creep
            P = np.stack([.5 + rad * np.cos(ang), .5 + rad * np.sin(ang)], -1)
            if k['r'] < .08:                                                # the middle smears: short straight drags
                P = np.stack([.5 + (tt - .5) * .26 * np.cos(k['a0']) + k['r'] * np.cos(k['ph']),
                              .5 + (tt - .5) * .26 * np.sin(k['a0']) + k['r'] * np.sin(k['ph'])], -1)
            wid = k['W'] * creep * np.clip(np.sin(np.pi * np.clip(t[:nv] * .92 + .04, 0, 1)), 0, 1) ** .45 * (1.1 - .25 * tt)
            wid *= 1 + .1 * np.sin(2 * np.pi * 2.3 * tt + k['ph'])
            rag = .1 * (lowfreq(S, S, 3, 2000 + si) - .5)
            a, v, tp = _stroke(P, tt / tt[-1], wid, S, rag)
            hv = np.interp((v + 1.2) * 24 + 2 * np.sin(4 * tp + k['ph']), np.arange(64), k['hairs'])
            # dry-brush: hairs split off along both edges and at the tail of the drag
            a *= 1 - smooth(.35, .65, smooth(.65, 1., np.abs(v)) * (1.3 - hv))
            a *= 1 - smooth(.3, .62, smooth(.7, 1., tp) * (1.25 - hv))
            a *= (.9 if k['tone'] == 0 else .93)
            tone = k['tone']
            base = (INK, MAROON, DEEP, BRICK)[tone]
            col = base[None, None] * np.ones((S, S, 1))
            if tone == 0:
                m = .4 * smooth(.45, .8, mott)
                col = col * (1 - m[..., None]) + MAROON[None, None] * m[..., None]
            if tone == 3:                                                   # the licks: a lit vermilion edge, a maroon shadow edge
                vl = v * k['lit']
                edge = smooth(-.45, -.75, vl)
                col = col * (1 - .75 * edge[..., None]) + VERMILION[None, None] * (.75 * edge[..., None])
                sh = smooth(.4, .8, vl)
                col = col * (1 - .5 * sh[..., None]) + MAROON[None, None] * (.5 * sh[..., None])
            col *= (.9 + .2 * hv)[..., None]                                # bristle grain along the drag
            # born ink: the red soaks up after the stroke lands
            soak = soakf * smooth(k['st'] + .5, k['st'] + 2.5, f)
            col = INK[None, None] * (1 - soak) + col * soak
            col_acc = col * a[..., None] + col_acc * (1 - a[..., None])
            a_acc = a + a_acc * (1 - a)
        # the under-wash: the strokes bleed together into one pool (soft, maroon, under everything)
        wash = nd.gaussian_filter(a_acc, S * .03) * smooth(-.5, 4, f)
        wash = np.clip(wash * 1.6, 0, 1) * .65 * (1 - smooth(.36, .44, rr))
        wcol = MAROON * .6 + INK * .4
        alpha = a_acc + wash * (1 - a_acc)
        # col_acc is already alpha-weighted (premultiplied over): weighting it by a_acc again drew a dark rim round every stroke
        col = (col_acc + wcol[None, None] * (wash * (1 - a_acc))[..., None]) / np.maximum(alpha, 1e-4)[..., None]
        # drying: soot colour; patches lift from the thin rims inward (a moving threshold = crisp lifted patches, not a haze)
        col = col * (1 - .55 * dryf) + SOOT[None, None] * (.55 * dryf)
        field = .5 * gran + .3 * mott + .35 * (1 - np.clip(alpha, 0, 1))
        thr = 1.2 - .85 * dryf
        alpha *= 1 - smooth(thr - .04, thr + .04, field)
        alpha *= .92 + .08 * gran
        R_, C_ = divmod(f, n)
        sq[R_ * S:(R_ + 1) * S, C_ * S:(C_ + 1) * S] = np.concatenate([col, (alpha * border)[..., None]], -1)
    out = (np.clip(sq, 0, 1) * 255 + .5).astype(np.uint8)
    dst = GAME / f'T_V308b_fire_{name}_vfx.png'
    Image.fromarray(out, 'RGBA').save(dst)
    print('SHEET fire', name, '4x4 procedural brushed ground wash (one 16-frame life)', out.shape[1], out.shape[0],
          'alpha mean %.3f' % float(sq[..., 3].mean()), 'max rgb %.2f' % float(sq[..., :3].max()), dst.name)


if __name__ == '__main__':
    for nm in sys.argv[1:] or list(SHEETS):
        make(nm)
