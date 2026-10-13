"""#308 PeopleLite: KCISA 보부상 팩의 몸 하나를 군중용 가벼운 스킨 메시로 줄인다(headless).

    blender -b --factory-startup -P lite.py -- <peoplelite308.json> <BodyName>

하는 일(순서): FBX 읽기 -> 메시 합치기 -> 뺄 재질 지우기 -> 가려진 면 지우기(정지 + 걷기 2 + 서기 자세에서 3 m 안에 막히지 않는 방향이
하나도 없는 면) -> 안 움직이는 뼈(손가락 등)를 부모로 합치기 · 지우기 -> 재질별로 따로 줄이기(얼굴 · 손 · 모자는 몫을 크게) ->
아틀라스 한 장으로 UV 다시 놓기 -> LOD1 · LOD2 -> 재질 하나 -> FBX(같은 뼈 이름 · 같은 바인드 자세 · 같은 축/단위).
뼈대는 건드리지 않는다(지우는 뼈만 지운다): 팩의 Generic .anim 이 경로로 그대로 물린다.
그림은 여기서 만들지 않는다 - 원본 그림을 _work/tex 에 꺼내고 배치(layout.json)만 쓴다. atlas.py 가 굽는다."""
import bpy, bmesh, sys, os, json, re
import numpy as np
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
import rig, pv

ARGS = sys.argv[sys.argv.index('--') + 1:]
ROOT = os.path.abspath(os.path.join(HERE, '..', '..', '..'))
CFG = json.load(open(ARGS[0], encoding='utf-8')); BODY = next(b for b in CFG['bodies'] if b['name'] == ARGS[1]); SRC = CFG['sources'][BODY['src']]
SRCDIR = os.path.join(ROOT, CFG['src']); OUT = os.path.join(ROOT, CFG['out']); WORK = os.path.join(OUT, '_work', BODY['name']); TEX = os.path.join(OUT, '_work', 'tex', BODY['src'])
os.makedirs(WORK, exist_ok=True); os.makedirs(TEX, exist_ok=True)
NAME = BODY['name']; STATS = dict(name=NAME, label=BODY['label'], src=BODY['src'])


def log(*a): print('LITE', NAME, *a); sys.stdout.flush()


def activate(o, others=()):
    bpy.ops.object.select_all(action='DESELECT')
    for x in (o, *others): x.select_set(True)
    bpy.context.view_layer.objects.active = o


def rule_of(mat_name):
    n = mat_name.lower()
    return next((r for r in CFG['parts'] if r['match'] in n), None)


def tri_count(me): return sum(len(p.vertices) - 2 for p in me.polygons)


# ---------------------------------------------------------------- 읽기 · 합치기
bpy.ops.wm.read_factory_settings(use_empty=True)
arm, meshes, new = rig.import_fbx(os.path.join(SRCDIR, SRC['fbx']))
for o in new:
    if o.type == 'EMPTY': bpy.data.objects.remove(o)
REST = arm.matrix_world.copy()
meshes.sort(key=lambda o: -len(o.data.polygons)); obj = meshes[0]
STATS['source'] = dict(triangles=sum(tri_count(o.data) for o in meshes), bones=len(arm.data.bones), meshes=len(meshes), slots=sum(len(o.material_slots) for o in meshes))
if len(meshes) > 1: activate(obj, meshes[1:]); bpy.ops.object.join()
for m in list(obj.modifiers): obj.modifiers.remove(m)
me = obj.data
while len(me.uv_layers) > 1: me.uv_layers.remove(me.uv_layers[len(me.uv_layers) - 1])
bm = bmesh.new(); bm.from_mesh(me); bmesh.ops.triangulate(bm, faces=bm.faces[:])

