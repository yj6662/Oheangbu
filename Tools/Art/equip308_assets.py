"""#308 장비·소지품 화면 (SPEC-UI-EQUIPMENT-308) textures. Deterministic: fixed seeds, numpy + Pillow only.

White RGB + alpha masks (tint with Image.color, #304 rule: ink monochrome, no emission), 4x supersampled, the #304 brush
edge model (periodic 1/f noise pushed into the shape's distance field; same model as Art/UI304/gen/content_assets.py).
Writes into the STAGE, never into Assets:
  Tools/Unity/Stage308_equip/_ProjectAssets/Art/UI/UI308/Equipment/
    frame_item.png       256x256  항목 그림 테 (광곽 사주쌍변: 바깥선 5 px + 틈 3 px + 안선 1.5 px, 마른 가장자리). Sliced 28.
    mark_effect.png      64x64    절 표시: 효과 (삐침 한 획)
    mark_compare.png     64x64    절 표시: 견줌 (길이가 다른 두 획)
    mark_upgrade.png     64x64    절 표시: 다음 강화 (받침 위로 솟는 쐐기)
    mark_candidates.png  64x64    절 표시: 바꿔 낄 것 (세 줄 목록)
    mark_body.png        64x64    절 표시: 몸과 먹 (머리 + 몸)
    mark_power.png       64x64    절 표시: 속성 위력 (오행 다섯 점)
    mark_tier.png        64x64    절 표시: 보강 (오르는 세 점)
    mark_learned.png     64x64    절 표시: 익힌 것 (종이 한 장 + 글줄)
    import308.json                import settings read by EquipmentSetup308 (Sprite, border, mips)
usage: python Tools/Art/equip308_assets.py            (all)
       python Tools/Art/equip308_assets.py --check    (regenerate in memory and compare with the files: exit 1 on a difference)
       python Tools/Art/equip308_assets.py --preview  (also write the contact sheet Art/UI308/Equipment/assets308_preview.png)"""
import hashlib
import io
import json
import os
import sys

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.normpath(os.path.join(HERE, '..', 'Unity', 'Stage308_equip', '_ProjectAssets', 'Art', 'UI', 'UI308', 'Equipment'))
SS = 4


def fft_noise(h, w, beta, rng):
    """periodic 1/f^beta noise, 0..1"""
    n = rng.standard_normal((h, w))
    F = np.fft.rfft2(n)
    fy = np.fft.fftfreq(h)[:, None]
    fx = np.fft.rfftfreq(w)[None, :]
    f = np.sqrt(fx ** 2 + fy ** 2)
    f[0, 0] = 1.0
    F /= f ** beta
    o = np.fft.irfft2(F, s=(h, w)).astype(np.float32)
    return (o - o.min()) / (o.max() - o.min() + 1e-6)


def grid(px):
    """pixel-centre coordinates of the supersampled px x px canvas, in output px"""
    c = (np.arange(px * SS) + .5) / SS
    return np.meshgrid(c, c)


def down(a, px):
    return a.reshape(px, SS, px, SS).mean(axis=(1, 3))


def ink(dist, rng, px, rough=1.1, dry=.0):
    """distance field (negative inside, output px) -> brush-edged coverage: the edge wobbles by `rough` px of 1/f noise and
    `dry` (0..1) eats pinholes out of the body the way a dry brush does."""
    n = px * SS
    edge = (fft_noise(n, n, 1.6, rng) - .5) * 2.0 * rough
    a = np.clip(.5 - (dist + edge) * SS / 2.0, 0.0, 1.0)
    if dry > 0:
        grain = fft_noise(n, n, .9, rng)
        a *= np.clip((grain - dry * .55) * 6.0 + .5, 0.0, 1.0)
    return a


def seg(x, y, ax, ay, bx, by, r0, r1=None):
    """distance to a tapered capsule from a (radius r0) to b (radius r1)"""
    r1 = r0 if r1 is None else r1
    dx, dy = bx - ax, by - ay
    t = np.clip(((x - ax) * dx + (y - ay) * dy) / (dx * dx + dy * dy), 0.0, 1.0)
    return np.hypot(x - (ax + t * dx), y - (ay + t * dy)) - (r0 + (r1 - r0) * t)


