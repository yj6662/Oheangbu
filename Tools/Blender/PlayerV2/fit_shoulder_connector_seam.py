"""Independent four-point seam fit against actual complete shoulder geometry."""
import bpy,json,ast,math,hashlib
from pathlib import Path
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree
ROOT=Path(__file__).resolve().parents[3];BASE=ROOT/'Art/PlayerV2/Inspect/ClothBlender/FoldedGusset'
OUT=ROOT/'Art/PlayerV2/Inspect/ClothBlender/ShoulderConnection';SOURCE=OUT/'DosaV2_ShoulderConnection_Candidate.blend'
bpy.ops.wm.open_mainfile(filepath=str(SOURCE));rig=bpy.data.objects['DosaV2_Rig']
helper=Path(__file__).with_name('solve_structural_shoulder_seam.py')
nodes=[n for n in ast.parse(helper.read_text()).body if isinstance(n,ast.FunctionDef) and n.name in ['outside_intervals','inside','intersect_intervals','one_dim_interval','project']];exec(compile(ast.Module(body=nodes,type_ignores=[]),str(helper),'exec'))
helper=Path(__file__).with_name('fit_gusset_anchor_field.py');nodes=[n for n in ast.parse(helper.read_text()).body if isinstance(n,ast.FunctionDef) and n.name=='skin'];exec(compile(ast.Module(body=nodes,type_ignores=[]),str(helper),'exec'))
directions=[Vector(d).normalized() for d in [(1,.371,.219),(.173,1,.293),(.271,.123,1)]]
models={}
for o in bpy.context.scene.objects:
    if o.type!='MESH' or not ('Lining' in o.name or o.name in ['DosaV2_BodyCore','DosaV2_Robe_Combined','DosaV2_SleeveOuter_L','DosaV2_SleeveOuter_R']):continue
    o.data.calc_loop_triangles();mob=o.data.color_attributes.get('ClothMobility')
    models[o.name]={'rest':np.array([v.co[:] for v in o.data.vertices]),'weights':[{o.vertex_groups[g.group].name:g.weight for g in v.groups} for v in o.data.vertices],
      'triangles':np.array([list(t.vertices) for t in o.data.loop_triangles]),'edges':np.array([list(e.vertices) for e in o.data.edges]),'mobility':np.array([v.color[0] for v in mob.data]) if mob else None}
mesh=models['DosaV2_SleeveOuter_L'];variables=[];mapping={}
for i in [28,339,340,343]:
    row=mesh['weights'][i];f=row['LeftArm'];mapping[i]=len(variables);variables.append({'vertex':i,'fraction':f,'other':{n:w/(1-f) for n,w in row.items() if n!='LeftArm'},'allowed':[(0.,1.)]})
rinv={b.name:np.linalg.inv(np.array(b.matrix_local)) for b in rig.data.bones};constraints=[];posed=[]
for file in sorted((BASE/'IntermediatePoseData').glob('*.json')):
    p=json.loads(file.read_text());mat={n:np.array(m)@rinv[n] for n,m in p['bonePoseMatrices'].items()}
    trees={n:BVHTree.FromPolygons(skin(m['rest'],m['weights'],mat).tolist(),m['triangles'].tolist(),all_triangles=True) for n,m in models.items() if 'Lining' in n}
    points=skin(mesh['rest'],mesh['weights'],mat);vars={}
    for j,v in enumerate(variables):
        point=np.r_[mesh['rest'][v['vertex']],1.];base=sum((mat[n]@point)[:3]*w for n,w in v['other'].items());delta=(mat['LeftArm']@point)[:3]-base;vars[j]=(base,delta)
        for tree in trees.values():v['allowed']=intersect_intervals(v['allowed'],outside_intervals(tree,base,delta))
    for a,b in mesh['edges']:
        if a not in mapping and b not in mapping:continue
        ids=[];columns=[];constant=np.zeros(3)
        for vertex,sign in [(a,1),(b,-1)]:
            if vertex in mapping:
                idx=mapping[vertex];base,delta=vars[idx];ids.append(idx);columns.append(delta*sign);constant+=base*sign
            else:constant+=points[vertex]*sign
        length=np.linalg.norm(mesh['rest'][a]-mesh['rest'][b]);mob=float(mesh['mobility'][a]+mesh['mobility'][b])
        constraints.append({'poseId':p['poseId'],'vertices':[int(a),int(b)],'indices':np.array(ids,dtype=int),'A':np.column_stack(columns),'constant':constant,'radius':1.30*length+mob,'length':length,'mobility':mob})
    posed.append((p['poseId'],mat,trees))
