"""Read-only Blender audit of native face winding and open boundary loops."""
import bpy
import hashlib
import json
from collections import defaultdict, Counter
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
BASE = ROOT / "Art/Characters/Folklore298"
rows = []
for ident in ("bulgasari-v2", "fox_spirit-v2"):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    source = BASE / "Source" / ident / "source.glb"
    bpy.ops.import_scene.gltf(filepath=str(source))
    objects = []
    for obj in [o for o in bpy.context.scene.objects if o.type == "MESH"]:
        mesh = obj.data
        positions = [tuple(v.co) for v in mesh.vertices]
        unique = {p: i for i, p in enumerate(dict.fromkeys(positions))}
        canonical = [unique[p] for p in positions]
        coords = list(unique)
        edges = defaultdict(list)
        for poly in mesh.polygons:
            vs = [canonical[v] for v in poly.vertices]
            for a, b in zip(vs, vs[1:] + vs[:1]):
                edges[tuple(sorted((a, b)))].append((poly.index, a, b))
        boundary = [edge for edge, uses in edges.items() if len(uses) == 1]
        bad_winding = [uses for uses in edges.values() if len(uses) == 2 and uses[0][1:] == uses[1][1:]]
        bad_faces = Counter(use[0] for pair in bad_winding for use in pair)
        boundary_graph = defaultdict(set)
        for a, b in boundary:
            boundary_graph[a].add(b); boundary_graph[b].add(a)
        seen, loops = set(), []
        for start in boundary_graph:
            if start in seen:
                continue
            component, todo = [], [start]
            while todo:
                node = todo.pop()
                if node in seen:
                    continue
                seen.add(node); component.append(node)
                todo.extend(boundary_graph[node] - seen)
            points = [coords[i] for i in component]
            loops.append({"vertices": component, "closedDegree2": all(len(boundary_graph[i]) == 2 for i in component),
                          "count": len(component), "min": [min(p[k] for p in points) for k in range(3)],
                          "max": [max(p[k] for p in points) for k in range(3)]})
        suspects = []
        for face, count in bad_faces.items():
            if count < 2:
                continue
            p = mesh.polygons[face]
            suspects.append({"face": face, "sameDirectionNeighbors": count, "vertices": list(p.vertices),
                             "centre": list(p.center), "normal": list(p.normal), "area": p.area})
        objects.append({"name": obj.name, "vertices": len(mesh.vertices), "faces": len(mesh.polygons),
                        "uniquePositions": len(unique), "openEdges": len(boundary), "badWindingEdges": len(bad_winding),
                        "nonmanifoldEdges": sum(len(uses) > 2 for uses in edges.values()), "loops": loops, "suspects": suspects})
    rows.append({"id": ident, "sourceSha256": hashlib.sha256(source.read_bytes()).hexdigest(), "objects": objects})
out = BASE / "Analysis/surface-defect-audit.json"
out.write_text(json.dumps(rows, indent=2) + "\n", encoding="utf-8")
print(json.dumps([{ "id": r["id"], "objects": [{k: v for k, v in o.items() if k not in ("loops", "suspects")} | {"loops": len(o["loops"]), "suspects": len(o["suspects"])} for o in r["objects"]]} for r in rows]))
