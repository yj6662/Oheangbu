# RETIRED (D308-4b, 2026-10-02): v1 per-species organ parts were replaced by ONE common magic-stone shard.
# Use Tools/Blender/Organ308/shard308_run.py. Kept for history only; running it would rebuild/delete the v1 deploy tree.
raise SystemExit('retired by D308-4b: use Tools/Blender/Organ308/shard308_run.py')
# #308 organ-art: build the deploy mirror from Parts/*.fbx + organ308_placements.json (plain Python, no Blender).
# Per part, in the folder organ308_deploy.local_dir(row) (named by the C# OrganSurface308 PartSpec.File id = deployId):
#   SM_Organ308_<deployId>.fbx  byte copy of Parts/SM_Organ308_<artId>.fbx
#   M_Organ308_<deployId>.mat   one matte URP Lit material (template = Folklore298 species material layout, URP Lit guid
#                               933532a4fcc9baf4fa0491de14d08ed7), no textures, no keywords (_EMISSION off), emission colour black
#   placement.json              OrganSurface308.Placement (bone, localPosition = mesh bounds centre, localEulerAngles, worldSize)
# Primaries -> _ProjectAssets/Art/Characters/Organs308/<deployId>/ ; alternates -> AlternateDeploy/<artId>/<deployId>/.
# Both trees are this track's generated output and are rebuilt from scratch. Unity assigns GUIDs on import (no .meta written).
#   python Tools/Blender/Organ308/make_materials.py
import json
import shutil
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import organ308_deploy as DP  # noqa: E402
import unity_prefab as UP  # noqa: E402

ROOT = Path(r"C:/Users/yj666/Oheangbu")
OUT = ROOT / "Art/Characters/Organ308"
MIRROR = OUT / "_ProjectAssets/Art/Characters/Organs308"
ALT = OUT / "AlternateDeploy"
URP_LIT = "933532a4fcc9baf4fa0491de14d08ed7"
TEX = ("_BaseMap", "_BumpMap", "_DetailAlbedoMap", "_DetailMask", "_DetailNormalMap", "_EmissionMap", "_MainTex",
       "_MetallicGlossMap", "_OcclusionMap", "_ParallaxMap", "_SpecGlossMap", "unity_Lightmaps", "unity_LightmapsInd",
       "unity_ShadowMasks")


def srgb(hexstr):
    h = hexstr.lstrip("#")
    return [round(int(h[i:i + 2], 16) / 255.0, 6) for i in (0, 2, 4)]


def material(name, color, metallic, smoothness):
    r, g, b = color
    tex = "".join("    - %s:\n        m_Texture: {fileID: 0}\n        m_Scale: {x: 1, y: 1}\n        m_Offset: {x: 0, y: 0}\n" % t
                  for t in TEX)
    floats = dict(_AddPrecomputedVelocity=0, _AlphaClip=0, _AlphaToMask=0, _Blend=0, _BlendModePreserveSpecular=1,
                  _BumpScale=1, _ClearCoatMask=0, _ClearCoatSmoothness=0, _Cull=2, _Cutoff=0.5,
                  _DetailAlbedoMapScale=1, _DetailNormalMapScale=1, _DstBlend=0, _DstBlendAlpha=0,
                  _EnvironmentReflections=1, _GlossMapScale=0, _Glossiness=0, _GlossyReflections=0,
                  _Metallic=metallic, _OcclusionStrength=1, _Parallax=0.005, _QueueOffset=0, _ReceiveShadows=1,
                  _Smoothness=smoothness, _SmoothnessTextureChannel=0, _SpecularHighlights=1, _SrcBlend=1,
                  _SrcBlendAlpha=1, _Surface=0, _WorkflowMode=1, _XRMotionVectorsPass=1, _ZWrite=1)
    fl = "".join("    - %s: %s\n" % (k, v) for k, v in floats.items())
    return ("%%YAML 1.1\n%%TAG !u! tag:unity3d.com,2011:\n--- !u!21 &2100000\nMaterial:\n  serializedVersion: 8\n"
            "  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {fileID: 0}\n  m_PrefabInstance: {fileID: 0}\n"
            "  m_PrefabAsset: {fileID: 0}\n  m_Name: %s\n  m_Shader: {fileID: 4800000, guid: %s, type: 3}\n"
            "  m_Parent: {fileID: 0}\n  m_ModifiedSerializedProperties: 0\n  m_ValidKeywords: []\n  m_InvalidKeywords: []\n"
            "  m_LightmapFlags: 4\n  m_EnableInstancingVariants: 0\n  m_DoubleSidedGI: 0\n  m_CustomRenderQueue: -1\n"
            "  stringTagMap:\n    RenderType: Opaque\n  disabledShaderPasses:\n  - MOTIONVECTORS\n  m_LockedProperties: \n"
            "  m_SavedProperties:\n    serializedVersion: 3\n    m_TexEnvs:\n%s    m_Ints: []\n    m_Floats:\n%s"
            "    m_Colors:\n    - _BaseColor: {r: %s, g: %s, b: %s, a: 1}\n    - _Color: {r: %s, g: %s, b: %s, a: 1}\n"
            "    - _EmissionColor: {r: 0, g: 0, b: 0, a: 1}\n    - _SpecColor: {r: 0.19999996, g: 0.19999996, b: 0.19999996, a: 1}\n"
            "  m_BuildTextureStacks: []\n  m_AllowLocking: 1\n--- !u!114 &1238892327208702662\nMonoBehaviour:\n"
            "  m_ObjectHideFlags: 11\n  m_CorrespondingSourceObject: {fileID: 0}\n  m_PrefabInstance: {fileID: 0}\n"
            "  m_PrefabAsset: {fileID: 0}\n  m_GameObject: {fileID: 0}\n  m_Enabled: 1\n  m_EditorHideFlags: 0\n"
            "  m_Script: {fileID: 11500000, guid: d0353a89b1f911e48b9e16bdc9f2e058, type: 3}\n  m_Name: \n"
            "  m_EditorClassIdentifier: Unity.RenderPipelines.Universal.Editor::UnityEditor.Rendering.Universal.AssetVersion\n"
            "  version: 10\n") % (name, URP_LIT, tex, fl, r, g, b, r, g, b)


