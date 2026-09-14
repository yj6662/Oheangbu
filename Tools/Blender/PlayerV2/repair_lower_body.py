"""Lower-body-only repair branch. Reads Refined, never writes that source file or animations."""
import bpy,bmesh,json,math,hashlib,argparse,sys
import numpy as np
from pathlib import Path
from mathutils import Vector,Matrix
from mathutils.kdtree import KDTree
ROOT=Path(__file__).resolve().parents[3];ART=ROOT/'Art/PlayerV2'
SOURCE=ART/'DosaV2_Refined.blend';OUT=ART/'Inspect/LowerRepair';OUT.mkdir(parents=True,exist_ok=True)

def digest(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def aim(o,target):o.rotation_euler=(Vector(target)-o.location).to_track_quat('-Z','Y').to_euler()
def smooth(a,b,x):
    t=max(0,min(1,(x-a)/(b-a)));return t*t*(3-2*t)

def assign(obj,index,values):
    for g in obj.vertex_groups:g.remove([index])
    ww=sorted(((n,w) for n,w in values.items() if w>1e-7),key=lambda t:-t[1])[:4]
    total=sum(w for n,w in ww)
    for n,w in ww:
        g=obj.vertex_groups.get(n) or obj.vertex_groups.new(name=n);g.add([index],w/total,'REPLACE')

def lower_weights(p):
    """A continuous pelvis/left/right field, never an x-sign nearest-bone switch."""
    x,y,z=p
    if z>.88:
        # Match the original untouched torso field exactly at the upper repair boundary.
        rig=bpy.data.objects['DosaV2_Rig'];pairs=[]
        for name in ['Hips','Spine','Spine01','Spine02','Neck']:
            bone=rig.data.bones[name];a=bone.head_local;line=bone.tail_local-a
            t=max(0,min(1,(Vector(p)-a).dot(line)/line.length_squared))
            pairs.append((name,(Vector(p)-a-line*t).length))
        pairs.sort(key=lambda e:e[1]);nearest=pairs[0][1]
        ww=[(name,math.exp(-((distance-nearest)/.065)**2*6)) for name,distance in pairs[:4]]
        ww=[(name,w) for name,w in ww if w>.004];total=sum(w for name,w in ww)
        return {name:w/total for name,w in ww}
    side=smooth(-.070,.070,x)
    hips=smooth(.66,.89,z)
    center=(1-smooth(.018,.075,abs(x)))*smooth(.46,.67,z)
    hips=1-(1-hips)*(1-.92*center)
    knee=smooth(.40,.59,z)
    ankle=smooth(.075,.145,z)
    toes=(1-smooth(.025,.07,z))*smooth(.08,.15,-y)
    result={'Hips':hips}
    for name,s in [('Left',side),('Right',1-side)]:
        s*=1-hips
        result[name+'UpLeg']=s*knee
        result[name+'Leg']=s*(1-knee)*ankle
        result[name+'Foot']=s*(1-knee)*(1-ankle)*(1-toes)
        result[name+'ToeBase']=s*(1-knee)*(1-ankle)*toes
    return result

def independent_leg_weights(p,side):
    # Separate trouser legs have no sewn center-crotch vertices; do not import the opposite leg
    # or a position-dependent pelvis weight on their inner circumferences.
    z=p.z;hip=smooth(.77,.915,z);thigh=smooth(.38,.60,z)
    return {'Hips':hip,side+'UpLeg':(1-hip)*thigh,side+'Leg':(1-hip)*(1-thigh)}

def set_pose(rig,name):
    for p in rig.pose.bones:p.matrix_basis=Matrix.Identity(4)
    bpy.context.view_layer.update()
    def bone(bn,direction):
        p=rig.pose.bones[bn];m=p.matrix.copy();q=m.to_quaternion()
        change=((q@Vector((0,1,0))).rotation_difference(Vector(direction).normalized())@q).to_matrix().to_4x4()
        change.translation=m.translation;p.matrix=change;bpy.context.view_layer.update()
    if name=='rest':return
    for side,sign in [('Left',1),('Right',-1)]:bone(side+'Arm',(sign*.08,0,-1))
    if name=='step':
        bone('LeftUpLeg',(0,-.7,-.714));bone('LeftLeg',(0,.2,-.98));bone('LeftFoot',(0,-.86,-.51))
    if name=='squat':
        p=rig.pose.bones['Hips'];m=p.matrix.copy();m.translation+=Vector((0,0,-.2));p.matrix=m;bpy.context.view_layer.update()
        for side,sign in [('Left',1),('Right',-1)]:
            bone(side+'UpLeg',(sign*.1,-.68,-.73));bone(side+'Leg',(0,.68,-.73));bone(side+'Foot',(0,-.86,-.51))

def mesh_hash(obj,upper_only=False):
    h=hashlib.sha256()
    for v in obj.data.vertices:
        if upper_only and v.co.z<1.10:continue
        h.update(np.asarray(v.co,dtype=np.float32).tobytes())
        for g in v.groups:h.update((obj.vertex_groups[g.group].name+':'+format(g.weight,'.8f')).encode())
    return h.hexdigest()

def make_part(name,records,rig,original=None,cloth=False):
    # Record each corner's source UV; merge only exact source positions, never flatten UV seams.
    coords=[];faces=[];uvs=[];weights=[];lookup={};materials=[];mat_ids=[]
    for source,polygon in records:
        face=[];uv=[]
        for vi,li in zip(polygon.vertices,polygon.loop_indices):
            vertex=source.data.vertices[vi];co=vertex.co.copy()
            key=tuple(round(v,7) for v in co)
            if key not in lookup:
                lookup[key]=len(coords);coords.append(co)
                weights.append({source.vertex_groups[g.group].name:g.weight for g in vertex.groups})
            face.append(lookup[key]);uv.append(source.data.uv_layers.active.data[li].uv.copy())
        if len(set(face))<3:continue
        faces.append(face);uvs.append(uv)
        mat=source.data.materials[polygon.material_index]
        if mat not in materials:materials.append(mat)
        mat_ids.append(materials.index(mat))
    mesh=bpy.data.meshes.new(name+'_LowerSemanticMesh');mesh.from_pydata(coords,[],faces);mesh.update()
    for mat in materials:mesh.materials.append(mat)
    uv=mesh.uv_layers.new(name='UVMap')
    for p,face_uv,mat in zip(mesh.polygons,uvs,mat_ids):
        p.material_index=mat;p.use_smooth=True
        for li,value in zip(p.loop_indices,face_uv):uv.data[li].uv=value
    if original:
        obj=original;obj.data=mesh
    else:
        obj=bpy.data.objects.new(name,mesh);bpy.context.scene.collection.objects.link(obj)
        obj.parent=rig;obj.matrix_basis=Matrix.Identity(4);obj.matrix_parent_inverse=Matrix.Identity(4)
        mod=obj.modifiers.new('DosaV2_Skin','ARMATURE');mod.object=rig
    obj.vertex_groups.clear()
    for bone in rig.data.bones:obj.vertex_groups.new(name=bone.name)
    mobility=[]
    for index,w in enumerate(weights):
        v=mesh.vertices[index]
        if cloth:
            # Static target follows the hip with light thigh participation. Native Cloth owns flap motion.
            # Reclassified cloth is now a real free outer layer with an independent trouser under-shell.
            fraction=.32*(1-smooth(.58,.9,v.co.z))
            lw=lower_weights(v.co);w={n:t*fraction for n,t in lw.items()};w['Hips']=w.get('Hips',0)+1-fraction
            if v.co.z>.88:w=lw
            distance=0 if v.co.z>=.907 else min(.24,max(0,(.907-v.co.z)*.48))
            mobility.append(distance)
        elif 'TrousersSource' in name and v.co.z<.5:
            w=independent_leg_weights(v.co,'Left' if v.co.x>=0 else 'Right')
        elif .32<v.co.z<.99:w=lower_weights(v.co)
        assign(obj,index,w)
    if cloth:
        colors=mesh.color_attributes.new(name='ClothMobility',type='FLOAT_COLOR',domain='POINT')
        for index,distance in enumerate(mobility):colors.data[index].color=(distance,0,0,1)
    obj['surface_role']='outer_robe_cloth' if cloth else ('authored_trouser_source' if 'Trousers' in name else 'body_core')
    return obj

def semantic_repair(rig):
    sources=[o for o in bpy.context.scene.objects if o.name=='DosaV2_BodyCore' or o.name.startswith('DosaV2_Robe_')]
    colors=json.loads((OUT/'lower-colors.json').read_text())
    # Atlas classification is regularized over actual triangle neighbors and original cut boundaries.
    records=[];points=[];initial=[];edge_faces={};vertex_keys={}
    keep=[];fixed_robe={o.name:[] for o in sources if 'Robe_' in o.name}
    for obj in sources:
        for p in obj.data.polygons:
            c=p.center
            if not (.305<c.z<.915):
                (fixed_robe[obj.name] if 'Robe_' in obj.name else keep).append((obj,p));continue
            index=len(records);records.append((obj,p));points.append(c.copy())
            lum=colors[obj.name]['luminance'][p.index]
            cx=.105 if c.x>=0 else -.105
            # Geometric depth distinguishes dark shadows in the outer robe from actual inner pants.
            ellipse=math.sqrt(((c.x-cx)/.116)**2+((c.y-.004)/.143)**2)
            prob=max(0,min(1,(.235-lum)/.11))*max(0,min(1,(1.50-ellipse)/.55))
            if c.z<.38 and abs(c.x)>.035:prob=max(prob,.92 if lum<.22 else prob)
            if c.z>.87:prob*=.15
            initial.append(prob)
            keys=[]
            for vi in p.vertices:
                q=tuple(round(a,5) for a in obj.data.vertices[vi].co);keys.append(q)
            for a,b in zip(keys,keys[1:]+keys[:1]):edge_faces.setdefault(tuple(sorted((a,b))),[]).append(index)
    neighbors=[set() for _ in records]
    for linked in edge_faces.values():
        for a in linked:
            for b in linked:
                if a!=b:neighbors[a].add(b)
    values=np.array(initial)
    for _ in range(20):
        values=np.array([.52*initial[i]+.48*(sum(values[j] for j in neighbors[i])/len(neighbors[i]) if neighbors[i] else values[i]) for i in range(len(values))])
    labels=values>.49
    # Keep tiny classification islands with their coherent neighboring garment.
    visited=set()
    for start in range(len(labels)):
        if start in visited:continue
        component=[];stack=[start];visited.add(start)
        while stack:
            i=stack.pop();component.append(i)
            for j in neighbors[i]:
                if j not in visited and labels[j]==labels[start]:visited.add(j);stack.append(j)
        if len(component)<7:
            boundary=[j for i in component for j in neighbors[i] if j not in component]
            if boundary and sum(labels[j] for j in boundary)/len(boundary)>.7:labels[component]=True
            elif boundary and sum(labels[j] for j in boundary)/len(boundary)<.3:labels[component]=False
    pants=[];robes={k:list(v) for k,v in fixed_robe.items()};transfers={}
    for (obj,p),c,ispants in zip(records,points,labels):
        if ispants:target='DosaV2_TrousersSource';pants.append((obj,p))
        else:
            direction='Front' if c.y<-.037 else ('Back' if c.y>.037 else 'Side')
            target='DosaV2_Robe_'+direction+'_'+('L' if c.x>0 else 'R');robes[target].append((obj,p))
        key=obj.name+' -> '+target;transfers[key]=transfers.get(key,0)+1
    # Preserve source references until every output mesh has been copied.
    oldcopies=[]
    for obj in sources:
        copy=obj.copy();copy.data=obj.data.copy();oldcopies.append(copy)
    remap={a.name:b for a,b in zip(sources,oldcopies)}
    def copied(rr):return [(remap[o.name],remap[o.name].data.polygons[p.index]) for o,p in rr]
    # The original scan has no closed thighs under the coat. Retain its authored cuff wrinkles;
    # scattered thigh islands are replaced by a complete garment rather than exposed as patches.
    retained_pants=[r for r in pants if r[1].center.z<.372 or r[1].center.z>.858]
    replaced_pants=len(pants)-len(retained_pants)
    saved_keep=copied(keep);saved_pants=copied(retained_pants);saved_robes={name:copied(rr) for name,rr in robes.items()}
    core=bpy.data.objects['DosaV2_BodyCore']
    make_part(core.name,saved_keep,rig,core)
    trouser=make_part('DosaV2_TrousersSource',saved_pants,rig)
    for name,rr in saved_robes.items():make_part(name,rr,rig,bpy.data.objects[name],True)
    for copy in oldcopies:bpy.data.objects.remove(copy)
    return {'face_transfers':transfers,'trouser_source_faces_classified':len(pants),'source_cuff_faces_retained':len(retained_pants),
            'incomplete_thigh_faces_replaced_by_closed_shell':replaced_pants,'cloth_faces':{k:len(v) for k,v in robes.items()}}

def make_trouser_shell(rig):
    """Closed, folded trousers under the incomplete single-surface scan; no visible shin cylinders."""
    material=bpy.data.materials.new('DosaV2_TrouserUndercloth');material.use_nodes=True
    nodes=material.node_tree.nodes;links=material.node_tree.links;bsdf=nodes.get('Principled BSDF')
    bsdf.inputs['Base Color'].default_value=(.024,.020,.017,1);bsdf.inputs['Roughness'].default_value=.94
    # This authored cloth remains a node material in the repair source. Export must bake it to maps.
    tex=nodes.new('ShaderNodeTexNoise');tex.inputs['Scale'].default_value=150;tex.inputs['Detail'].default_value=2
    ramp=nodes.new('ShaderNodeValToRGB');ramp.color_ramp.elements[0].color=(.005,.004,.0032,1);ramp.color_ramp.elements[1].color=(.026,.022,.018,1)
    links.new(tex.outputs['Fac'],ramp.inputs['Fac']);links.new(ramp.outputs['Color'],bsdf.inputs['Base Color'])
    wave=nodes.new('ShaderNodeTexWave');wave.wave_type='BANDS';wave.bands_direction='DIAGONAL';wave.inputs['Scale'].default_value=170
    wave.inputs['Distortion'].default_value=2;wave.inputs['Detail Scale'].default_value=3
    bump=nodes.new('ShaderNodeBump');bump.inputs['Strength'].default_value=.11;bump.inputs['Distance'].default_value=.00030
    # Fine aperiodic fiber breakup avoids visible, aliasing-prone stripes on a bent knee.
    links.new(tex.outputs['Fac'],bump.inputs['Height']);links.new(bump.outputs['Normal'],bsdf.inputs['Normal'])
    vertices=[];faces=[];uvfaces=[];segments=64
    zs=np.linspace(.305,.91,35)
    profile_z=[.305,.35,.42,.52,.64,.76,.86,.91]
    rx=[.067,.090,.093,.094,.094,.085,.082,.080]
    ry=[.063,.094,.097,.099,.097,.089,.085,.080]
    for side,sign in [('Left',1),('Right',-1)]:
        start=len(vertices)
        for j,z in enumerate(zs):
            t=(z-.305)/(.91-.305);centerx=sign*(.108-.012*t)
            for i in range(segments):
                angle=math.tau*i/segments
                fold=1+.125*math.cos(angle*7+.65*math.sin(t*4))+.045*math.sin(angle*13-t*6)
                bunch=1+.09*math.sin(t*math.tau*6+math.cos(angle)*1.2)*(1-smooth(.40,.67,z))
                px=centerx+math.cos(angle)*float(np.interp(z,profile_z,rx))*fold*bunch
                py=.004+math.sin(angle)*float(np.interp(z,profile_z,ry))*fold*bunch
                pz=z+.004*math.cos(angle*7+t*10)*math.sin(math.pi*t)
                vertices.append((px,py,pz))
        for j in range(len(zs)-1):
            for i in range(segments):
                a=start+j*segments+i;b=start+j*segments+(i+1)%segments
                faces.append((a,b,b+segments,a+segments))
                uvfaces.append([(i/segments,j/(len(zs)-1)),((i+1)/segments,j/(len(zs)-1)),((i+1)/segments,(j+1)/(len(zs)-1)),(i/segments,(j+1)/(len(zs)-1))])
        faces.append(tuple(reversed([start+i for i in range(segments)])))
        uvfaces.append([(0.5+math.cos(math.tau*i/segments)*.45,.5+math.sin(math.tau*i/segments)*.45) for i in reversed(range(segments))])
        faces.append(tuple(start+(len(zs)-1)*segments+i for i in range(segments)))
        uvfaces.append([(0.5+math.cos(math.tau*i/segments)*.45,.5+math.sin(math.tau*i/segments)*.45) for i in range(segments)])
    mesh=bpy.data.meshes.new('DosaV2_TrousersUnderShell');mesh.from_pydata(vertices,[],faces);mesh.materials.append(material);mesh.update()
    uv=mesh.uv_layers.new(name='UVMap')
    for p,coords in zip(mesh.polygons,uvfaces):
        p.use_smooth=True
        for li,value in zip(p.loop_indices,coords):uv.data[li].uv=value
    obj=bpy.data.objects.new('DosaV2_TrousersUnderShell',mesh);bpy.context.scene.collection.objects.link(obj)
    obj.parent=rig;obj.matrix_basis=Matrix.Identity(4);obj.matrix_parent_inverse=Matrix.Identity(4)
    mod=obj.modifiers.new('DosaV2_Skin','ARMATURE');mod.object=rig
    split=segments*len(zs)
    for v in mesh.vertices:assign(obj,v.index,independent_leg_weights(v.co,'Left' if v.index<split else 'Right'))
    obj['surface_role']='authored_complete_trouser_under_shell'
    obj['export_note']='Bake DosaV2_TrouserUndercloth to PBR maps before Unity import; source scan UV remains on TrousersSource.'
    for side in ['Left','Right']:
        lining=bpy.data.objects['DosaV2_LegLining_'+side];sign=1 if side=='Left' else -1
        # Compress former below-cuff coverage into the pants. Boots already contain their own surface.
        for v in lining.data.vertices:
            if v.co.z<.36:v.co.z=.325+(v.co.z-.10)*.035/.26
            v.co.x=sign*.096+(v.co.x-sign*.096)*.78
            v.co.y=.008+(v.co.y-.008)*.78
            assign(lining,v.index,independent_leg_weights(v.co,side))
        lining.data.update()
    return obj

def bake_trouser_material(obj,reuse=False):
    path=ART/'LowerRepairTextures';path.mkdir(exist_ok=True)
    mat=obj.data.materials[0];nodes=mat.node_tree.nodes;links=mat.node_tree.links
    bpy.ops.object.select_all(action='DESELECT');obj.hide_set(False);obj.select_set(True);bpy.context.view_layer.objects.active=obj
    scene=bpy.context.scene;scene.render.bake.use_selected_to_active=False;scene.render.bake.margin=12
    result={};images={}
    for label,typ,size in [('BaseColor','DIFFUSE',1024),('Normal','NORMAL',1024)]:
        existing=path/('T_DosaV2_Trousers_'+label+'.png')
        if reuse and existing.exists():
            image=bpy.data.images.load(str(existing),check_existing=False)
            if label=='Normal':image.colorspace_settings.name='Non-Color'
            image.pack();images[label]=image;result[label]=str(existing.relative_to(ROOT)).replace('\\','/');continue
        image=bpy.data.images.new('T_DosaV2_Trousers_'+label,width=size,height=size,alpha=False)
        if label=='Normal':image.colorspace_settings.name='Non-Color'
        target=nodes.new('ShaderNodeTexImage');target.image=image;nodes.active=target
        if typ=='DIFFUSE':bpy.ops.object.bake(type=typ,pass_filter={'COLOR'})
        else:bpy.ops.object.bake(type=typ)
        image.filepath_raw=str(path/(image.name+'.png'));image.file_format='PNG';image.save();image.pack()
        images[label]=image;result[label]=str(Path(image.filepath_raw).relative_to(ROOT)).replace('\\','/')
        nodes.remove(target)
    bsdf=nodes.get('Principled BSDF')
    for socket in ['Base Color','Normal']:
        for link in list(bsdf.inputs[socket].links):links.remove(link)
    color=nodes.new('ShaderNodeTexImage');color.image=images['BaseColor'];links.new(color.outputs['Color'],bsdf.inputs['Base Color'])
    normal=nodes.new('ShaderNodeTexImage');normal.image=images['Normal'];normal_map=nodes.new('ShaderNodeNormalMap')
    links.new(normal.outputs['Color'],normal_map.inputs['Color']);links.new(normal_map.outputs['Normal'],bsdf.inputs['Normal'])
    obj['export_note']='PBR base/normal maps baked in LowerRepairTextures; metallic0 roughness.94. Original garment UV unchanged.'
    return result

def lower_metrics():
    bpy.context.view_layer.update();dg=bpy.context.evaluated_depsgraph_get();worst=[]
    for obj in bpy.context.scene.objects:
        if obj.type!='MESH' or obj.name=='DosaV2_SourceSurface':continue
        if not (obj.name=='DosaV2_BodyCore' or obj.name.startswith(('DosaV2_Trousers','DosaV2_Robe_','DosaV2_LegLining'))):continue
        e=obj.evaluated_get(dg);me=e.to_mesh();maxentry=None
        for edge in obj.data.edges:
            a,b=edge.vertices
            if max(obj.data.vertices[a].co.z,obj.data.vertices[b].co.z)>.99:continue
            original=(obj.data.vertices[a].co-obj.data.vertices[b].co).length
            if original<.0005:continue
            posed=(me.vertices[a].co-me.vertices[b].co).length
            entry={'object':obj.name,'rest_length_m':original,'posed_length_m':posed,'ratio':posed/original,
                'rest_xyz':list((obj.data.vertices[a].co+obj.data.vertices[b].co)*.5)}
            if maxentry is None or entry['ratio']>maxentry['ratio']:maxentry=entry
        if maxentry:worst.append(maxentry)
        e.to_mesh_clear()
    return sorted(worst,key=lambda e:-e['ratio'])

def lighting():
    s=bpy.context.scene;s.render.engine='CYCLES';s.cycles.samples=16;s.cycles.use_denoising=True
    s.render.resolution_x=1100;s.render.resolution_y=1100;s.render.resolution_percentage=100
    s.world.color=(.18,.18,.18);s.view_settings.view_transform='AgX'
    for name,pos,power in [('LowerKey',(2,-3,3),380),('LowerFill',(-2,-1,2),180),('LowerRim',(1,2,3),350)]:
        o=bpy.data.objects.new(name,bpy.data.lights.new(name,'AREA'));s.collection.objects.link(o)
        o.data.energy=power;o.data.size=3;o.location=pos;aim(o,(0,0,.55))
    cam=bpy.data.objects.new('LowerRepairCamera',bpy.data.cameras.new('LowerRepairCamera'));s.collection.objects.link(cam)
    cam.data.type='ORTHO';cam.data.ortho_scale=1.12;s.camera=cam;return cam

def render(cam,prefix,view):
    cam.location={'front':(0,-3,.65),'quarter':(2,-3,.8),'back':(0,3,.65)}[view]
    aim(cam,(0,0,.55));bpy.context.scene.render.filepath=str(OUT/(prefix+'-'+view+'.png'))
    bpy.ops.render.render(write_still=True)

def main():
    parser=argparse.ArgumentParser();parser.add_argument('--inspect-only',action='store_true')
    parser.add_argument('--dump-only',action='store_true')
    parser.add_argument('--no-before',action='store_true')
    parser.add_argument('--no-renders',action='store_true')
    parser.add_argument('--reuse-bake',action='store_true')
    args=parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
    before=digest(SOURCE);bpy.ops.wm.open_mainfile(filepath=str(SOURCE))
    rig=bpy.data.objects['DosaV2_Rig']
    if args.dump_only:
        result={}
        for obj in bpy.context.scene.objects:
            if obj.name=='DosaV2_BodyCore' or obj.name.startswith('DosaV2_Robe_'):
                result[obj.name]={'vertices':[list(v.co) for v in obj.data.vertices], 'faces':[list(p.vertices) for p in obj.data.polygons],
                    'materials':[{'name':m.name,'images':[n.image.filepath for n in m.node_tree.nodes if n.type=='TEX_IMAGE' and n.image]} for m in obj.data.materials],
                    'uv':[[list(obj.data.uv_layers.active.data[i].uv) for i in p.loop_indices] for p in obj.data.polygons]}
        (OUT/'lower-data.json').write_text(json.dumps(result))
        print(json.dumps({k:{'vertices':len(v['vertices']),'faces':len(v['faces']),'materials':v['materials']} for k,v in result.items()}));return
    for p in rig.pose.bones:p.matrix_basis=Matrix.Identity(4)
    for obj in bpy.context.scene.objects:
        if obj.type=='MESH':obj.hide_render=obj.name=='DosaV2_SourceSurface'
    bpy.context.view_layer.update();cam=lighting()
    if not args.no_before and not args.no_renders:
        for view in ['front','quarter','back']:render(cam,'source-rest',view)
    if args.inspect_only:
        for obj in bpy.context.scene.objects:
            if obj.type=='MESH':obj.hide_render=not obj.name.startswith('DosaV2_Robe_')
        for view in ['front','back']:render(cam,'robe-only',view)
        for obj in bpy.context.scene.objects:
            if obj.type=='MESH':obj.hide_render=obj.name!='DosaV2_BodyCore'
        for view in ['front','back']:render(cam,'body-only',view)
        print(json.dumps({'source_unchanged':before==digest(SOURCE)}),flush=True);return
    protected={o.name:mesh_hash(o) for o in bpy.context.scene.objects if o.type=='MESH' and o.name not in ['DosaV2_BodyCore'] and not o.name.startswith(('DosaV2_Robe_','DosaV2_LegLining_'))}
    report={'source_sha256':before,'status':'WIP_REPAIR_PENDING_REVIEW','production_actions':len(bpy.data.actions),'before':{},'after':{}}
    if not args.no_before:
        for pose in ['lowered','step','squat']:
            set_pose(rig,pose);report['before'][pose]=lower_metrics()
            if not args.no_renders:
                for view in ['front','quarter']:render(cam,'before-'+pose,view)
    set_pose(rig,'rest');report['semantic']=semantic_repair(rig);shell=make_trouser_shell(rig)
    report['authored_trouser_material']={'name':shell.data.materials[0].name,'maps':bake_trouser_material(shell,args.reuse_bake),'metallic':0,'roughness':.94}
    set_pose(rig,'rest')
    for pose in ['rest','lowered','step','squat']:
        set_pose(rig,pose);report['after'][pose]=lower_metrics()
        if not args.no_renders:
            for view in ['front','quarter']:render(cam,'after-'+pose,view)
    set_pose(rig,'rest')
    report['protected_meshes_unchanged']={name:mesh_hash(bpy.data.objects[name])==value for name,value in protected.items()}
    report['source_unchanged']=before==digest(SOURCE)
    report['changed_objects']=['DosaV2_BodyCore','DosaV2_TrousersSource','DosaV2_TrousersUnderShell','DosaV2_LegLining_Left','DosaV2_LegLining_Right']+[o.name for o in bpy.context.scene.objects if o.name.startswith('DosaV2_Robe_')]
    report['cloth_contract']=[{'name':o.name,'vertices':len(o.data.vertices),'pinned':sum(c.color[0]==0 for c in o.data.color_attributes['ClothMobility'].data)} for o in bpy.context.scene.objects if o.name.startswith(('DosaV2_Robe_','DosaV2_SleeveOuter_'))]
    (OUT/'lower-repair-report.json').write_text(json.dumps(report,indent=2))
    bpy.ops.file.pack_all();bpy.ops.wm.save_as_mainfile(filepath=str(ART/'DosaV2_LowerRepair.blend'))
    print(json.dumps({'source_unchanged':report['source_unchanged'],'protected_unchanged':all(report['protected_meshes_unchanged'].values()),'changed_objects':report['changed_objects']}),flush=True)

if __name__=='__main__':main()
