# #308 D308-4e/4d review sheet: Art/Characters/Organ308/Review/organ308d_sheet.png — the six crystal shapes side by side, then each species
# front + close-up with its crystal and the contamination (idle) + the telegraph-window tint flow. Blender Workbench renders of an sRGB
# APPROXIMATION of the shader look (ink-wash approximation of the base colour, darkening toward 검보라·묵색, tint on the veins) — labelled.
# Streamed: each tile is opened, pasted and closed. Plain Python 3 + Pillow.   python Tools/Blender/Organ308/shard308e_sheet.py
import json
import sys
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

sys.path.insert(0, str(Path(__file__).resolve().parent))
import shard308e_data as D  # noqa: E402

TILE, GAP, PAD, LABEL_H = 372, 6, 24, 100
PAPER, INK, MUTED, ACCENT = (247, 241, 228), (42, 38, 34), (120, 112, 102), (190, 70, 30)
LAYOUT = [("dokkaebi", "agwi"), ("changgui", "bulgasari"), ("fox_spirit", "imugi"), ("growth_tree", None)]


def font(size, bold=False):
    for name in (("malgunbd.ttf" if bold else "malgun.ttf"), "malgun.ttf", "arial.ttf"):
        p = Path("C:/Windows/Fonts") / name
        if p.exists():
            return ImageFont.truetype(str(p), size)
    return ImageFont.load_default()


def fit(draw, text, f, width):
    if draw.textlength(text, font=f) <= width:
        return text
    while text and draw.textlength(text + "…", font=f) > width:
        text = text[:-1]
    return text + "…"


