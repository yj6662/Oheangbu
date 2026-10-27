#!/usr/bin/env python3
"""2026-10-27 weak-points pass (free showcase, batches 6 - 8 common weak points): before | after videos from two Vol2VfxCapture rounds.

  python Tools/Art/SpellVFX308/weak_polish_media308.py <before round dir> <after round dir> [--only sa,me] [--sheet]
     round dir = Art/SpellVFX308/Vol2Vfx/free_showcase/weak_polish/<before|r1|...> (its frames/<job>/f_*.jpg + index.csv)
Out: Art/SpellVFX308/Vol2Vfx/free_showcase/weak_polish/
  <key>_before_after.mp4               before | after side by side: the whole capture at 1x, then its first CUT seconds again at 0.5x (same glyph, same camera)
  free_showcase_weak_polish_small.mp4  every key in one file, 1280 px wide, 30 fps, libx264 crf 30 (< 30 MB)
  sheet_<key>_<frame>.jpg (--sheet)    a few paired moments for the eye
Frames are streamed to ffmpeg (imageio_ffmpeg's binary), never held in memory."""
import csv, subprocess, sys
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
import imageio_ffmpeg

REPO = Path(__file__).resolve().parents[3]
OUT = REPO / 'Art/SpellVFX308/Vol2Vfx/free_showcase/weak_polish'
FFMPEG = imageio_ffmpeg.get_ffmpeg_exe()
FONT = 'C:/Windows/Fonts/malgunbd.ttf'
# key, job folder, cut (s replayed at 0.5x), label
SET = [
    # 1 steel
    ('sa', 'P0_bridge_Metal_red', 1.0, '① 강철 · 사 (금 · ㅏ 경로)'),
    ('sang', 'P5_bridge4_b7_Metal_red', 1.4, '① 강철 · 상 (금 · ㅏ 돌아온다)'),
    ('song', 'F5_bridge4_b7_Metal_red', 1.4, '① 강철 · 송 (금 · ㅗ 돌아온다)'),
    ('sat', 'P4_bridge4_b6_Metal_red', 1.4, '① 강철 · 삿 (금 · ㅏ 가른다)'),
    ('me', 'X0_onEarth_Metal_red', 1.6, '① 강철 · 금>토 옮겨붙음'),
    # 2 the ㅏ path read
    ('ga', 'P0_bridge_Wood_red', 1.0, '② ㅏ 경로 · 가'),
    ('nat', 'P4_bridge4_b6_Fire_red', 1.4, '② ㅏ 경로 · 낫'),
    ('gang', 'P5_bridge4_b7_Wood_red', 1.4, '② ㅏ 경로 · 강'),
    ('sam', 'P3_bridge4_b5_Metal_red', 1.0, '② ㅏ 경로 · 삼'),
    ('a', 'P0_bridge_Water_red', 1.0, '② ㅏ 경로 · 아'),
    # 3 the acting glyph over the 토 reactions
    ('ew', 'X0_onWood_Earth_red', 1.6, '③ 겹침 · 토>목 걸림 (마)  + ④ 막힘'),
    ('ef', 'X0_onFire_Earth_red', 1.6, '③ 겹침 · 토>화 옮겨붙음 (마)'),
    ('ee', 'X0_onEarth_Earth_red', 1.6, '③ 겹침 · 토>토 비화 (마)'),
    ('em', 'X0_onMetal_Earth_red', 1.6, '③ 겹침 · 토>금 먹임 (마)'),
    ('ea', 'X0_onWater_Earth_red', 1.6, '③ 겹침 · 토>수 진창 (마)'),
    # 4 the 막힘 half
    ('wm', 'X0_onMetal_Wood_red', 1.6, '④ 막힘 · 목>금 잘림'),
    ('fa', 'X0_onWater_Fire_red', 1.6, '④ 막힘 · 화>수 꺼짐'),
    ('mf', 'X0_onFire_Metal_red', 1.6, '④ 막힘 · 금>화 무뎌짐'),
    ('ae', 'X0_onEarth_Water_red', 1.6, '④ 막힘 · 수>토 막힘'),
    # regression of the common layers
    ('na', 'P0_bridge_Fire_red', 1.0, '회귀 · 나 (ㅏ)'),
    ('ma', 'P0_bridge_Earth_red', 1.0, '회귀 · 마 (ㅏ)'),
    ('gak', 'P1_bridge4_Wood_red', 1.2, '회귀 · 각 (ㅏ 남긴다)'),
    ('sak', 'P1_bridge4_Metal_red', 1.2, '회귀 · 삭 (ㅏ 남긴다 · 강철)'),
    ('man', 'P2_bridge4_b4_Earth_red', 1.2, '회귀 · 만 (ㅏ 번진다)'),
    ('san', 'P2_bridge4_b4_Metal_red', 1.2, '회귀 · 산 (ㅏ 번진다 · 강철)'),
    ('mam', 'P3_bridge4_b5_Earth_red', 1.0, '회귀 · 맘 (ㅏ 심는다)'),
    ('seo', 'A0_bridge_Metal_red', 1.2, '회귀 · 서 (ㅓ · 강철)'),
    ('su', 'R0_bridge_Metal_red', 1.2, '회귀 · 수 (ㅜ · 강철)'),
]


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


