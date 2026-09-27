"""Cheap, offline #296 bridge/gate ledger comparison; never opens Unity.

Reads the generated receipts, JSON route ledgers, and authoring source text.
Writes only Analysis/bridge-gate-ledger-alignment.json. Geometry, collision,
NavMesh, and gameplay pass/fail remain the responsibility of the live checks.
Run after a completed canonical Crossings -> Gates -> final ledger build.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
from collections import Counter
from datetime import datetime, timezone
from pathlib import Path


EXPECTED_ROUTES = {
    "capital_center__palace", "hyeongang__capital_center",
    "jeokro__cheolong", "hyeongang__temple", "mountain_hwanggyeong_main",
}
EXPECTED_FACT = "hwanggyeong_south_gate"


def xyz(p):
    return (p["x"], p["y"], p["z"])


def distance(a, b):
    return math.dist(xyz(a), xyz(b))


def xz_distance(a, b):
    return math.hypot(a["x"] - b["x"], a["z"] - b["z"])


def ordered_indices(needles, points, epsilon=0.00001):
    found, cursor = [], 0
    for p in needles:
        while cursor < len(points) and distance(p, points[cursor]) > epsilon:
            cursor += 1
        if cursor == len(points):
            return None
        found.append(cursor)
        cursor += 1
    return found


def gate_crossing(points, centre, inward):
    """Closest intersection with the infinite gate plane, reporting XZ and Y."""
    candidates = []
    for a, b in zip(points, points[1:]):
        da = sum((a[k] - centre[k]) * inward[k] for k in ("x", "z"))
        db = sum((b[k] - centre[k]) * inward[k] for k in ("x", "z"))
        if da * db > 0 or abs(da - db) < 1e-12:
            continue
        t = da / (da - db)
        p = {k: a[k] + (b[k] - a[k]) * t for k in ("x", "y", "z")}
        candidates.append(p)
    return min(candidates, key=lambda p: xz_distance(p, centre)) if candidates else None


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[2])
    args = parser.parse_args()
    root = args.root.resolve()
    output_root = root / "Art/World/Compact/Rebuild/Architecture296"
    inputs, checks = {}, []

    def load(relative):
        path = output_root / relative
        raw = path.read_bytes()
        inputs[relative] = {
            "sha256": hashlib.sha256(raw).hexdigest(), "bytes": len(raw),
            "modifiedUtc": datetime.fromtimestamp(path.stat().st_mtime, timezone.utc).isoformat(),
        }
        return json.loads(raw.decode("utf-8-sig"))

    def check(label, passed, **details):
        checks.append({"check": label, "passed": bool(passed), **details})

    bridges = load("crossings.json")["Bridges"]
    loops = load("gates.json")["Loops"]
    crossings = load("Generated/crossings.json")["Crossings"]
    routes = load("Generated/routes.json")["routes"]
    sheet = load("architecture.json")
    route_map = {r["id"]: r for r in routes}
    crossing_map = {r["RouteId"]: r for r in crossings}
    physical_ids = [b["Id"] for b in bridges]
    physical_routes = [r for b in bridges for r in b["RouteIds"]]
    check("four physical bridge IDs", len(physical_ids) == 4 and len(set(physical_ids)) == 4,
          physicalIds=physical_ids)
    check("five unique functional crossing routes", set(physical_routes) == EXPECTED_ROUTES
          and len(physical_routes) == 5, routeCounts=dict(Counter(physical_routes)))
    check("generated crossing route set", len(crossings) == 5 and set(crossing_map) == EXPECTED_ROUTES)
    check("generated full route IDs unique", len(route_map) == len(routes), count=len(routes))
    bridge_records = []
    for b in bridges:
        check(b["Id"] + " scene root", b["SceneRoot"].startswith("Architecture296_Crossings/"),
              sceneRoot=b["SceneRoot"])
        check(b["Id"] + " receipt route list", b["RouteIds"] == [r["Id"] for r in b["Routes"]])
        expected_width = 3.4 if "mountain_hwanggyeong_main" in b["RouteIds"] else 6.0
        check(b["Id"] + " width", abs(b["Width"] - expected_width) < 1e-5,
              width=b["Width"], expected=expected_width)
        route_records = []
        for r in b["Routes"]:
            rid, pts = r["Id"], r["Points"]
            generated = crossing_map.get(rid)
            max_error = max((distance(a, z) for a, z in zip(pts, generated["Points"])), default=0) if generated else None
            check(rid + " crossing points", generated is not None and len(pts) == len(generated["Points"])
                  and max_error <= 1e-5, pointCount=len(pts), maximumErrorMetres=max_error)
            found = ordered_indices(pts, route_map[rid]["points"]) if rid in route_map else None
            check(rid + " ordered points in full route", found is not None)
            check(rid + " crossing width", generated is not None
                  and abs(generated["RouteWidth"] - b["Width"]) < 1e-5)
            route_records.append({"routeId": rid, "pointCount": len(pts), "fullRouteIndices": found,
                                  "maximumCrossingPointErrorMetres": max_error})
        bridge_records.append({k: b[k] for k in ("Id", "Kind", "Width", "SceneRoot", "LodTriangles",
                                               "CollisionTriangles", "CollisionBoxes", "PhysicsScope")}
                              | {"routeAlignment": route_records})
    shared = next((b for b in bridges if b["Id"] == "capital_shared_stone_bridge"), None)
    check("capital shared stone bridge carries both routes", shared is not None and
          set(shared["RouteIds"]) == {"capital_center__palace", "hyeongang__capital_center"})

    perimeter_map = {p["Id"]: p for p in sheet["Perimeters"]}
    check("three loop IDs match architecture sheet", len(loops) == 3
          and {p["Id"] for p in loops} == set(perimeter_map))
    gate_records = []
    for loop in loops:
        perimeter = perimeter_map.get(loop["Id"])
        check(loop["Id"] + " perimeter points", perimeter is not None
              and perimeter["Points"] == loop["Points"])
        check(loop["Id"] + " first gate sheet metadata", perimeter is not None and bool(loop["Gates"])
              and perimeter["GateScenePath"] == loop["Gates"][0]["Path"]
              and perimeter["OpenFact"] == loop["Gates"][0]["OpenFact"]
              and distance(perimeter["GateCentre"], loop["Gates"][0]["Centre"]) <= 1e-5)
        if loop["Id"] == "capital296":
            check("four capital gates use existing south gate fact", len(loop["Gates"]) == 4
                  and all(g["OpenFact"] == EXPECTED_FACT for g in loop["Gates"]))
        for gate in loop["Gates"]:
            record = {k: gate[k] for k in ("Id", "Path", "OpenFact", "Centre", "Width", "Routes")}
            record["routePlaneCrossings"] = []
            for rid in gate["Routes"]:
                hit = gate_crossing(route_map[rid]["points"], gate["Centre"], gate["Inward"]) if rid in route_map else None
                error = xz_distance(hit, gate["Centre"]) if hit else None
                check(gate["Id"] + " / " + rid + " enters gate centre in XZ",
                      hit is not None and error <= 0.001, xzErrorMetres=error)
                record["routePlaneCrossings"].append({"routeId": rid, "point": hit, "xzErrorMetres": error,
                    "guideYMinusThresholdMetres": hit["y"] - gate["Centre"]["y"] if hit else None})
            if not gate["OpenFact"]:
                arena_id = loop["Id"].removesuffix("_precinct296")
                arena = next((a for a in sheet["Arenas"] if a["Id"] == arena_id), None)
                nearest = min(arena["Approach"], key=lambda p: xz_distance(p, gate["Centre"])) if arena else None
                check(gate["Id"] + " open precinct matches actual arena approach", nearest is not None
                      and distance(nearest, gate["Centre"]) <= 0.001, approachPoint=nearest)
            gate_records.append(record)
    south = next((g for loop in loops for g in loop["Gates"] if g["Id"] == "south_gate"), None)
    check("southbound Jeokro route shares canonical south gate", south is not None and
          {"south_gate__capital_center", "capital_center__jeokro"}.issubset(south["Routes"]))

    source_root = root / "Oheangbu/Assets/_Project/Scripts/Editor/WorldMacro"
    terms = {
        "CompactArchitecture296.Gates.cs": ["shoulder=", "float SupportHeight", "float edge=", "float ends=",
            "Mathf.CeilToInt(width/.65f)", "batch.Finish(holder,id+", "WallSolid"],
        "CompactArchitecture296.Crossings.cs": ["float poleWidth=", "poleScale=", "PierCollision",
            "apronCaps.Finish", "MasonryFacing(", "Mathf.CeilToInt(width/3.6f)"],
    }
    excerpts = []
    for filename, needles in terms.items():
        path = source_root / filename
        raw = path.read_bytes()
        excerpts.append({"path": path.relative_to(root).as_posix(),
            "sha256": hashlib.sha256(raw).hexdigest(),
            "lines": [{"line": i, "text": line.strip()} for i, line in enumerate(raw.decode("utf-8-sig").splitlines(), 1)
                      if any(needle in line for needle in needles)]})
    failed = [c for c in checks if not c["passed"]]
    result = {
        "generatedUtc": datetime.now(timezone.utc).isoformat(),
        "scope": "Offline ledger and authoring-source snapshot. No Unity calls or scene/asset writes.",
        "limitations": ["JSON receipts do not prove live scene geometry, ordinary CC passage, or WheelCollider driving.",
            "Route guide Y differs from some gate thresholds; live common paving is tested separately.",
            "Source excerpts document the columns/support policy and are not evidence of rendered or colliding geometry."],
        "inputs": inputs, "passed": not failed, "checkCount": len(checks), "failureCount": len(failed),
        "checks": checks, "bridges": bridge_records, "gates": gate_records,
        "totals": {"lodTriangles": [sum(b["LodTriangles"][i] for b in bridges) for i in range(3)],
                   "collisionTriangles": sum(b["CollisionTriangles"] for b in bridges),
                   "collisionBoxes": sum(b["CollisionBoxes"] for b in bridges)},
        "geometryPolicySourceSnapshot": excerpts,
    }
    output = output_root / "Analysis/bridge-gate-ledger-alignment.json"
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"passed": result["passed"], "checks": len(checks), "failures": failed,
                      "output": str(output)}, ensure_ascii=False))
    return 0 if not failed else 1


if __name__ == "__main__":
    raise SystemExit(main())
