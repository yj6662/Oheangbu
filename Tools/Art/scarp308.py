"""#308 cliff boundary - `scarp308`: ops file -> stage height field. Offline, deterministic, always from the BASE height.
SPEC-WORLD-CLIFF-BOUNDARY-308 (design 1-6, "ops v5"; "ops v6" = stage 1b). Port of the plan prototype (plan/tools + terrain_first/tools/scarp308v2.py);
with ops308_v4.json and --legacy it reproduces plan/Stage/height_p1a_PROTOTYPE.bytes byte for byte (`--selftest`).

  python Tools/Art/scarp308.py <ops.json> --stage 1a                 # -> plan/Stage/height_p1a.bytes (+ .sha256, owner, report)
  python Tools/Art/scarp308.py <ops.json> --stage 1b --out <path>    # cumulative: every op whose stage <= 1b, from the base
  python Tools/Art/scarp308.py --selftest                            # ops308_v4.json (legacy) == height_p1a_PROTOTYPE.bytes

height(stage) = f(base sha, ops sha, stage). Never chained from a previous stage file. All randomness = value noise seeded by
sha256(f'{ops.seed}:{segment id}:{salt}') - no Python hash(), no global RNG, no time.

Ops (data, all numbers in the ops file):
  segments[]   face line + UPPER side + mode
      raise : lower (player) side untouched; upper side -> Pl = max(U_ref, T_ref + H) within top_w, then a back slope or `face2`.
      cut   : upper (player) side untouched; lower side cut to Low = min(T_ref, U_ref - H), talus back to natural.
      both  : raise upper + cut lower.
      outer : raise; first step Pl, then rise_deg up to a crest. `peak` (v5) = peaked ridge: crest >= crest_floor_y, cap <= cap_w
              wide, then down at back_deg -> no flat top, no shelf. Without `peak` = the v4 capped rise (flat cap).
      raster: 'snap' (v5: a vertex is raised fully or not at all -> the face is exactly one 4 m cell, no folded-paper ramp cells
              on the player side) | 'ramp' (v4: linear ramp over w_face).
      rim_amp / rim_wl: value-noise variation of the rim height along the line (H is the MINIMUM: noise only adds).
      stage_overrides (v6): {stage: {key: value}} - from that stage on these keys replace the segment's own (applied in stage
              order). The stage the segment is built in keeps its own keys, so an earlier stage field stays byte-identical.
      back_cut_m (v6): the raise ends this far from the op line (cells farther than that keep the base height): the back slope
              is cut off and the top inside the cut keeps EXACTLY the heights of the uncut stage (same envelope).
      back_close (v6, D308-9e "뒤 비탈 닫기", with back_cut_m or back = 'face2'): every raised cell within crest_w_m of the
              footprint edge (and at least front_keep_m from the op line: the EA rim is never touched) is raised to
              (highest ground OUTSIDE the footprint within foot_ring_m) + face_min_m (+ 0..rim_amp noise): the back and the two
              sides of the plateau become a face of at least face_min_m seen from the ground behind. Raise only.
  road_press   within `within_m` of a route centreline the raise target is clamped to route height + `above_route_m`.
  bays[]       cone-capped recess in a ring mesa rim (국 lift shelf + walk-up ramp), stage 1b. `rim_stretch` k > 1 = elliptical
               cone, k times steeper along the rim than into the mesa (needs `outward`).
  benches[]    wall foundation: `width_m` band levelled across the line to the graded profile (<= max_grade per module);
               only on cells no cliff op raised.
Hard masks (never edited by any op): deep water + wet cells, Watershed295 protected cells, every route corridor (w/2 + margin).
Composition: cuts (min) first, raises (max) second, road_press, bays, benches, hard masks restored last.
"""
import argparse, hashlib, json, math, sys, time
from pathlib import Path
import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
import cliff308_base as B                                                # noqa: E402
from cliff308_base import HH, W, CELL, resample, sample                    # noqa: E402

STAGES_DEFAULT = ['1a', '1b', '2a', '2b', '3']


# ------------------------------------------------------------------ 1-D helpers
def smooth1d(a, win, wrap=False):
    if win <= 1: return a
    k = np.ones(win) / win; pad = win // 2
    ap = np.pad(a, (pad, win - 1 - pad), mode='wrap' if wrap else 'edge'); return np.convolve(ap, k, mode='valid')


def runmin(a, win, wrap=False):
    pad = win // 2; ap = np.pad(a, (pad, pad), mode='wrap' if wrap else 'edge')
    return np.array([ap[i:i + win].min() for i in range(len(a))])


