"""Build a local C02 lab review without awarding verdicts or changing experiment records.

Usage: python Tools/Blender/C02_RigFaceLab/build_lab_review.py [--root ART_LAB_FOLDER]
Writes only REVIEW.html and review_source_verification.json, using atomic replacement.
Optional root final_summary.json/run_state.json own aggregate verdicts, attempts_used,
and best_files. Stage reports may use explicit stage/attempt, verdicts, adopted fields.
Audit data PASS, file recency and artifact existence never become feature PASS.
"""
from __future__ import annotations

import argparse
from datetime import datetime, timezone
import hashlib
import html
import json
import os
from pathlib import Path
import re
import tempfile
from urllib.parse import quote


REPO = Path(__file__).resolve().parents[3]
DEFAULT_ROOT = REPO / "Art/PlayerPhase1/C02_RigFaceLab"
FEATURES = {
    "body_clothing_deformation": "몸·의복 변형",
    "animation_naturalness": "동작 자연스러움",
    "secondary_motion": "보조 움직임",
    "face_blink": "눈깜박임",
    "face_gaze": "시선",
    "face_jaw": "턱·입",
    "face_expressions": "표정",
    "fbx_roundtrip": "FBX 왕복",
    "unity_integration": "Unity 통합",
    "art_review": "사용자 미술 판단",
}
STAGES = {
    "A": ("몸·의복", 4, "원본 동작을 유지한 변형 비교"),
    "B": ("동작·보조 움직임", 3, "원본과 수정 클립·런타임 제어 구분"),
    "C": ("기본 얼굴", 3, "눈깜박임·시선·턱을 독립 검수"),
    "D": ("표정·통합", 3, "통과한 기본 기능으로 조합 검사"),
}
IMAGE_EXTS = {".png", ".jpg", ".jpeg", ".webp"}
VIDEO_EXTS = {".mp4", ".webm"}
GENERATED = {"review_source_verification.json", "run_state.draft.json"}
TEXT_FIELDS = (
    "candidate", "model", "blend", "source_blend", "clip", "action", "actions",
    "clips", "hypothesis", "method", "reason", "status", "scope", "visual_failure", "art_quality", "result", "adopted",
    "decision", "total_triangles", "triangles", "added_triangles", "new_bones",
    "new_clothing_bones", "channels", "evidence", "videos", "best_file",
    "production_motion", "fps", "frames", "source_frame_range",
)


def clean(value, limit=1400):
    if isinstance(value, (dict, list)):
        value = json.dumps(value, ensure_ascii=False)
    value = str(value if value is not None else "기록 없음")
    value = re.sub(r"https?://[^\s<>\"']+", "[외부 URL 생략]", value, flags=re.I)
    value = re.sub(r"data:[^\s]+", "[인라인 데이터 생략]", value, flags=re.I)
    value = re.sub(r"(?i)(bearer\s+|(?:access_token|refresh_token|api_key|secret)\s*[:=]\s*)\S+", "[인증정보 생략]", value)
    return html.escape(value[:limit], quote=True)


