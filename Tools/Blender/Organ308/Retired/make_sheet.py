# RETIRED (D308-4b, 2026-10-02): v1 per-species organ parts were replaced by ONE common magic-stone shard.
# Use Tools/Blender/Organ308/shard308_run.py. Kept for history only; running it would rebuild/delete the v1 deploy tree.
raise SystemExit('retired by D308-4b: use Tools/Blender/Organ308/shard308_run.py')
# #308 organ-art: merge per-entry placement fragments into organ308_placements.json (+ deploy mirror) and compose
# the review contact sheet Art/Characters/Organ308/Review/organ308_sheet.png (<= 2400 px wide) from rendered tiles.
# Plain Python 3 + Pillow (no Blender):  python Tools/Blender/Organ308/make_sheet.py
import json
import shutil
from datetime import date
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

import organ308_deploy as DP

ROOT = Path(r"C:/Users/yj666/Oheangbu")
OUT = ROOT / "Art/Characters/Organ308"
TILES = OUT / "Review/tiles"
ENTRIES = ["dokkaebi", "agwi", "changgui", "bulgasari", "fox_spirit", "imugi", "growth_tree"]
LAYOUT = [("dokkaebi_club_knot", "agwi_throat_knot"), ("changgui_claw_scar", "growth_trunk_knot"),
          ("bulgasari_back_iron", "bulgasari_jaw_iron"), ("fox_spirit_bead", "fox_spirit_tail_bead"),
          ("imugi_yeouiju_socket", "imugi_yeokrin")]
TILE, GAP, PAD, LABEL_H = 372, 12, 24, 78
PAPER, INK, MUTED, ACCENT, ALT = (247, 241, 228), (42, 38, 34), (120, 112, 102), (190, 70, 30), (60, 90, 140)

CONVENTIONS = {
    "unityFromBlender": "Unity prefab-root = VisualScale * (-x, z, -y) + (0, VisualY, 0); measured by bone fit (<=1e-5 m humanoid, <=0.0024 m generic except tail tips).",
    "boneFrame": "Unity bone rotation = R * BlenderBoneRest * diag(-1,1,1) (measured on all six species).",
    "local": "local.position/rotation/scale are Unity Transform values for the part GameObject parented to bone.path, computed at the FBX bind pose (not the prefab's current pose). rotation is (x,y,z,w).",
    "partMesh": "FBX in metres, exported axis_forward=-Z axis_up=Y, FBX_SCALE_UNITS, bake_space_transform=True (repo-measured: Unity root rot 0 / scale 1). Expected Unity mesh-local vertex = (-x, z, -y) of the Blender part-local vertex; mesh.unityMeshBoundsCenter/Size are the expected Mesh.bounds after import (INFERRED until the editor import confirms; a mismatch means the axis map is wrong).",
    "partAxes": "Unity mesh-local +Y = outward from the body (organ normal), -Z = 'up along the body', origin = attachment point on the body surface (beads: bead centre).",
    "deviations": [
        "changgui: spec text says 'UpperChest(없으면 Chest, 뼈 이름 Spine02)'; the avatar maps UpperChest=Spine, Chest=Spine01, Spine=Spine02, so the scar parents to bone 'Spine' (= HumanBodyBones.UpperChest).",
        "Tool folder follows the task (Tools/Blender/Organ308) instead of the spec's Tools/Blender/Organs308/organs.py; deploy asset paths follow the spec (Assets/_Project/Art/Characters/Organs308/<id>/).",
        "fox_spirit Tail_04/Tail_05 in PF_fox_spirit.prefab are posed ~5 cm/6 deg off the FBX bind; tail_bead local values are bind-relative (correct once the Animator drives the bone; edit-mode prefab view is also consistent because the skinned tail follows the same bone).",
        "Two parts per bulgasari (two bones), so the batch has 8 primary FBX + 2 alternates (spec counts 7 actors)."
    ],
    "localScale": "1 / parent lossy scale (part FBX is real-size metres; generic species bones carry the Visual scale).",
    "organ": "organ.* are bone-local (same frame as local.position). surfaceLocalPoint = outermost point along the outward normal (counter-stroke landing); suggestedRadiusWorldM >= bounding sphere (spec: Part-mode Radius >= part bounding radius).",
    "tree": "growth_trunk_knot parents to the SCENE object demo_growth_lesson/Chapter3_Visual/Tree_00 (not PF_PlantedTree.prefab, which the spell 검 also uses). Values assume Tree_00 lossy 3.0 (Tree_00 local 2.8 / rot y -18 deg, Chapter3_Visual 1.0714285, demo_growth_lesson 1 — identical in all three ledger scenes, read 2026-10-02; ancestors above demo_growth_lesson not checked). OrganSurface308 derives localScale from worldSize / Tree_00.lossyScale itself.",
    "fallback": "bone.fallback is informational (spec 'MouthOrigin(없으면 Head)'). local.* and placement.json are valid ONLY under bone.name; every bone.name was found unique in its prefab, so a fallback bone must not reuse these values (refuse instead).",
    "deploy": "Deploy ids follow the C# track (OrganSurface308 PartSpec.File): <deployId> = deployId field (fox_spirit_bead -> fox_spirit_mouth_bead, imugi_yeouiju_socket -> imugi_chin_pearl, growth_trunk_knot -> growth_knot; alternates share their primary's slot). Each Assets/_Project/Art/Characters/Organs308/<deployId>/ holds SM_Organ308_<deployId>.fbx, M_Organ308_<deployId>.mat and placement.json (OrganSurface308.Placement: bone, localPosition = MESH BOUNDS CENTRE in bone space, localEulerAngles = Unity ZXY Euler of local.rotation, worldSize = max mesh bounds size at world scale 1). Primaries are mirrored under _ProjectAssets/Art/Characters/Organs308/<deployId>/; an alternate is staged under AlternateDeploy/<artId>/<deployId>/ and replaces the primary folder only if the user picks it.",
}


