"""Offline candidate skinning for approved Meshy #298 GLBs; never sculpt a model.

Normal Python (no Blender needed):
  python rig_creatures.py template --species fox_spirit --output fox-rig.json
  python rig_creatures.py self-test

Blender 5 background, factory startup:
  blender -b --factory-startup -P rig_creatures.py -- inspect --input model.glb --output inspection
  blender -b --factory-startup -P rig_creatures.py -- build --input model.glb \
      --config fox-rig.json --approval static-review.json --output candidate-run-01

The inspection exports indexed world coordinates to help fit anatomical landmarks
and partition skin regions. Coordinates are Blender world metres (+Z up). No
automatic pose straightening, remeshing, simplification, joining, or material
replacement occurs. The operator must fit joints/centreline to the ACTUAL animal.
For curled serpents, partition overlapping coils by vertex ranges: proximity alone
cannot identify anatomy. For quadrupeds, give each leg its own region below the
shoulder/hip transition. Regions are ordered; the last matching region wins.

Approval schema: {"status":"PASS", "sourceSha256":"...",
 "reviewer":"...", "reviewedAtUtc":"...", "staticGeometryApproved":true,
 "images":[{"path":"relative-or-absolute.png", "sha256":"..."}],
 "notes":"Actual static model viewed; silhouette, separated limbs, no fused floor."}
This tool validates the receipt; it does not award visual approval itself.

Outputs: source-preserving candidate.blend, creature.fbx, rig-report.json, and a
Blender FBX roundtrip report. Six Generic clips are Idle/Walk/Attack/Hit/Stun/Death.
Cheongryong also has three CR_* head-only compatibility clips. Its Generic body
clips must NOT run together with CheongryongBodyFollow/TailSweep, which own body
transforms at runtime. Existing CR binding paths include Cheongryong_PrototypeRig/
Root/Head/Body_01/.../Body_24 and Head/MouthOrigin, Body_24/TailTip.

Technical PASS is not art, contact, self-collision, or Unity approval. Sampled
stretch/contact measurements are explicit gates; inspected anatomy, weight paint,
and runtime import/culling/combat remain separate. Build never writes under Assets,
never invokes Unity/network/API services, and never overwrites the source GLB.
"""

from __future__ import annotations

import argparse
from collections import Counter
import hashlib
import json
import math
from pathlib import Path
import struct
import sys


VERSION = 1
SPECIES = ("bulgasari", "fox_spirit", "imugi", "cheongryong")
LEGS = ("Fore_L", "Fore_R", "Hind_L", "Hind_R")
CLIPS = {"Idle": (2.0, True), "Walk": (1.2, True), "Attack": (1.4, False),
         "Hit": (.55, False), "Stun": (1.5, True), "Death": (1.7, False)}
BIND_TOLERANCE = 0.0001


def require(condition, message):
    if not condition:
        raise ValueError(message)


def read_json(path):
    return json.loads(Path(path).read_text(encoding="utf-8-sig"))


def save_json(path, value):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2, allow_nan=False) + "\n",
                    encoding="utf-8")


def sha(path):
    digest = hashlib.sha256()
    with Path(path).open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def add(a, b):
    return tuple(x + y for x, y in zip(a, b))


def sub(a, b):
    return tuple(x - y for x, y in zip(a, b))


def mul(a, n):
    return tuple(x * n for x in a)


def dot(a, b):
    return sum(x * y for x, y in zip(a, b))


def norm(a):
    return math.sqrt(dot(a, a))


def unit(a):
    require(norm(a) > 1e-10, "Zero-length direction")
    return mul(a, 1 / norm(a))


def v3(value, label):
    require(isinstance(value, (tuple, list)) and len(value) == 3 and
            all(isinstance(x, (int, float)) and math.isfinite(x) for x in value),
            f"{label} must be three finite native-world coordinates; fit the actual model first")
    return tuple(float(x) for x in value)


def segment(p, a, b):
    delta = sub(b, a)
    denominator = dot(delta, delta)
    require(denominator > 1e-12, "Degenerate bone segment")
    t = max(0.0, min(1.0, dot(sub(p, a), delta) / denominator))
    return norm(sub(p, add(a, mul(delta, t)))), t


def resample(points, count):
    require(len(points) >= 2 and count >= 2, "Centreline needs at least two points")
    lengths = [norm(sub(b, a)) for a, b in zip(points, points[1:])]
    require(all(x > 1e-6 for x in lengths), "Repeated centreline point")
    total = sum(lengths)
    output = []
    for index in range(count):
        distance = total * index / (count - 1)
        for part, length in enumerate(lengths):
            if distance <= length or part == len(lengths) - 1:
                output.append(add(points[part], mul(sub(points[part + 1], points[part]),
                                                   min(1, distance / length))))
                break
            distance -= length
    return output


def normalized(raw):
    values = sorted(((name, weight) for name, weight in raw.items() if weight > 1e-9),
                    key=lambda item: (-item[1], item[0]))[:4]
    total = sum(weight for _, weight in values)
    require(total > 0 and math.isfinite(total), "Unweighted/nonfinite vertex")
    return {name: weight / total for name, weight in values}


def skin_weights(point, region, bones):
    names = region["bones"]
    distances = {name: segment(point, bones[name]["head"], bones[name]["tail"])[0]
                 for name in names}
    if region.get("rigid", False):
        require(len(names) == 1, "A rigid region must name exactly one bone")
        return {names[0]: 1.0}, distances[names[0]]
    # Restrict interpolation to a contiguous local part of an articulated chain.
    # This avoids binding one vertex to two spatially adjacent, distant coils.
    if region.get("chainBlend", False):
        nearest = min(range(len(names)), key=lambda i: (distances[names[i]], i))
        names = names[max(0, nearest - 1):nearest + 2]
    spread = float(region.get("blendRadiusM", .025))
    require(spread > 0, "blendRadiusM must be positive")
    return normalized({name: (spread + distances[name]) ** -3 for name in names}), min(distances.values())


def schema(species):
    quadruped = species in ("bulgasari", "fox_spirit")
    legs = {name: {"hip": None, "knee": None, "ankle": None, "toe": None,
                   "parent": "Chest" if name.startswith("Fore") else "Pelvis"}
            for name in LEGS} if quadruped else {}
    return {
        "schemaVersion": VERSION, "species": species,
        "sourceSha256": None, "landmarksReviewed": False, "regionsReviewed": False,
        "coordinateFrame": {"up": "+Z", "forward": [0, -1, 0], "units": "metres"},
        "root": [0, 0, 0], "rootBoneLengthM": .1,
        "joints": dict.fromkeys(("pelvis", "spine", "chest", "neck", "head", "nose", "mouth")) if quadruped else {},
        "serpent": None if quadruped else {"centrelineHeadToTail": [], "bodyBones": 24,
                                           "headTip": None, "mouthOrigin": None},
        "legs": legs,
        "tails": [],
        "regions": {"EXACT_IMPORTED_MESH_OBJECT_NAME": [
            {"id": "torso", "selector": {"all": True},
             "bones": ["Pelvis", "Spine", "Chest", "Neck", "Head"] if quadruped else
                      [f"Body_{i:02}" for i in range(1, 25)],
             "chainBlend": True, "blendRadiusM": .025},
            {"id": "head", "selector": {"indices": [], "ranges": []},
             "bones": ["Head"], "rigid": True}
        ]},
        "contactVertices": {},
        "weightSmoothIterations": 8,
        "animation": {"fps": 30, "walkStrideM": .20, "walkLiftM": .04,
                      "walkStanceFraction": .65, "walkBodyDropM": .015,
                      "serpentWaveDegreesPerBone": 2.0, "deathRollDegrees": 65,
                      "groundZ": 0.0, "amplitudeScale": 1.0},
        "limits": {"maximumTriangles": 15000, "maxInfluences": 4,
                   "maxAssignmentDistanceM": .75, "maxIkTargetErrorM": .025,
                   "maxEdgeStretchRatio": 2.5, "minEdgeCompressionRatio": .15,
                   "edgeLengthFloorM": .002, "maxGroundPenetrationM": .04,
                   "maxFootSoleSpreadM": .08, "maxLoopSeamM": .002,
                   "maxRoundtripPositionErrorM": .0001,
                   "maxRoundtripPoseErrorM": .001},
        "notes": ["DRAFT: fill landmarks from inspect/vertices-world.json after viewing the actual GLB.",
                  "Regions: last matching selector wins. ranges are inclusive [first,last].",
                  "Region bounds use Blender world metres: {min:[x,y,z],max:[x,y,z]}.",
                  "Separate each leg from torso and opposite limb. Separate nearby serpent coils.",
                  "tails: [{name:'Tail', parent:'Pelvis', points:[root,...,tip]}]; do not invent extra tails.",
                  "Serpent legs are optional actual limbs, parents Body_05 etc.; never add missing anatomy.",
                  "contactVertices: {Fore_L:[{object:'Mesh',indices:[...sole vertex ids...]}],...}.",
                  "Quadrupeds require all four explicit foot contact vertex sets.",
                  "Change thresholds only for documented measured scale/anatomy, never to hide failed deformation."]
    }


