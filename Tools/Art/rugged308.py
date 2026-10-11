"""#308 cliff boundary - `rugged308`: stage t1 -> stage n1. Offline, deterministic.
SPEC-WORLD-CLIFF-BOUNDARY-308 "n1 단계". The one-cell snap faces of scarp308 become multi-cell rock faces with spurs, gullies
and an uneven crest, so the cliff mountains read as the same terrain as the ground around them.

  python Tools/Art/rugged308.py Tools/Unity/Stage308_rugged/rugged308.json            # -> plan/Stage/height_pn1.bytes (+ sha, report)
  python Tools/Art/rugged308.py <cfg.json> --faces                                    # faces only -> height_pn1_faces.bytes (gallery308 route input)
  python Tools/Art/rugged308.py <cfg.json> --out <path>

height(n1) = f(base sha, t1 sha, cfg). Only cells scarp308 raised (t1 > base) are edited, so every hard mask, route corridor
and EA ground cell keeps its t1 value. All randomness = value noise seeded by sha256(f'{seed}:{salt}').
"""
import argparse, hashlib, json, math, sys
from pathlib import Path
import numpy as np
from scipy import ndimage as ndi
from scipy.sparse import coo_matrix
from scipy.sparse.csgraph import connected_components

sys.path.insert(0, str(Path(__file__).resolve().parent))
import cliff308_base as B                                                # noqa: E402
from cliff308_base import HH, W, CELL                                      # noqa: E402


def noise2d(X, Z, wl, seed, salt):
    """value noise in [-1, 1] on a wl-metre lattice (smoothstep)."""
    s = int.from_bytes(hashlib.sha256(f'{seed}:{salt}'.encode('utf-8')).digest()[:4], 'little')
    nx, nz = int(4000 / wl) + 3, int(6000 / wl) + 3; v = np.random.default_rng(s).uniform(-1, 1, (nz, nx))
    u = X / wl; t = Z / wl; j = np.floor(u).astype(int); i = np.floor(t).astype(int); fu = u - j; ft = t - i
    fu = fu * fu * (3 - 2 * fu); ft = ft * ft * (3 - 2 * ft)
    return v[i, j] * (1 - fu) * (1 - ft) + v[i, j + 1] * fu * (1 - ft) + v[i + 1, j] * (1 - fu) * ft + v[i + 1, j + 1] * fu * ft


def walk_reach(f, seed, limit=45.0):
    """nodes a capsule can walk to from `seed`: two nodes are linked when a tile-mesh triangle holding both is under the slope
    limit (each 4 m quad = triangles (00, 10, 01) and (11, 01, 10), as the tiles are built)."""
    n, m = f.shape; idx = np.arange(n * m).reshape(n, m); h00 = f[:-1, :-1]; h10 = f[:-1, 1:]; h01 = f[1:, :-1]; h11 = f[1:, 1:]
    wa = np.hypot(h10 - h00, h01 - h00) / CELL < math.tan(math.radians(limit)); wb = np.hypot(h11 - h01, h11 - h10) / CELL < math.tan(math.radians(limit))
    i00 = idx[:-1, :-1]; i10 = idx[:-1, 1:]; i01 = idx[1:, :-1]; i11 = idx[1:, 1:]
    r = np.concatenate([i00[wa], i00[wa], i10[wa], i11[wb], i11[wb], i10[wb]]); c = np.concatenate([i10[wa], i01[wa], i01[wa], i01[wb], i10[wb], i01[wb]])
    _, lab = connected_components(coo_matrix((np.ones(len(r), bool), (r, c)), shape=(n * m, n * m)), directed=False)
    return (lab == lab[seed[0] * m + seed[1]]).reshape(n, m)


def smoothstep(t):
    t = np.clip(t, 0, 1); return t * t * (3 - 2 * t)


