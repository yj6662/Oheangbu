# #308 D308-4c/4e/4d crystal deploy (plain Python, no Blender): merges Analysis/shard308_<entry>.json rows (or, for an entry whose
# element — D308-4e, Analysis/shard308e_elements.json — is not neutral, Analysis/shard308e_<entry>_<Element>.json) into
#   Art/Characters/Organ308/organ308_placements.json        master, schema organ308-placement/4 (v3 kept as organ308_placements.v3.json,
#                                                            v2 as organ308_placements.v2.json, v1 as organ308_placements.v1.json)
#   _ProjectAssets/Art/Characters/Organs308/Shard308/        deploy mirror -> Oheangbu/Assets/_Project/Art/Characters/Organs308/Shard308/
#       SM_Organ308_Shard.fbx          written by shard308.py build (not touched here) — the neutral (무속성) v3 crystal
#       SM_Organ308_Shard_<Element>.fbx written by shard308e.py build — the five element shapes (D308-4e)
#       M_Organ308_Shard.mat           ONE matte URP Lit material shared by every shard renderer (no textures, no keywords, black emission)
#       shard308_placements.json       C# contract (OrganSurface308.ShardDoc, JsonUtility): per owner bone, localPosition = MESH BOUNDS
#                                      CENTRE in bone space (of the row's own mesh), localEulerAngles (Unity ZXY), worldSize = world max
#                                      extent (m) of the row's mesh; schema shard308-placement/2 ADDS per row: element, mesh (FBX base
#                                      name), contamMode (Texture | Sphere | None), contamMask (Unity path, R8), contamRadius (m),
#                                      contamLocalPoint (bone-local crystal pivot) — D308-4e/4d; a v1 reader ignores the new fields
#   _ProjectAssets/Art/Characters/Organs308/Contam308/       T_Organ308_Contam_<species>.png (contam308.py bake) -> Organs308/Contam308/
# Same file names as v2/v3: the live .meta files keep the GUIDs (no .meta written here). Run through shard308_run.py / shard308e_run.py.
import json
import shutil
import sys
from datetime import date
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import unity_math as QM  # noqa: E402
import shard308e_data as E  # noqa: E402

ROOT = Path(r"C:/Users/yj666/Oheangbu")
OUT = ROOT / "Art/Characters/Organ308"
ANALYSIS = OUT / "Analysis"
DEPLOY = OUT / "_ProjectAssets/Art/Characters/Organs308/Shard308"
ENTRIES = ["dokkaebi", "agwi", "changgui", "bulgasari", "fox_spirit", "imugi", "growth_tree"]
URP_LIT = "933532a4fcc9baf4fa0491de14d08ed7"
TEX = ("_BaseMap", "_BumpMap", "_DetailAlbedoMap", "_DetailMask", "_DetailNormalMap", "_EmissionMap", "_MainTex",
       "_MetallicGlossMap", "_OcclusionMap", "_ParallaxMap", "_SpecGlossMap", "unity_Lightmaps", "unity_LightmapsInd",
       "unity_ShadowMasks")