def bone_definitions(config):
    result = {}
    def bone(name, head, tail, parent=None, deform=True):
        require(name not in result, f"Duplicate bone {name}")
        head, tail = v3(head, name + ".head"), v3(tail, name + ".tail")
        require(norm(sub(head, tail)) > 1e-5, f"Zero-length bone {name}")
        require(parent is None or parent in result, f"Parent {parent} must precede {name}")
        result[name] = {"head": head, "tail": tail, "parent": parent, "deform": deform}
    root = v3(config["root"], "root")
    bone("Root", root, add(root, (0, 0, config["rootBoneLengthM"])), deform=False)
    if config["species"] in ("bulgasari", "fox_spirit"):
        joints = config["joints"]
        chain = [("Pelvis", "pelvis", "spine", "Root"), ("Spine", "spine", "chest", "Pelvis"),
                 ("Chest", "chest", "neck", "Spine"), ("Neck", "neck", "head", "Chest"),
                 ("Head", "head", "nose", "Neck")]
        for name, head, tail, parent in chain:
            bone(name, joints[head], joints[tail], parent)
        mouth = v3(joints["mouth"], "mouth")
        bone("MouthOrigin", mouth, add(mouth, mul(config["coordinateFrame"]["forward"], .05)), "Head", False)
        require(set(config["legs"]) == set(LEGS), "Quadruped requires four native limb chains")
    else:
        serp = config["serpent"]
        count = int(serp["bodyBones"])
        require(8 <= count <= 64, "Serpent chain must contain 8..64 bones")
        require(config["species"] != "cheongryong" or count == 24,
                "Existing CheongryongBodyFollow/TailSweep requires exactly Body_01..Body_24")
        points = resample([v3(p, "centreline") for p in serp["centrelineHeadToTail"]], count + 1)
        bone("Head", points[0], serp["headTip"], "Root")
        for index in range(count):
            bone(f"Body_{index + 1:02}", points[index], points[index + 1],
                 "Head" if index == 0 else f"Body_{index:02}")
        bone("TailTip", points[-1], add(points[-1], mul(unit(sub(points[-1], points[-2])), .05)),
             f"Body_{count:02}", False)
        mouth = v3(serp["mouthOrigin"], "mouthOrigin")
        bone("MouthOrigin", mouth, add(mouth, mul(config["coordinateFrame"]["forward"], .05)), "Head", False)
    for name, leg in config["legs"].items():
        require(name in LEGS, f"Unsupported limb name {name}")
        bone(name + "_Upper", leg["hip"], leg["knee"], leg["parent"])
        bone(name + "_Lower", leg["knee"], leg["ankle"], name + "_Upper")
        bone(name + "_Foot", leg["ankle"], leg["toe"], name + "_Lower")
    for tail in config["tails"]:
        points = [v3(p, tail["name"]) for p in tail["points"]]
        require(len(points) >= 2, "Tail needs at least two landmarks")
        for i, (a, b) in enumerate(zip(points, points[1:])):
            bone(f"{tail['name']}_{i + 1:02}", a, b,
                 tail["parent"] if i == 0 else f"{tail['name']}_{i:02}")
    return result


def verify_config(config, source_hash):
    require(config["schemaVersion"] == VERSION and config["species"] in SPECIES, "Wrong schema/species")
    require(config.get("sourceSha256") == source_hash, "Config source SHA does not match this GLB")
    require(config.get("landmarksReviewed") is True and config.get("regionsReviewed") is True,
            "Fit and review actual anatomical landmarks/weight regions before build")
    frame = config["coordinateFrame"]
    require(frame["up"] == "+Z" and frame["units"] == "metres", "Use Blender +Z world metres")
    forward = v3(frame["forward"], "forward")
    require(abs(norm(forward) - 1) < 1e-5 and abs(forward[2]) < 1e-5, "forward must be a horizontal unit vector")
    limits = config["limits"]
    require(limits["maxInfluences"] == 4, "Four-influence Unity skin contract is fixed")
    for key, value in limits.items():
        require(isinstance(value, (int, float)) and math.isfinite(value) and value > 0,
                f"Invalid limit {key}")
    require(config["animation"]["fps"] in (24, 30, 60), "Choose 24, 30, or 60 FPS")
    require(.5 <= config["animation"]["walkStanceFraction"] < .9, "Invalid stance fraction")
    require(0 < config["animation"]["amplitudeScale"] <= 1.5, "Invalid amplitudeScale")
    bones = bone_definitions(config)
    for object_name, regions in config["regions"].items():
        require(regions, "Mesh needs explicit weight regions: " + object_name)
        ids = set()
        for region in regions:
            require(region["id"] not in ids, "Duplicate region id")
            ids.add(region["id"])
            require(region["bones"] and len(set(region["bones"])) == len(region["bones"]), "Invalid region bones")
            for name in region["bones"]:
                require(name in bones and bones[name]["deform"], f"Unknown/nondeforming bone {name}")
            if region.get("chainBlend"):
                for a, b in zip(region["bones"], region["bones"][1:]):
                    require(bones[b]["parent"] == a, "chainBlend must list a contiguous parent chain")
    if config["species"] in ("bulgasari", "fox_spirit"):
        require(set(config["contactVertices"]) == set(LEGS), "Explicit four sole vertex selections required")
    return bones


def verify_approval(path, source_hash):
    receipt = read_json(path)
    require(receipt.get("status") == "PASS" and receipt.get("staticGeometryApproved") is True,
            "Static preview quality gate is not PASS; no rig build allowed")
    require(receipt.get("sourceSha256") == source_hash, "Static preview approves a different source")
    require(receipt.get("reviewer") and receipt.get("reviewedAtUtc") and receipt.get("notes"),
            "Static review must identify reviewer/time/actual findings")
    require(receipt.get("images"), "Static review must reference actually reviewed images")
    for image in receipt["images"]:
        image_path = Path(image["path"])
        if not image_path.is_absolute():
            image_path = Path(path).resolve().parent / image_path
        require(image_path.is_file() and sha(image_path) == image["sha256"], "Static review image SHA mismatch")
    return receipt


def blender():
    try:
        import bpy
        from mathutils import Matrix, Vector, Quaternion
    except ImportError as error:
        raise RuntimeError("inspect/build require Blender background --factory-startup --python") from error
    return bpy, Matrix, Vector, Quaternion


def import_source(path):
    bpy, _, _, _ = blender()
    require(path.suffix.lower() == ".glb" and path.is_file(), "Input must be an existing GLB")
    # Only the unsaved background process is cleared, never an existing .blend.
    require(bpy.app.background and not bpy.data.filepath, "Run an unsaved --background --factory-startup Blender process")
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str(path))
    meshes = sorted((obj for obj in bpy.context.scene.objects if obj.type == "MESH"), key=lambda o: o.name)
    require(meshes, "GLB contains no mesh")
    require(not any(obj.type == "ARMATURE" for obj in bpy.context.scene.objects), "Expected static approved Meshy output; pre-rigged input needs separate review")
    for obj in meshes:
        require(not obj.modifiers and not obj.data.shape_keys and not obj.vertex_groups and
                not obj.animation_data, "Input already has deformation/animation: " + obj.name)
        require(obj.matrix_world.determinant() > 0, "Mirrored or singular mesh transform requires separate export review")
        obj.data.calc_loop_triangles()
        require(len(obj.data.vertices) > 0 and len(obj.data.loop_triangles) > 0, "Empty source mesh")
    require(not bpy.data.actions, "Static input must not carry source animation")
    return meshes


