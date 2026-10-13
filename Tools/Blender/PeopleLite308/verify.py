"""#308 PeopleLite: 내보낸 가벼운 FBX 를 다시 읽어 원본과 견준다(headless) + 보폭 재기 + 대조 시트.

    blender -b --factory-startup -P verify.py -- <peoplelite308.json> <BodyName>

1) 뼈대: 가벼운 몸의 모든 뼈가 원본에 같은 이름 · 같은 부모 · 같은 바인드 행렬로 있는가(= 팩의 .anim 경로가 그대로 물린다).
2) 자세: 팩 클립(서기 · 걷기 · 뛰기)을 원본과 가벼운 몸에 똑같이 얹고, 가벼운 LOD0 의 정점마다 원본 표면의 대응점(서기 0 프레임에서 가장 가까운 점,
   삼각형 무게중심 좌표로 따라감)과의 거리가 자세에 따라 얼마나 벌어지는지 잰다. 이름 붙은 지점(관절 · 머리 끝 · 손끝 · 발끝)은 따로 적는다.
3) 보폭: 원본 뼈대의 발 뼈가 디딘 동안 뒤로 밀리는 속도(m/s) = 제자리 걷기 클립이 뜻하는 걸음 속도. 한 바퀴 거리 = 속도 x 클립 길이.
4) 시트: 색 변형 x (앞 · 옆 · 3/4 · 걷는 자세), 원본 대 LOD0 / 1 / 2, 얼굴 · 손 · 어깨 · 골반 확대."""
import bpy, sys, os, json, math
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
import rig, pv

ARGS = sys.argv[sys.argv.index('--') + 1:]
ROOT = os.path.abspath(os.path.join(HERE, '..', '..', '..'))
CFG = json.load(open(ARGS[0], encoding='utf-8')); BODY = next(b for b in CFG['bodies'] if b['name'] == ARGS[1]); SRC = CFG['sources'][BODY['src']]
SRCDIR = os.path.join(ROOT, CFG['src']); OUT = os.path.join(ROOT, CFG['out']); NAME = BODY['name']; WORK = os.path.join(OUT, '_work', NAME); SHEETS = os.path.join(ROOT, CFG['sheets'])
os.makedirs(SHEETS, exist_ok=True); R = dict(name=NAME)


def log(*a): print('VERIFY', NAME, *a); sys.stdout.flush()


bpy.ops.wm.read_factory_settings(use_empty=True)
armO, meshO, newO = rig.import_fbx(os.path.join(SRCDIR, SRC['fbx']))
for o in newO:
    if o.type == 'EMPTY': bpy.data.objects.remove(o)
if len(meshO) > 1:
    bpy.ops.object.select_all(action='DESELECT')
    for o in meshO: o.select_set(True)
    bpy.context.view_layer.objects.active = meshO[0]; bpy.ops.object.join()
orig = meshO[0]; restO = armO.matrix_world.copy()
armL, meshL, _ = rig.import_fbx(os.path.join(OUT, NAME + '.fbx')); restL = armL.matrix_world.copy()
lods = sorted(meshL, key=lambda o: o.name); lite = lods[0]

# ---------------------------------------------------------------- 1) 뼈대
worst = 0.0; bad = []
for b in armL.data.bones:
    o = armO.data.bones.get(b.name)
    if o is None or (o.parent.name if o.parent else None) != (b.parent.name if b.parent else None): bad.append(b.name); continue
    worst = max(worst, max(abs(x - y) for r1, r2 in zip(b.matrix_local, o.matrix_local) for x, y in zip(r1, r2)))
world = max(abs(x - y) for r1, r2 in zip(restL, restO) for x, y in zip(r1, r2))
clips = {k: rig.Clip(os.path.join(SRCDIR, SRC[k]), SRC['root']) for k in ('idle', 'walk', 'run')}
paths = {rig.bone_path(b, SRC['root']) for b in armL.data.bones} | {SRC['root']}
R['skeleton'] = dict(root_object=armL.name.split('.0')[0], root_expected=SRC['root'], lite_bones=len(armL.data.bones), source_bones=len(armO.data.bones), name_or_parent_mismatch=bad,
                     bind_matrix_max_abs_diff_cm=round(worst, 6), root_world_max_abs_diff=round(world, 8),
                     clip_paths={k: dict(curves=len(c.a['curves']), bound_on_lite=len(paths & set(c.a['curves'])), lite_paths_without_curve=sorted(p for p in paths if p not in c.a['curves'])) for k, c in clips.items()})
