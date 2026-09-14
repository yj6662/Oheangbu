"""Sync public local provenance and protected-file hashes, without judging candidates.

python -B Tools/MeshyRuns/AutoPlayerV1/sync_manifest.py [--root RUN_FOLDER]
Reads public ledgers only. Writes source_manifest.json and preservation_after.json
atomically; never writes cost_ledger, run_state, or candidate_result.
"""
from __future__ import annotations

import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import re
import tempfile

PROJECT = Path(__file__).resolve().parents[3]
DEFAULT_ROOT = PROJECT / "Art/PlayerPhase1/AutoPlayerV1"
STATUS_FIELDS = ("status", "generation", "deformation", "serialization", "weapon_grip",
                 "unity_playtest", "art_review", "corrections_used")
CONFIG_FIELDS = ("model_type", "ai_model", "should_texture", "enable_pbr", "texture_resolution",
                 "ultra_mode", "should_remesh", "topology", "target_polycount", "pose_mode",
                 "image_enhancement", "save_pre_remeshed_model", "target_formats",
                 "input_task_id", "rig_task_id", "action_id", "height_meters")


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig")) if path.is_file() else {}


def write_atomic(path, data):
    fd, temporary = tempfile.mkstemp(prefix="." + path.stem + "-", suffix=".tmp", dir=path.parent)
    try:
        with os.fdopen(fd, "w", encoding="utf-8", newline="\n") as stream:
            json.dump(data, stream, indent=2, ensure_ascii=False)
            stream.write("\n")
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary, path)
    finally:
        if os.path.exists(temporary):
            os.unlink(temporary)


def safe_string(value):
    return isinstance(value, str) and len(value) < 2048 and not re.search(
        r"https?://|data:|bearer\s+|access_token|refresh_token|api_key|secret", value, re.I)


def local_path(value, base):
    if not safe_string(value):
        return None
    path = Path(value.replace("\\", "/"))
    path = path.resolve() if path.is_absolute() else (base / path).resolve()
    try:
        relative = path.relative_to(base.resolve())
    except ValueError:
        return None
    if any(p.startswith(".") or p.lower() == "private" for p in relative.parts):
        return None
    return path


def digest(path):
    before = path.stat()
    value = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            value.update(chunk)
    after = path.stat()
    if (before.st_size, before.st_mtime_ns) != (after.st_size, after.st_mtime_ns):
        return {"sha256": None, "bytes": after.st_size, "hash_status": "CHANGED_DURING_READ"}
    return {"sha256": value.hexdigest(), "bytes": after.st_size, "hash_status": "MEASURED_LOCAL_FILE"}


def scalar(value):
    return value if value is None or isinstance(value, (bool, int, float)) or safe_string(value) else None


def recorded_downloads(record, root):
    result = []
    for item in record if isinstance(record, list) else []:
        if not isinstance(item, dict):
            continue
        path = local_path(item.get("path"), root)
        sha = item.get("sha256", "")
        if path is None or not isinstance(sha, str) or not re.fullmatch(r"[a-fA-F0-9]{64}", sha):
            continue
        result.append({"key": scalar(item.get("key")), "path": path.relative_to(root).as_posix(),
                       "sha256": sha, "bytes": scalar(item.get("bytes")), "exists": path.is_file(),
                       "hash_basis": "RECORDED_DOWNLOAD_MANIFEST_NOT_REHASHED"})
    return result


def request_record(request, root):
    name = request.get("name", "")
    result = {k: scalar(request.get(k)) for k in ("name", "kind", "task_id", "status", "settings_sha256", "consumed_credits")}
    config = request.get("config", {})
    result["source_config"] = {}
    for key in CONFIG_FIELDS:
        if key in config:
            value = config[key]
            result["source_config"][key] = [scalar(x) for x in value] if isinstance(value, list) else scalar(value)
    result["input_files"] = []
    for value, sha in request.get("inputs", {}).items():
        path = local_path(value, root)
        if path is not None and isinstance(sha, str) and re.fullmatch(r"[a-fA-F0-9]{64}", sha):
            result["input_files"].append({"path": path.relative_to(root).as_posix(), "sha256": sha})
    manifest_path = local_path("Candidates/" + name + "/Source/manifest.json", root) if re.fullmatch(r"C\d+(?:_[A-Za-z0-9_-]+)?", name) else None
    if manifest_path is not None and manifest_path.is_file():
        manifest = read(manifest_path)
        result["download_manifest"] = manifest_path.relative_to(root).as_posix()
        result["download_manifest_task_id"] = scalar(manifest.get("task_id"))
        result["manifest_task_matches_ledger"] = manifest.get("task_id") == request.get("task_id")
        result["downloads"] = recorded_downloads(manifest.get("files", []), root)
    else:
        result["download_manifest"] = None
        result["downloads"] = recorded_downloads(request.get("downloads", []), root)
        for item in result["downloads"]:
            item["hash_basis"] = "RECORDED_COST_LEDGER_NOT_REHASHED"
    return result


