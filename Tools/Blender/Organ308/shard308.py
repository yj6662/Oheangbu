# #308 D308-4c — 기관 부위 = 공통 마석 결정 하나(one large magic-stone crystal), 더 크게·깊게 박는다. ONE matte raw-ore mesh for every
# enemy; per species only the bone, the place, the size and a slight tilt (DECISIONS D308-4c, D308-4b; SPEC-TELEGRAPH-ORGAN-308 overlay
# contract unchanged). v2 (D308-4b, 8 small crystals on a rock bed) is kept in Art/Characters/Organ308/Retired/Shard308_v2/.
# Blender 5.0 headless, one process at a time (shard308_run.py waits for any other blender.exe). Source models are read-only.
#   blender -b --factory-startup -t 4 --python shard308.py -- build          crystal mesh -> deploy FBX + Analysis/shard308_mesh.json
#                                                                            + 3 review tiles of the crystal alone
#   blender ... -- place <entry>    place the crystal on one species bind pose -> Analysis/shard308_<entry>.json + 3 review tiles
#   blender ... -- verify           re-import the deploy FBX and check the deploy contract -> Analysis/shard308_verify.json
#   entry = dokkaebi | agwi | changgui | bulgasari | fox_spirit | imugi | growth_tree
# Conventions (same as v1/v2, measured there): Unity prefab-root = VisualScale * (-x, z, -y) + (0, VisualY, 0); Unity bone frame =
# R * BlenderBoneRest * diag(-1,1,1); part FBX exported axis_forward=-Z axis_up=Y, FBX_SCALE_UNITS, bake_space_transform, metres,
# so Unity mesh-local = (-x, z, -y) of the Blender part-local vertex (Unity +Y = the crystal axis, outward from the body).
import bpy, bmesh, hashlib, json, math, random, sys
from pathlib import Path
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import organ308_common as C  # noqa: E402
import organ_geo as G  # noqa: E402
import unity_prefab as UP  # noqa: E402
import unity_mesh as UM  # noqa: E402
import unity_math as QM  # noqa: E402

R = Matrix(((-1, 0, 0), (0, 0, 1), (0, -1, 0)))
D = Matrix(((-1, 0, 0), (0, 1, 0), (0, 0, 1)))
FRONT = Vector((0, -1, 0))  # Unity actor +Z
UP_Z = Vector((0, 0, 1))
HIGHLIGHT = (0.85, 0.30, 0.12, 1.0)  # 리뷰 강조 전용(실제 재질 아님)

ORGAN_ID = "maseok_shard"
SHARD_NAME = "SM_Organ308_Shard"   # same FBX / mesh / material names as v2 -> Unity GUIDs and prefab references stay
MATERIAL_NAME = "M_Organ308_Shard"
VERSION = "v3 (D308-4c: one large crystal)"
# 마석 원석: ArtAudio ART-INK "날것 마석 빛은 정제 전 원석에만, 탁하게" + 오염 무발광 → 무광, 탁한 먹빛 청록 회색. v3 is a touch darker than
# v2 #3B4744 (a single big crystal covers more screen; darker keeps it ink, and the overlay's paper ring / element core read on it). TEST.
BASE_SRGB = "#35413E"
SMOOTHNESS = 0.15
METALLIC = 0.0
MAX_TRIANGLES = 300  # D308-4c task budget [TEST] (v2 cluster: 600)

DEPLOY_DIR = C.OUT / "_ProjectAssets/Art/Characters/Organs308/Shard308"
DEPLOY_FBX = DEPLOY_DIR / (SHARD_NAME + ".fbx")
UNITY_DIR = "Assets/_Project/Art/Characters/Organs308/Shard308"
ANALYSIS = C.OUT / "Analysis"
MESH_JSON = ANALYSIS / "shard308_mesh.json"
TILE_DIR = C.OUT / "Review" / "tiles_c"
TREE_MESH = C.ASSETS / "Art/SpellVFX120/Botanical/Meshes/Mesh_Tree.asset"
TREE_WORLD_SCALE = 2.8 * 1.0714285  # Tree_00 local 2.8 x Chapter3_Visual 1.0714285 (same in the three ledger scenes, v1 read)
TREE_YAW_DEG = -18.0
HUMAN_OF = {"Hips": "Hips", "Spine02": "Spine", "Spine01": "Chest", "Spine": "UpperChest", "neck": "Neck", "Head": "Head",
            "LeftShoulder": "LeftShoulder", "RightShoulder": "RightShoulder"}

# ================================================================ the common crystal (part-local metres; +Z = crystal axis, outward;
# origin = where the axis meets the body surface; +Y = 'up along the body' of the placement frame). One hexagonal prism, irregular
# (wide and narrow faces like natural quartz), slightly leaning, with a chisel (two-apex ridge) termination. The lower BURY of the axis
# is a tapered root that sits inside the body, so the crystal reads as growing out of the flesh (no rim, no bed). All TEST.
AXIS_LEN = 0.30           # bottom of the root -> highest apex
BURY = 0.35               # root fraction of the axis below the surface (target band .30-.40 after placement)
BURY_BAND = (0.30, 0.40)
RADIUS = 0.050            # nominal prism radius (diameter ~10 cm at scale 1)
SIDES = 6
SIDE_R = (1.18, 0.82, 1.08, 0.78, 1.12, 0.90)   # irregular hexagon: wide and narrow faces (natural quartz habit)
SIDE_A = (0.0, 7.0, -8.0, 5.0, -6.0, 9.0)        # deg
FLAT_Y = 0.86
# rings: (h = z / AXIS_LEN, radius factor, twist deg, lean = x offset in RADIUS units)
RINGS = ((-0.35, 0.78, 0.0, 0.00),    # root bottom (tapered: stays inside convex bodies)
         (-0.15, 0.93, 2.0, 0.00),
         (0.08, 1.00, 4.0, 0.02),     # just above the surface
         (0.30, 1.02, 7.0, 0.08),     # slight barrel, the shaft starts to lean
         (0.44, 0.95, 10.0, 0.15))    # shoulder: the termination starts
APEX = ((0.65, 0.42), (0.57, -0.12))  # chisel ridge, well off the axis: (h, offset along the ridge direction in RADIUS units)
RIDGE_AZ = 15.0
TOP_H = APEX[0][0]
GAP_TOL = 0.005           # rim gap target (m)
VISIBLE_TOL = 0.005       # depth (m) a visible-section sample may sit inside the body before it counts as hidden

# AC-T5 analytic check: Unity camera 1920x1080, vertical fov 60°, horizontal, at the crystal's height
SCREEN_W, SCREEN_H, FOV_V = 1920, 1080, 60.0
AC_T5_PX = 12


def lin(hexstr):
    h = hexstr.lstrip("#")
    out = []
    for i in (0, 2, 4):
        c = int(h[i:i + 2], 16) / 255.0
        out.append(c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4)
    return (out[0], out[1], out[2], 1.0)


def r6(v):
    return [round(float(c), 6) for c in v]


