"""Quadruped308 retarget: free Asset Store quadruped motion -> Folklore298 Rig298 (SPEC-ANIM-QUADRUPED-308, D308-7).

Our model, weights and bone names stay exactly as authored by Tools/MeshyRuns/Folklore298/rig_creatures.py.
Only motion is moved: the source skeleton is sampled in WORLD space and re-expressed on the target rig.

Headless Blender 5 only (one process at a time, -t 4; the PC can blue-screen under load):
  blender -b --factory-startup -t 4 --python retarget.py -- inspect-source --pack wolf311465 [--filter Attack]
  blender -b --factory-startup -t 4 --python retarget.py -- build --species bulgasari [--roles Idle,Walk]
  blender -b --factory-startup -t 4 --python retarget.py -- render --species bulgasari
  blender -b --factory-startup -t 4 --python retarget.py -- roundtrip --species bulgasari
  blender -b --factory-startup -t 4 --python retarget.py -- probe --species bulgasari --action DEBUG_Death
  python retarget.py self-test                       (pure maths + recipe checks, no Blender)
Recipes: maps/<species>_roles.json (role -> source clip and options); bone maps: maps/<source>_to_<species>.json.

Method (build):
  * axis chain (pelvis -> spine -> chest -> neck -> head) and tails: per-frame WORLD rotation difference of the source
    bones against the source reference pose (the calm idle), resampled along normalised arc length onto the target
    chain; body (pelvis), chain and tail amplitudes are separate (chain/tail relative to the pelvis).
  * legs: the source paw point is expressed as a displacement from its reference stance, normalised by standing leg length,
    and solved on the target with a two-bone IK that keeps the rest knee plane (rig_creatures.solve_leg geometry).
    Planted paws stay inside the Idle sole band (never below the rest sole) and never slide; the body yields instead:
    a bounded drop (Rig298 legs stand ~98% extended) and a horizontal pelvis shift (body follows planted feet).
    Lifted paws blend to a body-relative path (leg angle relative to the shoulder/hip, bounded fold).
  * root: source pelvis XZ drift is removed (loops: linear drift through the cycle mean; one-shots: first frame), the
    remaining pelvis offset is baked into the Pelvis bone; the Root bone only ever receives a vertical lift that keeps
    every skinned vertex above the ground (no horizontal Root translation: runtime ownership contract). A tail that meets
    the ground bends up about its base instead of lifting the body.
  * options per role: mirror (reflect the source across the midplane - bulgasari's left armpit is the weak skin),
    centerStride, autoBodyDrop, fullCollapse, minLegExtension, boneAmplitude, tailGroundKeep (see the recipe notes).
  * loops end on an exact copy of their first key; residual source seam is distributed over the cycle.
  * gates: rig_creatures.validate_deformation (stretch <= 2.5, compression >= .15, ground <= .008, sole spread <= .015
    except Death, four influences, loop seam <= .0001, root XZ = 0), IK error on planted paws <= .008 m, Root lift <= .008
    (no floating paws), Death must lower the pelvis by >= 25% (AC-Q5). Reported, not gated: stance-phase slip (AC-Q3
    predictor), attack peak, walk speed. A failing role retries down the [amplitude, stride] ladder; if every rung fails
    it is NOT exported (kept as DEBUG_<role> in the work blend for `probe`) and Unity keeps its procedural clip.
  * export: <species>_RT308.fbx = mesh + rig + RT308_<Role> actions only, with the creature.fbx export settings, then
    rig_creatures.roundtrip and a bone rest-frame comparison against creature.fbx (Unity curves are absolute local TRS).
    candidate.blend is opened read-only and never saved (its SHA256 is checked); the work blend is <species>_RT308.blend.

Source packs are Standard Unity Asset Store EULA files: read from Art/Characters/Quadruped308/Packages (gitignored);
nothing from them is copied into Assets or git. Only derived motion on our own rig leaves this tool.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import sys
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
MAPS = HERE / "maps"
PACKS = ROOT / "Art/Characters/Quadruped308/Packages"
OUT = ROOT / "Art/Characters/Quadruped308"
FOLKLORE = ROOT / "Art/Characters/Folklore298"
RIG_TOOL = ROOT / "Tools/MeshyRuns/Folklore298"
SPECIES = ("bulgasari", "fox_spirit")
ROLES = ("Idle", "Walk", "Attack", "Hit", "Stun", "Death")
LEGS = ("Fore_L", "Fore_R", "Hind_L", "Hind_R")
VERSION = 1
TARGET_FPS = 30
UP = (0.0, 0.0, 1.0)


# ----------------------------------------------------------------------------------------------- generic helpers

def require(condition, message):
    if not condition:
        raise ValueError(message)


def read_json(path):
    return json.loads(Path(path).read_text(encoding="utf-8-sig"))


def save_json(path, value):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2, allow_nan=False) + "\n", encoding="utf-8")


def sha(path):
    digest = hashlib.sha256()
    with Path(path).open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def rel(path):
    try:
        return Path(path).resolve().relative_to(ROOT).as_posix()
    except ValueError:
        return str(path)


def no_assets(path):
    path = Path(path).resolve()
    require("assets" not in [part.lower() for part in path.parts], "Outputs stay outside Unity Assets: " + str(path))
    return path


def lerp(a, b, t):
    return a + (b - a) * t


def smoothstep(e0, e1, x):
    if e1 <= e0:
        return 1.0 if x >= e1 else 0.0
    t = max(0.0, min(1.0, (x - e0) / (e1 - e0)))
    return t * t * (3 - 2 * t)


def arc_params(points):
    """Normalised cumulative arc length of a polyline (list of 3-tuples)."""
    lengths = [math.dist(a, b) for a, b in zip(points, points[1:])]
    total = sum(lengths)
    require(total > 1e-9, "Degenerate chain")
    out, acc = [0.0], 0.0
    for length in lengths:
        acc += length
        out.append(acc / total)
    return out


def arc_lookup(params, s):
    """Segment index i and fraction f such that s lies on segment i (params are segment boundaries)."""
    s = max(0.0, min(1.0, s))
    for i in range(len(params) - 1):
        if s <= params[i + 1] or i == len(params) - 2:
            span = params[i + 1] - params[i]
            return i, (0.0 if span <= 1e-12 else (s - params[i]) / span)
    return len(params) - 2, 1.0


def load_maps(species):
    roles = read_json(MAPS / f"{species}_roles.json")
    require(roles.get("schema") == VERSION and roles.get("target") == species, "Role recipe schema/target mismatch")
    maps = {}
    for key in sorted({row["map"] for row in roles["roles"].values()} | {row["map"] for row in roles.get("extras", {}).values()}):
        value = read_json(MAPS / f"{key}.json")
        require(value.get("schema") == VERSION and value.get("target") == species, "Bone map schema/target mismatch: " + key)
        maps[key] = value
    return roles, maps


def manifest_row(species):
    rows = read_json(FOLKLORE / "import-manifest.json")["rows"]
    row = next((r for r in rows if r["id"] == species), None)
    require(row is not None, "Folklore298 manifest has no row " + species)
    return row


def target_paths(species):
    row = manifest_row(species)
    model = ROOT / row["model"]
    run = model.parent
    return {"row": row, "run": run, "candidate": run / "candidate.blend", "creature": model,
            "config": run / "config-used.json", "rigReport": run / "rig-report.json"}


def species_out(species):
    return no_assets(OUT / "Retarget" / species)


# ----------------------------------------------------------------------------------------------- blender helpers

def blender():
    try:
        import bpy
        from mathutils import Matrix, Vector, Quaternion
    except ImportError as error:
        raise RuntimeError("This command needs Blender: blender -b --factory-startup -t 4 --python retarget.py -- ...") from error
    return bpy, Matrix, Vector, Quaternion


def rig_tool():
    if str(RIG_TOOL) not in sys.path:
        sys.path.insert(0, str(RIG_TOOL))
    import rig_creatures
    return rig_creatures


def assign(obj, action):
    obj.animation_data_create()
    obj.animation_data.action = action
    if action is not None and hasattr(action, "slots"):
        slot = next(iter(action.slots), None) or action.slots.new(id_type="OBJECT", name=obj.name)
        obj.animation_data.action_slot = slot


def frame_at(scene, frame):
    scene.frame_set(int(math.floor(frame)), subframe=frame - math.floor(frame))


def import_fbx(path, keep_meshes=False):
    """Fresh scene with one FBX. Source meshes are dropped (in memory only) so pose sampling stays light."""
    bpy, _, _, _ = blender()
    require(bpy.app.background, "Run Blender with -b")
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(path), use_anim=True)
    scene = bpy.context.scene
    fps = scene.render.fps / scene.render.fps_base
    if not keep_meshes:
        for obj in [o for o in scene.objects if o.type == "MESH"]:
            bpy.data.objects.remove(obj, do_unlink=True)
    rigs = [o for o in scene.objects if o.type == "ARMATURE"]
    require(len(rigs) == 1, f"{Path(path).name}: expected one armature, found {len(rigs)}")
    return rigs[0], fps


def find_action(name_part):
    bpy, _, _, _ = blender()
    hits = [a for a in bpy.data.actions if a.name == name_part or a.name.endswith("|" + name_part)
            or ("|" + name_part + "|") in a.name]
    require(len(hits) == 1, f"Action '{name_part}' not unique: {[a.name for a in bpy.data.actions]}")
    return hits[0]


def pose_world(rig, names):
    """World 4x4 of each named pose bone at the current evaluated frame."""
    return {name: rig.matrix_world @ rig.pose.bones[name].matrix for name in names}


def bone_depth(bone):
    depth = 0
    while bone.parent:
        depth += 1
        bone = bone.parent
    return depth


# ----------------------------------------------------------------------------------------------- inspect-source

PACK_FILES = {
    "bear228910": "bear228910/Blink/Art/Animals/Stylized/Bear",
    "wolf311465": "wolf311465/Wolf",
    "toonfox183005": "toonfox183005/Fox",
    "wolfboss189463": "wolfboss189463/Hatogame_new/BossMonsterPack1/Wolfboss",
    "tiger55797": "tiger55797/Tiger",
    "goat251910": "goat251910/UrsaAnimation",
    "turtle95526": "turtle95526/ToonyTurtle",
}


def inspect_source(args):
    bpy, _, Vector, _ = blender()
    require(args.pack in PACK_FILES, "Unknown pack " + args.pack)
    base = PACKS / PACK_FILES[args.pack]
    require(base.is_dir(), "Pack folder missing: " + str(base))
    files = sorted(p for p in base.rglob("*") if p.suffix.lower() == ".fbx")
    if args.filter:
        files = [p for p in files if args.filter.lower() in p.name.lower()]
    require(files, "No FBX in " + str(base))
    report = {"schema": VERSION, "pack": args.pack, "utc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
              "blender": bpy.app.version_string, "note": "World metres (+Z up). Rest = this file's armature rest; "
              "animation-only FBX rest may equal its first frame. Feet = bones whose rest head is in the lowest 12% of the rig.",
              "files": []}
    for path in files:
        row = {"file": path.relative_to(PACKS).as_posix(), "bytes": path.stat().st_size, "sha256": sha(path)}
        try:
            bpy.ops.wm.read_factory_settings(use_empty=True)
            bpy.ops.import_scene.fbx(filepath=str(path), use_anim=True)
        except Exception as error:  # report, keep inspecting the rest of the pack
            row["error"] = str(error)
            report["files"].append(row)
            continue
        scene = bpy.context.scene
        fps = scene.render.fps / scene.render.fps_base
        meshes = [o for o in scene.objects if o.type == "MESH"]
        rigs = [o for o in scene.objects if o.type == "ARMATURE"]
        row.update(fps=fps, meshes=[{"name": m.name, "vertices": len(m.data.vertices)} for m in meshes],
                   armatures=[r.name for r in rigs], actions=[])
        for obj in meshes:
            bpy.data.objects.remove(obj, do_unlink=True)
        if not rigs:
            report["files"].append(row)
            continue
        rig = rigs[0]
        bones = list(rig.data.bones)
        heads = {b.name: rig.matrix_world @ b.head_local for b in bones}
        tails = {b.name: rig.matrix_world @ b.tail_local for b in bones}
        zmin = min(h.z for h in heads.values())
        zmax = max(h.z for h in heads.values())
        feet = [b.name for b in bones if heads[b.name].z <= zmin + .12 * (zmax - zmin) and not b.children]
        feet_parents = sorted({rig.data.bones[n].parent.name for n in feet if rig.data.bones[n].parent})
        row["rig"] = {"object": rig.name, "objectScale": list(rig.matrix_world.to_scale()),
                      "restHeightM": zmax - zmin, "lowestBoneZ": zmin,
                      "bones": [{"name": b.name, "parent": b.parent.name if b.parent else None, "depth": bone_depth(b),
                                 "head": [round(c, 5) for c in heads[b.name]], "tail": [round(c, 5) for c in tails[b.name]],
                                 "deform": b.use_deform} for b in bones],
                      "lowLeafBones": feet, "lowLeafParents": feet_parents}
        top = [b.name for b in bones if b.parent is None]
        watch = sorted(set(top) | set(feet) | set(feet_parents))
        for action in bpy.data.actions:
            assign(rig, action)
            start, end = action.frame_range
            count = max(2, min(160, int(round(end - start)) + 1))
            frames = [start + (end - start) * i / (count - 1) for i in range(count)]
            samples = []
            for frame in frames:
                frame_at(scene, frame)
                samples.append({name: (rig.matrix_world @ rig.pose.bones[name].matrix).translation.copy() for name in watch})
            first, last = samples[0], samples[-1]
            seam = max((first[n] - last[n]).length for n in watch)
            stats = {}
            for name in watch:
                points = [s[name] for s in samples]
                stats[name] = {"min": [round(min(p[i] for p in points), 4) for i in range(3)],
                               "max": [round(max(p[i] for p in points), 4) for i in range(3)],
                               "startToEnd": [round(c, 4) for c in (points[-1] - points[0])]}
            pelvis = top[0] if top else None
            drift = (last[pelvis] - first[pelvis]) if pelvis else Vector()
            row["actions"].append({"name": action.name, "frames": [start, end], "seconds": (end - start) / fps,
                                   "seamM": round(seam, 5), "topBone": pelvis,
                                   "topDriftXY": [round(drift.x, 4), round(drift.y, 4)], "bones": stats})
        assign(rig, None)
        report["files"].append(row)
        print(f"INSPECT {row['file']} fps={fps} actions={[a['name'] for a in row['actions']]}", flush=True)
    out = no_assets(OUT / "reports" / "inspect")
    save_json(out / f"{args.pack}.json", report)
    return {"status": "INSPECTED", "pack": args.pack, "files": len(report["files"]), "output": rel(out / f"{args.pack}.json")}




# ----------------------------------------------------------------------------------------------- source sampling

def pack_path(relative):
    path = (PACKS / relative).resolve()
    require(PACKS.resolve() in path.parents, "Source must stay inside Packages: " + relative)
    require(path.is_file(), "Missing source " + relative)
    return path


def map_bones(bone_map):
    names = set(bone_map["axisChain"]["source"]) | {bone_map["pelvis"]}
    if bone_map.get("tail"):
        names |= set(bone_map["tail"]["source"])
    for leg in bone_map["legs"].values():
        names |= {leg["hip"], leg["foot"], leg["parent"]}
    return sorted(names)


def sample_frames(rig, action, bones, start, end, tails=False):
    """World (loc, rot[, tail]) of each bone at every integer frame start..end inclusive."""
    bpy, _, _, _ = blender()
    scene = bpy.context.scene
    assign(rig, action)
    rows = []
    for i in range(int(round(end - start)) + 1):
        frame_at(scene, start + i)
        row = {}
        for name in bones:
            pose = rig.pose.bones[name]
            loc, rot, _ = (rig.matrix_world @ pose.matrix).decompose()
            row[name] = (loc.copy(), rot.copy(), (rig.matrix_world @ pose.tail).copy()) if tails else (loc.copy(), rot.copy())
        rows.append(row)
    assign(rig, None)
    return rows


def recipe_row(recipe, role):
    row = recipe["roles"].get(role) or recipe.get("extras", {}).get(role)
    require(row is not None, "Recipe has no role " + role)
    return row


def gather_sources(recipe, maps, roles):
    """One import per FBX file; every role clip and every map reference is sampled from its own file."""
    jobs = {}
    for role in roles:
        row = recipe_row(recipe, role)
        jobs.setdefault(row["file"], []).append(("clip", role, row))
    for key, bone_map in maps.items():
        ref = bone_map["reference"]
        jobs.setdefault(ref["file"], []).append(("ref", key, ref))
    clips, refs = {}, {}
    for file, items in jobs.items():
        path = pack_path(file)
        rig, fps = import_fbx(path)
        for kind, key, row in items:
            action = find_action(row["action"])
            if kind == "ref":
                start = action.frame_range[0] if row.get("frame", "start") == "start" else float(row["frame"])
                sample = sample_frames(rig, action, map_bones(maps[key]), start, start, tails=True)[0]
                refs[key] = {"file": file, "action": action.name, "frame": start, "pose": sample, "sha256": sha(path)}
                continue
            start, end = row.get("range") or action.frame_range
            clips[key] = {"file": file, "action": action.name, "fps": fps, "start": start, "end": end,
                          "frames": sample_frames(rig, action, map_bones(maps[row["map"]]), start, end), "sha256": sha(path)}
            print(f"SAMPLED {key}: {action.name} [{start},{end}] @ {fps}fps from {file}", flush=True)
    return clips, refs


def mirrored(clip, bone_map, ref):
    """Reflect source motion across the body midplane (x -> -x) and swap left/right legs.
    Used when the Rig298 skin is weaker on one side, so the source's dominant side lands on the healthier limb."""
    _, _, Vector, Quaternion = blender()

    def loc(v):
        return Vector((-v.x, v.y, v.z))

    def rot(q):
        return Quaternion((q.w, q.x, -q.y, -q.z))
    frames = [{name: (loc(value[0]), rot(value[1])) for name, value in frame.items()} for frame in clip["frames"]]
    pose = {name: (loc(value[0]), rot(value[1]), loc(value[2])) for name, value in ref["pose"].items()}
    swap = {"Fore_L": "Fore_R", "Fore_R": "Fore_L", "Hind_L": "Hind_R", "Hind_R": "Hind_L"}
    legs = {name: bone_map["legs"][swap[name]] for name in LEGS}
    return dict(clip, frames=frames), dict(bone_map, legs=legs), dict(ref, pose=pose)


