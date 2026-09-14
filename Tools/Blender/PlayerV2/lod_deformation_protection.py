"""LOD-only repairs: structured cuff rings and contact-locked hand correctives.

The assembled source is read-only. This module operates on the current derived
Blender scene; the generator records every operation in its LOD report.
"""
import ast, heapq, json, math
from pathlib import Path
import bpy, bmesh
import numpy as np
from mathutils import Matrix, Vector, Euler
from mathutils.bvhtree import BVHTree
from mathutils.geometry import barycentric_transform

HERE=Path(__file__).resolve().parent
ART=HERE.parents[2]/'Art/PlayerV2'
_module=ast.parse((HERE/'audit_triangle_crossings.py').read_text())
_fn=next(n for n in _module.body if isinstance(n,ast.FunctionDef) and n.name=='proper_crossings')
exec(compile(ast.Module(body=[_fn],type_ignores=[]),'proper_triangle_crossing','exec'))

def select(obj):
    bpy.ops.object.select_all(action='DESELECT');obj.hide_set(False);obj.select_set(True);bpy.context.view_layer.objects.active=obj

def coarse_backpanel(obj):
    """Rebuild one rigid far-LOD accessory whose scan has nonmanifold edges."""
    me=obj.data;me.calc_loop_triangles();vertices=[v.co.copy() for v in me.vertices];tri=[tuple(t.vertices) for t in me.loop_triangles]
    uvname=me.uv_layers.active.name;uvs=[[Vector((*me.uv_layers.active.data[li].uv,0)) for li in t.loops] for t in me.loop_triangles]
    source=BVHTree.FromPolygons(vertices,tri,all_triangles=True)
    materials=list(me.materials)
    triangle_materials=[t.material_index for t in me.loop_triangles]
    source_material_counts={i:triangle_materials.count(i) for i in sorted(set(triangle_materials))}
    # A rebuilt face has one material, while its corners may straddle source
    # triangles. Never interpolate a corner UV from a different material atlas.
    material_triangles={i:[t for t,m in enumerate(triangle_materials) if m==i]
                        for i in source_material_counts}
    material_trees={i:BVHTree.FromPolygons(vertices,[tri[t] for t in ids],all_triangles=True)
                    for i,ids in material_triangles.items()}
    weights=[{obj.vertex_groups[g.group].name:g.weight for g in v.groups} for v in me.vertices]
    voxel=.004 if obj.name=='DosaPackV2_BrushBundle' else .009
    before=len(tri);select(obj)
    if obj.name!='DosaPackV2_BrushBundle':
        # Use this only for one small tube/pouch, never the complete backpanel:
        # a hull over the whole panel would fill spaces occupied by its bottles.
        bm=bmesh.new()
        for p in vertices:bm.verts.new(p)
        result=bmesh.ops.convex_hull(bm,input=list(bm.verts),use_existing_faces=False)
        bmesh.ops.delete(bm,geom=[v for v in bm.verts if not v.link_faces],context='VERTS')
        bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(me);bm.free();me.update()
        method='Closed source envelope of one independently rigid far-LOD accessory; no scale change.'
    else:
        mod=obj.modifiers.new('LOD_RigidRearShell','REMESH');mod.mode='VOXEL';mod.voxel_size=voxel;mod.use_smooth_shade=True
        while obj.modifiers.find(mod.name)>0:bpy.ops.object.modifier_move_up(modifier=mod.name)
        bpy.ops.object.modifier_apply(modifier=mod.name)
        method='Voxel reconstruction of fixed rear brush bundle.'
    me=obj.data
    me.materials.clear()
    for material in materials:me.materials.append(material)
    uv=me.uv_layers.get(uvname) or me.uv_layers.new(name=uvname)
    for v in me.vertices:
        p,n,t,d=source.find_nearest(v.co);a,b,c=tri[t];w=barycentric_transform(p,vertices[a],vertices[b],vertices[c],Vector((1,0,0)),Vector((0,1,0)),Vector((0,0,1)))
        combined={}
        for i,f in zip([a,b,c],w):
            for name,weight in weights[i].items():combined[name]=combined.get(name,0)+weight*f
        for g in obj.vertex_groups:g.remove([v.index])
        for name,weight in combined.items():
            if weight>1e-8:obj.vertex_groups[name].add([v.index],weight,'REPLACE')
    material_projection=[]
    for polygon in me.polygons:
        center=sum((me.vertices[i].co for i in polygon.vertices),Vector())/len(polygon.vertices)
        _,_,source_triangle,_=source.find_nearest(center)
        assert source_triangle is not None,(obj.name,polygon.index,'no source triangle')
        material=triangle_materials[source_triangle]
        polygon.material_index=material
        corner_sources=[]
        for li in polygon.loop_indices:
            p,n,local_triangle,d=material_trees[material].find_nearest(me.vertices[me.loops[li].vertex_index].co)
            assert local_triangle is not None,(obj.name,polygon.index,'no same-material UV source')
            t=material_triangles[material][local_triangle];a,b,c=tri[t]
            assert triangle_materials[t]==material
            uv.data[li].uv=barycentric_transform(p,vertices[a],vertices[b],vertices[c],*uvs[t])[:2]
            corner_sources.append(t)
        material_projection.append({'face':polygon.index,'slot':material,'sourceFaceTriangle':source_triangle,
                                    'sourceCornerTriangles':corner_sources})
    me.calc_loop_triangles()
    rebuilt_counts={i:sum(t.material_index==i for t in me.loop_triangles) for i in source_material_counts}
    assert all(rebuilt_counts.values()),(obj.name,'reconstruction lost an authored material surface',rebuilt_counts)
    return {'mesh':obj.name,'sourceTriangles':before,'remeshedTriangles':len(me.loop_triangles),'voxelSizeM':voxel if obj.name=='DosaPackV2_BrushBundle' else None,'method':method,
        'materialSlots':[m.name if m else None for m in materials],
        'sourceMaterialTriangleCounts':source_material_counts,'rebuiltMaterialTriangleCounts':rebuilt_counts,
        'materialProjection':material_projection,
        'scope':'LOD2 independently rigid small accessory or fixed brush bundle only. Each rebuilt face retains the nearest original triangle material identity; every corner UV is projected from source triangles of that same material. Bone weights use the original nearest surface. Original object/source unchanged.'}