# 재질 칸 -> 규칙 · 그림 · 알파
SLOTS = []
for i, s in enumerate(obj.material_slots):
    m = s.material; img = None
    if m and m.node_tree:
        bsdf = next((n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None)
        if bsdf and bsdf.inputs['Base Color'].links:
            n = bsdf.inputs['Base Color'].links[0].from_node
            if n.type == 'TEX_IMAGE' and n.image and n.image.size[0] > 0: img = n.image
    r = rule_of(m.name) if m else None
    dropped = (m is None or img is None or r is None or any(d in m.name.lower() for d in BODY['drop']))
    SLOTS.append(dict(index=i, name=m.name if m else '', image=img, rule=r, dropped=dropped, alpha=False))
gone = [f for f in bm.faces if SLOTS[f.material_index]['dropped']]
log('faces dropped by material', len(gone), sorted({SLOTS[f.material_index]['name'] for f in gone}))
bmesh.ops.delete(bm, geom=gone, context='FACES'); bm.to_mesh(me); bm.free()
for s in SLOTS:
    if s['dropped'] or s['image'] is None: continue
    a = pv.image_array(s['image'], 512); s['alpha'] = bool((a[..., 3] < .5).mean() > .005)
    key = re.sub(r'[^A-Za-z0-9_]+', '_', s['name']).strip('_'); s['png'] = os.path.join(TEX, key + '.png')
    if not os.path.exists(s['png']):
        s['image'].file_format = 'PNG'; s['image'].save(filepath=s['png'])
log('materials kept', [(s['name'], s['rule']['part'], 'alpha' if s['alpha'] else 'opaque') for s in SLOTS if not s['dropped']])


def mesh_np(me):
    nv = len(me.vertices); co = np.empty(nv * 3); me.vertices.foreach_get('co', co)
    tris = np.empty(len(me.polygons) * 3, dtype=np.int64); me.polygons.foreach_get('vertices', tris)
    mat = np.empty(len(me.polygons), dtype=np.int64); me.polygons.foreach_get('material_index', mat)
    return co.reshape(-1, 3), tris.reshape(-1, 3), mat


# ---------------------------------------------------------------- 가려진 면
def fib(n):
    i = np.arange(n) + .5; z = 1 - 2 * i / n; r = np.sqrt(1 - z * z); t = i * np.pi * (3 - 5 ** .5)
    return np.stack([r * np.cos(t), r * np.sin(t), z], 1)


DIRS = fib(42); EPS = .0015; REACH = 3.0
idle = rig.Clip(os.path.join(SRCDIR, SRC['idle']), SRC['root']); walk = rig.Clip(os.path.join(SRCDIR, SRC['walk']), SRC['root'])
ROOT_REF = idle.local(SRC['root'], 0.0)
mod = obj.modifiers.new('Armature', 'ARMATURE'); mod.object = arm
_, tris, mat = mesh_np(me)
opaque = np.array([not SLOTS[m]['alpha'] for m in mat]); visible = np.zeros(len(tris), dtype=bool)
for label, clip, t in (('rest', None, 0), ('walk.0', walk, 0), ('walk.25', walk, .25), ('walk.5', walk, .5), ('walk.75', walk, .75), ('idle', idle, 0)):
    if clip is None: rig.reset(arm, REST)
    else: rig.pose(arm, clip, clip.length * t, REST, ROOT_REF)
    co = rig.posed_world(obj); zmin = co[:, 2].min()
    g = len(co); verts = [tuple(c) for c in co] + [(-9, -9, zmin - .02), (9, -9, zmin - .02), (9, 9, zmin - .02), (-9, 9, zmin - .02)]
    bvh = BVHTree.FromPolygons(verts, tris[opaque].tolist() + [[g, g + 1, g + 2], [g, g + 2, g + 3]], all_triangles=True)
    p = co[tris]; c = p.mean(1); n = np.cross(p[:, 1] - p[:, 0], p[:, 2] - p[:, 0]); ln = np.linalg.norm(n, axis=1); n = n / np.maximum(ln, 1e-12)[:, None]
    dots = n @ DIRS.T; found = 0
    for i in np.nonzero(~visible)[0]:
        if ln[i] < 1e-12: visible[i] = True; continue
        o = Vector(c[i] + n[i] * EPS)
        for k in np.argsort(-dots[i]):
            if dots[i, k] < .12: break
            if bvh.ray_cast(o, Vector(DIRS[k]), REACH)[0] is None: visible[i] = True; found += 1; break
    log('visibility', label, 'newly visible', found, 'visible total', int(visible.sum()), '/', len(tris))
rig.reset(arm, REST); obj.modifiers.remove(mod)
vis_v = np.zeros(len(me.vertices), dtype=bool); vis_v[tris[visible].ravel()] = True
keep = vis_v[tris].any(1); vis_v[tris[keep].ravel()] = True; keep = vis_v[tris].any(1)      # 보이는 면에서 두 고리까지 남긴다(걷는 자세에서 구멍 방지)
before = {}; after = {}
for m, k in zip(mat, keep):
    part = SLOTS[m]['rule']['part']; before[part] = before.get(part, 0) + 1; after[part] = after.get(part, 0) + int(k)
STATS['hidden_removed'] = {p: [before[p], after[p]] for p in before}
log('hidden faces removed', int((~keep).sum()), 'per part [before, kept]', STATS['hidden_removed'])
bm = bmesh.new(); bm.from_mesh(me); bm.faces.ensure_lookup_table()
bmesh.ops.delete(bm, geom=[bm.faces[i] for i in np.nonzero(~keep)[0]], context='FACES')
bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context='VERTS'); bm.to_mesh(me); bm.free()

