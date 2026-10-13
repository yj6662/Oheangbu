"""#308 1인칭 손 전용 모델(D308-80h): 플레이어 오른팔을 잘라, 손가락이 자루를 감싼 모양을 메시에 굽는다.
    blender -b --factory-startup -P nearhand_build.py -- [stills] [export] [key=value ...]
왜: 런타임에서 손가락 마디를 돌려 감싸는 방식이 네 번 거부됐다(사용자 2026-10-13 "blender로 손 모델 따로 만들어서 진행해봐").
무엇: 팔 뼈(RightArm · RightForeArm · RightHand)는 쉬는 자세 그대로 두고 손가락만 감싼 뒤 그 모양을 정점에 굽고,
      손가락 가중치를 RightHand 로 합친다 → Unity 에서는 손가락 계산 없이 손 뼈 하나로 놓는다.
쥐기 약속(사용자 그림): 손바닥이 자루를 밑에서 받치고, 네 손가락이 건너편으로 돌아 자루 위에 얹히며, 엄지는 가까운 쪽으로 올라와 자루 위에 누워 붓끝(앞)을 향한다.
출력: Art/Characters308/GreatBrush308/nearhand/  SM_NearHand308.fbx · nearhand.json · stills/*.png · sheet.png"""
import bpy, json, math, os, sys
import numpy as np
from mathutils import Vector, Matrix
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
import common_m1 as C
ARGS = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
P = dict(r=.0178, palm=.021, back=.008, rf=.010, cap1=85, cap2=100, cap3=75, slant=0, rt=.012, theta=35, tw=1.0, smooth=0, res=640, pad=.006, sub=1, spp=24, sub2=1, lump=.35, tex=2048, bspp=48, handuv=2.2, red=.50, sr=.74, sg=.50, sb=.40, ao=.75, bump=.6)
for a in ARGS:
    if '=' in a: k, v = a.split('='); P[k] = float(v)
OUT = C.GB + '/nearhand'; os.makedirs(OUT + '/stills', exist_ok=True)
C.R_SHAFT = P['r']
g = C.init(C.GB + '/_none_.blend'); arm = g['arm']; body = g['body']; AW = g['AW']; RH, RT = g['REST_HEAD'], g['REST_TAIL']
S = 'Right'
print('NH animation on import: armature', arm.animation_data.action.name if arm.animation_data and arm.animation_data.action else None, '· brush rig', g['brush_rig'].animation_data.action.name if g['brush_rig'].animation_data and g['brush_rig'].animation_data.action else None)
for o in (arm, g['brush_rig'], g['brush']): o.animation_data_clear()      # 들여온 테이크가 렌더 때 자세를 다시 덮어쓴다

# ---- 1) 오른팔만 남긴다(정점 순서는 원본의 부분 순서 그대로)
gi = {v.index: v.name for v in body.vertex_groups}
keep = np.zeros(len(body.data.vertices), bool)
for v in body.data.vertices:
    w = sum(x.weight for x in v.groups if gi[x.group].startswith(S) and ('Arm' in gi[x.group] or 'Hand' in gi[x.group]))
    keep[v.index] = w > .5
bpy.ops.object.select_all(action='DESELECT'); body.select_set(True); bpy.context.view_layer.objects.active = body
bpy.ops.object.duplicate(); hand = bpy.context.active_object; hand.name = 'SM_NearHand308'; hand.data.name = 'SM_NearHand308'
import bmesh
bm = bmesh.new(); bm.from_mesh(hand.data); bm.verts.ensure_lookup_table()
bmesh.ops.delete(bm, geom=[v for v in bm.verts if not keep[v.index]], context='VERTS'); bm.to_mesh(hand.data); bm.free()
body.hide_render = True; body.hide_viewport = True
for o in bpy.data.objects:
    if o.name.startswith('FaceLid'): o.hide_render = True
print('NH shape keys on the cut mesh:', [k.name for k in hand.data.shape_keys.key_blocks] if hand.data.shape_keys else None)
if hand.data.shape_keys: hand.shape_key_clear()      # 모양 키가 있으면 정점에 적은 굽기가 무시된다
# ---- 1a) 촘촘하게(사용자 2026-10-13 "메시 더 늘리고 … 1인칭 화면에서 계속 가까이서 보게 될 손이니까"): 자세를 잡기 전에 나눠야 굽힌 마디가 둥글다
if P['sub'] > 0:
    bpy.ops.object.select_all(action='DESELECT'); hand.select_set(True); bpy.context.view_layer.objects.active = hand
    sm = hand.modifiers.new('Sub', 'SUBSURF'); sm.levels = int(P['sub']); sm.render_levels = int(P['sub']); sm.subdivision_type = 'CATMULL_CLARK'; sm.uv_smooth = 'PRESERVE_BOUNDARIES'; sm.boundary_smooth = 'PRESERVE_CORNERS'
    while hand.modifiers[0].name != 'Sub': bpy.ops.object.modifier_move_up(modifier='Sub')
    bpy.ops.object.modifier_apply(modifier='Sub')
