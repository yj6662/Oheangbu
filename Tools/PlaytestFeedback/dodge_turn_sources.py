from pathlib import Path
import json,hashlib
r=Path(__file__).resolve().parents[2];o=r/'Art/PlaytestRecovery/DodgeTurn'
rows=[]
for p in sorted((o/'Sources').glob('*.fbx')):
 rows.append(dict(file=p.name,sha256=hashlib.sha256(p.read_bytes()).hexdigest(),bytes=p.stat().st_size,source='https://www.mixamo.com/',character='X Bot',format='FBX Binary',skin=False,fps=60,keyframeReduction='none',overdrive=50,trim='full source',downloadDate='2026-09-14'))
(o/'SOURCE_LEDGER.json').write_text(json.dumps(rows,indent=2),encoding='utf-8')
d=json.loads((o/'Motion/AuthoredBoneMotion.json').read_text());c=next(c for c in d['clips'] if c['name']=='CrouchRoll')
for i in range(0,121,10):
 print(round(c['duration']*i/120,3), {n:round(c['poses'][(i*54+d['boneNames'].index(n))*7+2],3) for n in ['Hips','Head','RightFoot','LeftFoot']})
