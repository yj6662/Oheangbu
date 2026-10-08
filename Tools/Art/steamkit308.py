"""#308 steam kit (D308-43, SPEC-ARCH-TEMPLE-308): steam-engine parts that are hung on the temple buildings and stone works.
Procedural meshes in Unity coordinates (x, y up, z), base at y = 0, written in the same json form as cliffway308.
Material slots: iron_dark, copper, brass, soot.
  python Tools/Art/steamkit308.py   -> Art/World/Compact/Rebuild/SteamKit308/Meshes/*.json + parts.json
"""
from __future__ import annotations
import json, math, sys
from pathlib import Path
import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
from cliffway308 import Mesh

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Art/World/Compact/Rebuild/SteamKit308'
UP = np.array([0., 1., 0.]); X = np.array([1., 0., 0.]); Z = np.array([0., 0., 1.])


def basis(axis):
    a = np.asarray(axis, float); a = a / np.linalg.norm(a)
    h = X if abs(a[0]) < .9 else UP
    u = np.cross(a, h); u /= np.linalg.norm(u); v = np.cross(a, u)
    return a, u, v


def tube(m: Mesh, p0, p1, r0, r1, mat, seg=20, cap0=False, cap1=False):
    """Cylinder / cone from p0 to p1."""
    p0 = np.asarray(p0, float); p1 = np.asarray(p1, float); a, u, v = basis(p1 - p0); L = np.linalg.norm(p1 - p0)
    th = np.linspace(0, 2 * math.pi, seg + 1)
    ring = np.cos(th)[:, None] * u + np.sin(th)[:, None] * v
    slope = (r0 - r1) / max(L, 1e-6)
    nrm = ring + a * slope; nrm /= np.linalg.norm(nrm, axis=1, keepdims=True)
    P = np.stack([p0 + ring * r0, p1 + ring * r1], 1)
    Nn = np.stack([nrm, nrm], 1)
    UV = np.stack([np.stack([th / (2 * math.pi) * max(1, round(2 * math.pi * r0)), np.zeros(seg + 1)], -1), np.stack([th / (2 * math.pi) * max(1, round(2 * math.pi * r0)), np.full(seg + 1, L)], -1)], 1)
    i = np.arange(seg); a_ = i * 2; b_ = a_ + 1; c_ = a_ + 3; d_ = a_ + 2
    m.add(P, Nn, UV, np.concatenate([np.stack([a_, b_, c_], -1), np.stack([a_, c_, d_], -1)]), mat)
    for cap, c, r, sgn in ((cap0, p0, r0, -1), (cap1, p1, r1, 1)):
        if not cap or r <= 0: continue
        Pc = np.concatenate([[c], c + ring[:-1] * r]); Nc = np.repeat((a * sgn)[None], seg + 1, 0)
        UVc = np.concatenate([[[.5, .5]], .5 + .5 * np.stack([np.cos(th[:-1]), np.sin(th[:-1])], -1)])
        t = np.stack([np.zeros(seg, int), 1 + i, 1 + (i + 1) % seg], -1)
        m.add(Pc, Nc, UVc, t, mat)


def dome(m: Mesh, c, axis, r, mat, seg=20, rings=6, squash=1.0):
    """Half sphere on the plane through c, bulging along axis."""
    c = np.asarray(c, float); a, u, v = basis(axis)
    th = np.linspace(0, 2 * math.pi, seg + 1); ph = np.linspace(0, math.pi / 2, rings + 1)
    P = np.zeros((rings + 1, seg + 1, 3)); Nn = np.zeros_like(P)
    for k, f in enumerate(ph):
        ring = np.cos(th)[:, None] * u + np.sin(th)[:, None] * v
        P[k] = c + ring * r * math.cos(f) + a * r * math.sin(f) * squash
        n = ring * math.cos(f) + a * math.sin(f); Nn[k] = n / np.linalg.norm(n, axis=1, keepdims=True)
    UV = np.stack(np.meshgrid(ph, th, indexing='ij'), -1)
    ii, jj = np.meshgrid(np.arange(rings), np.arange(seg), indexing='ij'); nv = seg + 1
    a_ = (ii * nv + jj).ravel(); b_ = a_ + nv; c_ = b_ + 1; d_ = a_ + 1
    m.add(P, Nn, UV, np.concatenate([np.stack([a_, b_, c_], -1), np.stack([a_, c_, d_], -1)]), mat)