# ---------------------------------------------------------------- 뼈 합치기(안 움직이는 뼈는 서기 자세를 구워 부모로)
bones = arm.data.bones
drop = {b.name for b in bones if SRC['dropBones'] and re.search(SRC['dropBones'], b.name)}
unskin = {b.name for b in bones if SRC['unskinBones'] and re.search(SRC['unskinBones'], b.name)} - drop
for b in bones:
    if b.name in drop and any(c.name not in drop for c in b.children_recursive): raise SystemExit('REFUSE: dropped bone %s has a kept descendant' % b.name)
merged = drop | unskin
gname = [g.name for g in obj.vertex_groups]; gi = {n: i for i, n in enumerate(gname)}
W = np.zeros((len(me.vertices), len(gname)))
for v in me.vertices:
    for g in v.groups: W[v.index, g.group] = g.weight
W /= np.maximum(W.sum(1), 1e-9)[:, None]
Mrel = np.array(arm.matrix_world.inverted() @ obj.matrix_world); Minv = np.linalg.inv(Mrel)
co = np.empty(len(me.vertices) * 3); me.vertices.foreach_get('co', co); co = co.reshape(-1, 3)
ca = co @ Mrel[:3, :3].T + Mrel[:3, 3]; acc = np.zeros_like(ca); wk = np.ones(len(ca))
target = {}
for name in merged:
    b = bones[name]; chain = []; a = b
    while a is not None and a.name in merged: chain.append(a); a = a.parent
    if a is None: raise SystemExit('REFUSE: no kept ancestor for ' + name)
    target[name] = a.name
    if name not in gi: continue
    C = Matrix.Identity(4)
    for x in reversed(chain):
        src_clip = walk if SRC.get('bakeFromWalk') and re.search(SRC['bakeFromWalk'], x.name) else idle      # 손가락은 클립마다 고정된 다른 모양: 걷기의 느슨한 손을 굽는다
        L = src_clip.local(rig.bone_path(x, SRC['root']), 0.0); C = C @ (L if L is not None else rig.rest_local(x))
    K = np.array(a.matrix_local @ C @ b.matrix_local.inverted()); w = W[:, gi[name]]
    acc += w[:, None] * (ca @ K[:3, :3].T + K[:3, 3]); wk -= w
baked = acc + np.maximum(wk, 0)[:, None] * ca
STATS['static_bake_max_mm'] = round(float(np.linalg.norm(baked - ca, axis=1).max()) * 10, 3)   # 뼈대 공간 단위 = cm
log('static bones merged', len(merged), '(deleted', len(drop), ', kept as plain transforms', len(unskin), ') bake moved verts by at most', STATS['static_bake_max_mm'], 'mm')
me.vertices.foreach_set('co', (baked @ Minv[:3, :3].T + Minv[:3, 3]).ravel()); me.update()
for name, t in target.items():
    if name in gi:
        if t not in gi: gi[t] = W.shape[1]; gname.append(t); W = np.concatenate([W, np.zeros((len(W), 1))], 1)
        W[:, gi[t]] += W[:, gi[name]]; W[:, gi[name]] = 0


def write_weights(o, W, names, limit):
    """정점마다 큰 것 limit 개만 남기고 정규화해서 다시 쓴다."""
    o.vertex_groups.clear(); order = [n for n in [b.name for b in bones] if n in names]
    vgs = {n: o.vertex_groups.new(name=n) for n in order if W[:, names.index(n)].max() > 0}
    top = np.argsort(-W, axis=1)[:, :limit]
    for vi in range(len(W)):
        ws = W[vi, top[vi]]; s = ws.sum()
        if s <= 0: continue
        for g, w in zip(top[vi], ws):
            if w > 1e-4: vgs[names[g]].add([vi], float(w / s), 'REPLACE')
    return len(vgs)