def clip_span(clip, row):
    """Number of source frame intervals covered by u in [0,1]."""
    n = len(clip["frames"]) - 1
    return n + (int(row.get("periodExtraFrames", 0)) if row.get("loop") == "periodic" else 0)


def source_at(clip, row, u):
    """Interpolated world pose at normalised clip time u; periodic loops wrap end -> start."""
    frames = clip["frames"]
    n = len(frames) - 1
    span = clip_span(clip, row)
    x = max(0.0, min(1.0, u)) * span
    i = min(int(math.floor(x)), span - 1)
    f = x - i

    def at(j):
        return frames[j] if j <= n else frames[j - (n + 1)]
    a, b = at(i), at(i + 1)
    return {name: (a[name][0].lerp(b[name][0], f), a[name][1].slerp(b[name][1], f)) for name in a}


# ----------------------------------------------------------------------------------------------- target solver

class Target:
    """Rest data of the Rig298 armature and the per-frame pose solve (armature space == world: rig at identity)."""

    def __init__(self, rig, config):
        _, Matrix, Vector, Quaternion = blender()
        self.Matrix, self.Vector, self.Quaternion = Matrix, Vector, Quaternion
        identity = Matrix.Identity(4)
        require(all(abs(rig.matrix_world[i][j] - identity[i][j]) < 1e-6 for i in range(4) for j in range(4)),
                "Target rig object must sit at identity (rig_creatures contract)")
        self.rig = rig
        self.rest = {b.name: b.matrix_local.copy() for b in rig.data.bones}
        self.rest_inv = {n: m.inverted() for n, m in self.rest.items()}
        self.rest_rot = {n: m.to_quaternion() for n, m in self.rest.items()}
        self.parent = {b.name: (b.parent.name if b.parent else None) for b in rig.data.bones}
        self.tail = {b.name: b.tail_local.copy() for b in rig.data.bones}
        self.order = []
        pending = list(self.rest)
        while pending:
            for name in list(pending):
                if self.parent[name] is None or self.parent[name] in self.order:
                    self.order.append(name)
                    pending.remove(name)
        self.ground = float(config["animation"]["groundZ"])
        self.legs = {}
        for name in LEGS:
            leg = config["legs"][name]
            row = {key: Vector(leg[key]) for key in ("hip", "knee", "ankle", "toe")}
            row["parent"] = leg["parent"]
            require((self.rest[name + "_Upper"].translation - row["hip"]).length < 1e-4, "Config hip differs from rig " + name)
            require((self.rest[name + "_Foot"].translation - row["ankle"]).length < 1e-4, "Config ankle differs from rig " + name)
            row["a"] = (row["knee"] - row["hip"]).length
            row["b"] = (row["ankle"] - row["knee"]).length
            row["stand"] = (row["ankle"] - row["hip"]).length
            self.legs[name] = row
        self.leg_bones = {f"{leg}_{part}" for leg in LEGS for part in ("Upper", "Lower", "Foot")}

    def mid(self, name):
        return (self.rest[name].translation + self.tail[name]) * .5

    def child_base(self, desired, name):
        parent = self.parent[name]
        if parent is None:
            return self.rest[name].copy()
        return desired[parent] @ self.rest_inv[parent] @ self.rest[name]

    def delta(self, desired, name):
        return desired[name].to_quaternion() @ self.rest_rot[name].inverted()

    def ik(self, name, hip, target, parent_delta):
        """rig_creatures.solve_leg geometry: two-bone IK in the rest knee plane, rotated with the parent."""
        leg = self.legs[name]
        a, b = leg["a"], leg["b"]
        axis = target - hip
        require(axis.length > 1e-7, "IK target coincides with hip " + name)
        distance = max(abs(a - b) + 1e-6, min(a + b - 1e-6, axis.length))
        axis.normalize()
        reached = hip + axis * distance
        native_axis = (leg["ankle"] - leg["hip"]).normalized()
        rest_axis = parent_delta @ native_axis
        native_bend = (leg["knee"] - leg["hip"]) - native_axis * (leg["knee"] - leg["hip"]).dot(native_axis)
        bend = parent_delta @ native_bend
        bend = rest_axis.rotation_difference(axis) @ bend
        bend -= axis * bend.dot(axis)
        require(bend.length > 1e-9, "Collinear knee " + name)
        bend.normalize()
        along = (a * a - b * b + distance * distance) / (2 * distance)
        knee = hip + axis * along + bend * math.sqrt(max(0.0, a * a - along * along))
        return knee, reached, (reached - target).length

    def segment(self, name, start, end, reference):
        old = reference @ (self.tail[name] - self.rest[name].translation)
        rotation = old.rotation_difference(end - start) @ reference @ self.rest_rot[name]
        return self.Matrix.Translation(start) @ rotation.to_matrix().to_4x4()

    def chain(self, deltas, pelvis_offset):
        """Desired armature matrices of every non-leg bone (Root, axis chain, tails, MouthOrigin)."""
        Matrix = self.Matrix
        desired = {}
        for name in self.order:
            if name in self.leg_bones:
                continue
            base = self.child_base(desired, name)
            head = base.translation.copy()
            if name == "Pelvis":
                head += pelvis_offset
            rotation = deltas[name] @ self.rest_rot[name] if name in deltas else base.to_quaternion()
            desired[name] = Matrix.Translation(head) @ rotation.to_matrix().to_4x4()
        return desired

    def bone_tail(self, desired, name):
        return desired[name] @ (self.rest_inv[name] @ self.tail[name])

    def tail_names(self):
        return [n for n in self.order if n.startswith("Tail_")]

    def rest_tail_clearance(self):
        names = self.tail_names()
        return min(min(self.rest[n].translation.z, self.tail[n].z) for n in names) - self.ground if names else 0.0

    def grounded_chain(self, deltas, pelvis_offset, keep=.6):
        """chain(), then lift the whole tail about its base if a tail joint dips below `keep` x its rest ground clearance:
        a tail that meets the ground bends up instead of sinking (the Root lift would otherwise float every paw)."""
        desired = self.chain(deltas, pelvis_offset)
        names = self.tail_names()
        if not names:
            return desired, 0.0
        floor = self.ground + keep * max(self.rest_tail_clearance(), 0.0)
        raised = 0.0
        deltas = dict(deltas)
        for _ in range(4):
            base = desired[names[0]].translation
            points = [self.bone_tail(desired, n) for n in names]
            low = min(points, key=lambda q: q.z)
            need = floor - low.z
            if need <= 1e-5:
                break
            v = low - base
            axis = v.cross(self.Vector((0.0, 0.0, 1.0)))
            if v.length < 1e-6 or axis.length < 1e-6:
                break
            before = math.asin(max(-1.0, min(1.0, v.z / v.length)))
            after = math.asin(max(-1.0, min(1.0, (v.z + need) / v.length)))
            turn = self.Quaternion(axis.normalized(), after - before)
            for name in names:
                deltas[name] = turn @ deltas.get(name, desired[name].to_quaternion() @ self.rest_rot[name].inverted())
            raised += after - before
            desired = self.chain(deltas, pelvis_offset)
        return desired, raised

    def leg_target(self, desired, leg_name, spec):
        leg = self.legs[leg_name]
        parent_delta = self.delta(desired, leg["parent"])
        hip = self.child_base(desired, leg_name + "_Upper").translation
        offset = parent_delta @ (spec["body_rot"] @ (leg["ankle"] - leg["hip"]))
        reach = leg["a"] + leg["b"]
        length = min(max(offset.length * spec["body_scale"], reach * spec.get("min_extension", 0.0)), reach * .995)
        body = hip + offset.normalized() * length
        return hip, spec["contact"].lerp(body, spec["w_body"]), parent_delta

    def planted_needs(self, desired, legs):
        """Extra body drop and horizontal pelvis shift that would make every planted paw reachable."""
        Vector = self.Vector
        need, shift = 0.0, Vector()
        for leg_name in LEGS:
            if not legs[leg_name].get("planted"):
                continue
            hip, target, _ = self.leg_target(desired, leg_name, legs[leg_name])
            reach = (self.legs[leg_name]["a"] + self.legs[leg_name]["b"]) * .998
            v = hip - target
            horizontal = math.hypot(v.x, v.y)
            if horizontal < reach:
                need = max(need, v.z - math.sqrt(reach * reach - horizontal * horizontal))
            allowed = math.sqrt(max(0.0, reach * reach - v.z * v.z))
            if horizontal > allowed and horizontal > 1e-9:
                shift -= Vector((v.x, v.y, 0.0)) / horizontal * (horizontal - allowed)
        return need, shift

    def solve(self, deltas, pelvis_offset, legs, drop_cap=0.0, follow=True, tail_keep=.9):
        """deltas: bone -> world rotation delta; legs: name -> dict(contact, w_body, body_rot, body_scale, foot, planted).
        Planted paws stay where they are; the body yields instead: first a bounded drop (Rig298 legs stand ~98% extended, so
        a planted stride or a wide stance is otherwise out of reach), then, with follow, a horizontal pelvis shift that keeps a
        lunge within what the planted legs can reach (an animal cannot carry its body further than its planted legs)."""
        Matrix, Quaternion, Vector = self.Matrix, self.Quaternion, self.Vector
        drop, shift = 0.0, Vector()
        desired = self.chain(deltas, pelvis_offset)
        if drop_cap > 0 or follow:
            for _ in range(8):
                base = self.chain(deltas, pelvis_offset + shift)
                need, _ = self.planted_needs(base, legs)
                drop = min(max(need, 0.0), drop_cap)
                desired = self.chain(deltas, pelvis_offset + shift - Vector((0.0, 0.0, drop)))
                if not follow:
                    break
                _, more = self.planted_needs(desired, legs)
                if more.length < 1e-6:
                    break
                shift += more * .85
        desired, self.tail_raise = self.grounded_chain(deltas, pelvis_offset + shift - Vector((0.0, 0.0, drop)), tail_keep)
        errors = {}
        for leg_name in LEGS:
            spec = legs[leg_name]
            hip, target, parent_delta = self.leg_target(desired, leg_name, spec)
            knee, reached, error = self.ik(leg_name, hip, target, parent_delta)
            errors[leg_name] = (error, spec["w_body"], bool(spec.get("planted")),
                                [round(c, 4) for c in (target - hip)], round(self.legs[leg_name]["a"] + self.legs[leg_name]["b"], 4))
            desired[leg_name + "_Upper"] = self.segment(leg_name + "_Upper", hip, knee, parent_delta)
            desired[leg_name + "_Lower"] = self.segment(leg_name + "_Lower", knee, reached, parent_delta)
            mode = spec["foot"]
            if mode == "parent":
                foot_q = parent_delta @ self.rest_rot[leg_name + "_Foot"]
            else:
                yaw = Quaternion((parent_delta.w, 0.0, 0.0, parent_delta.z))
                yaw = yaw.normalized() if yaw.magnitude > 1e-9 else Quaternion()
                weight = spec["w_body"] if mode == "yaw" else 0.0
                foot_q = Quaternion().slerp(yaw, weight) @ self.rest_rot[leg_name + "_Foot"]
            desired[leg_name + "_Foot"] = Matrix.Translation(reached) @ foot_q.to_matrix().to_4x4()
        bases = {}
        for name in self.order:
            if name not in desired:
                bases[name] = Matrix.Identity(4)
                desired[name] = self.child_base(desired, name)
                continue
            parent = self.parent[name]
            reference = self.rest[name] if parent is None else desired[parent] @ self.rest_inv[parent] @ self.rest[name]
            bases[name] = reference.inverted() @ desired[name]
        return bases, desired, errors, drop, shift.length


