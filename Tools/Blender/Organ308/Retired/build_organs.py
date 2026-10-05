# RETIRED (D308-4b, 2026-10-02): v1 per-species organ parts were replaced by ONE common magic-stone shard.
# Use Tools/Blender/Organ308/shard308_run.py. Kept for history only; running it would rebuild/delete the v1 deploy tree.
raise SystemExit('retired by D308-4b: use Tools/Blender/Organ308/shard308_run.py')
# #308 organ-art (SPEC-TELEGRAPH-ORGAN-308 "기관 부위 — 모델에 더하는 것", batch B1).
# Builds the procedural organ parts of ONE entry, places them on the species bind pose, exports FBX (metres),
# writes the placement fragment and renders review tiles one at a time. Source models are read-only.
# Headless: blender -b --factory-startup -t 4 --python build_organs.py -- <entry>
#   entry = dokkaebi | agwi | changgui | bulgasari | fox_spirit | imugi | growth_tree
import bpy, bmesh, json, math, sys
from pathlib import Path
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import organ308_common as C  # noqa: E402
import organ_geo as G  # noqa: E402
import unity_prefab as UP  # noqa: E402
import unity_mesh as UM  # noqa: E402
import organ308_deploy as DP  # noqa: E402  (C# OrganSurface308 file ids + placement.json contract)

# Blender world -> Unity prefab-root direction map (verified per species by bone fit, residual <= 1e-5 m on
# humanoids and on every bone used here): Unity = s * (-x, z, -y) + (0, VisualY, 0).
R = Matrix(((-1, 0, 0), (0, 0, 1), (0, -1, 0)))
# Unity bone frame = R * BlenderBoneRest * D (measured on all six species, every bone).
D = Matrix(((-1, 0, 0), (0, 1, 0), (0, 0, 1)))
FRONT = Vector((0, -1, 0))  # Unity actor +Z
UP_Z = Vector((0, 0, 1))
HIGHLIGHT = (0.85, 0.30, 0.12, 1.0)  # 주홍 계열, 리뷰 강조 전용

PARTS_DIR = C.OUT / "Parts"
TILE_DIR = C.OUT / "Review" / "tiles"
UNITY_DEPLOY = DP.UNITY_DEPLOY  # deploy mirror (FBX copies, materials, placement.json) is written by make_materials.py
TREE_MESH = C.ASSETS / "Art/SpellVFX120/Botanical/Meshes/Mesh_Tree.asset"
TREE_WORLD_SCALE = 2.8 * 1.0714285  # Tree_00 local 2.8 x Chapter3_Visual 1.0714285 (W_Demo_Main, demo_growth_lesson)
TREE_YAW_DEG = -18.0  # Tree_00 localRotation (0,-0.1564345,0,0.9876884)


def lin(hexstr):
    h = hexstr.lstrip("#")
    out = []
    for i in (0, 2, 4):
        c = int(h[i:i + 2], 16) / 255.0
        out.append(c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4)
    return (out[0], out[1], out[2], 1.0)


def r6(v):
    return [round(float(c), 6) for c in v]


# ---------------------------------------------------------------- context: species model + prefab
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

    def _load_tree(self):
        C.reset()
        m = UM.read_mesh(TREE_MESH)
        k = TREE_WORLD_SCALE
        # Unity mesh-local u -> Blender b = R^T u, scaled to lesson-world metres
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
        a = math.radians(-TREE_YAW_DEG)  # actor +Z expressed in Tree_00 local, then to Blender
        u = Vector((math.sin(a), 0, math.cos(a)))
        self.front = Vector((-u.x, -u.z, u.y)).normalized()

    def _bvh(self):
        dg = bpy.context.evaluated_depsgraph_get()
        verts, polys, allv = [], [], []
        for m in self.meshes:
            ev = m.evaluated_get(dg)
            me = ev.to_mesh()
            mw = ev.matrix_world
            pts = [mw @ v.co for v in me.vertices]
            allv.extend(pts)
            if not self.tree or m.name == "Tree_00_sub2":  # tree: bark only for surface placement
                base = len(verts)
                verts.extend(pts)
                polys.extend([base + i for i in p.vertices] for p in me.polygons)
            ev.to_mesh_clear()
        self._verts = allv
        self.bark = verts
        return BVHTree.FromPolygons(verts, polys)

    # -- bones
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

    # -- surface queries (Blender world)
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


