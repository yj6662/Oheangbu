"""Garment tailoring derivative, preserving source anatomy, pins and skin.

This is the measured R2 geometry candidate. It is not the rejected rigid-arm
lining/upper-garment weighting experiment. No production Action is generated.
"""
import bpy,json,math,hashlib,ast,importlib.util
import numpy as np
from pathlib import Path
from mathutils import Matrix,Euler
ROOT=Path(__file__).resolve().parents[3]
OUT=ROOT/'Art/PlayerV2/Inspect/ClothBlender/UnderarmTailoring';OUT.mkdir(parents=True,exist_ok=True)
SOURCE=ROOT/'Art/PlayerV2/Inspect/ClothBlender/Inputs/Assembled-final-9f4cd412.blend'
NAMES=['DosaV2_BodyCore','DosaV2_Robe_Combined','DosaV2_SleeveOuter_L','DosaV2_SleeveOuter_R']
def sha(p):return hashlib.sha256(Path(p).read_bytes()).hexdigest()
def load(name,file):
    s=importlib.util.spec_from_file_location(name,Path(__file__).with_name(file));m=importlib.util.module_from_spec(s);s.loader.exec_module(m);return m
helper=load('render_helper','build_brush.py');physics=load('physics_helper','diagnose_cloth_blender.py')
bpy.ops.wm.open_mainfile(filepath=str(SOURCE));scene=bpy.context.scene;rig=bpy.data.objects['DosaV2_Rig']
for obj in scene.objects:
    if obj.name.startswith('CTRL_') and 'AuthoringMode' in obj:obj['AuthoringMode']=False;obj.update_tag()
physics.direct_pose(rig,'rest_settle')
def smooth(lo,hi,x):
    t=np.clip((x-lo)/(hi-lo),0,1);return t*t*(3-2*t)
def tailored(points):
    result=points.copy();x=np.abs(points[:,0]);z=points[:,2]
    under=np.maximum(0,x-.17)*smooth(.13,.20,x)*(1-smooth(.26,.33,x))*smooth(.98,1.10,z)*(1-smooth(1.24,1.31,z))
    waist=np.maximum(0,x-.195)*smooth(.17,.24,x)*(1-smooth(.28,.34,x))*smooth(.87,.93,z)*(1-smooth(.99,1.06,z))
    result[:,0]-=np.sign(points[:,0])*np.maximum(under,waist)
    return result
def invariant(obj):
    return {'faces':[list(p.vertices) for p in obj.data.polygons],
            'uvs':[[list(v.uv) for v in layer.data] for layer in obj.data.uv_layers],
            'colors':{a.name:[list(v.color) for v in a.data] for a in obj.data.color_attributes},
            'weights':[{obj.vertex_groups[g.group].name:g.weight for g in v.groups} for v in obj.data.vertices],
            'materials':[m.name if m else None for m in obj.data.materials]}
unchanged={o.name:invariant(o) for o in scene.objects if o.type=='MESH'}
original={o.name:np.array([v.co[:] for v in o.data.vertices]) for o in scene.objects if o.type=='MESH'}
restbones={b.name:[list(r) for r in b.matrix_local] for b in rig.data.bones};changes=[];updated={}
for name in NAMES:
    obj=bpy.data.objects[name];points=original[name];new=tailored(points);updated[name]=new
    for i,(before,after) in enumerate(zip(points,new)):
        if np.linalg.norm(before-after)<1e-8:continue
        obj.data.vertices[i].co=after
        changes.append({'mesh':name,'sourceVertex':i,'before':before.tolist(),'after':after.tolist(),'displacementMeters':float(np.linalg.norm(before-after))})
    obj.data.update()
assert unchanged=={o.name:invariant(o) for o in scene.objects if o.type=='MESH'}
for name,points in original.items():
    if name not in NAMES:assert np.array_equal(points,np.array([v.co[:] for v in bpy.data.objects[name].data.vertices]))
assert restbones=={b.name:[list(r) for r in b.matrix_local] for b in rig.data.bones}
assert not bpy.data.actions
DEST=OUT/'DosaV2_GussetTailored.blend';bpy.ops.wm.save_as_mainfile(filepath=str(DEST))
report={'status':'GARMENT_GEOMETRY_CANDIDATE_PENDING_VISUAL_AND_PHYSICS_REVIEW','source':str(SOURCE),'sourceSha256':sha(SOURCE),
        'output':str(DEST),'outputSha256':sha(DEST),'scriptSha256':sha(__file__),
        'changedMeshes':{n:sum(r['mesh']==n for r in changes) for n in NAMES},'maximumRestDisplacementMeters':max(r['displacementMeters'] for r in changes),
        'allUVTopologyMaterialsWeightsPinsAndBoneRestUnchanged':True,'allOtherMeshPositionsUnchanged':True,'productionActions':0,
        'changes':changes,'measuredStaticEvidence':'candidate-report-r2.json: underarmTargetX=.17, waistTargetX=.195; 25 unique poses, deep pin cases0; existing combined left ArmLining penetration .296mm remains.',
        'reason':'Tailor the broad fixed underarm/waist garment surface into the actual anatomical space between torso and adducted arm. One identical positional field preserves shared geometric seams across the four named meshes. No anatomy shrink or pin release.'}
(OUT/'gusset-tailoring-report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
(OUT/'gusset-rebuild-source.py').write_bytes(Path(__file__).read_bytes())
definitions=json.loads((ROOT/'Art/PlayerV2/Validation/static-pose-definitions.json').read_text())['poses']
def pose(name):
    physics.direct_pose(rig,'rest_settle')
    if name.endswith('_settle'):physics.direct_pose(rig,name)
    else:
        definition=next(d for d in definitions if d['id']==name)
        for row in definition['boneRotations']:
            rig.pose.bones[row['bone']].matrix_basis=Euler([math.radians(row[c]) for c in ('x','y','z')],'XYZ').to_matrix().to_4x4()
    bpy.context.view_layer.update()
camera=scene.camera or helper.lighting(scene)
for casename in ['rest','open_hand','grip_down','combined_reach','raised_arms_settle']:
    for state,geometry in [('before',original),('after',updated)]:
        for name in NAMES:
            obj=bpy.data.objects[name]
            for v,p in zip(obj.data.vertices,geometry[name]):v.co=p
            obj.data.update()
        pose(casename)
        helper.render(scene,camera,OUT/(state+'-'+casename+'-front.png'),(0,-.01,1.2),1.00,math.pi*.12,width=1100,height=880)
        print('RENDER '+state+' '+casename,flush=True)
print(json.dumps({k:v for k,v in report.items() if k!='changes'},indent=2))