def read_weights(o):
    names = [g.name for g in o.vertex_groups]; W = np.zeros((len(o.data.vertices), len(names)))
    for v in o.data.vertices:
        for g in v.groups: W[v.index, g.group] = g.weight
    return W, names


write_weights(obj, W, gname, 8)
activate(arm); bpy.ops.object.mode_set(mode='EDIT')
for n in drop: arm.data.edit_bones.remove(arm.data.edit_bones[n])
bpy.ops.object.mode_set(mode='OBJECT')
bones = arm.data.bones

# ---------------------------------------------------------------- 재질별로 줄이기
activate(obj); bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT'); bpy.ops.mesh.separate(type='MATERIAL'); bpy.ops.object.mode_set(mode='OBJECT')
pieces = [o for o in bpy.context.selected_objects if o.type == 'MESH']
if obj not in pieces: pieces.append(obj)
info = []
for p in pieces:
    if not len(p.data.polygons): continue
    mname = p.material_slots[p.data.polygons[0].material_index].material.name
    info.append(dict(obj=p, mat=mname, rule=rule_of(mname), tris=len(p.data.polygons)))
part_tris = {}
for x in info: part_tris[x['rule']['part']] = part_tris.get(x['rule']['part'], 0) + x['tris']
T0 = CFG['lods'][0]; s = 1.0
for _ in range(40):
    tot = sum(min(x['tris'], x['rule']['budget'] * s * x['tris'] / part_tris[x['rule']['part']]) for x in info)
    if abs(tot - T0) < 5 or (tot < T0 and all(x['tris'] <= x['rule']['budget'] * s * x['tris'] / part_tris[x['rule']['part']] for x in info)): break
    s *= T0 / max(tot, 1)
STATS['parts'] = {}
for x in info:
    want = min(x['tris'], x['rule']['budget'] * s * x['tris'] / part_tris[x['rule']['part']]); p = x['obj']
    if want < x['tris'] - 1:
        d = p.modifiers.new('d', 'DECIMATE'); d.decimate_type = 'COLLAPSE'; d.ratio = max(want / x['tris'], .002); d.use_collapse_triangulate = True
        with bpy.context.temp_override(object=p, active_object=p, selected_objects=[p]): bpy.ops.object.modifier_apply(modifier=d.name)
    e = STATS['parts'].setdefault(x['rule']['part'], [0, 0]); e[0] += x['tris']; e[1] += len(p.data.polygons)
log('parts [visible tris, LOD0 tris]', STATS['parts'])
alive = [x['obj'] for x in info]
for p in pieces:
    if p not in alive: bpy.data.objects.remove(p)
obj = alive[0]
if len(alive) > 1: activate(obj, alive[1:]); bpy.ops.object.join()
me = obj.data; obj.name = NAME + '_LOD0'; me.name = obj.name
bm = bmesh.new(); bm.from_mesh(me); bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 3])
bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.calc_area() < 1e-9], context='FACES')
bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context='VERTS'); bm.to_mesh(me); bm.free()


def smooth(o):
    m = o.data
    try:
        with bpy.context.temp_override(object=o, active_object=o, selected_objects=[o]): bpy.ops.mesh.customdata_custom_splitnormals_clear()
    except Exception as e: log('custom normals kept:', str(e)[:80])
    m.polygons.foreach_set('use_smooth', [True] * len(m.polygons)); m.update()