def chain_lookup(source_params, deltas, s):
    i, f = arc_lookup(source_params, s)
    return deltas[i].slerp(deltas[i + 1], f)


class Retarget:
    """Per-map source/target correspondence measured once from the reference pose and the target rest."""

    def __init__(self, target, bone_map, ref, row):
        _, _, Vector, Quaternion = blender()
        self.Vector, self.Quaternion = Vector, Quaternion
        self.t, self.m = target, bone_map
        pose = ref["pose"]
        self.ref = {name: (value[0], value[1]) for name, value in pose.items()}
        require(tuple(bone_map.get("forward", (0, -1, 0))) == (0, -1, 0), "Map forward must be -Y (all packs face -Y)")
        self.chain_src = bone_map["axisChain"]["source"]
        self.chain_tgt = bone_map["axisChain"]["target"]
        require(self.chain_tgt[0] == "Pelvis" and self.chain_src[0] == bone_map["pelvis"], "Axis chains start at the pelvis")
        self.chain_src_s = arc_params([tuple((pose[n][0] + pose[n][2]) * .5) for n in self.chain_src])
        self.chain_tgt_s = dict(zip(self.chain_tgt, arc_params([tuple(target.mid(n)) for n in self.chain_tgt])))
        self.tail_src, self.tail_tgt = [], []
        if bone_map.get("tail"):
            self.tail_src = bone_map["tail"]["source"]
            self.tail_tgt = bone_map["tail"]["target"]
            self.tail_src_s = arc_params([tuple((pose[n][0] + pose[n][2]) * .5) for n in self.tail_src])
            self.tail_tgt_s = dict(zip(self.tail_tgt, arc_params([tuple(target.mid(n)) for n in self.tail_tgt])))
        self.legs = {}
        for name in LEGS:
            leg = bone_map["legs"][name]
            hip, foot = pose[leg["hip"]][0], pose[leg["foot"]][0]
            standing = (foot - hip).length
            require(standing > 1e-6, "Degenerate source leg " + name)
            self.legs[name] = {"hip": leg["hip"], "foot": leg["foot"], "parent": leg["parent"], "refHip": hip, "refFoot": foot,
                               "refOffset": foot - hip, "standing": standing,
                               "ratio": target.legs[name]["stand"] / standing}
        for left, right in (("Fore_L", "Fore_R"), ("Hind_L", "Hind_R")):
            src = self.legs[left]["refHip"].x - self.legs[right]["refHip"].x
            tgt = target.legs[left]["hip"].x - target.legs[right]["hip"].x
            require(src * tgt > 0, f"Map pairs {left}/{right} with opposite body sides (source dx {src:.3f}, target dx {tgt:.3f})")
        ratios = [value["ratio"] for value in self.legs.values()]
        stride = row.get("strideScale", bone_map.get("strideScale", "min"))
        self.horizontal = min(ratios) if stride == "min" else sum(ratios) / len(ratios) if stride == "mean" else float(stride)
        self.vertical = sum(ratios) / len(ratios)
        self.pelvis_ref = pose[bone_map["pelvis"]][0]

    def describe(self):
        return {"horizontalScale": self.horizontal, "verticalScale": self.vertical,
                "legRatios": {n: v["ratio"] for n, v in self.legs.items()},
                "sourceStandingM": {n: v["standing"] for n, v in self.legs.items()},
                "targetStandingM": {n: self.t.legs[n]["stand"] for n in LEGS}}

    def contact_rel(self, src, origin, name):
        """Source paw displacement from its reference stance (ground frame, horizontal) and its lift above the reference."""
        leg = self.legs[name]
        foot = src[leg["foot"]][0]
        rel = self.Vector((foot.x - origin.x - (leg["refFoot"].x - self.pelvis_ref.x),
                           foot.y - origin.y - (leg["refFoot"].y - self.pelvis_ref.y), 0.0))
        return rel, foot.z - leg["refFoot"].z

    def frame(self, src, origin, k, row, stride=1.0, center=None):
        """Solve inputs for one source pose: k scales pose amplitude, stride scales horizontal paw travel,
        center (per leg, optional) shifts the planted paw path so locomotion sweeps under the hip."""
        Quaternion, Vector, t = self.Quaternion, self.Vector, self.t
        amp_body = float(row.get("bodyAmplitude", 1.0)) * k
        amp_chain = float(row.get("chainAmplitude", 1.0)) * k
        amp_tail = float(row.get("tailAmplitude", 1.0)) * k
        delta = {n: src[n][1] @ self.ref[n][1].inverted() for n in src}
        pelvis_src = delta[self.m["pelvis"]]
        pelvis_inv = pelvis_src.inverted()
        pelvis = Quaternion().slerp(pelvis_src, amp_body)
        deltas = {"Pelvis": pelvis}
        rel_chain = [pelvis_inv @ delta[n] for n in self.chain_src]
        bone_amp = row.get("boneAmplitude", {})
        for name in self.chain_tgt[1:]:
            deltas[name] = pelvis @ Quaternion().slerp(chain_lookup(self.chain_src_s, rel_chain, self.chain_tgt_s[name]),
                                                       amp_chain * float(bone_amp.get(name, 1.0)))
        if self.tail_src:
            rel_tail = [pelvis_inv @ delta[n] for n in self.tail_src]
            for name in self.tail_tgt:
                deltas[name] = pelvis @ Quaternion().slerp(chain_lookup(self.tail_src_s, rel_tail, self.tail_tgt_s[name]), amp_tail)
        pelvis_now = src[self.m["pelvis"]][0]
        vertical = float(row.get("pelvisVerticalAmplitude", 1.0)) * (1.0 if row.get("fullCollapse") else k)
        offset = Vector(((pelvis_now.x - origin.x) * self.horizontal * k,
                         (pelvis_now.y - origin.y) * self.horizontal * k,
                         (pelvis_now.z - self.pelvis_ref.z) * self.vertical * vertical))
        feet_mode = row.get("feet", "blend")
        h0, h1 = row.get("bodyBlend", (.03, .12))
        legs, lifts = {}, {}
        for name, leg in self.legs.items():
            tl = t.legs[name]
            foot, hip = src[leg["foot"]][0], src[leg["hip"]][0]
            rel, lift = self.contact_rel(src, origin, name)
            eps = .01 * leg["standing"]
            contact = tl["ankle"] + Vector((rel.x * self.horizontal * stride, rel.y * self.horizontal * stride,
                                            max(0.0, lift - eps) * leg["ratio"] * k))
            if center:
                contact += center[name]
            now = delta[leg["parent"]].inverted() @ (foot - hip)
            body_rot = Quaternion().slerp(leg["refOffset"].rotation_difference(now), k)
            body_scale = 1 + k * (now.length / leg["standing"] - 1)
            w_body = 0.0 if feet_mode == "contact" else 1.0 if feet_mode == "body" else smoothstep(h0, h1, lift / leg["standing"])
            legs[name] = {"contact": contact, "w_body": w_body, "body_rot": body_rot, "body_scale": body_scale,
                          "foot": row.get("footRotation", "yaw"),
                          "planted": w_body < .5 and lift <= float(row.get("plantedLift", .02)) * leg["standing"],
                          "min_extension": float(row.get("minLegExtension", .6))}
            lifts[name] = lift / leg["standing"]
        return deltas, offset, legs, lifts


