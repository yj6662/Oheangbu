"""Approved turtle derivative: rigid shell, four skinned limbs, planted water cast.
No paid generation, source overwrite, or Unity calls. Blender 5 --python ...
"""
import bpy, math, json, hashlib, sys
from pathlib import Path
from mathutils import Vector, Matrix, Quaternion
sys.path.insert(0,str(Path(__file__).resolve().parent))
from build_metal_tiger_combat import normalized, segment_distance, uv_hash, evaluated, assign, smooth

ROOT=Path('C:/Users/yj666/Oheangbu')
SOURCE=ROOT/'Art/SpellVFX120/WaterTurtle/WaterTurtle_Working.blend'
OUT=ROOT/'Art/Demo/Summons/WaterTurtle'
ASSET=ROOT/'Oheangbu/Assets/_Project/Art/Demo/Summons/WaterTurtle'
OUT.mkdir(parents=True,exist_ok=True);ASSET.mkdir(parents=True,exist_ok=True)
source_hash=hashlib.sha256(SOURCE.read_bytes()).hexdigest()
bpy.ops.wm.open_mainfile(filepath=str(SOURCE))
scene=bpy.context.scene;scene.render.fps=30;scene.render.fps_base=1
scene.unit_settings.system='METRIC';scene.unit_settings.scale_length=1
body=bpy.data.objects['WaterTurtle_Body'];mesh=body.data
assert body.matrix_world==Matrix.Identity(4) and not body.modifiers and not body.vertex_groups
mesh.calc_loop_triangles();assert len(mesh.loop_triangles)==16648
original=[v.co.copy() for v in mesh.vertices];uv_before=uv_hash(mesh)
for obj in list(bpy.data.objects):
    if obj!=body:bpy.data.objects.remove(obj,do_unlink=True)
for action in list(bpy.data.actions):bpy.data.actions.remove(action)
body.hide_render=False;body.hide_set(False)
data=bpy.data.armatures.new('WaterTurtle_CombatSkeleton');rig=bpy.data.objects.new('WaterTurtle_CombatRig',data)
scene.collection.objects.link(rig);bpy.context.view_layer.objects.active=rig;rig.select_set(True)
bpy.ops.object.mode_set(mode='EDIT');bones={}
def bone(name,head,tail,parent=None,deform=True):
    b=data.edit_bones.new(name);b.head=head;b.tail=tail;b.use_deform=deform
    if parent:b.parent=data.edit_bones[parent]
    bones[name]={'head':list(head),'tail':list(tail),'parent':parent,'deform':deform}
bone('Root',(0,0,0),(0,0,.2),deform=False)
bone('Shell',(0,.35,.7),(0,-.55,.7),'Root')
bone('Neck',(0,-.72,.53),(0,-1.13,.57),'Shell')
bone('Head',(0,-1.13,.57),(0,-1.70,.52),'Neck')
bone('Tail',(0,1.25,.34),(0,1.77,.24),'Shell')
bone('MouthOrigin',(0,-1.81,.94),(0,-1.97,.94),'Head',False)
legs={}
for side,sign in [('L',-1),('R',1)]:
    for prefix,y,fy in [('Fore',-.58,-.81),('Hind',.88,1.13)]:
        name=prefix+'_'+side
        hip=Vector((sign*.65,y,.48));knee=Vector((sign*.91,y+(.12 if prefix=='Hind' else -.1),.29));foot=Vector((sign*.90,fy,.08))
        legs[name]={'hip':hip,'knee':knee,'foot':foot,'phase':{'Fore_L':0,'Hind_R':.25,'Fore_R':.5,'Hind_L':.75}[name]}
        bone(name+'_Upper',hip,knee,'Shell');bone(name+'_Lower',knee,foot,name+'_Upper')
        bone(name+'_Foot',foot,foot+Vector((0,-.12,0)),name+'_Lower')
