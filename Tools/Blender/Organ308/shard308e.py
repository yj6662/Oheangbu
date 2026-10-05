# #308 D308-4e — 마석 결정 모양은 속성마다 다르다(오행 산형) + D308-4d 결정 둘레 마석 오염(몸 표면 결) — Blender 5.0 headless.
# Builds on the v3 crystal pipeline (shard308.py, D308-4c): same part-local frame (+Z = crystal axis outward, origin = where the axis
# meets the body surface, +Y = 'up along the body'), same scale semantics (scale k = target visible length / (0.65 x 0.30 m), shared by
# every shape), same buried root band (h -0.35 .. -0.15 of the axis = the v3 root rings), one shared matte material M_Organ308_Shard.
# So a v3 placement (bone, pivot, axis, k) stays valid for every element shape: `prep` measures each shape at the deployed v3 pose (fit).
# Source models are read-only. One Blender process at a time (shard308e_run.py waits for any other blender.exe).
#   blender -b --factory-startup -t 4 --python shard308e.py -- build            5 element crystals -> deploy FBX + Analysis/shard308e_mesh.json
#                                                                                + Review/tiles_d/shape_<Elem>_{side,sunk}.png (6 shapes)
#   blender ... -- prep <entry>     v3 pose re-solved (cross-checked against Analysis/shard308_<entry>.json) -> fit of every shape at that
#                                   pose (Analysis/shard308e_fit_<entry>.json); mapped element != Neutral -> full placement with that shape
#                                   (Analysis/shard308e_<entry>_<Elem>.json); species -> bind-pose body export for the contamination bake
#                                   (Analysis/Contam/<entry>_body.npz + _body.json)
#   blender ... -- render <entry>   review tiles with the mapped crystal + the contamination approximation textures (contam308.py approx)
#   blender ... -- verify           element FBX deploy contract + element rows -> Analysis/shard308e_verify.json
import bpy, bmesh, hashlib, json, math, random, sys
from pathlib import Path
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import organ308_common as C  # noqa: E402
import organ_geo as G  # noqa: E402
import shard308 as S  # noqa: E402  (main() is guarded: importing runs nothing)
import shard308e_data as D  # noqa: E402

S.TILE_DIR = D.TILE_DIR          # S.render_tiles writes here in this process (tiles_c of v3 stay untouched)
L, RR = S.AXIS_LEN, S.RADIUS
VERSION = "v3e (D308-4e: element shapes on the D308-4c frame)"
MESH_JSON = D.ANALYSIS / "shard308e_mesh.json"
MAX_TRIANGLES = S.MAX_TRIANGLES   # 300 [TEST]


# ================================================================ geometry helpers (part-local metres)
def frame_from(t, ref=Vector((1.0, 0.0, 0.0))):
    """(u, v) with u x v = t (rings built with increasing angle run counter-clockwise about t)."""
    t = t.normalized()
    if abs(ref.dot(t)) > 0.95:
        ref = Vector((0.0, 1.0, 0.0))
    u = (ref - t * ref.dot(t)).normalized()
    return u, t.cross(u)


def ring(centre, u, v, pts2d):
    return [centre + u * x + v * y for x, y in pts2d]


def hexsec(rf, sr, sa, flat, tw, rnd, jit=0.035):
    out = []
    for i in range(6):
        ang = math.radians(60.0 * i + sa[i] + tw)
        r = RR * rf * sr[i] * (1.0 + rnd.uniform(-jit, jit))
        out.append((math.cos(ang) * r, math.sin(ang) * r * flat))
    return out


def ngon(n, rf, tw, rnd, flat=1.0, jit=0.025):
    out = []
    for i in range(n):
        ang = math.radians(360.0 * i / n + tw)
        r = RR * rf * (1.0 + rnd.uniform(-jit, jit))
        out.append((math.cos(ang) * r, math.sin(ang) * r * flat))
    return out


def octsec(a, ch, rot):
    a, ch = a * RR, ch * RR
    pts = [(a, a - ch), (a - ch, a), (-(a - ch), a), (-a, a - ch), (-a, -(a - ch)), (-(a - ch), -a), (a - ch, -a), (a, -(a - ch))]
    c, s = math.cos(math.radians(rot)), math.sin(math.radians(rot))
    return [(x * c - y * s, x * s + y * c) for x, y in pts]


def shell(bm, rings_co, apex=None, top_cap=False, bottom_cap=True):
    vs = [[bm.verts.new(p) for p in r] for r in rings_co]
    n = len(vs[0])
    for k in range(len(vs) - 1):
        a, b = vs[k], vs[k + 1]
        for i in range(n):
            j = (i + 1) % n
            bm.faces.new((a[i], a[j], b[j], b[i]))
    if bottom_cap:
        bm.faces.new(list(reversed(vs[0])))
    if apex is not None:
        A = bm.verts.new(apex)
        top = vs[-1]
        for i in range(n):
            bm.faces.new((top[i], top[(i + 1) % n], A))
    elif top_cap:
        bm.faces.new(vs[-1])
    return vs


def root_samples(main):
    """main = [(h, [Vector]*n)] of the trunk shell, bottom first. Designed-buried samples (h -0.33 .. -0.10, ring vertices and edge mids,
    the v3 recipe) + the bottom centre — the same depths shard308.samples uses, so rim gap / buried fraction stay comparable."""
    hs = [h for h, _ in main]
    out = []
    for h in (-0.33, -0.27, -0.21, -0.15, -0.10):
        k = 0
        while k < len(hs) - 2 and h > hs[k + 1]:
            k += 1
        t = (h - hs[k]) / (hs[k + 1] - hs[k])
        a, b = main[k][1], main[k + 1][1]
        n = len(a)
        for i in range(n):
            p = a[i].lerp(b[i], t)
            q = ((a[i] + a[(i + 1) % n]) * 0.5).lerp((b[i] + b[(i + 1) % n]) * 0.5, t)
            f = 0.97 if h < -0.3 else 1.0
            out.extend((p * f, q * f))
    out.append(Vector((0, 0, main[0][0] * L + 0.004)))
    return out