def decimate_hand_in_grip(obj,ratio,label):
    """Preserve grip/rest correspondence through triangle reduction and skin inversion."""
    grip_qem=obj.get('LODLevel',1)==1
    me=obj.data;keys=me.shape_keys;me.calc_loop_triangles();tri=[tuple(t.vertices) for t in me.loop_triangles]
    basis=[v.co.copy() for v in keys.key_blocks['Basis'].data]
    shape=[keys.key_blocks['GripPalmRelax_'+('Right' if v.x<0 else 'Left')].data[i].co.copy() for i,v in enumerate(basis)]
    rig=next(m.object for m in obj.modifiers if m.type=='ARMATURE');definition=next(d for d in json.loads((ART/'Validation/static-pose-definitions.json').read_text())['poses'] if d['id']=='grip_down')
    deform={b.name:obj.matrix_world.inverted()@rig.matrix_world@Matrix(definition['boneMatricesRigLocal'][b.name])@b.matrix_local.inverted()@rig.matrix_world.inverted()@obj.matrix_world for b in rig.data.bones}
    weights=[{obj.vertex_groups[g.group].name:g.weight for g in v.groups} for v in me.vertices]
    posed=[sum((deform[name]@shape[i]*w for name,w in row.items()),Vector()) for i,row in enumerate(weights)]
    def cross_count(coords,triangles):
        co=np.array(coords);ts=np.array(triangles);t=BVHTree.FromPolygons(coords,triangles,all_triangles=True);pairs=np.array([(a,b) for a,b in t.overlap(t) if a<b and not set(ts[a]).intersection(ts[b])],dtype=int)
        return int(np.sum(proper_crossings(co[ts[pairs[:,0]]],co[ts[pairs[:,1]]]))) if len(pairs) else 0
    assert cross_count(posed,tri)==0,'Source hand grip is invalid before LOD authoring.'
    tree=BVHTree.FromPolygons(posed,tri,all_triangles=True)
    contact=json.loads((ART/'Inspect/HandsWithSleeves/self-fold-corrected-dense-contact-validation.json').read_text())
    assert len(basis)==8745,'Contact-index protection is certified for the f94 8745-vertex hand; recalibrate changed source hands.'
    locked={r['fingers'][f]['contact_vertex_index'] for side in contact['sides'].values() for r in side.values() for f in ['Thumb','Index','Middle']}
    neighbors=[set() for _ in basis]
    for e in me.edges:a,b=e.vertices;neighbors[a].add(b);neighbors[b].add(a)
    for _ in range(4):locked.update(j for i in list(locked) for j in neighbors[i])
    # Preserve the high-amplitude palm/knuckle corrective support itself; it is
    # an anatomical fold region even when QEM sees a locally flat gripped patch.
    critical={i for i,(a,b) in enumerate(zip(basis,shape)) if (a-b).length>.004}
    critical.update(j for i in list(critical) for j in neighbors[i]);locked.update(critical)
    obj.shape_key_clear()
    rest_attribute=me.attributes.new(name='LOD_SourceGrip',type='FLOAT_VECTOR',domain='POINT')
    for record,p in zip(rest_attribute.data,posed):record.vector=p
    for v,p in zip(me.vertices,posed if grip_qem else basis):v.co=p
    me.update();importance=obj.vertex_groups.new(name='LOD_ExactGripPatch')
    for i in locked:importance.add([i],1.,'REPLACE')
    select(obj);mod=obj.modifiers.new(label,'DECIMATE');mod.ratio=ratio;mod.vertex_group=importance.name;mod.invert_vertex_group=True;mod.vertex_group_factor=1.;mod.use_collapse_triangulate=True
    while obj.modifiers.find(mod.name)>0:bpy.ops.object.modifier_move_up(modifier=mod.name)
    bpy.ops.object.modifier_apply(modifier=mod.name);obj.vertex_groups.remove(obj.vertex_groups['LOD_ExactGripPatch'])
    me=obj.data;new_grip=[v.co.copy() for v in me.vertices]
    new_basis=new_grip.copy();target=[record.vector.copy() for record in me.attributes['LOD_SourceGrip'].data]
    if grip_qem:target=new_grip.copy()
    me.calc_loop_triangles()
    for i,p in enumerate(new_grip):
        combined={obj.vertex_groups[g.group].name:g.weight for g in me.vertices[i].groups}
        if grip_qem:
            nearest,normal,index,distance=tree.find_nearest(p);a,b,c=tri[index]
            bary=barycentric_transform(nearest,posed[a],posed[b],posed[c],Vector((1,0,0)),Vector((0,1,0)),Vector((0,0,1)))
            new_basis[i]=sum((basis[j]*w for j,w in zip([a,b,c],bary)),Vector());combined={}
            for j,w in zip([a,b,c],bary):
                for name,value in weights[j].items():combined[name]=combined.get(name,0)+value*w
        best=sorted([(name,w) for name,w in combined.items() if w>1e-8],key=lambda x:-x[1])[:4];total=sum(w for _,w in best)
        for group in obj.vertex_groups:group.remove([i])
        for name,w in best:obj.vertex_groups[name].add([i],w/total,'REPLACE')
    me.attributes.remove(me.attributes['LOD_SourceGrip'])
    for v,p in zip(me.vertices,new_basis):v.co=p
    obj.shape_key_add(name='Basis')
    for side in ['Right','Left']:
        key=obj.shape_key_add(name='GripPalmRelax_'+side)
        for i,p in enumerate(new_basis):key.data[i].co=p
        for i,v in enumerate(me.vertices):
            if (side=='Right')!=(v.co.x<0):continue
            matrix=sum((deform[obj.vertex_groups[g.group].name]*g.weight for g in v.groups),Matrix(((0.,)*4,)*4))
            key.data[i].co=matrix.inverted()@target[i]
    me.update()
    reconstructed=[]
    assert all(max((k.co-me.shape_keys.key_blocks['Basis'].data[i].co).length for i,k in enumerate(me.shape_keys.key_blocks['GripPalmRelax_'+s].data) if (s=='Right')!=(me.shape_keys.key_blocks['Basis'].data[i].co.x<0))==0 for s in ['Right','Left'])
    for i,v in enumerate(me.vertices):
        key=me.shape_keys.key_blocks['GripPalmRelax_'+('Right' if v.co.x<0 else 'Left')]
        reconstructed.append(sum((deform[obj.vertex_groups[g.group].name]@key.data[i].co*g.weight for g in v.groups),Vector()))
    assert max((a-b).length for a,b in zip(reconstructed,target))<.000002
    for b in rig.pose.bones:b.matrix_basis=Matrix.Identity(4)
    for e in definition['boneRotations']:rig.pose.bones[e['bone']].matrix_basis=Euler([math.radians(e[k]) for k in ['x','y','z']],'XYZ').to_matrix().to_4x4()
    for key in list(me.shape_keys.key_blocks)[1:]:key.value=1
    bpy.context.view_layer.update();evaluated=obj.evaluated_get(bpy.context.evaluated_depsgraph_get());em=evaluated.to_mesh()
    actual=[v.co.copy() for v in em.vertices];assert max((a-b).length for a,b in zip(actual,target))<.000002;evaluated.to_mesh_clear()
    for b in rig.pose.bones:b.matrix_basis=Matrix.Identity(4)
    for key in list(me.shape_keys.key_blocks)[1:]:key.value=0
    bpy.context.view_layer.update()
    # Sparse rest correspondence can bridge a convex finger web. Repair only
    # this LOD Basis; the absolute own-side grip shape remains untouched.
    base=np.array([v.co for v in me.shape_keys.key_blocks['Basis'].data]);coords=base.copy();me.calc_loop_triangles();ts=np.array([t.vertices for t in me.loop_triangles])
    def actual_pairs(p):
        tree=BVHTree.FromPolygons(p.tolist(),ts.tolist(),all_triangles=True);pairs=np.array([(a,b) for a,b in tree.overlap(tree) if a<b and not set(ts[a]).intersection(ts[b])],dtype=int)
        return pairs[proper_crossings(p[ts[pairs[:,0]]],p[ts[pairs[:,1]]])] if len(pairs) else []
    initial=len(actual_pairs(coords));best=(initial,coords.copy())
    for iteration in range(100):
        hits=actual_pairs(coords)
        if not len(hits):best=(0,coords.copy());break
        delta=np.zeros_like(coords);count=np.zeros(len(coords))
        for a,b in hits:
            ia,ib=ts[a],ts[b];ta,tb=coords[ia],coords[ib];ea=np.roll(ta,-1,axis=0)-ta;eb=np.roll(tb,-1,axis=0)-tb;choices=[]
            for axis in [np.cross(ea[0],ea[1]),np.cross(eb[0],eb[1])]+[np.cross(x,y) for x in ea for y in eb]:
                length=np.linalg.norm(axis)
                if length<1e-12:continue
                axis/=length;pa=ta@axis;pb=tb@axis;choices.extend([(pb.max()-pa.min()+.00004,axis),(pa.max()-pb.min()+.00004,-axis)])
            distance,direction=min((q for q in choices if q[0]>0),key=lambda q:q[0]);distance=min(distance,.0003)
            for ids,sign in [(ia,1),(ib,-1)]:
                for i in ids:delta[i]+=direction*distance*sign*.5;count[i]+=1
        use=count>0;delta[use]/=count[use,None];coords+=delta*.7
        disp=coords-base;length=np.linalg.norm(disp,axis=1);coords=base+disp*np.minimum(1,.003/np.maximum(length,1e-15))[:,None]
        value=len(actual_pairs(coords))
        if value<best[0]:best=(value,coords.copy())
    for i,p in enumerate(best[1]):
        me.shape_keys.key_blocks['Basis'].data[i].co=p
        me.shape_keys.key_blocks['GripPalmRelax_'+('Left' if base[i,0]<0 else 'Right')].data[i].co=p
    me.update();obj['LODOpenBasisRepair']=json.dumps({'initialCrossings':initial,'finalCrossings':best[0],'movedVertices':int(np.sum(np.linalg.norm(best[1]-base,axis=1)>1e-8)),
        'maxChangeM':float(np.linalg.norm(best[1]-base,axis=1).max()),'ownSideGripAbsolutePositionsUnchanged':True})
    obj['LODHandMethod']=('LOD1 actual grip surface QEM with source Basis correspondence' if grip_qem else 'LOD2 rest QEM with evaluated grip coordinates carried in vertex attributes')+'; primary contacts/high corrective regions protected; explicit triangles; inverse normalized4-weight LBS; opposite-hand delta zero.'

