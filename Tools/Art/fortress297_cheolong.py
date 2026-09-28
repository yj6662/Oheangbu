"""#297 철옹 산성 — legacy-dungeon layout around the #296 armoury (군기전 60x45 TestOnly arena at (670, 301.6, 3440)).

Terrain truth (measured): the mountain is 30-70 % grade; only the armoury saddle (flattened by #296), the E summit
plateau (310 m) and the NE valley floor (.15-.25) are gentle. Route (D297, fitted to that terrain):
  approach outside the west wall -> MAIN GATE on the NW knoll (whole-fortress view) -> switchback DESCENT into the
  NE valley YARD (barracks, storehouses, stable, well, shrine = rest) -> GRAND STAIR up the valley head to the citadel
  north gate: BARRED (lifted only from inside = shortcut) -> climb the EAST WALL WALK up the ridge -> 동장대 (tall
  landmark) compound on the E summit -> citadel EAST GATE -> armoury. Optional: west wall walk to the NW 포루, south 암문.
Outputs Finish297/Cheolong/: layout.json (placements, paths, routes, markers) + Meshes/*.json. Everything is TEST;
the #296 precinct wall (4-point square) is replaced by the inner citadel wall.
"""
import copy, json, math, sys
from pathlib import Path
import numpy as np
sys.path.insert(0, str(Path(__file__).resolve().parent))
from hanok297_building import BuildingSpec, build_building, storey_route
import hanok297_wall as HW
from hanok297_wall import H, FortressWallSpec, fortress_wall, circuit_profile, gatehouse_arch, stone_path, path_profile, chunks, wall_stair, ramp_stair, gate_door, rest_altar
import surface297

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Art/World/Compact/Rebuild/Finish297/Cheolong'
ARCH = ROOT / 'Art/World/Compact/Rebuild/Architecture296/architecture.json'

# east side follows the summit plateau edge (x 806-810, 300-306 m) so the 장대 compound and officers' hall are inside
OUTER = [(606, 3592), (640, 3622), (680, 3640), (716, 3642), (752, 3630), (784, 3612), (800, 3572), (806, 3530), (808, 3492), (808, 3462),
         (810, 3430), (798, 3404), (772, 3394), (736, 3390), (700, 3388), (664, 3390), (628, 3394), (598, 3402), (582, 3430), (586, 3466),
         (596, 3500), (610, 3528), (608, 3562), (606, 3592)]
INNER = [(599, 3506), (636, 3506), (670, 3504), (704, 3503), (731, 3500), (733, 3462), (733, 3426), (734, 3394)]
JANGDAE = (776.0, 3444.0)                      # 동장대 on the E summit plateau (310 m), facing the armoury
YARD = (740.0, 3598.0)                         # NE valley floor (grade .15-.25)
HALL_ENTRANCE = [670.0, 301.64, 3470.5]        # #296 arena entrance (unchanged)

