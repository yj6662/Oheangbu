"""NB20 (D308-150 V6): Blender authoring of the no-batchim 20 ink elements.  Run headless (one Blender at a time, nothing else GPU-heavy):
  "C:/Program Files/Blender Foundation/Blender 5.2/blender.exe" -b --factory-startup --python Tools/Art/SpellVFX308/nb20/nb20_blender_strokes.py -- [--only head,tail] [--bodies Wood,Fire] [--scale 1]

What it builds (all top-down orthographic, Eevee, film transparent, view transform Raw so the PNG values are the authored numbers):
  every element = a BRUSH of 40 - 90 bristles laid along a centre path. Each bristle is its own flat ribbon (a quad strip): it carries ink that runs out along the stroke
  (dry-brush = the ribbon breaks where its load drops under the paper's tooth), it splits from its neighbours into clumps (hollow strands), it presses wide and wet at the
  entry (a METABALL ink pool merges with the bristle roots: the wet blunt head with its darker pooled rim, reference VFX-REF-INK-BURST-308 section 3) and ends in the BODY tip
  (plan C: wood torn fibres / fire saw teeth + burnt holes / earth stacked plates / metal straight cut + transverse breaks / water open hook + hollow streams).
  Needle lines (very thin straight ribbons) and droplets (metaball splats with satellites, thrown ballistically) are added where the element calls for them.
Channels written (RGBA 16-bit PNG):
  R = ink tone      0 = wet pooled black (L*~10) .. 1 = driest grey ink (L*~30); a Noise texture in the shader mottles it (paper grain)
  G = pigment mask  1 where the bristle is dry / at a fibre end / a plate lip / an outer bristle: the body hue (L* 60) lives there, about 30 % of the drawn pixels
  B = reveal order  0 = the first ink laid (the press) .. 1 = the last (the tip and the needle lines): the engine reveals the element by this, never a soft fade
  A = coverage      ink present
After Effects (nb20_ae_build.jsx) roughens / bleeds the coverage and makes the drying order; nb20_pack.py packs the atlases.
Output: Art/SpellVFX308/Vol2Vfx/nb20/src/blender/<element>_<body>.png  + elements.json (sizes in metres, for the packer and the recipes)."""
import bpy, bmesh, math, json, os, random, sys
from mathutils import Vector

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", "..", ".."))
OUT = os.path.join(REPO, "Art", "SpellVFX308", "Vol2Vfx", "nb20", "src", "blender")
BODIES = ["Wood", "Fire", "Earth", "Metal", "Water"]

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
def arg(name, default=None):
    return argv[argv.index(name) + 1] if name in argv else default
ONLY = set(arg("--only", "").split(",")) - {""}
ONLY_BODIES = [b for b in arg("--bodies", ",".join(BODIES)).split(",") if b]
RES = int(2048 * float(arg("--scale", "1")))

