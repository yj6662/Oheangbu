"""Build the offline playtest-menu review page and evidence report.

The generator deliberately reports only evidence already present on disk. Unity
runtime captures belong in Screenshots/ and machine-readable or text reports in
Validation/. Re-running this file updates both deliverables without touching the
Unity project or the preserved pre-menu scene.
"""

from __future__ import annotations

import hashlib
import html
import json
import os
import re
from datetime import datetime, timezone
from pathlib import Path
from urllib.parse import quote, unquote, urlparse


ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "Art/UIAudio/PlaytestMenus"
SCREENSHOTS = OUT / "Screenshots"
VALIDATION = OUT / "Validation"
BUILDS = ROOT / "Builds/Playtest-20260915"

SPEC = ROOT / "Docs/Specs/SPEC-PLAYTEST-MENUS.md"
FONT = ROOT / "Oheangbu/Assets/_Project/Art/UI/PlaytestMenus/Fonts/NotoSansCJKkr-Regular.otf"
FONT_LICENSE = ROOT / "Oheangbu/Assets/_Project/Art/UI/PlaytestMenus/Fonts/NotoSansCJKkr-LICENSE.txt"
PAPER = ROOT / "Oheangbu/Assets/_Project/Art/UI/PlaytestMenus/HanjiSurface.png"
TITLE_ART = ROOT / "Oheangbu/Assets/_Project/Art/UI/PlaytestMenus/TitlePalace.png"
THEME = ROOT / "Oheangbu/Assets/_Project/Art/UI/PlaytestMenus/PlaytestTheme.asset"
MAP_DATA = ROOT / "Oheangbu/Assets/_Project/Resources/WorldMap/WorldMapBakedData.asset"
MAP_IMAGE = ROOT / "Oheangbu/Assets/_Project/Resources/WorldMap/WorldMapBase.png"
BACKUP = OUT / "Backups/W_WorldMacro_Playtest.before-menus.unity"
INSTALLATION = OUT / "installation_validation.txt"
PRIOR_REVIEW = ROOT / "Art/UIAudio/PlaytestFeedback/REVIEW.html"
PRIOR_REPORT = ROOT / "Art/UIAudio/PlaytestFeedback/REPORT.md"

FONT_SOURCE = "https://github.com/notofonts/noto-cjk/tree/main/Sans/OTF/Korean"
FONT_LICENSE_SOURCE = "https://openfontlicense.org/open-font-license-official-text/"

PAGES = [
    ("title", "타이틀 로비", "이어하기·새 게임·옵션·조작 안내와 저장 상태"),
    ("hud", "플레이 HUD", "체력·먹·레티클·한국어 상호작용 안내"),
    ("pause", "일시정지", "시간 정지와 여정 복귀 동선"),
    ("inventory", "소지품", "실제 저장 소지품과 석경 파편"),
    ("codex", "술식 도감", "발견한 글자와 오행 설명"),
    ("chapae", "차패", "아직 새겨지지 않은 덕목과 획득한 소유자 이력"),
    ("options", "옵션", "화면·음량·조작·접근성 설정"),
    ("map_cave", "폐광 상세 지도", "실제 보행 바닥·진입 경사·동굴 내부 경로"),
    ("map", "탐험 지도", "발견 영역·마커·핀·현재 위치"),
    ("map_fold", "지도 접힘", "여섯 한지 패널의 펼침과 역방향 닫힘"),
]

ALIASES = {
    "title": ("title", "lobby", "타이틀"),
    "hud": ("hud",),
    "pause": ("pause", "paused", "일시정지"),
    "inventory": ("inventory", "items", "소지품"),
    "codex": ("codex", "spellbook", "도감"),
    "chapae": ("chapae", "차패"),
    "options": ("options", "settings", "옵션"),
    "map_cave": ("map_cave", "map-cave", "cave_map", "mine_map", "동굴지도", "폐광지도"),
    "map": ("map", "worldmap", "지도"),
    "map_fold": ("map_fold", "map-fold", "fold", "folding", "지도접"),
}

BUILD_EVIDENCE_NAMES = (
    "BUILD_INFO.json",
    "package_report.json",
    "release_build_report.json",
    "ui_smoke.json",
    "smoke_start.json",
    "smoke_restart.json",
    "TEST_GUIDE.txt",
    "README_플레이테스트.txt",
)


def rel_url(path: Path) -> str:
    return quote(os.path.relpath(path, OUT).replace(os.sep, "/"), safe="/._-~")


def rel_md(path: Path) -> str:
    return os.path.relpath(path, OUT).replace(os.sep, "/")


def digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def load_json(path: Path):
    try:
        return json.loads(path.read_text(encoding="utf-8-sig"))
    except (OSError, UnicodeDecodeError, json.JSONDecodeError):
        return None


