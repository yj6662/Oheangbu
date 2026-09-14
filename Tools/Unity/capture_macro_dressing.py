"""Sequential, resumable 1080p stills with the system-commit guard retained."""
import argparse
import json
from pathlib import Path
from world_macro import call
from macro_memory import cool_down

parser = argparse.ArgumentParser()
parser.add_argument('--region')
parser.add_argument('--force', action='store_true')
args = parser.parse_args()
out = Path('Art/World/WorldMacro/Dressing')
plan = json.loads((out/'capture_plan.json').read_text(encoding='utf-8-sig'))
for view in plan['views']:
    if args.region and view['region'] != args.region:
        continue
    for enabled in (False, True):
        label = view['after' if enabled else 'before']
        if not args.force and (out/(label+'.png')).exists():
            print('Retained existing still: '+label, flush=True)
            continue
        for attempt in range(3):
            cool_down()
            try:
                call('DressingCapturePlanned', json.dumps(dict(label=view['id'], dressing=enabled)))
                break
            except RuntimeError as error:
                if 'commit >=85%' not in str(error) or attempt == 2:
                    raise
                print('Capture not started/completed; cooldown before bounded retry.', flush=True)
call('DressingReview')
