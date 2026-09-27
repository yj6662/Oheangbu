"""#297 palace parts: 궁장 (tile-capped palace wall), 월대 (stepped stone terrace with balustrades and stair flights),
박석 court paving (+ raised 삼도 and 품계석 rows). World-space meshes; terrain from hanok297_wall.H.
"""
from __future__ import annotations
import math
import numpy as np
from hanok297 import MB, beam, nz, TILE_W, TILE_H
from hanok297_wall import H, resample


def palace_wall(name, path, height=4.6, thickness=1.1, base_h=1.0, lod=0, step=2.0, closed=False, s0=0.0):
    """궁장: stone base course + plastered body + a small tiled gable coping (기와 지붕) along the top.
    The top follows a smoothed terrain line (stepped where steep). Collision: the wall body. Returns (vis, col, info)."""
    P = resample(path, [step, step * 2, step * 4][lod])
    if closed and not np.allclose(P[0], P[-1]): P = np.vstack([P, P[:1]])
    d = nz(np.gradient(P, axis=0)); n = np.stack([d[:, 1], -d[:, 0]], -1)
    s = np.concatenate([[0], np.cumsum(np.linalg.norm(np.diff(P, axis=0), axis=1))]); t = thickness / 2
    g = np.minimum.reduce([H(*(P + n * o).T) for o in (-t - .3, 0, t + .3)])
    top = g + height
    for _ in range(3): top[1:-1] = .25 * top[:-2] + .5 * top[1:-1] + .25 * top[2:]
    top = np.maximum(top, g + height - .4)
    vis = MB(name); col = MB(name + '_collision')
    X = lambda off, yy: np.stack([P[:, 0] + n[:, 0] * off, yy, P[:, 1] + n[:, 1] * off], -1)
    uv_s = (s + s0) / 2.0
    def face(off, y0, y1, mat, sign, scale=2.0):
        A, B = X(off, y0), X(off, y1); Q = np.stack([A, B], 1)
        uv = np.stack([np.repeat(uv_s[:, None], 2, 1), np.stack([A[:, 1], B[:, 1]], 1) / scale], -1)
        vis.grid(Q, mat, uv, lambda _: np.concatenate([n[:, None, 0:1].repeat(2, 1) * sign, np.zeros((len(n), 2, 1)), n[:, None, 1:2].repeat(2, 1) * sign], -1))
        col.grid(Q, 'c', uv, lambda _: np.concatenate([n[:, None, 0:1].repeat(2, 1) * sign, np.zeros((len(n), 2, 1)), n[:, None, 1:2].repeat(2, 1) * sign], -1))
    for side in (-1, 1):
        face(side * (t + .06), g - .8, g + base_h, 'stone_dressed', side)            # 지대석/장대석 base course
        face(side * t, g + base_h, top - .35, 'plaster', side, 1.5)                  # 회벽 body
        face(side * t, top - .35, top, 'brick_dark' if lod == 0 else 'plaster', side, .8)   # 전돌 band under the coping
    # coping: two short tiled slopes + ridge
    eave = .45; rise = .55
    for side in (-1, 1):
        A = X(side * (t + eave), top - .05); B = X(0, top + rise); Q = np.stack([A, B], 1)
        uv = np.stack([np.repeat((uv_s * 2.0 / TILE_W)[:, None], 2, 1), np.repeat(np.array([[0, (eave + t) / TILE_H]]), len(P), 0)], -1)
        vis.grid(Q, 'tile', uv, lambda _, side=side: np.concatenate([n[:, None, 0:1].repeat(2, 1) * side * .6, np.ones((len(n), 2, 1)), n[:, None, 1:2].repeat(2, 1) * side * .6], -1))
        if lod < 2:
            L0 = X(side * (t + eave), top - .05); L1 = X(side * (t + eave), top - .22)
            Q = np.stack([L1, L0], 1); vis.grid(Q, 'fascia', np.zeros(Q.shape[:2] + (2,)), lambda _, side=side: np.concatenate([n[:, None, 0:1].repeat(2, 1) * side, np.zeros((len(n), 2, 1)), n[:, None, 1:2].repeat(2, 1) * side], -1))
    if lod < 2:
        R0 = X(0, top + rise - .02); R1 = X(0, top + rise + .22)
        for sgn in (-1, 1):
            Q = np.stack([R0 + np.stack([n[:, 0], np.zeros(len(n)), n[:, 1]], -1) * .16 * sgn, R1 + np.stack([n[:, 0], np.zeros(len(n)), n[:, 1]], -1) * .16 * sgn], 1)
            vis.grid(Q, 'ridge', np.stack([np.repeat(uv_s[:, None], 2, 1), np.repeat(np.array([[0, .3]]), len(P), 0)], -1),
                     lambda _, sgn=sgn: np.concatenate([n[:, None, 0:1].repeat(2, 1) * sgn, np.zeros((len(n), 2, 1)), n[:, None, 1:2].repeat(2, 1) * sgn], -1))
    info = dict(length=float(s[-1]), top=[float(top.min()), float(top.max())])
    return vis, col, info


