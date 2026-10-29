#!/usr/bin/env python3
"""작업판 (AgentBoard, 2026-10-29) — 여러 모델이 나눠 맡은 작업의 현황을 한 창에서 본다.

사용자(2026-10-29): "fable, opus, sonnet, haiku, codex의 두 모델까지 각자 잘하는 일에 배정해서 일을 시킬 수 있나?
그리고 작업 진행하는 모델의 현황을 알 수 있는 간단한 프로그램도 만들어줄 수 있을까?"

기록은 덧붙이기만 하는 JSONL 한 파일(Art/AgentBoard/board.jsonl). 일꾼(메인 · 하위 에이전트 · codex · blender 래퍼)은 아래 명령으로 적고, 창은 그 파일을 2초마다 다시 읽는다.

  python Tools/AgentBoard/board.py view                                     # 현황 창(작업 표 · 모델 배정 · 선택한 작업의 기록)
  python Tools/AgentBoard/board.py list                                     # 같은 내용을 터미널에
  python Tools/AgentBoard/board.py start <id> --model sonnet --role 구현 --task "우 C 셰이더"
  python Tools/AgentBoard/board.py note  <id> "컴파일 통과"                  # 진행 메모(하위 에이전트가 단계마다)
  python Tools/AgentBoard/board.py done  <id> ["요약"]   /  fail <id> ["이유"]
  python Tools/AgentBoard/board.py run   <id> --model gpt-6-astra --role 시안 --task "..." [--log 파일] -- <명령 ...>
        # 명령을 실행하며 시작 · pid · 끝 · 종료 코드 · 출력 마지막 줄을 자동으로 적는다(codex exec · blender 래핑용)
  python Tools/AgentBoard/board.py selftest                                 # 창을 띄우지 않고 접기 · 표 그리기만 검사

묶음(--lane): start · run에 `--lane 맵`처럼 붙이면 작업이 그 묶음에 들어간다. 없으면 VFX(작업판을 처음 쓴 묶음).
창 위쪽의 묶음 단추로 전체 / 묶음별로 걸러 본다. 맵 작업 id는 `map_` 로 시작한다(다른 세션의 `main` 등과 겹치지 않게).
  python Tools/AgentBoard/board.py start map_j1 --lane 맵 --model haiku --role 점검 --task "J-1 적로 던전 의존 표"
  python Tools/AgentBoard/board.py list [맵]                               # 묶음 하나만
"""
import ctypes, json, os, subprocess, sys, time
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
BOARD = REPO / 'Art' / 'AgentBoard' / 'board.jsonl'

# 모델 배정표 — 누가 무엇을 잘하나(VFX 작업 기준). 창 위쪽에 그대로 보인다.
ROSTER = [
    ('opus',          'Claude Opus 5.5',  '총괄',   '설계 · 판정 · 통합 · 커밋 · 사용자 보고(메인 세션)'),
    ('fable',         'Claude Fable 5.1', '심판',   '토론 채점(97점 관문) · 시안/영상 판정 · 어려운 원인 진단'),
    ('sonnet',        'Claude Sonnet 5.5', '구현',  'C# · 셰이더 · Blender 스크립트 등 일반 구현, 정본 대조 채점'),
    ('haiku',         'Claude Haiku 5.5', '점검',   '로그 · 프레임 차이 · 파일 목록 점검, 정본 없이 읽는 플레이어 패널'),
    ('gpt-6-astra',   'Codex GPT-6 Astra', '시안',  '내장 그림 생성(시안), 컨셉 계획서 집필, 2차 진단'),
    ('gpt-6.1-sol',   'Codex GPT-6.1 Sol', '보조',  '빠른 2차 구현 · 프롬프트 · 변형 시안 대량 생성'),
    ('tool',          '도구',             '렌더',   'Blender Cycles · Unity 촬영 등 모델 아닌 장시간 작업'),
]
COLOR = {'opus': '#c9a0ff', 'fable': '#ffb86b', 'sonnet': '#7fd0ff', 'haiku': '#9be89b', 'gpt-6-astra': '#ff8fa3', 'gpt-6.1-sol': '#ffd27f', 'tool': '#b8b8b8'}
STATE_KO = {'run': '진행', 'done': '완료', 'fail': '실패', 'lost': '끊김'}
DEFAULT_LANE = 'VFX'          # 묶음 없이 적힌 옛 기록은 술식 VFX 작업이다


def now():
    return time.time()


def append(ev):
    BOARD.parent.mkdir(parents=True, exist_ok=True)
    ev.setdefault('ts', now())
    with open(BOARD, 'a', encoding='utf-8') as f:
        f.write(json.dumps(ev, ensure_ascii=False) + '\n')