def vis_samples(bm):
    """Visible-section samples: every vertex and edge midpoint at h >= .22 (on the crystal surface) — what must stay out of the body."""
    z0 = 0.22 * L
    pts = [v.co.copy() for v in bm.verts if v.co.z >= z0]
    for e in bm.edges:
        a, b = e.verts[0].co, e.verts[1].co
        if a.z >= z0 and b.z >= z0:
            pts.append((a + b) * 0.5)
    return pts


# ================================================================ the five element shapes (D308-4e). Every shape keeps the EXACT v3 root
# surface below h +0.02 of the axis (v3_root: same rings, same jitter draws), so the buried samples — rim gap, buried fraction — are the
# v3 ones; only what stands above the body differs.
def v3_root():
    """[(h, ring)] — the v3 crystal's root surface: rings h -0.35 / -0.15 (shard308.RINGS, Random(3084) draws) and h +0.02 (on the
    straight v3 facet between -0.15 and +0.08)."""
    rnd = random.Random(3084)
    rings = []
    for h, rf, tw, lean in S.RINGS[:3]:
        c = Vector((lean * RR, 0.0, h * L))
        rings.append((h, ring(c, Vector((1, 0, 0)), Vector((0, 1, 0)), hexsec(rf, S.SIDE_R, S.SIDE_A, S.FLAT_Y, tw, rnd))))
    t = (0.02 - rings[1][0]) / (rings[2][0] - rings[1][0])
    return [rings[0], rings[1], (0.02, [p.lerp(q, t) for p, q in zip(rings[1][1], rings[2][1])])]


def build_wood(rnd):
    # 목: 곧게 뻗은 긴 기둥(가늘고 높다 — 끝 h .86, v3 .65) + 곁가지 결정 둘(기둥 안에서 돋아 비스듬히 뻗는다)
    bm = G.new_bm()
    sr, sa, flat = (1.05, 0.95, 1.02, 0.94, 1.04, 0.97), (0.0, 4.0, -5.0, 3.0, -4.0, 6.0), 0.92
    main = v3_root()
    for h, rf, tw, lean in ((0.08, 0.86, 3.0, 0.0), (0.40, 0.78, 5.0, 0.03), (0.70, 0.72, 7.0, 0.06)):
        c = Vector((lean * RR, 0.0, h * L))
        main.append((h, ring(c, Vector((1, 0, 0)), Vector((0, 1, 0)), hexsec(rf, sr, sa, flat, tw, rnd))))
    shell(bm, [r for _, r in main], apex=Vector((0.10 * RR, 0.02 * RR, 0.86 * L)))
    for h0, az, tilt, length, rf in ((0.20, 140.0, 42.0, 0.62, 0.42), (0.40, -40.0, 36.0, 0.50, 0.34)):
        base = Vector((0.0, 0.0, h0 * L))
        t = Vector((math.sin(math.radians(tilt)) * math.cos(math.radians(az)), math.sin(math.radians(tilt)) * math.sin(math.radians(az)),
                    math.cos(math.radians(tilt))))
        u, v = frame_from(t)
        sec = hexsec(rf, sr, sa, 0.95, az, rnd)
        r0 = ring(base, u, v, sec)
        r1 = ring(base + t * (length * L * 0.58), u, v, [(x * 0.92, y * 0.92) for x, y in sec])
        shell(bm, [r0, r1], apex=base + t * (length * L))
    return bm, main


def build_fire(rnd):
    # 화: 뾰족한 가시 다발 — 납작한 마름모 날 다섯이 뿌리 위에서 불꽃처럼 솟고 끝이 안쪽으로 휜다(가운데 날만 바깥으로 흔들린다)
    bm = G.new_bm()
    main = v3_root()
    shell(bm, [r for _, r in main], apex=Vector((0.0, 0.0, 0.07 * L)))
    blades = ((0.00, 0.00, 20.0, 5.0, 0.80, 0.66, -5.0, 15.0), (0.28, 0.08, 15.0, 22.0, 0.64, 0.56, 13.0, -10.0),
              (-0.08, 0.30, 105.0, 25.0, 0.56, 0.52, 15.0, 12.0), (-0.30, -0.04, 195.0, 20.0, 0.68, 0.58, 11.0, -14.0),
              (0.04, -0.29, 280.0, 28.0, 0.50, 0.48, 17.0, 10.0))

    def direc(az, tilt):
        a, t = math.radians(az), math.radians(tilt)
        return Vector((math.sin(t) * math.cos(a), math.sin(t) * math.sin(a), math.cos(t)))

    for bx, by, az, tilt, H, w, curl, twist in blades:
        b0 = Vector((bx * RR, by * RR, -0.06 * L))
        tang = Vector((-math.sin(math.radians(az)), math.cos(math.radians(az)), 0.0))   # the blade's wide axis runs round the bundle
        pts, rings_co = b0, []
        # base (buried in the root) -> 42 % up the blade -> 33 % more, bending by `curl` -> tip (bent twice): a flame tongue
        for k, (frac, wf, tl) in enumerate(((0.0, 0.75, tilt), (0.42, 1.0, tilt), (0.33, 0.62, tilt - curl))):
            t = direc(az, tl)
            pts = pts + t * (H * L * frac)
            u = (tang - t * tang.dot(t)).normalized()
            v = t.cross(u)
            ang = math.radians(twist * k)
            uu, vv = u * math.cos(ang) + v * math.sin(ang), -u * math.sin(ang) + v * math.cos(ang)
            ww = w * wf * RR
            rings_co.append(ring(pts, uu, vv, [(ww, 0.0), (0.0, ww * 0.5), (-ww, 0.0), (0.0, -ww * 0.5)]))
        tip = pts + direc(az, tilt - 2.0 * curl) * (H * L * 0.25)
        shell(bm, rings_co, apex=tip)
    return bm, main


