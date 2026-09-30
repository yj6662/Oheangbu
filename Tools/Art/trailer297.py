"""#297 presentation trailer compositor (1920x1080, 30 fps, ~60 s).

Editing grammar comes from the game itself: each chapter opens with its initial consonant brush-written on hanji
(초성 = 오행 속성: ㄱ木 ㄴ火 ㅁ土 ㅅ金 ㅇ水), the ink of the written jamo bleeds into wet paper and becomes the window
onto the realm, and the realm's 오방색 blooms inside the footage like watercolour on wet paper. All game footage is
graded toward a low-saturation 수묵담채 tone. No explanatory captions: the jamo and the title are the only text.

usage: python Tools/Art/trailer297.py [--preview] [--start S] [--end S] [--no-audio]
Input: Art/Presentation297/Films/<take>.mp4   Output: Art/Presentation297/Trailer/
"""
import math
import random
import subprocess
import sys
import wave
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont
import imageio_ffmpeg

ROOT = Path(__file__).resolve().parents[2]
FILMS = ROOT / 'Art/Presentation297/Films'
OUT = ROOT / 'Art/Presentation297/Trailer'
SFX = ROOT / 'Oheangbu/Assets/_Project/Audio/Compact255'
HANJI = ROOT / 'Oheangbu/Assets/_Project/Art/UI/PlaytestMenus/HanjiSurface.png'
FONT_TITLE = r'C:\Windows\Fonts\H2GSRB.TTF'       # HY궁서 B
FONT_SEAL = r'C:\Windows\Fonts\batang.ttc'
FPS = 30
PREVIEW = '--preview' in sys.argv
W, H = (960, 540) if PREVIEW else (1920, 1080)
S = W / 1920.0                                      # layout scale
FFMPEG = imageio_ffmpeg.get_ffmpeg_exe()

# 오방색 (muted pigments): 木 靑, 火 赤, 土 黃, 金 白, 水 黑
STRENGTH = {'wood': .75, 'fire': .55, 'earth': .50, 'metal': 1.0, 'water': .65}
COLORS = {'wood': (0.30, 0.52, 0.47), 'fire': (0.74, 0.27, 0.19), 'earth': (0.80, 0.63, 0.30),
          'metal': (0.95, 0.93, 0.88), 'water': (0.16, 0.19, 0.24)}
INK = np.array([0.075, 0.072, 0.078], np.float32)

# ---------------------------------------------------------------- helpers


def clamp01(x):
    return np.clip(x, 0.0, 1.0)


def smooth(e0, e1, x):
    t = clamp01((x - e0) / (e1 - e0))
    return t * t * (3 - 2 * t)


def ease_out(u):
    u = min(max(u, 0.0), 1.0)
    return 1 - (1 - u) ** 3


def ease_in_out(u):
    u = min(max(u, 0.0), 1.0)
    return u * u * (3 - 2 * u)


_rng = np.random.default_rng(297)


def value_noise(w, h, cells, seed):
    g = np.random.default_rng(seed).random((cells + 1, int(cells * w / h) + 1)).astype(np.float32)
    img = Image.fromarray((g * 255).astype(np.uint8), 'L').resize((w, h), Image.BICUBIC)
    return np.asarray(img, np.float32) / 255.0


def fractal(w, h, seed, base=3, octaves=5):
    acc = np.zeros((h, w), np.float32); amp = 1.0; tot = 0.0
    for o in range(octaves):
        acc += value_noise(w, h, base * (2 ** o), seed + o * 17) * amp
        tot += amp; amp *= .5
    acc /= tot
    return (acc - acc.min()) / max(1e-6, acc.max() - acc.min())


def blur(arr, radius):
    if radius <= .3: return arr
    img = Image.fromarray((clamp01(arr) * 255).astype(np.uint8), 'L').filter(ImageFilter.GaussianBlur(radius))
    return np.asarray(img, np.float32) / 255.0


def dist_field(cx, cy):
    yy, xx = np.mgrid[0:H, 0:W].astype(np.float32)
    return np.sqrt(((xx - cx) / H) ** 2 + ((yy - cy) / H) ** 2)


# ---------------------------------------------------------------- paper and grade

_hanji = Image.open(HANJI).convert('RGB').resize((W, H), Image.BICUBIC)
HANJI_RGB = np.asarray(_hanji, np.float32) / 255.0
_hl = HANJI_RGB @ np.array([.299, .587, .114], np.float32)
PAPER_MOD = clamp01(1 + (_hl - _hl.mean()) * .45)[..., None].astype(np.float32)
PAPER = clamp01(np.array([0.925, 0.905, 0.855], np.float32) * PAPER_MOD * .5 + HANJI_RGB * .5 * np.array([1.0, .99, .96], np.float32))
_v = dist_field(W / 2, H / 2)
VIGNETTE = (1 - smooth(.45, 1.05, _v) * .10)[..., None].astype(np.float32)
FIBRE = (fractal(W, H, 901, base=40, octaves=3) - .5)[..., None].astype(np.float32)
GRAIN = (fractal(W, H, 555, base=110, octaves=2)[..., None] * .5 + .75).astype(np.float32)   # pigment granulation


