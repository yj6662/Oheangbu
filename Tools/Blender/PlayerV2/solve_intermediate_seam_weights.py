"""Convex local seam-weight corrections inside measured collision-free intervals.

Only the Arm fraction of selected fixed front-seam vertices is variable. Its
other three weights retain their relative proportions. Pose-dependent outside
intervals come from actual closed anatomy ray intersections, never capsules.
All incident edges and all25 declared poses retain their original motion balls.
"""
import bpy,ast,json,math,hashlib
from pathlib import Path
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree
ROOT=Path(__file__).resolve().parents[3];DATA=ROOT/'Art/PlayerV2/Inspect/ClothBlender/PosedArmFit9f4cd412'
OUT=ROOT/'Art/PlayerV2/Inspect/ClothBlender/GussetAnchor';OUT.mkdir(parents=True,exist_ok=True)
HELPER=OUT/'candidate-script-r3.py'
nodes=[n for n in ast.parse(HELPER.read_text()).body if isinstance(n,ast.FunctionDef) and n.name in ['smooth','geometry','coherent','skin']]
exec(compile(ast.Module(body=nodes,type_ignores=[]),str(HELPER),'exec'))
topology=json.loads((DATA/'topology-rest.json').read_text());poses=[]
for file in DATA.glob('*.json'):
    p=json.loads(file.read_text())
    if 'poseId' in p:poses.append(p)
for file in (ROOT/'Art/PlayerV2/Inspect/ClothBlender/FeasibleGusset/IntermediatePoseData').glob('*.json'):
    poses.append(json.loads(file.read_text()))
for file in (ROOT/'Art/PlayerV2/Inspect/ClothBlender/ContinuousGusset/IntermediatePoseData').glob('combined_reach_*.json'):
    data=json.loads(file.read_text());data['poseId']='dense_'+data['poseId'];poses.append(data)
names=['DosaV2_SleeveOuter_L','DosaV2_SleeveOuter_R'];meshes={};variables=[]
for name in names:
    m=topology['topology'][name];rest=geometry(np.array(m['restVertices']),.145);weights=coherent(rest,m['deformWeights'],1.24,1.34,.28)
    mobility=np.array(m['mobilityMeters']);edges=np.array(sorted({tuple(sorted((a,b))) for t in m['triangles'] for a,b in zip(t,t[1:]+t[:1])}))
    arm=('Left' if name.endswith('_L') else 'Right')+'Arm';mapping={}
    for i,p in enumerate(rest):
        if mobility[i]!=0 or not(abs(p[0])<.25 and 1.15<p[2]<1.36 and p[1]<.03):continue
        fraction=weights[i].get(arm,0);other={n:w/(1-fraction) for n,w in weights[i].items() if n!=arm}
        if not other:continue
        mapping[i]=len(variables);variables.append({'mesh':name,'vertex':i,'arm':arm,'rest':p,'fraction':fraction,'other':other,
            'lower':0.,'upper':1.,'allowedIntervals':[(0.,1.)],'originalWeights':weights[i]})
    meshes[name]={'rest':rest,'weights':weights,'mobility':mobility,'edges':edges,'mapping':mapping}
directions=[Vector(d).normalized() for d in [(1,.371,.219),(.173,1,.293),(.271,.123,1)]]
def inside(tree,point):
    votes=0
    for ray in directions:
        origin=point+ray*1e-6;count=0
        for _ in range(64):
            hit=tree.ray_cast(origin,ray,10)
            if hit[0] is None:break
            count+=1;origin=hit[0]+ray*5e-6
        votes+=count%2
    return votes>=2
def outside_intervals(tree,base,delta):
    length=np.linalg.norm(delta)
    if length<1e-9:return [] if inside(tree,Vector(base)) else [(0.,1.)]
    ray=Vector(delta/length);origin=Vector(base);cuts=[0.,1.];travel=0.
    for _ in range(128):
        hit=tree.ray_cast(origin,ray,max(0.,length-travel))
        if hit[0] is None:break
        travel+=hit[3];cuts.append(float(np.clip(travel/length,0,1)));travel+=5e-7
        if travel>=length:break
        origin=Vector(base)+ray*travel
    cuts=sorted(set(cuts));allowed=[];guard=2e-6/length
    for a,b in zip(cuts[:-1],cuts[1:]):
        if b-a<1e-10:continue
        if not inside(tree,Vector(base+delta*((a+b)*.5))):
            lo=a+guard if a>0 else a;hi=b-guard if b<1 else b
            if hi>=lo:allowed.append((lo,hi))
    return allowed
def intersect_intervals(a,b):
    return [(max(x0,y0),min(x1,y1)) for x0,x1 in a for y0,y1 in b if min(x1,y1)>=max(x0,y0)]
