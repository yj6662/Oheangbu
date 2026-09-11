from pathlib import Path
import bpy,math,json
from mathutils import Vector,Matrix
ROOT=Path('C:/Users/yj666/Oheangbu')
OUT=ROOT/'Art/SpellVFX120/Blender'
SOURCE=ROOT/'Art/SpellVFX120/MeshSources'
UNITY_TO_BLENDER=Matrix(((1,0,0),(0,0,-1),(0,1,0)))

def obj_read(p):
    verts=[];uvs=[];faces=[];face_uv=[]
    for line in p.read_text(encoding='utf-8-sig').splitlines():
        if line.startswith('v '):verts.append(UNITY_TO_BLENDER@Vector(tuple(map(float,line.split()[1:4]))))
        elif line.startswith('vt '):uvs.append(tuple(map(float,line.split()[1:3])))
        elif line.startswith('f '):
            fs=[];ft=[]
            for group in line.split()[1:]:
                a=group.split('/');fs.append(int(a[0])-1);ft.append(int(a[1])-1 if len(a)>1 and a[1] else -1)
            faces.append(fs);face_uv.append(ft)
    if not verts or not faces:return None
    mesh=bpy.data.meshes.new('UnitySource_'+p.stem);mesh.from_pydata(verts,[],faces);mesh.update()
    uv=mesh.uv_layers.new(name='UVMap')
    for poly,iu in zip(mesh.polygons,face_uv):
        for li,ui in zip(poly.loop_indices,iu):uv.data[li].uv=uvs[ui] if ui>=0 else (0,0)
    obj=bpy.data.objects.new('UnitySource_'+p.stem,mesh);bpy.context.scene.collection.objects.link(obj)
    for f in mesh.polygons:f.use_smooth=True
    return obj

def material(name,color):
    m=bpy.data.materials.get(name) or bpy.data.materials.new(name);m.use_nodes=True
    bs=next(n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED');bs.inputs['Base Color'].default_value=(*color,1);bs.inputs['Roughness'].default_value=.87
    return m

def setup():
    assert 'SpellVFX120' in bpy.data.filepath
    source=bpy.data.collections.new('SOURCE_UnityFactory_ReadOnly');bpy.context.scene.collection.children.link(source)
    stats=[]
    for p in sorted(SOURCE.glob('*.obj')):
        ob=obj_read(p)
        if ob is None:stats.append({'file':str(p),'status':'FAIL_EMPTY_OBJ'});continue
        for coll in list(ob.users_collection):coll.objects.unlink(ob)
        source.objects.link(ob);ob.hide_render=True;ob.hide_set(True)
        ob.data.materials.append(material('Source_Clay',(.40,.355,.275)))
        stats.append({'file':str(p),'status':'IMPORTED','vertices':len(ob.data.vertices),'triangles':sum(len(x.vertices)-2 for x in ob.data.polygons),'blender_dimensions':list(ob.dimensions)})
    scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=12;scene.cycles.use_denoising=True
    scene.render.resolution_x=1280;scene.render.resolution_y=720;scene.render.resolution_percentage=100
    scene.render.image_settings.file_format='PNG';scene.render.film_transparent=False
    scene.world=bpy.data.worlds.new('VfxLabWorld');scene.world.use_nodes=True
    bg=next(n for n in scene.world.node_tree.nodes if n.type=='BACKGROUND')
    bg.inputs[0].default_value=(.22,.205,.175,1)
    bg.inputs[1].default_value=.65
    for name,loc,energy,size in [('Key',(2,-3,4),180,4),('Fill',(-3,-1,2),100,3),('Rim',(0,3,2),110,3)]:
        light=bpy.data.lights.new(name,'AREA');light.energy=energy;light.shape='DISK';light.size=size
        ob=bpy.data.objects.new(name,light);scene.collection.objects.link(ob);ob.location=loc;ob.rotation_euler=(-ob.location).to_track_quat('-Z','Y').to_euler()
    camera=bpy.data.cameras.new('MeshLabCamera');ob=bpy.data.objects.new('MeshLabCamera',camera);scene.collection.objects.link(ob);scene.camera=ob;camera.type='ORTHO';camera.ortho_scale=1.9
    scene.view_settings.view_transform='AgX'
    (OUT/'unity_mesh_import.json').write_text(json.dumps(stats,indent=2),encoding='utf-8')
    bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'SpellVFX120_MeshLab.blend'))
    print(json.dumps(stats))

def render(name,objects,view='threequarter',ortho=1.8):
    scene=bpy.context.scene
    for o in scene.objects:
        if o.type=='MESH':o.hide_render=o not in objects
    positions={'threequarter':(2,-3,1.25),'front':(0,-3,.40),'side':(3,0,.6),'back':(0,3,.55),'top':(0,-.3,3)}
    cam=scene.camera;cam.location=positions[view];target=Vector((0,0,.025))
    cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=ortho
    scene.render.filepath=str(OUT/(name+'.png'))
    bpy.ops.render.render(write_still=True)
    print(scene.render.filepath)
