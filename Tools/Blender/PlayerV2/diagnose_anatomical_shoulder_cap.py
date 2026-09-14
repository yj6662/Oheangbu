"""Structural arm-lining candidate evaluation; no file/model mutation."""
import bpy,ast,json,hashlib
from pathlib import Path
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree
ROOT=Path(__file__).resolve().parents[3]
OUT=ROOT/'Art/PlayerV2/Inspect/ClothBlender/CoherentArmGusset';OUT.mkdir(parents=True,exist_ok=True)
DATA=ROOT/'Art/PlayerV2/Inspect/ClothBlender/PosedArmFit9f4cd412'
SOURCE=ROOT/'Art/PlayerV2/Inspect/ClothBlender/ContinuousGusset/DosaV2_ContinuousGusset.blend'
bpy.ops.wm.open_mainfile(filepath=str(SOURCE))
topology=json.loads((DATA/'topology-rest.json').read_text());poses=[]
for directory in [DATA,ROOT/'Art/PlayerV2/Inspect/ClothBlender/FeasibleGusset/IntermediatePoseData']:
    for file in directory.glob('*.json'):
        p=json.loads(file.read_text())
        if 'poseId' in p:poses.append(p)
helper=Path(__file__).with_name('fit_gusset_anchor_field.py')
nodes=[n for n in ast.parse(helper.read_text()).body if isinstance(n,ast.FunctionDef) and n.name in ['skin','inside']]
exec(compile(ast.Module(body=nodes,type_ignores=[]),str(helper),'exec'))
crossscript=Path(__file__).with_name('audit_triangle_crossings.py')
nodes=[n for n in ast.parse(crossscript.read_text()).body if isinstance(n,ast.FunctionDef) and n.name=='proper_crossings']
exec(compile(ast.Module(body=nodes,type_ignores=[]),str(crossscript),'exec'))
directions=[Vector(d).normalized() for d in [(1,.371,.219),(.173,1,.293),(.271,.123,1)]]
meshes={}
for o in bpy.context.scene.objects:
    if o.type!='MESH' or ('Lining' not in o.name and o.name not in ['DosaV2_Robe_Combined','DosaV2_SleeveOuter_L','DosaV2_SleeveOuter_R']):continue
    o.data.calc_loop_triangles();mob=o.data.color_attributes.get('ClothMobility')
    meshes[o.name]={'rest':np.array([v.co[:] for v in o.data.vertices]),'weights':[{o.vertex_groups[g.group].name:g.weight for g in v.groups} for v in o.data.vertices],
      'triangles':np.array([list(t.vertices) for t in o.data.loop_triangles]),'mobility':np.array([v.color[0] for v in mob.data]) if mob else None}
for name,m in meshes.items():
    if 'ArmLining' not in name:continue
    arm=('Left' if 'Left' in name else 'Right')+'Arm'
    forearm=('Left' if 'Left' in name else 'Right')+'ForeArm'
    head=np.array(topology['boneRestMatrices'][arm])[:3,3];end=np.array(topology['boneRestMatrices'][forearm])[:3,3]
    axis=(end-head)/np.linalg.norm(end-head)
    radius=max(np.linalg.norm((p-m['rest'][24:48].mean(axis=0))-axis*np.dot(p-m['rest'][24:48].mean(axis=0),axis)) for p in m['rest'][24:48])
    for ring in [0,1]:
        indices=range(ring*24,(ring+1)*24);center=m['rest'][list(indices)].mean(axis=0)
        old=m['rest'][list(indices)].copy();axial=-radius/np.sqrt(2) if ring==0 else 0.;radial=radius/np.sqrt(2) if ring==0 else radius
        for i,p in zip(indices,old):
            direction=p-center;direction-=axis*np.dot(direction,axis);direction/=np.linalg.norm(direction)
            m['rest'][i]=head+axis*axial+direction*radial
    for i,p in enumerate(m['rest']):
        if abs(p[0])<.335:m['weights'][i]={arm:1.}
rows=[]
for pose in poses:
    matrices={n:np.array(m)@np.linalg.inv(np.array(topology['boneRestMatrices'][n])) for n,m in pose['bonePoseMatrices'].items()}
    points={n:skin(m['rest'],m['weights'],matrices) for n,m in meshes.items()}
    trees={n:BVHTree.FromPolygons(p.tolist(),meshes[n]['triangles'].tolist(),all_triangles=True) for n,p in points.items() if 'Lining' in n}
    crossings={}
    for n,tree in trees.items():
        if 'ArmLining' not in n:continue
        t=meshes[n]['triangles'];pairs=np.array([(a,b) for a,b in tree.overlap(tree) if a<b and not set(t[a]).intersection(t[b])],dtype=int)
        crossings[n]=int(np.sum(proper_crossings(points[n][t[pairs[:,0]]],points[n][t[pairs[:,1]]]))) if len(pairs) else 0
    for n,m in meshes.items():
        if m['mobility'] is None:continue
        hits=[]
        for i in np.flatnonzero(m['mobility']==0):
            p=Vector(points[n][i])
            for lining,tree in trees.items():
                if inside(tree,p):
                    near,normal,face,depth=tree.find_nearest(p);hits.append({'vertex':int(i),'lining':lining,'depthMeters':depth,'rest':m['rest'][i].tolist(),'weights':m['weights'][i]})
        rows.append({'poseId':pose['poseId'],'surface':n,'insidePins':hits,'armLiningSelfCrossings':crossings})
summary={'status':'STRUCTURAL_CANDIDATE_READ_ONLY_NOT_PASS','sourceSha256':hashlib.sha256(SOURCE.read_bytes()).hexdigest(),
  'candidate':'First two ArmLining rings form a rounded proximal shoulder segment around actual UpperArm pivot, using source second-ring radius; proximal own Arm100. Topology, distal rest geometry/weights and garment continuous69 source unchanged.',
  'poseCount':len(poses),'insidePinCases':sum(len(r['insidePins']) for r in rows),'maximumDepthMeters':max([0.]+[h['depthMeters'] for r in rows for h in r['insidePins']]),
  'maximumArmLiningSelfCrossings':max([0]+[v for r in rows for v in r['armLiningSelfCrossings'].values()]),'rows':rows}
(OUT/'anatomical-shoulder-cap.json').write_text(json.dumps(summary,indent=2),encoding='utf-8')
print(json.dumps({k:v for k,v in summary.items() if k!='rows'},indent=2),flush=True)
