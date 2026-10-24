#!/usr/bin/env python3
"""S5-b slice 9a (D308-150 V5): turns the engine frames of Vol2VfxCapture into still sheets and mp4s.

  python Tools/Art/SpellVFX308/vol2vfx9a.py sheet P0 [P1 ...|all] [--crop] [--pitch 15|0]
  python Tools/Art/SpellVFX308/vol2vfx9a.py video P0 [P1 ...|all] [--bodies Wood,Fire,...] [--variant main|work|expire]
  python Tools/Art/SpellVFX308/vol2vfx9a.py frame P0 Wood red 0.083 [--pitch 15]     (path of one key frame)

Input  : Art/SpellVFX308/Vol2Vfx/9a/frames/<template>_<variant>_<body>_<back>/ (f_NNNNN.jpg 1280x720 at 60 fps game time + index.csv, key_*_p15_<age>.png 1920x1080)
Output : Art/SpellVFX308/Vol2Vfx/9a/sheets/<template>_sheet.png (5 bodies x 2 moments x 2 backgrounds) and videos/<template>_<variant>.mp4.
The age printed on a picture is the SCOPE clock of the cast (the executors' clock); a "skip" label marks a lab-simulated clock jump over a long wait (never a real-time pass)."""
import argparse
import csv
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont

REPO = Path(__file__).resolve().parents[3]
ROOT = REPO / 'Art/SpellVFX308/Vol2Vfx/9a_r2'
FRAMES = ROOT / 'frames'
BODIES = ['Wood', 'Fire', 'Earth', 'Metal', 'Water']
BODY_KO = {'Wood': '목', 'Fire': '화', 'Earth': '토', 'Metal': '금', 'Water': '수'}
ROWS = {'P0': '가나마사아', 'P1': '각낙막삭악', 'P2': '간난만산안', 'P3': '감남맘삼암', 'P4': '갓낫맛삿앗', 'P5': '강낭망상앙'}
PEAKS = {'각', '난', '맘', '삿', '앙'}
FONT = 'C:/Windows/Fonts/malgun.ttf'
INK = (240, 235, 225)
VERB = {'P0': '한 번', 'P1': '남긴다', 'P2': '번진다', 'P3': '심는다', 'P4': '가른다', 'P5': '돌아온다'}


def font(size):
    try:
        return ImageFont.truetype(FONT, size)
    except OSError:
        return ImageFont.load_default()


def label(draw, xy, text, f, fill=INK, bg=(0, 0, 0)):
    x, y = xy
    l, t, r, b = draw.textbbox((x, y), text, font=f)
    draw.rectangle((l - 6, t - 3, r + 6, b + 3), fill=bg)
    draw.text((x, y), text, font=f, fill=fill)


def key_frame(folder, tag, pitch, target):
    """the key_*.png of the moment (a008 / b030) whose age is nearest to target"""
    best, best_d = None, 9
    for p in folder.glob('key_%s_p%02d_*.png' % (tag, pitch)):
        age = float(p.stem.split('_')[-1])
        if abs(age - target) < best_d:
            best, best_d = p, abs(age - target)
    return best, (float(best.stem.split('_')[-1]) if best else None)


