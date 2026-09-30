"""Bounded temporary Cheongryong: reuse cropped head/claws, author rounded body.
Not approved final art. Blender background only; never overwrite source.
"""
import bpy, bmesh, math, json, hashlib, shutil
from pathlib import Path
from mathutils import Vector, Matrix, Quaternion

ROOT=Path('C:/Users/yj666/Oheangbu')
SOURCE=ROOT/'Art/SpellVFX120/MeshySummons/Blender/ImugiSalvage01/SM_112_Imugi_Salvage01.blend'
OUT=ROOT/'Art/Demo/Chapter3/Cheongryong'
ASSET=ROOT/'Oheangbu/Assets/_Project/Art/Demo/Chapter3/Cheongryong'
OUT.mkdir(parents=True,exist_ok=True);ASSET.mkdir(parents=True,exist_ok=True)
protected=[SOURCE,ROOT/'Art/SpellVFX120/MeshySummons/Blender/MeshySummons_Source.blend',ROOT/'Art/SpellVFX120/MeshySummons/Source/112_C634/preview/model_urls_glb.glb',ROOT/'Art/SpellVFX120/MeshySummons/Retry02/112_C634/preview/model_urls_glb.glb']
hashes={str(p):hashlib.sha256(p.read_bytes()).hexdigest() for p in protected}
bpy.ops.wm.open_mainfile(filepath=str(SOURCE))
def donor(name):
    o=bpy.data.objects[name]
    return [o.matrix_world@v.co for v in o.data.vertices],[list(p.vertices) for p in o.data.polygons]
head_v,head_f=donor('Reused_Meshy_Head_Horns_Beard')
foot_v,foot_f=donor('Reused_Fore_Left_ClawedFoot')
for o in list(bpy.data.objects):bpy.data.objects.remove(o,do_unlink=True)
for a in list(bpy.data.actions):bpy.data.actions.remove(a)
scene=bpy.context.scene;scene.name='Cheongryong_TemporaryPrototype';scene.render.fps=30
scene.unit_settings.system='METRIC';scene.unit_settings.scale_length=1

def center(t):return Vector((.43*math.sin(t*math.tau)*math.sin(t*math.pi),-3.2+7.8*t,.92-.52*t+.07*math.sin(t*math.tau)))
def radius(t):return (.22+.32*math.sin(min(1,t/.23)*math.pi*.5))*max(.028,(1-t)**.56)
def axes(t):
    forward=(center(min(1,t+.001))-center(max(0,t-.001))).normalized()
    side=forward.cross(Vector((0,0,1))).normalized();up=side.cross(forward).normalized()
    return side,up,forward
bone_defs={}
def define(n,h,t,p=None,deform=True):bone_defs[n]={'head':list(h),'tail':list(t),'parent':p,'deform':deform}
define('Root',(0,0,0),(0,0,.2),deform=False)
define('Head',center(0),(0,-4.55,.92),'Root')
for i in range(24):define('Body_%02d'%(i+1),center(i/24),center((i+1)/24),'Head' if i==0 else 'Body_%02d'%i)
define('MouthOrigin',(0,-4.75,.82),(0,-4.95,.82),'Head',False)
define('TailTip',center(1),center(1)+Vector((0,.16,0)),'Body_24',False)
legs={}
for prefix,t in [('Fore',.18),('Hind',.60)]:
    c=center(t)
    for side,sign in [('L',-1),('R',1)]:
        name=prefix+'_'+side;hip=c+Vector((sign*.35,0,-.12));knee=c+Vector((sign*.78,.08,-.36));paw=Vector((c.x+sign*.87,c.y-.16,.13))
        parent='Body_%02d'%(int(t*24)+1)
        define(name+'_Upper',hip,knee,parent);define(name+'_Lower',knee,paw,name+'_Upper');define(name+'_Foot',paw,paw+Vector((0,-.3,0)),name+'_Lower')
        legs[name]=(hip,knee,paw)

verts=[];faces=[];mats=[];weights=[]
def body_weights(t):
    q=max(0,min(23,t*24-.5));i=int(q);f=q-i
    if i==23:return {'Body_24':1.}
    return {'Body_%02d'%(i+1):1-f,'Body_%02d'%(i+2):f}
def geometry(vs,fs,mat=0,ws=None):
    start=len(verts);verts.extend([tuple(v) for v in vs]);faces.extend([tuple(start+i for i in f) for f in fs]);mats.extend([mat]*len(fs))
    if isinstance(ws,dict):weights.extend([dict(ws) for v in vs])
    else:weights.extend(ws)
