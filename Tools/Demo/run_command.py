"""Run one existing Demo Editor queue command and print a compact receipt.

Full command responses remain in the established Playtest response directory.
This tool never loops over mutations, enables Play or bakes unless its explicit
command requests that operation.
"""
from pathlib import Path
import contextlib
import io
import json
import sys

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'Tools/Unity'))
import world_macro

if len(sys.argv) != 2:
    raise SystemExit('Usage: python Tools/Demo/run_command.py <Demo command>')
world_macro.OUT = world_macro.OUT / 'Playtest'
with contextlib.redirect_stdout(io.StringIO()):
    response = world_macro.call('Demo', sys.argv[1])
try:
    value = json.loads(response.get('result', ''))
except (ValueError, TypeError):
    value = response
if isinstance(value, dict):
    summary = {key: item for key, item in value.items()
               if isinstance(item, (str, int, float, bool)) or item is None}
    for key, item in value.items():
        if isinstance(item, list):
            summary[key + 'Count'] = len(item)
    if isinstance(value.get('checks'), list):
        summary['findings'] = [c for c in value['checks']
                               if (isinstance(c, dict) and c.get('status') in ('FAIL', 'FINDING'))
                               or (isinstance(c, str) and c.startswith('FAIL'))]
    print(json.dumps(summary, ensure_ascii=False, indent=2))
else:
    print(value)
if response.get('status') != 'COMPLETE' or response.get('error'):
    raise SystemExit(1)