def paste(draw, sheet, path, x, y, cap):
    if path.exists():
        with Image.open(path) as src:
            im = src.convert("RGB")
            if im.size != (TILE, TILE):
                im = im.resize((TILE, TILE), Image.LANCZOS)
            sheet.paste(im, (x, y))
            im.close()
    else:
        draw.rectangle((x, y, x + TILE - 1, y + TILE - 1), fill=(236, 230, 218))
        draw.text((x + 14, y + TILE // 2 - 10), "(없음)", font=font(16), fill=MUTED)
    draw.rectangle((x, y, x + TILE - 1, y + TILE - 1), outline=(200, 192, 180))
    if cap:
        cw = int(draw.textlength(cap, font=font(14))) + 14
        draw.rectangle((x + 1, y + TILE - 28, x + cw, y + TILE - 2), fill=PAPER)
        draw.text((x + 7, y + TILE - 25), cap, font=font(14), fill=MUTED)


def main():
    master = json.loads((D.OUT / "organ308_placements.json").read_text(encoding="utf-8"))
    shapes = master["elements"]["shapes"]
    emap = master["elements"]["map"]
    contam = master.get("contamination", {})
    fits = master.get("fitAtV3Pose", {})
    rows = {r["entry"]: r for r in master["parts"]}
    block_w = 3 * TILE + 2 * GAP
    width = PAD * 2 + block_w * 2 + 48
    head_h, shape_h = 132, 96 + 2 * TILE + GAP + 30
    row_h = LABEL_H + TILE + 40
    height = head_h + shape_h + row_h * len(LAYOUT) + 80
    sheet = Image.new("RGB", (width, height), PAPER)
    d = ImageDraw.Draw(sheet)
    d.text((PAD, 20), "#308 시안 D — 속성별 마석 결정(D308-4e) · 결정 둘레 마석 오염(D308-4d) · TEST", font=font(30, True), fill=INK)
    d.text((PAD, 64), fit(d, "※ 근사 렌더: Blender Workbench(sRGB) — 바탕색을 먹·한지 톤으로 근사하고 오염은 검보라·묵색 쪽으로 어둡게 눌렀다. 실제 모습은 셰이더"
                             "(InkOrganSurface 덧칠, InkWash297 뒤 투명 큐, 선형)가 정한다. 예고 창 칸의 물듦은 시험용 속성 '화' 가정이다.", font(16), width - 2 * PAD),
           font=font(16), fill=ACCENT)
    d.text((PAD, 90), fit(d, "결정 색 = 무광 #35413E(발광 없음). 오염 = 평소 무채(검보라·묵색), 예고 창 안에서만 속성색 LDR(≤ .85)이 결정 쪽에서 결을 따라 바깥으로 번진다. "
                             "설화 6종은 지금 콘텐츠에서 모두 무속성(공격 프로필 Elemental 0)이라 기본형 결정이고 실제로는 물들지 않는다. 성장 교습만 목(목 덩굴 프로필).",
                             font(15), width - 2 * PAD), font=font(15), fill=MUTED)
    # ---- the six shapes
    y0 = head_h
    d.text((PAD, y0), "결정 산형 6종 — 같은 축척(배율 1), 위: 옆(뿌리 포함 전체) / 아래: 몸 면에 박힌 3/4", font=font(21, True), fill=INK)
    used = {}
    for e, m in emap.items():
        used.setdefault(m["element"], []).append(e)
    sx0 = PAD + (width - 2 * PAD - 6 * TILE - 5 * GAP) // 2
    for i, el in enumerate(D.SHAPES):
        sh = shapes[el]
        x = sx0 + i * (TILE + GAP)
        d.text((x, y0 + 34), fit(d, D.KO[el], font(16, True), TILE), font=font(16, True), fill=INK)
        d.text((x, y0 + 58), fit(d, "삼각형 %d · 조각 %d · 끝 h %.2f%s" % (sh["triangles"], sh["islands"], sh["topH"],
                                                                         " · 사용: " + ", ".join(used[el]) if el in used else " · 사용 없음(속성 적 없음)"),
                                 font(13), TILE), font=font(13), fill=MUTED)
        paste(d, sheet, D.TILE_DIR / ("shape_%s_side.png" % el), x, y0 + 84, "옆")
        paste(d, sheet, D.TILE_DIR / ("shape_%s_sunk.png" % el), x, y0 + 84 + TILE + GAP, "박힘 3/4")
    # ---- species blocks
    for r, pair in enumerate(LAYOUT):
        y = head_h + shape_h + r * row_h
        for c, entry in enumerate(pair):
            if entry is None:
                continue
            x = PAD + c * (block_w + 48)
            row = rows[entry]
            el = emap[entry]["element"]
            ct = contam.get(entry)
            d.text((x, y), fit(d, row["label"] + " · 결정 " + D.KO_SHORT[el], font(20, True), block_w), font=font(20, True), fill=INK)
            if ct:
                g = ct["geodesicVsSphere"]
                line2 = ("오염 반지름 %.2f m(보이는 결정 %.0f cm × %.1f, 측지 거리) · %s · UV0 %d² · 영역 결 %.0f %% (가까이 %.0f %% / 바깥 %.0f %%) · "
                         "UV 겹침 충돌 %.1f %%%s" % (ct["radiusM"], row["check"]["visibleLengthM"] * 100, ct["factor"],
                                                  "마스크(UV0)" if ct["mode"] == "Texture" else "구 대체(월드 거리)", ct["resolution"],
                                                  ct["veinCoverageOfRegionPct"], ct["nearCoveragePct"], ct["farCoveragePct"], ct["regionConflictPct"],
                                                  (" (먼 면과 공유한 결 텍셀 %d 버림)" % ct["conflictDroppedVeinTexels"]) if ct.get("conflictDroppedVeinTexels") else ""))
                line3 = ("구(유클리드)였다면 정점 %d개 중 %d개가 다른 부위로 번졌을 것(측지 %d) · 껍질 %d개, 맞닿은 껍질 다리 %d" %
                         (g["sphereVertices"], g["sphereBleedVertices"], g["geodesicVertices"], ct["components"], ct["shellBridges"]))
            else:
                line2 = "오염 없음 — 나무 Mesh_Tree는 서브메시 3이라 덧칠이 마지막(껍질) 서브메시에만 붙는다: 규칙 예외가 필요해 이번에 넣지 않음(Spec 남은 일)"
                line3 = "결정 목 산형: 보이는 %.0f cm · 묻힘 %.0f %% · 테 뜸 %.1f cm · 12 m 정면 %d px" % (
                    row["check"]["visibleLengthM"] * 100, row["check"]["buriedFraction"] * 100, row["check"]["rimGapM"] * 100,
                    row["check"]["acT5"]["views"]["front12"]["pixels"])
            d.text((x, y + 30), fit(d, line2, font(14), block_w), font=font(14), fill=MUTED)
            d.text((x, y + 52), fit(d, line3, font(14), block_w), font=font(14), fill=MUTED)
            fv = fits.get(entry, {})
            bad = [k for k, v in fv.items() if not v["ok"]]
            d.text((x, y + 74), fit(d, "v3 자리에 6산형 모두 박힘 검사: " + ("통과" if not bad else "실패 " + ", ".join(bad)) +
                                    " (테 뜸 ≤ 0.5 cm · 보이는 부분 몸 안 0)", font(13), block_w), font=font(13), fill=MUTED if not bad else ACCENT)
            ty = y + LABEL_H
            paste(d, sheet, D.TILE_DIR / ("%s_d_front.png" % entry), x, ty, "정면 · 평소(근사)")
            paste(d, sheet, D.TILE_DIR / ("%s_d_close_idle.png" % entry), x + TILE + GAP, ty,
                  "근접 · 평소 오염(무채)" if ct else "근접 · 결정(오염 없음)")
            paste(d, sheet, D.TILE_DIR / ("%s_d_close_tele.png" % entry), x + 2 * (TILE + GAP), ty,
                  "근접 · 예고 창 중간(속성 '화' 가정)" if ct else "")
    foot = ("도구: Tools/Blender/Organ308/shard308e.py · contam308.py · shard308e_sheet.py (Blender 5.0 헤드리스 + numpy) · 배치 organ308_placements.json(v4; v3 = .v3.json) · "
            "배포: _ProjectAssets/Art/Characters/Organs308/{Shard308, Contam308} · 원본 종 FBX·텍스처·프리팹·재질 무변경")
    d.text((PAD, height - 48), fit(d, foot, font(14), width - 2 * PAD), font=font(14), fill=MUTED)
    D.SHEET.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(D.SHEET, optimize=True)
    print("SHEET", D.SHEET, sheet.size)


main()
