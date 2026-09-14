"""Package measured first-section evidence and a static walking reference; no game input."""
import csv
import hashlib
import json
import math
import shutil
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Art/World/WorldMacro/Playtest'
data = json.loads((OUT / 'connected_content.json').read_text(encoding='utf-8-sig'))
path = data['MainPath']
distance = lambda a, b: math.sqrt(sum((a[k]-b[k])**2 for k in ('x','y','z')))
length = sum(distance(a,b) for a,b in zip(path,path[1:]))
baseline = json.loads((OUT/'baseline.json').read_text(encoding='utf-8-sig'))
preserved = {p: {'unchanged': hashlib.sha256((ROOT/p).read_bytes()).hexdigest()==digest,
                 'sha256': hashlib.sha256((ROOT/p).read_bytes()).hexdigest()} for p,digest in baseline.items()}
(OUT/'preservation.json').write_text(json.dumps(preserved,indent=2),encoding='utf-8')
assert all(x['unchanged'] for x in preserved.values()), 'Protected originals changed'
raw = json.loads((OUT/'response_4d875508e7fa49059929a28be32a3b1d.json').read_text(encoding='utf-8-sig'))['result']
progress = json.loads(raw.split('\nfeet=')[0])
assert progress['ledger']['completed']==['mine_inquiry','logger','herbalist']
assert progress['ledger']['currency']==0 and 'load=primary' in raw
(OUT/'content_reload_check.txt').write_text('PASS fresh Play session loads mine inquiry and both NPC records from primary disk save; repeated logger conversation has one record and no reward.\n'+raw,encoding='utf-8')
images=OUT/'Images'; images.mkdir(exist_ok=True)
commits=[]
for name in ['mine','play','branch','inn','inn_play']:
    for ext in ['png','json']:
        src=OUT.parent/'Dressing'/f'Playtest_{name}.{ext}'
        shutil.copy2(src,images/src.name)
    commits.append(json.loads((images/f'Playtest_{name}.json').read_text())['commitRatio'])
with (OUT/'walk_route.csv').open('w',newline='',encoding='utf-8-sig') as f:
    writer=csv.writer(f);writer.writerow(['station','distance_m','feet_x','feet_y','feet_z'])
    walked=0
    for i,p in enumerate(path):
        if i: walked+=distance(path[i-1],p)
        writer.writerow([i,round(walked,2),p['x'],p['y'],p['z']])

# North is up; terrain is intentionally not invented in this reference diagram.
xmin=min(p['x'] for p in path)-110; xmax=max(p['x'] for p in path)+110
zmin=min(p['z'] for p in path)-110; zmax=max(p['z'] for p in path)+110
scale=min(850/(xmax-xmin),750/(zmax-zmin))
xy=lambda p:(75+(p['x']-xmin)*scale,145+(zmax-p['z'])*scale)
coords=lambda points:' '.join(f'{xy(p)[0]:.1f},{xy(p)[1]:.1f}' for p in points)
svg=['<svg xmlns="http://www.w3.org/2000/svg" width="1000" height="1060" viewBox="0 0 1000 1060">',
     '<rect width="1000" height="1060" fill="#eeeade"/>',
     '<g font-family="Malgun Gothic, sans-serif" fill="#252b29">',
     '<text x="55" y="58" font-size="28" font-weight="bold">첫 플레이 구간 · 사용자 보행 검토 경로</text>',
     f'<text x="55" y="90" font-size="17">TEST · 본선 {length:,.0f}m · 북쪽 ↑ · 실제 지면 좌표, 지형 배경 생략</text>']
for axis,low,high in [('x',xmin,xmax),('z',zmin,zmax)]:
    for n in range(math.ceil(low/250)*250,int(high)+1,250):
        a={'x':n if axis=='x' else xmin,'z':zmin if axis=='x' else n}
        b={'x':n if axis=='x' else xmax,'z':zmax if axis=='x' else n}
        ax,ay=xy(a);bx,by=xy(b)
        svg.append(f'<path d="M{ax},{ay} L{bx},{by}" stroke="#d3d2c8"/>')
        svg.append(f'<text x="{ax+3}" y="{ay-4}" font-size="11" fill="#737a75">{axis.upper()} {n}</text>')
svg += [f'<polyline points="{coords(path)}" fill="none" stroke="#a87926" stroke-width="4" stroke-linejoin="round"/>',
        f'<polyline points="{coords(data["BranchPath"])}" fill="none" stroke="#16848c" stroke-width="5"/>']
marks=[('1. 폐광 조사 / 첫 교전',data['StartFeet']),('2. 선택 지선',data['BranchPath'][-1]),
       ('3. 북쪽 사면',max(path,key=lambda p:p['z'])),('4. 기존 다리',min(path,key=lambda p:abs(p['x']-2330)+abs(p['z']-820))),
       ('5. 금표 주막',data['InnCheckpointFeet'])]
for name,p in marks:
    x,y=xy(p); tx=x-10 if x>650 else x+12; anchor='end' if x>650 else 'start'
    svg.append(f'<circle cx="{x}" cy="{y}" r="6" fill="#293e37"/><text x="{tx}" y="{y-12}" text-anchor="{anchor}" font-size="17" font-weight="bold">{name}</text>')
svg += ['<text x="55" y="960" font-size="17">금색: 본선 / 청록색: 선택 지선 (약 18m 편도)</text>',
        '<text x="55" y="995" font-size="16">Scene 뷰: Tools → 오행부 → 첫 플레이 구간 → 전체 보행 경로 선택</text>',
        '<text x="55" y="1024" font-size="15">물리 표본 검사 통과. 실제 보행 완주와 길 찾기·비주얼 판단은 미검증.</text></g></svg>']
(OUT/'WALK_ROUTE.svg').write_text('\n'.join(svg),encoding='utf-8')
(OUT/'package_summary.json').write_text(json.dumps({'routeLengthMetres':length,'routeStations':len(path),
    'preservedFiles':len(preserved),'captureMaxCommit':max(commits),'imageCount':5},indent=2),encoding='utf-8')
print((OUT/'package_summary.json').read_text())