models={};constraints=[]
for pose in poses:
    matrices={n:np.array(m)@np.linalg.inv(np.array(topology['boneRestMatrices'][n])) for n,m in pose['bonePoseMatrices'].items()}
    trees=[(n,BVHTree.FromPolygons(pose['vertices'][n],m['triangles'],all_triangles=True)) for n,m in topology['topology'].items() if 'Lining' in n]
    var_pose={}
    for index,var in enumerate(variables):
        point=np.r_[var['rest'],1];base=sum((matrices[n]@point)[:3]*w for n,w in var['other'].items());delta=(matrices[var['arm']]@point)[:3]-base
        current=base+delta*var['fraction'];length=np.linalg.norm(delta)
        if length>1e-8:
            for name,tree in trees:
                var['allowedIntervals']=intersect_intervals(var['allowedIntervals'],outside_intervals(tree,base,delta))
        var_pose[index]=(base,delta)
    for name,mesh in meshes.items():
        rest,edges,mob=mesh['rest'],mesh['edges'],mesh['mobility'];points=skin(rest,mesh['weights'],matrices);mapping=mesh['mapping']
        for a,b in edges:
            if a not in mapping and b not in mapping:continue
            ids=[];columns=[];constant=np.zeros(3)
            for vertex,sign in [(a,1),(b,-1)]:
                if vertex in mapping:
                    index=mapping[vertex];base,delta=var_pose[index];constant+=base*sign;ids.append(index);columns.append(delta*sign)
                else:constant+=points[vertex]*sign
            rest_length=np.linalg.norm(rest[a]-rest[b]);allowed=1.34*rest_length+mob[a]+mob[b]
            constraints.append({'poseId':pose['poseId'],'mesh':name,'vertices':[int(a),int(b)],'indices':np.array(ids,dtype=int),
                'A':np.column_stack(columns),'constant':constant,'radius':allowed,'restLength':rest_length,'mobility':mob[a]+mob[b]})
    models[pose['poseId']]={'matrices':matrices,'trees':trees}
empty=[{'mesh':v['mesh'],'vertex':v['vertex']} for v in variables if not v['allowedIntervals']]
if empty:
    (OUT/'intermediate-weight-solution.json').write_text(json.dumps({'status':'NO_COMMON_OUTSIDE_ARM_FRACTION_INTERVAL','emptyVariables':empty,
      'variables':[{k:(value.tolist() if isinstance(value,np.ndarray) else value) for k,value in v.items()} for v in variables]},indent=2))
    print('EMPTY_OUTSIDE_INTERVALS '+json.dumps(empty),flush=True);raise SystemExit(0)
for var in variables:
    lo,hi=min(var['allowedIntervals'],key=lambda p:abs(var['fraction']-np.clip(var['fraction'],p[0],p[1])))
    var['lower'],var['upper']=lo,hi
initial=np.array([v['fraction'] for v in variables]);lower=np.maximum(0,[v['lower'] for v in variables]);upper=np.minimum(1,[v['upper'] for v in variables])

def one_dim_interval(c,a,r):
    aa=np.dot(a,a);bb=2*np.dot(c,a);cc=np.dot(c,c)-r*r
    if aa<1e-20:return (-np.inf,np.inf) if cc<=1e-15 else None
    disc=bb*bb-4*aa*cc
    if disc<0:return None
    root=math.sqrt(max(0,disc));return ((-bb-root)/(2*aa),(-bb+root)/(2*aa))

def project(row,current):
    ids=row['indices'];u0=current[ids];A=row['A'];c=row['constant'];r=row['radius'];lo,hi=lower[ids],upper[ids]
    if np.linalg.norm(c+A@u0)<=r+1e-10:return u0
    if len(ids)==1:
        interval=one_dim_interval(c,A[:,0],r)
        if interval is None:return None
        l=max(lo[0],interval[0]);h=min(hi[0],interval[1])
        return np.array([np.clip(u0[0],l,h)]) if l<=h+1e-10 else None
    candidates=[];G=A.T@A;q=A.T@c
    def value(lam):return np.linalg.solve(np.eye(2)+lam*G,u0-lam*q)
    high=1.
    while high<1e15 and np.linalg.norm(c+A@value(high))>r:high*=4
    if high<1e15:
        low=0
        for _ in range(40):
            mid=(low+high)*.5
            if np.linalg.norm(c+A@value(mid))>r:low=mid
            else:high=mid
        result=value(high)
        if np.all(result>=lo-1e-9) and np.all(result<=hi+1e-9):candidates.append(np.clip(result,lo,hi))
    for fixed in [0,1]:
        moving=1-fixed
        for boundary in [lo[fixed],hi[fixed]]:
            interval=one_dim_interval(c+A[:,fixed]*boundary,A[:,moving],r)
            if interval is None:continue
            l=max(lo[moving],interval[0]);h=min(hi[moving],interval[1])
            if l>h+1e-10:continue
            result=u0.copy();result[fixed]=boundary;result[moving]=np.clip(u0[moving],l,h);candidates.append(result)
    if not candidates:return None
    return min(candidates,key=lambda u:np.sum((u-u0)**2))

