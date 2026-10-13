"""#308 the first mine - the DERELICT ore-craft kit ("버려진 마석 광산"): the capital's vocabulary (arcane308_kit.py, D308-64 / D308-79) translated to an
abandoned gallery (D308-60). Offline, deterministic, no Blender. Vocabulary: Tools/Unity/Stage308_minearcane/REFERENCES_mine.md.

  python Tools/Art/arcane308_mine_kit.py            # triangles per object and LOD
  python Tools/Art/arcane308_mine_kit.py export     # OBJ + manifest -> Art/World/PackLite308/arcane_mine/, kit json -> Tools/Unity/Stage308_minearcane/mine_arcane308_kit.json
  python Tools/Art/arcane308_mine_kit.py sheet      # contact sheets -> Art/World/PackLite308/_sheets/arcane_mine/*.png (CPU)

Every object: metres, +y up, origin on the FLOOR. Wall pieces (seat 'wall'): the wall is the plane z = 0 and the piece stands out toward +z.
Material keys (KEYS): Joseon ware (yugi brass, onggi earthenware, lacq lacquer, paper), the mine's own stone / timber / iron, ore chips, powder, slag, soot,
straw rope - and the ore egg: `eggdead` (dull grey-green, UNLIT, cracked - the default) and `egglit` (the mine's living-vein material; the ONLY one that glows).
No rail, no cart, no gear, no gauge, no copper pipe, no boiler, no post lamp, no letters (plates carry stroke fragments only).
"""
import json, math, sys
from pathlib import Path
import numpy as np
sys.path.insert(0, str(Path(__file__).resolve().parent))
from arcane308_kit import MB, strokes, plate, heap, jar, JAR      # the capital's mesh writer and small parts (read only)

KEYS = ['yugi', 'onggi', 'lacq', 'paper', 'stone', 'wood', 'iron', 'ore', 'powder', 'slag', 'soot', 'rope', 'eggdead', 'egglit']
COL = dict(yugi=(.5, .4, .2), onggi=(.25, .14, .09), lacq=(.07, .05, .05), paper=(.55, .52, .44), stone=(.42, .41, .39), wood=(.23, .16, .11), iron=(.13, .13, .14),
           ore=(.25, .3, .22), powder=(.35, .43, .31), slag=(.2, .2, .21), soot=(.04, .04, .04), rope=(.45, .38, .22), eggdead=(.2, .23, .2), egglit=(.425, .52, .38))


# ---------------------------------------------------------------- parts
def sq(mb, a, b, r, mat='wood', caps=True):
    """a squared timber between two points (r = half diagonal)"""
    mb.frus(a, b, r, r, 4, mat, cap0=caps, cap1=caps)


def rod(mb, a, b, r, mat, n=5, caps=False): mb.frus(a, b, r, r, n, mat, cap0=caps, cap1=caps)


def revolve(mb, x, z, prof, n, mat, a0=0., a1=360., inner=None, y0=0.):
    """a partial solid of revolution (flat facets). inner = material of the inside faces (a broken vessel shows its inside)."""
    k = max(2, int(round(n * (a1 - a0) / 360.))); ang = [math.radians(a0 + (a1 - a0) * i / k) for i in range(k + 1)]
    for (h0, r0), (h1, r1) in zip(prof[:-1], prof[1:]):
        for i in range(k):
            c0, s0, c1, s1 = math.cos(ang[i]), math.sin(ang[i]), math.cos(ang[i + 1]), math.sin(ang[i + 1])
            q = [(x + c0 * r0, y0 + h0, z + s0 * r0), (x + c0 * r1, y0 + h1, z + s0 * r1), (x + c1 * r1, y0 + h1, z + s1 * r1), (x + c1 * r0, y0 + h0, z + s1 * r0)]
            if r0 < 1e-6: q = q[1:]
            elif r1 < 1e-6: q = [q[0], q[1], q[3]]
            mb.poly(q, mat)
            if inner: mb.poly([(p[0] - (p[0] - x) * .04, p[1], p[2] - (p[2] - z) * .04) for p in reversed(q)], inner)


def disc(mb, x, y, z, r, mat, n=9, seed=0, squash=1., yaw=0.):
    """a flat stain / fan on the floor (irregular)"""
    rng = np.random.default_rng(900 + seed); pts = []
    for i in range(n):
        a = i * 2 * math.pi / n; rr = r * rng.uniform(.7, 1.15); px, pz = math.cos(a) * rr, math.sin(a) * rr * squash; cy, sy = math.cos(math.radians(yaw)), math.sin(math.radians(yaw))
        pts.append((x + px * cy + pz * sy, y, z - px * sy + pz * cy))
    mb.poly(list(reversed(pts)), mat, (0, 1, 0))


def stone_block(mb, x, y, z, r, h, seed=0, n=6, mat='stone'):
    """a rough field stone with a flat top (things sit on it): returns the top height"""
    rng = np.random.default_rng(700 + seed); bot = []; top = []
    for i in range(n):
        a = i * 2 * math.pi / n + rng.uniform(-.2, .2); rb = r * rng.uniform(.85, 1.15); rt = r * rng.uniform(.6, .85)
        bot.append((x + math.cos(a) * rb, y, z + math.sin(a) * rb)); top.append((x + math.cos(a) * rt, y + h * rng.uniform(.93, 1.), z + math.sin(a) * rt))
    for i in range(n):
        j = (i + 1) % n; mb.poly([bot[j], bot[i], top[i]], mat); mb.poly([bot[j], top[i], top[j]], mat)
    mb.poly(list(reversed(top)), mat); return y + h * .93


def egg(mb, x, y, z, r, state, lod, yaw=0., pitch=0., chip=True):
    """the ore egg. state: 'lit' whole and alive | 'dead' whole, grey, a chip out of the crown | 'split' two halves fallen apart | 'stump' the lower half only"""
    n = 6 if lod == 0 else 4; prof = [(0., 0.), (r * .35, r * .72), (r * 1.0, r), (r * 1.7, r * .62), (r * 2.1, 0.)]
    with mb.at(x, y, z, yaw=yaw, pitch=pitch):
        if state == 'lit': mb.lathe(0, 0, prof, n, 'egglit', cap=False)
        elif state == 'dead':
            revolve(mb, 0, 0, prof[:4] + [(r * 1.82, r * .5)], n, 'eggdead'); revolve(mb, 0, 0, [(r * 1.82, r * .5), (r * 1.55, 0.)], n, 'soot')      # the crown is gone: a dark socket
            if lod == 0 and chip: mb.frus((r * 1.3, r * .1, r * .4), (r * 1.3, r * .1 + r * .5, r * .5), r * .3, 0, 3, 'eggdead', cap0=True)                       # the chip lies beside it
        elif state == 'split':
            for a0, dx, rl in ((0, r * .25, 14), (180, -r * .3, -22)):
                with mb.at(dx, 0, 0, roll=rl):
                    revolve(mb, 0, 0, prof, n, 'eggdead', a0 - 90, a0 + 90); cut = [(0, h, rr * s) for h, rr in prof for s in (1,)] + [(0, h, -rr) for h, rr in reversed(prof[1:-1])]
                    mb.poly(cut if a0 == 180 else list(reversed(cut)), 'soot')
        else: revolve(mb, 0, 0, prof[:3], n, 'eggdead'); mb.poly([(math.cos(i * 2 * math.pi / n) * r * .98, r * 1.0, -math.sin(i * 2 * math.pi / n) * r * .98) for i in range(n)], 'soot', (0, 1, 0))


def bud(mb, x, y, z, s=1., n=6):
    """놋쇠 연봉 받침: the brass lotus-bud calyx an egg sits in. returns the seat height"""
    mb.frus((x, y, z), (x, y + .025 * s, z), .05 * s, .03 * s, n, 'yugi', cap0=False); mb.frus((x, y + .025 * s, z), (x, y + .07 * s, z), .03 * s, .075 * s, n, 'yugi'); return y + .04 * s


def tag(mb, w, seed, lod):
    """a small brass plate with stroke fragments (not letters); lies in XZ, +y up, centred."""
    mb.box((0, .008, 0), (w, .016, w * 1.3), 'yugi')
    if lod == 0:
        mb.box((0, .018, 0), (w * .8, .004, w * 1.08), 'lacq')
        with mb.at(0, .02, 0): strokes(mb, w * .72, 'yugi', seed, th=.006)
        for sx in (-1, 1): mb.frus((sx * w * .4, .016, w * .56), (sx * w * .4, .03, w * .56), .014, .006, 5, 'yugi', cap1=True)


