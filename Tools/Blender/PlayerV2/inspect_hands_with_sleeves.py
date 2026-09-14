"""Static calibrated hand inspection with the actual sleeves and arm visible; no source saves."""
import bpy,bmesh,json,math,hashlib,sys
import numpy as np
from pathlib import Path
from mathutils import Vector,Matrix,Quaternion
from mathutils.bvhtree import BVHTree
ROOT=Path(__file__).resolve().parents[3];ART=ROOT/'Art/PlayerV2';OUT=ART/'Inspect/HandsWithSleeves';OUT.mkdir(parents=True,exist_ok=True)
SOURCE=ART/'DosaV2_Refined.blend';BRUSH=ART/'DosaBrushV2.blend'
REFINE='--refine' in sys.argv;PREFIX='refined-' if REFINE else ''
STRESS='--stress' in sys.argv
POSE_OVERRIDE=None;SIDE_OVERRIDE=None;CLOSE='--close' in sys.argv
for arg in sys.argv:
    if arg.startswith('--source='):SOURCE=Path(arg.split('=',1)[1])
    if arg.startswith('--prefix='):PREFIX=arg.split('=',1)[1]
    if arg.startswith('--poses='):POSE_OVERRIDE=arg.split('=',1)[1].split(',')
    if arg.startswith('--side='):SIDE_OVERRIDE=arg.split('=',1)[1]
UNLINED='--no-arm-lining' in sys.argv
if UNLINED:PREFIX='unlined-'+PREFIX
PROBE='--probe-only' in sys.argv;PROBES=[]

