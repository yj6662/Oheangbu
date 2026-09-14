"""Read-only outer seam and actual inner shoulder-surface connection audit."""
import bpy,json,hashlib,ast
from pathlib import Path
from collections import Counter
import numpy as np
from mathutils.bvhtree import BVHTree
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[3]
BASE=ROOT/'Art/PlayerV2/Inspect/ClothBlender/FoldedGusset'
OUT=ROOT/'Art/PlayerV2/Inspect/ClothBlender/ShoulderConnection';OUT.mkdir(parents=True,exist_ok=True)
SOURCE=BASE/'DosaV2_FoldedGusset.blend'
bpy.ops.wm.open_mainfile(filepath=str(SOURCE));rig=bpy.data.objects['DosaV2_Rig']
file=Path(__file__).with_name('fit_gusset_anchor_field.py')
nodes=[n for n in ast.parse(file.read_text()).body if isinstance(n,ast.FunctionDef) and n.name=='skin']
exec(compile(ast.Module(body=nodes,type_ignores=[]),str(file),'exec'))
models={}
names=['DosaV2_BodyCore','DosaV2_BodyLining','DosaV2_SleeveOuter_L','DosaV2_SleeveOuter_R','DosaV2_ArmLining_Left','DosaV2_ArmLining_Right']
for name in names:
    o=bpy.data.objects[name];o.data.calc_loop_triangles()
    t=np.array([list(t.vertices) for t in o.data.loop_triangles]);counts=Counter(tuple(sorted((int(a),int(b)))) for tri in t for a,b in zip(tri,np.roll(tri,-1)))
    edges=np.array([e for e,count in counts.items() if count==1],dtype=int)
    models[name]={'rest':np.array([v.co[:] for v in o.data.vertices]),'triangles':t,'boundaryEdges':edges,
      'weights':[{o.vertex_groups[g.group].name:g.weight for g in v.groups} for v in o.data.vertices]}
body=models['DosaV2_BodyCore'];bp=body['rest'];be=body['boundaryEdges']
body_edge_points=bp[be];ab=body_edge_points[:,1]-body_edge_points[:,0];l2=np.sum(ab*ab,axis=1)
seams={};unmatched={}
for name in names[2:4]:
    m=models[name];ids=np.unique(m['boundaryEdges']);p=m['rest'][ids]
    ids=ids[(np.abs(p[:,0])<.34)&(p[:,2]>1.03)&(p[:,2]<1.48)]
    pairs=[];bad=[]
    for i in ids:
        point=m['rest'][i];u=np.clip(np.sum((point-body_edge_points[:,0])*ab,axis=1)/np.maximum(l2,1e-30),0,1)
        q=body_edge_points[:,0]+u[:,None]*ab;distance=np.linalg.norm(q-point,axis=1);j=int(np.argmin(distance))
        row={'sleeveVertex':int(i),'bodyEdge':be[j].tolist(),'edgeParameter':float(u[j]),'restDistanceMeters':float(distance[j]),'restPoint':point.tolist()}
        (pairs if distance[j]<.002 else bad).append(row)
    seams[name]=pairs;unmatched[name]=bad
rest_inverse={b.name:np.linalg.inv(np.array(b.matrix_local)) for b in rig.data.bones}
rows=[];worst=[]
for path in sorted((BASE/'IntermediatePoseData').glob('*.json')):
    pose=json.loads(path.read_text());mat={n:np.array(m)@rest_inverse[n] for n,m in pose['bonePoseMatrices'].items()}
    points={n:skin(m['rest'],m['weights'],mat) for n,m in models.items()};record={'poseId':pose['poseId'],'outerSeams':{},'innerShoulders':{}}
    for name,pairs in seams.items():
        gaps=[]
        for pair in pairs:
            a,b=pair['bodyEdge'];u=pair['edgeParameter'];point=points[name][pair['sleeveVertex']];q=points['DosaV2_BodyCore'][a]*(1-u)+points['DosaV2_BodyCore'][b]*u
            gaps.append({'distanceMeters':float(np.linalg.norm(point-q)),'sleevePoint':point.tolist(),'bodyPoint':q.tolist(),**pair})
        record['outerSeams'][name]={'pairs':len(gaps),'maxDistanceMeters':max([0]+[g['distanceMeters'] for g in gaps]),'worst':sorted(gaps,key=lambda g:g['distanceMeters'],reverse=True)[:4]}
    tree=BVHTree.FromPolygons(points['DosaV2_BodyLining'].tolist(),models['DosaV2_BodyLining']['triangles'].tolist(),all_triangles=True)
    for name in names[4:]:
        distances=[]
        for i in range(24):
            p=Vector(points[name][i]);loc,n,face,dist=tree.find_nearest(p);distances.append({'vertex':i,'distanceMeters':dist,'armPoint':list(p),'torsoPoint':list(loc),'bodyFace':face})
        record['innerShoulders'][name]={'capVertices':24,'minimumSurfaceDistanceMeters':min(d['distanceMeters'] for d in distances),
          'maximumSurfaceDistanceMeters':max(d['distanceMeters'] for d in distances),'nearest':min(distances,key=lambda d:d['distanceMeters'])}
    rows.append(record)
    if len(rows)%100==0:print('AUDIT_POSES '+str(len(rows)),flush=True)
out={'status':'READ_ONLY_CONNECTION_MEASUREMENTS_NOT_PASS','source':str(SOURCE),'sourceSha256':hashlib.sha256(SOURCE.read_bytes()).hexdigest(),
  'poseCount':len(rows),'outerMethod':'Original rest-local sleeve boundary vertices within 2mm of an actual BodyCore boundary edge, fixed barycentric correspondence, recreated linear skin from same evaluated Blender pose matrices. Unmatched sleeve openings are reported separately; not assumed torn seams.',
  'innerMethod':'Actual first 24 proximal ArmLining cap vertices to actual skinned closed BodyLining triangles, not capsule distance. Positive distance alone is not a full surface-disjointness proof.',
  'matchedRestBoundaries':seams,'unmatchedRestBoundaries':unmatched,'rows':rows,
  'maximumMatchedOuterGapMeters':max(r['outerSeams'][n]['maxDistanceMeters'] for r in rows for n in seams)}
(OUT/'connection-audit.json').write_text(json.dumps(out,indent=2),encoding='utf-8')
dump={'sourceSha256':out['sourceSha256'],'models':{n:{k:(v.tolist() if isinstance(v,np.ndarray) else v) for k,v in m.items()} for n,m in models.items()},'boneRestMatrices':{b.name:[list(r) for r in b.matrix_local] for b in rig.data.bones}}
(OUT/'geometry-rest.json').write_text(json.dumps(dump),encoding='utf-8')
print(json.dumps({k:v for k,v in out.items() if k not in ['matchedRestBoundaries','unmatchedRestBoundaries','rows']},indent=2),flush=True)