def grade(img):
    """low-saturation 수묵담채: keep ~50% chroma, warm paper white, soft ink blacks, hanji fibre"""
    lum = (img @ np.array([.299, .587, .114], np.float32))[..., None]
    out = lum + (img - lum) * .50
    out = out * np.array([1.0, .985, .95], np.float32)
    out = .035 + out * .955
    out = out * (1 + FIBRE * .05) * VIGNETTE
    return clamp01(out)


# ---------------------------------------------------------------- clips


class Clip:
    """sequential frame reader with a start offset (frames) and optional speed"""

    FALLBACK = {'water_mum': 'water_bridge', 'wood_car': 'wood_village', 'climax_gom': 'op_cast_na', 'earth_palace_aerial': 'earth_capital'}

    def __init__(self, name, start=0.0, speed=1.0):
        self.path = FILMS / f'{name}.mp4'
        if not self.path.exists() and name in self.FALLBACK:
            print('stand-in for missing take', name, '->', self.FALLBACK[name]); self.path = FILMS / f'{self.FALLBACK[name]}.mp4'
        self.speed = speed
        self.pos = -1
        self.frame = None
        self.gen = imageio_ffmpeg.read_frames(str(self.path), output_params=['-vf', f'scale={W}:{H}'])
        meta = next(self.gen)
        self.n = int(round(meta.get('duration', 0) * meta.get('fps', FPS)))
        self.start = int(round(start * FPS))

    def at(self, local_t):
        want = self.start + int(local_t * FPS * self.speed)
        while self.pos < want:
            try:
                raw = next(self.gen)
                self.frame = np.frombuffer(raw, np.uint8).reshape(H, W, 3).astype(np.float32) / 255.0
                self.pos += 1
            except StopIteration:
                break
        return self.frame if self.frame is not None else PAPER


# ---------------------------------------------------------------- brush (jamo written with ink)

JAMO = {  # stroke polylines in a unit box (y down), in writing order
    'ㄱ': [[(.18, .24), (.50, .235), (.80, .25), (.79, .45), (.72, .84)]],
    'ㄴ': [[(.26, .16), (.255, .50), (.27, .76), (.55, .765), (.84, .75)]],
    'ㅁ': [[(.22, .22), (.215, .52), (.23, .82)], [(.23, .21), (.52, .205), (.79, .22), (.785, .55), (.77, .82)], [(.24, .81), (.52, .815), (.77, .80)]],
    'ㅅ': [[(.53, .14), (.47, .40), (.34, .64), (.16, .84)], [(.46, .45), (.60, .62), (.86, .83)]],
    'ㅇ': [[(.5 + .31 * math.sin(a), .5 - .31 * math.cos(a)) for a in np.linspace(0, -2 * math.pi * 1.04, 48)]],
}


PATHS = {  # brush paths in the glyph's own bbox (0..1, y down), writing order; the reveal brush is wide
    'ㄱ': [[(.02, .20), (.55, .12), (.92, .10), (.86, .45), (.62, 1.0)]],
    'ㄴ': [[(.10, .00), (.08, .45), (.12, .80), (.55, .86), (1.0, .78)]],
    'ㅁ': [[(.10, .02), (.08, .55), (.12, .98)], [(.12, .02), (.55, .00), (.90, .04), (.88, .55), (.86, .98)], [(.12, .96), (.55, .97), (.88, .95)]],
    'ㅅ': [[(.56, .00), (.44, .40), (.22, .75), (.00, 1.0)], [(.46, .42), (.66, .70), (1.0, .98)]],
    'ㅇ': [[(.5 + .46 * math.sin(a), .5 - .46 * math.cos(a)) for a in np.linspace(0, -2 * math.pi * 1.06, 60)]],
}