def build_crystal_bm():
    rnd = random.Random(3084)
    bm = G.new_bm()
    L, Rr = AXIS_LEN, RADIUS
    rings, ring_co = [], []
    for h, rf, tw, lean in RINGS:
        c = Vector((lean * Rr, 0.0, h * L))
        ring, co = [], []
        for i in range(SIDES):
            ang = math.radians(60.0 * i + SIDE_A[i] + tw)
            r = Rr * rf * SIDE_R[i] * (1.0 + rnd.uniform(-0.035, 0.035))
            p = c + Vector((math.cos(ang) * r, math.sin(ang) * r * FLAT_Y, 0.0))
            ring.append(bm.verts.new(p))
            co.append(p.copy())
        rings.append(ring)
        ring_co.append(co)
    for k in range(len(rings) - 1):
        a, b = rings[k], rings[k + 1]
        for i in range(SIDES):
            j = (i + 1) % SIDES
            bm.faces.new((a[i], a[j], b[j], b[i]))
    bm.faces.new(list(reversed(rings[0])))
    # chisel termination: the six shoulder edges climb to two apexes (a short ridge, off the axis) — raw, not a cut gem
    top = rings[-1]
    tc = Vector((RINGS[-1][3] * Rr, 0.0, 0.0))
    u = Vector((math.cos(math.radians(RIDGE_AZ)), math.sin(math.radians(RIDGE_AZ)), 0.0))
    apex_co = [tc + u * (off * Rr) + Vector((0, 0, h * L)) for h, off in APEX]
    A, B = bm.verts.new(apex_co[0]), bm.verts.new(apex_co[1])
    own = []
    for i in range(SIDES):
        m = (top[i].co + top[(i + 1) % SIDES].co) * 0.5 - tc
        own.append(A if m.x * u.x + m.y * u.y >= 0.0 else B)
    for i in range(SIDES):
        j = (i + 1) % SIDES
        bm.faces.new((top[i], top[j], own[i]))
        if own[i] is not own[j]:
            bm.faces.new((top[j], own[j], own[i]))
    G.finish(bm)
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    bm.normal_update()
    return bm, dict(rings=ring_co, apex=apex_co)


def ring_point(info, side, h, mid=False):
    """Point on the crystal's lateral surface at axis height h (fraction of AXIS_LEN): on the vertical edge `side`, or (mid) halfway
    to the next edge. Linear along the ring polyline (part-local)."""
    rings = info["rings"]
    hs = [r[0] for r in RINGS]
    h = max(hs[0], min(hs[-1], h))
    k = 0
    while k < len(hs) - 2 and h > hs[k + 1]:
        k += 1
    t = (h - hs[k]) / (hs[k + 1] - hs[k])
    j = (side + 1) % SIDES

    def at(ring):
        p = ring[side]
        return (p + ring[j]) * 0.5 if mid else p
    return at(rings[k]).lerp(at(rings[k + 1]), t)


def samples(info):
    """root = designed-buried samples (depth >= .10 L); visible = lateral samples h >= .22 plus points just under the two apexes.
    D308-4e element crystals (shard308e.py) bring their own sample lists (info["root"], info["vis"])."""
    if info.get("root") is not None:
        return info["root"], info["vis"]
    root, vis = [], []
    for s in range(SIDES):
        for mid in (False, True):
            for h in (-0.33, -0.27, -0.21, -0.15, -0.10):
                root.append(ring_point(info, s, h, mid) * (0.97 if h < -0.3 else 1.0))
            for h in (0.22, 0.30, 0.38, 0.46):
                vis.append(ring_point(info, s, h, mid))
    root.append(Vector((0, 0, RINGS[0][0] * AXIS_LEN + 0.004)))
    for p in info["apex"]:
        vis.append(p - Vector((0, 0, 0.004)))
    return root, vis


def shard_object(color):
    bm, info = build_crystal_bm()
    tris = len(bm.faces)
    me = bpy.data.meshes.new(SHARD_NAME)
    bm.to_mesh(me)
    bm.free()
    for p in me.polygons:
        p.use_smooth = False  # faceted (crisp facets read in ink wash)
    G.project_uv(me)
    mat = C.flat_material(MATERIAL_NAME, color, metallic=METALLIC, roughness=1.0 - SMOOTHNESS)
    me.materials.append(mat)
    ob = bpy.data.objects.new(SHARD_NAME, me)
    bpy.context.scene.collection.objects.link(ob)
    return ob, tris, mat, info


def unity_bounds(me):
    lo, hi = C.bounds([R @ v.co for v in me.vertices])
    return (lo + hi) * 0.5, hi - lo


def islands(me):
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
    return len({find(i) for i in range(len(me.vertices))})


def export_fbx(ob, path):
    path.parent.mkdir(parents=True, exist_ok=True)
    keep = ob.matrix_world.copy()
    ob.matrix_world = Matrix.Identity(4)
    for o in bpy.context.scene.objects:
        o.select_set(False)
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.export_scene.fbx(filepath=str(path), use_selection=True, object_types={'MESH'},
                             axis_forward='-Z', axis_up='Y', global_scale=1.0, apply_unit_scale=True,
                             apply_scale_options='FBX_SCALE_UNITS', bake_space_transform=True,
                             mesh_smooth_type='FACE', use_mesh_modifiers=True, add_leaf_bones=False,
                             bake_anim=False, path_mode='STRIP', use_tspace=False)
    ob.matrix_world = keep


def body_block(z_top, half, depth):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0, matrix=Matrix.Translation((0, 0, z_top - depth * 0.5)) @
                          Matrix.Diagonal((half * 2, half * 2, depth, 1)))
    me = bpy.data.meshes.new("Review_BodyBlock")
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new("Review_BodyBlock", me)
    bpy.context.scene.collection.objects.link(ob)
    ob.data.materials.append(C.flat_material("Review_Body", (0.80, 0.78, 0.74, 1)))
    return ob


def cmd_build():
    C.reset()
    ob, tris, mat, info = shard_object(lin(BASE_SRGB))
    if tris > MAX_TRIANGLES:
        raise RuntimeError("crystal has %d triangles (> %d)" % (tris, MAX_TRIANGLES))
    export_fbx(ob, DEPLOY_FBX)
    uc, us = unity_bounds(ob.data)
    loc = [v.co for v in ob.data.vertices]
    above = max(v.z for v in loc)
    below = -min(v.z for v in loc)
    data = dict(tool="Tools/Blender/Organ308/shard308.py build", version=VERSION, blender=bpy.app.version_string, name=SHARD_NAME,
                fbx=str(DEPLOY_FBX.relative_to(C.ROOT)).replace("\\", "/"), unityFbx=UNITY_DIR + "/" + SHARD_NAME + ".fbx",
                triangles=tris, vertices=len(loc), maxTriangles=MAX_TRIANGLES, units="metres", islands=islands(ob.data),
                unityMeshBoundsCenter=r6(uc), unityMeshBoundsSize=r6(us), maxExtent=round(max(us), 6),
                axisLength=AXIS_LEN, buriedFraction=BURY, radius=RADIUS, sides=SIDES,
                protrusionAboveSurface=round(above, 6), sunkBelowSurface=round(below, 6),
                protrusionRatio=round(above / (above + below), 4),
                material=dict(name=MATERIAL_NAME, shader="Universal Render Pipeline/Lit", baseColorSRGB=BASE_SRGB,
                              metallic=METALLIC, smoothness=SMOOTHNESS, emission=False, emissionColor="#000000"),
                crystals=1, sha256=hashlib.sha256(DEPLOY_FBX.read_bytes()).hexdigest(),
                axes="Unity mesh-local +Y = crystal axis (outward from the body), -Z = 'up along the body', origin = where the axis "
                     "meets the body surface; the lower %d %% of the axis is the buried root" % round(BURY * 100))
    ANALYSIS.mkdir(parents=True, exist_ok=True)
    MESH_JSON.write_text(json.dumps(data, ensure_ascii=False, indent=1), encoding="utf-8")
    # review: the crystal alone (whole, with its root), then sunk into a body block (what stays visible), then down the axis
    TILE_DIR.mkdir(parents=True, exist_ok=True)
    C.setup_render(372, 372)
    zc = (above - below) * 0.5
    rad = (above + below) * 0.62
    C.render_view(TILE_DIR / "shard_front.png", Vector((0, 0, zc)), rad, 20, 6, front=Vector((0, -1, 0)))
    blk = body_block(0.0, RADIUS * 3.2, below + 0.03)
    C.render_view(TILE_DIR / "shard_q34.png", Vector((0, 0, above * 0.42)), rad * 0.82, 40, 24, front=Vector((0, -1, 0)))
    bpy.data.objects.remove(blk)
    C.render_view(TILE_DIR / "shard_close.png", Vector((0, 0, zc)), RADIUS * 2.0, 0, 89, front=Vector((0, -1, 0)))
    print("ORGAN308_SHARD " + json.dumps(dict(triangles=tris, size=r6(us), protrusion=round(above, 4), islands=data["islands"]),
                                          ensure_ascii=False))


