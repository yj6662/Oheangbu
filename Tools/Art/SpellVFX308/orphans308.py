"""SPEC-SPELL-VFX-OVERHAUL-308 plan step 5: old spell-VFX cleanup *candidate list* (read only, deletes nothing).

Walks the asset reference graph (guid edges in Unity YAML files and their .meta) from four root groups and reports, for
every file under Art/SpellVFX120, which groups still reach it:

    main    W_Demo_Main + Realm_* + lobby / loading / title scenes + every Resources folder, with the old visual-set
            copies (SpellVFX120/Data, Reworld292, Watershed295) cut out - the game reads the Architecture296 copy
    lab     SpellLab308 scenes
    legacy  the other build scenes (W_Demo_Compact, Cheongrim, C1 / C2, SampleScene, migration check)
    sets    the old visual-set copies themselves

A file no group reaches is a tier-A candidate; one only legacy / sets reach is tier B (goes when those scenes retire).
Editor builders that name a SpellVFX120 folder by path are listed too: deleting such a folder breaks a rebuild.

    python Tools/Art/SpellVFX308/orphans308.py [--out DIR]
"""
import argparse
import os
import re
import sys
from collections import defaultdict

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
PROJECT = os.path.join(ROOT, "Oheangbu")
ASSETS = os.path.join(PROJECT, "Assets")
TARGET = os.path.join(ASSETS, "_Project", "Art", "SpellVFX120")
EDITOR = os.path.join(ASSETS, "_Project", "Scripts", "Editor")

OLD_SETS = {"9ba170fd23c53dc418707872eb7b2056",   # Art/SpellVFX120/Data (PlayerRig.prefab default, overridden in scene)
            "278c3cdf45b3ab24fb93a8f33e2264e2",   # Reworld292
            "9f55ff4897c35bf408434d2fd4cd5dbc"}   # Watershed295
MAIN_SCENES = ["Assets/_Project/Scenes/World/W_Demo_Main.unity",
               "Assets/_Project/Art/UI/Loading270/W_Compact_Lobby.unity",
               "Assets/_Project/Art/UI/Loading270/W_Compact_Loading.unity",
               "Assets/_Project/Scenes/World/W_Demo_Compact_Title.unity"]
GUID = re.compile(rb"guid: ([0-9a-f]{32})")


def rel(p):
    return os.path.relpath(p, PROJECT).replace("\\", "/")


def index():
    guid_to_path, all_files = {}, []
    for base, dirs, files in os.walk(ASSETS):
        for f in files:
            p = os.path.join(base, f)
            if f.endswith(".meta"):
                try:
                    with open(p, "rb") as h:
                        for line in h:
                            if line.startswith(b"guid: "):
                                guid_to_path[line[6:38].decode()] = p[:-5]
                                break
                except OSError:
                    pass
            else:
                all_files.append(p)
    return guid_to_path, all_files


def edges(path, cache):
    if path in cache:
        return cache[path]
    out = set()
    for p in (path, path + ".meta"):
        try:
            with open(p, "rb") as h:
                head = h.read(5)
                if p == path and head != b"%YAML":
                    continue
                out.update(g.decode() for g in GUID.findall(head + h.read()))
        except OSError:
            pass
    cache[path] = out
    return out


def reach(roots, guid_to_path, cache, cut=frozenset()):
    seen, stack = set(), [r for r in roots if os.path.exists(r)]
    while stack:
        p = stack.pop()
        if p in seen:
            continue
        seen.add(p)
        if os.path.isdir(p):
            continue
        for g in edges(p, cache):
            if g in cut:
                continue
            q = guid_to_path.get(g)
            if q and q not in seen and not os.path.isdir(q):
                stack.append(q)
    return seen


