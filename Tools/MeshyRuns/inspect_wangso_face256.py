"""Read-only head inspection: separate geometry, albedo and normal-map artifacts."""
import bpy, math, json
from pathlib import Path
from mathutils import Vector

root = Path('C:/Users/yj666/Oheangbu/Art/Characters/Principal256')
out = root / 'wangso/face_inspection'
out.mkdir(exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=str(root / 'wangso/prepared/Body.glb'))
meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH' and len(o.vertex_groups)]
for o in list(bpy.context.scene.objects):
    if o.type == 'MESH' and o not in meshes:
        bpy.data.objects.remove(o, do_unlink=True)
pts = [o.matrix_world @ v.co for o in meshes for v in o.data.vertices]
lo = Vector([min(p[i] for p in pts) for i in range(3)])
hi = Vector([max(p[i] for p in pts) for i in range(3)])
h = hi.z - lo.z
focus = Vector(((lo.x+hi.x)/2, (lo.y+hi.y)/2, hi.z-h*.078))
scene = bpy.context.scene
scene.render.engine = 'BLENDER_EEVEE'
scene.render.resolution_x = 1000
scene.render.resolution_y = 1000
scene.render.resolution_percentage = 100
scene.view_settings.view_transform = 'AgX'
world = bpy.data.worlds.new('FaceReview'); world.use_nodes = True
bg = world.node_tree.nodes.new('ShaderNodeBackground')
wo = world.node_tree.nodes.new('ShaderNodeOutputWorld')
world.node_tree.links.new(bg.outputs[0], wo.inputs['Surface'])
bg.inputs[0].default_value = (.3,.3,.3,1); bg.inputs[1].default_value = .6
scene.world = world
for xyz, energy in [((1,-2,2),450),((-2,-1,.6),300)]:
    bpy.ops.object.light_add(type='AREA', location=focus+Vector(xyz)*h)
    light=bpy.context.object; light.data.energy=energy*h*h;light.data.size=h*2
    light.rotation_euler=(focus-light.location).to_track_quat('-Z','Y').to_euler()
bpy.ops.object.camera_add(); camera=bpy.context.object;scene.camera=camera
camera.data.type='ORTHO';camera.data.ortho_scale=h*.235
originals={o.name:list(o.data.materials) for o in meshes}
report={'bounds':[list(lo),list(hi)],'objects':[]}
for o in meshes:
    report['objects'].append({'name':o.name,'vertices':len(o.data.vertices),'polygons':len(o.data.polygons),'custom_normals':o.data.has_custom_normals,'head_vertices':sum((o.matrix_world@v.co).z>hi.z-h*.15 for v in o.data.vertices),'materials':[m.name for m in o.data.materials], 'material_nodes':[[n.type for n in m.node_tree.nodes] for m in o.data.materials]})
for mode in ['original','no_normal','smooth_geometry','clay','welded_normals']:
    for o in meshes:
        o.data.materials.clear()
        for source in originals[o.name]:
            mat=source.copy();o.data.materials.append(mat)
            for n in mat.node_tree.nodes:
                if n.type!='BSDF_PRINCIPLED':continue
                if mode!='original':
                    for link in list(n.inputs['Normal'].links):mat.node_tree.links.remove(link)
                if mode!='original':
                    for socket_name in ['Emission Color','Emission Strength','Alpha']:
                        for link in list(n.inputs[socket_name].links):mat.node_tree.links.remove(link)
                    n.inputs['Emission Strength'].default_value=0
                    n.inputs['Alpha'].default_value=1
                if mode=='clay':
                    for link in list(n.inputs['Base Color'].links):mat.node_tree.links.remove(link)
                    n.inputs['Base Color'].default_value=(.38,.38,.38,1)
                n.inputs['Roughness'].default_value=.85
        if mode in ['smooth_geometry','clay','welded_normals']:
            # Clear imported split normals rather than merely setting smooth polygons.
            bpy.context.view_layer.objects.active=o
            o.select_set(True)
            if o.data.has_custom_normals:
                bpy.ops.mesh.customdata_custom_splitnormals_clear()
            for p in o.data.polygons:p.use_smooth=True
            for e in o.data.edges:e.use_edge_sharp=False
        if mode=='welded_normals':
            from collections import defaultdict
            groups=defaultdict(list)
            for v in o.data.vertices:groups[tuple(round(float(x),5) for x in v.co)].append(v.index)
            normals=[Vector((0,0,0)) for v in o.data.vertices]
            for p in o.data.polygons:
                for vi in p.vertices:normals[vi]+=p.normal*p.area
            merged=[Vector((0,0,0)) for v in o.data.vertices]
            for indices in groups.values():
                n=sum((normals[i] for i in indices),Vector((0,0,0))).normalized()
                for i in indices:merged[i]=n
            o.data.normals_split_custom_set_from_vertices(merged)
            report['coincident_vertex_groups']=len(groups)
    for view,angle in [('front',0),('quarter',math.pi/5)]:
        camera.location=focus+Vector((math.sin(angle)*h*2,-math.cos(angle)*h*2,0))
        camera.rotation_euler=(focus-camera.location).to_track_quat('-Z','Y').to_euler()
        scene.render.filepath=str(out/(mode+'_'+view+'.png'))
        bpy.ops.render.render(write_still=True)
(out/'report.json').write_text(json.dumps(report,indent=2))
