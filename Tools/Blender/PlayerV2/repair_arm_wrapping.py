"""Fit internal arm coverage and extend the original 3D wrapping over reconstructed wrists.

Only Hands (the independently validated weight refinement), SleeveInner and ArmLining are
carried as changes. Refined/LowerRepair/Assembled sources are never overwritten.
"""
import bpy,bmesh,json,math,hashlib,sys
import numpy as np
from pathlib import Path
from mathutils import Vector,Matrix
ROOT=Path(__file__).resolve().parents[3];ART=ROOT/'Art/PlayerV2';OUT=ART/'Inspect/HandsWithSleeves'
SOURCE=ART/'DosaV2_HandsSculpted.blend';source_hash=hashlib.sha256(SOURCE.read_bytes()).hexdigest()
BUDGET='--budget' in sys.argv
bpy.ops.wm.open_mainfile(filepath=str(SOURCE));rig=bpy.data.objects['DosaV2_Rig'];scene=bpy.context.scene
for p in rig.pose.bones:p.matrix_basis=Matrix.Identity(4)
bpy.context.view_layer.update()

def assign(obj,index,values):
    for g in obj.vertex_groups:g.remove([index])
    for name,w in values.items():
        if w<=0:continue
        group=obj.vertex_groups.get(name) or obj.vertex_groups.new(name=name);group.add([index],w,'REPLACE')
def smooth(a,b,x):
    t=max(0,min(1,(x-a)/(b-a)));return t*t*(3-2*t)
def select(obj):
    bpy.ops.object.select_all(action='DESELECT');obj.hide_set(False);obj.select_set(True);bpy.context.view_layer.objects.active=obj
def hash_mesh(obj):
    h=hashlib.sha256()
    for v in obj.data.vertices:
        h.update(np.asarray(v.co,dtype=np.float32).tobytes())
        for g in v.groups:h.update((obj.vertex_groups[g.group].name+format(g.weight,'.8f')).encode())
    if obj.data.uv_layers.active:
        for uv in obj.data.uv_layers.active.data:h.update(np.asarray(uv.uv,dtype=np.float32).tobytes())
    return h.hexdigest()
allowed=['DosaV2_SleeveInner_L','DosaV2_SleeveInner_R','DosaV2_ArmLining_Left','DosaV2_ArmLining_Right']
protected={o.name:hash_mesh(o) for o in scene.objects if o.type=='MESH' and o.name not in allowed}
report={'status':'WRIST_VOLUME_REPAIR_PENDING_STRESS_REVIEW','actions':len(bpy.data.actions),'sides':{}}
material=bpy.data.materials.new('DosaV2_WristWrapping');material.use_nodes=True
nodes=material.node_tree.nodes;links=material.node_tree.links;bs=nodes.get('Principled BSDF')
bs.inputs['Roughness'].default_value=.94
coord=nodes.new('ShaderNodeTexCoord');noise=nodes.new('ShaderNodeTexNoise');noise.inputs['Scale'].default_value=145;noise.inputs['Detail'].default_value=3
links.new(coord.outputs['UV'],noise.inputs['Vector'])
ramp=nodes.new('ShaderNodeValToRGB');ramp.color_ramp.elements[0].color=(.045,.040,.033,1);ramp.color_ramp.elements[1].color=(.105,.093,.076,1)
links.new(noise.outputs['Fac'],ramp.inputs['Fac'])
separate=nodes.new('ShaderNodeSeparateXYZ');links.new(coord.outputs['UV'],separate.inputs['Vector'])
multiply=nodes.new('ShaderNodeMath');multiply.operation='MULTIPLY';multiply.inputs[1].default_value=8;links.new(separate.outputs['Y'],multiply.inputs[0])
phase=nodes.new('ShaderNodeMath');phase.operation='ADD';links.new(multiply.outputs[0],phase.inputs[0]);links.new(separate.outputs['X'],phase.inputs[1])
tau=nodes.new('ShaderNodeMath');tau.operation='MULTIPLY';tau.inputs[1].default_value=math.tau;links.new(phase.outputs[0],tau.inputs[0])
cosine=nodes.new('ShaderNodeMath');cosine.operation='COSINE';links.new(tau.outputs[0],cosine.inputs[0])
shade=nodes.new('ShaderNodeMapRange');shade.inputs['From Min'].default_value=-1;shade.inputs['From Max'].default_value=1;shade.inputs['To Min'].default_value=.34;shade.inputs['To Max'].default_value=1
links.new(cosine.outputs[0],shade.inputs['Value']);mix=nodes.new('ShaderNodeMixRGB');mix.blend_type='MULTIPLY';mix.inputs[0].default_value=1
links.new(ramp.outputs['Color'],mix.inputs[1]);links.new(shade.outputs['Result'],mix.inputs[2]);links.new(mix.outputs['Color'],bs.inputs['Base Color'])
wave=nodes.new('ShaderNodeTexWave');wave.wave_type='BANDS';wave.bands_direction='Y';wave.inputs['Scale'].default_value=38;wave.inputs['Distortion'].default_value=.6
links.new(coord.outputs['UV'],wave.inputs['Vector']);bump=nodes.new('ShaderNodeBump');bump.inputs['Strength'].default_value=.25;bump.inputs['Distance'].default_value=.00016
links.new(wave.outputs['Color'],bump.inputs['Height']);links.new(bump.outputs['Normal'],bs.inputs['Normal'])
baked={};texdir=ART/'Staging/Character/WrappingTextures';texdir.mkdir(parents=True,exist_ok=True)
def arm_weights(side,x):
    hand=smooth(.638,.665,x);fore=smooth(.407,.478,x)*(1-hand)
    return {side+'Arm':1-hand-fore,side+'ForeArm':fore,side+'Hand':hand}
