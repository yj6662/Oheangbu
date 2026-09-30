"""Read-only derivative verification and at most two explicitly requested stills."""
import bpy, json, math, sys, hashlib
from pathlib import Path
from mathutils import Vector, Matrix
ROOT=Path('C:/Users/yj666/Oheangbu');OUT=ROOT/'Art/Demo/Chapter3/Cheongryong'
report=json.loads((OUT/'rig_report.json').read_text(encoding='utf-8'))
render='--render' in sys.argv
bpy.ops.wm.open_mainfile(filepath=str(OUT/'Cheongryong_Prototype.blend'))
rig=bpy.data.objects['Cheongryong_PrototypeRig'];obj=bpy.data.objects['SM_Cheongryong_Prototype']
expected=[obj.matrix_world@v.co for v in obj.data.vertices]
native_actions=[a.name for a in bpy.data.actions]
native_mesh=obj.data;edges={}
for p in native_mesh.polygons:
    for e in p.edge_keys:edges[e]=edges.get(e,0)+1
assert all(math.isfinite(x) for p in expected for x in p)
native={'triangles':report['triangles'],'boundaryEdges':sum(n==1 for n in edges.values()),'nonManifoldEdges':sum(n!=2 for n in edges.values()),'actions':native_actions}
if render:
    # Called only after host committed-memory check; never save staging objects.
    scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=16;scene.cycles.use_denoising=True
    scene.render.resolution_x=1920;scene.render.resolution_y=1080;scene.render.resolution_percentage=100
    scene.world=bpy.data.worlds.new('PrototypeReviewWorld');scene.world.color=(.18,.18,.18)
    bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,0));floor=bpy.context.object;floor.name='ReviewFloor'
    m=bpy.data.materials.new('ReviewFloor');m.diffuse_color=(.12,.14,.16,1);floor.data.materials.append(m)
    for name,location,energy,size in [('Key',(0,-5,8),1800,7),('Fill',(-7,0,4),1300,6),('Rim',(2,6,6),1600,5)]:
        ld=bpy.data.lights.new(name,'AREA');ld.energy=energy;ld.shape='DISK';ld.size=size;lo=bpy.data.objects.new(name,ld);scene.collection.objects.link(lo);lo.location=location;lo.rotation_euler=(Vector((0,0,.6))-lo.location).to_track_quat('-Z','Y').to_euler()
    cd=bpy.data.cameras.new('ReviewCamera');camera=bpy.data.objects.new('ReviewCamera',cd);scene.collection.objects.link(camera);scene.camera=camera;cd.type='ORTHO';cd.ortho_scale=11.7
    camera.location=(8,-10,15) if '--high' in sys.argv else (11,-10,8);camera.rotation_euler=(Vector((0,-.1,.65))-camera.location).to_track_quat('-Z','Y').to_euler()
    scene.render.image_settings.file_format='PNG';scene.render.filepath=str(OUT/('Cheongryong_Prototype_HighOblique.png' if '--high' in sys.argv else 'Cheongryong_Prototype_Oblique.png'));bpy.ops.render.render(write_still=True)
    print('RENDERED_ONE_STILL');sys.exit(0)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=str(OUT/'SM_Cheongryong_Prototype.fbx'),use_anim=True)
rig=next(o for o in bpy.data.objects if o.type=='ARMATURE');meshes=[o for o in bpy.data.objects if o.type=='MESH'];obj=meshes[0]
rig.animation_data.action=None
for p in rig.pose.bones:p.matrix_basis=Matrix.Identity(4)
bpy.context.view_layer.update()
actual=[obj.matrix_world@v.co for v in obj.data.vertices]
obj.data.calc_loop_triangles()
counts={'meshCount':len(meshes),'triangles':len(obj.data.loop_triangles),'vertices':len(actual),'bones':len(rig.data.bones),'actions':[a.name for a in bpy.data.actions],'maximumNativeRoundtripVertexErrorM':max((a-b).length for a,b in zip(expected,actual)),'maxWeightInfluences':max(len(v.groups) for v in obj.data.vertices),'weightSumError':max(abs(sum(g.weight for g in v.groups)-1) for v in obj.data.vertices),'rootTranslationMaxM':0,'finitePoseSamples':0}
assert counts['triangles']==report['triangles'] and len(actual)==len(expected) and len(meshes)==1 and counts['bones']==40
assert counts['maximumNativeRoundtripVertexErrorM']<1e-4
assert counts['maxWeightInfluences']<=4 and counts['weightSumError']<1e-5
for action in bpy.data.actions:
    rig.animation_data.action=action
    if action.slots:rig.animation_data.action_slot=action.slots[0]
    for f in [action.frame_range[0],sum(action.frame_range)/2,action.frame_range[1]]:
        bpy.context.scene.frame_set(round(f));bpy.context.view_layer.update();deps=bpy.context.evaluated_depsgraph_get();e=obj.evaluated_get(deps);em=e.to_mesh()
        assert all(math.isfinite(c) for v in em.vertices for c in v.co);counts['finitePoseSamples']+=1;e.to_mesh_clear()
        counts['rootTranslationMaxM']=max(counts['rootTranslationMaxM'],rig.pose.bones['Root'].location.length)
assert counts['rootTranslationMaxM']<1e-6
assert all(hashlib.sha256(Path(p).read_bytes()).hexdigest()==h for p,h in report['sourceHashesBefore'].items())
counts['sourceHashesUnchanged']=True;counts['nativeTopology']=native;counts['status']='TECHNICAL_FBX_ROUNDTRIP_PASS_NOT_ART_OR_RUNTIME_APPROVAL'
(OUT/'fbx_verification.json').write_text(json.dumps(counts,indent=2),encoding='utf-8');print('CHEONGRYONG_VERIFIED',json.dumps(counts))
