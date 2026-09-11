"""Build the local-only KTP/Meshy work-in-progress review and one old-page link.

This reads existing captures; it never starts a server, edits assets or publishes.
Rerun after new KTPIntegratedStillsNN/KTPExternalStillsNN captures are available.
"""
from __future__ import annotations

import html
import hashlib
import json
import math
import os
import re
from datetime import datetime, timezone
from pathlib import Path
from urllib.parse import quote, unquote, urlsplit, urlunsplit

ROOT = Path(__file__).resolve().parents[2]
ART = ROOT / "Art" / "SpellVFX120"
OUTPUT = ART / "KTP_REWORK.html"
TIMES = [0.12, 0.35, 0.75, 1.30, 2.20]
# The clod experiment was explicitly rejected. Preserve its evidence on disk,
# but never silently promote it because its capture folder has a larger number.
REJECTED_CAPTURE_VERSIONS = {11: "반려된 토괴 실험 · 현재 채택본 아님", 20: "물마루 형상 실험 반려 · 19로 복원"}
# Unreviewed future candidates must not displace accepted evidence by number.
CANDIDATE_CAPTURE_VERSIONS = {}
BOTANICAL_STEMS = {"002_AC01": "각", "010_AC80": "검", "018_ACF5": "공"}
BOTANICAL_MIN_STAGE = 14
STONE_WAVE_STEMS = {"050_B9C9": "막", "070_BB44": "뭄", "109_C624": "오"}
INPUT_BATCH_GLYPHS = {1: "가나마사아", 2: "고노모소오", 3: "거너머서어"}


def read_json(path: Path, default=None):
    try:
        return json.loads(path.read_text(encoding="utf-8-sig"))
    except (FileNotFoundError, json.JSONDecodeError):
        return {} if default is None else default


def relative_if_file(path: Path):
    return path.relative_to(ART).as_posix() if path.is_file() else None


def publish_review_doc(source: Path):
    """Publish only an explicitly linked Docs report and its document dependencies.

    Original Docs and already-served Art evidence are never modified. Markdown
    links are rebased; JSON evidence is copied byte-for-byte rather than treating
    arbitrary provenance fields as URLs. No source assets or repository directory
    are exposed by widening the HTTP server root.
    """
    docs = (ROOT / "Docs").resolve()
    served = ART.resolve()
    destination = served / "ReviewDocs"
    source = source.resolve()
    copied, unavailable, checking = {}, [], set()

    def publish(path):
        path = path.resolve()
        if not path.is_relative_to(docs) or path.suffix.lower() not in (".md", ".json") or not path.is_file():
            return None
        out = destination / path.relative_to(docs)
        if path in copied or path in checking:
            return out
        checking.add(path)
        original = path.read_bytes()

        def rebase(target):
            url = target[1:-1] if target.startswith("<") and target.endswith(">") else target
            parsed = urlsplit(url)
            if parsed.scheme or parsed.netloc or not parsed.path:
                return target
            local = unquote(parsed.path)
            # A leading slash in a repository document denotes its repository root.
            linked = ((ROOT / local.lstrip("/")) if local.startswith("/") else path.parent / local).resolve()
            if linked.is_relative_to(served) and linked.is_file():
                published = linked
            elif linked.is_relative_to(docs):
                published = publish(linked)
            else:
                published = None
            if published is None:
                unavailable.append({"document": path.relative_to(ROOT).as_posix(), "reference": url,
                                    "reason": "Missing target or outside served Art / explicitly linked Docs markdown and JSON."})
                return None
            relative = quote(Path(os.path.relpath(published, out.parent)).as_posix(), safe="/")
            fixed = urlunsplit(("", "", relative, parsed.query, parsed.fragment))
            return "<" + fixed + ">" if target.startswith("<") else fixed

        if path.suffix.lower() == ".md":
            text = original.decode("utf-8-sig")
            inline = re.compile(r'(?P<label>!?\[[^\]\n]*\])\((?P<url><[^>\n]+>|[^\s)]+)(?P<tail>[ \t]+[^\n)]*)?\)')
            reference = re.compile(r'^(?P<prefix>[ \t]{0,3}\[[^\]\n]+\]:[ \t]*)(?P<url><[^>\n]+>|[^\s]+)(?P<tail>[^\n]*)$')

            def replace_inline(match):
                fixed = rebase(match["url"])
                if fixed is None:
                    return match["label"].lstrip("!")[1:-1] + " (참조 제공 불가)"
                return match["label"] + "(" + fixed + (match["tail"] or "") + ")"

            lines, fence = [], None
            for line in text.splitlines(keepends=True):
                marker = re.match(r"^[ \t]{0,3}(`{3,}|~{3,})", line)
                if marker:
                    token = marker.group(1)
                    if fence is None: fence = token
                    elif token[0] == fence[0] and len(token) >= len(fence): fence = None
                    lines.append(line); continue
                if fence is not None:
                    lines.append(line); continue
                match = reference.match(line.rstrip("\r\n"))
                if match:
                    fixed = rebase(match["url"])
                    line = match["prefix"] + fixed + match["tail"] + "\n" if fixed else "\n"
                else:
                    line = inline.sub(replace_inline, line)
                lines.append(line)
            content = "".join(lines).encode("utf-8")
        else:
            content = original
        out.parent.mkdir(parents=True, exist_ok=True)
        if not out.is_file() or out.read_bytes() != content:
            out.write_bytes(content)
        copied[path] = {"source": path.relative_to(ROOT).as_posix(), "served": out.relative_to(served).as_posix(),
                        "sourceSha256": hashlib.sha256(original).hexdigest(), "servedSha256": hashlib.sha256(content).hexdigest()}
        checking.remove(path)
        return out

    published = publish(source)
    if published is None:
        return None
    manifest = {"scope": "Explicit Docs report and recursively linked Docs markdown/JSON only; Art evidence linked in place.",
                "entry": source.relative_to(ROOT).as_posix(), "files": list(copied.values()),
                "unresolved": unavailable, "status": "ALL_LOCAL_REFERENCES_RESOLVED" if not unavailable else "UNRESOLVED_REFERENCES_REMOVED_FROM_SERVED_COPY"}
    (destination / (source.stem + "_publication.json")).write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")
    return published.relative_to(served).as_posix()


def utc_stamp(value):
    try:
        result = datetime.fromisoformat(value.replace("Z", "+00:00"))
        return result.astimezone(timezone.utc) if result.tzinfo is not None else None
    except (AttributeError, TypeError, ValueError):
        return None


def mvid(value):
    value = str(value or "").lower()
    return value if re.fullmatch(r"[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}", value) and set(value) != {"0", "-"} else None


def capture_complete(frames, metadata):
    times = metadata.get("sampledSeconds", [])
    return all(f["image"] for f in frames) and isinstance(times, list) and len(times) == 5 \
        and all(isinstance(t, (int, float)) and not isinstance(t, bool) and math.isfinite(t) for t in times)


def botanical_capture_current(metadata):
    built = utc_stamp(read_json(ART / "botanical_build.json").get("generatedUtc"))
    captured = utc_stamp(metadata.get("capturedUtc"))
    return bool(built and captured and captured >= built and mvid(metadata.get("loadedRuntimeAssemblyMvid")))


def captures(folder: str, stem: str, group_metadata=None):
    frames = []
    group_metadata = group_metadata or read_json(ART / folder / f"{stem}_capture.json")
    for index in range(5):
        image = ART / folder / f"{stem}_{index}.png"
        own_metadata = read_json(image.with_suffix(".json"))
        metadata = own_metadata or group_metadata
        times = metadata.get("sampledSeconds", [])
        frames.append({
            "image": relative_if_file(image),
            "metadata": relative_if_file(image.with_suffix(".json"))
            or relative_if_file(ART / folder / f"{stem}_capture.json"),
            "phase": index,
            "time": times[index] if isinstance(times, list) and len(times) > index else None,
        })
    return frames


NATIVE_ROLES = [
    ("타격", "Fly01-01_Explosion", "Fly01 · Explosion", "문양 면 방향 보정 필요. 현재는 형광 녹색 난류가 우세합니다."),
    ("발동", "Fly06-01_Charge", "Fly06 · Charge", "별과 전기 링이 우세해, 문양 중심 연출의 보조 발동 후보입니다."),
    ("소환진", "Bottom12-01", "Bottom12", "꽃잎 선화가 보입니다. 사각 광면과 주황 광량은 조정이 필요합니다."),
    ("방어막 후보", "Bottom04-01", "Bottom04", "원형 바닥 문양은 보이지만, 유지 구간의 높이 있는 방어 경계는 부족합니다."),
]


def native_data():
    result = []
    for role, stem, name, note in NATIVE_ROLES:
        meta = read_json(ART / "KTPNativeReview02" / f"{stem}_2.json")
        result.append({"role": role, "name": name, "note": note,
                       "source": meta.get("source", stem), "kind": "KTP 원본 · 파티클 표본",
                       "frames": captures("KTPNativeReview02", stem)})
    return result


def capture_versions(prefix):
    result = []
    for folder in ART.glob(prefix + "*"):
        match = re.fullmatch(re.escape(prefix) + r"(\d+)", folder.name)
        if folder.is_dir() and match:
            result.append((int(match.group(1)), folder))
    return sorted(result, key=lambda row: row[0], reverse=True)


