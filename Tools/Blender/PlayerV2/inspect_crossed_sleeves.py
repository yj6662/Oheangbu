"""Additional direct-pose shoulder inspection; no model edits or production Action."""
import bpy,json,math,hashlib
from pathlib import Path
from mathutils import Matrix,Vector,Euler
ROOT=Path(__file__).resolve().parents[3];ART=ROOT/'Art/PlayerV2';OUT=ART/'Inspect/CrossedSleeves';OUT.mkdir(parents=True,exist_ok=True)
source=ART/'DosaV2_Assembled.blend';bpy.ops.wm.open_mainfile(filepath=str(source))
s=bpy.context.scene;rig=bpy.data.objects['DosaV2_Rig']
pose=next(p for p in json.loads((ART/'Validation/static-pose-definitions.json').read_text())['poses'] if p['id']=='combined_reach')
for p in rig.pose.bones:p.matrix_basis=Matrix.Identity(4)
for item in pose['boneRotations']:rig.pose.bones[item['bone']].matrix_basis=Euler([math.radians(item[k]) for k in ('x','y','z')],'XYZ').to_matrix().to_4x4()
hands=bpy.data.objects['DosaV2_Hands']
for key in list(hands.data.shape_keys.key_blocks)[1:]:key.value=1
meshes=[o for o in s.objects if o.type=='MESH' and o.name.startswith(('DosaV2_','DosaPackV2_')) and o.name!='DosaV2_SourceSurface']
for o in s.objects:
    if o.type=='MESH':o.hide_render=o not in meshes
    if o.type in ('LIGHT','CAMERA'):o.hide_render=True
bpy.context.view_layer.update()
s.render.engine='CYCLES';s.cycles.samples=16;s.cycles.use_denoising=True
s.render.resolution_x=1200;s.render.resolution_y=1200;s.render.resolution_percentage=100;s.render.image_settings.file_format='PNG'
for n,loc,power in [('Key',(-2,-3,3),300),('Fill',(2,-1,2),180)]:
    d=bpy.data.lights.new(n,'AREA');d.energy=power;d.size=3
    o=bpy.data.objects.new(n,d);s.collection.objects.link(o);o.location=loc;o.rotation_euler=(Vector((0,0,1.25))-o.location).to_track_quat('-Z','Y').to_euler()
c=bpy.data.objects.new('SleeveInspectionCamera',bpy.data.cameras.new('SleeveInspectionCamera'));s.collection.objects.link(c);s.camera=c;c.data.type='ORTHO';c.data.ortho_scale=.92
renders=[]
def capture(name,location):
    c.location=location;c.rotation_euler=(Vector((0,-.025,1.315))-c.location).to_track_quat('-Z','Y').to_euler()
    path=OUT/(name+'.png');s.render.filepath=str(path);bpy.ops.render.render(write_still=True)
    renders.append({'name':name,'path':str(path),'sha256':hashlib.sha256(path.read_bytes()).hexdigest()})
for n,loc in [('front_left',(2,-3,1.42)),('front_right',(-2,-3,1.42)),('below',(0,-3,.45))]:capture(n,loc)
colors={'DosaV2_SleeveOuter_L':(.1,.55,.8,1),'DosaV2_SleeveOuter_R':(.8,.18,.06,1),'DosaV2_BodyCore':(.3,.3,.3,1)}
for n,color in colors.items():
    o=bpy.data.objects[n];m=bpy.data.materials.new('Inspect_'+n);m.diffuse_color=color;m.use_nodes=True;m.node_tree.nodes.get('Principled BSDF').inputs['Base Color'].default_value=color
    for i in range(len(o.data.materials)):o.data.materials[i]=m
capture('part_colors',(0,-3,.8))
weights={}
for n in colors:
    o=bpy.data.objects[n];totals={}
    for v in o.data.vertices:
        for g in v.groups:
            name=o.vertex_groups[g.group].name;totals[name]=totals.get(name,0)+g.weight
    weights[n]=totals
(OUT/'report.json').write_text(json.dumps({'sourceSha256':hashlib.sha256(source.read_bytes()).hexdigest(),'renders':renders,'boneWeightTotals':weights,'actions':len(bpy.data.actions),'status':'DIAGNOSTIC_PENDING_VISUAL'},indent=2))
