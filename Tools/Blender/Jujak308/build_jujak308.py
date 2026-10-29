"""Jujak308 (주작) boss body: Meshy parts -> assembled, graded, rigged (Generic), verified, exported.

D308-149 D1 / design_v1.md section 4-5.  One reproducible headless script.

  blender --background --factory-startup --python Tools/Blender/Jujak308/build_jujak308.py -- [--stages a,b,c] [--no-grade]

stages (in order; default = all):
  load      read the four Meshy GLBs, weld, split plates, drop rivets, decimate to the tri budget
  grade     ink masks (wing / tail feather tips) -> system python grade_jujak308.py -> new 2K textures in the Unity folder
  assemble  scale to 5 m, place body / wings (half-open bind) / 5 tail feathers / plates, build materials
  rig       Generic armature (Root, Pelvis, Spine, Neck, Head, Leg x2, Wing x2, Tail chain + 5 feather chains)
  weights   body: Blender automatic weights on the body bones only, cleaned; wings / feathers / plates: own rigid or banded weights
  actions   four verification poses as actions (wing spread, wing fold, neck strike, step) - not final animation
  verify    stretch / compression / plate rigidity / ground / influence / emission / tri count -> JSON
  previews  Workbench renders (front, side, back, 3/4, poses, colour next to jj_side4.png)
  export    FBX + .blend

Never modifies the Meshy originals.  No Cycles, no EEVEE (the PC has blue-screened under GPU load): Workbench only.
Units: metres, Z up, creature faces -Y in Blender (= +Z in Unity after the project's standard FBX axes -Z/Y).
"""
import argparse
import json
import math
import os
import random
import subprocess
import sys
import time
from pathlib import Path

import bpy
import numpy as np
from mathutils import Euler, Matrix, Quaternion, Vector

T0 = time.time()
ROOT = Path(__file__).resolve().parents[3]
SRC = ROOT / "Art" / "Characters" / "EnemyBodies308" / "Source"
WORK = ROOT / "Art" / "Characters" / "EnemyBodies308" / "Jujak308"
GAME = ROOT / "Oheangbu" / "Assets" / "_Project" / "Art" / "Characters" / "Creatures308" / "Jujak308"
CONCEPT = ROOT / "Art" / "Jeokro308" / "Concepts"
PREV = WORK / "previews"
SCRATCH = WORK / "work"
GRADER = Path(__file__).resolve().parent / "grade_jujak308.py"

ALL_STAGES = ["load", "grade", "assemble", "rig", "actions", "weights", "verify", "previews", "export"]

# ---------------------------------------------------------------- tunables (final metres unless noted)
HEIGHT = 5.0                       # crest top to ground
TRI_BUDGET = 45000
WING_TARGET_TRIS = 5200            # per wing after decimation
FEATHER_TARGET_TRIS = 1000         # per tail feather
WING_REACH = 4.7                   # shoulder joint to wing tip (m) -> full spread ~ 2*(0.9 + 4.7) ~ 11 m
WING_DROOP_DEG = 36.0              # bind pose: half-open, tips drooped below horizontal
WING_SWEEP_DEG = 34.0              # bind pose: tips swept back
FEATHER_LEN = 4.7                  # central tail feather (m)
FEATHER_YAW = [14.0, 7.0, 0.0, -7.0, -14.0]
FEATHER_LEN_VAR = [0.86, 0.95, 1.0, 0.93, 0.84]
FEATHER_DROOP = 13.0
FOLD_SIGN = float(os.environ.get("JJ_FOLD_SIGN", "1"))
FOLD_ELBOW = float(os.environ.get("JJ_FOLD_ELBOW", "125"))      # in-plane Z-fold: forearm swings back by this, hand returns by FOLD_WRIST
FOLD_WRIST = float(os.environ.get("JJ_FOLD_WRIST", "20"))
FEATHER_ROLL = [-34.0, -17.0, 0.0, 17.0, 34.0]   # vanes tilt outward so the ribbons read from the side as well as from behind
FEATHER_ROOT_DX = [0.28, 0.14, 0.0, -0.14, -0.28]
RNG = random.Random(308149)

# ---------------------------------------------------------------- helpers


def log(*a):
    print("JJ", f"[{time.time() - T0:6.1f}s]", *a, flush=True)


class MD:
    """Plain numpy mesh: co (n,3), f (m,3), uv (m,3,2)."""

    def __init__(self, co, f, uv):
        self.co = np.asarray(co, dtype=np.float64)
        self.f = np.asarray(f, dtype=np.int64)
        self.uv = np.asarray(uv, dtype=np.float64)

    def copy(self):
        return MD(self.co.copy(), self.f.copy(), self.uv.copy())

    def transformed(self, M):
        M = np.array(M)
        return MD(self.co @ M[:3, :3].T + M[:3, 3], self.f.copy(), self.uv.copy())

    def mirrored_x(self):
        co = self.co.copy()
        co[:, 0] *= -1
        return MD(co, self.f[:, ::-1].copy(), self.uv[:, ::-1].copy())

    def subset(self, face_mask):
        f = self.f[face_mask]
        used, inv = np.unique(f, return_inverse=True)
        return MD(self.co[used], inv.reshape(-1, 3), self.uv[face_mask])

    def bbox(self):
        return self.co.min(0), self.co.max(0)

    @staticmethod
    def join(parts):
        co, f, uv, off = [], [], [], 0
        for p in parts:
            co.append(p.co)
            f.append(p.f + off)
            uv.append(p.uv)
            off += len(p.co)
        return MD(np.concatenate(co), np.concatenate(f), np.concatenate(uv))


def read_mesh(ob, weld=True):
    """Blender mesh object -> MD in world space (all-triangle meshes only)."""
    me = ob.data
    n = len(me.vertices)
    co = np.empty(n * 3)
    me.vertices.foreach_get("co", co)
    co = co.reshape(n, 3)
    mw = np.array(ob.matrix_world)
    co = co @ mw[:3, :3].T + mw[:3, 3]
    npoly = len(me.polygons)
    lt = np.empty(npoly, dtype=np.int64)
    me.polygons.foreach_get("loop_total", lt)
    assert (lt == 3).all(), "non-triangle polygons in " + ob.name
    f = np.empty(npoly * 3, dtype=np.int64)
    me.polygons.foreach_get("vertices", f)
    f = f.reshape(-1, 3)
    uvname = me.uv_layers[0].name
    uv = np.empty(npoly * 3 * 2)
    me.attributes[uvname].data.foreach_get("vector", uv)
    uv = uv.reshape(-1, 3, 2)
    if weld:
        key = np.round(co / 1e-6).astype(np.int64)
        _, first, inv = np.unique(key, axis=0, return_index=True, return_inverse=True)
        inv = inv.reshape(-1)
        co = co[first]
        f = inv[f]
        ok = (f[:, 0] != f[:, 1]) & (f[:, 1] != f[:, 2]) & (f[:, 0] != f[:, 2])
        f, uv = f[ok], uv[ok]
    return MD(co, f, uv)


def make_object(name, md, collection=None):
    me = bpy.data.meshes.new(name)
    me.from_pydata(md.co.tolist(), [], md.f.tolist())
    me.update()
    me.uv_layers.new(name="UVMap")
    me.attributes["UVMap"].data.foreach_set("vector", md.uv.reshape(-1))
    me.polygons.foreach_set("use_smooth", [True] * len(me.polygons))
    me.update()
    ob = bpy.data.objects.new(name, me)
    (collection or bpy.context.scene.collection).objects.link(ob)
    return ob


def labels_from_faces(f, n):
    """Connected-component label per vertex (union-find)."""
    parent = np.arange(n)

    def find(a):
        while parent[a] != a:
            parent[a] = parent[parent[a]]
            a = parent[a]
        return a

    for a, b, c in f:
        ra, rb, rc = find(a), find(b), find(c)
        if ra != rb:
            parent[rb] = ra
        rc = find(c)
        ra = find(a)
        if ra != rc:
            parent[rc] = ra
    lab = np.array([find(i) for i in range(n)])
    _, lab = np.unique(lab, return_inverse=True)
    return lab


def tri_count(md_or_ob):
    if isinstance(md_or_ob, MD):
        return len(md_or_ob.f)
    me = md_or_ob.data
    me.calc_loop_triangles()
    return len(me.loop_triangles)


def decimate_md(md, target_tris, name="dec"):
    if len(md.f) <= target_tris:
        return md
    ob = make_object(name, md)
    mod = ob.modifiers.new("dec", "DECIMATE")
    mod.decimate_type = "COLLAPSE"
    mod.ratio = max(0.02, min(1.0, target_tris / len(md.f)))
    mod.use_collapse_triangulate = True
    dg = bpy.context.evaluated_depsgraph_get()
    ev = ob.evaluated_get(dg)
    me2 = bpy.data.meshes.new_from_object(ev, preserve_all_data_layers=True, depsgraph=dg)
    ob2 = bpy.data.objects.new(name + "_ev", me2)
    bpy.context.scene.collection.objects.link(ob2)
    out = read_mesh(ob2, weld=False)
    for o in (ob, ob2):
        me = o.data
        bpy.data.objects.remove(o)
        bpy.data.meshes.remove(me)
    return out


def rotm(axis, deg):
    return Matrix.Rotation(math.radians(deg), 4, axis)


def scalem(s):
    return Matrix.Scale(s, 4)


def transm(v):
    return Matrix.Translation(Vector(v))


# ---------------------------------------------------------------- state shared across stages
S = {}