def integrated_data():
    """Choose latest complete per-spell views; retain older views with clear labels.

    An incomplete new capture cannot silently replace five verified older frames.
    Missing metadata is not treated as an issued time or a verified capture.
    """
    definitions = {d["glyph"]: d for d in read_json(ART / "build_manifest.json").get("spells", [])}
    selected = {}
    capture_sets = []
    for prefix, label in [("KTPIntegratedStills", "메인"), ("KTPExternalStills", "외부")]:
        for version, folder in capture_versions(prefix):
            if version in REJECTED_CAPTURE_VERSIONS or version in CANDIDATE_CAPTURE_VERSIONS:
                continue
            stems = sorted({m.group(1) for p in folder.glob("*.png")
                            if (m := re.fullmatch(r"(.+)_([0-4])", p.stem))})
            files = 0
            complete = 0
            manifests = 0
            for stem in stems:
                meta = read_json(folder / f"{stem}_capture.json")
                frames = captures(folder.name, stem, meta)
                count = sum(bool(f["image"]) for f in frames)
                files += count
                manifests += bool(meta)
                ready = capture_complete(frames, meta)
                if stem in BOTANICAL_STEMS and version >= BOTANICAL_MIN_STAGE:
                    ready = ready and botanical_capture_current(meta)
                complete += ready
                experiment = version == 13 and stem in BOTANICAL_STEMS
                view = {"label": label + f" {version:02d}" + (" 실험" if experiment else ""), "folder": folder.name,
                        "version": version, "frames": frames, "complete": ready,
                        "camera": meta.get("cameraName", "카메라 미확인"),
                        "capturedUtc": meta.get("capturedUtc"),
                        "runtimeMvid": meta.get("loadedRuntimeAssemblyMvid"),
                        "demonstrationCues": meta.get("demonstrationCues"),
                        "gameplayConnectionVerified": meta.get("gameplayConnectionVerified"),
                        "metadata": relative_if_file(folder / f"{stem}_capture.json"), "meta": meta}
                key = (stem, prefix)
                current = selected.get(key)
                if current is None or (ready and not current["complete"]):
                    selected[key] = view
            if files:
                capture_sets.append({"folder": folder.name, "images": files,
                                     "groups": len(stems), "completeGroups": complete, "manifests": manifests})
    result = []
    for stem in sorted({key[0] for key in selected}):
        views = [selected[(stem, prefix)] for prefix in ("KTPIntegratedStills", "KTPExternalStills")
                 if (stem, prefix) in selected]
        meta = views[0]["meta"]
        glyph = meta.get("glyph", stem)
        definition = definitions.get(glyph, {})
        design = read_json_design(definition.get("recipe", ""))
        role = {"Projectile": "투사", "Bind": "속박", "Heal": "회복", "Shield": "방어", "Zone": "영역",
                "Summon": "소환", "Weapon": "무기", "Buff": "강화", "Burst": "분출", "Wave": "파도", "Reserve": "공백"}.get(definition.get("behavior"), "통합 시제품")
        for view in views:
            view.pop("meta", None)
        note = "실제 장면의 예시 연출 표본입니다. 시점 버튼의 촬영 버전을 구분해 비교하세요. 게임 판정 검증이나 최종 미술 승인은 아닙니다."
        if glyph in ("몸", "옴"):
            note += " 이 소환수의 Meshy 교체는 미완료이며, 표본의 기존 임시 본체는 최종 모델이 아닙니다."
        if glyph in ("노", "모"):
            note += {"노": " 10 미술 검토: 큰 원형 타격은 제거됐지만 지형이 발사부를 가려 전체 Cone 연결감·양감은 미검증입니다.",
                     "모": " 10 미술 검토: 하단 광면은 제거됐지만 토사 전면의 높이·양감이 약합니다. 11 토괴 실험은 반려되어 현재 표본에서 제외합니다."}[glyph]
        if stem in STONE_WAVE_STEMS:
            if any(v["complete"] and v["version"] >= 19 for v in views):
                note += (" 돌·물결 교정 후 촬영입니다. 19 검토에서는 막의 접촉 그림자·소멸은 개선됐지만, 뭄의 지면 밀착과 오의 얇은 물막·디더 표현은 미달로 남았습니다. 전체 미술 통과가 아닙니다."
                         if (ART / "KTP_STONE_WAVE_REVIEW19.md").is_file() else " 돌·물결 교정 후 촬영이며 19 미술 검토는 대기 중입니다.")
            elif glyph == "오":
                note += " 이전 물결 촬영입니다. 과한 타격 광면의 교정 여부는 19 보고서와 구분해 확인하세요."
        if stem in BOTANICAL_STEMS:
            stages = {v["version"] for v in views if v["complete"]}
            if any(v >= BOTANICAL_MIN_STAGE for v in stages):
                note += " 새 식물 본체 촬영입니다. 각은 목질 뿌리, 공은 대나무, 검은 여름 수관 후보이며 미술 판정은 해당 버전 보고서를 확인하세요. 13 실험의 결함을 새 촬영에 자동 적용하지 않습니다."
            elif 13 in stages:
                note += " 13 실험: 각은 결박 표현 미달, 공은 Path 계획 누락으로 이전 표현이 촬영되어 대나무 미검증, 검은 겨울 고목입니다. 이후 완비 촬영은 대기 중입니다."
            else:
                note += " 이전 촬영 자료입니다. 새 식물 본체의 현재 형상을 보여주는 근거로 사용하지 않습니다."
        result.append({"role": " · ".join(x for x in [design.get("element"), role] if x),
                       "name": str(glyph) + " · " + str(meta.get("title", definition.get("title", stem))),
                       "source": stem, "kind": "통합 시제품 · 미술 검수 중",
                       "note": note,
                       "frames": views[0]["frames"], "views": views, "videos": videos_for(stem)})
    return result, capture_sets


def read_json_design(recipe):
    try:
        return json.loads(recipe).get("design", {})
    except (TypeError, json.JSONDecodeError):
        return {}


def videos_for(stem):
    result = []
    for prefix, frame_prefix, label in [("ClipsKTPIntegrated", "KTPIntegratedFrames", "메인 시점"),
                                       ("ClipsKTPExternal", "KTPExternalFrames", "외부 시점")]:
        for version, folder in capture_versions(prefix):
            if version in REJECTED_CAPTURE_VERSIONS or version in CANDIDATE_CAPTURE_VERSIONS:
                continue
            video = folder / f"{stem}.mp4"
            sidecar = folder / f"{stem}_encoding.json"
            proof = read_json(sidecar)
            metadata = ART / f"{frame_prefix}{version:02d}" / f"{stem}_capture.json"
            if not video.is_file() or not metadata.is_file() or proof.get("id") != stem:
                continue
            if proof.get("status") != "VERIFIED_ENCODING_AND_TIMING_ONLY":
                continue
            verification = proof.get("verification", {})
            checks = verification.get("checks", {})
            if verification.get("decodedFrames", 0) <= 0 or not checks.get("fullDecodeWithoutError") \
                    or not all(value is True for value in checks.values()):
                continue
            # A previous sidecar cannot approve a still-being-replaced MP4 or capture.
            if video.stat().st_size != proof.get("outputBytes"):
                continue
            if hashlib.sha256(video.read_bytes()).hexdigest() != proof.get("outputSha256"):
                continue
            if hashlib.sha256(metadata.read_bytes()).hexdigest() != proof.get("metadataSha256"):
                continue
            capture_meta = read_json(metadata)
            result.append({"path": relative_if_file(video), "version": version,
                           "runtimeMvid": mvid(capture_meta.get("loadedRuntimeAssemblyMvid")),
                           "capturedUtc": capture_meta.get("capturedUtc"), "label": label + f" 영상 {version:02d}"
                           + (" 실험" if version == 13 and stem in BOTANICAL_STEMS else ""),
                           "evidence": relative_if_file(sidecar), "duration": verification.get("durationSeconds"),
                           "scope": "C2 Editor 카메라 진단 재생 영상 · 24fps / 1280×720 · 실제 사용자 입력 영상 아님 · 인코딩·시간만 검증, 피해·미술·성능 통과 아님"})
            break
    return result


def proof_after_body_build(proof, *date_fields):
    build = read_json(ART / "element_wash_build.json")
    try:
        built_at = datetime.fromisoformat(build["generatedUtc"].replace("Z", "+00:00"))
        proof_at = next(datetime.fromisoformat(proof[key].replace("Z", "+00:00"))
                        for key in date_fields if proof.get(key))
        return proof_at >= built_at
    except (KeyError, ValueError, StopIteration, TypeError):
        return False


def validation_status():
    play_path = ART / "traditional_play_audit.json"
    play = read_json(play_path)
    play_pass = play.get("status") == "PASS_REAL_UPDATE_LIFETIME_ONLY" and play.get("completed") == 5 \
        and all(play.get(k) == 0 for k in ["failed", "errors", "shaderErrors"]) \
        and all(play.get(k) is True for k in ["restored", "sourceUnchanged", "sceneUnchanged", "cleanupObserved"])
    catalog_path = ART / "traditional_catalog_audit.json"
    catalog = read_json(catalog_path)
    catalog_pass = catalog.get("status") == "PASS_ASSIGNED_EDIT_PREVIEW_CONTRACTS_ONLY" \
        and catalog.get("profileCount") == 120 and catalog.get("passed") == 120 \
        and catalog.get("failed") == 0 and catalog.get("errorCount") == 0 \
        and catalog.get("sourceUnchanged") is True and catalog.get("cleanupObserved") is True
    builds = [utc_stamp(read_json(ART / name).get("generatedUtc")) for name in
              ("element_wash_build.json", "botanical_build.json", "stone_body_build.json", "water_wave_tuning16.json")]
    builds = [date for date in builds if date]
    catalog_at = utc_stamp(catalog.get("capturedUtc"))
    catalog_current = bool(catalog_pass and builds and catalog_at and catalog_at >= max(builds))
    return {"playPass": play_pass, "playReport": relative_if_file(play_path),
            "catalogPass": catalog_current, "catalogRecordedPass": catalog_pass,
            "catalogReport": relative_if_file(catalog_path),
            "catalogCapturedUtc": catalog.get("capturedUtc"),
            "playText": "실제 Play 5종(가·곰·구·놈·솜)의 생성~Life 종료·정리 통과. 오류0 / 셰이더 오류0, 원본·씬·설정 복원 확인."
            if play_pass else "실제 Play 5종 검사: 완료 근거 확인 전.",
            "catalogText": "최신 본체 Build 이후 120프로필 Edit preview 연결 계약 통과 · 실패0."
            if catalog_current else ("이전 120프로필 연결 계약은 통과했지만 최신 제작 이후 검사는 대기 중입니다. 이전 PASS를 최신판에 적용하지 않습니다."
                                    if catalog_pass else "120프로필 Edit preview 연결 계약: 완료 근거 확인 전.")}