def noise1d(s, wl, seed):
    """value noise in [-1, 1] along arclength s (smoothstep), deterministic for a given integer seed."""
    rng = np.random.default_rng(seed); n = int(s.max() / wl) + 3; v = rng.uniform(-1, 1, n)
    t = s / wl; i = np.floor(t).astype(int); f = t - i; f = f * f * (3 - 2 * f)
    return v[i] * (1 - f) + v[i + 1] * f


def seg_seed(ops_seed, sid, salt=0):
    """ops_seed None = the v4 prototype formula (sum of code points); otherwise sha256(seed:id:salt)."""
    if ops_seed is None: return sum(map(ord, sid)) * 7 % 1000 + salt
    return int.from_bytes(hashlib.sha256(f'{ops_seed}:{sid}:{salt}'.encode('utf-8')).digest()[:4], 'little')


def stage_rank(stages, s):
    return stages.index(s)


def seg_for_stage(d, stages, stage):
    """v6: the segment dict as it applies at `stage` (stage_overrides merged in stage order). Without overrides = d itself."""
    ov = d.get('stage_overrides')
    if not ov: return d
    out = dict(d)
    for s in stages:
        if s in ov and stage_rank(stages, s) <= stage_rank(stages, stage): out.update(ov[s])
    return out


def disc_offsets(radius_m):
    r = int(math.floor(radius_m / CELL + 1e-9))
    return [(di, dj) for di in range(-r, r + 1) for dj in range(-r, r + 1) if (di * di + dj * dj) * CELL * CELL <= radius_m * radius_m + 1e-6]


def shifted(a, di, dj, fill):
    """a moved by (di, dj) cells: out[i, j] = a[i - di, j - dj]; `fill` where that lies outside the array (no wrap)."""
    out = np.full(a.shape, fill, a.dtype); n, m = a.shape
    i0, i1 = max(0, di), min(n, n + di); j0, j1 = max(0, dj), min(m, m + dj)
    if i1 > i0 and j1 > j0: out[i0:i1, j0:j1] = a[i0 - di:i1 - di, j0 - dj:j1 - dj]
    return out


def back_close(bc, raise_t, hw, r, kb, seg, ops_seed):
    """v6 perimeter crest of one segment's raised footprint (window arrays): see the module doc. -> (new raise_t, stats)."""
    foot = raise_t > hw + 0.05
    if not foot.any(): return raise_t, dict(cells=0)
    out_h = np.where(foot, -np.inf, hw); ring = float(bc.get('foot_ring_m', 16.0)); cw = float(bc.get('crest_w_m', 12.0))
    B_ = np.full(hw.shape, -np.inf); near_out = np.zeros(hw.shape, bool); outside = ~foot
    for di, dj in disc_offsets(ring): np.maximum(B_, shifted(out_h, di, dj, -np.inf), out=B_)
    for di, dj in disc_offsets(cw): near_out |= shifted(outside, di, dj, False)
    strip = foot & near_out & (r >= float(bc.get('front_keep_m', 24.0))) & np.isfinite(B_)
    amp = float(bc.get('rim_amp', 0.0)); nz = 0.0
    if amp > 0:
        wl = float(bc.get('rim_wl', 80.0))
        nz = amp * 0.5 * (0.62 * noise1d(seg.s, wl, seg_seed(ops_seed, seg.id, 21)) + 0.38 * noise1d(seg.s, wl / 3.3, seg_seed(ops_seed, seg.id, 22)) + 1.0)
        nz = nz[kb]
    tgt = B_ + float(bc['face_min_m']) + nz
    up = strip & (tgt > raise_t); before = raise_t[up].copy(); new = raise_t.copy(); new[up] = tgt[up]
    return new, dict(cells=int(strip.sum()), cells_raised=int(up.sum()), crest_above_plateau_max_m=round(float((new[up] - before).max()), 1) if up.any() else 0.0,
                     crest_above_plateau_p50_m=round(float(np.median(new[up] - before)), 1) if up.any() else 0.0, face_min_m=float(bc['face_min_m']), crest_w_m=cw, foot_ring_m=ring)


def seg_stage(d):
    """stage of a v5 segment; v4 files carry phase 1 / 1.5 / 2 + optional stage."""
    if 'stage' in d: return d['stage']
    return {1: '1a', 1.5: '1b', 2: '2a'}.get(d.get('phase'), '2a')