if P['sub2'] > 0:          # 손(손목 아래)만 한 번 더: 화면에 가장 크게 보이는 곳
    hgx = {v.index for v in hand.vertex_groups if v.name.startswith(S + 'Hand')}
    isHand = [sum(x.weight for x in v.groups if x.group in hgx) > .5 for v in hand.data.vertices]
    bpy.ops.object.select_all(action='DESELECT'); hand.select_set(True); bpy.context.view_layer.objects.active = hand
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_mode(type='FACE'); bpy.ops.mesh.select_all(action='DESELECT'); bpy.ops.object.mode_set(mode='OBJECT')
    for pl in hand.data.polygons: pl.select = all(isHand[i] for i in pl.vertices)
    bpy.ops.object.mode_set(mode='EDIT')
    if P['lump'] > 0: bpy.ops.mesh.vertices_smooth(factor=P['lump'], repeat=2)      # 원본 손의 울퉁불퉁함을 조금 편다
    bpy.ops.mesh.subdivide(number_cuts=1, smoothness=1.0)
    bpy.ops.object.mode_set(mode='OBJECT')
for pl in hand.data.polygons: pl.use_smooth = True
print('NH cut verts', len(hand.data.vertices), 'faces', len(hand.data.polygons))

# ---- 1b) 끝 마디의 진짜 길이: 들여온 FBX 의 끝 뼈 꼬리는 지어낸 길이라, 메시에서 손끝(그 뼈가 지배하는 정점 중 가장 먼 것)을 잰다
hgi = {v.index: v.name for v in hand.vertex_groups}; far = {}
for v in hand.data.vertices:
    best = max(v.groups, key=lambda x: x.weight, default=None)
    if best is None: continue
    n = hgi[best.group]
    if not (n.startswith(S + 'Hand') and n.endswith('3')): continue
    d = (RT[n] - RH[n]).normalized(); t = (AW @ hand.data.vertices[v.index].co - RH[n]).dot(d) if False else ((hand.matrix_world @ v.co) - RH[n]).dot(d); far[n] = max(far.get(n, 0), t)
for n, t in far.items():
    old = (RT[n] - RH[n]).length; RT[n] = RH[n] + (RT[n] - RH[n]).normalized() * (t - P['pad'])      # 손끝 살 두께만큼 안쪽이 뼈 끝
    print('NH tip', n, 'bone tail %.3f -> mesh tip %.3f' % (old, t))

# ---- 2) 네 손가락: 승인된 감기(자루에 닿을 때까지)
cfg = dict(C.GRIP_CFG); cfg.update(palm=P['palm'], back=P['back'], rf=P['rf'], caps=(P['cap1'], P['cap2'], P['cap3']))
grip = C.Grip(S, cfg, slant=P['slant']); A, N, F, Cc = grip.A, grip.N, grip.F, grip.C
pose = C.Pose(); pose.fk()
for n, L in grip.L.items():
    if 'Thumb' not in n: pose.L[n] = L

# ---- 3) 엄지: 가까운 쪽(손목 쪽)으로 올라와 자루 위에 누워 붓끝을 향한다. 거친 격자 탐색.
names = [S + 'HandThumb' + str(i) for i in (1, 2, 3)]; pr = [RH[n] for n in names] + [RT[names[2]]]
seg = [pr[i + 1] - pr[i] for i in range(3)]; thr = P['r'] + P['rt']
th = math.radians(P['theta']); pad = (N * math.cos(th) - F * math.sin(th)).normalized()        # 엄지 바닥이 얹힐 둘레 위치(위에서 가까운 쪽으로 theta 도)
def off_axis(p): x = p - Cc; return x - A * x.dot(A)
best = None
t0 = seg[0].normalized()
for yaw in range(-70, 71, 5):
    for pit in range(-70, 71, 5):
        Q1 = C.axis_rot(N, yaw) @ C.axis_rot(F, pit)
        d1 = Q1 @ t0
        for c2 in range(-20, 61, 5):
            k = (Q1 @ seg[1]).cross(off_axis(pr[0] + Q1 @ seg[0]) * -1)
            if k.length < 1e-6: continue
            Q2 = C.axis_rot(k, c2) @ Q1
            for c3 in range(-10, 51, 5):
                Q3 = C.axis_rot(k, c3) @ Q2
                p = [pr[0], pr[0] + Q1 @ seg[0]]; p.append(p[1] + Q2 @ seg[1]); p.append(p[2] + Q3 @ seg[2])
                cost = 0
                for a_, b_, wgt in ((p[1], p[2], 1.0), (p[2], p[3], 2.0)):
                    for t in (.25, .6, 1.0):
                        o = off_axis(a_.lerp(b_, t)); dd = o.length - thr
                        cost += wgt * (dd * dd * (9 if dd < 0 else 1)) * 4000
                        if t == 1.0: cost += wgt * (1 - o.normalized().dot(pad)) * .6
                cost += (1 - (p[3] - p[2]).normalized().dot(A)) * P['tw'] + (1 - (p[2] - p[1]).normalized().dot(A)) * .4 * P['tw']
                cost += (abs(c2) + abs(c3)) * .0008 + (abs(yaw) + abs(pit)) * .0006
                if best is None or cost < best[0]: best = (cost, Q1, Q2, Q3, yaw, pit, c2, c3, p)
cost, Q1, Q2, Q3, yaw, pit, c2, c3, tp = best
pose.L[names[0]] = Q1; pose.L[names[1]] = Q1.inverted() @ Q2; pose.L[names[2]] = Q2.inverted() @ Q3
print('NH thumb cost %.4f yaw %d pitch %d curl %d %d  tip off-axis %.4f (want %.4f)  tip along shaft dot %.2f  pad dot %.2f' % (
    cost, yaw, pit, c2, c3, off_axis(tp[3]).length, thr, (tp[3] - tp[2]).normalized().dot(A), off_axis(tp[3]).normalized().dot(pad)))
