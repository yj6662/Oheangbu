"""#308 cliff boundary - measurement library shared by cliff308_align.py / cliff308_eval.py (offline, numpy only).

Face height = the AC definition (SPEC-WORLD-CLIFF-BOUNDARY-308 design 2): the largest rise inside any 8 m window ACROSS the op
line, sampled every 4 m ALONG it, on the triangulated tile surface (the mesh / collider the editor builds), searched from 12 m on
the player side to 8 m on the upper side. The prototype's 'plan window' number (step2.py) is reported next to it for comparison.
"""
import math
import numpy as np
import cliff308_base as B
from cliff308_base import HH, W, sample, sample_tri

OFFS = np.arange(-12.0, 8.01, 1.0)       # across-line sample offsets (m); + = upper side
WIN = 8                                  # AC window (m)


def face_stations(seg, new, step=1):
    """-> array [n, 5]: x, z, face_ac, face_plan_window, s   (one row per op-line station, 4 m apart)."""
    nU = seg.nU; out = []
    for q in range(0, len(seg.P), step):
        p = seg.P[q]; n = nU[q]
        hs = sample_tri(new, p[0] + n[0] * OFFS, p[1] + n[1] * OFFS); best = 0.0
        for a in range(len(OFFS)):
            b1 = min(len(OFFS), a + WIN + 1)
            best = max(best, float(hs[a + 1:b1].max() - hs[a]) if b1 > a + 1 else 0.0)
        up = max(float(sample(new, p[0] + n[0] * oo, p[1] + n[1] * oo)) for oo in (0, 2, 4, 6, 8)); lo = min(float(sample(new, p[0] - n[0] * oo, p[1] - n[1] * oo)) for oo in (4, 6, 8, 10, 12))
        out.append((float(p[0]), float(p[1]), best, up - lo, float(seg.s[q])))
    return np.array(out)


def runs_of(st, cond, step=4.0):
    out = []; cur = None
    for k, ok in enumerate(cond):
        if ok:
            if cur is None: cur = [k, k]
            else: cur[1] = k
        elif cur is not None: out.append(cur); cur = None
    if cur is not None: out.append(cur)
    return [dict(from_xz=[round(st[a, 0]), round(st[a, 1])], to_xz=[round(st[b, 0]), round(st[b, 1])], length_m=round((b - a + 1) * step), face_min_m=round(float(st[a:b + 1, 2].min()), 1), face_max_m=round(float(st[a:b + 1, 2].max()), 1)) for a, b in out]


