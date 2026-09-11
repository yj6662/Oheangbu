"""Record an already published item; requires successful delivery evidence."""
import json,sys
from pathlib import Path
o=Path('Art/SpellVFX120');m=json.loads(Path(sys.argv[1]).read_text(encoding='utf-8'))
a=json.loads((o/m['audit']).read_text(encoding='utf-8'))
assert a['status']=='PASS_REAL_UPDATE_LIFETIME_ONLY' and a['restored']
assert (o/(m['key']+'_REVIEW.html')).is_file()
d=json.loads((o/'NIEUN_PROGRESS.json').read_text(encoding='utf-8'))
r=next(x for x in d['rows'] if x['id']==m['id'])
r.update(status='TECHNICAL_PASS_USER_REVIEW_PENDING',version=int(m['version']),report=m['key']+'_REPORT.md',review=m['key']+'_REVIEW.html',audit=m['audit'],appMvid=a['appMvid'])
d['next']=next((x['id'] for x in d['rows'] if x['assigned'] and x['status']=='QUEUED'),None)
d['complete']=d['next'] is None
(o/'NIEUN_PROGRESS.json').write_text(json.dumps(d,ensure_ascii=False,indent=2),encoding='utf-8')