bpy.ops.object.mode_set(mode='OBJECT')
rest={b.name:b.matrix_local.copy() for b in data.bones}
for p in rig.pose.bones:p.rotation_mode='QUATERNION'
weights=[];soles={n:[] for n in legs};rigid_shell=[]
for v in mesh.vertices:
    p=v.co;w={'Shell':1.}
    # Preserve the high, patterned shell as one rigid surface. Motion is limited
    # to the exposed neck, tail and limb openings rather than bending the plates.
    head=smooth(.86,1.26,-p.y)*(1-smooth(.38,.60,abs(p.x)))*(1-smooth(.78,.96,p.z))
    if head>0:
        hn=smooth(1.12,1.48,-p.y)
        w={'Shell':1-head,'Neck':head*(1-hn),'Head':head*hn}
    elif p.y>1.35 and abs(p.x)<.40 and p.z<.5:
        t=smooth(1.30,1.6,p.y);w={'Shell':1-t,'Tail':t}
    elif abs(p.x)>.53 and p.z<.67:
        label=('Fore' if p.y<.15 else 'Hind')+('_L' if p.x<0 else '_R')
        row=legs[label]
        opening=(1-smooth(.34,.66,p.z))*smooth(.52,.72,abs(p.x))
        opening*=1-smooth(.43,.69,abs(p.y-row['foot'].y))
        if opening>0:
            paw=1-smooth(.16,.30,p.z)
            if p.z<.035 and abs(p.y-row['foot'].y)<.4:soles[label].append(v.index)
            raw={n:1/(.04+segment_distance(p,Vector(bones[n]['head']),Vector(bones[n]['tail'])))**3 for n in (label+'_Upper',label+'_Lower')}
            limb=normalized(raw);w={'Shell':1-opening,label+'_Foot':opening*paw}
            for n,a in limb.items():w[n]=opening*(1-paw)*a
    weights.append(normalized(w))
    if p.z>.96:rigid_shell.append(v.index)
for name,row in bones.items():
    if row['deform']:body.vertex_groups.new(name=name)
for v,w in zip(mesh.vertices,weights):
    for n,value in w.items():body.vertex_groups[n].add([v.index],value,'REPLACE')
assert all(soles.values())
mod=body.modifiers.new('WaterTurtle_ActualSkin','ARMATURE');mod.object=rig;mod.use_deform_preserve_volume=False
body.parent=rig;body.matrix_parent_inverse=Matrix.Identity(4)
def rotate(name,axis,angle):
    rig.pose.bones[name].rotation_quaternion=Quaternion(rest[name].to_quaternion().inverted()@Vector(axis),angle)
def segment(name,start,end):
    old=Vector(bones[name]['tail'])-Vector(bones[name]['head'])
    q=old.rotation_difference(end-start)@rest[name].to_quaternion()
    rig.pose.bones[name].matrix=Matrix.Translation(start)@q.to_matrix().to_4x4()
def solve(name,target):
    row=legs[name];parent=rig.pose.bones['Shell'].matrix@rest['Shell'].inverted()
    hip=parent@row['hip'];rh,rk,rf=row['hip'],row['knee'],row['foot']
    a=(rk-rh).length;b=(rf-rk).length;axis=target-hip;raw=axis.length;axis.normalize()
    d=max(abs(a-b)+1e-5,min(a+b-1e-5,raw));actual=hip+axis*d
    oldaxis=(rf-rh).normalized();bend=rk-rh-oldaxis*(rk-rh).dot(oldaxis)
    bend=oldaxis.rotation_difference(axis)@bend;bend-=axis*bend.dot(axis);bend.normalize()
    along=(a*a-b*b+d*d)/(2*d);knee=hip+axis*along+bend*math.sqrt(max(0,a*a-along*along))
    segment(name+'_Upper',hip,knee);bpy.context.view_layer.update()
    segment(name+'_Lower',knee,actual);bpy.context.view_layer.update()
    rig.pose.bones[name+'_Foot'].matrix=Matrix.Translation(actual)@rest[name+'_Foot'].to_quaternion().to_matrix().to_4x4()
    return (target-actual).length
