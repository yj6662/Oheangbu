"""#297 palace parts: 궁장 (tile-capped palace wall), 월대 (stepped stone terrace with balustrades and stair flights),
박석 court paving (+ raised 삼도 and 품계석 rows). World-space meshes; terrain from hanok297_wall.H.
"""
from __future__ import annotations
import math
import numpy as np
from hanok297 import MB, beam, nz, TILE_W, TILE_H
from hanok297_wall import H, resample


def palace_wall(name, path, height=4.6, thickness=1.1, base_h=1.0, lod=0, step=2.0, closed=False, s0=0.0, caps=(False, False)):
    """궁장: stone base course + plastered body + a small tiled gable coping (기와 지붕) along the top.
    The top follows a smoothed terrain line (stepped where steep). Collision: the wall body. Returns (vis, col, info).
    caps=(start, end): close a free wall end (gate gap) with its cross-section (visual + collision)."""
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
    for k, ok in ((0, caps[0]), (-1, caps[1])):
        if not ok: continue
        dd = -d[0] if k == 0 else d[-1]; out = [dd[0], 0, dd[1]]; nn = n[k]; p = P[k]; gk, tk = float(g[k]), float(top[k])
        Xk = lambda off, yy: [p[0] + nn[0] * off, yy, p[1] + nn[1] * off]
        base = [Xk(-t - .06, gk - .8), Xk(t + .06, gk - .8), Xk(t + .06, gk + base_h), Xk(-t - .06, gk + base_h)]
        body = [Xk(-t, gk + base_h), Xk(t, gk + base_h), Xk(t, tk), Xk(-t, tk)]
        vis.poly(base, 'stone_dressed', out, uvscale=2.0); vis.poly(body, 'plaster', out, uvscale=1.5)
        vis.poly([Xk(-t - eave, tk - .05), Xk(t + eave, tk - .05), Xk(0, tk + rise)], 'wood_board', out, uvscale=1.0)
        col.poly([Xk(-t - .06, gk - .8), Xk(t + .06, gk - .8), Xk(t, tk), Xk(-t, tk)], 'c', out)
    info = dict(length=float(s[-1]), top=[float(top.min()), float(top.max())], ground=[float(g.min()), float(g.max())])
    return vis, col, info


def balustrade(vis, col, a, b, y, lod=0, h=.95, post=2.2):
    """난간석 along a -> b (XZ) standing on height y: posts + top rail + low rail; collision = one box (h + .1)."""
    a = np.asarray(a, float); b = np.asarray(b, float); L = float(np.linalg.norm(b - a))
    if L < .3: return
    m = max(1, int(round(L / post))); P = lambda u: np.array([a[0] + (b[0] - a[0]) * u, y, a[1] + (b[1] - a[1]) * u])
    beam(vis, P(0) + [0, h - .08, 0], P(1) + [0, h - .08, 0], .16, .14, 'stone_plain')
    if lod < 2:
        beam(vis, P(0) + [0, .22, 0], P(1) + [0, .22, 0], .12, .12, 'stone_plain')
        for i in range(m + 1):
            q = P(i / m); vis.box((q + [0, h / 2, 0]).tolist(), [.22, h, .22], 'stone_plain', yaw=math.degrees(math.atan2(b[0] - a[0], b[1] - a[1])), faces='xXzZY', uvscale=1.0)
    c = (P(0) + P(1)) / 2; col.box((c + [0, (h + .1) / 2, 0]).tolist(), [.26, h + .1, L], 'c', yaw=math.degrees(math.atan2(b[0] - a[0], b[1] - a[1])), faces='xXzZY')