def pipe(mb, a, b, r=.07, lod=0, band=True, broken_end=False):
    """one fired earthenware conduit length with a brass band at the socket end b"""
    n = 6 if lod == 0 else 4; a = np.asarray(a, float); b = np.asarray(b, float); mb.frus(a, b, r, r * .95, n, 'onggi')
    if band and lod == 0: d = (b - a) / np.linalg.norm(b - a); mb.frus(b - d * .07, b - d * .02, r * 1.28, r * 1.28, n, 'yugi')
    if broken_end: d = (b - a) / np.linalg.norm(b - a); mb.frus(a + d * .002, a + d * .05, r * .8, r * .8, n, 'soot', cap0=True)      # the dark bore of a snapped length


def shards(mb, x, y, z, n, r, seed, mat='onggi'):
    rng = np.random.default_rng(500 + seed)
    for _ in range(n):
        a = rng.uniform(0, 6.283); d = r * math.sqrt(rng.random()); s = rng.uniform(.04, .1); px, pz = x + math.cos(a) * d, z + math.sin(a) * d; t = rng.uniform(0, 6.283)
        q = [(px + math.cos(t + k * 2.1) * s * rng.uniform(.6, 1.2), y + .006 + (.03 * rng.random() if k == 1 else 0), pz + math.sin(t + k * 2.1) * s * rng.uniform(.6, 1.2)) for k in range(3)]
        mb.poly(list(reversed(q)), mat); mb.poly(q, 'soot')


def rope_line(mb, pts, r=.016, n=4):
    for a, b in zip(pts[:-1], pts[1:]): mb.frus(a, b, r, r, n, 'rope')


def sag(a, b, drop, k=6):
    a = np.asarray(a, float); b = np.asarray(b, float); return [tuple(a + (b - a) * t + np.array([0, -drop * 4 * t * (1 - t), 0])) for t in np.linspace(0, 1, k + 1)]


def fret(mb, w, h, seed, both=False, mat='yugi', t=.011, dz=0.):
    """창살 면: a flat brass fret in the XY plane facing +z - a frame and a few stroke fragments (not letters), a different set for every seed."""
    rng = np.random.default_rng(4100 + seed)
    def bar(x0, y0, x1, y1):
        nonlocal t
        d = np.array([x1 - x0, y1 - y0], float); L = np.linalg.norm(d); d /= L; nx, ny = -d[1] * t / 2, d[0] * t / 2; q = [(x0 - nx, y0 - ny, dz), (x1 - nx, y1 - ny, dz), (x1 + nx, y1 + ny, dz), (x0 + nx, y0 + ny, dz)]
        mb.poly(q, mat, (0, 0, 1))
        if both: mb.poly(list(reversed(q)), mat, (0, 0, -1))
    a, b = w / 2, h / 2; m = min(w, h); t0 = t
    for x0, y0, x1, y1 in ((-a, -b, a, -b), (-a, b, a, b), (-a, -b, -a, b), (a, -b, a, b)): bar(x0, y0, x1, y1)
    # brush fragments, never glyphs: strokes of unequal length and weight that do not touch each other or the frame and follow no cell
    done = []; want = 4 if max(w, h) / m > 1.6 else 3
    for _ in range(60):
        if len(done) >= want: break
        ang = math.radians(float(rng.choice([8., 82., 38., -42., 22., -63.])) + rng.uniform(-7, 7)); L = m * rng.uniform(.34, .78); cx = rng.uniform(-a, a) * .74; cy = rng.uniform(-b, b) * .74; hx, hy = math.cos(ang) * L / 2, math.sin(ang) * L / 2
        if abs(cx) + abs(hx) > a - t0 * 1.6 or abs(cy) + abs(hy) > b - t0 * 1.6: continue
        if any(math.hypot(cx - px, cy - py) < (L + pl) * .42 for px, py, pl in done): continue
        done.append((cx, cy, L)); t = t0 * rng.uniform(.9, 1.9); bar(cx - hx, cy - hy, cx + hx, cy + hy)
    t = t0


def scroll(mb, lod=0, L=.64, W=.4, seed=3):
    """측량 도면 두루마리: a survey drawing left unrolled - lies in XZ from x = 0 to L, the rolled end at x = L, a brass weight on one corner.
    Ink: a ruled grid, one slanting vein line, a cluster of stroke fragments. No readable text."""
    def quad(x0, z0, x1, z1, y, mat): mb.poly([(x0, y, z1), (x1, y, z1), (x1, y, z0), (x0, y, z0)], mat, (0, 1, 0))
    quad(0, -W / 2, L, W / 2, .004, 'paper'); rod(mb, (L + .022, .028, -W / 2 - .03), (L + .022, .028, W / 2 + .03), .028, 'paper', 6 if lod == 0 else 4, True)
    rod(mb, (.0, .012, -W / 2), (.0, .012, W / 2), .012, 'paper', 4, True)                                        # the free end curls
    if lod == 0:
        for sz in (-1, 1): rod(mb, (L + .022, .028, sz * (W / 2 + .03)), (L + .022, .028, sz * (W / 2 + .06)), .014, 'lacq', 5, True)
        gx0, gx1, gz = .07, L * .6, W / 2 - .05
        for k in range(5): z = -gz + k * 2 * gz / 4; quad(gx0, z - .002, gx1, z + .002, .0065, 'soot')
        for k in range(6): x = gx0 + k * (gx1 - gx0) / 5; quad(x - .002, -gz, x + .002, gz, .0065, 'soot')
        mb.poly([(gx0 + .02, .0075, gz * .7), (gx1 - .03, .0075, -gz * .5), (gx1 - .03, .0075, -gz * .5 - .012), (gx0 + .02, .0075, gz * .7 - .012)], 'soot', (0, 1, 0))      # the vein as it was traced
        with mb.at(L * .8, .006, 0): strokes(mb, .17, 'soot', seed, th=.003)
        mb.box((.06, .022, W / 2 - .07), (.07, .036, .07), 'yugi')
    else: quad(.07, -W / 2 + .05, L * .6, W / 2 - .05, .0065, 'soot'); mb.box((.06, .022, W / 2 - .07), (.07, .036, .07), 'yugi')
    return .06


# ---------------------------------------------------------------- 14 감정대
def assay(mb, lod=0):
    """감정대: where an ore egg was set up to be judged - a brass tripod, a lotus-bud seat, a DEAD egg; under it a brass disc with stroke fragments in relief.
    (Pinterest: a great crystal stood on a tripod over a patterned disc. Here only the egg could glow, and this one is dead.)"""
    n = 10 if lod == 0 else 6
    mb.frus((0, 0, 0), (0, .03, 0), .36, .34, n, 'yugi', cap1=True)
    if lod == 0:
        mb.frus((0, .03, 0), (0, .034, 0), .27, .27, n, 'lacq', cap1=True)
        with mb.at(0, .034, 0, yaw=25): strokes(mb, .34, 'yugi', 11, th=.008)
        mb.frus((.2, .034, -.12), (.21, .07, -.11), .03, 0, 3, 'eggdead', cap0=True)                                # the chip that came off the crown
    for k in range(3):
        a = math.radians(90 + k * 120); fx, fz = math.cos(a) * .3, math.sin(a) * .3
        rod(mb, (fx, .03, fz), (math.cos(a) * .06, .8, math.sin(a) * .06), .015, 'yugi', 4 if lod == 0 else 3)
        if lod == 0: mb.frus((fx, .03, fz), (fx, .055, fz), .036, .02, 5, 'yugi', cap1=True)
    mb.frus((0, .41, 0), (0, .435, 0), .19, .185, 6, 'yugi'); mb.frus((0, .435, 0), (0, .41, 0), .17, .175, 6, 'yugi')      # the tie ring (outside and inside)
    mb.frus((0, .78, 0), (0, .82, 0), .07, .08, 6, 'yugi', cap1=True); seat = bud(mb, 0, .82, 0, 1.3); egg(mb, 0, seat, 0, .095, 'dead', lod, chip=False)
    return 1.1