KIT = dict(
    barracks=BuildingSpec('barracks_7x1', [2.7] * 7, [3.2], col_h=2.8, style='military', roof='matbae', platform_margin=.6, round_cols=False,
                          walls=dict(front='doors', back='plaster', left='plaster', right='plaster')),
    storehouse=BuildingSpec('storehouse_3x2', [3.2, 3.2, 3.2], [3.0, 3.0], col_h=3.1, style='military', roof='matbae', platform_margin=.5,
                            round_cols=False, walls=dict(front='planks', back='planks', left='planks', right='planks')),
    stable=BuildingSpec('stable_5x1', [3.0] * 5, [3.4], col_h=2.7, style='vernacular', roof='matbae', platform_margin=.3, round_cols=False,
                        walls=dict(front='open', back='planks', left='planks', right='planks')),
    guardhouse=BuildingSpec('guardhouse_2x2', [2.8, 2.8], [2.6, 2.6], col_h=2.8, style='military', roof='palzak', platform_margin=.5, round_cols=False,
                            walls=dict(front='doors', back='plaster', left='window', right='plaster')),
    shrine=BuildingSpec('shrine_3x2', [2.6, 3.0, 2.6], [2.6, 2.6], col_h=3.0, style='official', roof='matbae', platform_margin=.8, stairs=('front',),
                        walls=dict(front='doors', back='plaster', left='plaster', right='plaster')),
    hwayakgo=BuildingSpec('hwayakgo_2x1', [2.8, 2.8], [3.2], col_h=2.9, style='military', roof='matbae', platform_margin=.5, round_cols=False,
                          walls=dict(front='stone', back='stone', left='stone', right='stone')),
    jangdae=BuildingSpec('jangdae_3x3', [3.2, 3.8, 3.2], [3.2, 3.8, 3.2], col_h=4.2, style='military', roof='palzak', platform_margin=1.6,
                         stairs=('front', 'back'), walls='open', enterable=True, interior_stairs=True,
                         storeys=[dict(h=3.3, inset=0.0, balcony=True, walls=dict(front='window', back='window', left='plaster', right='plaster'))]),
    munru=BuildingSpec('munru_3x2', [3.0, 3.6, 3.0], [2.8, 2.8], col_h=3.0, style='military', roof='palzak', platform_h=0.0, platform_margin=0.0,
                       stairs=(), walls=dict(front='rail', back='rail', left='rail', right='rail'), enterable=True),
    poru=BuildingSpec('poru_1x1', [3.4], [3.4], col_h=2.8, style='military', roof='palzak', platform_h=0.0, platform_margin=0.0, stairs=(),
                      walls=dict(front='rail', back='rail', left='rail', right='rail'), round_cols=False),
)


def approach():
    d = json.loads(ARCH.read_text(encoding='utf-8-sig'))
    a = [x for x in d['Arenas'] if x['Id'] == 'arsenal296'][0]
    return np.array([[p['x'], p['y'], p['z']] for p in a['Approach']])


def crossing(P, Q):
    """First intersection (XZ) of polyline P (circuit) with polyline Q (route): returns (fractional index in P, point)."""
    for j in range(len(Q) - 1):
        q0, q1 = Q[j, [0, 2]], Q[j + 1, [0, 2]]
        for i in range(len(P) - 1):
            p0, p1 = P[i], P[i + 1]; r = p1 - p0; s_ = q1 - q0; den = r[0] * s_[1] - r[1] * s_[0]
            if abs(den) < 1e-9: continue
            t = ((q0 - p0)[0] * s_[1] - (q0 - p0)[1] * s_[0]) / den; u = ((q0 - p0)[0] * r[1] - (q0 - p0)[1] * r[0]) / den
            if 0 <= t <= 1 and 0 <= u <= 1: return i + t, p0 + r * t
    return None, None


def segments_between(P, s, y, cuts):
    """Split a circuit into runs excluding gate spans cuts=[(s0, s1), ...] (closed loops are re-joined at the seam)."""
    keep = np.ones(len(P), bool)
    for a, b in cuts: keep &= ~((s > a) & (s < b))
    runs = []; cur = []
    for i in range(len(P)):
        if keep[i]: cur.append(i)
        elif cur: runs.append(cur); cur = []
    if cur: runs.append(cur)
    if len(runs) > 1 and runs[0][0] == 0 and runs[-1][-1] == len(P) - 1 and np.allclose(P[0], P[-1]):
        runs[0] = runs[-1][:-1] + runs[0]; runs.pop()
    return [(P[r], y[r]) for r in runs if len(r) > 2]


TERRAIN_OPS = []                               # cut/fill pads for the #297 terrain pass (applied in Unity via Surface297)
MESHES = []                                    # unity.json mesh manifest


def pad(spec, x, z, yaw, lift=.3, cut=True):
    """Cut-and-fill terrace: platform top = 60th-percentile footprint ground + lift; the uphill ground inside the pad is
    cut to just below the top (terrain op), the downhill side is the platform's stone face (buried .5 m)."""
    hx, hz = spec.L / 2 + spec.pm + .5, spec.D / 2 + spec.pm + .5
    ca, sa = math.cos(math.radians(yaw)), math.sin(math.radians(yaw))
    u, v = np.meshgrid(np.linspace(-hx, hx, 9), np.linspace(-hz, hz, 9))
    g = H(x + u * ca + v * sa, z - u * sa + v * ca)
    top = (float(np.percentile(g, 60)) if cut else float(g.max())) + lift
    base = float(g.min()) - .5
    if cut and float(g.max()) > top - .05:
        TERRAIN_OPS.append(dict(type='pad', centre=[x, z], yaw=yaw, half=[hx + .6, hz + .6], top=top - .06, blend=6.0))
    return base, top - base


