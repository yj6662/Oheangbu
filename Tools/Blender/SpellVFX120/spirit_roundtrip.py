import bpy,json,math
from pathlib import Path
from mathutils import Vector
from mathutils.kdtree import KDTree
OUT=Path('C:/Users/yj666/Oheangbu/Art/SpellVFX120/Blender')
manifest=json.loads((OUT/'spirit_export_manifest.json').read_text())
results=[]
for entry in manifest:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=entry['fbx'],use_anim=False,colors_type='LINEAR')
    meshes=[o for o in bpy.context.scene.objects if o.type=='MESH']
    source=json.loads(Path(entry['source_samples']).read_text());verts=source['vertices_blender'];colors=source['colors']
    kd=KDTree(len(verts))
    for i,v in enumerate(verts):kd.insert(Vector(v),i)
    kd.balance();max_distance=0;max_color=0;missing_color=0;triangles=0;bounds=[]
    for obj in meshes:
        me=obj.data;me.calc_loop_triangles();triangles+=len(me.loop_triangles)
        for v in me.vertices:
            point=obj.matrix_world@v.co;bounds.append(point);nearest,index,dist=kd.find(point);max_distance=max(max_distance,dist)
        attr=me.color_attributes.active_color
        if attr is None:missing_color+=1;continue
        pairs=[(v.index,attr.data[v.index].color) for v in me.vertices] if attr.domain=='POINT' else [(l.vertex_index,attr.data[l.index].color) for l in me.loops]
        for vi,color in pairs:
            point=obj.matrix_world@me.vertices[vi].co
            found=kd.find_range(point,1e-5)
            if not found:max_color=float('inf');continue
            best=min(max(abs(color[k]-colors[index][k]) for k in range(4)) for nearest,index,dist in found)
            max_color=max(max_color,best)
    size=[max(p[k] for p in bounds)-min(p[k] for p in bounds) for k in range(3)]
    result=dict(family=entry['family'],fbx=entry['fbx'],status='PASS' if len(meshes)==1 and triangles==entry['triangles'] and max_distance<1e-5 and max_color<1e-5 and missing_color==0 else 'FAIL',scope='Empty Blender scene FBX import with LINEAR vertex-color decoding; world-space geometry, triangle count and all exported colors. Unity runtime not tested here.',mesh_count=len(meshes),triangles=triangles,vertices=sum(len(o.data.vertices) for o in meshes),max_world_vertex_error_m=max_distance,max_rgba_error=max_color,missing_color_attributes=missing_color,bounds_dimensions_blender=size,imported_objects=[dict(name=o.name,matrix_world=[list(r) for r in o.matrix_world],color_attributes=[dict(name=c.name,domain=c.domain,type=c.data_type) for c in o.data.color_attributes]) for o in meshes])
    results.append(result)
    bpy.ops.wm.save_as_mainfile(filepath=str(OUT/(entry['mesh_object']+'_FBX_Readback.blend')))
(OUT/'spirit_fbx_roundtrip.json').write_text(json.dumps(results,indent=2),encoding='utf-8')
print(json.dumps(results))