def band(m, c, axis, r, w, t, mat, seg=20):
    """Raised hoop of width w and thickness t around a cylinder of radius r."""
    c = np.asarray(c, float); a, _, _ = basis(axis)
    tube(m, c - a * w / 2, c + a * w / 2, r + t, r + t, mat, seg)
    tube(m, c - a * w / 2, c - a * w / 2 - a * 1e-3, r + t, r, mat, seg); tube(m, c + a * w / 2, c + a * w / 2 + a * 1e-3, r + t, r, mat, seg)


def rivets(m, c, axis, r, n, size, mat):
    c = np.asarray(c, float); a, u, v = basis(axis)
    for k in range(n):
        th = 2 * math.pi * k / n; d = math.cos(th) * u + math.sin(th) * v
        m.box(c + d * (r + size * .3), a, d, np.cross(a, d), (size, size, size), mat, faces='xXYzZ')


def elbow(m, c, a0, a1, R, r, mat, seg=14, steps=8):
    """Quarter bend from direction a0 to a1 (unit, perpendicular), starting at c, bend radius R."""
    a0 = np.asarray(a0, float); a1 = np.asarray(a1, float); o = np.asarray(c, float) + a1 * R
    pts = [o - a1 * R * math.cos(t) + a0 * R * math.sin(t) for t in np.linspace(0, math.pi / 2, steps + 1)]
    for p, q in zip(pts[:-1], pts[1:]): tube(m, p, q, r, r, mat, seg)
    return pts[-1]


def torus(m: Mesh, c, axis, R, r, mat, seg=28, sides=8):
    c = np.asarray(c, float); a, u, v = basis(axis)
    th = np.linspace(0, 2 * math.pi, seg + 1); ph = np.linspace(0, 2 * math.pi, sides + 1)
    ring = np.cos(th)[:, None] * u + np.sin(th)[:, None] * v
    P = np.zeros((seg + 1, sides + 1, 3)); Nn = np.zeros_like(P)
    for k, f in enumerate(ph):
        n = ring * math.cos(f) + a * math.sin(f); P[:, k] = c + ring * R + n * r; Nn[:, k] = n
    UV = np.stack(np.meshgrid(th * R, ph * r, indexing='ij'), -1)
    ii, jj = np.meshgrid(np.arange(seg), np.arange(sides), indexing='ij'); nv = sides + 1
    a_ = (ii * nv + jj).ravel(); b_ = a_ + nv; c_ = b_ + 1; d_ = a_ + 1
    m.add(P, Nn, UV, np.concatenate([np.stack([a_, b_, c_], -1), np.stack([a_, c_, d_], -1)]), mat)


def gear(m: Mesh, c, axis, r, teeth, thick, mat, hub='brass'):
    c = np.asarray(c, float); a, u, v = basis(axis)
    tube(m, c - a * thick / 2, c + a * thick / 2, r * .86, r * .86, mat, max(16, teeth * 2), True, True)
    for k in range(teeth):
        th = 2 * math.pi * k / teeth; d = math.cos(th) * u + math.sin(th) * v; t = np.cross(a, d)
        m.box(c + d * (r * .93), d, a, t, (r * .16, thick, 2 * math.pi * r / teeth * .5), mat, faces='XyYzZ')
    tube(m, c - a * (thick / 2 + .03), c + a * (thick / 2 + .03), r * .22, r * .22, hub, 12, True, True)
    for k in range(4):                                                            # spoke relief plates
        th = math.pi / 4 + math.pi / 2 * k; d = math.cos(th) * u + math.sin(th) * v
        m.box(c + d * (r * .5) + a * (thick / 2 + .005), d, a, np.cross(a, d), (r * .34, .012, r * .22), 'soot', faces='Y')
        m.box(c + d * (r * .5) - a * (thick / 2 + .005), d, a, np.cross(a, d), (r * .34, .012, r * .22), 'soot', faces='y')


