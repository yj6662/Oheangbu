#!/usr/bin/env python3
"""작업판 사무실 (AgentBoard Office, 2026-10-29) — board.jsonl을 게임 개발사 사무실 화면으로 본다.

사용자(2026-10-29): "작업판 프로그램을 더 발전시킬 수 있나? 비주얼 좀 신경써서, 게임 개발사처럼 만들고, 부서별 자리들을 만들어서,
각자 어떤 작업 진행 중인지 각 직원들의 생각풍선으로 보이도록? 또 부가 기능 같은 거 넣고."
이어서: "api 연결한 meshy 같은 에이전트도 여기에서 넣을 수 있나? 그리고 저 프로그램에서 직접 명령을 할 수도 있을까?"
 → 실제 일꾼 실행 · 자동 배정(Haiku) · Meshy는 확인창 거쳐 허용.

기록 · 명령은 그대로 board.py(start/note/done/fail/run)다. 로컬 서버가 board.jsonl을 접어서 내주고,
office.html이 2초마다 받아 부서 방 · 자리 · 생각풍선 · 게시판 · 성과판 · 타임라인으로 그린다.

  python Tools/AgentBoard/office.py                       # 사무실 창(127.0.0.1 서버 + Edge 앱 창)
  python Tools/AgentBoard/office.py --no-open [--port N]  # 서버만(주소를 브라우저에 직접 넣는다)
        # 켤 때 먼저 켜져 있던 사무실 서버를 끈다(옛 코드 서버에 새 창이 붙지 않게). 남기려면 --keep-old
  python Tools/AgentBoard/office.py snapshot [out.html]   # 지금 모습을 데이터 내장 HTML 한 장으로(메일 · 기록용, 지시 불가)
  python Tools/AgentBoard/office.py selftest              # 창 없이 접기 · API · 토큰 · 스냅샷 검사(일꾼은 띄우지 않음)
  python Tools/AgentBoard/office.py usage                 # Claude · ChatGPT(Codex) 사용 한도를 터미널에
  <get_usage 결과 JSON> | python Tools/AgentBoard/office.py usage-set-claude   # Claude 한도 캐시 갱신(데스크톱 앱 세션이)
  python Tools/AgentBoard/office.py statusline            # Claude Code CLI 상태줄 명령으로 쓰면 한 줄 표시 + 캐시 자동 갱신

사용 한도(사용자 2026-10-29: "chatgpt랑 너의 사용한도 체크하는 기능도"):
- ChatGPT(Codex): ~/.codex/sessions 최신 rollout의 rate_limits(5시간 · 주간 사용률 · 초기화 시각)를 그대로 읽는다. 마지막 codex 실행 시점 값.
- Claude: 로그인 토큰을 직접 쓰지 않는다(사용량 서버 직접 호출은 권한 검사에서 막혔다). 데스크톱 앱 get_usage 결과나
  CLI 상태줄 입력(rate_limits)을 Art/AgentBoard/usage_claude.json에 받아 두고, 사무실은 '몇 분 전 값'과 함께 보여 준다.

업무 지시: 위쪽 '업무 지시'에 글을 쓰면 Haiku가 배정표를 보고 맡을 직원을 고르고(바꿀 수 있음), '지시하기'를 누르면
office_worker.py가 그 모델을 실제로 돌린다(claude -p · codex exec · Meshy · Recraft · staff.json의 command).
기본은 읽기 전용, '파일 수정 허용'을 켜야 고친다. 유료(Meshy · Recraft)는 확인창 + 한 달 상한(staff.json month_cap).
POST는 서버를 띄울 때 만든 토큰 + 같은 출처만 받는다(다른 웹페이지가 127.0.0.1로 일을 시키거나 크레딧을 쓰지 못하게).
API 에이전트 추가 = staff.json에 한 줄(runner.kind=command면 argv를 실행하고 지시문을 stdin으로 넘긴다).
"""
import json, os, re, secrets, subprocess, sys, threading, time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import parse_qs, urlparse

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import board  # noqa: E402  (같은 폴더의 기록 · 접기 · 배정표)
import office_worker as worker  # noqa: E402  (원장 · 잔액 · 키 · 세션 변수 빼기)