def purge_import_junk():
    """The glTF importer leaves its own materials + packed 4K images behind (they would be saved into the .blend)."""
    for m in list(bpy.data.materials):
        if not m.name.startswith("M_Jujak308"):
            bpy.data.materials.remove(m)
    for im in list(bpy.data.images):
        if not im.name.startswith("T_Jujak308") and im.name != "Render Result":
            bpy.data.images.remove(im)


# ================================================================= stage: load
def stage_load():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    PREV.mkdir(parents=True, exist_ok=True)
    SCRATCH.mkdir(parents=True, exist_ok=True)
    src = {}
    for name in ("body", "wing", "tail", "plates"):
        before = set(bpy.data.objects)
        bpy.ops.import_scene.gltf(filepath=str(SRC / f"jujak_{name}" / "source.glb"))
        new = [o for o in bpy.data.objects if o not in before]
        meshes = [o for o in new if o.type == "MESH"]
        assert len(meshes) == 1, (name, [o.name for o in new])
        src[name] = read_mesh(meshes[0])
        for o in new:
            d = o.data
            bpy.data.objects.remove(o)
            if d is not None and hasattr(d, "users") and d.users == 0:
                try:
                    (bpy.data.meshes if o.type == "MESH" else bpy.data.armatures).remove(d)
                except Exception:
                    pass
        log("loaded", name, "verts", len(src[name].co), "tris", len(src[name].f))
    S["src"] = src
    purge_import_junk()

    # ---- plates: islands -> four pieces; drop rivets / eyelets
    pl = src["plates"]
    lab = labels_from_faces(pl.f, len(pl.co))
    face_lab = lab[pl.f[:, 0]]
    nlab = lab.max() + 1
    info = []
    for i in range(nlab):
        m = face_lab == i
        if not m.any():
            continue
        v = pl.co[np.unique(pl.f[m])]
        mn, mx = v.min(0), v.max(0)
        info.append((i, int(m.sum()), (mn + mx) / 2, mx - mn))
    # piece split by island centroid x (native plate sheet: shoulder | neck | chest | shin, left to right)
    SPLIT = (-0.155, 0.047, 0.335)
    piece_of = {}
    drop = set()
    for i, nf, c, d in info:
        piece = int(np.searchsorted(SPLIT, c[0]))
        if max(d) < 0.050 or nf < 40:               # rivets, eyelets, cord loops
            drop.add(i)
        piece_of[i] = piece
    log("plate islands", len(info), "dropped", len(drop), "faces dropped", sum(nf for i, nf, c, d in info if i in drop))
    names = ["shoulder", "neck", "chest", "shin"]
    pieces = {}
    for p, nm in enumerate(names):
        mask = np.array([(piece_of[l] == p) and (l not in drop) for l in face_lab])
        pieces[nm] = pl.subset(mask)
        mn, mx = pieces[nm].bbox()
        log("plate piece", nm, "tris", len(pieces[nm].f), "bbox", np.round(mn, 3), np.round(mx, 3))
    S["plates"] = pieces

    # ---- decimation to the tri budget
    S["wing"] = decimate_md(src["wing"], WING_TARGET_TRIS, "wing_dec")
    S["tail"] = decimate_md(src["tail"], FEATHER_TARGET_TRIS, "tail_dec")
    S["body"] = src["body"]
    log("tris: body", len(S["body"].f), "wing", len(S["wing"].f), "feather", len(S["tail"].f),
        "plates", {k: len(v.f) for k, v in pieces.items()})


# ================================================================= wing classification (native space)
# native wing landmarks (x, z) measured on the Meshy wing (span along X, shoulder at -X, leading edge +Z)
NS = np.array([-0.47, 0.0, 0.16])      # shoulder
NE = np.array([-0.15, 0.0, 0.15])      # elbow
NW = np.array([0.18, 0.0, 0.12])       # wrist (primaries fan from here)
NT = np.array([0.50, 0.0, 0.10])       # tip reference


def _ss(a, b, v):
    t = np.clip((v - a) / (b - a), 0, 1)
    return t * t * (3 - 2 * t)


WING_RIDGE_Z = 0.16                    # native leading-edge ridge height
PRIM_EDGES = (12.0, -10.0, -34.0)      # band boundaries (degrees about the wrist), top primary first
PRIM_BONE_ANGLES = (24.0, 1.0, -22.0, -55.0)


def wing_vertex(x, z):
    """Bone-role weights + ink for one native wing point (x along the span, z toward the leading edge)."""
    dx, dz = x - NW[0], z - NW[2]
    r = math.hypot(dx, dz)
    th = math.degrees(math.atan2(dz, dx))
    right = float(_ss(-0.06, 0.0, dx))
    root = float(_ss(0.03, 0.14, r))
    F = right * root                   # primary-fan share
    Wr = right * (1 - root)            # wrist hub
    L = 1 - right
    below = float(_ss(0.015, 0.045, WING_RIDGE_Z - z))
    s2 = float(_ss(NE[0] - 0.10, NE[0] + 0.10, x))        # secondaries: inner ones ride the upper arm, outer ones the forearm
    w = {}
    # arm bones along the ridge (shoulder / elbow / wrist by x); wide blends so a deep elbow fold spreads over the crease
    e = float(_ss(NE[0] - 0.11, NE[0] + 0.11, x))
    wr = float(_ss(NW[0] - 0.09, NW[0] + 0.09, x))
    arm = {"Shoulder": 1 - e, "Elbow": e * (1 - wr), "Wrist": e * wr}
    for k, v in arm.items():
        w[k] = w.get(k, 0) + L * (1 - below) * v
    w["Sec1"] = w.get("Sec1", 0) + L * below * (1 - s2)
    w["Sec2"] = w.get("Sec2", 0) + L * below * s2
    w["Wrist"] = w.get("Wrist", 0) + Wr
    bands = []
    prev = None
    soft = 12.0
    for k in range(4):
        hi = _ss(PRIM_EDGES[k] - soft, PRIM_EDGES[k] + soft, th) if k < 3 else 0.0   # above edge k
        # membership in band k: below edge k-1, above edge k
        up = (1 - _ss(PRIM_EDGES[k - 1] - soft, PRIM_EDGES[k - 1] + soft, th)) if k > 0 else 1.0
        dn = _ss(PRIM_EDGES[k] - soft, PRIM_EDGES[k] + soft, th) if k < 3 else 1.0
        bands.append(float(up * dn))
    tot = sum(bands) or 1.0
    for k in range(4):
        w[f"Prim{k + 1}"] = F * bands[k] / tot
    # ink: dipped tips (fan arcs beyond r ~ 0.30; hanging secondaries beyond ~0.27 below the ridge)
    ink_fan = float(_ss(0.27, 0.40, r)) * right
    ink_sec = float(_ss(0.25, 0.34, WING_RIDGE_Z - z)) * L * below
    ink = max(ink_fan, ink_sec)
    return {k: v for k, v in w.items() if v > 1e-4}, ink


def classify_wing(md):
    """Per-vertex role weights + ink for the native wing mesh."""
    n = len(md.co)
    weights, ink = [], np.zeros(n)
    for i in range(n):
        w, k = wing_vertex(md.co[i][0], md.co[i][2])
        s = sum(w.values())
        weights.append({a: b / s for a, b in w.items()})
        ink[i] = k
    # Feather blades (everything outside the wrist hub and the ridge strip) move as one piece: every vertex of a connected
    # blade takes the analytic weights of the blade's centroid, so a bone boundary never cuts through a feather.
    x, z = md.co[:, 0], md.co[:, 2]
    hub = (np.hypot(x - NW[0], z - NW[2]) < 0.14) | (z > WING_RIDGE_Z - 0.035)
    keep = ~hub[md.f].any(axis=1)                  # faces made only of blade vertices
    sub = md.f[keep]
    parent = np.arange(n)

    def find(a):
        while parent[a] != a:
            parent[a] = parent[parent[a]]
            a = parent[a]
        return a
    for a, b, c in sub:
        ra = find(a)
        for v in (b, c):
            rv = find(v)
            if rv != ra:
                parent[rv] = ra
    roots = np.array([find(i) for i in range(n)])
    nblade = 0
    for r_ in np.unique(roots[~hub]):
        idx = np.where((roots == r_) & ~hub)[0]
        cx, cz = x[idx].mean(), z[idx].mean()
        w, _ = wing_vertex(cx, cz)
        s = sum(w.values())
        w = {a: b / s for a, b in w.items()}
        for i in idx:
            weights[i] = dict(w)
        nblade += 1
    log("wing weights: vertices", n, "blades", nblade, "hub verts", int(hub.sum()), "ink>0.5 share", round(float((ink > 0.5).mean()), 3))
    return weights, ink, dict()


def tail_axes(md):
    """Tip end of the native feather (long axis = Y): the end that tapers harder is the tip."""
    y = md.co[:, 1]
    x = md.co[:, 0]
    def width(m):
        return float(x[m].max() - x[m].min()) if m.any() else 0.0
    wp, wm = width(y > 0.38), width(y < -0.38)
    # the Meshy ribbon is near-symmetric (end widths differ by ~1 mm in native units): the +Y end is the tip by convention
    return 1.0, float(wp - wm)