# ------------------------------------------------------------------ segment
class Seg:
    """op line = control polyline smoothed (smooth_line_m window), open ends extended, resampled every 4 m."""

    def __init__(self, d):
        self.d = d; self.id = d['id']; self.closed = bool(d.get('closed'))
        P = np.asarray(d['points'], float)
        if self.closed and np.hypot(*(P[0] - P[-1])) > 1: P = np.vstack([P, P[:1]])
        Q, _ = resample(P, 2.0)
        win = int(d.get('smooth_line_m', 24) / 2) | 1
        if win > 1 and len(Q) > win:
            k = np.ones(win) / win; pad = win // 2
            Qs = np.stack([np.convolve(np.pad(Q[:, c], (pad, pad), mode='wrap' if self.closed else 'edge'), k, mode='valid') for c in range(2)], 1)
            if not self.closed: Qs[0], Qs[-1] = Q[0], Q[-1]
            Q = Qs
        ext = d.get('extend_m', [14 if e == 'open' else 0 for e in d.get('ends', ['cap', 'cap'])]) if not self.closed else [0, 0]
        if ext[0] > 0:
            t0 = Q[0] - Q[min(5, len(Q) - 1)]; t0 /= np.linalg.norm(t0) + 1e-9; Q = np.vstack([Q[0] + t0 * ext[0], Q])
        if ext[1] > 0:
            t1 = Q[-1] - Q[max(-6, -len(Q))]; t1 /= np.linalg.norm(t1) + 1e-9; Q = np.vstack([Q, Q[-1] + t1 * ext[1]])
        self.P, self.s = resample(Q, 4.0)
        t = (np.roll(self.P, -1, 0) - np.roll(self.P, 1, 0)) if self.closed else np.gradient(self.P, axis=0)
        t /= np.linalg.norm(t, axis=1, keepdims=True) + 1e-9
        self.t = t; self.n = np.stack([-t[:, 1], t[:, 0]], 1)
        self.up = 1.0 if d['upper'] == 'left' else -1.0
        self.L = self.s[-1]

    @property
    def nU(self):
        return self.n * self.up


def profile(fr, frac):
    fr = np.asarray(fr, float); return np.interp(frac, fr[:, 0], fr[:, 1])


def build_refs(seg, h, ops_seed, stages, stage):
    d = seg.d; nU = seg.nU; wrap = seg.closed
    low_ref = d.get('low_ref', 14.0); up_ref = d.get('up_ref', 30.0)
    T = np.stack([sample(h, seg.P[:, 0] - nU[:, 0] * o, seg.P[:, 1] - nU[:, 1] * o) for o in (low_ref, low_ref + 8, low_ref + 16)], 0).min(0)
    U = np.stack([sample(h, seg.P[:, 0] + nU[:, 0] * o, seg.P[:, 1] + nU[:, 1] * o) for o in (up_ref, up_ref + 15, up_ref + 30)], 0).mean(0)
    win = max(3, int(d.get('smooth_m', 60) / 4) | 1)
    T = smooth1d(runmin(T, win, wrap), win, wrap); U = smooth1d(U, win, wrap)
    H = np.full(len(seg.s), float(d['H']))
    if 'H_profile' in d: H = profile(d['H_profile'], seg.s / seg.L)
    if d.get('rim_amp', 0) > 0:      # v5: rim height variation; noise is mapped to [0, 1] so H stays the minimum
        wl = d.get('rim_wl', 90.0)
        nz = 0.62 * noise1d(seg.s, wl, seg_seed(ops_seed, seg.id, 11)) + 0.38 * noise1d(seg.s, wl / 3.3, seg_seed(ops_seed, seg.id, 12))
        if wrap:                     # closed ring: blend the seam so the two ends agree
            w = np.clip(seg.s / min(60.0, seg.L / 4), 0, 1) * np.clip((seg.L - seg.s) / min(60.0, seg.L / 4), 0, 1); nz = nz * w
        H = H + d['rim_amp'] * (nz + 1) * 0.5
    dips = [dp for dp in d.get('H_dips', []) if stage_rank(stages, dp.get('stage', seg_stage(d))) <= stage_rank(stages, stage)]
    for dip in dips:
        dd = np.hypot(seg.P[:, 0] - dip['at'][0], seg.P[:, 1] - dip['at'][1])
        w = np.clip((dd - dip['half_len']) / dip.get('blend_m', 12.0), 0, 1); H = np.minimum(H, dip['H'] + (H - dip['H']) * w)
    if not seg.closed:
        for k, e in enumerate(d.get('ends', ['cap', 'cap'])):
            if e == 'taper':
                tl = d.get('taper_m', 60.0); w = np.clip((seg.s if k == 0 else seg.L - seg.s) / tl, 0, 1); H *= w * w * (3 - 2 * w)
    mode = d['mode']
    if mode in ('raise', 'outer'): Pl = np.maximum(U, T + H); Low = T
    elif mode == 'cut': Pl = U; Low = np.minimum(T, U - H)
    else: Pl = np.maximum(U, T + H * 0.5 + (U - T) * 0.5); Low = np.minimum(T, Pl - H)
    for dip in dips:                 # a dip must really cap the face: the plateau above it is pinned to T + H
        dd = np.hypot(seg.P[:, 0] - dip['at'][0], seg.P[:, 1] - dip['at'][1]); m = dd <= dip['half_len'] + 4
        Pl[m] = np.minimum(Pl[m], T[m] + H[m])
    return T, U, H, Pl, Low


