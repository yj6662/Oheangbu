"""Actual Blender Cloth on disposable static-pose copies, never a production animation.

Source geometry, deform rest bones and source files are read-only. Added pin groups,
native Cloth modifiers and collision meshes exist only in Inspect/ClothBlender.
"""
import argparse
from collections import deque
from datetime import datetime, timezone
import hashlib
import importlib.util
import json
import math
from pathlib import Path
import time

import bpy
import numpy as np
from mathutils import Matrix, Quaternion, Vector

ROOT = Path(__file__).resolve().parents[3]
ART = ROOT / 'Art/PlayerV2'
OUT = ART / 'Inspect/ClothBlender'
CASES = ['rest_settle', 'raised_arms_settle', 'grip_settle', 'reset']
LIMITS = {'maxWeightSumError': .0001, 'maxBoneLengthDeltaMeters': .0001, 'maxRootDeltaMeters': .0001,
          'maxPinnedDriftMeters': .001, 'maxClothStretchRatio': 1.35, 'maxSettledSpeedMetersPerSecond': .03,
          'minSimulationSeconds': 3, 'minSettledSamples': 60}
PHYSICS = {'fps': 60, 'minimumFrames': 181, 'maximumFrames': 301, 'quality': 8, 'gravity': [0, 0, -9.81],
           'pinStiffness': 1, 'massPerVertexKg': .08, 'tensionStiffness': 80, 'compressionStiffness': 80,
           'shearStiffness': 50, 'bendingStiffness': .8, 'airDamping': 5,
           'collisionDistanceMeters': .003, 'selfCollisionDistanceMeters': .002, 'collisionQuality': 6}


def digest(path): return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def evaluated_points(obj):
    evaluated = obj.evaluated_get(bpy.context.evaluated_depsgraph_get()); mesh = evaluated.to_mesh()
    positions = np.array([tuple(evaluated.matrix_world @ v.co) for v in mesh.vertices], dtype=np.float64)
    evaluated.to_mesh_clear(); return positions


def aim_bone(rig, name, direction):
    bone = rig.pose.bones[name]; matrix = bone.matrix.copy(); rotation = matrix.to_quaternion()
    delta = (rotation @ Vector((0, 1, 0))).rotation_difference(Vector(direction).normalized())
    changed = (delta @ rotation).to_matrix().to_4x4(); changed.translation = matrix.translation
    bone.matrix = changed; bpy.context.view_layer.update()


def direct_pose(rig, case):
    for bone in rig.pose.bones: bone.matrix_basis = Matrix.Identity(4)
    bpy.context.view_layer.update()
    if case == 'raised_arms_settle':
        for side, sign in [('Left', 1), ('Right', -1)]: aim_bone(rig, side + 'Arm', (sign * .38, 0, .93))
    elif case == 'grip_settle':
        for side, sign in [('Left', 1), ('Right', -1)]:
            aim_bone(rig, side + 'Arm', (sign * .25, -.6, -.4))
            aim_bone(rig, side + 'ForeArm', (sign * .08, -.95, .2))
            for finger in ['Thumb', 'Index', 'Middle', 'Ring', 'Pinky']:
                for index, angle in enumerate([30, 45, 30], 1):
                    bone = rig.pose.bones[side + 'Hand' + finger + str(index)]
                    bone.rotation_mode = 'XYZ'; bone.rotation_euler.x = -math.radians(angle)
    bpy.context.view_layer.update()