log('skeleton', json.dumps(R['skeleton'], ensure_ascii=False)[:900])
ROOT_REF = clips['idle'].local(SRC['root'], 0.0)


def pose_both(clip, t):
    rig.pose(armO, clip, t, restO, ROOT_REF); rig.pose(armL, clip, t, restL, ROOT_REF)


def tris_of(o):
    me = o.data; me.calc_loop_triangles(); t = np.empty(len(me.loop_triangles) * 3, dtype=np.int64); me.loop_triangles.foreach_get('vertices', t); return t.reshape(-1, 3)


# ---------------------------------------------------------------- 2) 자세 편차
rig.reset(armO, restO); rig.reset(armL, restL)      # 대응점은 정지 자세(팔 벌림)에서: 서기 자세는 손이 허벅지에 붙어 층이 헷갈린다
cO = rig.posed_world(orig); cL = rig.posed_world(lite); tO = tris_of(orig)
_mi = np.empty(len(orig.data.loop_triangles), dtype=np.int64); orig.data.loop_triangles.foreach_get('material_index', _mi)
_gone = np.array([s_.material is None or any(d in s_.material.name.lower() for d in BODY['drop']) for s_ in orig.material_slots])
tO = tO[~_gone[_mi]]      # 뺀 재질(겉치마 등)은 대응점 후보에서도 뺀다
bvh = BVHTree.FromPolygons([tuple(c) for c in cO], tO.tolist(), all_triangles=True)
tri = np.zeros(len(cL), dtype=np.int64); bary = np.zeros((len(cL), 3)); d0 = np.zeros(len(cL))
for i, p in enumerate(cL):
    loc, nrm, idx, dist = bvh.find_nearest(Vector(p)); tri[i] = idx; d0[i] = dist
    a, b, c = cO[tO[idx]]; v0, v1, v2 = b - a, c - a, np.array(loc) - a
    d00, d01, d11, d20, d21 = v0 @ v0, v0 @ v1, v1 @ v1, v2 @ v0, v2 @ v1; den = d00 * d11 - d01 * d01
    v = (d11 * d20 - d01 * d21) / den if abs(den) > 1e-18 else 0.0; w = (d00 * d21 - d01 * d20) / den if abs(den) > 1e-18 else 0.0
    bary[i] = (1 - v - w, v, w)
FIT = d0 < .003      # 구운 손가락 · 줄이며 떨어진 정점은 대응점이 믿을 수 없어 뺀다(수를 적는다)
# 이름 붙은 지점: 가벼운 몸에서 고른다(대응점이 믿을 만한 정점 가운데)
Wn = [g.name for g in lite.vertex_groups]; dom = np.full(len(cL), -1)
for v in lite.data.vertices:
    if v.groups: dom[v.index] = max(v.groups, key=lambda g: g.weight).group


def near_bone(bone):
    p = np.array(armL.matrix_world @ armL.pose.bones[bone].head); return int(np.argmin(np.linalg.norm(cL - p, axis=1) + (~FIT) * 9.0))


def extreme(bone_names, key):
    idx = np.nonzero(np.isin(dom, [Wn.index(n) for n in bone_names if n in Wn]) & FIT)[0]
    return int(idx[np.argmax(key(cL[idx]))]) if len(idx) else 0


marks = {'head_top': extreme(['b_Head'], lambda c: c[:, 2]), 'nose': extreme(['b_Head'], lambda c: -c[:, 1] - 3 * np.abs(c[:, 0])),
         'L_hand_tip': extreme(['b_L_Hand'], lambda c: -c[:, 2]), 'R_hand_tip': extreme(['b_R_Hand'], lambda c: -c[:, 2]),
         'L_toe': extreme(['b_L_Toe0', 'b_L_Foot'], lambda c: -c[:, 1]), 'R_toe': extreme(['b_R_Toe0', 'b_R_Foot'], lambda c: -c[:, 1])}
for j in ('UpperArm', 'Forearm', 'Hand', 'Thigh', 'Calf', 'Foot'):
    for s in 'LR': marks['%s_%s' % (s, j)] = near_bone('b_%s_%s' % (s, j))
