"""Record the manual review of the immutable 20260927T093745313 LOD capture set.

This reads artifacts only. It does not run Unity or edit production assets.
The visual judgements below apply to these exact, directly inspected images.
"""
from pathlib import Path
import hashlib
import json
import math
from datetime import datetime, timezone

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "Art/World/Compact/Rebuild/Architecture296"
FOLDER = OUT / "Analysis/LodVisibility/20260927T093745313"
RECEIPT = FOLDER / "visibility.json"


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def evidence(path, purpose):
    return {"path": path.relative_to(ROOT).as_posix(), "sha256": sha(path), "purpose": purpose}


def main():
    receipt = read(RECEIPT)
    lods_path = OUT / "CrossingLods/lods.json"
    lods = read(lods_path)
    parapet = [s for s in lods["Sources"] if "SM_CW_Parapet.prefab" in s["Source"]]
    vertices = [v for s in parapet for v in s["Levels"][0]["Vertices"]]
    source_size = {c: max(v[c] for v in vertices) - min(v[c] for v in vertices) for c in "xyz"}
    scale = .88 / source_size["y"]
    rail_width = source_size["x"] * scale
    annotations = {
        "bridge-capital_shared_stone_bridge": [
            "LOD0/1 retain the continuous deck, stone parapets, arch bodies and broad piers.",
            "Forced LOD2 at this close pose shows repeated triangular notches in the lower parapet faces. Arch bodies, piers and deck remain visible.",
        ],
        "bridge-hyeongang__temple_bridge": [
            "LOD0/1 retain both parapet rows, arch bodies, piers and the supported bank connection.",
            "Forced LOD2 shows the same small lower-parapet notches. No whole arch, deck or pier disappears.",
            "The latest local approach profile is present. A small land-contact side opening is shared by all three levels; it is not an LOD omission.",
        ],
        "bridge-mountain_hwanggyeong_main_bridge": [
            "Deck, stringers, transverse bents, posts, X braces and rails remain visible at all three levels.",
            "Far geometry is more faceted, with no newly empty structural section observed.",
        ],
        "cheolong-entry": [
            "Visible venue columns, wall panels, rafters, roof underside and eave strip remain at all levels.",
            "Tiled eave detail simplifies at LOD1/2. The foreground precinct gate obscures some of the target in all three images.",
        ],
        "cheolong-eye": [
            "Visible floor, posts, walls, rafters and ceiling remain at all three levels.",
            "Large bright rectangles are the same authored entrances in all levels, not missing wall meshes.",
            "A thin bright line beside the right interior post is shared by all three levels; this review does not identify it as a new LOD defect.",
        ],
        "hwanggyeong-entry": [
            "Visible columns, wall panels, roof underside and eave beam remain at all levels.",
            "Eave tiles simplify at LOD1/2. The foreground gate and wall obscure some of the target in all levels.",
        ],
        "hwanggyeong-eye": [
            "Visible floor, walls, ceiling, beams and posts remain at all three levels. Both large entrances remain intentional openings.",
            "No LOD-specific wall or roof hole was observed.",
        ],
    }
    views = []
    estimates = []
    for view in receipt["Views"]:
        hidden = FOLDER / Path(view["BackgroundFile"]).name
        assert sha(hidden) == view["BackgroundSHA256"]
        shots = []
        for shot in view["Shots"]:
            path = FOLDER / Path(shot["File"]).name
            assert sha(path) == shot["SHA256"]
            meshes = [m for g in shot["Groups"] for m in g["Meshes"]]
            shots.append({
                "level": shot["Level"], "file": path.relative_to(ROOT).as_posix(),
                "sha256": shot["SHA256"], "directlyInspected": True,
                "changedPixelsAgainstHidden": shot["ChangedPixels"],
                "changedRatioToLod0": shot["ChangedRatioToLod0"],
                "groupCount": len(shot["Groups"]), "rendererCount": len(meshes),
                "triangles": sum(m["Triangles"] for m in meshes),
                "copySerializedFallbackRendererCount": sum(m["CopySerializedFallback"] for m in meshes),
            })
        views.append({
            "id": view["Id"], "camera": view["Camera"],
            "hidden": {"file": hidden.relative_to(ROOT).as_posix(), "sha256": sha(hidden), "directlyInspected": True},
            "shots": shots, "observations": annotations[view["Id"]],
            "wholeTargetVisibleAtAllLevels": True,
            "verdict": "retain_with_far_parapet_limitation" if "stone_bridge" in view["Id"] or "temple_bridge" in view["Id"] else "no_structural_lod_omission_observed",
        })
        if view["Id"] not in ("bridge-capital_shared_stone_bridge", "bridge-hyeongang__temple_bridge"):
            continue
        bounds = [m["WorldBounds"] for s in view["Shots"] for g in s["Groups"] for m in g["Meshes"]]
        low = {c: min(b["m_Center"][c] - b["m_Extent"][c] for b in bounds) for c in "xyz"}
        high = {c: max(b["m_Center"][c] + b["m_Extent"][c] for b in bounds) for c in "xyz"}
        size = max(high[c] - low[c] for c in "xyz")
        radius = math.sqrt(sum(((high[c] - low[c]) / 2) ** 2 for c in "xyz"))
        projections = []
        for bias in (1, 2):
            distance = size * bias / (2 * math.tan(math.radians(30)) * .085)
            for height in (1080, 2160):
                pixels_per_meter = height * .085 / (bias * size)
                correction = distance / (distance - radius)
                projections.append({
                    "verticalPixels": height, "lodBias": bias,
                    "estimatedTransitionDistanceMetersAtFov60": distance,
                    "entireParapetHeightPixelsAtGroupCentre": .88 * pixels_per_meter,
                    "entireModuleWidthPixelsAtGroupCentre": rail_width * pixels_per_meter,
                    "conservativeNearestExtentParapetHeightPixels": .88 * pixels_per_meter * correction,
                    "conservativeNearestExtentModuleWidthPixels": rail_width * pixels_per_meter * correction,
                })
        estimates.append({"view": view["Id"], "groupSizeFromCapturedBoundsMeters": size,
                          "boundsSphereRadiusMeters": radius, "projections": projections})
    source_paths = [
        (RECEIPT, "Immutable live capture receipt and per-renderer mesh path/SHA/flags/bounds."),
        (OUT / "CrossingLods/sources.json", "Exported original source parts, source SHA and original face-corner UVs."),
        (lods_path, "Actual QEM LOD arrays and triangle counts; bounds preserved, detailed faces simplified."),
        (OUT / "CrossingLods/lod-audit.json", "Independent per-part QEM triangle and outer-bounds measurements."),
        (ROOT / "Oheangbu/Assets/_Project/Scripts/Editor/WorldMacro/CompactArchitecture296.CrossingSources.cs", "Thresholds .30/.085/.001; assembled native mesh branch versus CopySerialized fallback."),
        (ROOT / "Oheangbu/Assets/_Project/Scripts/Editor/WorldMacro/CompactArchitecture296.BridgeStructure.cs", "Uniform parapet scale sets source total height to .88m."),
        (ROOT / "Oheangbu/ProjectSettings/QualitySettings.asset", "Mobile lodBias1, PC lodBias2; both shown because capture receipt does not record active quality."),
        (ROOT / "Oheangbu/Assets/_Project/Scripts/Presentation/PlayerLodClothController.cs", "Existing project perspective size/distance/lodBias calculation."),
        (ROOT / "Tools/Blender/build_crossing_lods296.py", "Source-part QEM implementation; no source mesh mutation."),
    ]
    report = {
        "reviewedUtc": datetime.now(timezone.utc).isoformat(),
        "captureUtc": receipt["Utc"],
        "verdict": "retain_current_geometry_with_documented_far_parapet_simplification",
        "scope": "All21 forced PNGs and all7 target-hidden PNGs directly inspected. No Unity call, asset upload, production source change, or scene save during this review.",
        "gpuConclusion": "No whole-target GPU omission was reproduced in these seven views. Every inspected final renderer has CopySerializedFallback=false. This does not validate the unexercised fallback path or invisible intermediate module assets.",
        "visualConclusion": "Stone bridge LOD2 parapet notches are visible in forced close views. Main arches, piers, deck, timber frames and visible venue roofs/walls remain. At the authored far transition, the entire .88m parapet is under one pixel tall at1080p even with the conservative nearest-extent correction and lodBias1; actual holes are smaller. Retain the geometry and record the limitation.",
        "restoration": {k: receipt[k] for k in ("SceneFileUnchanged", "ActiveStatesRestored", "RendererStatesRestored", "AutomaticLodRestored", "SavedSceneReopened", "SceneSHA256Before", "SceneSHA256After", "Error")},
        "counts": {"forcedImagesInspected": 21, "hiddenImagesInspected": 7, "lowContributionImages": sum(s["NearZeroContribution"] for v in receipt["Views"] for s in v["Shots"]), "fallbackRendererOccurrences": sum(s["copySerializedFallbackRendererCount"] for v in views for s in v["shots"])},
        "views": views,
        "farParapetSource": {"asset": parapet[0]["Source"], "sourceSha256": parapet[0]["SourceSha256"], "publisher": "KCISA / Hwaseong Fortress Gate", "license": "Previously owned Unity Asset Store distribution; original EULA retained, not CC0", "partTriangleCounts": [s["ActualTriangles"] for s in parapet], "sourceSizeMeters": source_size, "uniformScale": scale, "assembledWholeModuleWidthMeters": rail_width, "assembledWholeModuleHeightMeters": .88},
        "transitionEstimates": estimates,
        "estimateMethod": "Screen pixels at group centre = detailMeters/groupSize * verticalPixels * .085/lodBias. Distance at FOV60 = groupSize*lodBias/(2*tan(30deg)*.085). Nearest-extent bound multiplies by distance/(distance-boundsSphereRadius). Whole-module dimensions bound each notch; no unsupported exact notch measurement is claimed. Assumes a perspective camera aimed toward the group. This is a geometric estimate, not an additional live transition capture. At2160p/bias1 the nearest-bound full parapet can reach about1.8px; no zero-pop guarantee is made.",
        "sources": [evidence(p, purpose) for p, purpose in source_paths],
        "externalSources": [
            {"url": "https://docs.unity.com/en-us/engine/6000.0/manual/analysis/graphics-performance-profiling/simplifying-distant-meshes-with-level-of-detail-lod/class-lodgroup", "supports": "LOD transition is based on relative screen height."},
            {"url": "https://docs.unity3d.com/ScriptReference/QualitySettings-lodBias.html", "supports": "Increasing lodBias delays lower-detail LOD transitions."},
        ],
        "decision": "Parent accepted retaining the current geometry after screen-size review. Production C# remains unchanged. An optional future parapet-specific LOD1 reuse in LOD2 would be cosmetic; it is not required by this evidence.",
    }
    out = OUT / "Analysis/LodVisibility/visual-review.json"
    out.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    md = ["# Architecture296 LOD 실가시성 검토", "", "## 판정", "", "현재 형상을 유지한다. 21장 강제 LOD와 7장 숨김 기준 이미지를 모두 직접 확인했다. 교량 전체나 대전의 벽·지붕이 사라지는 GPU 누락은 재현되지 않았다. 황경 공용 석교와 현강 사찰 석교의 LOD2 난간에는 근거리 강제 표시에서 작은 삼각형 형태 손실이 보인다.", "", "## 실제 전환 크기", "", "LOD1→2 기준은 화면 높이 8.5%%이며 난간 전체 높이는 0.88m이다. 전체 난간 부재 폭은 %.3fm이다. 아래 값은 구멍 자체보다 큰 **전체 부재**의 투영 크기다." % rail_width, "", "| 교량 | LODGroup 크기 | 1080p/bias1 전체 높이 | 보수적 최근접 높이 | 전체 부재 폭 |", "|---|---:|---:|---:|---:|"]
    for row in estimates:
        p = row["projections"][0]
        md.append(f'| {row["view"]} | {row["groupSizeFromCapturedBoundsMeters"]:.3f}m | {p["entireParapetHeightPixelsAtGroupCentre"]:.3f}px | {p["conservativeNearestExtentParapetHeightPixels"]:.3f}px | {p["entireModuleWidthPixelsAtGroupCentre"]:.3f}px |')
    md += ["", "PC 설정의 lodBias2에서는 전환 시 크기가 이보다 약 절반이다. 2160p/bias1에서는 전체 난간이 최대 약1.8px까지 보일 수 있어 작은 팝이 전혀 없다고 보장하지 않는다. 계산은 카메라가 대상 방향을 보는 원근 투영 추정이며, 별도 원거리 실촬영 결과로 표현하지 않는다. 기준과 bias 의미는 [Unity LODGroup](https://docs.unity.com/en-us/engine/6000.0/manual/analysis/graphics-performance-profiling/simplifying-distant-meshes-with-level-of-detail-lod/class-lodgroup), [Unity lodBias](https://docs.unity3d.com/ScriptReference/QualitySettings-lodBias.html)에 따른다.", "", "## 직접 확인한 형태", ""]
    for view in views:
        md.append("### " + view["id"])
        md.append("")
        md.extend("- " + s for s in view["observations"])
        md.append("")
    md += ["## 출처와 검증 범위", "", "- KCISA `SM_CW_Parapet` 두 재질 부분은 각240면에서 LOD1 71/72면, LOD2 36/24면으로 줄었다. 반복 삼각틈은 이 원거리 형상 단순화와 일치하며, CPU/GPU 전면 누락 근거가 아니다.", "- 실제 캡처 renderer 중 CopySerialized fallback 경로는0개다. 조립된 최종 메시의 가시성을 확인했으며, 사용되지 않은 fallback 자체의 안전성까지 판정하지 않는다.", "- 숨김 기준7장에서도 대상 제거를 확인했다. 단순 changed-pixel 값만으로 개별 메시 가시성을 대신 판정하지 않았다.", "- 씬 SHA, active/renderer 상태, 자동LOD 복원 및 저장된 씬 재열기 모두 receipt에서 PASS다.", "- 28장 SHA, renderer/삼각형 수, 출처 파일 SHA, 계산식은 `visual-review.json`에 보존했다.", "- production C#·씬·에셋은 변경하지 않았다. 원본28장도 수정하지 않았다.", "", "검토 대상: `20260927T093745313/visibility.json`. 이 기록은 해당 캡처 세트에 한정된다.", ""]
    (out.parent / "visual-review.md").write_text("\n".join(md), encoding="utf-8")
    final_path = OUT / "Analysis/final-bridge-visual-review.json"
    final = read(final_path)
    final["lodVisibilityReview"] = {"report": out.relative_to(ROOT).as_posix(), "reportSha256": sha(out), "reviewedUtc": report["reviewedUtc"], "verdict": report["verdict"], "forcedImagesInspected": 21, "hiddenImagesInspected": 7, "limitation": "Small LOD2 stone-parapet notches visible when forced near; below one full-parapet pixel at the1080p authored transition. Main support geometry remains. Does not replace the earlier dated normal-camera image hashes or current vehicle evidence."}
    final_path.write_text(json.dumps(final, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"report": str(out), "images": 28, "shaChecks": "PASS", "sourceEvidence": len(report["sources"]), "verdict": report["verdict"]}, ensure_ascii=False))


if __name__ == "__main__":
    main()
