"""Encode actual Unity Film() receipts into silent MP4/WebM studio previews.

No Unity invocation or source-model changes. New captures target 24 sampled poses
per second, capped at 300 per role. Actual clip duration is retained. Legacy
twelve-pose receipts remain readable; no interpolated poses are invented.
Run after CompactFolklore298.Film("all" or id).
"""
import argparse
from datetime import datetime, timezone
import hashlib
import json
import math
from pathlib import Path
import shutil
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[3]
BASE = ROOT / "Art/Characters/Folklore298"
ROLES = ["idle", "walk", "attack", "hit", "stun", "death"]
sys.path.insert(0, str(ROOT / "Tools/Blender/AutoPlayerV1"))
from verify_videos import parse_decode, video_sample_table


def sha(path):
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def dump(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def ffmpeg_path(explicit):
    if explicit:
        found = Path(explicit)
    else:
        command = shutil.which("ffmpeg")
        if command:
            found = Path(command)
        else:
            import imageio_ffmpeg
            found = Path(imageio_ffmpeg.get_ffmpeg_exe())
    if not found.is_file():
        raise FileNotFoundError(found)
    return found.resolve()


def run(command):
    result = subprocess.run([str(x) for x in command], capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=300)
    if result.returncode:
        raise RuntimeError("Local ffmpeg failed:\n" + result.stderr[-6000:])
    return result


def validate_receipt(path):
    receipt = json.loads(path.read_text(encoding="utf-8-sig"))
    if receipt["status"] != "STUDIO_FRAMES_CAPTURED_NOT_WORLD_PLAY_VERIFIED":
        raise ValueError("Incomplete film receipt: " + str(path))
    if not receipt["sourceMeshesUnchanged"] or not receipt["restTrsRestoredPerSample"] or receipt["rootMotion"]:
        raise ValueError("Capture isolation/restoration failed")
    if [r["role"] for r in receipt["clips"]] != ROLES or len(receipt["frames"]) != sum(c["frameCount"] for c in receipt["clips"]):
        raise ValueError("Expected six ordered roles and their exact recorded frame counts")
    duration = 0.0
    files = []
    role_stats = []
    for clip in receipt["clips"]:
        start = clip["firstFrame"]
        selected = receipt["frames"][start:start + clip["frameCount"]]
        sampling_fps = receipt.get("samplingFps", 0)
        expected_count = min(receipt["maximumSamplesPerRole"], max(2, math.ceil(clip["seconds"] * sampling_fps))) if sampling_fps else 12
        if len(selected) != expected_count or clip["seconds"] <= 0 or abs(clip["timelineStart"] - duration) > 1e-4:
            raise ValueError("Invalid clip duration/timeline")
        expected_phases = [i / (expected_count if clip["loop"] else expected_count - 1) for i in range(expected_count)]
        for index, (frame, phase) in enumerate(zip(selected, expected_phases)):
            image = (BASE / frame["path"]).resolve()
            if not image.is_relative_to(BASE.resolve()) or image.suffix.lower() != ".png":
                raise ValueError("Frame outside Folklore298 output")
            if frame["index"] != start + index or frame["sample"] != index or frame["role"] != clip["role"]:
                raise ValueError("Frame order/role mismatch")
            if abs(frame["phase"] - phase) > 1e-5 or abs(frame["clipTime"] - clip["seconds"] * phase) > 1e-4:
                raise ValueError("Sample is not at the recorded clip time")
            if not math.isfinite(frame["holdSeconds"]) or abs(frame["holdSeconds"] * expected_count - clip["seconds"]) > 1e-4:
                raise ValueError("Hold duration does not preserve clip duration")
            if sha(image) != frame["sha256"]:
                raise ValueError("Frame changed: " + str(image))
            header = image.read_bytes()[:24]
            if header[:8] != b"\x89PNG\r\n\x1a\n" or int.from_bytes(header[16:20], "big") != 512 or int.from_bytes(header[20:24], "big") != 512:
                raise ValueError("Expected 512px studio PNG")
            files.append((image, frame["holdSeconds"]))
        role_stats.append({"role": clip["role"], "clip": clip["name"], "seconds": clip["seconds"], "capturedPoses": expected_count,
                           "actualSamplesPerSecond": expected_count / clip["seconds"], "uniqueCapturedPngs": len({f["sha256"] for f in selected})})
        duration += clip["seconds"]
    if abs(receipt["totalSeconds"] - duration) > 1e-4:
        raise ValueError("Total duration differs from source clips")
    return receipt, files, role_stats, duration


def filter_graph(receipt):
    # Captions occupy a separate 64px band, never painted over the model.
    font = "C\\:/Windows/Fonts/arial.ttf"
    graph = ["fps=" + str(receipt.get("samplingFps") or 30), "pad=512:576:0:0:color=0x25282b",
             f"drawtext=fontfile='{font}':text='{receipt['id'].upper()} - STUDIO':fontcolor=white:fontsize=18:x=12:y=520"]
    for clip in receipt["clips"]:
        start = clip["timelineStart"]
        end = start + clip["seconds"]
        text = f"{clip['role'].upper()}   {clip['seconds']:.3f} s"
        graph.append(f"drawtext=fontfile='{font}':text='{text}':fontcolor=0xb9c2ca:fontsize=17:x=12:y=548:enable='gte(t,{start:.9f})*lt(t,{end:.9f})'")
    return ",".join(graph)


def encode(path, ffmpeg, webm=True):
    receipt, frames, role_stats, seconds = validate_receipt(path)
    output_dir = frames[0][0].parent.parent
    concat = output_dir / "frames.ffconcat"
    lines = ["ffconcat version 1.0"]
    for image, hold in frames:
        relative = image.relative_to(output_dir).as_posix()
        if "'" in relative:
            raise ValueError("Unexpected apostrophe in generated frame name")
        lines += [f"file '{relative}'", "option framerate 1000", f"duration {hold:.12f}"]
    lines += [f"file '{frames[-1][0].relative_to(output_dir).as_posix()}'", "option framerate 1000"]
    concat.write_text("\n".join(lines) + "\n", encoding="utf-8")
    versions = run([ffmpeg, "-version"]).stdout.splitlines()[0]
    receipt_sha = sha(path)
    fps = receipt.get("samplingFps") or 30
    results = []
    for extension in (["mp4", "webm"] if webm else ["mp4"]):
        video = output_dir / ("animation." + extension)
        command = [ffmpeg, "-nostdin", "-hide_banner", "-loglevel", "warning", "-n", "-f", "concat", "-safe", "0", "-i", concat,
                   "-vf", filter_graph(receipt), "-t", f"{seconds:.9f}", "-an", "-sn", "-dn", "-threads", "2", "-pix_fmt", "yuv420p"]
        if extension == "mp4":
            command += ["-c:v", "libx264", "-preset", "fast", "-crf", "18", "-movflags", "+faststart"]
        else:
            command += ["-c:v", "libvpx-vp9", "-crf", "32", "-b:v", "0", "-deadline", "good", "-cpu-used", "4", "-row-mt", "1"]
        command += [video]
        if video.exists():
            raise FileExistsError("Preserve completed encoding; use a new Film capture run: " + str(video))
        run(command)
        decode = run([ffmpeg, "-nostdin", "-hide_banner", "-loglevel", "info", "-xerror", "-i", video, "-vf", "showinfo", "-fps_mode", "passthrough", "-f", "null", "-"])
        (output_dir / (extension + "-decode.txt")).write_text(decode.stderr, encoding="utf-8")
        decoded, internal = parse_decode(decode.stderr)
        if decoded["dimensions_seen"] != [[512, 576]] or abs(decoded["presentation_duration_seconds"] - seconds) > 1 / fps + .003:
            raise ValueError("Decoded duration/dimensions do not match source receipt")
        table = video_sample_table(video.read_bytes()) if extension == "mp4" else None
        if table and (table["sample_count"] != decoded["decoded_frames"] or abs(table["duration_seconds"] - seconds) > 1 / fps + .003):
            raise ValueError("MP4 sample table mismatch")
        results.append({"path": video.relative_to(ROOT).as_posix(), "sha256": sha(video), "format": extension,
                        "decode": decoded, "mp4SampleTable": table, "command": [str(c) for c in command]})
    if sha(path) != receipt_sha:
        raise ValueError("Capture receipt changed during encoding")
    record = {"status": "ENCODE_AND_FULL_DECODE_PASS_NOT_WORLD_PLAY_VALIDATION", "id": receipt["id"], "utc": datetime.now(timezone.utc).isoformat(),
              "receipt": path.relative_to(ROOT).as_posix(), "receiptSha256": receipt_sha, "inputFrames": len(frames),
              "sourceClipSeconds": seconds, "outputFps": fps,
              "sampleMethod": "Uniform actual clip samples, target 24Hz and at most 300 per role. Nonloops include final pose. Clip duration retained; no invented interpolation." if receipt.get("samplingFps") else "Legacy twelve-pose sampling per clip, repeated into 30fps output.",
              "audio": False, "scope": receipt["scope"], "roles": role_stats, "ffmpeg": str(ffmpeg), "ffmpegVersion": versions, "outputs": results,
              "warnings": [s["role"] + ": all sampled PNGs identical; inspect binding or intentionally static clip" for s in role_stats if s["uniqueCapturedPngs"] < 2]}
    dump(output_dir / "video-receipt.json", record)
    # Run-local originals remain as history; only latest convenience copies update.
    for result in results:
        shutil.copyfile(ROOT / result["path"], path.parent / ("animation." + result["format"]))
    dump(path.parent / "video-receipt.json", record)
    print(json.dumps({"id": receipt["id"], "status": record["status"], "seconds": seconds, "warnings": record["warnings"]}), flush=True)
    return record


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("id", nargs="?", default="all")
    parser.add_argument("--ffmpeg")
    parser.add_argument("--mp4-only", action="store_true")
    parser.add_argument("--validate-only", action="store_true", help="Check existing PNG receipts, no encoding")
    args = parser.parse_args()
    manifest = json.loads((BASE / "import-manifest.json").read_text(encoding="utf-8-sig"))
    ids = [r["id"] for r in manifest["rows"] if args.id == "all" or r["id"] == args.id]
    if not ids:
        raise ValueError("Unknown model: " + args.id)
    ffmpeg = None if args.validate_only else ffmpeg_path(args.ffmpeg)
    for ident in ids:
        path = BASE / "Captures" / ident / "film-receipt.json"
        if args.validate_only:
            _, frames, stats, seconds = validate_receipt(path)
            print(json.dumps({"id": ident, "pngs": len(frames), "seconds": seconds, "roles": stats}))
        else:
            encode(path, ffmpeg, not args.mp4_only)


if __name__ == "__main__":
    main()
