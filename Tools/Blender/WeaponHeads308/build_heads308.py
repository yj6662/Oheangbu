"""#308 D308-127 2단계: 무기 변신 머리 넷(장창 · 부월 · 쌍수도 · 편곤). 각궁은 5단계(붓 전체를 바꾸고 한 손 쥐기가 필요).
    blender -b --factory-startup -P build_heads308.py -- <out_dir> [--spear-ext 1.58] [--sword-blade 1.2] [--sword-width .17] [--axe-ext .35]
원점 = 큰 붓의 페룰(붓털 뿌리, GripSocket +.57 m). +Z = 자루 축(털끝 쪽). Unity 에서는 GripSocket 의 자식으로 붙는다(+Y = 자루 축).
머리는 붓털의 먹이 굳은 것: 무광 먹(칸 0) + 날 가장자리 안료 띠(칸 1, 머리 면적의 15 % 이내). 발광 · 금속 반사 없음(재질은 Unity 에서).
X = 날이 옆으로 나가는 쪽(부월 날 · 칼날 · 창 잎 폭), Y = 날 두께.
편곤: 빈 물체 Head_Flail 아래 Head_Flail_Swing(원점 = 페룰) 하나에 사슬 + 막대. Unity 가 Swing 을 돌려 매단다(4단계 Verlet 전 임시).
build-report.json = 머리마다 길이 · 최대 폭 · 두께 · 삼각형 · 안료 면적 몫(Unity 검사가 읽는다)."""
import bpy, bmesh, json, math, os, sys, random
from mathutils import Vector

ARGV = sys.argv[sys.argv.index('--') + 1:]
OUT = ARGV[0]; os.makedirs(OUT, exist_ok=True)


def arg(name, default): return float(ARGV[ARGV.index(name) + 1]) if name in ARGV else default


SPEAR_EXT = arg('--spear-ext', 1.58); SPEAR_LEAF = arg('--spear-leaf', .45); SPEAR_WIDTH = arg('--spear-width', .085)
SWORD_BLADE = arg('--sword-blade', 1.2); SWORD_WIDTH = arg('--sword-width', .17)
AXE_EXT = arg('--axe-ext', .35); AXE_H = arg('--axe-height', .40); AXE_W = arg('--axe-width', .30)
FLAIL_CHAIN = arg('--flail-chain', .45); FLAIL_BAR = arg('--flail-bar', .35); FLAIL_R = arg('--flail-radius', .034)
R_POLE = .016                      # 먹 자루 연장 반지름(붓 자루 .017 보다 조금 가늘게)
# debate 13 (Codex · Fable): the element did not read on the still heads - pigment moved to what the camera sees (wider edge
# bands, the sword's guard, a ring at the root), the share brought to 12-15 % (the 15 % cap stays)
BAND_SPEAR = arg('--band-spear', .012); BAND_SWORD = arg('--band-sword', .010)
R_ROOT = .045                      # 붓털 뿌리 반지름: 밑동 먹 매듭이 이것을 덮는다
rnd = random.Random(1271)
MATS = ['M_WeaponHead308_Ink', 'M_WeaponHead308_Pigment']
INK, PIG = 0, 1

bpy.ops.wm.read_factory_settings(use_empty=True)
mats = []
for name, col in zip(MATS, [(.012, .011, .011), (.30, .30, .30)]):
    m = bpy.data.materials.new(name); m.diffuse_color = (*col, 1); m.roughness = 1.0; m.metallic = 0.0; mats.append(m)


def new_mesh(name):
    me = bpy.data.meshes.new(name); me.materials.append(mats[INK]); me.materials.append(mats[PIG]); return me


def ring(bm, z, r, n, jitter=0.0, ox=0.0, oy=0.0, sx=1.0, sy=1.0):
    vs = []
    for i in range(n):
        a = 2 * math.pi * i / n; j = 1 + (rnd.uniform(-jitter, jitter) if jitter else 0)
        vs.append(bm.verts.new((ox + math.cos(a) * r * sx * j, oy + math.sin(a) * r * sy * j, z)))
    return vs


def bridge(bm, a, b, mat=INK, closed=True):
    n = len(a)
    for i in range(n if closed else n - 1):
        j = (i + 1) % n
        f = bm.faces.new((a[i], a[j], b[j], b[i])); f.material_index = mat; f.smooth = True