def text_excerpt(path: Path, limit: int = 900) -> str:
    try:
        value = path.read_text(encoding="utf-8-sig", errors="replace").strip()
    except OSError:
        return ""
    return value[:limit] + ("…" if len(value) > limit else "")


def screenshot_group(path: Path) -> str | None:
    stem = path.stem.lower().replace(" ", "_")
    # Fold must win over the generic map token.
    order = ["map_fold"] + [slug for slug, _, _ in PAGES if slug != "map_fold"]
    for slug in order:
        if any(alias.lower() in stem for alias in ALIASES[slug]):
            return slug
    return None


def discover_screenshots() -> dict[str | None, list[Path]]:
    groups: dict[str | None, list[Path]] = {slug: [] for slug, _, _ in PAGES}
    groups[None] = []
    if SCREENSHOTS.exists():
        for path in sorted(SCREENSHOTS.rglob("*")):
            if path.is_file() and path.suffix.lower() in {".png", ".jpg", ".jpeg", ".webp"} and not path.stem.startswith("records_"):
                groups[screenshot_group(path)].append(path)
    return groups


def discover_validations() -> list[Path]:
    files: list[Path] = []
    if INSTALLATION.exists():
        files.append(INSTALLATION)
    if VALIDATION.exists():
        files.extend(
            path for path in VALIDATION.rglob("*")
            if path.is_file() and path.suffix.lower() in {".json", ".txt", ".md", ".log", ".csv"} and path.name != "capture_records.json"
        )
    return sorted(set(files))


def discover_builds() -> list[dict]:
    """Return packaged candidates newest-first without treating presence as QA."""
    builds: list[dict] = []
    if not BUILDS.exists():
        return builds
    for folder in sorted((p for p in BUILDS.iterdir() if p.is_dir()), reverse=True):
        evidence = [folder / name for name in BUILD_EVIDENCE_NAMES if (folder / name).exists()]
        info = load_json(folder / "BUILD_INFO.json")
        if not evidence or not isinstance(info, dict):
            continue
        archive_matches = sorted(BUILDS.glob(f"*{folder.name}.zip"))
        guide = folder / "TEST_GUIDE.txt"
        guide_text = text_excerpt(guide, 10000) if guide.exists() else ""
        issues: list[str] = []
        if "우클릭 표식" in guide_text or "우클릭: 표식" in guide_text:
            issues.append("TEST_GUIDE가 핀을 우클릭으로 안내하지만 실제 구현은 발견한 야외 영역 좌클릭입니다.")
        build_result = str(info.get("buildResult", "")).strip()
        errors = info.get("errors")
        qa_complete = info.get("qaComplete") is True
        if build_result and build_result.lower() not in {"succeeded", "success", "passed", "pass"}:
            issues.append(f"빌드 결과: {build_result}")
        if isinstance(errors, int) and errors:
            issues.append(f"빌드 오류 {errors}건")
        status = "FINDINGS" if issues else "PASS" if qa_complete else "FILED"
        detail_parts = []
        if build_result:
            detail_parts.append(f"build={build_result}")
        if "startup" in info:
            detail_parts.append(f"startup={info['startup']}")
        if "restart" in info:
            detail_parts.append(f"restart={info['restart']}")
        detail_parts.append("qaComplete=true" if qa_complete else "qaComplete=false")
        builds.append({
            "folder": folder,
            "info": info,
            "evidence": evidence,
            "archives": archive_matches,
            "issues": issues,
            "status": status,
            "detail": "; ".join(detail_parts),
        })
    return builds


def discover_build_reports() -> list[Path]:
    if not BUILDS.exists():
        return []
    return sorted(
        path for path in BUILDS.iterdir()
        if path.is_file() and path.suffix.lower() in {".json", ".txt", ".md", ".log"}
    )


def control_audit() -> list[tuple[str, str]]:
    """Document the user-facing input contract visible in the current sources."""
    facts = [
        ("지도 핀", "M으로 지도를 열고 발견한 야외 영역을 좌클릭합니다. 드래그는 이동, 휠은 확대·축소이며 동굴 상세에서는 핀을 놓지 않습니다."),
        ("술식 작도", "Q와 마우스 좌클릭을 누른 채 획을 그리고 Q를 놓아 시전합니다."),
    ]
    return facts


def capture_resolutions(groups: dict[str | None, list[Path]]) -> list[str]:
    found = set()
    for paths in groups.values():
        for path in paths:
            match = re.search(r"(?<!\d)(\d{3,5})x(\d{3,5})(?!\d)", path.stem.lower())
            if match: found.add(match.group(1) + "x" + match.group(2))
    return sorted(found, key=lambda value: tuple(map(int, value.split("x"))))