CONVENTIONS = {
    "decision": "D308-4c (D308-4b): every enemy carries the SAME magic-stone crystal mesh — ONE large matte crystal, embedded deep; per species only bone, place, size and a slight tilt.",
    "unityFromBlender": "Unity prefab-root = VisualScale * (-x, z, -y) + (0, VisualY, 0) (v1 bone fit, <=1e-5 m humanoid).",
    "boneFrame": "Unity bone rotation = R * BlenderBoneRest * diag(-1,1,1) (v1, all six species).",
    "mesh": "SM_Organ308_Shard.fbx in metres, exported axis_forward=-Z axis_up=Y, FBX_SCALE_UNITS, bake_space_transform (v1 smoke: Unity root rot 0 / scale 1). One irregular hexagonal prism (axis 0.30 m, nominal radius 0.05 m at scale 1; bounds 10.0 x 30.0 x 8.5 cm) with a chisel (two-apex) termination. Unity mesh-local +Y = crystal axis (outward), -Z = 'up along the body', origin = where the axis meets the body surface; the lower 35 % of the axis is a tapered root that sits inside the body.",
    "unity": "unity.localPosition = mesh BOUNDS CENTRE in bone space (OrganSurface308.EnsurePart: position = bone.TransformPoint(localPosition), rotation = bone.rotation * Euler(localEulerAngles), world scale = worldSize / max(mesh.bounds.size), localScale = world scale / bone.lossyScale). Values at the FBX bind pose.",
    "bone": "bone = the v2 (D308-4b) bone, kept; at every v3 spot it is also the bone with the dominant skin weight (skinWeights, top 4; split weights are listed in the Spec), so the crystal follows the skin it sits on; the tree uses the scene object Tree_00 (lossy 3.0, v1 read).",
    "surface": "surface.* are bone-local: surfaceLocalPoint = crystal tip (outermost vertex along the crystal axis = counter-stroke landing), surfaceLocalNormal = crystal axis, suggestedRadiusWorldM = 1.05 x bounding radius (OrganSurface308.ComputeSurface grows the organ radius to the same value from the imported mesh).",
    "size": "targetVisibleM = the visible axis length asked for (~2x the v2 size, D308-4c); scale k = targetVisibleM / (0.65 x 0.30 m); worldSize = k x mesh max extent (C# world size). check.visibleLengthM = measured: tip -> first body hit along -axis.",
    "tilt": "the crystal axis = the body normal (averaged over the crystal footprint) leaned by tiltDeg toward the frame's 'up' (humanoids / bulgasari / imugi / tree: up along the body; fox: toward the tail tip); if the visible crystal ran into the body the lean is turned about the normal (check.tiltTurnDeg) or reduced (check.tiltUsedDeg).",
    "sink": "check.sinkM: pushed in along -n in 0.5 % L steps until the root is closed all round (check.rimGapM <= 5 mm) with the buried fraction (check.buriedFraction, along the axis) in .30-.40. rimGapM = max distance outside the body of the designed-buried samples (root edges, faces, bottom; inside = back-face ray test, distance = nearest surface); baseGapM = the v2 metric (buried vertices, inward ray along -n).",
    "acT5": "check.acT5: offline bind-pose ray cast of a Unity 1920x1080 / 60° vertical fov frame, horizontal camera at the crystal height, 12 m front and ±45° (+ 8 / 4 m front): pixels whose first hit is the crystal (the body occludes). AC-T5 >= 12 px [TEST]; the fox (Spine, tail tip faces back) is exempt.",
    "role": "Core = lights for every elemental key ('*'); fox_spirit = Spine (tail tip faces away from the actor front: the facing selection and the AC-T5 authoring front check skip Spine; a single Spine organ is always lit inside the window).",
}


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


def xyz(v):
    return dict(x=v[0], y=v[1], z=v[2])


def row_for(entry, emap):
    """the deployed analysis row: the v3 (neutral) row, or the element-shape row of shard308e.py prep (D308-4e)"""
    el = emap[entry]["element"]
    name = ("shard308_%s.json" % entry) if el == E.NEUTRAL else ("shard308e_%s_%s.json" % (entry, el))
    r = json.loads((ANALYSIS / name).read_text(encoding="utf-8"))
    r["element"], r["mesh"], r["analysisFile"] = el, E.mesh_name(el), name
    return r


def contamination(entry):
    """D308-4d row fields from contam308.py bake (species). The growth-lesson tree has none (Mesh_Tree: 3 submeshes)."""
    rep = E.CONTAM_DIR / ("%s_bake.json" % entry)
    if entry not in E.SPECIES or not rep.exists():
        why = "Mesh_Tree has 3 submeshes (the overlay redraws only the last one) - Spec remaining work" if entry == "growth_tree" else "not baked"
        return dict(contamMode="None", contamMask="", contamRadius=0.0, contamNote="no contamination: " + why)
    b = json.loads(rep.read_text(encoding="utf-8"))
    return dict(contamMode=b["mode"], contamMask=b.get("unityMask", "") if b["mode"] == "Texture" else "", contamRadius=b["radiusM"],
                contamNote=b["reason"])


