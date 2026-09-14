"""Additional fitting diagnostics; definitions only until build_extra_diagnostics().

Creates FITTEST_* actions without editing any rest bone, mesh, weight or original DIAG_*.
Blender 5 action slots are assigned explicitly. All pose bones are keyed at frames
1 (rest), 24 (target), and 48 (rest), so unkeyed cloth/finger values cannot leak in.
These are diagnostic poses, not production animation or simulated fabric motion.
"""
import json
import math
from pathlib import Path

import bpy
from mathutils import Matrix, Quaternion, Vector


DX_RIG = 'Dosa_Phase1_Rig'
DX_LABELS = ('ArmsDown', 'Overhead', 'DeepElbow', 'Twist', 'ForwardBend')
DX_OUT = Path('C:/Users/yj666/Oheangbu/Art/PlayerPhase1/FitRigV3')


def dx_assign(rig, action, old_slot=None):
    rig.animation_data_create()
    rig.animation_data.action = action
    if action is not None and hasattr(action, 'slots'):
        slot = old_slot
        if slot is None:
            slots = [s for s in action.slots if s.target_id_type == 'OBJECT']
            slot = next((s for s in slots if rig.name in s.identifier), slots[0] if slots else None)
        if slot is None:
            slot = action.slots.new(id_type='OBJECT', name=rig.name)
        rig.animation_data.action_slot = slot


def dx_local_quaternion(pose_bone, rotation):
    """Preserve each bone's declared rotation mode, including any existing Euler bones."""
    rotation.normalize()
    mode = pose_bone.rotation_mode
    if mode == 'QUATERNION':
        pose_bone.rotation_quaternion = rotation
    elif mode == 'AXIS_ANGLE':
        axis, angle = rotation.to_axis_angle()
        pose_bone.rotation_axis_angle = (angle, axis.x, axis.y, axis.z)
    else:
        pose_bone.rotation_euler = rotation.to_euler(mode)


def dx_reset(rig):
    for p in rig.pose.bones:
        p.matrix_basis = Matrix.Identity(4)
    bpy.context.view_layer.update()


def dx_world_rotation(rig, name, axis, degrees):
    """Conjugate a WORLD rotation through the current parent's pose + child's REST frame.

    For the standard inherited-rotation hierarchy used here:
      Wbase = rig_world * parent_pose * inverse(parent_rest) * child_rest
      Rlocal_new = inverse(Qbase) * Rworld * Qbase * Rlocal_old
    This does not assume a Blender bone's local X/Y/Z is the anatomical hinge axis.
    """
    p = rig.pose.bones[name]
    rest = p.bone.matrix_local.copy()
    if p.parent and p.bone.use_inherit_rotation:
        parent_to_child = p.parent.bone.matrix_local.inverted_safe() @ rest
        base = rig.matrix_world @ p.parent.matrix @ parent_to_child
    else:
        base = rig.matrix_world @ rest
    qbase = base.to_quaternion().normalized()
    qworld = Quaternion(Vector(axis).normalized(), math.radians(degrees))
    old = p.matrix_basis.to_quaternion().normalized()
    dx_local_quaternion(p, qbase.inverted() @ qworld @ qbase @ old)
    bpy.context.view_layer.update()


def dx_world_rest_direction(rig, name):
    bone = rig.data.bones[name]
    return (rig.matrix_world.to_3x3() @ (bone.tail_local - bone.head_local)).normalized()


def dx_down_sign(rig, name):
    """Choose the sign about world Y that lowers this actual upper-arm rest direction."""
    direction = dx_world_rest_direction(rig, name)
    plus = Quaternion((0, 1, 0), math.radians(80)) @ direction
    minus = Quaternion((0, 1, 0), math.radians(-80)) @ direction
    return 1.0 if plus.z < minus.z else -1.0


def dx_pose(rig, label):
    operations = []

    def rotate(name, axis, angle):
        dx_world_rotation(rig, name, axis, angle)
        operations.append({'bone': name, 'world_axis': list(Vector(axis).normalized()),
                           'angle_degrees': angle})

    if label in ('ArmsDown', 'Overhead'):
        magnitude = 80.0 if label == 'ArmsDown' else -70.0
        for side in ('Left', 'Right'):
            name = side + 'Arm'
            rotate(name, (0, 1, 0), dx_down_sign(rig, name) * magnitude)
    elif label == 'DeepElbow':
        forward = Vector((0, -1, 0))
        for side in ('Left', 'Right'):
            name = side + 'ForeArm'
            direction = dx_world_rest_direction(rig, name)
            axis = direction.cross(forward)
            if axis.length < 1e-6:
                raise RuntimeError('Cannot determine forward elbow hinge for ' + name)
            rotate(name, axis.normalized(), 140.0)
    elif label == 'Twist':
        for name, angle in (('Spine02', 10.0), ('Spine01', 12.0), ('Spine', 13.0)):
            rotate(name, (0, 0, 1), angle)
    elif label == 'ForwardBend':
        # Positive world X tips the upright +Z torso toward the actual forward direction -Y.
        for name, angle in (('Spine02', 25.0), ('Spine01', 20.0), ('Spine', 15.0)):
            rotate(name, (1, 0, 0), angle)
    else:
        raise ValueError(label)
    return operations


def dx_key_all(rig, frame):
    for p in rig.pose.bones:
        rotation_channel = ('rotation_quaternion' if p.rotation_mode == 'QUATERNION' else
                            'rotation_axis_angle' if p.rotation_mode == 'AXIS_ANGLE' else 'rotation_euler')
        p.keyframe_insert(data_path='location', frame=frame, group=p.name)
        p.keyframe_insert(data_path=rotation_channel, frame=frame, group=p.name)
        p.keyframe_insert(data_path='scale', frame=frame, group=p.name)


