"""#308 spell VFX overhaul (D308-52): flung-ink forms. Blender 5.0, headless.

  blender --background --factory-startup --python Tools/Blender/SpellVFX308/inkforms308.py -- <preset|all> [--cell 256] [--ss 3] [--frames 16]

Where flipbook308.py bakes a gas (soft, lobed - it reads as smoke), this one throws INK: every frame is rebuilt as solid geometry
(round drops, tapered thrown tails, straight splinters, faceted chunks) moved by a small ballistic model, and rendered flat and
orthographic on a transparent film. The picture is only a silhouette plus one data colour: red = heat (fire), green = 1.
The wet look (fused necks, roughened edge, the erosion field the game shader thins the stroke with) is made afterwards by
Tools/Art/SpellVFX308/stylise308.py (or the After Effects pass) from these frames.
Timing is cel timing: one frame = 1/12 s. Output: Art/SpellVFX308/Forms/<preset>/f###.png at cell*ss pixels.
"""
import math
import random
import sys
from pathlib import Path

import bmesh
import bpy
from mathutils import Matrix, Vector

ROOT = Path(__file__).resolve().parents[3]
argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
WHICH = argv[0] if argv else 'all'


def opt(name, default):
    return type(default)(argv[argv.index(name) + 1]) if name in argv else default


CELL, SS, FRAMES = opt('--cell', 256), opt('--ss', 3), opt('--frames', 16)
OUTDIR = opt('--out', 'Forms')          # second pass (bleeding ink, stylised by bleed308.py): --out Forms2 --drops 0.5
THICK = opt('--thick', 1.0)             # metal rays: thicker, so the sharp lines survive the distance (3rd pass: --thick 1.6)
DROPS = opt('--drops', 1.0)             # fewer flung drops and spray: the bleed is the subject there
DT = 1.0 / 12.0
VIEW = 3.2            # metres across one cell (the game scales the quad; this is only the model's own unit)


# ---------------------------------------------------------------- geometry helpers (one mesh per frame)
class Frame:
    def __init__(self):
        self.bm = bmesh.new()
        self.col = self.bm.loops.layers.color.new('Col')

    def _paint(self, verts, heat):
        faces = {f for v in verts for f in v.link_faces}
        for f in faces:
            for l in f.loops:
                if l.vert in self._new:
                    l[self.col] = (heat, 1.0, 0.0, 1.0)

    def ball(self, p, r, heat=0.0, squash=(1, 1, 1), facet=False):
        if r <= .002:
            return
        m = Matrix.Translation(p) @ Matrix.Diagonal((r * squash[0], r * squash[1], r * squash[2], 1))
        g = bmesh.ops.create_icosphere(self.bm, subdivisions=1 if facet else 3, radius=1.0, matrix=m)
        self._new = set(g['verts']); self._paint(g['verts'], heat)

    def cone(self, a, b, ra, rb, heat=0.0):
        """a tapered stroke from a (radius ra) to b (radius rb)"""
        a, b = Vector(a), Vector(b)
        d = b - a
        if d.length < 1e-4 or max(ra, rb) <= .002:
            return
        rot = Vector((0, 0, 1)).rotation_difference(d.normalized()).to_matrix().to_4x4()
        m = Matrix.Translation((a + b) * .5) @ rot
        g = bmesh.ops.create_cone(self.bm, cap_ends=True, segments=10, radius1=max(ra, .0015), radius2=max(rb, .0015), depth=d.length, matrix=m)
        self._new = set(g['verts']); self._paint(g['verts'], heat)

    def chunk(self, p, r, rot, heat=0.0):
        m = Matrix.Translation(p) @ rot @ Matrix.Diagonal((r, r * .8, r * .65, 1))
        g = bmesh.ops.create_cube(self.bm, size=1.6, matrix=m)
        self._new = set(g['verts']); self._paint(g['verts'], heat)

    def needle(self, c, d, length, r, heat=0.0):
        """a straight splinter, pointed at both ends, centred at c along d"""
        c, d = Vector(c), Vector(d).normalized()
        self.cone(c, c + d * length * .5, r, 0.0, heat)
        self.cone(c, c - d * length * .5, r, 0.0, heat)


class Drop:
    """a flung drop: thrown at `speed` along `d`, slowed by drag, pulled by gravity; leaves a tail that thins and snaps"""
    def __init__(self, rnd, d, speed, r, drag=4.0, gravity=3.0, birth=0.0, tail=.22, life=1.4, heat=0.0, origin=(0, 0, 0)):
        self.d = Vector(d).normalized(); self.speed = speed; self.r = r; self.drag = drag; self.g = gravity
        self.birth = birth; self.tail = tail; self.life = life; self.heat = heat; self.o = Vector(origin)
        self.snap = rnd.uniform(.18, .42)          # when the thread to the body breaks
        self.wob = rnd.uniform(0, 6.28)

    def pos(self, t):
        t = max(0.0, t - self.birth)
        k = (1 - math.exp(-self.drag * t)) / self.drag
        return self.o + self.d * (self.speed * k) + Vector((0, 0, -.5 * self.g * t * t))

    def draw(self, f, t, shrink=1.0):
        age = t - self.birth
        if age < 0 or age > self.life:
            return
        fade = max(0.0, 1.0 - (age / self.life) ** 2.2)
        r = self.r * shrink * (.55 + .45 * fade)
        head = self.pos(t)
        f.ball(head, r, self.heat)
        # tail: a taper back along the path it flew; long when fast, gone once the thread has snapped and been swallowed
        tl = self.tail * max(0.0, 1.0 - age / (self.snap * 2.2))
        if tl > .01:
            steps = 5; prev = head; pr = r * .82
            for i in range(1, steps + 1):
                q = self.pos(t - tl * i / steps); qr = r * .82 * (1 - i / steps) ** 1.3
                f.cone(prev, q, pr, qr, self.heat); prev, pr = q, qr
        elif age < self.snap * 3.2:
            # the snapped thread pulls into one or two beads that trail behind
            for j, lag in enumerate((.07, .13)):
                f.ball(self.pos(t - lag) + Vector((0, 0, -.01 * j)), r * (.34 - .1 * j) * fade, self.heat)