R['fit_pose'] = 'rest'; R['fit_excluded_vertices'] = int((~FIT).sum())
R['rest_fit_mm'] = dict(mean=round(float(d0.mean()) * 1000, 2), p95=round(float(np.percentile(d0, 95)) * 1000, 2), max=round(float(d0.max()) * 1000, 2))
R['pose'] = {}
for key, n in (('idle', 3), ('walk', 8), ('run', 6)):
    clip = clips[key]; dev_all = []; mk = {m: 0.0 for m in marks}
    for f in range(n):
        pose_both(clip, clip.length * (f + .5) / n)
        pO = rig.posed_world(orig); pL = rig.posed_world(lite)
        ref = (pO[tO[tri]] * bary[:, :, None]).sum(1); dev = np.abs(np.linalg.norm(pL - ref, axis=1) - d0); dev_all.append(dev[FIT])
        for m, i in marks.items(): mk[m] = max(mk[m], float(dev[i]))
    dev = np.concatenate(dev_all)
    R['pose'][key] = dict(clip=clip.name, frames=n, vertices=int(FIT.sum()), mean_mm=round(float(dev.mean()) * 1000, 2), p95_mm=round(float(np.percentile(dev, 95)) * 1000, 2),
                          p99_mm=round(float(np.percentile(dev, 99)) * 1000, 2), max_mm=round(float(dev.max()) * 1000, 2), landmarks_max_mm={m: round(v * 1000, 2) for m, v in mk.items()},
                          landmarks_worst_mm=round(max(mk.values()) * 1000, 2))
    log('pose', key, {k: v for k, v in R['pose'][key].items() if k != 'landmarks_max_mm'}, 'hand tips', R['pose'][key]['landmarks_max_mm']['L_hand_tip'])

# ---------------------------------------------------------------- 3) 보폭(원본 뼈대)
walk = clips['walk']; N = 96; feet = {s: [] for s in 'LR'}; pelvis = []
for f in range(N):
    rig.pose(armO, walk, walk.length * f / N, restO, ROOT_REF); bpy.context.view_layer.update()
    for s in 'LR': feet[s].append(np.array(armO.matrix_world @ armO.pose.bones['b_%s_Foot' % s].head))
    pelvis.append(np.array(armO.matrix_world @ armO.pose.bones['b_Pelvis'].head))
pelvis = np.array(pelvis); dt = walk.length / N; speeds = []; ranges = []
for s in 'LR':
    p = np.array(feet[s]); ax = int(np.argmax(p[:, :2].max(0) - p[:, :2].min(0))); z = p[:, 2]; v = (np.roll(p[:, ax], -1) - np.roll(p[:, ax], 1)) / (2 * dt)
    stance = z < z.min() + .15 * (z.max() - z.min()); sign = np.sign(np.median(v[stance])); speeds.append(float(np.median(np.abs(v[stance & (np.sign(v) == sign)])))); ranges.append(float(p[:, ax].max() - p[:, ax].min()))
speed = float(np.mean(speeds))
R['walk'] = dict(clip=walk.name, length_s=round(walk.length, 4), in_place=bool((pelvis.max(0) - pelvis.min(0))[:2].max() < .12), pelvis_range_m=[round(float(x), 3) for x in pelvis.max(0) - pelvis.min(0)],
                 stance_foot_speed_mps=round(speed, 3), per_foot_mps=[round(x, 3) for x in speeds], stride_metres_per_cycle=round(speed * walk.length, 3),
                 foot_range_x2_m=round(float(np.mean(ranges)) * 2, 3), note='stride_metres_per_cycle = TownWalkerBody308.StrideMetres (걷기 클립 한 바퀴에 나아가는 거리)')
R['clips'] = {k: dict(name=c.name, file=SRC[k], length_s=round(c.length, 4), curves=len(c.a['curves'])) for k, c in clips.items()}
log('walk', R['walk'])

