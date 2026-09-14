"""Definitions only. Procedural joint diagnostics, not captured production motions.

Call create_diagnostic_poses(rig_name=None, mesh_names=None, output_path=None).
Creates DIAG_ArmsDown/DeepElbow/Overhead/Squat: 30 fps, frames 1..60,
T pose at 1/60, target hold at 16..46. Existing actions and rest data are preserved.
Squat solves both legs every frame; its numerical result still needs visual review.
"""
import json
import math
from pathlib import Path

import bpy
from mathutils import Matrix, Quaternion, Vector


def dp_assign(rig, action, slot=None):
    rig.animation_data_create()
    rig.animation_data.action = action
    if action is not None and len(getattr(action, 'slots', ())):
        slots = [s for s in action.slots if s.target_id_type == 'OBJECT']
        rig.animation_data.action_slot = slot or slots[0]


def dp_update():
    bpy.context.view_layer.update()


def dp_world(rig, pb):
    return rig.matrix_world @ pb.matrix


def dp_joint_child(pb):
    """Imported GLTF bone display tails need not end at the anatomical joint."""
    name = pb.name.rsplit(':', 1)[-1].rsplit('|', 1)[-1]
    chains = {s+a: s+b for s in ('Left', 'Right') for a, b in (
        ('Arm', 'ForeArm'), ('ForeArm', 'Hand'), ('UpLeg', 'Leg'),
        ('Leg', 'Foot'), ('Foot', 'ToeBase'))}
    wanted = chains.get(name)
    if wanted is None:
        return None
    child = next((p for p in pb.children
                  if p.name.rsplit(':', 1)[-1].rsplit('|', 1)[-1] == wanted), None)
    if child is None:
        raise RuntimeError('Diagnostic anatomical chain is not direct: ' + name + ' -> ' + wanted)
    return child


def dp_direction(rig, pb):
    child = dp_joint_child(pb)
    endpoint = child.head if child is not None else pb.tail
    return (rig.matrix_world.to_3x3() @ (endpoint - pb.head)).normalized()


def dp_head(rig, pb):
    return rig.matrix_world @ pb.head


def dp_aim(rig, pb, direction):
    """Shortest-arc world rotation; preserve translation/scale and bone length."""
    world = dp_world(rig, pb)
    rotation = dp_direction(rig, pb).rotation_difference(Vector(direction).normalized())
    matrix = rotation.to_matrix().to_4x4() @ world
    matrix.translation = world.translation
    pb.matrix = rig.matrix_world.inverted() @ matrix
    dp_update()


def dp_world_rotation(rig, pb, rotation):
    world = dp_world(rig, pb)
    pb.matrix = rig.matrix_world.inverted() @ Matrix.LocRotScale(
        world.translation, rotation, world.to_scale())
    dp_update()


def dp_reset(rig):
    for pb in rig.pose.bones:
        pb.rotation_mode = 'QUATERNION'
        pb.matrix_basis = Matrix.Identity(4)
    dp_update()


def dp_angle(a, b):
    return math.degrees(Vector(a).angle(Vector(b)))


def dp_leg_ik(rig, bones, side, ankle, lengths):
    up, lower = bones[side + 'UpLeg'], bones[side + 'Leg']
    hip = dp_head(rig, up)
    delta = ankle - hip
    distance = delta.length
    l1, l2 = lengths[side]
    reachable = abs(l1 - l2) + 1e-6 <= distance <= l1 + l2 - 1e-6
    d = min(max(distance, abs(l1 - l2) + 1e-6), l1 + l2 - 1e-6)
    axis = delta.normalized()
    pole = Vector((0, -1, 0))
    pole = pole - axis * pole.dot(axis)
    if pole.length < 1e-6:
        pole = Vector((0, 0, 1)) - axis * axis.z
    pole.normalize()
    along = (l1*l1 - l2*l2 + d*d) / (2*d)
    knee = hip + axis * along + pole * math.sqrt(max(0, l1*l1 - along*along))
    dp_aim(rig, up, knee - hip)
    dp_aim(rig, lower, ankle - dp_head(rig, lower))
    return {'target_reachable': reachable, 'hip_ankle_distance_m': distance,
            'lengths_m': [l1, l2], 'knee_target_world': list(knee)}


