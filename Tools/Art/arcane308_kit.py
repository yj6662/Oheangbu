"""#308 Hanyang phase 7 - the ore-craft ("arcane") kit: Joseon instruments and fittings that run on ore. Offline, deterministic, no Blender.
SPEC-WORLD-HANYANG-308 §17 (D308-64 / D308-79). Vocabulary: Tools/Unity/Stage308_arcane/REFERENCES.md.

  python Tools/Art/arcane308_kit.py sheet     # contact sheets -> Art/World/PackLite308/_sheets/arcane/*.png (CPU, matplotlib)
  python Tools/Art/arcane308_kit.py stats     # triangles per object and LOD

Every object: origin on the ground at its centre, +y up, metres. Materials are keys (KEYS): brass ware, earthenware, lacquer, paper, stone,
wood, raw ore - and `core`, the ore egg of a lamp, the ONLY emissive one (`coreoff` = the same egg unlit). No gear, no gauge dial, no copper pipe.
"""
import math, sys
from pathlib import Path
import numpy as np

KEYS = ['yugi', 'onggi', 'lacq', 'lacqred', 'paper', 'stone', 'wood', 'iron', 'ore', 'powder', 'core', 'coreoff']
COL = dict(yugi=(.58, .45, .2), onggi=(.24, .13, .08), lacq=(.06, .04, .04), lacqred=(.42, .09, .06), paper=(.62, .58, .48), stone=(.55, .54, .5), wood=(.2, .13, .09),
           iron=(.12, .12, .13), ore=(.55, .4, .16), powder=(.62, .5, .25), core=(1., .55, .12), coreoff=(.4, .33, .2))


class MB:
    def __init__(self): self.sub = {}; self.st = [(np.eye(3), np.zeros(3))]
    def buf(self, mat): return self.sub.setdefault(mat, dict(v=[], n=[], t=[]))
    def tris(self): return sum(len(b['t']) // 3 for b in self.sub.values())

    class _At:
        def __init__(s, mb, R, t): s.mb = mb; s.e = (R, t)
        def __enter__(s): R0, t0 = s.mb.st[-1]; s.mb.st.append((R0 @ s.e[0], R0 @ s.e[1] + t0))
        def __exit__(s, *a): s.mb.st.pop()

    def at(self, x=0., y=0., z=0., yaw=0., pitch=0., roll=0., sc=1.):
        cy, sy = math.cos(math.radians(yaw)), math.sin(math.radians(yaw)); cp, sp = math.cos(math.radians(pitch)), math.sin(math.radians(pitch)); cr, sr = math.cos(math.radians(roll)), math.sin(math.radians(roll))
        Ry = np.array([[cy, 0, sy], [0, 1, 0], [-sy, 0, cy]]); Rx = np.array([[1, 0, 0], [0, cp, -sp], [0, sp, cp]]); Rz = np.array([[cr, -sr, 0], [sr, cr, 0], [0, 0, 1]])
        return MB._At(self, Ry @ Rx @ Rz * sc, np.array([x, y, z], float))

    def _emit(self, mat, pts, nrms):
        R, t = self.st[-1]; b = self.buf(mat); i0 = len(b['v']); s = np.linalg.norm(R[:, 0])
        for p, n in zip(pts, nrms):
            b['v'].append(R @ np.asarray(p, float) + t); nn = R @ np.asarray(n, float) / (s * s); L = np.linalg.norm(nn); b['n'].append(nn / L if L > 1e-9 else np.array([0., 1., 0.]))
        return b, i0

    def poly(self, pts, mat, nrm=None):
        pts = [np.asarray(p, float) for p in pts]
        if nrm is None:
            nrm = np.cross(pts[1] - pts[0], pts[2] - pts[0]); L = np.linalg.norm(nrm)
            if L < 1e-10: return
            nrm = nrm / L
        b, i0 = self._emit(mat, pts, [nrm] * len(pts))
        for k in range(1, len(pts) - 1): b['t'] += [i0, i0 + k, i0 + k + 1]

    def box(self, c, size, mat, yaw=0., bottom=False, top=True):
        with self.at(c[0], c[1], c[2], yaw=yaw):
            x, y, z = size[0] / 2, size[1] / 2, size[2] / 2
            P = lambda a, b_, c_: (a * x, b_ * y, c_ * z)
            self.poly([P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1)], mat); self.poly([P(1, -1, -1), P(-1, -1, -1), P(-1, 1, -1), P(1, 1, -1)], mat)
            self.poly([P(1, -1, 1), P(1, -1, -1), P(1, 1, -1), P(1, 1, 1)], mat); self.poly([P(-1, -1, -1), P(-1, -1, 1), P(-1, 1, 1), P(-1, 1, -1)], mat)
            if top: self.poly([P(-1, 1, 1), P(1, 1, 1), P(1, 1, -1), P(-1, 1, -1)], mat)
            if bottom: self.poly([P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1)], mat)

    def frus(self, a, b, r0, r1, n, mat, cap0=False, cap1=False, phase=0.5):
        a = np.asarray(a, float); b = np.asarray(b, float); d = b - a; L = np.linalg.norm(d)
        if L < 1e-9: return
        d = d / L; h = np.array([1., 0, 0]) if abs(d[0]) < 0.9 else np.array([0, 0, 1.]); u = np.cross(d, h); u /= np.linalg.norm(u); w = np.cross(d, u)
        ang = [(k + phase) * 2 * math.pi / n for k in range(n)]; rad = [u * math.cos(t) + w * math.sin(t) for t in ang]
        R, t0 = self.st[-1]; s = np.linalg.norm(R[:, 0])
        def nr(k): v = rad[k] * L + d * (r0 - r1); return v / np.linalg.norm(v)
        for k in range(n):
            k1 = (k + 1) % n; pts = []; nn = []
            if r0 > 1e-6 and r1 > 1e-6: pts = [a + rad[k] * r0, a + rad[k1] * r0, b + rad[k1] * r1, b + rad[k] * r1]; nn = [nr(k), nr(k1), nr(k1), nr(k)]
            elif r0 > 1e-6: pts = [a + rad[k] * r0, a + rad[k1] * r0, b]; m = nr(k) + nr(k1); nn = [nr(k), nr(k1), m / np.linalg.norm(m)]
            elif r1 > 1e-6: pts = [a, b + rad[k1] * r1, b + rad[k] * r1]; m = nr(k) + nr(k1); nn = [m / np.linalg.norm(m), nr(k1), nr(k)]
            else: continue
            if n <= 4: fn = np.cross(pts[1] - pts[0], pts[2] - pts[0]); fn /= max(np.linalg.norm(fn), 1e-12); fn = -fn if fn @ (nn[0] + nn[1]) < 0 else fn; nn = [fn] * len(pts)      # few sides = faceted
            bf, i0 = self._emit(mat, pts, nn)
            for j in range(1, len(pts) - 1): bf['t'] += [i0, i0 + j, i0 + j + 1]
        if cap0 and r0 > 1e-6: self.poly([a + rad[k] * r0 for k in reversed(range(n))], mat, -d)
        if cap1 and r1 > 1e-6: self.poly([b + rad[k] * r1 for k in range(n)], mat, d)

    def lathe(self, x, z, prof, n, mat, cap=True, phase=0.5):
        for (y0, r0), (y1, r1) in zip(prof[:-1], prof[1:]): self.frus((x, y0, z), (x, y1, z), r0, r1, n, mat, phase=phase)
        if cap and prof[-1][1] > 1e-6: self.frus((x, prof[-1][0], z), (x, prof[-1][0] + 1e-4, z), prof[-1][1], 0, n, mat, phase=phase)

    def tube(self, pts, r, n, mat):
        for a, b in zip(pts[:-1], pts[1:]): self.frus(a, b, r, r, n, mat)