def font(size, bold=False):
    for name in (("malgunbd.ttf" if bold else "malgun.ttf"), "malgun.ttf", "arial.ttf"):
        p = Path("C:/Windows/Fonts") / name
        if p.exists():
            return ImageFont.truetype(str(p), size)
    return ImageFont.load_default()


def merge():
    parts = []
    for e in ENTRIES:
        f = OUT / "Analysis" / ("%s_placement.json" % e)
        parts.extend(DP.apply(p) for p in json.loads(f.read_text(encoding="utf-8")))
    doc = dict(schema="organ308-placement/1", spec="Docs/Specs/SPEC-TELEGRAPH-ORGAN-308.md", decision="D308-4",
               batch="B1", status="TEST (사용자 실루엣 확인 전 — prefabs 단계 관문)", generated=str(date.today()),
               tool="Tools/Blender/Organ308/build_organs.py", conventions=CONVENTIONS, parts=parts)
    text = json.dumps(doc, ensure_ascii=False, indent=1)
    (OUT / "organ308_placements.json").write_text(text, encoding="utf-8")
    mirror = OUT / "_ProjectAssets/Art/Characters/Organs308/organ308_placements.json"
    mirror.parent.mkdir(parents=True, exist_ok=True)
    mirror.write_text(text, encoding="utf-8")
    return {p["id"]: p for p in parts}


def block(draw, sheet, x, y, part):
    alt = part["variant"] == "alternate"
    f1, f2 = font(21, True), font(15)
    draw.text((x, y), part["label"], font=f1, fill=ALT if alt else INK)
    size_cm = part["organ"]["boundingRadiusWorldM"] * 200
    line2 = "%s · 뼈 %s%s · %s · 삼각형 %d · 지름 약 %.0f cm · 정면 내적 %.2f" % (
        part["id"], part["bone"]["name"], (" (%s)" % part["bone"]["humanBone"]) if part["bone"].get("humanBone") else "",
        part["role"], part["mesh"]["triangles"], size_cm, part["check"]["frontDotBind"])
    draw.text((x, y + 30), line2, font=f2, fill=MUTED)
    if part["check"]["notes"]:
        draw.text((x, y + 50), "※ " + part["check"]["notes"][0], font=font(13), fill=ACCENT)
    ty = y + LABEL_H
    caps = ("정면 · 부위 강조", "3/4 · 부위 강조", "근접 · 실제 재질색")
    for j, view in enumerate(("front", "q34", "close")):
        im = Image.open(TILES / ("%s_%s.png" % (part["id"], view))).convert("RGB")
        if im.size != (TILE, TILE):
            im = im.resize((TILE, TILE), Image.LANCZOS)
        tx = x + j * (TILE + 6)
        sheet.paste(im, (tx, ty))
        draw.rectangle((tx, ty, tx + TILE - 1, ty + TILE - 1), outline=(200, 192, 180))
        cap_w = int(draw.textlength(caps[j], font=font(14))) + 14
        draw.rectangle((tx + 1, ty + TILE - 28, tx + cap_w, ty + TILE - 2), fill=PAPER)
        draw.text((tx + 7, ty + TILE - 25), caps[j], font=font(14), fill=MUTED)
    if alt:
        draw.rectangle((x - 8, y - 8, x + 3 * TILE + 12 + 8, ty + TILE + 8), outline=ALT, width=3)


def main():
    parts = merge()
    block_w = 3 * TILE + 12
    width = PAD * 2 + block_w * 2 + 48
    head_h = 120
    row_h = LABEL_H + TILE + 40
    height = head_h + row_h * len(LAYOUT) + 70
    sheet = Image.new("RGB", (width, height), PAPER)
    d = ImageDraw.Draw(sheet)
    d.text((PAD, 22), "#308 기관 부위 시안 (B1) — 모델에 더하는 무광 부위 · TEST · 사용자 확인 전", font=font(30, True), fill=INK)
    d.text((PAD, 66), "주홍 = 리뷰 강조색(실제 재질 아님). 근접 칸만 실제 재질색. 파란 테두리 = 대안(ALT) — 고르지 않으면 넣지 않는다. "
                      "모든 부위 무광·발광 없음, 삼각형 ≤ 1,500. 이무기는 머리·앞몸만 확대.", font=font(16), fill=MUTED)
    for r, pair in enumerate(LAYOUT):
        y = head_h + r * row_h
        for c, pid in enumerate(pair):
            block(d, sheet, PAD + c * (block_w + 48), y, parts[pid])
    foot = ("도구: Tools/Blender/Organ308 (Blender 5.0 헤드리스, 절차 메시) · 배치: Art/Characters/Organ308/organ308_placements.json · "
            "FBX: Art/Characters/Organ308/Parts · 원본 종 FBX·프리팹·재질 무변경")
    d.text((PAD, height - 46), foot, font=font(14), fill=MUTED)
    dst = OUT / "Review/organ308_sheet.png"
    sheet.save(dst, optimize=True)
    print("SHEET", dst, sheet.size)


main()
