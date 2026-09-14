"""Inner garment coverage, cloth mobility, sleeve helper bones and mesh budgets.
Produces a diagnostic candidate; no production animation is authored or sampled.
"""
import bpy,bmesh,json,math
from pathlib import Path
from mathutils import Vector,Matrix

ROOT=Path('C:/Users/yj666/Oheangbu');OUT=ROOT/'Art/PlayerV2'
bpy.ops.wm.open_mainfile(filepath=str(OUT/'DosaV2_Hands.blend'))
s=bpy.context.scene;rig=bpy.data.objects['DosaV2_Rig'];source=bpy.data.objects['DosaV2_SourceSurface']
parts=[o for o in s.objects if o.type=='MESH' and o!=source]
material=bpy.data.materials.new('DosaV2_InnerLinen');material.use_nodes=True
bsdf=material.node_tree.nodes.get('Principled BSDF');bsdf.inputs['Base Color'].default_value=(.035,.029,.024,1)
bsdf.inputs['Roughness'].default_value=.94

def select(obj):
    bpy.ops.object.select_all(action='DESELECT');obj.hide_set(False);obj.select_set(True);bpy.context.view_layer.objects.active=obj

def normalize_weights(obj):
    valid={g.index for g in obj.vertex_groups if g.name in rig.data.bones and rig.data.bones[g.name].use_deform}
    for v in obj.data.vertices:
        values=sorted([(g.group,g.weight) for g in v.groups if g.group in valid and g.weight>0],key=lambda t:t[1],reverse=True)[:4]
        for g in obj.vertex_groups:g.remove([v.index])
        total=sum(w for g,w in values)
        if not total: values=[(obj.vertex_groups.get('Hips').index,1)];total=1
        for index,w in values:obj.vertex_groups[index].add([v.index],w/total,'REPLACE')

for obj in parts:
    name=obj.name.removeprefix('DosaV2_')
    obj.data.calc_loop_triangles();triangles=len(obj.data.loop_triangles)
    target=triangles
    if name=='BodyCore':target=12500
    elif name=='Hat':target=4500
    elif name=='Hair':target=2600
    elif name.startswith('Waist'):target=900
    elif name.startswith('Robe_Front'):target=2200
    elif name.startswith('Robe_Back'):target=1700
    elif name.startswith('Robe_Side'):target=700
    elif name.startswith('SleeveOuter'):target=1800
    if triangles>target:
        select(obj);mod=obj.modifiers.new('GameSurfaceBudget','DECIMATE');mod.ratio=target/triangles
        # Put topology processing before the skin modifier and keep source UV data.
        bpy.ops.object.modifier_move_up(modifier=mod.name)
        bpy.ops.object.modifier_apply(modifier=mod.name)
    normalize_weights(obj)
    if name.startswith('Waist'):
        bm=bmesh.new();bm.from_mesh(obj.data)
        boundary=[e for e in bm.edges if e.is_boundary]
        if boundary:
            cap=bmesh.ops.holes_fill(bm,edges=boundary,sides=0).get('faces',[])
            material_index=len(obj.data.materials);obj.data.materials.append(material)
            for f in cap:f.material_index=material_index
            bmesh.ops.triangulate(bm,faces=cap)
        bm.to_mesh(obj.data);bm.free()

# Dedicated sleeve hem chains share authored rest positions in world and near rigs.
# World Cloth owns the cloth deformation. Only the near rig drives these bones.
select(rig);bpy.ops.object.mode_set(mode='EDIT')
for label,side,sign in [('L','Left',1),('R','Right',-1)]:
    for i in range(3):
        b=rig.data.edit_bones.new('J_SleeveHem_'+label+'_'+str(i))
        b.head=(sign*.352,0,1.32-i*.055);b.tail=(sign*.352,0,1.32-(i+1)*.055)
        b.parent=rig.data.edit_bones[side+'Arm'] if i==0 else rig.data.edit_bones['J_SleeveHem_'+label+'_'+str(i-1)]
        b.align_roll(Vector((0,-1,0)))
bpy.ops.object.mode_set(mode='OBJECT')

cloth=[]
for obj in parts:
    name=obj.name.removeprefix('DosaV2_')
    if not name.startswith(('Robe_','SleeveOuter_')):continue
    mesh=obj.data
    colors=mesh.color_attributes.new(name='ClothMobility',type='FLOAT_COLOR',domain='POINT')
    pinned=0;maximum=0
    for v in mesh.vertices:
        p=v.co
        if name.startswith('Robe_'):
            mobility=0 if p.z>=.918 else min(.28,max(0,(.918-p.z)*.53))
        else:
            mobility=0 if abs(p.x)<.22 or p.z>=1.315 else min(.15,max(0,(1.315-p.z)*.85))
            label='L' if p.x>0 else 'R'
            if mobility>0:
                fraction=min(1,mobility/.14)*.80
                t=max(0,min(2.99,(1.32-p.z)/.055))
                lo=min(2,int(t));hi=min(2,lo+1);blend=t-lo
                values={obj.vertex_groups[g.group].name:g.weight*(1-fraction) for g in v.groups}
                for index,w in [(lo,1-blend),(hi,blend)]:
                    bn='J_SleeveHem_'+label+'_'+str(index);values[bn]=values.get(bn,0)+w*fraction
                for g in obj.vertex_groups:g.remove([v.index])
                ordered=sorted(values.items(),key=lambda item:item[1],reverse=True)[:4];total=sum(w for bn,w in ordered)
                for bn,w in ordered:
                    group=obj.vertex_groups.get(bn) or obj.vertex_groups.new(name=bn);group.add([v.index],w/total,'REPLACE')
        colors.data[v.index].color=(mobility,0,0,1)
        pinned+=mobility==0;maximum=max(maximum,mobility)
    cloth.append({'renderer':obj.name,'vertices':len(mesh.vertices),'pinned':pinned,'max_distance_m':maximum,
                  'coefficient_source':'ClothMobility vertex color red in metres; exact zero is fixed seam'})

