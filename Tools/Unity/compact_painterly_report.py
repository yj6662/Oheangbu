"""Build the Painterly review from completed Unity captures and measurement receipts.

No Unity calls or source-asset writes. Run with --check-only to validate inputs;
otherwise writes REPORT.md, REVIEW.html and summary.json in the capture folder.
"""
from __future__ import annotations

import argparse
import html
import json
import math
from pathlib import Path
import re
import statistics

from PIL import Image


ROOT = Path(__file__).resolve().parents[2]
DEFAULT_OUT = ROOT / "Art/World/WorldMacro/Compact/InkLandscape/Painterly"
NAMES = {
    "inn": "금표 주막 앞", "mountain_path": "산길 조망", "office": "시작 관청",
    "DeepForest": "청림 심부", "SouthGate": "황경 남문", "Jeokro": "적로",
    "Cheolong": "철옹", "Hyeongang": "현강 · 수변", "Hwanggyeong": "황경 분지",
    "ground_detail": "발밑 지면", "boundary_Hwanggyeong": "황경 경계",
    "boundary_Cheongrim": "청림 경계", "boundary_Jeokro": "적로 경계",
    "boundary_Cheolong": "철옹 경계", "boundary_Hyeongang": "현강 경계",
}
MIDDLE_VIEWS = ("inn", "mountain_path", "DeepForest")
PHASES = {
    "cold": "초기 생성", "stationary": "정지", "rotation": "회전",
    "first_visit": "첫 접근", "return_visit": "재방문", "returned_stationary": "복귀 정지",
}
PERF_METRICS = ("cpuP50", "cpuP95", "cpuP99", "gpuP50", "gpuP95", "gpuP99")
SUBMISSION_METRICS = ("drawCalls", "submittedInstances", "visibleTriangles", "billboardInstances")
GPU_LIMIT_MS = 0.5
CPU_TARGET_MS = 8.33


def read(out: Path, name: str):
    return json.loads((out / name).read_text(encoding="utf-8-sig"))


def escaped(value):
    return html.escape(str(value), quote=True)


def table(headers, rows, markdown=False):
    if markdown:
        return "\n".join([
            "|" + "|".join(headers) + "|", "|" + "|".join("---" for _ in headers) + "|",
            *("|" + "|".join(str(c).replace("|", "\\|").replace("\n", " ") for c in row) + "|" for row in rows),
        ])
    return '<div class="scroll"><table><thead><tr>' + "".join(
        f"<th>{escaped(h)}</th>" for h in headers
    ) + "</tr></thead><tbody>" + "".join(
        "<tr>" + "".join(f"<td>{escaped(c)}</td>" for c in row) + "</tr>" for row in rows
    ) + "</tbody></table></div>"


def choose_prefix(out, explicit, candidates, ids):
    if explicit:
        return explicit
    return next((prefix for prefix in candidates if all(
        (out / f"{prefix}_{view}.png").is_file() for view in ids
    )), candidates[0])


def aggregate_performance(before, after):
    """Medians of the two run statistics, deliberately not pooled percentiles."""
    runs = before + after
    if len(before) != 2 or len(after) != 2:
        raise ValueError("Before and after each require exactly two measured runs")
    reference = runs[0]
    for run in runs:
        if run.get("status") != "MEASURED_DIAGNOSTIC_CAMERA" or run.get("error"):
            raise ValueError("A performance run did not finish successfully")
        if (run.get("width"), run.get("height")) != (1920, 1080):
            raise ValueError("Performance runs must use the 1920×1080 comparison target")
        if run.get("start") != reference.get("start"):
            raise ValueError("Performance camera starting positions differ")
        for key in ("unityVersion", "gpuDevice", "cpuDevice", "mode"):
            if run.get(key) != reference.get(key):
                raise ValueError(f"Performance metadata differs: {key}")
        ids = [p.get("phase") for p in run["phases"]]
        if len(ids) != len(PHASES) or set(ids) != set(PHASES):
            raise ValueError("Missing or duplicated performance phases")
        for phase in run["phases"]:
            if phase.get("count", 0) <= 0:
                raise ValueError("An empty performance phase cannot establish a result")
            if any(not isinstance(phase.get(k), (int, float)) or not math.isfinite(phase[k])
                   or phase[k] < 0 for k in PERF_METRICS):
                raise ValueError("Missing/non-finite timing metric")
    indexed = [{p["phase"]: p for p in run["phases"]} for run in runs]
    result = []
    for phase in PHASES:
        old = {k: statistics.median(r[phase][k] for r in indexed[:2]) for k in PERF_METRICS}
        new = {k: statistics.median(r[phase][k] for r in indexed[2:]) for k in PERF_METRICS}
        delta = new["gpuP50"] - old["gpuP50"]
        result.append({
            "phase": phase, "before": old, "after": new, "gpuMedianDeltaMs": delta,
            "gpuIncreaseBelowLimit": delta < GPU_LIMIT_MS,
            "beforeRunStats": [r[phase] for r in indexed[:2]],
            "afterRunStats": [r[phase] for r in indexed[2:]],
        })
    return result