# ---------------------------------------------------------------- small parts
def strokes(mb, w, mat, seed=0, th=0.012):
    """abstracted brush strokes (not letters): bars, a corner, a ring of short bars - raised on a plate lying in XZ, +y up."""
    rng = np.random.default_rng(308 + seed); g = w / 5
    pat = [((-1.2, 1.2), (2.2, .3), 0), ((-1.4, -.2), (.3, 2.0), 0), ((.9, .6), (.3, 1.3), 0), ((.2, -1.2), (1.9, .3), 0), ((1.1, -.4), (1.2, .28), 45), ((-.3, .2), (1.0, .26), -40)]
    for k in rng.permutation(len(pat))[:5]:
        (cx, cz), (sx, sz), yw = pat[k]; mb.box((cx * g, th / 2, cz * g), (sx * g, th, sz * g), mat, yaw=yw)


def plate(mb, w=0.5, seed=0, lod=0, th=0.03):
    """술식 유기 패: a brass plate with raised stroke fragments and four boss nails; lies flat (+y)."""
    mb.box((0, th / 2, 0), (w, th, w), 'yugi'); mb.box((0, th + .004, 0), (w * .84, .008, w * .84), 'lacq')
    if lod == 0:
        with mb.at(0, th + .008, 0): strokes(mb, w * .8, 'yugi', seed)
        for sx in (-1, 1):
            for sz in (-1, 1): mb.frus((sx * w * .44, th, sz * w * .44), (sx * w * .44, th + .025, sz * w * .44), .03, .012, 6, 'yugi', cap1=True)


def egg(mb, y, r, lit, lod):
    n = 6 if lod == 0 else 4; mat = 'core' if lit else 'coreoff'
    mb.lathe(0, 0, [(y, 0.0), (y + r * .35, r * .72), (y + r * 1.0, r), (y + r * 1.7, r * .62), (y + r * 2.1, 0.0)], n, mat, cap=False)


def lantern(mb, s, lit, lod, y=0.0):
    """화사: a lacquer lattice box; inside, a brass lotus-bud calyx holds the ore egg (the only thing that glows)."""
    h = s * 1.12; t = s * .085
    mb.box((0, y + t / 2, 0), (s, t, s), 'lacq', bottom=True); mb.box((0, y + h - t / 2, 0), (s, t, s), 'lacq', bottom=True)
    for sx in (-1, 1):
        for sz in (-1, 1): mb.box((sx * (s - t) / 2, y + h / 2, sz * (s - t) / 2), (t, h - 2 * t, t), 'lacq', top=False)
    if lod == 0:
        for k in range(4):
            with mb.at(0, y, 0, yaw=90 * k):
                z_ = s / 2 - t * .3; e = t * .22
                for o in (-.17, .17): mb.poly([(o * s - e, t, z_), (o * s + e, t, z_), (o * s + e, h - t, z_), (o * s - e, h - t, z_)], 'lacq'); mb.poly([(o * s + e, t, z_), (o * s - e, t, z_), (o * s - e, h - t, z_), (o * s + e, h - t, z_)], 'lacq')
                mb.poly([(-s / 2 + t, h * .66, z_), (s / 2 - t, h * .66, z_), (s / 2 - t, h * .7, z_), (-s / 2 + t, h * .7, z_)], 'lacq'); mb.poly([(-s / 2 + t, h * .72, z_ - .004), (s / 2 - t, h * .72, z_ - .004), (s / 2 - t, h - t, z_ - .004), (-s / 2 + t, h - t, z_ - .004)], 'paper')
        mb.frus((0, y + t, 0), (0, y + t + h * .1, 0), s * .07, s * .09, 6, 'yugi'); mb.frus((0, y + t + h * .1, 0), (0, y + t + h * .3, 0), s * .09, s * .27, 6, 'yugi')
    egg(mb, y + t + h * .17, s * .31, lit, lod)
    return y + h


def roof4(mb, y, w, mat, lod, rise=.42):
    """옥개: a square hipped cap with a lifted eave line + a brass jewel finial."""
    mb.box((0, y + w * .03, 0), (w, w * .06, w), mat, bottom=True); r = w / math.sqrt(2) * .96
    mb.frus((0, y + w * .06, 0), (0, y + w * rise * .55, 0), r, r * .5, 4, mat, phase=0.5); mb.frus((0, y + w * rise * .55, 0), (0, y + w * rise, 0), r * .5, r * .12, 4, mat, phase=0.5, cap1=True)
    if lod == 0:
        for sx in (-1, 1):
            for sz in (-1, 1): mb.frus((sx * w * .47, y + w * .06, sz * w * .47), (sx * w * .5, y + w * .13, sz * w * .5), w * .06, 0, 3, mat)      # lifted corners
    yy = y + w * rise; mb.lathe(0, 0, [(yy, .035), (yy + .03, .05), (yy + .09, .075), (yy + .15, .05), (yy + .2, 0.0)], 6 if lod == 0 else 4, 'yugi', cap=False)
    return yy + .2


