#!/usr/bin/env python3
"""legacy bridge (D308-150 V9): old main game (Legacy308 route + its glyph bodies, capture legacy=2) vs the vol.2 legacy bridge, side by side, from Vol2VfxCapture frames.

  python Tools/Art/SpellVFX308/legacy_bridge/legacy_bridge_media.py step2              bridge variant = bridge
  python Tools/Art/SpellVFX308/legacy_bridge/legacy_bridge_media.py step3 --new bridge_apex

Input : Art/SpellVFX308/Vol2Vfx/legacy_bridge/<step>/frames/<T>_old_<Body>_red/ and <T>_<new>_<Body>_red/ (f_NNNNN.jpg 1280x720, index.csv, one frame per 1/60 s)
        --old-step: take the old frames from another step folder (default: the same step)
Output: Art/SpellVFX308/Vol2Vfx/legacy_bridge/<step>/videos/<T>_old_vs_bridge.mp4   five bodies, old | bridge, first 1 x (60 fps = game speed), then the same at 0.25 x
        Art/SpellVFX308/Vol2Vfx/legacy_bridge/<step>/sheets/<T>_old_vs_bridge.png  five bodies x old / bridge x eight moments
Camera = the in-game shoulder camera (pivot 1.7 m, local (0.7, 0.55, -2.8), FOV 60) at pitch 0, main-world look stepped at 적로, a real enemy (Creatures308 forest bear)."""
import argparse
import csv
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont

REPO = Path(__file__).resolve().parents[4]
BASE = REPO / 'Art/SpellVFX308/Vol2Vfx/legacy_bridge'
BODIES = ['Wood', 'Fire', 'Earth', 'Metal', 'Water']
BODY_KO = {'Wood': '목', 'Fire': '화', 'Earth': '토', 'Metal': '금', 'Water': '수'}
ROWS = {'P0': '가나마사아', 'A0': '거너머서어', 'F0': '고노모소오', 'R0': '구누무수우'}
NAME = {'P0': 'ㅏ 한 번 (단발)', 'A0': 'ㅓ 한 번 (패링)', 'F0': 'ㅗ 한 번 (광역)', 'R0': 'ㅜ 한 번 (방벽 띠)'}
FONT = 'C:/Windows/Fonts/malgun.ttf'


def font(n):
    try:
        return ImageFont.truetype(FONT, n)
    except OSError:
        return ImageFont.load_default()


def label(d, xy, text, f, fill=(240, 235, 225)):
    l, t, r, b = d.textbbox(xy, text, font=f)
    d.rectangle((l - 5, t - 3, r + 5, b + 3), fill=(0, 0, 0))
    d.text(xy, text, font=f, fill=fill)


def rows_of(folder):
    if not (folder / 'index.csv').exists():
        return []
    rows = [r for r in csv.DictReader(open(folder / 'index.csv', encoding='utf-8')) if r['video'] == '1']
    return [r for r in rows if (folder / ('f_%05d.jpg' % int(r['frame']))).exists()]


def main():
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
    ap = argparse.ArgumentParser(); ap.add_argument('step'); ap.add_argument('--new', default='bridge'); ap.add_argument('--old-step', default=''); ap.add_argument('--title', default='')
    a = ap.parse_args()
    import imageio_ffmpeg
    frames = BASE / a.step / 'frames'; old_frames = BASE / (a.old_step or a.step) / 'frames'
    out_v = BASE / a.step / 'videos'; out_s = BASE / a.step / 'sheets'
    out_v.mkdir(parents=True, exist_ok=True); out_s.mkdir(parents=True, exist_ok=True)
    tag_new = a.title or ('vol.2 다리' + (' (정점 발사 TEST)' if 'apex' in a.new else ' (커밋 발사)'))
    f2 = font(20)
    for t in ROWS:
        W, H = 1920, 544
        seq = []
        for body in BODIES:
            fo = old_frames / ('%s_old_%s_red' % (t, body)); fn = frames / ('%s_%s_%s_red' % (t, a.new, body))
            ra, rb = rows_of(fo), rows_of(fn)
            n = max(len(ra), len(rb))
            for i in range(n):
                seq.append((body, fo, fn, ra[min(i, len(ra) - 1)] if ra else None, rb[min(i, len(rb) - 1)] if rb else None, i == n - 1))
        if not seq:
            print('no frames for', t); continue
        p = out_v / ('%s_old_vs_bridge.mp4' % t)
        gen = imageio_ffmpeg.write_frames(str(p), (W, H), fps=60, codec='libx264', pix_fmt_out='yuv420p', output_params=['-crf', '18', '-preset', 'medium'], macro_block_size=8)
        gen.send(None)
        for speed, rep in (('1×', 1), ('0.25×', 4)):
            for body, fo, fn, ra, rb, last in seq:
                img = Image.new('RGB', (W, H), (16, 16, 16)); d = ImageDraw.Draw(img)
                for k, (r, folder, tag) in enumerate(((ra, fo, '옛 본편 (Legacy308)'), (rb, fn, tag_new))):
                    if r is None:
                        continue
                    im = Image.open(folder / ('f_%05d.jpg' % int(r['frame']))).convert('RGB').resize((960, 540), Image.LANCZOS)
                    img.paste(im, (k * 960, 2))
                    label(d, (k * 960 + 12, 10), '%s  |  %s %s %s  |  %.3f s  |  %s' % (tag, t, ROWS[t][BODIES.index(body)], BODY_KO[body], float(r['ageSec']), speed), f2)
                label(d, (12, H - 40), NAME[t] + '  · 같은 카메라(어깨 피치 0°) · 본편 룩(적로) · 곰 · 시각 = 글자 떼는 순간부터', f2)
                fb = np.asarray(img).tobytes()
                for _ in range(rep * (12 if last else 1)):
                    gen.send(fb)
        gen.close(); print('video', p)
        ms = [0.05, 0.15, 0.3, 0.45, 0.6, 0.8, 1.1, 1.5]
        cw, ch = 480, 270
        img = Image.new('RGB', (cw * len(ms), ch * 10 + 50), (16, 16, 16)); d = ImageDraw.Draw(img)
        label(d, (12, 12), '%s %s — 옛 본편(위) 대 %s(아래) · 적로 · 같은 카메라·룩·곰 · 시각 = 글자 떼는 순간부터' % (t, NAME[t], tag_new), font(22))
        for bi, body in enumerate(BODIES):
            for gi, (folder, tg) in enumerate(((old_frames / ('%s_old_%s_red' % (t, body)), '옛'), (frames / ('%s_%s_%s_red' % (t, a.new, body)), '새'))):
                rr = rows_of(folder); y = 50 + (bi * 2 + gi) * ch
                for mi, m in enumerate(ms):
                    if not rr:
                        continue
                    r = min(rr, key=lambda x: abs(float(x['ageSec']) - m))
                    im = Image.open(folder / ('f_%05d.jpg' % int(r['frame']))).convert('RGB').resize((cw, ch), Image.LANCZOS)
                    img.paste(im, (mi * cw, y))
                    label(d, (mi * cw + 6, y + 4), '%s %s %s %.2f s' % (tg, ROWS[t][bi], BODY_KO[body], float(r['ageSec'])), f2)
        sp = out_s / ('%s_old_vs_bridge.png' % t); img.save(sp); print('sheet', sp)


if __name__ == '__main__':
    main()
