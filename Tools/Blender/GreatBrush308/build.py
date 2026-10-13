"""#308 GreatBrush: 양손 큰 붓 SM_GreatBrush308 을 절차식으로 만든다(재현 가능, 입력 파일 없음).
    blender -b --factory-startup -P build.py -- <out_dir>
축 계약(SM_DosaBrushV2 와 같다): 붓은 Blender +Z(내보낸 Unity +Y)로 붓끝을 향한다. 원점 = GripSocket. 소켓의 로컬 +Y 가 붓끝 쪽.
뿌리 · 붓털 뼈 배율 1. 수치는 SPEC-SPELL-SWING-308 §4."""
import bpy, bmesh, json, math, os, sys
from mathutils import Vector, Matrix

OUT = sys.argv[sys.argv.index('--') + 1]
os.makedirs(OUT, exist_ok=True)

# ---- 치수 (m). z 는 꼬리 끝에서 잰 값, 마지막에 GripSocket 이 원점이 되게 옮긴다 ----
V2 = '--v2' in sys.argv     # 2차 시안(2026-10-13 사용자 승인): 전체 1.25 m, 자루를 줄이고 붓머리를 키운다 — 붓머리가 실루엣의 주인공
SHAFT = .78 if V2 else 1.10; NECK = 0.07; HEAD = .40 if V2 else .38; TOTAL = SHAFT + NECK + HEAD
R_SHAFT = 0.017; R_ROOT = .058 if V2 else .045
GRIP_FROM_FERRULE = .30 if V2 else .50; HAND_GAP = .34 if V2 else .42
Z_GRIP = SHAFT - GRIP_FROM_FERRULE; Z_GRIP_L = Z_GRIP - HAND_GAP
NODES = [0.045, 0.27, 0.375, 0.60, 0.70] if V2 else [0.055, 0.315, 0.455, 0.735, 0.885, 1.02]   # 대나무 마디(두 손 자리는 피한다)
KN = R_ROOT / .045          # 목(잔)의 벌어짐은 붓털 뿌리 굵기를 따라간다
N_BONES = 8; SEG_SHAFT = 14; SEG_HEAD = 24; RINGS_HEAD = 20
MATS = ['M_GreatBrush_Bamboo', 'M_GreatBrush_Brass', 'M_GreatBrush_Bristle', 'M_GreatBrush_Cord']
ROOT_COL = (.60, .52, .40); MID_COL = (.20, .16, .12); INK_COL = (.012, .012, .014)
COLORS = {'M_GreatBrush_Bamboo': ((.035, .022, .016), .28, 0), 'M_GreatBrush_Brass': ((.62, .43, .16), .32, 1),
          'M_GreatBrush_Bristle': ((.06, .055, .05), .30, 0), 'M_GreatBrush_Cord': ((.33, .05, .04), .8, 0)}


def head_radius(t):
    """붓털 윤곽: 뿌리 지름 9 cm, 배(腹)에서 조금 부풀고 허리 뒤로 끝까지 뾰족하게."""
    return R_ROOT * (1 + .10 * math.sin(math.pi * min(t / .55, 1.0))) * max(0.0, 1 - t ** 2.1) ** .82


bpy.ops.wm.read_factory_settings(use_empty=True)
bm = bmesh.new(); uv = bm.loops.layers.uv.new('UVMap'); col = bm.loops.layers.color.new('Col')
part = bm.verts.layers.int.new('part')   # 0 자루/쇠, 1 붓털
tval = bm.verts.layers.float.new('t')    # 붓털 길이 비율


