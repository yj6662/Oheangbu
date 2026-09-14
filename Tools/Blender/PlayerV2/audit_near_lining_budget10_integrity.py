"""Independent whole-source integrity and posed closed-shell checks for near10."""
import bpy,json,hashlib,ast
import numpy as np
from pathlib import Path
from mathutils import Matrix
ROOT=Path(__file__).resolve().parents[3];BASE=ROOT/'Art/PlayerV2/Inspect/ClothBlender';OUT=BASE/'NearLiningBudget10'
SOURCE=BASE/'TorsoSupportedPanel/SeamLineCandidate/DosaV2_FreeHemPanel_Candidate.blend';NEAR=OUT/'DosaV2_NearArmLining10.blend';NAMES=['DosaV2_ArmLining_Left','DosaV2_ArmLining_Right']
def read(path):
    bpy.ops.wm.open_mainfile(filepath=str(path));rig=bpy.data.objects['DosaV2_Rig'];models={}
    for o in bpy.context.scene.objects:
        if o.type!='MESH' or not o.name.startswith(('DosaV2_','DosaPackV2_')) or o.name=='DosaV2_SourceSurface':continue
        me=o.data;me.calc_loop_triangles();models[o.name]={'points':[list(v.co) for v in me.vertices],
         'triangles':[list(t.vertices) for t in me.loop_triangles],'polygons':[list(p.vertices) for p in me.polygons],
         'groups':[g.name for g in o.vertex_groups], 'weights':[{o.vertex_groups[g.group].name:g.weight for g in v.groups} for v in me.vertices],
         'materials':[m.name if m else None for m in me.materials],'materialIndices':[p.material_index for p in me.polygons],
         'uv':{uv.name:[list(d.uv) for d in uv.data] for uv in me.uv_layers},'colors':{a.name:[list(c.color) for c in a.data] for a in me.color_attributes},
         'shapeKeys':len(me.shape_keys.key_blocks) if me.shape_keys else 0,'worldMatrix':[list(r) for r in o.matrix_world]}
    bones={b.name:{'matrix':[list(r) for r in b.matrix_local],'parent':b.parent.name if b.parent else None,'deform':b.use_deform} for b in rig.data.bones}
    return models,bones,len(bpy.data.actions)
old,bones,actions=read(SOURCE);new,nbones,nactions=read(NEAR);assert bones==nbones and actions==nactions==0
assert set(old)==set(new);unchanged=[]
for n,m in old.items():
    if n not in NAMES:assert m==new[n],n;unchanged.append(n)
    else:
        for field in ['groups','materials','shapeKeys','worldMatrix']:assert m[field]==new[n][field],(n,field)
        assert new[n]['shapeKeys']==0
path=Path(__file__).with_name('fit_gusset_anchor_field.py');nodes=[n for n in ast.parse(path.read_text()).body if isinstance(n,ast.FunctionDef) and n.name=='skin'];exec(compile(ast.Module(body=nodes,type_ignores=[]),str(path),'exec'))
rinv={n:np.linalg.inv(np.array(v['matrix'])) for n,v in bones.items()};P=BASE/'FoldedGusset';poses=[]
for d in json.loads((P/'Inputs/static-pose-definitions.json').read_text())['poses']:poses.append((d['id'],{n:np.array(m)@rinv[n] for n,m in d['boneMatricesRigLocal'].items()}))
for file,label in [('fixture_grip_settle_00','fixture_rest_settle'),('fixture_grip_settle_100','fixture_grip_settle'),('fixture_raised_arms_settle_100','fixture_raised_arms_settle')]:
    d=json.loads((P/'IntermediatePoseData'/(file+'.json')).read_text());poses.append((label,{n:np.array(m)@rinv[n] for n,m in d['bonePoseMatrices'].items()}))
for file in sorted((P/'IntermediatePoseData').glob('*.json')):
    d=json.loads(file.read_text());poses.append((d['poseId'],{n:np.array(m)@rinv[n] for n,m in d['bonePoseMatrices'].items()}))
checks=[]
for name in NAMES:
    m=new[name];tri=np.array(m['triangles']);rest=np.array(m['points']);counts={}
    for t in tri:
        for a,b in zip(t,np.roll(t,-1)):
            e=tuple(sorted((int(a),int(b))));counts[e]=counts.get(e,0)+1
    assert set(counts.values())=={2};euler=len(rest)-len(counts)+len(tri);assert euler==2
    minimum_area=float('inf');minimum_volume=float('inf');max_inf=max(map(len,m['weights']));assert max_inf<=4
    for pose,mat in poses:
        points=skin(rest,m['weights'],mat);assert np.all(np.isfinite(points));p=points[tri]
        area=np.linalg.norm(np.cross(p[:,1]-p[:,0],p[:,2]-p[:,0]),axis=1)*.5;volume=float(np.sum(np.einsum('ij,ij->i',p[:,0],np.cross(p[:,1],p[:,2])))/6)
        minimum_area=min(minimum_area,float(np.min(area)));minimum_volume=min(minimum_volume,volume)
    assert minimum_area>1e-10 and minimum_volume>0
    checks.append({'mesh':name,'vertices':len(rest),'triangles':len(tri),'closedEveryEdgeTwoTriangles':True,'eulerCharacteristic':euler,
     'shapeKeys':0,'maxInfluences':max_inf,'maxWeightSumError':max(abs(sum(w.values())-1) for w in m['weights']),
     'materialNamesExact':m['materials'],'vertexGroupNamesAndIndicesExact':True,
     'minimumPosedTriangleAreaSquareMeters':minimum_area,'minimumPosedSignedVolumeCubicMeters':minimum_volume})
report={'status':'NEAR10_SOURCE_INTEGRITY_AND_CLOSED_SHELL_CHECKED_NOT_RIG_PASS','sourceSha256':hashlib.sha256(SOURCE.read_bytes()).hexdigest(),'nearSha256':hashlib.sha256(NEAR.read_bytes()).hexdigest(),
 'unchangedOtherRenderers':unchanged,'boneRestParentDeformExact':True,'boneCount':len(bones),'productionActions':nactions,'poseCount':len(poses),'parts':checks,
 'meshDataOnlyOverrideContract':'Existing objects retain their original group definitions and armature binding; only these two mesh data blocks may be exchanged. Group indices, not names inferred from weighted subsets, were compared exactly.'}
(OUT/'integrity-report.json').write_text(json.dumps(report,indent=2),encoding='utf-8');print(json.dumps({k:v for k,v in report.items() if k!='unchangedOtherRenderers'},indent=2),flush=True)