def few(n):
    """how many flung drops / spray specks (scaled by --drops; shards, rays and crown points are not scaled)"""
    return max(3, round(n * DROPS))


def ring_dirs(rnd, n, lo=0.0, hi=360.0, depth=.3, jitter=.35):
    out = []
    for j in range(n):
        a = math.radians(lo + (hi - lo) * (j + rnd.uniform(-jitter, jitter)) / n); tilt = rnd.uniform(-depth, depth)
        out.append(Vector((math.cos(a) * math.cos(tilt), math.sin(tilt), math.sin(a) * math.cos(tilt))))
    return out


# ---------------------------------------------------------------- presets: build(frame_index) -> Frame
def ink_splat(rnd):
    """the common hit: a wet body that is there at once, long thrown tails, fine spray; then it thins"""
    drops = [Drop(rnd, d, rnd.uniform(4.5, 9.5), rnd.uniform(.05, .11), drag=rnd.uniform(5.5, 8), gravity=rnd.uniform(1.5, 3.5), tail=rnd.uniform(.16, .3), life=rnd.uniform(.8, 1.3)) for d in ring_dirs(rnd, few(13))]
    spray = [Drop(rnd, d, rnd.uniform(6, 12), rnd.uniform(.018, .04), drag=rnd.uniform(6, 9), gravity=rnd.uniform(2, 5), tail=rnd.uniform(.05, .1), life=rnd.uniform(.45, .9)) for d in ring_dirs(rnd, few(26), jitter=.5)]
    lobes = [(Vector((rnd.uniform(-.2, .2), rnd.uniform(-.1, .1), rnd.uniform(-.18, .18))), rnd.uniform(.16, .27)) for _ in range(7)]

    def build(i):
        t = (i + 1.5) * DT; f = Frame()
        body = max(0.0, 1.0 - (i / 13.0) ** 1.6)
        f.ball((0, 0, -.25 * t * t), .3 * body)
        for o, r in lobes:
            f.ball(o * (1 + 1.5 * min(t, .25)) + Vector((0, 0, -.6 * t * t)), r * body)
        for d in drops + spray:
            d.draw(f, t)
        return f
    return build


def ink_drops(rnd):
    """not an animation: 16 single thrown blobs with tails (a random cell per particle in the game)"""
    def build(i):
        r = random.Random(900 + i); f = Frame()
        a = math.radians(90 + r.uniform(-25, 25)); d = Vector((math.cos(a), 0, math.sin(a)))
        side = Vector((d.z, 0, -d.x)); head = d * r.uniform(.25, .5); R = r.uniform(.2, .34)
        f.ball(head, R, squash=(1, 1, r.uniform(.85, 1.1)))
        f.ball(head + side * r.uniform(-.12, .12) - d * R * .5, R * r.uniform(.5, .75))
        n = 7; bend = r.uniform(-.5, .5); L = r.uniform(.9, 1.6) if i % 4 else 0.25
        prev, pr = head - d * R * .4, R * .7
        for k in range(1, n + 1):
            s = k / n; q = head - d * (R * .4 + L * s) + side * (bend * s * s); qr = R * .7 * (1 - s) ** 1.6
            f.cone(prev, q, pr, qr); prev, pr = q, qr
        for k in range(r.randint(1, 4)):
            s = r.uniform(.5, 1.25); f.ball(head - d * (R + L * s) + side * (bend * s * s + r.uniform(-.1, .1)), R * r.uniform(.1, .24))
        return f
    return build


def wood_splinter(rnd):
    """wood: straight splinters thrown in a fan (they stay straight and only thin), a spiked star at the moment of the hit, a little sap"""
    shards = []
    for d in ring_dirs(rnd, 18, lo=-20, hi=200, depth=.35):
        shards.append(dict(d=d, drop=Drop(rnd, d, rnd.uniform(3.5, 8.5), .01, drag=rnd.uniform(3.5, 6), gravity=rnd.uniform(1.5, 3), tail=0, life=rnd.uniform(.9, 1.33)), len=rnd.uniform(.28, .7), r=rnd.uniform(.022, .045), spin=rnd.uniform(-2.5, 2.5)))
    spikes = [(d, rnd.uniform(.7, 1.35), rnd.uniform(.035, .07)) for d in ring_dirs(rnd, 9, depth=.2)]
    sap = [Drop(rnd, d, rnd.uniform(3, 6), rnd.uniform(.03, .06), drag=6, gravity=4, tail=.12, life=rnd.uniform(.5, .9)) for d in ring_dirs(rnd, few(7), lo=20, hi=160)]

    def build(i):
        t = (i + .6) * DT; f = Frame()
        star = max(0.0, 1.0 - i / 5.0)                 # the star is full on the first frame and thins away in five
        for d, length, r in spikes:
            f.cone(Vector((0, 0, 0)), d * length * (1.0 + .08 * i), r * star, 0.0)
        f.ball((0, 0, 0), .16 * max(0.0, 1.0 - i / 7.0))
        for s in shards:
            age = t; life = s['drop'].life
            if age > life:
                continue
            thin = max(0.0, 1.0 - (age / life) ** 2)
            a = s['spin'] * age; d = s['d']
            dd = Vector((d.x * math.cos(a) - d.z * math.sin(a), d.y, d.x * math.sin(a) + d.z * math.cos(a)))
            f.needle(s['drop'].pos(t), dd, s['len'], s['r'] * thin)
        for d in sap:
            d.draw(f, t)
        return f
    return build