# ---------------------------------------------------------------------------------------------------------------------------------
# element table: length L (m, along x = the stroke's direction, x = 0 is the pressed WET start), height W (m, across), path kind, brush
# ---------------------------------------------------------------------------------------------------------------------------------
ELEMENTS = {
    # P0 travelling head: a short PRESSED stroke, wet blunt front (x = 0 leads), dry split rear (its body tip trails); hollow middle (mine)
    # r6: a COMET, not a rectangle - a long wet press (the pool is most of it) narrowing into 3 strands of unequal length (decay fast, split early, wide hollow between)
    "head":      dict(span=(0.07, 0.95), L=0.66, W=0.40, path="line", width=0.26, bristles=48, press=0.42, decay=2.4, split=0.36, clumps=3, tip=0.70, needles=0, drops=0, hollow=0.85, taper=True),
    # P0 dry-brush tail behind the head: thin, uneven 2 - 3 strands, no parallel rails
    "tail":      dict(L=1.50, W=0.36, path="tail", width=0.22, bristles=40, press=0.0, decay=1.9, split=0.10, clumps=3, tip=0.66, needles=0, drops=0, hollow=0.7),
    # contact main stroke: a real-size short slash, wet press, dry body tip, needle lines out of both ends, 2 droplets thrown off
    "contact":   dict(span=(0.05, 0.74), L=1.20, W=0.50, path="slash", width=0.30, bristles=80, press=0.22, decay=1.25, split=0.40, clumps=4, tip=0.70, needles=5, drops=3, hollow=0.45),
    "supportA":  dict(span=(0.05, 0.82), L=0.80, W=0.26, path="bow", width=0.16, bristles=44, press=0.14, decay=1.6, split=0.30, clumps=3, tip=0.66, needles=2, drops=0, hollow=0.5),
    "supportB":  dict(L=0.66, W=0.26, path="hook", width=0.15, bristles=40, press=0.16, decay=1.7, split=0.28, clumps=3, tip=0.64, needles=0, drops=1, hollow=0.5),
    # F0: the fan's radial edge stroke (flat on the ground, apex at x = 0 .. 4.4 m) and its far arc (R 5 m, chord 4 m)
    "fanRadial": dict(L=4.40, W=0.50, path="line", width=0.30, bristles=70, press=0.05, decay=0.9, split=0.20, clumps=4, tip=0.80, needles=0, drops=0, hollow=0.6),
    "fanArc":    dict(L=4.40, W=1.00, path="arc5", width=0.30, bristles=70, press=0.06, decay=0.9, split=0.20, clumps=4, tip=0.78, needles=0, drops=0, hollow=0.6),
    # R0: one 60 degree piece of the band (R 2 m, the stroke 0.36 m wide inside the 0.6 m band) and the standing stroke (1.1 m, drawn upright in the engine)
    "ringArc":   dict(L=2.24, W=0.86, path="arc2", width=0.36, bristles=72, press=0.10, decay=1.0, split=0.25, clumps=4, tip=0.76, needles=0, drops=0, hollow=0.55),
    "standing":  dict(span=(0.05, 0.86), L=1.16, W=0.34, path="line", width=0.24, bristles=56, press=0.18, decay=1.3, split=0.32, clumps=3, tip=0.70, needles=2, drops=0, hollow=0.5),
    # A0: the short ink pulse along the brush shaft / sleeve (<= 0.45 m), thin and dry with one wet bead
    "pulse":     dict(L=0.48, W=0.14, path="line", width=0.07, bristles=22, press=0.20, decay=1.8, split=0.30, clumps=2, tip=0.60, needles=0, drops=0, hollow=0.4),
}
SHARED = {
    # droplets in flight: a round drop with a short tail + satellites (reference: big few / medium / many tiny); 4 variants
    "fleck0": dict(L=0.30, W=0.12), "fleck1": dict(L=0.30, W=0.12), "fleck2": dict(L=0.30, W=0.12), "fleck3": dict(L=0.30, W=0.12),
    # ignition at the brush tip (reference 4.1): a small ink blot + 5 needle lines in a star, no light
    "ignite": dict(L=0.60, W=0.60),
}


def smooth(a, b, x):
    t = max(0.0, min(1.0, (x - a) / (b - a) if b != a else 0.0))
    return t * t * (3 - 2 * t)


class Noise1:
    """smooth 1-D value noise (deterministic)"""
    def __init__(self, rng, n=64):
        self.v = [rng.random() for _ in range(n)]
    def __call__(self, x):
        n = len(self.v); i = math.floor(x); f = x - i
        a = self.v[i % n]; b = self.v[(i + 1) % n]
        f = f * f * (3 - 2 * f)
        return a + (b - a) * f


class Noise2:
    """smooth 2-D value noise on a 32 x 32 lattice (deterministic)"""
    def __init__(self, rng, n=32):
        self.n = n; self.v = [[rng.random() for _ in range(n)] for _ in range(n)]
    def __call__(self, x, y):
        n = self.n; i = math.floor(x); j = math.floor(y); fx = x - i; fy = y - j
        fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy)
        a = self.v[i % n][j % n]; b = self.v[(i + 1) % n][j % n]; c = self.v[i % n][(j + 1) % n]; d = self.v[(i + 1) % n][(j + 1) % n]
        return (a + (b - a) * fx) * (1 - fy) + (c + (d - c) * fx) * fy


