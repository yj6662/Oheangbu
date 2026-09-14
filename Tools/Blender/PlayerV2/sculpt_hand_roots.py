"""Local topology and corrective rest sculpt outside the calibrated shaft-contact surface."""
import bpy,bmesh,json,math,hashlib,sys,heapq
import numpy as np
from pathlib import Path
from mathutils import Matrix,Vector
from mathutils.bvhtree import BVHTree
from mathutils.kdtree import KDTree
ROOT=Path(__file__).resolve().parents[3];ART=ROOT/'Art/PlayerV2';OUT=ART/'Inspect/HandsWithSleeves'
SOURCE=ART/'DosaV2_HandsRefined.blend';sourcehash=hashlib.sha256(SOURCE.read_bytes()).hexdigest()
BALANCED='--balanced' in sys.argv
FEATHERED='--feathered' in sys.argv
BASIS_ONLY='--basis-only' in sys.argv
bpy.ops.wm.open_mainfile(filepath=str(SOURCE));rig=bpy.data.objects['DosaV2_Rig'];hand=bpy.data.objects['DosaV2_Hands']
cal=json.loads((ART/'Calibration/hand-brush-contact-report.json').read_text())['sides']
handle=bpy.data.objects['DosaBrushV2_Handle'];grip=bpy.data.objects['GripSocket'];local=grip.matrix_world.inverted()@handle.matrix_world
handle.data.calc_loop_triangles();shaft=BVHTree.FromPolygons([local@v.co for v in handle.data.vertices],[t.vertices for t in handle.data.loop_triangles],all_triangles=True)
names=[b.name for b in rig.data.bones];indices={n:i for i,n in enumerate(names)}
def pose(side):
    for p in rig.pose.bones:p.matrix_basis=Matrix.Identity(4)
    for finger,triplet in cal[side]['finger_euler_xyz_degrees'].items():
        for j,angle in enumerate(triplet):
            p=rig.pose.bones[side+'Hand'+finger+str(j+1)];p.rotation_mode='XYZ';p.rotation_euler=[math.radians(a) for a in angle]
    bpy.context.view_layer.update()
    return rig.pose.bones[side+'BrushGrip'].matrix.copy()
def weights():
    result=np.zeros((len(hand.data.vertices),len(names)))
    for v in hand.data.vertices:
        for g in v.groups:result[v.index,indices[hand.vertex_groups[g.group].name]]=g.weight
    return result
def matrices():return np.asarray([rig.pose.bones[n].matrix@rig.data.bones[n].matrix_local.inverted() for n in names])
def neighbors():
    result=[set() for _ in hand.data.vertices]
    for e in hand.data.edges:a,b=e.vertices;result[a].add(b);result[b].add(a)
    return result
rest=np.asarray([v.co for v in hand.data.vertices]);W=weights();near=np.zeros(len(rest),dtype=bool)
for side,sign in [('Right',-1),('Left',1)]:
    world=pose(side);M=np.einsum('vb,bij->vij',W,matrices());points=np.einsum('vij,vj->vi',M,np.column_stack((rest,np.ones(len(rest)))))
    points=points@np.asarray(world.inverted()).T
    for i in np.where(rest[:,0]*sign>.60)[0]:
        if abs(points[i,1])<.095 and shaft.find_nearest(Vector(points[i,:3]))[3]<.004:near[i]=True
adj=neighbors()
for _ in range(2):near=np.asarray([near[i] or any(near[j] for j in ns) for i,ns in enumerate(adj)])
old_count=len(rest);hand.data.calc_loop_triangles();old_triangles=len(hand.data.loop_triangles)
protected={i:rest[i].copy() for i in np.where(near)[0]}
bm=bmesh.new();bm.from_mesh(hand.data);bm.verts.ensure_lookup_table()
original_bm_vertices=list(bm.verts)
lock=bm.verts.layers.int.new('ContactLocked');origin=bm.verts.layers.int.new('OriginalVertexIndex')
for v in bm.verts:v[lock]=int(near[v.index]);v[origin]=v.index
for _ in range(2):
    candidates=[]
    for e in bm.edges:
        if e.calc_length()<.006 or any(v[lock] for v in e.verts):continue
        if not all(.652<abs(v.co.x)<.741 and abs(v.co.y)<.061 for v in e.verts):continue
        candidates.append(e)
    if not candidates:break
    bmesh.ops.subdivide_edges(bm,edges=candidates,cuts=1,use_grid_fill=True)
