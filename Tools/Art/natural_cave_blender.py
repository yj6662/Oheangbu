"""Blender offline derivative and reproducible neutral geometry review.

Run: blender --background --python Tools/Art/natural_cave_blender.py
The Unity export uses metres and original x/y/z, independent of Blender display.
"""
import bpy
import bmesh
import json
import math
from pathlib import Path
from mathutils import Vector

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Art/World/WorldMacro/Playtest/NaturalCave'
path=OUT/'geometry.json'
doc=json.loads(path.read_text(encoding='utf-8'))
bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
source=bpy.data.collections.new('Source_SDF_Interior');bpy.context.scene.collection.children.link(source)
runtime=bpy.data.collections.new('Runtime_Cave');bpy.context.scene.collection.children.link(runtime)
def display(v):return (v['x'],-v['z'],v['y'])
def unity(v):return {'x':round(float(v.x),5),'y':round(float(v.z),5),'z':round(float(-v.y),5)}
stats=[]
for record in doc['meshes']:
    mesh=bpy.data.meshes.new(record['name']+'_Source')
    tr=record['triangles']
    mesh.from_pydata([display(v) for v in record['vertices']],[],[tr[i:i+3] for i in range(0,len(tr),3)])
    mesh.update()
    original=bpy.data.objects.new(record['name']+'_Source',mesh);source.objects.link(original)
    original.hide_render=True;original.hide_set(True)
    obj=bpy.data.objects.new(record['name'],mesh.copy());runtime.objects.link(obj)
    bpy.context.view_layer.objects.active=obj;obj.select_set(True)
    if 'Interior' in obj.name:
        # Preserve exact floor/mouth boundary vertices while reducing interior.
        bm=bmesh.new();bm.from_mesh(obj.data)
        boundary={v for e in bm.edges if e.is_boundary for v in e.verts}
        eligible_edges=[e for e in bm.edges if all(v not in boundary for v in e.verts)]
        bmesh.ops.dissolve_limit(bm,angle_limit=math.radians(3.0),use_dissolve_boundaries=False,
                                verts=[v for v in bm.verts if v not in boundary],edges=eligible_edges)
        bmesh.ops.triangulate(bm,faces=list(bm.faces),quad_method='BEAUTY',ngon_method='BEAUTY')
        bm.to_mesh(obj.data);bm.free();obj.data.update()
        for p in obj.data.polygons:p.use_smooth=True
    else:
        bm=bmesh.new();bm.from_mesh(obj.data)
        bmesh.ops.dissolve_limit(bm,angle_limit=.001,use_dissolve_boundaries=False,verts=list(bm.verts),edges=list(bm.edges))
        bmesh.ops.triangulate(bm,faces=list(bm.faces),quad_method='BEAUTY',ngon_method='BEAUTY')
        bm.to_mesh(obj.data);bm.free();obj.data.update()
    obj.data.calc_loop_triangles()
    vertices=[unity(v.co) for v in obj.data.vertices]
    normals=[unity(v.normal) for v in obj.data.vertices]
    faces=[int(i) for t in obj.data.loop_triangles for i in t.vertices]
    print(obj.name,'runtime',len(faces)//3,'source',record['triangleCount'],flush=True)
    stats.append({'name':obj.name,'sourceTriangles':record['triangleCount'],'runtimeTriangles':len(faces)//3})
    record.update(vertices=vertices,normals=normals,triangles=faces,vertexCount=len(vertices),triangleCount=len(faces)//3)
    obj.select_set(False)

# The editable file includes hidden SDF source and separate export meshes.
mat=bpy.data.materials.new('Neutral_Rock_Inspection');mat.diffuse_color=(.23,.21,.185,1)
mat.use_nodes=True
bs=next((n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED'),None)
if bs is None:
    bs=mat.node_tree.nodes.new('ShaderNodeBsdfPrincipled')
    output=mat.node_tree.nodes.new('ShaderNodeOutputMaterial')
    mat.node_tree.links.new(bs.outputs['BSDF'],output.inputs['Surface'])
bs.inputs['Base Color'].default_value=(.23,.21,.185,1);bs.inputs['Roughness'].default_value=.94
for obj in runtime.objects:obj.data.materials.append(mat)
scene=bpy.context.scene;scene.render.engine='BLENDER_EEVEE'
scene.world.color=(.12,.12,.12)
scene.render.resolution_x=1920;scene.render.resolution_y=1080;scene.render.resolution_percentage=100
scene.view_settings.view_transform='AgX'
for idx,(pos,power,size) in enumerate([((-128,0,9),1600,8),((-73,-5,12),1800,9),((-35,0,8),1200,8),((1,26,6),1600,10)]):
    light=bpy.data.lights.new('Inspection_Light_'+str(idx),'AREA');light.energy=power;light.shape='DISK';light.size=size
    obj=bpy.data.objects.new(light.name,light);scene.collection.objects.link(obj);obj.location=pos
    obj.rotation_euler=(0,0,0)
camdata=bpy.data.cameras.new('Inspection_Camera');camera=bpy.data.objects.new('Inspection_Camera',camdata);scene.collection.objects.link(camera);scene.camera=camera
camera.location=(-137,-2,2.3);target=Vector((-105,2,3));camera.rotation_euler=(target-camera.location).to_track_quat('-Z','Y').to_euler();camdata.lens=23;camdata.clip_end=500
source.hide_render=True
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'Natural_Cave_Source.blend'))
doc['offlineReduction']={'method':'Limited angle dissolve, preserve interior boundary vertices; exact planar floor dissolve','statistics':stats}
path.write_text(json.dumps(doc,separators=(',',':')),encoding='utf-8')
(OUT/'blender_statistics.json').write_text(json.dumps(stats,indent=2),encoding='utf-8')
scene.render.filepath=str(OUT/'offline_inside.png');bpy.ops.render.render(write_still=True)
camera.location=(-72,-106,105);target=Vector((-75,0,1));camera.rotation_euler=(target-camera.location).to_track_quat('-Z','Y').to_euler();camdata.type='ORTHO';camdata.ortho_scale=190
# A true plan geometry shot, roof deliberately hidden only for this diagnostic.
for obj in runtime.objects:
    if 'Interior' in obj.name:obj.hide_render=True
scene.render.filepath=str(OUT/'offline_floor_footprint.png');bpy.ops.render.render(write_still=True)
