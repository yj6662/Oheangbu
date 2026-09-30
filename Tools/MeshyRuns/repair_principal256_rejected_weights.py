"""Retain approved clothing; remove fully occluded leg faces and reduce cross-leg skirt weights.
Original Meshy downloads remain untouched. Outputs require independent motion review.
"""
import bpy,bmesh,sys,json
from pathlib import Path
from array import array
name=sys.argv[sys.argv.index('--')+1];root=Path('C:/Users/yj666/Oheangbu/Art/Characters/Principal256')/name
out=root/'prepared';out.mkdir(exist_ok=True);report=[]
for label,src in [('Body','result_rigged_character_glb_url.glb'),('Walk','result_basic_animations_walking_glb_url.glb'),('Run','result_basic_animations_running_glb_url.glb')]:
 bpy.ops.wm.read_factory_settings(use_empty=True);bpy.ops.import_scene.gltf(filepath=str(root/'rig'/src))
 meshes=[o for o in bpy.context.scene.objects if o.type=='MESH' and len(o.vertex_groups)>0];arms=[o for o in bpy.context.scene.objects if o.type=='ARMATURE']
 for o in list(bpy.context.scene.objects):
  if o not in meshes+arms:bpy.data.objects.remove(o,do_unlink=True)
 count=removed=0
 for o in meshes:
  if name=='wangso':
   bs=next(n for n in o.data.materials[0].node_tree.nodes if n.type=='BSDF_PRINCIPLED');tex=bs.inputs['Base Color'].links[0].from_node.image
   pix=array('f',[0.0])*(tex.size[0]*tex.size[1]*4);tex.pixels.foreach_get(pix);colors={}
   for loop in o.data.loops:
    u,v=o.data.uv_layers.active.data[loop.index].uv;x=min(tex.size[0]-1,max(0,int(u*tex.size[0])));y=min(tex.size[1]-1,max(0,int(v*tex.size[1])));i=(y*tex.size[0]+x)*4;colors[loop.vertex_index]=pix[i:i+3]
   hidden=set()
   for v in o.data.vertices:
    p=o.matrix_world@v.co;r,g,b=colors[v.index]
    skin=r>.70 and g>.48 and b>.40 and r>g*1.13
    # Short side (negative X) deliberately retains the approved exposed knee/calf.
    threshold=.63 if p.x<-.025 else .23
    if skin and abs(p.x)<.26 and threshold<p.z<.97:hidden.add(v.index)
    if .20<p.z<.98 and not skin and abs(p.x)<.35:
     # Cloth does not need knee/toe articulation. Blend both thighs with pelvis
     # instead of pulling the central fabric independently onto opposite shins.
     low=max(0,min(1,(.98-p.z)/.55));leg=.30*low
     for group_index in [g.group for g in v.groups]:o.vertex_groups[group_index].remove([v.index])
     o.vertex_groups['Hips'].add([v.index],1-leg,'REPLACE')
     o.vertex_groups['LeftUpLeg'].add([v.index],leg*.5,'REPLACE');o.vertex_groups['RightUpLeg'].add([v.index],leg*.5,'REPLACE');count+=1
   bm=bmesh.new();bm.from_mesh(o.data);bm.verts.ensure_lookup_table();faces=[f for f in bm.faces if all(v.index in hidden for v in f.verts)];removed=len(faces);bmesh.ops.delete(bm,geom=faces,context='FACES');bm.to_mesh(o.data);bm.free()
  for face in o.data.polygons:face.use_smooth=True
 bpy.ops.object.select_all(action='SELECT')
 bpy.ops.export_scene.gltf(filepath=str(out/(label+'.glb')),export_format='GLB',use_selection=True,export_animations=label!='Body')
 bpy.ops.export_scene.fbx(filepath=str(out/(label+'.fbx')),use_selection=True,object_types={'ARMATURE','MESH'},add_leaf_bones=False,bake_anim=label!='Body',bake_anim_use_all_actions=False,bake_anim_use_nla_strips=False,axis_forward='-Z',axis_up='Y')
 report.append(dict(mode=label,reweighted_cloth_vertices=count,removed_occluded_leg_faces=removed))
(out/'repair.json').write_text(json.dumps(report,indent=2))