# ---------------------------------------------------------------- part builders
# Each returns (bm in part-local metres, p0 world, Mp 3x3 world rotation of the part frame).

def world_to_part(bm, p0, Mp, s):
    L = Matrix.Scale(s, 4) @ Mp.transposed().to_4x4() @ Matrix.Translation(-p0)
    for v in bm.verts:
        v.co = L @ v.co


def b_dokkaebi_club_knot(c):
    # a gnarled wooden knot (old club head) hung from the sash by a twisted cord, at the left front hip.
    p0, n = c.surface(Vector((0.15, 0.0, 1.345)), FRONT, 1.0, spread=0.02)
    Mp = G.frame(n, UP_Z)
    bm = G.new_bm()
    cc = Vector((0.004, -0.104, 0.058))
    v = G.ico(bm, 0.077, 3, Matrix.Translation(cc) @ G.rot_z(-12) @ Matrix.Diagonal((1.0, 1.12, 0.74, 1)))
    G.displace(v, cc, 0.012, 15.0, seed=1)
    G.displace(v, cc, 0.004, 50.0, seed=2)
    # knot eye on the outward face
    ec = cc + Vector((0.014, 0.008, 0.054))
    G.lathe(bm, [(0.0, -0.007), (0.036, -0.005), (0.038, 0.007), (0.029, 0.016), (0.020, 0.018), (0.013, 0.011),
                 (0.0, 0.007)], 12, sx=0.85, sy=1.15, offset=ec, twist=0.1)
    # twisted two-strand cord from the knot up into the sash
    for strand in (0, 1):
        pts = []
        for i in range(10):
            t_ = i / 9
            y = -0.045 + 0.090 * t_
            ang = strand * math.pi + t_ * 3.4 * math.pi
            pts.append(Vector((0.0045 * math.cos(ang), y, 0.030 - 0.022 * t_ + 0.0045 * math.sin(ang))))
        G.tube(bm, pts, 5, lambda i, t: (0.0055, 0.0055))
    return bm, p0, Mp


def b_agwi_throat_knot(c):
    hn = c.bone_rest("neck")[1]
    hh = c.bone_rest("Head")[1]
    target = hn.lerp(hh, 0.55)
    target.x = 0.0
    p0, n = c.surface(target, (FRONT + Vector((0, 0, -0.15))), 0.6, spread=0.012)
    Mp = G.frame(n, UP_Z)
    bm = G.new_bm()
    mc = Vector((0, 0, 0.012))
    v = G.ico(bm, 0.038, 3, Matrix.Translation(mc) @ Matrix.Diagonal((0.9, 1.1, 0.8, 1)))
    G.displace(v, mc, 0.0045, 60.0, seed=4)
    uc = Vector((0.006, 0.042, 0.006))
    v = G.ico(bm, 0.021, 2, Matrix.Translation(uc) @ Matrix.Diagonal((1.0, 0.9, 0.8, 1)))
    G.displace(v, uc, 0.003, 80.0, seed=5)
    lc = Vector((-0.004, -0.039, 0.008))
    v = G.ico(bm, 0.026, 2, Matrix.Translation(lc) @ Matrix.Diagonal((1.0, 0.9, 0.85, 1)))
    G.displace(v, lc, 0.0035, 70.0, seed=6)
    cord = []
    for k in range(17):
        a = math.radians(-88 + 176 * k / 16)
        cord.append(Vector((0.044 * math.sin(a), 0.004 * math.sin(3 * a) - 0.003, 0.010 + 0.034 * math.cos(a))))
    G.tube(bm, cord, 6, lambda i, t: (0.0068 * (0.85 + 0.3 * abs(math.sin(i * 1.7))),) * 2)
    return bm, p0, Mp


