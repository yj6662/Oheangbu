"""Record the agent's directly viewed stills; does not grant user art approval."""
import hashlib
import json
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
BASE = ROOT / "Art/Characters/Folklore298"
REVIEWED = {
    "bulgasari": ("04", ["Joint seam splits from run-03 are absent in run-04.", "Side-fall death is supported by the torso/limbs.", "Two small black triangles on the right hindquarter also appear in the original source preview_right.png; original geometry is retained.", "Attack/Hit/Stun are restrained head and body motions; full-speed combat readability still needs Unity review."]),
    "fox_spirit": ("04", ["Joint seam splits from run-03 are absent in run-04.", "Side-fall death grounds the torso, but the large source tail remains raised and looks stiff.", "Small black tail/neck marks are also visible in original source previews; no source mesh or texture retouch performed.", "Attack/Hit/Stun are restrained motions; no dedicated jaw animation or facial rig."]),
    "imugi": ("09", ["Whole body remains continuous in all six stills.", "Death now lowers the raised neck and rests the long body on its side.", "Generic Walk is an offline diagnostic only; runtime idle/walk use Head-only CR_Idle plus procedural BodyFollow.", "CR_* clips are numerically checked with stationary follow emulation; actual moving Unity follow remains unverified here."]),
}

def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def main():
    rows = []
    for ident, (run, notes) in REVIEWED.items():
        folder = BASE / "Derivatives" / ident / ("run-" + run)
        receipts = json.loads((folder / "Review/receipts.json").read_text(encoding="utf-8"))
        assert {r["clip"] for r in receipts["captures"]} == {"Idle", "Walk", "Attack", "Hit", "Stun", "Death"}
        for receipt in receipts["captures"]:
            assert sha(folder / "Review" / receipt["path"]) == receipt["sha256"]
        report = json.loads((folder / "rig-report.json").read_text(encoding="utf-8"))
        roundtrip = json.loads((folder / "roundtrip.json").read_text(encoding="utf-8"))
        row = {"id": ident, "run": run, "reviewedAtUtc": datetime.now(timezone.utc).isoformat(),
               "reviewer": "natural_terrain_research; actual view_image calls on every listed image",
               "status": "OFFLINE_CANDIDATE_REVIEWED_WITH_LIMITATIONS", "userArtApproved": False,
               "scope": "Six individual Cycles stills; not video or Unity runtime approval", "notes": notes,
               "captures": receipts["captures"], "candidateSha256": receipts["candidateSha256"],
               "fbxSha256": sha(folder / "creature.fbx"), "sourceSha256": report["sourceSha256"],
               "sourceGeometryUnchanged": report["sourceHashUnchanged"],
               "triangleMaterialUvRoundtripEqual": all(m["triangleMaterialUvSignatureEqual"] for m in roundtrip["meshes"]),
               "maxCoincidentSeamGapM": max(d["maxCoincidentSeamGapM"] for d in report["deformation"]),
               "roundtrip": roundtrip,
               "remainingValidation": ["Actual Unity clips, movement and hit timing", "Slope foot placement", "Animated runtime bounds/culling", "Artist/user final acceptance"]}
        (folder / "Review/visual-review.json").write_text(json.dumps(row, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        rows.append({"id": ident, "review": (folder / "Review/visual-review.json").relative_to(ROOT).as_posix()})
    print(json.dumps(rows, ensure_ascii=False))

if __name__ == "__main__":
    main()
