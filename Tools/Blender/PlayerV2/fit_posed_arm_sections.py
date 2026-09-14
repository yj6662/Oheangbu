"""Anatomy-only axial + transverse partitions; no model or runtime edits."""
import ast,json,math,hashlib
from pathlib import Path
import numpy as np
ROOT=Path(__file__).resolve().parents[3]
DIR=ROOT/'Art/PlayerV2/Inspect/ClothBlender/PosedArmFit9f4cd412'
HELPER=Path(__file__).with_name('fit_posed_arm_capsules.py')
nodes=[n for n in ast.parse(HELPER.read_text()).body if isinstance(n,ast.FunctionDef) and n.name in
       ['fit_capsule','capsule_distances','polygon_area','point_surface_distance_and_inside','capsule_samples']]
exec(compile(ast.Module(body=nodes,type_ignores=[]),str(HELPER),'exec'))
topology=json.loads((DIR/'topology-rest.json').read_text())
axial=json.loads((DIR/'posed-arm-candidate-report.json').read_text())
actual=json.loads((DIR/'actual-arm-pin-clearance-allposes.json').read_text())
actual_map={(r['poseId'],r['lining']):{p['sourceVertex'] for p in r['records']} for r in actual['rows']}
poses=[]
for p in DIR.glob('*.json'):
    obj=json.loads(p.read_text())
    if 'poseId' in obj:poses.append(obj)

def cut(poly,axis,value,lower):
    result=[]
    for i,current in enumerate(poly):
        prior=poly[i-1];old=prior[axis]>=value if lower else prior[axis]<=value;new=current[axis]>=value if lower else current[axis]<=value
        if new!=old:result.append(prior+(value-prior[axis])/(current[axis]-prior[axis])*(current-prior))
        if new:result.append(current)
    return result

report={'sourceSha256':topology['sourceSha256'],'scriptSha256':hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
        'status':'OFFLINE_AXIAL_TRANSVERSE_CANDIDATES_NO_NATIVE_APPROVAL','pinUsedInFitting':False,'candidates':[]}
for divisions in [3,4]:
    breaks=next(c['partitionPlanesMeters'] for c in axial['candidates'] if c['capsulesPerArm']==divisions)
    for transverse in ['restY','restZResidual']:
        candidate={'capsulesPerArm':divisions*2,'axialPlanesMeters':breaks,'transverse':transverse,'poses':[]}
        for pose in poses:
            for side in ['Left','Right']:
                name='DosaV2_ArmLining_'+side;cloth='DosaV2_SleeveOuter_'+side[0]
                data=topology['topology'][name];rest=np.array(data['restVertices']);vertices=np.array(pose['vertices'][name]);triangles=np.array(data['triangles'])
                x=np.abs(rest[:,0]);design=np.column_stack((x,np.ones(len(x))));slope,intercept=np.linalg.lstsq(design,rest[:,2],rcond=None)[0]
                y=rest[:,1] if transverse=='restY' else rest[:,2]-slope*x-intercept
                tagged=np.column_stack((vertices,x,y));caps=[];area=0
                for i,(lo,hi) in enumerate(zip(breaks,breaks[1:])):
                    for half in [False,True]:
                        polygons=[]
                        for tri in triangles:
                            poly=cut(list(tagged[tri]),3,lo,True)
                            if poly:poly=cut(poly,3,hi,False)
                            if poly:poly=cut(poly,4,0,half)
                            if len(poly)>=3:polygons.append(np.array(poly)[:,:3])
                        points=np.unique(np.round(np.concatenate(polygons),10),axis=0)
                        orientation=vertices[np.argmax(x)]-vertices[np.argmin(x)]
                        cap=fit_capsule(points,points@orientation);cap['name']=name+f'_{i}_{int(half)}';cap['axialInterval']=[lo,hi];cap['transversePositive']=half
                        caps.append(cap);area+=sum(polygon_area(poly) for poly in polygons)
                pin_ids=np.flatnonzero(np.array(topology['topology'][cloth]['mobilityMeters'])==0);points=np.array(pose['vertices'][cloth])[pin_ids]
                distances=np.min(np.array([capsule_distances(points,c) for c in caps]),axis=0)
                inside_ids=np.flatnonzero(distances<-.0005);actual_inside=actual_map[(pose['poseId'],name)]
                records=[{'sourceVertex':int(pin_ids[i]),'depthMeters':float(-distances[i]),'actualLiningInside':int(pin_ids[i]) in actual_inside} for i in inside_ids]
                row={'poseId':pose['poseId'],'lining':name,'capsules':caps,'triangleAreaConservationError':abs(area-sum(polygon_area(vertices[t]) for t in triangles)),
                     'maximumClippedCornerOutsideMeters':max(c['maximumClippedCornerOutsideMeters'] for c in caps),
                     'pinsInsideOverHalfMm':len(records),'proxyOnlyPinsInsideOverHalfMm':sum(not r['actualLiningInside'] for r in records),
                     'maxPinDepthMeters':max([0.]+[r['depthMeters'] for r in records]),
                     'maxProxyOnlyPinDepthMeters':max([0.]+[r['depthMeters'] for r in records if not r['actualLiningInside']]),'records':records}
                candidate['poses'].append(row)
            print('FIT '+str(divisions*2)+' '+transverse+' '+pose['poseId'],flush=True)
        candidate['aggregate']={'pinsInsideOverHalfMm':sum(r['pinsInsideOverHalfMm'] for r in candidate['poses']),
             'proxyOnlyPinsInsideOverHalfMm':sum(r['proxyOnlyPinsInsideOverHalfMm'] for r in candidate['poses']),
             'maxProxyOnlyPinDepthMeters':max(r['maxProxyOnlyPinDepthMeters'] for r in candidate['poses']),
             'triangleCornerOutsideMeters':max(r['maximumClippedCornerOutsideMeters'] for r in candidate['poses']),
             'maximumAreaConservationError':max(r['triangleAreaConservationError'] for r in candidate['poses'])}
        report['candidates'].append(candidate)
        (DIR/'posed-arm-transverse-candidate-report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
        print(json.dumps({'count':divisions*2,'transverse':transverse,'aggregate':candidate['aggregate']}),flush=True)
