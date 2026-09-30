"""#306 opening mine (폐광) as a working coal/ore mine — PLAN Art/Playtest306/PLAN.md §2-7 (Track A generator, all values TEST).

Builds on the #297 V4 cave (cave297_dressing.py: frame, sections, round-wood primitives, track). Mine-local = geometry frame
(= local space of the scene `mine` root). The yard (갱구 마당) is world space around the portal axis (cave297_portal.py frame).
  * packwalls (버력 벽): dry-stacked spoil walls 1.6-2.4 m high set in 1.5-2.5 m from the rock, so the main and gallery drifts
    read 5-6 m wide (walk width >= 3 m); trimmed at turns/junctions (the ore seams at the turns stay visible) and around the
    gallery seams; stones LOD0, backing faces LOD1; collision = the backing prism (mesh collider per chunk);
  * dense full sets (동발) on the packwall line: round posts, split caps under a 3.8 m timbered roof, wedges, blocking stubs up to
    the rock and lagging poles between sets; 2.5 m apart in the first 40 m and the last 26 m before the portal sets, 3 m between;
    none next to the gallery lanterns; post collision boxes;
  * mine carts (광차, planks + iron straps + tyred wheels on the 0.9 m track): overturned by the start, derailed at the blast
    evidence, loaded on the rails, and a tipping cart at the spoil tip; a spur into the branch (갈림길) and stop blocks (멈춤목);
  * drainage (배수): wooden trough on trestles to a sump beside the deep_water sound, ladle (용두레) and bucket (두레박);
  * miner traces: A-frame carrier + tray, shovel, sledge, straw shoes, raincoat on a post, spent pine torch, rope coil;
  * wall accents fitted by ray in Unity: coal seams (탄층, matte black, never lit), pick-mark hatching (정 자국) around the work
    faces and the hand-driven portal drift, ore crystal clusters on the #297 seams (the only glowing thing: ART-INK, in-wall
    veins), black magic-stone dust (검은 마석 가루) and puddles as floor patches, burnt fuses (탄 심지) as floor ribbons;
  * yard (갱구 마당, the 40 m flat approach and the overlook stay clear): spoil tip on the valley side with a tip track,
    windlass over a small shaft (Jeju Well prefab), timber stack, collapsed miners' hut, mortar + scale, a closure notice (榜);
  * sounds (procedural, build_compact_cave_audio.py): timber creak, sand trickle, bucket drip, distant knocking, pulley creak;
  * lamps: only the first two gallery lanterns stay lit (the ore veins lead).
Outputs Art/World/Compact/Rebuild/Mine306/{Meshes/*.json, mine306.json, plan.png}; Unity side = CompactMine306 (editor queue
`Oheangbu.EditorTools.WorldMacro.CompactMine306 Run apply`). python Tools/Art/cave297_mine306.py
"""
import json, math, sys
from collections import defaultdict
from pathlib import Path
import numpy as np
sys.path.insert(0, str(Path(__file__).resolve().parent))
from hanok297 import MB, beam, nz, UP
import cave297_dressing as CD

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Art/World/Compact/Rebuild/Mine306'
F297 = ROOT / 'Art/World/Compact/Rebuild/Finish297'
GEO = ROOT / 'Art/World/Compact/Rebuild/CaveV4/geometry.json'
FLOOR = CD.FLOOR
CHUNK = 14.0                                   # render chunk (m): LOD + the 4-lights-per-object limit of the Mobile renderer
# -- TEST values ---------------------------------------------------------------------------------------------------------
WALK_W = 5.6; INSET = (1.5, 2.5); FACE_MIN = 2.6; PACK_H = (1.6, 2.4); BATTER = .28
SET_DENSE, SET_SPARSE, DENSE_START, DENSE_END = 2.5, 3.0, 40.0, 26.0
SET_END = 11.4                                  # path end = face + 2.6; the portal's own 3 sets reach face - 6.8
CAP_H = 3.8; CAP_MIN = 2.5; SPAN = (3.4, 6.4)
TRIM_TURN, TRIM_JUNCTION, TRIM_SET = 3.5, 4.5, 3.0
LAMPS_LIT = [0, 1]                              # Gallery_Lantern_<i> kept lit (geometry.json lights order)
LOADED_CART_U, BAY_HALF, CART_CLEAR = 96.0, 3.0, 1.9   # loaded cart on the rails: a passing bay (no packwall on the wider side,
                                                # no set within CART_CLEAR) keeps one lane >= 3 m beside it (PLAN §2-7 walk width)
OPEN_ROOMS = [(55.0, 85.0)]                     # (u0, u1): no packwall and no sets; #306 tutorial boss arena = blast room (mine_inquiry u 55–85, PLAN §2-11)
CLEAR_LANE, OVERLOOK = 4.0, 39.6                # yard: lane half-width kept empty, overlook distance on the axis (relocation.json)
PREFAB = 'Assets/KoreanTraditionalFestival/Prefabs/'
BILLE = 'Assets/BillemotdonggulLavaTubePack/Prefabs/'
WELL = 'Assets/JejumokGwana/Prefabs/Decoration/Well.prefab'


def r3(v): return [round(float(x), 3) for x in v]


# ============================================================================================================== frame / path
class Path3:
    """Polyline with arc length; frame(u) -> point, unit tangent (xz), right normal n (section() convention: lateral > 0 = hr)."""
    def __init__(self, P):
        self.P = np.asarray(P, float); d = np.linalg.norm(np.diff(self.P[:, [0, 2]], axis=0), axis=1)
        keep = np.concatenate([[True], d > 1e-3]); self.P = self.P[keep]
        self.S = np.concatenate([[0], np.cumsum(np.linalg.norm(np.diff(self.P[:, [0, 2]], axis=0), axis=1))]); self.L = float(self.S[-1])

    def frame(self, u):
        u = float(np.clip(u, 0, self.L)); k = min(int(np.searchsorted(self.S, u, 'right')) - 1, len(self.P) - 2)
        a, b = self.P[k], self.P[k + 1]; f = (u - self.S[k]) / max(self.S[k + 1] - self.S[k], 1e-6)
        t = b - a; t = np.array([t[0], 0, t[2]]) / max(np.linalg.norm(t[[0, 2]]), 1e-6)
        return a + (b - a) * f, t, np.array([t[2], 0, -t[0]])

    def project(self, q):
        best = (1e9, 0.0)
        for k in range(len(self.P) - 1):
            a, b = self.P[k][[0, 2]], self.P[k + 1][[0, 2]]; ab = b - a; f = float(np.clip((np.asarray(q)[[0, 2]] - a) @ ab / max(ab @ ab, 1e-9), 0, 1))
            d = float(np.linalg.norm(np.asarray(q)[[0, 2]] - (a + ab * f)))
            if d < best[0]: best = (d, float(self.S[k] + f * (self.S[k + 1] - self.S[k])))
        return best[1], best[0]


class Chunks:
    """Per-chunk builders: chunks[(layer, lod)][cell] -> MB; cell from the element's xz."""
    def __init__(self): self.m = defaultdict(dict)

    def mb(self, layer, lod, p):
        c = (int(math.floor(p[0] / CHUNK)), int(math.floor(p[2] / CHUNK))); d = self.m[(layer, lod)]
        if c not in d: d[c] = MB(f'{layer}_{c[0]}_{c[1]}')
        return d[c]

    def export(self, folder, lods=(0, 1, 2), collision=()):
        """-> manifest rows {name, lods:[files], collision:file|None}."""
        rows = []; layers = sorted({k[0] for k in self.m if k[0] not in collision})
        for layer in layers:
            cells = sorted({c for (ly, l), d in self.m.items() if ly == layer for c in d})
            for c in cells:
                name = f'{layer}_{c[0]}_{c[1]}'; files = []
                for l in lods:
                    mb = self.m.get((layer, l), {}).get(c)
                    if mb is None or mb.tris() == 0: continue
                    mb.name = f'{name}_LOD{l}'; mb.export(folder / f'{name}_LOD{l}.json'); files.append(f'{name}_LOD{l}')
                col = self.m.get((layer + '_Collision', 0), {}).get(c); cf = None
                if col is not None and col.tris(): col.name = name + '_Collision'; col.export(folder / f'{name}_Collision.json'); cf = name + '_Collision'
                if files: rows.append(dict(name=name, layer=layer, lods=files, collision=cf))
        return rows

    def tris(self, layer, lod): return sum(mb.tris() for mb in self.m.get((layer, lod), {}).values())