# ---------------------------------------------------------------- 15 솥형 도가니
def ding(mb, lod=0, x=0., z=0., yaw=0.):
    """청동 솥(鼎)형 도가니: the smelting pot as a three-legged cauldron with two upright ears and a stroke plate let into each side. Cold: soot inside, a crust of slag.
    (Pinterest: a bronze ding-shaped device with square inlaid panels.)"""
    n = 8 if lod == 0 else 6; st = 180. / n
    with mb.at(x, 0, z, yaw=yaw):
        for k in range(3):
            a = math.radians(90 + k * 120); mb.frus((math.cos(a) * .27, 0, math.sin(a) * .27), (math.cos(a) * .2, .3, math.sin(a) * .2), .035, .062, 5 if lod == 0 else 4, 'yugi', cap0=True)
        revolve(mb, 0, 0, [(.19, 0.), (.2, .13), (.27, .28), (.4, .355), (.56, .36), (.62, .33), (.66, .37)], n, 'yugi', st, 360. + st)
        revolve(mb, 0, 0, [(.66, .37), (.66, .31), (.57, .3)], n, 'soot', st, 360. + st)                            # the lip and the dark inside
        mb.poly([(math.cos(math.radians(st + i * 360. / n)) * .3, .57, -math.sin(math.radians(st + i * 360. / n)) * .3) for i in range(n)], 'slag', (0, 1, 0))
        if lod == 0: heap(mb, .05, -.04, .57, 'slag', 3, .16, 61)
        for sx in (-1, 1):
            if lod == 0:
                for sz in (-1, 1): mb.box((sx * .345, .74, sz * .075), (.035, .16, .035), 'yugi')
                mb.box((sx * .345, .835, 0), (.035, .035, .185), 'yugi')
            else: mb.box((sx * .345, .76, 0), (.035, .2, .185), 'yugi')
        if lod == 0:
            ap = .355 * math.cos(math.radians(st))
            for sz, seed in ((1, 8), (-1, 9)):
                with mb.at(0, .47, sz * (ap + .004), yaw=0 if sz > 0 else 180):
                    mb.box((0, 0, 0), (.2, .15, .012), 'lacq'); fret(mb, .17, .12, seed, dz=.008)
    return .86


# ---------------------------------------------------------------- 1 선광 탁자
def sort_table(mb, lod=0, broken=False, drawing=False):
    """선광 탁자: a low trestle table where ore was graded - lacquer trays, a brass hand balance. broken = the far trestle gave way: trays slid off, the balance lies on the floor."""
    def trestle(x, h):
        for sz in (-1, 1): sq(mb, (x, 0, sz * .3), (x, h, sz * .12), .04)
        sq(mb, (x, h * .45, -.22), (x, h * .45, .22), .028)
    def tray(x, y, z, yaw, fill=None, pitch=0., roll=0.):
        with mb.at(x, y, z, yaw=yaw, pitch=pitch, roll=roll):
            mb.box((0, .012, 0), (.42, .024, .3), 'lacq', bottom=True)
            for sx, sz, wx, wz in ((0, 1, .42, .02), (0, -1, .42, .02), (1, 0, .02, .3), (-1, 0, .02, .3)): mb.box((sx * .2, .04, sz * .14), (wx, .05, wz), 'lacq')
            if lod == 0:
                for sx in (-1, 1): mb.box((sx * .205, .04, 0), (.012, .052, .08), 'yugi')
            if fill == 'ore': heap(mb, 0, 0, .02, 'ore', 6 if lod == 0 else 3, .12, 11)
            elif fill == 'powder': mb.box((0, .03, 0), (.36, .012, .24), 'powder')
    def balance(x, y, z, fallen):
        with mb.at(x, y, z, yaw=25 if fallen else -12, roll=88 if fallen else 0):
            mb.frus((0, 0, 0), (0, .03, 0), .07, .05, 6, 'yugi', cap1=True); rod(mb, (0, .03, 0), (0, .42, 0), .012, 'yugi', 4); sq(mb, (-.28, .41, 0), (.28, .43, 0), .012, 'lacq')
            for sx, dy in ((-1, -.05), (1, .03)):
                px = sx * .27
                if lod == 0 and not fallen:
                    for a in (0, 2.1, 4.2): mb.frus((px, .41, 0), (px + math.cos(a) * .07, .2 + dy, math.sin(a) * .07), .004, .004, 3, 'iron')
                if not fallen: mb.frus((px, .17 + dy, 0), (px, .2 + dy, 0), .05, .085, 6 if lod == 0 else 4, 'yugi', cap0=True)
    if not broken:
        mb.box((0, .52, 0), (1.6, .05, .7), 'wood', bottom=True); trestle(-.6, .5); trestle(.6, .5)
        tray(-.5, .545, .05, 8, 'ore'); tray(.0, .545, -.12, -5, 'powder'); tray(.52, .545, .16, 96); balance(.3, .545, -.2, False)
        if lod == 0: egg(mb, -.1, .545, .2, .05, 'stump', 0)
    else:
        trestle(-.6, .5)
        with mb.at(-.78, .5, 0, roll=-19.5):
            mb.box((.8, .025, 0), (1.6, .05, .7), 'wood', bottom=True); tray(.45, .05, .05, 8, 'ore')
            if drawing:
                with mb.at(.8, .05, -.07, yaw=4): scroll(mb, lod)                                                    # the survey drawing, still pinned by its weight
        for sz in (-1, 1): sq(mb, (.58, 0, sz * .3), (.6, .16, sz * .22), .04)                                    # the snapped legs
        sq(mb, (.9, .03, -.5), (1.3, .03, .1), .04)
        tray(1.15, .0, .28, 40, None, pitch=0, roll=0); tray(.95, .14, -.3, -20, None, pitch=62); balance(1.25, .02, -.35, True)
        for k, (px, pz) in enumerate(((1.42, -.2), (1.0, -.62))): mb.frus((px, .0, pz), (px, .03, pz), .05, .085, 6 if lod == 0 else 4, 'yugi', cap0=True)
        disc(mb, 1.05, .006, .1, .34, 'powder', seed=3)
        if lod == 0: heap(mb, 1.25, .5, 0, 'ore', 5, .2, 5); egg(mb, .75, 0, .52, .05, 'split', 0, yaw=30)
    return .9


# ---------------------------------------------------------------- 2 마석 맷돌
def grinder(mb, lod=0, broken=False):
    """마석 맷돌: a rotary quern that ground ore to powder - brass-banded runner stone, wooden catch tray with a spout, a fan of dust where the powder ran out."""
    n = 10 if lod == 0 else 6
    mb.box((0, .09, 0), (1.05, .06, 1.05), 'wood', bottom=True)
    for sx in (-1, 1): sq(mb, (sx * .4, 0, -.5), (sx * .4, 0, .5), .06)
    if lod == 0:
        for sx, sz, wx, wz in ((0, 1, 1.05, .04), (0, -1, 1.05, .04), (1, 0, .04, 1.05), (-1, 0, .04, 1.05)): mb.box((sx * .505, .15, sz * .505), (wx, .07, wz), 'wood')
    mb.box((0, .1, .68), (.2, .04, .34), 'wood')                                                                # the spout
    mb.frus((0, .12, 0), (0, .32, 0), .4, .4, n, 'stone', cap1=True)
    disc(mb, .05, .006, 1.05, .55, 'powder', seed=1, squash=1.5); disc(mb, 0, .125, 0, .47, 'powder', seed=2)
    if not broken:
        mb.frus((0, .32, 0), (0, .48, 0), .37, .35, n, 'stone', cap1=True); mb.frus((0, .37, 0), (0, .43, 0), .378, .375, n, 'yugi')
        mb.frus((0, .48, 0), (0, .485, 0), .07, .07, 6, 'soot', cap1=True); sq(mb, (.3, .42, 0), (.56, .42, 0), .03); sq(mb, (.53, .42, 0), (.53, .62, 0), .025)
    else:                                                                                                         # the runner split and slid off
        prof = [(0, .36), (.16, .35)]
        with mb.at(.08, .32, -.05, yaw=20):
            revolve(mb, 0, 0, prof, n, 'stone', 0, 180); mb.poly([(-.36, 0, 0), (-.35, .16, 0), (.35, .16, 0), (.36, 0, 0)], 'stone'); mb.poly([(math.cos(math.radians(a)) * .35, .16, math.sin(math.radians(a)) * .35) for a in range(180, -1, -36)], 'stone', (0, 1, 0))
        with mb.at(-.5, .0, -.45, yaw=200, pitch=-58):
            revolve(mb, 0, 0, prof, n, 'stone', 0, 180); mb.poly([(-.36, 0, 0), (-.35, .16, 0), (.35, .16, 0), (.36, 0, 0)], 'stone'); mb.poly([(math.cos(math.radians(a)) * .35, .16, math.sin(math.radians(a)) * .35) for a in range(180, -1, -36)], 'stone', (0, 1, 0)); mb.poly([(math.cos(math.radians(a)) * .36, 0, math.sin(math.radians(a)) * .36) for a in range(0, 181, 36)], 'stone', (0, -1, 0))
        sq(mb, (.5, .03, .55), (.82, .03, .7), .03); mb.frus((.3, .0, -.85), (.3, .03, -.85), .37, .37, n, 'yugi') if lod == 0 else None      # the brass band, sprung, on the floor
    if lod == 0: heap(mb, -.75, .5, 0, 'ore', 5, .22, 7)
    return .62


