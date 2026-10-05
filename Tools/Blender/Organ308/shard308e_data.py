# #308 D308-4e (속성별 결정 산형) + D308-4d (결정 둘레 마석 오염) — shared constants and the element scan. Plain Python (no bpy):
# imported by shard308e.py (Blender 5.0 headless), contam308.py, shard308_deploy.py and shard308e_sheet.py.
# Source assets are read-only. Every value is TEST.
import json
import re
from pathlib import Path

ROOT = Path(r"C:/Users/yj666/Oheangbu")
ASSETS = ROOT / "Oheangbu/Assets/_Project"
OUT = ROOT / "Art/Characters/Organ308"
ANALYSIS = OUT / "Analysis"
CONTAM_DIR = ANALYSIS / "Contam"                      # body exports (npz), bake reports, review approximation textures
TILE_DIR = OUT / "Review" / "tiles_d"
SHEET = OUT / "Review" / "organ308d_sheet.png"
MIRROR = OUT / "_ProjectAssets/Art/Characters/Organs308"
SHARD_DEPLOY = MIRROR / "Shard308"                    # -> Oheangbu/Assets/_Project/Art/Characters/Organs308/Shard308
CONTAM_DEPLOY = MIRROR / "Contam308"                  # -> Oheangbu/Assets/_Project/Art/Characters/Organs308/Contam308
UNITY_SHARD = "Assets/_Project/Art/Characters/Organs308/Shard308"
UNITY_CONTAM = "Assets/_Project/Art/Characters/Organs308/Contam308"

ENTRIES = ["dokkaebi", "agwi", "changgui", "bulgasari", "fox_spirit", "imugi", "growth_tree"]
SPECIES = ENTRIES[:6]

# D308-4e 오행 산형 (DECISIONS D308-4e): 목 곧은 긴 기둥 + 곁가지 / 화 뾰족한 가시 다발 / 토 낮고 두툼한 판·입방 덩이 /
# 금 둥근 쌍뿔 구슬 / 수 비틀려 휜 결정 / 무속성 = v3 기본형(큰 결정 하나). Order = Oheangbu.Core.Domain.Element (Wood..Water).
ELEMENTS = ["Wood", "Fire", "Earth", "Metal", "Water"]
NEUTRAL = "Neutral"
SHAPES = [NEUTRAL] + ELEMENTS
SHARD_NAME = "SM_Organ308_Shard"


def mesh_name(element):
    return SHARD_NAME if element in (None, NEUTRAL) else SHARD_NAME + "_" + element


KO = {"Neutral": "무속성 — 기본형(큰 결정 하나, v3)", "Wood": "목 — 곧게 뻗은 긴 기둥 + 곁가지 결정",
      "Fire": "화 — 뾰족한 가시 다발(불꽃 날)", "Earth": "토 — 낮고 두툼한 판·입방 덩이", "Metal": "금 — 둥근 쌍뿔 구슬(잔 면)",
      "Water": "수 — 비틀려 휜 결정"}
KO_SHORT = {"Neutral": "무속성", "Wood": "목", "Fire": "화", "Earth": "토", "Metal": "금", "Water": "수"}
# ElementPalette_Test.asset (Data/Configs) — the single colour source (ART-COLOR); review approximation only
PALETTE = {"Wood": (0.306, 0.502, 0.412), "Fire": (0.722, 0.361, 0.22), "Earth": (0.69, 0.553, 0.29),
           "Metal": (0.682, 0.706, 0.729), "Water": (0.2, 0.278, 0.361)}
CORRUPT_INK = (0.200, 0.169, 0.212)     # ElementPaletteSO._corruptInk #332B36 — ART-COLOR 오염색 [LOCKED] "번진 먹 — 검보라·묵색"
INK = (0.165, 0.149, 0.133)
PAPER = (0.969, 0.945, 0.894)

# where each entry's element comes from (live data, read-only). EnemyAttackProfileSO YAML: Elemental (0/1) + Element (enum index).
ELEMENT_SOURCE = {
    "dokkaebi": "Art/Characters/Folklore298/Data/Attack_dokkaebi.asset",
    "agwi": "Art/Characters/Folklore298/Data/Attack_agwi.asset",
    "changgui": "Art/Characters/Folklore298/Data/Attack_changgui.asset",
    "bulgasari": "Art/Characters/Folklore298/Data/Attack_bulgasari.asset",
    "fox_spirit": "Art/Characters/Folklore298/Data/Attack_fox_spirit.asset",
    "imugi": "Art/Characters/Folklore298/Data/Attack_imugi.asset",
    # Chapter3 lesson actor: EnemyController.Configure(WoodProfile()) — DemoChapterThreeSceneAuthoring.cs WoodProfile()
    "growth_tree": "Art/Demo/Chapter3/GrowthLesson_WoodVine.asset",
}


def scan_elements():
    """entry -> dict(element, elemental, elementIndex, source). Neutral when Elemental is 0 (COMBAT-DEFENSE: 무속성은 빛나지 않는다)."""
    out = {}
    for entry, rel in ELEMENT_SOURCE.items():
        text = (ASSETS / rel).read_text(encoding="utf-8")
        el = re.search(r"^\s*Elemental:\s*(\d+)", text, re.M)
        ix = re.search(r"^\s*Element:\s*(\d+)", text, re.M)
        elemental = bool(el and int(el.group(1)))
        index = int(ix.group(1)) if ix else 0
        out[entry] = dict(element=ELEMENTS[index] if elemental else NEUTRAL, elemental=elemental, elementIndex=index,
                          source="Oheangbu/Assets/_Project/" + rel,
                          rule="EnemyAttackProfileSO Elemental %d, Element %d (%s)" % (int(elemental), index, ELEMENTS[index]))
    return out


ELEMENT_MAP_JSON = ANALYSIS / "shard308e_elements.json"


def element_map():
    """entry -> dict(element, elemental, elementIndex, source, rule) as written by shard308e_run.py elements."""
    return json.loads(ELEMENT_MAP_JSON.read_text(encoding="utf-8"))["entries"]


# D308-4d contamination: radius = factor x the crystal's visible length (task 2.5-4x), measured as geodesic distance on the bind-pose skin
CONTAM_FACTOR = {"dokkaebi": 2.6, "agwi": 2.8, "changgui": 2.6, "bulgasari": 2.6, "fox_spirit": 3.2, "imugi": 3.0}
CONTAM_RES = 1024              # R8, body UV0 (bumped to 2048 for a species whose region texel is coarser than CONTAM_MAX_TEXEL_M)
CONTAM_MAX_TEXEL_M = 0.006
CONTAM_CONFLICT_MAX = 0.03     # UV overlap: region texels shared with far-away surface > 3 % -> world-distance Sphere fallback
CONTAM_DILATE_PX = 4


def contam_mask_name(entry):
    return "T_Organ308_Contam_%s.png" % entry