# ---------------------------------------------------------------------------------------------------------------------------------
# centre paths: t in 0..1 -> (x, y) metres
# ---------------------------------------------------------------------------------------------------------------------------------
def make_path(kind, L, W, body, span=None):
    m = 0.06 * L if kind not in ("arc5", "arc2") else 0.03 * L
    x0, x1 = m, L - m
    if span is not None:
        x0, x1 = span[0] * L, span[1] * L
    if kind == "line":
        f = lambda t: (x0 + (x1 - x0) * t, 0.0)
    elif kind == "tail":
        f = lambda t: (x0 + (x1 - x0) * t, 0.03 * math.sin(t * 2.1))
    elif kind == "slash":     # slight S: the brush enters with a small reverse hook (역입) then runs
        f = lambda t: (x0 + (x1 - x0) * t, 0.05 * math.sin(t * math.pi * 1.2 + 0.4) - 0.02)
    elif kind == "bow":
        f = lambda t: (x0 + (x1 - x0) * t, 0.06 * math.sin(t * math.pi))
    elif kind == "hook":
        f = lambda t: (x0 + (x1 - x0) * t, 0.07 * (t ** 2) - 0.02)
    elif kind in ("arc5", "arc2"):
        R = 5.0 if kind == "arc5" else 2.0
        half = math.asin(min(0.999, (x1 - x0) * 0.5 / R))
        sag = R - R * math.cos(half)
        cx = L * 0.5
        def f(t, R=R, half=half, sag=sag, cx=cx):
            a = -half + 2 * half * t
            return (cx + R * math.sin(a), R * math.cos(a) - R + sag * 0.5)
    else:
        raise ValueError(kind)
    # water: the end bends into an OPEN hook (plan C): after t = 0.80 the path turns 150 degrees on a tight radius
    if body == "Water":
        base = f
        def fw(t, base=base):
            t0 = 0.80
            if t <= t0:
                return base(t)
            p0 = Vector(base(t0)); p1 = Vector(base(t0 - 0.01))
            d = (p0 - p1).normalized()
            seg = (Vector(base(1.0)) - p0).length          # the length the hook replaces
            u = (t - t0) / (1 - t0)
            ang = math.radians(150) * (u ** 1.2)
            r = seg / math.radians(150) * 1.05
            n = Vector((-d.y, d.x))
            # arc of radius r turning toward +n
            return tuple(p0 + d * (r * math.sin(ang)) + n * (r * (1 - math.cos(ang))))
        return fw
    return f


def tangent(f, t):
    a = Vector(f(max(0.0, t - 0.003))); b = Vector(f(min(1.0, t + 0.003)))
    d = b - a
    return d.normalized() if d.length > 1e-9 else Vector((1, 0))


# ---------------------------------------------------------------------------------------------------------------------------------
# the brush
# ---------------------------------------------------------------------------------------------------------------------------------
class Ribbon:
    def __init__(self):
        self.verts, self.faces, self.cols = [], [], []

    def strip(self, pts, widths, cols, z):
        """pts: list of (x, y); widths: full widths; cols: (tone, pig, reveal, 1) per point"""
        n = len(pts)
        if n < 2:
            return
        base = len(self.verts)
        for i in range(n):
            p = Vector(pts[i])
            a = Vector(pts[max(0, i - 1)]); b = Vector(pts[min(n - 1, i + 1)])
            d = (b - a)
            d = d.normalized() if d.length > 1e-9 else Vector((1, 0))
            nrm = Vector((-d.y, d.x)) * (widths[i] * 0.5)
            self.verts.append((p.x + nrm.x, p.y + nrm.y, z)); self.cols.append(cols[i])
            self.verts.append((p.x - nrm.x, p.y - nrm.y, z)); self.cols.append(cols[i])
        for i in range(n - 1):
            v = base + i * 2
            self.faces.append((v, v + 1, v + 3, v + 2))

    def polygon(self, pts, col, z):
        base = len(self.verts)
        cx = sum(p[0] for p in pts) / len(pts); cy = sum(p[1] for p in pts) / len(pts)
        self.verts.append((cx, cy, z)); self.cols.append(col)
        for p in pts:
            self.verts.append((p[0], p[1], z)); self.cols.append(col)
        n = len(pts)
        for i in range(n):
            self.faces.append((base, base + 1 + i, base + 1 + (i + 1) % n))


def body_tip_end(body, r, rng, spec, i, nb):
    """where bristle i (lateral rank r in -1..1) stops (t), plan C body tips"""
    tip = spec["tip"]
    if body == "Wood":
        # torn fibres: three groups of lengths 1 : .7 : .4 of the tip span, plus a few long fibres that run past the rest
        grp = [1.0, 0.7, 0.4][int((r * 0.5 + 0.5) * 2.999 + rng.uniform(-0.25, 0.25)) % 3]        # r4: three clean fingers, not mixed
        e = tip + (0.985 - tip) * grp * rng.uniform(0.86, 1.0)
        return min(0.995, e)
    if body == "Fire":
        # saw teeth: 2 - 3 teeth across the width (a triangle wave), each tooth a sharp point
        k = 3.0                                                     # r4: three big teeth
        tri = abs(((r * 0.5 + 0.5) * k) % 1.0 - 0.5) * 2.0       # 0 at a tooth point, 1 between teeth
        return tip + (0.985 - tip) * (1.0 - 0.85 * tri) * rng.uniform(0.95, 1.0)
    if body == "Earth":
        return 0.975                                               # the bristles run on through the three stacked plates
    if body == "Metal":
        return 0.97 - 0.14 * (r * 0.5 + 0.5)                       # one straight oblique cut, every bristle on it
    # water: the bristles follow the hook to the end, the streams end unevenly a little
    return 0.97 - 0.05 * rng.random()


