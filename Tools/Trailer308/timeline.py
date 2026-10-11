"""Trailer308 정본 타임라인 — 영상(cut.py) · 소리(audio.py) · 검사(check.py)가 같은 것을 import한다.

Cut(name, dur, src, speed, fx_in, fx_out, marks, take):
  name   컷 이름(p_ = 실제 입력 1인칭, w_ = 카메라 경로, black, title)
  dur    출력 초(1/30 배수) · src 테이크 안의 시작 초 · speed 재생 배속(2.0 = 두 배 빠르게)
  fx_in  'inkin'(먹 번짐으로 열림) · fx_out 'dip'(끝 0.12 s 암전)
  marks  [(kind, 테이크 초, *extra)] — 소리 자리. 출력 시각 t = t0 + (s − src) / speed (audio.py)
         kind: lockon · brush · cast(글자) · hit · windup · swing · dodge · board · roll(s0,s1) · step(s0,s1,바닥)
               stream(s0,s1) · wind(s0,s1) · stones(뭄 돌 솟음) · stamp · buk
  take   파일 이름(기본 = name). Films/<take>.mp4

TL30 = 900 f(플레이 18.0 s) · TL90 = 2,700 f(플레이 68.5 s). 수치는 계획서(atomic-tumbling-rainbow.md §새 콘티) 그대로.
두 판 모두 로고 직전 컷은 거인 앞 붓 드는 순간(take p_burst_giant, 이름 burst_hold) → 0.12 s 암전.
src · marks는 테이크가 오기 전의 자리 표시 — `sheet.py <take> --take`로 보고 고친다.
ALT는 같은 종류만(p_hook→p_hook_b, p_walk↔p_village/p_cliffway, p_mum↔p_mum_long, …). REQUIRED(w_giant · p_hook)는 대체까지 없으면 중단.
"""
import copy
from dataclasses import dataclass, field

from common import FPS, PLAY_MIN, frames_of, on_grid


@dataclass
class Cut:
    name: str
    dur: float
    src: float = 0.0
    speed: float = 1.0
    fx_in: str | None = None
    fx_out: str | None = None
    marks: list = field(default_factory=list)
    take: str | None = None
    kind: str = 'shot'

    def __post_init__(self):
        if self.kind == 'shot' and self.take is None:
            self.take = self.name

    @property
    def frames(self):
        return frames_of(self.dur)

    @property
    def is_play(self):
        return self.kind == 'shot' and bool(self.take) and self.take.startswith('p_')

    @property
    def is_burst(self):
        return self.kind == 'shot' and (self.name.startswith('p_burst') or self.name == 'burst_hold')

    def need(self):
        """테이크에서 필요한 구간의 끝 초."""
        return self.src + self.dur * self.speed

    def to_dict(self):
        return {'name': self.name, 'kind': self.kind, 'take': self.take, 'dur': self.dur, 'frames': self.frames, 'src': self.src,
                'speed': self.speed, 'fx_in': self.fx_in, 'fx_out': self.fx_out, 'marks': [list(m) for m in self.marks]}

    @staticmethod
    def from_dict(d):
        return Cut(d['name'], d['dur'], d.get('src', 0.0), d.get('speed', 1.0), d.get('fx_in'), d.get('fx_out'),
                   [tuple(m) for m in d.get('marks', [])], d.get('take'), d.get('kind', 'shot'))


# ---------------------------------------------------------------- 소리 자리(테이크 초). 훅 테이크: aim 0–0.6 → Tab → glyph 1.0–2.4 → 적중 → LeftShift
M_HOOK = [('lockon', .7), ('windup', .3), ('brush', 1.0), ('cast', 2.45, '나'), ('hit', 3.2)]
M_CAR = [('board', .3), ('roll', .9, 20.0)]
M_FIGHT = [('windup', .4), ('swing', 1.0), ('brush', 1.2), ('cast', 2.5, '가'), ('hit', 3.0), ('dodge', 3.5),
           ('brush', 3.9), ('cast', 5.1, '사'), ('hit', 5.6)]
