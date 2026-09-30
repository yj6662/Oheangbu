"""Clip display footprints to saved collider triangles. Never invent missing floor Y.

Inputs are exported by compact:recovery:surfaces-export; output requires explicit
Unity import. Source meshes, colliders, routes, widths and buildings are untouched.
"""
import json
import struct
from pathlib import Path
import numpy as np
import shapely
from shapely.geometry import Polygon, LineString
from shapely.ops import unary_union

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Art/World/WorldMacro/Compact/Recovery'
manifest = json.loads((OUT/'surface_manifest.json').read_text(encoding='utf-8-sig'))
destination = OUT/'SurfaceOutput'
destination.mkdir(exist_ok=True)

def load(row):
    b = Path(row['file']).read_bytes()
    n, m = struct.unpack_from('<ii', b)
    v = np.frombuffer(b, '<f4', n*3, 8).reshape(n, 3).astype(np.float64)
    t = np.frombuffer(b, '<i4', m, 8+n*12).reshape(-1, 3)
    uv = np.frombuffer(b, '<f4', n*2, 8+n*12+m*4).reshape(n, 2)
    return v, t, uv

def polys(geometry):
    if geometry.is_empty:
        return []
    if geometry.geom_type == 'Polygon':
        return [geometry]
    return [p for g in getattr(geometry, 'geoms', []) for p in polys(g)]

def cross_y(a):
    return np.cross(a[1]-a[0], a[2]-a[0])[1]

surfaces = []
for row in manifest['meshes']:
    if row['support']:
        v, t, _ = load(row)
        tri = v[t]
        norm = np.cross(tri[:, 1]-tri[:, 0], tri[:, 2]-tri[:, 0])
        tri = tri[norm[:, 1] > 1e-6]
        surfaces.append((row, tri))
# Explicit cave floors and bridge decks take precedence over terrain at equal Y.
surfaces.sort(key=lambda s: ('Terrain_' in s[0]['path'], s[0]['path']))
roads = {r['path']: r for r in manifest['roads']}
report = {'scope': 'Display-only clipping to actual saved triangle planes; no route or collider repair implied.', 'rows': []}
occupied_roads = Polygon()
for row in [m for m in manifest['meshes'] if not m['support']]:
    v, t, uv = load(row)
    road = roads.get(row['path'])
    old_flips = int(sum(cross_y(a) < -1e-7 for a in v[t]))
    if road:
        points = np.array([[p['x'], p['y'], p['z']] for p in road['points']])
        line = LineString(points[:, [0, 2]])
        footprint = line.buffer(road['width']/2, cap_style='flat', join_style='round')
        raw_footprint = footprint
        footprint = footprint.difference(occupied_roads)
        cumulative = np.r_[0, np.cumsum(np.linalg.norm(np.diff(points[:, [0, 2]], axis=0), axis=1))]
        def expected(xz):
            d = line.project(shapely.Point(*xz))
            return np.interp(d, cumulative, points[:, 1])
    else:
        # Courtyard footprint is convex; do not resize or translate it.
        footprint = shapely.MultiPoint(v[:, [0, 2]]).convex_hull
        raw_footprint = footprint
        uv_fit = np.linalg.lstsq(np.c_[v[:, 0], v[:, 2], np.ones(len(v))], uv, rcond=None)[0]
        def expected(xz):
            return v[np.argmin(np.sum((v[:, [0, 2]]-xz)**2, axis=1)), 1]
    vertices, texcoords, alphas = [], [], []
    covered = Polygon()
    rejected = 0
    maximum_reprojection = 0.
    for support, tri in surfaces:
        xmin, zmin, xmax, zmax = footprint.bounds
        xz = tri[:, :, [0, 2]]
        mask = (xz[:, :, 0].max(axis=1) >= xmin) & (xz[:, :, 0].min(axis=1) <= xmax) & (xz[:, :, 1].max(axis=1) >= zmin) & (xz[:, :, 1].min(axis=1) <= zmax)
        candidates = tri[mask]
        if not len(candidates):
            continue
        available = footprint.difference(covered)
        shapely.prepare(available)
        accepted = []
        for triangle in candidates:
            polygon = Polygon(triangle[:, [0, 2]])
            if not available.intersects(polygon):
                continue
            clipped = polygon.intersection(available)
            if clipped.area < 1e-8:
                continue
            normal = np.cross(triangle[1]-triangle[0], triangle[2]-triangle[0])
            def height(xz):
                return triangle[0, 1]-(normal[0]*(xz[0]-triangle[0, 0])+normal[2]*(xz[1]-triangle[0, 2]))/normal[1]
            center = clipped.representative_point()
            mismatch = abs(height((center.x, center.y))-expected((center.x, center.y)))
            # Only explicit support within a bounded vertical neighbourhood is eligible.
            if mismatch > (1.1 if road else 3.):
                rejected += 1
                continue
            maximum_reprojection = max(maximum_reprojection, mismatch)
            accepted.append(clipped)
            for part in polys(clipped):
                for face in shapely.constrained_delaunay_triangles(part).geoms:
                    coords = np.asarray(face.exterior.coords)[:3]
                    world = np.array([[p[0], height(p)+.008, p[1]] for p in coords], dtype=np.float32)
                    if cross_y(world) < 0:
                        world = world[[0, 2, 1]]
                    if cross_y(world) <= 1e-7:
                        continue
                    vertices.extend(world)
                    if road:
                        texcoords.extend(world[:, [0, 2]]*.37)
                        for p in world:
                            edge = raw_footprint.boundary.distance(shapely.Point(float(p[0]), float(p[2])))
                            x = np.clip(edge/(road['width']*.25), 0, 1)
                            alphas.append(float(.8*x*x*(3-2*x)))
                    else:
                        # Courtyard has world-planar UVs; fit its existing mapping exactly.
                        texcoords.extend(np.c_[world[:, 0], world[:, 2], np.ones(3)] @ uv_fit)
                        alphas.extend([1.]*3)
        if accepted:
            covered = unary_union([covered, *accepted])
    a = np.asarray(vertices, dtype='<f4').reshape(-1, 3)
    assert len(a) and np.all(np.isfinite(a))
    ids = np.arange(len(a), dtype='<i4')
    file = destination/Path(row['file']).name
    with file.open('wb') as f:
        f.write(struct.pack('<ii', len(a), len(ids)))
        f.write(a.tobytes()); f.write(ids.tobytes())
        f.write(np.asarray(texcoords, dtype='<f4').tobytes())
        f.write(np.asarray(alphas, dtype='<f4').tobytes())
    missing = footprint.difference(covered)
    result = {'path': row['path'], 'oldInvertedTriangles': old_flips, 'newInvertedTriangles': 0,
              'triangles': len(a)//3, 'footprintM2': footprint.area, 'unsupportedM2': missing.area,
              'largestUnsupportedM2': max([p.area for p in polys(missing)], default=0),
              'rejectedSupportTriangles': rejected, 'maximumPlanToGroundM': maximum_reprojection,
              'missingPolygons': [list(p.exterior.coords) for p in polys(missing) if p.area > .05]}
    report['rows'].append(result)
    if road:
        occupied_roads = unary_union([occupied_roads, covered])
    print(row['path'].split('/')[-1], len(a)//3, 'tris; uncovered', round(missing.area, 3), 'm2', flush=True)
    (OUT/'surface_build.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