def terrace_wall(name, x0, x1, z_face, y_low, y_high, depth=4.5, facing=1, openings=(), lod=0, rail=True, step=2.0):
    """축대: stone retaining block along x between two court levels. z_face = the visible face; the lower court lies on
    the `facing` (+1 = +z) side. The block top (y_high, walkable collision) spans `depth` behind the face, covering the
    lower pad's blend into the upper court; the face runs down below the lower ground. Balustrade (난간석) on the edge
    except the `openings` [(xa, xb)] (stair heads). Returns (vis, col, info)."""
    vis, col = MB(name), MB(name + '_collision'); zf, zb = z_face, z_face - facing * depth
    xs = np.linspace(x0, x1, max(2, int(math.ceil((x1 - x0) / [step, step * 2, step * 4][lod])) + 1))
    low = np.minimum(y_low, H(xs, np.full(len(xs), zf + facing * .8))) - .5
    A = np.stack([xs, low, np.full(len(xs), zf)], -1); B = np.stack([xs, np.full(len(xs), y_high - .2), np.full(len(xs), zf)], -1)
    Q = np.stack([A, B], 1); uv = np.stack([np.repeat(xs[:, None] / 2.0, 2, 1), Q[..., 1] / 2.0], -1)
    vis.grid(Q, 'stone_dressed', uv, np.array([0, 0, facing]))
    Qc = np.stack([A, np.stack([xs, np.full(len(xs), y_high), np.full(len(xs), zf)], -1)], 1); col.grid(Qc, 'c', uv, np.array([0, 0, facing]))
    top = [[x0, y_high, zb], [x1, y_high, zb], [x1, y_high, zf], [x0, y_high, zf]]
    vis.poly(top, 'paving' if lod == 0 else 'stone_plain', [0, 1, 0], uvscale=3.0); col.poly(top, 'c', [0, 1, 0])
    vis.box([(x0 + x1) / 2, y_high - .11, zf + facing * .02], [x1 - x0 + .14, .22, .34], 'stone_plain', faces='xXyzZ', uvscale=1.0)   # 갑석 band
    back = [[x0, y_high - .6, zb], [x1, y_high - .6, zb], [x1, y_high, zb], [x0, y_high, zb]]; vis.poly(back, 'stone_dressed', [0, 0, -facing], uvscale=2.0)
    for x, s in ((x0, -1), (x1, 1)):
        lo_ = float(min(y_low, H(x, zf + facing * .8))) - .5
        end = [[x, lo_, zf], [x, y_high, zf], [x, y_high, zb], [x, lo_, zb]]
        vis.poly(end, 'stone_dressed', [s, 0, 0], uvscale=2.0); col.poly(end, 'c', [s, 0, 0])
    if rail:
        zr = zf - facing * .3; segs = [(x0 + .25, x1 - .25)]
        for xa, xb in sorted(openings):
            nxt = []
            for a, b in segs:
                if xb <= a or xa >= b: nxt.append((a, b)); continue
                if xa > a: nxt.append((a, xa))
                if xb < b: nxt.append((xb, b))
            segs = nxt
        for a, b in segs: balustrade(vis, col, (a, zr), (b, zr), y_high, lod)
    return vis, col, dict(top=y_high, face=zf, back=zb, low=[float(low.min()), float(low.max())])


def revetment(name, a, b, face, y_low, depth=2.6, lod=0, step=2.0):
    """석축 revetment along a -> b (XZ) holding the ground behind a cut court. `face` = unit XZ normal toward the lower
    court. The top follows the upper ground depth + .6 m behind the face (never below y_low + .4), the face runs down
    below the lower ground; the block top covers the pad blend behind it. Collision: face + top + ends."""
    a = np.asarray(a, float); b = np.asarray(b, float); n = nz(np.asarray(face, float)); L = float(np.linalg.norm(b - a))
    vis, col = MB(name), MB(name + '_collision')
    s = np.linspace(0, L, max(2, int(math.ceil(L / [step, step * 2, step * 4][lod])) + 1)); P = a + (b - a) * (s / L)[:, None]
    top = np.maximum(H(*(P - n * (depth + .6)).T) + .05, y_low + .4)
    for _ in range(2): top[1:-1] = .25 * top[:-2] + .5 * top[1:-1] + .25 * top[2:]
    low = np.minimum(y_low, H(*(P + n * .8).T)) - .5
    X = lambda off, yy: np.stack([P[:, 0] + n[0] * off, yy, P[:, 1] + n[1] * off], -1)
    out = np.array([n[0], 0, n[1]])
    Q = np.stack([X(0, low), X(0, top)], 1); uv = np.stack([np.repeat(s[:, None] / 2.0, 2, 1), Q[..., 1] / 2.0], -1)
    vis.grid(Q, 'stone_rough', uv, out); col.grid(Q, 'c', uv, out)
    Qt = np.stack([X(0, top + .02), X(-depth, top + .02)], 1); uvt = np.stack([np.repeat(s[:, None] / 2.0, 2, 1), np.repeat(np.array([[0, depth / 2.0]]), len(s), 0)], -1)
    vis.grid(Qt, 'stone_plain', uvt, np.array([0, 1, 0])); col.grid(Qt, 'c', uvt, np.array([0, 1, 0]))
    Qb = np.stack([X(-depth, top - 1.0), X(-depth, top + .02)], 1); vis.grid(Qb, 'stone_rough', uv, -out)
    for k, sg in ((0, -1), (-1, 1)):
        dd = (b - a) / L * sg; o = [dd[0], 0, dd[1]]; p0 = P[k]
        end = [[p0[0], low[k], p0[1]], [p0[0], top[k] + .02, p0[1]], [p0[0] - n[0] * depth, top[k] + .02, p0[1] - n[1] * depth], [p0[0] - n[0] * depth, low[k], p0[1] - n[1] * depth]]
        vis.poly(end, 'stone_rough', o, uvscale=2.0); col.poly(end, 'c', o)
    return vis, col, dict(length=L, top=[float(top.min()), float(top.max())], height=[float((top - low).min()), float((top - low).max())])


