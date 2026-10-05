"""#307 phase 0 performance baseline (Tools/Unity/Plan307/PERF_DESIGN.md C.0). Measurement only; one run at a time.

Editor runs go through the playtest polish queue (Oheangbu.EditorTools.WorldMacro.Perf307.Run) and poll `status` to the end.
Keep the Unity window in front for the whole run: fewer than 90 % focused frames makes the run INVALID.
  python Tools/Unity/perf307.py baseline [tier=pc|mobile] [variant=full|noart] [mode=uncapped|cap120] [--wait 1200]
  python Tools/Unity/perf307.py arrival [tier=pc] [mode=uncapped]
  python Tools/Unity/perf307.py ab "feature297:SSAO297:0" [tier=pc] [mode=uncapped]      (or "urp:shadow=100,cascades=3")
  python Tools/Unity/perf307.py ab "sky308:1" tier=pc      (#308 AC-S19: RealmInkSky308 vs the step-1 sky, in-memory swap)
  python Tools/Unity/perf307.py ab "wash308:0" tier=pc     (#308 AC-W14: event wash drivers off (B) vs on (A), in memory; cost = -delta)
  python Tools/Unity/perf307.py status | abort | frametiming-on
Standalone player (Release build with frame timing stats on:
  python Tools/Unity/perf307.py frametiming-on
  python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.WorldMacro.Finish297Build Run ""   -> Builds/Demo297/<utc>/Oheangbu.exe):
  python Tools/Unity/perf307.py standalone --exe Builds/Demo297/<utc>/Oheangbu.exe [--command baseline|arrival]
         [--variant full|noart] [--mode uncapped|cap120] [--timeout 900]
  Before launch the whole persistentDataPath (%USERPROFILE%/AppData/LocalLow/<company>/<product>) is hashed and copied to
  Art/Performance/Backups/perf307-<utc>/; the player gets an isolated save slot (--ui-test-slot=_perf307_<utc>) and
  -perf307 <Art/Performance/Perf307/<utc>-standalone>; afterwards changed or missing files are restored from the copy, files
  carrying the run suffix are moved into the run folder, any other new file is reported (never deleted), and the tree is
  re-hashed against the pre-launch manifest. Do not run an Editor Play session at the same time (same persistentDataPath).
"""
import argparse, contextlib, hashlib, io, json, os, re, shutil, subprocess, sys, tempfile, time
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(Path(__file__).resolve().parent))
from playtest_polish import call  # noqa: E402

TYPE = 'Oheangbu.EditorTools.WorldMacro.Perf307'
PERF = ROOT / 'Art/Performance/Perf307'
ROUTES = PERF / 'routes.json'
LOCK = Path(tempfile.gettempdir()) / 'perf307.lock'
print = __import__('functools').partial(print, flush=True)


def quiet_call(argument, timeout=180):
    with contextlib.redirect_stdout(io.StringIO()):
        return call(TYPE, 'Run', argument, timeout)


def acquire():
    try:
        fd = os.open(str(LOCK), os.O_CREAT | os.O_EXCL | os.O_WRONLY); os.write(fd, str(os.getpid()).encode()); os.close(fd); return
    except FileExistsError:
        age = time.time() - LOCK.stat().st_mtime
        if age > 7200:
            LOCK.unlink(); return acquire()
        raise SystemExit('perf307: another run holds %s (%.0f s old); wait for it or remove the lock if it is stale' % (LOCK, age))


def release():
    try: LOCK.unlink()
    except FileNotFoundError: pass