def wall_hit(V, origin, d, radius=.35, reach=14.0):
    """Offline ray against the cave vertex cloud: distance to the nearest vertex within `radius` of the ray (None if open)."""
    d = nz(np.asarray(d, float)); rel = V - origin; along = rel @ d
    perp = np.linalg.norm(rel - along[:, None] * d, axis=1); sel = (along > .3) & (along < reach) & (perp < radius)
    return float(along[sel].min()) if sel.any() else None


# ============================================================================================================== stones
def stone(mb, c, t, up, w, L, H, D, rng, mat='rock_loose', back=False):
    """Jittered dry-stone block: centre c, axes t (along), up, w (into the wall); front face toward -w. 5 faces (10 tris)."""
    j = .07 * min(L, H)
    a1, a2 = rng.normal(0, .09), rng.normal(0, .07)                                   # slight tilt: laid by hand, not cut
    t, up = nz(t * math.cos(a1) + w * math.sin(a1)), nz(up * math.cos(a2) + t * math.sin(a2)); w = nz(np.cross(t, up)) * np.sign(np.dot(np.cross(t, up), w))
    C = {}
    for i in (-1, 1):
        for k in (-1, 1):
            for m in (-1, 1):
                C[(i, k, m)] = c + t * i * L / 2 + up * k * H / 2 + w * m * D / 2 + rng.normal(0, j, 3) * np.array([1, .6, 1])
    q = lambda *ks: [C[k] for k in ks]
    mb.poly(q((-1, -1, -1), (1, -1, -1), (1, 1, -1), (-1, 1, -1)), mat, -w)                 # front
    mb.poly(q((-1, 1, -1), (1, 1, -1), (1, 1, 1), (-1, 1, 1)), mat, up)                     # top
    mb.poly(q((-1, -1, -1), (-1, 1, -1), (-1, 1, 1), (-1, -1, 1)), mat, -t)                 # ends
    mb.poly(q((1, -1, -1), (1, -1, 1), (1, 1, 1), (1, 1, -1)), mat, t)
    mb.poly(q((-1, -1, -1), (-1, -1, 1), (1, -1, 1), (1, -1, -1)), mat, -up)
    if back: mb.poly(q((-1, -1, 1), (-1, 1, 1), (1, 1, 1), (1, -1, 1)), mat, w)


_ICO = None


def lump(mb, c, size, rng, mat, flat=.62, yaw=None):
    """Icosahedral lump (20 tris) centred at c, squashed vertically by `flat`."""
    global _ICO
    if _ICO is None:
        g = (1 + 5 ** .5) / 2
        _ICO = (nz(np.array([(-1, g, 0), (1, g, 0), (-1, -g, 0), (1, -g, 0), (0, -1, g), (0, 1, g), (0, -1, -g), (0, 1, -g), (g, 0, -1), (g, 0, 1), (-g, 0, -1), (-g, 0, 1)], float)),
                [(0, 11, 5), (0, 5, 1), (0, 1, 7), (0, 7, 10), (0, 10, 11), (1, 5, 9), (5, 11, 4), (11, 10, 2), (10, 7, 6), (7, 1, 8),
                 (3, 9, 4), (3, 4, 2), (3, 2, 6), (3, 6, 8), (3, 8, 9), (4, 9, 5), (2, 4, 11), (6, 2, 10), (8, 6, 7), (9, 8, 1)])
    P, F = _ICO; y = rng.uniform(0, 2 * math.pi) if yaw is None else yaw; cy, sy = math.cos(y), math.sin(y)
    Vr = P * size * np.array([1, flat, 1]) * (1 + .2 * rng.standard_normal((12, 1)))
    Vr = np.stack([Vr[:, 0] * cy + Vr[:, 2] * sy, Vr[:, 1], -Vr[:, 0] * sy + Vr[:, 2] * cy], -1) + np.asarray(c, float)
    b = mb._add(Vr, nz(Vr - Vr.mean(0)), Vr[:, [0, 2]] * .5)
    for f in F:
        i, j, l = b + f[0], b + f[1], b + f[2]
        if np.dot(np.cross(Vr[f[1]] - Vr[f[0]], Vr[f[2]] - Vr[f[0]]), Vr[f[0]] - Vr.mean(0)) < 0: j, l = l, j
        mb.T[mat] += [i, j, l]


