"""Reconfirm the exact inputs of the existing #296 native mesh validation.

Only hashes current prefab, source export, reduced render and collision inputs.
The previous finite/index/degeneracy result is reused only when every byte is
unchanged. This does not validate placement, physics, navigation or visual art.
"""
from pathlib import Path
from datetime import datetime, timezone
import hashlib
import json

ROOT = Path(__file__).resolve().parents[2]
WORK = ROOT / "Art/World/Compact/Rebuild/Architecture296/Complex"


def sha(path):
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(4 * 1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def run():
    proof_path = WORK / "validation.json"
    proof_hash = sha(proof_path)
    proof = json.loads(proof_path.read_text(encoding="utf-8-sig"))
    checks, files = [], []
    def check(name, passed, detail):
        checks.append(dict(name=name, passed=bool(passed), detail=detail))
    assets = proof.get("assets", [])
    valid = proof.get("version") == 2 and bool(assets)
    for asset in assets:
        levels = asset.get("reducedLevels", [])
        collision = asset.get("collision", {})
        valid = valid and len(levels) == 2 and bool(asset.get("near", {}).get("triangles"))
        valid = valid and asset.get("sourcePrefabSha256") == asset.get("expectedSourcePrefabSha256")
        valid = valid and all(row.get("finite") is True and row.get("degenerate") == 0 and
                              row.get("invalidIndices", 0) == 0 for row in levels + [collision])
        source = None
        for candidate in (WORK / "Source").glob("*.json"):
            with candidate.open(encoding="utf-8-sig") as stream:
                if asset["source"] in stream.read(3000):
                    source = candidate
                    break
        if source is None:
            check("Source export available: " + asset["source"], False, None)
            continue
        for label, path, expected in [
            ("prefab", ROOT / "Oheangbu" / asset["source"], asset["sourcePrefabSha256"]),
            ("source export", source, asset["sourceExportSha256"]),
            ("reduced render", WORK / "Reduced" / source.name, asset["reducedSha256"]),
            ("collision", WORK / "Collision" / source.name, collision["sha256"]),
        ]:
            actual = sha(path) if path.is_file() else None
            row = dict(path=path.relative_to(ROOT).as_posix(), expected=expected, actual=actual)
            files.append(row)
            check(label + " unchanged: " + source.stem, actual == expected, row)
    check("Prior complete geometry validation passed", valid, dict(assets=len(assets), version=proof.get("version")))
    check("Reused validation stayed unchanged while auditing", sha(proof_path) == proof_hash, proof_hash)
    result = dict(checked_utc=datetime.now(timezone.utc).isoformat(), passed=all(c["passed"] for c in checks),
                  scope=__doc__.strip(), reusedValidationUtc=proof.get("utc"), reusedValidationSha256=proof_hash,
                  checks=checks, files=files)
    output = WORK / "reuse-check.json"
    output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Complex input reuse: {sum(c['passed'] for c in checks)}/{len(checks)} PASS")
    for row in checks:
        if not row["passed"]:
            print("FAIL", row["name"], row["detail"])
    return result["passed"]


if __name__ == "__main__":
    raise SystemExit(0 if run() else 1)
