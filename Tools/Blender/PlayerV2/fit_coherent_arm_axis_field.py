"""Shared axial shoulder/upper-arm field, actual lining and garment checks."""
import bpy,ast,json,hashlib
from pathlib import Path
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree
ROOT=Path(__file__).resolve().parents[3];OUT=ROOT/'Art/PlayerV2/Inspect/ClothBlender/CoherentArmGusset';OUT.mkdir(parents=True,exist_ok=True)
DATA=ROOT/'Art/PlayerV2/Inspect/ClothBlender/PosedArmFit9f4cd412';SOURCE=ROOT/'Art/PlayerV2/Inspect/ClothBlender/ContinuousGusset/DosaV2_ContinuousGusset.blend'
bpy.ops.wm.open_mainfile(filepath=str(SOURCE))
topology=json.loads((DATA/'topology-rest.json').read_text());poses=[]
for directory in [DATA,ROOT/'Art/PlayerV2/Inspect/ClothBlender/FeasibleGusset/IntermediatePoseData']:
    for file in directory.glob('*.json'):
        p=json.loads(file.read_text())
        if 'poseId' in p:poses.append(p)
for filename,wanted in [('fit_gusset_anchor_field.py',['skin','inside','smooth']),('audit_triangle_crossings.py',['proper_crossings'])]:
    script=Path(__file__).with_name(filename);nodes=[n for n in ast.parse(script.read_text()).body if isinstance(n,ast.FunctionDef) and n.name in wanted]
    exec(compile(ast.Module(body=nodes,type_ignores=[]),str(script),'exec'))
directions=[Vector(d).normalized() for d in [(1,.371,.219),(.173,1,.293),(.271,.123,1)]]
meshes={}
for o in bpy.context.scene.objects:
    if o.type!='MESH' or ('Lining' not in o.name and o.name not in ['DosaV2_Robe_Combined','DosaV2_SleeveOuter_L','DosaV2_SleeveOuter_R']):continue
    o.data.calc_loop_triangles();mob=o.data.color_attributes.get('ClothMobility');triangles=np.array([list(t.vertices) for t in o.data.loop_triangles])
    edges=np.array(sorted({tuple(sorted((a,b))) for t in triangles.tolist() for a,b in zip(t,t[1:]+t[:1])}));rest=np.array([v.co[:] for v in o.data.vertices])
    meshes[o.name]={'rest':rest,'weights':[{o.vertex_groups[g.group].name:g.weight for g in v.groups} for v in o.data.vertices],
      'triangles':triangles,'mobility':np.array([v.color[0] for v in mob.data]) if mob else None,'edges':edges,'lengths':np.linalg.norm(rest[edges[:,0]]-rest[edges[:,1]],axis=1)}
models=[(pose['poseId'],{n:np.array(m)@np.linalg.inv(np.array(topology['boneRestMatrices'][n])) for n,m in pose['bonePoseMatrices'].items()}) for pose in poses]
trials=[]
for lo,hi in [(.13,.28),(.13,.32),(.15,.28),(.15,.32),(.17,.28),(.17,.32)]:
    altered={n:[w.copy() for w in m['weights']] for n,m in meshes.items()}
    for n,m in meshes.items():
        if 'ArmLining' not in n and 'SleeveOuter' not in n:continue
        side='Left' if ('Left' in n or n.endswith('_L')) else 'Right';arm=side+'Arm';shoulder=side+'Shoulder'
        for i,p in enumerate(m['rest']):
            x=abs(p[0]);z=p[2]
            if 'ArmLining' in n:
                if i>=72:continue
                axial=np.mean(np.abs(m['rest'][(i//24)*24:(i//24+1)*24,0]));q=float(smooth(lo,hi,axial));altered[n][i]={arm:q,shoulder:1-q}
            else:
                amount=float((1-smooth(.28,.335,x))*smooth(.98,1.08,z));q=float(smooth(lo,hi,x))
                if amount<1e-8:continue
                row={bone:w*(1-amount) for bone,w in m['weights'][i].items()};row[arm]=row.get(arm,0)+q*amount;row[shoulder]=row.get(shoulder,0)+(1-q)*amount
                row=dict(sorted(((bone,w) for bone,w in row.items() if w>1e-8),key=lambda a:a[1],reverse=True)[:4]);total=sum(row.values());altered[n][i]={bone:w/total for bone,w in row.items()}
    rows=[]
    for poseid,matrices in models:
        points={n:skin(m['rest'],altered[n],matrices) for n,m in meshes.items()}
        trees={n:BVHTree.FromPolygons(p.tolist(),meshes[n]['triangles'].tolist(),all_triangles=True) for n,p in points.items() if 'Lining' in n}
        crossings={}
        for n,tree in trees.items():
            if 'ArmLining' not in n:continue
            t=meshes[n]['triangles'];pairs=np.array([(a,b) for a,b in tree.overlap(tree) if a<b and not set(t[a]).intersection(t[b])],dtype=int)
            crossings[n]=int(np.sum(proper_crossings(points[n][t[pairs[:,0]]],points[n][t[pairs[:,1]]]))) if len(pairs) else 0
        for n,m in meshes.items():
            if m['mobility'] is None:continue
            e=m['edges'];mob=m['mobility'];p=points[n];distance=np.linalg.norm(p[e[:,0]]-p[e[:,1]],axis=1)
            ratio=np.maximum(0,distance-mob[e[:,0]]-mob[e[:,1]])/m['lengths'];hits=[]
            for i in np.flatnonzero(mob==0):
                point=Vector(p[i])
                for lining,tree in trees.items():
                    if inside(tree,point):
                        near,normal,face,depth=tree.find_nearest(point);hits.append({'vertex':int(i),'lining':lining,'depthMeters':depth,'rest':m['rest'][i].tolist(),'weights':altered[n][i]})
            worst=[]
            for i in np.argsort(ratio)[-4:][::-1]:worst.append({'vertices':e[i].tolist(),'ratio':float(ratio[i]),'restLength':float(m['lengths'][i]),'mobility':mob[e[i]].tolist()})
            rows.append({'poseId':poseid,'surface':n,'insidePins':hits,'maximumRequiredStretch':float(np.max(ratio)),'infeasibleEdges':int(np.sum(ratio>1.35)),
              'armLiningSelfCrossings':crossings,'worstEdges':worst})
    summary={'axisLo':lo,'axisHi':hi,'insidePinCases':sum(len(r['insidePins']) for r in rows),'maximumDepthMeters':max([0.]+[h['depthMeters'] for r in rows for h in r['insidePins']]),
      'maximumArmLiningSelfCrossings':max([0]+[v for r in rows for v in r['armLiningSelfCrossings'].values()]),
      'maximumRequiredStretch':max(r['maximumRequiredStretch'] for r in rows),'infeasibleEdgeCases':sum(r['infeasibleEdges'] for r in rows),'rows':rows}
    trials.append(summary);print(json.dumps({k:v for k,v in summary.items() if k!='rows'}),flush=True)
    (OUT/'shared-axis-candidates.json').write_text(json.dumps({'status':'STRUCTURAL_CANDIDATES_NOT_MODEL_EDITS','sourceSha256':hashlib.sha256(SOURCE.read_bytes()).hexdigest(),
      'scriptSha256':hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),'poseCount':len(poses),'trials':trials},indent=2),encoding='utf-8')
