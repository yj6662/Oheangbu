"""D308-112: retire old spell-VFX files by *moving* them out of Assets (never deletes).

Candidates are the tier A / B rows of orphans308.csv. A candidate stays put (지우면 안 됨) when any of these hold:

    template   SpellVFX120/Profiles|Prefabs/NNN_HHHH - Vfx308Build reads these by id as the source of every #308 copy
    path       its Assets path is written literally in a script or tool (Resources-free path loads, e.g. VFX120_Catalog)
    name       its file name is a string in runtime code, a 300-era editor tool or a #308 tool (name lookups)
    set        an old visual-set copy (PlayerRig.prefab default, Compact*Checks258 read it)
    guid       a file that is not itself moving references it (other assets, ProjectSettings, URP assets)
    closure    something that stays references it (repeated until nothing changes)

Moved files land at <repo>/_Quarantine308/<path from the Unity project>, each with its .meta, so a refresh sees them as
deleted and --revert puts them back byte for byte. Emptying the folder is the user's call.

    python Tools/Art/SpellVFX308/quarantine308.py            # plan only: writes the manifest, moves nothing
    python Tools/Art/SpellVFX308/quarantine308.py --apply    # moves (editor must not be in Play)
    python Tools/Art/SpellVFX308/quarantine308.py --revert   # moves everything in the manifest back
"""
import argparse
import csv
import importlib.util
import os
import re
import shutil
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
spec = importlib.util.spec_from_file_location("orphans308", os.path.join(HERE, "orphans308.py"))
orphans = importlib.util.module_from_spec(spec)
spec.loader.exec_module(orphans)

ROOT, PROJECT, ASSETS = orphans.ROOT, orphans.PROJECT, orphans.ASSETS
QUAR = os.path.join(ROOT, "_Quarantine308")
COVER = os.path.join(PROJECT, "Screenshots", "SpellVFX308", "coverage")
SCRIPTS = os.path.join(ASSETS, "_Project", "Scripts")
TEMPLATE = re.compile(r"/SpellVFX120/(Profiles|Prefabs)/\d{3}_[0-9A-F]{4}\.(asset|prefab)$")
OLD_SET_NAMES = {"SpellVisualSet_120.asset"}
ERA = re.compile(r"30\d")
TEXT_EXT = {".cs", ".py", ".sh", ".ps1", ".jsx", ".json", ".txt", ".md"}
GENERATED = re.compile(r"/Dry/|/Stage308_backup_[^/]+/|guid_(index|cache)|_(index|cache|report)\.json$")
YAML_EXT = {".asset", ".prefab", ".unity", ".mat", ".controller", ".anim", ".playable", ".mask", ".overrideController",
            ".lighting", ".preset", ".signal", ".spriteatlas", ".terrainlayer", ".mixer", ".renderTexture", ".cubemap",
            ".flare", ".physicMaterial", ".guiskin", ".fontsettings", ".shadervariants", ".vfx", ".vfxoperator",
            ".vfxblock", ".shadergraph", ".shadersubgraph", ".inputactions"}


def read(p):
    try:
        with open(p, "r", encoding="utf-8", errors="ignore") as h:
            return h.read()
    except OSError:
        return ""


def code_files():
    """(path, kind) for every script / tool: runtime = non-Editor .cs, live = 300-era or #308 editor tools and Tools/**."""
    out = []
    for base, _, files in os.walk(SCRIPTS):
        for f in files:
            if f.endswith(".cs"):
                p = os.path.join(base, f)
                editor = os.sep + "Editor" + os.sep in p + os.sep
                out.append((p, "runtime" if not editor else ("live" if ERA.search(f) else "legacy")))
    for base, dirs, files in os.walk(os.path.join(ROOT, "Tools")):
        dirs[:] = [d for d in dirs if d not in ("__pycache__", "node_modules", "logs", ".venv")]
        for f in files:
            if os.path.splitext(f)[1] in TEXT_EXT and f != "quarantine308.py":
                p = os.path.join(base, f)
                if GENERATED.search(p.replace("\\", "/")):
                    continue                    # whole-project caches name every asset: not a consumer

                out.append((p, "live" if (ERA.search(p.replace(ROOT, "")) or "Stage308" in p) else "legacy"))
    return out