def body_status():
    """Read spatial and actual-Update contracts without promoting either to art PASS."""
    edit_path = ART / "element_wash_audit.json"
    play_path = ART / "element_wash_play_audit.json"
    edit, play = read_json(edit_path), read_json(play_path)
    edit_pass = edit.get("status") == "PASS_EDIT_SPATIAL_NATIVE_BODY_CONTRACT_ONLY" \
        and edit.get("passed") == 11 and edit.get("failed") == 0 and edit.get("errors") == 0 \
        and edit.get("sourcesUnchanged") is True and edit.get("cleanupObserved") is True
    play_pass = play.get("status") == "PASS_REAL_UPDATE_LIFETIME_ONLY" \
        and play.get("completed") == 2 \
        and all(play.get(k) == 0 for k in ("failed", "errors", "shaderErrors")) \
        and all(play.get(k) is True for k in ("restored", "sourceUnchanged", "sceneUnchanged", "cleanupObserved"))
    edit_current = edit_pass and proof_after_body_build(edit, "capturedUtc")
    play_current = play_pass and proof_after_body_build(play, "startedUtc")
    return {"editPass": edit_current, "playPass": play_current,
            "editRecordedPass": edit_pass, "playRecordedPass": play_pass,
            "editReport": relative_if_file(edit_path), "playReport": relative_if_file(play_path),
            "artReport": relative_if_file(ART / "KTP_BODY_REVIEW10.md"),
            "rejectedExperimentReport": relative_if_file(ART / "element_wash_build_clods11.json"),
            "rejectedExperimentReview": relative_if_file(ART / "KTP_CLOD_EXPERIMENT11.md"),
            "videoReview": relative_if_file(ART / "KTP_VIDEO_REVIEW10.md"),
            "editCapturedUtc": edit.get("capturedUtc"), "playStartedUtc": play.get("startedUtc"),
            "editMvid": edit.get("appMvid"), "playMvid": play.get("appMvid"),
            "editText": "최신 Build 이후 노·모 Edit 공간 계약 11/11 통과: 앵커·폭·계획 시계·정리. 실제 입자 범위나 미술 판정은 아닙니다."
            if edit_current else "최신 10/12 본체의 Edit 공간 계약 검사 대기. 07의 11/11 통과 기록은 이전 버전 근거입니다.",
            "playText": "최신 Build 이후 노·모 실제 Play 2/2 수명·정리 통과. 진단용 공간 계획 검사이며 입력·피해·목표 FPS 판정은 아닙니다."
            if play_current else "최신 10/12 본체의 실제 Play 수명 검사 대기. 07의 2/2 통과를 최신 결과로 표시하지 않습니다.",
            "artText": "노·모는 10 본체를 기준으로 검토합니다. 11 토괴 실험은 반려·보존하며 현재 보기에서 제외합니다. 모의 약한 토사 양감, 노의 지형 가림이 남았습니다. 오의 후속 교정은 돌·물결 항목에서 따로 확인하세요.",
            "videoText": "10의 노·모 메인/외부 영상 4개는 인코딩·전체 디코드 검증을 통과했습니다. 04 영상 10개와 버전을 구분해 표시하며, 반려된 11 실험 영상은 선택하지 않습니다."}


def botanical_status():
    """A recorded PASS is historical until build time, capture MVID and source agree.

    The builder does not record an MVID: never invent one. Its UTC is the lower
    bound, while Edit/Play and six complete stage captures identify the DLL.
    These are technical contracts, not proof of art or actual player casting.
    """
    paths = {"build": ART / "botanical_build.json", "edit": ART / "botanical_audit.json",
             "play": ART / "botanical_play_audit.json"}
    build, edit, play = (read_json(paths[key]) for key in ("build", "edit", "play"))
    built = utc_stamp(build.get("generatedUtc"))
    prefixes = ("KTPIntegratedStills", "KTPExternalStills")
    versions = sorted({version for prefix in prefixes for version, _ in capture_versions(prefix)
                       if version >= BOTANICAL_MIN_STAGE}, reverse=True)

    def stage_rows(version):
        rows = []
        for prefix in prefixes:
            folder = f"{prefix}{version:02d}"
            for stem, glyph in BOTANICAL_STEMS.items():
                meta_path = ART / folder / f"{stem}_capture.json"
                meta = read_json(meta_path)
                rows.append({"glyph": glyph, "folder": folder,
                             "ready": capture_complete(captures(folder, stem, meta), meta)
                             and botanical_capture_current(meta),
                             "capturedUtc": meta.get("capturedUtc"),
                             "mvid": mvid(meta.get("loadedRuntimeAssemblyMvid")),
                             "report": relative_if_file(meta_path)})
        return rows

    stage = versions[0] if versions else BOTANICAL_MIN_STAGE
    capture_rows = stage_rows(stage)
    for version in versions:
        rows = stage_rows(version)
        if all(row["ready"] for row in rows) and len({row["mvid"] for row in rows}) == 1:
            stage, capture_rows = version, rows
            break
    capture_mvids = {row["mvid"] for row in capture_rows}
    captures_ready = all(row["ready"] for row in capture_rows)
    one_capture_mvid = captures_ready and len(capture_mvids) == 1 and None not in capture_mvids
    captured_mvid = next(iter(capture_mvids)) if one_capture_mvid else None
    runtime_source = ROOT / "Oheangbu/Assets/_Project/Scripts/App/SpellVFX120/Vfx120Effect.Botanical.cs"
    source_sha = hashlib.sha256(runtime_source.read_bytes()).hexdigest() if runtime_source.is_file() else None
    edit_time, play_time = utc_stamp(edit.get("capturedUtc")), utc_stamp(play.get("startedUtc"))
    edit_mvid, play_mvid = mvid(edit.get("appMvid")), mvid(play.get("appMvid"))
    build_valid = build.get("status") == "BUILT_STRUCTURALLY_VALID_VISUAL_UNVERIFIED" \
        and build.get("sourceFilesUnchanged") is True and built is not None
    build_geometry = {row.get("glyph"): (row.get("parts"), row.get("totalTriangles"))
                      for row in build.get("bodies", [])}
    edit_geometry = {row.get("glyph"): (row.get("directChildren"), row.get("triangles"))
                     for row in edit.get("inventory", {}).get("rows", [])}
    same_geometry = set(build_geometry) == {"각", "공", "검"} and build_geometry == edit_geometry
    current_source = bool(source_sha and source_sha == str(edit.get("botanicalRuntimeSourceSha256", "")).lower())
    edit_recorded = edit.get("status") == "PASS_BOTANICAL_EDIT_CONTRACTS_ONLY" \
        and edit.get("passed") == 7 and edit.get("failed") == 0 and edit.get("errors") == 0 \
        and edit.get("sourcesUnchanged") is True and edit.get("cleanupObserved") is True
    play_recorded = play.get("status") == "PASS_REAL_UPDATE_LIFETIME_ONLY" \
        and play.get("stage") == "FINISHED" and play.get("completed") == 3 \
        and set(play.get("glyphs", [])) == {"각", "공", "검"} \
        and all(play.get(k) == 0 for k in ("failed", "errors", "shaderErrors")) \
        and all(play.get(k) is True for k in ("restored", "sourceUnchanged", "sceneUnchanged", "cleanupObserved"))
    edit_current = bool(build_valid and same_geometry and current_source and one_capture_mvid and edit_mvid == captured_mvid
                        and edit_time and edit_time >= built)
    play_current = bool(edit_current and play_time and play_time >= built and play_mvid == edit_mvid)
    edit_pass, play_pass = edit_recorded and edit_current, play_recorded and play_current
    geometry = [{"glyph": row.get("glyph"), "parts": row.get("parts"), "triangles": row.get("totalTriangles"),
                 "prefab": row.get("prefab"), "visualBounds": row.get("actualVisualBounds")}
                for row in build.get("bodies", []) if row.get("glyph") in {"각", "공", "검"}]
    geometry_text = " / ".join(f"{r['glyph']} {r['triangles']:,} tris ({r['parts']}개)" for r in geometry
                              if isinstance(r["triangles"], int) and isinstance(r["parts"], int))
    art = relative_if_file(ART / f"KTP_BOTANICAL_REVIEW{stage:02d}.md")
    return {"stage": stage, "editPass": edit_pass, "playPass": play_pass,
            "editRecordedPass": edit_recorded, "playRecordedPass": play_recorded,
            "capturesReady": captures_ready, "captureMvid": captured_mvid, "captureRows": capture_rows,
            "buildUtc": build.get("generatedUtc"), "buildMvid": build.get("appMvid"),
            "editUtc": edit.get("capturedUtc"), "playUtc": play.get("startedUtc"),
            "editMvid": edit_mvid, "playMvid": play_mvid,
            "checks": {"buildValid": build_valid, "currentBotanicalSourceSha": current_source,
                       "buildAndEditInventoryMatch": same_geometry,
                       "sixCapturesOneMvid": bool(one_capture_mvid), "editAfterBuildAndSameMvid": edit_current,
                       "playAfterBuildAndSameMvid": play_current},
            "buildReport": relative_if_file(paths["build"]), "editReport": relative_if_file(paths["edit"]),
            "playReport": relative_if_file(paths["play"]), "artReport": art,
            "videoReview": relative_if_file(ART / f"KTP_BOTANICAL_VIDEO_REVIEW{stage:02d}.md"),
            "experimentReview": relative_if_file(ART / "KTP_BOTANICAL_REVIEW13.md"),
            "experimentBuild": relative_if_file(ART / "botanical_build13.json"),
            "priorReviews": [{"path": relative_if_file(path), "label": path.stem.removeprefix("KTP_BOTANICAL_REVIEW") + " 미술 검토"}
                             for path in sorted(ART.glob("KTP_BOTANICAL_REVIEW*.md"), reverse=True)
                             if path.name not in (f"KTP_BOTANICAL_REVIEW{stage:02d}.md", "KTP_BOTANICAL_REVIEW13.md")],
            "geometry": geometry, "geometryText": "최신 Build 실측: " + (geometry_text or "기록 대기")
            + ". 실제 잎·가지의 화면 점유 범위는 게임 판정 폭과 별도입니다.",
            "editText": f"{stage:02d}와 같은 Build 이후·촬영 MVID·현재 식물 소스의 Edit 계약 7/7 통과. 배치·시계·반복·종료·원본 보존만 검증했습니다."
            if edit_pass else f"{stage:02d} Edit 계약: 최신 Build·촬영 MVID·소스 일치 근거 대기. 이전 7/7 통과를 현재판에 적용하지 않습니다.",
            "playText": "동일 버전 각·공·검의 실제 Update/Destroy 수명·정리 3/3 통과. 입력·피해·식재 접촉·미술·목표 FPS 통과는 아닙니다."
            if play_pass else f"{stage:02d} 실제 Play 3종: 동일 최신 버전의 완료·복원 근거 대기.",
            "artText": f"{stage:02d} 미술 검토 보고서가 있습니다. 통과·미달 범위는 보고서의 실제 관찰을 확인하세요. 기술 계약만으로 미술 PASS를 부여하지 않습니다."
            if art else f"{stage:02d} 미술 검토 대기. 13은 각 결박 미달·공 fallback 촬영·검 겨울 고목의 이전 실험입니다.",
            "versionText": "제작본과 검사·촬영 버전이 맞는 자료만 최신으로 표시합니다. 개별 검사 범위와 미검증 항목은 아래 보고서에서 확인할 수 있습니다."}


