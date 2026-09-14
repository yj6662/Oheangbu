"""Create articulated hand surfaces on the V2 rest rig, using only its Meshy UV source."""
import bpy,bmesh,json,math
from pathlib import Path
from mathutils import Vector,Matrix
from mathutils.bvhtree import BVHTree
from mathutils.kdtree import KDTree
from mathutils.geometry import barycentric_transform

ROOT=Path('C:/Users/yj666/Oheangbu'); OUT=ROOT/'Art/PlayerV2'
bpy.ops.wm.open_mainfile(filepath=str(OUT/'DosaV2_Parts.blend'))
s=bpy.context.scene; rig=bpy.data.objects['DosaV2_Rig']; source=bpy.data.objects['DosaV2_SourceSurface']
source.data.calc_loop_triangles()
srcverts=[v.co.copy() for v in source.data.vertices]
srcuv=source.data.uv_layers.active.data
surfaces={}; source_triangles={}
for side,sign in [('Left',1),('Right',-1)]:
    triangles=[t for t in source.data.loop_triangles if all(sign*srcverts[i].x>.650 for i in t.vertices)]
    source_triangles[side]=[(tuple(t.vertices),tuple(srcuv[i].uv.copy() for i in t.loops)) for t in triangles]
    surfaces[side]=BVHTree.FromPolygons(srcverts,[t.vertices for t in triangles],all_triangles=True)

V=[];F=[];W=[];sides=[]
def vertex(p,weights,side):
    V.append(tuple(p));W.append(weights);sides.append(side);return len(V)-1
def tube(side,points,radii,width,dorsal,weights,flatten=.9):
    rings=[]
    for j,(p,r) in enumerate(zip(points,radii)):
        ring=[]
        for k in range(16):
            a=k*math.tau/16
            ring.append(vertex(p+width*(r*math.cos(a))+dorsal*(r*flatten*math.sin(a)),weights(j/(len(points)-1)),side))
        rings.append(ring)
    for j in range(len(rings)-1):
        for k in range(16):F.append((rings[j][k],rings[j][(k+1)%16],rings[j+1][(k+1)%16],rings[j+1][k]))
    F.append(tuple(reversed(rings[0])));F.append(tuple(rings[-1]))

bpy.ops.object.select_all(action='DESELECT'); rig.select_set(True); bpy.context.view_layer.objects.active=rig
bpy.ops.object.mode_set(mode='EDIT')
hand_specs={}
for side,sign in [('Left',1),('Right',-1)]:
    wrist=rig.data.edit_bones[side+'Hand'].head.copy()
    direction=Vector((sign,0,0));width=Vector((0,-1,0));dorsal=Vector((0,0,1))
    tube(side,[wrist+direction*t for t in [-.015,0,.02,.04,.056,.070]],
         [.024,.025,.035,.037,.035,.026],width,dorsal,lambda t:{side+'Hand':1},.49)
    entries=[]
    for finger,offset,length,radius in [('Index',.023,.074,.0086),('Middle',.005,.083,.0091),
                                        ('Ring',-.013,.076,.0085),('Pinky',-.029,.061,.0072)]:
        base=wrist+direction*.061+width*offset
        fd=(direction+width*offset*.5).normalized()
        times=[0,.43,.76,1]; names=[]
        for j in range(3):
            name=side+'Hand'+finger+str(j+1); names.append(name)
            b=rig.data.edit_bones.new(name); b.head=base+fd*length*times[j];b.tail=base+fd*length*times[j+1]
            b.parent=rig.data.edit_bones[side+'Hand'] if j==0 else rig.data.edit_bones[names[j-1]]
            b.use_connect=j>0;b.align_roll(dorsal)
        def weights(t,names=names):
            values=[max(0,1-abs(t-c)/.38) for c in [.18,.58,.88]]
            if t<.12:values[0]+=1
            total=sum(values);return {name:v/total for name,v in zip(names,values) if v>0}
        points=[base+fd*length*j/16 for j in range(17)]
        radii=[radius*(1-.35*j/16)*(1+.10*math.cos(j/16*math.pi*6)) for j in range(17)]
        radii[-1]=.0012
        tube(side,points,radii,width,dorsal,weights,.84)
        knuckle=base+dorsal*.002
        tube(side,[knuckle+fd*t for t in [-.012,-.008,0,.008,.014]], [.005,.010,.011,.009,.004],
             width,dorsal,lambda t,bn=names[0]:{side+'Hand':1-.5*t,bn:.5*t},.84)
        entries.append({'finger':finger,'bones':names,'base':list(base),'tip':list(points[-1])})
    base=wrist+direction*.023+width*.025
    td=(direction*.53+width*.84-dorsal*.12).normalized();length=.065;times=[0,.38,.72,1];names=[]
    for j in range(3):
        name=side+'HandThumb'+str(j+1);names.append(name)
        b=rig.data.edit_bones.new(name);b.head=base+td*length*times[j];b.tail=base+td*length*times[j+1]
        b.parent=rig.data.edit_bones[side+'Hand'] if j==0 else rig.data.edit_bones[names[j-1]]
        b.use_connect=j>0;b.align_roll(dorsal)
    tw=td.cross(dorsal).normalized()
    def thumbweights(t,names=names):
        values=[max(0,1-abs(t-c)/.4) for c in [.13,.54,.87]]
        total=sum(values);return {name:v/total for name,v in zip(names,values) if v>0}
    radii=[.012*(1-.40*j/16) for j in range(17)];radii[-1]=.0012
    tube(side,[base+td*length*j/16 for j in range(17)],radii,tw,dorsal,thumbweights,.89)
    grip=rig.data.edit_bones.new(side+'BrushGrip')
    grip.head=wrist+direction*.077-dorsal*.034
    grip.tail=grip.head+width*.035;grip.parent=rig.data.edit_bones[side+'Hand'];grip.use_deform=False;grip.align_roll(dorsal)
    hand_specs[side]={'wrist':list(wrist),'dorsal':list(dorsal),'direction':list(direction),'fingers':entries,
                      'grip_rest':list(grip.head),'grip_pending_calibration':True}
