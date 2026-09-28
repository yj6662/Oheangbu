"""Plan a walking corridor on the exported current terrain, without editing Unity assets."""
import heapq
import json
import math
from pathlib import Path
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Art/World/Compact/Rebuild/Progression251'
height = np.fromfile(ROOT/'Art/World/Compact/Rebuild/Cartography/heights.f32', dtype='<f4').reshape(2400,1600)
# Existing 2.5m samples; use 5m routing nodes and test intervening heights.
grid = height[::2,::2]
def y(p): return float(height[round(p[1]/2.5),round(p[0]/2.5)])
def clear(a,b):
    d=math.dist(a,b);n=max(1,math.ceil(d/2.5));prev=y(a)
    for i in range(1,n+1):
        p=(a[0]+(b[0]-a[0])*i/n,a[1]+(b[1]-a[1])*i/n);cur=y(p)
        if abs(cur-prev)/(d/n)>.34:return False
        prev=cur
    return True
def route(start,end):
    a=(round(start[0]/5),round(start[1]/5));b=(round(end[0]/5),round(end[1]/5))
    queue=[(0,a)];cost={a:0};parent={};closed=set()
    while queue:
        _,p=heapq.heappop(queue)
        if p in closed:continue
        closed.add(p)
        if p==b:break
        for dx,dz in [(1,0),(-1,0),(0,1),(0,-1),(1,1),(1,-1),(-1,1),(-1,-1)]:
            q=(p[0]+dx,p[1]+dz)
            if not (510<=q[0]<=740 and 430<=q[1]<=740):continue
            distance=math.hypot(dx,dz)*5;grade=abs(float(grid[q[1],q[0]]-grid[p[1],p[0]]))/distance
            if grade>.28:continue
            new=cost[p]+distance*(1+grade*grade*14)
            if new<cost.get(q,math.inf):cost[q]=new;parent[q]=p;heapq.heappush(queue,(new+math.dist(q,b)*5,q))
    if b not in cost:raise RuntimeError('No supported route '+str((start,end)))
    p=b;points=[p]
    while p!=a:p=parent[p];points.append(p)
    points=[(p[0]*5,p[1]*5) for p in reversed(points)]
    # Preserve natural bends; simplify only where the full slope probe remains walkable.
    simple=[points[0]];i=0
    while i<len(points)-1:
        j=min(len(points)-1,i+16)
        while j>i+1 and not clear(points[i],points[j]):j-=1
        simple.append(points[j]);i=j
    return simple

segments=[('village_logging251','village','logging',(2700,2180),(3340,2700)),
          ('logging_deep251','logging','deep_forest',(3340,2700),(3280,3150)),
          ('deep_sanctuary251','deep_forest','sanctuary',(3280,3150),(3210,3550))]
routes=[]
for name,frm,to,a,b in segments:
    points=route(a,b)
    # Leave village by its already-authored forest entrance, not through houses.
    if frm=='village':points=[(2700,2180),(2700,2204),(2680,2217),(2660,2210),(2660,2272),(2695,2328)]+route((2695,2328),b)[1:]
    routes.append(dict(id=name,fromId=frm,toId=to,points=[dict(x=x,y=z) for x,z in points],length=sum(math.dist(a,b) for a,b in zip(points,points[1:]))))
OUT.mkdir(exist_ok=True)
(OUT/'route-plan.json').write_text(json.dumps(dict(routes=routes),indent=2),encoding='utf-8')
print([(p['id'],round(p['length']),len(p['points'])) for p in routes])
