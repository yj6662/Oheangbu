"""Background Blender authoring: jointed granite blocks, near/far meshes, no texture synthesis."""
import bpy, bmesh, random, math, json
from pathlib import Path
from mathutils import Vector
from mathutils import noise

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Art/World/Compact/Rebuild/Rock275'
OUT.mkdir(parents=True,exist_ok=True)
bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)

def block(seed, center, size):
    rng=random.Random(seed);bm=bmesh.new()
    # Irregular convex core cut by fracture planes; avoid six orthogonal slab faces.
    bmesh.ops.create_icosphere(bm,subdivisions=2,radius=.65)
    for v in bm.verts:
        v.co.x+=v.co.z*.14+rng.uniform(-.10,.10)
        v.co.y+=v.co.z*.09+rng.uniform(-.09,.09)
        v.co.z+=rng.uniform(-.12,.12)
    for direction in [(1,1,.7),(-1,1,.9),(1,-1,-.6),(-1,-1,.7),(0,1,1.5)]:
        n=Vector(direction).normalized()
        bmesh.ops.bisect_plane(bm,geom=list(bm.verts)+list(bm.edges)+list(bm.faces),dist=.00001,
            plane_co=n*rng.uniform(.44,.56),plane_no=n,clear_outer=True)
        boundary=[e for e in bm.edges if e.is_boundary]
        if boundary:bmesh.ops.holes_fill(bm,edges=boundary,sides=0)
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
    bmesh.ops.bevel(bm,geom=list(bm.edges),offset=.022,segments=2,affect='EDGES')
    bmesh.ops.triangulate(bm,faces=list(bm.faces))
    bmesh.ops.subdivide_edges(bm,edges=list(bm.edges),cuts=2,use_grid_fill=True)
    bm.normal_update()
    for v in bm.verts:
        p=v.co.copy();normal=v.normal.copy()
        # Several scales of eroded grain; thin recessed seams cross the larger planar faces.
        fine=noise.fractal(p*13+Vector((seed,0,1)),1.1,2,3)*.008
        broad=noise.noise_vector(p*4+Vector((seed,1,0)))[0]*.062
        seam=abs(math.sin((p.z+p.x*.16)*15+seed))
        chip=.022*max(0,1-seam/.12)
        v.co+=normal*(fine+broad-chip)
        v.co=Vector((v.co.x*size[0]+center[0],v.co.y*size[1]+center[1],v.co.z*size[2]+center[2]))
    bmesh.ops.triangulate(bm,faces=list(bm.faces));bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
    mesh=bpy.data.meshes.new('joint');bm.to_mesh(mesh);bm.free()
    obj=bpy.data.objects.new('joint',mesh);bpy.context.collection.objects.link(obj)
    return obj

def make(variant):
    if variant==0:
        specs=[((-.15,0,.39),(.70,.92,.80)),((.32,-.04,.31),(.40,.76,.63)),((-.28,-.36,.16),(.40,.33,.35)),((.22,.36,.17),(.37,.33,.38))]
    elif variant==1:
        specs=[((0,0,.28),(.90,.76,.58)),((-.25,.20,.15),(.46,.60,.33)),((.36,-.25,.11),(.30,.40,.24))]
    else:
        specs=[((-.30,.02,.55),(.41,.80,1.14)),((.06,.12,.66),(.48,.69,1.33)),((.36,-.05,.40),(.37,.86,.84)),((-.06,-.30,.27),(.68,.42,.60)),((.18,.32,.24),(.56,.48,.52))]
    pieces=[block(275+variant*37+i*13,c,s) for i,(c,s) in enumerate(specs)]
    bpy.ops.object.select_all(action='DESELECT')
    for o in pieces:o.select_set(True)
    bpy.context.view_layer.objects.active=pieces[0];bpy.ops.object.join();obj=pieces[0];obj.name=f'JointedGranite{variant}'
    lo=Vector(tuple(min(v.co[i] for v in obj.data.vertices) for i in range(3)))
    hi=Vector(tuple(max(v.co[i] for v in obj.data.vertices) for i in range(3)))
    for v in obj.data.vertices:v.co=Vector(((v.co.x-lo.x)/(hi.x-lo.x)-.5,(v.co.y-lo.y)/(hi.y-lo.y)-.5,(v.co.z-lo.z)/(hi.z-lo.z)))
    for f in obj.data.polygons:f.use_smooth=True
    obj.data.set_sharp_from_angle(angle=.53)
    return obj

def export(obj,name):
    mesh=obj.data
    # Edge-collapse optimization may move extrema; keep both LODs within the authored envelope.
    for v in mesh.vertices:
        v.co.x=max(-.5,min(.5,v.co.x));v.co.y=max(-.5,min(.5,v.co.y));v.co.z=max(0,min(1,v.co.z))
    mesh.update();mesh.calc_loop_triangles();vertices=[];normals=[];uv=[];triangles=[]
    for tri in mesh.loop_triangles:
        offset=len(vertices)
        for li in tri.loops:
            p=mesh.vertices[mesh.loops[li].vertex_index].co;n=mesh.corner_normals[li].vector
            vertices.append({'x':p.x,'y':p.z,'z':-p.y});normals.append({'x':n.x,'y':n.z,'z':-n.y})
            uv.append({'x':p.x*2,'y':p.z*2})
        # The axis transform is a proper rotation; preserve outward triangle winding.
        triangles.extend([offset,offset+1,offset+2])
    (OUT/(name+'.json')).write_text(json.dumps(dict(vertices=vertices,normals=normals,uv=uv,triangles=triangles)),encoding='utf-8')
    return len(triangles)//3

receipt=[]
for i in range(3):
    o=make(i)
    bpy.context.view_layer.objects.active=o
    o.data.calc_loop_triangles()
    reduction=o.modifiers.new('Keep fracture silhouette','DECIMATE');reduction.ratio=min(1,(1400 if i==1 else 4300 if i==2 else 3500)/len(o.data.loop_triangles))
    bpy.ops.object.modifier_apply(modifier=reduction.name)
    near=export(o,f'granite-{i}-near')
    far=o.copy();far.data=o.data.copy();bpy.context.collection.objects.link(far);far.name=o.name+'_LOD1';bpy.context.view_layer.objects.active=far
    mod=far.modifiers.new('Collapse small weathering only','DECIMATE');mod.ratio=.22
    bpy.ops.object.modifier_apply(modifier=mod.name)
    distant=export(far,f'granite-{i}-far');far.hide_render=True;far.hide_viewport=True
    o.location.x=i*1.5
    receipt.append({'variant':i,'near_triangles':near,'far_triangles':distant})
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'JointedGranite275.blend'))
bpy.ops.object.select_all(action='DESELECT')
for o in bpy.context.scene.objects:
    if o.type=='MESH' and '_LOD1' not in o.name:o.select_set(True)
bpy.ops.export_scene.fbx(filepath=str(OUT/'JointedGranite275.fbx'),use_selection=True,object_types={'MESH'},add_leaf_bones=False,axis_forward='-Z',axis_up='Y')
(OUT/'blender-build.json').write_text(json.dumps(receipt,indent=2),encoding='utf-8')
print(receipt)