def sheet(template, crop, pitch, variant='main'):
    """Plan J: at most 8 cells a sheet, a cell 960 x 540 at NATIVE 1080p resolution (a crop of the key frame around the action, never scaled).
    One sheet per template x background x moment (5 bodies), in colour and in greyscale (the body cue must read without colour)."""
    CW, CH = 960, 540
    X0, Y0 = 430, 380
    paths = []
    out = ROOT / 'sheets'
    out.mkdir(parents=True, exist_ok=True)
    f3 = font(20)
    for back in ('red', 'green'):
        for tag, target, name in (('a008', 0.08, '0.08 s'), ('b030', 0.30, '0.30 s')):
            cols = 3
            W, H = cols * CW, 2 * CH
            img = Image.new('RGB', (W, H), (24, 22, 20))
            d = ImageDraw.Draw(img)
            for ci, body in enumerate(BODIES):
                folder = FRAMES / ('%s_%s_%s_%s' % (template, variant, body, back))
                p, age = key_frame(folder, tag, pitch, target) if folder.exists() else (None, None)
                note = ''
                if template == 'P1' and body == 'Wood' and tag == 'b030' and folder.exists():       # 각 cages its body for 1.5 s: the residue stands after it
                    p2, age2 = key_frame(folder, 'x172', pitch, 1.72)
                    if p2: p, age, note = p2, age2, 'cage 1.5 s: residue shown at 1.72 s'
                x, y = (ci % cols) * CW, (ci // cols) * CH
                if p is None:
                    d.text((x + 8, y + 8), 'no frame ' + body, font=f3, fill=(200, 120, 120))
                    continue
                im = Image.open(p).convert('RGB').crop((X0, Y0, X0 + CW, Y0 + CH))
                img.paste(im, (x, y))
                letter = ROWS[template][ci]
                label(d, (x + 6, y + 6), '%s %s %s%s | %s | %s | age %.3f s | %s' % (template, VERB[template], BODY_KO[body], letter, body, '적로' if back == 'red' else '청림', age, ('peak: base look; ' if letter in PEAKS else '') + note), f3)
            # the sixth cell: the legend
            d.text((2 * CW + 20, CH + 20), '%s %s  %s  %s\nshoulder camera pitch %d deg, FOV 60, 1920x1080 crop 960x540 (native pixels)\nscope-clock age on each cell' % (template, VERB[template], '적로' if back == 'red' else '청림', name, pitch), font=font(24), fill=INK)
            p1 = out / ('%s_%s_%s_p%02d.png' % (template, back, tag, pitch))
            img.save(p1)
            img.convert('L').convert('RGB').save(out / ('%s_%s_%s_p%02d_grey.png' % (template, back, tag, pitch)))
            paths.append(p1)
    return paths[0].parent

def load_index(folder):
    rows = []
    with open(folder / 'index.csv', newline='', encoding='utf-8') as fh:
        for r in csv.DictReader(fh):
            if r['video'] != '1':
                continue
            p = folder / ('f_%05d.jpg' % int(r['frame']))
            if p.exists():
                rows.append((float(r['ageSec']), r['note'], p, r['letter']))
    return rows


def video(template, bodies, variant, fps=60):
    import imageio_ffmpeg
    W, H = 1280, 720
    out_dir = ROOT / 'videos'
    out_dir.mkdir(parents=True, exist_ok=True)
    out = out_dir / ('%s_%s.mp4' % (template, variant))
    gen = imageio_ffmpeg.write_frames(str(out), (W, H), fps=fps, codec='libx264', pix_fmt_out='yuv420p', output_params=['-crf', '19', '-preset', 'medium'], macro_block_size=8)
    gen.send(None)
    big, small = font(30), font(22)
    count = 0

    def put(img):
        nonlocal count
        gen.send(np.asarray(img).tobytes())
        count += 1

    def card(text, sub, n):
        img = Image.new('RGB', (W, H), (24, 22, 20))
        d = ImageDraw.Draw(img)
        l, t, r, b = d.textbbox((0, 0), text, font=big)
        d.text(((W - (r - l)) // 2, H // 2 - 40), text, font=big, fill=INK)
        l, t, r, b = d.textbbox((0, 0), sub, font=small)
        d.text(((W - (r - l)) // 2, H // 2 + 10), sub, font=small, fill=(190, 185, 175))
        for _ in range(n):
            put(img)

    for body in bodies:
        folder = FRAMES / ('%s_%s_%s_red' % (template, variant, body))
        if not (folder / 'index.csv').exists():
            continue
        rows = load_index(folder)
        if not rows:
            continue
        letter = rows[0][3]
        card('%s %s  %s %s' % (template, VERB[template], BODY_KO[body], letter), 'variant %s - background 적로 - scope clock of the cast; "skip" = lab-simulated jump over a long wait' % variant, 40)
        prev_age = -1.0
        for age, note, p, _ in rows:
            im = Image.open(p).convert('RGB')
            d = ImageDraw.Draw(im)
            jump = age - prev_age > 0.05 and prev_age >= 0
            label(d, (14, 12), '%s %s %s %s   age %.3f s' % (template, VERB[template], BODY_KO[body], letter, age), small)
            if note:
                label(d, (14, 48), note[:110], font(18), fill=(255, 220, 160))
            if jump:
                label(d, (14, 84), 'skip: the clock jumped +%.2f s (lab-simulated wait, not real time)' % (age - prev_age), font(18), fill=(255, 220, 160))
                for _ in range(20):
                    put(im)
            put(im)
            prev_age = age
        # hold the last frame a moment
        for _ in range(30):
            put(im)
    gen.close()
    return out, count


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('cmd', choices=['sheet', 'video', 'frame'])
    ap.add_argument('args', nargs='*')
    ap.add_argument('--crop', action='store_true')
    ap.add_argument('--pitch', type=int, default=15)
    ap.add_argument('--bodies', default=','.join(BODIES))
    ap.add_argument('--variant', default='main')
    a = ap.parse_args()
    if a.cmd == 'frame':
        t, body, back, age = a.args
        folder = FRAMES / ('%s_%s_%s_%s' % (t, a.variant, body, back))
        p, got = key_frame(folder, 'a008' if float(age) < 0.2 else 'b030', a.pitch, float(age))
        print(p, got)
        return
    templates = [t for t in a.args] or ['P0']
    if templates == ['all']:
        templates = list(ROWS)
    for t in templates:
        if a.cmd == 'sheet':
            print(sheet(t, a.crop, a.pitch, a.variant))
        else:
            variants = [a.variant] if a.variant != 'main' or t != 'P3' else ['work', 'expire']
            for v in variants:
                print(video(t, a.bodies.split(','), v))


if __name__ == '__main__':
    main()