PAGE = HERE / 'office.html'
STAFF_JSON = HERE / 'staff.json'
JOBS = board.BOARD.parent / 'jobs'
RESULTS = board.BOARD.parent / 'results'
TOKEN = secrets.token_urlsafe(18)
VERSION = '2026-10-29.4'          # 화면은 이 값이 서버와 다르거나 없으면 '다시 켜라' 띠를 띄운다
STARTED = time.time()
CODE_FILES = [HERE / 'office.py', HERE / 'office_worker.py', HERE / 'staff.json']
USAGE_CLAUDE = board.BOARD.parent / 'usage_claude.json'
CODEX_SESSIONS = Path.home() / '.codex' / 'sessions'
LOOKBACK = 36 * 3600           # 이보다 오래 끝난 작업은 사무실에 안 그린다(진행 중은 항상)

# 부서 — 배정표(board.ROSTER)의 역할을 개발사 부서로. (키, 방 이름, 영문 팻말, 바닥, 책상 소품, 소속 모델)
DEPTS = [
    ('direct', '기획실',   'DIRECTOR',    'carpet', 'plant',   ['opus']),
    ('review', '심사실',   'REVIEW',      'carpet2', 'books',  ['fable']),
    ('art',    '아트팀',   'CONCEPT ART', 'wood2',  'palette', ['gpt-6-astra']),
    ('dev',    '개발팀',   'ENGINEERING', 'wood',   'duck',    ['sonnet', 'gpt-6.1-sol']),
    ('qa',     'QA팀',     'QA',          'tile',   'bug',     ['haiku']),
    ('farm',   '렌더팜',   'RENDER FARM', 'metal',  '',        ['tool']),
    ('vendor', '외주 협력사', 'API VENDORS', 'guest', 'plant',  []),      # staff.json의 dept=vendor 직원이 앉는다
]
STAFF = {   # 명패: 짧은 이름 · 직함
    'opus': ('Opus', '총괄 PD'), 'fable': ('Fable', '수석 심사역'), 'sonnet': ('Sonnet', '시니어 프로그래머'),
    'haiku': ('Haiku', 'QA 엔지니어'), 'gpt-6-astra': ('Astra', '컨셉 아티스트'), 'gpt-6.1-sol': ('Sol', '프로토타이퍼'),
    'tool': ('렌더봇', '렌더 서버'),
}
# 지시를 받을 수 있는 내장 직원 — 실제로 무엇을 돌리나
RUNNERS = {
    'opus': {'kind': 'claude', 'model': 'claude-opus-5-5'}, 'fable': {'kind': 'claude', 'model': 'claude-fable-5-1'},
    'sonnet': {'kind': 'claude', 'model': 'claude-sonnet-5-5'}, 'haiku': {'kind': 'claude', 'model': 'claude-haiku-5-5'},
    'gpt-6-astra': {'kind': 'codex', 'model': 'gpt-6-astra'}, 'gpt-6.1-sol': {'kind': 'codex', 'model': 'gpt-6.1-sol'},
}
PAID = ('meshy', 'recraft')

_procs = {'t': 0.0, 'v': {}}


def procs():
    """tasklist는 느리니 4초 동안 재사용."""
    if time.time() - _procs['t'] > 4:
        _procs.update(t=time.time(), v=board.untracked_procs())
    return _procs['v']


def log_tail(path, n):
    try:
        return [l[:240] for l in Path(path).read_text(encoding='utf-8', errors='replace').splitlines()[-n:]]
    except (OSError, TypeError):
        return []


def row_out(r, notes=12, log=0):
    o = {k: r.get(k) for k in ('id', 'model', 'role', 'task', 'state', 'start', 'end', 'pid', 'log')}
    o['lane'] = r.get('lane') or getattr(board, 'DEFAULT_LANE', 'VFX')
    o['notes'] = [list(x) for x in (r['notes'][-notes:] if notes else r['notes'])]
    o['note_count'] = len(r['notes'])
    o['last'] = max([r['start']] + [t for t, _ in r['notes']])
    if log:
        o['log_tail'] = log_tail(r['log'], log) if r['log'] else []
        res = RESULTS / (r['id'] + '.md')
        o['result'] = res.read_text(encoding='utf-8', errors='replace')[:6000] if res.exists() else ''
    return o


# ------------------------------------------------------------------ 직원 명부
def extra_staff():
    try:
        return json.loads(STAFF_JSON.read_text(encoding='utf-8')).get('staff', [])
    except (OSError, ValueError):
        return []