def headline(folder):
    summary = Path(folder) / 'summary.json'
    if not summary.exists():
        print('no summary.json in', folder); return None
    data = json.loads(summary.read_text(encoding='utf-8-sig'))
    print('status:', data.get('status')); print('scope :', data.get('scope'))
    print('frames %s valid %s focus %.1f%% drift %s spikes %s frameTiming %s quality %s pipeline %s' % (
        data.get('frames'), data.get('validFrames'), 100 * (data.get('focusShare') or 0), data.get('tierDriftEvents'), data.get('spikeCount'),
        data.get('frameTimingEnabled'), data.get('quality'), data.get('pipeline')))
    print('%-46s %7s %7s %7s %7s %7s %7s' % ('segment', 'med', 'p95', 'p99', 'busy', 'gpu', '>8.33'))
    for s in data.get('segments', []):
        print('%-46s %7.2f %7.2f %7.2f %7.2f %7.2f %6.0f%%' % (s['label'][:46], s['intervalMedian'], s['intervalP95'], s['intervalP99'], s['busyMedian'], s['gpuMedian'], 100 * max(0, s['overBudgetShare'])))
    for r in data.get('ab', []):
        print('ab %-40s busy %+.2f ms (noise %.2f, effect %s)  gpu %+.2f ms (noise %.2f, effect %s)' % (r['key'][:40], r['busyDelta'], r['noiseBusy'], r['busyEffect'], r['gpuDelta'], r['noiseGpu'], r['gpuEffect']))
    print('repeatability:', data.get('repeatability'))
    if data.get('missingRecorders'): print('missing recorders:', ', '.join(data['missingRecorders']))
    return data


def editor(argument, wait):
    acquire()
    try:
        started = quiet_call(argument)
        text = started.get('result', '')
        print('start:', text)
        if text.startswith('refused'): sys.exit(2)
        m = re.search(r'output (\S+)', text); folder = m.group(1) if m else None
        deadline = time.monotonic() + wait
        while time.monotonic() < deadline:
            time.sleep(15)
            status = quiet_call('status').get('result', '')
            print('status:', status[:220])
            if not status.startswith(('Running', 'Stopping')):
                if not folder:
                    m = re.search(r'output (\S+)', status); folder = m.group(1) if m else None
                if folder: headline(folder)
                sys.exit(0 if status.startswith('PASS') else 1)
        print('perf307: still running after %d s; poll with: python Tools/Unity/perf307.py status' % wait); sys.exit(3)
    finally:
        release()


def company_product():
    text = (ROOT / 'Oheangbu/ProjectSettings/ProjectSettings.asset').read_text(encoding='utf-8', errors='replace')
    company = re.search(r'^\s*companyName:\s*(.+)$', text, re.M).group(1).strip()
    product = re.search(r'^\s*productName:\s*(.+)$', text, re.M).group(1).strip()
    return company, product


def manifest(folder):
    result = {}
    if not folder.exists(): return result
    for f in sorted(folder.rglob('*')):
        if f.is_file():
            h = hashlib.sha256()
            with open(f, 'rb') as stream:
                for chunk in iter(lambda: stream.read(1 << 20), b''): h.update(chunk)
            result[f.relative_to(folder).as_posix()] = h.hexdigest()
    return result


