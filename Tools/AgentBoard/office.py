#!/usr/bin/env python3
"""작업판 사무실 (AgentBoard Office, 2026-10-29) — board.jsonl을 게임 개발사 사무실 화면으로 본다.

사용자(2026-10-29): "작업판 프로그램을 더 발전시킬 수 있나? 비주얼 좀 신경써서, 게임 개발사처럼 만들고, 부서별 자리들을 만들어서,
각자 어떤 작업 진행 중인지 각 직원들의 생각풍선으로 보이도록? 또 부가 기능 같은 거 넣고."

기록 · 명령은 그대로 board.py(start/note/done/fail/run)다. 이 파일은 읽기만 한다 — 로컬 서버가 board.jsonl을 접어서 내주고,
office.html이 2초마다 받아 부서 방 · 자리 · 생각풍선 · 게시판 · 성과판 · 타임라인으로 그린다.

  python Tools/AgentBoard/office.py                       # 사무실 창(127.0.0.1 서버 + Edge 앱 창)
  python Tools/AgentBoard/office.py --no-open [--port N]  # 서버만(주소를 브라우저에 직접 넣는다)
  python Tools/AgentBoard/office.py snapshot [out.html]   # 지금 모습을 데이터 내장 HTML 한 장으로(메일 · 기록용)
  python Tools/AgentBoard/office.py selftest              # 창 없이 접기 · API · 스냅샷 검사
"""
import json, os, subprocess, sys, threading, time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import parse_qs, urlparse

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import board  # noqa: E402  (같은 폴더의 기록 · 접기 · 배정표)

PAGE = HERE / 'office.html'
LOOKBACK = 36 * 3600           # 이보다 오래 끝난 작업은 사무실에 안 그린다(진행 중은 항상)

# 부서 — 배정표(board.ROSTER)의 역할을 개발사 부서로. (키, 방 이름, 영문 팻말, 바닥, 책상 소품, 소속 모델)
DEPTS = [
    ('direct', '기획실',   'DIRECTOR',    'carpet', 'plant',   ['opus']),
    ('review', '심사실',   'REVIEW',      'carpet2', 'books',  ['fable']),
    ('art',    '아트팀',   'CONCEPT ART', 'wood2',  'palette', ['gpt-6-astra']),
    ('dev',    '개발팀',   'ENGINEERING', 'wood',   'duck',    ['sonnet', 'gpt-6.1-sol']),
    ('qa',     'QA팀',     'QA',          'tile',   'bug',     ['haiku']),
    ('farm',   '렌더팜',   'RENDER FARM', 'metal',  '',        ['tool']),
]
STAFF = {   # 명패: 짧은 이름 · 직함
    'opus': ('Opus', '총괄 PD'), 'fable': ('Fable', '수석 심사역'), 'sonnet': ('Sonnet', '시니어 프로그래머'),
    'haiku': ('Haiku', 'QA 엔지니어'), 'gpt-6-astra': ('Astra', '컨셉 아티스트'), 'gpt-6.1-sol': ('Sol', '프로토타이퍼'),
    'tool': ('렌더봇', '렌더 서버'),
}

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
    return o


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
    feed = feed[:80]
    return {
        'now': t, 'board': str(path), 'snapshot': False,
        'roster': [{'key': k, 'name': n, 'role': ro, 'what': w, 'color': board.COLOR.get(k, '#b8b8b8'),
                    'short': STAFF.get(k, (k, ro))[0], 'title': STAFF.get(k, (k, ro))[1]} for k, n, ro, w in board.ROSTER],
        'depts': [{'key': k, 'name': n, 'en': en, 'floor': fl, 'item': it, 'models': ms} for k, n, en, fl, it, ms in DEPTS],
        'rows': [row_out(r, notes=0 if detail else 12, log=40 if detail else 0) for r in keep],
        'feed': feed,
        'procs': procs(),
    }


def page(snapshot=None):
    html = PAGE.read_text(encoding='utf-8')
    data = 'null' if snapshot is None else json.dumps(snapshot, ensure_ascii=False).replace('</', '<\\/')
    return html.replace('/*__SNAPSHOT__*/null', data, 1)


def make_handler(path):
    class H(BaseHTTPRequestHandler):
        def _send(self, body, ctype):
            b = body.encode('utf-8')
            self.send_response(200)
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


def cmd_serve(a):
    port = int(a[a.index('--port') + 1]) if '--port' in a else 8765
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
    import tempfile, urllib.request
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
    assert {d['key'] for d in s['depts']} >= {'dev', 'farm'} and all(k in board.COLOR for d in s['depts'] for k in d['models'])
    srv = serve(18765, p)
    threading.Thread(target=srv.serve_forever, daemon=True).start()
    base = 'http://127.0.0.1:%d' % srv.server_address[1]
    html = urllib.request.urlopen(base + '/').read().decode('utf-8')
    assert 'const SNAP = null' in html, 'page must be served without snapshot data'
    api = json.loads(urllib.request.urlopen(base + '/api/state').read().decode('utf-8'))
    assert len(api['rows']) == 2
    task = json.loads(urllib.request.urlopen(base + '/api/task?id=a').read().decode('utf-8'))
    assert task['notes'][0][1] == '컴파일 통과' and 'log_tail' in task, task
    srv.shutdown()
    snap = page(dict(s, snapshot=True))
    assert 'const SNAP = {' in snap and '"snapshot": true' in snap
    print('selftest ok: state · lookback · lane · feed · /api/state · /api/task · snapshot')


def main():
    a = sys.argv[1:]
    if a and a[0] in ('-h', '--help', 'help'):
        print(__doc__); return
    if a and a[0] == 'snapshot':
        cmd_snapshot(a[1:])
    elif a and a[0] == 'selftest':
        cmd_selftest(a[1:])
    else:
        cmd_serve(a)


if __name__ == '__main__':
    main()
