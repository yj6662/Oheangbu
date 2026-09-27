"""#297 황경 궁성 — legacy-dungeon palace around the #296 great hall (대전 60x50 TestOnly arena at (1860, 51.4, 3460)).

Site (measured): flat basin 48-53 m around the hall, ground rising south to 58-66 m, back hill north, the capital wall
(z = 3600) behind. Fixed routes kept intact: the Hwanggyeong mountain trail runs just OUTSIDE the west palace wall
(x = 1800) and crosses the palace plaza; capital_center__palace enters through an open EAST GATE into the east quarter.
Axis x = 1860, facing south: plaza -> 정문 (3 홍예 + 2-storey 문루) -> outer court (61 m) -> 중문 -> middle court (56 m)
-> 전문 on a 7 m terrace: BARRED (국상 계엄; lifted from inside = shortcut) -> 조정 (49 m, 박석, 삼도, 품계석) -> 월대 -> 대전.
Legacy route: middle court east side gate -> east quarter lanes (궐내각사) -> 3-storey 누각 (tall building 2, ~24 m) ->
timber bridge from its 2nd floor onto the 조정 east 행각 roof walk -> stair down into the 조정 -> 대전.
Outputs Finish297/Hwanggyeong/: layout.json, unity.json, Meshes/*.json. TEST values throughout.
"""
import copy, json, math, sys
from pathlib import Path
import numpy as np
sys.path.insert(0, str(Path(__file__).resolve().parent))
from hanok297 import MB, beam
from hanok297_building import BuildingSpec, build_building
from hanok297_wall import H, gatehouse_arch, stone_path, path_profile, chunks, multi_arch_gate
from hanok297_palace import palace_wall, wooltae, court_paving

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Art/World/Compact/Rebuild/Finish297/Hwanggyeong'
AX = 1860.0
HALL = dict(centre=(1860.0, 51.4, 3460.0), size=(66.0, 56.0), entrance=[1860.0, 51.39, 3427.0])
LEVEL = dict(plaza=None, outer=61.0, middle=56.2, court=49.3)
Z = dict(south=3160.0, jungmun=3250.0, jeonmun=3338.0, north=3598.0)
XW, XE, XC0, XC1 = 1800.0, 2030.0, 1806.0, 1914.0          # palace wall W/E, court edges W/E

TERRAIN_OPS = []; MESHES = []; STATS = {}


def export_all(fn, name, extra=None, kind='building', world=False, position=None, yaw=0.0):
    first = None
    for lod in (0, 1, 2):
        out = fn(lod); vis, col = out[0], out[1]
        vis.export(OUT / 'Meshes' / f'{name}_LOD{lod}.json')
        if lod == 0:
            col.export(OUT / 'Meshes' / f'{name}_Collision.json'); STATS[name] = dict(tris=vis.tris())
            if extra: STATS[name].update(extra(out))
            first = out
    e = dict(name=name, kind=kind, world=world)
    if not world: e.update(position=[float(v) for v in position], yaw=float(yaw))
    MESHES.append(e)
    return first


def pad_op(cx, cz, hx, hz, top, yaw=0.0, blend=6.0):
    TERRAIN_OPS.append(dict(type='pad', centre=[cx, cz], yaw=yaw, half=[hx, hz], top=top, blend=blend))


