#!/usr/bin/env python3
"""작업판 사무실 일꾼 (2026-10-29) — office.py가 받은 업무 지시 하나를 실행하며 board.jsonl에 착수 · 메모 · 완료/실패를 적는다.

  python Tools/AgentBoard/office_worker.py Art/AgentBoard/jobs/<id>.json

일 파일(job): {id, model, role, task, lane, prompt, mode(read|write|full), runner:{kind, model?, argv?, estimate?}, cwd?, resume?, max_turns?}
- 지시문은 argv가 아니라 stdin으로 넘긴다(cmd.exe가 & | > % 를 해석하지 않게).
- claude: read = Read · Grep · Glob · 웹만 / write = + Edit · Write(acceptEdits), Bash 없음 /
  full('이 세션처럼', 사용자 2026-10-29) = --permission-mode auto(데스크톱 메인 세션과 같은 자동 판정기) + Bash + Unity MCP(Oheangbu/.mcp.json),
  git push만 규칙으로 막고 삭제 · 유료 API 금지는 지시문으로. resume=세션 id면 그 세션을 이어 간다(야간 근무).
- codex: read = -s read-only, write · full = -s workspace-write.
- 편집기 재시작은 일꾼이 직접 하지 않는다 — RESTART_EDITOR 요청 파일을 쓰면 야간 감독(또는 사무실 단추)이 life.ps1로 한다.
- meshy · recraft(유료): office.py가 확인 · 상한을 본 뒤에만 일 파일을 만든다. 여기서는 원장에 먼저 적고 POST, 자동 재시도 없음.
결과: Art/AgentBoard/results/<id>.md(+ 받은 파일), 로그: Art/AgentBoard/logs/<id>.log
"""
import base64, json, os, re, shutil, subprocess, sys, time, urllib.error, urllib.request
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import board  # noqa: E402

REPO = board.REPO
ROOT = board.BOARD.parent
RESULTS = ROOT / 'results'
LOGS = ROOT / 'logs'
LEDGER = ROOT / 'vendor-ledger.json'
SYSTEM = ('너는 미적 스튜디오(오행부 개발) 작업판 사무실에서 업무 지시를 받은 일꾼이다. 한국어로 답한다. '
          '같은 리포를 다른 세션이 동시에 쓰고 있으니 git 명령 · 대량 수정 · 지시 범위 밖 파일 수정은 하지 않는다. '
          '끝에 결과를 세 줄 이내로 요약한다.')
RESTART_REQ = ROOT / 'RESTART_EDITOR'
SYSTEM_FULL = ('너는 미적 스튜디오(오행부 개발) 작업판 사무실에서 업무 지시를 받은 일꾼이다. 사용자가 데스크톱 앱에서 쓰는 메인 세션처럼 일한다 — '
               '같은 권한(auto), Bash, Unity 편집기 MCP(ai-game-developer)를 쓸 수 있다. 한국어로 답한다. CLAUDE.md와 자동 메모리 규칙을 따른다.\n'
               '지켜야 할 것(사용자 2026-10-29): 파일 삭제 금지(필요하면 보고서에 제안만) · git push 금지(로컬 커밋은 일반 커밋으로, git add -A 금지) · '
               '유료 API(Meshy · Recraft · Suno) 금지 · 큰 축(스토리 구조 · vol.2 · EA · 오의 한 획) 유지. 결정이 필요하면 권장안으로 진행하고 그렇게 했다고 기록한다.\n'
               '같은 리포 · 같은 Unity 편집기를 다른 세션이 쓸 수 있다. GPU를 많이 쓰는 단계(Play · 캡처 · Blender)는 '
               '`python Tools/resource_guard.py --gpu-run <라벨> -- <명령>`으로 하나씩.\n'
               'Unity 편집기를 직접 닫거나 죽이지 않는다. 재시작이 필요하면(전용 메모리 30 GB 초과 · 크래시 · 메모리 부족) '
               '파일 %s 에 이유를 한 줄 쓰고 이번 일을 마친다 — 감독이 바퀴 사이에 재시작한다(사용자 허락 2026-10-29).\n'
               '끝에 결과를 세 줄 이내로 요약하고, 검증하지 못한 것은 미검증이라고 쓴다.' % RESTART_REQ)
MCP_CONFIG = REPO / 'Oheangbu' / '.mcp.json'


def job_mode(job):
    return job.get('mode') or ('write' if job.get('write') else 'read')


def note(job, text):
    board.append({'ev': 'note', 'id': job['id'], 'text': ' '.join(str(text).split())[:300]})


def finish(job, ok, text):
    board.append({'ev': 'done' if ok else 'fail', 'id': job['id'], 'text': ' '.join(str(text).split())[:300]})


