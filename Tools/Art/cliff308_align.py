"""#308 cliff boundary - `cliff308_align`: re-align a cliff stretch to the real ground instead of the realm polygons, then
measure it. SPEC-WORLD-CLIFF-BOUNDARY-308 design 1-3 / 4 (D308-9c Q2 = B: 금표 서릉 and 적로 북단애 서 pulled into phase 1 AFTER this pass).

  python Tools/Art/cliff308_align.py <align308.json> --ops <ops base json> [--only <id>] [--out <result json>]

Rule: the cliff line = the Seal308-proven EA rim (forest runs) or the plan reference line, moved across itself to the natural
break of the ridge - never onto EA-reachable ground, protected / wet cells or a route corridor.
Per stretch (all numbers come from the align file):
  1. seed line  = `points` or the listed forest `runs` chained, smoothed, stations every `station_m`
  2. candidates = offsets lo..hi across the seed (+ = upper side). cost = - w_rise * natural rise ahead + w_fill * fill needed
                  + w_ea * EA-reach cells that would be raised + w_mask * hard-mask / road-press cells + w_far * |offset|
  3. dynamic programming picks one offset per station (|step| <= max_step_m, w_smooth * |step|) -> line -> simplified control points
  4. the candidate segment is applied WITH the base ops by scarp308 and measured: face heights (AC definition), masks, EA reach
     delta and items (closure as built), landmark sightlines from the EA path, tiles, straightness, road-press cells
  5. verdict against `accept`; a failing stretch is returned with reference_only = true and the reasons (it is NOT built)
Output: plan/align308_result.json (segments ready for ops308_v5.json + measurements + verdicts).
"""
import argparse, copy, json, math, sys
from pathlib import Path
import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
import cliff308_base as B                                                # noqa: E402
import scarp308 as SC                                                    # noqa: E402
import cliff308_measure as M                                             # noqa: E402
import cliff308_closure as CL                                            # noqa: E402
from cliff308_base import HH, W, sample                                  # noqa: E402


def chain_runs(runs, ids):
    """forest runs chained into one polyline (each run flipped if needed to continue from the previous end)."""
    out = None
    for rid in ids:
        rev = rid.endswith('!'); P = np.asarray(runs[rid.rstrip('!')]['points'], float)
        if rev: P = P[::-1]
        if out is None: out = P; continue
        if np.hypot(*(out[-1] - P[-1])) < np.hypot(*(out[-1] - P[0])): P = P[::-1]
        out = np.vstack([out, P[1:] if np.hypot(*(out[-1] - P[0])) < 1.0 else P])
    return out


def smooth_line(P, win_m, step=2.0):
    Q, _ = B.resample(P, step); win = int(win_m / step) | 1
    if win > 1 and len(Q) > win:
        k = np.ones(win) / win; pad = win // 2
        Qs = np.stack([np.convolve(np.pad(Q[:, c], (pad, pad), mode='edge'), k, mode='valid') for c in range(2)], 1); Qs[0], Qs[-1] = Q[0], Q[-1]; Q = Qs
    return Q


def simplify(P, tol):
    P = np.asarray(P, float)
    if len(P) < 3: return P
    a, b = P[0], P[-1]; ab = b - a; n = np.hypot(*ab)
    d = np.abs((P[:, 0] - a[0]) * ab[1] - (P[:, 1] - a[1]) * ab[0]) / (n + 1e-9); k = int(d.argmax())
    if d[k] <= tol: return np.array([a, b])
    return np.vstack([simplify(P[:k + 1], tol)[:-1], simplify(P[k:], tol)])


