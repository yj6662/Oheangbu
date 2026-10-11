"""소리 — numpy + wave, 48 kHz 스테레오 WAV. 음악 베드(ffmpeg 파이프 디코드) × 엔벌로프 + Compact255 효과음.

엔벌로프(음악): p_ 컷 .55 덕킹(±.15 s 램프) · w_still 0(.3 s 램프 = stopdown) · w_giant 즉시 1 + 합성 북 + impact
  · p_burst_* .7 · 암전 .35 + 직전 0.3 s 잔향(fftconvolve) · 제목 .35 · 끝 1 s 페이드.
효과음: brush_stroke soft .14 · cast_<오행>(ㄱ木 ㄴ火 ㅁ土 ㅅ金 ㅇ水; 金은 cast_metal, 없으면 cast_earth) · impact · enemy_windup/swing
  · dodge · vehicle_board/roll_loop · step_* 0.5 s 교대 · stream_loop · wind_loop · inspection_stamp(제목 낙관).
marks(테이크 초) → 출력 초: t = t0 + (s − src) / speed. 피크 .85 정규화(tanh 금지) · 끝 0.5 s 페이드 · 길이 = 프레임 수 / 30 정확히.

usage:
  python audio.py build 30|90 [--music X|none] [--offset S] [--out WAV] [--fake] [--no-report]
  python audio.py suggest --music X --hit 19.0 [--length 30] [--top 3]
  python audio.py ledger --file suno_30_1.mp3 --plan Pro --model v4.5 --reviewed-by 한예준 [--prompt …] [--note …]
"""
import argparse
import json
import math
import subprocess
import sys
import time
import wave
from pathlib import Path

import numpy as np
from scipy.signal import fftconvolve

sys.path.insert(0, str(Path(__file__).resolve().parent))
from common import FFMPEG, FPS, MUSIC, SFX, WORK, ensure_dirs, probe, scan_films  # noqa: E402
from timeline import AbortTimeline, Cut, out_time, resolve, starts, timeline  # noqa: E402

SR = 48000
CHO = 'ㄱㄲㄴㄷㄸㄹㅁㅂㅃㅅㅆㅇㅈㅉㅊㅋㅌㅍㅎ'
ELEMENT = {'ㄱ': 'wood', 'ㄴ': 'fire', 'ㅁ': 'earth', 'ㅅ': 'metal', 'ㅇ': 'water'}
GAIN = {'brush': .14, 'cast': .40, 'hit': .40, 'windup': .35, 'swing': .35, 'dodge': .35, 'board': .40, 'roll': .30,
        'step': .18, 'stream': .18, 'wind': .10, 'stamp': .80, 'lockon': .30, 'stones': .32}
DUCK_PLAY, DUCK_BURST, DUCK_BLACK, DUCK_TITLE = .55, .30, .65, .65     # 1 − 레벨: p_ → .45, burst → .7, 암전 · 제목 → .35
RAMP_PLAY, RAMP_STILL = .30, .30                                        # ±.15 s · .3 s
REVERB_PRE, REVERB_LEN = .30, 1.2


