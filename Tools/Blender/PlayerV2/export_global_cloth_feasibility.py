"""Read-only frozen AF6 kinematic graph dataset, no solver approximations."""
import bpy,json,ast,hashlib
from pathlib import Path
import numpy as np
ROOT=Path(__file__).resolve().parents[3];BASE=ROOT/'Art/PlayerV2/Inspect/ClothBlender/FoldedGusset'
OUT=ROOT/'Art/PlayerV2/Inspect/ClothBlender/GlobalPathFeasibility';OUT.mkdir(parents=True,exist_ok=True)
SOURCE=BASE/'DosaV2_FoldedGusset.blend';bpy.ops.wm.open_mainfile(filepath=str(SOURCE));rig=bpy.data.objects['DosaV2_Rig']
helper=Path(__file__).with_name('fit_gusset_anchor_field.py');nodes=[n for n in ast.parse(helper.read_text()).body if isinstance(n,ast.FunctionDef) and n.name=='skin'];exec(compile(ast.Module(body=nodes,type_ignores=[]),str(helper),'exec'))
restinverse={b.name:np.linalg.inv(np.array(b.matrix_local)) for b in rig.data.bones}
defs=json.loads((BASE/'Inputs/static-pose-definitions.json').read_text())['poses']
poses=[]
for d in defs:poses.append((d['id'],{n:np.array(m)@restinverse[n] for n,m in d['boneMatricesRigLocal'].items()}))
for name in ['fixture_rest_settle','fixture_grip_settle','fixture_raised_arms_settle']:
    p=json.loads((BASE/'IntermediatePoseData'/((name+'_100.json') if name!='fixture_rest_settle' else 'fixture_grip_settle_00.json')).read_text())
    poses.append((name,{n:np.array(m)@restinverse[n] for n,m in p['bonePoseMatrices'].items()}))
for path in sorted((BASE/'IntermediatePoseData').glob('*.json')):
    p=json.loads(path.read_text());poses.append((p['poseId'],{n:np.array(m)@restinverse[n] for n,m in p['bonePoseMatrices'].items()}))
rows=[]
for name in ['DosaV2_Robe_Combined','DosaV2_SleeveOuter_L','DosaV2_SleeveOuter_R']:
    o=bpy.data.objects[name];o.data.calc_loop_triangles();rest=np.array([v.co[:] for v in o.data.vertices]);w=[{o.vertex_groups[g.group].name:g.weight for g in v.groups} for v in o.data.vertices]
    triangles=np.array([list(t.vertices) for t in o.data.loop_triangles]);edges=np.array(sorted({tuple(sorted((a,b))) for t in triangles for a,b in zip(t,np.roll(t,-1))}));mob=np.array([v.color[0] for v in o.data.color_attributes['ClothMobility'].data])
    points=np.array([skin(rest,w,m) for pose,m in poses]);file=OUT/(name+'.npz');np.savez_compressed(file,rest=rest,edges=edges,mobility=mob,posePoints=points,poseNames=np.array([p for p,m in poses]))
    rows.append({'mesh':name,'vertices':len(rest),'triangles':len(triangles),'edges':len(edges),'exactPins':int(np.sum(mob==0)),'datasetSha256':hashlib.sha256(file.read_bytes()).hexdigest()})
report={'source':str(SOURCE),'sourceSha256':hashlib.sha256(SOURCE.read_bytes()).hexdigest(),'poseCount':len(poses),'staticPoseCount':25,'sourceCount':len(restinverse),'meshes':rows,
 'method':'Stored canonical Blender direct-pose matrices and evaluated 404 intermediate matrices applied to exactly frozen AF6 rest points/normalized deform weights. Graph includes every actual triangle edge, including all inserted fold edges; no mobility threshold or geometry edits.'}
(OUT/'dataset-manifest.json').write_text(json.dumps(report,indent=2),encoding='utf-8');print(json.dumps(report,indent=2),flush=True)