def snap(st, h, ctx, hard, press):
    """-> (control points, per-station table). st = stretch dict."""
    c = st['snap']; seed = chain_runs(ctx.runs, st['runs']) if 'runs' in st else np.asarray(st['points'], float)
    clip = st.get('clip', {})      # keep only the part of the seed inside the declared box (a run that continues past a joint)
    keep = np.ones(len(seed), bool)
    for key, col, sgn in (('x_min', 0, 1), ('x_max', 0, -1), ('z_min', 1, 1), ('z_max', 1, -1)):
        if key in clip: keep &= (seed[:, col] - clip[key]) * sgn >= 0
    seed = seed[keep]
    S, s = B.resample(smooth_line(seed, c.get('seed_smooth_m', 40.0)), c.get('station_m', 8.0))
    t = np.gradient(S, axis=0); t /= np.linalg.norm(t, axis=1, keepdims=True) + 1e-9
    nU = np.stack([-t[:, 1], t[:, 0]], 1) * (1.0 if st['upper'] == 'left' else -1.0)
    offs = np.arange(c['offset_m'][0], c['offset_m'][1] + 0.1, c.get('offset_step_m', 4.0)); K, O = len(S), len(offs)
    H_ = float(st['op']['H']); top_w = float(st['op'].get('top_w', 60.0)); cost = np.zeros((K, O)); tab = np.zeros((K, O, 4))
    qs_up = np.arange(0.0, top_w + 0.1, 4.0); qs_far = np.arange(24.0, 40.1, 8.0); qs_rise = np.arange(8.0, 40.1, 8.0)

    def cell(x, z):
        return np.clip((z / 4).round().astype(int), 0, HH - 1), np.clip((x / 4).round().astype(int), 0, W - 1)
    for o_i, o in enumerate(offs):
        P = S + nU * o
        toe = np.stack([sample(h, P[:, 0] - nU[:, 0] * q, P[:, 1] - nU[:, 1] * q) for q in (4.0, 8.0, 12.0)], 0).min(0)
        rise = np.stack([sample(h, P[:, 0] + nU[:, 0] * q, P[:, 1] + nU[:, 1] * q) for q in qs_rise], 0).mean(0) - toe
        far = np.stack([sample(h, P[:, 0] + nU[:, 0] * q, P[:, 1] + nU[:, 1] * q) for q in qs_far], 0).mean(0)
        Pl = np.maximum(far, toe + H_)
        fill = np.stack([np.maximum(0, Pl - sample(h, P[:, 0] + nU[:, 0] * q, P[:, 1] + nU[:, 1] * q)) for q in qs_up], 0).sum(0) * 4.0      # m2 per m of line
        ea = np.zeros(K); msk = np.zeros(K)
        for q in (0.0, 4.0, 8.0, 12.0):
            i, j = cell(P[:, 0] + nU[:, 0] * q, P[:, 1] + nU[:, 1] * q); ea += ctx.rcl[i, j]; msk += hard[i, j] | press[i, j]
        cost[:, o_i] = -c['w_rise'] * rise + c['w_fill'] * fill / 100.0 + c['w_ea'] * ea + c['w_mask'] * msk + c['w_far'] * abs(o)
        tab[:, o_i] = np.stack([rise, fill, ea, msk], 1)
    # dynamic programming over stations
    max_step = int(round(c.get('max_step_m', 8.0) / c.get('offset_step_m', 4.0))); ws = c['w_smooth'] * c.get('offset_step_m', 4.0)
    acc = cost[0].copy(); back = np.zeros((K, O), int)
    for k in range(1, K):
        best = np.full(O, np.inf); arg = np.zeros(O, int)
        for dlt in range(-max_step, max_step + 1):
            src = np.roll(acc, dlt) + ws * abs(dlt)
            if dlt > 0: src[:dlt] = np.inf
            elif dlt < 0: src[dlt:] = np.inf
            m = src < best; best[m] = src[m]; arg[m] = (np.arange(O) - dlt)[m]
        acc = best + cost[k]; back[k] = arg
    path = np.zeros(K, int); path[-1] = int(acc.argmin())
    for k in range(K - 1, 0, -1): path[k - 1] = back[k, path[k]]
    off = offs[path]; line = S + nU * off[:, None]
    ctrl = simplify(smooth_line(line, c.get('line_smooth_m', 24.0)), c.get('simplify_tol_m', 2.0))
    rows = tab[np.arange(K), path]
    info = dict(stations=K, seed_length_m=round(float(s[-1])), offset_m_min_p50_max=[float(off.min()), float(np.median(off)), float(off.max())],
                natural_rise_m_p10_p50=[round(float(np.percentile(rows[:, 0], 10)), 1), round(float(np.median(rows[:, 0])), 1)], fill_m2_per_m_p50=round(float(np.median(rows[:, 1]))),
                ea_cells_under_line=int(rows[:, 2].sum()), mask_cells_under_line=int(rows[:, 3].sum()), control_points=len(ctrl))
    return ctrl, info