def brush(rib, spec, body, seed, zbase=0.0):
    """ONE brush stroke: many thin bristles that overlap into a solid loaded body, run dry into long streaks (비백), split into a few hollow clumps (mine), end in the body tip."""
    rng = random.Random(seed)
    L, W = spec["L"], spec["W"]
    f = make_path(spec["path"], L, W, body, spec.get("span"))
    nb = int(spec["bristles"] * 2.6)
    width = spec["width"]
    M = 260
    tip = spec["tip"]
    ranks = sorted(rng.uniform(-1, 1) for _ in range(nb))
    clumps = spec["clumps"] + (1 if body == "Water" else 0)
    # clump centres across the width: uneven, never equally spaced rails
    cc = sorted(rng.uniform(-0.85, 0.85) for _ in range(clumps))
    for k in range(1, clumps):
        if cc[k] - cc[k - 1] < 0.30:
            cc[k] = cc[k - 1] + 0.30
    span = cc[-1] - cc[0]
    cc = [(-0.78 + 1.56 * (c - cc[0]) / span) if span > 1e-6 else 0.0 for c in cc]
    holes = []
    if body == "Fire" and not spec["path"].startswith("arc"):
        for h in range(2 if L > 0.6 else 1):
            holes.append((rng.uniform(0.40, tip - 0.06), rng.uniform(-0.3, 0.3), rng.uniform(0.22, 0.30)))
    breaks = [(tip + 0.03, 0.016), (tip + 0.13, 0.012)] if body == "Metal" else []
    fray = {"Wood": 1.0, "Fire": 0.8, "Earth": 0.35, "Metal": 0.15, "Water": 0.6}[body]
    split0 = spec["split"]; hollow = spec["hollow"]
    # earth: three stacked plates over the tip - the stroke narrows in steps, a seam across it at each step, the plate's front lip is ink-dark
    plate_t = [tip - 0.04 + k * (0.975 - tip + 0.04) / 3.0 for k in range(3)] if body == "Earth" else []
    hole_centres = []
    for (ht, hr, hrad) in holes:
        cp = Vector(f(ht)); dn = tangent(f, ht); nn = Vector((-dn.y, dn.x))
        hole_centres.append((cp + nn * (hr * width * 0.5), hrad * width))
    tooth2 = Noise2(random.Random(seed * 5 + 1))
    for i in range(nb):
        r = ranks[i]
        ci = min(range(clumps), key=lambda k: abs(cc[k] - r))
        nl = Noise1(random.Random(seed * 31 + i)); nw = Noise1(random.Random(seed * 17 + i)); nt_ = Noise1(random.Random(seed * 7 + i))
        rb = width / nb * rng.uniform(3.2, 6.4)
        load0 = rng.uniform(0.8, 1.05) * (1.0 - 0.30 * abs(r) ** 2)
        decay = spec["decay"] * rng.uniform(0.6, 1.5) * (1.0 + 0.7 * abs(r) ** 1.5)
        pig_thr = rng.uniform(0.42, 0.80)                            # each bristle turns from ink to dry pigment at its own dryness
        t_start = (spec["press"] * 0.55 * (abs(r) ** 1.5) * rng.uniform(0.5, 1.0)) if spec["press"] > 0 else rng.uniform(0.0, 0.05) * abs(r)
        t_end = body_tip_end(body, r, rng, spec, i, nb)
        if body not in ("Metal", "Earth"):
            t_end -= rng.uniform(0.0, 0.05) * (abs(r) ** 2)
        pts, wid, col = [], [], []

        def flush(lift):
            n = len(pts)
            if n >= 2:
                # the bristle touches down quickly and lifts off slowly (a pointed dry end); a cut (metal) or a plate seam ends square
                for q in range(n):
                    a = min(1.0, (q + 1) / 2.0)
                    b = min(1.0, (n - q) / (lift + 0.001)) if lift > 0 else 1.0
                    wid[q] *= a * (b ** 0.8)
                rib.strip(list(pts), list(wid), list(col), zbase + 0.00002 * (i % 50))
            pts.clear(); wid.clear(); col.clear()

        for s in range(M + 1):
            t = s / M
            if t < t_start:
                continue
            if t > t_end:
                flush(0 if body in ("Metal",) else 7); break
            in_press = t < spec["press"]
            load = load0 * math.exp(-decay * max(0.0, t - spec["press"] * 0.7)) + 0.16 * (nl(t * 7.0 + i * 0.37) - 0.5)
            # the paper's tooth: LONG streaks along the stroke (low frequency along, a different phase per bristle)
            # NB20 r2: the skip is shared by NEIGHBOURING bristles (2-D noise over stroke length x rank) so the paper shows through in a few BOLD white streaks
            # (>= 15 - 40 texels of the packed cell: still a streak at the ~150 px the element covers in game), not hair-fine lines that turn to noise in the mips
            tooth = 0.04 + 0.52 * tooth2(t * 2.6, r * 2.4) ** 1.7 + 0.06 * (nl(t * 3.0 + 50 + i * 0.61) - 0.5)
            # the body tip must READ (plan C, a shape): earth plates are pressed dabs, fire teeth carry ink to their points
            if plate_t and t > plate_t[0]:
                load = max(load, 0.80)
            elif body == "Fire" and t > tip - 0.06:
                load = max(load, 0.55 + 0.1 * nl(t * 13.0))
            elif t > tip - 0.16:
                load = max(load, (0.62 + 0.1 * nl(t * 11.0)) * smooth(tip - 0.16, tip - 0.02, t))   # r6: ramps in (no seam)          # r4: every body tip is laid in LOADED ink - the shape must read, not fade into dry streaks
            if not in_press and load < tooth:
                flush(5); continue
            if any(abs(t - bt) < bw for bt, bw in breaks):
                flush(0); continue
            if plate_t and any(abs(t - pt) < 0.016 for pt in plate_t[1:]):
                flush(0); continue
            c = Vector(f(t)); d = tangent(f, t); n = Vector((-d.y, d.x))
            spl = smooth(split0, split0 + 0.22, t) * hollow
            wfac = 1.0 + (0.22 * (1.0 - t / max(spec["press"], 1e-3)) if in_press else 0.0)
            if spec.get("taper"):
                wfac *= 1.0 - 0.5 * smooth(spec["press"] * 0.8, 1.0, t)          # r6: the comet narrows behind its wet head
            if plate_t and t > plate_t[0]:
                k = sum(1 for pt in plate_t if t > pt)
                wfac *= [1.0, 0.80, 0.60, 0.42][k]
                spl *= 0.25                                                  # a plate is a pressed dab: closed, not split
            lat = r * (1.0 - spl) + cc[ci] * spl + (r - cc[ci]) * spl * 0.30
            amp = (0.03 + 0.10 * abs(r) ** 2) * fray
            wob = (nw(t * 6.0) - 0.5) * amp * smooth(0.05, 0.4, t) + (nt_(t * 31.0) - 0.5) * 0.025 * fray * abs(r)
            if body == "Water":
                wob += 0.06 * math.sin(t * 8.0 + ci * 1.9) * smooth(0.3, 0.6, t)
            if body == "Wood" and t > tip:
                lat += (cc[ci] * 0.9 + r * 0.25) * (t - tip) / (1 - tip)    # r4: the three fibre fingers diverge as they run on                      # the torn fibres part as they run on
            off = (lat + wob) * width * 0.5 * wfac
            p = c + n * off
            if any((p - hc).length < hr_ * (1.0 + 0.25 * (nt_(t * 40) - 0.5)) for hc, hr_ in hole_centres):
                flush(2); continue
            near_hole = min([(p - hc).length / hr_ for hc, hr_ in hole_centres] + [9.0])
            w = rb * (0.45 + 0.55 * max(0.0, min(1.0, load))) * (1.3 if in_press else 1.0)
            if body == "Wood" and t > tip:
                w *= 0.7
            dry = 1.0 - max(0.0, min(1.0, load))
            tone = 0.04 + 0.62 * dry ** 1.3
            pig = 1.0 if dry + 0.30 * (nt_(t * 4.0 + 99) - 0.5) > pig_thr else 0.0
            if near_hole < 1.45:                                             # a burnt hole: its rim is charred black ink
                tone = 0.0; pig = 0.0
            if plate_t and t > plate_t[0]:
                k = sum(1 for pt in plate_t if t > pt)
                into = t - plate_t[k - 1]
                if into < 0.05:
                    tone, pig = 0.0, 0.0                                     # the plate's front lip: dark
                elif abs(lat) > 0.78:
                    pig = 1.0                                                # the plate's side edge: pigment
            if body == "Wood" and t > tip + 0.05 and pig_thr < 0.7:
                pig = 1.0                                                    # fibre ends carry the hue
            reveal = min(1.0, t * 0.92 + 0.03 * nl(i * 3.1))
            pts.append((p.x, p.y)); wid.append(w); col.append((tone, pig, reveal, 1.0))
        flush(0 if body == "Metal" else 9)
    return f