def cap(bm, vs, flip=False, mat=INK):
    f = bm.faces.new(vs[::-1] if flip else vs); f.material_index = mat


def lathe(bm, profile, n=10, jitter=.02, mat=INK, close_bottom=True, close_top=True):
    """profile = [(z, r)] from bottom to top; a ring per point, bridged."""
    rings = [ring(bm, z, r, n, jitter) for z, r in profile]
    for k in range(len(rings) - 1): bridge(bm, rings[k], rings[k + 1], mat)
    if close_bottom: cap(bm, rings[0], flip=True, mat=mat)
    if close_top: cap(bm, rings[-1], mat=mat)
    return rings


def root_knot(bm, top=.06):
    """the bristle root turned to ink: covers the ferrule's open end (r .045) and narrows into the head. debate 13 (Fable): a
    2 cm pigment ring at the bottom, seen from every side."""
    rings = [ring(bm, z, r, 12, .03) for z, r in [(-.004, R_ROOT * .98), (.004, R_ROOT * 1.02), (.024, R_ROOT * 1.02), (top * .55, R_ROOT * .72), (top, R_POLE * 1.25)]]
    for k in range(len(rings) - 1): bridge(bm, rings[k], rings[k + 1], PIG if k == 1 else INK)
    cap(bm, rings[0], flip=True); cap(bm, rings[-1])


def blade(bm, stations, edges_both, band, mat_body=INK):
    """stations = [(z, x_back, x_edge, t)]: a blade cross-section per station in the XY plane at height z.
    x_back = spine side (negative x), x_edge = cutting side (positive x), t = half thickness at the spine.
    Double-edged (edges_both): both sides are edges (a lens). The outer `band` (m) of every edge is pigment."""
    rows = []
    for z, xb, xe, t in stations:
        w = xe - xb; b = min(band, w * .25)
        if edges_both:
            mid = (xb + xe) * .5
            pts = [(xb, 0), (xb + b, t * .18), (mid, t), (xe - b, t * .18), (xe, 0), (xe - b, -t * .18), (mid, -t), (xb + b, -t * .18)]
        else:
            pts = [(xb, t), (xe - b, t * .22), (xe, 0), (xe - b, -t * .22), (xb, -t)]
        rows.append([bm.verts.new((x, y, z)) for x, y in pts])
    n = len(rows[0])
    ev = (0, 4) if edges_both else (2,)   # the edge vertices; the faces that touch one are the narrow pigment band
    for k in range(len(rows) - 1):
        a, c = rows[k], rows[k + 1]
        for i in range(n):
            j = (i + 1) % n
            f = bm.faces.new((a[i], a[j], c[j], c[i])); f.smooth = False
            f.material_index = PIG if (i in ev or j in ev) else mat_body
    cap(bm, rows[0], flip=True); cap(bm, rows[-1])
    return rows


def loop_link(bm, zc, half_len, half_w, rot, wire):
    """one chain link: an elongated loop of square wire in the plane through the Z axis turned by `rot`."""
    side = Vector((math.cos(rot), math.sin(rot), 0)); plane_n = Vector((-math.sin(rot), math.cos(rot), 0))
    path = [Vector((0, 0, zc)) + side * (math.cos(2 * math.pi * i / 10) * half_w) + Vector((0, 0, math.sin(2 * math.pi * i / 10) * half_len)) for i in range(10)]
    secs = []
    for i, p in enumerate(path):
        tangent = (path[(i + 1) % 10] - path[i - 1]).normalized(); inward = tangent.cross(plane_n).normalized()
        secs.append([bm.verts.new(p + inward * dx * wire + plane_n * dy * wire) for dx, dy in ((1, 1), (-1, 1), (-1, -1), (1, -1))])
    for i in range(10):
        a, c = secs[i], secs[(i + 1) % 10]
        for q in range(4):
            r = (q + 1) % 4
            f = bm.faces.new((a[q], a[r], c[r], c[q])); f.material_index = INK; f.smooth = True


def finish(name, bm, parent=None):
    me = new_mesh(name); bm.normal_update(); bm.to_mesh(me); bm.free()
    ob = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(ob)
    if parent is not None: ob.parent = parent
    return ob


def empty(name, parent=None):
    e = bpy.data.objects.new(name, None); e.empty_display_type = 'ARROWS'; e.empty_display_size = .05
    bpy.context.scene.collection.objects.link(e)
    if parent is not None: e.parent = parent
    return e


