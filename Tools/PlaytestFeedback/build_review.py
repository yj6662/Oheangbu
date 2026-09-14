"""Build the offline playtest HUD/SFX review page and technical report."""

from __future__ import annotations

import hashlib
import html
import json
import os
import re
from pathlib import Path
from urllib.parse import quote, unquote


ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "Art/UIAudio/PlaytestFeedback"
REQUESTS = OUT / "requests"
SCREENSHOTS = OUT / "Screenshots"
VALIDATION = OUT / "Validation"

EVIDENCE_CANDIDATES = {
    "audio": [
        VALIDATION / "runtime_audio.json",
        VALIDATION / "audio_runtime_review.json",
        OUT / "runtime_audio.json",
    ],
    "hud_live": [
        VALIDATION / "hud_live_runtime_review.json",
    ],
    "hud_states": [
        VALIDATION / "hud_states_runtime_review.json",
    ],
    "summon": [
        VALIDATION / "runtime_summon.json",
        VALIDATION / "summon_runtime_review.json",
        ROOT / "Art/World/WorldMacro/Playtest/SummonCast/runtime_review.json",
    ],
}

UI_LABELS = {
    "hp_stroke": ("체력 먹 획", "마른 붓결을 남긴 수평 체력 표시"),
    "ink_bottle": ("먹물병", "현재 먹의 액면을 담는 비어 있는 외곽선"),
    "lock_ring": ("일획 락온 원", "락온·그로기·받아치기 상태를 받는 절제된 원"),
    "prompt_paper": ("상호작용 한지", "한국어 안내 문구 뒤에 놓이는 작은 한지 조각"),
}

AUDIO_GROUPS = [
    ("오행 실제 시전", ["cast_wood", "cast_fire", "cast_earth", "cast_metal", "cast_water"]),
    ("작도·전투 공통 피드백", ["brush_stroke", "impact", "player_hit", "parry", "harvest"]),
    ("상호작용·휴식", ["interact", "rest"]),
    ("정적 소환 외형", ["summon_appear", "summon_release"]),
]

AUDIO_LABELS = {
    "cast_wood": ("목 시전", "대나무 휨, 마른 나무, 낮은 거문고 현"),
    "cast_fire": ("화 시전", "짧은 점화와 마른 불꽃"),
    "cast_earth": ("토 시전", "묵직한 돌과 흙의 짧은 결속"),
    "cast_metal": ("금 시전", "절제된 쇠 울림과 마찰"),
    "cast_water": ("수 시전", "작은 물살과 젖은 먹의 번짐"),
    "brush_stroke": ("붓 획", "한지 위 마른 붓과 먹의 접촉"),
    "impact": ("적중", "짧고 가까운 충격"),
    "player_hit": ("플레이어 피격", "과장하지 않은 신체 피격"),
    "parry": ("받아치기", "짧은 금속·목재 접점"),
    "harvest": ("먹 회수", "먹과 섬유가 모이는 짧은 소리"),
    "interact": ("상호작용", "작은 목재·한지 확인음"),
    "rest": ("휴식", "낮은 현과 실내의 짧은 안정감"),
    "summon_appear": ("소환 등장", "정적 외형이 나타나는 짧은 기척"),
    "summon_release": ("소환 소멸", "한지와 잎이 흩어지는 짧은 마감"),
}


def load_json(path: Path):
    return json.loads(path.read_text(encoding="utf-8"))


def rel_url(path: Path) -> str:
    relative = os.path.relpath(path, OUT).replace(os.sep, "/")
    return quote(relative, safe="/._-~")


def rel_md(path: Path) -> str:
    return os.path.relpath(path, OUT).replace(os.sep, "/")


def short_hash(value: str) -> str:
    return value[:12]


def fmt(value: float, digits: int = 2) -> str:
    return f"{value:.{digits}f}"


