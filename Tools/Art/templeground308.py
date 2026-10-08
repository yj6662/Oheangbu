"""#308 temple ground (D308-44, SPEC-ARCH-TEMPLE-308 §8): low earth terraces held by rubble stone walls (막돌 석축), capped
with long dressed stones, joined by stone stairs - the usual ground of a Korean mountain temple. Each terrace is fitted to the
ground it stands on (its top is a little above the terrain under it), so the walls stay low; where the hill is higher than a
terrace the wall simply runs into it. Nothing here edits terrain. Unity coordinates, json meshes like cliffway308.

  python Tools/Art/templeground308.py   -> Art/World/Compact/Rebuild/TempleGround308/{Meshes/*.json, layout.json}
Numbers: Tools/Unity/Stage308_temple/ground308.json
"""
from __future__ import annotations
import json, math, sys
from pathlib import Path
import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
from cliffway308 import Mesh, ground, ground_hi

ROOT = Path(__file__).resolve().parents[2]
CFG = json.loads((ROOT / 'Tools/Unity/Stage308_temple/ground308.json').read_text(encoding='utf-8'))
OUT = ROOT / 'Art/World/Compact/Rebuild/TempleGround308'
UP = np.array([0., 1., 0.])


def eff_ground(x, z, skip):
    """Terrain or the top of another terrace that covers the point (what a wall of terrace `skip` stands on / runs into)."""
    g = float(ground_hi(x, z))
    for t in CFG['terraces']:
        if t['id'] != skip and t['x0'] <= x <= t['x1'] and t['z0'] <= z <= t['z1']: g = max(g, t['top'])
    return g


def stair_gaps(t, edge):
    return [(s['at'] - s['width'] / 2 - .3, s['at'] + s['width'] / 2 + .3) for s in CFG['stairs'] if s['terrace'] == t['id'] and s['edge'] == edge]


