#!/usr/bin/env python3
"""D308-150 V12 cast camera treatments media: engine frames (Vol2VfxCapture through Tools/Unity/Vol2CastCameraStage/capture.ps1,
out=camera_treatments/<round>/<treatment>) -> comparison videos (1x then 0.5x) and stills, current camera vs each treatment, same spells, same ages.

  python Tools/Art/SpellVFX308/cast_camera_media308.py raw                      # all
  python Tools/Art/SpellVFX308/cast_camera_media308.py raw --only grid          # (grid | pairs | stills)
  python Tools/Art/SpellVFX308/cast_camera_media308.py soft                     # V13: A_soft/<key>_3way.mp4 (지금 | A V12 | A 다듬음), A_soft_vs_current_all.mp4, moments_<key>.jpg
Out: Art/SpellVFX308/Vol2Vfx/camera_treatments/
  <T>_vs_current.mp4          current | treatment side by side, 나 -> 고 -> 각, each 1x then 0.5x (T = A B C D AB)
  grid_<spell>.mp4            current + A + B + C + D + AB in one 3 x 2 grid, 1x then 0.5x
  camera_treatments_all.mp4   the three grids one after the other
  moments_<spell>.jpg         rows = current, A, B, C, D, AB; columns = three moments of the cast"""
import csv, sys
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

REPO = Path(__file__).resolve().parents[3]
BASE = REPO / 'Art/SpellVFX308/Vol2Vfx/camera_treatments'
SPELLS = [  # key, job folder, label, last age shown (s), still moments (s)
    ('na', 'P0_bridge_Fire_red', '나 · 불 창 (경로 · 한 번)', 2.2, [0.20, 0.40, 0.80]),
    ('go', 'F0_bridge_Wood_red', '고 · 죽순 부채꼴 (면 · 한 번)', 2.2, [0.45, 0.70, 1.20]),
    ('gak', 'P1_bridge4_Wood_red', '각 · 가지 새장 (경로 · 남긴다)', 2.6, [0.40, 0.80, 1.80]),
]
TREAT = [('off', '지금 카메라'), ('A', 'A 틀 잡기 (위 · 뒤 · 오른쪽)'), ('B', 'B 시전자 흐리기'), ('C', 'C 시야각 넓힘 + 고개 숙임'), ('D', 'D 짧은 옆 시점 (큰 술식용)'),
         ('AB', 'A + B 함께')]
FONT = 'C:/Windows/Fonts/malgunbd.ttf'


def font(size):
    try:
        return ImageFont.truetype(FONT, size)
    except OSError:
        return ImageFont.load_default()


def tag(img, text, sub=None, size=22):
    dr = ImageDraw.Draw(img); f = font(size)
    bb = dr.textbbox((10, 8), text, font=f)
    dr.rectangle((bb[0] - 6, bb[1] - 4, bb[2] + 6, bb[3] + 4), fill=(0, 0, 0)); dr.text((10, 8), text, font=f, fill=(255, 255, 255))
    if sub:
        f2 = font(max(14, size - 6)); y = img.height - size - 10
        bb = dr.textbbox((10, y), sub, font=f2)
        dr.rectangle((bb[0] - 6, bb[1] - 4, bb[2] + 6, bb[3] + 4), fill=(0, 0, 0)); dr.text((10, y), sub, font=f2, fill=(255, 230, 200))
    return img


def frames(rnd, t, job, last):
    d = BASE / rnd / t / 'frames' / job
    if not (d / 'index.csv').exists():
        return []
    with open(d / 'index.csv', encoding='utf-8', errors='replace') as f:
        rs = [r for r in csv.DictReader(f) if r.get('video') == '1' and float(r['ageSec']) <= last + 1e-6]
    return [(float(r['ageSec']), d / ('f_%05d.jpg' % int(r['frame']))) for r in rs if (d / ('f_%05d.jpg' % int(r['frame']))).exists()]


def nearest(seq, age):
    return min(seq, key=lambda x: abs(x[0] - age))[1] if seq else None


def write_mp4(dst, ims, size, fps=60):
    import imageio_ffmpeg
    gen = imageio_ffmpeg.write_frames(str(dst), size, fps=fps, codec='libx264', pix_fmt_out='yuv420p', output_params=['-crf', '20', '-preset', 'medium'], macro_block_size=8)
    gen.send(None)
    n = 0
    for im in ims:
        gen.send(im.convert('RGB').tobytes()); n += 1
    gen.close()
    print('mp4', dst, n)