# ================================================================ species context (copied from the retired v1 build_organs.Ctx)
class Ctx:
    def __init__(self, entry):
        self.entry = entry
        self.tree = entry == "growth_tree"
        if self.tree:
            self._load_tree()
        else:
            self.arm, self.meshes = C.import_species(entry)
            self.prefab = UP.Prefab(C.species_prefab(entry))
            vis = [f for f in self.prefab.tf if self.prefab.path(f) == "Visual"][0]
            v = self.prefab.tf[vis]
            self.s = v["s"][0]
            self.t = Vector(v["p"])
            self.front = FRONT.copy()
        self.bvh = self._bvh()
        self.lo, self.hi = C.bounds(self._verts)
        self.far = 2.5 * max(self.hi - self.lo)

    def _load_tree(self):
        C.reset()
        m = UM.read_mesh(TREE_MESH)
        k = TREE_WORLD_SCALE
        verts = [Vector((-p[0], -p[2], p[1])) * k for p in m["positions"]]
        self.tree_parts = []
        for i, tris in enumerate(m["triangles"]):
            used = sorted(set(x for t in tris for x in t))
            remap = {o: n for n, o in enumerate(used)}
            me = bpy.data.meshes.new("Tree_sub%d" % i)
            me.from_pydata([verts[o] for o in used], [], [(remap[a], remap[c], remap[b]) for a, b, c in tris])
            me.update()
            ob = bpy.data.objects.new("Tree_00_sub%d" % i, me)
            bpy.context.scene.collection.objects.link(ob)
            self.tree_parts.append(ob)
        self.meshes = self.tree_parts
        self.arm = None
        self.s = 1.0
        self.t = Vector((0, 0, 0))
        a = math.radians(-TREE_YAW_DEG)
        u = Vector((math.sin(a), 0, math.cos(a)))
        self.front = Vector((-u.x, -u.z, u.y)).normalized()

    def _bvh(self):
        dg = bpy.context.evaluated_depsgraph_get()
        verts, polys, allv = [], [], []
        self.skin = []  # (world point, mesh object, vertex index) for weight lookups
        for m in self.meshes:
            ev = m.evaluated_get(dg)
            me = ev.to_mesh()
            mw = ev.matrix_world
            pts = [mw @ v.co for v in me.vertices]
            allv.extend(pts)
            if not self.tree:
                self.skin.extend((p, m, i) for i, p in enumerate(pts))
            if not self.tree or m.name == "Tree_00_sub2":  # tree: bark only for surface placement
                base = len(verts)
                verts.extend(pts)
                polys.extend([base + i for i in p.vertices] for p in me.polygons)
            ev.to_mesh_clear()
        self._verts = allv
        self.bark = verts
        return BVHTree.FromPolygons(verts, polys)

    def bone_rest(self, name):
        b = self.arm.data.bones[name]
        mw = self.arm.matrix_world @ b.matrix_local
        return mw.to_quaternion().to_matrix(), self.arm.matrix_world @ b.head_local, self.arm.matrix_world @ b.tail_local

    def bone_prefab(self, name):
        hits = self.prefab.by_name(name)
        if len(hits) != 1:
            raise RuntimeError("bone %s not unique in prefab (%d)" % (name, len(hits)))
        W = self.prefab.world(hits[0])
        lossy = sum((W[0][c] ** 2 + W[1][c] ** 2 + W[2][c] ** 2) ** 0.5 for c in range(3)) / 3.0
        return W, lossy, self.prefab.path(hits[0])

    def surface(self, target, approach, far, spread=0.0):
        a = approach.normalized()
        o = target + a * far
        loc, nrm, _, _ = self.bvh.ray_cast(o, -a, far * 3)
        if loc is None:
            raise RuntimeError("no surface hit for %s from %s" % (tuple(target), tuple(a)))
        acc = nrm if nrm.dot(a) > 0 else -nrm
        if spread > 0:
            x = a.orthogonal().normalized()
            y = a.cross(x).normalized()
            for k in range(6):
                ang = k * math.pi / 3
                off = (x * math.cos(ang) + y * math.sin(ang)) * spread
                l2, n2, _, _ = self.bvh.ray_cast(o + off, -a, far * 3)
                if l2 is not None:
                    acc = acc + (n2 if n2.dot(a) > 0 else -n2)
        return loc, acc.normalized()

    def wide_normal(self, p0, n, spread):
        """Surface normal averaged over the crystal's footprint (two rings of rays toward -n around p0); p0 itself is kept."""
        x = n.orthogonal().normalized()
        y = n.cross(x).normalized()
        acc = n.copy()
        o = p0 + n * spread * 3.0
        for rr in (0.5, 1.0):
            for k in range(8):
                ang = k * math.pi / 4 + rr
                off = (x * math.cos(ang) + y * math.sin(ang)) * spread * rr
                l2, n2, _, _ = self.bvh.ray_cast(o + off, -n, spread * 8.0)
                if l2 is not None:
                    acc = acc + (n2 if n2.dot(n) > 0 else -n2)
        return acc.normalized()

    def dominant_bone(self, p, radius):
        """Skin weight the body follows around p: summed vertex-group weights of body vertices within radius (Blender units)."""
        acc = {}
        for q, m, i in self.skin:
            d = (q - p).length
            if d > radius:
                continue
            fall = 1.0 - d / radius
            for g in m.data.vertices[i].groups:
                name = m.vertex_groups[g.group].name
                acc[name] = acc.get(name, 0.0) + g.weight * fall
        tot = sum(acc.values()) or 1.0
        ranked = sorted(((w / tot, n) for n, w in acc.items()), reverse=True)
        return [(n, round(w, 3)) for w, n in ranked[:4]]

    def inside(self, p):
        """p inside the body: the first hit of (almost) every axis ray is a back face. Tolerates one stray ray (open meshes, layered
        clothes) and the tree bark tube (open ends: vertical rays may escape)."""
        back = hits = 0
        for d in (Vector((1, 0, 0)), Vector((-1, 0, 0)), Vector((0, 1, 0)), Vector((0, -1, 0)), Vector((0, 0, 1)), Vector((0, 0, -1))):
            loc, nrm, _, _ = self.bvh.ray_cast(p, d, self.far)
            if loc is None:
                continue
            hits += 1
            if nrm.dot(d) > 0:
                back += 1
        return back >= 4 and back >= hits - 1

    def nearest(self, p):
        loc, _, _, dist = self.bvh.find_nearest(p)
        return dist if loc is not None else 0.0

    def base_gap(self, ob, n, reach):
        """v2 metric: how far the sunk part floats off the body: for each part vertex below the attachment plane, cast inward along -n
        (up to reach, Blender units); a front-facing hit means the vertex is outside the body (gap = distance). Max gap in metres."""
        gap = 0.0
        for v in ob.data.vertices:
            if v.co.z > -0.002:
                continue
            w = ob.matrix_world @ v.co
            loc, nrm, _, dist = self.bvh.ray_cast(w, -n, reach)
            if loc is not None and nrm.dot(n) > 0.0:
                gap = max(gap, dist * self.s)
        return gap


