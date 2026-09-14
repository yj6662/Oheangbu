"""Global rest-path feasibility of continuous underarm free-fabric depth trials."""
import numpy as np,json,heapq,math,time
from pathlib import Path
ROOT=Path(__file__).resolve().parents[3];BASE=ROOT/'Art/PlayerV2/Inspect/ClothBlender/FoldedGusset';DATA=ROOT/'Art/PlayerV2/Inspect/ClothBlender/GlobalPathFeasibility';OUT=ROOT/'Art/PlayerV2/Inspect/ClothBlender/GlobalEnvelope';OUT.mkdir(parents=True,exist_ok=True)
geometry=json.loads((ROOT/'Art/PlayerV2/Inspect/ClothBlender/ShoulderConnection/geometry-rest.json').read_text());restinv={n:np.linalg.inv(np.array(m)) for n,m in geometry['boneRestMatrices'].items()}
definition=json.loads((BASE/'Inputs/static-pose-definitions.json').read_text());poses=[]
for p in definition['poses']:poses.append({n:np.array(m)@restinv[n] for n,m in p['boneMatricesRigLocal'].items()})
for name in ['fixture_grip_settle_00','fixture_grip_settle_100','fixture_raised_arms_settle_100']:
    p=json.loads((BASE/'IntermediatePoseData'/(name+'.json')).read_text());poses.append({n:np.array(m)@restinv[n] for n,m in p['bonePoseMatrices'].items()})
fold_report=json.loads((BASE/'source-report.json').read_text());folds={r['mesh']:r['added'] for r in fold_report['foldTopology']};trials=[]
def smooth(lo,hi,x):
    t=np.clip((x-lo)/(hi-lo),0,1);return t*t*(3-2*t)
def audit(rest,edges,mob,points,pose_names):
    count=len(rest);adj=[[] for _ in rest]
    for (a,b),d in zip(edges,np.linalg.norm(rest[edges[:,0]]-rest[edges[:,1]],axis=1)):adj[a].append((int(b),float(d)));adj[b].append((int(a),float(d)))
    maximum=0.;fail=0;worst=None
    for source in np.arange(count):
        dist=np.full(count,np.inf);dist[source]=0;parents=np.full(count,-1);queue=[(0.,int(source))]
        while queue:
            value,i=heapq.heappop(queue)
            if value>dist[i]:continue
            for j,length in adj[i]:
                v=value+length
                if v<dist[j]:dist[j]=v;parents[j]=i;heapq.heappush(queue,(v,j))
        valid=np.isfinite(dist)&(dist>1e-10);required=np.maximum(0,np.linalg.norm(points-points[:,source,None,:],axis=2)-mob[None,:]-mob[source]);ratio=np.zeros_like(required);ratio[:,valid]=required[:,valid]/dist[None,valid];fail+=int(np.sum(ratio>1.35));p,target=np.unravel_index(np.argmax(ratio),ratio.shape)
        if ratio[p,target]>maximum:
            maximum=float(ratio[p,target]);path=[];j=target
            while j>=0:path.append(int(j));j=int(parents[j])
            worst={'pose':str(pose_names[p]),'source':int(source),'target':int(target),'path':path[::-1],'necessaryRatio':maximum,'pathMeters':float(dist[target]),'neededMeters':float(required[p,target])}
    return {'maximumNecessaryPathRatio':maximum,'violatingAllVertexPairs':fail,'worst':worst}