def b_changgui_claw_scar(c):
    s0 = Vector((-0.045, 0.0, 1.305))
    s1 = Vector((-0.195, 0.0, 1.470))
    d = (s1 - s0).normalized()
    perp = Vector((d.z, 0.0, -d.x))
    if perp.z < 0:
        perp = -perp
    mid = s0.lerp(s1, 0.5)
    p0, n0 = c.surface(mid, FRONT, 0.8, spread=0.02)
    Mp = G.frame(n0, UP_Z)
    bm = G.new_bm()
    strokes = ((-1, 0.04, 0.90), (0, 0.0, 1.0), (1, 0.06, 0.84))
    for k, (o, start, length) in enumerate(strokes):
        a = s0 + perp * (0.034 * o) + (s1 - s0) * start
        b = a + (s1 - s0) * length
        pts, sides, ups, ab = [], [], [], []
        N = 12
        for i in range(N):
            t = i / (N - 1)
            q = a.lerp(b, t)
            loc, nrm = c.surface(q, FRONT, 0.8, spread=0.006)
            pts.append(loc)
            ups.append(nrm)
        for i in range(N):
            tang = (pts[min(i + 1, N - 1)] - pts[max(i - 1, 0)]).normalized()
            sv = tang.cross(ups[i]).normalized()
            sides.append(sv)
            t = i / (N - 1)
            taper = max(0.22, math.sin(math.pi * t) ** 0.55)
            lump = 0.78 + 0.5 * abs(math.sin(i * 2.3 + k * 1.7))
            ab.append((0.0135 * taper * (0.9 + 0.2 * lump), 0.0105 * taper * lump))
        centers = [pts[i] + ups[i] * (ab[i][1] * 0.12) for i in range(N)]
        G.tube(bm, centers, 6, lambda i, t: ab[i], side_vecs=sides, up_vecs=ups)
    world_to_part(bm, p0, Mp, c.s)
    return bm, p0, Mp


def b_bulgasari_back_iron(c):
    p0, n0 = c.surface(Vector((0.0, -0.045, 0.0)), UP_Z, 1.0, spread=0.01)
    Mp = G.frame(n0, Vector((0, -1, 0)))
    bm = G.new_bm()
    spikes = ((-0.140, 0.000, 0.24, 0.050), (-0.105, 0.030, 0.30, 0.058), (-0.085, -0.032, 0.27, 0.056),
              (-0.045, 0.022, 0.33, 0.062), (-0.015, -0.026, 0.27, 0.056), (0.022, 0.012, 0.22, 0.050),
              (0.052, -0.012, 0.16, 0.044))
    s = c.s
    for k, (y, x, h_m, r_m) in enumerate(spikes):
        loc, nrm = c.surface(Vector((x, y, 0.0)), UP_Z, 1.0, spread=0.004)
        axis = (nrm * 0.35 + UP_Z * 0.65 + Vector((0, 0.28, 0)) + Vector((x * 4.0, 0, 0))).normalized()
        h, rb = h_m / s, r_m / s
        bend = Vector((0, 1, 0)) * h * 0.08
        pts = [loc - axis * rb * 0.7, loc + axis * h * 0.05, loc + axis * h * 0.45 + bend * 0.4,
               loc + axis * h * 0.82 + bend]
        rads = (rb * 0.95, rb, rb * 0.55, rb * 0.16)
        G.tube(bm, pts, 6, lambda i, t, rads=rads: (rads[i], rads[i]), phase=0.25 * k)
        cm = Matrix.Translation(loc + axis * rb * 0.05) @ G.frame(axis, Vector((0, 1, 0))).to_4x4() @ Matrix.Diagonal((1.0, 1.0, 0.42, 1))
        v = G.ico(bm, rb * 1.4, 1, cm)
        G.displace(v, loc, rb * 0.12, 9.0 / rb, seed=10 + k)
    world_to_part(bm, p0, Mp, s)
    return bm, p0, Mp


