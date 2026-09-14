"""Wait for the editor's own memory guard before another authoring/render step."""
import time
from world_macro import call

def cool_down():
    call('Cleanup')
    for i in range(12):
        result = call('Probe')['result']
        ratio = float(result.split('commit=')[-1])
        if ratio < .85:
            return ratio
        print(f'Commit {ratio:.1%}; no new authoring or render allocation.', flush=True)
        time.sleep(5)
        if i % 3 == 2:
            call('Cleanup')
    raise RuntimeError('Memory guard remained at 85% or above; saved artifacts retained.')