def capsule(collection, name, start, end, radius, kind='body'):
    a, b = Vector(start), Vector(end); length = (b - a).length
    rotation = (b - a).to_track_quat('Z', 'Y').to_matrix() if length > 1e-9 else Matrix.Identity(3)
    rings = []
    for i in range(9):
        angle = -math.pi / 2 + i * math.pi / 16
        rings.append((math.sin(angle) * radius, max(.0000001, math.cos(angle) * radius)))
    for i in range(9):
        if length <= 1e-9 and i == 0: continue
        angle = i * math.pi / 16; rings.append((length + math.sin(angle) * radius, max(.0000001, math.cos(angle) * radius)))
    vertices = []
    for z, r in rings:
        for i in range(32): vertices.append(a + rotation @ Vector((r * math.cos(i * math.tau / 32), r * math.sin(i * math.tau / 32), z)))
    faces = []
    for j in range(len(rings) - 1):
        for i in range(32): faces.append((j * 32 + i, j * 32 + (i + 1) % 32, (j + 1) * 32 + (i + 1) % 32, (j + 1) * 32 + i))
    faces.extend([tuple(reversed(range(32))), tuple((len(rings) - 1) * 32 + i for i in range(32))])
    mesh = bpy.data.meshes.new(name); mesh.from_pydata(vertices, [], faces); mesh.update()
    obj = bpy.data.objects.new(name, mesh); collection.objects.link(obj); obj.hide_render = True
    obj.display_type = 'WIRE'; obj.modifiers.new('DiagnosticBodyCollision', 'COLLISION')
    obj.collision.thickness_outer = .002; obj.collision.damping = .6; obj.collision.cloth_friction = 8
    return {'object': obj, 'name': name, 'a': np.asarray(a), 'b': np.asarray(b), 'radius': radius, 'kind': kind}


def proxies_for_pose(rig, collection, fit_points=None, body_definition=None):
    def head(name): return rig.matrix_world @ rig.pose.bones[name].head
    fitted = []
    def make(name, a, b, radius):
        original = radius
        if fit_points is not None:
            av, bv = np.asarray(a), np.asarray(b); axis = bv - av
            t = np.clip(((fit_points - av) @ axis) / np.dot(axis, axis), 0, 1)
            distances = np.linalg.norm(fit_points - (av + t[:, None] * axis), axis=1)
            radius = min(radius, max(.005, float(distances.min()) - .006))
        result = capsule(collection, name, a, b, radius)
        result['unfittedRadius'] = original
        return result
    def segment(name, start, end, ratio):
        a, b = head(start), head(end); return make(name, a, b, (b - a).length * ratio)
    result = []
    if body_definition is not None:
        for row in body_definition['capsules']:
            if row.get('kind','body')!='body':continue
            anchor = row['anchorBone']; rest_inverse = rig.data.bones[anchor].matrix_local.inverted()
            matrix = rig.matrix_world @ rig.pose.bones[anchor].matrix @ rest_inverse
            a = matrix @ Vector(row['startBlender']); b = matrix @ Vector(row['endBlender'])
            result.append(capsule(collection,row['name'],a,b,row['radiusMeters']))
    else:
        for side in ['Left', 'Right']:
            result.extend([segment('Diag_' + side + 'Thigh', side + 'UpLeg', side + 'Leg', .20),
                           segment('Diag_' + side + 'Shin', side + 'Leg', side + 'Foot', .17),
                           segment('Diag_' + side + 'UpperArm', side + 'Arm', side + 'ForeArm', .18),
                           segment('Diag_' + side + 'ForeArm', side + 'ForeArm', side + 'Hand', .16)])
        width = (head('LeftArm') - head('RightArm')).length
        result.append(make('Diag_Torso', head('Hips'), head('Neck'), width * .32))
    grip = rig.matrix_world @ rig.pose.bones['RightBrushGrip'].matrix
    point = grip.translation; axis = grip.to_quaternion() @ Vector((0, 1, 0))
    result.append(capsule(collection, 'Diag_BrushShaft', point - axis * .1562184, point + axis * .4853899, .020854, 'brush'))
    result.append(capsule(collection, 'Diag_BrushHairEnvelope', point + axis * .4853899, point + axis * .7437816, .028, 'brush'))
    return result


def component_inventory(mesh, pinned):
    adjacency = [[] for _ in mesh.vertices]
    for edge in mesh.edges:
        a, b = edge.vertices; adjacency[a].append(b); adjacency[b].append(a)
    pending = set(range(len(mesh.vertices))); rows = []
    while pending:
        start = pending.pop(); found = [start]; stack = [start]
        while stack:
            for other in adjacency[stack.pop()]:
                if other in pending: pending.remove(other); found.append(other); stack.append(other)
        rows.append({'vertices': len(found), 'pinnedVertices': int(pinned[found].sum())})
    return rows