def ground_origins(clip, row, pelvis, count):
    """Horizontal origin per target key: loops -> drift line through the cycle mean; one-shots -> first frame."""
    _, _, Vector, _ = blender()
    frames = clip["frames"]
    xy = [Vector((f[pelvis][0].x, f[pelvis][0].y, 0.0)) for f in frames]
    if not row.get("loop"):
        return [xy[0].copy() for _ in range(count + 1)]
    n = len(frames) - 1
    span = clip_span(clip, row)
    drift = (xy[-1] - xy[0]) / max(1, n)
    mean = sum(xy, Vector()) / len(xy)
    return [mean + drift * (span * k / count - n / 2) for k in range(count + 1)]


# ----------------------------------------------------------------------------------------------- baking

def stride_center(target, retarget, clip, row, origins, count, stride):
    """Per-leg constant shift that puts the mean planted paw position straight under the hip (largest reach)."""
    _, _, Vector, _ = blender()
    planted = float(row.get("plantedLift", .02))
    sums = {name: [0.0, 0] for name in LEGS}
    for index in range(count):
        src = source_at(clip, row, index / count)
        for name in LEGS:
            rel, lift = retarget.contact_rel(src, origins[index], name)
            if lift <= planted * retarget.legs[name]["standing"]:
                sums[name][0] += rel.y * retarget.horizontal * stride
                sums[name][1] += 1
    center = {}
    for name in LEGS:
        leg = target.legs[name]
        mean = sums[name][0] / sums[name][1] if sums[name][1] else 0.0
        fraction = row.get("centerStride", 1.0)
        if isinstance(fraction, dict):
            fraction = fraction.get(name, fraction.get(name.split("_")[0], 0.0))
        center[name] = Vector((0.0, ((leg["hip"].y - leg["ankle"].y) - mean) * float(fraction), 0.0))
    return center


