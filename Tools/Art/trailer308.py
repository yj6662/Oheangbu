"""#308 trailer for the final award review deck (~45 s, 1920x1080, 30 fps). The #302 renderer (letterbox, brush cards, ink bleed,
grade, title and seal) with the #308 takes: a letter written in first person as the hook, the five realms after their dressing,
the magic-stone car, the tainted forest, then letters and spells in quick cuts. No text cards; the title comes last.

Takes: Art/Presentation297/Films/<take>.mp4 from Tools/Unity/Stage308_ops/trailer_shoot.sh (r_* camera paths, g_* first person).
A take that is missing is skipped (the cut closes up), so a partial shoot still renders.

usage: python Tools/Art/trailer308.py [--preview] [--start S] [--end S] [--no-audio]
Output: Art/Pitch308/Trailer/trailer308_<preview|final>.mp4
"""
import sys
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).parent))
import trailer302 as b  # noqa: E402
t = b.t

b.OUT = t.ROOT / 'Art/Pitch308/Trailer'

# (kind, seconds, take, source start s, speed, effect) — letter takes: the drawing runs slow in the capture, so it plays faster here
# No text cards (user, 2026-10-07): play, art style, each realm's character, the magic-stone car.
# Shape taken from what "best game trailer of all time" lists praise: the one thing no other game has in the first five seconds,
# a slow passage that lets the world be looked at, real play instead of cinematics, cuts that get shorter toward the end, the title last.
TL = [
    ('black', .5),
    ('shot', 4.6, 'g_ga', None, None, 'inkin'),           # hook: a letter is written and becomes a spell
    ('shot', 3.6, 'r_cheongrim', .6, 1.0, 'inkin'),       # the world, slowly
    ('shot', 3.0, 'r_inn', .6, 1.0, None),
    ('shot', 3.4, 'g_car', 2.5, 1.0, None),               # the magic-stone car
    ('shot', 3.0, 'r_gate', .8, 1.0, None),
    ('shot', 2.4, 'r_palace', .6, 1.0, None),
    ('shot', 2.4, 'r_jeokro', .6, 1.0, 'inkin'),          # the realms, faster
    ('shot', 2.2, 'r_cheolong', .6, 1.0, None),
    ('shot', 2.6, 'r_hyeongang', .8, 1.0, None),
    ('shot', 2.2, 'r_taint', .6, 1.0, None),
    ('shot', 3.0, 'g_na', None, None, 'inkin'),           # play, in shorter and shorter cuts
    ('shot', 2.6, 'g_seo', None, None, None),
    ('shot', 2.2, 'g_ma', None, None, None),
    ('shot', 2.0, 'g_sa', None, None, None),
    ('shot', 2.0, 'g_a', None, None, None),
    ('black', .7),
    ('title', 5.5),
]
MOVES = {
    'g_ga': (1.0, 1.05, .5, .5, 2, ()), 'g_na': (1.02, 1.06, .5, .5, 2, ()), 'g_seo': (1.02, 1.06, .5, .5, 2, ()),
    'g_ma': (1.02, 1.06, .5, .5, 2, ()), 'g_sa': (1.02, 1.06, .5, .5, 2, ()), 'g_a': (1.02, 1.06, .5, .5, 2, ()),
    'r_cheongrim': (1.0, 1.05, .5, .5, 0, ()), 'r_gate': (1.05, 1.0, .5, .5, 0, ()), 'r_jeokro': (1.0, 1.06, .5, .5, 1, ()),
    'r_cheolong': (1.06, 1.0, .5, .5, 0, ()), 'r_inn': (1.0, 1.05, .5, .5, 0, ()), 'r_palace': (1.05, 1.0, .5, .5, 0, ()), 'g_car': (1.0, 1.04, .5, .5, 3, ()), 'r_hyeongang': (1.0, 1.06, .5, .55, 0, ()), 'r_taint': (1.0, 1.07, .5, .6, 1, ()),
}
# a take that was not shot: its stand-in, or dropped when that is missing too
ALT = {'r_jeokro': 'r_jeokro2', 'r_cheolong': 'r_cheolong2', 'r_hyeongang': 'r_hyeongang2', 'r_taint': 'r_taint2'}


def outdoor_span(name):
    """A letter take holds the whole event (walk up, write, the spell leaves) in a couple of film seconds, with the dark mine
    before and after it (the measurement moves the player there and back). Returns (first, last) frame of the bright stretch."""
    from PIL import Image
    folder = t.FILMS / name
    frames = sorted(folder.glob('f*.png'))
    lum = []
    for f in frames[:240]:
        im = Image.open(f).convert('L').resize((32, 18))
        lum.append(sum(im.getdata()) / (32 * 18))
    bright = [i for i, v in enumerate(lum) if v > 95]
    if not bright:
        return None
    runs, a = [], bright[0]
    for i, j in zip(bright, bright[1:] + [None]):
        if j is None or j != i + 1:
            runs.append((a, i)); a = j
    return max(runs, key=lambda r: r[1] - r[0])


