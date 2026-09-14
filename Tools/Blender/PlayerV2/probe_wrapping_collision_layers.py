from pathlib import Path
import ast,collections
source=Path(__file__).with_name('repair_wrapping_inner_wall.py').read_text();exec(compile(source.split('bm=bmesh.new()')[0],'setup','exec'))
exec(compile(ast.fix_missing_locations(ast.Module(body=[next(n for n in ast.parse(source).body if isinstance(n,ast.FunctionDef) and n.name=='measure')],type_ignores=[])),'measure','exec'))
rows={}
for side in ['L','R']:
 obj=bpy.data.objects['DosaV2_SleeveInner_'+side];obj.data.calc_loop_triangles();tri=[t.vertices for t in obj.data.loop_triangles]
 inner=set()
 for v in obj.data.vertices:
  x=abs(v.co.x)
  if min(abs(x-a) for a in [.426,.645,.663,.683])>1e-6:continue
  z=1.336-(x-.442)/(.663-.442)*.009;w=np.interp(x,[.426,.46,.53,.60,.645,.663,.683],[.045,.043,.037,.031,.028,.029,.036])-.0016;d=np.interp(x,[.426,.46,.53,.60,.645,.663,.683],[.036,.035,.030,.025,.021,.019,.019])-.0016
  if abs((v.co.y/w)**2+((v.co.z-z)/d)**2-1)<1e-3:inner.add(v.index)
 out=[]
 for pid in ['grip_down','combined_reach','elbow_120','forearm_minus90','forearm_plus90']:
  r=measure(pid);counter=collections.Counter()
  for hit in r['selfPairs']:
   counts=[sum(int(v) in inner for v in tri[hit[k]]) for k in ['a','b']];names=[{0:'outer',3:'inner'}.get(c,'cap') for c in counts];counter[' x '.join(sorted(names))]+=1
  out.append({'poseId':pid,'pairClasses':dict(counter)})
 rows[side]={'innerVertices':len(inner),'poses':out}
(OUT/'layer-diagnostic.json').write_text(json.dumps(rows,indent=2));print(json.dumps(rows,indent=2))
