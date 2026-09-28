"""Run the Journey build twice with its explicit, isolated headless smoke entry."""
import json
import subprocess
import sys
from pathlib import Path

exe = Path(sys.argv[1]).resolve(strict=True)
if exe.name != "PineRestJourney.exe":
    raise ValueError("Expected the Journey player executable")
out = exe.parent / "Smoke"
out.mkdir(exist_ok=True)
for label in ("first", "continue"):
    report = out / (label + ".json")
    if report.exists():
        raise FileExistsError(f"Keep prior evidence; choose a fresh build: {report}")
    args = [str(exe), "-batchmode", "-nographics", "-logFile", str(out / (label + ".log")),
            "-journey-smoke-report", str(report)]
    if label == "continue":
        args.append("-journey-smoke-continue")
    process = subprocess.Popen(args, cwd=exe.parent, creationflags=subprocess.CREATE_NO_WINDOW)
    print(f"{label}: launched owned process {process.pid}", flush=True)
    try:
        code = process.wait(timeout=120)
    except subprocess.TimeoutExpired:
        process.terminate()
        process.wait(timeout=15)
        raise RuntimeError(f"{label}: player startup timed out; inspect its log")
    result = json.loads(report.read_text(encoding="utf-8-sig")) if report.exists() else {}
    print(json.dumps(dict(phase=label, exitCode=code, report=str(report), result=result), ensure_ascii=False), flush=True)
    if code != 0 or result.get("passed") is not True:
        raise RuntimeError(f"{label}: player smoke failed")