def raster_mask(md, per_vertex, size=2048, dilate=4):
    """Rasterise per-vertex scalar into the UV layout (PIL row order: top first)."""
    img = np.zeros((size, size), dtype=np.float32)
    cov = np.zeros((size, size), dtype=bool)
    for fi in range(len(md.f)):
        val = per_vertex[md.f[fi]]
        uv = md.uv[fi]
        px = uv[:, 0] * (size - 1)
        py = (1 - uv[:, 1]) * (size - 1)
        x0, x1 = int(max(0, np.floor(px.min()))), int(min(size - 1, np.ceil(px.max())))
        y0, y1 = int(max(0, np.floor(py.min()))), int(min(size - 1, np.ceil(py.max())))
        if x1 < x0 or y1 < y0:
            continue
        gx, gy = np.meshgrid(np.arange(x0, x1 + 1), np.arange(y0, y1 + 1))
        d = (py[1] - py[2]) * (px[0] - px[2]) + (px[2] - px[1]) * (py[0] - py[2])
        if abs(d) < 1e-9:
            continue
        a = ((py[1] - py[2]) * (gx - px[2]) + (px[2] - px[1]) * (gy - py[2])) / d
        b = ((py[2] - py[0]) * (gx - px[2]) + (px[0] - px[2]) * (gy - py[2])) / d
        c = 1 - a - b
        m = (a >= -0.02) & (b >= -0.02) & (c >= -0.02)
        v = a * val[0] + b * val[1] + c * val[2]
        sub = img[y0:y1 + 1, x0:x1 + 1]
        sub[m] = np.maximum(sub[m], v[m])
        cov[y0:y1 + 1, x0:x1 + 1] |= m
    for _ in range(dilate):                       # bleed past UV borders so mips / seams stay dark
        grown = img.copy()
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            sh = np.roll(np.roll(img, dy, 0), dx, 1)
            grown = np.where(~cov & (sh > grown), sh, grown)
        img = grown
        cov = cov | (img > 0)
    return img


# ================================================================= stage: grade
def stage_grade(no_grade=False):
    md_wing = S["wing"]
    S["wing_w"], S["wing_ink"], S["wing_stats"] = classify_wing(md_wing)
    tip_sign, cy = tail_axes(S["tail"])
    S["tail_tip_sign"] = tip_sign
    log("tail feather: vane centroid y", round(cy, 3), "-> tip at", "+Y" if tip_sign > 0 else "-Y")
    if no_grade and (GAME / "T_Jujak308_Body_base.png").exists():
        log("grade skipped (--no-grade)")
        return
    SCRATCH.mkdir(parents=True, exist_ok=True)
    GAME.mkdir(parents=True, exist_ok=True)
    y = S["tail"].co[:, 1] * tip_sign
    u = np.clip((y + 0.5) / 1.0, 0, 1)
    tail_ink = np.clip((u - 0.74) / 0.22, 0, 1) ** 1.1
    for nm, md, pv in (("wing", md_wing, S["wing_ink"]), ("tail", S["tail"], tail_ink)):
        m = raster_mask(md, pv)
        arr = (np.clip(m, 0, 1) * 255).astype(np.uint8)
        im = bpy.data.images.new("m", 2048, 2048, alpha=False, float_buffer=False)
        # write grey PNG through Blender (no PIL here): bottom-up rows
        rgba = np.repeat(arr[::-1, :, None].astype(np.float32) / 255.0, 4, axis=2)
        rgba[..., 3] = 1.0
        im.pixels.foreach_set(rgba.reshape(-1))
        im.filepath_raw = str(SCRATCH / f"ink_{nm}.png")
        im.file_format = "PNG"
        im.save()
        bpy.data.images.remove(im)
        log("ink mask", nm, "covered", float((m > 0.5).mean()))
    py = os.environ.get("JUJAK_PYTHON", "python")
    r = subprocess.run([py, str(GRADER), "--out", str(GAME), "--masks", str(SCRATCH)], capture_output=True, text=True)
    for line in r.stdout.splitlines():
        log(line)
    if r.returncode != 0:
        log("GRADER STDERR", r.stderr[-2000:])
        raise RuntimeError("texture grade failed")


# ================================================================= materials
def make_material(part):
    P = part.capitalize()
    mat = bpy.data.materials.new(f"M_Jujak308_{P}")
    mat.use_nodes = True
    nt = mat.node_tree
    for n in list(nt.nodes):
        nt.nodes.remove(n)
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    bsdf.inputs["Metallic"].default_value = 0.0
    bsdf.inputs["Emission Strength"].default_value = 0.0
    bsdf.inputs["Emission Color"].default_value = (0, 0, 0, 1)
    bsdf.inputs["Roughness"].default_value = 0.6

    def tex(fname, cs, loc):
        n = nt.nodes.new("ShaderNodeTexImage")
        p = GAME / fname
        if p.exists():
            n.image = bpy.data.images.load(str(p), check_existing=True)
            n.image.colorspace_settings.name = cs
        n.location = loc
        return n

    tb = tex(f"T_Jujak308_{P}_base.png", "sRGB", (-600, 200))
    tn = tex(f"T_Jujak308_{P}_normal.png", "Non-Color", (-600, -150))
    tr = tex(f"T_Jujak308_{P}_rough.png", "Non-Color", (-600, -500))
    nm = nt.nodes.new("ShaderNodeNormalMap")
    nm.location = (-250, -150)
    nt.links.new(tb.outputs["Color"], bsdf.inputs["Base Color"])
    nt.links.new(tn.outputs["Color"], nm.inputs["Color"])
    nt.links.new(nm.outputs["Normal"], bsdf.inputs["Normal"])
    nt.links.new(tr.outputs["Color"], bsdf.inputs["Roughness"])
    nt.nodes.active = tb
    return mat


# ================================================================= stage: assemble
def body_landmarks():
    """Source-unit landmarks on the Meshy body (left side = +X)."""
    return dict(
        pelvis_h=(0, -0.30, -0.10), pelvis_t=(0, -0.38, 0.00),
        spine1_t=(0, -0.45, 0.08), spine2_t=(0, -0.52, 0.15),
        neck1_t=(0, -0.58, 0.24), neck2_t=(0, -0.62, 0.33), neck3_t=(0, -0.65, 0.40), neck4_t=(0, -0.67, 0.45),
        head_t=(0, -0.95, 0.46),
        hip=(0.17, -0.38, 0.02), knee=(0.185, -0.50, -0.22), ankle=(0.17, -0.29, -0.43),
        foot=(0.21, -0.35, -0.60), ball=(0.22, -0.38, -0.655),
        toe_mid=(0.22, -0.62, -0.662), toe_in=(0.12, -0.58, -0.662), toe_out=(0.34, -0.57, -0.662), hallux=(0.22, -0.12, -0.662),
        tail1_h=(0, 0.00, -0.27), tail1_t=(0, 0.34, -0.31), tail2_t=(0, 0.64, -0.31), tail3_t=(0, 0.97, -0.31),
        shoulder=(0.235, -0.47, 0.15),
    )


