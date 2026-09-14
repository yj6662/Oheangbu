"""Assemble paired Unity captures without changing or synthesizing the gameplay frames."""
import argparse
import json
import subprocess
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument("--run", required=True, type=Path)
parser.add_argument("--output", required=True, type=Path)
parser.add_argument("--ffmpeg", required=True, type=Path)
args = parser.parse_args()
summary = json.loads((args.run / "summary.json").read_text(encoding="utf-8-sig"))
if summary["status"] != "COMPLETE" or not summary["restored"]:
    raise SystemExit("Use a completed replay with restored input and camera state")
count = summary["capturedPairs"]
if count < 1:
    raise SystemExit("No captured pairs")
for view in ("play", "observer"):
    for i in range(count):
        if not (args.run / view / f"frame_{i:05d}.png").is_file():
            raise SystemExit(f"Missing {view} frame {i}")
fps = summary["videoFps"]
font = "C\\:/Windows/Fonts/arial.ttf"
style = f"fontfile='{font}':fontcolor=white:fontsize=30:box=1:boxcolor=black@0.65:boxborderw=12:x=24:y=24"
observer_label = "WORLD BODY - INK HIDDEN FOR REVIEW" if summary.get("observerNote") else "WORLD BODY - SAME FRAME"
graph = (f"[0:v]drawtext={style}:text='PLAY CAMERA'[play];"
         f"[1:v]drawtext={style}:text='{observer_label}'[body];"
         "[play][body]hstack=inputs=2,format=yuv420p[out]")
args.output.parent.mkdir(parents=True, exist_ok=True)
subprocess.run([
    str(args.ffmpeg), "-hide_banner", "-loglevel", "warning", "-y",
    "-framerate", str(fps), "-i", str(args.run / "play" / "frame_%05d.png"),
    "-framerate", str(fps), "-i", str(args.run / "observer" / "frame_%05d.png"),
    "-filter_complex", graph, "-map", "[out]", "-frames:v", str(count),
    "-c:v", "libx264", "-crf", "19", "-preset", "medium", "-movflags", "+faststart", str(args.output)
], check=True)
# A separate subtitle track makes the actual replay phase available without obscuring hands.
records = [json.loads(line) for line in (args.run / "frames.jsonl").read_text(encoding="utf-8-sig").splitlines()]
def stamp(t):
    ms = round(t * 1000)
    return f"{ms // 3600000:02}:{ms // 60000 % 60:02}:{ms // 1000 % 60:02},{ms % 1000:03}"
segments = []
for i, record in enumerate(records):
    phase = record["phase"]
    if not segments or segments[-1][2] != phase:
        segments.append([i, i + 1, phase])
    else:
        segments[-1][1] = i + 1
subtitle = "\n\n".join(f"{i+1}\n{stamp(a/summary['inputFps'])} --> {stamp(b/summary['inputFps'])}\n{phase}"
                          for i, (a, b, phase) in enumerate(segments)) + "\n"
args.output.with_suffix(".srt").write_text(subtitle, encoding="utf-8")
manifest = {"video": str(args.output.resolve()), "source": str(args.run.resolve()), "paired_frames": count,
            "width": 3840, "height": 1080, "fps": fps, "gameplay_changed": False,
            "observer": summary.get("observerNote", "World body visible; near rig excluded for external review."),
            "clock": "Recorded Unity state, presented at replay logical time; raw input timestamps remain actual unscaled time.",
            "replay_summary": summary}
args.output.with_suffix(".json").write_text(json.dumps(manifest, indent=2, ensure_ascii=False), encoding="utf-8")
print(args.output)