pose.fk(); pose.apply()
for f in ('Index', 'Middle', 'Ring', 'Pinky', 'Thumb'):
    row = []
    for j in (1, 2, 3):
        o = off_axis(pose.H[S + 'Hand' + f + str(j)]); row.append('(N %+.3f F %+.3f r %.3f)' % (o.dot(N), o.dot(F), o.length))
    n3 = S + 'Hand' + f + '3'; tip = pose.H[n3] + pose.Q[n3] @ (RT[n3] - RH[n3]); o = off_axis(tip); row.append('tip (N %+.3f F %+.3f r %.3f)' % (o.dot(N), o.dot(F), o.length))
    print('NH joint', f, ' '.join(row))
o = off_axis(RH[S + 'Hand']); print('NH wrist (N %+.3f F %+.3f)' % (o.dot(N), o.dot(F)), 'N world', tuple(round(x, 2) for x in N), 'F world', tuple(round(x, 2) for x in F), 'A world', tuple(round(x, 2) for x in A))

# ---- 4) 붓을 쥐는 자리에 놓는다(리그 +Z = 붓끝 = A)
rig = g['brush_rig']; rig.matrix_world = Matrix.Translation(Cc) @ C.frame(A, N).to_4x4() @ C.frame(Vector((0, 0, 1)), Vector((1, 0, 0))).transposed().to_4x4()
bpy.context.view_layer.update()
dgb = bpy.context.evaluated_depsgraph_get(); eb = g['brush'].evaluated_get(dgb); rr = []
for v in eb.data.vertices:
    x = eb.matrix_world @ v.co - Cc; al = x.dot(A)
    if abs(al) < .3: rr.append((x - A * al).length)
print('NH brush check: rig origin - C = %.4f m, rig Z . A = %.3f, shaft verts within 0.3 m: n %d r %.4f..%.4f' % ((rig.matrix_world.translation - Cc).length, (rig.matrix_world.to_3x3() @ Vector((0, 0, 1))).normalized().dot(A), len(rr), min(rr), max(rr)))
ang = {n: round(math.degrees(L.to_quaternion().angle), 1) for n, L in grip.L.items() if 'Thumb' not in n}
print('NH finger curls', ang)

# ---- 5) 굽기: 변형된 정점을 메시에 적고, 손가락 가중치를 손 뼈로 합친다
mod = next(m for m in hand.modifiers if m.type == 'ARMATURE'); mod.use_deform_preserve_volume = True
if P['smooth'] > 0:
    cs = hand.modifiers.new('CS', 'CORRECTIVE_SMOOTH'); cs.factor = P['smooth']; cs.iterations = 8; cs.use_only_smooth = False; cs.rest_source = 'ORCO'
dg = bpy.context.evaluated_depsgraph_get(); ev = hand.evaluated_get(dg); co = np.empty(len(ev.data.vertices) * 3, 'f'); ev.data.vertices.foreach_get('co', co)
assert len(ev.data.vertices) == len(hand.data.vertices)
for m in list(hand.modifiers):
    if m.type != 'ARMATURE': hand.modifiers.remove(m)
hand.data.vertices.foreach_set('co', co); hand.data.update()
hgi2 = {v.index: v.name for v in hand.vertex_groups}; st = {}
for v in hand.data.vertices:
    best = max(v.groups, key=lambda x: x.weight, default=None)
    if best is None or not hgi2[best.group].startswith(S + 'Hand') or hgi2[best.group] == S + 'Hand': continue
    x = hand.matrix_world @ v.co - Cc; o = x - A * x.dot(A); c = st.setdefault(hgi2[best.group][len(S) + 4:], [0, 0, 9, 0, 0, 0])
    c[0] += 1; c[1] += o.length < P['r']; c[2] = min(c[2], o.length); c[3] = max(c[3], o.length); c[4] += o.dot(N); c[5] += o.dot(F); c.append(1 if (abs(o.dot(F)) < P['r'] and o.dot(N) > 0) else 0); c.append(round(o.dot(F), 3)); c.append(round(o.dot(N), 3))
for n in sorted(st): c = st[n]; print('NH flesh %-8s verts %3d inside shaft %3d  r %.3f..%.3f  mean N %+.3f F %+.3f' % (n, c[0], c[1], c[2], c[3], c[4] / c[0], c[5] / c[0]), 'over top', sum(c[6::3]), 'F range %.3f..%.3f N range %.3f..%.3f' % (min(c[7::3]), max(c[7::3]), min(c[8::3]), max(c[8::3])))
# ---- 5a) 칠할 자리(정점 속성 fx): R = 마디 등 쪽 붉은기, G = 손톱, B = 손 영역. 손가락 무게를 합치기 전에 뼈 이름으로 찾는다.
fx = hand.data.color_attributes.new('fx', 'FLOAT_COLOR', 'POINT'); fx2 = hand.data.color_attributes.new('fx2', 'FLOAT_COLOR', 'POINT'); NW = hand.matrix_world.to_3x3()
dors = lambda n: (pose.Q[n] @ (-N)).normalized()
joints = [(pose.H[S + 'Hand' + f + str(j)], dors(S + 'Hand' + f + str(j)), .010 if j == 1 else .008) for f in ('Index', 'Middle', 'Ring', 'Pinky') for j in (1, 2, 3)]
t3 = S + 'HandThumb3'; td_rest = (RT[t3] - RH[t3]).normalized(); tn_rest = RH[t3] - RH[S + 'HandIndex1']; tn_rest = (tn_rest - td_rest * tn_rest.dot(td_rest)).normalized()
joints += [(pose.H[S + 'HandThumb' + str(j)], (pose.Q[S + 'HandThumb' + str(j)] @ tn_rest).normalized(), .009) for j in (2, 3)]
nails = {}
for f in ('Index', 'Middle', 'Ring', 'Pinky', 'Thumb'):
    n = S + 'Hand' + f + '3'; d = (pose.Q[n] @ (RT[n] - RH[n])).normalized(); L = (RT[n] - RH[n]).length + P['pad']
    nails[n] = (pose.H[n], d, L, (pose.Q[n] @ tn_rest).normalized() if f == 'Thumb' else dors(n), .0078 if f == 'Thumb' else (.0052 if f == 'Pinky' else .0062))