def build_earth(rnd):
    # 토: 낮고 두툼한 판·입방 덩이 — 모깎은 사각 덩이(윗면이 조금 기운 판) + 비껴 얹힌 작은 층 덩이(판상 결정의 층). v3 뿌리 위에 앉는다
    bm = G.new_bm()
    main = v3_root()
    shell(bm, [r for _, r in main], top_cap=True)
    body = []
    for h, a, ch, rot in ((-0.05, 0.50, 0.14, 0.0), (0.04, 1.10, 0.30, 6.0), (0.30, 1.18, 0.30, 8.0), (0.40, 1.12, 0.34, 9.0)):
        r = ring(Vector((0.03 * RR * max(0.0, h), 0, h * L)), Vector((1, 0, 0)), Vector((0, 1, 0)), octsec(a, ch, rot))
        if h == 0.40:
            r = [p + Vector((0, 0, 0.04 * L * (p.x / (1.12 * RR)))) for p in r]     # 기운 윗면
        body.append(r)
    shell(bm, body, top_cap=True)
    off = Vector((0.38 * RR, -0.22 * RR, 0.0))
    t0 = ring(off + Vector((0, 0, 0.28 * L)), Vector((1, 0, 0)), Vector((0, 1, 0)), octsec(0.60, 0.18, 26.0))
    t1 = ring(off + Vector((0.02 * RR, 0, 0.50 * L)), Vector((1, 0, 0)), Vector((0, 1, 0)), octsec(0.56, 0.20, 28.0))
    shell(bm, [t0, t1], top_cap=True)
    return bm, main


def build_metal(rnd):
    # 금: 둥근 쌍뿔 구슬 — 열 면 고리가 반 칸씩 엇갈려 잔 마름모 면이 된다. 아래 뿔은 v3 뿌리 안으로 묻힌다
    bm = G.new_bm()
    main = v3_root()
    shell(bm, [r for _, r in main], top_cap=True)
    prof = ((-0.05, 0.55), (0.02, 0.72), (0.10, 1.02), (0.20, 1.28), (0.30, 1.40), (0.40, 1.16), (0.50, 0.80), (0.58, 0.42))
    bead = [ring(Vector((0, 0, h * L)), Vector((1, 0, 0)), Vector((0, 1, 0)), ngon(10, rf, 18.0 * (k % 2), rnd, flat=0.94))
            for k, (h, rf) in enumerate(prof)]
    shell(bm, bead, apex=Vector((0.0, 0.0, 0.67 * L)))
    return bm, main


def build_water(rnd):
    # 수: 비틀려 휜 결정 — v3 뿌리에서 육각 단면이 위로 갈수록 비틀리며(누적 92°) 한쪽으로 휘어 끝이 물결처럼 눕는다
    bm = G.new_bm()
    sr, sa, flat = S.SIDE_R, S.SIDE_A, S.FLAT_Y
    main = v3_root()
    defs = ((0.08, 0.95, 14.0, 0.04, 0.00), (0.22, 0.90, 30.0, 0.22, 0.04), (0.35, 0.82, 50.0, 0.55, 0.08), (0.47, 0.70, 72.0, 1.00, 0.10),
            (0.57, 0.54, 92.0, 1.50, 0.08))
    cs = [Vector((lx * RR, ly * RR, h * L)) for h, _, _, lx, ly in defs]
    apex = Vector((2.05 * RR, 0.04 * RR, 0.645 * L))
    u_prev = Vector((1, 0, 0))
    for i, (h, rf, tw, _, _) in enumerate(defs):
        nxt = cs[i + 1] if i + 1 < len(cs) else apex
        prv = cs[i - 1] if i > 0 else Vector((0, 0, 0.02 * L))
        t = (nxt - prv).normalized()
        u = (u_prev - t * u_prev.dot(t)).normalized()            # parallel transport (no spurious roll; the twist is explicit)
        v = t.cross(u)
        u_prev = u
        main.append((h, ring(cs[i], u, v, hexsec(rf, sr, sa, flat, tw, rnd))))
    shell(bm, [r for _, r in main], apex=apex)
    return bm, main


BUILDERS = {"Wood": (build_wood, 3081), "Fire": (build_fire, 3082), "Earth": (build_earth, 3083), "Metal": (build_metal, 3084),
            "Water": (build_water, 3085)}


def element_bm(elem):
    """(bm, info) — info carries the shape's own samples (root / vis) and designed top (top_h) for shard308.measure."""
    if elem == D.NEUTRAL:
        bm, info = S.build_crystal_bm()
        info = dict(info, top_h=S.TOP_H, shells=1)
        return bm, info
    fn, seed = BUILDERS[elem]
    bm, main = fn(random.Random(seed))
    G.finish(bm)
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    bm.normal_update()
    info = dict(root=root_samples(main), vis=vis_samples(bm), top_h=max(v.co.z for v in bm.verts) / L)
    return bm, info


def element_object(elem, color=None):
    bm, info = element_bm(elem)
    tris = len(bm.faces)
    name = D.mesh_name(elem)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    for p in me.polygons:
        p.use_smooth = False
    G.project_uv(me)
    mat = C.flat_material(S.MATERIAL_NAME, color or S.lin(S.BASE_SRGB), metallic=S.METALLIC, roughness=1.0 - S.SMOOTHNESS)
    me.materials.append(mat)
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    info["shells"] = S.islands(me)
    return ob, tris, info