def stone_wave_status():
    """Read the final 19 capture and build evidence without granting an art PASS."""
    build_path, wave_path = ART / "stone_body_build.json", ART / "water_wave_tuning16.json"
    build, wave = read_json(build_path), read_json(wave_path)
    dates = [utc_stamp(build.get("generatedUtc")), utc_stamp(wave.get("generatedUtc"))]
    built = max(dates) if all(dates) else None
    rows = []
    for prefix in ("KTPIntegratedStills", "KTPExternalStills"):
        folder = prefix + "19"
        for stem in STONE_WAVE_STEMS:
            meta = read_json(ART / folder / f"{stem}_capture.json")
            stamp = utc_stamp(meta.get("capturedUtc"))
            rows.append({"stem": stem, "folder": folder,
                         "ready": bool(capture_complete(captures(folder, stem, meta), meta)
                                       and built and stamp and stamp >= built),
                         "mvid": mvid(meta.get("loadedRuntimeAssemblyMvid")),
                         "metadata": relative_if_file(ART / folder / f"{stem}_capture.json")})
    versions = {r["mvid"] for r in rows}
    ready = all(r["ready"] for r in rows) and len(versions) == 1 and None not in versions
    videos = [v for stem in STONE_WAVE_STEMS for v in videos_for(stem) if v["version"] == 19]
    video_ready = ready and len(videos) == 6 and all(v["runtimeMvid"] in versions for v in videos)
    geometry = [{k: r.get(k) for k in ("glyph", "vertices", "triangles", "sourceTangentCount",
                "derivativeTangentCount", "derivativeTangentsRebuilt", "meshOutput", "materialOutput")}
                for r in build.get("sources", []) if r.get("glyph") in ("막", "뭄")]
    valid_build = build.get("status") == "BUILT_STRUCTURAL_CHECKS_ONLY_VISUAL_RUNTIME_UNVERIFIED" \
        and build.get("sourceFilesUnchanged") is True and len(geometry) == 2 and {r["glyph"] for r in geometry} == {"막", "뭄"}
    counts = " / ".join(f"{r['glyph']} {r['triangles']:,} tris · {r['vertices']:,}정점" for r in geometry
                        if isinstance(r["triangles"], int) and isinstance(r["vertices"], int))
    art = relative_if_file(ART / "KTP_STONE_WAVE_REVIEW19.md")
    implementation = ROOT / "Docs/Art/SpellVFX120/KTP_STONE_WAVE_IMPLEMENTATION.md"
    return {"stage": 19, "capturesReady": ready, "videoVerified": video_ready, "videos": videos,
            "captureRows": rows, "geometry": geometry, "buildPass": valid_build,
            "buildReport": relative_if_file(build_path), "waveReport": relative_if_file(wave_path),
            "artReport": art, "previousReview": relative_if_file(ART / "KTP_STONE_WAVE_REVIEW18.md"),
            "implementation": publish_review_doc(implementation) if implementation.is_file() else None,
            "priorAudit": relative_if_file(ART / "traditional_catalog_audit_source_memory_attempt18.json"),
            "rejectedRoll": relative_if_file(ART / "KTP_WATER_ROLL_REVIEW20.md"),
            "geometryText": ("실제 Build: " + counts + ". 원본 보존 확인. UV·노멀은 유지하고 없던 탄젠트는 파생 메시에서 계산했습니다."
                             if valid_build else "돌 원형 교체의 실제 Build 기록 확인 대기."),
            "captureText": ("19 두 시점 정지 표본 30장 완비. " if ready else "19 두 시점 정지 표본 확인 대기. ")
                           + ("영상 6개 인코딩·전체 디코드·해시 검증 완료. 미술 판정과 별도입니다." if video_ready else "19 영상 6개 완료 근거 확인 대기."),
            "artText": "19 미술 검토: 막의 그림자·소멸 중 재부유는 개선됐습니다. 뭄의 접지 인상, 오의 얇은 물막·디더 조각은 여전히 미달입니다."
                       if art else "19 미술 검토 대기. 18의 잔여 접지·물막 문제를 후속 미술 통과로 바꾸지 않습니다.",
            "limitText": "곡면 물마루에 실제 KTP 수파 무늬를 사용했습니다. 돌 원형의 비율은 의도적으로 조정했습니다. 예시 촬영이며 엄폐·다리 통행·피해·최종 미술은 미검증입니다."}


INPUT_RECOVERY = ("diObserved", "sharedWiringObserved", "sceneFileUnchanged", "inputFilterRestored",
                  "virtualDevicesRemoved", "loopRemoved", "drawingExited", "cameraPropertiesRestored",
                  "inputSettingsRestored", "currentDevicesRestored", "actionMapStatesRestored",
                  "drawingBindingsRestored", "cameraPoseReturned", "cursorRestored", "timeScaleReturned")


def legacy_input_status():
    path = ART / "player_input_audit_native12.json"
    response_path = ART / "response_66c030db1bfc471f9c0c29953ae76e71.json"
    record, response = read_json(path), read_json(response_path)
    try:
        returned = json.loads(response.get("result", ""))
    except (TypeError, json.JSONDecodeError):
        returned = {}
    cases = record.get("cases", [])
    passed = record.get("status") == "PASS_OBSERVED_THREE_INPUT_CASTS" \
        and record.get("phase") == "FINISHED" and record.get("completed") == 3 \
        and all(record.get(k) == 0 for k in ("failed", "errors", "residualEffects", "remainingInputStages", "remainingObserveStages")) \
        and all(record.get(k) is True for k in INPUT_RECOVERY) \
        and len(cases) == 3 and {c.get("glyph") for c in cases} == {"가", "노", "머"} \
        and all(c.get("recognized") == c.get("glyph") and c.get("recognitionSuccess") is True
                and c.get("status") == "PASS_OBSERVED_INPUT_CAST_VFX" and c.get("remainingEffects") == 0 for c in cases) \
        and response.get("status") == "COMPLETE" and returned == record
    return {"passed": passed, "report": relative_if_file(path),
            "response": relative_if_file(response_path), "configuredTargets": record.get("configuredTargets"),
            "inkBefore": record.get("inkBefore"), "inkAfter": record.get("inkAfter"),
            "text": "12 본체 이후 가·노·머 3/3 입력 회귀 통과. 가상 마우스·키보드 → 기존 입력 수집 → 실제 인식 → 시전 → VFX 종료를 확인했습니다. 오류·실패·잔존 효과0, 필터·장치·루프·카메라·시간 복귀 확인."
            if passed else "12 본체 이후 3자 입력 회귀: 완료 근거 확인 전.",
            "limits": "인식 결과를 주입하지 않았고 기존 먹을 1.0→0.6 실제 소비했습니다. 적0인 C2 검사이므로 전체15종·실제 적 피해/패링·자유 필기·실물 입력장치·성능·미술은 미검증입니다. 아래 영상은 이 입력 검사의 플레이 녹화가 아닙니다."}