M_FIGHT2 = [('windup', .4), ('swing', 1.0), ('brush', 1.2), ('cast', 2.5, '마'), ('hit', 3.0), ('dodge', 3.5),
            ('brush', 3.9), ('cast', 5.1, '마'), ('hit', 5.6), ('hit', 5.75), ('hit', 5.9)]
M_MUM = [('stream', 0.0, 20.0), ('brush', .8), ('cast', 2.3, '뭄'), ('stones', 2.6), ('step', 3.6, 20.0, 'water')]


def tl30():
    return [
        Cut('p_hook', 4.0, .5, 1.0, 'inkin', None, M_HOOK),
        Cut('w_reveal', 3.0, .5),
        Cut('p_car', 2.5, 1.0, 1.0, None, None, M_CAR),
        Cut('p_walk', 1.5, 1.0, 1.0, None, None, [('step', 0.0, 20.0, 'stone'), ('wind', 0.0, 20.0)]),
        Cut('p_mum', 3.0, 1.6, 1.0, None, None, M_MUM),
        Cut('p_fight', 3.0, 1.0, 1.0, None, None, M_FIGHT),
        Cut('w_still', 2.0, .5, 1.0, None, None, [('wind', 0.0, 20.0)]),
        Cut('w_giant', 3.0, .3),
        # 22–26: 획 클로즈업 → 발사 → 적중 · 회피 → 거인 앞 붓 드는 순간(p_burst_giant)에서 끊고 0.12 s 암전 → 로고(사용자 결정 표 · 거인 행)
        Cut('p_burst_1', 1.2, .8, 1.0, None, None, [('brush', 1.0)], 'p_burst_1'),
        Cut('p_burst_2', 1.0, 1.8, 1.0, None, None, [('cast', 2.3, '나')], 'p_burst_2'),
        Cut('p_burst_3', 0.9, 2.7, 1.0, None, None, [('hit', 3.0), ('dodge', 3.4)], 'p_burst_3'),
        Cut('burst_hold', 0.9, .5, 1.0, None, 'dip', [('brush', .8)], 'p_burst_giant'),
        Cut('black', 0.5, kind='black'),
        Cut('title', 3.5, kind='title'),
    ]


def tl90():
    bursts = [  # (dur, take, src, marks) — p_burst_1..4 · p_fight · p_hook 창을 돌려 쓴다
        (4.0, 'p_burst_1', .3, [('brush', 1.0), ('cast', 2.4, '나'), ('hit', 3.2)]),
        (3.5, 'p_burst_2', .3, [('brush', 1.0), ('cast', 2.3, '가'), ('hit', 3.0)]),
        (3.0, 'p_burst_3', .3, [('brush', .9), ('cast', 2.2, '사'), ('hit', 2.9)]),
        (2.5, 'p_burst_4', .3, [('brush', .9), ('cast', 2.1, '마'), ('dodge', 2.6)]),
        (2.0, 'p_fight', 3.4, [('dodge', 3.5), ('brush', 3.9), ('cast', 5.1, '사')]),
        (1.5, 'p_hook', 2.3, [('cast', 2.45, '나'), ('hit', 3.2)]),
        (1.2, 'p_burst_1', 2.2, [('cast', 2.4, '나'), ('hit', 3.2)]),
        (1.0, 'p_burst_2', 2.6, [('hit', 3.0)]),
        (0.8, 'p_burst_3', 3.0, [('hit', 2.9), ('dodge', 3.4)]),
    ]
    cuts = [
        Cut('p_hook', 5.0, .3, 1.0, 'inkin', None, M_HOOK),
        Cut('w_reveal', 4.0, .3),
        Cut('p_village', 3.0, 1.0, 1.0, None, None, [('step', 0.0, 20.0, 'dirt')]),
        Cut('p_car', 6.0, .5, 1.0, None, None, M_CAR),
        Cut('w_capital', 3.0, .5),
        Cut('p_jeokro', 3.0, 1.0, 1.0, None, None, [('step', 0.0, 20.0, 'dirt'), ('wind', 0.0, 20.0)]),
        Cut('p_fight', 7.0, .5, 1.0, None, None, M_FIGHT),
        Cut('p_mum_long', 8.0, 1.0, 1.0, None, None, M_MUM),
        Cut('p_cheolong', 3.0, 1.0, 1.0, None, None, [('step', 0.0, 20.0, 'stone')]),
        Cut('p_cliffway', 4.0, 1.0, 1.0, None, None, [('step', 0.0, 20.0, 'wood'), ('wind', 0.0, 20.0)]),
        Cut('p_fight2', 7.0, .5, 1.0, None, None, M_FIGHT2),
        Cut('w_still', 4.0, .3, 1.0, None, None, [('wind', 0.0, 20.0)]),
        Cut('w_giant', 5.0, .3),
    ]
    for i, (dur, take, src, marks) in enumerate(bursts, 1):
        cuts.append(Cut(f'p_burst_{i}', dur, src, 1.0, None, None, marks, take))
    cuts += [
        Cut('burst_hold', 3.0, .5, 1.0, None, 'dip', [('brush', 1.2)], 'p_burst_giant'),
        Cut('black', 0.5, kind='black'),
        Cut('title', 5.0, kind='title'),
    ]
    return cuts


