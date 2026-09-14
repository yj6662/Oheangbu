"""Local final pass: buried rounded wrist overlap and clean source-skin UV bake."""
import bpy,bmesh,json,math
from mathutils import Vector
ROOT='C:/Users/yj666/Oheangbu';s=bpy.data.scenes['Dosa_Player_Workshop'];bpy.context.window.scene=s
m=bpy.data.objects['Dosa_Body'];r=bpy.data.objects['Dosa_Rig'];spec=json.load(open(ROOT+'/Art/Player/refinement-report.json'))['hands']
handverts={i for p in m.data.polygons if p.material_index==1 for i in p.vertices};changed=0
if not m.get('rounded_wrist_overlap'):
    for i in handverts:
        v=m.data.vertices[i];side='Right' if v.co.x<0 else 'Left';hs=spec[side];origin=Vector(hs['wrist']);d=Vector(hs['length_axis']);rel=v.co-origin;t=rel.dot(d)
        if t<.035:
            factor=max(0,min(1,(.025-t)/.043));radial=rel-d*t
            v.co=origin+d*(t-.040*factor)+radial*(1-.12*factor)
            forearm=0
            for g in m.vertex_groups:g.remove([i])
            m.vertex_groups[side+'ForeArm'].add([i],forearm,'REPLACE');m.vertex_groups[side+'Hand'].add([i],1-forearm,'REPLACE');changed+=1
    m['rounded_wrist_overlap']=True
# Bake only a temporary hands copy so the body UV/material is untouched.
tmp=m.copy();tmp.data=m.data.copy();tmp.name='Dosa_HandBakeTemporary';s.collection.objects.link(tmp)
for mod in list(tmp.modifiers):tmp.modifiers.remove(mod)
bm=bmesh.new();bm.from_mesh(tmp.data);bm.verts.ensure_lookup_table();bmesh.ops.delete(bm,geom=[v for v in bm.verts if v.index not in handverts],context='VERTS');bm.to_mesh(tmp.data);bm.free()
tmp.data.materials.clear();mat=bpy.data.materials.new('Dosa_CleanSkinBakeTemporary');mat.use_nodes=True;tmp.data.materials.append(mat)
for p in tmp.data.polygons:p.material_index=0
uv=tmp.data.uv_layers.new(name='SourceSkinPatchUV')
for p in tmp.data.polygons:
    for li in p.loop_indices:
        v=tmp.data.vertices[tmp.data.loops[li].vertex_index];side='Right' if v.co.x<0 else 'Left';hs=spec[side];rel=v.co-Vector(hs['wrist'])
        # Clean skin patch: local 10px neighbourhood mean sRGB .428,.357,.301;
        # avoids the ink-black source island entering the previous broad sample.
        uv.data[li].uv=(.143846+rel.dot(Vector(hs['width_axis']))*.012,.721154+rel.dot(Vector(hs['length_axis']))*.010)
tmp.data.uv_layers.active=tmp.data.uv_layers['uv'];tmp.data.uv_layers['uv'].active_render=True
nodes=mat.node_tree.nodes;nodes.clear();out=nodes.new('ShaderNodeOutputMaterial');emit=nodes.new('ShaderNodeEmission');tex=nodes.new('ShaderNodeTexImage');tex.image=bpy.data.images['T_DosaCourier_base_color.png'];uvnode=nodes.new('ShaderNodeUVMap');uvnode.uv_map='SourceSkinPatchUV'
mat.node_tree.links.new(uvnode.outputs['UV'],tex.inputs['Vector']);mat.node_tree.links.new(tex.outputs['Color'],emit.inputs['Color']);mat.node_tree.links.new(emit.outputs[0],out.inputs['Surface'])
old=bpy.data.images.get('T_DosaCourier_HandsBaseColor');target=bpy.data.images.new('Dosa_HandCleanBake',width=1024,height=1024,alpha=True);dest=nodes.new('ShaderNodeTexImage');dest.image=target;nodes.active=dest
bpy.ops.object.select_all(action='DESELECT');tmp.select_set(True);bpy.context.view_layer.objects.active=tmp;s.render.engine='CYCLES';s.cycles.samples=1;s.render.bake.margin=8;bpy.ops.object.bake(type='EMIT')
path=ROOT+'/Art/Player/T_DosaCourier_HandsBaseColor.png';target.filepath_raw=path;target.file_format='PNG';target.save()
handmat=m.data.materials[1]
for node in handmat.node_tree.nodes:
    if node.type=='TEX_IMAGE':node.image=target
target.name='T_DosaCourier_HandsBaseColor_Clean'
bpy.data.objects.remove(tmp,do_unlink=True);bpy.data.materials.remove(mat)
open(ROOT+'/Art/Player/wrist-skin-refinement.json','w',encoding='utf-8').write(json.dumps({'wrist_vertices_changed':changed,'wrist_overlap_m':.040,'source_patch_uv':[.143846,.721154],'body_texture_unchanged':True},indent=2))
print('wrist vertices',changed,'clean skin baked',path)
