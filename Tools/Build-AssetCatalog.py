#!/usr/bin/env python3
"""Build an offline Unity asset gallery and 64-entry contact sheets.

Run from any directory: python Tools/Build-AssetCatalog.py
Requires Pillow. The source catalog and optional visual review JSON stay unchanged.
"""

from __future__ import annotations

import argparse
import csv
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import sys
from urllib.parse import quote

from PIL import Image, ImageDraw, ImageFont, ImageOps, UnidentifiedImageError


REPO_ROOT = Path(__file__).resolve().parent.parent
IMAGE_SIZE = 128
CELL_HEIGHT = 170
COLUMNS = ROWS = 8
PAGE_SIZE = COLUMNS * ROWS


def read_json(path: Path):
    with path.open("r", encoding="utf-8-sig") as stream:
        return json.load(stream)


def relative_url(path: Path, output: Path) -> str:
    return quote(Path(os.path.relpath(path, output)).as_posix(), safe="/")


def preview_file(entry: dict, unity_root: Path) -> tuple[Path | None, str]:
    value = entry.get("preview")
    if not isinstance(value, str) or not value.strip():
        return None, "미생성"
    candidate = (unity_root / value).resolve()
    if not candidate.is_relative_to(unity_root):
        return None, "프로젝트 밖 경로"
    if not candidate.is_file():
        return None, "파일 없음"
    try:
        with Image.open(candidate) as source:
            if source.format != "PNG":
                return None, "PNG 형식 아님"
            source.verify()
    except (OSError, UnidentifiedImageError, SyntaxError) as error:
        return None, "이미지 읽기 실패: " + type(error).__name__
    return candidate, "파일 확인"


def is_animation_source(entry: dict) -> bool:
    return entry.get("kind") == "animation_clip_source" or entry.get("previewStatus") == "NOT_APPLICABLE_ANIMATION"


def identity_key(record: dict, field: str, source: str) -> str:
    value = record.get(field)
    if value is None:
        return ""
    if not isinstance(value, str):
        raise ValueError(f"{source} {field}에는 문자열이 필요합니다: index {record.get('index')}")
    return value.strip().replace("\\", "/").casefold()


