"""Compact single-editor transport for C02 rerig operations; never builds."""
import contextlib,io,json,sys
from pathlib import Path
import world_macro
world_macro.OUT=Path(__file__).resolve().parents[2]/'Art/World/WorldMacro/Playtest'
command=sys.argv[1] if len(sys.argv)>1 else 'status'
with contextlib.redirect_stdout(io.StringIO()):reply=world_macro.call('PlayerReRig',command)
result=reply.get('result',reply)
try:result=json.loads(result)
except (ValueError,TypeError):pass
if isinstance(result,dict):
 result={k:v for k,v in result.items() if k not in ('transforms','meshes','bonePoses','fingerPoses') or not isinstance(v,(list,dict))}
if isinstance(result,dict):
 if command=='runtime':result.pop('fingers',None)
 if command.startswith('gait'):
  for clip in result.get('clips',[]):clip.pop('samples',None)
 if isinstance(result.get('checks'),list):result['checks']=[c for c in result['checks']if c.get('status','').startswith(('FAIL','UNVERIFIED'))]
print(json.dumps(result,ensure_ascii=False,indent=2))