def add_cloth(obj, pin_name, collection, max_frame):
    modifier = obj.modifiers.new('DiagnosticNativeCloth', 'CLOTH'); settings = modifier.settings
    settings.quality = PHYSICS['quality']
    obj.data.calc_loop_triangles()
    surface_area = sum(((obj.matrix_world @ obj.data.vertices[t.vertices[1]].co - obj.matrix_world @ obj.data.vertices[t.vertices[0]].co).cross(
                        obj.matrix_world @ obj.data.vertices[t.vertices[2]].co - obj.matrix_world @ obj.data.vertices[t.vertices[0]].co)).length * .5 for t in obj.data.loop_triangles)
    settings.mass = surface_area * PHYSICS['arealDensityKgPerSquareMetre'] / len(obj.data.vertices) if PHYSICS['arealDensityKgPerSquareMetre'] is not None else PHYSICS['massPerVertexKg']
    obj['diagnostic_surface_area_m2'] = surface_area
    settings.vertex_group_mass = pin_name; settings.pin_stiffness = PHYSICS['pinStiffness']
    settings.tension_stiffness = PHYSICS['tensionStiffness']; settings.compression_stiffness = PHYSICS['compressionStiffness']
    settings.shear_stiffness = PHYSICS['shearStiffness']; settings.bending_stiffness = PHYSICS['bendingStiffness']
    settings.bending_model = PHYSICS['bendingModel']
    settings.tension_damping = 10; settings.compression_damping = 10; settings.shear_damping = 10; settings.bending_damping = PHYSICS['bendingDamping']
    settings.air_damping = PHYSICS['airDamping']; settings.use_dynamic_mesh = False
    settings.effector_weights.gravity = 1; settings.effector_weights.wind = .2
    collision = modifier.collision_settings; collision.use_collision = True; collision.collection = collection
    collision.distance_min = PHYSICS['collisionDistanceMeters']; collision.collision_quality = PHYSICS['collisionQuality']
    collision.use_self_collision = PHYSICS['selfCollision']; collision.self_distance_min = PHYSICS['selfCollisionDistanceMeters']; collision.self_friction = 8
    modifier.point_cache.frame_start = 1; modifier.point_cache.frame_end = max_frame
    return modifier


def penetration_samples(points, proxies):
    result = {'body': {'count': 0, 'depth': 0.0}, 'brush': {'count': 0, 'depth': 0.0}}
    for proxy in proxies:
        a, b, radius = proxy['a'], proxy['b'], proxy['radius']; axis = b - a
        length_squared = np.dot(axis, axis)
        t = np.clip(((points - a) @ axis) / length_squared, 0, 1) if length_squared > 1e-18 else np.zeros(len(points))
        depth = radius - np.linalg.norm(points - (a + t[:, None] * axis), axis=1)
        if not np.isfinite(depth).all(): raise RuntimeError('Non-finite analytic capsule distance: ' + proxy['name'])
        selected = depth > .0005
        result[proxy['kind']]['count'] += int(selected.sum())
        if selected.any(): result[proxy['kind']]['depth'] = max(result[proxy['kind']]['depth'], float(depth[selected].max()))
    return result


