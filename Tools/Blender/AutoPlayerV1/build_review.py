"""Build an offline review from recorded results; never award or change a verdict.

Usage: python Tools/Blender/AutoPlayerV1/build_review.py [--root ART_RUN_FOLDER]
Only REVIEW.html is replaced, atomically. API responses and .private are not read.
"""
from __future__ import annotations

import argparse
import html
import json
import math
import os
from pathlib import Path
import re
import tempfile
from datetime import datetime, timezone
from urllib.parse import quote


DEFAULT_ROOT = Path(__file__).resolve().parents[3] / "Art/PlayerPhase1/AutoPlayerV1"
IMAGE_EXTS = {".png", ".jpg", ".jpeg", ".webp"}
VIDEO_EXTS = {".mp4", ".webm"}
LABELS = {
    "PASS": "통과", "FAIL": "실패", "UNVERIFIED": "미검증",
    "NOT_IMPLEMENTED": "구현 범위 제외", "RUNNING": "진행 중",
    "AWAITING_USER_REVIEW": "외형 검토 대기", "APPROVED": "승인", "REJECTED": "반려",
    "FIXED_OUTFIT_PROTOTYPE_PASS": "고정 복장 프로토타입 기술 검증 통과",
    "PARTIAL_UNVERIFIED": "일부 완료 · 필수 검증 미완료",
    "NO_VALID_CANDIDATE": "유효 후보 없음", "FAIL_RESIDUAL_DEFORMATION": "잔여 변형으로 실패",
    "RIGGING_PENDING": "리깅 검증 진행 중", "RIGGING": "리깅 및 동작 검증",
    "GENERATION": "모델 생성", "DEFORMATION": "동작 변형 검증", "UNITY_VALIDATION": "Unity 검증",
    "REPAIR_REVALIDATION": "보정 후 다시 검증 중",
    "UNQUALIFIED_AFTER_REPAIR_LIMIT": "보정 한도 후 미검증",
    "FAIL_VISIBLE_HEM_TETHER": "눈에 보이는 자락 띠 변형으로 실패",
}
FIELDS = (("generation", "생성"), ("deformation", "동작 변형"),
          ("serialization", "FBX 왕복"), ("weapon_grip", "붓 파지"),
          ("unity_playtest", "Unity 플레이"), ("art_review", "외형 검토"))


def text(value, limit=1800):
    """Render only short allowlisted text, with remote and credential fragments removed."""
    value = str(value if value is not None else "기록 없음")
    value = re.sub(r"https?://[^\s<>\"']+", "[외부 URL 생략]", value, flags=re.I)
    value = re.sub(r"data:[^\s]+", "[인라인 데이터 생략]", value, flags=re.I)
    value = re.sub(r"(?i)(bearer\s+|(?:access_token|refresh_token|api_key|secret)\s*[:=]\s*)\S+", "[인증정보 생략]", value)
    return html.escape(value[:limit], quote=True)


def read_json(path, notices):
    if not path.is_file():
        return {}
    try:
        value = json.loads(path.read_text(encoding="utf-8-sig"))
        return value if isinstance(value, dict) else {}
    except (OSError, ValueError):
        notices.append(f"{path.name}: 읽는 중이거나 유효한 JSON이 아닙니다. 해당 상태는 표시하지 않았습니다.")
        return {}


def public_file(path):
    return path.is_file() and not any(p.startswith(".") or p.lower() == "private" for p in path.parts)


def local_link(path, root):
    """Relative local links work directly from disk; remote paths are never accepted."""
    path, root = path.resolve(), root.resolve()
    if not public_file(path):
        return None
    try:
        relative = os.path.relpath(path, root).replace(os.sep, "/")
    except ValueError:
        return None
    return quote(relative, safe="/.-_")


def badge(value):
    value = str(value or "UNVERIFIED")
    style = "pass" if value in {"PASS", "APPROVED", "FIXED_OUTFIT_PROTOTYPE_PASS"} else "fail" if "FAIL" in value or value in {"NO_VALID_CANDIDATE", "REJECTED"} else "wait"
    label = LABELS.get(value, value.replace("_", " "))
    return f'<span class="badge {style}">{text(label)}</span>'


def number(value):
    return float(value) if isinstance(value, (int, float)) and not isinstance(value, bool) and math.isfinite(value) and value >= 0 else None