def b_bulgasari_jaw_iron(c):
    m = c.bone_rest("MouthOrigin")[1]
    target = m + Vector((0, 0.014, -0.016))
    p0, n = c.surface(target, Vector((0, -1, -0.6)), 0.6, spread=0.006)
    Mp = G.frame(n, UP_Z)
    bm = G.new_bm()
    for k, (r, sc, pos, sub) in enumerate((
            (0.078, (1.2, 0.85, 0.8), (0.000, -0.020, 0.044), 3),
            (0.056, (1.0, 0.9, 0.85), (0.074, -0.050, 0.032), 2),
            (0.050, (0.9, 1.0, 0.9), (-0.068, -0.044, 0.036), 2),
            (0.040, (1.0, 0.8, 1.0), (0.012, -0.098, 0.038), 2))):
        cc = Vector(pos)
        v = G.ico(bm, r, sub, Matrix.Translation(cc) @ Matrix.Diagonal((*sc, 1)))
        G.displace(v, cc, r * 0.14, 1.6 / r, seed=20 + k)
    for (pos, rot, ln) in (((0.040, 0.004, 0.080), (25, 0, 30), 0.10), ((-0.036, -0.062, 0.072), (-20, 10, -35), 0.085)):
        bmesh.ops.create_cube(bm, size=1.0, matrix=Matrix.Translation(Vector(pos)) @
                              Matrix.Rotation(math.radians(rot[2]), 4, 'Z') @ Matrix.Rotation(math.radians(rot[0]), 4, 'X') @
                              Matrix.Diagonal((0.014, 0.014, ln, 1)))
    return bm, p0, Mp


def _snout(c):
    q, hh, ht = c.bone_rest("Head")
    return (ht - hh).normalized()


def b_fox_bead(c):
    snout = _snout(c)
    m = c.bone_rest("MouthOrigin")[1]
    tip, _ = c.surface(m, snout, 0.25)
    rr = 0.038
    r_b = rr / c.s
    center = tip - snout * (r_b * 0.55) - UP_Z * (r_b * 0.35)
    Mp = G.frame(snout, UP_Z)
    bm = G.new_bm()
    G.uvsphere(bm, rr, 18, 10, Matrix.Identity(4))
    return bm, center, Mp


def b_fox_tail_bead(c):
    _, hh, ht = c.bone_rest("Tail_05")
    d = (ht - hh).normalized()
    tip, _ = c.surface(ht, d, 0.25)
    rr = 0.048
    center = tip + d * (rr / c.s * 0.25)
    Mp = G.frame(d, UP_Z)
    bm = G.new_bm()
    G.uvsphere(bm, rr, 18, 10, Matrix.Identity(4))
    return bm, center, Mp


def b_imugi_socket(c):
    p0, n0 = c.surface(Vector((0.0, -0.474, 0.060)), Vector((0, -0.35, -1.0)), 0.5, spread=0.003)
    n = (n0 + FRONT * 0.7).normalized()
    Mp = G.frame(n, UP_Z)
    bm = G.new_bm()
    prof = [(0.0, -0.026), (0.068, -0.020), (0.077, 0.004), (0.070, 0.030), (0.057, 0.050), (0.048, 0.057),
            (0.038, 0.051), (0.026, 0.038), (0.013, 0.031), (0.0, 0.029)]
    G.lathe(bm, prof, 14, sx=1.0, sy=0.9)
    outer = [v for v in bm.verts if v.co.z < 0.045 and v.co.xy.length > 0.05]
    G.displace(outer, Vector((0, 0, 0)), 0.004, 40.0, seed=30)
    return bm, p0, Mp


def b_imugi_yeokrin(c):
    p0, n0 = c.surface(Vector((0.0, -0.468, 0.060)), Vector((0, -0.35, -1.0)), 0.5, spread=0.003)
    n = (n0 + FRONT * 0.75).normalized()
    Mp = G.frame(n, Vector((0, -1, 0)))
    bm = G.new_bm()
    lc = Vector((0, 0, 0.002))
    v = G.ico(bm, 0.058, 3, Matrix.Translation(lc) @ Matrix.Diagonal((1.0, 1.2, 0.5, 1)))
    G.displace(v, lc, 0.004, 50.0, seed=31)
    outline = [(x * 1.3, y * 1.3) for x, y in (
        (0.0, 0.068), (0.026, 0.040), (0.042, 0.014), (0.045, -0.014), (0.032, -0.038), (0.012, -0.044),
        (0.0, -0.032), (-0.012, -0.044), (-0.032, -0.038), (-0.045, -0.014), (-0.042, 0.014), (-0.026, 0.040))]

    def lift(x, y):
        return 0.022 * max(0.0, y) / 0.088
    bot = [bm.verts.new(Vector((x, y, 0.020 + lift(x, y)))) for x, y in outline]
    top = [bm.verts.new(Vector((x * 0.96, y * 0.96, 0.036 + lift(x, y) * 1.3))) for x, y in outline]
    ct = bm.verts.new(Vector((0, 0.008, 0.046)))
    cb = bm.verts.new(Vector((0, 0.008, 0.018)))
    nn = len(outline)
    for i in range(nn):
        j = (i + 1) % nn
        bm.faces.new((bot[i], bot[j], top[j], top[i]))
        bm.faces.new((top[i], top[j], ct))
        bm.faces.new((bot[j], bot[i], cb))
    return bm, p0, Mp


