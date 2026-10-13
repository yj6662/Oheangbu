"""#308 GreatBrush motion1: 공용(장면 열기 · 자세 FK · 두 뼈 IK · 쥐기 · 카메라). pose.py 에서 옮겨 왔다(pose.py 는 들여오면 장면 전체를 돌리므로 직접 못 쓴다).
좌표: 캐릭터는 -Y 를 본다. 오른쪽 = -X, 위 = +Z."""
import bpy, json, math, os
import numpy as np
from mathutils import Vector, Matrix, Quaternion

ROOT = 'C:/Users/yj666/Oheangbu'
GB = ROOT + '/Art/Characters308/GreatBrush308'
OUT = GB + '/motion1'
V3 = GB + '/v3/SM_GreatBrush308.blend'
PLAYER = ROOT + '/Oheangbu/Assets/_Project/Art/Characters/PlaytestReRig/Models/Player_C02_ReRig.fbx'
MOCAP = GB + '/mocap'
BLEND = OUT + '/GreatBrush308_motion1.blend'
CLIPS = ['Great_Sword_Power_Slash', 'Standing_Melee_Attack_Horizontal', 'Heavy_Weapon_Swing', 'Standing_Melee_Attack_360_High', 'Great_Sword_Downward_Slash',
         'Standing_2H_Magic_Area_Attack_01', 'Great_Sword_Standing_To_Block', 'Great_Sword_Blocking_Idle', 'Great_Sword_Power_Up', 'Great_Sword_Idle']
TAIL = .60; TIPL = .95; HEADL = .38; HEADR = .048; R_SHAFT = .017; HAND_GAP = .42
TWIST_SHARE = .55
GRIP_CFG = dict(palm=.021, back=.008, rf=.010, caps=(85, 100, 75), squeeze=0, thumb_psi=25, thumb_along=-.15, thumb_caps=(70, 80), rt=.013, thumb_roll=0)   # poses_v2 의 승인된 쥐기
X, Y, Z = Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1))
ALIAS = {'Spine02': 'Spine', 'Spine01': 'Spine1', 'Spine': 'Spine2', 'neck': 'Neck'}      # 플레이어 뼈 → mixamorig 이름
G = {}          # 장면 전역(init 가 채운다)


def rot(axis, deg): return Matrix.Rotation(math.radians(deg), 3, axis)
def axis_rot(axis, deg): return Matrix.Rotation(math.radians(deg), 3, axis.normalized())


def frame(d, b):
    d = d.normalized(); b = (b - d * b.dot(d)).normalized(); c = d.cross(b)
    return Matrix((d, b, c)).transposed()


def init(blend=BLEND):
    """motion1 .blend 가 있으면 열고, 없으면 v3 붓 + 플레이어 FBX 로 새로 만든다(v3 파일에는 저장하지 않는다)."""
    if os.path.exists(blend):
        bpy.ops.wm.open_mainfile(filepath=blend)
        arm = bpy.data.objects['Armature']; body = next(o for o in bpy.data.objects if o.type == 'MESH' and o.name.startswith('C02'))
    else:
        bpy.ops.wm.open_mainfile(filepath=V3)
        before = set(bpy.data.objects)
        bpy.ops.import_scene.fbx(filepath=PLAYER, automatic_bone_orientation=False)
        new = [o for o in bpy.data.objects if o not in before]
        arm = next(o for o in new if o.type == 'ARMATURE'); body = next(o for o in new if o.type == 'MESH' and o.name.startswith('C02'))
        for o in new:
            if o.type == 'EMPTY': o.hide_render = True; o.hide_viewport = True
    bpy.context.scene.render.fps = 30
    g = G; g['arm'] = arm; g['body'] = body; g['brush_rig'] = bpy.data.objects['GreatBrush308_Rig']; g['brush'] = bpy.data.objects['SM_GreatBrush308']
    AW = arm.matrix_world.copy(); g['AW'] = AW; g['S_ARM'] = AW.to_scale().x
    B = {b.name: b for b in arm.data.bones}; g['BONES'] = B
    order = []
    def walk(b):
        order.append(b.name)
        for c in b.children: walk(c)
    for b in arm.data.bones:
        if b.parent is None: walk(b)
    g['ORDER'] = order
    g['REST_ROT'] = {n: b.matrix_local.to_3x3() for n, b in B.items()}
    g['REST_HEAD'] = {n: AW @ b.head_local for n, b in B.items()}
    g['REST_TAIL'] = {n: AW @ b.tail_local for n, b in B.items()}
    g['PARENT'] = {n: (b.parent.name if b.parent else None) for n, b in B.items()}
    for pb in arm.pose.bones: pb.rotation_mode = 'QUATERNION'
    g['brush_rig'].rotation_mode = 'QUATERNION'
    for pb in g['brush_rig'].pose.bones: pb.rotation_mode = 'QUATERNION'
    gi = {v.index: v.name for v in body.vertex_groups}; dom = []
    for v in body.data.vertices:
        best = max(v.groups, key=lambda x: x.weight, default=None); dom.append(gi[best.group] if best else '')
    dom = np.array(dom); g['dom'] = dom; g['is_hand'] = np.array(['Hand' in d for d in dom]); g['is_arm'] = np.array([('Arm' in d or 'Hand' in d) for d in dom])
    return g


