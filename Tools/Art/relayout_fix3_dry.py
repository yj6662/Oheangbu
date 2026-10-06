#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""#308 relayout fix 3 (D308-22 answer 1) - offline dry run of the delivery stop move + the fallen wreck. READ ONLY.

  python Tools/Art/relayout_fix3_dry.py                 every rule as PASS / FAIL with numbers -> Stage308_relayout_fix3/Dry/relayout_fix3_dry.txt (+ .json)
  python Tools/Art/relayout_fix3_dry.py --pictures      + the plan and the software views (Art/Playtest308/Relayout/fix3/*.png)
  python Tools/Art/relayout_fix3_dry.py mutations       each mutation of the data must turn the named rule(s) red -> Dry/relayout_fix3_mutations.txt
Exit code 0 = FAIL 0 (dry) / every mutation caught (mutations).

[O] height_p1b (4 m lattice, bilinear), the three scene files, the content / layout assets, the REAL meshes of the house (15 parts +
the stone skirt) and of the palanquin. [I] = inferred (marked in the line). Nothing is written under Oheangbu/.
Data: Stage308_relayout_fix3/Data/delivery308.json + propfix308.json (the staged files; --deployed reads the live copies).
"""
import copy, json, math, sys
from pathlib import Path
import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
import relayout_fix3_lib as L            # noqa: E402
import relayout_fix3_wreck as WK         # noqa: E402

STAGE = L.STAGE; OUT = L.OUT
LIVE = {'delivery308.json': L.CB / 'small_1b/delivery308.json', 'propfix308.json': L.CB / 'propfix308.json', 'earth308.json': L.CB / 'earth308.json', 'smallops308.cfg.json': L.CB / 'small_1b/smallops308.cfg.json'}


class Rep:
    def __init__(self): self.lines = []; self.rows = []; self.nums = {}
    def head(self, s): self.lines.append(''); self.lines.append('== ' + s)
    def say(self, s): self.lines.append(s)
    def row(self, rid, ok, text, mark='O'):
        self.rows.append((rid, bool(ok), text)); self.lines.append('%s %s [%s] %s' % ('PASS' if ok else 'FAIL', rid, mark, text))
    def info(self, rid, text, mark='O'): self.lines.append('INFO %s [%s] %s' % (rid, mark, text))
    def open(self, rid, text): self.lines.append('OPEN %s %s' % (rid, text))
    def fails(self): return [r for r in self.rows if not r[1]]


def load_data(deployed=False):
    def rd(name):
        p = LIVE[name] if deployed else STAGE / 'Data' / name
        return json.loads(p.read_text(encoding='utf-8-sig'))
    return dict(delivery=rd('delivery308.json'), propfix=rd('propfix308.json'), earth=rd('earth308.json'), smallops=rd('smallops308.cfg.json'))


class World:
    """everything read once (scenes, meshes, layout); the checks then run on (data, world) - mutations reuse it."""
    def __init__(self, scenes=('arch296', 'folk298', 'main')):
        self.W = L.load_world('main'); self.stops = {a: L.stop_nodes(a) for a in scenes}
        self.contents = {a: L.R.load_asset(L.PROJECT / L.R.CONTENT[a]) for a in scenes}
        self.near = None; self.wreck = WK.Wreck('main'); self.wrecks_sha = {'main': self.wreck.sha}
        self.seeds, self.grass = L.grass_seeds((1780, 1870, 2270, 2360)); self.trunks, self.sheets = L.trunks((1780, 1870, 2270, 2360))
        self.trunks_w, _ = L.trunks((2600, 2700, 1920, 2020))
        self.house_cache = {}
        try: self.campaign = L.R.load_asset(L.PROJECT / L.R.CAMPAIGN)
        except Exception: self.campaign = None

    def house(self, scale, off):
        k = (scale, tuple(off))
        if k not in self.house_cache: self.house_cache[k] = L.load_house(scale, off)
        return self.house_cache[k]


def car_rect(c, yaw, w, ln): return L.rect_poly((c[0], 0, c[1], yaw), (-w / 2, w / 2, -ln / 2, ln / 2))


def poly_len(pl): return sum(math.hypot(b[0] - a[0], b[1] - a[1]) for a, b in zip(pl, pl[1:]))


def poly_at(pl, s):
    acc = 0.0
    for a, b in zip(pl, pl[1:]):
        ln = math.hypot(b[0] - a[0], b[1] - a[1])
        if acc + ln >= s and ln > 0: t = (s - acc) / ln; return (a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t), math.degrees(math.atan2(b[0] - a[0], b[1] - a[1])) % 360
        acc += ln
    a, b = pl[-2], pl[-1]; return b, math.degrees(math.atan2(b[0] - a[0], b[1] - a[1])) % 360


def swept(rules, rin, rout, park, park_yaw):
    """car footprints: the arrival along the in-road onto Parking, then a full-lock right turn onto the out-road and 20 m along it."""
    car = rules['car']; w, ln = car['width_m'], car['length_m']; out = []
    Lin = poly_len(rin)
    for s in np.arange(20.0, 0.0, -1.0):
        p, y = poly_at(rin, Lin - s); out.append(car_rect(p, y, w, ln))
    end, yend = poly_at(rin, Lin)
    for t in np.linspace(0, 1, 6):
        d = ((park_yaw - yend + 180) % 360) - 180
        out.append(car_rect((end[0] + (park[0] - end[0]) * t, end[1] + (park[1] - end[1]) * t), yend + d * t, w, ln))
    R = car['wheelbase_m'] / math.tan(math.radians(car['steer_deg']))
    f = (math.sin(math.radians(park_yaw)), math.cos(math.radians(park_yaw))); r = (f[1], -f[0])
    axle = (park[0] - f[0] * car['wheelbase_m'] / 2, park[1] - f[1] * car['wheelbase_m'] / 2); cen = (axle[0] + r[0] * R, axle[1] + r[1] * R)
    _, yout = poly_at(rout, 6.0); turn = (yout - park_yaw) % 360
    for a in np.arange(0.0, turn + 1e-6, 5.0):
        yaw = park_yaw + a; ff = (math.sin(math.radians(yaw)), math.cos(math.radians(yaw))); rr = (ff[1], -ff[0])
        ax = (cen[0] - rr[0] * R, cen[1] - rr[1] * R); c = (ax[0] + ff[0] * car['wheelbase_m'] / 2, ax[1] + ff[1] * car['wheelbase_m'] / 2)
        out.append(car_rect(c, yaw, w, ln))
    for s in np.arange(8.0, 28.0, 2.0):
        p, y = poly_at(rout, s); out.append(car_rect(p, y, w, ln))
    outer = math.hypot(R + w / 2, car['wheelbase_m'] / 2 + ln / 2)
    return out, dict(R=R, outer=outer, centre=cen, turn_deg=turn, yout=yout)


def chord_sag(a, b, step=0.25):
    n = max(2, int(math.hypot(b[0] - a[0], b[1] - a[1]) / step)); ha, hb = L.h(*a), L.h(*b); worst = -9.0; smax = 0.0
    for i in range(n + 1):
        t = i / n; p = (a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t)
        worst = max(worst, L.h(*p) - (ha + (hb - ha) * t)); smax = max(smax, L.slope(*p))
    return worst, smax


# ================================================================================================= the delivery stop

def check_delivery(o, D, wd):
    d = D['delivery']; H = d['house']; rules = d['rules']; W = wd.W
    parts, skirt, key = wd.house(d['offline']['house_scale'], d['offline']['house_child_offset']); fr = L.house_frame(parts)
    rin = W['routes'][H['road']['routes'][0]]; rout = W['routes'][H['road']['routes'][1]]
    var = H['variants'][H['variant']]; st = wd.stops['main']; S = {'house': H, 'fr': fr}

    o.head('S  the three scenes (the stop subtree as it stands)')
    ref = wd.stops['main']
    for a, s in wd.stops.items():
        same = all(k in s['nodes'] and max(abs(x - y) for x, y in zip(s['nodes'][k]['pos'], ref['nodes'][k]['pos'])) <= 0.005 and abs(((s['nodes'][k]['yaw'] - ref['nodes'][k]['yaw'] + 180) % 360) - 180) <= 0.05 for k in ref['nodes']) and set(s['nodes']) == set(ref['nodes'])
        o.row('S1', same, '%s (scene sha %s): the %d children of %s stand as in main' % (a, s['sha'], len(s['nodes']), d['stop']))
    hb = st['nodes'][H['node']]; eb = H['expect_before']
    o.row('S2', max(abs(x - y) for x, y in zip(hb['pos'], eb['pos'])) <= rules['before_tol_m'] and abs(hb['yaw'] - eb['yaw']) <= rules['before_tol_deg'], 'the house stands on expect_before: (%.3f, %.3f, %.3f) yaw %.2f (data (%.3f, %.3f, %.3f) yaw %.2f)' % (*hb['pos'], hb['yaw'], *eb['pos'], eb['yaw']))
    names = set(st['nodes']); listed = {n['node'] for n in d['nodes']} | set(d['keep']) | {H['node']}
    o.row('S3', names == listed, 'every child of the stop is the house, a keep[] or a nodes[] row (scene %s; data %s)' % (sorted(names), sorted(listed)))
    kids = st['house_children']; cols = sum(1 for v in kids.values() if 'MeshCollider' in v['comps'])
    o.row('S4', H['skirt'] in kids and H['plate'] in kids and cols == 15, 'the house carries %d MeshCollider parts, its plate %s and its skirt child %s: they move as one transform' % (cols, H['plate'], H['skirt']))
    o.row('S5', bool(d.get('moves_colliders')) and bool(str(d.get('bake_decision', '')).strip()) and 'D308-22' in d.get('bake_decision', ''), 'the move carries colliders: moves_colliders %s, bake_decision names D308-22' % d.get('moves_colliders'))

    o.head('P  the house (variant %s): real meshes (%d parts + skirt %d triangles), frame from the meshes' % (H['variant'], len(parts), len(skirt[1])))
    for nm in ('base', 'plinth', 'roof', 'body'):
        diff = max(abs(a - b) for a, b in zip(fr[nm], H[nm + '_rect']))
        o.row('P4', diff <= H['rect_tol_m'], '%s rectangle of the data = the mesh (%s; largest difference %.3f m)' % (nm, ', '.join('%.3f' % v for v in fr[nm]), diff))
    o.row('P4', abs(fr['plinth_top'] - H['plinth_top_local_m']) <= H['plinth_top_local_tol_m'], 'plinth top over the pivot %.4f m (data %.4f); lowest eave %.2f m over the plinth top, roof top %.2f m' % (fr['plinth_top'], H['plinth_top_local_m'], fr['eave_low'] - fr['plinth_top'], fr['roof_top']))
    ev, verdict = L.entrance_evidence(parts)
    o.info('P5', 'wall panel / door leaf / post area (m2) on the outer plane of each side, band 0.9 - 3.0 m: ' + '; '.join('%s wall %.1f door-behind %.1f post %.1f' % (k, v['wall'], v['door_set_back'], v['post']) for k, v in ev.items()))
    side = min(ev, key=lambda k: ev[k]['wall'])
    o.row('P5', side == H['entrance']['side'] and ev[side]['wall'] < 1.0 and ev[side]['door_set_back'] > 2.0, 'the entrance is the %s gable: an open bay (wall panel %.1f m2 there, >= %.1f on the other sides) with the door leaf %.1f m2 on the wall behind it (data entrance.side %s)' % (side, ev[side]['wall'], min(v['wall'] for k, v in ev.items() if k != side), ev[side]['door_set_back'], H['entrance']['side']))
    ys = [L.h(*p) for p in H['pad_probe_xz']]; pad = sorted(ys)[len(ys) // 2]
    o.row('P1', max(ys) - min(ys) <= H['pad_spread_max_m'] and abs(pad - H['pad_level_expect_m']) <= H['pad_level_tol_m'], 'packed surface: median %.3f over %d probes, spread %.3f (<= %.2f), offline level %.2f +- %.2f' % (pad, len(ys), max(ys) - min(ys), H['pad_spread_max_m'], H['pad_level_expect_m'], H['pad_level_tol_m']))
    py = pad - H['plinth_top_local_m']; pose = (var['xz'][0], py, var['xz'][1], var['yaw']); S.update(pose=pose, pad=pad)
    o.row('P1', abs(py + fr['plinth_top'] - pad) <= H['plinth_top_tol_m'], 'plinth top %.3f = packed surface %.3f (%.3f m, +- %.2f): pivot (%.2f, %.3f, %.2f) yaw %.1f; raised %.2f m, moved %.1f m in plan' % (py + fr['plinth_top'], pad, py + fr['plinth_top'] - pad, H['plinth_top_tol_m'], pose[0], py, pose[2], pose[3], py - hb['pos'][1], math.hypot(pose[0] - hb['pos'][0], pose[2] - hb['pos'][2])))
    base = L.rect_poly(pose, H['base_rect']); roof = L.rect_poly(pose, H['roof_rect']); plinth = L.rect_poly(pose, H['plinth_rect']); S.update(base=base, roof=roof, plinth=plinth)
    xs, zs, G, sl, flat = L.flat_mask(1800, 1860, 2296, 2350, pad, 0.10, 2.0); FX, FZ = np.meshgrid(xs, zs); fpts = np.stack([FX[flat], FZ[flat]], axis=1); S.update(flat=(xs, zs, flat), flat_area=float(flat.sum()) * 0.0625)
    for rid, pl in ((H['road']['routes'][0], rin), (H['road']['routes'][1], rout)):
        half = W['width'][rid] / 2; db = L.poly_line_d(base, pl) - half; dr = L.poly_line_d(roof, pl) - half
        o.row('P2', db >= H['road']['base_clear_of_bed_m'], 'base %.2f m outside the bed of %s (width %.0f m; centre line %.2f m away): the road keeps its %.1f m' % (db, rid, 2 * half, db + half, 2 * half))
        o.row('P3', dr >= H['road']['roof_clear_of_bed_m'], 'eaves %.2f m outside the bed of %s (lowest eave %.2f m over the packed surface; the car is %.2f m tall)' % (dr, rid, fr['eave_low'] - fr['plinth_top'], rules['car']['height_m']))
    cov = sum(1 for p in fpts if L.in_poly((float(p[0]), float(p[1])), base)); covr = sum(1 for p in fpts if L.in_poly((float(p[0]), float(p[1])), roof))
    o.row('P2', cov == 0, 'the whole base is on or behind the edge of the flat: it covers %d of the %d flat cells (%.0f m2 at %.2f +- 0.10, slope <= 2 deg); the eaves cover %d' % (cov, len(fpts), S['flat_area'], pad, covr))
    prof = L.perimeter_profile(pose, parts, skirt, fr, step=H['footing']['step_m'] / 2, band=H['footing']['band_m']); S['prof'] = prof
    bys = {s: max(p[6] for p in prof if p[0] == s) for s in ('+x', '-x', '+z', '-z')}; worst = max(prof, key=lambda p: p[6])
    o.row('P6', worst[6] <= H['footing']['float_max_m'], 'footing (real base + skirt mesh against the ground, %d edge samples): largest daylight %.3f m (<= %.2f) at side %s (%.1f, %.1f); per side front +x %.2f, rear -x %.2f, +z %.2f, -z %.2f (negative = in the ground)' % (len(prof), worst[6], H['footing']['float_max_m'], worst[0], worst[2], worst[3], bys['+x'], bys['-x'], bys['+z'], bys['-z']))
    rear = [p for p in prof if p[0] == '-x']; o.info('P6', 'rear side: the skirt is there on %d of %d samples; its lowest stone is %.2f .. %.2f m under the ground' % (sum(1 for p in rear if p[7]), len(rear), -max(p[6] for p in rear), -min(p[6] for p in rear)))
    x0, x1, z0, z1 = H['plinth_rect']; fe = [L.h(*L.to_world(pose, x1, z)) for z in np.arange(z0, z1 + 1e-6, 0.25)]
    be = [L.to_world(pose, H['base_rect'][1], z) for z in np.arange(H['base_rect'][2], H['base_rect'][3] + 1e-6, 0.5)]
    gap = [float(np.sqrt(((fpts - np.array(p)) ** 2).sum(axis=1).min())) for p in be]
    under = [L.h(*L.to_world(pose, x, z)) for x in np.linspace(x0, x1, 13) for z in np.linspace(z0, z1, 19)]
    o.row('P7', pad - min(fe) <= rules['front_step_max_m'], 'the flat is the yard: kerb at the plinth front edge %.2f .. %.2f m (<= %.2f)' % (pad - max(fe), pad - min(fe), rules['front_step_max_m']))
    bg = [L.h(*p) for p in be]
    o.info('P7', 'strip between the base front edge and the flat (cells at the packed level +- 0.10, slope <= 2 deg): %.2f m wide on average, %.2f m at most; the ground along the base front edge is %.2f .. %.2f m under the packed level (the base bottom is %.2f m under it: in the ground, P6)' % (sum(gap) / len(gap), max(gap), pad - max(bg), pad - min(bg), H['plinth_top_local_m']))
    o.row('P7', pad - min(under) <= rules['rear_base_max_m'], 'rear: the plinth stands %.2f m over the lowest ground under it (<= %.1f, the skirt reaches 2.45 m)' % (pad - min(under), rules['rear_base_max_m']))
    pk = st['nodes']['Parking']; park = (pk['pos'][0], pk['pos'][2]); carp = car_rect(park, pk['yaw'], rules['car']['width_m'], rules['car']['length_m']); S.update(park=park, park_yaw=pk['yaw'], carp=carp)
    place = (float(W['place']['XZ']['x']), float(W['place']['XZ']['y']))
    o.row('P9', math.hypot(park[0] - place[0], park[1] - place[1]) <= 0.01 and 'Parking' in d['keep'], 'Parking stays (%.2f, %.2f) = the layout place capital_delivery (%.0f, %.0f): the map marker and the minimap read the place, not a node' % (*park, *place))
    dpb = L.poly_poly_d(carp, base); dpr = L.poly_poly_d(carp, roof)
    o.row('P9', min(dpb, dpr) >= rules['parking_clear_m'], 'the car at Parking (%.2f x %.2f m, yaw %.1f) keeps %.2f m to the base and %.2f m to the eaves (>= %.2f)' % (rules['car']['width_m'], rules['car']['length_m'], pk['yaw'], dpb, dpr, rules['parking_clear_m']))
    sw, ti = swept(rules, rin, rout, park, pk['yaw']); S.update(swept=sw, turn=ti)
    dsb = min(L.poly_poly_d(q, base) for q in sw); dsr = min(L.poly_poly_d(q, roof) for q in sw)
    o.row('P10', min(dsb, dsr) >= rules['turn_clear_m'], 'swept body of the car (arrival along the in-road, full-lock right turn %.0f deg onto the out-road: rear-axle radius %.2f m, outer corner %.2f m, turning centre (%.1f, %.1f)): %.2f m to the base, %.2f m to the eaves (>= %.1f)' % (ti['turn_deg'], ti['R'], ti['outer'], *ti['centre'], dsb, dsr, rules['turn_clear_m']))
    off_flat = 0
    for q in sw:
        for c in q:
            ix = int(round((c[0] - xs[0]) / 0.25)); iz = int(round((c[1] - zs[0]) / 0.25))
            if not (0 <= ix < len(xs) and 0 <= iz < len(zs)) or (abs(G[iz, ix] - pad) > 0.6): off_flat += 1
    o.info('P10', 'corners of the swept footprints more than 0.6 m off the packed level: %d of %d (the turn fits on the road beds; the house takes no flat cell, so the room is what it was)' % (off_flat, 4 * len(sw)))
    rad = L.inscribed_radius(flat[::2, ::2], xs[::2], zs[::2]); S['inscribed'] = rad
    o.info('P10', 'largest circle on the flat: radius %.2f m at (%.1f, %.1f) - a U-turn needs %.2f m (outer corner): %s on the flat alone, before and after (the house covers 0 flat cells)' % (rad[0], rad[1], rad[2], ti['outer'], 'possible' if rad[0] >= ti['outer'] else 'NOT possible'))
    enemy = tuple(rules['rule54_from']['encounter']); hd = L.poly_pt_d(base, enemy)
    o.info('P11', 'house base -> the road enemy at (%.0f, %.0f): %.1f m (was %.1f m)' % (*enemy, hd, L.poly_pt_d(L.rect_poly((hb['pos'][0], 0, hb['pos'][2], hb['yaw']), H['base_rect']), enemy)))
    f = (math.sin(math.radians(pk['yaw'])), math.cos(math.radians(pk['yaw']))); right = (f[1], -f[0]); dot = (pose[0] - park[0]) * right[0] + (pose[2] - park[1]) * right[1]
    o.row('P12', dot < 0, 'CompactRebuildSouthGate253 check "guesthouse on road\'s opposite side": dot(house - Parking, incoming right) = %.2f (< 0)' % dot)
    for vn, vv in H['variants'].items():
        if vn == H['variant']: o.row('P13', 'blocked' not in vv, 'the chosen variant %s is not marked blocked' % vn); continue
        vp = (vv['xz'][0], py, vv['xz'][1], vv['yaw']); vb = L.rect_poly(vp, H['base_rect']); vr = L.rect_poly(vp, H['roof_rect'])
        vprof = L.perimeter_profile(vp, parts, skirt, fr, step=0.25, band=H['footing']['band_m']); vf = max(p[6] for p in vprof)
        vbed = min(L.poly_line_d(vb, rin) - W['width'][H['road']['routes'][0]] / 2, L.poly_line_d(vb, rout) - W['width'][H['road']['routes'][1]] / 2)
        vroof = min(L.poly_line_d(vr, rin), L.poly_line_d(vr, rout)) - 4.0
        bad = vf > H['footing']['float_max_m'] or vbed < 0
        o.row('P13', ('blocked' in vv) == bad, 'variant %s (%.2f, %.2f) yaw %.2f: daylight %.2f m, base %.2f m / eaves %.2f m from the bed edge (negative = on the road) -> %s; the data says %s' % (vn, vv['xz'][0], vv['xz'][1], vv['yaw'], vf, vbed, vroof, 'fails the rules' if bad else 'passes', 'blocked' if 'blocked' in vv else 'usable'))

    o.head('N  the person nodes (targets of the data; Y = ground + lift)')
    at = {}; hbox = [min(p[0] for p in base + roof), max(p[0] for p in base + roof), min(p[1] for p in base + roof), max(p[1] for p in base + roof)]; S['hbox'] = hbox
    for n in d['nodes']:
        nm = n['node']; x, z = n['xz']; at[nm] = (x, z); g = L.h(x, z); sp = L.slope(x, z, rules['node_slope_probe_m'])
        lane = min(L.poly_pt_d([p, p, p], (x, z)) if False else min(L.seg_d((x, z), a, b) for a, b in zip(pl, pl[1:])) for pl in (rin, rout))
        dcar = L.poly_pt_d(carp, (x, z)); dh = max(hbox[0] - x, x - hbox[1], hbox[2] - z, z - hbox[3]); was = st['nodes'][nm]['pos']
        ok = sp <= rules['node_slope_max_deg'] and abs(g - pad) <= rules['node_pad_tol_m'] and lane >= rules['lane_clear_m'] and dcar >= rules['parking_clear_m'] and dh >= rules['house_clear_m']
        o.row('N1', ok, '%-20s (%.2f, %.3f, %.2f)%s [was (%.2f, %.2f, %.2f), slope %.1f deg]: slope %.1f deg (<= %.0f), ground %+.2f m from the packed level (+- %.2f), lane centre %.2f m (>= %.1f), car at Parking %.2f m (>= %.2f), outside the house bounds %.2f m (>= %.1f)' % (
            nm, x, g + n.get('lift', 0.0), z, ' yaw %.0f' % n['yaw'] if n.get('yaw') is not None else '', was[0], was[1], was[2], L.slope(was[0], was[2], rules['node_slope_probe_m']), sp, rules['node_slope_max_deg'], g - pad, rules['node_pad_tol_m'], lane, rules['lane_clear_m'], dcar, rules['parking_clear_m'], dh, rules['house_clear_m']))
        if n.get('same_as'): o.row('N2', at.get(n['same_as']) == (x, z), '%s stands on %s' % (nm, n['same_as']))
    S['at'] = at
    db = math.hypot(at['Checkpoint'][0] - at['capital_escort_rest'][0], at['Checkpoint'][1] - at['capital_escort_rest'][1])
    o.row('N3', abs(db - rules['bench_from_feet_m']) <= rules['bench_from_feet_tol_m'], 'bench %.2f m from the respawn feet (%.1f +- %.2f, the #252 builder rule); bench lift %.2f = the F5 stops rule (%.2f)' % (db, rules['bench_from_feet_m'], rules['bench_from_feet_tol_m'], next(n for n in d['nodes'] if n['node'] == 'capital_escort_rest').get('lift', 0), D['smallops']['escort_stops']['stops'][2]['lifts']['capital_escort_rest']))
    o.row('N3', abs(next(n for n in d['nodes'] if n['node'] == 'capital_escort_rest').get('lift', 0) - D['smallops']['escort_stops']['stops'][2]['lifts']['capital_escort_rest']) < 1e-6, 'the bench lift of the data equals smallops308.cfg.json escort_stops (stops:verify keeps judging it)')
    dd = math.hypot(at['CargoClerk252'][0] - at['InspectionDesk252'][0], at['CargoClerk252'][1] - at['InspectionDesk252'][1])
    o.row('N3', abs(dd - rules['desk_from_clerk_m']) <= rules['desk_from_clerk_tol_m'], 'desk %.2f m from the clerk (%.1f +- %.2f)' % (dd, rules['desk_from_clerk_m'], rules['desk_from_clerk_tol_m']))
    for nm in ('CargoClerk252', 'InspectionDesk252', 'capital_escort_rest'):
        lx, lz = L.to_local(pose, *at[nm]); front = lx - H['base_rect'][1]
        o.row('N4', 0 <= front <= rules['front_of_house_max_m'] and H['base_rect'][2] <= lz <= H['base_rect'][3], '%s reads as in front of the house: %.2f m before the base front edge (<= %.1f), %.2f m along the %.1f m facade (porch bay 0.5 .. 2.0, door wall 0.5)' % (nm, front, rules['front_of_house_max_m'], lz, H['base_rect'][3] - H['base_rect'][2]))
    lxb, lzb = L.to_local(pose, *at['capital_escort_rest']); o.info('N4', 'bench: %.2f m outside the eave line (roof edge %.3f), under the lattice window side of the closed room (body z %.2f .. 0.5)' % (lxb - H['roof_rect'][1], H['roof_rect'][1], H['body_rect'][2]))
    # the collider footprints of the solid nodes come from the scene YAML (capsule radius / box half sizes x the node scale), posed on the data targets
    yaw_of = {n['node']: (n['yaw'] if n.get('yaw') is not None else st['nodes'][n['node']]['yaw']) for n in d['nodes'] if n['node'] in st['nodes']}
    solid = {nm: L.footprint(st['nodes'][nm]['solid'], at[nm], yaw_of[nm]) for nm in at if nm in st['nodes'] and st['nodes'][nm].get('solid')}; S['solid'] = solid
    o.row('N5', set(solid) == set(rules['solid_nodes']), 'the nodes that carry a collider in the scene (%s) = rules.solid_nodes (%s)' % (', '.join('%s %s' % (k, 'capsule r %.2f' % v[2] if v[0] == 'circle' else 'box %.2f x %.2f' % tuple(2 * q for q in st['nodes'][k]['solid']['half'])) for k, v in sorted(solid.items())), ', '.join(sorted(rules['solid_nodes']))))
    for n in d['nodes']:
        if not n.get('walk'): continue
        near = min((math.hypot(at[n['node']][0] - at[s_][0], at[n['node']][1] - at[s_][1]), s_) for s_ in solid)
        o.row('N5', near[0] >= rules['node_spacing_min_m'], 'walk node %s is %.2f m from the nearest collider node %s (>= %.1f: outside its NavMesh carve)' % (n['node'], near[0], near[1], rules['node_spacing_min_m']), 'I')
    # N8 (review F4): the straight lines audit A4 sweeps keep the sweep capsule + a margin off every solid footprint
    fpk = (math.sin(math.radians(pk['yaw'])), math.cos(math.radians(pk['yaw']))); ex = (park[0] + fpk[1] * rules['a4_exit_side_m'], park[1] - fpk[0] * rules['a4_exit_side_m'])
    pts = dict(at); pts['exit'] = ex; S['exit'] = ex; need = rules['sweep_radius_m'] + rules['walk_line_clear_m']
    for a_, b_ in rules['walk_lines']:
        if a_ not in pts or b_ not in pts: o.row('N8', False, 'walk line %s -> %s names a point that is not a node' % (a_, b_)); continue
        cand = [(L.seg_footprint_d(pts[a_], pts[b_], fp), nm) for nm, fp in solid.items() if min(math.hypot(at[nm][0] - pts[e][0], at[nm][1] - pts[e][1]) for e in (a_, b_)) > rules['pose_tol_m']]
        worst = min(cand) if cand else (99.0, '-')
        o.row('N8', worst[0] >= need, 'walk line %s -> %s (%.1f m, audit A4 sweep): %.2f m from the collider of %s (>= %.2f = sweep capsule %.2f + %.2f; a solid standing ON an end of the line is not counted)' % (a_, b_, math.hypot(pts[a_][0] - pts[b_][0], pts[a_][1] - pts[b_][1]), worst[0], worst[1], need, rules['sweep_radius_m'], rules['walk_line_clear_m']))
    # N9: the cargo box set down on CargoWait (axis-aligned as the node stands) against the solids; worst turn of the box too
    cg = st.get('cargo'); crule = rules['cargo']
    if cg is None or crule['key'] != L.CARGO: o.row('N9', False, 'the cargo box %s is not in the scene with a BoxCollider (lib reads %s)' % (crule['key'], L.CARGO))
    else:
        crate = L.footprint(dict(shape='box', half=cg['half']), at['CargoWait'], st['nodes']['CargoWait']['yaw'])
        dc = min((L.poly_footprint_d(crate[1], fp), nm) for nm, fp in solid.items()); rc = math.hypot(*cg['half'])
        def rad(nm): return solid[nm][2] if solid[nm][0] == 'circle' else math.hypot(*st['nodes'][nm]['solid']['half'])
        turn = min((math.hypot(at['CargoWait'][0] - at[nm][0], at['CargoWait'][1] - at[nm][1]) - rc - rad(nm), nm) for nm in solid)
        o.row('N9', dc[0] >= crule['clear_m'] and turn[0] >= 0.0, 'the cargo box (%.2f x %.2f m, %s) set down on CargoWait: %.2f m from the collider of %s (>= %.2f); turned any way it still does not reach a solid (%.2f m left to %s, >= 0)' % (2 * cg['half'][0], 2 * cg['half'][1], crule['key'], dc[0], dc[1], crule['clear_m'], turn[0], turn[1]))
        hbx = max(hbox[0] - at['CargoWait'][0], at['CargoWait'][0] - hbox[1], hbox[2] - at['CargoWait'][1], at['CargoWait'][1] - hbox[3])
        o.info('N9', 'the box reaches %.2f m toward the house from the node (the node is %.2f m outside the house bounds; the eaves are %.2f m up)' % (cg['half'][1], hbx, fr['eave_low'] - fr['plinth_top']))
    ip = at['Interaction']; cells = [(ip[0] + dx, ip[1] + dz) for dx in np.arange(-3.2, 3.21, 0.4) for dz in np.arange(-3.2, 3.21, 0.4) if math.hypot(dx, dz) <= rules['interaction_radius_m']]
    okc = sum(1 for c in cells if abs(L.h(*c) - pad) <= 0.15 and L.slope(*c) <= 6 and not L.in_poly(c, base)); frac = okc / len(cells)
    o.row('N6', frac >= rules['interaction_flat_frac_min'], 'the delivery talk point: %.0f %% of its %.1f m reach is flat ground outside the base (>= %.0f %%)' % (100 * frac, rules['interaction_radius_m'], 100 * rules['interaction_flat_frac_min']))
    for nm, p in (('Parking', park), ('Interaction', at['Interaction']), ('Checkpoint', at['Checkpoint'])):
        de = math.hypot(p[0] - enemy[0], p[1] - enemy[1]); o.row('N7', de >= rules['rule54_m'], '%s %.1f m from the road enemy 토 2 (>= %.0f; margin %+.1f)' % (nm, de, rules['rule54_m'], de - rules['rule54_m']))
    dsn = min((min(L.poly_pt_d(q, at[n['node']]) for q in sw), n['node']) for n in d['nodes'])
    o.row('P10', dsn[0] >= rules['turn_clear_m'], 'the swept body of the car stays %.2f m from the nearest node (%s; >= %.1f)' % (dsn[0], dsn[1], rules['turn_clear_m']))

    o.head('O  old site, new site: what is there (scene YAML, sheets, grass field)')
    if wd.near is None: wd.near = L.nearby_objects('main', [(hb['pos'][0], hb['pos'][2]), (pose[0], pose[2]), park], rules['old_site_radius_m'])
    outside = [r for r in wd.near if not r[0].startswith(d['stop'] + '/') and r[0] != d['stop'] and 'Terrain' not in r[0]]
    o.row('O1', len(outside) == 0, 'scene objects with a renderer or a collider within %.0f m of the old pivot, the new pivot or Parking: %d, all under %s (%d outside it%s)' % (rules['old_site_radius_m'], len(wd.near), d['stop'], len(outside), ': ' + ', '.join(r[0] for r in outside[:4]) if outside else ''))
    tops = {}
    for r in wd.near:
        rest = r[0][len(d['stop']) + 1:].split('/')[0] if r[0].startswith(d['stop'] + '/') else r[0]; tops[rest] = tops.get(rest, 0) + 1
    for k, c in sorted(tops.items()):
        fate = 'moves with the house (one transform)' if k == H['node'] else 'stays (keep[])' if k in d['keep'] else 'moves to its node target' if k in at else 'UNDECIDED'
        o.row('O2', fate != 'UNDECIDED', '%-22s %3d object(s): %s' % (k, c, fate))
    o.info('O2', 'removed: nothing. Left at the old site: nothing (no object of another root stands within %.0f m). The stop root (a marker without components) stays at (%.1f, %.2f, %.1f)' % (rules['old_site_radius_m'], *st['root']))
    tr = [(L.poly_pt_d(base, (t[0], t[2])), t) for t in wd.trunks]; inside = [q for q in tr if q[0] <= rules['veg_clear_m']]
    yard = [min(p[0] for p in base) - 1, max(p[0] for p in base) + 1, max(p[1] for p in base), 2324.0]
    in_yard = [t for t in wd.trunks if yard[0] <= t[0] <= yard[1] and yard[2] <= t[2] <= yard[3]]
    nt = min(tr, key=lambda q: q[0])
    o.row('O3', not inside and not in_yard, 'vegetation rows (%d sheets) within %.1f m of the new base: %d; on the yard between the base and the road: %d; nearest %s %.1f m away (%.1f, %.1f) -> no row to remove, no vegetation ledger op' % (len(wd.sheets), rules['veg_clear_m'], len(inside), len(in_yard), nt[1][5], nt[0], nt[1][0], nt[1][2]))
    sd = np.array([L.poly_pt_d(base, (float(s[0]), float(s[1]))) for s in wd.seeds]); ns = int((sd <= rules['grass_margin_m']).sum())
    nn = sum(1 for s in wd.seeds if any(math.hypot(s[0] - p[0], s[1] - p[1]) <= rules['grass_margin_m'] for p in at.values()))
    o.row('O4', ns == 0 and nn == 0, 'grass seeds (rule F9, margin %.1f m) within the margin of the new base %d, of the nodes %d -> grass-apply is not needed (reapply_confirmed stays %s)' % (rules['grass_margin_m'], ns, nn, wd.grass['reapply_confirmed']))
    ob = L.rect_poly((hb['pos'][0], 0, hb['pos'][2], hb['yaw']), H['base_rect']); od = np.array([L.poly_pt_d(ob, (float(s[0]), float(s[1]))) for s in wd.seeds])
    o.info('O4', 'old footprint: %d seeds inside, %d in the 6 m ring around it (sparse grass all round: no bald rectangle to see) - the bare slope stays as the terrain draws it' % (int((od == 0).sum()), int(((od > 0) & (od <= 6)).sum())))
    o.open('O5', 'BuildingFix308 Veg holds a vegetation mask area b308veg/%s/%s#0 at the OLD footprint (veg-ledger.json): harmless now (it only keeps procedural trees / rocks out on a re-bake of the sheets); the new footprint has no such area until the next BuildingFix308 Veg plan / apply' % (d['stop'], H['node']))

    o.head('D  data that stores these places')
    for a, con in wd.contents.items():
        pts = {p['Id']: p for p in con['Points']}; cps = {p['Id']: p for p in con['Checkpoints']}
        p = pts['cargo_delivery']['Position']; c = cps['capital_escort_rest']['Feet']; was = wd.stops[a]['nodes']
        ok = abs(float(p['x']) - was['Interaction']['pos'][0]) <= 0.01 and abs(float(p['z']) - was['Interaction']['pos'][2]) <= 0.01 and abs(float(c['x']) - was['Checkpoint']['pos'][0]) <= 0.01 and abs(float(c['z']) - was['Checkpoint']['pos'][2]) <= 0.01
        o.row('D1', ok, '%s content: point cargo_delivery (%.2f, %.2f) radius %s and checkpoint capital_escort_rest feet (%.2f, %.2f) stand on the scene nodes today -> the tool writes both with the scene (ledger cb308-small-delivery-%s.json)' % (a, float(p['x']), float(p['z']), pts['cargo_delivery']['Radius'], float(c['x']), float(c['z']), a))
    row = next(r for r in D['earth']['checks']['stops'] if r['name'] == '인도 객주')
    o.row('D2', abs(row['x'] - at['CargoClerk252'][0]) < 1e-6 and abs(row['z'] - at['CargoClerk252'][1]) < 1e-6, 'earth308.json checks.stops row 인도 객주 (%.2f, %.2f) = the clerk target (the 54 m witness of Content308 Earth check)' % (row['x'], row['z']))
    prow = next(r for r in D['earth']['checks']['stops'] if r['name'] == '인도 정차'); o.row('D2', abs(prow['x'] - park[0]) < 1e-6 and abs(prow['z'] - park[1]) < 1e-6, 'earth308.json row 인도 정차 (%.0f, %.0f) = Parking (unchanged)' % (prow['x'], prow['z']))
    srow = D['smallops']['escort_stops']['stops'][2]
    o.row('D3', srow['id'] == 'cargo_delivery' and 'delivery308.json' in srow['note'] and srow['keep'] == ['Guesthouse252'], 'smallops308.cfg.json cargo_delivery row: keep / lifts / walk_nodes unchanged, the note names the move')
    if wd.campaign is not None:
        stg = next((s for s in wd.campaign.get('Stages', []) if s.get('Id') == 'delivery'), None)
        if stg: o.info('D4', 'campaign stage delivery.Destination today (%.2f, %.2f, %.2f) -> follows the point through Content308 campaign-dry / campaign-apply (ledger_campaign308.json), not through this tool' % (float(stg['Destination']['x']), float(stg['Destination']['y']), float(stg['Destination']['z'])))
    o.info('D5', 'not changed: layout Places[capital_delivery] XZ (%.0f, %.0f), GroundRadius %s (the map marker and the minimap), the two routes, relayout_fix308.json keep_required, content308_keepout.json (no row of this stop)' % (*place, W['place']['GroundRadius']), 'O')
    o.info('D5', 'arrival / talk radius: Points[cargo_delivery].Radius %.1f m (min with DemoEscortRules.MaximumInteractionDistance 4.5) is a field of the point, it moves with it; DemoEscortPresentation uses 7 m (player -> Interaction) / 9 m (companion -> CompanionWait) / 8 m (companion -> CargoWait) - relative to the nodes' % rules['interaction_radius_m'], 'O')
    return S


# ================================================================================================= the bake

def check_bake(o, D, wd, S):
    d = D['delivery']; rules = d['rules']; st = wd.stops['main']; at = S['at']; pk = st['nodes']['Parking']
    o.head('B  the one bake: why the audit line closes (offline proxy of Escort303Regression audit A4)')
    ex = S['exit']
    old_i = (st['nodes']['Interaction']['pos'][0], st['nodes']['Interaction']['pos'][2])
    sag0, sl0 = chord_sag(ex, old_i); sag1, sl1 = chord_sag(ex, at['Interaction'])
    o.info('B1', 'A4 exit = Parking + right x %.1f = (%.2f, %.2f) (first candidate with NavMesh within 1 m [M today: it is the start of the line that fails])' % (rules['a4_exit_side_m'], *ex))
    o.row('B1', sag0 > rules['a4_sag_max_m'], 'today: exit -> Interaction (%.2f, %.2f), %.1f m: the straight chord runs %.2f m UNDER the ground (steepest ground on the way %.1f deg) - the swept capsule (bottom 0.03 m over the chord) starts inside the terrain: the [M] line "blocked by Terrain_03_04 at (0,0,0)"' % (*old_i, math.hypot(ex[0] - old_i[0], ex[1] - old_i[1]), sag0, sl0), 'O/I')
    o.row('B1', sag1 <= rules['a4_sag_max_m'], 'after: exit -> Interaction (%.2f, %.2f), %.1f m: chord %.3f m under the ground at most (<= %.2f), steepest ground %.1f deg' % (*at['Interaction'], math.hypot(ex[0] - at['Interaction'][0], ex[1] - at['Interaction'][1]), max(sag1, 0.0), rules['a4_sag_max_m'], sl1), 'O/I')
    for nm, a, b in (('exit -> companion wait', ex, at['CompanionWait']), ('cargo -> companion wait', at['CargoWait'], at['CompanionWait'])):
        sg, sl = chord_sag(a, b); o.row('B2', sg <= rules['a4_sag_max_m'], 'after: %s %.1f m: chord %.3f m under the ground at most, steepest %.1f deg' % (nm, math.hypot(a[0] - b[0], a[1] - b[1]), max(sg, 0.0), sl), 'O/I')
    hb = S['hbox']; a = ex; b = at['Interaction']; n = 40; dmin = 9.0; who = ''
    for i in range(n + 1):
        t = i / n; p = (a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t)
        dh = max(hb[0] - p[0], p[0] - hb[1], hb[2] - p[1], p[1] - hb[3])
        if dh < dmin: dmin = dh; who = 'house bounds'
    for k, fp in S['solid'].items():
        if math.hypot(at[k][0] - b[0], at[k][1] - b[1]) <= rules['pose_tol_m']: continue          # the clerk stands ON Interaction (B4)
        dd = L.seg_footprint_d(a, b, fp)
        if dd < dmin: dmin = dd; who = k
    o.row('B3', dmin >= rules['a4_path_clear_m'], 'the straight exit -> Interaction line keeps %.2f m from %s (>= %.1f + capsule 0.27 would need %.2f)' % (dmin, who, rules['a4_path_clear_m'], rules['a4_path_clear_m']), 'O/I')
    o.info('B4', 'the clerk stands ON the Interaction point (as the inspectors of checkpoint_1 / _2 do): a PhysicsColliders bake carves his capsule (only EnemyVitals / EnemyController actors are switched off for the bake), the path ends at the rim of that hole - the same line PASSES at checkpoint_1 and checkpoint_2 after the 2026-10-05 bake [M EXTFIX_LOG J7]', 'I')
    o.info('B5', 'expected audit: 53 pass / 1 fail -> 54 pass / 0 fail - an ESTIMATE until the bake (the FAIL row A4 cargo_delivery exit->interaction becomes PASS; the row count stays 54). The three A4 lines of this stop are straight, level and clear of every solid by the sweep capsule + the N8 margin, so they hold whether the bake carves the clerk or not. A1: Interaction / Checkpoint = content within 0.01 (the tool writes both); A2: support + NavMesh <= 0.5 m at CompanionWait / CargoWait / Checkpoint after the bake; A6: the car placement at Parking is unchanged (Parking is not moved; %.2f m to the base)' % L.poly_poly_d(S['carp'], S['base']), 'I')
    o.info('B6', 'NavMesh after the bake [I]: the hole of the 15 house MeshColliders at the old place (x 1803 - 1816, z 2302 - 2314) closes into walkable slope; a new hole opens around the house body (posts and walls 3.3 x 4.8 m + 0.5 m agent radius) - the plinth top is level with the packed surface (kerb <= %.2f m, agentClimb 0.75) so the plinth itself stays walkable; holes around the desk, the bench and the clerk move with them' % (rules['front_step_max_m']), 'I')


# ================================================================================================= the wreck

def check_wreck(o, D, wd, pictures=None):
    pf = D['propfix']; grp = next(g for g in pf['groups'] if g['id'] == 'L3_wreck'); ops = [op for op in pf['ops'] if op.get('group') == 'L3_wreck']; wk = pf['checks']['wreck']; nav = pf['checks']['navmesh']
    o.head('W  the fallen wreck (propfix308.json L3_wreck, %d ops) - real FBX meshes, full matrices' % len(ops))
    o.row('W1', grp.get('enabled') is True and grp.get('moves_colliders') is True and 'D308-22' in str(grp.get('bake_decision', '')), 'group L3_wreck: enabled %s, moves_colliders %s, bake_decision names D308-22' % (grp.get('enabled'), grp.get('moves_colliders')))
    world_ops = [op for op in ops if op.get('world')]
    o.row('W1', (not world_ops) or grp.get('world_ops') is True, '%d world op(s) (%s): the group says world_ops %s and names skip_root %s' % (len(world_ops), ', '.join(op['id'] for op in world_ops), grp.get('world_ops'), grp.get('skip_root')))
    roots = pf['key_roots']
    o.row('W1', all(any(op['key'].startswith(r) for r in roots) for op in ops), 'every op key starts with a listed key root')
    w0 = wd.wreck; errs = [k for k, v in w0.mesh.items() if v is None]
    o.row('W2', not errs, 'the palanquin meshes load (cabin %d, roof %d, wheel %d, chest %d triangles); the cabin / roof boxes of the FBX = the BoxColliders the builder fitted' % tuple(len(w0.mesh[k][1]) if w0.mesh.get(k) is not None else 0 for k in ('Cabin', 'Roof', 'Wheel', 'SM_M_WoodenBox')))
    for op in ops:
        n = w0.nodes.get(op['key'])
        if n is None: o.row('W3', False, '%s: %s is not in the scene' % (op['id'], op['key'])); continue
        eb = op['expect_before']; dq = WK.q_angle(n['rot'], WK.euler_q(eb['euler'])); dp = max(abs(a - b) for a, b in zip(n['pos'], eb['pos']))
        o.row('W3', dp <= pf['tolerances']['before_m'] and dq <= pf['tolerances']['before_deg'], '%s stands on expect_before (%.3f m, %.2f deg off)' % (op['id'], dp, dq))
    w = w0.copy(); root = w.world_pos(WK.WRECK); rid = min(wd.W['routes'], key=lambda r: min(L.seg_d((root[0], root[2]), a, b) for a, b in zip(wd.W['routes'][r], wd.W['routes'][r][1:]))); road = wd.W['routes'][rid]
    info = {}
    for op in ops: info[op['id']] = w.apply_op(op)
    body = WK.WRECK + '/InertPalanquinWreck'; moved = {}
    for op in ops:
        key = op['key']; ck = op.get('check') or {}
        cc = w.collider_corners(key); pts = np.concatenate([c for _, c in cc]) if cc else np.zeros((0, 3))
        own = [c for k_, c in cc if not any(k_ == o2['key'] or k_.startswith(o2['key'] + '/') for o2 in ops if o2['key'] != key and o2['key'].startswith(key + '/'))]
        pts_own = np.concatenate(own) if own else pts
        lo, hi = pts_own.min(0), pts_own.max(0); probes = [(lo[0], lo[2]), (lo[0], hi[2]), (hi[0], lo[2]), (hi[0], hi[2]), ((lo[0] + hi[0]) / 2, (lo[2] + hi[2]) / 2)]
        droad = min(min(L.seg_d(p, a, b) for a, b in zip(road, road[1:])) for p in probes)
        far = max(math.hypot(p[0] - root[0], p[1] - root[2]) for p in probes[:4])
        dmax = max(float(np.linalg.norm(w.nodes[key]['pos'])) if False else 0.0, 0.0)
        rl, rh = w.renderer_bounds(key); g = L.h((rl[0] + rh[0]) / 2, (rl[2] + rh[2]) / 2)
        ok = True; notes = []
        if ck.get('road'): ok &= droad >= wk['piece_road_clear_m'] + wk['road_margin_min_m']; notes.append('collider bounds %.2f m from the centre of %s (rule %.1f + offline margin %.1f)' % (droad, rid, wk['piece_road_clear_m'], wk['road_margin_min_m']))
        ok &= far <= nav['disc_m']; notes.append('reach %.2f m from the root (<= %.1f)' % (far, nav['disc_m']))
        if ck.get('axis'):
            q = w.world_q(key); v = L.B.qrot(q, tuple(ck['axis'])); tilt = math.degrees(math.acos(min(1.0, abs(v[1]) / math.sqrt(sum(c * c for c in v)))))
            if ck.get('tilt_min_deg') is not None: ok &= tilt >= ck['tilt_min_deg']
            if ck.get('tilt_max_deg') is not None: ok &= tilt <= ck['tilt_max_deg']
            notes.append('axis %s %.1f deg off the vertical (%s .. %s)' % (ck['axis'], tilt, ck.get('tilt_min_deg', '-'), ck.get('tilt_max_deg', '-')))
        if ck.get('trunk'):
            tr = min((math.hypot(t[0] - (rl[0] + rh[0]) / 2, t[2] - (rl[2] + rh[2]) / 2) for t in wd.trunks_w), default=99.0); ok &= tr >= wk['trunk_clear_m']; notes.append('nearest trunk %.1f m (>= %.1f)' % (tr, wk['trunk_clear_m']))
        for ap in ck.get('apart', []):
            al, ah = w.renderer_bounds(ap['key']); gapb = max(max(al[i] - rh[i], rl[i] - ah[i]) for i in range(3)); ok &= gapb >= ap['min_m']; notes.append('%.2f m clear of %s (>= %.1f)' % (gapb, ap['key'].split('/')[-1], ap['min_m']))
        notes.append('renderer bounds %.2f x %.2f x %.2f m, bottom %+.2f m against the ground under its centre%s' % (rh[0] - rl[0], rh[1] - rl[1], rh[2] - rl[2], rl[1] - g, ', seat shift %+.2f m' % info[op['id']]['seat_shift'] if 'seat_shift' in info[op['id']] else ''))
        moved[op['id']] = dict(key=key, road=droad, far=far)
        o.row('W4', ok, '%s: %s' % (op['id'], '; '.join(notes)))
    # travel of the colliders (the stale NavMesh patch) and the look numbers
    c0 = dict(w0.collider_corners(WK.WRECK)); c1 = dict(w.collider_corners(WK.WRECK)); trav = max(float(np.linalg.norm(c1[k].mean(0) - c0[k].mean(0))) for k in c0)
    o.info('W5', '%d BoxColliders of the set move, the farthest by %.2f m (fix 2 pose: 5.65 m) - inside the %.1f m disc of the root; nearest encounter rule %d m (fix 2 dry)' % (len(c0), trav, nav['disc_m'], nav['encounter_min_m']))
    cab = w.tris(body + '/Cabin'); roofk = body + '/Roof'
    if roofk in w.nodes:
        rt = w.tris(roofk); dsep = float(np.linalg.norm(rt.reshape(-1, 3).mean(0)[[0, 2]] - cab.reshape(-1, 3).mean(0)[[0, 2]]))
        rl, rh = w.renderer_bounds(roofk); roof_in = sum(1 for p in rt.reshape(-1, 3)[::7] if p[1] < L.h(p[0], p[2])) / max(1, len(rt.reshape(-1, 3)[::7]))
        cab_in = sum(1 for p in cab.reshape(-1, 3)[::7] if p[1] < L.h(p[0], p[2])) / max(1, len(cab.reshape(-1, 3)[::7]))
        rt0 = w0.tris(roofk); cab0 = w0.tris(body + '/Cabin'); d0 = float(np.linalg.norm(rt0.reshape(-1, 3).mean(0)[[0, 2]] - cab0.reshape(-1, 3).mean(0)[[0, 2]]))
        o.row('W6', dsep >= 3.0, 'look: the roof lies %.1f m from the cabin (today %.1f m: on it); %.0f %% of its vertices are under the ground (one rim dug in), the cabin %.0f %%' % (dsep, d0, 100 * roof_in, 100 * cab_in))
        up0 = L.B.qrot(w0.world_q(body), (0.0, 1.0, 0.0)); up1 = L.B.qrot(w.world_q(body), (0.0, 1.0, 0.0))
        o.row('W6', abs(up1[1]) < 0.5 and abs(up0[1]) > 0.8, 'look: the body lies on its side (its up axis %.0f deg off the vertical; today %.0f) with the open top of the cabin showing (the roof is gone from it)' % (math.degrees(math.acos(min(1, abs(up1[1])))), math.degrees(math.acos(min(1, abs(up0[1]))))))
    low = float(min(p[1] - L.h(p[0], p[2]) for p in cab.reshape(-1, 3)[::5])); o.info('W6', 'cabin: lowest vertex %.2f m under the ground (half sunk on its lower flank)' % -low)
    lows = {}
    for key in [op['key'] for op in ops]:
        t = w.tris(key).reshape(-1, 3)
        if len(t): lows[key.split('/')[-1]] = float(min(p[1] - L.h(p[0], p[2]) for p in t[::3]))
    hang = {k: v for k, v in lows.items() if v > wk['gap_max_m']}
    o.row('W7', not hang, 'no piece hangs: lowest vertex of each posed piece against the ground %s (<= %.2f m over it)' % (', '.join('%s %+.2f' % kv for kv in lows.items()), wk['gap_max_m']))
    for op in ops:                     # review F6: what Fix2 verify measures for a seat_once op (F2OwnGap) - the number to hold against the editor's `num <id>.own_gap`
        if not op.get('seat_once'): continue
        gone = [o2['key'] for o2 in ops if o2.get('world') and o2['key'].startswith(op['key'] + '/')]
        lo, hi = w.renderer_bounds(op['key'], skip=gone); og = float(lo[1] - L.h((lo[0] + hi[0]) / 2, (lo[2] + hi[2]) / 2)); lim = (op.get('check') or {}).get('own_gap_max_m')
        o.row('W8', lim is not None and abs(lim - wk['gap_max_m']) < 1e-9 and og <= lim, '%s (seat_once): own renderer bounds without %d carried-away child(ren) (%s) - bottom %+.2f m against the ground under their centre (check.own_gap_max_m %s = checks.wreck.gap_max_m %.2f): Fix2 verify measures this as `num %s.own_gap`' % (op['id'], len(gone), ', '.join(g.split('/')[-1] for g in gone) or '-', og, lim, wk['gap_max_m'], op['id']))
    return dict(before=w0, after=w, root=root, road=road, route=rid)


# ================================================================================================= guards on the tool text

def check_code(o, D):
    o.head('C  the tool text')
    src = (STAGE / 'Editor/WorldMacro/SmallOps308.Delivery.cs').read_text(encoding='utf-8')
    code = '\n'.join(ln for ln in src.splitlines() if not ln.strip().startswith('//'))
    nums = [s for s in ('1824', '2312', '1822.75', '2317', '106.65', '311.19', '270f', '3.565', '0.3205') if s in code]
    o.row('C1', not nums, 'SmallOps308.Delivery.cs carries no place / level / rectangle number of the data (found %s)' % (nums or 'none'))
    o.row('C1', 'DisplayDialog' not in code and 'SaveAssets()' not in code, 'no modal dialog, no AssetDatabase.SaveAssets in the tool (SaveAssetIfDirty on the one content asset)')
    o.row('C1', 'static ' not in '\n'.join(ln for ln in code.splitlines() if 'static' in ln and '(' not in ln and 'const' not in ln and 'class' not in ln), 'no static field in the new partial (domain reload is off: nothing to reset)')
    o.row('C1', 'l.state = "applying"' in code and code.index('l.state = "applying"') < code.index('k.House.SetPositionAndRotation'), 'the ledger is written ("applying") before the first transform / asset write')
    o.row('C1', 'blocked' in code and 'moves_colliders' in code and 'bake_decision' in code, 'apply refuses a blocked variant and a data file without moves_colliders + bake_decision')
    f2 = (STAGE / 'Editor/WorldMacro/ContentSeat308.Fix2.cs').read_text(encoding='utf-8')
    o.row('C2', 'world_ops' in f2 and 'disc_m' in f2 and 'seat_once' in f2, 'ContentSeat308.Fix2 (fix 3): the world op is guarded by the group flag world_ops, a skip_root above the object and checks.navmesh.disc_m; seat_once is read')
    o.row('C2', 'static bool F2OwnGap' in f2 and '.own_gap ' in f2 and 'own_gap_max_m' in f2, 'ContentSeat308.Fix2 verify (review F6): a seat_once op is MEASURED as it stands (own renderers without the children a world op carried away) - num <id>.own_gap and the row check.own_gap_max_m')
    dsrc = (STAGE / 'Editor/WorldMacro/SmallOps308.Delivery.cs').read_text(encoding='utf-8')
    o.row('C1', 'collider_rect_slack_m' in dsrc and '+ .05f' not in dsrc and '<= .01f' not in dsrc, 'SmallOps308.Delivery.cs verify (review F9): the collider-rectangle slack and the A1 tolerance are read from the data (rules.collider_rect_slack_m, rules.pose_tol_m)')
    pr = (STAGE / 'Editor/WorldMacro/ContentSeat308.Props.cs').read_text(encoding='utf-8')
    o.row('C2', 'Fix2Reposed(k, ch.key' in pr and 'static bool Fix2Reposed' in f2, 'ContentSeat308 verify D8 (fix 3): the seat row of a piece that Fix2 re-posed is left to Fix2 verify (the bounds of the body change when its roof is carried away); the per-piece road rule still runs')
    return None


def run(D, wd, pictures=False):
    o = Rep(); o.say('relayout_fix3 dry - D308-22 answer 1 (delivery stop C1) + the fallen wreck. delivery data %s, propfix %s' % (D['delivery']['version'], D['propfix']['version']))
    S = check_delivery(o, D, wd); check_bake(o, D, wd, S); WR = check_wreck(o, D, wd); check_code(o, D)
    f = o.fails(); o.say(''); o.say('RESULT PASS %d / FAIL %d / OPEN %d' % (len(o.rows) - len(f), len(f), sum(1 for ln in o.lines if ln.startswith('OPEN '))))
    return o, S, WR


# ================================================================================================= mutations

def mutations(wd):
    base = load_data(); out = []; caught = 0
    def M(name, fn, want):
        nonlocal caught
        D = copy.deepcopy(base); fn(D); o = Rep()
        try:
            S = check_delivery(o, D, wd); check_bake(o, D, wd, S); check_wreck(o, D, wd)
        except Exception as e:
            o.row('X', False, 'the dry stops: %r' % e)
        red = sorted({r[0] for r in o.fails()}); hit = any(w in red for w in want) or (want == ['X'] and 'X' in red); caught += hit
        out.append('%s  %s -> red %s (want one of %s)' % ('CAUGHT' if hit else 'MISSED', name, red or 'none', want))
    H = lambda D: D['delivery']['house']; N = lambda D, n: next(x for x in D['delivery']['nodes'] if x['node'] == n)
    M('the option picture pose is switched on (variant C1_as_pictured, its blocked mark removed)', lambda D: (H(D).update(variant='C1_as_pictured'), H(D)['variants']['C1_as_pictured'].pop('blocked')), ['P6', 'P2'])
    M('the house 0.6 m further north (onto the road bed)', lambda D: H(D)['variants']['S_south_rim'].update(xz=[1824.0, 2313.0]), ['P2', 'P3'])
    M('the house kept at yaw 311.19 on the chosen XZ', lambda D: H(D)['variants']['S_south_rim'].update(yaw=311.19), ['P6', 'P2', 'N1'])
    M('the house 3 m further west (its end over the falling rim)', lambda D: H(D)['variants']['S_south_rim'].update(xz=[1820.0, 2312.4]), ['P6'])
    M('door to the yard: yaw 0 on the south rim', lambda D: H(D)['variants']['S_south_rim'].update(yaw=0.0, xz=[1824.0, 2310.5]), ['P6', 'P7'])
    M('plinth top of the data 0.10 m off the mesh', lambda D: H(D).update(plinth_top_local_m=0.42), ['P4', 'P1'])
    M('base rectangle of the data 0.3 m too narrow', lambda D: H(D).update(base_rect=[-3.565, 2.97, -5.0, 5.0]), ['P4'])
    M('the clerk moved into the lane', lambda D: (N(D, 'Interaction').update(xz=[1822.75, 2318.9]), N(D, 'CargoClerk252').update(xz=[1822.75, 2318.9])), ['N1'])
    M('the clerk under the eaves (inside the house bounds)', lambda D: (N(D, 'Interaction').update(xz=[1822.75, 2315.8]), N(D, 'CargoClerk252').update(xz=[1822.75, 2315.8])), ['N1'])
    M('CompanionWait left on the old slope', lambda D: N(D, 'CompanionWait').update(xz=[1815.11, 2319.06]), ['N1'])
    M('CompanionWait beside the parked car', lambda D: N(D, 'CompanionWait').update(xz=[1821.3, 2318.6]), ['N1'])
    M('the bench 2.2 m from the respawn feet', lambda D: N(D, 'capital_escort_rest').update(xz=[1827.3, 2316.2]), ['N3'])
    M('the bench lift differs from the stops rule', lambda D: N(D, 'capital_escort_rest').update(lift=0.4), ['N3'])
    M('the desk 3 m from the clerk', lambda D: N(D, 'InspectionDesk252').update(xz=[1825.75, 2317.4]), ['N3'])
    M('the clerk row does not follow Interaction', lambda D: N(D, 'CargoClerk252').update(xz=[1823.4, 2317.4]), ['N2'])
    M('CargoWait on top of the desk', lambda D: N(D, 'CargoWait').update(xz=[1824.6, 2317.2]), ['N5'])
    M('the first layout of the walk nodes (review F4: the cargo -> companion line passes the clerk at 0.28 m)', lambda D: (N(D, 'CargoWait').update(xz=[1823.75, 2316.75]), N(D, 'CompanionWait').update(xz=[1821.6, 2317.5])), ['N8'])
    M('CompanionWait north-west of the clerk with CargoWait left south-east of him (the line still crosses him)', lambda D: N(D, 'CompanionWait').update(xz=[1821.9, 2318.0]), ['N8'])
    M('the cargo box set down against the desk', lambda D: N(D, 'CargoWait').update(xz=[1824.05, 2316.75]), ['N9'])
    M('a walk line named in the rules that is not a node', lambda D: D['delivery']['rules']['walk_lines'].append(['CargoWait', 'StagingApproach']), ['N8'])
    M('the solid node list of the rules forgets the bench', lambda D: D['delivery']['rules']['solid_nodes'].remove('capital_escort_rest'), ['N5'])
    M('the respawn feet inside 54 m of the road enemy', lambda D: (N(D, 'Checkpoint').update(xz=[1829.5, 2317.6]), N(D, 'capital_escort_rest').update(xz=[1829.5, 2316.2])), ['N7'])
    M('Parking listed as a node to move', lambda D: (D['delivery']['nodes'].append({'node': 'Parking', 'xz': [1820.0, 2320.0]}), D['delivery'].update(keep=[])), ['S3', 'P9'])
    M('a stop child forgotten (CargoWait row removed)', lambda D: D['delivery']['nodes'].remove(N(D, 'CargoWait')), ['S3', 'X'])
    M('no bake decision on the move', lambda D: D['delivery'].update(bake_decision=''), ['S5'])
    M('earth308.json row left at the old clerk place', lambda D: next(r for r in D['earth']['checks']['stops'] if r['name'] == '인도 객주').update(x=1816.05, z=2315.48), ['D2'])
    M('the Interaction left on the slope (today\'s audit line)', lambda D: (N(D, 'Interaction').update(xz=[1816.05, 2315.48]), N(D, 'CargoClerk252').update(xz=[1816.05, 2315.48])), ['B1', 'N1'])
    P = lambda D, i: next(x for x in D['propfix']['ops'] if x['id'] == i)
    M('wreck: the group has no bake decision', lambda D: next(g for g in D['propfix']['groups'] if g['id'] == 'L3_wreck').update(bake_decision=''), ['W1'])
    M('wreck: the group is off', lambda D: next(g for g in D['propfix']['groups'] if g['id'] == 'L3_wreck').update(enabled=False), ['W1'])
    M('wreck: world ops without the group flag', lambda D: next(g for g in D['propfix']['groups'] if g['id'] == 'L3_wreck').pop('world_ops'), ['W1'])
    M('wreck: the roof thrown onto the road side (4.5 m from the centre line)', lambda D: P(D, 'L3_roof_off')['world'].update(xz=[2651.5, 1969.0]), ['W4'])
    M('wreck: the roof thrown 11 m from the root', lambda D: P(D, 'L3_roof_off')['world'].update(xz=[2640.0, 1978.0]), ['W4'])
    M('wreck: the roof left on the cabin (the look op removed)', lambda D: D['propfix']['ops'].remove(P(D, 'L3_roof_off')), ['W6'])
    M('wreck: the loose wheel standing on its rim', lambda D: P(D, 'L3_wheel_off')['world'].update(euler=[0, 35, 0]), ['W4'])
    M('wreck: the roof hanging 0.5 m over the ground', lambda D: P(D, 'L3_roof_off').update(seat=0.5), ['W7'])
    M('wreck: the body left hanging (seat +0.6) - the seat_once op has no seat row, the own-gap row must see it', lambda D: P(D, 'L3_body').update(seat=0.6), ['W8'])
    M('wreck: the own-gap limit taken off the seat_once op', lambda D: P(D, 'L3_body')['check'].pop('own_gap_max_m'), ['W8'])
    M('wreck: the body not rolled (fix 2 euler removed)', lambda D: P(D, 'L3_body').update(euler=[5, 28, 18]), ['W4', 'W6'])
    M('wreck: an op outside the key roots', lambda D: P(D, 'L3_wheel_off').update(key='Roadside303/hamlet_cart303/Wheel'), ['W1', 'W3'])
    out.append(''); out.append('mutations %d / %d caught' % (caught, len(out) - 1))
    return out, caught == len(out) - 2


def main():
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
    args = sys.argv[1:]; wd = World()
    if args and args[0] == 'mutations':
        lines, ok = mutations(wd); (STAGE / 'Dry').mkdir(parents=True, exist_ok=True)
        (STAGE / 'Dry/relayout_fix3_mutations.txt').write_text('\n'.join(lines), encoding='utf-8'); print('\n'.join(lines)); return 0 if ok else 2
    D = load_data('--deployed' in args); o, S, WR = run(D, wd)
    (STAGE / 'Dry').mkdir(parents=True, exist_ok=True)
    (STAGE / 'Dry/relayout_fix3_dry.txt').write_text('\n'.join(o.lines), encoding='utf-8')
    (STAGE / 'Dry/relayout_fix3_dry.json').write_text(json.dumps(dict(rows=o.rows, result=dict(passed=len(o.rows) - len(o.fails()), failed=len(o.fails()))), ensure_ascii=False, indent=1), encoding='utf-8')
    print('\n'.join(o.lines))
    if '--pictures' in args:
        import relayout_fix3_draw as DR
        for p in DR.draw_all(D, wd, S, WR): print('wrote', p)
    return 0 if not o.fails() else 2


if __name__ == '__main__':
    sys.exit(main())
