#!/usr/bin/env python3
"""#308 spell lab recording -> mp4 (Spec 11.34; user 2026-10-16 "다음부터는 영상으로 제공해줘").

  python Tools/Art/SpellVFX308/rec308.py <rec folder or name> [--pre .3] [--len 5] [--slow 4] [--out FILE]

Reads the frames SpellLab308 `rec:<name>:<seconds>` wrote (Art/Playtest306/Checks/rec_<name>/f_<n>.jpg, 1920 x 720 = first
person 1280 x 720 | side 640 x 360 over top 640 x 360) and index.csv (frame, Time.time, real time, timeScale, oldest spell
age, unity frame). The game usually ran slowed down (SwingProbe308 slow:<k>) and the recorder drops frames when it is busy, so
frames are placed by game time: t0 = the spell's start (first frame with an age: time - age), the clip runs from t0 - pre to
t0 + len, resampled to 30 fps once at real speed and once `slow` times slower, with "시전 후 X.XX초" and the speed written on
the picture. Output: rec_<name>.mp4 next to the folder (imageio_ffmpeg, H.264)."""
import argparse
import bisect
import csv
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont

REPO = Path(__file__).resolve().parents[3]
CHECKS = REPO / 'Art/Playtest306/Checks'
FPS = 30
W, H, MAIN_W, PANE_H = 1920, 720, 1280, 360
FONT = 'C:/Windows/Fonts/malgun.ttf'
INK = (240, 235, 225)


def font(size):
    try:
        return ImageFont.truetype(FONT, size)
    except OSError:
        return ImageFont.load_default()


def label(draw, xy, text, f):
    x, y = xy
    l, t, r, b = draw.textbbox((x, y), text, font=f)
    draw.rectangle((l - 8, t - 5, r + 8, b + 5), fill=(0, 0, 0))
    draw.text((x, y), text, font=f, fill=INK)


def load_index(folder):
    rows = []
    with open(folder / 'index.csv', newline='', encoding='utf-8') as fh:
        for r in csv.DictReader(fh):
            try:
                p = folder / ('f_%05d.jpg' % int(r['frame']))
                row = (float(r['time']), float(r['age']), float(r['timescale']), p)
            except (TypeError, ValueError):
                continue                   # torn last row: recording cut off by a crash, or still being written
            if p.exists():
                rows.append(row)
    if not rows:
        raise SystemExit('no frames in %s' % folder)
    rows.sort(key=lambda x: x[0])
    out = []
    for row in rows:                       # one picture per game time (paused / zero time-scale frames repeat)
        if out and abs(row[0] - out[-1][0]) < 1e-6:
            continue
        out.append(row)
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('rec', help='rec_<name> folder, or just <name>')
    ap.add_argument('--pre', type=float, default=.3, help='seconds of game time before the spell starts')
    ap.add_argument('--len', type=float, default=5.0, help='seconds of game time after the spell starts')
    ap.add_argument('--slow', type=float, default=4.0, help='second pass is this many times slower (0 = no second pass)')
    ap.add_argument('--out', help='mp4 path (default: next to the folder)')
    a = ap.parse_args()

    folder = Path(a.rec)
    if not folder.is_dir():
        folder = CHECKS / ('rec_' + a.rec)
    rows = load_index(folder)
    times = [r[0] for r in rows]
    aged = [r for r in rows if r[1] >= 0]
    t0 = aged[0][0] - aged[0][1] if aged else times[0]
    start, end = max(times[0], t0 - a.pre), min(times[-1], t0 + a.len)
    if end <= start:
        raise SystemExit('empty range: game time %.2f..%.2f, spell start %.2f' % (times[0], times[-1], t0))
    gaps = np.diff(times)
    print('frames %d | game time %.2f..%.2f | spell start %s | clip %.2f..%.2f | timescale %s | median step %.3f s, max %.3f s'
          % (len(rows), times[0], times[-1], ('%.2f' % t0) if aged else 'none (no Vfx120Effect seen)', start, end,
             sorted({r[2] for r in rows}), float(np.median(gaps)) if len(gaps) else 0, float(gaps.max()) if len(gaps) else 0))

    import imageio_ffmpeg
    out = Path(a.out) if a.out else folder.parent / (folder.name + '.mp4')
    gen = imageio_ffmpeg.write_frames(str(out), (W, H), fps=FPS, codec='libx264', pix_fmt_out='yuv420p',
                                      output_params=['-crf', '20', '-preset', 'medium'], macro_block_size=8)
    gen.send(None)
    big, small = font(30), font(22)
    cache = {}

    def frame_at(g):
        i = max(0, bisect.bisect_right(times, g) - 1)   # the last picture taken at or before game time g
        if i + 1 < len(times) and times[i + 1] - g < g - times[i]:
            i += 1
        p = rows[i][3]
        if p not in cache:
            if len(cache) > 64:
                cache.clear()
            cache[p] = Image.open(p).convert('RGB').resize((W, H))
        return cache[p].copy()

    def card(text, n):
        img = Image.new('RGB', (W, H), (24, 22, 20))
        d = ImageDraw.Draw(img)
        l, t, r, b = d.textbbox((0, 0), text, font=big)
        d.text(((W - (r - l)) // 2, (H - (b - t)) // 2), text, font=big, fill=INK)
        for _ in range(n):
            gen.send(np.asarray(img).tobytes())

    passes = [(1.0, '실제 속도')] + ([(a.slow, '1/%g 속도' % a.slow)] if a.slow > 0 else [])
    written = 0
    for k, (slow, name) in enumerate(passes):
        if k:
            card(name + '로 다시', FPS // 2)
        n = int(round((end - start) * slow * FPS))
        for j in range(n + 1):
            g = start + j / (FPS * slow)
            img = frame_at(g)
            d = ImageDraw.Draw(img)
            dt = g - t0
            label(d, (20, 16), ('시전 후 %.2f초' % dt) if dt >= 0 else ('시전 %.2f초 전' % -dt), big)
            label(d, (20, 66), name, small)
            label(d, (MAIN_W + 12, 10), '옆 (9 m)', small)
            label(d, (MAIN_W + 12, PANE_H + 10), '위 (11 m)', small)
            gen.send(np.asarray(img).tobytes())
            written += 1
    gen.close()
    print('MP4', out, '| %d frames, %.1f s' % (written + (FPS // 2) * (len(passes) - 1),
                                               (written + (FPS // 2) * (len(passes) - 1)) / FPS))


if __name__ == '__main__':
    main()