def structured_cuff(obj,level,decimate):
    """Keep closed outer/inner rings; generic collapse only touches scan cloth."""
    me=obj.data;slot=next(i for i,m in enumerate(me.materials) if m and m.name=='DosaV2_WristWrapping')
    material=me.materials[slot];source_count=len(me.vertices)
    ids={i for p in me.polygons if p.material_index==slot for i in p.vertices}
    votes={i:0. for i in ids}
    def axis_z(x):return 1.336-(abs(x)-.442)/(.663-.442)*.009
    for p in me.polygons:
        if p.material_index!=slot:continue
        center=p.center;radial=Vector((0,center.y,center.z-axis_z(center.x)))
        score=p.normal.dot(radial)
        for i in p.vertices:votes[i]+=score
    groups=[{},{}]
    for i in ids:
        v=me.vertices[i];kind=0 if votes[i]>0 else 1
        groups[kind].setdefault(round(abs(v.co.x),6),[]).append(i)
    assert all(len(row)==24 for g in groups for row in g.values()),[(x,len(v)) for g in groups for x,v in g.items()]
    segments=16 if level==1 else 12
    coordinates=[];weights=[];faces=[];uvfaces=[];chains=[]
    counts=[]
    for interior,g in enumerate(groups):
        xs=sorted(g);wanted=set(xs if interior else [xs[int(round(t*(len(xs)-1)))] for t in np.linspace(0,1,12 if level==1 else 8)])
        # Preserve the nonlinear wrist-to-hand skin transition and both openings.
        for critical in [.638,.665]:wanted.add(min(xs,key=lambda x:abs(x-critical)))
        xs=sorted(wanted);chain=[]
        for x in xs:
            source=g[x];center=sum((me.vertices[i].co for i in source),Vector())/len(source)
            ordered=sorted(source,key=lambda i:math.atan2(me.vertices[i].co.z-center.z,me.vertices[i].co.y))
            row=[]
            for k in range(segments):
                # Interpolate neighbouring source ring samples, never across walls.
                f=k*24/segments;ia=ordered[int(f)%24];ib=ordered[(int(f)+1)%24];t=f-int(f)
                row.append(len(coordinates));coordinates.append(me.vertices[ia].co.lerp(me.vertices[ib].co,t))
                w={}
                for j,a in [(ia,1-t),(ib,t)]:
                    for q in me.vertices[j].groups:w[obj.vertex_groups[q.group].name]=w.get(obj.vertex_groups[q.group].name,0)+q.weight*a
                weights.append(w)
            chain.append(row)
        for j in range(len(xs)-1):
            for k in range(segments):
                face=[chain[j][k],chain[j][(k+1)%segments],chain[j+1][(k+1)%segments],chain[j+1][k]]
                a=k/segments-.5;b=(k+1)/segments-.5;v=(xs[j]-.426)/.257;w=(xs[j+1]-.426)/.257
                uv=[(a,v),(b,v),(b,w),(a,w)]
                if interior:face.reverse();uv.reverse()
                faces.append(face);uvfaces.append(uv)
        chains.append(chain);counts.append(len(xs))
    for end in [0,-1]:
        for k in range(segments):
            faces.append([chains[0][end][k],chains[1][end][k],chains[1][end][(k+1)%segments],chains[0][end][(k+1)%segments]])
            uvfaces.append([(k/segments,0),((k+.2)/segments,0),((k+.8)/segments,0),((k+1)/segments,0)])
    uvname=me.uv_layers.active.name
    bm=bmesh.new();bm.from_mesh(me);remove=[f for f in bm.faces if f.material_index==slot]
    bmesh.ops.delete(bm,geom=remove,context='FACES');bmesh.ops.delete(bm,geom=[v for v in bm.verts if not v.link_faces],context='VERTS');bm.to_mesh(me);bm.free()
    me.calc_loop_triangles();scan_before=len(me.loop_triangles)
    select(obj);target=330 if level==1 else 125
    if scan_before>target:decimate(obj,target/scan_before,'ScanSleeve_LOD')
    mesh=bpy.data.meshes.new('LOD_StructuredCuff');mesh.from_pydata(coordinates,[],faces);mesh.materials.append(material);mesh.update()
    uv=mesh.uv_layers.new(name=uvname)
    for p,values in zip(mesh.polygons,uvfaces):
        p.use_smooth=True
        for li,value in zip(p.loop_indices,values):uv.data[li].uv=value
    cuff=bpy.data.objects.new('LOD_StructuredCuff',mesh);bpy.context.scene.collection.objects.link(cuff);cuff.matrix_world=obj.matrix_world
    for i,w in enumerate(weights):
        for name,value in w.items():
            if value>1e-8:(cuff.vertex_groups.get(name) or cuff.vertex_groups.new(name=name)).add([i],value,'REPLACE')
    bm=bmesh.new();bm.from_mesh(mesh);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(mesh);bm.free()
    mesh.calc_loop_triangles();wrap_triangles=len(mesh.loop_triangles)
    select(obj);cuff.select_set(True);bpy.ops.object.join();obj.data.calc_loop_triangles()
    return {'mesh':obj.name,'method':'Structured closed cuff rings sampled within each original wall; source scan sleeve reduced separately.',
            'sourceVertices':source_count,'ringCounts':counts,'segments':segments,'wrappingTriangles':wrap_triangles,
            'scanTrianglesBefore':scan_before,'finalTriangles':len(obj.data.loop_triangles),'preservedSkinSource':'Source ring interpolation; no UpperArm influence introduced.',
            'uv':'Same authored cylindrical source UV convention; scan UV corners preserved by Decimate.'}

