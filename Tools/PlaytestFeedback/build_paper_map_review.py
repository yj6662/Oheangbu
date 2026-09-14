"""Build an offline review from existing Unity paper-map evidence.

This does not run Unity, pose a mesh, manufacture frames, or render video. The
storyboard uses only the latest paper_review.json run and PNGs it records as
written. Re-run after captures or build evidence arrive to refresh both outputs.
"""

from __future__ import annotations

import hashlib
import html
import json
import os
import re
import struct
from datetime import datetime, timezone
from pathlib import Path
from urllib.parse import quote, unquote, urlparse


ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "Art/UIAudio/PaperMapReview"
VALIDATION = OUT / "Validation"
PAPER = ROOT / "Oheangbu/Assets/_Project/Resources/WorldMap/Paper/HanjiWorn.png"
SOURCE = ROOT / "Oheangbu/Assets/_Project/Scripts/App/World/UI"
PHASES = [
    ("01_open_packet_010", "01 · 접힌 종이 등장", "열기 목표 0.10 · 두 번 접힌 네 겹의 종이"),
    ("02_open_vertical_030", "02 · 세로 접힘 펼치기", "열기 목표 0.30 · 바깥 세로 접힘이 안쪽 접힘을 함께 들어 올림"),
    ("03_open_horizontal_065", "03 · 가로 접힘 펼치기", "열기 목표 0.65 · 남은 가로 접힘을 펼쳐 한 장으로 연결"),
    ("03b_open_printed_085", "03b · 인쇄면이 드러나는 순간", "열기 목표 0.85 · 움직이는 종이의 인쇄면과 지도 잉크 확인"),
    ("04_open_flat_100", "04 · 펼침 완료", "열기 목표 1.00 · 네 사분면과 지도 정보의 정렬 확인"),
    ("05_close_half_050", "05 · 다시 접기", "닫기 목표 0.50 · 펼침의 역순으로 종이를 접음"),
    ("06_reduced_open", "06 · 동작 줄이기 — 열기", "진단 API로 펼침 완료 상태에 즉시 도달"),
    ("07_reduced_closed", "07 · 동작 줄이기 — 닫기", "진단 API로 접힘 완료 상태에 즉시 도달"),
]
VISUAL_CHECKS = [
    "닫힌 종이가 네 겹으로 읽히고, 여섯 개의 세로 패널처럼 보이지 않는가",
    "바깥 세로 접힘 이후 안쪽 가로 접힘이 펼쳐지는 순서가 자연스러운가",
    "종이의 뒷면·주름·접힌 선·닳은 가장자리가 실제 화면에서 설득력 있게 보이는가",
    "겹친 종이의 가림 순서와 교차 접힘 중심에 뚫림·깜빡임이 없는가",
    "완전히 펼쳤을 때 지도, 탐험 안개, 현재 위치, 마커와 라벨이 맞게 정렬되는가",
    "M으로 열고 닫는 속도와 종이 소리가 플레이 흐름에 맞는가",
]


def escape(value: object) -> str:
    return html.escape(str(value), quote=True)


def url(path: Path) -> str:
    return quote(os.path.relpath(path, OUT).replace(os.sep, "/"), safe="/._-~")


def load_json(path: Path):
    try:
        return json.loads(path.read_text(encoding="utf-8-sig"))
    except (OSError, UnicodeError, json.JSONDecodeError):
        return None


def status(value: object) -> str:
    normalized = str(value or "").upper()
    return normalized if normalized in {"PASS", "FAIL"} else "UNVERIFIED"


def badge(value: object) -> str:
    state = status(value)
    return f'<span class="badge {state.lower()}">{state}</span>'


def source_path(recorded: object) -> Path | None:
    if not isinstance(recorded, str) or not recorded:
        return None
    path = Path(recorded)
    if path.is_absolute():
        return path
    for base in (OUT, ROOT):
        candidate = base / path
        if candidate.exists():
            return candidate
    return OUT / path


def png_size(path: Path | None) -> tuple[int, int] | None:
    if path is None:
        return None
    try:
        with path.open("rb") as stream:
            header = stream.read(24)
        if len(header) == 24 and header[:8] == b"\x89PNG\r\n\x1a\n" and header[12:16] == b"IHDR":
            return struct.unpack(">II", header[16:24])
    except OSError:
        pass
    return None


def newest_report() -> tuple[Path | None, dict]:
    candidates = sorted(VALIDATION.rglob("paper_review.json"), key=lambda p: (p.stat().st_mtime_ns, str(p))) if VALIDATION.exists() else []
    if not candidates:
        return None, {}
    path = candidates[-1]
    content = load_json(path)
    return path, content if isinstance(content, dict) else {}


def evidence_files() -> list[Path]:
    if not VALIDATION.exists():
        return []
    return sorted(p for p in VALIDATION.rglob("*") if p.is_file() and p.suffix.lower() in {".json", ".md", ".txt", ".log", ".csv"})