bm.to_mesh(hand.data);bm.free();hand.data.update()
kd=KDTree(len(hand.data.vertices))
for v in hand.data.vertices:kd.insert(v.co,v.index)
kd.balance();old_to_new={}
for i,p in enumerate(rest):
    co,index,distance=kd.find(Vector(p))
    if distance<1e-8:old_to_new[i]=index
assert all(i in old_to_new for i in protected), 'Topology operation removed a protected contact vertex'
protected={old_to_new[i]:p for i,p in protected.items()}
# Blender interpolates deform layers at new vertices; export remains normalized four-weight skin.
for v in hand.data.vertices:
    groups=sorted([(g.group,g.weight) for g in v.groups if g.weight>1e-7],key=lambda p:-p[1])[:4];total=sum(w for _,w in groups)
    for g in hand.vertex_groups:g.remove([v.index])
    for gid,w in groups:hand.vertex_groups[gid].add([v.index],w/total,'REPLACE')
rest=np.asarray([v.co for v in hand.data.vertices]);W=weights();adj=neighbors();report={'sides':{}}
# Contact protection is recomputed after interpolation; original protected vertices also remain fixed.
near=np.zeros(len(rest),dtype=bool)
for i in protected:near[i]=True
hand.data.calc_loop_triangles();tri=np.asarray([t.vertices for t in hand.data.loop_triangles]);edges={}
for i,f in enumerate(tri):
    for a,b in zip(f,np.roll(f,-1)):edges.setdefault(tuple(sorted((a,b))),[]).append(i)
result=rest.copy()
for side,sign in [('Right',-1),('Left',1)]:
    world=pose(side);M=np.einsum('vb,bij->vij',W,matrices());inverse=np.linalg.inv(M)
    homogeneous=np.column_stack((rest,np.ones(len(rest))));baseline=np.einsum('vij,vj->vi',M,homogeneous)[:,:3]
    in_grip=np.column_stack((baseline,np.ones(len(rest))))@np.asarray(world.inverted()).T
    for i in np.where(rest[:,0]*sign>.60)[0]:
        if abs(in_grip[i,1])<.095 and shaft.find_nearest(Vector(in_grip[i,:3]))[3]<.004:near[i]=True
    sideids=rest[:,0]*sign>.60;fwd=rest[:,0]*sign-.663
    region=sideids&(fwd>-.010)&(fwd<.080)&(np.abs(rest[:,1])<.061)&(~near)
    amount=np.minimum(np.clip((fwd+.010)/.014,0,1),np.clip((.080-fwd)/.015,0,1))*region
    if FEATHERED:
        distances=np.where(region,np.inf,0.0);queue=[(0.0,int(i)) for i in np.where(~region)[0]];heapq.heapify(queue)
        while queue:
            distance,i=heapq.heappop(queue)
            if distance>distances[i] or distance>.024:continue
            for j in adj[i]:
                d=distance+float(np.linalg.norm(rest[i]-rest[j]))
                if d<distances[j]:distances[j]=d;heapq.heappush(queue,(d,int(j)))
        feather=np.clip(distances/.020,0,1);amount*=feather*feather*(3-2*feather)
    pair=np.asarray([f for e,f in edges.items() if len(f)==2 and all(region[j] or sideids[j] and abs(fwd[j])<.078 for j in e)],dtype=int)
    def score(p):
        points=p[tri];n=np.cross(points[:,1]-points[:,0],points[:,2]-points[:,0]);n/=np.maximum(np.linalg.norm(n,axis=1)[:,None],1e-15)
        angle=np.degrees(np.arccos(np.clip(np.sum(n[pair[:,0]]*n[pair[:,1]],axis=1),-1,1)))
        return {'over120':int(np.sum(angle>120)),'over90':int(np.sum(angle>90)),'excess45':float(np.maximum(angle-45,0).sum())}
    current=baseline.copy();working=rest.copy();best=(score(current),rest.copy(),0);variants=[]
    rest_baseline=score(rest)
    normal_matrix=np.transpose(M[:,:3,:3],(0,2,1))
    regularized_inverse=np.linalg.inv(normal_matrix@M[:,:3,:3]+np.eye(3)[None,:,:]*1.5)
    if BALANCED:
        for k in best[0]:best[0][k]+=rest_baseline[k]
    for iteration in range(1,25):
        proposed=current.copy();local=working.copy()
        for i in np.where(region)[0]:
            delta_pose=np.mean(current[list(adj[i])],axis=0)-current[i]
            if BALANCED:
                delta_rest=np.mean(working[list(adj[i])],axis=0)-working[i]
                delta_local=regularized_inverse[i]@(normal_matrix[i]@delta_pose+1.5*delta_rest)
                local[i]+=delta_local*.36*amount[i]
            else:proposed[i]+=delta_pose*.36*amount[i]
        if not BALANCED:local=np.einsum('vij,vj->vi',inverse,np.column_stack((proposed,np.ones(len(proposed)))))[:,:3]
        delta=local-rest;length=np.linalg.norm(delta,axis=1);delta*=np.minimum(1,.006/np.maximum(length,1e-15))[:,None]
        local=rest+delta;local[~region]=rest[~region];working=local.copy();current=np.einsum('vij,vj->vi',M,np.column_stack((local,np.ones(len(local)))))[:,:3]
        if iteration not in [2,4,8,12,18,24]:continue
        s=score(current)
        if BALANCED:
            rs=score(local)
            for k in s:s[k]+=rs[k]
        variants.append(dict(iteration=iteration,**s))
        key=lambda q:(q['over120'],q['over90'],q['excess45'])
        if key(s)<key(best[0]):best=(s,local.copy(),iteration)
    result[sideids]=best[1][sideids]
    report['sides'][side]={'before':score(baseline),'rest_before':rest_baseline,'rest_after':score(best[1]),'after':best[0],'iterations':best[2],'variants':variants,'moved':int(np.sum(np.linalg.norm(result-rest,axis=1)[sideids]>1e-7)),
                         'max_rest_sculpt_m':float(np.linalg.norm(result-rest,axis=1)[sideids].max())}