def fire_tongue(rnd):
    """fire, looping: a round burning core with separate tongues that rise, lean and lick, their tips tearing off.
    Red = heat (core and roots hot, tips cool - the tips are drawn as dark ink)."""
    tongues = []
    for j in range(7):
        a = math.radians(20 + 140 * (j + rnd.uniform(-.25, .25)) / 6)            # roots spread over the top of the core
        tongues.append(dict(a=a, y=rnd.uniform(-.1, .1), ph=rnd.uniform(0, 1), h=rnd.uniform(.75, 1.45), w=rnd.uniform(.09, .15), lean=rnd.uniform(-.35, .35), k=rnd.choice((1, 1, 2))))
    core = Vector((0, 0, -.72)); R = .34

    def build(i):
        f = Frame(); u = i / FRAMES; w = 6.283 * u
        f.ball(core + Vector((0, 0, .02 * math.sin(w))), R, 1.0, squash=(1 + .06 * math.sin(w), 1, 1 - .06 * math.sin(w)))
        f.ball(core + Vector((.2 * math.cos(w), 0, .12 * math.sin(2 * w))), R * .62, 1.0)
        for T in tongues:
            cyc = (u * T['k'] + T['ph']) % 1.0                     # each tongue grows, licks and tears off within the loop
            h = T['h'] * (.3 + .7 * math.sin(math.pi * min(1.0, cyc * 1.25)) ** .7)
            root = core + Vector((math.cos(T['a']) * R * .8, T['y'], math.sin(T['a']) * R * .8))
            out = math.cos(T['a']) * .5
            n = 9; prev = None; pr = 0
            for s in range(n + 1):
                v = s / n
                x = root.x + out * v * h * .5 + T['lean'] * v * v * h * .4 + .1 * math.sin(6.283 * (v * 1.2 - cyc * 2 + T['ph'])) * v
                r = T['w'] * (1 - v) ** .75 * (1.0 if cyc < .8 else max(0.0, 1 - (cyc - .8) * 5 * v))
                p = Vector((x, T['y'], root.z + h * v)); heat = max(0.0, 1.0 - v * 1.6)
                if prev is not None:
                    f.cone(prev, p, pr, r, heat)
                f.ball(p, r, heat); prev, pr = p, r
            if cyc > .5:                                            # the torn tip keeps rising and shrinks
                q = (cyc - .5) / .5
                f.ball((root.x + out * h * .5 + T['lean'] * h * .45 + .08 * math.sin(9 * q), T['y'], root.z + h * (1.0 + .45 * q)), T['w'] * .75 * (1 - q), 0.0)
        return f
    return build


def fire_burst(rnd):
    """fire at the hit: not a ball - tongues that leap UP from the point, lean, lick and leave upward, over a small hot heart;
    embers thrown wide. (3rd pass: the earlier burst read as one orange lump.)"""
    tongues = []
    for j in range(9):
        tongues.append(dict(x=-.62 + 1.24 * (j + rnd.uniform(-.3, .3)) / 8, y=rnd.uniform(-.15, .15), H=rnd.uniform(.9, 1.9), W=rnd.uniform(.09, .17), lean=rnd.uniform(-.45, .45),
                            ph=rnd.uniform(0, 6.28), life=rnd.uniform(.75, 1.2), rise=rnd.uniform(.7, 1.5), late=rnd.uniform(0, .12)))
    embers = [Drop(rnd, d, rnd.uniform(5, 10), rnd.uniform(.02, .04), drag=6, gravity=-1.0, tail=.06, life=rnd.uniform(.5, 1.0)) for d in ring_dirs(rnd, few(16), jitter=.5)]

    def build(i):
        t = (i + .8) * DT; f = Frame()
        body = max(0.0, 1.0 - (i / 6.0) ** 1.5)
        f.ball((0, 0, -.55 + .4 * t), .26 * body, 1.0, squash=(1.5, 1, .8))
        for T in tongues:
            age = t - T['late']
            if age <= 0 or age > T['life']:
                continue
            u = age / T['life']
            h = T['H'] * min(1.0, age / .16)                       # full height almost at once
            root_z = -.6 + T['rise'] * age * (.3 + u)              # then the whole tongue lets go and climbs
            thin = max(0.0, 1.0 - u ** 1.6)
            n = 9; prev = None; pr = 0
            for sgm in range(n + 1):
                v = sgm / n
                x = T['x'] * (1 + .5 * u) + T['lean'] * v * v * h * .45 + .12 * math.sin(6.283 * (v * 1.2 - age * 2.2) + T['ph']) * v
                r = T['W'] * thin * (1 - v) ** .7 * (1.0 if v > .12 or u < .35 else max(0.0, 1 - (u - .35) * 3))     # the root pinches off first
                pt = Vector((x, T['y'], root_z + h * v)); heat = max(0.0, (1.0 - v * 1.5) * (1.0 - u * 1.5))
                if prev is not None:
                    f.cone(prev, pt, pr, r, heat)
                f.ball(pt, r, heat); prev, pr = pt, r
        for d in embers:
            d.heat = max(0.0, 1.0 - t * .9); d.draw(f, t)
        return f
    return build


