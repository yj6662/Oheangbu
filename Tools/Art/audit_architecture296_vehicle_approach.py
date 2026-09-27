"""Audit exported #296 vehicle-road geometry; no Unity or source asset writes.

This validates the authored mesh and its immutable inputs. It does not replace
the actual vehicle, walking, NavMesh, visual or performance checks.
"""
from pathlib import Path
import argparse
import hashlib
import json
from datetime import datetime, timezone
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "Art/World/Compact/Rebuild/Architecture296/BridgeApproach"
GEN295 = ROOT / "Art/World/Compact/Rebuild/Watershed295/Generated"


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def xyz(values):
    return np.asarray([[p[k] for k in ("x", "y", "z")] for p in values], dtype=float)


def sample(height, points):
    grid = points[..., [0, 2]] / 4
    index = grid.astype(int)
    x, z = index[..., 0], index[..., 1]
    u, v = grid[..., 0] - x, grid[..., 1] - z
    return np.where(
        u + v <= 1,
        height[z, x] + (height[z, x + 1] - height[z, x]) * u
        + (height[z + 1, x] - height[z, x]) * v,
        height[z + 1, x + 1]
        + (height[z + 1, x] - height[z + 1, x + 1]) * (1 - u)
        + (height[z, x + 1] - height[z + 1, x + 1]) * (1 - v),
    )


def prefix_ribbon(points, count):
    """Independent reconstruction of the original unmodified bridge ribbon."""
    points = points.astype(np.float32)
    rights = []
    up = np.asarray([0, 1, 0], dtype=np.float32)
    def normal(v):
        v = v.copy(); v[1] = 0
        return v / np.sqrt(np.sum(v * v, dtype=np.float32))
    for i in range(count + 1):
        before = normal(points[min(len(points)-1, i+1)] - points[max(0, i-1)])
        tangent = normal(points[i]-points[i-1] if i == len(points)-1 else points[i+1]-points[i])
        right = np.cross(up, before); right /= np.sqrt(np.sum(right * right, dtype=np.float32))
        segment = np.cross(up, tangent); segment /= np.sqrt(np.sum(segment * segment, dtype=np.float32))
        rights.append(right / max(np.float32(.8), np.dot(right, segment)))
    vertices, indices = [], []
    down = np.asarray([0, -.45, 0], dtype=np.float32)
    def quad(a, b, c, d):
        n = len(vertices); vertices.extend([a, b, c, d])
        indices.extend([n, n+1, n+2, n, n+2, n+3])
    for i in range(1, count + 1):
        a = points[i-1] + up * np.float32(.04); b = points[i] + up * np.float32(.04)
        al, ar = a-rights[i-1]*np.float32(3), a+rights[i-1]*np.float32(3)
        bl, br = b-rights[i]*np.float32(3), b+rights[i]*np.float32(3)
        quad(al, bl, br, ar); quad(al+down, ar+down, br+down, bl+down)
        quad(al+down, bl+down, bl, al); quad(br+down, ar+down, ar, br)
        quad(ar+down, al+down, al, ar); quad(bl+down, br+down, br, bl)
    return np.asarray(vertices), np.asarray(indices)


