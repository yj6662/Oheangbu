"""Reproducible derived Dosa body: preserved torso atlas, cuff-local hand retopology.
Run in the isolated Dosa_Player_Workshop. Original source objects are retained hidden.
"""
import bpy,bmesh,math,json,os,shutil
from mathutils import Vector,Matrix
from mathutils.bvhtree import BVHTree
from mathutils.geometry import barycentric_transform
from mathutils.kdtree import KDTree

ROOT='C:/Users/yj666/Oheangbu';OUT=ROOT+'/Art/Player'
s=bpy.context.scene
if bpy.context.object and bpy.context.object.mode!='OBJECT':bpy.ops.object.mode_set(mode='OBJECT')
for o in list(s.objects):
    if o.name.startswith(('Dosa_Rig','Dosa_Body','Dosa_Arms','Dosa_Hands')):bpy.data.objects.remove(o,do_unlink=True)
source_a=s.objects.get('Armature');source_m=s.objects.get('char1')
source_a.hide_set(False);source_m.hide_set(False)
a=source_a.copy();a.data=source_a.data.copy();a.name='Dosa_Rig';s.collection.objects.link(a)
a.hide_render=False;a.hide_set(False)
a.animation_data_clear()
for pb in a.pose.bones:pb.matrix_basis=Matrix.Identity(4)
world=a.matrix_world.copy();a.data.transform(world);a.matrix_world=Matrix.Identity(4)
m=source_m.copy();m.data=source_m.data.copy();m.name='Dosa_Body';s.collection.objects.link(m)
m.hide_render=False;m.hide_set(False)
mw=source_m.matrix_world.copy();m.parent=None;m.matrix_world=Matrix.Identity(4);m.data.transform(mw)
for mod in m.modifiers:
    if mod.type=='ARMATURE':mod.object=a;mod.use_deform_preserve_volume=False
for p in m.data.polygons:p.use_smooth=True
source_a.hide_render=True;source_m.hide_render=True;source_a.hide_set(True);source_m.hide_set(True)

# Keep a fully transformed source for nearest-surface atlas sampling before removing mittens/brush.
origverts=[v.co.copy() for v in m.data.vertices]
origweights=[{m.vertex_groups[g.group].name:g.weight for g in v.groups} for v in m.data.vertices]
m.data.calc_loop_triangles();srcuv=m.data.uv_layers.active.data
hand_bvhs={};hand_tris={}
for side in ('Left','Right'):
    tris=[t for t in m.data.loop_triangles if sum(origweights[i].get(side+'Hand',0) for i in t.vertices)>1.3]
    hand_tris[side]=[(tuple(t.vertices),tuple(srcuv[i].uv.copy() for i in t.loops)) for t in tris]
    hand_bvhs[side]=BVHTree.FromPolygons(origverts,[t.vertices for t in tris],all_triangles=True)

# Remove source fused fingers and fused right brush only; collar/sleeve vertices stay.
remove=[]
for v in m.data.vertices:
    ww=origweights[v.index]
    for side in ('Left','Right'):
        wrist=a.data.bones[side+'Hand'].head_local
        in_hand_region=(v.co.x>.185 and v.co.y<-.185 and v.co.z>.70) if side=='Left' else (v.co.x<-.253 and v.co.y<.105 and v.co.y>-.10 and v.co.z>.47)
        if ww.get(side+'Hand',0)>.45 and v.co.z<wrist.z+.004 and in_hand_region:
            remove.append(v.index);break
bm=bmesh.new();bm.from_mesh(m.data);bm.verts.ensure_lookup_table()
deletedset=set(remove);nearboundary=set()
for v in bm.verts:
    if v.index in deletedset:
        nearboundary.update(e.other_vert(v) for e in v.link_edges if e.other_vert(v).index not in deletedset)