def wooltae(name, centre, yaw, size, tiers=2, tier_h=1.05, step_in=3.0, stairs=('front',), lod=0, rail=True):
    """월대: stacked stone platforms (dressed faces, capping slab, stone balustrade on each tier edge) with stair flights;
    the front flight has a central carved 답도 slab. centre=(x, ground y, z) — tiers rise from the ground y."""
    cx, cy, cz = centre; ca, sa = math.cos(math.radians(yaw)), math.sin(math.radians(yaw))
    L = lambda x, y, z: np.array([cx + x * ca + z * sa, y, cz - x * sa + z * ca])
    vis = MB(name); col = MB(name + '_collision')
    W0, D0 = size
    for k in range(tiers):
        w, dpt = W0 - 2 * step_in * k, D0 - 2 * step_in * k; y0 = cy + tier_h * k - (.6 if k == 0 else 0); y1 = cy + tier_h * (k + 1)
        vis.box(L(0, (y0 + y1) / 2, 0), [w, y1 - y0, dpt], 'stone_dressed', yaw=yaw, faces='xXzZ', uvscale=2.0)
        vis.box(L(0, y1 + .05, 0), [w + .2, .1, dpt + .2], 'stone_plain', yaw=yaw, faces='xXzZY', uvscale=2.0)
        col.box(L(0, (y0 + y1) / 2 + .05, 0), [w, y1 - y0 + .1, dpt], 'c', yaw=yaw, faces='xXzZY')
        # stair flights through each tier edge
        for side in stairs:
            sz = 1 if side == 'front' else -1
            n = max(2, int(math.ceil(tier_h / .17))); rise = tier_h / n; tread = .33; fw = 4.2 if side == 'front' else 3.0
            for i in range(n):
                zc = sz * (dpt / 2 + (n - i - .5) * tread)
                vis.box(L(0, y0 + (k > 0) * 0 + rise * (i + .5) + (cy + tier_h * k - y0), zc), [fw, rise, tread], 'stone_plain', yaw=yaw, faces='xXzZY', uvscale=1.0)
            a = L(-fw / 2, cy + tier_h * k + .02, sz * (dpt / 2 + n * tread)); b = L(fw / 2, cy + tier_h * k + .02, sz * (dpt / 2 + n * tread))
            c = L(fw / 2, y1 + .1, sz * dpt / 2); dd = L(-fw / 2, y1 + .1, sz * dpt / 2)
            col.quad(a, b, c, dd, 'c', L(0, 1, sz) - L(0, 0, 0))
            if side == 'front' and lod < 2:   # 답도: carved slab in the middle of the flight
                p0 = L(0, cy + tier_h * k + .05, sz * (dpt / 2 + n * tread)); p1 = L(0, y1 + .12, sz * dpt / 2)
                beam(vis, p0, p1, 1.2, .1, 'stone_plain')
        if rail and lod < 2:   # stone balustrade (난간석): posts + rail along the tier edge, gaps at the stairs
            hw, hd = w / 2 - .25, dpt / 2 - .25
            edges = [((-hw, hd), (hw, hd), 'front'), ((hw, -hd), (-hw, -hd), 'back'), ((hw, hd), (hw, -hd), 'right'), ((-hw, -hd), (-hw, hd), 'left')]
            for (ax, az), (bx, bz), side in edges:
                seg = np.linalg.norm([bx - ax, bz - az]); m = max(2, int(seg / 2.2))
                for i in range(m):
                    u0, u1 = i / m, (i + 1) / m
                    p0 = np.array([ax + (bx - ax) * u0, az + (bz - az) * u0]); p1 = np.array([ax + (bx - ax) * u1, az + (bz - az) * u1])
                    mid = (p0 + p1) / 2
                    if side in stairs and abs(mid[0]) < 2.6: continue
                    A = L(p0[0], y1 + .1, p0[1]); B = L(p1[0], y1 + .1, p1[1])
                    beam(vis, A + [0, .72, 0], B + [0, .72, 0], .16, .14, 'stone_plain')
                    if lod == 0: vis.box((A + [0, .4, 0]).tolist(), [.22, .7, .22], 'stone_plain', yaw=yaw, faces='xXzZY', uvscale=1.0)
                    col.box(((A + B) / 2 + [0, .5, 0]).tolist(), [max(abs((B - A)[0]), .2), 1.0, max(abs((B - A)[2]), .2)], 'c', faces='xXzZ')
    top = cy + tier_h * tiers + .1
    return vis, col, dict(top=top)


def court_paving(name, centre, yaw, size, y=None, samdo=True, markers=0, lod=0):
    """박석 court: a flat slab surface on the court pad (collision = the same plane), raised 삼도 axis path and optional
    품계석 marker rows (markers per side). The pad height is the terrain op's top (passed as y)."""
    cx, cz = centre; ca, sa = math.cos(math.radians(yaw)), math.sin(math.radians(yaw)); W, D = size
    y = float(y if y is not None else H(cx, cz))
    L = lambda x, yy, z: np.array([cx + x * ca + z * sa, yy, cz - x * sa + z * ca])
    vis = MB(name); col = MB(name + '_collision')
    corners = [L(-W / 2, y, -D / 2), L(W / 2, y, -D / 2), L(W / 2, y, D / 2), L(-W / 2, y, D / 2)]
    vis.poly(corners, 'paving', [0, 1, 0], uvscale=3.0); col.poly(corners, 'c', [0, 1, 0])
    if samdo:
        vis.box(L(0, y + .08, 0), [4.6, .16, D], 'stone_plain', yaw=yaw, faces='xXzZY', uvscale=1.5)
        vis.box(L(0, y + .14, 0), [1.8, .12, D], 'stone_plain', yaw=yaw, faces='xXzZY', uvscale=1.2)
    for k in range(markers):
        zz = D / 2 - 8 - k * (D - 16) / max(1, markers - 1)
        for sx in (-1, 1): vis.box(L(sx * 6.5, y + .45, zz), [.38, .9, .28], 'stone_plain', yaw=yaw, faces='xXzZY', uvscale=1.0)
    return vis, col, dict(y=y)
