"""Read-only static deformation QA of DosaV2_Surfaces.blend; no actions or source save."""
import bpy, bmesh, json, math, hashlib
import numpy as np
from pathlib import Path
from mathutils import Vector, Matrix, Quaternion
from mathutils.kdtree import KDTree

ROOT=Path(__file__).resolve().parents[3]
SOURCE=ROOT/'Art/PlayerV2/DosaV2_Surfaces.blend'
OUT=ROOT/'Art/PlayerV2/Inspect/BodyPoses';OUT.mkdir(parents=True,exist_ok=True)


def digest(path):return hashlib.sha256(path.read_bytes()).hexdigest()
source_hash=digest(SOURCE)
bpy.ops.wm.open_mainfile(filepath=str(SOURCE))
scene=bpy.context.scene;rig=bpy.data.objects['DosaV2_Rig']
meshes=[o for o in scene.objects if o.type=='MESH' and o.name!='DosaV2_SourceSurface']
for o in scene.objects:
    if o.type=='MESH':o.hide_render=o not in meshes
for o in meshes:o.hide_set(False)


def aim_bone(name,direction):
    p=rig.pose.bones[name];m=p.matrix.copy();rotation=m.to_quaternion()
    delta=(rotation@Vector((0,1,0))).rotation_difference(Vector(direction).normalized())
    changed=(delta@rotation).to_matrix().to_4x4();changed.translation=m.translation;p.matrix=changed
    bpy.context.view_layer.update()


def world_twist(name,axis,degrees):
    p=rig.pose.bones[name];m=p.matrix.copy()
    changed=(Quaternion(Vector(axis).normalized(),math.radians(degrees))@m.to_quaternion()).to_matrix().to_4x4()
    changed.translation=m.translation;p.matrix=changed;bpy.context.view_layer.update()


def reset():
    for p in rig.pose.bones:p.matrix_basis=Matrix.Identity(4)
    bpy.context.view_layer.update()


def lower():
    for side,sign in [('Left',1),('Right',-1)]:aim_bone(side+'Arm',(sign*.08,0,-1))


def pose(name):
    reset()
    if name=='rest':return
    if name in ['lowered','elbow_fold','forearm_twist','torso_twist','squat','step']:lower()
    if name=='raised':
        for side,sign in [('Left',1),('Right',-1)]:aim_bone(side+'Arm',(sign*.38,0,.93))
    if name=='reach':
        for side,sign in [('Left',1),('Right',-1)]:
            aim_bone(side+'Arm',(sign*.06,-1,.02));aim_bone(side+'ForeArm',(sign*.04,-1,.16))
    if name=='cross':
        for side,sign in [('Left',1),('Right',-1)]:
            aim_bone(side+'Arm',(-sign*.66,-.72,.12*sign));aim_bone(side+'ForeArm',(-sign*.92,.23,.15))
    if name=='elbow_fold':
        for side in ['Left','Right']:aim_bone(side+'ForeArm',(0,-.8,.6))
    if name=='forearm_twist':
        for side,sign in [('Left',1),('Right',-1)]:
            aim_bone(side+'ForeArm',(sign*.04,-.5,-.866))
            direction=rig.pose.bones[side+'ForeArm'].matrix.to_quaternion()@Vector((0,1,0))
            world_twist(side+'ForeArm',direction,sign*95)
    if name=='torso_twist':world_twist('Spine01',(0,0,1),40)
    if name=='squat':
        p=rig.pose.bones['Hips'];m=p.matrix.copy();m.translation+=Vector((0,0,-.20));p.matrix=m;bpy.context.view_layer.update()
        for side,sign in [('Left',1),('Right',-1)]:
            aim_bone(side+'UpLeg',(sign*.1,-.68,-.73));aim_bone(side+'Leg',(0,.68,-.73))
            aim_bone(side+'Foot',(0,-.86,-.51))
    if name=='step':
        aim_bone('LeftUpLeg',(0,-.7,-.714));aim_bone('LeftLeg',(0,.2,-.98));aim_bone('LeftFoot',(0,-.86,-.51))


rest={};boundaries=[];part_reports=[]
for obj in meshes:
    rest[obj.name]=np.asarray([obj.matrix_world@v.co for v in obj.data.vertices])
    bm=bmesh.new();bm.from_mesh(obj.data);bm.verts.ensure_lookup_table()
    indices={v.index for e in bm.edges if e.is_boundary for v in e.verts}
    for index in indices:boundaries.append((obj.name,index,Vector(rest[obj.name][index])))
    obj.data.calc_loop_triangles()
    coordinates=rest[obj.name]
    report={'name':obj.name,'vertices':len(obj.data.vertices),'triangles':len(obj.data.loop_triangles),
            'boundary_vertices':len(indices),'boundary_edges':sum(e.is_boundary for e in bm.edges),
            'minimum_xyz':coordinates.min(axis=0).tolist(),'maximum_xyz':coordinates.max(axis=0).tolist(),
            'surface_role':obj.get('surface_role',''),'vertex_groups':[g.name for g in obj.vertex_groups]}
    part_reports.append(report);bm.free()

kd=KDTree(len(boundaries))
for i,(_,_,p) in enumerate(boundaries):kd.insert(p,i)
kd.balance();seams=[]
for i,(name,vertex,point) in enumerate(boundaries):
    candidates=kd.find_range(point,.004)
    for _,other,distance in candidates:
        other_name,other_vertex,_=boundaries[other]
        if i>=other or name==other_name:continue
        seams.append((name,vertex,other_name,other_vertex,float(distance)))


