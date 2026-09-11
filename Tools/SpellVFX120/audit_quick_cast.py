import json,time,shutil
from pathlib import Path
from original_scope import call
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Art/SpellVFX120'
if 'playing=True' in call('Probe',None):raise RuntimeError('Capture still active; wait before audit')
for method,path in [('EmphasisAudit',OUT/'Emphasis4/contact_audit.json'),('ContactAudit',OUT/'ktp_contact_play_audit.json')]:
    before=path.stat().st_mtime if path.exists() else 0
    backup=OUT/'Emphasis4'/('v3_'+path.name)
    if path.exists() and not backup.exists():shutil.copyfile(path,backup)
    print(call(method,None),flush=True)
    deadline=time.monotonic()+240
    while time.monotonic()<deadline:
        if path.exists() and path.stat().st_mtime>before:
            try:r=json.loads(path.read_text(encoding='utf-8'))
            except json.JSONDecodeError:time.sleep(1);continue
            if r.get('status') not in ['RUNNING','PENDING']:
                print(method+' '+json.dumps(r,ensure_ascii=False),flush=True)
                if r['status']!='PASS':raise RuntimeError(r)
                shutil.copyfile(path,OUT/'Emphasis4'/('v4_'+path.name))
                break
        time.sleep(1)
    else:raise TimeoutError(method)
    while 'playing=True' in call('Probe',None):time.sleep(2)
