"""Classify every source sleeve vertex against current model-cut boundaries.

Read-only geometric analysis. A shared cut does not establish an actual stitch.
The legacy role labels are the rejected initial hypothesis; see ART_ROLE_CORRECTION.
"""
import bpy,bmesh,json,math,heapq,hashlib
import numpy as np
from pathlib import Path
from mathutils import Matrix
from mathutils.kdtree import KDTree
ROOT=Path(__file__).resolve().parents[3];ART=ROOT/'Art/PlayerV2/Inspect/ClothBlender';OUT=ART/'VerifiedAttachments';OUT.mkdir(exist_ok=True)
SOURCE=OUT/'Inputs/Assembled-d979b9bc.blend';EXPECTED='d979b9bc9b5002be66b3a9a5e05524a266703ab81dbe386643674e1a53c86bee';assert hashlib.sha256(SOURCE.read_bytes()).hexdigest()==EXPECTED
bpy.ops.wm.open_mainfile(filepath=str(SOURCE));rig=bpy.data.objects['DosaV2_Rig']
for o in bpy.context.scene.objects:
    if o.name.startswith('CTRL_') and 'AuthoringMode' in o:o['AuthoringMode']=False;o.update_tag()
for b in rig.pose.bones:b.matrix_basis=Matrix.Identity(4)
def model(o):
    o.data.calc_loop_triangles();bm=bmesh.new();bm.from_mesh(o.data);bm.verts.ensure_lookup_table();boundary=sorted({v.index for e in bm.edges if e.is_boundary for v in e.verts});bm.free();tri=np.array([list(t.vertices) for t in o.data.loop_triangles])
    return {'rest':np.array([v.co[:] for v in o.data.vertices]),'triangles':tri,'boundary':boundary,'weights':[{o.vertex_groups[g.group].name:g.weight for g in v.groups} for v in o.data.vertices],
      'edges':np.array(sorted({tuple(sorted((int(a),int(b)))) for t in tri for a,b in zip(t,np.roll(t,-1))}))}
names=['DosaV2_BodyCore','DosaV2_SleeveInner_L','DosaV2_SleeveInner_R','DosaV2_SleeveOuter_L','DosaV2_SleeveOuter_R'];models={n:model(bpy.data.objects[n]) for n in names};kd={}
for n,m in models.items():
    tree=KDTree(len(m['boundary']))
    for j,i in enumerate(m['boundary']):tree.insert(m['rest'][i],j)
    tree.balance();kd[n]=tree
rinv={b.name:np.linalg.inv(np.array(b.matrix_local)) for b in rig.data.bones};poses=[];BASE=ART/'FoldedGusset'
for d in json.loads((BASE/'Inputs/static-pose-definitions.json').read_text())['poses']:poses.append((d['id'],{n:np.array(m)@rinv[n] for n,m in d['boneMatricesRigLocal'].items()}))
for f,label in [('fixture_grip_settle_00','fixture_rest_settle'),('fixture_grip_settle_100','fixture_grip_settle'),('fixture_raised_arms_settle_100','fixture_raised_arms_settle')]:
    d=json.loads((BASE/'IntermediatePoseData'/(f+'.json')).read_text());poses.append((label,{n:np.array(m)@rinv[n] for n,m in d['bonePoseMatrices'].items()}))
for file in sorted((BASE/'IntermediatePoseData').glob('*.json')):
    d=json.loads(file.read_text());poses.append((d['poseId'],{n:np.array(m)@rinv[n] for n,m in d['bonePoseMatrices'].items()}))
pose_names=[n for n,mat in poses]
def posed(m):
    p=np.column_stack((m['rest'],np.ones(len(m['rest']))));result=np.zeros((len(poses),len(p),3))
    for bone in set(n for w in m['weights'] for n in w):
        weights=np.array([row.get(bone,0) for row in m['weights']]);mat=np.array([row[bone] for n,row in poses]);result+=np.einsum('pij,vj->pvi',mat[:,:3,:],p)*weights[None,:,None]
    return result
