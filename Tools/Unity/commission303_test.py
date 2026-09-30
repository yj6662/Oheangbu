"""#303 follow-up tests through the editor queue (CHANGGUI_COMBAT_TEST_PLAN.md / CHANGGUI_COMBAT_TEST_IMPL.md).

usage:
  python Tools/Unity/commission303_test.py probe [main|folklore298]
  python Tools/Unity/commission303_test.py lab [마|나|가|마,나]            # Edit mode, W_Demo_Main open
  python Tools/Unity/commission303_test.py start [main|folklore298] [walk|stage]
  python Tools/Unity/commission303_test.py status | report | abort
  python Tools/Unity/commission303_test.py wait [seconds=1500]            # poll status until the run is no longer active
  python Tools/Unity/commission303_test.py escort-audit [main|folklore298|architecture296]
  python Tools/Unity/commission303_test.py escort-start [full]
  python Tools/Unity/commission303_test.py escort-status | escort-report | escort-abort
  python Tools/Unity/commission303_test.py escort-wait [seconds=2400]
  python Tools/Unity/commission303_test.py fix-plan [main|folklore298|architecture296]
  python Tools/Unity/commission303_test.py fix-move <main|folklore298|architecture296> [allow-offmesh]   # F1, writes that scene
  python Tools/Unity/commission303_test.py fix-apply-all [allow-offmesh]                                  # F1 on all three scenes
  python Tools/Unity/commission303_test.py plot <runId>                    # CombatTest/<runId>/trail.png (Pillow)
Add --shared-ok to proceed although another session queued a request in the last 10 minutes.
Output: Art/World/Compact/Rebuild/Roadside303/{CombatTest,EscortTest,EscortFix}
"""
import contextlib
import io
import json
import sys
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'Tools/Unity'))
from playtest_polish import call, OUT as QUEUE  # noqa: E402

RESULTS = ROOT / 'Art/World/Compact/Rebuild/Roadside303'
MINE = RESULTS / 'CombatTest' / 'queue-requests.txt'
COMBAT = 'Oheangbu.EditorTools.WorldMacro.Commission303Combat'
ESCORT = 'Oheangbu.EditorTools.WorldMacro.Escort303Regression'
FIX = 'Oheangbu.EditorTools.WorldMacro.Escort303Fix'


def guard(shared_ok):
    """Plan §11 G0: the queue must be idle, and nobody else should be driving the editor right now."""
    if (QUEUE / 'command.json').exists():
        raise SystemExit('refused: Art/PlaytestPolish/Unity/command.json is still pending — wait until the queue consumed it')
    mine = set(MINE.read_text(encoding='utf-8').split()) if MINE.exists() else set()
    now = time.time()
    others = [p.name for p in QUEUE.glob('request_*.json') if now - p.stat().st_mtime < 600 and p.stem[len('request_'):] not in mine]
    if others and not shared_ok:
        raise SystemExit('refused: other queue requests in the last 10 min (' + ', '.join(sorted(others)[:5]) + ') — '
                         'another session may share the editor; confirm with the user, then pass --shared-ok')


def run(kind, command, timeout=900, shared_ok=False):
    guard(shared_ok)
    buf = io.StringIO()
    with contextlib.redirect_stdout(buf):
        r = call(kind, 'Run', command, timeout)
    first = buf.getvalue().splitlines()[0] if buf.getvalue() else ''
    token = Path(first.strip()).stem.replace('response_', '') if first else ''
    if token:
        MINE.parent.mkdir(parents=True, exist_ok=True)
        with MINE.open('a', encoding='utf-8') as f:
            f.write(token + '\n')
    return r['result']


def wait(kind, seconds, shared_ok):
    until = time.monotonic() + seconds
    last = None
    while time.monotonic() < until:
        status = json.loads(run(kind, 'status', 120, True))
        line = f"{status.get('status')} {status.get('phase', '')} {status.get('note', '')}"
        if line != last:
            print(time.strftime('%H:%M:%S'), line, flush=True)
            last = line
        if not status.get('active') and status.get('status') in ('DONE', 'IDLE'):
            print(json.dumps(status, ensure_ascii=False, indent=2))
            return status
        time.sleep(5)
    raise SystemExit('wait: timed out; the run may still be going (use status / abort)')