nail_n = 0
for v in hand.data.vertices:
    best = max(v.groups, key=lambda x: x.weight, default=None); name = hgi2[best.group] if best else ''
    hw = sum(x.weight for x in v.groups if hgi2[x.group].startswith(S + 'Hand'))
    pw = hand.matrix_world @ v.co; nw = (NW @ v.normal).normalized(); red = 0.0; nail = 0.0
    if hw > .3:
        for jp, jd, sg in joints:
            dd = (pw - jp).length
            if dd < sg * 2.5: red = max(red, math.exp(-(dd / sg) ** 2) * max(0.0, nw.dot(jd)))
    if name in nails:
        h0, d, L, dn, half = nails[name]; x = pw - h0; t = x.dot(d) / L; lat = x.dot(d.cross(dn)); facing = nw.dot(dn)
        if facing > .35:
            edge = min((t - .40) / .10, (1.02 - t) / .06, (half - abs(lat)) / .0012, (facing - .35) / .2)
            nail = max(0.0, min(1.0, edge))
            if nail > .5: nail_n += 1
    fx.data[v.index].color = (red, nail, min(1.0, hw), 1.0)
    fw = sum(x.weight for x in v.groups if hgi2[x.group].startswith(S + 'Hand') and hgi2[x.group] != S + 'Hand')
    fx2.data[v.index].color = (min(1.0, fw), 0, 0, 1.0)                                  # 손가락 살: 옛 그림의 검은 얼룩이 있어도 피부로 칠한다
print('NH paint: nail vertices', nail_n)

hg = hand.vertex_groups[S + 'Hand']; fing = {v.index for v in hand.vertex_groups if v.name.startswith(S + 'Hand') and v.name != S + 'Hand'}
moved = 0
for v in hand.data.vertices:
    w = sum(x.weight for x in v.groups if x.group in fing)
    if w > 0: hg.add([v.index], w, 'ADD'); moved += 1
for vg in [v for v in hand.vertex_groups if v.index in fing]: hand.vertex_groups.remove(vg)
for vg in [v for v in hand.vertex_groups if not any(v.name == S + x for x in ('Shoulder', 'Arm', 'ForeArm', 'Hand'))]: hand.vertex_groups.remove(vg)
mod.use_deform_preserve_volume = False
for pb in arm.pose.bones: pb.rotation_quaternion = (1, 0, 0, 0); pb.location = (0, 0, 0)
bpy.context.view_layer.update()
print('NH baked: finger-weighted verts merged into the hand bone', moved, 'groups left', [v.name for v in hand.vertex_groups])

# ---- 5b) 구운 메시가 자루 둘레를 얼마나 감는가(둘레 각 30 도 칸마다 정점 수 · 가장 가까운 반지름). 0 = 위(N), +90 = 건너편(F), 180 = 밑(손바닥), -90 = 가까운 쪽
MW = hand.matrix_world; bins = {}
for v in hand.data.vertices:
    x = MW @ v.co - Cc; al = x.dot(A)
    if abs(al) > .07: continue
    o = x - A * al
    if o.length > .05: continue
    k = int(round(math.degrees(math.atan2(o.dot(F), o.dot(N))) / 30)) * 30; k = 180 if k == -180 else k
    c = bins.setdefault(k, [0, 9]); c[0] += 1; c[1] = min(c[1], o.length)
print('NH wrap', ' '.join('%+d:%d/%.3f' % (k, bins[k][0], bins[k][1]) for k in sorted(bins)), ' shaft r %.4f' % P['r'])

vis = 0; sunk = 0; al_ = []
for v in hand.data.vertices:
    x = MW @ v.co - Cc; f_ = x.dot(F); n_ = x.dot(N)
    if abs(f_) < P['r'] and n_ > 0 and abs(x.dot(A)) < .08:
        if n_ > math.sqrt(P['r'] ** 2 - f_ * f_): vis += 1; al_.append(x.dot(A))
        else: sunk += 1
print('NH over the top (seen from +N, above the shaft surface): verts %d (along shaft %.3f..%.3f), sunk inside %d; hand world matrix scale %.3f, parent %s' % (vis, min(al_ or [0]), max(al_ or [0]), sunk, MW.to_scale().x, hand.parent.name if hand.parent else None))