# ---- 장창: 밑동 매듭 → 먹 자루 연장 → 잎 받침 → 좁은 잎(양날) ----
bm = bmesh.new(); root_knot(bm)
lathe(bm, [(.05, R_POLE * 1.1), (SPEAR_EXT * .5, R_POLE), (SPEAR_EXT, R_POLE * .95)], n=8, jitter=.015, close_bottom=False, close_top=False)
z0 = SPEAR_EXT
lathe(bm, [(z0 - .02, R_POLE * 1.05), (z0 + .02, .021), (z0 + .055, .017), (z0 + .07, .012)], n=8, jitter=.02, mat=PIG)   # 잎 받침 — 안료(토론 13)
st = []
N = 12
for k in range(N + 1):
    s = k / N; z = z0 + .06 + SPEAR_LEAF * s
    w = SPEAR_WIDTH * .5 * (math.sin(math.pi * min(1.0, s / .42) * .5) if s < .42 else (1 - ((s - .42) / .58) ** 1.6))
    w = max(w, .0015); st.append((z, -w, w, .007 * (1 - .75 * s) + .002))
blade(bm, st, True, BAND_SPEAR)
spear = finish('Head_Spear', bm)

# ---- 쌍수도: 밑동 매듭 → 코등이 → 곧은 외날(등 · 날), 끝은 날 쪽이 등으로 모이는 뾰족 ----
bm = bmesh.new(); root_knot(bm, .05)
lathe(bm, [(.045, .02), (.05, .07), (.075, .07), (.08, .02)], n=12, jitter=.01, mat=PIG)   # 코등이(둥근 테) — 안료(토론 13)
st = []; N = 16; tip = .14
for k in range(N + 1):
    s = k / N; z = .08 + SWORD_BLADE * s
    wb, we = -SWORD_WIDTH * .42, SWORD_WIDTH * .58
    if z > .08 + SWORD_BLADE - tip:
        u = (z - (.08 + SWORD_BLADE - tip)) / tip; we = wb + (we - wb) * max(.02, 1 - u ** 1.3)
    st.append((z, wb, we, .011 * (1 - .4 * s) + .003))
blade(bm, st, False, BAND_SWORD)
sword = finish('Head_Sword', bm)

# ---- 부월: 밑동 매듭 → 먹 자루 연장 → 자루에 붙은 넓은 외날(볼록한 날, 낫 아님) → 자루 끝 마개 ----
bm = bmesh.new(); root_knot(bm)
top = AXE_EXT + AXE_H + .06
lathe(bm, [(.05, R_POLE * 1.1), (AXE_EXT + .02, R_POLE * 1.15), (AXE_EXT + AXE_H * .5, .022), (top - .02, .018), (top, .012)], n=8, jitter=.015, close_bottom=False)
# blade: u from the shaft (0) to the edge (1), v from the lower horn (0) to the upper horn (1)
NU, NV = 7, 9; grid = []
zb, zt = AXE_EXT, AXE_EXT + AXE_H
for i in range(NU + 1):
    u = i / NU; row = []
    for j in range(NV + 1):
        v = j / NV
        inner_z = AXE_EXT + AXE_H * (.28 + .44 * v)              # 자루에 붙는 목(높이의 44 %)
        outer_z = zb + AXE_H * v                                 # 날은 위아래 뿔까지 다 편다
        z = inner_z + (outer_z - inner_z) * (u ** .9)
        x = .018 + (AXE_W - .018) * u + (.03 * math.sin(math.pi * v) * u ** 3)   # 볼록한 날(가운데가 조금 더 나온다)
        t = .016 * (1 - u) ** .8 + .0025
        row.append((x, t, z))
    grid.append(row)
top_v = [[bm.verts.new((x, t, z)) for x, t, z in row] for row in grid]
bot_v = [[bm.verts.new((x, -t, z)) for x, t, z in row] for row in grid]
for i in range(NU):
    m = PIG if i >= NU - 1 else INK
    for j in range(NV):
        f = bm.faces.new((top_v[i][j], top_v[i + 1][j], top_v[i + 1][j + 1], top_v[i][j + 1])); f.material_index = m
        f = bm.faces.new((bot_v[i][j + 1], bot_v[i + 1][j + 1], bot_v[i + 1][j], bot_v[i][j])); f.material_index = m
