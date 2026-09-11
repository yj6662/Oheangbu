from pathlib import Path
R=Path(__file__).resolve().parents[2]
s=(R/'Tools/SpellVFX120/build_stone_dokkaebi.py').read_text().replace('StoneDokkaebi','StoneJangseung').replace('STONE_DOKKAEBI','STONE_JANGSEUNG').replace('2.05/(hi.z-lo.z)','2.6/(hi.z-lo.z)')
start=s.index('hand_centers={}');end=s.index('for poly in body.data.polygons:',start)
s=s[:start]+'''edits={'crownVertices':0,'baseVertices':0,'eyeVertices':0}
for p in body.data.vertices:
 q=p.co.copy()
 if q.z>2.09:q.z=2.09+(q.z-2.09)*.22;edits['crownVertices']+=1
 if q.z<.38:
  w=1-smooth(.20,.38,q.z);q.x*=1-.27*w;q.y*=1-.22*w;edits['baseVertices']+=1
 # Broaden round stone eyes within their existing face UVs; no painted pupils.
 for side in [-1,1]:
  d=((q.x-side*.145)/.135)**2+((q.z-1.94)/.115)**2
  if d<1 and q.y<-.12:
   q.y-=.105*(1-d)**.65;edits['eyeVertices']+=1
 q.x*=1.12
 p.co=q
''' +s[end:]
s=s.replace("sourceActualTriangles=12547","sourceActualTriangles=len(original.polygons)")
s=s.replace("Vector((0,0,.95))","Vector((0,0,1.1))").replace("Vector((0,0,.96))","Vector((0,0,1.1))").replace("ortho_scale=3.8","ortho_scale=4.4").replace("Vector((0,-.05,1.5))","Vector((0,-.05,1.8))").replace("ortho_scale=2.5","ortho_scale=2.5")
s=s.replace("'New Meshy7 source", "'New Meshy7 source")
(R/'Tools/SpellVFX120/build_stone_jangseung.py').write_text(s)
print('BUILDER_READY')