# ================================================================ per-species spot (Blender species space) — the v2 spots (D308-4b), kept.
def t_dokkaebi(c):
    # 복장뼈: 열린 저고리 깃 사이 맨가슴 한복판(가슴근 사이). 정면 면이 가장 넓고 락온 중심과 가깝다
    p0, n = c.surface(Vector((0.0, 0.0, 1.70)), FRONT, 1.0, spread=0.015)
    return p0, n, UP_Z


def t_agwi(c):
    # 부푼 배: 배꼽 위 볼록한 정점보다 조금 위(눈높이 쪽을 본다), 몸 왼쪽으로 3.5 cm 비껴 박힌 자리
    best = None
    for i in range(41):
        z = 1.05 + i * 0.01
        try:
            p, _ = c.surface(Vector((0.035, 0.0, z)), FRONT, 1.0)
        except RuntimeError:
            continue
        if best is None or p.y < best[0].y:
            best = (p, z)
    if best is None:
        raise RuntimeError("agwi belly not found")
    p0, n = c.surface(Vector((0.035, 0.0, best[1] + 0.04)), FRONT, 1.0, spread=0.015)
    return p0, n, UP_Z


def t_changgui(c):
    # 오른 쇄골 아래(옛 범 발톱 자국이 시작하던 자리 — 범에게 물린 자리의 해석, 창작 표시)
    p0, n = c.surface(Vector((-0.075, 0.0, 1.455)), FRONT, 0.8, spread=0.012)
    return p0, n, UP_Z


def t_bulgasari(c):
    # 이마 한가운데(눈 위·귀 사이 넓은 앞면) — 정면에서 머리가 몸을 가려도 보이는 자리
    p0, n = c.surface(Vector((0.0, -0.42, 0.06)), Vector((0, -1.0, 0.85)), 0.6, spread=0.004)
    return p0, n, UP_Z


def t_fox(c):
    # 꼬리 끝(사용자 지시): 마지막 꼬리 마디 Tail_05 위쪽 바깥면 — 결정이 꼬리 끝 방향으로 기울어 박힌다(기울기는 tilt로).
    # v3: 시안 B의 60 % 자리는 가늘어서 큰 결정의 뿌리(7.5 cm)가 꼬리 반대편으로 뚫고 나왔다(테 뜸 4.8 cm, 묻은 쪽 광선 21 cm).
    # 같은 뼈 안에서 25 %로 당기면 뚫림 0(조사 시 15/25/35 % × 기울기 20/30° × 보이는 길이 13/14 cm 비교, 35 %부터 다시 뚫림).
    _, hh, ht = c.bone_rest("Tail_05")
    d = (ht - hh).normalized()
    q = hh.lerp(ht, 0.25)
    out = (UP_Z - d * UP_Z.dot(d)).normalized()
    p0, n0 = c.surface(q, out, 0.3, spread=0.004)
    return p0, n0, d


def t_imugi(c):
    # 턱밑 역린 자리(사용자 결정 "역린으로 두자"): 턱받이 바로 아래 목 앞면(턱 끝에서 Blender z .025, 앞을 봄) = 턱밑 목울대 자리
    p0, n = c.surface(Vector((0.0, 0.0, 0.026)), FRONT, 1.0, spread=0.003)
    return p0, n, UP_Z


def t_tree(c):
    # 성장 교습 나무 줄기 앞면(교습 쪽), 높이 0.66 m — 줄기가 갈라지기 전 굵은 곳
    h = 0.66
    band = [v for v in c.bark if abs(v.z - h) < 0.06]
    if not band:
        h = 0.62
        band = [v for v in c.bark if abs(v.z - h) < 0.06]
    cx = sum(v.x for v in band) / len(band)
    cy = sum(v.y for v in band) / len(band)
    p0, n = c.surface(Vector((cx, cy, h)), c.front, 1.5, spread=0.02)
    return p0, n, UP_Z


# owner = OrganSurface308 PartSpec owner (C# join key). bone = the v2 bone (kept; v2 = dominant skin weight at the spot).
# visible = target visible crystal length along its axis (m, ~2x the v2 size, D308-4c); the world size (max extent, C# worldSize) follows
# from it: the crystal axis is visible / (1 - BURY) long. tilt = lean of the axis off the surface normal toward the frame's 'up'
# (humanoids / tree / bulgasari / imugi: up along the body; fox: toward the tail tip). All TEST.
ENTRIES = {
    "dokkaebi": dict(owner="dokkaebi", rig="Humanoid", bone="Spine01", role="Core", keys=["*"], visible=0.20, tilt=14,
                     place=t_dokkaebi, az=35, close=(70, 8), ko="도깨비 — 가슴 한복판(열린 저고리 깃 사이 복장뼈)",
                     why="정면 4–12 m에서 가장 넓은 정면 면, 락온 중심 근처. 옷이 아닌 맨살에 박힘."),
    "agwi": dict(owner="agwi", rig="Humanoid", bone="Hips", role="Core", keys=["*"], visible=0.20, tilt=14, place=t_agwi,
                 az=35, close=(70, 8), ko="아귀 — 부푼 배(배꼽 위, 몸 왼쪽으로 비껴)",
                 why="설화의 큰 배(folklore-enemies-298 §2), 정면에서 가장 큰 맨살 면. 목은 가늘어 측면 실루엣이 바뀌므로 피함."),
    "changgui": dict(owner="changgui", rig="Humanoid", bone="RightShoulder", role="Core", keys=["*"], visible=0.18, tilt=14,
                     place=t_changgui, az=-35, close=(-70, 8), ko="창귀 — 오른 쇄골 아래(옛 범 발톱 자국 자리)",
                     why="범에게 물린 자리 해석(위치는 원전에 없음 — 창작). 정면 상체, 조끼 앞섶 깃 옆(뼈 = 시안 B와 같은 RightShoulder)."),
    "bulgasari": dict(owner="bulgasari", rig="Generic", bone="Head", role="Core", keys=["*"], visible=0.28, tilt=10,
                      place=t_bulgasari, az=30, close=(65, 15), ko="불가사리 — 이마 한가운데",
                      why="네발 짐승은 정면에서 머리가 몸을 가린다 → 이마가 가장 확실히 보인다. 결정은 이마에서 위·앞으로 솟는다."),
    "fox_spirit": dict(owner="fox_spirit", rig="Generic", bone="Tail_05", role="Spine", keys=["*"], visible=0.14, tilt=20,
                       place=t_fox, az=140, close=(100, 4), ko="여우령 — 꼬리 끝(사용자 지시)",
                       why="D308-4b 사용자 원문. 마지막 마디 Tail_05의 60 %→25 %로 당김(큰 결정 뿌리가 가는 끝을 뚫어서). 꼬리 끝은 뒤를 보므로 역할 Spine(정면 검사 면제)."),
    "imugi": dict(owner="imugi", rig="Generic", bone="Body_01", role="Core", keys=["*"], visible=0.24, tilt=12, place=t_imugi,
                  az=35, close=(65, -12), ko="이무기 — 턱밑 역린 자리(사용자 결정)",
                  why="D308-4b 사용자 원문 \"역린으로 두자\". 여의주 자리는 버림(NARR-TRUTH 위험 해소)."),
    "growth_tree": dict(owner="demo_growth_lesson", rig="None", bone="Tree_00", role="Core", keys=["ground", "*"], visible=0.18,
                        tilt=14, place=t_tree, az=35, close=(70, 8), ko="성장 교습 — 나무 줄기 앞면(높이 약 0.66 m)",
                        why="교습 쪽 줄기 앞면. 나무 자체는 서브메시 3이라 덧칠 불가 → 결정 렌더러가 기관."),
}


