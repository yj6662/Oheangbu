"""Resume saved palette stages after releasing unused Unity resources."""
import re
import time
from pathlib import Path
from world_macro import call

last = -1
stalls = 0
for attempt in range(12):
    call('Cleanup')
    for cooling in range(12):
        ratio = float(call('Probe')['result'].split('commit=')[-1])
        if ratio < .85:
            break
        print(f'No new assets: commit {ratio:.1%}; allowing pending imports to settle.', flush=True)
        time.sleep(5)
        if cooling % 3 == 2:
            call('Cleanup')
    else:
        raise RuntimeError('Stopped: commit stayed above 85% after cleanup.')
    try:
        call('DressingPrepareAssets')
        break
    except RuntimeError as error:
        detail = str(error)
        if 'commit >=85%' not in detail:
            raise
        folder = Path('Oheangbu/Assets/_Project/Art/World/WorldMacro/Dressing')
        saved = len(re.findall(r'^  - Id:', (folder/'Dressing.asset').read_text(encoding='utf-8'), re.M))
        stalls = stalls + 1 if saved <= last else 0
        last = saved
        print(f'Palette checkpoint retained: {saved}; releasing resources before next stage.', flush=True)
        if stalls >= 2:
            raise RuntimeError('Stopped: two stages made no progress; inspect memory usage.') from error
else:
    raise RuntimeError('Stopped at bounded palette-stage limit; saved results retained.')
