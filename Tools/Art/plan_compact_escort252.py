"""Terrain-only carriage planning. Actual wheels, hull and water are checked in Unity."""
from pathlib import Path
import heapq, json, math
import numpy as np

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Art/World/Compact/Rebuild/Escort252'
h=np.fromfile(ROOT/'Art/World/Compact/Rebuild/Cartography/heights.f32',dtype='<f4').reshape(2400,1600)
def y(x,z):
    return float(h[round(z/2.5),round(x/2.5)])
def clear(a,b):
    d=math.dist(a,b)
    if d<.01:return True
    side=(-(b[1]-a[1])/d,(b[0]-a[0])/d)
    n=max(1,math.ceil(d/2.5))
    prev=[y(a[0]+side[0]*w,a[1]+side[1]*w) for w in (-4,0,4)]
    for i in range(1,n+1):
        p=[a[k]+(b[k]-a[k])*i/n for k in (0,1)]
        now=[y(p[0]+side[0]*w,p[1]+side[1]*w) for w in (-4,0,4)]
        if any(abs(v-u)/(d/n)>.19 for u,v in zip(prev,now)) or max(now)-min(now)>1.15:return False
        prev=now
    return True
def route(a,b):
    a=tuple(round(x/5) for x in a);b=tuple(round(x/5) for x in b)
    q=[(0,a)];cost={a:0};parents={};closed=set()
    while q:
        _,p=heapq.heappop(q)
        if p in closed:continue
        if p==b:break
        closed.add(p)
        for dx,dz in ((1,0),(-1,0),(0,1),(0,-1),(1,1),(-1,1),(1,-1),(-1,-1)):
            v=(p[0]+dx,p[1]+dz)
            if not(320<=v[0]<=550 and 330<=v[1]<=525):continue
            pa=(p[0]*5,p[1]*5);pb=(v[0]*5,v[1]*5)
            if not clear(pa,pb):continue
            length=math.hypot(dx,dz)*5;grade=abs(y(*pa)-y(*pb))/length
            score=cost[p]+length*(1+grade*grade*30)
            if score<cost.get(v,math.inf):
                cost[v]=score;parents[v]=p;heapq.heappush(q,(score+math.dist(v,b)*5,v))
    if b not in cost:raise RuntimeError(f'No 8m supported road {a} -> {b}')
    p=b;pts=[p]
    while p!=a:p=parents[p];pts.append(p)
    pts=[(x*5,z*5) for x,z in reversed(pts)]
    result=[pts[0]];i=0
    while i<len(pts)-1:
        j=min(i+12,len(pts)-1)
        while j>i+1 and not clear(pts[i],pts[j]):j-=1
        result.append(pts[j]);i=j
    return result

places=json.loads((OUT.parent/'layout.json').read_text(encoding='utf-8'))['Places']
nodes={p['Id']:(p['XZ']['x'],p['XZ']['y']) for p in places}
nodes['escort_departure']=(2700,2154)
ids=['escort_departure','road_pass','inspection_one','road_hamlet','inspection_two','capital_delivery','south_gate']
result=[]
for a,b in zip(ids,ids[1:]):
    points=route(nodes[a],nodes[b]);points[0]=nodes[a];points[-1]=nodes[b];length=sum(math.dist(p,q) for p,q in zip(points,points[1:]))
    result.append(dict(id='merchant__road_pass' if a=='escort_departure' else f'{a}__{b}',fromId=a,toId=b,points=[dict(x=x,y=z) for x,z in points],length=length))
OUT.mkdir(exist_ok=True)
(OUT/'route-plan.json').write_text(json.dumps(dict(routes=result),indent=2),encoding='utf-8')
print([(r['id'],round(r['length'],1)) for r in result])
