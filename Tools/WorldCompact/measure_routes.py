"""Compare authored routes using the persisted compact mapping. No scene mutations."""
from pathlib import Path
import json, math, yaml, bisect
ROOT=Path(__file__).resolve().parents[2]
PROJ=ROOT/'Oheangbu'
OUT=ROOT/'Art/World/WorldMacro/Compact'
def data(p):return yaml.safe_load('\n'.join(p.read_text(encoding='utf-8-sig').splitlines()[3:]))['MonoBehaviour']
mapping=data(PROJ/'Assets/_Project/Art/World/WorldCompact/Compression.asset')['Mapping']
def forward(axis,v):
    a=mapping[axis];segs=a['segments'];s=segs[max(0,min(len(segs)-1,bisect.bisect_left([s['sourceMax'] for s in segs],v)))];l=s['sourceMax']-s['sourceMin'];t=(v-s['sourceMin'])/l
    if v<a['sourceMin']:return a['targetMin']+(v-a['sourceMin'])*segs[0]['derivativeMin']
    if v>a['sourceMax']:return a['targetMax']+(v-a['sourceMax'])*segs[-1]['derivativeMax']
    return s['targetMin']+l*(s['derivativeMin']*t+(s['derivativeMax']-s['derivativeMin'])*t**3*(1-.5*t))
def transform(p):return dict(x=forward('xAxis',p['x']),y=p['y'],z=forward('zAxis',p['z']))
def stats(ps):
    grades=[];horizontal=0;length=0
    for a,b in zip(ps,ps[1:]):
        h=math.hypot(b['x']-a['x'],b['z']-a['z']);dy=abs(b['y']-a['y']);horizontal+=h;length+=math.hypot(h,dy)
        if h>.01:grades.append(math.degrees(math.atan2(dy,h)))
    grades.sort()
    return dict(metres=round(length,2),horizontalMetres=round(horizontal,2),maxGradeDegrees=round(max(grades,default=0),3),p95GradeDegrees=round(grades[int((len(grades)-1)*.95)] if grades else 0,3),segmentsAbove14=sum(g>14 for g in grades))
geo=data(PROJ/'Assets/_Project/Art/World/WorldMacro/WorldMacroSheet.asset')
routes=[]
for route in geo['Routes']:
    before=stats(route['Points']);after=stats([transform(p) for p in route['Points']])
    routes.append(dict(id=route['Id'],carriage=bool(route['Carriage']),width=route['Width'],before=before,after=after))
result=dict(scope='Authored centerline metric comparison, not physical ground/driving validation.',targetMetres=[4000,6000],routes=routes)
(OUT/'route_comparison.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(routes,ensure_ascii=False,indent=2))