def candidate_report_result(field: str, content: object) -> dict:
    result = {"status": "UNVERIFIED", "recorded": "명시 판정 없음"}
    if not isinstance(content, dict):
        return result
    if field == "buildReport":
        build_result, errors = content.get("buildResult"), content.get("totalErrors")
        valid_errors = isinstance(errors, int) and not isinstance(errors, bool) and errors >= 0
        result["recorded"] = (f"빌드 생성만: buildResult={build_result}; totalErrors={errors}; "
                              f"totalWarnings={content.get('totalWarnings', '미기록')}; "
                              f"qaComplete={content.get('qaComplete', '미기록')}; 원본 status={content.get('status', '미기록')}")
        if str(content.get("status", "")).upper() in {"RUNNING", "IN_PROGRESS", "BUILDING"}:
            return result
        if str(build_result).upper() == "SUCCEEDED" and valid_errors and errors == 0:
            result["status"] = "PASS"
        elif str(build_result).upper() in {"FAILED", "CANCELLED", "CANCELED"} or (valid_errors and errors > 0):
            result["status"] = "FAIL"
        return result
    if field == "smokeReport":
        outcome = str(content.get("outcome", ""))
        passed, failed, unverified = (content.get(key) for key in ("passed", "failed", "unverified"))
        counts_valid = all(isinstance(value, int) and not isinstance(value, bool) and value >= 0
                           for value in (passed, failed, unverified))
        if counts_valid:
            result["recorded"] = f"{passed}통과 · {unverified}미검증 · {failed}실패; outcome={outcome or '미기록'}"
            if failed > 0 or outcome.upper() in {"FAIL", "FAILED", "FAILURE"}:
                result["status"] = "FAIL"
            elif outcome.upper() == "PASS" and passed > 0 and unverified == 0:
                result["status"] = "PASS"
            elif unverified > 0:
                result["recorded"] += "; 일부 미검증이 남아 전체 QA 통과로 표시하지 않음"
        else:
            result["recorded"] = f"outcome={outcome or '미기록'}; 검사 개수 누락 또는 알 수 없는 형식"
        return result
    if field == "packageReport":
        crc, file_count, sha256 = content.get("crcVerified"), content.get("files"), content.get("sha256")
        files_valid = isinstance(file_count, int) and not isinstance(file_count, bool) and file_count > 0
        hash_valid = isinstance(sha256, str) and re.fullmatch(r"[0-9a-fA-F]{64}", sha256) is not None
        result["recorded"] = (f"패키지 CRC 검사만: crcVerified={crc}; files={file_count}; "
                              f"sha256={sha256 or '미기록'}; qaComplete={content.get('qaComplete', '미기록')}")
        if crc is True and files_valid and hash_valid:
            result["status"] = "PASS"
        elif crc is False:
            result["status"] = "FAIL"
        return result
    return result


def candidate_evidence() -> tuple[Path, list[dict]]:
    manifest = VALIDATION / "build_candidate.json"
    data = load_json(manifest)
    data = data if isinstance(data, dict) else {}
    entries = []
    for field, label, is_report in (
        ("candidateFolder", "빌드 후보 폴더", False),
        ("executable", "실행 파일", False),
        ("archive", "압축 패키지", False),
        ("buildReport", "빌드 검사", True),
        ("smokeReport", "실행 스모크 검사", True),
        ("packageReport", "패키지 검사", True),
    ):
        path = source_path(data.get(field))
        exists = path is not None and path.exists()
        entry = {"field": field, "label": label, "path": path, "exists": exists,
                 "is_report": is_report, "status": "UNVERIFIED", "recorded": "명시 판정 없음"}
        if is_report and exists and path.is_file():
            entry.update(candidate_report_result(field, load_json(path)))
        entries.append(entry)
    return manifest, entries


def candidate_html() -> str:
    manifest, entries = candidate_evidence()
    manifest_link = f'<a href="{url(manifest)}">build_candidate.json</a>' if manifest.is_file() else 'build_candidate.json 미수집'
    cards = []
    for entry in entries:
        path = entry["path"]
        target = f'<a href="{url(path)}">{escape(path.name)}</a>' if entry["exists"] else '파일 미수집'
        state = badge(entry["status"]) if entry["is_report"] else '<span class="muted">' + ('파일 있음' if entry["exists"] else 'UNVERIFIED') + '</span>'
        note = entry["recorded"] if entry["is_report"] else '파일 존재만 표시하며 실행 성공이나 사람의 승인을 뜻하지 않습니다.'
        cards.append(f'<article class="evidence-card">{state}<h3>{escape(entry["label"])}</h3><p>{target}</p><p>{escape(note)}</p></article>')
    return f'<p class="muted">후보 매니페스트: {manifest_link}. 빌드는 생성 성공과 오류 수, 스모크는 통과·실패·미검증 수, 패키지는 CRC·해시·파일 수를 구분해 읽습니다. 빌드 생성 또는 CRC의 PASS는 전체 QA 완료가 아닙니다.</p><div class="evidence-grid">' + ''.join(cards) + '</div>'


