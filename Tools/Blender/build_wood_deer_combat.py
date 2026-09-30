"""Provisional real skinning/animation derivative; never saves the approved source.

Run with Blender 5 --background --factory-startup --python this_file.py.
No Unity calls. Original geometry/UVs/materials stay intact in bind pose.
"""
import bpy, math, json, hashlib, shutil
from pathlib import Path
from mathutils import Vector, Matrix, Quaternion
from mathutils.kdtree import KDTree

ROOT=Path('C:/Users/yj666/Oheangbu')
SOURCE=ROOT/'Art/SpellVFX120/WoodDeer/WoodDeer_Working.blend'
OUT=ROOT/'Art/Demo/Summons/WoodDeer'
ASSET=ROOT/'Oheangbu/Assets/_Project/Art/Demo/Summons/WoodDeer'
OUT.mkdir(parents=True,exist_ok=True);ASSET.mkdir(parents=True,exist_ok=True)
source_hash=hashlib.sha256(SOURCE.read_bytes()).hexdigest()
bpy.ops.wm.open_mainfile(filepath=str(SOURCE))
scene=bpy.context.scene;scene.render.fps=30
parts=[bpy.data.objects[n] for n in ('WoodDeer_Body','WoodDeer_Roots','WoodDeer_Leaves')]
body=parts[0]
source_coords={o.name:[v.co.copy() for v in o.data.vertices] for o in parts}
source_stats={}
for o in parts:
    o.data.calc_loop_triangles()
    source_stats[o.name]={'vertices':len(o.data.vertices),'triangles':len(o.data.loop_triangles),'materials':len(o.data.materials)}
for o in list(bpy.data.objects):
    if o not in parts: bpy.data.objects.remove(o,do_unlink=True)
for a in list(bpy.data.actions):bpy.data.actions.remove(a)
for o in parts:
    o.hide_set(False);o.hide_render=False;o.animation_data_clear()
    o['derivative_status']='PROVISIONAL_COMBAT_RIG_NOT_FINAL_MODEL_POLISH'

# Preserve the approved body and decoration vertices, including the asymmetric pose.
# Native axes: +Z up, -Y forward. Unity FBX export uses +Y up and +Z gameplay forward.
bpy.ops.object.select_all(action='DESELECT')
rig_data=bpy.data.armatures.new('WoodDeer_CombatSkeleton')
rig=bpy.data.objects.new('WoodDeer_CombatRig',rig_data);scene.collection.objects.link(rig)
rig.show_in_front=True;rig['status']='PROVISIONAL_DEFORM_RIG';rig['nominal_walk_speed_mps']=1.20
rig.select_set(True);bpy.context.view_layer.objects.active=rig
bpy.ops.object.mode_set(mode='EDIT')
bone_rows={}
def bone(name,head,tail,parent=None,deform=True):
    b=rig_data.edit_bones.new(name);b.head=head;b.tail=tail;b.use_deform=deform
    if parent:b.parent=rig_data.edit_bones[parent]
    bone_rows[name]={'head':list(head),'tail':list(tail),'parent':parent,'deform':deform}
    return b
bone('Root',(0,0,0),(0,0,.20),deform=False)
bone('Pelvis',(.27,.53,1.25),(.12,.25,1.30),'Root')
bone('Spine',(.12,.25,1.30),(0,-.015,1.31),'Pelvis')
bone('Chest',(0,-.015,1.31),(0,-.12,1.45),'Spine')
bone('Neck',(0,-.12,1.45),(0,-.34,1.79),'Chest')
bone('Head',(0,-.34,1.79),(0,-.65,1.94),'Neck')
bone('Tail',(.31,.69,1.29),(.34,.80,1.15),'Pelvis')
legs={
 'Fore_L':{'hip':(-.105,-.005,1.10),'knee':(-.10,-.018,.58),'foot':(-.109,-.108,.050),'parent':'Chest','phase':0.0},
 'Fore_R':{'hip':(.105,-.050,1.10),'knee':(.100,-.066,.58),'foot':(.099,-.151,.050),'parent':'Chest','phase':.5},
 'Hind_L':{'hip':(.105,.595,1.19),'knee':(.183,.838,.635),'foot':(.154,.835,.047),'parent':'Pelvis','phase':.75},
 'Hind_R':{'hip':(.325,.480,1.19),'knee':(.389,.748,.635),'foot':(.382,.740,.047),'parent':'Pelvis','phase':.25},
}
for name,row in legs.items():
    bone(name+'_Upper',row['hip'],row['knee'],row['parent'])
    bone(name+'_Lower',row['knee'],row['foot'],name+'_Upper')
    toe=Vector(row['foot'])+Vector((0,-.080,-.018))
    bone(name+'_Hoof',row['foot'],toe,name+'_Lower')