def solve_role(target, retarget, clip, row, k, stride=1.0):
    """All keys of one role at amplitude factor k and stride factor (pure maths, no depsgraph)."""
    _, _, Vector, Quaternion = blender()
    loop = row.get("loop") or False
    require(loop in (False, "periodic", "seam"), "loop must be false, periodic or seam")
    seconds = clip_span(clip, row) / clip["fps"] * float(row.get("timeScale", 1.0))
    count = max(2, int(round(seconds * TARGET_FPS)))
    origins = ground_origins(clip, row, retarget.m["pelvis"], count)
    drop_cap = float(row.get("autoBodyDrop", .15)) * min(leg["stand"] for leg in target.legs.values())
    center = stride_center(target, retarget, clip, row, origins, count, stride) if row.get("centerStride") else None
    keys = []
    for index in range(count + 1):
        if loop == "periodic" and index == count:
            break
        src = source_at(clip, row, index / count)
        deltas, offset, legs, lifts = retarget.frame(src, origins[index], k, row, stride, center)
        bases, desired, errors, drop, shift = target.solve(deltas, offset, legs, drop_cap, bool(row.get("bodyFollowsFeet", True)),
                                                           float(row.get("tailGroundKeep", .9)))
        keys.append({"bases": bases, "desired": desired, "errors": errors, "lifts": lifts, "legs": legs, "drop": drop, "shift": shift})
    if loop == "periodic":
        keys.append(dict(keys[0]))
    elif loop == "seam":
        first, last = keys[0]["bases"], keys[-1]["bases"]
        for name in first:
            l0, q0, _ = first[name].decompose()
            ln, qn, _ = last[name].decompose()
            correction = q0 @ qn.inverted()
            for index, key in enumerate(keys):
                w = index / count
                loc, rot, _ = key["bases"][name].decompose()
                rot = Quaternion().slerp(correction, w) @ rot
                loc = loc + (l0 - ln) * w
                key["bases"][name] = target.Matrix.Translation(loc) @ rot.to_matrix().to_4x4()
        keys[-1] = dict(keys[0])
    return keys, count, seconds


def mesh_min_z(meshes, where=None):
    bpy, _, _, _ = blender()
    import numpy as np
    depsgraph = bpy.context.evaluated_depsgraph_get()
    low = float("inf")
    for obj in meshes:
        evaluated = obj.evaluated_get(depsgraph)
        mesh = evaluated.to_mesh()
        try:
            co = np.empty(len(mesh.vertices) * 3, dtype=np.float64)
            mesh.vertices.foreach_get("co", co)
            co = co.reshape(-1, 3)
            m = evaluated.matrix_world
            z = co[:, 0] * m[2][0] + co[:, 1] * m[2][1] + co[:, 2] * m[2][2] + m[2][3]
            if float(z.min()) < low:
                low = float(z.min())
                if where is not None:
                    where[:] = [obj.name, int(z.argmin())]
        finally:
            evaluated.to_mesh_clear()
    return low


def bake_action(target, meshes, name, keys, loop):
    """Key every bone (location/rotation/scale) at frames 1..N+1; Root only gets the vertical anti-penetration lift."""
    bpy, _, Vector, Quaternion = blender()
    rig = target.rig
    action = bpy.data.actions.new(name)
    action.use_fake_user = True
    assign(rig, action)
    previous, lifts = {}, []
    target.worst_lift = None
    root_axis = target.rest_rot["Root"].inverted()
    for index, key in enumerate(keys):
        for pose in rig.pose.bones:
            loc, rot, _ = key["bases"][pose.name].decompose()
            if pose.name not in ("Root", "Pelvis"):
                require(loc.length < 2e-5, f"{name}: non-root bone {pose.name} would translate {loc.length:.2e} m")
                loc = Vector()
            if pose.name == "Root":
                require(math.hypot(*(target.rest_rot["Root"] @ loc).xy) < 1e-7, "Root must not move horizontally")
            if pose.name in previous and rot.dot(previous[pose.name]) < 0:
                rot.negate()
            previous[pose.name] = rot.copy()
            pose.rotation_mode = "QUATERNION"
            pose.location, pose.rotation_quaternion, pose.scale = loc, rot, (1.0, 1.0, 1.0)
        bpy.context.view_layer.update()
        if loop and index == len(keys) - 1:
            lift = lifts[0]
        else:
            where = []
            lift = max(0.0, target.ground - mesh_min_z(meshes, where))
            if lift > 0 and (target.worst_lift is None or lift > target.worst_lift["lift"]):
                obj = bpy.data.objects[where[0]]
                groups = {g.index: g.name for g in obj.vertex_groups}
                weights = {groups[g.group]: round(g.weight, 2) for g in obj.data.vertices[where[1]].groups if g.weight > .05}
                target.worst_lift = {"lift": lift, "frame": 1 + index, "vertex": where[1], "weights": weights}
        lifts.append(lift)
        root = rig.pose.bones["Root"]
        root.location = Vector(root.location) + root_axis @ Vector((0.0, 0.0, lift))
        for pose in rig.pose.bones:
            for channel in ("location", "rotation_quaternion", "scale"):
                pose.keyframe_insert(channel, frame=1 + index, group=pose.name)
    for curve in rig_tool().curves(action):
        for point in curve.keyframe_points:
            point.interpolation = "LINEAR"
    assign(rig, None)
    rig_tool().reset(rig)
    bpy.context.view_layer.update()
    return action, lifts


def walk_measure(target, keys, lifts, seconds, count):
    """Stance-foot in-place speed (rig metres/s) from the solved ankle path; forward is -Y so stance feet move +Y."""
    dt = seconds / count
    per_leg, samples, phases = {}, [], {}
    for name in LEGS:
        rest_z = target.legs[name]["ankle"].z
        planted = [key["legs"][name]["w_body"] < 1e-6 and key["desired"][name + "_Foot"].translation.z - rest_z < 1e-6
                   and key["errors"][name][0] < 1e-4 and key["errors"][name][2] for key in keys]
        ys = [key["desired"][name + "_Foot"].translation.y for key in keys]
        speeds = [(ys[i + 1] - ys[i - 1]) / (2 * dt) for i in range(1, len(keys) - 1) if planted[i - 1] and planted[i] and planted[i + 1]]
        runs, start = [], None
        for i, flag in enumerate(planted + [False]):
            if flag and start is None:
                start = i
            elif not flag and start is not None:
                if i - 1 - start >= 2:
                    runs.append((start, i - 1))
                start = None
        phases[name] = [(ys[b] - ys[a], (b - a) * dt) for a, b in runs]
        per_leg[name] = {"stanceSamples": len(speeds), "stanceFraction": sum(planted[:-1]) / max(1, len(planted) - 1),
                         "meanSpeed": sum(speeds) / len(speeds) if speeds else None,
                         "minSpeed": min(speeds) if speeds else None, "maxSpeed": max(speeds) if speeds else None}
        samples += speeds
    require(samples, "Walk has no planted stance samples")
    mean = sum(samples) / len(samples)
    require(mean > 1e-4, f"Walk stance feet do not travel backward (mean {mean:.5f} m/s): wrong forward axis or not a forward walk")
    slip = max(abs(v - mean) / mean for v in samples)
    phase_slip = [abs(travel - mean * duration) / (mean * duration) for rows in phases.values() for travel, duration in rows]
    lift_range = max(lifts) - min(lifts)
    return {"stanceFootSpeedNativeMps": mean, "perLeg": per_leg, "maxStanceSpeedDeviation": slip,
            "stancePhases": sum(len(rows) for rows in phases.values()),
            "maxStancePhaseSlipRatio": max(phase_slip) if phase_slip else None,
            "rootLiftRangeM": lift_range,
            "note": "Mean backward speed of planted ankles over all four legs (rig metres/s). Unity WalkMetresPerSecond = this x unityScale; "
                    "maxStanceSpeedDeviation predicts the Play slip ratio of a planted foot when the clip is driven at that speed (AC-Q3 <= .15)."}


def attack_peak(keys, row):
    names = row.get("peakBones") or ["MouthOrigin", "Fore_L_Foot", "Fore_R_Foot"]
    best = None
    for name in names:
        ys = [key["desired"][name].translation.y for key in keys]
        reach = [ys[0] - y for y in ys]
        index = max(range(len(reach)), key=lambda i: reach[i])
        if best is None or reach[index] > best[1]:
            best = (name, reach[index], index)
    count = len(keys) - 1
    return {"effector": best[0], "forwardReachM": best[1], "key": best[2],
            "peak01": min(.95, max(.05, best[2] / count)), "rawPeak01": best[2] / count}


def unity_scale(target, meshes, row):
    """Scale CompactFolklore298.Import gave the prefab: targetLength / length of the old Idle pose at frame 1."""
    bpy, _, _, _ = blender()
    import numpy as np
    old = bpy.data.actions.get("Idle")
    require(old is not None, "candidate.blend lacks its procedural Idle (cannot reproduce the Unity import scale)")
    assign(target.rig, old)
    bpy.context.scene.frame_set(1)
    bpy.context.view_layer.update()
    depsgraph = bpy.context.evaluated_depsgraph_get()
    lo, hi = float("inf"), float("-inf")
    axis = 0 if row.get("lengthAxis") == "x" else 1
    for obj in meshes:
        evaluated = obj.evaluated_get(depsgraph)
        mesh = evaluated.to_mesh()
        co = np.empty(len(mesh.vertices) * 3)
        mesh.vertices.foreach_get("co", co)
        co = co.reshape(-1, 3)
        m = evaluated.matrix_world
        values = co[:, 0] * m[axis][0] + co[:, 1] * m[axis][1] + co[:, 2] * m[axis][2] + m[axis][3]
        lo, hi = min(lo, float(values.min())), max(hi, float(values.max()))
        evaluated.to_mesh_clear()
    assign(target.rig, None)
    rig_tool().reset(target.rig)
    bpy.context.view_layer.update()
    length = float(row.get("targetLength") or 0)
    require(length > 0, "Manifest row needs targetLength")
    return length / (hi - lo), hi - lo


def gate_config(config):
    """Spec SPEC-ANIM-QUADRUPED-308 gate values (identical to the run's limits; asserted, never relaxed)."""
    gates = {"maxEdgeStretchRatio": 2.5, "minEdgeCompressionRatio": .15, "maxGroundPenetrationM": .008,
             "maxFootSoleSpreadM": .015, "maxInfluences": 4, "maxLoopSeamM": .0001}
    for key, value in gates.items():
        require(abs(config["limits"][key] - value) < 1e-12, f"Run limit {key}={config['limits'][key]} differs from Spec {value}")
    return config, dict(gates, maxIkTargetErrorM=.008, maxStanceSlipRatio=.15, minDeathCollapse=.25, maxRootLiftM=.008)