class Pose:
    def __init__(self):
        self.L = {n: Matrix.Identity(3) for n in G['BONES']}; self.hips_t = Vector((0, 0, 0)); self.Q = {}; self.H = {}

    def fk(self):
        RH, PA = G['REST_HEAD'], G['PARENT']
        for n in G['ORDER']:
            p = PA[n]
            if p is None: self.Q[n] = self.L[n].copy(); self.H[n] = RH[n] + self.hips_t
            else: self.Q[n] = self.Q[p] @ self.L[n]; self.H[n] = self.H[p] + self.Q[p] @ (RH[n] - RH[p])

    def set_world(self, n, q):
        p = G['PARENT'][n]; self.L[n] = (self.Q[p].inverted() @ q) if p else q.copy(); self.fk()

    def apply(self, prev=None):
        """자세를 리그에 적는다. prev = 앞 프레임의 사원수 사전(부호 연속성)."""
        arm = G['arm']; out = {}
        for n in G['ORDER']:
            pb = arm.pose.bones[n]; r = G['REST_ROT'][n]
            q = (r.inverted() @ self.L[n] @ r).to_quaternion()
            if prev is not None and n in prev and q.dot(prev[n]) < 0: q.negate()
            pb.rotation_quaternion = q; out[n] = q
            pb.location = (r.inverted() @ self.hips_t) / G['S_ARM'] if G['PARENT'][n] is None else (0, 0, 0)
        return out


def two_bone(A, W, l1, l2, hint):
    D = W - A; dist = D.length; reach = dist / (l1 + l2); dist = min(dist, (l1 + l2) * .999); dist = max(dist, abs(l1 - l2) * 1.01)
    Dn = D.normalized(); a = (l1 * l1 - l2 * l2 + dist * dist) / (2 * dist); h = math.sqrt(max(l1 * l1 - a * a, 0))
    hp = hint - Dn * hint.dot(Dn); hp = hp.normalized() if hp.length > 1e-6 else Dn.orthogonal().normalized()
    E = A + Dn * a + hp * h; W2 = A + Dn * dist
    return E, W2, reach


def seg_line_dist(p, q, c, a):
    best = 1e9
    for i in range(7):
        x = p.lerp(q, i / 6) - c; best = min(best, (x - a * x.dot(a)).length)
    return best


def seg_pt(p, a, b):
    ab = b - a; t = max(0, min(1, (p - a).dot(ab) / max(ab.dot(ab), 1e-9))); return (p - (a + ab * t)).length