def lathe(profile, segs, mat, cap0=False, cap1=False, is_head=False, wobble=None, center=(0, 0), axis_z=True):
    """profile: [(z, r, t)] -> 고리들. 반환: 고리 목록."""
    rings = []
    for k, p in enumerate(profile):
        z, r = p[0], p[1]; t = p[2] if len(p) > 2 else 0.0; ring = []
        for i in range(segs):
            a = 2 * math.pi * i / segs; rr = r * (wobble(i, t) if wobble else 1.0)
            v = bm.verts.new((center[0] + math.cos(a) * rr, center[1] + math.sin(a) * rr, z)); v[part] = 1 if is_head else 0; v[tval] = t; ring.append(v)
        rings.append(ring)
    for k in range(len(rings) - 1):
        for i in range(segs):
            j = (i + 1) % segs
            f = bm.faces.new((rings[k][i], rings[k][j], rings[k + 1][j], rings[k + 1][i])); f.material_index = mat(k) if callable(mat) else mat; f.smooth = True
            v0, v1 = (profile[k][2], profile[k + 1][2]) if is_head else (profile[k][0], profile[k + 1][0])
            us = [(i / segs, v0), ((i + 1) / segs, v0), ((i + 1) / segs, v1), (i / segs, v1)]
            for l, u in zip(f.loops, us): l[uv].uv = u
    for cap, ring, flip in ((cap0, rings[0], True), (cap1, rings[-1], False)):
        if cap:
            f = bm.faces.new(ring[::-1] if flip else ring); f.material_index = (mat(0 if flip else len(rings) - 2) if callable(mat) else mat)
            for l in f.loops: l[uv].uv = (.5 + l.vert.co.x * 4, .5 + l.vert.co.y * 4)
    return rings


# 자루: 대나무 마디마다 살짝 불거진 테와 얕은 골
prof = [(0.0, R_SHAFT)]
for n in NODES:
    prof += [(n - .012, R_SHAFT), (n - .004, R_SHAFT * 1.10), (n, R_SHAFT * 1.02), (n + .004, R_SHAFT * 1.10), (n + .012, R_SHAFT)]
prof.append((SHAFT, R_SHAFT))
lathe(prof, SEG_SHAFT, 0)
# 꼬리 놋쇠 마개 + 고리 귀
lathe([(-.016, .0085), (-.012, .0195), (.0, .0205), (.022, .0205), (.026, .0178)], SEG_SHAFT, 1, cap0=True)
# 놋쇠 목(붓대 목): 자루 굵기에서 붓털 뿌리까지 벌어지는 잔 모양 + 테 두 줄
lathe([(SHAFT - .020, .0178), (SHAFT - .016, .0215), (SHAFT - .006, .0215), (SHAFT, .0235), (SHAFT + .006, .0215), (SHAFT + .018, .026 * KN),
       (SHAFT + .040, .0385 * KN), (SHAFT + .056, .0455 * KN), (SHAFT + .062, .0485 * KN), (SHAFT + NECK, .0485 * KN), (SHAFT + NECK, .0445 * KN)], 20, 1)


def tuft(i, t):
    """붓털 가닥 느낌: 세로 골(한 칸 건너 들어감) + 끝으로 갈수록 가라앉는다."""
    return 1 + (.045 if i % 2 else -.035) * (1 - t) * min(1.0, t * 6) + .02 * math.sin(i * 2.4 + t * 9)


head_prof = []
for k in range(RINGS_HEAD + 1):
    t = k / RINGS_HEAD; t = t ** .9
    r = head_radius(t) if k else R_ROOT * .985
    head_prof.append((SHAFT + NECK - .004 + (HEAD + .004) * t, max(r, .0012), t))


rings = lathe(head_prof, SEG_HEAD, 2, is_head=True, wobble=tuft)
tipv = bm.verts.new((0, 0, TOTAL)); tipv[part] = 1; tipv[tval] = 1.0
for i in range(SEG_HEAD):
    f = bm.faces.new((rings[-1][i], rings[-1][(i + 1) % SEG_HEAD], tipv)); f.material_index = 2; f.smooth = True
    for l, u in zip(f.loops, ((i / SEG_HEAD, head_prof[-1][2]), ((i + 1) / SEG_HEAD, head_prof[-1][2]), ((i + .5) / SEG_HEAD, 1.0))): l[uv].uv = u