def earth_dust(rnd):
    """earth: heavy lobes that roll out low, swell, sag and settle; faceted chunks lobbed over them"""
    lobes = []
    for j in range(16):
        side = -1 if j % 2 else 1
        lobes.append(dict(side=side, v=rnd.uniform(1.2, 4.2), y=rnd.uniform(-.25, .25), r=rnd.uniform(.16, .34), up=rnd.uniform(.1, .75), birth=rnd.uniform(0, .12), ph=rnd.uniform(0, 6.28)))
    chunks = [dict(drop=Drop(rnd, d, rnd.uniform(3.5, 6.5), .01, drag=1.2, gravity=9.0, tail=0, life=1.33, origin=(0, 0, -.8)), r=rnd.uniform(.05, .11), ax=Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1), rnd.uniform(-1, 1))).normalized(), w=rnd.uniform(4, 11)) for d in ring_dirs(rnd, 10, lo=30, hi=150, depth=.4)]
    floor = -.95

    def build(i):
        t = (i + .6) * DT; f = Frame()
        for L in lobes:
            age = t - L['birth']
            if age < 0:
                continue
            k = (1 - math.exp(-3.2 * age)) / 3.2
            x = L['side'] * L['v'] * k
            swell = min(1.0, age * 9) * (1 + .7 * min(age, .6)); settle = max(0.0, 1.0 - max(0.0, age - .55) / .8)
            r = L['r'] * swell * (.35 + .65 * settle)
            z = floor + r * .75 + L['up'] * k * 1.6 * settle + .03 * math.sin(L['ph'] + age * 5)
            f.ball((x, L['y'], z), r, squash=(1.15, 1, .6 + .4 * settle))
            f.ball((x - L['side'] * r * .7, L['y'], floor + r * .45), r * .7, squash=(1.2, 1, .7))
        f.ball((0, 0, floor + .12), .42 * max(0.0, 1 - i / 9.0), squash=(1.5, 1, .55))
        for c in chunks:
            p = c['drop'].pos(t)
            if p.z < floor:
                continue
            f.chunk(p, c['r'], Matrix.Rotation(c['w'] * t, 4, c['ax']))
        return f
    return build


def metal_streak(rnd):
    """metal: hair-thin radial streaks and a four-point glint, at full length on the first frame, gone by the seventh"""
    rays = [(d, rnd.uniform(.55, 1.5), rnd.uniform(.022, .045) * THICK, rnd.uniform(.15, .5)) for d in ring_dirs(rnd, 22, depth=.12, jitter=.45)]
    filings = [Drop(rnd, d, rnd.uniform(9, 15), rnd.uniform(.012, .022), drag=7, gravity=2.0, tail=.2, life=rnd.uniform(.3, .6)) for d in ring_dirs(rnd, few(12), jitter=.5)]
    for d in filings:
        d.snap = .5

    def build(i):
        t = (i + .6) * DT; f = Frame()
        k = max(0.0, 1.0 - i / 6.0)
        for d, length, r, start in rays:
            # each streak slides outward and thins: its inner end leaves the centre first
            a = start * (1 - k) * 1.6 + .05; b = a + length * (.45 + .55 * k)
            f.needle(d * (a + b) * .5, d, (b - a), r * k)
        for ang, length in ((0, 1.25), (90, .9), (180, 1.25), (270, .9)):
            d = Vector((math.cos(math.radians(ang + 12)), 0, math.sin(math.radians(ang + 12))))
            f.cone(Vector((0, 0, 0)), d * length * max(0.0, 1 - i / 4.0), .1 * max(0.0, 1 - i / 4.0), 0.0)
        for d in filings:
            d.draw(f, t)
        return f
    return build


def water_ring(rnd):
    """water: a crown - the ring is thrown up and out, hangs, and falls back; a bead rises from the middle. Seen from a little above."""
    n = 26; crown = []
    for j in range(n):
        a = 6.283 * (j + rnd.uniform(-.3, .3)) / n
        crown.append(dict(a=a, v=rnd.uniform(2.6, 4.6), out=rnd.uniform(1.1, 1.9), r=rnd.uniform(.05, .1), tip=rnd.random() < .45))
    floor = -.7; tiltc, tilts = math.cos(math.radians(32)), math.sin(math.radians(32))

    def proj(x, y, z):                       # the ring lies flat; the view looks 22 degrees down on it
        return Vector((x, y * tiltc, z * tiltc + y * tilts + 0.0))

    def build(i):
        t = (i + .6) * DT; f = Frame(); g = 9.0
        k = (1 - math.exp(-3.0 * t)) / 3.0
        prev = None
        pts = []
        for c in crown:
            R = .22 + c['out'] * k; z = max(0.0, c['v'] * t * .55 - .5 * g * t * t)
            pts.append((c, proj(math.cos(c['a']) * R, math.sin(c['a']) * R, floor + z), z))
        wall = max(0.0, 1.0 - (i / 10.0) ** 2)
        for j, (c, p, z) in enumerate(pts):
            q = pts[(j + 1) % n][1]
            f.cone(p, q, c['r'] * wall * .55, pts[(j + 1) % n][0]['r'] * wall * .55)          # the sheet between the crown points
            base = proj(math.cos(c['a']) * (.22 + c['out'] * k), math.sin(c['a']) * (.22 + c['out'] * k), floor)
            f.cone(base, p, c['r'] * .8 * wall, c['r'] * .7 * wall)
            if c['tip']:                                                                  # crown points throw a bead higher
                zt = max(0.0, c['v'] * t - .5 * g * t * t)
                if zt > 0:
                    R = .22 + c['out'] * k * 1.25
                    tip = proj(math.cos(c['a']) * R, math.sin(c['a']) * R, floor + zt)
                    f.ball(tip, c['r'] * 1.25, squash=(1, 1, 1.25))
                    if t < .35:
                        f.cone(p, tip, c['r'] * wall, c['r'] * .5)
        # the middle: a column that rises late and pinches off a bead
        tc = t - .25
        if tc > 0:
            h = max(0.0, 3.6 * tc - .5 * g * tc * tc)
            if h > 0:
                f.cone(proj(0, 0, floor), proj(0, 0, floor + h * .8), .11 * max(0.0, 1 - tc), .04)
                f.ball(proj(0, 0, floor + h), .1)
        # the flat ripple left on the surface
        Rr = .3 + 2.6 * k
        m = 30
        thick = .035 * max(0.0, 1 - i / 14.0)
        for j in range(m):
            a0, a1 = 6.283 * j / m, 6.283 * (j + 1) / m
            f.cone(proj(math.cos(a0) * Rr, math.sin(a0) * Rr, floor), proj(math.cos(a1) * Rr, math.sin(a1) * Rr, floor), thick, thick)
        return f
    return build


