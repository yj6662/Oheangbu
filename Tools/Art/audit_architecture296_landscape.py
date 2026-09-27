"""Read-only comparison of 296 clearance ledger and serialized placement arrays.

This independently checks source transforms, preserved order and the sanctuary.
It does not substitute for Unity's collider queries or actual Play traversal.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
import re
import struct
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "Art/World/Compact/Rebuild/Architecture296"
UNITY = ROOT / "Oheangbu"
PRIVATE = "Assets/_Project/Art/World/Architecture296/Data/"


def sha(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def placements(path: Path) -> list[dict]:
    rows: list[dict] = []
    active = False
    current = None
    with path.open(encoding="utf-8-sig") as stream:
        for line in stream:
            if line.startswith("  FixedPlacements:"):
                active = True
                continue
            if not active:
                continue
            if line.startswith("  - Id: "):
                current = {"Id": line.strip()[6:]}
                rows.append(current)
                continue
            if line.startswith("  ") and not line.startswith("    "):
                break
            if current is None or ":" not in line:
                continue
            name, raw = line.strip().split(":", 1)
            raw = raw.strip()
            if name in ("Position", "Euler"):
                current[name] = {k.strip(): float(v) for k, v in
                                 (p.split(":") for p in raw.strip("{}").split(","))}
            elif name == "Scale":
                current[name] = float(raw)
            elif name == "Preserve":
                current[name] = raw == "1"
            else:
                current[name] = raw
    return rows


def same(a: dict, b: dict) -> bool:
    for key in ("Id", "ClusterId", "PrototypeId", "Preserve"):
        if (a.get(key) or "") != (b.get(key) or ""):
            return False
    def same_float(x, y):
        return struct.pack("<f", x) == struct.pack("<f", y)
    if not same_float(a["Scale"], b["Scale"]):
        return False
    return all(same_float(a[k][v], b[k][v])
               for k in ("Position", "Euler") for v in ("x", "y", "z"))


def vec(p):
    return tuple(p[a] for a in "xyz")


def dot(a, b):
    return sum(x * y for x, y in zip(a, b))


def sub(a, b):
    return tuple(x - y for x, y in zip(a, b))


def cross(a, b):
    return (a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0])


def unit(a):
    length = math.sqrt(dot(a, a))
    return tuple(v / length for v in a) if length > 1e-8 else (0, 0, 0)


def corridor_overlap(removal, corridor):
    """Independent 15-axis OBB separation test against the recorded segment."""
    i = removal.get("SegmentIndex", -1)
    points = corridor["Points"]
    if not 0 <= i < len(points) - 1:
        return False
    a, b = vec(points[i]), vec(points[i + 1])
    delta = sub(b, a)
    forward = unit(delta)
    right = unit(cross((0, 1, 0), forward))
    up = cross(forward, right)
    axes_b = (right, up, forward)
    q = removal.get("ProbeRotation", {})
    if any(k not in q for k in "xyzw"):
        return False
    x, y, z, w = (q[k] for k in "xyzw")
    axes_a = ((1-2*(y*y+z*z), 2*(x*y+z*w), 2*(x*z-y*w)),
              (2*(x*y-z*w), 1-2*(x*x+z*z), 2*(y*z+x*w)),
              (2*(x*z+y*w), 2*(y*z-x*w), 1-2*(x*x+y*y)))
    half_a = tuple(v * .5 for v in vec(removal["ProbeSize"]))
    half_b = (corridor["Width"]*.5, corridor["Height"]*.5, (math.sqrt(dot(delta, delta))+.03)*.5)
    centre_b = tuple((a[j]+b[j])*.5 + (corridor["Height"]*.5 if j == 1 else 0) for j in range(3))
    centres = sub(vec(removal["ProbeCentre"]), centre_b)
    for axis in (*axes_a, *axes_b, *(cross(u, v) for u in axes_a for v in axes_b)):
        if dot(axis, axis) < 1e-10:
            continue
        ra = sum(h * abs(dot(axis, u)) for h, u in zip(half_a, axes_a))
        rb = sum(h * abs(dot(axis, u)) for h, u in zip(half_b, axes_b))
        if abs(dot(centres, axis)) >= ra + rb + 1e-4:
            return False
    return True


def corridor_sources(ledger, check):
    if ledger.get("Version", 1) < 2:
        return {}
    inputs = ledger.get("Inputs", [])
    expected = {"crossings.json", "Generated/crossings.json", "Generated/routes.json", "gates.json"}
    if ledger.get("VisualControls"):
        expected.add("Controls/landscape-visual-clearance.json")
    if ledger.get("Compounds"):
        expected.add("venue-complexes.json")
    actual = set()
    valid = True
    for row in inputs:
        path = (UNITY / row["Path"]).resolve()
        try:
            actual.add(path.relative_to(OUT.resolve()).as_posix())
        except ValueError:
            valid = False
        valid &= path.is_file() and sha(path) == row["Sha256"]
    check("passage inputs match current generated receipts", valid and actual == expected, sorted(actual))
    bridges = json.loads((OUT / "crossings.json").read_text(encoding="utf-8-sig"))["Bridges"]
    generated = json.loads((OUT / "Generated/crossings.json").read_text(encoding="utf-8-sig"))["Crossings"]
    route_rows = json.loads((OUT / "Generated/routes.json").read_text(encoding="utf-8-sig"))["routes"]
    gate_rows = json.loads((OUT / "gates.json").read_text(encoding="utf-8-sig"))["Loops"]
    bridge_by_route = {r["Id"]: b for b in bridges for r in b["Routes"]}
    crossings = {r["RouteId"]: r for r in generated}
    routes = {r["id"]: r for r in route_rows}
    gates = {g["Id"]: (loop, g) for loop in gate_rows for g in loop["Gates"]}
    rows = ledger.get("Corridors", [])
    corridors = {r["Id"]: r for r in rows}
    bad = []
    for row in rows:
        points = row["Points"]
        kind = row["Kind"]
        ok = len(points) >= 2 and abs(row["Height"] - 2.4) < 1e-5
        ok &= all(math.isfinite(p[k]) for p in points for k in "xyz")
        if kind.startswith("bridge"):
            bridge, source = bridge_by_route.get(row["RouteId"]), crossings.get(row["RouteId"])
            ok &= bool(bridge and source)
            if bridge and source:
                ok &= row["OwnerId"] == bridge["Id"] and row["ScenePath"] == bridge["SceneRoot"] and abs(row["Width"] - source["RouteWidth"]) < 1e-5
                if kind == "bridge":
                    ok &= points == source["Points"]
                elif kind == "bridge apron":
                    start = row["Id"].endswith(":start")
                    end = source["Points"][0 if start else -1]
                    adjacent = source["Points"][1 if start else -2]
                    direction = unit((adjacent["x"]-end["x"], 0, adjacent["z"]-end["z"]))
                    ok &= len(points) == 7 and all(abs(p[k] - (end[k] - direction[j]*(6-i))) < .001
                        for i, p in enumerate(points) for j, k in ((0, "x"), (2, "z")))
                else:
                    ok = False
        elif kind.startswith("gate"):
            pair = gates.get(row["OwnerId"])
            ok &= bool(pair)
            if pair:
                loop, gate = pair
                ok &= loop["Realm"].lower() != "cheongrim" and row["ScenePath"] == gate["Path"]
                if kind == "gate aperture":
                    direction = unit((gate["Inward"]["x"], 0, gate["Inward"]["z"]))
                    ok &= abs(row["Width"] - gate["Width"]) < 1e-5 and len(points) == 17 and all(
                        abs(p[k] - (gate["Centre"][k]+direction[j]*(i-8))) < .001
                        for i, p in enumerate(points) for j, k in ((0, "x"), (2, "z")))
                elif kind == "gate approach":
                    route = routes.get(row["RouteId"])
                    ok &= row["RouteId"] in gate["Routes"] and bool(route)
                    if route:
                        source = route["points"]
                        near = min(range(len(source)), key=lambda i: dot(sub(vec(source[i]), vec(gate["Centre"])), sub(vec(source[i]), vec(gate["Centre"]))))
                        first = last = near
                        length = 0
                        while first > 0 and length < 60:
                            d = sub(vec(source[first]), vec(source[first-1])); length += math.sqrt(dot(d, d)); first -= 1
                        length = 0
                        while last < len(source)-1 and length < 60:
                            d = sub(vec(source[last]), vec(source[last+1])); length += math.sqrt(dot(d, d)); last += 1
                        expected_points = source[first:last+1]
                        ok &= abs(row["Width"] - min(gate["Width"]-.8, max(2.6, route["width"]))) < 1e-5
                        ok &= len(points) == len(expected_points) and all(
                            p["x"] == q["x"] and p["z"] == q["z"] and p["y"] >= q["y"]-.001
                            for p, q in zip(points, expected_points))
                else:
                    ok = False
        else:
            ok = False
        if not ok:
            bad.append(row["Id"])
    check("precise bridge/gate corridor geometry matches authored sources", not bad and len(rows) == len(corridors), bad)
    check("generated bridge routes all included", {r["RouteId"] for r in rows if r["Kind"] == "bridge"} == set(crossings), len(crossings))
    check("passage owner counts", ledger["BridgeCount"] == len({r["OwnerId"] for r in rows if r["Kind"] == "bridge"}) and
          ledger["GateCount"] == len({r["OwnerId"] for r in rows if r["Kind"] == "gate aperture"}),
          {"bridges": ledger["BridgeCount"], "gates": ledger["GateCount"], "corridors": len(rows)})
    return corridors


def retained_reviewed_tree(ledger):
    """Correct the original visual misclassification using actual source bounds."""
    identity = "292_tree_1130_2678"
    source_path = UNITY / "Assets/_Project/Art/World/Watershed295/Dressing/DryLandscape.asset"
    record = next(r for r in ledger["Sheets"] if r["SourcePath"] == source_path.relative_to(UNITY).as_posix())
    placement = next(p for p in placements(source_path) if p["Id"] == identity)
    actual = next((p for p in placements(UNITY / record["CandidatePath"]) if p["Id"] == identity), None)
    mesh_path = UNITY / "Assets/_Project/Art/World/WorldMacro/Dressing/Meshes/abc00000000003294337539678740007_4300004.asset"
    mesh_text = mesh_path.read_text(encoding="utf-8-sig").split("  m_LocalAABB:\n", 1)[1]
    def xyz(line):
        return {k: float(v) for k, v in re.findall(r"([xyz]): ([^,}]+)", line)}
    centre = xyz(mesh_text.splitlines()[0]);extent = xyz(mesh_text.splitlines()[1])
    prototype = source_path.read_text(encoding="utf-8-sig").split("  - Id: " + placement["PrototypeId"] + "\n", 1)[1].split("    - Parts:", 2)[1]
    local = {a: float(re.search(r"e" + str(i) + r"3: ([-\d.eE+]+)", prototype)[1]) for i, a in enumerate("xyz")}
    for a in "xyz":
        centre[a] += local[a]
    yaw = math.radians(placement["Euler"]["y"]);s=placement["Scale"];cy=math.cos(yaw);sy=math.sin(yaw)
    def rotate(v):
        return (cy*v[0]+sy*v[2],v[1],-sy*v[0]+cy*v[2])
    world_c = tuple(placement["Position"][a]+v for a,v in zip("xyz",rotate(tuple(centre[a]*s for a in "xyz"))))
    world_size = tuple(extent[a]*2*s for a in "xyz")
    shape = {"ProbeCentre":dict(zip("xyz",world_c)),"ProbeSize":dict(zip("xyz",world_size)),
             "ProbeRotation":dict(zip("xyzw",(0,math.sin(yaw/2),0,math.cos(yaw/2))))}
    crossing_path = OUT / "Generated/crossings.json"
    crossing = next(r for r in json.loads(crossing_path.read_text(encoding="utf-8-sig"))["Crossings"] if r["RouteId"] == "jeokro__cheolong")
    corridor = {"Points":crossing["Points"],"Width":crossing["RouteWidth"],"Height":2.4}
    overlaps=[];nearest=(float("inf"),None,None)
    for i,(a,b) in enumerate(zip(corridor["Points"],corridor["Points"][1:])):
        shape["SegmentIndex"]=i
        if corridor_overlap(shape,corridor):overlaps.append(i)
        delta=(b["x"]-a["x"],b["z"]-a["z"]);d2=dot(delta,delta)
        t=max(0,min(1,((placement["Position"]["x"]-a["x"])*delta[0]+(placement["Position"]["z"]-a["z"])*delta[1])/d2)) if d2 else 0
        q=tuple(a[k]+t*(b[k]-a[k]) for k in "xyz");distance=math.hypot(q[0]-placement["Position"]["x"],q[2]-placement["Position"]["z"])
        if distance<nearest[0]:nearest=(distance,i,q)
    evidence=OUT/"Landscape/Reference/bridge-before-canopy-clearance.png"
    meta=json.loads(evidence.with_suffix(".png.json").read_text(encoding="utf-8-sig"));eye=vec(meta["View"]["Eye"])
    forward=unit(sub(vec(meta["View"]["Target"]),eye));right=unit(cross((0,1,0),forward));up=cross(forward,right)
    def project(p):
        d=sub(p,eye);depth=dot(d,forward)
        return (960+dot(d,right)/depth*540/math.tan(math.pi/6),540-dot(d,up)/depth*540/math.tan(math.pi/6),depth)
    projected=[]
    for x in (-1,1):
        for y in (-1,1):
            for z in (-1,1):
                v=rotate(tuple(a*b*.5 for a,b in zip(world_size,(x,y,z))));projected.append(project(tuple(a+b for a,b in zip(world_c,v))))
    passed=actual is not None and same(actual,placement) and not overlaps
    return {"passed":passed,"scope":"Retained foreground tree; capture silhouette overlap is not a 3D passage collision. No clearance widening.",
            "id":identity,"sourcePath":record["SourcePath"],"sourceSha256":sha(source_path),"candidatePath":record["CandidatePath"],
            "candidateRetainedExactly":actual is not None and same(actual,placement),"placement":placement,
            "meshPath":mesh_path.relative_to(UNITY).as_posix(),"meshSha256":sha(mesh_path),"sourceVisualBounds":{"center":centre,"extents":extent},
            "worldProbe":shape,"crossingSha256":sha(crossing_path),"actual3dOverlapSegments":overlaps,
            "nearestRouteXZMetres":nearest[0],"nearestRouteSegment":nearest[1],"nearestRoutePoint":nearest[2],
            "referenceCaptureSha256":sha(evidence),"referenceMetadataSha256":sha(evidence.with_suffix(".png.json")),
            "projection":{"width":1920,"height":1080,"verticalFovDegrees":60,
                          "treePixelMin":[min(p[i] for p in projected) for i in (0,1)],"treePixelMax":[max(p[i] for p in projected) for i in (0,1)],
                          "treeDepthMin":min(p[2] for p in projected),"treeDepthMax":max(p[2] for p in projected),
                          "nearestDeckPixelDepth":project(nearest[2])}}


def run() -> dict:
    ledger_path = OUT / "Landscape/clearance.json"
    ledger = json.loads(ledger_path.read_text(encoding="utf-8-sig"))
    architecture = json.loads((OUT / "architecture.json").read_text(encoding="utf-8-sig"))
    mapping = {r["target"]: r["source"] for r in json.loads(
        (OUT / "cloned-data.json").read_text(encoding="utf-8-sig"))}
    arenas = {a["Id"]: a for a in architecture["Arenas"]}
    sanctuary = arenas["sanctuary296"]
    checks = []

    def check(name, ok, detail):
        checks.append({"name": name, "passed": bool(ok), "details": detail})

    corridors = corridor_sources(ledger, check)
    visual_rows = ledger.get("VisualControls") or []
    visuals = {r["Id"]: r for r in visual_rows}
    compound_rows = ledger.get("Compounds") or []
    compounds = {r["Id"]: r for r in compound_rows}
    if compound_rows:
        complex_source = json.loads((OUT / "venue-complexes.json").read_text(encoding="utf-8-sig"))["Buildings"]
        by_root = {r["SceneRoot"]: r for r in complex_source}
        invalid = []
        for row in compound_rows:
            source = by_root.get(row["SceneRoot"])
            arena = next((a for a in arenas.values() if a["Realm"].lower() == row["Realm"].lower()), None)
            ok = bool(source and arena) and bool(row["ColliderPaths"])
            if source and arena:
                ok &= source["Id"] == row["Id"] and row["SceneRoot"].startswith(arena["SceneRoot"] + "/")
                ok &= all(abs(source["Position"][k] - row["Position"][k]) < 1e-5 for k in "xyz")
                ok &= all(p.startswith(row["SceneRoot"] + "/") or p == row["SceneRoot"] for p in row["ColliderPaths"])
            if not ok:
                invalid.append(row["Id"])
        check("new compound targets are exact recorded child buildings", not invalid and len(by_root) == len(compound_rows), invalid)
    if visual_rows:
        expected_ids = {"292_tree_1178_2630"}
        control_file = json.loads((OUT / "Controls/landscape-visual-clearance.json").read_text(encoding="utf-8-sig"))
        evidence = OUT / "Landscape/Reference/bridge-before-canopy-clearance.png"
        exact = len(visual_rows) == 1 and {r["Placement"]["Id"] for r in visual_rows} == expected_ids
        exact &= len(control_file["Controls"]) == len(visual_rows)
        for row in visual_rows:
            source = next((r for r in control_file["Controls"] if r["Id"] == row["Id"]), None)
            exact &= bool(source) and same(source["Placement"], row["Placement"])
            exact &= row["CorridorId"] == "bridge:jeokro__cheolong" and row["SourcePath"] == "Assets/_Project/Art/World/Watershed295/Dressing/DryLandscape.asset"
            exact &= (UNITY / row["EvidencePath"]).resolve() == evidence.resolve() and evidence.is_file()
            exact &= sha(evidence) == row["EvidenceSha256"] and sha(evidence.with_suffix(".png.json")) == row["CaptureMetadataSha256"]
            exact &= sha(UNITY / row["SourcePath"]) == row["SourceSha256"]
        check("exact single overlapping tree control and immutable capture evidence", exact, sorted(expected_ids))
    def protected(p, core=False):
        key = " ".join((p.get(k) or "") for k in ("Id", "ClusterId", "PrototypeId")).lower()
        if any(k in key for k in ("sinmok", "cheongryong", "sacred")):
            return True
        dx = p["Position"]["x"] - sanctuary["Centre"]["x"]
        dz = p["Position"]["z"] - sanctuary["Centre"]["z"]
        yaw = math.radians(sanctuary["Yaw"])
        x = math.cos(yaw) * dx - math.sin(yaw) * dz
        z = math.sin(yaw) * dx + math.cos(yaw) * dz
        margin = 0 if core else 20
        return abs(x) <= sanctuary["ClearSize"]["x"] / 2 + margin and abs(z) <= sanctuary["ClearSize"]["y"] / 2 + margin

    def source_probe(r):
        bounds = r.get("SourceVisualBounds", {})
        centre = bounds.get("center", bounds.get("m_Center", {}))
        extents = bounds.get("extents", bounds.get("m_Extent", {}))
        if not all(k in centre and k in extents and math.isfinite(centre[k]) and extents[k] > 0 for k in "xyz"):
            return False
        placement = r["Placement"];scale = placement["Scale"];yaw = math.radians(placement["Euler"]["y"])
        local = [centre[k]*scale for k in "xyz"]
        rotated = (math.cos(yaw)*local[0]+math.sin(yaw)*local[2],local[1],-math.sin(yaw)*local[0]+math.cos(yaw)*local[2])
        return all(abs(r["ProbeSize"][k] - extents[k]*2*scale) < .001 for k in "xyz") and all(
            abs(r["ProbeCentre"][k] - placement["Position"][k] - rotated[i]) < .001 for i,k in enumerate("xyz"))

    total = 0
    for record in ledger["Sheets"]:
        target, source = record["CandidatePath"], record["SourcePath"]
        check("private mapped sheet", target.startswith(PRIVATE) and mapping.get(target) == source,
              {"candidate": target, "source": source})
        source_path, target_path = UNITY / source, UNITY / target
        check("original source SHA", sha(source_path) == record["SourceSha256"], source)
        old, new = placements(source_path), placements(target_path)
        removals = {r["SourceIndex"]: r for r in record["Removed"]}
        check("source indices unique and bounded", len(removals) == len(record["Removed"]) and
              all(0 <= i < len(old) for i in removals), target)
        mismatch = [i for i, r in removals.items() if i >= len(old) or not same(old[i], r["Placement"])]
        check("removed transform ledger matches immutable source", not mismatch, mismatch[:10])
        expected = [r for i, r in enumerate(old) if i not in removals]
        mismatch = [i for i, (a, b) in enumerate(zip(expected, new)) if not same(a, b)]
        check("retained order and complete transforms unchanged", len(expected) == len(new) and not mismatch,
              {"expected": len(expected), "actual": len(new), "mismatches": mismatch[:10]})
        protected_bad = [r["Placement"]["Id"] for r in removals.values() if protected(r["Placement"]) and
                         not (r.get("CompoundId") in compounds and not protected(r["Placement"], core=True))]
        check("sanctuary core/sacred retained; outer buffer needs new-building evidence", not protected_bad,
              {"sourceProtected": sum(protected(p) for p in old), "candidateProtected": sum(protected(p) for p in new)})
        bad_reason = []
        bad_overlap = []
        for r in removals.values():
            arena = arenas.get(r["ArenaId"])
            reason = r["Reason"]
            corridor = corridors.get(r.get("CorridorId"))
            compound = compounds.get(r.get("CompoundId"))
            allowed = r["Category"] in ("Tree", "Shrub", "Grass", "Rock") and r["Penetration"] > .02
            if compound:
                allowed &= r["Category"] == "Tree" and r["Penetration"] > .025 and reason == "actual new compound collider penetration"
                allowed &= r["ColliderPath"] in compound["ColliderPaths"] and not protected(r["Placement"], core=True)
                allowed &= source_probe(r)
            elif corridor:
                if r.get("VisualControlId"):
                    visual = visuals.get(r["VisualControlId"])
                    allowed &= bool(visual) and reason == "reviewed canopy/branch passage intersection"
                    if visual:
                        allowed &= same(visual["Placement"], r["Placement"]) and visual["CorridorId"] == r["CorridorId"]
                        allowed &= visual["SourcePath"] == source and r["Category"] == "Tree"
                    allowed &= source_probe(r)
                else:
                    allowed &= reason == "generated " + corridor["Kind"] + " passage"
                if not corridor_overlap(r, corridor):
                    bad_overlap.append(r["Placement"]["Id"])
            else:
                allowed &= arena is not None and arena["Id"] != "sanctuary296" and arena["Realm"].lower() != "cheongrim"
                allowed &= reason in ("authored interior clear volume", "authored combat floor volume", "actual venue collider penetration") or reason.startswith("authored 4m approach segment ")
                if reason == "actual venue collider penetration":
                    allowed &= r["ColliderPath"].startswith(arena["SceneRoot"] + "/") if arena else False
            if not allowed:
                bad_reason.append(r["Placement"]["Id"])
        check("explicit arena/collider/passage removal evidence", not bad_reason, bad_reason[:10])
        if corridors:
            check("independent 15-axis passage overlap", not bad_overlap, bad_overlap[:10])
        check("counts and runtime geometric remainder", record["SourceCount"] == len(old) and
              record["RetainedCount"] == len(new) and record["RemainingConflicts"] == 0,
              {"source": len(old), "retained": len(new), "removed": len(removals)})
        total += len(removals)
    check("ledger totals", total == ledger["Removed"] and ledger["RemainingConflicts"] == 0 and ledger["OriginalSourcesUnchanged"],
          {"removed": total, "activeSheets": len(ledger["Sheets"])})
    if visual_rows:
        removed_visual = [r for s in ledger["Sheets"] for r in s["Removed"] if r.get("VisualControlId")]
        check("visual clearance removes exactly the single overlapping tree", len(removed_visual) == 1 and
              {r["VisualControlId"] for r in removed_visual} == set(visuals), [r["Placement"]["Id"] for r in removed_visual])
    if "ArenaRemovals" in ledger:
        removed = [r for s in ledger["Sheets"] for r in s["Removed"]]
        counts = {"ArenaRemovals": sum(bool(r.get("ArenaId")) for r in removed),
                  "BridgeRemovals": sum(not r.get("VisualControlId") and (r.get("CorridorId") or "").startswith(("bridge:", "apron:")) for r in removed),
                  "GateRemovals": sum((r.get("CorridorId") or "").startswith(("gate:", "gate-route:")) for r in removed),
                  "ReviewedTreeRemovals": sum(bool(r.get("VisualControlId")) for r in removed),
                  "CompoundTreeRemovals": sum(bool(r.get("CompoundId")) for r in removed)}
        check("removal classifications partition the complete ledger", all(ledger[k] == v for k, v in counts.items()) and sum(counts.values()) == ledger["Removed"], counts)
    retained_review = retained_reviewed_tree(ledger)
    check("reviewed foreground tree retained with no 3D passage overlap", retained_review["passed"],
          {"id":retained_review["id"],"nearestRouteXZMetres":retained_review["nearestRouteXZMetres"],"overlaps":retained_review["actual3dOverlapSegments"]})
    return {"passed": all(c["passed"] for c in checks), "scope": "Independent serialized placement and provenance comparison; collider/Play validation remains Unity-owned.",
            "ledgerSha256": sha(ledger_path), "checks": checks, "retainedReviewedTree":retained_review}


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--out", type=Path, default=OUT / "Landscape/independent-checks.json")
    args = parser.parse_args()
    result = run()
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    (args.out.parent / "retained-reviewed-tree.json").write_text(json.dumps(result["retainedReviewedTree"],ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
    passed = sum(c["passed"] for c in result["checks"])
    print(f"{passed}/{len(result['checks'])} PASS; {args.out}")
    raise SystemExit(0 if result["passed"] else 1)
