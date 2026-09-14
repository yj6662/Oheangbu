"""Save the complete-panel cloth slack repair, with actual Blender evaluation."""
import bpy,json,math,hashlib,importlib.util
from pathlib import Path
import numpy as np
from mathutils import Matrix,Euler
ROOT=Path(__file__).resolve().parents[3];BASE=ROOT/'Art/PlayerV2/Inspect/ClothBlender/FoldedGusset';OUT=ROOT/'Art/PlayerV2/Inspect/ClothBlender/GlobalGusset'
SOURCE=BASE/'DosaV2_FoldedGusset.blend';bpy.ops.wm.open_mainfile(filepath=str(SOURCE));scene=bpy.context.scene;rig=bpy.data.objects['DosaV2_Rig']
def load(name,file):
    spec=importlib.util.spec_from_file_location(name,Path(__file__).with_name(file));module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module);return module
helper=load('slack_render','build_brush.py');physics=load('slack_pose','diagnose_cloth_blender.py')
for o in scene.objects:
    if o.name.startswith('CTRL_') and 'AuthoringMode' in o:o['AuthoringMode']=False;o.update_tag()
physics.direct_pose(rig,'rest_settle');names=['DosaV2_SleeveOuter_L','DosaV2_SleeveOuter_R'];changes=[];data={}
def structural(o):
    return {'vertices':[v.co[:] for v in o.data.vertices],'weights':[[[o.vertex_groups[g.group].name,g.weight] for g in v.groups] for v in o.data.vertices],
      'faces':[list(p.vertices) for p in o.data.polygons],'uvs':[[list(v.uv) for v in layer.data] for layer in o.data.uv_layers],'colors':{a.name:[list(v.color) for v in a.data] for a in o.data.color_attributes}}
before={o.name:structural(o) for o in scene.objects if o.type=='MESH'}
for name in names:
    o=bpy.data.objects[name];d=np.load(OUT/(name+'-candidate.npz'));data[name]=d;original=np.array([v.co[:] for v in o.data.vertices]);delta=d['rest']-original;pins=np.flatnonzero(d['mobility']==0)
    assert np.array_equal(original[pins],d['rest'][pins])
    for v,p in zip(o.data.vertices,d['rest']):v.co=p
    o.data.update();o.data.calc_loop_triangles()
    current=structural(o)
    for k,v in before[name].items():
        if k!='vertices':assert v==current[k]
    changes.append({'mesh':name,'vertices':len(original),'triangles':len(o.data.loop_triangles),'changedVertices':int(np.sum(np.linalg.norm(delta,axis=1)>1e-8)),
      'maximumRestPositionChangeMeters':float(np.max(np.linalg.norm(delta,axis=1))),'allExactPinRestPositionsUnchanged':True,'changes':[{'vertex':int(i),'before':original[i].tolist(),'after':d['rest'][i].tolist()} for i in np.flatnonzero(np.linalg.norm(delta,axis=1)>1e-8)]})
for name,row in before.items():
    if name not in names:assert row==structural(bpy.data.objects[name])
assert len(rig.data.bones)==98 and not bpy.data.actions
DEST=OUT/'DosaV2_GlobalClothSlack.blend';bpy.ops.wm.save_as_mainfile(filepath=str(DEST));sha=hashlib.sha256(DEST.read_bytes()).hexdigest()
defs=json.loads((BASE/'Inputs/static-pose-definitions.json').read_text())['poses']
def pose(name):
    physics.direct_pose(rig,'rest_settle')
    if name.startswith('fixture_'):physics.direct_pose(rig,name.replace('fixture_',''))
    else:
        definition=next(d for d in defs if d['id']==name)
        for r in definition['boneRotations']:rig.pose.bones[r['bone']].matrix_basis=Euler([math.radians(r[c]) for c in ('x','y','z')],'XYZ').to_matrix().to_4x4()
    bpy.context.view_layer.update()
rows=[]
for i,name in enumerate([d['id'] for d in defs]+['fixture_rest_settle','fixture_grip_settle','fixture_raised_arms_settle']):
    pose(name);deps=bpy.context.evaluated_depsgraph_get()
    for n in names:
        o=bpy.data.objects[n];ev=o.evaluated_get(deps);me=ev.to_mesh();points=np.array([list(ev.matrix_world@v.co) for v in me.vertices]);ev.to_mesh_clear();expected=data[n]['posePoints'][i]
        rows.append({'poseId':name,'mesh':n,'maximumActualBlenderVsPredictedPositionMeters':float(np.max(np.linalg.norm(points-expected,axis=1)))})
report={'status':'GLOBAL_PIN_PATH_NECESSARY_CANDIDATE_NOT_PHYSICS_PASS','source':str(SOURCE),'sourceSha256':hashlib.sha256(SOURCE.read_bytes()).hexdigest(),'output':str(DEST),'outputSha256':sha,
 'additionalVertices':0,'additionalTriangles':0,'changedMeshes':changes,'allOtherMeshesExact':True,'allSourceWeightsPinsMotionLimitsUVsBonesExact':True,'productionActions':0,
 'actualBlenderEvaluatedPoseCount':25,'maximumActualBlenderVsPredictedPositionMeters':max(r['maximumActualBlenderVsPredictedPositionMeters'] for r in rows),'actualBlenderRows':rows,
 'limitation':'Global pin-to-vertex path feasibility is necessary, not sufficient for native cloth convergence/collision/visible drape. Existing five inner anatomy remains unchanged; no unvalidated shoulder connection volume is merged.'}
(OUT/'source-report.json').write_text(json.dumps(report,indent=2),encoding='utf-8');print(json.dumps({k:v for k,v in report.items() if k not in ['changedMeshes','actualBlenderRows']},indent=2),flush=True)
camera=scene.camera or helper.lighting(scene)
for name in ['rest','open_hand','grip_down','combined_reach','fixture_raised_arms_settle']:
    pose(name);helper.render(scene,camera,OUT/(name+'-front.png'),(0,-.01,1.2),1.00,math.pi*.12,width=1100,height=880);print('RENDER '+name,flush=True)