def fmt(value):
    return f"{value:,.0f}" if value == int(value) else f"{value:,.2f}"


def credit_summary(ledger, state):
    records = ledger.get("requests", [])
    if not isinstance(records, list):
        records = []
    unique = {}
    for i, item in enumerate(records):
        if isinstance(item, dict):
            unique[item.get("task_id") or f"entry-{i}"] = item
    spent, reserved, unknown = 0.0, 0.0, 0
    rows = []
    for item in unique.values():
        actual = number(item.get("consumed_credits"))
        status = str(item.get("status", "UNVERIFIED"))
        if actual is not None:
            spent += actual
        elif status in {"SUCCEEDED", "FAILED", "CANCELED", "CANCELLED"}:
            unknown += 1
        if status not in {"SUCCEEDED", "FAILED", "CANCELED", "CANCELLED"}:
            reserved += number(item.get("reserved_upper_credits")) or 0
        rows.append(f'<tr><td>{text(item.get("name", item.get("kind", "요청")))}</td><td>{text(status)}</td><td>{fmt(actual) if actual is not None else "미확인"}</td></tr>')
    budget = number(ledger.get("budget_credits", state.get("budget_credits")))
    amount = f'{fmt(spent)} / {fmt(budget)}' if budget is not None else fmt(spent)
    extra = f"진행 요청 예약 {fmt(reserved)}크레딧" if reserved else "진행 요청 예약 비용 없음"
    if unknown:
        extra += f" · 완료 요청 {unknown}건의 실제 청구액 미확인"
    details = '<details><summary>요청별 확인된 비용</summary><div class="scroll"><table><thead><tr><th>요청</th><th>API 상태</th><th>실제 크레딧</th></tr></thead><tbody>' + "".join(rows) + '</tbody></table></div></details>' if rows else '<p class="muted">요청별 비용 기록이 없습니다.</p>'
    return amount, extra, details


def figure(path, root, caption=None):
    link = local_link(path, root)
    if not link:
        return ""
    return f'<figure><a href="{link}" target="_blank" rel="noopener"><img src="{link}" alt="{text(caption or path.stem)}" loading="lazy"></a><figcaption>{text(caption or path.stem)}</figcaption></figure>'


def preview_files(folder):
    return sorted(p for p in folder.rglob("*") if public_file(p) and p.suffix.lower() in IMAGE_EXTS
                  and any(x.lower() in {"previews", "review", "renders", "screenshots", "inspect"} for x in p.relative_to(folder).parts[:-1])
                  and not any("frames" in x.lower() or x.lower() in {"textures", "source"} for x in p.relative_to(folder).parts[:-1]))


def four_views(images):
    found = []
    for view, label in (("front", "정면"), ("back", "후면"), ("left", "좌측"), ("right", "우측")):
        choices = [p for p in images if p.stem.lower() in {view, "final_" + view, "equipped_" + view, "raw_" + view, "rig_" + view, "review_" + view}]
        priorities = {"final": 0, "equipped": 1, "review": 2, "rig": 3, "raw": 4}
        choices.sort(key=lambda p: (priorities.get(p.stem.lower().split("_")[0], 5), str(p)))
        if choices:
            found.append((choices[0], f"{label} · {choices[0].stem}"))
    return found


def notes_from(record):
    notes = []
    sources = [record]
    if isinstance(record.get("visual_gate"), dict):
        sources.append(record["visual_gate"])
    for source in sources:
        for key in ("warnings", "limitations", "failure_reason", "failure_reasons", "reason", "notes"):
            value = source.get(key)
            if isinstance(value, str):
                notes.append(value)
            elif isinstance(value, list):
                notes.extend(x for x in value if isinstance(x, str))
    return list(dict.fromkeys(notes))[:12]


