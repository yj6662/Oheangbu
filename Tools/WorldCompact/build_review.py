"""Build a static, evidence-labelled compact-world review after authoring finishes.

Default: refuse to overwrite REVIEW.html/REPORT.md while current terrain is incomplete.
--check: read receipts and report freshness without writing the review.
--allow-incomplete: deliberately produce an unfinished draft, never a readiness claim.
--self-test: run pure managed Python checks; no Unity or scene/asset mutation.
"""
from __future__ import annotations
import argparse
from datetime import datetime, timezone
import hashlib
import html
import json
import math
import os
from pathlib import Path
import re
import sys
from urllib.parse import quote

SOURCE_SCENE = "Assets/_Project/Scenes/World/W_Demo_Campaign.unity"
TARGET_SCENE = "Assets/_Project/Scenes/World/W_Demo_Compact.unity"
ASSET_FOLDER = "Assets/_Project/Art/World/WorldCompact"
CAPTURE_NAMES = [("mine", "폐광", "EditSceneRender_mine"), ("inn", "주막", "EditSceneRender_inn"),
                 ("capital", "남문·도성 접근", "EditSceneRender_capital"),
                 ("overview", "전체 지형 진단", "TerrainDraft_overview")]
RECEIPTS = ["progress.json", "compact_audit.json", "grade_progress.json", "final_grade_attachments.json",
            "corridor_attachments.json", "road_physics_report.json", "road_physics_progress.json",
            "navigation_progress.json", "navigation_audit.json", "dressing_progress.json",
            "runtime_lifecycle_smoke.json", "relief_grid_candidate.json", "relief_context_progress.json",
            "terrain_normal_weld.json", "materials_report.json", "map_alignment_report.json",
            "fitted_route_measurements.json", "terrain_cliff_band_diagnosis.json", "route_grade_fit.json"]


def utc(value):
    if not value:
        return None
    try:
        parsed = datetime.fromisoformat(str(value).replace("Z", "+00:00"))
        return parsed.replace(tzinfo=timezone.utc) if parsed.tzinfo is None else parsed.astimezone(timezone.utc)
    except (ValueError, TypeError):
        return None


def number(value, digits=0):
    if value is None:
        return "자료 없음"
    return f"{value:,.{digits}f}"


def esc(value):
    return html.escape(str(value))


def route_length(points):
    return sum(math.dist(tuple(a[k] for k in "xyz"), tuple(b[k] for k in "xyz")) for a, b in zip(points, points[1:]))


def capture_reasons(meta, digest, cutoff, grade_hash):
    reasons = []
    if meta.get("scene") != TARGET_SCENE or meta.get("status") != "CAPTURED_EDIT_SCENE_REQUIRES_VISUAL_REVIEW":
        reasons.append("대상 씬·캡처 완료 기록 불일치")
    if not digest or meta.get("imageHash") != digest:
        reasons.append("이미지 SHA-256 불일치")
    if not meta.get("sourceSceneUnchanged"):
        reasons.append("원본 보존 확인 없음")
    taken = utc(meta.get("utc"))
    if taken is None or cutoff is None or taken.timestamp() < cutoff:
        reasons.append("현재 지형·부착물·시각 자산 갱신보다 오래된 캡처")
    if meta.get("gradeHash") and meta["gradeHash"] != grade_hash:
        reasons.append("경사 프로파일 해시 불일치")
    return reasons