current=np.clip(initial,lower,upper);unprojectable=[];history=[]
for iteration in range(600):
    worst=0;violations=0;unprojectable=[]
    # Larger violations first, then preserve the fixed full constraint set.
    order=sorted(range(len(constraints)),key=lambda i:np.linalg.norm(constraints[i]['constant']+constraints[i]['A']@current[constraints[i]['indices']])-constraints[i]['radius'],reverse=True)
    for index in order:
        row=constraints[index];length=np.linalg.norm(row['constant']+row['A']@current[row['indices']]);violation=length-row['radius']
        worst=max(worst,violation)
        if violation<=1e-8:continue
        violations+=1;result=project(row,current)
        if result is None:unprojectable.append(index)
        else:current[row['indices']]=result
    history.append({'iteration':iteration,'violations':violations,'maximumLengthResidualMeters':worst,'individuallyInfeasible':len(unprojectable)})
    if iteration%20==0:print(json.dumps(history[-1]),flush=True)
    if violations==0 or unprojectable:break
records=[]
for i,(var,fraction) in enumerate(zip(variables,current)):
    weights={n:w*(1-fraction) for n,w in var['other'].items()};weights[var['arm']]=float(fraction)
    meshes[var['mesh']]['weights'][var['vertex']]=weights
    records.append({k:v.tolist() if isinstance(v,np.ndarray) else v for k,v in var.items() if k!='rest'}|{'rest':var['rest'].tolist(),'afterFraction':float(fraction),'afterWeights':weights})
all_results=[]
directions=[Vector(d).normalized() for d in [(1,.371,.219),(.173,1,.293),(.271,.123,1)]]
def inside(tree,point):
    votes=0
    for ray in directions:
        origin=point+ray*1e-6;count=0
        for _ in range(64):
            hit=tree.ray_cast(origin,ray,10)
            if hit[0] is None:break
            count+=1;origin=hit[0]+ray*5e-6
        votes+=count%2
    return votes>=2
for poseid,model in models.items():
    for name,mesh in meshes.items():
        points=skin(mesh['rest'],mesh['weights'],model['matrices']);edges=mesh['edges'];mob=mesh['mobility']
        rest_length=np.linalg.norm(mesh['rest'][edges[:,0]]-mesh['rest'][edges[:,1]],axis=1)
        distance=np.linalg.norm(points[edges[:,0]]-points[edges[:,1]],axis=1);ratios=np.maximum(0,distance-mob[edges[:,0]]-mob[edges[:,1]])/rest_length
        pins=[]
        for i in np.flatnonzero(mob==0):
            p=Vector(points[i])
            for lining,tree in model['trees']:
                loc,n,face,depth=tree.find_nearest(p)
                if inside(tree,p):pins.append({'vertex':int(i),'lining':lining,'depthMeters':depth})
        all_results.append({'poseId':poseid,'surface':name,'maximumMinimumRequiredStretchRatio':float(np.max(ratios)),
            'edgesNecessarilyAbove1_35':int(np.sum(ratios>1.35)),'insidePins':pins,'maxInsideDepthMeters':max([0.]+[p['depthMeters'] for p in pins])})
report={'status':'LOCAL_WEIGHT_INTERVAL_PROJECTION_MEASURED_NOT_PHYSICS_PASS','sourceSha256':topology['sourceSha256'],
    'scriptSha256':hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),'variables':records,'constraintCount':len(constraints),'history':history,
    'unprojectableConstraints':[{'poseId':constraints[i]['poseId'],'mesh':constraints[i]['mesh'],'vertices':constraints[i]['vertices']} for i in unprojectable],
    'results':all_results,'maximumArmFractionChange':float(np.max(np.abs(current-initial))),
    'maxMinimumRequiredStretchRatio':max(r['maximumMinimumRequiredStretchRatio'] for r in all_results),
    'infeasibleEdgeCases':sum(r['edgesNecessarilyAbove1_35'] for r in all_results),
    'actualInsidePinCases':sum(len(r['insidePins']) for r in all_results)}
(OUT/'intermediate-weight-solution.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print(json.dumps({k:v for k,v in report.items() if k not in ['variables','history','results']},indent=2),flush=True)