def stone_lamp(mb, H=2.6, lit=True, lod=0, formal=False, stub=True):
    """마석 등 in 장명등 form: plinth - lotus foot - octagonal shaft with inscribed brass bands - lotus head - brass tray - lattice box with the egg - cap."""
    n = 8 if lod == 0 else 4; s = .54 if formal else .46; pw = .74 if formal else .6
    mb.box((0, .11, 0), (pw, .22, pw), 'stone')
    if formal: mb.box((0, .27, 0), (pw * .8, .1, pw * .8), 'stone'); y = .32
    else: y = .22
    if stub and lod == 0:      # the conduit enters the plinth along local z (brass collars)
        for sz in (-1, 1): mb.frus((0, .09, sz * (pw / 2 - .01)), (0, .09, sz * (pw / 2 + .07)), .125, .112, 6, 'yugi')
    top = H - (s * 1.12 + s * 1.3 * .42 + .2) - .2      # where the lotus head ends
    mb.frus((0, y, 0), (0, y + .14, 0), pw * .42, .15, n, 'stone'); y += .14; sh = top - .18 - y
    mb.frus((0, y, 0), (0, y + sh, 0), .125, .105, n, 'stone')
    for f in ((.3, .72) if formal else (.5,)):
        mb.frus((0, y + sh * f - .035, 0), (0, y + sh * f + .035, 0), .14, .135, n, 'yugi')
        if lod == 0:
            for k in range(4): a = (k + .5) * math.pi / 2; mb.box((math.cos(a) * .142, y + sh * f, math.sin(a) * .142), (.012, .05 if k % 2 else .028, .06), 'lacq', yaw=-math.degrees(a), top=False)      # stroke notches round the band
    y += sh; mb.frus((0, y, 0), (0, y + .18, 0), .11, s * .66, n, 'stone'); y += .18
    mb.frus((0, y, 0), (0, y + .035, 0), s * .74, s * .74, n, 'yugi', cap1=True); y += .035
    y = lantern(mb, s, lit, lod, y); return roof4(mb, y, s * 1.42, 'stone', lod)


def hook_lamp(mb, lit=True, lod=0):
    """등롱 걸이: a timber post on a stone foot, a bracketed arm, a hanging four-sided lamp."""
    mb.box((0, .12, 0), (.3, .24, .3), 'stone'); mb.box((0, 1.42, 0), (.11, 2.36, .11), 'wood'); mb.frus((0, .5, 0), (0, .58, 0), .085, .085, 4, 'yugi')
    mb.box((0, 2.5, .3), (.09, .09, .86), 'wood'); mb.frus((0, 2.05, .06), (0, 2.46, .5), .03, .03, 4, 'lacqred'); mb.box((0, 2.63, 0), (.2, .05, .2), 'lacq')
    mb.frus((0, 2.46, .62), (0, 2.26, .62), .012, .012, 4, 'iron'); mb.frus((0, 2.26, .62), (0, 2.2, .62), .04, .12, 4 if lod else 6, 'yugi')
    with mb.at(0, 0, .62): lantern(mb, .31, lit, lod, 1.85)
    return 2.66


def conduit(mb, lod=0):
    """옹기 도관 한 마디 (unit length along +z, authored for ~2.4 m): two fired lengths with bell sockets, brass bands, a stone saddle; half sunk."""
    n = 5 if lod == 0 else 4; r = .085; y = .055
    if lod: mb.frus((0, y, 0), (0, y, 1), r, r, n, 'onggi'); return
    for z0 in (0., .5):
        mb.frus((0, y, z0), (0, y, z0 + .5), r, r * .96, n, 'onggi'); mb.frus((0, y, z0 + .465), (0, y, z0 + .495), r * 1.3, r * 1.3, n, 'yugi')
    mb.box((0, .035, .25), (.3, .07, .06), 'stone')


JAR = [(0.0, .16), (.12, .27), (.35, .33), (.6, .3), (.78, .2), (.86, .17)]


def jar(mb, x, z, y, s=1.0, n=8, mat='onggi', lid='yugi', hoops=()):
    mb.lathe(x, z, [(y + h * s, r * s) for h, r in JAR], n, mat, cap=False)
    if lid: mb.frus((x, y + .86 * s, z), (x, y + .9 * s, z), .2 * s, .18 * s, n, lid, cap1=True); mb.frus((x, y + .9 * s, z), (x, y + .97 * s, z), .035 * s, .05 * s, 6, lid, cap1=True)
    for f in hoops:
        r = np.interp(f, [h for h, _ in JAR], [r for _, r in JAR]) * s; mb.frus((x, y + (f - .02) * s, z), (x, y + (f + .02) * s, z), r + .012, r + .012, n, 'yugi')
    return y + .97 * s


def junction(mb, lod=0):
    """이음 독: where conduits meet - an earthenware jar set in a stone collar, brass lid and hoop, sockets along z."""
    n = 8 if lod == 0 else 5; mb.frus((0, 0, 0), (0, .14, 0), .5, .46, n, 'stone', cap1=True); jar(mb, 0, 0, .1, 1.0, n, hoops=(.48,) if lod == 0 else ())
    if lod == 0:
        for sz in (-1, 1): mb.frus((0, .06, sz * .3), (0, .06, sz * .62), .1, .1, 6, 'onggi'); mb.frus((0, .06, sz * .56), (0, .06, sz * .6), .12, .12, 6, 'yugi')
        with mb.at(0, .5, .345, pitch=90): plate(mb, .2, 3, 1, th=.012)
    return 1.07