print('ALLOWED '+json.dumps(variables),flush=True)
if any(not v['allowed'] for v in variables):
    (OUT/'four-point-seam-fit.json').write_text(json.dumps({'status':'NO_COLLISION_FREE_INTERVAL','variables':variables},indent=2));raise SystemExit(0)
lower=[];upper=[]
for v in variables:
    lo,hi=min(v['allowed'],key=lambda q:abs(v['fraction']-np.clip(v['fraction'],q[0],q[1])));lower.append(lo);upper.append(hi)
lower=np.array(lower);upper=np.array(upper);current=np.clip([v['fraction'] for v in variables],lower,upper);failed=[]
for iteration in range(100):
    maxres=0.;changes=0;failed=[]
    for row in constraints:
        residual=np.linalg.norm(row['constant']+row['A']@current[row['indices']])-row['radius'];maxres=max(maxres,residual)
        if residual<=1e-8:continue
        result=project(row,current)
        if result is None:failed.append({'poseId':row['poseId'],'vertices':row['vertices'],'residualMeters':float(residual)})
        else:current[row['indices']]=result;changes+=1
    if changes==0:break
changes=[];o=bpy.data.objects['DosaV2_SleeveOuter_L']
for v,f in zip(variables,current):
    before=mesh['weights'][v['vertex']];after={n:w*(1-f) for n,w in v['other'].items()};after['LeftArm']=float(f)
    for group in o.vertex_groups:group.remove([v['vertex']])
    for n,w in after.items():o.vertex_groups[n].add([v['vertex']],w,'REPLACE')
    mesh['weights'][v['vertex']]=after;changes.append({'vertex':v['vertex'],'before':before,'after':after})
rows=[]
for poseid,mat,trees in posed:
    p=skin(mesh['rest'],mesh['weights'],mat);e=mesh['edges'];m=mesh['mobility'];ratio=np.maximum(0,np.linalg.norm(p[e[:,0]]-p[e[:,1]],axis=1)-m[e[:,0]]-m[e[:,1]])/np.linalg.norm(mesh['rest'][e[:,0]]-mesh['rest'][e[:,1]],axis=1)
    hits=[]
    for v in variables:
        point=Vector(p[v['vertex']])
        for n,tree in trees.items():
            if inside(tree,point):hits.append({'vertex':v['vertex'],'lining':n,'depthMeters':tree.find_nearest(point)[3]})
    rows.append({'poseId':poseid,'maximumMinimumRequiredStretch':float(np.max(ratio)),'insidePins':hits})
DEST=OUT/'DosaV2_ShoulderConnection_Fit.blend';bpy.ops.wm.save_as_mainfile(filepath=str(DEST))
report={'status':'FOUR_POINT_ACTUAL_SHOULDER_SEAM_CANDIDATE','sourceSha256':hashlib.sha256(SOURCE.read_bytes()).hexdigest(),'outputSha256':hashlib.sha256(DEST.read_bytes()).hexdigest(),
 'variables':variables,'changes':changes,'individualConstraintFailures':failed,'maximumNecessaryStretch':max(r['maximumMinimumRequiredStretch'] for r in rows),
 'insideChangedPins':sum(len(r['insidePins']) for r in rows),'rows':rows}
(OUT/'four-point-seam-fit.json').write_text(json.dumps(report,indent=2),encoding='utf-8');print(json.dumps({k:v for k,v in report.items() if k not in ['variables','changes','individualConstraintFailures','rows']},indent=2),flush=True)