def tilt_axis(n, up_hint, tilt_deg, turn_deg=0.0):
    """Crystal axis: the surface normal leaned by tilt toward 'up' (up_hint orthogonalised), the lean direction turned about n by turn."""
    y = up_hint - n * up_hint.dot(n)
    if y.length < 1e-6:
        y = n.orthogonal()
    y.normalize()
    if turn_deg:
        y = (Matrix.Rotation(math.radians(turn_deg), 3, n) @ y).normalized()
    t = math.radians(tilt_deg)
    return (n * math.cos(t) + y * math.sin(t)).normalized(), y


def measure(c, ob, info, origin, a, k):
    """Embedding metrics of the posed crystal (species space in, metres out)."""
    mw = ob.matrix_world
    root, vis = samples(info)
    gap = 0.0
    for p in root:
        w = mw @ p
        if not c.inside(w):
            gap = max(gap, c.nearest(w) * c.s)
    hidden, hid_n = 0.0, 0
    for p in vis:
        w = mw @ p
        if c.inside(w):
            dd = c.nearest(w) * c.s
            if dd > VISIBLE_TOL:
                hid_n += 1
            hidden = max(hidden, dd)
    axis_len = AXIS_LEN * k / c.s                     # species units
    top_h = info.get("top_h", TOP_H)                  # designed tip height (fraction of AXIS_LEN); element crystals differ (D308-4e)
    top = origin + a * (top_h * AXIS_LEN * k / c.s)
    loc, nrm, _, dist = c.bvh.ray_cast(top + a * 1e-5, -a, axis_len * 1.5)
    tip_inside = loc is not None and nrm.dot(a) < 0.0  # first hit from the tip is a back face: the tip itself is in the body
    vis_len = (dist - 1e-5) * c.s if (loc is not None and not tip_inside) else (0.0 if tip_inside else top_h * AXIS_LEN * k)
    buried = (top_h + BURY) - vis_len / (AXIS_LEN * k)   # root depth below the surface / (L k); v3: top_h + BURY = 1 (unchanged)
    return dict(rimGapM=round(gap, 4), buriedFraction=round(buried, 4), visibleLengthM=round(vis_len, 4),
                visibleInsideMaxM=round(hidden, 4), visibleHiddenSamples=hid_n, visibleSamples=len(vis), tipInside=tip_inside)


def pixel_view(c, ob, target, yaw_deg, dist_m):
    """AC-T5 analytic: pixels of the crystal in a Unity 1920x1080 / 60° vertical fov frame, horizontal camera dist_m from the crystal's
    visible centre, yaw from the actor front. Ray cast through every pixel centre around the projection; a pixel counts when the
    crystal is the first hit (body BVH in front of it = occluded). Bind pose. Returns (pixels, width px, height px)."""
    verts = [ob.matrix_world @ v.co for v in ob.data.vertices]
    cb = BVHTree.FromPolygons(verts, [list(p.vertices) for p in ob.data.polygons])
    f0 = (Matrix.Rotation(math.radians(yaw_deg), 3, UP_Z) @ c.front)
    f0.z = 0.0
    f0.normalize()
    cam = target + f0 * (dist_m / c.s)
    fwd = (target - cam).normalized()
    right = fwd.cross(UP_Z).normalized()
    up = right.cross(fwd).normalized()
    F = (SCREEN_H * 0.5) / math.tan(math.radians(FOV_V * 0.5))
    xs, ys = [], []
    for v in verts:
        d = v - cam
        z = d.dot(fwd)
        xs.append(SCREEN_W * 0.5 + d.dot(right) / z * F)
        ys.append(SCREEN_H * 0.5 - d.dot(up) / z * F)
    i0, i1 = int(math.floor(min(xs))) - 1, int(math.ceil(max(xs))) + 1
    j0, j1 = int(math.floor(min(ys))) - 1, int(math.ceil(max(ys))) + 1
    count = 0
    lo_i = lo_j = 10 ** 9
    hi_i = hi_j = -10 ** 9
    far = dist_m / c.s * 2.0
    for j in range(j0, j1 + 1):
        for i in range(i0, i1 + 1):
            dx = (i + 0.5 - SCREEN_W * 0.5) / F
            dy = (SCREEN_H * 0.5 - (j + 0.5)) / F
            ray = (fwd + right * dx + up * dy).normalized()
            hc = cb.ray_cast(cam, ray, far)
            if hc[0] is None:
                continue
            hb = c.bvh.ray_cast(cam, ray, hc[3])
            if hb[0] is not None and hb[3] < hc[3] - 1e-6:
                continue
            count += 1
            lo_i, hi_i, lo_j, hi_j = min(lo_i, i), max(hi_i, i), min(lo_j, j), max(hi_j, j)
    if count == 0:
        return 0, 0, 0
    return count, hi_i - lo_i + 1, hi_j - lo_j + 1


