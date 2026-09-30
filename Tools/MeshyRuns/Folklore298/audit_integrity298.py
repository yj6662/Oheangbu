"""Read-only final #298 provenance refresh. Writes only Analysis/final-integrity-latest.json.

No Unity, network, paid generation, decoding, model mutation or manifest write.
"""
from datetime import datetime, timezone
import hashlib
import json
import math
import re
import shutil
from pathlib import Path

import yaml
import encode_films298 as films

ROOT = Path(__file__).resolve().parents[3]
BASE = ROOT / "Art/Characters/Folklore298"
UNITY = ROOT / "Oheangbu"
ASSET = UNITY / "Assets/_Project/Art/Characters/Folklore298"
CACHE = {}
CHECKS = []


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def resolve(path):
    p = Path(str(path).replace("\\", "/"))
    return (p if p.is_absolute() else ROOT / p).resolve()


def identity(path):
    path = resolve(path)
    if path not in CACHE:
        value = {"path": path.relative_to(ROOT).as_posix(), "exists": path.is_file()}
        if path.is_file():
            value.update(bytes=path.stat().st_size, sha256=films.sha(path))
        CACHE[path] = value
    return CACHE[path]


def check(label, passed, detail=None):
    CHECKS.append({"id": label, "pass": bool(passed), "detail": detail})
    return bool(passed)


def present(label, path):
    value = identity(path)
    check(label, value["exists"] and value.get("bytes", 0) > 0, value)
    return value


def intake(path):
    path = resolve(path)
    ledger = path.parent / "downloads.json"
    rows = read(ledger)
    entries = [r for r in rows if r["path"] == path.name]
    current = identity(path)
    passed = len(entries) == 1 and current.get("sha256") == entries[0]["sha256"] and current.get("bytes") == entries[0]["bytes"]
    check("intake:" + current["path"], passed, {"downloadReceipt": identity(ledger), "file": current})
    return passed


def imported(source, model_id, category):
    source = resolve(source)
    return ASSET / category / model_id / (source.parent.name + "_" + source.name)


