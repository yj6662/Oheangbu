"""Sequential v5 capture; a stopped/failed capture never starts another glyph."""
import json,time
from pathlib import Path
from original_scope import call
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Art/SpellVFX120/Emphasis4'
def wait(glyph):
    path=OUT/(glyph+'_v5')/'report.json'
    deadline=time.monotonic()+600
    while time.monotonic()<deadline:
        if path.exists():
            try:r=json.loads(path.read_text(encoding='utf-8'))
            except json.JSONDecodeError:time.sleep(2);continue
            if r['status']!='RUNNING':
                if 'playing=True' in call('Probe',None):time.sleep(2);continue
                if r['status']!='PASS_RUNTIME_FIXTURE_VISUAL_PENDING':raise RuntimeError(r)
                print(glyph+' COMPLETE '+str(len(r['clips']))+' clips',flush=True)
                return
        time.sleep(2)
    raise TimeoutError(glyph)
if __name__=='__main__':
    wait('가')
    for glyph in '나거너':
        print(call('OffsetGuardBuild',glyph),flush=True)
        print(call('EmphasisCapture',glyph),flush=True)
        wait(glyph)
