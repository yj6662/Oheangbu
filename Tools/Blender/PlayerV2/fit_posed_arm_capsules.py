"""Offline anatomy-only capsule fits. Pins are NEVER fitting inputs.

Each source triangle is clipped using its interpolated rest abs(X) parameter.
A capsule is convex; containing every clipped polygon corner proves containing
the entire original triangle partition, not only a sparse coverage sample.
This is a candidate report, not runtime code or a cloth/RIG_PASS result.
"""
import json, hashlib, itertools, math, time
from pathlib import Path
import numpy as np

ROOT = Path(__file__).resolve().parents[3]
DIR = ROOT / 'Art/PlayerV2/Inspect/ClothBlender/PosedArmFit9f4cd412'
TOPOLOGY = json.loads((DIR / 'topology-rest.json').read_text())
CAPS_PATH = ROOT / 'Art/PlayerV2/Inspect/ClothBlender/Inputs/anatomy-capsules-final-9f4cd412.json'
OLD_CAPS = json.loads(CAPS_PATH.read_text(encoding='utf-8-sig'))['capsules']
POSES = [json.loads(p.read_text()) for p in sorted(DIR.glob('*.json'))
         if p.name not in ('topology-rest.json', 'posed-arm-candidate-report.json') and not p.name.startswith('capsules-')]
POSES = [p for p in POSES if 'poseId' in p]
NAMES = ['DosaV2_ArmLining_Left', 'DosaV2_ArmLining_Right']
# Inter-ring support planes from the real authored nine-ring anatomy. Endpoints
# extend past the mesh; those outer planes never create unmeasured geometry.
BREAKS = np.array([.12, .22, .32, .361, .402, .442, .53, .61, .68])

def clip(poly, value, lower):
    result = []
    for index, current in enumerate(poly):
        previous = poly[index - 1]
        old_in = previous[3] >= value if lower else previous[3] <= value
        new_in = current[3] >= value if lower else current[3] <= value
        if new_in != old_in:
            t = (value - previous[3]) / (current[3] - previous[3])
            result.append(previous + t * (current - previous))
        if new_in:
            result.append(current)
    return result

def clipped_polygons(vertices, parameters, triangles, lo, hi):
    tagged = np.column_stack((vertices, parameters))
    polygons = []
    for tri in triangles:
        poly = clip(list(tagged[tri]), lo, True)
        if poly:
            poly = clip(poly, hi, False)
        if len(poly) >= 3:
            polygons.append(np.array(poly)[:, :3])
    return polygons

def capsule_distances(points, cap):
    a, b = np.array(cap['startBlender']), np.array(cap['endBlender'])
    axis = b - a
    t = np.clip((points - a) @ axis / max(np.dot(axis, axis), 1e-30), 0, 1)
    return np.linalg.norm(points - a - t[:, None] * axis, axis=1) - cap['radiusMeters']

def fit_capsule(points, parameters):
    center = np.mean(points, axis=0)
    _, _, directions = np.linalg.svd(points - center, full_matrices=False)
    axis = directions[0]
    if np.sum(((points-center)@axis) * (parameters-np.mean(parameters))) < 0:
        axis = -axis
    projection = (points - center) @ axis
    radial = points - center - projection[:, None] * axis
    # Anatomy-only enclosing-circle-center adjustment in the perpendicular plane.
    # Deterministic shrinking subgradient, then score unchanged and adjusted axes.
    candidate_centers = [center.copy()]
    shift = np.zeros(3)
    for step in range(160):
        residual = radial - shift
        farthest = residual[np.argmax(np.sum(residual * residual, axis=1))]
        shift += farthest / (step + 2)
    candidate_centers.append(center + shift)
    best = None
    for center in candidate_centers:
        projection = (points - center) @ axis
        radial_sq = np.sum((points - center) ** 2, axis=1) - projection ** 2
        lower, upper = np.min(projection), np.max(projection)
        base_radius = math.sqrt(max(0, float(np.max(radial_sq))))
        inset = np.linspace(0, min((upper-lower)/2, base_radius*1.5), 19)
        left, right = np.meshgrid(lower+inset, upper-inset, indexing='ij')
        left, right = left.ravel(), right.ravel()
        overshoot = np.maximum(np.maximum(left[:, None]-projection, projection-right[:, None]), 0)
        radius = np.sqrt(np.maximum(np.max(radial_sq + overshoot**2, axis=1), 0)) + 1e-6
        volume = np.pi * radius**2 * np.maximum(right-left, 0) + (4/3)*np.pi*radius**3
        index = int(np.argmin(volume))
        cap = {'startBlender': (center + left[index]*axis).tolist(),
               'endBlender': (center + right[index]*axis).tolist(),
               'radiusMeters': float(radius[index]), 'volumeCubicMeters': float(volume[index])}
        if best is None or cap['volumeCubicMeters'] < best['volumeCubicMeters']:
            best = cap
    best['maximumClippedCornerOutsideMeters'] = max(0., float(np.max(capsule_distances(points, best))))
    return best

