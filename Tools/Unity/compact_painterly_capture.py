"""Sequential actual Unity captures. Memory guard stays inside each camera request."""
from pathlib import Path
import json, subprocess, sys
root=Path(__file__).resolve().parents[2]
out=root/'Art/World/WorldMacro/Compact/InkLandscape/Painterly'
label=sys.argv[1]
views=json.loads((out/'views.json').read_text(encoding='utf-8-sig'))['views']
if label=='atmosphere': views=[v for v in views if v['id'] in ('inn','mountain_path','DeepForest')]
for view in views:
    name=view['id']
    if (out/f'{label}_{name}.png').exists():
        print('EXISTS',name,flush=True)
        continue
    for attempt in range(4):
        command=f'capture-before:{name}' if label=='before' else f'capture:{label}:{name}'
        result=subprocess.run([sys.executable,str(root/'Tools/Demo/run_command.py'),f'chapter3:compact:recovery:ink-paint:{command}'],cwd=root,capture_output=True,text=True,encoding='utf-8')
        if result.returncode==0:
            print('CAPTURED',label,name,flush=True)
            break
        if 'Capture deferred' not in result.stdout or attempt==3:
            print(result.stdout,result.stderr,flush=True)
            raise SystemExit(result.returncode)
        print('PREPARING',name,flush=True)