# ---------------------------------------------------------------- 3 풀무와 화덕
def hearth(mb, lod=0, broken=False, pot=False):
    """식은 화덕과 손풀무: a clay refining hearth on a stone ring, a box bellows with its clay nozzle, a tipped crucible, slag at the mouth. Cold - nothing here glows."""
    n = 8 if lod == 0 else 6; dome = [(.24, .5), (.52, .47), (.78, .33), (.92, .17)]
    mb.frus((0, 0, 0), (0, .24, 0), .6, .54, n, 'stone', cap1=True); disc(mb, .1, .006, .75, .8, 'soot', seed=4, squash=1.2)
    if not broken: revolve(mb, 0, 0, dome, n, 'onggi', 20, 340, inner='soot'); mb.frus((0, .92, 0), (0, .925, 0), .16, 0, n, 'soot')
    else:
        revolve(mb, 0, 0, dome[:3], n, 'onggi', 95, 335, inner='soot'); shards(mb, .45, .25, .2, 7 if lod == 0 else 3, .3, 1); shards(mb, .7, .0, .5, 6 if lod == 0 else 2, .35, 2)
        disc(mb, 0, .245, 0, .44, 'soot', seed=6)
    heap(mb, .15, .8, 0, 'slag', 8 if lod == 0 else 4, .32, 21); heap(mb, 0, .1, .24, 'slag', 4 if lod == 0 else 2, .2, 22)
    # the box bellows (손풀무), 1.0 m long, lying along x on two skids, its nozzle to the hearth
    with mb.at(-1.25, 0, -.1, yaw=-8):
        for sz in (-1, 1): sq(mb, (-.5, .03, sz * .15), (.5, .03, sz * .15), .035)
        mb.box((0, .27, 0), (1.0, .4, .36), 'wood', bottom=False, top=not broken)
        if lod == 0:
            for sx in (-1, 1): mb.box((sx * .38, .27, .183), (.05, .4, .008), 'yugi'); mb.box((sx * .38, .27, -.183), (.05, .4, .008), 'yugi'); (mb.box((sx * .38, .474, 0), (.05, .008, .36), 'yugi') if not broken else None)
        rod(mb, (-.5, .27, 0), (-.95 if not broken else -.7, .27, 0), .022, 'wood', 5, True); sq(mb, (-.95 if not broken else -.7, .27, -.14), (-.95 if not broken else -.7, .27, .14), .028)
        if broken: mb.box((0, .1, 0), (.94, .012, .3), 'soot')
    if not broken: pipe(mb, (-.74, .2, -.05), (-.5, .26, -.02), .05, lod, band=True)
    else:
        pipe(mb, (-.74, .2, -.05), (-.66, .22, -.04), .05, lod, band=False); pipe(mb, (-.78, .05, .35), (-.55, .05, .5), .05, lod, band=True, broken_end=True)
        with mb.at(-1.2, .0, .42, yaw=-20, pitch=-78): mb.box((0, .006, 0), (1.0, .03, .36), 'wood', bottom=True)      # the bellows lid, leaning on the box
    # crucible and tongs
    with mb.at(.75, .07, .25, yaw=30, roll=82): mb.frus((0, -.08, 0), (0, .1, 0), .055, .085, 6, 'stone', cap0=True); mb.frus((0, .098, 0), (0, .1, 0), .07, 0, 6, 'slag')
    if pot: ding(mb, lod, 1.3, -.22, 20)
    if lod == 0: rod(mb, (.5, .015, .95), (1.05, .015, .7), .012, 'iron', 4); rod(mb, (.52, .015, .98), (1.05, .03, .76), .012, 'iron', 4); mb.frus((.95, 0, .35), (.95, .05, .35), .09, .12, 6, 'yugi', cap0=True)
    return .95


# ---------------------------------------------------------------- 4 녹로 틀
def hoist(mb, lod=0, fallen=False):
    """녹로 틀: a timber windlass frame over a blind winze - drum with peg handles and brass end bands, a plain brass sheave, a frayed rope, the ore basket on its side."""
    def drum(y):
        rod(mb, (-.72, y, 0), (.72, y, 0), .095, 'wood', 6 if lod == 0 else 4, True); rod(mb, (-.25, y, 0), (.25, y, 0), .118, 'rope', 6 if lod == 0 else 4)
        if lod == 0:
            for sx in (-1, 1):
                rod(mb, (sx * .62, y, 0), (sx * .68, y, 0), .105, 'yugi', 6)
                for a in (0, 90): c, s = math.cos(math.radians(a + 20 * sx)) * .3, math.sin(math.radians(a + 20 * sx)) * .3; sq(mb, (sx * .86, y - s, -c), (sx * .86, y + s, c), .022)
                rod(mb, (sx * .72, y, 0), (sx * .9, y, 0), .03, 'iron', 4)
    def basket(x, z, yaw):
        with mb.at(x, .22, z, yaw=yaw, roll=78):
            revolve(mb, 0, 0, [(-.2, .2), (.12, .3)], 8 if lod == 0 else 5, 'rope', inner='soot'); mb.poly([(math.cos(i * math.pi / 4) * .2, -.2, -math.sin(i * math.pi / 4) * .2) for i in range(8)], 'rope', (0, -1, 0))
            mb.frus((0, .1, 0), (0, .13, 0), .305, .305, 8 if lod == 0 else 5, 'wood')
    for sx in (-1, 1): sq(mb, (sx * .8, .05, -.75), (sx * .8, .05, .75), .07)                                 # sills
    if not fallen:
        for sx in (-1, 1):
            sq(mb, (sx * .8, .05, 0), (sx * .76, 2.35, .02), .075)
            for sz in (-1, 1): sq(mb, (sx * .8, .08, sz * .65), (sx * .78, 1.25, sz * .05), .045)
        sq(mb, (-.95, 2.3, .02), (.95, 2.33, .02), .07); drum(1.0)
        with mb.at(.05, 2.02, .02):
            mb.box((0, .14, 0), (.03, .28, .06), 'iron'); mb.frus((-.03, 0, 0), (.03, 0, 0), .13, .13, 8 if lod == 0 else 5, 'yugi', cap0=True, cap1=True)
        rope_line(mb, [(.05, 1.1, .1), (.05, 2.0, .15)]); rope_line(mb, [(.05, 2.0, -.11), (.07, 1.5, -.14), (.04, 1.15, -.12)])
        if lod == 0:
            for dx, dz in ((.05, -.03), (-.04, .02), (.01, .06)): rod(mb, (.04, 1.15, -.12), (.04 + dx, .98, -.12 + dz), .006, 'rope', 3)
        if lod == 0: tag_at(mb, (-.76, 1.55, .09), 0, 4, .2)
    else:                                                                                                        # one post snapped: the frame racked over, the beam's end on the floor
        sq(mb, (-.8, .05, 0), (-.7, 2.2, .1), .075); sq(mb, (-.8, .08, .65), (-.75, 1.2, .1), .045); sq(mb, (-.8, .08, -.65), (-.77, 1.15, .0), .045)
        sq(mb, (.8, .05, 0), (.86, .7, -.06), .075); sq(mb, (.95, .06, -.4), (1.5, .06, -1.6), .075)           # the stump and the fallen length
        sq(mb, (-.86, 2.18, .1), (1.1, .12, .55), .07)
        with mb.at(.1, .12, .15, yaw=-18, roll=-9): drum(0.0)
        rope_line(mb, [(.1, .2, .15), (.3, .03, .6), (.1, .03, 1.0), (-.3, .03, 1.15), (-.5, .03, .9)])
        with mb.at(.55, .03, .95, yaw=40, roll=90): mb.frus((-.03, 0, 0), (.03, 0, 0), .13, .13, 8 if lod == 0 else 5, 'yugi', cap0=True, cap1=True)
    basket(-.3, 1.15, 30)
    if lod == 0: heap(mb, -.75, 1.3, 0, 'ore', 5, .22, 31)
    stone_block(mb, .0, 0, -.2, .5, .1, 5, 7)                                                                    # the winze is choked with rock: its mouth is a low heap
    return 2.4 if not fallen else 2.2


