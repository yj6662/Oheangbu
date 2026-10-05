"""jangseong308 - long wall (jangseong) line options at the south pass gate. SPEC-WORLD-CLIFF-BOUNDARY-308 section 6, Q3 = C (D308-9c).
Offline, numpy + PIL only, read-only on the project. Every number and every input path comes from the params file
(default <CB>/wall_mid/params.json); the outputs go into the folder of that params file.

usage (repo root):
  python Tools/Art/jangseong308.py snapshot [params.json]   window inputs from the #308 scratch lattice -> <CB>/<inputs.snapshotDir>/inputs.npz + inputs.json
                                                            (only this step needs terrain_first/tools/tf_base and its scratch cache; run once)
  python Tools/Art/jangseong308.py study [params.json]      options foot / mid / crest for both wings (+ east ridge upper bound)
                                                            -> <params folder>/options.json, options_map.png, sections.png
  python Tools/Art/jangseong308.py layout [params.json]     preview lines for WallLook308 (line=mid) -> <CB>/<inputs.layoutOut>
  The stage height the study is measured on is inputs.stageHeight: wall_mid/params.json = the v4 prototype the 2026-10-04 stills
  were taken with, wall_mid/v5/params.json = the ops-v5 stage-1a height of plan.3 (west foot line = gate_wall.west.line_plan3).
  python Tools/Art/jangseong308.py build [params.json]       phase 1b: the wall + gate layout on the user-fixed FOOT lines (D308-9d / 9e)
                                                            -> <CB>/wall_1b/jangseong308.json (read by the editor ledger Jangseong308)
  python Tools/Art/jangseong308.py build-check [params.json] the layout acceptance lines (grade, top over the EA ground, end fill, counts,
                                                            bastions, masks, gate floor, encounter distance, budget, byte-stable second run)
                                                            -> <CB>/wall_1b/jangseong308_check.txt + a1_table.md
  build / build-check read <CB>/wall_1b/params.json by default; they never write outside <CB>/wall_1b.

Measures (same definitions as plan/tools/step4_review.py wall_check / bench_cells so the foot rows reproduce review308.json):
  module  = the line resampled at the pack module length; visible = stage-height fill under the module centre <= buriedFill
  A1      = highest NATURAL ground on the late side within a1Radius (half disc, more than a1SideMin behind the line) minus the
            natural ground under the module centre; limit a1Limit (LORE_CHECK A1)
  bench   = 8 m bed along the line levelled across to the grade-capped line profile; cells the cliff op raised and hard-mask
            cells (route corridor, protected, wet, water) are left alone
Grid: 4 m, arrays [i = z/4, j = x/4], 1501 x 1001 (plan/Stage/FORMAT.md - the file format, not a tuning number)."""
import sys, json, math, hashlib, heapq, time
from pathlib import Path
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
CB = ROOT / 'Art/World/Compact/Rebuild/CliffBoundary308'
OUT = CB / 'wall_mid'              # set from the params file in main(): its folder
PARAMS = OUT / 'params.json'
ASSETS = ROOT / 'Oheangbu/Assets/_Project'
BASE = ASSETS / 'Art/World/Finish297/Surface/height.bytes'
PROTO = CB / 'plan/Stage/height_p1a_PROTOTYPE.bytes'   # stage height; replaced by params inputs.stageHeight (configure)
PROTECTED = ASSETS / 'Art/World/Watershed295/Surface/protected.bytes'
WET = ROOT / 'Art/World/Compact/Rebuild/Watershed295/Generated/wet.bytes'
PLAN = CB / 'plan/boundary308.json'
REFIT = CB / 'plan/wall_east_crest_refit.json'
WCREST = CB / 'mesh_first/wallcrest.json'
LAYOUT = CB / 'look/wall/wall_layout.json'
SNAP = OUT                         # folder of inputs.json / inputs.npz (params inputs.snapshotDir)
LAYOUT_OUT = CB / 'look/wall/wall_layout_mid.json'
FOOT_KEY = dict(west='line', east='line')
HH, W, CELL = 1501, 1001, 4.0


def configure(params_path):
    """Paths from the params file. Without an inputs block the tool reads what it read before (prototype height, plan lines)."""
    global OUT, PARAMS, PROTO, PLAN, SNAP, LAYOUT_OUT, FOOT_KEY
    PARAMS = Path(params_path).resolve(); OUT = PARAMS.parent
    P = json.load(open(PARAMS, encoding='utf-8')); inp = P.get('inputs', {})
    PROTO = CB / inp.get('stageHeight', 'plan/Stage/height_p1a_PROTOTYPE.bytes'); PLAN = CB / inp.get('plan', 'plan/boundary308.json')
    SNAP = CB / inp.get('snapshotDir', 'wall_mid'); LAYOUT_OUT = CB / inp.get('layoutOut', 'look/wall/wall_layout_mid.json')
    FOOT_KEY = dict(west='line', east='line'); FOOT_KEY.update(inp.get('footKey', {}))
    return P
sha = lambda p: hashlib.sha256(open(p, 'rb').read()).hexdigest()
rel = lambda p: str(Path(p).relative_to(ROOT)).replace(chr(92), '/')
r1 = lambda v: round(float(v), 1)
r2 = lambda v: round(float(v), 2)


def write_json(path, data, indent=1):
    open(path, 'wb').write((json.dumps(data, ensure_ascii=False, indent=indent) + '\n').encode('utf-8'))


# ---------------------------------------------------------------------------------------------------------------- geometry
def sample(f, x, z):
    x = np.clip(np.asarray(x, float), 0, 3999.99); z = np.clip(np.asarray(z, float), 0, 5999.99)
    j = x / CELL; i = z / CELL; j0 = np.floor(j).astype(int); i0 = np.floor(i).astype(int); fj = j - j0; fi = i - i0
    j1 = np.minimum(j0 + 1, W - 1); i1 = np.minimum(i0 + 1, HH - 1)
    return f[i0, j0] * (1 - fj) * (1 - fi) + f[i0, j1] * fj * (1 - fi) + f[i1, j0] * (1 - fj) * fi + f[i1, j1] * fj * fi


def plen(P):
    P = np.asarray(P, float); return float(np.hypot(*np.diff(P, axis=0).T).sum())


def resample(P, step):
    """Evenly spaced points (spacing <= step) - the plan tools' module stations."""
    P = np.asarray(P, float); seg = np.hypot(*np.diff(P, axis=0).T); s = np.concatenate([[0], np.cumsum(seg)])
    n = max(2, int(math.ceil(s[-1] / step)) + 1); t = np.linspace(0, s[-1], n)
    return np.stack([np.interp(t, s, P[:, 0]), np.interp(t, s, P[:, 1])], 1), t


def dist_to_polyline(P, x, z):
    """Unsigned distance, side (+ = left of the walking direction) and arc length of the nearest point, for arrays x / z."""
    P = np.asarray(P, float); x = np.asarray(x, float); z = np.asarray(z, float)
    best = np.full(x.shape, np.inf); side = np.zeros(x.shape); arc = np.zeros(x.shape); acc = 0.0
    for k in range(len(P) - 1):
        ax, az = P[k]; bx, bz = P[k + 1]; L = math.hypot(bx - ax, bz - az)
        if L < 1e-6: continue
        tx, tz = (bx - ax) / L, (bz - az) / L
        s = np.clip((x - ax) * tx + (z - az) * tz, 0, L); q = -(x - ax) * tz + (z - az) * tx
        d = np.hypot(x - (ax + tx * s), z - (az + tz * s)); better = d < best
        best = np.where(better, d, best); side = np.where(better, np.where(q >= 0, 1.0, -1.0), side); arc = np.where(better, acc + s, arc)
        acc += L
    return best, side, arc


def poly_mask(poly, X, Z):
    """Even-odd point in polygon on arrays."""
    P = np.asarray(poly, float); n = len(P); inside = np.zeros(X.shape, bool)
    for k in range(n):
        x1, z1 = P[k]; x2, z2 = P[(k + 1) % n]
        cond = (z1 > Z) != (z2 > Z)
        with np.errstate(divide='ignore', invalid='ignore'):
            xint = (x2 - x1) * (Z - z1) / (z2 - z1 + 1e-12) + x1
        inside ^= cond & (X < xint)
    return inside


def dp(P, tol):
    """Douglas-Peucker."""
    P = np.asarray(P, float)
    if len(P) < 3: return P
    a, b = P[0], P[-1]; ab = b - a; n = np.hypot(*ab)
    d = np.abs((P[:, 0] - a[0]) * ab[1] - (P[:, 1] - a[1]) * ab[0]) / (n + 1e-9); k = int(d.argmax())
    if d[k] <= tol: return np.array([a, b])
    return np.vstack([dp(P[:k + 1], tol)[:-1], dp(P[k:], tol)])


def turns(P):
    P = np.asarray(P, float); seg = np.diff(P, axis=0); ang = np.degrees(np.arctan2(seg[:, 1], seg[:, 0]))
    return (np.diff(ang) + 180) % 360 - 180


# ---------------------------------------------------------------------------------------------------------------- inputs
def snapshot(P):
    """Window copy of the inputs that only exist in the #308 scratch cache (route polylines, water lattice, Seal308 closed reach)."""
    sys.path.insert(0, str(CB / 'terrain_first/tools'))
    import tf_base as tf
    x0, z0, x1, z1 = P['window']; j0, j1, i0, i1 = x0 // 4, x1 // 4, z0 // 4, z1 // 4
    lat = tf.lattice(); M = tf.meta(); rcl, scl = tf.seal('closed'); routes = []
    for r in M['routes']:
        Q = np.asarray(r['pts'], float); sel = (Q[:, 0] > x0 - 200) & (Q[:, 0] < x1 + 200) & (Q[:, 1] > z0 - 200) & (Q[:, 1] < z1 + 200)
        if not sel.any(): continue
        k = np.nonzero(sel)[0]; a, b = max(0, k[0] - 1), min(len(Q), k[-1] + 2)
        routes.append(dict(id=r['id'], width=float(r['width']), source=r.get('source', ''), pts=[[r1(x), r1(z)] for x, z in Q[a:b]]))
    SNAP.mkdir(parents=True, exist_ok=True)
    np.savez_compressed(SNAP / 'inputs.npz', water=lat['water4'][i0:i1 + 1, j0:j1 + 1], inside=lat['inside4'][i0:i1 + 1, j0:j1 + 1], reach_closed=rcl[i0:i1 + 1, j0:j1 + 1], solid_closed=scl[i0:i1 + 1, j0:j1 + 1])
    seal = ROOT / 'Art/World/Compact/Rebuild/Enclosure305/Out/Seal308/closure/_work'
    write_json(SNAP / 'inputs.json', dict(window=P['window'], taken=time.strftime('%Y-%m-%dT%H:%M:%S'), routes=routes,
                                         sources=dict(lattice='#308 scratch cache cliffb/lat.npz + lat.pkl (Enclosure305/emptiness305.py header: routes, water)',
                                                      reach_closed=dict(path=rel(seal / 'reach-closed.npy'), sha256=sha(seal / 'reach-closed.npy')),
                                                      solid_closed=dict(path=rel(seal / 'solid-closed.npy'), sha256=sha(seal / 'solid-closed.npy')))))
    print('snapshot:', len(routes), 'routes,', 'window', P['window'], '->', rel(SNAP / 'inputs.npz'))


class Fields:
    def __init__(self, P):
        self.P = P; lim = P['limits']
        self.h = np.fromfile(BASE, '<f4').reshape(HH, W).astype(np.float64)
        self.new = np.fromfile(PROTO, '<f4').reshape(HH, W).astype(np.float64); self.fill = self.new - self.h
        self.pm = np.fromfile(PROTECTED, np.uint8).reshape(HH, W) > 0; self.wet = np.fromfile(WET, np.uint8).reshape(HH, W) > 0
        inp = json.load(open(SNAP / 'inputs.json', encoding='utf-8')); z = np.load(SNAP / 'inputs.npz')
        if inp['window'] != P['window']: raise SystemExit('inputs.json window differs from params.json: run snapshot again')
        x0, z0, x1, z1 = P['window']; self.j0, self.j1, self.i0, self.i1 = x0 // 4, x1 // 4, z0 // 4, z1 // 4
        self.routes = inp['routes']; self.inputs = inp
        self.water = np.zeros((HH, W), bool); self.reach = np.zeros((HH, W), bool)
        self.water[self.i0:self.i1 + 1, self.j0:self.j1 + 1] = z['water']; self.reach[self.i0:self.i1 + 1, self.j0:self.j1 + 1] = z['reach_closed']
        self.X, self.Z = np.meshgrid(np.arange(W) * CELL, np.arange(HH) * CELL)
        win = (slice(self.i0, self.i1 + 1), slice(self.j0, self.j1 + 1)); self.win = win
        Xw, Zw = self.X[win], self.Z[win]
        # hard masks: route corridor (half width + margin), and the same corridor widened by the bench half width (wall centreline keep-out)
        self.route_hard = np.zeros((HH, W), bool); self.route_keep = np.zeros((HH, W), bool); self.route_d = {}
        for r in self.routes:
            d, _, _ = dist_to_polyline(r['pts'], Xw, Zw); self.route_d[r['id']] = d
            self.route_hard[win] |= d <= r['width'] / 2 + lim['routeMargin']; self.route_keep[win] |= d <= r['width'] / 2 + lim['routeMargin'] + lim['benchHalfWidth']
        self.hard = self.route_hard | self.pm | self.wet | self.water
        self.raised = self.fill > lim['changeEps']
        gz, gx = np.gradient(self.h, CELL); self.slope = np.degrees(np.arctan(np.hypot(gx, gz)))
        # A1 proxy for the search: highest natural ground within the radius (full disc) minus the ground
        R = int(math.ceil(lim['a1Radius'] / CELL)); sub = self.h[win]; mx = np.full(sub.shape, -1e9)
        pad = self.h[self.i0 - R:self.i1 + R + 1, self.j0 - R:self.j1 + R + 1]
        for di in range(-R, R + 1):
            for dj in range(-R, R + 1):
                if (di * CELL) ** 2 + (dj * CELL) ** 2 > lim['a1Radius'] ** 2: continue
                np.maximum(mx, pad[R + di:R + di + sub.shape[0], R + dj:R + dj + sub.shape[1]], out=mx)
        self.a1 = np.full((HH, W), np.nan); self.a1[win] = mx - sub
        plan = json.load(open(PLAN, encoding='utf-8'))['gate_wall']
        self.lines = dict(west=dict(foot=plan['west'][FOOT_KEY['west']], crest=json.load(open(WCREST, encoding='utf-8'))['W-W']['pts']),
                          east=dict(foot=plan['east'][FOOT_KEY['east']], crest=json.load(open(REFIT, encoding='utf-8'))['line']))