def placement(c, e, ob, origin, a, n, Ma, k, bone_name, weights, meas):
    loc = [v.co.copy() for v in ob.data.vertices]
    world = [ob.matrix_world @ v for v in loc]
    h = max((w - origin).dot(n) for w in world)
    tip = max(world, key=lambda w: (w - origin).dot(a))
    lo, hi = C.bounds(world)
    bc = (lo + hi) * 0.5
    brad = max((w - bc).length for w in world)
    uc, us = unity_bounds(ob.data)
    notes = []
    if c.tree:
        lossy = TREE_WORLD_SCALE
        to_local = lambda p: R @ p * (1.0 / TREE_WORLD_SCALE)  # noqa: E731
        Q = R @ Ma @ R.transposed()
        bone = dict(name="Tree_00", humanBone=None, path="demo_growth_lesson/Chapter3_Visual/Tree_00",
                    note="scene object (W_Demo_Main / candidates), NOT PF_PlantedTree.prefab (shared by spell 검)")
        residual, rot_err = 0.0, 0.0
    else:
        Mb, hb, _ = c.bone_rest(bone_name)
        W, lossy, path = c.bone_prefab(bone_name)
        kk = c.s / lossy
        A = D @ Mb.transposed()
        to_local = lambda p: A @ (p - hb) * kk  # noqa: E731
        Q = A @ Ma @ R.transposed()
        pu = (R @ origin) * c.s + c.t
        pw = UP.apply(W, list(to_local(origin)))
        residual = (Vector(pw) - pu).length
        Wr = Matrix([W[i][:3] for i in range(3)]).to_quaternion().to_matrix()
        rot_err = math.degrees((Wr @ Q).to_quaternion().rotation_difference((R @ Ma @ R.transposed()).to_quaternion()).angle)
        if residual > 0.005:
            notes.append("prefab bone pose differs from FBX bind by %.3f m here; local values use the bind pose "
                         "(correct once the Animator drives the bone)" % residual)
        bone = dict(name=bone_name, humanBone=HUMAN_OF.get(bone_name) if c.arm and e["rig"] == "Humanoid" else None, path=path)
    if abs(Q.determinant() - 1.0) > 1e-3:
        raise RuntimeError("non-proper rotation for " + c.entry)
    q = Q.to_quaternion()
    qt = (q.x, q.y, q.z, q.w)
    pivot = to_local(origin)
    s_local = k / lossy
    centre = pivot + Q @ (uc * s_local)
    # cross-check: the mesh bounds centre carried through the Blender world placement lands on the same bone-local point
    centre_check = (to_local(ob.matrix_world @ (R.transposed() @ uc)) - centre).length
    if centre_check > 1e-5:
        raise RuntimeError("bounds-centre cross-check %.2e for %s" % (centre_check, c.entry))
    euler = QM.q_to_euler(qt)
    err = QM.q_angle_deg(QM.euler_to_q(euler), qt)
    if err > 0.01:
        raise RuntimeError("Euler round trip %.4f deg for %s" % (err, c.entry))
    al = (to_local(origin + a) - pivot).normalized()
    nl = (to_local(origin + n) - pivot).normalized()
    gap_v2 = c.base_gap(ob, n, AXIS_LEN * k / c.s)
    front_dot = n.dot(c.front)
    if e["role"] != "Spine" and front_dot < -0.1:
        notes.append("outward normal faces away from the actor front (dot %.2f < VisibleDot -0.1)" % front_dot)
    size = max(us) * k
    return dict(
        owner=e["owner"], entry=c.entry, organ=ORGAN_ID, role=e["role"], attackKeys=e["keys"], rig=e["rig"],
        actor=("PF_" + c.entry) if not c.tree else "demo_growth_lesson",
        prefab=None if c.tree else "Assets/_Project/Art/Characters/Folklore298/Prefabs/PF_%s.prefab" % c.entry,
        label=e["ko"], reason=e["why"], bone=bone, skinWeights=weights, version=VERSION,
        worldSize=round(size, 6), worldScale=round(k, 6), axisLengthM=round(AXIS_LEN * k, 4),
        targetVisibleM=e["visible"], tiltDeg=round(math.degrees(a.angle(n)), 2),
        unity=dict(localPosition=r6(centre), localEulerAngles=euler, localScale=round(s_local, 6),
                   pivotLocalPosition=r6(pivot), localRotation=r6(qt), parentLossyScale=round(lossy, 6),
                   frame="Unity bone-local at FBX bind pose; localPosition = MESH BOUNDS CENTRE (OrganSurface308.EnsurePart contract)"),
        surface=dict(surfaceLocalPoint=r6(to_local(tip)), surfaceLocalNormal=r6(al), bodyNormalLocal=r6(nl),
                     boundsCenterLocal=r6(to_local(bc)), boundingRadiusWorldM=round(brad * c.s, 4),
                     suggestedRadiusWorldM=round(brad * c.s * 1.05, 4), mode="Part",
                     note="surfaceLocalPoint = crystal tip (outermost along the axis = counter-stroke landing); surfaceLocalNormal = "
                          "crystal axis (= the part's +Y, what OrganSurface308.ComputeSurface uses); bodyNormalLocal = body normal"),
        check=dict(prefabBindResidualM=round(residual, 5), prefabBindRotResidualDeg=round(rot_err, 3),
                   frontDotBind=round(front_dot, 3), axisFrontDot=round(a.dot(c.front), 3), baseGapM=round(gap_v2, 4),
                   centreCrossCheckM=round(centre_check, 8), eulerRoundTripDeg=round(err, 5), protrusionM=round(h * c.s, 4),
                   notes=notes, **meas),
        status="TEST",
    )


def render_tiles(c, ob, e, tag):
    TILE_DIR.mkdir(parents=True, exist_ok=True)
    C.setup_render(372, 372)
    body = C.flat_material("Review_Body", (0.80, 0.78, 0.74, 1))
    leaf = C.flat_material("Review_Leaf", (0.70, 0.76, 0.66, 1))
    for m in c.meshes:
        m.data.materials.clear()
        m.data.materials.append(leaf if (c.tree and m.name != "Tree_00_sub2") else body)
    real = ob.data.materials[0]
    hi_mat = C.flat_material("Review_Highlight", HIGHLIGHT)
    if c.entry == "imugi":
        center, radius = Vector((0.0, -0.36, 0.0)), 0.17
    elif c.tree:
        center, radius = (c.lo + c.hi) * 0.5, 0.58 * max(c.hi - c.lo)
    else:
        center, radius = (c.lo + c.hi) * 0.5, 0.56 * max(c.hi - c.lo)
    ob.data.materials[0] = hi_mat
    C.render_view(TILE_DIR / (tag + "_front.png"), center, radius, 0, 6, front=c.front)
    C.render_view(TILE_DIR / (tag + "_q34.png"), center, radius, e["az"], 14, front=c.front)
    ob.data.materials[0] = real
    world = [ob.matrix_world @ v.co for v in ob.data.vertices]
    lo, hi = C.bounds(world)
    pc = (lo + hi) * 0.5
    pr = max(max(hi - lo) * 1.5, 0.22 / c.s)
    C.render_view(TILE_DIR / (tag + "_close.png"), pc, pr, e["close"][0], e["close"][1], front=c.front)


def pose_matrix(origin, Ma, k, c):
    return Matrix.Translation(origin) @ Ma.to_4x4() @ Matrix.Scale(k / c.s, 4)


