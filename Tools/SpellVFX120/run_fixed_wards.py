from pathlib import Path
import json,time
from original_scope import call
out=Path('Art/SpellVFX120/FixedWards')
for glyph in '구무수우누':
 path=out/glyph/'report.json'
 if path.exists() and json.loads(path.read_text(encoding='utf-8')).get('status')=='PASS_VFX_DIAGNOSTIC_VISUAL_PENDING':continue
 print(call('ReviewDrainWorkers',None),flush=True)
 print(call('FixedWardBuild',glyph),flush=True)
 audit=call('FixedWardAudit',glyph);print(audit,flush=True)
 if not audit.startswith('PASS'):raise RuntimeError(audit)
 print(call('FixedWardCapture',glyph),flush=True)
 end=time.monotonic()+600
 while time.monotonic()<end:
  if path.exists():
   try:r=json.loads(path.read_text(encoding='utf-8'))
   except json.JSONDecodeError:time.sleep(1);continue
   if r['status']=='FAIL':raise RuntimeError(r)
   if r['status']=='PASS_VFX_DIAGNOSTIC_VISUAL_PENDING':print(glyph+' COMPLETE',flush=True);break
  time.sleep(2)
 else:raise TimeoutError(glyph)
 print(call('ReviewMemoryCleanup',None),flush=True)
