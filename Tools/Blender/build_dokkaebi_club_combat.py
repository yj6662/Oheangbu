"""Approved hornless DokkaebiClub: real humanoid skin and baked combat actions.

Run Blender 5 background with --python this_file.py -- --inspect / --build / --verify.
Only writes this task's Art/Demo/Summons/DokkaebiClub and Unity derivative folders.
Never saves the approved source. No Unity calls or paid services.
"""
import bpy, math, json, hashlib, shutil, sys
from pathlib import Path
from mathutils import Vector, Matrix, Quaternion, Euler
from mathutils.kdtree import KDTree

ROOT = Path('C:/Users/yj666/Oheangbu')
SOURCE = ROOT/'Art/SpellVFX120/DokkaebiClub/DokkaebiClub_Working.blend'
OUT = ROOT/'Art/Demo/Summons/DokkaebiClub'
ASSET = ROOT/'Oheangbu/Assets/_Project/Art/Demo/Summons/DokkaebiClub'
BODY_NAME = 'DokkaebiClub_Body'
RIG_NAME = 'DokkaebiClub_CombatRig'
LEGS = ('Leg_L', 'Leg_R')

def write_report(name, value):
    OUT.mkdir(parents=True, exist_ok=True)
    (OUT/name).write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding='utf-8')

def bounds(points):
    return [[min(p[i] for p in points), max(p[i] for p in points)] for i in range(3)]

def components(mesh, ids):
    ids = set(ids); adjacent = {i: [] for i in ids}
    for edge in mesh.edges:
        a,b = edge.vertices
        if a in ids and b in ids:
            adjacent[a].append(b); adjacent[b].append(a)
    result = []
    while ids:
        stack = [ids.pop()]; found = []
        while stack:
            i = stack.pop(); found.append(i)
            for j in adjacent[i]:
                if j in ids: ids.remove(j); stack.append(j)
        result.append(found)
    return sorted(result, key=len, reverse=True)

def inspect():
    before = hashlib.sha256(SOURCE.read_bytes()).hexdigest()
    bpy.ops.wm.open_mainfile(filepath=str(SOURCE))
    body = bpy.data.objects[BODY_NAME]; mesh = body.data
    mesh.calc_loop_triangles()
    sections=[]
    for height in (.08,.15,.30,.50,.70,.90,1.05,1.25,1.45,1.7):
        rows=[]
        for ids in components(mesh, (v.index for v in mesh.vertices if v.co.z<height)):
            if len(ids)<8: continue
            points=[mesh.vertices[i].co for i in ids]
            top=[p for p in points if p.z>height-.09]
            low=[p for p in points if p.z<min(q.z for q in points)+.04]
            centroid=lambda ps:list(sum(ps,Vector())/len(ps)) if ps else None
            rows.append({'n':len(ids),'bounds':bounds(points),'centroid':centroid(points),
                         'topCentroid':centroid(top),'bottomCentroid':centroid(low)})
        sections.append({'height':height,'components':rows})
    report={'source':str(SOURCE),'sha256':before,'body':{
        'vertices':len(mesh.vertices),'triangles':len(mesh.loop_triangles),'materialCount':len(mesh.materials),
        'location':list(body.location),'rotation':list(body.rotation_euler),'scale':list(body.scale),
        'bounds':bounds([v.co for v in mesh.vertices]),'modifiers':[m.type for m in body.modifiers],
        'vertexGroups':[v.name for v in body.vertex_groups]},'sections':sections,
        'islands':[{'n':len(ids),'bounds':bounds([mesh.vertices[i].co for i in ids])}
                   for ids in components(mesh,range(len(mesh.vertices)))],
        'images':[{'name':i.name,'path':i.filepath} for i in bpy.data.images]}
    write_report('native_mesh_data.json',{'vertices':[list(v.co) for v in mesh.vertices],
        'edges':[list(e.vertices) for e in mesh.edges],'faces':[list(p.vertices) for p in mesh.polygons]})
    write_report('native_geometry_inspection.json',report)
    assert hashlib.sha256(SOURCE.read_bytes()).hexdigest()==before
    print('DOKKAEBI_NATIVE_INSPECTION '+json.dumps(report),flush=True)

