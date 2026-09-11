"""One spell, one clip at a time. Fail closed on runtime or memory errors."""
import json,time
from pathlib import Path
from original_scope import call
OUT=Path('Art/SpellVFX120/BasicSix')
for glyph in '사마아서머어':
    path=OUT/glyph/'report.json'
    if path.exists() and json.loads(path.read_text(encoding='utf-8'))['status']=='PASS_RUNTIME_FIXTURE_VISUAL_PENDING':continue
    if 'playing=True' in call('Probe',None):raise RuntimeError('Editor still playing')
    print(call('ReviewDrainWorkers',None),flush=True)
    print(call('BasicSixBuild',glyph),flush=True)
    previous=path.stat().st_mtime if path.exists() else 0
    print(call('BasicSixCapture',glyph),flush=True)
    deadline=time.monotonic()+900
    while time.monotonic()<deadline:
        if path.exists() and path.stat().st_mtime>previous:
            try:r=json.loads(path.read_text(encoding='utf-8'))
            except json.JSONDecodeError:time.sleep(2);continue
            if r['status']!='RUNNING':
                if 'playing=True' in call('Probe',None):time.sleep(2);continue
                if r['status']!='PASS_RUNTIME_FIXTURE_VISUAL_PENDING':raise RuntimeError(r)
                print(glyph+' COMPLETE '+str(len(r['clips']))+' clips',flush=True);break
        time.sleep(2)
    else:raise TimeoutError(glyph)