class Grip:
    """한 손의 쥐기(pose.py 그대로): 쉬는 자세 월드 축에서 자루 축(A, 엄지 쪽 = 붓끝) · 손바닥 법선(N) · 자루 중심점(C) + 손가락 국소 회전."""

    def __init__(self, side, cfg=GRIP_CFG, slant=0):
        RH, RT = G['REST_HEAD'], G['REST_TAIL']
        s = side; self.side = s; hh = RH[s + 'Hand']
        mcp = [RH[s + 'Hand' + f + '1'] for f in ('Index', 'Middle', 'Ring', 'Pinky')]
        F = (RH[s + 'HandMiddle1'] - hh).normalized(); A0 = (mcp[0] - mcp[3]).normalized()
        N = F.cross(A0).normalized(); N = -N if N.z > 0 else N
        A = (A0 - N * A0.dot(N)).normalized()
        sg = 1 if N.cross(A).dot(F) > 0 else -1
        A = (axis_rot(N, slant * sg) @ A).normalized()
        Fp = N.cross(A); Fp = -Fp if Fp.dot(F) < 0 else Fp
        mid = (mcp[0] + mcp[1] + mcp[2] + mcp[3]) / 4
        self.C = mid + N * (R_SHAFT + cfg['palm']) - Fp * cfg['back']; self.A, self.N, self.F = A, N, Fp
        self.L = {}
        for f, rf in (('Index', cfg['rf']), ('Middle', cfg['rf']), ('Ring', cfg['rf'] * .95), ('Pinky', cfg['rf'] * .85)):
            names = [s + 'Hand' + f + str(i) for i in (1, 2, 3)]
            pr = [RH[n] for n in names] + [RT[names[2]]]
            k = (pr[1] - pr[0]).normalized().cross(N).normalized(); ang = [0, 0, 0]; caps = cfg['caps']; thr = R_SHAFT + rf

            def chain(ang):
                Q = Matrix.Identity(3); p = [pr[0]]
                for j in range(3): Q = Q @ axis_rot(k, ang[j]); p.append(p[-1] + Q @ (pr[j + 1] - pr[j]))
                return p
            for j in range(3):
                while ang[j] < caps[j]:
                    p = chain(ang)
                    if seg_line_dist(p[j].lerp(p[j + 1], .35), p[j + 1], self.C, A) <= thr: break
                    ang[j] += 1
            for j in range(3): self.L[names[j]] = axis_rot(k, ang[j])
        names = [s + 'HandThumb' + str(i) for i in (1, 2, 3)]; pr = [RH[n] for n in names] + [RT[names[2]]]
        t0 = (pr[1] - pr[0]).normalized(); kt = N.cross(Fp).normalized()
        t1 = (N * math.cos(math.radians(cfg['thumb_psi'])) + Fp * math.sin(math.radians(cfg['thumb_psi'])) + A * cfg['thumb_along']).normalized()
        Q1 = t0.rotation_difference(t1).to_matrix(); ang = [0, 0]; thr = R_SHAFT + cfg['rt']

        def tchain(ang):
            Q = Q1.copy(); p = [pr[0], pr[0] + Q @ (pr[1] - pr[0])]
            for j in range(2): Q = Q @ (Q1.inverted() @ axis_rot(kt, ang[j]) @ Q1); p.append(p[-1] + Q @ (pr[j + 2] - pr[j + 1]))
            return p
        for j in range(2):
            while ang[j] < cfg['thumb_caps'][j]:
                p = tchain(ang)
                if seg_line_dist(p[j + 1].lerp(p[j + 2], .35), p[j + 2], self.C, A) <= thr: break
                ang[j] += 1
        self.L[names[0]] = Q1
        for j in range(2): self.L[names[j + 1]] = Q1.inverted() @ axis_rot(kt, ang[j]) @ Q1

    def hand_q(self, d, n):
        """자루 방향 d(엄지 쪽이 붓끝), 손바닥이 향하는 방향 n → 손 뼈 월드 회전(쉬는 자세 대비)."""
        return frame(d, n) @ frame(self.A, self.N).transposed()