def prefab_structure(row, prefab_path, model_path, readability):
    """Validate current serialized bindings, without pretending to re-bake a Unity mesh."""
    text = prefab_path.read_text(encoding="utf-8-sig")
    chunks = re.split(r"^--- !u!\d+ &(-?\d+)\s*$", text, flags=re.M)
    objects = {int(chunks[i]): yaml.safe_load(chunks[i + 1]) for i in range(1, len(chunks), 2)}
    transforms = {i: d["Transform"] for i, d in objects.items() if "Transform" in d}
    skins = {i: d["SkinnedMeshRenderer"] for i, d in objects.items() if "SkinnedMeshRenderer" in d}
    components = [d["MonoBehaviour"] for d in objects.values() if "MonoBehaviour" in d]
    model_guid = yaml.safe_load(Path(str(model_path) + ".meta").read_text(encoding="utf-8"))["guid"]
    ident = row["id"]
    finite = all(math.isfinite(float(v)) for t in transforms.values() for field in ("m_LocalPosition", "m_LocalRotation", "m_LocalScale") for v in t[field].values())
    check(ident + ":prefab-finite-transforms", bool(transforms) and finite)
    check(ident + ":prefab-selected-model-binding", bool(skins) and all(s["m_Mesh"].get("guid") == model_guid for s in skins.values()))
    check(ident + ":prefab-bone-references", bool(skins) and all(s["m_Bones"] and all(b["fileID"] in transforms for b in s["m_Bones"]) and s["m_RootBone"]["fileID"] in transforms for s in skins.values()))
    readings = [c for c in components if c.get("m_EditorClassIdentifier", "").endswith(".FolkloreReadability298")]
    bound_components = [c for c in components if c.get("m_EditorClassIdentifier", "").endswith(".FolkloreCullingBounds298")]
    recorded = next(r for r in readability["prefabs"] if r["id"] == ident)
    valid_reading = len(readings) == 1 and abs(readings[0]["DiffuseFill"] - .3) < 1e-6 and {v["fileID"] for v in readings[0]["Targets"]} == set(skins)
    check(ident + ":current-readability-binding", valid_reading and recorded["renderers"] == len(skins) and recorded["materialsUnchanged"] and recorded["boundsUnchanged"] and readability["globalsUnchanged"])
    bounds_path = BASE / "Analysis" / ("bounds-" + ident + ".json")
    bound_receipt = read(bounds_path)
    entries = bound_components[0]["Entries"] if len(bound_components) == 1 else []
    bound_ok = len(entries) == len(skins) == len(bound_receipt["localRadii"])
    if bound_ok:
        for index, entry in enumerate(entries):
            expected = bound_receipt["localRadii"][index]
            actual = entry["LocalBounds"]
            skin = skins.get(entry["Skin"]["fileID"])
            bound_ok &= skin is not None and skin["m_AABB"] == actual and all(abs(v) < 1e-6 for v in actual["m_Center"].values()) and all(abs(v - expected) < 1e-5 for v in actual["m_Extent"].values())
    check(ident + ":current-culling-bounds-binding", bound_ok, {"receipt": identity(bounds_path), "radii": bound_receipt["localRadii"]})
    material_guids = {}
    for meta in (ASSET / "Materials" / ident).glob("*.meta"):
        material_guids[yaml.safe_load(meta.read_text(encoding="utf-8"))["guid"]] = Path(str(meta)[:-5])
    material_refs = [r for skin in skins.values() for r in skin["m_Materials"]]
    material_files = [identity(material_guids[r["guid"]]) for r in material_refs if r.get("guid") in material_guids]
    check(ident + ":prefab-material-references", bool(material_refs) and len(material_files) == len(material_refs) and all(f["exists"] and f["bytes"] > 0 for f in material_files))
    visuals = [t for t in transforms.values() if objects[t["m_GameObject"]["fileID"]]["GameObject"]["m_Name"] == "Visual"]
    check(ident + ":prefab-uniform-visual-scale", len(visuals) == 1 and min(visuals[0]["m_LocalScale"].values()) > 0 and max(visuals[0]["m_LocalScale"].values()) - min(visuals[0]["m_LocalScale"].values()) < 1e-5)
    return {"currentSerializedBindingsChecked": True, "skins": len(skins), "boneReferences": sum(len(s["m_Bones"]) for s in skins.values()), "visualTransform": visuals[0] if len(visuals) == 1 else visuals, "materials": material_files, "readabilityEvidence": identity(BASE / "Analysis/readability.json"), "boundsEvidence": identity(bounds_path), "scope": "Current model GUID, non-null local bone references, finite TRS, uniform scale, material files and bound/readability component fields. Not a new physical mesh bake or a byte diff to the unavailable old prefab contents."}


