"""Read-only exact source, seam, pin/anatomy and 429-pose envelope audit."""
import bpy,json,hashlib,ast,os
import numpy as np
from pathlib import Path
from mathutils import Matrix,Vector
from mathutils.bvhtree import BVHTree
ROOT=Path(__file__).resolve().parents[3];BASE=ROOT/'Art/PlayerV2/Inspect/ClothBlender';VARIANT=os.environ.get('DOSA_PANEL_VARIANT','original31');OUT=BASE/'FreeHemPanel' if VARIANT=='original31' else BASE/('TorsoSupportedPanel/SeamLineCandidate' if VARIANT=='torso10line' else 'TorsoSupportedPanel/Candidate')
SOURCE=OUT/'DosaV2_FreeHemPanel_Candidate.blend';OLD=BASE/'VerifiedAttachments/Inputs/Assembled-d979b9bc.blend'
def capture(path):
    bpy.ops.wm.open_mainfile(filepath=str(path));rig=bpy.data.objects['DosaV2_Rig']
    for o in bpy.context.scene.objects:
        if o.name.startswith('CTRL_') and 'AuthoringMode' in o:o['AuthoringMode']=False;o.update_tag()
    for b in rig.pose.bones:b.matrix_basis=Matrix.Identity(4)
    models={}
    for o in bpy.context.scene.objects:
        if o.type!='MESH' or not o.name.startswith(('DosaV2_','DosaPackV2_')) or o.name=='DosaV2_SourceSurface':continue
        o.data.calc_loop_triangles();tri=np.array([list(t.vertices) for t in o.data.loop_triangles]);mob=o.data.color_attributes.get('ClothMobility')
        models[o.name]={'rest':np.array([v.co[:] for v in o.data.vertices]),'triangles':tri,
          'faces':[list(p.vertices) for p in o.data.polygons],
          'uv':np.array([list(v.uv) for v in o.data.uv_layers.active.data]) if o.data.uv_layers.active else np.array([]),
          'materials':[m.name if m else None for m in o.data.materials],
          'materialIndices':[p.material_index for p in o.data.polygons],
          'weights':[{o.vertex_groups[g.group].name:g.weight for g in v.groups} for v in o.data.vertices],
          'mobility':np.array([v.color[0] for v in mob.data]) if mob else None,
          'edges':np.array(sorted({tuple(sorted((int(a),int(b)))) for t in tri for a,b in zip(t,np.roll(t,-1))}))}
    bones={b.name:{'matrix':np.array(b.matrix_local).tolist(),'parent':b.parent.name if b.parent else None,'deform':b.use_deform} for b in rig.data.bones}
    return models,bones
old,oldbones=capture(OLD);models,bones=capture(SOURCE);assert bones==oldbones
allowed={'DosaV2_BodyCore','DosaV2_SleeveOuter_L','DosaV2_SleeveOuter_R'};unchanged=[]
for name,m in models.items():
    if name in allowed:continue
    prior=old[name]
    for key in ['rest','triangles','uv']:
        assert np.array_equal(m[key],prior[key]),(name,key)
    for key in ['weights','materials','materialIndices','faces']:assert m[key]==prior[key],(name,key)
    if m['mobility'] is not None:assert np.array_equal(m['mobility'],prior['mobility'])
    unchanged.append(name)
ledger=json.loads((OUT/'authoring-ledger.json').read_text());mapped=ledger['bodyRetainedSourceVertexMap'];body=models['DosaV2_BodyCore'];prior=old['DosaV2_BodyCore']
assert np.array_equal(body['rest'],prior['rest'][mapped]);assert body['weights']==[prior['weights'][i] for i in mapped]
for side in ['L','R']:
    name='DosaV2_SleeveOuter_'+side;n=len(old[name]['rest']);assert np.array_equal(models[name]['rest'][:n],old[name]['rest'])
for row in ledger['transferredFaces']:
    sf=row['bodySourceFace'];df=row['outerDestinationFace'];sverts=old['DosaV2_BodyCore']['faces'][sf];dverts=models['DosaV2_SleeveOuter_R']['faces'][df]
    assert np.array_equal(old['DosaV2_BodyCore']['rest'][sverts],models['DosaV2_SleeveOuter_R']['rest'][dverts])
    assert np.array_equal(old['DosaV2_BodyCore']['uv'][sf*3:(sf+1)*3],models['DosaV2_SleeveOuter_R']['uv'][df*3:(df+1)*3])

rinv={n:np.linalg.inv(np.array(b['matrix'])) for n,b in bones.items()};POSE=BASE/'FoldedGusset';poses=[]
for d in json.loads((POSE/'Inputs/static-pose-definitions.json').read_text())['poses']:
    poses.append((d['id'],{n:np.array(m)@rinv[n] for n,m in d['boneMatricesRigLocal'].items()}))
for file,label in [('fixture_grip_settle_00','fixture_rest_settle'),('fixture_grip_settle_100','fixture_grip_settle'),('fixture_raised_arms_settle_100','fixture_raised_arms_settle')]:
    d=json.loads((POSE/'IntermediatePoseData'/(file+'.json')).read_text());poses.append((label,{n:np.array(m)@rinv[n] for n,m in d['bonePoseMatrices'].items()}))