def reservoir(mb, lod=0):
    """저장 독: a lacquered ore-powder vat on a timber stand with brass hoops and a notched measuring strip; two small jars beside."""
    n = 8 if lod == 0 else 5; mb.box((0, .12, 0), (.95, .1, .95), 'wood', bottom=True)
    for sx in (-1, 1):
        for sz in (-1, 1): mb.box((sx * .4, .035, sz * .4), (.12, .07, .12), 'wood')
    top = jar(mb, 0, 0, .17, 1.45, n, mat='lacq', hoops=(.2, .5, .74) if lod == 0 else (.5,))
    if lod == 0:
        mb.box((0, .17 + .5 * 1.45, .485), (.05, .9, .012), 'yugi')
        for k in range(7): mb.box((0, .42 + k * .13, .493), (.1 if k % 3 == 0 else .07, .012, .008), 'lacqred')
        mb.frus((0, top, 0), (0, top + .16, 0), .05, .16, 6, 'yugi')      # the funnel
    jar(mb, .78, .15, 0, .62, 6, hoops=()); jar(mb, .7, -.5, 0, .5, 6, lid=None)
    return top


def chest(mb, lod=0, stack=True):
    """봉인 마석 궤: lacquer chests with red edges, brass corner mounts and a round seal plate."""
    def one(x, y, z, yaw):
        with mb.at(x, y, z, yaw=yaw):
            mb.box((0, .22, 0), (.9, .44, .5), 'lacq', bottom=True); mb.box((0, .46, 0), (.94, .05, .54), 'lacqred')
            if lod == 0:
                for sx in (-1, 1):
                    mb.box((sx * .43, .22, .252), (.06, .44, .012), 'yugi'); mb.box((sx * .43, .07, 0), (.05, .14, .52), 'yugi')
                mb.frus((0, .27, .25), (0, .27, .265), .09, .09, 8, 'yugi', cap1=True); mb.box((0, .2, .268), (.03, .1, .012), 'yugi')
    one(-.5, 0, 0, 4); one(.5, 0, .05, -6)
    if stack: one(0, .485, 0, 2)
    return .98


def scale_set(mb, lod=0):
    """저울과 되 · 말: a beam balance on a stand with two brass pans, and nested square measures."""
    mb.box((0, .04, 0), (.5, .08, .26), 'wood'); mb.box((0, .62, 0), (.06, 1.1, .06), 'wood'); mb.box((0, 1.14, 0), (1.1, .035, .035), 'lacq'); mb.frus((0, 1.15, 0), (0, 1.24, 0), .03, .012, 4, 'yugi')
    for sx, dy in ((-1, -.06), (1, .04)):
        x = sx * .5
        for a in (0, 2.1, 4.2): mb.frus((x, 1.13, 0), (x + math.cos(a) * .14, .72 + dy, math.sin(a) * .14), .006, .006, 3, 'iron')
        mb.frus((x, .68 + dy, 0), (x, .72 + dy, 0), .1, .17, 8 if lod == 0 else 5, 'yugi', cap0=True)
    for k, (s, x, z) in enumerate(((.36, .75, .3), (.26, .82, -.12), (.18, .5, -.36))):
        mb.box((x, s * .35, z), (s, s * .7, s), 'wood', yaw=12 * k); mb.box((x, s * .7 + .004, z), (s * .82, .008, s * .82), 'powder' if k == 0 else 'lacq', yaw=12 * k)
    return 1.24


def cannon(mb, lod=0):
    """술식 총통: a banded bronze barrel on a timber carriage (동차). Local +z = the muzzle. No glow; the bands carry stroke notches."""
    n = 8 if lod == 0 else 5
    mb.box((0, .34, 0), (.5, .12, 1.25), 'wood', bottom=True)
    for sx in (-1, 1): mb.box((sx * .3, .4, 0), (.08, .46, 1.05), 'wood', bottom=True)
    for sz in (-.36, .36):
        for sx in (-1, 1): mb.frus((sx * .36, .22, sz), (sx * .43, .22, sz), .22, .22, 8 if lod == 0 else 6, 'wood', cap0=True, cap1=True)
    with mb.at(0, .74, -.1, pitch=-9):
        mb.lathe(0, 0, [(-.55, .0), (-.5, .1), (-.2, .12), (.5, .1), (.82, .09), (.86, .115), (.9, .115)], n, 'yugi', cap=False, phase=.5) if False else None
        mb.frus((0, 0, -.6), (0, 0, -.52), .0, .12, n, 'yugi'); mb.frus((0, 0, -.52), (0, 0, .5), .12, .095, n, 'yugi'); mb.frus((0, 0, .5), (0, 0, .92), .095, .09, n, 'yugi'); mb.frus((0, 0, .92), (0, 0, .93), .09, .05, n, 'iron')
        for z in ((-.35, -.05, .25, .55, .88) if lod == 0 else (.88,)): mb.frus((0, 0, z - .025), (0, 0, z + .025), .135 - .03 * (z + .35) / 1.2, .135 - .03 * (z + .35) / 1.2, n, 'yugi')
        if lod == 0:
            with mb.at(0, .118, -.2): plate(mb, .14, 5, 1, th=.01)
    return 1.0


def hwacha(mb, lod=0):
    """신기전 틀(화차): a two-wheeled cart carrying a launch box - a lacquered frame of tube rows. Local +z = forward. Unlit."""
    for sx in (-1, 1):
        mb.frus((sx * .62, .5, 0), (sx * .7, .5, 0), .5, .5, 10 if lod == 0 else 6, 'wood', cap0=True, cap1=True)
        if lod == 0: mb.frus((sx * .6, .5, 0), (sx * .74, .5, 0), .09, .09, 6, 'iron')
        mb.box((sx * .42, .56, -.5), (.07, .07, 2.0), 'wood')
    mb.box((0, .56, 0), (1.2, .09, .7), 'wood', bottom=True)
    with mb.at(0, .62, .1, pitch=-14):
        mb.box((0, .32, 0), (.96, .62, .8), 'lacq', bottom=True); mb.box((0, .32, .405), (.9, .56, .01), 'wood')
        if lod == 0:
            for i in range(6):
                for j in range(4): mb.box((-.375 + i * .15, .11 + j * .14, .412), (.085, .085, .012), 'iron')
            for sx in (-1, 1): mb.box((sx * .485, .32, 0), (.012, .62, .1), 'yugi'); mb.box((sx * .485, .32, .3), (.012, .62, .06), 'yugi')
    return 1.45