def islands_rooted(me):
    """Every piece grows out of the crystal: the island holding the lowest vertex is the trunk/root; every other island's lowest vertex
    must sit INSIDE another piece (6 axis rays against that piece alone, first hits are back faces) — no floating shard."""
    parent = list(range(len(me.vertices)))

    def find(x):
        while parent[x] != x:
            parent[x] = parent[parent[x]]
            x = parent[x]
        return x
    for e in me.edges:
        a, b = find(e.vertices[0]), find(e.vertices[1])
        if a != b:
            parent[a] = b
    low = {}
    for v in me.vertices:
        r = find(v.index)
        if r not in low or v.co.z < low[r].z:
            low[r] = v.co.copy()
    if len(low) <= 1:
        return True
    trunk = min(low, key=lambda r: low[r].z)       # the island holding the lowest vertex = the root / trunk
    verts = [v.co for v in me.vertices]
    bvhs = {r: BVHTree.FromPolygons(verts, [list(pp.vertices) for pp in me.polygons if find(pp.vertices[0]) == r]) for r in low}

    def inside(p, bvh):
        back = 0
        for d in (Vector((1, 0, 0)), Vector((-1, 0, 0)), Vector((0, 1, 0)), Vector((0, -1, 0)), Vector((0, 0, 1)), Vector((0, 0, -1))):
            loc, nrm, _, _ = bvh.ray_cast(p + d * 1e-5, d, 1.0)
            if loc is not None and nrm.dot(d) > 0:
                back += 1
        return back >= 5
    ok = True
    for r, p in low.items():
        if r != trunk:
            ok &= any(inside(p, bvhs[o]) for o in low if o != r)
    return ok


# ================================================================ build
def cmd_build():
    rows = {}
    C.reset()
    for elem in D.SHAPES:
        ob, tris, info = element_object(elem)
        if tris > MAX_TRIANGLES:
            raise RuntimeError("%s has %d triangles (> %d)" % (elem, tris, MAX_TRIANGLES))
        me = ob.data
        uc, us = S.unity_bounds(me)
        zs = [v.co.z for v in me.vertices]
        grows = islands_rooted(me)
        fbx = D.SHARD_DEPLOY / (D.mesh_name(elem) + ".fbx")
        if elem != D.NEUTRAL:
            S.export_fbx(ob, fbx)          # the neutral FBX stays the v3 file (shard308.py build) — never rewritten here
        rows[elem] = dict(element=elem, name=D.mesh_name(elem), label=D.KO[elem], triangles=tris, vertices=len(me.vertices),
                          islands=info["shells"], islandsGrowFromRootOrTrunk=grows, topH=round(info["top_h"], 4),
                          designedVisibleAtScale1M=round(info["top_h"] * L, 4), rootBottomM=round(-min(zs), 4),
                          unityMeshBoundsCenter=S.r6(uc), unityMeshBoundsSize=S.r6(us), maxExtent=round(max(us), 6),
                          rootSamples=len(info.get("root") or []), visSamples=len(info.get("vis") or []),
                          fbx=str(fbx.relative_to(D.ROOT)).replace("\\", "/"), unityFbx=D.UNITY_SHARD + "/" + D.mesh_name(elem) + ".fbx",
                          sha256=hashlib.sha256(fbx.read_bytes()).hexdigest() if fbx.exists() else None)
        bpy.data.objects.remove(ob)
    doc = dict(tool="Tools/Blender/Organ308/shard308e.py build", version=VERSION, blender=bpy.app.version_string, units="metres",
               axisLength=L, radius=RR, buriedFraction=S.BURY, maxTriangles=MAX_TRIANGLES, material=S.MATERIAL_NAME,
               frame="same as v3: Unity mesh-local +Y = crystal axis (outward), -Z = 'up along the body', origin = where the axis meets the "
                     "body surface; root rings h -0.35 / -0.15 of the axis = the v3 root (shard308.RINGS), so the buried part and the "
                     "scale semantics (k = target visible / (0.65 x 0.30 m)) are shared by every shape",
               shapes=rows)
    D.ANALYSIS.mkdir(parents=True, exist_ok=True)
    MESH_JSON.write_text(json.dumps(doc, ensure_ascii=False, indent=1), encoding="utf-8")
    # review: the six shapes at one scale — side (whole, with the root) and sunk into a body block (what stays visible), 3/4 view
    D.TILE_DIR.mkdir(parents=True, exist_ok=True)
    C.reset()
    C.setup_render(372, 372)
    centre, rad = Vector((0, 0, 0.26 * L)), 0.62 * 1.25 * L
    for elem in D.SHAPES:
        ob, _, _ = element_object(elem)
        C.render_view(D.TILE_DIR / ("shape_%s_side.png" % elem), centre, rad, 20, 6, front=Vector((0, -1, 0)))
        blk = S.body_block(0.0, RR * 3.6, 0.35 * L + 0.03)
        C.render_view(D.TILE_DIR / ("shape_%s_sunk.png" % elem), Vector((0, 0, 0.30 * L)), rad * 0.86, 40, 24, front=Vector((0, -1, 0)))
        bpy.data.objects.remove(blk)
        bpy.data.objects.remove(ob)
    print("ORGAN308E_BUILD " + json.dumps({k: dict(tris=v["triangles"], islands=v["islands"], topH=v["topH"], grows=v["islandsGrowFromRootOrTrunk"])
                                           for k, v in rows.items()}, ensure_ascii=False))


def trunk_vertices(me):
    """Vertex indices of the island holding the lowest vertex (the root / trunk — the crystal's outer buried surface)."""
    parent = list(range(len(me.vertices)))

    def find(x):
        while parent[x] != x:
            parent[x] = parent[parent[x]]
            x = parent[x]
        return x
    for e in me.edges:
        a, b = find(e.vertices[0]), find(e.vertices[1])
        if a != b:
            parent[a] = b
    lowest = min(me.vertices, key=lambda v: v.co.z).index
    r = find(lowest)
    return {v.index for v in me.vertices if find(v.index) == r}