TL = {30: tl30, 90: tl90}


def timeline(length):
    if length not in TL:
        raise ValueError(f'length must be 30 or 90, got {length}')
    return TL[length]()


# ---------------------------------------------------------------- 대체(같은 종류만) · 필수
ALT = {
    'p_hook': ['p_hook_b'],                               # R1 대체 글자 '가' 테이크
    'p_fight2': ['p_fight'],
    'p_village': ['p_walk'],
    'p_walk': ['p_cliffway', 'p_village'],
    'p_jeokro': ['p_cliffway', 'p_walk'],
    'p_cheolong': ['p_cliffway'],
    'w_capital': ['w_reveal'],
    'p_mum_long': ['p_mum'],
    'p_mum': ['p_mum_long'],
    'p_burst_1': ['p_fight', 'p_hook'],
    'p_burst_2': ['p_fight', 'p_hook'],
    'p_burst_3': ['p_fight', 'p_hook'],
    'p_burst_4': ['p_fight', 'p_hook'],
    'p_burst_giant': ['p_fight', 'p_hook'],
}
REQUIRED = {'w_giant', 'p_hook'}                          # 대체까지 없으면 중단(훅 없는 트레일러 · 거인 없는 클라이맥스는 만들지 않는다)


class AbortTimeline(RuntimeError):
    pass


def all_takes():
    s = set()
    for n in TL:
        s |= {c.take for c in timeline(n) if c.kind == 'shot'}
    for v in ALT.values():
        s |= set(v)
    return sorted(s)


def _alt_chain(take, films):
    """ALT를 따라가며 films에 있는 첫 대체를 찾는다(p_jeokro→p_cliffway→p_walk처럼 사슬도 탄다)."""
    seen, queue = {take}, list(ALT.get(take, []))
    while queue:
        c = queue.pop(0)
        if c in seen:
            continue
        seen.add(c)
        if c in films and films[c]:
            return c
        queue.extend(ALT.get(c, []))
    return None


def _fit_window(c, length):
    """테이크 길이에 창(src + dur·speed)을 맞춘다: 넘치면 src를 뒤로 당기고, 그래도 짧으면 FREEZE 프레임 수(마지막 프레임 복제)를 돌려준다."""
    if c.need() <= length + 1e-3:
        return 0
    new_src = max(0.0, length - c.dur * c.speed - 0.1)
    if new_src != c.src:
        c.src = round(new_src, 3)
    short = c.need() - length
    return int(round(short / c.speed * FPS)) if short > 1e-3 else 0


