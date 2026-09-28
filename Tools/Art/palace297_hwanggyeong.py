"""#297 황경 궁성 — legacy-dungeon palace around the #296 great hall (대전 60x50 TestOnly arena at (1860, 51.4, 3460)).

Site (measured): the 정문 stands on a ridge crest (65-69 m) above the plaza (62-65 m); behind it the ground falls north to a
48-50 m basin around the hall, with a hill east (east quarter, 55-72 m). Axis x = 1860, facing south. Terraces (cut-only
pads, terrain .06 m below the 박석): 정문 floor 65.2 -> outer court 61.0 -> middle court 56.2 -> 조정 49.3 (the 7 m 전문 축대).
Route (D297): plaza -> 정문 (3 홍예 + 2-storey 문루) -> stair down -> outer court -> 중문 -> 축대 stair -> middle court ->
전문 on the 조정 wall line: BARRED (국상 계엄; the 빗장 is lifted only from the 조정 side = shortcut) -> east side stair up
into the east quarter (궐내각사 lane) -> 3-storey 누각: climb to the 2nd floor -> timber bridge over the inner east wall
onto the 조정 east 행각 roof walk -> stair down the roof slope into the 조정 -> 월대 -> 대전. Rest (성황당) in the middle
court at the 전문 / east-stair fork: with the 전문 unbarred the try loop to the 대전 is ~30 s (LDB-CHECKPOINT).
조정 enclosure: 축대 balustrade + 전문 wall line (S), palace west wall (W), inner east wall on the bank top (E), inner north
wall behind the hall (N). Fixed #296 routes kept: the Hwanggyeong mountain trail passes the chamfered SW corner outside
the west wall; capital_center__palace enters the east quarter through the open east gate.
Order (Cheolong standard): pads + terrain-fitted placements on the #295 field -> HW._H = post-pad field -> walls, gates,
terraces, stairs, paths fitted to it. Court faces (축대, 석축 revetments on the cut banks, pad flat edges) sit on 4 m
terrain-grid lines so the triangulated Unity terrain meets their feet flat. Checks (layout.json checks): an offline
walk (dense route samples dropped onto the exported collision + post-pad terrain like walk-compound; rises, slopes,
blocking faces, head clearance), face exposure, stair/bridge clearances, the capital-wall join, NaN/empty meshes.
Outputs Finish297/Hwanggyeong/: layout.json, unity.json, Meshes/*.json. TEST.
"""
import copy, json, math, sys
from pathlib import Path
import numpy as np
sys.path.insert(0, str(Path(__file__).resolve().parent))
from hanok297 import MB, beam, nz
from hanok297_building import BuildingSpec, build_building, storey_route, grid_lines
import hanok297_wall as HW
from hanok297_wall import H, gatehouse_arch, stone_path, path_profile, chunks, multi_arch_gate, ramp_stair, gate_door, rest_altar
from hanok297_palace import palace_wall, wooltae, court_paving, terrace_wall, grand_stair, timber_stair, plank_walk, revetment
import surface297

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Art/World/Compact/Rebuild/Finish297/Hwanggyeong'
A296 = ROOT / 'Art/World/Compact/Rebuild/Architecture296'
AX = 1860.0
HALL = dict(centre=(1860.0, 51.4, 3460.0), size=(66.0, 56.0), entrance=[1860.0, 51.39, 3427.0], floor=51.39)
LEVEL = dict(gate=65.2, outer=61.0, middle=56.2, court=49.3)
EPS = .06                                                    # pad top below the paving (no terrain / 박석 z-fighting)
# Court faces (terrace 축대, 석축 revetments, pad flat edges) sit on 4 m terrain-grid lines: the Unity terrain is the 4 m
# grid triangulated, so a face mid-cell gets a sloped mound at its foot (the cell's far vertex is still high).
Z = dict(south=3160.0, outer0=3172.0, outer1=3247.5, t1=3252.0, middle1=3340.0, cross=3331.1, t2=3340.0, court1=3504.0,
         inner_n=3506.0, north=3598.5)                         # north = palace W/E wall ends, 1 m into the capital wall
XW, XE, XC0, XC1, XIE = 1800.0, 2030.0, 1804.0, 1916.0, 1919.5   # palace wall W/E, court pad edges = revetment faces W/E, inner east wall
ROW_W, ROW_E, PAVE_W = 1808.6, 1911.4, 99.0                    # 행각 row lines (eaves +-4.41 m clear the revetments), 박석 width
EAST_GATE = (3346.0, 3358.0)                                   # open east gate gap (capital_center__palace crosses z 3352)
CHAMFER = 8.0                                                  # SW corner chamfer: the mountain trail passes the corner diagonally
NUGAK = (1954.0, 3426.0)

TERRAIN_OPS = []; MESHES = []; STATS = {}; COLS = []


def export_all(fn, name, extra=None, kind='building', world=False, position=None, yaw=0.0):
    first = None
    for lod in (0, 1, 2):
        out = fn(lod); vis, col = out[0], out[1]
        vis.export(OUT / 'Meshes' / f'{name}_LOD{lod}.json')
        if lod == 0:
            col.export(OUT / 'Meshes' / f'{name}_Collision.json'); STATS[name] = dict(tris=vis.tris())
            if extra: STATS[name].update(extra(out))
            first = out
            COLS.append((name, col if world else col.transformed(yaw, position)))
        elif lod == 1: STATS[name]['tris_lod1'] = vis.tris()
        else: STATS[name]['tris_lod2'] = vis.tris()
    e = dict(name=name, kind=kind, world=world)
    if not world: e.update(position=[float(v) for v in position], yaw=float(yaw))
    MESHES.append(e)
    return first


def pad_op(cx, cz, hx, hz, top, yaw=0.0, blend=6.0):
    TERRAIN_OPS.append(dict(type='pad', centre=[float(cx), float(cz)], yaw=float(yaw), half=[float(hx), float(hz)], top=float(top), blend=float(blend)))


