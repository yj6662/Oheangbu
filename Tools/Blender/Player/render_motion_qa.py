"""Render evaluated final poses and the measured +Y grip with a .42m brush gauge."""
import bpy,os,math,json
from mathutils import Vector
ROOT='C:/Users/yj666/Oheangbu';OUT=ROOT+'/Art/Player/MotionQA'
os.makedirs(OUT,exist_ok=True)
s=bpy.data.scenes['Dosa_Player_Workshop'];bpy.context.window.scene=s;r=bpy.data.objects['Dosa_Rig'];m=bpy.data.objects['Dosa_Body']
s.cycles.samples=16;s.render.resolution_x=600;s.render.resolution_y=800;s.render.resolution_percentage=100
s.camera.location=(2,-4,1.7);s.camera.rotation_euler=(Vector((0,0,.9))-s.camera.location).to_track_quat('-Z','Y').to_euler()
rows=[]
for name,frame in [('Idle',1),('CombatReady',20),('WalkForward',9),('WalkBack',8),('WalkLeft',16),('WalkRight',16),('RunForward',6),('RunBack',15),('RunLeft',13),('RunRight',13),('DodgeForward',7),('DodgeBack',8),('DodgeLeft',8),('DodgeRight',8),('DrawReady',10),('DrawHold',1),('DrawRelease',5)]:
    r.animation_data.action=bpy.data.actions['Dosa_'+name];r.animation_data.action_slot=r.animation_data.action.slots[0];s.frame_set(frame)
    path=OUT+'/'+name+'-final.png';s.render.filepath=path;bpy.ops.render.render(write_still=True)
    ev=m.evaluated_get(bpy.context.evaluated_depsgraph_get());mesh=ev.to_mesh();stretch=[]
    for edge in m.data.edges:
        a,b=edge.vertices;before=(m.data.vertices[a].co-m.data.vertices[b].co).length
        if before>.004:stretch.append((mesh.vertices[a].co-mesh.vertices[b].co).length/before)
    ev.to_mesh_clear();rows.append({'clip':name,'frame':frame,'image':path,'edges_stretch_over_3x':sum(v>3 for v in stretch),'max_edge_stretch':max(stretch)})
# Gauge shares exactly the runtime convention, with grip at origin and tip +Y .28.
r.animation_data.action=bpy.data.actions['Dosa_DrawHold'];r.animation_data.action_slot=r.animation_data.action.slots[0];s.frame_set(1)
old=bpy.data.objects.get('Dosa_QA_BrushGauge')
if old:bpy.data.objects.remove(old,do_unlink=True)
verts=[];faces=[];rings=[(-.14,.0055),(-.134,.008),(.17,.0085),(.19,.010),(.21,.010),(.235,.009),(.263,.0045),(.28,.0003)]
for y,rad in rings:
    for j in range(20):
        angle=j*math.tau/20;verts.append((math.cos(angle)*rad,y,math.sin(angle)*rad))
for i in range(len(rings)-1):
    for j in range(20):faces.append((i*20+j,i*20+(j+1)%20,(i+1)*20+(j+1)%20,(i+1)*20+j))
mesh=bpy.data.meshes.new('Dosa_QA_BrushGauge');mesh.from_pydata(verts,[],faces);mesh.update();o=bpy.data.objects.new('Dosa_QA_BrushGauge',mesh);s.collection.objects.link(o)
for name,color in [('QA_WeatheredWood',(.095,.055,.027,1)),('QA_InkBristles',(.018,.014,.011,1))]:
    mat=bpy.data.materials.get(name) or bpy.data.materials.new(name);mat.use_nodes=True;p=next(n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED');p.inputs['Base Color'].default_value=color;p.inputs['Roughness'].default_value=.73;mesh.materials.append(mat)
for p in mesh.polygons:p.use_smooth=True;p.material_index=int(p.index//20>=4)
o.matrix_world=r.matrix_world@r.pose.bones['RightBrushGrip'].matrix
target=o.matrix_world.translation+Vector((0,-.055,.015));s.camera.location=target+Vector((.22,-.44,.24));s.camera.rotation_euler=(target-s.camera.location).to_track_quat('-Z','Y').to_euler();s.camera.data.lens=65
s.render.resolution_x=960;s.render.resolution_y=720;s.render.filepath=OUT+'/DrawGrip-final.png';bpy.ops.render.render(write_still=True)
o.hide_render=True;o.hide_set(True)
open(ROOT+'/Art/Player/motion-visual-qa.json','w',encoding='utf-8').write(json.dumps(rows,indent=2))
print('rendered',len(rows),'states plus grip closeup')
