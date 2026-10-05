"""#308 building audit still sweep (SPEC-WORLD-BUILDING-AUDIT-308 §C).

Sends the shots of BuildingAudit308/Shots/shots.json (written by `BuildingAudit308 Run "shots[:budget=300]"`) to the editor
queue ONE AT A TIME (one queue call = one still):
    Presentation297 Run "shot:<name>:<eye>:<target>:fov=60:w=1600:h=900:hideplayer"
Each still is moved at once from the shared Art/Presentation297/Stills to BuildingAudit308/Shots/<run>/ (no overwrite of the
shared folder), a 400x225 thumbnail is written (one image open at a time), and its provenance (command, utc, scene SHA,
quality level from `BuildingAudit308 status`) is appended to Shots/<run>/shots-log.jsonl. Every 16 thumbnails become one
contact sheet BuildingAudit308/Sheets/<run>_NN.jpg (4x4 tiles of 400x225 + a name band).

    python Tools/Unity/buildingaudit308_sweep.py --run before                 # all shots of shots.json (resumes: existing stills are skipped)
    python Tools/Unity/buildingaudit308_sweep.py --run after --shots Art/World/Compact/Rebuild/BuildingAudit308/Shots/shots-main-<utc>.json
    python Tools/Unity/buildingaudit308_sweep.py --run before --limit 20      # a slice (resume later)
    python Tools/Unity/buildingaudit308_sweep.py --run before --sheets-only   # rebuild the contact sheets from the thumbnails
    python Tools/Unity/buildingaudit308_sweep.py --run before --dry           # print the commands only

Before every run: Art/PlaytestPolish/Unity/command.json must not exist, no other session drives the editor, and the user is
not playing on this PC (GPU/CPU load; blue-screen history). Use the SAME shots json for the before and after runs so the review
page can pair stills by name.
"""
import argparse, datetime, json, shutil, sys, time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'Tools/Unity'))
AUDIT = ROOT / 'Art/World/Compact/Rebuild/BuildingAudit308'
STILLS = ROOT / 'Art/Presentation297/Stills'
QUEUE = ROOT / 'Art/PlaytestPolish/Unity/command.json'
TILE = (400, 225); BAND = 22; GRID = 4


def utc():
    return datetime.datetime.now(datetime.timezone.utc).strftime('%Y%m%dT%H%M%SZ')


def queue(kind, method, argument, timeout=900):
    import playtest_polish
    if QUEUE.exists(): raise RuntimeError('an unconsumed editor command is pending (%s); stop and inspect' % QUEUE)
    return playtest_polish.call(kind, method, argument, timeout=timeout)


def status():
    r = queue('Oheangbu.EditorTools.WorldMacro.BuildingAudit308', 'Run', 'status', timeout=300)
    text = r.get('result', '')
    info = {'raw': text, 'quality': '', 'scene': '', 'sha': '', 'dirty': False}
    for line in text.splitlines():
        if line.startswith('quality level now: '): info['quality'] = line[len('quality level now: '):].split(' ')[0]
        if line.startswith('open scene '):
            parts = line.split(' ')
            info['scene'] = parts[2]; info['dirty'] = '(DIRTY)' in line; info['sha'] = parts[-1]
    return info