def trunk_gap(c, ob, n, reach):
    """shard308.Ctx.base_gap (the v2 metric) on the trunk island only: pieces that start inside the root (blades, the bead's lower cone,
    the earth block's foot) are hidden by the crystal itself and never float. For the one-island v3 crystal this IS base_gap."""
    keep = trunk_vertices(ob.data)
    gap = 0.0
    for v in ob.data.vertices:
        if v.index not in keep or v.co.z > -0.002:
            continue
        w = ob.matrix_world @ v.co
        loc, nrm, _, dist = c.bvh.ray_cast(w, -n, reach)
        if loc is not None and nrm.dot(n) > 0.0:
            gap = max(gap, dist * c.s)
    return gap


# ================================================================ pose solve (= shard308.cmd_place search, factored; deterministic)
def solve(c, e, ob, info):
    p0, n0, up_hint = e["place"](c)
    k = e["visible"] / (S.TOP_H * L)                         # scale semantics shared by every shape (v3)
    rim_r = RR * k
    n = c.wide_normal(p0, n0, rim_r / c.s)
    trials = []
    for tilt, turn in [(e["tilt"], t) for t in (0, 45, -45, 90, -90, 180)] + [(e["tilt"] * 0.5, 0), (0.0, 0)]:
        a, y = S.tilt_axis(n, up_hint, tilt, turn)
        ob.matrix_world = S.pose_matrix(p0, G.frame(a, y), k, c)
        m = S.measure(c, ob, info, p0, a, k)
        trials.append((tilt, turn, m))
        if m["visibleHiddenSamples"] == 0 and not m["tipInside"]:
            break
    tilt, turn, _ = min(trials, key=lambda t: (t[2]["visibleHiddenSamples"], t[2]["tipInside"], trials.index(t)))
    a, y = S.tilt_axis(n, up_hint, tilt, turn)
    Ma = G.frame(a, y)
    chosen, history = None, []
    for step in [0] + [v for i in range(1, 11) for v in (i, -i)]:
        s = step * 0.005 * L * k
        origin = p0 - n * (s / c.s)
        ob.matrix_world = S.pose_matrix(origin, Ma, k, c)
        m = S.measure(c, ob, info, origin, a, k)
        g2 = trunk_gap(c, ob, n, L * k / c.s)
        history.append(dict(sinkM=round(s, 4), rimGapM=m["rimGapM"], gapV2M=round(g2, 4), buried=m["buriedFraction"]))
        if max(m["rimGapM"], g2) <= S.GAP_TOL and S.BURY_BAND[0] <= m["buriedFraction"] <= S.BURY_BAND[1] and not m["tipInside"]:
            chosen = (s, origin, m)
            break
    if chosen is None:
        inband = [h for h in history if S.BURY_BAND[0] <= h["buried"] <= S.BURY_BAND[1]] or history[:1]
        s = min(inband, key=lambda h: max(h["rimGapM"], h["gapV2M"]))["sinkM"]
        origin = p0 - n * (s / c.s)
        ob.matrix_world = S.pose_matrix(origin, Ma, k, c)
        chosen = (s, origin, S.measure(c, ob, info, origin, a, k))
    sink, origin, meas = chosen
    ob.matrix_world = S.pose_matrix(origin, Ma, k, c)
    return dict(p0=p0, n=n, up=up_hint, k=k, a=a, y=y, Ma=Ma, tilt=tilt, turn=turn, sink=sink, origin=origin, meas=meas,
                history=history, trials=trials)


def overhang(c, ob, n):
    """Largest clearance (m) under the visible base: vertices just above the surface plane (0 <= z <= .12 L) that stand off the body
    along -n more than they are designed to (a wide flat base on a curved body floats at its edges)."""
    worst = 0.0
    for v in ob.data.vertices:
        if not (0.0 <= v.co.z <= 0.12 * L):
            continue
        w = ob.matrix_world @ v.co
        loc, nrm, _, dist = c.bvh.ray_cast(w, -n, 0.5 / c.s)
        if loc is not None and nrm.dot(n) > 0.0:
            worst = max(worst, dist * c.s - v.co.z * (ob.matrix_world.to_scale()[0] * c.s))
    return round(max(0.0, worst), 4)


def bone_local_pivot(row_pivot, sol):
    return (Vector(row_pivot) - Vector(sol)).length