def run(cfg, log=print, ledges=True):
    inp = cfg['inputs']; base_p = B.ROOT / inp['base_height']; prev_p = B.ROOT / inp['prev_height']
    for p, key in ((base_p, 'base_sha256'), (prev_p, 'prev_sha256')):
        sha = B.sha256(p)
        if sha != inp[key]: raise SystemExit(f'REFUSED: {B.rel(p)} sha {sha} != cfg.inputs.{key} {inp[key]}')
    h = B.load_height(base_p); t1 = B.load_height(prev_p); X, Z = B.grid_xz(); seed = cfg['seed']
    fp = cfg['footprint']; raised = (t1 - h) > fp['min_fill_m']
    lab, n = ndi.label(raised); size = ndi.sum(raised, lab, np.arange(1, n + 1))
    F = np.isin(lab, 1 + np.flatnonzero(size >= fp['min_cells']))
    # distance to the nearest cell outside the footprint and that cell's height = the foot of the face
    d, idx = ndi.distance_transform_edt(F, sampling=CELL, return_indices=True)
    raw = t1[idx[0], idx[1]]; sg = cfg['foot']['smooth_cells']
    wsum = ndi.gaussian_filter(F.astype(float), sg); foot = np.where(F, np.maximum(raw, ndi.gaussian_filter(np.where(F, raw, 0.0), sg) / np.maximum(wsum, 1e-6)), raw)
    fc = cfg['face']; tan = lambda a: math.tan(math.radians(a))
    nz = fc['spur_amp'] * noise2d(X, Z, fc['spur_wl_m'], seed, 'spur') + fc['rib_amp'] * noise2d(X, Z, fc['rib_wl_m'], seed, 'rib')
    ang = np.clip(fc['deg_base'] + nz * (fc['deg_max'] - fc['deg_min']) / 2, fc['deg_min'], fc['deg_max']); k = np.tan(np.radians(ang))
    # talus apron: the face foot steps back from the op line by 0..talus.max_m (value noise); the strip in front is a debris slope
    ta = cfg['talus']; back = ta['max_m'] * np.clip(0.5 + 0.5 * noise2d(X, Z, ta['wl_m'], seed, 'talus') + ta['bias'], 0, 1) ** ta['power']
    # no apron where the body is thin (a ridge with two faces) or the rim is a crest wall standing over a lower top (back_close):
    # there every metre behind the line is needed to reach the height
    rw = int(round(ta['probe_m'] / CELL))
    relief = ndi.maximum_filter(np.where(F, t1, -np.inf), size=2 * rw + 1) - ndi.minimum_filter(np.where(F, t1, np.inf), size=2 * rw + 1)
    deep = ndi.maximum_filter(d, size=2 * rw + 1)
    back = np.where((relief > ta['crest_wall_relief_m']) | (deep < ta['min_body_m']), 0.0, np.minimum(back, np.maximum(0.0, deep - ta['min_body_m'])))
    back = np.where(np.floor(back / CELL) < 1, 0.0, back)                # under one cell there is no apron: the face starts at the line
    dt = np.minimum(d, back); df = np.maximum(d - back, 0.0); apron = math.tan(math.radians(ta['deg'])) * dt
    g = df - fc['toe_m'] * (1 - np.exp(-df / 4.0))
    # a narrow raised body cannot reach need_m at a lazy angle: steepen it there (never past deg_max)
    r = int(round(fc['need_search_m'] / CELL)); gmax = ndi.maximum_filter(np.where(F, g, 0.0), size=2 * r + 1)
    was = np.where(F, t1 - foot, 0.0); want = np.minimum(fc['need_m'], ndi.maximum_filter(was, size=2 * r + 1))
    k = np.clip(np.maximum(k, want / np.maximum(gmax, 1.0)), None, tan(fc['deg_max']))
    face = foot + apron + k * g
    tp = cfg['top']; ramp = smoothstep((d - tp['ramp_from_m']) / tp['ramp_len_m'])
    ridged = lambda wl, salt: 1 - np.abs(noise2d(X, Z, wl, seed, salt))
    bump = tp['peak_amp_m'] * ridged(tp['peak_wl_m'], 'peak') ** 2 + tp['knob_amp_m'] * ridged(tp['knob_wl_m'], 'knob') - tp['sag_amp_m'] * (0.5 + 0.5 * noise2d(X, Z, tp['sag_wl_m'], seed, 'sag'))
    bump = bump + tp['crag_amp_m'] * np.maximum(0, noise2d(X, Z, tp['crag_wl_m'], seed, 'crag')) * (1 - smoothstep((d - tp['crag_band_m']) / tp['crag_band_m']))
    # gullies: narrow notches cut into the rim so the skyline is not one level line
    notch = tp.get('notch_amp_m', 0.0) * smoothstep((ridged(tp.get('notch_wl_m', 60.0), 'notch') - tp.get('notch_from', 0.8)) / (1 - tp.get('notch_from', 0.8))) * (1 - smoothstep((d - tp.get('notch_band_m', 40.0)) / tp.get('notch_band_m', 40.0)))
    top = np.maximum(t1 + bump * ramp - notch * smoothstep(d / 8.0), np.minimum(t1, foot + tp['keep_m']))
    new = np.where(F, np.maximum(h, np.minimum(top, face)), t1)
    keeps = {}
    for kp in cfg.get('keep', []):
        w = smoothstep((np.hypot(X - kp['at'][0], Z - kp['at'][1]) - kp['radius_m']) / kp['blend_m'])
        keeps[kp['id']] = int((F & (w < 1)).sum()); new = t1 + (new - t1) * w
    # ground the player already walks on top of a cliff body (the 금표 단애산 field) keeps t1, rim included
    wk = cfg['walked']; reach_prev = walk_reach(t1, B.SEED_NODE, wk['slope_deg'])
    held = ndi.binary_dilation(reach_prev & F & ((t1 - raw) > wk['above_foot_m']), iterations=int(wk['margin_cells'])) & F
    new = np.where(F & ~held, new, t1)
    reach_new = walk_reach(new, B.SEED_NODE, wk['slope_deg']); gained = reach_new & ~reach_prev; lost = reach_prev & ~reach_new
    # ---- ledges (stone cliff way): cells within half_width of a centreline take the way height
    faces = new.copy(); hard = B.wet() | B.protected(); led = {}; led_mask = np.zeros(F.shape, bool)
    src = B.ROOT / cfg['ledges_from'] if ledges and cfg.get('ledges_from') else None
    if src is not None:
        if not src.exists(): raise SystemExit(f'REFUSED: {B.rel(src)} is missing (run Tools/Art/gallery308.py route first, or pass --faces)')
        for L in json.loads(src.read_text(encoding='utf-8'))['ledges']:
            P = np.asarray(L['points'], float); sl_ = np.concatenate([[0], np.cumsum(np.hypot(np.diff(P[:, 0]), np.diff(P[:, 1])))])
            D, SG_, S = B.signed_distance(P[:, :2], X, Z, L['half_width_m'] + 12); lr = cfg.get('ledge_rough')
            if lr:      # D308-57: an untidy ledge - the half width wanders, the shelf leans across, the stone is uneven away from the walked core
                hw = L['half_width_m'] * (lr['width'][0] + (lr['width'][1] - lr['width'][0]) * (0.5 + 0.5 * noise2d(X, Z, lr['width_wl_m'], seed, 'lw_' + L['id'])))
                core = D <= lr['core_m']; qs = np.nan_to_num(D * SG_)
                extra = np.where(core, 0.0, lr['tilt'] * noise2d(X, Z, lr['tilt_wl_m'], seed, 'lt_' + L['id']) * qs + lr['rough_m'] * noise2d(X, Z, lr['rough_wl_m'], seed, 'lg_' + L['id']))
            else:
                hw = np.full(D.shape, L['half_width_m']); extra = 0.0
            m = (D <= hw) & ~hard
            tgt = (np.interp(np.nan_to_num(S), sl_, P[:, 2]) + extra)[m]; dlt = tgt - new[m]; new[m] = tgt; led_mask |= m
            led[L['id']] = dict(cells=int(m.sum()), hard_skipped=int(((D <= hw) & hard).sum()), cut_max_m=round(float(-dlt.min()), 1), fill_max_m=round(float(dlt.max()), 1), length_m=round(float(sl_[-1]), 1),
                                half_width_m=[round(float(hw[m].min()), 2), round(float(hw[m].max()), 2)] if m.any() else [])
        reach_led = walk_reach(new, B.SEED_NODE, wk['slope_deg'])
        led['_walk'] = dict(ledge_cells_reached=int((reach_led & led_mask).sum()), ledge_cells=int(led_mask.sum()), gained_off_ledge_cells=int((reach_led & ~reach_prev & ~ndi.binary_dilation(led_mask, iterations=3)).sum()))
    # ---- massif: ridged relief on mountain ground outside the cliff bodies (keep-outs: wet, protected, route corridors, drawn objects, the cliff way)
    mas = {}; mas_mask = np.zeros(F.shape, bool)
    for M in (cfg.get('massif', []) if ledges else []):
        x0, z0, x1, z1 = M['box']; inbox = (X >= x0) & (X <= x1) & (Z >= z0) & (Z <= z1)
        edge_d = np.minimum.reduce([X - x0, x1 - X, Z - z0, z1 - Z]); keep = hard | F | led_mask | ~inbox
        lat = json.loads((B.ROOT / M['routes']).read_text(encoding='utf-8'))
        for r_ in lat['routes']:
            P = np.asarray(r_['pts'], float)
            if P[:, 0].max() < x0 - 40 or P[:, 0].min() > x1 + 40 or P[:, 1].max() < z0 - 40 or P[:, 1].min() > z1 + 40: continue
            keep |= B.stamp(P, r_['width'] / 2 + M['route_margin_m'], X, Z)
        sc = json.loads((B.ROOT / M['scan']).read_text(encoding='utf-8')); occ = np.zeros(sc['cols'] * sc['rows'], bool); occ[sc['occupied']] = True; occ = occ.reshape(sc['rows'], sc['cols'])
        ci = np.clip(((Z - sc['z0']) / sc['cell']).astype(int), 0, sc['rows'] - 1); cj = np.clip(((X - sc['x0']) / sc['cell']).astype(int), 0, sc['cols'] - 1)
        keep |= occ[ci, cj] & (X >= sc['x0']) & (Z >= sc['z0']) & (X < sc['x0'] + sc['cols'] * sc['cell']) & (Z < sc['z0'] + sc['rows'] * sc['cell'])
        if src is not None:
            RT = json.loads(src.read_text(encoding='utf-8')); keep |= B.stamp(np.stack([RT['x'], RT['z']], 1), M['way_margin_m'], X, Z)
        dk = ndi.distance_transform_edt(~keep, sampling=CELL)
        w = smoothstep((dk - M['keep_from_m']) / M['keep_len_m']) * smoothstep(edge_d / M['edge_len_m']) * smoothstep((new - M['elev_from']) / (M['elev_full'] - M['elev_from']))
        rel = M['peak_amp_m'] * ridged(M['peak_wl_m'], 'm_peak') ** 2 + M['spur_amp_m'] * ridged(M['spur_wl_m'], 'm_spur') ** 1.5 + M['crag_amp_m'] * ridged(M['crag_wl_m'], 'm_crag') - M['base_cut_m']
        up = np.maximum(0.0, rel) * w; m = up > 0.05; new = new + np.where(m, up, 0.0); mas_mask |= m
        mas[M['id']] = dict(cells=int(m.sum()), ha=round(float(m.sum() * 16 / 1e4), 2), raise_max_m=round(float(up.max()), 1), raise_mean_m=round(float(up[m].mean()), 1) if m.any() else 0.0, kept_cells_in_box=int((keep & inbox).sum()),
                            slope_over_45_share=dict(before=round(float((B.slope_deg(faces)[m] > 45).mean()), 3), after=round(float((B.slope_deg(new)[m] > 45).mean()), 3)) if m.any() else {})
    if mas:
        reach_m = walk_reach(new, B.SEED_NODE, wk['slope_deg']); out_m = ~ndi.binary_dilation(mas_mask, iterations=2)
        mas['_walk'] = dict(gained_outside_massif=int((reach_m & ~reach_prev & out_m & ~ndi.binary_dilation(led_mask, iterations=3)).sum()), lost_outside_massif=int((reach_prev & ~reach_m & out_m).sum()),
                            lost_inside_massif=int((reach_prev & ~reach_m & ~out_m).sum()), cliff_body_cells_reached=int((reach_m & F & ~held & ((new - raw) > 10)).sum()))
    # ---- path faces: the rock wall beside an old cut cliff path (#297) becomes terrain. Cells on the cliff side of the walk, from
    # offset_m on, rise at the face angle to walk height + H and then run back level until the natural slope takes over.
    pf = {}; pf_mask = np.zeros(F.shape, bool)
    if ledges and cfg.get('pathfaces'):
        import cliffpath297 as CP
        for S_ in cfg['pathfaces']:
            P3, dist = CP.main_path(S_['mountain']); sm = np.arange(S_['s0'], S_['s1'] + 1e-6, 0.5)
            xz = np.stack([np.interp(sm, dist, P3[:, 0]), np.interp(sm, dist, P3[:, 2])], -1); xz = CP.smooth_line(xz, 1.2 / 0.5)
            tt = np.gradient(xz, axis=0); tt /= np.linalg.norm(tt, axis=1, keepdims=True); left = np.stack([-tt[:, 1], tt[:, 0]], -1)
            rise = float(np.mean(B.sample(new, *(xz + left * 18).T) - B.sample(new, *(xz - left * 18).T))); sgn = 1.0 if rise > 0 else -1.0      # +1: the cliff is on the left
            sl_ = np.concatenate([[0], np.cumsum(np.linalg.norm(np.diff(xz, axis=0), axis=1))]); L_ = sl_[-1]
            yw = np.max([B.sample(new, *(xz + left * u).T) for u in (-0.9, 0.0, 0.9)], axis=0); k_ = np.ones(9) / 9; yw = np.convolve(np.pad(yw, 4, mode='edge'), k_, mode='valid')
            D, SG, SS = B.signed_distance(xz, X, Z, S_['reach_m'] + 12); q = D * SG * sgn; sv = np.nan_to_num(SS)
            taper = smoothstep(np.minimum(sv, L_ - sv) / S_['taper_m']); nzc = 0.5 + 0.5 * noise2d(X, Z, S_['crest_wl_m'], seed, 'pf_' + S_['id'])
            H_ = S_['face_m'] * taper * (1 - S_['crest_var'] + S_['crest_var'] * nzc)
            ang = np.clip(S_['deg'] + S_['deg_var'] * noise2d(X, Z, 30.0, seed, 'pfa_' + S_['id']), 58.0, 76.0); kk = np.tan(np.radians(ang))
            ywc = np.interp(sv, sl_, yw); run = H_ / kk
            tgt = ywc + np.minimum(kk * (q - S_['offset_m'] + 1.0), H_) - np.maximum(0.0, q - S_['offset_m'] - run - S_['top_m']) * math.tan(math.radians(S_['back_deg']))
            want = np.isfinite(D) & (q >= S_['offset_m']) & (q <= S_['reach_m']) & (sv > 0.5) & (sv < L_ - 0.5) & (tgt > new + 0.05)
            m = want & ~hard & ~F & ~led_mask; up = np.where(m, tgt - new, 0.0); new = new + up; pf_mask |= m
            pf[S_['id']] = dict(length_m=round(float(L_), 1), cliff_side='left' if sgn > 0 else 'right', cells=int(m.sum()), skipped_hard=int((want & hard).sum()), raise_max_m=round(float(up.max()), 1) if m.any() else 0.0,
                                share_applied=round(float(m.sum() / max(1, want.sum())), 2), walk_y=[round(float(yw.min()), 1), round(float(yw.max()), 1)])
        reach_p = walk_reach(new, B.SEED_NODE, wk['slope_deg'])
        pf['_walk'] = dict(gained_cells=int((reach_p & ~reach_prev & ~ndi.binary_dilation(led_mask | mas_mask, iterations=3)).sum()), lost_on_faces=int((reach_prev & ~reach_p & pf_mask).sum()),
                           lost_elsewhere=int((reach_prev & ~reach_p & ~ndi.binary_dilation(pf_mask | mas_mask, iterations=2)).sum()))
    # ---- report
    ch = np.abs(new - t1) > 0.05; sl = B.slope_deg(faces); onface = F & (faces < top - 0.5) & (d <= 60)
    edge = F & (d <= CELL + 0.01)                                         # first raised ring = one sample per 4 m of face foot
    rr = int(round(48 / CELL)); crest = ndi.maximum_filter(np.where(F, faces, -np.inf), size=2 * rr + 1); fh = (crest - foot)[edge]
    fh_prev = (ndi.maximum_filter(np.where(F, t1, -np.inf), size=2 * rr + 1) - foot)[edge]
    q = lambda a, p: round(float(np.percentile(a, p)), 1)
    rep = dict(stage=cfg['stage'], cfg_version=cfg['version'], base=B.rel(base_p), base_sha256=inp['base_sha256'], prev=B.rel(prev_p), prev_sha256=inp['prev_sha256'],
               footprint_cells=int(F.sum()), footprint_ha=round(float(F.sum() * 16 / 1e4), 2), changed_cells=int(ch.sum()),
               outside_footprint_changed=int((ch & ~F & ~led_mask & ~mas_mask & ~pf_mask).sum()), ledges=led, massif=mas, pathfaces=pf, hard_mask_cells_changed=int((ch & hard).sum()), below_base_cells=int((F & (new < h - 1e-6)).sum()),
               walk=dict(model='triangle slope < limit, from the new-game node', held_cells=int(held.sum()), gained_cells=int(gained.sum()), gained_above_foot_max_m=round(float((new - raw)[gained & F].max()), 1) if (gained & F).any() else 0.0,
                         gained_clusters=B.clusters(gained, 100, 12), lost_cells=int(lost.sum()), lost_clusters=B.clusters(lost, 100, 12)),
               lowered_max_m=round(float((t1 - new).max()), 1), raised_max_m=round(float((new - t1).max()), 1),
               face_height_48m=dict(min=q(fh, 0), p05=q(fh, 5), p50=q(fh, 50), max=q(fh, 100), under_28m_samples=int((fh < 28).sum()), samples=int(edge.sum()),
                                    t1_min=q(fh_prev, 0), t1_under_28m_samples=int((fh_prev < 28).sum())),
               face_slope_deg=dict(p05=q(sl[onface], 5), p50=q(sl[onface], 50), p95=q(sl[onface], 95), walkable_under_45_cells=int((onface & (sl < 45)).sum()), cells=int(onface.sum())),
               apron=dict(deg=ta['deg'], cells=int((F & (d <= back) & (back > 0)).sum()), rise_max_m=round(float(apron[F].max()), 1)), first_ring_rise_m=dict(min=q((faces - foot)[edge], 0), p50=q((faces - foot)[edge], 50)),
               keep_cells=keeps, tiles=B.tile_names(B.tiles_of(ch | ((np.abs(t1 - h) > 0.05)), 8)))
    log(json.dumps({k_: v for k_, v in rep.items() if k_ != 'tiles'}, ensure_ascii=False, indent=1))
    return new, rep