def water_orb(rnd):
    """water in flight, looping: a heavy orb that wobbles (lobes travelling round it), with a drip forming and falling"""
    lobes = [dict(ph=rnd.uniform(0, 6.28), k=rnd.choice((1, 2)), r=rnd.uniform(.3, .42), d=rnd.uniform(.3, .46), tilt=rnd.uniform(-.5, .5)) for _ in range(5)]

    def build(i):
        f = Frame(); u = i / FRAMES; w = 6.283 * u
        f.ball((0, 0, .05 * math.sin(w)), .62, squash=(1 + .07 * math.sin(w), 1, 1 - .07 * math.sin(w)))
        for L in lobes:
            a = L['ph'] + w * L['k']
            f.ball((math.cos(a) * L['d'], L['tilt'] * .3, math.sin(a) * L['d'] * .9), L['r'])
        # the drip: swells under the orb, necks, falls (one per loop)
        d = (u * 1.0) % 1.0
        z = -.62 - 1.1 * d * d
        f.ball((.08, 0, z), .13 * (1 - .35 * d), squash=(1, 1, 1.25))
        if d < .45:
            f.cone((.06, 0, -.5), (.08, 0, z), .12 * (1 - d * 1.6), .07)
        f.ball((-.5 - .5 * d, 0, .52 + .25 * d - 1.4 * d * d), .07 * (1 - d))
        return f
    return build


# ---------------------------------------------------------------- 4th pass: the range forms (circle, cone, volley, path)
def wood_stroke(rnd):
    """wood rising out of the ground as BRUSH STROKES: thick jointed bamboo strokes (ink-painting bamboo: fat segments with a
    breath between the joints), no thin blade leaves. Whole at once, then it thins from the top."""
    stalks = [dict(x=0.0, h=2.25, r=.135, lean=rnd.uniform(-.1, .1), late=0), dict(x=rnd.choice((-.42, .42)), h=1.5, r=.1, lean=rnd.uniform(-.18, .18), late=1),
              dict(x=rnd.choice((-.75, .7)), h=.95, r=.085, lean=rnd.uniform(-.2, .2), late=2)]
    floor = -1.25

    def build(i):
        f = Frame()
        for S in stalks:
            k = i - S['late']
            if k < 0:
                continue
            grow = min(1.0, (k + 1) / 2.0); thin = max(0.0, 1.0 - max(0.0, k - 6) / 8.0)
            segs = 4; z = floor; top = S['h'] * grow
            for g in range(segs):
                a, b = top * g / segs, top * (g + 1) / segs - .045
                if b - a < .04:
                    continue
                keep = 1.0 if g < segs * thin + .5 else max(0.0, 1 - (g - segs * thin))       # it goes from the top down
                r0 = S['r'] * (1 - .12 * g) * (.55 + .45 * thin) * keep; r1 = r0 * (.9 if g < segs - 1 else .82)       # a cut top, never a point (5th pass)
                xa, xb = S['x'] + S['lean'] * a * a * .3, S['x'] + S['lean'] * b * b * .3
                f.cone((xa, 0, z + a), (xb, 0, z + b), r0, r1)
                f.ball((xa, 0, z + a), r0 * 1.12, squash=(1, 1, .5))                          # the joint: a pressed start of the stroke
                if g == segs - 1:
                    f.ball((xb, 0, z + b), r1 * 1.05, squash=(1, 1, .4))                     # the cut face
        f.ball((0, 0, floor + .02), .3 * max(0.0, 1 - i / 9.0), squash=(1.9, 1, .32))        # the ink it comes up through
        return f
    return build


def fire_lick(rnd):
    """fire thrown from the hand, looping: long tongues that run along +X (root left, tip right) and curl up - no round heart.
    Laid side by side down a cone they make one continuous burning face. Red = heat (root), the tips are soot."""
    tongues = [dict(z=dz, ph=rnd.uniform(0, 6.28), L=rnd.uniform(2.0, 2.6), w=rnd.uniform(.2, .3), k=rnd.choice((1, 1, 2)), o=rnd.uniform(0, 1)) for dz in (-.3, -.05, .22, .4)]

    def build(i):
        f = Frame(); u = i / FRAMES
        for T in tongues:
            cyc = (u * T['k'] + T['o']) % 1.0
            L = T['L'] * (.55 + .45 * math.sin(math.pi * min(1.0, cyc * 1.2)) ** .6)
            n = 12; prev = None; pr = 0
            for sg in range(n + 1):
                v = sg / n
                x = -1.38 + L * v
                z = T['z'] * (1 - .3 * v) + .2 * math.sin(6.283 * (v * 1.3 - cyc * 2) + T['ph']) * v + .32 * v * v
                r = T['w'] * math.sin(math.pi * .5 * min(1.0, v / .3 + .35)) ** .7 * (1 - v) ** .75
                pt = Vector((x, 0, z)); heat = max(0.0, 1.0 - v * 1.5)
                if prev is not None:
                    f.cone(prev, pt, pr, r, heat)
                prev, pr = pt, r
            if cyc > .5:
                q = (cyc - .5) / .5
                f.ball((-1.38 + L * (1.0 + .25 * q), 0, T['z'] * .7 + .32 + .25 * q), T['w'] * .5 * (1 - q), 0.0)
        return f
    return build