def check_prefab_centre(row, pl):
    """Bounds centre via the placement.json path (W @ localPosition) == via the part's own TRS (W @ TRS(local) @ c)."""
    if not row.get("prefab"):
        return 0.0
    pf = UP.Prefab(ROOT / "Oheangbu" / row["prefab"])
    hits = pf.by_name(row["bone"]["name"])
    if len(hits) != 1:
        raise SystemExit("bone %s not unique in %s" % (row["bone"]["name"], row["prefab"]))
    W = pf.world(hits[0])
    loc = row["local"]
    part = UP.mul(W, UP.trs(loc["position"], loc["rotation"], loc["scale"]))
    a = UP.apply(part, row["mesh"]["unityMeshBoundsCenter"])
    lp = pl["localPosition"]
    b = UP.apply(W, [lp["x"], lp["y"], lp["z"]])
    return sum((a[i] - b[i]) ** 2 for i in range(3)) ** 0.5


def main():
    master = OUT / "organ308_placements.json"
    doc = json.loads(master.read_text(encoding="utf-8"))
    slots = {}
    for p in doc["parts"]:
        DP.apply(p)
        m = p["material"]
        if m["emission"] or m["smoothness"] > 0.2:
            raise SystemExit("organ parts must be matte without emission: " + p["id"])
        if not p.get("alternateOf"):
            if p["deployId"] in slots:
                raise SystemExit("two primaries for slot %s: %s, %s" % (p["deployId"], slots[p["deployId"]], p["id"]))
            slots[p["deployId"]] = p["id"]
    for tree in (MIRROR, ALT):  # generated output only
        if tree.exists():
            shutil.rmtree(tree)
    for p in doc["parts"]:
        d = OUT / DP.local_dir(p)
        d.mkdir(parents=True, exist_ok=True)
        src = ROOT / p["fbx"]
        shutil.copyfile(src, d / ("SM_Organ308_%s.fbx" % p["deployId"]))
        m = p["material"]
        (d / ("M_Organ308_%s.mat" % p["deployId"])).write_text(
            material(m["name"], srgb(m["baseColorSRGB"]), m["metallic"], m["smoothness"]), encoding="utf-8", newline="\n")
        pl = DP.placement_json(p)
        err = check_prefab_centre(p, pl)
        if err > 1e-4:
            raise SystemExit("placement centre mismatch %.6f m for %s" % (err, p["id"]))
        (d / "placement.json").write_text(json.dumps(pl, ensure_ascii=False, indent=1), encoding="utf-8", newline="\n")
        print("DEPLOY", p["id"], "->", d.relative_to(OUT), "centreCheck %.2e m" % err, "euler", pl["localEulerAngles"])
    text = json.dumps(doc, ensure_ascii=False, indent=1)
    master.write_text(text, encoding="utf-8")
    (MIRROR / "organ308_placements.json").write_text(text, encoding="utf-8")


main()