def resolve(cuts, films):
    """films: {take: 길이 초}. 없으면 ALT → drop → 부족분을 제목 앞 마지막 컷에 더한다. (cuts, report)
    held_last로 늘어난 컷은 창을 한 번 더 맞춘다(src 당김 · FREEZE) — cut.py의 tpad는 컷 길이만큼 복제하므로 어느 경우든 프레임이 모자라지 않는다."""
    length_frames = sum(c.frames for c in cuts)
    out, used, alt, dropped, freeze = [], [], [], [], []
    for c in cuts:
        c = copy.deepcopy(c)
        if c.kind != 'shot':
            out.append(c)
            continue
        take = c.take
        if not (take in films and films[take]):
            sub = _alt_chain(take, films)
            if sub is None:
                if take in REQUIRED:
                    raise AbortTimeline(f'필수 테이크 없음: {take} — 중단')
                dropped.append({'name': c.name, 'take': take, 'dur': c.dur})
                continue
            alt.append({'name': c.name, 'take': take, 'alt': sub})
            c.take = sub
        held = _fit_window(c, float(films[c.take]))
        if held:
            freeze.append({'name': c.name, 'take': c.take, 'frames': held})
        used.append({'name': c.name, 'take': c.take, 'src': c.src, 'speed': c.speed, 'dur': c.dur})
        out.append(c)
    shortfall = length_frames - sum(c.frames for c in out)
    held_last = None
    if shortfall > 0:
        idx = max(i for i, c in enumerate(out) if c.kind == 'shot')
        last = out[idx]
        last.dur = round(last.dur + shortfall / FPS, 6)
        held_last = {'name': last.name, 'take': last.take, 'added_frames': shortfall, 'dur': last.dur}
        held = _fit_window(last, float(films[last.take]))      # 늘어난 창을 다시 검사
        freeze = [f for f in freeze if f['name'] != last.name]
        if held:
            freeze.append({'name': last.name, 'take': last.take, 'frames': held})
        for u in used:
            if u['name'] == last.name:
                u.update({'src': last.src, 'dur': last.dur})
    report = {'used': used, 'alt': alt, 'dropped': dropped, 'freeze': freeze, 'held_last': held_last,
              'frames': sum(c.frames for c in out), 'play_seconds': play_seconds(out)}
    return out, report


def validate(cuts, length, films=None):
    """(errors, warnings). errors: 프레임 합 · 1/30 배수 · src/speed. warnings: 소스 길이(films가 있을 때)."""
    errors, warnings = [], []
    total = sum(c.frames for c in cuts)
    if total != length * FPS:
        errors.append(f'프레임 합 {total} ≠ {length * FPS}')
    for c in cuts:
        if not on_grid(c.dur):
            errors.append(f'{c.name}: dur {c.dur} 는 1/30 배수가 아님')
        if c.kind == 'shot':
            if c.src < 0 or c.speed <= 0:
                errors.append(f'{c.name}: src {c.src} / speed {c.speed}')
            if films is not None:
                L = films.get(c.take)
                if not L:
                    errors.append(f'{c.name}: 테이크 {c.take} 없음')
                elif c.need() > L + 1e-3:
                    warnings.append(f'{c.name}: {c.take} {L:.2f}s < src {c.src} + {c.dur}×{c.speed} = {c.need():.2f}s (FREEZE)')
    return errors, warnings


def starts(cuts):
    t, out = 0.0, []
    for c in cuts:
        out.append(round(t, 6))
        t += c.dur
    return out


def play_seconds(cuts):
    return round(sum(c.dur for c in cuts if c.is_play), 3)


def out_time(cut, t0, s):
    """테이크 초 s → 출력 초."""
    return t0 + (s - cut.src) / cut.speed


def describe(cuts, length=None):
    lines = []
    for t0, c in zip(starts(cuts), cuts):
        src = f'{c.take} @{c.src:.2f}s ×{c.speed:g}' if c.kind == 'shot' else c.kind
        fx = ' '.join(x for x in (c.fx_in and 'in:' + c.fx_in, c.fx_out and 'out:' + c.fx_out) if x)
        lines.append(f'{t0:6.2f}  {c.name:12s} {c.dur:5.2f}s {c.frames:4d}f  {src:28s} {fx}')
    total = sum(c.frames for c in cuts)
    tail = f'합 {total} f = {total / FPS:.3f} s · 플레이 {play_seconds(cuts):.1f} s'
    if length:
        tail += f' (AC-4 ≥ {PLAY_MIN[length]:.0f})'
    lines.append(tail)
    return '\n'.join(lines)


