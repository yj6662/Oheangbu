from pathlib import Path
import ast
source=Path(__file__).with_name('repair_wrapping_inner_wall.py').read_text()
exec(compile(source.split('bm=bmesh.new()')[0],'wrap_setup','exec'))
module=ast.parse(source);fn=next(n for n in module.body if isinstance(n,ast.FunctionDef) and n.name=='measure');exec(compile(ast.fix_missing_locations(ast.Module(body=[fn],type_ignores=[])),'wrap_measure','exec'))
results={}
for side in ['L','R']:
 obj=bpy.data.objects['DosaV2_SleeveInner_'+side]
 results[side]=[measure(p) for p in ['rest','grip_down','grip_up','combined_reach','elbow_120','forearm_minus90','forearm_plus90']]
(OUT/'baseline-expanded.json').write_text(json.dumps(results,indent=2));print(json.dumps({s:[{k:v for k,v in r.items() if k!='selfPairs'} for r in rows] for s,rows in results.items()},indent=2))
