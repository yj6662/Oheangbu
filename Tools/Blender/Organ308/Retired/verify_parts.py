# RETIRED (D308-4b, 2026-10-02): v1 per-species organ parts were replaced by ONE common magic-stone shard.
# Use Tools/Blender/Organ308/shard308_run.py. Kept for history only; running it would rebuild/delete the v1 deploy tree.
raise SystemExit('retired by D308-4b: use Tools/Blender/Organ308/shard308_run.py')
# #308 organ-art: offline check of every exported part FBX against organ308_placements.json.
# Re-imports each FBX into an empty scene (read-only on the FBX) and checks: one mesh object, identity object
# transform (bake_space_transform), triangles <= 1500, single material slot named M_Organ308_<id>, vertex bounds
# equal to the expected part-local bounds (Unity bounds mapped back), deploy copy byte-identical.
# Headless: blender -b --factory-startup -t 4 --python verify_parts.py
import bpy, hashlib, json, sys
from pathlib import Path
from mathutils import Vector

sys.path.insert(0, str(Path(__file__).resolve().parent))
import organ308_common as C  # noqa: E402
import organ308_deploy as DP  # noqa: E402
from io_scene_fbx import parse_fbx  # noqa: E402

SMOKE_AXES = {"UpAxis": [1], "UpAxisSign": [1], "FrontAxis": [2], "FrontAxisSign": [1], "CoordAxis": [0],
              "CoordAxisSign": [1], "UnitScaleFactor": [100.0]}


def _props(node):
    out = {}
    for p70 in node.elems:
        if p70.id == b"Properties70":
            for p in p70.elems:
                out[p.props[0].decode("utf-8", "replace")] = list(p.props[4:])
    return out


rows = json.loads((C.OUT / "organ308_placements.json").read_text(encoding="utf-8"))["parts"]
report, fails = [], 0
for row in rows:
    C.reset()
    fbx = C.ROOT / row["fbx"]
    ddir = C.OUT / DP.local_dir(row)
    deploy = ddir / ("SM_Organ308_%s.fbx" % row["deployId"])
    bpy.ops.import_scene.fbx(filepath=str(fbx))
    obs = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    res = dict(id=row["id"], checks={})
    ck = res["checks"]
    ck["oneMesh"] = len(obs) == 1
    o = obs[0]
    me = o.data
    me.calc_loop_triangles()
    # raw FBX: same GlobalSettings as the repo smoke cube measured in Unity (root rot 0 / scale 1), and the
    # Model node carries no Lcl transform (Blender's own re-import adds its axis matrix, so it is not used here)
    raw, _ = parse_fbx.parse(str(fbx))
    g = _props(next(e for e in raw.elems if e.id == b"GlobalSettings"))
    ck["fbxAxesYupZfrontXright_unit100"] = all(g.get(k) == v for k, v in SMOKE_AXES.items())
    models = [e for e in next(e for e in raw.elems if e.id == b"Objects").elems if e.id == b"Model"]
    mp = _props(models[0]) if len(models) == 1 else {}
    ck["fbxModelIdentity"] = len(models) == 1 and all(
        mp.get(k) in (None, d) for k, d in (("Lcl Translation", [0.0, 0.0, 0.0]), ("Lcl Rotation", [0.0, 0.0, 0.0]),
                                             ("Lcl Scaling", [1.0, 1.0, 1.0])))
    ck["triangles<=1500"] = len(me.loop_triangles) <= 1500
    ck["trianglesMatchJson"] = len(me.loop_triangles) == row["mesh"]["triangles"]
    mats = [m.name for m in me.materials]
    # one slot; the FBX keeps the art-id name (Unity never uses it: OrganSurface308 assigns M_Organ308_<deployId>.mat)
    ck["singleMaterial"] = mats == ["M_Organ308_" + row["id"]]
    lo, hi = C.bounds([o.matrix_world @ v.co for v in me.vertices])
    # expected part-local bounds = R^T applied to the Unity mesh bounds: b = (-ux, -uz, uy)
    uc, us = Vector(row["mesh"]["unityMeshBoundsCenter"]), Vector(row["mesh"]["unityMeshBoundsSize"])
    ec = Vector((-uc.x, -uc.z, uc.y))
    es = Vector((us.x, us.z, us.y))
    ck["boundsMatch"] = ((lo + hi) * 0.5 - ec).length < 1e-4 and ((hi - lo) - es).length < 1e-4
    ck["uv0"] = len(me.uv_layers) >= 1
    if me.materials:
        bsdf = next((n for n in me.materials[0].node_tree.nodes if n.type == "BSDF_PRINCIPLED"), None)
        em = 0.0
        if bsdf is not None:
            col = bsdf.inputs["Emission Color"].default_value if "Emission Color" in bsdf.inputs else (0, 0, 0, 1)
            em = max(col[0], col[1], col[2]) * (bsdf.inputs["Emission Strength"].default_value if "Emission Strength" in bsdf.inputs else 1.0)
        ck["noEmission"] = em <= 1e-6
    ck["deployCopyIdentical"] = deploy.exists() and hashlib.sha256(deploy.read_bytes()).hexdigest() == hashlib.sha256(fbx.read_bytes()).hexdigest()
    # C# contract (OrganSurface308): file ids by deployId, dedicated matte material, placement.json the tool places from
    matf = ddir / ("M_Organ308_%s.mat" % row["deployId"])
    mtxt = matf.read_text(encoding="utf-8") if matf.exists() else ""
    ck["deployMaterialMatte"] = ("m_Name: M_Organ308_%s\n" % row["deployId"]) in mtxt and "m_ValidKeywords: []" in mtxt and \
        "_EmissionColor: {r: 0, g: 0, b: 0, a: 1}" in mtxt and "_EMISSION" not in mtxt
    plf = ddir / "placement.json"
    pl = json.loads(plf.read_text(encoding="utf-8")) if plf.exists() else {}
    ck["placementJson"] = bool(pl) and pl.get("bone") == row["bone"]["name"] and pl.get("id") == row["deployId"] and \
        pl == DP.placement_json(row) and abs(pl["worldSize"] - max(row["mesh"]["unityMeshBoundsSize"])) < 1e-6
    res["triangles"] = len(me.loop_triangles)
    res["sha256"] = hashlib.sha256(fbx.read_bytes()).hexdigest()
    res["bytes"] = fbx.stat().st_size
    res["pass"] = all(ck.values())
    fails += 0 if res["pass"] else 1
    report.append(res)
# deploy mirror holds exactly one folder per primary slot (no stale art-id folders) + the JSON mirror
mirror = C.OUT / "_ProjectAssets/Art/Characters/Organs308"
want = sorted(set(r["deployId"] for r in rows if not r.get("alternateOf")))
have = sorted(d.name for d in mirror.iterdir() if d.is_dir())
mirror_ok = want == have and (mirror / "organ308_placements.json").read_bytes() == (C.OUT / "organ308_placements.json").read_bytes()
fails += 0 if mirror_ok else 1
out = dict(tool="Tools/Blender/Organ308/verify_parts.py", blender=bpy.app.version_string, parts=report,
           mirror=dict(expected=want, found=have, ok=mirror_ok), failures=fails)
(C.OUT / "Analysis" / "verify_parts.json").write_text(json.dumps(out, ensure_ascii=False, indent=1), encoding="utf-8")
print("ORGAN308_VERIFY failures=%d %s" % (fails, json.dumps([(r["id"], r["pass"]) for r in report])))
