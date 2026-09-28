"""Build the Korean BroadBrush review from complete, measured Unity evidence.

This script never invokes Unity or a browser and never modifies source assets.
Missing or invalid inputs fail before any report is written. --check-only validates
and aggregates without writing REPORT.md, REVIEW.html or summary.json.
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
DEFAULT_OUT = ROOT / "Art/World/WorldMacro/Compact/InkLandscape/BroadBrush"
NAMES = {
    "inn": "금표 주막 앞", "mountain_path": "산길 조망", "office": "시작 관청",
    "DeepForest": "청림 심부", "SouthGate": "황경 남문", "Jeokro": "적로",
    "Cheolong": "철옹", "Hyeongang": "현강 · 수변", "Hwanggyeong": "황경 분지",
    "ground_detail": "발밑 지면", "boundary_Hwanggyeong": "황경 경계",
    "boundary_Cheongrim": "청림 경계", "boundary_Jeokro": "적로 경계",
    "boundary_Cheolong": "철옹 경계", "boundary_Hyeongang": "현강 경계",
}
PHASES = {
    "cold": "초기 생성", "stationary": "정지", "rotation": "회전",
    "first_visit": "첫 접근", "return_visit": "재방문", "returned_stationary": "복귀 정지",
}
METRICS = ("cpuP50", "cpuP95", "cpuP99", "gpuP50", "gpuP95", "gpuP99")
COUNTS = ("drawCalls", "submittedInstances", "visibleTriangles", "billboardInstances")
BATCH_KEYS = (
    "prototype", "category", "mesh", "shadowMode", "lod", "instances", "packets",
    "submesh", "trianglesPerInstance", "submittedTriangles", "alphaClip", "billboard",
    "fadeOverride", "fade",
)
GPU_LIMIT_MS = .5
CPU_TARGET_MS = 8.33
CONFIG = {
    "BroadBrushScale": [80, 240, 420, 1000],
    "BroadBrushCoverage": [.9, .18, .76, .2],
    "BroadBrushTones": [.015, .07],
    "BroadBrushBands": [.33, .66, .12, .8],
    "loadedBodyFraction": [.42, .64], "centreJitterMultiplier": 2,
    "groundTones": [.14, .30, .17, .29], "uncoveredMountainTones": [.06, .18],
    "baseSaturation": .2, "regionalColourContribution": .2,
    "midAirRange": [500, 1500], "farAirRange": [1500, 3400], "airStrengths": [.18, .64],
    "foliagePainterly": [30, 180, 1.5, .3],
}


def read(out, name):
    return json.loads((out / name).read_text(encoding="utf-8-sig"))


def esc(value):
    return html.escape(str(value), quote=True)


def number(value):
    return isinstance(value, (int, float)) and not isinstance(value, bool) and math.isfinite(value)


def table(headers, rows, markdown=False):
    if markdown:
        def cell(value):
            return str(value).replace("|", "\\|").replace("\n", " ")
        return "\n".join(["|" + "|".join(headers) + "|", "|" + "|".join("---" for _ in headers) + "|",
                          *("|" + "|".join(map(cell, row)) + "|" for row in rows)])
    return '<div class="scroll"><table><thead><tr>' + "".join(f"<th>{esc(h)}</th>" for h in headers) + \
        "</tr></thead><tbody>" + "".join("<tr>" + "".join(f"<td>{esc(c)}</td>" for c in row) + "</tr>"
                                      for row in rows) + "</tbody></table></div>"


def signature(receipt, material=False):
    keys = BATCH_KEYS + (("material",) if material else ())
    return sorted(json.dumps({key: row[key] for key in keys}, sort_keys=True) for row in receipt["batches"])


def aggregate_performance(before, after):
    """Median of repeated per-run statistics, never a pooled-frame percentile."""
    if len(before) != 2 or len(after) != 2:
        raise ValueError("성능은 이전 2회와 이후 2회가 모두 필요합니다.")
    runs = before + after
    for run in runs:
        if run.get("status") != "MEASURED_DIAGNOSTIC_CAMERA" or run.get("error"):
            raise ValueError("완료되지 않았거나 오류가 있는 성능 원장이 있습니다.")
        if (run.get("width"), run.get("height")) != (1920, 1080):
            raise ValueError("성능 측정 해상도가 1920×1080이 아닙니다.")
        if run.get("restored") is not True:
            raise ValueError("성능 측정 후 복원 완료를 확인할 수 없습니다.")
        if not run.get("utc") or not isinstance(run.get("start"), dict):
            raise ValueError("성능 원장에 측정 시각 또는 시작 위치가 없습니다.")
        for key in ("start", "mode", "unityVersion", "gpuDevice", "cpuDevice"):
            if run.get(key) != runs[0].get(key):
                raise ValueError(f"성능 측정 조건이 다릅니다: {key}")
        phases = run.get("phases", [])
        if len(phases) != len(PHASES) or {p.get("phase") for p in phases} != set(PHASES):
            raise ValueError("성능 구간이 빠졌거나 중복되었습니다.")
        for phase in phases:
            if not number(phase.get("count")) or phase["count"] <= 0:
                raise ValueError("측정 프레임이 없는 성능 구간이 있습니다.")
            if any(not number(phase.get(k)) or phase[k] < 0 for k in METRICS):
                raise ValueError("유효한 CPU/GPU p50·p95·p99가 모두 필요합니다.")
    if len({r["utc"] for r in runs}) != 4:
        raise ValueError("네 실행의 측정 시각이 중복됩니다. 같은 원장을 반복 실행으로 세지 않습니다.")
    indexed = [{p["phase"]: p for p in run["phases"]} for run in runs]
    result = []
    for name in PHASES:
        old = {k: statistics.median(r[name][k] for r in indexed[:2]) for k in METRICS}
        new = {k: statistics.median(r[name][k] for r in indexed[2:]) for k in METRICS}
        delta = new["gpuP50"] - old["gpuP50"]
        result.append({"phase": name, "before": old, "after": new, "gpuMedianDeltaMs": delta,
                       "gpuIncreaseBelowLimit": delta < GPU_LIMIT_MS,
                       "cpuMedianTargetMet": new["cpuP50"] <= CPU_TARGET_MS,
                       "pairedGpuP50DeltaMs": [indexed[i + 2][name]["gpuP50"] - indexed[i][name]["gpuP50"] for i in (0, 1)],
                       "beforeRunStats": [r[name] for r in indexed[:2]],
                       "afterRunStats": [r[name] for r in indexed[2:]]})
    return result


def validate_inputs(out):
    required = ["baseline.json", "checks.json", "distance_response.json", "views.json", "LEGACY_SOURCE.md"]
    required += [f"performance_{side}_{i}.json" for side in ("before", "after") for i in (1, 2)]
    required += [f"{side}_{view}{suffix}" for side in ("before", "after") for view in NAMES
                 for suffix in (".png", "_submissions.json")]
    missing = [name for name in required if not (out / name).is_file()]
    if missing:
        raise ValueError("미측정 또는 미완료 자료가 있어 보고서를 생성하지 않았습니다:\n  " + "\n  ".join(missing))
    views = read(out, "views.json")["views"]
    ids = [v["id"] for v in views]
    if len(ids) != 15 or len(set(ids)) != 15 or set(ids) != set(NAMES):
        raise ValueError("15개 고정 구도가 중복 없이 모두 필요합니다.")
    for v in views:
        for field in ("eye", "target"):
            if any(not number(v[field].get(axis)) for axis in ("x", "y", "z")):
                raise ValueError(f"유효하지 않은 구도 좌표: {v['id']} / {field}")
    baseline = read(out, "baseline.json")
    if not all(baseline.get(k) for k in ("profile", "sheet", "bindings", "structure", "hashes", "supplements")):
        raise ValueError("원본 보존 원장이 불완전합니다.")
    checks, distance = read(out, "checks.json"), read(out, "distance_response.json")
    if checks.get("status") not in ("PASS", "FAIL") or not checks.get("checks"):
        raise ValueError("유효한 보존·셰이더 검사 결과가 없습니다.")
    if not number(distance.get("samples")) or distance["samples"] <= 0 or not number(distance.get("decreases")) or distance["decreases"] < 0:
        raise ValueError("거리 명도 검사가 미측정 또는 불완전합니다.")
    receipts = {}
    for v in views:
        for side in ("before", "after"):
            name = f"{side}_{v['id']}"
            with Image.open(out / f"{name}.png") as image:
                if image.size != (1920, 1080) or image.format != "PNG":
                    raise ValueError(f"실제 1920×1080 PNG가 필요합니다: {name}")
                image.verify()
            receipt = read(out, f"{name}_submissions.json")
            if receipt.get("status") != "ACTUAL_STREAM_OBSERVER_SUBMISSIONS" or not isinstance(receipt.get("batches"), list):
                raise ValueError(f"유효하지 않은 촬영 제출 원장: {name}")
            if any(not number(receipt.get(k)) or receipt[k] < 0 for k in COUNTS):
                raise ValueError(f"제출 수치 누락: {name}")
            if any(any(k not in row for k in BATCH_KEYS + ("material",)) for row in receipt["batches"]):
                raise ValueError(f"메시·LOD·재질 제출 상세 누락: {name}")
            receipts[name] = receipt
    before = [read(out, f"performance_before_{i}.json") for i in (1, 2)]
    after = [read(out, f"performance_after_{i}.json") for i in (1, 2)]
    performance = aggregate_performance(before, after)
    return views, baseline, checks, distance, receipts, before, after, performance


def build(out, check_only=False):
    views, baseline, checks, distance, receipts, before, after, performance = validate_inputs(out)
    views = [dict(v, label=NAMES[v["id"]], before=f'before_{v["id"]}.png', after=f'after_{v["id"]}.png') for v in views]
    submissions, submission_rows = [], []
    for v in views:
        old, new = (receipts[f"{side}_{v['id']}"] for side in ("before", "after"))
        equal = all(old[k] == new[k] for k in COUNTS) and signature(old) == signature(new)
        materials_equal = signature(old, True) == signature(new, True)
        submissions.append({"view": v["id"], "sameGeometryLodSubmissions": equal,
                            "sameVegetationMaterialSubmissions": materials_equal,
                            "before": {k: old[k] for k in COUNTS}, "after": {k: new[k] for k in COUNTS},
                            "beforeResidentCells": old.get("residentCells"), "afterResidentCells": new.get("residentCells")})
        submission_rows.append([v["label"]] + [f"{old[k]:,} → {new[k]:,}" for k in COUNTS] +
                               ["동일" if equal else "차이 있음", "동일" if materials_equal else "차이 있음"])
    checks_pass = checks["status"] == "PASS" and not any(str(x).startswith("FAIL") for x in checks["checks"])
    distance_pass = distance["decreases"] == 0
    geometry_pass = len(submissions) == 15 and all(r["sameGeometryLodSubmissions"] for r in submissions)
    materials_pass = len(submissions) == 15 and all(r["sameVegetationMaterialSubmissions"] for r in submissions)
    gpu_pass = all(r["gpuIncreaseBelowLimit"] for r in performance)
    cpu_pass = all(r["cpuMedianTargetMet"] for r in performance)
    technical_pass = checks_pass and distance_pass and geometry_pass and materials_pass
    worst_delta = max(r["gpuMedianDeltaMs"] for r in performance)
    cpu_range = [min(r["after"]["cpuP50"] for r in performance), max(r["after"]["cpuP50"] for r in performance)]
    gpu_range = [min(r["after"]["gpuP50"] for r in performance), max(r["after"]["gpuP50"] for r in performance)]
    commits = [r["commit"] for r in before + after if number(r.get("commit"))]
    for side in ("before", "after"):
        for v in views:
            timing = out / f"{side}_{v['id']}_timing.txt"
            found = re.search(r"Commit: ([\d.]+)", timing.read_text(encoding="utf-8-sig")) if timing.is_file() else None
            if found:
                commits.append(float(found[1]))
    memories = [p["maxMemoryMiB"] for r in before + after for p in r["phases"] if number(p.get("maxMemoryMiB"))]
    peak_commit, peak_memory = max(commits, default=None), max(memories, default=None)
    summary = {
        "status": "TECHNICAL_CHECKS_PASSED_ART_REVIEW_PENDING" if technical_pass and gpu_pass and cpu_pass else "REVIEW_REQUIRES_ATTENTION",
        "views": 15, "resolution": [1920, 1080], "beforePrefix": "before", "afterPrefix": "after",
        "defaultDisplay": "single_after", "baseline": {"profile": baseline["profile"], "sheet": baseline["sheet"],
            "transforms": len(baseline["structure"]), "rendererBindings": len(baseline["bindings"]), "sourceHashes": len(baseline["hashes"])},
        "technicalChecksPassed": technical_pass, "checksStatus": checks["status"], "checks": checks["checks"],
        "distanceSamples": distance["samples"], "distanceDecreases": distance["decreases"],
        "captureSubmissionAuditStatus": "PASS" if geometry_pass and materials_pass else "FAIL",
        "all15BeforeAfterSubmissionsIdentical": geometry_pass, "all15VegetationMaterialSubmissionsIdentical": materials_pass,
        "gpuIncreaseLimitMsExclusive": GPU_LIMIT_MS, "gpuIncreaseBelowLimitEveryPhase": gpu_pass,
        "worstGpuMedianDeltaMs": worst_delta, "cpuMedianTargetMs": CPU_TARGET_MS, "cpuMedianTargetMetEveryPhase": cpu_pass,
        "performanceAggregation": "Median of two per-run phase statistics; not pooled frame percentiles",
        "performance": performance, "performanceRuns": [{"side": side, "repeat": i, "utc": r["utc"], "scope": r.get("scope"),
            "restored": r["restored"], "width": r["width"], "height": r["height"]} for side, runs in (("before", before), ("after", after)) for i, r in enumerate(runs, 1)],
        "submissions": submissions, "commitPeak": peak_commit, "memoryPeakMiB": peak_memory,
        "cpuMedianRange": cpu_range, "gpuMedianRange": gpu_range, "configuration": CONFIG,
        "legacySource": "LEGACY_SOURCE.md", "legacyMechanism": "Mountains do not use Shader Graph; new broad strokes reference S_ToonLitTemp three-band shading principles",
        "actualPlayerTraversal": "UNVERIFIED", "artApproval": "UNVERIFIED", "video": False, "build": False,
    }
    if check_only:
        return summary

    gpu_message = f"GPU p50 증가 최대 {worst_delta:+.3f}ms. " + ("모든 구간이 +0.5ms 미만입니다." if gpu_pass else "+0.5ms 이상인 구간이 있어 조정이 필요합니다.")
    cpu_message = ("이후 CPU p50은 모든 구간에서 8.33ms 이하입니다. 실제 120fps 완주를 보증하지는 않습니다." if cpu_pass else
                   "CPU 8.33ms 목표 미달: 이후 CPU p50이 8.33ms를 넘는 구간이 있습니다. 120fps 달성으로 보고하지 않습니다.")
    audit_message = ("15개 구도의 메시·LOD·인스턴스·패킷·삼각형과 주 식생 재질 제출이 모두 동일합니다." if geometry_pass and materials_pass else
                     "촬영 제출 비교에 차이가 있습니다. 동일한 제출 조건의 비교로 승인하지 않으며 원인 확인이 필요합니다.")
    state_message = "자동 검사와 수치 목표 통과 · 미술 승인 대기" if technical_pass and gpu_pass and cpu_pass else "검사 또는 성능 목표에 확인 필요 항목 있음 · 미술 승인 대기"
    config_lines = [
        "산의 폭 2~7m 잔 획을 큰 먹 면으로 바꿉니다. 새 획의 전체 폭은 80~240m, 전체 길이는 420~1000m입니다.",
        "BroadBrushCoverage (.9, .18, .76, .2): 먹 불투명도 .9, 가장자리 번짐 비율 .18, 두 번째 겹먹 .76, 세계 좌표 휨 .2. 이 값은 화면의 산 피복률 실측치가 아닙니다.",
        "먹 면 톤은 .015~.07. 덮이지 않은 산의 기존 .06~.18 바탕을 유지하며 더 어두운 먹 면을 혼합합니다. 3단 명암 기준 .33/.66, 전이 폭 .12, 적용 비중 .8입니다.",
        "획 길이의 42~64%까지 넓은 몸통을 유지하고 뒤쪽에서 가늘어집니다. 중심 위치의 흔들림은 초기안 대비 2배이며 화면이나 청크 경계에 따라 획 위치가 바뀌지 않는 세계 좌표 방식입니다.",
        "지면 .14~.30, 흙길 .17~.29, 바탕 채도 .2, 추가 지역색 .2는 직전 Painterly 기준을 유지합니다. 산 먹 면 때문에 지면 전체 명암 범위를 낮추지 않습니다.",
        "대기 유지: 500~1500m에 .18, 1500~3400m에 .64 추가, 최종 .82. 식생 표현도 30~180m / RGB mip bias 1.5 / 대비 압축 .3을 유지합니다.",
        "지형·산 메시와 실루엣, 길·시설·충돌, 식생의 개체 수·위치·활성 상태·바람·LOD·스트리밍 설정은 직전 Painterly 보존 기준과 비교합니다. 파생 산 재질만 새로 사용합니다.",
    ]
    method_lines = [
        "정상 재시작과 Play 진단 이후 saved_scene_checks.json에서 원본239파일과 GameObject·Transform·MeshFilter·Rigidbody·Collider59,494개 직렬화 레코드 보존을 재확인했습니다. shader_math_checks.json에는 큰 획의 인접 셀 지원 범위와 재현 가능한 거리 계산을 기록했습니다.",
        "첫 Play 계측은 시스템 커밋85.18%에서 자동 중단했습니다. 그 결과는 performance_incomplete_memory85.json에 보존하고 성능 비교에서 제외했습니다. 저장된 씬을 유지해 Unity를 정상 재시작한 뒤 네 차례를 새로 측정했습니다.",
        "폴더 최상위 before_*.png와 after_*.png 30장만 사용합니다. draft 이미지와 과거 Painterly 캡처를 최종 비교에 섞지 않습니다.",
        "고정 구도는 views.json의 카메라 위치와 주시점입니다. 원본 PNG 1920×1080을 직접 표시하며 이미지를 클릭하면 원본을 엽니다.",
        "촬영 코드는 구도마다 식생 캐시를 초기화하고 Edit 전용 함수로 가시 패킷을 준비한 뒤 6회 렌더와 대기 패킷 0을 확인합니다. 실제 Play의 프레임 예산을 바꾸지 않습니다.",
        "이전 촬영의 임시 재질과 카메라·하늘은 finally에서 복원합니다. baseline.json은 원본 프로필·dressing·재질 해시·구조·보조 식생 보존 원장입니다.",
        "성능은 초기 생성·정지·회전·첫 접근·재방문·복귀 정지의 진단 카메라 Play 계측입니다. 캡처 timing.txt의 동기 렌더 호출 시간은 프레임 성능으로 사용하지 않습니다.",
    ]
    limits = [
        "실제 플레이어 입력으로 연속 이동하면서 보는 획 떨림·경계 통과·시설 통행과 사용자의 미술 승인은 미검증입니다.",
        "산의 뾰족한 윤곽과 기존 삼각 면 형상은 남습니다. 이번 변경은 표면 명암이며 산 메시를 새로 만들지 않습니다.",
        "CPU 거리 검산은 고정 표면·광량 조건입니다. 실제 그림자·mip·SSAO 전환을 포함한 모든 픽셀의 명도 단조성을 증명하지 않습니다.",
        "Editor 진단 카메라 수치는 실제 플레이어 완주나 배포 빌드 성능이 아닙니다. 두 반복의 차이를 셰이더만의 인과 효과로 단정하지 않습니다.",
        "영상과 새 빌드는 만들지 않았습니다.",
    ]
    provenance = [
        "레거시 MandateOfInk 산은 Shader Graph를 사용하지 않습니다. 실제 S_ToonLitTemp의 3단 명암과 먹·한지 매핑 원리를 참고해 새 큰 먹 획을 구현했습니다. 원본 브러시 Graph/텍스처를 복사했다는 의미가 아닙니다.",
        "레거시 조사에는 Graph 12개와 subgraph 0개, 산 머티리얼·BaseMap GUID, 저장 씬 연결과 런타임 교체 검색 근거를 기록했습니다. 과거 스크린샷 당시 실행 상태를 재현했다는 뜻은 아닙니다.",
        "현재 프로필: Assets/_Project/Art/World/WorldCompact/InkLandscape/BroadBrush/Profile.asset. 직전 Painterly/Profile.asset에서 파생하며 원본 재질과 dressing을 유지합니다.",
        "표면 함수: Assets/_Project/Shaders/CompactInkLandscape.hlsl. 프로필/거리 검산: Scripts/Data/World/CompactInkLandscapeProfile.cs. 보존/적용/검사: Scripts/Editor/WorldMacro/CompactBroadBrushAuthoring.cs.",
    ]
    perf_headers = ["구간", "CPU p50 ms", "CPU p95", "CPU p99", "GPU p50 ms", "GPU p95", "GPU p99", "GPU 차이", "+0.5ms 기준", "CPU 8.33ms"]
    perf_rows = [[PHASES[r["phase"]]] + [f'{r["before"][k]:.3f} → {r["after"][k]:.3f}' for k in METRICS] +
                 [f'{r["gpuMedianDeltaMs"]:+.3f}', "통과" if r["gpuIncreaseBelowLimit"] else "조정 필요", "이하" if r["cpuMedianTargetMet"] else "초과"] for r in performance]
    raw_headers = ["실행", "측정 UTC", "구간", "프레임 수", "CPU p50", "CPU p95", "CPU p99", "GPU p50", "GPU p95", "GPU p99"]
    raw_rows = [[f"{side} {i}회", run["utc"], PHASES[p["phase"]], p["count"]] + [f"{p[k]:.3f}" for k in METRICS]
                for side, runs in (("이전", before), ("이후", after)) for i, run in enumerate(runs, 1) for p in run["phases"]]
    pair_headers = ["구간", "1회 전후 GPU p50 차이 ms", "2회 전후 GPU p50 차이 ms"]
    pair_rows = [[PHASES[r["phase"]]] + [f"{delta:+.3f}" for delta in r["pairedGpuP50DeltaMs"]] for r in performance]
    sub_headers = ["구도", "제출 패킷", "제출 인스턴스", "가시 삼각형", "카드 인스턴스", "메시·LOD", "주 식생 재질"]
    position_rows = [[v["label"], " / ".join(f'{v["eye"][a]:.2f}' for a in "xyz"), " / ".join(f'{v["target"][a]:.2f}' for a in "xyz"),
                      f'[이전]({v["before"]}) · [이후]({v["after"]})'] for v in views]
    commit_text = f"{peak_commit:.1%}" if peak_commit is not None else "미기록"
    memory_text = f"{peak_memory:,.0f}MiB" if peak_memory is not None else "미기록"
    perf_note = f"이후 CPU p50 {cpu_range[0]:.3f}~{cpu_range[1]:.3f}ms, GPU p50 {gpu_range[0]:.3f}~{gpu_range[1]:.3f}ms. 측정 메모리 최대 {memory_text}, 완료된 촬영·측정의 시스템 커밋 최대 {commit_text}. 중단된 측정은 아래 방법에 별도로 기록합니다."
    aggregate_note = "이전 2회와 이후 2회의 구간별 p50·p95·p99를 각각 중앙값으로 집계했습니다. 프레임 전체를 합친 백분위수가 아닙니다. GPU p50 증가가 +0.5ms 이상이면 조정 필요, 이후 CPU p50이 8.33ms를 넘으면 목표 미달로 표시합니다."
    hardware = before[0]
    hardware_note = f"Unity {hardware.get('unityVersion') or '미기록'} / GPU {hardware.get('gpuDevice') or '미기록'} / CPU {str(hardware.get('cpuDevice') or '미기록').strip()}."
    report = "\n\n".join([
        "# 산 전체를 덮는 큰 먹 면 — BroadBrush TEST", state_message,
        "직전 Painterly 판과 15개 동일 구도의 실제 1920×1080 PNG 비교입니다. [큰 화면 비교](REVIEW.html) · [레거시 출처 조사](LEGACY_SOURCE.md).",
        "## 변경과 보존\n\n" + "\n".join("- " + s for s in config_lines),
        "## 자동 검사\n\n" + f"보존·셰이더 검사 **{checks['status']}**, 촬영 제출 검사 **{summary['captureSubmissionAuditStatus']}**. " + audit_message +
        f"\n\n보존 원장: Transform {len(baseline['structure']):,}개, 산 Renderer 바인딩 {len(baseline['bindings']):,}개, 원본 파일 해시 {len(baseline['hashes']):,}개.\n\n" + "\n".join("- " + str(x) for x in checks["checks"]),
        f"거리 검산: {distance['samples']:,}개 표본, 명도 감소 {distance['decreases']:,}개. [원본](distance_response.json).",
        "## 1080p Play 성능\n\n" + hardware_note + " " + aggregate_note + "\n\n" + table(perf_headers, perf_rows, True) + "\n\n**" + gpu_message + "**\n\n**" + cpu_message + "**\n\n" + perf_note,
        "### 반복별 전후 차이\n\n" + table(pair_headers, pair_rows, True),
        "### 네 실행의 p50·p95·p99\n\n" + table(raw_headers, raw_rows, True),
        "## 15구도 제출 비교\n\n" + table(sub_headers, submission_rows, True) +
        "\n\n주 식생 observer의 캐시된 제출이며 씬 전체 GPU 패스/그림자 수가 아닙니다. 제출 인스턴스는 파트·LOD 중복을 포함하므로 고유 식물 수로 읽지 않습니다.",
        "## 카메라 위치와 원본 이미지\n\n" + table(["구도", "카메라 x/y/z", "주시점 x/y/z", "1920×1080 PNG"], position_rows, True),
        "## 촬영·계측 방법\n\n" + "\n".join("- " + s for s in method_lines),
        "## 출처\n\n[LEGACY_SOURCE.md](LEGACY_SOURCE.md)\n\n" + "\n".join("- " + s for s in provenance),
        "## 남은 검증\n\n" + "\n".join("- " + s for s in limits),
        "## 근거 파일\n\n[baseline.json](baseline.json) · [checks.json](checks.json) · [distance_response.json](distance_response.json) · [views.json](views.json) · [summary.json](summary.json)\n\n" +
        " · ".join(f"[{side} {i}회](performance_{side}_{i}.json)" for side in ("before", "after") for i in (1, 2)),
    ]) + "\n"
    options = "".join(f'<option value="{esc(v["id"])}">{esc(v["label"])}</option>' for v in views)
    lists = lambda rows: "".join(f"<li>{esc(row)}</li>" for row in rows)
    data = json.dumps(views, ensure_ascii=False).replace("</", "<\\/")
    tokens = {
        "OPTIONS": options, "VIEWS": data, "STATE": esc(state_message), "GPU": esc(gpu_message), "CPU": esc(cpu_message),
        "CONFIG": lists(config_lines), "METHOD": lists(method_lines), "PROVENANCE": lists(provenance), "LIMITS": lists(limits),
        "CHECKS": lists(checks["checks"]), "CHECK_STATUS": esc(checks["status"]), "AUDIT_STATUS": summary["captureSubmissionAuditStatus"],
        "SUBMISSION_MESSAGE": esc(audit_message), "SUBMISSIONS": table(sub_headers, submission_rows),
        "DISTANCE": esc(f"고정 표면·광량 조건 {distance['samples']:,}개 표본 / 명도 감소 {distance['decreases']:,}개."),
        "PERFORMANCE": table(perf_headers, perf_rows), "RAW": table(raw_headers, raw_rows), "PAIRS": table(pair_headers, pair_rows),
        "PERF_NOTE": esc(perf_note), "AGGREGATE_NOTE": esc(aggregate_note), "HARDWARE": esc(hardware_note),
        "BASELINE": esc(f"Transform {len(baseline['structure']):,}개 · 산 Renderer 바인딩 {len(baseline['bindings']):,}개 · 원본 파일 해시 {len(baseline['hashes']):,}개"),
    }
    page = PAGE
    for key, value in tokens.items():
        page = page.replace(f"__{key}__", value)
    if re.search(r"__[A-Z_]+__", page):
        raise ValueError("HTML 템플릿에 채워지지 않은 항목이 있습니다.")
    (out / "REPORT.md").write_text(report, encoding="utf-8")
    (out / "REVIEW.html").write_text(page, encoding="utf-8")
    (out / "summary.json").write_text(json.dumps(summary, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    return summary


PAGE = '''<!doctype html>
<html lang="ko"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>산 전체를 덮는 큰 먹 면 · BroadBrush 비교</title>
<style>
*{box-sizing:border-box}body{margin:0;background:#191c19;color:#e9e4d6;font:16px/1.65 system-ui,"Malgun Gothic",sans-serif}
main{max-width:1984px;margin:auto;padding:28px 32px}h1{font-size:34px;margin:0}p{max-width:1200px}a{color:#c7d9c4;overflow-wrap:anywhere}
.tag{color:#d4bf93;font-size:13px;letter-spacing:.05em}.intro{color:#c5c8bb}nav,.controls{display:flex;gap:10px;align-items:center;flex-wrap:wrap;margin:18px 0}
button,select{font:inherit;background:#30392f;color:#fff;border:1px solid #697460;border-radius:5px;padding:8px 13px;max-width:100%;cursor:pointer}
button:hover{background:#46543f}button[aria-pressed="true"]{background:#647451;border-color:#c8d6b7}button:focus-visible,select:focus-visible,a:focus-visible{outline:3px solid #d9c699;outline-offset:3px}
figure{margin:0;min-width:0}img{width:100%;height:auto;display:block;aspect-ratio:16/9;object-fit:contain;background:#111410}figcaption{padding:8px 0;color:#c4c6b7}
.comparison{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:16px}.single{max-width:1920px}.note{border-left:3px solid #b8ba96;background:#262d25;padding:12px 18px;max-width:none}
.facts{display:flex;gap:14px;flex-wrap:wrap;margin:22px 0}.facts div{background:#262d25;padding:12px 18px}.facts strong{display:block;font-size:24px}
details{margin:18px 0;padding:18px;background:#222820;min-width:0}summary{font-size:19px;cursor:pointer}.scroll{overflow:auto;max-width:100%}table{border-collapse:collapse;width:100%;margin:16px 0;font-size:14px}
th,td{padding:9px 12px;border-bottom:1px solid #434c3e;text-align:left;white-space:nowrap}th{color:#c3d1b6}li{margin:7px 0;overflow-wrap:anywhere}#position{overflow-wrap:anywhere;color:#bfc5b6}footer{color:#b0b6a6;margin:30px 0}footer a{display:inline-block;margin-right:14px}
[hidden]{display:none!important}@media(max-width:850px){main{padding:16px}h1{font-size:27px}.comparison{grid-template-columns:1fr}.facts{gap:8px}.facts div{padding:10px 12px}details{padding:13px}}
</style></head><body><main>
<div class="tag">W_DEMO_COMPACT · TEST · 실제 UNITY PNG</div><h1>산 전체를 덮는 큰 먹 면</h1>
<p class="intro">가는 잔 획 대신 넓게 겹치는 먹 면을 산의 명암에 적용한 비교입니다. 지면·식생·대기는 직전 Painterly 기준을 유지합니다. 기본 화면은 이후 이미지 한 장이며, 이전/이후 전환과 나란히 비교를 지원합니다.</p>
<nav><label for="view">구도</label><select id="view">__OPTIONS__</select><button id="previous" type="button" aria-label="이전 구도">←</button><button id="next" type="button" aria-label="다음 구도">→</button><button id="mode" type="button" aria-pressed="false">나란히 비교</button><a href="REPORT.md">검사 보고서</a><a href="LEGACY_SOURCE.md">레거시 출처</a><a href="../Painterly/REVIEW.html">직전 판 검토</a></nav>
<div class="controls" id="stageControls"><label for="stage">큰 이미지</label><select id="stage"><option value="before">이전 · Painterly</option><option value="after" selected>이후 · 큰 먹 면</option></select><button id="toggle" type="button">이전으로 전환</button><span>원본 1920×1080 · 이미지를 클릭하면 원본 열기</span></div>
<figure class="single" id="single"><a id="largeLink"><img id="large" width="1920" height="1080" alt="이후 큰 먹 면"></a><figcaption id="largeLabel"></figcaption></figure>
<div class="comparison" id="comparison" hidden><figure><a id="beforeLink"><img id="before" width="1920" height="1080" alt="이전 Painterly"><figcaption>이전 · Painterly</figcaption></a></figure><figure><a id="afterLink"><img id="after" width="1920" height="1080" alt="이후 큰 먹 면"><figcaption>이후 · 큰 먹 면</figcaption></a></figure></div>
<p id="position" aria-live="polite"></p><p id="imageError" role="alert" hidden>이미지 로드에 실패했습니다. 원본 PNG 경로를 확인하세요.</p>
<div class="facts"><div><strong>80~240m</strong>획의 전체 폭</div><div><strong>420~1000m</strong>획의 전체 길이</div><div><strong>42~64%</strong>넓은 획 몸통 유지</div><div><strong>지형·식생 보존</strong>산 파생 재질만 변경</div></div>
<p class="note"><b>__STATE__</b><br>__GPU__<br>__CPU__<br>실제 플레이어 연속 이동과 최종 미술 승인은 미검증입니다.</p>
<details open><summary>바뀐 값과 유지한 값</summary><ul>__CONFIG__</ul></details>
<details><summary>실제 레거시 출처</summary><p><a href="LEGACY_SOURCE.md">Graph 노드·텍스처 GUID·산 머티리얼 연결 조사</a></p><ul>__PROVENANCE__</ul></details>
<details open><summary>1080p Play 성능 · 이전 2회 → 이후 2회</summary><p>__AGGREGATE_NOTE__</p><p>__HARDWARE__</p>__PERFORMANCE__<p>__PERF_NOTE__</p><details><summary>반복별 전후 GPU 차이</summary>__PAIRS__</details><details><summary>네 실행의 모든 p50·p95·p99</summary>__RAW__</details></details>
<details><summary>15구도 식생 제출 비교</summary><p>제출 검사 <b>__AUDIT_STATUS__</b> · __SUBMISSION_MESSAGE__</p>__SUBMISSIONS__<p>주 식생 observer의 제출이며 씬 전체 GPU 패스 수가 아닙니다. 제출 인스턴스는 메시 파트·LOD 중복을 포함하며 고유 식물 수가 아닙니다.</p></details>
<details><summary>원본 보존 · 셰이더 · 거리 검사</summary><p>보존·셰이더 검사 <b>__CHECK_STATUS__</b>. __BASELINE__.</p><ul>__CHECKS__</ul><p>__DISTANCE__ 모든 화면 픽셀의 명도 단조성을 보증하지는 않습니다.</p></details>
<details><summary>촬영과 계측 방법</summary><ul>__METHOD__</ul></details><details><summary>아직 검증하지 않은 항목</summary><ul>__LIMITS__</ul></details>
<footer>15개 동일 구도 · 이전/이후 30장 · 실제 1920×1080 PNG · 영상/새 빌드 없음<br><a href="baseline.json">보존 원장</a><a href="checks.json">검사 원본</a><a href="distance_response.json">거리 응답</a><a href="views.json">카메라 구도</a><a href="summary.json">집계</a><a href="performance_before_1.json">이전 1회</a><a href="performance_before_2.json">이전 2회</a><a href="performance_after_1.json">이후 1회</a><a href="performance_after_2.json">이후 2회</a></footer>
</main><script>
const views=__VIEWS__, $=id=>document.getElementById(id);let single=true;
function render(){
 const v=views.find(x=>x.id===$('view').value)||views[0],stage=$('stage').value;
 $('imageError').hidden=true;
 for(const side of ['before','after']){$(side).src=v[side];$(side+'Link').href=v[side];$(side).alt=v.label+' · '+(side==='before'?'이전 Painterly':'이후 큰 먹 면');}
 $('large').src=v[stage];$('largeLink').href=v[stage];$('large').alt=v.label+' · '+$('stage').selectedOptions[0].textContent;
 $('largeLabel').textContent=$('large').alt;$('toggle').textContent=stage==='after'?'이전으로 전환':'이후로 전환';
 $('single').hidden=!single;$('comparison').hidden=single;$('stageControls').hidden=!single;
 $('mode').textContent=single?'나란히 비교':'한 장 크게';$('mode').setAttribute('aria-pressed',String(!single));
 const vec=p=>[p.x,p.y,p.z].map(x=>x.toFixed(2)).join(', ');
 $('position').textContent=(views.indexOf(v)+1)+' / '+views.length+' · '+v.label+' · 카메라 ('+vec(v.eye)+') · 주시점 ('+vec(v.target)+')';
 if(location.hash.slice(1)!==v.id)history.replaceState(null,'','#'+v.id);
}
function move(delta){$('view').selectedIndex=($('view').selectedIndex+delta+views.length)%views.length;render();}
function readHash(){let value;try{value=decodeURIComponent(location.hash.slice(1));}catch{return;}if(views.some(v=>v.id===value))$('view').value=value;}
$('view').onchange=render;$('stage').onchange=render;$('previous').onclick=()=>move(-1);$('next').onclick=()=>move(1);
$('toggle').onclick=()=>{$('stage').value=$('stage').value==='after'?'before':'after';render();};$('mode').onclick=()=>{single=!single;render();};
for(const id of ['before','after','large'])$(id).addEventListener('error',()=>{$('imageError').hidden=false;});
window.addEventListener('hashchange',()=>{readHash();render();});
document.addEventListener('keydown',e=>{if(['SELECT','INPUT','TEXTAREA','BUTTON','A'].includes(e.target.tagName))return;if(e.key==='ArrowLeft'){e.preventDefault();move(-1);}if(e.key==='ArrowRight'){e.preventDefault();move(1);}});
readHash();render();
</script></body></html>'''


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--out", type=Path, default=DEFAULT_OUT)
    parser.add_argument("--check-only", action="store_true", help="자료 검증과 집계만 수행하며 파일을 쓰지 않습니다.")
    args = parser.parse_args()
    try:
        result = build(args.out, args.check_only)
    except (ValueError, KeyError, TypeError, OSError) as error:
        parser.exit(2, str(error) + "\n")
    print(json.dumps({k: v for k, v in result.items() if k not in ("performance", "submissions", "checks")}, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