def build(args):
    bpy, _, _, _ = blender()
    rc = rig_tool()
    species = args.species
    recipe, maps = load_maps(species)
    paths = target_paths(species)
    out = species_out(species)
    out.mkdir(parents=True, exist_ok=True)
    roles = [r.strip() for r in args.roles.split(",")] if args.roles else list(recipe["roles"]) + list(recipe.get("extras", {}))
    candidate_hash = sha(paths["candidate"])
    started = time.time()
    clips, refs = gather_sources(recipe, maps, roles)
    config, gates = gate_config(read_json(paths["config"]))
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.wm.open_mainfile(filepath=str(paths["candidate"]))
    scene = bpy.context.scene
    scene.render.fps, scene.render.fps_base = TARGET_FPS, 1
    rig = next(o for o in scene.objects if o.type == "ARMATURE")
    meshes = sorted((o for o in scene.objects if o.type == "MESH"), key=lambda o: o.name)
    require(rig.name == species + "_Rig298", "Unexpected rig object " + rig.name)
    assign(rig, None)
    rc.reset(rig)
    bpy.context.view_layer.update()
    original = {obj.name: rc.world_vertices(obj) for obj in meshes}
    target = Target(rig, config)
    scale, length_native = unity_scale(target, meshes, paths["row"])
    for action in list(bpy.data.actions):
        bpy.data.actions.remove(action)
    retargets = {key: Retarget(target, maps[key], refs[key], {}) for key in maps}
    report = {"schema": VERSION, "species": species, "utc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
              "tool": rel(__file__), "toolSha256": sha(__file__), "blender": bpy.app.version_string,
              "candidate": rel(paths["candidate"]), "candidateSha256": candidate_hash, "creature": rel(paths["creature"]),
              "gates": gates, "unityScale": scale, "idleLengthNativeM": length_native,
              "maps": {key: dict(retargets[key].describe(), reference=dict((k, v) for k, v in refs[key].items() if k != "pose"))
                       for key in maps},
              "roles": {}, "status": "STARTED"}
    for role in roles:
        row = recipe_row(recipe, role)
        clip = clips[role]
        bone_map, ref = maps[row["map"]], refs[row["map"]]
        if row.get("mirror"):
            clip, bone_map, ref = mirrored(clip, bone_map, ref)
        retarget = Retarget(target, bone_map, ref, row)
        result = {"source": {"file": clip["file"], "action": clip["action"], "frames": [clip["start"], clip["end"]], "fps": clip["fps"],
                             "sha256": clip["sha256"]}, "recipe": row, "attempts": [], "status": "FAIL"}
        ladder = row.get("attempts") or [[1, 1], [1, .85], [.85, .85], [.85, .7], [.7, .7], [.7, .55], [.55, .55]]
        for k, stride in ladder:
            attempt = {"factor": k, "stride": stride}
            action_name = "RT308_" + role
            stale = bpy.data.actions.get(action_name)
            if stale is not None:
                bpy.data.actions.remove(stale)
            try:
                keys, count, seconds = solve_role(target, retarget, clip, row, k, stride)
                contact_error = max((e[0] for key in keys for e in key["errors"].values() if e[2]), default=0.0)
                any_error = max((e[0] for key in keys for e in key["errors"].values()), default=0.0)
                per_leg = {}
                for name in LEGS:
                    worst = max(range(len(keys)), key=lambda i: keys[i]["errors"][name][0])
                    per_leg[name] = {"maxErrorM": keys[worst]["errors"][name][0], "key": worst,
                                     "plantedKeys": sum(1 for key in keys if key["errors"][name][2]),
                                     "hipToTarget": keys[worst]["errors"][name][3], "reach": keys[worst]["errors"][name][4],
                                     "drop": keys[worst]["drop"]}
                drops = [key["drop"] for key in keys]
                attempt.update(keys=count + 1, seconds=count / TARGET_FPS, maxPlantedIkErrorM=contact_error, maxIkErrorM=any_error,
                               ikPerLeg=per_leg, maxBodyDropM=max(drops), meanBodyDropM=sum(drops) / len(drops),
                               maxBodyFollowShiftM=max(key["shift"] for key in keys))
                action, lifts = bake_action(target, meshes, action_name, keys, bool(row.get("loop")))
                attempt.update(maxRootLiftM=max(lifts), meanRootLiftM=sum(lifts) / len(lifts), worstLift=target.worst_lift)
                clip_row = {"name": action_name, "frames": [1, count + 1], "loop": bool(row.get("loop")), "headOnlyCompatibility": False}
                if role != "Death" and max(lifts) > gates["maxRootLiftM"]:
                    # The Root lift only exists to stop skin going under the ground; above the penetration tolerance it means
                    # the clip pushes the body/head through the floor and every planted paw would float.
                    raise ValueError(f"Root lift {max(lifts):.4f} m > {gates['maxRootLiftM']} (body would sink; paws would float)")
                if contact_error > gates["maxIkTargetErrorM"]:
                    raise ValueError(f"IK error on planted feet {contact_error:.4f} m > {gates['maxIkTargetErrorM']} ({per_leg})")
                # rig_creatures exempts exactly the clip named "Death" from the sole-flatness gate (paws follow the falling
                # body); validate the death under that name so the original gate semantics apply, then restore the name.
                gate_name = "Death" if role == "Death" else action_name
                action.name = gate_name
                clip_row["name"] = gate_name
                try:
                    deformation = rc.validate_deformation(rig, meshes, config, [clip_row], original)[0]
                finally:
                    action.name = action_name
                    clip_row["name"] = action_name
                    assign(rig, None)
                    rc.reset(rig)
                attempt["deformation"] = deformation
                if role == "Death":
                    # AC-Q5: a death must actually go down (legs give way) - a clip that only nods is not accepted.
                    rest_height = target.rest["Pelvis"].translation.z - target.ground
                    end_height = keys[-1]["desired"]["Pelvis"].translation.z + lifts[-1] - target.ground
                    collapse = 1 - end_height / rest_height
                    attempt["deathCollapse"] = {"pelvisRestHeightM": rest_height, "pelvisEndHeightM": end_height,
                                                "collapseRatio": collapse, "endRootLiftM": lifts[-1], "minimum": gates["minDeathCollapse"]}
                    if collapse < gates["minDeathCollapse"]:
                        raise ValueError(f"Death does not collapse: pelvis ends at {end_height:.3f} m of {rest_height:.3f} m "
                                         f"(collapse {collapse:.2f} < {gates['minDeathCollapse']}) - keep the procedural clip")
                if role == "Walk" or row.get("measureWalk"):
                    attempt["walk"] = walk_measure(target, keys, lifts, count / TARGET_FPS, count)
                    attempt["walk"]["stanceFootSpeedUnityMps"] = attempt["walk"]["stanceFootSpeedNativeMps"] * scale
                if role == "Attack" or row.get("measurePeak"):
                    attempt["peak"] = attack_peak(keys, row)
                attempt["status"] = "PASS"
                result.update(status="PASS", factor=k, stride=stride, action=action_name, keys=count + 1, seconds=count / TARGET_FPS,
                              loop=bool(row.get("loop")), _clip=clip_row)
                result["attempts"].append(attempt)
                break
            except Exception as error:
                attempt.update(status="FAIL", error=str(error)[:1200])
                result["attempts"].append(attempt)
                failed = bpy.data.actions.get(action_name)
                if failed is not None:
                    # Keep the last failed attempt for `probe` (never exported: removed before the FBX write).
                    previous = bpy.data.actions.get("DEBUG_" + role)
                    if previous is not None:
                        bpy.data.actions.remove(previous)
                    failed.name = "DEBUG_" + role
                assign(rig, None)
                rc.reset(rig)
                bpy.context.view_layer.update()
                print(f"ATTEMPT {role} k={k} stride={stride} FAIL: {str(error)[:300]}", flush=True)
        report["roles"][role] = result
        print(f"ROLE {role}: {result['status']} factor={result.get('factor')} stride={result.get('stride')}", flush=True)
    passed = [role for role in roles if report["roles"][role]["status"] == "PASS"]
    clips_for_roundtrip = [report["roles"][role].pop("_clip") for role in passed]
    report["passedRoles"] = passed
    report["failedRoles"] = [role for role in roles if role not in passed]
    walk = report["roles"].get("Walk", {})
    walk_pass = walk.get("status") == "PASS"
    attack = report["roles"].get("Attack", {})
    report["unity"] = {
        "species": species, "rigObject": rig.name, "fbx": rel(out / f"{species}_RT308.fbx"), "unityScale": scale,
        "walkMetresPerSecondNative": walk["attempts"][-1]["walk"]["stanceFootSpeedNativeMps"] if walk_pass else 0.0,
        "walkMetresPerSecondUnity": walk["attempts"][-1]["walk"]["stanceFootSpeedUnityMps"] if walk_pass else 0.0,
        "attackPeak01": attack["attempts"][-1]["peak"]["peak01"] if attack.get("status") == "PASS" else 0.0,
        "roles": [{"role": role, "clip": "RT308_" + role, "pass": report["roles"][role]["status"] == "PASS",
                   "loop": bool(recipe_row(recipe, role).get("loop")), "seconds": report["roles"][role].get("seconds", 0.0),
                   "extra": role not in recipe["roles"],
                   "peak01": (report["roles"][role]["attempts"][-1].get("peak") or {}).get("peak01", .5),
                   "speedUnity": ((report["roles"][role]["attempts"][-1].get("walk") or {}).get("stanceFootSpeedUnityMps") or 0.0)
                   if report["roles"][role]["status"] == "PASS" else 0.0} for role in roles]}
    report["status"] = "BAKED" if passed else "NOTHING_PASSED"
    report["seconds"] = round(time.time() - started, 1)
    fbx = out / f"{species}_RT308.fbx"
    blend = out / f"{species}_RT308.blend"
    if passed:
        assign(rig, None)
        rc.reset(rig)
        scene.frame_set(1)
        bpy.context.view_layer.update()
        for role in passed:
            debug = bpy.data.actions.get("DEBUG_" + role)
            if debug is not None:
                bpy.data.actions.remove(debug)
        bpy.ops.wm.save_as_mainfile(filepath=str(blend))
        for action in [a for a in bpy.data.actions if a.name.startswith("DEBUG_")]:
            bpy.data.actions.remove(action)
        require(all(a.name.startswith("RT308_") for a in bpy.data.actions), "Only RT308_* actions may be exported")
        bpy.ops.object.select_all(action="DESELECT")
        rig.select_set(True)
        for obj in meshes:
            obj.select_set(True)
        bpy.context.view_layer.objects.active = rig
        bpy.ops.export_scene.fbx(filepath=str(fbx), use_selection=True,
                                 object_types={"MESH", "ARMATURE"}, use_mesh_modifiers=False,
                                 add_leaf_bones=False, use_armature_deform_only=False,
                                 bake_anim=True, bake_anim_use_all_actions=True,
                                 bake_anim_use_nla_strips=False, bake_anim_use_all_bones=False,
                                 bake_anim_step=.5, bake_anim_simplify_factor=0,
                                 apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS",
                                 axis_forward="-Z", axis_up="Y", path_mode="STRIP", embed_textures=False)
        report["outputs"] = {"fbx": rel(fbx), "fbxSha256": sha(fbx), "blend": rel(blend)}
        native = {obj.name: {"positions": original[obj.name],
                             "weights": [{obj.vertex_groups[g.group].name: g.weight for g in v.groups if g.weight > 1e-7} for v in obj.data.vertices],
                             "cornerSignature": rc.mesh_corner_signature(obj), "uvLayerCount": len(obj.data.uv_layers)} for obj in meshes}
        bones = {b.name: {"parent": b.parent.name if b.parent else None} for b in rig.data.bones}
        save_json(out / f"{species}_RT308.report.json", report)
        try:
            report["roundtrip"] = rc.roundtrip(fbx, native, config, clips_for_roundtrip, bones)
            report["roundtrip"]["restFrames"] = compare_rest_frames(paths["creature"], fbx)
        except Exception as error:
            report["roundtrip"] = {"status": "FAIL", "error": str(error)[:1500]}
            report["status"] = "ROUNDTRIP_FAIL"
    report["candidateUnchanged"] = sha(paths["candidate"]) == candidate_hash
    require(report["candidateUnchanged"], "candidate.blend changed - it must never be re-saved")
    save_json(out / f"{species}_RT308.report.json", report)
    save_json(no_assets(OUT / "reports") / f"{species}_RT308.report.json", report)
    return {"status": report["status"], "passed": passed, "failed": report["failedRoles"],
            "report": rel(out / f"{species}_RT308.report.json"), "seconds": report["seconds"]}