class Calligraphy:
    """the 궁체 jamo (HY궁서) written stroke by stroke: a wide reveal brush runs the writing path over the glyph"""

    def __init__(self, jamo, height, center):
        font = ImageFont.truetype(FONT_TITLE, int(height * 2.4))
        x0, y0, x1, y1 = font.getbbox(jamo)
        pad = int(height * .12)
        img = Image.new('L', (x1 - x0 + 2 * pad, y1 - y0 + 2 * pad), 0)
        ImageDraw.Draw(img).text((pad - x0, pad - y0), jamo, font=font, fill=255)
        scale = height / max(1, (y1 - y0))
        if abs(scale - 1) > .01:
            img = img.resize((max(1, int(img.width * scale)), max(1, int(img.height * scale))), Image.LANCZOS)
            pad = int(pad * scale)
        self.glyph = np.asarray(img, np.float32) / 255.0
        self.w, self.h = img.width, img.height
        self.box = (pad, pad, self.w - pad, self.h - pad)
        self.center = center
        self.reveal = Image.new('L', (self.w, self.h), 0)
        self.draw = ImageDraw.Draw(self.reveal)
        bw, bh = self.box[2] - self.box[0], self.box[3] - self.box[1]
        self.radius = .23 * max(bw, bh)
        self.strokes = []
        for path in PATHS[jamo]:
            pts = [(self.box[0] + x * bw, self.box[1] + y * bh) for x, y in path]
            seg = [math.dist(pts[i], pts[i + 1]) for i in range(len(pts) - 1)]
            self.strokes.append((pts, seg, sum(seg)))
        self.lift = .10                                   # pen lift between strokes (fraction of the timeline)
        total = sum(s[2] for s in self.strokes)
        n = len(self.strokes)
        span = 1 - self.lift * (n - 1)
        self.windows, u = [], 0.0
        for s in self.strokes:
            d = span * s[2] / total
            self.windows.append((u, u + d)); u += d + self.lift
        self.done = [0.0] * n

    def advance(self, progress):
        for i, (s, (u0, u1)) in enumerate(zip(self.strokes, self.windows)):
            f = clamp01((progress - u0) / max(1e-6, u1 - u0))
            f = float(f)
            f = f * f * (3 - 2 * f) * .15 + f * .85          # a little slower at the entry and the exit
            target = f * s[2]
            pts, seg, L = s
            d0 = self.done[i]
            step = 2.0
            dd = d0
            while dd < target:
                dd = min(target, dd + step)
                (x, y) = self._at(pts, seg, dd)
                r = self.radius
                self.draw.ellipse([x - r, y - r, x + r, y + r], fill=255)
            self.done[i] = max(d0, target)

    @staticmethod
    def _at(pts, seg, s):
        for i, d in enumerate(seg):
            if s <= d or i == len(seg) - 1:
                u = min(1.0, s / d) if d > 0 else 1.0
                return (pts[i][0] + (pts[i + 1][0] - pts[i][0]) * u, pts[i][1] + (pts[i + 1][1] - pts[i][1]) * u)
            s -= d
        return pts[-1]

    def mask(self):
        rv = np.asarray(self.reveal.filter(ImageFilter.GaussianBlur(self.radius * .18)), np.float32) / 255.0
        ink = self.glyph * smooth(.35, .75, rv)
        halo = blur(ink, 2.2 * S) * .30                     # wet edge soaking into the hanji
        a = clamp01(ink * .97 + halo)
        big = np.zeros((H, W), np.float32)
        cx, cy = self.center
        x0, y0 = int(cx - self.w / 2), int(cy - self.h / 2)
        xs0, ys0 = max(0, -x0), max(0, -y0)
        xs1, ys1 = min(self.w, W - x0), min(self.h, H - y0)
        big[y0 + ys0:y0 + ys1, x0 + xs0:x0 + xs1] = a[ys0:ys1, xs0:xs1]
        return big


