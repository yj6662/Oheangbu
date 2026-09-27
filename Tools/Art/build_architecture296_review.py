"""Build the local Korean #296 review from evidence already on disk.

Does not capture Unity, mutate assets, or turn missing/incomplete checks into PASS.
Root may run this after captures and checks settle. --check reads without writing.
"""
from __future__ import annotations

import argparse
import csv
import hashlib
import html
import io
import json
import math
import re
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
DEFAULT = ROOT / "Art/World/Compact/Rebuild/Architecture296"
REGIONS = [("cheongrim", "청림"), ("jeokro", "적로"), ("cheolong", "철옹"),
           ("hyeongang", "현강"), ("hwanggyeong", "황경"), ("bridge", "교량"), ("gate", "남문·성벽")]
# Full build only writes build.txt; individual command receipts can remain from a
# previous run. Actual generated ledgers also cover partial authoring commands.
GENERATION = ("build.txt", "resume-build.txt", "venue-placements.json", "venue-complexes.json", "venue-assets.json", "Legacy/replacements.json",
              "crossings.json", "crossing-materials.txt", "gates.json", "Landscape/clearance.json",
              "Surface/surface-materials.json", "map.json", "kaesong-link-refresh.txt", "BridgeApproach/jeokro__cheolong.json", "BridgeApproach/hyeongang__temple.json")
PHYSICS_GENERATION = ("native-colliders.txt", "gate-collision.txt")
VERIFICATION_EVIDENCE = (
    ("Analysis/final-verification-progress.json", "순차 검증 진행·완료·복원 기록"),
    ("reproduction-check.txt", "전체 생성 재현 대조"),
    ("Analysis/SupportLayout/latest.json", "물리 지원 메시 직렬화·형상 보존"),
    ("Analysis/MaterialSerialization/latest.json", "재질 메타데이터 정규화·시각값 보존"),
    ("Analysis/final-architecture-visual-review.json", "지역별 직접 시각 검토·남은 한계"),
    ("Analysis/FINAL-ARCHITECTURE-VISUAL-REVIEW.md", "21개 화면별 관찰·남은 미술 한계"),
    ("Analysis/gate-infill-final-visual-review.json", "남문 닫힘·열림 시각 검토"),
)


def read_json(path: Path):
    if not path.is_file():
        return None
    try:
        return json.loads(path.read_text(encoding="utf-8-sig"))
    except (OSError, UnicodeError, ValueError):
        return None


def read_text(path: Path) -> str:
    return path.read_text(encoding="utf-8-sig") if path.is_file() else ""