# ================================================================ prep (fit + element placement + body export)
def cmd_prep(entry):
    e = S.ENTRIES[entry]
    emap = D.element_map()[entry]
    mapped = emap["element"]
    row_v3 = json.loads((D.ANALYSIS / ("shard308_%s.json" % entry)).read_text(encoding="utf-8"))
    c = S.Ctx(entry)
    ob, _, info = element_object(D.NEUTRAL)
    sol = solve(c, e, ob, info)
    # cross-check: the re-solved v3 pose is the deployed one (same bone-local pivot / rotation as the v3 analysis row)
    weights = None if c.tree else c.dominant_bone(sol["origin"], RR * sol["k"] / c.s)
    chk = S.placement(c, e, ob, sol["origin"], sol["a"], sol["n"], sol["Ma"], sol["k"], e["bone"], weights, dict(sol["meas"], acT5={}))
    piv_err = (Vector(chk["unity"]["pivotLocalPosition"]) - Vector(row_v3["unity"]["pivotLocalPosition"])).length
    rot_err = S.QM.q_angle_deg(tuple(chk["unity"]["localRotation"]), tuple(row_v3["unity"]["localRotation"]))
    v3pose = dict(pivotErrBoneLocal=round(piv_err, 7), rotationErrDeg=round(rot_err, 5), same=piv_err < 1e-5 and rot_err < 1e-3)
    bpy.data.objects.remove(ob)
    # fit: every shape at the deployed v3 pose (same pivot, axis, k)
    fit = {}
    for elem in D.SHAPES:
        obe, tris, infe = element_object(elem)
        obe.matrix_world = S.pose_matrix(sol["origin"], sol["Ma"], sol["k"], c)
        m = S.measure(c, obe, infe, sol["origin"], sol["a"], sol["k"])
        designed = infe["top_h"] * L * sol["k"]
        fit[elem] = dict(triangles=tris, rimGapM=m["rimGapM"], baseGapV2M=round(trunk_gap(c, obe, sol["n"], L * sol["k"] / c.s), 4),
                         buriedFraction=m["buriedFraction"], visibleLengthM=m["visibleLengthM"], designedVisibleM=round(designed, 4),
                         visibleHiddenSamples=m["visibleHiddenSamples"], visibleSamples=m["visibleSamples"],
                         visibleInsideMaxM=m["visibleInsideMaxM"], tipInside=m["tipInside"], overhangM=overhang(c, obe, sol["n"]))
        fit[elem]["ok"] = (fit[elem]["rimGapM"] <= S.GAP_TOL and fit[elem]["baseGapV2M"] <= S.GAP_TOL and not m["tipInside"]
                           and m["visibleHiddenSamples"] == 0)
        bpy.data.objects.remove(obe)
    out = dict(tool="shard308e.py prep", entry=entry, owner=e["owner"], mappedElement=mapped, elementSource=emap, v3Pose=v3pose,
               poseFromV3=dict(tiltDeg=sol["tilt"], turnDeg=sol["turn"], sinkM=round(sol["sink"], 4), k=round(sol["k"], 6)),
               rule="fit ok = rim gap <= 5 mm (both metrics), tip outside, 0 visible-section samples inside the body; overhangM = base clearance (info)",
               fit=fit)
    (D.ANALYSIS / ("shard308e_fit_%s.json" % entry)).write_text(json.dumps(out, ensure_ascii=False, indent=1), encoding="utf-8")
    # mapped element shape: the full v3 placement procedure with that shape (its row replaces the neutral row in the deploy)
    pivot_sol = sol
    if mapped != D.NEUTRAL:
        obm, _, infm = element_object(mapped)
        solm = solve(c, e, obm, infm)
        vis_c = solm["origin"] + solm["a"] * (0.5 * infm["top_h"] * L * solm["k"] / c.s)
        px = {}
        for tag, yaw, dist in (("front12", 0, 12.0), ("left45_12", 45, 12.0), ("right45_12", -45, 12.0), ("front8", 0, 8.0), ("front4", 0, 4.0)):
            cnt, w, hh = S.pixel_view(c, obm, vis_c, yaw, dist)
            px[tag] = dict(pixels=cnt, widthPx=w, heightPx=hh)
        meas = dict(solm["meas"], sinkM=round(solm["sink"], 4), sinkHistory=solm["history"], tiltTurnDeg=solm["turn"], tiltUsedDeg=solm["tilt"],
                    tiltTrials=[dict(tilt=t, turn=tu, hidden=m["visibleHiddenSamples"], tipInside=m["tipInside"]) for t, tu, m in solm["trials"]],
                    acT5=dict(rule="as v3 (shard308.py place)", exempt=e["role"] == "Spine", views=px,
                              min12=min(px[t]["pixels"] for t in ("front12", "left45_12", "right45_12"))))
        wts = None if c.tree else c.dominant_bone(solm["origin"], RR * solm["k"] / c.s)
        row = S.placement(c, e, obm, solm["origin"], solm["a"], solm["n"], solm["Ma"], solm["k"], e["bone"], wts, meas)
        row.update(element=mapped, mesh=D.mesh_name(mapped), version=VERSION, targetVisibleDesignedM=round(infm["top_h"] * L * solm["k"], 4))
        S.render_tiles(c, obm, e, "%s_%s" % (entry, mapped))
        (D.ANALYSIS / ("shard308e_%s_%s.json" % (entry, mapped))).write_text(json.dumps(row, ensure_ascii=False, indent=1), encoding="utf-8")
        pivot_sol = solm
        bpy.data.objects.remove(obm)
        print("ORGAN308E_PLACED " + json.dumps(dict(entry=entry, element=mapped, visible=meas["visibleLengthM"], buried=meas["buriedFraction"],
                                                     rimGap=meas["rimGapM"], hidden=meas["visibleHiddenSamples"], min12=meas["acT5"]["min12"]),
                                                ensure_ascii=False))
    if not c.tree:
        export_body(c, entry, pivot_sol, mapped, row_v3)
    print("ORGAN308E_PREP " + json.dumps(dict(entry=entry, mapped=mapped, v3pose=v3pose["same"],
                                               fit={k: (v["ok"], v["visibleHiddenSamples"], v["overhangM"]) for k, v in fit.items()}), ensure_ascii=False))