def input_status():
    """Validate unique final reports, not just a runner's aggregate PASS string.

    Observed input coverage is independent of version attribution: older reports
    did not instrument an MVID, so never borrow it from the render captures.
    """
    legacy = legacy_input_status()
    aggregate_path = ART / "player_input_batches16.json"
    aggregate = read_json(aggregate_path)
    summaries = aggregate.get("batches", [])
    summaries = summaries if isinstance(summaries, list) else []
    rows, reports, run_ids = [], [], []
    for summary in summaries:
        if not isinstance(summary, dict):
            rows.append({"passed": False, "batchId": None, "report": None}); continue
        number = summary.get("batchId")
        expected = INPUT_BATCH_GLYPHS.get(number, "")
        value = summary.get("output")
        path = Path(value).resolve() if isinstance(value, str) and value else None
        valid_path = bool(path and path.is_relative_to((ART / "PlayerInputBatches").resolve())
                          and path.suffix == ".json" and path.is_file())
        record = read_json(path) if valid_path else {}
        cases = record.get("cases", [])
        cases = cases if isinstance(cases, list) else []
        final_path = Path(record["output"]).resolve() if isinstance(record.get("output"), str) else None
        run_id = record.get("runId")
        same_summary = all(record.get(k) == summary.get(k) for k in ("batchId", "status", "requestedGlyphs",
                           "completed", "failed", "errors", "cleanupStatus", "configuredTargets", "inkBefore", "inkAfter"))
        restored = all(record.get(k) is True for k in INPUT_RECOVERY) \
            and record.get("cleanupStatus") == "OBSERVED_INPUT_AND_EFFECT_CLEANUP" \
            and bool(record.get("fileHashBefore")) and record.get("fileHashBefore") == record.get("fileHashAfter") \
            and bool(record.get("drawingBindingsBefore")) and record.get("drawingBindingsBefore") == record.get("drawingBindingsAfter")
        cases_pass = len(cases) == 5 and all(isinstance(c, dict) for c in cases) \
            and {c.get("glyph") for c in cases} == set(expected) \
            and all(c.get("recognized") == c.get("glyph") and c.get("recognitionSuccess") is True
                    and c.get("status") == "PASS_OBSERVED_INPUT_CAST_VFX" and c.get("remainingEffects") == 0
                    and all(c.get(k) is True for k in ("newVfxProfile", "clockMatches", "issuedPlanUnchanged")) for c in cases)
        passed = bool(valid_path and path == final_path and expected and run_id and same_summary and restored and cases_pass
                      and record.get("status") == "PASS_OBSERVED_SELECTED_BATCH_INPUT_CASTS"
                      and record.get("phase") == "FINISHED" and record.get("requestedGlyphs") == expected
                      and record.get("requestedCount") == 5 and record.get("completed") == 5
                      and record.get("scene") == "Assets/_Project/Scenes/Dev/C2_CodexWorld.unity"
                      and all(record.get(k) == 0 for k in ("failed", "errors", "residualEffects", "remainingInputStages", "remainingObserveStages")))
        version = mvid(record.get("appMvid")) or mvid(record.get("loadedRuntimeAssemblyMvid"))
        rows.append({"batchId": number, "glyphs": expected, "passed": passed, "restored": restored,
                     "report": relative_if_file(path) if valid_path else None, "runId": run_id,
                     "mvid": version, "configuredTargets": record.get("configuredTargets"),
                     "completed": record.get("completed"), "failed": record.get("failed"), "errors": record.get("errors")})
        if valid_path: reports.append(str(path))
        if run_id: run_ids.append(run_id)
    completed = [r for r in rows if r["passed"]]
    observed = {g for r in completed for g in r["glyphs"]}
    expected_all = set("".join(INPUT_BATCH_GLYPHS.values()))
    started, finished = utc_stamp(aggregate.get("startedUtc")), utc_stamp(aggregate.get("finishedUtc"))
    passed = bool(aggregate.get("status") == "PASS_OBSERVED_15_REGISTERED_INPUT_CASTS_ONLY"
                  and started and finished and finished >= started and len(rows) == 3 and len(completed) == 3
                  and {r["batchId"] for r in rows} == {1, 2, 3} and observed == expected_all
                  and len(reports) == len(set(reports)) == 3 and len(run_ids) == len(set(run_ids)) == 3)
    versions = {r.get("mvid") for r in rows}
    version_verified = passed and len(versions) == 1 and None not in versions
    evidence = [{"path": relative_if_file(ART / "KTP_PLAYER_INPUT15_REVIEW.md"), "label": "15종 입력 결과 요약"},
                {"path": relative_if_file(aggregate_path), "label": "15종 배치 집계"}]
    evidence += [{"path": r["report"], "label": f"배치 {r['batchId']} · {r.get('glyphs', '')}"} for r in rows if r["report"]]
    evidence += [{"path": legacy["report"], "label": "이전 12판 3종 입력 관측"},
                 {"path": legacy["response"], "label": "이전 3종 완료 응답"}]
    text = ("등록 획 15종 실제 입력 관측 통과. 별도 Play 3회에서 5종씩 인식·시전·VFX 종료를 확인했습니다. 실패·오류·잔존 효과 0, 장치·루프·씬·카메라·시간 복귀를 확인했습니다."
            if passed else f"등록 획 15종 배치 검사 진행 상태: 최종 개별 보고서 {len(completed)}/3개, 고유 {len(observed)}/15종 관측 확인. 전체 통과 근거는 아직 갖춰지지 않았습니다.")
    if not passed:
        text += " 이전 가·노·머 3종 관측 통과는 별도 보존합니다." if legacy["passed"] else " 이전 3종 관측도 완료 근거 확인 전입니다."
    return {"passed": passed, "observedCount": len(observed), "runtimeVersionVerified": version_verified,
            "heading": "실제 입력 경로 검사", "badge": "등록 획 15종 관측 통과" if passed else f"15종 배치 · {len(observed)}종 확인",
            "text": text if aggregate else "이전 3종 기록: " + legacy["text"] + " 새 15종 배치는 대기 중입니다.",
            "versionText": "세 입력 보고서에 같은 실행 MVID가 기록됐습니다. 미술 촬영·게임 피해 판정과는 별개입니다."
                           if version_verified else "실행 버전 미검증: 입력 보고서에 세 배치의 동일 MVID를 입증하는 기록이 없습니다. 촬영 19의 버전을 대신 적용하지 않습니다.",
            "limits": "등록 XML 획을 가상 입력장치로 전달하고 기존 먹을 소비했습니다. 인식 결과 주입은 없습니다. 적 피해·패링 성공·자유 필기·실물 입력장치·원시 입력의 비트 일치·성능·미술은 미검증입니다. 아래 영상은 입력 검사 녹화가 아닙니다.",
            "batches": rows, "legacy": legacy, "reports": [r for r in evidence if r["path"]]}


def salvage_status():
    folder = ART / "MeshySummons/Blender/JangseungSalvage01"
    stats = read_json(folder / "final_mesh_checks.json")
    return {"available": (folder / "SALVAGE_REVIEW.md").is_file(),
            "report": relative_if_file(folder / "SALVAGE_REVIEW.md"),
            "stats": relative_if_file(folder / "final_mesh_checks.json"),
            "image": relative_if_file(folder / "Renders/05_salvage_oblique.png"),
            "triangles": stats.get("triangles"), "vertices": stats.get("vertices"),
            "note": "최종 미술 반려 · Unity 미연결. Meshy 얼굴의 눈·코·입을 재사용하고 Blender에서 긴 돌기둥을 새로 만들었습니다. 기둥 외곽은 복구했지만 귀면 부조가 붙은 인상이 남았습니다. 추가 유료 생성 0건, 리깅·애니메이션 없음."}


def imugi_salvage_status():
    folder = ART / "MeshySummons/Blender/ImugiSalvage01"
    stats = read_json(folder / "final_mesh_checks.json")
    return {"available": (folder / "SALVAGE_REVIEW.md").is_file(),
            "report": relative_if_file(folder / "SALVAGE_REVIEW.md"),
            "stats": relative_if_file(folder / "final_mesh_checks.json"),
            "image": relative_if_file(folder / "Renders/06_final_oblique.png"),
            "triangles": stats.get("triangles"), "vertices": stats.get("vertices"),
            "note": "최종 미술 반려 · 채택 안 함 · Unity 미연결. Meshy 머리·목 표면·한쪽 발을 재사용하고 Blender에서 연결 체적·꼬리를 만든 합성 수정본입니다. 전체가 새 Meshy 생성 모델인 것은 아닙니다. 몸통의 판형 실루엣과 목·꼬리 단차가 남았습니다. 이번 구제 실험 추가 소비 0크레딧. UV/PBR·리깅·애니메이션·FBX 왕복은 미검증입니다."}


def catalog_status():
    path = ART / "ktp_catalog_mapping.json"
    record = read_json(path)
    if not record or record.get("total") != 120 or not str(record.get("status", "")).startswith("BUILT_"):
        return {"text": "120프로필 Native 연결 빌더 준비 · 전체 Build 실행 기록은 아직 없습니다.", "report": None}
    return {"text": f"실행 기록: {record['total']}프로필 Native 연결 · 발동 {record.get('casts', '?')} / 명중 {record.get('impacts', '?')} / 필드 {record.get('fields', '?')} / 공백 {record.get('reserved', '?')} 유지.",
            "report": relative_if_file(path)}


def meshy_status():
    first = read_json(ART / "MeshySummons/ledger.json")
    retry = read_json(ART / "MeshySummons/Retry02/ledger02.json")
    prior = first.get("final_consumed_credits")
    extra = retry.get("actual_consumed_credits")
    total = prior + extra if isinstance(prior, (int, float)) and isinstance(extra, (int, float)) else None
    return {"actualCredits": total, "firstCredits": prior, "retryCredits": extra,
            "retryReport": relative_if_file(ART / "MeshySummons/Retry02/RETRY02_REPORT.md"),
            "paidRequestsStopped": first.get("further_paid_requests_stopped") is True
            and retry.get("further_paid_requests_stopped") is True}


def model_data():
    delivery = read_json(ART / "MeshySummons/Source/delivery_manifest.json")
    source = {m["id"]: m for m in delivery.get("models", [])}
    roundtrip = {m["id"]: m for m in read_json(ART / "MeshySummons/Blender/fbx_roundtrip.json").get("models", [])}
    unity = {m["id"]: m for m in read_json(ART / "meshy_unity_import.json").get("models", [])}
    descriptions = [
        ("016_ACF0", "곰", "뿌리 수호사슴", "수피 미술 목표 실패", "현재는 흰 사슴으로 읽힙니다. 몸통을 이루는 뿌리·덩굴과 수피 표현이 부족합니다."),
        ("040_B188", "놈", "불 해태", "최종 미술 미승인", "해태 얼굴과 갈기는 읽힙니다. 밝은 주황색, 면 경계와 들린 앞발 자세가 남아 있습니다."),
        ("088_C19C", "솜", "철 민화호랑이", "최종 미술 미승인", "입체 줄무늬가 읽힙니다. 민화의 과장된 비례보다 실제 호랑이에 가까운 후보입니다."),
    ]
    result = []
    for ident, glyph, name, judgement, note in descriptions:
        item = source.get(ident, {})
        geometry = item.get("actual_source_geometry", {})
        rt = roundtrip.get(ident, {})
        imported = unity.get(ident, {})
        result.append({"id": ident, "glyph": glyph, "name": name,
                       "image": relative_if_file(ART / f"MeshySummons/Source/{ident}/refine/thumbnail_url.png"),
                       "blenderImage": relative_if_file(ART / f"MeshySummons/Blender/Review/{ident}_three_quarter.png"),
                       "tris": geometry.get("unique_mesh_triangles"), "requested": item.get("requested_triangles"),
                       "roundtrip": rt.get("geometryStatus") == "PASS",
                       "unityImported": imported.get("triangleMatch") is True and imported.get("shaderSupported") is True,
                       "unityTris": imported.get("actualTriangles"),
                       "unitySkins": imported.get("skins"), "unityAnimators": imported.get("animators"),
                       "armatures": rt.get("armatures"), "animations": geometry.get("animations"),
                       "judgement": judgement, "note": note})
    rejected = []
    for ident, glyph, name, note in [
        ("064_BAB8", "몸", "돌 장승 거인", "갑옷·치마·버클 형상으로 장승 방향과 달라 반려"),
        ("112_C634", "옴", "수묵 이무기", "직립 몸통과 뒷다리로 이무기 실루엣과 달라 반려"),
    ]:
        retry_image = relative_if_file(ART / f"MeshySummons/Retry02/{ident}/preview/thumbnail_url.png")
        rejected.append({"id": ident, "glyph": glyph, "name": name, "note": note,
                         "image": relative_if_file(ART / f"MeshySummons/Source/{ident}/preview/thumbnail_url.png"),
                         "retryImage": retry_image,
                         "retryNote": ("Retry02도 형상 반려. 기둥형 장승 대신 복식·넓은 귀면이 남았습니다."
                                       if glyph == "몸" else "Retry02도 형상 반려. 낮은 수평 S형 대신 목을 세운 몸 축이 반복됐습니다.")
                         if retry_image else "재시도 자료 없음"})
    return result, rejected


