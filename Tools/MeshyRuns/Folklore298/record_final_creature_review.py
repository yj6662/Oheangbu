"""Record already-viewed final stills and verify their receipts; never edits models or manifest."""
import hashlib
import json
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
BASE = ROOT / "Art/Characters/Folklore298"
FINAL = {
    "bulgasari": {
        "run": "05-cleanup", "length": 3.2, "folders": ["Review", "Closeup"],
        "notes": [
            "오른쪽 엉덩이의 작은 구멍에 3개 면을 추가했다. 근접 사진에서는 옅고 평평한 보정 면과 경계가 보인다.",
            "몸통이 바닥에 닿는 옆으로 누운 사망 자세다. 앞다리 아래의 작은 원본 표면 결함은 이번 엉덩이 보정 범위에 포함하지 않았다.",
            "보정 후 Idle/Walk/Death와 근접 Idle을 직접 봤다. Attack/Hit/Stun은 같은 리그의 이전 run-04 사진과 최종 수치 검사를 근거로 하며, 보정본의 새 사진으로 주장하지 않는다."
        ], "prior": "04",
    },
    "fox_spirit": {
        "run": "07-cleanup", "length": 2.2, "folders": ["Review", "CloseupTail", "CloseupNeck"],
        "notes": [
            "꼬리와 목 주변의 지정된 3개 경계에 총 39개 면을 추가했다. 큰 검은 구멍은 줄었지만 꼬리에 작은 검은 이음새와 옅은 보정 면이 남는다.",
            "근접 목 사진에도 보정 면의 명암 차이와 이번에 선택하지 않은 좁은 원본 틈이 보인다. 모든 표면 결함을 제거한 결과가 아니다.",
            "사망 자세의 몸통은 접지하지만 큰 원본 꼬리는 위로 들려 있고 다소 뻣뻣하다.",
            "보정 후 Idle/Walk/Death와 두 근접 Idle을 직접 봤다. 나머지 3개 동작은 이전 run-04 사진과 최종 수치 검사로 구분한다."
        ], "prior": "04",
    },
    "imugi": {
        "run": "09", "length": 6, "folders": ["Review"],
        "notes": [
            "여섯 정지 화면에서 몸의 연결이 유지된다. 사망 자세는 들린 목을 낮추고 긴 몸을 옆으로 눕힌다.",
            "실제 이동은 Head 전용 CR_Idle과 절차적 BodyFollow 조합이다. Generic Walk 사진은 별도 오프라인 검토용이며 실행 중 BodyFollow 검증을 대신하지 않는다."
        ],
    },
    "cheongryong": {
        "run": "15", "length": 8, "folders": ["Review", "ReviewOther"],
        "notes": [
            "최종 여섯 정지 화면을 직접 확인했다. 사망 자세는 몸을 95도 기울이고 다리를 안쪽으로 접어 앞몸통을 바닥에 둔다.",
            "원형의 굽은 배와 꼬리가 조금 떠 있는 형태는 남는다. 이 한계를 포함해 기술 후보로 유지하며 추가 조형은 하지 않았다.",
            "살아 있는 상태의 CR_*는 Head 전용이다. Body_01부터 Body_24의 움직임은 기존 절차적 Follow가 맡는다. 사망 시 Follow를 끄는 연동이 필요하다."
        ],
    },
}


def sha(path):
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def rel(path):
    return path.relative_to(ROOT).as_posix()


