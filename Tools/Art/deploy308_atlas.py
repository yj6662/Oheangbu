# SPEC-SPELL-DEPLOY-308 / BUILD_PLAN step 0: generated textures for the spell deploy layer. numpy + PIL only, fixed seeds.
# Outputs (Art/SpellVFX120/Deploy308/Generated/, a mirror of Oheangbu/Assets/_Project/Art/SpellVFX120/Deploy308/):
#   ink_deploy_atlas308.png    1024 x 1024 RGB8 LINEAR, 4 x 4 cells of 256 px (8 px margin)
#       R = coverage distance field (.5 = the edge, 16 px of bleed either side)
#       G = wet rim / pooling (1 = dark pooled ink)
#       B = dry order (0 dries first, 1 stays to the end)
#   ink_deploy_atlas308_m.png  512 x 512 (the Mobile tier, same cells)
#   ink_flood_mask308.png      512 x 512 RGB8 LINEAR, R = clearing order (low opens first, stretched vertically), G = vertical grain
#   deploy_atlas308_preview.jpg  ink on paper, for people (never imported)
# Cell (col, row), row 0 at the TOP of the image. In Unity uv (v up) the cell rect is (col/4, 1 - (row+1)/4, 1/4, 1/4).
#   row 0: dry-brush tail a b c d      (U 1 = wet and full, U 0 = scattered hairs; V = width)
#   row 1: drop, tailed drop (head at +U), drop cluster, ground puddle
#   row 2: footprint a (left foot, toe at the top), footprint b, ignition smear, square smear
#   row 3: needle star a, needle star b, wet ring, drip end (column end, full at the top)
# Nothing here is light: the shaders map these channels to ink values at or below the LDR ink ceiling.
import hashlib, math, sys
from pathlib import Path
import numpy as np
from PIL import Image, ImageFilter

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Art' / 'SpellVFX120' / 'Deploy308' / 'Generated'
C, N, MARGIN, BLEED = 256, 4, 8, 16
yy, xx = np.mgrid[0:C, 0:C].astype(np.float32)
u = (xx + .5) / C            # 0 left .. 1 right
v = 1 - (yy + .5) / C        # 0 bottom .. 1 top (Unity uv)