def polygon_area(poly):
    return sum(np.linalg.norm(np.cross(poly[i]-poly[0], poly[i+1]-poly[0]))/2 for i in range(1, len(poly)-1))

def point_surface_distance_and_inside(points, vertices, triangles):
    tri = vertices[triangles]
    a, b, c = tri[:,0], tri[:,1], tri[:,2]
    u, v = b-a, c-a
    normal = np.cross(u,v)
    norm_sq = np.maximum(np.sum(normal*normal,axis=1),1e-30)
    dot00, dot01, dot11 = np.sum(u*u,axis=1), np.sum(u*v,axis=1), np.sum(v*v,axis=1)
    denominator = np.maximum(dot00*dot11-dot01**2,1e-30)
    all_distances, all_inside = [], []
    for chunk in np.array_split(points, max(1, math.ceil(len(points)/128))):
        rel = chunk[:,None,:]-a
        signed_num = np.einsum('nmc,mc->nm',rel,normal)
        projection = rel - signed_num[:,:,None]*normal[None,:,:]/norm_sq[None,:,None]
        dot20 = np.einsum('nmc,mc->nm',projection,u)
        dot21 = np.einsum('nmc,mc->nm',projection,v)
        bary_u=(dot11*dot20-dot01*dot21)/denominator
        bary_v=(dot00*dot21-dot01*dot20)/denominator
        valid=(bary_u>=0)&(bary_v>=0)&(bary_u+bary_v<=1)
        distances=np.where(valid,signed_num**2/norm_sq,np.inf)
        for start,end in [(a,b),(b,c),(c,a)]:
            edge=end-start;length_sq=np.maximum(np.sum(edge*edge,axis=1),1e-30)
            relative=chunk[:,None,:]-start
            t=np.clip(np.einsum('nmc,mc->nm',relative,edge)/length_sq,0,1)
            distances=np.minimum(distances,np.sum((relative-t[:,:,None]*edge)**2,axis=2))
        aa=a[None,:,:]-chunk[:,None,:];bb=b[None,:,:]-chunk[:,None,:];cc=c[None,:,:]-chunk[:,None,:]
        la,lb,lc=[np.linalg.norm(q,axis=2) for q in (aa,bb,cc)]
        numer=np.einsum('nmc,nmc->nm',aa,np.cross(bb,cc))
        denom=la*lb*lc+np.sum(aa*bb,axis=2)*lc+np.sum(bb*cc,axis=2)*la+np.sum(cc*aa,axis=2)*lb
        winding=np.sum(2*np.arctan2(numer,denom),axis=1)/(4*np.pi)
        all_distances.extend(np.sqrt(np.min(distances,axis=1)))
        all_inside.extend(np.abs(winding)>.5)
    return np.array(all_distances),np.array(all_inside)