def plot(run_id):
    from PIL import Image, ImageDraw
    folder = RESULTS / 'CombatTest' / run_id
    rows = json.loads((folder / 'timeline.json').read_text(encoding='utf-8-sig'))['rows']
    probe_file = RESULTS / 'CombatTest' / 'probe.json'
    probe = json.loads(probe_file.read_text(encoding='utf-8-sig')) if probe_file.exists() else {}
    marks = {'giver': probe.get('giverFront'), 'changgui': probe.get('changguiHome'),
             'rest': {'x': 3068.7, 'z': 2339.1}, 'jige': {'x': 3126.4, 'z': 2596.2}}
    pts = [(r['player']['x'], r['player']['z']) for r in rows] + [(r['enemy']['x'], r['enemy']['z']) for r in rows if r.get('enemyHp', -1) >= 0]
    pts += [(m['x'], m['z']) for m in marks.values() if m]
    if not pts:
        raise SystemExit('no samples')
    xs, zs = [p[0] for p in pts], [p[1] for p in pts]
    x0, x1, z0, z1 = min(xs) - 10, max(xs) + 10, min(zs) - 10, max(zs) + 10
    w, h = 1400, 1400
    scale = min(w / (x1 - x0), h / (z1 - z0))
    img = Image.new('RGB', (w, h + 500), (245, 242, 235))
    d = ImageDraw.Draw(img)
    def xy(x, z):
        return ((x - x0) * scale, h - (z - z0) * scale)
    player = [xy(r['player']['x'], r['player']['z']) for r in rows]
    if len(player) > 1:
        d.line(player, fill=(30, 30, 30), width=2)
    enemy = [xy(r['enemy']['x'], r['enemy']['z']) for r in rows if r.get('enemyHp', -1) >= 0 and r['phase'] in ('P6', 'P7', 'P8')]
    if len(enemy) > 1:
        d.line(enemy, fill=(170, 40, 30), width=2)
    for name, m in marks.items():
        if m:
            px, pz = xy(m['x'], m['z'])
            d.ellipse((px - 6, pz - 6, px + 6, pz + 6), outline=(0, 90, 160), width=3)
            d.text((px + 8, pz - 8), name, fill=(0, 60, 120))
    # HP / ink / enemy HP against real time
    t0, t1 = rows[0]['tReal'], rows[-1]['tReal'] or 1
    def series(key, top, colour, scale_to=1.0):
        line = [(20 + (r['tReal'] - t0) / max(1e-3, t1 - t0) * (w - 40), top + 200 - min(1.0, max(0.0, r[key] / scale_to)) * 180) for r in rows if r.get(key, -1) >= 0]
        if len(line) > 1:
            d.line(line, fill=colour, width=2)
    d.text((20, h + 10), 'HP (black), ink (blue), changgui HP (red) over real time', fill=(20, 20, 20))
    series('hp', h + 30, (20, 20, 20))
    series('ink', h + 30, (30, 80, 200))
    series('enemyHp', h + 260, (170, 40, 30), 60.0)
    out = folder / 'trail.png'
    img.save(out)
    return out


if __name__ == '__main__':
    args = [a for a in sys.argv[1:] if a != '--shared-ok']
    shared = '--shared-ok' in sys.argv
    if not args:
        raise SystemExit(__doc__)
    verb, rest = args[0], args[1:]
    if verb == 'probe':
        print(run(COMBAT, 'probe:' + (rest[0] if rest else 'main'), 300, shared))
    elif verb == 'lab':
        print(run(COMBAT, 'stroke-lab:' + (rest[0] if rest else '마'), 900, shared))
    elif verb == 'start':
        print(run(COMBAT, 'start:' + (rest[0] if rest else 'main') + ':' + (rest[1] if len(rest) > 1 else 'walk'), 300, shared))
    elif verb in ('status', 'report', 'abort'):
        print(run(COMBAT, verb, 300, True))
    elif verb == 'wait':
        wait(COMBAT, int(rest[0]) if rest else 1500, shared)
    elif verb == 'escort-audit':
        print(run(ESCORT, 'audit:' + (rest[0] if rest else 'main'), 600, shared))
    elif verb == 'escort-start':
        print(run(ESCORT, 'start' + (':full' if rest and rest[0] == 'full' else ''), 300, shared))
    elif verb in ('escort-status', 'escort-report', 'escort-abort'):
        print(run(ESCORT, verb[len('escort-'):], 300, True))
    elif verb == 'escort-wait':
        wait(ESCORT, int(rest[0]) if rest else 2400, shared)
    elif verb == 'fix-plan':
        print(run(FIX, 'plan:' + (rest[0] if rest else 'main'), 600, shared))
    elif verb == 'fix-move':
        if not rest:
            raise SystemExit('fix-move needs a target')
        print(run(FIX, 'move-start:' + rest[0] + (':allow-offmesh' if len(rest) > 1 and rest[1] == 'allow-offmesh' else ''), 900, shared))
    elif verb == 'fix-apply-all':
        print(run(FIX, 'apply-all' + (':allow-offmesh' if rest and rest[0] == 'allow-offmesh' else ''), 1800, shared))
    elif verb == 'plot':
        print(plot(rest[0]))
    else:
        raise SystemExit(__doc__)
