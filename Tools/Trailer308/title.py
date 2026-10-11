"""제목 카드 · 먹 번짐 마스크 — PIL/numpy로 PNG 시퀀스를 만들고 ffmpeg로 _work/title_<N>.mp4(crf 10)에 넣는다.

render_title(length, dur): '오행부'(HY궁서 B 210 pt, (W·.5, H·.46)) 왼→오 젖은 가장자리(0.3–2.0 s) → 2.3 s 주홍 낙관 '五行符'
  (.62/.16/.12, batang) → 3.0 s 부제 → 마지막 1 s 페이드. 수치는 #302 r_title(5.5 s 기준)이고 k = 5.5/dur 로 전부 축척한다.
  halo는 전체 마스크 blur 한 장 × edge. 띠 138 px는 검정.
render_inkin_mask(): 27장 'L'(0.9 s) — 거리장 + 프랙털 노이즈로 가운데서 번지는 먹. 첫 장 0, 마지막 장 255(maskedmerge가 마지막 장을 되풀이한다).

usage: python title.py 30|90 [--dur D] [--force]   |   python title.py inkin [--force]
"""
import argparse
import math
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

sys.path.insert(0, str(Path(__file__).resolve().parent))
from common import BAR, FFMPEG, FONT_SEAL, FONT_TITLE, FPS, H, W, WORK, count_frames, ensure_dirs, run  # noqa: E402

BLACK = np.float32(.018)
LIGHT = np.array([.93, .91, .86], np.float32)
SEAL_RED = np.array([.62, .16, .12], np.float32)
TITLE_REF = 5.5                                           # #302 제목 카드 길이(초) — 모든 시각의 기준
INKIN_FRAMES = 27                                         # 0.9 s


# ---------------------------------------------------------------- 수치 보조(#297)
def clamp01(x):
    return np.clip(x, 0.0, 1.0)


def smooth(e0, e1, x):
    t = clamp01((x - e0) / (e1 - e0))
    return t * t * (3 - 2 * t)


def ease_out(u):
    u = min(max(u, 0.0), 1.0)
    return 1 - (1 - u) ** 3


def value_noise(w, h, cells, seed):
    g = np.random.default_rng(seed).random((cells + 1, int(cells * w / h) + 1)).astype(np.float32)
    img = Image.fromarray((g * 255).astype(np.uint8), 'L').resize((w, h), Image.BICUBIC)
    return np.asarray(img, np.float32) / 255.0


def fractal(w, h, seed, base=3, octaves=5):
    acc = np.zeros((h, w), np.float32)
    amp, tot = 1.0, 0.0
    for o in range(octaves):
        acc += value_noise(w, h, base * (2 ** o), seed + o * 17) * amp
        tot += amp
        amp *= .5
    acc /= tot
    return (acc - acc.min()) / max(1e-6, acc.max() - acc.min())


def blur(arr, radius):
    if radius <= .3:
        return arr
    img = Image.fromarray((clamp01(arr) * 255).astype(np.uint8), 'L').filter(ImageFilter.GaussianBlur(radius))
    return np.asarray(img, np.float32) / 255.0


def dist_field(cx, cy):
    yy, xx = np.mgrid[0:H, 0:W].astype(np.float32)
    return np.sqrt(((xx - cx) / H) ** 2 + ((yy - cy) / H) ** 2)


def text_mask(text, font_path, size, index=0):
    font = ImageFont.truetype(str(font_path), int(size), index=index)
    x0, y0, x1, y1 = font.getbbox(text)
    img = Image.new('L', (x1 - x0 + 40, y1 - y0 + 40), 0)
    ImageDraw.Draw(img).text((20 - x0, 20 - y0), text, font=font, fill=255)
    return np.asarray(img, np.float32) / 255.0


def place(small, cx, cy):
    big = np.zeros((H, W), np.float32)
    h, w = small.shape
    x0, y0 = int(cx - w / 2), int(cy - h / 2)
    big[max(0, y0):min(H, y0 + h), max(0, x0):min(W, x0 + w)] = small[max(0, -y0):min(h, H - y0), max(0, -x0):min(w, W - x0)]
    return big


def seal_mask(size):
    """낙관: 五行符 세로 세 자, 주홍 바탕에 흰 글자, 돌 가장자리처럼 거칠게(#297)."""
    s = int(size)
    img = Image.new('L', (s, int(s * 1.9)), 0)
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([0, 0, s - 1, int(s * 1.9) - 1], radius=int(s * .06), fill=255)
    font = ImageFont.truetype(str(FONT_SEAL), int(s * .52), index=0)
    for i, ch in enumerate('五行符'):
        x0, y0, x1, y1 = font.getbbox(ch)
        d.text(((s - (x1 - x0)) / 2 - x0, s * .10 + i * s * .56 - y0), ch, font=font, fill=0)
    a = np.asarray(img, np.float32) / 255.0
    rough = fractal(a.shape[1], a.shape[0], 77, base=6, octaves=4)
    return a * smooth(.18, .30, rough + (a * .5))


