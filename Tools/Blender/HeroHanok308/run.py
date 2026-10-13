"""#308 HeroHanok batch driver: ONE Blender process per model, resource guard before each.
  python Tools/Blender/HeroHanok308/run.py [Name ...]          (no names = all)
  python Tools/Blender/HeroHanok308/run.py --verify            (pl_verify on the hero entries, merged into _cache/verify.json) + manifest rebuild
"""
import json, subprocess, sys, time, re
from pathlib import Path
HERE = Path(__file__).resolve().parent; REPO = HERE.parents[2]; OUT = REPO / 'Art/World/PackLite308'
BLENDER = 'C:/Program Files/Blender Foundation/Blender 5.0/blender.exe'


def wait():
    subprocess.run([sys.executable, str(REPO / 'Tools/resource_guard.py'), '--wait'], capture_output=True)
    for _ in range(360):
        tl = subprocess.run(['tasklist', '/FI', 'IMAGENAME eq blender.exe', '/NH'], capture_output=True, text=True).stdout
        if 'blender.exe' not in tl.lower(): break
        time.sleep(10)


def names_all():
    out = []
    for f in ('models.py', 'stone.py'):
        if (HERE / f).exists(): out += re.findall(r"^reg\('(\w+)'", (HERE / f).read_text(encoding='utf-8'), re.M)
    return out


def main():
    argv = sys.argv[1:]
    if '--verify' in argv:
        vp = OUT / '_cache/verify.json'; old = json.loads(vp.read_text()) if vp.exists() else {}
        hero = [json.loads(p.read_text(encoding='utf-8'))['name'] for p in sorted((OUT / 'hero').glob('*.json'))]
        wait()
        p = subprocess.run([BLENDER, '-b', '--factory-startup', '-P', str(HERE.parent / 'PackLite308/pl_verify.py'), '--'] + hero, capture_output=True, text=True, encoding='utf-8', errors='ignore')
        print('\n'.join(l for l in p.stdout.splitlines() if l.startswith('PLV') and ('BAD' in l or 'total' in l)))
        new = json.loads(vp.read_text()); old.update(new); vp.write_text(json.dumps(old, indent=1))
        subprocess.run([sys.executable, str(HERE.parent / 'PackLite308/run.py'), '--manifest'])
        return
    for n in argv or names_all():
        wait(); t = time.time()
        script = 'stone.py' if n.startswith('Hero_Haetae') else 'models.py'
        p = subprocess.run([BLENDER, '-b', '--factory-startup', '-P', str(HERE / script), '--', n], capture_output=True, text=True, encoding='utf-8', errors='ignore')
        lines = [l for l in (p.stdout or '').splitlines() if l.startswith('PL ') or 'Traceback' in l or l.startswith('  File') or ('Error' in l and 'could not find' not in l)]
        with open(OUT / '_cache/hero_run.log', 'a', encoding='utf-8') as f: f.write('\n'.join(lines) + '\n== %s exit %d %.0fs\n' % (n, p.returncode, time.time() - t))
        print('\n'.join(lines), flush=True)


if __name__ == '__main__':
    main()
