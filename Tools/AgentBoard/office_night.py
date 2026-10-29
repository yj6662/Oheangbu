#!/usr/bin/env python3
"""야간 근무 (AgentBoard Night Shift, 2026-10-29) — 사용자가 자는 동안 정한 시각까지 '다음 권장 작업 → 맡기기 → 기록'을 반복하고 아침 보고서를 남긴다.

사용자(2026-10-29): "'나 자러갈테니 질문없이 작업 계속 권장안대로 진행하고, 내가 돌아왔을 때 자는 동안 작업한 내용 정리해서 보고해'
라고 명령하면 어떻게 될까?" → 한 번 도는 일꾼으로는 안 되니 "야간 모드 만들어".

  python Tools/AgentBoard/office_night.py start [--until 07:30|2026-10-31T07:30|+48h] [--mode full|branch|draft] [--cycles 300] [--budget 0] [--instruction "…"]
  python Tools/AgentBoard/office_night.py stop      # 지금 바퀴를 마치고 보고서를 쓰고 끝낸다
  python Tools/AgentBoard/office_night.py status
사무실 화면의 '🌙 야간 근무' 단추가 같은 일을 한다.

한 바퀴: ① Opus(읽기 전용)가 PROJECT_STATUS · 인계 · 작업판 · 지난 바퀴를 읽고 다음 권장 작업 하나를 JSON으로 고른다
         ② 고른 직원이 office_worker.py로 실행 ③ 결과를 Art/AgentBoard/night/<id>/cycle_NN.md에 저장(브랜치 모드면 커밋).
끝: Opus가 밤새 기록으로 아침 보고서(report.md)를 쓴다. 작업판에는 night_<id> 줄 + 바퀴마다 일꾼 줄(묶음 '야간')이 뜬다.

모드
- full(기본, 사용자 2026-10-29 "대부분의 경우 이 세션처럼 진행할거니까"): Opus 세션 하나를 밤새 --resume으로 이어 간다.
  권한은 데스크톱 메인 세션과 같은 auto + Bash + Unity MCP — 구현 · 컴파일 · Play 검증 · 로컬 커밋까지 스스로, 하위 에이전트 · codex exec도 쓴다.
  바퀴 사이에 감독이 편집기 상태를 보고 필요하면 life.ps1로 재시작한다(office_editor.py, 사용자 "재시작도 가능하게").
  아침 보고서도 같은 세션이 쓴다. 아침에 `claude --resume <session_id>`(또는 사무실 '앱에서 열기')로 그 세션과 이어서 대화할 수 있다.
  비용 예산 · Codex 상한 없음(Max · 추가 사용 꺼짐). 한도에 걸리면 30분씩 기다렸다 같은 바퀴를 다시 한다. 인증 오류(401)만 멈춘다.
- draft: 일꾼은 리포를 못 고친다. 분석 · 계획 · 스펙 초안 · 패치 초안을 글로 내고 야간 폴더에 남는다.
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
import office_editor  # noqa: E402  (편집기 상태 · 재시작)

REPO = board.REPO
NIGHT = board.BOARD.parent / 'night'
RESULTS = board.BOARD.parent / 'results'
WT_ROOT = REPO.parent / 'Oheangbu-night'
STALE_MIN = 20 * 60          # 남은 시간이 이보다 적으면 새 바퀴를 시작하지 않는다
CYCLE_TIMEOUT = 45 * 60
FULL_TIMEOUT = 3 * 3600      # full 바퀴 하나(구현 · Play 포함) 상한
LIMIT_WAIT = 30 * 60         # 한도에 걸리면 이만큼 기다렸다 다시
NO_FLAGS = 0x08000000 if os.name == 'nt' else 0


def keep_awake(on):
    """ES_CONTINUOUS | ES_SYSTEM_REQUIRED — 도는 동안 절전으로 들어가지 않게(화면은 꺼져도 된다)."""
    if os.name == 'nt':
        ctypes.windll.kernel32.SetThreadExecutionState(0x80000000 | (0x00000001 if on else 0))


def parse_until(s):
    """'07:30'(다음 그 시각) · '2026-10-31T07:30'(날짜 넘어 여러 날) · '+48h'."""
    s = (s or '07:30').strip()
    if s.startswith('+') and s.endswith('h'):
        return time.time() + float(s[1:-1]) * 3600
    if len(s) > 5:
        return datetime.fromisoformat(s.replace(' ', 'T')).timestamp()
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
                  'mode': args.get('mode') or 'full', 'max_cycles': int(args.get('cycles') or 300), 'budget': float(args.get('budget') or 0),
                  'codex_cap': float(args.get('codex_cap') or 100), 'session_id': '', 'restarts': [], 'waits': 0, 'instruction': args.get('instruction') or '질문 없이 권장안대로 작업을 계속 진행',
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
    def job(self, model, role, title, prompt, write=False, cwd=None, max_turns=None, mode=None, resume=None, timeout=CYCLE_TIMEOUT):
        run = office.runner_of(model)
        jid = 'n%s_%s' % (self.id[-4:], secrets.token_hex(2))
        job = {'id': jid, 'model': model, 'role': role, 'task': title[:80], 'lane': '야간', 'prompt': prompt, 'write': bool(write),
               'mode': mode or ('write' if write else 'read'), 'resume': resume,
               'runner': run, 'cwd': str(cwd) if cwd else None, 'max_turns': max_turns}
        office.JOBS.mkdir(parents=True, exist_ok=True)
        jf = office.JOBS / (jid + '.json'); jf.write_text(json.dumps(job, ensure_ascii=False, indent=1), encoding='utf-8')
        p = subprocess.Popen([sys.executable, str(HERE / 'office_worker.py'), str(jf)], cwd=REPO, creationflags=NO_FLAGS,
                             stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        t0 = time.time()
        while p.poll() is None:
            time.sleep(5)
            if time.time() - t0 > timeout or (self.stopped() and (self.dir / 'STOP_NOW').exists()):
                subprocess.run(['taskkill', '/PID', str(p.pid), '/T', '/F'], capture_output=True)
                board.append({'ev': 'fail', 'id': jid, 'text': '야간 감독: %d분 넘어 중지' % (timeout // 60)})
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

    @staticmethod
    def auth_hit(text, meta):
        return meta.get('api_error_status') in (401, 403) or 'Failed to authenticate' in text[:400]

    def wait_limit(self, why):
        """한도 — 30분씩 기다린다. 종료 시각 · 중지가 오면 False."""
        self.s['waits'] += 1; self.save()
        self.note('한도 대기 %d분 — %s' % (LIMIT_WAIT // 60, why[:120]))
        t0 = time.time()
        while time.time() - t0 < LIMIT_WAIT:
            if self.stopped() or self.s['until'] - time.time() < STALE_MIN:
                return False
            time.sleep(30)
        return True

    def editor_check(self):
        """바퀴 사이: 편집기 메모리 · 크래시 · 일꾼 요청을 보고 필요하면 재시작(사용자 허락 2026-10-29)."""
        st = office_editor.status()
        why = st.get('reason')
        if not why:
            return
        self.note('편집기 재시작 — ' + why)
        ok, out = office_editor.restart(why)
        self.s['restarts'].append({'t': time.time(), 'why': why, 'ok': ok, 'out': out.splitlines()[-1:] if out else []})
        self.save()
        self.note('편집기 재시작 %s — %s' % ('OK' if ok else '실패', (out.splitlines() or [''])[-1][:120]))

    # ---------------------------------------------------------------- full: 한 세션을 밤새 이어 가기
    FULL_FIRST = ('[야간 근무 시작 — 너는 이 밤의 총괄(메인 세션)이다]\n사용자는 자고 있어 질문할 수 없다. 사용자 지시: «%s»\n'
                  '종료 %s(남은 %s). 이 대화는 바퀴 단위로 이어진다 — 매 바퀴 감독이 "다음 바퀴"를 보낸다.\n'
                  '한 바퀴: Docs/PROJECT_STATUS.md · 최근 Docs/Handoff · 작업판(python Tools/AgentBoard/board.py list)을 보고 다음 권장 작업 하나를 골라 '
                  '끝까지 한다 — 구현 → 컴파일 → 필요하면 Play/캡처 검증 → 로컬 커밋(일반 커밋, git add -A 금지) → 필요하면 PROJECT_STATUS 갱신.\n'
                  '하위 에이전트(Agent, 일반 구현은 sonnet)와 Codex(Bash로 codex exec -m gpt-6-astra / gpt-6.1-sol — 남은 토큰 써도 된다)를 써도 된다.\n'
                  '낮 세션이 진행 중인 작업과 겹치지 않는다:\n%s\n'
                  '진행 중 단계마다 `python Tools/AgentBoard/board.py note %s "…"` 로 짧게 남긴다(사무실 말풍선에 뜬다).\n'
                  '바퀴를 마칠 때 답의 마지막 줄은 JSON 한 줄: {"title": "30자 제목", "summary": "한 일 · 결과물 위치", "verified": "검증한 것 · 못 한 것", '
                  '"commits": ["해시"], "next": "다음에 할 일", "stop": false}  — 더 할 안전한 일이 없으면 stop을 true로.\n이제 1바퀴를 시작해라.')
    FULL_NEXT = ('[다음 바퀴 %d — 남은 %s]\n지난 바퀴에 이어서 다음 권장 작업 하나를 끝까지 해라. %s같은 JSON 한 줄로 끝낸다.')

    def cycle_full(self, n):
        left = '%.1f시간' % ((self.s['until'] - time.time()) / 3600)
        if not self.s['session_id']:
            prompt = self.FULL_FIRST % (self.s['instruction'], time.strftime('%m-%d %H:%M', time.localtime(self.s['until'])), left,
                                        self.daytime_running(), self.row)
        else:
            extra = '이전 두 바퀴가 실패했다 — 다른 작업으로 바꿔라. ' if self.fails >= 2 else ''
            if self.s['restarts'] and time.time() - self.s['restarts'][-1]['t'] < 3600:
                extra += 'Unity 편집기를 방금 재시작했다(%s). ' % self.s['restarts'][-1]['why']
            prompt = self.FULL_NEXT % (n, left, extra)
        jid, ok, text, meta = self.job('opus', '야간 근무', '야간 %d바퀴' % n, prompt, mode='full', resume=self.s['session_id'] or None,
                                       timeout=FULL_TIMEOUT)
        if meta.get('session_id') and not self.s['session_id']:
            self.s['session_id'] = meta['session_id']; self.save()
            self.note('야간 세션 %s' % meta['session_id'])
        if self.auth_hit(text, meta):
            return 'auth', '인증 오류 — 터미널에서 claude → /login 필요: ' + text[:120]
        if self.limit_hit(text, meta):
            return 'limit', text[:160]
        info = {}
        m = re.findall(r'\{[^{}]*"title"[^{}]*\}', text, re.S)
        if m:
            try:
                info = json.loads(m[-1])
            except ValueError:
                info = {}
        title = str(info.get('title') or '야간 %d바퀴' % n)[:60]
        summary = str(info.get('summary') or next((l.strip() for l in text.splitlines() if l.strip()), ''))[:400]
        out = self.dir / ('cycle_%02d.md' % n)
        out.write_text('# %d바퀴 — %s\n\n- 작업판 %s · 세션 %s\n- 검증: %s\n- 커밋: %s\n- 다음: %s\n\n## 답\n\n%s\n'
                       % (n, title, jid, self.s['session_id'], info.get('verified', ''), ', '.join(info.get('commits') or []), info.get('next', ''), text),
                       encoding='utf-8')
        self.s['cycles'].append({'n': n, 'title': title, 'assignee': 'opus', 'ok': ok, 'job': jid, 'write': True,
                                 'commit': ', '.join(info.get('commits') or [])[:80], 'why': '', 'summary': summary,
                                 'verified': str(info.get('verified', ''))[:300], 'file': str(out)})
        self.save()
        if info.get('stop') is True:
            return 'done', '감독 세션 판단으로 종료: ' + summary[:160]
        return ('ok' if ok else 'fail'), summary

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
        head = ('야간 근무 %s · %s – %s · 모드 %s · %d바퀴 · Claude 환산 $%.2f · 끝난 이유: %s%s%s%s'
                % (self.id, time.strftime('%m-%d %H:%M', time.localtime(self.s['started'])), time.strftime('%m-%d %H:%M'), self.s['mode'], len(self.s['cycles']),
                   self.s['spent'], self.s['stop_reason'], ('\n브랜치 %s (워크트리 %s, 기준 %s)' % (self.s['branch'], self.s['worktree'], self.s['base'])) if self.s['branch'] else '',
                   ('\n세션 %s — 이어서 대화: claude --resume %s' % (self.s['session_id'], self.s['session_id'])) if self.s.get('session_id') else '',
                   ('\n편집기 재시작 %d번 · 한도 대기 %d번' % (len(self.s.get('restarts', [])), self.s.get('waits', 0))) if self.s.get('restarts') or self.s.get('waits') else ''))
        prompt = ('사용자가 자는 동안 한 야간 근무 기록이다. 사용자가 일어나서 읽을 아침 보고서를 한국어 마크다운으로 써라.\n'
                  '순서: 한 줄 요약 → 한 일(바퀴별 결과물과 위치) → 확인이 필요한 것(미검증 수정 · 실패 · 사용자 결정 대기) → 다음 권장.\n'
                  '과장하지 말고, 검증 안 된 것은 미검증이라고 쓴다. 파일은 읽지 말고 아래 기록만 써라.\n\n%s\n\n%s\n\n변경 통계:\n%s'
                  % (head, '\n\n'.join(parts) or '(한 바퀴도 못 돌았다)', diff or '(없음)'))
        if self.s['mode'] == 'full' and self.s['session_id']:
            prompt = ('[야간 근무 끝 — %s] 사용자가 일어나서 읽을 아침 보고서를 한국어 마크다운으로 써라. 순서: 한 줄 요약 → 한 일(바퀴별 결과물 · 커밋 · 위치) → '
                      '확인이 필요한 것(미검증 · 실패 · 권장안으로 진행한 결정) → 다음 권장. 과장하지 말고 검증 안 된 것은 미검증이라고 쓴다. '
                      '이번 답은 보고서 본문만(JSON 없이). 참고 기록:\n%s' % (self.s['stop_reason'], head))
            _, ok, text, meta = self.job('opus', '아침 보고', '야간 근무 아침 보고서', prompt, mode='full', resume=self.s['session_id'], timeout=1800)
        else:
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
            fresh = cl.get('asof') and time.time() - cl['asof'] < 2 * 3600
            if weekly and weekly[0] >= 98 and fresh:
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
                if self.s['budget'] and self.s['spent'] >= self.s['budget']:
                    self.s['stop_reason'] = 'Claude 환산 예산 $%.0f 소진' % self.s['budget']; break
                full = self.s['mode'] == 'full'
                if full:
                    self.editor_check()
                n += 1
                kind, msg = self.cycle_full(n) if full else self.cycle(n)
                self.note('%d바퀴 %s — %s' % (n, {'ok': '끝', 'fail': '실패', 'done': '종료', 'limit': '한도', 'auth': '인증 오류'}[kind], msg[:160]))
                if kind == 'auth':
                    self.s['stop_reason'] = msg[:200]; break
                if kind == 'limit':
                    if not full:
                        self.s['stop_reason'] = msg[:200]; break
                    n -= 1                                  # 같은 바퀴를 다시
                    if not self.wait_limit(msg):
                        self.s['stop_reason'] = '한도 대기 중 종료 시각 · 중지'; break
                    continue
                if kind == 'done':
                    self.s['stop_reason'] = msg[:200]; break
                self.fails = self.fails + 1 if kind == 'fail' else 0
                if self.fails >= (6 if full else 2):
                    self.s['stop_reason'] = '연속 실패 %d번' % self.fails; break
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


def request_stop(now=False):
    """STOP = 지금 바퀴를 마치고 보고서. now면 도는 일꾼도 끊는다(STOP_NOW)."""
    cur = latest()
    if not cur or cur.get('status') not in ('running',):
        return 400, {'error': '도는 야간 근무가 없다'}
    (NIGHT / cur['id'] / 'STOP').write_text('stop', encoding='utf-8')
    if now:
        (NIGHT / cur['id'] / 'STOP_NOW').write_text('stop', encoding='utf-8')
    return 200, {'ok': True}


def open_in_app():
    """마지막 야간 세션을 데스크톱 앱에서 연다(사용자가 단추를 눌렀을 때만)."""
    cur = latest()
    sid = (cur or {}).get('session_id')
    if not sid:
        return 400, {'error': '이어 갈 야간 세션이 없다'}
    subprocess.Popen([office.worker.exe('claude'), '--resume', sid, '--desktop'], cwd=REPO, env=office.worker.clean_env(), creationflags=NO_FLAGS)
    return 200, {'ok': True, 'session_id': sid}


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
