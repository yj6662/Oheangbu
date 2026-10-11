#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""disk_cleanup308.py - move (never delete) regenerable / superseded work files out of the repo.

  python Tools/disk_cleanup308.py safe              dry run (default): prints what it WOULD move and the GB
  python Tools/disk_cleanup308.py more              safe + superseded recent scene copies + delivery zips
  python Tools/disk_cleanup308.py most              more + the Oheangbu_Unused307 quarantine + old .blend1
  python Tools/disk_cleanup308.py safe --move       really MOVE into  <repo>/../Oheangbu_Cleanup308/safe/<same relative path>
  python Tools/disk_cleanup308.py --restore <manifest.json>     move everything of one manifest back
  python Tools/disk_cleanup308.py --self-test       builds a throw-away tree in the temp folder and checks every rule
  options:  --only g1,g2   --skip g1,g2   --list (every file)   --root <repo>   --dest <folder>

There is NO delete mode. A move is an os.rename on the same drive (instant, reversible); a destination on another
drive is refused. The user empties Oheangbu_Cleanup308 by hand when they are sure.

Never moved, whatever a group selects (each refusal is counted and printed):
  * a file tracked by git (git ls-files; if git cannot be asked, --move is refused altogether)
  * anything under Oheangbu/Assets, Oheangbu/ProjectSettings, Oheangbu/Packages, Oheangbu/Library, Docs, .git, tmp
  * anything that matches a KEEP pattern (ledgers, stage folders, Blender / model sources, catalogue, today's folders)

Written 2026-10-06 AFTER Art/World, Art/Presentation297, Art/Performance, Art/PlayerV2, Art/Characters,
Art/PlayerPhase1 and Art/Playtest308 had already disappeared from the disk (06:23 - 06:26, not by this tool).
The groups below therefore describe what is LEFT. Tools/Unity/Stage30* is now recovery material: it is KEEP.
"""
import argparse, datetime, json, os, re, subprocess, sys, tempfile, time

DAY = 86400.0
PACKAGES = ("safe", "more", "most")
MODEL_EXT = (".blend", ".fbx", ".glb", ".gltf", ".usdz", ".obj", ".bvh", ".psd", ".spp")
SCENE_EXT = (".unity", ".asset", ".prefab", ".mat")
BACKUP_DIR = re.compile(r"^(backups?|scenebackup|before|insurance|backup_.+|.+_backup)$", re.I)

FORBIDDEN = ("Oheangbu/Assets", "Oheangbu/ProjectSettings", "Oheangbu/Packages", "Oheangbu/Library",
             "Oheangbu/UserSettings", "Docs", ".git", "tmp")
KEEP = (
    "**/ledger_*.json",                      # ledgers: revert / verify read them
    "Tools/Unity/Stage30*/**",               # stage folders + their backups = recovery material after 2026-10-06
    "Tools/Unity/C02RigFaceLab/**",
    "Tools/Unity/Plan307/**",
    "Oheangbu/Screenshots/AssetCatalog/**",  # curated catalogue the project reads
    "Art/Playtest308/**", "Art/Cinematic308/**", "Art/Characters308/**", "Art/UI308/**",   # live / not yet judged
    "Art/SpellVFX120/Deploy308/**", "Art/SpellVFX120/Spell120_308/**",
    "Art/Presentation302/**",                # the only deck / trailer finals left
    "Art/**/MeshySources/**", "Art/**/Source/**",
) + tuple("Art/**/*" + e for e in MODEL_EXT)

# id, package, one-line reason (Korean text lives in the plan document; this is the tool's own summary)
GROUPS = (
    ("builds-old",        "safe", "Builds/<series>/<utc> except the newest per series, older than 7 days (rebuildable player builds)"),
    ("dotnet-out",        "safe", "Tools/**/bin and obj next to a .csproj, older than 7 days (dotnet build output)"),
    ("pycache",           "safe", "__pycache__ under Tools/ and Art/ (Python rewrites them)"),
    ("playerdosa-frames", "safe", "Oheangbu/Screenshots/PlayerDosa/*_visuals/{observer,play}/*.png, older than 7 days (frame dumps; summary.json / mp4 stay)"),
    ("scene-copies-old",  "safe", "scene / asset copies inside backup folders under Art/, last touched 7+ days ago"),
    ("scene-copies-superseded", "more", "scene / asset copies inside backup folders under Art/, any age, except the newest per folder + tool + file name"),
    ("delivery-zips",     "more", "Art/SpellVFX120/<summon>/<summon>_Delivery.zip, older than 7 days (publish_*.py packs them again)"),
    ("unused307-quarantine", "most", "../Oheangbu_Unused307 (the 2026-10-01 quarantine of unused assets; needs its own decision)"),
    ("blend1",            "most", "Art/**/*.blend1 older than 7 days whose .blend is still next to it (Blender's previous-save copy)"),
)
LEVEL = {"safe": 0, "more": 1, "most": 2}


def glob_re(pat):
    out, i = "", 0
    while i < len(pat):
        if pat.startswith("**/", i): out += "(?:.*/)?"; i += 3
        elif pat.startswith("**", i): out += ".*"; i += 2
        elif pat[i] == "*": out += "[^/]*"; i += 1
        elif pat[i] == "?": out += "[^/]"; i += 1
        else: out += re.escape(pat[i]); i += 1
    return re.compile("^" + out + "$", re.I)


KEEP_RE = tuple((p, glob_re(p)) for p in KEEP)


def touched(st):
    """Last time the file was written OR created here (a copied backup keeps the old mtime of its source)."""
    return max(st.st_mtime, getattr(st, "st_birthtime", st.st_ctime))


class Tree:
    def __init__(self, root, dest, now=None, tracked="ask"):
        self.root = os.path.abspath(root)
        self.dest = os.path.abspath(dest)
        self.outside = os.path.join(os.path.dirname(self.root), "Oheangbu_Unused307")
        self.now = time.time() if now is None else now
        self.tracked = self._git_tracked() if tracked == "ask" else tracked

    def _git_tracked(self):
        try:
            p = subprocess.run(["git", "-C", self.root, "-c", "core.quotepath=false", "ls-files", "-z"],
                               capture_output=True, timeout=120)
            if p.returncode != 0: return None
            return {x.lower() for x in p.stdout.decode("utf-8", "replace").split("\0") if x}
        except (OSError, subprocess.SubprocessError):
            return None

    def rel(self, path):
        path = os.path.abspath(path)
        if path == self.outside or path.startswith(self.outside + os.sep):
            return "_outside/" + os.path.relpath(path, os.path.dirname(self.outside)).replace("\\", "/")
        return os.path.relpath(path, self.root).replace("\\", "/")

    def refusal(self, path):
        """None when the file may move, else the reason it must stay."""
        r = self.rel(path)
        if r.startswith("../") or r == "..": return "outside the repo"
        if r.startswith("_outside/"): return None            # the named quarantine folder only
        low = r.lower()
        for f in FORBIDDEN:
            if low == f.lower() or low.startswith(f.lower() + "/"): return "protected folder " + f
        for pat, rx in KEEP_RE:
            if rx.match(r): return "KEEP " + pat
        if self.tracked is not None and low in self.tracked: return "tracked by git"
        return None

    def old(self, t, days):
        return t < self.now - days * DAY

    def files(self, top):
        for r, ds, fs in os.walk(top):
            for f in fs:
                p = os.path.join(r, f)
                try: st = os.stat(p)
                except OSError: continue
                yield p, st

    # ---- selectors: each returns [(unit label, [(path, stat), ...])]
    def sel_builds_old(self):
        out, base = [], os.path.join(self.root, "Builds")
        if not os.path.isdir(base): return out
        for series in sorted(os.listdir(base)):
            sp = os.path.join(base, series)
            if not os.path.isdir(sp): continue
            stamps = sorted(d for d in os.listdir(sp) if os.path.isdir(os.path.join(sp, d)))
            for d in stamps[:-1]:                               # the newest (last by name = utc stamp) stays
                fl = list(self.files(os.path.join(sp, d)))
                if fl and self.old(max(touched(s) for _, s in fl), 7): out.append(("Builds/%s/%s" % (series, d), fl))
        return out

    def sel_dotnet_out(self):
        out, base = [], os.path.join(self.root, "Tools")
        for r, ds, fs in os.walk(base):
            if os.path.basename(r).lower().startswith("stage30"): ds[:] = []; continue
            if any(f.lower().endswith((".csproj", ".sln")) for f in fs):
                for d in [x for x in ds if x.lower() in ("bin", "obj")]:
                    fl = list(self.files(os.path.join(r, d)))
                    if fl and self.old(max(touched(s) for _, s in fl), 7): out.append((self.rel(os.path.join(r, d)), fl))
                    ds.remove(d)
        return out

    def sel_pycache(self):
        out = []
        for top in ("Tools", "Art"):
            for r, ds, fs in os.walk(os.path.join(self.root, top)):
                if "__pycache__" in ds:
                    fl = list(self.files(os.path.join(r, "__pycache__")))
                    if fl: out.append((self.rel(os.path.join(r, "__pycache__")), fl))
                    ds.remove("__pycache__")
        return out

    def sel_playerdosa_frames(self):
        out, base = [], os.path.join(self.root, "Oheangbu", "Screenshots", "PlayerDosa")
        if not os.path.isdir(base): return out
        for run in sorted(os.listdir(base)):
            if not run.endswith("_visuals"): continue
            for view in ("observer", "play"):
                vp = os.path.join(base, run, view)
                if not os.path.isdir(vp): continue
                fl = [(os.path.join(vp, f), os.stat(os.path.join(vp, f))) for f in os.listdir(vp) if f.lower().endswith(".png")]
                if fl and self.old(max(touched(s) for _, s in fl), 7): out.append((self.rel(vp), fl))
        return out

    def _scene_copies(self):
        """(container rel, first child under it, path, stat) for Unity-serialised copies inside backup-like folders of Art/."""
        base = os.path.join(self.root, "Art")
        for r, ds, fs in os.walk(base):
            parts = os.path.relpath(r, base).replace("\\", "/").split("/")
            hit = next((i for i, p in enumerate(parts) if BACKUP_DIR.match(p)), None)
            if hit is None: continue
            cont = "Art/" + "/".join(parts[:hit + 1])
            child = parts[hit + 1] if len(parts) > hit + 1 else ""
            for f in fs:
                name = f[:-5] if f.lower().endswith(".meta") else f
                if not name.lower().endswith(SCENE_EXT): continue
                p = os.path.join(r, f)
                try: yield cont, child, p, os.stat(p)
                except OSError: pass

    def sel_scene_copies_old(self):
        units = {}
        for cont, child, p, st in self._scene_copies():
            if self.old(touched(st), 7): units.setdefault(cont, []).append((p, st))
        return sorted(units.items())

    def sel_scene_copies_superseded(self):
        groups = {}
        for cont, child, p, st in self._scene_copies():
            tool = re.sub(r"[-_]?\d{8}(T|-)?\d{0,9}Z?.*$", "", child)      # "ore308-20261005T0137..." -> "ore308"
            groups.setdefault((cont, tool.lower(), os.path.basename(p).lower()), []).append((touched(st), p, st))
        units = {}
        for (cont, tool, name), rows in groups.items():
            rows.sort(key=lambda x: x[0])
            for t, p, st in rows[:-1]:                                     # the newest copy of each stays
                units.setdefault(cont, []).append((p, st))
        return sorted(units.items())

    def sel_delivery_zips(self):
        out, base = [], os.path.join(self.root, "Art", "SpellVFX120")
        if not os.path.isdir(base): return out
        for d in sorted(os.listdir(base)):
            p = os.path.join(base, d, d + "_Delivery.zip")
            if os.path.isfile(p):
                st = os.stat(p)
                if self.old(touched(st), 7): out.append((self.rel(p), [(p, st)]))
        return out

    def sel_unused307_quarantine(self):
        if not os.path.isdir(self.outside): return []
        units = {}
        for p, st in self.files(self.outside):
            parts = self.rel(p).split("/")
            units.setdefault("/".join(parts[:4]), []).append((p, st))      # _outside/Oheangbu_Unused307/Assets/<pack>
        return sorted(units.items())

    def sel_blend1(self):
        units = {}
        for p, st in self.files(os.path.join(self.root, "Art")):
            if p.lower().endswith(".blend1") and os.path.isfile(p[:-1]) and self.old(touched(st), 7):
                units.setdefault(self.rel(os.path.dirname(p)), []).append((p, st))
        return sorted(units.items())

    # ---- plan
    def plan(self, package, only=None, skip=None):
        rows, refused, seen = [], {}, set()
        for gid, pkg, why in GROUPS:
            if LEVEL[pkg] > LEVEL[package]: continue
            if only and gid not in only: continue
            if skip and gid in skip: continue
            for label, fl in getattr(self, "sel_" + gid.replace("-", "_"))():
                keep = []
                for p, st in fl:
                    k = os.path.normcase(os.path.abspath(p))
                    if k in seen: continue
                    no = self.refusal(p)
                    if no: refused.setdefault(no, [0, 0]); refused[no][0] += 1; refused[no][1] += st.st_size; continue
                    seen.add(k); keep.append((p, st))
                if keep: rows.append((gid, pkg, label, keep))
        return rows, refused


def gb(n): return n / 1073741824.0


def day(t): return time.strftime("%Y-%m-%d", time.localtime(t))


def report(tree, package, rows, refused, every=False, out=sys.stdout):
    w = out.write
    w("package %s   root %s\n" % (package, tree.root))
    if tree.tracked is None: w("!! git could not be asked for its tracked files: --move is refused\n")
    total = 0; count = 0
    for gid, pkg, why in GROUPS:
        mine = [r for r in rows if r[0] == gid]
        if not mine: continue
        size = sum(st.st_size for r in mine for _, st in r[3]); n = sum(len(r[3]) for r in mine)
        total += size; count += n
        w("\n[%s] %s  -  %.2f GB, %d files\n    %s\n" % (pkg, gid, gb(size), n, why))
        mine.sort(key=lambda r: -sum(st.st_size for _, st in r[3]))
        for _, _, label, fl in mine[:None if every else 40]:
            w("    %9.1f MB %6d f  newest %s  %s\n" % (sum(st.st_size for _, st in fl) / 1048576.0, len(fl), day(max(touched(st) for _, st in fl)), label))
            if every:
                for p, st in fl: w("          %s\n" % tree.rel(p))
        if not every and len(mine) > 40: w("    ... %d more units (use --list)\n" % (len(mine) - 40))
    w("\nTOTAL %s: %.2f GB in %d files would move to %s\n" % (package, gb(total), count, os.path.join(tree.dest, package)))
    for why, (n, size) in sorted(refused.items(), key=lambda kv: -kv[1][1]):
        w("  left in place (%s): %d files, %.2f GB\n" % (why, n, gb(size)))
    return total, count


def move(tree, package, rows):
    if tree.tracked is None: raise SystemExit("refused: git ls-files failed, tracked files cannot be told apart")
    target = os.path.join(tree.dest, package)
    if os.path.splitdrive(target)[0].lower() != os.path.splitdrive(tree.root)[0].lower():
        raise SystemExit("refused: %s is on another drive (a move there would copy and delete)" % target)
    inside = os.path.normcase(tree.dest + os.sep)
    if inside.startswith(os.path.normcase(tree.root + os.sep)): raise SystemExit("refused: destination lies inside the repo")
    os.makedirs(target, exist_ok=True)
    stamp = datetime.datetime.now(datetime.timezone.utc).strftime("%Y%m%dT%H%M%SZ")
    manifest = os.path.join(target, "manifest_%s.json" % stamp)
    done, failed = [], []
    try:
        for gid, pkg, label, fl in rows:
            for p, st in fl:
                if tree.refusal(p): failed.append((tree.rel(p), "refused at move time")); continue
                dst = os.path.join(target, tree.rel(p).replace("/", os.sep))
                if os.path.exists(dst): failed.append((tree.rel(p), "destination exists")); continue
                try:
                    os.makedirs(os.path.dirname(dst), exist_ok=True)
                    os.rename(p, dst)
                    done.append({"group": gid, "src": os.path.abspath(p), "dst": dst, "bytes": st.st_size})
                except OSError as e:
                    failed.append((tree.rel(p), str(e)))
    finally:
        with open(manifest, "w", encoding="utf-8") as f:
            json.dump({"tool": "disk_cleanup308", "utc": stamp, "package": package, "root": tree.root,
                       "moved": done, "failed": [{"path": a, "why": b} for a, b in failed]}, f, ensure_ascii=False, indent=1)
    print("moved %d files, %.2f GB -> %s" % (len(done), gb(sum(d["bytes"] for d in done)), target))
    print("not moved: %d (see manifest)   manifest: %s" % (len(failed), manifest))
    print("empty folders are left where they were; nothing was deleted. Undo: --restore \"%s\"" % manifest)
    return manifest


def restore(manifest):
    data = json.load(open(manifest, encoding="utf-8"))
    back = skipped = 0
    for row in data["moved"]:
        src, dst = row["src"], row["dst"]
        if not os.path.exists(dst) or os.path.exists(src): skipped += 1; continue
        os.makedirs(os.path.dirname(src), exist_ok=True)
        os.rename(dst, src); back += 1
    print("restored %d files, skipped %d (already back, or the original path is occupied)" % (back, skipped))
    return back, skipped


def self_test():
    base = tempfile.mkdtemp(prefix="disk_cleanup308_selftest_")
    root, dest, outside = os.path.join(base, "Oheangbu"), os.path.join(base, "Oheangbu_Cleanup308"), os.path.join(base, "Oheangbu_Unused307")
    real_now = time.time(); now = real_now + 30 * DAY          # every file made here looks 30 days old ...

    def mk(rel, recent=False, top=None):
        p = os.path.join(top or root, rel.replace("/", os.sep)); os.makedirs(os.path.dirname(p), exist_ok=True)
        with open(p, "w") as f: f.write(rel)
        if recent: os.utime(p, (now - DAY, now - DAY))            # ... unless its write time is set to "yesterday"
        return p

    old = ["Builds/Demo/20260101-000000/a.bin", "Tools/Foo/bin/Debug/x.dll", "Tools/Foo/obj/y.cache", "Tools/Foo/__pycache__/m.pyc",
           "Oheangbu/Screenshots/PlayerDosa/20260907_x_visuals/play/f00001.png",
           "Art/X/Backups/20260101T000000Z/W_Main.unity", "Art/X/Backups/20260101T000000Z/W_Main.unity.meta"]
    stay = ["Builds/Demo/20260102-000000/a.bin",                              # newest build of its series
            "Tools/Foo/Foo.csproj", "Tools/Bar/bin/tool.exe",                 # bin without a .csproj next to it
            "Tools/Unity/Stage308_x/bin/Debug/z.dll", "Tools/Unity/Stage308_x/Stage.csproj",
            "Oheangbu/Screenshots/PlayerDosa/20260907_x_visuals/summary.json", "Oheangbu/Screenshots/AssetCatalog/Backups/s.unity",
            "Oheangbu/Assets/Backups/W.unity", "Oheangbu/Library/bin/x.dll", "Docs/Backups/W.unity", "tmp/Backups/W.unity",
            "Art/X/Backups/20260101T000000Z/ledger_x.json", "Art/X/Backups/20260101T000000Z/notes.md",
            "Art/X/Backups/20260101T000000Z/rig.blend", "Art/X/Backups/tracked/W_Tracked.unity",
            "Art/Playtest308/Pacing/Backups/20260101T000000Z/W_Main.unity", "Art/Y/Source/Backups/m.asset",
            "Art/Z/model.blend", "Art/Z/lonely.blend1", "Art/Presentation302/Backups/t.unity"]
    for r in old + stay: mk(r)
    mk("Art/R/Backups/ore308-20261001T010101/W_Main.unity", recent=True)       # superseded recent copy -> more
    newest = mk("Art/R/Backups/ore308-20261002T010101/W_Main.unity", recent=True)
    os.utime(newest, (now - DAY / 2, now - DAY / 2))
    mk("Art/R/Backups/places308-20261001T010101/W_Main.unity", recent=True)    # only copy of its tool -> stays
    mk("Art/SpellVFX120/Deer/Deer_Delivery.zip"); mk("Art/Z/model.blend1"); mk("Assets/Pack/a.png", top=outside)
    tracked = {"art/x/backups/tracked/w_tracked.unity"}
    t = Tree(root, dest, now=now, tracked=tracked)

    def rels(rows): return sorted(t.rel(p) for r in rows for p, _ in r[3])
    safe, ref = t.plan("safe")
    assert rels(safe) == sorted(old), rels(safe)
    assert any(k == "tracked by git" for k in ref) and any(k.startswith("KEEP") for k in ref), ref
    more, _ = t.plan("more")
    assert rels(more) == sorted(old + ["Art/R/Backups/ore308-20261001T010101/W_Main.unity", "Art/SpellVFX120/Deer/Deer_Delivery.zip"]), rels(more)
    most, _ = t.plan("most")
    extra = set(rels(most)) - set(rels(more))
    assert extra == {"_outside/Oheangbu_Unused307/Assets/Pack/a.png", "Art/Z/model.blend1"}, extra
    for r in stay: assert t.refusal(os.path.join(root, r)) or r not in rels(most), r
    assert t.refusal(os.path.join(root, "Oheangbu/Assets/x.png")) and t.refusal(os.path.join(root, "Docs/a.md"))
    assert t.refusal(os.path.join(base, "elsewhere.txt")) == "outside the repo"
    try: move(Tree(root, dest, now=now, tracked=None), "safe", safe); raise AssertionError("moved without git")
    except SystemExit: pass
    try: move(Tree(root, os.path.join(root, "inside"), now=now, tracked=tracked), "safe", safe); raise AssertionError("moved into the repo")
    except SystemExit: pass
    before = sorted(os.path.relpath(os.path.join(r, f), base) for r, _, fs in os.walk(base) for f in fs)
    manifest = move(t, "most", most)
    for r in stay: assert os.path.isfile(os.path.join(root, r)), "must stay: " + r
    for r in old: assert not os.path.exists(os.path.join(root, r)) and os.path.isfile(os.path.join(dest, "most", r)), r
    assert os.path.isfile(os.path.join(dest, "most", "_outside", "Oheangbu_Unused307", "Assets", "Pack", "a.png"))
    assert os.path.isfile(newest) and os.path.isfile(os.path.join(root, "Art/Z/model.blend"))
    assert t.plan("most")[0] == [], "a second run must find nothing"
    back, skipped = restore(manifest)
    after = sorted(os.path.relpath(os.path.join(r, f), base) for r, _, fs in os.walk(base) for f in fs if not f.startswith("manifest_"))
    assert after == before and skipped == 0, "restore must put every file back"
    assert restore(manifest) == (0, back)
    print("SELF-TEST PASS (%d files moved and restored). The throw-away test tree is left at %s (this tool never deletes)." % (back, base))


def main():
    ap = argparse.ArgumentParser(description="Move regenerable work files out of the repo (dry run unless --move). No delete mode.")
    ap.add_argument("package", nargs="?", choices=PACKAGES)
    ap.add_argument("--move", action="store_true", help="really move (default: only print)")
    ap.add_argument("--restore", metavar="MANIFEST", help="move the files of a manifest back")
    ap.add_argument("--self-test", action="store_true")
    ap.add_argument("--only"); ap.add_argument("--skip"); ap.add_argument("--list", action="store_true")
    ap.add_argument("--root", default=os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    ap.add_argument("--dest")
    a = ap.parse_args()
    if a.self_test: return self_test()
    if a.restore: return restore(a.restore)
    if not a.package: ap.error("package name needed: safe | more | most")
    ids = {g[0] for g in GROUPS}
    only = set(a.only.split(",")) if a.only else None
    skip = set(a.skip.split(",")) if a.skip else None
    for s in (only or set()) | (skip or set()):
        if s not in ids: ap.error("unknown group %s (groups: %s)" % (s, ", ".join(sorted(ids))))
    root = os.path.abspath(a.root)
    tree = Tree(root, a.dest or os.path.join(os.path.dirname(root), "Oheangbu_Cleanup308"))
    rows, refused = tree.plan(a.package, only, skip)
    report(tree, a.package, rows, refused, a.list)
    if a.move: move(tree, a.package, rows)
    else: print("\nDRY RUN - nothing was moved. Add --move to move (never deletes).")


if __name__ == "__main__":
    main()