def evaluate(st, seg_d, ctx, base_ops, base_state, stage, obs, tree, log=print):
    """apply base ops + the candidate, measure, verdict."""
    new0, own0, cl0, R0, press0 = base_state
    ops = copy.deepcopy(base_ops); ops['segments'] = [d for d in ops['segments'] if d['id'] != seg_d['id']] + [seg_d]
    new, owner, rep = SC.run(ops, stage, log=lambda *a: None); k = len(ops['segments']) - 1
    seg = SC.Seg(seg_d); floor = float(st['accept'].get('face_min_m', 28.0))
    face, stn = M.face_report(seg, new, floor, joint=(new0 - ctx.h) > 3)
    chg = np.abs(new - new0) > 0.05; h = ctx.h
    cl, R = CL.run(ctx, ops, new, stage, log=lambda *a: None)
    s0, b0 = cl['S0_as_built'], cl0['S0_as_built']
    near = B.dilate(chg, 2); items_near = [[it['kind'], it['id']] for it in ctx.items if near[int(round(it['z'] / 4)), int(round(it['x'] / 4))]]
    lm0 = M.landmark_loss(h, new0, obs, tree); lm = M.landmark_loss(h, new, obs, tree)
    hidden = {n: lm[n]['visible_after'] - lm0[n]['visible_after'] for n in lm}
    vis, _ = M.visibility(seg, h, new, obs, M.surf(h, new, tree))
    dev = M.chord_deviation(seg.P)
    pressed = rep.get('road_press', {}).get('cells_pressed', 0) - press0
    m = dict(face=face, changed_ha=round(float(chg.sum() * 16 / 1e4), 2), fill_Mm3=round(float((new - new0)[chg].sum() * 16 / 1e6), 2), max_fill_m=round(float((new - new0).max()), 1),
             hard_mask_cells_changed=rep['hard_mask_cells_changed'], ea_reach_cells_changed=int((chg & ctx.rcl).sum()),
             tiles_added=B.tile_names(sorted(set(B.tiles_of(np.abs(new - h) > 0.05, 8)) - set(B.tiles_of(np.abs(new0 - h) > 0.05, 8)))),
             S0=dict(reach_ha=s0['reach_ha'], delta_vs_without_ha=round(s0['reach_ha'] - b0['reach_ha'], 2), new_ha_vs_without=round(float((R['S0_as_built'] & ~R0['S0_as_built']).sum() * 16 / 1e4), 2),
                     lost_ha_vs_without=round(float((R0['S0_as_built'] & ~R['S0_as_built']).sum() * 16 / 1e4), 2), new_clusters_vs_without=B.clusters(R['S0_as_built'] & ~R0['S0_as_built'], 60, 12),
                     ea_items_missed=s0['ea_items_missed'], late_items_newly_reached=s0['late_items_newly_reached'], world_edge_reach_m=s0['world_edge_reach_m'], cliff_top_reach_ha=s0['cliff_top_reach_ha']),
             S2=dict(items_lost=cl['S2']['items_lost'], cliff_top_reach_ha=cl['S2']['cliff_top_reach_ha'], cliff_top_reach_delta_ha=round(cl['S2']['cliff_top_reach_ha'] - cl0['S2']['cliff_top_reach_ha'], 2)),
             replaced_runs=cl['replaced_runs'], items_within_8m_of_changed_cells=items_near, landmark_views_lost_vs_without=hidden, seen_from_ea_path=vis,
             plan_chord_600m_deviation_min_max_n=list(dev), road_press_cells=int(pressed))
    a = st['accept']; why = []
    if face['face_min_m'] < a.get('face_min_m', 28.0): why.append(f"face min {face['face_min_m']} m < {a.get('face_min_m', 28.0)} m ({face['m_24_to_floor_outside_flanks']} m of line between 24 m and the floor, {face['m_8_24_outside_windows']} m at 8-24 m)")
    if face['face_median_m'] < a.get('face_median_m', 40.0): why.append(f"face median {face['face_median_m']} m < {a.get('face_median_m', 40.0)} m")
    if face['m_lt4'] > a.get('max_m_lt4', 0): why.append(f"{face['m_lt4']} m of line below the D305 4.0 m floor")
    if face['m_3p45_8'] > 0: why.append(f"{face['m_3p45_8']} m of line in the forbidden 3.45-8 m band")
    if m['hard_mask_cells_changed']: why.append(f"{m['hard_mask_cells_changed']} hard-mask cells changed")
    if m['S0']['ea_items_missed']: why.append(f"EA items missed: {m['S0']['ea_items_missed']}")
    if m['S0']['late_items_newly_reached']: why.append(f"late items newly reached: {m['S0']['late_items_newly_reached']}")
    if m['S0']['new_ha_vs_without'] > a.get('max_new_ea_ha', 0.5): why.append(f"EA reach grows {m['S0']['new_ha_vs_without']} ha (> {a.get('max_new_ea_ha', 0.5)}): the cliff does not seal what the replaced runs sealed")
    if m['S0']['lost_ha_vs_without'] > a.get('max_lost_ea_ha', 1.0): why.append(f"EA reach lost {m['S0']['lost_ha_vs_without']} ha (> {a.get('max_lost_ea_ha', 1.0)})")
    if m['S0']['cliff_top_reach_ha'] > 0: why.append(f"cliff top reachable before the gate: {m['S0']['cliff_top_reach_ha']} ha (AC-B23)")
    if m['S2']['items_lost']: why.append(f"S2 items lost: {m['S2']['items_lost']}")
    if items_near: why.append(f'catalogue items within 8 m of changed cells: {items_near}')
    lost = {n: v for n, v in hidden.items() if v < 0}
    if lost: why.append(f'landmark sightlines lost from the EA path (trees on): {lost}')
    if m['road_press_cells'] > a.get('max_road_press_cells', 0): why.append(f"{m['road_press_cells']} cells pressed near a route (a pass gap that is not in the exception list)")
    if a.get('min_chord_dev_m') and dev[0] < a['min_chord_dev_m']: why.append(f"plan line too straight: 600 m chord deviation {dev[0]} m < {a['min_chord_dev_m']} m")
    m['verdict'] = 'MEETS OFFLINE RULES' if not why else 'FAIL'; m['fail_reasons'] = why
    return m, new


