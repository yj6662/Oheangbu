import bpy,bmesh
from mathutils import Vector
from mathutils.kdtree import KDTree
robe=bpy.data.objects['Durumagi'];src=bpy.data.objects['Durumagi_TextureSource']
before=set(bpy.data.objects);bpy.ops.import_scene.gltf(filepath='C:/Users/yj666/Oheangbu/Art/PlayerPhase1/Source/Meshy/DurumagiRetry/glb.glb')
raw=next(o for o in set(bpy.data.objects)-before if o.type=='MESH')
bm=bmesh.new();bm.from_mesh(raw.data);bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=.00001);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(raw.data);bm.free()
assert len(raw.data.vertices)==len(robe.data.vertices),(len(raw.data.vertices),len(robe.data.vertices))
kd=KDTree(len(raw.data.vertices))
for v in raw.data.vertices:kd.insert(v.co,v.index)
kd.balance()
positions=[v.co.copy() for v in robe.data.vertices];weights=[[(g.group,g.weight) for g in v.groups] for v in robe.data.vertices]
mesh=src.data.copy();mapping=[];err=0
lo=Vector(tuple(min(v.co[i] for v in raw.data.vertices) for i in range(3)));hi=Vector(tuple(max(v.co[i] for v in raw.data.vertices) for i in range(3)))
center=(lo+hi)/2;scale=(hi.z-lo.z)/2
for v in mesh.vertices:
 co,idx,d=kd.find(v.co*scale+center);mapping.append(idx);err=max(err,d);v.co=positions[idx]
assert err<.0001,err
group_names=[g.name for g in robe.vertex_groups]
robe.data=mesh
if not robe.vertex_groups:
 for name in group_names:robe.vertex_groups.new(name=name)
for v,idx in zip(mesh.vertices,mapping):
 for g,w in weights[idx]:robe.vertex_groups[g].add([v.index],w,'REPLACE')
for p in mesh.polygons:p.use_smooth=True
if mesh.has_custom_normals:mesh.normals_split_custom_set([(0,0,0)]*len(mesh.loops))
for m in mesh.materials:
 bs=next(n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
 for l in list(bs.inputs['Metallic'].links):m.node_tree.links.remove(l)
 bs.inputs['Metallic'].default_value=0
bpy.data.objects.remove(raw,do_unlink=True)
for o in bpy.context.scene.objects:
 if o.type=='MESH' and o.name not in ['Body','InnerTop','Durumagi']:o.hide_render=True
bpy.context.scene.render.filepath='C:/Users/yj666/Oheangbu/Art/PlayerPhase1/Previews/Assembly_textured.png';bpy.ops.render.render(write_still=True)
print('UV repaired; source vertex correspondence maximum error:',err)
