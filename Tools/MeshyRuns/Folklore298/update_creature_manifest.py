"""Register offline rig candidates only; approval remains a separate decision."""
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
BASE = ROOT / "Art/Characters/Folklore298"
JOBS = [
    ("bulgasari", "불가사리", "bulgasari-v2", "04", 3.2, "cheolong", "cheolong"),
    ("fox_spirit", "여우귀물", "fox_spirit-v2", "04", 2.2, "deep_forest", "cheongrim"),
    ("imugi", "이무기", "imugi", "09", 6.0, "arena_hyeongang296", "hyeongang"),
    ("cheongryong", "청룡", "cheongryong", "15", 8.0, None, None),
]

def relative(path):
    return path.relative_to(ROOT).as_posix()

def main():
    path = BASE / "import-manifest.json"
    manifest = json.loads(path.read_text(encoding="utf-8-sig"))
    ids = {row[0] for row in JOBS}
    retained = [row for row in manifest["rows"] if row["id"] not in ids]
    retained_bytes = json.dumps(retained, ensure_ascii=False, sort_keys=True).encode()
    created = []
    for ident, label, source_id, run, length, place, realm in JOBS:
        output = BASE / "Derivatives" / ident / ("run-" + run)
        model = output / "creature.fbx"
        report = json.loads((output / "rig-report.json").read_text(encoding="utf-8"))
        assert report["sourceHashUnchanged"] and report["status"] == "TECHNICAL_CANDIDATE_PASS_PENDING_VISUAL_AND_UNITY"
        assert hashlib.sha256(model.read_bytes()).hexdigest() == report["outputSha256"]["creature.fbx"]
        prefix = "Cheongryong_PrototypeRig" if ident == "cheongryong" else ident + "_Rig298"
        clips = []
        roles = [(r.lower(), r) for r in ("Idle", "Walk", "Attack", "Hit", "Stun", "Death")]
        if ident in ("imugi", "cheongryong"):
            roles[:3] = [("idle", "CR_Idle"), ("walk", "CR_Idle"), ("attack", "CR_HeadAttack_Anticipation")]
            roles.append(("tail", "CR_TailSweep_Anticipation"))
        for role, name in roles:
            clips.append({"role": role, "path": relative(model), "name": prefix + "|" + name,
                          "loop": role in ("idle", "walk", "stun"), "peak01": .5})
        source = BASE / "Source" / source_id
        textures = {"slot": 0, "materialName": "Material_0"}
        for field, suffix in [("baseColor", "base_color"), ("normal", "normal"), ("roughness", "roughness"), ("metallic", "metallic")]:
            texture = source / ("T_0_" + suffix + ".png")
            assert texture.is_file()
            textures[field] = relative(texture)
        row = {"id": ident, "displayName": label, "model": relative(model), "rigType": "Generic",
               "rootBone": "Root", "targetLength": length, "lengthAxis": "z", "approved": False,
               "sourceEvidence": relative(output / "rig-report.json"), "clips": clips, "textures": [textures],
               "rigConfig": relative(BASE / "RigConfigs" / (ident + ".json")),
               "visualReview": relative(output / "Review" / "visual-review.json"),
               "approvalScope": "Offline technical candidate; runtime and final art approval remain pending"}
        if place:
            row.update(placeId=place, realm=realm)
        created.append(row)
    manifest["rows"] = retained + created
    assert retained_bytes == json.dumps([row for row in manifest["rows"] if row["id"] not in ids], ensure_ascii=False, sort_keys=True).encode()
    path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"updated": [row["id"] for row in created], "preservedExistingRows": len(retained), "approved": False}, ensure_ascii=False))

if __name__ == "__main__":
    main()