def build():
    c = CFG; rng = np.random.default_rng(c['seed'])
    wall = Mesh('TempleGround_Wall'); cap = Mesh('TempleGround_Cap'); earth = Mesh('TempleGround_Earth'); stairs = Mesh('TempleGround_Stairs'); col = Mesh('TempleGround_Collision')
    stats = dict(terraces=[], stones=0, caps=0)
    for t in c['terraces']:
        x0, x1, z0, z1, top = t['x0'], t['x1'], t['z0'], t['z1'], t['top']
        # edges: start point, direction along, outward normal, name
        edges = [((x0, z0), (1, 0), (0, -1), 'south'), ((x1, z0), (0, 1), (1, 0), 'east'), ((x1, z1), (-1, 0), (0, 1), 'north'), ((x0, z1), (0, -1), (-1, 0), 'west')]
        heights = []
        for (ax, az), (dx, dz), (nx, nz), name in edges:
            L = (x1 - x0) if dx != 0 else (z1 - z0)
            A = np.array([ax, 0., az]); D = np.array([dx, 0., dz], float); N = np.array([nx, 0., nz], float)
            gaps = stair_gaps(t, name)

            def coord(s): return ax + dx * s if dx != 0 else az + dz * s
            def in_gap(s0, s1):
                a, b = sorted((coord(s0), coord(s1)))
                return any(a < g1 and b > g0 for g0, g1 in gaps)

            def base(s):                                                         # what the wall stands on, just outside the face
                p = A + D * s + N * .45; return eff_ground(p[0], p[2], t['id'])

            ss = np.arange(0, L + .01, 1.0); gb = np.array([base(s) for s in ss]); heights += list(np.maximum(top - gb, 0))
            # ---- rubble courses
            y = top - c['capHeight']
            while True:
                h = float(rng.uniform(*c['course'])); yb = y - h
                if yb < gb.min() - .5: break
                s = float(rng.uniform(-.3, 0))
                while s < L:
                    ln = float(rng.uniform(*c['stone'])); s1 = min(s + ln, L + .05)
                    mid = (s + s1) / 2; g = base(min(max(mid, 0), L))
                    if y > g - .08 and not in_gap(s, s1):
                        out = c['batter'] * (top - (y + yb) / 2) + float(rng.uniform(-.035, .045))
                        ctr = A + D * mid + N * (out - .22) + UP * ((y + yb) / 2)
                        wall.box(ctr, D, UP, N, (s1 - s - .025, h - .02, .5), 'fieldstone', faces='xXyYZ', uvscale=1.2); stats['stones'] += 1
                    s = s1
                y = yb
            # ---- cap stones (갑석), skipped where the hill is as high as the terrace
            s = 0.
            while s < L:
                ln = float(rng.uniform(*c['capStone'])); s1 = min(s + ln, L)
                mid = (s + s1) / 2
                if base(mid) < top - .12 and not in_gap(s, s1):
                    ctr = A + D * mid + N * (.06 - c['capDepth'] / 2) + UP * (top - c['capHeight'] / 2 + .03)
                    cap.box(ctr, D, UP, N, (s1 - s - .02, c['capHeight'], c['capDepth']), 'stone_dressed', uvscale=1.5); stats['caps'] += 1
                s = s1
            # ---- collision: one vertical sheet per edge stretch that is not a stair gap
            o, dd = (ax, dx) if dx != 0 else (az, dz)
            sb = sorted([0.] + [min(max((v - o) / dd, 0.), L) for g0, g1 in gaps for v in (g0, g1)] + [L])
            for a, b in zip(sb[0::2], sb[1::2]):
                P = [A + D * a + UP * (gb.min() - .5), A + D * b + UP * (gb.min() - .5), A + D * b + UP * top, A + D * a + UP * top]
                col.add(P, np.repeat(N[None], 4, 0), np.zeros((4, 2)), [[0, 1, 2], [0, 2, 3]], 'collision')
        # ---- earth top: flat, a few cm of unevenness away from the edge
        inset = c['capDepth'] - .08; step = 3.0
        xs = np.linspace(x0 + inset, x1 - inset, max(2, int((x1 - x0) / step))); zs = np.linspace(z0 + inset, z1 - inset, max(2, int((z1 - z0) / step)))
        X, Z = np.meshgrid(xs, zs, indexing='ij'); edge = np.minimum(np.minimum(X - xs[0], xs[-1] - X), np.minimum(Z - zs[0], zs[-1] - Z))
        Y = top + .02 + np.clip(edge / 4, 0, 1) * (.035 * np.sin(X * .31 + Z * .17) + .03 * np.sin(X * .11 - Z * .23 + 1.3))
        P = np.stack([X, Y, Z], -1)
        earth.grid(P, UP, 'temple_earth', uv=np.stack([X, Z], -1) / 4.0)
        col.add([[x0, top, z0], [x1, top, z0], [x1, top, z1], [x0, top, z1]], np.repeat(UP[None], 4, 0), np.zeros((4, 2)), [[0, 1, 2], [0, 2, 3]], 'collision')
        stats['terraces'].append(dict(id=t['id'], top=top, size=[x1 - x0, z1 - z0], wallMax=float(np.max(heights)), wallMedian=float(np.median(heights)),
                                      terrainUnder=[float(min(ground(x, z) for x in np.arange(x0, x1, 4) for z in np.arange(z0, z1, 4))), float(max(ground_hi(x, z) for x in np.arange(x0, x1, 4) for z in np.arange(z0, z1, 4)))]))
    # ---- stairs: on one edge of their terrace, running outward (down) from the top
    EDGE = dict(south=((1, 0), (0, -1)), north=((1, 0), (0, 1)), west=((0, 1), (-1, 0)), east=((0, 1), (1, 0)))
    for s in c['stairs']:
        t = next(t for t in c['terraces'] if t['id'] == s['terrace']); top = t['top']; w = s['width']
        (dx, dz), (nx, nz) = EDGE[s['edge']]; D = np.array([dx, 0., dz], float); N = np.array([nx, 0., nz], float)
        ex = dict(south=t['z0'], north=t['z1'], west=t['x0'], east=t['x1'])[s['edge']]
        E = np.array([s['at'], 0., ex]) if dx != 0 else np.array([ex, 0., s['at']])           # middle of the stair's top edge
        probe = E + N * 3.0; low = eff_ground(probe[0], probe[2], t['id']); rise = top - low
        if rise < .05: s['_out'] = dict(steps=0, riser=0, rise=rise, run=0, slopeDeg=0); continue
        n = max(1, int(math.ceil(rise / c['riser']))); r = rise / n; tread = c['tread']
        for k in range(n):                                                       # k = 0 is the top step
            yk = top - r * k
            stairs.box(E + N * (tread * (k + .5)) + UP * (yk - (r + .5) / 2), D, UP, N, (w, r + .5, tread + .02), 'stone_dressed', faces='xXYZ', uvscale=1.5)
        run = tread * n
        for sd in (-1, 1):                                                       # stepped cheek stones (소맷돌)
            for k in range(0, n, 2):
                yk = top - r * k + .22
                stairs.box(E + D * sd * (w / 2 + .2) + N * (tread * (k + 1)) + UP * (yk - .6), D, UP, N, (.4, 1.2, tread * 2 + .02), 'stone_dressed', faces='xXYZ', uvscale=1.5)
        A0 = E - D * (w / 2); A1 = E + D * (w / 2)
        P = [A0 + UP * top, A1 + UP * top, A1 + N * run + UP * low, A0 + N * run + UP * low]
        nrm = N * rise + UP * run; nrm = nrm / np.linalg.norm(nrm)
        col.add(P, np.repeat(nrm[None], 4, 0), np.zeros((4, 2)), [[0, 1, 2], [0, 2, 3]], 'collision')
        s['_out'] = dict(steps=n, riser=r, rise=rise, run=run, slopeDeg=math.degrees(math.atan2(rise, run)))
    (OUT / 'Meshes').mkdir(parents=True, exist_ok=True)
    meshes = {}
    for m in (wall, cap, earth, stairs, col):
        (OUT / 'Meshes' / (m.name + '.json')).write_text(json.dumps(m.data()), encoding='utf-8'); v, tr = m.count(); meshes[m.name] = dict(vertices=int(v), triangles=int(tr))
    stats['stairs'] = [dict(id=s['id'], **s['_out']) for s in c['stairs']]; stats['meshes'] = meshes
    (OUT / 'layout.json').write_text(json.dumps(stats, indent=1), encoding='utf-8')
    print(json.dumps(stats, indent=1))


if __name__ == '__main__':
    build()