def run(args):
    OUT.mkdir(parents=True, exist_ok=True); source = Path(args.source).resolve(); source_hash = digest(source)
    case_dir = OUT / args.run_tag / args.case if args.run_tag else OUT / args.case; case_dir.mkdir(parents=True, exist_ok=True)
    validator_snapshot = case_dir / 'validator-source.py'
    validator_snapshot.write_bytes(Path(__file__).read_bytes()); validator_hash = digest(validator_snapshot)
    PHYSICS['selfCollision'] = args.self_collision
    PHYSICS['pinTransitionMeters'] = args.pin_transition
    PHYSICS['collisionFitPinClearance'] = args.collision_fit_pin_clearance
    PHYSICS['quality'] = args.quality
    PHYSICS['airDamping'] = args.air_damping
    PHYSICS['bendingDamping'] = args.bending_damping
    PHYSICS['arealDensityKgPerSquareMetre'] = args.areal_density
    PHYSICS['bendingModel'] = args.bending_model
    if args.body_proxies and args.collision_fit_pin_clearance:raise RuntimeError('Anatomy proxy definitions cannot be combined with pin-clearance fitting.')
    body_definition=json.loads(Path(args.body_proxies).read_text(encoding='utf-8-sig')) if args.body_proxies else None
    PHYSICS['bodyProxyDefinitionSha256']=digest(args.body_proxies) if args.body_proxies else None
    physics_id = hashlib.sha256(json.dumps(PHYSICS,sort_keys=True).encode()).hexdigest()[:12]
    config_path = OUT / ('blender-fixture-'+physics_id+'-configuration.json')
    config = {'fixedAtUtc': datetime.now(timezone.utc).isoformat(), 'acceptanceLimits': LIMITS, 'physics': PHYSICS,
              'rationale': 'Parent-fixed limits before execution: 1.75m rig; pin drift1mm, maximum instantaneous heavy-cloth strain35%, settled speed3cm/s over at least60frames after3..5seconds. Do not relax to fit results.',
              'solverDifference': 'Exact ClothMobility red==0 maps to unit pin weights. A declared near-seam transition may apply squared graded pin weights to tiny positive max-distance values; other vertices remain free. This is not an exact Unity maxDistance constraint. Native solvers are evaluated independently.'}
    if config_path.exists():
        previous = json.loads(config_path.read_text());
        if previous['acceptanceLimits'] != LIMITS or previous['physics'] != PHYSICS: raise RuntimeError('Frozen fixture configuration changed.')
    else: config_path.write_text(json.dumps(config, indent=2), encoding='utf-8')
    bpy.ops.wm.open_mainfile(filepath=str(source)); scene = bpy.context.scene; scene.frame_set(1)
    rig = bpy.data.objects['DosaV2_Rig']
    if bpy.data.actions: raise RuntimeError('Production actions are not allowed in this fixture source.')
    for obj in scene.objects:
        if obj.name.startswith('CTRL_') and 'AuthoringMode' in obj: obj['AuthoringMode'] = False; obj.update_tag()
    bpy.context.view_layer.update(); scene.frame_set(1); direct_pose(rig, args.case)
    pose_records = [{'bone': b.name, 'xyz_local_euler_degrees': [math.degrees(a) for a in b.matrix_basis.to_euler('XYZ')]} for b in rig.pose.bones]
    surfaces = [o for o in scene.objects if o.type == 'MESH' and o.name.startswith(('DosaV2_Robe_', 'DosaV2_SleeveOuter_'))]
    combined_coat = any(o.name == 'DosaV2_Robe_Combined' for o in surfaces)
    if len(surfaces) != (3 if combined_coat else 8): raise RuntimeError(f'Unexpected cloth solver surface count: {len(surfaces)}')
    collection = bpy.data.collections.new('ClothDiagnosticCollisionProxies'); scene.collection.children.link(collection)
    fit_points = None
    if args.collision_fit_pin_clearance:
        fit_points = np.concatenate([evaluated_points(o)[np.asarray([c.color[0] == 0 for c in o.data.color_attributes['ClothMobility'].data])] for o in surfaces])
    proxies = proxies_for_pose(rig, collection, fit_points, body_definition); inventory = []
    for obj in surfaces:
        if any(m.type == 'CLOTH' for m in obj.modifiers): raise RuntimeError('Source already has a Cloth modifier.')
        colors = obj.data.color_attributes.get('ClothMobility')
        if colors is None or colors.domain != 'POINT': raise RuntimeError('Missing point ClothMobility: ' + obj.name)
        mobility = np.asarray([c.color[0] for c in colors.data]); pinned = mobility == 0
        if not pinned.any() or pinned.all() or not np.isfinite(mobility).all(): raise RuntimeError('Invalid pin/free contract: ' + obj.name)
        positions = evaluated_points(obj); edge_indices = np.asarray([tuple(e.vertices) for e in obj.data.edges], dtype=np.int32)
        lengths = np.linalg.norm(positions[edge_indices[:, 0]] - positions[edge_indices[:, 1]], axis=1)
        positive = lengths > .0000001
        pin = obj.vertex_groups.new(name='DIAG_ClothPins'); pin.add(np.nonzero(pinned)[0].tolist(), 1, 'REPLACE')
        soft_pin = np.zeros(len(mobility), dtype=float)
        if args.pin_transition > 0:
            soft_pin = np.maximum(0, 1 - mobility / args.pin_transition) ** 2
            for index in np.nonzero((soft_pin > 0) & (~pinned))[0]: pin.add([int(index)], float(soft_pin[index]), 'REPLACE')
        obj.data.calc_loop_triangles(); triangles = np.asarray([tuple(t.vertices) for t in obj.data.loop_triangles], dtype=np.int32)
        record = {'object': obj, 'rest': positions, 'previous': positions.copy(), 'pinned': pinned, 'pin_group': pin.name,
                  'edges': edge_indices[positive], 'lengths': lengths[positive], 'triangles': triangles,
                  'stats': {'name': obj.name, 'vertices': len(positions), 'pinnedVertices': int(pinned.sum()), 'freeVertices': int((~pinned).sum()),
                            'gradedNearSeamParticles': int(((soft_pin > 0) & (~pinned)).sum()),
                            'components': component_inventory(obj.data, pinned), 'zeroLengthEdges': int((~positive).sum()),
                            'minPositiveEdgeMeters': float(lengths[positive].min()), 'maxPinnedDriftMeters': 0.0, 'maxStretchRatio': 1.0,
                            'nonFiniteValues': 0, 'maxProxyPenetrationMeters': 0.0, 'maxBrushProxyPenetrationMeters': 0.0,
                            'maxFreeProxySamplesInside': 0, 'maxFreeBrushSamplesInside': 0}, 'speeds': deque(maxlen=60), 'motion_samples': deque(maxlen=60)}
        inventory.append(record)
        initial_pin_penetration=penetration_samples(positions[pinned],proxies)
        initial_free_penetration=penetration_samples(positions[~pinned],proxies)
        record['stats']['initialPinnedProxyPenetration']=initial_pin_penetration
        record['stats']['initialFreeProxyPenetration']=initial_free_penetration
        record['modifier'] = add_cloth(obj, pin.name, collection, PHYSICS['maximumFrames'])
        record['stats']['vertexMassKg'] = record['modifier'].settings.mass
        record['stats']['surfaceAreaSquareMetres'] = obj['diagnostic_surface_area_m2']
        record['stats']['totalMassKg'] = record['modifier'].settings.mass * len(obj.data.vertices)
    scene.render.fps = PHYSICS['fps']; scene.gravity = PHYSICS['gravity']; scene.frame_end = PHYSICS['maximumFrames']
    bpy.context.view_layer.update(); scene.frame_set(1)
    if args.inspect_only:
        (case_dir / 'inventory.json').write_text(json.dumps({'source': str(source), 'source_sha256': source_hash,
             'surfaces': [r['stats'] for r in inventory], 'proxyCount': len(proxies)}, indent=2)); return
    started = time.perf_counter(); reset_restored = None; reset_drift = None
    if args.case == 'reset':
        wind = bpy.data.objects.new('DiagnosticWindImpulse', None); scene.collection.objects.link(wind)
        bpy.context.view_layer.objects.active = wind; wind.select_set(True); bpy.ops.object.forcefield_toggle()
        wind.field.type = 'WIND'; wind.rotation_euler = Vector((0, 1, 0)).to_track_quat('Z', 'Y').to_euler(); wind.location = (0, -1, 1)
        for frame in range(2, 62):
            wind.field.strength = 25 if frame < 20 else 0; scene.frame_set(frame)
            for record in inventory: evaluated_points(record['object'])
        impulse_motion = max(float(np.linalg.norm(evaluated_points(r['object']) - r['rest'], axis=1).max()) for r in inventory)
        bpy.data.objects.remove(wind, do_unlink=True)
        for record in inventory: record['object'].modifiers.remove(record['modifier'])
        scene.frame_set(1); bpy.context.view_layer.update()
        for record in inventory: record['modifier'] = add_cloth(record['object'], record['pin_group'], collection, 241)
        bpy.context.view_layer.update(); scene.frame_set(1)
        reset_drift = max(float(np.linalg.norm(evaluated_points(r['object']) - r['rest'], axis=1).max()) for r in inventory)
        reset_restored = reset_drift <= LIMITS['maxPinnedDriftMeters']
    else: impulse_motion = None
    frame_end = 241 if args.case == 'reset' else PHYSICS['maximumFrames']
    snapshots = []; proxy_history = deque(maxlen=60)
    for frame in range(2, frame_end + 1):
        scene.frame_set(frame); sample = {'frame': frame}; frame_proxy = {'body': 0, 'brush': 0}
        for record in inventory:
            positions = evaluated_points(record['object']); stat = record['stats']; finite = np.isfinite(positions)
            stat['nonFiniteValues'] += int((~finite).sum())
            if not finite.all(): raise RuntimeError('Cloth produced non-finite coordinates: ' + stat['name'])
            drift = np.linalg.norm(positions - record['rest'], axis=1)
            stat['maxFreeDisplacementMeters'] = max(stat.get('maxFreeDisplacementMeters',0),float(drift[~record['pinned']].max()))
            stat['maxPinnedDriftMeters'] = max(stat['maxPinnedDriftMeters'], float(drift[record['pinned']].max()))
            edge = record['edges']; ratio = np.linalg.norm(positions[edge[:, 0]] - positions[edge[:, 1]], axis=1) / record['lengths']
            if float(ratio.max()) > stat['maxStretchRatio']:
                index = int(ratio.argmax()); a, b = map(int, edge[index]); stat['maxStretchRatio'] = float(ratio[index])
                stat['worstStretchEdge'] = {'frame': frame, 'vertices': [a, b], 'restLengthMeters': float(record['lengths'][index]),
                    'restPoints': record['rest'][[a, b]].tolist(), 'simulatedPoints': positions[[a, b]].tolist(),
                    'pinned': record['pinned'][[a, b]].tolist()}
            speed = np.linalg.norm(positions - record['previous'], axis=1) * PHYSICS['fps']; record['speeds'].append(float(speed.max()))
            if frame > frame_end - 60 and float(speed.max()) > stat.get('worstFinalVertexSpeed', 0):
                worst_index = int(speed.argmax()); stat['worstFinalVertexSpeed'] = float(speed[worst_index])
                stat['worstFinalSpeedVertex'] = {'vertex': worst_index, 'restPoint': record['rest'][worst_index].tolist(), 'position': positions[worst_index].tolist(), 'frame': frame}
            record['previous'] = positions
            record['motion_samples'].append(positions.astype(np.float32))
            # Proxy coverage includes free vertices and centroids of free triangles;
            # it does not claim exact complete-body triangle intersection coverage.
            free_triangles = record['triangles'][~record['pinned'][record['triangles']].all(axis=1)]
            points = np.concatenate([positions[~record['pinned']], positions[free_triangles].mean(axis=1)])
            penetration = penetration_samples(points, proxies)
            stat['maxProxyPenetrationMeters'] = max(stat['maxProxyPenetrationMeters'], penetration['body']['depth'])
            stat['maxBrushProxyPenetrationMeters'] = max(stat['maxBrushProxyPenetrationMeters'], penetration['brush']['depth'])
            stat['maxFreeProxySamplesInside'] = max(stat['maxFreeProxySamplesInside'], penetration['body']['count'])
            stat['maxFreeBrushSamplesInside'] = max(stat['maxFreeBrushSamplesInside'], penetration['brush']['count'])
            frame_proxy['body'] += penetration['body']['count']
            frame_proxy['brush'] += penetration['brush']['count']
        proxy_history.append(frame_proxy)
        if frame % 30 == 1:
            progress = {'case': args.case, 'frame': frame, 'seconds': (frame - 1) / PHYSICS['fps'], 'wallSeconds': time.perf_counter() - started}
            print(json.dumps(progress), flush=True); snapshots.append(progress)
        if frame >= PHYSICS['minimumFrames'] and args.case != 'reset' and all(max(r['speeds']) <= LIMITS['maxSettledSpeedMetersPerSecond'] for r in inventory):
            break
    actual_frame = frame
    motion_samples_path=case_dir/'settled-motion-samples.npz'
    np.savez_compressed(motion_samples_path,frameNumbers=np.arange(actual_frame-len(inventory[0]['motion_samples'])+1,actual_frame+1),
                        **{r['object'].name:np.asarray(r['motion_samples']) for r in inventory})
    max_final_proxy = {kind: max(frame[kind] for frame in proxy_history) for kind in ['body', 'brush']}
    for record in inventory:
        record['stats']['maxSettledSpeedMetersPerSecond'] = max(record['speeds'])
        samples=np.asarray(record['motion_samples'],dtype=np.float64)
        record['stats']['maxSettledPositionRangeMeters']=float(np.linalg.norm(samples.max(axis=0)-samples.min(axis=0),axis=1).max())
    measurement = {'caseId': args.case, 'solver': 'Blender5.0.1 native Cloth; gravity/body and brush capsule collision; selfCollision=' + str(PHYSICS['selfCollision']),
        'auxiliaryBoneRoles': 'Static Humanoid input precedes Cloth. All sleeve-hem/hat/pack auxiliary bones remain fixed; no secondary solver or production Actions drive free Cloth vertices.',
        'simulatedFrames': actual_frame - 1, 'simulationSeconds': (actual_frame - 1) / PHYSICS['fps'], 'settledSamples': min(len(r['speeds']) for r in inventory),
        'solverSurfaces': len(surfaces), 'semanticClothRegions': 8,
        'pinnedVertices': sum(r['stats']['pinnedVertices'] for r in inventory), 'freeVertices': sum(r['stats']['freeVertices'] for r in inventory),
        'bodyColliders': sum(p['kind'] == 'body' for p in proxies), 'brushColliders': sum(p['kind'] == 'brush' for p in proxies),
        'nonFiniteValues': sum(r['stats']['nonFiniteValues'] for r in inventory), 'maxPinnedDriftMeters': max(r['stats']['maxPinnedDriftMeters'] for r in inventory),
        'maxStretchRatio': max(r['stats']['maxStretchRatio'] for r in inventory), 'maxSettledSpeedMetersPerSecond': max(r['stats']['maxSettledSpeedMetersPerSecond'] for r in inventory),
        'proxyBodyPenetrationSamplesLast60': max_final_proxy['body'], 'proxyBrushPenetrationSamplesLast60': max_final_proxy['brush'],
        'unintendedDoubleDrivenVertices': 0, 'resetRestored': reset_restored, 'resetRestVertexDriftMeters': reset_drift,
        'resetWarmupImpulseMotionMeters': impulse_motion}
    measurement['maxFreeDisplacementMeters']=max(r['stats']['maxFreeDisplacementMeters'] for r in inventory)
    failures = []
    for field, limit in [('maxPinnedDriftMeters', 'maxPinnedDriftMeters'), ('maxStretchRatio', 'maxClothStretchRatio'), ('maxSettledSpeedMetersPerSecond', 'maxSettledSpeedMetersPerSecond')]:
        if measurement[field] > LIMITS[limit]: failures.append(f'{field}={measurement[field]} exceeds {LIMITS[limit]}')
    if max_final_proxy['body'] or max_final_proxy['brush']: failures.append('Free cloth vertex/centroid samples remain inside declared collision proxies.')
    if args.case == 'reset' and not reset_restored: failures.append('Native Cloth reset failed to restore rest input.')
    # Snapshot the evaluated solver output into the derivative only, so reopening
    # and rendering it shows the measured end state without relying on memory cache.
    for record in inventory:
        obj = record['object']; points = evaluated_points(obj); inverse = obj.matrix_world.inverted()
        obj.data = obj.data.copy()
        for modifier in list(obj.modifiers): obj.modifiers.remove(modifier)
        for vertex, point in zip(obj.data.vertices, points): vertex.co = inverse @ Vector(point)
        obj.data.update()
    bpy.context.view_layer.update()
    spec = importlib.util.spec_from_file_location('cloth_render_helpers', Path(__file__).with_name('build_brush.py'))
    render_helpers = importlib.util.module_from_spec(spec); spec.loader.exec_module(render_helpers)
    camera = scene.camera or render_helpers.lighting(scene)
    images = []
    for label, angle in [('front_quarter', math.pi * .15), ('back_quarter', math.pi * .85)]:
        path = case_dir / (label + '.png'); render_helpers.render(scene, camera, path, (0, .05, .88), 2.02, angle, width=1050, height=1350)
        images.append({'path': str(path.relative_to(ROOT)).replace('\\', '/'), 'sha256': digest(path)})
    derivative = case_dir / 'diagnostic.blend'; bpy.ops.wm.save_as_mainfile(filepath=str(derivative))
    source_unchanged = digest(source) == source_hash
    report = {'status': 'MEASURED_FAILURES_REMAIN' if failures else 'PROXY_NUMERICS_PASS_ACTUAL_SURFACE_REVIEW_REQUIRED',
        'recordedAtUtc': datetime.now(timezone.utc).isoformat(), 'source': str(source), 'source_sha256': source_hash, 'source_unchanged': source_unchanged,
        'configuration_sha256': digest(config_path), 'validator_sha256': validator_hash, 'validatorSnapshot': str(validator_snapshot), 'measurement': measurement,
        'settledMotionSamples':{'path':str(motion_samples_path),'sha256':digest(motion_samples_path),'coordinates':'Actual native Cloth output world positions; last60frames; unmodified float32 snapshots.'},
        'surfaceResults': [r['stats'] for r in inventory], 'failureReasons': failures, 'poseDefinitions': pose_records,
        'proxies': [{'name': p['name'], 'kind': p['kind'], 'start': p['a'].tolist(), 'end': p['b'].tolist(), 'radius': p['radius'], 'unfittedRadius': p.get('unfittedRadius', p['radius'])} for p in proxies],
        'images': images, 'derivative_sha256': digest(derivative), 'simulationWallSeconds': time.perf_counter() - started,
        'progress': snapshots, 'actions': len(bpy.data.actions),
        'coverageLimitations': ['Proxy tests sample actual free Cloth vertices and free-triangle centroids against analytic capsules; they do not certify complete body or brush triangle intersections.',
            'Inter-region Cloth-to-Cloth collision is not enabled; self collision is explicitly recorded in the physics configuration; all regions share declared body/brush proxies.',
            'If enabled, pin-clearance collision fitting isolates an impossible fixed-point/capsule conflict. Fitted capsules are diagnostic only and do not by themselves certify coverage of the actual body surface.',
            'The derivative stores the measured solver end geometry for reproducible viewing. Source mesh topology/weights/rest bones and source file remain unchanged.',
            'unresolvedBodyPenetrations/unresolvedBrushPenetrations are intentionally not manufactured as0 for the gate; complete geometry/visual review is still required.']}
    (case_dir / 'cloth-diagnostic.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    if not source_unchanged: raise RuntimeError('Source changed while diagnostic ran; measured snapshot is stale.')
    print(json.dumps({'case': args.case, 'report': str(case_dir / 'cloth-diagnostic.json'), 'measurement': measurement, 'failures': failures}), flush=True)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(); parser.add_argument('--source', default=str(ART / 'DosaV2_Assembled.blend'))
    parser.add_argument('--case', choices=CASES, required=True); parser.add_argument('--inspect-only', action='store_true')
    parser.add_argument('--run-tag', default=''); parser.add_argument('--self-collision', action='store_true')
    parser.add_argument('--pin-transition', type=float, default=0)
    parser.add_argument('--collision-fit-pin-clearance', action='store_true')
    parser.add_argument('--quality', type=int, default=8)
    parser.add_argument('--air-damping', type=float, default=5)
    parser.add_argument('--bending-damping', type=float, default=1)
    parser.add_argument('--areal-density', type=float, default=None)
    parser.add_argument('--body-proxies', default=None)
    parser.add_argument('--bending-model', choices=['ANGULAR','LINEAR'], default='ANGULAR')
    import sys
    args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else [])
    run(args)
