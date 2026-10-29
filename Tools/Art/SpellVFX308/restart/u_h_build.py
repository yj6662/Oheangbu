#!/usr/bin/env python3
"""D308-153 우 route H (2026-10-29, the user: "반투명 홀로그램 육각형 막에 문양이 박혀있는 모습" -> "돔 + 다양한 문양"; concept kcisa/concepts/hx_1.png + hx_2's patterns):
the data for the hexagon dome.
  1. Art/SpellVFX308/Restart/u_H/h_patterns.png - the eight KCISA patterns of kcisa/ref_water_patterns.png (4 x 2 tiles of 400 px) as a 4 x 2 atlas of line masks
     (512 px tiles, the hairlines thickened for game distance; white line on black, in every channel).
  2. Art/SpellVFX308/Restart/u_H/h_dome.json - a Goldberg dome: the dual of a frequency-F geodesic icosphere (hexagons + the pentagons at the icosahedron's corners),
     the cells whose centre is above the ground, each a triangle fan. Per vertex: position (unit sphere, y flattened by FLAT), normal, uv0 = the pattern uv inside the
     cell (0..1, the cell's own tangent plane, "up" toward the pole), uv1 = (cell hash 0..1, edge 0 at the centre / 1 on the border), uv2 = (pattern index 0..7, height 0..1).
SpellFxSetupH (setup:h) turns the json into a Mesh asset and imports the atlas.
  python Tools/Art/SpellVFX308/restart/u_h_build.py [--freq 4]"""
import json, math, sys
from pathlib import Path
import numpy as np
from PIL import Image, ImageFilter

REPO = Path(__file__).resolve().parents[4]
SRC = REPO / 'Art/SpellVFX308/Restart/kcisa/ref_water_patterns.png'
OUT = REPO / 'Art/SpellVFX308/Restart/u_H'
FREQ = int(sys.argv[sys.argv.index('--freq') + 1]) if '--freq' in sys.argv else 4
FLAT = 0.85          # dome height / radius
TILE = 512


def atlas():
    im = np.asarray(Image.open(SRC).convert('L')).astype(np.float32) / 255.0
    bg = float(np.median(im))
    out = Image.new('L', (TILE * 4, TILE * 2), 0)
    for k in range(8):
        cx, cy = (k % 4) * 400, (k // 4) * 400
        t = im[cy:cy + 400, cx:cx + 400]
        m = np.clip((t - bg - 0.06) / 0.35, 0, 1)
        mi = Image.fromarray((m * 255).astype(np.uint8)).resize((TILE, TILE), Image.LANCZOS)
        mi = mi.filter(ImageFilter.MaxFilter(3)).filter(ImageFilter.GaussianBlur(0.7))
        mi = mi.point(lambda v: min(255, int(v * 1.6)))
        out.paste(mi, ((k % 4) * TILE, (k // 4) * TILE))
    OUT.mkdir(parents=True, exist_ok=True)
    out.convert('RGB').save(OUT / 'h_patterns.png')
    return out


def icosphere(freq):
    t = (1 + 5 ** 0.5) / 2
    v = np.array([(-1, t, 0), (1, t, 0), (-1, -t, 0), (1, -t, 0), (0, -1, t), (0, 1, t), (0, -1, -t), (0, 1, -t), (t, 0, -1), (t, 0, 1), (-t, 0, -1), (-t, 0, 1)], float)
    f = [(0, 11, 5), (0, 5, 1), (0, 1, 7), (0, 7, 10), (0, 10, 11), (1, 5, 9), (5, 11, 4), (11, 10, 2), (10, 7, 6), (7, 1, 8), (3, 9, 4), (3, 4, 2), (3, 2, 6), (3, 6, 8), (3, 8, 9), (4, 9, 5), (2, 4, 11), (6, 2, 10), (8, 6, 7), (9, 8, 1)]
    verts, index, faces = [], {}, []

    def vid(p):
        p = p / np.linalg.norm(p)
        key = tuple(np.round(p, 6))
        if key not in index:
            index[key] = len(verts); verts.append(p)
        return index[key]

    for a, b, c in f:
        A, B, C = v[a], v[b], v[c]
        grid = {}
        for i in range(freq + 1):
            for j in range(freq + 1 - i):
                k = freq - i - j
                grid[i, j] = vid((A * i + B * j + C * k) / freq)
        for i in range(freq):
            for j in range(freq - i):
                faces.append((grid[i, j], grid[i + 1, j], grid[i, j + 1]))
                if i + j < freq - 1:
                    faces.append((grid[i + 1, j], grid[i + 1, j + 1], grid[i, j + 1]))
    return np.array(verts), faces


def dome(freq):
    V, F = icosphere(freq)
    centres = np.array([(V[a] + V[b] + V[c]) / 3 for a, b, c in F]); centres /= np.linalg.norm(centres, axis=1)[:, None]
    adj = [[] for _ in V]
    for fi, (a, b, c) in enumerate(F):
        for x in (a, b, c): adj[x].append(fi)
    rnd = np.random.default_rng(11)
    pos, nrm, uv0, uv1, uv2, tris = [], [], [], [], [], []
    cells = 0
    for ci, n in enumerate(V):
        if n[1] < -0.06:
            continue
        ring = centres[adj[ci]]
        # tangent frame: b = toward the pole along the surface, t = round the dome
        up = np.array([0, 1, 0.0])
        t = np.cross(up, n)
        if np.linalg.norm(t) < 1e-3: t = np.array([1, 0, 0.0])
        t /= np.linalg.norm(t); b = np.cross(n, t)
        ang = np.arctan2(ring @ b, ring @ t)
        ring = ring[np.argsort(ang)]
        rad = float(np.mean([np.linalg.norm((p - n) - n * ((p - n) @ n)) for p in ring]))
        h = float(rnd.random()); pat = int(rnd.integers(0, 8))
        height = float(max(n[1], 0.0))

        def add(p, edge):
            d = p - n; d = d - n * (d @ n)
            u = 0.5 + (d @ t) / (2 * rad) * 1.08; w = 0.5 + (d @ b) / (2 * rad) * 1.08
            pf = np.array([p[0], p[1] * FLAT, p[2]])
            nf = np.array([p[0], p[1] / FLAT, p[2]]); nf /= np.linalg.norm(nf)
            pos.append(pf.tolist()); nrm.append(nf.tolist()); uv0.append([float(u), float(w)]); uv1.append([h, edge]); uv2.append([float(pat), height])
            return len(pos) - 1

        c0 = add(n, 0.0)
        corners = [add(p, 1.0) for p in ring]
        for k in range(len(corners)):
            tris += [c0, corners[k], corners[(k + 1) % len(corners)]]
        cells += 1
    return {'freq': freq, 'flat': FLAT, 'cells': cells, 'pos': pos, 'nrm': nrm, 'uv0': uv0, 'uv1': uv1, 'uv2': uv2, 'tris': tris}


def main():
    atlas()
    d = dome(FREQ)
    (OUT / 'h_dome.json').write_text(json.dumps(d), encoding='utf-8')
    print('atlas', OUT / 'h_patterns.png', 'dome cells', d['cells'], 'verts', len(d['pos']), 'tris', len(d['tris']) // 3)


if __name__ == '__main__':
    main()
