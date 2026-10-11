"""접촉 인화 — mp4를 K 프레임마다 한 칸씩 격자로 붙이고 칸마다 그 프레임의 초를 적는다(글자 · HUD · 컷 경계를 눈으로 확인, marks 채우기).

칸 n = 프레임 n·K (K = round(step·fps), `select=not(mod(n\\,K))` + `-fps_mode passthrough`) — 라벨 = n·K/fps.
(`fps=1/step` 는 칸 n에 (n+½)·step 에 가장 가까운 프레임을 넣어 라벨이 0.47 s 밀렸다 — 검토 지적.)
30 fps 에서 --take(0.25 s)는 K=8 → 0.267 s 간격으로 찍고 라벨은 실제 초(0.00 · 0.27 · 0.53 …)를 적는다.

usage: python sheet.py <mp4> [out.jpg] [--step 1.0] [--take] [--tile CxR] [--cell 320]
  30 s → 6×5, 90 s → 10×9(1초 1장). --take = 0.25초 간격(테이크용). 기본 출력 _work/<이름>_sheet.jpg
"""
import argparse
import math
import subprocess
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont

sys.path.insert(0, str(Path(__file__).resolve().parent))
from common import FFMPEG, FONT_TITLE, FPS, WORK, count_frames, ensure_dirs, probe  # noqa: E402


def grab(mp4, K, n, cw, ch):
    """프레임 0, K, 2K, … n장을 rawvideo로 읽는다 → (n_got, ch, cw, 3) uint8. 타임스탬프 재배치 없이(passthrough) 고른 프레임 그대로."""
    cmd = [FFMPEG, '-loglevel', 'error', '-i', mp4, '-vf', f'select=not(mod(n\\,{K})),scale={cw}:{ch}:flags=bicubic',
           '-fps_mode', 'passthrough', '-frames:v', str(n), '-f', 'rawvideo', '-pix_fmt', 'rgb24', '-']
    raw = subprocess.run([str(c) for c in cmd], capture_output=True, check=True).stdout
    got = len(raw) // (cw * ch * 3)
    return np.frombuffer(raw[:got * cw * ch * 3], np.uint8).reshape(got, ch, cw, 3)


def tile_for(n, tile=None):
    if tile:
        c, r = tile.lower().split('x')
        return int(c), int(r)
    if n <= 30:
        return 6, max(1, math.ceil(n / 6))
    if n <= 90:
        return 10, max(1, math.ceil(n / 10))
    cols = min(12, math.ceil(math.sqrt(n * 1.3)))
    return cols, math.ceil(n / cols)


def make_sheet(mp4, out=None, step=1.0, tile=None, cell=320):
    """반환: (출력 경로, 칸 수, (cols, rows), 실제 간격 초)."""
    ensure_dirs()
    mp4 = Path(mp4)
    info = probe(mp4)
    fps = info['fps'] or float(FPS)
    total = count_frames(mp4) or int(round((info['duration'] or 0.0) * fps))
    K = max(1, int(round(step * fps)))
    n = max(1, math.ceil(total / K))
    cols, rows = tile_for(n, tile)
    cw = min(cell, 1920 // cols)
    cw -= cw % 2
    ch = int(round(cw * 9 / 16))
    ch -= ch % 2
    frames = grab(mp4, K, n, cw, ch)
    rows = max(rows, math.ceil(len(frames) / cols))
    sheet = Image.new('RGB', (cols * cw, rows * ch), (24, 24, 24))
    d = ImageDraw.Draw(sheet)
    try:
        font = ImageFont.truetype(str(FONT_TITLE), max(12, int(ch * .16)))
    except Exception:
        font = ImageFont.load_default()
    whole = abs(K / fps - round(K / fps)) < 1e-6
    for i, fr in enumerate(frames):
        x, y = (i % cols) * cw, (i // cols) * ch
        sheet.paste(Image.fromarray(fr, 'RGB'), (x, y))
        t = i * K / fps
        label = f'{t:g}' if whole else f'{t:.2f}'
        tw = d.textlength(label, font=font)
        d.rectangle([x, y, x + tw + 10, y + int(ch * .2)], fill=(0, 0, 0))
        d.text((x + 5, y + 1), label, font=font, fill=(255, 235, 180))
    out = Path(out) if out else WORK / f'{mp4.stem}_sheet.jpg'
    out.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(out, quality=90, subsampling=0)
    return out, len(frames), (cols, rows), K / fps, K


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('mp4')
    ap.add_argument('out', nargs='?', default=None)
    ap.add_argument('--step', type=float, default=1.0)
    ap.add_argument('--take', action='store_true', help='0.25초 간격')
    ap.add_argument('--tile', default=None, help='예: 6x5')
    ap.add_argument('--cell', type=int, default=320)
    a = ap.parse_args(argv)
    out, n, (c, r), actual, K = make_sheet(a.mp4, a.out, 0.25 if a.take else a.step, a.tile, a.cell)
    print(f'{out}  {n} cells  {c}x{r}  cell n = frame n·{K} = {actual:.4f}s·n (label = that frame\'s time)')


if __name__ == '__main__':
    main()