def box(x, y, cx, cy, hw, hh):
    qx, qy = np.abs(x - cx) - hw, np.abs(y - cy) - hh
    return np.hypot(np.maximum(qx, 0), np.maximum(qy, 0)) + np.minimum(np.maximum(qx, qy), 0)


def disc(x, y, cx, cy, r):
    return np.hypot(x - cx, y - cy) - r


def union(*d):
    o = d[0]
    for e in d[1:]:
        o = np.minimum(o, e)
    return o


def mark_effect(x, y):       # one flick: thick start, thin lift
    return union(seg(x, y, 14, 48, 34, 28, 6.5, 4.5), seg(x, y, 34, 28, 52, 13, 4.5, 1.2))


def mark_compare(x, y):      # two strokes of different length
    return union(seg(x, y, 12, 24, 52, 24, 4.2, 3.0), seg(x, y, 12, 42, 36, 42, 4.2, 3.0))


def mark_upgrade(x, y):      # a wedge rising from a base stroke
    return union(seg(x, y, 14, 52, 50, 52, 3.6, 2.6), seg(x, y, 32, 44, 32, 14, 3.8, 2.2),
                 seg(x, y, 32, 12, 20, 26, 3.0, 1.6), seg(x, y, 32, 12, 44, 26, 3.0, 1.6))


def mark_candidates(x, y):   # a list of three
    return union(seg(x, y, 12, 17, 52, 17, 3.6, 2.8), seg(x, y, 12, 32, 52, 32, 3.6, 2.8), seg(x, y, 12, 47, 52, 47, 3.6, 2.8))


def mark_body(x, y):         # head over shoulders
    shoulders = np.maximum(seg(x, y, 21, 47, 43, 47, 10.0), y - 54.0)
    return union(disc(x, y, 32, 19, 9.0), shoulders)


def mark_power(x, y):        # five dabs on a ring = 오행. Not a four-point spark: next to the damage numbers that reads as a shine
    d = None
    for k in range(5):
        ang = -np.pi / 2 + k * 2 * np.pi / 5
        e = disc(x, y, 32 + 19 * np.cos(ang), 33 + 19 * np.sin(ang), 5.6)
        d = e if d is None else np.minimum(d, e)
    return d


def mark_tier(x, y):         # three rising dabs
    return union(disc(x, y, 15, 46, 6.0), disc(x, y, 32, 33, 6.8), disc(x, y, 49, 19, 7.6))


def mark_learned(x, y):      # a sheet with two written lines
    sheet = np.abs(box(x, y, 32, 32, 19, 22)) - 1.9
    return union(sheet, seg(x, y, 22, 25, 42, 25, 2.0, 1.6), seg(x, y, 22, 37, 36, 37, 2.0, 1.6))


MARKS = [('mark_effect', mark_effect, 3081), ('mark_compare', mark_compare, 3082), ('mark_upgrade', mark_upgrade, 3083),
         ('mark_candidates', mark_candidates, 3084), ('mark_body', mark_body, 3085), ('mark_power', mark_power, 3086),
         ('mark_tier', mark_tier, 3087), ('mark_learned', mark_learned, 3088)]
FRAME_PX, FRAME_BORDER, MARK_PX = 256, 28, 64


def build_mark(fn, seed):
    rng = np.random.default_rng(seed)
    x, y = grid(MARK_PX)
    return down(ink(fn(x, y), rng, MARK_PX, rough=1.0, dry=.18), MARK_PX)


def build_frame(seed=3080):
    """사주쌍변: outer line 5 px (centre 6.5 px inside the edge), gap 3 px, inner line 1.5 px; the slices keep the corners."""
    rng = np.random.default_rng(seed)
    px = FRAME_PX
    x, y = grid(px)
    c = px / 2.0
    outer = np.abs(box(x, y, c, c, c - 6.5, c - 6.5)) - 2.5
    inner = np.abs(box(x, y, c, c, c - 13.0, c - 13.0)) - .75
    a = np.maximum(ink(outer, rng, px, rough=.9, dry=.30), ink(inner, rng, px, rough=.35, dry=.12))
    return down(a, px)