def part_stack():
    """Smokestack: base collar, tapered iron shaft with hoops, flared copper cap. 9 m tall (scaled in y by the dresser)."""
    m = Mesh('Steam_Stack')
    m.box([0, .25, 0], X, UP, Z, (1.5, .5, 1.5), 'iron_dark')
    tube(m, [0, .5, 0], [0, 8.2, 0], .52, .38, 'iron_dark', 16)
    for y in (1.2, 3.4, 5.6, 7.6):
        r = .52 + (.38 - .52) * (y - .5) / 7.7; band(m, [0, y, 0], UP, r, .14, .035, 'iron_dark', 16); rivets(m, [0, y, 0], UP, r + .035, 12, .04, 'brass')
    tube(m, [0, 8.2, 0], [0, 8.75, 0], .38, .62, 'copper', 16); tube(m, [0, 8.75, 0], [0, 9.0, 0], .62, .56, 'copper', 16)
    tube(m, [0, 8.2, 0], [0, 8.95, 0], .33, .33, 'soot', 12, False, True)
    return m


def part_boiler():
    """Horizontal riveted boiler on two saddles, steam dome, safety valve, firebox door. About 3.6 x 2.3 x 1.9 m."""
    m = Mesh('Steam_Boiler'); r = .85; y = 1.15
    tube(m, [-1.4, y, 0], [1.4, y, 0], r, r, 'iron_dark', 20)
    dome(m, [1.4, y, 0], X, r, 'iron_dark', 20, 5, .45); dome(m, [-1.4, y, 0], -X, r, 'iron_dark', 20, 5, .45)
    for x in (-1.35, -.45, .45, 1.35): band(m, [x, y, 0], X, r, .12, .03, 'iron_dark', 20); rivets(m, [x, y, 0], X, r + .03, 18, .04, 'brass')
    for x in (-.9, .9):
        m.box([x, .3, 0], X, UP, Z, (.35, .6, 1.5), 'iron_dark'); m.box([x, .68, 0], X, UP, Z, (.28, .25, 1.1), 'iron_dark')
    tube(m, [0, y + r - .05, 0], [0, y + r + .45, 0], .32, .32, 'copper', 14); dome(m, [0, y + r + .45, 0], UP, .32, 'copper', 14, 4, .8)
    tube(m, [.75, y + r - .05, 0], [.75, y + r + .35, 0], .06, .06, 'brass', 8); tube(m, [.75, y + r + .35, 0], [.75, y + r + .5, 0], .1, .04, 'brass', 8, True, True)
    m.box([1.4 + r * .45 + .02, y - .15, 0], X, UP, Z, (.06, .6, .7), 'soot'); m.box([1.4 + r * .45 + .05, y - .15, 0], X, UP, Z, (.04, .5, .08), 'brass')
    tube(m, [1.4 + r * .45 + .03, y + .45, .3], [1.4 + r * .45 + .1, y + .45, .3], .13, .13, 'brass', 12, False, True)   # gauge
    return m


def part_tank():
    """Upright copper tank on three legs with a domed top and a level tube. 2.9 m tall."""
    m = Mesh('Steam_Tank'); r = .7
    for k in range(3):
        th = 2 * math.pi * k / 3; p = np.array([math.cos(th) * r * .8, 0, math.sin(th) * r * .8]); m.box(p + UP * .3, X, UP, Z, (.14, .6, .14), 'iron_dark')
    tube(m, [0, .55, 0], [0, 2.3, 0], r, r, 'copper', 18, True); dome(m, [0, 2.3, 0], UP, r, 'copper', 18, 5, .6)
    for y in (.75, 1.45, 2.15): band(m, [0, y, 0], UP, r, .1, .025, 'iron_dark', 18); rivets(m, [0, y, 0], UP, r + .025, 14, .035, 'brass')
    tube(m, [0, 2.7, 0], [0, 2.92, 0], .09, .09, 'brass', 8, False, True)
    tube(m, [r + .06, .9, 0], [r + .06, 2.0, 0], .025, .025, 'brass', 6)
    return m


def part_pipe(length, name, r=.085):
    """Straight copper pipe along +x from 0, flanges at both ends and every 2 m, wall brackets toward -z."""
    m = Mesh(name)
    tube(m, [0, 0, 0], [length, 0, 0], r, r, 'copper', 12)
    n = max(1, int(round(length / 2.0)))
    for k in range(n + 1):
        x = length * k / n; band(m, [x, 0, 0], X, r, .07, .035, 'brass', 12)
    for k in range(n):
        x = length * (k + .5) / n; m.box([x, 0, -.14], X, UP, Z, (.06, .3, .28), 'iron_dark')
    return m


