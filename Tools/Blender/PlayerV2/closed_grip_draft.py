"""Three authored closed-grasp candidates only. Never saves or alters source blend files."""
import bpy,json,math,hashlib
from pathlib import Path
from mathutils import Matrix,Vector,Euler,Quaternion
from mathutils.bvhtree import BVHTree
ROOT=Path('C:/Users/yj666/Oheangbu');ART=ROOT/'Art/PlayerV2';OUT=ART/'Inspect/ClosedGripDraft';OUT.mkdir(parents=True,exist_ok=True)
SOURCE=ART/'DosaV2_Assembled.blend';BRUSH=ART/'DosaBrushV2_TuftRefined.blend'
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
hashes={'character':sha(SOURCE),'brush':sha(BRUSH)}
bpy.ops.wm.open_mainfile(filepath=str(SOURCE));scene=bpy.context.scene
rig=bpy.data.objects['DosaV2_Rig'];hand=bpy.data.objects['DosaV2_Hands']
if 'DosaBrushV2_Rig' not in bpy.data.objects:
    with bpy.data.libraries.load(str(BRUSH),link=False) as(src,dst):dst.objects=[n for n in src.objects if n.startswith('DosaBrushV2_') or n in ['GripSocket','TipSocket']]
    for o in dst.objects:
        if o and not o.users_collection:scene.collection.objects.link(o)
brush=bpy.data.objects['DosaBrushV2_Rig'];grip=bpy.data.objects['GripSocket'];handle=bpy.data.objects['DosaBrushV2_Handle']
for o in scene.objects:
    if o.type=='MESH':o.hide_render=o.name=='DosaV2_SourceSurface'
    if o.type=='ARMATURE':
        o.animation_data_clear()
        for p in o.pose.bones:p.matrix_basis=Matrix.Identity(4)
for mod in hand.modifiers:
    if mod.type=='ARMATURE':mod.use_deform_preserve_volume=False
bpy.context.view_layer.update();brushGrip=brush.matrix_world.inverted()@grip.matrix_world
rest={p.name:p.matrix.copy() for p in rig.pose.bones}
entries={b['name']:b for b in json.loads((ROOT/'Oheangbu/Screenshots/PlayerDosaV2/import-bones-and-surfaces.json').read_text())['bones']}
C=Matrix(((-1,0,0),(0,0,1),(0,-1,0)))
def uq(d):return Quaternion((d['w'],d['x'],d['y'],d['z']))
def qj(q):return {'x':q.x,'y':q.y,'z':q.z,'w':q.w}
def vj(v):return {'x':v.x,'y':v.y,'z':v.z}
def look(o,p):o.rotation_euler=(Vector(p)-o.location).to_track_quat('-Z','Y').to_euler()
def aim(name,d):
    p=rig.pose.bones[name];m=p.matrix.copy();q=m.to_quaternion();n=((q@Vector((0,1,0))).rotation_difference(Vector(d).normalized())@q).to_matrix().to_4x4();n.translation=m.translation;p.matrix=n;bpy.context.view_layer.update()
for o in list(scene.objects):
    if o.type in ['CAMERA','LIGHT']:bpy.data.objects.remove(o,do_unlink=True)
cam=bpy.data.objects.new('ClosedGripDraftCamera',bpy.data.cameras.new('ClosedGripDraftCamera'));scene.collection.objects.link(cam);scene.camera=cam
cam.data.type='ORTHO';cam.data.ortho_scale=.25
scene.render.engine='CYCLES';scene.cycles.samples=12;scene.cycles.use_denoising=True
scene.render.resolution_x=1000;scene.render.resolution_y=850;scene.render.resolution_percentage=100
scene.world.color=(.18,.18,.18);scene.view_settings.view_transform='AgX'
lights=[]
for name,off,en,size in [('Key',(.7,-.6,.7),50,.7),('Fill',(-.4,.1,.4),30,.6)]:
    o=bpy.data.objects.new(name,bpy.data.lights.new(name,'AREA'));scene.collection.objects.link(o);o.data.energy=en;o.data.size=size;lights.append((o,Vector(off)))
variants=[('A',[(20,80,45),(20,85,45),(25,85,45),(30,85,45)],.071,-.030),
          ('B',[(25,85,50),(25,85,50),(30,85,45),(35,85,40)],.067,-.031),
          ('C',[(15,85,50),(18,85,50),(25,90,45),(30,90,45)],.071,-.0324)]