class Brush:
    """dry-brush ink stamps along the jamo strokes onto a persistent glyph canvas (alpha)"""

    def __init__(self, jamo, box, center, seed):
        self.box = int(box); self.center = center
        self.alpha = Image.new('L', (self.box, self.box), 0)
        self.draw = ImageDraw.Draw(self.alpha)
        self.strokes = []
        rnd = random.Random(seed)
        for pts in JAMO[jamo]:
            p = [(x * self.box, y * self.box) for x, y in pts]
            seg = [math.dist(p[i], p[i + 1]) for i in range(len(p) - 1)]
            self.strokes.append((p, seg, sum(seg), [rnd.random() for _ in range(64)]))
        self.total = sum(s[2] for s in self.strokes)
        self.done = 0.0
        self.width = self.box * .085

    def _point(self, stroke, s):
        p, seg, L, _ = stroke
        for i, d in enumerate(seg):
            if s <= d or i == len(seg) - 1:
                u = min(1.0, s / d) if d > 0 else 1.0
                a, b = p[i], p[i + 1]
                return (a[0] + (b[0] - a[0]) * u, a[1] + (b[1] - a[1]) * u), ((b[0] - a[0]) / max(d, 1e-6), (b[1] - a[1]) / max(d, 1e-6))
            s -= d
        return p[-1], (1.0, 0.0)

    def advance(self, progress):
        """progress 0..1 over the whole jamo (strokes in order, a short lift between)"""
        target = progress * self.total
        base = 0.0
        for stroke in self.strokes:
            L = stroke[2]
            lo, hi = max(self.done - base, 0.0), min(target - base, L)
            if hi > lo:
                s = lo
                while s <= hi:
                    self._stamp(stroke, s, L)
                    s += 1.2
            base += L
        self.done = max(self.done, target)

    def _stamp(self, stroke, s, L):
        (x, y), (dx, dy) = self._point(stroke, s)
        u = s / max(L, 1e-6)
        press = .72 + .38 * smooth(0, .08, u) - .45 * smooth(.78, 1.0, u)      # 입필 누름, 끝은 가늘게
        r = self.width * press
        nx, ny = -dy, dx
        noise = stroke[3]
        core = r * .62
        self.draw.ellipse([x - core, y - core, x + core, y + core], fill=255)
        k = 15
        dry = smooth(.55, 1.0, u) * .75                                           # 갈필: 끝으로 갈수록 마름
        for i in range(k):
            o = (i / (k - 1) - .5) * 2 * r
            h = noise[(i * 7 + int(s / 9)) % 64]
            if h < dry and abs(o) > core * .4: continue
            br = r * (.20 + .08 * h)
            bx, by = x + nx * o, y + ny * o
            self.draw.ellipse([bx - br, by - br, bx + br, by + br], fill=int(200 + 55 * h))

    def mask(self):
        a = np.asarray(self.alpha, np.float32) / 255.0
        big = np.zeros((H, W), np.float32)
        cx, cy = self.center
        x0, y0 = int(cx - self.box / 2), int(cy - self.box / 2)
        xs0, ys0 = max(0, -x0), max(0, -y0)
        xs1, ys1 = min(self.box, W - x0), min(self.box, H - y0)
        big[y0 + ys0:y0 + ys1, x0 + xs0:x0 + xs1] = a[ys0:ys1, xs0:xs1]
        return big


# ---------------------------------------------------------------- ink bleed / watercolour


class Bleed:
    """organic spread from a seed (point or mask) through wet paper; returns 0..1 mask and a pigment rim"""

    def __init__(self, center, seed, roughness=.42, fine=.07):
        cx, cy = center
        self.base = dist_field(cx, cy) + fractal(W, H, seed, base=3) * roughness + fractal(W, H, seed + 5, base=24, octaves=3) * fine

    def at(self, u, reach=1.9, soft=.045):
        R = ease_out(u) * reach
        m = smooth(0, soft, R - self.base)
        rim = smooth(0, soft * 2.2, R - self.base) * (1 - smooth(soft * .8, soft * 4.0, R - self.base))
        return m, rim


def watercolour(img_graded, img_raw, m, rim, color, amount=1.0):
    """inside the bloom the scene's own colour comes back and the 오방색 pigment settles, darker at the rim"""
    c = np.array(color, np.float32)
    m3, r3 = m[..., None] * amount, rim[..., None] * amount
    out = img_graded + (img_raw - img_graded) * (m3 * .55)
    if c.mean() > .85:                                     # 白: gofun white lifts toward paper light
        out = out + (c - out) * (m3 * .22 + r3 * .18)
    else:
        absorb = (1 - c) * (m3 * .30 + r3 * .34) * GRAIN
        out = out * (1 - absorb)
    return clamp01(out)


def ink_over(img, alpha, color=INK):
    a = alpha[..., None]
    return img * (1 - a) + np.array(color, np.float32) * a


# ---------------------------------------------------------------- title and seal


def text_mask(text, font_path, size, index=0):
    font = ImageFont.truetype(font_path, int(size), index=index)
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
    """낙관: 五行符 carved white on vermilion, rough stone edges"""
    s = int(size)
    img = Image.new('L', (s, int(s * 1.9)), 0)
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([0, 0, s - 1, int(s * 1.9) - 1], radius=int(s * .06), fill=255)
    font = ImageFont.truetype(FONT_SEAL, int(s * .52), index=0)
    for i, ch in enumerate('五行符'):
        x0, y0, x1, y1 = font.getbbox(ch)
        d.text(((s - (x1 - x0)) / 2 - x0, s * .10 + i * s * .56 - y0), ch, font=font, fill=0)
    a = np.asarray(img, np.float32) / 255.0
    rough = fractal(a.shape[1], a.shape[0], 77, base=6, octaves=4)
    a = a * smooth(.18, .30, rough + (a * .5))
    return a


# ---------------------------------------------------------------- timeline