def compose(b, a, label, sub, font, small, W, H):
    im = Image.new('RGB', (W * 2, H))
    im.paste(b.resize((W, H)), (0, 0)); im.paste(a.resize((W, H)), (W, 0))
    d = ImageDraw.Draw(im)
    d.rectangle([0, 0, W * 2, 44], fill=(0, 0, 0))
    d.text((16, 6), '전 (before)', font=font, fill=(235, 235, 235)); d.text((W + 16, 6), '후 (after · 공통 약점 다듬기)', font=font, fill=(255, 220, 150))
    d.text((W - 520, 6), label, font=font, fill=(255, 255, 255))
    if sub: d.text((16, H - 34), sub, font=small, fill=(255, 255, 160))
    d.line([(W, 0), (W, H)], fill=(0, 0, 0), width=4)
    return im


def main():
    if len(sys.argv) < 3: print(__doc__); return
    before, after = Path(sys.argv[1]), Path(sys.argv[2])
    only = sys.argv[sys.argv.index('--only') + 1].split(',') if '--only' in sys.argv else None
    font = ImageFont.truetype(FONT, 26); small = ImageFont.truetype(FONT, 20)
    OUT.mkdir(parents=True, exist_ok=True)
    allsmall = None
    W = H = None
    for key, job, cut, label in SET:
        if only and key not in only: continue
        bd, ad = before / 'frames' / job, after / 'frames' / job
        if not bd.is_dir() or not ad.is_dir(): print('skip', key, bd.is_dir(), ad.is_dir()); continue
        fb, fa = frames(bd), frames(ad)
        n = min(len(fb), len(fa))
        if n < 2: print('skip', key, 'frames', len(fb), len(fa)); continue
        if W is None:
            W, H = Image.open(fb[0][1]).size
            if not only: allsmall = Enc(OUT / 'free_showcase_weak_polish_small.mp4', W * 2, H, 30, 30, 1280)
        one = Enc(OUT / (key + '_before_after.mp4'), W * 2, H, 60, 21)
        jumped = False
        seq = []
        for k in range(n):
            if 'JUMP' in (fb[k][2] or '') or 'JUMP' in (fa[k][2] or ''): jumped = True
            seq.append((k, '1x' + (' · JUMP 뒤 (실험실이 긴 대기를 건너뜀)' if jumped else '')))
        slow = [(k, '0.5x') for k in range(n) if fb[k][0] <= cut]
        for idx, (k, tag) in enumerate(seq + [s for s in slow for _ in (0, 1)]):
            im = compose(Image.open(fb[k][1]).convert('RGB'), Image.open(fa[k][1]).convert('RGB'), label, tag + '  t=%.2fs' % fb[k][0], font, small, W, H)
            one.put(im)
            if allsmall and idx % 2 == 0: allsmall.put(im)
            if '--sheet' in sys.argv and tag == '1x' and k in (int(n * 0.12), int(n * 0.3), int(n * 0.6)):
                im.resize((W, H // 2)).save(OUT / ('sheet_%s_%03d.jpg' % (key, k)), quality=88)
        one.close()
        print('wrote', key, n, 'frames (+', len(slow) * 2, 'slow)')
    if allsmall: allsmall.close()
    p = OUT / 'free_showcase_weak_polish_small.mp4'
    if p.exists(): print(p.name, round(p.stat().st_size / 1e6, 1), 'MB')


if __name__ == '__main__':
    main()
