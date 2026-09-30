"""#302 opening trailer for the pitch deck (~60 s, 1920x1080, 30 fps): cinematic mood -> fast gameplay -> title.

Grammar of a AAA reveal trailer, in ink: 2.39:1 letterbox, a slow cold open (world, village, the dokkaebi), two
brush-typography cards on black carrying the one phrase, then hard cuts on the drum through the world and the
fights, the 앞잡 volley in slow motion as the peak, a beat of black, the brush title and seal.
Takes: Art/Presentation297/Films/<take>.mp4 (the _302 takes are the current look). Tools from trailer297.py.
Audio: the #297 trailer's own instruments and sounds (drone, wind, gayageum plucks, buk, game SFX) re-timed to
this cut; --old-audio muxes the unchanged #297 wav instead.

usage: python Tools/Art/trailer302.py [--preview] [--start S] [--end S] [--no-audio] [--old-audio]
Output: Art/Presentation302/Trailer/
"""
import subprocess
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont

sys.path.insert(0, str(Path(__file__).parent))
import trailer297 as t  # noqa: E402  (reads --preview from argv, sets W/H/S)

W, H, S, FPS = t.W, t.H, t.S, t.FPS
OUT = t.ROOT / 'Art/Presentation302/Trailer'
BLACK = np.zeros((H, W, 3), np.float32) + .018
LIGHT = np.array([.93, .91, .86], np.float32)
BAR = int(round((H - W / 2.39) / 2))            # letterbox bar height

# (take, source start s, speed) per shot; durations live in the timeline
TL = [
    ('black', 1.0),
    ('shot', 3.2, 'end_vista_302', .6, 1.0, 'inkin'),
    ('shot', 2.8, 'wood_village_302', .6, 1.0, None),
    ('card', 2.6, ['붓 한 자루를 든', '도사가 되어']),
    ('shot', 2.0, 'earth_palace_aerial_302', .8, 1.0, 'inkin'),
    ('shot', 1.8, 'metal_fortress_302', .8, 1.0, None),
    ('card', 2.6, ['마법과 전설이 살아 숨 쉬는', '조선을 누빈다']),
    ('shot', 4.0, 'op_cast_na_302', 1.2, 1.0, 'inkin'),
    ('shot', 2.4, 'wood_car_302', 1.4, 1.0, None),
    ('shot', 1.4, 'fire_field_302', .8, 1.0, None),
    ('shot', 1.5, 'metal_bulgasari_302', .6, 1.0, None),
    ('shot', 2.6, 'water_mum', 3.0, 1.2, None),
    ('shot', 2.6, 'dokkaebi_fight_302', 2.3, 1.0, None),
    ('black', .4),
    # the giant: frames are read at 30 fps, so the 60/90 fps takes play 2x/3x slow (start = frame / 30)
    ('shot', 5.0, 'giant_reveal', .6, 1.0, 'inkin'),
    ('shot', 4.5, 'giant_smash_slow', 1.8, 1.0, None),
    ('shot', 4.2, 'giant_jump_orbit', .5, 1.0, None),
    ('shot', 4.0, 'giant_cast_fp', 1.0, 1.0, None),
    ('shot', 4.6, 'giant_riposte', 2.2, 1.0, None),
    # same take, contiguous frames at real time: the slow 앞잡 ramps into the fall (name#key = own camera move)
    ('shot', 3.2, 'giant_riposte#fall', 6.8, 2.0, None),
    ('black', .8),
    ('title', 5.5),
]
# camera on top of the takes: (zoom from, zoom to, focus x, focus y, handheld px at 1080p, impact punches at take seconds)
MOVES = {
    'end_vista_302': (1.0, 1.08, .5, .45, 0, ()), 'wood_village_302': (1.07, 1.0, .5, .5, 0, ()),
    'confront_302': (1.0, 1.12, .55, .45, 3, ()), 'op_cast_na_302': (1.02, 1.06, .5, .5, 2, (3.4, 4.2)),
    'wood_car_302': (1.04, 1.07, .5, .55, 4, ()), 'earth_palace_aerial_302': (1.0, 1.07, .5, .5, 0, ()),
    'metal_fortress_302': (1.08, 1.0, .5, .5, 0, ()), 'fire_field_302': (1.0, 1.06, .5, .5, 1, ()),
    'metal_bulgasari_302': (1.0, 1.07, .55, .6, 2, ()), 'water_bridge_302': (1.06, 1.0, .5, .5, 0, ()),
    'water_mum': (1.02, 1.06, .5, .55, 2, ()), 'climax_gom_302': (1.02, 1.05, .5, .55, 2, (4.4,)),
    'dokkaebi_fight_302': (1.02, 1.08, .5, .5, 3, (3.4, 4.2)),
    'riposte_side_302': (1.08, 1.16, .5, .3, 2, (2.1, 3.1, 3.7, 4.3, 4.9, 5.5, 5.75)),
    'riposte_fp_302': (1.03, 1.06, .5, .45, 4, (3.2, 3.9)),
    # giant takes: punch times in 30 fps-equivalent take seconds (game seconds x capture fps / 30)
    # blows from the clip analysis (Guardian302Build analyze): smash downswing, leap landing, the body meeting the ground
    'giant_reveal': (1.0, 1.04, .5, .4, 2, (2.2,)), 'giant_smash_slow': (1.0, 1.06, .5, .5, 1, (3.6,)),
    'giant_jump_orbit': (1.0, 1.05, .5, .45, 1, (3.84,)), 'giant_cast_fp': (1.02, 1.05, .5, .5, 2, (3.4, 4.35)),
    'giant_riposte': (1.04, 1.10, .5, .32, 1, (4.0, 5.0, 6.0)), 'giant_riposte#fall': (1.10, 1.14, .5, .34, 1, (7.0, 11.8)),
}


