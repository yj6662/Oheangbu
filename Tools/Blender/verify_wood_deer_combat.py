"""Actual Blender/FBX geometry, skin and sampled motion verification, no Unity calls."""
import bpy, json, math, hashlib, sys
from pathlib import Path
from mathutils import Vector, Matrix
from mathutils.kdtree import KDTree

ROOT=Path('C:/Users/yj666/Oheangbu');OUT=ROOT/'Art/Demo/Summons/WoodDeer'
report=json.loads((OUT/'rig_report.json').read_text(encoding='utf-8'))
names=('WoodDeer_Body','WoodDeer_Roots','WoodDeer_Leaves')
def assign(rig,action):
    rig.animation_data_create();rig.animation_data.action=action
    if action:rig.animation_data.action_slot=next(iter(action.slots))
def positions(o):
    ev=o.evaluated_get(bpy.context.evaluated_depsgraph_get());m=ev.to_mesh()
    try:return [ev.matrix_world@v.co for v in m.vertices]
    finally:ev.to_mesh_clear()
def distance(a,b):
    tree=KDTree(len(a))
    for i,p in enumerate(a):tree.insert(p,i)
    tree.balance();return max(tree.find(p)[2] for p in b)
def gather(rig):
    result={}
    for row in report['animations']:
        action=next(a for a in bpy.data.actions if a.name==row['name'] or a.name.endswith('|'+row['name']))
        assign(rig,action);samples={}
        for f in sorted(set([1,round(row['frames'][1]/2),row['frames'][1]])):
            bpy.context.scene.frame_set(f);bpy.context.view_layer.update()
            samples[f]={n:positions(bpy.data.objects[n]) for n in names}
        result[row['name']]=samples
    return result
bpy.ops.wm.open_mainfile(filepath=report['blend'])
rig=bpy.data.objects['WoodDeer_CombatRig'];native=gather(rig)
stance={};deforming={}
body=bpy.data.objects['WoodDeer_Body']
footsets={}
for n in ('Fore_L','Fore_R','Hind_L','Hind_R'):
    foot=Vector(report['bones'][n+'_Hoof']['head'])
    footsets[n]=[v.index for v in body.data.vertices if v.co.z<.045 and (Vector((v.co.x,v.co.y,0))-Vector((foot.x,foot.y,0))).length<.105]
phases={'Fore_L':0,'Fore_R':.5,'Hind_L':.75,'Hind_R':.25}
assign(rig,bpy.data.actions['WD_Walk'])
for n in phases:stance[n]=[];deforming[n]=[]
walk_frames=next(r['frames'][1] for r in report['animations'] if r['name']=='WD_Walk')
for f in range(1,walk_frames+1):
    bpy.context.scene.frame_set(f);bpy.context.view_layer.update();pts=positions(body)
    for n,phase in phases.items():
        u=((f-1)/(walk_frames-1)+phase)%1
        if u<.6:
            stance[n].append(min(pts[i].z for i in footsets[n]))
        # Difference from moving the entire creature rigidly with Root.
        root_transform=rig.pose.bones['Root'].matrix@rig.data.bones['Root'].matrix_local.inverted()
        deforming[n].append(max((pts[i]-root_transform@body.data.vertices[i].co).length for i in footsets[n]))
native_metrics={'stanceHoofMeshMinimumZRangeM':{n:[min(v),max(v)] for n,v in stance.items()},
                'legMeshMotionBeyondRootTransformM':{n:max(v) for n,v in deforming.items()}}

# Isolated FBX reimport verifies actual exported animation/skin rather than export flags.
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=report['fbx'],use_anim=True)
rig=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE')
parts=[bpy.data.objects[n] for n in names]
import_stats={}
for o in parts:
    o.data.calc_loop_triangles()
    sums=[sum(g.weight for g in v.groups) for v in o.data.vertices]
    import_stats[o.name]={'vertices':len(o.data.vertices),'triangles':len(o.data.loop_triangles),
      'materialSlots':len(o.material_slots),'maxInfluences':max(len(v.groups) for v in o.data.vertices),
      'unweightedVertices':sum(not v.groups for v in o.data.vertices),'maxWeightSumError':max(abs(v-1) for v in sums),
      'armatureModifiers':[m.object.name for m in o.modifiers if m.type=='ARMATURE'],
      'fbxReimportLocalTransform':{'position':list(o.location),'rotationEulerRadians':list(o.rotation_euler),'scale':list(o.scale)},
      'boneGroups':[g.name for g in o.vertex_groups]}