# ---------------------------------------------------------------- 제목 카드
class TitleCard:
    def __init__(self, dur):
        self.dur = float(dur)
        self.k = TITLE_REF / self.dur
        self.tm = place(text_mask('오행부', FONT_TITLE, 210), W * .5, H * .46)
        self.noise = fractal(W, H, 133, base=8, octaves=3)
        self.halo_full = blur(self.tm, 6)                 # 한 장만 흐리고 프레임마다 edge를 곱한다
        self.seal = place(seal_mask(64), W * .5 + 370, H * .46 + 40)
        self.sub = place(text_mask('五行符', FONT_SEAL, 34), W * .5, H * .46 + 170)
        self.xx = np.linspace(0, 1, W, dtype=np.float32)[None, :]

    def frame(self, lt):
        """출력 초 lt → uint8 H×W×3."""
        s = lt * self.k
        u = clamp01((s - .3) / 1.7)
        edge = smooth(0, .03, u * 1.1 - (self.xx * .8 + .1) + (self.noise - .5) * .06)
        halo = self.halo_full * edge * (.3 * (1 - smooth(2.2, 3.4, s)))
        a = clamp01(self.tm * edge * .96 + halo)[..., None]
        out = BLACK * (1 - a) + LIGHT * a
        if s > 2.3:
            su = clamp01((s - 2.3) / .2)
            sm = (self.seal * (su * .95))[..., None]
            out = out * (1 - sm) + SEAL_RED * sm
        if s > 3.0:
            s2 = clamp01((s - 3.0) / .8)
            sb = (self.sub * (s2 * .75))[..., None]
            out = out * (1 - sb) + LIGHT * sb
        fade = 1 - smooth(TITLE_REF - 1.0, TITLE_REF, s)
        out = out * fade + BLACK * (1 - fade)
        out[:BAR] = 0.0
        out[H - BAR:] = 0.0
        return (clamp01(out) * 255 + .5).astype(np.uint8)


def title_duration(length):
    from timeline import timeline
    return next(c.dur for c in timeline(length) if c.kind == 'title')


def render_title(length, dur=None, force=False, out_dir=None):
    """PNG 시퀀스 → _work/title_<N>.mp4 (crf 10). 이미 있고 프레임 수가 맞으면 그대로 둔다. 반환: mp4 경로."""
    ensure_dirs()
    dur = float(dur if dur is not None else title_duration(length))
    n = int(round(dur * FPS))
    out_dir = Path(out_dir or WORK)
    mp4 = out_dir / f'title_{length}.mp4'
    if mp4.exists() and not force and count_frames(mp4) == n:
        return mp4
    seq = out_dir / f'title_{length}'
    seq.mkdir(parents=True, exist_ok=True)
    for old in seq.glob('t_*.png'):
        old.unlink()
    card = TitleCard(dur)
    for i in range(n):
        Image.fromarray(card.frame(i / FPS), 'RGB').save(seq / f't_{i:04d}.png', compress_level=1)
    run([FFMPEG, '-y', '-loglevel', 'error', '-framerate', str(FPS), '-i', str(seq / 't_%04d.png'), '-frames:v', str(n),
         '-c:v', 'libx264', '-preset', 'medium', '-crf', '10', '-pix_fmt', 'yuv420p', '-movflags', '+faststart', str(mp4)])
    got = count_frames(mp4)
    if got != n:
        raise RuntimeError(f'title_{length}.mp4 frames {got} != {n}')
    return mp4


# ---------------------------------------------------------------- 먹 번짐 마스크(첫 컷 inkin)
def render_inkin_mask(n=INKIN_FRAMES, seed=40, roughness=.45, fine=.07, reach=2.0, soft=.045):
    """가운데(W·.5, H·.5)에서 젖은 종이로 번지는 먹: 'L' 이미지 n장. 0 = 검정, 255 = 영상."""
    base = dist_field(W * .5, H * .5) + fractal(W, H, seed, base=3) * roughness + fractal(W, H, seed + 5, base=24, octaves=3) * fine
    frames = []
    for i in range(n):
        u = i / (n - 1)
        R = ease_out(u) * reach
        m = smooth(0, soft, R - base)
        if i == n - 1:
            m = np.ones_like(m)
        frames.append(Image.fromarray((m * 255 + .5).astype(np.uint8), 'L'))
    return frames


def write_inkin(out_dir=None, force=False):
    """_work/inkin/ink_%02d.png (27장). 반환: 폴더."""
    ensure_dirs()
    d = Path(out_dir or WORK / 'inkin')
    d.mkdir(parents=True, exist_ok=True)
    have = sorted(d.glob('ink_*.png'))
    if len(have) == INKIN_FRAMES and not force:
        return d
    for old in have:
        old.unlink()
    for i, img in enumerate(render_inkin_mask()):
        img.save(d / f'ink_{i:02d}.png', compress_level=1)
    return d


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('what', help='30 | 90 | inkin')
    ap.add_argument('--dur', type=float, default=None)
    ap.add_argument('--force', action='store_true')
    a = ap.parse_args(argv)
    if a.what == 'inkin':
        print(write_inkin(force=a.force))
        return
    length = int(a.what)
    print(render_title(length, a.dur, force=a.force))


if __name__ == '__main__':
    main()