def digest(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def look(obj,target):obj.rotation_euler=(Vector(target)-obj.location).to_track_quat('-Z','Y').to_euler()
source_hash=digest(SOURCE);brush_hash=digest(BRUSH)
bpy.ops.wm.open_mainfile(filepath=str(SOURCE));scene=bpy.context.scene;rig=bpy.data.objects['DosaV2_Rig'];hand=bpy.data.objects['DosaV2_Hands']
calibration=json.loads((ART/'Calibration/hand-brush-contact-report.json').read_text())['sides']
if 'DosaBrushV2_Rig' not in bpy.data.objects:
    with bpy.data.libraries.load(str(BRUSH),link=False) as (src,dst):
        dst.objects=[n for n in src.objects if n.startswith('DosaBrushV2_') or n in ['GripSocket','TipSocket']]
    for obj in dst.objects:
        if obj and not obj.users_collection:scene.collection.objects.link(obj)
brush=bpy.data.objects['DosaBrushV2_Rig'];grip=bpy.data.objects['GripSocket'];handle=bpy.data.objects['DosaBrushV2_Handle']
for obj in scene.objects:
    if obj.type=='MESH':obj.hide_render=obj.name=='DosaV2_SourceSurface' or (UNLINED and obj.name.startswith('DosaV2_ArmLining'));obj.hide_set(obj.hide_render)
bpy.context.view_layer.update();brush_to_grip=brush.matrix_world.inverted()@grip.matrix_world
handle_to_grip=grip.matrix_world.inverted()@handle.matrix_world;handle.data.calc_loop_triangles()
shaft=BVHTree.FromPolygons([handle_to_grip@v.co for v in handle.data.vertices],[t.vertices for t in handle.data.loop_triangles],all_triangles=True)

def pose(side,preset='ready'):
    for p in rig.pose.bones:p.matrix_basis=Matrix.Identity(4)
    if hand.data.shape_keys:
        for name in ['GripPalmRelax_Right','GripPalmRelax_Left']:
            key=hand.data.shape_keys.key_blocks.get(name)
            if key:key.value=1 if name.endswith(side) and preset!='open-hand' else 0
    bpy.context.view_layer.update()
    def bone(name,direction):
        p=rig.pose.bones[name];m=p.matrix.copy();q=m.to_quaternion()
        new=((q@Vector((0,1,0))).rotation_difference(Vector(direction).normalized())@q).to_matrix().to_4x4()
        new.translation=m.translation;p.matrix=new;bpy.context.view_layer.update()
    for name,sign in [('Left',1),('Right',-1)]:bone(name+'Arm',(sign*.16,-.02,-.987))
    sign=1 if side=='Left' else -1
    bone(side+'ForeArm',(sign*.14,-.84,.525) if preset!='elbow-deep' else (sign*.05,-.42,.90))
    if preset in ['pronation-flex','supination-extend']:
        amount=1 if preset=='pronation-flex' else -1
        p=rig.pose.bones[side+'ForeArm'];m=p.matrix.copy();q=m.to_quaternion()
        new=(Quaternion(q@Vector((0,1,0)),math.radians(90*amount))@q).to_matrix().to_4x4()
        new.translation=m.translation;p.matrix=new;bpy.context.view_layer.update()
        p=rig.pose.bones[side+'Hand'];p.rotation_mode='XYZ';p.rotation_euler.x=math.radians(30*amount)
        bpy.context.view_layer.update()
    for finger,angles in calibration[side]['finger_euler_xyz_degrees'].items():
        for j,angle in enumerate(angles):
            p=rig.pose.bones[side+'Hand'+finger+str(j+1)];p.rotation_mode='XYZ';p.rotation_euler=[math.radians(x)*(0 if preset=='open-hand' else .9 if preset=='pen-up' and finger in ['Ring','Pinky'] else 1) for x in angle]
    for name in ['DosaBrushV2_Handle','DosaBrushV2_Bristles']:
        bpy.data.objects[name].hide_render=preset=='open-hand';bpy.data.objects[name].hide_set(preset=='open-hand')
    bpy.context.view_layer.update();world=rig.matrix_world@rig.pose.bones[side+'BrushGrip'].matrix
    brush.matrix_world=world@brush_to_grip.inverted();bpy.context.view_layer.update()
    return world

scene.render.engine='CYCLES';scene.cycles.samples=24;scene.cycles.use_denoising=True
scene.render.resolution_x=1200;scene.render.resolution_y=1000;scene.render.resolution_percentage=100
scene.world.color=(.14,.14,.14);scene.view_settings.view_transform='AgX'
for obj in list(scene.objects):
    if obj.type=='LIGHT':bpy.data.objects.remove(obj,do_unlink=True)
lights=[]
for name,offset,energy,size in [('HandKey',(.8,-.8,1),50,.8),('HandFill',(-.7,-.2,.3),20,.6)]:
    obj=bpy.data.objects.new(name,bpy.data.lights.new(name,'AREA'));scene.collection.objects.link(obj)
    obj.data.energy=energy;obj.data.size=size;lights.append((obj,Vector(offset)))
camera=bpy.data.objects.new('HandsWithSleevesCamera',bpy.data.cameras.new('HandsWithSleevesCamera'));scene.collection.objects.link(camera)
camera.data.type='ORTHO';camera.data.ortho_scale=.34 if CLOSE else .49;scene.camera=camera
original_mat=hand.data.materials[0];no_normal=original_mat.copy();no_normal.name='HandDiagnostic_NoNormal'
for link in list(no_normal.node_tree.nodes.get('Principled BSDF').inputs['Normal'].links):no_normal.node_tree.links.remove(link)
clay=bpy.data.materials.new('HandDiagnostic_Clay');clay.use_nodes=True
clay.node_tree.nodes.get('Principled BSDF').inputs['Base Color'].default_value=(.24,.27,.29,1)
clay.node_tree.nodes.get('Principled BSDF').inputs['Roughness'].default_value=.75

def render(side,view,variant,world,preset='ready'):
    forearm=rig.matrix_world@rig.pose.bones[side+'ForeArm'].matrix
    center=world.translation*(.8 if CLOSE else .65)+forearm.translation*(.2 if CLOSE else .35)
    basis=world.to_3x3();sign=1 if side=='Left' else -1
    # Include the complete wrist/cuff junction and elbow-side forearm, with the full body visible.
    offset=Vector((.34*sign,-.15,.40 if view=='dorsal' else -.40))
    camera.location=center+(basis@offset if view=='dorsal' else Vector((sign*.60,-.75,-.28)));look(camera,center)
    for light,offset in lights:light.location=center+offset;look(light,center)
    if PROBE:
        if side=='Right' and view=='palm':
            frame=camera.data.view_frame(scene=scene);xmin=min(p.x for p in frame);xmax=max(p.x for p in frame);ymin=min(p.y for p in frame);ymax=max(p.y for p in frame)
            bpy.context.view_layer.update();dg=bpy.context.evaluated_depsgraph_get()
            for px,py in [(563,474),(581,456),(600,420),(590,350),(670,330),(535,480),(542,550),(512,600)]:
                origin=camera.matrix_world@Vector((xmin+(xmax-xmin)*px/1200,ymin+(ymax-ymin)*(1-py/1000),0))
                direction=camera.matrix_world.to_3x3()@Vector((0,0,-1));hit=scene.ray_cast(dg,origin,direction)
                entry={'pixel':[px,py],'object':hit[4].name if hit[0] else None}
                if hit[0] and hit[3]>=0:
                    obj=hit[4].original;p=obj.data.polygons[hit[3]]
                    entry['rest_face_center']=list(p.center);entry['vertices']=list(p.vertices)
                    entry['weights']=[{obj.vertex_groups[g.group].name:g.weight for g in obj.data.vertices[i].groups} for i in p.vertices]
                PROBES.append(entry)
    hand.data.materials[0]={'material':original_mat,'no-normal':no_normal,'clay':clay}[variant]
    label=PREFIX+side+'-'+(preset+'-' if STRESS else '')+view+'-'+variant
    if not PROBE:
        scene.render.filepath=str(OUT/(label+'.png'));bpy.ops.render.render(write_still=True)
    hand.data.materials[0]=original_mat
    frame=camera.data.view_frame(scene=scene);xmin=min(p.x for p in frame);xmax=max(p.x for p in frame);ymin=min(p.y for p in frame);ymax=max(p.y for p in frame)
    dg=bpy.context.evaluated_depsgraph_get();hits={};lining_hits=[];direction=camera.matrix_world.to_3x3()@Vector((0,0,-1))
    for y in range(48):
        for x in range(58):
            origin=camera.matrix_world@Vector((xmin+(xmax-xmin)*(x+.5)/58,ymin+(ymax-ymin)*(y+.5)/48,0))
            hit=scene.ray_cast(dg,origin,direction)
            if hit[0]:
                hits[hit[4].name]=hits.get(hit[4].name,0)+1
                if hit[4].name.startswith('DosaV2_ArmLining'):
                    lining_hits.append({'object':hit[4].name,'rest_center':list(hit[4].original.data.polygons[hit[3]].center),'pixel':[x/58,y/48]})
    return {'image':label+'.png','sampled_visible_objects':hits,'visible_arm_lining_samples':sum(n for k,n in hits.items() if k.startswith('DosaV2_ArmLining')),'lining_hits':lining_hits}

def diagnostics(side,world):
    dg=bpy.context.evaluated_depsgraph_get();evaluated=hand.evaluated_get(dg);mesh=evaluated.to_mesh();mesh.calc_loop_triangles()
    sign=1 if side=='Left' else -1;indices={v.index for v in hand.data.vertices if v.co.x*sign>.60}
    local=world.inverted()@evaluated.matrix_world;points={i:local@mesh.vertices[i].co for i in indices}
    distances={}
    for i,p in points.items():
        nearest,normal,tri,distance=shaft.find_nearest(p);radial=Vector((p.x,0,p.z));radius=radial.length
        hit=shaft.ray_cast(Vector((0,p.y,0)),radial.normalized(),.15) if radius>1e-9 else (None,None,None,None)
        inside=hit[0] is not None and radius<math.hypot(hit[0].x,hit[0].z)-1e-7
        distances[i]=-distance if inside else distance
    contacts={}
    for finger in ['Thumb','Index','Middle','Ring','Pinky']:
        original_id=calibration[side]['fingers'][finger]['contact_vertex_index']
        contacts[finger]={'calibrated_contact_vertex':original_id,'signed_surface_gap_m':distances[original_id]}
    edges=[];bm=bmesh.new();bm.from_mesh(mesh);bm.verts.ensure_lookup_table();bm.edges.ensure_lookup_table()
    for edge in bm.edges:
        if not all(v.index in indices for v in edge.verts) or len(edge.link_faces)!=2:continue
        angle=edge.calc_face_angle(0);length=edge.calc_length()
        if angle>math.radians(22) and length>.002:
            rest=(hand.data.vertices[edge.verts[0].index].co+hand.data.vertices[edge.verts[1].index].co)*.5
            edges.append({'vertices':[v.index for v in edge.verts],'rest_midpoint_xyz':list(rest),'face_angle_degrees':math.degrees(angle),'edge_length_m':length})
    bm.free();evaluated.to_mesh_clear()
    return {'source_contact_vertices':contacts,'whole_hand_vertex_penetration_m':max(0,-min(distances.values())),
            'sharp_geometric_edges_over_22_degrees':len(edges),'sharpest_edges':sorted(edges,key=lambda e:-e['face_angle_degrees'])[:25],
            'all_hand_faces_smooth':all(p.use_smooth for p in hand.data.polygons),'custom_normals':hand.data.has_custom_normals}

def smoothstep(a,b,x):
    t=np.clip((x-a)/(b-a),0,1);return t*t*(3-2*t)

def refine_roots():
    """Only blend the discontinuous palm/root weights. Geometry, UV and contacts stay frozen."""
    report={};bones=list(rig.data.bones);names=[b.name for b in bones];bn={name:i for i,name in enumerate(names)}
    rest=np.asarray([v.co for v in hand.data.vertices]);hom=np.column_stack((rest,np.ones(len(rest))))
    original=np.zeros((len(rest),len(bones)))
    for v in hand.data.vertices:
        for group in v.groups:original[v.index,bn[hand.vertex_groups[group.group].name]]=group.weight
    hand.data.calc_loop_triangles();triangles=np.asarray([t.vertices for t in hand.data.loop_triangles],dtype=int)
    edges={}
    for i,face in enumerate(triangles):
        for a,b in zip(face,np.roll(face,-1)):edges.setdefault(tuple(sorted((int(a),int(b)))),[]).append(i)
    adjacency=[set() for _ in rest]
    for a,b in edges:adjacency[a].add(b);adjacency[b].add(a)
    result=original.copy()
    for side,sign in [('Right',-1),('Left',1)]:
        world=pose(side);skin=np.asarray([rig.pose.bones[n].matrix@rig.data.bones[n].matrix_local.inverted() for n in names])
        transformed=np.einsum('bij,vj->vbi',skin,hom)[:,:,:3]
        wrist=np.asarray(rig.data.bones[side+'Hand'].head_local)
        fwd=(rest[:,0]-wrist[0])*sign;width=-(rest[:,1]-wrist[1]);side_ids=rest[:,0]*sign>.60
        baseline=np.einsum('vb,vbi->vi',original,transformed)
        local=np.column_stack((baseline,np.ones(len(rest))))@np.asarray(world.inverted()).T
        protected=np.zeros(len(rest),dtype=bool)
        for i in np.where(side_ids)[0]:
            if abs(local[i,1])>.095:continue
            p=Vector(local[i,:3]);near=shaft.find_nearest(p)
            if near[3] is not None and near[3]<.004:protected[i]=True
        # Lock entire contact faces and two neighboring rings, not merely the nearest measured vertex.
        for _ in range(2):
            protected=np.asarray([protected[i] or any(protected[j] for j in adjacency[i]) for i in range(len(rest))])
        region=side_ids&(fwd>-.008)&(fwd<.088)&(width<.060)&(~protected)
        amount=(1-smoothstep(.069,.088,fwd))*(1-smoothstep(.041,.060,width))*smoothstep(-.008,.012,fwd)
        amount*=region
        desired=original.copy()
        ff=smoothstep(.043,.083,fwd)
        tt=smoothstep(.007,.052,width)*(1-smoothstep(.049,.082,fwd))
        finger_offsets=[.023,.005,-.013,-.029]
        for i in np.where(region)[0]:
            row=np.zeros(len(bones));row[bn[side+'Hand']]=(1-ff[i])*(1-tt[i])
            row[bn[side+'HandThumb1']]=tt[i]*(1-.55*ff[i])
            distance=np.array([(width[i]-offset)**2 for offset in finger_offsets])
            fw=np.exp(-distance/(2*.010**2));fw/=fw.sum()
            for finger,w in zip(['Index','Middle','Ring','Pinky'],fw):row[bn[side+'Hand'+finger+'1']]+=w*ff[i]*(1-.55*tt[i])
            row/=max(row.sum(),1e-12);desired[i]=row
        pairlist=[(faces[0],faces[1]) for (a,b),faces in edges.items() if len(faces)==2 and side_ids[a] and side_ids[b] and ((fwd[a]+fwd[b])*.5<.079)]
        pair=np.asarray(pairlist,dtype=int)
        def evaluate(weights):
            positions=np.einsum('vb,vbi->vi',weights,transformed)
            ts=positions[triangles];normals=np.cross(ts[:,1]-ts[:,0],ts[:,2]-ts[:,0]);normals/=np.maximum(np.linalg.norm(normals,axis=1)[:,None],1e-12)
            angles=np.degrees(np.arccos(np.clip(np.sum(normals[pair[:,0]]*normals[pair[:,1]],axis=1),-1,1)))
            return {'over_120_degrees':int(np.sum(angles>120)),'over_90_degrees':int(np.sum(angles>90)),
                'over_45_degrees':int(np.sum(angles>45)),'angle_excess_sum':float(np.maximum(angles-30,0).sum())}
        baseline_score=evaluate(original);variants=[];best=None
        for strength in [.35,.60,.85,1.0]:
            candidate=original*(1-amount[:,None]*strength)+desired*(amount[:,None]*strength)
            # Discarding low weights is part of the actual four-weight export contract.
            for i in np.where(region)[0]:
                keep=np.argpartition(candidate[i],-4)[-4:];row=np.zeros(len(bones));row[keep]=candidate[i,keep];row/=row.sum();candidate[i]=row
            score=evaluate(candidate);score['strength']=strength;variants.append(score)
            key=(score['over_120_degrees'],score['over_90_degrees'],score['angle_excess_sum'])
            if best is None or key<best[0]:best=(key,candidate,score)
        chosen=best[1];ids=np.where(side_ids)[0];result[ids]=chosen[ids]
        changed=np.max(np.abs(chosen-original),axis=1)>1e-7
        report[side]={'baseline_palm_angles':baseline_score,'variants':variants,'chosen':best[2],
            'changed_vertices':int(np.sum(changed&side_ids)),'locked_contact_region_vertices':int(np.sum(protected&side_ids)),
            'locked_contact_weights_unchanged':bool(np.array_equal(chosen[protected],original[protected]))}
    for i,row in enumerate(result):
        if np.max(np.abs(row-original[i]))<1e-7:continue
        for group in hand.vertex_groups:group.remove([i])
        for index in np.where(row>1e-7)[0]:hand.vertex_groups[names[index]].add([i],float(row[index]),'REPLACE')
    return report

report={'status':'STATIC_VISUAL_DIAGNOSTIC_NOT_RIG_PASS','source':str(SOURCE),'sides':{},'actions':len(bpy.data.actions)}
if REFINE:report['root_weight_refinement']=refine_roots()
for side in ['Right','Left']:
    if SIDE_OVERRIDE is not None and side!=SIDE_OVERRIDE:continue
    world=pose(side);report['sides'][side]=diagnostics(side,world)
    presets=['ready','pen-up','elbow-deep','pronation-flex','supination-extend'] if STRESS else ['ready']
    if POSE_OVERRIDE is not None:presets=POSE_OVERRIDE
    report['sides'][side]['views']=[]
    for preset in presets:
        world=pose(side,preset)
        for view in ['dorsal','palm']:report['sides'][side]['views'].append(render(side,view,'material',world,preset))
    if side=='Right' and not REFINE and not STRESS:
        for variant in ['no-normal','clay']:render(side,'palm',variant,world)
report['source_unchanged']=source_hash==digest(SOURCE);report['brush_source_unchanged']=brush_hash==digest(BRUSH)
(OUT/(PREFIX+'hand-sleeve-inspection.json')).write_text(json.dumps(report,indent=2))
if REFINE and not PROBE and not UNLINED:
    for p in rig.pose.bones:p.matrix_basis=Matrix.Identity(4)
    bpy.context.view_layer.update();bpy.ops.file.pack_all();bpy.ops.wm.save_as_mainfile(filepath=str(ART/'DosaV2_HandsRefined.blend'))
print(json.dumps({k:v for k,v in report.items() if k!='sides'}),flush=True)
if PROBE:
    (OUT/(PREFIX+'visible-surface-probes.json')).write_text(json.dumps(PROBES,indent=2));print(json.dumps(PROBES),flush=True)