def tag_at(mb, p, yaw, seed, w=.2, lod=0, roll=0.):
    """a stroke-fragment plate standing upright at p, facing +z (rotated by yaw), turned in its own plane by roll about its top-left nail"""
    with mb.at(p[0], p[1], p[2], yaw=yaw):
        with mb.at(-w * .4, w * .56, 0, roll=roll):
            with mb.at(w * .4, -w * .56, 0, pitch=90): tag(mb, w, seed, lod)


# ---------------------------------------------------------------- 5 옹기 도관 (벽)
def conduit(mb, lod=0, state='sag'):
    """벽 옹기 도관 (2.4 m along x at 1.45 m, wall at z=0): earthenware lengths on wooden pegs with brass bands; it once fed the lamp niches.
    state: 'sag' still up but sagging, one joint open | 'snapped' two lengths up, one hanging by its band, one broken on the floor | 'fallen' pegs only, the run in pieces below"""
    H = 1.45; pegs = (-1.05, -.2, .65)
    for i, px in enumerate(pegs):
        if state == 'fallen' and i == 1: sq(mb, (px, H - .1, 0), (px + .05, H - .2, .1), .03); continue      # a peg torn half out
        sq(mb, (px, H - .1, -.02), (px, H - .1, .2), .032)
        if lod == 0: mb.box((px, H - .03, .12), (.022, .2, .022), 'iron')
    if state == 'sag':
        ys = [0, -.03, -.09, -.05, -.02]; xs = [-1.2, -.6, 0., .6, 1.2]
        for k in range(4):
            if k == 2: pipe(mb, (xs[k] + .05, H + ys[k] - .05, .12), (xs[k + 1], H + ys[k + 1], .12), .07, lod); continue      # the joint that slipped
            pipe(mb, (xs[k], H + ys[k], .12), (xs[k + 1], H + ys[k + 1], .12), .07, lod)
        pipe(mb, (-.6, H - .03, .12), (-.6, H - .28, .12), .04, lod, band=False)                                # a feed stub for a niche
    elif state == 'snapped':
        pipe(mb, (-1.2, H, .12), (-.6, H - .03, .12), .07, lod); pipe(mb, (-.6, H - .03, .12), (-.02, H - .07, .12), .07, lod)
        pipe(mb, (.62, H - .12, .14), (.42, H - .7, .2), .07, lod, broken_end=False); mb.frus((.42, H - .7, .2), (.415, H - .72, .202), .056, .056, 6, 'soot', cap1=True)
        with mb.at(.2, .07, .55, yaw=28): pipe(mb, (-.3, 0, 0), (.3, 0, 0), .07, lod, broken_end=True)
        shards(mb, .5, 0, .45, 8 if lod == 0 else 3, .4, 3)
    else:
        with mb.at(-.7, .07, .4, yaw=-15): pipe(mb, (-.3, 0, 0), (.3, 0, 0), .07, lod, broken_end=True)
        with mb.at(.1, .0, .62, yaw=50):
            revolve(mb, 0, 0, [(0, .07), (.55, .068)], 6, 'onggi', 180, 360, inner='soot') if False else None
            with mb.at(0, .0, 0, pitch=90): revolve(mb, 0, 0, [(-.28, .07), (.28, .068)], 6 if lod == 0 else 4, 'onggi', 200, 340, inner='soot')
        with mb.at(.75, .07, .3, yaw=75): pipe(mb, (-.22, 0, 0), (.2, 0, 0), .07, lod, broken_end=True)
        shards(mb, .1, 0, .4, 10 if lod == 0 else 4, .6, 4)
        if lod == 0: mb.frus((.65, H - .14, .13), (.65, H - .09, .13), .09, .09, 6, 'yugi')                      # one band still on its peg
    return H + .1


# ---------------------------------------------------------------- 6 등 감실 (벽)
def niche(mb, lod=0, state='dead'):
    """등 감실: a lamp niche built against the rock - stone sill on a corbel, jambs, lintel, a sooted back (it reads as a recess), a brass lotus-bud holder for an ore egg,
    an earthenware feed stub from above. state: 'lit' | 'dead' (egg grey, crown gone) | 'empty' (holder tipped, shards) | 'ruin' (lintel fallen, one jamb gone)"""
    Y = 1.02
    mb.frus((0, Y - .34, .02), (0, Y - .02, .12), .05, .2, 4, 'stone'); mb.box((0, Y + .015, .13), (.6, .07, .26), 'stone', bottom=True)
    mb.box((0, Y + .3, .012), (.44, .52, .012), 'soot')
    jl = state != 'ruin'
    if lod == 0:
        for k, (px, py, r) in enumerate(((-.36, Y + .12, .1), (.37, Y + .36, .09), (-.33, Y + .55, .08), (.3, Y - .12, .09))): stone_block(mb, px, py - r * .5, .03, r, r * 1.1, 80 + k, 5)
    mb.box((-.24, Y + .3, .08), (.09, .5, .16), 'stone')
    if jl: mb.box((.24, Y + .3, .08), (.09, .5, .16), 'stone'); mb.box((0, Y + .59, .1), (.66, .09, .22), 'stone', bottom=True)
    else:
        with mb.at(.3, Y + .08, .2, roll=-64): mb.box((0, .25, 0), (.09, .5, .16), 'stone', bottom=True)
        with mb.at(.1, 0, .5, yaw=25): mb.box((0, .045, 0), (.66, .09, .22), 'stone')                          # the lintel on the floor
        shards(mb, .2, 0, .4, 5 if lod == 0 else 2, .35, 8, 'stone')
    if state != 'ruin': pipe(mb, (.1, Y + .64, .1), (.1, Y + .95, .1), .04, lod, band=lod == 0)
    seat = bud(mb, 0, Y + .05, .13) if state != 'empty' else None
    if state == 'lit': egg(mb, 0, seat, .13, .068, 'lit', lod)
    elif state == 'dead': egg(mb, 0, seat, .13, .068, 'dead', lod)
    elif state == 'ruin': egg(mb, 0, seat, .13, .068, 'stump', lod)
    else:
        with mb.at(.08, Y + .09, .15, roll=-80): bud(mb, 0, 0, 0)
        shards(mb, -.05, Y + .05, .14, 4 if lod == 0 else 2, .1, 9, 'eggdead')
    if lod == 0 and state in ('lit', 'dead'): tag_at(mb, (0, Y - .2, .135), 0, 2 if state == 'lit' else 5, .13)
    if lod == 0 and state != 'ruin':                                                                               # 창살: a brass fret over the opening and one let into each jamb - every face its own strokes; none of it glows
        s0 = dict(lit=20, dead=30, empty=40)[state]
        with mb.at(0, Y + .455, .163): fret(mb, .38, .17, s0, both=True)
        with mb.at(-.287, Y + .3, .08, yaw=-90): fret(mb, .12, .3, s0 + 1)
        with mb.at(.287, Y + .3, .08, yaw=90): fret(mb, .12, .3, s0 + 2)
    return Y + .95


# ---------------------------------------------------------------- 7 구역 패목
def marker(mb, lod=0, state='up'):
    """구역 패목: a squared stake wedged with stones, a brass plate of stroke fragments nailed to it. state: 'up' | 'hang' (plate by one nail, stake leaning) | 'down' (on the floor)"""
    if state == 'down':
        sq(mb, (-.75, .06, .1), (.7, .06, -.1), .075); sq(mb, (-.9, .0, .25), (-.82, .22, .2), .075)
        with mb.at(.35, .01, .45, yaw=35): tag(mb, .24, 6, lod)
        if lod == 0: shards(mb, -.6, 0, .3, 4, .3, 12, 'stone')
        return .3
    lean = 0 if state == 'up' else -9
    with mb.at(0, 0, 0, roll=lean):
        sq(mb, (0, -.05, 0), (0, 1.5, 0), .078); mb.frus((0, 1.5, 0), (0, 1.56, 0), .078, .03, 4, 'wood')
        tag_at(mb, (0, 1.12, .057), 0, 6, .24, lod, roll=0 if state == 'up' else -58)
    for k, (a, d) in enumerate(((.4, .16), (2.3, .15), (4.4, .17))): stone_block(mb, math.cos(a) * d, 0, math.sin(a) * d, .11, .13, 20 + k, 5)
    return 1.56