def smooth(a,b,x):
    u=max(0,min(1,(x-a)/(b-a))); return u*u*(3-2*u)

def segment_distance(p,a,b):
    d=b-a; t=max(0,min(1,(p-a).dot(d)/max(1e-12,d.length_squared)))
    return (p-(a+d*t)).length

def normalized(weights):
    keep=sorted(((n,w) for n,w in weights.items() if w>1e-7),key=lambda q:-q[1])[:4]
    total=sum(w for _,w in keep)
    assert total>0
    return {n:w/total for n,w in keep}

def assign(rig,action):
    rig.animation_data_create(); rig.animation_data.action=action
    if action:
        slot=next(iter(action.slots),None) or action.slots.new(id_type='OBJECT',name=rig.name)
        rig.animation_data.action_slot=slot

def evaluated(obj):
    ev=obj.evaluated_get(bpy.context.evaluated_depsgraph_get()); mesh=ev.to_mesh()
    try: return [ev.matrix_world@v.co for v in mesh.vertices]
    finally: ev.to_mesh_clear()

def uv_hash(mesh):
    import struct
    digest=hashlib.sha256()
    for uv in mesh.uv_layers:
        digest.update(uv.name.encode())
        for p in uv.data: digest.update(struct.pack('<2f',*p.uv))
    return digest.hexdigest()