def roster():
    out = [{'key': k, 'name': n, 'role': ro, 'what': w, 'color': board.COLOR.get(k, '#b8b8b8'), 'short': STAFF.get(k, (k, ro))[0],
            'title': STAFF.get(k, (k, ro))[1], 'kind': RUNNERS.get(k, {}).get('kind', '')} for k, n, ro, w in board.ROSTER]
    for x in extra_staff():
        run = x.get('runner') or {}
        out.append({'key': x['key'], 'name': x.get('name', x['key']), 'role': x.get('role', ''), 'what': x.get('what', ''),
                    'color': x.get('color', '#b8b8b8'), 'short': x.get('short', x['key']), 'title': x.get('title', ''),
                    'kind': run.get('kind', ''), 'paid': run.get('kind') in PAID, 'estimate': run.get('estimate'), 'unit': x.get('unit', '')})
    return out


def depts():
    extra = extra_staff()
    return [{'key': k, 'name': n, 'en': en, 'floor': fl, 'item': it,
             'models': ms + [x['key'] for x in extra if x.get('dept', 'vendor') == k]} for k, n, en, fl, it, ms in DEPTS]


def runner_of(model):
    if model in RUNNERS:
        return dict(RUNNERS[model])
    x = next((x for x in extra_staff() if x['key'] == model), None)
    return dict(x['runner']) if x and x.get('runner') else None


# ------------------------------------------------------------------ 사용 한도
_usage = {'t': 0.0, 'v': None}
WIN_KO = {300: '5시간', 10080: '주간'}


def _iso(ts):
    return time.strftime('%Y-%m-%dT%H:%M:%S', time.localtime(ts))


def _parse_time(v):
    """ISO 문자열 · 초 단위 숫자 → epoch 초."""
    if v is None or v == '':
        return None
    if isinstance(v, (int, float)):
        return float(v) / (1000 if v > 1e11 else 1)
    try:
        from datetime import datetime
        return datetime.fromisoformat(str(v).replace('Z', '+00:00')).timestamp()
    except ValueError:
        return None


def codex_usage():
    """가장 최근 codex 세션 기록의 마지막 rate_limits."""
    try:
        files = sorted(CODEX_SESSIONS.glob('*/*/*/rollout-*.jsonl'), key=lambda f: f.stat().st_mtime)[-4:]
    except OSError:
        files = []
    for f in reversed(files):
        try:
            with open(f, 'rb') as fh:
                fh.seek(max(0, f.stat().st_size - 400_000))
                lines = fh.read().decode('utf-8', 'replace').splitlines()
        except OSError:
            continue
        for line in reversed(lines):
            if '"rate_limits"' not in line:
                continue
            try:
                e = json.loads(line)
            except ValueError:
                continue
            rl = (e.get('payload') or {}).get('rate_limits') or e.get('rate_limits')
            if not rl:
                continue
            wins = []
            for k in ('primary', 'secondary'):
                w = rl.get(k)
                if w and w.get('used_percent') is not None:
                    wins.append({'label': WIN_KO.get(w.get('window_minutes'), '%s분' % w.get('window_minutes')), 'pct': float(w['used_percent']),
                                 'resets_at': _parse_time(w.get('resets_at'))})
            cr = rl.get('credits') or {}
            return {'who': 'ChatGPT · Codex', 'windows': wins, 'asof': _parse_time(e.get('timestamp')) or f.stat().st_mtime,
                    'source': 'codex 세션 기록', 'extra': ('크레딧 %.0f' % float(cr['balance'])) if cr.get('balance') else ''}
    return {'who': 'ChatGPT · Codex', 'windows': [], 'asof': None, 'source': 'codex 세션 기록 없음', 'extra': ''}


def claude_from_any(d):
    """get_usage 결과(plan.windows) · 상태줄 입력(rate_limits.five_hour) 둘 다 받는다."""
    wins = []
    plan = d.get('plan') if isinstance(d.get('plan'), dict) else d
    for w in plan.get('windows') or []:
        lab = str(w.get('label', ''))
        ko = '5시간' if '5-hour' in lab else ('주간' if lab.endswith('all models') else lab.replace('Weekly · ', '주간 · '))
        wins.append({'label': ko, 'pct': float(w.get('percentUsed') or 0), 'resets_at': _parse_time(w.get('resetsAt'))})
    rl = d.get('rate_limits') or {}
    for k, ko in (('five_hour', '5시간'), ('seven_day', '주간')):
        w = rl.get(k)
        if isinstance(w, dict) and w.get('used_percentage') is not None:
            wins.append({'label': ko, 'pct': float(w['used_percentage']), 'resets_at': _parse_time(w.get('resets_at'))})
    return wins