for side,short,sign in [('Right','R',-1),('Left','L',1)]:
    source=bpy.data.objects['DosaV2_SleeveInner_'+short]
    source.data.calc_loop_triangles();before_triangles=len(source.data.loop_triangles)
    # The scanned forearm patch has open branches and cannot be lofted as a continuous cuff.
    # Retain the upper sleeve and replace only its distal region with actual closed fabric walls.
    bm=bmesh.new();bm.from_mesh(source.data);remove=[f for f in bm.faces if all(abs(v.co.x)>.475 for v in f.verts)]
    removed_triangles=sum(len(f.verts)-2 for f in remove);bmesh.ops.delete(bm,geom=remove,context='FACES')
    loose=[v for v in bm.verts if not v.link_faces];bmesh.ops.delete(bm,geom=loose,context='VERTS');bm.to_mesh(source.data);bm.free()
    coords=[];faces=[];uvfaces=[];rings=[];segments=24
    for interior,count in [(False,24 if BUDGET else 33),(True,4 if BUDGET else 9)]:
        chain=[]
        for j in range(count):
            t=j/(count-1);x=([.426,.645,.663,.683][j] if BUDGET and interior else .426+.257*t);t=(x-.426)/.257;z=1.336-(x-.442)/(.663-.442)*.009
            width=float(np.interp(x,[.426,.46,.53,.60,.645,.663,.683],[.045,.043,.037,.031,.028,.029,.036]))
            depth=float(np.interp(x,[.426,.46,.53,.60,.645,.663,.683],[.036,.035,.030,.025,.021,.019,.019]))
            ring=[]
            for k in range(segments):
                a=k*math.tau/segments
                phase=(t*8+k/segments)%1
                ridge=.0025*math.sin(math.pi*phase)**.45 if not interior else -.0016
                ring.append(len(coords));coords.append((sign*x,(width+ridge)*math.cos(a),z+(depth+ridge)*math.sin(a)))
            chain.append(ring)
        for j in range(count-1):
            for k in range(segments):
                a=k/segments;b=(k+1)/segments;v=j/(count-1);w=(j+1)/(count-1)
                face=[chain[j][k],chain[j][(k+1)%segments],chain[j+1][(k+1)%segments],chain[j+1][k]]
                uvs=[(a,v),(b,v),(b,w),(a,w)]
                if interior:face.reverse();uvs.reverse()
                faces.append(face);uvfaces.append(uvs)
        rings.append(chain)
    for end in [0,-1]:
        for k in range(segments):
            faces.append([rings[0][end][k],rings[1][end][k],rings[1][end][(k+1)%segments],rings[0][end][(k+1)%segments]])
            uvfaces.append([(k/segments,0),((k+.2)/segments,0),((k+.8)/segments,0),((k+1)/segments,0)])
    mesh=bpy.data.meshes.new('DosaV2_WristWrap_'+short);mesh.from_pydata(coords,[],faces);mesh.update()
    mesh.materials.append(material)
    uv=mesh.uv_layers.new(name=source.data.uv_layers.active.name)
    for p,coords in zip(mesh.polygons,uvfaces):
        p.use_smooth=True
        for li,co in zip(p.loop_indices,coords):uv.data[li].uv=co
    cuff=bpy.data.objects.new('DosaV2_WristWrap_'+short,mesh);scene.collection.objects.link(cuff)
    cuff.parent=rig;cuff.matrix_basis=Matrix.Identity(4);cuff.matrix_parent_inverse=Matrix.Identity(4)
    cuff.vertex_groups.new(name=side+'Arm');cuff.vertex_groups.new(name=side+'ForeArm');cuff.vertex_groups.new(name=side+'Hand')
    for v in mesh.vertices:
        assign(cuff,v.index,arm_weights(side,abs(v.co.x)))
    bm=bmesh.new();bm.from_mesh(mesh);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(mesh);bm.free()
    if not baked:
        select(cuff);scene.render.engine='CYCLES';scene.cycles.samples=8;scene.render.bake.margin=8
        for suffix,bake_type in [('BaseColor','DIFFUSE'),('Normal','NORMAL')]:
            image=bpy.data.images.new('T_DosaV2_Wrapping_'+suffix,width=1024,height=1024,alpha=False)
            image.colorspace_settings.name='sRGB' if suffix=='BaseColor' else 'Non-Color'
            target=nodes.new('ShaderNodeTexImage');target.image=image;nodes.active=target
            for n in nodes:n.select=n==target
            scene.render.bake.use_pass_direct=False;scene.render.bake.use_pass_indirect=False;scene.render.bake.use_pass_color=True
            bpy.ops.object.bake(type=bake_type);image.filepath_raw=str(texdir/(image.name+'.png'));image.file_format='PNG';image.save()
            baked[suffix]=image;nodes.remove(target)
        for socket in ['Base Color','Normal']:
            for link in list(bs.inputs[socket].links):links.remove(link)
        for suffix in ['BaseColor','Normal']:
            n=nodes.new('ShaderNodeTexImage');n.image=baked[suffix]
            if suffix=='BaseColor':links.new(n.outputs['Color'],bs.inputs['Base Color'])
            else:
                normal=nodes.new('ShaderNodeNormalMap');links.new(n.outputs['Color'],normal.inputs['Color']);links.new(normal.outputs['Normal'],bs.inputs['Normal'])
    skin=cuff.modifiers.new('DosaV2_Skin','ARMATURE');skin.object=rig
    cuff.data.calc_loop_triangles();added_triangles=len(cuff.data.loop_triangles)
    # One renderer per existing SleeveInner binding; all original triangles and UV corners remain.
    select(source);cuff.select_set(True);bpy.ops.object.join()
    source['wrist_wrap']='Closed authored fabric wrapping with inner wall, physical helical folds, Hand100% at distal opening'
    lining=bpy.data.objects['DosaV2_ArmLining_'+side]
    for v in lining.data.vertices:
        ax=abs(v.co.x);zaxis=float(np.interp(ax,[.13,.22,.32,.442,.53,.61,.676],[1.36,1.35,1.344,1.336,1.332,1.329,1.327]))
        v.co.y*=.55;v.co.z=zaxis+(v.co.z-zaxis)*.55
        if ax>.40:assign(lining,v.index,arm_weights(side,ax))
    lining.data.update();lining.hide_render=False;lining.hide_set(False)
    report['sides'][side]={'source_distal_triangles_replaced':removed_triangles,
        'wrist_wrap_extent_m':[.426,.683],'added_triangles':added_triangles,'original_sleeve_triangles':before_triangles,
        'physical_thickness_m':.0016,'lining_radial_scale':.55}
report['protected_meshes_unchanged']={name:hash_mesh(bpy.data.objects[name])==value for name,value in protected.items()}
report['source_unchanged']=source_hash==hashlib.sha256(SOURCE.read_bytes()).hexdigest()
report['merge_objects']=['DosaV2_Hands']+allowed
report['budget_variant']=BUDGET
report['remaining']=['Inspect elbow flexion, forearm pronation/supination and wrist flexion/extension with the real sleeve.',
 'Recheck actual hand/shaft contact, and verify inner lining has no visible camera samples.']
(OUT/('arm-wrapping-budget-repair.json' if BUDGET else 'arm-wrapping-repair.json')).write_text(json.dumps(report,indent=2))
bpy.ops.file.pack_all();bpy.ops.wm.save_as_mainfile(filepath=str(ART/('DosaV2_ArmsWrappedBudget.blend' if BUDGET else 'DosaV2_ArmsWrapped.blend')))
print(json.dumps(report),flush=True)