PAGE = r'''<!doctype html>
<html lang="ko"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>오행부 · KTP / Meshy 재제작 검수</title>
<style>
:root{color-scheme:light;--paper:#f0ecdf;--card:#fbf8ef;--ink:#292922;--muted:#65695f;--line:#d5d0bf;--accent:#456b5c}
*{box-sizing:border-box}body{margin:0;background:var(--paper);color:var(--ink);font:15px/1.65 system-ui,'Malgun Gothic',sans-serif}main{max-width:1560px;margin:auto;padding:32px 28px 48px}a{color:var(--accent);text-underline-offset:3px}h1{font-size:clamp(25px,3vw,37px);line-height:1.3;margin:10px 0}h2{font-size:23px;margin:0}h3{font-size:19px;margin:0 0 4px}.eyebrow{color:var(--accent);font-weight:650;letter-spacing:.04em}.intro{max-width:920px;color:var(--muted);margin:12px 0}.notice,.empty{background:#e5e7d9;border-left:3px solid var(--accent);padding:12px 16px;margin:18px 0;color:#414d40}.section{margin-top:34px}.section-head{display:flex;justify-content:space-between;align-items:baseline;gap:15px;flex-wrap:wrap;margin-bottom:12px}.subtle{font-size:13px;color:var(--muted)}.grid{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:15px}.models{grid-template-columns:repeat(3,minmax(0,1fr))}.card{min-width:0;background:var(--card);border:1px solid var(--line);border-radius:8px;overflow:hidden}.card-head,.card-copy{padding:13px 14px}.card-copy p{margin:8px 0;font-size:14px}.card .kind{font-size:12px;color:var(--accent);font-weight:650}.image-button{display:block;border:0;width:100%;padding:0;background:#e8e5dc;cursor:zoom-in}.image-button img{display:block;width:100%;aspect-ratio:16/9;object-fit:contain}.model-image img{aspect-ratio:1;max-height:340px}.missing-image{display:grid;place-content:center;aspect-ratio:16/9;background:#e8e5dc;color:var(--muted);font-size:14px}.phases{display:flex;gap:5px;padding:10px 12px 0}.phases button{flex:1;min-width:0;border:1px solid var(--line);background:transparent;border-radius:4px;padding:5px 2px;color:var(--muted);font:inherit;font-size:12px;cursor:pointer}.phases button[aria-pressed=true]{background:var(--accent);color:white;border-color:var(--accent)}.phases button:disabled{opacity:.45;cursor:default}.source{font-size:11px;color:var(--muted);overflow-wrap:anywhere;margin-top:9px}.pills{display:flex;gap:5px;flex-wrap:wrap;margin:8px 0}.pill{font-size:11px;display:inline-block;padding:2px 7px;border:1px solid #b9c6b9;border-radius:3px;background:#eaf0e6}.pending{background:#ece5d8;border-color:#d2c1a0;color:#715b35}.failed{background:#eee1dc;border-color:#d3b5a7;color:#8c4733}dl{margin:9px 0;font-size:13px}dl>div{display:grid;grid-template-columns:62px 1fr;gap:6px;padding:5px 0;border-top:1px solid #e5e0d3}dt{color:var(--muted)}dd{margin:0}.rejected{display:flex;gap:20px;flex-wrap:wrap;margin-top:15px}.rejected article{display:flex;align-items:center;gap:12px;max-width:470px}.rejected img{height:92px;width:92px;object-fit:contain;background:#e8e5dc;border-radius:5px}.rejected p{font-size:13px;margin:0}.rejected strong{font-size:14px}.reports{display:flex;gap:15px;flex-wrap:wrap;margin-top:13px}footer{margin-top:35px;border-top:1px solid var(--line);padding-top:14px;color:var(--muted);font-size:12px}dialog{padding:0;border:1px solid var(--line);border-radius:8px;background:var(--card);color:var(--ink);width:min(1200px,95vw);max-height:95vh}dialog::backdrop{background:#151814dd}.modal-head{padding:10px 15px;display:flex;gap:15px;justify-content:space-between;align-items:center}.modal-head button{font:inherit;background:transparent;border:1px solid var(--line);padding:4px 12px;border-radius:4px;cursor:pointer}dialog img{display:block;width:100%;height:auto;max-height:76vh;object-fit:contain;background:#dddacf}.modal-caption{margin:0;padding:10px 15px;font-size:13px;color:var(--muted)}button:focus-visible,a:focus-visible{outline:3px solid #b07631;outline-offset:3px}@media(max-width:1040px){.grid{grid-template-columns:repeat(2,minmax(0,1fr))}.models{grid-template-columns:repeat(3,minmax(0,1fr))}}@media(max-width:740px){main{padding:22px 16px 35px}.models{grid-template-columns:1fr}.model-image img{max-height:290px}.section-head{display:block}.grid{gap:12px}.card-head,.card-copy{padding:10px}h3{font-size:17px}.phases{gap:3px;padding:8px 6px 0}}@media(max-width:480px){.grid{grid-template-columns:1fr}.section{margin-top:28px}}
</style></head><body><main>
<a href="REVIEW.html">← 기존 120종 검수 자료</a>
<header><div class="eyebrow">오행부 · 사용자 피드백 반영</div><h1>KTP / Meshy 재제작 검수</h1>
<p class="intro">타격·발동·소환진·방어막은 KTP 완제품 효과의 움직임과 문양을 중심으로 다시 제작합니다. 소환수는 Meshy 모델을 별도로 정리하고 있습니다.</p>
<p class="notice"><strong>제작 중 · 미술 승인 전.</strong> 원본 효과 표본, 통합 시제품, 생성 모델을 나누어 표시합니다. 120개 Native 발동·타격 연결과 <strong>120개 주형상 재제작 완료는 서로 다릅니다.</strong> 실제 게임 동작 통과나 최종 미술 승인을 뜻하지 않습니다.<br><span id="catalog-status"></span></p></header>
<section class="section" aria-labelledby="native-title"><div class="section-head"><h2 id="native-title">KTP 원본 효과 4종</h2><span class="subtle">C2 · 1280 × 720 · 각 5개 시간 표본</span></div>
<p class="subtle">원본 프리팹의 파티클을 분리해 촬영했습니다. Fly의 전체 Animator 순서나 완성된 술식의 재생 검증은 아닙니다. 시간 버튼으로 표본을 바꾸고 이미지를 눌러 크게 볼 수 있습니다.</p><div id="native" class="grid"></div></section>
<section class="section" aria-labelledby="body-title"><div class="section-head"><h2 id="body-title">노·모 본체 교정 · 진행 중</h2><span class="pill pending">10 기준 · 미술 미완료</span></div><div class="notice"><p id="body-edit-status"></p><p id="body-play-status"></p><p id="body-art-status"></p></div><p class="subtle" id="body-video-status"></p><div class="reports" id="body-reports"></div></section>
<section class="section" aria-labelledby="botanical-title"><div class="section-head"><h2 id="botanical-title">각·공·검 식물 본체</h2><span class="pill pending" id="botanical-stage"></span></div><div class="notice"><p id="botanical-edit"></p><p id="botanical-play"></p><p id="botanical-art"></p></div><p class="subtle" id="botanical-geometry"></p><p class="subtle" id="botanical-version"></p><div class="reports" id="botanical-reports"></div></section>
<section class="section" aria-labelledby="stone-title"><div class="section-head"><h2 id="stone-title">막·뭄·오 돌·물결 교정</h2><span class="pill pending">19 촬영 · 미술 미완료</span></div><div class="notice"><p id="stone-art"></p><p id="stone-capture"></p></div><p class="subtle" id="stone-geometry"></p><p class="subtle" id="stone-limits"></p><div class="reports" id="stone-reports"></div></section>
<section class="section" aria-labelledby="input-title"><div class="section-head"><h2 id="input-title">실제 입력 경로 검사</h2><span class="subtle" id="input-badge"></span></div><div class="notice"><p id="input-status"></p><p id="input-limits" class="subtle"></p></div><details><summary>입력 검사 버전과 개별 기록</summary><p id="input-version" class="subtle"></p><div id="input-reports" class="reports"></div></details></section>
<section class="section" aria-labelledby="integrated-title"><div class="section-head"><h2 id="integrated-title">KTP 통합 시제품</h2><span class="subtle" id="integrated-count"></span></div><p class="subtle">각 술식·시점별로 메타데이터와 5장이 갖춰진 최신 촬영을 선택합니다. 같은 표본 번호라도 술식마다 시각이 다릅니다. 최신 촬영이 미완료이면 이전 완비 자료를 유지하고 버전을 표시합니다.</p><div class="notice"><span id="play-status"></span><br><span id="contract-status"></span><br><strong>검사 범위:</strong> 진단용 명중 시각을 공급한 객체 수명·연결 검사입니다. 플레이어 입력·실제 시전/피해 판정·미술·목표 FPS 통과는 포함하지 않습니다.</div><p id="video-status" class="subtle"></p><div id="integrated"></div><details style="margin-top:12px"><summary>실제 촬영 파일 집계와 검토 근거</summary><div id="capture-evidence" class="subtle"></div><div id="capture-reports" class="reports"></div></details></section>
<section class="section" aria-labelledby="models-title"><div class="section-head"><h2 id="models-title">Meshy 소환수 3종</h2><span class="subtle">Meshy 7 · 2K PBR · 정적 모델 제작 후보</span></div>
<p class="subtle">큰 이미지는 <strong>Meshy 생성 원본 미리보기</strong>입니다. Blender 정리 후 사선 렌더는 카드 링크, C2 배치는 위 통합 촬영에서 확인합니다. Unity 정적 임포트는 리깅·보행·게임 동작 완료와 구분합니다.</p><p class="notice"><strong>추가 소환수 2종 미완료.</strong> 몸(돌 장승)·옴(이무기)은 <strong>Retry02도 형상 반려</strong>되어 교체 제작이 남아 있습니다. 곰·놈·솜 3종은 Unity 정적 합계 37,489 tris, 리깅·애니메이션 없이 연결한 후보입니다.<br><span id="meshy-cost"></span></p><div id="models" class="grid models"></div>
<div class="reports"><a href="MeshySummons/Source/REPORT.md">생성·출처 보고서</a><a href="MeshySummons/Blender/BLENDER_EXPORT_REPORT.md">Blender 정리·FBX 검수 보고서</a><a href="meshy_unity_import.json">Unity 실제 임포트 수치</a></div>
<details style="margin-top:22px"><summary>형상 반려 2종 · 첫 생성 / Retry02 · 모두 사용 안 함</summary><div id="rejected" class="rejected"></div><div id="retry-report" class="reports"></div></details></section>
<section class="section" id="salvage-section" hidden><div class="section-head"><h2>장승 Blender 구제 실험</h2><span class="pill failed">최종 미술 반려 · Unity 미연결</span></div><div id="salvage" class="rejected"></div></section>
<section class="section" id="imugi-salvage-section" hidden><div class="section-head"><h2>이무기 Blender 합성 구제 실험</h2><span class="pill failed">최종 미술 반려 · 채택 안 함</span></div><div id="imugi-salvage" class="rejected"></div></section>
<footer>로컬 검수 전용 · 외부 배포 없음 · 자료 갱신 __GENERATED__<br>원본 이미지에는 텍스트·색 보정을 추가하지 않았습니다. 누락된 촬영은 미촬영으로 표시합니다.</footer>
</main><dialog id="viewer"><div class="modal-head"><strong id="viewer-title"></strong><button type="button" id="close-viewer" aria-label="미리보기 닫기">닫기</button></div><img id="viewer-image" alt=""><video id="viewer-video" controls playsinline preload="metadata" hidden style="width:100%;max-height:76vh"></video><p class="modal-caption" id="viewer-caption"></p></dialog>
<script type="application/json" id="review-data">__DATA__</script><script>
'use strict';
const data=JSON.parse(document.getElementById('review-data').textContent);
const el=(tag,cls,text)=>{const n=document.createElement(tag);if(cls)n.className=cls;if(text!==undefined)n.textContent=text;return n;};
const viewer=document.getElementById('viewer');
function openImage(src,title,caption){if(!src)return;const video=document.getElementById('viewer-video');video.pause();video.hidden=true;const img=document.getElementById('viewer-image');img.style.display='block';img.src=src;img.alt=title;document.getElementById('viewer-title').textContent=title;document.getElementById('viewer-caption').textContent=caption;viewer.showModal();}
function openVideo(video,title){document.getElementById('viewer-image').style.display='none';const player=document.getElementById('viewer-video');player.src=video.path;player.hidden=false;document.getElementById('viewer-title').textContent=title+' · '+video.label;document.getElementById('viewer-caption').textContent=video.scope;viewer.showModal();}
viewer.addEventListener('close',()=>document.getElementById('viewer-video').pause());
document.getElementById('close-viewer').addEventListener('click',()=>viewer.close());
viewer.addEventListener('click',e=>{if(e.target===viewer)viewer.close();});
function imageBlock(src,title,caption,extra=''){if(!src)return el('div','missing-image','미촬영');const b=el('button','image-button '+extra);b.type='button';b.setAttribute('aria-label',title+' 크게 보기');const img=el('img');img.src=src;img.alt=title;img.loading='lazy';img.addEventListener('error',()=>b.replaceWith(el('div','missing-image','미촬영 · 파일 확인 필요')));b.append(img);b.addEventListener('click',()=>openImage(src,title,caption));return b;}
function captureCard(item){
 const card=el('article','card'),head=el('div','card-head');head.append(el('div','kind',item.kind),el('h3','',item.role),el('div','subtle',item.name));card.append(head);
 const preview=el('div'),row=el('div','phases'),viewRow=el('div','phases'),provenance=el('div','source');
 const views=item.views??[{label:'원본',frames:item.frames,folder:'KTPNativeReview02',complete:item.frames.every(f=>f.image&&f.metadata&&f.time!==null)}],viewButtons=[];
 let currentView=0,currentPhase=2;
 card.append(preview);if(views.length>1)card.append(viewRow);card.append(row);
 function choosePhase(i){
  currentPhase=i;const view=views[currentView],f=view.frames[i],time=f.time===null?'표본 '+(i+1):Number(f.time).toFixed(2)+'초';
  preview.replaceChildren(imageBlock(f.image,item.name+' · '+view.label+' · '+time,item.kind+' / '+view.folder+' / '+(view.camera??'')+' / '+f.image));
  Array.from(row.children).forEach((b,j)=>b.setAttribute('aria-pressed',String(i===j)));
 }
 function chooseView(index){
  currentView=index;const view=views[index];row.replaceChildren();
  view.frames.forEach((f,i)=>{const b=el('button','',f.time===null?'표본 '+(i+1):Number(f.time).toFixed(2)+'s');b.type='button';b.disabled=!f.image;b.title=f.image?'실제 촬영 시각 · '+view.label:'미촬영';b.addEventListener('click',()=>choosePhase(i));row.append(b);});
  viewButtons.forEach((b,i)=>b.setAttribute('aria-pressed',String(index===i)));
  const available=view.frames.map((f,i)=>f.image?i:-1).filter(i=>i>=0);choosePhase(available.includes(currentPhase)?currentPhase:(available[0]??0));
  provenance.replaceChildren(el('span','',view.folder+' · '+(view.complete?'5/5 + 촬영 JSON 확인':'촬영 일부 / 메타데이터 확인 필요')));
  if(view.metadata){const a=el('a','',' · 촬영 근거');a.href=view.metadata;provenance.append(a);}
 }
 views.forEach((view,i)=>{const b=el('button','',view.label);b.type='button';b.addEventListener('click',()=>chooseView(i));viewButtons.push(b);viewRow.append(b);});
 const copy=el('div','card-copy');copy.append(el('p','',item.note),provenance,el('div','source',item.source));
 if(item.videos?.length){const videos=el('div','reports');item.videos.forEach(v=>{const a=el('a','',v.label);a.href=v.path;a.addEventListener('click',e=>{e.preventDefault();openVideo(v,item.name);});videos.append(a);});copy.append(videos);}
 card.append(copy);chooseView(0);return card;
}
data.native.forEach(x=>document.getElementById('native').append(captureCard(x)));
const integrated=document.getElementById('integrated');
if(!data.integrated.length){integrated.append(el('p','empty','통합 시제품 촬영 준비 중 — 현재는 KTP 원본 자료만 표시합니다.'));document.getElementById('integrated-count').textContent='미촬영';}
else{integrated.className='grid';data.integrated.forEach(x=>integrated.append(captureCard(x)));const images=data.integrated.reduce((n,x)=>n+x.views.reduce((v,s)=>v+s.frames.filter(f=>f.image).length,0),0);document.getElementById('integrated-count').textContent=data.integrated.length+'개 프로필 · 선택된 시점 자료 '+images+'장 · 미술 검수 중';}
document.getElementById('catalog-status').textContent=data.catalog.text;
document.getElementById('play-status').textContent=data.validation.playText;
document.getElementById('contract-status').textContent=data.validation.catalogText;
document.getElementById('body-edit-status').textContent=data.body.editText;
document.getElementById('body-play-status').textContent=data.body.playText;
document.getElementById('body-art-status').textContent=data.body.artText;
document.getElementById('body-video-status').textContent=data.body.videoText;
for(const [path,label] of [[data.body.editReport,'본체 Edit 공간 계약 기록'],[data.body.playReport,'본체 실제 Play 수명 기록'],[data.body.artReport,'10 정지 화면 미술 검토'],[data.body.videoReview,'10 영상 원본 32표본 검토'],[data.body.rejectedExperimentReview,'11 토괴 실험 반려 검토'],[data.body.rejectedExperimentReport,'11 실험 Build 보존 기록']]){if(!path)continue;const a=el('a','',label);a.href=path;document.getElementById('body-reports').append(a);}
document.getElementById('botanical-stage').textContent=String(data.botanical.stage).padStart(2,'0')+(data.botanical.capturesReady?' 촬영 · 미술 별도 판정':' 촬영·검사 대기');
for(const [id,key] of [['botanical-edit','editText'],['botanical-play','playText'],['botanical-art','artText'],['botanical-geometry','geometryText'],['botanical-version','versionText']])document.getElementById(id).textContent=data.botanical[key];
for(const [path,label] of [[data.botanical.buildReport,'최신 식물 제작·실측'],[data.botanical.editReport,'식물 Edit 7행 검사'],[data.botanical.playReport,'식물 실제 Play 3종 검사'],[data.botanical.artReport,String(data.botanical.stage).padStart(2,'0')+' 미술 검토'],[data.botanical.videoReview,'두 시점 영상 표본 검토'],[data.botanical.experimentReview,'13 실험·대체 표시 검토'],[data.botanical.experimentBuild,'13 이전 제작본'],...data.botanical.priorReviews.map(r=>[r.path,r.label])]){if(!path)continue;const a=el('a','',label);a.href=path;document.getElementById('botanical-reports').append(a);}
for(const [id,key] of [['stone-art','artText'],['stone-capture','captureText'],['stone-geometry','geometryText'],['stone-limits','limitText']])document.getElementById(id).textContent=data.stoneWave[key];
for(const [path,label] of [[data.stoneWave.buildReport,'돌 원형·실제 메시 수치'],[data.stoneWave.waveReport,'수파 문양 연결 기록'],[data.stoneWave.artReport,'19 실제 장면 검토'],[data.stoneWave.previousReview,'18 이전 장면 검토'],[data.stoneWave.implementation,'구현·검증 경계'],[data.stoneWave.rejectedRoll,'20 물마루 반려·19 복원'],[data.stoneWave.priorAudit,'18 원본 메모리 비교 실패 보존']]){if(!path)continue;const a=el('a','',label);a.href=path;document.getElementById('stone-reports').append(a);}
for(const [id,key] of [['input-title','heading'],['input-badge','badge'],['input-status','text'],['input-limits','limits'],['input-version','versionText']])document.getElementById(id).textContent=data.input[key];
for(const r of data.input.reports){const a=el('a','',r.label);a.href=r.path;document.getElementById('input-reports').append(a);}
const videoCount=data.integrated.reduce((n,i)=>n+(i.videos?.length??0),0);
document.getElementById('video-status').textContent=videoCount?'C2 Editor 카메라 진단 재생 영상 '+videoCount+'개를 카드에서 볼 수 있습니다. 인코딩·전체 디코드·파일 해시를 확인했습니다. 실제 사용자 입력 영상이나 120종 전체 영상이 아닙니다. 영상과 정지 표본의 버전은 각각 표시합니다.':'새 영상 촬영·인코딩 대기. 완료된 MP4와 검증 기록이 있을 때만 재생 버튼을 표시합니다.';
document.getElementById('meshy-cost').textContent=data.meshy.actualCredits===null?'Meshy 실제 비용 기록 확인 필요.':'Meshy 실제 누적 '+data.meshy.actualCredits+'크레딧 = 첫 배치 '+data.meshy.firstCredits+' + Retry02 '+data.meshy.retryCredits+'. '+(data.meshy.paidRequestsStopped?'추가 유료 요청은 중단했습니다.':'추가 요청 상태 확인 필요.');
if(data.meshy.retryReport){const a=el('a','','Retry02 실제 형상 반려·비용 보고서');a.href=data.meshy.retryReport;document.getElementById('retry-report').append(a);}
data.captureSets.forEach(s=>document.getElementById('capture-evidence').append(el('p','',s.folder+' — 실제 PNG '+s.images+'장 / '+s.groups+'프로필 / 촬영 JSON '+s.manifests+'개 / 5장 완비 '+s.completeGroups+'개')));
data.reports.forEach(r=>{const a=el('a','',r.label);a.href=r.path;document.getElementById('capture-reports').append(a);});
for(const [path,label] of [[data.validation.playReport,'실제 Play 5종 수명 검사'],[data.validation.catalogReport,'120프로필 Edit preview 계약 검사']]){if(!path)continue;const a=el('a','',label);a.href=path;document.getElementById('capture-reports').append(a);}
if(data.catalog.report){const a=el('a','','120프로필 Native 연결 실행 기록');a.href=data.catalog.report;document.getElementById('capture-reports').append(a);}
function detail(list,label,value){const row=el('div');row.append(el('dt','',label),el('dd','',value));list.append(row);}
data.models.forEach(m=>{const card=el('article','card'),head=el('div','card-head');head.append(el('div','kind','제작 후보 · 미술 승인 전'),el('h3','',m.glyph+' · '+m.name));card.append(head,imageBlock(m.image,m.glyph+' · Meshy 생성 원본','Meshy refine 미리보기입니다. Blender 정리 후의 렌더가 아닙니다.','model-image'));const copy=el('div','card-copy'),pills=el('div','pills');pills.append(el('span','pill '+(m.glyph==='곰'?'failed':'pending'),m.judgement));copy.append(pills,el('p','',m.note));const dl=el('dl');detail(dl,'생성','Meshy 7 · 2K PBR');detail(dl,'실제 메시',(m.tris?.toLocaleString('ko-KR')??'미검증')+' tris · 원본 GLB 실측');detail(dl,'요청 수치',(m.requested?.toLocaleString('ko-KR')??'미확인')+' · 실제 tris와 구분');detail(dl,'Blender',m.roundtrip?'방향·크기·재질 1차 정리 / FBX 왕복 형상 통과':'정리·왕복 검사 미확인');detail(dl,'리깅','미진행 · Armature '+(m.armatures??'미확인')+' / Animation '+(m.animations??'미확인'));detail(dl,'Unity',m.unityImported?'정적 임포트·프리팹 연결 확인 / '+m.unityTris.toLocaleString('ko-KR')+' tris / Skin '+m.unitySkins+' · Animator '+m.unityAnimators:'임포트 미검증');detail(dl,'게임 동작','보행·타격·소환 AI 미검증');copy.append(dl);if(m.blenderImage){const a=el('a','subtle','Blender 정리 후 사선 렌더 보기');a.href=m.blenderImage;a.addEventListener('click',e=>{e.preventDefault();openImage(m.blenderImage,m.glyph+' · Blender 정리 후 사선','정적 형상·재질 검수. 리깅·Unity 게임 동작은 미검증입니다.');});copy.append(a);}card.append(copy);document.getElementById('models').append(card);});
data.rejected.forEach(m=>{const a=el('article');for(const [src,label] of [[m.image,'첫 생성'],[m.retryImage,'Retry02']]){if(!src)continue;const figure=el('div'),img=el('img');img.src=src;img.alt=m.glyph+' · '+label+' 반려 형상';img.loading='lazy';img.addEventListener('error',()=>figure.remove());figure.append(img,el('p','subtle',label));a.append(figure);}const d=el('div');d.append(el('strong','',m.glyph+' · '+m.name),el('p','',m.retryImage?m.retryNote:m.note),el('p','subtle',m.glyph==='몸'&&data.salvage.available?'원형 미사용 · 별도 Blender 구제 실험도 최종 미술 반려':'PBR·Blender·Unity 미진행 · 사용 안 함'));a.append(d);document.getElementById('rejected').append(a);});
if(data.salvage.available){document.getElementById('salvage-section').hidden=false;const row=document.getElementById('salvage'),preview=imageBlock(data.salvage.image,'몸 · Blender 구제 실험','Meshy 얼굴 부분 재사용 + 새 Blender 돌기둥. 최종 미술 반려, Unity 미연결.');preview.style.maxWidth='300px';const img=preview.querySelector('img');if(img)img.style.cssText='width:100%;height:auto;aspect-ratio:16/9';row.append(preview);const copy=el('div');copy.style.maxWidth='700px';copy.append(el('p','',data.salvage.note),el('p','subtle',(data.salvage.triangles?.toLocaleString('ko-KR')??'미검증')+' tris / '+(data.salvage.vertices?.toLocaleString('ko-KR')??'미검증')+' 정점 · 최종 Blender 메시 실측 · 기존 3종 Unity 합계와 별도'));for(const [path,label] of [[data.salvage.report,'구제 실험·반려 보고서'],[data.salvage.stats,'최종 메시 실측']]){if(!path)continue;const a=el('a','',label);a.href=path;copy.append(a,document.createTextNode(' · '));}row.append(copy);}
if(data.imugiSalvage.available){document.getElementById('imugi-salvage-section').hidden=false;const row=document.getElementById('imugi-salvage'),preview=imageBlock(data.imugiSalvage.image,'옴 · 합성 구제 실험','Meshy 부분 재사용 + Blender 합성 수정본. 최종 미술 반려, 채택·Unity 연결 안 함.');preview.style.maxWidth='300px';const img=preview.querySelector('img');if(img)img.style.cssText='width:100%;height:auto;aspect-ratio:16/9';row.append(preview);const copy=el('div');copy.style.maxWidth='700px';copy.append(el('p','',data.imugiSalvage.note),el('p','subtle',(data.imugiSalvage.triangles?.toLocaleString('ko-KR')??'미검증')+' tris / '+(data.imugiSalvage.vertices?.toLocaleString('ko-KR')??'미검증')+' 정점 · Blender 최종 실측 · 채택된 Unity 모델 합계에 포함 안 함'));for(const [path,label] of [[data.imugiSalvage.report,'합성 출처·미술 반려 보고서'],[data.imugiSalvage.stats,'최종 메시 실측']]){if(!path)continue;const a=el('a','',label);a.href=path;copy.append(a,document.createTextNode(' · '));}row.append(copy);}
</script></body></html>'''


