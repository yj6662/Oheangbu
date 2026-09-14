"""Compare actual original/derived anatomy, without altering the saved model."""
import bpy,json,math,ast,importlib.util
from pathlib import Path
from mathutils import Matrix,Euler
ROOT=Path(__file__).resolve().parents[3];OUT=ROOT/'Art/PlayerV2/Inspect/ClothBlender/FoldedGusset/ShoulderCapComparison';OUT.mkdir(parents=True,exist_ok=True)
SOURCE=OUT.parent/'DosaV2_FoldedGusset.blend'
def load(name,file):
    s=importlib.util.spec_from_file_location(name,Path(__file__).with_name(file));m=importlib.util.module_from_spec(s);s.loader.exec_module(m);return m
helper=load('render_helper','build_brush.py');bpy.ops.wm.open_mainfile(filepath=str(SOURCE));scene=bpy.context.scene;rig=bpy.data.objects['DosaV2_Rig']
target=bpy.data.objects['DosaV2_ArmLining_Left'];body=bpy.data.objects['DosaV2_BodyLining']
for obj in scene.objects:
    if obj.type=='MESH':obj.hide_render=obj not in [target,body]
    if obj.name.startswith('CTRL_') and 'AuthoringMode' in obj:obj['AuthoringMode']=False;obj.update_tag()
def material(name,color):
    m=bpy.data.materials.new(name);m.use_nodes=True;node=m.node_tree.nodes.get('Principled BSDF');node.inputs['Base Color'].default_value=color;node.inputs['Roughness'].default_value=.7;return m
target.data.materials.clear();target.data.materials.append(material('Diagnostic shoulder blue',(.08,.38,.52,1)))
body.data.materials.clear();body.data.materials.append(material('Diagnostic torso grey',(.36,.36,.36,1)))
wire=target.copy();wire.name='DiagnosticShoulderWire';scene.collection.objects.link(wire);wire.data=target.data.copy();wire.data.materials.clear();wire.data.materials.append(material('Diagnostic wire',(.015,.02,.025,1)))
modifier=wire.modifiers.new('DiagnosticTopology','WIREFRAME');modifier.thickness=.00035;modifier.use_replace=True
after_points=[v.co.copy() for v in target.data.vertices];after_weights=[{target.vertex_groups[g.group].name:g.weight for g in v.groups} for v in target.data.vertices]
original=json.loads((ROOT/'Art/PlayerV2/Inspect/ClothBlender/PosedArmFit9f4cd412/topology-rest.json').read_text())['topology'][target.name]
definitions=json.loads((ROOT/'Art/PlayerV2/Validation/static-pose-definitions.json').read_text())['poses'];camera=scene.camera or helper.lighting(scene)
def weights(obj,rows):
    for group in obj.vertex_groups:group.remove(list(range(len(obj.data.vertices))))
    for i,row in enumerate(rows):
        for name,w in row.items():
            group=obj.vertex_groups.get(name) or obj.vertex_groups.new(name=name);group.add([i],w,'REPLACE')
for pose in ['rest','combined_reach']:
    for p in rig.pose.bones:p.matrix_basis=Matrix.Identity(4)
    definition=next(d for d in definitions if d['id']==pose)
    for r in definition['boneRotations']:rig.pose.bones[r['bone']].matrix_basis=Euler([math.radians(r[c]) for c in ('x','y','z')],'XYZ').to_matrix().to_4x4()
    for state,points,rows in [('before',original['restVertices'],original['deformWeights']),('after',after_points,after_weights)]:
        for obj in [target,wire]:
            for vertex,p in zip(obj.data.vertices,points):vertex.co=p
            weights(obj,rows);obj.data.update()
        bpy.context.view_layer.update()
        helper.render(scene,camera,OUT/(state+'-'+pose+'.png'),(.12,0,1.34),.65,math.pi*.08,width=1000,height=760)
        print(state+' '+pose,flush=True)
print('COMPARISON_RENDERED_NO_MODEL_SAVE',flush=True)