def stage_assemble():
    sc = bpy.context.scene
    body = S["body"]
    mn, mx = body.bbox()
    zmin = mn[2]
    feet = body.co[body.co[:, 2] < zmin + 0.08]
    fy = float(feet[:, 1].mean())
    SB = HEIGHT / (mx[2] - mn[2])
    S["SB"], S["fy"], S["zmin"] = SB, fy, zmin
    log("body scale", round(SB, 4), "foot centre y", round(fy, 3), "height", round((mx[2] - mn[2]) * SB, 3))

    def B(p):
        return Vector(((p[0]) * SB, (p[1] - fy) * SB, (p[2] - zmin) * SB))

    S["B"] = B
    Mb = np.eye(4)
    Mb[:3, :3] *= SB
    Mb[:3, 3] = -np.array([0, fy, zmin]) * SB
    S["body_md"] = body.transformed(Mb)
    lm = body_landmarks()
    S["lm"] = lm

    # ------------------------------------------------ wings
    wmd = S["wing"]
    sw = WING_REACH / (NT[0] - NS[0])
    S["sw"] = sw
    Rbase = Matrix(((-1, 0, 0, 0), (0, 0, -1, 0), (0, -1, 0, 0), (0, 0, 0, 1)))   # native -> right wing, upper face +Z
    shR = B((-lm["shoulder"][0], lm["shoulder"][1], lm["shoulder"][2]))
    Mw = (transm(shR) @ rotm("Z", -WING_SWEEP_DEG) @ rotm("Y", -WING_DROOP_DEG) @ scalem(sw) @ Rbase
          @ transm(-NS))
    S["Mw_R"] = Mw
    wR = wmd.transformed(Mw)
    wL = wR.mirrored_x()
    S["wingR_md"], S["wingL_md"] = wR, wL

    def wl(p, side):                       # native point -> final (side +1 left)
        q = Mw @ Vector(p)
        return Vector((-q.x, q.y, q.z)) if side > 0 else q
    S["wing_lm"] = lambda p, side: wl(p, side)

    # ------------------------------------------------ tail feathers
    tmd = S["tail"]
    ts = S["tail_tip_sign"]
    root_y = -0.5 * ts
    feathers = []
    for i in range(5):
        L = FEATHER_LEN * FEATHER_LEN_VAR[i]
        roll = FEATHER_ROLL[i] + RNG.uniform(-4, 4)
        droop = FEATHER_DROOP + RNG.uniform(-2.5, 2.5)
        base = Matrix.Identity(4)
        if ts < 0:
            base = rotm("Z", 180)
        rootp = B((FEATHER_ROOT_DX[i] * 0.18, lm["tail1_h"][1] + 0.02, lm["tail1_h"][2] - 0.01 - 0.01 * abs(i - 2)))
        M = (transm(rootp) @ rotm("Z", FEATHER_YAW[i]) @ rotm("X", -droop) @ rotm("Y", roll) @ scalem(L)
             @ base @ transm((0, -root_y * 1.0, 0)))
        # the above recentres: native root end -> origin; scale; orient
        feathers.append(tmd.transformed(M))
        S.setdefault("feather_axes", []).append((rootp, M, L))
    S["feather_mds"] = feathers

    # ------------------------------------------------ plates (rigid ceramic)
    plates = {}

    def place(piece, centre, target_h, euler, mirror=False):
        md = S["plates"][piece]
        mn, mx = md.bbox()
        c = (mn + mx) / 2
        k = target_h / (mx[2] - mn[2])
        M = (transm(centre) @ Euler([math.radians(a) for a in euler], "XYZ").to_matrix().to_4x4() @ scalem(k) @ transm(-c))
        out = md.transformed(M)
        if mirror:
            out = out.mirrored_x()
        return out, k
    S["place_plate"] = place
    P = PLATE_PLACE
    shL, k1 = place("shoulder", Vector(P["shoulder"]["c"]), P["shoulder"]["h"], P["shoulder"]["e"])
    # the native sheet is a *right* shoulder (scallops at -X outward) -> use for creature right (-X); mirror for left
    shin, k4 = place("shin", Vector(P["shin"]["c"]), P["shin"]["h"], P["shin"]["e"])
    plates["shoulderR"] = shL
    plates["shoulderL"] = shL.mirrored_x()
    plates["shinR"] = shin
    plates["shinL"] = shin.mirrored_x()
    plates["neck"], _ = place("neck", Vector(P["neck"]["c"]), P["neck"]["h"], P["neck"]["e"])
    plates["chest"], _ = place("chest", Vector(P["chest"]["c"]), P["chest"]["h"], P["chest"]["e"])
    S["plate_mds"] = plates

    # ------------------------------------------------ objects + materials
    for c in list(bpy.data.collections):
        pass
    mats = {p: make_material(p) for p in ("body", "wing", "tail", "plates")}
    S["mats"] = mats
    objs = {}
    objs["body"] = make_object("Jujak308_Body", S["body_md"])
    objs["wings"] = make_object("Jujak308_Wings", MD.join([S["wingR_md"], S["wingL_md"]]))
    objs["tail"] = make_object("Jujak308_Tail", MD.join(S["feather_mds"]))
    order = ["shoulderR", "shoulderL", "neck", "chest", "shinR", "shinL"]
    objs["plates"] = make_object("Jujak308_Plates", MD.join([plates[k] for k in order]))
    # piece vertex ranges inside the joined plate object
    ranges, off = {}, 0
    for k in order:
        ranges[k] = (off, off + len(plates[k].co))
        off += len(plates[k].co)
    S["plate_ranges"] = ranges
    wn = len(S["wingR_md"].co)
    S["wing_ranges"] = dict(R=(0, wn), L=(wn, 2 * wn))
    S["feather_ranges"] = []
    off = 0
    for f in feathers:
        S["feather_ranges"].append((off, off + len(f.co)))
        off += len(f.co)
    for key, part in (("body", "body"), ("wings", "wing"), ("tail", "tail"), ("plates", "plates")):
        objs[key].data.materials.append(mats[part])
    S["objs"] = objs
    total = sum(tri_count(o) for o in objs.values())
    log("assembled tris", total, {k: tri_count(o) for k, o in objs.items()})
    # frame info
    allco = np.concatenate([MD_of(o) for o in objs.values()])
    log("bbox", np.round(allco.min(0), 2), np.round(allco.max(0), 2))


def MD_of(ob):
    n = len(ob.data.vertices)
    co = np.empty(n * 3)
    ob.data.vertices.foreach_get("co", co)
    return co.reshape(n, 3)


# final-metre plate placement (centre, target height along native Z, euler XYZ degrees). Tuned against jj_front4 / jj_side4.
PLATE_PLACE = {
    "shoulder": dict(c=(-0.92, -0.50, 3.10), h=0.95, e=(10.0, 0.0, 0.0)),     # right shoulder (native sheet orientation)
    "neck": dict(c=(0.0, -1.38, 3.45), h=1.00, e=(-8.0, 0.0, 0.0)),
    "chest": dict(c=(0.0, -1.52, 2.50), h=1.00, e=(0.0, 0.0, 0.0)),
    "shin": dict(c=(-0.72, 0.18, 0.72), h=0.80, e=(0.0, 0.0, 0.0)),            # right shin
}


# ================================================================= stage: rig
def stage_rig():
    lm = S["lm"]
    B = S["B"]
    bpy.ops.object.select_all(action="DESELECT")
    ad = bpy.data.armatures.new("Jujak308_Rig")
    arm = bpy.data.objects.new("Jujak308_Rig", ad)
    bpy.context.scene.collection.objects.link(arm)
    bpy.context.view_layer.objects.active = arm
    arm.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    eb = ad.edit_bones
    defs = []     # (name, head, tail, parent, deform)

    def add(name, head, tail, parent=None, deform=True):
        b = eb.new(name)
        b.head = Vector(head)
        b.tail = Vector(tail)
        if (b.tail - b.head).length < 1e-3:
            b.tail = b.head + Vector((0, 0, 0.01))
        if parent:
            b.parent = eb[parent]
        b.use_connect = False
        b.use_deform = deform
        defs.append(name)
        return b

    bb = lambda k: B(lm[k])
    mirror = lambda p, side: Vector((p[0] * side, p[1], p[2]))
    root_h = Vector((0, 0, 0))
    add("Root", root_h, (0, 0.25, 0), None, deform=False)
    add("Pelvis", bb("pelvis_h"), bb("pelvis_t"), "Root")
    add("Spine1", bb("pelvis_t"), bb("spine1_t"), "Pelvis")
    add("Spine2", bb("spine1_t"), bb("spine2_t"), "Spine1")
    add("Neck1", bb("spine2_t"), bb("neck1_t"), "Spine2")
    add("Neck2", bb("neck1_t"), bb("neck2_t"), "Neck1")
    add("Neck3", bb("neck2_t"), bb("neck3_t"), "Neck2")
    add("Neck4", bb("neck3_t"), bb("neck4_t"), "Neck3")
    add("Head", bb("neck4_t"), bb("head_t"), "Neck4")
    for side, sn in ((1, "L"), (-1, "R")):
        pts = {k: mirror(B(lm[k]), 1) for k in ("hip", "knee", "ankle", "foot", "ball", "toe_mid", "toe_in", "toe_out", "hallux")}
        pts = {k: Vector((v.x * side, v.y, v.z)) for k, v in pts.items()}
        add(f"Leg_{sn}_Thigh", pts["hip"], pts["knee"], "Pelvis")
        add(f"Leg_{sn}_Shin", pts["knee"], pts["ankle"], f"Leg_{sn}_Thigh")
        add(f"Leg_{sn}_Tarsus", pts["ankle"], pts["foot"], f"Leg_{sn}_Shin")
        add(f"Leg_{sn}_Foot", pts["foot"], pts["ball"], f"Leg_{sn}_Tarsus")
        add(f"Leg_{sn}_ToeMid", pts["ball"], pts["toe_mid"], f"Leg_{sn}_Foot")
        add(f"Leg_{sn}_ToeIn", pts["ball"], pts["toe_in"], f"Leg_{sn}_Foot")
        add(f"Leg_{sn}_ToeOut", pts["ball"], pts["toe_out"], f"Leg_{sn}_Foot")
        add(f"Leg_{sn}_Hallux", pts["ball"], pts["hallux"], f"Leg_{sn}_Foot")
    add("Tail1", bb("tail1_h"), bb("tail1_t"), "Pelvis")
    add("Tail2", bb("tail1_t"), bb("tail2_t"), "Tail1")
    add("Tail3", bb("tail2_t"), bb("tail3_t"), "Tail2")
    # wing bones: positions from the wing's own landmarks through the placement matrix
    wlm = S["wing_lm"]
    ang_prim = list(PRIM_BONE_ANGLES)
    for side, sn in ((1, "L"), (-1, "R")):
        P = lambda p: wlm(p, side)
        sh, el, wr = P(NS), P(NE), P(NW)
        tp = P(NT)
        add(f"Wing_{sn}_Shoulder", sh, el, "Spine2")
        add(f"Wing_{sn}_Elbow", el, wr, f"Wing_{sn}_Shoulder")
        add(f"Wing_{sn}_Wrist", wr, tp, f"Wing_{sn}_Elbow")
        for k, a in enumerate(ang_prim):
            ra = math.radians(a)
            tipn = np.array([NW[0] + 0.32 * math.cos(ra), 0.0, NW[2] + 0.32 * math.sin(ra)])
            add(f"Wing_{sn}_Prim{k + 1}", wr, P(tipn), f"Wing_{sn}_Wrist")
        mid_in = np.array([(NS[0] + NE[0]) / 2, 0, NS[2] - 0.05])
        add(f"Wing_{sn}_Sec1", P((NS[0] + NE[0]) / 2 * 1.0 * np.array([1, 0, 0]) + np.array([0, 0, 0.14])), P(np.array([-0.30, 0, -0.18])), f"Wing_{sn}_Shoulder")
        add(f"Wing_{sn}_Sec2", P(np.array([-0.02, 0, 0.13])), P(np.array([0.10, 0, -0.18])), f"Wing_{sn}_Elbow")
    # tail feather chains
    for i, (rootp, M, L) in enumerate(S["feather_axes"]):
        tip = M @ Vector((0, 0.5 * 1.0 * 1.0, 0)) if False else None
        # feather runs from its root point along +Y of the placed frame: direction = M rotation applied to (0,1,0)
        d = (M.to_3x3() @ Vector((0, 1, 0))).normalized()
        # note: M includes the scale; direction normalised, length L
        for j in range(3):
            a = rootp + d * (L * j / 3)
            b = rootp + d * (L * (j + 1) / 3)
            add(f"TailF{i + 1}_{j + 1}", a, b, "Tail1" if j == 0 else f"TailF{i + 1}_{j}")
    bpy.ops.object.mode_set(mode="OBJECT")
    S["arm"] = arm
    # root gets a name used by Unity as the foot-centre origin
    log("bones", len(ad.bones), "deform", sum(1 for b in ad.bones if b.use_deform))