def main():
    B.utf8()
    ap = argparse.ArgumentParser(description='rugged308: stage t1 -> stage n1 (multi-cell natural cliff faces)')
    ap.add_argument('cfg'); ap.add_argument('--out'); ap.add_argument('--quiet', action='store_true')
    ap.add_argument('--faces', action='store_true', help='faces only (no ledges) -> height_p<stage>_faces.bytes: the input of gallery308 route')
    a = ap.parse_args(); cfg_p = Path(a.cfg); cfg = json.loads(cfg_p.read_text(encoding='utf-8'))
    out = Path(a.out) if a.out else B.PL / f"Stage/height_p{cfg['stage']}{'_faces' if a.faces else ''}.bytes"; B.guard_out(out)
    new, rep = run(cfg, log=(lambda *x: None) if a.quiet else print, ledges=not a.faces)
    sha = B.save_height(out, new); B.write_text(str(out) + '.sha256', f'{sha}  {out.name}\n')
    rep.update(cfg=B.rel(cfg_p), cfg_sha256=B.sha256(cfg_p), out=B.rel(out), out_sha256=sha,
               ops=B.rel(cfg_p), ops_sha256=B.sha256(cfg_p), ops_version=cfg["version"], ops_note="stage n1 has no scarp ops file: the ledger's ops sha is this config (the ledge lines come from cfg.ledges_from)")
    B.write_json(str(out.with_suffix('')) + '.report.json', rep)
    print(f"rugged308 {cfg['version']} stage {cfg['stage']}: {rep['changed_cells']} cells -> {B.rel(out)} sha256 {sha}")
    return 0


if __name__ == '__main__':
    sys.exit(main())