class Evidence:
    def __init__(self, workspace):
        self.workspace = workspace.resolve()
        self.project = self.workspace / "Oheangbu"
        self.output = self.workspace / "Art/World/WorldMacro/Compact"
        self.warnings = []
        self.cache = {}
        self.receipts = {name: self.read(self.output / name) for name in RECEIPTS}

    def read(self, path):
        if not path.is_file():
            return {}
        try:
            data = json.loads(path.read_text(encoding="utf-8-sig"))
            if isinstance(data, dict):
                return data
            self.warnings.append(f"{path.name}: JSON 최상위 객체 아님")
        except (OSError, ValueError) as error:
            self.warnings.append(f"{path.name}: 읽기 실패 ({error})")
        return {}

    def file(self, name):
        if name.startswith("Assets/"):
            return self.project / name
        return self.output / name

    def digest(self, path):
        path = Path(path)
        if path in self.cache:
            return self.cache[path]
        if not path.is_file():
            return None
        with path.open("rb") as stream:
            digest = hashlib.file_digest(stream, "sha256").hexdigest()
        self.cache[path] = digest
        return digest

    def href(self, path):
        return quote(os.path.relpath(Path(path), self.output).replace(os.sep, "/"), safe="/._-#")

    def link(self, title, path, markdown=False):
        path = Path(path)
        if not path.exists():
            return f"{title} (아직 없음)" if markdown else f"{esc(title)} <small>아직 없음</small>"
        target = self.href(path)
        return f"[{title}]({target})" if markdown else f'<a href="{target}">{esc(title)}</a>'

    def receipt_link(self, name, title=None, markdown=False):
        return self.link(title or name, self.output / name, markdown)

    def stamps_match(self, rows):
        return bool(rows) and all(isinstance(row, dict) and row.get("path") and re.fullmatch(r"[0-9a-f]{64}", row.get("hash") or "") and self.digest(self.file(row["path"])) == row["hash"] for row in rows)

    def model(self):
        r = self.receipts
        progress, grade, audit = r["progress.json"], r["grade_progress.json"], r["compact_audit.json"]
        source_hash = self.digest(self.project / SOURCE_SCENE)
        profile_hash = self.digest(self.project / ASSET_FOLDER / "RoadGrade.asset")
        relief_hash = self.digest(self.project / ASSET_FOLDER / "Relief.asset") or ""
        mapping_hash = self.digest(self.project / ASSET_FOLDER / "Compression.asset")
        fit_hash = self.digest(self.output / "route_grade_fit.json")
        jobs = progress.get("meshes", [])
        terrain = grade.get("terrain", [])
        rebuilt = grade.get("rebuilt", [])
        rebuilt_files_valid = bool(rebuilt) and all(v.get("path") and (v.get("target") or "").startswith(ASSET_FOLDER + "/Meshes/") and self.file(v["target"]).is_file() for v in rebuilt)
        grade_complete = rebuilt_files_valid and bool(grade) and grade.get("phase") == "GRADED_TERRAIN_READY_FOR_PHYSICAL_AUDIT" and grade.get("nextTerrain") == len(terrain) and grade.get("retainedTerrain", 0) + len(terrain) == 88 and len({v.get("path") for v in rebuilt}) == 88 and grade.get("profileHash") == profile_hash and (grade.get("reliefHash") or "") == relief_hash and grade.get("fitHash") == fit_hash
        relief_meta = r["relief_grid_candidate.json"]
        relief_current = bool(grade_complete and relief_meta.get("readyForApply") and relief_meta.get("sourceHash") == source_hash and relief_meta.get("mappingHash") == mapping_hash and relief_meta.get("fitHash") == fit_hash and self.stamps_match([{"path": relief_meta.get("targetFile"), "hash": relief_meta.get("targetSha256")}, {"path": relief_meta.get("weightFile"), "hash": relief_meta.get("weightSha256")}]))
        originals = progress.get("assets", [])
        original_ok = bool(source_hash and originals) and source_hash == progress.get("sourceHash") and all(self.digest(self.file(v["source"])) == v.get("hash") for v in originals)
        # Capture sidecars currently lack dependency hashes. Their UTC must be newer than
        # every saved geometry/visual update below. This intentionally rejects old V3 art.
        visual_paths = [self.project / TARGET_SCENE, self.project / ASSET_FOLDER / "RoadGrade.asset", self.project / ASSET_FOLDER / "Relief.asset"]
        visual_paths += [self.file(j["result"]) for j in jobs if j.get("result")]
        visual_paths += [self.output / n for n in ("grade_progress.json", "final_grade_attachments.json", "relief_context_progress.json", "terrain_normal_weld.json", "materials_report.json", "dressing_progress.json", "corridor_attachments.json")]
        visual_times = [p.stat().st_mtime for p in visual_paths if p.is_file()]
        cutoff = max(visual_times) if visual_times else None
        audit_time = utc(audit.get("utc"))
        audit_pass = bool(audit.get("checks")) and all(c.get("status") == "PASS" for c in audit.get("checks", [])) and audit.get("sourceSceneUnchanged") and audit.get("originalCloneInputsUnchanged")
        audit_current = bool(audit_pass and grade_complete and audit_time and cutoff and audit_time.timestamp() >= cutoff and audit.get("sourceHash") == source_hash)
        road = max((r["road_physics_report.json"], r["road_physics_progress.json"]), key=lambda item: utc(item.get("updatedUtc")) or datetime.min.replace(tzinfo=timezone.utc))
        road_complete = road.get("nextRoute") == road.get("routeCount") == 37
        road_inputs = self.stamps_match(road.get("inputs"))
        road_time = utc(road.get("updatedUtc"))
        road_floor = max((p.stat().st_mtime for p in [self.output / "grade_progress.json"] + [self.file(x["target"]) for x in rebuilt if x.get("target")] if p.is_file()), default=0)
        road_current = bool(grade_complete and road_inputs and road_time and road_time.timestamp() >= road_floor)
        nav, nav_audit = r["navigation_progress.json"], r["navigation_audit.json"]
        nav_current = bool(grade_complete and nav.get("gradeHash") == profile_hash and nav.get("fitHash") == fit_hash and nav.get("mappingHash") == mapping_hash and ((nav.get("relief") or {}).get("assetHash") or "") == relief_hash)
        nav_done = nav.get("status") == "BAKED" and nav.get("next") == 20 and len(nav.get("surfaces", [])) == 20 and all(s.get("baked") and (s.get("targetData") or "").startswith(ASSET_FOLDER + "/Navigation/") and self.file(s["targetData"]).is_file() and s.get("sourceHash") == self.digest(self.file(s.get("sourceData", ""))) for s in nav.get("surfaces", []))
        nav_audit_file = self.output / "navigation_audit.json"
        nav_progress_file = self.output / "navigation_progress.json"
        nav_audit_current = bool(nav_current and nav_done and nav_audit_file.is_file() and nav_progress_file.is_file() and nav_audit_file.stat().st_mtime >= nav_progress_file.stat().st_mtime and nav_audit.get("scene") == TARGET_SCENE)
        dressing = r["dressing_progress.json"]
        fine = (dressing.get("fingerprint") or "").startswith("compact-physics-v2-fine2m-check1m:")
        dressing_done = bool(fine and dressing.get("published") and dressing.get("stage") == "Complete")
        dressing_time = utc(dressing.get("finishedUtc"))
        dressing_current = bool(dressing_done and grade_complete and dressing_time and dressing_time.timestamp() >= road_floor)
        attachments = r["final_grade_attachments.json"]
        attachments_current = bool(grade_complete and attachments.get("gradeHash") == profile_hash and (attachments.get("reliefHash") or "") == relief_hash and attachments.get("mappingHash") == mapping_hash and attachments.get("sourceSceneHash") == source_hash)
        smoke = r["runtime_lifecycle_smoke.json"]
        smoke_time = utc(smoke.get("startedUtc"))
        smoke_current = bool(smoke and smoke.get("sourceHash") == source_hash and smoke.get("compactSceneHash") == self.digest(self.project / TARGET_SCENE) and grade_complete and smoke_time and cutoff and smoke_time.timestamp() >= cutoff)
        captures = []
        for view, label, stem in CAPTURE_NAMES:
            image = self.output / "Captures" / (stem + ".png")
            meta = self.read(image.with_suffix(".json"))
            reasons = capture_reasons(meta, self.digest(image), cutoff, profile_hash) if image.is_file() and meta else ["이미지 또는 대응 JSON 없음"]
            if not grade_complete:
                reasons.append("현재 지형 재구성 미완료")
            captures.append(dict(view=view, label=label, image=image, meta=meta, reasons=reasons, current=not reasons, diagnostic=view == "overview"))
        measurements, fit = r["fitted_route_measurements.json"], r["route_grade_fit.json"]
        source_km = sum(v.get("sourceMetres", 0) for v in measurements.get("routes", [])) / 1000 or None
        compact_km = sum(route_length(v.get("points", [])) for v in fit.get("routes", [])) / 1000 or None
        content = (audit.get("content") or [{}])[0]
        stages = content.get("campaignStages", [])
        return dict(progress=progress, grade=grade, grade_complete=grade_complete, audit=audit, audit_pass=audit_pass, audit_current=audit_current,
                    source_hash=source_hash, original_ok=original_ok, source_assets=len(originals), source_km=source_km, compact_km=compact_km,
                    stages=len(stages), implemented=sum(bool(s.get("implemented")) for s in stages), content=content, road=road, road_complete=road_complete, road_current=road_current,
                    nav=nav, nav_audit=nav_audit, nav_current=nav_current, nav_done=nav_done, nav_audit_current=nav_audit_current, dressing=dressing, dressing_done=dressing_done, dressing_current=dressing_current,
                    attachments=attachments, attachments_current=attachments_current, smoke=smoke, smoke_current=smoke_current, captures=captures,
                    cutoff=cutoff, relief=r["relief_grid_candidate.json"], relief_current=relief_current, cliff=r["terrain_cliff_band_diagnosis.json"], warnings=self.warnings)