def dx_curves(action):
    curves = []
    if hasattr(action, 'layers'):
        for layer in action.layers:
            for strip in layer.strips:
                for bag in getattr(strip, 'channelbags', ()):
                    curves.extend(bag.fcurves)
    if not curves and hasattr(action, 'fcurves'):
        curves.extend(action.fcurves)
    return curves


def build_extra_diagnostics(reportpath=None, replace_existing=False):
    """Create all five FITTEST actions and restore current scene/action/pose afterwards.

    replace_existing can replace only actions carrying this tool's ownership marker.
    It never replaces DIAG_* or another author's same-named action.
    Save the working .blend separately after checking the returned report.
    """
    rig = bpy.data.objects[DX_RIG]
    scene = bpy.context.scene
    if bpy.context.mode != 'OBJECT':
        raise RuntimeError('Call from Object Mode; no automatic edit-mode changes are made.')
    required = ('LeftArm', 'RightArm', 'LeftForeArm', 'RightForeArm',
                'Spine02', 'Spine01', 'Spine', 'Hips')
    missing = [n for n in required if n not in rig.pose.bones]
    if missing:
        raise RuntimeError('Required diagnostic bones are missing: ' + ', '.join(missing))
    for label in DX_LABELS:
        existing = bpy.data.actions.get('FITTEST_' + label)
        if existing is not None and (not replace_existing or not existing.get('fitrigv3_diagnostic', False)):
            raise RuntimeError('Refusing to replace existing action ' + existing.name)
    rig.animation_data_create()
    ad = rig.animation_data
    snapshot = {'frame': scene.frame_current, 'subframe': scene.frame_subframe,
                'action': ad.action, 'slot': getattr(ad, 'action_slot', None),
                'use_nla': ad.use_nla, 'pose_position': rig.data.pose_position,
                'object_matrix': rig.matrix_basis.copy(),
                'matrices': {p.name: p.matrix_basis.copy() for p in rig.pose.bones},
                'nla': [(t, t.mute, t.is_solo) for t in ad.nla_tracks]}
    report = {
        'status': 'INCOMPLETE', 'rig': rig.name, 'bone_count': len(rig.pose.bones),
        'frames': [1, 24, 48], 'fps_unchanged': scene.render.fps / scene.render.fps_base,
        'actions': [], 'rest_skeleton_modified': False, 'production_animation': False,
        'root_motion': 'Object transform and Hips remain fixed; all pose scales are 1.',
        'secondary_bones': 'All bones, including cloth and fingers, explicitly keyed at rest. '
                           'They inherit their existing parent transforms; no invented cloth motion.',
        'limits': ['Diagnostic action creation only; renders, penetration and FBX validation are separate.',
                   'These actions do not establish physical gravity behavior.',
                   'Existing pose constraints/drivers, if present, may alter evaluated endpoint poses.'],
        'pose_constraints': {p.name: [c.name for c in p.constraints]
                             for p in rig.pose.bones if p.constraints},
        'armature_world_matrix': [list(row) for row in rig.matrix_world],
    }
    try:
        ad.use_nla = False
        for track in ad.nla_tracks:
            track.mute, track.is_solo = True, False
        rig.data.pose_position = 'POSE'
        for label in DX_LABELS:
            name = 'FITTEST_' + label
            existing = bpy.data.actions.get(name)
            if existing is not None:
                replacing_snapshot_action = snapshot['action'] == existing
                if ad.action == existing:
                    ad.action = None
                bpy.data.actions.remove(existing)
                if replacing_snapshot_action:
                    snapshot['action'], snapshot['slot'] = None, None
            action = bpy.data.actions.new(name)
            action.use_fake_user = True
            action['fitrigv3_diagnostic'] = True
            action['purpose'] = 'Fit diagnostic only; T pose -> target -> T pose'
            dx_assign(rig, action)
            operations = []
            for frame in (1, 24, 48):
                scene.frame_set(frame)
                dx_reset(rig)
                if frame == 24:
                    operations = dx_pose(rig, label)
                dx_key_all(rig, frame)
            for curve in dx_curves(action):
                curve.extrapolation = 'CONSTANT'
                for point in curve.keyframe_points:
                    point.interpolation = 'BEZIER'
                    point.handle_left_type = point.handle_right_type = 'AUTO_CLAMPED'
                curve.update()
            report['actions'].append({'name': action.name, 'keyed_bones': len(rig.pose.bones),
                                      'frames': [1, 24, 48], 'target_world_rotations': operations,
                                      'status': 'CREATED_NOT_VISUALLY_VALIDATED'})
        report['status'] = 'CREATED_NOT_VISUALLY_VALIDATED'
    except Exception as exc:
        report['error'] = repr(exc)
    finally:
        dx_assign(rig, snapshot['action'], snapshot['slot'])
        ad.use_nla = snapshot['use_nla']
        for track, mute, solo in snapshot['nla']:
            track.mute, track.is_solo = mute, solo
        rig.data.pose_position = snapshot['pose_position']
        scene.frame_set(snapshot['frame'], subframe=snapshot['subframe'])
        rig.matrix_basis = snapshot['object_matrix']
        for p in rig.pose.bones:
            p.matrix_basis = snapshot['matrices'][p.name]
        bpy.context.view_layer.update()
        destination = Path(reportpath) if reportpath else DX_OUT / 'diagnostic_extra_creation.json'
        destination.parent.mkdir(parents=True, exist_ok=True)
        destination.write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding='utf-8')
    print(json.dumps({'status': report['status'], 'actions': [a['name'] for a in report['actions']]},
                     ensure_ascii=False))
    return report