# ============================================================================================================== packwall
def packwalls(V, path, ck, windows, rng, stats):
    """Runs of packwall per side. windows: list of (u0, u1, side|0) where no wall is built. Returns faces[side] = {u: face}."""
    step = .5; U = np.arange(0.0, path.L + 1e-6, step); faces = {-1: {}, 1: {}}
    raw = {-1: [], 1: []}
    for u in U:
        p, t, n = path.frame(u); hl, hr, ceil, _ = CD.section(V, p, t)
        for side, hw in ((-1, hl), (1, hr)):
            ok = hw is not None and hw < 7.0 and not any(a <= u <= b and s in (0, side) for a, b, s in windows)
            if ok:                                                    # each side on its own: the walk stays centred on the track
                face = hw - float(np.clip(hw - WALK_W / 2, *INSET))
                ok = face >= FACE_MIN
            raw[side].append((u, face if ok else None, hw))
    runs = []
    for side in (-1, 1):
        cur = []
        for u, f, hw in raw[side] + [(None, None, None)]:
            if f is not None: cur.append((u, f, hw)); continue
            if len(cur) * step >= 3.0: runs.append((side, cur))
            cur = []
    for side, run in runs:
        us = np.array([r[0] for r in run]); fs = np.array([r[1] for r in run]); hws = np.array([r[2] for r in run])
        k = 7; fs = np.convolve(np.pad(fs, k // 2, mode='edge'), np.ones(k) / k, mode='valid')
        L = us[-1] - us[0]; h = np.empty(len(us))
        for i, u in enumerate(us):
            base = PACK_H[0] + (PACK_H[1] - PACK_H[0]) * (.5 + .5 * math.sin(u * .37 + side * 1.3) * math.cos(u * .13 + .4))
            end = min(u - us[0], us[-1] - u); taper = .55 + .45 * min(1.0, end / 1.4)
            p, t, n = path.frame(u); rock = CD.ceiling_at(V, p, t, side * fs[i], .5)
            h[i] = max(.5, min(base * taper, (rock - .8) if rock is not None else base))
        for i, u in enumerate(us): faces[side][round(float(u), 1)] = float(fs[i])
        stats['packwall_m'] += L; stats['packwall_runs'] += 1
        # backing prism (LOD0/1 visual, collision): front face base -> batter top, then top slope into the rock
        for i in range(len(us) - 1):
            quads = []
            for j in (i, i + 1):
                p, t, n = path.frame(us[j]); w = n * side
                fb = p + w * (fs[j] + .06); fb[1] = FLOOR - .15
                ft = p + w * (fs[j] + .06 + BATTER); ft[1] = FLOOR + h[j] - .05
                bk = p + w * (hws[j] + .35); bk[1] = FLOOR + h[j] + .45
                quads.append((fb, ft, bk, w))
            (a0, a1, a2, w0), (b0, b1, b2, _) = quads; mid = (a1 + b1) / 2
            for lod in (0, 1):
                mb = ck.mb('Packwall', lod, mid)
                mb.quad(a0, b0, b1, a1, 'rock_loose', -w0 + UP * .2); mb.quad(a1, b1, b2, a2, 'rock_loose', UP - w0 * .3)
            col = ck.mb('Packwall_Collision', 0, mid)
            fa0 = a0 - w0 * .06; fb0 = b0 - w0 * .06
            col.quad(fa0, fb0, b1, a1, 'c', -w0); col.quad(a1, b1, b2, a2, 'c', UP)
        # stones in courses (LOD0), top rubble (LOD0)
        u = us[0]
        y = 0.0; course = 0
        while True:
            ch = float(rng.uniform(.26, .44)); yc = y + ch / 2
            if yc > h.max() - .1: break
            x = us[0] + float(rng.uniform(0, .3)) * (course % 2)
            while x < us[-1] - .2:
                Ls = float(rng.uniform(.42, .88)); cx = min(x + Ls / 2, us[-1] - .15); i = int(np.clip(round((cx - us[0]) / step), 0, len(us) - 1))
                if yc < h[i] - .08:
                    p, t, n = path.frame(cx); w = n * side; lat = fs[i] + BATTER * (yc / max(h[i], .1)) + .2 - float(rng.uniform(0, .06))
                    c = p + w * lat; c[1] = FLOOR + yc
                    stone(ck.mb('Packwall', 0, c), c, t, UP, w, Ls * .96, ch * .94, .42, rng)
                    stats['stones'] += 1
                x += Ls
            y += ch; course += 1
        for u in np.arange(us[0] + .4, us[-1] - .3, .85):
            i = int(np.clip(round((u - us[0]) / step), 0, len(us) - 1)); p, t, n = path.frame(u); w = n * side
            c = p + w * (fs[i] + BATTER + .35 + float(rng.uniform(0, .5))); c[1] = FLOOR + h[i] + .05
            lump(ck.mb('Packwall', 0, c), c, float(rng.uniform(.14, .3)), rng, 'rock_loose')
        # spill at the wall foot (a few stones rolled out)
        for u in np.arange(us[0] + 1.0, us[-1] - .5, 2.3):
            i = int(np.clip(round((u - us[0]) / step), 0, len(us) - 1)); p, t, n = path.frame(u + float(rng.normal(0, .5))); w = n * side
            s = float(rng.uniform(.1, .22)); c = p + w * (fs[i] - float(rng.uniform(.1, .45))); c[1] = FLOOR - .2 * s
            lump(ck.mb('Packwall', 0, c), c, s, rng, 'rock_loose')
    return faces


# ============================================================================================================== timber sets
def full_set(ck, p, t, n, la, lb, cap, rock_a, rock_b, lod, key, lean=0, dense=True):
    """One set: tapered round posts at laterals la < 0 < lb (battered 5 cm inward), split cap at FLOOR+cap."""
    yb = FLOOR + .02; top = FLOOR + cap; sides = (8, 6, 4)[lod]; arc = (4, 3, 2)[lod]; mb = ck.mb('Timbers', lod, p)
    a = p + n * la; b = p + n * lb
    for q, inward, sgn in ((a, n, -1), (b, -n, 1)):
        head = np.array([q[0], top + .05, q[2]]) + inward * .05
        if lean == sgn:
            head = head + t * .2
            foot = np.array([q[0], yb, q[2]]) - t * 1.0 + inward * .12; mid = np.array([q[0], yb + (top - yb) * .58, q[2]]) + t * .12
            CD.log(mb, foot, mid, .1, .09, (6, 5, 4)[lod], 'wood_dark', rot=key)
        CD.log(mb, [q[0], yb - .04, q[2]], head, .155, .135, sides, 'wood_dark', rot=key + sgn)
        if lod == 0: ck.mb('Timbers_Collision', 0, p).box([q[0], (yb + top) / 2, q[2]], [.3, top - yb, .3], 'c', faces='xXzZ')
    ea, eb = a - n * .2, b + n * .2
    CD.split_log(mb, [ea[0], top, ea[2]], [eb[0], top, eb[2]], .17, .25, arc, 'wood_dark')
    if lod == 0:
        span = b - a
        for u, sg in ((.1, 1), (.9, -1)):
            c = a + span * u; CD.wedge(mb, [c[0], top + .31, c[2]], t * sg, .32, .12, .13, 'wood_board')
        for u, rock in ((.3, rock_a), (.7, rock_b)):                     # blocking stubs (고임목) from the cap up to the rock
            if rock is None: continue
            gap = rock - cap
            if .6 < gap < 3.4:
                c = a + span * u; CD.log(mb, [c[0], top + .24, c[2]], [c[0], FLOOR + rock + .15, c[2]], .09, .08, 6, 'wood_dark', rot=key * 3 + u)


def lagging(ck, A, B, lod, dense):
    """Poles / boards laid on two consecutive caps. A, B = (a_post, b_post, cap_y)."""
    (a0, b0, y0), (a1, b1, y1) = A, B
    if lod == 2: return
    fr = np.linspace(-.38, .38, 5 if dense else 2) if lod == 0 else np.linspace(-.3, .3, 3 if dense else 2)
    mid = (a0 + b0 + a1 + b1) / 4; mb = ck.mb('Timbers', lod, mid)
    for f in fr:
        pa = (a0 + b0) / 2 + (b0 - a0) * f; pb = (a1 + b1) / 2 + (b1 - a1) * f
        pa = np.array([pa[0], FLOOR + y0 + .27, pa[2]]); pb = np.array([pb[0], FLOOR + y1 + .27, pb[2]])
        d = nz(pb - pa); pa = pa - d * .15; pb = pb + d * .15
        if dense and lod == 0: CD.log(mb, pa, pb, .05, .045, 5, 'wood_dark', rot=f * 7)
        else: beam(mb, pa, pb, .2, .05, 'wood_board')


def timber_sets(V, path, ck, faces, turns, lanterns, rng, stats, clear=()):
    u = 1.2; prev = None; idx = 0; sets = []
    while u < path.L - SET_END:
        dense = u < DENSE_START or u > path.L - SET_END - DENSE_END
        step = SET_DENSE if dense else SET_SPARSE
        def clash(u):
            p, t, n = path.frame(u)
            return any(abs(float((q - p)[[0, 2]] @ t[[0, 2]])) < 1.1 and np.linalg.norm((q - p)[[0, 2]]) < 4 for q in lanterns)
        for _ in range(4):                                                                # step past a gallery lantern
            if clash(u): u += .6
        p, t, n = path.frame(u); ok = not any(abs(u - k) < TRIM_SET for k in turns) and not clash(u) and not any(a <= u <= b for a, b in clear)
        hl, hr, ceil, _ = CD.section(V, p, t) if ok else (None, None, None, None)
        if ok and (hl is not None or hr is not None) and ceil is not None:
            # posts stand in front of the packwall; where there is none (trims, seam windows, corner openings) the set is
            # free-standing on the same line (sets are frames, they do not need the rock beside them)
            key_u = round(float(u) * 2) / 2; nominal = WALK_W / 2 - .25
            fl = faces[-1].get(round(key_u, 1)); fr = faces[1].get(round(key_u, 1))
            la = -((fl - .25) if fl is not None else min(hl - .35, nominal) if hl is not None else nominal)
            lb = (fr - .25) if fr is not None else min(hr - .35, nominal) if hr is not None else nominal
            ra = CD.ceiling_at(V, p, t, la); rb = CD.ceiling_at(V, p, t, lb)
            rocks = [x for x in (ra, rb, ceil) if x is not None]; cap = min(CAP_H + .08 * math.sin(u * .7), min(rocks) - .55)
            if SPAN[0] <= lb - la <= SPAN[1] and cap >= CAP_MIN:
                key = float(p[0] * 1.7 + p[2] * .9); lean = {6: 1, 21: -1}.get(idx, 0)
                for lod in (0, 1, 2): full_set(ck, p, t, n, la, lb, cap, ra, rb, lod, key, lean, dense)
                cur = (p + n * la, p + n * lb, cap)
                if prev is not None and u - prev[0] <= 3.3:
                    for lod in (0, 1): lagging(ck, prev[1], cur, lod, dense)
                prev = (u, cur); idx += 1; sets.append(dict(u=round(u, 2), la=round(la, 2), lb=round(lb, 2), cap=round(cap, 2), dense=dense))
            else: prev = None
        else: prev = None
        u += step
    stats['sets'] = len(sets); stats['sets_dense'] = sum(s['dense'] for s in sets)
    return sets


# ============================================================================================================== carts, stops
def wheel(mb, c, axis, r, w, sides, mat_tyre, mat_hub):
    a = c - axis * w / 2; b = c + axis * w / 2
    mb.cyl(a, b, r, r, sides, mat_tyre, caps=True)
    mb.cyl(b, b + axis * .04, r * .35, r * .3, max(4, sides // 2), mat_hub, caps=True)


def cart(lod, loaded, rng):
    """Cart-local: origin on the rail tops between the axles, +z along the track, y up. Body 1.5 x .95 x .62, planked."""
    mb = MB('MineCart'); y0, y1, hx, hz = .34, .96, .475, .75; sides = 10 if lod == 0 else 6
    for sx in (-1, 1): beam(mb, [sx * .3, .27, -hz - .12], [sx * .3, .27, hz + .12], .12, .1, 'wood_dark')    # sills
    mb.box([0, y0 + .03, 0], [2 * hx, .06, 2 * hz], 'wood_board', faces='yY')                                   # floor
    if lod == 0:
        for k in range(3):                                                                                        # side/end planks
            yc = y0 + .1 + k * .2
            for sx in (-1, 1): mb.box([sx * (hx - .025), yc, 0], [.05, .18, 2 * hz], 'wood_board', faces='xXyYzZ')
            for sz in (-1, 1): mb.box([0, yc, sz * (hz - .025)], [2 * hx - .1, .18, .05], 'wood_board', faces='xXyYzZ')
        for sz in (-1, -.34, .34, 1):                                                                             # iron straps
            z = sz * (hz - .03)
            for sx in (-1, 1): mb.box([sx * (hx + .004), (y0 + y1) / 2, z], [.012, y1 - y0 + .02, .07], 'iron', faces='xXzZY')
            mb.box([0, y1 + .006, z], [2 * hx + .03, .012, .07], 'iron', faces='yYzZ')
        for sx in (-1, 1):
            for sz in (-1, 1): mb.box([sx * (hx + .006), (y0 + y1) / 2, sz * (hz + .006)], [.07, y1 - y0 + .02, .07], 'iron', faces='xXzZY')
        beam(mb, [-.35, y1 - .05, -hz - .28], [.35, y1 - .05, -hz - .28], .05, .05, 'wood_dark')                     # push bar
        for sx in (-1, 1): beam(mb, [sx * .35, y1 - .05, -hz - .3], [sx * .35, y1 - .15, -hz + .02], .05, .05, 'wood_dark')
    else:
        mb.box([0, (y0 + y1) / 2, 0], [2 * hx, y1 - y0, 2 * hz], 'wood_board', faces='xXzZ')
    for sz in (-.48, .48):                                                                                        # axles + wheels
        beam(mb, [-.5, .2, sz], [.5, .2, sz], .045, .045, 'iron')
        for sx in (-1, 1): wheel(mb, np.array([sx * .45, .2, sz]), np.array([sx, 0, 0.]), .2, .07, sides, 'iron', 'wood_dark')
    if loaded:
        for k in range(26 if lod == 0 else 8):
            x = float(rng.uniform(-hx + .12, hx - .12)); z = float(rng.uniform(-hz + .15, hz - .15)); crown = .18 * (1 - (x / hx) ** 2) * (1 - (z / hz) ** 2)
            lump(mb, [x, y1 - .06 + crown + float(rng.uniform(0, .06)), z], float(rng.uniform(.1, .2)), rng, 'coal' if k % 3 == 0 else 'ore_loose')
    return mb


def stop_block(mb, c, t, gauge=.9):
    """멈춤목: a log across the rails held by two stakes (local c on the floor, t = track direction)."""
    n = np.array([t[2], 0, -t[0]]); c = np.asarray(c, float)
    CD.log(mb, c - n * .8 + UP * .24, c + n * .8 + UP * .24, .13, .12, 7, 'wood_dark', caps=(True, True), rot=.3)
    for s in (-1, 1): CD.log(mb, c + n * s * .62 + t * .2 + UP * -.1, c + n * s * .62 + t * .2 + UP * .48, .06, .05, 5, 'wood_dark', caps=(False, True))


def spill(mb, c, t, rng, n_lumps=34, reach=1.6):
    """Spilled load in front of an overturned/derailed cart (non-glowing ore + coal)."""
    t = nz(np.asarray(t, float)); n = np.array([t[2], 0, -t[0]])
    for k in range(n_lumps):
        d = float(rng.gamma(1.6, reach / 3.2)); lat = float(rng.normal(0, .35 + .2 * d))
        s = float(np.clip(rng.lognormal(-1.9, .45), .06, .24)); q = np.asarray(c) + t * d + n * lat; q[1] = FLOOR - .25 * s
        lump(mb, q, s, rng, 'coal' if k % 2 else 'ore_loose')


# ============================================================================================================== drainage
def trough(mb, a, b, ya, yb):
    """나무 홈통: V trough of two boards a->b (floor points) with its lip at ya/yb above FLOOR, trestles every ~1.8 m."""
    a = np.asarray(a, float); b = np.asarray(b, float); d = nz(b - a); n = np.array([d[2], 0, -d[0]])
    A = np.array([a[0], FLOOR + ya, a[2]]); B = np.array([b[0], FLOOR + yb, b[2]])
    for s in (-1, 1):
        off = n * s * .1 + UP * -.02
        beam(mb, A + off, B + off, .03, .2, 'wood_board')      # (tilted by the boards' own thinness; reads as a V at this size)
    beam(mb, A + UP * -.1, B + UP * -.1, .08, .03, 'wood_board')
    L = float(np.linalg.norm((b - a)[[0, 2]]))
    for f in np.linspace(.08, .92, max(2, int(L / 1.8) + 1)):
        c = A + (B - A) * f
        for s in (-1, 1): beam(mb, [c[0] + n[0] * s * .16, FLOOR - .02, c[2] + n[2] * s * .16], c + n * s * .12 + UP * -.08, .05, .05, 'wood_dark')
        beam(mb, c + n * -.18 + UP * -.12, c + n * .18 + UP * -.12, .05, .05, 'wood_dark')


# ============================================================================================================== yard meshes (object-local)
def windlass():
    """권양기: forked posts 2.6 m apart (x), drum log with rope and crank; origin on the ground at the shaft centre."""
    mb = MB('Windlass')
    for sx in (-1, 1):
        CD.log(mb, [sx * 1.3, -.5, 0], [sx * 1.3, 1.25, 0], .11, .1, 7, 'wood_dark', caps=(False, True), rot=sx)
        for sz in (-1, 1): CD.log(mb, [sx * 1.3, 1.1, 0], [sx * 1.3, 1.42, sz * .12], .05, .045, 5, 'wood_dark', caps=(False, True))   # fork
        CD.log(mb, [sx * 1.3, -.2, -.75], [sx * 1.3, .9, -.05], .06, .05, 5, 'wood_dark', caps=(True, True))                       # brace
    CD.log(mb, [-1.45, 1.32, 0], [1.45, 1.32, 0], .12, .12, 8, 'wood_dark', caps=(True, True), rot=.2)                            # drum
    CD.log(mb, [-.35, 1.32, 0], [.45, 1.32, 0], .165, .165, 8, 'rope', caps=(True, True))                                        # rope coil
    beam(mb, [1.45, 1.32, 0], [1.62, 1.32, 0], .04, .04, 'iron'); beam(mb, [1.62, 1.32, 0], [1.62, .95, .12], .04, .04, 'iron')     # crank
    beam(mb, [1.62, .95, .12], [1.8, .95, .12], .035, .035, 'wood_dark')
    CD.log(mb, [.1, 1.2, .14], [.1, .05, .14], .015, .015, 4, 'rope')                                                            # hanging rope
    return mb


def timber_stack(rng):
    """갱목 더미: 6-5-3 logs, 3.2 m, on two chocks; origin on the ground, logs along z."""
    mb = MB('TimberStack')
    for z in (-1.1, 1.1): beam(mb, [-.85, .06, z], [.85, .06, z], .14, .12, 'wood_dark')
    y = .12
    for layer, count in enumerate((6, 5, 3)):
        r = .135; x0 = -(count - 1) * r
        for i in range(count):
            x = x0 + i * 2 * r + float(rng.normal(0, .015)); rr = float(rng.uniform(.12, .15)); dz = float(rng.normal(0, .12))
            CD.log(mb, [x, y + rr, -1.6 + dz], [x + float(rng.normal(0, .03)), y + rr, 1.6 + dz], rr, rr * .92, 7, 'wood_dark', caps=(True, True), rot=float(rng.uniform(0, 6)))
        y += 2 * r * .87
    return mb


def hut(rng):
    """무너진 덕대 막사: lean-to 3 x 2.4 m, one post broken, ridge fallen, roof boards slid, thatch bundles; origin on the ground,
    open side toward +z (the lane)."""
    mb = MB('HutCollapsed')
    CD.log(mb, [-1.5, -.3, -1.2], [-1.5, 2.1, -1.2], .09, .08, 6, 'wood_dark', caps=(False, True))
    CD.log(mb, [1.5, -.3, -1.2], [1.5, 2.1, -1.2], .09, .08, 6, 'wood_dark', caps=(False, True))
    CD.log(mb, [-1.5, -.3, 1.2], [-1.5, .9, 1.2], .09, .085, 6, 'wood_dark', caps=(False, True))                   # broken front post
    CD.log(mb, [1.2, .09, 1.6], [2.3, .12, -.3], .09, .08, 6, 'wood_dark', caps=(True, True))                        # fallen front post
    CD.log(mb, [-1.7, 2.12, -1.2], [1.7, 2.12, -1.2], .1, .09, 7, 'wood_dark', caps=(True, True))                  # back plate
    CD.log(mb, [-1.7, .95, 1.1], [1.6, .12, 1.4], .1, .09, 7, 'wood_dark', caps=(True, True))                      # fallen front plate
    for i, x in enumerate(np.linspace(-1.3, 1.3, 7)):                                                               # slid roof boards
        drop = .15 + .9 * (x + 1.3) / 2.6; tilt = float(rng.normal(0, .06))
        a = np.array([x, 2.2 - drop * .3, -1.3]); b = np.array([x + tilt, max(.1, 1.0 - drop), 1.35 + drop * .2])
        beam(mb, a, b, .26, .03, 'wood_board')
    for k in range(9):                                                                                              # thatch bundles
        c = np.array([float(rng.uniform(-1.8, 1.8)), .12, float(rng.uniform(.4, 2.4))])
        CD.log(mb, c - np.array([.35, 0, .1]), c + np.array([.35, .05, .1]), .11, .08, 6, 'straw', caps=(True, True), rot=float(k))
    beam(mb, [-.9, .45, -.6], [.2, .45, -.6], .3, .05, 'wood_board')                                                 # bench
    for x in (-.8, .1): beam(mb, [x, 0, -.6], [x, .43, -.6], .06, .2, 'wood_board')
    return mb


def notice(rng):
    """폐광·출입 금지 방: post + board + posted paper with brushed columns (no legible text); faces +z."""
    mb = MB('NoticeBoard')
    CD.log(mb, [0, -.4, 0], [0, 2.3, 0], .07, .065, 6, 'wood_dark', caps=(False, True))
    mb.box([0, 1.7, .08], [.95, .62, .04], 'wood_board', faces='xXyYzZ'); mb.box([0, 2.07, .1], [1.1, .05, .18], 'wood_board', faces='xXyYzZ')
    mb.box([0, 1.69, .104], [.78, .5, .004], 'paper', faces='Z')
    for col in range(6):
        x = .3 - col * .12
        for k in range(int(rng.integers(4, 8))):
            y = 1.88 - k * .06 - float(rng.uniform(0, .015)); w = float(rng.uniform(.03, .06))
            mb.box([x + float(rng.normal(0, .006)), y, .1075], [w, .016, .002], 'ink', faces='Z')
    return mb


def spoil_heap(A, D, R, terrain, yo, rng, stats):
    """버력더미 on the valley side of the approach: a flat-topped tip from the lane edge outward, 34 deg flanks down to the terrain.
    World mesh relative to the yard origin `yo`. Returns (visual MB, collision MB, footprint samples)."""
    vis = MB('SpoilHeap'); col = MB('SpoilHeap_Collision'); tan = math.tan(math.radians(34))
    # #306 nav: the mine_overlook__geumpyo_inn route crosses the yard at s 17-19; the crest starts at s 24 so the toe clears it
    S = np.arange(18.0, 42.01, .5); Lt = np.arange(-26.0, -4.49, .5)
    world = lambda s, l: A + D * s + R * l
    crest = lambda s, l: max(0.0, max(24.0 - s, s - 36.0, l - (-6.5), (-13.5 - .15 * (s - 24)) - l))   # metres outside the crest
    G = np.zeros((len(S), len(Lt), 3)); keep = np.zeros((len(S), len(Lt)), bool)
    for i, s in enumerate(S):
        top = float(terrain(*world(s, -4.5))) + .12
        for j, l in enumerate(Lt):
            x, z = world(s, l); g = float(terrain(x, z)); d = crest(s, l)
            hh = top - d * tan + .06 * math.sin(s * 1.7 + l * .9) + .05 * math.sin(s * .6 - l * 2.1)
            y = max(g - .35, hh); keep[i, j] = hh > g - .3
            G[i, j] = [x - yo[0], y - yo[1], z - yo[2]]
    b = vis._add(G.reshape(-1, 3), np.tile(UP, (G.size // 3, 1)), G.reshape(-1, 3)[:, [0, 2]] * .25); nl = len(Lt)
    for i in range(len(S) - 1):
        for j in range(nl - 1):
            if keep[i, j] or keep[i + 1, j] or keep[i, j + 1] or keep[i + 1, j + 1]:
                q = [b + i * nl + j, b + i * nl + j + 1, b + (i + 1) * nl + j + 1, b + (i + 1) * nl + j]
                vis.T['rock_loose'] += [q[0], q[1], q[2], q[0], q[2], q[3]]
    Vv = np.asarray(vis.V); t = vis.T['rock_loose']
    for k in range(0, len(t), 3):
        i, j, l = t[k:k + 3]
        if np.cross(Vv[j] - Vv[i], Vv[l] - Vv[i])[1] < 0: t[k + 1], t[k + 2] = l, j
    Nn = np.zeros_like(Vv)
    for k in range(0, len(t), 3):
        i, j, l = t[k:k + 3]; Nn[[i, j, l]] += np.cross(Vv[j] - Vv[i], Vv[l] - Vv[i])
    vis.N = nz(np.where(np.linalg.norm(Nn, axis=1, keepdims=True) > 0, Nn, UP)).tolist()
    col.V = list(vis.V); col.N = list(vis.N); col.UV = list(vis.UV); col.T['c'] = list(t)
    for k in range(70):                                                                                  # boulders on flanks/top
        s = float(rng.uniform(12, 29)); l = float(rng.uniform(-22, -7))
        i = int(round((s - S[0]) / .5)); j = int(round((l - Lt[0]) / .5))
        if not keep[i, j]: continue
        size = float(np.clip(rng.lognormal(-1.1, .45), .12, .7)); c = G[i, j] + np.array([0, -.25 * size, 0])
        lump(vis, c, size, rng, 'rock_loose')
    samples = []
    for s, l in ((13.5, -8), (20, -10), (26, -9), (18, -15), (24, -18)):
        x, z = world(s, l); samples += [round(float(x), 2), round(float(z), 2), round(float(terrain(x, z)), 3)]      # flat x,z,y
    stats['heap_tris'] = vis.tris(); stats['heap_top'] = round(float(terrain(*world(20, -4.5))) + .12, 2)
    return vis, col, samples


# ============================================================================================================== main
def main():
    rng = np.random.default_rng(306); (OUT / 'Meshes').mkdir(parents=True, exist_ok=True)
    V, main_p, branch_p, recess_p, ext, portal = CD.load()
    geo = json.loads(GEO.read_text(encoding='utf-8'))
    lanterns = [np.array([q['x'], FLOOR, q['z']]) for q in geo['lights']]
    path = Path3(np.vstack([main_p, ext[1:]]))
    stats = defaultdict(float); ck = Chunks()
    # turns / junctions along the path
    ends = [np.asarray(x) for x in (branch_p[0], branch_p[-1], recess_p[0])]
    turns, windows = [], []
    for k in range(1, len(path.P) - 1):
        a, b, c = path.P[k - 1], path.P[k], path.P[k + 1]
        din = nz((b - a)[[0, 2]]); dout = nz((c - b)[[0, 2]])
        junction = any(np.linalg.norm((b - e)[[0, 2]]) < .6 for e in ends)
        if junction or float(din @ dout) < math.cos(math.radians(12)):
            turns.append(float(path.S[k])); tr = TRIM_JUNCTION if junction else TRIM_TURN
            windows.append((path.S[k] - tr, path.S[k] + tr, 0))
    windows.append((-1.0, 1.0, 0))                                                      # start: the spawn spot itself
    ore = [o for o in CD.ore_specs(main_p)]
    g0, g1 = ext[0], ext[-1]; gd = nz((g1 - g0)[[0, 2]]); right = np.array([gd[1], -gd[0]])
    for f, side in ((.35, 1), (.62, -1)):
        q = g0 + (g1 - g0) * f; ore.append(dict(origin=[float(q[0]), FLOOR + 1.8, float(q[2])], dir=[float(right[0] * side), float(right[1] * side)]))
    for o in ore:                                                                          # keep every #297 seam in view
        u, d = path.project(np.array(o['origin'])); _, t, n = path.frame(u)
        side = 1 if (o['dir'][0] * n[0] + o['dir'][1] * n[2]) > 0 else -1
        if d < 3: windows.append((u - 2.5, u + 2.5, side))
    evidence = np.array([geo['evidence']['x'], FLOOR, geo['evidence']['z']])
    ue, _ = path.project(evidence); windows.append((ue - 2.5, ue + 2.5, 0))
    pb, tb, _ = path.frame(LOADED_CART_U); hlb, hrb, _, _ = CD.section(V, pb, tb); bay = 1 if (hrb or 0) >= (hlb or 0) else -1
    windows.append((LOADED_CART_U - BAY_HALF, LOADED_CART_U + BAY_HALF, bay))
    clear = [(LOADED_CART_U - CART_CLEAR, LOADED_CART_U + CART_CLEAR)] + [tuple(r) for r in OPEN_ROOMS]
    windows += [(a, b, 0) for a, b in OPEN_ROOMS]
    faces = packwalls(V, path, ck, windows, rng, stats)
    sets = timber_sets(V, path, ck, faces, turns, lanterns, rng, stats, clear)

    # -- carts / spur / stops ------------------------------------------------------------------------------------------
    carts = []
    for lod in (0, 1):
        for loaded in (False, True):
            m = cart(lod, loaded, np.random.default_rng(3061 + loaded)); name = f'MineCart_{"Loaded" if loaded else "Empty"}'
            m.name = f'{name}_LOD{lod}'; m.export(OUT / 'Meshes' / f'{name}_LOD{lod}.json')
            if lod == 0: stats[name + '_tris'] = m.tris()
    def place_cart(kind, u, lateral, dy, pitch, dyaw, roll, snap, note):
        p, t, n = path.frame(u); q = p + n * lateral; yaw = math.degrees(math.atan2(t[0], t[2]))
        carts.append(dict(mesh=kind, local=r3([q[0], FLOOR + dy, q[2]]), euler=r3([pitch, yaw + dyaw, roll]), snap=snap, collider='box', note=note))
        return p, t, n
    rail_top = .235
    p0, t0, n0 = place_cart('MineCart_Empty', 6.2, -1.62, .98, 4, 11, 180, 'ground', 'overturned by the start (seen from the spawn)')
    # #306 check: one spill mesh spanning the start and the blast room touched 5 lights (Mobile renderer: 4 per object),
    # so each spill is its own single; the same for the spur track and the start stop block
    spills = MB('CartSpill')
    spill(spills, p0 + n0 * -1.1 - t0 * .4, -n0 * .3 + t0, rng)
    spills.export(OUT / 'Meshes' / 'CartSpill_LOD0.json')
    spills = MB('CartSpillDerail')
    # #306 check: at -0.55 m the derailed cart left a 0.96 m lane; the blast room is also the tutorial arena (OPEN_ROOMS),
    # so the cart lies against the left wall (-2.7 m, swung 34 deg) and keeps the floor clear
    ud = max(ue - 3.4, 0); pd, td, nd = place_cart('MineCart_Loaded', ud, -2.7, .12, 2, 34, -9, 'ground', 'derailed by the blast (evidence, against the wall)')
    spill(spills, pd + nd * -2.1 + td * .6, td + nd * -.4, rng, 26, 1.1)
    # #306 walk-cave: the campaign MainPath runs down the drift centre, so the loaded cart is parked on the side spur below
    spills.export(OUT / 'Meshes' / 'CartSpillDerail_LOD0.json')
    track = MB('MineSpur')
    spur = np.array([branch_p[0], branch_p[0] + nz(branch_p[1] - branch_p[0]) * 9.5]); spur[:, 1] = FLOOR
    for lod in (0,): CD.track(track, spur, lod)
    sd = nz(spur[1] - spur[0]); sm = spur[0] + sd * 5.2; syaw = math.degrees(math.atan2(sd[0], sd[1]))
    carts.append(dict(mesh='MineCart_Loaded', local=r3([sm[0], FLOOR + rail_top, sm[2]]), euler=r3([0, syaw, 0]), snap='none', collider='box', note='loaded, parked on the side spur (off the drift lane)'))
    stop_block(track, spur[-1] + nz(spur[-1] - spur[0]) * .5, nz(spur[-1] - spur[0]))
    track.export(OUT / 'Meshes' / 'MineSpur_LOD0.json')
    head = MB('MineStartStop'); ps, ts, _ = path.frame(0.0); stop_block(head, ps - ts * .75, -ts)
    head.export(OUT / 'Meshes' / 'MineStartStop_LOD0.json')
    stop = MB('StopBlock'); stop_block(stop, np.zeros(3), np.array([0, 0, 1.])); stop.export(OUT / 'Meshes' / 'StopBlock_LOD0.json')

    # -- drainage by the deep_water sound (seg 1, right side) ------------------------------------------------------------
    dw = np.array([3540.0, FLOOR, 1788.0]); udw, _ = path.project(dw); pdw, tdw, ndw = path.frame(udw)
    fr = faces[1].get(round(round(udw * 2) / 2, 1)); lat = min(2.05, (fr - .7) if fr else 2.05)
    drain = MB('Drainage'); ta = pdw + ndw * lat - tdw * 7.2; tb = pdw + ndw * lat - tdw * .9
    trough(drain, ta, tb, .46, .34); drain.export(OUT / 'Meshes' / 'Drainage_LOD0.json')
    sump = pdw + ndw * (lat - .15) + tdw * .1
    # -- floor patches (C# builds them on the floor collider) + fuses --------------------------------------------------
    patches = [dict(kind='water', local=r3(sump), radius=.62, seed=1), dict(kind='water', local=r3(sump + tdw * 1.5 - ndw * .5), radius=.35, seed=2)]
    for q, rad in ((p0 + n0 * -1.3, 1.5), (pd + nd * -.8 + td * .5, 1.3), (sump - tdw * 3.2, .7)):
        patches.append(dict(kind='coal', local=r3(q), radius=rad, seed=len(patches)))
    fuses = []; flat = lambda pts: [x for q in pts for x in r3(q)]                      # JsonUtility: no nested arrays
    pe, te, ne = path.frame(ue); fuses.append(dict(frame='mine', points=flat(pe + ne * 1.9 + te * s + ne * .25 * math.sin(s * 1.3) for s in np.arange(-4.5, 1.6, .5))))
    fuses.append(dict(frame='mine', points=flat(pe - ne * 1.7 + te * s * .8 - ne * .2 * math.sin(s) for s in np.arange(-2.0, 3.1, .5))))

    # -- wall accents: coal seams / pick marks (ray-fitted in Unity) --------------------------------------------------
    wall = lambda u, side, y, length, tilt=0.0, width=.3, strands=2: (lambda p, t, n: dict(origin=r3([p[0], FLOOR + y, p[2]]), dir=r3([(n * side)[0], (n * side)[2]]), length=length, tilt=tilt, width=width, strands=strands))(*path.frame(u))
    coal = [wall(4.0, -1, 2.95, 6.0, 3, .34, 3), wall(7.5, 1, 2.75, 5.0, -4, .26, 2), wall(24.0, -1, 3.0, 6.5, 2, .3, 2),
            wall(62.0, 1, 2.85, 7.0, -3, .32, 3), wall(100.0, -1, 2.9, 6.0, 5, .28, 2), wall(148.0, 1, 3.05, 6.5, -2, .3, 2),
            wall(170.0, -1, 2.8, 7.0, 4, .34, 3), wall(188.0, 1, 2.95, 5.5, -5, .26, 2)]
    face_d = wall_hit(V, np.array([path.P[0][0], FLOOR + 1.2, path.P[0][2]]), -nz(path.P[1] - path.P[0]) * np.array([1, 0, 1])) or 4.5
    back = -nz((path.P[1] - path.P[0]) * np.array([1, 0, 1]))
    sf = path.P[0].copy(); sf[1] = FLOOR
    coal += [dict(origin=r3(sf + UP * 1.25), dir=r3([back[0], back[2]]), length=3.8, tilt=6, width=.3, strands=3),
             dict(origin=r3(sf + UP * 2.2), dir=r3([back[0], back[2]]), length=3.0, tilt=-4, width=.22, strands=2)]
    rec = recess_p[-1].copy(); rin = nz((recess_p[-1] - recess_p[-2]) * np.array([1, 0, 1])); rec[1] = FLOOR
    coal.append(dict(origin=r3(rec + UP * 1.4), dir=r3([rin[0], rin[2]]), length=3.2, tilt=-7, width=.3, strands=2))
    picks = [dict(origin=r3(sf + UP * .3), dir=r3([back[0], back[2]]), width=4.6, height=2.3, angle=52, seed=1),
             dict(origin=r3(rec + UP * .3), dir=r3([rin[0], rin[2]]), width=3.6, height=2.2, angle=-48, seed=2)]
    for u, side, y in ((3.0, -1, 2.65), (5.0, 1, 2.65), (9.0, -1, 2.7)):             # above the packwall top
        p, t, n = path.frame(u); picks.append(dict(origin=r3([p[0], FLOOR + y, p[2]]), dir=r3([(n * side)[0], (n * side)[2]]), width=3.0, height=1.0, angle=50 * side, seed=int(u)))
    for f, side in ((.15, 1), (.25, -1), (.5, 1), (.75, -1)):                       # the hand-driven portal drift
        u = path.L - SET_END - DENSE_END * (1 - f); p, t, n = path.frame(u)
        picks.append(dict(origin=r3([p[0], FLOOR + .45, p[2]]), dir=r3([(n * side)[0], (n * side)[2]]), width=3.4, height=2.0, angle=46 * side, seed=int(u * 3)))

    # -- miner traces (prefabs; ground snap unless noted) --------------------------------------------------------------
    props = []
    def prop(path_, q, yaw, size, snap='ground', pitch=0.0, roll=0.0, strip=False, note=''):
        props.append(dict(prefab=path_, frame='mine', local=r3(q), euler=r3([pitch, yaw, roll]), size=size, snap=snap, strip=strip, note=note))
    wy = math.degrees(math.atan2(back[0], back[2])); side0 = np.array([back[2], 0, -back[0]])
    wf = sf + back * (face_d - .7)
    # prefab axes are unknown offline: no lean (pitch/roll 0); the apply report prints each prefab's bounds to tune these rows
    prop(PREFAB + 'SM_MeHammer.prefab', wf + side0 * 1.2, wy + 90, .9, note='sledge by the work face')
    prop(PREFAB + 'SM_GalaeShovel.prefab', wf - side0 * 1.4, wy - 80, 1.5, note='shovel by the work face')
    prop(PREFAB + 'SM_FrameCarrier.prefab', wf - side0 * .2 - back * .55, wy + 170, 1.3, note='A-frame carrier')
    prop(PREFAB + 'SM_SamtaegiTray.prefab', wf + side0 * .45 - back * 1.0, wy + 30, .6, note='tray (삼태기)')
    prop(PREFAB + 'SM_StrawShoes_01.prefab', wf + side0 * .9 - back * 1.3, wy + 70, .3, note='straw shoes')
    prop(PREFAB + 'SM_Torch.prefab', wf - side0 * .9 - back * .9, wy + 20, .7, strip=True, note='spent pine torch (lights/particles stripped)')
    if sets:                                                                           # raincoat on the first set's left post
        s0 = sets[0]; p, t, n = path.frame(s0['u']); q = p + n * (s0['la'] + .2); q[1] = FLOOR + 1.55
        prop(PREFAB + 'SM_UjangRaincoat.prefab', q, math.degrees(math.atan2(n[0], n[2])), 1.1, snap='center', note='raincoat on a nail')
        s1 = sets[min(2, len(sets) - 1)]; p, t, n = path.frame(s1['u']); q = p + n * (s1['lb'] - .2); q[1] = FLOOR + 1.35
        prop(PREFAB + 'SM_Rope.prefab', q, math.degrees(math.atan2(-n[0], -n[2])), .5, snap='center', note='rope coil hung on a post')
    rq = rec - rin * 1.2; rs = np.array([rin[2], 0, -rin[0]]); ry = math.degrees(math.atan2(rin[0], rin[2]))
    prop(PREFAB + 'SM_Rope.prefab', rq + rs * 1.3, ry + 40, .5, note='rope coil (tool bay)')
    prop(PREFAB + 'SM_GalaeShovel.prefab', rq - rs * 1.5 + rin * .7, ry - 90, 1.5, note='shovel (tool bay)')
    prop(PREFAB + 'SM_YongduleLadle.prefab', sump + tdw * .7 + ndw * .1, math.degrees(math.atan2(tdw[0], tdw[2])) + 20, 2.2, note='용두레 at the sump')
    prop(PREFAB + 'SM_CucurbitBucket.prefab', sump - tdw * .75 - ndw * .2, 40, .4, note='두레박 by the sump')
    for k, (dq, sz) in enumerate(((ne * 1.6 - te * 1.0, 1.4), (-ne * 1.3 + te * 1.4, 1.1), (ne * .9 + te * 2.4, .8))):
        prop(BILLE + 'SM_Rockfall0%dA.prefab' % (k + 1), pe + dq, 37 * k, sz, note='blast rockfall at the evidence')
    prop(BILLE + 'SM_Rockfall02B.prefab', pd + nd * -3.6 - td * .8, 60, 1.0, note='rockfall by the derailed cart')

    # -- yard (world) ---------------------------------------------------------------------------------------------------
    op = json.loads((F297 / 'Cave/portal.json').read_text(encoding='utf-8'))['terrainOps'][0]
    A = np.array(op['a'], float); D = nz(np.array(op['b'], float) - A); R = np.array([D[1], -D[0]])
    H = np.fromfile(F297 / 'Stage/Surface/height.bytes', '<f4').reshape(1501, 1001).astype(np.float64)
    def terrain(x, z):
        fx = min(max(x / 4, 0), 1000); fz = min(max(z / 4, 0), 1500); ix = min(int(fx), 999); iz = min(int(fz), 1499); u = fx - ix; v = fz - iz
        return (H[iz, ix] * (1 - u) + H[iz, ix + 1] * u) * (1 - v) + (H[iz + 1, ix] * (1 - u) + H[iz + 1, ix + 1] * u) * v
    yo = np.array(portal['origin'], float); axis = lambda s, l: A + D * s + R * l
    heading = math.degrees(math.atan2(D[0], D[1]))                                    # yaw of the outward axis
    heap, heapc, heap_samples = spoil_heap(A, D, R, terrain, yo, rng, stats)
    heap.name = 'SpoilHeap_LOD0'; heap.export(OUT / 'Meshes' / 'SpoilHeap_LOD0.json'); heapc.name = 'SpoilHeap_Collision'; heapc.export(OUT / 'Meshes' / 'SpoilHeap_Collision.json')
    yard_meshes = []
    for mb in (windlass(), timber_stack(rng), hut(rng), notice(rng)):
        n0_ = mb.name; mb.name = n0_ + '_LOD0'; mb.export(OUT / 'Meshes' / f'{n0_}_LOD0.json')
    def ymesh(name, s, l, yaw, collider='box', note=''):
        x, z = axis(s, l); yard_meshes.append(dict(mesh=name, world=r3([x, terrain(x, z), z]), yaw=round(yaw, 2), collider=collider, note=note, s=s, l=l))
    ymesh('Windlass', 10.0, 6.8, heading, 'none', 'windlass over the shaft (Well prefab under it); #306 nav: kept 8 m clear of the mine_overlook__geumpyo_inn crossing at s 17-18')
    ymesh('TimberStack', 9.5, -6.4, heading, 'box', 'timber stack by the portal (clear of the tip track)')
    ymesh('HutCollapsed', 26.5, 7.6, heading - 90, 'none', 'collapsed miners hut, open side to the lane')
    ymesh('NoticeBoard', 5.4, 4.5, heading - 35, 'none', 'closure notice facing outward, turned 35 deg toward the lane')
    yard_props = []
    def yprop(prefab, s, l, yaw, size, note='', snap='ground', dy=0.0):
        x, z = axis(s, l); yard_props.append(dict(prefab=prefab, frame='world', world=r3([x, terrain(x, z) + dy, z]), euler=[0, round(yaw, 2), 0], size=size, snap=snap, strip=False, note=note, s=s, l=l))
    yprop(WELL, 10.0, 6.8, heading, 1.8, 'shaft collar (Jeju well)')
    yprop(PREFAB + 'SM_CucurbitBucket.prefab', 10.0 + .14, 6.8 + .1, heading + 30, .4, 'bucket on the windlass rope', snap='center', dy=.55)
    yprop(PREFAB + 'SM_Mortar.prefab', 7.2, 8.4, heading + 15, .7, '확돌 (ore mortar)')
    yprop(PREFAB + 'SM_Scale.prefab', 8.6, 9.2, heading - 60, 1.2, '저울')
    yprop(BILLE + 'SM_Rockfall03A.prefab', 4.2, -4.7, 20, 1.6, 'blast debris beside the portal (opening half-width ~2.45)')
    yprop(BILLE + 'SM_Rockfall04A.prefab', 3.6, 4.6, 140, 1.3, 'blast debris beside the portal')
    tip = [axis(s, l) for s, l in ((4.8, -1.2), (8.0, -2.6), (11.5, -4.8), (15.0, -7.2), (19.0, -8.8), (23.0, -9.8), (27.0, -10.3), (31.0, -10.6), (35.4, -10.8))]
    tip_track = dict(points=flat([x, terrain(x, z), z] for x, z in tip), gauge=.9)
    te_ = nz(tip[-1] - tip[-2]); ex, ez = tip[-1] + te_ * .6
    yard_meshes.append(dict(mesh='StopBlock', world=r3([ex, terrain(ex, ez), ez]), yaw=round(math.degrees(math.atan2(te_[0], te_[1])), 2), collider='none', note='tip stop block', s=35.4, l=-10.8))
    cx, cz = tip[-1] - te_ * .9
    yard_meshes.append(dict(mesh='MineCart_Empty', world=r3([cx, terrain(cx, cz), cz]), yaw=round(math.degrees(math.atan2(te_[0], te_[1])), 2), pitch=28.0, collider='box', note='tipping cart at the spoil tip', s=34.8, l=-10.8))
    fw = [axis(s, .9 * math.sin(s * .8) - 1.5) for s in np.arange(-1.5, 5.6, .5)]           # fuse out of the face onto the apron
    fuses.append(dict(frame='world', points=flat([x, terrain(x, z), z] for x, z in fw)))
    # clearance checks (lane, overlook, actors)
    actors = {'mine_beast/1': (3372.6, 2010.2), 'mine_fire/0': (3384.9, 2001.4)}
    warn = []
    for row in yard_meshes + yard_props:
        s, l = row['s'], row['l']
        if abs(l) < CLEAR_LANE and s > 6: warn.append(f"{row.get('mesh') or row.get('prefab')}: inside the lane (s {s}, l {l})")
        if math.hypot(s - OVERLOOK, l) < 8: warn.append(f"{row.get('mesh') or row.get('prefab')}: near the overlook")
        x, z = axis(s, l)
        for k, (ax_, az_) in actors.items():
            if math.hypot(x - ax_, z - az_) < 4: warn.append(f"{row.get('mesh') or row.get('prefab')}: {k} within 4 m")
    # sounds (clips from build_compact_cave_audio.py -> Art/Audio/Cave235)
    ps1, _, _ = path.frame(4.0); p4, _, _ = path.frame(100.0)
    sounds = [dict(clip='timber_creak', frame='mine', local=r3(ps1 + UP * 1.6), level=.13, radius=20, intermittent=False),
              dict(clip='timber_creak', frame='mine', local=r3(p4 + UP * 1.6), level=.11, radius=18, intermittent=False),
              dict(clip='sand_trickle', frame='mine', local=r3(pe + UP * 1.2), level=.12, radius=16, intermittent=False),
              dict(clip='bucket_drip', frame='mine', local=r3(sump + UP * .6), level=.18, radius=14, intermittent=True),
              dict(clip='distant_knock', frame='mine', local=r3(np.array([3490.0, FLOOR + 1.5, 1782.0])), level=.07, radius=34, intermittent=False),
              dict(clip='pulley_creak', frame='world', world=r3([*axis(19, 7)[:1], terrain(*axis(19, 7)) + 1.3, *axis(19, 7)[1:]]), level=.12, radius=18, intermittent=False)]

    walk = []                                                                            # walk-width probes for `check`: x,z,tx,tz
    for u in np.arange(1.0, path.L - 3, 2.0):
        p, t, n = path.frame(u); walk += [round(float(p[0]), 2), round(float(p[2]), 2), round(float(t[0]), 3), round(float(t[2]), 3)]
    # -- manifest -------------------------------------------------------------------------------------------------------
    rows = ck.export(OUT / 'Meshes', collision=('Packwall_Collision', 'Timbers_Collision'))
    manifest = dict(note=__doc__.strip().splitlines()[0], parent='mine', status='TEST', chunks=rows,
                    carts=carts, singles=[dict(mesh='MineSpur', local=[0, 0, 0]), dict(mesh='MineStartStop', local=[0, 0, 0]), dict(mesh='CartSpill', local=[0, 0, 0]), dict(mesh='CartSpillDerail', local=[0, 0, 0]), dict(mesh='Drainage', local=[0, 0, 0], collider='box')],
                    patches=patches, fuses=fuses, coal=coal, picks=picks, props=props,
                    crystals=dict(variants=['OreCrystals_A', 'OreCrystals_B', 'OreCrystals_C'], perSeam=2),
                    yard=dict(origin=r3(yo), heap=dict(mesh='SpoilHeap', collision='SpoilHeap_Collision', samples=heap_samples), meshes=yard_meshes, props=yard_props, tipTrack=tip_track),
                    sounds=sounds, lamps=dict(prefix='Gallery_Lantern_', keep=LAMPS_LIT),
                    walk=walk, floor=FLOOR,
                    sets=sets, warnings=warn)
    for k, v in enumerate('ABC'):                                                        # ore crystal clusters (cluster-local, +z = out of the wall)
        mb = MB('OreCrystals_' + v); cr = np.random.default_rng(3060 + k)
        for i in range(int(cr.integers(5, 9))):
            base = np.array([float(cr.normal(0, .12)), float(cr.normal(0, .09)), -.04]); d = nz(np.array([float(cr.normal(0, .45)), float(cr.normal(0, .45)), 1.0]))
            L = float(cr.uniform(.12, .32)) * (1.4 if i == 0 else 1); r = L * float(cr.uniform(.16, .24))
            mb.cyl(base, base + d * L, r, r * .85, 6, 'vein'); tipc = base + d * (L + r * 1.3); ring_b = base + d * L
            e1 = nz(np.cross(d, [0, 1, 0] if abs(d[1]) < .9 else [1, 0, 0])); e2 = np.cross(d, e1)
            ring = [ring_b + (math.cos(a) * e1 + math.sin(a) * e2) * r * .85 for a in np.linspace(0, 2 * math.pi, 7)[:-1]]
            for j in range(6): mb.poly([ring[j], ring[(j + 1) % 6], tipc], 'vein', (ring[j] + ring[(j + 1) % 6]) / 2 + tipc - 2 * ring_b)
        mb.name = f'OreCrystals_{v}_LOD0'; mb.export(OUT / 'Meshes' / f'OreCrystals_{v}_LOD0.json')
    stats['packwall_tris_LOD0'] = ck.tris('Packwall', 0); stats['packwall_tris_LOD1'] = ck.tris('Packwall', 1)
    stats['timber_tris_LOD0'] = ck.tris('Timbers', 0); stats['timber_tris_LOD1'] = ck.tris('Timbers', 1); stats['timber_tris_LOD2'] = ck.tris('Timbers', 2)
    stats['chunks'] = len(rows); stats['path_m'] = round(path.L, 1); stats['work_face_m'] = round(face_d, 2)
    manifest['stats'] = {k: (round(v, 1) if isinstance(v, float) else v) for k, v in stats.items()}
    (OUT / 'mine306.json').write_text(json.dumps(manifest, indent=1, ensure_ascii=False), encoding='utf-8')
    plan(path, faces, sets, carts, props, patches, OUT / 'plan.png')
    print(json.dumps(manifest['stats'], ensure_ascii=False)); print('warnings:', warn or 'none')


def plan(path, faces, sets, carts, props, patches, out):
    """Top view of the interior (for review): path, packwall faces, sets, carts, props, patches."""
    try: from PIL import Image, ImageDraw
    except ImportError: return
    P = path.P; lo = P[:, [0, 2]].min(0) - 12; hi = P[:, [0, 2]].max(0) + 12; sc = 6
    W_, H_ = (hi - lo) * sc; im = Image.new('RGB', (int(W_), int(H_)), 'white'); d = ImageDraw.Draw(im)
    X = lambda q: ((q[0] - lo[0]) * sc, (hi[1] - q[2]) * sc)
    d.line([X(q) for q in P], fill=(150, 150, 150), width=2)
    for side, fs in faces.items():
        for u, f in fs.items():
            p, t, n = path.frame(u); q = p + n * side * f; x, y = X(q); d.point((x, y), fill=(120, 90, 60))
    for s in sets:
        p, t, n = path.frame(s['u']); d.line([X(p + n * s['la']), X(p + n * s['lb'])], fill=(90, 50, 20) if s['dense'] else (170, 120, 70), width=1)
    for c in carts: x, y = X(c['local']); d.rectangle([x - 4, y - 4, x + 4, y + 4], outline=(20, 20, 160), width=2)
    for pr in props: x, y = X(pr['local']); d.ellipse([x - 2, y - 2, x + 2, y + 2], fill=(0, 130, 0))
    for pa in patches: x, y = X(pa['local']); r = pa['radius'] * sc; d.ellipse([x - r, y - r, x + r, y + r], outline=(0, 0, 0) if pa['kind'] == 'coal' else (0, 90, 200))
    im.save(out)


if __name__ == '__main__':
    main()