def read_events(path=BOARD):
    if not Path(path).exists():
        return []
    out = []
    for line in Path(path).read_text(encoding='utf-8', errors='replace').splitlines():
        try:
            out.append(json.loads(line))
        except ValueError:
            pass
    return out


def pid_alive(pid):
    """Windows: OpenProcess + GetExitCodeProcess (os.kill(pid, 0) would TERMINATE the process on Windows)."""
    if not pid:
        return None
    if os.name != 'nt':
        try:
            os.kill(pid, 0); return True
        except OSError:
            return False
    k = ctypes.windll.kernel32
    h = k.OpenProcess(0x1000, False, int(pid))      # PROCESS_QUERY_LIMITED_INFORMATION
    if not h:
        return False
    code = ctypes.c_ulong()
    ok = k.GetExitCodeProcess(h, ctypes.byref(code)); k.CloseHandle(h)
    return bool(ok) and code.value == 259          # STILL_ACTIVE


def fold(events):
    """Events -> one row per task id (latest state wins), newest first."""
    rows = {}
    for e in events:
        i = e.get('id')
        if not i:
            continue
        r = rows.setdefault(i, {'id': i, 'lane': DEFAULT_LANE, 'model': '', 'role': '', 'task': '', 'state': 'run', 'start': e['ts'], 'end': None, 'pid': None, 'log': None, 'notes': []})
        k = e.get('ev')
        if k == 'start':
            r.update({k2: e[k2] for k2 in ('lane', 'model', 'role', 'task', 'pid', 'log') if e.get(k2)})
            r.update(state='run', start=e['ts'], end=None)
        elif k == 'note':
            r['notes'].append((e['ts'], e.get('text', '')))
        elif k in ('done', 'fail'):
            r.update(state=k, end=e['ts'])
            if e.get('text'):
                r['notes'].append((e['ts'], e['text']))
    for r in rows.values():
        if r['state'] == 'run' and r['pid'] and pid_alive(r['pid']) is False:
            r['state'] = 'lost'      # its process is gone without a done/fail line
    return sorted(rows.values(), key=lambda r: (r['state'] != 'run', -r['start']))