def needles(rib, f, spec, rng, zbase):
    """thin straight needle lines out of the stroke's end (and one out of its start): the reference's impact lines, ink, no light"""
    L = spec["L"]; W = spec["W"]
    n_need = spec["needles"]
    ends = [0.5 * L + 0.5 * L * 0.0]
    n_end = n_need
    for k in range(n_need):
        at_end = k < n_end
        t = 0.74 if at_end else 0.03
        c = Vector(f(t)); d = tangent(f, t) * (1 if at_end else -1)
        # a fan out of the end: ordered angles and ordered start offsets, so no two lines cross
        u = (k / max(1, n_end - 1) - 0.5) if n_end > 1 else 0.0
        ang = math.radians(u * 34.0 + rng.uniform(-3, 3)) if at_end else math.radians(rng.uniform(-8, 8))
        dd = Vector((d.x * math.cos(ang) - d.y * math.sin(ang), d.x * math.sin(ang) + d.y * math.cos(ang)))
        nrm = Vector((-d.y, d.x))
        start = c + nrm * (u * 0.5 * spec["width"]) + d * rng.uniform(-0.02, 0.04) * L
        # as long as the frame allows: stop 2 % short of the edge
        ln = rng.uniform(0.75, 1.0) * L
        for s in range(60):
            q = start + dd * ln
            if 0.02 * L < q.x < 0.98 * L and abs(q.y) < 0.48 * W:
                break
            ln *= 0.93
        pts = [tuple(start + dd * (ln * s / 12)) for s in range(13)]
        w = [0.006 * L * (1 - s / 12.5) ** 0.7 for s in range(13)]
        rib.strip(pts, w, [(0.02, 0.0, 0.9 + 0.08 * s / 12, 1.0) for s in range(13)], zbase + 0.01)