def technical_summary(validations: list[Path]) -> list[tuple[str, str, str, Path | None]]:
    by_name = {path.name: path for path in validations}
    rows: list[tuple[str, str, str, Path | None]] = []
    runtime = by_name.get("menu_runtime_checks.json")
    if runtime:
        payload = load_json(runtime) or {}
        status, _, passed, total = explicit_status(runtime)
        rows.append(("메뉴·저장·수집", status,
            f"명시 검사 {passed}/{total}; 수집 이벤트 {payload.get('collectionEvents', '—')}, 고유 묶음 {payload.get('uniqueBundlesCollected', '—')}, 파편 {payload.get('fragmentItemsCollected', '—')}. 합성 키보드 입력과 격리 저장 슬롯 기반.", runtime))
    advanced = by_name.get("menu_advanced_review.json")
    if advanced:
        payload = load_json(advanced) or {}
        status, _, passed, total = explicit_status(advanced)
        mean = payload.get("mapMeanTickMilliseconds")
        mean_text = f"{mean:.4f}ms" if isinstance(mean, (int, float)) else "—"
        rows.append(("차량 일시정지·지도 수명주기", status,
            f"명시 검사 {passed}/{total}; 지도 열기·닫기 {payload.get('mapCycles', '—')}회, 텍스처 증감 {payload.get('textureDelta', '—')}, 측정 평균 Map.Tick {mean_text}. 진단 설정과 합성 입력 기반.", advanced))
    layout = by_name.get("layout_review.json")
    if layout:
        status, _, passed, total = explicit_status(layout)
        rows.append(("기본 배율 레이아웃", status,
            "1280x720·1920x1080·2560x1440·3440x1440은 CanvasScaler 계산과 활성 텍스트 크기 분석 결과입니다. 실제 화면 캡처 해상도와 구분합니다.", layout))
    large = by_name.get("layout_large_review.json")
    if large:
        payload = load_json(large) or {}
        status, _, _, _ = explicit_status(large)
        overflow = len(payload.get("textOverflows", [])) if isinstance(payload.get("textOverflows"), list) else "—"
        rows.append(("큰 UI·텍스트 배율", status,
            f"UiScale 1.20 / TextScale 1.25 분석에서 텍스트 넘침 {overflow}건이 보고되었습니다. 실제 확대 화면의 미술·가독성 판단과 구분합니다.", large))
    return rows


def is_historical(path: Path) -> bool:
    stem = path.stem.lower()
    return any(part.lower() in {"attempt", "attempts", "archive", "archived"} for part in path.parts) or \
           "_before_" in stem or stem.startswith("partial_") or "_partial_" in stem


def explicit_status(path: Path) -> tuple[str, str, int, int]:
    """Return status, reported detail, passed checks, total checks.

    FILED means evidence exists but carries no explicit machine verdict. It must
    never be promoted to PASS by filename or by the generator's expectations.
    """
    payload = load_json(path) if path.suffix.lower() == ".json" else None
    if isinstance(payload, dict):
        checks = payload.get("checks")
        passed = total = 0
        failed = False
        if isinstance(checks, list):
            for check in checks:
                if not isinstance(check, dict):
                    continue
                total += 1
                raw = check.get("pass")
                ok = raw is True or str(check.get("status", "")).upper() in {"PASS", "PASSED"}
                passed += int(ok)
                failed |= not ok
        failures = payload.get("failures")
        failed |= bool(failures)
        raw_status = str(payload.get("status", "")).strip()
        upper = raw_status.upper()
        if failed or upper.startswith(("FAIL", "ERROR", "FINDING")):
            status = "FINDINGS"
        elif payload.get("pass") is True or upper.startswith("PASS") or upper in {"SUCCESS", "SUCCEEDED", "COMPLETE"}:
            status = "PASS"
        elif total:
            status = "PASS" if passed == total else "FINDINGS"
        else:
            status = "FILED"
        detail = raw_status or str(payload.get("scope") or payload.get("detail") or "JSON report filed")
        return status, detail, passed, total

    excerpt = text_excerpt(path)
    lines = [line.strip() for line in excerpt.splitlines() if line.strip()]
    pass_lines = sum(line.upper().startswith("PASS") for line in lines)
    finding_lines = sum(line.upper().startswith(("FAIL", "ERROR", "FINDING")) for line in lines)
    status = "FINDINGS" if finding_lines else "PASS" if lines and pass_lines == len(lines) else "FILED"
    return status, lines[0] if lines else "Text report filed", pass_lines, len(lines)


def validation_matches(path: Path, slug: str) -> bool:
    stem = path.stem.lower().replace(" ", "_")
    if slug == "map" and (any(alias.lower() in stem for alias in ALIASES["map_cave"]) or
                          any(alias.lower() in stem for alias in ALIASES["map_fold"])):
        return False
    return any(alias.lower() in stem for alias in ALIASES[slug])