def face_report(seg, new, floor, windows=(), joint=None):
    """grammar check of one stretch. windows = [(x, z, half_len)] declared lift-bay windows (8-24 m allowed inside).
    joint = bool grid of cells raised (> 3 m) by ANOTHER cliff op: a station whose player-side toe (4 / 8 m out) lies in it is where
    the line dies into that mass (a joint inside rock, not a face) - counted apart and left out of the grammar numbers."""
    st = face_stations(seg, new); r1 = lambda v: round(float(v), 1); n_all = len(st); joints = np.zeros(len(st), bool)
    if joint is not None:
        jd = B.dilate(joint, 1); nU = seg.nU
        for o in (4.0, 8.0):
            i = np.clip(((seg.P[:, 1] - nU[:, 1] * o) / 4).round().astype(int), 0, HH - 1); j = np.clip(((seg.P[:, 0] - nU[:, 0] * o) / 4).round().astype(int), 0, W - 1); joints |= jd[i, j]
    st_all = st; st = st[~joints] if (~joints).any() else st; f = st[:, 2]
    inwin = np.zeros(len(st), bool)
    for x, z, half in windows: inwin |= np.hypot(st[:, 0] - x, st[:, 1] - z) <= half
    flank = np.zeros(len(st), bool)
    for x, z, half in windows: flank |= (np.hypot(st[:, 0] - x, st[:, 1] - z) <= half + 12) & ~inwin
    lt4 = f < 4.0; b345 = (f >= 3.45) & (f < 8.0); b824 = (f >= 8.0) & (f <= 24.0); below = (f > 24.0) & (f < floor)
    e = dict(stations=len(st), station_step_m=4, op_line_m=round(float(seg.L)), face_min_m=r1(f.min()), face_p10_m=r1(np.percentile(f, 10)), face_median_m=r1(np.median(f)), face_p90_m=r1(np.percentile(f, 90)), face_max_m=r1(f.max()),
             plan_window_min_p10_p50_p90_max=[r1(v) for v in (st[:, 3].min(), *np.percentile(st[:, 3], [10, 50, 90]), st[:, 3].max())], class_floor_m=floor,
             m_lt4=int(lt4.sum() * 4), m_3p45_8=int(b345.sum() * 4), m_8_24_outside_windows=int((b824 & ~inwin).sum() * 4), m_8_24_inside_windows=int((b824 & inwin).sum() * 4),
             m_24_to_floor_outside_flanks=int((below & ~inwin & ~flank).sum() * 4), m_24_to_floor_in_flanks=int((below & flank).sum() * 4),
             runs_lt4=runs_of(st, lt4), runs_3p45_8=runs_of(st, b345), runs_8_24=runs_of(st, b824), runs_24_to_floor=runs_of(st, below))
    e['joint_stations_excluded'] = int(joints.sum()); e['joint_length_m'] = int(joints.sum() * 4); e['stations_all'] = n_all
    # the excluded stations are not hidden: face of the line ABOVE the other mass (a low value = a step from that mass's top onto this one)
    e['joint_face_min_m'] = r1(st_all[joints, 2].min()) if joints.any() else None
    e['joint_runs'] = runs_of(st_all, joints) if joints.any() else []
    e['all_stations_face_min_m'] = r1(st_all[:, 2].min())
    e['min_excluding_windows_m'] = r1(f[~inwin].min()) if (~inwin).any() else None
    e['median_excluding_windows_m'] = r1(np.median(f[~inwin])) if (~inwin).any() else None
    return e, st_all


def perimeter_stations(new, foot, seg, front_m=16.0):
    """v6 (stage 1b back closure): the back and the two sides of a raised footprint as face stations. One station per footprint
    vertex that has a 4-neighbour outside the footprint and lies farther than front_m from the op line (the front face has the op
    line's own stations). -> array [n, 6]: x, z, nx, nz, face_ac, s. (x, z) = the middle of the face cell (2 m outside the raised
    vertex), (nx, nz) = unit normal INTO the footprint (the upper side, as on an op line), face_ac = the AC face height (largest rise
    inside any 8 m window across, tile triangles, 12 m outside .. 8 m inside), s = arclength of the nearest op-line station.
    Ordered along the op line (s, then distance from it)."""
    f = foot.astype(float); out_nb = np.zeros(foot.shape, bool)
    out_nb[1:, :] |= ~foot[:-1, :]; out_nb[:-1, :] |= ~foot[1:, :]; out_nb[:, 1:] |= ~foot[:, :-1]; out_nb[:, :-1] |= ~foot[:, 1:]
    gx = np.zeros(foot.shape); gz = np.zeros(foot.shape)
    for di in range(-2, 3):
        for dj in range(-2, 3):
            if di == 0 and dj == 0: continue
            sh = np.zeros(foot.shape); i0, i1 = max(0, -di), HH - max(0, di); j0, j1 = max(0, -dj), W - max(0, dj)
            sh[i0:i1, j0:j1] = f[i0 + di:i1 + di, j0 + dj:j1 + dj]; gx += sh * dj; gz += sh * di
    ii, jj = np.nonzero(foot & out_nb); rows = []
    for i, j in zip(ii.tolist(), jj.tolist()):
        x, z = j * 4.0, i * 4.0; d = np.hypot(seg.P[:, 0] - x, seg.P[:, 1] - z); k = int(d.argmin())
        if d[k] <= front_m: continue
        n = np.array([gx[i, j], gz[i, j]]); L = float(np.hypot(*n))
        if L < 1e-6: continue
        n /= L; px, pz = x - n[0] * 2.0, z - n[1] * 2.0
        hs = sample_tri(new, px + n[0] * OFFS, pz + n[1] * OFFS); best = 0.0
        for a in range(len(OFFS)):
            b1 = min(len(OFFS), a + WIN + 1)
            if b1 > a + 1: best = max(best, float(hs[a + 1:b1].max() - hs[a]))
        rows.append((px, pz, float(n[0]), float(n[1]), best, float(seg.s[k]), float(d[k])))
    rows.sort(key=lambda r: (r[5], r[6]))
    return np.array([r[:6] for r in rows]).reshape(-1, 6)