def speed_line(rnd):
    """not an animation: 16 single speed strokes (head right, tail left) - one fat tapered brush line with a companion line
    or two and a few flecks torn off it. For the fixed ribbon behind a volley shot."""
    def build(i):
        r = random.Random(1700 + i); f = Frame()
        head = Vector((1.25, 0, r.uniform(-.05, .05))); L = r.uniform(2.2, 2.65); R = r.uniform(.1, .135) * THICK / 1.6
        n = 8; prev, pr = head, R
        for k in range(1, n + 1):
            v = k / n; q = head + Vector((-L * v, 0, .05 * math.sin(v * 5 + i))); qr = R * (1 - v) ** .7
            f.cone(prev, q, pr, qr); prev, pr = q, qr
        f.ball(head, R * 1.05)
        for side in (-1, 1):
            if r.random() < .8:
                off = side * r.uniform(.2, .34); l2 = L * r.uniform(.4, .75); x0 = r.uniform(.2, .9)
                f.needle((x0 - l2 * .5, 0, off), (1, 0, 0), l2, R * r.uniform(.28, .45))
        for _ in range(r.randint(2, 5)):
            f.ball((r.uniform(-1.2, .9), 0, r.uniform(-.4, .4)), R * r.uniform(.15, .3))
        return f
    return build


def earth_heave(rnd):
    """earth along a path: the ground HEAVES - fat lumpy lobes thrown straight up to twice a man's height with clods over
    them, then they sag back. A tall, jagged, heavy head (against water's low smooth crest)."""
    lobes = [dict(x=-1.0 + 2.0 * (j + rnd.uniform(-.3, .3)) / 6, H=rnd.uniform(1.0, 2.2), r=rnd.uniform(.2, .33), lean=rnd.uniform(-.3, .3), late=rnd.uniform(0, .1)) for j in range(7)]
    clods = [dict(drop=Drop(rnd, d, rnd.uniform(4, 7), .01, drag=1.0, gravity=9.0, tail=0, life=1.33, origin=(rnd.uniform(-.6, .6), 0, -1.0)), r=rnd.uniform(.07, .14),
                  ax=Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1), rnd.uniform(-1, 1))).normalized(), w=rnd.uniform(4, 10)) for d in ring_dirs(rnd, 9, lo=55, hi=125, depth=.3)]
    floor = -1.25

    def build(i):
        t = (i + .8) * DT; f = Frame()
        for Lb in lobes:
            age = t - Lb['late']
            if age <= 0:
                continue
            up = min(1.0, age / .18); sag = max(0.0, 1.0 - max(0.0, age - .45) / .8)
            h = Lb['H'] * up * (.35 + .65 * sag); n = 5
            for sg in range(n + 1):
                v = sg / n
                r = Lb['r'] * (1 - .55 * v) * (.6 + .4 * sag) * (1 + .18 * math.sin(sg * 2.1 + Lb['x'] * 7))
                f.ball((Lb['x'] + Lb['lean'] * v * h * .4, 0, floor + r * .5 + h * v), r, squash=(1.1, 1, .9))
        f.ball((0, 0, floor + .1), .5 * max(0.0, 1 - i / 12.0), squash=(2.6, 1, .4))
        for c in clods:
            pt = c['drop'].pos(t)
            if pt.z > floor:
                f.chunk(pt, c['r'], Matrix.Rotation(c['w'] * t, 4, c['ax']))
        return f
    return build


def water_crest(rnd):
    """water along a path: one long low CREST that rises, leans over and falls - a smooth dark rim line with a thrown lip
    and a few beads, over a flat wet edge. Wide and low (against earth's tall jagged head)."""
    ph = [rnd.uniform(0, 6.28) for _ in range(4)]
    beads = [dict(s=rnd.uniform(.08, .92), v=rnd.uniform(2.2, 3.8), r=rnd.uniform(.035, .065), late=rnd.uniform(.1, .3)) for _ in range(few(12))]
    floor = -.85

    def build(i):
        t = (i + .8) * DT; f = Frame()
        amp = math.sin(math.pi * min(1.0, t / 1.15)) ** .8                      # up, over, down
        n = 22; prev = None; lip_prev = None
        for k in range(n + 1):
            sx = k / n; x = -1.45 + 2.9 * sx
            arch = math.sin(math.pi * sx) ** .55
            z = floor + (.18 + .82 * amp) * arch * (.62 + .1 * math.sin(sx * 9 + ph[0] + t * 3))
            r = .085 * (.5 + .5 * arch) * (.6 + .4 * amp)
            pt = Vector((x, 0, z))
            if prev is not None:
                f.cone(prev, pt, pr, r)
            prev, pr = pt, r
            lip = Vector((x + .05, 0, z + .16 * amp * arch * (1 + .5 * math.sin(sx * 14 + ph[1] - t * 6))))      # the lip thrown ahead of the crest
            if lip_prev is not None and k % 2 == 0:
                f.cone(pt, lip, r * .7, r * .3)
            lip_prev = lip
        m = 18
        for k in range(m):                                                       # the flat wet edge on the ground
            x0, x1 = -1.5 + 3.0 * k / m, -1.5 + 3.0 * (k + 1) / m
            f.cone((x0, 0, floor - .02 + .015 * math.sin(k * 1.7 + ph[2])), (x1, 0, floor - .02 + .015 * math.sin((k + 1) * 1.7 + ph[2])), .03, .03)
        for b in beads:
            age = t - b['late']
            if age > 0:
                z = floor + .7 * amp + b['v'] * age - 4.5 * age * age
                if z > floor:
                    f.ball((-1.45 + 2.9 * b['s'], 0, z), b['r'], squash=(1, 1, 1.25))
        return f
    return build