def run(folder=OUT, route="jeokro__cheolong"):
    temple = route == "hyeongang__temple"
    plan = read(folder / f"{route}.json")
    mesh = read(folder / f"{route}-mesh.json")
    heights = np.fromfile(GEN295 / "height.bytes", dtype="<f4").reshape(1501, 1001)
    vertices = xyz(mesh["Vertices"])
    indices = np.asarray(mesh["Triangles"], dtype=np.int64).reshape(-1, 3)
    checks = []

    def check(name, condition, detail):
        checks.append(dict(name=name, passed=bool(condition), detail=detail))

    check("Scope is the one approved shared route", plan["RouteId"] == route
          and mesh["RouteId"] == plan["RouteId"], plan["RouteId"])
    for field, name in [("HeightSha256", "height.bytes"), ("WaterSha256", "waterlevel.bytes"),
                        ("SourceRoutesSha256", "routes.json"), ("SourceCrossingsSha256", "crossings.json")]:
        check(f"Immutable 295 input {name}", plan[field] == sha(GEN295 / name), plan[field])
    check("All mesh data finite and indexed", np.isfinite(vertices).all()
          and indices.min() >= 0 and indices.max() < len(vertices),
          dict(vertices=len(vertices), triangles=len(indices)))
    packed = vertices.astype("<f4").tobytes() + indices.astype("<i4").tobytes()
    check("Exported physical mesh fingerprint", hashlib.sha256(packed).hexdigest() == plan["SurfaceMeshSha256"],
          hashlib.sha256(packed).hexdigest())
    rows = plan["Rows"]
    centre = xyz([r["Centre"] for r in rows])
    right = xyz([r["Right"] for r in rows])
    top = np.asarray([r["SurfaceY"] for r in rows])
    terrain = np.asarray([r["TerrainY"] for r in rows])
    expected = centre[:, None, :] + right[:, None, :] * np.arange(-3, 3.001, .25)[None, :, None]
    expected[:, :, 1] = top
    mesh_difference = float(abs(vertices - expected.reshape(-1, 3)).max())
    check("Recorded surface and physical mesh use the same rows", mesh_difference < .0005, mesh_difference)
    path_difference = float(abs(xyz(plan["Points"]) - vertices.reshape(-1, 25, 3)[:, 12]).max())
    check("Route centre is the physical support height", path_difference < .00001, path_difference)
    check("Approved local extent and width", abs(plan["Length"] - (55.25 if temple else 213)) < .001
          and plan["Width"] == 6 and plan["Guard"] >= .199
          and plan["EndFeather"] == 8 and plan["MaximumEnvelopeGrade"] <= .25,
          {key: plan[key] for key in ["Length", "Width", "Guard", "EndFeather", "MaximumEnvelopeGrade"]})
    triangle = vertices[indices]
    normals = np.cross(triangle[:, 1] - triangle[:, 0], triangle[:, 2] - triangle[:, 0])
    check("All road faces face upward without degeneracy", (normals[:, 1] > 1e-6).all(), float(normals[:, 1].min()))
    degrees = np.degrees(np.arctan2(np.linalg.norm(normals[:, [0, 2]], axis=1), normals[:, 1]))
    check("Actual full-width triangle slope <= 15 degrees", degrees.max() <= 15,
          dict(maximum_degrees=float(degrees.max()), recorded=plan["MaximumSurfaceDegrees"]))
    cross_grade = abs(np.diff(top, axis=1) / .25)
    cross_limit = .21 if temple else .1
    check(f"Actual transverse grade <= {cross_limit}", cross_grade.max() <= cross_limit + .0001, float(cross_grade.max()))
    # Include triangle centroids and every edge midpoint: the support must not
    # cut through a convex terrain ridge between recorded lattice vertices.
    probes = np.concatenate([vertices, triangle.mean(axis=1),
                             (triangle[:, 0] + triangle[:, 1]) / 2,
                             (triangle[:, 1] + triangle[:, 2]) / 2,
                             (triangle[:, 2] + triangle[:, 0]) / 2])
    clearance = probes[:, 1] - sample(heights, probes)
    check("Raised surface clears exact original terrain", clearance.min() >= .005,
          dict(minimum_metres=float(clearance.min()), probes=len(probes)))
    height_error = abs(terrain - sample(heights, expected)).max()
    check("Recorded ground is the unchanged terrain triangle field", height_error < .001, float(height_error))
    end_rows = [-1] if temple else [0, -1]
    end_error = abs((top[end_rows] - terrain[end_rows]) - .035).max()
    check("Entire-width dry endpoint(s) blend to natural ground + 3.5cm", end_error < .0002, float(end_error))
    input_files = [folder / f"{route}.json", folder / f"{route}-mesh.json"]
    if temple:
        join = read(folder / f"{route}-join.json")
        prefix = read(folder / f"{route}-prefix-mesh.json")
        input_files.extend([folder / f"{route}-join.json", folder / f"{route}-prefix-mesh.json"])
        old = next(c for c in read(GEN295 / "crossings.json")["Crossings"] if c["RouteId"] == route)
        old_points = xyz(old["Points"])
        count = join["OriginalJoinRow"]
        check("Local correction retains exactly 86 original physical segments", count == 86
              and join["UnchangedPrefixSegments"] == 86 and plan["StartExtension"] == 0
              and 9 < plan["EndExtension"] < 10, dict(join_row=count, end_extension=plan["EndExtension"]))
        pv = xyz(prefix["Vertices"]); pt = np.asarray(prefix["Triangles"], dtype=np.int32)
        prefix_expected, prefix_indices = prefix_ribbon(old_points, count)
        difference = float(abs(pv-prefix_expected).max()) if pv.shape == prefix_expected.shape else float("inf")
        check("Retained full physical ribbon independently matches source geometry", difference < .0006
              and np.array_equal(pt, prefix_indices), dict(maximum_difference=difference, triangles=len(pt)//3))
        prefix_hash = hashlib.sha256(pv.astype("<f4").tobytes()+pt.astype("<i4").tobytes()).hexdigest()
        check("Retained prefix mesh fingerprint", prefix_hash == join["PrefixMeshSha256"], prefix_hash)
        first_edge = expected[0, [0, -1]]
        last_top = pv.reshape(count, 24, 3)[-1, [1, 2]]
        seam_error = float(abs(first_edge-last_top).max())
        check("Local full-width beginning matches retained bridge edge", seam_error < .0005, seam_error)
        full = xyz(join["FullRoutePoints"])
        # Unity parses the rounded source JSON into float32 before adding the
        # old 4cm support offset. Compare those exact float32 bits, not decimal
        # source coordinates at double precision (one Z ULP is .488mm here).
        full_expected = np.concatenate([old_points[:count].astype("<f4") + np.asarray([0, .04, 0], dtype="<f4"),
                                        xyz(plan["Points"]).astype("<f4")])
        full_equal = full.shape == full_expected.shape and np.array_equal(full.astype("<f4"), full_expected)
        full_error = float(abs(full.astype("<f4")-full_expected).max()) if full.shape == full_expected.shape else float("inf")
        check("Full crossing path is unchanged-prefix top plus actual patch top", full_equal,
              dict(exact_float32_equal=full_equal, maximum_difference=full_error))
        generated = read(folder.parent / "Generated/routes.json")
        input_files.append(folder.parent / "Generated/routes.json")
        generated_points = xyz(next(r for r in generated["routes"] if r["id"] == route)["points"])
        start = int(np.linalg.norm(generated_points[:, [0, 2]] - full[0, [0, 2]], axis=1).argmin())
        section = generated_points[start:start+len(full)]
        error = float(abs(section-full).max()) if section.shape == full.shape else float("inf")
        check("Generated gameplay route uses the full physical crossing path", error < .0002, error)
    dry = np.asarray([r["DryNext"] for r in rows[:-1]])
    check("Dry foundations and open wet rows partition the road", int(dry.sum()) == plan["DryFoundationSegments"]
          and int((~dry).sum()) == plan["WetOpenSegments"] and (temple or (~dry).any()),
          dict(dry_rows=int(dry.sum()), open_rows=int((~dry).sum())))
    # Read actual water triangles only from chunks whose bounding boxes touch
    # this short road, then check the dry foundation grid footprint.
    hydro = read(GEN295 / "hydro.json")
    dry_vertices = expected[:-1][dry].reshape(-1, 3)
    dry_vertices = np.concatenate([dry_vertices, expected[1:][dry].reshape(-1, 3)])
    wet_hits = np.zeros(len(dry_vertices), dtype=bool)
    for file in hydro["ChunkFiles"]:
        water = read(GEN295 / file)
        vv = xyz(water["Vertices"])
        if (vv[:, 0].max() < vertices[:, 0].min() or vv[:, 0].min() > vertices[:, 0].max()
                or vv[:, 2].max() < vertices[:, 2].min() or vv[:, 2].min() > vertices[:, 2].max()):
            continue
        tt = vv[np.asarray(water["Triangles"]).reshape(-1, 3)]
        for tri in tt:
            keep = ((dry_vertices[:, 0] >= tri[:, 0].min() - 1e-5)
                    & (dry_vertices[:, 0] <= tri[:, 0].max() + 1e-5)
                    & (dry_vertices[:, 2] >= tri[:, 2].min() - 1e-5)
                    & (dry_vertices[:, 2] <= tri[:, 2].max() + 1e-5))
            ids = np.flatnonzero(keep)
            if not len(ids):
                continue
            a, b, c = tri[:, [0, 2]]
            q = dry_vertices[ids][:, [0, 2]] - a
            ab, ac = b - a, c - a
            det = ab[0] * ac[1] - ab[1] * ac[0]
            if abs(det) < 1e-5:
                continue
            u = (q[:, 0] * ac[1] - q[:, 1] * ac[0]) / det
            v = (ab[0] * q[:, 1] - ab[1] * q[:, 0]) / det
            water_y = tri[0, 1] + (tri[1, 1] - tri[0, 1]) * u + (tri[2, 1] - tri[0, 1]) * v
            inside = (u >= -1e-5) & (v >= -1e-5) & (u + v <= 1 + 1e-5)
            ground_y = sample(heights, dry_vertices[ids])
            wet_hits[ids] |= inside & (ground_y < water_y + .19)
    check("Full-width dry foundations stay outside actual water support", not wet_hits.any(),
          dict(wet_vertices=int(wet_hits.sum()), probes=len(dry_vertices)))
    result = dict(checked_utc=datetime.now(timezone.utc).isoformat(),
                  scope="Independent offline exported geometry and input audit. Actual WheelCollider, NavMesh, CC, visual and performance results are separate.",
                  passed=all(c["passed"] for c in checks), checks=checks,
                  route=route, inputs={(p.name if p.parent == folder else "../Generated/" + p.name): sha(p) for p in input_files})
    output = f"{route}-independent-checks.json" if temple else "independent-checks.json"
    (folder / output).write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"Vehicle approach: {sum(c['passed'] for c in checks)}/{len(checks)} PASS")
    for item in checks:
        if not item["passed"]:
            print("FAIL", item["name"], item["detail"])
    return result["passed"]


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--folder", type=Path, default=OUT)
    parser.add_argument("--route", choices=["jeokro__cheolong", "hyeongang__temple"], default="jeokro__cheolong")
    args = parser.parse_args()
    raise SystemExit(0 if run(args.folder, args.route) else 1)
