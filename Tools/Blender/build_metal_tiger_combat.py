"""Approved-model derivative: actual quadruped skin and baked combat actions.

Run Blender 5 background with --python this_file.py -- --inspect / --build / --verify.
Only writes this task's Art/Demo/Summons/MetalTiger and Unity derivative folders.
Never saves the approved source. No Unity calls or paid services.
"""
import bpy, math, json, hashlib, shutil, sys
from pathlib import Path
from mathutils import Vector, Matrix, Quaternion, Euler
from mathutils.kdtree import KDTree

ROOT = Path('C:/Users/yj666/Oheangbu')
SOURCE = ROOT/'Art/SpellVFX120/MetalTiger/MetalTiger_Working.blend'
OUT = ROOT/'Art/Demo/Summons/MetalTiger'
ASSET = ROOT/'Oheangbu/Assets/_Project/Art/Demo/Summons/MetalTiger'
BODY_NAME = 'MetalTiger_Body'
RIG_NAME = 'MetalTiger_CombatRig'
LEGS = ('Fore_L', 'Fore_R', 'Hind_L', 'Hind_R')

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
    for height in (.08,.15,.25,.35,.45,.55,.65,.75,.85,.95,1.05,1.2):
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
        'vertexGroups':[v.name for v in body.vertex_groups]},'sections':sections}
    write_report('native_geometry_inspection.json',report)
    assert hashlib.sha256(SOURCE.read_bytes()).hexdigest()==before
    print('TIGER_NATIVE_INSPECTION '+json.dumps(report),flush=True)

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
    scene=bpy.context.scene; scene.render.fps=30; scene.render.fps_base=1
    scene.unit_settings.system='METRIC'; scene.unit_settings.scale_length=1
    body=bpy.data.objects[BODY_NAME]; mesh=body.data
    original=[v.co.copy() for v in mesh.vertices]; original_uv_hash=uv_hash(mesh)
    mesh.calc_loop_triangles(); original_tris=len(mesh.loop_triangles)
    assert original_tris==12414 and len(mesh.vertices)==6204
    assert not body.modifiers and not body.vertex_groups and body.matrix_world==Matrix.Identity(4)
    for obj in list(bpy.data.objects):
        if obj!=body: bpy.data.objects.remove(obj,do_unlink=True)
    for action in list(bpy.data.actions): bpy.data.actions.remove(action)
    body.hide_set(False); body.hide_render=False; body.animation_data_clear()
    body['derivative_status']='PROVISIONAL_REAL_COMBAT_SKIN_FINAL_MODEL_POLISH_DEFERRED'

    # These are native-source anatomical joints, NOT flattened neutral targets.
    # Native +Z up / -Y forward. L/R follow the established deer naming convention.
    legs={
        'Fore_L':{'hip':(-.235,-.450,.860),'knee':(-.275,-.545,.430),'foot':(-.340,-.660,.125),
                  'neutral':(-.340,-.660,.125),'parent':'Chest','phase':0.0},
        'Fore_R':{'hip':(-.035,-.625,.900),'knee':(-.005,-.735,.440),'foot':(-.040,-.900,.125),
                  'neutral':(-.040,-.865,.125),'parent':'Chest','phase':.5},
        'Hind_L':{'hip':(.055,.520,.870),'knee':(.080,.795,.340),'foot':(.035,.740,.125),
                  'neutral':(.035,.735,.125),'parent':'Pelvis','phase':.75},
        'Hind_R':{'hip':(.305,.330,.860),'knee':(.370,.490,.335),'foot':(.330,.350,.125),
                  'neutral':(.330,.350,.125),'parent':'Pelvis','phase':.25},
    }
    # Exact topology cut isolates four source limbs below the torso/whiskers.
    limb_components=[ids for ids in components(mesh,(v.index for v in mesh.vertices if v.co.z<.45))
                     if len(ids)>150 and min(mesh.vertices[i].co.z for i in ids)<.03]
    assert len(limb_components)==4
    labels={}; sole_sets={}; paw_sets={}; source_limb_rows={}
    for ids in limb_components:
        low=[mesh.vertices[i].co for i in ids if mesh.vertices[i].co.z<.10]
        centre=sum(low,Vector())/len(low)
        label=min(legs,key=lambda n:(centre-Vector(legs[n]['foot'])).length_squared)
        assert label not in sole_sets
        for i in ids: labels[i]=label
        minimum=min(mesh.vertices[i].co.z for i in ids)
        sole_sets[label]=[i for i in ids if mesh.vertices[i].co.z<minimum+.025]
        paw_sets[label]=[i for i in ids if mesh.vertices[i].co.z<.155]
        # The low curled tail is a fifth component (minimum Z=.0515), explicitly
        # excluded by the .03m sole condition. The old static anchors included it.
        legs[label]['neutral']=list(legs[label]['neutral'])
        legs[label]['neutral'][2]=legs[label]['foot'][2]-minimum+.004
        source_limb_rows[label]={'componentVertices':len(ids),'sourceMinimumZ':minimum,
                                'soleVertices':len(sole_sets[label]),'pawVertices':len(paw_sets[label]),
                                'sourceBounds':bounds([original[i] for i in ids])}
    assert set(sole_sets)==set(LEGS)
    bpy.ops.object.select_all(action='DESELECT')
    data=bpy.data.armatures.new('MetalTiger_CombatSkeleton')
    rig=bpy.data.objects.new(RIG_NAME,data); scene.collection.objects.link(rig)
    rig.show_in_front=True; rig.select_set(True); bpy.context.view_layer.objects.active=rig
    bpy.ops.object.mode_set(mode='EDIT'); bone_rows={}
    def bone(name,head,tail,parent=None,deform=True):
        b=data.edit_bones.new(name); b.head=head; b.tail=tail; b.use_deform=deform
        if parent: b.parent=data.edit_bones[parent]
        bone_rows[name]={'head':list(head),'tail':list(tail),'parent':parent,'deform':deform}
    bone('Root',(0,0,0),(0,0,.20),deform=False)
    bone('Pelvis',(.155,.545,.930),(.105,.185,.965),'Root')
    bone('Spine',(.105,.185,.965),(-.090,-.380,1.025),'Pelvis')
    bone('Chest',(-.090,-.380,1.025),(-.145,-.655,1.090),'Spine')
    bone('Neck',(-.145,-.655,1.090),(-.205,-.965,1.100),'Chest')
    bone('Head',(-.205,-.965,1.100),(-.220,-1.360,1.015),'Neck')
    bone('Tail',(.250,.725,.935),(.285,.965,.470),'Pelvis')
    bone('TailMid',(.285,.965,.470),(.395,1.280,.155),'Tail')
    bone('TailTip',(.395,1.280,.155),(.430,1.360,.430),'TailMid')
    for name,row in legs.items():
        bone(name+'_Upper',row['hip'],row['knee'],row['parent'])
        bone(name+'_Lower',row['knee'],row['foot'],name+'_Upper')
        bone(name+'_Hoof',row['foot'],Vector(row['foot'])+Vector((0,-.14,-.03)),name+'_Lower')
    bone('MouthOrigin',(-.225,-1.485,.930),(-.225,-1.650,.930),'Head',False)
    for side in ('L','R'):
        name='Fore_'+side; ids=paw_sets[name]; front=min(original[i].y for i in ids)
        tips=[original[i] for i in ids if original[i].y<front+.035]
        claw=sum(tips,Vector())/len(tips)
        bone('Claw_'+side,claw,claw+Vector((0,-.12,0)),name+'_Hoof',False)
    bone('LeapImpact',(-.180,-1.160,.340),(-.180,-1.320,.340),'Chest',False)
    bpy.ops.object.mode_set(mode='OBJECT')
    for p in rig.pose.bones: p.rotation_mode='QUATERNION'
    rest={b.name:b.matrix_local.copy() for b in data.bones}
    def segment_weights(p,names,spread=.055,power=3):
        raw={n:1/(spread+segment_distance(p,Vector(bone_rows[n]['head']),Vector(bone_rows[n]['tail'])))**power for n in names}
        return normalized(raw)
    def nearest_leg(p):
        return min(legs,key=lambda n:segment_distance(p,Vector(legs[n]['hip']),Vector(legs[n]['knee'])))
    tail_labels=set()
    for ids in components(mesh,(v.index for v in mesh.vertices if v.co.z<.75)):
        if min(original[i].y for i in ids)>.80: tail_labels.update(ids)
    weights=[]
    for vertex in mesh.vertices:
        p=vertex.co
        if vertex.index in tail_labels or p.y>.78 and p.x>.19:
            w=segment_weights(p,['Tail','TailMid','TailTip'],.045,4)
        else:
            w=segment_weights(p,['Pelvis','Spine','Chest','Neck','Head'])
            # Preserve the tiger's face/whiskers as a coherent head; no new jaw.
            head=smooth(.76,1.07,p.z)*smooth(.63,.98,-p.y)
            if p.y<-1.12: head=max(head,smooth(.62,.80,p.z))
            w={n:v*(1-head) for n,v in w.items()}; w['Head']=w.get('Head',0)+head
            label=labels.get(vertex.index)
            blend=1-smooth(.36,.88,p.z) if label else 0
            if label is None and p.z<.92:
                label=nearest_leg(p)
                distance=segment_distance(p,Vector(legs[label]['hip']),Vector(legs[label]['knee']))
                blend=(1-smooth(.075,.27,distance))*(1-smooth(.46,.92,p.z))
            if blend>0:
                limb=segment_weights(p,[label+'_Upper',label+'_Lower'],.03,4)
                paw=1-smooth(.16,.32,p.z)
                limb={n:v*(1-paw) for n,v in limb.items()}; limb[label+'_Hoof']=paw
                w={n:v*(1-blend) for n,v in w.items()}
                for n,v in limb.items(): w[n]=w.get(n,0)+v*blend
            w=normalized(w)
        weights.append(w)
    # Smooth the procedural joint fields along real topology, pinning entire paws.
    # Low limbs cannot acquire the opposite limb's weights even when spatially close.
    adjacent=[[] for _ in mesh.vertices]
    for edge in mesh.edges:
        a,b=edge.vertices; adjacent[a].append(b); adjacent[b].append(a)
    pinned={i for ids in paw_sets.values() for i in ids}
    for iteration in range(8):
        next_weights=[]
        for i,w in enumerate(weights):
            if i in pinned or not adjacent[i]: next_weights.append(w); continue
            mixed={n:value*.65 for n,value in w.items()}
            for j in adjacent[i]:
                for n,value in weights[j].items(): mixed[n]=mixed.get(n,0)+.35*value/len(adjacent[i])
            label=labels.get(i)
            if label:
                mixed={n:v for n,v in mixed.items() if not n.startswith(('Fore_','Hind_')) or n.startswith(label+'_')}
            next_weights.append(normalized(mixed))
        weights=next_weights
    for n,row in bone_rows.items():
        if row['deform']: body.vertex_groups.new(name=n)
    for vertex,w in zip(mesh.vertices,weights):
        for n,value in w.items(): body.vertex_groups[n].add([vertex.index],value,'REPLACE')
    modifier=body.modifiers.new('MetalTiger_ActualSkin','ARMATURE'); modifier.object=rig
    modifier.use_deform_preserve_volume=False; body.parent=rig; body.matrix_parent_inverse=Matrix.Identity(4)
    weight_stats={'maxInfluences':max(len(w) for w in weights),'unweightedVertices':sum(not w for w in weights),
                  'maxWeightSumError':max(abs(sum(w.values())-1) for w in weights),
                  'soleMinimumHoofWeight':{n:min(weights[i].get(n+'_Hoof',0) for i in ids) for n,ids in sole_sets.items()}}
    assert all(value>.999 for value in weight_stats['soleMinimumHoofWeight'].values())
    def rotate(name,axis,angle):
        local=rest[name].to_quaternion().inverted()@Vector(axis)
        rig.pose.bones[name].rotation_quaternion=Quaternion(local,angle)
    def body_offset(v): rig.pose.bones['Pelvis'].location=rest['Pelvis'].to_quaternion().inverted()@Vector(v)
    def segment(name,start,end):
        old=Vector(bone_rows[name]['tail'])-Vector(bone_rows[name]['head'])
        q=old.rotation_difference(end-start)@rest[name].to_quaternion()
        rig.pose.bones[name].matrix=Matrix.Translation(start)@q.to_matrix().to_4x4()
    def solve(name,requested,paw_rotation=None,paw_follow=0):
        row=legs[name]; parent=rig.pose.bones[row['parent']].matrix@rest[row['parent']].inverted()
        hip=parent@Vector(row['hip']); requested=Vector(requested)
        rh,rk,rf=map(Vector,(row['hip'],row['knee'],row['foot']))
        a=(rk-rh).length; b=(rf-rk).length; axis=requested-hip; raw=axis.length
        assert raw>1e-6
        axis.normalize(); distance=max(abs(a-b)+1e-5,min(a+b-1e-5,raw)); target=hip+axis*distance
        rest_axis=(rf-rh).normalized(); bend=rk-rh-rest_axis*(rk-rh).dot(rest_axis)
        bend=rest_axis.rotation_difference(axis)@bend; bend-=axis*bend.dot(axis)
        if bend.length<1e-5:
            seed=Vector((1,0,0)) if abs(axis.x)<.8 else Vector((0,1,0))
            bend=seed-axis*seed.dot(axis)
        bend.normalize(); along=(a*a-b*b+distance*distance)/(2*distance)
        knee=hip+axis*along+bend*math.sqrt(max(0,a*a-along*along))
        segment(name+'_Upper',hip,knee); bpy.context.view_layer.update()
        segment(name+'_Lower',knee,target); bpy.context.view_layer.update()
        paw_q=rest[name+'_Hoof'].to_quaternion()
        if paw_rotation is not None:
            lower_delta=rig.pose.bones[name+'_Lower'].matrix.to_quaternion()@rest[name+'_Lower'].to_quaternion().inverted()
            # A lifted feline paw partly follows its forearm. Keeping the source
            # flat-ground paw orientation through a raised swipe folds the wrist
            # excessively and collapses linear skin volume.
            paw_rotation=paw_rotation.slerp(lower_delta,paw_follow)
            paw_q=paw_rotation@paw_q
        rig.pose.bones[name+'_Hoof'].matrix=Matrix.Translation(target)@paw_q.to_matrix().to_4x4()
        return {'error':(requested-target).length,'upperClamp':max(0,raw-(a+b-1e-5)),
                'lowerClamp':max(0,abs(a-b)+1e-5-raw)}
    actions=[]; ik_stats={}
    recipes=[('MT_Idle',2.,True),('MT_Walk',.6,True),('MT_Run',.4,True),
             ('MT_Leap',1.2,False),('MT_ClawLeft',.8,False),('MT_ClawRight',.8,False)]
    gaits={'MT_Walk':{'stride':.42,'duration':.6,'stance':.65,'lift':.095},
           'MT_Run':{'stride':.55,'duration':.4,'stance':.5,'lift':.16}}
    walk_speed=.42/(.6*.65); run_speed=.55/(.4*.5)
    for name,seconds,loop in recipes:
        action=bpy.data.actions.new(name); action.use_fake_user=True; assign(rig,action)
        end=round(seconds*30)+1; metrics={'maxRequestedTargetErrorM':0,'upperClampBoneFrames':0,'lowerClampBoneFrames':0}
        previous={}
        for sample_index in range((end-1)*2+1):
            frame=1+sample_index*.5
            t=(frame-1)/30; u=t/seconds
            for p in rig.pose.bones: p.matrix_basis=Matrix.Identity(4)
            # Root stays exactly at the ground origin. Only Pelvis/body deforms locally;
            # manager owns all horizontal approach/leap travel and damage.
            body_drop=.055; arc=0; air=0; attack_side=0; strike=0
            rotate('Tail',(1,0,0),.055)
            if name=='MT_Idle':
                rotate('Chest',(1,0,0),math.sin(u*math.tau)*.007)
                rotate('Neck',(1,0,0),math.sin(u*math.tau)*.012)
                rotate('Head',(0,0,1),math.sin(u*math.tau)*.012)
                rotate('TailMid',(0,0,1),math.sin(u*math.tau)*.025)
                rotate('TailTip',(0,0,1),math.sin(u*math.tau)*.04)
            elif name in gaits:
                body_drop=(.104 if name=='MT_Walk' else .150)-.006*math.cos(u*math.tau*2)
                rotate('Tail',(1,0,0),.13 if name=='MT_Walk' else .22)
                rotate('Spine',(0,1,0),math.sin(u*math.tau)*.012)
                rotate('Neck',(1,0,0),math.sin(u*math.tau*2)*.018)
                rotate('Head',(1,0,0),-math.sin(u*math.tau*2)*.012)
                rotate('TailMid',(0,0,1),math.sin(u*math.tau)*.04)
                rotate('TailTip',(0,0,1),math.sin(u*math.tau)*.05)
            elif name=='MT_Leap':
                if t<.2: body_drop+=.08*math.sin(math.pi*t/.2)**2
                elif t<.8:
                    air=(t-.2)/.6; arc=.38*math.sin(math.pi*air)
                else: body_drop+=.055*math.sin(math.pi*(t-.8)/.4)**2
                tail_crouch=.20*smooth(0,.10,t)*(1-smooth(.14,.28,t))
                tail_land=.18*smooth(.70,.82,t)*(1-smooth(.98,1.20,t))
                rotate('Tail',(1,0,0),.055+tail_crouch+tail_land)
                rotate('Spine',(1,0,0),.028*math.sin(math.pi*air))
                rotate('Neck',(1,0,0),-.05*math.sin(math.pi*air))
            else:
                attack_side=-1 if name=='MT_ClawLeft' else 1
                strike=smooth(0,.18,t)*(1-smooth(.42,.8,t))
                rotate('Chest',(0,0,1),attack_side*.035*strike)
                rotate('Neck',(1,0,0),.035*strike)
                rotate('Head',(0,0,1),-attack_side*.025*strike)
                body_drop+=.016*strike
                rotate('Tail',(1,0,0),.055+.035*strike)
            body_offset((0,0,-body_drop+arc)); bpy.context.view_layer.update()
            for leg,row in legs.items():
                target=Vector(row['neutral'])
                paw_rotation=None; paw_follow=0
                if name in gaits:
                    gait=gaits[name]; stride=gait['stride']; stance_fraction=gait['stance']
                    phase=(u+(row['phase'] if name=='MT_Walk' else {'Fore_L':0,'Fore_R':.5,'Hind_L':.5,'Hind_R':0}[leg]))%1
                    if phase<stance_fraction:
                        y=-stride/2+stride*phase/stance_fraction; lift=0
                    else:
                        swing=(phase-stance_fraction)/(1-stance_fraction)
                        y=stride/2-stride*smooth(0,1,swing); lift=gait['lift']*math.sin(math.pi*swing)**1.35
                    target+=Vector((0,y,lift))
                elif name=='MT_Leap' and .2<t<.8:
                    tuck=math.sin(math.pi*air)**2
                    target+=Vector((0,(-.09 if leg.startswith('Fore') else .08)*tuck,arc+.085*tuck))
                elif name in ('MT_ClawLeft','MT_ClawRight') and leg==('Fore_L' if name=='MT_ClawLeft' else 'Fore_R'):
                    neutral=Vector(row['neutral']); hip=Vector(row['hip'])
                    coil=neutral+Vector((attack_side*.10,-.10,.33))
                    contact=Vector((hip.x-attack_side*.14,hip.y-.58,.52))
                    follow=contact+Vector((-attack_side*.08,.09,-.095))
                    if t<.18: target=neutral.lerp(coil,smooth(0,.18,t))
                    elif t<.30: target=coil.lerp(contact,smooth(.18,.30,t))
                    elif t<.42: target=contact.lerp(follow,smooth(.30,.42,t))
                    else: target=follow.lerp(neutral,smooth(.42,.8,t))
                    paw_rotation=Quaternion(Vector((1,0,0)),.26*strike)@Quaternion(Vector((0,0,1)),-attack_side*.22*strike)
                    paw_follow=.58*strike
                result=solve(leg,target,paw_rotation,paw_follow)
                metrics['maxRequestedTargetErrorM']=max(metrics['maxRequestedTargetErrorM'],result['error'])
                metrics['upperClampBoneFrames']+=result['upperClamp']>.0001
                metrics['lowerClampBoneFrames']+=result['lowerClamp']>.0001
            bpy.context.view_layer.update()
            for p in rig.pose.bones:
                q=p.rotation_quaternion.copy()
                if p.name in previous and q.dot(previous[p.name])<0: q.negate(); p.rotation_quaternion=q
                previous[p.name]=q
                p.keyframe_insert('location',frame=frame,group=p.name)
                p.keyframe_insert('rotation_quaternion',frame=frame,group=p.name)
                p.keyframe_insert('scale',frame=frame,group=p.name)
        for layer in action.layers:
            for strip in layer.strips:
                for bag in strip.channelbags:
                    for fc in bag.fcurves:
                        for key in fc.keyframe_points: key.interpolation='LINEAR'
        actions.append({'name':name,'frames':[1,end],'seconds':seconds,'fps':30,'bakeStepFrames':.5,'bakedSamplesPerSecond':60,'loop':loop})
        ik_stats[name]=metrics
    # Existing texture references only: this task owns the new Unity FBX, no new
    # material/texture assets or importer mutations. Native packed images stay packed.
    texture_hashes={}
    for suffix in ('BaseColor','Normal'):
        filename='T_MetalTiger_'+suffix+'.png'
        src=ROOT/'Oheangbu/Assets/_Project/Art/SpellVFX120/MetalTiger/Textures'/filename
        texture_hashes[filename]=hashlib.sha256(src.read_bytes()).hexdigest()
        for image in bpy.data.images:
            if image.name in (filename,filename[:-4]): image.filepath=str(src); image.pack()
    assign(rig,None)
    for p in rig.pose.bones: p.matrix_basis=Matrix.Identity(4)
    bpy.context.view_layer.update()
    bind_error=max((a-b).length for a,b in zip(evaluated(body),original))
    report={'status':'PROVISIONAL_NATIVE_BUILD_PENDING_FBX_VERIFICATION','source':str(SOURCE),
        'sourceSha256Before':source_hash,'sourceSha256After':hashlib.sha256(SOURCE.read_bytes()).hexdigest(),
        'blend':str(OUT/'MetalTiger_Combat.blend'),'fbx':str(ASSET/'SM_MetalTiger_Combat.fbx'),
        'nativeAxes':{'up':'+Z','forward':'-Y'},'nativeSourceBounds':bounds(original),
        'vertices':len(mesh.vertices),'triangles':original_tris,'materialCount':len(mesh.materials),
        'uvHashBefore':original_uv_hash,'uvHashAfter':uv_hash(mesh),'bindPoseMaxVertexErrorM':bind_error,
        'boneCount':len(bone_rows),'deformBoneCount':sum(v['deform'] for v in bone_rows.values()),'bones':bone_rows,
        'sourceLimbs':source_limb_rows,'legs':legs,'soleVertexIndices':sole_sets,'pawVertexIndices':paw_sets,
        'weights':weight_stats,'animations':actions,'ik':ik_stats,'walkNominalSpeedMps':walk_speed,
        'runNominalSpeedMps':run_speed,'gaits':gaits,
        'contactCues':{'MT_Leap':{'takeoffSeconds':.2,'takeoffFrame':7,'contactSeconds':.8,'contactFrame':25,'endSeconds':1.2},
                       'MT_ClawLeft':{'contactSeconds':.30,'contactFrame':10,'recoveryEnd':.8,'socket':'Claw_L'},
                       'MT_ClawRight':{'contactSeconds':.30,'contactFrame':10,'recoveryEnd':.8,'socket':'Claw_R'}},
        'rootMotionContract':'Armature object and Root translation/rotation remain identity in every clip. Pelvis owns small vertical posture changes and Leap visual arc (peak .38m); no horizontal translation. Manager alone owns world navigation/leap travel; Animator.applyRootMotion=false.',
        'socketConvention':'Claw_L/R are non-deforming children of Fore_L/R_Hoof at measured native toe tips; MouthOrigin belongs to Head, LeapImpact to Chest. Use position, and actor gameplay +Z for attack direction.',
        'sourceTextureHashes':texture_hashes,
        'limitations':['Provisional procedural quadruped animation; final model and animation polish deferred.',
                      'No jaw or individual toe articulation; original feline face/paws/materials preserved.',
                      'Run is a fast diagonal trot, not a polished feline gallop. Source tail curl and asymmetry preserved.',
                      'Unity import, terrain IK and gameplay are owned by root and not tested here.']}
    assert bind_error<1e-5 and original_uv_hash==report['uvHashAfter'] and source_hash==report['sourceSha256After']
    assert weight_stats['maxInfluences']<=4 and weight_stats['unweightedVertices']==0 and weight_stats['maxWeightSumError']<1e-6
    write_report('rig_report.json',report)
    # Export only after geometric feasibility is established; no silently clamped gait.
    assert max(v['maxRequestedTargetErrorM'] for v in ik_stats.values())<.001, json.dumps(ik_stats)
    assign(rig,bpy.data.actions['MT_Idle']); scene.frame_set(1); scene.frame_start=1; scene.frame_end=61
    rig['animation_notes']='Provisional real skin. In-place walk %.4fm/s; run %.4fm/s. Leap contact .8s, each alternating claw contact .3s. Root zero translation.'%(walk_speed,run_speed)
    for obj in bpy.context.selected_objects: obj.select_set(False)
    for obj in (rig,body): obj.select_set(True)
    bpy.context.view_layer.objects.active=rig
    bpy.ops.wm.save_as_mainfile(filepath=report['blend'])
    bpy.ops.export_scene.fbx(filepath=report['fbx'],use_selection=True,object_types={'ARMATURE','MESH'},
        axis_forward='-Z',axis_up='Y',apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',
        add_leaf_bones=False,use_armature_deform_only=False,armature_nodetype='NULL',
        use_mesh_modifiers=True,mesh_smooth_type='FACE',bake_anim=True,bake_anim_use_nla_strips=False,
        bake_anim_use_all_actions=True,bake_anim_force_startend_keying=True,bake_anim_step=.5,
        bake_anim_simplify_factor=0,path_mode='ABSOLUTE',embed_textures=False)
    print('TIGER_COMBAT_EXPORTED '+json.dumps({'bones':len(bone_rows),'tris':original_tris,'ik':ik_stats,'walkSpeed':walk_speed,'runSpeed':run_speed}),flush=True)

