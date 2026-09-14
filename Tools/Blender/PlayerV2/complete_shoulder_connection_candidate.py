"""Separate actual shoulder-volume connection with coherent local fabric darts."""
import bpy,bmesh,json,ast,hashlib,importlib.util,math
from pathlib import Path
import numpy as np
from mathutils import Matrix,Vector,Euler
from mathutils.bvhtree import BVHTree
ROOT=Path(__file__).resolve().parents[3];BASE=ROOT/'Art/PlayerV2/Inspect/ClothBlender/FoldedGusset';DATA=ROOT/'Art/PlayerV2/Inspect/ClothBlender/GlobalEnvelope';OUT=ROOT/'Art/PlayerV2/Inspect/ClothBlender/ShoulderComplete';OUT.mkdir(parents=True,exist_ok=True)
SOURCE=DATA/'DosaV2_GlobalClothEnvelope.blend';APPEND=ROOT/'Art/PlayerV2/Inspect/ClothBlender/ShoulderConnection/DosaV2_ShoulderConnection_Candidate.blend'
bpy.ops.wm.open_mainfile(filepath=str(SOURCE));rig=bpy.data.objects['DosaV2_Rig'];newnames=['DosaV2_ShoulderLining_Left','DosaV2_ShoulderLining_Right']
with bpy.data.libraries.load(str(APPEND),link=False) as (src,dst):dst.objects=list(newnames)
for obj in dst.objects:
    bpy.context.scene.collection.objects.link(obj);obj.parent=rig
    for modifier in obj.modifiers:
        if modifier.type=='ARMATURE':modifier.object=rig
for o in bpy.context.scene.objects:
    if o.name.startswith('CTRL_') and 'AuthoringMode' in o:o['AuthoringMode']=False;o.update_tag()
for bone in rig.pose.bones:bone.matrix_basis=Matrix.Identity(4)
wire_cleanup=[]
for name in ['DosaV2_Robe_Combined','DosaV2_SleeveOuter_L','DosaV2_SleeveOuter_R']:
    obj=bpy.data.objects[name];me=obj.data
    signature=lambda:{'points':[v.co[:] for v in me.vertices],'faces':[list(p.vertices) for p in me.polygons],
      'weights':[[[g.group,g.weight] for g in v.groups] for v in me.vertices],
      'uvs':[[v.uv[:] for v in layer.data] for layer in me.uv_layers],'colors':{a.name:[v.color[:] for v in a.data] for a in me.color_attributes}}
    before=signature();bm=bmesh.new();bm.from_mesh(me);bm.verts.ensure_lookup_table();original_vertices=list(bm.verts);wire=[e for e in bm.edges if not e.link_faces]
    ids=[list(sorted(v.index for v in e.verts)) for e in wire]
    if wire:bmesh.ops.delete(bm,geom=wire,context='EDGES_FACES')
    bm.verts.ensure_lookup_table();assert len(bm.verts)==len(original_vertices) and all(a is b for a,b in zip(bm.verts,original_vertices))
    bm.to_mesh(me);bm.free();me.update();assert signature()==before
    wire_cleanup.append({'mesh':name,'removedLooseEdges':ids,'count':len(ids),'allPointsFacesUVsWeightsColorChannelsExact':True})
functions=[('fit_gusset_anchor_field.py',['skin','inside']),('audit_triangle_crossings.py',['proper_crossings'])]
for f,wanted in functions:
    path=Path(__file__).with_name(f);nodes=[n for n in ast.parse(path.read_text()).body if isinstance(n,ast.FunctionDef) and n.name in wanted];exec(compile(ast.Module(body=nodes,type_ignores=[]),str(path),'exec'))