pose_points={n:posed(m) for n,m in models.items()}
authored=json.loads((ART/'SleeveAttachments/sleeve-attachment-report.json').read_text());surfaces=[]
for side in ['L','R']:
    name='DosaV2_SleeveOuter_'+side;m=models[name];o=bpy.data.objects[name];mob=np.array([p.color[0] for p in o.data.color_attributes['ClothMobility'].data]);old=next(r for r in authored['surfaces'] if r['mesh']==name);oldseeds={r['sourceVertex']:r for r in old['actualAttachmentSeeds']};rows=[]
    targets=['DosaV2_BodyCore','DosaV2_SleeveInner_'+side]
    for i,p in enumerate(m['rest']):
        matches=[]
        for n in targets:
            if not models[n]['boundary']:continue
            q,j,d=kd[n].find(p);target=models[n]['boundary'][j];gaps=np.linalg.norm(pose_points[name][:,i]-pose_points[n][:,target],axis=1);worst=int(np.argmax(gaps))
            matches.append({'mesh':n,'boundaryVertex':target,'restDistanceMeters':float(d),'maximumPoseDistanceMeters':float(np.max(gaps)),
             'maximumGapIncreaseMeters':float(max(0,np.max(gaps)-d)),'maximumGapPose':pose_names[worst],'targetRestPoint':models[n]['rest'][target].tolist(),'targetWeights':models[n]['weights'][target]})
        nearest=min(matches,key=lambda r:r['restDistanceMeters']) if matches else None
        row={'vertex':i,'rest':p.tolist(),'oldMaxDistanceMeters':float(mob[i]),'oldWasExactPin':bool(mob[i]==0),'onOpenBoundary':i in m['boundary'],
          'oldAuthoredSeed':oldseeds.get(i),'oldAutomaticTorsoProtection':bool(abs(p[0])<=.22),'weights':m['weights'][i],'nearestBoundary':nearest,'allBoundaryMatches':matches}
        if i in oldseeds:
            match=next((r for r in matches if r['mesh']==oldseeds[i]['otherMesh']),None)
            row['role']='recorded_sewn_seed_currently_aligned' if match and match['restDistanceMeters']<=.006 and match['maximumGapIncreaseMeters']<=.001 else 'recorded_sewn_seed_needs_current_join_repair'
        elif row['onOpenBoundary'] and nearest and nearest['restDistanceMeters']<=.001 and nearest['maximumPoseDistanceMeters']<=.001:row['role']='newly_verified_shared_boundary'
        else:row['role']='unclassified_cloth'
        rows.append(row)
    # Keep both aligned and currently mismatched historical sewing attachments.
    # Mismatch is a repair obligation; it is never a reason to release a seam.
    seeds=[r['vertex'] for r in rows if r['role']!='unclassified_cloth'];adj=[[] for p in m['rest']]
    for a,b in m['edges']:
        length=float(np.linalg.norm(m['rest'][a]-m['rest'][b]));adj[a].append((int(b),length));adj[b].append((int(a),length))
    dist=np.full(len(rows),np.inf);queue=[]
    for i in seeds:dist[i]=0;heapq.heappush(queue,(0,int(i)))
    while queue:
        d,i=heapq.heappop(queue)
        if d>dist[i]:continue
        for j,length in adj[i]:
            v=d+length
            if v<dist[j]:dist[j]=v;heapq.heappush(queue,(v,j))
    components=[];seen=set()
    for start in range(len(rows)):
        if start in seen:continue
        stack=[start];seen.add(start);ids=[]
        while stack:
            i=stack.pop();ids.append(i)
            for j,length in adj[i]:
                if j not in seen:seen.add(j);stack.append(j)
        compid=len(components);components.append({'id':compid,'vertexCount':len(ids),'vertices':ids,'historicalExactPins':int(sum(mob[i]==0 for i in ids)),'verifiedOrHistoricalSewingSeeds':[i for i in ids if i in seeds]})
        for i in ids:rows[i]['component']=compid
    for r,d in zip(rows,dist):
        r['restGraphDistanceToSewingMeters']=float(d) if math.isfinite(d) else None
        if r['role']!='unclassified_cloth':continue
        if not math.isfinite(d):r['role']='unresolved_component_without_verified_sewing'
        elif d<=.006 and r['oldWasExactPin']:r['role']='reinforced_six_mm_sewing_margin'
        elif r['oldWasExactPin']:r['role']='old_automatic_pin_on_unsewn_boundary' if r['onOpenBoundary'] else 'old_automatic_pin_in_cloth_interior'
        else:r['role']='existing_free_cloth'
    np.savez_compressed(OUT/(name+'-source.npz'),rest=m['rest'],edges=m['edges'],mobility=mob,posePoints=pose_points[name],poseNames=np.array(pose_names))
    surfaces.append({'mesh':name,'oldExactPins':int(np.sum(mob==0)),'roles':{role:sum(r['role']==role for r in rows) for role in sorted({r['role'] for r in rows})},'components':components,'vertices':rows})
report={'status':'READ_ONLY_MODEL_CUT_LEDGER_ART_ROLE_HYPOTHESIS_REJECTED','artRoleWarning':'Shared Inner/Outer cut is not a sewn cuff. The reference shows a free outer hem. Legacy role labels are geometric hypotheses, not production pin authorization. See ART_ROLE_CORRECTION.md.','sourceSha256':EXPECTED,'poseCount':len(poses),'surfaces':surfaces,
 'policyIntent':'Required fixed attachment means recorded shared cut/sewing or a current geometrically/kinematically verified shared boundary, plus narrow6mm reinforced sewing margin. Old restX/height rules are not evidence of sewing. Historical seams that moved in newer wrapped-forearm source remain protected pending repair. Components without verified sewing are explicitly unresolved, never released into free floating cloth.',
 'thresholdMeaning':{'historicalSeamRestMatchMeters':.006,'maximumHistoricalGapIncreaseMeters':.001,'newExactSharedBoundaryMaximumRestAndPosedGapMeters':.001,'reinforcedSeamWidthMeters':.006},
 'targetBoundaries':{n:{'vertices':len(models[n]['rest']),'boundaryVertices':len(models[n]['boundary'])} for n in names if 'Outer' not in n}}
(OUT/'attachment-role-ledger.json').write_text(json.dumps(report,indent=2),encoding='utf-8');print(json.dumps({'sourceSha256':EXPECTED,'poseCount':len(poses),'targetBoundaries':report['targetBoundaries'],'surfaces':[{'mesh':r['mesh'],'oldExactPins':r['oldExactPins'],'roles':r['roles'],'components':len(r['components'])} for r in surfaces]},indent=2),flush=True)
