"""Explicit local finalization of an incomplete/unsuccessful AutoPlayer run.

Draft tool: importing it has no side effects. No API, Blender, Unity, generation,
or candidate repair calls are made. Requires --status and --reason to execute.
Writes FINAL_REPORT.md, final_summary.json, then run_state.json atomically per file.
Candidate results and the paid-request ledger are read-only.
"""
from __future__ import annotations

import argparse
from datetime import datetime, timezone
import hashlib
import json
import math
import os
from pathlib import Path
import re
import tempfile

PROJECT = Path(__file__).resolve().parents[3]
ROOT = PROJECT / "Art/PlayerPhase1/AutoPlayerV1"
TECHNICAL = ("generation", "deformation", "serialization", "weapon_grip", "unity_playtest")
FEATURES = ("wardrobe_swap", "cloth_physics", "facial_rig")
TERMINAL_API = {"SUCCEEDED", "FAILED", "CANCELED", "CANCELLED", "EXPIRED"}


def read(path):
    if not path.is_file():
        return {}
    value = json.loads(path.read_text(encoding="utf-8-sig"))
    if not isinstance(value, dict):
        raise ValueError(path.name + " must contain a JSON object.")
    return value


def clean(value):
    value = str(value if value is not None else "기록 없음")
    value = re.sub(r"https?://[^\s<>\"']+|data:[^\s]+", "[외부 데이터 생략]", value, flags=re.I)
    value = re.sub(r"(?i)(bearer\s+|(?:access_token|refresh_token|api_key|secret)\s*[:=]\s*)\S+", "[인증정보 생략]", value)
    return value[:4000]


def md(value):
    return clean(value).replace("|", "\\|").replace("\r", " ").replace("\n", " ")


def num(value):
    return value if isinstance(value, (int, float)) and not isinstance(value, bool) and math.isfinite(value) and value >= 0 else None


def formatted(value):
    return "미확인" if num(value) is None else f"{value:,.0f}" if value == int(value) else f"{value:,.2f}"


def public(path):
    return not any(part.startswith(".") or part.lower() == "private" for part in path.parts)


def contained(value, base):
    if not isinstance(value, str) or re.search(r"https?://|data:", value, re.I):
        return None
    path = Path(value.replace("\\", "/"))
    path = path.resolve() if path.is_absolute() else (base / path).resolve()
    try:
        path.relative_to(base.resolve())
    except ValueError:
        return None
    return path if public(path) else None


def atomic(path, content):
    fd, temporary = tempfile.mkstemp(prefix="." + path.stem + "-", suffix=".tmp", dir=path.parent)
    try:
        with os.fdopen(fd, "w", encoding="utf-8", newline="\n") as stream:
            stream.write(content)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary, path)
    finally:
        if os.path.exists(temporary):
            os.unlink(temporary)


def json_text(value):
    return json.dumps(value, ensure_ascii=False, indent=2, allow_nan=False) + "\n"


def current_preservation(root, project):
    baseline = read(root / "preservation_before.json")
    files = []
    for value, expected in baseline.items():
        path = contained(value, project)
        row = {"path": path.relative_to(project).as_posix() if path else "[invalid protected path]",
               "expected_sha256": expected.get("sha256"), "status": "UNVERIFIED"}
        if path is not None and not path.is_file():
            row["status"] = "MISSING"
        elif path is not None:
            try:
                before = path.stat()
                digest = hashlib.sha256()
                with path.open("rb") as stream:
                    for block in iter(lambda: stream.read(1024 * 1024), b""):
                        digest.update(block)
                after = path.stat()
                row.update(sha256=digest.hexdigest(), bytes=after.st_size)
                if (before.st_size, before.st_mtime_ns) != (after.st_size, after.st_mtime_ns):
                    row["status"] = "UNVERIFIED_CHANGED_DURING_READ"
                elif re.fullmatch(r"[a-fA-F0-9]{64}", str(expected.get("sha256", ""))):
                    row["status"] = "UNCHANGED" if row["sha256"] == expected["sha256"].lower() else "CHANGED"
            except OSError:
                row["status"] = "UNVERIFIED_READ_ERROR"
        files.append(row)
    return {"status": "ALL_UNCHANGED" if files and all(x["status"] == "UNCHANGED" for x in files) else "CHANGES_OR_UNVERIFIED",
            "scope": "Current protected-file SHA256 only; no character quality verdict.", "files": files}