def ink_pool(rnd):
    """seen from above: ink welling out over the ground - a pool that spreads from the middle outward in lobes and fingers.
    Its middle is thin wash and its edge is where the ink gathers (the rim is made in the After Effects pass). For the circle form."""
    lobes = [dict(a=6.283 * (j + rnd.uniform(-.3, .3)) / 11, d=rnd.uniform(.75, 1.12), r=rnd.uniform(.2, .36), late=rnd.uniform(0, .25)) for j in range(11)]
    fingers = [dict(a=rnd.uniform(0, 6.283), L=rnd.uniform(.25, .55), r=rnd.uniform(.05, .09)) for _ in range(6)]

    def build(i):
        t = (i + .8) * DT; f = Frame()
        R = .28 + .95 * (1 - math.exp(-3.6 * t))
        f.ball((0, 0, 0), R * .78, squash=(1, .3, 1))
        for L in lobes:
            g = max(0.0, min(1.0, (t - L['late']) / .35))
            d = Vector((math.cos(L['a']), 0, math.sin(L['a'])))
            f.ball(d * (R * L['d'] * (.45 + .55 * g)), L['r'] * (.5 + .8 * g) * (R / 1.2 + .3), squash=(1, .3, 1))
        for F in fingers:
            d = Vector((math.cos(F['a']), 0, math.sin(F['a'])))
            f.cone(d * R * .8, d * (R * 1.02 + F['L'] * min(1.0, t * 2.2)), F['r'] * 1.6, F['r'] * .5)
        return f
    return build


def ink_throw(rnd):
    """6th pass (D308-83): ink THROWN off the brush along +X - not a splat. A bundle of bristle lines (a wet middle, thinner
    hairs beside it, all running the way of the throw), a wet lump that leads them, specks torn off ahead of it only. Then
    the lines sag, thin and snap into beads from the tail. Drawn stretched along its velocity in the game."""
    hairs = [dict(z=z, l=rnd.uniform(.55, 1.0) * (1.0 - abs(z) * 1.6), r=rnd.uniform(.035, .06) * (1.6 - abs(z) * 3.2), ph=rnd.uniform(0, 6.28), cut=rnd.uniform(0, 1))
             for z in (-.26, -.17, -.09, -.02, .05, .12, .2, .27)]
    ahead = [Drop(rnd, Vector((1, 0, math.tan(math.radians(a)))), rnd.uniform(5, 9.5), rnd.uniform(.03, .075), drag=rnd.uniform(4.5, 7), gravity=rnd.uniform(1.5, 3.5), birth=rnd.uniform(0, .16), tail=rnd.uniform(.1, .22), life=rnd.uniform(.6, 1.1), origin=(-1.05, 0, rnd.uniform(-.15, .15)))
             for a in [rnd.uniform(-22, 26) for _ in range(few(12))]]
    spray = [Drop(rnd, Vector((1, 0, math.tan(math.radians(a)))), rnd.uniform(7, 12), rnd.uniform(.014, .03), drag=rnd.uniform(6, 9), gravity=rnd.uniform(2, 5), birth=rnd.uniform(0, .12), tail=rnd.uniform(.04, .09), life=rnd.uniform(.4, .8), origin=(-1.0, 0, rnd.uniform(-.2, .2)))
             for a in [rnd.uniform(-30, 34) for _ in range(few(18))]]

    def build(i):
        t = (i + 1) * DT; f = Frame()
        xh = -1.2 + 2.25 * (1 - math.exp(-5.5 * t))                       # the lump leads, fast then spent
        dry = max(0.0, (t - .5) / .75)                                    # 0 wet .. 1 gone
        for j, H in enumerate(hairs):
            L = 2.3 * H['l'] * min(1.0, t / .22)
            xa = max(-1.32, xh - L); n = 9; prev = None; pr = 0
            for k in range(n + 1):
                v = k / n                                                  # 0 tail .. 1 head
                x = xa + (xh - xa) * v
                z = H['z'] * (.55 + .45 * v) + .03 * math.sin(v * 7 + H['ph']) - .42 * t * t * (1 - v) ** 1.5
                r = max(0.0, H['r']) * (.25 + .75 * v ** .7) * max(0.0, 1 - dry * (1.15 - .5 * v))
                pt = Vector((x, 0, z))
                # the line breaks from the tail: a segment is dropped once the drying has passed its place on the line
                gone = dry * 1.5 > v + .35 * math.sin(k * 2.3 + H['cut'] * 9) + .25
                if prev is not None and r > .004 and not gone:
                    f.cone(prev, pt, pr, r)
                elif gone and r > .012 and (k + j) % 2 == 0:
                    f.ball(pt + Vector((0, 0, -.12 * dry)), r * .8)       # what is left of it: a bead
                prev, pr = pt, max(r, 0.0)
        lump = max(0.0, 1.0 - t / .95)
        f.ball((xh, 0, -.1 * t * t), .21 * lump, squash=(1.25, 1, .9))
        f.ball((xh - .16, 0, .06 - .1 * t * t), .15 * lump); f.ball((xh - .1, 0, -.09 - .12 * t * t), .13 * lump)
        for d in ahead + spray:
            d.draw(f, t)
        return f
    return build