def cmd_place(entry):
    e = ENTRIES[entry]
    mesh = json.loads(MESH_JSON.read_text(encoding="utf-8"))
    c = Ctx(entry)
    p0, n0, up_hint = e["place"](c)
    ob, tris, _, info = shard_object(lin(BASE_SRGB))
    uc, us = unity_bounds(ob.data)
    if tris != mesh["triangles"] or (Vector(mesh["unityMeshBoundsSize"]) - us).length > 1e-5:
        raise RuntimeError("rebuilt crystal differs from the exported one (run build first)")
    k = e["visible"] / (TOP_H * AXIS_LEN)                    # scale: the designed visible axis length = target
    rim_r = RADIUS * k                                        # world rim radius (m)
    n = c.wide_normal(p0, n0, rim_r / c.s)                   # body normal over the crystal footprint
    # lean direction: the preferred one first; if the visible crystal runs into the body (jaw, collar, clothes) try the lean turned
    # about the normal, then a smaller lean. Recorded in the row (tiltTurnDeg).
    trials = []
    for tilt, turn in [(e["tilt"], t) for t in (0, 45, -45, 90, -90, 180)] + [(e["tilt"] * 0.5, 0), (0.0, 0)]:
        a, y = tilt_axis(n, up_hint, tilt, turn)
        Ma = G.frame(a, y)
        ob.matrix_world = pose_matrix(p0, Ma, k, c)
        m = measure(c, ob, info, p0, a, k)
        trials.append((tilt, turn, m))
        if m["visibleHiddenSamples"] == 0 and not m["tipInside"]:
            break
    tilt, turn, _ = min(trials, key=lambda t: (t[2]["visibleHiddenSamples"], t[2]["tipInside"], trials.index(t)))
    a, y = tilt_axis(n, up_hint, tilt, turn)
    Ma = G.frame(a, y)
    # sink: move along n in 0.5 % L steps, nearest to the designed depth first (0, +, -, ++, --, ...; + = deeper), until the root is
    # closed all round (rim gap <= 5 mm, both metrics) with the buried fraction (along the axis) in the .30-.40 band
    chosen, history = None, []
    for step in [0] + [v for i in range(1, 11) for v in (i, -i)]:
        s = step * 0.005 * AXIS_LEN * k
        origin = p0 - n * (s / c.s)
        ob.matrix_world = pose_matrix(origin, Ma, k, c)
        m = measure(c, ob, info, origin, a, k)
        g2 = c.base_gap(ob, n, AXIS_LEN * k / c.s)
        history.append(dict(sinkM=round(s, 4), rimGapM=m["rimGapM"], gapV2M=round(g2, 4), buried=m["buriedFraction"]))
        if max(m["rimGapM"], g2) <= GAP_TOL and BURY_BAND[0] <= m["buriedFraction"] <= BURY_BAND[1] and not m["tipInside"]:
            chosen = (s, origin, m)
            break
    if chosen is None:
        # nothing met both: keep the pose with the smallest gap whose buried fraction stays in the band (reported, verify fails)
        inband = [h for h in history if BURY_BAND[0] <= h["buried"] <= BURY_BAND[1]] or history[:1]
        s = min(inband, key=lambda h: max(h["rimGapM"], h["gapV2M"]))["sinkM"]
        origin = p0 - n * (s / c.s)
        ob.matrix_world = pose_matrix(origin, Ma, k, c)
        chosen = (s, origin, measure(c, ob, info, origin, a, k))
    sink, origin, meas = chosen
    ob.matrix_world = pose_matrix(origin, Ma, k, c)
    # AC-T5 (analytic, bind pose): pixel count at 12 m front / ±45°, 8 and 4 m front
    vis_c = origin + a * (0.5 * TOP_H * AXIS_LEN * k / c.s)
    px = {}
    for tag, yaw, dist in (("front12", 0, 12.0), ("left45_12", 45, 12.0), ("right45_12", -45, 12.0), ("front8", 0, 8.0),
                           ("front4", 0, 4.0)):
        cnt, w, hh = pixel_view(c, ob, vis_c, yaw, dist)
        px[tag] = dict(pixels=cnt, widthPx=w, heightPx=hh)
    if e["role"] == "Spine":
        cnt, w, hh = pixel_view(c, ob, vis_c, e["az"], 12.0)
        px["tileAz12"] = dict(pixels=cnt, widthPx=w, heightPx=hh, yawDeg=e["az"])
    meas = dict(meas, sinkM=round(sink, 4), sinkHistory=history, tiltTurnDeg=turn, tiltUsedDeg=tilt,
                tiltTrials=[dict(tilt=t, turn=tu, hidden=m["visibleHiddenSamples"], tipInside=m["tipInside"]) for t, tu, m in trials],
                acT5=dict(rule="pixels of the crystal (first hit) at 1920x1080, vertical fov 60°, horizontal camera at the crystal "
                               "height; >= %d px at 12 m front and ±45° (TEST, bind pose, offline ray cast)" % AC_T5_PX,
                          exempt=e["role"] == "Spine", views=px,
                          min12=min(px[t]["pixels"] for t in ("front12", "left45_12", "right45_12"))))
    weights = None
    bone_name = e["bone"]
    if not c.tree:
        weights = c.dominant_bone(origin, rim_r / c.s)
    row = placement(c, e, ob, origin, a, n, Ma, k, bone_name, weights, meas)
    if weights and weights[0][0] != bone_name:
        row["check"]["notes"].append("dominant skin weight here is %s (%.2f); kept the v2 bone %s" % (weights[0][0], weights[0][1], bone_name))
    if meas["visibleHiddenSamples"]:
        row["check"]["notes"].append("%d of %d visible-section samples are inside the body (max %.1f cm)" %
                                     (meas["visibleHiddenSamples"], meas["visibleSamples"], meas["visibleInsideMaxM"] * 100))
    render_tiles(c, ob, e, entry)
    (ANALYSIS / ("shard308_%s.json" % entry)).write_text(json.dumps(row, ensure_ascii=False, indent=1), encoding="utf-8")
    print("ORGAN308_PLACED " + json.dumps(dict(entry=entry, bone=bone_name, weights=weights, size=row["worldSize"],
                                                 visible=meas["visibleLengthM"], buried=meas["buriedFraction"],
                                                 tilt=row["tiltDeg"], turn=turn, sink=round(sink, 4), rimGap=meas["rimGapM"],
                                                 gapV2=row["check"]["baseGapM"], hidden=meas["visibleHiddenSamples"],
                                                 front=row["check"]["frontDotBind"], px12=meas["acT5"]["views"]["front12"],
                                                 min12=meas["acT5"]["min12"], residual=row["check"]["prefabBindResidualM"],
                                                 notes=row["check"]["notes"]), ensure_ascii=False))


# ================================================================ verify (deploy contract, read-only on the FBX)
SMOKE_AXES = {"UpAxis": [1], "UpAxisSign": [1], "FrontAxis": [2], "FrontAxisSign": [1], "CoordAxis": [0],
              "CoordAxisSign": [1], "UnitScaleFactor": [100.0]}


def _props(node):
    out = {}
    for p70 in node.elems:
        if p70.id == b"Properties70":
            for p in p70.elems:
                out[p.props[0].decode("utf-8", "replace")] = list(p.props[4:])
    return out