# ---------------------------------------------------------------------------------------------------------------- measure
def measure(F, line, late_left, foot=None):
    P = F.P; lim = P['limits']; mod = P['module']; ms = P['measure']; eps = lim['changeEps']; L = mod['length']; h = F.h
    line = np.asarray(line, float); Q, s = resample(line, L); step = float(s[1] - s[0])
    mid = (Q[:-1] + Q[1:]) / 2; t = np.diff(Q, axis=0); t /= np.linalg.norm(t, axis=1, keepdims=True)
    late = np.stack([-t[:, 1], t[:, 0]], 1) * (1.0 if late_left else -1.0)
    hq = sample(h, Q[:, 0], Q[:, 1]); hm = sample(h, mid[:, 0], mid[:, 1]); fm = sample(F.fill, mid[:, 0], mid[:, 1]); vis = fm <= lim['buriedFill']
    # grade-capped profile on the stage-1a surface (bench target): lowered only, never raised
    y = sample(F.new, Q[:, 0], Q[:, 1]); g = lim['grade'] * step; yb = y.copy()
    for k in range(1, len(yb)): yb[k] = min(yb[k], yb[k - 1] + g)
    for k in range(len(yb) - 2, -1, -1): yb[k] = min(yb[k], yb[k + 1] + g)
    base = (yb[:-1] + yb[1:]) / 2; grade = np.abs(np.diff(hq)) / step
    yn = hq.copy()                                   # along-line cut on NATURAL ground (plan/tools/step1.py bench): what the grade cap alone lowers
    for k in range(1, len(yn)): yn[k] = min(yn[k], yn[k - 1] + g)
    for k in range(len(yn) - 2, -1, -1): yn[k] = min(yn[k], yn[k + 1] + g)
    lowered = np.maximum(hq[:-1] - yn[:-1], hq[1:] - yn[1:])
    # per module: A1, wall-top cover, deck access, deck over the EA side
    R = int(math.ceil(lim['a1Radius'] / CELL)) + 1; mods = []
    for k in range(len(mid)):
        ci, cj = int(mid[k, 1] // CELL), int(mid[k, 0] // CELL); sl = (slice(ci - R, ci + R + 1), slice(cj - R, cj + R + 1))
        dx = F.X[sl] - mid[k, 0]; dz = F.Z[sl] - mid[k, 1]; side = dx * late[k, 0] + dz * late[k, 1]; dist = np.hypot(dx, dz); sub = h[sl]
        m150 = (dist <= lim['a1Radius']) & (side > lim['a1SideMin']); m16 = (dist <= lim['deckRadius']) & (side > lim['a1SideMin'])
        hi = float(sub[m150].max()); top = base[k] + mod['parapetTop']; ea = mid[k] - late[k] * (mod['halfDepth'] + lim['eaProbe'])
        mods.append(dict(k=k, x=r1(mid[k, 0]), z=r1(mid[k, 1]), ground=r1(hm[k]), base=r1(base[k]), fill=r1(fm[k]), visible=bool(vis[k]), grade=round(float(grade[k]), 3), lowered=r2(lowered[k]),
                         a1=r1(hi - hm[k]), a1_top=r1(hi - top), below_top=round(float((sub[m150] <= top).mean()), 3),
                         late16=r1(sub[m16].max() - base[k]) if m16.any() else None, deck_over_ea=r1(base[k] + mod['height'] - float(sample(h, ea[0], ea[1])))))
    V = [m for m in mods if m['visible']]; a1 = np.array([m['a1'] for m in V]); a1t = np.array([m['a1_top'] for m in V]); cov = np.array([m['below_top'] for m in V])
    d16 = np.array([m['late16'] for m in V if m['late16'] is not None]); dea = np.array([m['deck_over_ea'] for m in V]); gv = np.array([m['grade'] for m in V]); lo = np.array([m['lowered'] for m in V])
    five = lambda a: dict(min=r1(a.min()), p50=r1(np.percentile(a, 50)), p90=r1(np.percentile(a, 90)), max=r1(a.max())) if len(a) else None
    # bench as real cells: the 8 m bed levelled across to the grade-capped profile of the NATURAL ground (yn). Cells the cliff op
    # raised and hard-mask cells are left alone. review308 (plan/tools/step4_review.py bench_cells) took the profile from the
    # stage-1a surface instead, which ramps the bed up against the raised cliff and fills the last cells in front of the rock;
    # that figure is kept beside it so the foot rows can be checked against review308.json bench_op.
    win = F.win; D, _, S = dist_to_polyline(line, F.X[win], F.Z[win]); band = np.zeros((HH, W), bool); band[win] = D <= lim['benchHalfWidth']
    ok = band & ~F.route_hard & ~F.pm & ~(F.wet | F.water) & ~F.raised

    def cells(profile):
        tgt = np.zeros((HH, W)); tgt[win] = np.interp(S, s, profile); d = np.where(band, tgt - F.new, 0.0); ch = ok & (np.abs(d) > eps)
        return d, ch, ch & (d < 0), ch & (d > 0)
    d, ch, cut, fl = cells(yn); dp_, chp, cutp, flp = cells(yb)
    tp, ts = ms['tilePad'], ms['tileSize']
    tiles = sorted({'x%d_z%d' % (int(min(ms['tilesX'] - 1, max(0, (j * CELL + ox) // ts))), int(min(ms['tilesZ'] - 1, max(0, (i * CELL + oz) // ts)))) for i, j in np.argwhere(ch) for ox in (-tp, tp) for oz in (-tp, tp)})
    bench = dict(band_cells=int(band.sum()), changed_cells=int(ch.sum()), changed_ha=round(ch.sum() * 16 / 1e4, 3), cut_cells=int(cut.sum()), fill_cells=int(fl.sum()),
                 cut_max_m=r1(-d[cut].min()) if cut.any() else 0.0, fill_max_m=r1(d[fl].max()) if fl.any() else 0.0,
                 cut_p90_m=r1(np.percentile(-d[cut], 90)) if cut.any() else 0.0, fill_p90_m=r1(np.percentile(d[fl], 90)) if fl.any() else 0.0,
                 cut_volume_m3=int(round(float(-d[cut].sum() * 16))), fill_volume_m3=int(round(float(d[fl].sum() * 16))),
                 ea_reach_cells_changed=int((ch & F.reach).sum()),
                 masked_out=dict(route_corridor=int((band & F.route_hard).sum()), protected=int((band & F.pm).sum()), wet=int((band & (F.wet | F.water)).sum()), already_raised_by_cliff=int((band & F.raised).sum())),
                 tiles=tiles, over_cut_limit=bool(cut.any() and -d[cut].min() > lim['cutMax'] + eps), over_fill_limit=bool(fl.any() and d[fl].max() > lim['fillMax'] + eps),
                 cut_max_exact_m=round(float(-d[cut].min()), 3) if cut.any() else 0.0, fill_max_exact_m=round(float(d[fl].max()), 3) if fl.any() else 0.0,
                 review308_definition=dict(changed_cells=int(chp.sum()), cut_max_m=r1(-dp_[cutp].min()) if cutp.any() else 0.0, fill_max_m=r1(dp_[flp].max()) if flp.any() else 0.0,
                                           ea_reach_cells_changed=int((chp & F.reach).sum()), note='bed profile taken from the stage-1a surface (ramps up against the raised cliff)'))
    # end joint (stations every measure.endStation m)
    E, se = resample(line, ms['endStation']); fe = sample(F.fill, E[:, 0], E[:, 1]); inb = np.nonzero(fe > lim['buriedFill'])[0]
    rock = F.fill[win] > lim['buriedFill']; to_rock = float(np.hypot(F.X[win][rock] - line[-1, 0], F.Z[win][rock] - line[-1, 1]).min()) if rock.any() else None
    end = dict(fill_at_end_m=r1(fe[-1]), end_inside_rock=bool(fe[-1] > lim['buriedFill']), end_to_nearest_rock_m=r1(to_rock) if to_rock is not None else None, first_rock_arc_m=r1(se[inb[0]]) if len(inb) else None, embed_along_line_m=r1(se[-1] - se[inb[0]]) if len(inb) else 0.0,
               max_fill_on_line_m=r1(fe.max()), exits_rock=bool(len(inb) and fe[-1] <= lim['buriedFill']),
               fill_profile_from_end=[[r1(se[-1] - se[q]), r1(fe[q])] for q in range(len(E) - 1, max(-1, len(E) - ms['endProfileEvery'] * ms['endProfileCount']), -ms['endProfileEvery'])])
    # routes: clearance of the wall centreline and length of wall inside a corridor (= a road gap the wall cannot close)
    T, st = resample(line, ms['routeStation']); routes = []
    for r in F.routes:
        dr, _, _ = dist_to_polyline(r['pts'], T[:, 0], T[:, 1]); cor = r['width'] / 2 + lim['routeMargin']
        if dr.min() > cor + ms['routeReportBeyond']: continue
        inside = dr <= cor
        routes.append(dict(route=r['id'], min_distance_m=r1(dr.min()), corridor_half_m=cor, crosses=bool(dr.min() <= r['width'] / 2), wall_inside_corridor_m=r1(inside.sum() * (st[1] - st[0])),
                           at=[r1(T[int(dr.argmin()), 0]), r1(T[int(dr.argmin()), 1])]))
    out = dict(line=[[r1(a), r1(b)] for a, b in line], length_m=r1(plen(line)), modules=len(mods), module_step_m=round(step, 3), visible_modules=len(V), visible_length_m=r1(len(V) * step), buried_modules=len(mods) - len(V),
               turns_deg=[r1(a) for a in turns(line)] if len(line) > 2 else [], turns_gt15=int((np.abs(turns(line)) > 15).sum()) if len(line) > 2 else 0,
               ground_start_end=[r1(hq[0]), r1(hq[-1])], base_p50=r1(np.percentile([m['base'] for m in V], 50)),
               grade=dict(natural=dict(p50=round(float(np.percentile(gv, 50)), 3), p90=round(float(np.percentile(gv, 90)), 3), max=round(float(gv.max()), 3)), modules_over_cap=int((gv > lim['grade'] + 5e-4).sum()),
                          lowered_max_m=r2(lo.max()), modules_lowered_gt1m=int((lo > 1).sum()), modules_lowered_gt2m=int((lo > 2).sum())),
               A1=dict(**five(a1), modules_over_limit=int((a1 > lim['a1Limit']).sum()), of_visible=len(V), share_over_limit=round(float((a1 > lim['a1Limit']).mean()), 3), limit_m=lim['a1Limit']),
               A1_against_wall_top=dict(**five(a1t), modules_with_late_ground_above_top=int((a1t > 0).sum())),
               late_ground_below_wall_top_share=dict(min=round(float(cov.min()), 3), p50=round(float(np.percentile(cov, 50)), 3), max=round(float(cov.max()), 3),
                                                     note='share of the late-side lattice ground within a1Radius that is not higher than the wall top (bench base + parapetTop), per visible module'),
               deck_over_ea=dict(**five(dea), modules_under_min=int((dea < lim['topOverEaMin']).sum()), min_m=lim['topOverEaMin']),
               deck_access=dict(modules_late_ground_above_deck=int((d16 > mod['height']).sum()), of=len(d16), p50=r1(np.percentile(d16, 50)) if len(d16) else None, max=r1(d16.max()) if len(d16) else None),
               bench=bench, end=end, routes=routes, per_module=mods)
    if foot is not None:
        # EA ground between the foot line and this line (the strip that changes side when the wall moves uphill)
        poly = np.vstack([np.asarray(foot, float), line[::-1]]); pmk = np.zeros((HH, W), bool); pmk[win] = poly_mask(poly, F.X[win], F.Z[win])
        walk = pmk & (F.slope < lim['walkSlopeDeg']) & ~F.water & ~F.raised
        out['ea_ground'] = dict(strip_between_foot_and_line_ha=round(pmk.sum() * 16 / 1e4, 2), walkable_lt45_ha=round(walk.sum() * 16 / 1e4, 2), already_ea_reach_ha=round((pmk & F.reach).sum() * 16 / 1e4, 2),
                                note='polygon between the plan.2 foot line and this line on the 4 m lattice: with the wall on this line and the seam forest removed, the strip is on the EA side (upper bound of new EA-reachable ground; not a closure flood)')
    else:
        out['ea_ground'] = dict(strip_between_foot_and_line_ha=0.0, walkable_lt45_ha=0.0, already_ea_reach_ha=0.0, note='this is the foot line')
    return out


# ---------------------------------------------------------------------------------------------------------------- search
def moves(sp):
    """Lattice steps of the search: every offset within stepReachCells whose length is stepMinCells..stepMaxCells cells (16 headings)."""
    n = int(sp['stepReachCells'])
    return [(dj, di) for dj in range(-n, n + 1) for di in range(-n, n + 1) if sp['stepMinCells'] <= math.hypot(dj, di) <= sp['stepMaxCells']]


def search(F, start, allowed, terminal):
    """Cheapest path start -> first terminal node. State = (i, j, heading). Returns lattice points [[x, z]...] or None."""
    P = F.P; lim = P['limits']; sp = P['search']; h = F.h; bw = lim['benchHalfWidth']; MOVES = moves(sp)
    ang = [math.degrees(math.atan2(di, dj)) for dj, di in MOVES]; lens = [math.hypot(dj, di) * CELL for dj, di in MOVES]
    si, sj = int(round(start[1] / CELL)), int(round(start[0] / CELL)); pq = [(0.0, si, sj, -1)]; best = {(si, sj, -1): 0.0}; prev = {}
    while pq:
        c, i, j, d = heapq.heappop(pq)
        if best.get((i, j, d), 1e18) < c - 1e-9: continue
        if terminal[i, j]:
            path = [(i, j, d)]
            while path[-1] in prev: path.append(prev[path[-1]])
            return [[q[1] * CELL, q[0] * CELL] for q in path[::-1]], c
        for k, (dj, di) in enumerate(MOVES):
            ni, nj = i + di, j + dj
            if not (F.i0 + sp['stepReachCells'] <= ni <= F.i1 - sp['stepReachCells'] and F.j0 + sp['stepReachCells'] <= nj <= F.j1 - sp['stepReachCells']) or not allowed[ni, nj]: continue
            turn = 0.0
            if d >= 0:
                turn = abs((ang[k] - ang[d] + 180) % 360 - 180)
                if turn > sp['turnMaxDeg']: continue
            L = lens[k]; mx, mz = (j + nj) * CELL / 2, (i + ni) * CELL / 2; inside = F.fill[ni, nj] > lim['buriedFill']   # stepping into the raised cliff: the module is buried, no ground rule applies
            excess = 0.0
            if not inside:
                if abs(h[ni, nj] - h[i, j]) / L > sp['gradeSearch']: continue
                nx, nz = -di * CELL / L, dj * CELL / L; hc = float(sample(h, mx, mz)); hl = float(sample(h, mx + nx * bw, mz + nz * bw)); hr = float(sample(h, mx - nx * bw, mz - nz * bw))
                if max(hl, hr) - hc > lim['cutMax'] * sp['crossMargin'] or hc - min(hl, hr) > lim['fillMax'] * sp['crossMargin']: continue
                mi, mj = int(round(mz / CELL)), int(round(mx / CELL))
                if not allowed[mi, mj] and (i, j) != (si, sj): continue
                excess = max(0.0, float(sample(F.a1, mx, mz)) - lim['a1Limit']) / lim['a1Limit']
            nc = c + L * (1.0 + sp['a1Weight'] * excess) + sp['turnCost'] * (turn / 45.0) ** 2
            key = (ni, nj, k)
            if nc < best.get(key, 1e18) - 1e-9: best[key] = nc; prev[key] = (i, j, d); heapq.heappush(pq, (nc, ni, nj, k))
    return None, None


def box_mask(F, box):
    x0, z0, x1, z1 = box; return (F.X >= x0) & (F.X <= x1) & (F.Z >= z0) & (F.Z <= z1)


def region_between(F, a, b, dilate):
    """Cells inside the polygon a + reversed(b), dilated."""
    win = F.win; poly = np.vstack([np.asarray(a, float), np.asarray(b, float)[::-1]]); m = np.zeros((HH, W), bool); m[win] = poly_mask(poly, F.X[win], F.Z[win])
    if dilate > 0:
        da, _, _ = dist_to_polyline(a, F.X[win], F.Z[win]); db, _, _ = dist_to_polyline(b, F.X[win], F.Z[win]); m[win] |= (da <= dilate) | (db <= dilate)
    return m


def find_line(F, wing, option):
    """Searched options: 'mid' (inside the brief's band) and, east only, 'ridge' (upper bound)."""
    P = F.P; cfg = P['wings'][wing]; sp = P['search']; lim = P['limits']; start = cfg['start']; foot = F.lines[wing]['foot']
    free = ~(F.route_keep | F.pm | F.wet | F.water)
    if wing == 'east' and option == 'mid':
        region = region_between(F, foot, F.lines[wing]['crest'], sp['corridorDilate']); anchor = box_mask(F, cfg['mid']['anchorBox'])
    elif wing == 'east' and option == 'ridge':
        win = F.win; d, side, _ = dist_to_polyline(foot, F.X[win], F.Z[win]); region = np.zeros((HH, W), bool)
        region[win] = (side < 0) | (d <= sp['corridorDilate'])          # late side of the foot line = right of the walking direction
        region &= box_mask(F, cfg['ridge']['box']); anchor = box_mask(F, cfg['ridge']['anchorBox'])
    elif wing == 'west' and option == 'mid':
        m = cfg['mid']; R = np.asarray([r for r in F.routes if r['id'] == m['boundRoute']][0]['pts'], float)
        ka = int(np.hypot(*(R - np.asarray(m['boundRouteFrom'], float)).T).argmin()); kb = int(np.hypot(*(R - np.asarray(m['boundRouteTo'], float)).T).argmin())
        road = R[min(ka, kb):max(ka, kb) + 1]
        if ka > kb: road = road[::-1]
        upper = np.vstack([np.asarray(start, float)[None, :], road, np.asarray(m['closeVia'], float)])
        region = region_between(F, foot, upper, sp['corridorDilate']) | (box_mask(F, m['anchorBox']) & (F.fill > lim['buriedFill'])); anchor = box_mask(F, m['anchorBox'])
    else:
        raise SystemExit('no search for %s %s' % (wing, option))
    allowed = region & (free | (F.fill > lim['buriedFill'])); terminal = anchor & (F.fill >= sp['embedFill']) & region_dilate(region, int(sp['terminalDilateCells']))
    stand = region & free & ~F.raised   # every lattice cell a wall of this option may stand on
    band = dict(standable_ha=round(stand.sum() * CELL * CELL / 1e4, 2), slope_deg=dict(p10=r1(np.percentile(F.slope[stand], 10)), p50=r1(np.percentile(F.slope[stand], 50)), p90=r1(np.percentile(F.slope[stand], 90)), max=r1(F.slope[stand].max())),
                share_steeper_than_walk=round(float((F.slope[stand] >= lim['walkSlopeDeg']).mean()), 3))
    if wing == 'west' and option == 'mid':
        # width of the band the west wing can move in: foot line -> edge of the bounding route corridor
        T, _ = resample(np.asarray(foot, float), CELL); dr, _, _ = dist_to_polyline(R, T[:, 0], T[:, 1])
        wdt = dr - ([r for r in F.routes if r['id'] == cfg['mid']['boundRoute']][0]['width'] / 2 + lim['routeMargin'])
        band['foot_line_to_route_corridor_m'] = dict(min=r1(wdt.min()), p50=r1(np.percentile(wdt, 50)), max=r1(wdt.max()), route=cfg['mid']['boundRoute'])
    proxy = F.a1[stand]
    a1_floor = dict(min=r1(np.nanmin(proxy)), p10=r1(np.nanpercentile(proxy, 10)), p50=r1(np.nanpercentile(proxy, 50)),
                    note='A1 proxy = highest ground within a1Radius (FULL disc) minus the ground, over every standable lattice cell of this option. The module A1 uses the late-side half disc, which can only be lower or equal, so this is an indication, not a strict floor; it equals the module A1 wherever the highest ground lies on the late side (every measured module here)')
    path, cost = search(F, start, allowed, terminal)
    if path is None: return None, dict(region_ha=round(region.sum() * 16 / 1e4, 2), found=False, band=band, a1_proxy_in_region=a1_floor)
    path[0] = [float(start[0]), float(start[1])]
    info = dict(region_ha=round(region.sum() * 16 / 1e4, 2), found=True, lattice_points=len(path), cost=round(cost, 1), raw_length_m=r1(plen(path)), band=band, a1_proxy_in_region=a1_floor)
    # simplify, but never into a keep-out: shrink the tolerance until the simplified line stays legal
    for tol in [sp['simplifyTol'] * f for f in sp['simplifyShrink']] + [0.0]:
        S = dp(path, tol) if tol > 0 else np.asarray(path, float); T, _ = resample(S, sp['checkStation']); ti = np.round(T[:, 1] / CELL).astype(int); tj = np.round(T[:, 0] / CELL).astype(int)
        skip = np.hypot(T[:, 0] - start[0], T[:, 1] - start[1]) < sp['startFree']
        if (allowed[ti, tj] | skip).all(): break
    info['simplify_tol_m'] = round(tol, 2)
    # push the end further into the rock along the last leg (AC-B11b: the end lies INSIDE the cliff), as far as the fill holds
    S = np.asarray(S, float); u = S[-1] - S[-2]; u /= np.hypot(*u); ext = 0.0
    for e in np.arange(sp['embedStep'], sp['embedExtra'] + 0.01, sp['embedStep']):
        q = S[-1] + u * e
        if float(sample(F.fill, q[0], q[1])) <= lim['buriedFill']: break
        ext = float(e)
    S[-1] = S[-1] + u * ext; info['end_pushed_into_rock_m'] = ext
    return [[r1(a), r1(b)] for a, b in S], info


def region_dilate(m, cells):
    out = m.copy()
    for _ in range(cells):
        n = out.copy(); n[1:] |= out[:-1]; n[:-1] |= out[1:]; n[:, 1:] |= out[:, :-1]; n[:, :-1] |= out[:, 1:]; out = n
    return out


# ---------------------------------------------------------------------------------------------------------------- study
LABEL = dict(foot='기슭선 (plan.2)', mid='중턱 단 (이번 검토)', crest='마루 대안', ridge='능선 상한 (참고)')


def study(P):
    F = Fields(P); res = dict(id='jangseong308.options', version=P['version'], date=time.strftime('%Y-%m-%d'), status=P['status'], spec=P['spec'], decision='D308-3c / D308-9c Q3 = C',
                              inputs=[dict(path=rel(p), sha256=sha(p)) for p in (BASE, PROTO, PROTECTED, WET, PLAN, REFIT, WCREST, SNAP / 'inputs.json', SNAP / 'inputs.npz', PARAMS)],
                              stage_height=rel(PROTO), foot_key=FOOT_KEY,
                              limits=P['limits'], module=P['module'], search=P['search'], measure=P['measure'], wings={})
    for wing in ('west', 'east'):
        cfg = P['wings'][wing]; foot = F.lines[wing]['foot']; opts = {}
        opts['foot'] = dict(label=LABEL['foot'], source=cfg['foot']['source'], **measure(F, foot, cfg['lateLeft']))
        line, info = find_line(F, wing, 'mid')
        if line is None: opts['mid'] = dict(label=LABEL['mid'], found=False, search=info)
        else: opts['mid'] = dict(label=LABEL['mid'], source='jangseong308.py search (%s)' % cfg['mid']['corridor'], search=info, **measure(F, line, cfg['lateLeft'], foot))
        opts['crest'] = dict(label=LABEL['crest'], source=cfg['crest']['source'], status=cfg['crest'].get('status', ''), **measure(F, F.lines[wing]['crest'], cfg['lateLeft'], foot))
        if 'ridge' in cfg:
            line, info = find_line(F, wing, 'ridge')
            if line is not None: opts['ridge'] = dict(label=LABEL['ridge'], source='jangseong308.py search (%s)' % cfg['ridge']['corridor'], search=info, **measure(F, line, cfg['lateLeft'], foot))
        res['wings'][wing] = dict(lateLeft=cfg['lateLeft'], start=cfg['start'], options=opts)
    # the A1 floor at the gate itself (no line can change it: the wings start here)
    g = json.load(open(LAYOUT, encoding='utf-8')); gx, gz = g['gateX'], g['gateZ']
    res['gate'] = dict(centre=[gx, gz], ground=r1(sample(F.h, gx, gz)), a1_proxy_at_gate=r1(sample(F.a1, gx, gz)), a1_proxy_at_wing_starts={w: r1(sample(F.a1, *P['wings'][w]['start'])) for w in ('west', 'east')},
                       highest_ground_within_150m=r1(sample(F.a1, gx, gz) + sample(F.h, gx, gz)),
                       note='the gate stays on the Seal308 frame north of the jeokro__cheolong fork (plan.2); every wing starts at this height, so the modules next to the gate keep this A1 whatever the line')
    write_json(OUT / 'options.json', res)
    table(res); draw(F, res)
    return res


def table(res):
    print('wing option | len  vis  mod(vis) | A1 min/p50/max  over | below-top p50 | grade max  >cap  lowered | bench cut/fill max  cut m3  fill m3  cells  EA | EA strip ha | end fill  inside  exits | road gap')
    for w, wd in res['wings'].items():
        for o, d in wd['options'].items():
            if 'A1' not in d: print('%-4s %-5s | no line found' % (w, o)); continue
            gap = sum(r['wall_inside_corridor_m'] for r in d['routes']); a = d['A1']; b = d['bench']; e = d['end']
            print('%-4s %-5s | %5.1f %5.1f %2d(%2d) | %4.1f/%4.1f/%4.1f  %2d/%2d | %4.0f%% | %.3f  %d  %.2f | %.1f/%.1f  %5d  %5d  %3d  %3d | %.2f (%.2f) | %4.1f  %s  %s | %.0f m' % (
                w, o, d['length_m'], d['visible_length_m'], d['modules'], d['visible_modules'], a['min'], a['p50'], a['max'], a['modules_over_limit'], a['of_visible'], d['late_ground_below_wall_top_share']['p50'] * 100,
                d['grade']['natural']['max'], d['grade']['modules_over_cap'], d['grade']['lowered_max_m'], b['cut_max_m'], b['fill_max_m'], b['cut_volume_m3'], b['fill_volume_m3'], b['changed_cells'], b['ea_reach_cells_changed'],
                d['ea_ground']['strip_between_foot_and_line_ha'], d['ea_ground']['walkable_lt45_ha'], e['fill_at_end_m'], e['end_inside_rock'], e['exits_rock'], gap))


# ---------------------------------------------------------------------------------------------------------------- pictures
COL = dict(foot=(30, 60, 200), mid=(0, 140, 60), crest=(190, 0, 150), ridge=(220, 120, 0))


def a1_colour(v, limit, bins):
    return (40, 150, 60) if v <= limit else (225, 200, 40) if v <= bins[0] else (235, 130, 30) if v <= bins[1] else (200, 40, 40)


def draw(F, res):
    from PIL import Image, ImageDraw, ImageFont
    dr_ = F.P['draw']; a1l = F.P['limits']['a1Limit']; bins = dr_['a1Bins']
    x0, z0, x1, z1 = dr_['mapWindow']; S = dr_['mapScale']
    px = lambda x, z: ((x - x0) * S, (z1 - z) * S)
    xs = np.arange(x0, x1, 1 / S); zs = np.arange(z1, z0, -1 / S); XX, ZZ = np.meshgrid(xs, zs); hh = sample(F.h, XX, ZZ)
    gz, gx = np.gradient(hh, 1 / S); shade = np.clip(0.8 + 0.55 * (-gx * 0.6 + gz * 0.6), 0.35, 1.15)
    t = np.clip((hh - 130) / 170, 0, 1); rgb = np.stack([226 + 14 * t, 222 - 10 * t, 205 - 40 * t], -1) * shade[..., None]
    ea = sample(F.reach.astype(float), XX, ZZ) > 0.5; rgb[ea] = rgb[ea] * 0.82 + np.array([120, 170, 225]) * 0.18
    fl = sample(F.fill, XX, ZZ) > F.P['limits']['buriedFill']; rgb[fl] = rgb[fl] * 0.55 + np.array([95, 75, 65]) * 0.45
    rk = sample(F.route_hard.astype(float), XX, ZZ) > 0.5; rgb[rk] = rgb[rk] * 0.8 + np.array([180, 110, 60]) * 0.2
    for stp, k in ((10, 0.8), (50, 0.55)):
        ct = (np.floor(hh / stp) != np.floor(np.roll(hh, 1, 0) / stp)) | (np.floor(hh / stp) != np.floor(np.roll(hh, 1, 1) / stp)); rgb[ct] *= k
    im = Image.fromarray(np.clip(rgb, 0, 255).astype(np.uint8)); d = ImageDraw.Draw(im)
    f = ImageFont.truetype('C:/Windows/Fonts/malgun.ttf', 17); fb = ImageFont.truetype('C:/Windows/Fonts/malgunbd.ttf', 19); fs = ImageFont.truetype('C:/Windows/Fonts/malgun.ttf', 13)
    for r in F.routes: d.line([px(*p) for p in r['pts']], fill=(140, 70, 30), width=3)
    g = res['gate']['centre']; gp = px(*g); d.rectangle([gp[0] - 9, gp[1] - 6, gp[0] + 9, gp[1] + 6], fill=(20, 20, 20)); d.text((gp[0] + 12, gp[1] - 30), '관문', font=fb, fill=(0, 0, 0))
    for w, wd in res['wings'].items():
        for o in ('ridge', 'crest', 'foot', 'mid'):
            od = wd['options'].get(o)
            if not od or 'line' not in od: continue
            d.line([px(*p) for p in od['line']], fill=COL[o], width=5 if o == 'mid' else 3)
            for m in od['per_module']:
                if not m['visible']: continue
                p = px(m['x'], m['z']); rr = 5 if o == 'mid' else 4; d.ellipse([p[0] - rr, p[1] - rr, p[0] + rr, p[1] + rr], fill=a1_colour(m['a1'], a1l, bins), outline=COL[o])
    for x in range(x0 + 20, x1, 40):
        for z in range(z0 + 20, z1, 40): d.text(px(x - 5, z + 3), '%d' % sample(F.h, x, z), font=fs, fill=(70, 70, 70))
    for x in range(x0, x1, 100): d.text(px(x + 2, z0 + 9), 'x %d' % x, font=fs, fill=(0, 0, 0))
    for z in range(z0 + 100, z1, 100): d.text(px(x0 + 2, z + 4), 'z %d' % z, font=fs, fill=(0, 0, 0))
    y = 8
    d.rectangle([8, 4, 640, 4 + 26 * 8], fill=(246, 243, 235), outline=(60, 60, 60))
    d.text((14, y), '장성 선형 검토 (Q3 = C) — 오프라인 4 m 격자, 북쪽이 위', font=fb, fill=(0, 0, 0)); y += 28
    for o in ('foot', 'mid', 'crest', 'ridge'): d.line([(16, y + 11), (60, y + 11)], fill=COL[o], width=5); d.text((68, y), LABEL[o], font=f, fill=(0, 0, 0)); y += 24
    d.text((14, y), '모듈 점 = A1(적로 쪽 150 m 안 최고 지면 - 모듈 밑):', font=f, fill=(0, 0, 0)); y += 24
    xx = 16
    for lab, v in (('≤%g m' % a1l, a1l), ('%g–%g' % (a1l, bins[0]), bins[0]), ('%g–%g' % (bins[0], bins[1]), bins[1]), ('>%g' % bins[1], bins[1] + 1)):
        d.ellipse([xx, y + 4, xx + 12, y + 16], fill=a1_colour(v, a1l, bins)); d.text((xx + 16, y), lab, font=f, fill=(0, 0, 0)); xx += 105
    y += 24; d.text((14, y), '파란 기운 = EA 도달(닫힘) · 갈색 = 절벽 메움(%s) · 붉은 띠 = 길 회랑' % PROTO.name, font=fs, fill=(0, 0, 0))
    im.save(OUT / 'options_map.png'); print('map', im.size, rel(OUT / 'options_map.png'))
    # sections: wall base and the highest late-side ground within the radius, along each option
    PW, PH, MG = 1180, 300, 56; rows = [(w, o) for w in ('west', 'east') for o in ('foot', 'mid', 'crest', 'ridge') if 'per_module' in res['wings'][w]['options'].get(o, {})]
    sh = Image.new('RGB', (PW, 46 + len(rows) * PH), (246, 243, 235)); d = ImageDraw.Draw(sh)
    d.text((10, 8), '선 따라 단면: 굵은 선 = 성벽 밑(자연 지면), 가는 붉은 선 = 적로 쪽 %g m 안 최고 지면, 회색 띠 = 한도 %g m, 세로 선 = 절벽 안으로 들어가는 곳' % (F.P['limits']['a1Radius'], a1l), font=f, fill=(0, 0, 0))
    lo, hi = dr_['sectionY']; span = float(dr_['sectionLength'])
    for r, (w, o) in enumerate(rows):
        od = res['wings'][w]['options'][o]; M_ = od['per_module']; y0 = 46 + r * PH; n = len(M_); step = od['module_step_m']
        X_ = lambda k: MG + (k + 0.5) * step * (PW - MG - 20) / span; Y_ = lambda v: y0 + PH - 30 - (v - lo) * (PH - 60) / (hi - lo)
        d.rectangle([MG, y0 + 26, PW - 20, y0 + PH - 30], outline=(120, 120, 120))
        for v in range(int(lo) + 10, int(hi), 20): d.line([(MG, Y_(v)), (PW - 20, Y_(v))], fill=(215, 212, 204)); d.text((8, Y_(v) - 8), '%d m' % v, font=fs, fill=(60, 60, 60))
        a = od['A1']; d.text((MG + 6, y0 + 4), '%s %s — 길이 %.0f m · 모듈 %d(보이는 것 %d) · A1 %.0f / %.0f / %.0f m (최소/중앙/최대) · 한도 초과 %d/%d' % ('서익' if w == 'west' else '동익', LABEL[o], od['length_m'], od['modules'], od['visible_modules'], a['min'], a['p50'], a['max'], a['modules_over_limit'], a['of_visible']), font=f, fill=COL[o])
        g_ = [(X_(k), Y_(m['ground'])) for k, m in enumerate(M_)]; hi_ = [(X_(k), Y_(m['ground'] + m['a1'])) for k, m in enumerate(M_)]
        band = [(X_(k), Y_(m['ground'] + a1l)) for k, m in enumerate(M_)]
        d.polygon(g_ + band[::-1], fill=(222, 222, 216)); d.line(hi_, fill=(200, 50, 40), width=2); d.line(g_, fill=COL[o], width=4)
        for k, m in enumerate(M_):
            if not m['visible']: d.line([(X_(k), y0 + 26), (X_(k), y0 + PH - 30)], fill=(110, 90, 80), width=1); break
        for s_ in range(0, int(span) + 1, 50): d.text((MG + s_ * (PW - MG - 20) / span - 8, y0 + PH - 28), '%d m' % s_, font=fs, fill=(60, 60, 60))
    sh.save(OUT / 'sections.png'); print('sections', sh.size, rel(OUT / 'sections.png'))


# ---------------------------------------------------------------------------------------------------------------- layout
def layout(P):
    """Preview lines (variant 'mid') in the WallLook308 Line shape, appended at load time to look/wall/wall_layout.json."""
    F = Fields(P); res = json.load(open(OUT / 'options.json', encoding='utf-8')); base = json.load(open(LAYOUT, encoding='utf-8')); lines = []
    head = {l['id']: l['pts'][:4] for l in base['lines']}   # gate block side + Seal308 frame end on the wall centreline (wall1_layout.py)
    for wing, lab in (('west', '서익 중턱 단 (jangseong308 mid)'), ('east', '동익 중턱 단 (jangseong308 mid)')):
        od = res['wings'][wing]['options']['mid']
        if 'line' not in od: continue
        pts = [head[wing][0:2], head[wing][2:4]] + od['line'][1:]; Pn = np.asarray(pts, float)
        T, s = resample(Pn, P['search']['checkStation']); f = sample(F.fill, T[:, 0], T[:, 1]); ins = np.nonzero(f > P['limits']['buriedFill'])[0]
        lines.append(dict(id=wing + '_mid', variant='mid', prefix='W' if wing == 'west' else 'E', label=lab, lateLeft=res['wings'][wing]['lateLeft'], pts=[round(float(v), 3) for p in pts for v in p],
                          length=round(plen(Pn), 2), planFrom=1, planLength=od['length_m'], planModules=od['modules'], bendsDeg=[r1(a) for a in turns(Pn)],
                          cliffEntry=r1(s[ins[0]]) if len(ins) else -1.0, cliffFillAtEnd=r1(f[-1]),
                          note='pts[0..1] as in wall_layout.json (gate block side, Seal308 frame end on the wall centreline), then the study line; cliffEntry = arc length where the stage height fill (%s) first exceeds buriedFill' % PROTO.name))
    g = P['gate']
    out = dict(version='308.look.wall.mid.1', status='TEST look data (Q3 = C study): in-memory preview only, nothing is placed', study=dict(path=rel(OUT / 'options.json'), sha256=sha(OUT / 'options.json')),
               stageHeight=rel(PROTO), base=dict(path=rel(LAYOUT), sha256=sha(LAYOUT)), lines=lines,
               doorPlaneZ=g['doorPlaneZ'], doorHalfWidth=g['doorHalfWidth'], doorBottom=g['doorBottom'], doorBury=g['doorBury'], doorNote=g['gateNote'])
    write_json(LAYOUT_OUT, out)
    for l in lines: print(l['id'], 'length %.1f m, study %d modules, bends %s, cliff entry %.1f, fill at end %.1f' % (l['length'], l['planModules'], l['bendsDeg'], l['cliffEntry'], l['cliffFillAtEnd']))
    print('->', rel(LAYOUT_OUT))


# ---------------------------------------------------------------------------------------------------------------- build (phase 1b)
# The wall + gate layout the editor ledger (Jangseong308) builds: user-fixed FOOT lines (D308-9d), kit limits (D308-9e).
# Everything is measured on the 4 m lattice [O]; the editor seats every module on the physics ground at apply time with the
# same rule and re-measures in check. The output holds no time stamp: a second run writes the same bytes.
BUILD_PARAMS = CB / 'wall_1b/params.json'
r3 = lambda v: round(float(v), 3)


def tri(f, x, z):
    """Height on the tile-mesh triangles (plan/Stage/FORMAT.md: (00, 10, 01) and (10, 01, 11) per 4 m cell) = the terrain collider."""
    x = np.clip(np.asarray(x, float), 0, 3999.99); z = np.clip(np.asarray(z, float), 0, 5999.99)
    j = x / CELL; i = z / CELL; j0 = np.floor(j).astype(int); i0 = np.floor(i).astype(int); u = j - j0; v = i - i0
    j1 = np.minimum(j0 + 1, W - 1); i1 = np.minimum(i0 + 1, HH - 1)
    h00 = f[i0, j0]; h10 = f[i0, j1]; h01 = f[i1, j0]; h11 = f[i1, j1]
    return np.where(u + v <= 1, h00 + (h10 - h00) * u + (h01 - h00) * v, h11 + (h01 - h11) * (1 - u) + (h10 - h11) * (1 - v))


def first_existing(paths):
    for k, q in enumerate(paths):
        if Path(q).exists(): return k, Path(q)
    return -1, None


def seg_dist(P, x, z):
    """Distance from points to a polyline (scalar or arrays)."""
    d, _, _ = dist_to_polyline(P, np.atleast_1d(np.asarray(x, float)), np.atleast_1d(np.asarray(z, float))); return d


class BuildCtx:
    def __init__(self, params_path):
        self.params_path = Path(params_path); self.P = Pb = json.load(open(params_path, encoding='utf-8')); inp = Pb['inputs']
        self.study = configure(CB / inp['study'])                      # module size, limits, the route / mask snapshot (plan.3 basis)
        k, stage = first_existing([CB / q for q in inp['stageHeight']])
        if stage is None: raise SystemExit('REFUSED: no stage height (%s)' % ', '.join(inp['stageHeight']))
        global PROTO; PROTO = stage
        self.stage_path = stage; self.F = F = Fields(self.study)
        eps = self.study['limits']['changeEps']; owner = CB / inp['stageOwner'][k] if k < len(inp['stageOwner']) else None
        if owner is not None and owner.exists():
            self.cliff = (np.load(owner) >= 0) & (F.fill > eps); self.cliff_src = rel(owner)
        else:
            fb = CB / inp['cliffHeightFallback']; self.cliff = (np.fromfile(fb, '<f4').reshape(HH, W).astype(np.float64) - F.h) > eps; self.cliff_src = rel(fb) + ' - base'
            owner = fb
        self.owner_path = owner
        self.cliff_fill = np.where(self.cliff, F.fill, 0.0)
        self.seat = np.where(self.cliff, F.h, F.new)                    # the ground the wall is seated on: under cliff rock = the ground below the rock
        gz, gx = np.gradient(F.new, CELL); self.slope_stage = np.degrees(np.arctan(np.hypot(gx, gz)))
        _, ops_path = first_existing([CB / q for q in inp['ops']])
        if ops_path is None: raise SystemExit('REFUSED: no ops file')
        self.ops_path = ops_path; self.ops = json.load(open(ops_path, encoding='utf-8'))
        self.lay_path = CB / inp['lookLayout']; self.lay = json.load(open(self.lay_path, encoding='utf-8'))
        self.pack_path = CB / inp['pack']; self.pack = json.load(open(self.pack_path, encoding='utf-8'))
        self.probe_path = CB / inp['probe']; self.probe = json.load(open(self.probe_path, encoding='utf-8'))['probe']
        self.frame_path = (CB / inp['sealFrame']).resolve(); self.frame = json.load(open(self.frame_path, encoding='utf-8'))['frame']
        self.out = CB / inp['out']
        lay = self.lay; self.L = lay['moduleLength']; self.hd = lay['moduleHalfDepth']; self.mh = lay['moduleHeight']; self.ptop = lay['parapetTop']
        # the user-fixed lines: the ops file must name exactly these
        fx = Pb['fixed']; tol = fx['lineTolerance']; wl = {l['id']: l['line'] for l in self.ops['wall']['lines']}
        for wing in ('west', 'east'):
            a = np.asarray(fx[wing], float); b = np.asarray(wl[wing], float)
            if a.shape != b.shape or np.abs(a - b).max() > tol:
                raise SystemExit('REFUSED: %s names another %s line than the user-fixed one (D308-9d): %s' % (rel(ops_path), wing, wl[wing]))
        g = np.asarray(self.ops['wall']['gate'], float)
        if np.abs(g - np.asarray([fx['gateA'], fx['gateB']], float)).max() > tol: raise SystemExit('REFUSED: the ops gate points differ from the user-fixed A / B')
        self.fixed = {w: np.asarray(fx[w], float) for w in ('west', 'east')}

    def plan_fields(self):
        """The study's field view for measure(): 'fill' = what a cliff op raised (a bench fill never buries a module)."""
        import copy
        F2 = copy.copy(self.F); F2.fill = self.cliff_fill; F2.raised = self.cliff
        return F2

    def inputs(self):
        ps = [self.params_path, CB / self.P['inputs']['study'], self.ops_path, self.stage_path, self.owner_path, BASE, PROTECTED, WET, SNAP / 'inputs.json', SNAP / 'inputs.npz',
              self.lay_path, self.pack_path, self.probe_path, self.frame_path]
        return [dict(path=rel(q), sha256=sha(q)) for q in ps]


def foot_min(B, q, late):
    """Lowest seat ground under the wall thickness at one station + whether the station stands in cliff rock."""
    n = B.P['seat']['footSamplesAcross']; c = (np.arange(n) - (n - 1) / 2) * (2 * B.hd / (n - 1))
    x = q[0] + late[0] * c; z = q[1] + late[1] * c
    return float(tri(B.seat, x, z).min()), bool((tri(B.cliff_fill, x, z) > B.study['limits']['changeEps']).any())


def plan_line(B, lid, pts, late_left, prefix, plan_from):
    """WallLook308.PlanLine offline: modules per leg, joint heights (grade cap by lowering, then no float), bastions."""
    st = B.P['seat']; bs = B.P['bastion']; L = B.L; hd = B.hd; cap = st['gradeCap']
    p = np.asarray(pts, float); legs = len(p) - 1; d = np.diff(p, axis=0); ll = np.hypot(d[:, 0], d[:, 1]); dirv = d / ll[:, None]
    late = np.stack([-dirv[:, 1], dirv[:, 0]], 1) * (1.0 if late_left else -1.0)
    bend = [0.0] * (legs + 1)
    for k in range(1, legs): bend[k] = math.degrees(math.atan2(dirv[k - 1, 0] * dirv[k, 1] - dirv[k - 1, 1] * dirv[k, 0], float(dirv[k - 1] @ dirv[k])))
    ext = lambda deg: min(st['bendOverlapMax'], hd * math.tan(math.radians(abs(deg)) / 2))
    mods = []; nat = []; rock = []; jxz = []; vertex_joint = [0] * (legs + 1); arc0 = 0.0
    for k in range(legs):
        e0 = ext(bend[k]) if k > 0 else 0.0; e1 = ext(bend[k + 1]) if k < legs - 1 else 0.0
        s = p[k] - dirv[k] * e0; e = p[k + 1] + dirv[k] * e1; ln = float(np.hypot(*(e - s)))
        n = max(1, int(round(ln / L)))
        if ln / (n * L) > st['stretchMax']: n += 1
        for j in range(n + 1):
            q = s + (e - s) * (j / n); hq, rq = foot_min(B, q, late[k])
            if j == 0 and k > 0: nat[-1] = min(nat[-1], hq); rock[-1] = rock[-1] or rq
            else: nat.append(hq); rock.append(rq); jxz.append(q.copy())
            if j == n: break
            mods.append(dict(leg=k, gateSpan=k < plan_from, a=q.copy(), b=s + (e - s) * ((j + 1) / n), late=late[k].copy(), length=ln / n, scale=ln / (n * L), arc=arc0 - e0 + ln * (j + .5) / n))
        vertex_joint[k + 1] = len(nat) - 1; arc0 += float(ll[k])
    for i, m in enumerate(mods): m['id'] = '%s%02d' % (prefix, i + 1); m['line'] = lid
    # footprint samples on the REAL stage ground (rock included): float / bury / hidden
    na, nc = st['samplesAlong'], st['samplesAcross']
    for m in mods:
        u = np.repeat((np.arange(na) + .5) / na, nc); c = np.tile((np.arange(nc) - (nc - 1) / 2) * (2 * hd / (nc - 1)), na)
        x = m['a'][0] + (m['b'][0] - m['a'][0]) * u + m['late'][0] * c; z = m['a'][1] + (m['b'][1] - m['a'][1]) * u + m['late'][1] * c
        m['su'] = u; m['sg'] = tri(B.F.new, x, z); m['sr'] = tri(B.cliff_fill, x, z) > B.study['limits']['changeEps']
    built = np.asarray(nat, float).copy(); lens = [m['length'] for m in mods]

    def capit():
        for i in range(1, len(built)): built[i] = min(built[i], built[i - 1] + cap * lens[i - 1])
        for i in range(len(built) - 2, -1, -1): built[i] = min(built[i], built[i + 1] + cap * lens[i])

    def flt(i):
        m = mods[i]; return float(np.max(built[i] + (built[i + 1] - built[i]) * m['su'] - m['sg'], initial=0.0))
    capit()
    for _ in range(st['sinkPasses']):
        moved = False
        for i in range(len(mods)):
            f = flt(i)
            if f > st['floatTolerance']: built[i] -= f; built[i + 1] -= f; moved = True
        capit()
        if not moved: break
    for i, m in enumerate(mods):
        m['naturalA'], m['naturalB'], m['baseA'], m['baseB'] = nat[i], nat[i + 1], float(built[i]), float(built[i + 1]); m['rockA'], m['rockB'] = rock[i], rock[i + 1]
        gap = built[i] + (built[i + 1] - built[i]) * m['su'] - m['sg']; open_ = ~m['sr']
        m['floatMax'] = float(np.max(gap, initial=0.0)); m['sunkMax'] = float(np.max(-gap[open_], initial=0.0)) if open_.any() else None
        m['hidden'] = bool((m['sg'] - (built[i] + (built[i + 1] - built[i]) * m['su'] + B.ptop) >= st['hiddenMargin']).all())
        c = (m['a'] + m['b']) / 2; m['buried'] = bool(float(tri(B.cliff_fill, c[0], c[1])) > B.study['limits']['buriedFill'])
    # bastions: gate joint / bends, cliff joint, grade breaks - never closer than minSpacingModules modules to another one
    chis = []; notes = []
    free = lambda at: all(np.hypot(*(c['at'] - at)) > L * bs['minSpacingModules'] for c in chis)
    for k in range(1, legs):
        gate_joint = bs['gateJoints'] and k == plan_from
        if abs(bend[k]) > bs['bendDeg'] or gate_joint:
            lt = late[k - 1] + late[k]; lt /= np.hypot(*lt)
            why = ('gate joint, bend %+.1f deg' % bend[k]) if gate_joint else 'bend %+.1f deg' % bend[k]
            chis.append(dict(at=p[k].copy(), late=lt, baseY=float(built[vertex_joint[k]]), reason=why, kind='gate' if gate_joint else 'bend', bendDeg=r1(bend[k]), anchor='joint', index=vertex_joint[k]))
    first_buried = next((m for m in mods if m['buried'] and not m['gateSpan']), None)
    if bs['cliffJoints'] and first_buried is not None:
        m = first_buried; c = (m['a'] + m['b']) / 2
        if free(c): chis.append(dict(at=c, late=m['late'].copy(), baseY=(m['baseA'] + m['baseB']) / 2, reason='cliff joint (the first module inside the rock, %s)' % m['id'], kind='cliff', bendDeg=0.0, anchor='centre', index=int(m['id'][len(prefix):]) - 1))
        else: notes.append('%s: the cliff joint (%s) falls within %.1f modules of another bastion - one bastion serves both' % (lid, m['id'], bs['minSpacingModules']))
    for i in range(1, len(mods)):
        g0 = (mods[i - 1]['baseB'] - mods[i - 1]['baseA']) / mods[i - 1]['length']; g1 = (mods[i]['baseB'] - mods[i]['baseA']) / mods[i]['length']; jump = abs(g1 - g0)
        if jump <= bs['gradeBreak'] or mods[i]['leg'] != mods[i - 1]['leg'] or mods[i]['buried'] or mods[i]['rockA']: continue
        if free(mods[i]['a']): chis.append(dict(at=mods[i]['a'].copy(), late=mods[i]['late'].copy(), baseY=mods[i]['baseA'], reason='grade break %.2f at %s|%s' % (jump, mods[i - 1]['id'], mods[i]['id']), kind='grade', bendDeg=0.0, anchor='joint', index=i))
        else: notes.append('%s: grade break %.2f at %s|%s is within %.1f modules of another bastion - none added' % (lid, jump, mods[i - 1]['id'], mods[i]['id'], bs['minSpacingModules']))
    kb = B.P['kit']['bastion']['colliderParts'][0]['b']       # the kit footprint (bastion frame: x across, z toward the hill)
    for i, c in enumerate(chis):
        c['id'] = '%schi%d' % (prefix, i + 1); c['line'] = lid; fr = c['at'] + c['late'] * (hd + L)
        c['groundFront'] = float(tri(B.F.new, fr[0], fr[1])) - c['baseY']
        rt = np.array([c['late'][1], -c['late'][0]]); gx_, gz_ = np.meshgrid(np.linspace(kb[0], kb[2], 7), np.linspace(kb[1], kb[3], 7))
        wx = c['at'][0] + rt[0] * gx_ + c['late'][0] * gz_; wz = c['at'][1] + rt[1] * gx_ + c['late'][1] * gz_
        c['hidden'] = bool((tri(B.F.new, wx, wz) - (c['baseY'] + B.ptop) >= st['hiddenMargin']).all())
    return dict(id=lid, pts=p, legs=legs, bend=bend, mods=mods, joints=dict(xz=jxz, natural=nat, rock=rock, built=built.tolist()), chis=chis, notes=notes, length=float(ll.sum()), planFrom=plan_from)


def module_metrics(B, m, fixed_line):
    """A1 (recorded exception), wall top over the EA ground, deck access from the hill, masks - per module, on the lattice [O]."""
    F = B.F; lim = B.study['limits']; lm = B.P['limits']; dk = B.P['deck']; hd = B.hd; pr = B.probe
    c = (m['a'] + m['b']) / 2; late = m['late']; base_c = (m['baseA'] + m['baseB']) / 2; t = (m['b'] - m['a']) / m['length']
    R = int(math.ceil(lim['a1Radius'] / CELL)) + 1; ci, cj = int(c[1] // CELL), int(c[0] // CELL); sl = (slice(ci - R, ci + R + 1), slice(cj - R, cj + R + 1))
    dx = F.X[sl] - c[0]; dz = F.Z[sl] - c[1]; side = dx * late[0] + dz * late[1]; dist = np.hypot(dx, dz)
    m150 = (dist <= lim['a1Radius']) & (side > lim['a1SideMin']); m16 = (dist <= dk['radius']) & (side > dk['sideMin'])
    hi = float(F.h[sl][m150].max()); gn = float(sample(F.h, c[0], c[1]))
    m['a1'] = hi - gn; m['a1Top'] = hi - (base_c + B.ptop); m['groundNatural'] = gn
    m['late16'] = float(F.new[sl][m16].max()) - base_c if m16.any() else None
    m['aboveDeck'] = bool(m['late16'] is not None and m['late16'] > B.mh); m['aboveTop'] = bool(m['late16'] is not None and m['late16'] > B.ptop)
    # wall top / deck over the EA-side ground: the P0 figure (deck, one point eaProbe m in front) and the strict one (lowest top - highest strip ground)
    ea = c - late * (hd + lim['eaProbe']); m['deckOverEa'] = base_c + B.mh - float(tri(F.new, ea[0], ea[1]))
    us = (np.arange(5) + .5) / 5; offs = np.arange(lm['eaStripFrom'], lm['eaStripTo'] + 1e-6, lm['eaStripStep']); worst = None; ea_hi = None; rock_n = 0
    for u in us:
        q = m['a'] + (m['b'] - m['a']) * u; sx = q[0] - late[0] * (hd + offs); sz = q[1] - late[1] * (hd + offs)
        open_ = tri(B.cliff_fill, sx, sz) <= lim['changeEps']; rock_n += int((~open_).sum())     # cliff rock in front of the wall is the barrier itself, not ground to stand on
        if not open_.any(): continue
        g = float(tri(F.new, sx, sz)[open_].max()); t_ = m['baseA'] + (m['baseB'] - m['baseA']) * u + B.ptop - g
        worst = t_ if worst is None else min(worst, t_); ea_hi = g if ea_hi is None else max(ea_hi, g)
    m['topOverEa'] = worst; m['eaGround'] = ea_hi; m['eaRockShare'] = rock_n / (len(us) * len(offs))
    # footprint against the hard masks (0.5 m samples)
    na = max(2, int(math.ceil(m['length'] / 0.5)) + 1); u = np.repeat(np.linspace(0, 1, na), 5); cc = np.tile(np.linspace(-hd, hd, 5), na)
    x = m['a'][0] + (m['b'][0] - m['a'][0]) * u + late[0] * cc; z = m['a'][1] + (m['b'][1] - m['a'][1]) * u + late[1] * cc
    ii = np.clip(np.round(z / CELL).astype(int), 0, HH - 1); jj = np.clip(np.round(x / CELL).astype(int), 0, W - 1)
    road = 0.0; corridor = 0.0; near = None
    for r in F.routes:
        dr = seg_dist(r['pts'], x, z); road = max(road, float((dr <= r['width'] / 2).mean())); corridor = max(corridor, float((dr <= r['width'] / 2 + lim['routeMargin']).mean()))
        if near is None or dr.min() < near[0]: near = (float(dr.min()), r['id'])
    m['onRoad'] = road; m['inCorridor'] = corridor; m['routeNear'] = near
    m['protected'] = int(F.pm[ii, jj].sum()); m['wet'] = int((F.wet | F.water)[ii, jj].sum())
    m['lineOffset'] = float(seg_dist(fixed_line, c[0], c[1])[0])
    # deck access estimate: ballistic run / jump / guk+jump from walkable hill ground onto the wall top and over it
    capz = B.P['kit']['wall']['capZ']; g = abs(pr['gravity']); v = pr['speed']; vj = math.sqrt(2 * g * pr['jumpHeight'])
    su = np.arange(-2.0, m['length'] + 2.0 + 1e-6, dk['gridAlong']); ss = np.arange(capz[1] + dk['gridAcross'], dk['radius'] + 1e-6, dk['gridAcross'])
    UU, SS = np.meshgrid(su, ss); x = m['a'][0] + t[0] * UU + late[0] * SS; z = m['a'][1] + t[1] * UU + late[1] * SS
    yg = tri(F.new, x, z); walk = sample(B.slope_stage, x, z) <= dk['walkSlopeDeg']
    top = m['baseA'] + (m['baseB'] - m['baseA']) * np.clip(UU / m['length'], 0, 1) + B.ptop
    res = {}
    for name, y0, vy in (('walk', 0.0, 0.0), ('jump', 0.0, vj), ('guk+jump', dk['gukLift'], vj)):
        for tgt, dist_ in (('top', SS - capz[1]), ('over', SS - capz[0])):
            tt = dist_ / v; ok = walk & (yg + y0 + vy * tt - .5 * g * tt * tt >= top)
            res[(name, tgt)] = ok
    order = ('walk', 'jump', 'guk+jump')
    m['reachTop'] = next((n for n in order if res[(n, 'top')].any()), 'none'); m['reachOver'] = next((n for n in order if res[(n, 'over')].any()), 'none')
    lj = res[('jump', 'top')]; m['launchCells'] = int(lj.sum())
    m['launchAt'] = [r1(x[lj].mean()), r1(z[lj].mean())] if lj.any() else None
    m['launchSpan'] = r1(UU[lj].max() - UU[lj].min() + dk['gridAlong']) if lj.any() else 0.0
    m['launchNear'] = r1(SS[lj].min()) if lj.any() else None
    return m


def build_layout(B):
    Pb = B.P; lay = B.lay; fx = Pb['fixed']; st = Pb['seat']; kit = Pb['kit']; lim = B.study['limits']; lm = Pb['limits']; F = B.F
    head = {l['id']: l['pts'][:4] for l in lay['lines']}
    lines = {}
    for wing, late_left, prefix in (('west', True, 'W'), ('east', False, 'E')):
        pts = [head[wing][0:2], head[wing][2:4]] + [list(q) for q in B.fixed[wing][1:]]
        ln = plan_line(B, wing, pts, late_left, prefix, 1)
        gate_seg = np.asarray([fx['gateA'], fx['gateB']], float)
        for m in ln['mods']: module_metrics(B, m, gate_seg if m['gateSpan'] else B.fixed[wing])
        lines[wing] = ln
    # ---- gate (Seal308 frame as the look gate showed it; floor = the editor-measured door-plane rule)
    sg = B.study['gate']; G = np.array([lay['gateX'], lay['gateZ']]); late = np.array([lay['lateX'], lay['lateZ']]); late /= np.hypot(*late); right = np.array([late[1], -late[0]])
    xs = np.arange(-sg['doorHalfWidth'], sg['doorHalfWidth'] + 1e-6, 0.5); dp_ = G[None, :] + right[None, :] * xs[:, None] + late[None, :] * sg['doorPlaneZ']
    dg = tri(F.new, dp_[:, 0], dp_[:, 1]); floor_off = float(dg.min()) - sg['doorBury'] - sg['doorBottom']; floor = fx['gateFloorY']
    A = np.asarray(fx['gateA'], float); Bp = np.asarray(fx['gateB'], float); tAB = (Bp - A) / np.hypot(*(Bp - A)); gate_s = float((G - A) @ tAB); hw = lay['gateHalfWidth']
    pieces = [dict(q) for q in lay['gatePieces'] if q['tag'] not in kit['gate']['skipTags']]
    gk = kit['gate']; dhw = gk['doorHalfWidth']; zh = gk['hingeZ']; leaf_bottom = floor + sg['doorBottom']

    def swing(direction):
        """Leaf bottom against the offline ground through the swing (0..openDegrees): deepest plough and the open-pose range, per leaf."""
        out = {}
        for name, sx in (('left', -1.0), ('right', 1.0)):
            deep = 0.0; rng = None
            for ang in np.arange(0.0, gk['openDegrees'] + 1e-6, 10.0):
                a = math.radians(ang); r = np.arange(0.0, dhw + 1e-6, 0.25)
                lx = sx * dhw - sx * r * math.cos(a); lz = sg['doorPlaneZ'] + (1.0 if direction == 'late' else -1.0) * r * math.sin(a)
                w = G[None, :] + right[None, :] * lx[:, None] + late[None, :] * lz[:, None]; d = tri(F.new, w[:, 0], w[:, 1]) - leaf_bottom
                deep = max(deep, float(d.max()))
                if abs(ang - gk['openDegrees']) < 1e-6: rng = [r2(d.min()), r2(d.max())]
            out[name] = dict(ploughMax=r2(deep), openGroundOverBottom=rng)
        return out
    gate = dict(a=[r3(A[0]), r3(A[1])], b=[r3(Bp[0]), r3(Bp[1])], length=r3(np.hypot(*(Bp - A))), centre=[r3(G[0]), r3(G[1])], centreS=r3(gate_s),
                blockS0=r3(gate_s - hw), blockS1=r3(gate_s + hw), passageS0=r3(gate_s - sg['doorHalfWidth']), passageS1=r3(gate_s + sg['doorHalfWidth']),
                floorY=floor, floorOffline=r3(floor_off), floorLowered=fx['gateFloorLowered'], doorPlaneGroundOffline=[r2(dg.min()), r2(dg.max())], leafBottomY=r3(leaf_bottom),
                late=[round(float(late[0]), 6), round(float(late[1]), 6)], yaw=r3(math.degrees(math.atan2(late[0], late[1]))), halfWidth=hw, top=lay['gateTop'], scale=lay['gateScale'],
                doorPlaneZ=sg['doorPlaneZ'], passageHalfWidth=sg['doorHalfWidth'], doorBottom=sg['doorBottom'], doorBury=sg['doorBury'], requiredFact=fx['requiredFact'],
                pieces=pieces, boxes=lay['gateBoxes'], skipTags=gk['skipTags'], noShadowTags=gk['noShadowTags'], doorTag=gk['doorTag'],
                leaves=dict(leftPrefab=gk['leftDoorPrefab'], rightPrefab=gk['rightDoorPrefab'], hingeX=dhw, hingeZ=zh, openDegrees=gk['openDegrees'], duration=gk['duration'], swing=gk['swing'],
                            meshOffset=[r3(dhw), 0.0, r3(sg['doorPlaneZ'] - zh)], collider=gk['leafCollider'],
                            clearHalfWidthOpen=r3(dhw - (zh - gk['doorZ'][0])),
                            swingStudy=dict(late=swing('late'), ea=swing('ea'),
                                            note='ground minus leaf bottom (m, + = the leaf bottom is in the ground) on the offline lattice: ploughMax = deepest during the swing, openGroundOverBottom = range under the open leaf. late = into the passage (built), ea = out through the EA-side arch (not built: the 8.8 m leaf does not pass the arch ring above 5.3 m)')),
                colliders=gk['colliders'], blocker=gk['blocker'], obstacle=gk['obstacle'], note=gk['gateNote'])
    # ---- flat records
    def rec(m):
        c = (m['a'] + m['b']) / 2
        return dict(id=m['id'], wing=m['line'], leg=m['leg'], gateSpan=m['gateSpan'], a=[r3(m['a'][0]), r3(m['a'][1])], b=[r3(m['b'][0]), r3(m['b'][1])], centre=[r3(c[0]), r3(c[1])],
                    yaw=r3(math.degrees(math.atan2(m['late'][0], m['late'][1]))), late=[round(float(m['late'][0]), 6), round(float(m['late'][1]), 6)], length=r3(m['length']), scale=round(m['scale'], 4), halfDepth=B.hd,
                    baseA=r3(m['baseA']), baseB=r3(m['baseB']), topA=r3(m['baseA'] + B.ptop), topB=r3(m['baseB'] + B.ptop), grade=round((m['baseB'] - m['baseA']) / m['length'], 4),
                    gradeNatural=round((m['naturalB'] - m['naturalA']) / m['length'], 4), lowered=r2(max(m['naturalA'] - m['baseA'], m['naturalB'] - m['baseB'])),
                    rockA=m['rockA'], rockB=m['rockB'], buried=m['buried'], hidden=m['hidden'], floatMax=r2(m['floatMax']), sunkMax=r2(m['sunkMax']) if m['sunkMax'] is not None else -1.0,
                    eaGround=r2(m['eaGround']) if m['eaGround'] is not None else -1.0, topOverEa=r2(m['topOverEa']) if m['topOverEa'] is not None else 99.0, eaRockShare=round(m['eaRockShare'], 2), deckOverEa=r2(m['deckOverEa']), a1=r1(m['a1']), a1Top=r1(m['a1Top']), a1Over=bool(m['a1'] > lim['a1Limit']),
                    late16=r1(m['late16']) if m['late16'] is not None else -99.0, aboveDeck=m['aboveDeck'], aboveTop=m['aboveTop'], parapet='cap', parapetHill=True, parapetEa=True,
                    reachTop=m['reachTop'], reachOver=m['reachOver'], launchCells=m['launchCells'],
                    onRoad=round(m['onRoad'], 3), inCorridor=round(m['inCorridor'], 3), protectedCells=m['protected'], wetCells=m['wet'], lineOffset=r2(m['lineOffset']))
    modules = [rec(m) for w in ('west', 'east') for m in lines[w]['mods']]
    joints = []
    for w in ('west', 'east'):
        j = lines[w]['joints']
        joints.append(dict(wing=w, x=[r3(q[0]) for q in j['xz']], z=[r3(q[1]) for q in j['xz']], natural=[r3(v) for v in j['natural']], built=[r3(v) for v in j['built']], rock=j['rock']))
    kb = kit['bastion']; bast = []
    for w in ('west', 'east'):
        for c in lines[w]['chis']:
            bast.append(dict(id=c['id'], wing=w, kind=c['kind'], reason=c['reason'], at=[r3(c['at'][0]), r3(c['at'][1])], late=[round(float(c['late'][0]), 6), round(float(c['late'][1]), 6)],
                             yaw=r3(math.degrees(math.atan2(c['late'][0], c['late'][1]))), baseY=r3(c['baseY']), groundFront=r2(c['groundFront']), frontBuried=bool(c['groundFront'] > B.ptop), hidden=c['hidden'], anchor=c['anchor'], index=c['index'],
                             deckY=r3(c['baseY'] + kb['deck']['y'])))
    chi_boxes = [dict(q) for q in lay['chiBoxes']]
    for q in chi_boxes:
        if q['tag'] == 'chi_core':      # carry the fill back under the wall's parapet cap (the look-gate kit left a slot behind it)
            z1 = q['cz'] + q['sz'] / 2; q['cz'] = round((kb['coreBackZ'] + z1) / 2, 4); q['sz'] = round(z1 - kb['coreBackZ'], 4)
    # ---- rock slots (AC-B11c): modules the offline estimate reaches by a run or a plain jump
    rk = Pb['rocks']; slots = []
    for w in ('west', 'east'):
        for m in lines[w]['mods']:
            if m['hidden'] or m['buried'] or m['reachTop'] not in ('walk', 'jump') or m['launchAt'] is None: continue
            fit = sorted(rk['prefabs'], key=lambda q: abs(max(q['size'][0], q['size'][2]) - m['launchSpan']))[0]
            slots.append(dict(module=m['id'], x=m['launchAt'][0], z=m['launchAt'][1], yaw=r3(math.degrees(math.atan2(m['late'][0], m['late'][1]))), prefab=fit['name'], path=fit['path'], scale=1.0,
                              bury=rk['bury'], launchCells=m['launchCells'], launchSpan=m['launchSpan'], nearest=m['launchNear'], reach=m['reachTop'], lod0Tris=fit['lod0Tris']))
    slots.sort(key=lambda q: -q['launchCells']); kept = slots[:rk['maxSlots']]
    # ---- probe points for the editor check
    probes = [dict(id='spec_%d' % (i + 1), x=q[0], z=q[1], kind='spec section 6 (S0 lattice leak cell)') for i, q in enumerate(Pb['probes']['points'])]
    for w in ('west', 'east'):
        e = B.fixed[w][-1]; probes.append(dict(id=w + '_end', x=r1(e[0]), z=r1(e[1]), kind='wing end (inside the rock)'))
        vis = [m for m in lines[w]['mods'] if not m['buried'] and not m['gateSpan']]; lv = vis[-1]
        probes.append(dict(id=w + '_cliff_joint', x=r1(lv['b'][0]), z=r1(lv['b'][1]), kind='last visible module end = the wall meets the rock'))
        gj = lines[w]['pts'][1]; probes.append(dict(id=w + '_gate_joint', x=r1(gj[0]), z=r1(gj[1]), kind='gate joint (Seal308 frame end on the wall centreline)'))
    # ---- end embed (AC-B11b)
    ends = {}
    X1, Z1 = np.meshgrid(np.arange(B.study['window'][0], B.study['window'][2] + .5, 1.0), np.arange(B.study['window'][1], B.study['window'][3] + .5, 1.0))
    g1 = tri(F.new, X1, Z1); c1 = tri(B.cliff_fill, X1, Z1) > lim['buriedFill']; s1 = sample(B.slope_stage, X1, Z1) <= B.P['deck']['walkSlopeDeg']; pr = B.probe
    reach_up = max(v['nominalRise'] for v in pr['variants'])
    for w in ('west', 'east'):
        e = B.fixed[w][-1]; fe = float(tri(B.cliff_fill, e[0], e[1])); mods_ = lines[w]['mods']; vis = [m for m in mods_ if not m['hidden']]; near = 0; nearest = None
        for m in vis:
            d = seg_dist(np.asarray([m['a'], m['b']]), X1.ravel(), Z1.ravel()).reshape(X1.shape); top = (m['baseA'] + m['baseB']) / 2 + B.ptop
            hit = (d <= B.hd + lm['edgeClear']) & c1 & s1 & (g1 >= top - reach_up) & (g1 <= top + 6.0)
            if hit.any():
                near += int(hit.sum()); k = np.argmax(hit); nearest = (m['id'], r1(X1.ravel()[k]), r1(Z1.ravel()[k]), r1(g1.ravel()[k] - top))
        ends[w] = dict(end=[r1(e[0]), r1(e[1])], fillAtEnd=r1(fe), endInsideRock=bool(fe >= lm['endFillMin']), buriedModules=sum(m['buried'] for m in mods_), hiddenModules=sum(m['hidden'] for m in mods_),
                       standableCliffCellsNearTop=near, nearestStandable=list(nearest) if nearest else [],
                       note='standableCliffCellsNearTop = 1 m cells of walkable cliff-raised ground within edgeClear m of a built module and between (wall top - %.2f m) and (wall top + 6 m): 0 = no wall top within reach of a cliff edge (AC-B11b)' % reach_up)
    gate['fillAtA'] = r1(float(tri(B.cliff_fill, A[0], A[1]))); gate['fillAtB'] = r1(float(tri(B.cliff_fill, Bp[0], Bp[1])))
    # ---- counts and budget
    pk = B.pack; mesh_of = lambda name: pk['modules'][pk['prefabs'][name]['mesh_fbx'][0].replace('Models/', '')]
    tri_of = lambda name: mesh_of(name)['tris']; vert_of = lambda name: mesh_of(name)['verts']; mats_of = lambda name: [q for q in pk['prefabs'][name]['materials'] if q.startswith('M')]
    built_mods = [m for m in modules if not m['hidden']]; wp, pp = lay['wallPrefab'], lay['parapetPrefab']
    box_t, box_v = 12, 24
    chi_t = sum(tri_of(q['prefab']) for q in lay['chiPieces']) + box_t * len(chi_boxes); chi_v = sum(vert_of(q['prefab']) for q in lay['chiPieces']) + box_v * len(chi_boxes)
    static_pieces = [q for q in pieces if q['tag'] != gk['doorTag']]; doors = [q for q in pieces if q['tag'] == gk['doorTag']]
    gate_t = sum(tri_of(q['prefab']) for q in static_pieces) + box_t * len(lay['gateBoxes']); gate_v = sum(vert_of(q['prefab']) for q in static_pieces) + box_v * len(lay['gateBoxes'])
    door_t = sum(tri_of(q['prefab']) for q in doors)
    built_bast = [c for c in bast if not c['hidden']]
    tris = dict(modules=len(built_mods) * (tri_of(wp) + tri_of(pp)), bastions=len(built_bast) * chi_t, gate=gate_t, leaves=door_t)
    tris['total'] = sum(tris.values())
    # batches: one merged mesh per chunk (sub-mesh per ink material) for the wall, one per wing for the bastions, one for the gate masonry + gate house; leaves = pack meshes
    chunks = []
    for w in ('west', 'east'):
        ids = [m['id'] for m in built_mods if m['wing'] == w]; n = max(1, int(math.ceil(len(ids) / st['chunkMaxModules']))); per = int(math.ceil(len(ids) / n))
        for k in range(n): chunks.append(dict(name='%s_%d' % (w, k + 1), wing=w, modules=ids[k * per:(k + 1) * per]))
    wall_mats = sorted(set(mats_of(wp) + mats_of(pp))); chi_mats = sorted({q2 for q in lay['chiPieces'] for q2 in mats_of(q['prefab'])} | set(mats_of(lay['chiBoxes'][0]['materialFrom'])))
    quiet = [q for q in static_pieces if q['tag'] in gk['noShadowTags']]; loud = [q for q in static_pieces if q['tag'] not in gk['noShadowTags']]
    masonry_mats = sorted({q2 for q in loud for q2 in mats_of(q['prefab'])} | {q2 for q in lay['gateBoxes'] for q2 in mats_of(q['materialFrom'])}); timber_mats = sorted({q2 for q in quiet for q2 in mats_of(q['prefab'])})
    chi_wings = sorted({c['wing'] for c in built_bast})
    batches = dict(wallChunks=len(chunks) * len(wall_mats), bastions=len(chi_wings) * len(chi_mats), gate=len(masonry_mats) + len(timber_mats), leaves=sum(len(mats_of(q['prefab'])) for q in doors), rocks=len(kept) if rk['build'] else 0)
    batches['total'] = sum(batches.values())
    stride = kit['vertexStride']; verts = dict(modules=len(built_mods) * (vert_of(wp) + vert_of(pp)), bastions=len(built_bast) * chi_v, gate=gate_v)
    mem = sum(verts.values()) * stride + (tris['modules'] + tris['bastions'] + tris['gate']) * 3 * 2
    colliders = dict(modules=len(built_mods), bastions=len(built_bast), gate=len(gk['colliders']), blocker=1, leaves=2, rocks=len(kept) if rk['build'] else 0)
    colliders['total'] = sum(colliders.values())
    no_shadow = len(quiet)
    shadow = dict(casters=len(chunks) + len(chi_wings) + 1 + len(doors), gateMasonryMaterials=len(masonry_mats), gateTimberMaterials=len(timber_mats), note='renderers with shadow casting on: wall chunks, one bastion mesh per wing, the gate masonry + roof mesh, two leaves. The gate house timber (%d pieces: %s) is a second gate mesh without shadow casting' % (no_shadow, ', '.join(gk['noShadowTags'])))
    bd = Pb['budget']
    counts = dict(modules=len(modules), modulesBuilt=len(built_mods), modulesHidden=len(modules) - len(built_mods), bastions=len(bast), bastionsBuilt=len(built_bast), triangles=tris, vertices=verts, batches=batches, shadow=shadow, colliders=colliders,
                  meshMemoryMB=round(mem / 1048576, 2), meshMemoryNote='vertices x %d bytes + 16-bit indices of the merged wall / bastion / gate meshes; the two leaves use the pack meshes as they are (no copy)' % stride,
                  renderers=len(chunks) + len(chi_wings) + 2 + len(doors) + (len(kept) if rk['build'] else 0),
                  lod='the pack has no LODGroup and no LOD mesh: the wall is drawn at LOD0 at every distance (far cost = the triangles and batches above while a chunk is in the frustum); chunks of <= %d modules are the culling unit' % st['chunkMaxModules'],
                  budget=bd, within=dict(triangles=tris['total'] <= bd['triangles'], batches=batches['total'] <= bd['batches'], colliders=colliders['total'] <= bd['colliders'], meshMemory=mem / 1048576 <= bd['meshMemoryMB']),
                  p0Preview='206,299 triangles for 65 modules + 7 bastions + gate with Roof_001 (88,006) [M-eng look gate]')
    # ---- wing summaries
    def five(a): a = np.asarray(a, float); return dict(min=r2(a.min()), p50=r2(np.percentile(a, 50)), p90=r2(np.percentile(a, 90)), max=r2(a.max())) if len(a) else None
    wings = {}
    for w in ('west', 'east'):
        ln = lines[w]; ms = [m for m in modules if m['wing'] == w]; vis = [m for m in ms if not m['buried']]; wing_vis = [m for m in vis if not m['gateSpan']]
        plan = measure(B.plan_fields(), B.fixed[w], B.study['wings'][w]['lateLeft'])   # the plan definition (line resampled at the module length): the brief's expected counts and A1 [O]
        wings[w] = dict(line=[[r1(a), r1(b)] for a, b in B.fixed[w]], lineLength=r1(plen(B.fixed[w])), lateLeft=B.study['wings'][w]['lateLeft'],
                        built=[[r3(a), r3(b)] for a, b in ln['pts']], builtLength=r2(ln['length']), bendsDeg=[r1(a) for a in ln['bend'][1:-1]],
                        modules=len(ms), gateSpan=sum(m['gateSpan'] for m in ms), wing=sum(not m['gateSpan'] for m in ms), visible=len(vis), visibleWing=len(wing_vis), buried=len(ms) - len(vis), hidden=sum(m['hidden'] for m in ms),
                        scale=[round(min(m['scale'] for m in ms), 4), round(max(m['scale'] for m in ms), 4)],
                        gradeMax=round(max(abs(m['grade']) for m in ms), 4), gradeNaturalMax=round(max(abs(m['gradeNatural']) for m in vis), 4), loweredMax=r2(max(m['lowered'] for m in vis)),
                        floatMax=r2(max(m['floatMax'] for m in ms)), sunk=five([m['sunkMax'] for m in vis if m['sunkMax'] >= 0]),
                        topOverEa=five([m['topOverEa'] for m in vis]), deckOverEa=five([m['deckOverEa'] for m in vis]),
                        a1=dict(**five([m['a1'] for m in wing_vis]), over=sum(m['a1Over'] for m in wing_vis), of=len(wing_vis), limit=lim['a1Limit']),
                        deckAccess=dict(aboveDeck=sum(m['aboveDeck'] for m in wing_vis), aboveTop=sum(m['aboveTop'] for m in wing_vis), of=len(wing_vis),
                                        reachTop={k: sum(m['reachTop'] == k for m in vis) for k in ('walk', 'jump', 'guk+jump', 'none')}, reachOver={k: sum(m['reachOver'] == k for m in vis) for k in ('walk', 'jump', 'guk+jump', 'none')}),
                        lineOffsetMax=r2(max(m['lineOffset'] for m in ms if not m['gateSpan'])),
                        planDefinition=dict(modules=plan['modules'], visible=plan['visible_modules'], a1=[plan['A1']['min'], plan['A1']['p50'], plan['A1']['max']], a1Over=plan['A1']['modules_over_limit'],
                                            note='the line resampled at the module length (Tools/Art/jangseong308.py measure, as in the Q3 study): the counts and A1 the decision was taken on. Built modules follow each leg on its own, so their count can differ by one per leg'),
                        end=ends[w], notes=ln['notes'])
    ag = np.asarray(lm['agwi'], float); dmin = min(float(seg_dist(np.asarray([m['a'], m['b']]), ag[0], ag[1])[0]) - B.hd for m in modules)
    for c in bast: dmin = min(dmin, float(np.hypot(c['at'][0] - ag[0], c['at'][1] - ag[1])) - 10.0)
    kitw = dict(kit['wall']); kitw['colliderProfileZY'] = [v for q in kitw.pop('colliderProfile') for v in q]
    mat = kit['materials']
    return dict(version=Pb['version'], status=Pb['status'], spec=Pb['spec'], decisions=Pb['decisions'], root=fx['root'], stage='1b', inputs=B.inputs(),
                stageHeight=rel(B.stage_path), stageHeightIsPreview='PREVIEW' in B.stage_path.name, cliffMask=B.cliff_src, opsVersion=B.ops.get('version', ''),
                packPrefabDir=lay['packPrefabDir'], wallPrefab=wp, parapetPrefab=pp, moduleLength=B.L, moduleHeight=B.mh, moduleHalfDepth=B.hd, parapetY=lay['parapetY'], parapetZ=lay['parapetZ'], parapetTop=B.ptop,
                seat=dict(gradeCap=st['gradeCap'], floatTolerance=st['floatTolerance'], sinkPasses=st['sinkPasses'], footSamplesAcross=st['footSamplesAcross'], samplesAlong=st['samplesAlong'], samplesAcross=st['samplesAcross'],
                          hiddenMargin=st['hiddenMargin'], chunkMaxModules=st['chunkMaxModules'], note=st['seatNote']),
                limits=dict(topOverEaMin=lm['topOverEaMin'], eaStripFrom=lm['eaStripFrom'], eaStripTo=lm['eaStripTo'], eaStripStep=lm['eaStripStep'], endFillMin=lm['endFillMin'], edgeClear=lm['edgeClear'], floorTolerance=lm['floorTolerance'],
                            a1Radius=lim['a1Radius'], a1SideMin=lim['a1SideMin'], a1Limit=lim['a1Limit'], deckRadius=Pb['deck']['radius'], agwiX=lm['agwi'][0], agwiZ=lm['agwi'][1], agwiMin=lm['agwiMin'], note=lm['limitsNote']),
                wings=wings, chunks=chunks, modules=modules, joints=joints,
                bastions=bast, bastionKit=dict(pieces=lay['chiPieces'], boxes=chi_boxes, colliderParts=kb['colliderParts'], deckY=kb['deck']['y'], deckRect=kb['deck']['rect'], rule=Pb['bastion'], note=kb['bastionNote']),
                gate=gate, wallKit=kitw,
                materials=dict(shader=mat['shader'], floatNames=list(mat['floats'].keys()), floatValues=list(mat['floats'].values()), bumpFallback=mat['bumpFallback'], note=mat['materialsNote']),
                vertexStride=stride, vertexNote=kit['vertexNote'],
                rocks=dict(build=rk['build'], slots=kept, candidates=len(slots), note=rk['rocksNote']), probes=dict(points=probes, every=Pb['probes']['every'], leakPast=Pb['probes']['leakPast'], hillBack=Pb['probes']['hillBack'], gateLanes=Pb['probes']['gateLanes'], roadHalfWidth=Pb['probes']['roadHalfWidth'], shellJointRadius=Pb['probes']['shellJointRadius'], note=Pb['probes']['probesNote']),
                replaces=list(B.ops['wall'].get('replaces', [])), replacesNote='forest seam runs the wall takes over (ops wall.replaces): check switches their shell colliders off for the probes and restores them; the shells are removed by Enclosure305 v305.4b only after check answers yes (AC-B11f)',
                agwiDistance=r1(dmin), counts=counts,
                a1Exception=dict(limit=lim['a1Limit'], radius=lim['a1Radius'], recorded='D308-9d: the wall stands on the foot line; A1 (ground behind the wall more than limit m higher within radius m) fails on every visible module and is a RECORDED EXCEPTION - reported per module (modules[].a1), not fixed'))


def build_main(params_path, write=True):
    B = BuildCtx(params_path); data = build_layout(B); blob = (json.dumps(data, ensure_ascii=False, indent=1) + '\n').encode('utf-8')
    if write:
        B.out.parent.mkdir(parents=True, exist_ok=True); open(B.out, 'wb').write(blob)
        c = data['counts']; print('jangseong308 build ->', rel(B.out), 'sha256', hashlib.sha256(blob).hexdigest()[:16])
        print('  stage height %s%s | ops %s | cliff mask %s' % (data['stageHeight'], ' (PREVIEW)' if data['stageHeightIsPreview'] else '', rel(B.ops_path), data['cliffMask']))
        for w, d in data['wings'].items():
            print('  %s: %d modules (%d gate span + %d wing; visible %d, hidden %d) | plan definition %d (%d visible) | grade max %.3f | top over EA min %.2f | A1 %.1f / %.1f / %.1f over %d/%d | end fill %.1f' % (
                w, d['modules'], d['gateSpan'], d['wing'], d['visible'], d['hidden'], d['planDefinition']['modules'], d['planDefinition']['visible'], d['gradeMax'], d['topOverEa']['min'], d['a1']['min'], d['a1']['p50'], d['a1']['max'], d['a1']['over'], d['a1']['of'], d['end']['fillAtEnd']))
        print('  bastions %d | triangles %d | batches %d | colliders %d | mesh memory %.2f MB | gate floor %.2f (offline rule %.3f)' % (c['bastions'], c['triangles']['total'], c['batches']['total'], c['colliders']['total'], c['meshMemoryMB'], data['gate']['floorY'], data['gate']['floorOffline']))
        return 0
    return B, data, blob


def build_check(params_path):
    """Layout acceptance (offline). Re-runs the build in memory, compares it with the file on disk byte for byte and prints one line per rule."""
    B, data, blob = build_main(params_path, write=False); out = []; fails = 0; Pb = B.P; lim = data['limits']; bd = data['counts']['budget']

    def line(ok, text):
        nonlocal fails
        out.append(('ok   ' if ok else 'FAIL ') + text); fails += 0 if ok else 1
    disk = open(B.out, 'rb').read() if B.out.exists() else b''
    line(disk == blob, 'second run = same bytes as %s (sha256 %s, %d bytes)' % (rel(B.out), hashlib.sha256(blob).hexdigest()[:16], len(blob)))
    mods = data['modules']; g = data['seat']['gradeCap']
    worst = max(mods, key=lambda m: abs(m['grade'])); line(abs(worst['grade']) <= g + 5e-4, 'grade <= %.2f on every module: max |grade| %.4f at %s (%d modules, sheared, no step: joint heights are shared)' % (g, abs(worst['grade']), worst['id'], len(mods)))
    jt = {j['wing']: j for j in data['joints']}
    for w in ('west', 'east'):
        ms = [m for m in mods if m['wing'] == w]; step = max(abs(ms[i]['baseB'] - ms[i + 1]['baseA']) for i in range(len(ms) - 1))
        line(step < 1e-6 and len(jt[w]['built']) == len(ms) + 1, '%s: no step between modules (largest joint mismatch %.4f m, %d joints for %d modules)' % (w, step, len(jt[w]['built']), len(ms)))
    vis = [m for m in mods if not m['hidden']]; w_ = min(vis, key=lambda m: m['topOverEa']); rk_ = [m['id'] for m in vis if m['eaRockShare'] > 0]
    line(w_['topOverEa'] >= lim['topOverEaMin'], 'wall top - EA-side ground >= %.1f m (lattice, strip %.1f..%.1f m in front): min %.2f m at %s; deck (%.2f m) - ground %.1f m in front: min %.2f m' % (
        lim['topOverEaMin'], lim['eaStripFrom'], lim['eaStripTo'], w_['topOverEa'], w_['id'], data['moduleHeight'], B.study['limits']['eaProbe'], min(m['deckOverEa'] for m in vis if m['eaRockShare'] == 0))
        + ('; cliff rock in front of %s is the barrier there and is not counted as ground' % ', '.join(rk_) if rk_ else ''))
    line(max(m['floatMax'] for m in mods) <= data['seat']['floatTolerance'] + 1e-6, 'no module floats: largest gap under a base %.3f m (tolerance %.2f)' % (max(m['floatMax'] for m in mods), data['seat']['floatTolerance']))
    for w, exp in (('west', (20, 19)), ('east', (39, 35))):
        d = data['wings'][w]; pd = d['planDefinition']
        line((pd['modules'], pd['visible']) == exp, '%s plan-definition count %d (%d visible), expected %d (%d) | built: %d on the wing + %d on the gate span = %d (visible %d, buried %d, hidden in rock %d), x scale %.3f..%.3f' % (
            w, pd['modules'], pd['visible'], exp[0], exp[1], d['wing'], d['gateSpan'], d['modules'], d['visible'], d['buried'], d['hidden'], d['scale'][0], d['scale'][1]))
        e = d['end']
        line(e['endInsideRock'] and e['fillAtEnd'] >= lim['endFillMin'], '%s end (%.1f, %.1f): fill %.1f m >= %.1f (inside the rock); standable cliff ground within %.0f m of a wall top and within reach: %d cells' % (
            w, e['end'][0], e['end'][1], e['fillAtEnd'], lim['endFillMin'], lim['edgeClear'], e['standableCliffCellsNearTop']))
        if e['standableCliffCellsNearTop'] > 0: line(False, '%s: wall top within reach of a cliff edge at %s' % (w, e['nearestStandable']))
        a = d['a1']
        line(True, '%s A1 [recorded exception D308-9d]: min / median / max %.1f / %.1f / %.1f m over %d visible wing modules, %d over the %.0f m limit | plan definition %.1f / %.1f / %.1f, %d over' % (
            w, a['min'], a['p50'], a['max'], a['of'], a['over'], a['limit'], d['planDefinition']['a1'][0], d['planDefinition']['a1'][1], d['planDefinition']['a1'][2], d['planDefinition']['a1Over']))
        da = d['deckAccess']
        line(True, '%s hill side: ground within %.0f m above the deck on %d/%d wing modules (above the parapet top on %d); offline reach onto the top: %s; over the wall: %s' % (
            w, lim['deckRadius'], da['aboveDeck'], da['of'], da['aboveTop'], da['reachTop'], da['reachOver']))
        line(d['lineOffsetMax'] <= 3.3, '%s wing modules on the fixed line: largest centre offset %.2f m (the first leg starts at the Seal308 frame end on the wall centreline, 3.2 m to the EA side of the line vertex)' % (w, d['lineOffsetMax']))
    gs = data['gate']; exp_fill = 'gate ends A / B stand outside the rock (fill %.1f / %.1f): the gate joints are bastions' % (gs['fillAtA'], gs['fillAtB']); line(True, exp_fill)
    road = [m for m in mods if m['onRoad'] > 0]; cor = [m for m in mods if m['inCorridor'] > 0]; pw = [m for m in mods if m['protectedCells'] or m['wetCells']]
    line(not road, 'no module on a road (route half width): %d' % len(road))
    line(all(m['gateSpan'] for m in cor), 'no wing module inside a route corridor (half width + %.0f m hard mask): %d; gate-span modules inside it (the gate stands on the road, no terrain op there): %s' % (
        B.study['limits']['routeMargin'], sum(not m['gateSpan'] for m in cor), ', '.join('%s %.0f%%' % (m['id'], m['inCorridor'] * 100) for m in cor) or 'none'))
    line(not pw, 'no module on a protected / wet cell: %d' % len(pw))
    line(len(data['bastions']) > 0, 'bastions %d, built %d (D308-9e: only bends > %.0f deg, grade breaks > %.1f, cliff joints, gate joints):' % (len(data['bastions']), data['counts']['bastionsBuilt'], Pb['bastion']['bendDeg'], Pb['bastion']['gradeBreak']))
    for c in data['bastions']:
        out.append('       %s (%.1f, %.1f) %s | base %.2f | ground at its front face %+.1f m%s' % (c['id'], c['at'][0], c['at'][1], c['reason'], c['baseY'], c['groundFront'],
                   ' - NOT BUILT: the whole kit lies inside the rock' if c['hidden'] else ' (front inside the hill, above the parapet top)' if c['frontBuried'] else ''))
    for w in ('west', 'east'):
        for n in data['wings'][w]['notes']: out.append('       note: ' + n)
    line(all(c['kind'] in ('gate', 'bend', 'cliff', 'grade') for c in data['bastions']), 'every bastion has one of the four allowed reasons; no barbican ring, gun tower, guard post or flag is in the kit (pieces: %s)' % ', '.join(sorted({q['prefab'] for q in gs['pieces']} | {q['prefab'] for q in data['bastionKit']['pieces']} | {data['wallPrefab'], data['parapetPrefab']})))
    line(abs(gs['floorY'] - 187.67) < 1e-9 and abs(gs['floorOffline'] - gs['floorY']) <= lim['floorTolerance'], 'gate floor %.2f m (user-fixed, editor-measured) | offline door-plane rule %.3f m (lowest door-plane ground %.2f - %.2f bury - (%.2f) door bottom), difference %.3f <= %.2f' % (
        gs['floorY'], gs['floorOffline'], gs['doorPlaneGroundOffline'][0], gs['doorBury'], gs['doorBottom'], gs['floorOffline'] - gs['floorY'], lim['floorTolerance']))
    line(abs((gs['blockS1'] - gs['blockS0']) - 2 * gs['halfWidth']) < 1e-3 and 0 < gs['blockS0'] < gs['blockS1'] < gs['length'], 'gate block s %.2f..%.2f of the %.2f m A-B line (%.2f m wide; passage %.2f..%.2f)' % (gs['blockS0'], gs['blockS1'], gs['length'], gs['blockS1'] - gs['blockS0'], gs['passageS0'], gs['passageS1']))
    sw = gs['leaves']['swingStudy']
    out.append('       leaf swing [O]: into the passage (built) plough max L %.2f / R %.2f m, open pose ground over the leaf bottom L %s / R %s m | out through the EA arch (not built) plough max L %.2f / R %.2f, open pose L %s / R %s' % (
        sw['late']['left']['ploughMax'], sw['late']['right']['ploughMax'], sw['late']['left']['openGroundOverBottom'], sw['late']['right']['openGroundOverBottom'], sw['ea']['left']['ploughMax'], sw['ea']['right']['ploughMax'], sw['ea']['left']['openGroundOverBottom'], sw['ea']['right']['openGroundOverBottom']))
    line(data['agwiDistance'] >= lim['agwiMin'], 'distance to the encounter agwi (%.0f, %.0f): %.1f m >= %.0f' % (lim['agwiX'], lim['agwiZ'], data['agwiDistance'], lim['agwiMin']))
    c = data['counts']; wi = c['within']
    line(wi['triangles'], 'LOD0 triangles %d <= %d (modules %d, bastions %d, gate %d, leaves %d; the P0 look gate had 206,299 with Roof_001)' % (c['triangles']['total'], bd['triangles'], c['triangles']['modules'], c['triangles']['bastions'], c['triangles']['gate'], c['triangles']['leaves']))
    line(wi['batches'], 'batches %d <= %d (wall chunks %d, bastions %d, gate %d, leaves %d, rocks %d): merged meshes, one sub-mesh per ink material' % (c['batches']['total'], bd['batches'], c['batches']['wallChunks'], c['batches']['bastions'], c['batches']['gate'], c['batches']['leaves'], c['batches']['rocks']))
    line(wi['colliders'], 'colliders %d <= %d (modules %d, bastions %d, gate %d, blocker %d, leaves %d, rocks %d)' % (c['colliders']['total'], bd['colliders'], c['colliders']['modules'], c['colliders']['bastions'], c['colliders']['gate'], c['colliders']['blocker'], c['colliders']['leaves'], c['colliders']['rocks']))
    line(wi['meshMemory'], 'mesh memory %.2f MB <= %.1f (%d-byte vertices, 16-bit indices); shadow casters %d' % (c['meshMemoryMB'], bd['meshMemoryMB'], data['vertexStride'], c['shadow']['casters']))
    rk = data['rocks']; out.append('       rock slots [O]: %d modules where a run / plain jump from the hill lands on the wall top, %d slots kept (build = %s): %s' % (
        rk['candidates'], len(rk['slots']), rk['build'], ', '.join('%s %s %d cells' % (q['module'], q['reach'], q['launchCells']) for q in rk['slots']) or 'none'))
    head = 'jangseong308 build-check: %s, %d FAIL | stage height %s%s' % ('OK' if fails == 0 else 'NOT OK', fails, data['stageHeight'], ' (PREVIEW - re-run when plan/Stage/height_p1b.bytes is delivered)' if data['stageHeightIsPreview'] else '')
    text = head + '\n' + '\n'.join(out) + '\n'; open(B.out.parent / 'jangseong308_check.txt', 'wb').write(text.encode('utf-8')); print(text, end='')
    # the A1 exception table (per module) for the Spec patch
    rows = ['| 모듈 | 날개 | 중심 (x, z) | 밑 y | A1 (m) | 성벽 윗선 기준 A1 (m) | 적로 쪽 16 m 안 지면 − 밑 (m) | 상판보다 높음 | 오프라인 추정: 윗선에 닿는 수단 |', '|---|---|---|---|---|---|---|---|---|']
    for m in mods:
        if m['buried']: continue
        rows.append('| %s%s | %s | %.1f, %.1f | %.2f | %.1f | %.1f | %.1f | %s | %s |' % (m['id'], ' (관문 구간)' if m['gateSpan'] else '', '서' if m['wing'] == 'west' else '동', m['centre'][0], m['centre'][1], (m['baseA'] + m['baseB']) / 2, m['a1'], m['a1Top'], m['late16'],
                                                                           '예' if m['aboveDeck'] else '아니오', dict(walk='달리기', jump='점프', none='없음').get(m['reachTop'], '국+점프')))
    open(B.out.parent / 'a1_table.md', 'wb').write(('\n'.join(rows) + '\n').encode('utf-8'))
    return 1 if fails else 0


if __name__ == '__main__':
    sys.stdout.reconfigure(encoding='utf-8')
    cmd = sys.argv[1] if len(sys.argv) > 1 else ''
    if cmd in ('build', 'build-check'):
        bp = Path(sys.argv[2]).resolve() if len(sys.argv) > 2 else BUILD_PARAMS
        sys.exit(build_main(bp) if cmd == 'build' else build_check(bp))
    P = configure(sys.argv[2] if len(sys.argv) > 2 else PARAMS)
    if cmd == 'snapshot': snapshot(P)
    elif cmd == 'study': study(P)
    elif cmd == 'layout': layout(P)
    else: print(__doc__)
