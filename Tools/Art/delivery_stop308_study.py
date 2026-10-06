#!/usr/bin/env python3
"""L8 study (D308-16 relayout, look defect L8): the cargo delivery stop on the 22 deg slope. READ-ONLY.

  python Tools/Art/delivery_stop308_study.py            -> Art/Playtest308/Relayout/fix2/delivery_stop_study.json (+ .txt)

Reads: height_p1b (4 m lattice, bilinear = CompactWorldSurface.Sample), W_Demo_Main.unity YAML, WorldLayout_Main / WorldContent_Main,
earth308.json, the vegetation sheets named by content308_relayout.json dry.trunk_sheets, the grass seed asset (contentseat308.json).
Writes nothing under Oheangbu/Assets. Every number is [O] offline unless the text says [M] (quoted from EXTFIX_VERIFY.md).
"""
import json, math, sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import buildingaudit308_offline as B
import contentseat308_dry as S
import content308_relayout_dry as R
import meshbounds308 as MB
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
PROJECT = ROOT / 'Oheangbu'
CB = ROOT / 'Art/World/Compact/Rebuild/CliffBoundary308'
OUT = ROOT / 'Art/Playtest308/Relayout/fix2'
P = dict(
    floor_top=103.2,           # [M] EXTFIX_VERIFY 7-da
    bounds_M=dict(x=(1803.2, 1815.7), z=(2301.8, 2314.0), y=(100.1, 110.5)),   # [M]
    rule54=54.0, enemy_gap=44.0, road_half=4.0,
    flat_deg=6.0, win=16.0, route_reach=300.0,
    shrine=(1924.7, 2448.7), potter=(1925.5, 2434.5), boss=(2000.0, 2530.0),
    earth={'earth_road308/1': (1868.0, 2354.0), 'earth_road308/2': (1906.0, 2390.0), 'earth_road308/0': (2035.0, 2211.0)},
)
HN = np.frombuffer(S.HEIGHT_P1B.read_bytes(), dtype='<f4').reshape(S.H_ROWS, S.H_COLS)


def h(x, z):
    fx = x / 4.0; fz = z / 4.0; ix = int(fx); iz = int(fz); u = fx - ix; v = fz - iz
    a = HN[iz, ix] + (HN[iz, ix + 1] - HN[iz, ix]) * u; b = HN[iz + 1, ix] + (HN[iz + 1, ix + 1] - HN[iz + 1, ix]) * u
    return float(a + (b - a) * v)


def slope(x, z, d=2.0):
    gx = (h(x + d, z) - h(x - d, z)) / (2 * d); gz = (h(x, z + d) - h(x, z - d)) / (2 * d)
    return math.degrees(math.atan(math.hypot(gx, gz))), (gx, gz)


def poly_len(pl): return sum(math.hypot(pl[i + 1][0] - pl[i][0], pl[i + 1][1] - pl[i][1]) for i in range(len(pl) - 1))


def poly_proj(p, pl):
    """(distance, chainage) of p on the polyline."""
    best = (1e9, 0.0); acc = 0.0
    for i in range(len(pl) - 1):
        ax, az = pl[i]; bx, bz = pl[i + 1]; dx = bx - ax; dz = bz - az; L2 = dx * dx + dz * dz; L = math.sqrt(L2)
        t = 0 if L2 == 0 else max(0, min(1, ((p[0] - ax) * dx + (p[1] - az) * dz) / L2))
        d = math.hypot(p[0] - ax - dx * t, p[1] - az - dz * t)
        if d < best[0]: best = (d, acc + L * t)
        acc += L
    return best


def poly_at(pl, s):
    acc = 0.0
    for i in range(len(pl) - 1):
        L = math.hypot(pl[i + 1][0] - pl[i][0], pl[i + 1][1] - pl[i][1])
        if acc + L >= s and L > 0:
            t = (s - acc) / L; return (pl[i][0] + (pl[i + 1][0] - pl[i][0]) * t, pl[i][1] + (pl[i + 1][1] - pl[i][1]) * t)
        acc += L
    return pl[-1]


