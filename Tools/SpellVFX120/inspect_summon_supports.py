import bpy,json,sys
from pathlib import Path
name=sys.argv[sys.argv.index('--')+1];O=Path('C:/Users/yj666/Oheangbu/Art/SpellVFX120')/name
bpy.ops.wm.open_mainfile(filepath=str(O/(name+'_Working.blend')));body=bpy.data.objects[name+'_Body'];verts=[v.co for v in body.data.vertices]
rows=[]
for lo,hi in [(-2,-.6),(-.6,-.15),(-.15,.15),(.15,.6),(.6,2)]:
 points=[v for v in verts if lo<v.x<hi]
 if points:
  p=min(points,key=lambda v:v.z);rows.append(dict(xRange=[lo,hi],minPoint=list(p)))
(O/'support_inspect.json').write_text(json.dumps(rows,indent=2));print(rows,flush=True)