# ---------------------------------------------------------------- 보조
def smooth(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def element_of(ch):
    code = ord(ch) - 0xAC00
    if 0 <= code < 11172:
        return ELEMENT.get(CHO[code // 588], 'earth')
    return ELEMENT.get(ch, 'earth')


def cast_sfx(ch):
    elem = element_of(ch)
    if elem == 'metal' and not (SFX / 'cast_metal.wav').exists():
        return 'cast_earth'
    return f'cast_{elem}'


_wav = {}


def read_wav(name):
    """Compact255/<name>.wav → 모노 float32 48 kHz (다른 샘플레이트면 선형 보간)."""
    if name in _wav:
        return _wav[name]
    with wave.open(str(SFX / f'{name}.wav')) as w:
        raw = w.readframes(w.getnframes())
        width, ch, sr = w.getsampwidth(), w.getnchannels(), w.getframerate()
    if width == 2:
        data = np.frombuffer(raw, np.int16).astype(np.float32) / 32768.0
    elif width == 4:
        data = np.frombuffer(raw, np.int32).astype(np.float32) / 2147483648.0
    else:
        data = (np.frombuffer(raw, np.uint8).astype(np.float32) - 128) / 128.0
    if ch > 1:
        data = data.reshape(-1, ch).mean(1)
    if sr != SR:
        x = np.linspace(0, len(data), int(len(data) * SR / sr), endpoint=False)
        data = np.interp(x, np.arange(len(data)), data).astype(np.float32)
    _wav[name] = data
    return data


def soft(x, k=9):
    """이동 평균: 붓 샘플의 쉿 소리를 뺀다."""
    return np.convolve(x, np.ones(k, np.float32) / k, mode='same').astype(np.float32)


def buk(dur=1.4, seed=0):
    t = np.arange(int(SR * dur)) / SR
    f = 62 * np.exp(-t * 3.5) + 44
    body = np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t * 4.2)
    click = np.random.default_rng(seed).normal(0, 1, len(t)) * np.exp(-t * 60) * .25
    return (body + click).astype(np.float32)


def reverb_ir(dur=REVERB_LEN, seed=11):
    t = np.arange(int(SR * dur)) / SR
    rng = np.random.default_rng(seed)
    ir = np.stack([rng.normal(0, 1, len(t)) * np.exp(-t * 4.5), rng.normal(0, 1, len(t)) * np.exp(-t * 4.5)], 1).astype(np.float32)
    ir /= np.abs(ir).sum(0, keepdims=True) / 6.0        # 에너지 보존 정도: 잔향이 원음보다 커지지 않게
    return ir


def add(mix, clip, t, gain, pan=0.0):
    """모노 clip을 t초에 더한다(스테레오 mix (n,2)). pan −1..1."""
    i = int(round(t * SR))
    if i >= len(mix) or gain == 0:
        return
    if i < 0:
        clip, i = clip[-i:], 0
    j = min(len(mix), i + len(clip))
    if j <= i:
        return
    ang = (pan + 1) * math.pi / 4
    seg = clip[:j - i] * gain
    mix[i:j, 0] += seg * math.cos(ang)
    mix[i:j, 1] += seg * math.sin(ang)


def tiled(lp, span, ramp=.3):
    """루프를 span초로 이어 붙이고 양끝 ramp."""
    n = int(span * SR)
    if n <= 0:
        return np.zeros(0, np.float32)
    seg = np.tile(lp, n // len(lp) + 1)[:n]
    tt = np.arange(n) / SR
    return (seg * smooth(0, ramp, tt) * (1 - smooth(span - ramp, span, tt))).astype(np.float32)


def music_bed(path, offset, n):
    """ffmpeg 파이프: -ss offset → f32le 2ch 48 kHz. 길이 n으로 자르거나 0으로 채운다."""
    cmd = [FFMPEG, '-v', 'error', '-ss', f'{offset:.3f}', '-i', str(path), '-f', 'f32le', '-ac', '2', '-ar', str(SR), '-']
    raw = subprocess.run(cmd, capture_output=True, check=True).stdout
    arr = np.frombuffer(raw, np.float32).reshape(-1, 2)
    if len(arr) < n:
        arr = np.pad(arr, ((0, n - len(arr)), (0, 0)))
    return arr[:n].copy()


# ---------------------------------------------------------------- 엔벌로프(음악)
def level_of(cut):
    if cut.kind == 'black':
        return 1 - DUCK_BLACK
    if cut.kind == 'title':
        return 1 - DUCK_TITLE
    if cut.take == 'w_still':
        return 0.0
    if cut.take == 'w_giant':
        return 1.0
    if cut.is_burst:
        return 1 - DUCK_BURST
    if cut.is_play:
        return 1 - DUCK_PLAY
    return 1.0


def ramp_of(cut):
    """컷에 들어갈 때의 램프 폭(초). w_giant는 0 = 즉시."""
    if cut.take == 'w_giant':
        return 0.0
    if cut.take == 'w_still':
        return RAMP_STILL
    return RAMP_PLAY


def envelope(cuts, t0s, n):
    env = np.ones(n, np.float32)
    prev = 1.0
    for c, t0 in zip(cuts, t0s):
        lv = level_of(c)
        b = int(round(t0 * SR))
        env[b:] = lv
        r = int(ramp_of(c) * SR)
        if r > 0 and b > 0:
            lo, hi = max(0, b - r // 2), min(n, b + r // 2)
            env[lo:hi] = np.linspace(prev, lv, hi - lo, dtype=np.float32)
        prev = lv
    T = n / SR
    tt = np.arange(n) / SR
    env *= (1 - smooth(T - 1.0, T, tt)).astype(np.float32)
    return env


# ---------------------------------------------------------------- 효과음 자리
def sfx_layer(cuts, t0s, n, log=None):
    mix = np.zeros((n, 2), np.float32)
    placed = []

    def put(name, t, gain, pan=0.0, clip=None, tag=None):
        if t < -0.5 or t > n / SR:
            return
        add(mix, read_wav(name) if clip is None else clip, t, gain, pan)
        placed.append({'t': round(float(t), 3), 'sfx': tag or name, 'gain': gain})

    for c, t0 in zip(cuts, t0s):
        t1 = t0 + c.dur
        if c.kind == 'title':
            k = c.dur / 5.5                                 # 제목 카드는 5.5 s 수치를 dur로 축척한다(title.py)
            for j in range(3):
                put('brush_stroke', t0 + (.4 + j * .45) * k, .10, clip=soft(read_wav('brush_stroke')), tag='brush(title)')
            put('inspection_stamp', t0 + 2.3 * k, GAIN['stamp'])
            put('buk', t0 + 2.3 * k, .45, clip=buk(2.4, 9), tag='buk(seal)')
            continue
        if c.kind != 'shot':
            continue
        if c.take == 'w_giant':
            put('buk', t0, .70, clip=buk(1.8, 3), tag='buk(giant)')
            put('impact', t0, .50)
        for m in c.marks:
            kind, s = m[0], float(m[1])
            if kind in ('roll', 'step', 'stream', 'wind'):
                a = max(t0, out_time(c, t0, s))
                b = min(t1, out_time(c, t0, float(m[2])))
                if b - a < .1:
                    continue
                if kind == 'step':
                    surface = m[3] if len(m) > 3 else 'stone'
                    name, j, t = f'step_{surface}', 0, a
                    while t < b - .05:
                        put(name, t, GAIN['step'] * (1.0 if j % 2 == 0 else .85), -.25 if j % 2 == 0 else .25)
                        t += .5
                        j += 1
                else:
                    name = {'roll': 'vehicle_roll_loop', 'stream': 'stream_loop', 'wind': 'wind_loop'}[kind]
                    put(name, a, GAIN[kind], clip=tiled(read_wav(name), b - a), tag=f'{name}[{b - a:.1f}s]')
                continue
            t = out_time(c, t0, s)
            if not (t0 - .05 <= t < t1):
                continue
            if kind == 'brush':
                put('brush_stroke', t, GAIN['brush'], clip=soft(read_wav('brush_stroke')))
            elif kind == 'cast':
                put(cast_sfx(m[2] if len(m) > 2 else '마'), t, GAIN['cast'])
            elif kind in ('hit', 'impact'):
                put('impact', t, GAIN['hit'])
            elif kind == 'windup':
                put('enemy_windup', t, GAIN['windup'], .2)
            elif kind == 'swing':
                put('enemy_swing', t, GAIN['swing'], .2)
            elif kind == 'dodge':
                put('dodge', t, GAIN['dodge'])
            elif kind == 'board':
                put('vehicle_board', t, GAIN['board'])
            elif kind == 'lockon':
                put('lock_on', t, GAIN['lockon'])
            elif kind == 'stamp':
                put('inspection_stamp', t, GAIN['stamp'])
            elif kind == 'buk':
                put('buk', t, .5, clip=buk(1.4, int(s * 10) % 97), tag='buk')
            elif kind == 'stones':                           # 뭄: 돌이 하나씩 앉는다
                for j in range(6):
                    put('buk', t + j * .18, GAIN['stones'], clip=buk(.5, 20 + j), tag='buk(stone)')
    if log is not None:
        log.extend(placed)
    return mix


# ---------------------------------------------------------------- build
def build(length, cuts, music=None, offset=0.0, out=None, bed_gain=.85):
    ensure_dirs()
    frames = sum(c.frames for c in cuts)
    n = frames * SR // FPS
    t0s = starts(cuts)
    placed = []
    mix = sfx_layer(cuts, t0s, n, placed)
    env = envelope(cuts, t0s, n)
    if music:
        bed = music_bed(music, offset, n)
        mix += bed * env[:, None] * bed_gain
    # 암전: 직전 0.3 s를 잔향으로 늘여 검은 화면 속으로 흘린다
    ir = reverb_ir()
    for c, t0 in zip(cuts, t0s):
        if c.kind != 'black':
            continue
        b = int(round(t0 * SR))
        a = max(0, b - int(REVERB_PRE * SR))
        if b - a < SR // 20:
            continue
        seg = mix[a:b].copy()
        tail = np.stack([fftconvolve(seg[:, 0], ir[:, 0]), fftconvolve(seg[:, 1], ir[:, 1])], 1).astype(np.float32)
        j = min(n, a + len(tail))
        mix[a:j] += tail[:j - a] * .5
    T = n / SR
    tt = np.arange(n) / SR
    mix *= (1 - smooth(T - .5, T, tt))[:, None].astype(np.float32)
    peak = float(np.abs(mix).max())
    if peak > 1e-6:
        mix = mix / peak * .85
    out = Path(out or WORK / f'audio_{length}.wav')
    out.parent.mkdir(parents=True, exist_ok=True)
    with wave.open(str(out), 'wb') as w:
        w.setnchannels(2)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes((np.clip(mix, -1, 1) * 32767).astype('<i2').tobytes())
    info = {'path': str(out), 'samples': int(n), 'seconds': n / SR, 'peak_after': float(np.abs(mix).max()), 'peak_before': peak,
            'music': str(music) if music else None, 'offset': offset, 'sfx_count': len(placed), 'sfx': placed}
    with open(out.with_suffix('.json'), 'w', encoding='utf-8') as f:
        json.dump(info, f, ensure_ascii=False, indent=1)
    return info


def cuts_for(length, fake=False, use_report=True):
    """report_<N>.json(cut.py가 쓴 실제 컷)이 있으면 그것, 아니면 Films(또는 test_takes)로 resolve."""
    rep = WORK / f'report_{length}.json'
    if use_report and rep.exists():
        d = json.loads(rep.read_text(encoding='utf-8'))
        return [Cut.from_dict(x) for x in d['cuts']], 'report'
    films = scan_films(WORK / 'test_takes' if fake else None)
    cuts, _ = resolve(timeline(length), {k: v.get('duration') for k, v in films.items()})
    return cuts, 'resolve'


def default_music(length):
    p = MUSIC / f'bgm308_{length}_1.mp3'
    return p if p.exists() else None


def music_path(x, length):
    if x is None:
        return default_music(length)
    if str(x).lower() == 'none':
        return None
    p = Path(x)
    if not p.exists() and (MUSIC / x).exists():
        p = MUSIC / x
    if not p.exists():
        raise FileNotFoundError(p)
    return p


# ---------------------------------------------------------------- suggest: 온셋 에너지로 오프셋 후보
def suggest(music, hit, length=30, top=3, hop=.01, win=.04):
    cmd = [FFMPEG, '-v', 'error', '-i', str(music), '-f', 'f32le', '-ac', '1', '-ar', str(SR), '-']
    x = np.frombuffer(subprocess.run(cmd, capture_output=True, check=True).stdout, np.float32)
    dur = len(x) / SR
    h, w = int(hop * SR), int(win * SR)
    k = (len(x) - w) // h
    idx = np.arange(k)[:, None] * h + np.arange(w)[None, :]
    rms = np.sqrt((x[idx] ** 2).mean(1) + 1e-12)
    e = 20 * np.log10(rms + 1e-6)
    look = int(1.5 / hop)
    onset = np.zeros(k, np.float32)
    for i in range(look, k):
        onset[i] = max(0.0, e[i] - e[i - look:i - 5].mean())
    cands = []
    for i in range(1, k - 1):
        if onset[i] >= onset[i - 1] and onset[i] > onset[i + 1] and onset[i] > 0:
            cands.append((float(onset[i]), i * hop))
    cands.sort(reverse=True)
    picked = []
    for strength, t in cands:
        if any(abs(t - p[1]) < 1.0 for p in picked):
            continue
        picked.append((strength, t))
        if len(picked) >= top * 4:
            break
    rows = []
    for strength, t in picked:
        off = t - hit
        if off < 0:
            continue
        covered = off + length <= dur + .3
        quiet = e[max(0, int((t - 1.5) / hop)):max(1, int((t - .1) / hop))].mean()
        rows.append({'offset': round(off, 2), 'music_t': round(t, 2), 'rise_db': round(strength, 1), 'before_db': round(float(quiet), 1),
                     'covers_length': covered})
        if len(rows) >= top:
            break
    return {'music': str(music), 'duration': round(dur, 2), 'hit': hit, 'length': length, 'candidates': rows}


# ---------------------------------------------------------------- ledger
def ledger(file, plan, model, reviewed_by, prompt=None, note=None):
    ensure_dirs()
    p = Path(file)
    if not p.exists() and (MUSIC / file).exists():
        p = MUSIC / file
    info = probe(p) if p.exists() else {'duration': None, 'bytes': None}
    row = {'file': p.name, 'seconds': round(info['duration'], 2) if info.get('duration') else None, 'bytes': info.get('bytes'),
           'at': time.strftime('%Y-%m-%d %H:%M:%S'), 'source': 'suno', 'plan': plan, 'model': model, 'reviewed_by': reviewed_by,
           'human_in_the_loop': True}
    if prompt:
        row['prompt'] = prompt
    if note:
        row['note'] = note
    with open(MUSIC / 'ledger.jsonl', 'a', encoding='utf-8') as f:
        f.write(json.dumps(row, ensure_ascii=False) + '\n')
    return row


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest='cmd', required=True)
    b = sub.add_parser('build')
    b.add_argument('length', type=int, choices=(30, 90))
    b.add_argument('--music', default=None, help='경로 | music/ 안의 이름 | none (기본 bgm308_<N>_1.mp3)')
    b.add_argument('--offset', type=float, default=0.0)
    b.add_argument('--out', default=None)
    b.add_argument('--fake', action='store_true', help='_work/test_takes 로 resolve')
    b.add_argument('--no-report', action='store_true', help='report_<N>.json 을 쓰지 않고 다시 resolve')
    s = sub.add_parser('suggest')
    s.add_argument('--music', required=True)
    s.add_argument('--hit', type=float, required=True, help='트레일러에서 타격이 와야 할 초(거인 발견 = 30초판 19.0)')
    s.add_argument('--length', type=int, default=30)
    s.add_argument('--top', type=int, default=3)
    l = sub.add_parser('ledger')
    l.add_argument('--file', required=True)
    l.add_argument('--plan', required=True)
    l.add_argument('--model', required=True)
    l.add_argument('--reviewed-by', required=True)
    l.add_argument('--prompt', default=None)
    l.add_argument('--note', default=None)
    a = ap.parse_args(argv)
    if a.cmd == 'build':
        try:
            cuts, how = cuts_for(a.length, a.fake, not a.no_report)
        except AbortTimeline as e:
            print('ABORT', e)
            sys.exit(2)
        music = music_path(a.music, a.length)
        info = build(a.length, cuts, music, a.offset, a.out)
        print(f"{info['path']}  {info['seconds']:.3f}s  peak {info['peak_after']:.3f}  sfx {info['sfx_count']}  cuts:{how}  music:{music}")
    elif a.cmd == 'suggest':
        r = suggest(music_path(a.music, a.length), a.hit, a.length, a.top)
        print(f"{r['music']}  {r['duration']}s  hit@{r['hit']}s")
        print(' offset   music_t  rise_dB  before_dB  covers')
        for c in r['candidates']:
            print(f" {c['offset']:6.2f}  {c['music_t']:7.2f}  {c['rise_db']:7.1f}  {c['before_db']:9.1f}  {c['covers_length']}")
        if not r['candidates']:
            print(' (후보 없음 — --hit 보다 뒤에 오는 온셋이 없다)')
    elif a.cmd == 'ledger':
        print(json.dumps(ledger(a.file, a.plan, a.model, a.reviewed_by, a.prompt, a.note), ensure_ascii=False))


if __name__ == '__main__':
    main()