def snapshot(obj):
    mesh = obj.data
    digest = hashlib.sha256()
    def numbers(fmt, values):
        for row in values:
            digest.update(struct.pack(fmt, *row))
    numbers("<3f", (tuple(v.co) for v in mesh.vertices))
    numbers("<i", ((loop.vertex_index,) for loop in mesh.loops))
    numbers("<3i", ((p.loop_start, p.loop_total, p.material_index) for p in mesh.polygons))
    for layer in mesh.uv_layers:
        digest.update(layer.name.encode("utf-8"))
        numbers("<2f", (tuple(uv.uv) for uv in layer.data))
    # Loop normals and smoothing must survive skinning too.
    numbers("<3f", (tuple(n.vector) for n in mesh.corner_normals))
    numbers("<?", ((p.use_smooth,) for p in mesh.polygons))
    material_names = [slot.material.name if slot.material else None for slot in obj.material_slots]
    digest.update(json.dumps(material_names).encode("utf-8"))
    numbers("<4f", (tuple(row) for row in obj.matrix_world))
    return {"sha256": digest.hexdigest(), "vertices": len(mesh.vertices),
            "triangles": len(mesh.loop_triangles), "polygons": len(mesh.polygons),
            "uvLayers": [uv.name for uv in mesh.uv_layers], "materials": material_names}


def world_vertices(obj, evaluated=False):
    bpy, _, _, _ = blender()
    if not evaluated:
        return [obj.matrix_world @ vertex.co for vertex in obj.data.vertices]
    evaluated_object = obj.evaluated_get(bpy.context.evaluated_depsgraph_get())
    mesh = evaluated_object.to_mesh()
    try:
        return [evaluated_object.matrix_world @ vertex.co for vertex in mesh.vertices]
    finally:
        evaluated_object.to_mesh_clear()


def bounds(points):
    return {"min": [min(p[i] for p in points) for i in range(3)],
            "max": [max(p[i] for p in points) for i in range(3)]}


def output_directory(path, fresh=False):
    path = Path(path).resolve()
    require("assets" not in [part.lower() for part in path.parts], "Candidate output must remain outside Unity Assets")
    if fresh:
        require(not path.exists() or not any(path.iterdir()), "Use a new candidate output directory; previous evidence is not overwritten")
    path.mkdir(parents=True, exist_ok=True)
    return path


def inspect(args):
    source = Path(args.input).resolve()
    source_hash = sha(source)
    out = output_directory(args.output, fresh=True)
    meshes = import_source(source)
    rows, coordinates = [], {}
    for obj in meshes:
        points = world_vertices(obj)
        row = {"object": obj.name, **snapshot(obj), "worldBounds": bounds(points),
               "matrixWorld": [list(row) for row in obj.matrix_world]}
        rows.append(row)
        coordinates[obj.name] = {"vertices": [list(p) for p in points],
                                 "edges": [list(e.vertices) for e in obj.data.edges]}
    save_json(out / "inspection.json", {"source": str(source), "sourceSha256": source_hash,
                                         "coordinateFrame": "+Z up, Blender world metres", "meshes": rows,
                                         "status": "STATIC_INSPECTION_ONLY_NO_RIG_OR_APPROVAL"})
    save_json(out / "vertices-world.json", coordinates)
    require(sha(source) == source_hash, "Source changed during inspection")
    return {"status": "INSPECTED", "objects": len(meshes), "output": str(out)}


def selector_indices(selector, points):
    allowed = {"all", "indices", "ranges", "bounds"}
    require(set(selector) <= allowed and selector, "Unknown/empty region selector")
    indices = set(range(len(points))) if selector.get("all") else set()
    for value in selector.get("indices", []):
        require(isinstance(value, int) and 0 <= value < len(points), "Invalid vertex index")
        indices.add(value)
    for first, last in selector.get("ranges", []):
        require(isinstance(first, int) and isinstance(last, int) and 0 <= first <= last < len(points), "Invalid inclusive vertex range")
        indices.update(range(first, last + 1))
    if "bounds" in selector:
        b = selector["bounds"]
        low, high = v3(b["min"], "bounds.min"), v3(b["max"], "bounds.max")
        require(all(a <= z for a, z in zip(low, high)), "Inverted bounds")
        indices.update(i for i, p in enumerate(points) if all(low[a] <= p[a] <= high[a] for a in range(3)))
    return indices


def add_skin(meshes, config, bones):
    bpy, Matrix, Vector, _ = blender()
    name = "Cheongryong_PrototypeRig" if config["species"] == "cheongryong" else config["species"] + "_Rig298"
    armature = bpy.data.armatures.new(name + "Skeleton")
    rig = bpy.data.objects.new(name, armature)
    bpy.context.scene.collection.objects.link(rig)
    bpy.ops.object.select_all(action="DESELECT")
    rig.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="EDIT")
    for bone_name, row in bones.items():
        bone = armature.edit_bones.new(bone_name)
        bone.head, bone.tail = row["head"], row["tail"]
        bone.use_deform = row["deform"]
        if row["parent"]:
            bone.parent = armature.edit_bones[row["parent"]]
        bone.use_connect = False
    bpy.ops.object.mode_set(mode="OBJECT")
    for bone in rig.pose.bones:
        bone.rotation_mode = "QUATERNION"
    require(set(config["regions"]) == {obj.name for obj in meshes}, "Regions must cover exact imported mesh names, with no stale names")
    weights_report, stored = [], {}
    for obj in meshes:
        points = world_vertices(obj)
        assignments = [None] * len(points)
        regions = config["regions"][obj.name]
        for region in regions:
            ids = selector_indices(region["selector"], points)
            require(ids, f"Region {obj.name}/{region['id']} selects no vertices")
            for index in ids:
                assignments[index] = region
        require(all(row is not None for row in assignments), "Uncovered vertices in " + obj.name)
        for bone_name, row in bones.items():
            if row["deform"]:
                obj.vertex_groups.new(name=bone_name)
        rows, max_distance = [], 0.0
        populations = Counter()
        for index, (point, region) in enumerate(zip(points, assignments)):
            weights, distance = skin_weights(point, region, bones)
            max_distance = max(max_distance, distance)
            populations[region["id"]] += 1
            rows.append(weights)
        # Smooth skin weights across native surface edges and coincident UV seams.
        # This modifies weights only, never vertices/topology/UVs. Rigid explicit
        # regions remain pinned, e.g. horn tips or contact soles after their review.
        adjacency = [set() for _ in points]
        for edge in obj.data.edges:
            a, b = edge.vertices
            adjacency[a].add(b)
            adjacency[b].add(a)
        coincident = {}
        for i, point in enumerate(points):
            coincident.setdefault(tuple(point), []).append(i)
        for ids in coincident.values():
            for i in ids:
                adjacency[i].update(j for j in ids if j != i)
        iterations = int(config.get("weightSmoothIterations", 0))
        require(0 <= iterations <= 40, "Weight smoothing must stay within 0..40 native-edge iterations")
        for _ in range(iterations):
            smoothed = []
            for i, neighbors in enumerate(adjacency):
                if not neighbors or assignments[i].get("rigid"):
                    smoothed.append(rows[i])
                    continue
                raw = {name: weight * .5 for name, weight in rows[i].items()}
                for j in neighbors:
                    for name, weight in rows[j].items():
                        raw[name] = raw.get(name, 0) + weight * .5 / len(neighbors)
                smoothed.append(normalized(raw))
            rows = smoothed
            # UV seams have distinct vertices and distinct surface valences.
            # Merely adding adjacency cannot keep them coincident after skinning.
            # Share weights exactly, without welding or editing any mesh data.
            for ids in coincident.values():
                if len(ids) < 2:
                    continue
                pinned = {tuple(assignments[i]["bones"]) for i in ids if assignments[i].get("rigid")}
                require(len(pinned) <= 1, "Conflicting rigid bones at one native seam")
                if pinned:
                    shared = {next(iter(pinned))[0]: 1.0}
                else:
                    shared = {}
                    for i in ids:
                        for bone_name, value in rows[i].items():
                            shared[bone_name] = shared.get(bone_name, 0) + value / len(ids)
                    shared = normalized(shared)
                for i in ids:
                    rows[i] = shared.copy()
        for index, weights in enumerate(rows):
            for bone_name, value in weights.items():
                obj.vertex_groups[bone_name].add([index], value, "REPLACE")
        require(max_distance <= config["limits"]["maxAssignmentDistanceM"],
                f"Weights too far from fitted bones on {obj.name}: {max_distance:.5f}m")
        stored[obj.name] = rows
        saved_world = obj.matrix_world.copy()
        obj.parent = rig
        obj.matrix_parent_inverse = Matrix.Identity(4)
        obj.matrix_world = saved_world
        modifier = obj.modifiers.new("Folklore298_Skin", "ARMATURE")
        modifier.object = rig
        modifier.use_vertex_groups = True
        modifier.use_deform_preserve_volume = False  # Unity four-weight linear skinning parity.
        weights_report.append({"object": obj.name, "regionVertices": dict(populations),
                               "identicalPositionSeamGroups": sum(len(ids) > 1 for ids in coincident.values()),
                               "maximumCoincidentWeightDifference": max((max(abs(rows[ids[0]].get(name, 0) - rows[i].get(name, 0)) for name in set(rows[ids[0]]) | set(rows[i])) for ids in coincident.values() for i in ids), default=0),
                               "nativeEdgeWeightSmoothingIterations": iterations,
                               "maximumAssignmentDistanceM": max_distance,
                               "maximumInfluences": max(len(row) for row in rows),
                               "maximumWeightSumError": max(abs(sum(row.values()) - 1) for row in rows)})
    bpy.context.view_layer.update()
    return rig, weights_report, stored


