import json,time,shutil
from pathlib import Path
from original_scope import call
OUT=Path('Art/SpellVFX120')
if 'playing=True' in call('Probe',None):raise RuntimeError('Capture active')
for method,path in [('BasicSixAudit',OUT/'BasicSix/contact_audit.json'),('ContactAudit',OUT/'ktp_contact_play_audit.json')]:
    previous=path.stat().st_mtime if path.exists() else 0
    print(call(method,None),flush=True)
    deadline=time.monotonic()+240
    while time.monotonic()<deadline:
        if path.exists() and path.stat().st_mtime>previous:
            try:r=json.loads(path.read_text(encoding='utf-8'))
            except json.JSONDecodeError:time.sleep(1);continue
            if r.get('status') not in ['RUNNING','PENDING']:
                print(method+' '+r['status']+' '+str(len(r['checks']))+' checks',flush=True)
                if r['status']!='PASS':raise RuntimeError(r)
                if path.parent!=OUT/'BasicSix':shutil.copyfile(path,OUT/'BasicSix'/path.name)
                break
        time.sleep(1)
    else:raise TimeoutError(method)
    while 'playing=True' in call('Probe',None):time.sleep(2)