results=[]
for label,fingerAngles,along,dorsal in variants:
    for p in rig.pose.bones:p.matrix_basis=Matrix.Identity(4)
    if hand.data.shape_keys:
        for key in hand.data.shape_keys.key_blocks:
            if key.name.startswith('GripPalmRelax_'):key.value=1 if key.name.endswith('Right') else 0
    rotations={'Thumb':[(-42,6,-50),(-40,0,0),(-25,0,0)]}
    rotations.update({f:[(-v,0,0) for v in a] for f,a in zip(['Index','Middle','Ring','Pinky'],fingerAngles)})
    for f,angles in rotations.items():
        for i,a in enumerate(angles):p=rig.pose.bones['RightHand'+f+str(i+1)];p.rotation_mode='XYZ';p.rotation_euler=[math.radians(v) for v in a]
    bpy.context.view_layer.update()
    # Convert all fifteen pose offsets through the actual imported FBX basis, as in the existing calibration evidence.
    targets={}
    for p in rig.pose.bones:
        if p.name not in entries:continue
        delta=C@(p.matrix.to_3x3()@rest[p.name].to_3x3().inverted())@C.inverted();targets[p.name]=delta.to_quaternion()@uq(entries[p.name]['rotation'])
    offsets=[]
    for f in ['Thumb','Index','Middle','Ring','Pinky']:
        for j in range(1,4):
            n='RightHand'+f+str(j);e=entries[n];q=uq(e['localRotation']).inverted()@(targets[e['parent']].inverted()@targets[n]);q.normalize();offsets.append(qj(q))
    hg=rig.data.bones['RightBrushGrip'].matrix_local.copy();wrist=rig.data.bones['RightHand'].matrix_local.translation
    hg.translation=wrist+Vector((-along,.00376,dorsal))
    eh=entries['RightHand'];handU=uq(eh['rotation']);handP=Vector(tuple(eh['position'][k] for k in 'xyz'))
    gripP=C@hg.translation;gripLocal=handU.inverted()@(gripP-handP)
    # Keep the existing calibrated grip orientation; this draft moves its origin only.
    originalGrip=rig.data.bones['RightBrushGrip'].matrix_local
    gripRotU=(C@originalGrip.to_3x3()@C.inverted()).to_quaternion()
    # Position conversion does not define FBX socket axes. Preserve imported socket rotation directly.
    eg=entries.get('RightBrushGrip');gripLocalRot=handU.inverted()@uq(eg['rotation']) if eg else None
    if gripLocalRot is None:raise RuntimeError('Missing actual imported RightBrushGrip rotation')
    # Pose whole arm for an unobstructed wrist/sleeve-inclusive view; the finger/socket contract stays hand-local.
    aim('LeftArm',(.16,-.02,-.987));aim('RightArm',(-.16,-.02,-.987));aim('RightForeArm',(-.14,-.84,.525))
    currentHand=rig.pose.bones['RightHand'].matrix;world=rig.matrix_world@currentHand@rig.data.bones['RightHand'].matrix_local.inverted()@hg
    brush.matrix_world=world@brushGrip.inverted();bpy.context.view_layer.update()
    center=world.translation
    for light,off in lights:light.location=center+off;look(light,center)
    files=[]
    for view,offset in [('palm',Vector((-.60,-.75,-.28))),('side',world.to_3x3()@Vector((-.34,-.15,.40)))]:
        cam.location=center+offset;look(cam,center);scene.render.filepath=str(OUT/(label+'-'+view+'.png'));bpy.ops.render.render(write_still=True);files.append(scene.render.filepath)
    # Compact actual skin/shaft nearest-surface evidence. This is not an all-surface clearance gate.
    dg=bpy.context.evaluated_depsgraph_get();ev=hand.evaluated_get(dg);mesh=ev.to_mesh();local=grip.matrix_world.inverted()@ev.matrix_world
    hl=grip.matrix_world.inverted()@handle.matrix_world;handle.data.calc_loop_triangles();hv=[hl@v.co for v in handle.data.vertices];tree=BVHTree.FromPolygons(hv,[t.vertices for t in handle.data.loop_triangles],all_triangles=True)
    groups={g.index:g.name for g in hand.vertex_groups};stats={}
    for f in rotations:
        vals=[]
        for v in hand.data.vertices:
            if sum(g.weight for g in v.groups if groups[g.group].startswith('RightHand'+f))<.5:continue
            p=local@mesh.vertices[v.index].co;hit=tree.find_nearest(p)
            if hit[0] is not None:vals.append(hit[3])
        stats[f]={'nearest_skin_shaft_mm':min(vals)*1000 if vals else None,'summed_flex_degrees':sum(-a[0] for a in rotations[f])}
    ev.to_mesh_clear()
    record={'candidate':label,'status':'CLOSED_GRASP_DRAFT_NOT_SURFACE_GATE','sourceSha256':hashes,'images':files,'OverrideHandGrip':True,'HandGripLocalPosition':vj(gripLocal),'HandGripLocalRotation':qj(gripLocalRot),'KeepGripClosedOnPenLift':True,'CalibratedRightFingerOffsets':offsets,'blenderFingerEulerXYZDegrees':rotations,'compactEvidence':stats}
    (OUT/(label+'.json')).write_text(json.dumps(record,indent=2));results.append(record);print('DONE '+label,flush=True)
(OUT/'report.json').write_text(json.dumps({'status':'THREE_STATIC_CLOSED_GRASP_CANDIDATES','sourcesUnchanged':sha(SOURCE)==hashes['character'] and sha(BRUSH)==hashes['brush'],'candidates':results},indent=2))