# 꼬리 고리(놋쇠) + 술(끈 매듭 · 술머리 · 술)
def torus(center, R, r, seg, sides, mat, normal_axis='X'):
    grid = []
    for i in range(seg):
        a = 2 * math.pi * i / seg; row = []
        for j in range(sides):
            b = 2 * math.pi * j / sides; d = R + r * math.cos(b)
            p = Vector((r * math.sin(b), d * math.cos(a), d * math.sin(a))) if normal_axis == 'X' else Vector((d * math.cos(a), r * math.sin(b), d * math.sin(a)))
            row.append(bm.verts.new(Vector(center) + p))
        grid.append(row)
    for i in range(seg):
        for j in range(sides):
            f = bm.faces.new((grid[i][j], grid[(i + 1) % seg][j], grid[(i + 1) % seg][(j + 1) % sides], grid[i][(j + 1) % sides])); f.material_index = mat; f.smooth = True


torus((0, 0, -.034), .020, .0032, 14, 6, 1)
# 술: 고리 아래로 늘어진다(정지 형상 — 게임에서는 흔들림 없이 시작)
zc = -.054
lathe([(zc - .060, .0030), (zc, .0030)], 6, 3)                                                        # 끈
lathe([(zc - .084, .0105), (zc - .078, .0125), (zc - .066, .0115), (zc - .060, .0050)], 8, 3, cap1=True)  # 매듭(술머리)
lathe([(zc - .215, .0175), (zc - .180, .0150), (zc - .120, .0118), (zc - .084, .0102)], 10, 3, cap0=True,
      wobble=lambda i, t: 1 + (.10 if i % 2 else -.10))                                                  # 술
lathe([(zc - .096, .0126), (zc - .090, .0126)], 8, 1)                                                 # 술머리 놋쇠 테

bm.normal_update()
# 원점 = GripSocket
for v in bm.verts: v.co.z -= Z_GRIP
# 붓털 뿌리(밝음) → 끝(먹) 꼭짓점 색
for f in bm.faces:
    for l in f.loops:
        if l.vert[part] == 1:
            t = l.vert[tval]; k = min(1.0, max(0.0, (t - .04) / .22)); k = k * k * (3 - 2 * k)
            a = Vector(ROOT_COL); b = Vector(INK_COL); c = a.lerp(b, k)
            l[col] = (c.x, c.y, c.z, 1)
        else:
            c = COLORS[MATS[f.material_index]][0]; l[col] = (c[0], c[1], c[2], 1)

me = bpy.data.meshes.new('SM_GreatBrush308');
info = [(v[part], v[tval]) for v in bm.verts]
bm.to_mesh(me); bm.free()
# 붓털 결 그림: 뿌리(밝은 털) → 먹 먹은 끝. 세로 줄마다 먹이 오른 높이가 조금씩 다르다(가닥 느낌)
import random
rnd = random.Random(308); TW, TH = 64, 256; px = [0.0] * (TW * TH * 4); rise = [.10 + rnd.random() * .13 for _ in range(TW)]; shade = [.85 + rnd.random() * .3 for _ in range(TW)]
for y in range(TH):
    t = y / (TH - 1)
    for x in range(TW):
        k = min(1.0, max(0.0, (t - rise[x] * .45) / (rise[x] * 1.5 + .05))); k = k * k * (3 - 2 * k)
        c = (Vector(ROOT_COL) * shade[x]).lerp(Vector(MID_COL), min(1, k * 1.6)).lerp(Vector(INK_COL) * (.8 + .4 * (shade[x] - .85) / .3), max(0, (k - .45) / .55))
        i = (y * TW + x) * 4; px[i:i + 4] = (c.x, c.y, c.z, 1)
