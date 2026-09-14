"""Common torso/outer-cloth seam derivative with measured local weight correction.

No animation, anatomy alteration, pin release or physics-limit change. The saved
derivative is a necessary-constraint candidate, never a native simulation PASS.
"""
import bpy,ast,json,math,hashlib,importlib.util
from pathlib import Path
import numpy as np
from mathutils import Matrix,Euler,Vector
from mathutils.bvhtree import BVHTree
ROOT=Path(__file__).resolve().parents[3]
OUT=ROOT/'Art/PlayerV2/Inspect/ClothBlender/ContinuousGusset';OUT.mkdir(parents=True,exist_ok=True)
DATA=ROOT/'Art/PlayerV2/Inspect/ClothBlender/PosedArmFit9f4cd412'
SOURCE=ROOT/'Art/PlayerV2/Inspect/ClothBlender/Inputs/Assembled-final-9f4cd412.blend'
HELPER=ROOT/'Art/PlayerV2/Inspect/ClothBlender/GussetAnchor/candidate-script-r3.py'
SOLUTION=ROOT/'Art/PlayerV2/Inspect/ClothBlender/GussetAnchor/intermediate-weight-solution.json'
NAMES=['DosaV2_BodyCore','DosaV2_Robe_Combined','DosaV2_SleeveOuter_L','DosaV2_SleeveOuter_R']
def sha(p):return hashlib.sha256(Path(p).read_bytes()).hexdigest()
def load(name,file):
    s=importlib.util.spec_from_file_location(name,Path(__file__).with_name(file));m=importlib.util.module_from_spec(s);s.loader.exec_module(m);return m
nodes=[n for n in ast.parse(HELPER.read_text()).body if isinstance(n,ast.FunctionDef) and n.name in ['smooth','geometry','coherent','skin']]
exec(compile(ast.Module(body=nodes,type_ignores=[]),str(HELPER),'exec'))
render_helper=load('render_helper','build_brush.py');physics=load('physics_helper','diagnose_cloth_blender.py')
bpy.ops.wm.open_mainfile(filepath=str(SOURCE));scene=bpy.context.scene;rig=bpy.data.objects['DosaV2_Rig']
for obj in scene.objects:
    if obj.name.startswith('CTRL_') and 'AuthoringMode' in obj:obj['AuthoringMode']=False;obj.update_tag()
physics.direct_pose(rig,'rest_settle')
def weights(obj):return [{obj.vertex_groups[g.group].name:g.weight for g in v.groups} for v in obj.data.vertices]
def structural(obj):
    return {'faces':[list(p.vertices) for p in obj.data.polygons],
      'uvs':[[list(v.uv) for v in layer.data] for layer in obj.data.uv_layers],
      'colors':{a.name:[list(v.color) for v in a.data] for a in obj.data.color_attributes},
      'materials':[m.name if m else None for m in obj.data.materials]}
def normalize(row):
    row=dict(sorted(((n,max(0,w)) for n,w in row.items() if w>1e-8),key=lambda r:r[1],reverse=True)[:4]);total=sum(row.values())
    return {n:w/total for n,w in row.items()}
def apply_weights(obj,rows):
    indices=list(range(len(obj.data.vertices)))
    for group in obj.vertex_groups:group.remove(indices)
    for i,row in enumerate(rows):
        for name,w in row.items():
            if w<=1e-8:continue
            group=obj.vertex_groups.get(name) or obj.vertex_groups.new(name=name);group.add([i],float(w),'REPLACE')
objects={o.name:o for o in scene.objects if o.type=='MESH'}
structure={n:structural(o) for n,o in objects.items()}
original={n:np.array([v.co[:] for v in o.data.vertices]) for n,o in objects.items()}
old_weights={n:weights(o) for n,o in objects.items()}
restbones={b.name:[list(r) for r in b.matrix_local] for b in rig.data.bones}
altered={n:geometry(original[n],.145) for n in NAMES}
new_weights={n:old_weights[n] if 'Robe' in n else coherent(altered[n],old_weights[n],1.24,1.34,.28) for n in NAMES}
solution=json.loads(SOLUTION.read_text());assert solution['sourceSha256']==sha(SOURCE)
for row in solution['variables']:new_weights[row['mesh']][row['vertex']]=normalize(row['afterWeights'])