KIT = dict(
    haenggak=lambda n: BuildingSpec('haenggak', [3.0] * n, [3.4], col_h=3.0, style='official', roof='matbae', platform_h=.6, platform_margin=.45,
                                    stairs=(), walls=dict(front='open', back='plaster', left='plaster', right='plaster')),
    gate3=BuildingSpec('gate3', [3.6, 4.4, 3.6], [3.4, 3.4], col_h=4.2, style='palace', roof='palzak', platform_h=.9, platform_margin=1.3,
                       stairs=('front', 'back'), walls=dict(front='doors', back='doors', left='plaster', right='plaster'),
                       open_bays={('front', 1), ('back', 1)}, enterable=True),
    gate3_barred=BuildingSpec('gate3_barred', [3.6, 4.4, 3.6], [3.4, 3.4], col_h=4.4, style='palace', roof='palzak', platform_h=.9, platform_margin=1.3,
                              stairs=('front', 'back'), walls=dict(front='doors', back='doors', left='plaster', right='plaster'), enterable=True),
    jeongmun_top=BuildingSpec('jeongmun_top', [3.4, 3.8, 4.4, 3.8, 3.4], [3.2, 3.2], col_h=3.4, style='palace', roof='uzin' if False else 'palzak',
                              platform_h=0.0, platform_margin=0.0, stairs=(), walls=dict(front='rail', back='rail', left='rail', right='rail'), enterable=True,
                              storeys=[dict(h=2.9, inset=.9, balcony=False, walls=dict(front='window', back='window', left='plaster', right='plaster'))]),
    office5=BuildingSpec('office_5x2', [3.0] * 5, [3.0, 3.0], col_h=3.2, style='official', roof='matbae', platform_h=.7, platform_margin=.8,
                         walls=dict(front='doors', back='plaster', left='plaster', right='plaster')),
    office3=BuildingSpec('office_3x2', [3.0, 3.4, 3.0], [3.0, 3.0], col_h=3.2, style='official', roof='palzak', platform_h=.7, platform_margin=.8,
                         walls=dict(front='doors', back='window', left='plaster', right='plaster')),
    hall5=BuildingSpec('hall_5x3', [3.3, 3.6, 4.2, 3.6, 3.3], [3.0, 3.4, 3.0], col_h=4.0, style='palace', roof='palzak', platform_h=1.2, platform_margin=1.6,
                       enterable=True, floor='floor_brick'),
    nugak=BuildingSpec('nugak_3storey', [3.2, 3.8, 3.2], [3.2, 3.8, 3.2], col_h=4.6, style='palace', roof='palzak', platform_h=1.8, platform_margin=2.2,
                       stairs=('front', 'back'), walls=dict(front='doors', back='plaster', left='plaster', right='plaster'), open_bays={('front', 1), ('back', 1)},
                       enterable=True, interior_stairs=True,
                       storeys=[dict(h=3.8, inset=.9, balcony=True, walls=dict(front='window', back='window', left='window', right='window')),
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
    export_all(lambda lod: build_building(s, lod), name, lambda o: dict(height=round(max(v[1] for v in o[0].V), 1), platform=round(s.ph, 2)),
               position=[x, base, z], yaw=yaw)
    placements.append(dict(name=name, kind=spec.name, position=[x, base, z], yaw=yaw, platform=s.ph, floor=base + s.ph + .08))
    return placements[-1]


def haenggak_line(prefix, a, b, level, facing, placements, bay=3.0):
    """Row of 행각 buildings from a to b (XZ) at a court level, open side facing `facing` (+1/-1 across the row)."""
    a = np.asarray(a, float); b = np.asarray(b, float); L = float(np.linalg.norm(b - a)); d = (b - a) / L
    n_total = max(1, int(L / bay)); per = 12; k = 0; start = 0
    while start < n_total:
        n = min(per, n_total - start); c = a + d * bay * (start + n / 2)
        yaw = math.degrees(math.atan2(d[0], d[1])) + 90 * facing       # front (+z local) perpendicular to the row
        place(f'{prefix}_{k:02d}', KIT['haenggak'](n), float(c[0]), float(c[1]), yaw, level=level, placements=placements)
        start += n; k += 1


def main():
    OUT.mkdir(parents=True, exist_ok=True); (OUT / 'Meshes').mkdir(exist_ok=True)
    for f in (OUT / 'Meshes').glob('*.json'): f.unlink()
    layout = dict(note=__doc__.strip().splitlines()[0], walls=[], gates=[], buildings=[], paths=[], routes={}, markers={})
    placements = []
    # --- court pads (cut) ------------------------------------------------------------------------------------------
    pad_op(AX, (Z['south'] + Z['jungmun']) / 2, (XC1 - XC0) / 2, (Z['jungmun'] - Z['south']) / 2 - 1, LEVEL['outer'], blend=4.0)
    pad_op(AX, (Z['jungmun'] + Z['jeonmun']) / 2, (XC1 - XC0) / 2, (Z['jeonmun'] - Z['jungmun']) / 2 - 1, LEVEL['middle'], blend=4.0)
    pad_op(AX, (Z['jeonmun'] + 3505) / 2, (XC1 - XC0) / 2, (3505 - Z['jeonmun']) / 2 - 1, LEVEL['court'], blend=4.0)
    # --- palace walls (궁장): W and E sides + south with gate gaps; the capital wall closes the north ---------------------
    walls = dict(WallWest=[(XW, Z['south']), (XW, Z['north'])],
                 WallSouthW=[(XW, Z['south']), (AX - 16.5, Z['south'])], WallSouthE=[(AX + 16.5, Z['south']), (XE, Z['south'])],
                 WallEastS=[(XE, Z['south']), (XE, 3346.0)], WallEastN=[(XE, 3358.0), (XE, Z['north'])])
    for name, pts in walls.items():
        P, _ = path_profile(pts, width=1.1, step=2.0); dummy = np.zeros(len(P))
        for c, (Pc, yc, s0) in enumerate(chunks(P, dummy, 50.0)):
            nm = f'{name}_{c:02d}'
            out = export_all(lambda lod: palace_wall(nm, Pc, lod=lod, s0=s0), nm, lambda o: dict(length=round(o[2]['length'], 1)), kind='wall', world=True)
        layout['walls'].append(dict(name=name, length=float(np.linalg.norm(np.subtract(pts[-1], pts[0])))))
    # --- 정문: three 홍예 in a stone gatehouse + 2-storey 문루 ---------------------------------------------------------------
    g_y = float(H(AX, Z['south']))
    export_all(lambda lod: multi_arch_gate('Jeongmun_Base', (AX, g_y - .05, Z['south']), 0.0, top=g_y + 7.2, lod=lod), 'Jeongmun_Base', kind='gate', world=True)
    jt = place('Jeongmun_Top', KIT['jeongmun_top'], AX, Z['south'], 180.0, level=g_y + 7.2 - .3, placements=placements)
    layout['gates'].append(dict(name='Jeongmun', position=[AX, g_y, Z['south']], yaw=180.0, width=5.0, height=5.4, barred='', shortcut=False))
    # --- 중문 / 전문 (barred) on the terrace edges; grand stairs down to the lower court --------------------------------
    place('Jungmun', KIT['gate3'], AX, Z['jungmun'], 180.0, level=LEVEL['outer'] - .9, placements=placements)
    place('Jeonmun', KIT['gate3_barred'], AX, Z['jeonmun'], 180.0, level=LEVEL['middle'] - .9, placements=placements)
    layout['gates'] += [dict(name='Jungmun', position=[AX, LEVEL['outer'], Z['jungmun']], yaw=180.0, width=4.4, height=4.2, barred='', shortcut=False),
                        dict(name='Jeonmun', position=[AX, LEVEL['middle'], Z['jeonmun']], yaw=180.0, width=4.4, height=4.4, barred='inside', shortcut=True)]
    for nm, z0, y0, y1 in (('StairJungmun', Z['jungmun'] + 5.2, LEVEL['outer'], LEVEL['middle']), ('StairJeonmun', Z['jeonmun'] + 5.2, LEVEL['middle'], LEVEL['court'])):
        run = (y0 - y1) / .5                                        # ~27 deg monumental flight
        pts = [(AX, z0), (AX, z0 + run)]
        vis = MB(nm); col = MB(nm + '_collision'); n = max(3, int(math.ceil((y0 - y1) / .17))); tread = run / n
        for i in range(n):
            yy = y0 - (y0 - y1) * (i + 1) / n
            vis.box([AX, yy + (y0 - y1) / n / 2 - .01, z0 + tread * (i + .5)], [8.0, (y0 - y1) / n, tread + .01], 'stone_plain', faces='xXzZY', uvscale=1.0)
        col.quad([AX - 4, y0, z0], [AX + 4, y0, z0], [AX + 4, y1, z0 + run], [AX - 4, y1, z0 + run], 'c', [0, 1, 1])
        for sx in (-1, 1): vis.box([AX + sx * 4.3, (y0 + y1) / 2, z0 + run / 2], [.6, y0 - y1 + .6, run], 'stone_dressed', faces='xXzZY', uvscale=2.0)
        for lod in (0, 1, 2): vis.export(OUT / 'Meshes' / f'{nm}_LOD{lod}.json')
        col.export(OUT / 'Meshes' / f'{nm}_Collision.json'); MESHES.append(dict(name=nm, kind='path', world=True)); STATS[nm] = dict(tris=vis.tris())
    # --- 행각 around the courts (open to the court) -------------------------------------------------------------------
    haenggak_line('HaengOuterW', (XC0 + 1.8, Z['south'] + 14), (XC0 + 1.8, Z['jungmun'] - 6), LEVEL['outer'], -1, placements)
    haenggak_line('HaengOuterE', (XC1 - 1.8, Z['south'] + 14), (XC1 - 1.8, Z['jungmun'] - 6), LEVEL['outer'], 1, placements)
    haenggak_line('HaengMiddleW', (XC0 + 1.8, Z['jungmun'] + 8), (XC0 + 1.8, Z['jeonmun'] - 6), LEVEL['middle'], -1, placements)
    haenggak_line('HaengMiddleE', (XC1 - 1.8, Z['jungmun'] + 8), (XC1 - 1.8, 3300.0), LEVEL['middle'], 1, placements)      # gap = east side gate
    haenggak_line('HaengCourtW', (XC0 + 1.8, Z['jeonmun'] + 22), (XC0 + 1.8, 3500.0), LEVEL['court'], -1, placements)
    haenggak_line('HaengCourtE', (XC1 - 1.8, Z['jeonmun'] + 22), (XC1 - 1.8, 3500.0), LEVEL['court'], 1, placements)
    haenggak_line('HaengCourtSW', (XC0 + 4, Z['jeonmun'] + 2.5), (AX - 9, Z['jeonmun'] + 2.5), LEVEL['court'], 1, placements)
    haenggak_line('HaengCourtSE', (AX + 9, Z['jeonmun'] + 2.5), (XC1 - 4, Z['jeonmun'] + 2.5), LEVEL['court'], 1, placements)
    # --- paving, 월대 ---------------------------------------------------------------------------------------------------
    for nm, zc, dz, y, marks in (('PaveOuter', (Z['south'] + Z['jungmun']) / 2, Z['jungmun'] - Z['south'] - 8, LEVEL['outer'], 0),
                                 ('PaveMiddle', (Z['jungmun'] + Z['jeonmun']) / 2 + 2, Z['jeonmun'] - Z['jungmun'] - 12, LEVEL['middle'], 0),
                                 ('PaveCourt', 3380.0, 72.0, LEVEL['court'], 6)):
        export_all(lambda lod: court_paving(nm, (AX, zc), 0.0, (XC1 - XC0 - 9, dz), y, True, marks, lod), nm, kind='path', world=True)
    export_all(lambda lod: wooltae('Wooltae', (AX, LEVEL['court'], 3421.0), 0.0, (46.0, 15.0), tiers=2, tier_h=1.05, lod=lod), 'Wooltae', kind='path', world=True)
    # --- east quarter (궐내각사) with the tall 누각, north halls --------------------------------------------------------
    for name, key, x, z, yaw in (('OfficeE1', 'office5', 1972.0, 3268.0, 270.0), ('OfficeE2', 'office3', 2002.0, 3300.0, 180.0),
                                 ('OfficeE3', 'office5', 1946.0, 3372.0, 90.0), ('OfficeE4', 'office3', 2004.0, 3395.0, 270.0),
                                 ('Donggung', 'hall5', 1978.0, 3528.0, 180.0), ('Pyeonjeon', 'hall5', AX, 3552.0, 180.0),
                                 ('PavilionN', 'pavilion', 1946.0, 3580.0, 180.0)):
        place(name, KIT[key], x, z, yaw, placements=placements)
    nugak = place('Nugak', KIT['nugak'], 1954.0, 3440.0, 270.0, placements=placements)
    # timber bridge: from the 누각 2nd floor (west balcony) to the 조정 east 행각 roof walk, then the roof walk + stair down
    y2 = nugak['floor'] + KIT['nugak'].col_h + .3 + .85 + 1.1                # 2nd floor level (approx., matches build_building)
    roof_y = LEVEL['court'] + .6 + .08 + 3.0 + .3 + .45 + 1.6               # 행각 ridge walk height (approx.)
    br = MB('NugakBridge'); brc = MB('NugakBridge_collision')
    a = np.array([1954.0 - 7.6, y2, 3440.0]); b = np.array([XC1 - 1.8, roof_y, 3440.0])
    for sx in (-1, 1): beam(br, a + [0, -.2, sx * .9], b + [0, -.2, sx * .9], .22, .3, 'wood_dark')
    d = b - a; nb = int(np.linalg.norm(d) / .5)
    for i in range(nb):
        p = a + d * (i + .5) / nb; br.box(p.tolist(), [.46, .08, 1.9], 'wood_board', faces='xXyYzZ', uvscale=1.0)
    for sx in (-1, 1): beam(br, a + [0, .95, sx * 1.0], b + [0, .95, sx * 1.0], .08, .08, 'wood_dark')
    brc.quad((a + [0, 0, -1]).tolist(), (a + [0, 0, 1]).tolist(), (b + [0, 0, 1]).tolist(), (b + [0, 0, -1]).tolist(), 'c', [0, 1, 0])
    walk = MB('RoofWalk'); walkc = MB('RoofWalk_collision')
    z0, z1 = Z['jeonmun'] + 24, 3496.0
    walk.box([XC1 - 1.8, roof_y + .05, (z0 + z1) / 2], [1.1, .1, z1 - z0], 'wood_board', faces='xXyYzZ', uvscale=1.0)
    walkc.box([XC1 - 1.8, roof_y + .05, (z0 + z1) / 2], [1.4, .1, z1 - z0], 'c', faces='Y')
    for zz in np.arange(z0, z1, 3.0): beam(walk, [XC1 - 1.8, roof_y - .5, zz], [XC1 - 1.8, roof_y, zz], .1, .1, 'wood_dark')
    stair_top = np.array([XC1 - 1.8, roof_y, z1]); stair_bot = np.array([XC1 - 7.0, LEVEL['court'] + .05, z1 + 6.5])
    n = int(math.ceil((roof_y - LEVEL['court']) / .19))
    for i in range(n):
        p = stair_top + (stair_bot - stair_top) * (i + .5) / n; walk.box(p.tolist(), [1.2, .1, .5], 'wood_board', yaw=math.degrees(math.atan2(-5.2, 6.5)), faces='xXyYzZ')
    walkc.quad((stair_top + [-.7, 0, 0]).tolist(), (stair_top + [.7, 0, 0]).tolist(), (stair_bot + [.7, 0, 0]).tolist(), (stair_bot + [-.7, 0, 0]).tolist(), 'c', [0, 1, 0])
    for nm, v, c in (('NugakBridge', br, brc), ('RoofWalk', walk, walkc)):
        for lod in (0, 1, 2): v.export(OUT / 'Meshes' / f'{nm}_LOD{lod}.json')
        c.export(OUT / 'Meshes' / f'{nm}_Collision.json'); MESHES.append(dict(name=nm, kind='path', world=True)); STATS[nm] = dict(tris=v.tris())
    layout['buildings'] = placements
    # --- routes / markers -----------------------------------------------------------------------------------------------
    court = LEVEL['court']
    layout['routes'] = dict(
        plaza_to_middle=[[AX, g_y, Z['south'] - 20], [AX, g_y, Z['south'] - 6], [AX, LEVEL['outer'], Z['south'] + 8], [AX, LEVEL['outer'], Z['jungmun'] - 6],
                         [AX, LEVEL['outer'] + .9, Z['jungmun']], [AX, LEVEL['middle'], Z['jungmun'] + 14], [AX, LEVEL['middle'], Z['jeonmun'] - 8]],
        middle_to_nugak=[[AX, LEVEL['middle'], Z['jeonmun'] - 8], [XC1 - 4, LEVEL['middle'], 3310.0], [1940.0, float(H(1940, 3310)), 3310.0],
                         [1935.0, float(H(1935, 3400)), 3400.0], [nugak['position'][0] + 8.0, nugak['floor'], nugak['position'][2]]],
        nugak_to_hall=[[float(a[0]), float(a[1]), float(a[2])], [float(b[0]), float(b[1]), float(b[2])], [XC1 - 1.8, roof_y, z1],
                       [float(stair_bot[0]), court, float(stair_bot[2])], [AX + 10, court, 3405.0], HALL['entrance']],
        shortcut=[HALL['entrance'], [AX, court, 3400.0], [AX, court, Z['jeonmun'] + 8], [AX, LEVEL['middle'], Z['jeonmun'] - 8]])
    layout['markers'] = dict(rest=dict(position=[1936.0, float(H(1936, 3330)), 3330.0], checkpoint='hwanggyeong_palace_rest297', note='east side gate of the middle court'),
                             lookout=dict(position=[nugak['position'][0], y2 + 3.8 + 1.2, nugak['position'][2]], note='누각 3rd floor: palace and capital skyline'),
                             enemySlots=[[AX, LEVEL['outer'], 3205.0], [AX, LEVEL['middle'], 3300.0], [1972.0, float(H(1972, 3340)), 3340.0], [AX, court, 3395.0]],
                             rewardSlots=[[nugak['position'][0], y2 + 3.8 + 1.2, nugak['position'][2]], [1946.0, float(H(1946, 3580)), 3580.0]], testOnly=True)
    layout['terrainOps'] = TERRAIN_OPS; layout['stats'] = STATS
    layout['preview'] = dict(terrain=[1740, 2080, 3080, 3620],
                             placeholders=[dict(name='GreatHall296', centre=[1860, 51.4, 3460], size=[66, 22, 56], yaw=0)],
                             cameras=[dict(name='plaza', eye=[1860, 70, 3095], target=[1860, 62, 3200]),
                                      dict(name='overview', eye=[1640, 190, 3060], target=[1880, 55, 3380], lens=28),
                                      dict(name='middle', eye=[1860, 58.5, 3262], target=[1860, 57, 3340]),
                                      dict(name='jeonmun_view', eye=[1860, 66, 3346], target=[1860, 52, 3440]),
                                      dict(name='nugak', eye=[1990, 62, 3470], target=[1954, 62, 3440]),
                                      dict(name='roofwalk', eye=[1912, 60, 3370], target=[1860, 55, 3460]),
                                      dict(name='aerial', eye=[2060, 260, 3240], target=[1880, 50, 3420], lens=26)])
    (OUT / 'layout.json').write_text(json.dumps(layout, indent=1), encoding='utf-8')
    flat = lambda pts: [round(float(v), 3) for p in pts for v in p]
    unity = dict(compound='Hwanggyeong', root='Finish297_Hwanggyeong', meshes=MESHES, hide=['Architecture296_Gates/palace296_PrecinctWall'],
                 markers=[dict(id='rest', kind='rest', checkpoint=layout['markers']['rest']['checkpoint'], position=layout['markers']['rest']['position']),
                          dict(id='lookout', kind='lookout', checkpoint='', position=layout['markers']['lookout']['position'])]
                         + [dict(id=f'enemy_{i}', kind='enemySlot', checkpoint='', position=p) for i, p in enumerate(layout['markers']['enemySlots'])]
                         + [dict(id=f'reward_{i}', kind='rewardSlot', checkpoint='', position=p) for i, p in enumerate(layout['markers']['rewardSlots'])],
                 gates=[dict(name=g['name'], position=g['position'], yaw=g['yaw'], width=g['width'], height=g['height'], barred=g['barred'], shortcut=g['shortcut']) for g in layout['gates']],
                 routes=[dict(id=k, points=flat(v)) for k, v in layout['routes'].items()],
                 terrainOps=[dict(type=o['type'], centre=o['centre'], yaw=o['yaw'], half=o['half'], top=o['top'], blend=o['blend']) for o in TERRAIN_OPS])
    (OUT / 'unity.json').write_text(json.dumps(unity, indent=1), encoding='utf-8')
    print(json.dumps(dict(meshes=len(MESHES), LOD0_total=sum(v['tris'] for v in STATS.values()), pads=len(TERRAIN_OPS)), indent=1))
    for k, v in STATS.items():
        if 'height' in v or 'length' in v: print(k, v)


if __name__ == '__main__':
    main()