def thumb(src, dst):
    from PIL import Image
    with Image.open(src) as im:
        size = im.size
        t = im.convert('RGB'); t.thumbnail(TILE)
        canvas = Image.new('RGB', TILE, (30, 30, 30)); canvas.paste(t, ((TILE[0] - t.width) // 2, (TILE[1] - t.height) // 2))
        dst.parent.mkdir(parents=True, exist_ok=True); canvas.save(dst, quality=88)
    return size


def font():
    from PIL import ImageFont
    for f in ('C:/Windows/Fonts/malgun.ttf', 'C:/Windows/Fonts/arial.ttf'):
        try: return ImageFont.truetype(f, 13)
        except OSError: pass
    return ImageFont.load_default()


def sheets(run, names):
    """4x4 contact sheets of the thumbnails in shot order; one sheet image in memory at a time."""
    from PIL import Image, ImageDraw
    out = AUDIT / 'Sheets'; out.mkdir(parents=True, exist_ok=True)
    tdir = AUDIT / 'Shots' / run / 'thumbs'
    have = [n for n in names if (tdir / (n + '.jpg')).exists()]
    f = font(); made = []
    for k in range(0, len(have), GRID * GRID):
        page = have[k:k + GRID * GRID]
        sheet = Image.new('RGB', (TILE[0] * GRID, (TILE[1] + BAND) * GRID), (236, 231, 222))
        draw = ImageDraw.Draw(sheet)
        for i, n in enumerate(page):
            x, y = (i % GRID) * TILE[0], (i // GRID) * (TILE[1] + BAND)
            with Image.open(tdir / (n + '.jpg')) as im: sheet.paste(im, (x, y))
            draw.text((x + 4, y + TILE[1] + 3), n[-58:], fill=(40, 40, 40), font=f)
        path = out / ('%s_%02d.jpg' % (run, k // (GRID * GRID) + 1))
        sheet.save(path, quality=85); sheet.close(); made.append(path)
    return made


def main():
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('--shots', default=str(AUDIT / 'Shots/shots.json'))
    ap.add_argument('--run', required=True, help='run name, e.g. before / after (folder Shots/<run>)')
    ap.add_argument('--from', dest='start', type=int, default=0); ap.add_argument('--limit', type=int, default=0)
    ap.add_argument('--dry', action='store_true'); ap.add_argument('--sheets-only', action='store_true')
    ap.add_argument('--status-every', type=int, default=25)
    a = ap.parse_args()
    shots = json.loads(Path(a.shots).read_text(encoding='utf-8-sig'))
    items = shots.get('shots', [])
    names = [s['name'] for s in items]
    run_dir = AUDIT / 'Shots' / a.run
    if a.sheets_only:
        for p in sheets(a.run, names): print(p)
        return
    todo = items[a.start:]
    if a.limit: todo = todo[:a.limit]
    if a.dry:
        for s in todo: print(s['command'])
        return
    run_dir.mkdir(parents=True, exist_ok=True)
    (run_dir / 'shots-source.json').write_text(json.dumps({'shots': a.shots, 'run': a.run, 'started': utc(), 'count': len(items)}, ensure_ascii=False, indent=1), encoding='utf-8')
    log = run_dir / 'shots-log.jsonl'
    st = status()
    print('editor:', st['scene'], 'sha', st['sha'], 'quality', st['quality'], 'dirty', st['dirty'])
    if st['dirty']: raise SystemExit('refused: the open scene has unsaved changes')
    if shots.get('scene') and st['scene'] and st['scene'] != shots['scene']: raise SystemExit('refused: open scene %s, the shot list was made for %s' % (st['scene'], shots['scene']))
    done = fails = streak = 0
    for i, s in enumerate(todo):
        dst = run_dir / (s['name'] + '.png')
        if dst.exists(): continue
        if done and a.status_every and done % a.status_every == 0: st = status()
        row = {'name': s['name'], 'command': s['command'], 'utc': utc(), 'scene': st['scene'], 'sceneSha': st['sha'], 'qualityBefore': st['quality'],
               'qualityRender': 'PC (Presentation297 rig switches to PC and back)', 'priority': s.get('priority'), 'group': s.get('group'), 'kind': s.get('kind'),
               'interior': s.get('interior', False), 'findings': s.get('findings', []), 'ok': False}
        try:
            r = queue('Oheangbu.EditorTools.WorldMacro.Presentation297', 'Run', s['command'])
            src = Path(r.get('result', '').strip())
            if not src.exists(): src = STILLS / (s['name'] + '.png')
            shutil.move(str(src), str(dst))
            size = thumb(dst, run_dir / 'thumbs' / (s['name'] + '.jpg'))
            row.update(ok=tuple(size) == (s.get('w', 1600), s.get('h', 900)), size=list(size))
            done += 1; streak = 0
        except TimeoutError as e:   # the editor may still be rendering this still: never queue the next one behind it
            row['error'] = 'timeout: %s' % str(e)[:400]
            with open(log, 'a', encoding='utf-8') as f: f.write(json.dumps(row, ensure_ascii=False) + '\n')
            print('TIMEOUT %s: wait for %s, then move the still from %s by hand or rerun (resume)' % (s['name'], e, STILLS), flush=True)
            break
        except Exception as e:   # one failed still is logged; three in a row stop the sweep
            row['error'] = str(e)[:500]; fails += 1; streak += 1
        with open(log, 'a', encoding='utf-8') as f: f.write(json.dumps(row, ensure_ascii=False) + '\n')
        print('%4d/%d %s %s' % (a.start + i + 1, len(items), 'ok' if row['ok'] else 'FAIL', s['name']), flush=True)
        if streak >= 3: print('three failures in a row: stopping (resume with the same --run)'); break
        time.sleep(.2)
    made = sheets(a.run, names)
    total = sum(1 for n in names if (run_dir / (n + '.png')).exists())
    print('stills %d/%d in %s (this call: %d ok, %d failed); sheets %d' % (total, len(items), run_dir, done, fails, len(made)))


if __name__ == '__main__':
    main()