def preservation(root, project):
    before = read(root / "preservation_before.json")
    files = []
    for value, expected in before.items():
        path = local_path(value, project)
        item = {"path": value, "expected_sha256": expected.get("sha256"), "expected_bytes": expected.get("bytes")}
        if path is None:
            # Do not echo arbitrary paths supplied by a malformed provenance input.
            item["path"] = "[invalid protected path]"
            item["status"] = "UNVERIFIED_INVALID_PATH"
        elif not path.is_file():
            item["status"] = "MISSING"
        else:
            try:
                item.update(digest(path))
                expected_sha = str(expected.get("sha256", "")).lower()
                if item["hash_status"] != "MEASURED_LOCAL_FILE" or not re.fullmatch(r"[a-f0-9]{64}", expected_sha):
                    item["status"] = "UNVERIFIED"
                else:
                    item["status"] = "UNCHANGED" if item["sha256"] == expected_sha else "CHANGED"
            except OSError:
                item["status"] = "UNVERIFIED_READ_ERROR"
        files.append(item)
    unchanged = sum(x["status"] == "UNCHANGED" for x in files)
    status = "ALL_UNCHANGED" if files and unchanged == len(files) else "CHANGES_OR_UNVERIFIED" if files else "UNVERIFIED_NO_BASELINE"
    return {"checked_at": datetime.now(timezone.utc).isoformat(), "status": status,
            "scope": "Exact protected-file SHA256 comparison only; not candidate quality or Unity validation.",
            "checked_files": len(files), "unchanged_files": unchanged, "files": files}


def sync(root, project):
    root, project = root.resolve(), project.resolve()
    if not root.is_dir():
        raise ValueError("Existing run folder is required.")
    ledger, state = read(root / "cost_ledger.json"), read(root / "run_state.json")
    requests = [r for r in ledger.get("requests", []) if isinstance(r, dict)]
    state_candidates = {c["id"]: c for c in state.get("candidates", []) if isinstance(c, dict) and re.fullmatch(r"C\d+", str(c.get("id", "")))}
    generations = {r["name"]: r for r in requests if r.get("kind") == "character" and re.fullmatch(r"C\d+", str(r.get("name", "")))}
    candidates = sorted(set(state_candidates) | set(generations))
    outputs, pipeline = [], []
    for candidate in candidates:
        folder = root / "Candidates" / candidate
        result_path = folder / "candidate_result.json"
        candidate_result = read(result_path)
        stage = {"candidate": candidate, "candidate_result": result_path.relative_to(root).as_posix() if result_path.is_file() else None,
                 "recorded_verdict": {k: scalar(candidate_result.get(k, state_candidates.get(candidate, {}).get(k))) for k in STATUS_FIELDS},
                 "generation": None, "rigging": [], "animations": [], "local_outputs": []}
        generation = generations.get(candidate)
        generation_id = generation.get("task_id") if generation else None
        if generation:
            stage["generation"] = request_record(generation, root)
        rigs = [r for r in requests if generation_id and r.get("kind") == "rigging" and r.get("config", {}).get("input_task_id") == generation_id]
        rig_ids = {r.get("task_id") for r in rigs if r.get("task_id")}
        stage["rigging"] = [request_record(r, root) for r in rigs]
        animations = [r for r in requests if r.get("kind") == "animation" and r.get("config", {}).get("rig_task_id") in rig_ids]
        stage["animations"] = [request_record(r, root) for r in animations]
        major = [p for p in folder.glob("*.blend") if not p.name.lower().startswith(("before", "backup"))]
        if result_path.is_file():
            major.append(result_path)
        major.extend(p for p in folder.rglob("*") if p.is_file() and p.suffix.lower() in {".mp4", ".webm"})
        for path in sorted(set(major)):
            if local_path(str(path), root) is None:
                continue
            relative = path.relative_to(root).as_posix()
            record = {"candidate": candidate, "path": relative,
                      "kind": "blender_source" if path.suffix.lower() == ".blend" else "candidate_result" if path == result_path else "review_video"}
            try:
                record.update(digest(path))
            except OSError:
                record["hash_status"] = "UNVERIFIED_READ_ERROR"
            outputs.append(record)
            stage["local_outputs"].append(relative)
        pipeline.append(stage)
    # Re-read immediately before replacement to retain any inputs/references added during hashing.
    manifest = read(root / "source_manifest.json")
    manifest.update(outputs=outputs, candidate_pipeline=pipeline, synced_at=datetime.now(timezone.utc).isoformat(),
                    provenance_scope="Task lineage follows cost_ledger config input_task_id/rig_task_id. Download digests remain manifest records; local major blends/results/videos are freshly hashed. Candidate verdicts are copied, never awarded.")
    after = preservation(root, project)
    write_atomic(root / "source_manifest.json", manifest)
    write_atomic(root / "preservation_after.json", after)
    return {"candidates": candidates, "outputs_hashed": len(outputs), "preservation": after["status"],
            "protected_files": after["checked_files"], "verdict_changed": False}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=DEFAULT_ROOT)
    parser.add_argument("--project-root", type=Path, default=PROJECT)
    args = parser.parse_args()
    print(json.dumps(sync(args.root, args.project_root), ensure_ascii=False))