def main():
    manifest_path = BASE / "import-manifest.json"
    previous = read(BASE / "Analysis/final-integrity.json")
    manifest = read(manifest_path)
    baseline = read(BASE / "Analysis/protected-baseline.json")
    ledger = read(BASE / "meshy-ledger.json")
    validation = read(BASE / "Import/validation.json")
    readability = read(BASE / "Analysis/readability.json")
    watched = [manifest_path, BASE / "meshy-ledger.json", BASE / "Analysis/protected-baseline.json", BASE / "Import/validation.json"]
    inputs = {identity(p)["path"]: identity(p)["sha256"] for p in watched}
    protected = []
    for old in baseline:
        current = identity(old["path"])
        same = current.get("sha256") == old["sha256"] and current.get("bytes") == old["bytes"]
        check("protected:" + current["path"], same, current)
        protected.append({"baseline": old, "actual": current, "unchanged": same})
    check("protected-count", len(protected) == 10)
    check("models-count", len(manifest["rows"]) == 8 and len({r["id"] for r in manifest["rows"]}) == 8)
    models = []
    for row in manifest["rows"]:
        ident = row["id"]
        src = present(ident + ":source-model", row["model"])
        imp_path = imported(row["model"], ident, "Models")
        imp = present(ident + ":imported-model", imp_path)
        prefab = present(ident + ":prefab", ASSET / "Prefabs" / ("PF_" + ident + ".prefab"))
        check(ident + ":model-copy-sha", src.get("sha256") == imp.get("sha256"))
        old_model = next(m for m in previous["models"] if m["id"] == ident)
        prefab_change = {"priorSha256": old_model["prefab"]["sha256"], "currentSha256": prefab.get("sha256"), "byteIdenticalToPrior": prefab.get("sha256") == old_model["prefab"]["sha256"], "interpretation": "Candidate prefab updates are authorized. Current readability and culling bindings are checked separately; these prefabs are not protected originals."}
        current_prefab = prefab_structure(row, resolve(prefab["path"]), imp_path, readability)
        check(ident + ":model-unchanged-since-prior-audit", imp.get("sha256") == old_model["importedModel"]["sha256"])
        source_folder = resolve(row["textures"][0]["baseColor"]).parent
        static_source = source_folder / "source.glb"
        intake(static_source)
        task = next(t for t in ledger["tasks"] if t["name"] == source_folder.name and t["kind"] == "model")
        check(ident + ":selected-model-task", task["status"] == "SUCCEEDED", {"name": task["name"], "kind": task["kind"], "status": task["status"]})
        reference = identity(task["reference"])
        check(ident + ":reference-sha", reference.get("sha256") == task["reference_sha256"], reference)
        textures = []
        for texture in row["textures"]:
            for slot in ("baseColor", "normal", "roughness", "metallic"):
                if texture.get(slot):
                    item = present(ident + ":texture:" + slot, texture[slot]); intake(texture[slot]); textures.append({"slot": slot, "file": item})
        clips = []
        for clip in row["clips"]:
            source = clip.get("path") or row["model"]
            imported_path = imported(source, ident, "Models" if source == row["model"] else "Animations")
            source_info = present(ident + ":clip-source:" + clip["role"], source)
            imported_info = present(ident + ":clip-import:" + clip["role"], imported_path)
            check(ident + ":clip-copy:" + clip["role"], source_info.get("sha256") == imported_info.get("sha256"))
            meta = yaml.safe_load(Path(str(imported_path) + ".meta").read_text(encoding="utf-8"))["ModelImporter"]
            imported_clips = meta["animations"]["clipAnimations"]
            matches = [c for c in imported_clips if c["name"] == clip["name"] or c["name"].endswith("|" + clip["name"])]
            requested_loop = bool(clip.get("loop") or clip["role"] in ("idle", "walk"))
            valid = len(matches) == 1 and matches[0]["lastFrame"] > matches[0]["firstFrame"] and bool(matches[0]["loopTime"]) == requested_loop
            valid = valid and meta["animationType"] == (3 if row["rigType"] == "Humanoid" else 2)
            valid = valid and meta["animations"]["animationCompression"] == 0 and meta["importAnimation"] == 1
            check(ident + ":clip-meta:" + clip["role"], valid, {"requested": clip["name"], "matches": len(matches), "actual": matches[0]["name"] if matches else None})
            clips.append({"role": clip["role"], "name": clip["name"], "source": source_info, "imported": imported_info,
                          "meta": identity(str(imported_path) + ".meta"), "match": matches[0] if len(matches) == 1 else matches})
            if "Source/" in source.replace("\\", "/"):
                intake(source)
        rig = None
        if row["rigType"] == "Generic":
            folder = resolve(row["model"]).parent
            rig = read(resolve(row["sourceEvidence"]))
            trip = read(folder / "roundtrip.json")
            review = read(resolve(row["visualReview"]))
            source_hash = identity(static_source)["sha256"]
            check(ident + ":rig-source-sha", rig["sourceSha256"] == source_hash and rig["sourceHashUnchanged"])
            check(ident + ":rig-output-sha", rig["outputSha256"]["creature.fbx"] == src["sha256"])
            check(ident + ":rig-blend-sha", rig["outputSha256"]["candidate.blend"] == identity(folder / "candidate.blend")["sha256"])
            review_output = review.get("outputSha256", {}).get("creature.fbx", review.get("fbxSha256"))
            check(ident + ":visual-review-output-sha", review_output == src["sha256"] and review["sourceSha256"] == source_hash)
            check(ident + ":rig-roundtrip", trip["status"] == "TECHNICAL_ROUNDTRIP_PASS" and all(m["triangleMaterialUvSignatureEqual"] for m in trip["meshes"]))
            check(ident + ":rig-source-approval", read(folder / "approval-used.json")["sourceSha256"] == source_hash)
            for key in ("rigConfig", "visualReview", "sourceEvidence"):
                present(ident + ":reference:" + key, row[key])
            rig = {"report": identity(row["sourceEvidence"]), "roundtrip": identity(folder / "roundtrip.json"), "review": identity(row["visualReview"]), "source": identity(static_source), "cleanup": rig.get("cleanup")}
        else:
            intake(row["model"])
        engine = next(v for v in validation["rows"] if v["id"] == ident)
        axis = "y" if row.get("targetHeight") else row.get("lengthAxis", "z")
        expected_size = row.get("targetHeight") or row["targetLength"]
        measured_size = engine["physicalBounds"]["m_Extent"][axis] * 2
        check(ident + ":recorded-unity-size", engine["status"] == "PASS" and abs(measured_size - expected_size) < .0001,
              {"scope": "Historical Unity import validation for the same selected FBX and target size. Current prefab byte identity is not required or claimed; no new Unity physical measurement.", "axis": axis, "expected": expected_size, "measured": measured_size})
        pose_path = BASE / "Captures" / ident / "pose-review.json"
        pose = read(pose_path)
        pose_images = [present(ident + ":studio-image:" + image, pose_path.parent / image) for image in pose["images"]]
        check(ident + ":studio-recorded-culling", pose["invalidVertices"] == 0 and pose["escapedBounds"] == 0 and pose["cullingBoundsOutsideVertices"] == 0,
              {"samples": pose["samples"], "scope": "Existing studio evidence, not live world performance"})
        film_path = BASE / "Captures" / ident / "film-receipt.json"
        film, frames, film_roles, duration = films.validate_receipt(film_path)
        video = read(film_path.parent / "video-receipt.json")
        check(ident + ":film-source-sha", film["sourceModelSha256"] == src["sha256"])
        check(ident + ":film-current-clips", all(any(c["role"] == f["role"] and (f["name"] == c["name"] or f["name"].endswith("|" + c["name"])) and f["assetSha256"] == c["imported"]["sha256"] for c in clips) for f in film["clips"]))
        check(ident + ":film-24fps", film.get("samplingFps") == 24 and video["outputFps"] == 24)
        check(ident + ":film-video-receipt-sha", identity(film_path)["sha256"] == video["receiptSha256"])
        for output in video["outputs"]:
            actual = identity(output["path"])
            copy = identity(film_path.parent / ("animation." + output["format"]))
            check(ident + ":video-sha:" + output["format"], actual.get("sha256") == output["sha256"] == copy.get("sha256"))
            check(ident + ":video-recorded-decode:" + output["format"], output["decode"]["dimensions_seen"] == [[512, 576]] and abs(output["decode"]["presentation_duration_seconds"] - duration) < 1 / 24 + .003)
        models.append({"id": ident, "currentModel": src, "importedModel": imp, "prefab": prefab, "selectedSource": identity(static_source),
                       "sourceReference": reference, "clips": clips, "textures": textures, "rig": rig, "prefabChange": prefab_change, "currentPrefabBindings": current_prefab, "recordedUnitySize": {"expected": expected_size, "measured": measured_size, "validationUtc": validation["timestamp"], "scope": "Historical import receipt, not a new measurement"},
                       "studio": {"receipt": identity(pose_path), "images": pose_images, "samples": pose["samples"]},
                       "film": {"receipt": identity(film_path), "videoReceipt": identity(film_path.parent / "video-receipt.json"), "frames": len(frames), "duration": duration,
                                "captureManifestSha256": film["manifestSha256"], "currentManifestSha256": identity(manifest_path)["sha256"],
                                "reuseBasis": "Current source-model and imported clip SHA/name matches; target size matches the recorded import. Films are earlier studio animation evidence. Later prefab readability SH changes are not represented by these movies; current prefab or full manifest byte identity is not claimed."}})
    # Audio has progressed since the old report. Audit only the current local hash chain here.
    audio_path = BASE / "Audio/validated-intake.json"
    audio = read(audio_path)
    for item in audio["clips"]:
        for file_key, hash_key in [("file", "sha256"), ("original_file", "original_sha256"), ("request_file", "request_sha256"), ("processing_file", "processing_sha256")]:
            check("audio:" + item["id"] + ":" + file_key, identity(BASE / "Audio" / item[file_key]).get("sha256") == item[hash_key])
    for path, before in list(CACHE.items()):
        if before["exists"]:
            check("stable-during-audit:" + before["path"], path.is_file() and path.stat().st_size == before["bytes"] and films.sha(path) == before["sha256"])
    failed = [c for c in CHECKS if not c["pass"]]
    current_manifest_hash = identity(manifest_path)["sha256"]
    report = {"schema": 2, "timestampUtc": datetime.now(timezone.utc).isoformat(), "scope": __doc__.strip(), "passed": not failed,
              "counts": {"pass": len(CHECKS) - len(failed), "fail": len(failed), "protected": 10, "models": len(models), "clipRoleMappings": sum(len(m["clips"]) for m in models),
                         "filmActors": len(models), "filmPngs": sum(m["film"]["frames"] for m in models), "movieFiles": len(models) * 2, "audioOriginals": len(audio["clips"])},
              "manifest": {"current": identity(manifest_path), "previousAuditSha256": previous["inputs"]["Art/Characters/Folklore298/import-manifest.json"],
                           "byteIdenticalToPriorAudit": current_manifest_hash == previous["inputs"]["Art/Characters/Folklore298/import-manifest.json"],
                           "interpretation": "Current manifest is intentionally updated. This audit proves selected assets and references now, not immutable manifest bytes since the earlier coordinates.",
                           "currentImugiPlacement": next({k: v for k, v in r.items() if k in ("placeId", "realm", "preferredFeet", "placementEvidence")} for r in manifest["rows"] if r["id"] == "imugi"),
                           "currentAgwiRange": next(r.get("range") for r in manifest["rows"] if r["id"] == "agwi")},
              "inputs": inputs, "protected": protected, "models": models,
              "audio": {"intake": identity(audio_path), "originals": len(audio["clips"]), "prepared": len(audio["clips"]), "scope": "File SHA-chain only; no decode/listening/Unity call repeated here"},
              "failures": failed, "checks": CHECKS,
              "notCovered": ["Ongoing final live Unity/runtime checks", "Fresh physical size or culling measurement", "Continuous movie visual viewing", "Final user art approval", "Audio listening or actual DSP output"]}
    target = BASE / "Analysis/final-integrity-latest.json"
    if target.exists():
        archive = BASE / "History" / ("integrity-" + datetime.now(timezone.utc).strftime("%Y%m%d-%H%M%S-%f"))
        archive.mkdir(parents=True, exist_ok=False)
        shutil.copy2(target, archive / target.name)
    target.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"path": str(target.relative_to(ROOT)), "passed": report["passed"], "counts": report["counts"], "manifestChangedFromPrior": not report["manifest"]["byteIdenticalToPriorAudit"], "failures": failed}, ensure_ascii=False))


if __name__ == "__main__":
    main()
