#!/usr/bin/env python3
"""야간 근무 (AgentBoard Night Shift, 2026-10-29) — 사용자가 자는 동안 정한 시각까지 '다음 권장 작업 → 맡기기 → 기록'을 반복하고 아침 보고서를 남긴다.

사용자(2026-10-29): "'나 자러갈테니 질문없이 작업 계속 권장안대로 진행하고, 내가 돌아왔을 때 자는 동안 작업한 내용 정리해서 보고해'
라고 명령하면 어떻게 될까?" → 한 번 도는 일꾼으로는 안 되니 "야간 모드 만들어".

  python Tools/AgentBoard/office_night.py start [--until 07:30] [--mode draft|branch] [--cycles 12] [--budget 30] [--codex-cap 85] [--instruction "…"]
  python Tools/AgentBoard/office_night.py stop      # 지금 바퀴를 마치고 보고서를 쓰고 끝낸다
  python Tools/AgentBoard/office_night.py status
사무실 화면의 '🌙 야간 근무' 단추가 같은 일을 한다.

한 바퀴: ① Opus(읽기 전용)가 PROJECT_STATUS · 인계 · 작업판 · 지난 바퀴를 읽고 다음 권장 작업 하나를 JSON으로 고른다
         ② 고른 직원이 office_worker.py로 실행 ③ 결과를 Art/AgentBoard/night/<id>/cycle_NN.md에 저장(브랜치 모드면 커밋).
끝: Opus가 밤새 기록으로 아침 보고서(report.md)를 쓴다. 작업판에는 night_<id> 줄 + 바퀴마다 일꾼 줄(묶음 '야간')이 뜬다.

모드
- draft(기본): 일꾼은 리포를 못 고친다. 분석 · 계획 · 스펙 초안 · 패치 초안을 글로 내고 야간 폴더에 남는다.
- branch: 새 워크트리(C:/Users/yj666/Oheangbu-night/<id>, 브랜치 night/<id>, 지금 HEAD에서)에서만 고치고 바퀴마다 로컬 커밋.
  추적 파일만 있다(Docs · 에셋 없음). Bash가 없어 컴파일 · 실행 검증은 못 한다 → 작고 명확한 수정만. push는 하지 않는다.
안전장치: 종료 시각(남은 시간 20분 미만이면 새 바퀴 안 함) · 최대 바퀴 · Claude 환산 비용 예산(일꾼이 보고한 total_cost_usd 합 —
Claude 한도는 직접 못 읽는다) · 한도/인증 오류 즉시 중지 · Codex 5시간 사용률 상한 넘으면 Codex 배정 금지 · 연속 실패 2번이면 중지 ·
유료 API · LOCKED/설계 결정 · 삭제 · git push 금지(감독 지시문) · 도는 동안 PC 절전 막기.
"""
import ctypes, json, os, re, secrets, subprocess, sys, time
from datetime import datetime, timedelta
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import board  # noqa: E402
import office  # noqa: E402  (직원 명부 · 실행기 · 사용 한도)

REPO = board.REPO
NIGHT = board.BOARD.parent / 'night'
RESULTS = board.BOARD.parent / 'results'
WT_ROOT = REPO.parent / 'Oheangbu-night'
STALE_MIN = 20 * 60          # 남은 시간이 이보다 적으면 새 바퀴를 시작하지 않는다
CYCLE_TIMEOUT = 45 * 60
NO_FLAGS = 0x08000000 if os.name == 'nt' else 0


def keep_awake(on):
    """ES_CONTINUOUS | ES_SYSTEM_REQUIRED — 도는 동안 절전으로 들어가지 않게(화면은 꺼져도 된다)."""
    if os.name == 'nt':
        ctypes.windll.kernel32.SetThreadExecutionState(0x80000000 | (0x00000001 if on else 0))


def parse_until(s):
    if not s:
        s = '07:30'
    h, m = (int(x) for x in s.split(':'))
    t = datetime.now().replace(hour=h, minute=m, second=0, microsecond=0)
    if t <= datetime.now():
        t += timedelta(days=1)
    return t.timestamp()


