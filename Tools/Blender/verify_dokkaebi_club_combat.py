"""Inspect the unfinished derivative, roundtrip its FBX and render disposable review cameras."""
import bpy, json, math, importlib.util, ctypes
from pathlib import Path
from mathutils import Vector
from mathutils.kdtree import KDTree

ROOT=Path('C:/Users/yj666/Oheangbu')
OUT=ROOT/'Art/Demo/Summons/DokkaebiClub'
spec=importlib.util.spec_from_file_location('dokka',ROOT/'Tools/Blender/build_dokkaebi_club_combat.py')
module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
report=json.loads((OUT/'rig_report.json').read_text(encoding='utf-8'))
def save(name,data): (OUT/name).write_text(json.dumps(data,ensure_ascii=False,indent=2),encoding='utf-8')
def points(body):
    obj=body.evaluated_get(bpy.context.evaluated_depsgraph_get());mesh=obj.to_mesh()
    try:return [obj.matrix_world@v.co for v in mesh.vertices]
    finally:obj.to_mesh_clear()
def at(t):
    f=1+t*30;bpy.context.scene.frame_set(math.floor(f),subframe=f-math.floor(f));bpy.context.view_layer.update()
def assign(rig,name):
    action=next(a for a in bpy.data.actions if a.name==name or a.name.endswith('|'+name))
    module.assign(rig,action)
def open_native():
    bpy.ops.wm.open_mainfile(filepath=report['blend'])
    return bpy.data.objects['DokkaebiClub_CombatRig'],bpy.data.objects['DokkaebiClub_Body']
rig,body=open_native()
original=[v.co.copy() for v in body.data.vertices]
soles=report['soleVertexIndices'];club=report['clubVertexIndices']
snapshots={};metrics={};sweep=[]
for action in report['animations']:
    name=action['name'];assign(rig,name);rows=[];frames={}
    for i in range(round(action['seconds']*120)+1):
        t=i/120;at(t);p=points(body)
        tip=rig.matrix_world@rig.pose.bones['ClubTip'].head
        grip=rig.matrix_world@rig.pose.bones['ClubGrip'].head
        rigid_error=max(abs((p[n]-p[club[0]]).length-(original[n]-original[club[0]]).length) for n in club[::4])
        rows.append({'t':t,'groundMin':min(q.z for q in p),'soleMin':{leg:min(p[n].z for n in ids) for leg,ids in soles.items()},'clubRigidError':rigid_error})
        if name=='DC_Swing' and .8-1e-6<=t<=1.1+1e-6:
            sweep.append({'t':t,'tipUnity':[-tip.x,tip.z,-tip.y],'angle':math.degrees(math.atan2(-tip.x,-tip.y)),
                          'radius':math.hypot(tip.x,tip.y),'height':tip.z,'gripToTip':(tip-grip).length})
        if i in (0,round(action['seconds']*60),round(action['seconds']*120)) or name=='DC_Swing' and i in (96,114,132):
            frames[str(t)]=[q.copy() for q in p]
    snapshots[name]=frames
    metrics[name]={'samples':len(rows),'minimumSurfaceHeight':min(r['groundMin'] for r in rows),
                   'maximumClubRigidError':max(r['clubRigidError'] for r in rows),
                   'soleRange':{leg:[min(r['soleMin'][leg] for r in rows),max(r['soleMin'][leg] for r in rows)] for leg in soles},
                   'rows':rows}
save('native_motion_verification.json',{'status':'MEASURED_NOT_VISUAL_APPROVAL','motions':metrics,'sweep':sweep,
    'unverified':['Stance-only foot sliding','Skin self intersections','Unity terrain contact','Actual combat collision']})
assign(rig,'DC_Idle');at(0);p=points(body)
body_pts=[p[i] for i in report['bodyWithoutClubVertexIndices']]
xs=[-q.x for q in body_pts];zs=[-q.y for q in body_pts]
measure={'source':'native_motion_verification.json: actual posed ClubTip at 120Hz; body dimensions from rest mesh. verticalTolerance=.65 is an authored gameplay allowance, not a measured club dimension; footprint includes .06m clearance and height includes .05m clearance.',
 'clubRange':min(r['radius'] for r in sweep),'originHeight':sum(r['height'] for r in sweep)/len(sweep),
 'halfAngle':min(abs(sweep[0]['angle']),abs(sweep[-1]['angle'])),'verticalTolerance':.65,
 'walkSpeed':report['walkNominalSpeedMps'],'footprintWidth':max(xs)-min(xs)+.06,'footprintLength':max(zs)-min(zs)+.06,
 'bodyHeight':max(q.z for q in body_pts)+.05,'footprintOffset':{'x':(max(xs)+min(xs))/2,'y':0,'z':(max(zs)+min(zs))/2}}