def beacon(mb, lit=True, lod=0):
    """봉수 화두 / 정료대: a tall stone post carrying a brass fire bowl; the bowl holds one large ore egg (봉수 = an allowed sustained light)."""
    n = 8 if lod == 0 else 4; mb.box((0, .15, 0), (.8, .3, .8), 'stone'); mb.frus((0, .3, 0), (0, .46, 0), .34, .2, n, 'stone'); mb.frus((0, .46, 0), (0, 2.5, 0), .17, .14, n, 'stone')
    mb.frus((0, 1.45, 0), (0, 1.53, 0), .185, .185, n, 'yugi'); mb.frus((0, 2.5, 0), (0, 2.66, 0), .13, .34, n, 'stone')
    mb.lathe(0, 0, [(2.66, .2), (2.72, .4), (2.9, .5), (2.94, .47)], n, 'yugi', cap=False); mb.frus((0, 2.9, 0), (0, 2.78, 0), .46, .2, n, 'iron', cap1=True)
    if lod == 0:
        for k in range(4): a = k * math.pi / 2 + .4; mb.frus((math.cos(a) * .44, 2.9, math.sin(a) * .44), (math.cos(a) * .3, 3.22, math.sin(a) * .3), .018, .018, 3, 'yugi')
    egg(mb, 2.8, .2, lit, lod); return 3.25


def gatebox(mb, lod=0):
    """개폐 함: the gate's ore cabinet at the jamb - a lacquer case on a stone foot, a brass face with strokes, a ring pull, a conduit dropping into the ground."""
    mb.box((0, .14, 0), (.74, .28, .52), 'stone'); mb.box((0, .83, 0), (.6, 1.1, .4), 'lacq'); mb.box((0, 1.4, 0), (.68, .06, .48), 'lacqred'); mb.box((0, 1.47, 0), (.5, .08, .3), 'lacq')
    with mb.at(0, .92, .2, pitch=90): plate(mb, .42, 7, lod, th=.014)
    if lod == 0:
        mb.frus((0, .5, .2), (0, .5, .225), .07, .07, 8, 'yugi', cap1=True); mb.frus((0, .44, .23), (0, .5, .23), .05, .05, 6, 'iron')
        for sx in (-1, 1): mb.box((sx * .29, .83, .201), (.03, 1.1, .008), 'yugi')
    mb.frus((.37, .06, 0), (.37, 1.0, 0), .075, .075, 6 if lod == 0 else 4, 'onggi'); mb.frus((.37, .52, 0), (.37, .57, 0), .095, .095, 6, 'yugi'); mb.frus((.37, 1.0, 0), (.3, 1.1, 0), .075, .06, 6 if lod == 0 else 4, 'onggi')
    return 1.51


def brazier(mb, lod=0):
    """마석 화로: a brass brazier on three legs with spent grey ore, an earthenware flue beside."""
    n = 10 if lod == 0 else 6
    for a in (0.3, 2.4, 4.5): mb.frus((math.cos(a) * .2, 0, math.sin(a) * .2), (math.cos(a) * .16, .3, math.sin(a) * .16), .03, .03, 4, 'yugi')
    mb.frus((0, .3, 0), (0, .52, 0), .2, .3, n, 'yugi'); mb.frus((0, .5, 0), (0, .53, 0), .26, .22, n, 'iron', cap1=True); mb.frus((0, .5, 0), (0, .54, 0), .31, .31, n, 'yugi')
    mb.tube([(.3, .42, 0), (.55, .5, 0), (.55, 1.7, 0)], .06, 6 if lod == 0 else 4, 'onggi'); mb.frus((.55, 1.7, 0), (.55, 1.78, 0), .09, .05, 6, 'yugi', cap1=True); mb.frus((.55, .95, 0), (.55, 1.0, 0), .078, .078, 6, 'yugi')
    return 1.78


def heap(mb, x, z, y, mat, n, r, seed):
    rng = np.random.default_rng(seed)
    for _ in range(n):
        a = rng.uniform(0, 6.283); d = r * math.sqrt(rng.random()); s = rng.uniform(.05, .11)
        with mb.at(x + math.cos(a) * d, y + s * .5 + .08 * (1 - d / r), z + math.sin(a) * d, yaw=rng.uniform(0, 360), pitch=rng.uniform(-30, 30)): mb.frus((0, -s, 0), (0, s, 0), s, s * .3, 4, mat, cap0=True)


def orestall(mb, lod=0):
    """마석전: a low lacquer counter with five trays of sorted ore (the five colours), a hand quern with its powder, a balance."""
    mb.box((0, .3, 0), (2.4, .08, .8), 'wood', bottom=True)
    for sx in (-1, 1): mb.box((sx * 1.05, .13, 0), (.1, .26, .7), 'wood')
    for k, m in enumerate(('ore_mok', 'ore_hwa', 'ore', 'ore_geum', 'ore_su')):
        x = -.96 + k * .48; mb.box((x, .355, .05), (.42, .03, .5), 'lacq'); mb.box((x, .375, .05), (.36, .012, .44), 'lacqred')
        heap(mb, x, .05, .37, m, 9 if lod == 0 else 4, .15, 50 + k)
    with mb.at(1.7, 0, .1):      # the quern: two stones, a handle, powder round it
        n = 10 if lod == 0 else 6; mb.frus((0, 0, 0), (0, .16, 0), .34, .34, n, 'stone', cap1=True); mb.frus((0, .17, 0), (0, .3, 0), .3, .3, n, 'stone', cap1=True); mb.box((.26, .34, 0), (.05, .16, .05), 'wood')
        mb.frus((0, .002, 0), (0, .012, 0), .62, .5, n, 'powder', cap1=True)
    with mb.at(-1.75, 0, .1, sc=.8): scale_set(mb, lod)
    return 1.0