def sections(e, m):
    a, g, road, nav, dressing, smoke = (m[k] for k in ("audit", "grade", "road", "nav", "dressing", "smoke"))
    now = datetime.now(timezone.utc).isoformat(timespec="seconds")
    checks = a.get("checks", [])
    pass_count = sum(c.get("status") == "PASS" for c in checks)
    fails = sum(c.get("status") == "FAIL" for c in checks)
    records = []
    def add(title, paragraphs=(), rows=(), headings=(), links=()):
        records.append(dict(title=title, paragraphs=list(paragraphs), rows=list(rows), headings=list(headings), links=list(links)))
    road_stage = "현재 입력" if m["road_current"] else "이전/미확인 입력"
    add("현재 검토 상태", [f"생성 시각 {now}. 실제 JSON 기록을 읽어 만든 정적 스냅샷이며 자동 갱신되지 않습니다.",
        f"원본 보존 해시: {'일치' if m['original_ok'] else '미확인 또는 불일치'}. 현재 지형 재구성: {'완료 기록·입력 해시 일치' if m['grade_complete'] else '진행 중 또는 입력 기록 불일치'}.",
        f"도로 {road_stage}: {number(road.get('nextRoute'))}/{number(road.get('routeCount'))} 경로, 실패 표본 {number(road.get('totalFailedSamples'))}개. 내비게이션 {number(nav.get('next'))}/20, 식생 {dressing.get('stage','기록 없음')}, 실제 Play {smoke.get('status','미검증')}. 세부 기록의 완료 여부와 현재 입력 일치 여부는 아래에 함께 표시합니다.",
        "구조·물리·내비게이션·런타임 결과는 아래에서 각각 표시합니다. 직접 조작과 사용자 화면 승인은 별도이며, 이 문서는 플레이테스트 준비 완료나 최종 승인을 선언하지 않습니다."],
        links=[("progress.json", "변환 기록"), ("grade_progress.json", "현재 지형 진행")])
    add("4 × 6 km 적용 방식", ["수평 경계를 약 8 × 12 km에서 4 × 6 km로 줄였습니다. X −2,000…2,000 m, Z −3,000…3,000 m이며 직사각형 면적은 96→24 km², 75% 감소입니다.",
        "전체 씬에 0.5배 스케일을 적용하지 않았습니다. 건물·동굴·현장 콘텐츠의 크기와 저작된 도로 폭을 보존하고, 보호 구역 사이의 간격을 압축했습니다. 길 연결부의 높이·굽이와 주변 지형은 별도로 맞춥니다.",
        f"36개 저작 경로의 3D 중심선 합계: {number(m['source_km'],2)}→{number(m['compact_km'],2)} km. 겹치는 경로를 포함한 합계이며 고유 도로 길이나 실제 이동 시간 측정이 아닙니다.",
        "원본 W_Demo_Campaign.unity와 별도로 W_Demo_Compact.unity 및 WorldCompact 파생 자산을 사용합니다."],
        links=[("fitted_route_measurements.json", "원본 경로 길이·ID·폭"), ("route_grade_fit.json", "현재 전체 경로 좌표")])
    counts=[("객체 / Transform", a.get("sourceObjects"),a.get("targetObjects")), ("액터 / Encounter",a.get("sourceActors"),a.get("targetActors")),
            ("콘텐츠 포인트",a.get("sourceContentPoints"),a.get("targetContentPoints")), ("원본의 필수 참조",a.get("expectedSourceReferences"),a.get("matchedSourceReferences"))]
    add("콘텐츠 보존과 구조 감사", [f"최근 구조 기록: {a.get('status','없음')}, {pass_count}개 PASS / {fails}개 FAIL, 기록 시각 {a.get('utc','없음')}. {'현재 저장 결과와 시각이 맞습니다.' if m['audit_current'] else '현재 지형·시각 자산 변경 후의 구조 재검사가 아직 확인되지 않았습니다. 아래 수량은 이 구조 기록의 값입니다.'}",
        f"캠페인 {m['stages']}단계 중 원래 구현 표시가 있던 {m['implemented']}단계를 유지합니다. ID·트리거·필수 적 목록의 구조 보존과 실제 이벤트 동작 성공은 별도입니다. 상호작용 {number(m['content'].get('interactions'))}, 체크포인트 {number(m['content'].get('checkpoints'))}.",
        f"보호 렌더 구역 {number(a.get('comparedRenderZones'))}개, 해석 가능한 Transform {number(a.get('resolvedTransformScalesChecked'))}개를 검사했습니다. 미해결 원본 Transform {number(a.get('unresolvedOriginalTransforms'))}개까지 같은 검사를 끝냈다고 주장하지 않습니다.",
        f"원본 씬 및 복제 입력 {m['source_assets']}개 파일의 현재 SHA-256 {'일치' if m['original_ok'] else '미확인/불일치'}. 원본 씬 SHA-256: {m['source_hash'] or '없음'}"],
        rows=[(name,number(src),number(dst)) for name,src,dst in counts],headings=["항목","원본","축소본 기록"],links=[("compact_audit.json","구조 감사"),("original_reference_manifest.json","원본 ID·참조 목록")])
    numeric=m['relief'].get('numericComparison',{});cliff=m['cliff'];before=cliff.get('edgeCounts',{}).get('mappedOver75');after=numeric.get('edgesOver75')
    reduction=100*(1-after/before) if before and after is not None else None
    add("채택한 지형 완화장", [f"후보의 source/mapping/fit 및 높이·가중치 바이너리 해시가 현재 입력과 {'일치합니다' if m['relief_current'] else '일치하는지 확인되지 않았습니다; 아래 수치는 해당 후보 기록만 설명합니다'}.", f"원본 메시 모서리 경사의 p95: 원본 {number(cliff.get('sourceEdgeSlope',{}).get('p95'),2)}° → 수평 압축 직후 {number(cliff.get('pureMappedEdgeSlope',{}).get('p95'),2)}° → 채택 완화장 계산 {number(numeric.get('allOriginalEdgeGrade',{}).get('p95'),2)}°.",
        f"75° 초과 모서리: {number(before)}→{number(after)}, {number(reduction,1)}% 감소. 원본 모서리 대응을 사용한 계산이며 최종 세분화 삼각형·콜라이더·차량 주행의 통과 수치가 아닙니다.",
        f"보호·수변 높이를 보존하는 완화장이고, 실제 메시 반영은 현재 {number(g.get('nextTerrain'))}/{len(g.get('terrain',[]))}개 작업 + 유지 {number(g.get('retainedTerrain',0))}개입니다. 최대 절토·성토 후보 {number(numeric.get('maximumCutMetres'),1)} m / {number(numeric.get('maximumFillMetres'),1)} m로 지형 형상 변화가 커서 화면 검토가 필요합니다.",
        "콘텐츠 보호 구역과 수변에서 유지된 급경사, 도로 연결부는 별도 물리 검사 대상으로 남습니다. 위 높이장 계산과 저장된 실제 메시의 검사는 구분하며, 최종 통행이나 외형 합격을 의미하지 않습니다."],links=[("relief_grid_candidate.json","채택 완화장 입력·수치"),("actual_relief_mesh_verification.json","별도 저장 메시 검사"),("relief_water_weight_validation.json","수변 보호 검사"),("terrain_cliff_band_diagnosis.json","원본·압축 지형 비교")])
    road_label="현재 입력과 일치" if m['road_current'] else "현재 지형 판정에 사용할 수 없는 이전 또는 미확인 기록"
    add("도로 물리", [f"{road_label}. 상태 {road.get('status','기록 없음')}, {number(road.get('nextRoute'))}/{number(road.get('routeCount'))} 경로 처리, 기록 시각 {road.get('updatedUtc','없음')}.",
        f"표본 {number(road.get('totalSamples'))}개, 실패 표본 {number(road.get('totalFailedSamples'))}개, 개별 문제 {number(road.get('totalIssues'))}개. {'전체 경로 표본 집계가 끝났습니다.' if m['road_complete'] else '전체 경로 집계가 끝나지 않았습니다.'} 실패가 남아 있으면 통과로 표시하지 않습니다.",
        "실제 Terrain_ 및 명시된 다리·동굴 지지면을 약 3 m 간격으로 중심·양쪽 가장자리에서 측정합니다. 차량 종단·횡단 기준 14°, 보행 30°. 원본 보호 구역 문제도 유지하며 원본 중심선 기울기를 원본 실제 물리로 간주하지 않습니다.",
        f"실행 오류: {road.get('error') or '현재 error 필드 비어 있음'}. 과거 실행·재시도 메모 {len(road.get('executionNotes',[]))}개. 네이티브 주행 검증: {'기록됨' if road.get('nativeTraversalVerified') else '미검증'}.",
        f"부착물: {m['attachments'].get('status','기록 없음')}; 현재 지형 의존성 {'일치' if m['attachments_current'] else '미확인/불일치'}, 리본 지지면 누락 {number(m['attachments'].get('missingRoadSupport'))}개."],
        rows=[(r.get('id',''),number(r.get('failedSamples')),number(r.get('heightMismatches')),number(r.get('longitudinalExceedances')),number(r.get('crossSlopeExceedances'))) for r in sorted(road.get('routes',[]),key=lambda x:x.get('failedSamples',0),reverse=True)[:6] if r.get('failedSamples')],
        headings=["실패가 많은 경로 (최대 6개)","실패 표본","높이","종단","횡단"],links=[("road_physics_report.json","도로 측정 전체 기록"),("road_physics_progress.json","진행·실행 메모"),("final_grade_attachments.json","부착물·리본 지지면"),("ROAD_FINAL_TRIAGE.md","남은 구간의 원인·수정 순서"),("road_physics_v2_baseline.json","이전 V2 기준 — 현재 판정 아님")])
    add("식생·내비게이션", [f"식생 {dressing.get('stage','없음')}: {number(dressing.get('processedCells'))}/{number(dressing.get('totalCells'))} 셀 처리, 게시 {number(dressing.get('publishedCells'))}개. {'2 m 정밀 높이 / 독립 1 m 검사 방식' if (dressing.get('fingerprint') or '').startswith('compact-physics-v2') else '16 m 높이 v1 또는 미확인 예비 결과'}. 현재 지형 완료 결과와 {'일치' if m['dressing_current'] else '일치 확인 안 됨'}.",
        f"허공·물가·오차 제외: 지지면 누락 {number(dressing.get('missingGroundTiles'))}, 높이 오차 {number(dressing.get('inaccurateHeightTiles'))} 타일. 최종 밀도·접지·화면 품질은 별도 검토합니다.",
        f"내비게이션 상태 {nav.get('status','없음')}, {number(nav.get('next'))}/20개 베이크. 원본 NavData 해시 및 별도 출력 20개·완료 표시 {'확인' if m['nav_done'] else '미완료/미확인'}, 현재 경사·완화장·경로 해시 {'일치' if m['nav_current'] else '불일치/미확인'}.",
        f"내비게이션 감사 {m['nav_audit'].get('status','없음')}; 검사 기록의 액터 {number(m['nav_audit'].get('actors'))}, 링크 {number(m['nav_audit'].get('links'))}, 발견 사항 {len(m['nav_audit'].get('findings',[]))}개. 이 감사 기록은 현재 베이크와 {"일치" if m["nav_audit_current"] else "일치 미확인"}; compact NavData 참조 {m["nav_audit"].get("allSurfaceDataIsCompact", "미검증")}, 20/28/37 인벤토리 {m["nav_audit"].get("inventoryMatches", "미검증")}. Edit 샘플·경로 검사와 실제 전투·호위 행동은 별도입니다."],links=[("dressing_progress.json","식생 진행"),("dressing_coarse_baseline.json","이전 식생 v1 기준"),("navigation_progress.json","20개 베이크·입력 해시"),("navigation_audit.json","액터·순찰·링크 검사"),("NAVIGATION_REPAIR.md","호송 연결 보정과 남은 구간"),("navigation_seam_repairs.json","적용·복원·원본 보존 증거")])
    health=smoke.get('health') or {}
    add("실제 Play 수명주기와 남은 조작 확인", [f"런타임 기록: {smoke.get('status','실행 결과 없음')}. 현재 저장 씬과 시간·해시 {'일치' if m['smoke_current'] else '일치 확인 안 됨'}. 시작 확인 {smoke.get('startupVerified','미검증')}, 제한 관찰 완료 {smoke.get('boundedObservationCompleted','미검증')}.",
        f"실제 관찰 건강 상태: 세션 {health.get('session','없음')}, 진행 데이터 {health.get('progressValid','없음')}, 모터 {health.get('motor','없음')}, 카메라 {health.get('camera','없음')}, 지도/UI {health.get('mapUsable','없음')}, NavMesh에 연결된 액터 {number(health.get('actorsBoundToNavMesh'))}/{number(health.get('actors'))}.",
        f"저장 namespace 분리 {smoke.get('saveNamespaceIsolated','미검증')}, 이전 저장 불변 {smoke.get('existingSavesUnchanged','미검증')}, 진단 접미사 복원 {smoke.get('suffixRestored','미검증')}. 오류 로그 {number(smoke.get('loggedErrors'))}; SaveError {health.get('saveError') or '기록 없음/빈 값'}; LoadStatus {health.get('loadStatus') or '기록 없음'}.",
        f"관찰된 화면은 {health.get('uiPage') or '기록 없음'}, 시간 배율 {health.get('timeScale','미기록')}, 게임 입력 차단 {health.get('gameplayInputBlocked','미기록')} 상태입니다. 시작 안내로 게임이 정지된 경우 동작 중 검사로 해석하지 않습니다.",
        f"프레임 관찰 {number(smoke.get('observedFrames'))}개, 평균 {number(smoke.get('meanFrameMilliseconds'),2)} ms / p95 {number(smoke.get('p95FrameMilliseconds'),2)} ms. Editor의 시작 상태 관찰이며 이동 중 성능, GPU 또는 배포 빌드 성능 측정이 아닙니다.",
        "이전 검사에서 보고서 파일의 원자적 교체가 IOException으로 중단된 기록을 보존했습니다. 검사 기록 저장에만 짧은 재시도 2회를 추가했으며, 게임 로직을 바꾸거나 실패 기록을 삭제하지 않았습니다.",
        f"직접 입력 검증: {'기록됨 — 원 보고서 범위 확인 필요' if smoke.get('nativeInputVerified') else '미검증'}. 이동·차량 호출·운전·하차·전투·죽음·휴식·저장 이어하기는 별도 증거 없이는 완료로 보지 않습니다. 사용자 최종 화면 승인도 대기 중입니다."],links=[("runtime_lifecycle_smoke.json","실제 Play 시작·종료·건강 상태"),("runtime_lifecycle_smoke_observer_io_failure.json","보완 전 검사 기록 저장 오류")])
    add("화면에서 확인한 보완점", ["폐광 암반의 각진 이음, 주막 마당과 주변 지형의 경계, 일부 급한 봉우리와 황경 진입부의 빈 바닥이 남아 있습니다. 크기·좌표·구조 보존을 최종 환경 아트 합격으로 해석하지 않습니다. 정지 이미지의 원본 대비 회귀 원인은 별도 비교 없이는 확정하지 않았습니다."], links=[("VISUAL_NOTES.md","실제 이미지 확인 범위와 보완점"),("actual_relief_mesh_verification.json","저장된 지형 메시 검증"),("actual_relief_boundary_verification.json","지형 청크 공유 경계 검증")])
    if m['warnings']:
        add("읽기 경고",m['warnings'])
    return now, records