def plan():
    rows = [r for r in csv.DictReader(open(os.path.join(COVER, "orphans308.csv"), encoding="utf-8-sig")) if r["tier"] in ("A", "B")]
    cand = {r["path"]: r for r in rows}
    keep = {}                                   # path -> reason
    texts = [(p, k, read(p)) for p, k in code_files()]

    for path in cand:
        name = os.path.basename(path)
        stem = os.path.splitext(name)[0]
        if TEMPLATE.search(path):
            keep[path] = "template: Vfx308Build reads SpellVFX120/Profiles|Prefabs/<id> as the #308 source"
            continue
        if name in OLD_SET_NAMES:
            keep[path] = "set: old visual-set copy (PlayerRig.prefab default, Compact*Checks258)"
            continue
        hits = [os.path.relpath(p, ROOT).replace("\\", "/") for p, k, t in texts if path in t]
        if hits:
            keep[path] = "path: " + ", ".join(hits[:4])
            continue
        tok = re.compile(r"(?<![A-Za-z0-9_])" + re.escape(stem) + r"(?![A-Za-z0-9_])")
        hits = [os.path.relpath(p, ROOT).replace("\\", "/") for p, k, t in texts if k in ("runtime", "live") and tok.search(t)]
        if hits:
            keep[path] = "name: " + ", ".join(hits[:4])

    # guid edges over the whole project + ProjectSettings
    guid_to_path, all_files = orphans.index()
    path_to_guid = {orphans.rel(p): g for g, p in guid_to_path.items()}
    cache = {}
    refs = {}                                   # referencer rel path -> set of guids
    for p in all_files:
        if os.path.splitext(p)[1] in YAML_EXT:
            refs[orphans.rel(p)] = orphans.edges(p, cache)
    ps = os.path.join(PROJECT, "ProjectSettings")
    for f in os.listdir(ps):
        p = os.path.join(ps, f)
        if os.path.isfile(p):
            refs["ProjectSettings/" + f] = set(g.decode() for g in orphans.GUID.findall(open(p, "rb").read()))
    cand_guid = {path_to_guid.get(p): p for p in cand if path_to_guid.get(p)}
    referenced_by = {}
    for src, gs in refs.items():
        for g in gs:
            if g in cand_guid and cand_guid[g] != src:
                referenced_by.setdefault(cand_guid[g], set()).add(src)
    for path, srcs in referenced_by.items():
        outside = sorted(s for s in srcs if s not in cand)
        if outside and path not in keep:
            keep[path] = "guid: " + ", ".join(outside[:4])

    changed = True
    while changed:                              # closure: what a kept file references must stay
        changed = False
        for path in list(keep):
            for g in refs.get(path, ()):
                t = cand_guid.get(g)
                if t and t not in keep:
                    keep[t] = "closure: referenced by kept " + os.path.basename(path)
                    changed = True
    move = [p for p in cand if p not in keep]
    return cand, keep, move


def legacy_breaks(move):
    """Editor builders (not live) that name a moved file's folder by path: re-running them would rebuild or fail."""
    folders = sorted({p.rsplit("/", 1)[0] for p in move})
    out = {}
    for p, k, t in [(p, k, read(p)) for p, k in code_files() if k == "legacy" and p.endswith(".cs")]:
        for f in folders:
            if f + "/" in t or f + "\"" in t:
                out.setdefault(os.path.basename(p), set()).add(f.split("SpellVFX120/")[-1])
    return out