def verify():
    report=json.loads((OUT/'rig_report.json').read_text(encoding='utf-8'))
    bpy.ops.wm.open_mainfile(filepath=report['blend'])
    scene=bpy.context.scene; rig=bpy.data.objects[RIG_NAME]; body=bpy.data.objects[BODY_NAME]
    source=[v.co.copy() for v in body.data.vertices]
    soles=report['soleVertexIndices']; paws=report['pawVertexIndices']; legs=report['legs']
    snapshots={}; metrics={}; initial=None
    def set_time(frame):
        base=math.floor(frame); scene.frame_set(base,subframe=frame-base); bpy.context.view_layer.update()
    for action_row in report['animations']:
        name=action_row['name']; assign(rig,bpy.data.actions[name]); end=action_row['frames'][1]
        rows=[]; first=None; last=None; samples={}
        for sample in range((end-1)*4+1):
            frame=1+sample*.25; set_time(frame); points=evaluated(body)
            t=(frame-1)/30; u=t/action_row['seconds']; is_walk=name in report['gaits']
            foot_rows={}; joint_gap=0; length_error=0; scale_error=0
            for leg in LEGS:
                upper=rig.pose.bones[leg+'_Upper']; lower=rig.pose.bones[leg+'_Lower']; hoof=rig.pose.bones[leg+'_Hoof']
                joint_gap=max(joint_gap,(upper.tail-lower.head).length,(lower.tail-hoof.head).length)
                for p in (upper,lower,hoof):
                    length_error=max(length_error,abs((p.tail-p.head).length-rig.data.bones[p.name].length))
                    scale_error=max(scale_error,max(abs(v-1) for v in p.scale))
                phase_offset=legs[leg]['phase'] if name!='MT_Run' else {'Fore_L':0,'Fore_R':.5,'Hind_L':.5,'Hind_R':0}[leg]
                phase=(u+phase_offset)%1
                planted=not is_walk or .035<=phase<report['gaits'][name]['stance']-.035
                if name=='MT_Leap': planted=t<=.2 or t>=.8
                if name in ('MT_ClawLeft','MT_ClawRight') and leg==('Fore_L' if name=='MT_ClawLeft' else 'Fore_R'):
                    planted=t<.0001 or t>action_row['seconds']-.0001
                # Underside samples are actual source vertices, all weighted exactly
                # to Hoof; no bone-head or bbox-only grounding claims.
                subset=[points[i] for i in soles[leg]]; whole_paw=[points[i] for i in paws[leg]]
                midpoint=sum(source[i].y for i in soles[leg])/len(soles[leg])
                toe=[points[i].z for i in soles[leg] if source[i].y<midpoint]
                heel=[points[i].z for i in soles[leg] if source[i].y>=midpoint]
                root_deform=rig.pose.bones['Root'].matrix@rig.data.bones['Root'].matrix_local.inverted()
                foot_rows[leg]={'planted':planted,'soleMinZ':min(p.z for p in subset),'pawMinZ':min(p.z for p in whole_paw),
                    'toeMinZ':min(toe),'heelMinZ':min(heel),'hoof':list(hoof.head),
                    'pawDeformationBeyondRootM':max((points[i]-root_deform@source[i]).length for i in paws[leg])}
            socket_rows={n:{'nativeRigSpace':list(rig.pose.bones[n].head),
                            'expectedUnityActorLocal':[-rig.pose.bones[n].head.x,rig.pose.bones[n].head.z,-rig.pose.bones[n].head.y]}
                         for n in ('MouthOrigin','Claw_L','Claw_R','LeapImpact')}
            root_position=rig.pose.bones['Root'].matrix.translation
            pelvis_delta=rig.pose.bones['Pelvis'].head-rig.data.bones['Pelvis'].head_local
            rows.append({'time':t,'feet':foot_rows,'jointGapM':joint_gap,'boneLengthErrorM':length_error,'boneScaleError':scale_error,
                         'sockets':socket_rows,'rootPosition':list(root_position),'pelvisDelta':list(pelvis_delta),
                         'armatureObjectPosition':list(rig.location),
                         'bounds':bounds(points),'meshMinZ':min(p.z for p in points),
                         'finite':all(math.isfinite(v) for p in points for v in p)})
            if sample==0:
                first=points
                if name=='MT_Idle':initial=rows[-1]
            if sample==(end-1)*4:last=points
            # Keep a bounded set of genuine surfaces for FBX roundtrip comparison.
            cue=report['contactCues'].get(name,{}).get('contactSeconds',-1)
            if frame in (1,1+(end-1)/2,end) or abs(t-cue)<1e-5 or name=='MT_Leap' and abs(t-.5)<1e-5:
                samples[str(frame)]=points
        snapshots[name]=samples
        feet={}
        for leg in LEGS:
            stance=[r['feet'][leg] for r in rows if r['feet'][leg]['planted']]
            feet[leg]={'stanceSoleMinRangeM':[min(f['soleMinZ'] for f in stance),max(f['soleMinZ'] for f in stance)],
                       'stanceToeMinRangeM':[min(f['toeMinZ'] for f in stance),max(f['toeMinZ'] for f in stance)],
                       'stanceHeelMinRangeM':[min(f['heelMinZ'] for f in stance),max(f['heelMinZ'] for f in stance)],
                       'allPawMinRangeM':[min(r['feet'][leg]['pawMinZ'] for r in rows),max(r['feet'][leg]['pawMinZ'] for r in rows)],
                       'maxPawDeformationBeyondRootM':max(r['feet'][leg]['pawDeformationBeyondRootM'] for r in rows)}
        metrics[name]={'sampleStepFrames':.25,'samples':len(rows),'feet':feet,
            'loopEndSurfaceErrorM':max((a-b).length for a,b in zip(first,last)),
            'maxJointGapM':max(r['jointGapM'] for r in rows),'maxBoneLengthErrorM':max(r['boneLengthErrorM'] for r in rows),
            'maxBoneScaleError':max(r['boneScaleError'] for r in rows),'minimumMeshZ':min(r['meshMinZ'] for r in rows),
            'maxRootTranslationM':max(Vector(r['rootPosition']).length for r in rows),
            'maxArmatureObjectTranslationM':max(Vector(r['armatureObjectPosition']).length for r in rows),
            'maxPelvisHorizontalTranslationM':max(math.hypot(r['pelvisDelta'][0],r['pelvisDelta'][1]) for r in rows),
            'pelvisVerticalRangeM':[min(r['pelvisDelta'][2] for r in rows),max(r['pelvisDelta'][2] for r in rows)],
            'finite':all(r['finite'] for r in rows),'timingSamples':[r for r in rows if any(abs(r['time']-t)<1e-5 for t in (0,.2,.3,.5,.8,1.2))]}
    native_result={'status':'NATIVE_VERIFIED_PENDING_FBX','initialIdle':initial,'animations':metrics,
        'notes':['Soles are measured deformed vertices in native meters (+Z up); expected Unity values use reflection (-X,+Z,-Y).',
                 'The four native paws have sole minimum Z=0..1.72mm. Low curled tail is not a fifth foot; old static clustering incorrectly selected it.',
                 'Leap airborne feet and the active claw are intentionally excluded from support; Root stays at the ground origin.',
                 'Only flat native support verified; external Unity ground and runtime IK remain root-owned.']}
    write_report('native_motion_verification.json',native_result)
    assert all(r['finite'] for r in metrics.values())
    assert all(r['maxRootTranslationM']<1e-7 and r['maxArmatureObjectTranslationM']<1e-7 and r['maxPelvisHorizontalTranslationM']<1e-6 for r in metrics.values()), 'Unexpected root motion'
    assert all(r['maxJointGapM']<.001 and r['maxBoneLengthErrorM']<1e-5 and r['maxBoneScaleError']<1e-5 for r in metrics.values())
    assert all(r['minimumMeshZ']>-.003 for r in metrics.values()), 'Mesh penetrates native ground'
    assert all(r['loopEndSurfaceErrorM']<.0001 for r in metrics.values()), 'Action endpoint discontinuity'
    assert all(f['stanceSoleMinRangeM'][0]>-.002 and f['stanceSoleMinRangeM'][1]<.012 for r in metrics.values() for f in r['feet'].values())
    for name in ('MT_Walk','MT_Run'):
        assert all(f['maxPawDeformationBeyondRootM']>.12 for f in metrics[name]['feet'].values()),'Rigid fake walk'
    # Isolated importer verifies real curves and skin, not just requested export flags.
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=report['fbx'],use_anim=True)
    scene=bpy.context.scene; scene.render.fps=30
    rig=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE'); body=bpy.data.objects[BODY_NAME]
    body.data.calc_loop_triangles(); sums=[sum(g.weight for g in v.groups) for v in body.data.vertices]
    imported_stats={'vertices':len(body.data.vertices),'triangles':len(body.data.loop_triangles),'materialCount':len(body.material_slots),
        'maxInfluences':max(len(v.groups) for v in body.data.vertices),'unweighted':sum(not v.groups for v in body.data.vertices),
        'maxWeightSumError':max(abs(v-1) for v in sums),'boneCount':len(rig.data.bones),
        'skinModifiers':[m.object.name for m in body.modifiers if m.type=='ARMATURE'],
        'localTransforms':{o.name:{'position':list(o.location),'rotation':list(o.rotation_euler),'scale':list(o.scale),'parent':o.parent.name if o.parent else None} for o in (rig,body)}}
    errors={}; clips=[]
    for name,samples in snapshots.items():
        action=next(a for a in bpy.data.actions if a.name==name or a.name.endswith('|'+name)); assign(rig,action)
        curves=[fc for layer in action.layers for strip in layer.strips for bag in strip.channelbags for fc in bag.fcurves]
        clips.append({'name':action.name,'frames':list(action.frame_range),'curves':len(curves),
                      'keys':sum(len(fc.keyframe_points) for fc in curves)})
        errors[name]={}
        for frame,reference in samples.items():
            set_time(float(frame)); points=evaluated(body); tree=KDTree(len(points))
            for i,p in enumerate(points):tree.insert(p,i)
            tree.balance(); errors[name][frame]=max(tree.find(p)[2] for p in reference)
    # Binary FBX scene-axis and node-unit metadata compare against the approved static.
    from io_scene_fbx import parse_fbx
    def decode(value): return value.decode('utf-8','replace').replace('\x00','|') if isinstance(value,bytes) else value
    def properties(element):
        p=next((c for c in element.elems if c.id==b'Properties70'),None)
        return {decode(c.props[0]):[decode(v) for v in c.props[4:]] for c in p.elems} if p else {}
    data,_=parse_fbx.parse(report['fbx'])
    settings=properties(next(c for c in data.elems if c.id==b'GlobalSettings'))
    objects=next(c for c in data.elems if c.id==b'Objects')
    model_rows=[{'name':decode(m.props[1]),'type':decode(m.props[2]),'properties':properties(m)} for m in objects.elems if m.id==b'Model']
    node_forward=Euler((-math.pi/2,0,0)).to_matrix()@Vector((0,-1,0))
    result={'status':'PASS_NATIVE_SKIN_AND_BLENDER_FBX_ROUNDTRIP_PENDING_UNITY','imported':imported_stats,
            'animations':clips,'maxSampledSurfaceErrorM':errors,'sourceUnchanged':hashlib.sha256(SOURCE.read_bytes()).hexdigest()==report['sourceSha256Before'],
            'fbxSha256':hashlib.sha256(Path(report['fbx']).read_bytes()).hexdigest(),'globalSettings':settings,
            'fbxAnimationStacks':[decode(m.props[1]) for m in objects.elems if m.id==b'AnimationStack'],
            'fbxRootAndMeshNodes':[m for m in model_rows if m['type']!='LimbNode'],
            'fbxNodeConvertedForward':list(node_forward),'expectedUnityForward':'+Z',
            'unitContract':'meters; UnitScaleFactor100, FBX model scale1; ModelImporter globalScale1/useFileScale. No extra scale100 wrapper.',
            'nativeMetricsFile':str(OUT/'native_motion_verification.json')}
    assert imported_stats['triangles']==12414 and imported_stats['boneCount']==25
    assert imported_stats['maxInfluences']<=4 and imported_stats['unweighted']==0 and imported_stats['maxWeightSumError']<1e-5
    assert all(e<.001 for action in errors.values() for e in action.values())
    assert all(c['curves']>=25*9 for c in clips) and len(clips)==6 and result['sourceUnchanged']
    assert (node_forward-Vector((0,0,1))).length<1e-5
    write_report('fbx_verification.json',result)
    report['status']=result['status']
    report['verificationFiles']={name:str(OUT/name) for name in ('native_geometry_inspection.json','native_motion_verification.json','fbx_verification.json')}
    report['neutralIdleBoundsNative']=initial['bounds']
    report['attackSocketSamples']={name:[{'seconds':r['time'],'sockets':r['sockets']} for r in metrics[name]['timingSamples']]
                                   for name in ('MT_Leap','MT_ClawLeft','MT_ClawRight')}
    report['neutralFeetExpectedUnityActorLocal']={name:[-row['hoof'][0],row['hoof'][2],-row['hoof'][1]] for name,row in initial['feet'].items()}
    report['neutralBoundsExpectedUnityActorLocal']=[[-initial['bounds'][0][1],-initial['bounds'][0][0]],initial['bounds'][2],[-initial['bounds'][1][1],-initial['bounds'][1][0]]]
    report['fbxSha256']=result['fbxSha256']
    report['visualReview']='WorkBench diagnostic poses require manual review after this numeric pass; no Unity or final art PASS is inferred.'
    write_report('rig_report.json',report)
    print('TIGER_FBX_VERIFIED '+json.dumps({'imported':imported_stats,'clips':clips,'surfaceErrors':errors,'initialIdle':initial}),flush=True)

