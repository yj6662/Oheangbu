"""Read-only anatomical collision authoring for head/neck; no model save."""
import bpy,json,hashlib,sys,collections,shutil
from pathlib import Path
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree
import math
ROOT=Path(__file__).resolve().parents[3];ART=ROOT/'Art/PlayerV2'
SOURCE=ART/'DosaV2_Assembled.blend';OUT=ART/'Inspect/HeadNeckCollision';OUT.mkdir(parents=True,exist_ok=True)
for arg in sys.argv:
    if arg.startswith('--source='):SOURCE=Path(arg.split('=',1)[1])
bpy.ops.wm.open_mainfile(filepath=str(SOURCE));rig=bpy.data.objects['DosaV2_Rig']
result={'source':str(SOURCE),'sourceSha256':hashlib.sha256(SOURCE.read_bytes()).hexdigest(),'meshes':[],'bones':{}}
(OUT/'Inputs').mkdir(exist_ok=True);frozen=OUT/'Inputs'/(result['sourceSha256']+'.blend')
if not frozen.exists():shutil.copy2(SOURCE,frozen)
for name in ['DosaV2_Head','DosaV2_Hair','DosaV2_HatTassel_L','DosaV2_HatTassel_R']:
    o=bpy.data.objects[name];o.data.calc_loop_triangles();co=np.array([rig.matrix_world.inverted()@o.matrix_world@v.co for v in o.data.vertices]);weights=collections.Counter()
    for v in o.data.vertices:
        for g in v.groups:weights[o.vertex_groups[g.group].name]+=g.weight
    result['meshes'].append({'name':name,'vertices':len(co),'triangles':len(o.data.loop_triangles),'minimum':co.min(axis=0).tolist(),'maximum':co.max(axis=0).tolist(),'weights':dict(weights)})
    if name=='DosaV2_Head':
        groups={g.name:g.index for g in o.vertex_groups}
        head=np.array([sum(g.weight for g in v.groups if g.group==groups['Head']) for v in o.data.vertices])
        neck=np.array([sum(g.weight for g in v.groups if g.group==groups['Neck']) for v in o.data.vertices])
        result['headRegionBounds']={}
        for label,mask in [('headRigid',head>.99999),('neckMixed',(neck>0)&(head<.99999)),('other',neck==0)]:
            ps=co[mask];result['headRegionBounds'][label]={'vertices':len(ps),'min':ps.min(axis=0).tolist(),'max':ps.max(axis=0).tolist()}
for b in rig.data.bones:
    if any(q in b.name.lower() for q in ['head','neck','tassel']):result['bones'][b.name]={'head':list(b.head_local),'tail':list(b.tail_local),'matrix':[list(r) for r in b.matrix_local]}
(OUT/'inventory.json').write_text(json.dumps(result,indent=2));print(json.dumps(result,indent=2))
def fit_capsule(points):
    center=points.mean(axis=0);cov=(points-center).T@(points-center);_,axes=np.linalg.eigh(cov)
    best=None;candidates=[]
    for axis in [*np.eye(3),*axes.T]:
        axis=axis/np.linalg.norm(axis);u=np.cross(axis,np.eye(3)[np.argmin(np.abs(axis))]);u/=np.linalg.norm(u);v=np.cross(axis,u)
        projected=np.column_stack([points@u,points@v]);radial=(projected.min(axis=0)+projected.max(axis=0))*.5
        # Minimax radial-centre refinement; radius is measured afterwards, never estimated.
        for step in range(400):
            far=projected[np.argmax(np.sum((projected-radial)**2,axis=1))];radial+=(far-radial)/(step+2)
        inward=np.array([0,0,center[2]]);inward=np.array([inward@u,inward@v]);t=points@axis;lo,hi=t.min(),t.max()
        for shift in [0,.35,.7,1]:
            rc=radial*(1-shift)+inward*shift;radial_center=u*rc[0]+v*rc[1];rho=np.sum((projected-rc)**2,axis=1);candidate=None
            for a in np.linspace(lo,(lo+hi)*.5,19):
                for b in np.linspace((lo+hi)*.5,hi,19):
                    radius=np.sqrt(np.max(rho+np.maximum(np.maximum(a-t,t-b),0)**2))+.00002
                    volume=math.pi*radius*radius*(b-a)+4/3*math.pi*radius**3
                    if candidate is None or volume<candidate['volume']:
                        candidate={'start':(radial_center+axis*a).tolist(),'end':(radial_center+axis*b).tolist(),'radius':float(radius),'volume':float(volume),'inwardCenterFraction':shift}
            outside=[];a=np.array(candidate['start']);b=np.array(candidate['end'])
            for h in np.linspace(-1,1,9):
                for azimuth in np.linspace(0,2*math.pi,24,endpoint=False):
                    normal=axis*h+(u*math.cos(azimuth)+v*math.sin(azimuth))*math.sqrt(1-h*h)
                    p=Vector((b if h>=0 else a)+normal*candidate['radius']);q,n,i,d=surface.find_nearest(p)
                    if (p-q).dot(n)>0:outside.append(d)
            candidate['fitSampledNormalBasedOverhangM']=max(outside or [0])
            candidate['fitScore']=candidate['fitSampledNormalBasedOverhangM']+.4*candidate['volume']
            candidates.append(candidate)
    best=min(candidates,key=lambda c:c['fitScore'])
    return best
