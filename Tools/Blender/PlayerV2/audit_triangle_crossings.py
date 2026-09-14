"""Actual nonadjacent triangle crossings, kept as unresolved candidates for review.

Does not label an intentional folded/material surface as a defect automatically,
does not create animations, and never issues RIG_PASS. BVH is only broad phase;
proper segment/triangle intersections are independently tested in double precision.
"""
import bpy, json, hashlib, math, argparse, sys
from pathlib import Path
from mathutils import Matrix, Euler
from mathutils.bvhtree import BVHTree
import numpy as np

ROOT = Path(__file__).resolve().parents[3]
ART = ROOT / 'Art/PlayerV2'
p = argparse.ArgumentParser()
p.add_argument('--source', default=str(ART / 'DosaV2_Assembled.blend'))
p.add_argument('--poses', default='rest,combined_reach,hip_flex_right')
p.add_argument('--output', default=str(ART / 'Inspect/TriangleCrossings'))
args = p.parse_args(sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else [])
source = Path(args.source); out = Path(args.output); out.mkdir(parents=True, exist_ok=True)
sha = hashlib.sha256(source.read_bytes()).hexdigest()
bpy.ops.wm.open_mainfile(filepath=str(source))
rig = bpy.data.objects['DosaV2_Rig']
assert not bpy.data.objects['CTRL_DosaV2_Root']['AuthoringMode']
assert len(bpy.data.actions) == 0
defs_path = ART / 'Validation/static-pose-definitions.json'
definitions = json.loads(defs_path.read_text())
assert definitions['sourceSha256'] == sha, 'Regenerate actual pose snapshot before inspection.'
meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH' and
          o.name.startswith(('DosaV2_', 'DosaPackV2_')) and o.name != 'DosaV2_SourceSurface']

def proper_crossings(a, b):
    """Non-coplanar, interior segment/triangle hits; endpoint contacts excluded.
    Inputs are Nx3x3 world triangle coordinates, metres. Return one bool per pair.
    Near-coplanar pairs are not certified here and remain a separate limitation.
    """
    result = np.zeros(len(a), dtype=bool)
    for s, t in ((a, b), (b, a)):
        e1, e2 = t[:, 1] - t[:, 0], t[:, 2] - t[:, 0]
        for edge in range(3):
            start = s[:, edge]; direction = s[:, (edge + 1) % 3] - start
            h = np.cross(direction, e2); determinant = np.einsum('ij,ij->i', e1, h)
            valid = np.abs(determinant) > 1e-14
            inverse = np.zeros(len(s)); inverse[valid] = 1. / determinant[valid]
            offset = start - t[:, 0]
            u = inverse * np.einsum('ij,ij->i', offset, h)
            q = np.cross(offset, e1)
            v = inverse * np.einsum('ij,ij->i', direction, q)
            distance = inverse * np.einsum('ij,ij->i', e2, q)
            # Exclude contacts within 1 micrometre of either segment endpoint.
            endpoint_epsilon = 1e-6 / np.maximum(1e-6, np.linalg.norm(direction, axis=1))
            result |= valid & (u > 1e-7) & (v > 1e-7) & (u + v < 1 - 1e-7) & \
                      (distance > endpoint_epsilon) & (distance < 1 - endpoint_epsilon)
    return result

report = {'status': 'MEASURED_UNRESOLVED_CANDIDATES', 'source': str(source),
          'sourceSha256': sha, 'poseDefinitionsSha256': hashlib.sha256(defs_path.read_bytes()).hexdigest(),
          'toolVersion': bpy.app.version_string, 'productionActions': 0,
          'method': 'Every actual evaluated mesh triangle; BVH overlap broad phase; exclude shared-vertex triangle pairs; double-precision proper edge/triangle intersection narrow phase.',
          'limitations': ['Cross-object contacts are not measured by this self-mesh audit.',
                         'Coplanar overlaps and endpoint/edge-only contacts need separate classification.',
                         'Intentional generated folds/torn edges can intersect. Counts remain unresolved candidates; no gate approval.'],
          'poses': []}
for definition in definitions['poses']:
    if definition['id'] not in args.poses.split(','): continue
    for bone in rig.pose.bones: bone.matrix_basis = Matrix.Identity(4)
    for side in ('Right', 'Left'):
        keys = bpy.data.objects['DosaV2_Hands'].data.shape_keys
        keys.key_blocks['GripPalmRelax_' + side].value = definition.get('handGripCorrectives', {}).get(side.lower(), 0.)
    for entry in definition['boneRotations']:
        rig.pose.bones[entry['bone']].matrix_basis = Euler([math.radians(entry[c]) for c in ('x', 'y', 'z')], 'XYZ').to_matrix().to_4x4()
    bpy.context.view_layer.update(); deps = bpy.context.evaluated_depsgraph_get()
    pose = {'id': definition['id'], 'objects': []}
    for obj in meshes:
        evaluated = obj.evaluated_get(deps); mesh = evaluated.to_mesh(); mesh.calc_loop_triangles()
        coordinates = np.array([list(evaluated.matrix_world @ v.co) for v in mesh.vertices], dtype=np.float64)
        triangles = np.array([list(t.vertices) for t in mesh.loop_triangles], dtype=np.int32)
        tree = BVHTree.FromPolygons(coordinates.tolist(), triangles.tolist(), all_triangles=True, epsilon=0.)
        pairs = [(a, b) for a, b in tree.overlap(tree) if a < b and not set(triangles[a]).intersection(triangles[b])]
        hits = []
        if pairs:
            values = np.array(pairs, dtype=np.int32)
            mask = proper_crossings(coordinates[triangles[values[:, 0]]], coordinates[triangles[values[:, 1]]])
            for a, b in values[mask]:
                hits.append({'triangleA': int(a), 'triangleB': int(b), 'verticesA': triangles[a].tolist(),
                             'verticesB': triangles[b].tolist(), 'center': coordinates[np.r_[triangles[a], triangles[b]]].mean(axis=0).tolist()})
        pose['objects'].append({'name': obj.name, 'vertices': len(coordinates), 'triangles': len(triangles),
                                'broadPhaseNonadjacentPairs': len(pairs), 'properCrossingPairs': len(hits), 'pairs': hits})
        evaluated.to_mesh_clear()
    report['poses'].append(pose)
    (out / 'report.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    print(json.dumps({'pose': definition['id'], 'pairs': sum(o['properCrossingPairs'] for o in pose['objects']),
                      'meshes': [{'name': o['name'], 'pairs': o['properCrossingPairs']} for o in pose['objects'] if o['properCrossingPairs']]}), flush=True)
assert hashlib.sha256(source.read_bytes()).hexdigest() == sha
