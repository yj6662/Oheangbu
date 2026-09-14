"""Near-only closed arm-lining angular LOD; world source and hands untouched."""
import bpy,bmesh,json,ast,math,hashlib
import numpy as np
from pathlib import Path
from mathutils import Matrix,Vector
from mathutils.bvhtree import BVHTree
ROOT=Path(__file__).resolve().parents[3]
BASE=ROOT/'Art/PlayerV2/Inspect/ClothBlender/FoldedGusset'
SOURCE=ROOT/'Art/PlayerV2/Inspect/ClothBlender/ShoulderComplete/DosaV2_ShoulderComplete_Candidate.blend'
OUT=SOURCE.parent/'NearOverrides';OUT.mkdir(parents=True,exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(SOURCE));rig=bpy.data.objects['DosaV2_Rig']
for name,wanted in [('fit_gusset_anchor_field.py',['skin','inside']),('audit_triangle_crossings.py',['proper_crossings'])]:
    file=Path(__file__).with_name(name);nodes=[n for n in ast.parse(file.read_text()).body if isinstance(n,ast.FunctionDef) and n.name in wanted];exec(compile(ast.Module(body=nodes,type_ignores=[]),str(file),'exec'))
directions=[Vector(d).normalized() for d in [(1,.371,.219),(.173,1,.293),(.271,.123,1)]]
for o in bpy.context.scene.objects:
    if o.name.startswith('CTRL_') and 'AuthoringMode' in o:o['AuthoringMode']=False;o.update_tag()
for bone in rig.pose.bones:bone.matrix_basis=Matrix.Identity(4)
def model(o):
    o.data.calc_loop_triangles()
    return {'rest':np.array([v.co[:] for v in o.data.vertices]),'triangles':np.array([list(t.vertices) for t in o.data.loop_triangles]),
      'weights':[{o.vertex_groups[g.group].name:g.weight for g in v.groups} for v in o.data.vertices]}