def lamprack(mb, lod=0):
    """등전: a timber rack hung with unlit lamps for sale, one lit as the sample."""
    for sx in (-1, 1): mb.box((sx * .9, 1.0, 0), (.08, 2.0, .08), 'wood'); mb.box((sx * .9, .04, 0), (.1, .08, .6), 'wood')
    mb.box((0, 1.96, 0), (2.0, .07, .07), 'wood'); mb.box((0, 1.3, 0), (1.8, .05, .05), 'wood')
    for k in range(4):
        with mb.at(-.6 + k * .4, 0, 0): mb.frus((0, 1.93, 0), (0, 1.82, 0), .008, .008, 3, 'iron'); lantern(mb, .22, k == 1, max(lod, 0 if k == 1 else 1), 1.57)
    for k in range(3):
        with mb.at(-.45 + k * .45, 0, 0): mb.frus((0, 1.28, 0), (0, 1.2, 0), .008, .008, 3, 'iron'); lantern(mb, .2, False, 1, .97)
    return 2.0


def armillary(mb, y, r, lod):
    n = 12 if lod == 0 else 6
    for ax in ((0, 0), (90, 0), (66, 90)):
        with mb.at(0, y, 0, pitch=ax[0], yaw=ax[1]):
            pts = [(math.cos(t) * r, 0, math.sin(t) * r) for t in np.linspace(0, 2 * math.pi, n + 1)]
            for a, b in zip(pts[:-1], pts[1:]): mb.frus(a, b, r * .05, r * .05, 3, 'yugi')
    mb.frus((0, y - r * 1.05, 0), (0, y + r * 1.05, 0), r * .03, r * .03, 3, 'iron', phase=0)


def yugistall(mb, lod=0):
    """유기전: a counter of brass ware and instruments - bowls, a small scaphe sundial, an armillary sphere on a dragon-less plain stand, a lamp body."""
    mb.box((0, .4, 0), (2.2, .08, .8), 'wood', bottom=True)
    for sx in (-1, 1): mb.box((sx * .95, .18, 0), (.1, .36, .7), 'wood')
    n = 8 if lod == 0 else 5
    for k, (x, z, s) in enumerate(((-.9, .15, .16), (-.62, -.1, .12), (-.4, .2, .1), (-.75, -.2, .09))): mb.frus((x, .44, z), (x, .44 + s * .7, z), s * .5, s, n, 'yugi', cap0=True)
    mb.frus((.0, .44, .05), (.0, .5, .05), .1, .12, n, 'yugi'); mb.frus((.0, .62, .05), (.0, .5, .05), .2, .08, n, 'yugi'); mb.frus((0, .5, .05), (.07, .62, .05), .008, .004, 3, 'iron')      # 앙부일구
    mb.frus((.6, .44, 0), (.6, .5, 0), .14, .1, n, 'yugi'); mb.frus((.6, .5, 0), (.6, .62, 0), .03, .03, 4, 'yugi'); armillary(mb, .86, .22, lod)
    with mb.at(.6, 0, 0): pass
    with mb.at(.6 - .6, 0, 0): pass
    return 1.1


def pump(mb, lod=0):
    """유기 용두 양수기 / 방화 기물: a sealed ore jar on a stone foot, a brass riser with a down-turned spout, a long lever; a water tub."""
    n = 8 if lod == 0 else 5; mb.box((0, .1, 0), (.7, .2, .7), 'stone'); jar(mb, 0, 0, .18, .8, n, hoops=(.5,) if lod == 0 else ())
    mb.tube([(0, .9, 0), (0, 1.35, 0), (.1, 1.5, 0), (.42, 1.5, 0), (.55, 1.38, 0)], .05, 6 if lod == 0 else 4, 'yugi'); mb.frus((.55, 1.38, 0), (.6, 1.3, 0), .05, .075, 6, 'yugi')
    mb.box((-.05, 1.42, 0), (.06, .2, .06), 'yugi'); mb.frus((-.05, 1.5, 0), (-.85, 1.05, 0), .022, .022, 4, 'wood')
    mb.frus((.75, 0, 0), (.75, .4, 0), .3, .36, n, 'wood'); mb.frus((.75, .3, 0), (.75, .34, 0), .37, .37, n, 'iron'); mb.frus((.75, .36, 0), (.75, .362, 0), .33, .0, n, 'iron')
    return 1.55


def gauge(mb, lod=0):
    """측우기 + 측우대, 곁에 수표석: the brass cylinder on its stone stand (a little over life size, to read at street scale) and a graduated stone pillar."""
    n = 8 if lod == 0 else 5; mb.box((0, .08, 0), (.9, .16, .9), 'stone'); mb.frus((0, .16, 0), (0, 1.0, 0), .36 * 1.41, .3 * 1.41, 4, 'stone', cap1=True, phase=.5)
    mb.frus((0, 1.0, 0), (0, 1.14, 0), .12, .12, n, 'yugi'); mb.frus((0, 1.145, 0), (0, 1.28, 0), .12, .12, n, 'yugi'); mb.frus((0, 1.285, 0), (0, 1.42, 0), .12, .12, n, 'yugi'); mb.frus((0, 1.42, 0), (0, 1.38, 0), .105, .1, n, 'iron', cap1=True)
    mb.box((.22, 1.2, .1), (.02, .44, .035), 'yugi')      # 주척
    with mb.at(0, .6, .425, pitch=83): plate(mb, .34, 11, lod, th=.012)
    with mb.at(1.5, 0, .2):      # 수표석
        mb.box((0, .1, 0), (.6, .2, .6), 'stone'); mb.frus((0, .2, 0), (0, 2.2, 0), .17, .15, 6, 'stone'); mb.frus((0, 2.2, 0), (0, 2.34, 0), .24, .1, 6, 'stone', cap1=True)
        if lod == 0:
            for k in range(9): mb.box((0, .42 + k * .2, .15), (.16 if k % 3 == 0 else .1, .014, .02), 'yugi')
    return 2.34


def pungi(mb, lod=0):
    """풍기대: an octagonal stone stand carrying a pole with a brass vane (instead of the cloth streamer)."""
    n = 8 if lod == 0 else 4; mb.box((0, .2, 0), (.8, .4, .8), 'stone'); mb.frus((0, .4, 0), (0, 1.5, 0), .22, .19, n, 'stone'); mb.frus((0, 1.5, 0), (0, 1.62, 0), .28, .22, n, 'stone', cap1=True)
    mb.frus((0, 1.62, 0), (0, 4.4, 0), .035, .02, 5, 'wood'); mb.frus((0, 2.3, 0), (0, 2.36, 0), .05, .05, 5, 'yugi')
    with mb.at(0, 4.2, 0, yaw=25): mb.box((.33, 0, 0), (.6, .16, .012), 'yugi', bottom=True); mb.box((.72, 0, 0), (.2, .3, .012), 'yugi', bottom=True); mb.frus((-.1, 0, 0), (-.34, 0, 0), .03, .0, 4, 'yugi')
    return 4.4


