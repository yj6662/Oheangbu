"""Serialized #296 authoring/inspection. Live Unity is owned by the root agent."""
import argparse, contextlib, io, json
from playtest_polish import call
if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('command');p.add_argument('--wait',type=int,default=900);a=p.parse_args()
    if a.command.startswith('mum296:'):
        # The full receipt contains save hashes, bank candidates and movement
        # samples. Keep it on disk instead of flooding the console.
        with contextlib.redirect_stdout(io.StringIO()):
            reply=call('Oheangbu.EditorTools.WorldMacro.CompactArchitecture296','Run',a.command,a.wait)
        result=json.loads(reply['result'])
        summary={k:result.get(k) for k in ('status','active','finished','restored','phase','playStarts','terrainAcceptedSite','terrainSteps','failures')}
        summary['checks']=len(result.get('checks',[]))
        print(json.dumps(summary,ensure_ascii=False,indent=2))
    else:
        call('Oheangbu.EditorTools.WorldMacro.CompactArchitecture296','Run',a.command,a.wait)