def body_points():
    body = G['body']
    dg = bpy.context.evaluated_depsgraph_get(); ev = body.evaluated_get(dg); me = ev.to_mesh()
    co = np.empty(len(me.vertices) * 3, dtype=np.float32); me.vertices.foreach_get('co', co); ev.to_mesh_clear()
    M = np.array(body.matrix_world, dtype=np.float32)
    return co.reshape(-1, 3) @ M[:3, :3].T + M[:3, 3]


def dist_to_segment(P, a, b):
    a = np.array(a, dtype=np.float32); b = np.array(b, dtype=np.float32); ab = b - a
    t = np.clip(((P - a) @ ab) / (ab @ ab), 0, 1); return np.linalg.norm(P - (a + t[:, None] * ab), axis=1), t


def assign(idblock, action):
    """Blender 5 슬롯: 액션을 붙이고 슬롯이 있으면 첫 슬롯을 고른다."""
    if idblock.animation_data is None: idblock.animation_data_create()
    idblock.animation_data.action = action
    if action is not None and len(action.slots):
        for sl in action.slots:
            try: idblock.animation_data.action_slot = sl; break
            except Exception: pass


def assign_clip(clip):
    g = G; ok = True
    for idb, suf in ((g['arm'], 'Body'), (g['brush_rig'], 'Brush'), (g['brush'].data.shape_keys, 'Keys')):
        a = bpy.data.actions.get('M1_%s_%s' % (clip, suf))
        if a is None: ok = False; continue
        assign(idb, a)
    return ok


# ---------------------------------------------------------------- 렌더
def setup_render(w=480, h=480):
    s = bpy.context.scene
    s.render.engine = 'BLENDER_WORKBENCH'
    s.render.resolution_x = w; s.render.resolution_y = h; s.render.resolution_percentage = 100; s.render.image_settings.file_format = 'PNG'
    s.render.film_transparent = False
    if s.world is None: s.world = bpy.data.worlds.new('Grey')
    try: s.world.use_nodes = False
    except Exception: pass
    s.world.color = (.30, .30, .30)
    sh = s.display.shading; sh.light = 'STUDIO'; sh.color_type = 'TEXTURE'; sh.show_shadows = True; sh.shadow_intensity = .35; sh.show_cavity = True; sh.cavity_type = 'BOTH'
    sh.show_object_outline = False; sh.show_specular_highlight = True; sh.curvature_ridge_factor = .6; sh.curvature_valley_factor = .8
    s.display.light_direction = (-.45, .35, .82); s.display.render_aa = '8'; s.view_settings.view_transform = 'Standard'
    if 'Floor' not in bpy.data.objects:
        fm = bpy.data.materials.new('Floor'); fm.diffuse_color = (.62, .62, .62, 1)
        bpy.ops.mesh.primitive_plane_add(size=40, location=(0, 0, 0)); fl = bpy.context.active_object; fl.name = 'Floor'; fl.data.materials.append(fm)
    cam = bpy.data.objects.get('Cam')
    if cam is None:
        cam = bpy.data.objects.new('Cam', bpy.data.cameras.new('Cam')); s.collection.objects.link(cam)
    s.camera = cam; cam.data.sensor_fit = 'VERTICAL'; cam.data.clip_start = .05
    return cam


def look(cam, pos, target, fov):
    cam.location = pos; cam.rotation_euler = (Vector(target) - Vector(pos)).to_track_quat('-Z', 'Y').to_euler(); cam.data.angle_y = math.radians(fov)


def shoulder_cam(cam, root_xy, pitch=8, pivot_z=1.60):
    """게임 어깨 카메라: 머리 높이 축에서 뒤 2.8 m · 위 0.55 m, 아래로 pitch 도, 세로 FOV 60."""
    pos = Vector((root_xy[0], root_xy[1] + 2.8, pivot_z + .55)); q = rot('X', pitch)
    look(cam, pos, pos + q @ Vector((0, -1, 0)), 60)


def shot(path):
    bpy.context.scene.render.filepath = path; bpy.ops.render.render(write_still=True)