KIT = dict(
    haenggak=lambda n: BuildingSpec('haenggak', [3.0] * n, [3.4], col_h=3.0, style='official', roof='matbae', platform_h=.6, platform_margin=.45,
                                    stairs=(), walls=dict(front='open', back='plaster', left='plaster', right='plaster')),
    gate3=BuildingSpec('gate3', [3.6, 4.4, 3.6], [3.4, 3.4], col_h=4.2, style='palace', roof='palzak', platform_h=.9, platform_margin=1.3,
                       stairs=('front', 'back'), walls=dict(front='doors', back='doors', left='plaster', right='plaster'),
                       open_bays={('front', 1), ('back', 1)}, enterable=True),
    jeongmun_top=BuildingSpec('jeongmun_top', [3.4, 3.8, 4.4, 3.8, 3.4], [3.2, 3.2], col_h=3.4, style='palace', roof='palzak',
                              platform_h=0.0, platform_margin=0.0, stairs=(), walls=dict(front='rail', back='rail', left='rail', right='rail'), enterable=True,
                              storeys=[dict(h=2.9, inset=.9, balcony=False, walls=dict(front='window', back='window', left='plaster', right='plaster'))]),
    jeonmun_top=BuildingSpec('jeonmun_top', [2.8, 3.4, 2.8], [2.6, 2.6], col_h=3.0, style='palace', roof='palzak', platform_h=0.0, platform_margin=0.0,
                             stairs=(), walls=dict(front='rail', back='rail', left='rail', right='rail'), enterable=True),
    office5=BuildingSpec('office_5x2', [3.0] * 5, [3.0, 3.0], col_h=3.2, style='official', roof='matbae', platform_h=.7, platform_margin=.8,
                         walls=dict(front='doors', back='plaster', left='plaster', right='plaster')),
    office3=BuildingSpec('office_3x2', [3.0, 3.4, 3.0], [3.0, 3.0], col_h=3.2, style='official', roof='palzak', platform_h=.7, platform_margin=.8,
                         walls=dict(front='doors', back='window', left='plaster', right='plaster')),
    hall5=BuildingSpec('hall_5x3', [3.3, 3.6, 4.2, 3.6, 3.3], [3.0, 3.4, 3.0], col_h=4.0, style='palace', roof='palzak', platform_h=1.2, platform_margin=1.6,
                       enterable=True, floor='floor_brick'),
    # the kit's first interior flight starts .7 m inside the back wall at x = +-(L/2 - .95) and needs n x .26 m: with col_h 4.6
    # and a .9 m 2nd-floor inset it topped out at z = 5.22, past the 2nd-floor front wall (4.2) — not climbable. col_h 3.6 +
    # 2nd floor without inset lands it at 4.44 inside the floor (height still ~25 m). No 2nd-floor balcony rail: the open
    # front bay leads straight onto the bridge (the kit balcony rail is solid collision).
    nugak=BuildingSpec('nugak_3storey', [3.2, 3.8, 3.2], [3.2, 3.8, 3.2], col_h=3.6, style='palace', roof='palzak', platform_h=1.8, platform_margin=2.2,
                       stairs=('front', 'back'), walls=dict(front='doors', back='plaster', left='plaster', right='plaster'), open_bays={('front', 1), ('back', 1)},
                       enterable=True, interior_stairs=True,
                       storeys=[dict(h=4.0, inset=0.0, balcony=False, walls=dict(front='window', back='window', left='window', right='window')),
                                dict(h=3.4, inset=.7, balcony=True, walls='open')]),
    pavilion=BuildingSpec('pavilion_1x1', [4.2], [4.2], col_h=3.2, style='official', roof='palzak', platform_h=.8, platform_margin=.9, stairs=('front',),
                          walls='open', enterable=True),
)


def place(name, spec, x, z, yaw, level=None, lift=.3, placements=None):
    """Building on its pad: level given (court) -> platform from the court level; else fit (cut 60th percentile)."""
    s = copy.copy(spec); s.name = name
    if level is None:
        hx, hz = s.L / 2 + s.pm + .5, s.D / 2 + s.pm + .5; ca, sa = math.cos(math.radians(yaw)), math.sin(math.radians(yaw))
        u, v = np.meshgrid(np.linspace(-hx, hx, 9), np.linspace(-hz, hz, 9)); g = H(x + u * ca + v * sa, z - u * sa + v * ca)
        top = float(np.percentile(g, 60)) + lift; base = float(g.min()) - .5
        if float(g.max()) > top - .05: pad_op(x, z, hx + .6, hz + .6, top - .06, yaw)
    else:
        base = level - .4; top = level + s.ph
    s.ph = max(.3, top - base) if level is None else s.ph + .4
    out = export_all(lambda lod: build_building(s, lod), name, lambda o: dict(height=round(max(v[1] for v in o[0].V), 1), platform=round(s.ph, 2)),
                     position=[x, base, z], yaw=yaw)
    V = np.asarray(out[0].V); ridge = V[(np.abs(V[:, 0]) < max(.5, s.L / 2 - 1.5)) & (np.abs(V[:, 2]) < .6)]
    placements.append(dict(name=name, kind=spec.name, position=[x, base, z], yaw=yaw, platform=s.ph, floor=base + s.ph + .08,
                           footprint=[s.L + 2 * s.pm, s.D + 2 * s.pm], ridge=float(base + ridge[:, 1].max()) if len(ridge) else None, spec=s))
    return placements[-1]


def haenggak_line(prefix, a, b, level, toward, placements, bay=3.0):
    """Row of 행각 from a to b (XZ) at a court level; the open front faces `toward` (the court), plaster back outward."""
    a = np.asarray(a, float); b = np.asarray(b, float); L = float(np.linalg.norm(b - a)); d = (b - a) / L
    n_total = max(1, int(L / bay)); per = 12; k = 0; start = 0; rows = []
    base_yaw = math.degrees(math.atan2(d[0], d[1]))
    while start < n_total:
        n = min(per, n_total - start); c = a + d * bay * (start + n / 2)
        yaw = max((base_yaw + 90, base_yaw - 90), key=lambda y_: math.sin(math.radians(y_)) * (toward[0] - c[0]) + math.cos(math.radians(y_)) * (toward[1] - c[1]))
        rows.append(place(f'{prefix}_{k:02d}', KIT['haenggak'](n), float(c[0]), float(c[1]), yaw % 360, level=level, placements=placements)
                    | dict(span=[float(a[1] + d[1] * bay * start), float(a[1] + d[1] * bay * (start + n))]))
        placements[-1]['span'] = rows[-1]['span']
        start += n; k += 1
    return rows


def world_of(pl, q):
    """Building-local point -> world (Unity Euler(0, yaw, 0) about the placement origin)."""
    ya = math.radians(pl['yaw']); bx, by, bz = pl['position']
    return [bx + q[0] * math.cos(ya) + q[2] * math.sin(ya), by + q[1], bz - q[0] * math.sin(ya) + q[2] * math.cos(ya)]


def wall(name, a, b, ext=(0.0, 0.0), caps=(False, False), height=4.6):
    """Straight 궁장 a -> b, extended past corners (ext) so meeting walls overlap, capped at free ends (gates)."""
    a = np.asarray(a, float); b = np.asarray(b, float); d = (b - a) / np.linalg.norm(b - a); a = a - d * ext[0]; b = b + d * ext[1]
    P, _ = path_profile([tuple(a), tuple(b)], width=1.1, step=2.0); dummy = np.zeros(len(P)); parts = chunks(P, dummy, 50.0); info = []
    for c, (Pc, yc, s0) in enumerate(parts):
        nm = f'{name}_{c:02d}'; cp = (caps[0] and c == 0, caps[1] and c == len(parts) - 1)
        out = export_all(lambda lod: palace_wall(nm, Pc, height=height, lod=lod, s0=s0, caps=cp), nm,
                         lambda o: dict(length=round(o[2]['length'], 1), top=[round(v, 2) for v in o[2]['top']]), kind='wall', world=True)
        info.append(out[2])
    return dict(name=name, a=a.tolist(), b=b.tolist(), length=float(np.linalg.norm(b - a)), top=[min(i['top'][0] for i in info), max(i['top'][1] for i in info)])