def standalone(a):
    exe = Path(a.exe) if Path(a.exe).is_absolute() else ROOT / a.exe
    if not exe.exists(): raise SystemExit('perf307: no player at %s' % exe)
    if not ROUTES.exists(): raise SystemExit('perf307: routes missing %s' % ROUTES)
    acquire()
    try:
        company, product = company_product()
        persistent = Path(os.environ['USERPROFILE']) / 'AppData/LocalLow' / company / product
        utc = datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%SZ')
        suffix = '_perf307_' + utc.rstrip('Z')
        out = PERF / (utc + '-standalone'); out.mkdir(parents=True, exist_ok=False)
        backup = ROOT / 'Art/Performance/Backups' / ('perf307-' + utc)
        before = manifest(persistent)
        if persistent.exists(): shutil.copytree(persistent, backup / 'persistent')
        else: (backup).mkdir(parents=True, exist_ok=True)
        (backup / 'manifest.json').write_text(json.dumps({'persistent': str(persistent), 'files': before}, indent=1), encoding='utf-8')
        copied = manifest(backup / 'persistent') if persistent.exists() else {}
        if copied != before: raise SystemExit('perf307: backup copy does not match the live tree; nothing launched')
        print('backup: %d file(s) of %s -> %s' % (len(before), persistent, backup))
        args = [str(exe), '-screen-width', '1920', '-screen-height', '1080', '-window-mode', 'borderless',
                '--ui-test-slot=' + suffix, '-perf307', str(out), '-perf307-routes', str(ROUTES),
                '-perf307-command', a.command, '-perf307-variant', a.variant, '-perf307-mode', a.mode, '-perf307-run', utc,
                '-logFile', str(out / 'player.log')]
        (out / 'launch.json').write_text(json.dumps({'args': args, 'backup': str(backup)}, indent=1), encoding='utf-8')
        print('launch:', ' '.join(args))
        print('keep the player window in front until it quits by itself')
        started = time.monotonic(); proc = subprocess.Popen(args, cwd=str(exe.parent))
        try: code = proc.wait(timeout=a.timeout)
        except subprocess.TimeoutExpired:
            print('perf307: player still running after %d s; terminating our own player process' % a.timeout)
            proc.terminate()
            try: code = proc.wait(timeout=30)
            except subprocess.TimeoutExpired: proc.kill(); code = proc.wait()
        print('player exit code %s after %.0f s' % (code, time.monotonic() - started))
        # restore persistentDataPath
        after = manifest(persistent); report = {'restored': [], 'moved': [], 'unexpected': [], 'exit': code}
        for rel in sorted(set(after) - set(before)):
            src = persistent / rel
            if suffix in rel:
                dst = out / 'isolated-saves' / rel; dst.parent.mkdir(parents=True, exist_ok=True); shutil.move(str(src), str(dst)); report['moved'].append(rel)
            else: report['unexpected'].append(rel)
        for rel, sha in before.items():
            if after.get(rel) != sha:
                dst = persistent / rel; dst.parent.mkdir(parents=True, exist_ok=True); shutil.copy2(str(backup / 'persistent' / rel), str(dst)); report['restored'].append(rel)
        final = manifest(persistent)
        report['verified'] = all(final.get(rel) == sha for rel, sha in before.items())
        (out / 'persistent-restore.json').write_text(json.dumps(report, indent=1), encoding='utf-8')
        print('persistentDataPath: restored %d, moved %d isolated file(s), unexpected new %d, verified %s' % (
            len(report['restored']), len(report['moved']), len(report['unexpected']), report['verified']))
        if report['unexpected']: print('  unexpected (left in place):', ', '.join(report['unexpected']))
        data = headline(out)
        ok = report['verified'] and code == 0 and data is not None and str(data.get('status', '')).startswith('PASS')
        sys.exit(0 if ok else 1)
    finally:
        release()


if __name__ == '__main__':
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument('command', choices=['baseline', 'arrival', 'ab', 'status', 'abort', 'frametiming-on', 'standalone'])
    p.add_argument('rest', nargs='*', help='k=v options (tier, variant, mode); for ab the toggle first')
    p.add_argument('--wait', type=int, default=1200)
    p.add_argument('--exe'); p.add_argument('--timeout', type=int, default=900)
    p.add_argument('--mode', default='uncapped', choices=['uncapped', 'cap120'])
    p.add_argument('--variant', default='full', choices=['full', 'noart'])
    p.add_argument('--command', dest='run_command', default='baseline', choices=['baseline', 'arrival'], help='standalone only')
    a = p.parse_args()
    if a.command == 'status': print(quiet_call('status').get('result', '')); sys.exit(0)
    if a.command == 'abort': print(quiet_call('abort').get('result', '')); sys.exit(0)
    if a.command == 'frametiming-on': print(quiet_call('settings:frametiming-on').get('result', '')); sys.exit(0)
    if a.command == 'standalone':
        if not a.exe: raise SystemExit('standalone needs --exe <path to Oheangbu.exe>')
        a.command = a.run_command; standalone(a)
    opts = [x for x in a.rest if '=' in x and not x.startswith(('feature297:', 'urp:', 'artpath:', 'farclip:', 'sky308:', 'wash308:'))]
    if a.command == 'ab':
        toggles = [x for x in a.rest if x.startswith(('feature297:', 'urp:', 'artpath:', 'farclip:', 'sky308:', 'wash308:'))]
        if len(toggles) != 1: raise SystemExit('ab needs exactly one toggle: feature297:<name>:<0|1> or urp:shadow=<m>[,cascades=<n>][,ssao=<0|1>]')
        editor('ab:' + toggles[0] + ''.join('|' + o for o in opts), a.wait)
    editor(a.command + ''.join(':' + o for o in opts), a.wait)