# Explicit inert sockets travel with the head/body and are not physics colliders.
bone('HornImpact',(0,-.68,2.15),(0,-.82,2.15),'Head',False)
bone('RootAttackOrigin',(0,-.12,.08),(0,-.38,.08),'Root',False)
bpy.ops.object.mode_set(mode='OBJECT')
for p in rig.pose.bones:p.rotation_mode='QUATERNION'
rest={b.name:b.matrix_local.copy() for b in rig_data.bones}

def smooth(a,b,x):
    q=max(0,min(1,(x-a)/(b-a)));return q*q*(3-2*q)
def segment_dist(p,a,b):
    d=b-a;t=max(0,min(1,(p-a).dot(d)/max(1e-10,d.length_squared)))
    return (p-(a+d*t)).length
def segment_weights(p,names,spread=.075,power=3.0):
    pairs=[]
    for n in names:
        row=bone_rows[n];d=segment_dist(p,Vector(row['head']),Vector(row['tail']))
        pairs.append((n,1/(spread+d)**power))
    pairs.sort(key=lambda x:-x[1]);pairs=pairs[:3]
    total=sum(w for _,w in pairs);return {n:w/total for n,w in pairs}
def normalize(weights):
    keep=sorted(((n,w) for n,w in weights.items() if w>1e-6),key=lambda q:-q[1])[:4]
    total=sum(w for _,w in keep)
    return {n:w/total for n,w in keep}
def nearest_leg(p):
    return min(legs,key=lambda n:min(segment_dist(p,Vector(bone_rows[n+s]['head']),Vector(bone_rows[n+s]['tail'])) for s in ('_Upper','_Lower','_Hoof')))

# Connectivity restricts skinning of low legs so spatially adjacent limbs cannot share bones.
ids={v.index for v in body.data.vertices if v.co.z<1.005};adj={i:[] for i in ids}
for e in body.data.edges:
    a,b=e.vertices
    if a in ids and b in ids:adj[a].append(b);adj[b].append(a)
leg_labels={}
while ids:
    todo=[ids.pop()];found=[]
    while todo:
        i=todo.pop();found.append(i)
        for j in adj[i]:
            if j in ids:ids.remove(j);todo.append(j)
    low=[body.data.vertices[i].co for i in found if body.data.vertices[i].co.z<.2]
    if not low:continue
    centre=sum(low,Vector())/len(low)
    label=min(legs,key=lambda n:(centre-Vector(legs[n]['foot'])).length_squared)
    for i in found:leg_labels[i]=label

all_weights=[]
core=['Pelvis','Spine','Chest','Neck','Head']
for v in body.data.vertices:
    p=v.co
    if p.z>1.84:
        w={'Head':1.0}
    elif p.y>.695 and p.z>1.12 and abs(p.x-.32)<.09:
        w=segment_weights(p,['Tail','Pelvis'])
    else:
        w=segment_weights(p,core)
        if p.z>1.63:
            h=smooth(1.63,1.84,p.z)
            w={n:weight*(1-h) for n,weight in w.items()};w['Head']=w.get('Head',0)+h
        label=leg_labels.get(v.index)
        blend=1-smooth(.84,1.19,p.z) if label else 0
        if not label and p.z<1.28:
            label=nearest_leg(p)
            d=segment_dist(p,Vector(legs[label]['hip']),Vector(legs[label]['knee']))
            blend=(1-smooth(.055,.22,d))*(1-smooth(1.02,1.28,p.z))
        if blend>0:
            lw=segment_weights(p,[label+'_Upper',label+'_Lower',label+'_Hoof'],.030,4)
            w={n:weight*(1-blend) for n,weight in w.items()}
            for n,weight in lw.items():w[n]=w.get(n,0)+weight*blend
        w=normalize(w)
    all_weights.append(w)