def write(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def main():
    reviewed_at = datetime.now(timezone.utc).isoformat()
    rows = []
    for ident, plan in FINAL.items():
        folder = BASE / "Derivatives" / ident / ("run-" + plan["run"])
        report = read(folder / "rig-report.json")
        trip = read(folder / "roundtrip.json")
        source = Path(report["source"])
        actual_source = sha(source)
        assert actual_source == report["sourceSha256"]
        assert report["sourceHashUnchanged"]
        assert trip["status"] == "TECHNICAL_ROUNDTRIP_PASS"
        assert all(m["triangleMaterialUvSignatureEqual"] for m in trip["meshes"])
        outputs = {name: sha(folder / name) for name in ("candidate.blend", "creature.fbx")}
        assert outputs == report["outputSha256"]
        captures = []
        for subdir in plan["folders"]:
            receipt_path = folder / subdir / "receipts.json"
            receipts = read(receipt_path)
            assert receipts["candidateSha256"] == outputs["candidate.blend"]
            for capture in receipts["captures"]:
                capture = dict(capture)
                picture = folder / subdir / capture["path"]
                assert sha(picture) == capture["sha256"]
                capture.update(path=rel(picture), receipt=rel(receipt_path), directlyViewed=True)
                captures.append(capture)
        prior = None
        if plan.get("prior"):
            old = BASE / "Derivatives" / ident / ("run-" + plan["prior"]) / "Review/visual-review.json"
            assert old.exists()
            prior = {"path": rel(old), "sha256": sha(old), "scope": "Earlier unchanged rig motion; original surface, before bounded face cleanup"}
        cleanup = report.get("cleanup")
        deforms = report["deformation"]
        row = {
            "id": ident, "run": plan["run"], "reviewedAtUtc": reviewed_at,
            "status": "OFFLINE_TECHNICAL_CANDIDATE_WITH_VISUAL_LIMITATIONS",
            "reviewer": "natural_terrain_research; direct view_image inspection of each listed still",
            "scope": "Blender stills, sampled deformation and FBX roundtrip. No Unity runtime verdict is issued by this report.",
            "userArtApproved": False,
            "rootTechnicalCandidateAcceptance": "Parent accepted these final candidates with the stated visual limitations; this is not user art acceptance.",
            "candidate": rel(folder / "candidate.blend"), "model": rel(folder / "creature.fbx"),
            "targetLengthM": plan["length"], "outputSha256": outputs,
            "source": rel(source), "sourceSha256": actual_source, "sourceFileByteIdentical": True,
            "geometryPolicy": report["geometryPolicy"], "boundedCleanup": cleanup,
            "captures": captures, "previousSixClipVisualReview": prior,
            "notes": plan["notes"], "rigReport": rel(folder / "rig-report.json"),
            "roundtripReport": rel(folder / "roundtrip.json"),
            "technical": {
                "clips": [d["clip"] for d in deforms], "clipCount": len(deforms),
                "deformationSamples": sum(d["sampleCount"] for d in deforms),
                "maximumCoincidentSeamGapNativeM": max(d["maxCoincidentSeamGapM"] for d in deforms),
                "maximumInfluences": max(w["maximumInfluences"] for w in report["weights"]),
                "derivedVertices": sum(m["importedVertices"] for m in trip["meshes"]),
                "derivedTriangles": sum(m["triangles"] for m in trip["meshes"]),
                "triangleMaterialUvRoundtripEqual": True,
                "maximumRestPositionErrorNativeM": trip["maximumRestPositionErrorM"],
                "maximumSampledPoseErrorNativeM": trip["maximumSampledPoseErrorM"],
                "units": "Native approximately 1m-long model before importer uniform target-length scaling",
            },
            "remainingLimits": [
                "Restrained Attack/Hit/Stun stills do not prove full-speed combat readability.",
                "No dedicated jaw or facial animation.",
                "Original PBR source textures are retained; final ink-material appearance is evaluated in the Unity task.",
                "Runtime foot contacts, combat timing and animated culling belong to separate Unity evidence.",
            ],
        }
        # Preserve the previously completed imugi review. A new final report is separate.
        destination = folder / "Review" / ("final-visual-review.json" if ident == "imugi" else "visual-review.json")
        write(destination, row)
        row["visualReview"] = rel(destination)
        rows.append(row)
    summary = {
        "schemaVersion": 1, "generatedAtUtc": reviewed_at,
        "status": "FOUR_OFFLINE_TECHNICAL_CANDIDATES_VERIFIED_WITH_STATED_VISUAL_LIMITS",
        "sourceFilesVerified": 4, "totalBoundedTrianglesAdded": sum((r["boundedCleanup"] or {}).get("trianglesAdded", 0) for r in rows),
        "originalVerticesMoved": 0, "originalFacesChanged": 0, "originalCornerUvsChanged": 0,
        "directlyViewedFinalStills": sum(len(r["captures"]) for r in rows),
        "manifestModified": False, "unityInvoked": False, "paidCalls": 0, "rows": rows,
    }
    target = BASE / "Analysis/nonhuman-rig-summary.json"
    write(target, summary)
    print(json.dumps({"report": rel(target), "sources": 4, "caps": summary["totalBoundedTrianglesAdded"], "stills": summary["directlyViewedFinalStills"], "status": summary["status"]}))


if __name__ == "__main__":
    main()
