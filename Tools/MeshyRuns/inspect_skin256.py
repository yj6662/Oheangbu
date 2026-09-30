import bpy,json
from pathlib import Path
p=Path('C:/Users/yj666/Oheangbu/Art/Characters/Principal256/wangso')
bpy.ops.wm.read_factory_settings(use_empty=True);bpy.ops.import_scene.gltf(filepath=str(p/'rig/result_rigged_character_glb_url.glb'))
r=[]
for o in bpy.context.scene.objects:
 if o.type=='ARMATURE':r.append({'armature':o.name,'bones':[{ 'name':b.name,'head':list(o.matrix_world@b.head_local),'tail':list(o.matrix_world@b.tail_local)} for b in o.data.bones]})
 if o.type=='MESH':r.append({'mesh':o.name,'location':list(o.location),'scale':list(o.scale),'bounds':[list(o.matrix_world@__import__('mathutils').Vector(b)) for b in o.bound_box],'groups':[g.name for g in o.vertex_groups],'images':[(n.name,n.image.name if n.image else None) for m in o.data.materials for n in m.node_tree.nodes if n.type=='TEX_IMAGE']})
(p/'skin-structure.json').write_text(json.dumps(r,indent=2))
