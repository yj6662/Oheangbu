"""Read-only dense shoulder geometry/path checks on the saved cloth derivative."""
import bpy,ast,json,hashlib
from pathlib import Path
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree
ROOT=Path(__file__).resolve().parents[3];OUT=ROOT/'Art/PlayerV2/Inspect/ClothBlender/FoldedGusset'
SOURCE=OUT/'DosaV2_FoldedGusset.blend';bpy.ops.wm.open_mainfile(filepath=str(SOURCE));rig=bpy.data.objects['DosaV2_Rig']
for filename,wanted in [('fit_gusset_anchor_field.py',['skin','inside']),('audit_triangle_crossings.py',['proper_crossings'])]:
    file=Path(__file__).with_name(filename);nodes=[n for n in ast.parse(file.read_text()).body if isinstance(n,ast.FunctionDef) and n.name in wanted];exec(compile(ast.Module(body=nodes,type_ignores=[]),str(file),'exec'))
directions=[Vector(d).normalized() for d in [(1,.371,.219),(.173,1,.293),(.271,.123,1)]]
rest_inverse={b.name:np.linalg.inv(np.array(b.matrix_local)) for b in rig.data.bones}
line_topology={};cloth={}
for o in bpy.context.scene.objects:
    if o.type!='MESH':continue
    if 'Lining' in o.name:
        o.data.calc_loop_triangles();line_topology[o.name]=np.array([list(t.vertices) for t in o.data.loop_triangles])
    mob=o.data.color_attributes.get('ClothMobility')
    if mob is None or o.name not in ['DosaV2_Robe_Combined','DosaV2_SleeveOuter_L','DosaV2_SleeveOuter_R']:continue
    cloth[o.name]={'rest':np.array([v.co[:] for v in o.data.vertices]),'mobility':np.array([v.color[0] for v in mob.data]),
      'weights':[{o.vertex_groups[g.group].name:g.weight for g in v.groups} for v in o.data.vertices],
      'edges':{tuple(sorted(e.vertices)) for e in o.data.edges}}
source_report=json.loads((OUT/'source-report.json').read_text());folds={r['mesh']:r['added'] for r in source_report['foldTopology'] if r['mesh'] in cloth}
for n,records in folds.items():
    for r in records:
        a,b=r['sourceVertices'];mid=r['newVertex'];assert tuple(sorted((a,b))) not in cloth[n]['edges']
        assert tuple(sorted((a,mid))) in cloth[n]['edges'] and tuple(sorted((b,mid))) in cloth[n]['edges']
rows=[];closest=[]
for file in sorted((OUT/'IntermediatePoseData').glob('*.json')):
    pose=json.loads(file.read_text());matrices={n:np.array(m)@rest_inverse[n] for n,m in pose['bonePoseMatrices'].items()}
    trees={n:BVHTree.FromPolygons(points,line_topology[n].tolist(),all_triangles=True) for n,points in pose['vertices'].items()}
    crossing={}
    for n,tree in trees.items():
        if 'ArmLining' not in n:continue
        points=np.array(pose['vertices'][n]);t=line_topology[n];pairs=np.array([(a,b) for a,b in tree.overlap(tree) if a<b and not set(t[a]).intersection(t[b])],dtype=int)
        count=int(np.sum(proper_crossings(points[t[pairs[:,0]]],points[t[pairs[:,1]]]))) if len(pairs) else 0;crossing[n]=count
    maximum_path_ratio=0.;minimum_clearance=float('inf');nearest_record=None
    for n,m in cloth.items():
        points=skin(m['rest'],m['weights'],matrices)
        for i in np.flatnonzero(m['mobility']==0):
            p=Vector(points[i])
            for lining,tree in trees.items():
                loc,normal,face,depth=tree.find_nearest(p)
                if depth<minimum_clearance:
                    minimum_clearance=depth;nearest_record={'surface':n,'vertex':int(i),'lining':lining,'distanceMeters':depth,'point':list(p)}
        for record in folds.get(n,[]):
            a,b=record['sourceVertices'];mid=record['newVertex'];length=np.linalg.norm(m['rest'][a]-m['rest'][mid])+np.linalg.norm(m['rest'][b]-m['rest'][mid])
            required=max(0,np.linalg.norm(points[a]-points[b])-m['mobility'][a]-m['mobility'][b]);maximum_path_ratio=max(maximum_path_ratio,required/length)
    rows.append({'poseId':pose['poseId'],'armLiningSelfCrossings':crossing,'maximumOriginalEndpointToGussetPathRequiredRatio':float(maximum_path_ratio),
      'minimumExactPinToAnatomyDistanceMeters':minimum_clearance,'nearestPin':nearest_record})
summary={'status':'DENSE_GEOMETRY_AND_PATH_CHECKS_NOT_NATIVE_PHYSICS_PASS','sourceSha256':hashlib.sha256(SOURCE.read_bytes()).hexdigest(),
  'poseCount':len(rows),'maximumArmLiningSelfCrossings':max(v for r in rows for v in r['armLiningSelfCrossings'].values()),
  'maximumOriginalEndpointToGussetPathRequiredRatio':max(r['maximumOriginalEndpointToGussetPathRequiredRatio'] for r in rows),
  'minimumExactPinToAnatomyDistanceMeters':min(r['minimumExactPinToAnatomyDistanceMeters'] for r in rows),
  'distanceMethod':'Actual saved anatomy vertices from Blender 404-sample evaluation. Cloth linear skin recreated from the same saved model and actual bone pose matrices; previous actual Blender samples establish no inside pins. Distance here is nearest triangle surface, not a capsule approximation.',
  'pathMethod':'Every removed original edge is verified absent; its two new ridge edges exist. Original endpoint span minus unchanged endpoint mobility is compared to the full physical folded rest path, independent of new midpoint maxDistance.',
  'rows':rows}
(OUT/'dense-geometry-path-audit.json').write_text(json.dumps(summary,indent=2),encoding='utf-8');print(json.dumps({k:v for k,v in summary.items() if k!='rows'},indent=2),flush=True)