bmesh.ops.delete(bm,geom=[bm.verts[i] for i in remove],context='VERTS')
cutedges=[e for e in bm.edges if e.is_boundary and all(v in nearboundary for v in e.verts)]
if cutedges:
    caps=bmesh.ops.holes_fill(bm,edges=cutedges,sides=0).get('faces',[])
    uvl=bm.loops.layers.uv.active
    for face in caps:
        for loop in face.loops:
            other=next((l for f in loop.vert.link_faces if f not in caps for l in f.loops if l.vert==loop.vert),None)
            if other:loop[uvl].uv=other[uvl].uv
    bmesh.ops.triangulate(bm,faces=caps)
bm.to_mesh(m.data);bm.free()

# Two local relax passes at the existing shoulders/elbows/knees. All source shapes/UVs remain.
adj=[set() for _ in m.data.vertices]
for e in m.data.edges:adj[e.vertices[0]].add(e.vertices[1]);adj[e.vertices[1]].add(e.vertices[0])
wg=[{g.group:g.weight for g in v.groups} for v in m.data.vertices]
joints=[(a.data.bones[n].head_local.copy(),rad) for n,rad in [('LeftArm',.13),('RightArm',.13),('LeftForeArm',.10),('RightForeArm',.10),('LeftLeg',.13),('RightLeg',.13)]]
selected=[i for i,v in enumerate(m.data.vertices) if any((v.co-p).length<rad for p,rad in joints)]
for _ in range(2):
    nextwg=[dict(x) for x in wg]
    for i in selected:
        ns=adj[i]
        if not ns:continue
        blend=dict((k,v*.72) for k,v in wg[i].items())
        for j in ns:
            for k,v in wg[j].items():blend[k]=blend.get(k,0)+v*.28/len(ns)
        top=sorted(blend.items(),key=lambda x:x[1],reverse=True)[:4];total=sum(v for k,v in top)
        nextwg[i]={k:v/total for k,v in top}
    wg=nextwg
for i in selected:
    for g in m.vertex_groups:g.remove([i])
    for k,v in wg[i].items():m.vertex_groups[k].add([i],v,'REPLACE')

V=[];F=[];UV=[];WG=[];spec={}
def sample_uv(side,p):
    hit=hand_bvhs[side].find_nearest(p)
    if hit[2] is None:return (.5,.5)
    ids,uvs=hand_tris[side][hit[2]]
    q=barycentric_transform(hit[0],*(origverts[i] for i in ids),*(Vector((u.x,u.y,0)) for u in uvs))
    return (q.x,q.y)
def vertex(p,side,weights):
    V.append(tuple(p));UV.append(sample_uv(side,p));WG.append(weights);return len(V)-1
def tube(side,points,radii,width_axis,dorsal_axis,weight_func,flatten=1.0):
    rings=[];total=len(points)
    for j,(p,rad) in enumerate(zip(points,radii)):
        ring=[]
        for k in range(12):
            ang=k*math.tau/12
            ring.append(vertex(p+width_axis*(math.cos(ang)*rad)+dorsal_axis*(math.sin(ang)*rad*flatten),side,weight_func(j/(total-1))))
        rings.append(ring)
    for j in range(total-1):
        for k in range(12):F.append((rings[j][k],rings[j][(k+1)%12],rings[j+1][(k+1)%12],rings[j+1][k]))
    F.append(tuple(reversed(rings[0])));F.append(tuple(rings[-1]))