def cmd_verify():
    from io_scene_fbx import parse_fbx
    mesh = json.loads(MESH_JSON.read_text(encoding="utf-8"))
    ck = {}
    C.reset()
    bpy.ops.import_scene.fbx(filepath=str(DEPLOY_FBX))
    obs = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    ck["oneMesh"] = len(obs) == 1
    me = obs[0].data
    me.calc_loop_triangles()
    raw, _ = parse_fbx.parse(str(DEPLOY_FBX))
    g = _props(next(e for e in raw.elems if e.id == b"GlobalSettings"))
    ck["fbxAxesYupZfrontXright_unit100"] = all(g.get(k) == v for k, v in SMOKE_AXES.items())
    models = [e for e in next(e for e in raw.elems if e.id == b"Objects").elems if e.id == b"Model"]
    mp = _props(models[0]) if len(models) == 1 else {}
    ck["fbxModelIdentity"] = len(models) == 1 and all(
        mp.get(k) in (None, d) for k, d in (("Lcl Translation", [0.0, 0.0, 0.0]), ("Lcl Rotation", [0.0, 0.0, 0.0]),
                                             ("Lcl Scaling", [1.0, 1.0, 1.0])))
    ck["fbxNamesKept(%s)" % SHARD_NAME] = obs[0].name == SHARD_NAME and me.name == SHARD_NAME
    ck["triangles<=%d" % MAX_TRIANGLES] = len(me.loop_triangles) <= MAX_TRIANGLES
    ck["trianglesMatchJson"] = len(me.loop_triangles) == mesh["triangles"]
    ck["oneCrystal(1 island)"] = islands(me) == 1 and mesh.get("crystals") == 1
    ck["singleMaterial"] = [m.name for m in me.materials] == [MATERIAL_NAME]
    lo, hi = C.bounds([obs[0].matrix_world @ v.co for v in me.vertices])
    uc, us = Vector(mesh["unityMeshBoundsCenter"]), Vector(mesh["unityMeshBoundsSize"])
    ck["boundsMatch"] = ((lo + hi) * 0.5 - Vector((-uc.x, -uc.z, uc.y))).length < 1e-4 and ((hi - lo) - Vector((us.x, us.z, us.y))).length < 1e-4
    ck["uv0"] = len(me.uv_layers) >= 1
    ck["flatFacets"] = not any(p.use_smooth for p in me.polygons)
    bsdf = next((nd for nd in me.materials[0].node_tree.nodes if nd.type == "BSDF_PRINCIPLED"), None) if me.materials else None
    em = 0.0
    if bsdf is not None:
        col = bsdf.inputs["Emission Color"].default_value if "Emission Color" in bsdf.inputs else (0, 0, 0, 1)
        em = max(col[0], col[1], col[2]) * (bsdf.inputs["Emission Strength"].default_value if "Emission Strength" in bsdf.inputs else 1.0)
    ck["noEmission"] = em <= 1e-6
    ck["sha256MatchesBuild"] = hashlib.sha256(DEPLOY_FBX.read_bytes()).hexdigest() == mesh["sha256"]
    # deploy folder: exactly the shard files; the Organs308 mirror holds Shard308 only (v1 per-species folders retired)
    mat = DEPLOY_DIR / (MATERIAL_NAME + ".mat")
    mtxt = mat.read_text(encoding="utf-8") if mat.exists() else ""
    ck["deployMaterialMatte"] = ("m_Name: %s\n" % MATERIAL_NAME) in mtxt and "m_ValidKeywords: []" in mtxt and \
        "_EmissionColor: {r: 0, g: 0, b: 0, a: 1}" in mtxt and "_EMISSION" not in mtxt and \
        ("- _Smoothness: %s\n" % SMOOTHNESS) in mtxt and "_Metallic: 0" in mtxt
    pj = DEPLOY_DIR / "shard308_placements.json"
    doc = json.loads(pj.read_text(encoding="utf-8")) if pj.exists() else {}
    rows = doc.get("placements", [])
    owners = sorted(r["owner"] for r in rows)
    ck["placements7"] = owners == sorted(e["owner"] for e in ENTRIES.values())
    ck["placementsComplete"] = bool(rows) and all(
        r.get("organ") == ORGAN_ID and r.get("bone") and r.get("worldSize", 0) > 0 and r.get("radius", 0) > 0 and
        QM.q_angle_deg(QM.euler_to_q([r["localEulerAngles"][a] for a in "xyz"]), tuple(r["localRotation"][a] for a in "xyzw")) < 0.01
        for r in rows)
    ck["placementsBonesKeptFromV2"] = all(r.get("bone") == ENTRIES[k]["bone"] for k in ENTRIES for r in rows if r["owner"] == ENTRIES[k]["owner"])
    ck["deployDocV3"] = str(doc.get("decision", "")).startswith("D308-4c") and doc.get("triangles") == mesh["triangles"]
    # embedding / size / AC-T5 (per placement analysis rows, the same rows the master carries)
    parts = [json.loads((ANALYSIS / ("shard308_%s.json" % e)).read_text(encoding="utf-8")) for e in ENTRIES]
    ck["rimGap<=5mm(all)"] = all(p["check"]["rimGapM"] <= GAP_TOL and p["check"]["baseGapM"] <= GAP_TOL for p in parts)
    ck["buried30-40%(all)"] = all(BURY_BAND[0] - 1e-4 <= p["check"]["buriedFraction"] <= BURY_BAND[1] + 1e-4 for p in parts)
    ck["tipNotInside(all)"] = not any(p["check"]["tipInside"] for p in parts)
    ck["visibleLength>=0.9xTarget(all)"] = all(p["check"]["visibleLengthM"] >= 0.9 * p["targetVisibleM"] for p in parts)
    v2 = C.OUT / "organ308_placements.v2.json"
    v2doc = json.loads(v2.read_text(encoding="utf-8")) if v2.exists() else {}
    v2size = {p["owner"]: p["worldSize"] for p in v2doc.get("parts", [])}
    ck["v2Kept(organ308_placements.v2.json)"] = len(v2size) == 7 and str(v2doc.get("decision", "")).startswith("D308-4b")
    ck["visible>=1.5xV2Size(all)"] = bool(v2size) and all(p["check"]["visibleLengthM"] >= 1.5 * v2size.get(p["owner"], 1e9) for p in parts)
    ck["acT5Front12m+-45>=%dpx(non-Spine)" % AC_T5_PX] = all(p["check"]["acT5"]["min12"] >= AC_T5_PX for p in parts if p["role"] != "Spine")
    master = json.loads((C.OUT / "organ308_placements.json").read_text(encoding="utf-8"))
    ck["masterV3"] = master.get("schema") in ("organ308-placement/3", "organ308-placement/4") and len(master.get("parts", [])) == 7
    # D308-4e: the five element shapes sit beside the neutral crystal (shard308e.py build); D308-4d: Contam308 beside Shard308
    want = {SHARD_NAME + ".fbx", MATERIAL_NAME + ".mat", "shard308_placements.json"} | \
        {"%s_%s.fbx" % (SHARD_NAME, e) for e in ("Wood", "Fire", "Earth", "Metal", "Water")}
    have = {p.name for p in DEPLOY_DIR.iterdir()}
    ck["deployFolderExact"] = have == want
    mirror = DEPLOY_DIR.parent
    ck["mirrorShardOnly"] = sorted(p.name for p in mirror.iterdir()) in (["Shard308"], ["Contam308", "Shard308"])
    fails = [k for k, v in ck.items() if not v]
    out = dict(tool="Tools/Blender/Organ308/shard308.py verify", version=VERSION, blender=bpy.app.version_string, fbx=str(DEPLOY_FBX),
               triangles=len(me.loop_triangles), checks=ck, deployFiles=sorted(have), failures=len(fails),
               summary=[dict(owner=p["owner"], bone=p["bone"]["name"], worldSizeM=p["worldSize"], visibleM=p["check"]["visibleLengthM"],
                             buried=p["check"]["buriedFraction"], rimGapM=p["check"]["rimGapM"], gapV2M=p["check"]["baseGapM"],
                             tiltDeg=p["tiltDeg"], px12=p["check"]["acT5"]["views"]["front12"]["pixels"], min12=p["check"]["acT5"]["min12"],
                             v2SizeM=v2size.get(p["owner"])) for p in parts])
    (ANALYSIS / "shard308_verify.json").write_text(json.dumps(out, ensure_ascii=False, indent=1), encoding="utf-8")
    print("ORGAN308_VERIFY failures=%d %s" % (len(fails), json.dumps(fails)))


def main():
    args = sys.argv[sys.argv.index("--") + 1:]
    if args[0] == "build":
        cmd_build()
    elif args[0] == "place":
        cmd_place(args[1])
    elif args[0] == "verify":
        cmd_verify()
    else:
        raise SystemExit("unknown command " + args[0])


if __name__ == "__main__":   # shard308e.py (D308-4e/4d) imports this module
    main()