def assign(rig, action):
    rig.animation_data_create()
    rig.animation_data.action = action
    if action is not None and hasattr(action, "slots"):
        slot = next(iter(action.slots), None) or action.slots.new(id_type="OBJECT", name=rig.name)
        rig.animation_data.action_slot = slot


def reset(rig):
    _, Matrix, _, _ = blender()
    for bone in rig.pose.bones:
        bone.matrix_basis = Matrix.Identity(4)


def curves(action):
    if hasattr(action, "layers"):
        for layer in action.layers:
            for strip in layer.strips:
                for bag in strip.channelbags:
                    yield from bag.fcurves
    else:
        yield from action.fcurves


def make_actions(rig, meshes, config, bones):
    bpy, Matrix, Vector, Quaternion = blender()
    animation = config["animation"]
    scene = bpy.context.scene
    scene.render.fps = animation["fps"]
    scene.render.fps_base = 1
    rest = {bone.name: bone.matrix_local.copy() for bone in rig.data.bones}
    forward = Vector(config["coordinateFrame"]["forward"])
    right = forward.cross(Vector((0, 0, 1)))
    scale = animation["amplitudeScale"]
    rows = []
    maximum_ik_error = 0.0

    def rotate(name, axis, radians):
        if name not in rig.pose.bones:
            return
        local_axis = rest[name].to_quaternion().inverted() @ Vector(axis)
        pose = rig.pose.bones[name]
        pose.rotation_quaternion = Quaternion(local_axis, radians) @ pose.rotation_quaternion

    def offset(name, displacement):
        rig.pose.bones[name].location = rest[name].to_quaternion().inverted() @ Vector(displacement)

    def set_segment(name, start, end, reference_rotation=None):
        old = Vector(bones[name]["tail"]) - Vector(bones[name]["head"])
        base = rest[name].to_quaternion()
        if reference_rotation is not None:
            old = reference_rotation @ old
            base = reference_rotation @ base
        rotation = old.rotation_difference(end - start) @ base
        rig.pose.bones[name].matrix = Matrix.Translation(start) @ rotation.to_matrix().to_4x4()

    def solve_leg(name, requested, foot_delta=None):
        leg = config["legs"][name]
        parent = rig.pose.bones[leg["parent"]].matrix @ rest[leg["parent"]].inverted()
        hip = parent @ Vector(leg["hip"])
        rh, rk, rf = (Vector(leg[key]) for key in ("hip", "knee", "ankle"))
        a, b = (rk - rh).length, (rf - rk).length
        axis = requested - hip
        require(axis.length > 1e-6, "IK target coincides with hip")
        distance = max(abs(a - b) + 1e-6, min(a + b - 1e-6, axis.length))
        axis.normalize()
        target = hip + axis * distance
        native_axis = (rf - rh).normalized()
        parent_rotation = parent.to_quaternion()
        rest_axis = parent_rotation @ native_axis
        native_bend = rk - rh - native_axis * (rk - rh).dot(native_axis)
        bend = parent_rotation @ native_bend
        require(bend.length > 1e-5, "Native limb is collinear; explicit bent knee landmark is required")
        bend = rest_axis.rotation_difference(axis) @ bend
        bend -= axis * bend.dot(axis)
        bend.normalize()
        along = (a * a - b * b + distance * distance) / (2 * distance)
        knee = hip + axis * along + bend * math.sqrt(max(0, a * a - along * along))
        set_segment(name + "_Upper", hip, knee, parent_rotation)
        bpy.context.view_layer.update()
        set_segment(name + "_Lower", knee, target, parent_rotation)
        bpy.context.view_layer.update()
        foot_q = rest[name + "_Foot"].to_quaternion()
        if foot_delta is not None:
            foot_q = foot_delta @ foot_q
        rig.pose.bones[name + "_Foot"].matrix = Matrix.Translation(target) @ foot_q.to_matrix().to_4x4()
        return (target - requested).length

    recipes = [(name, seconds, loop, False) for name, (seconds, loop) in CLIPS.items()]
    if config["species"] in ("imugi", "cheongryong"):
        recipes += [("CR_Idle", 2.0, True, True), ("CR_HeadAttack_Anticipation", 1.2, False, True),
                    ("CR_TailSweep_Anticipation", 1.4, False, True)]
    body_names = [name for name in bones if name.startswith("Body_")]
    body_radius = {}
    death_head_radius = 0.0
    if body_names:
        widths = {name: [] for name in body_names}
        for obj in meshes:
            group_names = {g.index: g.name for g in obj.vertex_groups}
            for vertex, point in zip(obj.data.vertices, world_vertices(obj)):
                for group in vertex.groups:
                    name = group_names[group.group]
                    if group.weight >= .20:
                        if config["species"] == "cheongryong":
                            radial = Quaternion(forward, math.radians(165)) @ (point - Vector(bones[name]["head"]))
                            if name in widths:
                                widths[name].append(max(0, -radial.z))
                            if name == "Head":
                                death_head_radius = max(death_head_radius, -radial.z)
                        elif name in widths:
                            widths[name].append(abs(point.x - bones[name]["head"][0]))
        for name in body_names:
            values = sorted(widths[name])
            body_radius[name] = max(.003, values[-1 if config["species"] == "cheongryong" else int((len(values) - 1) * .95)] if values else .005)
    tail_names = [name for name in bones if any(name.startswith(tail["name"] + "_") for tail in config["tails"])]
    for name, seconds, loop, compatibility in recipes:
        action = bpy.data.actions.new(name)
        action.use_fake_user = True
        assign(rig, action)
        end = round(seconds * scene.render.fps)
        previous = {}
        for sample in range(end * 2 + 1):
            u = sample / (end * 2)
            frame = 1 + sample * .5
            reset(rig)
            sine = math.sin(u * math.tau)
            pulse = math.sin(u * math.pi) ** 2
            if compatibility:
                angle = .025 * sine if name == "CR_Idle" else (.24 if "HeadAttack" in name else .26) * pulse
                rotate("Head", (1, 0, 0) if "HeadAttack" in name else (0, 0, 1), angle)
            else:
                if name == "Idle":
                    rotate("Head", (0, 0, 1), .018 * sine * scale)
                    rotate("Chest", right, .008 * sine * scale)
                elif name == "Walk":
                    rotate("Head", right, .018 * math.sin(u * math.tau * 2) * scale)
                    rotate("Spine", forward, .012 * sine * scale)
                elif name == "Attack":
                    rotate("Neck", right, -.12 * pulse * scale)
                    rotate("Head", right, .28 * pulse * scale)
                    rotate("Chest", right, -.03 * pulse * scale)
                elif name == "Hit":
                    rotate("Head", (0, 0, 1), -.20 * pulse * scale)
                    rotate("Neck", right, .12 * pulse * scale)
                elif name == "Stun":
                    rotate("Head", right, -.10 + .012 * sine * scale)
                    rotate("Head", (0, 0, 1), .035 * sine * scale)
                elif name == "Death":
                    smooth = u * u * (3 - 2 * u)
                    if not body_names or config["species"] == "cheongryong":
                        rotate("Root", forward, math.radians(95) * smooth)
                        rotate("Head", right, -.12 * smooth * scale)
                        if config["species"] == "cheongryong":
                            for leg_name, leg in config["legs"].items():
                                sign = 1 if leg["hip"][0] > 0 else -1
                                rotate(leg_name + "_Upper", forward, -sign * math.radians(45) * smooth)
                    else:
                        # A long animal cannot rest on one rigidly rolled paw or
                        # on its raised neck while its tail hangs in mid-air. Settle
                        # a length-preserving chain onto its actual cross-section
                        # radii. Only bone transforms change; source mesh stays intact.
                        roll = math.radians(165 if config["species"] == "cheongryong" else 85) * smooth
                        rotate("Head", forward, roll)
                        original_heads = [Vector(bones[n]["head"]) for n in body_names]
                        original_heads.append(Vector(bones[body_names[-1]]["tail"]))
                        targets = []
                        for i, point in enumerate(original_heads):
                            radius = body_radius[body_names[min(i, len(body_names) - 1)]]
                            if config["species"] == "cheongryong":
                                radius = max(radius, death_head_radius * max(0, 1 - i / 4))
                            target = point.copy()
                            target.z = animation["groundZ"] + radius
                            targets.append(point.lerp(target, smooth))
                        offset("Head", targets[0] - original_heads[0])
                        bpy.context.view_layer.update()
                        solved = [targets[0]]
                        for i, body_name in enumerate(body_names):
                            length = (original_heads[i + 1] - original_heads[i]).length
                            # Interpolate segment directions, not absolute next
                            # positions: flattening a raised neck increases its
                            # horizontal reach and must not flip the following bone
                            # backward when it overtakes the old projected point.
                            direction = (targets[i + 1] - targets[i]).normalized()
                            solved.append(solved[-1] + direction * length)
                            bind_direction = Vector(bones[body_name]["tail"]) - Vector(bones[body_name]["head"])
                            if config["species"] == "cheongryong":
                                global_roll = Quaternion(forward, roll)
                                rotation = (global_roll @ bind_direction).rotation_difference(direction) @ global_roll @ rest[body_name].to_quaternion()
                            else:
                                rotation = Quaternion(-direction, roll) @ bind_direction.rotation_difference(direction) @ rest[body_name].to_quaternion()
                            # Derive the world orientation explicitly. Reading
                            # pose.matrix immediately after assigning it returns
                            # stale dependency-graph state and accumulates roll.
                            rig.pose.bones[body_name].matrix = Matrix.Translation(solved[-2]) @ rotation.to_matrix().to_4x4()
                            bpy.context.view_layer.update()
                if body_names and name != "Death":
                    # Generic snake motion moves the neck independently. The legacy
                    # hierarchy puts Body_01 below Head, so cancel inherited head
                    # rotation at the same neck anchor before authoring body waves.
                    # CR_* deliberately omit this channel: runtime BodyFollow owns it.
                    bpy.context.view_layer.update()
                    rig.pose.bones["Body_01"].matrix = rest["Body_01"].copy()
                if name != "Death":
                    for i, bone_name in enumerate(body_names):
                        amplitude = math.radians(animation["serpentWaveDegreesPerBone"])
                        amplitude *= (1 if name == "Walk" else .2) * scale
                        rotate(bone_name, (0, 0, 1), amplitude * math.sin(u * math.tau - i * .45))
                    for i, bone_name in enumerate(tail_names):
                        rotate(bone_name, (0, 0, 1), .035 * scale * math.sin(u * math.tau - i * .4))
                if config["legs"] and name != "Death":
                    if "Pelvis" in bones and name == "Walk":
                        offset("Pelvis", (0, 0, -animation["walkBodyDropM"]))
                    bpy.context.view_layer.update()
                    for leg_name, leg in config["legs"].items():
                        target = Vector(leg["ankle"])
                        if name == "Walk":
                            phases = {"Fore_L": 0, "Hind_R": .25, "Fore_R": .5, "Hind_L": .75}
                            phase = (u + phases[leg_name]) % 1
                            stance = animation["walkStanceFraction"]
                            stride = animation["walkStrideM"]
                            if phase < stance:
                                travel, lift = stride * (.5 - phase / stance), 0
                            else:
                                swing = (phase - stance) / (1 - stance)
                                smooth = swing * swing * (3 - 2 * swing)
                                travel = stride * (-.5 + smooth)
                                lift = animation["walkLiftM"] * math.sin(math.pi * swing) ** 1.35
                            target += forward * travel + Vector((0, 0, lift))
                        maximum_ik_error = max(maximum_ik_error, solve_leg(leg_name, target))
                elif config["legs"] and body_names and name == "Death" and config["species"] != "cheongryong":
                    smooth = u * u * (3 - 2 * u)
                    bpy.context.view_layer.update()
                    for leg_name, leg in config["legs"].items():
                        parent_delta = rig.pose.bones[leg["parent"]].matrix @ rest[leg["parent"]].inverted()
                        rh, rk, ra = (Vector(leg[key]) for key in ("hip", "knee", "ankle"))
                        reach = (rk - rh).length + (ra - rk).length
                        # Flex each leg alongside its own body attachment. Staying
                        # below 1/2 reach avoids pretending an extended paw is a
                        # valid dead-body floor support.
                        tucked = rh + forward * (reach * .025) + (ra - rh).normalized() * (reach * .90)
                        native_target = ra.lerp(tucked, smooth)
                        requested = parent_delta @ native_target
                        maximum_ik_error = max(maximum_ik_error, solve_leg(leg_name, requested, parent_delta.to_quaternion()))
                if name == "Death":
                    bpy.context.view_layer.update()
                    low = min(p.z for obj in meshes for p in world_vertices(obj, True))
                    # Presentation-only vertical collapse correction; actor root has no XZ animation.
                    offset("Root", (0, 0, animation["groundZ"] - low))
            bpy.context.view_layer.update()
            for pose in rig.pose.bones:
                if compatibility and pose.name != "Head":
                    continue
                q = pose.rotation_quaternion.copy()
                if pose.name in previous and q.dot(previous[pose.name]) < 0:
                    q.negate()
                    pose.rotation_quaternion = q
                previous[pose.name] = q
                for channel in ("location", "rotation_quaternion", "scale"):
                    pose.keyframe_insert(channel, frame=frame, group=pose.name)
        for curve in curves(action):
            for key in curve.keyframe_points:
                key.interpolation = "LINEAR"
        rows.append({"name": name, "seconds": end / scene.render.fps, "loop": loop,
                     "frames": [1, end + 1], "headOnlyCompatibility": compatibility,
                     "rootXZTranslation": False, "presentationOnly": True})
    assign(rig, None)
    reset(rig)
    bpy.context.view_layer.update()
    require(maximum_ik_error <= config["limits"]["maxIkTargetErrorM"],
            f"IK reach error {maximum_ik_error:.5f}m: adjust fitted landmarks/stride, do not hide clamp")
    return rows, maximum_ik_error


