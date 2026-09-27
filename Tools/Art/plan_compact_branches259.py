"""Route proposals against existing height samples; no scene or navigation mutation."""
import heapq
import hashlib
import json
import math
from pathlib import Path
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
HEIGHT = ROOT / 'Art/World/Compact/Rebuild/Cartography/heights.f32'
OUT = ROOT / 'Art/World/Compact/Rebuild/Branches259'


def plan():
    height = np.fromfile(HEIGHT, dtype='<f4').reshape(2400, 1600)
    def sample(p):
        return float(height[round(p[1] / 2.5), round(p[0] / 2.5)])
    def clear(a, b):
        d = math.dist(a, b)
        n = max(1, math.ceil(d / 2.5))
        previous = sample(a)
        for i in range(1, n + 1):
            p = (a[0] + (b[0] - a[0]) * i / n, a[1] + (b[1] - a[1]) * i / n)
            current = sample(p)
            if abs(current - previous) / max(.01, d / n) > .30:
                return False
            previous = current
        return True
    def route(start, end):
        a = tuple(round(v / 5) for v in start)
        b = tuple(round(v / 5) for v in end)
        queue, cost, parent, closed = [(0, a)], {a: 0}, {}, set()
        while queue:
            _, p = heapq.heappop(queue)
            if p in closed:
                continue
            closed.add(p)
            if p == b:
                break
            for dx, dz in [(1,0),(-1,0),(0,1),(0,-1),(1,1),(1,-1),(-1,1),(-1,-1)]:
                q = (p[0]+dx, p[1]+dz)
                if not (520 <= q[0] <= 760 and 430 <= q[1] <= 720):
                    continue
                pa, pb = (p[0]*5, p[1]*5), (q[0]*5, q[1]*5)
                distance = math.dist(pa, pb)
                grade = abs(sample(pa)-sample(pb))/distance
                if grade > .28 or not clear(pa, pb):
                    continue
                proposed = cost[p] + distance * (1 + 14 * grade * grade)
                if proposed < cost.get(q, math.inf):
                    cost[q], parent[q] = proposed, p
                    heapq.heappush(queue, (proposed + math.dist(q, b)*5, q))
        if b not in cost:
            return None
        points, p = [b], b
        while p != a:
            p = parent[p]
            points.append(p)
        points = [(p[0]*5, p[1]*5) for p in reversed(points)]
        simple, i = [points[0]], 0
        while i < len(points)-1:
            j = min(len(points)-1, i+12)
            while j > i+1 and not clear(points[i], points[j]):
                j -= 1
            simple.append(points[j])
            i = j
        return simple
    segments = [
        ('herb_entry259', 'herb_junction259', 'herb_path', (3120,2570), (3030,2800)),
        ('herb_return259', 'herb_path', 'deep_forest', (3030,2800), (3280,3150)),
        ('root_approach259', 'logging', 'root_cave', (3340,2700), (3550,2850)),
        ('root_return259', 'root_cave', 'deep_forest', (3550,2850), (3280,3150)),
        ('tree_branch259', 'deep_forest', 'old_tree', (3280,3150), (3690,3270)),
    ]
    rows = []
    for name, frm, to, start, end in segments:
        points = route(start, end)
        rows.append(dict(id=name, fromId=frm, toId=to,
                         status='HEIGHT_PROPOSAL_ONLY' if points else 'NO_GRADE_SUPPORTED_ROUTE',
                         points=[dict(x=x, y=z) for x,z in points] if points else [],
                         length=sum(math.dist(a,b) for a,b in zip(points,points[1:])) if points else None))
    result = dict(scope='Height-grid proposals only. Does not test scenery collisions, actual NavMesh, cave interior, visual guidance or human wayfinding.',
                  height_sha256=hashlib.sha256(HEIGHT.read_bytes()).hexdigest(), routes=rows)
    OUT.mkdir(parents=True, exist_ok=True)
    (OUT/'route-proposals.json').write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps([dict(id=r['id'], status=r['status'], metres=round(r['length'] or 0)) for r in rows]))


if __name__ == '__main__':
    plan()