def state_badge(status: str) -> str:
    labels = {"PASS": "보고된 PASS", "FINDINGS": "확인 필요", "FILED": "자료 있음", "UNVERIFIED": "증거 대기"}
    return f'<span class="badge {status.lower()}">{labels[status]}</span>'


def screenshot_html(path: Path) -> str:
    label = path.stem.replace("_", " ")
    return f"""
    <figure class="shot">
      <a href="{rel_url(path)}" target="_blank" rel="noopener" title="원본 크기로 열기">
        <img loading="lazy" src="{rel_url(path)}" alt="{html.escape(label)} 런타임 진단 캡처">
      </a>
      <figcaption><strong>{html.escape(path.name)}</strong><br>런타임 진단 상태 설정 캡처 · 클릭하면 원본 크기로 열림</figcaption>
    </figure>"""


def build_report(groups: dict[str | None, list[Path]], validations: list[Path], builds: list[dict], build_reports: list[Path]) -> str:
    now = datetime.now(timezone.utc).strftime("%Y-%m-%d %H:%M UTC")
    lines = [
        "# Playtest menus review evidence",
        "",
        f"Generated: {now}",
        "",
        "This file is regenerated by `Tools/PlaytestFeedback/build_menu_review.py`.",
        "",
        "## Evidence boundary",
        "",
        "2026-09-15: 기록/기록첩 메뉴와 검토 진입을 제거했다. 기존 조사·NPC 완료 ID 및 저장 스키마는 유지한다. 아래 이전 캡처와 통합 검사 JSON은 당시 구현의 증거이며, 사이드바에 제거 전 기록 탭이 보이더라도 현재 메뉴를 나타내지 않는다. 새 런타임 캡처는 아직 수행하지 않았다.",
        "",
        "Runtime screenshots use deterministic diagnostic state setup. They show rendered UI states, not manual traversal or organic player input. Synthetic input checks are separate evidence and count only when an explicit validation report records them. No manual walk-through or video review has been performed.",
        "",
        "A screenshot proves neither persistence nor interaction by itself. Missing files remain pending and are never inferred as passing.",
        "",
        "Analytical layout coverage includes 1280x720, 1920x1080, 2560x1440, and 3440x1440. Current actual screenshot resolutions: " + (", ".join(capture_resolutions(groups)) or "none") + ".",
        "",
        "## Page coverage",
        "",
        "| Page | Screenshots | Related validation | State |",
        "|---|---:|---:|---|",
    ]
    for slug, title, _ in PAGES:
        related = [path for path in validations if validation_matches(path, slug)]
        statuses = [explicit_status(path)[0] for path in related if not is_historical(path)]
        state = "FINDINGS" if "FINDINGS" in statuses else "PASS" if "PASS" in statuses else "FILED" if related else "UNVERIFIED"
        lines.append(f"| {title} | {len(groups[slug])} | {len(related)} | {state} |")
    lines.extend(["", "## Validation files", ""])
    if validations:
        for path in validations:
            status, detail, passed, total = explicit_status(path)
            checks = f"; explicit checks {passed}/{total}" if total else ""
            role = "; historical attempt, not the current verdict" if is_historical(path) else ""
            lines.append(f"- [{rel_md(path)}]({rel_md(path)}): **{status}**{role} — {detail}{checks}")
    else:
        lines.append("- No validation files are present yet.")
    lines.extend(["", "## Current control contract", ""])
    for label, detail in control_audit():
        lines.append(f"- **{label}:** {detail}")
    lines.extend(["", "## Latest technical evidence", ""])
    summaries = technical_summary(validations)
    if summaries:
        for label, status, detail, path in summaries:
            lines.append(f"- **{label} — {status}:** {detail} [{path.name}]({rel_md(path)})")
    else:
        lines.append("- No matching runtime, advanced, or layout reports are present.")
    lines.extend(["", "## Packaged build candidates", ""])
    if builds:
        for candidate in builds:
            folder = candidate["folder"]
            lines.append(f"### {folder.name} — {candidate['status']}")
            lines.append("")
            lines.append(candidate["detail"] + ". Candidate presence or packaging integrity alone does not mean manual QA is complete.")
            if candidate["issues"]:
                lines.extend(["", *[f"- **Finding:** {issue}" for issue in candidate["issues"]]])
            links = candidate["evidence"] + candidate["archives"]
            lines.extend(["", "Evidence: " + " · ".join(f"[{path.name}]({rel_md(path)})" for path in links), ""])
    else:
        lines.extend(["No candidate with `BUILD_INFO.json` is present yet. Re-run the generator after packaging to add links automatically.", ""])
    if build_reports:
        lines.extend(["Build-wide reports: " + " · ".join(f"[{path.name}]({rel_md(path)})" for path in build_reports), ""])
    lines.extend([
        "",
        "## Preserved inputs",
        "",
        f"- [Pre-menu scene backup]({rel_md(BACKUP)})" + (f" — SHA-256 `{digest(BACKUP)}`" if BACKUP.exists() else " — missing"),
        f"- [Earlier HUD/audio review]({rel_md(PRIOR_REVIEW)}) and [report]({rel_md(PRIOR_REPORT)}) remain separate evidence.",
        f"- [Noto Sans CJK KR font]({rel_md(FONT)}) and [SIL OFL 1.1 license]({rel_md(FONT_LICENSE)}); official source: {FONT_SOURCE}",
        "",
        "## Pending manual evidence",
        "",
        "- Manual walk-through and controller/keyboard navigation.",
        "- Human readability on target displays and native audio listening on target hardware.",
        "- Native mouse/keyboard/controller behavior and GPU/device performance.",
        "- Video verification of the six-panel open/close motion.",
        "- Persistence and scene round-trip checks unless an explicit report above records them.",
        "",
    ])
    return "\n".join(lines)


