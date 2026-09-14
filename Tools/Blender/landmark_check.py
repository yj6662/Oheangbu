# SPEC-WORLD-MAP §4 층 4 — Blender 랜드마크 계약 헤드리스 검사·내보내기(D8).
# 실행: "<blender.exe>" --background <LM_Name.blend> --python landmark_check.py -- --out <dir> [--skirt-min -0.5]
# 규칙 전수(단위·Apply·3종 존재·피벗·스커트·트라이·콜라이더·매니폴드·ngon·슬롯 이름·UV/정점색 부재)를 검사하고,
# 하나라도 FAIL이면 FBX를 쓰지 않고 <Name>.report.json만 남긴 뒤 exit 1. PASS면 FBX 옵션 고정으로 내보내고
# <Name>.footprint.json{polygon, polygonFlat, groundOffset, contractVersion, tris, sha256} + <Name>.report.json을 쓴다.
# 좌표 규약: polygon = Blender XY 그대로(Unity XZ 대응은 임포트 실측으로 확정 — §8 P0 FBX 스케일 조합 실측 항목).
import bpy
import bmesh
import hashlib
import json
import os
import sys
from mathutils import Vector

CONTRACT_VERSION = 1
RENDER_TRI_MAX = 8000
COLLIDER_TRI_MAX = 2000
NGON_MAX = 4
PIVOT_TOL = 0.01
PLANAR_TOL = 0.01
APPLY_TOL = 1e-4
SKIRT_MIN_DEFAULT = -0.5
MATERIAL_SLOTS = ("InkWorld_Rock", "InkWorld_Wood")
FBX_OPTIONS = dict(
    axis_forward='-Z', axis_up='Y',
    apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS',
    bake_space_transform=True, use_mesh_modifiers=True,
    mesh_smooth_type='OFF', path_mode='STRIP',
    object_types={'MESH'}, add_leaf_bones=False,
)


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    out_dir = None
    skirt_min = SKIRT_MIN_DEFAULT
    i = 0
    while i < len(argv):
        if argv[i] == "--out" and i + 1 < len(argv):
            out_dir = argv[i + 1]
            i += 2
        elif argv[i] == "--skirt-min" and i + 1 < len(argv):
            skirt_min = float(argv[i + 1])
            i += 2
        else:
            i += 1
    if out_dir is None:
        blend = bpy.data.filepath
        out_dir = os.path.join(os.path.dirname(blend) if blend else os.getcwd(), "_out")
    return out_dir, skirt_min


class Report:
    def __init__(self):
        self.checks = []

    def add(self, check_id, ok, detail=""):
        self.checks.append({"id": check_id, "ok": bool(ok), "detail": detail})
        return ok

    @property
    def passed(self):
        return all(c["ok"] for c in self.checks)


def evaluated_mesh(obj):
    depsgraph = bpy.context.evaluated_depsgraph_get()
    eval_obj = obj.evaluated_get(depsgraph)
    return eval_obj.to_mesh()


def tri_count(obj):
    mesh = evaluated_mesh(obj)
    mesh.calc_loop_triangles()
    n = len(mesh.loop_triangles)
    obj.evaluated_get(bpy.context.evaluated_depsgraph_get()).to_mesh_clear()
    return n


def transform_applied(obj):
    s = obj.scale
    r = obj.rotation_euler
    return (abs(s.x - 1.0) <= APPLY_TOL and abs(s.y - 1.0) <= APPLY_TOL and abs(s.z - 1.0) <= APPLY_TOL
            and abs(r.x) <= APPLY_TOL and abs(r.y) <= APPLY_TOL and abs(r.z) <= APPLY_TOL)


def manifold_and_ngon(obj):
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    non_manifold = sum(1 for e in bm.edges if not e.is_manifold)
    ngons = sum(1 for f in bm.faces if len(f.verts) > NGON_MAX)
    bm.free()
    return non_manifold, ngons


def footprint_loop(obj):
    """Return (ordered world-space vertex list, error) for a single closed planar loop."""
    mesh = obj.data
    verts = mesh.vertices
    edges = mesh.edges
    if len(verts) < 3:
        return None, "vertices < 3"
    if len(edges) != len(verts):
        return None, "edges %d != vertices %d (single closed loop expected)" % (len(edges), len(verts))
    if len(mesh.polygons) > 1:
        return None, "faces %d > 1" % len(mesh.polygons)
    adjacency = {i: [] for i in range(len(verts))}
    for e in edges:
        a, b = e.vertices
        adjacency[a].append(b)
        adjacency[b].append(a)
    for i, links in adjacency.items():
        if len(links) != 2:
            return None, "vertex %d has %d edges (loop needs exactly 2)" % (i, len(links))
    order = [0]
    prev, cur = None, 0
    while True:
        nxt = adjacency[cur][0] if adjacency[cur][0] != prev else adjacency[cur][1]
        if nxt == 0:
            break
        if nxt in order:
            return None, "loop is not simple"
        order.append(nxt)
        prev, cur = cur, nxt
    if len(order) != len(verts):
        return None, "loop does not visit every vertex (%d/%d) — disconnected" % (len(order), len(verts))
    mat = obj.matrix_world
    return [mat @ verts[i].co for i in order], None