# ---------------------------------------------------------------- 8 옻칠 광석 궤
def chest(mb, lod=0, state='open'):
    """옻칠 광석 궤: a lacquer ore chest with brass mounts, emptied. state: 'open' (lid wrenched off, leaning) | 'tipped' (on its side, a few dead chips spilled)"""
    def body():
        mb.box((0, .02, 0), (.84, .04, .48), 'lacq', bottom=True)
        for sx, sz, wx, wz in ((0, 1, .84, .03), (0, -1, .84, .03), (1, 0, .03, .48), (-1, 0, .03, .48)): mb.box((sx * .405, .22, sz * .225), (wx, .4, wz), 'lacq')
        mb.box((0, .045, 0), (.76, .01, .4), 'soot')
        if lod == 0:
            for sx in (-1, 1):
                for sz in (-1, 1): mb.box((sx * .4, .22, sz * .22), (.06, .42, .06), 'yugi')
            mb.box((0, .3, .245), (.07, .12, .012), 'yugi')
    def lid(): mb.box((0, .02, 0), (.88, .04, .52), 'lacq', bottom=True); (mb.box((0, .042, 0), (.3, .006, .3), 'yugi') if lod == 0 else None)
    if state == 'open':
        body()
        with mb.at(.1, .0, -.36, yaw=6, pitch=-68): lid()
        if lod == 0: shards(mb, .2, .05, 0, 3, .2, 14, 'ore')
    else:
        with mb.at(0, .24, .0, pitch=90):
            with mb.at(0, -.21, 0): body()
        with mb.at(.3, .0, 1.0, yaw=-28): lid()
        heap(mb, .1, .62, 0, 'ore', 6 if lod == 0 else 3, .25, 15)
    return .45


# ---------------------------------------------------------------- 9 측량 기물
def survey(mb, lod=0, state='set'):
    """광맥 재는 기물: on a flat stone - a brass carpenter's square (곡척), an ink-line reel (먹통) with its line run out to a plumb bob, a brass level dish, a lacquer rule."""
    top = stone_block(mb, 0, 0, 0, .5, .3, 40, 7)
    with mb.at(-.16, top, .02, yaw=20): mb.box((.2, .007, 0), (.46, .014, .045), 'yugi'); mb.box((0, .007, .15), (.045, .014, .32), 'yugi')
    with mb.at(.13, top, -.1, yaw=-35, sc=1.35):                                                                         # 먹통
        mb.box((0, .035, 0), (.2, .07, .075), 'wood', bottom=True); mb.frus((.1, .035, 0), (.16, .03, 0), .035, .012, 4, 'wood'); rod(mb, (-.05, .05, -.045), (-.05, .05, .045), .032, 'yugi', 6, True)
        mb.box((.05, .072, 0), (.06, .004, .05), 'soot')
    if state == 'set':
        mb.frus((.0, top, .26), (.0, top + .035, .26), .06, .09, 6, 'yugi', cap0=True)
        rope_line(mb, [(.22, top + .03, -.17), (.4, top - .02, -.3), (.55, .3, -.42), (.6, .12, -.47)], .006, 3); mb.frus((.6, .0, -.47), (.6, .12, -.47), 0, .035, 5, 'yugi', cap1=True)
    else:                                                                                                         # swept off the stone
        with mb.at(.55, .03, .5, roll=70): mb.frus((0, 0, 0), (0, .035, 0), .06, .09, 6, 'yugi', cap0=True)
        rope_line(mb, [(.22, top + .03, -.17), (.42, top - .03, -.3), (.6, .01, -.5), (.95, .01, -.35), (1.0, .01, -.1)], .006, 3)
        with mb.at(1.02, .035, -.05, roll=90): mb.frus((0, -.06, 0), (0, .06, 0), 0, .035, 5, 'yugi', cap1=True)
    with mb.at(-.2, top + .0, -.22, yaw=65):
        mb.box((0, .008, 0), (.62, .016, .03), 'lacq')
        if lod == 0:
            for k in range(7): mb.box((-.27 + k * .09, .018, 0), (.006, .004, .03 if k % 3 == 0 else .018), 'yugi')
    return top + .1


# ---------------------------------------------------------------- 10 봉인 금줄
def seal(mb, lod=0, broken=False):
    """봉인: a collapsed side working barred - two crossed poles, a straw rope (금줄) with paper strips, a board carrying a brass plate of stroke fragments.
    broken = one pole down, the rope trailing, the board on the floor. Stands in front of a wall or a roof fall (wall at z = 0)."""
    def strips(pts, cut):
        for k, p in enumerate(pts[1:-1]):
            L = (.3, .16, .26, .1, .28)[k % 5] * (cut if k % 2 else 1); w = .035
            for s in (1, -1): mb.poly([(p[0] - w, p[1], p[2] + .012 * s), (p[0] + w, p[1], p[2] + .012 * s), (p[0] + w * .8, p[1] - L, p[2] + .03 * s), (p[0] - w * .6, p[1] - L * .92, p[2] + .03 * s)][::s], 'paper')
    def board(seed):
        mb.box((0, .012, 0), (.34, .024, .52), 'wood', bottom=True)
        with mb.at(0, .024, 0): tag(mb, .2, seed, lod)
    sq(mb, (-1.15, 0, .5), (1.0, 2.05, .12), .055)
    if not broken:
        sq(mb, (1.15, 0, .55), (-1.0, 2.0, .16), .055); r = sag((-1.2, 1.5, .4), (1.2, 1.42, .42), .22, 6 if lod == 0 else 3); rope_line(mb, r, .02); strips(r, 1.)
        with mb.at(.02, 1.0, .43, pitch=84, yaw=0, roll=0):
            with mb.at(0, 0, 0, yaw=7): board(7)
    else:
        sq(mb, (1.25, .05, .6), (-.9, .05, 1.35), .055)
        r = sag((-1.2, 1.5, .4), (-.15, .9, .33), .1, 3) + [(.2, .03, .7), (.75, .03, .95), (1.2, .03, .8)]; rope_line(mb, r, .02); strips(r[:4], .5)
        with mb.at(.35, .0, 1.1, yaw=-25): board(7)
        if lod == 0:
            for k, (px, pz) in enumerate(((.8, .7), (1.05, 1.1), (.1, 1.4))): mb.poly([(px, .008, pz), (px + .07, .008, pz + .02), (px + .09, .008, pz + .3), (px + .01, .008, pz + .27)][::-1], 'paper', (0, 1, 0))
    for k, (px, pz) in enumerate(((-1.15, .5), (1.15, .55))): stone_block(mb, px + .12, 0, pz + .1, .14, .16, 60 + k, 5)
    return 2.05