if BASIS_ONLY:
    result=rest.copy();movable=(np.abs(rest[:,0])>.651)&(np.abs(rest[:,0])<.741)&(np.abs(rest[:,1])<.061)
    for i in protected:movable[i]=False
    distance=np.where(movable,np.inf,0.0);queue=[(0.0,int(i)) for i in np.where(~movable)[0]];heapq.heapify(queue)
    while queue:
        d,i=heapq.heappop(queue)
        if d>distance[i] or d>.020:continue
        for j in adj[i]:
            nd=d+float(np.linalg.norm(rest[i]-rest[j]))
            if nd<distance[j]:distance[j]=nd;heapq.heappush(queue,(nd,int(j)))
    a=np.clip(distance/.016,0,1);a=a*a*(3-2*a)*movable
    for _ in range(12):
        proposed=result.copy()
        for i in np.where(movable)[0]:proposed[i]+=(np.mean(result[list(adj[i])],axis=0)-result[i])*.35*a[i]
        delta=proposed-rest;length=np.linalg.norm(delta,axis=1);delta*=np.minimum(1,.0025/np.maximum(length,1e-15))[:,None];result=rest+delta
    report['basis_only']={'method':'Open-hand rest surface relaxation, 16mm geodesic feather outside contact lock','max_displacement_m':float(np.linalg.norm(result-rest,axis=1).max())}
for i,p in protected.items():result[i]=p
for v,p in zip(hand.data.vertices,result):v.co=p;v.select=False
for p in hand.data.polygons:p.use_smooth=True
hand.data.update();hand.data.calc_loop_triangles()
report.update(status='CONTACT_LOCKED_LOCAL_SCULPT_PENDING_VISUAL_AND_DENSE_CONTACT',old_vertices=old_count,new_vertices=len(rest),old_triangles=old_triangles,new_triangles=len(hand.data.loop_triangles),
              original_contact_vertices_unchanged=all(np.array_equal(np.asarray(hand.data.vertices[i].co),p) for i,p in protected.items()),protected_vertices=len(protected),actions=len(bpy.data.actions),source_unchanged=sourcehash==hashlib.sha256(SOURCE.read_bytes()).hexdigest())
report['original_to_new_vertex_indices']={str(k):int(v) for k,v in old_to_new.items() if k!=v}
assert report['original_contact_vertices_unchanged']
for p in rig.pose.bones:p.matrix_basis=Matrix.Identity(4)
bpy.context.view_layer.update();bpy.ops.file.pack_all();bpy.ops.wm.save_as_mainfile(filepath=str(ART/('DosaV2_HandsBasis.blend' if BASIS_ONLY else 'DosaV2_HandsFeathered.blend' if FEATHERED else 'DosaV2_HandsBalanced.blend' if BALANCED else 'DosaV2_HandsSculpted.blend')))
(OUT/('hand-root-basis-sculpt.json' if BASIS_ONLY else 'hand-root-feathered-sculpt.json' if FEATHERED else 'hand-root-balanced-sculpt.json' if BALANCED else 'hand-root-sculpt.json')).write_text(json.dumps(report,indent=2));print(json.dumps(report),flush=True)