def file_hash(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def request_records(prefix: str) -> list[dict]:
    return [load_json(path) for path in sorted(REQUESTS.glob(f"{prefix}_*.json"))]


def discover_validation() -> list[Path]:
    found: set[Path] = set()
    if VALIDATION.exists():
        found.update(p for p in VALIDATION.rglob("*") if p.is_file())
    for pattern in ("*validation*.json", "*runtime*.json", "*audit*.json"):
        found.update(OUT.glob(pattern))
    return sorted(found)


def check_passed(item: dict) -> bool:
    if isinstance(item.get("pass"), bool):
        return item["pass"]
    return str(item.get("status", "")).strip().upper() in {"PASS", "PASSED"}


def normalize_status(payload: dict | None) -> str:
    if not payload:
        return "UNVERIFIED"
    raw = str(payload.get("status", "")).strip().upper()
    failures = payload.get("failures")
    if isinstance(failures, list) and failures:
        return "FINDINGS"
    if isinstance(failures, str) and failures.strip():
        return "FINDINGS"
    checks = payload.get("checks")
    if isinstance(checks, list) and checks and any(
        isinstance(item, dict) and not check_passed(item) for item in checks
    ):
        return "FINDINGS"
    if raw.startswith("PASS") or raw in {"COMPLETE", "SUCCESS", "SUCCEEDED"}:
        return "PASS"
    if raw.startswith("FAIL") or raw.startswith("FINDING") or raw == "ERROR":
        return "FINDINGS"
    if isinstance(checks, list) and checks:
        statuses = [check_passed(item) for item in checks if isinstance(item, dict)]
        if statuses and all(statuses):
            return "PASS"
        if statuses:
            return "FINDINGS"
    return raw or "UNVERIFIED"


def evidence_record(kind: str, label: str, boundary: str) -> dict:
    candidates = EVIDENCE_CANDIDATES[kind]
    existing = [candidate for candidate in candidates if candidate.exists()]
    path = max(existing, key=lambda candidate: candidate.stat().st_mtime_ns) if existing else None
    payload = load_json(path) if path else None
    checks = payload.get("checks", []) if isinstance(payload, dict) else []
    passed = sum(check_passed(item) for item in checks if isinstance(item, dict))
    findings = [
        item for item in checks if isinstance(item, dict)
        and not check_passed(item)
    ]
    if isinstance(payload, dict):
        report_failures = payload.get("failures", [])
        if isinstance(report_failures, str) and report_failures.strip():
            report_failures = [report_failures]
        if isinstance(report_failures, list):
            findings.extend(
                {"name": "report_failure", "detail": str(item)}
                for item in report_failures if str(item).strip()
            )
    return {
        "kind": kind,
        "label": label,
        "boundary": boundary,
        "path": path,
        "payload": payload,
        "status": normalize_status(payload),
        "reported_status": str(payload.get("status", "")).strip() if isinstance(payload, dict) else "",
        "reported_scope": str(payload.get("scope", "")).strip() if isinstance(payload, dict) else "",
        "passed": passed,
        "check_count": len(checks),
        "findings": findings,
    }


def evidence_summary(record: dict) -> str:
    if record["path"] is None:
        return "보고서 없음 · 미검증"
    if record["check_count"]:
        reported = f"보고서 status={record['reported_status']} · " if record["reported_status"] else ""
        return reported + f"검사 {record['check_count']}개 중 PASS {record['passed']}개"
    detail = str(record["payload"].get("detail", "보고서 상태 필드 기준"))
    return detail


def status_class(status: str) -> str:
    return "pass" if status == "PASS" else "findings" if status == "FINDINGS" else "unverified"


def screenshot_boundary(name: str) -> str:
    if name.startswith("hud_live_"):
        return "저장된 현재 서비스 상태를 바꾸지 않은 런타임 캡처 · 수동 플레이 장면이나 사용자 시인성 승인 증거가 아님"
    if name.startswith("hud_states_"):
        return "서비스 값을 통제한 UI 진단 상태 이미지 · 자연 플레이 입력 장면 증거가 아님"
    return "런타임 검토 이미지 · 자연 플레이 여부는 연결된 보고서 범위를 따름"


def validation_role(path: Path, evidence: list[dict]) -> str:
    if any(part.lower() == "attempts" for part in path.parts):
        return "과거 시도 기록 · 현재 판정 아님"
    for record in evidence:
        if record["path"] is not None and path.resolve() == record["path"].resolve():
            return "현재 판정 · " + record["label"]
    return "보조 검증 자료"


def build() -> tuple[str, str, list[Path]]:
    ui = load_json(OUT / "ui_assets.json")
    audio = load_json(OUT / "audio_analysis.json")
    if {entry["name"] for entry in ui} != set(UI_LABELS):
        raise RuntimeError("UI manifest must contain the four specified HUD assets")
    expected_audio = {name for _, names in AUDIO_GROUPS for name in names}
    if {entry["name"] for entry in audio} != expected_audio or len(audio) != 14:
        raise RuntimeError("Audio manifest must contain the fourteen specified SFX clips")

    ui_by_name = {entry["name"]: entry for entry in ui}
    audio_by_name = {entry["name"]: entry for entry in audio}
    ui_requests = request_records("ui")
    sfx_requests = request_records("sfx")
    rejected = [load_json(path) for path in sorted((REQUESTS / "rejected").glob("*.json"))]
    recraft_attempts = len(ui_requests) + len(rejected)

    recraft_actual = sum(float(r["actual_usage"]) for r in ui_requests if r.get("actual_usage") is not None)
    eleven_cost = 0.0
    eleven_fallbacks: list[str] = []
    for record in sfx_requests:
        actual = record.get("actual_usage")
        if actual is None:
            actual = record.get("response_headers", {}).get("character-cost")
            if actual is not None:
                eleven_fallbacks.append(record["name"])
        if actual is None:
            raise RuntimeError(f"No returned character cost for {record['name']}")
        eleven_cost += float(actual)
    requested_seconds = sum(float(r["request"]["duration_seconds"]) for r in sfx_requests)
    prepared_seconds = sum(float(a["duration"]) for a in audio)
    first_parry = OUT / "Originals/sfx/parry.mp3"
    first_parry_request = REQUESTS / "sfx_parry.json"

    screenshots = sorted(SCREENSHOTS.glob("*.png")) if SCREENSHOTS.exists() else []
    validations = discover_validation()
    evidence = [
        evidence_record(
            "audio", "효과음 서비스 이벤트",
            "주입된 인식 완료 글자와 통제된 전투 서비스 경로 검증이다. 직접 청음·실제 필기·사용자 기기 출력 증거가 아니다.",
        ),
        evidence_record(
            "hud_live", "HUD 현재 상태 렌더",
            "저장된 현재 서비스 상태를 바꾸지 않고 캡처한 런타임 렌더다. 수동 이동·전투 장면이나 사용자 시인성 승인 증거가 아니다.",
        ),
        evidence_record(
            "hud_states", "HUD 주입 상태 진단",
            "HP·먹·락온·그로기·받아치기 값을 통제해 네 표시와 저체력 상태·복원을 확인한 진단이다. 자연 플레이 장면 증거가 아니다.",
        ),
        evidence_record(
            "summon", "5종 소환 등장·소멸",
            "주입된 인식 완료 글자를 실제 Resolver/먹 승인/표현 수명 경로에 전달한 검증이다. 실제 펜 입력·수동 필기 증거가 아니다.",
        ),
    ]
    existing_still = ROOT / "Art/World/WorldMacro/Playtest/VisualCorridor/mine_exit.png"
    spec = ROOT / "Docs/Specs/SPEC-PLAYTEST-UI-AUDIO.md"
    macro_spec = ROOT / "Docs/Specs/SPEC-WORLD-MACRO-PLAYTEST.md"
    bible_index = ROOT / "Docs/BIBLE_INDEX.md"
    report_path = OUT / "REPORT.md"

    link_targets: list[Path] = [spec, macro_spec, bible_index, report_path]
    link_targets.extend([first_parry, first_parry_request])
    link_targets.extend(record["path"] for record in evidence if record["path"] is not None)
    if existing_still.exists():
        link_targets.append(existing_still)

    ui_cards = []
    for name in UI_LABELS:
        entry = ui_by_name[name]
        label, description = UI_LABELS[name]
        runtime = ROOT / entry["file"]
        source = ROOT / entry["source"]
        request = REQUESTS / f"ui_{name}.json"
        link_targets.extend([runtime, source, request])
        src = rel_url(runtime)
        ui_cards.append(f"""
        <article class="ui-card">
          <header><p class="eyebrow">HUD 자산</p><h3>{html.escape(label)}</h3><p>{html.escape(description)}</p></header>
          <div class="surfaces">
            <div><p class="surface-label">밝은 배경 · 먹색 표시</p><div class="surface light"><img loading="lazy" src="{src}" alt="{html.escape(label)} 밝은 배경 미리보기"></div></div>
            <div><p class="surface-label">어두운 배경 · 한지색 표시</p><div class="surface dark"><img loading="lazy" src="{src}" alt="{html.escape(label)} 어두운 배경 미리보기"></div></div>
          </div>
          <dl><div><dt>Unity PNG</dt><dd>{entry['size'][0]}×{entry['size'][1]}</dd></div><div><dt>투명 픽셀</dt><dd>{entry['transparent_pixels']:,}</dd></div><div><dt>SHA-256</dt><dd><code>{short_hash(entry['sha256'])}…</code></dd></div></dl>
          <p class="asset-links"><a href="{rel_url(runtime)}">Unity PNG</a><a href="{rel_url(source)}">Recraft SVG 원본</a><a href="{rel_url(request)}">요청 원장</a></p>
        </article>""")

    audio_sections = []
    for group_label, names in AUDIO_GROUPS:
        rows = []
        for name in names:
            entry = audio_by_name[name]
            label, description = AUDIO_LABELS[name]
            wav = ROOT / entry["file"]
            mp3 = ROOT / entry["source"]
            request_name = Path(entry["source"]).stem
            request = REQUESTS / f"sfx_{request_name}.json"
            link_targets.extend([wav, mp3, request])
            rows.append(f"""
            <article class="audio-row">
              <div class="audio-copy"><p class="eyebrow">{html.escape(group_label)}</p><h3>{html.escape(label)}</h3><p>{html.escape(description)}</p></div>
              <audio controls preload="metadata" src="{rel_url(wav)}">오디오 재생을 지원하지 않는 브라우저입니다.</audio>
              <dl><div><dt>길이</dt><dd>{fmt(entry['duration'])}초</dd></div><div><dt>RMS</dt><dd>{fmt(entry['rms_dbfs'], 1)} dBFS</dd></div><div><dt>피크</dt><dd>{fmt(entry['peak_dbfs'], 1)} dBFS</dd></div><div><dt>클리핑</dt><dd>{entry['clipped_samples']}</dd></div></dl>
              <p class="asset-links"><a href="{rel_url(wav)}">Unity WAV</a><a href="{rel_url(mp3)}">ElevenLabs MP3 원본</a><a href="{rel_url(request)}">요청 원장</a></p>
            </article>""")
        audio_sections.append(f'<section class="audio-group"><h2>{html.escape(group_label)}</h2>{"".join(rows)}</section>')

    if screenshots:
        shot_cards = []
        for shot in screenshots:
            link_targets.append(shot)
            shot_boundary = screenshot_boundary(shot.name)
            shot_cards.append(f'<figure><img loading="lazy" src="{rel_url(shot)}" alt="런타임 검토 이미지 {html.escape(shot.stem)}"><figcaption>{html.escape(shot.name)} · {html.escape(shot_boundary)} · 판정은 연결된 보고서 기준</figcaption></figure>')
        screenshot_html = '<div class="shot-grid">' + "".join(shot_cards) + "</div>"
    else:
        screenshot_html = '<p class="pending">아직 이 작업의 런타임 캡처가 없습니다. 생성 자산 준비 상태를 런타임 적용 확인으로 해석하지 않습니다.</p>'

    if validations:
        items = []
        for path in validations:
            link_targets.append(path)
            role = validation_role(path, evidence)
            items.append(f'<li><a href="{rel_url(path)}">{html.escape(path.name)}</a> · {html.escape(role)}</li>')
        validation_html = '<ul class="file-list">' + "".join(items) + "</ul>"
    else:
        validation_html = '<p class="pending">런타임 이벤트 연결·중복·정리 검증 보고서가 아직 없습니다.</p>'

    evidence_cards = []
    for record in evidence:
        status = record["status"]
        path = record["path"]
        findings = record["findings"]
        finding_html = ""
        if findings:
            finding_html = "<ul>" + "".join(
                "<li><strong>" + html.escape(str(item.get("name", "unnamed"))) + "</strong> · "
                + html.escape(str(item.get("detail", item.get("status", "finding")))) + "</li>"
                for item in findings
            ) + "</ul>"
        report_link = f'<a href="{rel_url(path)}">근거 보고서</a>' if path else "보고서 없음"
        evidence_cards.append(f"""
        <article class="evidence-card">
          <p class="eyebrow">런타임 근거</p><h3>{html.escape(record['label'])}</h3>
          <p><span class="status {status_class(status)}">{html.escape(status)}</span> {html.escape(evidence_summary(record))}</p>
          {f'<p><strong>보고서 범위</strong> · {html.escape(record["reported_scope"])}</p>' if record['reported_scope'] else ''}
          <p>{html.escape(record['boundary'])}</p>{finding_html}<p class="asset-links">{report_link}</p>
        </article>""")

    evidence_status = " · ".join(
        f"{record['label']} {record['reported_status'] or record['status']}" for record in evidence
    )
    evidence_by_kind = {record["kind"]: record for record in evidence}
    remaining_items = []
    if evidence_by_kind["audio"]["status"] != "PASS":
        remaining_items.append("효과음 서비스 이벤트 보고서의 미통과·미검증 항목을 바로잡고 재검증한다.")
    if evidence_by_kind["hud_live"]["status"] != "PASS":
        remaining_items.append("HUD가 저장된 현재 서비스 상태에서 렌더되는지 런타임에서 검증한다.")
    if evidence_by_kind["hud_states"]["status"] != "PASS":
        remaining_items.append("HUD 네 표시의 통제 상태 반영·저체력 표시·원상 복원을 런타임에서 검증한다.")
    if evidence_by_kind["summon"]["status"] != "PASS":
        remaining_items.append("5종 소환의 승인·등장·소멸 수명 연결을 런타임에서 검증한다.")
    remaining_items.extend([
        "사용자가 실제 필기로 인식·시전 흐름을 확인한다. 주입된 인식 완료 이벤트는 수동 필기 증거가 아니다.",
        "사용자 기기 출력으로 14개를 직접 청음해 한국적 인상, 오행 구분과 최종 음량을 판정한다.",
        "자연 플레이 화면에서 HUD 시인성과 중앙 시야 비침범을 확인한다. 진단 상태 이미지는 자연 플레이 장면이 아니다.",
    ])
    remaining_html = "".join(f"<li>{html.escape(item)}</li>" for item in remaining_items)

    still_html = ""
    if existing_still.exists():
        still_html = f"""
        <figure class="context-still">
          <img loading="lazy" src="{rel_url(existing_still)}" alt="기존 비주얼 코리도 폐광 정지 이미지">
          <figcaption>기존 VisualCorridor 정지 이미지 · UI 적용 전 맥락 참고 · 이번 작업에서 새로 촬영한 이미지가 아님</figcaption>
        </figure>"""

    build_html = ""
    build_markdown = ""
    package_path = VALIDATION / "package_report.json"
    if package_path.exists():
        package = load_json(package_path)
        archive = Path(package["archive"])
        if archive.exists() and package.get("crcVerified"):
            link_targets.append(archive)
            size_gb = package["bytes"] / 1_000_000_000
            build_html = f'<section class="section"><p class="eyebrow">WINDOWS PLAYTEST CANDIDATE</p><h2>UI·효과음 포함 빌드</h2><p><a href="{rel_url(archive)}">테스트 빌드 ZIP · {size_gb:.2f} GB</a></p><p class="muted">빌드 오류 0건. 별도 저장 슬롯에서 시작·저장·재실행을 확인했습니다. 숨김 실행은 렌더 성능이나 사용자 기기 청음 검증으로 취급하지 않습니다. 기존 후보 빌드는 보존했습니다.</p></section>'
            build_markdown = f'\n## UI·효과음 포함 빌드\n\n[Windows 테스트 빌드 ZIP]({rel_md(archive)}) · {size_gb:.2f} GB. 빌드 오류 0건, 기존 경고 581건. 별도 슬롯 시작·저장·재실행 PASS, 렌더 성능·기기 청음은 미검증. ZIP CRC 확인.\n'

    html_text = f"""<!doctype html>
<html lang="ko">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>오행부 · 플레이테스트 HUD와 효과음 검토</title>
  <style>
    :root{{--paper:#eee9dc;--paper-deep:#ddd4bf;--ink:#1d211f;--soft:#60645d;--line:#b8b09e;--pine:#354941;--clay:#875a43;--night:#202825}}
    *{{box-sizing:border-box}} html{{background:#cac2b0;color:var(--ink);font-family:"Malgun Gothic","Apple SD Gothic Neo",sans-serif;line-height:1.65}} body{{margin:0}}
    body:before{{content:"";position:fixed;inset:0;pointer-events:none;opacity:.22;background-image:radial-gradient(#4d4a4218 .7px,transparent .8px);background-size:7px 7px}}
    main{{position:relative;width:min(1180px,calc(100% - 32px));margin:24px auto 64px;background:var(--paper);box-shadow:0 18px 60px #2d2b2538;border:1px solid #aaa18e}}
    .hero{{padding:clamp(34px,7vw,88px);border-bottom:1px solid var(--line);background:linear-gradient(130deg,#f4f0e5 0 70%,#d8d0bd 70%)}}
    .hero h1{{font-family:Batang,"Nanum Myeongjo",serif;font-weight:500;font-size:clamp(32px,5vw,64px);line-height:1.18;margin:.2em 0}}
    .hero .lede{{font-size:18px;max-width:760px;color:#424740}} .stamp{{display:inline-block;color:var(--clay);border:2px solid var(--clay);padding:3px 9px;transform:rotate(-2deg);font-weight:700}}
    .notice{{margin:28px clamp(20px,5vw,64px) 0;padding:18px 22px;border-left:4px solid var(--clay);background:#e4dccb}}
    .section{{padding:48px clamp(20px,5vw,64px);border-bottom:1px solid var(--line)}} .section>h2,.audio-group>h2{{font-family:Batang,serif;font-weight:500;font-size:30px;margin:0 0 20px}}
    .eyebrow{{margin:0;color:var(--clay);font-size:12px;font-weight:700;letter-spacing:.12em}} .muted{{color:var(--soft)}}
    .usage{{display:grid;grid-template-columns:repeat(3,1fr);gap:1px;background:var(--line);border:1px solid var(--line);margin-top:26px}} .usage div{{background:#f4f0e5;padding:22px}} .usage strong{{display:block;font-size:30px;font-family:Batang,serif}}
    .ui-grid{{display:grid;grid-template-columns:repeat(2,1fr);gap:22px}} .ui-card{{background:#f5f1e7;border:1px solid var(--line);padding:22px}} .ui-card h3,.audio-row h3{{font-family:Batang,serif;font-size:23px;font-weight:500;margin:2px 0 4px}}
    .surfaces{{display:grid;grid-template-columns:1fr 1fr;gap:12px;margin:18px 0}} .surface-label{{font-size:12px;color:var(--soft);margin:0 0 6px}}
    .surface{{height:180px;display:flex;align-items:center;justify-content:center;padding:22px;border:1px solid #bdb5a5}} .surface img{{max-width:100%;max-height:100%;object-fit:contain}} .surface.light{{background:#faf7ed}} .surface.light img{{filter:brightness(0) saturate(100%)}} .surface.dark{{background:var(--night)}} .surface.dark img{{filter:brightness(0) invert(92%) sepia(13%) saturate(250%) hue-rotate(5deg)}}
    dl{{display:flex;gap:18px;flex-wrap:wrap;margin:12px 0}} dl div{{min-width:90px}} dt{{font-size:11px;color:var(--soft)}} dd{{margin:0;font-size:13px}} code{{font-family:Consolas,monospace;font-size:12px}}
    a{{color:var(--pine);text-underline-offset:3px}} .asset-links{{display:flex;flex-wrap:wrap;gap:14px;font-size:12px;margin:10px 0 0}}
    .audio-group{{margin-top:42px}} .audio-row{{display:grid;grid-template-columns:minmax(180px,1.1fr) minmax(240px,1.5fr) minmax(220px,1fr);gap:22px;align-items:center;padding:20px 0;border-top:1px solid var(--line)}} audio{{width:100%;height:38px}} .audio-row dl{{margin:0}}
    .context-still img,.shot-grid img{{width:100%;display:block;border:1px solid #312f29}} figure{{margin:20px 0}} figcaption{{font-size:12px;color:var(--soft);margin-top:8px}}
    .shot-grid,.evidence-grid{{display:grid;grid-template-columns:repeat(2,1fr);gap:20px}} .pending{{padding:18px;background:#e4dccb;border:1px dashed #9c927f}} .file-list{{padding-left:20px}}
    .evidence-card{{padding:20px;background:#f5f1e7;border:1px solid var(--line)}} .evidence-card h3{{font-family:Batang,serif;font-size:22px;font-weight:500;margin:2px 0 8px}} .evidence-card ul{{padding-left:20px}}
    .status{{display:inline-block;margin-right:8px;padding:2px 8px;border:1px solid;font-size:11px;font-weight:700;letter-spacing:.08em}} .status.pass{{color:#315746;border-color:#668c78}} .status.findings{{color:#843f2f;border-color:#a66b58}} .status.unverified{{color:#665f54;border-color:#978d7b}}
    footer{{padding:28px clamp(20px,5vw,64px);font-size:12px;color:var(--soft)}}
    @media(max-width:820px){{.usage,.ui-grid,.shot-grid,.evidence-grid{{grid-template-columns:1fr}}.audio-row{{grid-template-columns:1fr}}.surfaces{{grid-template-columns:1fr}}}}
  </style>
</head>
<body><main>
  <header class="hero"><span class="stamp">TEST</span><p class="eyebrow">RECRAFT HUD · ELEVENLABS SFX</p><h1>먹의 상태를 보고,<br>재료의 결을 듣는다</h1><p class="lede">플레이테스트 전용 HUD 네 종과 효과음 열네 종의 오프라인 검토 페이지입니다. 각 소리는 사용자가 직접 눌러야 재생됩니다.</p></header>
  <aside class="notice"><strong>현재 판정</strong> · 생성 원본과 Unity용 변환 자산은 준비되었습니다. 런타임 보고서: <strong>{html.escape(evidence_status)}</strong>. PASS는 각 보고서에 적힌 통제 범위에만 적용하며 실제 필기, 직접 청음, 사용자 기기 출력과 자연 플레이 시인성은 별도 검토입니다.</aside>
  <section class="section"><p class="eyebrow">제작 원장</p><h2>사용량과 범위</h2><div class="usage"><div><span>Recraft</span><strong>{recraft_actual:.0f} units</strong><small>잔량 1028→708 · 성공 {len(ui_requests)} · 무효 요청 {len(rejected)}</small></div><div><span>ElevenLabs</span><strong>{eleven_cost:.0f} cost</strong><small>성공 생성 {len(sfx_requests)}건 · {requested_seconds:.2f}초</small></div><div><span>최종 준비 결과</span><strong>4 UI · 14 SFX</strong><small>WAV 합계 {prepared_seconds:.2f}초 · 청음 대기</small></div></div><p class="muted">Recraft의 320 units는 생성 전후 잔량 차이와 성공 응답 원장으로 확인했습니다. ElevenLabs는 각 성공 응답의 character-cost를 합산했으며, <code>brush_stroke</code>는 actual_usage가 없어 응답 헤더 값 9를 사용했습니다. 1차 <code>parry</code>도 공급자 생성에는 성공했지만 로컬 분석 RMS -55.7 dBFS로 최종 후보에서 제외했고, <code>parry_v2</code>를 아래 정본 14종에 연결했습니다. <a href="{rel_url(first_parry)}">1차 원본</a> · <a href="{rel_url(first_parry_request)}">1차 요청 원장</a></p></section>
  <section class="section"><p class="eyebrow">HUD 화이트리스트</p><h2>네 가지 표시 자산</h2><p class="muted">아래 밝은/어두운 판은 동일한 투명 PNG의 런타임 틴트 방향을 비교합니다. 이미지 안에 문자를 넣지 않으며 실제 안내 문구는 한국어 텍스트로 별도 표시합니다.</p><div class="ui-grid">{"".join(ui_cards)}</div></section>
  <section class="section"><p class="eyebrow">개별 청음 · 자동 재생 없음</p><h2>효과음 열네 종</h2><p class="muted">아래 컨트롤은 WAV를 직접 재생하는 개별 큐 시청용입니다. 주입된 인식 완료 이벤트가 실제 서비스에서 큐를 요청했는지는 별도 런타임 보고서로 판정합니다.</p>{"".join(audio_sections)}</section>
  <section class="section"><p class="eyebrow">적용 전 맥락</p><h2>기존 플레이테스트 화면</h2>{still_html or '<p class="pending">기존 VisualCorridor 참고 이미지가 없습니다.</p>'}</section>
  <section class="section"><p class="eyebrow">실제 보고서 기반 판정</p><h2>런타임 근거와 경계</h2><div class="evidence-grid">{"".join(evidence_cards)}</div></section>
  <section class="section"><p class="eyebrow">조건부 표시</p><h2>런타임 캡처</h2>{screenshot_html}<h2 style="margin-top:38px">검증 보고서</h2>{validation_html}</section>
  <section class="section"><p class="eyebrow">판정 경계</p><h2>아직 확인할 것</h2><ul>{remaining_html}</ul><p><a href="{rel_url(report_path)}">기술 보고서 보기</a> · <a href="{rel_url(spec)}">적용 명세</a> · <a href="{rel_url(macro_spec)}">플레이테스트 명세</a></p></section>
  {build_html}
  <footer>로컬 파일만 사용 · 외부 CDN 없음 · 오디오 autoplay 없음 · 생성 원본 보존</footer>
</main></body></html>
"""

    clipped = sum(int(a["clipped_samples"]) for a in audio)
    silent = sum(bool(a["silent"]) for a in audio)
    audio_rows = []
    for group_label, names in AUDIO_GROUPS:
        for name in names:
            a = audio_by_name[name]
            label = AUDIO_LABELS[name][0]
            audio_rows.append(
                f"| {group_label} | {label} | {a['duration']:.2f}s | {a['sample_rate']} Hz / {a['channels']}ch | "
                f"{a['rms_dbfs']:.1f} | {a['peak_dbfs']:.1f} | {a['gain_db']:+.1f} | {a['clipped_samples']} | `{short_hash(a['sha256'])}…` |"
            )
    ui_rows = [
        f"| {UI_LABELS[e['name']][0]} | {e['size'][0]}×{e['size'][1]} | {e['transparent_pixels']:,} | `{short_hash(e['sha256'])}…` | [{Path(e['source']).name}]({rel_md(ROOT / e['source'])}) |"
        for e in ui
    ]
    shot_lines = [
        f"- [{p.name}]({rel_md(p)}): {screenshot_boundary(p.name)}. 판정은 연결된 검증 보고서를 따른다."
        for p in screenshots
    ]
    validation_lines = [f"- [{p.name}]({rel_md(p)}): {validation_role(p, evidence)}" for p in validations]
    evidence_lines = []
    for record in evidence:
        path_text = f"[{record['path'].name}]({rel_md(record['path'])})" if record["path"] else "보고서 없음"
        evidence_lines.append(
            f"- **{record['label']}: {record['reported_status'] or record['status']}**"
            f" (판정 {record['status']}) — {evidence_summary(record)} · {path_text}\n"
            + (f"  - 보고서 범위: {record['reported_scope']}\n" if record["reported_scope"] else "")
            + f"  - 경계: {record['boundary']}"
        )
        for item in record["findings"]:
            evidence_lines.append(
                f"  - 미통과: `{item.get('name', 'unnamed')}` — {item.get('detail', item.get('status', 'finding'))}"
            )
    remaining_md = "\n".join(f"{index}. {item}" for index, item in enumerate(remaining_items, 1))
    report_text = f"""# 플레이테스트 HUD · 효과음 제작 보고서

상태: **생성·변환 자산 준비 완료 / {evidence_status} / 사용자 청음·수동 필기·기기 출력 미검증**  
명세: [SPEC-PLAYTEST-UI-AUDIO]({rel_md(spec)}) · [SPEC-WORLD-MACRO-PLAYTEST]({rel_md(macro_spec)}) · [검토 페이지](REVIEW.html)

런타임 상태는 아래 JSON 보고서에서 읽은 값을 그대로 사용한다. 주입된 인식 완료 글자를 실제 Resolver와 서비스에 전달한 검증은 직접 WAV 재생 시험보다 강한 연결 증거지만, 실제 필기·자연 플레이·사용자 기기 청음 증거는 아니다. 한국적 인상과 최종 음량도 사용자 청음 전에는 확정하지 않는다.

## 제작 결과

- Recraft: 성공 {len(ui_requests)}건과 HTTP 400 무효 요청 {len(rejected)}건, 총 시도 {recraft_attempts}건이다. 성공 응답의 `actual_usage` 합계와 생성 전후 잔량 **1028 → 708**의 차이가 모두 **{recraft_actual:.0f} units**다. 무효 요청에는 사용량 필드가 없다.
- ElevenLabs: 공급자 생성 성공 {len(sfx_requests)}건, 요청 길이 합계 **{requested_seconds:.2f}초**, 응답 `character-cost` 합계 **{eleven_cost:.0f}**다. `brush_stroke`는 `actual_usage`가 없으므로 응답 헤더 값 **9**를 사용했다.
- 1차 [`parry.mp3`]({rel_md(first_parry)})는 공급자 호출에는 성공했지만 로컬 분석 RMS -55.7 dBFS로 최종 후보에서 제외했다. 해당 [요청 원장]({rel_md(first_parry_request)})과 원본은 보존하며, 두 번째 생성 `parry_v2`를 최종 `parry.wav`의 공급자 원본으로 사용한다. 따라서 최종 묶음은 14종이고 공급자 성공 횟수·사용량에는 두 생성이 모두 포함된다.
- Recraft는 잔량 차이까지 확인했다. ElevenLabs의 계정 전체 잔량·청구서는 조회하지 않았으며, 위 수치는 각 응답이 반환한 `character-cost`의 합계다.
- Unity용 결과: 투명 PNG 4개, 44.1kHz mono WAV 14개, WAV 길이 합계 **{prepared_seconds:.2f}초**. 무음 {silent}개, 클리핑 표본 {clipped}개.
- 모든 공급자 원본 SVG/MP3, 요청 모델·프롬프트·응답 사용량·파일 해시는 `Originals/`와 `requests/`에 보존한다. 비밀키나 인증 헤더는 포함하지 않는다.

## UI 기술 요약

| 자산 | Unity PNG | 투명 픽셀 | SHA-256 | 공급자 원본 |
|---|---:|---:|---|---|
{chr(10).join(ui_rows)}

밝은/어두운 미리보기는 같은 투명 PNG에 표시용 먹색/한지색 틴트를 적용한다. 이미지 자체에는 글자가 없으며 상호작용 문구는 런타임 한국어 텍스트가 담당한다.

## 효과음 기술 요약

| 묶음 | 효과음 | 길이 | 포맷 | RMS dBFS | Peak dBFS | Gain dB | Clipped | SHA-256 |
|---|---|---:|---|---:|---:|---:|---:|---|
{chr(10).join(audio_rows)}

변환은 앞쪽 공급자 무음만 정리하고 전체 꼬리를 보존했으며, 최대 피크를 -3 dBFS 방향으로 맞추되 증폭은 +6.02 dB로 제한했다. 최종 `parry_v2`는 Peak -3.0 dBFS / RMS -23.6 dBFS다. `rest`는 Peak -13.5 dBFS, `summon_release`는 RMS -35.8 dBFS여서 실제 믹스에서 체감 음량을 확인한다. 14개 모두 `listening_review=USER_REVIEW_PENDING`이다.

## 화면과 런타임 증거

- [기존 VisualCorridor 정지 이미지]({rel_md(existing_still) if existing_still.exists() else ''}): UI 적용 전 맥락 참고이며 이번 작업에서 촬영한 새 이미지가 아니다.
{chr(10).join(shot_lines) if shot_lines else '- `Screenshots/*.png`: 아직 없음.'}

실제 보고서 판정:

{chr(10).join(evidence_lines)}

검증 보고서:

{chr(10).join(validation_lines) if validation_lines else '- 런타임 이벤트 연결·중복·정리 검증 보고서가 아직 없음.'}

## 남은 판정

{remaining_md}

## 재생성

```powershell
python Tools/PlaytestFeedback/build_review.py
```

외부 CDN, 자동 재생, 공급자 호출, Unity 실행 없이 현재 로컬 원장과 산출물만 읽어 `REVIEW.html`과 이 보고서를 다시 만든다.
"""
    return html_text, report_text + build_markdown, link_targets


def validate_written_links(html_path: Path, report_path: Path) -> tuple[int, list[str]]:
    refs: list[tuple[Path, str]] = []
    html_text = html_path.read_text(encoding="utf-8")
    if re.search(r"<audio\b[^>]*\bautoplay(?:\s|=|>)", html_text, re.IGNORECASE):
        raise RuntimeError("Review page must not contain autoplay")
    if html_text.count("<audio ") != 14:
        raise RuntimeError("Review page must contain exactly fourteen audio controls")
    if re.search(r"(?:src|href)=[\"']https?://", html_text, re.IGNORECASE):
        raise RuntimeError("Review page must not use external URLs")
    for value in re.findall(r'(?:src|href)="([^"]+)"', html_text):
        if value.startswith("#"):
            continue
        refs.append((html_path.parent, value))
    report_text = report_path.read_text(encoding="utf-8")
    for value in re.findall(r"\[[^\]]*\]\(([^)]+)\)", report_text):
        if value and not value.startswith(("http://", "https://", "#")):
            refs.append((report_path.parent, value))
    missing = []
    for base, value in refs:
        decoded = unquote(value)
        target = (base / decoded).resolve()
        if not target.exists():
            missing.append(str(target))
    return len(refs), sorted(set(missing))


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    html_text, report_text, link_targets = build()
    missing_inputs = sorted(str(path) for path in set(link_targets) if path.name != "REPORT.md" and not path.exists())
    if missing_inputs:
        raise FileNotFoundError("Missing review inputs:\n" + "\n".join(missing_inputs))
    html_path = OUT / "REVIEW.html"
    report_path = OUT / "REPORT.md"
    html_path.write_text(html_text, encoding="utf-8")
    report_path.write_text(report_text, encoding="utf-8")
    checked, missing = validate_written_links(html_path, report_path)
    if missing:
        raise FileNotFoundError("Broken local review links:\n" + "\n".join(missing))
    with report_path.open("a", encoding="utf-8") as handle:
        handle.write(f"\n링크 검사: 로컬 참조 **{checked}개**, 누락 **0개**. REVIEW.html 오디오 컨트롤 **14개**, autoplay **0개**.\n")
    checked_after, missing_after = validate_written_links(html_path, report_path)
    if missing_after:
        raise FileNotFoundError("Broken local review links after report update:\n" + "\n".join(missing_after))
    print(json.dumps({"html": str(html_path), "report": str(report_path), "local_links": checked_after, "missing": 0, "audio_controls": 14, "autoplay": 0}, ensure_ascii=False))


if __name__ == "__main__":
    main()