tree=KDTree(len(body.data.vertices))
for v in body.data.vertices:tree.insert(v.co,v.index)
tree.balance()
weight_stats={}
for o in parts:
    for n,row in bone_rows.items():
        if row['deform']:o.vertex_groups.new(name=n)
    weights=all_weights if o==body else [all_weights[tree.find(v.co)[1]] for v in o.data.vertices]
    for v,w in zip(o.data.vertices,weights):
        for n,value in w.items():o.vertex_groups[n].add([v.index],value,'REPLACE')
    arm=o.modifiers.new('WoodDeer_ActualSkin','ARMATURE');arm.object=rig
    arm.use_deform_preserve_volume=False;o.parent=rig;o.matrix_parent_inverse=Matrix.Identity(4)
    weight_stats[o.name]={'maxInfluences':max(len(w) for w in weights),'unweightedVertices':sum(not w for w in weights),
                        'maxWeightSumError':max(abs(sum(w.values())-1) for w in weights)}

# Two-bone IK is solved offline into actual joint matrices (no runtime IK dependency).
def point_from_parent(parent,point):
    return rig.pose.bones[parent].matrix @ rest[parent].inverted() @ Vector(point)
def set_segment(name,start,end):
    start,end=Vector(start),Vector(end)
    r=rest[name];old=Vector(bone_rows[name]['tail'])-Vector(bone_rows[name]['head'])
    q=old.rotation_difference(end-start) @ r.to_quaternion()
    rig.pose.bones[name].matrix=Matrix.Translation(start) @ q.to_matrix().to_4x4()
def ik_leg(name,target):
    row=legs[name];hip=point_from_parent(row['parent'],row['hip']);target=Vector(target)
    rest_hip,rest_knee,rest_foot=map(Vector,(row['hip'],row['knee'],row['foot']))
    a=(rest_knee-rest_hip).length;b=(rest_foot-rest_knee).length
    axis=target-hip;raw_distance=axis.length;axis.normalize()
    distance=min(a+b-.00001,max(abs(a-b)+.00001,raw_distance))
    target=hip+axis*distance
    rest_axis=(rest_foot-rest_hip).normalized()
    bend=rest_knee-rest_hip-rest_axis*(rest_knee-rest_hip).dot(rest_axis)
    bend=rest_axis.rotation_difference(axis)@bend
    bend-=axis*bend.dot(axis)
    if bend.length<.0001:bend=Vector((0,1,0))-axis*axis.y
    bend.normalize()
    along=(a*a-b*b+distance*distance)/(2*distance)
    knee=hip+axis*along+bend*math.sqrt(max(0,a*a-along*along))
    set_segment(name+'_Upper',hip,knee);bpy.context.view_layer.update()
    set_segment(name+'_Lower',knee,target);bpy.context.view_layer.update()
    rig.pose.bones[name+'_Hoof'].matrix=Matrix.Translation(target) @ rest[name+'_Hoof'].to_quaternion().to_matrix().to_4x4()
    return raw_distance>distance+.001
def set_local_rotation(name,axis,angle):
    # Input is a native armature-space axis; convert it to the bone local frame.
    local=rest[name].to_quaternion().inverted()@Vector(axis)
    rig.pose.bones[name].rotation_quaternion=Quaternion(local,angle)
def set_root_offset(offset):
    rig.pose.bones['Root'].location=rest['Root'].to_quaternion().inverted()@Vector(offset)
def assign(action):
    rig.animation_data_create();rig.animation_data.action=action
    if action:
        slot=next(iter(action.slots),None) or action.slots.new(id_type='OBJECT',name=rig.name)
        rig.animation_data.action_slot=slot