def blur(a, r):
    return np.asarray(Image.fromarray((np.clip(a, 0, 1) * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(r)), np.float32) / 255


def noise(scale, seed, size=C):
    g = np.random.default_rng(seed).random((scale + 1, scale + 1)).astype(np.float32)
    return np.asarray(Image.fromarray((g * 255).astype(np.uint8)).resize((size, size), Image.BICUBIC), np.float32) / 255


def noise2(rows, cols, seed, size=C):
    """Value noise with its own row / column counts (rows >> cols = streaks that run along U)."""
    g = np.random.default_rng(seed).random((rows + 1, cols + 1)).astype(np.float32)
    return np.asarray(Image.fromarray((g * 255).astype(np.uint8)).resize((size, size), Image.BICUBIC), np.float32) / 255


def smooth(a, b, x):
    t = np.clip((x - a) / (b - a), 0, 1)
    return t * t * (3 - 2 * t)


def band_distance(mask, radius=BLEED):
    """Distance (px) from every pixel to the nearest True pixel, capped at radius + 1."""
    h, w = mask.shape
    best = np.full((h, w), radius + 1.0, np.float32)
    pad = np.pad(mask, radius, constant_values=False)
    for dy in range(-radius, radius + 1):
        for dx in range(-radius, radius + 1):
            d = math.hypot(dx, dy)
            if d > radius: continue
            best = np.minimum(best, np.where(pad[radius + dy:radius + dy + h, radius + dx:radius + dx + w], np.float32(d), np.float32(radius + 1)))
    return best


def pack(cov, order=None, wet=None):
    cov = np.clip(cov, 0, 1).astype(np.float32)
    frame = (xx >= MARGIN) & (xx < C - MARGIN) & (yy >= MARGIN) & (yy < C - MARGIN)
    inside = (cov >= .5) & frame
    d_out = band_distance(inside)            # 0 inside
    d_in = band_distance(~inside)            # 0 outside
    sdf = np.clip(.5 + (d_in - d_out) / (2.0 * BLEED), 0, 1)
    soft = np.where(inside, 1.0, 0.0).astype(np.float32)
    if wet is None:
        edge = np.clip(soft - blur(soft, 5), 0, 1) * 3.5           # the pooled rim = coverage minus its own blur
        wet = np.clip(edge + .22 * noise(7, 71), 0, 1)
    if order is None:
        order = blur(soft, 10)                                       # the middle dries last
        order = order / max(float(order.max()), 1e-3)
    return np.stack([sdf, np.clip(wet, 0, 1) * soft, np.clip(order, 0, 1)], -1)


def tail(seed):
    r = np.random.default_rng(seed)
    cov = np.zeros((C, C), np.float32)
    grain = noise2(70, 9, seed * 7 + 3)                              # dry skips are streaks along the stroke, not round specks
    for k in range(24):
        y0 = .08 + .84 * (k + r.random() * .9) / 24.0
        start = r.random() ** 1.4 * .72                              # where this hair begins
        taper = np.clip((u - start) / (1 - start + 1e-3), 0, 1)
        half = (.006 + r.random() * .010) * (.35 + taper * 1.9)
        hair = (np.abs(v - y0 - (1 - u) * (y0 - .5) * .22) < half) & (u > start)
        hair &= grain > (.42 * (1 - taper) ** 1.3)                   # dry skips near the tip
        cov = np.maximum(cov, hair.astype(np.float32))
    cov = np.maximum(cov, ((u > .9) & (np.abs(v - .5) < .46)).astype(np.float32))   # joins the wet body
    # The join (U 1) carries the wet body's own values - light inside, the pooled rim only at the two edges (InkBurst308:
    # rim = the outer 12 % of the half width) - so no dark collar shows where the capsule begins. The dry hairs further out
    # are the dark ones (reference a_025..a_033: grey wet bodies, black streaks).
    spine = np.clip(1 - np.abs(v - .5) / .46, 0, 1)                  # 0 on the outline, 1 on the spine (= the body's uv.y)
    rim = 1 - smooth(.12, .18, spine)
    join = smooth(.60, .93, u)
    wet = rim * join + (.78 + .12 * noise(9, seed + 11)) * (1 - join)
    return pack(cov, order=np.clip(u * .9 + .1 * noise(6, seed + 5), 0, 1), wet=wet)


def blob(cx, cy, rad, lobes, seed, sx=1.0):
    r = np.random.default_rng(seed)
    ang = np.arctan2(v - cy, (u - cx) / sx)
    dist = np.sqrt(((u - cx) / sx) ** 2 + (v - cy) ** 2)
    edge = rad * (1 + lobes * (.55 * np.sin(ang * 3 + r.random() * 6.28) + .3 * np.sin(ang * 5 + r.random() * 6.28) + .15 * np.sin(ang * 9 + r.random() * 6.28)))
    return (dist < edge).astype(np.float32)


def drop_tailed(seed):
    head = blob(.66, .5, .2, .06, seed)
    t = np.clip((u - .1) / .56, 0, 1)
    body = ((np.abs(v - .5) < .17 * t ** 1.6) & (u > .1) & (u < .68)).astype(np.float32)
    return pack(np.maximum(head, body), order=np.clip(u * 1.1, 0, 1))


def cluster(seed):
    r = np.random.default_rng(seed)
    cov = np.zeros((C, C), np.float32)
    for k in range(13):
        cov = np.maximum(cov, blob(.14 + r.random() * .72, .14 + r.random() * .72, .022 + r.random() ** 2 * .07, .08, seed + k))
    return pack(cov)


# Review 2026-10-04: the first version cut the sole with five wide wavy bands, which read as the moulded tread of a rubber
# sole (and the Spec had already turned down a striped footprint). Off = a plain mottled sole; True brings the bands back.
FOOT_WEAVE = False


def footprint(seed):
    """Left straw-sandal sole filling the cell: ball at the top, heel at the bottom, narrow arch (inner side at +U)."""
    r = np.random.default_rng(seed)
    px, py = (u - .5) * 2, (v - .5) * 2                              # -1..1
    ball = ((px + .04) / .80) ** 2 + ((py - .40) / .52) ** 2 < 1
    heel = ((px + .06) / .58) ** 2 + ((py + .60) / .33) ** 2 < 1
    # the outer edge of the sole joins ball and heel; the inner arch (+U on a left foot) leaves no ink
    outer = (px > -.74 + .10 * np.cos(py * 2.2)) & (px < -.02 + .16 * np.cos((py + .1) * 3.0)) & (np.abs(py + .10) < .50)
    sole = (ball | heel | outer).astype(np.float32)
    weave = (np.sin(py * 21 + noise(6, seed + 1) * 3.0) > -.90).astype(np.float32) if FOOT_WEAVE else np.ones((C, C), np.float32)
    grain = (noise(34, seed + 2) > .14 + .06 * r.random()).astype(np.float32)           # dry specks where the sole did not touch
    press = np.exp(-((py - .40) / .42) ** 2) + np.exp(-((py + .60) / .30) ** 2)         # ball and heel carry the ink
    solid = (press > .92) if FOOT_WEAVE else (press > .97) & (grain > 0)
    cov = sole * np.maximum(weave * grain, solid.astype(np.float32))
    order = np.clip(press * .75 + noise(10, seed + 3) * .25, 0, 1)                      # the arch dries first
    return pack(cov, order=order, wet=np.clip(press - .35, 0, 1))


def needle_star(rays, seed):
    r = np.random.default_rng(seed)
    cov = blob(.5, .5, .035, .1, seed)
    for k in range(rays):
        a = (k + r.random() * .5) / rays * 2 * math.pi
        ln = .30 + r.random() * .15
        dx, dy = math.cos(a), math.sin(a)
        px, py = u - .5, v - .5
        t = px * dx + py * dy
        d = np.abs(-px * dy + py * dx)
        w = .013 * (1 - np.clip(t / ln, 0, 1)) + .0035
        cov = np.maximum(cov, ((d < w) & (t > 0) & (t < ln)).astype(np.float32))
    rad = np.sqrt((u - .5) ** 2 + (v - .5) ** 2)
    return pack(cov, order=np.clip(1 - rad * 2.1, 0, 1), wet=np.clip(1 - rad * 5, 0, 1))


def ring(seed):
    ang = np.arctan2(v - .5, u - .5)
    rad = np.sqrt((u - .5) ** 2 + (v - .5) ** 2)
    thick = .05 + .028 * np.sin(ang * 2 + 1.3) + .012 * np.sin(ang * 7 + .4)
    cov = ((np.abs(rad - .33) < thick) & ~((ang > 2.2) & (ang < 2.75))).astype(np.float32)     # the brush lifts: a gap
    return pack(cov, order=np.clip(.5 + .5 * np.sin(ang - .6), 0, 1))


def square_smear(seed):
    px, py = np.abs(u - .5), np.abs(v - .5)
    n = noise(11, seed) * .035
    outer = np.maximum(px, py) < .37 + n
    inner = np.maximum(px, py) < .24 - n
    return pack((outer & ~inner).astype(np.float32))


def drip_end(seed):
    prof = noise(5, seed)[40][None, :] * .55 + noise(13, seed + 1)[90][None, :] * .45      # per-column drip length
    bottom = .82 - prof * .78
    col = v > bottom
    round_end = np.zeros((C, C), bool)                                                    # round the finger ends
    for cx in np.arange(.06, 1.0, .11):
        b = float(.82 - (noise(5, seed)[40][int(cx * C)] * .55 + noise(13, seed + 1)[90][int(cx * C)] * .45) * .78)
        round_end |= (u - cx) ** 2 + (v - b) ** 2 < .045 ** 2
    return pack((col | round_end).astype(np.float32), order=np.clip(v, 0, 1), wet=np.clip(1 - v * 1.4, 0, 1))


def build_atlas():
    cells = [
        [tail(101), tail(102), tail(103), tail(104)],
        [pack(blob(.5, .5, .34, .05, 201)), drop_tailed(202), cluster(203), pack(blob(.5, .5, .27, .16, 204, sx=1.55))],
        [footprint(301), footprint(302), pack(np.maximum(blob(.5, .5, .25, .22, 303), cluster(304)[..., 0] >= .5)), square_smear(305)],
        [needle_star(6, 401), needle_star(8, 402), ring(403), drip_end(404)],
    ]
    atlas = np.zeros((N * C, N * C, 3), np.float32)
    for row in range(N):
        for col in range(N):
            atlas[row * C:(row + 1) * C, col * C:(col + 1) * C] = cells[row][col]
    return atlas


def build_flood(size=512):
    low = np.asarray(Image.fromarray((np.random.default_rng(900).random((4, 13)) * 255).astype(np.uint8)).resize((size, size), Image.BICUBIC), np.float32) / 255
    mid = np.asarray(Image.fromarray((np.random.default_rng(901).random((9, 41)) * 255).astype(np.uint8)).resize((size, size), Image.BICUBIC), np.float32) / 255
    order = np.clip(low * .75 + mid * .25, 0, 1)
    order = (order - order.min()) / max(float(order.max() - order.min()), 1e-3)
    grain_line = np.random.default_rng(902).random(size).astype(np.float32)
    grain = np.tile(grain_line[None, :], (size, 1))
    grain = np.asarray(Image.fromarray((grain * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(1.2)), np.float32) / 255
    grain = np.clip(grain * .8 + .2 * noise(64, 903, size), 0, 1)
    return np.stack([order, grain, np.zeros_like(order)], -1)


def save(arr, path):
    Image.fromarray((np.clip(arr, 0, 1) * 255 + .5).astype(np.uint8), 'RGB').save(path)
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
    OUT.mkdir(parents=True, exist_ok=True)
    atlas = build_atlas()
    print('ink_deploy_atlas308.png   sha256 %s' % save(atlas, OUT / 'ink_deploy_atlas308.png'))
    half = np.asarray(Image.fromarray((np.clip(atlas, 0, 1) * 255 + .5).astype(np.uint8), 'RGB').resize((512, 512), Image.LANCZOS), np.float32) / 255
    print('ink_deploy_atlas308_m.png sha256 %s' % save(half, OUT / 'ink_deploy_atlas308_m.png'))
    print('ink_flood_mask308.png     sha256 %s' % save(build_flood(), OUT / 'ink_flood_mask308.png'))
    # preview the way the shaders compose it: coverage from the distance field, the wet rim darker, on paper
    cov = np.clip((atlas[..., 0:1] - .5) * 2 * BLEED + .5, 0, 1)
    wet = atlas[..., 1:2]
    ink = np.array([.20, .19, .18], np.float32) * (1 - wet) + np.array([.05, .045, .04], np.float32) * wet
    paper = np.array([.88, .86, .81], np.float32)
    left = paper * (1 - cov) + ink * cov
    dry = np.clip((atlas[..., 2:3] - .5) * 12 + .5, 0, 1) * cov          # the same cells half-way through drying (B > .5 left)
    right = paper * (1 - dry) + (ink * .6 + paper * .4) * dry
    sheet = np.concatenate([left, right], 1)
    Image.fromarray((np.clip(sheet, 0, 1) * 255).astype(np.uint8), 'RGB').save(OUT.parent / 'deploy_atlas308_preview.jpg', quality=88)
    filled = [(float((atlas[r * C:(r + 1) * C, c * C:(c + 1) * C, 0] >= .5).mean())) for r in range(N) for c in range(N)]
    print('cell coverage ' + ' '.join('%.2f' % f for f in filled))


if __name__ == '__main__':
    main()
