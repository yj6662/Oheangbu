"""Read-only native/FBX turtle checks and sequential disposable 1080p stills."""
import bpy, json, math, hashlib, ctypes
from pathlib import Path
from mathutils import Vector
from mathutils.kdtree import KDTree

ROOT=Path('C:/Users/yj666/Oheangbu')
OUT=ROOT/'Art/Demo/Summons/WaterTurtle'
report=json.loads((OUT/'rig_report.json').read_text())
source=Path(report['source'])
source_before=hashlib.sha256(source.read_bytes()).hexdigest()

def save(name,data): (OUT/name).write_text(json.dumps(data,indent=2),encoding='utf-8')
def points(body):
    obj=body.evaluated_get(bpy.context.evaluated_depsgraph_get());mesh=obj.to_mesh()
    try:return [obj.matrix_world@v.co for v in mesh.vertices]
    finally:obj.to_mesh_clear()
def at(t):
    f=1+t*30;bpy.context.scene.frame_set(math.floor(f),subframe=f-math.floor(f));bpy.context.view_layer.update()
def assign(rig,name):
    action=next(a for a in bpy.data.actions if a.name==name or a.name.endswith('|'+name))
    rig.animation_data_create();rig.animation_data.action=action
    rig.animation_data.action_slot=next(iter(action.slots))
    return action
def native():
    bpy.ops.wm.open_mainfile(filepath=report['blend'])
    return bpy.data.objects['WaterTurtle_CombatRig'],bpy.data.objects['WaterTurtle_Body']
def unity(p):return [-p.x,p.z,-p.y]
def bounds(ps):
    rows=[unity(p) for p in ps]
    lo=[min(p[i] for p in rows) for i in range(3)];hi=[max(p[i] for p in rows) for i in range(3)]
    return {'min':lo,'max':hi,'size':[hi[i]-lo[i] for i in range(3)],'center':[(hi[i]+lo[i])/2 for i in range(3)]}
def weights(body):
    counts=[len([g for g in v.groups if g.weight>0]) for v in body.data.vertices]
    return {'maximumInfluences':max(counts),'unweightedVertices':counts.count(0),'maximumWeightSumError':max(abs(sum(g.weight for g in v.groups)-1) for v in body.data.vertices)}

rig,body=native();snapshots={};anchors={};native_weights=weights(body)
for recipe in report['actions']:
    name=recipe['name'];action=assign(rig,name);snapshots[name]={};anchors[name]={}
    for t in sorted(set([0,recipe['duration']/2,recipe['duration'],.125,.25,.375,.625,.875]+([.6,1.3] if name=='WT_WaterCast' else []))):
        if t>recipe['duration']:continue
        at(t);snapshots[name][str(t)]=points(body)
        mouth=rig.matrix_world@rig.pose.bones['MouthOrigin'].head
        direction=rig.matrix_world.to_3x3()@(rig.pose.bones['MouthOrigin'].tail-rig.pose.bones['MouthOrigin'].head).normalized()
        anchors[name][str(t)]={'mouthUnity':unity(mouth),'mouthForwardUnity':unity(direction)}
assign(rig,'WT_WaterCast');release=[]
for i in range(85):
    t=.6+i/120;at(t);release.append({'seconds':t,'mouthUnity':unity(rig.matrix_world@rig.pose.bones['MouthOrigin'].head)})
release_origin=Vector(release[0]['mouthUnity'])
release_error=max((Vector(r['mouthUnity'])-release_origin).length for r in release)
assign(rig,'WT_Idle');at(0)
measure={'coordinateConversion':'Unity position = (-Blender X, Blender Z, -Blender Y); metres',
         'idleBodyBoundsUnity':bounds(points(body)),'mouthPoses':anchors,'nativeWeights':native_weights,
         'castRelease':{'startSeconds':.6,'endSeconds':1.3,'sampleHz':120,'actorSpaceOriginUnity':list(release_origin),'maxVariationFromStartM':release_error,'samples':release},
         'sourceSha256':source_before,'scope':'Measured native poses. MouthOrigin is an authored bone; geometric mouth attachment and runtime jet still require review.'}
save('combat_measurements.json',measure)

bpy.ops.wm.read_factory_settings(use_empty=True);bpy.ops.import_scene.fbx(filepath=report['fbx'],use_anim=True)
rig=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE');body=bpy.data.objects['WaterTurtle_Body'];bpy.context.scene.render.fps=30
actions=[{'name':a.name,'frameRange':list(a.frame_range)} for a in bpy.data.actions]
errors={}
for name,frames in snapshots.items():
    assign(rig,name);errors[name]={}
    for time,reference in frames.items():
        at(float(time));p=points(body);tree=KDTree(len(p));ref=KDTree(len(reference))
        for i,q in enumerate(p):tree.insert(q,i)
        for i,q in enumerate(reference):ref.insert(q,i)
        tree.balance();ref.balance()
        errors[name][time]=max(max(tree.find(q)[2] for q in reference),max(ref.find(q)[2] for q in p))