def clean_env():
    """이 세션 안에서 띄워도 중첩 세션으로 막히지 않게 Claude 세션 변수를 뺀다."""
    return {k: v for k, v in os.environ.items() if not k.startswith('CLAUDE') or k == 'CLAUDE_CONFIG_DIR'}


def exe(name):
    return shutil.which(name) or name


def env_key(name):
    v = os.environ.get(name)
    if v:
        return v
    for f in (REPO / '.env', REPO / 'Oheangbu' / '.env'):
        try:
            for line in f.read_text(encoding='utf-8-sig').splitlines():
                if line.startswith(name + '='):
                    return line.split('=', 1)[1].strip().strip('"').strip("'")
        except OSError:
            pass
    return None


# ------------------------------------------------------------------ claude
def run_claude(job, log):
    mode = job_mode(job)
    sysf = RESULTS / (job['id'] + '.system.txt')   # 한글은 .cmd 인자로 넘기지 않는다
    sysf.write_text(SYSTEM_FULL if mode == 'full' else SYSTEM, encoding='utf-8')
    cmd = [exe('claude'), '-p', '--model', job['runner']['model'], '--output-format', 'stream-json', '--verbose', '--append-system-prompt-file', str(sysf)]
    if mode == 'full':
        cmd += ['--permission-mode', 'auto', '--disallowedTools', 'Bash(git push:*)']
        if MCP_CONFIG.exists():
            cmd += ['--mcp-config', str(MCP_CONFIG)]
    else:
        tools = ['Read', 'Grep', 'Glob', 'WebSearch', 'WebFetch'] + (['Edit', 'Write'] if mode == 'write' else [])
        cmd += (['--permission-mode', 'acceptEdits'] if mode == 'write' else []) + ['--allowedTools', *tools, '--disallowedTools', 'Bash', 'NotebookEdit']
    if job.get('resume'):
        cmd += ['--resume', job['resume']]
    if job.get('max_turns'):
        cmd += ['--max-turns', str(int(job['max_turns']))]
    p = subprocess.Popen(cmd, cwd=job.get('cwd') or REPO, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, env=clean_env())
    p.stdin.write(job['prompt'].encode('utf-8')); p.stdin.close()
    result, ok, last = '', False, 0.0
    for raw in p.stdout:
        line = raw.decode('utf-8', 'replace').strip()
        try:
            ev = json.loads(line)
        except ValueError:
            log.write(line + '\n'); log.flush(); continue
        if ev.get('type') == 'system' and ev.get('subtype') == 'init' and ev.get('session_id'):
            job.setdefault('_meta', {})['session_id'] = ev['session_id']
        if ev.get('type') == 'assistant':
            for c in ev.get('message', {}).get('content', []):
                if c.get('type') == 'tool_use':
                    arg = c.get('input', {})
                    what = arg.get('file_path') or arg.get('pattern') or arg.get('query') or arg.get('url') or ''
                    log.write('· %s %s\n' % (c['name'], what)); log.flush()
                    if time.time() - last > 4:
                        note(job, '%s %s' % (c['name'], Path(what).name if os.sep in str(what) or '/' in str(what) else what)); last = time.time()
                elif c.get('type') == 'text' and c.get('text', '').strip():
                    log.write(c['text'].strip() + '\n'); log.flush()
        elif ev.get('type') == 'result':
            result, ok = ev.get('result') or '', not ev.get('is_error') and ev.get('subtype') == 'success'
            job.setdefault('_meta', {}).update(cost_usd=ev.get('total_cost_usd'), api_error_status=ev.get('api_error_status'), subtype=ev.get('subtype'),
                                               num_turns=ev.get('num_turns'), session_id=ev.get('session_id') or job.get('_meta', {}).get('session_id'))
            # 야간 감독이 예산 · 한도 오류 · 이어 갈 세션 id를 본다
    code = p.wait()
    return ok and code == 0, result or '(결과 글 없음 — 종료 코드 %d)' % code


# ------------------------------------------------------------------ codex
def run_codex(job, log):
    out = RESULTS / (job['id'] + '.md')
    cwd = job.get('cwd') or REPO
    cmd = [exe('codex'), 'exec', '-m', job['runner']['model'], '-s', 'workspace-write' if job_mode(job) in ('write', 'full') else 'read-only',
           '-C', str(cwd), '--skip-git-repo-check', '-o', str(out), '-']
    p = subprocess.Popen(cmd, cwd=cwd, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, env=clean_env())
    p.stdin.write((SYSTEM + '\n\n' + job['prompt']).encode('utf-8')); p.stdin.close()
    tail, last = '', 0.0
    for raw in p.stdout:
        line = raw.decode('utf-8', 'replace').rstrip()
        log.write(line + '\n'); log.flush()
        if line.strip() and not re.match(r'^\d{4}-\d\d-\d\dT\S+\s+(ERROR|WARN|INFO|DEBUG)\b', line.strip()):   # codex 내부 로그는 말풍선에 안 올린다
            tail = line.strip()
        if tail and time.time() - last > 15:
            note(job, tail[:160]); last = time.time()
    code = p.wait()
    text = out.read_text(encoding='utf-8', errors='replace') if out.exists() else ''
    return code == 0, text or '(결과 글 없음 — 종료 코드 %d)' % code