def one_then_half(make, ages):
    for a in ages:
        yield make(a, '1×')
    for a in ages:
        im = make(a, '0.5×')
        yield im
        yield im


def pairs(rnd):
    for t, tname in TREAT[1:]:
        def gen():
            for key, job, label, last, _ in SPELLS:
                base = frames(rnd, 'off', job, last); other = frames(rnd, t, job, last)
                if not base or not other:
                    print('missing', t, key); continue
                def make(age, speed):
                    canvas = Image.new('RGB', (1920, 540), (16, 16, 16))
                    a = Image.open(nearest(base, age)).resize((954, 536)); b = Image.open(nearest(other, age)).resize((954, 536))
                    tag(a, '지금 카메라 · ' + speed, label + ' · t = %.2f s' % age); tag(b, tname + ' · ' + speed, label + ' · t = %.2f s' % age)
                    canvas.paste(a, (2, 2)); canvas.paste(b, (964, 2))
                    return canvas
                yield from one_then_half(make, [x[0] for x in base])
        write_mp4(BASE / f'{t}_vs_current.mp4', gen(), (1920, 540))


def grid_frames(rnd, key, job, label, last):
    seqs = {t: frames(rnd, t, job, last) for t, _ in TREAT}
    base = seqs['off']
    if not base:
        print('missing off', key); return
    def make(age, speed):
        canvas = Image.new('RGB', (1920, 720), (16, 16, 16))
        for i, (t, tname) in enumerate(TREAT[:6]):
            p = nearest(seqs[t], age)
            tile = Image.open(p).resize((636, 356)) if p else Image.new('RGB', (636, 356), (40, 0, 0))
            tag(tile, tname, (label + ' · t = %.2f s · ' % age + speed) if i == 0 else None, 18)
            canvas.paste(tile, ((i % 3) * 640 + 2, (i // 3) * 360 + 2))
        return canvas
    yield from one_then_half(make, [x[0] for x in base])


def grids(rnd):
    allims = []
    for key, job, label, last, _ in SPELLS:
        ims = list(grid_frames(rnd, key, job, label, last) or [])
        if ims:
            write_mp4(BASE / f'grid_{key}.mp4', ims, (1920, 720))
            allims += ims
    if allims:
        write_mp4(BASE / 'camera_treatments_all.mp4', allims, (1920, 720))


def stills(rnd):
    for key, job, label, last, moments in SPELLS:
        tw, th = 640, 360
        out = Image.new('RGB', (tw * len(moments) + 220, th * len(TREAT)), (16, 16, 16))
        dr = ImageDraw.Draw(out)
        for row, (t, tname) in enumerate(TREAT):
            seq = frames(rnd, t, job, last)
            dr.text((10, row * th + 20), tname, font=font(20), fill=(255, 255, 255))
            for col, age in enumerate(moments):
                p = nearest(seq, age)
                if p is None:
                    continue
                im = tag(Image.open(p).resize((tw - 4, th - 4)), '%.2f s' % age, None, 18)
                out.paste(im, (220 + col * tw + 2, row * th + 2))
        dst = BASE / f'moments_{key}.jpg'
        out.save(dst, quality=90)
        print('still', dst)


# ---- D308-150 V13 (A softened = the vol.2 default): camera_treatments/A_soft/raw/<off|A12|A>/frames/<job> -> 3-way videos (지금 | A V12 | A 다듬음) ----
#   python Tools/Art/SpellVFX308/cast_camera_media308.py soft
SOFT = BASE / 'A_soft'
SOFT_SPELLS = [  # key, job folder, label, last age shown (s), still moments (s), in the combined video
    ('na', 'P0_bridge_Fire_red', '나 · 불 창 (경로 · 한 번)', 2.2, [0.20, 0.40, 0.80, 1.40], True),
    ('go', 'F0_bridge_Wood_red', '고 · 죽순 부채꼴 (면 · 한 번)', 2.2, [0.45, 0.70, 1.20, 1.60], True),
    ('gak', 'P1_bridge4_Wood_red', '각 · 가지 새장 (경로 · 남긴다)', 2.6, [0.40, 0.80, 1.40, 2.00], True),
    ('ga', 'P0_bridge_Wood_red', '가 · 나무 경로 (경로 · 한 번)', 2.2, [0.20, 0.40, 0.80, 1.40], True),
    ('no', 'F0_bridge_Fire_red', '노 · 불 부채꼴 (면 · 한 번)', 2.2, [0.45, 0.70, 1.20, 1.60], True),
    ('ma', 'P0_bridge_Earth_red', '마', 2.2, [0.20, 0.40, 0.80, 1.40], False),
    ('sa', 'P0_bridge_Metal_red', '사', 2.2, [0.20, 0.40, 0.80, 1.40], False),
    ('a', 'P0_bridge_Water_red', '아', 2.2, [0.20, 0.40, 0.80, 1.40], False),
    ('mo', 'F0_bridge_Earth_red', '모', 2.2, [0.45, 0.70, 1.20, 1.60], False),
    ('so', 'F0_bridge_Metal_red', '소', 2.2, [0.45, 0.70, 1.20, 1.60], False),
    ('o', 'F0_bridge_Water_red', '오', 2.2, [0.45, 0.70, 1.20, 1.60], False),
    ('rapid', 'P0_bridge_burst5_Fire_red', '연사 · 나 5번 (0.65 s 간격, 실험실 채널)', 4.4, [0.50, 1.20, 2.00, 3.00, 3.80], True),
]
SOFT_TREAT = [('off', '지금 카메라 (A0)'), ('A12', 'A · V12 (지난 시안)'), ('A', 'A · 다듬음 (V13 기본)')]


def soft_frames(t, job, last):
    d = SOFT / 'raw' / t / 'frames' / job
    if not (d / 'index.csv').exists():
        return []
    with open(d / 'index.csv', encoding='utf-8', errors='replace') as f:
        rs = [r for r in csv.DictReader(f) if r.get('video') == '1' and float(r['ageSec']) <= last + 1e-6]
    return [(float(r['ageSec']), d / ('f_%05d.jpg' % int(r['frame']))) for r in rs if (d / ('f_%05d.jpg' % int(r['frame']))).exists()]


def soft_three(key, job, label, last):
    seqs = {t: soft_frames(t, job, last) for t, _ in SOFT_TREAT}
    base = seqs['A'] or seqs['off']
    if not base:
        print('missing', key); return
    def make(age, speed):
        canvas = Image.new('RGB', (1920, 362), (16, 16, 16))
        for i, (t, tname) in enumerate(SOFT_TREAT):
            p = nearest(seqs[t], age)
            tile = Image.open(p).resize((636, 358)) if p else Image.new('RGB', (636, 358), (40, 0, 0))
            tag(tile, tname + ' · ' + speed, label + ' · t = %.2f s' % age, 18)
            canvas.paste(tile, (i * 640 + 2, 2))
        return canvas
    yield from one_then_half(make, [x[0] for x in base])


def soft():
    allims = []
    for key, job, label, last, _, combined in SOFT_SPELLS:
        ims = list(soft_three(key, job, label, last) or [])
        if ims:
            write_mp4(SOFT / f'{key}_3way.mp4', ims, (1920, 362))
            if combined:
                allims += ims
    if allims:
        write_mp4(SOFT / 'A_soft_vs_current_all.mp4', allims, (1920, 362))
    for key, job, label, last, moments, combined in SOFT_SPELLS:
        if not combined:
            continue
        tw, th = 480, 270
        out = Image.new('RGB', (tw * len(moments) + 200, th * len(SOFT_TREAT)), (16, 16, 16))
        dr = ImageDraw.Draw(out)
        for row, (t, tname) in enumerate(SOFT_TREAT):
            seq = soft_frames(t, job, last)
            dr.text((8, row * th + 16), tname, font=font(16), fill=(255, 255, 255))
            for col, age in enumerate(moments):
                p = nearest(seq, age)
                if p is None:
                    continue
                out.paste(tag(Image.open(p).resize((tw - 4, th - 4)), '%.2f s' % age, None, 16), (200 + col * tw + 2, row * th + 2))
        out.save(SOFT / f'moments_{key}.jpg', quality=90)
        print('still', SOFT / f'moments_{key}.jpg')


def main():
    rnd = sys.argv[1] if len(sys.argv) > 1 else 'raw'
    if rnd == 'soft':
        soft(); return
    only = sys.argv[sys.argv.index('--only') + 1] if '--only' in sys.argv else None
    if only in (None, 'stills'): stills(rnd)
    if only in (None, 'grid'): grids(rnd)
    if only in (None, 'pairs'): pairs(rnd)


if __name__ == '__main__':
    main()
