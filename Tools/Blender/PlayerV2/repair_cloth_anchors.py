"""Inspect disconnected rag attachment points; optional pin-only derivative repair."""
import argparse
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import bpy
import numpy as np
from mathutils import Matrix, kdtree

ROOT = Path(__file__).resolve().parents[3]
OUT = ROOT / 'Art/PlayerV2/Inspect/ClothBlender/Anchors'


def digest(path): return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def run(args):
    OUT.mkdir(parents=True, exist_ok=True); source = Path(args.source).resolve(); source_hash = digest(source)
    bpy.ops.wm.open_mainfile(filepath=str(source)); rig = bpy.data.objects['DosaV2_Rig']
    for pose in rig.pose.bones: pose.matrix_basis = Matrix.Identity(4)
    bpy.context.view_layer.update()
    surfaces = [o for o in bpy.context.scene.objects if o.type == 'MESH' and o.name.startswith(('DosaV2_Robe_', 'DosaV2_SleeveOuter_'))]
    objects = [o for o in bpy.context.scene.objects if o.type == 'MESH' and not o.hide_render]
    cloud = []; lookup = []
    for obj in objects:
        for vertex in obj.data.vertices: cloud.append(obj.matrix_world @ vertex.co); lookup.append((obj.name, vertex.index))
    tree = kdtree.KDTree(len(cloud))
    for index, point in enumerate(cloud): tree.insert(point, index)
    tree.balance(); reports = []; repaired = []
    for obj in surfaces:
        mesh = obj.data; colors = mesh.color_attributes['ClothMobility']; points = np.array([tuple(obj.matrix_world @ v.co) for v in mesh.vertices])
        pinned = np.array([c.color[0] == 0 for c in colors.data]); adjacency = [[] for _ in mesh.vertices]
        for edge in mesh.edges:
            a, b = edge.vertices; adjacency[a].append(b); adjacency[b].append(a)
        pending = set(range(len(mesh.vertices))); component_id = 0
        while pending:
            seed = min(pending); pending.remove(seed); members = [seed]; stack = [seed]
            while stack:
                for other in adjacency[stack.pop()]:
                    if other in pending: pending.remove(other); members.append(other); stack.append(other)
            if pinned[members].any(): component_id += 1; continue
            member_set = set(members); component = points[members]; minimum, maximum = component.min(axis=0), component.max(axis=0)
            band = min(.012, max(.002, (maximum[2] - minimum[2]) * .20))
            candidates = [i for i in members if points[i, 2] >= maximum[2] - band]
            matches = []
            for index in candidates:
                neighbors = tree.find_n(points[index], min(64, len(cloud)))
                external = [(distance, lookup[j], list(location)) for location, j, distance in neighbors
                            if lookup[j][0] != obj.name or lookup[j][1] not in member_set]
                if external:
                    distance, other, location = min(external)
                    matches.append({'vertex': index, 'point': points[index].tolist(), 'neighborObject': other[0],
                                    'neighborVertex': other[1], 'neighborPoint': location, 'distanceMeters': distance})
            matches.sort(key=lambda value: value['distanceMeters'])
            attachments = [m for m in matches if m['distanceMeters'] <= .02 and
                           m['neighborObject'].startswith(('DosaV2_BodyCore', 'DosaV2_Robe_', 'DosaV2_Sleeve'))]
            # Use a local non-collinear top stitch patch, not the entire free rag.
            selected = []
            if attachments:
                selected.append(attachments[0]['vertex'])
                farthest = max(candidates, key=lambda i: np.linalg.norm(points[i] - points[selected[0]]))
                if farthest not in selected: selected.append(farthest)
                if len(selected) == 2:
                    a, b = points[selected[0]], points[selected[1]]
                    third = max(candidates, key=lambda i: np.linalg.norm(np.cross(b - a, points[i] - a)))
                    if third not in selected and np.linalg.norm(np.cross(b - a, points[third] - a)) > .00000005: selected.append(third)
            record = {'surface': obj.name, 'componentId': component_id, 'members': sorted(members), 'vertices': len(members),
                      'boundsMeters': [minimum.tolist(), maximum.tolist()], 'sizeMeters': (maximum - minimum).tolist(),
                      'topZMeters': float(maximum[2]), 'topBandMeters': band,
                      'nearestTopNeighbors': matches[:8], 'proposedPinVertices': selected,
                      'attachmentEvidence': 'Top band has an original garment vertex within2cm; 2-3 local non-collinear top vertices selected' if attachments else 'No reviewed garment attachment inside2cm at top; unresolved, no automatic pin'}
            reports.append(record)
            if args.repair and len(selected) >= 2 and len(selected) < len(members):
                changes = []
                for index in selected:
                    old = list(colors.data[index].color); new = old.copy(); new[0] = 0; colors.data[index].color = new
                    changes.append({'vertex': index, 'oldColor': old, 'newColor': new})
                repaired.append({'surface': obj.name, 'componentId': component_id, 'changes': changes})
            component_id += 1
    destination = OUT / 'DosaV2_ClothAnchorRepair.blend'
    if args.repair: bpy.ops.wm.save_as_mainfile(filepath=str(destination))
    report = {'status': 'PIN_ONLY_DERIVATIVE_REQUIRES_SOLVER_RETEST' if args.repair else 'ATTACHMENT_INSPECTION',
              'recordedAtUtc': datetime.now(timezone.utc).isoformat(), 'source': str(source), 'source_sha256': source_hash,
              'source_unchanged': digest(source) == source_hash, 'componentsWithoutOriginalPins': reports, 'repairs': repaired,
              'output': str(destination) if args.repair else None, 'output_sha256': digest(destination) if args.repair else None,
              'policy': 'Only ClothMobility red values change in derivative. Geometry, UV, deform skeleton, hands and bone weights are unchanged. No new bridge faces or broad full-rag pinning.'}
    (OUT / ('anchor-repair-report.json' if args.repair else 'attachment-inspection.json')).write_text(json.dumps(report, indent=2), encoding='utf-8')
    print(json.dumps({'unpinnedComponents': len(reports), 'proposed': sum(bool(r['proposedPinVertices']) for r in reports), 'repaired': len(repaired),
                      'summary': [{'surface': r['surface'], 'component': r['componentId'], 'vertices': r['vertices'], 'topZ': r['topZMeters'],
                                   'size': r['sizeMeters'], 'closest': r['nearestTopNeighbors'][:1], 'pins': r['proposedPinVertices']} for r in reports]}))


if __name__ == '__main__':
    import sys
    parser = argparse.ArgumentParser(); parser.add_argument('--source', required=True); parser.add_argument('--repair', action='store_true')
    run(parser.parse_args(sys.argv[sys.argv.index('--') + 1:]))