def render_review():
    # Disposable in-memory camera/floor for shape review; never save or export them.
    report=json.loads((OUT/'rig_report.json').read_text(encoding='utf-8'))
    bpy.ops.wm.open_mainfile(filepath=report['blend'])
    scene=bpy.context.scene; rig=bpy.data.objects[RIG_NAME]
    scene.render.engine='BLENDER_WORKBENCH'; scene.render.resolution_x=900; scene.render.resolution_y=760; scene.render.resolution_percentage=100
    scene.display.shading.light='STUDIO'; scene.display.shading.color_type='SINGLE'; scene.display.shading.single_color=(.36,.33,.30)
    scene.display.shading.show_shadows=True; scene.display.shading.show_cavity=True
    scene.display.shading.cavity_type='BOTH'; scene.display.shading.show_object_outline=True
    scene.display.shading.background_type='WORLD'; scene.world.color=(.15,.15,.15)
    bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,-.008)); plane=bpy.context.object; plane.name='ReviewOnlyGround'
    bpy.ops.object.camera_add(location=(3.6,-4.6,2.55)); camera=bpy.context.object
    camera.data.type='ORTHO'; camera.data.ortho_scale=3.40
    camera.rotation_euler=(Vector((0,-.12,.80))-camera.location).to_track_quat('-Z','Y').to_euler(); scene.camera=camera
    for name,frame,label in [('MT_Idle',1,'idle'),('MT_Walk',8,'walk'),('MT_Run',6,'run'),
                             ('MT_Leap',16,'leap_airborne'),('MT_ClawLeft',10,'claw_left'),('MT_ClawRight',10,'claw_right')]:
        assign(rig,bpy.data.actions[name]); base=math.floor(frame); scene.frame_set(base,subframe=frame-base)
        scene.render.filepath=str(OUT/('review_'+label+'.png')); bpy.ops.render.render(write_still=True)
    camera.location=(-3.6,-2.0,1.4);camera.rotation_euler=(Vector((0,-.12,.70))-camera.location).to_track_quat('-Z','Y').to_euler()
    assign(rig,bpy.data.actions['MT_Idle']);scene.frame_set(1)
    scene.render.filepath=str(OUT/'review_idle_reverse_side.png');bpy.ops.render.render(write_still=True)
    print('TIGER_REVIEW_RENDERED',flush=True)