def bridge(name, a, b, lod=0):
    """Timber 누다리: two beams, boards, rails; collision = the deck plane (2 m)."""
    a = np.asarray(a, float); b = np.asarray(b, float); br = MB(name); brc = MB(name + '_collision')
    d = b - a; u = nz(np.array([d[0], 0, d[2]])); side = np.array([u[2], 0, -u[0]])
    for sx in (-1, 1): beam(br, a + side * sx * .9 + [0, -.2, 0], b + side * sx * .9 + [0, -.2, 0], .22, .3, 'wood_dark')
    nb = int(np.linalg.norm(d) / .5); yaw = math.degrees(math.atan2(u[0], u[2]))
    for i in range(nb):
        p = a + d * (i + .5) / nb; br.box(p.tolist(), [1.9, .08, .46], 'wood_board', yaw=yaw, faces='xXyYzZ', uvscale=1.0)
    for sx in (-1, 1): beam(br, a + side * sx * 1.0 + [0, .95, 0], b + side * sx * 1.0 + [0, .95, 0], .08, .08, 'wood_dark')
    if lod < 2:
        for k in range(int(np.linalg.norm(d) / 2.4) + 1):
            p = a + d * min(1.0, k * 2.4 / np.linalg.norm(d))
            for sx in (-1, 1): beam(br, p + side * sx * 1.0, p + side * sx * 1.0 + [0, .95, 0], .07, .07, 'wood_dark')
    brc.quad((a - side).tolist(), (a + side).tolist(), (b + side).tolist(), (b - side).tolist(), 'c', [0, 1, 0])
    return br, brc, dict(length=float(np.linalg.norm(d)), grade=float(abs(d[1]) / max(1e-6, np.linalg.norm(d[[0, 2]]))))


# --- offline walk check: drop dense route samples onto the exported collision (+ terrain), flag steps/missing/blocked ---
class Collision:
    def __init__(self, meshes, extra_floors=()):
        tris = []
        for _, mb in meshes:
            V = np.asarray(mb.V, float); T = np.array([i for t in mb.T.values() for i in t], int).reshape(-1, 3)
            if len(T): tris.append(V[T])
        self.T = np.concatenate(tris); e1 = self.T[:, 1] - self.T[:, 0]; e2 = self.T[:, 2] - self.T[:, 0]
        nrm = np.cross(e1, e2); self.ny = np.abs(nrm[:, 1]) / np.maximum(np.linalg.norm(nrm, axis=1), 1e-12)
        self.lo = self.T.min(1); self.hi = self.T.max(1); self.floors = list(extra_floors)
        self.cell = 6.0; self.grid = {}
        i0 = np.floor(self.lo[:, [0, 2]] / self.cell).astype(int); i1 = np.floor(self.hi[:, [0, 2]] / self.cell).astype(int)
        for k in range(len(self.T)):
            for gx in range(i0[k, 0], i1[k, 0] + 1):
                for gz in range(i0[k, 1], i1[k, 1] + 1): self.grid.setdefault((gx, gz), []).append(k)

    def near(self, x0, z0, x1, z1):
        ks = set()
        for gx in range(int(math.floor(min(x0, x1) / self.cell)), int(math.floor(max(x0, x1) / self.cell)) + 1):
            for gz in range(int(math.floor(min(z0, z1) / self.cell)), int(math.floor(max(z0, z1) / self.cell)) + 1): ks.update(self.grid.get((gx, gz), ()))
        return np.fromiter(ks, int) if ks else np.zeros(0, int)

    def support(self, p, up=1.2, down=3.0):
        """Highest walkable surface (normal.y > .5) in [y - down, y + up] under p: collision, extra floors, terrain."""
        best = None; ks = self.near(p[0], p[2], p[0], p[2])
        if len(ks):
            T = self.T[ks]; ok = self.ny[ks] > .5; T = T[ok]
            if len(T):
                o = np.array([p[0], p[1] + up, p[2]]); dvec = np.array([0, -(up + down), 0])
                v0, e1, e2 = T[:, 0], T[:, 1] - T[:, 0], T[:, 2] - T[:, 0]; pv = np.cross(dvec, e2); det = np.sum(e1 * pv, 1)
                okd = np.abs(det) > 1e-12; inv = np.where(okd, 1 / np.where(okd, det, 1), 0); tv = o - v0
                u = np.sum(tv * pv, 1) * inv; qv = np.cross(tv, e1); v = np.sum(dvec * qv, 1) * inv; t = np.sum(e2 * qv, 1) * inv
                hit = okd & (u >= 0) & (v >= 0) & (u + v <= 1) & (t >= 0) & (t <= 1)
                if hit.any(): best = float(o[1] - t[hit].min() * (up + down))
        for (x0, x1, z0, z1, y) in self.floors:
            if x0 <= p[0] <= x1 and z0 <= p[2] <= z1 and p[1] - down <= y <= p[1] + up: best = y if best is None else max(best, y)
        g = float(H(p[0], p[2]))
        if p[1] - down <= g <= p[1] + up: best = g if best is None else max(best, g)
        return best

    def ceiling(self, p, lo=.3, hi=1.85):
        ks = self.near(p[0], p[2], p[0], p[2])
        if not len(ks): return False
        T = self.T[ks]; o = np.array([p[0], p[1] + lo, p[2]]); dvec = np.array([0, hi - lo, 0])
        v0, e1, e2 = T[:, 0], T[:, 1] - T[:, 0], T[:, 2] - T[:, 0]; pv = np.cross(dvec, e2); det = np.sum(e1 * pv, 1)
        okd = np.abs(det) > 1e-12; inv = np.where(okd, 1 / np.where(okd, det, 1), 0); tv = o - v0
        u = np.sum(tv * pv, 1) * inv; qv = np.cross(tv, e1); v = np.sum(dvec * qv, 1) * inv; tt = np.sum(e2 * qv, 1) * inv
        return bool(np.any(okd & (u >= 0) & (v >= 0) & (u + v <= 1) & (tt >= 0) & (tt <= 1)))

    def blocked(self, a, b, heights=(.55, 1.45)):
        """Blocking (steep) collision triangle crossing the segment a -> b at knee / chest height above the supports."""
        ks = self.near(a[0], a[2], b[0], b[2])
        if not len(ks): return False
        T = self.T[ks][self.ny[ks] <= .5]
        if not len(T): return False
        for h in heights:
            o = np.array([a[0], a[1] + h, a[2]]); dvec = np.array([b[0] - a[0], b[1] - a[1], b[2] - a[2]])
            v0, e1, e2 = T[:, 0], T[:, 1] - T[:, 0], T[:, 2] - T[:, 0]; pv = np.cross(dvec, e2); det = np.sum(e1 * pv, 1)
            okd = np.abs(det) > 1e-12; inv = np.where(okd, 1 / np.where(okd, det, 1), 0); tv = o - v0
            u = np.sum(tv * pv, 1) * inv; qv = np.cross(tv, e1); v = np.sum(dvec * qv, 1) * inv; t = np.sum(e2 * qv, 1) * inv
            if np.any(okd & (u >= 0) & (v >= 0) & (u + v <= 1) & (t >= 0) & (t <= 1)): return True
        return False


def walk_check(col, pts, step=.25):
    """Offline analogue of walk-compound: densify, drop every sample onto the highest support within +1.2/-3 m (like
    the Unity walker), then flag missing support, rises > .32 m between .25 m samples (step .3), slopes > 45 deg over
    .5 m, and blocking collision crossing the path at knee/chest height (1 m samples)."""
    P = np.asarray(pts, float); dense = []
    for i in range(len(P) - 1):
        k = max(1, int(math.ceil(np.linalg.norm(P[i + 1] - P[i]) / step)))
        dense += [P[i] + (P[i + 1] - P[i]) * j / k for j in range(k)]
    dense.append(P[-1]); S = []; miss = []
    for p in dense:
        y = col.support(p); S.append(np.array([p[0], p[1] if y is None else y, p[2]]))
        if y is None: miss.append([round(float(v), 2) for v in p])
    S = np.array(S); dy = np.diff(S[:, 1]); dxz = np.linalg.norm(np.diff(S[:, [0, 2]], axis=0), axis=1)
    steps = [[round(float(v), 2) for v in S[i + 1]] + [round(float(dy[i]), 2)] for i in np.where(np.abs(dy) > .32)[0]]
    steep = []
    for i in range(0, len(S) - 2, 2):
        run = dxz[i] + dxz[i + 1]; rise = S[i + 2, 1] - S[i, 1]
        if run > .1 and abs(rise) / run > 1.0 and abs(rise) > .32: steep.append([round(float(v), 2) for v in S[i]])
    blocked = []
    for i in range(0, len(S) - 4, 4):
        if col.blocked(S[i], S[i + 4]): blocked.append([round(float(v), 2) for v in S[i]])
    head = [[round(float(v), 2) for v in S[i]] for i in range(0, len(S), 2) if col.ceiling(S[i])]
    length = float(np.sum(np.linalg.norm(np.diff(S, axis=0), axis=1)))
    return dict(samples=len(S), length=round(length, 1), seconds_at_4_5=round(length / 4.5, 1), missing=miss[:6], n_missing=len(miss),
                steps=steps[:6], n_steps=len(steps), steep=steep[:6], n_steep=len(steep), blocked=blocked[:6], n_blocked=len(blocked), head=head[:6], n_head=len(head))