def b_tree_knot(c):
    h = 0.62
    band = [v for v in c.bark if abs(v.z - h) < 0.06]
    cx = sum(v.x for v in band) / len(band)
    cy = sum(v.y for v in band) / len(band)
    p0, n = c.surface(Vector((cx, cy, h)), c.front, 1.5, spread=0.02)
    Mp = G.frame(n, UP_Z)
    bm = G.new_bm()
    prof = [(0.0, -0.022), (0.072, -0.016), (0.078, 0.006), (0.068, 0.026), (0.053, 0.040), (0.043, 0.044),
            (0.033, 0.038), (0.023, 0.024), (0.011, 0.016), (0.0, 0.014)]
    G.lathe(bm, [(r * 1.15, h * 1.1) for r, h in prof], 16, sx=0.8, sy=1.18, twist=0.07)
    G.displace(list(bm.verts), Vector((0, 0, 0)), 0.007, 26.0, seed=40)
    return bm, p0, Mp


# ---------------------------------------------------------------- catalogue (data; all values TEST)
ENTRIES = {
    "dokkaebi": dict(rig="Humanoid", parts=[dict(
        id="dokkaebi_club_knot", organ="club_knot", bone="Hips", human="Hips", role="Core", keys=["*"],
        color="#5E4330", metallic=0.0, smoothness=0.12, build=b_dokkaebi_club_knot, az=40,
        ko="도깨비 — 허리 샅바에 꿴 손때 묻은 방망이 옹이(헌 나무 토막)",
        lore="folklore-enemies-298 §1: 오래 쓰던 물건이 변한 존재, 씨름꾼 각색. 뿔·철퇴 불사용.")]),
    "agwi": dict(rig="Humanoid", parts=[dict(
        id="agwi_throat_knot", organ="throat_knot", bone="neck", human="Neck", role="Mouth", keys=["*"],
        color="#4A3B36", metallic=0.0, smoothness=0.10, build=b_agwi_throat_knot, az=40,
        ko="아귀 — 가는 목의 목울대 응어리(좁은 목구멍 매듭)",
        lore="folklore-enemies-298 §2: 큰 배·좁은 목구멍.")]),
    "changgui": dict(rig="Humanoid", parts=[dict(
        id="changgui_claw_scar", organ="claw_scar", bone="Spine", human="UpperChest", role="Chest", keys=["*"],
        color="#2F2A26", metallic=0.0, smoothness=0.15, build=b_changgui_claw_scar, az=-40,
        ko="창귀 — 앞가슴에서 어깨로 걸친 범 발톱 자국 세 줄의 굳은 먹 혹(창작 표시)",
        lore="folklore-enemies-298 §3: 호환 희생자의 넋. 상처 위치는 원전에 없음 → 창작.")]),
    "bulgasari": dict(rig="Generic", parts=[
        dict(id="bulgasari_back_iron", organ="back_iron", bone="Spine", human=None, role="Core", keys=["*"],
             color="#3C3F44", metallic=0.2, smoothness=0.18, build=b_bulgasari_back_iron, az=35,
             ko="불가사리 — 등줄기에 돋은 쇠가시 다발",
             lore="folklore-enemies-298 §4: 쇠를 먹고 자람. 몸 전체 금속 융합 금지 → 국소 가시."),
        dict(id="bulgasari_jaw_iron", organ="jaw_iron", bone="MouthOrigin", fallback="Head", human=None, role="Mouth",
             keys=["*"], color="#3C3F44", metallic=0.2, smoothness=0.18, build=b_bulgasari_jaw_iron, az=35,
             ko="불가사리 — 턱의 쇠 덩이",
             lore="folklore-enemies-298 §4: 입과 금속 덩어리는 독립 제어.")]),
    "fox_spirit": dict(rig="Generic", parts=[
        dict(id="fox_spirit_bead", organ="fox_bead", bone="MouthOrigin", fallback="Head", human=None, role="Mouth",
             keys=["*"], color="#D8D0BC", metallic=0.0, smoothness=0.2, build=b_fox_bead, az=40,
             ko="여우 귀물 — 입에 문 여우구슬",
             lore="folklore-enemies-298 §5: 입속 구슬이 설화의 핵심."),
        dict(id="fox_spirit_tail_bead", organ="tail_bead", bone="Tail_05", human=None, role="Core", keys=["*"],
             color="#D8D0BC", metallic=0.0, smoothness=0.2, build=b_fox_tail_bead, az=140, alt_of="fox_spirit_bead",
             ko="[대안] 여우 귀물 — 꼬리 끝 여우구슬(문답 예시안)",
             lore="조사 근거 없음(§5 '꼬리 끝 구슬 근거 없음') — 사용자 예시안을 비교용으로만 제작.")]),
    "imugi": dict(rig="Generic", parts=[
        dict(id="imugi_yeouiju_socket", close_el=-28, close_az=30, organ="yeouiju_socket", bone="MouthOrigin", fallback="Head", human=None,
             role="Mouth", keys=["*"], color="#8C8670", metallic=0.0, smoothness=0.15, build=b_imugi_socket, az=35,
             ko="이무기 — 턱 아래 덜 여문 여의주 자리(빈 구슬 혹)",
             lore="folklore-enemies-298 §6 + 일반 속설(INFERRED). NARR-TRUTH 서사 검토 대상."),
        dict(id="imugi_yeokrin", close_el=-28, close_az=30, organ="yeokrin", bone="MouthOrigin", fallback="Head", human=None, role="Mouth",
             keys=["*"], color="#7A7560", metallic=0.0, smoothness=0.15, build=b_imugi_yeokrin, az=35,
             alt_of="imugi_yeouiju_socket",
             ko="[대안] 이무기 — 턱밑 역린 혹(거꾸로 난 비늘)",
             lore="여의주 자리가 NARR-TRUTH(몰락한 황룡)를 미리 드러낼 위험의 대안. 역린 = 용의 턱밑 거꾸로 난 비늘.")]),
    "growth_tree": dict(rig="None", parts=[dict(
        id="growth_trunk_knot", organ="trunk_knot", bone="Tree_00", human=None, role="Core", keys=["ground", "*"],
        color="#4B3A2C", metallic=0.0, smoothness=0.1, build=b_tree_knot, az=35,
        ko="성장 교습 — 줄기 앞면의 옹이(나무의 눈)",
        lore="목 생장 교습. FALLBACK 오프셋 기관 둘(core·mouth)을 이 앵커 하나로.")]),
}