def ink_wisp(rnd):
    """6th pass, looping: ink smoke that streams off a body toward -X (root right, tip left) and curls up as it thins - the
    strands behind an ink creature, the ink that climbs a buffed body. No heart, no heat: all of it is wash."""
    strands = [dict(z=dz, ph=rnd.uniform(0, 6.28), L=rnd.uniform(1.5, 2.1), w=rnd.uniform(.05, .095), k=rnd.choice((1, 1, 2)), o=rnd.uniform(0, 1)) for dz in (-.3, -.1, .1, .3)]

    def build(i):
        f = Frame(); u = i / FRAMES
        for T in strands:
            cyc = (u * T['k'] + T['o']) % 1.0
            L = T['L'] * (.6 + .4 * math.sin(math.pi * min(1.0, cyc * 1.2)) ** .6)
            n = 13; prev = None; pr = 0
            for sg in range(n + 1):
                v = sg / n
                x = .85 - L * v
                z = T['z'] * (1 - .25 * v) + .24 * math.sin(6.283 * (v * 1.4 - cyc * 2) + T['ph']) * v + .34 * v * v
                r = T['w'] * math.sin(math.pi * .5 * min(1.0, v / .2 + .45)) ** .7 * (1 - v) ** .9
                pt = Vector((x, 0, z))
                if prev is not None:
                    f.cone(prev, pt, pr, r)
                prev, pr = pt, r
            # a curl that lets go of the tip and drifts on
            q = cyc
            f.ball((.85 - L * (1.0 + .22 * q), 0, T['z'] * .75 + .34 + .3 * q + .1 * math.sin(T['ph'] + q * 6)), T['w'] * .55 * (1 - q))
        return f
    return build


def ink_gather(rnd):
    """6th pass: thrown ink that comes TOGETHER - drops spiral in from all round and a round wet body swells where they meet
    (the moment before the spell takes its shape). The last cels hold a compact body."""
    drops = [dict(d=d, R=rnd.uniform(.95, 1.45), r=rnd.uniform(.05, .12), delay=rnd.uniform(0, .3), swirl=rnd.uniform(.9, 1.7)) for d in ring_dirs(rnd, max(10, few(22)), depth=.15)]
    lobes = [(Vector((rnd.uniform(-.16, .16), 0, rnd.uniform(-.14, .14))), rnd.uniform(.12, .2)) for _ in range(5)]

    def place(D, s):
        s = min(1.0, max(0.0, s)); e = s * s * (3 - 2 * s)
        a = D['swirl'] * (1 - e); ca, sa = math.cos(a), math.sin(a)
        d = D['d']; v = Vector((d.x * ca - d.z * sa, d.y, d.x * sa + d.z * ca))
        return v * (D['R'] * (1 - e))

    def build(i):
        t = i / (FRAMES - 1); f = Frame()
        body = min(1.0, t / .72); body = body * body * (3 - 2 * body)
        f.ball((0, 0, 0), .1 + .25 * body, squash=(1 + .06 * math.sin(t * 9), 1, 1 - .06 * math.sin(t * 9)))
        for o, r in lobes:
            f.ball(o * (1.9 - .7 * body), r * (.35 + .75 * body))
        for D in drops:
            s = (t - D['delay']) / .55
            if s <= 0 or s >= 1.0:
                if s <= 0:
                    f.ball(place(D, 0), D['r'] * .8)                       # waiting where it was thrown
                continue
            p = place(D, s); r = D['r'] * (1 - .45 * s)
            f.ball(p, r)
            prev, pr = p, r * .8
            for k in range(1, 5):                                          # the tail lies back along the way it came
                q = place(D, s - .07 * k); qr = r * .8 * (1 - k / 4) ** 1.2
                f.cone(prev, q, pr, qr); prev, pr = q, qr
        return f
    return build


PRESETS = dict(ink_splat=ink_splat, ink_drops=ink_drops, wood_splinter=wood_splinter, fire_tongue=fire_tongue, fire_burst=fire_burst,
               earth_dust=earth_dust, metal_streak=metal_streak, water_ring=water_ring, water_orb=water_orb,
               wood_stroke=wood_stroke, fire_lick=fire_lick, speed_line=speed_line, earth_heave=earth_heave, water_crest=water_crest, ink_pool=ink_pool,
               ink_throw=ink_throw, ink_wisp=ink_wisp, ink_gather=ink_gather)     # 6th pass (D308-83): the ink before the spell

# ---------------------------------------------------------------- scene: flat render, data colours, transparent film
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
cam_data = bpy.data.cameras.new('Cam'); cam_data.type = 'ORTHO'; cam_data.ortho_scale = VIEW
cam = bpy.data.objects.new('Cam', cam_data); scene.collection.objects.link(cam); scene.camera = cam
cam.location = (0, -10, 0); cam.rotation_euler = (math.radians(90), 0, 0)
scene.render.engine = 'BLENDER_WORKBENCH'
sh = scene.display.shading; sh.light = 'FLAT'; sh.color_type = 'VERTEX'; sh.show_shadows = False; sh.show_cavity = False; sh.show_object_outline = False
scene.display.render_aa = '8'
scene.render.film_transparent = True
scene.render.resolution_x = scene.render.resolution_y = CELL * SS; scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = 'PNG'; scene.render.image_settings.color_mode = 'RGBA'; scene.render.image_settings.color_depth = '8'
scene.view_settings.view_transform = 'Standard'; scene.display_settings.display_device = 'sRGB'
mesh = bpy.data.meshes.new('Ink'); obj = bpy.data.objects.new('Ink', mesh); scene.collection.objects.link(obj)

for name in (PRESETS if WHICH == 'all' else WHICH.split(',')):
    out = ROOT / 'Art/SpellVFX308' / OUTDIR / name; out.mkdir(parents=True, exist_ok=True)
    build = PRESETS[name](random.Random(sum(map(ord, name)) * 7 + 308))
    for i in range(FRAMES):
        fr = build(i)
        fr.bm.to_mesh(mesh); fr.bm.free(); mesh.update()
        if mesh.color_attributes:
            mesh.color_attributes.active_color = mesh.color_attributes[0]; mesh.color_attributes.render_color_index = 0
        scene.render.filepath = str(out / f'f{i + 1:03d}.png'); bpy.ops.render.render(write_still=True)
    print('FORMS', name, out)
    if WHICH != 'all' and name == WHICH.split(',')[-1] and '--save' in argv:
        bpy.ops.wm.save_as_mainfile(filepath=str(out / (name + '_lastframe.blend')))