def build_html(groups: dict[str | None, list[Path]], validations: list[Path], builds: list[dict], build_reports: list[Path]) -> str:
    page_sections: list[str] = []
    for index, (slug, title, description) in enumerate(PAGES, 1):
        shots = groups[slug]
        related = [path for path in validations if validation_matches(path, slug)]
        statuses = [explicit_status(path)[0] for path in related if not is_historical(path)]
        status = "FINDINGS" if "FINDINGS" in statuses else "PASS" if "PASS" in statuses else "FILED" if related else "UNVERIFIED"
        shot_markup = "".join(screenshot_html(path) for path in shots) if shots else '<div class="empty">Screenshots/에 해당 화면의 런타임 캡처가 들어오면 여기에 표시됩니다.</div>'
        validation_links = " · ".join(f'<a href="{rel_url(path)}">{html.escape(path.name)}</a>' for path in related) or "연결된 검증 보고서 없음"
        page_sections.append(f"""
        <section class="page-card" id="{slug}">
          <header><span class="number">{index:02d}</span><div><p class="eyebrow">PLAYTEST MENU</p><h2>{html.escape(title)}</h2><p>{html.escape(description)}</p></div>{state_badge(status)}</header>
          <div class="shots">{shot_markup}</div>
          <p class="evidence-links"><strong>검증 자료</strong> · {validation_links}</p>
        </section>""")

    if groups[None]:
        page_sections.append(f"""
        <section class="page-card" id="additional">
          <header><span class="number">+</span><div><p class="eyebrow">ADDITIONAL</p><h2>추가 런타임 이미지</h2><p>파일명으로 화면을 특정하지 않은 캡처</p></div>{state_badge('FILED')}</header>
          <div class="shots">{''.join(screenshot_html(path) for path in groups[None])}</div>
        </section>""")

    validation_cards: list[str] = []
    for path in validations:
        status, detail, passed, total = explicit_status(path)
        excerpt = text_excerpt(path)
        if path.suffix.lower() == ".json":
            payload = load_json(path)
            excerpt = json.dumps(payload, ensure_ascii=False, indent=2)[:1800] if payload is not None else excerpt
        count = f" · 명시 검사 {passed}/{total}" if total else ""
        role = " · 과거 시도이며 현재 판정이 아님" if is_historical(path) else ""
        validation_cards.append(f"""
        <article class="validation-card">
          <div>{state_badge(status)}<h3>{html.escape(path.name)}</h3><p>{html.escape(detail)}{count}{role}</p></div>
          <a href="{rel_url(path)}">원본 자료</a>
          <details><summary>내용 일부</summary><pre>{html.escape(excerpt)}</pre></details>
        </article>""")
    validation_markup = "".join(validation_cards) or '<div class="empty">Validation/ 보고서가 아직 없습니다. 파일이 생기면 판정을 추측하지 않고 그대로 읽습니다.</div>'

    control_markup = "".join(
        f'<article><h3>{html.escape(label)}</h3><p>{html.escape(detail)}</p></article>'
        for label, detail in control_audit()
    )
    technical_cards = "".join(
        f'<article class="validation-card"><div>{state_badge(status)}<h3>{html.escape(label)}</h3><p>{html.escape(detail)}</p></div>'
        f'<a href="{rel_url(path)}">{html.escape(path.name)}</a></article>'
        for label, status, detail, path in technical_summary(validations)
    ) or '<div class="empty">해당 기술 보고서가 들어오면 현재 판정을 그대로 요약합니다.</div>'
    actual_resolutions = ", ".join(capture_resolutions(groups)) or "캡처 없음"
    build_cards: list[str] = []
    for candidate in builds:
        folder = candidate["folder"]
        links = candidate["evidence"] + candidate["archives"]
        issue_markup = "".join(f'<li>{html.escape(issue)}</li>' for issue in candidate["issues"])
        if issue_markup:
            issue_markup = f'<ul class="findings-list">{issue_markup}</ul>'
        build_cards.append(f"""
        <article class="validation-card build-card">
          <div>{state_badge(candidate['status'])}<h3>{html.escape(folder.name)}</h3><p>{html.escape(candidate['detail'])}</p>{issue_markup}</div>
          <p class="build-links">{' · '.join(f'<a href="{rel_url(path)}">{html.escape(path.name)}</a>' for path in links)}</p>
        </article>""")
    build_markup = "".join(build_cards) or '<div class="empty">BUILD_INFO.json이 있는 후보가 생기면 빌드·스모크·가이드·패키지 보고서 링크를 자동으로 표시합니다.</div>'
    build_report_links = " · ".join(f'<a href="{rel_url(path)}">{html.escape(path.name)}</a>' for path in build_reports)
    if build_report_links:
        build_report_links = f'<p class="build-links"><strong>빌드 공통 보고서</strong> · {build_report_links}</p>'

    paper_style = rel_url(PAPER) if PAPER.exists() else ""
    font_url = rel_url(FONT)
    return f"""<!doctype html>
<html lang="ko"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>오행부 · 플레이테스트 메뉴 검토</title>
<style>
@font-face{{font-family:NotoMenu;src:url('{font_url}') format('opentype');font-display:swap}}
:root{{--ink:#262b28;--muted:#626962;--paper:#eee6d2;--paper2:#ddd2b8;--seal:#8f3229;--line:rgba(38,43,40,.18);--night:#171b19}}
*{{box-sizing:border-box}}html{{scroll-behavior:smooth}}body{{margin:0;background:var(--night);color:var(--ink);font-family:NotoMenu,"Malgun Gothic",sans-serif;line-height:1.65}}
body:before{{content:"";position:fixed;inset:0;background:linear-gradient(120deg,rgba(255,255,255,.025),transparent 42%),radial-gradient(circle at 75% 12%,rgba(161,126,76,.13),transparent 34%);pointer-events:none}}
a{{color:inherit;text-underline-offset:3px}}.shell{{width:min(1460px,calc(100% - 32px));margin:24px auto 70px;background:var(--paper);background-image:linear-gradient(rgba(239,231,210,.88),rgba(239,231,210,.88)),url('{paper_style}');box-shadow:0 24px 90px #0008}}
.hero{{padding:clamp(34px,6vw,86px);min-height:430px;display:grid;grid-template-columns:minmax(0,1.3fr) minmax(280px,.7fr);gap:56px;align-items:end;border-bottom:1px solid var(--line)}}
.eyebrow{{font-size:12px;letter-spacing:.19em;color:var(--seal);margin:0 0 8px}}h1{{font-size:clamp(44px,7vw,92px);line-height:1.05;margin:0 0 24px;font-weight:500;letter-spacing:-.055em}}h2,h3,p{{margin-top:0}}.hero-copy{{font-size:20px;color:var(--muted);max-width:720px}}.scope{{border-left:3px solid var(--seal);padding:18px 22px;background:#fff4}}.scope strong{{display:block;margin-bottom:6px}}
.nav{{position:sticky;top:0;z-index:20;display:flex;gap:8px;overflow:auto;padding:12px 20px;background:#eee6d2ee;backdrop-filter:blur(8px);border-bottom:1px solid var(--line)}}.nav a{{white-space:nowrap;text-decoration:none;padding:6px 10px;border-radius:3px}}.nav a:hover{{background:#fff7}}
main{{padding:clamp(18px,4vw,58px)}}.section-lead{{max-width:840px;margin:0 0 32px;color:var(--muted)}}.page-card{{padding:34px 0 48px;border-top:1px solid var(--line)}}.page-card>header{{display:grid;grid-template-columns:54px minmax(0,1fr) auto;gap:18px;align-items:start}}.page-card h2{{font-size:32px;margin-bottom:6px;font-weight:500}}.number{{font-family:serif;font-size:26px;color:var(--seal)}}
.badge{{display:inline-block;white-space:nowrap;padding:5px 10px;border:1px solid;border-radius:999px;font-size:12px;letter-spacing:.04em}}.pass{{color:#315d46;background:#e0eadf}}.findings{{color:#873a30;background:#f1dcd4}}.filed{{color:#625830;background:#eee6bc}}.unverified{{color:#686c67;background:#e2e1db}}
.shots{{display:grid;grid-template-columns:repeat(auto-fit,minmax(min(100%,420px),1fr));gap:22px;margin:24px 0}}.shot{{margin:0;background:#171b19;padding:10px;box-shadow:0 8px 24px #51482e30}}.shot a{{display:block}}.shot img{{display:block;width:100%;height:auto;max-height:70vh;object-fit:contain;background:#0e100f}}.shot figcaption{{color:#d7d0bd;font-size:13px;padding:10px 6px 3px}}.empty{{min-height:150px;display:grid;place-items:center;text-align:center;padding:25px;border:1px dashed #897f6a;color:var(--muted);background:#fff3}}
.evidence-links{{font-size:14px;color:var(--muted)}}.evidence-links a{{margin-right:6px}}.evidence{{margin-top:40px;padding-top:35px;border-top:2px solid var(--ink)}}.validation-grid{{display:grid;grid-template-columns:repeat(auto-fit,minmax(min(100%,340px),1fr));gap:14px}}.validation-card{{background:#fff5;border:1px solid var(--line);padding:18px}}.validation-card h3{{margin:10px 0 4px;font-size:17px;word-break:break-all}}.validation-card p{{font-size:14px;color:var(--muted)}}details{{margin-top:14px}}pre{{white-space:pre-wrap;word-break:break-word;max-height:300px;overflow:auto;background:#1b1e1c;color:#e9e2d0;padding:12px;font-size:11px}}
.provenance{{display:grid;grid-template-columns:repeat(auto-fit,minmax(260px,1fr));gap:18px;margin-top:24px}}.provenance article{{padding:20px;border:1px solid var(--line);background:#fff4}}.findings-list{{margin:12px 0 0;padding-left:20px;color:#873a30}}.build-links{{margin:14px 0 0;overflow-wrap:anywhere}}footer{{padding:30px clamp(18px,4vw,58px);border-top:1px solid var(--line);color:var(--muted);font-size:13px}}
@media(max-width:760px){{.shell{{width:100%;margin:0}}.hero{{grid-template-columns:1fr;gap:24px;min-height:0}}.page-card>header{{grid-template-columns:38px 1fr}}.page-card>header .badge{{grid-column:2}}main{{padding-top:28px}}}}
@media(prefers-reduced-motion:reduce){{html{{scroll-behavior:auto}}}}
</style></head><body><div class="shell">
<header class="hero"><div><p class="eyebrow">EXTERNAL PLAYTEST · MENU REVIEW</p><h1>여정을 위한<br>한지 메뉴</h1><p class="hero-copy">한국어 한지 메뉴, 지속 탐험 지도, 석경 수집 상태를 실제 생성물과 런타임 증거로 검토합니다.</p><p>2026-09-15: 기록첩 메뉴를 제거했습니다. 이전 화면의 기록 탭은 당시 구현이며 현재 기능이 아닙니다. 새 런타임 캡처는 아직 없습니다.</p></div><aside class="scope"><strong>현재 증거 경계</strong>스크린샷은 런타임 진단 상태 설정으로 만든 정지 이미지입니다. 현재 실제 캡처 해상도는 {html.escape(actual_resolutions)}입니다. 720p·1080p·1440p·울트라와이드 표기는 분석 보고서이며 해당 해상도의 실제 캡처를 뜻하지 않습니다. 수동 보행·동영상·사람의 청취 검토는 아직 수행하지 않았습니다.</aside></header>
<nav class="nav"><a href="#pages">화면</a>{''.join(f'<a href="#{slug}">{title}</a>' for slug,title,_ in PAGES)}<a href="#controls">조작</a><a href="#technical">기술 요약</a><a href="#evidence">검증</a><a href="#builds">빌드</a><a href="#sources">출처</a></nav>
<main><section id="pages"><p class="eyebrow">SCREEN INDEX</p><h2>화면별 검토</h2><p class="section-lead">Screenshots/의 실제 파일만 표시합니다. 이미지는 원본에 연결되며, 캡처가 없다는 사실을 검증 통과로 바꾸지 않습니다.</p>{''.join(page_sections)}</section>
<section class="evidence" id="controls"><p class="eyebrow">CONTROL CONTRACT</p><h2>현재 조작 안내</h2><p class="section-lead">현재 구현과 배포 문구를 대조할 기준입니다. 스크린샷이나 파일 존재만으로 실제 입력 성공을 판정하지 않습니다.</p><div class="provenance">{control_markup}</div></section>
<section class="evidence" id="technical"><p class="eyebrow">LATEST TECHNICAL EVIDENCE</p><h2>최근 자동 검증 요약</h2><p class="section-lead">런타임·고급 수명주기·레이아웃 보고서의 명시 판정만 요약합니다. 네이티브 장치 입력, 실제 디스플레이 가독성, 사람의 음향 청취, GPU·기기 성능은 아직 검증하지 않았습니다.</p><div class="validation-grid">{technical_cards}</div></section>
<section class="evidence" id="evidence"><p class="eyebrow">VALIDATION LEDGER</p><h2>검증 자료</h2><p class="section-lead">PASS는 보고서 안의 명시적 판정만 반영합니다. 자료 있음은 파일이 존재하지만 자동 판정 근거가 부족하다는 뜻입니다.</p><div class="validation-grid">{validation_markup}</div></section>
<section class="evidence" id="builds"><p class="eyebrow">BUILD CANDIDATES</p><h2>빌드 후보와 보고서</h2><p class="section-lead">BUILD_INFO가 있는 후보만 표시합니다. qaComplete=false는 자동 검사가 일부 통과했더라도 사람의 플레이 검토가 끝나지 않았다는 뜻입니다. 오래된 가이드의 조작 불일치도 후보별로 표시합니다.</p>{build_report_links}<div class="validation-grid">{build_markup}</div></section>
<section class="evidence" id="sources"><p class="eyebrow">ASSET &amp; PRESERVATION</p><h2>자산과 보존 원장</h2><div class="provenance">
<article><h3>한국어 글꼴</h3><p>Noto Sans CJK KR Regular · SIL Open Font License 1.1</p><p><a href="{rel_url(FONT)}">포함 OTF</a> · <a href="{rel_url(FONT_LICENSE)}">포함 라이선스</a> · <a href="{FONT_SOURCE}">공식 Noto 저장소</a> · <a href="{FONT_LICENSE_SOURCE}">OFL 원문</a></p></article>
<article><h3>한지와 지도</h3><p><a href="{rel_url(PAPER)}">HanjiSurface.png</a> · <a href="{rel_url(TITLE_ART)}">TitlePalace.png</a> · <a href="{rel_url(THEME)}">PlaytestTheme.asset</a> · <a href="{rel_url(MAP_IMAGE)}">WorldMapBase.png</a> · <a href="{rel_url(MAP_DATA)}">WorldMapBakedData.asset</a></p></article>
<article><h3>기존 결과 보존</h3><p><a href="{rel_url(BACKUP)}">메뉴 설치 전 플레이 씬</a>{' · SHA-256 '+digest(BACKUP)[:16]+'…' if BACKUP.exists() else ' · 파일 대기'}</p><p><a href="{rel_url(PRIOR_REVIEW)}">이전 HUD·효과음 검토</a> · <a href="{rel_url(PRIOR_REPORT)}">이전 기술 보고서</a></p></article>
<article><h3>기준 문서</h3><p><a href="{rel_url(SPEC)}">SPEC-PLAYTEST-MENUS</a> · <a href="REPORT.md">생성된 증거 보고서</a></p></article>
</div></section></main><footer>오행부 플레이테스트 메뉴 검토 · 오프라인 정적 문서 · 외부 CDN과 자동 재생 없음</footer>
</div></body></html>"""