# ---- 6) 수치: 손 뼈 기준점으로 만든 틀(축 관례와 무관)에서 자루 중심 · 방향
H = RH[S + 'Hand']; I = RH[S + 'HandIndex1']; K = RH[S + 'HandPinky1']; Mi = RH[S + 'HandMiddle1']
e1 = (Mi - H).normalized(); e3 = e1.cross((I - K)).normalized(); e2 = e3.cross(e1)          # e1 = 손목→중지 뿌리, e2 = 새끼→검지 쪽, e3 = 그 둘에 수직
def coef(v): return [round(v.dot(e1), 5), round(v.dot(e2), 5), round(v.dot(e3), 5)]
info = dict(basis='e1 = norm(Middle1 - Hand), e3 = norm(e1 x (Index1 - Pinky1)), e2 = e3 x e1 (bone head points, any space)',
            shaft_centre=coef(Cc - H), shaft_axis_to_tip=coef(A), palm_to_shaft=coef(N), finger_dir=coef(F), shaft_radius=P['r'],
            forearm_dir=coef((H - RH[S + 'ForeArm']).normalized()), knuckle_m=round((Mi - H).length, 5), params=P, cut_vertices=len(hand.data.vertices),
            thumb=dict(yaw=yaw, pitch=pit, curl=[c2, c3]), finger_curl_deg=ang,
            wrist_to_shaft_cm=round(100 * (Cc - H - A * (Cc - H).dot(A)).length, 2))
json.dump(info, open(OUT + '/nearhand.json', 'w'), indent=1); print('NH json', json.dumps(info)[:600])