def candidate_snapshot(root, candidate, state_record):
    folder = root / "Candidates" / candidate
    result_path = folder / "candidate_result.json"
    result = read(result_path)
    raw = read(folder / "raw_geometry.json")
    blends = sorted((p for p in folder.glob("*.blend") if public(p) and not p.name.lower().startswith(("before", "backup"))), key=lambda p: p.stat().st_mtime_ns, reverse=True)
    videos = sorted(p for p in folder.rglob("*") if p.is_file() and public(p) and p.suffix.lower() in {".mp4", ".webm"})
    video_folders = sorted({p.parent.relative_to(root).as_posix() for p in videos})
    audits = []
    for value in video_folders:
        path = root / value / "video_validation.json"
        record = read(path)
        if record:
            audits.append({"path": path.relative_to(root).as_posix(), "status": clean(record.get("status")),
                           "actual_total_decoded_frames": num(record.get("actual_total_decoded_frames")),
                           "clip_count": num(record.get("clip_count")), "scope": "Video encoding/timing only; not deformation or Unity."})
    row = {"candidate": candidate, "candidate_result": result_path.relative_to(root).as_posix() if result_path.is_file() else None,
           "status": clean(result.get("status", state_record.get("status", "UNVERIFIED"))),
           **{key: clean(result.get(key, "UNVERIFIED")) for key in TECHNICAL},
           "art_review": clean(result.get("art_review", "AWAITING_USER_REVIEW")),
           "corrections_used": num(result.get("corrections_used", state_record.get("corrections_used"))),
           "triangles_raw": num(result.get("triangles_raw", raw.get("triangles"))),
           "triangles_unity": num(result.get("triangles_unity")),
           "reason": clean(result.get("reason", "후보 결과에 이유가 기록되지 않았습니다.")),
           "latest_blend": blends[0].relative_to(root).as_posix() if blends else None,
           "latest_blend_scope": "Most recently saved non-backup local blend; not an approved or canonical model.",
           "video_folders": video_folders, "videos": [p.relative_to(root).as_posix() for p in videos],
           "video_validation": audits, "source_manifest": (folder / "Source/manifest.json").relative_to(root).as_posix() if (folder / "Source/manifest.json").is_file() else None}
    return row