def validate_local_links(document: str, report_path: Path) -> list[str]:
    missing: list[str] = []
    for target in re.findall(r'(?:href|src)=["\']([^"\']+)', document):
        parsed = urlparse(target)
        if parsed.scheme or target.startswith("#"):
            continue
        resolved = (OUT / unquote(parsed.path)).resolve()
        if not resolved.exists():
            missing.append(target)
    # Check local markdown targets without interpreting web URLs.
    report = report_path.read_text(encoding="utf-8")
    for target in re.findall(r"\[[^]]+\]\(([^)]+)\)", report):
        parsed = urlparse(target)
        if parsed.scheme or target.startswith("#"):
            continue
        if not (OUT / unquote(parsed.path)).resolve().exists():
            missing.append(target)
    return sorted(set(missing))


def build() -> tuple[Path, Path]:
    OUT.mkdir(parents=True, exist_ok=True)
    SCREENSHOTS.mkdir(exist_ok=True)
    VALIDATION.mkdir(exist_ok=True)
    groups = discover_screenshots()
    validations = discover_validations()
    builds = discover_builds()
    build_reports = discover_build_reports()
    report_path = OUT / "REPORT.md"
    review_path = OUT / "REVIEW.html"
    report_path.write_text(build_report(groups, validations, builds, build_reports), encoding="utf-8")
    document = build_html(groups, validations, builds, build_reports)
    review_path.write_text(document, encoding="utf-8")
    missing = validate_local_links(document, report_path)
    if missing:
        raise RuntimeError("Missing local review links: " + ", ".join(missing))
    return review_path, report_path


if __name__ == "__main__":
    review, report = build()
    print(f"Wrote {review.relative_to(ROOT)}")
    print(f"Wrote {report.relative_to(ROOT)}")