def selftest():
    """resolve 규칙 단위시험(검토 지적 1 · 3 · 4): python timeline.py test"""
    full = {t: 20.0 for t in all_takes()}

    def case(name, cond, detail=''):
        print(f'{"ok  " if cond else "FAIL"} {name} {detail}')
        if not cond:
            raise AssertionError(name)

    # 1. 30초판 로고 직전 = 거인 테이크 · 900 f · 플레이 18.0
    c30 = timeline(30)
    shots = [c for c in c30 if c.kind == 'shot']
    case('tl30 last shot is p_burst_giant', shots[-1].take == 'p_burst_giant' and shots[-1].fx_out == 'dip', f'{shots[-1].name}')
    case('tl30 900 f · play 18.0', sum(c.frames for c in c30) == 900 and play_seconds(c30) == 18.0)
    # 4. p_hook 없음 + p_hook_b 있음 → ALT(훅 · inkin 유지), 둘 다 없음 → 중단
    f = dict(full)
    f.pop('p_hook')
    out, rep = resolve(timeline(30), f)
    case('p_hook → p_hook_b ALT', out[0].take == 'p_hook_b' and out[0].fx_in == 'inkin' and not rep['dropped'], f"{rep['alt']}")
    f.pop('p_hook_b')
    try:
        resolve(timeline(30), f)
        case('p_hook+p_hook_b missing aborts', False)
    except AbortTimeline as e:
        case('p_hook+p_hook_b missing aborts', True, str(e))
    f = dict(full)
    f.pop('p_mum')
    out, rep = resolve(timeline(30), f)
    case('p_mum → p_mum_long ALT', any(c.name == 'p_mum' and c.take == 'p_mum_long' for c in out) and not rep['dropped'])
    f = dict(full)
    f.pop('p_walk')
    out, rep = resolve(timeline(30), f)
    case('p_walk → p_cliffway ALT', any(c.name == 'p_walk' and c.take == 'p_cliffway' for c in out) and not rep['dropped'])
    # 3. held_last 뒤 창 재검사: 90초판 p_cheolong · p_cliffway 없음 + p_burst_giant 8 s → burst_hold 10.0 s, src 0, FREEZE 60 f
    f = dict(full)
    f.pop('p_cheolong')
    f.pop('p_cliffway')
    f['p_burst_giant'] = 8.0
    out, rep = resolve(timeline(90), f)
    bh = next(c for c in out if c.name == 'burst_hold')
    fz = next((x for x in rep['freeze'] if x['name'] == 'burst_hold'), None)
    used = next(u for u in rep['used'] if u['name'] == 'burst_hold')
    case('held_last re-fit: burst_hold 10.0 s · src 0 · FREEZE 60 f', bh.dur == 10.0 and bh.src == 0.0 and fz and fz['frames'] == 60
         and used['dur'] == 10.0 and used['src'] == 0.0 and sum(c.frames for c in out) == 2700,
         f"dur {bh.dur} src {bh.src} freeze {fz} held_last {rep['held_last']}")
    case('resolve keeps 2700 f with 2 drops', rep['frames'] == 2700 and [d['name'] for d in rep['dropped']] == ['p_cheolong', 'p_cliffway'])
    print('timeline selftest PASS')


if __name__ == '__main__':
    import sys
    if len(sys.argv) > 1 and sys.argv[1] == 'test':
        selftest()
        sys.exit(0)
    for n in (30, 90):
        if len(sys.argv) > 1 and sys.argv[1] not in (str(n),):
            continue
        cuts = timeline(n)
        e, w = validate(cuts, n)
        print(f'== TL{n}')
        print(describe(cuts, n))
        print('errors:', e or 'none')
    print('takes:', ', '.join(all_takes()))