def build_scenes():
    out = []
    with open(os.path.join(PROJECT, "ProjectSettings", "EditorBuildSettings.asset"), "r", encoding="utf-8") as h:
        for line in h:
            line = line.strip()
            if line.startswith("path: "):
                out.append(line[6:])
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default=os.path.join(PROJECT, "Screenshots", "SpellVFX308", "coverage"))
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)
    guid_to_path, all_files = index()
    ab = lambda r: os.path.join(PROJECT, r.replace("/", os.sep))
    main_roots = [ab(s) for s in MAIN_SCENES]
    main_roots += [p for p in all_files if os.sep + "Resources" + os.sep in p and not p.endswith(".cs")]
    main_roots += [p for p in all_files if re.search(r"[\\/]Stream[\\/]Realm_\w+\.unity$", p)]
    lab_roots = [p for p in all_files if p.endswith(".unity") and "SpellLab308" in os.path.basename(p)]
    legacy_roots = [ab(s) for s in build_scenes() if ab(s) not in main_roots]
    set_roots = [guid_to_path[g] for g in OLD_SETS if g in guid_to_path]
    cache = {}
    groups = {"main": reach(main_roots, guid_to_path, cache, frozenset(OLD_SETS)),
              "lab": reach(lab_roots, guid_to_path, cache, frozenset(OLD_SETS)),
              "legacy": reach(legacy_roots, guid_to_path, cache),
              "sets": reach(set_roots, guid_to_path, cache)}
    print("roots main %d lab %d legacy %d sets %d" % (len(main_roots), len(lab_roots), len(legacy_roots), len(set_roots)))
    for k, v in groups.items():
        print("reached", k, len(v))

    # editor builders that name a SpellVFX120 folder by path
    by_path = defaultdict(set)
    for base, _, files in os.walk(EDITOR):
        for f in files:
            if f.endswith(".cs"):
                with open(os.path.join(base, f), "r", encoding="utf-8", errors="replace") as h:
                    for m in re.finditer(r"SpellVFX120/(\w+)", h.read()):
                        by_path[m.group(1)].add(f)

    rows, folders = [], defaultdict(lambda: defaultdict(int))
    for p in all_files:
        if not p.startswith(TARGET + os.sep):
            continue
        hit = [k for k in ("main", "lab", "legacy", "sets") if p in groups[k]]
        tier = "keep" if ("main" in hit or "lab" in hit) else ("B" if hit else "A")
        size = os.path.getsize(p)
        folder = os.path.relpath(p, TARGET).split(os.sep)[0]
        if os.sep not in os.path.relpath(p, TARGET):
            folder = "(root)"
        rows.append((tier, folder, rel(p), size, "+".join(hit) or "-"))
        folders[folder][tier] += size
        folders[folder][tier + "#"] += 1

    rows.sort(key=lambda r: (r[0], r[1], r[2]))
    with open(os.path.join(a.out, "orphans308.csv"), "w", encoding="utf-8-sig", newline="") as h:
        h.write("tier,folder,path,bytes,reached_by\n")
        for r in rows:
            h.write("%s,%s,%s,%d,%s\n" % r)
    mb = lambda b: "%.1f" % (b / 1048576.0)
    lines = ["# orphans308 - Art/SpellVFX120 정리 후보 (읽기만, 지우지 않음)", "",
             "keep = main 또는 lab이 닿음 · A = 아무도 안 닿음 · B = legacy 장면 / 옛 세트 사본만 닿음", "",
             "| 폴더 | keep MB (#) | A MB (#) | B MB (#) | 편집기 빌더가 경로로 씀 |", "|---|---|---|---|---|"]
    tot = defaultdict(int)
    for f in sorted(folders):
        d = folders[f]
        for t in ("keep", "A", "B"):
            tot[t] += d[t]
            tot[t + "#"] += d[t + "#"]
        lines.append("| %s | %s (%d) | %s (%d) | %s (%d) | %s |" % (
            f, mb(d["keep"]), d["keep#"], mb(d["A"]), d["A#"], mb(d["B"]), d["B#"], ", ".join(sorted(by_path.get(f, ()))) or "-"))
    lines.append("| **합계** | %s (%d) | %s (%d) | %s (%d) | |" % (
        mb(tot["keep"]), tot["keep#"], mb(tot["A"]), tot["A#"], mb(tot["B"]), tot["B#"]))
    lines += ["", "뿌리: main = %s + Realm_* + Resources 폴더 전부(옛 세트 사본 3개는 끊음) · lab = SpellLab308 장면 · legacy = 나머지 빌드 장면 · sets = 옛 세트 사본 3개." % ", ".join(os.path.basename(s) for s in MAIN_SCENES),
              "주의: 스크립트가 경로 문자열로 불러오는 에셋(Resources.Load 밖, AssetDatabase 경로)은 그래프에 안 잡힌다 — 오른쪽 열의 빌더를 같이 볼 것. 삭제는 사용자 확인 뒤."]
    with open(os.path.join(a.out, "orphans308.md"), "w", encoding="utf-8", newline="\n") as h:
        h.write("\n".join(lines) + "\n")
    print("\n".join(lines))


if __name__ == "__main__":
    sys.exit(main())