def sha256_of(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def main():
    out_dir, skirt_min = parse_args()
    os.makedirs(out_dir, exist_ok=True)
    scene = bpy.context.scene
    report = Report()

    blend_path = bpy.data.filepath
    name = os.path.splitext(os.path.basename(blend_path))[0] if blend_path else "LM_Unknown"

    render = bpy.data.objects.get(name)
    if render is None or render.type != 'MESH':
        # fall back: any LM_* mesh that is not _col/_fp/UCX_
        for o in bpy.data.objects:
            if o.type == 'MESH' and o.name.startswith("LM_") and not (o.name.endswith("_col") or o.name.endswith("_fp") or o.name.startswith("UCX_")):
                render = o
                name = o.name
                break
    report.add("render_object", render is not None, "LM_<Name> mesh object = %s" % (render.name if render else "MISSING"))
    if render is None:
        return finish(report, out_dir, name, None, None, skirt_min)

    col_objects = [o for o in bpy.data.objects if o.type == 'MESH' and (o.name == name + "_col" or o.name.startswith("UCX_"))]
    fp = bpy.data.objects.get(name + "_fp")

    report.add("unit_system", scene.unit_settings.system == 'METRIC' and abs(scene.unit_settings.scale_length - 1.0) <= APPLY_TOL,
               "system=%s scale_length=%s" % (scene.unit_settings.system, scene.unit_settings.scale_length))
    report.add("collider_object", len(col_objects) > 0, "%s" % [o.name for o in col_objects])
    report.add("footprint_object", fp is not None and fp.type == 'MESH', name + "_fp")

    for o in [render] + col_objects + ([fp] if fp else []):
        report.add("transform_applied:" + o.name, transform_applied(o), "scale=%s rot=%s" % (tuple(round(v, 5) for v in o.scale), tuple(round(v, 5) for v in o.rotation_euler)))

    r_tris = tri_count(render)
    report.add("render_tris", r_tris <= RENDER_TRI_MAX, "%d <= %d" % (r_tris, RENDER_TRI_MAX))
    c_tris = sum(tri_count(o) for o in col_objects)
    report.add("collider_tris", c_tris <= COLLIDER_TRI_MAX, "%d <= %d" % (c_tris, COLLIDER_TRI_MAX))

    non_manifold, ngons = manifold_and_ngon(render)
    report.add("manifold", non_manifold == 0, "non-manifold edges = %d" % non_manifold)
    report.add("ngon", ngons == 0, "faces with > %d verts = %d" % (NGON_MAX, ngons))
    for o in col_objects:
        nm, ng = manifold_and_ngon(o)
        report.add("collider_manifold:" + o.name, nm == 0, "non-manifold edges = %d" % nm)

    slots = [s.material.name if s.material else "" for s in render.material_slots]
    report.add("material_slot", len(slots) == 1 and slots[0] in MATERIAL_SLOTS, "slots=%s (expected 1 of %s)" % (slots, list(MATERIAL_SLOTS)))
    report.add("no_uv", len(render.data.uv_layers) == 0, "uv_layers=%d" % len(render.data.uv_layers))
    report.add("no_vertex_color", len(render.data.color_attributes) == 0, "color_attributes=%d" % len(render.data.color_attributes))

    min_z = min((render.matrix_world @ v.co).z for v in render.data.vertices)
    report.add("skirt", min_z <= skirt_min, "min z %.3f <= %.3f" % (min_z, skirt_min))

    polygon = None
    ground_offset = 0.0
    if fp is not None and fp.type == 'MESH':
        loop, err = footprint_loop(fp)
        report.add("footprint_loop", loop is not None, err or "%d vertices, single closed loop" % len(loop))
        if loop:
            zs = [v.z for v in loop]
            ground_offset = sum(zs) / len(zs)
            planar = all(abs(z - ground_offset) <= PLANAR_TOL for z in zs)
            report.add("footprint_planar", planar and abs(ground_offset) <= PLANAR_TOL, "z mean %.4f, spread %.4f" % (ground_offset, max(zs) - min(zs)))
            cx = (min(v.x for v in loop) + max(v.x for v in loop)) * 0.5
            cy = (min(v.y for v in loop) + max(v.y for v in loop)) * 0.5
            loc = render.matrix_world.translation
            pivot_ok = abs(loc.x - cx) <= PIVOT_TOL and abs(loc.y - cy) <= PIVOT_TOL and abs(loc.z) <= PIVOT_TOL
            report.add("pivot", pivot_ok, "pivot=(%.3f, %.3f, %.3f) footprint center=(%.3f, %.3f)" % (loc.x, loc.y, loc.z, cx, cy))
            for o in col_objects + [fp]:
                l2 = o.matrix_world.translation
                report.add("pivot_shared:" + o.name, (l2 - loc).length <= PIVOT_TOL, "offset %.4f" % (l2 - loc).length)
            polygon = [[round(v.x, 4), round(v.y, 4)] for v in loop]

    return finish(report, out_dir, name, render, col_objects, skirt_min, polygon, ground_offset, r_tris)


def finish(report, out_dir, name, render, col_objects, skirt_min, polygon=None, ground_offset=0.0, tris=0):
    result = {
        "name": name,
        "result": "PASS" if report.passed else "FAIL",
        "contractVersion": CONTRACT_VERSION,
        "blender": bpy.app.version_string,
        "blend": bpy.data.filepath,
        "skirtMin": skirt_min,
        "tris": tris,
        "checks": report.checks,
    }
    report_path = os.path.join(out_dir, name + ".report.json")
    if not report.passed:
        with open(report_path, "w", encoding="utf-8", newline="\n") as f:
            json.dump(result, f, ensure_ascii=False, indent=1)
        print("LANDMARK_FAIL " + name)
        for c in report.checks:
            if not c["ok"]:
                print("  FAIL %s: %s" % (c["id"], c["detail"]))
        sys.exit(1)

    fbx_path = os.path.join(out_dir, name + ".fbx")
    bpy.ops.object.select_all(action='DESELECT')
    for o in [render] + list(col_objects):
        o.select_set(True)
    bpy.context.view_layer.objects.active = render
    bpy.ops.export_scene.fbx(filepath=fbx_path, use_selection=True, **FBX_OPTIONS)
    digest = sha256_of(fbx_path)

    dims = [round(v, 4) for v in render.dimensions]
    # 좌표 규약 주의(SPEC-WORLD-MAP §4 층 4 · §13):
    #  - polygon 은 Blender 월드 XY 를 그대로 쓴다. Unity XZ 로의 축 대응(부호·순서 — FBX axis_forward='-Z'/axis_up='Y'
    #    + bake_space_transform 조합)은 P0 FBX 임포트 실측(§13)이 끝날 때까지 **미실측** — 임포터 쪽(LandmarkMeshPostprocessor)
    #    에서 뒤집기/스왑을 가정하지 말 것. 실측 결과는 axisMap 문자열과 CONTRACT_VERSION 갱신으로 박는다.
    #  - groundOffset 은 발자국 루프 정점 z 의 평균이다. 그런데 footprint_planar 검사가 |mean z| <= PLANAR_TOL 을 요구하므로
    #    PASS 산출물에서는 사실상 항상 ≈0 으로 강제된다(_fp 루프는 z≈0 에 놓여야 통과). 즉 SO.groundOffset 은 현재
    #    "평균 z" 이상의 정보를 갖지 않는다 — 접지 목표를 z≠0 에 두려면 계약(§4 층 4) 문답 후 planar 검사부터 바꿔야 한다.
    footprint = {
        "name": name,
        "polygon": polygon,
        "polygonFlat": [c for p in polygon for c in p],
        "groundOffset": round(ground_offset, 4),
        "contractVersion": CONTRACT_VERSION,
        "tris": tris,
        "sha256": digest,
        "axisMap": "polygon = Blender XY (Unity XZ 대응은 임포트 실측으로 확정)",
    }
    with open(os.path.join(out_dir, name + ".footprint.json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump(footprint, f, ensure_ascii=False, indent=1)
    result.update({"fbx": fbx_path, "sha256": digest, "fbxBytes": os.path.getsize(fbx_path), "dimensions_m": dims,
                   "fbxOptions": {k: (sorted(v) if isinstance(v, set) else v) for k, v in FBX_OPTIONS.items()}})
    with open(report_path, "w", encoding="utf-8", newline="\n") as f:
        json.dump(result, f, ensure_ascii=False, indent=1)
    print("LANDMARK_PASS " + name + " tris=%d fbx=%s sha256=%s" % (tris, fbx_path, digest))
    sys.exit(0)


main()