# ================================================================= stage: weights
def assign_groups(ob, weights):
    """weights: list per vertex of {group: w}."""
    for name in sorted({k for w in weights for k in w}):
        if name not in ob.vertex_groups:
            ob.vertex_groups.new(name=name)
    for vi, w in enumerate(weights):
        for k, v in w.items():
            if v > 1e-5:
                ob.vertex_groups[k].add([vi], float(v), "REPLACE")


def attach(ob, arm):
    ob.parent = arm
    mod = ob.modifiers.new("Armature", "ARMATURE")
    mod.object = arm
    mod.use_vertex_groups = True


def clean_weights(ob, side_rules, max_inf=4, floor=0.02):
    me = ob.data
    names = {g.index: g.name for g in ob.vertex_groups}
    out = []
    for v in me.vertices:
        d = {names[g.group]: g.weight for g in v.groups}
        x = v.co.x
        for pred, banned in side_rules:
            if pred(x):
                d = {k: w for k, w in d.items() if not k.startswith(banned)}
        d = {k: w for k, w in d.items() if w >= floor}
        if len(d) > max_inf:
            d = dict(sorted(d.items(), key=lambda kv: -kv[1])[:max_inf])
        s = sum(d.values())
        out.append({k: w / s for k, w in d.items()} if s > 0 else {})
    return out


LEG_FADE = (0.12, 0.50)     # a leg bone's weight fades to 0 toward the body centre line (metres from x=0)
SMOOTH_ITERS = int(os.environ.get("JJ_SMOOTH", "2"))
SMOOTH_R = float(os.environ.get("JJ_SMOOTH_R", "0.14"))


def smooth_leg_weights(ob, max_inf=4, floor=0.02):
    """Automatic weights -> (a) legs only act on their own side, fading out toward the centre line, (b) a few Laplacian
    smoothing passes over the mesh graph (kills hard switches between neighbours), (c) dust removed, <= 4 influences."""
    me = ob.data
    n = len(me.vertices)
    names = [g.name for g in ob.vertex_groups]
    col = {nm: i for i, nm in enumerate(names)}
    W = np.zeros((n, len(names)))
    for v in me.vertices:
        for g in v.groups:
            W[v.index, g.group] = g.weight
    x = np.empty(n * 3)
    me.vertices.foreach_get("co", x)
    x = x.reshape(n, 3)[:, 0]

    def ss(a, b, v):
        t = np.clip((v - a) / (b - a), 0, 1)
        return t * t * (3 - 2 * t)

    pts0 = np.empty(n * 3)
    me.vertices.foreach_get("co", pts0)
    pts0 = pts0.reshape(n, 3)

    def fade():
        for nm, i in col.items():
            if nm.startswith("Leg_L_"):
                W[:, i] *= ss(LEG_FADE[0], LEG_FADE[1], x)
            elif nm.startswith("Leg_R_"):
                W[:, i] *= ss(LEG_FADE[0], LEG_FADE[1], -x)

    def renorm():
        s = W.sum(1, keepdims=True)
        np.divide(W, s, out=W, where=s > 1e-9)
    # spatial (not mesh-graph) smoothing: weights become a smooth function of position at the SMOOTH_R scale, so tiny
    # neighbouring edges of the feathered shell do not get pulled apart (stretch gate counts every edge >= 2 mm)
    from mathutils.kdtree import KDTree
    pts = np.empty(n * 3)
    me.vertices.foreach_get("co", pts)
    pts = pts.reshape(n, 3)
    tree = KDTree(n)
    for i in range(n):
        tree.insert(pts[i], i)
    tree.balance()
    rows, cols, vals = [], [], []
    for i in range(n):
        for (_, j, d) in tree.find_range(pts[i], SMOOTH_R):
            rows.append(i)
            cols.append(j)
            vals.append(math.exp(-(d / (SMOOTH_R * 0.55)) ** 2))
    rows, cols, vals = np.array(rows), np.array(cols), np.array(vals)
    norm = np.bincount(rows, weights=vals, minlength=n)
    fade()
    renorm()
    for _ in range(SMOOTH_ITERS):
        Wn = np.zeros_like(W)
        for j in range(W.shape[1]):
            Wn[:, j] = np.bincount(rows, weights=vals * W[cols, j], minlength=n) / norm
        W = Wn
        fade()
        renorm()
    log("weight smoothing: radius", SMOOTH_R, "passes", SMOOTH_ITERS, "neighbours/vertex", round(len(rows) / n, 1))
    W = prune(W, max_inf, floor)
    W = strain_optimise(ob, names, W, pts0)
    out = []
    for i in range(n):
        d = {names[j]: W[i, j] for j in np.where(W[i] > 0)[0]}
        s = sum(d.values())
        out.append({k: v / s for k, v in d.items()} if s > 0 else {})
    return out


def prune(W, max_inf=4, floor=0.02):
    W = W.copy()
    W[W < floor] = 0
    if W.shape[1] > max_inf:
        kth = np.sort(W, axis=1)[:, -max_inf][:, None]
        W[W < kth] = 0
    s = W.sum(1, keepdims=True)
    return np.divide(W, s, out=np.zeros_like(W), where=s > 1e-9)


STRAIN_MAX, STRAIN_MIN = 2.2, 0.30      # optimiser targets (stricter than the 2.5 / 0.15 gate: leaves room for the 4-influence cut)


def skin_matrices(arm, names):
    ad = arm.data
    K = np.zeros((len(names), 4, 4))
    for i, nm in enumerate(names):
        K[i] = np.array(arm.pose.bones[nm].matrix @ ad.bones[nm].matrix_local.inverted())
    return K


def strain_optimise(ob, names, W, pts):
    """Weight clean-up against the verification poses: wherever an edge (>= 2 mm) would stretch past STRAIN_MAX or squash
    below STRAIN_MIN in any sampled pose, average the two end vertices' weights with their neighbours (repeat) and
    re-prune to 4 influences.  Pure numpy linear-blend skinning, so no editor round trips."""
    arm = S["arm"]
    n = len(pts)
    e = edges_of(ob)
    l0 = np.linalg.norm(pts[e[:, 0]] - pts[e[:, 1]], axis=1)
    ok = l0 >= 0.002
    # pose samples
    mats = []
    for an, act in S["actions"].items():
        fr0, fr1 = [int(x) for x in act.frame_range]
        frames = [fr0 + i * 0.5 for i in range(int((fr1 - fr0) * 2) + 1)]          # same half-frame grid the verifier uses
        ad = arm.animation_data_create()
        for fr in frames:
            clear_action(arm) if fr == frames[0] else None
            ad.action = act
            if act.slots:
                ad.action_slot = act.slots[0]
            bpy.context.scene.frame_set(int(math.floor(fr)), subframe=fr - math.floor(fr))
            bpy.context.view_layer.update()
            mats.append(skin_matrices(arm, names))
    clear_action(arm)
    R9 = [m[:, :3, :3].reshape(len(names), 9) for m in mats]
    T = [m[:, :3, 3] for m in mats]
    deg = np.zeros(n)
    np.add.at(deg, e[:, 0], 1)
    np.add.at(deg, e[:, 1], 1)

    def worst(W):
        badedge = np.zeros(len(e), dtype=bool)
        mx, mn, score = 1.0, 1.0, 0.0
        for R9k, Tk in zip(R9, T):
            Mk = (W @ R9k).reshape(n, 3, 3)
            q = np.einsum("nij,nj->ni", Mk, pts) + W @ Tk
            r = np.linalg.norm(q[e[:, 0]] - q[e[:, 1]], axis=1) / np.maximum(l0, 1e-9)
            r = np.where(ok, r, 1.0)
            badedge |= (r > STRAIN_MAX) | (r < STRAIN_MIN)
            mx, mn = max(mx, r.max()), min(mn, r.min())
            score += float((np.clip(r - STRAIN_MAX, 0, None) ** 2).sum() + (np.clip(STRAIN_MIN - r, 0, None) ** 2).sum())
        return badedge, mx, mn, score
    badedge, mx, mn, score = worst(W)
    log("strain optimiser start: max", round(mx, 3), "min", round(mn, 3), "offending edges", int(badedge.sum()), "poses", len(mats))
    alpha = 1.0
    it = 0
    while badedge.any() and alpha > 0.04 and it < 300:
        it += 1
        Sm = np.zeros_like(W)
        cnt = np.zeros(n)
        pair = 0.5 * (W[e[badedge, 0]] + W[e[badedge, 1]])
        np.add.at(Sm, e[badedge, 0], pair)
        np.add.at(Sm, e[badedge, 1], pair)
        np.add.at(cnt, e[badedge, 0], 1)
        np.add.at(cnt, e[badedge, 1], 1)
        v = cnt > 0
        Wn = W.copy()
        Wn[v] = (1 - alpha) * W[v] + alpha * Sm[v] / cnt[v][:, None]
        Wn = prune(Wn)
        b2, mx2, mn2, s2 = worst(Wn)
        if s2 < score:                                  # monotone: keep only improvements
            W, badedge, mx, mn, score = Wn, b2, mx2, mn2, s2
        else:
            alpha *= 0.6
    bad = badedge
    log("strain optimiser end: max", round(mx, 3), "min", round(mn, 3), "offending vertices", int(bad.sum()), "iterations", it, "score", round(score, 4))
    return W


