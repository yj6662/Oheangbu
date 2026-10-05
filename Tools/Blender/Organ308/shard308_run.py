# #308 D308-4c crystal pipeline runner (v2 D308-4b scripts kept in Art/Characters/Organ308/Retired/Shard308_v2/tools): one Blender process at a time (BSOD history), waits while any other blender.exe runs.
#   python Tools/Blender/Organ308/shard308_run.py                 build -> place x7 -> deploy -> sheet -> verify
#   python Tools/Blender/Organ308/shard308_run.py place agwi fox_spirit   only those placements (then deploy, sheet, verify)
#   python Tools/Blender/Organ308/shard308_run.py build|deploy|sheet|verify
import subprocess
import sys
import time
from pathlib import Path

BLENDER = r"C:/Program Files/Blender Foundation/Blender 5.0/blender.exe"
HERE = Path(__file__).resolve().parent
GUARD = HERE.parents[1] / "resource_guard.py"      # Tools/resource_guard.py (GPU lock wrapper)
ENTRIES = ["dokkaebi", "agwi", "changgui", "bulgasari", "fox_spirit", "imugi", "growth_tree"]


def blender_busy():
    out = subprocess.run(["tasklist", "/FI", "IMAGENAME eq blender.exe", "/NH"], capture_output=True, text=True).stdout
    return "blender.exe" in out.lower()


def wait_free(limit_s=1800):
    t0 = time.time()
    while blender_busy():
        if time.time() - t0 > limit_s:
            raise SystemExit("another blender.exe kept running for %d s — aborting" % limit_s)
        time.sleep(5)


def blender(*args):
    # GPU LOCK (BSOD 0x13A on 2026-10-03): Blender is never started directly — resource_guard.py --gpu-run waits for the danger state to
    # clear and for Tools/.gpu_lock (one GPU-heavy job on the PC at a time), runs the command, always releases
    wait_free()
    cmd = [sys.executable, str(GUARD), "--gpu-run", "organ308", "--",
           BLENDER, "-b", "--factory-startup", "-t", "4", "--python", str(HERE / "shard308.py"), "--", *args]
    p = subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=5400 + 1200)
    keep = [l for l in p.stdout.splitlines() + p.stderr.splitlines()
            if l.startswith(("ORGAN308", "Traceback", "RuntimeError", "Error")) or "Error:" in l or l.startswith("  File")]
    print("\n".join(keep) or "(no marker output)", flush=True)
    if p.returncode != 0 or not any(l.startswith("ORGAN308") for l in keep):
        raise SystemExit("blender step failed: %s" % (args,))


def python(script):
    subprocess.run([sys.executable, str(HERE / script)], check=True)


def main():
    a = sys.argv[1:]
    if not a:
        blender("build")
        for e in ENTRIES:
            blender("place", e)
    elif a[0] == "build":
        blender("build")
        return
    elif a[0] == "place":
        for e in a[1:] or ENTRIES:
            blender("place", e)
    elif a[0] in ("deploy", "sheet"):
        python("shard308_%s.py" % a[0])
        return
    elif a[0] == "verify":
        blender("verify")
        return
    else:
        raise SystemExit("unknown step " + a[0])
    python("shard308_deploy.py")
    python("shard308_sheet.py")
    blender("verify")


main()
