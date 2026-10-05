# RETIRED (D308-4b, 2026-10-02): v1 per-species organ parts were replaced by ONE common magic-stone shard.
# Use Tools/Blender/Organ308/shard308_run.py. Kept for history only; running it would rebuild/delete the v1 deploy tree.
raise SystemExit('retired by D308-4b: use Tools/Blender/Organ308/shard308_run.py')
# #308 organ-art runner: one Blender process at a time (BSOD history), waits while any other blender.exe runs.
#   python Tools/Blender/Organ308/run_all.py [entry ...]      (default: all entries, then sheet + verify)
import subprocess
import sys
import time
from pathlib import Path

BLENDER = r"C:/Program Files/Blender Foundation/Blender 5.0/blender.exe"
HERE = Path(__file__).resolve().parent
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


def blender(script, *args):
    wait_free()
    cmd = [BLENDER, "-b", "--factory-startup", "-t", "4", "--python", str(HERE / script)]
    if args:
        cmd += ["--", *args]
    p = subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=1200)
    keep = [l for l in p.stdout.splitlines() + p.stderr.splitlines()
            if l.startswith(("ORGAN308", "Traceback", "RuntimeError", "Error")) or "Error:" in l]
    print("\n".join(keep) or "(no marker output)", flush=True)
    if p.returncode != 0 or not any(l.startswith("ORGAN308") for l in keep):
        raise SystemExit("blender step failed: %s %s" % (script, args))


def main():
    entries = sys.argv[1:] or ENTRIES
    for e in entries:
        blender("build_organs.py", e)
    subprocess.run([sys.executable, str(HERE / "make_sheet.py")], check=True)
    subprocess.run([sys.executable, str(HERE / "make_materials.py")], check=True)
    blender("verify_parts.py")


main()