source_models={};near_models={};changes=[]
rings=[list(range(j,j+24)) for j in [0,24,48]]+[list(range(168,216,2)),list(range(169,216,2))]+[list(range(j,j+24)) for j in [72,96,120,144]]
assert sorted(i for ring in rings for i in ring)==list(range(216))
for side in ['Left','Right']:
    name='DosaV2_ArmLining_'+side;obj=bpy.data.objects[name];old=obj.data;original=model(obj);source_models[name]=original
    assert len(old.vertices)==216 and len(old.loop_triangles)==428
    source_uv=[np.array([old.uv_layers.active.data[l].uv[:] for l in t.loops]) for t in old.loop_triangles] if old.uv_layers.active else None
    tree=BVHTree.FromPolygons(original['rest'].tolist(),original['triangles'].tolist(),all_triangles=True)
    points=[];weights=[];samples=[];segments=18
    for ring in rings:
        for j in range(segments):
            value=j*24/segments;a=int(math.floor(value));t=value-a;i,k=ring[a],ring[(a+1)%24]
            points.append((original['rest'][i]*(1-t)+original['rest'][k]*t).tolist())
            w={n:original['weights'][i].get(n,0)*(1-t)+original['weights'][k].get(n,0)*t for n in set(original['weights'][i])|set(original['weights'][k])}
            # Retain all real influences here; the existing source has at most4.
            w={n:v for n,v in w.items() if v>1e-9};total=sum(w.values());weights.append({n:v/total for n,v in w.items()})
            samples.append({'sourceVertices':[i,k],'fraction':t})
    faces=[]
    for j in range(len(rings)-1):
        for i in range(segments):
            a=j*segments+i;b=j*segments+(i+1)%segments;c=(j+1)*segments+(i+1)%segments;d=(j+1)*segments+i;faces.extend([(a,b,c),(a,c,d)])
    faces.append(tuple(reversed(range(segments))));faces.append(tuple((len(rings)-1)*segments+i for i in range(segments)))
    me=bpy.data.meshes.new(name+'_Near18');me.from_pydata(points,[],faces);me.update()
    bm=bmesh.new();bm.from_mesh(me);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(me);bm.free()
    for material in old.materials:me.materials.append(material)
    uv=me.uv_layers.new(name=old.uv_layers.active.name) if source_uv is not None else None
    for face in me.polygons:
        face.use_smooth=True;center=face.center
        if uv is None:continue
        for loopid in face.loop_indices:
            p=me.vertices[me.loops[loopid].vertex_index].co;query=p.lerp(center,.0001)
            q,normal,faceid,d=tree.find_nearest(query);tri=original['rest'][original['triangles'][faceid]]
            basis=np.column_stack((tri[1]-tri[0],tri[2]-tri[0]));ab=np.linalg.lstsq(basis,np.array(p)-tri[0],rcond=None)[0]
            uv.data[loopid].uv=tuple(np.array([1-ab.sum(),ab[0],ab[1]])@source_uv[faceid])
    # Keep every original group definition and its index. The parent exporter
    # may replace only mesh data on an existing near object; weights must still
    # resolve to the same source bones after that operation.
    original_group_names=[group.name for group in obj.vertex_groups]
    obj.data=me
    obj.vertex_groups.clear()
    for name_in_source in original_group_names:obj.vertex_groups.new(name=name_in_source)
    for row in weights:
        for n in row:
            if obj.vertex_groups.get(n) is None:obj.vertex_groups.new(name=n)
    for i,row in enumerate(weights):
        for n,w in row.items():obj.vertex_groups[n].add([i],w,'REPLACE')
    assert [group.name for group in obj.vertex_groups]==original_group_names
    obj['NearOnlyAngularSamples']=18;obj['SourceWorldAngularSamples']=24
    me.calc_loop_triangles();bm=bmesh.new();bm.from_mesh(me)
    assert len(me.vertices)==162 and len(me.loop_triangles)==320
    assert all(e.is_manifold for e in bm.edges);bm.free()
    near_models[name]=model(obj)
    changes.append({'mesh':name,'beforeVertices':216,'afterVertices':162,'beforeTriangles':428,'afterTriangles':320,
      'closedManifold':True,'maxInfluences':max(map(len,weights)),'maxWeightSumError':max(abs(sum(w.values())-1) for w in near_models[name]['weights']),
      'vertexGroupNamesAndIndicesPreserved':original_group_names,'sourceRingIndices':rings,'angularInterpolation':samples,'uvTransfer':'Source has no UV map; procedural lining material remains exact.' if source_uv is None else 'Nearest source triangle corner with barycentric original-UV transfer; side query is biased 0.01% toward current polygon center.'})
DEST=OUT/'DosaV2_NearArmLining18.blend';bpy.ops.wm.save_as_mainfile(filepath=str(DEST))
rinv={b.name:np.linalg.inv(np.array(b.matrix_local)) for b in rig.data.bones};poses=[]
for d in json.loads((BASE/'Inputs/static-pose-definitions.json').read_text())['poses']:poses.append((d['id'],{n:np.array(m)@rinv[n] for n,m in d['boneMatricesRigLocal'].items()}))
for f,label in [('fixture_grip_settle_00','fixture_rest_settle'),('fixture_grip_settle_100','fixture_grip_settle'),('fixture_raised_arms_settle_100','fixture_raised_arms_settle')]:
    p=json.loads((BASE/'IntermediatePoseData'/(f+'.json')).read_text());poses.append((label,{n:np.array(m)@rinv[n] for n,m in p['bonePoseMatrices'].items()}))
for file in sorted((BASE/'IntermediatePoseData').glob('*.json')):
    p=json.loads(file.read_text());poses.append((p['poseId'],{n:np.array(m)@rinv[n] for n,m in p['bonePoseMatrices'].items()}))
cloth={}
for name in ['DosaV2_Robe_Combined','DosaV2_SleeveOuter_L','DosaV2_SleeveOuter_R']:
    o=bpy.data.objects[name];ids=np.flatnonzero(np.array([v.color[0] for v in o.data.color_attributes['ClothMobility'].data])==0);m=model(o)
    cloth[name]={'ids':ids,'rest':m['rest'][ids],'weights':[m['weights'][i] for i in ids]}