def load_entries(catalog_path: Path, reviews_path: Path, unity_root: Path, output: Path, reference_repairs_path: Path | None = None):
    catalog = read_json(catalog_path)
    raw_entries = catalog.get("entries") if isinstance(catalog, dict) else catalog
    if not isinstance(raw_entries, list):
        raise ValueError("ModelCatalog.json에 entries 배열이 필요합니다.")
    review_map = {}
    reviews_by_guid = {}
    reviews_by_path = {}
    review_paths = set()
    reference_map = {}
    if reference_repairs_path and reference_repairs_path.is_file():
        reference_doc = read_json(reference_repairs_path)
        reference_items = reference_doc.get("items") if isinstance(reference_doc, dict) else reference_doc
        if not isinstance(reference_items, list):
            raise ValueError("PrefabReferenceRepairs.json에 items 배열이 필요합니다.")
        for repair in reference_items:
            if not isinstance(repair, dict) or not isinstance(repair.get("prefab"), str):
                raise ValueError("참조 복구 항목에 prefab 경로가 필요합니다.")
            key = repair["prefab"].replace("\\", "/").casefold()
            reference_map.setdefault(key, []).append({field: repair.get(field) for field in ("status", "note", "evidence", "sourceAsset", "sourceName", "connectedColliders", "connectedMaterialSlots", "removedEmptyLodNodes")})
    if reviews_path.is_file():
        review_doc = read_json(reviews_path)
        reviews = review_doc.get("reviews") if isinstance(review_doc, dict) else review_doc
        if not isinstance(reviews, list):
            raise ValueError("VisualReview.json에 reviews 배열이 필요합니다.")
        for review in reviews:
            if not isinstance(review, dict) or not isinstance(review.get("index"), int):
                raise ValueError("VisualReview의 모든 항목에는 정수 index가 필요합니다.")
            if review["index"] in review_map:
                raise ValueError(f"VisualReview 중복 index: {review['index']}")
            guid = identity_key(review, "guid", "VisualReview")
            path = identity_key(review, "path", "VisualReview")
            if not guid and not path:
                raise ValueError(f"VisualReview에 GUID 또는 경로가 필요합니다; index만으로 연결하지 않습니다: {review['index']}")
            if guid and guid in reviews_by_guid:
                raise ValueError(f"VisualReview 중복 GUID: {guid}")
            if path and path in review_paths:
                raise ValueError(f"VisualReview 중복 path: {path}")
            if guid:
                reviews_by_guid[guid] = review
            elif path:
                # Only legacy records without GUID may fall back to a path match.
                reviews_by_path[path] = review
            if path:
                review_paths.add(path)
            review_map[review["index"]] = review
    indices = set()
    catalog_guids = set()
    catalog_paths = set()
    matched_reviews = set()
    entries = []
    previews = {}
    for raw in raw_entries:
        if not isinstance(raw, dict) or not isinstance(raw.get("index"), int):
            raise ValueError("ModelCatalog의 모든 항목에는 정수 index가 필요합니다.")
        entry = dict(raw)
        index = entry["index"]
        if index in indices:
            raise ValueError(f"ModelCatalog 중복 index: {index}")
        indices.add(index)
        guid = identity_key(entry, "guid", "ModelCatalog")
        path = identity_key(entry, "path", "ModelCatalog")
        if guid and guid in catalog_guids:
            raise ValueError(f"ModelCatalog 중복 GUID: {guid}")
        if path and path in catalog_paths:
            raise ValueError(f"ModelCatalog 중복 path: {path}")
        if guid:
            catalog_guids.add(guid)
        if path:
            catalog_paths.add(path)
        guid_review = reviews_by_guid.get(guid)
        path_review = reviews_by_path.get(path)
        if guid_review and path_review:
            raise ValueError(f"동일 자산에 GUID 리뷰와 경로 리뷰가 중복됩니다: {entry.get('path')}")
        review = guid_review or path_review or {}
        if review:
            if review["index"] in matched_reviews:
                raise ValueError(f"VisualReview가 여러 자산에 연결됩니다: {review['index']}")
            matched_reviews.add(review["index"])
        entry["sourceCategory"] = entry.get("category")
        entry["sourceLabel"] = entry.get("label")
        for key in ("category", "label"):
            if review.get(key):
                entry[key] = review[key]
        entry["reviewStatus"] = review.get("reviewStatus", "NOT_APPLICABLE_ANIMATION" if is_animation_source(entry) else "미검토")
        entry["reviewNotes"] = review.get("notes", "")
        entry["hasReviewRecord"] = bool(review)
        entry["hasVisualReview"] = bool(review) and not is_animation_source(entry)
        entry["referenceRepairs"] = reference_map.get(str(entry.get("path") or "").replace("\\", "/").casefold(), [])
        entry["category"] = entry.get("category") or "미분류"
        entry["pack"] = entry.get("pack") or "출처 미상"
        entry["name"] = entry.get("name") or Path(entry.get("path") or "이름 없음").stem
        entry["label"] = entry.get("label") or entry["name"]
        entry["isAnimationSource"] = is_animation_source(entry)
        image_path, image_status = (None, "애니메이션 소스 · 정적 이미지 비대상") if entry["isAnimationSource"] else preview_file(entry, unity_root)
        entry["imageStatus"] = image_status
        entry["previewUrl"] = relative_url(image_path, output) if image_path else None
        entry["previewStatus"] = entry.get("previewStatus") or "pending"
        entry["alternateViews"] = []
        alternate_previews = entry.get("alternatePreviews") or []
        if not isinstance(alternate_previews, list):
            raise ValueError(f"alternatePreviews에는 경로 배열이 필요합니다: index {index}")
        for position, alternate in enumerate(alternate_previews):
            alternate_path, alternate_status = preview_file({"preview": alternate}, unity_root)
            entry["alternateViews"].append({"path": alternate, "label": f"시점 {position + 1}", "url": relative_url(alternate_path, output) if alternate_path else None, "status": alternate_status})
        if image_path:
            previews[index] = image_path
        entries.append(entry)
    orphan_reviews = sorted(set(review_map) - matched_reviews)
    metadata = {key: value for key, value in catalog.items() if key != "entries"} if isinstance(catalog, dict) else {}
    # The gallery needs catalog-level context, not the full material inventory a second time.
    metadata.pop("materials", None)
    metadata.update({"generated": datetime.now(timezone.utc).isoformat(), "orphanReviews": orphan_reviews})
    return entries, previews, metadata