def set_claude_usage(d, source):
    wins = claude_from_any(d)
    if not wins:
        raise SystemExit('한도 창을 못 찾았다(get_usage 결과 또는 상태줄 입력 JSON이어야 한다)')
    USAGE_CLAUDE.parent.mkdir(parents=True, exist_ok=True)
    plan = (d.get('plan') or {}).get('plan') if isinstance(d.get('plan'), dict) else None
    USAGE_CLAUDE.write_text(json.dumps({'asof': time.time(), 'source': source, 'plan': plan, 'windows': wins}, ensure_ascii=False, indent=1), encoding='utf-8')
    return wins


def claude_usage():
    try:
        d = json.loads(USAGE_CLAUDE.read_text(encoding='utf-8'))
    except (OSError, ValueError):
        return {'who': 'Claude', 'windows': [], 'asof': None, 'source': '캐시 없음 — 세션에서 usage-set-claude 또는 상태줄로 채운다', 'extra': ''}
    return {'who': 'Claude' + (' ' + d['plan'] if d.get('plan') else ''), 'windows': d.get('windows', []), 'asof': d.get('asof'),
            'source': d.get('source', ''), 'extra': ''}


def usage():
    if time.time() - _usage['t'] > 10:
        _usage.update(t=time.time(), v=[claude_usage(), codex_usage()])
    return _usage['v']


def state(path=board.BOARD, detail=False):
    evs = board.read_events(path)
    rows = board.fold(evs)
    t = board.now()
    keep = [r for r in rows if r['state'] == 'run' or (r['end'] or t) > t - LOOKBACK]
    by_id = {r['id']: r for r in rows}
    feed = []
    for e in evs[-200:]:
        r = by_id.get(e.get('id'))
        if not r or e.get('ts', 0) < t - LOOKBACK:
            continue
        feed.append({'ts': e.get('ts'), 'ev': e.get('ev'), 'id': r['id'], 'model': r['model'], 'lane': r.get('lane') or 'VFX',
                     'text': (e.get('task') if e.get('ev') == 'start' else e.get('text')) or ''})
    feed.sort(key=lambda f: -f['ts'])
    return {
        'now': t, 'board': str(path), 'snapshot': False,
        'roster': roster(), 'depts': depts(),
        'rows': [row_out(r, notes=0 if detail else 12, log=40 if detail else 0) for r in keep],
        'feed': feed[:80],
        'procs': procs(),
        'usage': usage(),
        'version': VERSION,
        'code_changed': any(f.exists() and f.stat().st_mtime > STARTED + 1 for f in CODE_FILES),
    }


def page(snapshot=None):
    html = PAGE.read_text(encoding='utf-8')
    data = 'null' if snapshot is None else json.dumps(snapshot, ensure_ascii=False).replace('</', '<\\/')
    html = html.replace('/*__SNAPSHOT__*/null', data, 1)
    html = html.replace('/*__VERSION__*/""', json.dumps(VERSION), 1)
    return html.replace('/*__TOKEN__*/""', '""' if snapshot is not None else json.dumps(TOKEN), 1)


# ------------------------------------------------------------------ 업무 지시
ROUTE_HINTS = [   # Haiku가 답을 못 줄 때만 쓰는 열쇠말 배정
    ('meshy', r'3[dD]|모델링|메시|meshy|glb|입체'), ('recraft', r'아이콘|svg|SVG|벡터|recraft|로고'),
    ('gpt-6-astra', r'시안|그림|이미지|컨셉|그려|일러스트'), ('fable', r'채점|판정|심사|평가|점수|토론'),
    ('haiku', r'점검|확인|세어|목록|로그|찾아|몇 개|있나'), ('sonnet', r'구현|만들|고쳐|수정|코드|셰이더|스크립트|버그'),
]


def route_keyword(text):
    model = next((k for k, pat in ROUTE_HINTS if re.search(pat, text)), 'sonnet')
    return {'model': model, 'title': (text.strip().splitlines() or [''])[0][:40], 'reason': '열쇠말로 고름(Haiku 배정을 못 받음)', 'by': 'keyword'}


def limits_hint():
    """한도가 빠듯한 쪽을 배정 담당에게 알려 준다(85 % 이상)."""
    tight = ['%s %s %.0f%%' % (u['who'], w['label'], w['pct']) for u in usage() for w in u['windows'] if w['pct'] >= 85]
    return ('사용 한도가 빠듯하다: %s — 가능하면 다른 쪽 직원에게 맡긴다.\n' % ', '.join(tight)) if tight else ''