def finalize(root, project, status, reason):
    if status not in {"PARTIAL_UNVERIFIED", "NO_VALID_CANDIDATE"} or not reason.strip():
        raise ValueError("Explicit terminal --status and nonempty --reason are required.")
    root, project = root.resolve(), project.resolve()
    if not root.is_dir():
        raise ValueError("Existing run folder is required.")
    state = read(root / "run_state.json")
    ledger = read(root / "cost_ledger.json")
    requests = [r for r in ledger.get("requests", []) if isinstance(r, dict)]
    pending = [clean(r.get("name", "request")) for r in requests if r.get("status") not in TERMINAL_API]
    if pending:
        raise ValueError("Unfinished/uncertain paid requests remain; wait or reconcile first: " + ", ".join(pending))
    task_ids = [r.get("task_id") for r in requests if r.get("task_id")]
    if len(task_ids) != len(set(task_ids)):
        raise ValueError("Duplicate task IDs in ledger; reconcile before totaling consumed credits.")
    state_records = {r["id"]: r for r in state.get("candidates", []) if isinstance(r, dict) and re.fullmatch(r"C\d+", str(r.get("id", "")))}
    generated_ids = {r["name"] for r in requests if r.get("kind") == "character" and re.fullmatch(r"C\d+", str(r.get("name", "")))}
    ids = sorted(set(state_records) | generated_ids | {p.parent.name for p in (root / "Candidates").glob("*/candidate_result.json") if re.fullmatch(r"C\d+", p.parent.name)})
    candidates = [candidate_snapshot(root, candidate, state_records.get(candidate, {})) for candidate in ids]
    if not candidates:
        raise ValueError("No candidate records are available for finalization.")
    if status == "NO_VALID_CANDIDATE" and any(all(c[key] == "PASS" for key in TECHNICAL) for c in candidates):
        raise ValueError("A candidate has all required technical PASS records; NO_VALID_CANDIDATE would contradict them.")
    credits = [{"name": clean(r.get("name")), "kind": clean(r.get("kind")), "task_id": clean(r.get("task_id")),
                "status": clean(r.get("status")), "consumed_credits": num(r.get("consumed_credits"))} for r in requests]
    consumed = sum(r["consumed_credits"] for r in credits if r["consumed_credits"] is not None)
    unconfirmed = [r["name"] for r in credits if r["consumed_credits"] is None]
    budget = num(ledger.get("budget_credits", state.get("budget_credits")))
    candidate_limit = num(state.get("max_candidates")) or 3
    stop_causes = []
    if len(generated_ids) >= candidate_limit:
        stop_causes.append("CANDIDATE_LIMIT_REACHED")
    if budget is not None and consumed >= budget:
        stop_causes.append("CONFIRMED_CONSUMED_BUDGET_REACHED")
    # Never newly set this flag merely because a partial report is being written.
    stopped = bool(state.get("paid_requests_stopped", False)) or bool(stop_causes)
    selected_id = state.get("selected_candidate")
    if isinstance(selected_id, dict):
        selected_id = selected_id.get("id", selected_id.get("candidate"))
    selected = next((c for c in candidates if c["candidate"] == selected_id), None)
    top = {}
    for key in TECHNICAL:
        if selected:
            top[key] = selected[key]
        elif key == "generation" and any(c[key] == "PASS" for c in candidates):
            top[key] = "PASS"
        elif all(c[key] == "FAIL" for c in candidates):
            top[key] = "FAIL"
        else:
            top[key] = "UNVERIFIED"
    timestamp = datetime.now(timezone.utc).isoformat()
    preservation = current_preservation(root, project)
    summary = {"version": 1, "status": status, "finished_at": timestamp, "reason": clean(reason),
               "selected_candidate": selected_id, **top,
               "aggregate_scope": "generation PASS means at least one recorded generated candidate. Other technical PASS requires an explicitly selected candidate. Unresolved warnings remain UNVERIFIED.",
               "art_review": selected["art_review"] if selected else "AWAITING_USER_REVIEW",
               **{key: clean(state.get(key, "NOT_IMPLEMENTED")) for key in FEATURES},
               "canonical_player_replaced": state.get("canonical_player_replaced", False),
               "paid_requests_stopped": stopped, "paid_stop_causes": stop_causes,
               "candidate_generation_count": len(generated_ids), "max_candidates": candidate_limit,
               "actual_consumed_credits": consumed, "budget_credits": budget,
               "unconfirmed_actual_cost_requests": unconfirmed, "request_costs": credits,
               "candidates": candidates, "preservation": preservation,
               "limitations": [clean(reason), "경고가 남은 UNVERIFIED와 확인된 구조 FAIL을 구분합니다.",
                               "Blender 렌더 및 영상 인코딩 검사와 Unity 실시간 플레이 검증은 별개입니다.",
                               "최신 Blender 파일의 존재는 후보의 기술 통과나 정본 채택을 뜻하지 않습니다.",
                               "내장 이미지 생성의 청구액은 응답에 없어 미확인입니다. Meshy 사용액과 별개입니다.",
                               "실제 라이브러리 공격 action 240을 재생했지만 붓/창을 장착한 파지는 미검증입니다."],
               "review": "REVIEW.html", "report": "FINAL_REPORT.md", "source_manifest": "source_manifest.json"}
    lines = ["# AutoPlayerV1 최종 실행 기록", "", f"**{status}** · {timestamp}", "", clean(reason), "",
             f"확인된 Meshy 사용: **{formatted(consumed)} / {formatted(budget)}크레딧**. 예상값·예약 비용은 실제 사용액에 넣지 않았다.", "",
             f"신규 유료 요청 중단: `{str(stopped).lower()}` · 근거: `{', '.join(stop_causes) or '이번 실행에서 새 한도 종료 조건 없음'}`.", "",
             "이 기록은 현재 후보 JSON의 판정을 모은다. 잔여 경고가 있는 UNVERIFIED를 구조적 FAIL로 바꾸지 않는다.", "",
             "## 판정", "", "| 항목 | 기록 |", "|---|---|"]
    for key in TECHNICAL + ("art_review",) + FEATURES + ("canonical_player_replaced",):
        lines.append(f"| {key} | {md(summary[key])} |")
    lines += ["", "생성 PASS는 생성에 성공한 후보가 있다는 뜻이다. 후보 선정이 없으면 후속 기술 항목을 자동 통과시키지 않는다.", "",
              "## 후보별 결과", "", "| 후보 | 현재 상태 | 동작 변형 | 생성 삼각형 | Unity 삼각형 | 보정 횟수 |", "|---|---|---|---:|---:|---:|"]
    for c in candidates:
        lines.append(f"| {c['candidate']} | {md(c['status'])} | {md(c['deformation'])} | {formatted(c['triangles_raw'])} | {formatted(c['triangles_unity'])} | {formatted(c['corrections_used'])} |")
    for c in candidates:
        lines += ["", f"### {c['candidate']}", "", c["reason"], "",
                  f"- 최신 저장 Blender 자료: `{c['latest_blend'] or '없음'}`. 합격 모델 지정이 아니다.",
                  f"- 영상 폴더: {', '.join('`' + p + '`' for p in c['video_folders']) or '없음'} · MP4/WebM {len(c['videos'])}개.",
                  f"- 최신 후보 기록: `{c['candidate_result'] or '없음 · UNVERIFIED'}`.",
                  f"- FBX 왕복 `{c['serialization']}` · 붓 파지 `{c['weapon_grip']}` · Unity 플레이 `{c['unity_playtest']}`."]
        for v in c["video_validation"]:
            lines.append(f"- 영상 인코딩 기록 `{v['status']}` · 실제 디코드 {formatted(v['actual_total_decoded_frames'])}프레임: `{v['path']}`. 캐릭터 품질 검증과 별개다.")
    lines += ["", "## 실제 비용", "", "| 작업 | 종류 | 서버 상태 | consumed_credits |", "|---|---|---|---:|"]
    for r in credits:
        lines.append(f"| {md(r['name'])} | {md(r['kind'])} | {md(r['status'])} | {formatted(r['consumed_credits'])} |")
    if unconfirmed:
        lines += ["", "실제 청구액 미확인 요청: " + ", ".join(unconfirmed) + ". 합계는 확인된 값만 포함한다."]
    lines += ["", "내장 이미지 생성(제작용 입력과 다음 의상 시안)의 청구액은 도구 응답에 없어 미확인이다. 위 Meshy 크레딧과 별개로 출처에 기록했다.",
              "", "## 다음 외형 검토와 검사 범위", "",
              "`DesignProposals/LongSplit_and_ShortCoat_Concepts.png`의 왼쪽은 긴 기장과 트임 정리, 오른쪽은 무릎 위 기장으로 바꾼 2D 제안이다. 모델에 적용하지 않았으며 리깅 합격을 보장하지 않는다. 설명은 `DesignProposals/README.md`에 있다.", "",
              "후보별 원본 Walk·Run·Idle·공격 1개와 진단 자세 4종을 모든 정수 프레임에서 수치 검사했다. 공격은 실제 Meshy 라이브러리 action 240이며, 창/붓을 장착한 파지는 미검증이다. 영상은 Blender 렌더이며 Unity 캡처가 아니다. 전신 피부 관통 수치는 통합 메시에서 대상을 정의하지 못해 미검증으로 남겼다.", "",
              "재검사와 기존 task 결과 재사용 방법은 `Tools/Blender/AutoPlayerV1/README.md`(프로젝트 루트 기준)에 있다. 새 유료 생성은 중단 상태다.",
              "", "## 원본 보존", "", f"최종화 시점의 보호 파일 SHA256 비교: **{preservation['status']}**.", ""]
    lines.extend(f"- `{r['path']}`: `{r['status']}`" for r in preservation["files"])
    lines += ["", "파일 보존 검사는 후보의 동작 품질 합격을 뜻하지 않는다. 새 정본 채택이나 C2·공용 PlayerRig 변경은 이 도구가 수행하지 않는다.", "",
              "## 자료 갱신·재실행", "", "프로젝트 루트에서 실행한다. 후보의 최신 결과를 먼저 기록한 뒤 명시적인 종료 상태·이유를 전달한다.", "", "```powershell",
              "python -B Tools/Blender/AutoPlayerV1/finalize_run.py --status PARTIAL_UNVERIFIED --reason \"실제로 남은 미검증 이유\"",
              "python -B Tools/MeshyRuns/AutoPlayerV1/sync_manifest.py",
              "python -B Tools/Blender/AutoPlayerV1/build_review.py", "```", "",
              "종료 상태는 `PARTIAL_UNVERIFIED` 또는 `NO_VALID_CANDIDATE`를 명시한다. 후보 결과·리그·원장을 수정하지 않으며 네트워크나 유료 호출도 없다. 진행 중/불명확한 API 요청이 있으면 종료하지 않는다.", "",
              "`REVIEW.html`은 외부 의존성 없는 이미지·영상 검토 페이지다. `source_manifest.json`은 출처와 주요 파일 해시를 기록한다. 최종 기술 미검증·잔여 실패와 사용자 외형 승인은 별도로 남긴다.", ""]
    # Commit the terminal run marker last. Existing references and unrelated state keys survive.
    state.update(status=status, phase="FINALIZED", finished_at=timestamp, terminal_reason=clean(reason),
                 paid_requests_stopped=stopped, paid_stop_causes=stop_causes,
                 final_summary="final_summary.json", final_report="FINAL_REPORT.md")
    for item in state.get("candidates", []):
        latest = next((c for c in candidates if c["candidate"] == item.get("id")), None)
        if latest:
            item["status"] = latest["status"]
            if latest["corrections_used"] is not None:
                item["corrections_used"] = latest["corrections_used"]
    atomic(root / "FINAL_REPORT.md", "\n".join(lines))
    atomic(root / "final_summary.json", json_text(summary))
    atomic(root / "run_state.json", json_text(state))
    return {"status": status, "actual_consumed_credits": consumed, "paid_requests_stopped": stopped,
            "preservation": preservation["status"], "candidate_verdicts_changed": False}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=ROOT)
    parser.add_argument("--project-root", type=Path, default=PROJECT)
    parser.add_argument("--status", choices=("PARTIAL_UNVERIFIED", "NO_VALID_CANDIDATE"), required=True)
    parser.add_argument("--reason", required=True)
    args = parser.parse_args()
    print(json.dumps(finalize(args.root, args.project_root, args.status, args.reason), ensure_ascii=False))