def latest():
    """가장 최근 야간 근무 상태(state.json) 또는 None."""
    st = sorted(NIGHT.glob('*/state.json'), key=lambda f: f.stat().st_mtime)
    if not st:
        return None
    try:
        d = json.loads(st[-1].read_text(encoding='utf-8'))
    except ValueError:
        return None
    if d.get('status') == 'running' and d.get('pid') and board.pid_alive(d['pid']) is False:
        d['status'] = 'lost'
    rep = st[-1].parent / 'report.md'
    d['report'] = rep.read_text(encoding='utf-8', errors='replace')[:20000] if rep.exists() else ''
    return d


class Night:
    def __init__(self, args):
        self.id = time.strftime('%m%d_%H%M')
        self.dir = NIGHT / self.id
        self.dir.mkdir(parents=True, exist_ok=True)
        self.s = {'id': self.id, 'status': 'running', 'pid': os.getpid(), 'started': time.time(), 'until': parse_until(args.get('until')),
                  'mode': args.get('mode') or 'draft', 'max_cycles': int(args.get('cycles') or 12), 'budget': float(args.get('budget') or 30),
                  'codex_cap': float(args.get('codex_cap') or 85), 'instruction': args.get('instruction') or '질문 없이 권장안대로 작업을 계속 진행',
                  'spent': 0.0, 'cycles': [], 'stop_reason': '', 'branch': '', 'worktree': '', 'base': ''}
        self.row = 'night_' + self.id
        self.fails = 0

    # ---------------------------------------------------------------- 기록
    def save(self):
        tmp = self.dir / 'state.writing'
        tmp.write_text(json.dumps(self.s, ensure_ascii=False, indent=1), encoding='utf-8'); tmp.replace(self.dir / 'state.json')

    def note(self, text):
        board.append({'ev': 'note', 'id': self.row, 'text': text[:300]})

    def stopped(self):
        return (self.dir / 'STOP').exists()

    # ---------------------------------------------------------------- 일꾼 한 번
    def job(self, model, role, title, prompt, write=False, cwd=None, max_turns=None):
        run = office.runner_of(model)
        jid = 'n%s_%s' % (self.id[-4:], secrets.token_hex(2))
        job = {'id': jid, 'model': model, 'role': role, 'task': title[:80], 'lane': '야간', 'prompt': prompt, 'write': bool(write),
               'runner': run, 'cwd': str(cwd) if cwd else None, 'max_turns': max_turns}
        office.JOBS.mkdir(parents=True, exist_ok=True)
        jf = office.JOBS / (jid + '.json'); jf.write_text(json.dumps(job, ensure_ascii=False, indent=1), encoding='utf-8')
        p = subprocess.Popen([sys.executable, str(HERE / 'office_worker.py'), str(jf)], cwd=REPO, creationflags=NO_FLAGS,
                             stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        t0 = time.time()
        while p.poll() is None:
            time.sleep(5)
            if time.time() - t0 > CYCLE_TIMEOUT:
                subprocess.run(['taskkill', '/PID', str(p.pid), '/T', '/F'], capture_output=True)
                board.append({'ev': 'fail', 'id': jid, 'text': '야간 감독: %d분 넘어 중지' % (CYCLE_TIMEOUT // 60)})
                break
        text = (RESULTS / (jid + '.md')).read_text(encoding='utf-8', errors='replace') if (RESULTS / (jid + '.md')).exists() else ''
        try:
            meta = json.loads((RESULTS / (jid + '.meta.json')).read_text(encoding='utf-8'))
        except (OSError, ValueError):
            meta = {'ok': False}
        self.s['spent'] += float(meta.get('cost_usd') or 0)
        return jid, bool(meta.get('ok')), text, meta

    @staticmethod
    def limit_hit(text, meta):
        return meta.get('api_error_status') in (401, 403, 429) or bool(re.search(r'usage limit|rate limit|limit reached|Failed to authenticate|한도', text[:400], re.I))

    # ---------------------------------------------------------------- 준비
    def setup_branch(self):
        wt = WT_ROOT / self.id
        br = 'night/' + self.id
        WT_ROOT.mkdir(parents=True, exist_ok=True)
        base = subprocess.run(['git', '-C', str(REPO), 'rev-parse', '--abbrev-ref', 'HEAD'], capture_output=True, text=True, encoding='utf-8', errors='replace').stdout.strip()
        r = subprocess.run(['git', '-C', str(REPO), 'worktree', 'add', '-b', br, str(wt), 'HEAD'], capture_output=True, text=True, encoding='utf-8', errors='replace')
        if r.returncode:
            raise RuntimeError('워크트리 만들기 실패: ' + r.stderr.strip()[:200])
        self.s.update(branch=br, worktree=str(wt), base=base)

    def staff_menu(self, codex_ok):
        out = []
        for r in office.roster():
            if not r['kind'] or r.get('paid') or (r['kind'] == 'codex' and not codex_ok):
                continue
            out.append('- %s: %s %s — %s' % (r['key'], r['short'], r['title'], r['what']))
        return '\n'.join(out)

    def daytime_running(self):
        rows = [r for r in board.fold(board.read_events()) if r['state'] == 'run' and r.get('lane') != '야간' and not r['id'].startswith('night_')]
        return '\n'.join('- [%s] %s: %s' % (r.get('lane'), r['model'], r['task']) for r in rows[:15]) or '- (없음)'

    def history(self):
        return '\n'.join('%d. [%s · %s] %s — %s' % (c['n'], c['assignee'], '성공' if c['ok'] else '실패', c['title'], c.get('summary', '')[:200])
                         for c in self.s['cycles']) or '(아직 없음)'

    # ---------------------------------------------------------------- 한 바퀴
    def cycle(self, n):
        cx = office.codex_usage()
        codex_ok = all(w['pct'] < self.s['codex_cap'] for w in cx['windows'][:1])
        mode = self.s['mode']
        mode_desc = ('초안만 — 일꾼은 리포 파일을 고칠 수 없다(write는 항상 false). 결과(분석 · 계획 · 스펙 초안 · 코드 패치 초안)는 답 글로 내고 감독이 야간 폴더에 저장한다.'
                     if mode == 'draft' else
                     '별도 브랜치 — write=true면 일꾼은 워크트리 %s(브랜치 %s, 추적 파일만 있고 Docs · 에셋 없음)에서 실제로 고치고 감독이 커밋한다. '
                     '컴파일 · 실행 검증을 못 하니 작고 명확한 수정만. 문서(Docs) 수정은 반영되지 않으니 답 글 초안으로.' % (self.s['worktree'], self.s['branch']))
        plan_prompt = (
            '너는 미적 스튜디오(오행부, 1인 개발) 야간 근무 감독(총괄 PD)이다. 사용자는 자고 있어 질문할 수 없다.\n'
            '사용자 지시: «%s»\n지금 %d번째 바퀴 / 최대 %d, 종료 %s.\n\n지금까지 밤에 한 일:\n%s\n\n'
            '낮 세션이 진행 중인 작업(겹치지 말 것):\n%s\n\n'
            '먼저 Docs/PROJECT_STATUS.md(Now · Next · Blocker)와 최근 Docs/Handoff 문서, 필요하면 Docs/DECISIONS.md를 읽고 다음 권장 작업 한 개를 골라라.\n'
            '규칙: 한 바퀴 = 한 작업(30분 안에 끝낼 크기). 무인으로 안전한 일만 — 유료 API(Meshy · Recraft · Suno) 금지, LOCKED 변경 · 설계 결정 금지'
            '(필요한 결정은 \'결정 대기\'로 적기만), 파일 삭제 금지, 일꾼은 Bash · git · Unity 편집기 · Play · Blender를 못 쓴다. 이미 한 일을 반복하지 않는다.\n'
            '모드: %s\n맡길 수 있는 직원:\n%s\n\n'
            'JSON 한 줄로만 답하라: {"assignee": "키", "title": "30자 제목", "prompt": "일꾼에게 줄 자세한 지시(읽을 문서 경로와 맥락 포함 — 일꾼은 이 대화를 모른다)", '
            '"write": false, "why": "한 문장"}  더 할 안전한 일이 없으면 {"stop": "이유"}'
            % (self.s['instruction'], n, self.s['max_cycles'], time.strftime('%H:%M', time.localtime(self.s['until'])), self.history(),
               self.daytime_running(), mode_desc, self.staff_menu(codex_ok)))
        pid_, ok, text, meta = self.job('opus', '야간 기획', '야간 %d바퀴 — 다음 일 고르기' % n, plan_prompt, max_turns=40)
        if self.limit_hit(text, meta):
            return 'limit', '기획 단계에서 한도/인증 오류: ' + text[:160]
        try:
            plan = json.loads(re.search(r'\{.*\}', text, re.S).group(0))
        except (AttributeError, ValueError):
            return 'fail', '기획 결과를 못 읽음: ' + text[:160]
        if plan.get('stop'):
            return 'done', '감독 판단으로 종료: ' + str(plan['stop'])[:200]
        who = plan.get('assignee')
        if not office.runner_of(who) or who in office.PAID or (office.runner_of(who)['kind'] == 'codex' and not codex_ok):
            who = 'sonnet'
        write = bool(plan.get('write')) and mode == 'branch' and office.runner_of(who)['kind'] in ('claude', 'codex')
        title = str(plan.get('title') or '야간 작업')[:60]
        self.note('%d바퀴: %s → %s' % (n, title, who))
        head = ('[야간 근무 %d바퀴] 사용자는 자고 있어 질문할 수 없다. 막히면 가정을 적고 진행하거나, 멈추고 이유를 보고하라. '
                '유료 API · 설계 결정 · 파일 삭제는 하지 않는다.\n' % n)
        if write:
            head += '작업 폴더는 별도 워크트리(브랜치 %s)다. 이 폴더 안 파일만 고친다. Docs 폴더는 여기 없다.\n' % self.s['branch']
        elif mode == 'branch' or mode == 'draft':
            head += '이번 일은 읽기 전용이다. 결과물(초안 · 패치 · 표)은 답 글에 그대로 적어라 — 감독이 저장한다.\n'
        jid, ok, text, meta = self.job(who, plan.get('role') or '야간', title, head + '\n' + str(plan.get('prompt', '')), write=write,
                                       cwd=self.s['worktree'] if write else None, max_turns=80 if who in ('opus', 'sonnet', 'fable', 'haiku') else None)
        out = self.dir / ('cycle_%02d.md' % n)
        out.write_text('# %d바퀴 — %s\n\n- 직원: %s · %s · 작업판 %s\n- 이유: %s\n\n## 지시\n\n%s\n\n## 결과\n\n%s\n'
                       % (n, title, who, '수정' if write else '읽기 전용', jid, plan.get('why', ''), plan.get('prompt', ''), text), encoding='utf-8')
        commit = ''
        if write and ok:
            subprocess.run(['git', '-C', self.s['worktree'], 'add', '-A'], capture_output=True)
            r = subprocess.run(['git', '-C', self.s['worktree'], 'commit', '-q', '-m', 'night(%s #%d): %s\n\n야간 근무 자동 커밋 — %s 작업, 컴파일 · 실행 미검증.'
                                % (self.id, n, title, who)], capture_output=True, text=True, encoding='utf-8', errors='replace')
            if r.returncode == 0:
                commit = subprocess.run(['git', '-C', self.s['worktree'], 'rev-parse', '--short', 'HEAD'], capture_output=True, text=True, encoding='utf-8', errors='replace').stdout.strip()
        summary = next((l.strip() for l in text.splitlines() if l.strip() and not l.startswith('#')), '')[:300]
        self.s['cycles'].append({'n': n, 'title': title, 'assignee': who, 'ok': ok, 'job': jid, 'write': write, 'commit': commit,
                                 'why': plan.get('why', ''), 'summary': summary, 'file': str(out)})
        self.save()
        if self.limit_hit(text, meta):
            return 'limit', '작업 중 한도/인증 오류: ' + text[:160]
        return ('ok' if ok else 'fail'), summary

    # ---------------------------------------------------------------- 보고
    def report(self):
        parts = []
        for c in self.s['cycles']:
            body = Path(c['file']).read_text(encoding='utf-8', errors='replace') if Path(c['file']).exists() else ''
            parts.append('### %d. %s (%s, %s%s)\n%s' % (c['n'], c['title'], c['assignee'], '성공' if c['ok'] else '실패',
                                                       ', 커밋 ' + c['commit'] if c['commit'] else '', body[-2500:]))
        diff = ''
        if self.s['worktree']:
            diff = subprocess.run(['git', '-C', self.s['worktree'], 'diff', '--stat', 'HEAD~%d' % max(1, sum(1 for c in self.s['cycles'] if c['commit'])), 'HEAD'],
                                  capture_output=True, text=True, encoding='utf-8', errors='replace').stdout[-3000:] if any(c['commit'] for c in self.s['cycles']) else ''
        head = ('야간 근무 %s · %s – %s · 모드 %s · %d바퀴 · Claude 환산 $%.2f · 끝난 이유: %s%s'
                % (self.id, time.strftime('%H:%M', time.localtime(self.s['started'])), time.strftime('%H:%M'), self.s['mode'], len(self.s['cycles']),
                   self.s['spent'], self.s['stop_reason'], ('\n브랜치 %s (워크트리 %s, 기준 %s)' % (self.s['branch'], self.s['worktree'], self.s['base'])) if self.s['branch'] else ''))
        prompt = ('사용자가 자는 동안 한 야간 근무 기록이다. 사용자가 일어나서 읽을 아침 보고서를 한국어 마크다운으로 써라.\n'
                  '순서: 한 줄 요약 → 한 일(바퀴별 결과물과 위치) → 확인이 필요한 것(미검증 수정 · 실패 · 사용자 결정 대기) → 다음 권장.\n'
                  '과장하지 말고, 검증 안 된 것은 미검증이라고 쓴다. 파일은 읽지 말고 아래 기록만 써라.\n\n%s\n\n%s\n\n변경 통계:\n%s'
                  % (head, '\n\n'.join(parts) or '(한 바퀴도 못 돌았다)', diff or '(없음)'))
        _, ok, text, meta = self.job('opus', '아침 보고', '야간 근무 아침 보고서', prompt, max_turns=3)
        if not ok or not text.strip():
            text = '# 야간 근무 보고(자동 요약 — 보고서 작성 실패)\n\n' + '\n'.join(
                '- %d. [%s · %s] %s — %s' % (c['n'], c['assignee'], '성공' if c['ok'] else '실패', c['title'], c['summary']) for c in self.s['cycles'])
        head = re.sub(r'Claude 환산 \$[\d.]+', 'Claude 환산 $%.2f(보고서 포함)' % self.s['spent'], head)   # 보고서 쓰는 비용까지
        (self.dir / 'report.md').write_text('> ' + head.replace('\n', '\n> ') + '\n\n' + text, encoding='utf-8')
        self.s['report_file'] = str(self.dir / 'report.md')

    # ---------------------------------------------------------------- 전체
    def run(self):
        board.append({'ev': 'start', 'id': self.row, 'lane': '야간', 'model': 'opus', 'role': '야간 근무',
                      'task': '야간 근무 — %s까지 · %s' % (time.strftime('%H:%M', time.localtime(self.s['until'])), self.s['mode']), 'pid': os.getpid()})
        keep_awake(True)
        try:
            cl = office.claude_usage()
            weekly = [w['pct'] for w in cl['windows'] if w['label'] == '주간']
            if weekly and weekly[0] >= 90:
                self.s['stop_reason'] = 'Claude 주간 사용률 %.0f%% — 시작하지 않음' % weekly[0]
            elif self.s['mode'] == 'branch':
                self.setup_branch(); self.note('워크트리 %s · 브랜치 %s' % (self.s['worktree'], self.s['branch']))
            self.save()
            n = 0
            while not self.s['stop_reason']:
                if self.stopped():
                    self.s['stop_reason'] = '사무실에서 중지'; break
                if n >= self.s['max_cycles']:
                    self.s['stop_reason'] = '최대 바퀴(%d) 도달' % self.s['max_cycles']; break
                if self.s['until'] - time.time() < STALE_MIN:
                    self.s['stop_reason'] = '종료 시각'; break
                if self.s['spent'] >= self.s['budget']:
                    self.s['stop_reason'] = 'Claude 환산 예산 $%.0f 소진' % self.s['budget']; break
                n += 1
                kind, msg = self.cycle(n)
                self.note('%d바퀴 %s — %s' % (n, {'ok': '끝', 'fail': '실패', 'done': '종료', 'limit': '한도'}[kind], msg[:160]))
                if kind == 'limit':
                    self.s['stop_reason'] = msg[:200]; break
                if kind == 'done':
                    self.s['stop_reason'] = msg[:200]; break
                self.fails = self.fails + 1 if kind == 'fail' else 0
                if self.fails >= 2:
                    self.s['stop_reason'] = '연속 실패 2번'; break
                self.save()
        except Exception as x:
            self.s['stop_reason'] = '감독 오류 %s: %s' % (type(x).__name__, x)
        finally:
            self.s['status'] = 'reporting'; self.save()
            try:
                self.report()
            except Exception as x:
                (self.dir / 'report.md').write_text('# 야간 근무 보고 실패\n\n%s: %s\n' % (type(x).__name__, x), encoding='utf-8')
            self.s['status'] = 'done'; self.save()
            keep_awake(False)
            board.append({'ev': 'done', 'id': self.row, 'text': '%d바퀴 · %s · 보고서 %s' % (len(self.s['cycles']), self.s['stop_reason'], self.dir / 'report.md')})


def opts(a):
    out, i = {}, 0
    while i < len(a):
        if a[i].startswith('--') and i + 1 < len(a):
            out[a[i][2:].replace('-', '_')] = a[i + 1]; i += 2
        else:
            i += 1
    return out


def spawn(args):
    """사무실 서버가 부른다 — 감독을 창 없이 떼어 띄운다."""
    cur = latest()
    if cur and cur.get('status') in ('running', 'reporting'):
        return 409, {'error': '이미 야간 근무 중이다(%s)' % cur['id']}
    argv = [sys.executable, str(HERE / 'office_night.py'), 'start']
    for k in ('until', 'mode', 'cycles', 'budget', 'codex_cap', 'instruction'):
        if args.get(k) not in (None, ''):
            argv += ['--' + k.replace('_', '-'), str(args[k])]
    # 창 없이 · 새 프로세스 그룹(사무실 서버를 Ctrl+C로 꺼도 같이 죽지 않게)
    subprocess.Popen(argv, cwd=REPO, creationflags=NO_FLAGS | (0x00000200 if os.name == 'nt' else 0),
                     stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    return 200, {'ok': True}


def request_stop():
    cur = latest()
    if not cur or cur.get('status') not in ('running',):
        return 400, {'error': '도는 야간 근무가 없다'}
    (NIGHT / cur['id'] / 'STOP').write_text('stop', encoding='utf-8')
    return 200, {'ok': True}


def main():
    a = sys.argv[1:]
    if not a or a[0] in ('-h', '--help'):
        print(__doc__); return
    if a[0] == 'start':
        Night(opts(a[1:])).run()
    elif a[0] == 'stop':
        print(request_stop())
    elif a[0] == 'status':
        d = latest()
        print(json.dumps({k: v for k, v in (d or {}).items() if k != 'report'}, ensure_ascii=False, indent=1) if d else '야간 근무 기록 없음')


if __name__ == '__main__':
    main()