def part_riser(height, name, r=.085):
    """Vertical copper pipe from the ground to `height`, then a quarter bend toward +x."""
    m = Mesh(name)
    tube(m, [0, 0, 0], [0, height - .3, 0], r, r, 'copper', 12)
    for y in np.arange(.4, height - .4, 1.8): band(m, [0, y, 0], UP, r, .07, .035, 'brass', 12)
    elbow(m, [0, height - .3, 0], UP, X, .3, r, 'copper')
    m.box([0, .06, 0], X, UP, Z, (.34, .12, .34), 'iron_dark')
    return m


def part_gears():
    """Three meshed gears on a back plate, facing +z. About 2.6 x 2.2 m."""
    m = Mesh('Steam_Gears')
    m.box([0, 1.25, -.06], X, UP, Z, (2.5, 2.1, .08), 'iron_dark'); rivets(m, [0, 1.25, -.02], Z, 1.25, 10, .05, 'brass')
    gear(m, [-.45, 1.05, .1], Z, .78, 18, .12, 'iron_dark'); gear(m, [.62, 1.62, .1], Z, .5, 12, .12, 'brass', 'iron_dark'); gear(m, [.72, .62, .1], Z, .42, 10, .12, 'copper')
    return m


def part_valve():
    """Valve body with a hand wheel, facing +z. .5 m."""
    m = Mesh('Steam_Valve')
    tube(m, [-.2, 0, 0], [.2, 0, 0], .11, .11, 'brass', 12, True, True); tube(m, [0, 0, 0], [0, 0, .22], .035, .035, 'iron_dark', 8)
    a, u, v = basis(Z)
    torus(m, [0, 0, .22], Z, .2, .022, 'iron_dark')
    for k in range(4):
        t0 = math.pi / 2 * k; tube(m, [0, 0, .22], np.array([0, 0, .22]) + (math.cos(t0) * u + math.sin(t0) * v) * .2, .014, .014, 'iron_dark', 6)
    return m


def part_gauge():
    """Brass pressure gauge on a short stem, facing +z. .36 m."""
    m = Mesh('Steam_Gauge')
    tube(m, [0, -.3, 0], [0, -.12, 0], .025, .025, 'brass', 8)
    tube(m, [0, 0, -.04], [0, 0, .05], .18, .18, 'brass', 16, True, False); tube(m, [0, 0, .05], [0, 0, .051], .155, .155, 'soot', 16, False, True)
    m.box([.04, .03, .056], np.array([.8, .6, 0]), np.array([-.6, .8, 0]), Z, (.14, .012, .006), 'iron_dark')
    return m


def part_wheel():
    """Water wheel (수차) on an A frame, axis along x. 4.6 m across."""
    m = Mesh('Steam_WaterWheel'); R = 2.2; c = np.array([0., 2.5, 0.])
    a, u, v = basis(X)
    for sx in (-.45, .45):
        for k in range(24):
            t0 = 2 * math.pi * k / 24; t1 = 2 * math.pi * (k + 1) / 24
            tube(m, c + X * sx + (math.cos(t0) * u + math.sin(t0) * v) * R, c + X * sx + (math.cos(t1) * u + math.sin(t1) * v) * R, .06, .06, 'iron_dark', 6)
        for k in range(8):
            t0 = math.pi / 4 * k; tube(m, c + X * sx, c + X * sx + (math.cos(t0) * u + math.sin(t0) * v) * R, .045, .045, 'iron_dark', 6)
    for k in range(16):
        t0 = 2 * math.pi * k / 16; d = math.cos(t0) * u + math.sin(t0) * v
        m.box(c + d * (R - .05), X, d, np.cross(X, d), (.95, .5, .03), 'copper')
    tube(m, c - X * .8, c + X * .8, .11, .11, 'brass', 10, True, True)
    for sx in (-.75, .75):
        for sz in (-1, 1):
            tube(m, [sx, 0, sz * 1.3], c + X * sx, .09, .09, 'iron_dark', 8)
    return m


def part_collar():
    """Riveted iron collar, unit square 1 x 1 in plan and .35 m high (scaled to a stone work's footprint), with two pipe stubs."""
    m = Mesh('Steam_Collar')
    for sx, sz, ax in ((0, .5, X), (0, -.5, X), (.5, 0, Z), (-.5, 0, Z)):
        m.box([sx, .175, sz], ax, UP, np.cross(ax, UP), (1.04, .35, .04), 'iron_dark')
        for f in (-.4, -.2, 0, .2, .4):
            p = np.array([sx, .28, sz]) + ax * f; m.box(p + np.cross(ax, UP) * (.025 if (sx + sz) < 0 else -.025) * -1, ax, UP, np.cross(ax, UP), (.03, .03, .03), 'brass')
    return m