# ------------------------------------------------------------------ command (staff.json의 임의 API 에이전트)
def run_command(job, log):
    argv = [a.replace('{repo}', str(REPO)).replace('{id}', job['id']) for a in job['runner']['argv']]
    p = subprocess.Popen(argv, cwd=REPO, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, env=clean_env())
    p.stdin.write(job['prompt'].encode('utf-8')); p.stdin.close()
    lines = []
    for raw in p.stdout:
        line = raw.decode('utf-8', 'replace').rstrip(); log.write(line + '\n'); log.flush()
        if line.strip():
            lines.append(line.strip()); note(job, line.strip()[:160])
    code = p.wait()
    return code == 0, '\n'.join(lines[-20:]) or '종료 코드 %d' % code


# ------------------------------------------------------------------ paid vendors
def ledger_load():
    try:
        return json.loads(LEDGER.read_text(encoding='utf-8'))
    except (OSError, ValueError):
        return {'schema': 1, 'note': '작업판 사무실에서 건 유료 요청. POST 전에 예약을 적고, 자동 재시도는 없다.', 'entries': []}


def ledger_save(d):
    LEDGER.parent.mkdir(parents=True, exist_ok=True)
    tmp = LEDGER.with_suffix('.writing'); tmp.write_text(json.dumps(d, ensure_ascii=False, indent=2), encoding='utf-8'); tmp.replace(LEDGER)


def month_spent(vendor, d=None):
    m = time.strftime('%Y-%m')
    return sum((e.get('consumed') if e.get('consumed') is not None else e.get('reserved', 0)) for e in (d or ledger_load())['entries']
               if e['vendor'] == vendor and e['created'].startswith(m) and e['status'] not in ('NOT_SENT',))


def http(method, url, key, payload=None, timeout=120):
    req = urllib.request.Request(url, method=method, data=None if payload is None else json.dumps(payload).encode(),
                                 headers={'Authorization': 'Bearer ' + key, 'Content-Type': 'application/json'})
    with urllib.request.urlopen(req, timeout=timeout) as r:
        return json.load(r)


def balance(vendor):
    """잔액 조회(무료 GET). 실패하면 None."""
    try:
        if vendor == 'meshy':
            return http('GET', 'https://api.meshy.ai/openapi/v1/balance', env_key('MESHY_API_KEY'), timeout=15).get('balance')
        if vendor == 'recraft':
            return http('GET', 'https://external.api.recraft.ai/v1/users/me', env_key('RECRAFT_API_KEY'), timeout=15).get('credits')
    except Exception:
        return None


def reserve(job, vendor, config):
    d = ledger_load()
    e = {'id': job['id'], 'vendor': vendor, 'created': time.strftime('%Y-%m-%dT%H:%M:%S'), 'prompt': job['prompt'][:800], 'config': config,
         'reserved': job['runner'].get('estimate', 0), 'consumed': None, 'status': 'POST_RESERVED', 'balance_before': balance(vendor)}
    d['entries'].append(e); ledger_save(d)
    return d, e