def grand_stair(name, x_c, z0, y0, y1, width, facing=1, lod=0, grade=.5, sides=True):
    """Monumental stone flight from the terrace edge (x_c, y0, z0) down `facing` (+1 = +z) to y1: solid step blocks
    (risers <= .17 m, treads ~.34 m), 소맷돌 side slabs following the flight, collision = one ramp (27 deg).
    Returns (vis, col, info)."""
    vis, col = MB(name), MB(name + '_collision'); drop = y0 - y1; run = drop / grade; n = max(2, int(math.ceil(drop / .17))); tread = run / n
    base = y1 - .35
    for i in range(n):
        yt = y0 - drop * (i + 1) / n; zc = z0 + facing * tread * (i + .5)
        vis.box([x_c, (yt + base) / 2, zc], [width, yt - base, tread + .01], 'stone_plain', faces='xXzZY', uvscale=1.0)
    z1 = z0 + facing * run
    col.quad([x_c - width / 2, y0, z0], [x_c + width / 2, y0, z0], [x_c + width / 2, y1 + .02, z1], [x_c - width / 2, y1 + .02, z1], 'c', [0, 1, facing])
    col.quad([x_c - width / 2, y0, z0 - facing * .6], [x_c + width / 2, y0, z0 - facing * .6], [x_c + width / 2, y0, z0], [x_c - width / 2, y0, z0], 'c', [0, 1, 0])
    if sides:
        for sx in (-1, 1):
            x = x_c + sx * (width / 2 + .3)
            for xf, o in ((x - .3, -1), (x + .3, 1)):
                vis.poly([[xf, base, z0], [xf, base, z1], [xf, y1 + .55, z1 + facing * .3], [xf, y0 + .55, z0]], 'stone_dressed', [o, 0, 0], uvscale=2.0)
            vis.poly([[x - .3, y0 + .55, z0], [x + .3, y0 + .55, z0], [x + .3, y1 + .55, z1 + facing * .3], [x - .3, y1 + .55, z1 + facing * .3]], 'stone_plain', [0, 1, facing * .5], uvscale=1.0)
            vis.poly([[x - .3, base, z1 + facing * .3], [x + .3, base, z1 + facing * .3], [x + .3, y1 + .55, z1 + facing * .3], [x - .3, y1 + .55, z1 + facing * .3]], 'stone_dressed', [0, 0, facing], uvscale=1.0)
            col.box([x, (base + y0 + .55) / 2, (z0 + z1) / 2], [.6, y0 + .55 - base, abs(z1 - z0)], 'c', faces='xX')
    return vis, col, dict(top=[x_c, y0, z0], foot=[x_c, y1, z1], steps=n, riser=drop / n, tread=tread)


