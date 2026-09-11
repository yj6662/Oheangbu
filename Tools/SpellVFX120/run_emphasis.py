"""Resume four single-glyph captures, never overlapping Unity operations."""
import json,time
from pathlib import Path
from original_scope import call as queue_call
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Art/SpellVFX120/Emphasis4'
def call(method,request=None):
    return queue_call(method,request)
def wait(glyph):
    p=OUT/(glyph+'_v3')/'report.json'
    deadline=time.monotonic()+420
    while time.monotonic()<deadline:
        if p.exists():
            try:r=json.loads(p.read_text(encoding='utf-8'))
            except json.JSONDecodeError:time.sleep(2);continue
            if r['status']!='RUNNING':
                if r['status']!='PASS_RUNTIME_FIXTURE_VISUAL_PENDING':raise RuntimeError(r)
                while 'playing=True' in call('Probe'):time.sleep(2)
                print(glyph+' COMPLETE '+str(len(r['clips']))+' clips',flush=True)
                return
        time.sleep(2)
    raise TimeoutError(glyph)
if __name__=='__main__':
    wait('가')
    for glyph in ['나','거','너']:
        p=OUT/(glyph+'_v3')/'report.json'
        if p.exists() and json.loads(p.read_text(encoding='utf-8'))['status']=='PASS_RUNTIME_FIXTURE_VISUAL_PENDING':continue
        print(call('EmphasisBuild',glyph),flush=True)
        print(call('EmphasisCapture',glyph),flush=True)
        wait(glyph)