# ---------------------------------------------------------------------------------------------------------------------------------
# metaballs: the wet press pool and the droplets
# ---------------------------------------------------------------------------------------------------------------------------------
def metaball_pool(name, centres, mat):
    mb = bpy.data.metaballs.new(name)
    mb.resolution = 0.004; mb.render_resolution = 0.002; mb.threshold = 0.6
    ob = bpy.data.objects.new(name, mb)
    bpy.context.scene.collection.objects.link(ob)
    for c in centres:
        x, y, r, sx, sy = c[:5]
        e = mb.elements.new(); e.type = 'ELLIPSOID'; e.co = (x, y, 0.0); e.radius = r; e.size_x = sx; e.size_y = sy; e.size_z = 1.0
        if len(c) > 5:
            a = c[5]; e.rotation = (math.cos(a * 0.5), 0.0, 0.0, math.sin(a * 0.5))
    ob.data.materials.append(mat)
    return ob


def emission_material(name, use_attr=True, color=(0, 0, 0, 1)):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    for n in list(nt.nodes):
        nt.nodes.remove(n)
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    em = nt.nodes.new("ShaderNodeEmission")
    em.inputs["Strength"].default_value = 1.0
    if use_attr:
        at = nt.nodes.new("ShaderNodeAttribute"); at.attribute_name = "c"; at.attribute_type = 'GEOMETRY'
        sep = nt.nodes.new("ShaderNodeSeparateColor")
        comb = nt.nodes.new("ShaderNodeCombineColor")
        # paper grain on the tone (R): a procedural noise texture mottles the ink a little; G (pigment) and B (reveal order) pass unchanged
        tc = nt.nodes.new("ShaderNodeTexCoord")
        nz = nt.nodes.new("ShaderNodeTexNoise"); nz.inputs["Scale"].default_value = 90.0; nz.inputs["Detail"].default_value = 6.0
        mixm = nt.nodes.new("ShaderNodeMath"); mixm.operation = 'MULTIPLY_ADD'
        nt.links.new(tc.outputs["Object"], nz.inputs["Vector"])
        nt.links.new(at.outputs["Color"], sep.inputs["Color"])
        nt.links.new(nz.outputs["Fac"], mixm.inputs[0]); mixm.inputs[1].default_value = 0.16
        nt.links.new(sep.outputs["Red"], mixm.inputs[2])
        sub = nt.nodes.new("ShaderNodeMath"); sub.operation = 'SUBTRACT'; sub.inputs[1].default_value = 0.08; sub.use_clamp = True
        nt.links.new(mixm.outputs["Value"], sub.inputs[0])
        nt.links.new(sub.outputs["Value"], comb.inputs["Red"])
        nt.links.new(sep.outputs["Green"], comb.inputs["Green"])
        nt.links.new(sep.outputs["Blue"], comb.inputs["Blue"])
        nt.links.new(comb.outputs["Color"], em.inputs["Color"])
    else:
        em.inputs["Color"].default_value = color
    nt.links.new(em.outputs["Emission"], out.inputs["Surface"])
    return m