recipes=[('WD_Idle',2.0,True),('WD_Walk',.6,True),('WD_HornAttack',1.4,False),('WD_RootCast',1.4,False)]
actions=[];reach_warnings={};pose_samples={}
for action_name,seconds,loop in recipes:
    action=bpy.data.actions.new(action_name);action.use_fake_user=True;assign(action)
    end=round(seconds*30)+1;clamped=0
    for f in range(1,end+1):
        t=(f-1)/30;u=t/seconds
        for p in rig.pose.bones:p.matrix_basis=Matrix.Identity(4)
        stride=0;step_height=0;attack=0
        if action_name=='WD_Idle':
            set_local_rotation('Chest',(1,0,0),math.sin(u*math.tau)*.008)
            set_local_rotation('Neck',(1,0,0),math.sin(u*math.tau+.35)*.012)
            set_local_rotation('Head',(0,0,1),math.sin(u*math.tau)*.018)
            set_local_rotation('Tail',(0,0,1),math.sin(u*math.tau)*.035)
        elif action_name=='WD_Walk':
            set_root_offset((0,0,-.065+.006*math.cos(u*math.tau*2)))
            set_local_rotation('Spine',(0,1,0),math.sin(u*math.tau)*.012)
            set_local_rotation('Neck',(1,0,0),math.sin(u*math.tau*2)*.018)
            set_local_rotation('Head',(1,0,0),-math.sin(u*math.tau*2)*.012)
            set_local_rotation('Tail',(0,0,1),math.sin(u*math.tau+.8)*.075)
            stride=.432;step_height=.115
        elif action_name=='WD_HornAttack':
            # Anticipation, deliberate downward head/antler strike, then recovery.
            attack=smooth(.15,.70,t)*(1-smooth(.78,1.32,t))
            anticipation=smooth(0,.15,t)*(1-smooth(.15,.38,t))
            set_local_rotation('Neck',(1,0,0),attack*.37-anticipation*.055)
            set_local_rotation('Head',(1,0,0),attack*.34-anticipation*.065)
            set_local_rotation('Chest',(1,0,0),attack*.035)
            set_root_offset((0,-.048*attack,-.025*attack))
        else:
            attack=smooth(.16,.55,t)*(1-smooth(.73,1.30,t))
            set_local_rotation('Neck',(1,0,0),attack*.16)
            set_local_rotation('Head',(1,0,0),attack*.10)
            set_root_offset((0,0,-.018*attack))
        bpy.context.view_layer.update()
        for name,row in legs.items():
            target=Vector(row['foot'])
            if stride:
                phase=(u+row['phase'])%1
                if phase<.6: y=-stride/2+stride*(phase/.6);lift=0
                else:
                    swing=(phase-.6)/.4;y=stride/2-stride*smooth(0,1,swing)
                    lift=step_height*math.sin(math.pi*swing)**1.35
                target+=Vector((0,y,lift))
            clamped+=ik_leg(name,target)
        bpy.context.view_layer.update()
        for p in rig.pose.bones:
            p.keyframe_insert('location',frame=f,group=p.name)
            p.keyframe_insert('rotation_quaternion',frame=f,group=p.name)
            p.keyframe_insert('scale',frame=f,group=p.name)
    # Linear baked channels avoid Bezier overshoot between inspected frames.
    for layer in action.layers:
        for strip in layer.strips:
            for bag in strip.channelbags:
                for fc in bag.fcurves:
                    for key in fc.keyframe_points:key.interpolation='LINEAR'
    actions.append({'name':action_name,'frames':[1,end],'seconds':seconds,'fps':30,'loop':loop})
    reach_warnings[action_name]=clamped

# Copy only the existing two maps; source texture files and materials remain untouched.
texture_dir=ASSET/'Textures';texture_dir.mkdir(exist_ok=True)
for suffix in ('BaseColor','Normal'):
    name='T_WoodDeer_'+suffix+'.png'
    src=ROOT/'Oheangbu/Assets/_Project/Art/SpellVFX120/WoodDeer/Textures'/name
    target=texture_dir/name;shutil.copy2(src,target)
    for image in bpy.data.images:
        if image.name==name or image.name==name[:-4]:image.filepath=str(target);image.pack()

def evaluated(o):
    ev=o.evaluated_get(bpy.context.evaluated_depsgraph_get());mesh=ev.to_mesh()
    try:return [ev.matrix_world@v.co for v in mesh.vertices]
    finally:ev.to_mesh_clear()
def geometry_summary():
    points=[p for o in parts for p in evaluated(o)]
    return {'min':[min(v[i] for v in points) for i in range(3)],'max':[max(v[i] for v in points) for i in range(3)],
            'finite':all(math.isfinite(c) for p in points for c in p)}

