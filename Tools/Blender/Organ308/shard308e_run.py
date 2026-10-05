# #308 D308-4e/4d runner: element crystals + contamination masks + review sheet. One Blender process at a time; before every heavy step
# Tools/resource_guard.py --wait blocks only while RAM / CPU / GPU is in a danger state (no fixed throttling — user 2026-10-02).
# Every Blender invocation goes through the GPU lock wrapper (resource_guard.py --gpu-run organ308 -- blender ...; added 2026-10-03 after a
# BSOD): one GPU-heavy job on the PC at a time. Never start Blender for this pipeline any other way.
#   python Tools/Blender/Organ308/shard308e_run.py               elements -> build -> prep x7 -> bake -> render x7 -> deploy -> sheet -> verify
#   python Tools/Blender/Organ308/shard308e_run.py <step> [entries...]   step = elements | build | prep | bake | render | deploy | sheet | verify
import json
import subprocess
import sys
import time
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import shard308e_data as D  # noqa: E402

BLENDER = r"C:/Program Files/Blender Foundation/Blender 5.0/blender.exe"
GUARD = D.ROOT / "Tools/resource_guard.py"
GPU_LABEL = "organ308"


def guard():
    subprocess.run([sys.executable, str(GUARD), "--wait"], check=False)


def blender_busy():
    out = subprocess.run(["tasklist", "/FI", "IMAGENAME eq blender.exe", "/NH"], capture_output=True, text=True).stdout
    return "blender.exe" in out.lower()


def blender(script, *args):
    # GPU LOCK (BSOD 0x13A on 2026-10-03 while Unity Play and other GPU work overlapped): Blender is NEVER started directly. Every
    # invocation goes through `resource_guard.py --gpu-run organ308 -- <blender ...>`: it waits while RAM / CPU / GPU is in a danger state
    # and for Tools/.gpu_lock (one GPU-heavy job on the PC at a time — Unity Play run, Blender render / bake), runs, always releases.
    # Exit code = Blender's (4 = gave up waiting for the lock).
    t0 = time.time()
    while blender_busy():
        if time.time() - t0 > 1800:
            raise SystemExit("another blender.exe kept running for 1800 s")
        time.sleep(5)
    cmd = [sys.executable, str(GUARD), "--gpu-run", GPU_LABEL, "--",
           BLENDER, "-b", "--factory-startup", "-t", "4", "--python", str(HERE / script), "--", *args]
    p = subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=5400 + 3600)
    keep = [l for l in p.stdout.splitlines() + p.stderr.splitlines()
            if l.startswith(("ORGAN308", "Traceback", "RuntimeError", "Error")) or "Error:" in l or l.startswith("  File")]
    print("\n".join(keep) or "(no marker output)", flush=True)
    if p.returncode != 0 or not any(l.startswith("ORGAN308") for l in keep):
        raise SystemExit("blender step failed: %s %s" % (script, args))


def python(script, *args):
    guard()
    subprocess.run([sys.executable, str(HERE / script), *args], check=True)


def elements():
    m = D.scan_elements()
    D.ANALYSIS.mkdir(parents=True, exist_ok=True)
    D.ELEMENT_MAP_JSON.write_text(json.dumps(dict(
        decision="D308-4e: 몹마다 쓰는 결정은 그 몹의 속성(EnemyElementTelegraph 속성 = 공격 프로필 Elemental/Element)으로 정한다",
        rule="Elemental 0 -> Neutral (v3 기본형, COMBAT-DEFENSE 무속성은 빛나지 않는다); Elemental 1 -> ELEMENTS[Element]",
        entries=m), ensure_ascii=False, indent=1), encoding="utf-8")
    print("ELEMENTS", {k: v["element"] for k, v in m.items()})


def main():
    a = sys.argv[1:]
    step = a[0] if a else "all"
    ents = a[1:] or D.ENTRIES
    if step in ("all", "elements"):
        elements()
    if step in ("all", "build"):
        blender("shard308e.py", "build")
    if step in ("all", "prep"):
        for e in ents:
            blender("shard308e.py", "prep", e)
    if step in ("all", "bake"):
        python("contam308.py", "bake", *[e for e in ents if e in D.SPECIES])
    if step in ("all", "render"):
        for e in ents:
            blender("shard308e.py", "render", e)
    if step in ("all", "deploy"):
        python("shard308_deploy.py")
    if step in ("all", "sheet"):
        python("shard308e_sheet.py")
    if step in ("all", "verify"):
        blender("shard308.py", "verify")
        blender("shard308e.py", "verify")
        python("contam308.py", "verify")


if __name__ == "__main__":
    main()
