import bpy,json,bmesh
from pathlib import Path
from mathutils import Vector
root=Path('C:/Users/yj666/Oheangbu/Art/Characters/Principal257/wangso')
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=str(root/'texture/model_urls_glb.glb'))
meshes=[o for o in bpy.context.scene.objects if o.type=='MESH']
# Split only for density allocation; head keeps a much larger share than uniform decimation.
# Modifiers operate on copies of downloaded rigs. Bone weights and animation hierarchy remain.
for o in list(meshes):
 if len(o.data.polygons)<=180000:continue
 bpy.ops.object.select_all(action='DESELECT');o.select_set(True);bpy.context.view_layer.objects.active=o
 coords=[o.matrix_world@v.co for v in o.data.vertices];zmin=min(v.z for v in coords);zmax=max(v.z for v in coords)
 cut=zmax-(zmax-zmin)*.15
 head=o.copy();head.data=o.data.copy();bpy.context.collection.objects.link(head)
 for target_obj,keep_head in [(head,True),(o,False)]:
  bm=bmesh.new();bm.from_mesh(target_obj.data)
  faces=[f for f in bm.faces if all((target_obj.matrix_world@v.co).z>cut for v in f.verts)!=keep_head]
  bmesh.ops.delete(bm,geom=faces,context='FACES')
  loose=[v for v in bm.verts if not v.link_faces]
  if loose:bmesh.ops.delete(bm,geom=loose,context='VERTS')
  bm.to_mesh(target_obj.data);bm.free()
meshes=[o for o in bpy.context.scene.objects if o.type=='MESH']
for o in meshes:
 coords=[o.matrix_world@v.co for v in o.data.vertices]
 is_head=min(v.z for v in coords)>.6
 target=60000 if is_head else 100000
 o.name='WangsoHead257' if is_head else 'WangsoBody257'
 if len(o.data.polygons)>target:
  bpy.context.view_layer.objects.active=o
  modifier=o.modifiers.new('BodyDensityHeadProtected257','DECIMATE');modifier.ratio=target/len(o.data.polygons);modifier.use_collapse_triangulate=True
  while o.modifiers.find(modifier.name)>0:bpy.ops.object.modifier_move_up(modifier=modifier.name)
  bpy.ops.object.modifier_apply(modifier=modifier.name)
# Retain the head anatomy while removing clearly invented pale accessories behind ears.
report=[]
for o in meshes:
 if 'Head' not in o.name:continue
 bm=bmesh.new();bm.from_mesh(o.data);bm.verts.ensure_lookup_table()
 uv=bm.loops.layers.uv.active
 principled=next(n for n in o.data.materials[0].node_tree.nodes if n.type=='BSDF_PRINCIPLED')
 image=principled.inputs['Base Color'].links[0].from_node.image
 pixels=list(image.pixels[:]);w,h=image.size
 dark=bpy.data.materials.new('HairTieDark');dark.use_nodes=True;dark.node_tree.nodes.clear()
 pn=dark.node_tree.nodes.new('ShaderNodeBsdfPrincipled');on=dark.node_tree.nodes.new('ShaderNodeOutputMaterial');dark.node_tree.links.new(pn.outputs[0],on.inputs['Surface']);pn.inputs['Base Color'].default_value=(.018,.016,.014,1);pn.inputs['Roughness'].default_value=.85
 o.data.materials.append(dark);dark_index=len(o.data.materials)-1
 doomed=[]
 for f in bm.faces:
  co=o.matrix_world@f.calc_center_median()
  if not ((.04<abs(co.x)<.103 and -.017<co.y<.079 and .716<co.z<.817) or (abs(co.x)<.09 and .80<co.z<.96 and co.y>.07)):continue
  st=sum((l[uv].uv for l in f.loops),Vector((0,0)))/len(f.loops)
  idx=4*(max(0,min(h-1,int(st.y*h)))*w+max(0,min(w-1,int(st.x*w))))
  rgb=pixels[idx:idx+3]
  if min(rgb)>.62 and max(rgb)-min(rgb)<.10:f.material_index=dark_index
 bmesh.ops.delete(bm,geom=doomed,context='FACES');bm.to_mesh(o.data);bm.free()
 report.append({'object':o.name,'removed_faces':len(doomed),'dark_hair_material_faces':sum(p.material_index==dark_index for p in o.data.polygons)})
for o in meshes:
 for p in o.data.polygons:p.use_smooth=True
 for e in o.data.edges:e.use_edge_sharp=False
 bpy.context.view_layer.objects.active=o
 if o.data.has_custom_normals:bpy.ops.mesh.customdata_custom_splitnormals_clear()
# Single mesh for rigging, UV islands and material references retained.
bpy.ops.object.select_all(action='DESELECT')
for o in meshes:o.select_set(True)
bpy.context.view_layer.objects.active=meshes[0];bpy.ops.object.join()
out=root/'rig_input';out.mkdir(exist_ok=True)
bpy.ops.export_scene.gltf(filepath=str(out/'Wangso.glb'),export_format='GLB',use_selection=True,export_animations=False)
report.append({'triangles':len(bpy.context.object.data.polygons),'vertices':len(bpy.context.object.data.vertices)})
(out/'preparation.json').write_text(json.dumps(report,indent=2))