def tube(points,radii,ws,mat=0,sides=16):
    vs=[];fs=[]
    for j,p in enumerate(points):
        direction=(points[min(j+1,len(points)-1)]-points[max(0,j-1)]).normalized()
        a=direction.cross(Vector((0,0,1)))
        if a.length<.001:a=direction.cross(Vector((0,1,0)))
        a.normalize();b=a.cross(direction).normalized()
        for k in range(sides):vs.append(p+radii[j]*(a*math.cos(k*math.tau/sides)+b*math.sin(k*math.tau/sides)))
    for j in range(len(points)-1):
        for k in range(sides):fs.append((j*sides+k,j*sides+(k+1)%sides,(j+1)*sides+(k+1)%sides,(j+1)*sides+k))
    fs.extend([tuple(reversed(range(sides))),tuple((len(points)-1)*sides+k for k in range(sides))])
    geometry(vs,fs,mat,[ws[j] for j in range(len(points)) for k in range(sides)])

# A genuinely new circular tube, never the rejected stretched neck/body donor.
tube([center(i/144) for i in range(145)],[radius(i/144) for i in range(145)],[body_weights(i/144) for i in range(145)],0,28)
# Overlapping diamond scales preserve round cross section; each is closed.
for row in range(66):
    t=(row+.5)/67;c=center(t);r=radius(t);side,up,forward=axes(t)
    for col in range(11):
        angle=-.12+(col+(row%2)*.5)*math.tau/11
        normal=side*math.cos(angle)+up*math.sin(angle);tangent=-side*math.sin(angle)+up*math.cos(angle)
        p=c+normal*(r+.006);wide=r*.255;long=.105*(.3+.7*(1-t));h=.035*(1-t)+.005
        corners=[p+forward*long,p+tangent*wide,p-forward*long,p-tangent*wide]
        sv=corners+[p+normal*h,p-normal*.012]
        sf=[(k,(k+1)%4,4) for k in range(4)]+[((k+1)%4,k,5) for k in range(4)]
        geometry(sv,sf,1 if math.sin(angle)>-.35 else 2,body_weights(t))
# A low dorsal ridge reads along the tail without a broad wing/fin.
for row in range(32):
    t=.06+row*.027;c=center(t);s,u,f=axes(t);r=radius(t);p=c+u*r
    geometry([p-s*.035,p+s*.035,p+f*.17,p+u*(.14*(1-t)+.03)-f*.06],[(0,1,2),(0,3,1),(1,3,2),(2,3,0)],3,body_weights(t))
# Source head preserves its anatomy uniformly; only placement/scale change.
head_scale=2.1
geometry([Vector(((v.x+.0466222)*head_scale,(v.y+1.0007696)*head_scale-3.2,(v.z-.47)*head_scale+.92)) for v in head_v],head_f,0,{'Head':1.})
# Four replicated claw donors with separately skinned short limb connectors.
for name,(hip,knee,paw) in legs.items():
    tube([hip,hip.lerp(knee,.5),knee,knee.lerp(paw,.5),paw],[.185,.17,.135,.105,.09],[{name+'_Upper':1},{name+'_Upper':1},{name+'_Upper':.5,name+'_Lower':.5},{name+'_Lower':1},{name+'_Foot':1}],0,14)
    sign=-1 if name.endswith('_L') else 1
    # Original cropped left paw is one preserved donor, mirrored for right.
    pv=[Vector((paw.x+(v.x+.1557333)*1.85*(-sign),paw.y+(v.y+.64)*1.85,v.z*1.55+.015)) for v in foot_v]
    pf=foot_f if sign==-1 else [list(reversed(f)) for f in foot_f]
    geometry(pv,pf,3,{name+'_Foot':1})

mesh=bpy.data.meshes.new('Cheongryong_PrototypeMesh');mesh.from_pydata(verts,[],faces);mesh.update()
bm=bmesh.new();bm.from_mesh(mesh);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(mesh);bm.free()
body=bpy.data.objects.new('SM_Cheongryong_Prototype',mesh);scene.collection.objects.link(body)
palette=[('Dragon_Jade',(0.035,.25,.23,1),.22,.49),('Dragon_Scales',(0.065,.39,.31,1),.3,.4),('Dragon_Belly',(.49,.57,.35,1),.1,.6),('Dragon_HornClaw',(.42,.33,.16,1),.25,.44)]
for name,color,metal,rough in palette:
    m=bpy.data.materials.new(name);m.diffuse_color=color;m.use_nodes=True;m.node_tree.nodes.clear();bs=m.node_tree.nodes.new('ShaderNodeBsdfPrincipled');output=m.node_tree.nodes.new('ShaderNodeOutputMaterial');m.node_tree.links.new(bs.outputs['BSDF'],output.inputs['Surface']);bs.inputs['Base Color'].default_value=color;bs.inputs['Metallic'].default_value=metal;bs.inputs['Roughness'].default_value=rough;mesh.materials.append(m)
for p,mat in zip(mesh.polygons,mats):p.material_index=mat;p.use_smooth=mat==0
uv=mesh.uv_layers.new(name='PrototypePlanarUV')
for p in mesh.polygons:
    for li in p.loop_indices:
        co=mesh.vertices[mesh.loops[li].vertex_index].co;uv.data[li].uv=((co.x+1.5)/3,(co.y+5)/10)