def back_face_report(st, floor=28.0):
    """grammar of a back face (perimeter_stations rows): the inner-face rule on the far side - no face under the floor."""
    if not len(st): return dict(stations=0)
    f = st[:, 4]; r1 = lambda v: round(float(v), 1)
    lt4 = f < 4.0; b345 = (f >= 3.45) & (f < 8.0); b824 = (f >= 8.0) & (f <= 24.0); below = (f > 24.0) & (f < floor); k = int(f.argmin())
    return dict(stations=len(st), station_note='one per boundary vertex of the raised footprint (about 4 m apart; a diagonal stair gives two)', face_min_m=r1(f.min()), face_min_at=[r1(st[k, 0]), r1(st[k, 1])],
                face_p10_m=r1(np.percentile(f, 10)), face_median_m=r1(np.median(f)), face_p90_m=r1(np.percentile(f, 90)), face_max_m=r1(f.max()), class_floor_m=floor,
                stations_lt4=int(lt4.sum()), stations_3p45_8=int(b345.sum()), stations_8_24=int(b824.sum()), stations_24_to_floor=int(below.sum()),
                ok=bool(f.min() >= floor and np.median(f) >= 40.0), rule='inner-face grammar on the far side: min >= 28 m, median >= 40 m, no 3.45-8 m, no 8-24 m (no lift bay is declared on a back face)')


def chord_deviation(P, chord=600.0, step=20.0):
    """plan-view straightness: for every window of `chord` metres of arclength, the largest distance of the line from the window's
    chord; returns (min over windows, max over windows, n windows). A line shorter than the chord = one window (whole line)."""
    Q, s = B.resample(np.asarray(P, float), 4.0); L = s[-1]; devs = []
    starts = np.arange(0, max(L - chord, 0) + 1e-6, step)
    for s0 in starts:
        m = (s >= s0) & (s <= s0 + chord); R = Q[m]
        if len(R) < 3: continue
        a, b = R[0], R[-1]; ab = b - a; n = np.hypot(*ab) + 1e-9
        devs.append(float((np.abs((R[:, 0] - a[0]) * ab[1] - (R[:, 1] - a[1]) * ab[0]) / n).max()))
    return (round(min(devs), 1), round(max(devs), 1), len(devs)) if devs else (0.0, 0.0, 0)


def outer_rows(seg, new, reach_s0, reach_s2, owner_mask, radius=1500.0, eye=1.7, far=220.0):
    """outer range per station: crest (max along the upper normal), flat-top width, void exposure vs the highest ground a player
    can stand on within `radius` (S0 and S2), per-cell step band inside the owned cells."""
    nU = seg.nU; qs = np.arange(0.0, far + 0.1, 1.0); crest = []; flat = []; cx = []; cz = []
    for p, n in zip(seg.P, nU):
        xs = p[0] + n[0] * qs; zs = p[1] + n[1] * qs; ok = (xs >= 0) & (xs <= 4000) & (zs >= 0) & (zs <= 6000)
        hs = sample_tri(new, xs[ok], zs[ok]); k = int(hs.argmax()); crest.append(float(hs[k])); flat.append(float((hs >= hs[k] - 0.5).sum()))
        cx.append(float(xs[ok][k])); cz.append(float(zs[ok][k]))
    crest = np.array(crest); flat = np.array(flat); r1 = lambda v: round(float(v), 1); res = {}
    X, Z = B.grid_xz(); rr = int(radius / 4)
    for nm, reach in (('S0', reach_s0), ('S2', reach_s2)):
        need = []
        for p in seg.P[::2]:
            i, j = int(p[1] / 4), int(p[0] / 4); i0, i1 = max(0, i - rr), min(HH, i + rr + 1); j0, j1 = max(0, j - rr), min(W, j + rr + 1)
            sub = reach[i0:i1, j0:j1]; mm = sub & (np.hypot(X[i0:i1, j0:j1] - p[0], Z[i0:i1, j0:j1] - p[1]) <= radius)
            need.append(float(new[i0:i1, j0:j1][mm].max()) + eye if mm.any() else -1e9)
        need = np.repeat(np.array(need), 2)[:len(crest)]
        if len(need) < len(crest): need = np.concatenate([need, need[-1:].repeat(len(crest) - len(need))])
        bad = crest < need
        res[nm] = dict(max_reach_eye_y=r1(need.max()), stations_crest_below_eye=int(bad.sum()), length_m=int(bad.sum() * 4), worst_deficit_m=r1((need - crest).max()), min_margin_m=r1((crest - need).min()),
                       z_ranges=[[int(seg.P[a, 1]), int(seg.P[b, 1])] for a, b in _ranges(bad)][:12])
    dzx = np.abs(np.diff(new, axis=1)); dzz = np.abs(np.diff(new, axis=0)); own = owner_mask
    px = int(((dzx >= 3.45) & (dzx < 8) & own[:, 1:] & own[:, :-1]).sum()); pz = int(((dzz >= 3.45) & (dzz < 8) & own[1:, :] & own[:-1, :]).sum())
    return dict(stations=len(crest), crest_y_min_p10_p50_max=[r1(crest.min()), r1(np.percentile(crest, 10)), r1(np.percentile(crest, 50)), r1(crest.max())],
                flat_top_width_m_p50_max=[r1(np.percentile(flat, 50)), r1(flat.max())], stations_flat_top_gt_8m=int((flat > 8).sum()),
                void_check=res, cell_pairs_step_3p45_8_in_owned_cells=px + pz,
                cell_pairs_note='cells above the first step (>= class floor): reachable only if the first step fails; AC-B9c (capsule + guk + jump walker) stays an editor check'), crest, np.stack([cx, cz], 1)