# ---- 6b) 텍스처(사용자 2026-10-13 "텍스쳐 현실적으로"): 팔 전용 UV 를 새로 펴고, 옛 그림(몸 전체 아틀라스의 작은 조각) 위에 피부를 다시 칠해 굽는다.
#      피부: 옛 색을 바탕으로 얼룩 · 마디 붉은기 · 손톱 · 손가락 사이와 자루에 닿은 곳의 그늘(쥔 모양이 고정이라 구워도 된다). 법선: 모공 · 마디 주름 · 손톱 테두리.
if 'tex' in ARGS or 'export' in ARGS:
    scn = bpy.context.scene; T = int(P['tex'])
    old_uv = hand.data.uv_layers[0].name
    bpy.ops.object.select_all(action='DESELECT'); hand.select_set(True); bpy.context.view_layer.objects.active = hand
    new_uv = hand.data.uv_layers.new(name='UVNear'); hand.data.uv_layers.active = new_uv
    # 손(가까이서 계속 보는 곳)이 그림의 왼쪽 64 % 를 쓰고 소매 조각들이 나머지를 쓴다
    fxd = hand.data.color_attributes['fx'].data
    is_hand = [all(fxd[v].color[2] > .5 for v in f.vertices) for f in hand.data.polygons]
    # 한 번에 편다(전체 선택: 골라 펴기는 선택이 새서 겹쳤다). 그 뒤 손 쪽 섬만 키워서 다시 채운다 = 손이 그림을 더 많이 쓴다.
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT'); bpy.ops.uv.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=.001, margin_method='FRACTION'); bpy.ops.uv.average_islands_scale(); bpy.ops.object.mode_set(mode='OBJECT')
    import bmesh
    bm = bmesh.new(); bm.from_mesh(hand.data); bm.faces.ensure_lookup_table(); ul = bm.loops.layers.uv['UVNear']; par = list(range(len(bm.faces)))
    def find(a):
        while par[a] != a: par[a] = par[par[a]]; a = par[a]
        return a
    for e in bm.edges:
        ll = e.link_loops
        if len(ll) != 2: continue
        a, b = ll
        ua = {a.vert.index: a[ul].uv, a.link_loop_next.vert.index: a.link_loop_next[ul].uv}; ub = {b.vert.index: b[ul].uv, b.link_loop_next.vert.index: b.link_loop_next[ul].uv}
        if all((ua[k] - ub[k]).length < 1e-5 for k in ua): par[find(a.face.index)] = find(b.face.index)
    isl = {}
    for f in bm.faces: isl.setdefault(find(f.index), []).append(f)
    big = 0
    for fs in isl.values():
        if sum(1 for f in fs if is_hand[f.index]) * 2 < len(fs): continue
        big += 1
        for f in fs:
            for l in f.loops: l[ul].uv = l[ul].uv * P['handuv']
    bm.to_mesh(hand.data); bm.free(); hand.data.update()
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT'); bpy.ops.uv.select_all(action='SELECT')
    bpy.ops.uv.pack_islands(rotate=True, margin_method='FRACTION', margin=.003); bpy.ops.object.mode_set(mode='OBJECT')
    print('NH uv islands', len(isl), 'of which hand (enlarged x%.1f)' % P['handuv'], big)
    uvl = hand.data.uv_layers['UVNear']; cover = [0.0, 0.0]
    for f in hand.data.polygons:
        q = [uvl.data[li].uv for li in f.loop_indices]
        cover[0 if is_hand[f.index] else 1] += abs(sum(q[i].x * q[(i + 1) % len(q)].y - q[(i + 1) % len(q)].x * q[i].y for i in range(len(q)))) * .5
    print('NH uv: hand faces', sum(is_hand), 'sleeve faces', len(is_hand) - sum(is_hand), '· share of the whole picture: hand %.3f sleeve %.3f' % tuple(cover))
    mat = hand.data.materials[0].copy(); mat.name = 'M_NearHand308_Bake'; hand.data.materials[0] = mat; nt = mat.node_tree; nd = nt.nodes; lk = nt.links
    oldimg = next(n for n in nd if n.type == 'TEX_IMAGE'); out = next(n for n in nd if n.type == 'OUTPUT_MATERIAL')
    uvn = nd.new('ShaderNodeUVMap'); uvn.uv_map = old_uv; lk.new(uvn.outputs[0], oldimg.inputs[0])
    att = nd.new('ShaderNodeAttribute'); att.attribute_name = 'fx'; sep = nd.new('ShaderNodeSeparateColor'); lk.new(att.outputs['Color'], sep.inputs[0])
    osep = nd.new('ShaderNodeSeparateColor'); lk.new(oldimg.outputs['Color'], osep.inputs[0])
    att2 = nd.new('ShaderNodeAttribute'); att2.attribute_name = 'fx2'; sep2 = nd.new('ShaderNodeSeparateColor'); lk.new(att2.outputs['Color'], sep2.inputs[0])
    def math_(op, a, b=None, clamp=False):
        m = nd.new('ShaderNodeMath'); m.operation = op; m.use_clamp = clamp
        for i, x in enumerate((a, b)):
            if x is None: continue
            if isinstance(x, (int, float)): m.inputs[i].default_value = x
            else: lk.new(x, m.inputs[i])
        return m.outputs[0]
    def mix(fac, a, b, mode='MIX'):
        m = nd.new('ShaderNodeMix'); m.data_type = 'RGBA'; m.blend_type = mode; m.clamp_factor = True
        if isinstance(fac, (int, float)): m.inputs[0].default_value = fac
        else: lk.new(fac, m.inputs[0])
        for sock, x in ((m.inputs[6], a), (m.inputs[7], b)):
            if isinstance(x, tuple): sock.default_value = x
            else: lk.new(x, sock)
        return m.outputs[2]
    # 피부인 곳 = 손 영역이면서 옛 그림이 살색(붉은 쪽이 푸른 쪽보다 밝다); 소매는 그대로 둔다
    skin = math_('MULTIPLY', math_('MULTIPLY', math_('SUBTRACT', osep.outputs[0], osep.outputs[2]), 14.0, True), math_('MULTIPLY', sep.outputs[2], 1.6, True), True)
    skin = math_('MAXIMUM', skin, math_('MULTIPLY', sep2.outputs[0], 2.5, True))
    tc = nd.new('ShaderNodeTexCoord')
    n1 = nd.new('ShaderNodeTexNoise'); n1.inputs['Scale'].default_value = 55; n1.inputs['Detail'].default_value = 4; lk.new(tc.outputs['Object'], n1.inputs['Vector'])
    n2 = nd.new('ShaderNodeTexNoise'); n2.inputs['Scale'].default_value = 900; n2.inputs['Detail'].default_value = 2; lk.new(tc.outputs['Object'], n2.inputs['Vector'])
    lum = math_('ADD', math_('MULTIPLY', osep.outputs[0], .5), math_('MULTIPLY', osep.outputs[1], .5))
    base = mix(math_('ADD', .55, math_('MULTIPLY', math_('SUBTRACT', .55, lum), 1.6, True), True), oldimg.outputs['Color'], (P['sr'], P['sg'], P['sb'], 1))                                         # 옛 색을 살리되 따뜻한 살색 쪽으로
    mott = mix(math_('MULTIPLY', math_('SUBTRACT', n1.outputs[0], .5), 1.1), base, (.62, .30, .24, 1))             # 큰 얼룩: 붉은 쪽으로 조금
    mott = mix(math_('MULTIPLY', math_('SUBTRACT', .5, n1.outputs[0]), .9), mott, (.80, .64, .50, 1))              # 반대쪽은 누런 쪽으로
    red = mix(math_('MULTIPLY', sep.outputs[0], P['red']), mott, (.60, .24, .20, 1))                                    # 마디 등 쪽
    nailc = mix(math_('MULTIPLY', n2.outputs[0], .15), (.86, .66, .62, 1), (.93, .80, .76, 1))
    withnail = mix(sep.outputs[1], red, nailc)
    ao = nd.new('ShaderNodeAmbientOcclusion'); ao.samples = 16; ao.inputs['Distance'].default_value = .018
    shade = mix(math_('MULTIPLY', math_('SUBTRACT', 1.0, ao.outputs['AO']), P['ao'], True), withnail, (.33, .13, .10, 1), 'MULTIPLY')   # 그늘은 붉은 갈색
    final = mix(skin, oldimg.outputs['Color'], shade)
    emi = nd.new('ShaderNodeEmission'); lk.new(final, emi.inputs[0])
    # 법선: 모공 + 마디 주름 + 손톱은 매끈
    wav = nd.new('ShaderNodeTexWave'); wav.inputs['Scale'].default_value = 260; wav.inputs['Distortion'].default_value = 3.5; lk.new(tc.outputs['Object'], wav.inputs['Vector'])
    h = math_('ADD', math_('MULTIPLY', n2.outputs[0], .5), math_('MULTIPLY', math_('MULTIPLY', wav.outputs['Fac'], sep.outputs[0]), 1.6))
    h = math_('MULTIPLY', h, math_('SUBTRACT', 1.0, sep.outputs[1]))
    h = math_('ADD', h, math_('MULTIPLY', sep.outputs[1], 1.2))                                                       # 손톱이 살보다 조금 솟는다
    bump = nd.new('ShaderNodeBump'); bump.inputs['Strength'].default_value = P['bump']; bump.inputs['Distance'].default_value = .0006; lk.new(math_('MULTIPLY', h, skin), bump.inputs['Height'])
    pbs = nd.new('ShaderNodeBsdfPrincipled'); lk.new(bump.outputs[0], pbs.inputs['Normal'])
    def target(name, non_color):
        img = bpy.data.images.new(name, T, T, alpha=False); img.colorspace_settings.name = 'Non-Color' if non_color else 'sRGB'
        tn = nd.new('ShaderNodeTexImage'); tn.image = img
        for n in nd: n.select = False
        tn.select = True; nd.active = tn; return img, tn
    scn.render.engine = 'CYCLES'; scn.cycles.device = 'CPU'; scn.cycles.samples = int(P['bspp']); scn.cycles.use_denoising = False
    scn.render.bake.margin = 16; scn.render.bake.use_clear = True; scn.render.bake.use_selected_to_active = False
    g['brush'].hide_render = False
    cimg, cnode = target('T_NearHand308_Base', False); lk.new(emi.outputs[0], out.inputs['Surface'])
    bpy.ops.object.bake(type='EMIT'); cimg.filepath_raw = OUT + '/T_NearHand308_Base.png'; cimg.file_format = 'PNG'; cimg.save()
    nimg, nnode = target('T_NearHand308_Normal', True); lk.new(pbs.outputs[0], out.inputs['Surface'])
    bpy.ops.object.bake(type='NORMAL'); nimg.filepath_raw = OUT + '/T_NearHand308_Normal.png'; nimg.file_format = 'PNG'; nimg.save()
    print('NH baked textures', T, 'px')
    # 스틸 · 내보내기용 재질: 구운 그림만(새 UV)
    hand.data.uv_layers.remove(hand.data.uv_layers[old_uv]); hand.data.uv_layers[0].name = 'UVMap'
    fin = bpy.data.materials.new('M_NearHand308'); fin.use_nodes = True; fn = fin.node_tree.nodes; fl = fin.node_tree.links; fp = next(n for n in fn if n.type == 'BSDF_PRINCIPLED')
    ci = fn.new('ShaderNodeTexImage'); ci.image = cimg; fl.new(ci.outputs['Color'], fp.inputs['Base Color'])
    ni = fn.new('ShaderNodeTexImage'); ni.image = nimg; nm = fn.new('ShaderNodeNormalMap'); fl.new(ni.outputs['Color'], nm.inputs['Color']); fl.new(nm.outputs[0], fp.inputs['Normal'])
    fp.inputs['Roughness'].default_value = .55
    hand.data.materials[0] = fin