def optimise_dicts(ob, dicts):
    """Per-vertex {group: weight} dicts -> <= 4 influences, strain-optimised against the test poses, back to dicts."""
    names = sorted({k for d in dicts for k in d})
    col = {nm: i for i, nm in enumerate(names)}
    W = np.zeros((len(dicts), len(names)))
    for i, d in enumerate(dicts):
        for k, v in d.items():
            W[i, col[k]] = v
    s = W.sum(1, keepdims=True)
    W = np.divide(W, s, out=np.zeros_like(W), where=s > 1e-9)
    W = prune(W, 4, 0.01)
    W = strain_optimise(ob, names, W, MD_of(ob))
    return [{names[j]: float(W[i, j]) for j in np.where(W[i] > 0)[0]} for i in range(len(dicts))]


def stage_weights():
    arm = S["arm"]
    objs = S["objs"]
    ad = arm.data
    names_all = [b.name for b in ad.bones]
    body_names = [n for n in names_all if not n.startswith(("Wing_", "TailF")) and n != "Root"]
    # --- body: automatic weights with only body bones deforming
    for b in ad.bones:
        b.use_deform = b.name in body_names
    bpy.ops.object.select_all(action="DESELECT")
    objs["body"].select_set(True)
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.parent_set(type="ARMATURE_AUTO")
    log("auto weights groups", len(objs["body"].vertex_groups))
    # clean: nothing from the other leg; drop dust; <= 4 influences
    body = objs["body"]
    me = body.data
    fixed = smooth_leg_weights(body)
    # vertices that lost everything: nearest bone segment
    bones = {b.name: (b.head_local.copy(), b.tail_local.copy()) for b in ad.bones if b.use_deform}

    def nearest_bone(p):
        best, bn = 1e9, None
        for n, (h, t) in bones.items():
            d = t - h
            u = max(0, min(1, (p - h).dot(d) / max(d.dot(d), 1e-9)))
            dist = (p - (h + d * u)).length
            if dist < best:
                best, bn = dist, n
        return bn
    nfix = 0
    for vi, w in enumerate(fixed):
        if not w:
            w[nearest_bone(me.vertices[vi].co)] = 1.0
            nfix += 1
    for g in list(body.vertex_groups):
        body.vertex_groups.remove(g)
    assign_groups(body, fixed)
    log("body weights cleaned; unweighted fallbacks", nfix)
    for b in ad.bones:
        b.use_deform = b.name != "Root"

    # --- wings: native classification carried to both sides
    ww = S["wing_w"]
    wobj = objs["wings"]
    wn = len(ww)
    allw = []
    for sn in ("R", "L"):
        for w in ww:
            allw.append({f"Wing_{sn}_{k}": v for k, v in w.items()})
    allw = optimise_dicts(wobj, allw)
    assign_groups(wobj, allw)
    attach(wobj, arm)

    # --- tail feathers: banded smooth along length across the 3 chain bones
    tobj = objs["tail"]
    tw = []
    for i, ((a, b), (rootp, M, L)) in enumerate(zip(S["feather_ranges"], S["feather_axes"])):
        d = (M.to_3x3() @ Vector((0, 1, 0))).normalized()
        for vi in range(a, b):
            p = Vector(tobj.data.vertices[vi].co)
            u = max(0.0, min(1.0, (p - rootp).dot(d) / L))
            # soft bands
            def ss(x0, x1, v):
                t = max(0, min(1, (v - x0) / (x1 - x0)))
                return t * t * (3 - 2 * t)
            e1 = ss(1 / 3 - 0.08, 1 / 3 + 0.08, u)
            e2 = ss(2 / 3 - 0.08, 2 / 3 + 0.08, u)
            tw.append({f"TailF{i + 1}_1": 1 - e1, f"TailF{i + 1}_2": e1 * (1 - e2), f"TailF{i + 1}_3": e1 * e2})
    tw = optimise_dicts(tobj, tw)
    assign_groups(tobj, tw)
    attach(tobj, arm)

    # --- plates: 100 % to one bone each
    pobj = objs["plates"]
    bone_for = {"shoulderR": "Wing_R_Shoulder", "shoulderL": "Wing_L_Shoulder", "neck": "Neck3", "chest": "Spine2",
                "shinR": "Leg_R_Tarsus", "shinL": "Leg_L_Tarsus"}
    pw = [None] * len(pobj.data.vertices)
    for k, (a, b) in S["plate_ranges"].items():
        for vi in range(a, b):
            pw[vi] = {bone_for[k]: 1.0}
    assign_groups(pobj, pw)
    attach(pobj, arm)
    S["plate_bone_for"] = bone_for
    for o in objs.values():
        o.parent = arm
    log("weights done")


# ================================================================= posing
def rot_from(spec):
    """spec: list of (axis 'X'|'Y'|'Z' or Vector, degrees), applied in order in armature space."""
    R = Matrix.Identity(3)
    for ax, deg in spec:
        axis = Vector(ax) if not isinstance(ax, str) else {"X": Vector((1, 0, 0)), "Y": Vector((0, 1, 0)), "Z": Vector((0, 0, 1))}[ax]
        R = Quaternion(axis.normalized(), math.radians(deg)).to_matrix() @ R
    return R


def pose_basis(arm, targets):
    """targets: {bone: 3x3 armature-space rotation delta}. Returns {bone: quaternion basis} for those bones
    (unspecified bones inherit their parent's delta with identity basis)."""
    ad = arm.data
    delta = {}
    out = {}

    def rest_rot(b):
        return b.matrix_local.to_3x3()

    def walk(b):
        par = b.parent
        inherit = delta[par.name] if par else Matrix.Identity(3)
        if b.name in targets:
            Rt = targets[b.name] @ rest_rot(b)
            A = inherit @ rest_rot(b)          # parent already carries delta * rest; the rest offset cancels
            out[b.name] = (A.inverted() @ Rt).to_quaternion()
            delta[b.name] = targets[b.name]
        else:
            delta[b.name] = inherit
        for c in b.children:
            walk(c)
    for b in ad.bones:
        if b.parent is None:
            walk(b)
    return out


def apply_keys(arm, action_name, keyframes):
    """keyframes: [(frame, {bone: R3x3 delta})]."""
    ad = arm.animation_data_create()
    act = bpy.data.actions.new(action_name)
    ad.action = act
    for pb in arm.pose.bones:
        pb.rotation_mode = "QUATERNION"
        pb.rotation_quaternion = Quaternion()
    touched = set()
    for _, t in keyframes:
        touched |= set(t)
    for frame, targets in keyframes:
        q = pose_basis(arm, targets)
        for name in touched:
            pb = arm.pose.bones[name]
            pb.rotation_quaternion = q.get(name, Quaternion())
            pb.keyframe_insert("rotation_quaternion", frame=frame)
    if act.slots and ad.action_slot is None:
        ad.action_slot = act.slots[0]
    ad.action = None
    for pb in arm.pose.bones:
        pb.rotation_quaternion = Quaternion()
    act.use_fake_user = True
    return act


def set_action(arm, act, frame):
    clear_action(arm)
    ad = arm.animation_data_create()
    ad.action = act
    if act.slots:
        ad.action_slot = act.slots[0]
    bpy.context.scene.frame_set(frame)
    bpy.context.view_layer.update()


def clear_action(arm):
    ad = arm.animation_data_create()
    ad.action = None
    for pb in arm.pose.bones:
        pb.rotation_quaternion = Quaternion()
        pb.location = (0, 0, 0)
    bpy.context.view_layer.update()


