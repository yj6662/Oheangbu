"""#308 enemy rig revision 2: an offline model of what Unity's Humanoid (Mecanim) does to a clip that is imported and played on
ONE avatar (the monster's own). Pure numpy, no Blender, no Unity.

What is established [O] and what is not:
  * the joint frames (PreQ / PostQ) of the three live avatars are READ from Unity's own import artifacts (avatarblob308) and the
    rule that builds them from the avatar reference pose is recovered and checked against them (unity_frames, cause308.py)
  * the smallest elbow / knee angle an avatar can show follows from those frames alone (mid_floor)
  * NOT reproduced: the step in which Unity re-poses a limb whose bend it cannot represent (27 variants were compared with the editor's 84
    elbow ranges: 'elbow kept above the floor' is the best at 4.3 deg rms, the rest 10 - 35). So this module predicts frames and floors, not full poses of the live avatar.
Avatar space = Unity model space: x right, y up, z forward; the character's left is -x."""
import json, math, os, re
import numpy as np

P_BU = np.array([[-1., 0, 0], [0, 0, 1], [0, -1, 0]])   # Blender world (x left, -y forward, z up) -> Unity (x right, y up, z forward)

HUMAN = {'Hips': 'Hips', 'Spine': 'Spine02', 'Chest': 'Spine01', 'UpperChest': 'Spine', 'Neck': 'neck', 'Head': 'Head',
         'LeftShoulder': 'LeftShoulder', 'LeftUpperArm': 'LeftArm', 'LeftLowerArm': 'LeftForeArm', 'LeftHand': 'LeftHand',
         'RightShoulder': 'RightShoulder', 'RightUpperArm': 'RightArm', 'RightLowerArm': 'RightForeArm', 'RightHand': 'RightHand',
         'LeftUpperLeg': 'LeftUpLeg', 'LeftLowerLeg': 'LeftLeg', 'LeftFoot': 'LeftFoot', 'LeftToes': 'LeftToeBase',
         'RightUpperLeg': 'RightUpLeg', 'RightLowerLeg': 'RightLeg', 'RightFoot': 'RightFoot', 'RightToes': 'RightToeBase'}