# ---------------------------------------------------------------- 11 셈 판
def tally(mb, lod=0, fallen=False):
    """셈 판: the tally board of baskets carried out - rows of notches, the last row unfinished; counting sticks in a tube at its foot. Leans on the wall (z = 0)."""
    def face(rows, last):
        mb.box((0, .015, 0), (.34, .03, 1.2), 'wood', bottom=True)
        if lod == 0:
            for r in range(rows):
                z0 = -.46 + r * .14
                for c in range(5 if r < rows - 1 else last): x0 = -.11 + c * .055; mb.poly([(x0 - .007, .0315, z0 + .045), (x0 + .007, .0315, z0 + .045), (x0 + .012, .0315, z0 - .045), (x0 - .002, .0315, z0 - .045)], 'soot', (0, 1, 0))
                if r < rows - 1: mb.poly([(-.15, .032, z0 + .03), (-.15, .032, z0 + .042), (.14, .032, z0 - .03), (.14, .032, z0 - .042)], 'soot', (0, 1, 0))
            mb.frus((0, .03, -.56), (0, .05, -.56), .018, .008, 5, 'yugi', cap1=True)
        else: mb.box((0, .031, -.1), (.26, .002, .8), 'soot')
    if not fallen:
        with mb.at(0, 0, .36, pitch=74):
            with mb.at(0, 0, -.6): face(7, 2)
        mb.frus((.32, 0, .3), (.32, .22, .3), .045, .05, 6, 'wood', cap0=True)
        if lod == 0:
            for k in range(5): rod(mb, (.32 + (k - 2) * .012, .05, .3), (.3 + (k - 2) * .035, .42, .3 + (k % 2) * .03), .005, 'wood', 3)
    else:
        with mb.at(-.1, 0, .75, yaw=18): face(4, 5)
        with mb.at(.25, 0, .95, yaw=31):
            with mb.at(0, 0, .05): mb.box((0, .015, .9), (.34, .03, .5), 'wood', bottom=True)
        with mb.at(.55, .05, .4, yaw=40, roll=90): mb.frus((0, -.11, 0), (0, .11, 0), .045, .05, 6, 'wood', cap0=True)
        if lod == 0:
            rng = np.random.default_rng(77)
            for k in range(7): a = rng.uniform(0, 3.14); px, pz = .5 + rng.uniform(-.3, .3), .65 + rng.uniform(-.25, .25); rod(mb, (px, .006, pz), (px + math.cos(a) * .36, .006, pz + math.sin(a) * .36), .005, 'wood', 3)
    return 1.2


# ---------------------------------------------------------------- 12 알 지게
def carrier(mb, lod=0, state='down'):
    """알 지게: an A-frame back carrier (지게) fitted with a padded lacquer cradle for ore eggs. state: 'down' (fallen on its side, one egg rolled out and split) | 'lean' (against the wall, a dead egg still in it)"""
    def frame():
        for sx in (-1, 1):
            sq(mb, (sx * .3, 0, 0), (sx * .12, 1.25, 0), .035); sq(mb, (sx * .25, .38, .0), (sx * .27, .5, .42), .03)
        for y, w in ((.3, .26), (.62, .21), (.95, .16)): sq(mb, (-w, y, 0), (w, y, 0), .022)
        mb.box((0, .62, -.04), (.3, .5, .04), 'rope')                                                            # the straw back pad
        rope_line(mb, [(-.2, .95, -.03), (-.26, .6, -.12), (-.22, .32, -.03)], .014, 3); rope_line(mb, [(.2, .95, -.03), (.26, .6, -.12), (.22, .32, -.03)], .014, 3)
        mb.box((0, .47, .22), (.5, .04, .36), 'lacq', bottom=True)
        for sx, sz, wx, wz in ((0, 1, .5, .025), (1, 0, .025, .36), (-1, 0, .025, .36)): mb.box((sx * .24, .55, .22 + sz * .17), (wx, .14, wz), 'lacq')
        if lod == 0:
            for sx in (-1, 1): mb.box((sx * .245, .55, .395), (.03, .15, .03), 'yugi')
    if state == 'lean':
        with mb.at(0, 0, .42, pitch=-17): frame(); egg(mb, -.1, .49, .22, .075, 'dead', lod); bud(mb, .12, .49, .22)
    else:
        with mb.at(-.2, .3, .2, yaw=20, roll=84): frame(); egg(mb, .12, .49, .22, .075, 'stump', lod)
        egg(mb, .75, 0, .55, .075, 'split', lod, yaw=50)
        if lod == 0: shards(mb, .6, 0, .4, 4, .25, 31, 'eggdead')
    return 1.25


# ---------------------------------------------------------------- 13 가루 독
def jars(mb, lod=0, state='mixed'):
    """가루 독: earthenware powder jars with brass lids and hoops. state: 'mixed' (one whole, one cracked open and spilled, one on its side) | 'broken' (shards and one half)"""
    n = 8 if lod == 0 else 5; prof = [(h * .8, r * .8) for h, r in JAR]
    if state == 'mixed': jar(mb, -.45, -.1, 0, .8, n, hoops=(.5,) if lod == 0 else ())
    revolve(mb, .25, .0, prof[:4], n, 'onggi', 40, 290, inner='soot'); disc(mb, .45, .006, .4, .5, 'powder', seed=9); shards(mb, .5, 0, .2, 7 if lod == 0 else 3, .35, 41)
    if lod == 0: mb.frus((.85, .0, .55), (.85, .03, .55), .16, .145, n, 'yugi', cap1=True)                      # a brass lid on the floor
    if state == 'mixed':
        with mb.at(-.25, .2, .62, yaw=115, roll=90): mb.lathe(0, 0, [(h * .62 - .25, r * .62) for h, r in JAR], n, 'onggi', cap=False); mb.frus((0, .283, 0), (0, .28, 0), .1, 0, n, 'soot')
    else: shards(mb, -.3, 0, .3, 9 if lod == 0 else 4, .45, 42)
    return .8


KIT = {
    'sort_table': lambda mb, lod: sort_table(mb, lod, False), 'sort_table_broken': lambda mb, lod: sort_table(mb, lod, True),
    'grinder': lambda mb, lod: grinder(mb, lod, False), 'grinder_broken': lambda mb, lod: grinder(mb, lod, True),
    'hearth': lambda mb, lod: hearth(mb, lod, False), 'hearth_broken': lambda mb, lod: hearth(mb, lod, True),
    'hoist': lambda mb, lod: hoist(mb, lod, False), 'hoist_fallen': lambda mb, lod: hoist(mb, lod, True),
    'conduit_sag': lambda mb, lod: conduit(mb, lod, 'sag'), 'conduit_snapped': lambda mb, lod: conduit(mb, lod, 'snapped'), 'conduit_fallen': lambda mb, lod: conduit(mb, lod, 'fallen'),
    'niche_lit': lambda mb, lod: niche(mb, lod, 'lit'), 'niche_dead': lambda mb, lod: niche(mb, lod, 'dead'), 'niche_empty': lambda mb, lod: niche(mb, lod, 'empty'), 'niche_ruin': lambda mb, lod: niche(mb, lod, 'ruin'),
    'marker_up': lambda mb, lod: marker(mb, lod, 'up'), 'marker_hang': lambda mb, lod: marker(mb, lod, 'hang'), 'marker_down': lambda mb, lod: marker(mb, lod, 'down'),
    'chest_open': lambda mb, lod: chest(mb, lod, 'open'), 'chest_tipped': lambda mb, lod: chest(mb, lod, 'tipped'),
    'survey_set': lambda mb, lod: survey(mb, lod, 'set'), 'survey_swept': lambda mb, lod: survey(mb, lod, 'swept'),
    'seal': lambda mb, lod: seal(mb, lod, False), 'seal_broken': lambda mb, lod: seal(mb, lod, True),
    'tally': lambda mb, lod: tally(mb, lod, False), 'tally_fallen': lambda mb, lod: tally(mb, lod, True),
    'carrier_down': lambda mb, lod: carrier(mb, lod, 'down'), 'carrier_lean': lambda mb, lod: carrier(mb, lod, 'lean'),
    'jars_mixed': lambda mb, lod: jars(mb, lod, 'mixed'), 'jars_broken': lambda mb, lod: jars(mb, lod, 'broken'),
    # 2026-10-13, after the Pinterest pass (Tools/Unity/Stage308_minearcane/PINTEREST_seen.md)
    'assay_stand': lambda mb, lod: assay(mb, lod), 'ding_cold': lambda mb, lod: ding(mb, lod), 'scroll_open': lambda mb, lod: scroll(mb, lod),
    'sort_table_scroll': lambda mb, lod: sort_table(mb, lod, True, True), 'hearth_ding': lambda mb, lod: hearth(mb, lod, True, True),
}
LIT = {'niche_lit': 1}
SEAT = {k: 'wall' for k in KIT if k.split('_')[0] in ('conduit', 'niche', 'tally', 'seal')}; SEAT.update({'carrier_lean': 'wall', 'tally_fallen': 'wall'})
BROKEN = {k: 0 for k in ('sort_table', 'grinder', 'hearth', 'hoist', 'niche_lit', 'marker_up', 'survey_set', 'seal', 'tally', 'assay_stand', 'ding_cold', 'scroll_open')}
NAMES = {'sort': '선광 탁자(유기 손저울 · 옻칠 함지)', 'grinder': '마석 맷돌(유기 띠 · 가루 부채꼴)', 'hearth': '식은 화덕과 손풀무', 'hoist': '녹로 틀(유기 도르래 · 해진 밧줄)', 'conduit': '벽 옹기 도관',
         'niche': '등 감실(놋쇠 연봉 받침 · 마석 알)', 'marker': '구역 패목(획 조각 유기 패)', 'chest': '옻칠 광석 궤', 'survey': '광맥 재는 기물(곡척 · 먹통 · 수준 접시)', 'seal': '봉인 금줄과 부적 판',
         'tally': '셈 판', 'carrier': '알 지게', 'jars': '가루 독', 'assay': '감정대(삼발 놋쇠 받침 · 죽은 알 · 획 조각 유기 원반)', 'ding': '청동 솥형 도가니(식음)', 'scroll': '측량 도면 두루마리(먹선 격자 · 획 조각)'}