# ---------------------------------------------------------------- 아틀라스 배치 + UV 다시 놓기
S = CFG['atlas']; PAD = 3
uvl = me.uv_layers[0]; nl = len(me.loops); uv = np.empty(nl * 2); uvl.data.foreach_get('uv', uv); uv = uv.reshape(-1, 2)
co, tris, mat = mesh_np(me); loops = np.arange(nl).reshape(-1, 3)       # 전부 삼각형: 면 i 의 루프 = 3i..3i+2
ls = np.empty(len(me.polygons), dtype=np.int64); me.polygons.foreach_get('loop_start', ls); loops = ls[:, None] + np.arange(3)[None, :]
p = co[tris]; area = np.linalg.norm(np.cross(p[:, 1] - p[:, 0], p[:, 2] - p[:, 0]), axis=1) * .5
slot_names = [s.material.name for s in obj.material_slots]; rects = []
for m in sorted(set(mat.tolist())):
    slot = next(s for s in SLOTS if s['name'] == slot_names[m]); l = loops[mat == m].ravel(); u = uv[l]; wrapped = False
    wrapped = bool(u.min() < -.002 or u.max() > 1.002)      # 타일을 넘는 UV(짚 무늬 등): 상자를 그대로 두고 atlas.py 가 원본을 되풀이해 채운다
    lo = u.min(0) - .004; hi = u.max(0) + .004
    if not wrapped: lo = np.clip(lo, 0, 1); hi = np.clip(hi, 0, 1)
    rects.append(dict(m=m, material=slot['name'], part=slot['rule']['part'], role=slot['rule'].get('role'), alpha=slot['alpha'], src=os.path.relpath(slot['png'], ROOT).replace('\\', '/'),
                      uv=[float(lo[0]), float(lo[1]), float(hi[0]), float(hi[1])], weight=float(area[mat == m].sum()) * slot['rule']['importance'], wrapped=wrapped,
                      aspect=float(max(hi[0] - lo[0], 1e-4) / max(hi[1] - lo[1], 1e-4)), tris=int((mat == m).sum())))
wsum = sum(r['weight'] for r in rects); k = .92
while True:
    for r in rects:
        a = max(r['weight'] / wsum, .0015) * S * S * k; r['w'] = max(20, int(round((a * r['aspect']) ** .5))); r['h'] = max(20, int(round((a / r['aspect']) ** .5)))
        r['w'] = min(r['w'], S - 2 * PAD); r['h'] = min(r['h'], S - 2 * PAD)
    x = y = shelf = 0; ok = True
    for r in sorted(rects, key=lambda r: -r['h']):
        W_, H_ = r['w'] + 2 * PAD, r['h'] + 2 * PAD
        if x + W_ > S: x = 0; y += shelf; shelf = 0
        if y + H_ > S: ok = False; break
        r['x'] = x + PAD; r['y'] = y + PAD; x += W_; shelf = max(shelf, H_)
    if ok: break
    k *= .96
for r in rects:
    l = loops[mat == r['m']].ravel(); u0, v0, u1, v1 = r['uv']
    uv[l, 0] = (r['x'] + (uv[l, 0] - u0) / (u1 - u0) * r['w']) / S; uv[l, 1] = (r['y'] + (uv[l, 1] - v0) / (v1 - v0) * r['h']) / S
uvl.data.foreach_set('uv', uv.ravel()); uvl.name = 'UV0'; me.update()
log('atlas', S, 'fill %.2f' % (sum(r['w'] * r['h'] for r in rects) / S / S), 'rects', [(r['part'], r['w'], r['h']) for r in rects], 'wrapped', [r['material'] for r in rects if r['wrapped']])
json.dump(dict(name=NAME, src=BODY['src'], size=S, pad=PAD, alpha=any(r['alpha'] for r in rects), rects=[{k: v for k, v in r.items() if k != 'm'} for r in rects]),
          open(os.path.join(WORK, 'layout.json'), 'w', encoding='utf-8'), indent=1)

# ---------------------------------------------------------------- LOD
W0, n0 = read_weights(obj); skinned = write_weights(obj, W0, n0, CFG['lodInfluences'][0]); smooth(obj)
lods = [obj]; part_of_slot = [rule_of(n) for n in slot_names]
for level in (1, 2):
    o = obj.copy(); o.data = obj.data.copy(); bpy.context.collection.objects.link(o); o.name = '%s_LOD%d' % (NAME, level); o.data.name = o.name
    bm = bmesh.new(); bm.from_mesh(o.data)
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if part_of_slot[f.material_index].get('maxLod', 9) < level], context='FACES')
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context='VERTS'); bm.to_mesh(o.data); bm.free()
    cur = len(o.data.polygons); want = CFG['lods'][level]
    if cur > want:
        d = o.modifiers.new('d', 'DECIMATE'); d.decimate_type = 'COLLAPSE'; d.ratio = want / cur; d.use_collapse_triangulate = True
        with bpy.context.temp_override(object=o, active_object=o, selected_objects=[o]): bpy.ops.object.modifier_apply(modifier=d.name)
    Wl, nl_ = read_weights(o); write_weights(o, Wl, nl_, CFG['lodInfluences'][level]); smooth(o); lods.append(o)