directions=[Vector(d).normalized() for d in [(1,.371,.219),(.173,1,.293),(.271,.123,1)]]
before_points={o.name:np.array([v.co[:] for v in o.data.vertices]) for o in bpy.context.scene.objects if o.type=='MESH'}
origin=before_points['DosaV2_SleeveOuter_L'];kernels=[(340,[0.,-.006,0.]),(28,[0.,.001,0.]),(343,[.002,0.,-.003])]
changes={}
for name in ['DosaV2_SleeveOuter_L','DosaV2_BodyCore']:
    obj=bpy.data.objects[name];before=before_points[name];after=before.copy()
    for vertex,shift in kernels:
        d=np.linalg.norm(before-origin[vertex],axis=1);field=np.maximum(0,1-(d/.03)**2)**2;after+=field[:,None]*np.array(shift)
    # The added shoulder volume requires this small true dart at the existing
    # free fold interior. Preserve both sewn endpoints and their exact pins.
    if name=='DosaV2_SleeveOuter_L':after[895]+=np.array([0.,-.004,0.])
    for vertex,p in zip(obj.data.vertices,after):vertex.co=p
    obj.data.update();delta=np.linalg.norm(after-before,axis=1)
    changes[name]=[{'vertex':int(i),'before':before[i].tolist(),'after':after[i].tolist()} for i in np.flatnonzero(delta>1e-8)]
models={};clothnames=['DosaV2_Robe_Combined','DosaV2_SleeveOuter_L','DosaV2_SleeveOuter_R']
for obj in bpy.context.scene.objects:
    if obj.type!='MESH' or not ('Lining' in obj.name or obj.name in clothnames):continue
    obj.data.calc_loop_triangles();mob=obj.data.color_attributes.get('ClothMobility')
    models[obj.name]={'rest':np.array([v.co[:] for v in obj.data.vertices]),'triangles':np.array([list(t.vertices) for t in obj.data.loop_triangles]),
      'weights':[{obj.vertex_groups[g.group].name:g.weight for g in vertex.groups} for vertex in obj.data.vertices],
      'mobility':np.array([p.color[0] for p in mob.data]) if mob else None,'edges':np.array(sorted({tuple(sorted((a,b))) for t in obj.data.loop_triangles for a,b in zip(list(t.vertices),np.roll(t.vertices,-1))}))}
rinv={b.name:np.linalg.inv(np.array(b.matrix_local)) for b in rig.data.bones};poses=[]
for d in json.loads((BASE/'Inputs/static-pose-definitions.json').read_text())['poses']:poses.append((d['id'],{n:np.array(m)@rinv[n] for n,m in d['boneMatricesRigLocal'].items()}))
for f,label in [('fixture_grip_settle_00','fixture_rest_settle'),('fixture_grip_settle_100','fixture_grip_settle'),('fixture_raised_arms_settle_100','fixture_raised_arms_settle')]:
    p=json.loads((BASE/'IntermediatePoseData'/(f+'.json')).read_text());poses.append((label,{n:np.array(m)@rinv[n] for n,m in p['bonePoseMatrices'].items()}))
for file in sorted((BASE/'IntermediatePoseData').glob('*.json')):
    p=json.loads(file.read_text());poses.append((p['poseId'],{n:np.array(m)@rinv[n] for n,m in p['bonePoseMatrices'].items()}))
