import bpy,json,math
from pathlib import Path
from mathutils import Vector
OUT=Path('C:/Users/yj666/Oheangbu/Art/PlayerPhase1');(OUT/'Textures').mkdir(exist_ok=True)
s=bpy.context.scene;a=bpy.data.objects['Dosa_Phase1_Rig'];parts=[bpy.data.objects[n] for n in ['Body','InnerTop','Durumagi']]
a.animation_data.action=None
for p in a.pose.bones:p.rotation_quaternion=(1,0,0,0);p.location=(0,0,0);p.scale=(1,1,1)
rows=[]
for o in parts:
 o.hide_render=False;o.hide_set(False)
 for v in o.data.vertices:
  weights=sorted([(g.group,g.weight) for g in v.groups if math.isfinite(g.weight) and g.weight>1e-8],key=lambda p:-p[1])[:4]
  if not weights:weights=[(o.vertex_groups.get('Hips').index,1)]
  total=sum(w for g,w in weights)
  for g in list(v.groups):o.vertex_groups[g.group].remove([v.index])
  for g,w in weights:o.vertex_groups[g].add([v.index],w/total,'REPLACE')
 o.data.calc_loop_triangles()
 rows.append({'part':o.name,'vertices':len(o.data.vertices),'faces':len(o.data.polygons),'triangles':len(o.data.loop_triangles),'materials':len(o.data.materials),'max_weights':max(len(v.groups) for v in o.data.vertices),'max_weight_sum_error':max(abs(sum(g.weight for g in v.groups)-1) for v in o.data.vertices),'unweighted_vertices':sum(not v.groups for v in o.data.vertices)})
 for m in o.data.materials:
  m.name='M_Phase1_'+o.name
  bs=next(n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
  for l in list(bs.inputs['Metallic'].links):m.node_tree.links.remove(l)
  bs.inputs['Metallic'].default_value=0
  for i,n in enumerate(n for n in m.node_tree.nodes if n.type=='TEX_IMAGE' and n.image):
   im=n.image;im.filepath_raw=str(OUT/'Textures'/('T_'+o.name+'_'+str(i)+'.png'));im.file_format='PNG';im.save();im.pack()
stats={'requested_meshy_faces':{'Body':13000,'InnerTop':5000,'Durumagi_initial_quad':7000,'Durumagi_selected_triangle':14000},'blender_meshes':rows,'total_triangles':sum(r['triangles'] for r in rows),'limit':60000,'target':50000,'bones':len(a.data.bones),'full_character_80000_budget_remaining':80000-sum(r['triangles'] for r in rows)}
(OUT/'mesh_stats.json').write_text(json.dumps(stats,indent=2))
# The new edit file contains only the new character, studio, and diagnostic actions.
keep=set(parts+[a,s.camera]+[o for o in s.objects if o.type=='LIGHT'])
for o in list(s.objects):
 if o not in keep:
  for c in list(o.users_collection):
   if c in list(s.collection.children_recursive)+[s.collection]:c.objects.unlink(o)
c=s.camera;c.location=(2.4,-4.5,2.1);c.rotation_euler=(Vector((0,0,1.05))-c.location).to_track_quat('-Z','Y').to_euler();c.data.ortho_scale=4.15
s.frame_set(1);s.render.filepath=str(OUT/'Previews/Assembly_final.png');bpy.ops.render.render(write_still=True)
actions={bpy.data.actions[n] for n in ['DIAG_Walk','DIAG_Run','DIAG_ArmsUp','DIAG_Squat','DIAG_BrushSwing']}
bpy.data.libraries.write(str(OUT/'Dosa_Phase1.blend'),{s}|actions,fake_user=True,compress=True)
print(json.dumps(stats))