# ---------------------------------------------------------------------------------------------- small rotation helpers (matrices)
def q2m(x, y, z, w):
    n = math.sqrt(x * x + y * y + z * z + w * w); x, y, z, w = x / n, y / n, z / n, w / n
    return np.array([[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
                     [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
                     [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]])


def m2q(m):
    """-> (x, y, z, w), w >= 0"""
    t = m[0, 0] + m[1, 1] + m[2, 2]
    if t > 0:
        s = math.sqrt(t + 1.0) * 2; q = ((m[2, 1] - m[1, 2]) / s, (m[0, 2] - m[2, 0]) / s, (m[1, 0] - m[0, 1]) / s, .25 * s)
    elif m[0, 0] > m[1, 1] and m[0, 0] > m[2, 2]:
        s = math.sqrt(1.0 + m[0, 0] - m[1, 1] - m[2, 2]) * 2; q = (.25 * s, (m[0, 1] + m[1, 0]) / s, (m[0, 2] + m[2, 0]) / s, (m[2, 1] - m[1, 2]) / s)
    elif m[1, 1] > m[2, 2]:
        s = math.sqrt(1.0 + m[1, 1] - m[0, 0] - m[2, 2]) * 2; q = ((m[0, 1] + m[1, 0]) / s, .25 * s, (m[1, 2] + m[2, 1]) / s, (m[0, 2] - m[2, 0]) / s)
    else:
        s = math.sqrt(1.0 + m[2, 2] - m[0, 0] - m[1, 1]) * 2; q = ((m[0, 2] + m[2, 0]) / s, (m[1, 2] + m[2, 1]) / s, .25 * s, (m[1, 0] - m[0, 1]) / s)
    q = np.array(q); q /= np.linalg.norm(q)
    return q if q[3] >= 0 else -q


def unit(v):
    v = np.asarray(v, dtype=float); return v / (np.linalg.norm(v) + 1e-15)


def axis_angle(axis, deg):
    a = unit(axis); h = math.radians(deg) / 2; s = math.sin(h)
    return q2m(a[0] * s, a[1] * s, a[2] * s, math.cos(h))


def arc(a, b):
    """shortest rotation taking direction a onto direction b (Quaternion.FromToRotation)"""
    a = unit(a); b = unit(b); c = float(np.dot(a, b))
    if c > 1 - 1e-12: return np.eye(3)
    if c < -1 + 1e-12:
        o = unit(np.cross(a, [1., 0, 0]) if abs(a[0]) < .9 else np.cross(a, [0., 1, 0])); return axis_angle(o, 180)
    return axis_angle(np.cross(a, b), math.degrees(math.acos(max(-1., min(1., c)))))


def angle(a, b):
    return math.degrees(math.acos(max(-1., min(1., float(np.dot(unit(a), unit(b)))))))


def rot_angle(m):
    return math.degrees(math.acos(max(-1., min(1., (np.trace(m) - 1) / 2))))


def ortho(m):
    u, _, vt = np.linalg.svd(m); r = u @ vt
    if np.linalg.det(r) < 0: u[:, -1] *= -1; r = u @ vt
    return r


def swing_twist(m):
    """m = Swing(y, z) @ Twist(x)  ->  (tx, ty, tz) as tan(half angle) components (Mecanim quat2ZYRoll)"""
    x, y, z, w = m2q(m)
    if abs(w) < 1e-9: w = 1e-9
    qx, qy, qz = x / w, y / w, z / w; d = 1 + qx * qx
    return qx, (qy - qx * qz) / d, (qz + qx * qy) / d


def from_swing_twist(tx, ty, tz):
    """Mecanim ZYRoll2Quat"""
    return q2m(tx, ty + tx * tz, tz - tx * ty, 1.0)


# ---------------------------------------------------------------------------------------------- avatar reference pose (.meta)
def read_meta(path):
    """-> rows of humanDescription.skeleton: name -> (position xyz, rotation matrix, scale xyz), in file order; plus raw text"""
    text = open(path, encoding='utf-8').read()
    block = text[text.index('    skeleton:\n'):text.index('    armTwist:')]
    rows = {}; order = []
    for m in re.finditer(r"    - name: (.*)\n      parentName: (.*)\n      position: \{x: (\S+), y: (\S+), z: (\S+)\}\n      rotation: \{x: (\S+), y: (\S+), z: (\S+), w: (\S+)\}\n      scale: \{x: (\S+), y: (\S+), z: (\S+)\}\n", block):
        g = m.groups(); name = g[0].strip()
        rows[name] = {'p': np.array([float(g[2]), float(g[3]), float(g[4])]), 'q': np.array([float(g[5]), float(g[6]), float(g[7]), float(g[8])]),
                      's': np.array([float(g[9]), float(g[10]), float(g[11])])}
        order.append(name)
    return rows, order, text


class Avatar:
    """Global reference pose of the avatar skeleton in avatar (Unity model) space."""
    def __init__(self, rows, bones, parents):
        self.bones = list(bones); self.parents = list(parents); self.rows = rows
        root = np.eye(3); rp = np.zeros(3)
        arm = rows['Armature']; self.armR = q2m(*arm['q']); self.armP = arm['p'].copy()
        self.L = [q2m(*rows[b]['q']) for b in self.bones]; self.lp = [rows[b]['p'].copy() for b in self.bones]
        self.G, self.P = self.fk(self.L)

    def fk(self, L, hips_pos=None):
        G = [None] * len(self.bones); P = [None] * len(self.bones)
        for i, b in enumerate(self.bones):
            pa = self.parents[i]
            if pa < 0:
                G[i] = self.armR @ L[i]; P[i] = self.armP + self.armR @ (self.lp[i] if hips_pos is None else hips_pos)
            else:
                G[i] = G[pa] @ L[i]; P[i] = P[pa] + G[pa] @ self.lp[i]
        return G, P

    def idx(self, human): return self.bones.index(HUMAN[human])


# ---------------------------------------------------------------------------------------------- Unity's joint frames [O]
# Recovered from the three live avatars (avatarblob308.read_axes on the import artifacts): for every human bone
#     inner frame F (avatar space, reference pose) = look-at: x = the bone's own direction, z = a fixed avatar-space axis made
#                                                    perpendicular to x, y = z cross x        (feet, toes, head: a fixed frame)
#     outer frame C = Qw @ F,   Qw = one fixed AVATAR-SPACE rotation per bone type (the muscle-zero offset of a T-pose)
#     PostQ = boneGlobalRef^T @ F,   PreQ = parentGlobalRef^T @ C
# The three avatars agree on Qw to 0.0000 deg. Qw only means "elbow 80 deg forward" when the limb really lies along the T-pose axes.
SECONDARY = {'Shoulder': (0, 0, 1), 'UpperArm': (0, 0, 1), 'Hand': (0, 0, 1), 'LowerArm': (0, 1, 0), 'UpperLeg': (1, 0, 0), 'LowerLeg': (1, 0, 0)}
FIXED = {'Foot': ((0, -1, 0), (0, 0, -1), (1, 0, 0)), 'Toes': ((0, 0, 1), (0, -1, 0), (1, 0, 0)), 'Head': ((0, 1, 0), (0, 0, -1), (-1, 0, 0))}
AXIS_CHILD = {'LeftShoulder': 'LeftUpperArm', 'LeftUpperArm': 'LeftLowerArm', 'LeftLowerArm': 'LeftHand', 'RightShoulder': 'RightUpperArm',
              'RightUpperArm': 'RightLowerArm', 'RightLowerArm': 'RightHand', 'LeftUpperLeg': 'LeftLowerLeg', 'LeftLowerLeg': 'LeftFoot',
              'RightUpperLeg': 'RightLowerLeg', 'RightLowerLeg': 'RightFoot', 'Spine': 'Chest', 'Chest': 'UpperChest', 'UpperChest': 'Neck', 'Neck': 'Head'}
AXIS_PARENT = {'LeftHand': 'LeftLowerArm', 'RightHand': 'RightLowerArm'}
ZERO_OFFSET = {'UpperLeg': ((-1, 0, 0), 30.005), 'LowerLeg': ((1, 0, 0), 79.993), 'UpperArm': ((0, 0.5929, 0.8053), 48.648), 'LowerArm': ((0, 1, 0), 79.993)}


def kind(human):
    for k in ('Shoulder', 'UpperArm', 'LowerArm', 'Hand', 'UpperLeg', 'LowerLeg', 'Foot', 'Toes', 'Head'):
        if human.endswith(k): return k
    return 'Spine'


def unity_frames(avatar):
    """-> human -> {'F','C','pre','post'} built by the recovered rule from an avatar reference pose (Avatar object)."""
    out = {}
    for human in HUMAN:
        if human == 'Hips': continue
        i = avatar.idx(human); pa = avatar.parents[i]; k = kind(human); right = human.startswith('Right')
        if k in FIXED: F = np.array(FIXED[k], dtype=float).T
        else:
            if human in AXIS_PARENT: d = avatar.P[i] - avatar.P[avatar.idx(AXIS_PARENT[human])]
            else: d = avatar.P[avatar.idx(AXIS_CHILD[human])] - avatar.P[i]
            x = unit(d); sec = np.array(SECONDARY.get(k, (-1, 0, 0)), dtype=float); z = unit(sec - float(sec @ x) * x); F = np.stack([x, np.cross(z, x), z], 1)
        Q = np.eye(3)
        if k in ZERO_OFFSET:
            ax, ang = ZERO_OFFSET[k]
            Q = axis_angle(np.array(ax, dtype=float) * (-1.0 if right and k in ('UpperArm', 'LowerArm') else 1.0), ang)
        C = Q @ F
        out[human] = {'F': F, 'C': C, 'pre': avatar.G[pa].T @ C, 'post': avatar.G[i].T @ F}
    return out


def mid_floor(avatar, frames, parent_human, child_human):
    """How far the parent limb lies outside the plane its child can sweep with the one bend muscle it has (deg): the smallest
    elbow / knee angle the avatar can show. 0 for a T-pose."""
    i = avatar.idx(child_human); a = unit(avatar.lp[i])            # parent bone direction in the parent's own frame
    return math.degrees(math.asin(min(1.0, abs(float(a @ frames[child_human]['pre'][:, 2])))))


def human_order(dump):
    """Human-skeleton node order of the Avatar (children sorted by name, the importer's sortHierarchyByName)."""
    def walk(i, out):
        out.append(i)
        for k in sorted([k for k in range(len(dump.bones)) if dump.parents[k] == i], key=lambda k: dump.bones[k]): walk(k, out)
    seq = []; walk(dump.bones.index('Hips'), seq); human = set(HUMAN.values()); return [i for i in seq if dump.bones[i] in human]


# ---------------------------------------------------------------------------------------------- clip poses from the Blender dump
class Dump:
    def __init__(self, path):
        z = np.load(path, allow_pickle=False); self.z = z
        self.bones = [str(x) for x in z['bones']]; self.parents = [int(x) for x in z['parents']]
        self.clips = [k[5:] for k in z.files if k.startswith('pose_')]

    def rest(self, src='rig'): return self.z['rest_' + src]

    def unity_globals(self, clip, src):
        """-> list over frames of (G[b] rotation matrices in avatar space RELATIVE delta from the source rest, hips position in avatar space)"""
        pose = self.z['pose_' + clip]; rest = self.z['rest_' + src]; out = []
        Rr = [ortho(rest[b][:3, :3]) for b in range(len(self.bones))]
        for f in range(pose.shape[0]):
            D = []
            for b in range(len(self.bones)):
                D.append(P_BU @ (ortho(pose[f, b][:3, :3]) @ Rr[b].T) @ P_BU.T)
            out.append((D, P_BU @ pose[f, self.bones.index('Hips')][:3, 3]))
        return out

    def rest_positions_unity(self, src='rig'):
        return [P_BU @ self.z['rest_' + src][b][:3, 3] for b in range(len(self.bones))]


SRC_OF = {'walk': 'walk', 'walkfix': 'fixed', 'run': 'run', 'idle': 'motion', 'attack': 'motion', 'hit': 'motion', 'stun': 'motion', 'death': 'motion'}


def clip_locals(dump, avatar_live, clip):
    """Unity local rotations per frame of a clip, as the clip FBX carries them: world delta from the FBX rest applied on the LIVE
    reference globals (the rig's default pose = what the .meta rows hold), then made local. Also the hips position per frame."""
    frames = dump.unity_globals(clip, SRC_OF[clip]); out = []
    for D, hips in frames:
        G = [D[b] @ avatar_live.G[b] for b in range(len(dump.bones))]
        L = [(G[b] if dump.parents[b] < 0 else G[dump.parents[b]].T @ G[b]) for b in range(len(dump.bones))]
        L[dump.bones.index('Hips')] = avatar_live.armR.T @ G[dump.bones.index('Hips')]
        out.append((L, avatar_live.armR.T @ (hips - avatar_live.armP)))
    return out


def arm_measures(P, avatar):
    """abduction / swing / elbow of both arms from joint positions (same definitions as EnemyRigVerify308.Capture and rig308.frame)"""
    out = {}
    for s, side in enumerate(('Left', 'Right')):
        U = P[avatar.idx(side + 'UpperArm')]; E = P[avatar.idx(side + 'LowerArm')]; H = P[avatar.idx(side + 'Hand')]
        ua = unit(E - U); fa = unit(H - E); outward = -1.0 if s == 0 else 1.0
        out[side] = {'abduction': math.degrees(math.atan2(ua[0] * outward, -ua[1])), 'swing': math.degrees(math.atan2(ua[2], -ua[1])), 'elbow': angle(ua, fa),
                     'ua': ua, 'fa': fa, 'wrist': H, 'elbow_pos': E, 'shoulder_pos': U}
    return out


def load_monster(repo, cfg, mid):
    m = cfg['monsters'][mid]
    dump = Dump(os.path.join(repo, cfg['work'], mid + '_dump.npz'))
    rows, order, text = read_meta(os.path.join(repo, m['rig'] + '.meta'))
    return dump, Avatar(rows, dump.bones, dump.parents), rows, order, text
