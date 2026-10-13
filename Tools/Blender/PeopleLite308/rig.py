"""#308 PeopleLite: Unity Generic .anim 을 Blender 뼈대에 그대로 얹는다(검증 · 굽기 · 미리보기용).
Unity 는 FBX 를 X 뒤집어 읽는다: 위치 (x,y,z)->(-x,y,z) · 쿼터니언 (x,y,z,w)->(x,-y,-z,w). 파일 단위 cm -> 위치 x100.
Blender 가져오기(기본 뼈 축)에서 뼈 matrix_local == FBX 노드 전역 행렬이므로 부모 기준 행렬을 그대로 쓸 수 있다.
뿌리(b_Root / b_Bip_001 = 뼈대 오브젝트)는 Idle 0 프레임을 가져온 자세로 보고 그 대비 변화만 얹는다."""
import os, sys
import bpy
from mathutils import Matrix, Quaternion, Vector
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import anim308

UNIT = 100.0


def conv(pos, rot, scl=None):
    q = Quaternion((rot[3], rot[0], -rot[1], -rot[2])) if rot else Quaternion()
    m = q.to_matrix().to_4x4()
    if scl: m = m @ Matrix.Diagonal((scl[0], scl[1], scl[2], 1.0))
    if pos: m.translation = Vector((-pos[0] * UNIT, pos[1] * UNIT, pos[2] * UNIT))
    return m


class Clip:
    def __init__(self, path, root):
        self.a = anim308.parse(path); self.root = root; self.length = self.a['length']; self.name = self.a['name']

    def local(self, path, t):
        c = self.a['curves'].get(path)
        if c is None: return None
        return conv(anim308.sample(c.get('pos'), t), anim308.sample(c.get('rot'), t, True), anim308.sample(c.get('scl'), t))


def bone_path(bone, root):
    names = []
    while bone: names.append(bone.name); bone = bone.parent
    return root + '/' + '/'.join(reversed(names))


def rest_local(bone):
    return bone.parent.matrix_local.inverted() @ bone.matrix_local if bone.parent else bone.matrix_local.copy()


def pose(arm, clip, t, rest_world, root_ref):
    """arm 의 모든 뼈를 clip 의 t 초 자세로. rest_world = 가져온 직후의 arm.matrix_world, root_ref = 뿌리의 기준 Unity 로컬(Idle 0) 변환 행렬."""
    missing = 0
    for pb in arm.pose.bones:
        L = clip.local(bone_path(pb.bone, clip.root), t)
        if L is None: pb.matrix_basis = Matrix.Identity(4); missing += 1; continue
        pb.matrix_basis = rest_local(pb.bone).inverted() @ L
    R = clip.local(clip.root, t)
    arm.matrix_world = rest_world @ (root_ref.inverted() @ R) if R is not None and root_ref is not None else rest_world.copy()
    return missing


def reset(arm, rest_world):
    for pb in arm.pose.bones: pb.matrix_basis = Matrix.Identity(4)
    arm.matrix_world = rest_world.copy()


def posed_world(obj):
    """평가된(뼈대 변형 뒤) 정점의 월드 좌표 numpy (n,3)."""
    import numpy as np
    dg = bpy.context.evaluated_depsgraph_get(); dg.update()
    ev = obj.evaluated_get(dg); me = ev.data
    co = np.empty(len(me.vertices) * 3, dtype=np.float64); me.vertices.foreach_get('co', co); co = co.reshape(-1, 3)
    M = np.array(ev.matrix_world, dtype=np.float64)
    return co @ M[:3, :3].T + M[:3, 3]


def import_fbx(path):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=path, use_anim=False, ignore_leaf_bones=False, automatic_bone_orientation=False)
    new = [o for o in bpy.data.objects if o not in before]
    arm = next(o for o in new if o.type == 'ARMATURE'); meshes = [o for o in new if o.type == 'MESH']
    return arm, meshes, new