assign(None)
for p in rig.pose.bones:p.matrix_basis=Matrix.Identity(4)
bpy.context.view_layer.update()
bind_errors={o.name:max((v-s).length for v,s in zip(evaluated(o),source_coords[o.name])) for o in parts}
bind_bounds=geometry_summary()
motion_checks={}
for row in actions:
    assign(bpy.data.actions[row['name']]);samples=[];deforms=[];hoof=[]
    for f in range(1,row['frames'][1]+1):
        scene.frame_set(f);bpy.context.view_layer.update()
        positions=evaluated(body)
        delta=max((p-source_coords[body.name][i]).length for i,p in enumerate(positions))
        samples.append(geometry_summary());deforms.append(delta)
        hoof.append({n:list(rig.pose.bones[n+'_Hoof'].head) for n in legs})
    scene.frame_set(1);first=evaluated(body);scene.frame_set(row['frames'][1]);last=evaluated(body)
    motion_checks[row['name']]={'finite':all(s['finite'] for s in samples),'maxBodyVertexDisplacementM':max(deforms),
      'loopEndVertexErrorM':max((a-b).length for a,b in zip(first,last)),
      'hoofZRangeM':{n:[min(v[n][2] for v in hoof),max(v[n][2] for v in hoof)] for n in legs},
      'bounds':{'min':[min(s['min'][i] for s in samples) for i in range(3)],'max':[max(s['max'][i] for s in samples) for i in range(3)]}}

assign(bpy.data.actions['WD_Idle']);scene.frame_set(1);scene.frame_start=1;scene.frame_end=61
rig['animation_notes']='In-place walk: nominal 1.20 m/s; horn impact at 0.70 s; root cast release at 0.55 s. Gameplay owns damage.'
for o in bpy.context.selected_objects:o.select_set(False)
for o in [rig]+parts:o.select_set(True)
bpy.context.view_layer.objects.active=rig
blend=OUT/'WoodDeer_Combat.blend';fbx=ASSET/'SM_WoodDeer_Combat.fbx'
bpy.ops.wm.save_as_mainfile(filepath=str(blend))
bpy.ops.export_scene.fbx(filepath=str(fbx),use_selection=True,object_types={'ARMATURE','MESH'},
    axis_forward='-Z',axis_up='Y',apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',
    add_leaf_bones=False,use_armature_deform_only=False,armature_nodetype='NULL',
    use_mesh_modifiers=True,mesh_smooth_type='FACE',bake_anim=True,bake_anim_use_nla_strips=False,
    bake_anim_use_all_actions=True,bake_anim_force_startend_keying=True,bake_anim_step=1,
    bake_anim_simplify_factor=0,path_mode='COPY',embed_textures=False)
report={'status':'BLENDER_SKIN_AND_MOTION_COMPLETE_PENDING_UNITY_IMPORT_AND_GAMEPLAY_REVIEW',
    'provisional':True,'source':str(SOURCE),'sourceSha256Before':source_hash,
    'sourceSha256After':hashlib.sha256(SOURCE.read_bytes()).hexdigest(),'blend':str(blend),'fbx':str(fbx),
    'nativeAxes':{'up':'+Z','forward':'-Y'},'fbxAxes':{'up':'+Y','forward':'-Z export convention; Unity gameplay +Z'},
    'parts':source_stats,'triangles':sum(s['triangles'] for s in source_stats.values()),
    'boneCount':len(bone_rows),'deformBoneCount':sum(r['deform'] for r in bone_rows.values()),'bones':bone_rows,
    'weights':weight_stats,'bindPoseMaxVertexErrorM':bind_errors,'bindPoseBounds':bind_bounds,'animations':actions,
    'recommendedImpactSeconds':{'WD_HornAttack':.70,'WD_RootCast':.55},'walkNominalSpeedMps':1.20,
    'ikReachClampedBoneFrames':reach_warnings,'motionChecks':motion_checks,
    'blenderObjectLocalTransforms':{o.name:{'position':list(o.location),'rotationQuaternionWxyz':list(o.rotation_quaternion),'rotationEulerRadians':list(o.rotation_euler),'scale':list(o.scale),'parent':o.parent.name if o.parent else None} for o in [rig]+parts},
    'limitations':['Procedural provisional motion; final anatomy and animation polish pending.',
       'No Unity import, Avatar/Animator, NavMesh, hitbox, damage, pause, replacement or gameplay checks were run.',
       'Damage is not embedded in animation events; caller must schedule one hit using the recommended cue time.']}
(OUT/'rig_report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
assert source_hash==report['sourceSha256After']
assert max(bind_errors.values())<.00001
assert all(s['maxInfluences']<=4 and not s['unweightedVertices'] and s['maxWeightSumError']<.00001 for s in weight_stats.values())
assert all(s['finite'] for s in motion_checks.values())
print('WOOD_DEER_COMBAT_EXPORTED '+json.dumps({'bones':len(bone_rows),'triangles':report['triangles'],'bindErrors':bind_errors,'clamped':reach_warnings}),flush=True)
