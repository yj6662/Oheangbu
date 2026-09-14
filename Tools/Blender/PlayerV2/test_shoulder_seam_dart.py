"""Actual local front underarm fabric shaping, keeping the complete anatomy."""
import bpy,json,ast,hashlib
from pathlib import Path
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree
ROOT=Path(__file__).resolve().parents[3];BASE=ROOT/'Art/PlayerV2/Inspect/ClothBlender/FoldedGusset';OUT=ROOT/'Art/PlayerV2/Inspect/ClothBlender/ShoulderConnection'
SOURCE=OUT/'DosaV2_ShoulderConnection_Candidate.blend';bpy.ops.wm.open_mainfile(filepath=str(SOURCE));rig=bpy.data.objects['DosaV2_Rig']
helper=Path(__file__).with_name('fit_gusset_anchor_field.py');nodes=[n for n in ast.parse(helper.read_text()).body if isinstance(n,ast.FunctionDef) and n.name in ['skin','inside']];exec(compile(ast.Module(body=nodes,type_ignores=[]),str(helper),'exec'))
directions=[Vector(d).normalized() for d in [(1,.371,.219),(.173,1,.293),(.271,.123,1)]]
models={};clothnames=['DosaV2_Robe_Combined','DosaV2_SleeveOuter_L','DosaV2_SleeveOuter_R']
for o in bpy.context.scene.objects:
    if o.type!='MESH' or not ('Lining' in o.name or o.name in clothnames+['DosaV2_BodyCore']):continue
    o.data.calc_loop_triangles();mob=o.data.color_attributes.get('ClothMobility')
    models[o.name]={'rest':np.array([v.co[:] for v in o.data.vertices]),'weights':[{o.vertex_groups[g.group].name:g.weight for g in v.groups} for v in o.data.vertices],
      'triangles':np.array([list(t.vertices) for t in o.data.loop_triangles]),'edges':np.array([list(e.vertices) for e in o.data.edges]),'mobility':np.array([v.color[0] for v in mob.data]) if mob else None}
rinv={b.name:np.linalg.inv(np.array(b.matrix_local)) for b in rig.data.bones};poses=[]
for path in sorted((BASE/'IntermediatePoseData').glob('*.json')):
    p=json.loads(path.read_text());mat={n:np.array(m)@rinv[n] for n,m in p['bonePoseMatrices'].items()};anatomy={}
    for n,m in models.items():
        if 'Lining' not in n:continue
        points=skin(m['rest'],m['weights'],mat);anatomy[n]={'tree':BVHTree.FromPolygons(points.tolist(),m['triangles'].tolist(),all_triangles=True),'lo':np.min(points,axis=0),'hi':np.max(points,axis=0)}
    poses.append((p['poseId'],mat,anatomy))
origin=models['DosaV2_SleeveOuter_L']['rest'][340].copy();radius=.030;trials=[]
for amplitude in [.006,.008,.010]:
    trial={n:{'rest':m['rest'].copy(),'weights':[r.copy() for r in m['weights']]} for n,m in models.items()}
    for n in ['DosaV2_SleeveOuter_L','DosaV2_BodyCore']:
        p=trial[n]['rest'];base=p.copy();d=np.linalg.norm(base-origin,axis=1);bump=np.maximum(0,1-(d/radius)**2)**2;p[:,1]-=amplitude*bump
        for source_vertex,shift in [(28,np.array([0.,.001,0.])),(343,np.array([0.,0.,-.003]))]:
            center=models['DosaV2_SleeveOuter_L']['rest'][source_vertex];distance=np.linalg.norm(base-center,axis=1);field=np.maximum(0,1-(distance/.030)**2)**2;p+=field[:,None]*shift
    rows=[]
    for poseid,mat,anatomy in poses:
        records=[];maximum=0.
        for n in clothnames:
            m=models[n];p=skin(trial[n]['rest'],trial[n]['weights'],mat);mob=m['mobility'];edges=m['edges'];rest=trial[n]['rest']
            ratio=np.maximum(0,np.linalg.norm(p[edges[:,0]]-p[edges[:,1]],axis=1)-mob[edges[:,0]]-mob[edges[:,1]])/np.linalg.norm(rest[edges[:,0]]-rest[edges[:,1]],axis=1);maximum=max(maximum,float(np.max(ratio)))
            ids=np.flatnonzero(mob==0)
            for lining,a in anatomy.items():
                candidates=ids[np.all(p[ids]>=a['lo'],axis=1)&np.all(p[ids]<=a['hi'],axis=1)]
                for i in candidates:
                    point=Vector(p[i])
                    if inside(a['tree'],point):records.append({'cloth':n,'vertex':int(i),'lining':lining,'depthMeters':a['tree'].find_nearest(point)[3]})
        rows.append({'poseId':poseid,'maximumNecessaryStretch':maximum,'pinsInside':records})
    result={'amplitudeMeters':amplitude,'radiusMeters':radius,'center':origin.tolist(),'pinCasesInside':sum(len(r['pinsInside']) for r in rows),
      'maximumInsideDepthMeters':max([0]+[h['depthMeters'] for r in rows for h in r['pinsInside']]),'maximumNecessaryStretch':max(r['maximumNecessaryStretch'] for r in rows),'rows':rows}
    trials.append(result);print('DART '+json.dumps({k:v for k,v in result.items() if k!='rows'}),flush=True)
    (OUT/'dart-trials-geometry-only.json').write_text(json.dumps({'sourceSha256':hashlib.sha256(SOURCE.read_bytes()).hexdigest(),'status':'GENUINE_FABRIC_DART_TRIAL_NO_THRESHOLD_ANATOMY_OR_SKIN_CHANGES','trials':trials},indent=2),encoding='utf-8')
    if result['pinCasesInside']==0 and result['maximumNecessaryStretch']<=1.35:
        changed={}
        for n in ['DosaV2_SleeveOuter_L','DosaV2_BodyCore']:
            o=bpy.data.objects[n];changed[n]=[]
            for i,(before,after) in enumerate(zip(models[n]['rest'],trial[n]['rest'])):
                if np.linalg.norm(before-after)>1e-8:changed[n].append({'vertex':i,'before':before.tolist(),'after':after.tolist()})
                o.data.vertices[i].co=after
        DEST=OUT/'DosaV2_ShoulderConnection_Dart.blend';bpy.ops.wm.save_as_mainfile(filepath=str(DEST))
        (OUT/'dart-source-changes.json').write_text(json.dumps({'outputSha256':hashlib.sha256(DEST.read_bytes()).hexdigest(),'selectedAmplitudeMeters':amplitude,'changes':changed,'weightChanges':0},indent=2),encoding='utf-8')
        print('DART_FEASIBLE_SAVED',flush=True);break