CHAPTERS = [  # (element, jamo, takes as (name, seconds, start offset, speed))
    ('wood', 'ㄱ', [('wood_village', 2.2, 0.3, 1.0), ('wood_car', 2.4, 1.6, 1.0), ('wood_fox', 1.5, 0.4, 1.0), ('wood_dokkaebi', 1.5, 1.9, 1.0)]),
    ('fire', 'ㄴ', [('fire_field', 2.6, 0.4, 1.0), ('fire_beacon', 2.6, 0.3, 1.0)]),
    ('earth', 'ㅁ', [('earth_palace_aerial', 3.0, 0.5, 1.0), ('earth_palace', 2.2, 0.0, 1.0)]),
    ('metal', 'ㅅ', [('metal_fortress', 2.6, 0.5, 1.0), ('metal_wall', 1.8, 0.8, 1.0), ('metal_bulgasari', 2.0, 0.5, 1.0)]),
    ('water', 'ㅇ', [('water_bridge', 2.2, 0.6, 1.0), ('water_oldcapital', 2.0, 0.8, 1.0), ('water_mum', 4.0, 1.0, 1.6)]),
]
OPENING = ('op_cast_na', 7.4, 0.0, 1.0)
CLIMAX = ('climax_gom', 5.0, 0.8, 1.0)
WRITE, BLEED, XFADE = 1.25, 1.0, .55          # jamo writing, jamo bleed into the realm, ink dissolve between takes


def build_timeline():
    """returns list of events with absolute times; each event renders itself"""
    ev = []
    t = 0.0
    ev.append(('drop', t, 1.1)); t += 1.1                          # ink drop on bare hanji
    ev.append(('open', t, OPENING[1], OPENING)); t += OPENING[1]    # first-person play revealed inside the drop
    ev.append(('wash', t, .9)); t += .9                              # the scene floods back to paper
    for element, jamo, takes in CHAPTERS:
        ev.append(('jamo', t, WRITE + BLEED, element, jamo, takes[0])); t += WRITE + BLEED
        seg = []
        for i, tk in enumerate(takes):
            dur = tk[1] - (BLEED if i == 0 else 0)
            seg.append((t, dur, tk)); t += dur
        ev.append(('takes', seg[0][0], sum(d for _, d, _ in seg), element, seg))
    ev.append(('climax', t, CLIMAX[1], CLIMAX)); t += CLIMAX[1]
    ev.append(('title', t, 6.0)); t += 6.0
    return ev, t


