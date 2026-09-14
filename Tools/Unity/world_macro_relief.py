"""Compare potential terrain relief beside fixed V2 road samples.

This is a coarse terrain diagnostic, not a renderer, raycast or visual pass gate.
Only the explicitly requested JSON report is written. Unity and source data are
never changed. Run again after exporting a revised sheet and terrain grid.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
from pathlib import Path
from statistics import mean


ROOT = Path(__file__).resolve().parents[2]
DEFAULT_CURRENT = ROOT / "Art/World/WorldMacro"
SPACING = 160.0
EYE_HEIGHT = 1.8
ANGLES = tuple(range(35, 146, 10))
DISTANCES = tuple(range(120, 1200, 80)) + (1200,)
MIN_RISE = 10.0
MIN_ANGLE = 2.0
MIN_COVERAGE = 0.20
MIN_VALID_RAYS = 6
MIN_VALID_DISTANCES = 3


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def q(value):
    return round(value, 4) if value is not None else None


def distribution(values):
    values = sorted(x for x in values if x is not None)
    if not values:
        return {"count": 0}
    def percentile(t):
        i = (len(values) - 1) * t
        a = int(i)
        return values[a] + (values[min(a + 1, len(values) - 1)] - values[a]) * (i - a)
    return {"count": len(values), "min": q(values[0]), "p10": q(percentile(.1)),
            "median": q(percentile(.5)), "p90": q(percentile(.9)),
            "max": q(values[-1]), "mean": q(mean(values))}


class Grid:
    def __init__(self, data):
        self.data = data
        self.step = data["step"]
        self.cols = data["cols"]
        self.rows = data["rows"]
        if len(data["heights"]) != self.cols * self.rows:
            raise ValueError("terrain grid dimensions and height count disagree")
        if len(data["inside"]) != len(data["heights"]):
            raise ValueError("terrain grid inside mask count disagrees")

    def at(self, x, z):
        gx = (x - self.data["minX"]) / self.step
        gz = (z - self.data["minZ"]) / self.step
        if gx < 0 or gz < 0 or gx > self.cols - 1 or gz > self.rows - 1:
            return None
        ix, iz = min(int(gx), self.cols - 2), min(int(gz), self.rows - 2)
        ids = (iz * self.cols + ix, iz * self.cols + ix + 1,
               (iz + 1) * self.cols + ix, (iz + 1) * self.cols + ix + 1)
        # Never interpolate across the exported mainland boundary into sea/outside.
        if not all(self.data["inside"][i] for i in ids):
            return None
        a, b, c, d = (self.data["heights"][i] for i in ids)
        fx, fz = gx - ix, gz - iz
        return (a + (b - a) * fx) * (1 - fz) + (c + (d - c) * fx) * fz


def route_samples(route):
    """Planar chainage keeps reference locations independent of terrain edits."""
    points = route["Points"]
    segments = []
    distance = 0.0
    for a, b in zip(points, points[1:]):
        dx, dz = b["x"] - a["x"], b["z"] - a["z"]
        length = math.hypot(dx, dz)
        if length > 1e-6:
            segments.append((distance, distance + length, a, b, dx / length, dz / length))
            distance += length
    if not segments:
        raise ValueError(f"route has no usable horizontal segments: {route['Id']}")
    chainages = [i * SPACING for i in range(int(distance // SPACING) + 1)]
    if distance - chainages[-1] > 1e-4:
        chainages.append(distance)
    cursor = 0
    samples = []
    for s in chainages:
        while cursor < len(segments) - 1 and s > segments[cursor][1]:
            cursor += 1
        start, end, a, b, tx, tz = segments[cursor]
        t = (s - start) / (end - start)
        samples.append({"chainageM": s, "x": a["x"] + (b["x"] - a["x"]) * t,
                        "z": a["z"] + (b["z"] - a["z"]) * t,
                        "referenceY": a["y"] + (b["y"] - a["y"]) * t,
                        "forwardX": tx, "forwardZ": tz})
    return samples, distance


def closest_route_y(route, x, z):
    best_distance2, best_y = math.inf, None
    if route:
        for a, b in zip(route["Points"], route["Points"][1:]):
            dx, dz = b["x"] - a["x"], b["z"] - a["z"]
            length2 = dx * dx + dz * dz
            t = max(0., min(1., ((x - a["x"]) * dx + (z - a["z"]) * dz) / length2)) if length2 else 0.
            error2 = (x - a["x"] - t * dx) ** 2 + (z - a["z"] - t * dz) ** 2
            if error2 < best_distance2:
                best_distance2 = error2
                best_y = a["y"] + t * (b["y"] - a["y"])
    return best_y, math.sqrt(best_distance2)


def inside_polygon(x, z, polygon):
    inside = False
    j = len(polygon) - 1
    for i, p in enumerate(polygon):
        a = polygon[j]
        if (p["y"] > z) != (a["y"] > z):
            cross = (a["x"] - p["x"]) * (z - p["y"]) / (a["y"] - p["y"]) + p["x"]
            if x < cross:
                inside = not inside
        j = i
    return inside


def region_at(sample, regions):
    for region in regions:
        if inside_polygon(sample["x"], sample["z"], region["Polygon"]):
            return region["Id"]
    return "OutsideDefinedRegions"


def side_relief(grid, sample, surface_y, sign):
    if surface_y is None:
        return {"hasRelief": None, "coverage": 0., "qualifyingRayCount": 0,
                "validRayCount": 0, "reason": "observer height unavailable"}
    # x-right, z-forward: heading +35..145 degrees is right; negative is left.
    heading = math.atan2(sample["forwardX"], sample["forwardZ"])
    ray_results = []
    for angle in ANGLES:
        azimuth = heading + math.radians(sign * angle)
        valid = []
        for distance in DISTANCES:
            height = grid.at(sample["x"] + math.sin(azimuth) * distance,
                             sample["z"] + math.cos(azimuth) * distance)
            if height is None:
                continue
            rise = height - surface_y
            elevation = math.degrees(math.atan2(rise - EYE_HEIGHT, distance))
            valid.append((rise, elevation, distance))
        qualifying = [v for v in valid if v[0] >= MIN_RISE and v[1] >= MIN_ANGLE]
        best = max(valid, key=lambda v: v[1]) if valid else (None, None, None)
        ray_results.append({"angleOffsetDegrees": sign * angle,
                            "validDistanceCount": len(valid),
                            "qualifies": len(valid) >= MIN_VALID_DISTANCES and bool(qualifying),
                            "nearestQualifiedDistanceM": min((v[2] for v in qualifying), default=None),
                            "maxRiseM": q(max(v[0] for v in valid)) if valid else None,
                            "maxElevationDegrees": q(best[1]), "bestDistanceM": best[2]})
    valid_rays = sum(r["validDistanceCount"] >= MIN_VALID_DISTANCES for r in ray_results)
    qualifying_rays = sum(r["qualifies"] for r in ray_results)
    coverage = qualifying_rays / len(ANGLES)
    return {"hasRelief": coverage >= MIN_COVERAGE if valid_rays >= MIN_VALID_RAYS else None,
            "coverage": q(coverage), "qualifyingRayCount": qualifying_rays,
            "validRayCount": valid_rays,
            "nearestQualifiedDistanceM": min((r["nearestQualifiedDistanceM"] for r in ray_results if r["nearestQualifiedDistanceM"] is not None), default=None),
            "maxRiseM": max((r["maxRiseM"] for r in ray_results if r["maxRiseM"] is not None), default=None),
            "maxElevationDegrees": max((r["maxElevationDegrees"] for r in ray_results if r["maxElevationDegrees"] is not None), default=None),
            "rays": ray_results}


def observe(grid, sample, route, reference=False):
    terrain_y = grid.at(sample["x"], sample["z"])
    route_y, route_offset = closest_route_y(route, sample["x"], sample["z"])
    # Same-XZ road heights are preferable to the 80m grid: narrow cut roads and
    # bridges are deliberately not represented accurately by that coarse grid.
    if reference:
        surface_y, source = sample["referenceY"], "baseline_sheet_route"
    elif route_y is not None and route_offset <= .01:
        surface_y, source = route_y, "current_sheet_route_same_xz"
    else:
        surface_y, source = terrain_y, "current_grid_at_fixed_baseline_xz"
    return {"observerSurfaceY": q(surface_y), "observerHeightSource": source,
            "routeY": q(route_y), "routeHorizontalOffsetM": q(route_offset) if math.isfinite(route_offset) else None,
            "terrainY": q(terrain_y),
            "routeMinusTerrainM": q(route_y - terrain_y) if route_y is not None and terrain_y is not None else None,
            "left": side_relief(grid, sample, surface_y, -1),
            "right": side_relief(grid, sample, surface_y, 1)}


def aggregate(samples, version):
    values = [s[version] for s in samples]
    n = len(values)
    left = sum(v["left"]["hasRelief"] is True for v in values)
    right = sum(v["right"]["hasRelief"] is True for v in values)
    both = sum(v["left"]["hasRelief"] is True and v["right"]["hasRelief"] is True for v in values)
    neither = sum(v["left"]["hasRelief"] is False and v["right"]["hasRelief"] is False for v in values)
    unknown = sum(v["left"]["hasRelief"] is None or v["right"]["hasRelief"] is None for v in values)
    return {"sampleCount": n, "leftReliefRatio": q(left / n) if n else None,
            "rightReliefRatio": q(right / n) if n else None,
            "bothReliefRatio": q(both / n) if n else None,
            "eitherReliefRatio": q((left + right - both) / n) if n else None,
            "neitherReliefRatio": q(neither / n) if n else None,
            "unknownSideSampleCount": unknown,
            "leftSectorCoverage": distribution(v["left"]["coverage"] for v in values),
            "rightSectorCoverage": distribution(v["right"]["coverage"] for v in values),
            "leftNearestReliefDistanceM": distribution(v["left"].get("nearestQualifiedDistanceM") for v in values),
            "rightNearestReliefDistanceM": distribution(v["right"].get("nearestQualifiedDistanceM") for v in values),
            "observerGroundY": distribution(v["observerSurfaceY"] for v in values),
            "coarseTerrainY": distribution(v["terrainY"] for v in values),
            "routeMinusCoarseTerrainM": distribution(v["routeMinusTerrainM"] for v in values)}


def gaps(samples, version, length):
    selectors = {
        "left": lambda s: s[version]["left"]["hasRelief"] is False,
        "right": lambda s: s[version]["right"]["hasRelief"] is False,
        "bothAbsent": lambda s: s[version]["left"]["hasRelief"] is False and s[version]["right"]["hasRelief"] is False,
        "unknown": lambda s: s[version]["left"]["hasRelief"] is None or s[version]["right"]["hasRelief"] is None,
    }
    result = {}
    for side, predicate in selectors.items():
        intervals, start = [], None
        for i in range(len(samples) + 1):
            active = i < len(samples) and predicate(samples[i])
            if active and start is None:
                start = i
            if not active and start is not None:
                end = i - 1
                a = 0. if start == 0 else (samples[start - 1]["chainageM"] + samples[start]["chainageM"]) * .5
                b = length if end == len(samples) - 1 else (samples[end]["chainageM"] + samples[end + 1]["chainageM"]) * .5
                intervals.append({"startChainageM": q(a), "endChainageM": q(b),
                                  "estimatedLengthM": q(b - a), "sampleCount": end - start + 1})
                start = None
        result[side] = intervals
    return result


def path_shape(route):
    return [(p["x"], p["z"]) for p in route["Points"]]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--current", type=Path, default=DEFAULT_CURRENT)
    parser.add_argument("--baseline", type=Path, default=DEFAULT_CURRENT / "Versions/v2_before_route_relief")
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    output = args.output or args.current / "route_relief.json"
    sheet_paths = {v: folder / "sheet.json" for v, folder in (("baseline", args.baseline), ("current", args.current))}
    grid_paths = {v: folder / "terrain_grid.json" for v, folder in (("baseline", args.baseline), ("current", args.current))}
    sheet_bytes = {v: path.read_bytes() for v, path in sheet_paths.items()}
    grid_bytes = {v: path.read_bytes() for v, path in grid_paths.items()}
    sheets = {v: json.loads(blob.decode("utf-8-sig")) for v, blob in sheet_bytes.items()}
    grids = {v: Grid(json.loads(blob.decode("utf-8-sig"))) for v, blob in grid_bytes.items()}
    route_maps = {v: {r["Id"]: r for r in sheet["Routes"]} for v, sheet in sheets.items()}
    report = {
        "schemaVersion": 1, "status": "DIAGNOSTIC_ONLY_NOT_VISUAL_APPROVAL",
        "metric": {"description": "Fixed route-side potential terrain silhouette, not direct rendered visibility.",
                   "sampleSpacingM": SPACING, "sampleSpacingType": "horizontal planar chainage including route endpoints",
                   "distanceMinM": min(DISTANCES), "distanceMaxM": max(DISTANCES), "distanceSamplesM": DISTANCES,
                   "sideSectorDegrees": [35, 145], "angularStepDegrees": 10, "raysPerSide": len(ANGLES),
                   "minRiseM": MIN_RISE, "minElevationDegrees": MIN_ANGLE, "minSectorCoverage": MIN_COVERAGE,
                   "minimumValidRaysPerSide": MIN_VALID_RAYS, "minimumValidDistancesPerRay": MIN_VALID_DISTANCES,
                   "eyeHeightM": EYE_HEIGHT,
                   "sideCriterion": "At least 3 of 12 side rays have a terrain sample >=10m above observer ground and >=2deg above eye; at least 6 rays have >=3 valid distances.",
                   "ratioDenominator": "All baseline-route samples including unknowns; no length weighting; shared endpoints can occur more than once."},
        "comparison": {"mode": "identical_baseline_route_xz_and_forward_samples",
                       "source": {v: {"folder": str(folder.resolve()), "sheetSha256": hashlib.sha256(sheet_bytes[v]).hexdigest(),
                                      "terrainGridSha256": hashlib.sha256(grid_bytes[v]).hexdigest(), "gridStepM": grids[v].step}
                                  for v, folder in (("baseline", args.baseline), ("current", args.current))},
                       "newCurrentRoutesExcluded": sorted(set(route_maps["current"]) - set(route_maps["baseline"])),
                       "missingCurrentRoutes": sorted(set(route_maps["baseline"]) - set(route_maps["current"])),
                       "changedPlanarRoutes": [rid for rid, r in route_maps["baseline"].items()
                                               if rid in route_maps["current"] and path_shape(r) != path_shape(route_maps["current"][rid])]},
        "limitations": [
            "80m exported grid cannot resolve narrow road cuts, shoulders, small hills or local cliffs; bilinear interpolation is not the full Unity terrain.",
            "No buildings, trees, rock props, lighting, shadows, fog or render depth are considered; no occlusion test. Higher terrain is only potential silhouette.",
            "Samples use the fixed V2 route position and local segment direction. Current route Y is used only if a current segment crosses the same XZ within 1cm; otherwise current grid Y is used and labeled.",
            "Route-minus-grid height can be large at bridges or narrow cut roads. It is not a floating-road or walkability failure by itself.",
            "The 20% angular-coverage threshold is a fixed diagnostic floor, not a definition of visually near-continuous mountain enclosure.",
            "Gap lengths use midpoints between 160m samples and are approximate. Main routes and optional return routes are counted independently.",
            "Region assignments are fixed to the baseline polygons; unknown sectors remain explicit and count as not-qualified in aggregate ratios.",
        ], "samples": [],
    }
    route_lengths = {}
    for route in sheets["baseline"]["Routes"]:
        samples, length = route_samples(route)
        route_lengths[route["Id"]] = length
        for index, sample in enumerate(samples):
            record = {**sample, "routeId": route["Id"], "sampleIndex": index,
                      "regionId": region_at(sample, sheets["baseline"]["Regions"])}
            record["baseline"] = observe(grids["baseline"], sample, route, reference=True)
            record["current"] = observe(grids["current"], sample, route_maps["current"].get(route["Id"]))
            report["samples"].append(record)
    for version in ("baseline", "current"):
        summary = {"overall": aggregate(report["samples"], version), "routes": [], "regions": [],
                   "interiorGridHeightsM": distribution(h for h, inside in zip(grids[version].data["heights"], grids[version].data["inside"]) if inside)}
        for rid, length in route_lengths.items():
            subset = [s for s in report["samples"] if s["routeId"] == rid]
            summary["routes"].append({"id": rid, "planarLengthM": q(length), **aggregate(subset, version),
                                      "gaps": gaps(subset, version, length)})
        for rid in sorted({s["regionId"] for s in report["samples"]}):
            summary["regions"].append({"id": rid, **aggregate([s for s in report["samples"] if s["regionId"] == rid], version)})
        report[version] = summary
    ratio_keys = ("leftReliefRatio", "rightReliefRatio", "bothReliefRatio", "eitherReliefRatio", "neitherReliefRatio")
    report["deltas"] = {"overallPercentagePoints": {k: q(100 * (report["current"]["overall"][k] - report["baseline"]["overall"][k])) for k in ratio_keys},
                        "coarseRouteGroundHeightChangeM": distribution(s["current"]["terrainY"] - s["baseline"]["terrainY"] for s in report["samples"] if s["current"]["terrainY"] is not None and s["baseline"]["terrainY"] is not None),
                        "routes": []}
    for before, after in zip(report["baseline"]["routes"], report["current"]["routes"]):
        report["deltas"]["routes"].append({"id": before["id"], **{k + "PercentagePoints": q(100 * (after[k] - before[k])) for k in ratio_keys}})
    report["currentGroundSites"] = [{"id": site["Id"], "sheetY": q(site["Position"]["y"]),
                                     "coarseTerrainY": q(grids["current"].at(site["Position"]["x"], site["Position"]["z"]))}
                                    for site in sheets["current"]["Sites"]]
    # Terrain edits may be accompanied by new switchbacks. Fixed old samples
    # then land off-road; keep those as a geographic comparison, and separately
    # measure the actual new roads without presenting it as paired camera data.
    current_samples = []
    current_lengths = {}
    for route in sheets["current"]["Routes"]:
        samples, length = route_samples(route)
        current_lengths[route["Id"]] = length
        for index, sample in enumerate(samples):
            current_samples.append({**sample, "routeId": route["Id"], "sampleIndex": index,
                                    "regionId": region_at(sample, sheets["baseline"]["Regions"]),
                                    "current": observe(grids["current"], sample, route)})
    current_route_audit = {
        "comparisonMode": "each_version_own_actual_route_samples_not_identical_locations_or_directions",
        "overall": aggregate(current_samples, "current"), "routes": [], "regions": [], "samples": current_samples,
    }
    for rid, length in current_lengths.items():
        subset = [s for s in current_samples if s["routeId"] == rid]
        current_route_audit["routes"].append({"id": rid, "planarLengthM": q(length), **aggregate(subset, "current"),
                                             "gaps": gaps(subset, "current", length)})
    for rid in sorted({s["regionId"] for s in current_samples}):
        current_route_audit["regions"].append({"id": rid, **aggregate([s for s in current_samples if s["regionId"] == rid], "current")})
    report["currentRouteAudit"] = current_route_audit
    report["comparison"]["fixedLocationOffCurrentRouteSampleCount"] = sum(
        s["current"]["observerHeightSource"] == "current_grid_at_fixed_baseline_xz" for s in report["samples"])
    report["comparison"]["primaryPresentation"] = (
        "Compare baseline.overall/routes/regions with currentRouteAudit equivalents. These use each version's actual roads, so sample counts, positions and directions differ. "
        "Top-level current and deltas.overallPercentagePoints retain the separate fixed-old-location terrain comparison; do not call those current-road coverage.")
    report["deltas"]["actualRouteAggregatePercentagePoints"] = {
        k: q(100 * (current_route_audit["overall"][k] - report["baseline"]["overall"][k])) for k in ratio_keys}
    old_route_summaries = {r["id"]: r for r in report["baseline"]["routes"]}
    report["deltas"]["actualRoutes"] = [
        {"id": after["id"], "baselineSampleCount": old_route_summaries[after["id"]]["sampleCount"], "currentSampleCount": after["sampleCount"],
         **{k + "PercentagePoints": q(100 * (after[k] - old_route_summaries[after["id"]][k])) for k in ratio_keys}}
        for after in current_route_audit["routes"] if after["id"] in old_route_summaries]
    # Refuse to mix an export being rewritten by Unity into one comparison.
    for version in ("baseline", "current"):
        source = report["comparison"]["source"][version]
        if source["sheetSha256"] != sha(sheet_paths[version]) or source["terrainGridSha256"] != sha(grid_paths[version]):
            raise RuntimeError("Input changed while measuring; rerun after Unity export finishes")
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"output": str(output.resolve()), "sampleCount": len(report["samples"]),
                      "baseline": report["baseline"]["overall"], "currentActualRoads": current_route_audit["overall"],
                      "deltaActualRouteAggregatePercentagePoints": report["deltas"]["actualRouteAggregatePercentagePoints"],
                      "fixedLocationOffCurrentRouteSampleCount": report["comparison"]["fixedLocationOffCurrentRouteSampleCount"]}, ensure_ascii=False))


if __name__ == "__main__":
    main()
