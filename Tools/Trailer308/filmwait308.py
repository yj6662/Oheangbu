"""Trailer 4: start one Presentation297 film and wait for it - the trailer's own wait loop.

  python Tools/Trailer308/filmwait308.py <spec.json> [--limit S] [--poll S]
  python Tools/Trailer308/filmwait308.py --selftest          the loop against scripted replies (no editor)

Why not Tools/Unity/Stage308_ops/filmwait.py: the #308 FilmSpec.settle keeps film-state.json at status "settling" before
"running" (stage Presentation297.cs StartFilm / FilmTick), and that script breaks out at the first status that is not
"running" -> exit 1 after 3 s, the film left running, the next `film:` refused with "A film is already running".
Here:   film-status first: a film still settling|running (a leftover of a failed attempt) is film-stop'ped
     -> film:<spec>  ("A film is already running" -> film-stop, 3 s, one retry)
     -> every POLL s film-status: settling | running = keep waiting; done = exit 0; stopped | error | other = exit 1;
        another name = not started (exit 2); at --limit the film is stopped and the status is TIMEOUT (exit 1).
Prints one line: <take> <status> <frames>/<total> [| <note>].  The queue conflict ("Unconsumed polish command") is printed
as the status so shoot.sh can stop the round (exit 8).
"""
import contextlib
import io
import json
import sys
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'Tools/Unity'))
P = 'Oheangbu.EditorTools.WorldMacro.'
WAITING = ('settling', 'running')
QUEUE_CONFLICT = 'Unconsumed polish command'


def editor_run(tool, command, timeout=120):
    """one queue call; the reply text, or 'ERROR: ...' (never a traceback - the caller judges the text)"""
    from playtest_polish import call   # noqa: E402  (Tools/Unity/playtest_polish.py)
    try:
        with contextlib.redirect_stdout(io.StringIO()):
            r = call(P + tool, 'Run', command, timeout)
        return str(r.get('result') or r.get('error') or '')
    except Exception as e:                           # RuntimeError (status != COMPLETE, queue conflict) / TimeoutError
        return 'ERROR: ' + str(e)


def parse_state(raw):
    try:
        s = json.loads(raw)
        return s if isinstance(s, dict) else {'status': 'unreadable: ' + raw[:80]}
    except Exception:
        return {'status': 'unreadable: ' + raw[:80]}


def wait_film(spec, name, limit, poll, run, sleep=time.sleep, clock=time.time):
    """(state dict, note) - `run(tool, command)` is the editor call (swapped in by --selftest)"""
    notes = []
    # 1. a film still up from a failed attempt would make film: throw - stop it first
    raw = run('Presentation297', 'film-status')
    if QUEUE_CONFLICT in raw: return {'status': QUEUE_CONFLICT}, raw[:120]
    before = parse_state(raw)
    if before.get('status') in WAITING:
        run('Presentation297', 'film-stop'); sleep(3); notes.append('stopped leftover %s (%s)' % (before.get('name'), before.get('status')))
    # 2. start, one retry when the editor still says a film is running
    started = run('Presentation297', 'film:' + str(spec))
    if QUEUE_CONFLICT in started: return {'status': QUEUE_CONFLICT}, started[:120]
    if 'already running' in started:
        run('Presentation297', 'film-stop'); sleep(3); notes.append('film-stop before start')
        started = run('Presentation297', 'film:' + str(spec))
    if started.startswith('ERROR') or 'refused' in started:
        return {'status': 'not started'}, started[:160]
    # 3. poll: settling and running both mean "wait"
    state, t0 = {}, clock()
    while clock() - t0 < limit:
        sleep(poll)
        raw = run('Presentation297', 'film-status')
        if QUEUE_CONFLICT in raw: return {'status': QUEUE_CONFLICT}, raw[:120]
        state = parse_state(raw)
        if state.get('name') != name:
            return {'status': 'not started'}, ('state is for %r; start reply: %s' % (state.get('name'), started[:100]))
        if state.get('status') not in WAITING: break
    else:
        run('Presentation297', 'film-stop')
        notes.append('limit %.0f s (last %s %s/%s, film-stop sent)' % (limit, state.get('status'), state.get('frames', 0), state.get('total', 0)))
        state['status'] = 'TIMEOUT'
    settle = [c for c in (state.get('commits') or []) if str(c).startswith('settle ')]
    if settle: notes.append(settle[-1])
    return state, '; '.join(notes)


