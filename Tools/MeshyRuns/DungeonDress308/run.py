"""D308-117 C3b dungeon dress props Meshy intake (SPEC-FIELD-FIRST-HOUR-308 §8.7) - the EnemyBodies308 runner with its own
ledger (Art/World/Dungeon308/Dress308Meshy/meshy-ledger.json) and a 200-credit cap of this pass. The 1,000-credit month ceiling is
shared with EnemyBodies308: before every paid POST the two ledgers are summed with the largest estimate (30).
Paid POSTs are explicit, serialized, never retried; secrets and image data are never logged.
python Tools/MeshyRuns/DungeonDress308/run.py status | poll | download | submit image <name> --prompt-file <txt> --aspect 1:1
                                              | submit model <name> --reference <png> --polygons 8000 --texture-prompt "..."
"""
import importlib.util, json, sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location('enemy_runner', HERE.parent / 'EnemyBodies308' / 'run.py')
R = importlib.util.module_from_spec(spec); spec.loader.exec_module(R)
R.OUT = R.ROOT / 'Art/World/Dungeon308/Dress308Meshy'
R.LEDGER = R.OUT / 'meshy-ledger.json'
R.CAP = 200
SHARED = [R.ROOT / 'Art/Characters/EnemyBodies308/meshy-ledger.json']
MONTH = 1000
_submit = R.submit


def shared_submit(args):
    used = 0
    for f in SHARED + [R.LEDGER]:
        if f.exists():
            d = json.loads(f.read_text(encoding='utf-8'))
            if any(t['status'] in ('POST_RESERVED', 'POST_UNCERTAIN') for t in d['tasks']):
                raise RuntimeError(f.name + ' of ' + f.parent.name + ' has an unreconciled POST; no paid call')
            used += sum(map(R.cost, d['tasks']))
    if used + 30 > MONTH:
        raise RuntimeError('The shared 1,000-credit month ceiling would be exceeded (used ' + str(used) + ')')
    return _submit(args)


R.submit = shared_submit

if __name__ == '__main__':
    try:
        R.main()
    except (RuntimeError, ValueError, KeyError, StopIteration) as error:
        print(str(error) if isinstance(error, RuntimeError) else 'Invalid request or provider schema; inspect local task receipt safely')
        raise SystemExit(1)