def distances(points,cap):
    a=np.array(cap['start']);delta=np.array(cap['end'])-a;d=points-a
    t=np.clip(d@delta/max(delta@delta,1e-20),0,1)
    return np.linalg.norm(d-t[:,None]*delta,axis=1)-cap['radius']
triangles=[];exclusions=[]
selected_head_polygons=set()
for name in ['DosaV2_Head','DosaV2_Hair']:
    o=bpy.data.objects[name];co=np.array([rig.matrix_world.inverted()@o.matrix_world@v.co for v in o.data.vertices]);o.data.calc_loop_triangles()
    headweight=np.array([sum(g.weight for g in v.groups if o.vertex_groups[g.group].name=='Head') for v in o.data.vertices])
    for index,t in enumerate(o.data.loop_triangles):
        record={'renderer':name,'triangle':index,'indices':list(t.vertices),'points':co[list(t.vertices)].tolist()}
        center=co[list(t.vertices)].mean(axis=0)
        collar=name=='DosaV2_Head' and center[2]<1.49 and (abs(center[0])>.055 or center[1]>.012)
        if all(headweight[i]>.99999 for i in t.vertices) and not collar:
            triangles.append(record)
            if name=='DosaV2_Head':selected_head_polygons.add(t.polygon_index)
        else:exclusions.append(dict(record,headWeights=headweight[list(t.vertices)].tolist(),reason='Visible collar outside the central neck: z<1.49m and(abs(x)>.055m or y>.012m).' if collar else 'Mixed Neck/Spine/Shoulder/Arm upper-garment strip in the Head renderer. Visually separated in source/semantic captures.'))
points=np.array([p for t in triangles for p in t['points']]);surface=BVHTree.FromPolygons(points.tolist(),np.arange(len(points)).reshape(-1,3).tolist(),all_triangles=True)
regions=[[],[],[],[],[],[]]
for t in triangles:
    p=np.array(t['points']);center=p.mean(axis=0)
    region=(0 if center[2]<1.462 else 1) if t['renderer']=='DosaV2_Head' and center[2]<1.492 else 5 if center[2]>=1.611 else (2 if center[0]<0 else 3) if center[1]<-.007 else 4
    regions[region].append(t)
caps=[]
for name,rows in zip(['Neck','Jaw','Face_R','Face_L','Occiput','Crown'],regions):
    points=np.unique(np.array([p for t in rows for p in t['points']]),axis=0);fit=fit_capsule(points)
    fit.update(name='__DosaSecondaryProxy_HeadNeck_'+name,anchorBone='Head',sourceTriangles=len(rows),fitVertices=len(points),
               maximumTriangleCornerOutsideM=float(distances(points,fit).max()),triangleReferences=[{'renderer':t['renderer'],'triangle':t['triangle']} for t in rows])
    caps.append(fit)
