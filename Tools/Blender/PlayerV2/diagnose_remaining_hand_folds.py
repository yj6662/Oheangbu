import bpy,json
from pathlib import Path
ROOT=Path(__file__).resolve().parents[3];ART=ROOT/'Art/PlayerV2';OUT=ART/'Inspect/HandsSelfFolds'
r=json.loads((OUT/'repair-report.json').read_text());bpy.ops.wm.open_mainfile(filepath=r['derivative']);o=bpy.data.objects['DosaV2_Hands'];o.data.calc_loop_triangles();tri=[list(t.vertices) for t in o.data.loop_triangles];locked=set(r['protectedIndices']);rows=[]
for a,b in next(p for p in r['results'] if p['poseId']=='grip_down')['pairs']:
 ids=tri[a]+tri[b];weights={}
 for i in ids:
  for g in o.data.vertices[i].groups:
   n=o.vertex_groups[g.group].name;weights[n]=weights.get(n,0)+g.weight
 rows.append({'a':a,'b':b,'verticesA':tri[a],'verticesB':tri[b],'lockedA':sum(i in locked for i in tri[a]),'lockedB':sum(i in locked for i in tri[b]),'dominantBones':sorted(weights.items(),key=lambda p:-p[1])[:3], 'restCenter':[sum(o.data.vertices[i].co[k] for i in ids)/6 for k in range(3)]})
(OUT/'remaining-diagnostic.json').write_text(json.dumps(rows,indent=2));print(json.dumps({'count':len(rows),'fullyLocked':sum(p['lockedA']==3 and p['lockedB']==3 for p in rows),'dominants':[p['dominantBones'][0][0] for p in rows]},indent=2))
