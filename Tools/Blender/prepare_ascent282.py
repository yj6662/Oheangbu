"""Inspect actual Meshy geometry; export LODs/collision for the isolated ascent."""
import bpy, math, json, sys
from pathlib import Path
from mathutils import Vector
from mathutils.bvhtree import BVHTree
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Art/World/Compact/Rebuild/Ascent282'
MESH=OUT/'Meshes';MESH.mkdir(parents=True,exist_ok=True)
NAMES=['GraniteButtress','GraniteShoulder']
def clear():
    bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
def load(name):
    clear()
    file=OUT/'Meshy'/name/'preview/model_urls_glb.glb'
    bpy.ops.import_scene.gltf(filepath=str(file))
    meshes=[o for o in bpy.context.scene.objects if o.type=='MESH']
    bpy.ops.object.select_all(action='DESELECT')
    for o in meshes:o.select_set(True)
    bpy.context.view_layer.objects.active=meshes[0];bpy.ops.object.join()
    obj=meshes[0];bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
    lo=Vector(tuple(min(v.co[i] for v in obj.data.vertices) for i in range(3)))
    hi=Vector(tuple(max(v.co[i] for v in obj.data.vertices) for i in range(3)))
    size=hi-lo
    for v in obj.data.vertices:v.co=(v.co-Vector(((hi.x+lo.x)/2,(hi.y+lo.y)/2,lo.z)))/size.z
    import bmesh
    bm=bmesh.new();bm.from_mesh(obj.data)
    bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=.00001)
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(obj.data);bm.free()
    obj.name=name;obj.data.update()
    return obj,list(size)
def reduced(obj, target):
    copy=obj.copy();copy.data=obj.data.copy();bpy.context.collection.objects.link(copy)
    bpy.context.view_layer.objects.active=copy
    mod=copy.modifiers.new('Preserve_rock_planes','DECIMATE');mod.ratio=min(1,target/len(copy.data.polygons))
    bpy.ops.object.modifier_apply(modifier=mod.name)
    return copy
def render(obj,name):
    mat=bpy.data.materials.new('neutral rock inspection');mat.diffuse_color=(.32,.34,.35,1)
    obj.data.materials.clear();obj.data.materials.append(mat)
    world=bpy.context.scene.world or bpy.data.worlds.new('World');bpy.context.scene.world=world;world.use_nodes=True
    world.node_tree.nodes['Background'].inputs[0].default_value=(.1,.11,.12,1)
    world.node_tree.nodes['Background'].inputs[1].default_value=.6
    scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=24
    scene.render.resolution_x=1000;scene.render.resolution_y=1000;scene.render.resolution_percentage=100
    scene.view_settings.view_transform='AgX'
    for position,energy,size in [((-2,-3,4),450,3),((3,1,2),220,2)]:
        data=bpy.data.lights.new('softbox','AREA');data.energy=energy;data.shape='DISK';data.size=size
        light=bpy.data.objects.new('softbox',data);scene.collection.objects.link(light);light.location=position
        light.rotation_euler=(Vector((0,0,.5))-light.location).to_track_quat('-Z','Y').to_euler()
    cam=bpy.data.objects.new('Camera',bpy.data.cameras.new('Camera'));scene.collection.objects.link(cam);scene.camera=cam
    extent=max(obj.dimensions)
    for index,pos in enumerate([(1.45,-2.4,1.5),(-1.6,2.4,1.5)]):
        cam.location=Vector(pos)*max(1,extent*.8);cam.rotation_euler=(Vector((0,0,.47))-cam.location).to_track_quat('-Z','Y').to_euler()
        cam.data.type='ORTHO';cam.data.ortho_scale=max(1.45,extent*1.4)
        scene.render.filepath=str(OUT/f'{name}-{index}.png');bpy.ops.render.render(write_still=True)
def route(z):
    z=max(0,min(330,z));return (42*math.sin(z*.031)+.19*z,22+.265*z+2*math.sin(z*.04))

# Widely spaced, partly embedded masses. No evenly repeated line of boulders.
PLACEMENTS=[(0,0,45),(1,52,105),(1,109,85),
            (1,167,118),(1,222,81),(1,280,105),(0,335,48)]

