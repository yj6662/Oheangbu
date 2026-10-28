#!/usr/bin/env python3
"""작업판 사무실 일꾼 (2026-10-29) — office.py가 받은 업무 지시 하나를 실행하며 board.jsonl에 착수 · 메모 · 완료/실패를 적는다.

  python Tools/AgentBoard/office_worker.py Art/AgentBoard/jobs/<id>.json

일 파일(job): {id, model, role, task, lane, prompt, write, runner:{kind, model?, argv?, estimate?}}
- 지시문은 argv가 아니라 stdin으로 넘긴다(cmd.exe가 & | > % 를 해석하지 않게).
- claude: 읽기 모드 = Read · Grep · Glob · 웹만, 수정 모드 = + Edit · Write(acceptEdits). Bash는 둘 다 막는다.
- codex: 읽기 모드 = -s read-only, 수정 모드 = -s workspace-write.
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
    tools = ['Read', 'Grep', 'Glob', 'WebSearch', 'WebFetch'] + (['Edit', 'Write'] if job.get('write') else [])
    sysf = RESULTS / (job['id'] + '.system.txt'); sysf.write_text(SYSTEM, encoding='utf-8')   # 한글은 .cmd 인자로 넘기지 않는다
    cmd = [exe('claude'), '-p', '--model', job['runner']['model'], '--output-format', 'stream-json', '--verbose',
           '--append-system-prompt-file', str(sysf), '--allowedTools', *tools, '--disallowedTools', 'Bash', 'NotebookEdit']
    if job.get('write'):
        cmd[cmd.index('--allowedTools'):cmd.index('--allowedTools')] = ['--permission-mode', 'acceptEdits']
    p = subprocess.Popen(cmd, cwd=REPO, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, env=clean_env())
    p.stdin.write(job['prompt'].encode('utf-8')); p.stdin.close()
    result, ok, last = '', False, 0.0
    for raw in p.stdout:
        line = raw.decode('utf-8', 'replace').strip()
        try:
            ev = json.loads(line)
        except ValueError:
            log.write(line + '\n'); log.flush(); continue
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
    code = p.wait()
    return ok and code == 0, result or '(결과 글 없음 — 종료 코드 %d)' % code


# ------------------------------------------------------------------ codex
def run_codex(job, log):
    out = RESULTS / (job['id'] + '.md')
    cmd = [exe('codex'), 'exec', '-m', job['runner']['model'], '-s', 'workspace-write' if job.get('write') else 'read-only',
           '-C', str(REPO), '--skip-git-repo-check', '-o', str(out), '-']
    p = subprocess.Popen(cmd, cwd=REPO, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, env=clean_env())
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
    note(job, '지시 받음' + (' · 파일 수정 허용' if job.get('write') else ' · 읽기 전용'))
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
    first = next((l for l in text.splitlines() if l.strip()), '')
    finish(job, ok, first[:200])


if __name__ == '__main__':
    main()
