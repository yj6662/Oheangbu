"""One bounded hole-fill pass on rig derivatives. Protected Meshy files stay intact."""
import bpy
import json
import sys
from collections import defaultdict
from pathlib import Path
from mathutils import Vector
from mathutils.geometry import tessellate_polygon

sys.path.insert(0, str(Path(__file__).resolve().parent))
import rig_creatures as R

ROOT = Path(__file__).resolve().parents[3]
BASE = ROOT / "Art/Characters/Folklore298"
SELECTED = {"bulgasari": [6], "fox_spirit": [2, 8, 26]}

def repair(ident):
    old = BASE / "Derivatives" / ident / "run-04"
    destination = BASE / "Derivatives" / ident / ("run-07-cleanup" if ident == "fox_spirit" else "run-05-cleanup")
    assert not destination.exists() or not any(destination.iterdir()), "Never overwrite a cleanup result"
    out = R.output_directory(destination, fresh=False)
    source = BASE / "Source" / (ident + "-v2") / "source.glb"
    protected_hash = R.sha(source)
    bpy.ops.wm.open_mainfile(filepath=str(old / "candidate.blend"))
    rig = next(o for o in bpy.context.scene.objects if o.type == "ARMATURE")
    R.assign(rig, None); R.reset(rig); bpy.context.view_layer.update()
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    obj = meshes[0]; mesh = obj.data
    original_group_names = [g.name for g in obj.vertex_groups]
    original_weights = [[(g.group, g.weight) for g in v.groups] for v in mesh.vertices]
    positions = [tuple(v.co) for v in mesh.vertices]
    unique = {p: i for i, p in enumerate(dict.fromkeys(positions))}
    canonical = [unique[p] for p in positions]
    representative = {}
    for i, c in enumerate(canonical):
        representative.setdefault(c, i)
    coords = list(unique)
    edges = defaultdict(list)
    for poly in mesh.polygons:
        vs = [canonical[v] for v in poly.vertices]
        for a, b in zip(vs, vs[1:] + vs[:1]):
            edges[tuple(sorted((a, b)))].append((poly.index, a, b))
    audit = next(r for r in json.loads((BASE / "Analysis/surface-defect-audit.json").read_text()) if r["id"] == ident + "-v2")
    old_faces = [list(p.vertices) for p in mesh.polygons]
    existing_triangles = {tuple(sorted(canonical[v] for v in p.vertices)) for p in mesh.polygons}
    old_uv = [tuple(d.uv) for d in mesh.uv_layers.active.data]
    old_normals = [tuple(n.vector) for n in mesh.corner_normals]
    vertex_normal = defaultdict(list)
    for loop in mesh.loops:
        vertex_normal[canonical[loop.vertex_index]].append(Vector(old_normals[loop.index]))
    added, added_uv, added_normals, fills = [], [], [], []
    for loop_index in SELECTED[ident]:
        record = audit["objects"][0]["loops"][loop_index]
        assert record["closedDegree2"]
        ids = set(record["vertices"])
        selected_edges = {e: uses for e, uses in edges.items() if len(uses) == 1 and e[0] in ids and e[1] in ids}
        graph = defaultdict(set)
        for a, b in selected_edges:
            graph[a].add(b); graph[b].add(a)
        assert len(graph) == len(ids) and all(len(ns) == 2 for ns in graph.values())
        first_use = next(iter(selected_edges.values()))[0]
        start, previous = first_use[2], first_use[1]
        ordered = [start, previous]
        while len(ordered) < len(ids):
            nxt = next(n for n in graph[ordered[-1]] if n != ordered[-2])
            assert nxt not in ordered
            ordered.append(nxt)
        assert start in graph[ordered[-1]]
        vectors = [Vector(coords[c]) for c in ordered]
        reference_faces = {uses[0][0] for uses in selected_edges.values()}
        reference = max((mesh.polygons[i] for i in reference_faces), key=lambda p: p.area)
        rv = [Vector(positions[i]) for i in reference.vertices]
        ru = [Vector(old_uv[i]) for i in reference.loop_indices]
        e1, e2 = rv[1] - rv[0], rv[2] - rv[0]
        d00, d01, d11 = e1.dot(e1), e1.dot(e2), e2.dot(e2)
        denominator = d00 * d11 - d01 * d01
        assert abs(denominator) > 1e-15
        def texture(point):
            delta = point - rv[0]
            a = (d11 * delta.dot(e1) - d01 * delta.dot(e2)) / denominator
            b = (d00 * delta.dot(e2) - d01 * delta.dot(e1)) / denominator
            # Put new UVs on a stable grid finer than 1/100 of a 1k texel.
            # Existing corners are untouched; this avoids FBX float half-boundaries.
            return tuple(round(c, 5) for c in ru[0] + (ru[1] - ru[0]) * a + (ru[2] - ru[0]) * b)
        count_before = len(added)
        for triangle in tessellate_polygon([vectors]):
            triangle = [vectors[p] if isinstance(p, int) else p for p in triangle]
            cs = [unique[tuple(p)] for p in triangle]
            if tuple(sorted(cs)) in existing_triangles:
                continue
            added.append([representative[c] for c in cs])
            added_uv.extend(texture(p) for p in triangle)
            for c in cs:
                n = sum(vertex_normal[c], Vector())
                n.normalize(); added_normals.append(tuple(n))
        fills.append({"auditLoop": loop_index, "boundaryVertexCount": len(ids), "trianglesAdded": len(added) - count_before,
                      "bounds": {"min": record["min"], "max": record["max"]}, "textureReferenceOriginalFace": reference.index})
    repaired = bpy.data.meshes.new(mesh.name + "_BoundedHoleFill298")
    repaired.from_pydata(positions, [], old_faces + added)
    for material in mesh.materials:
        repaired.materials.append(material)
    for i, p in enumerate(repaired.polygons):
        p.material_index = mesh.polygons[i].material_index if i < len(old_faces) else 0
        p.use_smooth = mesh.polygons[i].use_smooth if i < len(old_faces) else True
    uv = repaired.uv_layers.new(name=mesh.uv_layers.active.name)
    for d, tex in zip(uv.data, old_uv + added_uv):
        d.uv = tex
    repaired.normals_split_custom_set(old_normals + added_normals)
    obj.data = repaired
    if not obj.vertex_groups:
        for name in original_group_names:
            obj.vertex_groups.new(name=name)
    for i, groups in enumerate(original_weights):
        for group, weight in groups:
            obj.vertex_groups[group].add([i], weight, "REPLACE")
    assert [tuple(v.co) for v in repaired.vertices] == positions
    assert [list(p.vertices) for p in repaired.polygons[:len(old_faces)]] == old_faces
    assert [tuple(d.uv) for d in repaired.uv_layers.active.data[:len(old_uv)]] == old_uv
    config = json.loads((old / "config-used.json").read_text())
    report = json.loads((old / "rig-report.json").read_text())
    actions = report["actions"]
    original = {o.name: R.world_vertices(o) for o in meshes}
    report["deformation"] = R.validate_deformation(rig, meshes, config, actions, original)
    native = {}
    for o in meshes:
        names = {g.index: g.name for g in o.vertex_groups}
        weights = [{names[g.group]: g.weight for g in v.groups} for v in o.data.vertices]
        native[o.name] = {"positions": original[o.name], "weights": weights, "cornerSignature": R.mesh_corner_signature(o), "uvLayerCount": len(o.data.uv_layers)}
    R.assign(rig, None); R.reset(rig); bpy.context.scene.frame_set(1); bpy.context.view_layer.update()
    bpy.ops.wm.save_as_mainfile(filepath=str(out / "candidate.blend"))
    bpy.ops.object.select_all(action="DESELECT"); rig.select_set(True)
    for o in meshes:
        o.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.export_scene.fbx(filepath=str(out / "creature.fbx"), use_selection=True, object_types={"MESH", "ARMATURE"},
                            use_mesh_modifiers=False, add_leaf_bones=False, use_armature_deform_only=False,
                            bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
                            bake_anim_use_all_bones=False, bake_anim_step=.5, bake_anim_simplify_factor=0,
                            apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS", axis_forward="-Z", axis_up="Y",
                            path_mode="COPY", embed_textures=True)
    roundtrip = R.roundtrip(out / "creature.fbx", native, config, actions, R.bone_definitions(config))
    assert R.sha(source) == protected_hash
    report["cleanup"] = {"policy": "Authorized narrow derived hole repair; original source files preserved", "sourceSha256": protected_hash,
                         "sourceCandidate": old.relative_to(ROOT).as_posix(), "verticesAdded": 0, "verticesMoved": 0,
                         "originalFacesChanged": 0, "originalCornerUvsChanged": 0, "trianglesAdded": len(added), "fills": fills,
                         "faceBefore": len(old_faces), "faceAfter": len(old_faces) + len(added),
                         "newFaceUvs": "Local affine extension of one adjacent original triangle; source texture images unchanged"}
    report["sourceOriginalMeshes"] = report["sourceMeshes"]
    report["geometryPolicy"] = "Protected Meshy source remains byte-identical; only explicitly listed missing boundary faces filled in this derivative. Existing vertices/faces/UVs unchanged."
    report["outputSha256"] = {p.name: R.sha(p) for p in (out / "candidate.blend", out / "creature.fbx")}
    report["actions"] = [{k: value for k, value in clip.items() if not k.startswith("_")} for clip in actions]
    report["status"] = "TECHNICAL_CANDIDATE_PASS_PENDING_VISUAL_AND_UNITY"
    for name in ("config-used.json", "approval-used.json", "builder-used.py"):
        (out / name).write_bytes((old / name).read_bytes())
    (out / "cleanup-builder-used.py").write_bytes(Path(__file__).read_bytes())
    R.save_json(out / "roundtrip.json", roundtrip); R.save_json(out / "rig-report.json", report)
    print(json.dumps({"id": ident, "status": "BOUNDED_CLEANUP_TECHNICAL_PASS", "addedFaces": len(added), "sourceUnchanged": True}), flush=True)

if __name__ == "__main__":
    for ident in sys.argv[sys.argv.index("--") + 1:]:
        repair(ident)
