from pathlib import Path
import json,math
r=Path(__file__).resolve().parents[2];o=r/'Art/PlaytestRecovery/DodgeTurn'
p=sorted((o/'Live').glob('*/live_input.json'))[-1];d=json.loads(p.read_text(encoding='utf-8-sig'))
def dist(a,b):return math.sqrt(sum((a[k]-b[k])**2 for k in ['x','y','z']))
report={'source':str(p),'status':d['status'],'error':d.get('error'),'restored':d['restored'],'phases':{}}
for name in dict.fromkeys(x['phase'] for x in d['frames']):
 rows=[x for x in d['frames'] if x['phase']==name];first,last=rows[0],rows[-1]
 value={'states':list(dict.fromkeys(x['state'] for x in rows)),'turnSerial':[first['turnSerial'],last['turnSerial']],'lookRange':[min(x['look'] for x in rows),max(x['look'] for x in rows)],'bodyYawRange':[min(x['body'] for x in rows),max(x['body'] for x in rows)],'yawRange':[min(x['yaw'] for x in rows),max(x['yaw'] for x in rows)],'rollSamples':sum(x['rolling'] for x in rows),'dodgeSamples':sum(x['dodging'] for x in rows),'crouchEnd':last['crouching'],'heightEnd':last['height'],'displacement':dist(first['position'],last['position']),'leftFootDisplacement':dist(first['leftFoot'],last['leftFoot'])}
 report['phases'][name]=value
(o/'LIVE_ASSESSMENT.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
for n,v in report['phases'].items():print(n,v)