def write_reviewed_catalog(entries: list[dict], metadata: dict, catalog_path: Path, reviews_path: Path, output: Path) -> tuple[Path, Path]:
    """Write the effective classification without modifying Unity's raw audit."""
    ui_fields = {"previewUrl", "imageStatus", "alternateViews", "sourceCategory", "sourceLabel", "reviewNotes"}
    reviewed = []
    for entry in entries:
        row = {key: value for key, value in entry.items() if key not in ui_fields}
        row["automaticCategory"] = entry.get("sourceCategory")
        row["automaticLabel"] = entry.get("sourceLabel")
        row["notes"] = entry.get("reviewNotes", "")
        reviewed.append(row)
    output.mkdir(parents=True, exist_ok=True)
    json_path = output / "ModelCatalog.reviewed.json"
    csv_path = output / "ModelCatalog.reviewed.csv"
    source_paths = {catalog_path.resolve(), reviews_path.resolve()}
    if json_path.resolve() in source_paths or csv_path.resolve() in source_paths:
        raise ValueError("검토 반영 출력은 원본 원장/리뷰 파일과 다른 경로여야 합니다.")
    document = {
        "schema": "oheangbu.asset-catalog.reviewed.v1",
        "generated": metadata["generated"],
        "rawCatalog": str(catalog_path.resolve()),
        "visualReview": str(reviews_path.resolve()) if reviews_path.is_file() else None,
        "description": "씬 배치 후보 검색에는 이 검토 반영 원장을 우선 사용한다. category/label은 VisualReview의 수정값을 반영하며 automaticCategory/automaticLabel은 Unity 원장의 자동 분류 원값이다. reviewStatus와 notes의 미해결 사항을 함께 읽는다. 형상 분류 검토는 사용자 미술 PASS나 배치 성능 검증을 뜻하지 않는다.",
        "entries": reviewed,
    }
    json_path.write_text(json.dumps(document, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    primary = ["index", "kind", "pack", "name", "category", "label", "automaticCategory", "automaticLabel", "reviewStatus", "notes", "path", "guid"]
    all_fields = {key for row in reviewed for key in row}
    fieldnames = primary + sorted(all_fields - set(primary))
    with csv_path.open("w", encoding="utf-8-sig", newline="") as stream:
        writer = csv.DictWriter(stream, fieldnames=fieldnames)
        writer.writeheader()
        for row in reviewed:
            writer.writerow({key: json.dumps(value, ensure_ascii=False, separators=(",", ":")) if isinstance(value, (dict, list)) else value for key, value in row.items()})
    return json_path, csv_path


def font_path(custom: Path | None) -> Path | None:
    if custom:
        if not custom.is_file():
            raise ValueError(f"폰트가 없습니다: {custom}")
        return custom
    candidates = [
        Path(os.environ.get("WINDIR", "C:/Windows")) / "Fonts/malgun.ttf",
        Path("/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc"),
        Path("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"),
    ]
    return next((candidate for candidate in candidates if candidate.is_file()), None)


def shorten(draw: ImageDraw.ImageDraw, value: str, font, max_width: int) -> str:
    if draw.textlength(value, font=font) <= max_width:
        return value
    while value and draw.textlength(value + "…", font=font) > max_width:
        value = value[:-1]
    return value + "…"


def make_sheets(entries: list[dict], previews: dict, output: Path, custom_font: Path | None) -> dict:
    selected = [entry for entry in entries if entry["index"] in previews]
    chosen_font = font_path(custom_font)
    regular = ImageFont.truetype(str(chosen_font), 11) if chosen_font else ImageFont.load_default()
    title_font = ImageFont.truetype(str(chosen_font), 12) if chosen_font else regular
    pages = []
    for page_number, start in enumerate(range(0, len(selected), PAGE_SIZE)):
        group = selected[start : start + PAGE_SIZE]
        sheet = Image.new("RGB", (COLUMNS * IMAGE_SIZE, ROWS * CELL_HEIGHT), "#f1efe8")
        draw = ImageDraw.Draw(sheet)
        cells = []
        for position, entry in enumerate(group):
            x = position % COLUMNS * IMAGE_SIZE
            y = position // COLUMNS * CELL_HEIGHT
            image_path = previews[entry["index"]]
            with Image.open(image_path) as image:
                image = ImageOps.exif_transpose(image).convert("RGBA")
                image.thumbnail((IMAGE_SIZE - 4, IMAGE_SIZE - 4), Image.Resampling.LANCZOS)
                base = Image.new("RGBA", (IMAGE_SIZE, IMAGE_SIZE), "#d9d8d1")
                base.alpha_composite(image, ((IMAGE_SIZE - image.width) // 2, (IMAGE_SIZE - image.height) // 2))
                sheet.paste(base.convert("RGB"), (x, y))
            label = f"#{entry['index']} · {entry['category']}"
            draw.text((x + 4, y + IMAGE_SIZE + 2), shorten(draw, label, title_font, IMAGE_SIZE - 8), font=title_font, fill="#282b28")
            draw.text((x + 4, y + IMAGE_SIZE + 19), shorten(draw, entry["name"], regular, IMAGE_SIZE - 8), font=regular, fill="#545a54")
            if entry["previewStatus"] != "rendered":
                draw.rectangle((x, y, x + IMAGE_SIZE - 1, y + 18), fill="#924721")
                draw.text((x + 3, y + 1), shorten(draw, entry["previewStatus"], regular, IMAGE_SIZE - 6), font=regular, fill="white")
            draw.line((x, y + CELL_HEIGHT - 1, x + IMAGE_SIZE - 1, y + CELL_HEIGHT - 1), fill="#d1d4ca")
            cells.append({"index": entry["index"], "name": entry["name"], "label": entry["label"], "category": entry["category"], "row": position // COLUMNS, "column": position % COLUMNS, "preview": entry.get("preview"), "previewStatus": entry["previewStatus"]})
        filename = f"sheet_{page_number:02d}.png"
        sheet.save(output / filename)
        pages.append({"page": page_number, "file": filename, "count": len(group), "firstIndex": group[0]["index"], "lastIndex": group[-1]["index"], "minIndex": min(entry["index"] for entry in group), "maxIndex": max(entry["index"] for entry in group), "indices": [entry["index"] for entry in group], "cells": cells})
    manifest = {"columns": COLUMNS, "rows": ROWS, "imageSize": IMAGE_SIZE, "cellHeight": CELL_HEIGHT, "width": COLUMNS * IMAGE_SIZE, "height": ROWS * CELL_HEIGHT, "entriesPerPage": PAGE_SIZE, "renderedFiles": len(selected), "excludedIndices": [entry["index"] for entry in entries if entry["index"] not in previews and not entry.get("isAnimationSource")], "notApplicableIndices": [entry["index"] for entry in entries if entry.get("isAnimationSource")], "pages": pages}
    (output / "pageIndices.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    return manifest


HTML = r'''<!doctype html>
<html lang="ko"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>오행부 · 모델 에셋 도감</title>
<style>
:root{color-scheme:light;--paper:#f3f1e9;--ink:#26332e;--muted:#697369;--line:#d8ddd3;--accent:#315b49}*{box-sizing:border-box}body{margin:0;background:var(--paper);color:var(--ink);font:15px/1.55 "Malgun Gothic",system-ui,sans-serif}button,input,select{font:inherit}button,a,input,select{-webkit-tap-highlight-color:transparent}button{cursor:pointer}header{padding:38px max(24px,5vw) 24px;background:#e8ecdf;border-bottom:1px solid var(--line)}.eyebrow{font-size:12px;letter-spacing:.16em;font-weight:700;color:var(--accent)}h1{font-size:clamp(28px,4vw,42px);letter-spacing:-.05em;margin:6px 0 9px}header p{margin:0;color:var(--muted);max-width:850px}.stats{display:flex;gap:28px;flex-wrap:wrap;margin-top:24px}.stat strong{display:block;font-size:25px;line-height:1.25}.stat span{font-size:12px;color:var(--muted)}main{max-width:1800px;margin:auto;padding:24px max(20px,4vw) 56px}.toolbar{position:sticky;top:0;z-index:2;background:var(--paper);padding:10px 0 16px;display:grid;grid-template-columns:minmax(180px,1fr) repeat(3,minmax(130px,200px));gap:10px}.control{display:flex;flex-direction:column;gap:5px;font-size:12px;color:var(--muted)}input,select{color:var(--ink);border:1px solid #bac4b6;background:#fbfcf6;border-radius:8px;padding:10px;width:100%;min-width:0}input:focus,select:focus,button:focus-visible,a:focus-visible{outline:3px solid #83a28f;outline-offset:2px}.bar{display:flex;align-items:center;gap:12px;justify-content:space-between;margin:0 0 18px;color:var(--muted);font-size:13px}.bar button,.pagination button,.close,.smallbutton{border:1px solid #b7c4b3;border-radius:7px;background:#fbfcf6;color:var(--accent);padding:7px 13px}.grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(180px,1fr));gap:15px}.card{display:flex;flex-direction:column;text-align:left;padding:0;border:1px solid var(--line);background:#fafaf4;border-radius:12px;overflow:hidden;color:inherit;box-shadow:0 2px 3px #24392c06;transition:border-color .15s,transform .15s}.card:hover{border-color:#769980;transform:translateY(-2px)}.thumb{width:100%;aspect-ratio:1;background:#dfe3d8;display:flex;align-items:center;justify-content:center;position:relative}.thumb img{width:100%;height:100%;object-fit:contain}.missing{padding:20px;text-align:center;font-size:13px;color:#73786e}.index{position:absolute;top:9px;left:9px;font-size:11px;background:#f9fbf2e8;padding:2px 7px;border-radius:5px}.cardtext{padding:12px 13px 14px;min-width:0;width:100%}.category{font-size:11px;color:var(--accent);font-weight:700}.cardtitle{font-size:14px;line-height:1.4;margin:4px 0;overflow-wrap:anywhere}.filename,.pack{font-size:11px;color:var(--muted);white-space:nowrap;overflow:hidden;text-overflow:ellipsis}.badge{display:inline-block;margin-top:9px;padding:3px 6px;border-radius:4px;background:#e4ebdf;font-size:10px;color:#41643d}.badge.warn{background:#f0e2c7;color:#856020}.badge.bad{background:#f3dcd4;color:#934626}.pagination{display:flex;justify-content:center;align-items:center;gap:20px;margin:28px 0}.pagination button:disabled{opacity:.4;cursor:default}.empty{text-align:center;padding:60px;color:var(--muted)}.sheets{border-top:1px solid var(--line);padding-top:20px;margin-top:30px}.sheets h2{font-size:18px}.sheetlinks{display:flex;gap:8px;flex-wrap:wrap}.sheetlinks a{font-size:12px;color:var(--accent);border:1px solid var(--line);border-radius:6px;text-decoration:none;padding:6px 10px}.footnote{font-size:12px;color:var(--muted);margin-top:24px}dialog{border:1px solid #bcc6b6;border-radius:16px;padding:0;width:min(1060px,95vw);max-height:92vh;color:var(--ink);background:var(--paper);box-shadow:0 30px 100px #18271e55}dialog::backdrop{background:#14241ace;backdrop-filter:blur(3px)}.dialogtop{display:flex;align-items:center;justify-content:space-between;gap:16px;padding:16px 22px;border-bottom:1px solid var(--line)}.dialogtop h2{font-size:20px;margin:0;overflow-wrap:anywhere}.dialogbody{display:grid;grid-template-columns:minmax(240px,44%) minmax(0,1fr);gap:24px;padding:24px}.largeimage{aspect-ratio:1;background:#dce1d4;border-radius:10px;overflow:hidden;display:flex;align-items:center;justify-content:center}.largeimage img{width:100%;height:100%;object-fit:contain}.details h3{font-size:13px;color:var(--accent);margin:20px 0 6px}.details h3:first-child{margin-top:0}.details p{margin:3px 0;font-size:13px;overflow-wrap:anywhere}.details pre{font:12px/1.55 "Malgun Gothic",system-ui,sans-serif;white-space:pre-wrap;overflow-wrap:anywhere;background:#e7ebe1;padding:10px;border-radius:6px}.facts{display:grid;grid-template-columns:1fr 1fr;gap:10px}.fact{background:#e7ebe1;padding:10px;border-radius:6px;font-size:12px}.fact strong{font-size:17px;display:block}.smallbutton{font-size:12px;padding:4px 10px}.imagecaption{font-size:12px;color:var(--muted);margin-top:10px}.statusmsg{min-height:20px;font-size:12px;color:var(--accent)}@media(max-width:700px){header{padding:25px 20px}.stats{gap:18px}.toolbar{grid-template-columns:1fr 1fr}.control:first-child{grid-column:1/-1}.grid{grid-template-columns:repeat(2,minmax(0,1fr));gap:10px}.dialogbody{grid-template-columns:1fr}.largeimage{max-height:380px;aspect-ratio:auto;height:320px}.bar{align-items:flex-start}.stat strong{font-size:21px}}

.alternateviews{display:flex;flex-wrap:wrap;gap:8px;margin-top:12px}.alternateviews button{border:1px solid #afbeaa;border-radius:6px;background:#e2e8d9;padding:3px;width:68px;color:#38563f;font-size:10px}.alternateviews button[aria-pressed="true"]{outline:2px solid #416f51;outline-offset:1px}.alternateviews img{width:60px;height:60px;object-fit:contain;display:block}.alternateviews .footnote{flex-basis:100%;margin:3px 0}
</style></head><body>
<header><div class="eyebrow">OHEANGBU · ASSET LIBRARY</div><h1>모델 에셋 도감</h1><p>실제 Unity 모델과 재질을 확인하는 작업용 도감입니다. 썸네일 생성과 육안 검토를 구분하고, 씬에 어울리는 모델을 출처와 함께 찾습니다.</p><div id="stats" class="stats"></div></header>
<main><div class="toolbar"><label class="control">모델 검색<input id="query" type="search" placeholder="이름, 용도, 경로, 인덱스 검색" autocomplete="off"></label><label class="control">에셋 팩<select id="pack"><option value="">모든 팩</option></select></label><label class="control">모델 분류<select id="category"><option value="">모든 분류</option></select></label><label class="control">확인 상태<select id="state"><option value="">전체 항목</option><option value="image">실제 PNG 있음</option><option value="noimage">모델 대표 이미지 없음</option><option value="animation">애니메이션 소스</option><option value="attention">렌더·참조 확인 필요</option><option value="hightri">고폴리 성능 검토</option><option value="reviewed">검토 기록 있음</option><option value="unreviewed">육안 검토 기록 없음</option></select></label></div><div class="bar"><span id="count" aria-live="polite"></span><button id="reset" type="button">필터 초기화</button></div><div id="grid" class="grid"></div><div id="empty" class="empty" hidden>조건에 맞는 모델이 없습니다.</div><div class="pagination"><button id="prev" type="button">이전</button><span id="page"></span><button id="next" type="button">다음</button></div><section class="sheets"><h2>전수 검토 시트</h2><p class="footnote">한 장에 최대 64개 모델을 담았습니다. 라벨의 #번호는 원장의 index입니다. 실제 PNG가 없는 항목은 시트에서 제외하고 위 도감에는 남깁니다.</p><div id="sheets" class="sheetlinks"></div></section><p id="footer" class="footnote"></p></main>
<dialog id="detail"><div class="dialogtop"><h2 id="detailtitle"></h2><button id="close" class="close" type="button" aria-label="상세 닫기">닫기 ✕</button></div><div class="dialogbody"><div><div id="largeimage" class="largeimage"></div><div id="alternateviews" class="alternateviews"></div><p id="imagecaption" class="imagecaption"></p></div><div id="details" class="details"></div></div></dialog>
<script id="catalog-data" type="application/json">__CATALOG_DATA__</script>
<script>
"use strict";
const DATA=JSON.parse(document.getElementById("catalog-data").textContent),entries=DATA.entries;
const $=id=>document.getElementById(id),nf=new Intl.NumberFormat("ko-KR");
const number=v=>v!==null&&v!==undefined&&v!==""&&Number.isFinite(Number(v))?nf.format(Number(v)):"미기록";
const stringify=v=>typeof v==="string"?v:JSON.stringify(v??"",null,2);
const node=(tag,cls,text)=>{const el=document.createElement(tag);if(cls)el.className=cls;if(text!==undefined)el.textContent=text;return el;};
function problem(e){return ["missingScripts","missingMesh","missingColliderMesh","missingMaterial","brokenMaterials"].some(k=>Number(e[k]||0)>0)||(!e.isAnimationSource&&(!e.previewUrl||e.previewStatus!=="rendered"))||(e.referenceRepairs||[]).some(r=>String(r.status).startsWith("UNRESOLVED")||String(r.status).includes("APPEARANCE_REVIEW"));}
const highTri=e=>Number(e.highestLodTriangles)>250000;
function stat(value,label){const el=node("div","stat");el.append(node("strong","",number(value)),node("span","",label));$("stats").append(el);}
stat(entries.length,"전체 모델 항목");stat(new Set(entries.map(e=>e.pack)).size,"에셋 팩");stat(entries.filter(e=>e.previewUrl).length,"실제 PNG 확인");stat(entries.filter(e=>e.hasVisualReview).length,"육안 검토 기록");stat(entries.filter(problem).length,"렌더·참조 확인 필요");stat(entries.filter(highTri).length,"고폴리 성능 검토");stat(entries.filter(e=>e.isAnimationSource).length,"애니메이션 소스");
for(const [id,key] of [["pack","pack"],["category","category"]]){const values=[...new Set(entries.map(e=>e[key]))].sort((a,b)=>String(a).localeCompare(String(b),"ko"));for(const value of values){const option=node("option","",`${value} (${number(entries.filter(e=>e[key]===value).length)})`);option.value=value;$(id).append(option);}}
let page=0,filtered=entries;const pageSize=72;
function filter(){const q=$("query").value.trim().toLocaleLowerCase(),pack=$("pack").value,category=$("category").value,state=$("state").value;filtered=entries.filter(e=>{if(pack&&e.pack!==pack||category&&e.category!==category)return false;if(q&&!`${e.index} ${e.name} ${e.label} ${e.category} ${e.pack} ${e.placement||""} ${e.path} ${e.reviewNotes||""}`.toLocaleLowerCase().includes(q))return false;return !state||(state==="image"&&e.previewUrl)||(state==="noimage"&&!e.previewUrl&&!e.isAnimationSource)||(state==="animation"&&e.isAnimationSource)||(state==="attention"&&problem(e))||(state==="hightri"&&highTri(e))||(state==="reviewed"&&e.hasVisualReview)||(state==="unreviewed"&&!e.hasVisualReview&&!e.isAnimationSource);});page=0;render();}
function putImage(container,e,lazy){if(e.previewUrl){const image=node("img");image.src=e.previewUrl;image.alt=`${e.label} 실제 Unity 렌더`;if(lazy)image.loading="lazy";image.decoding="async";image.addEventListener("error",()=>{image.remove();container.append(node("span","missing","이미지 파일을 열 수 없습니다"));},{once:true});container.append(image);}else container.append(node("span","missing",e.isAnimationSource?"애니메이션 클립 소스 · 정적 이미지 대상이 아닙니다":`대표 이미지 없음\n${e.imageStatus}`));}
function alternateViews(e){const parent=$("alternateviews");parent.replaceChildren();const options=[...(e.previewUrl?[{url:e.previewUrl,label:"기본"}]:[]),...(e.alternateViews||[]).filter(v=>v.url)];if(options.length<=1)return;for(const [index,view] of options.entries()){const button=node("button");button.type="button";button.setAttribute("aria-pressed",index===0?"true":"false");button.setAttribute("aria-label",`${view.label} 확대`);const image=node("img");image.src=view.url;image.alt=view.label;image.loading="lazy";button.append(image,node("span","",view.label));button.addEventListener("click",()=>{$("largeimage").replaceChildren();putImage($("largeimage"),{...e,previewUrl:view.url},false);for(const item of parent.children)if(item.tagName==="BUTTON")item.setAttribute("aria-pressed",item===button?"true":"false");$("imagecaption").textContent=`${view.label} · 실제 Unity 다각도 렌더. 정적 형상 확인용이며 사용자 미술 PASS를 뜻하지 않습니다.`;});parent.append(button);}const missing=(e.alternateViews||[]).filter(v=>!v.url).length;if(missing)parent.append(node("p","footnote",`추가 시점 ${missing}개는 아직 파일이 준비되지 않았습니다.`));}
function card(e){const button=node("button","card");button.type="button";button.setAttribute("aria-label",`#${e.index} ${e.label} 상세 보기`);const thumb=node("div","thumb");putImage(thumb,e,true);thumb.append(node("span","index",`#${e.index}`));const body=node("div","cardtext");body.append(node("div","category",e.category),node("h2","cardtitle",e.label),node("div","filename",e.name),node("div","pack",e.pack));body.append(node("span",`badge ${problem(e)?"warn":""}`,e.isAnimationSource?"애니메이션 소스 · 이미지 비대상":e.hasVisualReview?`${e.reviewStatus}`:(problem(e)?"확인 필요 · 육안 미검토":"실물 렌더 · 육안 미검토")));if(highTri(e))body.append(node("span","badge warn",`성능 검토 · ${number(e.highestLodTriangles)} tris`));button.append(thumb,body);button.addEventListener("click",()=>detail(e));return button;}
function render(){$("grid").replaceChildren(...filtered.slice(page*pageSize,(page+1)*pageSize).map(card));$("empty").hidden=filtered.length!==0;$("count").textContent=`전체 ${number(entries.length)}개 중 ${number(filtered.length)}개 · ${number(filtered.filter(e=>e.previewUrl).length)}개 이미지 확인`;const pages=Math.max(1,Math.ceil(filtered.length/pageSize));$("page").textContent=`${page+1} / ${pages}`;$("prev").disabled=page===0;$("next").disabled=page>=pages-1;}
for(const id of ["pack","category","state"])$(id).addEventListener("change",filter);$("query").addEventListener("input",filter);$("reset").addEventListener("click",()=>{for(const id of ["pack","category","state","query"])$(id).value="";filter();});$("prev").addEventListener("click",()=>{page--;render();$("count").scrollIntoView({block:"start"});});$("next").addEventListener("click",()=>{page++;render();$("count").scrollIntoView({block:"start"});});
function section(parent,title,value){parent.append(node("h3","",title),node("p","",value||"미기록"));}
function detail(e){$("detailtitle").textContent=`#${e.index} · ${e.label}`;$("largeimage").replaceChildren();putImage($("largeimage"),e,false);alternateViews(e);$("imagecaption").textContent=e.isAnimationSource?"애니메이션 클립 원본 · 정적 이미지 비대상. 메시가 없는 정상 파일이며 실제 동작 검수는 별도입니다.":`렌더: ${e.previewStatus} · ${e.imageStatus}. ${e.hasVisualReview?"별도 육안 검토 기록이 있습니다.":"렌더가 존재해도 육안 분류 완료를 뜻하지 않습니다."}`;const d=$("details");d.replaceChildren();section(d,"모델 분류",`${e.category} · ${e.kind||"종류 미기록"} · ${e.pack}`);section(d,"배치 후보",stringify(e.placement));if(e.isAnimationSource){section(d,"애니메이션 클립 소스","정적 렌더 메시가 없는 애니메이션 파일입니다. 이미지 없음은 정상이며 프리뷰 누락/실패 통계에서 제외합니다. 캐릭터 리그 연결과 실제 동작 검수는 별도입니다.");section(d,"포함 애니메이션 클립",Array.isArray(e.animationClips)?e.animationClips.map(stringify).join("\n"):"클립 목록 미기록");}const facts=node("div","facts");for(const [label,value] of [["최고 상세 LOD 삼각형",number(e.highestLodTriangles)],["크기 X × Y × Z (m)",Array.isArray(e.size)?e.size.map(v=>Number(v).toFixed(2)).join(" × "):"미기록"],["렌더러 / 충돌체",`${number(e.renderers)} / ${number(e.colliders)}`],["전체 LOD 삼각형",number(e.triangles)]]){const fact=node("div","fact");fact.append(node("strong","",value),node("span","",label));facts.append(fact);}d.append(node("h3","","기하 정보"),facts);d.append(node("p","footnote","에디터 자산 통계입니다. 씬 동시 렌더량이나 빌드 성능 측정값은 아닙니다."));section(d,"LOD 구성",Array.isArray(e.lodDetails)&&e.lodDetails.length?e.lodDetails.map(stringify).join("\n"):"LOD 상세 없음");section(d,"참조·셰이더 감사",`Missing script ${number(e.missingScripts)} · Mesh ${number(e.missingMesh)} · Collider mesh ${number(e.missingColliderMesh)} · Material ${number(e.missingMaterial)} · 깨진 재질 ${number(e.brokenMaterials)}`);if(highTri(e))section(d,"배치 전 성능 검토",`최고 상세 LOD ${number(e.highestLodTriangles)} tris. 250,000 tris 초과 표시는 검토 목록을 만드는 기준이며 프로젝트의 제작 예산이나 불합격 판정이 아닙니다. 야외 배치 전 가시 거리·LOD·동시 배치량을 확인해야 합니다.`);if(e.referenceRepairs?.length){d.append(node("h3","","소스 참조 복구 기록"));for(const repair of e.referenceRepairs){d.append(node("p","",repair.status||"상태 미기록"),node("p","",repair.note||"메모 없음"));if(repair.evidence)d.append(node("p","footnote",`근거: ${repair.evidence}`));if(repair.sourceAsset)d.append(node("pre","",repair.sourceAsset));}d.append(node("p","footnote","참조 연결 복구와 원본 외형 복원은 별개입니다. APPEARANCE_REVIEW는 장면 적용 전 외형 검토가 남았음을 뜻합니다."));}section(d,"육안 검토",`${e.reviewStatus}${e.reviewNotes?" — "+stringify(e.reviewNotes):""}`);section(d,"분류 근거",`자동 분류 신뢰도: ${stringify(e.confidence)||"미기록"}${e.hasVisualReview?` · 원장 분류: ${e.sourceCategory||"미기록"} / ${e.sourceLabel||"미기록"}`:" · 별도 육안 기록 없음"}`);d.append(node("h3","","원본 경로"),node("pre","",e.path||"미기록"));const copy=node("button","smallbutton","경로 복사");copy.type="button";const msg=node("div","statusmsg");copy.addEventListener("click",async()=>{try{await navigator.clipboard.writeText(e.path||"");msg.textContent="경로를 복사했습니다.";}catch{msg.textContent="브라우저에서 복사가 제한됐습니다. 위 경로를 선택해 복사하세요.";}});d.append(copy,msg,node("p","footnote",`GUID: ${e.guid||"미기록"}`));if(e.materials?.length){section(d,"연결 재질",e.materials.map(stringify).join("\n"));}$("detail").showModal();}
$("close").addEventListener("click",()=>$("detail").close());$("detail").addEventListener("click",event=>{if(event.target===$("detail")){const rect=$("detail").getBoundingClientRect();if(event.clientX<rect.left||event.clientX>rect.right||event.clientY<rect.top||event.clientY>rect.bottom)$("detail").close();}});
for(const sheet of DATA.sheets.pages||[]){const link=node("a","",`${String(sheet.page+1).padStart(2,"0")} · #${sheet.firstIndex}–#${sheet.lastIndex} (${sheet.count})`);link.href=sheet.file;link.target="_blank";link.rel="noopener";$("sheets").append(link);}if(!DATA.sheets.pages?.length)$("sheets").append(node("span","footnote","검토 시트가 아직 생성되지 않았습니다."));$("footer").textContent=`씬 배치 후보 검색에는 Docs/Assets/ModelCatalog.reviewed.json 또는 .csv를 우선 사용하세요. category/label은 검토 수정값이며 automaticCategory/automaticLabel은 자동 분류 원값입니다. 사용자 미술 PASS를 뜻하지 않습니다. 원장 생성: ${DATA.metadata.created||"미기록"} · 도감 생성: ${DATA.metadata.generated}. 원본 JSON은 변경하지 않습니다. 검토 기록이 바뀌면 생성 스크립트를 다시 실행하세요.${DATA.metadata.orphanReviews.length?" 원장에 없는 검토 index: "+DATA.metadata.orphanReviews.join(", "):""}`;
render();
</script></body></html>'''


def write_html(entries: list[dict], metadata: dict, sheets: dict, output: Path):
    data = json.dumps({"entries": entries, "metadata": metadata, "sheets": sheets}, ensure_ascii=False, separators=(",", ":"))
    # JSON is data, never markup: names and review notes may contain HTML-like text.
    data = data.replace("&", "\\u0026").replace("<", "\\u003c").replace(">", "\\u003e").replace("\u2028", "\\u2028").replace("\u2029", "\\u2029")
    (output / "index.html").write_text(HTML.replace("__CATALOG_DATA__", data), encoding="utf-8")


def main(argv=None) -> int:
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
        sys.stderr.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser(description="Unity 모델 원장으로 오프라인 도감과 64개씩 검토 시트를 만듭니다.")
    parser.add_argument("--catalog", type=Path, default=REPO_ROOT / "Docs/Assets/ModelCatalog.json")
    parser.add_argument("--reviews", type=Path, default=REPO_ROOT / "Docs/Assets/VisualReview.json")
    parser.add_argument("--reference-repairs", type=Path, default=REPO_ROOT / "Docs/Assets/PrefabReferenceRepairs.json")
    parser.add_argument("--unity-root", type=Path, default=REPO_ROOT / "Oheangbu")
    parser.add_argument("--output", type=Path, default=REPO_ROOT / "Oheangbu/Screenshots/AssetCatalog")
    parser.add_argument("--reviewed-output", type=Path, help="검토 반영 JSON/CSV 폴더(기본: 원본 원장과 같은 폴더)")
    parser.add_argument("--font", type=Path, help="검토 시트 라벨용 한글 TTF/TTC")
    parser.add_argument("--no-sheets", action="store_true", help="시트를 생성하지 않고 도감만 갱신(기존 pageIndices.json 연결 유지)")
    args = parser.parse_args(argv)
    if not args.catalog.is_file():
        parser.error(f"원장이 없습니다. Unity AssetIntakeAudit.Scan을 먼저 실행하세요: {args.catalog}")
    output = args.output.resolve()
    unity_root = args.unity_root.resolve()
    try:
        entries, previews, metadata = load_entries(args.catalog, args.reviews, unity_root, output, args.reference_repairs)
        output.mkdir(parents=True, exist_ok=True)
        if args.no_sheets:
            manifest_path = output / "pageIndices.json"
            sheets = read_json(manifest_path) if manifest_path.is_file() else {"pages": []}
            current_indices = {entry["index"] for entry in entries}
            stale = any(index not in current_indices for page in sheets.get("pages", []) for index in page.get("indices", []))
            if stale:
                print("주의: 기존 시트에 현재 원장에 없는 index가 있습니다. --no-sheets 없이 재생성하세요.", file=sys.stderr)
            sheets["excludedIndices"] = [entry["index"] for entry in entries if not entry["previewUrl"] and not entry["isAnimationSource"]]
            sheets["notApplicableIndices"] = [entry["index"] for entry in entries if entry["isAnimationSource"]]
            if manifest_path.is_file():
                manifest_path.write_text(json.dumps(sheets, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        else:
            sheets = make_sheets(entries, previews, output, args.font)
        reviewed_json, reviewed_csv = write_reviewed_catalog(entries, metadata, args.catalog, args.reviews, (args.reviewed_output or args.catalog.parent).resolve())
        write_html(entries, metadata, sheets, output)
    except (ValueError, OSError, json.JSONDecodeError) as error:
        print(f"도감 생성 실패: {error}", file=sys.stderr)
        return 1
    print(json.dumps({"entries": len(entries), "pngFiles": len(previews), "missingPreviews": sum(not entry["previewUrl"] and not entry["isAnimationSource"] for entry in entries), "animationSources": sum(entry["isAnimationSource"] for entry in entries), "visualReviewRecords": sum(entry["hasVisualReview"] and not entry["isAnimationSource"] for entry in entries), "sheets": len(sheets.get("pages", [])), "orphanReviewIndices": metadata["orphanReviews"], "html": str(output / "index.html"), "pageIndices": str(output / "pageIndices.json"), "reviewedJson": str(reviewed_json), "reviewedCsv": str(reviewed_csv)}, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