def dur(r):
    s = int((r['end'] or now()) - r['start'])
    return '%d:%02d:%02d' % (s // 3600, s % 3600 // 60, s % 60) if s >= 3600 else '%d:%02d' % (s // 60, s % 60)


def last_note(r):
    return r['notes'][-1][1] if r['notes'] else ''


def untracked_procs():
    """Heavy processes running right now that are not on the board (a hint, not a record)."""
    try:
        out = subprocess.run(['tasklist', '/fo', 'csv', '/nh'], capture_output=True, text=True, timeout=5).stdout
    except Exception:
        return {}
    cnt = {}
    for line in out.splitlines():
        n = line.split('","')[0].strip('"').lower()
        for key in ('blender.exe', 'codex.exe', 'unity.exe'):
            if n == key:
                cnt[key] = cnt.get(key, 0) + 1
    return cnt


# ------------------------------------------------------------------ commands
def opt(args, name, default=None):
    return args[args.index(name) + 1] if name in args else default


def cmd_start(a):
    append({'ev': 'start', 'id': a[0], 'lane': opt(a, '--lane', ''), 'model': opt(a, '--model', ''), 'role': opt(a, '--role', ''), 'task': opt(a, '--task', ''),
            'pid': int(opt(a, '--pid')) if opt(a, '--pid') else None, 'log': opt(a, '--log')})


def cmd_run(a):
    sep = a.index('--')
    head, cmd = a[:sep], a[sep + 1:]
    log = opt(head, '--log') or str(BOARD.parent / 'logs' / (head[0] + '.log'))
    Path(log).parent.mkdir(parents=True, exist_ok=True)
    with open(log, 'w', encoding='utf-8') as lf:
        p = subprocess.Popen(cmd, stdout=lf, stderr=subprocess.STDOUT, shell=os.name == 'nt')
        append({'ev': 'start', 'id': head[0], 'lane': opt(head, '--lane', ''), 'model': opt(head, '--model', ''), 'role': opt(head, '--role', ''), 'task': opt(head, '--task', ''), 'pid': p.pid, 'log': log})
        code = p.wait()
    tail = [l for l in Path(log).read_text(encoding='utf-8', errors='replace').splitlines() if l.strip()][-1:] or ['']
    append({'ev': 'done' if code == 0 else 'fail', 'id': head[0], 'text': ('종료 %d · ' % code) + tail[0][:160]})
    sys.exit(code)


def cmd_list(a):
    rows = [r for r in fold(read_events()) if not a or r['lane'] == a[0]]
    for r in rows:
        print('%-4s %-4s %-12s %-5s %-8s %-40s %s' % (STATE_KO[r['state']], r['lane'], r['model'], r['role'], dur(r), r['task'][:40], last_note(r)[:60]))
    u = untracked_procs()
    if u:
        print('실행 중 프로세스:', ', '.join('%s ×%d' % kv for kv in u.items()))


def build_ui(root, path=BOARD):
    import tkinter as tk
    from tkinter import ttk
    F = ('Malgun Gothic', 10)
    root.title('작업판 — 모델별 작업 현황'); root.geometry('1180x640'); root.configure(bg='#1e2430')
    st = ttk.Style(root); st.theme_use('clam')
    st.configure('Treeview', background='#262d3b', fieldbackground='#262d3b', foreground='#e6e6e6', font=F, rowheight=24)
    st.configure('Treeview.Heading', background='#323b4d', foreground='#ffd76a', font=('Malgun Gothic', 10, 'bold'))
    top = tk.Frame(root, bg='#1e2430'); top.pack(fill='x', padx=8, pady=(8, 4))
    badges = {}
    for i, (key, name, role, what) in enumerate(ROSTER):
        b = tk.Label(top, text='%s\n%s' % (name, role), bg='#262d3b', fg=COLOR[key], font=('Malgun Gothic', 9, 'bold'), padx=8, pady=4, justify='center')
        b.grid(row=0, column=i, padx=3, sticky='nsew'); top.columnconfigure(i, weight=1)
        tip = tk.Label(top, text=what, bg='#1e2430', fg='#9aa3b5', font=('Malgun Gothic', 8), wraplength=150, justify='center')
        tip.grid(row=1, column=i, padx=3, sticky='n'); badges[key] = (b, name, role)
    bar = tk.Frame(root, bg='#1e2430'); bar.pack(fill='x', padx=8, pady=(2, 4))
    lane_sel = tk.StringVar(root, '전체')
    cols = ('state', 'lane', 'model', 'role', 'time', 'task', 'note')
    heads = ('상태', '묶음', '모델', '역할', '경과', '작업', '최근 기록')
    tv = ttk.Treeview(root, columns=cols, show='headings', height=14)
    for c, h, w in zip(cols, heads, (60, 60, 130, 60, 70, 360, 420)):
        tv.heading(c, text=h); tv.column(c, width=w, anchor='w' if c in ('task', 'note') else 'center')
    for s, fg in (('run', '#ffffff'), ('done', '#8f9bb0'), ('fail', '#ff7b7b'), ('lost', '#ffb36b')):
        tv.tag_configure(s, foreground=fg)
    tv.pack(fill='both', expand=True, padx=8)
    detail = tk.Text(root, height=8, bg='#161b24', fg='#d0d6e2', font=('Malgun Gothic', 9), relief='flat', wrap='word')
    detail.pack(fill='x', padx=8, pady=(4, 0))
    foot = tk.Label(root, text='', bg='#1e2430', fg='#9aa3b5', font=('Malgun Gothic', 9), anchor='w'); foot.pack(fill='x', padx=8, pady=4)
    state = {'rows': {}, 'sel': None, 'lanes': None}

    def lane_buttons(rows):
        """묶음 단추: 전체 + 기록에 나온 묶음마다 하나(진행 중 수 표시)."""
        lanes = sorted({r['lane'] for r in rows}, key=lambda x: (x != DEFAULT_LANE, x))
        counts = {l: sum(r['lane'] == l and r['state'] == 'run' for r in rows) for l in lanes}
        key = (tuple(lanes), tuple(counts.values()))
        if key == state['lanes']:
            return
        state['lanes'] = key
        for w in bar.winfo_children():
            w.destroy()
        for l in ['전체'] + lanes:
            n = sum(counts.values()) if l == '전체' else counts[l]
            tk.Radiobutton(bar, text='%s%s' % (l, ' · 진행 %d' % n if n else ''), value=l, variable=lane_sel, indicatoron=False,
                           command=lambda: refresh(False), bg='#262d3b', fg='#e6e6e6', selectcolor='#34405a', activebackground='#34405a',
                           activeforeground='#ffd76a', font=('Malgun Gothic', 9, 'bold'), padx=10, pady=2, relief='flat').pack(side='left', padx=3)

    def show_detail(_=None):
        sel = tv.selection()
        if sel:
            state['sel'] = sel[0]
        r = state['rows'].get(state['sel'])
        detail.delete('1.0', 'end')
        if not r:
            return
        lines = ['[%s] %s · %s · %s · 시작 %s' % (STATE_KO[r['state']], r['model'], r['role'], r['task'], time.strftime('%H:%M:%S', time.localtime(r['start'])))]
        lines += ['  %s  %s' % (time.strftime('%H:%M:%S', time.localtime(t)), x) for t, x in r['notes'][-12:]]
        if r['log'] and Path(r['log']).exists():
            tail = Path(r['log']).read_text(encoding='utf-8', errors='replace').splitlines()[-6:]
            lines += ['  ── 로그 끝(%s)' % r['log']] + ['  ' + l[:200] for l in tail]
        detail.insert('end', '\n'.join(lines))

    def refresh(again=True):
        every = fold(read_events(path))
        lane_buttons(every)
        rows = [r for r in every if lane_sel.get() in ('전체', r['lane'])]
        state['rows'] = {r['id']: r for r in rows}
        tv.delete(*tv.get_children())
        for r in rows:
            tv.insert('', 'end', iid=r['id'], values=(STATE_KO[r['state']], r['lane'], r['model'], r['role'], dur(r), r['task'], last_note(r)), tags=(r['state'],))
        if state['sel'] in state['rows']:
            tv.selection_set(state['sel'])
        busy = {}
        for r in rows:
            if r['state'] == 'run':
                busy[r['model']] = busy.get(r['model'], 0) + 1
        for key, (b, name, role) in badges.items():
            n = busy.get(key, 0)
            b.configure(text='%s\n%s%s' % (name, role, ' · 작업 %d' % n if n else ' · 대기'), bg='#34405a' if n else '#262d3b')
        u = untracked_procs()
        foot.configure(text='[%s] 진행 %d · 완료 %d · 실패/끊김 %d   |   실행 중 프로세스: %s   |   %s  (2초마다 갱신)' % (
            lane_sel.get(), sum(r['state'] == 'run' for r in rows), sum(r['state'] == 'done' for r in rows), sum(r['state'] in ('fail', 'lost') for r in rows),
            ', '.join('%s ×%d' % kv for kv in u.items()) or '없음', path))
        show_detail()
        if again:
            root.after(2000, refresh)

    tv.bind('<<TreeviewSelect>>', show_detail)
    refresh()
    return tv


def cmd_view(_):
    import tkinter as tk
    root = tk.Tk(); build_ui(root); root.mainloop()


def cmd_selftest(_):
    import tempfile, tkinter as tk
    p = Path(tempfile.mkdtemp()) / 'b.jsonl'
    t0 = now() - 300
    evs = [{'ev': 'start', 'id': 'a', 'model': 'sonnet', 'role': '구현', 'task': '시험 구현', 'ts': t0},
           {'ev': 'note', 'id': 'a', 'text': '컴파일 통과', 'ts': t0 + 60},
           {'ev': 'done', 'id': 'a', 'text': '끝', 'ts': t0 + 120},
           {'ev': 'start', 'id': 'b', 'model': 'gpt-6-astra', 'role': '시안', 'task': '시안', 'pid': 999999, 'ts': t0 + 10},
           {'ev': 'start', 'id': 'c', 'lane': '맵', 'model': 'haiku', 'role': '점검', 'task': '점검', 'pid': os.getpid(), 'ts': t0 + 20}]
    p.write_text('\n'.join(json.dumps(e, ensure_ascii=False) for e in evs) + '\n', encoding='utf-8')
    rows = {r['id']: r for r in fold(read_events(p))}
    assert rows['a']['state'] == 'done' and rows['a']['notes'][0][1] == '컴파일 통과', rows['a']
    assert rows['b']['state'] == 'lost', rows['b']       # dead pid
    assert rows['c']['state'] == 'run', rows['c']        # our own pid is alive
    assert rows['a']['lane'] == DEFAULT_LANE and rows['c']['lane'] == '맵', (rows['a']['lane'], rows['c']['lane'])
    root = tk.Tk(); root.withdraw()                      # never shown (no focus change)
    tv = build_ui(root, p); root.update()
    assert len(tv.get_children()) == 3
    bar = [w for w in root.winfo_children() if any(isinstance(b, tk.Radiobutton) for b in w.winfo_children())][0]
    names = [b.cget('text') for b in bar.winfo_children()]
    assert names[0].startswith('전체') and names[1] == DEFAULT_LANE and names[2].startswith('맵'), names
    bar.winfo_children()[2].invoke(); root.update()      # 맵 단추 → 맵 한 줄만
    assert list(tv.get_children()) == ['c'], tv.get_children()
    root.destroy()
    print('selftest ok: fold · pid · lane · table (3 rows, 맵 거르기 1)')


def main():
    if len(sys.argv) < 2:
        print(__doc__); return
    c, a = sys.argv[1], sys.argv[2:]
    {'start': cmd_start, 'note': lambda a: append({'ev': 'note', 'id': a[0], 'text': ' '.join(a[1:])}),
     'done': lambda a: append({'ev': 'done', 'id': a[0], 'text': ' '.join(a[1:])}),
     'fail': lambda a: append({'ev': 'fail', 'id': a[0], 'text': ' '.join(a[1:])}),
     'run': cmd_run, 'list': cmd_list, 'view': cmd_view, 'selftest': cmd_selftest}[c](a)


if __name__ == '__main__':
    main()