recipes=[('WT_Idle',2,True),('WT_Walk',1,True),('WT_BackWalk',1,True),('WT_WaterCast',1.8,False)]
actions=[];metrics={};snapshots={}
for name,duration,loop in recipes:
    action=bpy.data.actions.new(name);action.use_fake_user=True;assign(rig,action);last={}
    rows=[];snapshots[name]={}
    for index in range(round(duration*60)+1):
        t=index/60;frame=1+t*30;u=t/duration
        for pb in rig.pose.bones:pb.matrix_basis=Matrix.Identity(4)
        # Root never moves. Four staggered swing phases leave three feet planted.
        walking=name in ('WT_Walk','WT_BackWalk')
        drop=.028 if walking else 0
        rig.pose.bones['Shell'].location=rest['Shell'].to_quaternion().inverted()@Vector((0,0,-drop))
        if name=='WT_Idle':rotate('Head',(0,0,1),math.sin(u*math.tau)*.01)
        elif walking:rotate('Head',(0,0,1),math.sin(u*math.tau)*.012)
        else:
            ready=smooth(0,.6,t)*(1-smooth(1.3,1.8,t))
            rotate('Neck',(1,0,0),-.035*ready)
            rotate('Head',(1,0,0),.035*ready)
        rotate('Tail',(0,0,1),math.sin(u*math.tau)*(.02 if loop else 0));bpy.context.view_layer.update()
        err=0;stance={}
        for leg,row in legs.items():
            target=row['foot'].copy();ph=(u+row['phase'])%1
            if walking:
                sign=-1 if name=='WT_BackWalk' else 1
                if ph<.75:target.y+=sign*(-.15+.30*ph/.75)
                else:
                    swing=(ph-.75)/.25;target.y+=sign*(.15-.30*smooth(0,1,swing));target.z+=.055*math.sin(math.pi*swing)**1.3
            stance[leg]=not walking or ph<.75
            err=max(err,solve(leg,target))
        for pb in rig.pose.bones:
            q=pb.rotation_quaternion.copy()
            if pb.name in last and q.dot(last[pb.name])<0:q.negate();pb.rotation_quaternion=q
            last[pb.name]=q
            for channel in ('location','rotation_quaternion','scale'):pb.keyframe_insert(channel,frame=frame,group=pb.name)
        bpy.context.view_layer.update();ps=evaluated(body)
        shell_matrix=rig.pose.bones['Shell'].matrix@rest['Shell'].inverted()
        mouth=rig.pose.bones['MouthOrigin'].head.copy()
        rows.append({'time':t,'maximumIkClampM':err,'minimumSurfaceHeight':min(p.z for p in ps),
            'soleMin':{n:min(ps[i].z for i in ids) for n,ids in soles.items()},'stance':stance,
            'mouthUnity':[-mouth.x,mouth.z,-mouth.y],
            'shellRigidError':max((ps[i]-shell_matrix@original[i]).length for i in rigid_shell)})
        if index in (0,round(duration*30),round(duration*60)):snapshots[name][str(t)]=[list(p) for p in ps]
    for layer in action.layers:
        for strip in layer.strips:
            for bag in strip.channelbags:
                for curve in bag.fcurves:
                    for key in curve.keyframe_points:key.interpolation='LINEAR'
    actions.append({'name':name,'duration':duration,'loop':loop})
    metrics[name]={'samples':rows,'maximumIkClampM':max(r['maximumIkClampM'] for r in rows),
                   'minimumSurfaceHeight':min(r['minimumSurfaceHeight'] for r in rows),
                   'shellRigidError':max(r['shellRigidError'] for r in rows)}
assign(rig,bpy.data.actions['WT_Idle']);scene.frame_set(1);bpy.context.view_layer.update()
bind_error=max((a-b).length for a,b in zip(original,evaluated(body)))
assert bind_error<1e-5 and uv_hash(mesh)==uv_before
blend=OUT/'WaterTurtle_Combat.blend';fbx=ASSET/'SM_WaterTurtle_Combat.fbx'
bpy.ops.wm.save_as_mainfile(filepath=str(blend))
bpy.ops.object.select_all(action='DESELECT');body.select_set(True);rig.select_set(True);bpy.context.view_layer.objects.active=rig
bpy.ops.export_scene.fbx(filepath=str(fbx),use_selection=True,object_types={'MESH','ARMATURE'},
    add_leaf_bones=False,bake_anim=True,bake_anim_use_all_actions=True,bake_anim_use_nla_strips=False,
    bake_anim_step=.5,bake_anim_simplify_factor=0,axis_forward='-Z',axis_up='Y',path_mode='AUTO')
assert hashlib.sha256(SOURCE.read_bytes()).hexdigest()==source_hash
report={'status':'RIG_BUILT_PENDING_ROUNDTRIP_VISUAL_RUNTIME','source':str(SOURCE),'sourceHashBefore':source_hash,'sourceHashAfter':source_hash,
    'blend':str(blend),'fbx':str(fbx),'triangles':16648,'vertices':8326,'materials':1,'bones':len(data.bones),
    'uvHashBefore':uv_before,'uvHashAfter':uv_hash(mesh),'bindPoseErrorM':bind_error,'actions':actions,
    'walkNominalSpeedMps':.4,'maxWeightInfluences':max(len(w) for w in weights),'weightSumError':max(abs(sum(w.values())-1) for w in weights),
    'soleVertices':soles,'rigidShellVertices':rigid_shell,'boneDefinitions':bones,
    'unverified':['Terrain foot placement','Actual mouth jet alignment','Shoulder skin self intersections','Runtime combat','Visual approval']}
for name,value in [('rig_report.json',report),('native_motion_verification.json',metrics),('roundtrip_reference.json',snapshots)]:
    (OUT/name).write_text(json.dumps(value,indent=2),encoding='utf-8')
print('TURTLE_COMBAT_RIG_BUILT',json.dumps({n:{k:v for k,v in m.items() if k!='samples'} for n,m in metrics.items()}))