def validate_deformation(rig, meshes, config, actions, original):
    bpy, _, _, _ = blender()
    limits = config["limits"]
    by_name = {obj.name: obj for obj in meshes}
    edges = {obj.name: [(a, b, (original[obj.name][a] - original[obj.name][b]).length)
                       for a, b in (edge.vertices[:] for edge in obj.data.edges)
                       if (original[obj.name][a] - original[obj.name][b]).length >= limits["edgeLengthFloorM"]]
             for obj in meshes}
    for leg_name, entries in config["contactVertices"].items():
        require(leg_name in config["legs"] and entries, "Sole selections must match real limbs")
        count = 0
        for entry in entries:
            require(entry["object"] in by_name, "Unknown sole mesh")
            count += len(selector_indices({"indices": entry["indices"]}, original[entry["object"]]))
        require(count >= 3, "At least three sole vertices required per foot")
    rows = []
    coincident_pairs = {}
    for obj in meshes:
        groups = {}
        for i, point in enumerate(original[obj.name]):
            groups.setdefault(tuple(point), []).append(i)
        coincident_pairs[obj.name] = [(ids[0], i) for ids in groups.values() for i in ids[1:]]
    for clip in actions:
        assign(rig, bpy.data.actions[clip["name"]])
        start, end = clip["frames"]
        # All exported whole frames plus half-frame samples detect interpolation spikes.
        times = [start + i * .5 for i in range(round((end - start) * 2) + 1)]
        maximum_stretch, minimum_compression = 1.0, 1.0
        maximum_penetration, maximum_sole_spread, maximum_displacement = 0.0, 0.0, 0.0
        maximum_root_xz, maximum_body_motion = 0.0, 0.0
        maximum_seam_gap, worst_edge = 0.0, None
        first, last = None, None
        pose_samples = []
        for frame in times:
            # Clear channels absent from Head-only clips before evaluation.
            reset(rig)
            scene = bpy.context.scene
            scene.frame_set(int(frame), subframe=frame % 1)
            bpy.context.view_layer.update()
            positions = {obj.name: world_vertices(obj, True) for obj in meshes}
            raw_positions = positions
            if clip["headOnlyCompatibility"]:
                for bone in rig.pose.bones:
                    if bone.name.startswith("Body_"):
                        maximum_body_motion = max(maximum_body_motion, max(abs(bone.matrix_basis[i][j] - (1 if i == j else 0)) for i in range(4) for j in range(4)))
                # The actual stationary BodyFollow solve preserves bind body points
                # when Head only rotates around the first body's anchor. Emulate
                # that documented ownership for ground/skin QA, keep RAW clip
                # snapshots below to test FBX serialization independently.
                # All child local transforms are identity in these head-only clips.
                # Setting the first body world pose cancels inherited Head rotation
                # for the entire chain. Reassigning every child without evaluating
                # Blender's parent dependency between writes gives a false cascade.
                rig.pose.bones["Body_01"].matrix = rig.data.bones["Body_01"].matrix_local.copy()
                bpy.context.view_layer.update()
                positions = {obj.name: world_vertices(obj, True) for obj in meshes}
            require(all(math.isfinite(c) for points in positions.values() for p in points for c in p), "Nonfinite deformed position")
            for obj in meshes:
                points = positions[obj.name]
                require(len(points) == len(original[obj.name]), "Modifier changed topology")
                maximum_displacement = max(maximum_displacement, max((a - b).length for a, b in zip(points, original[obj.name])))
                maximum_penetration = max(maximum_penetration, config["animation"]["groundZ"] - min(p.z for p in points))
                maximum_seam_gap = max(maximum_seam_gap, max(((points[a] - points[b]).length for a, b in coincident_pairs[obj.name]), default=0))
                for a, b, length in edges[obj.name]:
                    ratio = (points[a] - points[b]).length / length
                    if ratio > maximum_stretch:
                        worst_edge = {"object": obj.name, "vertices": [a, b], "frame": frame, "nativeLength": length}
                    maximum_stretch = max(maximum_stretch, ratio)
                    minimum_compression = min(minimum_compression, ratio)
            for leg_name, entries in config["contactVertices"].items():
                sole = [positions[entry["object"]][i] for entry in entries for i in entry["indices"]]
                if clip["name"] != "Death":
                    maximum_sole_spread = max(maximum_sole_spread, max(p.z for p in sole) - min(p.z for p in sole))
            # Bone local Y follows the vertical Root bone; measure horizontal
            # translation in rig world axes, not the bone's local channel names.
            loc = rig.data.bones["Root"].matrix_local.to_quaternion() @ rig.pose.bones["Root"].location
            maximum_root_xz = max(maximum_root_xz, math.hypot(loc.x, loc.y))
            if first is None:
                first = positions
            last = positions
            if frame in (start, (start + end) / 2, end):
                pose_samples.append({"frame": frame, "positions": raw_positions})
        seam = max((a - b).length for name in first for a, b in zip(first[name], last[name])) if clip["loop"] else None
        row = {"clip": clip["name"], "sampleCount": len(times), "maxEdgeStretchRatio": maximum_stretch,
               "minEdgeCompressionRatio": minimum_compression, "maxGroundPenetrationM": max(0, maximum_penetration),
               "maxFootSoleSpreadM": maximum_sole_spread, "maxDisplacementM": maximum_displacement,
               "maxRootHorizontalTranslationM": maximum_root_xz, "loopSeamM": seam,
               "maxCompatibilityBodyLocalMotion": maximum_body_motion,
               "maxCoincidentSeamGapM": maximum_seam_gap, "worstStretchEdge": worst_edge}
        rows.append(row)
        require(maximum_seam_gap < 1e-6, "Separated native UV seam: " + str(row))
        require(maximum_stretch <= limits["maxEdgeStretchRatio"] and minimum_compression >= limits["minEdgeCompressionRatio"],
                f"{clip['name']} skin stretches/compresses too far: {row}")
        row["groundGateScope"] = "STATIONARY_BODYFOLLOW_EMULATION_NOT_RUNTIME_PASS" if clip["headOnlyCompatibility"] else "SAMPLED_SKIN"
        require(maximum_penetration <= limits["maxGroundPenetrationM"], f"{clip['name']} ground penetration {maximum_penetration:.5f}m")
        require(maximum_sole_spread <= limits["maxFootSoleSpreadM"], f"{clip['name']} foot contact deforms too far")
        require(seam is None or seam <= limits["maxLoopSeamM"], f"{clip['name']} loop seam {seam}m")
        require(maximum_root_xz < 1e-6 and maximum_body_motion < 1e-6, "Runtime ownership conflict in root/body channels: " + str(row))
        require(maximum_displacement > 1e-5, "Clip has no visible skin motion: " + clip["name"])
        clip["_roundtripSamples"] = pose_samples
        print("DEFORMATION " + json.dumps(row), flush=True)
    assign(rig, None)
    reset(rig)
    bpy.context.view_layer.update()
    return rows