# ---------------------------------------------------------------- realise / place / export / render
def make_object(spec, bm, p0, Mp, s):
    G.finish(bm)
    tris = G.tri_count(bm)
    name = "SM_Organ308_" + spec["id"]
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    for p in me.polygons:
        p.use_smooth = True
    try:
        me.set_sharp_from_angle(angle=math.radians(42))
    except Exception:
        pass
    G.project_uv(me)
    mat = C.flat_material("M_Organ308_" + spec["id"], lin(spec["color"]), metallic=spec["metallic"],
                          roughness=1.0 - spec["smoothness"])
    me.materials.append(mat)
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    ob.matrix_world = Matrix.Translation(p0) @ Mp.to_4x4() @ Matrix.Scale(1.0 / s, 4)
    return ob, tris, mat


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


def placement(c, spec, ob, tris, p0, Mp):
    me = ob.data
    loc = [v.co.copy() for v in me.vertices]  # part-local metres
    world = [ob.matrix_world @ v for v in loc]
    n = Mp.col[2].normalized()
    h = max((w - p0).dot(n) for w in world)
    ps = p0 + n * h
    lo, hi = C.bounds(world)
    bc = (lo + hi) * 0.5
    brad = max((w - bc).length for w in world)
    ulo, uhi = C.bounds([R @ v for v in loc])  # expected Unity mesh-local bounds (metres)
    notes = []
    if c.tree:
        k = 1.0 / TREE_WORLD_SCALE
        to_local = lambda p: R @ p * k  # noqa: E731
        Q = R @ Mp @ R.transposed()
        lossy = TREE_WORLD_SCALE
        bone = dict(name="Tree_00", humanBone=None, path="demo_growth_lesson/Chapter3_Visual/Tree_00",
                    note="scene object (W_Demo_Main / candidates), NOT PF_PlantedTree.prefab (shared by spell 검)")
        residual = 0.0
        rot_err = 0.0
        root_pos = None
        front_dot = n.dot(c.front)
    else:
        bname = spec["bone"]
        Mb, hb, _ = c.bone_rest(bname)
        W, lossy, path = c.bone_prefab(bname)
        k = c.s / lossy
        A = D @ Mb.transposed()
        to_local = lambda p: A @ (p - hb) * k  # noqa: E731
        Q = A @ Mp @ R.transposed()
        lp = to_local(p0)
        pu = (R @ p0) * c.s + c.t
        pw = UP.apply(W, list(lp))
        residual = (Vector(pw) - pu).length
        Wr = Matrix([W[i][:3] for i in range(3)]).to_quaternion().to_matrix()
        rot_err = math.degrees((Wr @ Q).to_quaternion().rotation_difference((R @ Mp @ R.transposed()).to_quaternion()).angle)
        if residual > 0.005:
            notes.append("prefab bone pose differs from FBX bind by %.3f m here; local values use the bind pose" % residual)
        root_pos = r6(pu)
        bone = dict(name=bname, humanBone=spec.get("human"), path=path, fallback=spec.get("fallback"))
        front_dot = n.dot(c.front)
    q = Q.to_quaternion()
    if abs(Q.determinant() - 1.0) > 1e-3:
        raise RuntimeError("non-proper rotation for " + spec["id"])
    lp = to_local(p0)
    nl = (to_local(p0 + n) - lp).normalized()
    return DP.apply(dict(
        id=spec["id"], organId=spec["organ"], variant="alternate" if spec.get("alt_of") else "primary",
        alternateOf=spec.get("alt_of"), actor=("PF_" + c.entry) if not c.tree else "demo_growth_lesson",
        species=c.entry, rig=ENTRIES[c.entry]["rig"],
        prefab=None if c.tree else "Assets/_Project/Art/Characters/Folklore298/Prefabs/PF_%s.prefab" % c.entry,
        bone=bone, role=spec["role"], attackKeys=spec["keys"], label=spec["ko"], lore=spec["lore"],
        fbx="Art/Characters/Organ308/Parts/SM_Organ308_%s.fbx" % spec["id"],
        deployFbx="%s/%s/SM_Organ308_%s.fbx" % (UNITY_DEPLOY, spec["id"], spec["id"]),
        material=dict(name="M_Organ308_" + spec["id"], deployPath="%s/%s/M_Organ308_%s.mat" % (UNITY_DEPLOY, spec["id"], spec["id"]),
                      shader="Universal Render Pipeline/Lit", baseColorSRGB=spec["color"], metallic=spec["metallic"],
                      smoothness=spec["smoothness"], emission=False, emissionColor="#000000", castShadows=True, collider=False),
        local=dict(position=r6(lp), rotation=r6((q.x, q.y, q.z, q.w)), scale=r6((1.0 / lossy,) * 3),
                   parentLossyScale=round(lossy, 6), frame="Unity bone-local at FBX bind pose"),
        rootSpace=dict(position=root_pos, note="prefab-root space at bind (Visual scale applied); check only") if not c.tree else None,
        organ=dict(surfaceLocalPoint=r6(to_local(ps)), surfaceLocalNormal=r6(nl), boundsCenterLocal=r6(to_local(bc)),
                   boundingRadiusWorldM=round(brad * c.s, 4), suggestedRadiusWorldM=round(brad * c.s * 1.05 + 0.005, 3),
                   boundingRadiusLocal=round(brad * c.s / lossy, 5), mode="Part"),
        mesh=dict(triangles=tris, vertices=len(loc), units="metres", unityMeshBoundsCenter=r6((ulo + uhi) * 0.5),
                  unityMeshBoundsSize=r6(uhi - ulo), pivot="attachment point on body surface (bead: bead centre)"),
        check=dict(prefabBindResidualM=round(residual, 5), prefabBindRotResidualDeg=round(rot_err, 3), frontDotBind=round(front_dot, 3), notes=notes),
        status="TEST",
    ))