def candidate_markdown() -> list[str]:
    manifest, entries = candidate_evidence()
    lines = ["", "## 빌드 후보", ""]
    lines.append(f"[후보 매니페스트]({url(manifest)})" if manifest.is_file() else "후보 매니페스트 미수집 · UNVERIFIED.")
    lines += ["", "| 항목 | 상태 | 파일 / 명시 판정 |", "|---|---|---|"]
    for entry in entries:
        path = entry["path"]
        target = f"[{path.name}]({url(path)})" if entry["exists"] else "파일 미수집"
        state = entry["status"] if entry["is_report"] else ("파일 있음" if entry["exists"] else "UNVERIFIED")
        detail = entry["recorded"] if entry["is_report"] else "파일 존재는 실행 성공이나 사람의 승인을 뜻하지 않음"
        lines.append(f"| {entry['label']} | {state} | {target} · {detail} |")
    lines += ["", "빌드 PASS는 buildResult=Succeeded 및 totalErrors=0일 때의 생성 성공만 뜻합니다. 스모크는 outcome과 passed/failed/unverified를 함께 읽으며 일부 미검증이 남으면 통과 개수를 표시하고 전체 상태를 UNVERIFIED로 유지합니다. 패키지 PASS는 crcVerified=true, SHA-256 기록 및 양수 파일 수가 있는 CRC 검사만 뜻합니다. qaComplete=false를 전체 QA 승인으로 바꾸지 않으며 알 수 없는 형식은 UNVERIFIED입니다."]
    return lines


def collect_frames(report: dict) -> tuple[list[dict], list[dict]]:
    by_id = {item.get("id"): item for item in report.get("stills", []) if isinstance(item, dict)}
    available, missing = [], []
    for phase_id, title, guide in PHASES:
        record = by_id.get(phase_id, {})
        path = source_path(record.get("path"))
        size = png_size(path)
        frame = {"id": phase_id, "title": title, "guide": guide, "record": record, "path": path, "size": size}
        if record.get("written") is True and size and path is not None:
            frame["url"] = url(path)
            available.append(frame)
        else:
            missing.append(frame)
    return available, missing


def check_rows(report: dict, frames: list[dict], missing: list[dict]) -> list[dict]:
    rows = []
    for check in report.get("checks", []):
        if isinstance(check, dict):
            rows.append({"id": check.get("id", "unnamed-check"), "status": status(check.get("status")), "detail": check.get("detail", "")})
    rows.append({"id": "review-png-evidence-available", "status": "PASS" if not missing else "UNVERIFIED",
                 "detail": f"Latest run: {len(frames)}/{len(PHASES)} planned PNGs are recorded written and have a valid PNG header. Missing captures are not technical passes."})
    rows.append({"id": "human-visual-judgment", "status": "UNVERIFIED", "detail": "사람이 실제 펼침의 형태·가림·질감을 판단해야 합니다. 자동 기술 검사로 시각적 만족을 승인하지 않습니다."})
    rows.append({"id": "native-M-input-and-audio", "status": "UNVERIFIED", "detail": "이 캡처는 진단 API의 실제 시간 전환입니다. M 키 입력, 사람의 소리 청취, 실제 기기 GPU 성능을 증명하지 않습니다."})
    return rows


def obj_models() -> list[dict]:
    folder = OUT / "Models"
    result = []
    for path in sorted(folder.rglob("*.obj")) if folder.exists() else []:
        vertices = faces = 0
        try:
            with path.open(encoding="utf-8-sig", errors="replace") as stream:
                for line in stream:
                    vertices += line.startswith("v ")
                    faces += line.startswith("f ")
            result.append({"path": path, "vertices": vertices, "faces": faces})
        except OSError:
            continue
    return result


def format_number(value: object, digits: int = 3) -> str:
    return f"{value:.{digits}f}" if isinstance(value, (int, float)) else "미기록"


def frame_caption(frame: dict) -> str:
    record = frame["record"]
    return (f"요청 {format_number(record.get('requestedProgress'))} · 캡처 발행 {format_number(record.get('progressAtIssue'))} "
            f"→ 다음 프레임 {format_number(record.get('progressAfterIssue'))} · 전환 경과 {format_number(record.get('transitionElapsedSeconds'))} s · "
            f"프레임 {record.get('frameAtIssue', '미기록')} → {record.get('frameAfterIssue', '미기록')}")


def image_card(path: Path, title: str, caption: str) -> str:
    if not path.is_file():
        return f'<article class="card"><h3>{escape(title)}</h3>{badge("UNVERIFIED")}<p>파일이 아직 없습니다.</p></article>'
    return (f'<figure class="card"><a href="{url(path)}"><img src="{url(path)}" alt="{escape(title)}" loading="lazy"></a>'
            f'<figcaption><h3>{escape(title)}</h3><p>{escape(caption)}</p><a href="{url(path)}">원본 열기</a></figcaption></figure>')


def evidence_card(path: Path, latest: Path | None) -> str:
    content = load_json(path) if path.suffix.lower() == ".json" else None
    state = status(content.get("status")) if isinstance(content, dict) else "UNVERIFIED"
    original = content.get("status", "명시 판정 없음") if isinstance(content, dict) else "명시 판정 없음"
    label = str(path.relative_to(OUT)).replace(os.sep, "/")
    note = " · 현재 스토리보드의 원본 보고서" if path == latest else ""
    return (f'<article class="evidence-card">{badge(state)}<h3><a href="{url(path)}">{escape(label)}</a></h3>'
            f'<p>파일 내부 판정: {escape(original)}{escape(note)}</p></article>')


