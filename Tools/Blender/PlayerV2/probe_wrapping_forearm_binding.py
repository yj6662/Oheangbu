"""Treat the authored forearm wrapping as forearm fabric, rather than elbow-spanning upper sleeve."""
from pathlib import Path
import ast
source=Path(__file__).with_name('repair_wrapping_inner_wall.py').read_text();exec(compile(source.split('bm=bmesh.new()')[0],'setup','exec'))
exec(compile(ast.fix_missing_locations(ast.Module(body=[next(n for n in ast.parse(source).body if isinstance(n,ast.FunctionDef) and n.name=='measure')],type_ignores=[])),'measure','exec'))
rows={}
for side,short in [('Left','L'),('Right','R')]:
 obj=bpy.data.objects['DosaV2_SleeveInner_'+short];ids={i for p in obj.data.polygons if obj.data.materials[p.material_index].name=='DosaV2_WristWrapping' for i in p.vertices}
 for i in ids:
  hand=smooth(.638,.665,abs(obj.data.vertices[i].co.x))
  for g in obj.vertex_groups:g.remove([i])
  for name,w in [(side+'ForeArm',1-hand),(side+'Hand',hand)]:
   if w>0:obj.vertex_groups[name].add([i],w,'REPLACE')
 rows[short]=[measure(p['id']) for p in defs['poses'] if p['id'] in ['rest','grip_down','grip_up','combined_reach','elbow_120','forearm_minus90','forearm_plus90','wrist_flex','wrist_extend','wrist_flex_left','wrist_flex_right']]
for b in rig.pose.bones:b.matrix_basis=Matrix.Identity(4)
bpy.context.view_layer.update();dest=ART/'DosaV2_WrappingForearmProbe.blend';bpy.ops.wm.save_as_mainfile(filepath=str(dest));report={'candidate':str(dest),'results':rows}
(OUT/'forearm-probe.json').write_text(json.dumps(report,indent=2));print(json.dumps({s:[{k:v for k,v in r.items() if k!='selfPairs'} for r in rows] for s,rows in rows.items()},indent=2))