def export_body(c, entry, sol, mapped, row_v3):
    """Bind-pose skin (species space) for the geodesic contamination bake: positions, triangles, per-corner UV0, the crystal pivot."""
    import numpy as np
    dg = bpy.context.evaluated_depsgraph_get()
    P, T, UV, NRM, OBJ = [], [], [], [], []
    base = 0
    uv_layers = {}
    for m in c.meshes:
        ev = m.evaluated_get(dg)
        me = ev.to_mesh()
        mw = ev.matrix_world
        me.calc_loop_triangles()
        uvl = me.uv_layers[0] if len(me.uv_layers) else None
        uv_layers[m.name] = [l.name for l in me.uv_layers]
        P.extend(tuple(mw @ v.co) for v in me.vertices)
        nm = mw.to_3x3().inverted().transposed()
        NRM.extend(tuple((nm @ v.normal).normalized()) for v in me.vertices)
        for lt in me.loop_triangles:
            T.append((lt.vertices[0] + base, lt.vertices[1] + base, lt.vertices[2] + base))
            UV.append([tuple(uvl.data[li].uv) if uvl else (-1.0, -1.0) for li in lt.loops])
            OBJ.append(c.meshes.index(m))
        base += len(me.vertices)
        ev.to_mesh_clear()
    loc, nrm, _, dist = c.bvh.find_nearest(sol["origin"])
    D.CONTAM_DIR.mkdir(parents=True, exist_ok=True)
    np.savez_compressed(D.CONTAM_DIR / ("%s_body.npz" % entry), P=np.array(P, dtype=np.float64), T=np.array(T, dtype=np.int64),
                        UV=np.array(UV, dtype=np.float64), N=np.array(NRM, dtype=np.float64), OBJ=np.array(OBJ, dtype=np.int64),
                        pivot=np.array(tuple(sol["origin"])), source=np.array(tuple(loc)), axis=np.array(tuple(sol["a"])),
                        normal=np.array(tuple(sol["n"])))
    meta = dict(entry=entry, mapped=mapped, scale=c.s, k=sol["k"], meshes=[m.name for m in c.meshes], uvLayers=uv_layers,
                vertices=len(P), triangles=len(T), sourceDistM=round(dist * c.s, 5), visibleLengthM=row_v3["check"]["visibleLengthM"],
                bone=row_v3["bone"]["name"], note="species space (Blender, bind pose); metres = units x scale")
    (D.CONTAM_DIR / ("%s_body.json" % entry)).write_text(json.dumps(meta, ensure_ascii=False, indent=1), encoding="utf-8")


# ================================================================ review render (Workbench; approximation of the in-game look)
def image_material(name, path=None, rgba=None):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    tex = nt.nodes.new("ShaderNodeTexImage")
    if path is not None:
        tex.image = bpy.data.images.load(str(path), check_existing=False)
    else:
        img = bpy.data.images.new(name + "_px", 4, 4)
        img.pixels = list(rgba) * 16
        tex.image = img
    nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    nt.nodes.active = tex
    for key in ("Emission Color",):
        if key in bsdf.inputs:
            bsdf.inputs[key].default_value = (0, 0, 0, 1)
    if "Emission Strength" in bsdf.inputs:
        bsdf.inputs["Emission Strength"].default_value = 0.0
    m.diffuse_color = rgba if rgba is not None else (0.8, 0.8, 0.8, 1)
    return m, tex


def render_dir(path, centre, radius, d):
    cam = C._camera()
    cam.data.type = 'ORTHO'
    cam.data.ortho_scale = 2.0 * radius
    cam.data.clip_start = radius * 0.01
    cam.data.clip_end = radius * 40
    d = d.normalized()
    cam.location = centre + d * radius * 8
    cam.rotation_euler = (-d).to_track_quat('-Z', 'Z' if abs(d.z) < 0.95 else 'Y').to_euler()
    bpy.context.scene.render.filepath = str(path)
    bpy.ops.render.render(write_still=True)


def srgb_to_lin(c):
    return tuple((x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4) for x in c)


def cmd_render(entry):
    e = S.ENTRIES[entry]
    mapped = D.element_map()[entry]["element"]
    rep_path = D.CONTAM_DIR / ("%s_bake.json" % entry)
    rep = json.loads(rep_path.read_text(encoding="utf-8")) if rep_path.exists() else None
    c = S.Ctx(entry)
    ob, _, info = element_object(mapped)
    sol = solve(c, e, ob, info)
    D.TILE_DIR.mkdir(parents=True, exist_ok=True)
    C.setup_render(372, 372)
    bpy.context.scene.display.shading.color_type = 'TEXTURE'
    idle = D.CONTAM_DIR / ("%s_approx_idle.png" % entry)
    tele = D.CONTAM_DIR / ("%s_approx_tele.png" % entry)
    has = (not c.tree) and idle.exists() and tele.exists()
    leaf_m, _ = image_material("Review_Leaf", rgba=(0.70, 0.76, 0.66, 1))
    if has:
        body_m, body_tex = image_material("Review_BodyContam", idle)
    else:
        body_m, body_tex = image_material("Review_Body", rgba=(0.80, 0.78, 0.74, 1))
    for m in c.meshes:
        m.data.materials.clear()
        m.data.materials.append(leaf_m if (c.tree and m.name != "Tree_00_sub2") else body_m)
    crystal = tuple(int(S.BASE_SRGB[i:i + 2], 16) / 255.0 for i in (1, 3, 5))       # byte image pixels are sRGB-encoded
    cr_m, cr_tex = image_material("Review_Crystal", rgba=(*crystal, 1.0))
    ob.data.materials.clear()
    ob.data.materials.append(cr_m)
    if entry == "imugi":
        centre, radius = Vector((0.0, -0.36, 0.0)), 0.17
    elif c.tree:
        centre, radius = (c.lo + c.hi) * 0.5, 0.58 * max(c.hi - c.lo)
    else:
        centre, radius = (c.lo + c.hi) * 0.5, 0.56 * max(c.hi - c.lo)
    C.render_view(D.TILE_DIR / ("%s_d_front.png" % entry), centre, radius, e["az"] * 0.5, 6, front=c.front)
    # close-up: around the crystal, wide enough for the contamination radius; camera between the crystal axis and the actor front
    contam_r = (rep["radiusM"] if rep else 0.0) / c.s
    world = [ob.matrix_world @ v.co for v in ob.data.vertices]
    lo, hi = C.bounds(world)
    pc = sol["origin"].lerp((lo + hi) * 0.5, 0.35)
    pr = max(contam_r * 1.12, max(hi - lo) * 1.6, 0.22 / c.s)
    side = c.front.cross(Vector((0, 0, 1))).normalized()
    d = (sol["a"] * 0.45 + c.front * 0.60 + side * (0.35 if e["az"] >= 0 else -0.35) + Vector((0, 0, 0.15)))
    render_dir(D.TILE_DIR / ("%s_d_close_idle.png" % entry), pc, pr, d)
    if has:
        body_tex.image = bpy.data.images.load(str(tele), check_existing=False)
        demo = mapped if mapped != D.NEUTRAL else "Fire"
        tint = D.PALETTE[demo]
        mix = tuple(crystal[i] * 0.45 + tint[i] * 0.55 for i in range(3))       # organ overlay core ~ tint over the crystal (approx.)
        img = cr_tex.image
        img.pixels = list((*mix, 1.0)) * 16
        render_dir(D.TILE_DIR / ("%s_d_close_tele.png" % entry), pc, pr, d)
    print("ORGAN308E_RENDER " + json.dumps(dict(entry=entry, mapped=mapped, contamination=has, closeRadius=round(pr * c.s, 3)), ensure_ascii=False))