def selftest():
    """scripted editors: each case is a list of replies per (tool, command) in call order"""
    class Fake:
        def __init__(self, script): self.script = list(script); self.calls = []
        def __call__(self, tool, command):
            self.calls.append(command.split(':')[0])
            for i, (match, reply) in enumerate(self.script):
                if command.startswith(match): self.script.pop(i); return reply
            return json.dumps({'name': 'x', 'status': 'done', 'frames': 1, 'total': 1})
    S = lambda name, status, f=0, t=241, commits=None: json.dumps({'name': name, 'status': status, 'frames': f, 'total': t, 'commits': commits or []})
    fails = 0
    def case(label, script, want_status, want_calls=None):
        nonlocal fails
        fake = Fake(script)
        st, note = wait_film('spec.json', 'p_hook', limit=100, poll=0, run=fake, sleep=lambda s: None, clock=_Clock())
        ok = st.get('status') == want_status and (want_calls is None or all(c in fake.calls for c in want_calls))
        fails += 0 if ok else 1
        print('%-5s %-28s status %-26s calls %s%s' % ('ok' if ok else 'FAIL', label, st.get('status'), fake.calls, (' | ' + note) if note else ''))
    class _Clock:
        def __init__(self): self.t = 0.0
        def __call__(self): self.t += 7.0; return self.t      # every poll "takes" 7 s -> 100 s limit = ~14 polls
    settle_commit = ['settle quiet after 6.2 s, loaders 1']
    # A: clean start, settling twice, running, done
    case('settle -> running -> done', [('film-status', 'no film'), ('film', 'film p_hook started'),
                                       ('film-status', S('p_hook', 'settling')), ('film-status', S('p_hook', 'settling')),
                                       ('film-status', S('p_hook', 'running', 10)), ('film-status', S('p_hook', 'done', 241, 241, settle_commit))], 'done', ['film'])
    # B: a leftover film of the failed attempt is still settling -> film-stop first
    case('leftover settling -> stop', [('film-status', S('p_hook', 'settling')), ('film-stop', S('p_hook', 'stopped')), ('film', 'started'),
                                       ('film-status', S('p_hook', 'running', 3)), ('film-status', S('p_hook', 'done', 241, 241))], 'done', ['film-stop', 'film'])
    # C: film: refused "already running" -> film-stop -> retry
    case('already running -> retry', [('film-status', 'no film'), ('film', 'ERROR: A film is already running'), ('film-stop', S('p_hook', 'stopped')),
                                      ('film', 'started'), ('film-status', S('p_hook', 'running', 3)), ('film-status', S('p_hook', 'done', 241, 241))], 'done', ['film-stop'])
    # D: never leaves settling -> TIMEOUT with film-stop
    case('settling forever -> TIMEOUT', [('film-status', 'no film'), ('film', 'started')] + [('film-status', S('p_hook', 'settling'))] * 40, 'TIMEOUT', ['film-stop'])
    # E: stopped by the editor (error) -> exit 1 status
    case('stopped -> fail', [('film-status', 'no film'), ('film', 'started'), ('film-status', S('p_hook', 'running', 50)), ('film-status', S('p_hook', 'stopped', 50))], 'stopped')
    # F: queue conflict
    case('queue conflict', [('film-status', 'ERROR: ' + QUEUE_CONFLICT + '; inspect before retrying.')], QUEUE_CONFLICT)
    print('selftest %s' % ('PASS' if fails == 0 else 'FAIL %d' % fails))
    return 0 if fails == 0 else 1


def main():
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
    if '--selftest' in sys.argv: return selftest()
    if len(sys.argv) < 2 or sys.argv[1].startswith('--'): print(__doc__); return 2
    spec = Path(sys.argv[1]).resolve()
    name = json.loads(spec.read_text(encoding='utf-8'))['name']
    limit = float(sys.argv[sys.argv.index('--limit') + 1]) if '--limit' in sys.argv else 420.0
    poll = float(sys.argv[sys.argv.index('--poll') + 1]) if '--poll' in sys.argv else 3.0
    state, note = wait_film(spec, name, limit, poll, editor_run)
    print(name, state.get('status'), '%s/%s' % (state.get('frames', 0), state.get('total', 0)), ('| ' + note) if note else '')
    if state.get('status') == 'done': return 0
    return 2 if state.get('status') == 'not started' else 1


if __name__ == '__main__':
    sys.exit(main())