def stage_actions():
    arm = S["arm"]
    acts = {}
    sweep = WING_SWEEP_DEG
    droop = WING_DROOP_DEG
    Z = (0, 0, 1)
    # spread: raise to horizontal, swing forward, fan the primaries
    def wing_pose(sn, shoulder, elbow, wrist, prim_fan):
        s = 1 if sn == "L" else -1
        t = {}
        t[f"Wing_{sn}_Shoulder"] = rot_from([("Y", s * shoulder[0]), ("Z", s * shoulder[1])])
        t[f"Wing_{sn}_Elbow"] = rot_from([("Y", s * shoulder[0]), ("Z", s * (shoulder[1] + elbow))])
        t[f"Wing_{sn}_Wrist"] = rot_from([("Y", s * shoulder[0]), ("Z", s * (shoulder[1] + elbow + wrist))])
        for k in range(4):
            t[f"Wing_{sn}_Prim{k + 1}"] = rot_from([("Y", s * shoulder[0]), ("Z", s * (shoulder[1] + elbow + wrist) + prim_fan[k])])
        return t
    # sign notes: for the left wing, rotation about +Y by +a lifts... droop was a Y rotation of -droop for the right wing;
    # left is the mirror -> +droop. A *positive* value for the left wing is "tips down", so un-droop = negative.
    keys = [(1, {}), (12, {}), (24, {})]
    spread_mid, spread_end = {}, {}
    for sn in ("L", "R"):
        s = 1 if sn == "L" else -1
        spread_end.update(wing_pose(sn, (-droop - 10, -sweep - 8), 0, 0, [-6, -2, 2, 6]))
        spread_mid.update(wing_pose(sn, (-droop / 2 - 4, -sweep / 2 - 3), 0, 0, [-3, -1, 1, 3]))
    acts["TEST_WingSpread"] = apply_keys(arm, "TEST_WingSpread", [(1, {}), (12, spread_mid), (24, spread_end)])
    # fold: the wing hangs down along the flank (extra droop), then Z-folds *in its own plane* (rotation about the plane normal)
    def wing_fold(sn, k):
        s = 1 if sn == "L" else -1
        sh = [("Y", s * 44.0 * k)]
        n_bind = (Matrix.Rotation(math.radians(s * WING_SWEEP_DEG), 3, "Z") @ Matrix.Rotation(math.radians(s * WING_DROOP_DEG), 3, "Y")) @ Vector((0, 0, 1))
        n = rot_from(sh) @ n_bind
        t = {f"Wing_{sn}_Shoulder": rot_from(sh)}
        t[f"Wing_{sn}_Elbow"] = rot_from(sh + [(n, FOLD_SIGN * s * FOLD_ELBOW * k)])
        t[f"Wing_{sn}_Wrist"] = rot_from(sh + [(n, FOLD_SIGN * s * FOLD_WRIST * k)])
        for j, fan in enumerate([-7, -2.5, 2.5, 7]):
            t[f"Wing_{sn}_Prim{j + 1}"] = rot_from(sh + [(n, FOLD_SIGN * s * fan * k)])
        return t
    fold_mid, fold_end = {}, {}
    for sn in ("L", "R"):
        fold_end.update(wing_fold(sn, 1.0))
        fold_mid.update(wing_fold(sn, 0.5))
    acts["TEST_WingFold"] = apply_keys(arm, "TEST_WingFold", [(1, {}), (12, fold_mid), (24, fold_end)])
    # tail swish: the centre chain and the five ribbons yaw about the vertical, each joint a bit more
    tl = {}
    tl_mid = {}
    for k, a in zip(("Tail1", "Tail2", "Tail3"), (8, 16, 24)):
        tl[k] = rot_from([("Z", a)])
        tl_mid[k] = rot_from([("Z", a * 0.5)])
    for i in range(5):
        for j, a in enumerate((8 + 6, 14 + 12, 20 + 22)):
            tl[f"TailF{i + 1}_{j + 1}"] = rot_from([("Z", a + (i - 2) * 1.5)])
            tl_mid[f"TailF{i + 1}_{j + 1}"] = rot_from([("Z", (a + (i - 2) * 1.5) * 0.5)])
    acts["TEST_TailSwish"] = apply_keys(arm, "TEST_TailSwish", [(1, {}), (8, tl_mid), (16, tl)])
    # neck strike: cumulative pitch about +X (forward = -Y for an upright neck)
    ns = {}
    for k, a in zip(("Spine1", "Spine2", "Neck1", "Neck2", "Neck3", "Neck4", "Head"), (6, 12, 24, 38, 52, 64, 50)):
        ns[k] = rot_from([("X", a)])
    acts["TEST_NeckStrike"] = apply_keys(arm, "TEST_NeckStrike", [(1, {}), (8, ns), (16, {})])
    # step: left leg lifts (hip forward, knee folds back, ankle drops); body shifts a little
    st = {"Leg_L_Thigh": rot_from([("X", -20)]), "Leg_L_Shin": rot_from([("X", 8)]), "Leg_L_Tarsus": rot_from([("X", -14)]),
          "Leg_L_Foot": rot_from([("X", -24)])}
    acts["TEST_Step"] = apply_keys(arm, "TEST_Step", [(1, {}), (10, st), (20, {})])
    S["actions"] = acts
    log("actions", list(acts))


# ================================================================= verification
def eval_co(ob):
    dg = bpy.context.evaluated_depsgraph_get()
    ev = ob.evaluated_get(dg)
    me = ev.to_mesh()
    n = len(me.vertices)
    co = np.empty(n * 3)
    me.vertices.foreach_get("co", co)
    ev.to_mesh_clear()
    return co.reshape(n, 3)


def edges_of(ob):
    me = ob.data
    e = np.empty(len(me.edges) * 2, dtype=np.int64)
    me.edges.foreach_get("vertices", e)
    return e.reshape(-1, 2)


def stage_verify():
    arm = S["arm"]
    objs = S["objs"]
    rep = {"tris": {k: tri_count(o) for k, o in objs.items()}}
    rep["tris_total"] = sum(rep["tris"].values())
    rep["bones"] = len(arm.data.bones)
    rep["deform_bones"] = sum(1 for b in arm.data.bones if b.use_deform)
    rep["materials"] = sorted({m.name for o in objs.values() for m in o.data.materials})
    clear_action(arm)
    rest = {k: eval_co(o) for k, o in objs.items()}
    ed = {k: edges_of(o) for k, o in objs.items()}
    elen = {k: np.linalg.norm(rest[k][ed[k][:, 0]] - rest[k][ed[k][:, 1]], axis=1) for k in objs}
    valid = {k: elen[k] >= 0.002 for k in objs}
    # ground / foot at rest
    allmin = min(rest[k][:, 2].min() for k in objs)
    body_rest = rest["body"]
    left = body_rest[(body_rest[:, 0] > 0) & (body_rest[:, 2] < 0.05)]
    right = body_rest[(body_rest[:, 0] < 0) & (body_rest[:, 2] < 0.05)]
    rep["rest"] = dict(lowest_vertex_z_m=float(allmin), foot_left_min_z=float(left[:, 2].min()), foot_right_min_z=float(right[:, 2].min()),
                       foot_sole_spread_m=float(max(left[:, 2].max() - left[:, 2].min(), right[:, 2].max() - right[:, 2].min())),
                       crest_top_z=float(body_rest[:, 2].max()))
    allv = np.concatenate([rest[k] for k in objs])
    rep["rest"]["extent_xyz"] = (allv.max(0) - allv.min(0)).round(3).tolist()
    # influences
    inf = {}
    for k, o in objs.items():
        mx = 0
        un = 0
        for v in o.data.vertices:
            n = sum(1 for g in v.groups if g.weight > 1e-5)
            mx = max(mx, n)
            un += n == 0
        inf[k] = dict(max_influences=mx, unweighted=un)
    rep["influences"] = inf
    # emission
    em = []
    for m in bpy.data.materials:
        if m.use_nodes and m.name.startswith("M_Jujak308"):
            for n in m.node_tree.nodes:
                if n.type == "BSDF_PRINCIPLED":
                    em.append(dict(material=m.name, emission_strength=float(n.inputs["Emission Strength"].default_value),
                                   metallic=float(n.inputs["Metallic"].default_value),
                                   emission_linked=n.inputs["Emission Color"].is_linked))
    rep["materials_check"] = em
    # poses
    P = {}
    plate_pairs = {}
    rng = np.random.default_rng(1)
    for pk, (a, b) in S["plate_ranges"].items():
        idx = rng.integers(a, b, size=(400, 2))
        plate_pairs[pk] = idx
    for an, act in S["actions"].items():
        fr0, fr1 = [int(x) for x in act.frame_range]
        clear_action(arm)
        row = dict(max_stretch=1.0, min_compression=1.0, worst=None, plate_max_edge_dev_mm=0.0, plate_max_pair_dev_mm=0.0, min_z=1e9,
                   foot_min_z=1e9, max_disp=0.0)
        frames = sorted(set([fr0 + i * 0.5 for i in range(int((fr1 - fr0) * 2) + 1)]))
        for fr in frames:
            ad = arm.animation_data_create()
            ad.action = act
            if act.slots:
                ad.action_slot = act.slots[0]
            bpy.context.scene.frame_set(int(math.floor(fr)), subframe=fr - math.floor(fr))
            bpy.context.view_layer.update()
            for k, o in objs.items():
                co = eval_co(o)
                e = ed[k]
                ratio = np.linalg.norm(co[e[:, 0]] - co[e[:, 1]], axis=1) / np.where(elen[k] > 0, elen[k], 1)
                r = ratio[valid[k]]
                if len(r):
                    if r.max() > row["max_stretch"]:
                        row["max_stretch"] = float(r.max())
                        j = int(np.argmax(np.where(valid[k], ratio, 0)))
                        row["worst"] = dict(object=k, edge=e[j].tolist(), frame=fr, native=float(elen[k][j]))
                    row["min_compression"] = float(min(row["min_compression"], r.min()))
                row["min_z"] = float(min(row["min_z"], co[:, 2].min()))
                row["x_lo"] = float(min(row.get("x_lo", 1e9), co[:, 0].min()))
                row["x_hi"] = float(max(row.get("x_hi", -1e9), co[:, 0].max()))
                row["max_disp"] = float(max(row["max_disp"], np.linalg.norm(co - rest[k], axis=1).max()))
                if k == "body":
                    lf = co[(rest[k][:, 0] > 0) & (rest[k][:, 2] < 0.05)]
                    row["foot_min_z"] = float(min(row["foot_min_z"], lf[:, 2].min()))
                if k == "plates":
                    for pk, (a, b) in S["plate_ranges"].items():
                        ee = e[(e[:, 0] >= a) & (e[:, 0] < b)]
                        d0 = np.linalg.norm(rest[k][ee[:, 0]] - rest[k][ee[:, 1]], axis=1)
                        d1 = np.linalg.norm(co[ee[:, 0]] - co[ee[:, 1]], axis=1)
                        row["plate_max_edge_dev_mm"] = max(row["plate_max_edge_dev_mm"], float(np.abs(d1 - d0).max() * 1000))
                        ij = plate_pairs[pk]
                        d0 = np.linalg.norm(rest[k][ij[:, 0]] - rest[k][ij[:, 1]], axis=1)
                        d1 = np.linalg.norm(co[ij[:, 0]] - co[ij[:, 1]], axis=1)
                        row["plate_max_pair_dev_mm"] = max(row["plate_max_pair_dev_mm"], float(np.abs(d1 - d0).max() * 1000))
        row["max_span_x_m"] = row["x_hi"] - row["x_lo"]       # widest the creature gets over this action (wing spread = full span)
        P[an] = row
        log("verify", an, {k: (round(v, 4) if isinstance(v, float) else v) for k, v in row.items() if k != "worst"})
    rep["poses"] = P
    clear_action(arm)
    # gates (same limits as SPEC-ENEMY-BODIES-308 / rig_creatures.validate_deformation)
    gates = {
        "tris<=45000": rep["tris_total"] <= TRI_BUDGET,
        "materials<=4": len(rep["materials"]) <= 4,
        "influences<=4": all(v["max_influences"] <= 4 and v["unweighted"] == 0 for v in inf.values()),
        "emission==0": all(m["emission_strength"] == 0 and not m["emission_linked"] and m["metallic"] == 0 for m in em),
        "stretch<=2.5": all(v["max_stretch"] <= 2.5 for v in P.values()),
        "compression>=0.15": all(v["min_compression"] >= 0.15 for v in P.values()),
        "plates_rigid(<=0.1mm)": all(v["plate_max_edge_dev_mm"] <= 0.1 and v["plate_max_pair_dev_mm"] <= 0.1 for v in P.values()),
        "rest_ground(|z|<=0.008)": abs(rep["rest"]["lowest_vertex_z_m"]) <= 0.008,
        "pose_ground(>=-0.008)": all(v["min_z"] >= -0.008 for v in P.values()),
    }
    rep["gates"] = gates
    WORK.mkdir(parents=True, exist_ok=True)
    (WORK / "verify_jujak308.json").write_text(json.dumps(rep, indent=1, ensure_ascii=False), encoding="utf-8")
    log("GATES", json.dumps(gates))
    S["report"] = rep


