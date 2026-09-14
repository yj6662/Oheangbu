# SPEC-WORLD-MAP §8 P1 — 랜드마크 파이프라인 스모크용 더미 .blend 생성(헤드리스).
# 실행: "<blender.exe>" --background --python make_dummy_landmark.py -- [--out <Art/Blender/LM_Dummy.blend>]
# 계약(§4 층 4) 충족 3종: LM_Dummy(6 m 폭 × 4 m 깊이 × 3 m 높이 바위 블록 + z<0 스커트 −0.6 m, 슬롯 InkWorld_Rock)
# · LM_Dummy_col(같은 부피 박스) · LM_Dummy_fp(z=0 6×4 사각 폐다각형). 피벗 = 발자국 중심 (0,0,0). 결정론(seed 고정) — 미감 자산 아님.
import bpy
import bmesh
import os
import random
import sys

NAME = "LM_Dummy"
WIDTH_X = 6.0
DEPTH_Y = 4.0
HEIGHT_Z = 3.0
SKIRT_Z = -0.6
JITTER = 0.18
SEED = 20260906


def parse_out():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if "--out" in argv:
        return argv[argv.index("--out") + 1]
    root = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    return os.path.join(root, "Art", "Blender", NAME + ".blend")


def make_block(name, jitter):
    mesh = bpy.data.meshes.new(name)
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co.x *= WIDTH_X
        v.co.y *= DEPTH_Y
        v.co.z = SKIRT_Z if v.co.z < 0 else HEIGHT_Z
    if jitter > 0:
        bmesh.ops.subdivide_edges(bm, edges=bm.edges[:], cuts=3, use_grid_fill=True)
        rng = random.Random(SEED)
        for v in bm.verts:
            if v.co.z > 0.0:
                # side and top vertices get a small jitter; the skirt ring (z <= 0) stays a clean box
                v.co.x += rng.uniform(-jitter, jitter)
                v.co.y += rng.uniform(-jitter, jitter)
                if abs(v.co.z - HEIGHT_Z) <= 1e-6:
                    v.co.z += rng.uniform(-jitter, jitter)
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    return obj


def make_footprint(name):
    mesh = bpy.data.meshes.new(name)
    hx, hy = WIDTH_X * 0.5, DEPTH_Y * 0.5
    verts = [(-hx, -hy, 0.0), (hx, -hy, 0.0), (hx, hy, 0.0), (-hx, hy, 0.0)]
    edges = [(0, 1), (1, 2), (2, 3), (3, 0)]
    mesh.from_pydata(verts, edges, [(0, 1, 2, 3)])
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    return obj


def main():
    out = parse_out()
    os.makedirs(os.path.dirname(out), exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.unit_settings.system = 'METRIC'
    scene.unit_settings.scale_length = 1.0

    render = make_block(NAME, JITTER)
    mat = bpy.data.materials.new("InkWorld_Rock")
    render.data.materials.append(mat)
    for p in render.data.polygons:
        p.use_smooth = True

    make_block(NAME + "_col", 0.0)
    make_footprint(NAME + "_fp")

    bpy.ops.wm.save_as_mainfile(filepath=out)
    print("DUMMY_OK " + out)


main()