def mesh_corner_signature(obj, digits=6, reference_positions=None):
    # Corner signatures survive legal FBX splits/reindexing at UV/normal seams.
    # A triangle multiset verifies topology + material + UV without assuming indices.
    mesh = obj.data
    mesh.calc_loop_triangles()
    uv = mesh.uv_layers.active
    from mathutils.kdtree import KDTree
    reference_positions = reference_positions if reference_positions is not None else world_vertices(obj)
    tree = KDTree(len(reference_positions))
    for index, point in enumerate(reference_positions):
        tree.insert(point, index)
    tree.balance()
    counts = Counter()
    for tri in mesh.loop_triangles:
        corners = []
        for vi, li in zip(tri.vertices, tri.loops):
            p = obj.matrix_world @ mesh.vertices[vi].co
            _, nearest, distance = tree.find(p)
            require(distance < BIND_TOLERANCE, "Corner is not on original bind surface")
            # Snap ONLY the validation key to the measured source vertex. Mesh data
            # are untouched. Decimal half-boundaries otherwise turn a 1e-7m FBX
            # roundoff into a false topological mismatch (e.g. -0.4687500).
            p = reference_positions[nearest]
            tex = tuple(uv.data[li].uv) if uv else ()
            corners.append(tuple(p) + tuple(round(x, digits) for x in tex))
        # Preserve winding up to cyclic start; exporter/importer must not reflect the source.
        cyclic = [tuple(corners[i:] + corners[:i]) for i in range(3)]
        counts[(tri.material_index, min(cyclic))] += 1
    return counts