def dp_foot_skin(rig, meshes, bones):
    """Actual evaluated skin vertices whose Foot group weight alone exceeds .7."""
    rows = {}
    for side in ('Left', 'Right'):
        z = []
        for obj in meshes:
            group = obj.vertex_groups.get(bones[side + 'Foot'].name)
            if not group:
                continue
            indices = [v.index for v in obj.data.vertices
                       if any(g.group == group.index and g.weight > .7 for g in v.groups)]
            evaluated = obj.evaluated_get(bpy.context.evaluated_depsgraph_get())
            mesh = evaluated.to_mesh()
            try:
                if len(mesh.vertices) != len(obj.data.vertices):
                    rows[side] = {'status': 'UNVERIFIED_TOPOLOGY_CHANGED'}
                    break
                z.extend((evaluated.matrix_world @ mesh.vertices[i].co).z for i in indices)
            finally:
                evaluated.to_mesh_clear()
        else:
            rows[side] = {'vertices': len(z), 'min_z_m': min(z) if z else None,
                          'status': 'MEASURED' if z else 'UNVERIFIED_NO_FOOT_WEIGHT_GT_0_7'}
    return rows


def create_diagnostic_poses(rig_name=None, mesh_names=None, output_path=None):
    scene = bpy.context.scene
    rig = bpy.data.objects[rig_name] if rig_name else next(o for o in scene.objects if o.type == 'ARMATURE')
    bones = {p.name.rsplit(':', 1)[-1].rsplit('|', 1)[-1]: p for p in rig.pose.bones}
    needed = ['Hips', 'Spine02'] + [s + n for s in ('Left', 'Right')
              for n in ('Arm', 'ForeArm', 'Hand', 'UpLeg', 'Leg', 'Foot')]
    missing = [n for n in needed if n not in bones]
    if missing:
        raise RuntimeError('Missing diagnostic bones: ' + ', '.join(missing))
    if any(c for p in rig.pose.bones for c in p.constraints if not c.mute):
        raise RuntimeError('Active bone constraints require review before direct diagnostic posing.')
    labels = ('ArmsDown', 'DeepElbow', 'Overhead', 'Squat')
    if any(bpy.data.actions.get('DIAG_' + n) for n in labels):
        raise RuntimeError('DIAG actions already exist; preserve/reuse them, do not silently overwrite.')
    meshes = [bpy.data.objects[n] for n in mesh_names] if mesh_names else [o for o in scene.objects
        if o.type == 'MESH' and any(m.type == 'ARMATURE' and m.object == rig for m in o.modifiers)]
    rig.animation_data_create()
    ad = rig.animation_data
    saved = {'action': ad.action, 'slot': getattr(ad, 'action_slot', None),
             'frame': scene.frame_current, 'subframe': scene.frame_subframe,
             'fps': scene.render.fps, 'fps_base': scene.render.fps_base,
             'start': scene.frame_start, 'end': scene.frame_end,
             'pose_position': rig.data.pose_position,
             'basis': {p.name: p.matrix_basis.copy() for p in rig.pose.bones},
             'mode': {p.name: p.rotation_mode for p in rig.pose.bones},
             'nla': [(t, t.mute) for t in ad.nla_tracks], 'matrix': rig.matrix_world.copy()}
    report = {'diagnostic_type': 'PROCEDURAL_JOINT_TEST_NOT_PRODUCTION_MOTION',
              'tool_revision': 2,
              'tool_correction': {
                  'cause': 'Revision 1 mistook GLTF display bone tails for anatomical joints. C01 display lengths were about 40m while hip-to-ankle separation was below 1m.',
                  'change': 'IK lengths and limb aim/angle directions now use actual parent-head to named child-head world vectors. Display tail disagreement is measured separately.',
                  'prior_squat_result': 'INVALID_DIAGNOSTIC_TOOL_RESULT_NOT_CANDIDATE_FAILURE',
                  'candidate_repair_count_increment': 0},
              'fps': 30, 'frames': [1, 60], 'target_hold_frames': [16, 46],
              'coordinates': 'Blender world metres, Z up, -Y forward', 'actions': {},
              'quality': 'UNVERIFIED_REQUIRES_FULL_FRAME_AND_VISUAL_AUDIT'}
    try:
        for track, _ in saved['nla']:
            track.mute = True
        dp_assign(rig, None)
        rig.data.pose_position = 'POSE'
        dp_reset(rig)
        ankle = {s: dp_head(rig, bones[s + 'Foot']).copy() for s in ('Left', 'Right')}
        foot_rotation = {s: dp_world(rig, bones[s + 'Foot']).to_quaternion() for s in ankle}
        rest_hips = dp_world(rig, bones['Hips']).copy()
        lengths = {s: ((dp_head(rig, bones[s+'Leg'])-dp_head(rig, bones[s+'UpLeg'])).length,
                       (ankle[s]-dp_head(rig, bones[s+'Leg'])).length) for s in ankle}
        if any(not math.isfinite(v) or not .01 < v < 2 for pair in lengths.values() for v in pair):
            raise RuntimeError('INVALID_DIAGNOSTIC_SCALE: anatomical leg segments must be in metres; no candidate failure inferred.')
        axes = {}
        for side in ankle:
            for part in ('Arm', 'ForeArm', 'UpLeg', 'Leg', 'Foot'):
                pb = bones[side+part]
                child = dp_joint_child(pb)
                anatomical = dp_head(rig, child)-dp_head(rig, pb)
                display = rig.matrix_world.to_3x3() @ (pb.tail-pb.head)
                axes[pb.name] = {'child_joint': child.name,
                    'anatomical_length_m': anatomical.length, 'display_tail_length_m': display.length,
                    'display_to_anatomical_length_ratio': display.length/anatomical.length,
                    'display_vs_anatomical_angle_degrees': dp_angle(display, anatomical),
                    'aim_uses': 'ANATOMICAL_CHILD_HEAD_DIRECTION'}
        report['rest_limb_axis_comparison'] = axes
        report['rest_leg_segment_lengths_m'] = {s: list(v) for s, v in lengths.items()}
        report['rest_ankles_world'] = {s: list(v) for s, v in ankle.items()}
        report['rest_foot_skin'] = dp_foot_skin(rig, meshes, bones)
        scene.render.fps, scene.render.fps_base = 30, 1
        scene.frame_start, scene.frame_end = 1, 60
        for label in labels:
            dp_assign(rig, None)
            dp_reset(rig)
            for side in ('Left', 'Right'):
                sign = 1 if dp_head(rig, bones[side+'Arm']).x >= dp_head(rig, bones['Hips']).x else -1
                upper = Vector((sign*.25, 0, .97)) if label == 'Overhead' else Vector((sign*.15, 0, -1))
                upper.normalize()
                dp_aim(rig, bones[side+'Arm'], upper)
                lower = upper
                if label == 'DeepElbow':
                    bend = math.radians(130)
                    lower = upper*math.cos(bend) + Vector((0, -1, 0))*math.sin(bend)
                dp_aim(rig, bones[side+'ForeArm'], lower)
            if label == 'Squat':
                world = dp_world(rig, bones['Spine02'])
                dp_world_rotation(rig, bones['Spine02'], Quaternion((1, 0, 0), math.radians(15)) @ world.to_quaternion())
            target = {p.name: p.matrix_basis.copy() for p in rig.pose.bones}
            dp_reset(rig)
            action = bpy.data.actions.new('DIAG_' + label)
            action.use_fake_user = True
            action['diagnostic_type'] = report['diagnostic_type']
            action['fps'] = 30
            action['target_frames'] = '16..46'
            action['quality'] = 'UNVERIFIED'
            dp_assign(rig, action)
            row = {'action': action.name, 'foot_bone_max_drift_m': {s: 0.0 for s in ankle},
                   'leg_ik_unreachable_frames': [], 'target_pose': None}
            for frame in range(1, 61):
                scene.frame_set(frame)
                t = min(1.0, (frame-1)/15) if frame <= 46 else (60-frame)/14
                t = max(0.0, t); t = t*t*(3-2*t)
                for pb in rig.pose.bones:
                    loc, rot, scale = target[pb.name].decompose()
                    pb.location = loc*t
                    pb.rotation_quaternion = Quaternion().slerp(rot, t)
                    pb.scale = Vector((1, 1, 1))
                dp_update()
                solves = {}
                if label == 'Squat':
                    h = rest_hips.copy(); h.translation += Vector((0, .12, -.32))*t
                    bones['Hips'].matrix = rig.matrix_world.inverted() @ h
                    dp_update()
                    for side in ankle:
                        solves[side] = dp_leg_ik(rig, bones, side, ankle[side], lengths)
                        dp_world_rotation(rig, bones[side+'Foot'], foot_rotation[side])
                        if not solves[side]['target_reachable'] and t > 1e-5:
                            row['leg_ik_unreachable_frames'].append({'frame': frame, 'side': side})
                for side in ankle:
                    drift = (dp_head(rig, bones[side+'Foot'])-ankle[side]).length
                    row['foot_bone_max_drift_m'][side] = max(row['foot_bone_max_drift_m'][side], drift)
                if frame == 16:
                    joints = {}
                    for side in ankle:
                        up = dp_direction(rig, bones[side+'Arm'])
                        fore = dp_direction(rig, bones[side+'ForeArm'])
                        thigh = dp_direction(rig, bones[side+'UpLeg'])
                        shin = dp_direction(rig, bones[side+'Leg'])
                        joints[side] = {'upper_arm_world': list(up), 'forearm_world': list(fore),
                            'elbow_flexion_degrees': dp_angle(up, fore),
                            'elbow_internal_degrees': 180-dp_angle(up, fore),
                            'knee_flexion_degrees': dp_angle(thigh, shin),
                            'ankle_world': list(dp_head(rig, bones[side+'Foot'])),
                            'ankle_drift_m': (dp_head(rig, bones[side+'Foot'])-ankle[side]).length}
                    row['target_pose'] = {'joints': joints, 'leg_ik': solves,
                                          'foot_skin': dp_foot_skin(rig, meshes, bones)}
                for pb in rig.pose.bones:
                    pb.keyframe_insert('rotation_quaternion', frame=frame, group=pb.name)
                    pb.keyframe_insert('location', frame=frame, group=pb.name)
            row['status'] = 'UNVERIFIED_IK_UNREACHABLE' if row['leg_ik_unreachable_frames'] else 'CREATED_REQUIRES_VISUAL_AUDIT'
            report['actions'][label] = row
        report['root_object_matrix_max_change'] = max(abs(rig.matrix_world[i][j]-saved['matrix'][i][j]) for i in range(4) for j in range(4))
        report['limits'] = ['Foot-bone planting is not a skin/ground pass. Compare target skin minZ with rest and actual ground.',
                            'No cloth, secondary bones, accessories, production motion, geometry or weights were added.',
                            'No real brush is present in these joint diagnostics.']
        if output_path:
            path = Path(output_path); path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(json.dumps(report, indent=2), encoding='utf-8')
        return report
    finally:
        dp_assign(rig, saved['action'], saved['slot'])
        for track, mute in saved['nla']:
            track.mute = mute
        scene.render.fps, scene.render.fps_base = saved['fps'], saved['fps_base']
        scene.frame_start, scene.frame_end = saved['start'], saved['end']
        rig.data.pose_position = saved['pose_position']
        scene.frame_set(saved['frame'], subframe=saved['subframe'])
        for pb in rig.pose.bones:
            pb.rotation_mode = saved['mode'][pb.name]
            pb.matrix_basis = saved['basis'][pb.name]
        dp_update()
