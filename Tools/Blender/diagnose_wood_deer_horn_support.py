"""Measure horn-attack foot support without changing the blend or FBX."""
import bpy, json, math, hashlib
from pathlib import Path
from mathutils import Vector
ROOT=Path('C:/Users/yj666/Oheangbu');OUT=ROOT/'Art/Demo/Summons/WoodDeer'
report=json.loads((OUT/'rig_report.json').read_text(encoding='utf-8'))
source_paths=[Path(report['blend']),Path(report['fbx'])]
before={str(p):hashlib.sha256(p.read_bytes()).hexdigest() for p in source_paths}
def assign(rig,action):
    rig.animation_data_create();rig.animation_data.action=action;rig.animation_data.action_slot=next(iter(action.slots))
def vertices(body):
    evaluated=body.evaluated_get(bpy.context.evaluated_depsgraph_get());mesh=evaluated.to_mesh()
    try:return [evaluated.matrix_world@v.co for v in mesh.vertices]
    finally:evaluated.to_mesh_clear()
def inspect(label):
    rig=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE')
    body=bpy.data.objects['WoodDeer_Body']
    action=next(a for a in bpy.data.actions if a.name=='WD_HornAttack' or a.name.endswith('|WD_HornAttack'))
    # Classify by actual exported skin weights, independent of vertex index/order.
    sets={n:[] for n in ('Fore_L','Fore_R','Hind_L','Hind_R')}
    for v in body.data.vertices:
        weights={body.vertex_groups[g.group].name:g.weight for g in v.groups}
        for n in sets:
            if weights.get(n+'_Hoof',0)>.5:sets[n].append(v.index)
    assign(rig,action);samples=[]
    for time in [0,.15,.40,.70,.75,.80,.85,.90,.95,1.0,1.2,1.4]:
        frame=time*30+1;whole=math.floor(frame)
        bpy.context.scene.frame_set(whole,subframe=frame-whole);bpy.context.view_layer.update()
        points=vertices(body);foot={}
        for n,ids in sets.items():
            p=min((points[i] for i in ids),key=lambda p:p.z)
            foot[n]={'lowestMeshPoint':list(p),'minimumZ':p.z,'maxZ':max(points[i].z for i in ids),
                     'boneHead':list(rig.matrix_world@rig.pose.bones[n+'_Hoof'].head),'candidateVertices':len(ids)}
        samples.append({'seconds':time,'rootBoneHead':list(rig.matrix_world@rig.pose.bones['Root'].head),
                        'bodyMinimumZ':min(p.z for p in points),'bodyMaximumZ':max(p.z for p in points),'feet':foot})
    support=[s for s in samples if .70<=s['seconds']<=.95]
    return {'label':label,'action':action.name,'frames':list(action.frame_range),'samples':samples,
      'impactSupportRangeM':{n:[min(s['feet'][n]['minimumZ'] for s in support),max(s['feet'][n]['minimumZ'] for s in support)] for n in sets},
      'flatGroundExpectedGapIncludingManager035M':{n:[min(s['feet'][n]['minimumZ'] for s in support)+.035,max(s['feet'][n]['minimumZ'] for s in support)+.035] for n in sets}}
bpy.ops.wm.open_mainfile(filepath=report['blend']);native=inspect('native blend')
bpy.ops.wm.read_factory_settings(use_empty=True);bpy.ops.import_scene.fbx(filepath=report['fbx'],use_anim=True)
exported=inspect('empty-scene FBX reimport')
result={'status':'MEASURED_OFFLINE_NO_MODEL_EDIT','native':native,'fbx':exported,
  'filesUnchanged':all(hashlib.sha256(p.read_bytes()).hexdigest()==before[str(p)] for p in source_paths),
  'scope':'Actual skinned hoof vertices at native horizontal z=0 ground. Unity playback, terrain and ground query require separate measurement.'}
assert result['filesUnchanged']
(OUT/'horn_support_diagnostic.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps({'native':native['impactSupportRangeM'],'fbx':exported['impactSupportRangeM'],
                  'flatGroundIncluding035':exported['flatGroundExpectedGapIncludingManager035M'],'filesUnchanged':result['filesUnchanged']}),flush=True)