def clear_scene():
    for ob in list(bpy.data.objects):
        bpy.data.objects.remove(ob, do_unlink=True)
    for coll in (bpy.data.meshes, bpy.data.metaballs, bpy.data.materials, bpy.data.cameras):
        for d in list(coll):
            coll.remove(d)


def setup_render(L, W, path):
    sc = bpy.context.scene
    try:
        sc.render.engine = 'BLENDER_EEVEE'
    except TypeError:
        sc.render.engine = 'BLENDER_EEVEE_NEXT'
    sc.render.film_transparent = True
    try:
        sc.view_settings.view_transform = 'Raw'
    except TypeError:
        sc.view_settings.view_transform = 'Standard'
    sc.view_settings.look = 'None'
    sc.view_settings.exposure = 0.0; sc.view_settings.gamma = 1.0
    sc.sequencer_colorspace_settings.name = 'Non-Color' if 'Non-Color' in [i.identifier for i in bpy.types.ColorManagedSequencerColorspaceSettings.bl_rna.properties['name'].enum_items] else sc.sequencer_colorspace_settings.name
    sc.render.resolution_x = RES
    sc.render.resolution_y = max(64, int(round(RES * W / L)))
    sc.render.resolution_percentage = 100
    sc.render.filter_size = 0.9
    try:
        sc.eevee.taa_render_samples = 16
    except AttributeError:
        pass
    sc.render.image_settings.file_format = 'PNG'
    sc.render.image_settings.color_mode = 'RGBA'
    sc.render.image_settings.color_depth = '16'
    cam_data = bpy.data.cameras.new("cam"); cam_data.type = 'ORTHO'; cam_data.ortho_scale = L
    cam = bpy.data.objects.new("cam", cam_data); sc.collection.objects.link(cam)
    cam.location = (L * 0.5, 0.0, 5.0); cam.rotation_euler = (0, 0, 0)
    cam_data.clip_start = 0.1; cam_data.clip_end = 20
    sc.camera = cam
    if sc.world is None:
        sc.world = bpy.data.worlds.new("w")
    sc.world.use_nodes = False if hasattr(sc.world, 'use_nodes') else None
    sc.render.filepath = path


def build_mesh(rib, name, mat):
    me = bpy.data.meshes.new(name)
    me.from_pydata(rib.verts, [], rib.faces)
    a = me.color_attributes.new("c", 'FLOAT_COLOR', 'POINT')
    for k, c in enumerate(rib.cols):
        a.data[k].color = c
    me.materials.append(mat)
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    return ob


def render_element(name, spec, body, seed):
    clear_scene()
    L, W = spec["L"], spec["W"]
    path = os.path.join(OUT, "%s_%s.png" % (name, body))
    setup_render(L, W, path)
    rng = random.Random(seed + 7)
    mat = emission_material("ink")
    rib = Ribbon()
    f = brush(rib, spec, body, seed)
    needles(rib, f, spec, rng, 0.0)
    build_mesh(rib, name, mat)
    # the wet press: a metaball pool around the first 12 % of the stroke (the blunt, loaded head with its darker pooled rim)
    if spec["press"] > 0:
        pool_rim = emission_material("poolrim", False, (0.0, 0.0, 0.0, 1))
        pool_in = emission_material("poolin", False, (0.16, 0.0, 0.0, 1))
        cs = []
        nk = 6
        for k in range(nk):
            t = spec["press"] * (0.05 + 1.05 * k / (nk - 1)) * rng.uniform(0.9, 1.1)
            c = Vector(f(t)); d = tangent(f, t); n = Vector((-d.y, d.x))
            p = c + n * rng.uniform(-0.12, 0.12) * spec["width"]
            rad = spec["width"] * (0.27 - 0.10 * k / (nk - 1)) * rng.uniform(0.9, 1.05)
            cs.append((p.x, p.y, rad, rng.uniform(1.3, 1.8), rng.uniform(0.75, 0.95), math.atan2(d.y, d.x)))
        metaball_pool("pool_rim", cs, pool_rim).location.z = -0.01
        # the inside of the pool is a little lighter than its rim (the reference's wet head: lighter core, dark pooled edge)
        inner = [(x + 0.02 * spec["width"], y, r * 0.74, sx, sy, a) for (x, y, r, sx, sy, a) in cs]
        metaball_pool("pool_in", inner, pool_in).location.z = 0.02
    # droplets thrown off the stroke (a ballistic toss from points along it; each lands as a round splat + a short tail + satellites)
    if spec.get("drops", 0) > 0:
        dmat = emission_material("drop", False, (0.03, 0.0, 0.85, 1))
        cs = []
        for k in range(spec["drops"]):
            t = rng.uniform(0.3, 0.9)
            c = Vector(f(t)); d = tangent(f, t); n = Vector((-d.y, d.x)) * (1 if rng.random() < 0.5 else -1)
            v = (n * rng.uniform(0.6, 1.0) + d * rng.uniform(0.2, 0.6)).normalized()
            dist = rng.uniform(0.25, 0.45) * spec["W"]
            p = c + v * dist
            r = spec["width"] * rng.uniform(0.08, 0.14)
            cs.append((p.x, p.y, r, 1.0, 1.0))
            cs.append((p.x - v.x * r * 1.4, p.y - v.y * r * 1.4, r * 0.55, 1.6, 0.7))
            for q in range(rng.randint(2, 4)):
                pp = p + v * rng.uniform(r * 2, r * 5) + Vector((-v.y, v.x)) * rng.uniform(-r * 2, r * 2)
                cs.append((pp.x, pp.y, r * rng.uniform(0.18, 0.35), 1.0, 1.0))
        metaball_pool("drops", cs, dmat)
    bpy.ops.render.render(write_still=True)
    return path