single = bpy.data.materials.new('M_' + NAME)
for o in lods:
    o.data.materials.clear(); o.data.materials.append(single); o.data.polygons.foreach_set('material_index', [0] * len(o.data.polygons)); o.data.update()
    mod = o.modifiers.new('Armature', 'ARMATURE'); mod.object = arm
    if o.parent != arm: raise SystemExit('REFUSE: mesh lost its armature parent')
co = rig.posed_world(obj)
STATS.update(lod_triangles=[len(o.data.polygons) for o in lods], lod_vertices=[len(o.data.vertices) for o in lods], skinned_bones=[len(o.vertex_groups) for o in lods],
             transforms=len(arm.data.bones) + 1, bones_deleted=sorted(drop), bones_unskinned=sorted(unskin), height_m=round(float(co[:, 2].max() - co[:, 2].min()), 3),
             head_bone_z=round(float((arm.matrix_world @ arm.data.bones['b_Head'].head_local).z), 3), materials=1, root=SRC['root'])
log('LOD tris', STATS['lod_triangles'], 'verts', STATS['lod_vertices'], 'skinned bones', STATS['skinned_bones'], 'transforms', STATS['transforms'], 'height', STATS['height_m'])

# ---------------------------------------------------------------- 내보내기(원본과 같은 축 · 단위)
axes = dict(axis_up='Y', axis_forward='-Z'); gs_src = None
try:
    from io_scene_fbx import parse_fbx, fbx_utils

    def settings(path):
        root, _ = parse_fbx.parse(path); gs = next(e for e in root.elems if e.id == b'GlobalSettings'); p70 = next(e for e in gs.elems if e.id == b'Properties70')
        return {e.props[0].decode(): e.props[4] for e in p70.elems if e.props[0] in (b'UpAxis', b'UpAxisSign', b'FrontAxis', b'FrontAxisSign', b'CoordAxis', b'CoordAxisSign', b'UnitScaleFactor', b'OriginalUnitScaleFactor')}
    gs_src = settings(os.path.join(SRCDIR, SRC['fbx']))
    want = ((gs_src['UpAxis'], gs_src['UpAxisSign']), (gs_src['FrontAxis'], gs_src['FrontAxisSign']), (gs_src['CoordAxis'], gs_src['CoordAxisSign']))
    hit = [k for k, v in fbx_utils.RIGHT_HAND_AXES.items() if tuple(tuple(x) for x in v) == want]
    if hit: axes = dict(axis_up=hit[0][0], axis_forward=hit[0][1])
    log('source FBX settings', gs_src, '-> export axes', axes, '' if hit else '(NO MATCH: default axes)')
except Exception as e:
    log('FBX settings not read:', repr(e)[:200])


def export(path, objs):
    activate(arm, objs)
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={'ARMATURE', 'MESH'}, add_leaf_bones=False, bake_anim=False, use_armature_deform_only=False,
                             apply_scale_options='FBX_SCALE_NONE', apply_unit_scale=True, global_scale=1.0, bake_space_transform=False, mesh_smooth_type='OFF', use_tspace=False,
                             primary_bone_axis='Y', secondary_bone_axis='X', use_custom_props=False, path_mode='STRIP', embed_textures=False, **axes)


main = os.path.join(OUT, NAME + '.fbx'); export(main, lods)
os.makedirs(os.path.join(OUT, '_lod_single'), exist_ok=True)
for i, o in enumerate(lods): export(os.path.join(OUT, '_lod_single', '%s_LOD%d.fbx' % (NAME, i)), [o])
if gs_src is not None:
    gs_out = settings(main); STATS['fbx_settings'] = dict(source=gs_src, lite=gs_out, equal=all(abs(gs_src[k] - gs_out.get(k, 1e9)) < 1e-6 for k in gs_src if k != 'OriginalUnitScaleFactor'))
    log('lite FBX settings', gs_out, 'equal', STATS['fbx_settings']['equal'])
STATS['fbx'] = os.path.relpath(main, ROOT).replace('\\', '/'); STATS['fbx_bytes'] = os.path.getsize(main)
json.dump(STATS, open(os.path.join(WORK, 'stats.json'), 'w', encoding='utf-8'), indent=1, ensure_ascii=False)
log('DONE', main, STATS['fbx_bytes'], 'bytes')
