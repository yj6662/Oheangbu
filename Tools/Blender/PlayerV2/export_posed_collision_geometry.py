"""Read-only evaluated anatomy/cloth geometry for offline capsule approximation."""
import bpy, json, hashlib, importlib.util, math
from pathlib import Path
from mathutils import Matrix, Euler

ROOT = Path(__file__).resolve().parents[3]
SOURCE = ROOT / 'Art/PlayerV2/Inspect/ClothBlender/Inputs/Assembled-final-9f4cd412.blend'
OUT = ROOT / 'Art/PlayerV2/Inspect/ClothBlender/PosedArmFit9f4cd412'
OUT.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(SOURCE))
rig = bpy.data.objects['DosaV2_Rig']
for obj in bpy.context.scene.objects:
    if obj.name.startswith('CTRL_') and 'AuthoringMode' in obj:
        obj['AuthoringMode'] = False
        obj.update_tag()
spec = importlib.util.spec_from_file_location('fixture', Path(__file__).with_name('diagnose_cloth_blender.py'))
fixture = importlib.util.module_from_spec(spec)
spec.loader.exec_module(fixture)
definitions = json.loads((ROOT / 'Art/PlayerV2/Validation/static-pose-definitions.json').read_text())
names = sorted(o.name for o in bpy.context.scene.objects if o.type == 'MESH' and o.name.startswith((
    'DosaV2_ArmLining_', 'DosaV2_BodyLining', 'DosaV2_LegLining_',
    'DosaV2_Robe_', 'DosaV2_SleeveOuter_')))
topology = {}
for name in names:
    obj = bpy.data.objects[name]
    obj.data.calc_loop_triangles()
    mobility = obj.data.color_attributes.get('ClothMobility')
    topology[name] = {
        'restVertices': [list(obj.matrix_world @ v.co) for v in obj.data.vertices],
        'triangles': [list(t.vertices) for t in obj.data.loop_triangles],
        'mobilityMeters': [float(c.color[0]) for c in mobility.data] if mobility else None,
        'deformWeights': [{obj.vertex_groups[g.group].name: g.weight for g in v.groups if g.weight > 1e-8}
                          for v in obj.data.vertices],
    }
metadata = {'source': str(SOURCE), 'sourceSha256': hashlib.sha256(SOURCE.read_bytes()).hexdigest(),
            'space': 'Blender world metres; Z up; character forward -Y',
            'actualNativeClothApplied': False, 'productionActions': 0, 'topology': topology,
            'boneRestMatrices': {b.name: [list(r) for r in b.matrix_local] for b in rig.data.bones}}
(OUT / 'topology-rest.json').write_text(json.dumps(metadata, separators=(',', ':')), encoding='utf-8')
poses = [('fixture_' + case, None, case) for case in ('rest_settle', 'grip_settle', 'raised_arms_settle')]
poses += [(d['id'], d, None) for d in definitions['poses']]
for name, definition, case in poses:
    for bone in rig.pose.bones:
        bone.matrix_basis = Matrix.Identity(4)
    if case:
        fixture.direct_pose(rig, case)
    else:
        for row in definition['boneRotations']:
            rig.pose.bones[row['bone']].matrix_basis = Euler(
                [math.radians(row[c]) for c in ('x', 'y', 'z')], 'XYZ').to_matrix().to_4x4()
    bpy.context.view_layer.update()
    geometry = {}
    for meshname in names:
        obj = bpy.data.objects[meshname]
        evaluated = obj.evaluated_get(bpy.context.evaluated_depsgraph_get())
        mesh = evaluated.to_mesh()
        if len(mesh.vertices) != len(obj.data.vertices):
            raise RuntimeError('Topology changed during static skinning: ' + meshname)
        geometry[meshname] = [list(evaluated.matrix_world @ v.co) for v in mesh.vertices]
        evaluated.to_mesh_clear()
    report = {'poseId': name, 'sourceSha256': metadata['sourceSha256'], 'vertices': geometry,
              'bonePoseMatrices': {b.name: [list(r) for r in b.matrix] for b in rig.pose.bones}}
    (OUT / (name + '.json')).write_text(json.dumps(report, separators=(',', ':')), encoding='utf-8')
    print('EXPORTED ' + name, flush=True)
print('READ_ONLY_EXPORT_COMPLETE ' + str(OUT), flush=True)