arm=bpy.data.armatures.new('Cheongryong_PrototypeSkeleton');rig=bpy.data.objects.new('Cheongryong_PrototypeRig',arm);scene.collection.objects.link(rig)
bpy.context.view_layer.objects.active=rig;rig.select_set(True);bpy.ops.object.mode_set(mode='EDIT')
for n,row in bone_defs.items():
    b=arm.edit_bones.new(n);b.head=row['head'];b.tail=row['tail'];b.use_deform=row['deform']
    if row['parent']:b.parent=arm.edit_bones[row['parent']]
bpy.ops.object.mode_set(mode='OBJECT')
for n,row in bone_defs.items():
    if row['deform']:body.vertex_groups.new(name=n)
for idx,w in enumerate(weights):
    total=sum(w.values())
    for n,value in w.items():
        if value>1e-7:body.vertex_groups[n].add([idx],value/total,'REPLACE')
mod=body.modifiers.new('Cheongryong_Skin','ARMATURE');mod.object=rig;body.parent=rig;body.matrix_parent_inverse=Matrix.Identity(4)
rig.show_in_front=True
for p in rig.pose.bones:p.rotation_mode='QUATERNION'
# Head-only animation follows the production bible. Runtime sets body follow.
actions=[]
for name,duration,amplitude,axis in [('CR_Idle',2,.025,(0,0,1)),('CR_HeadAttack_Anticipation',1.2,.24,(1,0,0)),('CR_TailSweep_Anticipation',1.4,.26,(0,0,1))]:
    rig.animation_data_create();rig.animation_data.action=None
    action=bpy.data.actions.new(name);action.use_fake_user=True;rig.animation_data.action=action
    frames=round(duration*30)
    for f in range(frames+1):
        phase=f/frames;angle=amplitude*math.sin(phase*math.tau) if name=='CR_Idle' else amplitude*math.sin(phase*math.pi)**2
        p=rig.pose.bones['Head'];local_axis=arm.bones['Head'].matrix_local.to_quaternion().inverted()@Vector(axis);p.rotation_quaternion=Quaternion(local_axis,angle);p.keyframe_insert('rotation_quaternion',frame=f+1,group='Head')
    actions.append({'name':name,'durationSeconds':duration,'headOnly':True,'loop':name=='CR_Idle','rootTranslation':False})
rig.animation_data.action=None
for p in rig.pose.bones:p.matrix_basis=Matrix.Identity(4)
scene.frame_set(1);bpy.context.view_layer.update();mesh.calc_loop_triangles()
count=len(mesh.loop_triangles);assert count<=35000
report={'status':'TEMPORARY_DERIVATIVE_NOT_FINAL_ART_APPROVED','triangles':count,'vertices':len(verts),'bones':len(arm.bones),'bodyChainCount':24,'maxWeightInfluences':max(sum(g.weight>1e-7 for g in v.groups) for v in mesh.vertices),'weightSumError':max(abs(sum(g.weight for g in v.groups)-1) for v in mesh.vertices),'nativeAxes':{'up':'+Z','forward':'-Y','units':'meters'},'nativeBounds':[[min(v.co[i] for v in mesh.vertices),max(v.co[i] for v in mesh.vertices)] for i in range(3)],'bodyCrossSection':'circular with continuous neck and tail taper','maximumBodyDiameterM':2*max(radius(i/1000) for i in range(1001)),'bonesDefinitions':bone_defs,'actions':actions,'reuse':['Head/horns/beard cropped donor uniformly scaled 2.1','One cropped claw donor duplicated four times, right copies mirrored'],'new':['Rounded tapered serpentine body and tail','Closed raised scale geometry','Dorsal ridge','Limb connectors','Skeleton and normalized skin','Head-only provisional clips'],'defects':['Temporary palette and planar UV; no production textures','Head and limb intersections have no remeshed seam','Four paws repeat the same donor','Tail/body animation requires runtime procedural follow','No final art or runtime approval'],'sourceHashesBefore':hashes}
blend=OUT/'Cheongryong_Prototype.blend';fbx=OUT/'SM_Cheongryong_Prototype.fbx'
bpy.ops.wm.save_as_mainfile(filepath=str(blend))
bpy.ops.object.select_all(action='DESELECT');body.select_set(True);rig.select_set(True);bpy.context.view_layer.objects.active=rig
bpy.ops.export_scene.fbx(filepath=str(fbx),use_selection=True,object_types={'MESH','ARMATURE'},add_leaf_bones=False,bake_anim=True,bake_anim_use_all_actions=True,bake_anim_use_nla_strips=False,bake_anim_step=1,bake_anim_simplify_factor=0,axis_forward='-Z',axis_up='Y',path_mode='AUTO')
shutil.copy2(fbx,ASSET/fbx.name)
report['sourceHashesAfter']={str(p):hashlib.sha256(p.read_bytes()).hexdigest() for p in protected};assert report['sourceHashesAfter']==hashes
(OUT/'rig_report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print('CHEONGRYONG_REPORT',json.dumps({k:v for k,v in report.items() if k not in ('bonesDefinitions','sourceHashesBefore','sourceHashesAfter')}))