# ---- 7) 스틸
if 'stills' in ARGS:
    cam = C.setup_render(int(P['res']), int(P['res'])); bpy.data.objects['Floor'].hide_render = True
    # Workbench 는 반투명 물체를 깊이 없이 물체 원점 거리 순으로 그린다 → 손과 붓의 앞뒤가 시점마다 뒤바뀐다(광선 검사와 스틸이 어긋났던 까닭). 전부 불투명으로.
    print('NH alpha before:', [(m.name, round(m.diffuse_color[3], 2)) for m in bpy.data.materials], [(i.name, i.alpha_mode) for i in bpy.data.images if i.type == 'IMAGE'])
    for m in bpy.data.materials: m.diffuse_color[3] = 1
    for i in bpy.data.images:
        if i.type == 'IMAGE': i.alpha_mode = 'NONE'
    for o in bpy.data.objects: o.color[3] = 1
    # Workbench 스틸은 손과 자루의 앞뒤를 틀리게 그렸다(광선 검사와 어긋남, 원인 미확인) → Cycles(CPU)로 찍는다
    scn = bpy.context.scene; scn.render.engine = 'CYCLES'; scn.cycles.device = 'CPU'; scn.cycles.samples = int(P['spp']); scn.cycles.use_denoising = True; scn.cycles.max_bounces = 3
    scn.world.use_nodes = True; bgn = next(n for n in scn.world.node_tree.nodes if n.type == 'BACKGROUND'); bgn.inputs[0].default_value = (.55, .55, .55, 1); bgn.inputs[1].default_value = .9
    sun = bpy.data.objects.new('Sun', bpy.data.lights.new('Sun', 'SUN')); scn.collection.objects.link(sun); sun.data.energy = 3.0; sun.data.angle = .3
    sun.rotation_euler = (-(N * .8 - A * .4 - F * .45)).to_track_quat('-Z', 'Y').to_euler()
    mod.show_render = False; mod.show_viewport = False      # 스틸은 구운 정점 그대로(뼈대가 렌더 때 다시 움직이지 못하게)
    def view(name, pos, target, up, fov=38):
        f = (target - pos).normalized(); r = f.cross(up).normalized(); u = r.cross(f)
        cam.matrix_world = Matrix.Translation(pos) @ Matrix((r, u, -f)).transposed().to_4x4(); cam.data.angle_y = math.radians(fov)
        C.shot('%s/stills/%s%s.png' % (OUT, os.environ.get('NH_TAG', ''), name))
    if 'marks' in ARGS:          # 방향 표시 공: 빨강 = N(손바닥→자루 너머), 초록 = F(건너편), 파랑 = A(붓끝)
        for nm, pos, col in (('N', Cc + N * .07, (1, 0, 0, 1)), ('F', Cc + F * .07, (0, 1, 0, 1)), ('A', Cc + A * .12, (0, 0, 1, 1))):
            bpy.ops.mesh.primitive_uv_sphere_add(radius=.008, location=pos); o = bpy.context.active_object; m = bpy.data.materials.new('mk' + nm); m.diffuse_color = col; o.data.materials.append(m)
        bpy.context.scene.display.shading.color_type = 'MATERIAL'
    if 'toponly' in ARGS:
        W_ = 220; rows_ = [[(120, 120, 120)] * W_ for _ in range(W_)]; dgx = bpy.context.evaluated_depsgraph_get(); span = .118
        for py_ in range(W_):
            for px_ in range(W_):
                o_ = Cc + N * .42 + (-F) * ((px_ / W_ - .5) * span) + A * ((.5 - py_ / W_) * span)
                ok, loc, nor, idx, ob, mtx = bpy.context.scene.ray_cast(dgx, o_, -N)
                if ok:
                    sh = max(.25, abs(nor.dot(N))); base = (60, 50, 45) if ob.name.startswith('SM_Great') else (225, 180, 160)
                    rows_[py_][px_] = tuple(int(c * sh) for c in base)
        open('%s/stills/%sray_top.ppm' % (OUT, os.environ.get('NH_TAG', '')), 'wb').write(b'P6 %d %d 255 ' % (W_, W_) + bytes(c for r_ in rows_ for px in r_ for c in px))
        print('NH cam object:', cam.name, 'parent', cam.parent, 'constraints', [c.type for c in cam.constraints], 'world pos', tuple(round(x, 3) for x in cam.matrix_world.translation))
        hits = []
        for i in range(-5, 8):
            o_ = Cc + N * .42 + A * (i * .01); ok, loc, nor, idx, ob, mtx = bpy.context.scene.ray_cast(d2 if False else bpy.context.evaluated_depsgraph_get(), o_, -N)
            hits.append('%+d:%s@N%.3f' % (i, ob.name[3:8] if ok else '-', (loc - Cc).dot(N) if ok else 0))
        print('NH rays from the top camera side straight down onto the shaft centre line (cm along shaft : first object hit @ height):', ' '.join(hits))
        d2 = bpy.context.evaluated_depsgraph_get(); e2 = hand.evaluated_get(d2); dif = max((e2.data.vertices[i].co - hand.data.vertices[i].co).length for i in range(len(hand.data.vertices)))
        print('NH render-time mesh vs baked data: max vertex difference %.5f (object units), modifiers %s, armature pose_position %s' % (dif, [(m.type, m.show_render, m.show_viewport) for m in hand.modifiers], arm.data.pose_position))
        print('NH cam', tuple(round(x, 3) for x in (Cc + N * .42)), 'C', tuple(round(x, 3) for x in Cc), 'hand bbox centre', tuple(round(x, 3) for x in (hand.matrix_world @ (sum((Vector(c) for c in hand.bound_box), Vector()) / 8))))
        for nm, hide in (('zoom_top', False), ('zoom_top_nobrush', True)):
            g['brush'].hide_render = hide; view(nm, Cc + N * .42, Cc, A, 16)
        g['brush'].hide_render = False; view('zoom_side', Cc + N * .30 + F * .30, Cc, A, 20); view('zoom_side2', Cc + N * .30 - F * .30, Cc, A, 20)
        print('NH objects rendered:', [(o.name, o.type, tuple(round(x, 3) for x in o.dimensions)) for o in bpy.context.scene.objects if not o.hide_render and o.type == 'MESH'])
        raise SystemExit
    up = N          # 화면의 위 = 손바닥에서 자루 쪽(게임에서는 손바닥이 밑이므로 위)
    view('top', Cc + up * .42, Cc, A)                                        # 위에서(사용자 그림): 화면 위쪽 = 붓끝
    view('eye', Cc - A * .40 + up * .22 - F * .10, Cc + A * .15, up, 42)      # 눈: 뒤 · 위 · 가까운 쪽에서 붓끝을 본다
    if 'ortho' in ARGS:
        cam.data.type = 'ORTHO'; cam.data.ortho_scale = .118; view('ortho_top', Cc + up * .42, Cc, A); g['brush'].hide_render = True; view('ortho_top_nobrush', Cc + up * .42, Cc, A); g['brush'].hide_render = False
        hand.hide_render = True; view('ortho_top_nohand', Cc + up * .42, Cc, A); hand.hide_render = False; cam.data.type = 'PERSP'
        print('NH render-visible meshes', [(o.name, [(m.type, m.show_render, m.show_viewport) for m in o.modifiers]) for o in scn.objects if o.type == 'MESH' and not o.hide_render])
    if 'quick' in ARGS: raise SystemExit
    view('near', Cc - F * .42 + up * .06, Cc, up)                             # 가까운 쪽(손목 쪽)에서
    view('far', Cc + F * .42 + up * .06, Cc, up)                              # 건너편에서
    view('end', Cc + A * .40 + up * .03, Cc, up)                              # 붓끝 쪽에서: 둘레 감김
    view('under', Cc - up * .42, Cc, A)                                       # 밑에서: 손바닥
    view('quarter', Cc + up * .30 + F * .22 + A * .18, Cc, up)                # 위 · 건너편 · 앞에서 비스듬히
    g['brush'].hide_render = True; view('top_nobrush', Cc + up * .42, Cc, A); g['brush'].hide_render = False
    print('NH cam check: top camera is on the +N side of the shaft by %.2f m; brush show_in_front %s %s' % ((Cc + up * .42 - Cc).dot(N), g['brush'].show_in_front, rig.show_in_front))
    print('NH stills done')

mod.show_render = True; mod.show_viewport = True
# ---- 8) 내보내기: 뼈대(쉬는 자세 그대로) + 구운 팔
if 'export' in ARGS:
    rig.hide_viewport = False
    bpy.ops.object.select_all(action='DESELECT'); arm.select_set(True); hand.select_set(True); bpy.context.view_layer.objects.active = arm
    bpy.ops.export_scene.fbx(filepath=OUT + '/SM_NearHand308.fbx', use_selection=True, object_types={'ARMATURE', 'MESH'}, add_leaf_bones=False, bake_anim=False,
                             axis_forward='-Z', axis_up='Y', mesh_smooth_type='OFF', use_mesh_modifiers=False)
    print('NH export', os.path.getsize(OUT + '/SM_NearHand308.fbx'))