def png_bytes(alpha):
    a = (np.clip(alpha, 0, 1) * 255 + .5).astype(np.uint8)
    rgba = np.dstack([np.full_like(a, 255)] * 3 + [a])       # RGB white everywhere: achromatic, no dark bilinear fringe
    buf = io.BytesIO()
    Image.fromarray(rgba, 'RGBA').save(buf, format='PNG', optimize=False)
    return buf.getvalue()


def manifest():
    tex = [{'file': 'frame_item.png', 'kind': 'Sprite', 'border': [FRAME_BORDER] * 4, 'mips': False, 'wrap': 'Clamp', 'size': FRAME_PX}]
    tex += [{'file': name + '.png', 'kind': 'Sprite', 'border': [0, 0, 0, 0], 'mips': True, 'wrap': 'Clamp', 'size': MARK_PX} for name, _, _ in MARKS]
    return (json.dumps({'spec': 'SPEC-UI-EQUIPMENT-308', 'generator': 'Tools/Art/equip308_assets.py', 'textures': tex}, ensure_ascii=False, indent=1) + '\n').encode('utf-8')


def outputs():
    files = {'frame_item.png': png_bytes(build_frame())}
    for name, fn, seed in MARKS:
        files[name + '.png'] = png_bytes(build_mark(fn, seed))
    files['import308.json'] = manifest()
    return files


def preview(files):
    """contact sheet (not a game asset): the eight marks at 128 and at their 24 px use size, the frame on a chip and on the veil"""
    veil, paper, chip = (0x0F, 0x0F, 0x0E), (0xE6, 0xE2, 0xD7), (0xD6, 0xD1, 0xC4)
    sheet = Image.new('RGB', (1536, 764), veil)

    def put(name, x, y, size, color, resample):
        a = Image.open(io.BytesIO(files[name + '.png'])).getchannel('A').resize((size, size), resample)
        sheet.paste(Image.new('RGB', (size, size), color), (x, y), a)

    for k, (name, _, _) in enumerate(MARKS):
        put(name, 32 + k * 192, 32, 128, paper, Image.LANCZOS)
        put(name, 84 + k * 192, 200, 24, paper, Image.LANCZOS)
    sheet.paste(Image.new('RGB', (400, 400), chip), (40, 300))
    put('frame_item', 40, 300, 400, (0x14, 0x14, 0x13), Image.LANCZOS)
    put('frame_item', 520, 300, 400, paper, Image.LANCZOS)
    out = os.path.normpath(os.path.join(HERE, '..', '..', 'Art', 'UI308', 'Equipment', 'assets308_preview.png'))
    os.makedirs(os.path.dirname(out), exist_ok=True)
    sheet.save(out)
    print('wrote', out)


def main():
    check = '--check' in sys.argv[1:]
    files = outputs()
    if '--preview' in sys.argv[1:]:
        preview(files)
    bad = 0
    if not check:
        os.makedirs(OUT, exist_ok=True)
    for name in sorted(files):
        data = files[name]
        path = os.path.join(OUT, name)
        digest = hashlib.sha256(data).hexdigest()[:16]
        if check:
            same = os.path.exists(path) and open(path, 'rb').read() == data
            bad += 0 if same else 1
            print(('SAME ' if same else 'DIFF ') + name, digest)
        else:
            with open(path, 'wb') as f:      # binary write: no newline translation
                f.write(data)
            print('wrote', name, len(data), 'bytes', digest)
    total = sum(len(v) for k, v in files.items() if k.endswith('.png'))
    raw = FRAME_PX * FRAME_PX * 4 + len(MARKS) * MARK_PX * MARK_PX * 4
    print('png bytes', total, '| uncompressed RGBA bytes', raw, '(budget 1048576)')
    sys.exit(1 if bad else 0)


if __name__ == '__main__':
    main()