def nearest_weights(p,names):
    pairs=[]
    for name in names:
        b=rig.data.bones[name];line=b.tail_local-b.head_local
        t=max(0,min(1,(p-b.head_local).dot(line)/line.length_squared))
        pairs.append((name,(p-b.head_local-line*t).length))
    pairs.sort(key=lambda x:x[1]);best=pairs[0][1]
    ww=[(n,math.exp(-((d-best)/.07)**2*5)) for n,d in pairs[:4]];total=sum(w for n,w in ww)
    return {n:w/total for n,w in ww}

def lining_tube(name,points,radii,names,ellipticity=1):
    vertices=[];faces=[];weights=[];segments=24
    for j,p in enumerate(points):
        p=Vector(p)
        tangent=Vector(points[min(j+1,len(points)-1)])-Vector(points[max(0,j-1)])
        tangent.normalize();a=tangent.cross(Vector((0,1,0))).normalized();b=tangent.cross(a).normalized()
        for i in range(segments):
            angle=math.tau*i/segments
            q=p+a*(math.cos(angle)*radii[j])+b*(math.sin(angle)*radii[j]*ellipticity)
            vertices.append(q);weights.append(nearest_weights(q,names))
    for j in range(len(points)-1):
        for i in range(segments):
            a=j*segments+i;b=j*segments+(i+1)%segments
            faces.append((a,b,b+segments,a+segments))
    faces.append(tuple(reversed(range(segments))));faces.append(tuple((len(points)-1)*segments+i for i in range(segments)))
    mesh=bpy.data.meshes.new(name);mesh.from_pydata(vertices,[],faces);mesh.update();mesh.materials.append(material)
    bm=bmesh.new();bm.from_mesh(mesh);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(mesh);bm.free()
    obj=bpy.data.objects.new(name,mesh);s.collection.objects.link(obj)
    for p in mesh.polygons:p.use_smooth=True
    for index,ww in enumerate(weights):
        for bn,w in ww.items():
            g=obj.vertex_groups.get(bn) or obj.vertex_groups.new(name=bn);g.add([index],w,'REPLACE')
    obj.parent=rig;obj.matrix_parent_inverse=Matrix.Identity(4);obj.matrix_basis=Matrix.Identity(4)
    mod=obj.modifiers.new('DosaV2_Skin','ARMATURE');mod.object=rig;obj['surface_role']='inner_coverage'
    parts.append(obj);return obj

lining_tube('DosaV2_BodyLining',[(0,0,.81),(0,0,.90),(0,0,1.03),(0,0,1.15),(0,0,1.27),(0,0,1.36),(0,0,1.427)],
            [.12,.13,.11,.12,.14,.14,.06],['Hips','Spine','Spine01','Spine02','Neck'],.67)
for side,sign in [('Left',1),('Right',-1)]:
    lining_tube('DosaV2_ArmLining_'+side,[(sign*x,0,z) for x,z in [(.13,1.36),(.22,1.35),(.32,1.344),(.442,1.336),(.53,1.332),(.61,1.329),(.676,1.327)]],
        [.053,.051,.045,.039,.034,.029,.023],[side+'Shoulder',side+'Arm',side+'ForeArm',side+'Hand'],.94)
    lining_tube('DosaV2_LegLining_'+side,[(sign*.096,.008,z) for z in [.88,.78,.65,.485,.36,.22,.10]],
        [.072,.073,.066,.055,.051,.041,.030],['Hips',side+'UpLeg',side+'Leg',side+'Foot'],.94)

report={'status':'WIP_NOT_RIG_PASS','production_actions':0,'cloth':cloth,'parts':[],
        'remaining':['Inspect and repair semantic cut boundaries in static extremes.',
        'Calibrate hand/shaft contact.','Add separately generated backpack.',
        'Unity native Cloth stability and roundtrip verification.']}
for obj in parts:
    obj.data.calc_loop_triangles();report['parts'].append({'name':obj.name,'triangles':len(obj.data.loop_triangles),
        'vertices':len(obj.data.vertices),'materials':[m.name for m in obj.data.materials]})
report['triangles_without_backpack']=sum(x['triangles'] for x in report['parts'])
(OUT/'Inspect/character-surfaces-wip.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
bpy.ops.file.pack_all();bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'DosaV2_Surfaces.blend'))
print(json.dumps({k:v for k,v in report.items() if k!='parts'}))
