#!/usr/bin/env python3
"""2026-10-27 common material pass (showcase120_plan.md step 2): before | after videos of the representative glyphs from two Vol2VfxCapture rounds.

  python Tools/Art/SpellVFX308/material_pass_media308.py <before round dir> <after round dir> [--only na,nak] [--sheet]
     round dir = Art/SpellVFX308/Vol2Vfx/free_showcase/material_pass/<before|after...> (its frames/<job>/f_*.jpg + index.csv)
Out: Art/SpellVFX308/Vol2Vfx/free_showcase/material_pass/
  <key>_before_after.mp4          before | after side by side (2560 x 720): the whole capture at 1x, then its first CUT seconds again at 0.5x
  material_pass_all.mp4           every glyph in one file (60 fps, crf 22)
  material_pass_all_small.mp4     the same at 1280 px wide, 30 fps, libx264 crf 30 (< 30 MB)
  sheet_<key>.jpg (--sheet)       a few paired moments for the eye
Frames are streamed to ffmpeg (imageio_ffmpeg's binary), never held in memory."""
import csv, subprocess, sys
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
import imageio_ffmpeg

REPO = Path(__file__).resolve().parents[3]
OUT = REPO / 'Art/SpellVFX308/Vol2Vfx/free_showcase/material_pass'
FFMPEG = imageio_ffmpeg.get_ffmpeg_exe()
FONT = 'C:/Windows/Fonts/malgunbd.ttf'
# key, template, body, cut (s of the cast replayed at 0.5x), label
SET = [
    ('na', 'P0', 'Fire', 1.6, '나 · 화 · ㅏ 경로 — 불'),
    ('nak', 'P1', 'Fire', 1.8, '낙 · 화 · ㅏ 경로 · 남긴다 — 불'),
    ('no', 'F0', 'Fire', 1.6, '노 · 화 · ㅗ 면 — 불'),
    ('sa', 'P0', 'Metal', 1.6, '사 · 금 · ㅏ 경로 — 강철'),
    ('san', 'P2', 'Metal', 2.2, '산 · 금 · ㅏ 경로 · 번진다 — 강철'),
    ('sak', 'P1', 'Metal', 1.8, '삭 · 금 · ㅏ 경로 · 남긴다 — 강철'),
    ('seo', 'A0', 'Metal', 1.4, '서 · 금 · ㅓ 부착 — 강철'),
    ('su', 'R0', 'Metal', 1.6, '수 · 금 · ㅜ 둘레 — 강철'),
    ('a', 'P0', 'Water', 1.6, '아 · 수 · ㅏ 경로 — 물'),
    ('o', 'F0', 'Water', 1.6, '오 · 수 · ㅗ 면 — 물'),
    ('an', 'P2', 'Water', 2.2, '안 · 수 · ㅏ 경로 · 번진다 — 물'),
    ('ma', 'P0', 'Earth', 1.6, '마 · 토 · ㅏ 경로 — 돌'),
    ('mo', 'F0', 'Earth', 1.6, '모 · 토 · ㅗ 면 — 돌'),
    ('mok', 'F1', 'Earth', 1.8, '목 · 토 · ㅗ 면 · 남긴다 — 돌'),
    ('gak', 'P1', 'Wood', 1.8, '각 · 목 · ㅏ 경로 · 남긴다 — 나무'),
    ('gok', 'F1', 'Wood', 1.8, '곡 · 목 · ㅗ 면 · 남긴다 — 나무'),
    ('ga', 'P0', 'Wood', 1.6, '가 · 목 · ㅏ 경로 — 나무'),
]


def job_dir(round_dir, template, body):
    hits = sorted(d for d in (round_dir / 'frames').glob(template + '_*_' + body + '_*') if d.is_dir())
    hits += sorted(d for d in (round_dir / 'frames').glob(template + '_*_' + body) if d.is_dir())
    return hits[-1] if hits else None


def frames(d):
    rows = []
    with open(d / 'index.csv', encoding='utf-8', errors='replace') as f:
        for r in csv.DictReader(f):
            p = d / ('f_%05d.jpg' % int(r['frame']))
            if p.exists(): rows.append((float(r['ageSec']), p, r.get('note', '')))
    return rows