bpy.ops.object.mode_set(mode='OBJECT')

mesh=bpy.data.meshes.new('DosaV2_HandTopology')
mesh.from_pydata(V,[],F);mesh.update()
hands=bpy.data.objects.new('DosaV2_Hands',mesh);s.collection.objects.link(hands)
mesh.materials.append(source.data.materials[0])
bpy.ops.object.select_all(action='DESELECT');hands.select_set(True);bpy.context.view_layer.objects.active=hands
kd=KDTree(len(V))
for i,p in enumerate(V):kd.insert(Vector(p),i)
kd.balance()
remesh=hands.modifiers.new('ConnectedFingerWebs','REMESH');remesh.mode='VOXEL';remesh.voxel_size=.00145
bpy.ops.object.modifier_apply(modifier=remesh.name)
smooth=hands.modifiers.new('OrganicHandSurface','SMOOTH');smooth.factor=.48;smooth.iterations=3
bpy.ops.object.modifier_apply(modifier=smooth.name)
dec=hands.modifiers.new('HandBudget','DECIMATE');dec.ratio=.20
bpy.ops.object.modifier_apply(modifier=dec.name)
mesh=hands.data
for v in mesh.vertices:
    values={}
    for co,index,distance in kd.find_n(v.co,4):
        for name,weight in W[index].items():values[name]=values.get(name,0)+weight/max(distance,.0002)**2
    values=sorted(values.items(),key=lambda item:item[1],reverse=True)[:4]
    total=sum(w for name,w in values)
    for name,w in values:
        group=hands.vertex_groups.get(name) or hands.vertex_groups.new(name=name)
        group.add([v.index],w/total,'REPLACE')
uv=mesh.uv_layers.new(name=source.data.uv_layers.active.name)
for p in mesh.polygons:
    p.use_smooth=True
    for li in p.loop_indices:
        v=mesh.vertices[mesh.loops[li].vertex_index];side='Left' if v.co.x>0 else 'Right'
        hit=surfaces[side].find_nearest(v.co)
        ids,uvs=source_triangles[side][hit[2]]
        q=barycentric_transform(hit[0],*(srcverts[i] for i in ids),*(Vector((u.x,u.y,0)) for u in uvs))
        uv.data[li].uv=(q.x,q.y)
bm=bmesh.new();bm.from_mesh(mesh);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(mesh);bm.free()
mod=hands.modifiers.new('DosaV2_Skin','ARMATURE');mod.object=rig;hands.parent=rig
hands.matrix_parent_inverse=Matrix.Identity(4);hands.matrix_basis=Matrix.Identity(4)
hands['surface_role']='articulated_hands'
mesh.calc_loop_triangles()
report={'status':'WIP_NOT_RIG_PASS','hands_triangles':len(mesh.loop_triangles),'hands_vertices':len(mesh.vertices),
        'bones':len(rig.data.bones),'source_uv':'Meshy7 character-01 only','hands':hand_specs,
        'remaining':['Skin/shaft contact calibration','Static opposing-thumb and wrist seam render inspection']}
(OUT/'Inspect/character-hands-wip.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
bpy.ops.file.pack_all();bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'DosaV2_Hands.blend'))
print(json.dumps({k:v for k,v in report.items() if k!='hands'}))
