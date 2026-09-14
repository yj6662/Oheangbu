"""Measure capital return paths and geographic changes from exported Unity data."""
from __future__ import annotations

import heapq
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Art/World/WorldMacro'
BEFORE = OUT / 'Versions/v3_before_realm_tint'


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def shortest(measurements, start, end='Hwanggyeong'):
    graph = {}
    for route in measurements['routes']:
        for a, b in ((route['from'], route['to']), (route['to'], route['from'])):
            graph.setdefault(a, []).append((b, route))
    queue = [(0.0, start)]
    distance, previous = {start: 0.0}, {}
    while queue:
        cost, node = heapq.heappop(queue)
        if cost != distance[node]:
            continue
        if node == end:
            break
        for target, route in graph.get(node, []):
            candidate = cost + route['lengthM']
            if candidate < distance.get(target, float('inf')):
                distance[target] = candidate
                previous[target] = (node, route)
                heapq.heappush(queue, (candidate, target))
    if end not in distance:
        return None
    sites, routes = [end], []
    while sites[-1] != start:
        node, route = previous[sites[-1]]
        sites.append(node)
        routes.append(route)
    return list(reversed(sites)), list(reversed(routes))


def build():
    sheet, old_sheet = read(OUT / 'sheet.json'), read(BEFORE / 'sheet.json')
    measurements, old_measurements = read(OUT / 'measurements.json'), read(BEFORE / 'measurements.json')
    grid, old_grid = read(OUT / 'terrain_grid.json'), read(BEFORE / 'terrain_grid.json')
    assert all(grid[key] == old_grid[key] for key in ('minX', 'minZ', 'step', 'cols', 'rows', 'inside'))
    deltas = [new-old for new, old, inside in zip(grid['heights'], old_grid['heights'], grid['inside']) if inside]
    cell_area = grid['step'] ** 2 / 1_000_000
    revision = {
        'ridgeCountBefore': len(old_sheet['Ridges']), 'ridgeCountAfter': len(sheet['Ridges']),
        'raisedArea25mKm2': sum(d > 25 for d in deltas) * cell_area,
        'raisedArea75mKm2': sum(d > 75 for d in deltas) * cell_area,
        'maxRiseM': max(deltas), 'meanRiseM': sum(deltas) / len(deltas),
        'outlineUnchanged': sheet['Outline'] == old_sheet['Outline'],
        'basinsUnchanged': sheet['Basins'] == old_sheet['Basins'],
        'riversUnchanged': sheet['Rivers'] == old_sheet['Rivers'],
        'sampling': '80m grid-cell estimate of height change, not surveyed mountain area.'
    }
    rows = []
    for start in ('Cheongrim', 'Jeokro', 'Cheolong', 'Hyeongang'):
        result = shortest(measurements, start)
        before_anchor = start
        old = shortest(old_measurements, before_anchor)
        old_length = sum(r['lengthM'] for r in old[1]) if old else None
        if result is None:
            rows.append({'from': start, 'to': 'Hwanggyeong', 'status': 'FAIL_NO_ROUTE'})
            continue
        sites, routes = result
        carriage = sum(r['lengthM'] for r in routes if r['carriage'])
        trail = sum(r['lengthM'] for r in routes if not r['carriage'])
        length = carriage + trail
        rows.append({
            'from': start, 'to': 'Hwanggyeong',
            'status': 'PASS' if all(r['waterSamplesWithoutBridge'] == 0 and r['outsideSamples'] == 0 for r in routes) else 'FAIL_ROUTE_GEOMETRY',
            'siteIds': sites, 'routeIds': [r['id'] for r in routes],
            'lengthM': length, 'walkSeconds': length / sheet['WalkSpeed'],
            'mixedSeconds': carriage / sheet['CarriageSpeed'] + trail / sheet['WalkSpeed'],
            'trailLengthM': trail, 'carriageLengthM': carriage,
            'beforeLengthM': old_length, 'beforeAnchor': before_anchor,
            'shorterByM': old_length - length if old_length is not None else None,
        })
    report = {
        'scope': 'V3 to V4. Shortest by distance on authored undirected route graph. Times are constant-speed estimates, not unlocked gameplay or vehicle validation. All four comparisons use the same named regional origin; route geometry may change with terrain.',
        'baselineVersion': 'v3_before_realm_tint',
        'rows': rows, 'revision': revision,
    }
    (OUT / 'return_routes.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps(report, ensure_ascii=False, indent=2))
    assert all(r['status'] == 'PASS' for r in rows), 'A realm has no valid capital return path'
    assert revision['ridgeCountAfter'] == revision['ridgeCountBefore'], 'Unexpected geographic ridge change'
    assert max(abs(d) for d in deltas) < .001, 'Unexpected terrain height change'
    assert all(revision[k] for k in ('outlineUnchanged', 'riversUnchanged')), 'Reserved rivers or outline changed'


if __name__ == '__main__':
    build()