def html_document(report_path: Path | None, report: dict, frames: list[dict], missing: list[dict], rows: list[dict], models: list[dict], evidence: list[Path]) -> str:
    run_id = report.get("runId", "아직 캡처 실행 기록이 없습니다")
    run_state = status(report.get("status"))
    report_link = f'<a href="{url(report_path)}">원본 실행 보고서</a>' if report_path else "실행 보고서 대기"
    frame_data = [{"title": f["title"], "guide": f["guide"], "url": f["url"], "caption": frame_caption(f),
                   "evidence": f["record"].get("evidence", ""), "size": " × ".join(map(str, f["size"]))} for f in frames]
    serialized = json.dumps(frame_data, ensure_ascii=False).replace("<", "\\u003c")
    if frames:
        first = frames[0]
        storyboard = f'''<div class="viewer"><a id="full-image" href="{first['url']}"><img id="frame-image" src="{first['url']}" alt="{escape(first['title'])}"></a></div>
<div class="frame-info" aria-live="polite"><div><p class="eyebrow" id="frame-index">실제 캡처 1 / {len(frames)}</p><h3 id="frame-title">{escape(first['title'])}</h3><p id="frame-guide">{escape(first['guide'])}</p></div><div><p id="frame-caption">{escape(frame_caption(first))}</p><p class="muted" id="frame-evidence">{escape(first['record'].get('evidence', ''))}</p></div></div>
<div class="viewer-controls"><button id="previous" type="button">← 이전</button><input id="scrubber" type="range" min="0" max="{len(frames)-1}" value="0" aria-label="실제 캡처 선택"><button id="next" type="button">다음 →</button><button id="slideshow" type="button" aria-pressed="false">정지 화면 자동 넘김</button></div>
<div class="phase-buttons">{''.join(f'<button type="button" data-frame="{i}" aria-pressed="{str(i == 0).lower()}">{escape(f["title"])}</button>' for i, f in enumerate(frames))}</div>'''
    else:
        storyboard = '<div class="empty"><strong>실제 Unity 캡처를 기다리고 있습니다.</strong><p>최신 보고서가 기록한 PNG만 이곳에 표시합니다. 임의 모델 포즈나 재구성 이미지를 대신 넣지 않습니다.</p></div>'
    missing_markup = ('<p class="notice">미수집: ' + " · ".join(escape(f["title"]) for f in missing) + '</p>') if missing else ''
    before = OUT / "Before"
    comparison = image_card(before / "map_fold_1920x1080.png", "이전 · 여섯 세로 패널의 접힘", "변경 전 보존한 실제 화면. 이번 구현의 결과가 아닙니다.")
    comparison += image_card(before / "map_1920x1080.png", "이전 · 펼친 지도", "변경 전 지도 정보와 화면 구성의 비교 기준.")
    flat = next((frame for frame in frames if frame["id"] == "04_open_flat_100"), None)
    if flat:
        comparison += image_card(flat["path"], "현재 · 한 번씩 접은 종이 지도", "최신 실행의 실제 완전 펼침 캡처. 접힘의 중간 모습은 위의 정지 화면으로 확인하세요.")
    else:
        comparison += '<article class="card"><h3>현재 · 완전 펼침 캡처</h3>' + badge("UNVERIFIED") + '<p>최신 실행의 해당 PNG가 아직 없습니다.</p></article>'
    material = image_card(PAPER, "실제 사용 자산 · HanjiWorn.png", "종이 섬유·낡은 가장자리·십자 접힘 흔적을 담은 생성 텍스처. 최종 화면은 실제 Unity 캡처로 판단합니다.")
    model_markup = ''.join(f'<li><a href="{url(m["path"])}">{escape(m["path"].name)}</a> <span>{m["vertices"]:,} vertices · {m["faces"]:,} faces</span></li>' for m in models)
    if not model_markup:
        model_markup = '<li>OBJ 파일 미수집 · UNVERIFIED</li>'
    checks = ''.join(f'<tr><td>{badge(r["status"])}</td><td><code>{escape(r["id"])}</code></td><td>{escape(r["detail"])}</td></tr>' for r in rows)
    failures = [str(value) for value in report.get("failures", [])]
    if report.get("failure") and str(report["failure"]) not in failures:
        failures.insert(0, str(report["failure"]))
    failure_markup = '<div class="failure"><h3>실행 보고서의 실패 기록</h3><ul>' + ''.join(f'<li>{escape(f)}</li>' for f in failures) + '</ul></div>' if failures else ''
    source_links = ' · '.join(f'<a href="{url(SOURCE / name)}">{escape(name)}</a>' for name in ["WorldMapPaperGeometry.cs", "WorldMapPaperGraphic.cs", "WorldMapPresenter.cs"] if (SOURCE / name).is_file())
    source_doc = OUT / "SOURCE.md"
    if source_doc.is_file():
        source_links += f' · <a href="{url(source_doc)}">SOURCE.md · 자산 출처와 구현 근거</a>'
    return f'''<!doctype html>
<html lang="ko"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>오행부 · 접힌 종이 지도 검토</title>
<style>
:root{{--ink:#ecede6;--muted:#b3b7ab;--bg:#151b19;--panel:#1e2722;--line:#3a453c;--accent:#d5b775;--pass:#a6d6b6;--fail:#ffa899;--unknown:#e6d49a}}
*{{box-sizing:border-box}}html{{scroll-behavior:smooth}}body{{margin:0;background:var(--bg);color:var(--ink);font-family:"Malgun Gothic","Apple SD Gothic Neo",sans-serif;font-size:15px;line-height:1.7}}a{{color:var(--accent);text-underline-offset:4px}}a:hover{{color:#fff0c8}}button,input{{font:inherit}}button{{cursor:pointer;border:1px solid var(--line);background:#283129;color:var(--ink);padding:10px 15px;border-radius:6px}}button:hover,button[aria-pressed=true]{{border-color:var(--accent);background:#3a3b2c}}button:focus-visible,a:focus-visible,input:focus-visible{{outline:2px solid #ffe4aa;outline-offset:4px}}.shell{{max-width:1540px;margin:auto;padding:42px 36px}}header{{display:grid;grid-template-columns:1.25fr 1fr;gap:50px;padding:24px 0 36px;border-bottom:1px solid var(--line)}}h1{{font-size:clamp(31px,4vw,56px);font-weight:500;line-height:1.2;letter-spacing:-2px;margin:10px 0 22px}}h2{{font-size:27px;font-weight:500;margin:0 0 14px}}h3{{font-size:18px;font-weight:500;margin:0 0 8px}}p{{margin:8px 0 16px}}.eyebrow{{color:var(--accent);font-size:11px;letter-spacing:2px;text-transform:uppercase;margin:0 0 8px}}.lead{{max-width:740px;color:var(--muted);font-size:17px}}.summary{{align-self:center;border-left:2px solid var(--accent);padding-left:24px}}.summary p{{color:var(--muted)}}.muted{{color:var(--muted);font-size:13px}}.badge{{display:inline-block;padding:2px 8px;border:1px solid currentColor;border-radius:3px;font-size:11px;letter-spacing:1px;white-space:nowrap}}.pass{{color:var(--pass)}}.fail{{color:var(--fail)}}.unverified{{color:var(--unknown)}}nav{{display:flex;gap:24px;flex-wrap:wrap;padding:18px 0;border-bottom:1px solid var(--line)}}nav a{{font-size:13px;text-decoration:none}}section{{padding:42px 0;border-bottom:1px solid var(--line)}}.section-head{{display:flex;gap:24px;align-items:end;justify-content:space-between;margin-bottom:20px}}.section-head p{{max-width:760px;color:var(--muted)}}.viewer{{border:1px solid var(--line);background:#0d100e;line-height:0}}.viewer img{{width:100%;height:auto;display:block}}.frame-info{{display:grid;grid-template-columns:1fr 1.25fr;gap:28px;background:var(--panel);padding:22px 24px}}.frame-info p{{margin-bottom:5px}}.frame-info #frame-caption{{font-size:13px}}.viewer-controls{{display:flex;gap:12px;align-items:center;padding:17px 0}}input[type=range]{{flex:1;min-width:80px;accent-color:var(--accent)}}.phase-buttons{{display:flex;gap:8px;flex-wrap:wrap}}.phase-buttons button{{font-size:12px;padding:8px 12px}}.notice{{padding:14px 18px;border-left:2px solid var(--unknown);background:#292b21;color:var(--unknown);font-size:13px}}.grid{{display:grid;grid-template-columns:repeat(3,1fr);gap:20px}}.card{{margin:0;background:var(--panel);border:1px solid var(--line);min-width:0}}.card img{{display:block;width:100%;height:auto}}.card figcaption{{padding:17px 20px}}.card>h3,.card>p,.card>.badge{{margin:20px}}.card p{{font-size:13px;color:var(--muted)}}.material-grid{{display:grid;grid-template-columns:1fr 1.4fr;gap:28px}}.material-grid .card img{{max-height:640px;object-fit:contain;background:repeating-conic-gradient(#343b31 0 25%,#3d4539 0 50%) 0/22px 22px}}.detail{{padding:24px 28px;background:var(--panel);border:1px solid var(--line)}}.detail p,.detail li{{color:var(--muted)}}.detail ul{{padding-left:22px}}.models{{padding-left:20px}}.models li{{margin:7px 0}}.models span{{color:var(--muted);font-size:12px;margin-left:12px}}.table-wrap{{overflow-x:auto}}table{{width:100%;border-collapse:collapse;font-size:13px}}td,th{{padding:14px 13px;border-bottom:1px solid var(--line);text-align:left;vertical-align:top}}th{{color:var(--accent);font-size:11px;font-weight:400;letter-spacing:1px}}td:nth-child(2){{width:28%;overflow-wrap:anywhere}}td:nth-child(3){{color:var(--muted)}}code{{font-family:Consolas,monospace;font-size:12px}}.evidence-grid{{display:grid;grid-template-columns:repeat(2,1fr);gap:14px}}.evidence-card{{border:1px solid var(--line);padding:20px;background:var(--panel);overflow-wrap:anywhere}}.evidence-card h3{{font-size:13px;margin-top:10px}}.evidence-card p{{font-size:12px;color:var(--muted);margin:0}}.failure{{margin:20px 0;padding:22px;border:1px solid #754d43;background:#30211e;color:var(--fail)}}.empty{{padding:70px 30px;text-align:center;background:var(--panel);border:1px dashed var(--line)}}.empty p{{color:var(--muted)}}.judgment{{columns:2;column-gap:42px;padding-left:22px}}.judgment li{{break-inside:avoid;padding:5px 0 10px;color:var(--muted)}}footer{{padding:26px 0;color:var(--muted);font-size:12px}}@media(max-width:900px){{.shell{{padding:24px 16px}}header,.frame-info,.material-grid{{grid-template-columns:1fr;gap:20px}}.grid{{grid-template-columns:1fr}}.evidence-grid{{grid-template-columns:1fr}}.section-head{{display:block}}.viewer-controls{{flex-wrap:wrap}}.judgment{{columns:1}}}}@media(prefers-reduced-motion:reduce){{html{{scroll-behavior:auto}}}}
</style></head><body><div class="shell">
<header><div><p class="eyebrow">OHEANGBU / PAPER MAP REVIEW</p><h1>접힌 종이를<br>펼치는 지도</h1><p class="lead">가로 한 번, 세로 한 번 접은 한 장의 종이. 실제 Unity 화면을 단계별로 넘기며 펼침의 형태와 질감을 판단하는 검토 자료입니다.</p></div><aside class="summary"><p>최신 실행의 기술 판정 {badge(run_state)}</p><p>사람의 시각 검토 {badge('UNVERIFIED')}</p><p class="muted">실행: {escape(run_id)}<br>파일 내부 상태: {escape(report.get('status', '미기록'))}<br>{report_link} · <a href="REPORT.md">기술 보고서</a></p></aside></header>
<nav><a href="#storyboard">실제 펼침 화면</a><a href="#comparison">이전 화면 비교</a><a href="#paper">종이·3D 모델</a><a href="#controls">조작·연동 범위</a><a href="#technical">기술 검사</a><a href="#judgment">시각 검토 항목</a><a href="#builds">빌드 후보</a><a href="#evidence">근거 파일</a></nav>
<main><section id="storyboard"><div class="section-head"><div><p class="eyebrow">ACTUAL UNITY STILLS</p><h2>접힘의 각 단계를 확인하세요</h2></div><p>각 이미지는 새로운 실제 시간 전환에서 따로 촬영한 정지 화면입니다. 연속 촬영 영상이 아닙니다. 캡처 요청 시점과 다음 프레임의 진행도를 함께 기록합니다.</p></div>{storyboard}{missing_markup}</section>
<section id="comparison"><p class="eyebrow">BEFORE / CURRENT</p><h2>기존 여섯 패널과 비교</h2><p class="lead">보존한 이전 화면과 최신 실제 캡처를 나란히 봅니다. 이미지 내부에 설명이나 새 요소를 합성하지 않았습니다.</p><div class="grid">{comparison}</div></section>
<section id="paper"><p class="eyebrow">MATERIAL / GEOMETRY</p><h2>종이 자산과 실제 3D 표면</h2><div class="material-grid">{material}<div class="detail"><h3>두 개의 직교 접힘이 연결된 한 장</h3><p>40 × 32 격자의 1,353개 3D 위치와 표면 법선을 평가합니다. 가로로 접힌 두 겹을 바깥 세로 접힘이 함께 감싸며, 유한한 곡률의 접힌 선과 작은 주름·가장자리 굴곡을 유지합니다.</p><p>이 3D 표면을 원근 투영하여 uGUI에 그립니다. 앞·뒷면용 UI 정점은 2,706개이며, 보이는 2,560개 삼각형을 깊이 순서로 제출합니다. 이 종이 렌더러에는 추가 Camera나 RenderTexture가 필요하지 않습니다.</p><p>OBJ는 표면 형상을 살펴보는 자료입니다. 실제 게임에서 보인 결과는 위 Unity PNG로 확인합니다.</p><ul class="models">{model_markup}</ul><p class="muted">{source_links}</p></div></div></section>
<section id="controls"><p class="eyebrow">IMPLEMENTED CONTRACT / REGRESSION SCOPE</p><h2>조작과 기존 지도 정보</h2><div class="material-grid"><div class="detail"><h3>M · 지도 열기와 닫기</h3><p>구현상 열기 1.10초, 닫기 0.65초입니다. 월드가 일시정지되어도 unscaled time으로 종이가 움직입니다. 동작 줄이기 설정은 접힘 애니메이션을 건너뜁니다.</p><p>이 캡처의 동작 줄이기 닫기는 진단용 presenter API를 확인합니다. 저장된 옵션 화면에서 닫기까지 이어지는 흐름이나 실제 M 키 입력을 대신 검증하지 않습니다.</p></div><div class="detail"><h3>기존 데이터와 UI를 이어 쓰는 범위</h3><p>기존 지도 좌표·탐험 안개·현재 위치·마커·사용자 핀·내부 상세 경로는 지도 presenter에 연결됩니다. 펼침 중 종이 표면에 잉크를 합성하고, 완전 펼침 후 표식과 조작 레이어를 표시합니다.</p><p>이 설명은 구현 범위입니다. 각 기능의 회귀 검증 성공 여부는 아래 근거 파일에 명시된 검사만 따릅니다.</p></div></div></section>
<section id="technical"><p class="eyebrow">EXPLICIT MACHINE EVIDENCE</p><h2>기술 검사 결과</h2><p class="lead">PASS와 FAIL은 최신 실행 보고서의 명시 판정을 옮깁니다. 미수집·진행 중·명시 판정 없음은 UNVERIFIED로 표시하며, 기술 통과를 시각 승인으로 바꾸지 않습니다.</p>{failure_markup}<div class="table-wrap"><table><thead><tr><th>STATUS</th><th>CHECK</th><th>RECORDED DETAIL</th></tr></thead><tbody>{checks}</tbody></table></div><p class="muted">{escape(report.get('limitations', '현재 실행의 성능·입력 검증 범위가 아직 기록되지 않았습니다.'))}</p></section>
<section id="judgment"><p class="eyebrow">HUMAN REVIEW PENDING</p><h2>화면을 보고 판단할 항목 {badge('UNVERIFIED')}</h2><ul class="judgment">{''.join('<li>'+escape(item)+'</li>' for item in VISUAL_CHECKS)}</ul><p class="muted">자동 검사는 종이의 촉감이나 연출 만족도를 판정하지 않습니다. 이 페이지를 생성했다는 사실은 사람의 승인을 뜻하지 않습니다.</p></section>
<section id="builds"><p class="eyebrow">BUILD CANDIDATE / EXPLICIT RESULTS</p><h2>실행할 빌드와 검사 기록</h2>{candidate_html()}</section>
<section id="evidence"><p class="eyebrow">LOCAL EVIDENCE LEDGER</p><h2>원본 검사·빌드 자료</h2><p class="lead">Validation 폴더의 기존 파일을 모두 연결합니다. 이전 실행과 최신 실행은 경로로 구분되며, 빌드 성공은 해당 보고서의 명시 기록이 있을 때만 읽을 수 있습니다.</p><div class="evidence-grid">{''.join(evidence_card(path, report_path) for path in evidence) or '<p>검사 파일이 아직 없습니다. UNVERIFIED</p>'}</div></section></main>
<footer>오행부 종이 지도 · 오프라인 검토 문서 · 외부 CDN 없음 · 실제 캡처를 수정하거나 동영상으로 합성하지 않음<br>생성 시각 {escape(datetime.now(timezone.utc).isoformat())}</footer>
</div><script id="frame-data" type="application/json">{serialized}</script><script>
(()=>{{const frames=JSON.parse(document.getElementById('frame-data').textContent);if(!frames.length)return;let current=0,timer=null;const byId=id=>document.getElementById(id);const choices=[...document.querySelectorAll('[data-frame]')];function show(index){{current=(index+frames.length)%frames.length;const f=frames[current];byId('frame-image').src=f.url;byId('frame-image').alt=f.title;byId('full-image').href=f.url;byId('frame-title').textContent=f.title;byId('frame-guide').textContent=f.guide;byId('frame-caption').textContent=f.caption;byId('frame-evidence').textContent=f.evidence;byId('frame-index').textContent=`실제 캡처 ${{current+1}} / ${{frames.length}} · ${{f.size}}`;byId('scrubber').value=current;choices.forEach((button,i)=>button.setAttribute('aria-pressed',String(i===current)));}}function stop(){{if(timer)clearInterval(timer);timer=null;byId('slideshow').textContent='정지 화면 자동 넘김';byId('slideshow').setAttribute('aria-pressed','false');}}byId('previous').addEventListener('click',()=>{{stop();show(current-1);}});byId('next').addEventListener('click',()=>{{stop();show(current+1);}});byId('scrubber').addEventListener('input',event=>{{stop();show(Number(event.target.value));}});choices.forEach((button,i)=>button.addEventListener('click',()=>{{stop();show(i);}}));byId('slideshow').addEventListener('click',()=>{{if(timer){{stop();return;}}timer=setInterval(()=>show(current+1),1800);byId('slideshow').textContent='자동 넘김 멈추기';byId('slideshow').setAttribute('aria-pressed','true');}});document.addEventListener('visibilitychange',()=>{{if(document.hidden)stop();}});show(0);}})();
</script></body></html>'''


