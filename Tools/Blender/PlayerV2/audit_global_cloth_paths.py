"""Exact rest-geodesic necessary length test from every pin to every vertex."""
import numpy as np,json,heapq,time,argparse,hashlib
from pathlib import Path
ROOT=Path(__file__).resolve().parents[3];OUT=ROOT/'Art/PlayerV2/Inspect/ClothBlender/GlobalPathFeasibility'
parser=argparse.ArgumentParser();parser.add_argument('--mesh',required=True);parser.add_argument('--dense',action='store_true');parser.add_argument('--data-dir');parser.add_argument('--all-sources',action='store_true');args=parser.parse_args()
if args.data_dir:OUT=Path(args.data_dir)
file=OUT/(args.mesh+'.npz');data=np.load(file);rest=data['rest'];edges=data['edges'];mob=data['mobility'];points=data['posePoints'];names=data['poseNames']
if not args.dense:points=points[:25];names=names[:25]
count=len(rest);adj=[[] for _ in range(count)]
for (a,b),length in zip(edges,np.linalg.norm(rest[edges[:,0]]-rest[edges[:,1]],axis=1)):
    adj[a].append((int(b),float(length)));adj[b].append((int(a),float(length)))
def distances(source):
    d=np.full(count,np.inf);d[source]=0.;parent=np.full(count,-1,dtype=int);heap=[(0.,int(source))]
    while heap:
        value,v=heapq.heappop(heap)
        if value>d[v]:continue
        for neighbor,length in adj[v]:
            candidate=value+length
            if candidate<d[neighbor]:d[neighbor]=candidate;parent[neighbor]=v;heapq.heappush(heap,(candidate,neighbor))
    return d,parent
pins=np.flatnonzero(mob==0);sources=np.arange(count) if args.all_sources else pins;maximum=np.zeros(len(names));worst=[None]*len(names);top=[];violations=0;tested=0;unreachable=0;started=time.time()
for iteration,source in enumerate(sources):
    length,parent=distances(int(source));valid=np.isfinite(length)&(length>1e-10);unreachable+=int(np.sum(~np.isfinite(length)));tested+=int(np.sum(valid))*len(names)
    distance=np.linalg.norm(points-points[:,source,None,:],axis=2);required=np.maximum(0,distance-mob[None,:]-mob[source]);ratio=np.zeros_like(required)
    ratio[:,valid]=required[:,valid]/length[None,valid];violations+=int(np.sum(ratio>1.35));maxids=np.argmax(ratio,axis=1)
    for p,target in enumerate(maxids):
        value=float(ratio[p,target])
        if value<=maximum[p]:continue
        maximum[p]=value;path=[];j=int(target)
        while j>=0:path.append(j);j=int(parent[j])
        path.reverse();worst[p]={'poseId':str(names[p]),'sourceVertex':int(source),'sourceIsExactPin':bool(mob[source]==0),'target':int(target),'targetIsExactPin':bool(mob[target]==0),
          'necessaryStretchRatio':value,'restGeodesicMeters':float(length[target]),'posedEndpointDistanceMeters':float(distance[p,target]),'endpointMobilityMeters':[float(mob[source]),float(mob[target])],
          'restEndpointPoints':rest[[source,target]].tolist(),'posedEndpointPoints':points[p,[source,target]].tolist(),'shortestRestPath':path}
    if iteration%100==0:print(args.mesh+' '+str(iteration)+'/'+str(len(sources))+' max='+str(float(np.max(maximum)))+' seconds='+str(round(time.time()-started,2)),flush=True)
report={'status':'EXACT_PIN_TO_ALL_VERTEX_REST_GRAPH_NECESSARY_CONSTRAINT_AUDIT_NOT_PHYSICS_PASS','datasetSha256':hashlib.sha256(file.read_bytes()).hexdigest(),'mesh':args.mesh,'vertices':count,'edges':len(edges),'exactPins':len(pins),'poseCount':len(names),
 'testedOrderedPinVertexPosePairs':tested,'disconnectedOrderedPinVertexPairs':unreachable,'maximumNecessaryPathStretch':float(np.max(maximum)),
 'violatingOrderedPinVertexPosePairsAbove1_35':violations,'maxWorstPath':max(worst,key=lambda r:r['necessaryStretchRatio']),
 'method':'Dijkstra on the complete undirected triangle-edge graph with exact rest Euclidean lengths. Every exact pin is a source; every reachable vertex is a target. Required path length=max(0, actual posed LBS endpoint distance minus both endpoint maxDistance radii). Intermediate mobility is not subtracted. Original and inserted gusset vertices/edges are all included. A ratio above1.35 is an independent global infeasibility certificate; below is only a necessary condition, not a physical solver pass.',
 'sourceVertexCount':len(sources),'allVertexSources':args.all_sources,'elapsedSeconds':time.time()-started,'worstPerPose':worst}
if args.all_sources:
    report['status']='ALL_VERTEX_PAIR_REST_GRAPH_NECESSARY_CONSTRAINT_AUDIT_NOT_PHYSICS_PASS'
    report['method']=report['method'].replace('Every exact pin is a source; every reachable vertex is a target.','Every vertex is a source, including all free vertices; every reachable vertex is a target.')
    report['testedOrderedVertexPairPoseCases']=report.pop('testedOrderedPinVertexPosePairs');report['disconnectedOrderedVertexPairs']=report.pop('disconnectedOrderedPinVertexPairs');report['violatingOrderedVertexPairPoseCasesAbove1_35']=report.pop('violatingOrderedPinVertexPosePairsAbove1_35')
result=OUT/(args.mesh+('-429' if args.dense else '-25')+('-all' if args.all_sources else '')+'.json');result.write_text(json.dumps(report,indent=2),encoding='utf-8');print(json.dumps({k:v for k,v in report.items() if k not in ['worstPerPose','maxWorstPath']},indent=2),flush=True)