body.data.calc_loop_triangles();worst=max(e for frames in errors.values() for e in frames.values());fbx_weights=weights(body)
check={'status':'PASS' if worst<.0001 and fbx_weights['maximumInfluences']<=4 and fbx_weights['maximumWeightSumError']<1e-5 else 'FAIL',
       'comparison':'Bidirectional nearest vertex distance in evaluated world space, including fractional frames',
       'sampledPoseCount':sum(len(frames) for frames in snapshots.values()),
       'maxSurfaceRoundtripErrorM':worst,'poses':errors,'actions':actions,
       'triangles':len(body.data.loop_triangles),'vertices':len(body.data.vertices),'bones':len(rig.data.bones),
       'boneNames':[b.name for b in rig.data.bones],'materialSlots':len(body.material_slots),'nativeWeights':native_weights,'fbxWeights':fbx_weights,
       'sourceSha256Before':source_before,'sourceSha256After':hashlib.sha256(source.read_bytes()).hexdigest(),
       'unverified':['Unity importer','Full motion visual approval','Runtime terrain contact','Runtime combat and water VFX']}
save('fbx_verification.json',check)
if check['status']!='PASS':raise RuntimeError('Turtle FBX verification failed')

class Perf(ctypes.Structure):
    _fields_=[('cb',ctypes.c_ulong)]+[(n,ctypes.c_size_t) for n in ('CommitTotal','CommitLimit','CommitPeak','PhysicalTotal','PhysicalAvailable','SystemCache','KernelTotal','KernelPaged','KernelNonpaged','PageSize')]+[(n,ctypes.c_ulong) for n in ('HandleCount','ProcessCount','ThreadCount')]
def commit():
    info=Perf();info.cb=ctypes.sizeof(info)
    if not ctypes.windll.psapi.GetPerformanceInfo(ctypes.byref(info),info.cb):raise RuntimeError('Cannot verify system commit')
    return info.CommitTotal/info.CommitLimit

rig,body=native();scene=bpy.context.scene
scene.render.engine='BLENDER_WORKBENCH';scene.render.resolution_x=1920;scene.render.resolution_y=1080;scene.render.resolution_percentage=100
scene.display.shading.light='STUDIO';scene.display.shading.studio_light='paint.sl';scene.display.shading.color_type='SINGLE'
scene.display.shading.single_color=(.48,.48,.48);scene.display.shading.show_shadows=True;scene.display.shading.show_cavity=True
scene.display.shading.cavity_type='BOTH';scene.display.shading.background_type='WORLD'
if scene.world is None:scene.world=bpy.data.worlds.new('ReviewWorld')
scene.world.color=(.17,.17,.17)
bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,-.012))
bpy.ops.object.camera_add(location=(4.4,-5.8,3.0));camera=bpy.context.object;camera.data.type='ORTHO';camera.data.ortho_scale=5.7
camera.rotation_euler=(Vector((0,0,.60))-camera.location).to_track_quat('-Z','Y').to_euler();scene.camera=camera
caps=[]
for name,t,label in [('WT_Idle',0,'idle'),('WT_Walk',.125,'walk'),('WT_WaterCast',.9,'water_cast')]:
    ratio=commit()
    if ratio>=.85:save('capture.json',{'status':'STOPPED_COMMIT','captures':caps,'ratio':ratio});raise RuntimeError('Capture stopped at commit threshold')
    assign(rig,name);at(t);scene.render.filepath=str(OUT/('review_'+label+'.png'));bpy.ops.render.render(write_still=True)
    caps.append({'file':Path(scene.render.filepath).name,'action':name,'seconds':t,'commitRatio':ratio,'resolution':[1920,1080]})
ratio=commit()
if ratio>=.85:raise RuntimeError('Anchor capture stopped at commit threshold')
assign(rig,'WT_WaterCast');at(.9)
mouth=rig.matrix_world@rig.pose.bones['MouthOrigin'].head
bpy.ops.mesh.primitive_uv_sphere_add(segments=16,ring_count=8,radius=.045,location=mouth)
marker=bpy.context.object;marker.color=(1,.05,.02,1)
for obj in scene.objects:
    if obj!=marker:obj.color=(.48,.48,.48,1)
scene.display.shading.color_type='OBJECT'
camera.location=(4,-.8,1.05);camera.data.ortho_scale=4.5
camera.rotation_euler=(Vector((0,0,.7))-camera.location).to_track_quat('-Z','Y').to_euler()
scene.render.filepath=str(OUT/'review_mouth_anchor.png');bpy.ops.render.render(write_still=True)
caps.append({'file':'review_mouth_anchor.png','action':'WT_WaterCast','seconds':.9,'commitRatio':ratio,'resolution':[1920,1080],'purpose':'Red sphere marks authored MouthOrigin, for anatomical attachment inspection only.'})
save('capture.json',{'status':'CAPTURED_PENDING_VISUAL_INSPECTION','captures':caps,'scope':'Disposable Blender gray workbench lighting. Not Unity runtime, material approval, or animation approval.'})
assert hashlib.sha256(source.read_bytes()).hexdigest()==source_before
print('TURTLE_VERIFIED_AND_RENDERED',json.dumps({'fbxErrorM':worst,'weights':fbx_weights,'bounds':measure['idleBodyBoundsUnity']}),flush=True)