def contour_yaw(x, z, toward):
    """Long axis on the contour (front faces up- or down-slope), whichever front faces `toward` better."""
    gx = (H(x + 3, z) - H(x - 3, z)) / 6; gz = (H(x, z + 3) - H(x, z - 3)) / 6
    t = math.degrees(math.atan2(toward[0] - x, toward[1] - z))
    if math.hypot(gx, gz) < 1e-3: return t
    a = math.degrees(math.atan2(gx, gz))
    return min((a, a + 180), key=lambda c: abs(((c - t) + 180) % 360 - 180)) % 360


def export_all(fn, name, stats, extra=None):
    for lod in (0, 1, 2):
        out = fn(lod); vis, col = out[0], out[1]
        vis.export(OUT / 'Meshes' / f'{name}_LOD{lod}.json')
        if lod == 0:
            col.export(OUT / 'Meshes' / f'{name}_Collision.json'); stats[name] = dict(tris=vis.tris())
            if extra: stats[name].update(extra(out))
            first = out
    return first


def front_axis(pl):
    """(stair-foot XZ, unit front direction) of a placed building: kit front = local +z, stairs n x .32 m (platform_stairs)."""
    x, y, z = pl['position']; ya = math.radians(pl['yaw']); d = np.array([math.sin(ya), math.cos(ya)])
    run = max(2, math.ceil(pl['platform'] / .18)) * .32
    r = pl.get('footprint', [6, 6])[1] / 2 + run
    return np.array([x + d[0] * r, z + d[1] * r]), d


def front_foot(pl, extra):
    """Point on the building's front axis `extra` metres beyond its stair foot."""
    sf, d = front_axis(pl); q = sf + d * extra
    return (float(q[0]), float(q[1]))


