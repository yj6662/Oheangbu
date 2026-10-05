# #308 D308-4c review sheet: Art/Characters/Organ308/Review/organ308c_sheet.png (<= 2400 px wide), composed from the rendered tiles
# one tile at a time (streamed: each tile is opened, pasted and closed; nothing else is held). Plain Python 3 + Pillow (no Blender).
#   python Tools/Blender/Organ308/shard308_sheet.py      (after shard308_deploy.py — reads the master organ308_placements.json)
import json
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(r"C:/Users/yj666/Oheangbu")
OUT = ROOT / "Art/Characters/Organ308"
TILES = OUT / "Review/tiles_c"
DST = OUT / "Review/organ308c_sheet.png"
LAYOUT = [("shard", "dokkaebi"), ("agwi", "changgui"), ("bulgasari", "fox_spirit"), ("imugi", "growth_tree")]
TILE, GAP, PAD, LABEL_H = 372, 6, 24, 96
PAPER, INK, MUTED, ACCENT = (247, 241, 228), (42, 38, 34), (120, 112, 102), (190, 70, 30)


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


def tiles(draw, sheet, x, ty, tag, caps):
    for j, view in enumerate(("front", "q34", "close")):
        with Image.open(TILES / ("%s_%s.png" % (tag, view))) as src:
            im = src.convert("RGB")
            if im.size != (TILE, TILE):
                im = im.resize((TILE, TILE), Image.LANCZOS)
            tx = x + j * (TILE + GAP)
            sheet.paste(im, (tx, ty))
            im.close()
        draw.rectangle((tx, ty, tx + TILE - 1, ty + TILE - 1), outline=(200, 192, 180))
        cap_w = int(draw.textlength(caps[j], font=font(14))) + 14
        draw.rectangle((tx + 1, ty + TILE - 28, tx + cap_w, ty + TILE - 2), fill=PAPER)
        draw.text((tx + 7, ty + TILE - 25), caps[j], font=font(14), fill=MUTED)


def block_part(draw, sheet, x, y, row, width):
    ck = row["check"]
    draw.text((x, y), fit(draw, row["label"], font(21, True), width), font=font(21, True), fill=INK)
    px = ck["acT5"]["views"]["front12"]["pixels"]
    line2 = "뼈 %s%s · %s · 결정 %.0f cm(보이는 %.0f cm · 묻힘 %.0f %%) · 기울기 %.0f° · 테 뜸 %.1f cm · 12 m 정면 %d px%s" % (
        row["bone"]["name"], (" (%s)" % row["bone"]["humanBone"]) if row["bone"].get("humanBone") else "", row["role"],
        row["worldSize"] * 100, ck["visibleLengthM"] * 100, ck["buriedFraction"] * 100, row["tiltDeg"],
        max(ck["rimGapM"], ck["baseGapM"]) * 100, px, " (면제)" if ck["acT5"]["exempt"] else " · ±45° 최소 %d px" % ck["acT5"]["min12"])
    draw.text((x, y + 30), fit(draw, line2, font(15), width), font=font(15), fill=MUTED)
    draw.text((x, y + 52), fit(draw, row["reason"], font(14), width), font=font(14), fill=MUTED)
    if ck["notes"]:
        draw.text((x, y + 72), fit(draw, "※ " + ck["notes"][0], font(13), width), font=font(13), fill=ACCENT)
    tiles(draw, sheet, x, y + LABEL_H, row["entry"], ("정면 · 결정 강조", "3/4 · 결정 강조", "근접 · 실제 재질색"))


def block_shard(draw, sheet, x, y, shard, width):
    draw.text((x, y), "공통 마석 결정 — 큰 결정 하나, 모든 적이 같은 메시", font=font(21, True), fill=INK)
    s = shard["unityMeshBoundsSize"]
    m = shard["material"]
    line2 = "%s · 삼각형 %d (≤ %d) · 원본 %.1f×%.1f×%.1f cm · 뿌리 묻힘 %.0f %% · URP Lit 무광(매끈 %.2f) · 발광 없음 · %s" % (
        shard["name"], shard["triangles"], shard["maxTriangles"], s[0] * 100, s[1] * 100, s[2] * 100,
        shard["buriedFraction"] * 100, m["smoothness"], m["baseColorSRGB"])
    draw.text((x, y + 30), fit(draw, line2, font(15), width), font=font(15), fill=MUTED)
    draw.text((x, y + 52), fit(draw, "육각 결정 하나(넓고 좁은 면이 섞인 날것, 끌로 깎은 듯한 비뚤어진 끝). 아래 35 %는 몸속 뿌리 — 살에서 자라 나온 모습. 예고 덧칠만 LDR로 물든다.",
                               font(14), width), font=font(14), fill=MUTED)
    tiles(draw, sheet, x, y + LABEL_H, "shard", ("옆 · 뿌리 포함 전체", "3/4 · 몸 면에 박힌 모습", "위 · 끝 능선"))


def main():
    doc = json.loads((OUT / "organ308_placements.json").read_text(encoding="utf-8"))
    rows = {r["entry"]: r for r in doc["parts"]}
    block_w = 3 * TILE + 2 * GAP
    width = PAD * 2 + block_w * 2 + 48
    head_h = 120
    row_h = LABEL_H + TILE + 40
    height = head_h + row_h * len(LAYOUT) + 70
    sheet = Image.new("RGB", (width, height), PAPER)
    d = ImageDraw.Draw(sheet)
    d.text((PAD, 22), "#308 기관 부위 시안 C — 큰 마석 결정 하나 (D308-4c · TEST)", font=font(30, True), fill=INK)
    d.text((PAD, 66), fit(d, "주홍 = 리뷰 강조색(실제 재질 아님). 근접 칸만 실제 재질색. 뼈·자리는 시안 B 그대로(여우령만 같은 마디 안 60→25 %), 보이는 길이는 시안 B 크기의 약 2배. "
                             "12 m 화소 = 바인드 자세 오프라인 광선 계산(1920×1080, 세로 60°, 기준 ≥ 12 px). 이무기는 머리·앞몸만 확대.", font(16), width - 2 * PAD),
           font=font(16), fill=MUTED)
    for r, pair in enumerate(LAYOUT):
        y = head_h + r * row_h
        for c, tag in enumerate(pair):
            x = PAD + c * (block_w + 48)
            if tag == "shard":
                block_shard(d, sheet, x, y, doc["shard"], block_w)
            else:
                block_part(d, sheet, x, y, rows[tag], block_w)
    foot = ("도구: Tools/Blender/Organ308/shard308*.py (Blender 5.0 헤드리스) · 배치: Art/Characters/Organ308/organ308_placements.json (v3; v2 = .v2.json, v1 = .v1.json) · "
            "배포: _ProjectAssets/Art/Characters/Organs308/Shard308 · 시안 B 파일은 Retired/Shard308_v2 · 원본 종 FBX·프리팹·재질 무변경")
    d.text((PAD, height - 46), fit(d, foot, font(14), width - 2 * PAD), font=font(14), fill=MUTED)
    sheet.save(DST, optimize=True)
    print("SHEET", DST, sheet.size)


main()