def part_roofstack():
    """Stack that comes out of a roof slope: y = 0 is the roof surface. The shaft goes 2 m down into the roof, a copper flashing
    skirt covers the cut, a spark-arrester cage and a conical hat sit on top. 6 m above the roof (scaled in y by the dresser)."""
    m = Mesh('Steam_RoofStack')
    tube(m, [0, -2.0, 0], [0, 5.4, 0], .36, .30, 'iron_dark', 20)
    tube(m, [0, -.35, 0], [0, .28, 0], 1.05, .40, 'copper', 20); tube(m, [0, .28, 0], [0, .40, 0], .40, .37, 'copper', 20)
    for y in (1.1, 2.6, 4.1):
        r = .36 + (.30 - .36) * (y + 2) / 7.4; band(m, [0, y, 0], UP, r, .11, .03, 'iron_dark'); rivets(m, [0, y, 0], UP, r + .03, 12, .035, 'brass')
    tube(m, [0, 5.4, 0], [0, 5.55, 0], .30, .40, 'copper', 20)
    for k in range(10):
        th = 2 * math.pi * k / 10; d = np.array([math.cos(th), 0, math.sin(th)]); tube(m, np.array([0, 5.55, 0]) + d * .38, np.array([0, 5.95, 0]) + d * .38, .014, .014, 'brass', 6)
    tube(m, [0, 5.95, 0], [0, 6.0, 0], .46, .46, 'copper', 20, True); tube(m, [0, 6.0, 0], [0, 6.3, 0], .46, .05, 'copper', 20)
    tube(m, [0, 5.0, 0], [0, 5.5, 0], .26, .26, 'soot', 14, False, True)
    return m


def part_ridgepipe():
    """Copper pipe that rides a roof ridge along x (-4..4), on saddles, with three small whistle vents."""
    m = Mesh('Steam_RidgePipe8')
    tube(m, [-4, .22, 0], [4, .22, 0], .10, .10, 'copper', 14, True, True)
    for x in np.linspace(-3.6, 3.6, 7):
        band(m, [x, .22, 0], X, .10, .08, .03, 'brass', 14)
        m.box([x, .06, 0], X, UP, Z, (.10, .16, .46), 'iron_dark')
    for x in (-2.4, 0, 2.4):
        tube(m, [x, .30, 0], [x, .75, 0], .045, .045, 'brass', 10); tube(m, [x, .75, 0], [x, .88, 0], .045, .09, 'brass', 10); tube(m, [x, .88, 0], [x, .93, 0], .09, .02, 'brass', 10)
    return m


def part_gablegear():
    """Big gear, pinion and a crank rod for a gable end, facing +z, centred on the big gear's axle at the origin."""
    m = Mesh('Steam_GableGear')
    gear(m, [0, 0, .12], Z, 1.25, 24, .14, 'iron_dark'); gear(m, [1.42, .62, .12], Z, .42, 9, .14, 'brass', 'iron_dark')
    torus(m, [0, 0, .12], Z, .62, .04, 'copper')
    tube(m, [0, 0, -.25], [0, 0, .32], .10, .10, 'brass', 14, True, True); tube(m, [1.42, .62, -.25], [1.42, .62, .3], .06, .06, 'iron_dark', 10, True, True)
    tube(m, [.75, 0, .26], [-1.2, -1.5, .26], .04, .04, 'iron_dark', 8); tube(m, [.75, 0, .2], [.75, 0, .32], .07, .07, 'brass', 10, True, True)
    return m


def part_bundle():
    """Three pipes of different bore on shared brackets, along +x from 0 to 8, wall toward -z."""
    m = Mesh('Steam_PipeBundle8')
    for y, r, mat in ((.22, .11, 'copper'), (0, .075, 'copper'), (-.17, .05, 'brass')):
        tube(m, [0, y, 0], [8, y, 0], r, r, mat, 14, True, True)
        for x in np.linspace(0, 8, 5): band(m, [x, y, 0], X, r, .06, .028, 'brass', 14)
    for x in np.linspace(1, 7, 4):
        m.box([x, .02, -.16], X, UP, Z, (.07, .62, .30), 'iron_dark'); m.box([x, .02, .0], X, UP, Z, (.05, .6, .03), 'iron_dark')
    return m