for j in range(NV):                        # 날 끝(얇은 띠) · 자루 쪽 면
    f = bm.faces.new((top_v[NU][j], bot_v[NU][j], bot_v[NU][j + 1], top_v[NU][j + 1])); f.material_index = PIG
    f = bm.faces.new((top_v[0][j + 1], bot_v[0][j + 1], bot_v[0][j], top_v[0][j])); f.material_index = INK
for i in range(NU):                        # 위 · 아래 테
    m = PIG if i >= NU - 1 else INK
    f = bm.faces.new((top_v[i][0], bot_v[i][0], bot_v[i + 1][0], top_v[i + 1][0])); f.material_index = m
    f = bm.faces.new((top_v[i + 1][NV], bot_v[i + 1][NV], bot_v[i][NV], top_v[i][NV])); f.material_index = m
axe = finish('Head_Axe', bm)

# ---- 편곤: 밑동 매듭(머리 루트) + Swing(페룰 원점) 아래 꼬인 먹 가닥 사슬 고리 6 + 막대(양 끝 안료 테) ----
flail = empty('Head_Flail')
bm = bmesh.new(); root_knot(bm, .045); finish('Head_Flail_Knot', bm, flail)
bm = bmesh.new()
L = FLAIL_CHAIN / 6
for k in range(6):                         # 고리 = 꼬인 먹 가닥 고리(번갈아 90도), 이음이 보이게 조금씩 겹친다
    loop_link(bm, .045 + L * (k + .5), L * .62, .017, (k % 2) * math.pi / 2, .0045)
zb_ = .045 + FLAIL_CHAIN
prof = [(zb_, FLAIL_R * .55), (zb_ + .02, FLAIL_R * .95), (zb_ + .05, FLAIL_R), (zb_ + FLAIL_BAR * .5, FLAIL_R * 1.08), (zb_ + FLAIL_BAR - .05, FLAIL_R), (zb_ + FLAIL_BAR - .02, FLAIL_R * .95), (zb_ + FLAIL_BAR, FLAIL_R * .55)]   # 끝 테 3 cm(토론 13)
rings_ = [ring(bm, z, r, 10, .02) for z, r in prof]
for k in range(len(rings_) - 1): bridge(bm, rings_[k], rings_[k + 1], PIG if k in (1, len(rings_) - 3) else INK)
cap(bm, rings_[0], flip=True); cap(bm, rings_[-1])
swing = finish('Head_Flail_Swing', bm, flail)

# ---- report ----
def measure(obs):
    tris = 0; area = [0.0, 0.0]; lo = Vector((1e9, 1e9, 1e9)); hi = -lo
    for ob in obs:
        me = ob.data; me.calc_loop_triangles(); tris += len(me.loop_triangles)
        for p in me.polygons: area[p.material_index] += p.area
        for v in me.vertices: lo = Vector(map(min, lo, v.co)); hi = Vector(map(max, hi, v.co))
    return dict(tris=tris, length=round(hi.z, 4), widthX=round(hi.x - lo.x, 4), maxX=round(hi.x, 4), thicknessY=round(hi.y - lo.y, 4),
                pigmentShare=round(area[1] / max(1e-9, area[0] + area[1]), 4))
report = dict(version='heads308-v1', params=dict(spear_ext=SPEAR_EXT, spear_leaf=SPEAR_LEAF, spear_width=SPEAR_WIDTH, sword_blade=SWORD_BLADE, sword_width=SWORD_WIDTH,
              axe_ext=AXE_EXT, axe_height=AXE_H, axe_width=AXE_W, flail_chain=FLAIL_CHAIN, flail_bar=FLAIL_BAR, flail_radius=FLAIL_R),
              origin='ferrule (GripSocket +0.57 m), +Z = shaft axis toward the tip; Unity +Y', materials=MATS,
              heads=dict(Head_Spear=measure([spear]), Head_Sword=measure([sword]), Head_Axe=measure([axe]), Head_Flail=measure([c for c in flail.children])))
json.dump(report, open(os.path.join(OUT, 'build-report.json'), 'w', encoding='utf-8'), indent=1, ensure_ascii=False)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, 'SM_WeaponHeads308.blend'))
for o in bpy.context.scene.objects: o.select_set(True)
bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, 'SM_WeaponHeads308.fbx'), use_selection=True, object_types={'MESH', 'EMPTY'}, bake_anim=False,
                         axis_forward='-Z', axis_up='Y', apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', use_mesh_modifiers=False)
print('HEADS', json.dumps(report['heads']))
