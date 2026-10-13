"""#308 PeopleLite: Unity .anim(YAML, Generic 변환 곡선) 읽기 + 헤르미트 평가. 순수 파이썬(시스템 Python · Blender 공용).
parse(path) -> dict(name, length, rate, curves={path: {'rot'|'pos'|'scl': [(t, value, inSlope, outSlope), ...]}})
sample(keys, t) -> 값 튜플 (쿼터니언은 정규화). Unity 는 성분별 3차 헤르미트로 평가한다."""
import re, math
_V = re.compile(r'([xyzw]): ([-+0-9.eE]+|nan|inf|-inf)')
_KEY = re.compile(r'^[A-Za-z_]+:( |$)')
_SEC = {'m_RotationCurves': 'rot', 'm_PositionCurves': 'pos', 'm_ScaleCurves': 'scl', 'm_EulerCurves': 'eul'}


def _vec(s):
    return tuple(float(v) for _, v in _V.findall(s))


def parse(path):
    name, rate, stop = '', 30.0, 0.0; curves = {}; sec = None; keys = None; key = None; pend = None
    with open(path, encoding='utf-8') as f:
        for raw in f:
            line = raw.rstrip('\n'); s = line.strip()
            if pend is not None:      # 긴 경로는 YAML 이 다음 줄로 접는다(이름에 빈칸이 있는 치마 뼈)
                if line.startswith('      ') and not _KEY.match(s) and not s.startswith('- '): pend[0] += ' ' + s; continue
                curves.setdefault(pend[0], {})[pend[1]] = pend[2]; pend = None
            if line.startswith('  m_'):
                if pend is not None: curves.setdefault(pend[0], {})[pend[1]] = pend[2]; pend = None
                head = s.split(':')[0]
                sec = _SEC.get(head)
                if head == 'm_Name': name = s.split(':', 1)[1].strip()
                if head == 'm_SampleRate': rate = float(s.split(':', 1)[1])
                if sec is None: keys = None
                continue
            if s.startswith('m_StopTime:'): stop = max(stop, float(s.split(':')[1]))
            if sec is None: continue
            if s == '- curve:': keys = []; key = None; continue
            if keys is None: continue
            if s.startswith('time:') or s.startswith('- time:'): key = [float(s.split(':')[1]), None, None, None]; keys.append(key); continue
            if key is not None:
                if s.startswith('value:'): key[1] = _vec(s)
                elif s.startswith('inSlope:'): key[2] = _vec(s)
                elif s.startswith('outSlope:'): key[3] = _vec(s)
            if s.startswith('path:'):
                pend = [s.split(':', 1)[1].strip(), sec, [tuple(k) for k in keys if k[1] is not None]]; keys = None; key = None
    length = stop or max((k[-1][0] for c in curves.values() for k in c.values() if k), default=0.0)
    return dict(name=name, length=length, rate=rate, curves=curves)


def sample(keys, t, quat=False):
    if not keys: return None
    if t <= keys[0][0]: v = keys[0][1]
    elif t >= keys[-1][0]: v = keys[-1][1]
    else:
        lo, hi = 0, len(keys) - 1
        while hi - lo > 1:
            m = (lo + hi) // 2
            if keys[m][0] <= t: lo = m
            else: hi = m
        a, b = keys[lo], keys[hi]; dt = b[0] - a[0]; u = (t - a[0]) / dt if dt > 0 else 0.0
        h00 = 2 * u**3 - 3 * u**2 + 1; h10 = u**3 - 2 * u**2 + u; h01 = -2 * u**3 + 3 * u**2; h11 = u**3 - u**2
        def fin(x): return x if math.isfinite(x) else 0.0
        v = tuple(h00 * a[1][i] + h10 * dt * fin(a[3][i]) + h01 * b[1][i] + h11 * dt * fin(b[2][i]) for i in range(len(a[1])))
    if quat:
        n = math.sqrt(sum(c * c for c in v)) or 1.0; v = tuple(c / n for c in v)
    return v


if __name__ == '__main__':
    import sys
    for p in sys.argv[1:]:
        a = parse(p); print(a['name'], 'len %.3f rate %g paths %d' % (a['length'], a['rate'], len(a['curves'])))
        rows = []
        for path, c in a['curves'].items():
            rot = c.get('rot', []); pos = c.get('pos', []); ang = 0.0
            if rot:
                q0 = sample(rot, 0, True)
                for i in range(33):
                    q = sample(rot, a['length'] * i / 32, True); d = abs(sum(x * y for x, y in zip(q, q0))); ang = max(ang, 2 * math.degrees(math.acos(min(1, d))))
            pr = 0.0
            if pos:
                ps = [sample(pos, a['length'] * i / 32) for i in range(33)]; pr = max(max(p[i] for p in ps) - min(p[i] for p in ps) for i in range(3))
            rows.append((path.split('/')[-1], ang, pr, len(rot), sorted(c.keys())))
        if '-v' in sys.argv or True:
            print('  ' + ' | '.join('%s %.0f°%s' % (n, a_, (' p%.3f' % p_) if p_ > .002 else '') for n, a_, p_, k, s in rows))