def render_tiles(c, ob, spec, hide, mat):
    TILE_DIR.mkdir(parents=True, exist_ok=True)
    C.setup_render(372, 372)
    body = C.flat_material("Review_Body", (0.80, 0.78, 0.74, 1))
    leaf = C.flat_material("Review_Leaf", (0.70, 0.76, 0.66, 1))
    for m in c.meshes:
        m.data.materials.clear()
        m.data.materials.append(leaf if (c.tree and m.name != "Tree_00_sub2") else body)
    for o, hidden in hide:
        o.hide_render = hidden
    hi_mat = C.flat_material("Review_Highlight", HIGHLIGHT)
    if c.entry == "imugi":
        center, radius = Vector((0.0, -0.36, 0.0)), 0.17
    elif c.tree:
        center, radius = (c.lo + c.hi) * 0.5, 0.58 * max(c.hi - c.lo)
    else:
        center, radius = (c.lo + c.hi) * 0.5, 0.56 * max(c.hi - c.lo)
    ob.data.materials[0] = hi_mat
    C.render_view(TILE_DIR / (spec["id"] + "_front.png"), center, radius, 0, 6, front=c.front)
    C.render_view(TILE_DIR / (spec["id"] + "_q34.png"), center, radius, spec["az"], 14, front=c.front)
    ob.data.materials[0] = mat
    world = [ob.matrix_world @ v.co for v in ob.data.vertices]
    lo, hi = C.bounds(world)
    pc = (lo + hi) * 0.5
    pr = max(max(hi - lo) * 1.8, 0.25 / c.s)
    C.render_view(TILE_DIR / (spec["id"] + "_close.png"), pc, pr, spec.get("close_az", spec["az"]),
                  spec.get("close_el", 16), front=c.front)