def sha(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def stamp(path: Path):
    return datetime.fromtimestamp(path.stat().st_mtime, timezone.utc).isoformat() if path.is_file() else None


def safe_number(value):
    return value if isinstance(value, (int, float)) and not isinstance(value, bool) and math.isfinite(value) else None


def status_json(data, runtime=False):
    if not isinstance(data, dict):
        return "pending", "대기 · 결과 파일 없음 또는 읽기 실패"
    if runtime:
        failures = data.get("Failures", data.get("failures", []))
        checks = data.get("Checks", data.get("checks", []))
        reported = str(data.get("Status", data.get("status", ""))).upper()
        if failures or reported == "FAIL":
            return "fail", f"실패 {len(failures)} · 기록 {len(checks)}"
        if data.get("Active", data.get("active")) or not data.get("Finished", data.get("finished")) or not data.get("Restored", data.get("restored")):
            return "pending", f"진행·복원 확인 대기 · {data.get('Status', data.get('status')) or '상태 미기록'}"
        if not checks:
            return "pending", "대기 · 완료된 검사 행 없음"
        if reported != "PASS":
            return "pending", "완료 기록 있음 · 명시적 PASS 상태 확인 대기"
        return "pass", f"PASS · 기록 {len(checks)} · Play 시작 {data.get('PlayStarts', data.get('playStarts', 0))}회"
    failed = data.get("passed", data.get("Passed"))
    checks = data.get("checks", data.get("Checks", []))
    if failed is False:
        return "fail", "FAIL · 원문 확인"
    if failed is True:
        return "pass", f"PASS · 검사 {len(checks)}" if isinstance(checks, list) and checks else "PASS · 결과 파일 확인"
    return "pending", "기록 있음 · PASS 판정 필드 없음"


def status_text(text: str):
    passed = len(re.findall(r"^PASS(?:\s|$)", text, re.M))
    failed = len(re.findall(r"^FAIL(?:\s|$)", text, re.M))
    if failed:
        return "fail", f"{passed} PASS · {failed} FAIL"
    reproduction = re.search(r"^PASS deterministic regeneration: entries=(\d+) changed/missing/added=(\d+)\s*$", text, re.M)
    if reproduction:
        count, changed = map(int, reproduction.groups())
        return ("pass" if changed == 0 else "fail"), f"{count:,}개 대조 · 변경·누락·추가 {changed:,}개"
    if passed:
        return "pass", f"{passed} PASS · 0 FAIL"
    return "pending", "대기 · 검사 결과 없음"


def status_complex(data):
    if isinstance(data, dict) and data.get("version") == 2:
        assets = data.get("assets", [])
        if not assets:
            return "pending", "대기 · 전각 원본별 검사 없음"
        for asset in assets:
            levels = asset.get("reducedLevels", [])
            collision = asset.get("collision", {})
            if len(levels) != 2 or not collision or not asset.get("near", {}).get("triangles"):
                return "pending", "대기 · 원형 근경·중원경·충돌 자료 미완료"
            if not asset.get("sourcePrefabSha256") or asset.get("sourcePrefabSha256") != asset.get("expectedSourcePrefabSha256"):
                return "fail", "FAIL · 전각 원본 SHA 불일치"
            if any(level.get("finite") is not True or level.get("degenerate") != 0 or level.get("invalidIndices", 0) != 0 for level in levels + [collision]):
                return "fail", "FAIL · 비유한 좌표·퇴화면·유효하지 않은 인덱스"
        return "pass", f"PASS · 원본 {len(assets)}개 / 중원경 {len(assets)*2}개 / 충돌 {len(assets)}개 · 배치·보행 별도"
    if not isinstance(data, list) or not data:
        return "pending", "대기 · 전각 원본별 검사 없음"
    levels = [level for row in data if isinstance(row, dict) for level in row.get("levels", [])]
    if not levels or any(not isinstance(row, dict) or len(row.get("levels", [])) != 3 for row in data):
        return "pending", "대기 · 원본별 3개 LOD 검사 미완료"
    if any(level.get("finite") is not True or level.get("degenerate") != 0 for level in levels):
        return "fail", "FAIL · 비유한 좌표 또는 퇴화면 기록 확인"
    return "pass", f"PASS · 원본 {len(data)}개 / LOD {len(levels)}개 · 배치·보행 별도"


def same_view(a, b):
    if not isinstance(a, dict) or not isinstance(b, dict) or a.get("Id") != b.get("Id"):
        return False
    try:
        return all(abs(float(a[k][axis]) - float(b[k][axis])) < 1e-5
                   for k in ("Eye", "Target") for axis in ("x", "y", "z"))
    except (KeyError, TypeError, ValueError):
        return False


def capture_data(folder: Path):
    cameras = read_json(folder / "cameras.json") or {}
    views = cameras.get("Views", [])
    if not views:
        views = [{"Id": f"{realm}-{suffix}", "Label": f"{label} {name}", "Scale": scale}
                 for realm, label in REGIONS[:5]
                 for suffix, name, scale in (("overview", "조감", "overview"), ("entry", "진입", "medium"), ("eye", "눈높이", "eye"))]
        views += [{"Id": "south-gate", "Label": "황경 남문과 성벽", "Scale": "medium"}]
    output = []
    for index, view in enumerate(views):
        key = view["Id"]
        group = "bridge" if key.startswith("bridge-") else "gate" if key.startswith("south-gate") else key.split("-")[0].lower()
        row = {"id": key, "label": view.get("Label", key), "scale": view.get("Scale", ""), "group": group,
               "expectedCamera": view, "before": None, "after": None}
        for stage in ("before", "after"):
            expected = folder / "Captures" / stage / f"{index:02d}-{key}.png"
            alternatives = sorted((folder / "Captures" / stage).glob(f"*-{key}.png")) if expected.parent.exists() else []
            path = expected if expected.is_file() else alternatives[0] if len(alternatives) == 1 else expected
            if not path.is_file():
                continue
            meta_path = path.with_suffix(path.suffix + ".json")
            metadata = read_json(meta_path)
            row[stage] = {"path": path.relative_to(folder).as_posix(), "sha256": sha(path), "modifiedUtc": stamp(path),
                          "metadataPath": meta_path.relative_to(folder).as_posix() if meta_path.is_file() else None,
                          "metadata": metadata}
        before = (row["before"] or {}).get("metadata") or {}
        after = (row["after"] or {}).get("metadata") or {}
        row["samePose"] = bool(row["before"] and row["after"] and same_view(before.get("View"), after.get("View")) and same_view(after.get("View"), view))
        row["sameTerrainRecorded"] = bool(before.get("HeightSHA256") and before.get("HeightSHA256") == after.get("HeightSHA256") and
                                          before.get("WaterSHA256") and before.get("WaterSHA256") == after.get("WaterSHA256"))
        latest = max((p.stat().st_mtime for name in GENERATION if (p := folder / name).is_file()), default=0)
        row["afterStale"] = bool(row["after"] and (folder / row["after"]["path"]).stat().st_mtime + 2 < latest)
        output.append(row)
    return output


def checks_data(folder: Path):
    definitions = [
        ("기존 원본·293·295 파일 보존", "protection-check.json", "json"),
        ("새 구조·지면·수면·경로 지지", "checks.txt", "text"),
        ("교량·성문 원장 좌표·공유 경로 대조 (오프라인)", "Analysis/bridge-gate-ledger-alignment.json", "json"),
        ("성벽 둘레·남문 개폐 충돌", "gate-checks.txt", "text"),
        ("실제 Edit 캡슐의 벽·문 통과 및 가마 배치", "gate-mobility.txt", "text"),
        ("철옹·황경 실제 실내 문폭·Edit 캡슐 왕복", "interior-entrances.txt", "text"),
        ("첫 방문 경로의 Edit 캡슐 보행", "walk-first-visit.txt", "text"),
        ("길찾기 연결", "navigation.txt", "text"),
        ("문 갱신 이후 길찾기 표본·접촉 진단", "nav-diagnostics.txt", "text"),
        ("식생 컬링 행렬·순서·집계", "culling.txt", "text"),
        ("후보 생성 반복성", "reproduction-check.txt", "text"),
        ("지면 셰이더 정적·수치 검사", "Surface/offline-checks.json", "json"),
        ("선택 재질의 전체 Unity 셰이더 pass", "Surface/shader-preflight.json", "json"),
        ("실내·교량·성문 통로 식생 제거 독립 대조", "Landscape/independent-checks.json", "json"),
        ("실제 Play 입력·전투 API·저장·남문", "Runtime/checks.json", "runtime"),
        ("현강 실양안 뭄 표면·왕복·저장·익사·낙사", "Mum/hyeongang-play-checks.json", "runtime"),
        ("실제 가마·교량 지지·발길 전용 제외", "Vehicle/checks.json", "runtime"),
        ("적로–철옹 전폭 차량 접근로·원지형 보존 (오프라인)", "BridgeApproach/independent-checks.json", "json"),
        ("현강 사찰 마지막 접근로·기존 교량 접합 (오프라인)", "BridgeApproach/hyeongang__temple-independent-checks.json", "json"),
        ("전체 LOD 경계·참조점·renderer", "lod-bounds.txt", "text"),
        ("전각 근경 원형·중원경·충돌 메시 검사", "Complex/validation.json", "complex"),
        ("전각 메시 검사 입력 SHA 재대조·기존 결과 재사용", "Complex/reuse-check.json", "json"),
        ("기존 사찰 기단·목재 더미·등롱 보존 독립 대조", "Legacy/independent-checks.json", "json"),
    ]
    rows = []
    for label, relative, kind in definitions:
        path = folder / relative
        data = read_json(path) if kind != "text" else None
        state, summary = status_text(read_text(path)) if kind == "text" else status_complex(data) if kind == "complex" else status_json(data, runtime=kind == "runtime")
        dependencies = []
        if relative in ("checks.txt", "walk-first-visit.txt", "navigation.txt", "nav-diagnostics.txt", "reproduction-check.txt", "Runtime/checks.json", "Mum/hyeongang-play-checks.json", "Vehicle/checks.json", "lod-bounds.txt"):
            dependencies = [folder / name for name in GENERATION + PHYSICS_GENERATION]
            if relative in ("walk-first-visit.txt", "nav-diagnostics.txt", "Runtime/checks.json"):
                dependencies.append(folder / "navigation.txt")
        elif relative in ("gate-checks.txt", "gate-mobility.txt"):
            dependencies = [folder / "gates.json", folder / "gate-collision.txt"]
        elif relative == "interior-entrances.txt":
            dependencies = [folder / "venue-placements.json", folder / "venue-complexes.json"]
        elif relative == "culling.txt":
            dependencies = [folder / "Landscape/clearance.json", folder / "cameras.json", folder / "Surface/surface-materials.json"]
        elif relative.startswith("Surface/"):
            dependencies = list((ROOT / "Oheangbu/Assets/_Project/Art/World/Architecture296/Shaders").glob("*.hlsl"))
            dependencies += list((ROOT / "Oheangbu/Assets/_Project/Art/World/Architecture296/Shaders").glob("*.shader"))
        elif relative.startswith("Landscape/"):
            dependencies = [folder / name for name in ("Landscape/clearance.json", "crossings.json", "gates.json", "venue-placements.json", "venue-complexes.json")]
        elif relative == "Complex/validation.json":
            dependencies = [folder / "Complex/Reduced/receipts.json", folder / "Complex/Collision/receipts.json"]
        elif relative == "Complex/reuse-check.json":
            dependencies = [folder / "Complex/validation.json", folder / "Complex/Reduced/receipts.json", folder / "Complex/Collision/receipts.json"]
        elif relative.startswith("Legacy/"):
            dependencies = [folder / "Legacy/replacements.json"]
        stale = [p.name for p in dependencies if p.is_file() and path.is_file() and p.stat().st_mtime > path.stat().st_mtime + 2]
        if relative.startswith("BridgeApproach/") and relative.endswith("independent-checks.json") and isinstance(data, dict):
            inputs = data.get("inputs", {})
            stale = [name for name, value in inputs.items() if not (folder / "BridgeApproach" / name).is_file() or sha(folder / "BridgeApproach" / name) != value]
            if not inputs and state == "pass":
                state, summary = "pending", "기록 있음 · 접근로 입력 SHA 대조 근거 없음"
        if relative == "Analysis/bridge-gate-ledger-alignment.json" and isinstance(data, dict):
            inputs = data.get("inputs", {})
            stale = [name for name, record in inputs.items() if not (folder / name).is_file() or sha(folder / name) != record.get("sha256")]
            if not inputs and state == "pass":
                state, summary = "pending", "기록 있음 · 입력 SHA 대조 근거 없음"
        if stale and state == "pass":
            state, summary = "pending", "이전 " + summary + " · 생성 변경 후 재검증 대기"
        if relative in ("walk-first-visit.txt", "nav-diagnostics.txt"):
            request = read_json(folder / "traversal-status.json") or {}
            expected_command = "walk" if relative == "walk-first-visit.txt" else "nav-diagnose"
            if state == "pass" and "INFO Open-gate baked NavData snapshot." not in read_text(path):
                state, summary = "pending", "기록 있음 · 열린 문 정적 Nav 질의 범위 확인 대기"
            if request.get("Command") == expected_command:
                if request.get("Active"):
                    state, summary = "pending", "검사 진행 중 · " + str(request.get("Status", ""))
                elif request.get("Finished") and (request.get("Error") or not request.get("Restored")):
                    state, summary = "fail", "FAIL · 비동기 검사 오류 또는 문 상태 복원 실패"
        rows.append({"label": label, "path": relative if path.is_file() else None, "expectedPath": relative,
                     "status": state, "summary": summary, "modifiedUtc": stamp(path), "newerInputs": stale})
    return rows


def performance_data(folder: Path):
    rows = []
    perf = folder / "Perf"
    for path in sorted(perf.glob("**/frame-times.txt")) if perf.exists() else []:
        if "History" in path.relative_to(perf).parts:
            continue
        text = read_text(path)
        row = {"scenario": path.parent.relative_to(perf).as_posix(), "source": path.relative_to(folder).as_posix(),
               "environment": "Unity Editor Play", "modifiedUtc": stamp(path), "device": "", "raw": text}
        for key, label in (("cpu", "CPU FrameTiming"), ("gpu", "GPU FrameTiming"), ("frame", "Editor frame interval")):
            match = re.search(re.escape(label) + r":\s*samples=(\d+)\s+median_ms=([\d.]+)\s+p95_ms=([\d.]+)", text)
            row[key + "Samples"] = int(match[1]) if match else 0
            row[key + "MedianMs"] = float(match[2]) if match and int(match[1]) > 0 else None
            row[key + "P95Ms"] = float(match[3]) if match and int(match[1]) > 0 else None
        device = re.search(r"^Device:\s*(.+)", text, re.M)
        row["device"] = device[1] if device else "미기록"
        walk = path.parent / "walk-play.txt"
        row["walkStatus"], row["walkSummary"] = status_text(read_text(walk))
        row["walkSource"] = walk.relative_to(folder).as_posix() if walk.is_file() else None
        row["provenance"] = read_json(path.parent / "provenance.json")
        rows.append(row)
    for path in sorted(perf.glob("**/*.json")) if perf.exists() else []:
        if "History" in path.relative_to(perf).parts:
            continue
        if path.name in ("performance-summary.json", "review-data.json"):
            continue
        value = read_json(path)
        if not isinstance(value, dict) or "cpuMedianMs" not in value or "gpuMedianMs" not in value:
            continue
        row = {"scenario": path.stem, "source": path.relative_to(folder).as_posix(), "environment": value.get("environment", "측정 JSON · 원문 환경 확인"),
               "modifiedUtc": stamp(path), "device": value.get("device", value.get("gpuDevice", "미기록")), "raw": json.dumps(value, ensure_ascii=False, indent=2)}
        for channel in ("cpu", "gpu", "frame"):
            samples = value.get(channel + "Samples", value.get("samples", 0))
            row[channel + "Samples"] = samples
            for metric in ("MedianMs", "P95Ms"):
                row[channel + metric] = safe_number(value.get(channel + metric)) if isinstance(samples, int) and samples > 0 else None
        row["walkStatus"], row["walkSummary"], row["walkSource"] = "pending", "보행 판정 별도", None
        rows.append(row)
    for row in rows:
        source = folder / row["source"]
        row["newerInputs"] = [name for name in GENERATION + PHYSICS_GENERATION
                              if (path := folder / name).is_file() and path.stat().st_mtime > source.stat().st_mtime + 2]
        row["stale"] = bool(row["newerInputs"])
    return rows


def provenance_data(documents):
    sources = (documents.get("architecture.json") or {}).get("Sources", [])
    credit_values = [safe_number(row.get("MeshyCredits")) for row in sources]
    known = bool(sources) and all(value is not None and value >= 0 for value in credit_values)
    return {"sources": sources, "sourceCount": len(sources),
            "meshyRecordedCredits": sum(credit_values) if known else None,
            "meshyScope": "architecture.json Sources에 명시된 기록 합계; 누락된 항목은 0으로 간주하지 않음",
            "meshyBudgetCredits": 100, "budgetSpec": "../../../../../Docs/Specs/SPEC-ARCHITECTURE-296.md"}


def vehicle_data(data):
    if not isinstance(data, dict):
        return None
    drives = data.get("Drives", [])
    fields = ("Id", "Pass", "Closed", "Seconds", "Metres", "PathMetres", "MaximumCrossTrack", "MaximumTilt",
              "MaximumStep", "Frames", "WheelSamples", "BridgeWheelSamples", "GateContacts", "Detail")
    return {"scope": data.get("Scope"), "driveCount": len(drives),
            "passedDrives": sum(row.get("Pass") is True for row in drives),
            "failedDrives": sum(row.get("Pass") is False for row in drives),
            "drives": [{key: row.get(key) for key in fields} for row in drives]}


def collect(folder: Path):
    documents = {name: read_json(folder / name) for name in ("architecture.json", "venue-placements.json", "venue-assets.json", "venue-complexes.json", "crossings.json", "gates.json",
        "Landscape/clearance.json", "Surface/surface-materials.json", "protection-check.json", "Runtime/checks.json", "art-review.json")}
    placement_audit = folder / "Analysis/compound-placement-audit.json"
    placement_source = folder / "venue-complexes.json"
    placement_stale = placement_audit.is_file() and placement_source.is_file() and placement_audit.stat().st_mtime + 2 < placement_source.stat().st_mtime
    audit = read_text(folder / "audit.txt")
    inventory = re.search(r"Architecture inventory: (\d+) owned entries / (\d+) scene assemblies; source material repairs=(\d+), grounded living footing pieces=(\d+), unresolved missing slots=(\d+)", audit)
    assembly_audit = dict(zip(("ownedEntries", "sceneAssemblies", "materialRepairs", "footingPieces", "unresolvedSlots"), map(int, inventory.groups()))) if inventory else None
    captures = capture_data(folder)
    visual_review = read_json(folder / "Analysis/final-architecture-visual-review.json") or {}
    reviewed_images = visual_review.get("Images", [])
    current_images = {row["after"]["path"]: row["after"]["sha256"] for row in captures if row["after"]}
    visual_current = bool(current_images and len(reviewed_images) == len(current_images) and
                          len({row.get("Image") for row in reviewed_images}) == len(current_images) and
                          all(row.get("DirectViewed") is True and current_images.get(row.get("Image")) == row.get("SHA256")
                              for row in reviewed_images))
    return {"revision": "architecture-296", "generatedUtc": datetime.now(timezone.utc).isoformat(),
            "captures": captures, "checks": checks_data(folder), "performance": performance_data(folder), "documents": documents,
            "visualReview": visual_review, "visualReviewMatchesCurrentImages": visual_current,
            "provenance": provenance_data(documents),
            "vehicle": vehicle_data(read_json(folder / "Vehicle/checks.json")),
            "assemblyAudit": assembly_audit, "placementAuditStale": placement_stale,
            "verificationEvidence": [{"label": label, "path": name if (folder / name).is_file() else None,
                                      "expectedPath": name, "modifiedUtc": stamp(folder / name)}
                                     for name, label in VERIFICATION_EVIDENCE],
            "generation": [{"path": name, "modifiedUtc": stamp(folder / name), "sha256": sha(folder / name)} for name in GENERATION if (folder / name).is_file()],
            "extraEvidence": {name: stamp(folder / name) for name in ("build.txt", "resume-build.txt", "audit.txt", "interior-entrances.txt", "Legacy/replacements.json", "crossing-materials.txt", "CrossingLods/lod-audit.json", "CrossingLods/lods.json", "lod-bounds.txt", "Vehicle/checks.json", "Mum/hyeongang-play-checks.json", "gate-mobility.json", "native-colliders.txt", "Complex/validation.json", "Complex/Reduced/receipts.json", "Complex/Collision/receipts.json", "Complex/IMPLEMENTATION-EVIDENCE.md", "Analysis/compound-placement-audit.json", "Analysis/interior-visual-review.json", "Analysis/final-architecture-visual-review.json", "BridgeApproach/jeokro__cheolong.json", "BridgeApproach/hyeongang__temple.json", "BridgeApproach/hyeongang__temple-join.json", "BridgeApproach/FailureEvidence/manifest.json", "Landscape/retained-reviewed-tree.json")}}


def esc(value):
    return html.escape(str(value), quote=True)


def source_link(path, label="원문"):
    return f'<a href="{esc(path)}">{esc(label)}</a>' if path else '<span class="pending">대기</span>'


def number(value, suffix=""):
    return "대기" if value is None else f"{value:,.2f}{suffix}" if isinstance(value, float) else f"{value:,}{suffix}"


def render(data):
    docs = data["documents"]
    arenas = (docs["architecture.json"] or {}).get("Arenas", [])
    venues = (docs["venue-placements.json"] or {}).get("Venues", [])
    bridges = (docs["crossings.json"] or {}).get("Bridges", [])
    loops = (docs["gates.json"] or {}).get("Loops", [])
    landscape = docs["Landscape/clearance.json"] or {}
    surface = docs["Surface/surface-materials.json"] or {}
    protection = docs["protection-check.json"] or {}
    human = docs["art-review.json"] or {}
    provenance = data["provenance"]
    assembly = data["assemblyAudit"] or {}
    vehicle = data["vehicle"] or {}
    vehicle_rows = []
    for drive in vehicle.get("drives", []):
        state = "PASS" if drive.get("Pass") is True else "FAIL" if drive.get("Pass") is False else "대기"
        vehicle_rows.append(f'<tr><td>{esc(drive.get("Id"))}<small>{esc(drive.get("Detail"))}</small></td><td>{state}</td><td>{number(drive.get("Metres"))} / {number(drive.get("PathMetres"))}</td><td>{number(drive.get("WheelSamples"))} / {number(drive.get("BridgeWheelSamples"))}</td><td>{number(drive.get("GateContacts"))}</td></tr>')
    if not vehicle_rows:
        vehicle_rows.append('<tr><td colspan="5" class="pending">실제 WheelCollider 주행 원장 대기</td></tr>')
    source_rows = []
    for source in provenance["sources"]:
        url = source.get("Url", "")
        link = f'<a href="{esc(url)}" target="_blank" rel="noopener">공식 출처</a>' if url.startswith(("https://", "http://")) else '외부 URL 미기록'
        source_rows.append(f'<tr><td>{esc(source.get("AssetPath", source.get("Id", "미기록")))}</td><td>{esc(source.get("Publisher", "미기록"))}<br>{link}</td><td>{esc(source.get("License", "미기록"))}</td><td>{esc(source.get("Use", "미기록"))}</td></tr>')
    if not source_rows:
        source_rows.append('<tr><td colspan="4" class="pending">출처·라이선스 원장 대기</td></tr>')
    # Never infer manual approval from captures, automatic tests, or an authoring receipt.
    manual = "수동 미술 검토 대기"
    if human.get("reviewer") and human.get("status") in ("approved", "changes_requested"):
        manual = ("수동 검토 승인" if human["status"] == "approved" else "수동 수정 요청") + " · " + str(human["reviewer"])
    completed = sum(bool(r["before"] and r["after"]) for r in data["captures"])
    checked = sum(r["samePose"] for r in data["captures"])
    status_rows = "".join(f'<tr><td>{esc(r["label"])}</td><td class="{r["status"]}">{esc(r["summary"])}</td><td>{source_link(r["path"])}<small>{esc(r["modifiedUtc"] or r["expectedPath"])}</small></td></tr>' for r in data["checks"])
    verification_links = "".join(source_link(row["path"], row["label"]) for row in data.get("verificationEvidence", []))
    visual = data.get("visualReview") or {}
    visual_labels = {"court-plinth-repetition": "마당과 기단", "interior-style": "실내 반복",
                     "bridge-structure": "교량 접근과 지지부", "entry-visibility": "진입 장면의 한계", "gate-arch": "남문 이음과 그림자"}
    visual_findings = "".join(f'<li><strong>{esc(visual_labels[row["Id"]])}</strong> — {esc(row.get("Observation", ""))}</li>'
                              for row in visual.get("Findings", []) if row.get("Id") in visual_labels)
    visual_status = (f'{len(visual.get("Images", []))}개 후보 화면 직접 검토 · 현재 PNG SHA 일치'
                     if data.get("visualReviewMatchesCurrentImages") else "현재 후보 화면과 직접 시각 검토 기록의 대조 대기")
    if not visual_findings:
        visual_findings = '<li class="pending">직접 시각 검토의 구체적인 관찰 기록 대기</li>'
    arena_rows = "".join(f'<tr><td>{esc(a.get("Label", a.get("Id")))}</td><td>{number(a.get("ClearSize", {}).get("x"))} × {number(a.get("ClearSize", {}).get("y"))}m</td><td>{"실내" if a.get("Interior") else "마당"} · {"후반 시험 공간" if a.get("TestOnly") else "기존 조우 연결"}</td><td>{esc(a.get("PlaceId", ""))}</td></tr>' for a in arenas)
    if not arena_rows:
        arena_rows = '<tr><td colspan="4" class="pending">지역별 생성 원장 대기</td></tr>'
    perf_rows = "".join(f'<tr><td>{esc(r["scenario"])}<small>{esc(r["environment"])} · {esc(r["device"])}</small><small class="pending">{"생성 변경 후 재측정 대기" if r.get("stale") else ""}</small></td><td>{number(r.get("cpuMedianMs"))} / {number(r.get("cpuP95Ms"))}</td><td>{number(r.get("gpuMedianMs"))} / {number(r.get("gpuP95Ms"))}</td><td>{r.get("cpuSamples", 0):,} / {r.get("gpuSamples", 0):,}</td><td>{source_link(r["source"])}<small class="{r["walkStatus"]}">{esc(r["walkSummary"])}</small></td></tr>' for r in data["performance"])
    if not perf_rows:
        perf_rows = '<tr><td colspan="5" class="pending">실제 성능 측정 파일 대기. 값이 없을 때 0ms로 표시하지 않는다.</td></tr>'
    evidence_links = []
    for name, label in (("venue-placements.json", "지역 배치·접근"), ("venue-assets.json", "사용 부재·라이선스"), ("venue-complexes.json", "주전각·회랑·연결 동선"), ("crossings.json", "4교량·5경로"),
                        ("gates.json", "성벽 둘레·문"), ("Landscape/clearance.json", "식생 제거 변환 원장"), ("Surface/surface-materials.json", "지면 적용 재질")):
        evidence_links.append(source_link(name if docs[name] is not None else None, label))
    for name, label in (("build.txt", "전체 생성 실행 기록"), ("resume-build.txt", "중간 저장 후 생성 재개 기록"),
                        ("audit.txt", "보유 건축 전수 조사·누락 재질"), ("interior-entrances.txt", "실내 문폭·Edit 캡슐 직접 왕복"),
                        ("Legacy/replacements.json", "기존 사찰 기단·목재 교체 및 등롱 보존"),
                        ("crossing-materials.txt", "교량 재질 변경·보존 기록"), ("CrossingLods/lod-audit.json", "교량 LOD 감사"),
                        ("lod-bounds.txt", "LOD 전체 경계 검사"), ("Vehicle/checks.json", "실제 가마 회귀"), ("Mum/hyeongang-play-checks.json", "현강 뭄 회귀"),
                        ("gate-mobility.json", "벽·문·가마 배치 전수 좌표"), ("native-colliders.txt", "근경 외형 유지·독립 충돌 메시 적용"),
                        ("Complex/validation.json", "전각 LOD 정적 검사"), ("Complex/Reduced/receipts.json", "전각 축약 SHA·삼각형"),
                        ("Complex/Collision/receipts.json", "전각 독립 충돌 SHA·삼각형"),
                        ("Complex/IMPLEMENTATION-EVIDENCE.md", "전각 출처·라이선스·검증 범위"),
                        ("Analysis/compound-placement-audit.json", "이전 전각 좌표 감사 · 배치 변경 후 갱신 대기" if data["placementAuditStale"] else "전각 배치·원지형 좌표 감사"),
                        ("Analysis/interior-visual-review.json", "두 실내 시점의 한정된 결함 검토 · 전체 미술 승인 별도"),
                        ("Analysis/final-architecture-visual-review.json", "지역별 직접 미술 검토 · 남는 한계 포함"),
                        ("BridgeApproach/jeokro__cheolong.json", "실측 차량 실패 후 전폭 노면·교대 교정 원장"),
                        ("BridgeApproach/hyeongang__temple.json", "현강 사찰 마지막 55.25m 전폭 교정 원장"),
                        ("BridgeApproach/hyeongang__temple-join.json", "현강 앞 86구간 물리 보존·55.25m 접합 원장"),
                        ("History/third-vehicle-run/checks.json", "국부 교정 전 3회차: 적로 양방향 성공·현강 정방향 실패"),
                        ("Complex/reuse-check.json", "전각 기하 검사의 동일 입력 SHA 재사용 확인"),
                        ("BridgeApproach/FailureEvidence/manifest.json", "수정 전 실제 차량 실패 보존 근거"),
                        ("Landscape/retained-reviewed-tree.json", "전경 수목 보존·3D 오판 정정 근거")):
        evidence_links.append(source_link(name if data["extraEvidence"].get(name) else None, label))
    library = []
    for key, label in REGIONS:
        cards = []
        for row in data["captures"]:
            if row["group"] != key:
                continue
            picture = row["after"] or row["before"]
            media = f'<img loading="lazy" src="{esc(picture["path"])}" alt="{esc(row["label"])}">' if picture else '<span class="empty">캡처 대기</span>'
            state = "전후 있음" if row["after"] and row["before"] else "후보 화면만 있음" if row["after"] else "이전 화면만 있음" if row["before"] else "미생성"
            cards.append(f'<button class="thumb" data-view="{esc(row["id"])}">{media}<span>{esc(row["label"])}<small>{state}</small></span></button>')
        if cards:
            library.append(f'<section class="contact" id="region-{key}"><h3>{label}</h3><div class="thumbs">'+"".join(cards)+"</div></section>")
    payload = json.dumps({"captures": data["captures"], "regions": REGIONS}, ensure_ascii=False).replace("<", "\\u003c")
    return f'''<!doctype html>
<html lang="ko"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>오행부 #296 · 다섯 강토 검토</title>
<style>
*{{box-sizing:border-box}}:root{{color-scheme:light;--paper:#eeeae1;--ink:#23382f;--line:#bac1b5;--quiet:#56655a}}body{{margin:0;background:var(--paper);color:var(--ink);font:16px/1.7 "Malgun Gothic","Apple SD Gothic Neo",sans-serif}}main{{max-width:1840px;margin:auto;padding:30px clamp(16px,3vw,60px) 80px}}header{{display:flex;align-items:end;justify-content:space-between;gap:30px;margin-bottom:24px}}h1{{font-size:clamp(27px,3vw,45px);line-height:1.3;font-weight:500;margin:7px 0 8px}}h2{{font-size:26px;font-weight:500;margin:48px 0 10px}}h3{{font-size:21px;font-weight:500;margin:24px 0 10px}}p{{margin:10px 0;max-width:1150px}}small,.quiet{{color:var(--quiet);font-size:12px}}a{{color:#315c49;text-underline-offset:3px}}button{{font:inherit;border:1px solid var(--line);padding:8px 14px;background:transparent;color:var(--ink);cursor:pointer}}button:hover{{border-color:#315c49}}button[aria-pressed=true]{{color:#f4f0e7;background:#2c4839;border-color:#2c4839}}button:focus-visible,a:focus-visible{{outline:3px solid #a47036;outline-offset:3px}}nav,.modes{{display:flex;gap:7px;flex-wrap:wrap;margin:12px 0}}#view-nav button{{font-size:14px;padding:5px 12px}}.viewer{{position:relative;aspect-ratio:16/9;width:100%;background:#d6d9cf;isolation:isolate;overflow:hidden}}.viewer img{{position:absolute;inset:0;object-fit:contain;width:100%;height:100%;background:#d6d9cf}}#before{{clip-path:inset(0 50% 0 0)}}.tag{{position:absolute;top:15px;background:#172f24d9;color:#fff;padding:4px 11px;font-size:13px;z-index:3}}.tag.left{{left:15px}}.tag.right{{right:15px}}.empty-main{{position:absolute;inset:0;display:grid;place-items:center;color:#536456}}.controls{{display:grid;grid-template-columns:1fr auto;gap:20px;align-items:center}}input[type=range]{{width:100%;accent-color:#315c49}}.caption{{display:flex;justify-content:space-between;gap:20px;flex-wrap:wrap;font-size:14px;min-height:30px}}.caption a{{margin-left:12px}}.section-rule{{border-top:1px solid var(--line);margin-top:42px;padding-top:4px}}.thumbs{{display:grid;grid-template-columns:repeat(3,1fr);gap:14px}}.thumb{{padding:0;text-align:left;overflow:hidden;display:block}}.thumb img,.thumb .empty{{width:100%;aspect-ratio:16/9;object-fit:cover;display:grid;place-items:center;background:#d9ddd2}}.thumb>span:last-child{{display:block;padding:8px 12px}}.thumb small,td small{{display:block}}.summary{{display:grid;grid-template-columns:repeat(3,1fr);gap:24px;margin:22px 0}}.summary>div{{border-top:1px solid var(--line);padding-top:10px}}.summary strong{{display:block;font-weight:500;font-size:22px}}table{{width:100%;border-collapse:collapse;font-size:14px}}th,td{{text-align:left;padding:12px 10px 12px 0;border-bottom:1px solid var(--line);vertical-align:top}}th{{font-weight:500;color:var(--quiet)}}.table-scroll{{overflow:auto}}.pending{{color:#6d684f}}.fail{{color:#a13b25}}.pass{{color:#2c6346}}details{{border-bottom:1px solid var(--line);padding:12px 0}}summary{{cursor:pointer}}.links{{display:flex;gap:17px;flex-wrap:wrap;margin:20px 0}}.note{{border-left:3px solid #83937d;padding-left:14px}}pre{{white-space:pre-wrap;word-break:break-word;font-size:12px;max-height:350px;overflow:auto}}footer{{border-top:1px solid var(--line);margin-top:46px;padding-top:18px}}[hidden]{{display:none!important}}@media(max-width:800px){{header{{display:block}}.thumbs{{grid-template-columns:1fr 1fr}}.summary{{grid-template-columns:1fr}}.controls{{grid-template-columns:1fr}}h2{{font-size:22px}}.modes{{margin:0 0 8px}}}}@media(max-width:500px){{.thumbs{{grid-template-columns:1fr}}}}
</style></head><body><main>
<header><div><small>오행부 · ARCHITECTURE 296 · TEST</small><h1>다섯 강토의 건축과 자연 지면</h1><p>별도 후보 씬의 실제 화면과 검사 기록. 지역의 큰 형태, 통행 가능한 공간, 가까이서 읽히는 재료를 함께 검토한다.</p></div><div class="quiet">전후 화면 {completed}/{len(data['captures'])}쌍<br>같은 위치·방향 기록 {checked}쌍<br>{esc(manual)}</div></header>
<nav id="region-nav" aria-label="지역"></nav><nav id="view-nav" aria-label="카메라 시점"></nav>
<div class="viewer" id="viewer"><div class="empty-main" id="empty-main">캡처 대기</div><img id="after" alt="296 후보 화면" hidden><img id="before" alt="295 이전 화면" hidden><span class="tag left" id="before-tag">#295 이전</span><span class="tag right" id="after-tag">#296 후보</span></div>
<div class="controls"><input id="split" type="range" min="0" max="100" value="50" aria-label="이전 화면 비율"><div class="modes"><button data-mode="split" aria-pressed="true">전후 비교</button><button data-mode="before">이전</button><button data-mode="after">후보</button><button id="full">크게 보기</button></div></div>
<div class="caption"><span id="capture-state" aria-live="polite"></span><span id="capture-links"></span></div><p class="quiet">같은 시점 확인은 PNG 옆 기록의 위치·바라보는 점을 대조한다. 파일이 없는 화면과 검사는 대기로 표시한다. 이미지를 생성하거나 보정하지 않는다.</p>
<details><summary>지역별 모든 시점 펼치기</summary>{''.join(library)}</details>
<section class="section-rule"><h2>현재 후보에 들어간 것</h2><div class="summary"><div><small>지역 공간</small><strong>{len(venues) if venues else '대기'}개 배치 기록</strong><span>기존 조우와 후반 시험 공간의 역할을 원장으로 구분</span></div><div><small>물길과 통행</small><strong>{len(bridges) if bridges else '대기'}개 물리 교량 · {len(loops) if loops else '대기'}개 둘레 기록</strong><span>같은 경로를 공유하는 황경 석교와 저장에 연결된 문</span></div><div><small>원본 보존</small><strong>{number(protection.get('files'))}개 파일 대조</strong><span>원본·293·295 보호 결과는 아래 실제 검사에 표시</span></div></div>
<div class="table-scroll"><table><thead><tr><th>공간</th><th>명시적 전투면</th><th>성격</th><th>장소 ID</th></tr></thead><tbody>{arena_rows}</tbody></table></div><div class="links">{''.join(evidence_links)}</div>
<p>식생 원장: 제거 {number(landscape.get('Removed'))}개, 남은 기하 충돌 {number(landscape.get('RemainingConflicts'))}개. 지면 적용: 기본 지면 {number(surface.get('GroundMaterials'))}개, 자연 고지대 {number(surface.get('NaturalHighlandMaterials'))}개, 의도적 건축 재질 유지 {number(surface.get('PreservedHighlandConstructionMaterials'))}개. 건축 전수 조사 기록: 씬 조립물 {number(assembly.get('sceneAssemblies'))}개, 해결하지 못한 재질 슬롯 {number(assembly.get('unresolvedSlots'))}개. 파일이 없으면 수치를 추정하지 않는다.</p></section>
<section class="section-rule"><h2>직접 본 화면과 남은 미술 한계</h2><p>{esc(visual_status)}. 검토 시각: {esc(visual.get("ReviewedUtc", "미기록"))}.</p><ul>{visual_findings}</ul><p class="note">이 기록은 해당 정지 화면에서 본 결과다. 전체 미술 완성이나 사용자 승인을 뜻하지 않는다. {source_link("Analysis/FINAL-ARCHITECTURE-VISUAL-REVIEW.md" if visual else None, "21개 시점별 관찰과 한계 전문")}을 함께 확인한다.</p></section>
<section class="section-rule"><h2>사용 자산과 출처</h2><p>Meshy 원장 사용량 <strong>{number(provenance['meshyRecordedCredits'])} 크레딧</strong> / 승인 상한 {number(provenance['meshyBudgetCredits'])} 크레딧. <a href="architecture.json">출처 {provenance['sourceCount']}건의 사용 기록</a>과 <a href="{esc(provenance['budgetSpec'])}">예산 계약</a>을 구분한다. KCISA 보유 자산은 원래 배포 라이선스를 유지하며 Poly Haven CC0 자산과 구분한다.</p><details><summary>자산별 원본 경로·공식 출처·라이선스 펼치기</summary><div class="table-scroll"><table><thead><tr><th>원본</th><th>제공자·출처</th><th>라이선스</th><th>사용 범위</th></tr></thead><tbody>{''.join(source_rows)}</tbody></table></div></details></section>
<section class="section-rule"><h2>자동 검사와 플레이 증거</h2><div class="links">{verification_links}</div><p class="quiet">진행 원장은 명령별 완료·복원을 기록한다. 직렬화 정규화 원장은 실행 당시 형상·시각값 보존 범위이며, 최종 생성 재현과 플레이 판정은 각 검사 결과를 따른다. 시각 검토는 기록된 화면의 범위로 읽는다.</p><p class="note">검사 PASS는 해당 파일의 범위에 한정된다. Edit 캡슐 이동, 실제 Play의 가상 입력, 보스 사망 API, 사람이 직접 한 전투와 미술 승인은 서로 다른 검증이다. 최종 수동 미술 상태: <strong>{esc(manual)}</strong>.</p><div class="table-scroll"><table><thead><tr><th>검사</th><th>상태</th><th>실제 파일 · 갱신 UTC</th></tr></thead><tbody>{status_rows}</tbody></table></div><details><summary>실제 가마 주행 세부 기록 · {number(vehicle.get('driveCount'))}건</summary><p>기존 소환·탑승 API 이후 자동 조향을 입력하고 원래 Rigidbody·WheelCollider로 이동한 결과다. 사람이 직접 운전한 검증과 구분한다. 각 행은 해당 주행 결과이며 전체 완료·저장 복원 상태는 위 검사표를 따른다.</p><div class="table-scroll"><table><thead><tr><th>주행</th><th>결과</th><th>실이동 / 경로 m</th><th>바퀴 지지 / 교량 지지 표본</th><th>문 접촉</th></tr></thead><tbody>{''.join(vehicle_rows)}</tbody></table></div></details></section>
<section class="section-rule"><h2>성능 측정</h2><p>CPU와 GPU는 각각 median / p95, 단위 ms. 표본이 없으면 대기로 남긴다. Editor 결과에는 편집기 비용이 포함되며, 독립 빌드나 120fps 승인을 뜻하지 않는다. 측정 환경과 이동 경로는 원문을 함께 확인한다.</p><div class="table-scroll"><table><thead><tr><th>장면·환경</th><th>CPU ms</th><th>GPU ms</th><th>CPU / GPU 표본</th><th>원문·보행</th></tr></thead><tbody>{perf_rows}</tbody></table></div><p><a href="Perf/review-summary.csv">전체 측정 CSV</a></p></section>
<footer><p><a href="review-data.json">페이지 데이터·캡처 SHA</a> · <a href="cameras.json">카메라 원장</a> · <a href="architecture.json">공간·동선 원장</a> · <a href="Baseline/protected.json">원본 보호 기준</a></p><small>파일을 읽어 페이지를 만든 시각: {esc(data['generatedUtc'])}. 파일이 이후 갱신되면 생성기를 다시 실행한다.</small></footer>
</main><script id="review-payload" type="application/json">{payload}</script><script>
const data=JSON.parse(document.querySelector('#review-payload').textContent),views=data.captures;
const regionNav=document.querySelector('#region-nav'),viewNav=document.querySelector('#view-nav'),before=document.querySelector('#before'),after=document.querySelector('#after'),split=document.querySelector('#split');
let index=0,mode='split';
function media(img,item){{img.hidden=!item;if(item)img.src=item.path;else img.removeAttribute('src');}}
function paint(){{const row=views[index];if(!row)return;media(before,mode==='after'?null:row.before);media(after,mode==='before'?null:row.after);document.querySelector('#empty-main').hidden=!(mode==='before'?row.before:mode==='after'?row.after:row.before||row.after);before.style.clipPath=(mode==='split'&&row.after)?`inset(0 ${{100-split.value}}% 0 0)`:'none';split.disabled=mode!=='split'||!row.before||!row.after;document.querySelector('#before-tag').hidden=before.hidden;document.querySelector('#after-tag').hidden=after.hidden;document.querySelectorAll('[data-mode]').forEach(b=>b.setAttribute('aria-pressed',b.dataset.mode===mode));}}
function select(i,writeHash=true){{index=i;const row=views[i];if(!row)return;viewNav.replaceChildren();views.forEach((v,k)=>{{if(v.group!==row.group)return;const b=document.createElement('button');b.textContent=v.label;b.setAttribute('aria-pressed',k===i);b.onclick=()=>select(k);viewNav.appendChild(b);}});[...regionNav.children].forEach(b=>b.setAttribute('aria-pressed',b.dataset.region===row.group));let state=row.before&&row.after?(row.samePose?'기록된 위치·방향 일치':'전후 있음 · 동일 시점 확인 대기'):(row.after?'후보 화면 있음 · 이전 캡처 대기':row.before?'이전 화면 있음 · 후보 캡처 대기':'전후 캡처 대기');if(row.afterStale)state+=' · 후보 생성 변경 후 재촬영 대기';document.querySelector('#capture-state').textContent=row.label+' · '+state;const links=document.querySelector('#capture-links');links.replaceChildren();for(const stage of ['before','after']){{const item=row[stage];if(!item)continue;const a=document.createElement('a');a.href=item.path;a.target='_blank';a.rel='noopener';a.textContent=stage==='before'?'이전 원본':'후보 원본';links.appendChild(a);if(item.metadataPath){{const m=document.createElement('a');m.href=item.metadataPath;m.textContent='기록';links.appendChild(m);}}}}if(writeHash)history.replaceState(null,'','#'+encodeURIComponent(row.id));paint();}}
for(const [key,label] of data.regions){{if(!views.some(v=>v.group===key))continue;const b=document.createElement('button');b.dataset.region=key;b.textContent=label;b.onclick=()=>select(views.findIndex(v=>v.group===key));regionNav.appendChild(b);}}
document.querySelectorAll('[data-view]').forEach(b=>b.onclick=()=>{{select(views.findIndex(v=>v.id===b.dataset.view));document.querySelector('#region-nav').scrollIntoView({{behavior:'smooth'}});}});document.querySelectorAll('[data-mode]').forEach(b=>b.onclick=()=>{{mode=b.dataset.mode;paint();}});split.oninput=paint;document.querySelector('#full').onclick=()=>document.querySelector('#viewer').requestFullscreen?.();
function fromHash(){{const key=decodeURIComponent(location.hash.slice(1));let i=views.findIndex(v=>v.id===key);if(i<0)i=views.findIndex(v=>v.after);select(Math.max(0,i),false);}}window.addEventListener('hashchange',fromHash);fromHash();
</script></body></html>'''


def write_if_changed(path: Path, text: str):
    path.parent.mkdir(parents=True, exist_ok=True)
    if not path.exists() or path.read_text(encoding="utf-8-sig") != text:
        path.write_text(text, encoding="utf-8")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--folder", type=Path, default=DEFAULT)
    parser.add_argument("--check", action="store_true", help="read and build in memory without writing output")
    args = parser.parse_args()
    data = collect(args.folder)
    document = render(data)
    if args.check:
        print(f"Read-only build OK: {len(data['captures'])} views, {len(data['checks'])} checks, {len(data['performance'])} measured reports; {len(document):,} HTML characters")
        return
    write_if_changed(args.folder / "REVIEW.html", document)
    write_if_changed(args.folder / "review-data.json", json.dumps(data, ensure_ascii=False, indent=2) + "\n")
    columns = ["scenario", "environment", "device", "cpuSamples", "cpuMedianMs", "cpuP95Ms", "gpuSamples", "gpuMedianMs", "gpuP95Ms", "frameSamples", "frameMedianMs", "frameP95Ms", "walkStatus", "source", "modifiedUtc"]
    stream = io.StringIO(newline="")
    writer = csv.DictWriter(stream, fieldnames=columns, extrasaction="ignore")
    writer.writeheader()
    writer.writerows(data["performance"])
    write_if_changed(args.folder / "Perf/review-summary.csv", stream.getvalue())
    print(args.folder / "REVIEW.html")


if __name__ == "__main__":
    main()