def continuous_progress(root, notices):
    records, cards = {}, []
    for path in sorted(root.rglob("progress.json")):
        if not public_file(path) or not path.parent.name.lower().startswith("continuous"):
            continue
        record = read_json(path, notices)
        if not record:
            continue
        records[path.parent.resolve()] = record
        jobs = [j for j in record.get("jobs", []) if isinstance(j, dict)]
        rendered = sum(number(j.get("rendered_frames")) or 0 for j in jobs)
        files = [p for p in path.parent.rglob("*") if public_file(p) and p.suffix.lower() in VIDEO_EXTS]
        state = str(record.get("status", "UNVERIFIED"))
        state_label = {"COMPLETE": "렌더 작업 완료", "RUNNING": "렌더 진행 중", "FAILED": "렌더 작업 실패", "FAIL": "렌더 작업 실패"}.get(state, state)
        engine = str(record.get("engine", ""))
        source = "Blender 렌더" if engine.startswith("BLENDER") else "촬영 방식 미기록"
        fps = number(record.get("fps"))
        timing = f" · {fmt(fps)}fps 렌더 설정" if fps is not None else ""
        cards.append(f'<div class="notice"><strong>{text(path.parent.relative_to(root))}</strong> <span class="badge wait">{text(state_label)}</span><p>{source}{timing} · 렌더 기록 {fmt(rendered)}프레임 · MP4/WebM 파일 {len(files)}개</p><small>렌더 완료는 동작 품질이나 Unity 플레이 검증의 통과를 뜻하지 않습니다.</small></div>')
    return records, ''.join(cards)


def video_caption(path, progress):
    record = progress.get(path.parent.resolve(), {})
    job = next((j for j in record.get("jobs", []) if isinstance(j, dict) and j.get("label") == path.stem), {})
    source = "Blender 렌더" if str(record.get("engine", "")).startswith("BLENDER") else "촬영 방식은 보고서 참조"
    parts = [path.stem, source]
    fps, frames = number(job.get("fps")), number(job.get("rendered_frames"))
    if fps is not None:
        parts.append(f"{fmt(fps)}fps 설정")
    if frames is not None:
        parts.append(f"{fmt(frames)}프레임 렌더 기록")
    return text(" · ".join(parts))


def candidate_section(candidate, record, root, selected, progress):
    folder = root / "Candidates" / candidate
    images = preview_files(folder) if folder.is_dir() else []
    videos = sorted(p for p in folder.rglob("*") if public_file(p) and p.suffix.lower() in VIDEO_EXTS) if folder.is_dir() else []
    out = [f'<section id="{text(candidate)}"><div class="section-title"><h2>{text(candidate)}{ " · 선정 후보" if candidate == selected else ""}</h2>{badge(record.get("status"))}</div>']
    out.append('<div class="checks">' + ''.join(f'<div><span>{label}</span>{badge(record.get(key))}</div>' for key, label in FIELDS) + '</div>')
    counts = []
    for key, label in (("triangles_raw", "생성 원본 삼각형"), ("triangles_unity", "Unity 삼각형"), ("corrections_used", "후보 보정 횟수")):
        value = number(record.get(key))
        if value is not None:
            counts.append(f"{label} {fmt(value)}")
    if counts:
        out.append('<p class="muted">' + " · ".join(counts) + '</p>')
    notes = notes_from(record)
    if notes:
        out.append('<div class="notice"><strong>기록된 주의점</strong><ul>' + ''.join(f'<li>{text(n)}</li>' for n in notes) + '</ul></div>')
    views = four_views(images)
    for view, label in (("front", "정면"), ("back", "후면"), ("left", "좌측"), ("right", "우측")):
        if not any(caption.startswith(label + " ·") for _, caption in views):
            fallback = folder / "Source" / f"thumbnail_urls_{view}.png"
            if public_file(fallback):
                views.append((fallback, f"{label} · Meshy 제공 미리보기"))
    out.append('<h3>사면도</h3>')
    if views:
        out.append('<div class="gallery four">' + ''.join(figure(p, root, label) for p, label in views) + '</div>')
    if len(views) != 4:
        out.append(f'<p class="muted">사면도 {len(views)}/4장 확인. 없는 방향은 아직 제공되지 않았습니다.</p>')
    chosen = {p for p, _ in views}
    close = [p for p in images if p not in chosen and any(t in p.stem.lower() for t in ("hand", "shoulder", "brush", "grip", "hem", "close"))]
    out.append('<h3>손·어깨·소매·붓 근접</h3>')
    out.append('<div class="gallery">' + ''.join(figure(p, root) for p in close[:8]) + '</div>' if close else '<p class="muted">근접 검수 이미지가 아직 없습니다.</p>')
    chosen.update(close[:8])
    remaining = [p for p in images if p not in chosen]
    if remaining:
        out.append(f'<details><summary>나머지 정지 검수 이미지 {len(remaining)}장</summary><div class="gallery">' + ''.join(figure(p, root) for p in remaining) + '</div></details>')
    out.append('<h3>동작 영상</h3>')
    if videos:
        out.append('<p class="muted">발견된 로컬 영상입니다. 연속 프레임·FPS·촬영 방식의 검증 여부는 최종 보고서에 따릅니다.</p><div class="gallery videos">')
        for p in videos:
            link = local_link(p, root)
            out.append(f'<figure><video controls preload="metadata" src="{link}"></video><figcaption>{video_caption(p, progress)} · <a href="{link}">영상 파일 열기</a></figcaption></figure>')
        out.append('</div>')
    else:
        out.append('<div class="notice">이 후보 폴더에는 MP4/WebM 동작 영상이 아직 없습니다. 공통 영상이 있으면 아래 별도 항목에 표시됩니다. 정지 이미지는 연속 재생 검증을 대신하지 않습니다.</div>')
    out.append('</section>')
    return ''.join(out)