def sibo(mb, lit=True, lod=0):
    """마석 시보 기물 (자격루 + 앙부일구): a stepped stone stand; three lacquer vessels step down (파수호) into two tall brass receivers with float rods;
    in front a scaphe sundial on four legs; on top the ore egg that drives it, in a lotus calyx under a small cap."""
    n = 10 if lod == 0 else 6
    mb.box((0, .15, 0), (3.6, .3, 2.6), 'stone'); mb.box((0, .42, -.3), (3.0, .24, 1.7), 'stone')
    for k, (x, h) in enumerate(((-1.0, .9), (-.35, .55), (.25, .2))): mb.box((x, .54 + h / 2, -.6), (.62, h, .7), 'stone')
    for k, (x, h, s) in enumerate(((-1.0, .9, .78), (-.35, .55, .62), (.25, .2, .5))):
        jar(mb, x, -.6, .54 + h, s, n, mat='lacq', hoops=(.5,) if lod == 0 else ())
        if lod == 0: x1 = x + .6 if k < 2 else x + .55; mb.tube([(x + .3 * s, .54 + h + .16 * s, -.6), (x1 - .12, .54 + h + .04, -.6), (x1 - .12, .54 + h - (.22 if k < 2 else .0), -.6)], .028, 4, 'yugi')
    for x in (.95, 1.3):
        mb.frus((x, .54, -.6), (x, 1.75, -.6), .15, .15, n, 'yugi', cap1=True)
        for f in ((.3, .9) if lod == 0 else ()): mb.frus((x, .54 + f - .02, -.6), (x, .54 + f + .02, -.6), .162, .162, n, 'lacq')
        mb.frus((x, 1.75, -.6), (x, 2.25, -.6), .014, .014, 3, 'wood'); mb.box((x, 2.05, -.6), (.2, .03, .02), 'lacqred')
    with mb.at(0, .54, .55):      # the sundial bowl on four legs, a gnomon, on a low stone table
        mb.box((0, .2, 0), (.9, .4, .9), 'stone')
        for k in range(4): a = k * math.pi / 2 + .785; mb.frus((math.cos(a) * .3, .4, math.sin(a) * .3), (math.cos(a) * .36, .72, math.sin(a) * .36), .03, .025, 4, 'yugi')
        mb.frus((0, .56, 0), (0, .8, 0), .12, .44, n, 'yugi', cap0=True); mb.frus((0, .8, 0), (0, .83, 0), .47, .47, n, 'yugi'); mb.frus((0, .8, 0), (0, .62, 0), .43, .14, n, 'lacq', cap1=True); mb.frus((0, .62, -.1), (.0, .86, .16), .012, .004, 3, 'iron')
    with mb.at(-1.0, 2.2, -.6):      # the driving egg over the top vessel
        mb.frus((0, 0, 0), (0, .16, 0), .05, .05, 6, 'yugi'); mb.frus((0, .16, 0), (0, .3, 0), .06, .2, 6, 'yugi'); egg(mb, .22, .19, lit, lod)
        for k in range(4): a = k * math.pi / 2 + .785; mb.frus((math.cos(a) * .24, .2, math.sin(a) * .24), (math.cos(a) * .24, .78, math.sin(a) * .24), .014, .014, 3, 'lacq')
        roof4(mb, .78, .62, 'lacq', lod)
    with mb.at(1.5, .54, .5): plate(mb, .5, 13, lod)
    return 3.3


def chimney(mb, lod=0):
    """연가 굴뚝: a free-standing earthenware flue of stacked lengths with brass bands, topped by a little house-shaped cap (연가) with a brass rain lid."""
    n = 6 if lod == 0 else 4; mb.box((0, .14, 0), (.56, .28, .56), 'stone'); y = .28
    for k in range(3): mb.frus((0, y, 0), (0, y + .5, 0), .2 - k * .015, .185 - k * .015, n, 'onggi'); y += .5; mb.frus((0, y - .03, 0), (0, y + .03, 0), .215 - k * .015, .215 - k * .015, n, 'yugi')
    mb.box((0, y + .12, 0), (.4, .2, .4), 'onggi'); mb.frus((0, y + .22, 0), (0, y + .4, 0), .3, .06, 4, 'onggi', phase=.5); mb.frus((0, y + .4, 0), (0, y + .46, 0), .08, .03, 4, 'yugi', cap1=True, phase=.5)
    return y + .46


def tap(mb, lod=0):
    """도관 꼭지: a short earthenware riser out of the conduit with a brass cap and ring - where a stall or a house draws ore."""
    n = 6 if lod == 0 else 4; mb.box((0, .06, 0), (.34, .12, .34), 'stone'); mb.frus((0, .12, 0), (0, .62, 0), .085, .075, n, 'onggi'); mb.frus((0, .34, 0), (0, .39, 0), .1, .1, n, 'yugi'); mb.frus((0, .62, 0), (0, .7, 0), .1, .06, n, 'yugi', cap1=True)
    return .7


def oredust(mb, lod=0):
    """patched conduit end for poor quarters: a broken jar and a short length of pipe bound with rope-coloured wood strips."""
    mb.lathe(0, 0, [(0, .16), (.12, .27), (.3, .32)], 6, 'onggi', cap=False); mb.frus((.35, .07, -.4), (.35, .07, .5), .08, .08, 6 if lod == 0 else 4, 'onggi'); mb.frus((.35, .07, .0), (.35, .07, .08), .095, .095, 6, 'wood')
    heap(mb, -.3, .3, 0, 'onggi', 4, .25, 9); return .32