def build():
    OUT.mkdir(parents=True,exist_ok=True); ASSET.mkdir(parents=True,exist_ok=True)
    source_hash=hashlib.sha256(SOURCE.read_bytes()).hexdigest()
    bpy.ops.wm.open_mainfile(filepath=str(SOURCE))
    scene=bpy.context.scene; scene.render.fps=30; scene.unit_settings.system='METRIC'; scene.unit_settings.scale_length=1
    body=bpy.data.objects[BODY_NAME]; mesh=body.data; original=[v.co.copy() for v in mesh.vertices]
    original_uv=uv_hash(mesh); mesh.calc_loop_triangles(); tris=len(mesh.loop_triangles)
    assert tris==16653 and len(original)==8320 and not body.modifiers and not body.vertex_groups
    assert body.matrix_world==Matrix.Identity(4)
    for obj in list(bpy.data.objects):
        if obj!=body: bpy.data.objects.remove(obj,do_unlink=True)
    for action in list(bpy.data.actions): bpy.data.actions.remove(action)
    body.animation_data_clear(); body.hide_set(False); body.hide_render=False
    body['derivative_status']='PROVISIONAL_REAL_HUMANOID_SKIN_FINAL_POLISH_DEFERRED'
    # Anatomical left/right correspond to Unity actor +X right = native -X.
    chains={
        'Leg_L':{'hip':(.350,.275,.690),'knee':(.400,.280,.375),'foot':(.505,.320,.115),'parent':'Pelvis','end':'Foot','phase':0},
        'Leg_R':{'hip':(-.045,.225,.690),'knee':(-.090,.190,.370),'foot':(-.150,.180,.115),'parent':'Pelvis','end':'Foot','phase':.5},
        'Arm_L':{'hip':(.670,.205,1.355),'knee':(.865,.180,.990),'foot':(.864,.230,.540),'parent':'Chest','end':'Hand'},
        'Arm_R':{'hip':(-.610,.170,1.355),'knee':(-.850,.145,1.030),'foot':(-.830,.110,.790),'parent':'Chest','end':'Hand'},
    }
    labels={}; soles={}; paws={}; limb_rows={}
    low=components(mesh,(i for i,p in enumerate(original) if p.z<.5))
    for ids in low:
        if len(ids)<300: continue
        center=sum((original[i] for i in ids if original[i].z<.08),Vector())/max(1,sum(original[i].z<.08 for i in ids))
        if center.y<-.15: continue  # genuine club end, not a third foot
        label=min(LEGS,key=lambda n:(center-Vector(chains[n]['foot'])).length_squared)
        assert label not in soles
        for i in ids: labels[i]=label
        z=min(original[i].z for i in ids)
        soles[label]=[i for i in ids if original[i].z<z+.025]
        paws[label]=[i for i in ids if original[i].z<.155]
        chains[label]['neutral']=list(chains[label]['foot']); chains[label]['neutral'][2]+= .004-z
        limb_rows[label]={'vertices':len(ids),'sourceBounds':bounds([original[i] for i in ids]),'soleMinimumZ':z}
    assert set(soles)==set(LEGS)
    # Below the shoulders, source topology reliably separates each arm from torso.
    for ids in components(mesh,(i for i,p in enumerate(original) if p.z<1.26)):
        pts=[original[i] for i in ids]
        if len(ids)>100 and max(p.x for p in pts)<-.09: label='Arm_R'
        elif len(ids)>100 and min(p.x for p in pts)>.60: label='Arm_L'
        else: continue
        for i in ids: labels[i]=label
        limb_rows[label]={'vertices':len(ids),'sourceBounds':bounds(pts)}
    club_ids=[i for i,n in labels.items() if n=='Arm_R' and original[i].z<.685]
    hand_ids=[i for i,n in labels.items() if n=='Arm_R' and .685<=original[i].z<.845]
    low_club=[i for i in club_ids if original[i].z<.025]
    club_tip=sum((original[i] for i in low_club),Vector())/len(low_club)
    grip=Vector(chains['Arm_R']['foot']); club_vector=club_tip-grip; club_length=club_vector.length
    bpy.ops.object.select_all(action='DESELECT')
    data=bpy.data.armatures.new('DokkaebiClub_CombatSkeleton'); rig=bpy.data.objects.new(RIG_NAME,data)
    scene.collection.objects.link(rig); rig.select_set(True); rig.show_in_front=True; bpy.context.view_layer.objects.active=rig
    bpy.ops.object.mode_set(mode='EDIT'); bones={}
    def bone(name,head,tail,parent=None,deform=True):
        b=data.edit_bones.new(name); b.head=head; b.tail=tail; b.use_deform=deform
        if parent:b.parent=data.edit_bones[parent]
        bones[name]={'head':list(head),'tail':list(tail),'parent':parent,'deform':deform}
    bone('Root',(0,0,0),(0,0,.18),deform=False)
    bone('Pelvis',(.140,.245,.720),(.120,.225,.960),'Root')
    bone('Spine',(.120,.225,.960),(.110,.190,1.190),'Pelvis')
    bone('Chest',(.110,.190,1.190),(.100,.160,1.430),'Spine')
    bone('Neck',(.100,.160,1.430),(.105,.100,1.565),'Chest')
    bone('Head',(.105,.100,1.565),(.110,.030,1.855),'Neck')
    for n,r in chains.items():
        bone(n+'_Upper',r['hip'],r['knee'],r['parent'])
        bone(n+'_Lower',r['knee'],r['foot'],n+'_Upper')
        tail=Vector(r['foot'])+Vector((0,-.13,-.02) if r['end']=='Foot' else (0,0,-.13))
        bone(n+'_'+r['end'],r['foot'],tail,n+'_Lower')
    bone('Club',grip,club_tip,'Arm_R_Hand')
    bone('ClubGrip',grip,grip+Vector((0,-.1,0)),'Arm_R_Hand',False)
    bone('ClubTip',club_tip,club_tip+Vector((0,0,.1)),'Club',False)
    bpy.ops.object.mode_set(mode='OBJECT'); rest={b.name:b.matrix_local.copy() for b in data.bones}
    for p in rig.pose.bones:p.rotation_mode='QUATERNION'
    def field(p,names,spread=.06,power=4):
        return normalized({n:1/(spread+segment_distance(p,Vector(bones[n]['head']),Vector(bones[n]['tail'])))**power for n in names})
    weights=[]; pinned=set(club_ids+hand_ids)
    for ids in paws.values():pinned.update(ids)
    for i,p in enumerate(original):
        n=labels.get(i); w=field(p,['Pelvis','Spine','Chest','Neck','Head'])
        if p.z>1.47:
            head=smooth(1.47,1.64,p.z); w={k:v*(1-head) for k,v in w.items()};w['Head']=w.get('Head',0)+head
        if n:
            r=chains[n]; end=n+'_'+r['end']; limb=field(p,[n+'_Upper',n+'_Lower'],.05)
            if n.startswith('Leg'):
                f=1-smooth(.155,.305,p.z); blend=1-smooth(.46,.73,p.z)
            elif n=='Arm_R': f=1-smooth(.845,1.025,p.z);blend=1-smooth(1.18,1.42,p.z)
            else: f=1-smooth(.65,.88,p.z);blend=1-smooth(1.18,1.42,p.z)
            limb={k:v*(1-f) for k,v in limb.items()};limb[end]=f
            w={k:v*(1-blend) for k,v in w.items()}
            for k,v in limb.items():w[k]=w.get(k,0)+v*blend
        elif p.z<1.46 and abs(p.x-.10)>.42:
            n='Arm_R' if p.x<0 else 'Arm_L';blend=(1-smooth(1.30,1.55,p.z))*smooth(.42,.67,abs(p.x-.10))
            w={k:v*(1-blend) for k,v in w.items()};w[n+'_Upper']=blend
        if i in club_ids:w={'Club':1}
        elif i in hand_ids:w={'Arm_R_Hand':1}
        weights.append(normalized(w))
    adjacency=[[] for _ in original]
    for edge in mesh.edges:
        a,b=edge.vertices;adjacency[a].append(b);adjacency[b].append(a)
    for iteration in range(10):
        new=[]
        for i,w in enumerate(weights):
            if i in pinned or not adjacency[i]:new.append(w);continue
            mixed={n:v*.70 for n,v in w.items()}
            for j in adjacency[i]:
                for n,v in weights[j].items():mixed[n]=mixed.get(n,0)+.30*v/len(adjacency[i])
            label=labels.get(i)
            if label:mixed={n:v for n,v in mixed.items() if not n.startswith(('Leg_','Arm_')) or n.startswith(label+'_')}
            new.append(normalized(mixed))
        weights=new
    for n,row in bones.items():
        if row['deform']:body.vertex_groups.new(name=n)
    for i,w in enumerate(weights):
        for n,v in w.items():body.vertex_groups[n].add([i],v,'REPLACE')
    modifier=body.modifiers.new('DokkaebiClub_ActualSkin','ARMATURE');modifier.object=rig
    modifier.use_deform_preserve_volume=False;body.parent=rig;body.matrix_parent_inverse=Matrix.Identity(4)
    def rotate(name,angle):
        rig.pose.bones[name].rotation_quaternion=Quaternion(rest[name].to_quaternion().inverted()@Vector((0,0,1)),angle)
    def segment(name,start,end):
        old=Vector(bones[name]['tail'])-Vector(bones[name]['head'])
        q=old.rotation_difference(end-start)@rest[name].to_quaternion()
        rig.pose.bones[name].matrix=Matrix.Translation(start)@q.to_matrix().to_4x4()
    def solve(name,target,rotation=None,pole=None):
        row=chains[name];parent=rig.pose.bones[row['parent']].matrix@rest[row['parent']].inverted()
        hip=parent@Vector(row['hip']); rh,rk,rf=map(Vector,(row['hip'],row['knee'],row['foot']))
        a=(rk-rh).length;b=(rf-rk).length;axis=Vector(target)-hip;raw=axis.length;axis.normalize()
        d=max(abs(a-b)+1e-5,min(a+b-1e-5,raw));end=hip+axis*d
        if pole is None:
            ra=(rf-rh).normalized();bend=rk-rh-ra*(rk-rh).dot(ra);bend=ra.rotation_difference(axis)@bend
        else:bend=Vector(pole)-hip
        bend-=axis*bend.dot(axis)
        if bend.length<1e-6:bend=Vector((0,-1,0))-axis*axis.dot(Vector((0,-1,0)))
        bend.normalize();along=(a*a-b*b+d*d)/(2*d)
        knee=hip+axis*along+bend*math.sqrt(max(0,a*a-along*along))
        segment(name+'_Upper',hip,knee);bpy.context.view_layer.update()
        segment(name+'_Lower',knee,end);bpy.context.view_layer.update()
        q=rest[name+'_'+row['end']].to_quaternion()
        if rotation is not None:q=rotation@q
        rig.pose.bones[name+'_'+row['end']].matrix=Matrix.Translation(end)@q.to_matrix().to_4x4()
        return abs(d-raw)
    def desired_hand(tip):
        r=chains['Arm_R'];parent=rig.pose.bones['Chest'].matrix@rest['Chest'].inverted()
        shoulder=parent@Vector(r['hip']);tip=Vector(tip);delta=tip-shoulder;distance=delta.length;axis=delta.normalized()
        # Sphere intersection enforces both rigid club and unchanged arm length.
        a=(Vector(r['knee'])-Vector(r['hip'])).length;b=(Vector(r['foot'])-Vector(r['knee'])).length
        reach=(a+b)*.94
        assert abs(reach-club_length)<distance<reach+club_length,('Unreachable sweep',distance,reach,club_length,list(tip))
        along=(reach*reach-club_length*club_length+distance*distance)/(2*distance)
        perpendicular=Vector((0,0,1))-axis*axis.z;perpendicular.normalize()
        hand=shoulder+axis*along+perpendicular*math.sqrt(max(0,reach*reach-along*along))
        return hand,club_vector.rotation_difference(tip-hand)
    gait={'stride':.36,'duration':.9,'stance':.65,'lift':.085};nominal=gait['stride']/(gait['duration']*gait['stance'])
    actions=[];ik={};sweep=[];radius=1.30;strike_height=.85
    for name,seconds,loop in [('DC_Idle',2.,True),('DC_Walk',.9,True),('DC_Swing',1.7,False)]:
        action=bpy.data.actions.new(name);action.use_fake_user=True;assign(rig,action);end=round(seconds*30)+1
        previous={};error=0
        for index in range((end-1)*2+1):
            frame=1+index*.5;t=(frame-1)/30;u=t/seconds
            for p in rig.pose.bones:p.matrix_basis=Matrix.Identity(4)
            drop=.055;coil=0;active=0
            if name=='DC_Idle':rotate('Head',.013*math.sin(u*math.tau));rotate('Chest',.005*math.sin(u*math.tau))
            elif name=='DC_Walk':drop=.092-.005*math.cos(u*math.tau*2);rotate('Spine',.018*math.sin(u*math.tau))
            else:
                active=smooth(0,.8,t)*(1-smooth(1.1,1.7,t))
                if t<.8:coil=math.radians(70)*smooth(0,.8,t)
                elif t<=1.1:coil=math.radians(70-90*(t-.8)/.3)
                else:coil=math.radians(-20)*(1-smooth(1.1,1.7,t))
                rotate('Pelvis',coil*.18);rotate('Spine',coil*.42);rotate('Chest',coil*.40)
                rotate('Neck',-coil*.27);rotate('Head',-coil*.25)
                drop+=.025*active
            rig.pose.bones['Pelvis'].location=rest['Pelvis'].to_quaternion().inverted()@Vector((0,0,-drop))
            bpy.context.view_layer.update()
            for n in LEGS:
                r=chains[n];target=Vector(r['neutral'])
                if name=='DC_Walk':
                    phase=(u+r['phase'])%1
                    if phase<gait['stance']:y=-gait['stride']/2+gait['stride']*phase/gait['stance'];lift=0
                    else:
                        phase=(phase-gait['stance'])/(1-gait['stance']);y=gait['stride']/2-gait['stride']*smooth(0,1,phase);lift=gait['lift']*math.sin(math.pi*phase)**1.35
                    target+=Vector((0,y,lift))
                error=max(error,solve(n,target))
            # Free arm counterbalances the weapon and has its own elbow articulation.
            chest_delta=rig.pose.bones['Chest'].matrix@rest['Chest'].inverted()
            left_target=chest_delta@Vector(chains['Arm_L']['foot'])
            if name=='DC_Walk':left_target+=Vector((0,.08*math.sin(u*math.tau),.025))
            elif name=='DC_Swing':left_target+=Vector((0,-.10*active,.12*active))
            error=max(error,solve('Arm_L',left_target,chest_delta.to_quaternion()))
            neutral_hand=grip+Vector((0,0,.035));neutral_rot=Quaternion()
            if name=='DC_Walk':
                neutral_hand+=Vector((0,.045*math.sin(u*math.tau),.015*(1-math.cos(u*math.tau))))
            hand=neutral_hand;hand_rot=neutral_rot
            if name=='DC_Swing' and t>0:
                theta=math.radians(-70 if t<.8 else 70 if t>1.1 else -70+140*(t-.8)/.3)
                desired_tip=Vector((-radius*math.sin(theta),-radius*math.cos(theta),strike_height))
                if t<.8:w=smooth(0,.8,t)
                elif t<=1.1:w=1
                else:w=1-smooth(1.1,1.7,t)
                # During preparation/recovery the reachable endpoint itself moves
                # with the torso coil; solving the final left endpoint too early
                # would demand an impossible arm length from the unturned chest.
                desired_tip=(club_tip+Vector((0,0,.035))).lerp(desired_tip,w)
                attack_hand,attack_rot=desired_hand(desired_tip)
                hand=neutral_hand.lerp(attack_hand,w);hand_rot=neutral_rot.slerp(attack_rot,w)
            error=max(error,solve('Arm_R',hand,hand_rot))
            bpy.context.view_layer.update()
            if name=='DC_Swing' and .7999<=t<=1.1001:
                p=rig.pose.bones['ClubTip'].head
                sweep.append({'time':t,'native':list(p),'unity':[-p.x,p.z,-p.y],
                              'angleDeg':math.degrees(math.atan2(-p.x,-p.y)),'radiusM':math.hypot(p.x,p.y),'heightM':p.z})
            for p in rig.pose.bones:
                q=p.rotation_quaternion.copy()
                if p.name in previous and q.dot(previous[p.name])<0:q.negate();p.rotation_quaternion=q
                previous[p.name]=q
                for channel in ('location','rotation_quaternion','scale'):p.keyframe_insert(channel,frame=frame,group=p.name)
        for layer in action.layers:
            for strip in layer.strips:
                for bag in strip.channelbags:
                    for fc in bag.fcurves:
                        for key in fc.keyframe_points:key.interpolation='LINEAR'
        actions.append({'name':name,'frames':[1,end],'seconds':seconds,'fps':30,'bakeStepFrames':.5,'loop':loop});ik[name]={'maxRequestedTargetErrorM':error}
    textures={}
    for suffix in ('BaseColor','Normal'):
        src=ROOT/'Oheangbu/Assets/_Project/Art/SpellVFX120/DokkaebiClub/Textures'/('T_DokkaebiClub_'+suffix+'.png')
        textures[src.name]={'path':str(src),'sha256':hashlib.sha256(src.read_bytes()).hexdigest()}
        for image in bpy.data.images:
            if image.name in (src.name,src.stem):image.filepath=str(src);image.pack()
    assign(rig,None)
    for p in rig.pose.bones:p.matrix_basis=Matrix.Identity(4)
    bpy.context.view_layer.update();bind_error=max((a-b).length for a,b in zip(original,evaluated(body)))
    report={'status':'PROVISIONAL_BUILD_PENDING_VERIFICATION','source':str(SOURCE),'sourceSha256Before':source_hash,
        'sourceSha256After':hashlib.sha256(SOURCE.read_bytes()).hexdigest(),'blend':str(OUT/'DokkaebiClub_Combat.blend'),
        'fbx':str(ASSET/'SM_DokkaebiClub_Combat.fbx'),'vertices':len(original),'triangles':tris,'rendererCount':1,'materialCount':len(mesh.materials),
        'uvHashBefore':original_uv,'uvHashAfter':uv_hash(mesh),'bindPoseMaxVertexErrorM':bind_error,
        'boneCount':len(bones),'deformBoneCount':sum(v['deform'] for v in bones.values()),'bones':bones,'chains':chains,
        'soleVertexIndices':soles,'pawVertexIndices':paws,'clubVertexIndices':club_ids,'handVertexIndices':hand_ids,
        'bodyWithoutClubVertexIndices':[i for i in range(len(original)) if i not in set(club_ids)],
        'weights':{'maxInfluences':max(len(w) for w in weights),'unweightedVertices':sum(not w for w in weights),
                  'maxWeightSumError':max(abs(sum(w.values())-1) for w in weights)},
        'sourceLimbs':limb_rows,'animations':actions,'gait':gait,'walkNominalSpeedMps':nominal,'ik':ik,
        'gripSourceNative':list(grip),'clubTipSourceNative':list(club_tip),'clubGripToTipLengthM':club_length,
        'requestedSweep':{'startSeconds':.8,'centerSeconds':.95,'endSeconds':1.1,'recoveryEndSeconds':1.7,
                          'anglesDeg':[-70,0,70],'radiusM':radius,'heightM':strike_height},'authoredSweepSamples':sweep,
        'nativeAxes':{'up':'+Z','forward':'-Y'},'expectedUnityPointConversion':'(-nativeX, nativeZ, -nativeY)',
        'rootMotionContract':'Armature object and Root identity. Pelvis has vertical posture changes and yaw, zero horizontal translation. Manager owns world travel.',
        'weaponStructure':'One connected approved mesh preserved without cuts. Existing club vertices rigidly weighted to Club; hand to Arm_R_Hand; Club is a rest-transform-only child of Hand, so grip/weapon share the same deformation. No new geometry or UV edits.',
        'sourceTextureHashes':textures,'limitations':['Provisional procedural humanoid motion; final model/animation polish deferred. Thick source shoulder ornaments limit extreme joint folding.',
            'The club has no topologically separate complete island; retained unified geometry to avoid a fabricated split boundary.',
            'Native flat ground and Blender FBX roundtrip only. Unity importer, terrain support, actual collision and damage are parent-owned.']}
    write_report('rig_report.json',report)
    assert bind_error<1e-5 and original_uv==report['uvHashAfter'] and source_hash==report['sourceSha256After']
    assert max(v['maxRequestedTargetErrorM'] for v in ik.values())<.001,ik
    assign(rig,bpy.data.actions['DC_Idle']);scene.frame_set(1);scene.frame_start=1;scene.frame_end=61
    for obj in bpy.context.selected_objects:obj.select_set(False)
    for obj in (rig,body):obj.select_set(True)
    bpy.context.view_layer.objects.active=rig
    bpy.ops.wm.save_as_mainfile(filepath=report['blend'])
    bpy.ops.export_scene.fbx(filepath=report['fbx'],use_selection=True,object_types={'ARMATURE','MESH'},axis_forward='-Z',axis_up='Y',
        apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',add_leaf_bones=False,use_armature_deform_only=False,
        armature_nodetype='NULL',use_mesh_modifiers=True,mesh_smooth_type='FACE',bake_anim=True,bake_anim_use_nla_strips=False,
        bake_anim_use_all_actions=True,bake_anim_force_startend_keying=True,bake_anim_step=.5,bake_anim_simplify_factor=0,
        path_mode='ABSOLUTE',embed_textures=False)
    print('DOKKAEBI_COMBAT_EXPORTED '+json.dumps({'bones':len(bones),'tris':tris,'ik':ik,'sweep':sweep[::9]}),flush=True)


if __name__=='__main__':
    mode=sys.argv[-1]
    if mode=='--inspect':inspect()
    elif mode=='--build':build()