# ================================================================= previews
def setup_render(w=1280, h=720):
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_WORKBENCH"
    sc.render.resolution_x, sc.render.resolution_y = w, h
    sc.render.resolution_percentage = 100
    sc.render.film_transparent = False
    sc.display.shading.light = "STUDIO"
    sc.display.shading.color_type = "TEXTURE"
    sc.display.shading.show_object_outline = False
    sc.display.shading.show_specular_highlight = False
    sc.display.shading.show_shadows = False
    sc.display.shading.show_cavity = False
    sc.view_settings.view_transform = "Standard"
    sc.view_settings.exposure = 0.7
    try:
        sc.display.render_aa = "8"
    except Exception:
        pass
    world = bpy.data.worlds.get("W") or bpy.data.worlds.new("W")
    world.color = (0.50, 0.46, 0.40)       # lands near paper (211,199,183) after the +0.7 exposure
    sc.world = world
    cam = bpy.data.objects.get("PrevCam")
    if cam is None:
        cd = bpy.data.cameras.new("PrevCam")
        cam = bpy.data.objects.new("PrevCam", cd)
        sc.collection.objects.link(cam)
    sc.camera = cam
    return cam


def aim(cam, loc, target):
    cam.location = Vector(loc)
    d = Vector(target) - Vector(loc)
    cam.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()


def render_to(path):
    sc = bpy.context.scene
    sc.render.filepath = str(path)
    bpy.ops.render.render(write_still=True)


def stage_previews(poses=True):
    cam = setup_render()
    sc = bpy.context.scene
    PREV.mkdir(parents=True, exist_ok=True)
    arm = S.get("arm")
    if arm:
        clear_action(arm)
    ctr = Vector((0, 0.6, 2.4))
    views = {
        "front": ((0, -30, 2.5), (0, 0, 2.5), "ORTHO", 13.0),
        "side": ((30, 0.6, 2.5), (0, 0.6, 2.5), "ORTHO", 13.0),
        "back": ((0, 30, 2.5), (0, 0, 2.5), "ORTHO", 13.0),
        "threequarter": ((-9.5, -13.0, 5.0), (0, 0.4, 2.4), "PERSP", 0),
    }
    def shoot(name, view):
        loc, tgt, kind, sc_ = view
        cam.data.type = "ORTHO" if kind == "ORTHO" else "PERSP"
        if kind == "ORTHO":
            cam.data.ortho_scale = sc_
        else:
            cam.data.lens = 45
            cam.data.clip_end = 200
        cam.data.clip_start = 0.1
        cam.data.clip_end = 200
        aim(cam, loc, tgt)
        render_to(PREV / f"jujak308_{name}.png")
    for n, v in views.items():
        shoot(n, v)
    if poses and arm and S.get("actions"):
        pv = {"threequarter": views["threequarter"], "front": views["front"]}
        for an, fr, tag, vs in (("TEST_WingSpread", 24, "wing_spread", ("front", "threequarter")),
                                ("TEST_WingFold", 24, "wing_fold", ("front", "threequarter")),
                                ("TEST_TailSwish", 16, "tail_swish", ("back",)),
                                ("TEST_NeckStrike", 8, "neck_strike", ("side",)),
                                ("TEST_Step", 10, "step", ("side",))):
            set_action(arm, S["actions"][an], fr)
            for vn in vs:
                shoot(f"{tag}_{vn}", views[vn])
        clear_action(arm)
    # colour comparison next to the concept side view
    side = PREV / "jujak308_side.png"
    compare(side, CONCEPT / "jj_side4.png", PREV / "jujak308_color_vs_concept_side.png")
    log("previews done")


def compare(a_path, b_path, out_path, height=720):
    ims = []
    for p in (a_path, b_path):
        im = bpy.data.images.load(str(p), check_existing=False)
        w, h = im.size
        nw = int(round(w * height / h))
        im.scale(nw, height)
        arr = np.empty(nw * height * 4, dtype=np.float32)
        im.pixels.foreach_get(arr)
        ims.append(arr.reshape(height, nw, 4))
        bpy.data.images.remove(im)
    sheet = np.concatenate(ims, axis=1)
    sheet[..., 3] = 1.0
    out = bpy.data.images.new("cmp", sheet.shape[1], sheet.shape[0], alpha=False)
    out.pixels.foreach_set(sheet.reshape(-1))
    out.filepath_raw = str(out_path)
    out.file_format = "PNG"
    out.save()
    bpy.data.images.remove(out)


# ================================================================= export
def stage_export():
    arm = S["arm"]
    objs = S["objs"]
    clear_action(arm)
    GAME.mkdir(parents=True, exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    arm.select_set(True)
    for o in objs.values():
        o.select_set(True)
    bpy.context.view_layer.objects.active = arm
    for act in S["actions"].values():
        act.use_fake_user = True
    fbx = GAME / "Jujak308.fbx"
    bpy.ops.export_scene.fbx(
        filepath=str(fbx), use_selection=True, object_types={"ARMATURE", "MESH"}, add_leaf_bones=False,
        bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False, bake_anim_force_startend_keying=True,
        bake_anim_step=1, bake_anim_simplify_factor=0, axis_forward="-Z", axis_up="Y",
        apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS", path_mode="AUTO", mesh_smooth_type="FACE",
        use_armature_deform_only=False)
    log("exported", fbx, round(fbx.stat().st_size / 1e6, 2), "MB")
    WORK.mkdir(parents=True, exist_ok=True)
    purge_import_junk()
    bpy.context.preferences.filepaths.save_version = 0
    blend = WORK / "jujak308.blend"
    bpy.ops.wm.save_as_mainfile(filepath=str(blend), compress=True, relative_remap=True)
    log("saved", blend)


# ================================================================= main
def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument("--stages", default=",".join(ALL_STAGES))
    ap.add_argument("--no-grade", action="store_true")
    ap.add_argument("--save-wip", action="store_true")
    a = ap.parse_args(argv)
    stages = [s for s in a.stages.split(",") if s]
    for s in stages:
        assert s in ALL_STAGES, s
        log("=== stage", s)
        if s == "load":
            stage_load()
        elif s == "grade":
            stage_grade(a.no_grade)
        elif s == "assemble":
            if "wing_w" not in S:
                S["wing_w"], S["wing_ink"], S["wing_stats"] = classify_wing(S["wing"])
                S["tail_tip_sign"], _ = tail_axes(S["tail"])
            stage_assemble()
        elif s == "rig":
            stage_rig()
        elif s == "weights":
            stage_weights()
        elif s == "actions":
            stage_actions()
        elif s == "verify":
            stage_verify()
        elif s == "previews":
            stage_previews(poses=("actions" in stages))
        elif s == "export":
            stage_export()
    if a.save_wip:
        WORK.mkdir(parents=True, exist_ok=True)
        bpy.context.preferences.filepaths.save_version = 0
        bpy.ops.wm.save_as_mainfile(filepath=str(WORK / "jujak308_wip.blend"), compress=True)
    log("done")


if __name__ == "__main__":
    main()