def atomic_write(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    handle, temporary = tempfile.mkstemp(prefix=".review-", dir=path.parent)
    try:
        with os.fdopen(handle, "w", encoding="utf-8", newline="\n") as stream:
            stream.write(value)
        os.replace(temporary, path)
    finally:
        if os.path.exists(temporary):
            os.unlink(temporary)


def is_private(path):
    return any(part.startswith(".") or part.lower() == "private" for part in path.parts)


def files_below(root):
    """Skip frame dumps/private folders before descending, not after allocating paths."""
    found = []
    for folder, directories, names in os.walk(root):
        directories[:] = sorted(name for name in directories if not name.startswith(".")
                                and name.lower() != "private"
                                and not re.search(r"(^|[_-])(frames?|framepng)([_-]|$)", name, re.I))
        for name in sorted(names):
            path = Path(folder) / name
            if path.is_file() and not path.is_symlink() and not is_private(path.relative_to(root)):
                found.append(path)
    return found


def read_json(path, notices):
    try:
        if path.stat().st_size > 8 * 1024 * 1024:
            notices.append(f"{path.name}: 큰 수치 원장은 펼치지 않고 파일로 연결합니다.")
            return None
        return json.loads(path.read_text(encoding="utf-8-sig"))
    except (OSError, ValueError):
        notices.append(f"{path.name}: 작성 중이거나 읽을 수 없어 이번 스냅샷에서 생략했습니다.")
        return None


def relative_url(path, root):
    path = path.resolve()
    if not path.is_file() or is_private(path.relative_to(root)):
        return None
    try:
        return quote(path.relative_to(root).as_posix(), safe="/.-_")
    except ValueError:
        return None


def link(path, root, label=None):
    try:
        url = relative_url(path, root)
    except ValueError:
        url = None
    if not url:
        return f'<span class="muted">{clean(label or path.name)} (파일 없음/외부 경로)</span>'
    return f'<a href="{url}">{clean(label or path.relative_to(root).as_posix())}</a>'


def badge(value):
    if isinstance(value, dict):
        value = value.get("status", value.get("verdict", "UNVERIFIED"))
    value = str(value)
    tone = "pass" if value == "PASS" else "fail" if value.startswith("FAIL") else "wait"
    return f'<span class="badge {tone}">{clean(value, 160)}</span>'


def feature_value(record, key):
    if not isinstance(record, dict):
        return None
    for container in (record.get("verdicts"), record.get("statuses"), record.get("results"), record):
        if isinstance(container, dict) and key in container:
            value = container[key]
            if isinstance(value, str) or isinstance(value, dict) and ("status" in value or "verdict" in value):
                return value
    return None


def record_stage(path, record):
    stage = str(record.get("stage", "")).upper()
    if stage in STAGES:
        return stage
    # Path membership routes records; it does not invent an attempt or verdict.
    for part in reversed(path.parts):
        match = re.fullmatch(r"([ABCD])\d+(?:\.[^.]+)?", part, re.I)
        if match:
            return match[1].upper()
    return None


def source_verification(root, preservation, now):
    """Verify only recorded, local workspace sources; this is not rig verification."""
    rows = []
    entries = preservation.get("files", {}) if isinstance(preservation, dict) else {}
    for raw_path, expected in entries.items():
        path = Path(raw_path)
        entry = {"path": str(path), "expected_sha256": expected.get("sha256") if isinstance(expected, dict) else None}
        try:
            resolved = path.resolve()
            resolved.relative_to(REPO)
            if is_private(resolved.relative_to(REPO)):
                raise ValueError("private source")
            before = resolved.stat()
            digest = hashlib.sha256()
            with resolved.open("rb") as stream:
                for chunk in iter(lambda: stream.read(1024 * 1024), b""):
                    digest.update(chunk)
            after = resolved.stat()
            entry["actual_sha256"] = digest.hexdigest()
            entry["bytes"] = after.st_size
            if (before.st_size, before.st_mtime_ns) != (after.st_size, after.st_mtime_ns):
                entry["status"] = "CHANGED_DURING_READ"
            elif not entry["expected_sha256"]:
                entry["status"] = "NO_EXPECTED_HASH"
            else:
                entry["status"] = "UNCHANGED" if digest.hexdigest().lower() == str(entry["expected_sha256"]).lower() else "CHANGED"
        except FileNotFoundError:
            entry["status"] = "MISSING"
        except (OSError, ValueError):
            entry["status"] = "UNVERIFIED_LOCAL_PATH"
        rows.append(entry)
    baseline = root / "Baseline.blend"
    if preservation.get("baseline_sha256"):
        baseline_record = verify_file_hash(baseline, preservation["baseline_sha256"])
    else:
        baseline_record = {"path": str(baseline), "status": "NO_EXPECTED_HASH"}
    return {"generated_at": now, "kind": "preservation_hash_comparison_not_quality_verdict",
            "input": "preservation_before.json", "protected_files": rows, "baseline_copy": baseline_record,
            "all_recorded_files_unchanged": bool(rows) and all(r["status"] == "UNCHANGED" for r in rows),
            "canonical_player_replaced": "not_inferred_from_hashes"}


def verify_file_hash(path, expected):
    result = {"path": str(path), "expected_sha256": expected}
    try:
        before = path.stat()
        digest = hashlib.sha256()
        with path.open("rb") as stream:
            for chunk in iter(lambda: stream.read(1024 * 1024), b""):
                digest.update(chunk)
        after = path.stat()
        result["actual_sha256"] = digest.hexdigest()
        result["status"] = ("CHANGED_DURING_READ" if (before.st_size, before.st_mtime_ns) != (after.st_size, after.st_mtime_ns)
                            else "UNCHANGED" if digest.hexdigest().lower() == str(expected).lower() else "CHANGED")
    except OSError:
        result["status"] = "UNVERIFIED"
    return result


def diagnostic(path):
    return bool(re.search(r"warning|diagnostic|landmarks?|heatmap|wireframe|overlay|진단|경고", path.as_posix(), re.I))


def media_note(path, root, records):
    notes = records.get(root / "review_media_notes.json", {}).get("files", {})
    explicit = notes.get(path.relative_to(root).as_posix())
    if isinstance(explicit, dict):
        return explicit
    for folder in (path.parent, path.parent.parent):
        validation = records.get(folder / "video_validation.json", {})
        if validation.get("visual_failure") or str(validation.get("status", "")).startswith("FAILED_CAPTURE"):
            return {"status": "FAIL_VISUAL_CAPTURE", "reason": validation.get("visual_failure", "캡처 실패로 기록됨"),
                    "scope": "시각 검수에 쓸 수 없는 캡처이며 모델 자체의 실패 판정과 구분합니다."}
    if any("probe" in part.lower() for part in path.parts):
        return {"status": "TEST_CAPTURE_UNVERIFIED", "reason": "구도·연결 확인용 시험 캡처입니다. 최종 검수 영상으로 채택하지 않습니다."}
    return {}


def media_group(paths, root, title, video=False, records=None):
    if not paths:
        return f'<h3>{clean(title)}</h3><p class="muted">현재 파일 없음.</p>'
    cards = []
    for path in paths:
        url = relative_url(path, root)
        if not url:
            continue
        media = (f'<video controls preload="none" src="{url}"></video>' if video
                 else f'<a href="{url}"><img loading="lazy" src="{url}" alt="{clean(path.stem)}"></a>')
        note = media_note(path, root, records or {})
        annotation = (f'<p>{badge(note.get("status", "UNVERIFIED"))} {clean(note.get("reason", ""))}</p>'
                      f'<p>{clean(note.get("scope", ""))}</p>') if note else ""
        cards.append(f'<figure>{media}<figcaption>{link(path, root)}{annotation}</figcaption></figure>')
    return f'<h3>{clean(title)}</h3><div class="gallery">{"".join(cards)}</div>'


def record_card(path, record, root):
    lines = []
    for key in TEXT_FIELDS:
        if key in record:
            lines.append(f'<dt>{clean(key)}</dt><dd>{clean(record[key])}</dd>')
    verdicts = []
    for key, title in FEATURES.items():
        value = feature_value(record, key)
        if value is not None:
            reason = f' — {clean(value.get("reason"))}' if isinstance(value, dict) and value.get("reason") else ""
            verdicts.append(f'<li>{title}: {badge(value)}{reason}</li>')
    return (f'<article><h3>{link(path, root)}</h3>'
            f'<dl>{"".join(lines)}</dl><ul>{"".join(verdicts)}</ul></article>')


def build(root):
    root = root.resolve()
    if not root.is_dir():
        raise SystemExit(f"Lab folder does not exist: {root}")
    now = datetime.now(timezone.utc).isoformat()
    notices = []
    paths = files_below(root)
    records = {}
    for path in paths:
        if path.suffix.lower() != ".json" or path.name in GENERATED or path.name.endswith("_audit.json") or path.name.startswith(("command_", "response_")):
            continue
        value = read_json(path, notices)
        if isinstance(value, dict):
            records[path] = value
    final_path, run_path = root / "final_summary.json", root / "run_state.json"
    final, run = records.get(final_path, {}), records.get(run_path, {})
    final_status = final.get("status", final.get("overall_status"))
    has_final = bool(final_status) and not final.get("is_draft", False) and final_status not in {"RUNNING", "IN_PROGRESS", "DRAFT"}
    owners = [(final_path, final if has_final else {}), (run_path, run)]
    phase = final.get("status", final.get("overall_status")) if has_final else run.get("phase", run.get("status", "진행 중 · 실행 상태 원장 미기록"))
    preservation = records.get(root / "preservation_before.json", {})
    verification = source_verification(root, preservation, now)
    quality = records.get(root / "Unity/quality_preservation_before.json", {})
    additional = []
    if quality.get("path") and quality.get("sha256"):
        path = Path(quality["path"]).resolve()
        expected_path = (REPO / "Oheangbu/ProjectSettings/QualitySettings.asset").resolve()
        additional.append(verify_file_hash(path, quality["sha256"]) if path == expected_path else
                          {"path": str(path), "status": "UNVERIFIED_UNEXPECTED_QUALITY_PATH"})
    verification["additional_protected_files"] = additional
    verification["all_protected_files_unchanged"] = verification["all_recorded_files_unchanged"] and all(row["status"] == "UNCHANGED" for row in additional)
    verify_path = root / "review_source_verification.json"
    atomic_write(verify_path, json.dumps(verification, ensure_ascii=False, indent=2) + "\n")
    body = [f'<header><p>C02 RIG & FACE LAB</p><h1>C02 리깅·얼굴 실험 검수</h1><p>{badge(phase)}</p>'
            f'<p>{"최종 요약 기록" if has_final else "실행 중 스냅샷"} · {clean(now)}</p>'
            '<p>명시된 JSON 판정만 표시합니다. 파일 생성·수치 데이터 통과·최근 저장은 품질 통과나 최선 결과 선정이 아닙니다.</p></header>',
            '<nav><a href="#verdicts">판정</a><a href="#stages">A–D</a><a href="#assets">모델·클립</a>'
            '<a href="#media">영상·이미지</a><a href="#sources">원본 검증</a></nav>']
    final_artifacts = records.get(final_path, {}).get('artifacts', {})
    if final_artifacts:
        body.append('<section><h2>먼저 확인할 결과</h2><p>A2 겨드랑이 웨이트 · B2 Idle 접지 · C3 눈꺼풀. 전체 캐릭터 품질은 미검증이며 정본은 교체하지 않았습니다.</p><p>')
        selected_labels = {'integrated_blend':'최선 Blender', 'integrated_fbx':'최선 FBX', 'original_texture':'텍스처', 'unity_report':'Unity 검수 보고서'}
        for key, label in selected_labels.items():
            item = final_artifacts.get(key, {})
            if isinstance(item, dict) and item.get('path'):
                body.append(link(root / item['path'], root, label) + ' · ')
        body.append(link(root / 'FINAL_REPORT.md', root, '전체 결과 보고서') + '</p>')
        selected_videos = [root / final_artifacts[key]['path'] for key in ('unity_body_video', 'unity_face_video')
                           if isinstance(final_artifacts.get(key), dict) and final_artifacts[key].get('path')]
        body.append(media_group(selected_videos, root, '최종 B2 Unity 실제 재생', video=True, records=records))
        body.append('</section>')
    body.append('<section><h2>범위와 비용</h2><p>추가 유료 사용 한도 <strong>0크레딧</strong>. 이전 AutoPlayerV1 실제 지출 <strong>123크레딧</strong>은 별도 역사적 비용입니다. 이 생성기는 유료 호출을 하지 않습니다.</p>'
                '<p>새 의복 변형 본 ≤12 · 실제 착의 전신 ≤60,000삼각형 · 원본 AutoPlayerV1/C02, FitRigV3, C2, 공용 PlayerRig 보존.</p>')
    for owner_path, owner in owners:
        if owner:
            body.append(f'<p>상태 원장: {link(owner_path, root)}</p>')
            for key in ("additional_paid_credits", "additional_credits_consumed", "paid_requests", "canonical_player_replaced"):
                if key in owner:
                    body.append(f'<p>{clean(key)}: {clean(owner[key])} · {clean(owner_path.name)}</p>')
    body.append('</section><section id="verdicts"><h2>기능별 판정</h2><p>전체 판정이 없으면 기본값 NOT_ATTEMPTED를 표시합니다. 개별 후보의 실제 시도·판정은 아래 단계 기록에 별도로 남습니다.</p><table><tr><th>기능</th><th>명시된 전체 판정</th><th>근거</th></tr>')
    for key, title in FEATURES.items():
        supplied = next(((path, feature_value(owner, key)) for path, owner in owners if feature_value(owner, key) is not None), None)
        if supplied:
            path, value = supplied
            reason = clean(value.get("scope", value.get("reason", ""))) if isinstance(value, dict) else ""
            body.append(f'<tr><td>{title}</td><td>{badge(value)} {reason}</td><td>{link(path, root)}</td></tr>')
        else:
            body.append(f'<tr><td>{title}</td><td>{badge("NOT_ATTEMPTED")}</td><td>전체 판정 원장 미기록 · 개별 기록과 구분</td></tr>')
    body.append('</table></section><section id="stages"><h2>단계별 시도와 채택</h2>')
    stage_record_paths = set()
    for stage, (title, limit, purpose) in STAGES.items():
        selected = [(path, record) for path, record in records.items() if record_stage(path.relative_to(root), record) == stage]
        stage_record_paths.update(path for path, _ in selected)
        attempts = sorted({record["attempt"] for _, record in selected if isinstance(record.get("attempt"), int) and not isinstance(record["attempt"], bool)})
        used = next((owner["attempts_used"][stage] for _, owner in owners if isinstance(owner.get("attempts_used"), dict) and stage in owner["attempts_used"]), None)
        body.append(f'<h3>{stage} · {title}</h3><p>{purpose}. 상한 {limit}회. '
                    f'원장 사용 횟수: {clean(used) if used is not None else "미기록"}; 개별 기록의 시도 번호: {clean(attempts) if attempts else "없음"}.</p>')
        best = next((owner["best_files"][stage] for _, owner in owners if isinstance(owner.get("best_files"), dict) and stage in owner["best_files"]), None)
        if best is not None:
            body.append(f'<p>명시된 최선 결과: {clean(best)} (원장 값)</p>')
        else:
            body.append('<p class="muted">명시된 최선 결과 없음. 최신 파일을 자동 채택하지 않습니다.</p>')
        body.extend(record_card(path, record, root) for path, record in selected)
        if not selected:
            body.append(f'<p>{badge("NOT_ATTEMPTED")} · 이 단계의 명시 실험 기록이 아직 없습니다.</p>')
    body.append('</section><section><h2>공통·Unity 기록</h2>')
    supplemental = [(path, record) for path, record in records.items() if path not in stage_record_paths and path not in {final_path, run_path}
                    and (any(feature_value(record, key) is not None for key in FEATURES) or "Unity" in path.parts)]
    body.extend(record_card(path, record, root) for path, record in supplemental)
    if not supplemental:
        body.append('<p class="muted">별도 기능 판정 기록 없음.</p>')
    body.append('</section><section id="assets"><h2>모델·내보내기·동작 원천</h2><p>아래는 파일 목록입니다. 채택·기능 통과 여부를 뜻하지 않습니다. FBX 안의 클립은 실제 원장에 기록된 경우에만 확인된 클립으로 봅니다.</p><ul>')
    models = [p for p in paths if p.suffix.lower() in {".blend", ".fbx", ".anim", ".controller"}]
    body.extend(f'<li>{link(path, root)} <span class="muted">{path.stat().st_size / 1024 / 1024:.1f} MB</span></li>' for path in models)
    if not models:
        body.append('<li>현재 모델·동작 파일 없음.</li>')
    body.append('</ul>')
    inventory = records.get(root / "baseline_inventory.json", {})
    if inventory:
        body.append(f'<p>Baseline 실제 데이터: {link(root / "baseline_inventory.json", root)} · {clean(inventory.get("triangles"))}삼각형 / {clean(inventory.get("vertices"))}정점.</p>')
        body.append(f'<details><summary>기준 클립 목록</summary><pre>{clean(inventory.get("actions", inventory.get("clips", [])), 10000)}</pre></details>')
    protocols = [(path, record) for path, record in records.items() if "action" in record and "protocol" in path.name]
    body.extend(record_card(path, record, root) for path, record in protocols)
    body.append('</section><section id="media"><h2>검수 미디어</h2><p>Blender 렌더와 Unity 캡처는 폴더·실제 보고서로 구분합니다. 영상의 원래 속도·해상도·검사 범위는 해당 기록을 따르며 파일 존재로 검증하지 않습니다.</p>')
    for folder in ("Body", "Face", "Unity", "Shared"):
        group = [path for path in paths if (path.relative_to(root).parts[0] == folder if folder != "Shared" else path.relative_to(root).parts[0] not in {"Body", "Face", "Unity", "Source"})]
        videos = [path for path in group if path.suffix.lower() in VIDEO_EXTS]
        images = [path for path in group if path.suffix.lower() in IMAGE_EXTS and any("preview" in part.lower() for part in path.relative_to(root).parts)]
        if not group and folder == "Shared":
            continue
        body.append(f'<h3>{folder}</h3>')
        is_test = lambda p: diagnostic(p) or bool(media_note(p, root, records))
        body.append(media_group([p for p in videos if not is_test(p)], root, "일반 연속 영상", video=True, records=records))
        body.append(media_group([p for p in images if not is_test(p)], root, "일반·중립 회색 검수 이미지", records=records))
        diagnostics = [p for p in videos + images if is_test(p)]
        if diagnostics:
            body.append('<details><summary>실패·시험 캡처 및 진단 미디어 — 정상 최종 영상과 구분</summary>')
            body.append(media_group([p for p in diagnostics if p.suffix.lower() in VIDEO_EXTS], root, "실패·시험·진단 영상", video=True, records=records))
            body.append(media_group([p for p in diagnostics if p.suffix.lower() in IMAGE_EXTS], root, "실패·시험·진단 이미지", records=records))
            body.append('</details>')
    body.append('</section><section id="sources"><h2>원본 검증·원장</h2><p>아래 해시 비교는 원본 파일 보존 검사입니다. 리깅·변형·미술 통과 판정과 관계없습니다.</p>')
    body.append(f'<p>{link(verify_path, root)} · 검사 시점 {clean(now)}</p><table><tr><th>기록된 원본</th><th>해시 비교</th></tr>')
    for row in verification["protected_files"] + verification["additional_protected_files"] + [verification["baseline_copy"]]:
        body.append(f'<tr><td>{clean(row["path"])}</td><td>{clean(row["status"])}</td></tr>')
    body.append('</table><details><summary>공개 JSON·문서 링크</summary><ul>')
    for path in paths:
        if path.suffix.lower() in {".json", ".md"} and path.name != "review_source_verification.json":
            body.append(f'<li>{link(path, root)}</li>')
    body.append('</ul></details>')
    if notices:
        body.append('<h3>스냅샷 읽기 메모</h3><ul>' + ''.join(f'<li>{clean(note)}</li>' for note in notices) + '</ul>')
    body.append('</section><footer>재생성: python Tools/Blender/C02_RigFaceLab/build_lab_review.py<br>원장·모델·씬은 수정하지 않습니다. REVIEW.html과 별도 원본 해시 검사 JSON만 원자적으로 갱신합니다.</footer>')
    css = """*{box-sizing:border-box}body{margin:0;background:#161a1d;color:#e7e5df;font:16px/1.55 system-ui,sans-serif}main{max-width:1340px;margin:auto;padding:32px}header,section{background:#20272a;border:1px solid #394447;border-radius:10px;padding:24px;margin-bottom:20px}h1{font-size:32px;margin:6px 0}h2{font-size:24px}h3{font-size:18px}a{color:#acd9d2;overflow-wrap:anywhere}nav{display:flex;gap:24px;flex-wrap:wrap;margin:20px 0}.muted{color:#adb7b6}.badge{display:inline-block;border-radius:5px;padding:2px 8px;font-size:13px;background:#514c38;color:#f3e8b7}.pass{background:#285143;color:#c6ffe1}.fail{background:#673f3e;color:#ffdad6}table{border-collapse:collapse;width:100%;font-size:14px}td,th{text-align:left;padding:10px;border-bottom:1px solid #445052;vertical-align:top;overflow-wrap:anywhere}article{background:#192024;padding:18px;border-radius:7px;margin:14px 0}dl{display:grid;grid-template-columns:180px minmax(0,1fr);gap:5px 18px;font-size:14px}dt{color:#bbc9c6}dd{margin:0;overflow-wrap:anywhere}.gallery{display:grid;grid-template-columns:repeat(auto-fit,minmax(290px,1fr));gap:15px}figure{margin:0;background:#13191c;border-radius:6px;overflow:hidden}img,video{width:100%;max-height:520px;object-fit:contain;display:block}figcaption{font-size:12px;padding:9px}details{margin:18px 0}summary{cursor:pointer}pre{white-space:pre-wrap;overflow-wrap:anywhere}footer{padding:16px;color:#a7b7b4;font-size:13px}@media(max-width:700px){main{padding:12px}section,header{padding:16px}dl{display:block}dd{margin-bottom:8px}}
"""
    document = '<!doctype html><html lang="ko"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>C02 Rig & Face Lab</title><style>' + css + '</style></head><body><main>' + ''.join(body) + '</main></body></html>'
    atomic_write(root / "REVIEW.html", document)
    return {"review": str(root / "REVIEW.html"), "source_verification": str(verify_path),
            "models": len(models), "videos": sum(p.suffix.lower() in VIDEO_EXTS for p in paths),
            "read_notices": len(notices), "final_summary_present": has_final}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=DEFAULT_ROOT)
    print(json.dumps(build(parser.parse_args().root), ensure_ascii=False))
