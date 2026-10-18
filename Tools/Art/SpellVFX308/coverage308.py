"""SPEC-SPELL-VFX-OVERHAUL-308 coverage census (read only, no editor).

For every letter of the live SpellVisualSet_120 (Architecture296 copy): the set's FxPrefab -> its Vfx120Effect.Profile ->
each picture slot of that profile, named by the prefab it points at. A slot is "308" when the prefab name starts with
PF_V308, "old" when it points at anything else, "-" when empty. Joined with the vocabulary CSV (분류 / 효과) so the gaps
read per category. Writes a CSV and a short markdown summary.

    python Tools/Art/SpellVFX308/coverage308.py [--out DIR]
"""
import argparse
import csv
import os
import re
import sys
from collections import Counter, defaultdict

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
ASSETS = os.path.join(ROOT, "Oheangbu", "Assets")
LIVE_SET = os.path.join(ASSETS, "_Project", "Art", "World", "Architecture296", "Data",
                        "9f55ff4897c35bf408434d2fd4cd5dbc_278c3cdf45b3ab24fb93a8f33e2264e2_9ba170fd23c53dc418707872eb7b2056_SpellVisualSet_120.asset")
CSV_PATH = os.path.join(ROOT, "Docs", "오행부_작도어휘_v0_1.csv")
EFFECT_GUID = "e6e7730f74bc81e4abba7acfff16b982"  # Vfx120Effect.cs

# picture slots of Vfx120Profile, in the order a cast meets them
SLOTS = ["NativeCastPrefab", "Bolt300Prefab", "NativeBodyPrefab", "Aura308Prefab", "Guard308Prefab", "Zone308Prefab",
         "NativeFieldPrefab", "BotanicalPrefab", "SummonPrefab", "CompanionShot308Prefab", "SwordStrike308Prefab",
         "Derived308Prefab", "Mark308Prefab", "Burst308Prefab", "AreaContactPrefab", "NativeImpactPrefab",
         "GuardContactPrefab", "AreaStainPrefab"]
FLAGS = ["UseOriginalKtp", "KtpEmphasis", "NativeReplaceBody", "Aura308Lasting", "Aura308ByWardRadius", "AreaKeepShape308",
         "Bolt300Pierce", "Bolt300FollowTarget", "Bolt300Return", "Bolt300ReturnToCaster",
         "WoodDeerPresentation", "FireHaetaePresentation", "MetalTigerPresentation", "DokkaebiClubPresentation",
         "WaterTurtlePresentation", "StoneDokkaebiPresentation"]
ENUMS = ["Behavior", "Layout", "AreaRemake", "WardKind", "NativeBodyMotion", "BotanicalKind"]

REF = re.compile(r"\{fileID: (-?\d+)(?:, guid: ([0-9a-f]{32}), type: \d+)?\}")


def guid_map():
    out = {}
    for base, _, files in os.walk(os.path.join(ASSETS, "_Project")):
        for f in files:
            if not f.endswith(".meta"):
                continue
            p = os.path.join(base, f)
            try:
                with open(p, "r", encoding="utf-8", errors="replace") as h:
                    for line in h:
                        if line.startswith("guid: "):
                            out[line[6:].strip()] = p[:-5]
                            break
            except OSError:
                pass
    return out


def read(p):
    with open(p, "r", encoding="utf-8", errors="replace") as h:
        return h.read()


def set_entries(path):
    text = read(path)
    out = []
    for m in re.finditer(r'- Letter: "(\\u[0-9A-Fa-f]{4}|.)"\s*\n\s*FxPrefab: (\{[^}]*\})', text):
        raw = m.group(1)
        letter = chr(int(raw[2:], 16)) if raw.startswith("\\u") else raw
        r = REF.search(m.group(2))
        out.append((letter, r.group(2) if r else None))
    return out


def profile_of(prefab_path, gm, depth=0):
    """Vfx120Effect.Profile guid: the component block, or a variant's modification, or the base prefab's."""
    if not prefab_path or not os.path.exists(prefab_path) or depth > 4:
        return None
    text = read(prefab_path)
    for block in text.split("--- !u!")[1:]:
        if "m_Script: {fileID: 11500000, guid: " + EFFECT_GUID in block:
            m = re.search(r"\n  Profile: (\{[^}]*\})", block)
            if m:
                r = REF.search(m.group(1))
                if r and r.group(2):
                    return r.group(2)
    m = re.search(r"propertyPath: Profile\s*\n\s*value:[^\n]*\n\s*objectReference: (\{[^}]*\})", text)
    if m:
        r = REF.search(m.group(1))
        if r and r.group(2):
            return r.group(2)
    m = re.search(r"m_SourcePrefab: (\{[^}]*\})", text)
    if m:
        r = REF.search(m.group(1))
        if r and r.group(2) in gm:
            return profile_of(gm[r.group(2)], gm, depth + 1)
    return None


def profile_fields(path):
    text = read(path)
    f = {}
    for name in SLOTS:
        m = re.search(r"\n  " + name + r": (\{[^}]*\})", text)
        r = REF.search(m.group(1)) if m else None
        f[name] = r.group(2) if r and r.group(2) else None
    for name in FLAGS + ENUMS:
        m = re.search(r"\n  " + name + r": (-?\d+)", text)
        f[name] = int(m.group(1)) if m else None
    return f