rows=[];allposepoints={n:[] for n in clothnames}
for poseid,mat in poses:
    anatomy={}
    for n,m in models.items():
        if 'Lining' not in n:continue
        p=skin(m['rest'],m['weights'],mat);t=m['triangles'];anatomy[n]={'tree':BVHTree.FromPolygons(p.tolist(),t.tolist(),all_triangles=True),'lo':np.min(p,axis=0),'hi':np.max(p,axis=0),'points':p}
    hits=[];edge_max=0.
    for n in clothnames:
        m=models[n];p=skin(m['rest'],m['weights'],mat);allposepoints[n].append(p);e=m['edges'];mob=m['mobility'];ratios=np.maximum(0,np.linalg.norm(p[e[:,0]]-p[e[:,1]],axis=1)-mob[e[:,0]]-mob[e[:,1]])/np.linalg.norm(m['rest'][e[:,0]]-m['rest'][e[:,1]],axis=1);edge_max=max(edge_max,float(np.max(ratios)))
        ids=np.flatnonzero(mob==0)
        for lining,a in anatomy.items():
            candidates=ids[np.all(p[ids]>=a['lo'],axis=1)&np.all(p[ids]<=a['hi'],axis=1)]
            for i in candidates:
                point=Vector(p[i])
                if inside(a['tree'],point):hits.append({'cloth':n,'vertex':int(i),'lining':lining,'depthMeters':a['tree'].find_nearest(point)[3]})
    bridges=[]
    for side in ['Left','Right']:
        sign=1 if side=='Left' else -1;name='DosaV2_ShoulderLining_'+side;head=np.array(rig.data.bones[side+'Arm'].head_local);prox=np.array([sign*.111,0.,1.351]);delta=head-prox
        for kind,restpoint,weights,target in [('joint',head,{side+'Shoulder':1.},'DosaV2_ArmLining_'+side),('torso',prox+delta*.2,{'Spine02':.8,side+'Shoulder':.2},'DosaV2_BodyLining')]:
            q=Vector(skin(np.array([restpoint]),[weights],mat)[0]);branches={n:{'inside':inside(anatomy[n]['tree'],q),'surfaceDistanceMeters':anatomy[n]['tree'].find_nearest(q)[3]} for n in [name,target]};bridges.append({'side':side,'kind':kind,'commonInteriorPoint':list(q),'branches':branches})
    crossings={}
    for n in newnames:
        p=anatomy[n]['points'];t=models[n]['triangles'];tree=anatomy[n]['tree']
        pairs=np.array([(a,b) for a,b in tree.overlap(tree) if a<b and not set(t[a]).intersection(t[b])],dtype=int)
        crossings[n]=int(np.sum(proper_crossings(p[t[pairs[:,0]]],p[t[pairs[:,1]]]))) if len(pairs) else 0
    rows.append({'poseId':poseid,'pinIntersections':hits,'maximumNecessaryEdgeStretch':edge_max,'connectionInteriorWitnesses':bridges,'newShoulderProperSelfCrossings':crossings})
    if len(rows)%100==0:print('SHOULDER_COMPLETE '+str(len(rows)),flush=True)
DEST=OUT/'DosaV2_ShoulderComplete_Candidate.blend';bpy.ops.wm.save_as_mainfile(filepath=str(DEST))
for n in clothnames:
    m=models[n];np.savez_compressed(OUT/(n+'.npz'),rest=m['rest'],edges=m['edges'],mobility=m['mobility'],posePoints=np.array(allposepoints[n]),poseNames=np.array([p for p,mat in poses]))
report={'status':'ACTUAL_SHOULDER_CONNECTION_CANDIDATE_NOT_NATIVE_PASS','sourceSha256':hashlib.sha256(SOURCE.read_bytes()).hexdigest(),'outputSha256':hashlib.sha256(DEST.read_bytes()).hexdigest(),'sourceChanges':changes,'nonRenderedWireCleanup':wire_cleanup,
 'poseCount':len(poses),'pinIntersectionCases':sum(len(r['pinIntersections']) for r in rows),'maximumPinIntersectionDepthMeters':max([0]+[h['depthMeters'] for r in rows for h in r['pinIntersections']]),
 'failedCommonInteriorWitnesses':sum(not all(a['inside'] for a in v['branches'].values()) for r in rows for v in r['connectionInteriorWitnesses']),
 'maximumNecessaryEdgeStretch':max(r['maximumNecessaryEdgeStretch'] for r in rows),'rows':rows}
(OUT/'candidate-report.json').write_text(json.dumps(report,indent=2),encoding='utf-8');print(json.dumps({k:v for k,v in report.items() if k not in ['sourceChanges','rows']},indent=2),flush=True)
