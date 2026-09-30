import bpy,json,statistics
from pathlib import Path
ROOT=Path('C:/Users/yj666/Oheangbu/Art/Characters/Sinmok272/rig')
bpy.ops.wm.open_mainfile(filepath=str(ROOT/'normalized.blend'));o=next(o for o in bpy.context.scene.objects if o.type=='MESH')
rows=[]
for z in [1,3,5,7,9,11]:
 for x in [-5,-3,0,3,5]:
  p=[v.co for v in o.data.vertices if abs(v.co.x-x)<.6 and abs(v.co.z-z)<.6]
  if p:rows.append(dict(x=x,z=z,count=len(p),y=[round(min(v.y for v in p),2),round(statistics.median(v.y for v in p),2),round(max(v.y for v in p),2)]))
(ROOT/'coordinates.json').write_text(json.dumps(rows,indent=2));print(json.dumps(rows))