def roundtrip(path, native, config, actions, bones):
    bpy, Matrix, Vector, _ = blender()
    from mathutils.kdtree import KDTree
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.render.fps = config["animation"]["fps"]
    bpy.context.scene.render.fps_base = 1
    bpy.ops.import_scene.fbx(filepath=str(path), use_anim=True)
    rigs = [obj for obj in bpy.context.scene.objects if obj.type == "ARMATURE"]
    require(len(rigs) == 1, "FBX roundtrip must contain exactly one armature")
    rig = rigs[0]
    assign(rig, None)
    reset(rig)
    bpy.context.view_layer.update()
    imported = {obj.name: obj for obj in bpy.context.scene.objects if obj.type == "MESH"}
    require(set(imported) == set(native), "Roundtrip mesh objects changed")
    require(set(rig.data.bones.keys()) == set(bones), "Roundtrip bone contract changed")
    for name, row in bones.items():
        parent = rig.data.bones[name].parent
        require((parent.name if parent else None) == row["parent"], "Roundtrip bone hierarchy changed: " + name)
    maximum_error, skin_error, maximum_pose_error = 0.0, 0.0, 0.0
    mappings, rows = {}, []
    for name, obj in imported.items():
        positions = world_vertices(obj, True)
        expected = native[name]["positions"]
        tree = KDTree(len(expected))
        for i, point in enumerate(expected):
            tree.insert(Vector(point), i)
        tree.balance()
        mapping = []
        for vertex, point in zip(obj.data.vertices, positions):
            _, closest, distance = tree.find(point)
            maximum_error = max(maximum_error, distance)
            group_weights = {obj.vertex_groups[group.group].name: group.weight for group in vertex.groups if group.weight > 1e-7}
            require(0 < len(group_weights) <= 4, "FBX lost four-weight skin")
            require(abs(sum(group_weights.values()) - 1) < 1e-5, "FBX weights no longer sum to one")
            # Coincident source vertices may have distinct UV seams and skin weights.
            choices = [index for _, index, _ in tree.find_range(point, BIND_TOLERANCE)]
            closest = min(choices or [closest], key=lambda index: sum(abs(group_weights.get(k, 0) - native[name]["weights"][index].get(k, 0)) for k in set(group_weights) | set(native[name]["weights"][index])))
            expected_weights = native[name]["weights"][closest]
            skin_error = max(skin_error, max((abs(group_weights.get(k, 0) - expected_weights.get(k, 0)) for k in set(group_weights) | set(expected_weights)), default=0))
            mapping.append(closest)
        mappings[name] = mapping
        # Compare the same precision at both ends; tolerances are separately measured above.
        signature = mesh_corner_signature(obj, reference_positions=expected)
        require(signature == native[name]["cornerSignature"], "FBX triangle winding/material/UV corner mapping changed: " + name)
        require(len(obj.data.uv_layers) == native[name]["uvLayerCount"], "FBX lost UV layer")
        rows.append({"object": name, "sourceVertices": len(expected), "importedVertices": len(positions),
                     "triangles": len(obj.data.loop_triangles), "triangleMaterialUvSignatureEqual": True})
    require(maximum_error <= config["limits"]["maxRoundtripPositionErrorM"], f"FBX bind shape error {maximum_error}m")
    require(skin_error < 1e-5, f"FBX weight error {skin_error}")
    for clip in actions:
        candidates = [action for action in bpy.data.actions if action.name == clip["name"] or action.name.endswith("|" + clip["name"])]
        require(len(candidates) == 1, f"FBX missing/ambiguous action {clip['name']}")
        assign(rig, candidates[0])
        for sample in clip["_roundtripSamples"]:
            reset(rig)
            frame = sample["frame"]
            bpy.context.scene.frame_set(int(frame), subframe=frame % 1)
            bpy.context.view_layer.update()
            for name, obj in imported.items():
                actual = world_vertices(obj, True)
                expected = sample["positions"][name]
                maximum_pose_error = max(maximum_pose_error, max((p - expected[i]).length for p, i in zip(actual, mappings[name])))
    require(maximum_pose_error <= config["limits"]["maxRoundtripPoseErrorM"], f"FBX pose serialization error {maximum_pose_error}m")
    return {"status": "TECHNICAL_ROUNDTRIP_PASS", "meshes": rows, "bones": len(bones),
            "maximumRestPositionErrorM": maximum_error, "maximumWeightError": skin_error,
            "maximumSampledPoseErrorM": maximum_pose_error, "actions": [clip["name"] for clip in actions],
            "unverified": ["Unity importer/Generic Avatar/clip selection", "terrain contacts/self-collision",
                           "silhouette and animation art review", "combat timing, actual runtime culling"]}


def build(args):
    bpy, _, _, _ = blender()
    source = Path(args.input).resolve()
    source_hash = sha(source)
    config = read_json(args.config)
    approval = verify_approval(args.approval, source_hash)
    bones = verify_config(config, source_hash)
    out = output_directory(args.output, fresh=True)
    (out / "builder-used.py").write_bytes(Path(__file__).read_bytes())
    save_json(out / "config-used.json", config)
    save_json(out / "approval-used.json", approval)
    report = {"schemaVersion": VERSION, "status": "STARTED", "species": config["species"],
              "source": str(source), "sourceSha256": source_hash, "configSha256": sha(args.config),
              "staticApprovalSha256": sha(args.approval), "staticReviewer": approval["reviewer"],
              "builderSha256": sha(__file__), "blenderVersion": bpy.app.version_string,
              "geometryPolicy": "Unchanged source local vertices, topology, UVs, normals, material assignments and bind world transforms",
              "limits": config["limits"]}
    try:
        meshes = import_source(source)
        before = {obj.name: snapshot(obj) for obj in meshes}
        original = {obj.name: world_vertices(obj) for obj in meshes}
        require(sum(row["triangles"] for row in before.values()) <= config["limits"]["maximumTriangles"], "Source exceeds configured Smart topology face budget; builder never decimates")
        rig, weights, stored = add_skin(meshes, config, bones)
        require(before == {obj.name: snapshot(obj) for obj in meshes}, "Adding rig changed source geometry/material/UV/bind transform")
        rest_error = max((a - b).length for obj in meshes for a, b in zip(original[obj.name], world_vertices(obj, True)))
        require(rest_error < BIND_TOLERANCE, f"Skinning changed bind silhouette by {rest_error}m")
        report.update(sourceMeshes=before, bones=bones, weights=weights, maximumBindDeformationM=rest_error)
        actions, ik_error = make_actions(rig, meshes, config, bones)
        report["maximumIkTargetErrorM"] = ik_error
        report["deformation"] = validate_deformation(rig, meshes, config, actions, original)
        require(before == {obj.name: snapshot(obj) for obj in meshes}, "Animation authoring mutated original mesh")
        report["actions"] = [{k: value for k, value in clip.items() if not k.startswith("_")} for clip in actions]
        report["bonePaths"] = {}
        for name, row in bones.items():
            path, parent = [name], row["parent"]
            while parent:
                path.insert(0, parent)
                parent = bones[parent]["parent"]
            report["bonePaths"][name] = rig.name + "/" + "/".join(path)
        native = {obj.name: {"positions": original[obj.name], "weights": stored[obj.name],
                             "cornerSignature": mesh_corner_signature(obj),
                             "uvLayerCount": len(obj.data.uv_layers)} for obj in meshes}
        scene = bpy.context.scene
        scene.frame_set(1)
        assign(rig, None)
        reset(rig)
        scene.unit_settings.system = "METRIC"
        scene.unit_settings.scale_length = 1
        bpy.context.view_layer.update()
        bpy.ops.wm.save_as_mainfile(filepath=str(out / "candidate.blend"))
        bpy.ops.object.select_all(action="DESELECT")
        rig.select_set(True)
        for obj in meshes:
            obj.select_set(True)
        bpy.context.view_layer.objects.active = rig
        bpy.ops.export_scene.fbx(filepath=str(out / "creature.fbx"), use_selection=True,
                                 object_types={"MESH", "ARMATURE"}, use_mesh_modifiers=False,
                                 add_leaf_bones=False, use_armature_deform_only=False,
                                 bake_anim=True, bake_anim_use_all_actions=True,
                                 bake_anim_use_nla_strips=False, bake_anim_use_all_bones=False,
                                 bake_anim_step=.5, bake_anim_simplify_factor=0,
                                 apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS",
                                 axis_forward="-Z", axis_up="Y", path_mode="COPY", embed_textures=True)
        imported = roundtrip(out / "creature.fbx", native, config, actions, bones)
        save_json(out / "roundtrip.json", imported)
        require(sha(source) == source_hash, "Protected source GLB changed")
        report.update(status="TECHNICAL_CANDIDATE_PASS_PENDING_VISUAL_AND_UNITY", sourceHashUnchanged=True,
                      outputSha256={p.name: sha(p) for p in (out / "candidate.blend", out / "creature.fbx")},
                      runtimeIntegration="Generic clips; Cheongryong uses CR_* with existing BodyFollow/TailSweep. Generic whole-body clips require those procedural components disabled for that state.",
                      unverified=imported["unverified"])
    except Exception as error:
        if "rig" in locals():
            bpy.ops.wm.save_as_mainfile(filepath=str(out / "FAILED-debug.blend"))
        report.update(status="FAILED_NOT_FOR_IMPORT", error=str(error), sourceHashUnchanged=sha(source) == source_hash)
        save_json(out / "rig-report.json", report)
        raise
    save_json(out / "rig-report.json", report)
    return {"status": report["status"], "output": str(out), "sourceHashUnchanged": True}