for name in ['DosaV2_SleeveOuter_L','DosaV2_SleeveOuter_R']:
    data=np.load(DATA/(name+'.npz'));original=data['rest'];edges=data['edges'];mob=data['mobility'];before=data['posePoints'][:25];weights=geometry['models'][name]['weights']
    matrices=np.zeros((25,len(original),3,3))
    for bone in set(n for w in weights for n in w):
        w=np.array([r.get(bone,0) for r in weights]);matrices+=np.array([p[bone][:3,:3] for p in poses])[:,None,:,:]*w[None,:,None,None]
    x=np.abs(original[:,0]);y=original[:,1];z=original[:,2]
    profile=smooth(.135,.17,x)*(1-smooth(.245,.29,x))*smooth(1.14,1.22,z)*(1-smooth(1.30,1.345,z))*(1-smooth(-.012,.005,y))
    profile[mob==0]=0
    side='Left' if name.endswith('_L') else 'Right';head=np.array(geometry['boneRestMatrices'][side+'Arm'])[:3,3];end=np.array(geometry['boneRestMatrices'][side+'ForeArm'])[:3,3];axis=(end-head)/np.linalg.norm(end-head)
    radial=original-head;radial-=np.sum(radial*axis,axis=1)[:,None]*axis;radial/=np.maximum(1e-10,np.linalg.norm(radial,axis=1))[:,None]
    for depth in [.030]:
        rest=original.copy();rest[:,1]-=depth*profile
        crown=np.sin(np.pi*np.clip((x-.265)/.090,0,1))**2;crown[(x<=.265)|(x>=.355)|(mob==0)]=0
        rest+=.035*crown[:,None]*radial
        for record in folds[name]:
            a,b=record['sourceVertices'];i=record['newVertex'];rest[i]=original[i]+((rest[a]-original[a])+(rest[b]-original[b]))*.5
            if name.endswith('_L') and i in [891,904]:rest[i]+=(original[i]-(original[a]+original[b])*.5)*1.25
        adj=[[] for _ in rest]
        for (i,j),length in zip(edges,np.linalg.norm(rest[edges[:,0]]-rest[edges[:,1]],axis=1)):adj[i].append((int(j),float(length)));adj[j].append((int(i),float(length)))
        distance=np.full(len(rest),np.inf);queue=[]
        for i in np.flatnonzero(mob==0):distance[i]=0.;queue.append((0.,int(i)))
        heapq.heapify(queue)
        while queue:
            value,i=heapq.heappop(queue)
            if value>distance[i]:continue
            for j,length in adj[i]:
                d=value+length
                if d<distance[j]:distance[j]=d;heapq.heappush(queue,(d,j))
        newmob=mob.copy();finite=np.isfinite(distance);newmob[finite]=np.maximum(mob[finite],.18*smooth(0.,.05,distance[finite]));newmob[mob==0]=0
        delta=rest-original;points=before+np.einsum('pvij,vj->pvi',matrices,delta);a=audit(rest,edges,newmob,points,data['poseNames'][:25]);a.update({'mesh':name,'depthMeters':depth,'changedVertices':int(np.sum(np.linalg.norm(delta,axis=1)>1e-8)),
          'unanchoredVertices':int(np.sum(~finite)),'oldMaximumMobility':float(np.max(mob)),'newMaximumMobility':float(np.max(newmob)),'changedMobilityVertices':int(np.sum(newmob>mob+1e-8))});trials.append(a)
        file=OUT/(name+'-candidate.npz');np.savez_compressed(file,rest=rest,edges=edges,mobility=newmob,originalMobility=mob,posePoints=points,poseNames=data['poseNames'][:25],geodesicDistanceToSeam=distance);print(json.dumps(a),flush=True)
        (OUT/'candidate-global-paths.json').write_text(json.dumps({'status':'ALL_VERTEX_PAIR_TEST_WITH_GEODESICALLY_AUTHORED_FREE_CLOTH_ENVELOPE','policy':'Exact seam pins remain0; free radius=max(original,0.18m*smoothstep(0,0.05m,rest geodesic distance to nearest existing exact pin)). Unanchored components keep original mobility and remain explicitly counted. Local30mm underarm geometry adds true pin-to-pin fabric length; all source skin weights unchanged. Physical acceptance remains unchanged.','trials':trials},indent=2),encoding='utf-8')
