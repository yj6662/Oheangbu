import bpy,json
from pathlib import Path
r=Path(__file__).resolve().parents[2]
p=r/'Art/World/Compact/Rebuild/Mountain285/Sources/modular_wooden_pier'
bpy.ops.wm.open_mainfile(filepath=str(p/'modular_wooden_pier.blend'))
rows=[]
for ob in bpy.data.objects:
 if ob.type=='MESH':
  rows.append(dict(name=ob.name,vertices=len(ob.data.vertices),dims=list(ob.dimensions),location=list(ob.location),rotation=list(ob.rotation_euler),materials=[m.name for m in ob.data.materials if m],modifiers=[m.type for m in ob.modifiers]))
(p/'inventory.json').write_text(json.dumps(rows,indent=2))
print(json.dumps(rows,indent=2))
for name in ['modular_wooden_pier_planks','modular_wooden_pier_poles']:
 ob=bpy.data.objects[name];me=ob.data
 adjacent=[set() for _ in me.vertices]
 for e in me.edges:
  a,b=e.vertices;adjacent[a].add(b);adjacent[b].add(a)
 unseen=set(range(len(me.vertices)));parts=[]
 while unseen:
  stack=[unseen.pop()];ids=[]
  while stack:
   i=stack.pop();ids.append(i)
   for k in adjacent[i]:
    if k in unseen:unseen.remove(k);stack.append(k)
  coords=[me.vertices[i].co for i in ids]
  lo=[min(v[j] for v in coords) for j in range(3)];hi=[max(v[j] for v in coords) for j in range(3)]
  parts.append(dict(ids=ids,lo=lo,hi=hi,size=[hi[i]-lo[i] for i in range(3)]))
 (p/(name+'-parts.json')).write_text(json.dumps(parts))
 print(name,[(len(t['ids']),t['size']) for t in parts])
