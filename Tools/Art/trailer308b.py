"""#308 trailers (SPEC-TRAILER-308): the 30 s presentation cut and the 90 s promotion cut, 1920x1080, 30 fps.
No captions and no cards: the only text is the title at the end. Play first (a letter written and landing), then the road.

Takes: Art/Presentation297/Films/<take>.mp4 from Tools/Unity/Stage308_trailer/shoot308.sh and mum_b.sh
  p_*  first-person play (real input)      c_*  Cinemachine rig shots (TrailerRig308)
Music: Art/Pitch308/Trailer/music/bgm308_<30|90>_1.mp3 (ElevenLabs Music; commercial terms not confirmed = TEST). Without it: the synthesised bed.

usage: python Tools/Art/trailer308b.py 30|90 [--preview] [--no-audio]
Output: Art/Pitch308/Trailer/trailer308_<30|90>[_preview].mp4
"""
import subprocess
import sys
from pathlib import Path

import numpy as np

LENGTH = int(sys.argv[1]); del sys.argv[1]
sys.path.insert(0, str(Path(__file__).parent))
import trailer302 as b  # noqa: E402
import trailer308 as c  # noqa: E402
t = b.t

b.OUT = t.ROOT / 'Art/Pitch308/Trailer'
MUSIC = b.OUT / 'music' / f'bgm308_{LENGTH}_1.mp3'

# (kind, seconds, take, source start s, speed, effect)
# 2026-10-09 recut (user: 뭄 빼고 전부 새로, 보스전은 금강역사, 아트는 강토별로). Order of a trailer that sells the mechanic:
# the hook first (a letter written, the spell lands), the world it happens in (one image a realm), the same act on a larger thing
# (뭄: the river), a still beat, then the giant - and the cut ends on the blow in the air, not on its result.
TL30 = [
    ('shot', 4.0, 'p_hook', .75, 1.0, 'inkin'),     # hook: a letter is written, the spell leaves and lands
    ('shot', 1.5, 'c_exit', 1.2, 2.0, None),          # the world opens
    ('shot', 1.5, 'c_layers', 1.5, 1.0, None),        # 청림
    ('shot', 1.5, 'c_jeokro', 1.2, 1.0, None),        # 적로
    ('shot', 1.5, 'c_cheolong2', 1.5, 1.0, None),     # 철옹
    ('shot', 1.5, 'c_hyeongang', 1.5, 1.0, None),     # 현강
    ('shot', 1.5, 'c_capital', 1.5, 1.0, None),       # 황경
    ('shot', 3.0, 'p_road', .75, 1.0, None),          # the act again: another letter, another spell
    ('shot', 5.0, 'p_mum', 4.6, 1.0, None),           # and on the world itself: stones rise across the river
    ('shot', 2.0, 'c_temple', 2.4, 1.0, None),        # the still beat
    ('shot', 4.0, 'c_scale', 2.0, 1.0, None),         # the Geumgang guardian over the small player: no motion, no fight (user 2026-10-10)
    ('title', 3.0),
]
# the 90 s opening (user 2026-10-10: 초반부분 수정): the dark mine cuts went - after the hook the screen opens straight onto the land,
# the car and the inn, and the second letter comes within twenty seconds
TL90 = [
    ('shot', 5.0, 'p_hook', .5, 1.0, 'inkin'),
    ('shot', 6.0, 'c_exit', .8, 1.0, None),
    ('shot', 4.0, 'c_car', 1.0, 1.0, None),
    ('shot', 4.0, 'c_inn', 0, 1.0, None),
    ('shot', 5.0, 'p_road', .5, 1.0, None),           # play 2: another letter on the road
    ('shot', 5.0, 'c_village', 0, 1.0, None),         # 청림
    ('shot', 5.0, 'c_layers', .8, 1.0, None),
    ('shot', 4.0, 'c_jeokro', 0, 1.0, None),          # 적로
    ('shot', 4.0, 'c_cheolong', 0, 1.0, None),        # 철옹
    ('shot', 4.0, 'c_cheolong2', .8, 1.0, None),
    ('shot', 12.0, 'p_mum', 3.6, 1.0, None),          # play 3: 뭄 - written, the stones cross, the walk over them
    ('shot', 5.0, 'c_hyeongang', 0, 1.0, None),       # 현강
    ('shot', 5.0, 'c_cliffway', 0, 1.0, None),
    ('shot', 5.0, 'c_capital', 0, 1.0, None),         # 황경
    ('shot', 4.0, 'c_temple', 1.0, 1.0, None),        # the still beat
    ('shot', 8.0, 'c_scale', .8, 1.0, None),          # the Geumgang guardian over the small player - then the title
    ('title', 5.0),
]
TL = TL30 if LENGTH == 30 else TL90
assert abs(sum(x[1] for x in TL) - LENGTH) < 1e-6, sum(x[1] for x in TL)
# a take that was not shot or is unusable: its stand-in
ALT = {'p_road': 'p_hook', 'c_car': 'c_layers', 'c_scale': 'c_temple', 'c_jeokro': 'c_layers', 'c_cheolong': 'c_cheolong2', 'c_cheolong2': 'c_layers', 'c_hyeongang': 'c_cliffway', 'c_capital': 'c_layers'}
STILL = (1.0, 1.0, .5, .5, 0, ())                     # rig shots already move; play cuts stay steady so the letter can be read
MOVES = {name: STILL for name in {x[2] for x in TL if x[0] == 'shot'} | set(ALT.values())}