# ================================================================ verify (element deploy contract)
def cmd_verify():
    mesh = json.loads(MESH_JSON.read_text(encoding="utf-8"))
    emap = D.element_map()
    ck = {}
    for elem in D.ELEMENTS:
        row = mesh["shapes"][elem]
        fbx = D.SHARD_DEPLOY / (D.mesh_name(elem) + ".fbx")
        C.reset()
        bpy.ops.import_scene.fbx(filepath=str(fbx))
        obs = [o for o in bpy.context.scene.objects if o.type == 'MESH']
        me = obs[0].data if len(obs) == 1 else None
        if me is not None:
            me.calc_loop_triangles()
        tag = elem
        ck[tag + ":oneObject"] = len(obs) == 1
        ck[tag + ":names"] = me is not None and obs[0].name == D.mesh_name(elem) and me.name == D.mesh_name(elem)
        ck[tag + ":triangles<=%d" % MAX_TRIANGLES] = me is not None and len(me.loop_triangles) <= MAX_TRIANGLES and len(me.loop_triangles) == row["triangles"]
        ck[tag + ":singleSharedMaterial"] = me is not None and [m.name for m in me.materials] == [S.MATERIAL_NAME]
        ck[tag + ":flatFacets"] = me is not None and not any(p.use_smooth for p in me.polygons)
        ck[tag + ":uv0"] = me is not None and len(me.uv_layers) >= 1
        if me is not None:
            lo, hi = C.bounds([obs[0].matrix_world @ v.co for v in me.vertices])
            uc, us = Vector(row["unityMeshBoundsCenter"]), Vector(row["unityMeshBoundsSize"])
            ck[tag + ":boundsMatch(axis map)"] = ((lo + hi) * 0.5 - Vector((-uc.x, -uc.z, uc.y))).length < 1e-4 and \
                ((hi - lo) - Vector((us.x, us.z, us.y))).length < 1e-4
        ck[tag + ":sha256"] = hashlib.sha256(fbx.read_bytes()).hexdigest() == row["sha256"]
        ck[tag + ":islandsGrowFromRootOrTrunk"] = row["islandsGrowFromRootOrTrunk"]
        ck[tag + ":rootBottomSameAsV3"] = abs(row["rootBottomM"] - 0.35 * L) < 0.002
    # fits at the v3 poses (every shape on every spot) and the mapped-element rows
    fits = {}
    for entry in D.ENTRIES:
        p = D.ANALYSIS / ("shard308e_fit_%s.json" % entry)
        if not p.exists():
            ck["fit:%s present" % entry] = False
            continue
        f = json.loads(p.read_text(encoding="utf-8"))
        ck["fit:%s v3 pose re-solved" % entry] = f["v3Pose"]["same"]
        ck["fit:%s neutral ok" % entry] = f["fit"][D.NEUTRAL]["ok"]
        fits[entry] = {k: v["ok"] for k, v in f["fit"].items()}
        mapped = emap[entry]["element"]
        if mapped != D.NEUTRAL:
            rp = D.ANALYSIS / ("shard308e_%s_%s.json" % (entry, mapped))
            ok = rp.exists()
            if ok:
                r = json.loads(rp.read_text(encoding="utf-8"))
                ck2 = r["check"]
                ok = (ck2["rimGapM"] <= S.GAP_TOL and ck2["baseGapM"] <= S.GAP_TOL and S.BURY_BAND[0] - 1e-4 <= ck2["buriedFraction"] <= S.BURY_BAND[1] + 1e-4
                      and not ck2["tipInside"] and ck2["visibleLengthM"] >= 0.9 * r["targetVisibleM"]
                      and (r["role"] == "Spine" or ck2["acT5"]["min12"] >= S.AC_T5_PX))
            ck["row:%s %s embedded + visible + AC-T5" % (entry, mapped)] = ok
    fails = [k for k, v in ck.items() if not v]
    out = dict(tool="Tools/Blender/Organ308/shard308e.py verify", version=VERSION, blender=bpy.app.version_string, checks=ck,
               failures=len(fails), fitMatrix=fits, elementMap={k: v["element"] for k, v in emap.items()})
    (D.ANALYSIS / "shard308e_verify.json").write_text(json.dumps(out, ensure_ascii=False, indent=1), encoding="utf-8")
    print("ORGAN308E_VERIFY failures=%d %s" % (len(fails), json.dumps(fails, ensure_ascii=False)))


def main():
    args = sys.argv[sys.argv.index("--") + 1:]
    if args[0] == "build":
        cmd_build()
    elif args[0] == "prep":
        cmd_prep(args[1])
    elif args[0] == "render":
        cmd_render(args[1])
    elif args[0] == "verify":
        cmd_verify()
    else:
        raise SystemExit("unknown command " + args[0])


if __name__ == "__main__":
    main()