def markdown_report(report_path: Path | None, report: dict, frames: list[dict], missing: list[dict], rows: list[dict], models: list[dict], evidence: list[Path]) -> str:
    lines = ["# 종이 지도 검토 근거", "", f"생성 시각: {datetime.now(timezone.utc).isoformat()}", "",
             f"최신 실행: `{report.get('runId', '미수집')}` · 기술 상태: **{status(report.get('status'))}** · 원본 상태: `{report.get('status', '미기록')}`", "",
             "사람의 시각 검토: **UNVERIFIED**. 실제 펼침의 형태·가림·질감을 사람이 판단해야 하며 자동 통과로 바꾸지 않습니다.", "",
             "[오프라인 화면 검토](REVIEW.html)"]
    if report_path:
        lines += ["", f"[최신 실행 원본 JSON]({url(report_path)})"]
    lines += ["", "## 증거 범위", "", "실제 Unity Game View의 정지 화면을 표시합니다. 각 목표는 새로운 실제 unscaled-time 전환에서 따로 캡처했으며, 연속 촬영 영상이 아닙니다. 이미지를 합성하거나 임의의 모델 포즈로 대체하지 않았습니다.", "",
              str(report.get("scope", "실행 범위 아직 미기록.")), "", str(report.get("limitations", "입력·성능 검증 범위 아직 미기록.")), "",
              "## 실제 캡처", "", "| 단계 | 목표 / 발행 / 다음 프레임 | 전환 경과 | 원본 |", "|---|---|---|---|"]
    for frame in frames:
        record = frame["record"]
        lines.append(f"| {frame['title']} | {format_number(record.get('requestedProgress'))} / {format_number(record.get('progressAtIssue'))} / {format_number(record.get('progressAfterIssue'))} | {format_number(record.get('transitionElapsedSeconds'))} s | [PNG]({frame['url']}) |")
    for frame in missing:
        lines.append(f"| {frame['title']} | UNVERIFIED | 미수집 | 실제 기록 PNG 없음 |")
    lines += ["", "## 변경 전 화면", ""]
    for name in ("map_fold_1920x1080.png", "map_1920x1080.png"):
        path = OUT / "Before" / name
        if path.is_file():
            lines.append(f"- [이전 여섯 패널 기준: {name}]({url(path)})")
    lines += ["", "## 구현과 연동 범위", "", "가로 한 번, 세로 한 번 접은 연속 3D 종이를 40 × 32 격자(1,353 표면 정점)로 평가합니다. 곡률을 가진 접힘, 네 겹의 분리, 주름과 가장자리 굴곡을 유지합니다. 앞·뒷면 2,706 UI 정점과 2,560개 보이는 삼각형을 깊이 순서로 uGUI에 투영하며, 이 종이 렌더러는 추가 Camera/RenderTexture를 사용하지 않습니다.", "",
              "구현상 조작은 M, 열기 1.10초, 닫기 0.65초이며 unscaled time을 사용합니다. 동작 줄이기는 즉시 전환합니다. 이 캡처의 동작 줄이기 닫기는 진단 presenter API 검사이며, 저장된 옵션과 실제 M 키 입력의 연속 흐름을 검증하지 않습니다.", "",
              "기존 지도 좌표, 탐험 안개, 현재 위치, 마커, 사용자 핀, 내부 상세 경로를 이어 쓰도록 구현했습니다. 위 설명은 구현 범위이며, 기능별 회귀 성공은 아래 명시 검사 근거만 따릅니다."]
    if PAPER.is_file():
        lines += ["", f"[실제 생성 종이 텍스처 HanjiWorn.png]({url(PAPER)}) · SHA-256 `{hashlib.sha256(PAPER.read_bytes()).hexdigest()}`"]
    if (OUT / "SOURCE.md").is_file():
        lines += ["", "[SOURCE.md · 자산 출처와 구현 근거](SOURCE.md)"]
    lines += ["", "## 모델", ""]
    lines += [f"- [{m['path'].name}]({url(m['path'])}): {m['vertices']:,} vertices / {m['faces']:,} faces." for m in models] or ["OBJ 자료 미수집 · UNVERIFIED."]
    lines += ["", "## 명시 기술 검사", "", "| 상태 | 검사 | 기록된 세부 내용 |", "|---|---|---|"]
    for row in rows:
        detail = str(row["detail"]).replace("|", "\\|").replace("\n", " ")
        lines.append(f"| {row['status']} | {row['id']} | {detail} |")
    if report.get("failures") or report.get("failure"):
        lines += ["", "실행 보고서 실패 기록:", ""]
        lines += ["- " + str(failure).replace("\n", " ") for failure in report.get("failures", [])]
        if report.get("failure"):
            lines.append("- " + str(report["failure"]).replace("\n", " "))
    lines += ["", "## 사람의 시각 검토 — UNVERIFIED", ""] + ["- [ ] " + item for item in VISUAL_CHECKS]
    lines += ["", "## 원본 검사·빌드 파일", "", "PASS/FAIL은 파일 내부의 명시 판정만 표시합니다. RUNNING, 판정 없음, 읽을 수 없는 파일은 UNVERIFIED입니다. 이전 실행의 성공을 최신 실행의 성공으로 사용하지 않습니다.", ""]
    for path in evidence:
        content = load_json(path) if path.suffix.lower() == ".json" else None
        recorded = content.get("status") if isinstance(content, dict) else None
        lines.append(f"- **{status(recorded)}** [{path.relative_to(OUT).as_posix()}]({url(path)}) — 원본 상태: `{recorded or '명시 판정 없음'}`")
    if not evidence:
        lines.append("검사 파일 미수집 · UNVERIFIED.")
    lines += candidate_markdown()
    lines += ["", "## 재생성", "", "저장소 루트에서 `python Tools/PlaytestFeedback/build_paper_map_review.py`를 실행하면 기존 캡처·모델·검사 파일을 다시 읽습니다. Unity 프로젝트나 원본 이미지를 변경하지 않습니다.", ""]
    return "\n".join(lines)