bpy.ops.object.select_all(action='DESELECT');a.select_set(True);bpy.context.view_layer.objects.active=a;bpy.ops.object.mode_set(mode='EDIT')
for side in ('Right','Left'):
    wrist=a.data.edit_bones[side+'Hand'].head.copy()
    d=Vector((-.15,-.20,-.968) if side=='Right' else (-.36,-.24,-.9)).normalized()
    w=Vector((1,0,0) if side=='Right' else (-1,0,0));w=(w-d*w.dot(d)).normalized()
    n=d.cross(w).normalized()
    if n.y>0:n=-n
    palm_len=.080
    palm_points=[wrist+d*x for x in [-.018,0,.022,.045,.062,.073]]
    # Elliptical cross sections taper into the sleeve and rounded metacarpal mass.
    tube(side,palm_points,[.029,.028,.036,.038,.035,.026],w,n,lambda t:{side+'Hand':1},flatten=.49)
    finger_defs=[('Index',.022,.074,.0090),('Middle',.004,.082,.0097),('Ring',-.014,.074,.0090),('Pinky',-.030,.058,.0075)]
    specs=[]
    for finger,offset,length,rad in finger_defs:
        base=wrist+d*.073+w*offset
        fd=(d+w*(offset*.7)).normalized()
        joint_t=[0,.43,.76,1]
        bones=[]
        for j in range(3):
            bn=side+'Hand'+finger+str(j+1);b=a.data.edit_bones.new(bn)
            b.head=base+fd*length*joint_t[j];b.tail=base+fd*length*joint_t[j+1]
            b.parent=a.data.edit_bones[side+'Hand'] if j==0 else bones[-1];b.use_connect=j>0;b.align_roll(n);bones.append(b)
        points=[base+fd*length*(j/12) for j in range(13)]
        radii=[rad*(1-.40*(j/12))*(1+.08*math.cos(j/12*math.pi*6)) for j in range(13)];radii[-1]=.001
        def weights(t,names=[b.name for b in bones]):
            centers=[.18,.58,.88];dist=[max(0,1-abs(t-c)/.38) for c in centers]
            if t<.12:dist[0]+=1
            tot=sum(dist);return {bn:x/tot for bn,x in zip(names,dist) if x>0}
        tube(side,points,radii,w,n,weights,flatten=.88)
        # Rounded metacarpal heads form an arched knuckle line, removing a straight
        # slab-to-finger ledge. Their volume welds into the palm during local remesh.
        kc=base+n*.003
        kpts=[kc+fd*x for x in [-.013,-.008,0,.008,.015]]
        tube(side,kpts,[.006,.011,.012,.010,.006],w,n,lambda t,bn=bones[0].name:{side+'Hand':1-t*.55,bn:t*.55},.82)
        specs.append({'finger':finger,'base':list(base),'tip':list(points[-1]),'bones':[b.name for b in bones]})
    # Thumb opposes the fingers and has its own three-joint chain.
    base=wrist+d*.026+w*.023
    td=(d*.75+w*.68-n*.18).normalized();length=.065;joint_t=[0,.38,.72,1];bones=[]
    for j in range(3):
        b=a.data.edit_bones.new(side+'HandThumb'+str(j+1));b.head=base+td*length*joint_t[j];b.tail=base+td*length*joint_t[j+1]
        b.parent=a.data.edit_bones[side+'Hand'] if j==0 else bones[-1];b.use_connect=j>0;b.align_roll(n);bones.append(b)
    tw=td.cross(n).normalized();points=[base+td*length*(j/12) for j in range(13)];radii=[.013*(1-.47*j/12) for j in range(13)];radii[-1]=.001
    def thumbweights(t,names=[b.name for b in bones]):
        ds=[max(0,1-abs(t-c)/.40) for c in [.13,.54,.87]];tot=sum(ds);return {bn:x/tot for bn,x in zip(names,ds) if x>0}
    tube(side,points,radii,tw,n,thumbweights,.9)
    grip=a.data.edit_bones.new(side+'BrushGrip');grip.head=wrist+d*.087-n*.042;grip.tail=grip.head+w*.035
    grip.parent=a.data.edit_bones[side+'Hand'];grip.use_deform=False;grip.align_roll(n)
    # Socket local Y follows its bone. A natural writing orientation is calibrated in runtime.
    spec[side]={'wrist':list(wrist),'length_axis':list(d),'width_axis':list(w),'dorsal_axis':list(n),'grip_rest':list(grip.head),'fingers':specs}