points=np.array([p for t in triangles for p in t['points']]);surface=BVHTree.FromPolygons(points.tolist(),np.arange(len(points)).reshape(-1,3).tolist(),all_triangles=True)
body_path=ROOT/'Oheangbu/Screenshots/PlayerDosaV2/anatomy-capsules-blender.json'
body_document=json.loads(body_path.read_text(encoding='utf-8-sig'))
body_caps=[{'start':c['startBlender'],'end':c['endBlender'],'radius':c['radiusMeters']} for c in body_document['capsules']]
for cap in caps:
    a=np.array(cap['start']);b=np.array(cap['end']);axis=b-a;axis/=np.linalg.norm(axis);u=np.cross(axis,np.eye(3)[np.argmin(np.abs(axis))]);u/=np.linalg.norm(u);v=np.cross(axis,u);samples=[]
    for h in np.linspace(-1,1,17):
        for azimuth in np.linspace(0,2*math.pi,48,endpoint=False):
            normal=axis*h+(u*math.cos(azimuth)+v*math.sin(azimuth))*math.sqrt(1-h*h)
            samples.append((b if h>=0 else a)+normal*cap['radius'])
    for t in np.linspace(0,1,7):
        for azimuth in np.linspace(0,2*math.pi,48,endpoint=False):samples.append(a*(1-t)+b*t+(u*math.cos(azimuth)+v*math.sin(azimuth))*cap['radius'])
    samples=np.array(samples);exposed=np.ones(len(samples),dtype=bool)
    for other in caps:
        if other is not cap:exposed &= distances(samples,other)>=-.000001
    outside=[];nearest=[];outward_points=[];body_exposed=exposed.copy()
    for body in body_caps:body_exposed &= distances(samples,body)>=-.000001
    outside_body=[]
    for si,p in enumerate(samples):
        if not exposed[si]:continue
        q,n,i,d=surface.find_nearest(Vector(p));nearest.append(d)
        if (Vector(p)-q).dot(n)>0:
            outside.append(d);outward_points.append(p.tolist())
            if body_exposed[si]:outside_body.append(d)
    cap['unionBoundarySamples']=int(exposed.sum());cap['maximumNearestSurfaceDistanceM']=max(nearest or [0]);cap['maximumOutwardNormalOverhangM']=max(outside or [0])
    cap['exposedOutsideExistingBodySamples']=int(body_exposed.sum());cap['maximumOutwardOverhangOutsideExistingRestBodyM']=max(outside_body or [0])
    cap['worstNormalBasedOverhangPointBlender']=outward_points[int(np.argmax(outside))] if outside else None
    cap['overhangMethod']='Sampled exposed capsule-union surface to original selected triangle BVH; outward inferred only from nearest triangle normal. Source has an open neck cut, so this is not an exact solid-volume proof.'
tassels=[]
for name in ['DosaV2_HatTassel_L','DosaV2_HatTassel_R']:
    o=bpy.data.objects[name];co=np.array([rig.matrix_world.inverted()@o.matrix_world@v.co for v in o.data.vertices]);o.data.calc_loop_triangles()
    samples=[]
    for t in o.data.loop_triangles:
        p=co[list(t.vertices)];n=max(1,math.ceil(max(np.linalg.norm(p[i]-p[(i+1)%3]) for i in range(3))/.002))
        for i in range(n+1):
            for j in range(n+1-i):samples.append(p[0]*(i/n)+p[1]*(j/n)+p[2]*(1-(i+j)/n))
    samples=np.array(samples);sd=np.min([distances(samples,c) for c in caps],axis=0);worst=int(np.argmin(sd))
    near=surface.find_nearest(Vector(samples[worst]))
    tassels.append({'renderer':name,'triangleSamples':len(samples),'maximumSamplingEdgeM':.002,'minimumCapsuleGapM':float(sd[worst]),'worstPointBlender':samples[worst].tolist(),'nearestActualHeadHairDistanceAtWorstM':near[3],
                    'insideSamples':int(np.sum(sd<0)),'totalMeshTriangles':len(o.data.loop_triangles)})
coverage=[]
for t in triangles:
    outside=min(float(distances(np.array(t['points']),cap).max()) for cap in caps)
    coverage.append(outside)
assert max(coverage)<=0
plan={'source':str(frozen),'sourceSha256':result['sourceSha256'],'space':'Blender rig-local rest meters; Unity representation conversion is(-x,z,-y).',
      'status':'AUTHORED_STATIC_TRIANGLE_COVERAGE_PENDING_UNITY_AND_SECONDARY_REPLAY','rigPass':False,'capsules':caps,
      'includedTriangles':len(triangles),'excludedUpperGarmentTriangles':len(exclusions),'exclusions':exclusions,
      'maximumWholeTriangleOutsideM':max(coverage),'coverageProof':'Each original selected triangle has all three corners inside one convex capsule; therefore its complete face is contained. No vertex-only union inference.',
      'rigidPoseProof':'Every selected triangle corner has Head weight1 in the source. All colliders follow Head without scale/shape changes. Coverage is invariant under any rigid Head pose. This does not cover excluded shoulder/collar garment triangles.',
      'semanticMask':{'rendererNames':['DosaV2_Head','DosaV2_Hair'],'minimumHeadWeight':.99999,'collarHeadCentroidMaximumZ':1.49,'collarHeadCentroidAbsXGreaterThan':.055,'collarHeadCentroidYGreaterThan':.012,'rule':'Include every Hair triangle and every Head triangle when all three corners have Head weight >.99999. Exclude Head collar when centroid.z<1.49 AND (abs(centroid.x)>.055 OR centroid.y>.012). No mesh triangles are removed.'},
      'existingRestBodySnapshot':{'path':str(body_path),'sha256':hashlib.sha256(body_path.read_bytes()).hexdigest(),'count':len(body_caps),'note':'Used only to report which sampled head-union boundary is already inside the existing rest body union. Does not modify or pose existing body proxies.'},
      'neckAnchorRationale':'Visible central neck surface in this source follows Head100%, not Neck. The Neck-labelled capsule is therefore also Head-parented. Mixed Neck-weight triangles belong to the surrounding shoulder/collar strip.',
      'tasselRestClearance':tassels,'sourceHeadRestMatrix':[list(r) for r in rig.data.bones['Head'].matrix_local],
      'sourceRenderers':[{'name':m['name'],'vertices':m['vertices'],'triangles':m['triangles']} for m in result['meshes'] if m['name'] in ['DosaV2_Head','DosaV2_Hair']]}
