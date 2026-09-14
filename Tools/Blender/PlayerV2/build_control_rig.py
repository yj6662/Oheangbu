"""Functional Blender-only controls; no production animation or Unity asset writes."""
import hashlib
import importlib.util
import json
import math
from pathlib import Path

import bpy
import numpy as np
from mathutils import Matrix, Quaternion, Vector

ROOT = Path(__file__).resolve().parents[3]
ART = ROOT / 'Art/PlayerV2'
SOURCE = ART / 'DosaV2_PackIntegrated.blend'
DESTINATION = ART / 'DosaV2_Controls.blend'
OUT = ART / 'Inspect/ControlRig'
PREFIX = 'CTRL_DosaV2_'


def digest(path): return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def load_helpers():
    spec = importlib.util.spec_from_file_location('pack_assembly_helpers', Path(__file__).with_name('assemble_backpack.py'))
    module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
    return module


def update():
    bpy.context.view_layer.update()
    # Newly authored custom-property driver relations require a time evaluation
    # in a background session as well as the ordinary transform depsgraph tag.
    bpy.context.scene.frame_set(bpy.context.scene.frame_current)


def build():
    OUT.mkdir(parents=True, exist_ok=True)
    source_hash = digest(SOURCE)
    bpy.ops.wm.open_mainfile(filepath=str(SOURCE))
    rig = bpy.data.objects['DosaV2_Rig']; helpers = load_helpers()
    if bpy.data.actions: raise RuntimeError('The control branch requires a static source without actions.')
    if any(b.constraints for b in rig.pose.bones): raise RuntimeError('Existing constraints need explicit merging.')
    original_bones = {b.name: b.matrix_local.copy() for b in rig.data.bones}
    original_rotation_modes = {p.name: p.rotation_mode for p in rig.pose.bones}
    meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    signatures = {o.name: helpers.body_signature(o) for o in meshes}
    original_points = {o.name: helpers.evaluated_points(o) for o in meshes if not o.hide_render}
    collection = bpy.data.collections.new('DosaV2_Controls_DO_NOT_EXPORT'); bpy.context.scene.collection.children.link(collection)
    collection['export'] = False
    controls = {}; rest_matrices = {}; constraints = []; chain_records = []; finger_helpers = []

    def empty(name, matrix, parent=None, display='CIRCLE', size=.08, hidden=False):
        obj = bpy.data.objects.new(PREFIX + name, None); collection.objects.link(obj)
        obj.empty_display_type = display; obj.empty_display_size = size; obj.show_in_front = True; obj.hide_render = True
        obj.parent = parent; obj.matrix_world = matrix; obj.rotation_mode = 'XYZ'
        obj.lock_scale = (True, True, True); obj['DosaControlHelper'] = True
        obj['Export'] = False
        if hidden: obj.hide_set(True); obj.hide_select = True
        update(); controls[name] = obj; rest_matrices[name] = obj.matrix_basis.copy()
        return obj

    def world_bone(name): return rig.matrix_world @ rig.data.bones[name].matrix_local

    root = empty('Root', world_bone('Root'), display='CIRCLE', size=.45)
    root['AuthoringMode'] = False
    root.id_properties_ui('AuthoringMode').update(description='Enable Blender controls. OFF preserves direct deform-bone posing and static export.')
    root['Usage'] = 'Enable AuthoringMode; move hand/foot cubes, pole spheres, hips and root. Rotate chest. Reset text restores defaults.'
    hips = empty('Hips', world_bone('Hips'), root, size=.20)
    chest = empty('Chest', world_bone('Spine02'), hips, size=.23); chest.lock_location = (True, True, True)

    def influence(constraint):
        driver = constraint.driver_add('influence').driver; driver.type = 'AVERAGE'
        variable = driver.variables.new(); variable.name = 'mode'; variable.type = 'SINGLE_PROP'
        variable.targets[0].id = root; variable.targets[0].data_path = '["AuthoringMode"]'
        constraint.influence = 0; constraints.append(constraint)

    def copy_constraint(bone_name, target, kind='COPY_ROTATION'):
        constraint = rig.pose.bones[bone_name].constraints.new(kind)
        constraint.name = 'DosaControl_' + target.name.removeprefix(PREFIX)
        constraint.target = target; constraint.owner_space = 'WORLD'; constraint.target_space = 'WORLD'
        if kind == 'COPY_ROTATION': constraint.mix_mode = 'REPLACE'
        influence(constraint); return constraint

    copy_constraint('Root', root, 'COPY_TRANSFORMS')
    copy_constraint('Hips', hips, 'COPY_TRANSFORMS')
    copy_constraint('Spine02', chest)

    for side in ['Left', 'Right']:
        for limb, upper_name, lower_name, endpoint_name in [
                ('Hand', side + 'Arm', side + 'ForeArm', side + 'Hand'),
                ('Foot', side + 'UpLeg', side + 'Leg', side + 'Foot')]:
            target = empty(side + limb + '_IK', world_bone(endpoint_name), root, 'CUBE', .06 if limb == 'Hand' else .09)
            a = rig.matrix_world @ rig.data.bones[upper_name].head_local
            b = rig.matrix_world @ rig.data.bones[lower_name].head_local
            c = rig.matrix_world @ rig.data.bones[lower_name].tail_local
            axis = (c - a).normalized(); bend = b - (a + axis * (b - a).dot(axis))
            if bend.length < .00001: bend = Vector((0, -1 if limb == 'Foot' else 0, -1 if limb == 'Hand' else 0))
            pole_matrix = Matrix.Translation(b + bend.normalized() * (.36 if limb == 'Hand' else .45))
            pole = empty(side + ('Elbow' if limb == 'Hand' else 'Knee') + '_Pole', pole_matrix, root, 'SPHERE', .035)
            pole.lock_rotation = (True, True, True)
            constraint = rig.pose.bones[lower_name].constraints.new('IK'); constraint.name = 'DosaControl_' + side + limb + '_IK'
            constraint.target = target; constraint.pole_target = pole; constraint.chain_count = 2
            constraint.use_tail = True; constraint.use_stretch = False; constraint.iterations = 256
            rig.pose.bones[upper_name].ik_stretch = 0; rig.pose.bones[lower_name].ik_stretch = 0
            influence(constraint); copy_constraint(endpoint_name, target)
            chain_records.append({'side': side, 'limb': limb, 'upper': upper_name, 'lower': lower_name,
                                  'endpoint': endpoint_name, 'target': target, 'pole': pole, 'constraint': constraint,
                                  'rest_joint': b, 'lengths': [rig.data.bones[n].length for n in [upper_name, lower_name]]})

    # Finger helpers duplicate rest orientation only; constraints are disabled in
    # direct-pose mode, so driver channels never overwrite the deform FK channels.
    for side in ['Left', 'Right']:
        hand_control = controls[side + 'Hand_IK']
        for finger in ['Thumb', 'Index', 'Middle', 'Ring', 'Pinky']:
            prop = 'Curl' + finger; hand_control[prop] = 0.0
            hand_control.id_properties_ui(prop).update(min=0, max=1, soft_min=0, soft_max=1,
                                                      description='Blender authoring curl; 0 open, 1 folded. Not the runtime grip calibration.')
            parent = hand_control
            for index, degrees in enumerate([40, 55, 35] if finger == 'Thumb' else [60, 80, 55], 1):
                bone_name = f'{side}Hand{finger}{index}'
                if bone_name not in rig.data.bones: raise RuntimeError('Missing finger bone: ' + bone_name)
                helper = empty('Finger_' + bone_name, world_bone(bone_name), parent, 'PLAIN_AXES', .012, hidden=True)
                # Keep the authored rest matrix in an unanimated parent offset;
                # the control's local X Euler then applies only the curl delta.
                rest = helper.matrix_basis.copy(); helper.matrix_parent_inverse = rest
                helper.matrix_basis = Matrix.Identity(4); rest_matrices['Finger_' + bone_name] = Matrix.Identity(4)
                driver = helper.driver_add('rotation_euler', 0).driver; driver.type = 'SCRIPTED'; driver.expression = f'-curl*{math.radians(degrees):.12f}'
                variable = driver.variables.new(); variable.name = 'curl'; variable.type = 'SINGLE_PROP'
                variable.targets[0].id = hand_control; variable.targets[0].data_path = '["' + prop + '"]'
                copy_constraint(bone_name, helper); parent = helper; finger_helpers.append(helper)
    update()
    for obj in [rig] + finger_helpers:
        if obj.animation_data:
            for curve in obj.animation_data.drivers:
                curve.driver.expression = curve.driver.expression
        obj.update_tag()
    update()
    # Verify the serialized definition that a user actually opens. Reopening
    # also rebuilds Blender's driver relations after incremental constraint
    # creation; tagging alone left stale invalid drivers in the background.
    definition = OUT / 'control-definition.blend'
    control_names = {name: obj.name for name, obj in controls.items()}
    mesh_names = [obj.name for obj in meshes]
    for chain in chain_records:
        chain['target_name'] = chain['target'].name; chain['pole_name'] = chain['pole'].name
        chain['constraint_name'] = chain['constraint'].name
    bpy.ops.wm.save_as_mainfile(filepath=str(definition))
    bpy.ops.wm.open_mainfile(filepath=str(definition))
    rig = bpy.data.objects['DosaV2_Rig']; controls = {name: bpy.data.objects[obj_name] for name, obj_name in control_names.items()}
    root = controls['Root']; meshes = [bpy.data.objects[name] for name in mesh_names]
    collection = bpy.data.collections['DosaV2_Controls_DO_NOT_EXPORT']
    constraints = [c for bone in rig.pose.bones for c in bone.constraints if c.name.startswith('DosaControl_')]
    for chain in chain_records:
        chain['target'] = bpy.data.objects[chain['target_name']]; chain['pole'] = bpy.data.objects[chain['pole_name']]
        chain['constraint'] = rig.pose.bones[chain['lower']].constraints[chain['constraint_name']]
    update()

    def mode(enabled):
        root['AuthoringMode'] = bool(enabled); root.update_tag(); update()

    def reset():
        mode(False)
        for name, obj in controls.items(): obj.matrix_basis = rest_matrices[name]
        for side in ['Left', 'Right']:
            obj = controls[side + 'Hand_IK']
            for finger in ['Thumb', 'Index', 'Middle', 'Ring', 'Pinky']: obj['Curl' + finger] = 0.0
            obj.update_tag()
        for pose in rig.pose.bones: pose.matrix_basis = Matrix.Identity(4)
        update()

    reset(); mode(True)
    # Fit the pole angle against the actual authored bend plane. This avoids
    # mirrored bone roll assumptions and records a reproducible static calibration.
    pole_calibration = []
    for chain in chain_records:
        constraint = chain['constraint']
        def error(angle):
            constraint.pole_angle = angle; update()
            return (rig.matrix_world @ rig.pose.bones[chain['lower']].head - chain['rest_joint']).length_squared
        samples = [(error(-math.pi + i * math.pi / 12), -math.pi + i * math.pi / 12) for i in range(24)]
        best = min(samples)[1]; lo, hi = best - math.pi / 12, best + math.pi / 12
        for _ in range(22):
            x = lo + (hi - lo) / 3; y = hi - (hi - lo) / 3
            if error(x) < error(y): hi = y
            else: lo = x
        angle = (lo + hi) * .5; joint_error = math.sqrt(error(angle))
        pole_calibration.append({'chain': chain['side'] + chain['limb'], 'pole_angle_degrees': math.degrees(angle), 'rest_joint_error_m': joint_error})
    reset(); mode(True)
    rest_mode_drift = max(float(np.linalg.norm(helpers.evaluated_points(bpy.data.objects[name]) - points, axis=1).max())
                          for name, points in original_points.items())
    tests = []
    for chain in chain_records:
        side = chain['side']; sign = 1 if side == 'Left' else -1
        candidates = ([(sign * .48, -.18, 1.18), (sign * .25, -.32, 1.22), (sign * .30, .12, 1.05)] if chain['limb'] == 'Hand'
                      else [(sign * .111, -.18, .25), (sign * .20, .08, .22), (sign * .10, -.08, .40)])
        for index, position in enumerate(candidates):
            reset(); mode(True); target = chain['target']; matrix = target.matrix_world.copy(); matrix.translation = position; target.matrix_world = matrix; update()
            actual = rig.matrix_world @ rig.pose.bones[chain['endpoint']].head
            target_error = (actual - target.matrix_world.translation).length
            drift = max(abs(rig.pose.bones[n].length - length) for n, length in zip([chain['upper'], chain['lower']], chain['lengths']))
            valid = chain['constraint'].is_valid
            tests.append({'chain': side + chain['limb'], 'case': index, 'target': list(position), 'target_error_m': target_error,
                          'segment_length_drift_m': drift, 'constraint_valid': valid})
            if target_error > .001 or drift > .000002 or not valid:
                tests[-1]['influence'] = chain['constraint'].influence
                tests[-1]['mode'] = root['AuthoringMode']
                tests[-1]['actual'] = list(actual)
                tests[-1]['drivers'] = [{'path': d.data_path, 'valid': d.driver.is_valid, 'expression': d.driver.expression,
                                        'vars': [{'name': v.name, 'target': v.targets[0].id.name, 'path': v.targets[0].data_path,
                                                  'resolved': str(v.targets[0].id.path_resolve(v.targets[0].data_path))} for v in d.driver.variables]} for d in list(rig.animation_data.drivers)[:1]]
                raise RuntimeError('IK diagnostic failed: ' + json.dumps(tests[-1]))
    # Pole movement must alter the middle joint while leaving the hand/foot on target.
    pole_tests = []
    for chain in chain_records:
        reset(); mode(True); sign = 1 if chain['side'] == 'Left' else -1
        target = chain['target']; matrix = target.matrix_world.copy()
        matrix.translation = (sign * .40, -.20, 1.20) if chain['limb'] == 'Hand' else (sign * .111, -.15, .25)
        target.matrix_world = matrix; update(); before = rig.pose.bones[chain['lower']].head.copy()
        pole = chain['pole']; matrix = pole.matrix_world.copy(); matrix.translation += Vector((.20, .15, .10)); pole.matrix_world = matrix; update()
        joint_motion = (rig.pose.bones[chain['lower']].head - before).length
        error = (rig.matrix_world @ rig.pose.bones[chain['endpoint']].head - target.matrix_world.translation).length
        pole_tests.append({'chain': chain['side'] + chain['limb'], 'joint_motion_m': joint_motion, 'target_error_m': error})
        if joint_motion < .001 or error > .001: raise RuntimeError('Pole is not functional: ' + json.dumps(pole_tests[-1]))
    reset(); mode(True)
    finger_tests = []
    for side in ['Left', 'Right']:
        for finger in ['Thumb', 'Index', 'Middle', 'Ring', 'Pinky']:
            reset(); mode(True); hand = controls[side + 'Hand_IK']; end_bone = side + 'Hand' + finger + '3'
            before = rig.pose.bones[end_bone].tail.copy(); hand['Curl' + finger] = .65; hand.update_tag(); update()
            motion = (rig.pose.bones[end_bone].tail - before).length
            finger_tests.append({'side': side, 'finger': finger, 'tip_motion_m': motion})
            if motion < .01: raise RuntimeError('Finger curl is a placeholder, not a working control.')
    torso_tests = []
    for control_name, bone_name in [('Root', 'Root'), ('Hips', 'Hips'), ('Chest', 'Spine02')]:
        reset(); mode(True); control = controls[control_name]; matrix = control.matrix_world.copy()
        if control_name != 'Chest': matrix.translation += Vector((.025, -.018, .025))
        new_rotation = (Quaternion((0, 0, 1), math.radians(9)) @ matrix.to_quaternion()).to_matrix().to_4x4()
        new_rotation.translation = matrix.translation; control.matrix_world = new_rotation; update()
        actual = rig.matrix_world @ rig.pose.bones[bone_name].matrix
        position_error = (actual.translation - control.matrix_world.translation).length if control_name != 'Chest' else 0
        rotation_error = actual.to_quaternion().rotation_difference(control.matrix_world.to_quaternion()).angle
        torso_tests.append({'control': control_name, 'position_error_m': position_error, 'rotation_error_radians': rotation_error})
        if position_error > .00001 or rotation_error > .0005: raise RuntimeError('Root/hips/chest control failed: ' + json.dumps(torso_tests[-1]))
    reset()
    reset_error = max(float(np.linalg.norm(helpers.evaluated_points(bpy.data.objects[name]) - points, axis=1).max())
                      for name, points in original_points.items())
    if reset_error > .000002: raise RuntimeError(f'Reset changed mesh pose: {reset_error}')
    # OFF must also allow a direct deform pose, not only the identity rest pose.
    arm = rig.pose.bones['LeftArm']; arm.rotation_mode = 'QUATERNION'; arm.rotation_quaternion = Quaternion((0, 0, 1), .3); update()
    direct_matrix = arm.matrix.copy()
    controls['LeftHand_IK'].location += Vector((.1, .1, .1)); update()
    off_matrix_delta = max(abs(v) for row in arm.matrix - direct_matrix for v in row)
    if off_matrix_delta > .000001: raise RuntimeError('Disabled controls interfere with direct FK posing.')
    reset()
    for pose in rig.pose.bones: pose.rotation_mode = original_rotation_modes[pose.name]
    for obj in meshes:
        if helpers.body_signature(obj) != signatures[obj.name]: raise RuntimeError('Geometry/weights/UV changed: ' + obj.name)
    for name, matrix in original_bones.items():
        if max(abs(v) for row in rig.data.bones[name].matrix_local - matrix for v in row) > .0000001: raise RuntimeError('Deform rest changed.')
    # A text-block reset command works without installing a Blender add-on or handlers.
    reset_code = "import bpy\nfrom mathutils import Matrix\n"
    reset_code += f"root=bpy.data.objects[{root.name!r}]\nroot['AuthoringMode']=False\nroot.update_tag()\n"
    for name, matrix in rest_matrices.items(): reset_code += f"bpy.data.objects[{(PREFIX + name)!r}].matrix_basis=Matrix({[list(row) for row in matrix]!r})\n"
    for side in ['Left', 'Right']:
        for finger in ['Thumb', 'Index', 'Middle', 'Ring', 'Pinky']:
            reset_code += f"bpy.data.objects[{(PREFIX + side + 'Hand_IK')!r}][{'Curl' + finger!r}]=0.0\n"
    reset_code += "for p in bpy.data.objects['DosaV2_Rig'].pose.bones:p.matrix_basis=Matrix.Identity(4)\nbpy.context.view_layer.update()\n"
    text_block = bpy.data.texts.new('DosaV2_ResetControls.py'); text_block.write(reset_code)
    root['ResetText'] = text_block.name
    # Visible diagnostic pose; restored before saving. The empties themselves
    # are viewport authoring guides and intentionally do not render.
    spec = importlib.util.spec_from_file_location('brush_control_qa_helpers', Path(__file__).with_name('build_brush.py'))
    render_helpers = importlib.util.module_from_spec(spec); spec.loader.exec_module(render_helpers)
    scene = bpy.context.scene; camera = scene.camera
    if camera is None: camera = render_helpers.lighting(scene)
    qa = OUT / 'QA'; qa.mkdir(exist_ok=True)
    render_helpers.render(scene, camera, qa / 'Controls_Rest_Quarter.png', (0, .04, .90), 2.0, math.pi * .20, width=1050, height=1350)
    mode(True)
    for side, sign in [('Left', 1), ('Right', -1)]:
        hand = controls[side + 'Hand_IK']; matrix = hand.matrix_world.copy(); matrix.translation = (sign * .32, -.25, 1.13)
        hand.matrix_world = matrix
        for finger in ['Thumb', 'Index', 'Middle', 'Ring', 'Pinky']: hand['Curl' + finger] = .35
        hand.update_tag()
    foot = controls['LeftFoot_IK']; matrix = foot.matrix_world.copy(); matrix.translation = (.111, -.18, .25); foot.matrix_world = matrix; update()
    visual_errors = {chain['side'] + chain['limb']: (rig.matrix_world @ rig.pose.bones[chain['endpoint']].head - chain['target'].matrix_world.translation).length for chain in chain_records}
    render_helpers.render(scene, camera, qa / 'Controls_IK_Quarter.png', (0, .04, .90), 2.0, math.pi * .20, width=1050, height=1350)
    reset()
    for obj in controls.values(): obj.select_set(False)
    root.hide_set(False); root.select_set(True); bpy.context.view_layer.objects.active = root
    bpy.ops.file.pack_all(); bpy.ops.wm.save_as_mainfile(filepath=str(DESTINATION))
    if digest(SOURCE) != source_hash: raise RuntimeError('Input source changed.')
    report = {'status': 'BLENDER_CONTROL_STATIC_TESTS_PASS_NOT_CHARACTER_RIG_PASS', 'source': str(SOURCE), 'source_sha256': source_hash,
              'source_unchanged': True, 'output': str(DESTINATION), 'output_sha256': digest(DESTINATION),
              'recipe_sha256': digest(Path(__file__)), 'control_collection': collection.name, 'mode_control': root.name,
              'mode_property': 'AuthoringMode', 'default_authoring_mode': False, 'controls': [obj.name for obj in controls.values()],
              'deform_bone_count': len(rig.data.bones), 'constraint_count': len(constraints), 'pole_calibration': pole_calibration,
              'rest_authoring_enabled_mesh_drift_m': rest_mode_drift, 'ik_tests': tests, 'pole_tests': pole_tests, 'finger_tests': finger_tests,
              'torso_tests': torso_tests, 'visible_diagnostic_ik_error_m': visual_errors,
              'reset_mesh_drift_m': reset_error, 'disabled_control_direct_fk_matrix_delta': off_matrix_delta,
              'geometry_weights_uv_rest_unchanged': True, 'actions': len(bpy.data.actions),
              'export_contract': 'Static export with AuthoringMode=false; select DosaV2_Rig and intended meshes only, object_types ARMATURE/MESH. Do not include CTRL_* helpers or bake animation before RIG_PASS.',
              'limitations': 'Static Blender IK/pole/curl checks only. No production animation, retarget, Unity execution, cloth or runtime grip calibration.'}
    (OUT / 'control-rig-report.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    print(json.dumps({'report': str(OUT / 'control-rig-report.json'), 'ik_cases': len(tests), 'reset_error_m': reset_error, 'rest_enabled_drift_m': rest_mode_drift}))


if __name__ == '__main__': build()