def available(tl):
    out = []
    for item in tl:
        if item[0] != 'shot':
            out.append(item); continue
        name = item[2]
        if item[3] is None:                      # a letter take: play its bright stretch across the cut, minus the walk-up
            span = outdoor_span(name) if (t.FILMS / name).exists() else None
            if span is None or span[1] - span[0] < 20:
                print('no usable stretch in', name, '- dropped'); continue
            first, last = span[0] + 8, span[1] - 1
            item = item[:3] + (first / 30.0, max(.3, (last - first) / 30.0 / item[1]), item[5])
            print(name, 'frames', first, last, 'speed %.2f' % item[4])
        if not (t.FILMS / f'{name}.mp4').exists():
            alt = ALT.get(name)
            if alt and (t.FILMS / f'{alt}.mp4').exists():
                MOVES[alt] = MOVES.get(name, (1.0, 1.04, .5, .5, 0, ()))
                item = item[:2] + (alt,) + item[3:]
            else:
                print('no take', name, '- dropped'); continue
        out.append(item)
    return out


def build_audio(events, duration, path):
    """drone and wind under everything; brush strokes and a drum on the letter takes, a low drum on each realm, plucks on cards and title"""
    SR = t.SR
    n = int((duration + .5) * SR)
    mix = np.zeros(n, np.float32)
    scale = [196.0, 220.0, 261.6, 293.7, 329.6, 392.0, 440.0]
    bed = t.drone(duration + .5); bed *= t.smooth(0, 3.0, np.arange(len(bed)) / SR)
    t.add(mix, bed, 0, .06)
    wind = t.read_wav('wind_loop')
    for k in range(int(duration / (len(wind) / SR)) + 1): t.add(mix, wind, k * len(wind) / SR, .09)
    brush = t.read_wav('brush_stroke'); fire = t.read_wav('cast_fire'); impact = t.read_wav('impact'); stamp = t.read_wav('inspection_stamp')
    for e in events:
        t0, kind, dur = e[0], e[1], e[2]
        if kind == 'card':
            for k, deg in enumerate([5, 3, 2]): t.add(mix, t.pluck(scale[deg], 2.8, seed=40 + k + int(t0), bend=.02 if k == 2 else 0), t0 + .2 + k * .45, .22)
            for k in range(3): t.add(mix, brush, t0 + .15 + k * .38, .35)
        elif kind == 'shot':
            name = e[3]
            if name == 'g_car':
                roll = t.read_wav('vehicle_roll_loop'); seg = np.tile(roll, int(dur * SR / len(roll)) + 1)[:int(dur * SR)]
                t.add(mix, seg, t0, .22); t.add(mix, t.buk(1.6, seed=5), t0, .5)
            elif name.startswith('g_'):
                for k in range(4): t.add(mix, brush, t0 + .25 + k * dur * .17, .45)
                t.add(mix, fire, t0 + dur * .78, .6); t.add(mix, t.buk(seed=4 + int(t0)), t0 + dur * .78, .45)
                t.add(mix, impact, min(t0 + dur * .93, t0 + dur - .05), .4)
            else:
                t.add(mix, t.buk(1.2, seed=int(t0) % 50), t0, .4)
        elif kind == 'title':
            for k, deg in enumerate([5, 4, 3, 2, 0]): t.add(mix, t.pluck(scale[deg], 3.5, seed=30 + k, bend=.015 if k == 4 else 0), t0 + .2 + k * .12, .18)
            for k in range(3): t.add(mix, brush, t0 + .4 + k * .45, .38)
            t.add(mix, stamp, t0 + 2.32, .8); t.add(mix, t.buk(2.4, seed=9), t0 + 2.32, .5)
    tt = np.arange(n) / SR
    mix *= (1 - t.smooth(duration - 1.2, duration, tt))
    mix = np.tanh(mix / max(np.abs(mix).max(), 1e-6) * 1.2) * .89
    import wave
    with wave.open(str(path), 'wb') as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR); w.writeframes((mix * 32767).astype(np.int16).tobytes())
    return path


def grade308(img):
    """the game's own colour, kept: each realm is told apart by its tint (the #302 grade halved the chroma). Paper white, soft blacks, fibre and vignette as before."""
    lum = (img @ np.array([.299, .587, .114], np.float32))[..., None]
    out = lum + (img - lum) * .92
    out = out * np.array([1.0, .99, .965], np.float32)
    out = .03 + out * .96
    out = out * (1 + t.FIBRE * .05) * t.VIGNETTE
    out = np.clip((out - .5) * 1.08 + .5, 0, 1)
    return t.clamp01(out * .97)


if __name__ == '__main__':
    b.TL = available(TL)
    b.MOVES = MOVES
    b.build_audio = build_audio
    b.grade302 = grade308
    b.main()
    for f in sorted(b.OUT.glob('trailer302_*')):          # the #302 renderer names its files after itself
        g = f.with_name(f.name.replace('trailer302_', 'trailer308_'))
        if g.exists(): g.unlink()
        f.rename(g); print(g)