def _ranges(bad):
    idx = np.nonzero(bad)[0]
    if not len(idx): return []
    parts = np.split(idx, np.nonzero(np.diff(idx) > 1)[0] + 1)
    return [(int(q[0]), int(q[-1])) for q in parts]


def los(field, ox, oz, oy, tx, tz, ty, skip=16.0):
    L = math.hypot(tx - ox, tz - oz); n = max(2, int(L / 4)); t = np.linspace(0, 1, n)[1:-1]; t = t[(t * L > skip) & ((1 - t) * L > skip)]
    if not len(t): return True
    return bool(np.all(sample(field, ox + (tx - ox) * t, oz + (tz - oz) * t) < oy + (ty - oy) * t - 0.5))


def ea_observers(content, rcl, spacing=30.0):
    obs = []
    for key in ('MainPath', 'BranchPath'):
        last = None
        for p in content.get(key) or []:
            x, z = p['x'], p['z']
            if last is None or math.hypot(x - last[0], z - last[1]) >= spacing:
                i, j = int(z / 4), int(x / 4)
                if 0 <= i < HH and 0 <= j < W and rcl[max(0, i - 2):i + 3, max(0, j - 2):j + 3].any(): obs.append((x, z)); last = (x, z)
    return np.array(obs)


LANDMARKS = {'sinmok crown (+30 m)': (3710, 3255, 30), 'palace roof (+12 m)': (1876, 3318, 12), 'south gate (+10 m)': (2000, 2530, 10), 'geumpyo inn lantern (+5 m)': (3069, 2339, 5), 'seal gate munru (+14 m)': (1894, 1746, 14)}


def tree_layer(sheets, names=('Forest305', 'DryLandscape')):
    tree = np.zeros((HH, W), bool)
    for name in names:
        P = sheets[name]; i = np.clip((P[:, 2] / 4).round().astype(int), 0, HH - 1); j = np.clip((P[:, 0] / 4).round().astype(int), 0, W - 1); tree[i, j] = True
    return tree


def surf(h, new, tree, crown=12.0):
    """sight surface with the +12 m crown layer (INFERRED upper bound); new faces carry no trees."""
    s_ = new.copy(); face = (np.abs(new - h) > 0.05) & (B.slope_deg(new) >= 40); s_[tree & ~face] += crown; return s_