def camera(img, name, lt, dur, start, speed):
    """digital push/pull, handheld drift and impact punches on a graded frame (float 0..1, H x W x 3)"""
    if name not in MOVES:
        return img
    z0, z1, fx, fy, hand, punches = MOVES[name]
    u = t.clamp01(lt / max(dur, 1e-3)); u = u * u * (3 - 2 * u)
    z = z0 + (z1 - z0) * u
    shake = hand * S
    for p in punches:
        d = lt - (p - start) / speed
        if d >= 0:
            k = np.exp(-d * 9.0)
            z += .07 * k; shake += 14 * S * k
    ox = shake * (np.sin(lt * 7.3) + .5 * np.sin(lt * 17.1 + 1.0)) + hand * S * 2 * np.sin(lt * .9)
    oy = shake * (np.sin(lt * 6.1 + 2.0) + .5 * np.sin(lt * 13.7)) + hand * S * 1.5 * np.sin(lt * .7 + 1)
    if z <= 1.0005 and abs(ox) < .5 and abs(oy) < .5:
        return img
    z = max(z, 1.0 + (abs(ox) + abs(oy)) * 2.2 / W)          # always enough margin for the drift
    cw, ch = W / z, H / z
    x0 = min(max(fx * W - cw / 2 + ox, 0), W - cw); y0 = min(max(fy * H - ch / 2 + oy, 0), H - ch)
    from PIL import Image as _I
    im = _I.fromarray((t.clamp01(img) * 255 + .5).astype(np.uint8))
    im = im.resize((W, H), _I.BICUBIC, box=(x0, y0, x0 + cw, y0 + ch))
    return np.asarray(im, np.float32) / 255.0


FALLBACK = {'wood_car_302': 'wood_car', 'dokkaebi_low_302': 'wood_dokkaebi', 'riposte_fp_302': 'riposte_side_302', 'confront_302': 'wood_dokkaebi'}


def timeline():
    ev, at = [], 0.0
    for item in TL:
        ev.append((at,) + item)
        at += item[1]
    return ev, at


def grade302(img):
    """the trailer297 수묵 grade, then a filmic S-curve: deeper blacks, held highlights"""
    g = t.grade(img)
    g = np.clip((g - .5) * 1.12 + .5, 0, 1)
    return t.clamp01(g * .96)