def main():
    OUT.mkdir(parents=True, exist_ok=True); (OUT / 'Meshes').mkdir(exist_ok=True)
    for f in (OUT / 'Meshes').glob('*.json'): f.unlink()
    layout = dict(note=__doc__.strip().splitlines()[0], walls=[], gates=[], buildings=[], paths=[], routes={}, markers={})
    placements = []
    prot_path = HW.SURF / 'protected.bytes'
    prot = (np.fromfile(prot_path, np.uint8).reshape(1501, 1001) > 0) if prot_path.exists() else None
    HW._H = HW.terrain().copy(); base_field = HW._H.copy()
    # --- 1. pads + terrain-fitted placements on the #295 field: they change the ground everything else is fitted to -----
    pad_op(AX, 3154.0, 18.0, 14.0, LEVEL['gate'] - EPS, blend=5.0)                                 # 정문 site + forecourt (flat to the z 3168 grid row)
    pad_op(AX, (Z['outer0'] + Z['t1']) / 2, (XC1 - XC0) / 2, (Z['t1'] - Z['outer0']) / 2, LEVEL['outer'] - EPS, blend=4.0)
    pad_op(AX, (Z['t1'] + Z['middle1']) / 2, (XC1 - XC0) / 2, (Z['middle1'] - Z['t1']) / 2, LEVEL['middle'] - EPS, blend=4.0)
    pad_op(AX, (Z['t2'] + Z['court1']) / 2, (XC1 - XC0) / 2, (Z['court1'] - Z['t2']) / 2, LEVEL['court'] - EPS, blend=4.0)
    for name, key, x, z, yaw in (('OfficeE1', 'office5', 1972.0, 3268.0, 270.0), ('OfficeE2', 'office3', 2002.0, 3300.0, 180.0),
                                 ('OfficeE3', 'office5', 1946.0, 3372.0, 90.0), ('OfficeE4', 'office3', 2004.0, 3395.0, 270.0),
                                 ('Donggung', 'hall5', 1978.0, 3528.0, 180.0), ('Pyeonjeon', 'hall5', AX, 3552.0, 180.0),
                                 ('PavilionN', 'pavilion', 1946.0, 3580.0, 180.0)):
        place(name, KIT[key], x, z, yaw, placements=placements)
    nugak = place('Nugak', KIT['nugak'], NUGAK[0], NUGAK[1], 270.0, placements=placements)
    # east side stair (middle court -> east quarter) set into the court pad's bank: a cut-only notch under its line
    s4a, s4b = (1913.0, 3317.0), (1932.0, 3317.0); s4y0 = LEVEL['middle'] + .02; s4y1 = float(H(s4b[0] + 1.0, s4b[1])) + .1
    TERRAIN_OPS.append(dict(type='notch', a=list(s4a), b=list(s4b), width=4.2, ya=s4y0 - .12, yb=s4y1 - .16, blend=2.5))
    # --- 2. ground after the pads (same op code as surface297 / the Unity surface pass) ----------------------------------
    HW._H, op_stats = surface297.apply(base_field.copy(), prot, [dict(o, source='Hwanggyeong') for o in TERRAIN_OPS])
    # --- 3. palace walls (궁장) on the post-pad ground ---------------------------------------------------------------------
    for spec in (('WallWest', (XW, Z['north']), (XW, Z['south'] + CHAMFER), (0, .65), (False, False)),
                 ('WallSW', (XW, Z['south'] + CHAMFER), (XW + CHAMFER, Z['south']), (.65, .65), (False, False)),
                 ('WallSouthW', (XW + CHAMFER, Z['south']), (AX - 15.3, Z['south']), (.65, 0), (False, False)),
                 ('WallSouthE', (AX + 15.3, Z['south']), (XE, Z['south']), (0, .65), (False, False)),
                 ('WallEastS', (XE, Z['south']), (XE, EAST_GATE[0]), (.65, 0), (False, True)),
                 ('WallEastN', (XE, EAST_GATE[1]), (XE, Z['north']), (0, 0), (True, False)),
                 ('WallCrossW', (XW, Z['cross']), (AX - 4.9, Z['cross']), (0, 0), (False, False)),
                 ('WallCrossE', (AX + 4.9, Z['cross']), (XIE, Z['cross']), (0, .65), (False, False)),
                 ('WallInnerE', (XIE, Z['cross']), (XIE, Z['inner_n']), (.65, .65), (False, False)),
                 ('WallInnerN', (XW, Z['inner_n']), (XIE, Z['inner_n']), (0, .65), (False, False))):
        layout['walls'].append(wall(spec[0], spec[1], spec[2], spec[3], spec[4]))
    # --- 4. 정문 (3 홍예 + 2-storey 문루) on its pad, stair down into the outer court ----------------------------------------
    g_y = LEVEL['gate']; g_top = g_y + 7.2
    gate_out = export_all(lambda lod: multi_arch_gate('Jeongmun_Base', (AX, g_y - .05, Z['south']), 0.0, top=g_top, lod=lod), 'Jeongmun_Base', kind='gate', world=True)
    place('Jeongmun_Top', KIT['jeongmun_top'], AX, Z['south'], 180.0, level=g_top + .14, placements=placements)
    # 박석 forecourt + passage floor at the gate level: the outer court pad's blend reaches the 4 m grid vertex at z 3168,
    # so the triangulated terrain under the passages' north end dips ~.6 m (a step at the stair head)
    export_all(lambda lod: court_paving('GateFloor', (AX, (3142.0 + Z['south'] + 8.3) / 2), 0.0, (31.0, Z['south'] + 8.3 - 3142.0), g_y, False, 0, lod),
               'GateFloor', kind='path', world=True)
    s1 = export_all(lambda lod: grand_stair('StairJeongmun', AX, Z['south'] + 8.0, g_y, LEVEL['outer'], 26.0, 1, lod), 'StairJeongmun',
                    lambda o: {k: v for k, v in o[2].items() if k in ('steps', 'riser', 'tread')}, kind='path', world=True)[2]
    # --- 5. 중문 on the outer court, 축대 + stair down to the middle court ---------------------------------------------------
    gspec = KIT['gate3']; n_st = max(2, int(math.ceil((gspec.ph + .4 + .08) / .18))); PZ = gspec.D / 2 + gspec.pm
    z_jm = Z['t1'] - 1.5 - PZ - n_st * .32
    jungmun = place('Jungmun', gspec, AX, z_jm, 180.0, level=LEVEL['outer'], placements=placements)
    t1 = export_all(lambda lod: terrace_wall('TerraceJungmun', XW + .6, XIE - 1.5, Z['t1'], LEVEL['middle'], LEVEL['outer'], 4.5, 1, [(AX - 4.3, AX + 4.3)], lod),
                    'TerraceJungmun', kind='path', world=True)[2]
    s2 = export_all(lambda lod: grand_stair('StairJungmun', AX, Z['t1'], LEVEL['outer'], LEVEL['middle'], 8.0, 1, lod), 'StairJungmun',
                    lambda o: {k: v for k, v in o[2].items() if k in ('steps', 'riser', 'tread')}, kind='path', world=True)[2]
    # --- 6. 전문: stone 홍예 in the 조정 wall line, barred from the 조정 side; 7 m 축대 + stair down into the 조정 -------------
    jn_ground = LEVEL['middle'] - EPS
    jn = export_all(lambda lod: gatehouse_arch('Jeonmun_Base', (AX, jn_ground - .05, Z['cross']), 0.0, 4.2, 4.6, 7.0, None, lod), 'Jeonmun_Base', kind='gate', world=True)[2]
    place('Jeonmun_Top', KIT['jeonmun_top'], AX, Z['cross'], 180.0, level=jn['top'] + .1, placements=placements)
    t2 = export_all(lambda lod: terrace_wall('TerraceJeonmun', XW + .6, XIE - 1.5, Z['t2'], LEVEL['court'], LEVEL['middle'], Z['t2'] - Z['cross'] - .5, 1, [(AX - 4.3, AX + 4.3)], lod),
                    'TerraceJeonmun', kind='path', world=True)[2]
    s3 = export_all(lambda lod: grand_stair('StairJeonmun', AX, Z['t2'], LEVEL['middle'], LEVEL['court'], 8.0, 1, lod), 'StairJeonmun',
                    lambda o: {k: v for k, v in o[2].items() if k in ('steps', 'riser', 'tread')}, kind='path', world=True)[2]
    layout['gates'] = [dict(name='Jeongmun', position=[AX, g_y, Z['south']], yaw=180.0, width=5.0, height=5.4, barred='', shortcut=False),
                       dict(name='Jungmun', position=[AX, LEVEL['outer'], z_jm], yaw=180.0, width=4.4, height=4.2, barred='', shortcut=False),
                       dict(name='Jeonmun', position=[AX, jn_ground, Z['cross']], yaw=0.0, width=4.2, height=4.6, depth=7.0, barred='inside', shortcut=True,
                            passage=jn['passage'], note='조정 wall line; the 빗장 is lifted from the 조정 side (shortcut back to the middle court)'),
                       dict(name='EastGate', position=[XE, float(H(XE, sum(EAST_GATE) / 2)), sum(EAST_GATE) / 2], yaw=270.0, width=EAST_GATE[1] - EAST_GATE[0],
                            height=0.0, barred='', shortcut=False, note='open gap; #296 capital_center__palace')]
    # 빗장 판문 on the 전문's 조정 face (inside = +z, swing inward onto the 축대 landing)
    doors = []
    for lod in (0, 1, 2):
        parts, dinfo = gate_door(4.2, 4.6, lod)
        for k, mb in parts.items(): mb.export(OUT / 'Meshes' / f'Door_Jeonmun_{k}_LOD{lod}.json')
        if lod == 0: STATS['Door_Jeonmun'] = dict(tris=sum(mb.tris() for mb in parts.values()))
    zd = 7.0 / 2 + dinfo['thick'] / 2 + .02; door_z = Z['cross'] + zd
    doors.append(dict(id='hwanggyeong_jeonmun_bar297', gate='Jeonmun', position=[AX, LEVEL['middle'], door_z], yaw=0.0, prompt='빗장', text='전문 빗장을 벗겼다.', **dinfo))
    layout['doors'] = doors
    # --- 7. 행각 (open to their court) -----------------------------------------------------------------------------------
    rows = {}
    for prefix, x, z0, z1, lv in (('HaengOuterW', ROW_W, Z['outer0'] + 4, Z['outer1'] - 1.5, 'outer'), ('HaengOuterE', ROW_E, Z['outer0'] + 4, Z['outer1'] - 1.5, 'outer'),
                                  ('HaengMiddleW', ROW_W, Z['t1'] + 8, Z['cross'] - 4, 'middle'), ('HaengMiddleE', ROW_E, Z['t1'] + 8, 3300.0, 'middle'),
                                  ('HaengCourtW', ROW_W, Z['t2'] + 22, 3500.0, 'court'), ('HaengCourtE', ROW_E, Z['t2'] + 22, 3500.0, 'court')):
        rows[prefix] = haenggak_line(prefix, (x, z0), (x, z1), LEVEL[lv], (AX, (z0 + z1) / 2), placements)
    # --- 7b. 석축 on the cut banks beside the courts (east: up to ~16 m at the 조정 SE corner; the inner east wall stands on
    # the east revetment's top edge). Beyond the 행각 eaves (row +- 4.41 m); the east-stair notch passes through a gap. -----
    xe_f, xw_f = XC1, XC0
    revs = []
    for nm, a, b, fc, lv, dep in (('RevetOuterE', (xe_f, Z['outer0'] - 1.0), (xe_f, Z['t1'] - 4.5), (-1, 0), 'outer', 4.0),
                                  ('RevetMiddleE_S', (xe_f, Z['t1']), (xe_f, s4a[1] - 2.7), (-1, 0), 'middle', 4.0),
                                  ('RevetMiddleE_N', (xe_f, s4a[1] + 2.7), (xe_f, Z['t2']), (-1, 0), 'middle', 4.0),
                                  ('RevetCourtE', (xe_f, Z['t2']), (xe_f, 3446.0), (-1, 0), 'court', 4.0),
                                  ('RevetOuterW', (xw_f, Z['outer0'] - 1.0), (xw_f, Z['t1'] - 4.5), (1, 0), 'outer', xw_f - XW - .55),
                                  ('RevetMiddleW', (xw_f, Z['t1']), (xw_f, Z['t2']), (1, 0), 'middle', xw_f - XW - .55),
                                  ('RevetCourtW', (xw_f, Z['t2']), (xw_f, 3420.0), (1, 0), 'court', xw_f - XW - .55),
                                  ('RevetOuterS_W', (xw_f, Z['outer0']), (AX - 13.65, Z['outer0']), (0, 1), 'outer', 3.0),
                                  ('RevetOuterS_E', (AX + 13.65, Z['outer0']), (xe_f, Z['outer0']), (0, 1), 'outer', 3.0)):
        o = export_all(lambda lod: revetment(nm, a, b, fc, LEVEL[lv], dep, lod), nm, lambda o: dict(height=[round(v, 1) for v in o[2]['height']]), kind='path', world=True)
        revs.append(dict(name=nm, a=list(a), b=list(b), height=[round(v, 1) for v in o[2]['height']]))
    layout['revetments'] = revs
    # face exposure: the post-pad ground .3 m in front of each face must be at its court level (else a mound hides the foot)
    expo = {}
    for r in revs:
        a, b = np.array(r['a']), np.array(r['b']); dd = (b - a) / np.linalg.norm(b - a); fn = np.array([-1.0, 0]) if r['name'].endswith(('E', 'E_S', 'E_N')) else (np.array([1.0, 0]) if 'S_' not in r['name'] else np.array([0, 1.0]))
        lv = LEVEL['outer'] if 'Outer' in r['name'] else LEVEL['middle'] if 'Middle' in r['name'] else LEVEL['court']
        q = a + np.outer(np.linspace(1.5, np.linalg.norm(b - a) - .5, 40), dd) + fn * .3
        expo[r['name']] = round(float(np.max(H(q[:, 0], q[:, 1]) - (lv - EPS))), 2)
    for nm, z, lv in (('TerraceJungmun', Z['t1'], 'middle'), ('TerraceJeonmun', Z['t2'], 'court')):
        xs_ = np.linspace(XC0 + .5, XC1 - .5, 60); expo[nm] = round(float(np.max(H(xs_, np.full(60, z + .3)) - (LEVEL[lv] - EPS))), 2)
    layout['faceExposure'] = expo
    # --- 8. 박석, 월대 (front stair toward the 조정, no rail on the hall side) ---------------------------------------------
    for nm, z0, z1, y, marks in (('PaveOuter', Z['outer0'] + 1.0, Z['outer1'], LEVEL['outer'], 0), ('PaveMiddle', Z['t1'] + 6.0, Z['cross'] - .6, LEVEL['middle'], 0),
                                 ('PaveCourt', 3344.0, 3416.0, LEVEL['court'], 6)):
        export_all(lambda lod: court_paving(nm, (AX, (z0 + z1) / 2), 0.0, (PAVE_W, z1 - z0), y, True, marks, lod), nm, kind='path', world=True)
    WT_Z = 3423.0; WT_H = (HALL['floor'] + .02 - .1 - LEVEL['court']) / 2        # tier-1 back edge 3427.5: 1 m under the hall threshold
    wt = export_all(lambda lod: wooltae('Wooltae', (AX, LEVEL['court'], WT_Z), 180.0, (46.0, 15.0), tiers=2, tier_h=WT_H, lod=lod, rail_sides=('front', 'left', 'right')),
                    'Wooltae', kind='path', world=True)[2]
    # --- 9. east quarter: stair up the bank, lane to the 누각, climb, bridge, roof walk, roof stair -------------------------
    export_all(lambda lod: ramp_stair('StairEast', [s4a, s4b], s4y0, s4y1, width=3.0, lod=lod), 'StairEast',
               lambda o: dict(length=round(o[2]['length'], 1), rise=round(o[2]['rise'], 1)), kind='path', world=True)
    nspec = nugak['spec']; local, up_y = storey_route(nspec); zs0 = grid_lines(nspec.bz)
    local.insert(2, [0, nspec.ph + .08, zs0[-1] - .7])                        # straight through the open front bay (not past a door post)
    SZ = nspec.D / 2 - nspec.storeys[0]['inset'] + .1                          # 2nd-floor slab edge (no balcony)
    climb = [world_of(nugak, [q[0], q[1] + .05, q[2]]) for q in local] + [world_of(nugak, [0, up_y + .05, SZ - .05])]
    climb[0][1] = float(H(climb[0][0], climb[0][2])) + .1
    lane_pts = [(s4b[0] + .3, s4b[1]), (1934.0, 3340.0), (1932.0, 3375.0), (1933.5, 3405.0), (climb[0][0], climb[0][2])]
    PP_, yy_ = path_profile(lane_pts, width=2.6); lane = []
    for c, (Pc, yc, s0) in enumerate(chunks(PP_, yy_, 40.0)):
        nm = f'EastLane_{c:02d}'
        out = export_all(lambda lod: stone_path(nm, width=2.6, lod=lod, P=Pc, y=yc, s0=s0), nm,
                         lambda o: dict(length=round(o[2]['length'], 1), rise=round(o[2]['rise'], 1), grade=round(o[2]['max_grade'], 2)), kind='path', world=True)
        lane += out[2]['points']
    layout['paths'].append(dict(name='EastLane', points=lane))
    # roof walk on the 조정 east 행각 whose ridge carries the bridge (one building: no ridge-end ornaments on the walk)
    carrier = [r for r in rows['HaengCourtE'] if min(r['span']) + 4 < NUGAK[1] < max(r['span']) - 4][0]
    stair_z = min(carrier['span']) + 8.0; rw_y = carrier['ridge'] + .5; x_r = ROW_E
    a_br = np.array(world_of(nugak, [0, up_y, SZ])); b_br = np.array([x_r, rw_y, NUGAK[1]])
    br = export_all(lambda lod: bridge('NugakBridge', a_br, b_br, lod), 'NugakBridge', lambda o: dict(length=round(o[2]['length'], 1), grade=round(o[2]['grade'], 3)), kind='path', world=True)[2]
    export_all(lambda lod: plank_walk('RoofWalk', [x_r, rw_y, NUGAK[1] + 1.2], [x_r, rw_y, stair_z - 1.0], lod=lod), 'RoofWalk', kind='path', world=True)
    rs_run = (rw_y - LEVEL['court'] - .05) / .57; rs_top = np.array([x_r - .7, rw_y, stair_z]); rs_bot = np.array([x_r - .7 - rs_run, LEVEL['court'] + .05, stair_z])
    rs = export_all(lambda lod: timber_stair('RoofStair', rs_top, rs_bot, 1.4, lod), 'RoofStair',
                    lambda o: {k: round(float(v), 3) for k, v in o[2].items()}, kind='path', world=True)[2]
    # clearances: roof stair / bridge over the 행각 roof, bridge over the inner east wall coping
    def roof_world(pl):
        V = np.asarray(build_building(pl['spec'], 0)[0].V); return np.array([world_of(pl, v) for v in V])
    RV = roof_world(carrier); sel = (np.abs(RV[:, 2] - stair_z) < 1.0) & (RV[:, 0] > rs_bot[0]) & (RV[:, 0] < x_r + .1)
    line = lambda x: np.minimum(rw_y, rw_y - (rs_top[0] - x) * (rw_y - rs_bot[1]) / rs_run)
    clear_stair = float(np.min(line(RV[sel, 0]) - .3 - RV[sel, 1])) if sel.any() else None
    selb = (np.abs(RV[:, 2] - NUGAK[1]) < 1.1) & (RV[:, 0] > x_r + .5)
    bline = lambda x: b_br[1] + (x - x_r) * (a_br[1] - b_br[1]) / (a_br[0] - x_r)
    clear_bridge_roof = float(np.min(bline(RV[selb, 0]) - .5 - RV[selb, 1])) if selb.any() else None
    wt_here = float(H(XIE, NUGAK[1])) + 4.6 + .55 + .22
    clear_bridge_wall = float(bline(XIE) - .5 - wt_here)
    # --- 10. 성황당 rest in the middle court at the 전문 / east-stair fork ---------------------------------------------------
    fx, fz = AX + 14.0, 3318.0; ax_, az_ = fx + 2.3, fz; altar_yaw = 270.0          # altar front (+z local) faces the feet (west)
    export_all(lambda lod: rest_altar(lod), 'RestAltar', kind='prop', world=False, position=[ax_, LEVEL['middle'], az_], yaw=altar_yaw)
    rest = dict(id='hwanggyeong_palace_rest297', label='황경 궁성 성황당', altar='RestAltar', position=[ax_, LEVEL['middle'], az_], radius=2.6,
                feet=[fx, LEVEL['middle'], fz], feetYaw=math.degrees(math.atan2(AX - fx, Z['cross'] - 3.5 - fz)), flames=[-.4, 1.17, -.1, .4, 1.17, -.1])
    layout['buildings'] = [{k: v for k, v in p.items() if k != 'spec'} for p in placements]
    # --- 11. routes ------------------------------------------------------------------------------------------------------
    court, mid, outer = LEVEL['court'] + .05, LEVEL['middle'] + .05, LEVEL['outer'] + .05
    jm_floor = jungmun['floor'] + .05; jmz = jungmun['position'][2]
    wt0, wt1 = WT_Z - 7.5, WT_Z - 4.5                                        # 월대 tier-0 / tier-1 front edges (yaw 180)
    n_wt = max(2, int(math.ceil(WT_H / .17))); fl = n_wt * .33
    hall_in = [[AX, court, 3409.5], [AX, court, wt0 - fl - .3], [AX, LEVEL['court'] + WT_H + .1, wt0 + .1], [AX, LEVEL['court'] + WT_H + .1, wt1 - fl - .05],
               [AX, wt['top'] + .03, wt1 + .15], [AX, wt['top'] + .03, 3425.8], HALL['entrance']]
    s3_foot = s3['foot'][2]
    court_axis = [[AX, court, s3_foot + .8], [AX, court, 3380.0], [AX, court, 3409.5]]
    s3_up = [[AX, court, s3_foot + .6], [AX, LEVEL['middle'] + .05, Z['t2'] - .1], [AX, mid, Z['t2'] - 1.2], [AX, mid, door_z], [AX, jn_ground + .05, Z['cross']],
             [AX, jn_ground + .05, Z['cross'] - 4.2]]
    feet = rest['feet']
    plaza = [[AX, float(H(AX, 3098.0)) + .05, 3098.0], [AX, float(H(AX, 3125.0)) + .05, 3125.0], [AX, g_y + .05, 3146.0], [AX, g_y + .05, 3154.0],
             [AX, g_y + .05, 3165.8], [AX, g_y + .05, Z['south'] + 8.2], [AX, outer, Z['south'] + 8.0 + s1['tread'] * s1['steps'] + .4], [AX, outer, 3205.0],
             [AX, outer, jmz - PZ - n_st * .32 - .4], [AX, jm_floor, jmz - PZ + .3], [AX, jm_floor, jmz], [AX, jm_floor, jmz + PZ - .3],
             [AX, outer, jmz + PZ + n_st * .32 + .35], [AX, outer, Z['t1'] - .15], [AX, mid, Z['t1'] + 9.6 + .35], [AX, mid, 3280.0]]
    layout['routes'] = dict(
        plaza_to_middle=plaza,
        middle_to_jeonmun_blocked=[[AX, mid, 3280.0], [AX, mid, 3320.0], [AX, jn_ground + .05, Z['cross'] - 4.0], [AX, jn_ground + .05, door_z - 1.4]],
        middle_to_nugak=[[AX, mid, 3280.0], [1885.0, mid, 3305.0], [s4a[0] - 1.2, mid, s4a[1]], [s4a[0] + .3, s4y0 + .05, s4a[1]]]
                        + [[s4b[0] - .2, s4y1 + .03, s4b[1]]] + [list(p) for p in lane],
        nugak_climb=climb,
        bridge_to_court=[[float(a_br[0]) - .3, float(a_br[1]) + .05, float(a_br[2])], [x_r + .4, rw_y + .05, NUGAK[1]], [x_r, rw_y + .05, NUGAK[1] - .6],
                         [x_r, rw_y + .05, stair_z + .2], [x_r - .5, rw_y + .05, stair_z], [float(rs_bot[0]) + .3, court + .1, stair_z],
                         [float(rs_bot[0]) - 1.5, court, stair_z]],
        court_to_hall=[[float(rs_bot[0]) - 1.5, court, stair_z], [AX + 8.0, court, 3406.0]] + hall_in,
        shortcut=hall_in[::-1] + court_axis[::-1] + s3_up + [[fx, mid, fz]],
        rest_to_hall_via_shortcut=[[fx, mid, fz]] + s3_up[::-1] + court_axis + hall_in)
    # fixed #296 routes crossing / skirting the palace (must stay walkable): slices of the exported polylines
    R296 = {r['id']: np.array([[p['x'], p['y'], p['z']] for p in r['points']]) for r in json.loads((A296 / 'Generated/routes.json').read_text(encoding='utf-8-sig'))['routes']}
    trail = R296['mountain_hwanggyeong_main']; m = (trail[:, 0] > 1740) & (trail[:, 0] < 1877) & (trail[:, 2] > 3083) & (trail[:, 2] < 3420)
    idx = np.where(m)[0]; tr = trail[idx[0]:idx[-1] + 1]; tr = tr[np.r_[np.arange(0, len(tr) - 1, 3), len(tr) - 1]]
    cc = R296['capital_center__palace']; cc = cc[len(cc) - 60:]; cc = cc[np.r_[np.arange(0, len(cc) - 1, 3), len(cc) - 1]]
    layout['routes']['fixed_mountain_trail'] = tr.tolist(); layout['routes']['fixed_capital_center_palace'] = cc.tolist()
    # --- 12. checks ------------------------------------------------------------------------------------------------------
    col = Collision(COLS, extra_floors=[(1828.0, 1892.0, 3426.5, 3490.0, HALL['floor'])])   # #296 hall floor (kept; threshold from the #296 approach top z 3426.5)
    checks = dict(walk={k: walk_check(col, v) for k, v in layout['routes'].items()})
    tri = sum(v['tris'] for v in STATS.values()); nan = []; empty = []
    for f in sorted((OUT / 'Meshes').glob('*.json')):
        d = json.loads(f.read_text(encoding='utf-8'))
        if not (np.all(np.isfinite(d['v'])) and np.all(np.isfinite(d['n'])) and np.all(np.isfinite(d['uv']))): nan.append(f.name)
        if not d['sub'] or sum(len(s_['t']) for s_ in d['sub']) == 0: empty.append(f.name)
    ch_a, ch_b = np.array([XW, Z['south'] + CHAMFER]), np.array([XW + CHAMFER, Z['south']])
    segd = lambda p, a, b: float(np.linalg.norm(p - (a + np.clip(np.dot(p - a, b - a) / np.dot(b - a, b - a), 0, 1) * (b - a))))
    trail_clear = min(min(segd(p, ch_a, ch_b), segd(p, np.array([XW, Z['north']]), ch_a), segd(p, ch_b, np.array([AX - 15.3, Z['south']]))) for p in trail[:, [0, 2]])
    cc_full = R296['capital_center__palace']
    cc_gap = [float(p[2]) for p in cc_full if abs(p[0] - XE) < 1.2]
    try:                                                                     # the palace W/E walls end inside the capital wall
        import capital297_wall as CW
        loop = [l for l in json.loads((A296 / 'gates.json').read_text(encoding='utf-8-sig'))['Loops'] if l['Id'] == 'capital296'][0]
        south = [g for g in loop['Gates'] if g['Id'] == 'south_gate'][0]
        saved = HW._H; HW._H, _ = surface297.apply(base_field.copy(), prot, surface297.load_ops())
        Pd, sd, _ = CW.fillet_loop(np.array([[p['x'], p['z']] for p in loop['Points']]), south['Edge'], south['Distance'], CW.FILLET)
        sp, yp = CW.loop_profile(Pd, sd, 1); HW._H = saved; cap = []
        for x, wn in ((XW, 'WallWest'), (XE, 'WallEastN')):
            q, _ = CW.project(Pd, sd, np.array([x, 3600.0])); yq = float(np.interp(q, sp, yp)); g_i = float(H(x, 3600.0 - (CW.SPEC['walk_w'] / 2 + 1.5)))
            base_i = min(g_i, yq) - .8; inner_top = 3600.0 - CW.SPEC['walk_w'] / 2; inner_foot = inner_top - .08 * (yq - base_i)
            pw_top = float(min(H(x - .85, Z['north']), H(x, Z['north']), H(x + .85, Z['north']))) + 4.6 + .55; face_at = inner_top - .08 * (yq - pw_top)
            cap.append(dict(x=x, capitalWalk=round(yq, 2), palaceWallTop=round(pw_top, 2), palaceWallEndZ=Z['north'], capitalInnerFaceZ_atPalaceTop=round(face_at, 2),
                            penetrationAtPalaceTop=round(Z['north'] - face_at, 2), penetrationAtFoot=round(Z['north'] - inner_foot, 2)))
    except Exception as ex:
        cap = [dict(error=str(ex))]
    checks.update(nan=nan, empty=empty, LOD0_total=tri, pads=len([o for o in TERRAIN_OPS if o['type'] == 'pad']), notches=len([o for o in TERRAIN_OPS if o['type'] == 'notch']),
                  opStats=op_stats, roofStairClearance=clear_stair, bridgeRoofClearance=clear_bridge_roof, bridgeInnerWallClearance=round(clear_bridge_wall, 2),
                  mountainTrailToWalls=round(trail_clear, 2), capitalCenterPalaceAtEastWallZ=[round(v, 2) for v in cc_gap], eastGateGap=list(EAST_GATE),
                  capitalWallJoin=cap, stairs=dict(S1=s1, S2=s2, S3=s3, roof=rs, bridge=br))
    # --- 13. markers, preview, outputs ----------------------------------------------------------------------------------
    top_floor = up_y + nspec.storeys[0]['h'] + .30 + .85 + max(1.1, (nspec.D / 2 - nspec.storeys[0]['inset'] - nspec.storeys[1]['inset']) * .30)
    lookout = world_of(nugak, [0, top_floor + 1.2, 0])
    layout['markers'] = dict(rest=dict(position=rest['position'], checkpoint=rest['id'], note='성황당 in the middle court at the 전문 / east-stair fork (30 s to the 대전 once the 전문 is unbarred)'),
                             lookout=dict(position=lookout, note='누각 3rd floor: palace and capital skyline'),
                             enemySlots=[[AX, LEVEL['outer'], 3205.0], [AX - 20, LEVEL['middle'], 3295.0], [1934.0, float(H(1934, 3360)), 3360.0], [AX + 22, LEVEL['court'], 3385.0]],
                             rewardSlots=[lookout, [1946.0, float(H(1946, 3580)), 3580.0]], testOnly=True)
    # Tools/Blender/preview_fortress297.py loads Meshes/<name>_LOD0.json for every walls/gates/paths entry: list the actual
    # world meshes there (line summaries stay in wallLines / gates / lanes)
    layout['wallLines'] = layout['walls']; layout['lanes'] = layout['paths']; layout['gateInfo'] = layout['gates']
    layout['walls'] = [dict(name=m['name']) for m in MESHES if m['world'] and m['kind'] == 'wall']
    layout['gates'] = [dict(name=m['name']) for m in MESHES if m['world'] and m['kind'] == 'gate']
    layout['paths'] = [dict(name=m['name']) for m in MESHES if m['world'] and m['kind'] == 'path']
    layout['buildings'] = layout['buildings'] + [dict(name=m['name'], kind=m['kind'], position=m['position'], yaw=m['yaw']) for m in MESHES if m['kind'] == 'prop']
    layout['terrainOps'] = TERRAIN_OPS; layout['stats'] = STATS; layout['checks'] = checks
    layout['preview'] = dict(terrain=[1740, 2080, 3060, 3620],
                             placeholders=[dict(name='GreatHall296', centre=[1860, 51.4, 3460], size=[66, 22, 56], yaw=0)],
                             cameras=[dict(name='plaza', eye=[1860, 70, 3095], target=[1860, 66, 3200]),
                                      dict(name='overview', eye=[1640, 190, 3060], target=[1880, 55, 3380], lens=28),
                                      dict(name='middle', eye=[1860, 58.5, 3262], target=[1860, 57, 3340]),
                                      dict(name='jeonmun_court', eye=[1860, 52, 3372], target=[1860, 57, 3334]),
                                      dict(name='east_stair', eye=[1895, 58, 3318], target=[1932, 64, 3317]),
                                      dict(name='nugak', eye=[1990, 64, 3456], target=[1954, 62, 3426]),
                                      dict(name='bridge', eye=[1956, 66, 3432], target=[1912, 56, 3426]),
                                      dict(name='roofwalk', eye=[1912, 58, 3432], target=[1900, 50, 3404]),
                                      dict(name='aerial', eye=[2060, 260, 3240], target=[1880, 50, 3420], lens=26)])
    (OUT / 'layout.json').write_text(json.dumps(layout, indent=1, ensure_ascii=False), encoding='utf-8')
    flat = lambda pts: [round(float(v), 3) for p in pts for v in p]
    palace_poly = [XW, Z['north'], XW, Z['south'] + CHAMFER, XW + CHAMFER, Z['south'], XE, Z['south'], XE, Z['north']]
    unity = dict(compound='Hwanggyeong', root='Finish297_Hwanggyeong', meshes=MESHES, hide=['Architecture296_Gates/palace296_PrecinctWall'],
                 clip=[dict(path='Architecture296_Venues/Approach_palace296', polygon=[round(float(v), 2) for v in palace_poly])],
                 markers=[dict(id='rest', kind='rest', checkpoint=layout['markers']['rest']['checkpoint'], position=layout['markers']['rest']['position']),
                          dict(id='lookout', kind='lookout', checkpoint='', position=layout['markers']['lookout']['position'])]
                         + [dict(id=f'enemy_{i}', kind='enemySlot', checkpoint='', position=p) for i, p in enumerate(layout['markers']['enemySlots'])]
                         + [dict(id=f'reward_{i}', kind='rewardSlot', checkpoint='', position=p) for i, p in enumerate(layout['markers']['rewardSlots'])],
                 gates=[dict(name=g['name'], position=g['position'], yaw=g['yaw'], width=g['width'], height=g['height'], barred=g['barred'], shortcut=g['shortcut']) for g in layout['gateInfo']],
                 routes=[dict(id=k, points=flat(v)) for k, v in layout['routes'].items()],
                 doors=doors, rest=rest,
                 terrainOps=[dict(o) for o in TERRAIN_OPS])
    (OUT / 'unity.json').write_text(json.dumps(unity, indent=1, ensure_ascii=False), encoding='utf-8')
    wsum = {k: (v['length'], v['seconds_at_4_5'], v['n_missing'], v['n_steps'], v['n_steep'], v['n_blocked'], v['n_head']) for k, v in checks['walk'].items()}
    print(json.dumps(dict(meshes=len(MESHES), LOD0_total=tri, pads=checks['pads'], notches=checks['notches'], nan=nan, empty=empty,
                          walk_len_s_missing_steps_steep_blocked_head=wsum, roofStairClearance=clear_stair, bridgeRoofClearance=clear_bridge_roof,
                          bridgeInnerWallClearance=checks['bridgeInnerWallClearance'], trailClear=checks['mountainTrailToWalls'], capitalJoin=cap,
                          ccAtEastWall=checks['capitalCenterPalaceAtEastWallZ'], stairs={k: v for k, v in checks['stairs'].items()}), indent=1, ensure_ascii=False, default=str))


if __name__ == '__main__':
    main()