CSS = """*{box-sizing:border-box}body{margin:0;background:#f4f3ed;color:#24352c;font:16px/1.75 system-ui,'Malgun Gothic',sans-serif}header,main,footer{max-width:1120px;margin:auto;padding:28px}header{padding-top:48px}h1{font-size:44px;line-height:1.25;margin:10px 0}h2{font-size:25px;margin:0 0 18px}p{margin:10px 0;overflow-wrap:anywhere}.tag{font-size:13px;color:#7a542e}.banner{padding:16px 28px;background:#263b31;color:#fff}.muted,small{color:#67746b}section{padding:28px;background:#fffefa;border:1px solid #d9ddd1;margin:24px 0}table{border-collapse:collapse;width:100%;font-size:14px}th,td{text-align:left;border-bottom:1px solid #dce1d7;padding:12px}th{background:#eef1e7}.table{overflow:auto}a{color:#275740;text-underline-offset:3px}.links{display:flex;flex-wrap:wrap;gap:12px;font-size:13px;margin-top:18px}.images{display:grid;grid-template-columns:1fr 1fr;gap:22px}figure{margin:0}img{display:block;width:100%;height:auto;border:1px solid #bbc8ba}figcaption{font-size:13px;padding-top:9px;color:#526154}.pending{border:1px dashed #abb7a6;padding:20px;background:#f1f3ec}.files{display:flex;gap:15px;flex-wrap:wrap}footer{font-size:12px}@media(max-width:720px){header,main,footer{padding:18px}h1{font-size:32px}.images{grid-template-columns:1fr}section{padding:20px}}@media print{body{background:white}section{break-inside:avoid}.banner{background:white;color:black;border-bottom:2px solid black}a{color:black}}"""