def part_vent():
    """Small steam vent: stem, bulb, cap. .6 m. Stands on y = 0."""
    m = Mesh('Steam_Vent')
    tube(m, [0, 0, 0], [0, .32, 0], .04, .04, 'copper', 10); tube(m, [0, .32, 0], [0, .44, 0], .04, .10, 'brass', 12); tube(m, [0, .44, 0], [0, .52, 0], .10, .10, 'brass', 12)
    tube(m, [0, .52, 0], [0, .62, 0], .13, .02, 'copper', 12)
    return m


def part_band(square):
    """Copper band around a stone body, unit size (1 x 1 in plan), .14 high, with rivets."""
    m = Mesh('Steam_BandSquare' if square else 'Steam_BandRound')
    if square:
        for sx, sz, ax in ((0, .5, X), (0, -.5, X), (.5, 0, Z), (-.5, 0, Z)):
            n = np.cross(ax, UP); c0 = np.array([sx, 0., sz]); out = c0 / np.linalg.norm(c0)
            m.box(c0, ax, UP, n, (1.03, .14, .03), 'copper')
            for f in (-.36, -.12, .12, .36): m.box(c0 + ax * f + out * .018, ax, UP, n, (.022, .022, .022), 'brass')
    else:
        tube(m, [0, -.07, 0], [0, .07, 0], .515, .515, 'copper', 24)
        tube(m, [0, -.07, 0], [0, -.071, 0], .515, .5, 'copper', 24); tube(m, [0, .07, 0], [0, .071, 0], .515, .5, 'copper', 24)
        rivets(m, [0, 0, 0], UP, .515, 12, .022, 'brass')
    return m


def part_striker():
    """Mechanical bell striker: frame, gear, cam and a swinging log hammer on chains. 2.6 m tall, hammer swings along x."""
    m = Mesh('Steam_BellStriker')
    for sx in (-.9, .9):
        tube(m, [sx, 0, -.5], [sx, 2.5, 0], .06, .06, 'iron_dark', 10); tube(m, [sx, 0, .5], [sx, 2.5, 0], .06, .06, 'iron_dark', 10)
    tube(m, [-1.0, 2.5, 0], [1.0, 2.5, 0], .07, .07, 'brass', 12, True, True)
    gear(m, [-.9, 1.5, 0], X, .5, 14, .1, 'iron_dark'); gear(m, [-.9, .85, .42], X, .26, 8, .1, 'brass', 'iron_dark')
    tube(m, [-.3, 1.25, 0], [1.9, 1.25, 0], .16, .16, 'copper', 16, True, True)
    band(m, [1.7, 1.25, 0], X, .16, .12, .03, 'brass', 16); band(m, [.0, 1.25, 0], X, .16, .12, .03, 'brass', 16)
    for x in (.1, 1.5): tube(m, [x, 1.4, 0], [x, 2.45, 0], .018, .018, 'iron_dark', 6)
    tube(m, [-.9, 1.5, 0], [-.3, 1.25, 0], .035, .035, 'iron_dark', 8)
    return m


def part_prayerwheel():
    """Geared sutra wheel (윤장대): octagonal copper drum on a vertical axle, bevel gear under it, drive shaft toward -x. 3.4 m."""
    m = Mesh('Steam_PrayerWheel')
    tube(m, [0, 0, 0], [0, .35, 0], 1.25, 1.15, 'iron_dark', 8, False, True)
    gear(m, [0, .5, 0], UP, 1.0, 20, .14, 'brass', 'iron_dark'); gear(m, [-1.02, .5, 0], X, .34, 9, .12, 'iron_dark')
    tube(m, [-3.2, .5, 0], [-1.0, .5, 0], .06, .06, 'iron_dark', 10)
    tube(m, [0, .35, 0], [0, 3.3, 0], .09, .09, 'iron_dark', 12, False, True)
    tube(m, [0, .85, 0], [0, 2.75, 0], .95, .95, 'copper', 8, True, True)
    for y in (.95, 1.8, 2.65): band(m, [0, y, 0], UP, .95, .12, .03, 'brass', 8)
    tube(m, [0, 2.75, 0], [0, 3.05, 0], 1.1, .25, 'copper', 8)
    for k in range(4):
        th = math.pi / 2 * k; d = np.array([math.cos(th), 0, math.sin(th)]); tube(m, UP * 1.3 + d * .95, UP * 1.3 + d * 1.45, .03, .03, 'brass', 8)
    return m


