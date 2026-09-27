"""Sequential real Unity screenshots for broad mountain brushwork; no scene traversal."""
from pathlib import Path
import json, subprocess, sys

root = Path(__file__).resolve().parents[2]
out = root / 'Art/World/WorldMacro/Compact/InkLandscape/BroadBrush'
label = sys.argv[1]
views = [v['id'] for v in json.loads((out / 'views.json').read_text(encoding='utf-8-sig'))['views']]
if len(sys.argv) > 2:
    views = sys.argv[2:]
for name in views:
    if (out / f'{label}_{name}.png').exists():
        print('EXISTS', name, flush=True)
        continue
    command = f'capture-before:{name}' if label == 'before' else f'capture:{label}:{name}'
    result = subprocess.run([sys.executable, str(root / 'Tools/Demo/run_command.py'),
                             f'chapter3:compact:recovery:ink-broad:{command}'],
                            cwd=root, capture_output=True, text=True, encoding='utf-8')
    print(result.stdout, result.stderr, flush=True)
    if result.returncode:
        raise SystemExit(result.returncode)