class Enc:
    def __init__(self, path, w, h, fps, crf, scale_w=None):
        vf = ['-vf', 'scale=%d:-2:flags=lanczos' % scale_w] if scale_w else []
        self.p = subprocess.Popen([FFMPEG, '-y', '-loglevel', 'error', '-f', 'rawvideo', '-pix_fmt', 'rgb24', '-s', '%dx%d' % (w, h), '-r', str(fps), '-i', '-',
                                   *vf, '-c:v', 'libx264', '-preset', 'medium', '-crf', str(crf), '-pix_fmt', 'yuv420p', '-movflags', '+faststart', str(path)],
                                  stdin=subprocess.PIPE)
    def put(self, im): self.p.stdin.write(im.tobytes())
    def close(self): self.p.stdin.close(); self.p.wait()


def compose(b, a, label, sub, font, small):
    W, H = b.size
    im = Image.new('RGB', (W * 2, H))
    im.paste(b, (0, 0)); im.paste(a.resize((W, H)), (W, 0))
    d = ImageDraw.Draw(im)
    d.rectangle([0, 0, W * 2, 44], fill=(0, 0, 0))
    d.text((16, 6), '전 (before)', font=font, fill=(235, 235, 235)); d.text((W + 16, 6), '후 (after · 공통 재질 다듬기)', font=font, fill=(255, 220, 150))
    d.text((W - 380, 6), label, font=font, fill=(255, 255, 255))
    if sub: d.text((16, H - 34), sub, font=small, fill=(255, 255, 160))
    d.line([(W, 0), (W, H)], fill=(0, 0, 0), width=4)
    return im


def main():
    if len(sys.argv) < 3: print(__doc__); return
    before, after = Path(sys.argv[1]), Path(sys.argv[2])
    only = sys.argv[sys.argv.index('--only') + 1].split(',') if '--only' in sys.argv else None
    font = ImageFont.truetype(FONT, 26); small = ImageFont.truetype(FONT, 20)
    OUT.mkdir(parents=True, exist_ok=True)
    allfull = allsmall = None
    for key, tpl, body, cut, label in SET:
        if only and key not in only: continue
        bd, ad = job_dir(before, tpl, body), job_dir(after, tpl, body)
        if bd is None or ad is None: print('skip', key, 'before', bd, 'after', ad); continue
        fb, fa = frames(bd), frames(ad)
        n = min(len(fb), len(fa))
        if n < 2: print('skip', key, 'frames', len(fb), len(fa)); continue
        b0 = Image.open(fb[0][1]).convert('RGB'); W, H = b0.size
        if allfull is None:
            allfull = Enc(OUT / 'material_pass_all.mp4', W * 2, H, 60, 22)
            allsmall = Enc(OUT / 'material_pass_all_small.mp4', W * 2, H, 30, 30, 1280)
        one = Enc(OUT / (key + '_before_after.mp4'), W * 2, H, 60, 21)
        jumped = False
        seq = []
        for k in range(n):
            if 'JUMP' in (fb[k][2] or '') or 'JUMP' in (fa[k][2] or ''): jumped = True
            seq.append((k, '1x' + (' · JUMP 뒤 (실험실이 긴 대기를 건너뜀)' if jumped else '')))
        slow = [(k, '0.5x') for k in range(n) if fb[k][0] <= cut]
        for idx, (k, tag) in enumerate(seq + [s for s in slow for _ in (0, 1)]):
            im = compose(Image.open(fb[k][1]).convert('RGB'), Image.open(fa[k][1]).convert('RGB'), label, tag + '  t=%.2fs' % fb[k][0], font, small)
            one.put(im); allfull.put(im)
            if idx % 2 == 0: allsmall.put(im)
            if '--sheet' in sys.argv and tag == '1x' and k in (int(n * 0.15), int(n * 0.3), int(n * 0.5)):
                im.resize((W, H // 2)).save(OUT / ('sheet_%s_%03d.jpg' % (key, k)), quality=88)
        one.close()
        print('wrote', key, n, 'frames (+', len(slow) * 2, 'slow)')
    if allfull: allfull.close(); allsmall.close()
    for f in ('material_pass_all.mp4', 'material_pass_all_small.mp4'):
        p = OUT / f
        if p.exists(): print(f, round(p.stat().st_size / 1e6, 1), 'MB')


if __name__ == '__main__':
    main()
