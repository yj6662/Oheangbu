import json,numpy as np
from collections import Counter
from pathlib import Path
ROOT=Path(__file__).resolve().parents[3];OUT=ROOT/'Art/PlayerV2/Inspect/ClothBlender/GussetAnchor'
data=json.loads((ROOT/'Art/PlayerV2/Inspect/ClothBlender/PosedArmFit9f4cd412/topology-rest.json').read_text())
report={}
for name,m in data['topology'].items():
    if 'SleeveOuter' not in name:continue
    points=np.array(m['restVertices']);remaining={i for i,v in enumerate(m['mobilityMeters']) if v==0};adj={i:set() for i in remaining}
    for triangle in m['triangles']:
        for a,b in zip(triangle,triangle[1:]+triangle[:1]):
            if a in remaining and b in remaining:adj[a].add(b);adj[b].add(a)
    components=[]
    while remaining:
        todo=[remaining.pop()];found=set(todo)
        while todo:
            a=todo.pop();neighbors=adj[a]&remaining;todo.extend(neighbors);remaining-=neighbors;found|=neighbors
        ids=sorted(found);weights=Counter()
        for i in ids:weights.update(m['deformWeights'][i])
        components.append({'count':len(ids),'minimum':points[ids].min(0).tolist(),'maximum':points[ids].max(0).tolist(),'sourceVertices':ids,
                           'meanWeights':{n:w/len(ids) for n,w in weights.most_common()}})
    components.sort(key=lambda r:r['count'],reverse=True);report[name]=components
(OUT/'pinned-components.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print(json.dumps({n:[{k:v for k,v in r.items() if k!='sourceVertices'} for r in rows] for n,rows in report.items()},indent=2))