def render(e, m, source_overview=None):
    now, records = sections(e, m)
    out = ['<!doctype html><html lang="ko"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>오행부 4 × 6 km 월드 검토</title><style>'+CSS+'</style></head><body>',
           '<div class="banner">실제 기록 기반 검토 자료 · 직접 조작 및 사용자 화면 승인 대기</div>',
           '<header><div class="tag">OHEANGBU / WORLD COMPACT</div><h1>4 × 6 km 월드 검토</h1><p>콘텐츠의 크기를 보존하며 연결 공간과 지형을 조정한 별도 월드입니다.</p><small>'+esc(now)+' · 오프라인 정적 문서</small></header><main>']
    md = ["# 4 × 6 km 월드 검토", "", f"생성 시각: {now}", "", "**실제 기록 기반 검토 자료입니다. 직접 조작·사용자 화면 승인은 대기 중이며 최종 성공이나 플레이테스트 준비 완료를 선언하지 않습니다.**", ""]
    for record in records:
        out.append('<section><h2>'+esc(record['title'])+'</h2>')
        md += ['## '+record['title'],'']
        for paragraph in record['paragraphs']:
            out.append('<p>'+esc(paragraph)+'</p>');md += [paragraph,'']
        if record['rows']:
            out.append('<div class="table"><table><thead><tr>'+''.join('<th>'+esc(v)+'</th>' for v in record['headings'])+'</tr></thead><tbody>')
            md += ['| '+' | '.join(record['headings'])+' |','| '+' | '.join('---' for _ in record['headings'])+' |']
            for row in record['rows']:
                out.append('<tr>'+''.join('<td>'+esc(v)+'</td>' for v in row)+'</tr>');md.append('| '+' | '.join(str(v).replace('|','\\|') for v in row)+' |')
            out.append('</tbody></table></div>');md.append('')
        out.append('<div class="links">'+''.join(e.receipt_link(n,t) for n,t in record['links'])+'</div></section>')
        if record['links']:
            md += [' · '.join(e.receipt_link(n,t,True) for n,t in record['links']),'']
    out.append('<section><h2>현재 세대 캡처</h2><p>이미지 해시와 기록 시각을 현재 저장 지형·부착물·재질·식생 갱신 시각에 대조합니다. 캡션은 이미지 밖에 표시합니다. 새 캡처도 사용자 시각 승인을 의미하지 않습니다.</p><div class="images">')
    md += ['## 현재 세대 캡처','','이미지 SHA-256·씬 기록·UTC를 현재 저장 지형과 대조합니다. 오래된 캡처는 최종 이미지로 사용하지 않습니다.','']
    for cap in m['captures']:
        if cap['current']:
            caption=cap['label']+' — '+('거리 wash를 잠시 끈 전체 지형 진단. 실제 기본 화면의 색·대기 효과 판정용이 아닙니다.' if cap['diagnostic'] else '실제 Edit 씬 카메라 렌더. 네이티브 Game View·게임플레이 검증이 아닙니다.')+' 촬영 '+str(cap['meta'].get('utc',''))+'; 사용자 검토 대기.'
            out.append('<figure><img src="'+e.href(cap['image'])+'" alt="'+esc(cap['label'])+'" loading="lazy"><figcaption>'+esc(caption)+'</figcaption></figure>')
            md += ['!['+cap['label']+']('+e.href(cap['image'])+')','',caption,'']
        else:
            why='; '.join(cap['reasons']);out.append('<div class="pending"><strong>'+esc(cap['label'])+' · 재촬영/준비 필요</strong><p>'+esc(why)+'</p>'+e.link('기존 캡처 기록',cap['image'].with_suffix('.json'))+'</div>')
            md += ['- '+cap['label']+': 재촬영/준비 필요 — '+why,'']
    out.append('</div></section>')
    if source_overview and source_overview.is_file():
        caption='원본 8 × 12 km의 이전 촬영 자료입니다. 카메라 위치·구도가 달라 현재 화면과 일대일 비교하거나 최종 축소본 승인에 사용하지 않습니다.'
        out.append('<section><h2>이전 원본 참고</h2><figure><img src="'+e.href(source_overview)+'" alt="이전 원본 월드 참고"><figcaption>'+esc(caption)+'</figcaption></figure></section>')
        md += ['## 이전 원본 참고','',caption,'',e.link('이전 원본 개요',source_overview,True),'']
    files=[('축소 씬',e.project/TARGET_SCENE),('원본 씬',e.project/SOURCE_SCENE),('축소 자산 폴더',e.project/ASSET_FOLDER),('RoadGrade.asset',e.project/ASSET_FOLDER/'RoadGrade.asset'),('Relief.asset',e.project/ASSET_FOLDER/'Relief.asset'),('현재 Geo',e.project/ASSET_FOLDER/'Data/01_WorldMacroSheet.asset'),('현재 콘텐츠',e.project/ASSET_FOLDER/'Data/03_Content.asset'),('현재 지도',e.project/ASSET_FOLDER/'Data/04_Map.asset')]
    out.append('<section><h2>Unity 파일과 문서</h2><div class="files">'+''.join(e.link(t,p) for t,p in files)+'</div><p>'+'<a href="REPORT.md">Markdown 보고서</a>'+'</p></section></main><footer>정적 로컬 문서 · 외부 요청·JavaScript·fetch 없음 · 실제 JSON 기록과 캡처 범위를 우선합니다.</footer></body></html>')
    md += ['## Unity 파일과 문서','']+['- '+e.link(t,p,True) for t,p in files]+['','정적 로컬 문서입니다. 외부 요청·JavaScript·fetch 없이 열 수 있습니다.']
    return '\n'.join(out), '\n'.join(md)+'\n'