save('combat_measurements.json',measure)
bpy.ops.wm.read_factory_settings(use_empty=True);bpy.ops.import_scene.fbx(filepath=report['fbx'],use_anim=True)
rig=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE');body=bpy.data.objects['DokkaebiClub_Body'];bpy.context.scene.render.fps=30
errors={}
for name,frames in snapshots.items():
    assign(rig,name);errors[name]={}
    for time,reference in frames.items():
        at(float(time));p=points(body);tree=KDTree(len(p))
        for i,q in enumerate(p):tree.insert(q,i)
        tree.balance();errors[name][time]=max(tree.find(q)[2] for q in reference)
body.data.calc_loop_triangles()
worst=max(e for frames in errors.values() for e in frames.values())
save('fbx_verification.json',{'status':'PASS' if worst<.0001 else 'FAIL','maxSurfaceRoundtripErrorM':worst,'poses':errors,
 'triangles':len(body.data.loop_triangles),'vertices':len(body.data.vertices),'bones':len(rig.data.bones),
 'materialSlots':len(body.material_slots),'unverified':['Unity importer','Final deformation quality']})
if worst>=.0001:raise RuntimeError('FBX surface roundtrip mismatch')

class Perf(ctypes.Structure):
    _fields_=[('cb',ctypes.c_ulong)]+[(n,ctypes.c_size_t) for n in ('CommitTotal','CommitLimit','CommitPeak','PhysicalTotal','PhysicalAvailable','SystemCache','KernelTotal','KernelPaged','KernelNonpaged','PageSize')]+[(n,ctypes.c_ulong) for n in ('HandleCount','ProcessCount','ThreadCount')]
def commit():
    info=Perf();info.cb=ctypes.sizeof(info)
    if not ctypes.windll.psapi.GetPerformanceInfo(ctypes.byref(info),info.cb):raise RuntimeError('Cannot verify system commit')
    return info.CommitTotal/info.CommitLimit
rig,body=open_native();scene=bpy.context.scene
scene.render.engine='BLENDER_WORKBENCH';scene.render.resolution_x=1920;scene.render.resolution_y=1080;scene.render.resolution_percentage=100
scene.display.shading.light='STUDIO';scene.display.shading.studio_light='paint.sl';scene.display.shading.color_type='SINGLE'
scene.display.shading.single_color=(.49,.46,.38);scene.display.shading.show_shadows=True;scene.display.shading.show_cavity=True
scene.display.shading.cavity_type='BOTH';scene.display.shading.background_type='WORLD';scene.world.color=(.17,.17,.17)
bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,-.01));floor=bpy.context.object
bpy.ops.object.camera_add(location=(3.6,-4.6,2.4));camera=bpy.context.object;camera.data.type='ORTHO';camera.data.ortho_scale=4.6
camera.rotation_euler=(Vector((0,0,.9))-camera.location).to_track_quat('-Z','Y').to_euler();scene.camera=camera
caps=[]
for name,t,label in [('DC_Idle',0,'idle'),('DC_Walk',.225,'walk'),('DC_Swing',.8,'swing_left'),('DC_Swing',.95,'swing_center'),('DC_Swing',1.1,'swing_right')]:
    ratio=commit()
    if ratio>=.85:save('capture.json',{'status':'STOPPED_COMMIT','captures':caps,'ratio':ratio});raise RuntimeError('Capture stopped at commit threshold')
    assign(rig,name);at(t);scene.render.filepath=str(OUT/('review_'+label+'.png'));bpy.ops.render.render(write_still=True)
    caps.append({'file':Path(scene.render.filepath).name,'action':name,'seconds':t,'commitRatio':ratio,'resolution':[1920,1080]})
save('capture.json',{'status':'CAPTURED_PENDING_VISUAL_INSPECTION','captures':caps,'scope':'Disposable Blender workbench shape lighting, not actual Unity material or combat.'})
print('DOKKAEBI_VERIFIED_AND_RENDERED',flush=True)
