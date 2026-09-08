"""Encode actual Unity Play Mode PNG frames; verify MP4 by full decode and sample tables."""
import argparse
import importlib.util
import json
from pathlib import Path
import subprocess
from PIL import Image


def encode(folder):
    folder = Path(folder).resolve()
    meta = json.loads((folder / "capture.json").read_text(encoding="utf-8"))
    if meta["status"] != "COMPLETE":
        raise ValueError("Capture is not complete")
    count = int(meta["frameCount"])
    files = sorted(folder.glob("frame_*.png"))
    if [p.name for p in files] != [f"frame_{i:06d}.png" for i in range(count)]:
        raise ValueError("Noncontiguous or unexpected frame files")
    if (meta["width"], meta["height"], meta["fps"]) != (1280, 720, 30):
        raise ValueError("Expected 1280x720 at 30fps")
    source = Path(__file__).resolve().parents[2] / "Blender/AutoPlayerV1/verify_videos.py"
    spec = importlib.util.spec_from_file_location("verify_videos", source)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    video = folder / (folder.name + ".mp4")
    command = [module.DEFAULT_FFMPEG, "-nostdin", "-hide_banner", "-loglevel", "error", "-y",
               "-framerate", "30", "-start_number", "0", "-i", str(folder / "frame_%06d.png"),
               "-frames:v", str(count), "-c:v", "libx264", "-preset", "fast", "-crf", "18",
               "-pix_fmt", "yuv420p", "-movflags", "+faststart", str(video)]
    subprocess.run(command, check=True)
    row = module.validate_job({"label": folder.name, "mp4_path": str(video), "frame_count": count,
                               "fps": 30, "view": meta["camera"], "action": meta["mode"]}, folder, module.DEFAULT_FFMPEG)
    image = Image.open(files[0]).convert("RGB")
    colors = image.getcolors(image.width * image.height) or []
    content_pixels = image.width * image.height - max((count for count, _ in colors), default=0)
    content_present = content_pixels > 1000
    report = {"status": "TECHNICAL_PASS" if row["status"] == "PASS" and content_present else "FAILED_CAPTURE",
              "first_frame_nonbackground_pixels": content_pixels,
              "content_presence_is_not_visual_quality": True, "source": "actual Unity Play Mode camera render requests",
              "art_quality": "UNVERIFIED", "capture_timing_is_not_gameplay_performance": True, "video": row}
    (folder / "video_validation.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    return report


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("folders", nargs="+")
    args = parser.parse_args()
    for folder in args.folders:
        result = encode(folder)
        print(json.dumps({"folder": folder, "status": result["status"]}))