def main():
    entry = sys.argv[sys.argv.index("--") + 1]
    c = Ctx(entry)
    built = []
    for spec in ENTRIES[entry]["parts"]:
        bm, p0, Mp = spec["build"](c)
        ob, tris, mat = make_object(spec, bm, p0, Mp, c.s)
        if tris > 1500:
            raise RuntimeError("%s has %d triangles (> 1500)" % (spec["id"], tris))
        built.append((spec, ob, tris, mat, p0, Mp))
    rows = []
    for spec, ob, tris, mat, p0, Mp in built:
        rows.append(placement(c, spec, ob, tris, p0, Mp))
        src = PARTS_DIR / ("SM_Organ308_%s.fbx" % spec["id"])
        export_fbx(ob, src)
    for spec, ob, tris, mat, p0, Mp in built:
        # primaries stay visible together; an alternate replaces its primary
        hide = []
        for s2, o2, *_ in built:
            if o2 is ob:
                hide.append((o2, False))
            elif s2.get("alt_of"):
                hide.append((o2, True))
            else:
                hide.append((o2, spec.get("alt_of") == s2["id"]))
        render_tiles(c, ob, spec, hide, mat)
    frag = C.OUT / "Analysis" / ("%s_placement.json" % entry)
    frag.write_text(json.dumps(rows, ensure_ascii=False, indent=1), encoding="utf-8")
    print("ORGAN308_BUILT " + json.dumps([(r["id"], r["mesh"]["triangles"], r["check"]) for r in rows], ensure_ascii=False))


main()