def evaluated_points():
    result={};depsgraph=bpy.context.evaluated_depsgraph_get()
    for obj in meshes:
        evaluated=obj.evaluated_get(depsgraph);mesh=evaluated.to_mesh()
        result[obj.name]=np.asarray([evaluated.matrix_world@v.co for v in mesh.vertices]);evaluated.to_mesh_clear()
    return result


def diagnose(name):
    current=evaluated_points();gaps=[];stretch=[]
    for a,ai,b,bi,original in seams:
        distance=float(np.linalg.norm(current[a][ai]-current[b][bi]))
        if distance>.012 and distance-original>.01:
            gaps.append({'objects':[a,b],'rest_vertex_xyz':[rest[a][ai].tolist(),rest[b][bi].tolist()],
                         'posed_vertex_xyz':[current[a][ai].tolist(),current[b][bi].tolist()],
                         'rest_gap_m':original,'posed_gap_m':distance,'vertices':[ai,bi]})
    # Keep independently located worst pairs, rather than hundreds of copies of one seam.
    groups={}
    for gap in gaps:
        key=tuple(sorted(gap['objects']))
        if key not in groups or gap['posed_gap_m']>groups[key]['posed_gap_m']:groups[key]=gap
    for obj in meshes:
        r=rest[obj.name];p=current[obj.name]
        worst=None
        for edge in obj.data.edges:
            a,b=edge.vertices;before=float(np.linalg.norm(r[a]-r[b]));after=float(np.linalg.norm(p[a]-p[b]))
            if before<.0005:continue
            ratio=after/before
            if ratio>2 and after-before>.012 and (worst is None or ratio>worst['ratio']):
                worst={'object':obj.name,'ratio':ratio,'rest_length_m':before,'posed_length_m':after,
                       'rest_xyz':((r[a]+r[b])*.5).tolist(),'posed_xyz':((p[a]+p[b])*.5).tolist(),'vertices':[int(a),int(b)]}
        if worst:stretch.append(worst)
    return {'pose':name,'seam_gap_pairs':sorted(groups.values(),key=lambda g:-g['posed_gap_m']),
            'stretched_edges':sorted(stretch,key=lambda g:-g['ratio']),
            'posed_bounds':[np.concatenate(list(current.values())).min(axis=0).tolist(),np.concatenate(list(current.values())).max(axis=0).tolist()]}


scene.render.engine='CYCLES';scene.cycles.samples=10;scene.cycles.use_denoising=True
scene.render.resolution_x=850;scene.render.resolution_y=1000;scene.render.resolution_percentage=100
scene.render.image_settings.file_format='PNG';scene.world.color=(.15,.15,.15);scene.view_settings.view_transform='AgX'
clay=bpy.data.materials.new('BodyPoseDiagnosticClay');clay.use_nodes=True
bsdf=clay.node_tree.nodes.get('Principled BSDF');bsdf.inputs['Base Color'].default_value=(.36,.39,.41,1);bsdf.inputs['Roughness'].default_value=.8
scene.view_layers[0].material_override=clay
for name,location,energy,size in [('BodyQAKey',(2,-3,4),500,3),('BodyQAFill',(-2,-1,2),220,3),('BodyQARim',(1,3,3),500,2)]:
    obj=bpy.data.objects.new(name,bpy.data.lights.new(name,'AREA'));scene.collection.objects.link(obj)
    obj.data.energy=energy;obj.data.size=size;obj.location=location;obj.rotation_euler=(Vector((0,0,1))-obj.location).to_track_quat('-Z','Y').to_euler()
camera=bpy.data.objects.new('BodyQACamera',bpy.data.cameras.new('BodyQACamera'));scene.collection.objects.link(camera)
camera.data.type='ORTHO';camera.data.ortho_scale=2.1;scene.camera=camera


def render(name,view):
    center=Vector((0,0,.87))
    positions={'front':(0,-4,1.02),'quarter':(3,-4,1.4),'back':(0,4,1.1)}
    camera.location=positions[view];camera.rotation_euler=(center-camera.location).to_track_quat('-Z','Y').to_euler()
    scene.render.filepath=str(OUT/(name+'-'+view+'.png'));bpy.ops.render.render(write_still=True)


results=[]
for name in ['rest','lowered','raised','reach','cross','elbow_fold','forearm_twist','torso_twist','squat','step']:
    pose(name);results.append(diagnose(name))
    for view in ['front','quarter']+(['back'] if name in ['rest','lowered','raised','cross'] else []):render(name,view)
    print(json.dumps({'pose':name,'gap_pairs':len(results[-1]['seam_gap_pairs']),'stretched_parts':len(results[-1]['stretched_edges'])}),flush=True)

reset()
report={'status':'STATIC_DEFORMATION_REVIEW_NOT_RIG_PASS','source':str(SOURCE),'source_sha256':source_hash,
        'source_unchanged':digest(SOURCE)==source_hash,'production_actions_created':0,'actions_present':len(bpy.data.actions),
        'render':'Uniform clay material override removes atlas and normal-map artifacts; geometry and original smooth normals remain.',
        'limitations':'Cloth simulation is intentionally off. Some robe motion waits for authored Unity Cloth. Seam pair distances identify geometric separation but need visual review for intended overlapping garments.',
        'parts':part_reports,'poses':results}
(OUT/'body-pose-diagnostic.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print(json.dumps({'report':str(OUT/'body-pose-diagnostic.json'),'source_unchanged':report['source_unchanged'],'actions_created':0}),flush=True)