def textures():
    """Tileable 512 px albedo + metallic/smoothness maps: verdigris on copper, rust and soot on iron, tarnish on brass."""
    from PIL import Image
    out = OUT / 'Textures'; out.mkdir(parents=True, exist_ok=True); n = 512; rng = np.random.default_rng(30843)

    def noise(scale, octaves=4):
        a = np.zeros((n, n))
        for o in range(octaves):
            k = max(2, int(scale * 2 ** o)); g = rng.random((k, k)); g = np.concatenate([g, g[:1]], 0); g = np.concatenate([g, g[:, :1]], 1)
            x = np.linspace(0, k, n, endpoint=False); i = x.astype(int); f = x - i; f = f * f * (3 - 2 * f)
            r0 = g[i][:, i] * (1 - f)[None] + g[i][:, i + 1] * f[None]; r1 = g[i + 1][:, i] * (1 - f)[None] + g[i + 1][:, i + 1] * f[None]
            a += (r0 * (1 - f)[:, None] + r1 * f[:, None]) / 2 ** o
        return (a - a.min()) / (a.max() - a.min())

    def save(name, rgb, metal, smooth):
        Image.fromarray((np.clip(rgb, 0, 1) * 255).astype(np.uint8)).save(out / (name + '_BC.png'))
        ms = np.stack([metal, metal, metal, smooth], -1); Image.fromarray((np.clip(ms, 0, 1) * 255).astype(np.uint8), 'RGBA').save(out / (name + '_MS.png'))

    def lerp(a, b, t): return a + (b - a) * t[..., None]

    base = lerp(np.array([.62, .34, .19]), np.array([.36, .19, .11]), noise(6))
    col = noise(3, 3)[0][None, :].repeat(n, 0)
    streak = np.clip((col * .6 + noise(10) * .7) - .62, 0, 1) * 3.2
    streak = np.clip(streak + np.clip(noise(4) - .68, 0, 1) * 4, 0, 1)
    rgb = lerp(base, lerp(np.array([.30, .56, .50]), np.array([.55, .74, .66]), noise(16)), streak * .85)
    save('copper', rgb, .9 * (1 - streak * .9), .5 * (1 - streak * .8) * (.6 + .4 * noise(12)))
    base = lerp(np.array([.10, .10, .10]), np.array([.21, .20, .19]), noise(8))
    rust = np.clip(np.clip(noise(5) * .7 + noise(22) * .5 - .72, 0, 1) * 4.0, 0, 1)
    rgb = lerp(base, lerp(np.array([.33, .15, .07]), np.array([.46, .25, .12]), noise(18)), rust * .8)
    save('iron_dark', rgb, .7 * (1 - rust * .9), .34 * (1 - rust * .7) * (.5 + .5 * noise(14)))
    base = lerp(np.array([.72, .56, .22]), np.array([.45, .32, .12]), np.clip(noise(7) * 1.3 - .2, 0, 1))
    save('brass', base, np.full((n, n), .9), .55 * (.45 + .55 * noise(10)))
    save('soot', lerp(np.array([.03, .03, .03]), np.array([.08, .075, .07]), noise(9)), np.zeros((n, n)), np.full((n, n), .06))


def main():
    (OUT / 'Meshes').mkdir(parents=True, exist_ok=True)
    parts = [part_stack(), part_boiler(), part_tank(), part_pipe(4.0, 'Steam_Pipe4'), part_pipe(8.0, 'Steam_Pipe8'), part_riser(4.0, 'Steam_Riser4'),
             part_gears(), part_valve(), part_gauge(), part_wheel(), part_collar(),
             part_roofstack(), part_ridgepipe(), part_gablegear(), part_bundle(), part_vent(), part_band(True), part_band(False), part_striker(), part_prayerwheel()]
    textures()
    info = {}
    for m in parts:
        d = m.data(); (OUT / 'Meshes' / (m.name + '.json')).write_text(json.dumps(d), encoding='utf-8')
        V = np.array(d['vertices']).reshape(-1, 3); v, t = m.count()
        info[m.name] = dict(vertices=int(v), triangles=int(t), min=V.min(0).round(2).tolist(), max=V.max(0).round(2).tolist())
        print(m.name, v, t, info[m.name]['min'], info[m.name]['max'])
    (OUT / 'parts.json').write_text(json.dumps(info, indent=1), encoding='utf-8')


if __name__ == '__main__':
    main()
