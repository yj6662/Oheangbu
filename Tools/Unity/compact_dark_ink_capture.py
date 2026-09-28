"""Sequential fixed-view captures; Unity guards system commit before allocating each image."""
from pathlib import Path
import json, subprocess, sys
root=Path(__file__).resolve().parents[2]
out=root/'Art/World/WorldMacro/Compact/InkLandscape/Darker'
label=sys.argv[1]
views=json.loads((out/'views.json').read_text(encoding='utf-8-sig'))['views']
for view in views:
    name=view['id']
    if (out/f'{label}_{name}.png').exists():
        print('EXISTS',name,flush=True)
        continue
    for attempt in range(4):
        result=subprocess.run([sys.executable,str(root/'Tools/Demo/run_command.py'),f'chapter3:compact:recovery:ink-dark:capture:{label}:{name}'],cwd=root,capture_output=True,text=True,encoding='utf-8')
        if result.returncode==0:
            print('CAPTURED',label,name,flush=True)
            break
        if 'Capture deferred' not in result.stdout or attempt==3:
            print(result.stdout,result.stderr,flush=True)
            raise SystemExit(result.returncode)
        print('PREPARING',name,flush=True)