def rect_pts(c, yaw, hw, hl, step=1.0):
    """sample points of a rectangle centred c, local +z = Unity yaw direction, half width hw (local x), half length hl (local z)."""
    s = math.sin(math.radians(yaw)); co = math.cos(math.radians(yaw)); out = []
    nx = int(round(2 * hw / step)); nz = int(round(2 * hl / step))
    for i in range(nx + 1):
        for j in range(nz + 1):
            lx = -hw + 2 * hw * i / nx; lz = -hl + 2 * hl * j / nz
            out.append((c[0] + lx * co + lz * s, c[1] - lx * s + lz * co))
    return out


def rect_corners(c, yaw, hw, hl):
    s = math.sin(math.radians(yaw)); co = math.cos(math.radians(yaw))
    return [(c[0] + lx * co + lz * s, c[1] - lx * s + lz * co) for lx, lz in ((-hw, -hl), (hw, -hl), (hw, hl), (-hw, hl))]


def plane_fit(pts):
    A = np.array([[x, z, 1.0] for x, z in pts]); y = np.array([h(x, z) for x, z in pts])
    c, *_ = np.linalg.lstsq(A, y, rcond=None); res = y - A @ c
    return math.degrees(math.atan(math.hypot(c[0], c[1]))), float(y.min()), float(y.max()), float(y.mean()), float(np.abs(res).max())


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    J = {'marks': '[O] offline from height_p1b (4 m lattice, bilinear) / scene YAML / data assets; [M] quoted from EXTFIX_VERIFY.md', 'params': P}
    T = []
    def say(s=''): T.append(s)

    # ------------------------------------------------------------------ layout / content
    lay = R.load_asset(PROJECT / R.LAYOUT['main']); con = R.load_asset(PROJECT / R.CONTENT['main'])
    routes = {r['Id']: [(float(p['x']), float(p['y'])) for p in r['Bends']] for r in lay['Routes'] if len(r.get('Bends') or []) >= 2}
    width = {r['Id']: float(r['Width']) for r in lay['Routes']}
    rin = routes['inspection_two__capital_delivery']; rout = routes['capital_delivery__south_gate']
    J['routes'] = {'in': {'id': 'inspection_two__capital_delivery', 'width': width['inspection_two__capital_delivery'], 'len': round(poly_len(rin), 1), 'bends': rin},
                   'out': {'id': 'capital_delivery__south_gate', 'width': width['capital_delivery__south_gate'], 'len': round(poly_len(rout), 1), 'bends': rout}}
    say('ROADS  in %s W %.0f len %.0f m  end (%.1f, %.1f) | out %s W %.0f len %.0f m  start (%.1f, %.1f)' % ('inspection_two__capital_delivery', width['inspection_two__capital_delivery'], poly_len(rin), rin[-1][0], rin[-1][1], 'capital_delivery__south_gate', width['capital_delivery__south_gate'], poly_len(rout), rout[0][0], rout[0][1]))
    # road profile near the stop (every 8 m, last 120 m of in, first 120 m of out)
    prof = {'in': [], 'out': []}
    Lin = poly_len(rin)
    for s in range(0, 121, 8):
        p = poly_at(rin, Lin - s); prof['in'].append((s, round(p[0], 1), round(p[1], 1), round(h(*p), 2), round(slope(*p)[0], 1)))
        p = poly_at(rout, s); prof['out'].append((s, round(p[0], 1), round(p[1], 1), round(h(*p), 2), round(slope(*p)[0], 1)))
    J['road_profile'] = prof
    say('in-road profile back from its end (m back, x, z, y, ground slope): ' + '  '.join('%d:(%.0f,%.0f) %.1f %s°' % q for q in prof['in']))
    say('out-road profile from its start: ' + '  '.join('%d:(%.0f,%.0f) %.1f %s°' % q for q in prof['out']))
    grades = lambda pr: [round(math.degrees(math.atan((pr[i + 1][3] - pr[i][3]) / 8.0)), 1) for i in range(len(pr) - 1)]
    J['road_grade_deg'] = {'in_back': grades(prof['in']), 'out': grades(prof['out'])}
    say('in-road running grade per 8 m (back from the end): %s' % grades(prof['in'])); say('out-road running grade per 8 m: %s' % grades(prof['out']))

    # ------------------------------------------------------------------ scene: the stop
    sk = S.SceneK(PROJECT / S.SCENES['main']); sc = sk.sc
    J['scene_sha'] = sk.sha
    stop = 'CapitalEscort252/cargo_delivery'; nodes = {}
    for c in sc.children_of(sk.get(stop)):
        n = sc.nodes[c]; p, q, s_ = sc.world(c)
        nodes[n.name] = dict(x=round(p[0], 2), y=round(p[1], 3), z=round(p[2], 2), yaw=round(B.yaw_of(q), 1), ground=round(h(p[0], p[2]), 3), slope=round(slope(p[0], p[2])[0], 1), above_floor=round(p[1] - P['floor_top'], 2))
    J['nodes'] = nodes
    say(); say('STOP NODES (x, y, z | ground | slope | above floor 103.2)')
    for k, v in nodes.items(): say('  %-20s (%.2f, %.3f, %.2f) yaw %5.1f | g %.2f | %4.1f° | %+.2f' % (k, v['x'], v['y'], v['z'], v['yaw'], v['ground'], v['slope'], v['above_floor']))
    # building: mesh boxes -> oriented footprint in the pivot frame
    gh = stop + '/Guesthouse252'; gp, gq, gs = sc.world(sk.get(gh)); gyaw = B.yaw_of(gq)
    cache = {}; lo = [1e9] * 3; hi = [-1e9] * 3; parts = []
    for c in sc.children_of(sk.get(gh)):
        n = sc.nodes[c]; p, q, s_ = sc.world(c)
        for (cls, _, info) in sk.comps.get(n.go, []):
            if cls != 33 or not info.get('mesh') or not info['mesh'][1]: continue
            path = MB.guid_path(info['mesh'][1], cache) if False else None
            parts.append((n.name, info['mesh'][1], s_))
    J['building'] = dict(key=gh, source='Assets/HwaseongHaenggung/Prefabs/SM_Naeposa.prefab (CompactRebuildEscort252.Build.cs:94, PlaceSource fit (12, 0, 10)), baked to 15 VisualCorridor meshes, child scale 1.14; StoneSkirt308 = GroundFit299 skirt mesh',
                         pivot=[round(v, 2) for v in gp], yaw=round(gyaw, 1), parts=len(parts), bounds_M=P['bounds_M'], floor_top_M=P['floor_top'])
    say(); say('BUILDING %s pivot (%.2f, %.2f, %.2f) yaw %.1f, %d mesh parts; [M] bounds x %.1f-%.1f z %.1f-%.1f y %.1f-%.1f, floor top %.1f' % (gh, gp[0], gp[1], gp[2], gyaw, len(parts), *P['bounds_M']['x'], *P['bounds_M']['z'], *P['bounds_M']['y'], P['floor_top']))
    J['building_parts'] = [(a, g) for a, g, _ in parts]
    # terrain under the [M] footprint, 1 m
    bx = P['bounds_M']['x']; bz = P['bounds_M']['z']
    fp = [(x, z, h(x, z)) for x in np.arange(bx[0], bx[1] + 0.01, 1.0) for z in np.arange(bz[0], bz[1] + 0.01, 1.0)]
    ys = [q[2] for q in fp]; J['building']['terrain_under'] = dict(min=round(min(ys), 2), max=round(max(ys), 2), mean=round(sum(ys) / len(ys), 2), plane=[round(v, 2) for v in plane_fit([(q[0], q[1]) for q in fp])])
    lo_pt = min(fp, key=lambda q: q[2]); hi_pt = max(fp, key=lambda q: q[2])
    say('terrain under the footprint: %.2f at (%.0f, %.0f) .. %.2f at (%.0f, %.0f), mean %.2f; plane slope %.1f°; floor 103.2 is %.2f above the low corner, %.2f below the high corner' % (lo_pt[2], lo_pt[0], lo_pt[1], hi_pt[2], hi_pt[0], hi_pt[1], sum(ys) / len(ys), J['building']['terrain_under']['plane'][0], P['floor_top'] - lo_pt[2], hi_pt[2] - P['floor_top']))
    sl, g = slope(1809.5, 2308.0, 6.0); J['building']['fall_dir_yaw'] = round(math.degrees(math.atan2(-g[0], -g[1])) % 360, 1)
    say('slope at the pivot (12 m window) %.1f°, falls toward yaw %.1f (uphill = yaw %.1f)' % (sl, J['building']['fall_dir_yaw'], (J['building']['fall_dir_yaw'] + 180) % 360))

    # ------------------------------------------------------------------ 1 m field for the picture
    X0, X1, Z0, Z1 = 1730, 1960, 2230, 2470
    grid = np.array([[h(x, z) for x in range(X0, X1 + 1)] for z in range(Z0, Z1 + 1)], dtype='float32')
    np.save(OUT / 'delivery_stop_field_1m.npy', grid); J['field'] = dict(x0=X0, x1=X1, z0=Z0, z1=Z1, step=1, note='bilinear resample of the 4 m lattice (no information finer than 4 m)')

    # ------------------------------------------------------------------ flat-ground search (option C)
    def road_d(p): return min(poly_proj(p, rin)[0], poly_proj(p, rout)[0])
    def cand(cx, cz, win, lim):
        hw = win / 2; pts = [(cx + dx, cz + dz) for dx in np.arange(-hw, hw + 0.01, 2.0) for dz in np.arange(-hw, hw + 0.01, 2.0)]
        smax = max(slope(x, z)[0] for x, z in pts)
        if smax > lim: return None
        fit = plane_fit(pts)
        return dict(x=cx, z=cz, slope_max=round(smax, 1), plane=round(fit[0], 1), relief=round(fit[2] - fit[1], 2), y=round(h(cx, cz), 2), road_edge_min=round(min(road_d(q) for q in pts) - P['road_half'], 1), road_centre=round(road_d((cx, cz)), 1))
    found = {}
    for win, lim in ((16, 6), (16, 8), (16, 10), (12, 6), (12, 8)):
        rows = []
        for cx in range(1700, 2041, 2):
            for cz in range(2140, 2561, 2):
                p = (cx, cz); din, sin_ = poly_proj(p, rin); dout, sout = poly_proj(p, rout)
                if min(din, dout) > 60: continue
                if din <= dout: chain = -(poly_len(rin) - sin_)
                else: chain = sout
                if abs(chain) > P['route_reach']: continue
                c = cand(cx, cz, win, lim)
                if c is None: continue
                c['chain'] = round(chain, 0); c['earth'] = {k: round(math.hypot(cx - v[0], cz - v[1]), 1) for k, v in P['earth'].items()}
                c['shrine'] = round(math.hypot(cx - P['shrine'][0], cz - P['shrine'][1]), 1); c['boss'] = round(math.hypot(cx - P['boss'][0], cz - P['boss'][1]), 1)
                c['off_road'] = c['road_edge_min'] >= 0.0
                c['ok54'] = all(v >= P['rule54'] for v in c['earth'].values()) and c['boss'] >= P['rule54']
                rows.append(c)
        found['%dx%d_le%d' % (win, win, lim)] = rows
        say(); say('FLAT SEARCH window %d x %d m, max local slope <= %d°, within 60 m of the two roads, |chainage| <= 300 m: %d centres; off the road bed %d; off road + 54 m rule %d' % (win, win, lim, len(rows), sum(1 for r in rows if r['off_road']), sum(1 for r in rows if r['off_road'] and r['ok54'])))
    J['flat_search'] = {k: dict(n=len(v), off_road=sum(1 for r in v if r['off_road']), off_road_ok54=sum(1 for r in v if r['off_road'] and r['ok54']), rows=v) for k, v in found.items()}

    def clusters(rows, link=4.1):
        rows = list(rows); out = []
        while rows:
            cur = [rows.pop()]; grew = True
            while grew:
                grew = False
                for r in rows[:]:
                    if any(abs(r['x'] - q['x']) <= link and abs(r['z'] - q['z']) <= link for q in cur): cur.append(r); rows.remove(r); grew = True
            out.append(cur)
        return out
    J['clusters'] = {}
    for key in found:
        cl = clusters(found[key]); rep = []
        for c in cl:
            best = min(c, key=lambda r: (r['slope_max'], r['relief']))
            rep.append(dict(n=len(c), x=(min(r['x'] for r in c), max(r['x'] for r in c)), z=(min(r['z'] for r in c), max(r['z'] for r in c)), best=best, any_off_road=any(r['off_road'] for r in c), any_ok=any(r['off_road'] and r['ok54'] for r in c)))
        rep.sort(key=lambda r: r['best']['chain']); J['clusters'][key] = rep
        say('clusters %s:' % key)
        for r in rep:
            b = r['best']; say('  n %3d x %d-%d z %d-%d | best (%d, %d) y %.2f slope max %.1f° relief %.2f | chain %+.0f m | road edge %.1f m%s | earth1 %.0f earth2 %.0f earth0 %.0f boss %.0f shrine %.0f%s' % (r['n'], r['x'][0], r['x'][1], r['z'][0], r['z'][1], b['x'], b['z'], b['y'], b['slope_max'], b['relief'], b['chain'], b['road_edge_min'], '' if r['any_off_road'] else ' ROAD BED', b['earth']['earth_road308/1'], b['earth']['earth_road308/2'], b['earth']['earth_road308/0'], b['boss'], b['shrine'], '' if r['any_ok'] else ' (54 m or road: no)'))

    # ------------------------------------------------------------------ sightlines to the south gate from places (eye 1.6, target gate roof)
    gate = (2000.0, 2545.0); gate_top = h(2000.0, 2530.0) + 14.0
    def sight(a, tgt, ty, eye=1.6):
        ya = h(*a) + eye; L = math.hypot(tgt[0] - a[0], tgt[1] - a[1]); n = int(L / 2)
        worst = min(ya + (ty - ya) * i / n - h(a[0] + (tgt[0] - a[0]) * i / n, a[1] + (tgt[1] - a[1]) * i / n) for i in range(1, n))
        return round(L, 0), round(worst, 1)
    J['sight'] = {}
    for name, a in (('stop Parking', (1820.0, 2320.0)), ('clerk', (1816.05, 2315.48)), ('junction wedge', (1833.0, 2325.0))):
        J['sight'][name] = dict(to_gate=sight(a, gate, gate_top), gate_base_y=round(h(2000.0, 2530.0), 2), eye_y=round(h(*a) + 1.6, 2))
        say('sight %s (eye y %.1f) -> south gate roof (y %.1f, %.0f m): min clearance over terrain %.1f m' % (name, h(*a) + 1.6, gate_top, J['sight'][name]['to_gate'][0], J['sight'][name]['to_gate'][1]))

    # ------------------------------------------------------------------ vegetation / grass around
    try:
        rel = json.loads((CB / 'content308_relayout.json').read_text(encoding='utf-8'))
        tr = S.read_trunks((1760, 1880, 2260, 2380), rel['dry']['trunk_sheets'])
        J['trunks'] = [(round(t[0], 1), round(t[1], 1), round(t[2], 1), t[3], t[4], t[5], t[6]) for t in tr]
        say(); say('vegetation rows (tree / shrub prototypes) in x 1760-1880 z 2260-2380: %d' % len(tr))
    except Exception as e:
        say('trunks: %r' % e)
    try:
        seat = json.loads((CB / 'contentseat308.json').read_text(encoding='utf-8'))
        f = PROJECT / seat['grass']['asset']; raw = f.read_bytes(); arr = np.frombuffer(raw[:len(raw) // 4 * 4], '<f4')
        x = arr[:-4]; y = arr[1:-3]; z = arr[2:-2]; nx = arr[3:-1]; nz = arr[4:]
        with np.errstate(invalid='ignore'):
            m = (x > 1760) & (x < 1880) & (z > 2260) & (z < 2380) & (y > 60) & (y < 160) & (np.abs(nx) <= 1.0) & (np.abs(nz) <= 1.0)
        gx, gy, gz = x[m], y[m], z[m]
        ok = np.array([abs(float(gy[i]) - h(float(gx[i]), float(gz[i]))) < 1.5 for i in range(len(gx))]) if len(gx) else np.array([], bool)
        gx, gz = gx[ok], gz[ok]; np.save(OUT / 'delivery_stop_grass_xz.npy', np.stack([gx, gz]))
        J['grass'] = dict(asset=seat['grass']['asset'], seeds_in_window=int(len(gx)))
        say('grass seeds in the same window (y within 1.5 m of the field): %d' % len(gx))
    except Exception as e:
        say('grass: %r' % e)

    # ------------------------------------------------------------------ option A: pad volumes
    def pad(name, c, yaw, hw, hl, level, note):
        pts = rect_pts(c, yaw, hw, hl, 1.0); d = [level - h(x, z) for x, z in pts]; cell = (2 * hw) * (2 * hl) / len(pts)
        fill = sum(v for v in d if v > 0) * cell; cut = -sum(v for v in d if v < 0) * cell
        r = dict(name=name, centre=c, yaw=yaw, size=(2 * hw, 2 * hl), level=level, cut_m3=round(cut), fill_m3=round(fill), max_cut=round(-min(d), 2), max_fill=round(max(d), 2), corners=[(round(x, 1), round(z, 1), round(h(x, z), 2)) for x, z in rect_corners(c, yaw, hw, hl)], lattice_vertices=sum(1 for ix in range(int(c[0] // 4) - 8, int(c[0] // 4) + 9) for iz in range(int(c[1] // 4) - 8, int(c[1] // 4) + 9) if _in_rect((ix * 4.0, iz * 4.0), c, yaw, hw + 4, hl + 4)), note=note)
        say('  %-34s centre (%.1f, %.1f) yaw %.0f %dx%d m level %.1f: cut %d m3 (max %.2f m) fill %d m3 (max %.2f m); 4 m lattice vertices inside + 1 ring: %d' % (name, c[0], c[1], yaw, 2 * hw, 2 * hl, level, cut, r['max_cut'], fill, r['max_fill'], r['lattice_vertices']))
        return r
    say(); say('OPTION A pads (yaw 41.2 = the house front axis pointing at the stop; local x = along the contour)')
    A = []
    fwd = (math.sin(math.radians(41.2)), math.cos(math.radians(41.2)))
    house_c = ((bx[0] + bx[1]) / 2, (bz[0] + bz[1]) / 2)
    front = (house_c[0] + fwd[0] * 6.2, house_c[1] + fwd[1] * 6.2)     # the house front edge (centre + half depth)
    for level in (103.2, 104.7, 106.3):
        yc = (front[0] + fwd[0] * 8.0, front[1] + fwd[1] * 8.0)
        A.append(pad('yard 14x16 in front, level %.1f' % level, yc, 41.2, 7.0, 8.0, level, 'yard between the house front and the road end'))
    A.append(pad('house plinth 14x14, level 103.2', house_c, 41.2, 7.0, 7.0, 103.2, 'under the house itself (what the stone skirt hides today)'))
    J['option_A'] = dict(front=front, fwd=fwd, pads=A, tile='Reworld292_Terrain/Terrain_03_04 (x 1500-2000, z 2000-2500; LOD0 = 4 m lattice 126 x 126) [support name: cb308-small-stops ledger]')
    # where the yard edge meets the road: distance from yard far edge to road end
    far = (front[0] + fwd[0] * 16.0, front[1] + fwd[1] * 16.0)
    J['option_A']['yard_far_edge'] = far; J['option_A']['far_edge_ground'] = round(h(*far), 2); J['option_A']['far_edge_to_parking'] = round(math.hypot(far[0] - 1820.0, far[1] - 2320.0), 1)
    say('  yard far edge centre (%.1f, %.1f) ground %.2f, %.1f m from Parking (1820, 2320 y %.2f)' % (far[0], far[1], h(*far), J['option_A']['far_edge_to_parking'], h(1820.0, 2320.0)))
    for level in (103.2, 104.7):
        drop = h(1827.5, 2320.0) - level
        say('  road -> yard at level %.1f: drop %.2f m from the road at (1827.5, 2320); ramp length at 6° %.0f m, 8° %.0f m, 10° %.0f m' % (level, drop, drop / math.tan(math.radians(6)), drop / math.tan(math.radians(8)), drop / math.tan(math.radians(10))))
    # option B
    say(); say('OPTION B raise the house')
    Bq = []
    for raise_ in (2.9, 3.1, 3.4):
        f = P['floor_top'] + raise_
        Bq.append(dict(raise_m=raise_, floor=round(f, 2), base_low=round(f - lo_pt[2], 2), base_high=round(f - hi_pt[2], 2), roof_top=round(P['bounds_M']['y'][1] + raise_, 1)))
        say('  +%.1f m: floor %.2f; visible base %.2f m at the low corner, %+.2f m at the high corner; node ground 106.01-106.65 vs floor: %+.2f .. %+.2f' % (raise_, f, f - lo_pt[2], f - hi_pt[2], 106.01 - f, 106.65 - f))
    J['option_B'] = Bq
    # option D deck
    say(); say('OPTION D deck between the node ground and the house front')
    dk = rect_pts((front[0] + fwd[0] * 4.0, front[1] + fwd[1] * 4.0), 41.2, 7.0, 4.0, 1.0); dys = [h(x, z) for x, z in dk]
    J['option_D'] = dict(deck_14x8_ground=(round(min(dys), 2), round(max(dys), 2)), front_edge_ground=[round(h(front[0] + (-fwd[1]) * t, front[1] + fwd[0] * t), 2) for t in (-6, -3, 0, 3, 6)], floor=P['floor_top'])
    say('  ground along the house front edge (5 points across 12 m): %s; floor 103.2; ground under a 14 x 8 m deck in front: %.2f .. %.2f' % (J['option_D']['front_edge_ground'], min(dys), max(dys)))
    say('  deck at 106.0: above the house floor %.2f m; steps down to the floor at 0.17 m risers: %d risers, run %.1f m at 0.28 m treads' % (106.0 - 103.2, math.ceil((106.0 - 103.2) / 0.17), math.ceil((106.0 - 103.2) / 0.17) * 0.28))

    (OUT / 'delivery_stop_study.json').write_text(json.dumps(J, ensure_ascii=False, indent=1, default=lambda o: o.tolist() if hasattr(o, 'tolist') else str(o)), encoding='utf-8')
    (OUT / 'delivery_stop_study.txt').write_text('\n'.join(T), encoding='utf-8')
    print('\n'.join(T))


def _in_rect(p, c, yaw, hw, hl):
    s = math.sin(math.radians(yaw)); co = math.cos(math.radians(yaw)); dx = p[0] - c[0]; dz = p[1] - c[1]
    lx = dx * co - dz * s; lz = dx * s + dz * co
    return abs(lx) <= hw and abs(lz) <= hl


if __name__ == '__main__':
    main()