def is308(word):
    # PF_V308* bodies, and the #308 build's own derived / companion-shot prefabs (…_308l_derived, …_308h_shot)
    return word.startswith("PF_V308") or bool(re.search(r"_308[a-z]_", word))


def route(row):
    """Where the game actually takes this letter's picture from (SPEC 11.53 · 11.65 · D58)."""
    c, j, prof = row["class"], row["jong"], row["profile"]
    if c == "공백":
        return "not spawned: Blank -> 헛먹 (11.53)"
    if c == "공격" and j == "ㄱ" and "_308" not in prof:
        return "EAGiyeok Root/Burn/Pierce/Return = 308e (11.45, D53)"
    if c == "버프" and j == "ㄱ" and "_308" not in prof:
        return "EABuffs PresentationPrefabs = 308f (11.55, D54)"
    if c == "방벽":
        return "EAWards PresentationPrefabs = 308f rim line (11.49 · 11.55)"
    if c == "소환":
        return "DemoSummonCombat ink body (11.52 · 11.47)"
    if c == "필드" and row["Burst308Prefab"] != "-":
        return "Burst seam -> Burst308 (11.65, D58)"
    return "set -> Vfx120Effect"


def slot_word(guid, gm):
    if not guid:
        return "-"
    p = gm.get(guid)
    if not p:
        return "?missing"
    n = os.path.splitext(os.path.basename(p))[0]
    return n


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default=os.path.join(ROOT, "Oheangbu", "Screenshots", "SpellVFX308", "coverage"))
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)
    gm = guid_map()
    vocab = {}
    with open(CSV_PATH, "r", encoding="utf-8-sig") as h:
        for row in csv.DictReader(h):
            vocab[row["글자"]] = row
    rows = []
    for letter, fx in set_entries(LIVE_SET):
        fxp = gm.get(fx)
        prof_guid = profile_of(fxp, gm)
        prof_path = gm.get(prof_guid)
        fields = profile_fields(prof_path) if prof_path else {}
        v = vocab.get(letter, {})
        row = {"letter": letter, "class": v.get("분류", "?"), "element": v.get("속성", "?"), "frame": v.get("프레임", "?"),
               "jong": v.get("종성", "?"), "fx": os.path.splitext(os.path.basename(fxp))[0] if fxp else "?",
               "profile": os.path.basename(prof_path) if prof_path else "?"}
        n308 = nold = 0
        for s in SLOTS:
            w = slot_word(fields.get(s), gm)
            row[s] = w
            if is308(w):
                n308 += 1
            elif w != "-":
                nold += 1
        for s in FLAGS + ENUMS:
            row[s] = fields.get(s)
        row["n308"] = n308
        row["nold"] = nold
        row["effect"] = v.get("효과", "")
        row["route"] = route(row)
        rows.append(row)
    cols = ["letter", "class", "element", "frame", "jong", "fx", "profile", "route", "n308", "nold"] + SLOTS + FLAGS + ENUMS + ["effect"]
    with open(os.path.join(a.out, "coverage308.csv"), "w", encoding="utf-8-sig", newline="") as h:
        w = csv.DictWriter(h, fieldnames=cols)
        w.writeheader()
        for r in rows:
            w.writerow(r)
    # summary
    lines = ["# coverage308 - live set %d letters" % len(rows), ""]
    by = defaultdict(list)
    for r in rows:
        by[r["class"]].append(r)
    lines.append("| 분류 | 글자 수 | 308 슬롯 0 | 옛 슬롯 있음 |")
    lines.append("|---|---|---|---|")
    for k, rs in sorted(by.items()):
        z = "".join(r["letter"] for r in rs if r["n308"] == 0)
        o = "".join(r["letter"] for r in rs if r["nold"] > 0)
        lines.append("| %s | %d | %s | %s |" % (k, len(rs), z or "-", o or "-"))
    lines.append("")
    old = Counter()
    for r in rows:
        for s in SLOTS:
            if r[s] not in ("-",) and not is308(r[s]):
                old[s] += 1
    lines.append("옛 프리팹을 가리키는 슬롯: " + ", ".join("%s %d" % kv for kv in old.most_common()))
    lines.append("")
    lines.append("| 경로 | 글자 |")
    lines.append("|---|---|")
    byr = defaultdict(list)
    for r in rows:
        byr[r["route"]].append(r["letter"])
    for k, ls in sorted(byr.items()):
        lines.append("| %s | %s (%d) |" % (k, "".join(ls), len(ls)))
    lines.append("")
    gaps = [r for r in rows if r["route"] == "set -> Vfx120Effect" and r["n308"] == 0]
    lines.append("set 경로인데 308 슬롯 0: " + ("".join(r["letter"] for r in gaps) or "없음"))
    lines.append("")
    for r in rows:
        if r["n308"] == 0 or r["nold"] > 0:
            used = ["%s=%s" % (s.replace("Prefab", ""), r[s]) for s in SLOTS if r[s] != "-"]
            lines.append("- %s (%s %s %s) %s | %s | %s" % (r["letter"], r["class"], r["element"], r["jong"], r["profile"], r["route"], "; ".join(used) or "(빈 프로필)"))
    with open(os.path.join(a.out, "coverage308.md"), "w", encoding="utf-8", newline="\n") as h:
        h.write("\n".join(lines) + "\n")
    print("\n".join(lines[:20]))
    print("rows", len(rows), "->", a.out)


if __name__ == "__main__":
    sys.exit(main())