def run_meshy(job, log):
    key = env_key('MESHY_API_KEY')
    if not key:
        return False, 'MESHY_API_KEY 없음'
    cfg = {'mode': 'preview', 'prompt': job['prompt'][:800], 'ai_model': 'meshy-7.1', 'should_remesh': True, 'target_polycount': 30000,
           'topology': 'triangle', 'target_formats': ['glb']}
    d, e = reserve(job, 'meshy', cfg)
    try:
        e['task_id'] = http('POST', 'https://api.meshy.ai/openapi/v2/text-to-3d', key, cfg)['result']; e['status'] = 'SUBMITTED'
    except Exception as x:
        e['status'] = 'SUBMISSION_UNCERTAIN_NO_RETRY'; ledger_save(d)
        return False, 'Meshy POST 실패(재시도 안 함 — 원장 확인): %s' % type(x).__name__
    ledger_save(d); note(job, 'Meshy 접수 %s' % e['task_id'])
    last, r = -1, {}
    while True:
        time.sleep(8)
        try:
            r = http('GET', 'https://api.meshy.ai/openapi/v2/text-to-3d/' + e['task_id'], key, timeout=30)
        except Exception as x:
            log.write('poll error %s\n' % type(x).__name__); log.flush(); continue
        log.write('%s %s%%\n' % (r.get('status'), r.get('progress'))); log.flush()
        if r.get('progress') != last:
            last = r.get('progress'); note(job, '모델링 %s%%' % last)
        if r.get('status') in ('SUCCEEDED', 'FAILED', 'CANCELED', 'EXPIRED'):
            break
    e.update(status=r.get('status'), consumed=r.get('consumed_credits', e['reserved'])); ledger_save(d)
    if r.get('status') != 'SUCCEEDED':
        return False, 'Meshy %s' % r.get('status')
    out = RESULTS / job['id']; out.mkdir(parents=True, exist_ok=True)
    got = []
    for fmt, url in (r.get('model_urls') or {}).items():
        if url:
            with urllib.request.urlopen(url, timeout=120) as resp:
                (out / ('model.' + fmt)).write_bytes(resp.read()); got.append(str(out / ('model.' + fmt)))
    if r.get('thumbnail_url'):
        with urllib.request.urlopen(r['thumbnail_url'], timeout=60) as resp:
            (out / 'thumbnail.png').write_bytes(resp.read())
    e['files'] = got; ledger_save(d)
    return True, '모델 %s · 크레딧 %s\n%s' % (e['task_id'], e['consumed'], '\n'.join(got))


def run_recraft(job, log):
    key = env_key('RECRAFT_API_KEY')
    if not key:
        return False, 'RECRAFT_API_KEY 없음'
    body = {'model': job['runner'].get('model', 'recraftv4_vector'), 'prompt': job['prompt'][:1000], 'n': 1, 'size': '1:1', 'response_format': 'b64_json'}
    d, e = reserve(job, 'recraft', body)
    note(job, 'Recraft 그리는 중')
    try:
        data = http('POST', 'https://external.api.recraft.ai/v1/images/generations', key, body, timeout=180)
    except Exception as x:
        e['status'] = 'SUBMISSION_UNCERTAIN_NO_RETRY'; ledger_save(d)
        return False, 'Recraft POST 실패(재시도 안 함): %s' % type(x).__name__
    item = data['data'][0]
    raw = base64.b64decode(item['b64_json']) if 'b64_json' in item else urllib.request.urlopen(item['url'], timeout=60).read()
    out = RESULTS / job['id']; out.mkdir(parents=True, exist_ok=True)
    f = out / ('image.svg' if b'<svg' in raw[:1000] else 'image.png'); f.write_bytes(raw)
    e.update(status='SUCCEEDED', consumed=data.get('credits', e['reserved']), files=[str(f)]); ledger_save(d)
    return True, '그림 %s · 사용 %s' % (f, e['consumed'])


RUN = {'claude': run_claude, 'codex': run_codex, 'command': run_command, 'meshy': run_meshy, 'recraft': run_recraft}


def main():
    job = json.loads(Path(sys.argv[1]).read_text(encoding='utf-8'))
    RESULTS.mkdir(parents=True, exist_ok=True); LOGS.mkdir(parents=True, exist_ok=True)
    log_path = LOGS / (job['id'] + '.log')
    board.append({'ev': 'start', 'id': job['id'], 'lane': job.get('lane', ''), 'model': job['model'], 'role': job.get('role', ''),
                  'task': job['task'], 'pid': os.getpid(), 'log': str(log_path)})
    note(job, '지시 받음 · ' + {'read': '읽기 전용', 'write': '파일 수정 허용', 'full': '이 세션처럼(auto · Bash · Unity)'}[job_mode(job)]
         + (' · 세션 이어 감' if job.get('resume') else ''))
    with open(log_path, 'w', encoding='utf-8') as log:
        log.write('지시: %s\n\n' % job['prompt'])
        try:
            ok, text = RUN[job['runner']['kind']](job, log)
        except Exception as x:
            ok, text = False, '%s: %s' % (type(x).__name__, x)
        log.write('\n== %s ==\n%s\n' % ('완료' if ok else '실패', text))
    res = RESULTS / (job['id'] + '.md')
    if job['runner']['kind'] != 'codex' or not res.exists():
        res.write_text(text, encoding='utf-8')
    (RESULTS / (job['id'] + '.meta.json')).write_text(json.dumps(dict(job.get('_meta') or {}, ok=ok), ensure_ascii=False), encoding='utf-8')
    first = next((l for l in text.splitlines() if l.strip()), '')
    finish(job, ok, first[:200])


if __name__ == '__main__':
    main()