def landmark_loss(h, new, obs, tree=None):
    F0 = h if tree is None else surf(h, h, tree); F1 = new if tree is None else surf(h, new, tree); out = {}
    for name, (tx, tz, dy) in LANDMARKS.items():
        ty = float(sample(h, tx, tz)) + dy; b = a_ = lost = 0; where = []
        for ox, oz in obs:
            oy = float(sample(h, ox, oz)) + 1.7; vb = los(F0, ox, oz, oy, tx, tz, ty); va = los(F1, ox, oz, oy, tx, tz, ty); b += vb; a_ += va
            if vb and not va: lost += 1; where.append([round(ox), round(oz)])
        out[name] = dict(observers=len(obs), visible_before=int(b), visible_after=int(a_), newly_hidden=lost, where=where[:8])
    return out


def visibility(seg, h, new, obs, field):
    """share of the stretch seen from EA path observers (nearest 60 within 1.5 km), elevation angle of the rim."""
    nU = seg.nU; rows = []
    for q in range(0, len(seg.P), 6):
        p = seg.P[q]; n = nU[q]; top = p + n * 6; ty = float(sample(new, *top)); fy = (ty + float(sample(new, *(p - n * 8)))) / 2
        dist = np.hypot(obs[:, 0] - top[0], obs[:, 1] - top[1]); best = None
        for c in np.argsort(dist)[:60]:
            if dist[c] > 1500: break
            ox, oz = obs[c]; oy = float(sample(new, ox, oz)) + 1.7
            if los(field, ox, oz, oy, p[0], p[1], fy): best = (float(dist[c]), math.degrees(math.atan2(ty - oy, dist[c]))); break
        rows.append(best)
    seen = [r for r in rows if r]
    return dict(samples=len(rows), seen_share=round(len(seen) / max(1, len(rows)), 2), nearest_view_m_p50=round(float(np.median([r[0] for r in seen]))) if seen else None,
                elev_angle_deg_p50=round(float(np.median([r[1] for r in seen])), 1) if seen else None), np.array([r is not None for r in rows])


def envelope(ctx_h, reach, new, solid, wallband, water):
    """EA rim by cause: lattice edges between the reach and the first node outside it."""
    sl = B.slope_deg(new); raised_ = B.dilate((new - ctx_h) > 3, 2)
    out = dict(cliff=0, wall=0, forest_or_seal=0, steep_natural=0, water=0, map_edge=0, other=0); rr = reach
    for di, dj in ((0, 1), (0, -1), (1, 0), (-1, 0)):
        src = np.zeros_like(rr)
        if dj == 1: src[:, :-1] = rr[:, :-1] & ~rr[:, 1:]; nbi = (slice(None), slice(1, None)); me = rr[:, -1].sum()
        elif dj == -1: src[:, 1:] = rr[:, 1:] & ~rr[:, :-1]; nbi = (slice(None), slice(None, -1)); me = rr[:, 0].sum()
        elif di == 1: src[:-1, :] = rr[:-1, :] & ~rr[1:, :]; nbi = (slice(1, None), slice(None)); me = rr[-1, :].sum()
        else: src[1:, :] = rr[1:, :] & ~rr[:-1, :]; nbi = (slice(None, -1), slice(None)); me = rr[0, :].sum()
        si = {(0, 1): (slice(None), slice(None, -1)), (0, -1): (slice(None), slice(1, None)), (1, 0): (slice(None, -1), slice(None)), (-1, 0): (slice(1, None), slice(None))}[(di, dj)]
        e = src[si]; out['map_edge'] += int(me)
        w_ = wallband[nbi] & e; out['wall'] += int(w_.sum()); e = e & ~wallband[nbi]
        c_ = raised_[nbi] & e; out['cliff'] += int(c_.sum()); e = e & ~raised_[nbi]
        f_ = solid[nbi] & e; out['forest_or_seal'] += int(f_.sum()); e = e & ~solid[nbi]
        wa = water[nbi] & e; out['water'] += int(wa.sum()); e = e & ~water[nbi]
        s_ = (sl[nbi] >= 45) & e; out['steep_natural'] += int(s_.sum()); e = e & ~(sl[nbi] >= 45)
        out['other'] += int(e.sum())
    tot = max(1, sum(out.values()))
    return dict(perimeter_m=tot * 4, by_cause_m={k: v * 4 for k, v in out.items()}, by_cause_share={k: round(v / tot, 3) for k, v in out.items()})