def export_geometry():
    originals={};stats={}
    for name in NAMES:
        obj,size=load(name)
        stats[name]={'source_bounds':size,'source_triangles':len(obj.data.polygons)}
        originals[name]=[]
        for target in (32000,10000,2200):
            copy=reduced(obj,target);mesh=copy.data;mesh.calc_loop_triangles()
            vs=[v.co.copy() for v in mesh.vertices];faces=[tuple(t.vertices) for t in mesh.loop_triangles]
            originals[name].append((vs,faces));bpy.data.objects.remove(copy,do_unlink=True)
        stats[name]['lod_triangles']=[len(fs) for vs,fs in originals[name]]
    clear();counts=[]
    for index,(variant,zcenter,height) in enumerate(PLACEMENTS):
        name=NAMES[variant]
        reference=originals[name][0][0]
        lo=Vector(tuple(min(v[i] for v in reference) for i in range(3)))
        hi=Vector(tuple(max(v[i] for v in reference) for i in range(3)))
        shift=0
        for level,(vs,fs) in enumerate(originals[name]):
            size=hi-lo
            transformed=[]
            rx,ry=route(zcenter)
            derivative=42*.031*math.cos(max(0,min(330,zcenter))*.031)+.19
            length=math.sqrt(1+derivative*derivative)
            angle=math.atan(derivative)+math.radians([-10,15,28,-13,20,-17,5][index])
            tangent=(math.sin(angle),math.cos(angle))
            inward=(tangent[1],-tangent[0])
            for v in vs:
                # Meshy X is the face width, -Y its outward-facing side.
                u=(v.x-lo.x)/size.x
                if index%3==2:u=1-u
                # Rigid, uniformly scaled Meshy rock. Do not bend its topology
                # around the road: that stretches real fractures into ribbons.
                along=(u-.5)*size.x/size.z*height
                vertical=(v.z-lo.z)/size.z
                y=vertical*height-(25 if variant==0 else 19)
                thick=(v.y-lo.y)/size.z*height
                x=rx+inward[0]*(4.3+thick)+tangent[0]*along
                z=zcenter+inward[1]*(4.3+thick)+tangent[1]*along
                transformed.append((x,-z,ry+y))
            # Keep the lower generated shell out of the existing walkway.
            if level==0:
                shift=max([0]+[route(-p[1])[0]+4.2-p[0] for p in transformed
                              if -10<-p[1]<340 and route(-p[1])[1]-.5<p[2]<route(-p[1])[1]+4])
            transformed=[(x+shift,y,z) for x,y,z in transformed]
            mesh=bpy.data.meshes.new(f'Bedrock_{index}_LOD{level}');mesh.from_pydata(transformed,[],fs);mesh.update()
            # A mirrored face direction needs winding corrected.
            if index%3==2:
                import bmesh
                bm=bmesh.new();bm.from_mesh(mesh);bmesh.ops.reverse_faces(bm,faces=list(bm.faces));bm.to_mesh(mesh);bm.free()
            for p in mesh.polygons:p.use_smooth=True
            mesh.update();mesh.calc_loop_triangles()
            obj=bpy.data.objects.new(mesh.name,mesh);bpy.context.collection.objects.link(obj)
            # Small-scale geometry occlusion, independent of painted texture.
            bvh=BVHTree.FromPolygons([v.co for v in mesh.vertices],[tuple(t.vertices) for t in mesh.loop_triangles],all_triangles=True)
            colors=[]
            for v in mesh.vertices:
                n=v.normal;axis=Vector((0,0,1)) if abs(n.z)<.85 else Vector((1,0,0))
                tangent=n.cross(axis).normalized();bitangent=n.cross(tangent)
                occ=0
                for offset in (tangent*.8,-tangent*.8,bitangent*.8,-bitangent*.8):
                    hit=bvh.ray_cast(v.co+n*.08,(n+offset).normalized(),5)
                    if hit[0] is not None:occ+=1-min(1,hit[3]/5)
                shade=max(.35,1-occ*.16);colors.append(dict(r=shade,g=shade,b=shade,a=1))
            data=dict(vertices=[dict(x=v.co.x,y=v.co.z,z=-v.co.y) for v in mesh.vertices],
                      normals=[dict(x=v.normal.x,y=v.normal.z,z=-v.normal.y) for v in mesh.vertices],
                      uv=[dict(x=v.co.x*.25,y=-v.co.y*.25) for v in mesh.vertices],
                      triangles=[i for t in mesh.loop_triangles for i in t.vertices],colors=colors)
            (MESH/(mesh.name+'.json')).write_text(json.dumps(data,separators=(',',':')))
            counts.append(dict(name=mesh.name,triangles=len(mesh.loop_triangles),vertices=len(mesh.vertices)))
    bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'MeshyAscent282.blend'))
    placements=[dict(source=NAMES[v],route_z=z,height_before_burial=h) for v,z,h in PLACEMENTS]
    (OUT/'mesh-report.json').write_text(json.dumps(dict(sources=stats,placements=placements,meshes=counts),indent=2))

if '--inspect' in sys.argv:
    for name in NAMES:
        obj,size=load(name);print(name,'bounds',size,'faces',len(obj.data.polygons));render(obj,name)
else:export_geometry()