def verify_links(document: str, report: str) -> None:
    targets = re.findall(r'(?:href|src)=["\']([^"\']+)', document)
    targets += re.findall(r"\[[^]]+\]\(([^)]+)\)", report)
    missing = []
    for target in targets:
        parsed = urlparse(target)
        if target.startswith("#") or parsed.scheme:
            continue
        if not (OUT / unquote(parsed.path)).resolve().exists():
            missing.append(target)
    if missing:
        raise RuntimeError("Missing review links: " + ", ".join(sorted(set(missing))))


def build() -> tuple[Path, Path]:
    OUT.mkdir(parents=True, exist_ok=True)
    report_path, report = newest_report()
    frames, missing = collect_frames(report)
    rows = check_rows(report, frames, missing)
    models, evidence = obj_models(), evidence_files()
    document = html_document(report_path, report, frames, missing, rows, models, evidence)
    markdown = markdown_report(report_path, report, frames, missing, rows, models, evidence)
    review_file, report_file = OUT / "REVIEW.html", OUT / "REPORT.md"
    review_file.write_text(document, encoding="utf-8")
    report_file.write_text(markdown, encoding="utf-8")
    verify_links(document, markdown)
    return review_file, report_file


if __name__ == "__main__":
    for generated in build():
        print("Wrote " + generated.relative_to(ROOT).as_posix())