# The numerical correction belongs to the same garment seam on BodyCore too.
# Exact coincident source seams copy final weights. Elsewhere a compact spatial
# interpolation of only the small solved delta fades back within twelve mm.
body_transfer=[];samples=solution['variables'];sample_points=np.array([r['rest'] for r in samples])
for i,p in enumerate(altered['DosaV2_BodyCore']):
    distances=np.linalg.norm(sample_points-p,axis=1);nearest=int(np.argmin(distances));distance=distances[nearest]
    if distance>=.012:continue
    before=new_weights['DosaV2_BodyCore'][i]
    if distance<1e-5:
        after=normalize(samples[nearest]['afterWeights']);mode='coincident seam final weights'
    else:
        selected=np.argsort(distances)[:8];influence=(1-smooth(0,.012,distances[selected]))/np.maximum(distances[selected],1e-5)**2
        if influence.sum()<=1e-15:continue
        influence/=influence.sum();delta={}
        for index,factor in zip(selected,influence):
            row=samples[index]
            for bone in set(row['afterWeights'])|set(row['originalWeights']):
                delta[bone]=delta.get(bone,0)+factor*(row['afterWeights'].get(bone,0)-row['originalWeights'].get(bone,0))
        fade=float(1-smooth(0,.012,distance));after=normalize({bone:before.get(bone,0)+fade*delta.get(bone,0) for bone in set(before)|set(delta)})
        mode='twelve mm compact delta field'
    if max(abs(after.get(b,0)-before.get(b,0)) for b in set(after)|set(before))<1e-8:continue
    new_weights['DosaV2_BodyCore'][i]=after
    body_transfer.append({'vertex':i,'nearestSleeve':samples[nearest]['mesh'],'nearestSleeveVertex':samples[nearest]['vertex'],
      'distanceMeters':float(distance),'mode':mode,'before':before,'after':after})

for name in NAMES:
    obj=objects[name]
    for v,p in zip(obj.data.vertices,altered[name]):v.co=p
    if 'Robe' not in name:apply_weights(obj,new_weights[name])
    obj.data.update()
assert structure=={n:structural(o) for n,o in objects.items()}
for n,o in objects.items():
    if n not in NAMES:
        assert np.array_equal(original[n],np.array([v.co[:] for v in o.data.vertices])) and old_weights[n]==weights(o)
assert old_weights['DosaV2_Robe_Combined']==weights(objects['DosaV2_Robe_Combined'])
assert restbones=={b.name:[list(r) for r in b.matrix_local] for b in rig.data.bones}
assert not bpy.data.actions
DEST=OUT/'DosaV2_ContinuousGusset.blend';bpy.ops.wm.save_as_mainfile(filepath=str(DEST))
report={'status':'NECESSARY_CONSTRAINT_CANDIDATE_PENDING_ACTUAL_BLENDER_AND_NATIVE_PHYSICS',
  'source':str(SOURCE),'sourceSha256':sha(SOURCE),'output':str(DEST),'outputSha256':sha(DEST),'scriptSha256':sha(__file__),
  'helperSha256':sha(HELPER),'localSolutionSha256':sha(SOLUTION),
  'parameters':{'underarmTargetX':.145,'waistTargetX':.195,'chestPlateauEndZ':1.24,'chestFadeEndZ':1.34,'chestFadeEndX':.28,'chestLowerFade':[1.,1.08]},
  'geometryChanges':{},'weightChanges':{},'bodySeamLocalTransfer':body_transfer,
  'preserved':{'allTopologyUVMaterialsAndMobilityExact':True,'allOtherMeshesExact':True,'boneRestExact':True,'RobeWeightsExact':True,'productionActions':0},
  'limitations':['Necessary edge-length bound is not proof of a stable collision-free cloth solve.',
    '1.340002 predicted maximum leaves less than one percent margin under the fixed 1.35 native stretch criterion.',
    'Raw FK folded clothing can self-intersect; static inner-pin clearance does not certify free cloth or every surface intersection.']}
for name in NAMES:
    current=np.array([v.co[:] for v in objects[name].data.vertices]);delta=np.linalg.norm(current-original[name],axis=1)
    report['geometryChanges'][name]={'vertices':int(np.sum(delta>1e-8)),'maximumMeters':float(np.max(delta)),
      'changes':[{'sourceVertex':int(i),'before':original[name][i].tolist(),'after':current[i].tolist()} for i in np.flatnonzero(delta>1e-8)]}
    currentweights=weights(objects[name]);changed=[]
    for i,(a,b) in enumerate(zip(old_weights[name],currentweights)):
        d=max([0]+[abs(a.get(n,0)-b.get(n,0)) for n in set(a)|set(b)])
        if d>1e-7:changed.append({'sourceVertex':i,'maximumWeightDelta':d,'before':a,'after':b})
    report['weightChanges'][name]={'vertices':len(changed),'maximumWeightDelta':max([0]+[r['maximumWeightDelta'] for r in changed]),'changes':changed}