def submission_signature(receipt):
    # Material names change with the derived look; geometry and LOD submissions do not have to.
    keys = ("prototype", "category", "mesh", "shadowMode", "lod", "instances", "packets",
            "submesh", "trianglesPerInstance", "submittedTriangles", "alphaClip", "billboard",
            "fadeOverride", "fade")
    return sorted(json.dumps({k: row.get(k) for k in keys}, sort_keys=True)
                  for row in receipt.get("batches", []))


def validate_inputs(out, after_prefix=None, middle_prefix=None):
    if not (out / "views.json").is_file():
        raise ValueError("views.json is missing")
    views = read(out, "views.json")["views"]
    ids = [v["id"] for v in views]
    if len(ids) != 15 or len(set(ids)) != 15 or set(ids) != set(NAMES):
        raise ValueError("Expected the 15 fixed views, without duplicates")
    after_prefix = choose_prefix(out, after_prefix, ("after", "final"), ids)
    middle_prefix = choose_prefix(out, middle_prefix, ("atmosphere", "middle", "air"), MIDDLE_VIEWS)
    captures = [("before", i) for i in ids] + [(after_prefix, i) for i in ids]
    captures += [(middle_prefix, i) for i in MIDDLE_VIEWS]
    required = ["checks.json", "distance_response.json"] + [
        f"performance_{side}_{n}.json" for side in ("before", "after") for n in (1, 2)
    ] + [f"{prefix}_{i}{suffix}" for prefix, i in captures for suffix in (".png", "_submissions.json")]
    missing = [name for name in required if not (out / name).is_file()]
    if missing:
        raise ValueError("Inputs are incomplete; no review files written:\n  " + "\n  ".join(missing))
    for prefix, i in captures:
        with Image.open(out / f"{prefix}_{i}.png") as image:
            if image.size != (1920, 1080):
                raise ValueError(f"Unexpected image size: {prefix}_{i}.png {image.size}")
            image.verify()
        receipt = read(out, f"{prefix}_{i}_submissions.json")
        if receipt.get("status") != "ACTUAL_STREAM_OBSERVER_SUBMISSIONS":
            raise ValueError(f"Unexpected submission receipt status: {prefix}_{i}")
        if any(k not in receipt for k in SUBMISSION_METRICS):
            raise ValueError(f"Incomplete submission counts: {prefix}_{i}")
    return views, after_prefix, middle_prefix


