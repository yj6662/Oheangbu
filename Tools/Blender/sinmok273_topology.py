import bpy,json
from pathlib import Path
from collections import defaultdict
R=Path('C:/Users/yj666/Oheangbu/Art/Characters/Sinmok272/rig');bpy.ops.wm.open_mainfile(filepath=str(R/'normalized.blend'));o=next(o for o in bpy.context.scene.objects if o.type=='MESH')
parent=list(range(len(o.data.vertices)));keys={}
def find(i):
 while parent[i]!=i:parent[i]=parent[parent[i]];i=parent[i]
 return i
def union(a,b):parent[find(a)]=find(b)
for v in o.data.vertices:
 key=tuple(round(c,4) for c in v.co)
 if key in keys:union(v.index,keys[key])
 else:keys[key]=v.index
for e in o.data.edges:union(*e.vertices)
components=defaultdict(list)
for v in o.data.vertices:components[find(v.index)].append(v.index)
rows=[]
for ids in sorted(components.values(),key=len,reverse=True):
 ps=[o.data.vertices[i].co for i in ids];rows.append(dict(n=len(ids),min=[min(p[k]for p in ps)for k in range(3)],max=[max(p[k]for p in ps)for k in range(3)],ids=ids))
(R/'topology.json').write_text(json.dumps(rows));print(json.dumps([{k:v for k,v in r.items()if k!='ids'}for r in rows[:25]]))