def write(cand, keep, move, breaks):
    os.makedirs(COVER, exist_ok=True)
    mb = lambda ps: sum(int(cand[p]["bytes"]) for p in ps) / 1048576
    with open(os.path.join(COVER, "quarantine308.csv"), "w", encoding="utf-8-sig", newline="") as h:
        w = csv.writer(h)
        w.writerow(["action", "지우면 안 됨", "tier", "bytes", "path", "quarantine_path", "reason"])
        for p in sorted(move):
            w.writerow(["move", "", cand[p]["tier"], cand[p]["bytes"], p, "_Quarantine308/" + p, cand[p]["reached_by"]])
        for p in sorted(keep):
            w.writerow(["stay", "지우면 안 됨", cand[p]["tier"], cand[p]["bytes"], p, "", keep[p]])
    by = lambda tier, ps: [p for p in ps if cand[p]["tier"] == tier]
    lines = ["# quarantine308 - 옛 술식 VFX 격리 (D308-112)", "",
             "지우지 않고 옮긴다. 옮긴 파일은 `_Quarantine308/` 아래 같은 경로에 .meta와 함께 있다. "
             "확인이 끝나면 그 폴더를 직접 비우면 된다 (되돌리기: `python Tools/Art/SpellVFX308/quarantine308.py --revert`).", "",
             "| | A | B | 합 |", "|---|---|---|---|",
             "| 옮김 | %.1f MB (%d) | %.1f MB (%d) | %.1f MB (%d) |" % (mb(by("A", move)), len(by("A", move)), mb(by("B", move)), len(by("B", move)), mb(move), len(move)),
             "| 남김 = 지우면 안 됨 | %.1f MB (%d) | %.1f MB (%d) | %.1f MB (%d) |" % (mb(by("A", keep)), len(by("A", keep)), mb(by("B", keep)), len(by("B", keep)), mb(keep), len(keep)),
             "", "## 남긴 까닭 (지우면 안 됨)", ""]
    kinds = {}
    for p, r in keep.items():
        kinds.setdefault(r.split(":")[0], []).append(p)
    for k, ps in sorted(kinds.items()):
        lines.append("- **%s** %d개 %.1f MB - 예: %s" % (k, len(ps), mb(ps), ", ".join(os.path.basename(x) for x in sorted(ps)[:5])))
    lines += ["", "## 다시 돌리면 깨지는 옛 빌더 (옮긴 파일의 폴더를 경로로 씀, 지금 게임 · #308 빌드와 무관)", ""]
    for f, ds in sorted(breaks.items()):
        lines.append("- %s - %s" % (f, ", ".join(sorted(ds))))
    with open(os.path.join(COVER, "quarantine308.md"), "w", encoding="utf-8-sig") as h:
        h.write("\n".join(lines) + "\n")
    print("\n".join(lines[4:8]))


def apply(move):
    done, skipped = [], []
    for p in sorted(move):
        src = os.path.join(PROJECT, p)
        dst = os.path.join(QUAR, p)
        if not os.path.exists(src):
            skipped.append(p + " (gone)")
            continue
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        try:
            shutil.move(src, dst)
            if os.path.exists(src + ".meta"):
                shutil.move(src + ".meta", dst + ".meta")
            done.append(p)
        except OSError as e:
            skipped.append("%s (%s)" % (p, e))
    with open(os.path.join(QUAR, "README.md"), "w", encoding="utf-8-sig") as h:
        h.write("# _Quarantine308\n\nD308-112 (2026-10-18) 옛 술식 VFX 격리. Unity 프로젝트(Oheangbu/) 기준 같은 경로에 .meta와 함께 옮겼다.\n"
                "목록 = Oheangbu/Screenshots/SpellVFX308/coverage/quarantine308.csv (action=move).\n"
                "되돌리기 = python Tools/Art/SpellVFX308/quarantine308.py --revert\n"
                "확인이 끝나면 이 폴더를 통째로 지워도 된다 (사용자가 직접).\n")
    print("moved %d, skipped %d" % (len(done), len(skipped)))
    for s in skipped:
        print("  skip " + s)


def revert():
    rows = [r for r in csv.DictReader(open(os.path.join(COVER, "quarantine308.csv"), encoding="utf-8-sig")) if r["action"] == "move"]
    back = 0
    for r in rows:
        src = os.path.join(QUAR, r["path"])
        dst = os.path.join(PROJECT, r["path"])
        if os.path.exists(src) and not os.path.exists(dst):
            os.makedirs(os.path.dirname(dst), exist_ok=True)
            shutil.move(src, dst)
            if os.path.exists(src + ".meta"):
                shutil.move(src + ".meta", dst + ".meta")
            back += 1
    print("moved back %d of %d" % (back, len(rows)))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--apply", action="store_true")
    ap.add_argument("--revert", action="store_true")
    a = ap.parse_args()
    if a.revert:
        return revert()
    cand, keep, move = plan()
    write(cand, keep, move, legacy_breaks(move))
    if a.apply:
        apply(move)


if __name__ == "__main__":
    sys.exit(main())