def patch_old_banner():
    old = ART / "REVIEW.html"
    if not old.is_file():
        return False
    content = old.read_text(encoding="utf-8")
    banner = '<p id="ktp-rework-banner" style="margin:0;padding:12px 24px;background:#e5e7d9;border-bottom:1px solid #c5cebd"><a href="KTP_REWORK.html" style="color:#456b5c;font-weight:650">사용자 피드백 반영 KTP/Meshy 재제작 진행</a></p>'
    if 'id="ktp-rework-banner"' in content:
        updated = re.sub(r'<p id="ktp-rework-banner"[^>]*>.*?</p>', banner, content, count=1, flags=re.S)
    else:
        updated = content.replace('<body>', '<body>\n' + banner, 1)
    if updated != content:
        old.write_text(updated, encoding="utf-8")
    return True


def main():
    models, rejected = model_data()
    integrated, capture_sets = integrated_data()
    reports = []
    for path in sorted(ART.glob("KTP_INTEGRATED_REVIEW*.md"), reverse=True):
        suffix = path.stem.removeprefix("KTP_INTEGRATED_REVIEW") or "01·02"
        reports.append({"label": "실제 통합 장면 검토 " + suffix, "path": relative_if_file(path)})
    data = {"native": native_data(), "integrated": integrated, "captureSets": capture_sets,
            "catalog": catalog_status(), "validation": validation_status(), "meshy": meshy_status(),
            "body": body_status(), "botanical": botanical_status(), "stoneWave": stone_wave_status(), "input": input_status(),
            "salvage": salvage_status(), "imugiSalvage": imugi_salvage_status(),
            "reports": [r for r in reports if r["path"]], "models": models, "rejected": rejected}
    serialized = json.dumps(data, ensure_ascii=False).replace('<', '\\u003c')
    result = PAGE.replace('__DATA__', serialized).replace('__GENERATED__', html.escape(datetime.now().strftime('%Y-%m-%d %H:%M')))
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    OUTPUT.write_text(result, encoding="utf-8")
    patched = patch_old_banner()
    print(json.dumps({"output": str(OUTPUT), "nativeFrames": sum(bool(f['image']) for i in data['native'] for f in i['frames']),
                      "integratedGroups": len(data['integrated']), "selectedViewFrames": sum(bool(f['image']) for i in data['integrated'] for v in i['views'] for f in v['frames']),
                      "captureSets": capture_sets, "catalog": data['catalog'],
                      "sourceModels": len(models), "rejectedModels": len(rejected), "oldPageBanner": patched}, ensure_ascii=False))


if __name__ == '__main__':
    main()