KIT = {
    'lamp_street': lambda mb, lod: stone_lamp(mb, 2.6, True, lod), 'lamp_street_off': lambda mb, lod: stone_lamp(mb, 2.6, False, lod),
    'lamp_formal': lambda mb, lod: stone_lamp(mb, 3.1, True, lod, formal=True), 'lamp_formal_off': lambda mb, lod: stone_lamp(mb, 3.1, False, lod, formal=True),
    'lamp_gate': lambda mb, lod: stone_lamp(mb, 1.9, True, lod, stub=False), 'lamp_gate_off': lambda mb, lod: stone_lamp(mb, 1.9, False, lod, stub=False),
    'lamp_hook': lambda mb, lod: hook_lamp(mb, True, lod), 'lamp_hook_off': lambda mb, lod: hook_lamp(mb, False, lod),
    'conduit': conduit, 'junction': junction, 'tap': tap, 'plate': lambda mb, lod: plate(mb, .56, 1, lod), 'plate_b': lambda mb, lod: plate(mb, .56, 2, lod),
    'reservoir': reservoir, 'chest': chest, 'scale_set': scale_set, 'cannon': cannon, 'hwacha': hwacha,
    'beacon': lambda mb, lod: beacon(mb, True, lod), 'beacon_off': lambda mb, lod: beacon(mb, False, lod), 'gatebox': gatebox, 'brazier': brazier,
    'orestall': orestall, 'lamprack': lamprack, 'yugistall': yugistall, 'pump': pump, 'gauge': gauge, 'pungi': pungi,
    'sibo': lambda mb, lod: sibo(mb, True, lod), 'chimney': chimney, 'oredust': oredust,
}
LIT = {'lamp_street': 1, 'lamp_formal': 1, 'lamp_gate': 1, 'lamp_hook': 1, 'beacon': 1, 'sibo': 1, 'lamprack': 1}
NAMES = {'lamp_street': '마석 가로등(장명등형)', 'lamp_formal': '의장 마석 등(석등형)', 'lamp_gate': '문등', 'lamp_hook': '등롱 걸이', 'conduit': '옹기 도관', 'junction': '이음 독', 'tap': '도관 꼭지',
         'plate': '술식 유기 패', 'reservoir': '저장 독', 'chest': '봉인 마석 궤', 'scale_set': '저울 · 되 · 말', 'cannon': '술식 총통', 'hwacha': '신기전 틀', 'beacon': '봉수 화두', 'gatebox': '개폐 함',
         'brazier': '마석 화로', 'orestall': '마석전', 'lamprack': '등전', 'yugistall': '유기전', 'pump': '유기 양수기', 'gauge': '측우기 · 수표석', 'pungi': '풍기대', 'sibo': '마석 시보 기물', 'chimney': '연가 굴뚝', 'oredust': '기운 도관'}


def build(kid, lod):
    mb = MB(); KIT[kid](mb, lod); out = []
    for mat, b in mb.sub.items():
        v = np.array(b['v'], float); n = np.array(b['n'], float); ln = np.linalg.norm(n, axis=1); assert (ln > .5).all(), (kid, mat)
        out.append(dict(mat=mat, v=[round(float(x), 4) for x in v.ravel()], n=[round(float(x), 3) for x in n.ravel()], t=b['t']))
    return out, mb


def dump():
    return [dict(id=k, lit=LIT.get(k, 0), lods=[dict(subs=build(k, 0)[0]), dict(subs=build(k, 1)[0])]) for k in KIT]


def sheet(out):
    """CPU contact sheets: painter's algorithm with PIL (no GPU, no matplotlib)."""
    from PIL import Image, ImageDraw
    out.mkdir(parents=True, exist_ok=True); ids = [k for k in KIT if not k.endswith('_off') and k != 'plate_b']; L = np.array([.45, .8, .4]); L /= np.linalg.norm(L); W, H = 640, 560
    az, el = math.radians(35), math.radians(16); fw = np.array([-math.sin(az) * math.cos(el), -math.sin(el), -math.cos(az) * math.cos(el)]); rt = np.cross(fw, [0, 1, 0]); rt /= np.linalg.norm(rt); up = np.cross(rt, fw)
    for s0 in range(0, len(ids), 4):
        img = Image.new('RGB', (W * 2, H * 2), (217, 211, 196)); dr = ImageDraw.Draw(img)
        for i, kid in enumerate(ids[s0:s0 + 4]):
            subs, mb = build(kid, 0); ox, oy = (i % 2) * W, (i // 2) * H; tris = []
            for sb in subs:
                v = np.array(sb['v']).reshape(-1, 3); nn = np.array(sb['n']).reshape(-1, 3); t = np.array(sb['t']).reshape(-1, 3); base = np.array(COL.get(sb['mat'], COL['ore'] if sb['mat'].startswith('ore') else (.5, .5, .5)))
                for tr in t:
                    P = v[tr]; fn = np.cross(P[1] - P[0], P[2] - P[0])
                    if fn @ fw > 0: continue
                    sh = float(np.clip(nn[tr].mean(0) @ L, 0, 1)) * .7 + .38; c = base if sb['mat'] == 'core' else np.clip(base * sh, 0, 1); tris.append((float(P.mean(0) @ fw), P, tuple(int(x * 255) for x in c)))
            av = np.vstack([p for _, p, _ in tris]); lo, hi = av.min(0), av.max(0); c0 = (lo + hi) / 2; r = np.linalg.norm(hi - lo) / 2 * 1.02; k = (min(W, H) / 2 - 26) / r
            for _, P, c in sorted(tris, key=lambda q: -q[0]):
                dr.polygon([(ox + W / 2 + float((p - c0) @ rt) * k, oy + H / 2 + 12 - float((p - c0) @ up) * k) for p in P], fill=c)
            dr.text((ox + 8, oy + 6), '%s  %.2f x %.2f x %.2f m  %d / %d tris' % (kid, hi[0] - lo[0], hi[1] - lo[1], hi[2] - lo[2], mb.tris(), build(kid, 1)[1].tris()), fill=(20, 20, 20))
        img.save(out / ('arcane_kit_%d.png' % (s0 // 4)))


if __name__ == '__main__':
    root = Path(__file__).resolve().parents[2]
    if len(sys.argv) > 1 and sys.argv[1] == 'sheet': sheet(root / 'Art/World/PackLite308/_sheets/arcane')
    for k in KIT: print('%-16s LOD0 %4d  LOD1 %4d' % (k, build(k, 0)[1].tris(), build(k, 1)[1].tris()))