def apply_segment(seg, h, X, Z, hard, protect, ops_seed, stages, stage):
    d = seg.d; mode = d['mode']; w_face = d.get('w_face', 4.0); top_w = d.get('top_w', 80.0); snap = d.get('raster', 'ramp') == 'snap'
    T, U, H, Pl, Low = build_refs(seg, h, ops_seed, stages, stage)
    reach = top_w + 400 if mode == 'outer' else top_w + max(120.0, d.get('back_w', 0))
    reach = max(reach, d.get('cut_w', 60) + w_face + 10)
    x0, z0 = seg.P.min(0) - reach; x1, z1 = seg.P.max(0) + reach
    j0, j1 = max(0, int(x0 // 4)), min(W - 1, int(x1 // 4) + 1); i0, i1 = max(0, int(z0 // 4)), min(HH - 1, int(z1 // 4) + 1)
    xs = X[i0:i1 + 1, j0:j1 + 1]; zs = Z[i0:i1 + 1, j0:j1 + 1]
    best = np.full(xs.shape, np.inf); kb = np.zeros(xs.shape, int); env = np.full(xs.shape, -np.inf)
    crest = np.zeros(len(seg.P)); info = {}
    if mode == 'outer':
        A_ = d.get('crest_amp', 35.0); wl = d.get('crest_wl', 140.0)
        crest = A_ * (noise1d(seg.s, wl, seg_seed(ops_seed, seg.id, 0)) + 0.5 * noise1d(seg.s, wl / 3, seg_seed(ops_seed, seg.id, 7)))
        rise = math.tan(math.radians(d.get('rise_deg', 52)))
        rmax = np.full(len(seg.s), float(d.get('rise_max', 140.0)))
        if 'cap_profile' in d: rmax = profile(d['cap_profile'], seg.s / seg.L)
    elif d.get('crest_amp', 0) > 0:
        crest = d['crest_amp'] * noise1d(seg.s, d.get('crest_wl', 90.0), seg_seed(ops_seed, seg.id, 0))
    back = d.get('back', 30.0); tb = None if back == 'face2' else math.tan(math.radians(back))
    for k in range(len(seg.P)):
        dd = (xs - seg.P[k, 0]) ** 2 + (zs - seg.P[k, 1]) ** 2
        m = dd < best; best[m] = dd[m]; kb[m] = k
        if mode == 'outer': continue
        dk = np.sqrt(dd)
        if tb is None: e = np.where(dk <= top_w, Pl[k] + crest[k] * np.clip(dk / 30, 0, 1), -np.inf)
        else: e = Pl[k] + crest[k] * np.clip(dk / 30, 0, 1) - np.maximum(0, dk - top_w) * tb
        np.maximum(env, e, out=env)
    px = seg.P[kb, 0]; pz = seg.P[kb, 1]; nx = seg.n[kb, 0] * seg.up; nz = seg.n[kb, 1] * seg.up
    tx = seg.t[kb, 0]; tz = seg.t[kb, 1]
    q = (xs - px) * nx + (zs - pz) * nz; along = (xs - px) * tx + (zs - pz) * tz
    beyond = np.zeros(xs.shape, bool) if seg.closed else (((kb == 0) & (along < -2)) | ((kb == len(seg.P) - 1) & (along > 2)))
    r = np.sqrt(best); pl = Pl[kb]; lo = Low[kb]
    if mode == 'outer':
        pk = d.get('peak')
        if pk:      # v5 peaked ridge: first step, rise to the crest, short cap, back slope down (no flat top / shelf)
            Ck = Pl + rmax + crest
            if 'crest_floor_y' in pk:
                fl = pk['crest_floor_y']; fl = profile(fl, seg.s / seg.L) if isinstance(fl, list) else np.full(len(seg.s), float(fl))
                Ck = np.maximum(Ck, fl + np.abs(crest) * pk.get('serration', 0.5))
            if 'crest_ceiling_y' in pk:
                ce = pk['crest_ceiling_y']; ce = profile(ce, seg.s / seg.L) if isinstance(ce, list) else np.full(len(seg.s), float(ce))
                Ck = np.minimum(Ck, ce)
            Ck = np.maximum(Ck, Pl + pk.get('min_rise', 8.0))
            dc = (Ck - Pl) / rise; capw = pk.get('cap_w', 6.0); tbk = math.tan(math.radians(pk.get('back_deg', 60.0)))
            ck = Ck[kb]; dck = dc[kb]; qq = np.maximum(q, 0)
            up_t = np.minimum(np.minimum(pl + qq * rise, ck), ck - np.maximum(0, qq - dck - capw) * tbk)
            info['crest_y'] = Ck; info['crest_dist'] = dc
        else:
            up_t = np.minimum(pl + np.maximum(q, 0) * rise, pl + rmax[kb] + crest[kb])
    else:
        up_t = env
        if d.get('back_cut_m') is not None: up_t = np.where(r <= float(d['back_cut_m']), up_t, -np.inf)      # v6: no raise behind the cut
    face_t = lo + (pl - lo) * np.clip(1 + q / w_face, 0, 1)
    raise_t = np.full(xs.shape, -np.inf); cut_t = np.full(xs.shape, np.inf)
    ok = ~beyond
    if mode in ('raise', 'outer', 'both'):
        m = ok & (q >= 0); raise_t[m] = up_t[m]
        if not snap:
            m = ok & (q < 0) & (q > -w_face); raise_t[m] = face_t[m]
    if mode in ('cut', 'both'):
        talus = math.tan(math.radians(d.get('talus_deg', 34))); cw = d.get('cut_w', 60.0)
        m = ok & (q < 0) & (q > -w_face); cut_t[m] = np.minimum(cut_t[m], lo[m] if snap else face_t[m])
        m = ok & (q <= -w_face) & (q > -(w_face + cw)); cut_t[m] = lo[m] + (-q[m] - w_face) * talus
    if not seg.closed:
        for k, e in enumerate(d.get('ends', ['cap', 'cap'])):
            if e != 'cap': continue
            idx = 0 if k == 0 else len(seg.P) - 1
            m = beyond & (kb == idx)
            Rc = min(top_w, d.get('cap_r', 30.0))
            inner = m & (r <= Rc) & (q > -Rc)
            if mode in ('raise', 'outer', 'both'):
                raise_t[inner] = Pl[idx]
                if not snap:
                    ring = m & (r > Rc) & (r < Rc + w_face) & (q > -Rc)
                    raise_t[ring] = np.maximum(raise_t[ring], Low[idx] + (Pl[idx] - Low[idx]) * (1 - (r[ring] - Rc) / w_face))
    hd = hard[i0:i1 + 1, j0:j1 + 1]
    raise_t[hd] = -np.inf; cut_t[hd] = np.inf
    if protect is not None and d.get('protect_ea'):
        pr = protect[i0:i1 + 1, j0:j1 + 1] & (r > w_face + 2)
        raise_t[pr] = -np.inf; cut_t[pr] = np.inf
    bc = d.get('back_close')
    if bc and mode in ('raise', 'both'):      # v6: masked cells are already out of the footprint, so the crest never edits one
        raise_t, info['back_close'] = back_close(bc, raise_t, h[i0:i1 + 1, j0:j1 + 1], r, kb, seg, ops_seed)
    info.update(T=T, U=U, H=H, Pl=Pl, Low=Low)
    return (i0, i1, j0, j1), raise_t, cut_t, info


# ------------------------------------------------------------------ masks and the other ops
def route_mask(routes, margin, X, Z, ids=None):
    m = np.zeros((HH, W), bool)
    for r in routes:
        if ids is not None and r['id'] not in ids: continue
        P = np.asarray(r['pts'], float); half = r['width'] / 2 + margin
        Q, _ = resample(P, 3.0); rr = int(math.ceil(half / 4)) + 1
        for x, z in Q:
            j0, i0 = int(round(x / 4)), int(round(z / 4))
            js = slice(max(j0 - rr, 0), min(j0 + rr + 1, W)); is_ = slice(max(i0 - rr, 0), min(i0 + rr + 1, HH))
            m[is_, js] |= np.hypot(X[is_, js] - x, Z[is_, js] - z) <= half
    return m


def road_press_cap(routes, op, h, X, Z):
    """cap[i, j] = lowest (route height + above) over the route points within `within_m`; +inf elsewhere."""
    cap = np.full((HH, W), np.inf); within = float(op['within_m']); above = float(op['above_route_m']); ids = op.get('route_ids')
    rr = int(math.ceil(within / 4)) + 1
    for r in routes:
        if ids and r['id'] not in ids: continue
        Q, _ = resample(np.asarray(r['pts'], float), 3.0); y = sample(h, Q[:, 0], Q[:, 1])
        for (x, z), yy in zip(Q, y):
            j0, i0 = int(round(x / 4)), int(round(z / 4))
            js = slice(max(j0 - rr, 0), min(j0 + rr + 1, W)); is_ = slice(max(i0 - rr, 0), min(i0 + rr + 1, HH))
            near = np.hypot(X[is_, js] - x, Z[is_, js] - z) <= within
            sub = cap[is_, js]; sub[near] = np.minimum(sub[near], yy + above)
    return cap


def bay_cap(bay, ring_points, h, X, Z):
    """cone-capped recess: inside the ring and within `reach` of `at`, height <= foot_y + shelf_rise + slope * max(0, d - flat_r)."""
    dx = X - bay['at'][0]; dz = Z - bay['at'][1]; inring = B.poly_mask(ring_points, X, Z); k = float(bay.get('rim_stretch', 1.0))
    if k != 1.0 and 'outward' in bay:      # elliptical cone: steeper ALONG the rim (keeps the 8-24 m band inside the window), `slope` into the mesa (walk-up ramp)
        ox, oz = bay['outward']; v = dx * ox + dz * oz; u = -dx * oz + dz * ox; dB = np.hypot(u * k, v)
    else:
        dB = np.hypot(dx, dz)
    yf = float(bay['foot'][2]) if len(bay['foot']) > 2 else float(sample(h, *bay['foot'][:2]))
    cap = yf + bay['shelf_rise'] + bay['slope'] * np.maximum(0, dB - bay['flat_r'])
    return inring & (np.hypot(dx, dz) <= bay['reach'] * max(1.0, k)), cap, yf


def bench_target(bench, field, X, Z):
    """cells within width/2 of the line + their target height = the graded line profile (module steps, |grade| <= max_grade)."""
    line = np.asarray(bench['line'], float); mod = float(bench.get('module_m', 7.815))
    Q, s = resample(line, mod); y = sample(field, Q[:, 0], Q[:, 1]); g = float(bench.get('max_grade', 0.45)) * (s[1] - s[0]); yb = y.copy()
    for k in range(1, len(yb)): yb[k] = min(yb[k], yb[k - 1] + g)
    for k in range(len(yb) - 2, -1, -1): yb[k] = min(yb[k], yb[k + 1] + g)
    D, _, S = B.signed_distance(line, X, Z, 12); m = D <= float(bench.get('width_m', 8.0)) / 2
    tgt = np.zeros((HH, W)); tgt[m] = np.interp(np.nan_to_num(S[m]), s, yb)
    return m, tgt


# ------------------------------------------------------------------ run
def run(ops, stage, base_path=None, log=print):
    """-> (new float64 [HH, W], owner int16, report dict). ops = parsed ops dict."""
    t0 = time.time(); legacy = 'seed' not in ops
    stages = ops.get('stages', STAGES_DEFAULT); ops_seed = ops.get('seed')
    if stage not in stages: raise SystemExit(f'REFUSED: stage {stage!r} is not in the ops stages {stages}')
    base_path = Path(base_path) if base_path else (B.ROOT / ops['inputs']['base_height'] if 'inputs' in ops else B.BASE_HEIGHT)
    base_sha = B.sha256(base_path)
    if 'inputs' in ops and ops['inputs'].get('base_sha256') and ops['inputs']['base_sha256'] != base_sha:
        raise SystemExit(f"REFUSED: base height sha {base_sha} != ops.inputs.base_sha256 {ops['inputs']['base_sha256']}")
    h = B.load_height(base_path); X, Z = B.grid_xz(); lat = B.lattice(log=log)
    mk = ops.get('masks', {}); margin = float(mk.get('route_margin_m', 8.0))
    rm = route_mask(lat['routes'], margin, X, Z); wt = B.wet(); pm = B.protected()
    hard = lat['water4'] | wt | pm | rm
    protect, _ = B.seal('closed')
    CUT = np.full(h.shape, np.inf); RAISE = np.full(h.shape, -np.inf); owner = np.full(h.shape, -1, np.int16)
    refs = {}; applied = []; closes = {}
    for k, d in enumerate(ops['segments']):
        if d.get('skip') or d.get('reference_only'): continue
        if stage_rank(stages, seg_stage(d)) > stage_rank(stages, stage): continue
        d = seg_for_stage(d, stages, stage); seg = Seg(d)
        (i0, i1, j0, j1), rt, ct, rf = apply_segment(seg, h, X, Z, hard, protect, ops_seed, stages, stage)
        if 'back_close' in rf: closes[seg.id] = rf.pop('back_close')
        refs[seg.id] = rf; applied.append(seg.id)
        sub = RAISE[i0:i1 + 1, j0:j1 + 1]; m = rt > sub; sub[m] = rt[m]
        o = owner[i0:i1 + 1, j0:j1 + 1]; o[m & (rt > h[i0:i1 + 1, j0:j1 + 1] + 0.05)] = k
        sub = CUT[i0:i1 + 1, j0:j1 + 1]; m = ct < sub; sub[m] = ct[m]
        o[(ct < h[i0:i1 + 1, j0:j1 + 1] - 0.05)] = k
        log(f'  {seg.id:8s} {time.time() - t0:5.1f} s')
    rep = dict(stage=stage, segments_applied=applied)
    if closes: rep['back_close'] = closes
    # road press: clamp the raise target near routes (corridor cells themselves are hard masks)
    rp = ops.get('road_press')
    if rp and stage_rank(stages, rp.get('stage', stages[0])) <= stage_rank(stages, stage):
        cap = road_press_cap(lat['routes'], rp, h, X, Z)
        would = (RAISE > h + 0.05) & ~hard; pressed = would & (RAISE > cap)
        rep['road_press'] = dict(cells_pressed=int(pressed.sum()), max_press_m=round(float((RAISE - np.maximum(cap, h))[pressed].max()), 1) if pressed.any() else 0.0,
                                 clusters=B.clusters(pressed, 80, 12), within_m=rp['within_m'], above_route_m=rp['above_route_m'])
        RAISE = np.where(pressed, np.maximum(cap, h), RAISE)
    new = np.minimum(h, CUT); up = RAISE > h + 0.05; new[up] = RAISE[up]      # a raise wins only above the ORIGINAL ground
    new[hard] = h[hard]
    # bays (cone-capped recess in a ring mesa)
    segs = {d['id']: d for d in ops['segments']}
    rep['bays'] = {}
    for bay in ops.get('bays', []):
        if stage_rank(stages, bay['stage']) > stage_rank(stages, stage): continue
        m, cap, yf = bay_cap(bay, segs[bay['segment']]['points'], h, X, Z)
        m = m & (new > h + 0.05); before = new[m].copy(); new[m] = np.minimum(new[m], np.maximum(h[m], cap[m]))
        rep['bays'][bay['id']] = dict(cells_lowered=int((before - new[m] > 0.05).sum()), foot_y=round(yf, 2), shelf_y=round(yf + bay['shelf_rise'], 2))
    # benches (wall foundation): only where no cliff op raised, outside the hard masks
    raised = (new - h) > 0.05; rep['benches'] = {}; bench_mask = np.zeros(h.shape, bool)
    for bn in ops.get('benches', []):
        if stage_rank(stages, bn['stage']) > stage_rank(stages, stage): continue
        m, tgt = bench_target(bn, new, X, Z); dlt = np.where(m, tgt - new, 0.0)
        okb = m & ~hard & ~raised; chg = okb & (np.abs(dlt) > 0.05)
        new[chg] = tgt[chg]; bench_mask |= chg; owner[chg] = -2
        rep['benches'][bn['id']] = dict(band_cells=int(m.sum()), changed_cells=int(chg.sum()), cut_max_m=round(float(-dlt[chg].min()), 1) if chg.any() else 0.0,
                                        fill_max_m=round(float(dlt[chg].max()), 1) if chg.any() else 0.0,
                                        masked_out=dict(route_corridor=int((m & rm).sum()), protected=int((m & pm).sum()), wet=int((m & (wt | lat['water4'])).sum()), already_raised_by_cliff=int((m & raised).sum())))
    new[hard] = h[hard]
    ch = np.abs(new - h) > 0.05
    rep.update(base=B.rel(base_path), base_sha256=base_sha, legacy_v4_semantics=legacy, seed=ops_seed, changed_cells=int(ch.sum()), raised_ha=round(float(((new - h) > 0.05).sum() * 16 / 1e4), 2),
               cut_ha=round(float(((new - h) < -0.05).sum() * 16 / 1e4), 2), max_fill_m=round(float((new - h).max()), 1), max_cut_m=round(float((h - new).max()), 1),
               hard_mask_cells_changed=int((ch & hard).sum()))      # no wall-clock value: report.json is byte-stable for a given base + ops
    rep['_refs'] = refs; rep['_masks'] = dict(route=rm, hard=hard, bench=bench_mask)
    return new, owner, rep


def legacy_bay_v4(new, h, ops):
    """the prototype's bay post-process (plan/tools/step2.py), kept only for --selftest."""
    X, Z = B.grid_xz(); BAY = dict(at=[2730.0, 2650.0], foot=[2744.0, 2650.0], shelf_rise=20.0, flat_r=10.0, slope=0.40, reach=70.0)
    ring = [s for s in ops['segments'] if s['id'] == 'P1_M1'][0]['points']
    dB = np.hypot(X - BAY['at'][0], Z - BAY['at'][1]); inring = B.poly_mask(ring, X, Z); yf = float(sample(h, *BAY['foot']))
    cap = yf + BAY['shelf_rise'] + BAY['slope'] * np.maximum(0, dB - BAY['flat_r'])
    nh = new.astype(np.float32).astype(np.float64); m = inring & (dB <= BAY['reach']) & (nh > h + 0.05)
    nh[m] = np.minimum(nh[m], np.maximum(h[m], cap[m])); return nh


def selftest():
    ops_p = B.PL / 'ops308_v4.json'; proto = B.PL / 'Stage/height_p1a_PROTOTYPE.bytes'
    ops = json.loads(ops_p.read_text(encoding='utf-8')); ops.pop('seed', None)
    new, owner, rep = run(ops, '1a', B.BASE_HEIGHT); h = B.load_height()
    out = legacy_bay_v4(new, h, ops).astype('<f4'); ref = np.fromfile(proto, '<f4').reshape(HH, W)
    dmax = float(np.abs(out - ref).max()); same = hashlib.sha256(out.tobytes()).hexdigest() == B.sha256(proto)
    print(f'selftest: ops308_v4 (legacy semantics) vs {B.rel(proto)}: max |d| = {dmax:.6f} m, byte-identical = {same}')
    return 0 if same else 1


def main():
    B.utf8()
    ap = argparse.ArgumentParser(description='scarp308: ops -> stage height field (from the base height)')
    ap.add_argument('ops', nargs='?'); ap.add_argument('--stage', default='1a'); ap.add_argument('--out'); ap.add_argument('--base')
    ap.add_argument('--selftest', action='store_true'); ap.add_argument('--quiet', action='store_true')
    ap.add_argument('--replace', action='store_true', help='allow the default stage file to be replaced by a DIFFERENT ops version')
    a = ap.parse_args()
    if a.selftest: return selftest()
    if not a.ops: ap.error('ops file required')
    ops_p = Path(a.ops); ops = json.loads(ops_p.read_text(encoding='utf-8'))
    out = Path(a.out) if a.out else B.PL / f'Stage/height_p{a.stage}.bytes'; B.guard_out(out)
    prev = out.with_suffix('.report.json')
    if not a.out and not a.replace and out.exists() and prev.exists():      # the delivered stage file: same ops version re-runs in place, another version needs --replace
        was = json.loads(prev.read_text(encoding='utf-8')).get('ops_version')
        if was != ops.get('version'):
            print(f"REFUSED: {B.rel(out)} was written from ops {was}; this run is ops {ops.get('version')}. Pass --out <path> to write elsewhere or --replace to overwrite the stage file."); return 2
    new, owner, rep = run(ops, a.stage, a.base, log=(lambda *x: None) if a.quiet else print)
    sha = B.save_height(out, new); stem = out.with_suffix('')
    B.write_text(str(out) + '.sha256', f'{sha}  {out.name}\n')
    try:
        out.resolve().relative_to(B.PL.resolve()); work = B.WORK
    except ValueError:
        work = out.parent                                                # v6: a scratch --out keeps its side files beside it
    np.save(work / f'{out.stem}_owner.npy', owner)
    refs = rep.pop('_refs'); rep.pop('_masks')
    rep.update(ops=B.rel(ops_p), ops_sha256=B.sha256(ops_p), ops_version=ops.get('version'), out=B.rel(out), out_sha256=sha, format='float32 LE, 1501 rows (z/4) x 1001 cols (x/4), row-major, no header; see plan/Stage/FORMAT.md',
               owner=B.rel(work / f'{out.stem}_owner.npy'), owner_note='int16 index into ops.segments (-1 = untouched, -2 = bench)')
    B.write_json(str(stem) + '.report.json', rep)
    B.write_json(work / f'{out.stem}_refs.json', {k: {kk: [round(float(x), 2) for x in vv] for kk, vv in v.items()} for k, v in refs.items()}, indent=None)
    print(f"scarp308 {ops.get('version')} stage {a.stage}: {rep['changed_cells']} cells, raised {rep['raised_ha']} ha, cut {rep['cut_ha']} ha, max fill {rep['max_fill_m']} m, "
          f"hard-mask cells changed {rep['hard_mask_cells_changed']} -> {B.rel(out)} sha256 {sha}")
    return 0


if __name__ == '__main__':
    sys.exit(main())