def compare_rest_frames(creature, fbx):
    """Unity clip curves are absolute local transforms: the new FBX must reproduce creature.fbx bone frames exactly."""
    bpy, _, _, _ = blender()

    def frames(path):
        bpy.ops.wm.read_factory_settings(use_empty=True)
        bpy.ops.import_scene.fbx(filepath=str(path), use_anim=False)
        rig = next(o for o in bpy.context.scene.objects if o.type == "ARMATURE")
        return rig.name, {b.name: (rig.matrix_world @ b.matrix_local).copy() for b in rig.data.bones}
    name_a, a = frames(creature)
    name_b, b = frames(fbx)
    require(name_a == name_b, f"Armature object name differs: {name_a} vs {name_b}")
    require(set(a) == set(b), "Bone names differ from creature.fbx")
    error = max(abs(a[n][i][j] - b[n][i][j]) for n in a for i in range(4) for j in range(4))
    require(error < 1e-5, f"Bone rest frames differ from creature.fbx by {error}")
    return {"status": "PASS", "bones": len(a), "maxMatrixError": error, "armature": name_a}


# ----------------------------------------------------------------------------------------------- render sheets

def set_engine(scene):
    for engine in ("BLENDER_WORKBENCH", "BLENDER_EEVEE_NEXT", "BLENDER_EEVEE"):
        try:
            scene.render.engine = engine
            return engine
        except TypeError:
            continue
    raise RuntimeError("No raster engine available")


def render(args):
    """Side/front pose sheets per role plus a turntable (rest pose and attack peak). Reads <species>_RT308.blend only."""
    bpy, _, Vector, _ = blender()
    import numpy as np
    species = args.species
    out = species_out(species)
    blend = out / f"{species}_RT308.blend"
    report_path = out / f"{species}_RT308.report.json"
    require(blend.is_file() and report_path.is_file(), "Build first: " + str(blend))
    report = read_json(report_path)
    renders = no_assets(OUT / "Renders")
    renders.mkdir(parents=True, exist_ok=True)
    blend_hash = sha(blend)
    bpy.ops.wm.open_mainfile(filepath=str(blend))
    scene = bpy.context.scene
    rig = next(o for o in scene.objects if o.type == "ARMATURE")
    meshes = [o for o in scene.objects if o.type == "MESH"]
    ground = float(read_json(target_paths(species)["config"])["animation"]["groundZ"])
    engine = set_engine(scene)
    if engine == "BLENDER_WORKBENCH":
        shading = scene.display.shading
        shading.light, shading.color_type = "STUDIO", "SINGLE"
        shading.single_color = (.62, .60, .56)
        # Workbench ignores per-object shadow flags: shadows off, so the camera-parented label cannot streak the cells.
        shading.show_shadows, shading.show_cavity, shading.show_object_outline = False, True, True
        shading.background_type = "VIEWPORT" if hasattr(shading, "background_type") else shading.background_type
        scene.display.render_aa = "8"
    w, h = args.size
    scene.render.resolution_x, scene.render.resolution_y, scene.render.resolution_percentage = w, h, 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.film_transparent = False
    world = bpy.data.worlds.new("RT308Review")
    world.color = (.86, .84, .80)
    scene.world = world
    actions = [a for a in bpy.data.actions if a.name.startswith("RT308_")]
    order = [r["role"] for r in report["unity"]["roles"]]
    actions.sort(key=lambda a: order.index(a.name[6:]) if a.name[6:] in order else 99)
    require(actions, "No RT308 actions in " + str(blend))
    # One framing for every cell: union of skinned bounds over sampled frames of every action.
    lo, hi = Vector((1e9, 1e9, 1e9)), Vector((-1e9, -1e9, -1e9))
    for action in actions:
        assign(rig, action)
        a, b = action.frame_range
        for f in (a, a + (b - a) * .25, a + (b - a) * .5, a + (b - a) * .75, b):
            frame_at(scene, f)
            for obj in meshes:
                for corner in obj.evaluated_get(bpy.context.evaluated_depsgraph_get()).bound_box:
                    p = obj.matrix_world @ Vector(corner)
                    lo, hi = Vector(map(min, lo, p)), Vector(map(max, hi, p))
    centre = (lo + hi) * .5
    extent = max(hi - lo)
    bpy.ops.mesh.primitive_plane_add(size=extent * 30, location=(centre.x, centre.y, ground - .0005))
    camera_data = bpy.data.cameras.new("RT308Camera")
    camera_data.type, camera_data.ortho_scale = "ORTHO", extent * 1.25
    camera = bpy.data.objects.new("RT308Camera", camera_data)
    scene.collection.objects.link(camera)
    scene.camera = camera
    text_data = bpy.data.curves.new("RT308Label", "FONT")
    text_data.size = extent * .075
    label = bpy.data.objects.new("RT308Label", text_data)
    scene.collection.objects.link(label)
    label.parent = camera
    label.location = (-extent * .6, extent * .6 * h / w - extent * .1, -1.0)
    label.visible_shadow = False

    def place(azimuth_deg, elevation_deg):
        az, el = math.radians(azimuth_deg), math.radians(elevation_deg)
        direction = Vector((math.cos(el) * math.cos(az), math.cos(el) * math.sin(az), math.sin(el)))
        camera.location = centre + direction * extent * 4
        camera.rotation_euler = (centre - camera.location).to_track_quat("-Z", "Y").to_euler()

    def shot(path, text):
        text_data.body = text
        scene.render.filepath = str(path)
        bpy.ops.render.render(write_still=True)
        image = bpy.data.images.load(str(path))
        pixels = np.empty(w * h * 4, dtype=np.float32)
        image.pixels.foreach_get(pixels)
        bpy.data.images.remove(image)
        return pixels.reshape(h, w, 4)

    def sheet(cells, rows, cols, path):
        grid = np.ones((rows * h, cols * w, 4), dtype=np.float32)
        for (r, c), pixels in cells.items():
            grid[(rows - 1 - r) * h:(rows - r) * h, c * w:(c + 1) * w] = pixels
        image = bpy.data.images.new(path.stem, cols * w, rows * h, alpha=False)
        image.pixels.foreach_set(grid.ravel())
        image.filepath_raw = str(path)
        image.file_format = "PNG"
        image.save()
        bpy.data.images.remove(image)

    tmp = renders / f"_{species}_cells"
    tmp.mkdir(exist_ok=True)
    fractions = (0, .25, .5, .75, 1)
    peaks = {r["role"]: r["peak01"] for r in report["unity"]["roles"]}
    receipts = {"blend": rel(blend), "blendSha256": blend_hash, "engine": engine, "cell": [w, h], "sheets": []}
    for view, (az, el) in (("side", (0, 8)), ("front", (-90, 8))):
        place(az, el)
        cells = {}
        for r, action in enumerate(actions):
            assign(rig, action)
            a, b = action.frame_range
            role = action.name[6:]
            for c, fraction in enumerate(fractions):
                if role in ("Attack", "TailAttack") and c == 2:
                    fraction = peaks.get(role, .5)
                frame = a + (b - a) * fraction
                frame_at(scene, frame)
                cells[(r, c)] = shot(tmp / f"{view}_{role}_{c}.png", f"{role} {fraction:.2f}")
        path = renders / f"{species}_RT308_pose_{view}.png"
        sheet(cells, len(actions), len(fractions), path)
        receipts["sheets"].append({"path": rel(path), "sha256": sha(path), "rows": [a.name for a in actions],
                                   "columns": list(fractions), "view": view})
    cells = {}
    turn = [("rest", None, 0.0)] + [(a.name[6:], a, peaks.get(a.name[6:], .5)) for a in actions if a.name[6:] in ("Attack",)]
    for r, (title, action, fraction) in enumerate(turn):
        assign(rig, action)
        if action is None:
            rig_tool().reset(rig)
        else:
            a, b = action.frame_range
            frame_at(scene, a + (b - a) * fraction)
        bpy.context.view_layer.update()
        for c in range(8):
            place(c * 45, 18)
            cells[(r, c)] = shot(tmp / f"turn_{title}_{c}.png", f"{title} {c * 45}deg")
    path = renders / f"{species}_RT308_turntable.png"
    sheet(cells, len(turn), 8, path)
    receipts["sheets"].append({"path": rel(path), "sha256": sha(path), "rows": [t[0] for t in turn], "columns": [c * 45 for c in range(8)]})
    for cell in tmp.iterdir():
        cell.unlink()
    tmp.rmdir()
    require(sha(blend) == blend_hash, "Render must not modify the retarget blend")
    receipts["status"] = "RENDERED_NOT_VISUALLY_APPROVED"
    save_json(renders / f"{species}_RT308_renders.json", receipts)
    return {"status": receipts["status"], "sheets": [s["path"] for s in receipts["sheets"]]}


