"""#308 enemy rig revision 2 (SPEC-ENEMY-RIG-VERIFY-308 '개정 2'): dump what the offline humanoid model needs.
Blender headless, CPU only, NO render, read only (source FBX under Assets and the two stages are only read).

  B="C:/Program Files/Blender Foundation/Blender 5.0/blender.exe"
  python Tools/resource_guard.py --wait
  "$B" -b --factory-startup -t 4 --python Tools/Blender/EnemyAvatar308/dump308.py -- <id>

Writes Art/Characters308/EnemyRigVerify2/work/<id>_dump.npz:
  bones, parents                     armature bone names and parent index (-1 = root)
  rest_<src>            (B,4,4)      world matrix of every bone at the FBX rest pose, per source file (rig / walk / motion / fixed / run)
  pose_<clip>           (F,B,4,4)    world matrix of every pose bone per frame, scale and non-root translation stripped (what a humanoid
                                     clip keeps: rotations + the hips position)
  frames_<clip>         (F,)         Blender frame numbers (Unity frame index = Blender frame - 1)
  V0 (N,3), tris (T,3), W (N,B)      rig mesh at rest (world), triangles, skin weights by bone index (0 where a bone has no group)
Blender axes: +Z up, the character faces -Y, its LEFT is +X."""
import bpy, sys, os, json
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, '..', '..', '..')).replace('\\', '/')
CFG = json.load(open(os.path.join(HERE, 'avatar308.json'), encoding='utf-8'))
ARGS = sys.argv[sys.argv.index('--') + 1:]
OUT = REPO + '/' + CFG['work']
os.makedirs(OUT, exist_ok=True)


def load(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path, automatic_bone_orientation=False, use_anim=True, ignore_leaf_bones=False)
    arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
    mesh = next((o for o in bpy.data.objects if o.type == 'MESH'), None)
    return arm, mesh


def set_action(arm, act):
    ad = arm.animation_data or arm.animation_data_create()
    ad.action = act
    try:
        if act is not None and len(act.slots): ad.action_slot = act.slots[0]
    except Exception as ex:
        print('slot', ex)


def rest_world(arm, names):
    MW = arm.matrix_world
    return np.array([np.array(MW @ arm.data.bones[n].matrix_local) for n in names])


def sample(arm, act, names):
    set_action(arm, act); sc = bpy.context.scene; MW = arm.matrix_world
    f0, f1 = int(round(act.frame_range[0])), int(round(act.frame_range[1])); out = []; frames = []
    for f in range(f0, f1 + 1):
        sc.frame_set(f)
        for pb in arm.pose.bones:
            pb.scale = (1, 1, 1)
            if pb.name != 'Hips': pb.location = (0, 0, 0)
        bpy.context.view_layer.update()
        out.append([np.array(MW @ arm.pose.bones[n].matrix) for n in names]); frames.append(f)
    return np.array(out), np.array(frames)


def role_of(name, attack):
    for cand in name.split('|'):
        r = {'Idle': 'idle', attack: 'attack', 'Hit_Reaction': 'hit', 'Hit_Reaction_1': 'stun', 'Dead': 'death'}.get(cand)
        if r: return r
    return None


def main(mid):
    m = CFG['monsters'][mid]; data = {}
    arm, mesh = load(REPO + '/' + m['rig'])
    names = [b.name for b in arm.data.bones]; parents = [names.index(b.parent.name) if b.parent else -1 for b in arm.data.bones]
    data['bones'] = np.array(names); data['parents'] = np.array(parents); data['rest_rig'] = rest_world(arm, names)
    data['arm_world'] = np.array(arm.matrix_world)
    # rig mesh at rest
    set_action(arm, None)
    for pb in arm.pose.bones: pb.location = (0, 0, 0); pb.rotation_quaternion = (1, 0, 0, 0); pb.scale = (1, 1, 1)
    bpy.context.view_layer.update()
    dg = bpy.context.evaluated_depsgraph_get(); ev = mesh.evaluated_get(dg); me = ev.to_mesh()
    co = np.empty(len(me.vertices) * 3); me.vertices.foreach_get('co', co); ev.to_mesh_clear()
    M = np.array(mesh.matrix_world); data['V0'] = co.reshape(-1, 3) @ M[:3, :3].T + M[:3, 3]
    me = mesh.data; me.calc_loop_triangles(); data['tris'] = np.array([t.vertices[:] for t in me.loop_triangles], dtype=np.int32)
    W = np.zeros((len(me.vertices), len(names)), dtype=np.float32); gname = [g.name for g in mesh.vertex_groups]
    for v in me.vertices:
        for g in v.groups:
            if gname[g.group] in names: W[v.index, names.index(gname[g.group])] = g.weight
    data['W'] = W
    print('DUMP', mid, 'rig bones', len(names), 'verts', len(me.vertices), 'tris', len(data['tris']), 'armature scale', list(arm.scale), flush=True)
    for src in ('walk', 'motion', 'fixed', 'run'):
        rel = m.get(src)
        if not rel: continue
        path = REPO + '/' + rel
        if not os.path.exists(path): print('DUMP', mid, src, 'MISSING', path, flush=True); continue
        arm, mesh = load(path)
        got = [b.name for b in arm.data.bones]
        if got != names: raise SystemExit('bone list differs in %s: %s' % (src, sorted(set(got) ^ set(names))))
        data['rest_' + src] = rest_world(arm, names)
        for act in bpy.data.actions:
            clip = {'walk': 'walk', 'fixed': 'walkfix', 'run': 'run'}.get(src) or role_of(act.name, m['attack'])
            if clip is None: print('DUMP', mid, src, 'skipped action', act.name, flush=True); continue
            P, F = sample(arm, act, names); data['pose_' + clip] = P; data['frames_' + clip] = F
            data['fps_' + clip] = np.array(bpy.context.scene.render.fps); data['action_' + clip] = np.array(act.name)
            print('DUMP', mid, src, clip, repr(act.name), 'frames', len(F), 'fps', bpy.context.scene.render.fps, flush=True)
    np.savez_compressed(OUT + '/%s_dump.npz' % mid, **data)
    print('DUMP DONE', mid, OUT + '/%s_dump.npz' % mid)


main(ARGS[0])