def render_fleck(name, spec, seed):
    clear_scene()
    L, W = spec["L"], spec["W"]
    path = os.path.join(OUT, "%s.png" % name)
    setup_render(L, W, path)
    rng = random.Random(seed)
    m = emission_material("fleck", False, (0.03, 0.0, 0.5, 1))
    big = W * rng.uniform(0.26, 0.36)
    cs = [(L * 0.70, 0.0, big, 1.0, 1.0)]
    # the short tail behind the drop (it flies toward +x)
    for k in range(4):
        cs.append((L * 0.70 - big * (0.9 + 0.7 * k), rng.uniform(-0.1, 0.1) * big, big * (0.55 - 0.11 * k), 1.5, 0.75))
    for k in range(rng.randint(3, 6)):
        cs.append((L * rng.uniform(0.15, 0.95), rng.uniform(-0.38, 0.38) * W, W * rng.uniform(0.03, 0.08), 1.0, 1.0))
    metaball_pool(name, cs, m)
    bpy.ops.render.render(write_still=True)
    return path


def render_ignite(spec, seed):
    clear_scene()
    L, W = spec["L"], spec["W"]
    path = os.path.join(OUT, "ignite.png")
    setup_render(L, W, path)
    rng = random.Random(seed)
    cx, cy = L * 0.5, 0.0
    m = emission_material("blot", False, (0.04, 0.0, 0.0, 1))
    cs = [(cx + rng.uniform(-0.02, 0.02), cy + rng.uniform(-0.02, 0.02), 0.03 * rng.uniform(0.9, 1.3), rng.uniform(0.8, 1.4), rng.uniform(0.8, 1.2)) for _ in range(4)]
    metaball_pool("blot", cs, m)
    rib = Ribbon()
    for k in range(5):
        a = k / 5 * math.tau + rng.uniform(-0.3, 0.3)
        d = Vector((math.cos(a), math.sin(a)))
        r0 = 0.06; r1 = rng.uniform(0.22, 0.29)
        pts = [(cx + d.x * (r0 + (r1 - r0) * s / 10), cy + d.y * (r0 + (r1 - r0) * s / 10)) for s in range(11)]
        rib.strip(pts, [0.012 * (1 - s / 10.5) for s in range(11)], [(0.02, 0.0, s / 10, 1.0) for s in range(11)], 0.01)
    build_mesh(rib, "needles", emission_material("needle"))
    bpy.ops.render.render(write_still=True)
    return path


def main():
    os.makedirs(OUT, exist_ok=True)
    meta = {"elements": {}, "shared": {}, "bodies": BODIES, "res": RES}
    for ei, (name, spec) in enumerate(ELEMENTS.items()):
        meta["elements"][name] = {k: v for k, v in spec.items()}
        if ONLY and name not in ONLY:
            continue
        for bi, body in enumerate(BODIES):
            if body not in ONLY_BODIES:
                continue
            # the same seed for every body: the trunk is the same brush, only the body tip differs (template x body, not bespoke)
            p = render_element(name, spec, body, 1000 + ei * 37)
            print("NB20 rendered", p, flush=True)
    for k, (name, spec) in enumerate(SHARED.items()):
        meta["shared"][name] = dict(spec)
        if ONLY and name not in ONLY:
            continue
        p = render_fleck(name, spec, 500 + k * 11) if name.startswith("fleck") else render_ignite(spec, 900)
        print("NB20 rendered", p, flush=True)
    with open(os.path.join(OUT, "elements.json"), "w", encoding="utf-8") as fh:
        json.dump(meta, fh, indent=1)
    print("NB20 DONE", flush=True)


main()