def build(out, after_prefix=None, middle_prefix=None, check_only=False):
    views, after_prefix, middle_prefix = validate_inputs(out, after_prefix, middle_prefix)
    for view in views:
        view["label"] = NAMES[view["id"]]
        view["before"] = f'before_{view["id"]}.png'
        view["after"] = f'{after_prefix}_{view["id"]}.png'
        view["middle"] = f'{middle_prefix}_{view["id"]}.png' if view["id"] in MIDDLE_VIEWS else None
    checks, distance = read(out, "checks.json"), read(out, "distance_response.json")
    before = [read(out, f"performance_before_{n}.json") for n in (1, 2)]
    after = [read(out, f"performance_after_{n}.json") for n in (1, 2)]
    performance = aggregate_performance(before, after)
    checks_pass = checks.get("status") == "PASS" and not any(
        line.startswith("FAIL") for line in checks.get("checks", [])
    )
    distance_pass = distance.get("samples", 0) > 0 and distance.get("decreases") == 0
    gpu_pass = all(row["gpuIncreaseBelowLimit"] for row in performance)
    perf_headers = ["구간", "CPU p50 ms", "CPU p95", "CPU p99", "GPU p50 ms", "GPU p95", "GPU p99", "GPU p50 차이", "+0.5ms 기준"]
    perf_rows = [[PHASES[row["phase"]]] + [
        f'{row["before"][k]:.3f} → {row["after"][k]:.3f}' for k in PERF_METRICS
    ] + [f'{row["gpuMedianDeltaMs"]:+.3f} ms', "통과" if row["gpuIncreaseBelowLimit"] else "조정 필요"] for row in performance]
    raw_rows = []
    for side, runs in (("이전", before), ("이후", after)):
        for n, run in enumerate(runs, 1):
            for phase in run["phases"]:
                raw_rows.append([f"{side} {n}회", PHASES[phase["phase"]], phase["count"]] +
                                [f"{phase[k]:.3f}" for k in PERF_METRICS])
    raw_headers = ["실행", "구간", "프레임 수", "CPU p50", "CPU p95", "CPU p99", "GPU p50", "GPU p95", "GPU p99"]
    submissions, submission_rows = [], []
    for view in views:
        i = view["id"]
        old, new = read(out, f"before_{i}_submissions.json"), read(out, f"{after_prefix}_{i}_submissions.json")
        equal = (submission_signature(old) == submission_signature(new)
                 and all(old[k] == new[k] for k in SUBMISSION_METRICS))
        row = {"view": i, "sameGeometryLodSubmissions": equal,
               "before": {k: old[k] for k in SUBMISSION_METRICS},
               "after": {k: new[k] for k in SUBMISSION_METRICS},
               "beforeResidentCells": old.get("residentCells"), "afterResidentCells": new.get("residentCells")}
        if i in MIDDLE_VIEWS:
            mid = read(out, f"{middle_prefix}_{i}_submissions.json")
            row["atmosphereOnly"] = {k: mid[k] for k in SUBMISSION_METRICS}
            row["sameGeometryLodSubmissionsAtmosphereOnly"] = (
                submission_signature(old) == submission_signature(mid)
                and all(old[k] == mid[k] for k in SUBMISSION_METRICS)
            )
        submissions.append(row)
        submission_rows.append([view["label"]] + [f"{old[k]:,} → {new[k]:,}" for k in SUBMISSION_METRICS] +
                               ["동일" if equal else "차이 있음",
                                ("동일" if row["sameGeometryLodSubmissionsAtmosphereOnly"] else "차이 있음")
                                if i in MIDDLE_VIEWS else "중간판 없음"])
    submission_headers = ["구도", "제출 패킷", "제출 인스턴스", "가시 삼각형", "카드 인스턴스", "이후 메시·LOD 제출", "중간판 메시·LOD 제출"]
    all_submissions_equal = len(submissions) == 15 and all(row["sameGeometryLodSubmissions"] for row in submissions)
    middle_submissions = [row for row in submissions if row["view"] in MIDDLE_VIEWS]
    all_middle_equal = len(middle_submissions) == 3 and all(
        row["sameGeometryLodSubmissionsAtmosphereOnly"] for row in middle_submissions
    )
    capture_audit_pass = all_submissions_equal and all_middle_equal
    distance_rows = [[f'{x["distance"]:,.0f} m', f'{x["wash"]:.3f}', f'{x["tone"]:.4f}', f'{x["luminance"]:.5f}']
                     for x in distance.get("examples", [])]
    distance_headers = ["거리", "대기 혼합", "산 표면 톤", "최종 선형 명도"]
    commits = [r["commit"] for r in before + after if isinstance(r.get("commit"), (int, float))]
    for timing in out.glob("*_timing.txt"):
        found = re.search(r"Commit: ([\d.]+)", timing.read_text(encoding="utf-8-sig"))
        if found:
            commits.append(float(found[1]))
    peak_commit = max(commits) if commits else None
    memory = max((p.get("maxMemoryMiB", 0) for r in before + after for p in r["phases"]), default=0)
    worst_delta = max(r["gpuMedianDeltaMs"] for r in performance)
    cpu_range = [min(r["after"]["cpuP50"] for r in performance), max(r["after"]["cpuP50"] for r in performance)]
    gpu_range = [min(r["after"]["gpuP50"] for r in performance), max(r["after"]["gpuP50"] for r in performance)]
    cpu_target_met = all(r["after"]["cpuP50"] <= CPU_TARGET_MS for r in performance)
    summary = {
        "status": "IMPLEMENTED_TEST_VISUAL_REVIEW_PENDING" if checks_pass and distance_pass and gpu_pass and capture_audit_pass else "REVIEW_REQUIRES_ATTENTION",
        "views": 15, "atmosphereOnlyViews": list(MIDDLE_VIEWS), "resolution": [1920, 1080],
        "afterPrefix": after_prefix, "middlePrefix": middle_prefix,
        "checksStatus": checks.get("status"), "distanceSamples": distance.get("samples"),
        "distanceDecreases": distance.get("decreases"), "gpuIncreaseLimitMsExclusive": GPU_LIMIT_MS,
        "gpuIncreaseBelowLimitEveryPhase": gpu_pass, "worstGpuMedianDeltaMs": worst_delta,
        "captureSubmissionAuditStatus": "PASS" if capture_audit_pass else "FAIL",
        "all15BeforeAfterSubmissionsIdentical": all_submissions_equal,
        "all3AtmosphereOnlySubmissionsIdentical": all_middle_equal,
        "capturePreparation": "Edit-only visible packet preparation without draw submission, then six renders; PendingObserverPackets == 0, runtime budgets unchanged",
        "excludedCaptureArchive": ["IncompletePacketWarmup", "MixedViewCache"],
        "cpuMedianTargetMs": CPU_TARGET_MS, "cpuMedianTargetMetEveryPhase": cpu_target_met,
        "performanceAggregation": "Median of two per-run phase statistics; not pooled frame percentiles",
        "performance": performance, "submissions": submissions, "commitPeak": peak_commit,
        "memoryPeakMiB": memory, "cpuMedianRange": cpu_range, "gpuMedianRange": gpu_range,
        "actualPlayerTraversal": "UNVERIFIED", "artApproval": "UNVERIFIED", "video": False, "build": False,
    }
    if check_only:
        return summary

    gpu_message = (f"전체 구간에서 GPU 중앙값 증가가 +0.5ms 미만입니다. 최대 차이 {worst_delta:+.3f}ms."
                   if gpu_pass else f"GPU 중앙값 증가가 +0.5ms 이상인 구간이 있어 조정이 필요합니다. 최대 차이 {worst_delta:+.3f}ms.")
    submissions_message = ("촬영 제출 검사 통과: 전후 15구도와 대기만 변경한 중간 3구도의 메시·LOD·인스턴스·패킷·삼각형 제출이 모두 동일합니다. 추가 제출은 없습니다."
                           if capture_audit_pass else
                           "촬영 제출 검사 실패: 전후 15구도 또는 중간 3구도에서 제출 차이가 있습니다. 준비가 완료된 동등한 비교로 판정하지 않으며 원인 확인이 필요합니다.")
    cpu_message = ("CPU 중앙값은 모든 측정 구간에서 8.33ms 이하입니다. 이 결과만으로 실제 120fps 달성을 확정하지 않습니다."
                   if cpu_target_met else
                   "CPU 8.33ms(120fps) 목표는 미달입니다. 이후 측정 구간 중 CPU 중앙값이 8.33ms를 넘는 구간이 있으므로 120fps 달성으로 보고하지 않습니다.")
    commit_text = f"{peak_commit:.1%}" if peak_commit is not None else "기록 없음"
    hardware = before[0]
    config = [
        "대기: 500~1500m에서 혼합 .18까지, 1500~3400m에서 .64를 더해 최종 .82. 지형·식생·카드가 같은 거리 함수를 사용합니다.",
        "명암 범위는 지면 .14~.30, 흙길 .17~.29, 산 .06~.18 유지. 바탕 채도와 추가 지역색 기여도는 각각 .2 유지.",
        "세계 좌표의 넓은 안료 농담과 드문 붓 자국을 표면에 더하고, 식생의 먼 디테일을 정리합니다. 전역 노출·하늘 설정·조명은 유지합니다.",
        "붓 자국: 간격 20~40m, 폭 2~7m, 최대 감산 먹 강도 .02, 200~1400m에서 점차 사라짐. 산의 안료 대비 .03, 지면의 안료 대비 .65.",
        "식생: 30~180m에서 단계 없이 전환하며 색상 표본의 mip bias 상한 1.5, 명도 대비 압축 .3. 잎 알파·그림자·깊이 표본과 바람·LOD 배치는 그대로 유지합니다.",
        "이번 조정의 계약은 직전 Darker 판의 개체 수·ID·위치·회전·크기·활성 상태·LOD·충돌·길·시설 보존입니다. 새 식생 감축이나 재배치는 수행하지 않습니다.",
    ]
    limits = [
        "실제 플레이어 입력을 통한 보행·경계 통과·전체 시설 통행과 사용자의 최종 미술 승인은 미검증입니다.",
        "기존 산의 뾰족한 윤곽과 삼각 면은 남습니다. 이번 표면·대기 조정은 산 메시 형상 수정이 아닙니다.",
        "고정 표면·광량의 CPU 거리 명도 검사는 그림자 cascade, mip, SSAO 전환을 포함한 모든 화면 픽셀의 단조성을 보증하지 않습니다.",
        "진단 카메라의 실제 회전·접근 구간은 계측했지만, 이동 중 붓결 떨림·카드 전환의 시각적 품질은 연속 화면으로 최종 검수하지 않았습니다. 셰이더의 세계 좌표 고정과 알파/LOD 보존 검사를 시각 승인으로 간주하지 않습니다.",
        "Editor 진단 카메라 계측은 실제 플레이어 완주나 빌드 성능이 아닙니다. 두 실행의 변동만으로 셰이더의 인과 효과를 단정하지 않습니다.",
        "영상·새 빌드는 만들지 않았습니다.",
    ]
    capture_method = [
        "초기 33장에는 셀 생성이 끝났어도 일부 가시 제출 패킷이 아직 준비되지 않은 촬영이 섞여 있었습니다. 당시의 동기 렌더 6회만으로는 예산 내 패킷 준비가 항상 끝나지 않았습니다.",
        "초기 이미지와 원장은 IncompletePacketWarmup/에, 이전 시점의 원경 패킷 캐시가 남았던 비교는 MixedViewCache/에 보존하고 판정에서 제외했습니다. 현재 보고서는 매 구도 같은 빈 캐시로 시작한 폴더 최상위의 재촬영 33장만 사용합니다.",
        "재촬영은 셀 생성 후 Edit 전용 준비 함수로 가시 패킷을 완성합니다. 이때 렌더를 반복 제출하지 않고, 준비 후 6회 렌더와 PendingObserverPackets == 0을 확인합니다. 실제 Play 예산은 바꾸지 않습니다. 준비 시간은 촬영 timing.txt의 동기 렌더 시간에서 제외하며, 그 동기 시간도 실제 Play 프레임 시간으로 사용하지 않습니다.",
        "재촬영 도중 시스템 커밋 85% 중단 규칙에 따라 작업을 멈췄습니다. 저장된 Edit 상태에서 Unity를 정상 종료·재실행해 커밋이 약 84.5%에서 48%로 낮아진 뒤 작업을 재개했습니다. 중단 규칙과 실행 예산을 완화하지 않았습니다.",
        "최종 성능 표는 Unity 재실행 후 다시 측정한 이전 2회·이후 2회만 사용합니다. 정지 이미지 준비 완료 조건과 실제 Play의 사전 생성 없는 측정은 별개입니다.",
    ]
    provenance = [
        "프로필·파생 재질: Assets/_Project/Art/World/WorldCompact/InkLandscape/Painterly/Profile.asset 및 Materials/. 직전 Darker/Profile.asset과 Darker 재질을 보존하고 파생합니다.",
        "설정·CPU 거리 검산: Assets/_Project/Scripts/Data/World/CompactInkLandscapeProfile.cs. 적용·보존 검사: Scripts/Editor/WorldMacro/CompactPainterlyAuthoring.cs.",
        "공통 지면·산·대기 함수: Assets/_Project/Shaders/CompactInkLandscape.hlsl. 현재 프로젝트 CodexInkLandscape.shader의 끊기는 붓 획 원리를 세계 좌표의 큰 간격과 낮은 먹 강도로 재해석했습니다.",
        "식생 색상 처리: Assets/_Project/Shaders/CompactPainterlyFoliage.hlsl. 호출 경로는 CompactNaturalGround.shader, WorldMacroTerrain.shader, CompactNaturalVegetation.shader, EarlyRegionFoliageCard.shader입니다.",
        "지면의 공급자 원본은 Assets/SeyeonjeongPavilion/Texture/Ground/의 T_Grass_1, T_Dirt_1, T_RockGround_1 색·노멀입니다. NaturalSurface 파생 텍스처와 기존 길 마스크를 재사용하며 원본을 덮어쓰지 않습니다.",
        "기존 복원 출처: C2 Ground_With_Path.mat의 옅은 먹 농담 및 레거시 MandateOfInk/Assets/_Project/Art/Shaders/S_ToonLitTemp.shader의 먹–한지 명암 원리. 이번 작업은 옛 씬·지형 전체 복원이 아닙니다.",
    ]
    report = "\n\n".join([
        "# 거리 대기와 수묵 표면 — 축소본 TEST",
        "`W_Demo_Compact` · 직전 Darker 판과 15개 동일 구도 비교. 주막·산길·청림 심부는 대기만 바꾼 중간판도 함께 제공합니다. 최종 외형 승인 대기.",
        "## 변경과 보존\n\n" + "\n".join("- " + line for line in config),
        "## 촬영 재검증과 메모리 중단\n\n" + "\n".join("- " + line for line in capture_method),
        "## 검사 결과\n\n구조·셰이더 검사: **" + str(checks.get("status", "UNKNOWN")) +
        "**. 촬영 제출 검사: **" + summary["captureSubmissionAuditStatus"] + "**.\n\n" +
        "\n".join("- " + line for line in checks.get("checks", [])),
        f"거리 명도 표본 {distance.get('samples', 0):,}개, 감소 {distance.get('decreases', '미상')}개. 아래는 고정된 한 표면·광량 조합의 예시입니다.\n\n" + table(distance_headers, distance_rows, True),
        "## 1080p Play 성능\n\n" + f"Unity {hardware.get('unityVersion', '미기록')} / {hardware.get('gpuDevice', '미기록')} / {hardware.get('cpuDevice', '미기록').strip()}. " +
        "이전 2회·이후 2회의 1920×1080 진단 카메라 실행입니다. 각 구간의 실행별 p50/p95/p99를 두 실행 사이에서 중앙값으로 집계했습니다. 전체 프레임을 합친 백분위수가 아닙니다.\n\n" +
        table(perf_headers, perf_rows, True) + "\n\n" + gpu_message +
        f" 이후 CPU p50 {cpu_range[0]:.3f}~{cpu_range[1]:.3f}ms, GPU p50 {gpu_range[0]:.3f}~{gpu_range[1]:.3f}ms. **{cpu_message}**",
        "### 실행별 원시 구간 통계\n\n" + table(raw_headers, raw_rows, True),
        f"측정된 최대 메모리 {memory:,.0f}MiB, 캡처·계측 기록의 최대 시스템 커밋 {commit_text}. 캡처 timing.txt의 동기 호출 시간은 실제 프레임 시간으로 사용하지 않았습니다.",
        "## 고정 구도 제출 비교\n\n" + submissions_message + "\n\n" + table(submission_headers, submission_rows, True) +
        "\n\n표는 주 식생 observer의 실제 캐시된 제출이며 씬 전체 GPU Draw Call/그림자 패스 수가 아닙니다. 인스턴스는 메시 파트와 LOD별로 중복될 수 있으므로 고유 식물 수로 읽지 않습니다. 중간판 제출 수는 summary.json에도 포함됩니다.",
        "## 남은 항목\n\n" + "\n".join("- " + line for line in limits),
        "## 구현과 출처\n\n" + "\n".join("- " + line for line in provenance),
        "## 파일\n\n- `REVIEW.html`: 15구도 전후 및 3구도 중간판 비교.\n" +
        "- `checks.json`, `distance_response.json`: 이번 실행의 보존·셰이더·거리 검사.\n" +
        "- `performance_before_1/2.json`, `performance_after_1/2.json`: 네 번의 실제 Play 계측.\n" +
        f"- `before_*.png`, `{after_prefix}_*.png`, `{middle_prefix}_*.png` 및 대응 `_submissions.json`.\n" +
        "- `summary.json`: 수치 집계와 검사 상태. `before_disk.unity` / `before_open.unity`: 이전 씬 보존본.",
    ]) + "\n"

    options = "".join(f'<option value="{escaped(v["id"])}">{escaped(v["label"])}</option>' for v in views)
    check_list = "".join(f'<li class="{("failed" if line.startswith("FAIL") else "")}">{escaped(line)}</li>' for line in checks.get("checks", []))
    data = json.dumps(views, ensure_ascii=False).replace("</", "<\\/")
    page = '''<!doctype html><html lang="ko"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>거리 대기와 수묵 표면 비교</title>
<style>*{box-sizing:border-box}body{margin:0;background:#181b19;color:#e5e0d3;font:16px/1.65 system-ui,"Malgun Gothic",sans-serif}main{max-width:1640px;margin:auto;padding:32px}h1{font-size:34px;margin:0}h2{font-size:23px}p{max-width:1120px}a{color:#c7d7c4}nav{display:flex;gap:12px;align-items:center;flex-wrap:wrap;margin:22px 0}button,select{font:inherit;background:#303a32;color:#fff;border:1px solid #657666;border-radius:5px;padding:8px 14px;cursor:pointer}button:hover{background:#455343}button:focus-visible,select:focus-visible,a:focus-visible{outline:3px solid #d5c094;outline-offset:3px}.tag{color:#d5c094;font-size:13px;letter-spacing:.06em}.comparison{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:16px}.comparison.three{grid-template-columns:repeat(3,minmax(0,1fr))}figure{margin:0;min-width:0}img{width:100%;display:block;aspect-ratio:16/9;background:#10130f;object-fit:contain}figcaption{padding:8px 0;color:#b9b9aa}.single{display:none}.note{background:#252c26;border-left:3px solid #a9ac8a;padding:12px 18px}.failed{color:#efac91}.facts{display:flex;gap:16px;flex-wrap:wrap;margin:22px 0}.facts div{background:#252c26;padding:12px 18px}.facts strong{display:block;font-size:23px}details{margin:18px 0;padding:18px;background:#222822}summary{font-size:19px;cursor:pointer}.scroll{overflow:auto}table{border-collapse:collapse;width:100%;margin:16px 0;font-size:14px}th,td{padding:9px 12px;border-bottom:1px solid #424b40;text-align:left;white-space:nowrap}th{color:#c3d0b9}code{color:#d3c6a9}li{margin:7px 0;overflow-wrap:anywhere}footer{margin-top:30px;color:#aeb2a7}footer a{display:inline-block;margin-right:14px}[hidden]{display:none!important}@media(max-width:850px){main{padding:16px}.comparison,.comparison.three{grid-template-columns:1fr}h1{font-size:27px}}</style></head>
<body><main><div class="tag">W_DEMO_COMPACT · TEST · 실제 UNITY 캡처</div><h1>거리 대기를 늦추고, 표면의 먹결을 더했습니다</h1>
<p>짙은 발밑과 앞산의 기본 명암을 유지하며 중경과 원경을 조정한 비교입니다. 주막·산길·청림 심부에서는 <b>이전 → 대기만 변경 → 대기와 수묵 표면 변경</b>을 나란히 볼 수 있습니다.</p>
<nav><label for="view">비교 구도</label><select id="view">__OPTIONS__</select><button id="previous" type="button" aria-label="이전 구도">←</button><button id="next" type="button" aria-label="다음 구도">→</button><button id="mode" type="button">크게 비교</button><select id="stage" aria-label="큰 이미지 비교 단계" hidden><option value="before">이전 Darker 판</option><option value="middle">대기만 변경</option><option value="after" selected>대기 + 수묵 표면</option></select><a href="REPORT.md">검사 보고서</a><a href="../Darker/REVIEW.html">직전 판 검토</a></nav>
<div class="comparison" id="comparison"><figure><a id="beforeLink"><img id="before" alt="이전 Darker 판"><figcaption>이전 · 짙은 근경 기준판</figcaption></a></figure><figure id="middleFigure" hidden><a id="middleLink"><img id="middle" alt="대기만 변경한 중간판"><figcaption>중간 · 거리 대기만 변경</figcaption></a></figure><figure><a id="afterLink"><img id="after" alt="거리 대기와 수묵 표면 적용판"><figcaption>이후 · 거리 대기 + 수묵 표면</figcaption></a></figure></div>
<figure class="single" id="single"><a id="largeLink"><img id="large" alt="선택한 비교 단계"></a><figcaption id="largeLabel"></figcaption></figure><p id="position" aria-live="polite"></p>
<div class="facts"><div><strong>500 → 1500m</strong>중경 대기 .18</div><div><strong>1500 → 3400m</strong>원경 대기 +.64 · 합계 .82</div><div><strong>.14~.30 / .06~.18</strong>지면 / 산 기본 명암 유지</div><div><strong>배치 보존</strong>개체 수 · 위치 · LOD · 충돌</div></div>
<p class="note">__GPU_MESSAGE__<br>__CPU_MESSAGE__<br>촬영 제출 검사: <b>__CAPTURE_STATUS__</b>. 산의 뾰족한 윤곽·삼각 면은 남아 있습니다. 실제 플레이어 보행과 최종 미술 승인은 미검증입니다.</p>
<details open><summary>이번에 바꾼 값과 유지한 값</summary><ul>__CONFIG__</ul></details>
<details><summary>촬영 재검증 · 메모리 중단과 정상 재실행</summary><ul>__CAPTURE_METHOD__</ul></details>
<details><summary>15구도 주 식생 제출 비교</summary><p>__SUBMISSION_MESSAGE__</p>__SUBMISSIONS__<p>GPU 프로파일러의 씬 전체 Draw Call/그림자 패스 수가 아닙니다. 인스턴스는 파트·LOD별 중복을 포함하며 고유 식물 수가 아닙니다.</p></details>
<details open><summary>1080p Play 비용 — 이전 2회 → 이후 2회</summary><p>각 구간의 실행별 p50·p95·p99를 두 실행 사이에서 중앙값으로 집계했습니다. 전체 프레임을 합친 백분위수가 아닙니다. <b>GPU p50 증가가 +0.5ms 이상이면 조정 필요</b>로 판정합니다.</p>__PERFORMANCE__<p>__PERF_NOTE__</p><details><summary>네 실행의 p50 / p95 / p99 원시 구간 값</summary>__RAW_PERFORMANCE__</details></details>
<details><summary>보존 · 셰이더 · 거리 명도 검사</summary><p>검사 상태: <b>__CHECK_STATUS__</b> · 거리 명도 __DISTANCE_SAMPLES__개 · 감소 __DISTANCE_DECREASES__개.</p><ul>__CHECKS__</ul>__DISTANCE__<p>고정 표면·광량의 예시입니다. 그림자·mip 전환 등 모든 실제 화면 픽셀의 단조성을 보증하지 않습니다.</p></details>
<details><summary>검증 범위와 남은 항목</summary><ul>__LIMITS__</ul></details>
<details><summary>프로필 · 셰이더 · 에셋 출처</summary><ul>__PROVENANCE__</ul></details>
<footer>15개 고정 구도 전후 + 3개 중간판 · 1920×1080 · 영상/새 빌드 없음<br><a href="checks.json">검사 원본</a><a href="distance_response.json">거리 응답</a><a href="summary.json">집계</a><a href="performance_before_1.json">이전 1회</a><a href="performance_before_2.json">이전 2회</a><a href="performance_after_1.json">이후 1회</a><a href="performance_after_2.json">이후 2회</a></footer></main>
<script>const views=__VIEWS__;const $=id=>document.getElementById(id);let single=false;function render(){const v=views.find(x=>x.id===$('view').value)||views[0];const hasMiddle=!!v.middle;$('middleFigure').hidden=!hasMiddle;$('comparison').classList.toggle('three',hasMiddle);$('stage').querySelector('[value="middle"]').disabled=!hasMiddle;if(!hasMiddle&&$('stage').value==='middle')$('stage').value='after';for(const side of ['before','middle','after']){if(v[side]){$(side).src=v[side];$(side+'Link').href=v[side];}else{$(side).removeAttribute('src');$(side+'Link').removeAttribute('href');}}const stage=$('stage').value;$('large').src=v[stage];$('largeLink').href=v[stage];$('largeLabel').textContent=v.label+' · '+$('stage').selectedOptions[0].textContent;$('position').textContent=v.label+' · 카메라 ('+[v.eye.x,v.eye.y,v.eye.z].map(x=>x.toFixed(1)).join(', ')+') · '+(hasMiddle?'3단계 동일 구도 비교':'이전/이후 동일 구도 비교');history.replaceState(null,'','#'+v.id);}function move(n){$('view').selectedIndex=($('view').selectedIndex+n+views.length)%views.length;render();}$('view').onchange=render;$('previous').onclick=()=>move(-1);$('next').onclick=()=>move(1);$('stage').onchange=render;$('mode').onclick=()=>{single=!single;$('comparison').style.display=single?'none':'grid';$('single').style.display=single?'block':'none';$('stage').hidden=!single;$('mode').textContent=single?'나란히 비교':'크게 비교';render();};function readHash(){const requested=decodeURIComponent(location.hash.slice(1));if(views.some(v=>v.id===requested))$('view').value=requested;}window.addEventListener('hashchange',()=>{readHash();render();});readHash();document.addEventListener('keydown',event=>{if(['SELECT','INPUT','TEXTAREA'].includes(event.target.tagName))return;if(event.key==='ArrowLeft')move(-1);if(event.key==='ArrowRight')move(1);});render();</script></body></html>'''
    tokens = {
        "OPTIONS": options, "GPU_MESSAGE": escaped(gpu_message), "CPU_MESSAGE": escaped(cpu_message),
        "CAPTURE_STATUS": summary["captureSubmissionAuditStatus"],
        "CONFIG": "".join(f"<li>{escaped(x)}</li>" for x in config),
        "CAPTURE_METHOD": "".join(f"<li>{escaped(x)}</li>" for x in capture_method),
        "PROVENANCE": "".join(f"<li>{escaped(x)}</li>" for x in provenance),
        "SUBMISSION_MESSAGE": escaped(submissions_message), "SUBMISSIONS": table(submission_headers, submission_rows),
        "PERFORMANCE": table(perf_headers, perf_rows), "RAW_PERFORMANCE": table(raw_headers, raw_rows),
        "PERF_NOTE": escaped(f"이후 CPU p50 {cpu_range[0]:.3f}~{cpu_range[1]:.3f}ms / GPU p50 {gpu_range[0]:.3f}~{gpu_range[1]:.3f}ms. "
                             f"최대 측정 메모리 {memory:,.0f}MiB · 기록상 커밋 최대 {commit_text}. {cpu_message}"),
        "CHECK_STATUS": escaped(checks.get("status", "UNKNOWN")), "DISTANCE_SAMPLES": f"{distance.get('samples', 0):,}",
        "DISTANCE_DECREASES": escaped(distance.get("decreases", "미상")), "CHECKS": check_list,
        "DISTANCE": table(distance_headers, distance_rows), "LIMITS": "".join(f"<li>{escaped(x)}</li>" for x in limits), "VIEWS": data,
    }
    for key, value in tokens.items():
        page = page.replace(f"__{key}__", value)
    if re.search(r"__[A-Z_]+__", page):
        raise ValueError("An HTML template token was not filled")
    (out / "REPORT.md").write_text(report, encoding="utf-8")
    (out / "REVIEW.html").write_text(page, encoding="utf-8")
    (out / "summary.json").write_text(json.dumps(summary, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    return summary


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--out", type=Path, default=DEFAULT_OUT)
    parser.add_argument("--after-prefix", help="Default: after, or final if all final captures exist")
    parser.add_argument("--middle-prefix", help="Default: atmosphere, middle, or air")
    parser.add_argument("--check-only", action="store_true", help="Validate and aggregate without writing any files")
    args = parser.parse_args()
    try:
        result = build(args.out, args.after_prefix, args.middle_prefix, args.check_only)
    except (ValueError, KeyError, OSError) as error:
        parser.exit(2, str(error) + "\n")
    compact = {k: v for k, v in result.items() if k not in ("performance", "submissions")}
    print(json.dumps(compact, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