# Source vertices, every edge midpoint and triangle centroid are all checked.
# This is sampled bidirectional surface error, not a formal Hausdorff certificate.
def samples(p,tri):
    e=np.array(sorted({tuple(sorted((int(a),int(b)))) for t in tri for a,b in zip(t,np.roll(t,-1))}))
    return np.concatenate((p,(p[e[:,0]]+p[e[:,1]])*.5,p[tri].mean(axis=1)))
rows=[]
for poseid,mat in poses:
    row={'poseId':poseid,'parts':{}}
    for name,near in near_models.items():
        source=source_models[name];p=skin(near['rest'],near['weights'],mat);q=skin(source['rest'],source['weights'],mat);t=near['triangles'];u=source['triangles']
        ntree=BVHTree.FromPolygons(p.tolist(),t.tolist(),all_triangles=True);otree=BVHTree.FromPolygons(q.tolist(),u.tolist(),all_triangles=True)
        source_to_near=max(ntree.find_nearest(Vector(v))[3] for v in samples(q,u));near_to_source=max(otree.find_nearest(Vector(v))[3] for v in samples(p,t))
        pairs=np.array([(a,b) for a,b in ntree.overlap(ntree) if a<b and not set(t[a]).intersection(t[b])],dtype=int)
        crosses=int(np.sum(proper_crossings(p[t[pairs[:,0]]],p[t[pairs[:,1]]]))) if len(pairs) else 0
        lo=np.min(p,axis=0);hi=np.max(p,axis=0);hits=[]
        for clothname,c in cloth.items():
            cp=skin(c['rest'],c['weights'],mat);ids=np.flatnonzero(np.all(cp>=lo,axis=1)&np.all(cp<=hi,axis=1))
            for j in ids:
                point=Vector(cp[j])
                if inside(ntree,point):hits.append({'cloth':clothname,'vertex':int(c['ids'][j]),'depthMeters':ntree.find_nearest(point)[3]})
        row['parts'][name]={'sourceToNearSampledMaxMeters':source_to_near,'nearToSourceSampledMaxMeters':near_to_source,'properSelfCrossings':crosses,'pinsInside':hits}
    rows.append(row)
    if len(rows)%100==0:print('NEAR_LOD '+str(len(rows)),flush=True)
report={'status':'NEAR_ONLY_CLOSED_ARM_LINING_LOD_NOT_RIG_PASS','sourceSha256':hashlib.sha256(SOURCE.read_bytes()).hexdigest(),'outputSha256':hashlib.sha256(DEST.read_bytes()).hexdigest(),
 'recipeSha256':hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),'changedMeshes':changes,'worldSourceUntouched':True,'unchangedSkeletonBoneCount':len(rig.data.bones),'productionActions':len(bpy.data.actions),
 'netNearTrianglesIncludingTwo96TriangleShoulders':25990-216+192,'addedWorldShoulderTriangles':192,
 'poseCount':len(rows),'maximumSampledSurfaceErrorMeters':max(max(p['sourceToNearSampledMaxMeters'],p['nearToSourceSampledMaxMeters']) for r in rows for p in r['parts'].values()),
 'maximumProperSelfCrossings':max(p['properSelfCrossings'] for r in rows for p in r['parts'].values()),'pinIntersectionCases':sum(len(p['pinsInside']) for r in rows for p in r['parts'].values()),'rows':rows,
 'limitations':'World anatomy/collision geometry is unchanged. Only near rendering uses these18-angle closed surfaces. The finite vertex/edge/centroid sample is not a formal continuous Hausdorff bound. Native cloth and overall RIG_PASS are separate.'}
(OUT/'near-lod-report.json').write_text(json.dumps(report,indent=2),encoding='utf-8');print(json.dumps({k:v for k,v in report.items() if k not in ['changedMeshes','rows']},indent=2),flush=True)