def self_test():
    meta=dict(scene=TARGET_SCENE,status='CAPTURED_EDIT_SCENE_REQUIRES_VISUAL_REVIEW',imageHash='good',sourceSceneUnchanged=True,utc='2030-01-01T00:00:02Z')
    cutoff=utc('2030-01-01T00:00:01Z').timestamp()
    assert not capture_reasons(meta,'good',cutoff,'grade')
    assert capture_reasons(meta,'bad',cutoff,'grade')
    assert capture_reasons({**meta,'utc':'2029-12-31T23:59:59Z'},'good',cutoff,'grade')
    assert capture_reasons({**meta,'sourceSceneUnchanged':False},'good',cutoff,'grade')
    assert capture_reasons({**meta,'gradeHash':'old'},'good',cutoff,'grade')
    assert capture_reasons({**meta,'scene':SOURCE_SCENE},'good',cutoff,'grade')
    assert route_length([dict(x=0,y=0,z=0),dict(x=3,y=4,z=0)])==5
    assert utc('2026-09-15T15:08:29.9960851Z').tzinfo==timezone.utc
    assert esc('<script>')=='&lt;script&gt;'
    assert utc('2030-01-01T00:00:00-05:00') == utc('2030-01-01T05:00:00Z')
    assert utc('2030-01-01T00:00:00') == utc('2030-01-01T00:00:00Z')
    assert utc('invalid') is None
    print('PASS: 12 generator checks; stale/mismatched captures rejected; no review files written.')