class Renderer:
    def __init__(self):
        self.events, self.duration = build_timeline()
        self.clips = {}
        self.brushes = {}
        self.bleeds = {}
        self.title = text_mask('오행부', FONT_TITLE, 230 * S)
        self.seal = seal_mask(78 * S)

    def clip(self, key, take):
        if key not in self.clips:
            name, _, start, speed = take
            self.clips[key] = Clip(name, start, speed)
        return self.clips[key]

    def bleed(self, key, center, seed, **kw):
        if key not in self.bleeds: self.bleeds[key] = Bleed(center, seed, **kw)
        return self.bleeds[key]

    def frame(self, t):
        for e in self.events:
            kind, t0, dur = e[0], e[1], e[2]
            if t0 <= t < t0 + dur or (e is self.events[-1] and t >= t0):
                return getattr(self, 'r_' + kind)(e, t - t0, dur)
        return PAPER

    # --- event renderers

    def r_drop(self, e, lt, dur):
        u = lt / dur
        b = self.bleed('drop', (W * .5, H * .47), 11, roughness=.30)
        m, rim = b.at(u * .16, reach=1.0)
        return ink_over(PAPER, clamp01(m * .92 + rim * .08))

    def r_open(self, e, lt, dur):
        take = e[3]
        raw = self.clip('open', take).at(lt)
        g = grade(raw)
        b = self.bleed('drop', (W * .5, H * .47), 11, roughness=.30)
        u = min(1.0, .16 + lt / 1.4)
        m, rim = b.at(u, reach=2.0)
        out = PAPER * (1 - m[..., None]) + g * m[..., None]
        out = ink_over(out, rim * .6)
        # the written glyph ignites: warm 赤 bloom where the game's fire lives (right after the commit)
        if lt > 3.3:
            fb = self.bleed('openfire', (W * .5, H * .40), 23, roughness=.55)
            fm, frim = fb.at((lt - 3.3) / 1.4, reach=.55)
            fade = 1 - smooth(4.6, 6.4, lt)
            out = watercolour(out, raw, fm * fade, frim * fade, COLORS['fire'], .45)
        return out

    def r_wash(self, e, lt, dur):
        prev = grade(self.clip('open', OPENING).at(OPENING[1] + lt))
        b = self.bleed('wash', (W * .62, H * .55), 31, roughness=.5)
        m, rim = b.at(lt / dur, reach=2.1)
        paper = PAPER
        out = prev * (1 - m[..., None]) + paper * m[..., None]
        return ink_over(out, rim * .45)

    def r_jamo(self, e, lt, dur):
        _, t0, _, element, jamo, first = e
        key = 'jamo-' + element
        if key not in self.brushes:
            self.brushes[key] = Calligraphy(jamo, 520 * S, (W * .5, H * .5))
        br = self.brushes[key]
        br.advance(min(1.0, lt / WRITE))
        glyph = br.mask()
        color = COLORS[element]
        if lt < WRITE:
            return ink_over(PAPER, glyph * .96)
        # the written jamo takes its 오방색 and its ink bleeds through wet paper into the realm
        u = (lt - WRITE) / BLEED
        spread_r = 2 + u * 60 * S
        soak = blur(glyph, spread_r)
        grain = fractal(W, H, 300 + len(element), base=6, octaves=4) if not hasattr(self, 'grain_' + element) else getattr(self, 'grain_' + element)
        setattr(self, 'grain_' + element, grain)
        window = smooth(.10, .16, soak * (1.0 + u * 7.0) + grain * .35 * u - .12 * (1 - u))
        window = np.maximum(window, smooth(0, .05, ease_out(u) * 1.9 - (self.bleed('w-' + element, (W * .5, H * .5), 55 + len(element)).base)))
        raw = self.clip('take-' + element + '-0', first).at(lt - WRITE)
        g = grade(raw)
        cb = self.bleed('c-' + element, (W * .5, H * .5), 55 + len(element))
        cm, crim = cb.at(u * .85, reach=1.25)
        scene = watercolour(g, raw, cm, crim, color, .75 * STRENGTH[element])
        out = PAPER * (1 - window[..., None]) + scene * window[..., None]
        ink = glyph * (1 - smooth(.3, 1.0, u))
        tinted = np.array(INK) * (1 - min(1.0, u * 1.6)) + np.array(color) * min(1.0, u * 1.6)
        out = ink_over(out, ink * .92, tinted)
        rim = clamp01(window * (1 - window) * 4) * (1 - u)
        return ink_over(out, rim * .35, np.array(color) * .6)

    def r_takes(self, e, lt, dur):
        _, t0, _, element, seg = e
        t = t0 + lt
        color = COLORS[element]
        for i, (s0, d, tk) in enumerate(seg):
            if s0 <= t < s0 + d or i == len(seg) - 1:
                local = t - s0 + (BLEED if i == 0 else XFADE)
                raw = self.clip(f'take-{element}-{i}', tk).at(local)
                g = grade(raw)
                # chapter colour: the first take keeps a fading pigment bloom, later takes a faint wash
                cb = self.bleed('c-' + element, (W * .5, H * .5), 55 + len(element))
                amount = .62 * (1 - smooth(0, 2.4, t - seg[0][0])) + .10
                m, rim = cb.at(.85, reach=1.25)
                out = watercolour(g, raw, m, rim, color, amount * STRENGTH[element])
                # ink dissolve into the next take
                if i + 1 < len(seg) and t > s0 + d - XFADE:
                    u = (t - (s0 + d - XFADE)) / XFADE
                    nxt_raw = self.clip(f'take-{element}-{i + 1}', seg[i + 1][2]).at(t - (s0 + d - XFADE))
                    nxt = watercolour(grade(nxt_raw), nxt_raw, m, rim, color, .10)
                    b = self.bleed(f'x-{element}-{i}', (W * (.3 + .4 * ((i * 37) % 10) / 10), H * .55), 70 + i * 9 + len(element))
                    mm, rr = b.at(u, reach=2.0)
                    out = out * (1 - mm[..., None]) + nxt * mm[..., None]
                    out = ink_over(out, rr * .22)
                return out
        return PAPER

    def r_climax(self, e, lt, dur):
        take = e[3]
        raw = self.clip('climax', take).at(lt)
        g = grade(raw)
        if lt < .6:
            b = self.bleed('climax-in', (W * .5, H * .5), 91)
            m, rim = b.at(lt / .6, reach=2.0)
            prev = PAPER
            g = prev * (1 - m[..., None]) + g * m[..., None]
            g = ink_over(g, rim * .5)
        if lt > 2.6:   # the summoned beast in 靑 (the jamo ㄱ = 木)
            fb = self.bleed('climax-wood', (W * .5, H * .55), 97, roughness=.6)
            fm, frim = fb.at((lt - 2.6) / 2.0, reach=1.3)
            g = watercolour(g, raw, fm * .6, frim * .6, COLORS['wood'], .5)
        return g

    def r_title(self, e, lt, dur):
        last = grade(self.clip('climax', CLIMAX).at(CLIMAX[1] + lt))
        b = self.bleed('title-wash', (W * .5, H * .5), 101, roughness=.45)
        m, rim = b.at(min(1.0, lt / 1.1), reach=2.1)
        out = last * (1 - m[..., None]) + PAPER * m[..., None]
        out = ink_over(out, rim * .4)
        # five pigments settle around the centre (상생 order), then the title is written over them
        # 오행부: brush reveal left to right with a wet halo
        tm = place(self.title, W * .5, H * .47)
        reveal_u = clamp01((lt - 1.6) / 1.6)
        xx = np.linspace(0, 1, W, dtype=np.float32)[None, :]
        edge = smooth(0, .03, reveal_u * 1.1 - (xx * .8 + .1) + (fractal(W, H, 133, base=8, octaves=3) - .5) * .06)
        halo = blur(tm * edge, 6 * S) * .35
        out = ink_over(out, clamp01(tm * edge * .95 + halo * (1 - smooth(3.2, 4.4, lt))))
        # 낙관
        if lt > 3.6:
            su = clamp01((lt - 3.6) / .22)
            sm = place(self.seal, W * .5 + 330 * S, H * .47 + 70 * S)
            out = ink_over(out, sm * su * .92, (0.70, 0.16, 0.12))
        if lt > dur - .8:
            out = out * (1 - smooth(dur - .8, dur, lt) * 0.0)
        return out