def capsule_samples(cap):
    a,b=np.array(cap['startBlender']),np.array(cap['endBlender']);axis=b-a
    axis/=max(np.linalg.norm(axis),1e-30)
    u=np.cross(axis,[0,0,1] if abs(axis[2])<.8 else [0,1,0]);u/=np.linalg.norm(u);v=np.cross(axis,u)
    points=[];radius=cap['radiusMeters']
    for phi in np.linspace(0,2*np.pi,24,endpoint=False):
        radial=u*np.cos(phi)+v*np.sin(phi)
        for t in np.linspace(0,1,5):points.append(a+(b-a)*t+radius*radial)
        for angle in np.linspace(0,np.pi/2,5)[1:]:
            points.extend([a+radius*(radial*np.cos(angle)-axis*np.sin(angle)),b+radius*(radial*np.cos(angle)+axis*np.sin(angle))])
    return np.array(points)

cache={};pose_data={}
for pose in POSES:
    for name in NAMES:
        vertices=np.array(pose['vertices'][name]);topo=TOPOLOGY['topology'][name]
        triangles=np.array(topo['triangles']);parameters=np.abs(np.array(topo['restVertices'])[:,0])
        pose_data[(pose['poseId'],name)]=(vertices,triangles,parameters)
        for i,j in itertools.combinations(range(len(BREAKS)),2):
            polygons=clipped_polygons(vertices,parameters,triangles,BREAKS[i],BREAKS[j])
            # Retain a rest-parameter label for deterministic axis orientation.
            points=np.unique(np.round(np.concatenate(polygons),10),axis=0)
            orientation=(vertices[np.argmax(parameters)]-vertices[np.argmin(parameters)])
            labels=points@orientation
            cap=fit_capsule(points,labels)
            cache[(pose['poseId'],name,i,j)]=(cap,polygons)
    print('FIT_INTERVALS '+pose['poseId'],flush=True)

report={'status':'OFFLINE_ANATOMY_ONLY_CANDIDATES_NOT_RUNTIME_APPROVAL','sourceSha256':TOPOLOGY['sourceSha256'],
        'proxyBaselineSha256':hashlib.sha256(CAPS_PATH.read_bytes()).hexdigest(),
        'scriptSha256':hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
        'fitInputs':'Only actual posed ArmLining triangle partitions. Cloth pins, mobility and capsule clearance are excluded from every optimization objective.',
        'coverageProof':'Clip every triangle by linearly interpolated rest abs(X). Each resulting planar polygon is inside a convex capsule because all its corners are inside. Verify partition area conservation.',
        'limitations':['Dynamic anatomy fit candidate, no runtime integration or native Cloth simulation.',
                       'Capsule outward error is sampled, not a formal bound; final physical cloth validation still required.',
                       'Fitting requires stable continuous runtime axes/endpoints; this offline PCA implementation is not production ordering.'],
        'sourceBreakPlanesMeters':BREAKS.tolist(),'candidates':[]}