def main():
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--workspace',type=Path,default=Path(__file__).resolve().parents[2])
    parser.add_argument('--check',action='store_true',help='Read current evidence/freshness without writing documents.')
    parser.add_argument('--allow-incomplete',action='store_true',help='Explicitly write an unfinished draft while grade is incomplete.')
    parser.add_argument('--self-test',action='store_true')
    parser.add_argument('--source-overview',type=Path,help='Optional older 8x12 overview, captioned as a different-camera reference.')
    args=parser.parse_args()
    if args.self_test:
        self_test();return 0
    e=Evidence(args.workspace);m=e.model()
    summary={k:m[k] for k in ('grade_complete','original_ok','relief_current','audit_current','road_current','nav_done','nav_current','nav_audit_current','attachments_current','dressing_current','smoke_current')}
    summary['captures']=[dict(view=c['view'],current=c['current'],reasons=c['reasons']) for c in m['captures']]
    summary['warnings']=m['warnings']
    if args.check:
        print(json.dumps(summary,ensure_ascii=False,indent=2));return 0
    if not m['grade_complete'] and not args.allow_incomplete:
        print('Refused: current grade is incomplete or its hashes/88-chunk coverage do not match. Existing REVIEW.html/REPORT.md were not overwritten. Use --check to inspect, or --allow-incomplete for an explicitly unfinished draft.',file=sys.stderr);return 2
    html_text,md_text=render(e,m,args.source_overview.resolve() if args.source_overview else None)
    e.output.mkdir(parents=True,exist_ok=True)
    for name,text in [('REVIEW.html',html_text),('REPORT.md',md_text)]:
        path=e.output/name;temporary=path.with_suffix(path.suffix+'.tmp');temporary.write_text(text,encoding='utf-8');os.replace(temporary,path)
    print(json.dumps(dict(status='STATIC_REVIEW_GENERATED_NOT_FINAL_APPROVAL',files=[str(e.output/'REVIEW.html'),str(e.output/'REPORT.md')],evidence=summary),ensure_ascii=False,indent=2));return 0


if __name__=='__main__':
    raise SystemExit(main())