tex = bpy.data.images.new('T_GreatBrush308_Bristle', TW, TH); tex.pixels = px; tex.filepath_raw = os.path.join(OUT, 'T_GreatBrush308_Bristle.png'); tex.file_format = 'PNG'; tex.save()
for name in MATS:
    m = bpy.data.materials.new(name); m.use_nodes = True
    b = next(n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'); c, rough, metal = COLORS[name]
    b.inputs['Base Color'].default_value = (*c, 1); b.inputs['Roughness'].default_value = rough; b.inputs['Metallic'].default_value = metal
    m.diffuse_color = (*c, 1); m.roughness = rough; m.metallic = metal
    if name == 'M_GreatBrush_Bristle':
        tn = m.node_tree.nodes.new('ShaderNodeTexImage'); tn.image = tex; m.node_tree.links.new(tn.outputs['Color'], b.inputs['Base Color']); m.node_tree.nodes.active = tn
    me.materials.append(m)
ob = bpy.data.objects.new('SM_GreatBrush308', me); bpy.context.scene.collection.objects.link(ob)

# ---- 뼈대 ----
z0 = -Z_GRIP                       # 꼬리 끝
z_head = SHAFT + NECK - Z_GRIP     # 붓털 뿌리
arm = bpy.data.armatures.new('GreatBrush308_Rig'); rig = bpy.data.objects.new('GreatBrush308_Rig', arm); bpy.context.scene.collection.objects.link(rig)
bpy.context.view_layer.objects.active = rig; bpy.ops.object.mode_set(mode='EDIT')
hb = arm.edit_bones.new('BrushHandle'); hb.head = (0, 0, z0); hb.tail = (0, 0, z_head); hb.roll = 0
prev = hb; seg = HEAD / N_BONES
for i in range(N_BONES):
    b = arm.edit_bones.new('Bristle_%02d' % (i + 1)); b.head = (0, 0, z_head + seg * i); b.tail = (0, 0, z_head + seg * (i + 1)); b.roll = 0
    b.parent = prev; b.use_connect = True; prev = b
bpy.ops.object.mode_set(mode='OBJECT')

groups = {n: ob.vertex_groups.new(name=n) for n in ['BrushHandle'] + ['Bristle_%02d' % (i + 1) for i in range(N_BONES)]}
for v, (p, t) in zip(me.vertices, info):
    if p == 0: groups['BrushHandle'].add([v.index], 1.0, 'REPLACE'); continue
    # 붓털: 뼈 가운데를 매듭으로 한 선형 섞기(이웃 두 뼈) — 뿌리 고리는 자루에 붙는다
    x = (v.co.z - z_head) / seg - .5           # 뼈 가운데 기준 좌표
    if x <= 0:
        # 뿌리 반 칸: 자루 ↔ 첫 뼈
        w1 = min(1.0, max(0.0, (v.co.z - z_head) / (seg * .5))) * .5 + .5 if v.co.z > z_head else 0.0
        if v.co.z <= z_head + 1e-5: groups['BrushHandle'].add([v.index], 1.0, 'REPLACE')
        else:
            groups['Bristle_01'].add([v.index], w1, 'REPLACE'); groups['BrushHandle'].add([v.index], 1 - w1, 'REPLACE')
        continue
    i = min(N_BONES - 1, int(math.floor(x))); f = x - i
    if i >= N_BONES - 1: groups['Bristle_%02d' % N_BONES].add([v.index], 1.0, 'REPLACE'); continue
    f = f * f * (3 - 2 * f)
    groups['Bristle_%02d' % (i + 1)].add([v.index], 1 - f, 'REPLACE'); groups['Bristle_%02d' % (i + 2)].add([v.index], f, 'REPLACE')
mod = ob.modifiers.new('Armature', 'ARMATURE'); mod.object = rig; ob.parent = rig

# ---- 블렌드셰이프 ----
ob.shape_key_add(name='Basis')
splay = ob.shape_key_add(name='BristleSplay'); press = ob.shape_key_add(name='BristlePress')
for v, (p, t) in zip(me.vertices, info):
    if p != 1: continue
    co = v.co.copy(); rad = Vector((co.x, co.y, 0)); r = rad.length; d = rad / r if r > 1e-6 else Vector((0, 0, 0))
    # 퍼짐: 뿌리는 그대로, 끝으로 갈수록 부채처럼 벌어지고 끝이 조금 물러난다(끝점은 가운데 남는다 → 갈라진 붓끝)
    k = max(0.0, (t - .12) / .88) ** 1.4
    s = co + d * (.050 * k + r * .35 * k); s.z -= .030 * k * k
    if r < 1e-6: s.z -= .045
    if V2 and r > 1e-6:      # 2차: 가닥이 갈라져 보이게 — 세 줄에 하나는 깊이 물러나고, 한 줄 건너 더 벌어진다(막힌 원통처럼 보이지 않게)
        colm = int(round(math.atan2(co.y, co.x) / (2 * math.pi) * SEG_HEAD)) % SEG_HEAD
        s += d * (.022 * k if colm % 2 else -.014 * k); s.z -= (.085 if colm % 3 == 0 else .0) * k * k
    splay.data[v.index].co = s
    # 눌림: 면에 대고 누른 듯 길이가 줄고(끝 쪽일수록) 허리가 불룩해지며 끝이 뭉툭해진다
    k = t ** 1.3; bulge = math.sin(math.pi * min(1.0, t / .8)) ** 1.2
    q = co + d * (.016 * bulge + .020 * max(0.0, t - .55) / .45); q.z -= HEAD * .24 * k
    press.data[v.index].co = q


# ---- 소켓 ----
def socket(name, world, bone=None):
    e = bpy.data.objects.new(name, None); e.empty_display_type = 'ARROWS'; e.empty_display_size = .06
    bpy.context.scene.collection.objects.link(e); e.parent = rig
    if bone: e.parent_type = 'BONE'; e.parent_bone = bone
    bpy.context.view_layer.update()
    e.matrix_world = Matrix.Translation(world) @ Matrix.Rotation(math.radians(90), 4, 'X')   # 로컬 +Y → 월드 +Z(붓끝)
    return e


socket('GripSocket', Vector((0, 0, 0)))
socket('GripSocketL', Vector((0, 0, Z_GRIP_L - Z_GRIP)))
socket('ShaftEndSocket', Vector((0, 0, z0)))
socket('TipSocket', Vector((0, 0, TOTAL - Z_GRIP)), 'Bristle_%02d' % N_BONES)
bpy.context.view_layer.update()

me.calc_loop_triangles(); tris = len(me.loop_triangles)
if tris > 6000: raise RuntimeError('triangle budget %d' % tris)
zs = [v.co.z for v in me.vertices]
blend = os.path.join(OUT, 'SM_GreatBrush308.blend'); fbx = os.path.join(OUT, 'SM_GreatBrush308.fbx')
bpy.ops.wm.save_as_mainfile(filepath=blend)
for o in bpy.context.scene.objects: o.select_set(True)
bpy.context.view_layer.objects.active = rig
bpy.ops.export_scene.fbx(filepath=fbx, use_selection=True, object_types={'ARMATURE', 'MESH', 'EMPTY'}, add_leaf_bones=False, use_armature_deform_only=False,
                         bake_anim=False, axis_forward='-Z', axis_up='Y', apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', use_mesh_modifiers=False, path_mode='COPY', embed_textures=True)
report = {'triangles': tris, 'vertices': len(me.vertices), 'shaft_to_tip_m': TOTAL, 'mesh_z_min': min(zs), 'mesh_z_max': max(zs),
          'tassel_below_tail_m': -(min(zs) - z0), 'shaft_m': SHAFT, 'neck_m': NECK, 'head_m': HEAD, 'shaft_diameter_m': R_SHAFT * 2, 'bristle_root_diameter_m': R_ROOT * 2,
          'grip_from_ferrule_m': GRIP_FROM_FERRULE, 'hand_gap_m': HAND_GAP, 'bones': [b.name for b in arm.bones], 'sockets': ['GripSocket', 'GripSocketL', 'TipSocket', 'ShaftEndSocket'],
          'shape_keys': [k.name for k in me.shape_keys.key_blocks], 'materials': MATS,
          'axis_contract': 'Brush points Blender +Z / Unity +Y. Origin = GripSocket. Socket local +Y points toward tip.'}
json.dump(report, open(os.path.join(OUT, 'build-report.json'), 'w', encoding='utf-8'), indent=1)
print('BUILD', json.dumps(report)); print('BUILD DONE')