def letterbox(img):
    img = img.copy()
    img[:BAR] = .0; img[H - BAR:] = .0
    return img


class Card:
    """two lines of brush typography revealed left to right with a wet edge, then sinking back into black"""

    def __init__(self, lines, seed):
        font = ImageFont.truetype(t.FONT_TITLE, int(92 * S))
        lh = int(138 * S)
        m = Image.new('L', (W, H), 0); d = ImageDraw.Draw(m)
        y0 = H / 2 - lh * len(lines) / 2
        for i, line in enumerate(lines):
            x0, _, x1, _ = font.getbbox(line)
            d.text(((W - (x1 - x0)) / 2 - x0, y0 + i * lh), line, font=font, fill=255)
        self.mask = np.asarray(m, np.float32) / 255.0
        self.noise = t.fractal(W, H, seed, base=8, octaves=3)
        self.halo = t.blur(self.mask, 5 * S)

    def at(self, lt, dur):
        xx = np.linspace(0, 1, W, dtype=np.float32)[None, :]
        u = t.clamp01(lt / 1.3)
        edge = t.smooth(0, .04, u * 1.15 - (xx * .9 + .05) + (self.noise - .5) * .08)
        out = 1 - t.smooth(dur - .7, dur, lt)
        a = t.clamp01(self.mask * edge * .97 + self.halo * edge * .25 * (1 - t.smooth(1.2, 2.2, lt))) * out
        return BLACK * (1 - a[..., None]) + LIGHT * a[..., None]


class Trailer:
    def __init__(self):
        self.events, self.duration = timeline()
        self.clips, self.cards, self.bleeds = {}, {}, {}
        self.title = t.text_mask('오행부', t.FONT_TITLE, 210 * S)
        self.seal = t.seal_mask(64 * S)
        self.sub = t.text_mask('五行符', t.FONT_SEAL, 34 * S)

    def clip(self, i, name, start, speed):
        if i not in self.clips:
            path = t.FILMS / f'{name}.mp4'
            if not path.exists() and name in FALLBACK:
                print('stand-in', name, '->', FALLBACK[name]); name = FALLBACK[name]
            self.clips[i] = t.Clip(name, start, speed)
        return self.clips[i]

    def frame(self, tt):
        for i, e in enumerate(self.events):
            t0, kind, dur = e[0], e[1], e[2]
            if t0 <= tt < t0 + dur or i == len(self.events) - 1:
                lt = tt - t0
                if kind == 'black':
                    return BLACK
                if kind == 'card':
                    if i not in self.cards: self.cards[i] = Card(e[3], 700 + i)
                    return letterbox(self.cards[i].at(lt, dur))
                if kind == 'shot':
                    _, _, _, name, start, speed, fx = e
                    img = camera(grade302(self.clip(i, name.split('#')[0], start, speed).at(lt)), name, lt, dur, start, speed)
                    if fx == 'inkin' and lt < .9:        # the shot bleeds in from black like ink on wet paper
                        if i not in self.bleeds: self.bleeds[i] = t.Bleed((W * .5, H * .5), 40 + i, roughness=.45)
                        m, rim = self.bleeds[i].at(lt / .9, reach=2.0)
                        img = BLACK * (1 - m[..., None]) + img * m[..., None]
                    if lt > dur - .12 and i + 1 < len(self.events) and self.events[i + 1][1] != 'shot':
                        img = img * (1 - t.smooth(dur - .12, dur, lt))
                    return letterbox(img)
                if kind == 'title':
                    return self.r_title(lt, dur)
        return BLACK

    def r_title(self, lt, dur):
        out = BLACK.copy()
        tm = t.place(self.title, W * .5, H * .46)
        xx = np.linspace(0, 1, W, dtype=np.float32)[None, :]
        if not hasattr(self, '_tn'): self._tn = t.fractal(W, H, 133, base=8, octaves=3)
        u = t.clamp01((lt - .3) / 1.7)
        edge = t.smooth(0, .03, u * 1.1 - (xx * .8 + .1) + (self._tn - .5) * .06)
        halo = t.blur(tm * edge, 6 * S) * .3 * (1 - t.smooth(2.2, 3.4, lt))
        a = t.clamp01(tm * edge * .96 + halo)
        out = out * (1 - a[..., None]) + LIGHT * a[..., None]
        if lt > 2.3:
            su = t.clamp01((lt - 2.3) / .2)
            sm = t.place(self.seal, W * .5 + 370 * S, H * .46 + 40 * S)
            out = out * (1 - (sm * su * .95)[..., None]) + np.array([.62, .16, .12], np.float32) * (sm * su * .95)[..., None]
        if lt > 3.0:
            s2 = t.clamp01((lt - 3.0) / .8)
            sub = t.place(self.sub, W * .5, H * .46 + 170 * S) * s2 * .75
            out = out * (1 - sub[..., None]) + LIGHT * sub[..., None]
        fade = 1 - t.smooth(dur - 1.0, dur, lt)
        return letterbox(out * fade + BLACK * (1 - fade))