def build(root):
    root = root.resolve()
    if not root.is_dir():
        raise ValueError("Existing AutoPlayer run folder is required.")
    notices = []
    state = read_json(root / "run_state.json", notices)
    ledger = read_json(root / "cost_ledger.json", notices)
    final = read_json(root / "final_summary.json", notices)
    source = read_json(root / "source_manifest.json", notices)
    progress, progress_cards = continuous_progress(root, notices)
    root_candidate = read_json(root / "candidate_result.json", notices)
    records = {}
    for value in state.get("candidates", []):
        if isinstance(value, dict) and re.fullmatch(r"C\d+", str(value.get("id", ""))):
            records[value["id"]] = dict(value)
    for p in sorted((root / "Candidates").glob("*/candidate_result.json")):
        if re.fullmatch(r"C\d+", p.parent.name):
            records[p.parent.name] = {**records.get(p.parent.name, {}), **read_json(p, notices)}
    candidate_id = root_candidate.get("candidate", root_candidate.get("id"))
    if re.fullmatch(r"C\d+", str(candidate_id or "")):
        records[candidate_id] = {**records.get(candidate_id, {}), **root_candidate}
    selected = final.get("selected_candidate", state.get("selected_candidate"))
    if isinstance(selected, dict):
        selected = selected.get("id", selected.get("candidate"))
    overall = final.get("status", state.get("status", "UNVERIFIED"))
    amount, extra, cost_details = credit_summary(ledger, state)
    stamp = datetime.now(timezone.utc).astimezone().strftime("%Y-%m-%d %H:%M %Z")
    preserved = final.get("canonical_player_replaced", state.get("canonical_player_replaced"))
    preserved_text = "기존 C2·공용 PlayerRig 교체 없음" if preserved is False else "정본 교체 기록: true" if preserved is True else "정본 보존 상태 기록 미제공"
    phase = state.get("phase", "단계 미기록")
    parts = [f'<header><p class="eyebrow">오행부 · AUTO PLAYER V1</p><h1>고정 복장 도사 후보 검토</h1><div class="headline">{badge(overall)}</div><p>{"최종 기록" if final else "현재 진행 기록"} · {text(LABELS.get(phase, phase))} · {text(stamp)}</p><p class="muted">상태는 결과 JSON에서 가져옵니다. 이 페이지가 새 합격 판정을 만들지는 않습니다.</p></header>']
    parts.append(f'<div class="metrics"><div><span>확인된 Meshy 소비 / 상한</span><strong>{amount} 크레딧</strong><small>{text(extra)}</small></div><div><span>후보</span><strong>{text(selected or "선정 전")}</strong><small>{len(records)}개 후보 기록 · 외형 채택은 사용자 검토</small></div><div><span>정본</span><strong>{text(preserved_text)}</strong><small>새 후보는 별도 검수 자료로 관리</small></div></div>')
    parts.append('<section><h2>이번 구현 범위</h2><div class="checks">' + ''.join(f'<div><span>{label}</span>{badge(final.get(key, state.get(key, "NOT_IMPLEMENTED")))}</div>' for key, label in (("wardrobe_swap", "의상 교체"), ("cloth_physics", "실시간 천 물리"), ("facial_rig", "얼굴 표정 리그"))) + '</div>')
    for note in notes_from(final):
        parts.append(f'<p class="notice">{text(note)}</p>')
    report_link = local_link(root / "FINAL_REPORT.md", root)
    if report_link:
        parts.append(f'<p><a href="{report_link}">최종 보고서 열기</a></p>')
    parts.append(cost_details + '</section>')
    if progress_cards:
        parts.append('<section><h2>연속 동작 렌더 진행</h2>' + progress_cards + '<p class="muted">아래 영상은 Blender에서 만든 후보 검수 렌더입니다. Unity 실시간 캡처와 구분합니다. 프레임 PNG 폴더는 이 페이지에 나열하지 않습니다.</p></section>')
    if notices:
        parts.append('<aside class="notice">' + '<br>'.join(text(n) for n in notices) + '</aside>')
    for candidate, record in sorted(records.items()):
        parts.append(candidate_section(candidate, record, root, selected, progress))
    if not records:
        parts.append('<section><h2>후보 준비 중</h2><p>아직 후보 결과 기록이 없습니다.</p></section>')
    shared_videos = sorted(p for p in root.rglob("*") if public_file(p) and p.suffix.lower() in VIDEO_EXTS
                           and p.relative_to(root).parts[0] != "Candidates")
    if shared_videos:
        parts.append('<section><h2>공통·최종 동작 영상</h2><p class="muted">후보 폴더 밖에 저장된 영상입니다. 촬영 방식과 연속 프레임 검증은 최종 보고서에 따릅니다.</p><div class="gallery videos">')
        for p in shared_videos:
            link = local_link(p, root)
            parts.append(f'<figure><video controls preload="metadata" src="{link}"></video><figcaption>{video_caption(p, progress)} · <a href="{link}">영상 파일 열기</a></figcaption></figure>')
        parts.append('</div></section>')
    design_folder = root / "DesignProposals"
    design_image = design_folder / "LongSplit_and_ShortCoat_Concepts.png"
    design_readme = design_folder / "README.md"
    if public_file(design_image) or public_file(design_readme):
        parts.append('<section><div class="section-title"><h2>다음 생성용 2D 의상 시안</h2><span class="badge wait">디자인 제안 · 미적용</span></div>')
        parts.append('<p>기준 인물을 참조한 2D 디자인 제안입니다. 3D 모델이나 리깅 검증 결과가 아니며, 현재 후보와 정본에 아직 적용하지 않았습니다.</p>')
        if public_file(design_image):
            parts.append(figure(design_image, root, '왼쪽: 긴 기장·트임을 정리하는 방향 / 오른쪽: 무릎 위 기장으로 바꾸는 방향'))
        parts.append('<div class="checks"><div><span>왼쪽</span><span>긴 두루마기 유지 · 자락 패널과 트임을 명확히 구분</span></div><div><span>오른쪽</span><span>겉옷을 무릎 위로 단축 · 기장 변경에 대한 디자인 채택 필요</span></div></div>')
        parts.append('<p class="muted">정면 시안으로 뒤트임 구조를 검증할 수는 없습니다. 어느 안을 선택해도 실제 3D 연결면과 동작 변형은 다시 검사해야 합니다.</p>')
        link = local_link(design_readme, root)
        if link:
            parts.append(f'<p><a href="{link}">시안 설명과 제한 읽기</a></p>')
        parts.append('</section>')
    parts.append('<section><h2>제작 입력</h2><p class="muted">캐릭터 생성에 사용한 로컬 입력 이미지입니다.</p><div class="gallery">')
    inputs = []
    for item in source.get("inputs", []):
        value = item.get("path") if isinstance(item, dict) else None
        if not isinstance(value, str) or re.match(r"^[a-z]+://|^data:", value, re.I):
            continue
        p = Path(value.replace("\\", "/"))
        p = p if p.is_absolute() else root / p
        # Keep the review portable and exclude external files or private payloads.
        try:
            p.resolve().relative_to(root)
        except ValueError:
            continue
        if p.suffix.lower() in IMAGE_EXTS and public_file(p):
            inputs.append(p)
    if not inputs and (root / "Source/production_front.png").is_file():
        inputs = [root / "Source/production_front.png"]
    parts.extend(figure(p, root) for p in dict.fromkeys(inputs))
    if not inputs:
        parts.append('<p class="muted">입력 이미지가 아직 없습니다.</p>')
    parts.append('</div></section><footer>인터넷 연결 없이 열리는 로컬 검토 페이지 · 이미지의 파일명을 눌러 원본을 확인할 수 있습니다.</footer>')
    page = '<!doctype html><html lang="ko"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>오행부 · AutoPlayerV1 검토</title><style>' + CSS + '</style></head><body><main>' + ''.join(parts) + '</main></body></html>'
    # A concurrent reader always sees a complete old or new HTML file.
    target = root / "REVIEW.html"
    fd, temporary = tempfile.mkstemp(prefix=".REVIEW-", suffix=".tmp", dir=root)
    try:
        with os.fdopen(fd, "w", encoding="utf-8", newline="\n") as stream:
            stream.write(page)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary, target)
    finally:
        if os.path.exists(temporary):
            os.unlink(temporary)
    return {"review": str(target), "candidates": sorted(records), "recorded_status": overall,
            "verdict_changed": False, "notices": len(notices)}


