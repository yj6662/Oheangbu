"""D308-153b variant 우/M (2026-10-29): wave models from the concept uks_1 crops, Meshy image-to-3D. The EnemyBodies308 runner with its own ledger
(Art/SpellVFX308/Restart/u_M/meshy-ledger.json) and a hard cap of 90 credits (three generations of ~30). Paid POSTs are explicit, serialized,
never retried; the key is read by the runner and never logged.
python Tools/MeshyRuns/SpellFxM308/run.py status | poll | download | submit model <name> --reference <png> --polygons 6000 --texture-prompt "..."
"""
import importlib.util, sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location('enemy_runner', HERE.parent / 'EnemyBodies308' / 'run.py')
R = importlib.util.module_from_spec(spec); spec.loader.exec_module(R)
R.OUT = R.ROOT / 'Art/SpellVFX308/Restart/u_M/meshy'
R.LEDGER = R.OUT / 'meshy-ledger.json'
R.CAP = 90

if __name__ == '__main__':
    try:
        R.main()
    except (RuntimeError, ValueError, KeyError, StopIteration) as error:
        print(str(error) if isinstance(error, RuntimeError) else 'Invalid request or provider schema; inspect local task receipt safely')
        raise SystemExit(1)