# ---------------------------------------------------------------- audio: the #297 instruments, re-timed to this cut

def build_audio(events, duration, path):
    SR = t.SR
    n = int((duration + .5) * SR)
    mix = np.zeros(n, np.float32)
    scale = [196.0, 220.0, 261.6, 293.7, 329.6, 392.0, 440.0]
    bed = t.drone(duration + .5); bed *= t.smooth(0, 3.0, np.arange(len(bed)) / SR)
    t.add(mix, bed, 0, .06)
    wind = t.read_wav('wind_loop')
    for k in range(int(duration / (len(wind) / SR)) + 1): t.add(mix, wind, k * len(wind) / SR, .09)
    brush = t.read_wav('brush_stroke'); fire = t.read_wav('cast_fire'); impact = t.read_wav('impact')
    summon = t.read_wav('summon_appear'); stamp = t.read_wav('inspection_stamp')
    roll = t.read_wav('vehicle_roll_loop'); stream = t.read_wav('stream_loop')

    def loop(lp, t0, span, gain):
        seg = np.tile(lp, int(span * SR / len(lp)) + 1)[:int(span * SR)]
        tt = np.arange(len(seg)) / SR
        t.add(mix, seg * t.smooth(0, .3, tt) * (1 - t.smooth(span - .3, span, tt)), t0, gain)

    def src(t0, start, speed, s):          # absolute time of a moment s seconds into the take
        return t0 + (s - start) / speed

    for e in events:
        t0, kind, dur = e[0], e[1], e[2]
        if kind == 'card':
            for k, deg in enumerate([5, 3, 2]):
                t.add(mix, t.pluck(scale[deg], 2.8, seed=40 + k + int(t0), bend=.02 if k == 2 else 0), t0 + .2 + k * .45, .22)
            for k in range(3): t.add(mix, brush, t0 + .15 + k * .38, .35)
        elif kind == 'shot':
            name, start, speed = e[3], e[4], e[5]
            if name in ('confront_302', 'dokkaebi_low_302'):
                t.add(mix, t.buk(2.0, seed=3), t0, .6)
            if name.startswith('op_cast_na') or name.startswith('dokkaebi_fight'):
                for s in (1.35, 2.05, 2.75): t.add(mix, brush, src(t0, start, speed, s), .5)
                t.add(mix, fire, src(t0, start, speed, 3.4), .7); t.add(mix, impact, src(t0, start, speed, 4.2), .55)
                t.add(mix, t.buk(seed=4), src(t0, start, speed, 3.4), .45)
            if name.startswith('wood_car'):
                loop(roll, t0, dur, .22); t.add(mix, t.buk(1.6, seed=5), t0, .55)
            if name == 'water_mum':
                loop(stream, t0, dur, .18)
            if name.startswith('climax_gom'):
                t.add(mix, summon, src(t0, start, speed, 4.4), .7); t.add(mix, t.buk(seed=6), src(t0, start, speed, 4.4), .5)
            if name.startswith('riposte_side'):
                # twelve circles burst open, then 48 도깨비불 land one after another, the last with the drum
                t.add(mix, t.buk(2.4, seed=7), src(t0, start, speed, 2.05), .7)
                for k in range(12): t.add(mix, brush, src(t0, start, speed, 2.0 + k * .045), .24)
                for k in range(24): t.add(mix, impact, src(t0, start, speed, 2.95 + k * .115), .13)
                t.add(mix, impact, src(t0, start, speed, 5.7), .5); t.add(mix, t.buk(2.0, seed=8), src(t0, start, speed, 5.7), .6)
            if name.startswith('riposte_fp'):
                for k in range(10): t.add(mix, impact, src(t0, start, speed, 3.0 + k * .11), .13)
            if name in ('earth_palace_aerial_302', 'metal_fortress_302', 'fire_field_302', 'metal_bulgasari_302', 'water_bridge_302'):
                t.add(mix, t.buk(1.0, seed=hash(name) % 50), t0, .38)
        elif kind == 'title':
            for k, deg in enumerate([5, 4, 3, 2, 0]): t.add(mix, t.pluck(scale[deg], 3.5, seed=30 + k, bend=.015 if k == 4 else 0), t0 + .2 + k * .12, .18)
            for k in range(3): t.add(mix, brush, t0 + .4 + k * .45, .38)
            t.add(mix, stamp, t0 + 2.32, .8); t.add(mix, t.buk(2.4, seed=9), t0 + 2.32, .5)
    tt = np.arange(n) / SR
    mix *= (1 - t.smooth(duration - 1.2, duration, tt))
    peak = np.abs(mix).max()
    mix = np.tanh(mix / max(peak, 1e-6) * 1.2) * .89
    import wave
    with wave.open(str(path), 'wb') as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR); w.writeframes((mix * 32767).astype(np.int16).tobytes())
    return path


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    r = Trailer()
    start = float(sys.argv[sys.argv.index('--start') + 1]) if '--start' in sys.argv else 0.0
    end = float(sys.argv[sys.argv.index('--end') + 1]) if '--end' in sys.argv else r.duration
    tag = ('preview' if t.PREVIEW else 'final') + ('' if start == 0 and end == r.duration else f'_{start:g}-{end:g}')
    video = OUT / f'trailer302_{tag}_video.mp4'
    cmd = [t.FFMPEG, '-y', '-loglevel', 'error', '-f', 'rawvideo', '-pix_fmt', 'rgb24', '-s', f'{W}x{H}', '-r', str(FPS), '-i', '-',
           '-c:v', 'libx264', '-preset', 'medium', '-crf', '17', '-pix_fmt', 'yuv420p', '-threads', '4', str(video)]
    proc = subprocess.Popen(cmd, stdin=subprocess.PIPE)
    for f in range(int(start * FPS), int(end * FPS)):
        img = r.frame(f / FPS)
        proc.stdin.write((t.clamp01(img) * 255 + .5).astype(np.uint8).tobytes())
        if f % 90 == 0: print(f'{f / FPS:5.1f}s / {end:.1f}s', flush=True)
    proc.stdin.close(); proc.wait()
    if '--no-audio' in sys.argv:
        print(video); return
    audio = t.OUT / 'trailer297_audio.wav' if '--old-audio' in sys.argv else build_audio(r.events, r.duration, OUT / 'trailer302_audio.wav')
    final = OUT / f'trailer302_{tag}{"_oldaudio" if "--old-audio" in sys.argv else ""}.mp4'
    subprocess.run([t.FFMPEG, '-y', '-loglevel', 'error', '-i', str(video), '-ss', f'{start}', '-i', str(audio), '-map', '0:v', '-map', '1:a',
                    '-c:v', 'copy', '-c:a', 'aac', '-b:a', '192k', '-shortest', str(final)], check=True)
    print(final, f'{r.duration:.1f}s')


if __name__ == '__main__':
    main()
