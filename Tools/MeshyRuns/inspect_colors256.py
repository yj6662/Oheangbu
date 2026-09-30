import bpy,json
from pathlib import Path
from array import array
p=Path('C:/Users/yj666/Oheangbu/Art/Characters/Principal256/wangso')
bpy.ops.wm.read_factory_settings(use_empty=True);bpy.ops.import_scene.gltf(filepath=str(p/'rig/result_rigged_character_glb_url.glb'))
o=next(o for o in bpy.context.scene.objects if o.type=='MESH' and o.vertex_groups)
mat=o.data.materials[0];bs=next(n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED');tex=bs.inputs['Base Color'].links[0].from_node.image;pix=array('f',[0.0])*(tex.size[0]*tex.size[1]*4);tex.pixels.foreach_get(pix)
colors={}
for loop in o.data.loops:
 u,v=o.data.uv_layers.active.data[loop.index].uv;x=min(tex.size[0]-1,max(0,int(u*tex.size[0])));y=min(tex.size[1]-1,max(0,int(v*tex.size[1])));i=(y*tex.size[0]+x)*4;colors[loop.vertex_index]=list(pix[i:i+3])
rows=[]
for v in o.data.vertices:
 pos=o.matrix_world@v.co
 if abs(pos.x)<.15 and pos.y<-.08 and .2<pos.z<1.:
  rows.append({'p':[round(x,3) for x in pos],'c':[round(x,3) for x in colors[v.index]],'w':[(o.vertex_groups[g.group].name,round(g.weight,2)) for g in v.groups]})
(p/'skin-color-samples.json').write_text(json.dumps(rows[::max(1,len(rows)//24)],indent=2))