def repair_hand_correctives(hand,rig):
    """Fix actual LOD crossing roots while preserving measured shaft vicinity."""
    defs=json.loads((ART/'Validation/static-pose-definitions.json').read_text())['poses']
    keys=hand.data.shape_keys;hand.data.calc_loop_triangles();tri=np.array([t.vertices for t in hand.data.loop_triangles],dtype=int)
    basis=np.array([v.co for v in keys.key_blocks['Basis'].data]);n=len(basis)
    original=np.array([keys.key_blocks['GripPalmRelax_'+('Right' if p[0]<0 else 'Left')].data[i].co for i,p in enumerate(basis)])
    neighbors=[set() for _ in range(n)]
    for e in hand.data.edges:a,b=e.vertices;neighbors[a].add(b);neighbors[b].add(a)
    def pose(d,alpha=1.):
        for b in rig.pose.bones:b.matrix_basis=Matrix.Identity(4)
        for side in ['Right','Left']:keys.key_blocks['GripPalmRelax_'+side].value=d.get('handGripCorrectives',{}).get(side.lower(),0)*alpha
        for e in d['boneRotations']:rig.pose.bones[e['bone']].matrix_basis=Euler([math.radians(e[k])*alpha for k in ['x','y','z']],'XYZ').to_matrix().to_4x4()
        bpy.context.view_layer.update()
    def points():
        e=hand.evaluated_get(bpy.context.evaluated_depsgraph_get());m=e.to_mesh();p=np.array([e.matrix_world@v.co for v in m.vertices]);e.to_mesh_clear();return p
    def crossings(p):
        tree=BVHTree.FromPolygons(p.tolist(),tri.tolist(),all_triangles=True);pairs=np.array([(a,b) for a,b in tree.overlap(tree) if a<b and not set(tri[a]).intersection(tri[b])],dtype=int)
        return pairs[proper_crossings(p[tri[pairs[:,0]]],p[tri[pairs[:,1]]])] if len(pairs) else np.empty((0,2),dtype=int)
    def set_shape(co):
        for i,p in enumerate(co):keys.key_blocks['GripPalmRelax_'+('Right' if basis[i,0]<0 else 'Left')].data[i].co=p
        hand.data.update();bpy.context.view_layer.update()
    with bpy.data.libraries.load(str(ART/'DosaBrushV2.blend'),link=False) as(src,dst):dst.objects=[name for name in src.objects if name in ['DosaBrushV2_Handle','GripSocket','DosaBrushV2_Rig']]
    brush=[o for o in dst.objects if o]
    for o in brush:
        if not o.users_collection:bpy.context.scene.collection.objects.link(o)
    bpy.context.view_layer.update();grip=next(o for o in brush if o.name.startswith('GripSocket'));handle=next(o for o in brush if o.name.startswith('DosaBrushV2_Handle'))
    transform=grip.matrix_world.inverted()@handle.matrix_world;handle.data.calc_loop_triangles();shaft=BVHTree.FromPolygons([transform@v.co for v in handle.data.vertices],[tuple(t.vertices) for t in handle.data.loop_triangles],all_triangles=True)
    down=next(d for d in defs if d['id']=='grip_down');pose(down);base=points();initial=crossings(base)
    lock=np.zeros(n,dtype=bool)
    for side,sign in [('Right',-1),('Left',1)]:
        inv=(rig.matrix_world@rig.pose.bones[side+'BrushGrip'].matrix).inverted()
        for i in np.where(basis[:,0]*sign>.6)[0]:
            p=inv@Vector(base[i]);distance=shaft.find_nearest(p)[3]
            if abs(p.y)<.095 and distance<.002:lock[i]=True
    lock=np.array([lock[i] or any(lock[j] for j in neighbors[i]) for i in range(n)])
    dist=np.full(n,np.inf);queue=[]
    for i in set(int(i) for hit in initial for t in hit for i in tri[t]):dist[i]=0;heapq.heappush(queue,(0,i))
    while queue:
        d,i=heapq.heappop(queue)
        if d>dist[i] or d>.025:continue
        for j in neighbors[i]:
            nd=d+np.linalg.norm(original[i]-original[j])
            if nd<dist[j]:dist[j]=nd;heapq.heappush(queue,(nd,j))
    amount=np.clip(1-dist/.025,0,1);amount=amount*amount*(3-2*amount);amount[lock]=0;amount[np.abs(basis[:,0])<.65]=0
    bone_m={b.name:np.array(rig.matrix_world@rig.pose.bones[b.name].matrix@b.matrix_local.inverted()@rig.matrix_world.inverted()@hand.matrix_world) for b in rig.data.bones}
    M=np.array([sum((bone_m[hand.vertex_groups[g.group].name]*g.weight for g in v.groups),np.zeros((4,4))) for v in hand.data.vertices]);inverse=np.linalg.inv(M[:,:3,:3])
    edge=np.array([(i,j) for i,row in enumerate(neighbors) for j in row]);degree=np.bincount(edge[:,0],minlength=n)
    candidate=original.copy();samples=[];best=(len(initial),candidate.copy(),0,0.)
    for cap in [.002,.004]:
        posed=base.copy()
        for iteration in range(1,201):
            mean=np.zeros_like(posed);np.add.at(mean,edge[:,0],posed[edge[:,1]]);mean/=np.maximum(degree,1)[:,None]
            moved=posed+(mean-posed)*.4*amount[:,None]-base;length=np.linalg.norm(moved,axis=1);moved*=np.minimum(1,cap/np.maximum(length,1e-15))[:,None];posed=base+moved
            if iteration not in [10,25,50,100,200]:continue
            candidate=original+np.einsum('vij,vj->vi',inverse,posed-base);candidate[lock]=original[lock];set_shape(candidate);pose(down);hits=len(crossings(points()))
            samples.append({'cap':cap,'iterations':iteration,'crossings':hits})
            if hits<best[0]:best=(hits,candidate.copy(),iteration,cap)
            if hits==0:break
        if best[0]==0:break
    # A small surviving triangular fold can require separation rather than more
    # smoothing. Solve the real crossing half-spaces at several grip fractions.
    collision_trials=[];frame_data=[]
    for alpha in np.linspace(.05,1.,20):
        pose(down,alpha)
        bone_m={b.name:np.array(rig.matrix_world@rig.pose.bones[b.name].matrix@b.matrix_local.inverted()@rig.matrix_world.inverted()@hand.matrix_world) for b in rig.data.bones}
        frame=np.array([sum((bone_m[hand.vertex_groups[g.group].name]*g.weight for g in v.groups),np.zeros((4,4))) for v in hand.data.vertices])
        frame_data.append((alpha,frame,np.linalg.inv(frame[:,:3,:3])))
    def posed_points(rest,alpha,frame):
        return np.einsum('vij,vj->vi',frame,np.concatenate([basis+(rest-basis)*alpha,np.ones((n,1))],axis=1))[:,:3]
    def score(rest):return sum(len(crossings(posed_points(rest,a,m))) for a,m,inv in frame_data)
    candidate=best[1].copy();collision_best=(score(candidate),candidate.copy(),0)
    for iteration in range(1,121):
        if collision_best[0]==0:break
        for alpha,frame,inv in frame_data:
            p=posed_points(candidate,alpha,frame);hits=crossings(p)
            delta=np.zeros_like(p);counts=np.zeros(n)
            for a,b in hits:
                ia,ib=tri[a],tri[b];ta,tb=p[ia],p[ib];ea=np.roll(ta,-1,axis=0)-ta;eb=np.roll(tb,-1,axis=0)-tb
                axes=[np.cross(ea[0],ea[1]),np.cross(eb[0],eb[1])]+[np.cross(x,y) for x in ea for y in eb]
                choices=[]
                for axis in axes:
                    length=np.linalg.norm(axis)
                    if length<1e-12:continue
                    axis=axis/length;pa=ta@axis;pb=tb@axis
                    choices.extend([(pb.max()-pa.min()+.00008,axis),(pa.max()-pb.min()+.00008,-axis)])
                distance,direction=min((q for q in choices if q[0]>0),key=lambda q:q[0]);distance=min(distance,.001)
                free_a=ia[~lock[ia]];free_b=ib[~lock[ib]];sides=int(len(free_a)>0)+int(len(free_b)>0)
                if not sides:continue
                for ids,sign in [(free_a,1),(free_b,-1)]:
                    for i in ids:delta[i]+=direction*distance*sign/sides;counts[i]+=1
            use=counts>0;delta[use]/=counts[use,None]
            candidate+=np.einsum('vij,vj->vi',inv,delta)*.7/alpha
            disp=candidate-original;length=np.linalg.norm(disp,axis=1);disp*=np.minimum(1,.006/np.maximum(length,1e-15))[:,None]
            candidate=original+disp;candidate[lock]=original[lock]
        if iteration%5:continue
        value=score(candidate);collision_trials.append({'iterations':iteration,'crossingScore':value})
        if value<collision_best[0]:collision_best=(value,candidate.copy(),iteration)
    if collision_best[0]<score(best[1]):best=(best[0],collision_best[1],best[2],best[3])
    set_shape(best[1]);pose(down);best=(len(crossings(points())),best[1],best[2],best[3]);results=[]
    for d in defs:pose(d);results.append({'id':d['id'],'properCrossings':len(crossings(points()))})
    transition=[]
    for alpha in np.linspace(0,1,21):pose(down,float(alpha));transition.append({'alpha':float(alpha),'properCrossings':len(crossings(points()))})
    changed=np.linalg.norm(best[1]-original,axis=1)
    pose(down);remaining=crossings(points())
    remaining_details=[{'triangles':[int(a),int(b)],'vertexIndices':tri[[a,b]].tolist(),
        'locked':lock[tri[[a,b]]].tolist(),'basisPoints':basis[tri[[a,b]]].tolist()} for a,b in remaining]
    for o in brush:bpy.data.objects.remove(o,do_unlink=True)
    pose(next(d for d in defs if d['id']=='rest'))
    return {'method':'Contact-locked posed-space harmonic repair of existing corrective shapes only; no post-decimation Basis/weights/UV/topology change.',
            'initialGripCrossings':len(initial),'finalGripCrossings':best[0],'movedVertices':int(np.sum(changed>1e-8)),
            'maximumCorrectiveChangeM':float(changed.max()),'shaftLockedVertices':int(lock.sum()),'shaftLockedMaxChangeM':float(max(changed[lock],default=0)),
            'remainingDetails':remaining_details,
            'samples':samples,'collisionTrials':collision_trials,'poses':results,'transition':transition,'denseContactStatus':'PENDING','rigPass':False}