def main():
    mesh = json.loads((ANALYSIS / "shard308_mesh.json").read_text(encoding="utf-8"))
    emesh = json.loads((ANALYSIS / "shard308e_mesh.json").read_text(encoding="utf-8"))
    emap = E.element_map()
    rows = [row_for(e, emap) for e in ENTRIES]
    m = mesh["material"]
    if m["emission"] or m["smoothness"] > 0.2 or m["metallic"] > 0.0:
        raise SystemExit("the shard must be matte without emission")
    if mesh["triangles"] > mesh["maxTriangles"]:
        raise SystemExit("shard over the triangle budget")
    shapes = [dict(element=k, mesh=v["name"], triangles=v["triangles"], islands=v["islands"], topH=v["topH"],
                   meshBoundsCenter=xyz(v["unityMeshBoundsCenter"]), meshBoundsSize=xyz(v["unityMeshBoundsSize"]))
              for k, v in emesh["shapes"].items()]
    if any(sh["triangles"] > mesh["maxTriangles"] for sh in shapes):
        raise SystemExit("an element shape is over the triangle budget")
    owners = [r["owner"] for r in rows]
    if len(set(owners)) != len(owners):
        raise SystemExit("two placements for one owner")
    placements = []
    for r in rows:
        u = r["unity"]
        q = u["localRotation"]
        if QM.q_angle_deg(QM.euler_to_q(u["localEulerAngles"]), tuple(q)) > 0.01:
            raise SystemExit("Euler round trip for " + r["owner"])
        placements.append(dict(
            owner=r["owner"], organ=r["organ"], role=r["role"], keys=r["attackKeys"], bone=r["bone"]["name"],
            bonePath=r["bone"]["path"], localPosition=xyz(u["localPosition"]), localEulerAngles=xyz(u["localEulerAngles"]),
            localRotation=dict(x=q[0], y=q[1], z=q[2], w=q[3]), worldSize=r["worldSize"],
            radius=r["surface"]["suggestedRadiusWorldM"], surfaceLocalPoint=xyz(r["surface"]["surfaceLocalPoint"]),
            surfaceLocalNormal=xyz(r["surface"]["surfaceLocalNormal"]), frontDotBind=r["check"]["frontDotBind"],
            visibleLengthM=r["check"]["visibleLengthM"], buriedFraction=r["check"]["buriedFraction"], tiltDeg=r["tiltDeg"],
            label=r["label"], element=r["element"], mesh=r["mesh"], elementSource=emap[r["entry"]]["source"],
            contamLocalPoint=xyz(u["pivotLocalPosition"]), **contamination(r["entry"])))
    c = srgb(m["baseColorSRGB"])
    deploy_doc = dict(schema="shard308-placement/2", decision="D308-4c + D308-4e (element shapes) + D308-4d (contamination)",
                      meshVersion=mesh.get("version", "v3"), status="TEST (user capture check pending)", generated=str(date.today()),
                      tool="Tools/Blender/Organ308/shard308_deploy.py",
                      mesh=mesh["name"] + ".fbx", material=m["name"] + ".mat", organ=rows[0]["organ"],
                      baseColor=dict(r=c[0], g=c[1], b=c[2], a=1.0), smoothness=m["smoothness"], metallic=m["metallic"],
                      maxTriangles=mesh["maxTriangles"], triangles=mesh["triangles"],
                      meshBoundsCenter=xyz(mesh["unityMeshBoundsCenter"]), meshBoundsSize=xyz(mesh["unityMeshBoundsSize"]),
                      shapes=shapes, placements=placements)
    DEPLOY.mkdir(parents=True, exist_ok=True)
    (DEPLOY / "shard308_placements.json").write_text(json.dumps(deploy_doc, ensure_ascii=False, indent=1), encoding="utf-8", newline="\n")
    (DEPLOY / (m["name"] + ".mat")).write_text(material(m["name"], c, m["metallic"], m["smoothness"]), encoding="utf-8", newline="\n")
    cur = OUT / "organ308_placements.json"
    # keep the v3 master once (D308-4c) before the first v4 write, and the v2 master once before the first v3 write
    for keep, schema in ((OUT / "organ308_placements.v3.json", "organ308-placement/3"), (OUT / "organ308_placements.v2.json", "organ308-placement/2")):
        if not keep.exists() and cur.exists() and json.loads(cur.read_text(encoding="utf-8")).get("schema") == schema:
            shutil.copy2(cur, keep)
    contam = {e: json.loads((E.CONTAM_DIR / ("%s_bake.json" % e)).read_text(encoding="utf-8")) for e in E.SPECIES
              if (E.CONTAM_DIR / ("%s_bake.json" % e)).exists()}
    fits = {e: json.loads((ANALYSIS / ("shard308e_fit_%s.json" % e)).read_text(encoding="utf-8")) for e in ENTRIES
            if (ANALYSIS / ("shard308e_fit_%s.json" % e)).exists()}
    master = dict(schema="organ308-placement/4", spec="Docs/Specs/SPEC-TELEGRAPH-ORGAN-308.md",
                  decision="D308-4c (D308-4b) + D308-4e element crystal shapes + D308-4d contamination around the crystal",
                  status="TEST (user capture check pending - prefabs gate)", generated=str(date.today()),
                  tool="Tools/Blender/Organ308/shard308.py + shard308e.py + contam308.py (+ shard308_deploy.py)",
                  previous=["organ308_placements.v3.json (D308-4c, one crystal for every enemy)",
                            "organ308_placements.v2.json (D308-4b shard cluster; files in Retired/Shard308_v2)",
                            "organ308_placements.v1.json (per-species parts, retired)"],
                  conventions=CONVENTIONS, shard=mesh,
                  elements=dict(rule="D308-4e: crystal shape = the actor's element (attack profile Elemental/Element); neutral = the v3 crystal",
                                map={e: emap[e] for e in ENTRIES}, shapes=emesh["shapes"]),
                  contamination={e: {k: v.get(k) for k in ("radiusM", "factor", "mode", "reason", "resolution", "regionConflictPct",
                                                           "uvOverlapTexelsPct", "regionTexelMedianM", "veinCoverageOfRegionPct",
                                                           "nearCoveragePct", "farCoveragePct", "conflictDroppedTexels", "conflictDroppedVeinTexels", "components", "shellBridges",
                                                           "geodesicVsSphere", "unityMask")} for e, v in contam.items()},
                  fitAtV3Pose={e: {k: dict(ok=v["ok"], overhangM=v["overhangM"]) for k, v in f["fit"].items()} for e, f in fits.items()},
                  parts=rows,
                  sceneOnly=dict(mineBoss306="MineTutorialBoss306 keeps its teal-crystal colour-key mask (Mask mode, both shoulders Core) - no shard, no contamination",
                                 southGate="south_gate_general keeps the spear tip ReusedMetalTip (Part) - no shard",
                                 cheongryong="mouth / eyes Sphere + ridge mask chain - no shard (unchanged)",
                                 mineFire="mine_fire/0 ember mask - no shard (neutral while D308-2 holds)"))
    text = json.dumps(master, ensure_ascii=False, indent=1)
    (OUT / "organ308_placements.json").write_text(text, encoding="utf-8", newline="\n")
    for p in placements:
        print("DEPLOY", p["owner"], p["bone"], p["role"], p["element"], p["mesh"], "size", p["worldSize"], "radius", p["radius"],
              "visible", p["visibleLengthM"], "buried", p["buriedFraction"], "contam", p["contamMode"], p["contamRadius"])
    print("DEPLOY ->", DEPLOY)


main()