CSS = """
:root{color-scheme:dark;--bg:#131514;--panel:#1d211f;--line:#39423c;--text:#edf0e9;--muted:#aab6ac;--accent:#d6c692}
*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--text);font:16px/1.65 system-ui,'Malgun Gothic',sans-serif}main{max-width:1200px;margin:auto;padding:48px 28px 24px}header{padding-bottom:28px;border-bottom:1px solid var(--line)}.eyebrow{color:var(--accent);font-size:.8rem;letter-spacing:.14em}h1{font-size:clamp(1.8rem,4vw,2.6rem);line-height:1.2;margin:16px 0 24px}h2{font-size:1.35rem;margin:0 0 20px}h3{font-size:1rem;margin:28px 0 14px}p{margin:12px 0}.muted,small,footer{color:var(--muted)}.metrics{display:grid;grid-template-columns:1.1fr .7fr 1.2fr;gap:14px;margin:28px 0}.metrics>div{border:1px solid var(--line);border-radius:10px;padding:22px;background:var(--panel)}.metrics span,.metrics strong,.metrics small{display:block}.metrics span{font-size:.85rem;color:var(--muted)}.metrics strong{font-size:1.2rem;margin:7px 0}.metrics small{font-size:.8rem}section{padding:32px 0;border-bottom:1px solid var(--line)}.section-title{display:flex;justify-content:space-between;align-items:baseline;gap:15px}.badge{display:inline-block;border:1px solid #666b55;background:#34392a;color:#e0dfb3;border-radius:6px;padding:3px 10px;font-size:.8rem;white-space:normal}.badge.pass{border-color:#426f55;color:#afe0bd;background:#203b2c}.badge.fail{border-color:#8b5655;color:#ffd0cb;background:#4c2827}.checks{display:grid;grid-template-columns:repeat(3,1fr);gap:10px}.checks>div{display:flex;align-items:center;justify-content:space-between;gap:12px;padding:10px 12px;border:1px solid var(--line);border-radius:6px}.checks span:first-child{font-size:.85rem}.gallery{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:18px}.gallery.four{grid-template-columns:repeat(4,minmax(0,1fr))}figure{margin:0;min-width:0}img,video{width:100%;display:block;aspect-ratio:16/9;object-fit:contain;background:#262b28;border:1px solid var(--line);border-radius:6px}figcaption{color:var(--muted);font-size:.78rem;padding:8px 0;overflow-wrap:anywhere}a{color:var(--accent);text-underline-offset:3px}a:focus-visible,summary:focus-visible{outline:2px solid var(--accent);outline-offset:4px}.notice{padding:16px 20px;background:#292d25;border-left:3px solid #9f956e;margin:18px 0;font-size:.9rem}.notice ul{margin:8px 0;padding-left:20px}details{margin:24px 0}summary{cursor:pointer;color:var(--accent);margin-bottom:18px}.scroll{overflow-x:auto}table{border-collapse:collapse;width:100%;font-size:.85rem}td,th{padding:10px 12px;text-align:left;border-bottom:1px solid var(--line)}th{color:var(--muted)}footer{padding:30px 0;font-size:.8rem}@media(max-width:850px){.metrics{grid-template-columns:1fr}.checks{grid-template-columns:repeat(2,1fr)}.gallery.four{grid-template-columns:repeat(2,minmax(0,1fr))}}@media(max-width:540px){main{padding:28px 16px}.checks,.gallery,.gallery.four{grid-template-columns:1fr}.section-title{display:block}}
"""


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=DEFAULT_ROOT)
    args = parser.parse_args()
    print(json.dumps(build(args.root), ensure_ascii=False))