for count in [3,4,6,8]:
    partitions=[]
    for middle in itertools.combinations(range(1,len(BREAKS)-1),count-1):
        cuts=(0,)+middle+(len(BREAKS)-1,)
        # One stable source partition for every side/pose. Minimize mean volume.
        objective=sum(cache[(p['poseId'],name,i,j)][0]['volumeCubicMeters']
                      for p in POSES for name in NAMES for i,j in zip(cuts,cuts[1:]))
        partitions.append((objective,cuts))
    _,cuts=min(partitions)
    candidate={'capsulesPerArm':count,'partitionPlanesMeters':BREAKS[list(cuts)].tolist(),'poses':[]}
    for pose in POSES:
        for name in NAMES:
            vertices,triangles,parameters=pose_data[(pose['poseId'],name)]
            capsules=[];area=0
            for number,(i,j) in enumerate(zip(cuts,cuts[1:])):
                cap,polygons=cache[(pose['poseId'],name,i,j)];cap=dict(cap)
                cap.update({'name':f'{name}_Posed_{number}','partRestX':[float(BREAKS[i]),float(BREAKS[j])]})
                capsules.append(cap);area+=sum(polygon_area(poly) for poly in polygons)
            original_area=sum(polygon_area(vertices[t]) for t in triangles)
            side=name.split('_')[-1][0];cloth='DosaV2_SleeveOuter_'+side
            cloth_points=np.array(pose['vertices'][cloth]);mobility=np.array(TOPOLOGY['topology'][cloth]['mobilityMeters'])
            pin_ids=np.flatnonzero(mobility==0);pin_points=cloth_points[pin_ids]
            distances=np.array([capsule_distances(pin_points,c) for c in capsules]);minimum=np.min(distances,axis=0)
            pin_rows=[{'sourceVertex':int(pin_ids[n]),'pointBlender':pin_points[n].tolist(),
                       'depthMeters':float(-minimum[n]),'capsule':capsules[int(np.argmin(distances[:,n]))]['name']}
                      for n in np.flatnonzero(minimum<-.0005)]
            surface=np.concatenate([capsule_samples(c) for c in capsules]);outward,inside=point_surface_distance_and_inside(surface,vertices,triangles)
            outward[inside]=0
            prior=[]
            for old in OLD_CAPS:
                if ('ArmLining_'+name.split('_')[-1]+'_') not in old['name']:continue
                bone=old['anchorBone'];matrix=np.array(pose['bonePoseMatrices'][bone])@np.linalg.inv(np.array(TOPOLOGY['boneRestMatrices'][bone]))
                c=dict(old)
                c['startBlender']=(matrix@np.r_[old['startBlender'],1])[:3].tolist()
                c['endBlender']=(matrix@np.r_[old['endBlender'],1])[:3].tolist();prior.append(c)
            baseline=np.min(np.array([capsule_distances(pin_points,c) for c in prior]),axis=0)
            # Dense barycentric sample supplements the clipped-corner convex proof.
            bary=np.array([(i/8,j/8,1-(i+j)/8) for i in range(9) for j in range(9-i)])
            samples=np.einsum('ntc,kt->nkc',vertices[triangles],bary).reshape(-1,3)
            miss=np.min(np.array([capsule_distances(samples,c) for c in capsules]),axis=0)
            row={'poseId':pose['poseId'],'lining':name,'capsules':capsules,
                 'triangleAreaConservationErrorSquareMeters':abs(area-original_area),
                 'maximumClippedCornerOutsideMeters':max(c['maximumClippedCornerOutsideMeters'] for c in capsules),
                 'barycentricSamples':len(samples),'barycentricMissesOver1um':int(np.sum(miss>1e-6)),
                 'maxBarycentricOutsideMeters':max(0.,float(np.max(miss))),
                 'pins':len(pin_ids),'pinsInsideOverHalfMm':len(pin_rows),'maximumPinDepthMeters':max(0.,float(-np.min(minimum))),
                 'pinnedRecords':pin_rows,'baselineRigidTwoCapsPinsInsideOverHalfMm':int(np.sum(baseline<-.0005)),
                 'baselineRigidTwoCapsMaxPinDepthMeters':max(0.,float(-np.min(baseline))),
                 'capsuleSurfaceSamples':len(surface),'sampledMaximumOutwardMeters':float(np.max(outward)),
                 'sampled95PercentileOutwardMeters':float(np.quantile(outward,.95))}
            candidate['poses'].append(row)
        print('MEASURE '+str(count)+' '+pose['poseId'],flush=True)
    candidate['aggregate']={
        'bodyTriangleCornerMisses':sum(r['maximumClippedCornerOutsideMeters']>1e-6 for r in candidate['poses']),
        'barycentricMissesOver1um':sum(r['barycentricMissesOver1um'] for r in candidate['poses']),
        'pinsInsideOverHalfMm':sum(r['pinsInsideOverHalfMm'] for r in candidate['poses']),
        'maxPinDepthMeters':max(r['maximumPinDepthMeters'] for r in candidate['poses']),
        'maxOutwardMeters':max(r['sampledMaximumOutwardMeters'] for r in candidate['poses']),
        'maximumAreaConservationErrorSquareMeters':max(r['triangleAreaConservationErrorSquareMeters'] for r in candidate['poses'])}
    report['candidates'].append(candidate)
    (DIR/'posed-arm-candidate-report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    print(json.dumps({'count':count,'partition':candidate['partitionPlanesMeters'],'aggregate':candidate['aggregate']}),flush=True)
