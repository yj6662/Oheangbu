"""Serial editor-Play camera benchmarks. No prewarm, simultaneous capture or player traversal."""
import sys,time,json
from pathlib import Path
R=Path(__file__).resolve().parents[2];sys.path.insert(0,str(R/'Tools/Unity'))
from playtest_polish import call
root=R/'Art/PlaytestRecovery/Performance';results=[]
for mode in ['legacy','off','stream','stream:1440','off:1440']:
 before=set(root.rglob('measurement.json'))
 call('Oheangbu.EditorTools.WorldMacro.PlaytestRecoveryStreamingReview','Execute',mode)
 until=time.monotonic()+150
 while time.monotonic()<until:
  found=set(root.rglob('measurement.json'))-before
  if found:
   p=next(iter(found))
   try:d=json.loads(p.read_text(encoding='utf-8-sig'))
   except (OSError,json.JSONDecodeError):time.sleep(.5);continue
   results.append(str(p.relative_to(R)));print(mode,d['status'],str(p),flush=True)
   if d['status']!='MEASURED_DIAGNOSTIC_CAMERA':raise RuntimeError('Incomplete measurement; subsequent runs cancelled')
   break
  time.sleep(.5)
 else:
  call('Oheangbu.EditorTools.WorldMacro.PlaytestRecoveryStreamingReview','Execute','abort');raise TimeoutError(mode)
 (root/'final_run_set.json').write_text(json.dumps(results,indent=2))
 time.sleep(1)