def route(text):
    """지시문 → {model, title, reason, by}. Haiku에게 배정표를 주고 고르게 하고, 실패하면 열쇠말로."""
    staff = [r for r in roster() if r['kind']]
    menu = '\n'.join('- %s: %s %s — %s%s' % (r['key'], r['short'], r['title'], r['what'], ' (유료)' if r.get('paid') else '') for r in staff)
    prompt = ('게임 개발사 작업판의 업무 배정 담당이다. 아래 직원 중 이 지시를 가장 잘할 한 명을 고르고, 작업판에 보일 짧은 제목(30자 이내)을 붙여라.\n'
              '유료 직원(meshy · recraft)은 지시가 분명히 3D 모델 · 2D 그림 생성을 원할 때만 고른다. 여러 단계가 필요하면 첫 단계를 맡을 직원을 고른다.\n'
              '%s직원:\n%s\n\n지시:\n%s\n\nJSON 한 줄로만 답하라: {"model": "키", "title": "제목", "reason": "한 문장 이유"}' % (limits_hint(), menu, text))
    try:
        p = subprocess.run([worker.exe('claude'), '-p', '--model', 'claude-haiku-5-5', '--output-format', 'json',
                            '--disallowedTools', 'Bash', 'Edit', 'Write', 'Read', 'Grep', 'Glob', 'WebSearch', 'WebFetch'],
                           input=prompt.encode('utf-8'), capture_output=True, timeout=90, cwd=board.REPO, env=worker.clean_env())
        res = json.loads(p.stdout.decode('utf-8', 'replace')).get('result', '')
        m = json.loads(re.search(r'\{.*\}', res, re.S).group(0))
        if m.get('model') in {r['key'] for r in staff}:
            return {'model': m['model'], 'title': str(m.get('title') or text)[:40], 'reason': str(m.get('reason', '')), 'by': 'haiku'}
    except Exception:
        pass
    return route_keyword(text)