bpy.ops.object.mode_set(mode='OBJECT')
mesh=bpy.data.meshes.new('Dosa_ArticulatedHands');mesh.from_pydata(V,[],F);mesh.update()
hm=bpy.data.objects.new('Dosa_Hands',mesh);s.collection.objects.link(hm);mesh.materials.append(m.data.materials[0])
uvlayer=mesh.uv_layers.new(name=m.data.uv_layers.active.name)
for poly in mesh.polygons:
    poly.use_smooth=True
    for li in poly.loop_indices:uvlayer.data[li].uv=UV[mesh.loops[li].vertex_index]
for i,weights in enumerate(WG):
    for name,value in weights.items():
        group=hm.vertex_groups.get(name) or hm.vertex_groups.new(name=name);group.add([i],value,'REPLACE')
# Weld the intersecting palm/finger webs into one smooth organic surface, then restore
# the source atlas UV and interpolated skin weights. This is local to the two new hands.
bpy.ops.object.select_all(action='DESELECT');hm.select_set(True);bpy.context.view_layer.objects.active=hm
kd=KDTree(len(V))
for i,v in enumerate(V):kd.insert(Vector(v),i)
kd.balance()
remesh=hm.modifiers.new('Hand_Webs','REMESH');remesh.mode='VOXEL';remesh.voxel_size=.0017;remesh.use_smooth_shade=True
bpy.ops.object.modifier_apply(modifier=remesh.name)
smooth=hm.modifiers.new('Hand_Soften','SMOOTH');smooth.factor=.55;smooth.iterations=3;bpy.ops.object.modifier_apply(modifier=smooth.name)
dec=hm.modifiers.new('Hand_SurfaceBudget','DECIMATE');dec.ratio=.30;bpy.ops.object.modifier_apply(modifier=dec.name)
mesh=hm.data
for group in list(hm.vertex_groups):hm.vertex_groups.remove(group)
for v in mesh.vertices:
    nearest=kd.find_n(v.co,4);weights={}
    for co,index,dist in nearest:
        for name,value in WG[index].items():weights[name]=weights.get(name,0)+value/max(dist,.0003)**2
    weights=sorted(weights.items(),key=lambda x:x[1],reverse=True)[:4];total=sum(x[1] for x in weights)
    for name,value in weights:
        group=hm.vertex_groups.get(name) or hm.vertex_groups.new(name=name);group.add([v.index],value/total,'REPLACE')
uvlayer=mesh.uv_layers.get(m.data.uv_layers.active.name) or mesh.uv_layers.new(name=m.data.uv_layers.active.name)
for poly in mesh.polygons:
    poly.use_smooth=True
    for li in poly.loop_indices:
        v=mesh.vertices[mesh.loops[li].vertex_index];side='Right' if v.co.x<0 else 'Left';uvlayer.data[li].uv=sample_uv(side,v.co)
mod=hm.modifiers.new('DosaSkin','ARMATURE');mod.object=a
# Source hand atlas islands are tiny. Reprojecting them per vertex interpolates across
# unrelated islands. Bake a continuous verified skin patch instead, preserving its
# weathered skin colour and fine mottling in an independent, seam-safe hand atlas.
handmat=bpy.data.materials.get('Dosa_Hands_Surface') or bpy.data.materials.new('Dosa_Hands_Surface');handmat.use_nodes=True
nodes=handmat.node_tree.nodes;nodes.clear();links=handmat.node_tree.links
outnode=nodes.new('ShaderNodeOutputMaterial');emit=nodes.new('ShaderNodeEmission');links.new(emit.outputs[0],outnode.inputs['Surface'])
srcmap=nodes.new('ShaderNodeUVMap');srcmap.uv_map='HandSourceUV'
srctex=nodes.new('ShaderNodeTexImage');srctex.image=bpy.data.images.get('T_DosaCourier_base_color.png');links.new(srcmap.outputs['UV'],srctex.inputs['Vector'])
links.new(srctex.outputs['Color'],emit.inputs['Color'])
sourceuv=mesh.uv_layers.get('HandSourceUV') or mesh.uv_layers.new(name='HandSourceUV')
for p in mesh.polygons:
    for li in p.loop_indices:
        v=mesh.vertices[mesh.loops[li].vertex_index];side='Right' if v.co.x<0 else 'Left';hs=spec[side]
        rel=v.co-Vector(hs['wrist']);x=rel.dot(Vector(hs['width_axis']));y=rel.dot(Vector(hs['length_axis']))
        sourceuv.data[li].uv=(.143+x*.12,.708+y*.065)