def main():
    OUT.mkdir(parents=True, exist_ok=True); (OUT / 'Meshes').mkdir(exist_ok=True)
    for f in (OUT / 'Meshes').glob('*.json'): f.unlink()
    layout = dict(note=__doc__.strip().splitlines()[0], walls=[], gates=[], buildings=[], paths=[], routes={}, markers={})
    stats = {}
    # --- 1. buildings on terrain-fitted platforms first: their pads change the ground everything else is fitted to --
    placements = []
    def place(name, key, x, z, toward, lift=.3, min_ph=.45):
        spec = copy.copy(KIT[key]); spec.name = name; yaw = contour_yaw(x, z, toward)
        base, ph = pad(spec, x, z, yaw, lift); spec.ph = max(min_ph, ph)
        export_all(lambda lod: build_building(spec, lod), name, stats, lambda o: dict(height=round(max(v[1] for v in o[0].V), 1), platform=round(spec.ph, 2)))
        placements.append(dict(name=name, kind=KIT[key].name, position=[x, base, z], yaw=yaw, platform=spec.ph, floor=base + spec.ph + .08,
                               footprint=[spec.L + 2 * spec.pm, spec.D + 2 * spec.pm], toward=list(toward)))
        MESHES.append(dict(name=name, kind='building', world=False, position=[x, base, z], yaw=yaw))
    for name, key, x, z, toward in (
            ('Barracks_A', 'barracks', 734, 3617, YARD), ('Barracks_B', 'barracks', 734, 3546, YARD),
            ('Storehouse_A', 'storehouse', 708, 3600, YARD), ('Storehouse_B', 'storehouse', 766, 3572, YARD),
            ('Stable', 'stable', 704, 3622, YARD), ('Shrine', 'shrine', 752, 3558, YARD),
            ('Guardhouse', 'guardhouse', 628, 3578, (660, 3600)), ('Hwayakgo', 'hwayakgo', 722, 3420, (670, 3440)),
            ('OfficerHall', 'shrine', 790, 3472, JANGDAE), ('NWPoru', 'poru', 618, 3532, (670, 3440))):
        place(name, key, x, z, toward)
    place('Jangdae', 'jangdae', JANGDAE[0], JANGDAE[1], (670, 3440), lift=.6, min_ph=2.2)
    byname = {p['name']: p for p in placements}
    # ground after the pads (same op code as surface297 / the Unity surface pass): walls, stairs and paths sit on it
    prot_path = HW.SURF / 'protected.bytes'
    prot = (np.fromfile(prot_path, np.uint8).reshape(1501, 1001) > 0) if prot_path.exists() else None
    HW._H, _ = surface297.apply(HW.terrain().copy(), prot, [dict(o, source='Cheolong') for o in TERRAIN_OPS])
    # --- 2. outer circuit + inner citadel wall ------------------------------------------------------------------------
    P, s, y = circuit_profile(OUTER, -1, max_grade=.62)          # walls step down into the valley (stairs on the walk)
    ap = approach(); ci, cpt = crossing(P, ap)
    s_gate = float(np.interp(ci, np.arange(len(s)), s))
    s_amun = float(s[np.argmin(np.linalg.norm(P - np.array([700, 3388]), axis=1))])
    for k, (Pk, yk) in enumerate(segments_between(P, s, y, [(s_gate - 5.3, s_gate + 5.3), (s_amun - 2.6, s_amun + 2.6)])):
        walk = []; total = 0.0
        for c, (Pc, yc, s0) in enumerate(chunks(Pk, yk)):
            name = f'OuterWall_{k}_{c:02d}'; spec = FortressWallSpec(name, None, outward=-1)
            out = export_all(lambda lod: fortress_wall(spec, lod, Pc, yc, s0), name, stats,
                             lambda o: dict(length=round(o[2]['length'], 1), walk=[round(v, 1) for v in o[2]['top']], outer=[round(v, 1) for v in o[2]['outer_height']]))
            MESHES.append(dict(name=name, kind='wall', world=True)); walk += out[2]['walk']; total += out[2]['length']
        layout['walls'].append(dict(name=f'OuterWall_{k}', kind='outer', walk=walk, length=total))
    Pi, si, yi = circuit_profile(INNER, -1, walk_w=2.6, min_outer=4.2, inner_rise=.9, max_grade=.62)
    s_ng = float(si[np.argmin(np.linalg.norm(Pi - np.array([670, 3504]), axis=1))])
    s_eg = float(si[np.argmin(np.linalg.norm(Pi - np.array([733, 3446]), axis=1))])
    for k, (Pk, yk) in enumerate(segments_between(Pi, si, yi, [(s_ng - 5.0, s_ng + 5.0), (s_eg - 4.0, s_eg + 4.0)])):
        walk = []; total = 0.0
        for c, (Pc, yc, s0) in enumerate(chunks(Pk, yk)):
            name = f'InnerWall_{k}_{c:02d}'; spec = FortressWallSpec(name, None, outward=-1, walk_w=2.6, parapet_h=1.3)
            out = export_all(lambda lod: fortress_wall(spec, lod, Pc, yc, s0), name, stats,
                             lambda o: dict(length=round(o[2]['length'], 1), walk=[round(v, 1) for v in o[2]['top']]))
            MESHES.append(dict(name=name, kind='wall', world=True)); walk += out[2]['walk']; total += out[2]['length']
        layout['walls'].append(dict(name=f'InnerWall_{k}', kind='inner', walk=walk, length=total))
    # --- 3. gates (홍예 passages; pavilions on the main gate and the barred citadel gate) ------------------------------
    for name, PP, ss, yy, sg, width, height, depth, pav, extra in (
            ('MainGate', P, s, y, s_gate, 4.6, 5.2, 9.0, 'munru', {}),
            ('SouthAmun', P, s, y, s_amun, 2.2, 2.7, 6.0, None, dict(shortcut=True, barred='inside', note='optional exit to the south slope')),
            ('InnerNorthGate', Pi, si, yi, s_ng, 4.2, 4.6, 7.0, 'munru', dict(shortcut=True, barred='inside', note='top of the grand stair; lifted from inside the citadel')),
            ('InnerEastGate', Pi, si, yi, s_eg, 3.2, 3.6, 6.0, None, dict(note='from the 장대 compound into the citadel'))):
        i = int(np.argmin(np.abs(ss - sg))); d = PP[min(i + 2, len(PP) - 1)] - PP[max(i - 2, 0)]
        yaw = math.degrees(math.atan2(d[0], d[1])) + 90; ground = float(H(PP[i, 0], PP[i, 1])); top = float(yy[i])
        c = (float(PP[i, 0]), ground - .05, float(PP[i, 1]))
        out = export_all(lambda lod: gatehouse_arch(name, c, yaw, width, height, depth, top, lod), name, stats)
        MESHES.append(dict(name=name, kind='gate', world=True))
        g = dict(name=name, position=[c[0], ground, c[2]], yaw=yaw, width=width, height=height, depth=depth, top=top, pavilion=pav, passage=out[2]['passage'])
        g.update(extra); layout['gates'].append(g)
    gates = {g['name']: g for g in layout['gates']}
    for g in layout['gates']:
        if not g['pavilion']: continue
        spec = KIT[g['pavilion']]; nm = g['name'] + '_Munru'
        export_all(lambda lod: build_building(spec, lod), nm, stats)
        placements.append(dict(name=nm, kind=spec.name, position=[g['position'][0], g['top'] + .12, g['position'][2]], yaw=g['yaw'], platform=0.0))
        MESHES.append(dict(name=nm, kind='building', world=False, position=[g['position'][0], g['top'] + .12, g['position'][2]], yaw=g['yaw']))
    layout['buildings'] = placements
    # --- 3b. 빗장 판문 on the barred gates: mounted on the inner face, swing inward; unbarred only from inside ---------
    doors = []
    for g, did, text in ((gates['InnerNorthGate'], 'cheolong_north_gate_bar297', '내성 북문 빗장을 벗겼다.'),
                         (gates['SouthAmun'], 'cheolong_south_amun_bar297', '남쪽 암문 빗장을 벗겼다.')):
        for lod in (0, 1, 2):
            parts, info = gate_door(g['width'], g['height'], lod)
            for k, mb in parts.items(): mb.export(OUT / 'Meshes' / f"Door_{g['name']}_{k}_LOD{lod}.json")
            if lod == 0: stats['Door_' + g['name']] = dict(tris=sum(mb.tris() for mb in parts.values()))
        zd = g['depth'] / 2 + info['thick'] / 2 + .02; ya = math.radians(g['yaw'])
        x, z = g['position'][0] + zd * math.sin(ya), g['position'][2] + zd * math.cos(ya)
        doors.append(dict(id=did, gate=g['name'], position=[x, float(H(x, z)), z], yaw=g['yaw'], prompt='빗장', text=text, **info))
        # small forecourt cut inside the door so the leaves swing clear of the slope (cut-only op, surface pass)
        cx_, cz_ = x + 2.0 * math.sin(ya), z + 2.0 * math.cos(ya)
        TERRAIN_OPS.append(dict(type='pad', centre=[cx_, cz_], yaw=g['yaw'], half=[g['width'] / 2 + 2.8, 2.4], top=float(H(x, z)) - .03, blend=3.5))
    layout['doors'] = doors
    # --- 4. 등성 계단: onto the east wall walk from the yard (NE) and down into the 장대 compound (summit) -------------
    stairs = {}
    for name, target, near in (('WallStair_NE', (797, 3586), (770, 3586)), ('WallStair_Summit', (807, 3458), JANGDAE)):
        s_top = float(s[np.argmin(np.linalg.norm(P - np.array(target), axis=1))])
        cand = [wall_stair(name, P, y, s_top, dd, -1, 3.0, lod=0) for dd in (-1, 1)]
        best = min(range(2), key=lambda k: math.hypot(cand[k][2]['foot'][0] - near[0], cand[k][2]['foot'][2] - near[1]))
        dd = (-1, 1)[best]
        out = export_all(lambda lod: wall_stair(name, P, y, s_top, dd, -1, 3.0, lod=lod), name, stats, lambda o: dict(run=round(o[2]['run'], 1)))
        MESHES.append(dict(name=name, kind='path', world=True)); stairs[name] = dict(out[2], s_top=s_top)
    # wall walk between the two stair tops (centre of the walk)
    a_, b_ = sorted((stairs['WallStair_NE']['s_top'], stairs['WallStair_Summit']['s_top']))
    idx = np.where((s >= a_) & (s <= b_))[0]
    walk_pts = [[float(P[i, 0]), float(y[i]) + .05, float(P[i, 1])] for i in idx[::2]]
    if stairs['WallStair_NE']['s_top'] > stairs['WallStair_Summit']['s_top']: walk_pts = walk_pts[::-1]
    # --- 5. stone paths / stairs -----------------------------------------------------------------------------------
    gx, gz = gates['MainGate']['passage'][1][0], gates['MainGate']['passage'][1][2]
    ng, eg = gates['InnerNorthGate'], gates['InnerEastGate']
    shrine_foot = front_foot(byname['Shrine'], 3.9)                # in front of the 성황당 altar (5b)
    ne_foot = (stairs['WallStair_NE']['foot'][0], stairs['WallStair_NE']['foot'][2])
    sm_foot = (stairs['WallStair_Summit']['foot'][0], stairs['WallStair_Summit']['foot'][2])
    jx, jz = JANGDAE
    paths = dict(
        Descent=[(gx, gz), (628, 3592), (648, 3606), (672, 3612), (694, 3612), (716, 3606)],
        YardLoop=[(716, 3606), (740, 3600), (752, 3586), shrine_foot],       # no hairpin: separate stone paths never overlap
        GrandStair=[(724, 3584), (708, 3560), (690, 3534), (676, 3514), (ng['position'][0], ng['position'][2] + 4)],
        RidgeAccess=[(764, 3582), ((764 + ne_foot[0]) / 2, (3582 + ne_foot[1]) / 2 - 2), ne_foot],
        CompoundLane=[sm_foot, (jx + 8, jz + 11), (jx - 8, jz + 10), (eg['position'][0] + 4, eg['position'][2])],
        CitadelLane=[(eg['position'][0] - 4, eg['position'][2]), (715, 3462), (700, 3480), (670, 3482)])
    for name, pts in paths.items():
        wide = 3.2 if name in ('GrandStair', 'Descent') else 2.4
        PP_, yy_ = path_profile(pts, width=wide); allpts = []
        for c, (Pc, yc, s0) in enumerate(chunks(PP_, yy_, 40.0)):
            nm = f'{name}_{c:02d}'
            out = export_all(lambda lod: stone_path(nm, width=wide, lod=lod, P=Pc, y=yc, s0=s0), nm, stats,
                             lambda o: dict(length=round(o[2]['length'], 1), rise=round(o[2]['rise'], 1), grade=round(o[2]['max_grade'], 2)))
            MESHES.append(dict(name=nm, kind='path', world=True)); allpts += out[2]['points']
        layout['paths'].append(dict(name=name, points=allpts))
    lane_end = (670.0, 3483.0)
    out = export_all(lambda lod: ramp_stair('HallStair', [lane_end, (HALL_ENTRANCE[0], HALL_ENTRANCE[2] + .6)], float(H(*lane_end)) + .12, HALL_ENTRANCE[1], lod=lod),
                     'HallStair', stats, lambda o: dict(length=round(o[2]['length'], 1), rise=round(o[2]['rise'], 1)))
    MESHES.append(dict(name='HallStair', kind='path', world=True)); layout['paths'].append(dict(name='HallStair', points=out[2]['points']))
    pts = {p['name']: p['points'] for p in layout['paths']}
    jd = byname['Jangdae']
    st = lambda n, rev=False: (lambda q: q[::-1] if rev else q)([[a, b + .05, c] for a, b, c in [stairs[n]['foot'], stairs[n]['top'], stairs[n]['landing']]])
    layout['routes'] = dict(
        approach=[a.tolist() for a in ap[:int(np.argmin(np.linalg.norm(ap[:, [0, 2]] - np.asarray(cpt), axis=1)))][::4]]
                 + [list(gates['MainGate']['passage'][0]), list(gates['MainGate']['passage'][1])],
        gate_to_yard=pts['Descent'] + pts['YardLoop'],
        grand_stair_blocked=pts['GrandStair'],
        ridge_to_jangdae=pts['RidgeAccess'] + st('WallStair_NE') + walk_pts + st('WallStair_Summit', True) + pts['CompoundLane'],
        jangdae_to_hall=pts['CompoundLane'] + pts['CitadelLane'] + pts['HallStair'] + [HALL_ENTRANCE],
        shortcut=[HALL_ENTRANCE] + pts['HallStair'][::-1] + [ng['passage'][1], ng['passage'][0]] + pts['GrandStair'][::-1] + pts['YardLoop'][:3][::-1])
    # --- 5b. 성황당 제단 = rest checkpoint at the fork where the yard meets the grand stair and the ridge access (a
    # 성황당 stands at the foot of a climb; LDB-CHECKPOINT: with the north-gate shortcut open the run back to the armoury
    # is ~30 s). Beside the walking line, not on it; respawn faces up the grand stair toward the barred gate's munru. --
    gs0 = np.array(paths['GrandStair'][0], float); yl2 = np.array(pts['YardLoop'][2], float)[[0, 2]]
    u = (yl2 - gs0) / np.linalg.norm(yl2 - gs0); side = np.array([u[1], -u[0]])
    if side[0] < 0: side = -side                                  # east of the line (open yard floor)
    fx, fz = (float(v) for v in gs0 + u * 4.0)
    ax_, az_ = (float(v) for v in gs0 + u * 4.0 + side * 2.3)
    dvec = -side; altar_yaw = math.degrees(math.atan2(dvec[0], dvec[1]))       # altar front (+z) faces the feet
    export_all(lambda lod: rest_altar(lod), 'RestAltar', stats)
    MESHES.append(dict(name='RestAltar', kind='prop', world=False, position=[ax_, float(H(ax_, az_)), az_], yaw=altar_yaw))
    rest = dict(id='cheolong_fortress_rest297', label='철옹 산성 성황당', altar='RestAltar', position=[ax_, float(H(ax_, az_)), az_], radius=2.6,
                feet=[fx, float(H(fx, fz)), fz], feetYaw=math.degrees(math.atan2(690 - fx, 3534 - fz)), flames=[-.4, 1.17, -.1, .4, 1.17, -.1])
    # 동장대 climb: front stair -> ground floor -> interior flight -> upper floor (the lookout over fortress + armoury)
    jspec = copy.copy(KIT['jangdae']); jspec.ph = jd['platform']; local, up_y = storey_route(jspec)
    ya = math.radians(jd['yaw']); bx, by_, bz_ = jd['position']
    W = lambda q: [bx + q[0] * math.cos(ya) + q[2] * math.sin(ya), by_ + q[1] + .05, bz_ - q[0] * math.sin(ya) + q[2] * math.cos(ya)]
    climb = [W(q) for q in local]; climb[0][1] = float(H(climb[0][0], climb[0][2])) + .1
    layout['routes']['jangdae_climb'] = climb
    # the retry run once the north gate is unbarred (LDB-CHECKPOINT 30 s rule — simulated time reported by walk-compound)
    # stays on the stone paths (on cross slopes a path's downhill edge is a terrace face, not a step)
    layout['routes']['rest_to_hall_via_shortcut'] = ([rest['feet']] + pts['GrandStair']
                                                    + [ng['passage'][0], ng['passage'][1]] + pts['HallStair'] + [HALL_ENTRANCE])
    layout['markers'] = dict(
        rest=dict(position=rest['position'], checkpoint=rest['id'], note='성황당 제단 at the grand-stair foot (yard / grand stair / ridge fork)'),
        lookout=dict(position=[jd['position'][0], jd['floor'] + 5.6, jd['position'][2]], note='장대 upper floor: whole fortress and the armoury'),
        enemySlots=[[734, float(H(734, 3600)), 3600], [790, float(H(790, 3540)), 3540], [612, float(H(612, 3540)), 3540], [700, 301.6, 3482]],
        rewardSlots=[[jd['position'][0], jd['floor'] + 5.6, jd['position'][2]], [618, float(H(618, 3532)), 3532], [730, float(H(730, 3395)), 3395]],
        testOnly=True)
    layout['terrainOps'] = TERRAIN_OPS
    layout['preview'] = dict(
        terrain=[520, 880, 3300, 3720],
        placeholders=[dict(name='Armoury296', centre=[670, 301.6, 3440], size=[66, 19, 51], yaw=0)],
        cameras=[dict(name='approach', eye=[560, 300, 3560], target=[606, 300, 3588]),
                 dict(name='gate', eye=[612, 307, 3590], target=[700, 285, 3510]),
                 dict(name='overview', eye=[470, 430, 3730], target=[690, 280, 3520], lens=28),
                 dict(name='yard', eye=[712, 262, 3614], target=[690, 292, 3515]),
                 dict(name='jangdae', eye=[774, 327, 3448], target=[670, 300, 3440]),
                 dict(name='ridge', eye=[801, 293, 3545], target=[776, 318, 3446]),
                 dict(name='south', eye=[700, 262, 3300], target=[690, 302, 3440]),
                 dict(name='wallwalk', eye=[798, 285, 3580], target=[806, 305, 3470]),
                 dict(name='northgate_out', eye=[671, 290.4, 3517], target=[670, 290.2, 3500]),
                 dict(name='northgate_in', eye=[673.5, float(H(673.5, 3491)) + 1.7, 3491], target=[670, 290.6, 3501]),
                 dict(name='amun_in', eye=[702.5, float(H(702.5, 3399)) + 1.7, 3399], target=[700, 281.4, 3391]),
                 dict(name='lookout', eye=[climb[-1][0], climb[-1][1] + 1.6, climb[-1][2]], target=[670, 302, 3440]),
                 dict(name='rest', eye=[fx + u[0] * 3.4 - side[0] * .6, float(H(fx + u[0] * 3.4, fz + u[1] * 3.4)) + 1.65, fz + u[1] * 3.4 - side[1] * .6],
                      target=[ax_ - u[0] * 1.2 + side[0] * .6, rest['position'][1] + .9, az_ - u[1] * 1.2 + side[1] * .6])])
    layout['stats'] = stats
    layout['circuit'] = dict(outer_length=float(s[-1]), inner_length=float(si[-1]), main_gate_s=s_gate, south_amun_s=s_amun)
    layout['stairs'] = {k: dict(foot=v['foot'], top=v['top'], landing=v['landing'], run=v['run']) for k, v in stairs.items()}
    (OUT / 'layout.json').write_text(json.dumps(layout, indent=1), encoding='utf-8')
    flat = lambda pts: [round(float(v), 3) for p in pts for v in p]
    unity = dict(compound='Cheolong', root='Finish297_Cheolong', meshes=MESHES,
                 hide=['Architecture296_Gates/arsenal296_PrecinctWall'],
                 clip=[dict(path='Architecture296_Venues/Approach_arsenal296', polygon=[round(float(v), 2) for q in OUTER for v in q])],
                 markers=[dict(id='rest', kind='rest', checkpoint=layout['markers']['rest']['checkpoint'], position=layout['markers']['rest']['position']),
                          dict(id='lookout', kind='lookout', checkpoint='', position=layout['markers']['lookout']['position'])]
                         + [dict(id=f'enemy_{i}', kind='enemySlot', checkpoint='', position=p) for i, p in enumerate(layout['markers']['enemySlots'])]
                         + [dict(id=f'reward_{i}', kind='rewardSlot', checkpoint='', position=p) for i, p in enumerate(layout['markers']['rewardSlots'])],
                 gates=[dict(name=g['name'], position=g['position'], yaw=g['yaw'], width=g['width'], height=g['height'], barred=g.get('barred', ''),
                             shortcut=bool(g.get('shortcut', False))) for g in layout['gates']],
                 routes=[dict(id=k, points=flat(v)) for k, v in layout['routes'].items()],
                 doors=doors, rest=rest,
                 terrainOps=[dict(type=o['type'], centre=o['centre'], yaw=o['yaw'], half=o['half'], top=o['top'], blend=o['blend']) for o in TERRAIN_OPS])
    (OUT / 'unity.json').write_text(json.dumps(unity, indent=1), encoding='utf-8')
    print(json.dumps(dict(circuit=layout['circuit'], stairs=layout['stairs'], LOD0_total_tris=sum(v['tris'] for v in stats.values())), indent=1))


if __name__ == '__main__':
    main()
