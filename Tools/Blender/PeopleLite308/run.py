"""#308 PeopleLite: 전부 차례로(Blender 는 한 번에 하나, 작업마다 resource_guard --wait).
    python Tools/Blender/PeopleLite308/run.py [BodyName ...] [--no-verify]
lite.py(Blender) -> atlas.py(Python) -> verify.py(Blender) -> manifest.py"""
import json, os, subprocess, sys
HERE = os.path.dirname(os.path.abspath(__file__)); ROOT = os.path.abspath(os.path.join(HERE, '..', '..', '..'))
BLENDER = 'C:/Program Files/Blender Foundation/Blender 5.0/blender.exe'
CFG = os.path.join(HERE, 'peoplelite308.json'); names = [a for a in sys.argv[1:] if not a.startswith('--')] or [b['name'] for b in json.load(open(CFG, encoding='utf-8'))['bodies']]


def blender(script, name, tag):
    subprocess.run([sys.executable, os.path.join(ROOT, 'Tools', 'resource_guard.py'), '--wait'], stdout=subprocess.DEVNULL)
    r = subprocess.run([BLENDER, '-b', '--factory-startup', '-P', os.path.join(HERE, script), '--', CFG, name], capture_output=True, text=True, encoding='utf-8', errors='replace')
    out = [l for l in (r.stdout + r.stderr).splitlines() if l.startswith(tag) or 'Error' in l or 'Traceback' in l or 'REFUSE' in l or l.startswith('  File')]
    print('\n'.join(l[:700] for l in out)); sys.stdout.flush()
    if not any('DONE' in l for l in out): sys.exit('FAILED ' + script + ' ' + name)


for n in names:
    blender('lite.py', n, 'LITE')
    subprocess.run([sys.executable, os.path.join(HERE, 'atlas.py'), n], check=True)
    if '--no-verify' not in sys.argv: blender('verify.py', n, 'VERIFY')
if os.path.exists(os.path.join(HERE, 'manifest.py')): subprocess.run([sys.executable, os.path.join(HERE, 'manifest.py')], check=True)