def timber_stair(name, top, bottom, width=1.4, lod=0, riser=.18):
    """Straight wooden stair (널계단) between two 3D points: boards + stringers + posts down to the ground; collision =
    one ramp. Returns (vis, col, info)."""
    top = np.asarray(top, float); bottom = np.asarray(bottom, float); vis, col = MB(name), MB(name + '_collision')
    d = bottom - top; dh = np.array([d[0], 0, d[2]]); L = float(np.linalg.norm(dh)); u = dh / L; side = np.array([u[2], 0, -u[0]])
    n = max(2, int(math.ceil((top[1] - bottom[1]) / riser))); tread = L / n; yaw = math.degrees(math.atan2(u[0], u[2]))
    for i in range(n):
        p = top + u * tread * (i + .5); p[1] = top[1] - (top[1] - bottom[1]) * (i + 1) / n
        vis.box([p[0], p[1] - .04, p[2]], [width, .08, tread + .03], 'wood_board', yaw=yaw, faces='xXyYzZ', uvscale=1.0)
    for s in (-1, 1): beam(vis, top + side * s * width / 2 + [0, -.12, 0], bottom + side * s * width / 2 + [0, -.12, 0], .08, .26, 'wood_dark')
    if lod < 2:
        for s in (-1, 1): beam(vis, top + side * s * width / 2 + [0, .9, 0], bottom + side * s * width / 2 + [0, .9, 0], .07, .07, 'wood_dark')
        for k in range(1, int(L / 2.5) + 1):
            p = top + (bottom - top) * (k * 2.5 / L)
            for s in (-1, 1):
                q = p + side * s * width / 2; g = float(H(q[0], q[2]))
                if q[1] - g > .5: beam(vis, [q[0], g - .2, q[2]], [q[0], q[1] - .2, q[2]], .12, .12, 'wood_dark')
    a, b = top - side * width / 2, top + side * width / 2; c, e = bottom + side * width / 2, bottom - side * width / 2
    col.quad(a.tolist(), b.tolist(), c.tolist(), e.tolist(), 'c', [0, 1, 0])
    return vis, col, dict(steps=n, riser=(top[1] - bottom[1]) / n, tread=tread, length=L)


def plank_walk(name, a, b, width=1.4, lod=0, post=.4):
    """Raised plank walkway (지붕길) from a to b (3D, same height) on short posts; collision = the deck top."""
    a = np.asarray(a, float); b = np.asarray(b, float); vis, col = MB(name), MB(name + '_collision')
    d = b - a; L = float(np.linalg.norm(d)); u = d / L; side = np.array([u[2], 0, -u[0]]); yaw = math.degrees(math.atan2(u[0], u[2]))
    nb = max(1, int(L / .5))
    for i in range(nb):
        p = a + d * (i + .5) / nb; vis.box([p[0], p[1] - .04, p[2]], [width, .08, L / nb + .02], 'wood_board', yaw=yaw, faces='xXyYzZ', uvscale=1.0)
    for s in (-1, 1): beam(vis, a + side * s * (width / 2 - .1) + [0, -.16, 0], b + side * s * (width / 2 - .1) + [0, -.16, 0], .1, .16, 'wood_dark')
    if lod < 2:
        for k in range(int(L / 2.0) + 1):
            p = a + u * min(k * 2.0, L); beam(vis, p + [0, -post - .1, 0], p + [0, -.2, 0], .12, .12, 'wood_dark')
    c = [a - side * width / 2, a + side * width / 2, b + side * width / 2, b - side * width / 2]
    col.poly([q.tolist() for q in c], 'c', [0, 1, 0])
    return vis, col, dict(length=L)


def wooltae(name, centre, yaw, size, tiers=2, tier_h=1.05, step_in=3.0, stairs=('front',), lod=0, rail=True, rail_sides=('front', 'back', 'left', 'right')):
    """월대: stacked stone platforms (dressed faces, capping slab, stone balustrade on each tier edge) with stair flights;
    the front flight has a central carved 답도 slab. centre=(x, ground y, z) — tiers rise from the ground y.
    rail_sides: tier edges that get the balustrade (leave out the side that meets the hall front)."""
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
                if side not in rail_sides: continue
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
