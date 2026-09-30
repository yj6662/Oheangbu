"""Write final film evidence after direct inspection of all eight decoded sheets.

Explicit --all-sheets-viewed is an operator attestation, not an image-quality test.
This script verifies file identities and separates numeric checks from visual notes.
"""
import argparse
from datetime import datetime, timezone
import json

import encode_films298 as E


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--all-sheets-viewed", action="store_true", required=True)
    parser.parse_args()
    sheets = json.loads((E.BASE / "Analysis/film-decoded-review-frames.json").read_text(encoding="utf-8-sig"))
    if len(sheets) != 8:
        raise ValueError("Expected eight independently inspected decoded sheets")
    rows = []
    for sheet in sheets:
        folder = E.BASE / "Captures" / sheet["id"]
        film, frames, _, duration = E.validate_receipt(folder / "film-receipt.json")
        video = json.loads((folder / "video-receipt.json").read_text(encoding="utf-8-sig"))
        if film.get("samplingFps") != 24 or video["outputFps"] != 24:
            raise ValueError("Final report only accepts the new 24fps captures")
        if E.sha(folder / "film-receipt.json") != video["receiptSha256"]:
            raise ValueError("Movie/capture receipt mismatch")
        if E.sha(E.Path(sheet["sheet"])) != sheet["sheetSha256"] or E.sha(folder / "animation.mp4") != sheet["sourceSha256"]:
            raise ValueError("Directly inspected decoded sheet or its video changed")
        if not all(m["beforeSha256"] == m["afterSha256"] for m in film["sourceMeshes"]):
            raise ValueError("Source mesh fingerprint differs")
        for output in video["outputs"]:
            if E.sha(E.ROOT / output["path"]) != output["sha256"]:
                raise ValueError("Validated run-local movie changed")
        sheet = dict(sheet, directlyViewed=True)
        rows.append({"id": film["id"], "displayName": film["displayName"],
                     "movie": "Captures/" + film["id"] + "/animation.mp4", "webm": "Captures/" + film["id"] + "/animation.webm",
                     "seconds": duration, "capturedPoseSamples": len(frames), "samplingFps": 24, "containerFps": 24,
                     "roles": video["roles"], "sourceModelSha256": film["sourceModelSha256"], "sourceMeshesUnchanged": True,
                     "fixedCameraAllSixRoles": True, "frameEnvelopeCheckPassed": True,
                     "outputs": video["outputs"], "filmReceipt": "Captures/" + film["id"] + "/film-receipt.json",
                     "videoReceipt": "Captures/" + film["id"] + "/video-receipt.json", "decodedVisualReview": sheet,
                     "directlyViewedDecodedRoleFrames": 6, "visibleCropping": False, "captionVisible": True, "missingRenderedModel": False})
    report = {
        "utc": datetime.now(timezone.utc).isoformat(), "status": "24FPS_STUDIO_FILMS_DECODED_AND_REPRESENTATIVE_FRAMES_VIEWED",
        "actors": 8, "sourcePngs": sum(r["capturedPoseSamples"] for r in rows), "videoFiles": sum(len(r["outputs"]) for r in rows),
        "directlyViewedDecodedFrames": 48, "continuousVideoViewed": False, "worldPlayApproved": False, "userArtApproved": False,
        "renderSize": [512, 512], "videoSize": [512, 576], "samplingFps": 24, "maximumSamplesPerRole": 300,
        "scope": "Actual Unity manual graph samples and CPU physical skin snapshots. All sixteen full videos decoded; six decoded frames per actor directly viewed. Runtime Follow, IK, collision and gameplay are not exercised.",
        "limitations": [
            "Uniform ceil(length*24) poses per clip, capped at 300. Source duration retained; nonloops include their final pose.",
            "Serpent idle/walk share CR_Idle. With runtime BodyFollow disabled, a studio CR attack rotates the child chain with Head. This does not demonstrate moving runtime follow.",
            "Quadruped Attack/Hit/Stun are subtle and some frames resemble Idle. Unique images prove pose changes, not strong combat readability.",
            "The studio has no world terrain or collision, so feet, combat timing and death contact require separate world tests.",
            "Small source/capped surface marks and the raised fox death tail remain as recorded in nonhuman-rig-summary.md."
        ], "rows": rows}
    E.dump(E.BASE / "Analysis/film-review.json", report)
    lines = ["# #298 스튜디오 애니메이션 영상 — 24fps", "",
             f"8종의 실제 Unity 클립을 초당 24자세 기준으로 촬영했다. 원본 PNG {report['sourcePngs']:,}장의 해시와 16개 영상 전체 디코딩, 재생시간을 확인했다. MP4에서 종류별 6역할의 대표 프레임을 추출해 총 48장을 직접 봤다. 확인한 화면에서 자막과 모델은 정상적으로 보이고 잘림은 없었다.", "",
             "영상은 실제 클립 길이를 유지한다. 종마다 고정 카메라로 Idle → Walk → Attack → Hit → Stun → Death를 보여준다. 이전 클립당 12표본 영상은 각 UTC Film 폴더와 History/film-12-samples-20260927-143841에 보존했다.", "",
             "| 종류 | 길이 | 새 자세 표본 | MP4 | WebM |", "|---|---:|---:|---|---|"]
    for r in rows:
        lines.append(f"| {r['displayName']} | {r['seconds']:.3f}초 | {r['capturedPoseSamples']} | [재생](../{r['movie']}) | [재생](../{r['webm']}) |")
    lines += ["", "## 촬영과 검사 범위", "",
              "- 클립당 표본은 `ceil(length × 24)`, 최대 300개다. 현재 클립은 모두 상한보다 짧다. 루프는 끝의 중복 자세를 생략하고 비루프는 마지막 자세를 포함한다.",
              "- 모든 촬영 자세의 실제 메시 bounds를 먼저 측정한 뒤 같은 카메라로 촬영했다. 사망 자세를 위한 별도 줌 변경은 없다.",
              "- 매 표본 전에 rest TRS를 복원하고 Manual PlayableGraph를 평가했다. CPU physical mesh snapshot을 렌더링했고 원본 메시 해시는 촬영 전후 같다.",
              "- 영상은 무음이다. 512×512 모델 화면 아래 별도 64px 자막에 동작 이름과 원래 클립 길이를 표시한다.",
              "- 전체 MP4/WebM 디코딩과 재생시간 검사는 자동 검사다. 시각 검토는 실제 디코딩 대표 프레임 48장으로 한정하며 연속 영상 전체를 직접 시청했다고 주장하지 않는다.",
              "", "## 남은 한계", "",
              "- 이무기·청룡의 실제 BodyFollow와 IK는 스튜디오에서 꺼져 있다. idle/walk는 같은 CR_Idle이며, CR 공격에서 Head에 종속된 몸이 함께 회전할 수 있다. 실제 게임에서 뒤따르는 몸의 움직임을 이 영상으로 판단하지 않는다.",
              "- 사족의 공격·피격·기절 동작은 작아 Idle과 비슷해 보이는 구간이 있다. 기존 작은 표면 흔적과 여우 사망 자세의 들린 꼬리도 남는다.",
              "- 월드 지면·충돌·이동 입력을 사용하지 않았다. 접지와 전투 판정은 별도 런타임 증거를 따른다. 이 영상은 사용자 최종 미술 승인을 대신하지 않는다.",
              "", "## 재생성", "",
              'Unity Edit에서 `CompactFolklore298.Film("all")` 또는 id를 호출한 뒤 저장소 루트에서 실행한다.', "",
              "```powershell", "C:/Python314/python.exe -X utf8 Tools/MeshyRuns/Folklore298/encode_films298.py all",
              "C:/Python314/python.exe -X utf8 Tools/MeshyRuns/Folklore298/extract_film_review298.py", "```", "",
              "각 `Captures/{id}/Film/{UTC}/`에 촬영과 인코딩 원본이 남는다. `Captures/{id}/animation.mp4`, `.webm`과 두 receipt는 최신 결과의 편의 경로다. [검증 JSON](film-review.json)에 SHA, 클립별 시간·표본 수와 확인한 프레임 번호를 기록했다."]
    (E.BASE / "Analysis/film-review.md").write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(json.dumps({"actors": 8, "sourcePngs": report["sourcePngs"], "videos": report["videoFiles"], "decodedFramesViewed": 48, "status": report["status"]}))


if __name__ == "__main__":
    main()