def finalize_handoff():
    """Read-only source/derivative comparison plus compact authoring metadata."""
    report=json.loads((OUT/'rig_report.json').read_text(encoding='utf-8'))
    verified=json.loads((OUT/'fbx_verification.json').read_text(encoding='utf-8'))
    def material_signature(material):
        nodes=material.node_tree.nodes
        return {'name':material.name,
            'principled':[{'metallic':n.inputs['Metallic'].default_value,'roughness':n.inputs['Roughness'].default_value}
                          for n in nodes if n.type=='BSDF_PRINCIPLED'],
            'normalMapStrength':[n.inputs['Strength'].default_value for n in nodes if n.type=='NORMAL_MAP'],
            'images':[{'name':n.image.name,'path':n.image.filepath,'colorspace':n.image.colorspace_settings.name}
                      for n in nodes if n.type=='TEX_IMAGE' and n.image],
            'links':sorted((l.from_node.name,l.from_socket.name,l.to_node.name,l.to_socket.name) for l in material.node_tree.links)}
    bpy.ops.wm.open_mainfile(filepath=str(SOURCE))
    source_body=bpy.data.objects[BODY_NAME]
    source_material=material_signature(source_body.data.materials[0]); source_uv=uv_hash(source_body.data)
    bpy.ops.wm.open_mainfile(filepath=report['blend'])
    rig=bpy.data.objects[RIG_NAME]; body=bpy.data.objects[BODY_NAME]
    assign(rig,bpy.data.actions['MT_Idle']); bpy.context.scene.frame_set(1); bpy.context.view_layer.update()
    points=evaluated(body)
    paw_points=[points[i] for ids in report['pawVertexIndices'].values() for i in ids]
    native_paw_bounds=bounds(paw_points)
    unity_paw_bounds=[[-native_paw_bounds[0][1],-native_paw_bounds[0][0]],native_paw_bounds[2],[-native_paw_bounds[1][1],-native_paw_bounds[1][0]]]
    material=material_signature(body.data.materials[0])
    assert material==source_material and uv_hash(body.data)==source_uv
    assert hashlib.sha256(SOURCE.read_bytes()).hexdigest()==report['sourceSha256Before']
    assert hashlib.sha256(Path(report['fbx']).read_bytes()).hexdigest()==verified['fbxSha256']
    result={'status':'READY_FOR_UNITY_IMPORT_PROVISIONAL_ART','sourceMaterialPreserved':True,'material':material,
        'sourceUVPreserved':True,'sourceBlendUnchanged':True,'fbxSha256':verified['fbxSha256'],
        'fbx':report['fbx'],'blend':report['blend'],'mesh':BODY_NAME,'armature':RIG_NAME,
        'animations':report['animations'],'walkNominalSpeedMps':report['walkNominalSpeedMps'],'runNominalSpeedMps':report['runNominalSpeedMps'],
        'contactCues':report['contactCues'],'rootMotionContract':report['rootMotionContract'],
        'neutralBoundsExpectedUnityActorLocal':report['neutralBoundsExpectedUnityActorLocal'],
        'neutralPawMeshBoundsExpectedUnityActorLocal':unity_paw_bounds,
        'neutralPawFootprintSizeXZ':[unity_paw_bounds[0][1]-unity_paw_bounds[0][0],unity_paw_bounds[2][1]-unity_paw_bounds[2][0]],
        'neutralPawFootprintOffsetXZ':[(unity_paw_bounds[0][1]+unity_paw_bounds[0][0])/2,(unity_paw_bounds[2][1]+unity_paw_bounds[2][0])/2],
        'neutralFootBonesExpectedUnityActorLocal':report['neutralFeetExpectedUnityActorLocal'],
        'attackSocketSamples':report['attackSocketSamples'],
        'inspectedScreenshots':[str(OUT/('review_'+n+'.png')) for n in ('idle','walk','run','leap_airborne','claw_left','claw_right','idle_reverse_side')],
        'visualReview':{'status':'PROVISIONAL_RIG_DIAGNOSTICS_REVIEWED','findings':[
            'Four actual paws remain planted in neutral pose. Low curled tail was excluded from sole selection.',
            'Walk/run articulate four real skin chains. Run is a diagonal trot, not a polished feline gallop.',
            'Leap visibly raises body and all feet; Root remains at ground origin and contains no horizontal travel.',
            'Left/right swipes articulate different forelimbs. Paw orientation now follows the lifted forearm to reduce wrist collapse.',
            'Angular elbow/carpal creases and shoulder pinching remain in deep flexion; native low-poly anatomy and weights need final manual polish.',
            'Single-color workbench lighting emphasizes pre-existing stripe relief and thin whisker shadow artifacts. No Unity material or in-game visual PASS inferred.']},
        'runtimeNotes':['Disable automatic root motion; manager supplies horizontal travel only.',
                        'Exclude airborne leap (.2,.8) and the active claw from any later ground IK.',
                        'Footprint values are measured paw bounds, without safety padding or collider assumptions.',
                        'Original static Foot_1 was a low tail point; use actual Fore/Hind chains and measured paws instead.']}
    write_report('handoff.json',result)
    report['visualReview']=result['visualReview'];report['handoffFile']=str(OUT/'handoff.json')
    write_report('rig_report.json',report)
    print('TIGER_HANDOFF '+json.dumps({'materialPreserved':True,'pawBounds':unity_paw_bounds,'fullBounds':report['neutralBoundsExpectedUnityActorLocal'],
          'sha256':verified['fbxSha256'],'path':str(OUT/'handoff.json')}),flush=True)

if __name__=='__main__':
    if '--inspect' in sys.argv: inspect()
    elif '--build' in sys.argv: build()
    elif '--verify' in sys.argv: verify()
    elif '--render' in sys.argv: render_review()
    elif '--finalize' in sys.argv: finalize_handoff()
    else: raise SystemExit('Expected --inspect / --build / --verify / --render / --finalize.')