(OUT/'head-neck-capsules.json').write_text(json.dumps(plan,indent=2));print('CAPSULES',json.dumps([{k:v for k,v in c.items() if k!='triangleReferences'} for c in caps]),flush=True)
if '--fit-only' in sys.argv:sys.exit(0)
scene=bpy.context.scene
for o in scene.objects:o.hide_render=o.type=='MESH' and o.name not in ['DosaV2_Head','DosaV2_Hair','DosaV2_HatTassel_L','DosaV2_HatTassel_R']
scene.render.engine='CYCLES';scene.cycles.samples=20;scene.cycles.use_denoising=True
scene.render.resolution_x=900;scene.render.resolution_y=900;scene.render.resolution_percentage=100
scene.world.color=(.1,.1,.1);scene.view_settings.view_transform='AgX';scene.view_settings.exposure=0
for loc,power in [((0,-3,3),350),((3,1,2),220)]:
    d=bpy.data.lights.new('QAHead','AREA');d.energy=power;d.size=3;o=bpy.data.objects.new(d.name,d);scene.collection.objects.link(o);o.location=loc;o.rotation_euler=(Vector((0,0,1.55))-o.location).to_track_quat('-Z','Y').to_euler()
cam=bpy.data.objects.new('QAHeadCamera',bpy.data.cameras.new('QAHeadCamera'));scene.collection.objects.link(cam);scene.camera=cam;cam.data.type='ORTHO';cam.data.ortho_scale=.54
for kind in ['source','semantic','capsules']:
    if kind=='semantic':
        o=bpy.data.objects['DosaV2_Head'];o.data.materials.clear()
        for label,color in [('RigidHead',(1,.14,.1,1)),('NeckMix',(.05,.8,.8,1)),('UpperGarment',(.1,.15,1,1))]:
            m=bpy.data.materials.new(label);m.diffuse_color=color;m.use_nodes=True;next(n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED').inputs['Base Color'].default_value=color;o.data.materials.append(m)
        for p in o.data.polygons:p.material_index=0 if p.index in selected_head_polygons else 1
    if kind=='capsules':
        for number,cap in enumerate(caps):
            mat=bpy.data.materials.new(cap['name']);mat.diffuse_color=(*[(.95,.8,.06),(.95,.35,.05),(.04,.8,.2),(.04,.5,.95),(.7,.1,.95),(.8,.2,.6)][number],1);mat.use_nodes=True
            shader=next(n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED');shader.inputs['Base Color'].default_value=mat.diffuse_color
            a=np.array(cap['start']);b=np.array(cap['end']);axis=b-a;axis/=np.linalg.norm(axis);u=np.cross(axis,np.eye(3)[np.argmin(np.abs(axis))]);u/=np.linalg.norm(u);v=np.cross(axis,u)
            paths=[]
            for angle in np.linspace(0,math.pi,8,endpoint=False):
                radial=u*math.cos(angle)+v*math.sin(angle)
                paths.append([a+cap['radius']*(radial*math.cos(t)-axis*math.sin(t)) for t in np.linspace(0,math.pi,24)]+[b+cap['radius']*(radial*math.cos(t)+axis*math.sin(t)) for t in np.linspace(math.pi,0,24)])
            for center in [a,b]:paths.append([center+cap['radius']*(u*math.cos(t)+v*math.sin(t)) for t in np.linspace(0,2*math.pi,48)])
            curve=bpy.data.curves.new(cap['name'],'CURVE');curve.dimensions='3D';curve.bevel_depth=.00042;curve.bevel_resolution=1
            for path in paths:
                spline=curve.splines.new('POLY');spline.points.add(len(path)-1)
                for dest,p in zip(spline.points,path):dest.co=(*p,1)
                spline.use_cyclic_u=True
            obj=bpy.data.objects.new(curve.name,curve);scene.collection.objects.link(obj);curve.materials.append(mat)
    for view,offset in [('front',(0,-3,.10)),('side',(3,-.4,.08))]:
        center=Vector((0,0,1.53));cam.location=center+Vector(offset);cam.rotation_euler=(center-cam.location).to_track_quat('-Z','Y').to_euler();scene.render.filepath=str(OUT/(kind+'_'+view+'.png'));bpy.ops.render.render(write_still=True)
