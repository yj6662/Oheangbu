"""Capture menu pages sequentially through the guarded Editor review queue."""
import json
import sys
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'Tools/Unity'))
import world_macro

world_macro.OUT = ROOT / 'Art/World/WorldMacro/Playtest'
for page in sys.argv[1:]:
    world_macro.call('UI', 'capture:' + page)
    until = time.monotonic() + 45
    while True:
        result = world_macro.call('UI', 'capture-poll')['result']
        if result.startswith('COMPLETE'):
            report = json.loads(result[result.index('{'):])
            if report['status'] != 'PASS':
                raise RuntimeError(report)
            print('CAPTURED', page, report['width'], report['height'], flush=True)
            break
        if time.monotonic() >= until:
            raise TimeoutError(page)
        time.sleep(1)