actions=[]
for a in bpy.data.actions:
    curves=[fc for layer in a.layers for strip in layer.strips for bag in strip.channelbags for fc in bag.fcurves]
    actions.append({'name':a.name,'range':list(a.frame_range),'curveCount':len(curves),
                    'keyCount':sum(len(fc.keyframe_points) for fc in curves)})
imported=gather(rig);errors={}
for name,samples in native.items():
    errors[name]={str(f):{n:distance(samples[f][n],imported[name][f][n]) for n in names} for f in samples}
result={'status':'PASS_BLENDER_FBX_ROUNDTRIP_PENDING_UNITY_AND_GAMEPLAY',
  'bones':[b.name for b in rig.data.bones],'boneCount':len(rig.data.bones),'parts':import_stats,'animations':actions,
  'maxSampledSurfaceErrorM':errors,'nativeMotionMetrics':native_metrics,
  'sourceUnchanged':hashlib.sha256(Path(report['source']).read_bytes()).hexdigest()==report['sourceSha256Before'],
  'fbxSha256':hashlib.sha256(Path(report['fbx']).read_bytes()).hexdigest(),
  'limitations':['Sampled surface positions prove serialization; they do not prove final artistic quality or Unity gameplay correctness.']}
assert result['sourceUnchanged']
assert sum(o['triangles'] for o in import_stats.values())==report['triangles']
assert len(rig.data.bones)==report['boneCount']
assert all(o['maxInfluences']<=4 and o['unweightedVertices']==0 and o['maxWeightSumError']<.00001 for o in import_stats.values())
assert all(e<.002 for action in errors.values() for sample in action.values() for e in sample.values())
assert all(row['curveCount']>0 for row in actions)
assert all(v>.08 for v in native_metrics['legMeshMotionBeyondRootTransformM'].values())
(OUT/'fbx_verification.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
print('WOOD_DEER_FBX_PASS '+json.dumps({'bones':len(rig.data.bones),'animations':actions,'motion':native_metrics}),flush=True)
if '--render' not in sys.argv:sys.exit(0)

# Compact three-pose contact sheet inputs from the saved native derivative.
bpy.ops.wm.open_mainfile(filepath=report['blend']);scene=bpy.context.scene;rig=bpy.data.objects['WoodDeer_CombatRig']
scene.render.engine='CYCLES';scene.cycles.samples=8;scene.cycles.use_denoising=True;scene.cycles.device='CPU'
scene.render.resolution_x=720;scene.render.resolution_y=720;scene.render.resolution_percentage=100
scene.world.color=(.12,.12,.12)
for location,power,size in [((3,-4,5),800,4),((-3,-2,3),500,3),((1,3,4),600,3)]:
    bpy.ops.object.light_add(type='AREA',location=location);o=bpy.context.object;o.data.energy=power;o.data.shape='DISK';o.data.size=size
    o.rotation_euler=(Vector((0,.1,1.2))-o.location).to_track_quat('-Z','Y').to_euler()
bpy.ops.object.camera_add(location=(3.8,-4.7,2.7));camera=bpy.context.object;camera.data.type='ORTHO';camera.data.ortho_scale=3.25
camera.rotation_euler=(Vector((.05,.0,1.25))-camera.location).to_track_quat('-Z','Y').to_euler();scene.camera=camera
for name,frame,label in [('WD_Idle',1,'idle'),('WD_Walk',10,'walk'),('WD_HornAttack',22,'horn_attack')]:
    assign(rig,bpy.data.actions[name]);scene.frame_set(frame)
    scene.render.filepath=str(OUT/('review_'+label+'.png'));bpy.ops.render.render(write_still=True)
print('WOOD_DEER_REVIEW_FRAMES_RENDERED',flush=True)