def main():
    B.utf8()
    ap = argparse.ArgumentParser(description='cliff308 line re-alignment + measurement')
    ap.add_argument('align'); ap.add_argument('--ops', required=True); ap.add_argument('--only'); ap.add_argument('--out'); ap.add_argument('--stage', default='1a')
    a = ap.parse_args()
    al = json.loads(Path(a.align).read_text(encoding='utf-8')); base_ops = json.loads(Path(a.ops).read_text(encoding='utf-8'))
    ctx = CL.Ctx(); h = ctx.h; X, Z = ctx.X, ctx.Z
    margin = float(base_ops.get('masks', {}).get('route_margin_m', 8.0)); rm = SC.route_mask(ctx.lat['routes'], margin, X, Z)
    hard = ctx.water | B.wet() | B.protected() | rm
    rp = base_ops.get('road_press'); press = np.isfinite(SC.road_press_cap(ctx.lat['routes'], rp, h, X, Z)) if rp else np.zeros(h.shape, bool)
    print(f'base ops {B.rel(a.ops)} ({base_ops.get("version")}); base height sha {B.sha256(B.BASE_HEIGHT)}')
    new0, own0, rep0 = SC.run(base_ops, a.stage, log=lambda *x: None); cl0, R0 = CL.run(ctx, base_ops, new0, a.stage, log=lambda *x: None)
    base_state = (new0, own0, cl0, R0, rep0.get('road_press', {}).get('cells_pressed', 0))
    obs = M.ea_observers(ctx.lat['content'], ctx.rcl); tree = M.tree_layer(B.sheets())
    out = dict(id='align308', version=al.get('version'), align=B.rel(a.align), align_sha256=B.sha256(a.align), base_ops=B.rel(a.ops), base_ops_sha256=B.sha256(a.ops), base_height_sha256=B.sha256(B.BASE_HEIGHT),
               rule=al.get('rule'), stage=a.stage, observers=len(obs), stretches=[])
    for st in al['stretches']:
        if a.only and st['id'] != a.only: continue
        if st.get('snap'): ctrl, info = snap(st, h, ctx, hard, press)
        else: ctrl = np.asarray(st['points'], float); info = dict(note='line given as data (no snapping)')
        seg_d = dict(id=st['id'], name=st.get('name'), ko=st.get('ko'), stage=st.get('stage', a.stage), role=st.get('role', 'INNER-LATER'), border=st.get('border'), upper=st['upper'],
                     points=[[round(float(x), 1), round(float(z), 1)] for x, z in ctrl], **st['op'])
        if st.get('replaces'): seg_d['replaces'] = st['replaces']
        m, _ = evaluate(st, seg_d, ctx, base_ops, base_state, a.stage, obs, tree)
        if m['verdict'] == 'FAIL': seg_d['reference_only'] = True; seg_d['reference_reason'] = m['fail_reasons']
        print(f"{st['id']:8s} {m['verdict']:20s} line {m['face']['op_line_m']} m, face min/median {m['face']['face_min_m']}/{m['face']['face_median_m']} m, changed {m['changed_ha']} ha, "
              f"EA new/lost {m['S0']['new_ha_vs_without']}/{m['S0']['lost_ha_vs_without']} ha, seen {m['seen_from_ea_path']['seen_share']}")
        for w in m['fail_reasons']: print('      -', w)
        out['stretches'].append(dict(id=st['id'], snap=info, segment=seg_d, measured=m, accept=st['accept'], note=st.get('note')))
    outp = Path(a.out) if a.out else B.PL / 'align308_result.json'; B.write_json(outp, out); print('->', B.rel(outp))
    return 0


if __name__ == '__main__':
    sys.exit(main())