(OUT/'source-report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
(OUT/'rebuild-source.py').write_bytes(Path(__file__).read_bytes())
print('DERIVATIVE_SAVED '+str(DEST),flush=True)

# Re-evaluate the saved model with Blender Armature modifiers, not only the
# offline linear predictor. Closed anatomy trees use the actual evaluated mesh.
definitions=json.loads((ROOT/'Art/PlayerV2/Validation/static-pose-definitions.json').read_text())['poses']
def pose(name):
    physics.direct_pose(rig,'rest_settle')
    if name.startswith('fixture_'):physics.direct_pose(rig,name.replace('fixture_',''))
    else:
        definition=next(d for d in definitions if d['id']==name)
        for row in definition['boneRotations']:rig.pose.bones[row['bone']].matrix_basis=Euler([math.radians(row[c]) for c in ('x','y','z')],'XYZ').to_matrix().to_4x4()
        keys=bpy.data.objects['DosaV2_Hands'].data.shape_keys
        if keys:
            for side in ('Right','Left'):
                key=keys.key_blocks.get('GripPalmRelax_'+side)
                if key:key.value=definition.get('handGripCorrectives',{}).get(side.lower(),0.)
    bpy.context.view_layer.update()
directions=[Vector(d).normalized() for d in [(1,.371,.219),(.173,1,.293),(.271,.123,1)]]
def inside(tree,p):
    votes=0
    for ray in directions:
        origin=p+ray*1e-6;count=0
        for _ in range(64):
            hit=tree.ray_cast(origin,ray,10)
            if hit[0] is None:break
            count+=1;origin=hit[0]+ray*5e-6
        votes+=count%2
    return votes>=2
rows=[]
for name in [d['id'] for d in definitions]+['fixture_rest_settle','fixture_grip_settle','fixture_raised_arms_settle']:
    pose(name);deps=bpy.context.evaluated_depsgraph_get();evaluated={};triangles={}
    for n,o in objects.items():
        if n not in NAMES and 'Lining' not in n:continue
        ev=o.evaluated_get(deps);me=ev.to_mesh();me.calc_loop_triangles()
        evaluated[n]=np.array([list(ev.matrix_world@v.co) for v in me.vertices]);triangles[n]=[list(t.vertices) for t in me.loop_triangles];ev.to_mesh_clear()
    trees=[(n,BVHTree.FromPolygons(p.tolist(),triangles[n],all_triangles=True)) for n,p in evaluated.items() if 'Lining' in n]
    for n in NAMES:
        obj=objects[n];mob=obj.data.color_attributes.get('ClothMobility')
        if not mob:continue
        rest=np.array([list(obj.matrix_world@v.co) for v in obj.data.vertices]);points=evaluated[n]
        edges=np.array(sorted({tuple(sorted((a,b))) for t in triangles[n] for a,b in zip(t,t[1:]+t[:1])}))
        m=np.array([v.color[0] for v in mob.data]);lengths=np.linalg.norm(rest[edges[:,0]]-rest[edges[:,1]],axis=1)
        distances=np.linalg.norm(points[edges[:,0]]-points[edges[:,1]],axis=1);ratios=np.maximum(0,distances-m[edges[:,0]]-m[edges[:,1]])/np.maximum(lengths,1e-30)
        hits=[]
        for i in np.flatnonzero(m==0):
            p=Vector(points[i])
            for lining,tree in trees:
                loc,norm,face,depth=tree.find_nearest(p)
                if inside(tree,p):hits.append({'vertex':int(i),'lining':lining,'depthMeters':depth,'point':list(p)})
        worst=[]
        for i in np.argsort(ratios)[-4:][::-1]:worst.append({'vertices':edges[i].tolist(),'ratio':float(ratios[i]),'restLengthMeters':float(lengths[i]),'skinTargetDistanceMeters':float(distances[i]),'mobilityMeters':m[edges[i]].tolist()})
        rows.append({'poseId':name,'surface':obj.name,'maximumMinimumRequiredStretchRatio':float(np.max(ratios)),
          'infeasibleEdges':int(np.sum(ratios>1.35)),'exactPins':int(np.sum(m==0)),'actualInsidePins':hits,'worstEdges':worst})
    print('ACTUAL_POSE '+name,flush=True)
actual={'status':'ACTUAL_BLENDER_STATIC_NECESSARY_CONSTRAINTS_MEASURED_NOT_PHYSICS_PASS','sourceSha256':report['outputSha256'],
  'poseCount':25,'maximumMinimumRequiredStretchRatio':max(r['maximumMinimumRequiredStretchRatio'] for r in rows),
  'infeasibleEdgeCases':sum(r['infeasibleEdges'] for r in rows),'actualInsidePinCases':sum(len(r['actualInsidePins']) for r in rows),
  'method':'Actual evaluated Blender Armature skin targets; all triangle edges and exact mobility; three-ray closed actual inner-mesh parity. No collision proxy fitting and no excluded source edges.','rows':rows}
(OUT/'actual-static-25.json').write_text(json.dumps(actual,indent=2),encoding='utf-8')
print('ACTUAL_SUMMARY '+json.dumps({k:v for k,v in actual.items() if k!='rows'}),flush=True)
camera=scene.camera or render_helper.lighting(scene)
for name in ['rest','open_hand','grip_down','combined_reach','fixture_raised_arms_settle']:
    pose(name);render_helper.render(scene,camera,OUT/(name+'-front.png'),(0,-.01,1.2),1.00,math.pi*.12,width=1100,height=880)
    print('RENDER '+name,flush=True)
print('READY_STATIC_CANDIDATE '+report['outputSha256'],flush=True)