def order(body, launch=True):
    """일 파일을 만들고 일꾼을 띄운다. 유료는 confirm=true가 와야 하고 한 달 상한을 넘으면 거절."""
    model, text = body.get('model', ''), str(body.get('text', '')).strip()
    run = runner_of(model)
    if not text or not run:
        return 400, {'error': '지시 글이 없거나 지시를 받을 수 없는 직원(%s)' % model}
    if run['kind'] in PAID:
        x = next((x for x in extra_staff() if x['key'] == model), {})
        est, cap, spent = run.get('estimate', 0), x.get('month_cap', 0), worker.month_spent(run['kind'])
        info = {'need_confirm': True, 'vendor': run['kind'], 'estimate': est, 'month_spent': spent, 'month_cap': cap,
                'balance': worker.balance(run['kind']), 'unit': x.get('unit', ''), 'prompt': text[:800]}
        if spent + est > cap:
            return 409, dict(info, error='한 달 상한 초과: %s + %s > %s (사무실 원장 기준)' % (spent, est, cap))
        if not body.get('confirm'):
            return 200, info
    jid = 'o_%s_%s' % (time.strftime('%m%d_%H%M%S'), secrets.token_hex(2))
    job = {'id': jid, 'model': model, 'role': body.get('role') or next((r['role'] for r in roster() if r['key'] == model), ''),
           'task': str(body.get('title') or text.splitlines()[0])[:80], 'lane': str(body.get('lane') or '').strip()[:20],
           'prompt': text, 'write': bool(body.get('write')) and run['kind'] in ('claude', 'codex'), 'runner': run}
    JOBS.mkdir(parents=True, exist_ok=True)
    jf = JOBS / (jid + '.json')
    jf.write_text(json.dumps(job, ensure_ascii=False, indent=1), encoding='utf-8')
    if launch:
        flags = 0x08000000 if os.name == 'nt' else 0          # CREATE_NO_WINDOW — 콘솔 창을 띄우지 않는다
        subprocess.Popen([sys.executable, str(HERE / 'office_worker.py'), str(jf)], cwd=board.REPO, creationflags=flags,
                         stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    return 200, {'ok': True, 'id': jid, 'job': str(jf)}


def append_to(path, ev):
    """board.append는 실제 기록 파일에만 쓴다 — selftest가 임시 파일을 쓰도록 경로를 받는다."""
    ev.setdefault('ts', board.now())
    with open(path, 'a', encoding='utf-8') as f:
        f.write(json.dumps(ev, ensure_ascii=False) + '\n')


def stop(body, path=board.BOARD):
    r = {x['id']: x for x in board.fold(board.read_events(path))}.get(body.get('id'))
    if not r or r['state'] != 'run':
        return 400, {'error': '진행 중인 작업이 아니다'}
    if r['pid']:
        subprocess.run(['taskkill', '/PID', str(r['pid']), '/T', '/F'] if os.name == 'nt' else ['kill', str(r['pid'])], capture_output=True)
    append_to(path, {'ev': 'fail', 'id': r['id'], 'text': '사무실에서 중지' + ('' if r['pid'] else ' (pid 없음 — 기록만 닫음)')})
    return 200, {'ok': True}


def note(body, path=board.BOARD):
    if not body.get('id') or not str(body.get('text', '')).strip():
        return 400, {'error': 'id · text 필요'}
    append_to(path, {'ev': 'note', 'id': body['id'], 'text': ('[사장님] ' + str(body['text']).strip())[:300]})
    return 200, {'ok': True}


# ------------------------------------------------------------------ 서버
def make_handler(path):
    class H(BaseHTTPRequestHandler):
        def _send(self, body, ctype, code=200):
            b = body.encode('utf-8')
            self.send_response(code)
            self.send_header('Content-Type', ctype + '; charset=utf-8')
            self.send_header('Cache-Control', 'no-store')
            self.send_header('Content-Length', str(len(b)))
            self.end_headers()
            self.wfile.write(b)

        def do_GET(self):
            u = urlparse(self.path)
            try:
                if u.path in ('/', '/index.html'):
                    self._send(page(), 'text/html')
                elif u.path == '/api/state':
                    self._send(json.dumps(state(path), ensure_ascii=False), 'application/json')
                elif u.path == '/api/task':
                    i = parse_qs(u.query).get('id', [''])[0]
                    r = {x['id']: x for x in board.fold(board.read_events(path))}.get(i)
                    self._send(json.dumps(row_out(r, notes=0, log=40) if r else None, ensure_ascii=False), 'application/json')
                else:
                    self.send_error(404)
            except (ConnectionError, OSError):
                pass

        def do_POST(self):
            port = self.server.server_address[1]
            origin = self.headers.get('Origin')
            if self.headers.get('X-Office-Token') != TOKEN or (origin and origin not in ('http://127.0.0.1:%d' % port, 'http://localhost:%d' % port)):
                self.send_error(403); return
            fn = {'/api/route': lambda b: (200, route(str(b.get('text', '')))), '/api/order': order,
                  '/api/stop': lambda b: stop(b, path), '/api/note': lambda b: note(b, path)}.get(urlparse(self.path).path)
            if not fn:
                self.send_error(404); return
            try:
                body = json.loads(self.rfile.read(int(self.headers.get('Content-Length') or 0)) or b'{}')
                code, out = fn(body)
                self._send(json.dumps(out, ensure_ascii=False), 'application/json', code)
            except ValueError:
                self._send('{"error": "bad json"}', 'application/json', 400)
            except (ConnectionError, OSError):
                pass

        def log_message(self, *a):
            pass
    return H


def serve(port=8765, path=board.BOARD):
    for p in range(port, port + 12):
        try:
            return ThreadingHTTPServer(('127.0.0.1', p), make_handler(path))
        except OSError:
            continue
    raise SystemExit('빈 포트가 없다(%d–%d)' % (port, port + 11))


def open_window(url):
    """Edge 앱 창(주소창 없는 창)으로, 없으면 기본 브라우저로."""
    for exe in (r'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe', r'C:\Program Files\Microsoft\Edge\Application\msedge.exe'):
        if os.path.exists(exe):
            subprocess.Popen([exe, '--app=' + url, '--window-size=1500,920'])
            return
    import webbrowser
    webbrowser.open(url)


def stop_old_servers():
    """먼저 켜 둔 사무실 서버(옛 코드일 수 있다)를 끈다 — 같은 포트를 쥐고 있으면 새 창이 옛 서버에 붙는다. 일꾼(office_worker)은 건드리지 않는다."""
    if os.name != 'nt':
        return []
    ps = ("Get-CimInstance Win32_Process -Filter \"Name like 'python%%'\" | Where-Object { $_.CommandLine -match 'office\\.py' "
          "-and $_.CommandLine -notmatch 'office_worker|selftest|snapshot|usage|statusline' -and $_.ProcessId -ne %d } | "
          "ForEach-Object { Stop-Process -Id $_.ProcessId -Force; $_.ProcessId }" % os.getpid())
    try:
        out = subprocess.run(['powershell', '-NoProfile', '-Command', ps], capture_output=True, text=True, timeout=20).stdout
    except Exception:
        return []
    return [x for x in out.split() if x.isdigit()]


def cmd_serve(a):
    port = int(a[a.index('--port') + 1]) if '--port' in a else 8765
    if '--keep-old' not in a:
        old = stop_old_servers()
        if old:
            print('먼저 켜져 있던 사무실 서버를 껐다:', ', '.join(old), '(그 창들은 닫아도 된다)')
            time.sleep(0.5)
    srv = serve(port)
    url = 'http://127.0.0.1:%d/' % srv.server_address[1]
    print('작업판 사무실:', url, '  (기록: %s · 끝내려면 Ctrl+C)' % board.BOARD)
    if '--no-open' not in a:
        threading.Timer(0.4, open_window, [url]).start()
    try:
        srv.serve_forever()
    except KeyboardInterrupt:
        pass


def cmd_snapshot(a):
    out = Path(a[0]) if a else board.BOARD.parent / ('office_%s.html' % time.strftime('%Y%m%d_%H%M'))
    s = state(detail=True)
    s['snapshot'] = True
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(page(s), encoding='utf-8')
    print('스냅샷:', out)


def cmd_selftest(_):
    import tempfile, urllib.error, urllib.request
    p = Path(tempfile.mkdtemp()) / 'b.jsonl'
    t0 = board.now() - 1800
    evs = [{'ev': 'start', 'id': 'a', 'model': 'sonnet', 'role': '구현', 'task': '시험 구현', 'ts': t0},
           {'ev': 'note', 'id': 'a', 'text': '컴파일 통과', 'ts': t0 + 60},
           {'ev': 'done', 'id': 'a', 'text': '끝', 'ts': t0 + 120},
           {'ev': 'start', 'id': 'b', 'lane': '맵', 'model': 'haiku', 'role': '점검', 'task': '점검', 'pid': os.getpid(), 'ts': t0 + 10},
           {'ev': 'start', 'id': 'old', 'model': 'fable', 'task': '아주 옛날', 'ts': t0 - 3 * 86400},
           {'ev': 'done', 'id': 'old', 'ts': t0 - 3 * 86400 + 5}]
    p.write_text('\n'.join(json.dumps(e, ensure_ascii=False) for e in evs) + '\n', encoding='utf-8')
    s = state(p)
    ids = {r['id']: r for r in s['rows']}
    assert set(ids) == {'a', 'b'}, ids.keys()                       # 오래전에 끝난 'old'는 빠진다
    assert ids['a']['state'] == 'done' and ids['a']['last'] == t0 + 120, ids['a']
    assert ids['b']['lane'] == '맵' and ids['a']['lane'] == 'VFX', (ids['a']['lane'], ids['b']['lane'])
    assert [(f['id'], f['ev']) for f in s['feed']] == [('a', 'done'), ('a', 'note'), ('b', 'start'), ('a', 'start')], s['feed']  # 최신순, 옛 기록 제외
    keys = {r['key'] for r in s['roster']}
    assert {'meshy', 'recraft'} <= keys and 'meshy' in next(d for d in s['depts'] if d['key'] == 'vendor')['models'], 'staff.json 직원'
    assert runner_of('haiku')['model'] == 'claude-haiku-5-5' and runner_of('meshy')['kind'] == 'meshy' and runner_of('tool') is None
    assert route_keyword('도깨비 3D 모델 만들어')['model'] == 'meshy' and route_keyword('로그 점검해 줘')['model'] == 'haiku'
    # 일 파일만 만들고 띄우지 않기
    code, out = order({'model': 'haiku', 'text': '시험 지시 & | > %PATH%', 'write': True}, launch=False)
    job = json.loads(Path(out['job']).read_text(encoding='utf-8'))
    assert code == 200 and job['prompt'] == '시험 지시 & | > %PATH%' and job['write'] is True and job['runner']['kind'] == 'claude'
    Path(out['job']).unlink()
    assert order({'model': 'tool', 'text': 'x'}, launch=False)[0] == 400
    srv = serve(18765, p)
    threading.Thread(target=srv.serve_forever, daemon=True).start()
    base = 'http://127.0.0.1:%d' % srv.server_address[1]
    html = urllib.request.urlopen(base + '/').read().decode('utf-8')
    assert 'const SNAP = null' in html and json.dumps(TOKEN) in html, 'page must carry the token, no snapshot'
    api = json.loads(urllib.request.urlopen(base + '/api/state').read().decode('utf-8'))
    assert len(api['rows']) == 2
    task = json.loads(urllib.request.urlopen(base + '/api/task?id=a').read().decode('utf-8'))
    assert task['notes'][0][1] == '컴파일 통과' and 'log_tail' in task, task

    def post(url, body, headers):
        req = urllib.request.Request(base + url, data=json.dumps(body).encode(), method='POST', headers=dict({'Content-Type': 'application/json'}, **headers))
        try:
            return urllib.request.urlopen(req).status
        except urllib.error.HTTPError as e:
            return e.code
    assert post('/api/note', {'id': 'b', 'text': 'x'}, {}) == 403, '토큰 없는 POST는 거절'
    assert post('/api/note', {'id': 'b', 'text': 'x'}, {'X-Office-Token': TOKEN, 'Origin': 'https://evil.example'}) == 403, '다른 출처 거절'
    assert post('/api/note', {'id': 'b', 'text': '사장 메모'}, {'X-Office-Token': TOKEN}) == 200
    assert post('/api/stop', {'id': 'a'}, {'X-Office-Token': TOKEN}) == 400            # 끝난 일은 멈출 수 없다
    srv.shutdown()
    assert {r['id']: r for r in board.fold(board.read_events(p))}['b']['notes'][-1][1] == '[사장님] 사장 메모'
    snap = page(dict(s, snapshot=True))
    assert 'const SNAP = {' in snap and '"snapshot": true' in snap and TOKEN not in snap, 'snapshot carries no token'
    g = claude_from_any({'plan': {'windows': [{'label': '5-hour limit', 'percentUsed': 7, 'resetsAt': '2026-10-28T21:30:00.085Z'},
                                              {'label': 'Weekly · all models', 'percentUsed': 52, 'resetsAt': '2026-10-31T04:00:00Z'}]}})
    sl = claude_from_any({'rate_limits': {'five_hour': {'used_percentage': 12.5, 'resets_at': 1793234533}}})
    assert [(w['label'], w['pct']) for w in g] == [('5시간', 7.0), ('주간', 52.0)] and g[0]['resets_at'] and sl[0]['pct'] == 12.5, (g, sl)
    print('selftest ok: 사용 한도 해석(get_usage · 상태줄) · state · lookback · lane · feed · staff.json · route 열쇠말 · 일 파일(stdin 지시) · 토큰/출처 403 · note · stop · snapshot')


def cmd_usage(_):
    for u in [claude_usage(), codex_usage()]:
        age = '%d분 전' % ((time.time() - u['asof']) // 60) if u['asof'] else '값 없음'
        print('%s  (%s · %s)' % (u['who'], age, u['source']))
        for w in u['windows']:
            print('   %-12s %5.1f%%   초기화 %s' % (w['label'], w['pct'], time.strftime('%m-%d %H:%M', time.localtime(w['resets_at'])) if w['resets_at'] else '?'))


def cmd_usage_set_claude(_):
    wins = set_claude_usage(json.loads(sys.stdin.buffer.read().decode('utf-8-sig')), 'get_usage')
    print('Claude 한도 캐시:', ', '.join('%s %.0f%%' % (w['label'], w['pct']) for w in wins))


def cmd_statusline(_):
    """Claude Code 상태줄 명령: stdin JSON의 rate_limits를 캐시에 쓰고 한 줄을 찍는다(한도 정보가 없으면 캐시는 그대로)."""
    try:
        d = json.loads(sys.stdin.buffer.read().decode('utf-8-sig') or '{}')
    except ValueError:
        d = {}
    wins = claude_from_any(d)
    if wins:
        set_claude_usage(d, 'CLI 상태줄')
    model = (d.get('model') or {}).get('display_name', '') if isinstance(d.get('model'), dict) else ''
    print(' · '.join([model] + ['%s %.0f%%' % (w['label'], w['pct']) for w in wins]).strip(' ·'))


def main():
    a = sys.argv[1:]
    if a and a[0] in ('-h', '--help', 'help'):
        print(__doc__); return
    if a and a[0] == 'snapshot':
        cmd_snapshot(a[1:])
    elif a and a[0] == 'usage':
        cmd_usage(a[1:])
    elif a and a[0] == 'usage-set-claude':
        cmd_usage_set_claude(a[1:])
    elif a and a[0] == 'statusline':
        cmd_statusline(a[1:])
    elif a and a[0] == 'selftest':
        cmd_selftest(a[1:])
    else:
        cmd_serve(a)


if __name__ == '__main__':
    main()