destuv=mesh.uv_layers.get('HandUV') or mesh.uv_layers.new(name='HandUV');mesh.uv_layers.active=destuv
bpy.context.view_layer.objects.active=hm
bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT');bpy.ops.uv.smart_project(angle_limit=1.25,island_margin=.018);bpy.ops.object.mode_set(mode='OBJECT')
hm.data.materials.clear();hm.data.materials.append(handmat)
bakeimg=bpy.data.images.get('T_DosaCourier_HandsBaseColor') or bpy.data.images.new('T_DosaCourier_HandsBaseColor',width=1024,height=1024,alpha=False)
target=nodes.new('ShaderNodeTexImage');target.image=bakeimg;nodes.active=target
s.render.engine='CYCLES';s.cycles.samples=1;s.render.bake.margin=8
bpy.ops.object.bake(type='EMIT')
bakeimg.filepath_raw=OUT+'/T_DosaCourier_HandsBaseColor.png';bakeimg.file_format='PNG';bakeimg.save()
nodes.remove(emit);bsdf=nodes.new('ShaderNodeBsdfPrincipled');bsdf.inputs['Roughness'].default_value=.78
links.new(target.outputs['Color'],bsdf.inputs['Base Color']);links.new(bsdf.outputs['BSDF'],outnode.inputs['Surface'])
# Consolidate active UV into the body's channel name; each submesh keeps its own texture.
bodyuv=m.data.uv_layers.active.name
for layername in [u.name for u in mesh.uv_layers]:
    if layername!='HandUV':mesh.uv_layers.remove(mesh.uv_layers[layername])
mesh.uv_layers['HandUV'].name=bodyuv;mesh.uv_layers.active=mesh.uv_layers[bodyuv];mesh.uv_layers[bodyuv].active_render=True
bpy.ops.object.select_all(action='DESELECT');m.select_set(True);hm.select_set(True);bpy.context.view_layer.objects.active=m;bpy.ops.object.join()
# Geometric normals are smoothed; no global subdivision that would melt cloth/hat silhouette.
for p in m.data.polygons:p.use_smooth=True
m.data.calc_loop_triangles()
report={'source_vertices':len(origverts),'removed_fused_hand_brush_vertices':len(remove),'smoothed_joint_vertices':len(selected),
        'new_hand_vertices':len(V),'final_vertices':len(m.data.vertices),'final_triangles':len(m.data.loop_triangles),
        'bone_count':len(a.data.bones),'finger_bones':[b.name for b in a.data.bones if any(x in b.name for x in ['Thumb','Index','Middle','Ring','Pinky'])],
        'hands':spec,'notes':['Original torso/hat/garment UV and geometry retained except cuff-local fused hands/brush removal.','Hands rebuilt as independent articulated palm/finger surfaces using nearest source hand atlas UV.','No Magica Cloth simulation authored by this asset script.']}
open(OUT+'/refinement-report.json','w',encoding='utf-8').write(json.dumps(report,indent=2))
bpy.ops.wm.save_as_mainfile(filepath=OUT+'/Dosa_Refined.blend')
print(json.dumps({k:v for k,v in report.items() if k not in ('hands','finger_bones','notes')}))