def title(self, lt, dur):
    """the #302 title card with its timing scaled to the cut's length (written in 2 s of 5.5; here in the same share)"""
    k = 5.5 / dur
    return _title(self, lt * k, dur * k)


_title = b.Trailer.r_title
b.Trailer.r_title = title


def music(duration):
    if not MUSIC.exists():
        return None
    raw = subprocess.run([t.FFMPEG, '-loglevel', 'error', '-i', str(MUSIC), '-ac', '1', '-ar', str(t.SR), '-f', 's16le', '-'], capture_output=True, check=True).stdout
    m = np.frombuffer(raw, np.int16).astype(np.float32) / 32768.0
    n = int(duration * t.SR)
    return np.pad(m, (0, max(0, n - len(m))))[:n]


def soft(x, k=9):
    """a moving average: takes the hiss off the brush sample"""
    return np.convolve(x, np.ones(k, np.float32) / k, mode='same').astype(np.float32)


def build_audio(events, duration, path):
    """music under everything, pulled down while a letter is written so the brush, the cast, the hit, the stones and the steps are heard"""
    SR = t.SR; n = int(duration * SR)
    mix = np.zeros(n, np.float32); tt = np.arange(n) / SR
    bed = music(duration)
    gain = np.ones(n, np.float32)
    brush = t.read_wav('brush_stroke'); fire = t.read_wav('cast_fire'); impact = t.read_wav('impact'); roll = t.read_wav('vehicle_roll_loop'); wind = t.read_wav('wind_loop')
    for e in events:
        t0, kind, dur = e[0], e[1], e[2]
        if kind != 'shot':
            continue
        name = e[3]
        if name.startswith('p_') and name != 'p_mine_hand':
            gain *= 1 - .55 * (t.smooth(t0 - .15, t0 + .1, tt) - t.smooth(t0 + dur - .2, t0 + dur + .1, tt))
            t.add(mix, soft(brush), t0 + .05, .14)          # one soft stroke: four loud ones tore the ear (user, 2026-10-08)
            if name == 'p_mum':
                for k in range(6): t.add(mix, t.buk(.5, seed=20 + k), t0 + dur * .5 + k * .18, .32)      # stones seating one after another
            else:
                t.add(mix, fire, t0 + .9, .4); t.add(mix, impact, t0 + 1.5, .4)
        elif name == 'p_mine_hand':
            t.add(mix, soft(brush), t0 + .4, .1)
        elif name == 'c_car':
            seg = np.tile(roll, int(dur * SR / len(roll)) + 1)[:int(dur * SR)]; t.add(mix, seg, t0, .3)
        elif name == 'c_rush':
            seg = wind[:int(1.6 * SR)] * np.linspace(.2, 1, int(1.6 * SR)).astype(np.float32) if len(wind) >= int(1.6 * SR) else wind
            t.add(mix, seg, t0 + .4, .5); t.add(mix, t.buk(1.8, seed=3), t0 + 2.15, .7)
    if bed is not None:
        mix += bed * gain * .85
    else:
        d = t.drone(duration + .5)[:n]; mix[:len(d)] += d * .06
    mix *= (1 - t.smooth(duration - .5, duration, tt))
    mix = mix / max(np.abs(mix).max(), 1e-6) * .85       # no saturation: it roughened the effects
    import wave
    with wave.open(str(path), 'wb') as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR); w.writeframes((mix * 32767).astype(np.int16).tobytes())
    return path


if __name__ == '__main__':
    c.ALT = ALT; c.MOVES = MOVES
    b.TL = c.available(TL)
    short = LENGTH - sum(x[1] for x in b.TL)
    if short > 1e-6:                                    # a dropped cut: the last landscape cut before the title holds longer, so the length stays exact
        i = max(k for k, x in enumerate(b.TL) if x[0] == 'shot')
        b.TL[i] = (b.TL[i][0], b.TL[i][1] + short) + tuple(b.TL[i][2:]); print('held the last cut', '%.1f s longer' % short)
    b.MOVES = MOVES
    b.build_audio = build_audio
    b.grade302 = c.grade308
    b.main()
    tag = 'preview' if '--preview' in sys.argv else 'final'
    for f in sorted(b.OUT.glob('trailer302_*')):
        g = f.with_name(f.name.replace('trailer302_', f'trailer308_{LENGTH}_'))
        if g.exists(): g.unlink()
        f.rename(g); print(g)