for file in sorted((POSE/'IntermediatePoseData').glob('*.json')):
    d=json.loads(file.read_text());poses.append((d['poseId'],{n:np.array(m)@rinv[n] for n,m in d['bonePoseMatrices'].items()}))
assert len(poses)==429
(OUT/'rest-geometry.json').write_text(json.dumps({'bones':bones,'meshes':{n:{'points':m['rest'].tolist(),'weights':m['weights'],'triangles':m['triangles'].tolist(),'mobility':m['mobility'].tolist() if m['mobility'] is not None else None} for n,m in models.items() if n in allowed or 'Lining' in n}}),encoding='utf-8')
def posed(m):
    p=np.column_stack((m['rest'],np.ones(len(m['rest']))));result=np.zeros((len(poses),len(p),3))
    for bone in set(n for w in m['weights'] for n in w):
        w=np.array([r.get(bone,0) for r in m['weights']]);mat=np.array([mat[bone] for _,mat in poses]);result+=np.einsum('pij,vj->pvi',mat[:,:3,:],p)*w[None,:,None]
    return result
cloth=['DosaV2_SleeveOuter_L','DosaV2_SleeveOuter_R'];anatomy=[n for n in models if 'Lining' in n];needed=cloth+anatomy+['DosaV2_BodyCore','DosaV2_SleeveInner_L','DosaV2_SleeveInner_R'];points={n:posed(models[n]) for n in needed}
path=Path(__file__).with_name('fit_gusset_anchor_field.py');nodes=[n for n in ast.parse(path.read_text()).body if isinstance(n,ast.FunctionDef) and n.name=='inside'];exec(compile(ast.Module(body=nodes,type_ignores=[]),str(path),'exec'))
directions=[Vector(d).normalized() for d in [(1,.371,.219),(.173,1,.293),(.271,.123,1)]]
rows=[]
for k,(pose,mat) in enumerate(poses):
    trees={n:BVHTree.FromPolygons(points[n][k].tolist(),models[n]['triangles'].tolist(),all_triangles=True) for n in anatomy};hits=[];surfaces=[]
    for name in cloth:
        m=models[name];p=points[name][k];ids=np.flatnonzero(m['mobility']==0)
        for n,tree in trees.items():
            a=points[n][k];lo=np.min(a,axis=0);hi=np.max(a,axis=0);candidates=ids[np.all(p[ids]>=lo,axis=1)&np.all(p[ids]<=hi,axis=1)]
            for i in candidates:
                q=Vector(p[i])
                if inside(tree,q):hits.append({'cloth':name,'vertex':int(i),'anatomy':n,'depthMeters':tree.find_nearest(q)[3]})
        e=m['edges'];length=np.linalg.norm(m['rest'][e[:,0]]-m['rest'][e[:,1]],axis=1);ratio=np.maximum(0,np.linalg.norm(p[e[:,0]]-p[e[:,1]],axis=1)-m['mobility'][e[:,0]]-m['mobility'][e[:,1]])/length
        j=int(np.argmax(ratio));s=next(s for s in ledger['surfaces'] if s['mesh']==name)
        gaps=[{'clothVertex':int(i),'bodyVertex':v['bodyVertex'],'gapMeters':float(np.linalg.norm(p[int(i)]-points['DosaV2_BodyCore'][k,v['bodyVertex']]))} for i,v in s['sewingSeeds'].items()]
        surfaces.append({'mesh':name,'maximumNecessaryEdgeStretch':float(np.max(ratio)),'worstEdge':e[j].tolist(),
          'maximumAttachmentGapMeters':max(v['gapMeters'] for v in gaps),'worstAttachment':max(gaps,key=lambda v:v['gapMeters'])})
    rows.append({'poseId':pose,'pinAnatomyIntersections':hits,'surfaces':surfaces})
    if (k+1)%50==0:print('FREE_HEM_AUDIT '+str(k+1),flush=True)
for name in cloth:
    m=models[name]
    for suffix,count in [('25',25),('429',429)]:
        np.savez_compressed(OUT/(name+'-'+suffix+'.npz'),rest=m['rest'],edges=m['edges'],mobility=m['mobility'],posePoints=points[name][:count],poseNames=np.array([n for n,mat in poses[:count]]))
report={'status':'GEOMETRY_AND_ANATOMY_DIAGNOSTIC_NOT_NATIVE_CLOTH_PASS','sourceSha256':hashlib.sha256(SOURCE.read_bytes()).hexdigest(),'poseCount':429,'unchangedMeshes':unchanged,
 'boneRestParentDeformExact':True,'sourceFacePositionUVTransferExact':True,'worldTriangles':sum(len(m['triangles']) for m in models.values()),
 'pinAnatomyIntersectionCases':sum(len(r['pinAnatomyIntersections']) for r in rows),'maxPinAnatomyDepthMeters':max([0]+[v['depthMeters'] for r in rows for v in r['pinAnatomyIntersections']]),
 'maximumNecessaryEdgeStretch':max(s['maximumNecessaryEdgeStretch'] for r in rows for s in r['surfaces']),
 'maximumAttachmentGapMeters':max(s['maximumAttachmentGapMeters'] for r in rows for s in r['surfaces']),'rows':rows}
(OUT/'geometry-audit.json').write_text(json.dumps(report,indent=2),encoding='utf-8');print(json.dumps({k:v for k,v in report.items() if k!='rows'},indent=2),flush=True)