# ----------------------------------------------------------------------------------------------- standalone roundtrip

def roundtrip_cmd(args):
    """Re-verify an exported FBX against its retarget blend (validate samples + rig_creatures.roundtrip + rest frames)."""
    bpy, _, _, _ = blender()
    rc = rig_tool()
    species = args.species
    out = species_out(species)
    paths = target_paths(species)
    blend, fbx = out / f"{species}_RT308.blend", out / f"{species}_RT308.fbx"
    require(blend.is_file() and fbx.is_file(), "Build first")
    config, _ = gate_config(read_json(paths["config"]))
    bpy.ops.wm.open_mainfile(filepath=str(blend))
    rig = next(o for o in bpy.context.scene.objects if o.type == "ARMATURE")
    meshes = sorted((o for o in bpy.context.scene.objects if o.type == "MESH"), key=lambda o: o.name)
    assign(rig, None)
    rc.reset(rig)
    bpy.context.view_layer.update()
    original = {obj.name: rc.world_vertices(obj) for obj in meshes}
    clips = []
    for action in sorted((a for a in bpy.data.actions if a.name.startswith("RT308_")), key=lambda a: a.name):
        a, b = action.frame_range
        clips.append({"name": action.name, "frames": [int(a), int(b)], "loop": False, "headOnlyCompatibility": False})
    death = bpy.data.actions.get("RT308_Death")
    for clip in clips:
        if clip["name"] == "RT308_Death":   # same sole-gate exemption as build (rig_creatures keys it on the name "Death")
            death.name, clip["name"] = "Death", "Death"
    try:
        rows = rc.validate_deformation(rig, meshes, config, clips, original)
    finally:
        if death is not None:
            death.name = "RT308_Death"
        for clip in clips:
            if clip["name"] == "Death":
                clip["name"] = "RT308_Death"
    native = {obj.name: {"positions": original[obj.name],
                         "weights": [{obj.vertex_groups[g.group].name: g.weight for g in v.groups if g.weight > 1e-7} for v in obj.data.vertices],
                         "cornerSignature": rc.mesh_corner_signature(obj), "uvLayerCount": len(obj.data.uv_layers)} for obj in meshes}
    bones = {b.name: {"parent": b.parent.name if b.parent else None} for b in rig.data.bones}
    result = rc.roundtrip(fbx, native, config, clips, bones)
    result["restFrames"] = compare_rest_frames(paths["creature"], fbx)
    result["deformation"] = rows
    result["fbxSha256"] = sha(fbx)
    save_json(no_assets(OUT / "reports") / f"{species}_RT308.roundtrip.json", result)
    return {"status": result["status"], "restFrames": result["restFrames"]["status"], "actions": result["actions"]}


# ----------------------------------------------------------------------------------------------- probe (diagnostics)

def probe(args):
    """Worst stretched/compressed edges of one action in the retarget blend, with the bones that drive them."""
    bpy, _, _, _ = blender()
    import numpy as np
    rc = rig_tool()
    out = species_out(args.species)
    bpy.ops.wm.open_mainfile(filepath=str(out / f"{args.species}_RT308.blend"))
    rig = next(o for o in bpy.context.scene.objects if o.type == "ARMATURE")
    obj = next(o for o in bpy.context.scene.objects if o.type == "MESH")
    action = bpy.data.actions.get(args.action)
    require(action is not None, "No action " + args.action + " in " + str([a.name for a in bpy.data.actions]))
    assign(rig, None)
    rc.reset(rig)
    bpy.context.view_layer.update()
    rest = np.array([tuple(obj.matrix_world @ v.co) for v in obj.data.vertices])
    edges = np.array([tuple(e.vertices) for e in obj.data.edges])
    length = np.linalg.norm(rest[edges[:, 0]] - rest[edges[:, 1]], axis=1)
    keep = length >= .0007
    edges, length = edges[keep], length[keep]
    names = {g.index: g.name for g in obj.vertex_groups}
    assign(rig, action)
    a, b = action.frame_range
    rows = []
    frame = a
    while frame <= b + 1e-6:
        frame_at(bpy.context.scene, frame)
        depsgraph = bpy.context.evaluated_depsgraph_get()
        evaluated = obj.evaluated_get(depsgraph)
        mesh = evaluated.to_mesh()
        co = np.empty(len(mesh.vertices) * 3)
        mesh.vertices.foreach_get("co", co)
        evaluated.to_mesh_clear()
        co = co.reshape(-1, 3)
        ratio = np.linalg.norm(co[edges[:, 0]] - co[edges[:, 1]], axis=1) / length
        for index in np.argsort(ratio)[:3].tolist() + np.argsort(ratio)[-3:].tolist():
            rows.append((float(ratio[index]), frame, int(edges[index][0]), int(edges[index][1])))
        frame += 1
    rows.sort()
    report = []
    for ratio, frame, v0, v1 in rows[:6] + rows[-6:]:
        weights = {names[g.group]: round(g.weight, 2) for g in obj.data.vertices[v0].groups if g.weight > .05}
        report.append({"ratio": round(ratio, 3), "frame": frame, "edge": [v0, v1], "rest": [round(c, 3) for c in rest[v0]], "weights": weights})
        print("PROBE", json.dumps(report[-1]), flush=True)
    return {"status": "PROBED", "action": args.action, "edges": len(report)}


# ----------------------------------------------------------------------------------------------- self-test

def self_test(_args):
    checks = []

    def check(condition, label):
        require(condition, "Self-test: " + label)
        checks.append(label)
    params = arc_params([(0, 0, 0), (0, 1, 0), (0, 3, 0)])
    check(params == [0.0, 1 / 3, 1.0], "Arc-length parameters")
    check(arc_lookup(params, .5) == (1, .25), "Arc lookup inside segment")
    check(arc_lookup(params, 1.0) == (1, 1.0) and arc_lookup(params, -1)[0] == 0, "Arc lookup clamps")
    check(smoothstep(0, 1, .5) == .5 and smoothstep(.2, .1, .3) == 1.0, "Smoothstep")
    for species in SPECIES:
        recipe, maps = load_maps(species)
        check(set(ROLES) <= set(recipe["roles"]), species + " recipe covers the six roles")
        for role, row in list(recipe["roles"].items()) + list(recipe.get("extras", {}).items()):
            check(row["map"] in maps and (PACKS / row["file"]).suffix.lower() == ".fbx", f"{species}/{role} map and FBX named")
            check(row.get("loop") in (False, None, "periodic", "seam"), f"{species}/{role} loop mode")
            check(row.get("feet", "blend") in ("contact", "blend", "body"), f"{species}/{role} feet mode")
        for key, bone_map in maps.items():
            check(set(bone_map["legs"]) == set(LEGS), key + " maps four legs")
            check(bone_map["axisChain"]["target"][0] == "Pelvis" and bone_map["axisChain"]["source"][0] == bone_map["pelvis"],
                  key + " chain anchored at the pelvis")
    return {"status": "MATH_AND_RECIPE_CHECKS_PASS", "checks": checks, "blenderRequiredFor": ["inspect-source", "build", "render", "roundtrip"]}


# ----------------------------------------------------------------------------------------------- entry

def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    commands = parser.add_subparsers(dest="command", required=True)
    p = commands.add_parser("inspect-source", help="Dump source skeleton, rest pose, clips, fps and motion ranges")
    p.add_argument("--pack", required=True, choices=sorted(PACK_FILES))
    p.add_argument("--filter", help="Only FBX files whose name contains this text")
    p = commands.add_parser("build", help="Retarget the recipe roles onto the species rig, gate, export FBX + report")
    p.add_argument("--species", required=True, choices=SPECIES)
    p.add_argument("--roles", help="Comma-separated subset (default: every role and extra in the recipe)")
    p = commands.add_parser("render", help="Pose/turntable sheets from the retarget blend")
    p.add_argument("--species", required=True, choices=SPECIES)
    p.add_argument("--size", type=int, nargs=2, default=(360, 270))
    p = commands.add_parser("roundtrip", help="Re-verify the exported FBX against the retarget blend")
    p.add_argument("--species", required=True, choices=SPECIES)
    commands.add_parser("self-test", help="Pure maths + recipe/map checks (no Blender)")
    p = commands.add_parser("probe", help="Diagnostics: worst skin edges of one action (RT308_* or DEBUG_*) in the retarget blend")
    p.add_argument("--species", required=True, choices=SPECIES)
    p.add_argument("--action", required=True)
    args = parser.parse_args(argv)
    handlers = {"inspect-source": inspect_source, "build": build, "render": render, "roundtrip": roundtrip_cmd, "self-test": self_test, "probe": probe}
    result = handlers[args.command](args)
    print("RESULT " + json.dumps(result, ensure_ascii=False, allow_nan=False), flush=True)


if __name__ == "__main__":
    main(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:])