def build(kid, lod):
    mb = MB(); KIT[kid](mb, lod); out = []
    for mat, b in mb.sub.items():
        assert mat in KEYS, (kid, mat)
        if not b['t']: continue
        v = np.array(b['v'], float); n = np.array(b['n'], float); ln = np.linalg.norm(n, axis=1); assert (ln > .5).all(), (kid, mat); assert np.isfinite(v).all(), kid
        out.append(dict(mat=mat, v=[round(float(x), 4) for x in v.ravel()], n=[round(float(x), 3) for x in n.ravel()], t=[int(i) for i in b['t']]))
    return out, mb


def bounds(subs):
    av = np.vstack([np.array(s['v']).reshape(-1, 3) for s in subs]); return av.min(0), av.max(0)


def export(root):
    out = root / 'Art/World/PackLite308/arcane_mine'; out.mkdir(parents=True, exist_ok=True); kit = []; man = []
    for kid in KIT:
        lods = []
        for lod in (0, 1):
            subs, mb = build(kid, lod); lods.append(dict(subs=subs)); lines = ['# %s LOD%d  (#308 mine arcane kit, metres, +y up, origin on the floor)' % (kid, lod), 'o %s_LOD%d' % (kid, lod)]; base = 1
            for s in subs:
                v = np.array(s['v']).reshape(-1, 3); n = np.array(s['n']).reshape(-1, 3); t = np.array(s['t']).reshape(-1, 3) + base
                lines += ['v %.4f %.4f %.4f' % tuple(p) for p in v] + ['vn %.3f %.3f %.3f' % tuple(p) for p in n] + ['usemtl ' + s['mat']] + ['f %d//%d %d//%d %d//%d' % (a, a, b, b, c, c) for a, b, c in t]; base += len(v)
            (out / ('%s_LOD%d.obj' % (kid, lod))).write_text('\n'.join(lines) + '\n', encoding='utf-8')
        lo, hi = bounds(lods[0]['subs']); t0 = sum(len(s['t']) // 3 for s in lods[0]['subs']); t1 = sum(len(s['t']) // 3 for s in lods[1]['subs'])
        kit.append(dict(id=kid, lit=LIT.get(kid, 0), seat=SEAT.get(kid, 'floor'), lods=lods))
        man.append(dict(id=kid, name=NAMES[kid.split('_')[0]], variant='intact' if kid in BROKEN else 'broken', seat=SEAT.get(kid, 'floor'), lit=LIT.get(kid, 0), tris=[t0, t1], size=[round(float(x), 2) for x in hi - lo],
                        min=[round(float(x), 2) for x in lo], max=[round(float(x), 2) for x in hi], materials=[s['mat'] for s in lods[0]['subs']]))
    (out / 'manifest.json').write_text(json.dumps(dict(version='308.minearcane.2', keys=KEYS, objects=man), ensure_ascii=False, indent=1), encoding='utf-8')
    (root / 'Tools/Unity/Stage308_minearcane/mine_arcane308_kit.json').write_text(json.dumps(dict(version='308.minearcane.2', kit=kit), ensure_ascii=False, separators=(',', ':')), encoding='utf-8')
    return man


def sheet(out, ids=None, per=4, az_deg=35.):
    """CPU contact sheets (painter's algorithm, PIL)"""
    from PIL import Image, ImageDraw
    out.mkdir(parents=True, exist_ok=True); ids = ids or list(KIT); L = np.array([.45, .8, .4]); L /= np.linalg.norm(L); W, H = 640, 560
    az, el = math.radians(az_deg), math.radians(20); fw = np.array([-math.sin(az) * math.cos(el), -math.sin(el), -math.cos(az) * math.cos(el)]); rt = np.cross(fw, [0, 1, 0]); rt /= np.linalg.norm(rt); up = np.cross(rt, fw)
    for s0 in range(0, len(ids), per):
        img = Image.new('RGB', (W * 2, H * 2), (96, 92, 86)); dr = ImageDraw.Draw(img)
        for i, kid in enumerate(ids[s0:s0 + per]):
            subs, mb = build(kid, 0); ox, oy = (i % 2) * W, (i // 2) * H; tris = []
            lo, hi = bounds(subs)
            if SEAT.get(kid) == 'wall':      # show the wall it stands against
                subs = subs + [dict(mat='_wall', v=[lo[0] - .3, 0, -.01, hi[0] + .3, 0, -.01, hi[0] + .3, hi[1] + .3, -.01, lo[0] - .3, hi[1] + .3, -.01], n=[0, 0, 1] * 4, t=[0, 1, 2, 0, 2, 3])]
            subs = subs + [dict(mat='_floor', v=[lo[0] - .3, -.002, -.02 if SEAT.get(kid) == 'wall' else lo[2] - .3, hi[0] + .3, -.002, -.02 if SEAT.get(kid) == 'wall' else lo[2] - .3, hi[0] + .3, -.002, hi[2] + .3, lo[0] - .3, -.002, hi[2] + .3], n=[0, 1, 0] * 4, t=[0, 2, 1, 0, 3, 2])]
            for sb in subs:
                v = np.array(sb['v']).reshape(-1, 3); nn = np.array(sb['n']).reshape(-1, 3); t = np.array(sb['t']).reshape(-1, 3); base = np.array(COL.get(sb['mat'], (.3, .29, .28) if sb['mat'] == '_wall' else (.36, .35, .33)))
                for tr in t:
                    P = v[tr]; fn = np.cross(P[1] - P[0], P[2] - P[0])
                    if fn @ fw > 0: continue
                    sh = float(np.clip(nn[tr].mean(0) @ L, 0, 1)) * .7 + .38; c = base * 1.6 if sb['mat'] == 'egglit' else np.clip(base * sh * 1.5, 0, 1); bias = .5 if sb['mat'].startswith('_') else 0
                    tris.append((float(P.mean(0) @ fw) + bias, P, tuple(int(min(1, x) * 255) for x in c)))
            av = np.vstack([p for _, p, _ in tris]); l2, h2 = av.min(0), av.max(0); c0 = (l2 + h2) / 2; r = np.linalg.norm(h2 - l2) / 2 * 1.02; k = (min(W, H) / 2 - 26) / r
            for _, P, c in sorted(tris, key=lambda q: -q[0]):
                dr.polygon([(ox + W / 2 + float((p - c0) @ rt) * k, oy + H / 2 + 12 - float((p - c0) @ up) * k) for p in P], fill=c)
            dr.text((ox + 8, oy + 6), '%s  %.2f x %.2f x %.2f m  %d / %d tris  %s' % (kid, hi[0] - lo[0], hi[1] - lo[1], hi[2] - lo[2], mb.tris(), build(kid, 1)[1].tris(), SEAT.get(kid, 'floor')), fill=(240, 240, 240))
        img.save(out / ('arcane_mine_%d.png' % (s0 // per)))


if __name__ == '__main__':
    root = Path(__file__).resolve().parents[2]; cmd = sys.argv[1] if len(sys.argv) > 1 else ''
    if cmd == 'sheet': sheet(root / 'Art/World/PackLite308/_sheets/arcane_mine', sys.argv[2:] or None)
    if cmd == 'export': export(root)
    tot = 0
    for k in KIT: a, b = build(k, 0)[1].tris(), build(k, 1)[1].tris(); tot += a; print('%-18s LOD0 %4d  LOD1 %4d  %s%s' % (k, a, b, SEAT.get(k, 'floor'), '  LIT' if k in LIT else ''))
    print('sum LOD0', tot)