def render_candidate(args):
    """Candidate-only review scene. Render props never enter the saved derivative."""
    bpy, _, Vector, _ = blender()
    candidate = Path(args.candidate).resolve()
    require(candidate.name in ("candidate.blend", "FAILED-debug.blend") and "assets" not in [p.lower() for p in candidate.parts],
            "Review only a staged candidate.blend outside Assets")
    candidate_hash = sha(candidate)
    config = read_json(args.config)
    out = output_directory(args.output, fresh=True)
    bpy.ops.wm.open_mainfile(filepath=str(candidate))
    rig = next(obj for obj in bpy.context.scene.objects if obj.type == "ARMATURE")
    meshes = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
    assign(rig, None)
    reset(rig)
    bpy.context.view_layer.update()
    points = [p for obj in meshes for p in world_vertices(obj)]
    box = bounds(points)
    centre = Vector(tuple((a + b) / 2 for a, b in zip(box["min"], box["max"])))
    extent = max(b - a for a, b in zip(box["min"], box["max"]))
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.samples = 16
    scene.cycles.use_denoising = True
    scene.render.resolution_x, scene.render.resolution_y = 1200, 800
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    world = bpy.data.worlds.new("Rig298ReviewWorld")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs["Color"].default_value = (.12, .14, .17, 1)
    world.node_tree.nodes["Background"].inputs["Strength"].default_value = .45
    scene.world = world
    for label, offset, energy in [("Key", (1, -.9, 1.5), 130), ("Fill", (-.8, -.5, .6), 70), ("Rim", (.2, 1, 1.1), 100)]:
        light = bpy.data.lights.new(label, "AREA")
        light.energy, light.shape, light.size = energy * extent * extent, "DISK", extent
        obj = bpy.data.objects.new(label, light)
        scene.collection.objects.link(obj)
        obj.location = centre + Vector(offset) * extent
        obj.rotation_euler = (centre - obj.location).to_track_quat("-Z", "Y").to_euler()
    camera_data = bpy.data.cameras.new("ReviewCamera")
    camera = bpy.data.objects.new("ReviewCamera", camera_data)
    scene.collection.objects.link(camera)
    camera_data.type, camera_data.ortho_scale = "ORTHO", extent * 1.35
    if args.focus:
        centre = Vector([float(v) for v in args.focus.split(",")])
        require(len(centre) == 3 and args.ortho > 0, "Close-up needs xyz focus and positive ortho size")
        camera_data.ortho_scale = args.ortho
    camera.location = centre + Vector((1.8, -.45, .72)) * extent
    camera.rotation_euler = (centre - camera.location).to_track_quat("-Z", "Y").to_euler()
    scene.camera = camera
    bpy.ops.mesh.primitive_plane_add(size=extent * 200, location=(0, 0, config["animation"]["groundZ"] - .0001))
    floor = bpy.context.object
    floor.name = "REVIEW_ONLY_GROUND"
    mat = bpy.data.materials.new("ReviewGround")
    mat.diffuse_color = (.18, .20, .23, 1)
    mat.use_nodes = True
    mat.node_tree.nodes.get("Principled BSDF").inputs["Base Color"].default_value = mat.diffuse_color
    mat.node_tree.nodes.get("Principled BSDF").inputs["Roughness"].default_value = .9
    floor.data.materials.append(mat)
    captures = []
    requested_clips = args.clips.split(",") if args.clips else list(CLIPS)
    require(all(name in CLIPS for name in requested_clips), "Unknown review clip")
    for name in requested_clips:
        action = bpy.data.actions[name]
        assign(rig, action)
        reset(rig)
        a, b = action.frame_range
        frame = b if name == "Death" else a + (b - a) * (.25 if name == "Walk" else .5)
        scene.frame_set(int(frame), subframe=frame % 1)
        bpy.context.view_layer.update()
        path = out / (name + ".png")
        scene.render.filepath = str(path)
        bpy.ops.render.render(write_still=True)
        captures.append({"clip": name, "frame": frame, "path": path.name, "sha256": sha(path),
                         "eye": list(camera.location), "lookAt": list(centre),
                         "bounds": bounds([p for obj in meshes for p in world_vertices(obj, True)])})
    require(sha(candidate) == candidate_hash, "Candidate modified by review capture")
    save_json(out / "receipts.json", {"candidateSha256": candidate_hash,
                                      "status": "RENDERED_NOT_VISUALLY_APPROVED", "captures": captures})
    return {"status": "CLIP_STILLS_RENDERED", "clips": requested_clips, "output": str(out)}


def self_test():
    tests = []
    def check(condition, label):
        require(condition, "Self-test: " + label)
        tests.append(label)
    points = resample([(0, 0, 0), (0, 2, 0), (3, 2, 0)], 6)
    check(points == [(0, 0, 0), (0, 1, 0), (0, 2, 0), (1, 2, 0), (2, 2, 0), (3, 2, 0)], "Arc-length centreline resampling")
    check(segment((1, 2, 0), (0, 0, 0), (2, 0, 0)) == (2, .5), "Segment distance")
    weights = normalized({str(i): 1 / (i + 1) for i in range(9)})
    check(len(weights) == 4 and abs(sum(weights.values()) - 1) < 1e-12, "Four normalized influences")
    cfg = schema("cheongryong")
    cfg["serpent"].update(centrelineHeadToTail=[[0, 0, 1], [0, 6, 1]], headTip=[0, -1, 1], mouthOrigin=[0, -1, .9])
    bones = bone_definitions(cfg)
    check(len(bones) == 28 and bones["Body_01"]["parent"] == "Head" and bones["TailTip"]["parent"] == "Body_24", "Exact legacy Cheongryong body hierarchy")
    weights, _ = skin_weights((0, 3, 1), {"bones": [f"Body_{i:02}" for i in range(1, 25)], "chainBlend": True}, bones)
    numbers = [int(name[5:]) for name in weights]
    check(max(numbers) - min(numbers) <= 2, "Only neighboring body bones influence one chain region")
    check(selector_indices({"ranges": [[1, 2]], "indices": [3]}, [(0, 0, 0)] * 4) == {1, 2, 3}, "Explicit anatomical index selection")
    check(selector_indices({"bounds": {"min": [0, 0, 0], "max": [1, 1, 1]}}, [(0, 0, 0), (2, 0, 0)]) == {0}, "Native-world bounds selection")
    try:
        verify_config(schema("fox_spirit"), "unapproved")
    except ValueError:
        check(True, "Unreviewed/mismatched source config rejected")
    else:
        check(False, "Unreviewed config rejection")
    return {"status": "MATH_AND_CONTRACT_TESTS_PASS", "checks": tests,
            "actualMeshRiggingTested": False, "note": "No creature mesh or static preview is available; no synthetic model substituted."}


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    commands = parser.add_subparsers(dest="command", required=True)
    template = commands.add_parser("template", help="Write an unapproved anatomical config skeleton")
    template.add_argument("--species", choices=SPECIES, required=True)
    template.add_argument("--output", required=True)
    commands.add_parser("self-test", help="Pure mathematical/contract checks; no generated model")
    render = commands.add_parser("render", help="Six stills from candidate only; never resaves its geometry")
    render.add_argument("--candidate", required=True)
    render.add_argument("--config", required=True)
    render.add_argument("--output", required=True)
    render.add_argument("--clips", help="Comma-separated clip stills, default all six")
    render.add_argument("--focus", help="Optional native-world xyz close-up target")
    render.add_argument("--ortho", type=float, default=.25)
    for command in ("inspect", "build"):
        subparser = commands.add_parser(command)
        subparser.add_argument("--input", required=True)
        subparser.add_argument("--output", required=True)
        if command == "build":
            subparser.add_argument("--config", required=True)
            subparser.add_argument("--approval", required=True)
    args = parser.parse_args(argv)
    if args.command == "template":
        target = Path(args.output).resolve()
        require(not target.exists(), "Do not overwrite an existing anatomical config")
        require("assets" not in [part.lower() for part in target.parts], "Keep staging configs outside Unity Assets")
        save_json(target, schema(args.species))
        result = {"status": "DRAFT_CONFIG_WRITTEN_NOT_APPROVED", "path": str(target)}
    elif args.command == "self-test":
        result = self_test()
    elif args.command == "inspect":
        result = inspect(args)
    elif args.command == "render":
        result = render_candidate(args)
    else:
        result = build(args)
    print(json.dumps(result, ensure_ascii=False, allow_nan=False), flush=True)


if __name__ == "__main__":
    arguments = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
    main(arguments)
