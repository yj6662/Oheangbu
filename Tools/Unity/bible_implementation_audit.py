"""Read-only Bible/candidate inventory; writes JSON reports, never modifies Unity or saves.

This does not certify gameplay or automatically change the curated implementation tracker.
"""
import hashlib
import json
import re
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "Art/World/Compact/Rebuild/BibleTracking"


def run():
    bibles = sorted((ROOT / "Docs").glob("*_Bible_v0_1.md"))
    bibles += [ROOT / "Docs/오행부_LDB_v0_6.md"]
    sections = []
    for path in bibles:
        for number, line in enumerate(path.read_text(encoding="utf-8-sig").splitlines(), 1):
            if line.startswith("## "):
                sections.append({"path": path.relative_to(ROOT).as_posix(), "line": number,
                                 "heading": line, "ids": re.findall(r"\[([A-Z]+-[A-Z/-]+)\]", line)})
    receipt = json.loads((ROOT / "Art/World/Compact/Rebuild/migration_slice.json").read_text(encoding="utf-8-sig"))
    scene = ROOT / "Oheangbu" / receipt["scene"]
    campaign = scene.parent / "Village245/Campaign.asset"
    stages = []
    stage_section = re.search(r"^  Stages:\s*\n(.*?)(?=^  [A-Za-z]\w*:|\Z)",
                              campaign.read_text(encoding="utf-8-sig"), re.M | re.S)
    if stage_section is None:
        raise ValueError("Campaign has no Stages section; cannot report implementation flags")
    for match in re.finditer(r"^  - Id: ([^\n]+)\n(.*?)(?=^  - Id:|\Z)", stage_section[1], re.M | re.S):
        enabled = re.search(r"^    Implemented: (\d+)", match[2], re.M)
        stages.append({"id": match[1].strip(), "implemented_flag": bool(int(enabled[1])) if enabled else None})
    paths = bibles + [ROOT / "Docs/Plans/PLAN-BIBLE-IMPLEMENTATION-TRACKER.md", scene, campaign,
                     scene.parent / "Content.asset", scene.parent / "Map.asset", scene.parent / "Navigation.asset",
                     ROOT / "Oheangbu/Assets/_Project/Art/World/WorldCompact/Rebuild/WorldLayout.asset",
                     ROOT / "Art/World/Compact/Rebuild/Vehicle250/bible-bindings.txt"]
    # Watching sources detects changes, but does not give earlier fixtures new validity.
    paths += list((ROOT / "Docs/Specs").glob("*.md"))
    paths += list((ROOT / "Oheangbu/Assets/_Project/Scripts").rglob("*.cs"))
    paths += list((ROOT / "Oheangbu/Assets/_Project").rglob("*.prefab"))
    inputs = {p.relative_to(ROOT).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest()
              for p in sorted(set(paths)) if p.is_file()}
    OUT.mkdir(parents=True, exist_ok=True)
    latest = OUT / "latest.json"
    old = json.loads(latest.read_text(encoding="utf-8")) if latest.exists() else {}
    prior = old.get("inputs", {})
    changed = sorted(k for k in inputs.keys() | prior.keys() if inputs.get(k) != prior.get(k))
    report = {"checked_utc": datetime.now(timezone.utc).isoformat(),
              "scope": "Static source inventory; flags are not scene connectivity or gameplay completion",
              "baseline_created": not bool(old), "changed_inputs": changed,
              "bible_sections": sections, "candidate_scene": receipt["scene"],
              "campaign_stages": stages, "inputs": inputs}
    latest.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps({"sections": len(sections), "stages": len(stages),
                      "enabled_flags": sum(s["implemented_flag"] is True for s in stages),
                      "baseline_created": not bool(old), "changed_inputs": len(changed)}, ensure_ascii=False))


if __name__ == "__main__":
    run()