# ---------------------------------------------------------------- audio (gayageum/buk bed + game SFX)

SR = 48000


def read_wav(name):
    with wave.open(str(SFX / f'{name}.wav')) as w:
        data = np.frombuffer(w.readframes(w.getnframes()), np.int16).astype(np.float32) / 32768.0
        if w.getnchannels() == 2: data = data.reshape(-1, 2).mean(1)
        if w.getframerate() != SR:
            x = np.linspace(0, len(data), int(len(data) * SR / w.getframerate()), endpoint=False)
            data = np.interp(x, np.arange(len(data)), data)
    return data


def pluck(freq, dur, bright=.6, seed=0, vib=5.2, depth=.010, bend=0.0):
    """Karplus-Strong string with 농현 (late vibrato) and an optional upward bend"""
    n = int(SR * dur)
    period = int(SR / freq)
    rng = np.random.default_rng(seed)
    buf = rng.uniform(-1, 1, period).astype(np.float32)
    for _ in range(int((1 - bright) * 6)): buf = .5 * (buf + np.roll(buf, 1))
    out = np.empty(n, np.float32)
    for i in range(n):
        v = buf[i % period]
        out[i] = v
        buf[i % period] = .4985 * (v + buf[(i + 1) % period])
    t = np.arange(n) / SR
    phase = t + np.cumsum(depth * np.sin(2 * np.pi * vib * t) * smooth(.25, .9, t)) / SR * 40 + bend * smooth(.05, .35, t) * t
    out = np.interp(np.clip(phase * SR, 0, n - 1), np.arange(n), out)
    env = np.exp(-t * 1.6) * smooth(0, .004, t)
    return (out * env).astype(np.float32)


def buk(dur=1.4, seed=0):
    t = np.arange(int(SR * dur)) / SR
    f = 62 * np.exp(-t * 3.5) + 44
    body = np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t * 4.2)
    click = np.random.default_rng(seed).normal(0, 1, len(t)) * np.exp(-t * 60) * .25
    return (body + click).astype(np.float32)


def drone(dur, freq=98.0):
    t = np.arange(int(SR * dur)) / SR
    tone = sum(np.sin(2 * np.pi * freq * k * t + k) / k ** 1.6 for k in range(1, 7))
    breath = np.random.default_rng(5).normal(0, 1, len(t))
    breath = np.convolve(breath, np.ones(60) / 60, mode='same')
    swell = .6 + .4 * np.sin(2 * np.pi * t / 9.0 - 1.5)
    return ((tone * .8 + breath * .25) * swell * smooth(0, 2.5, t) * (1 - smooth(dur - 3, dur, t))).astype(np.float32)


def add(track, clip, at, gain):
    i = int(at * SR)
    if i >= len(track): return
    j = min(len(track), i + len(clip))
    track[i:j] += clip[:j - i] * gain