# ---------------------------------------------------------------- 4) 시트
tex = json.load(open(os.path.join(WORK, 'textures.json'), encoding='utf-8')); layout = json.load(open(os.path.join(WORK, 'layout.json'), encoding='utf-8'))
atl = {k: pv.image_array(bpy.data.images.load(os.path.join(ROOT, p)), 1024) for k, p in tex.items()}
matsO = []
for s in orig.material_slots:
    m = s.material; t = None; alpha = False
    if m and m.node_tree:
        bsdf = next((n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None)
        if bsdf and bsdf.inputs['Base Color'].links:
            n = bsdf.inputs['Base Color'].links[0].from_node
            if n.type == 'TEX_IMAGE' and n.image and n.image.size[0] > 0: t = pv.image_array(n.image, 1024); alpha = bool((t[..., 3] < .5).mean() > .005)
    if t is None or (m and any(d in m.name.lower() for d in BODY['drop'])): t = np.zeros((1, 1, 4), dtype=np.float32); alpha = True      # 뺀 재질은 원본에서도 숨긴다(같은 것끼리 견준다)
    matsO.append(dict(tex=t, alpha=alpha))


def shot(o, mats, view, size, lo, hi):
    co = rig.posed_world(o); A = pv.mesh_arrays(o.data, co); c = co[A['tri']].mean(1)
    lo = np.asarray(lo, dtype=np.float64); hi = np.asarray(hi, dtype=np.float64)
    inside = ((c > lo - .05) & (c < hi + .05)).all(1)
    for k in ('tri', 'tl', 'mat', 'normal', 'area'): A[k] = A[k][inside]
    if not len(A['tri']): return np.full((size[1], size[0], 3), .86, dtype=np.float32)
    px = max(hi[2] - lo[2], 1e-3) / size[1]
    P, C, Nn = pv.sample(A, px, mats, density=7.0, cap=1_600_000)
    return pv.render(P, C, Nn, view, size, lo, hi)


def one(key): return [dict(tex=atl[key], alpha=layout['alpha'])]


def box(c=None):
    c = rig.posed_world(orig) if c is None else c; return c.min(0) - .03, c.max(0) + .03


CELL = (300, 540); variants = [v['key'] for v in CFG['variants']]; tw = walk.length * .30
pose_both(clips['idle'], 0.0); lo, hi = box(np.concatenate([rig.posed_world(orig), rig.posed_world(lite)]))
rows = [[shot(lite, one(k), v, CELL, lo, hi) for v in ('front', 'side', 'q34', 'back')] for k in variants]
pose_both(walk, tw); lo2, hi2 = box(np.concatenate([rig.posed_world(orig), rig.posed_world(lite)]))
for r, k in zip(rows, variants): r += [shot(lite, one(k), v, CELL, lo2, hi2) for v in ('q34', 'side')]
pv.save_png(os.path.join(SHEETS, NAME + '_variants.png'), pv.grid(rows))

rowA = []      # 원본 / LOD0(원래 색) / LOD0 / LOD1 / LOD2 : 서기 앞 + 걷기 3/4 + 걷기 뒤 3/4
for o, mats in [(orig, matsO), (lite, one('orig')), (lite, one('white')), (lods[1], one('white')), (lods[2], one('white'))]:
    pose_both(clips['idle'], 0.0); a = shot(o, mats, 'front', CELL, lo, hi)
    pose_both(walk, tw); rowA.append([a, shot(o, mats, 'q34', CELL, lo2, hi2), shot(o, mats, 'q34b', CELL, lo2, hi2)])
pv.save_png(os.path.join(SHEETS, NAME + '_compare.png'), pv.grid([list(c) for c in zip(*rowA)]))

BIG = (420, 420); pose_both(walk, tw); cO2 = rig.posed_world(orig)
head = np.array(armO.matrix_world @ armO.pose.bones['b_Head'].head); hand = np.array(armO.matrix_world @ armO.pose.bones['b_L_Hand'].head)
sh = np.array(armO.matrix_world @ armO.pose.bones['b_Spine3'].head); hip = np.array(armO.matrix_world @ armO.pose.bones['b_Pelvis'].head)
zones = [('front', head + (0, 0, .12), .23), ('q34', head + (0, 0, .12), .23), ('q34', hand + (0, 0, -.06), .16), ('q34b', sh + (0, 0, .02), .34), ('q34', hip + (0, 0, -.12), .38)]
close = []
for o, mats in [(orig, matsO), (lite, one('orig')), (lite, one('white')), (lods[1], one('white'))]:
    close.append([shot(o, mats, v, BIG, c - r, c + r) for v, c, r in zones])
pv.save_png(os.path.join(SHEETS, NAME + '_closeup.png'), pv.grid(close))
R['sheets'] = [os.path.relpath(os.path.join(SHEETS, NAME + s), ROOT).replace(os.sep, '/') for s in ('_variants.png', '_compare.png', '_closeup.png')]
json.dump(R, open(os.path.join(WORK, 'verify.json'), 'w', encoding='utf-8'), indent=1, ensure_ascii=False)
log('DONE sheets', R['sheets'])