def build_audio(events, duration, path):
    n = int((duration + .5) * SR)
    mix = np.zeros(n, np.float32)
    # 평조 pentatonic on G: G3 A3 C4 D4 E4 G4
    scale = [196.0, 220.0, 261.6, 293.7, 329.6, 392.0, 440.0]
    add(mix, drone(duration + .5), 0, .05)
    wind = read_wav('wind_loop')
    for k in range(int(duration / (len(wind) / SR)) + 1): add(mix, wind, k * len(wind) / SR, .10)
    brush = read_wav('brush_stroke'); fire = read_wav('cast_fire'); impact = read_wav('impact')
    summon = read_wav('summon_appear'); stamp = read_wav('inspection_stamp'); paper = read_wav('ui_paper')
    loops = {'fire': read_wav('fire_loop'), 'water': read_wav('stream_loop'), 'wood': read_wav('vehicle_roll_loop')}
    melody = {'wood': [0, 2, 3], 'fire': [3, 4, 5], 'earth': [2, 1, 0], 'metal': [4, 3, 5], 'water': [1, 0, 2]}
    for e in events:
        kind, t0 = e[0], e[1]
        if kind == 'drop':
            add(mix, pluck(scale[5], 3.0, seed=1), t0 + .35, .22)
        elif kind == 'open':
            # the take's own inputs: Q, strokes (brush on paper), commit (fire) — times from the film spec
            for st in (1.35, 2.05, 2.75): add(mix, brush, t0 + st, .55)
            add(mix, fire, t0 + 3.35, .7); add(mix, impact, t0 + 4.1, .6)
            add(mix, buk(seed=2), t0 + 3.35, .5)
        elif kind == 'wash':
            add(mix, paper, t0, .35)
        elif kind == 'jamo':
            element = e[3]
            strokes = len(JAMO[e[4]])
            for k in range(strokes): add(mix, brush, t0 + k * WRITE / strokes, .6)
            add(mix, buk(seed=hash(element) % 99), t0 + WRITE, .55)
            for k, deg in enumerate(melody[element]):
                add(mix, pluck(scale[deg], 2.6, seed=k + 10, bend=.02 if k == 2 else 0), t0 + WRITE + .12 + k * .42, .24)
        elif kind == 'takes':
            element = e[3]
            if element in loops:
                lp = loops[element]; span = e[2]
                seg = np.tile(lp, int(span * SR / len(lp)) + 1)[:int(span * SR)]
                seg = seg * smooth(0, .5, np.arange(len(seg)) / SR) * (1 - smooth(span - .6, span, np.arange(len(seg)) / SR))
                add(mix, seg, t0, .18)
        elif kind == 'climax':
            for st in (1.45, 2.0, 2.45, 2.95, 3.35, 3.7): add(mix, brush, t0 - .8 + st, .45)
            add(mix, summon, t0 + 2.9, .7); add(mix, buk(seed=7), t0 + 2.9, .55)
        elif kind == 'title':
            for k, deg in enumerate([5, 4, 3, 2, 0]): add(mix, pluck(scale[deg], 3.5, seed=30 + k, bend=.015 if k == 4 else 0), t0 + .5 + k * .12, .18)
            for k in range(3): add(mix, brush, t0 + 1.6 + k * .5, .4)
            add(mix, stamp, t0 + 3.62, .8); add(mix, buk(2.4, seed=9), t0 + 3.62, .45)
    peak = np.abs(mix).max()
    mix = np.tanh(mix / max(peak, 1e-6) * 1.2) * .89
    pcm = (mix * 32767).astype(np.int16)
    with wave.open(str(path), 'wb') as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR); w.writeframes(pcm.tobytes())
    return path


# ---------------------------------------------------------------- main


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    r = Renderer()
    start = float(sys.argv[sys.argv.index('--start') + 1]) if '--start' in sys.argv else 0.0
    end = float(sys.argv[sys.argv.index('--end') + 1]) if '--end' in sys.argv else r.duration
    tag = ('preview' if PREVIEW else 'final') + ('' if start == 0 and end == r.duration else f'_{start:g}-{end:g}')
    video = OUT / f'trailer297_{tag}_video.mp4'
    cmd = [FFMPEG, '-y', '-loglevel', 'error', '-f', 'rawvideo', '-pix_fmt', 'rgb24', '-s', f'{W}x{H}', '-r', str(FPS), '-i', '-',
           '-c:v', 'libx264', '-preset', 'medium', '-crf', '17', '-pix_fmt', 'yuv420p', '-threads', '4', str(video)]
    proc = subprocess.Popen(cmd, stdin=subprocess.PIPE)
    n0, n1 = int(start * FPS), int(end * FPS)
    for f in range(n0, n1):
        img = r.frame(f / FPS)
        proc.stdin.write((clamp01(img) * 255 + .5).astype(np.uint8).tobytes())
        if f % 60 == 0: print(f'{f / FPS:5.1f}s / {end:.1f}s', flush=True)
    proc.stdin.close(); proc.wait()
    if '--no-audio' in sys.argv: print(video); return
    audio = build_audio(r.events, r.duration, OUT / 'trailer297_audio.wav')
    final = OUT / f'trailer297_{tag}.mp4'
    subprocess.run([FFMPEG, '-y', '-loglevel', 'error', '-i', str(video), '-ss', f'{start}', '-i', str(audio), '-map', '0:v', '-map', '1:a',
                    '-c:v', 'copy', '-c:a', 'aac', '-b:a', '192k', '-shortest', str(final)], check=True)
    print(final, f'{r.duration:.1f}s')


if __name__ == '__main__':
    main()
