"""#308 cliff boundary - evaluation of a stage height field "as built" + rock-skin placement data + plan maps. Offline.
SPEC-WORLD-CLIFF-BOUNDARY-308 AC-B3 / B7 / B8 / B9 / B12 / B13 / B15 / B19 (offline part only: 4 m lattice, no engine).

  python Tools/Art/cliff308_eval.py <ops.json> --height <height.bytes> --stage 1a [--out plan/eval_v5.json] [--skins plan/skins308.json] [--maps]
  python Tools/Art/cliff308_eval.py plan/ops308_v6.json --height plan/Stage/height_p1b.bytes --stage 1b --out plan/eval_v6.json --prev-height plan/Stage/height_p1a.bytes
  (v6 / stage 1b: + back_faces = the far-side faces of the closed plateaus, + vegetation_from_previous_stage = what `veg` moves on sheets that
   already stand on the previous stage. Without --out the file is plan/eval_v<ops minor>.json; --maps and --skins are for the v5 plan files.)

Reads the height it is given (prints its sha256), never a constant path. Outputs:
  eval json  closure as built (EA reach ha, EA items missed, late items newly reached, world-edge reach m), per-stretch face
             height min / median / max (AC definition: 8 m window), outer crest vs eye level, tiles x<col>_z<row>, forest runs
             replaced, edits + masks, vegetation per sheet, road press, EA rim by cause, first blocker, horizon, sightlines
  skins json per-station skin guides measured on the TRIANGULATED wall (so a skin sits <= 2.5 m in front of the collider),
             panels (instances) and a LOD0 triangle cap per stretch inside the AC-B15 budget
  maps       plan/plan_ea.png, plan/plan_overview.png
"""
import argparse, json, math, sys
from pathlib import Path
import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
import cliff308_base as B                                                # noqa: E402
import scarp308 as SC                                                    # noqa: E402
import cliff308_measure as M                                             # noqa: E402
import cliff308_closure as CL                                            # noqa: E402
from cliff308_base import HH, W, sample, sample_tri                      # noqa: E402

r1 = lambda v: round(float(v), 1)
SKIN_RULES = B.PL / 'skins308_rules.json'      # skin placement rules + triangle budget (data; copied into the skins file under `rules`)


def skin_rules(path=None):
    p = Path(path) if path else SKIN_RULES
    return json.loads(p.read_text(encoding='utf-8'))['rules'], p


def obs_and_trees(ctx):
    return M.ea_observers(ctx.lat['content'], ctx.rcl), M.tree_layer(B.sheets())


# ------------------------------------------------------------------ skins
def wall_contours(new, h, seg, reach_m=9.0):
    """mid-height contour of the as-built wall of one stretch, traced on the tile triangulation: polylines [[x, z, y], ...] ordered
    along the op line. A lattice vertex is 'raised' when new - base > 0.5 m; every triangle with raised AND unraised vertices is a
    wall triangle and contributes the segment between the midpoints of its two mixed edges (= the wall at half height)."""
    x0, z0 = seg.P.min(0) - 16; x1, z1 = seg.P.max(0) + 16
    j0, j1 = max(0, int(x0 // 4)), min(W - 2, int(x1 // 4) + 1); i0, i1 = max(0, int(z0 // 4)), min(HH - 2, int(z1 // 4) + 1)
    g = (new - h) > 0.5; segs = []
    sub = g[i0:i1 + 2, j0:j1 + 2]; mixed = ~((sub[:-1, :-1] == sub[1:, :-1]) & (sub[:-1, :-1] == sub[:-1, 1:]) & (sub[:-1, :-1] == sub[1:, 1:]))
    for qi, qj in np.argwhere(mixed):
        i = i0 + int(qi); j = j0 + int(qj); v00 = (i, j); v10 = (i, j + 1); v01 = (i + 1, j); v11 = (i + 1, j + 1)
        for tri in ((v00, v10, v01), (v10, v01, v11)):
            fl = [bool(g[v]) for v in tri]
            if fl[0] == fl[1] == fl[2]: continue
            pts = []
            for a_, b_ in ((0, 1), (1, 2), (2, 0)):      # key = doubled lattice coords (2 * col sum, 2 * row sum) -> metres = key * 2
                if fl[a_] != fl[b_]: pts.append((tri[a_][1] + tri[b_][1], tri[a_][0] + tri[b_][0], float(new[tri[a_]] + new[tri[b_]]) / 2))
            segs.append(pts)
    keep = []    # the segments of THIS line's face: near the op line, not behind it by more than a cell, not past its ends
    for a_, b_ in segs:
        mx = (a_[0] + b_[0]) * 1.0; mz = (a_[1] + b_[1]) * 1.0
        d = np.hypot(seg.P[:, 0] - mx, seg.P[:, 1] - mz); k = int(d.argmin())
        if d[k] > reach_m: continue
        q = (mx - seg.P[k, 0]) * seg.nU[k, 0] + (mz - seg.P[k, 1]) * seg.nU[k, 1]; al = (mx - seg.P[k, 0]) * seg.t[k, 0] + (mz - seg.P[k, 1]) * seg.t[k, 1]
        if q > 4.0 or (not seg.closed and ((k == 0 and al < -6) or (k == len(seg.P) - 1 and al > 6))): continue
        keep.append((a_, b_))
    adj = {}
    for n_, (a_, b_) in enumerate(keep):
        adj.setdefault(a_[:2], []).append(n_); adj.setdefault(b_[:2], []).append(n_)
    used = [False] * len(keep); lines = []
    for start in range(len(keep)):
        if used[start]: continue
        used[start] = True; a_, b_ = keep[start]; chain = [a_, b_]
        for end in (1, 0):
            while True:
                tip = chain[-1] if end else chain[0]; nxt = [n_ for n_ in adj[tip[:2]] if not used[n_]]
                if not nxt: break
                n_ = nxt[0]; used[n_] = True; p_, q_ = keep[n_]; other = q_ if p_[:2] == tip[:2] else p_
                if end: chain.append(other)
                else: chain.insert(0, other)
        P3 = np.array([[c[0] * 2.0, c[1] * 2.0, c[2]] for c in chain])
        if len(P3) < 3 or np.hypot(*np.diff(P3[:, :2], axis=0).T).sum() < 8.0: continue
        ks = [int(np.hypot(seg.P[:, 0] - x, seg.P[:, 1] - z).argmin()) for x, z, _ in (P3[0], P3[len(P3) // 3], P3[-1])]
        if ks[-1] < ks[0] or (ks[0] == ks[-1] and ks[1] < ks[0]): P3 = P3[::-1]; ks = ks[::-1]
        lines.append((ks[0], P3))
    lines.sort(key=lambda t_: t_[0]); return [P3 for _, P3 in lines]


def skin_piece(P3, new, h, reach_s0, joint, path_pts, P):
    """stations every station_m along one wall contour; outward = toward the unraised (player) side; wall offsets per height level."""
    sa = np.concatenate([[0], np.cumsum(np.hypot(*np.diff(P3[:, :2], axis=0).T))]); L = float(sa[-1]); n = max(2, int(round(L / P['station_m'])) + 1); s = np.linspace(0, L, n)
    Q = np.stack([np.interp(s, sa, P3[:, 0]), np.interp(s, sa, P3[:, 1])], 1)
    t = np.gradient(Q, axis=0); t /= np.linalg.norm(t, axis=1, keepdims=True) + 1e-9; nrm = np.stack([-t[:, 1], t[:, 0]], 1)
    a_ = sample_tri(new, Q[:, 0] + nrm[:, 0] * 2.5, Q[:, 1] + nrm[:, 1] * 2.5) - sample_tri(h, Q[:, 0] + nrm[:, 0] * 2.5, Q[:, 1] + nrm[:, 1] * 2.5)
    b_ = sample_tri(new, Q[:, 0] - nrm[:, 0] * 2.5, Q[:, 1] - nrm[:, 1] * 2.5) - sample_tri(h, Q[:, 0] - nrm[:, 0] * 2.5, Q[:, 1] - nrm[:, 1] * 2.5)
    out = -nrm if float(np.median(a_ - b_)) > 0 else nrm      # the +normal side is the raised side -> outward is the other one (one decision per piece)
    tt = np.arange(-8.0, 8.01, 0.25); F = np.array(P['levels']); stand = P['relief_m'] + P['clearance_m']

    def profile(Qx, ox):
        m = len(Qx); EXT = np.zeros((m, len(F))); toe = np.zeros(m); rim = np.zeros(m); yF = np.zeros(m)
        for q in range(m):
            x = Qx[q, 0] + ox[q, 0] * tt; z = Qx[q, 1] + ox[q, 1] * tt; hn = sample_tri(new, x, z); hb = sample_tri(h, x, z); raised = (hn - hb > 0.5) & (tt <= 6.0)
            if not raised.any(): continue
            base = float(tt[raised].max()); toe[q] = float(np.interp(base + 0.25, tt, hn)); up = (tt <= base) & (tt >= base - 6.5); rim[q] = float(hn[up].max())
            for l_, f in enumerate(F):
                hit = (hn >= toe[q] + f * (rim[q] - toe[q])) & (tt <= base + 0.01); EXT[q, l_] = tt[hit].max() if hit.any() else base - 4.0 * f
            front = (tt >= base - 0.5) & (tt <= min(8.0, base + stand + 2.0)); yF[q] = hb[front].min() - P['bury_m']
        return EXT, toe, rim, yF
    EXT, toe, rim, yF = profile(Q, out); face = rim - toe
    ii = np.clip((Q[:, 1] / 4).round().astype(int), 0, HH - 1); jj = np.clip((Q[:, 0] / 4).round().astype(int), 0, W - 1); rr = int(P['near_reach_m'] / 4)
    near = np.array([reach_s0[max(0, i - rr):i + rr + 1, max(0, j - rr):j + rr + 1].any() for i, j in zip(ii, jj)])
    it = np.clip(((Q[:, 1] + out[:, 1] * 5) / 4).round().astype(int), 0, HH - 1); jt = np.clip(((Q[:, 0] + out[:, 0] * 5) / 4).round().astype(int), 0, W - 1)
    want = near & (face >= P['min_face_m']) & ~B.dilate(joint, 1)[it, jt]
    dpath = np.hypot(Q[:, None, 0] - path_pts[None, :, 0], Q[:, None, 1] - path_pts[None, :, 1]).min(1) if len(path_pts) else np.full(n, 1e9)
    # verification on 0.5 m sub-stations: guide (interpolated from the stations) vs the true wall measured there
    m = max(2, int(L / 0.5) + 1); sv = np.linspace(0, L, m); Qv = np.stack([np.interp(sv, sa, P3[:, 0]), np.interp(sv, sa, P3[:, 1])], 1); ov = np.stack([np.interp(sv, s, out[:, 0]), np.interp(sv, s, out[:, 1])], 1)
    ov /= np.linalg.norm(ov, axis=1, keepdims=True) + 1e-9; EXv, _, _, _ = profile(Qv, ov); wv = np.interp(sv, s, want.astype(float)) >= 0.999
    Gv = np.stack([np.interp(sv, s, EXT[:, l_]) for l_ in range(len(F))], 1)
    tw = (EXv - Gv)[wv][:, 1:]       # true wall minus guide (m, + = wall in front of the guide); the buried foot level is left out
    proud = (stand - tw).ravel() if wv.any() else np.zeros(1); pierce = (tw - P['clearance_m']).ravel() if wv.any() else np.zeros(1)
    return dict(Q=Q, s=s, L=L, out=out, EXT=EXT, toe=toe, rim=rim, yF=yF, face=face, want=want, tier_a=dpath <= P['tier_a_path_m'], proud=proud, pierce=pierce)


def skins(ops, new, h, R, owner, ctx, stage, P):
    st = ops.get('stages', SC.STAGES_DEFAULT); out = dict(stretches=[]); tot_a = tot_b = 0.0; step = P['station_m']
    path = [(p['x'], p['z']) for key in ('MainPath', 'BranchPath') for p in (ctx.lat['content'].get(key) or [])]
    for r in ctx.lat['routes']:
        Qr, _ = B.resample(np.asarray(r['pts'], float), 8.0); path += [(float(x), float(z)) for x, z in Qr]
    path = np.array([p for p in path if 0 <= p[0] < 4000 and 0 <= p[1] < 6000 and R['S0_as_built'][max(0, int(p[1] / 4) - 1):int(p[1] / 4) + 2, max(0, int(p[0] / 4) - 1):int(p[0] / 4) + 2].any()])
    rows = []
    for k, d in enumerate(ops['segments']):
        if d.get('reference_only') or SC.stage_rank(st, SC.seg_stage(d)) > SC.stage_rank(st, stage): continue
        seg = SC.Seg(d); pieces = [skin_piece(P3, new, h, R['S0_as_built'], ((new - h) > 3) & (owner != k), path, P) for P3 in wall_contours(new, h, seg)]; rows.append((d, seg, pieces))
        for g in pieces:
            hgt = np.where(g['want'], g['rim'] + P['over_m'] - g['yF'], 0.0) * step; tot_a += float(hgt[g['tier_a']].sum()); tot_b += float(hgt[~g['tier_a']].sum())
    bud = P['budget']; cap = bud['ac_b15_tris'] - bud['reserved_wall_tris'] - bud['reserved_authored_tris']
    dens_b = min(P['density_b_max'], P['budget_fill'] * cap / max(1.0, P['tier_ratio'] * tot_a + tot_b)); dens_a = min(P['density_a_max'], P['tier_ratio'] * dens_b)
    total = 0; npan = 0
    for d, seg, pieces in rows:
        panels = []; guides = []; proud = []; pierce = []; zig = []
        for pi, g in enumerate(pieces):
            want = g['want']; n = len(want); q = 0
            while q < n:
                if not want[q]: q += 1; continue
                e = q
                while e + 1 < n and want[e + 1]: e += 1
                Ln = float(g['s'][e] - g['s'][q])
                if Ln >= 4.0:
                    cnt = max(1, int(round(Ln / P['panel_m'])))
                    for c in range(cnt):
                        a = q + int(round(c * (e - q) / cnt)); b = q + int(round((c + 1) * (e - q) / cnt)); sl = slice(a, b + 1)
                        hgt = g['rim'][sl] + P['over_m'] - g['yF'][sl]; ln_ = float(g['s'][b] - g['s'][a]); area = float(hgt.mean() * ln_); ta = bool(g['tier_a'][sl].mean() >= 0.5); tris = int(area * (dens_a if ta else dens_b))
                        panels.append(dict(piece=pi, s0=round(float(g['s'][a]) - (P['overlap_m'] if c else 0), 1), s1=round(float(g['s'][b]) + (P['overlap_m'] if c < cnt - 1 else 0), 1), length_m=round(ln_, 1), height_m_mean=r1(hgt.mean()),
                                           height_m_max=r1(hgt.max()), area_m2=round(area), tier='A' if ta else 'B', lod0_tris_cap=tris, lod1_tris_cap=int(tris * 0.3), lod2_tris_cap=int(tris * 0.07),
                                           from_xz=[r1(g['Q'][a, 0]), r1(g['Q'][a, 1])], to_xz=[r1(g['Q'][b, 0]), r1(g['Q'][b, 1])]))
                q = e + 1
            if want.any(): proud.append(g['proud']); pierce.append(g['pierce'])
            if want.sum() > 1: zig.append(np.abs(np.diff(g['EXT'][:, 3]))[want[1:] & want[:-1]])
            guides.append(dict(piece=pi, length_m=r1(g['L']), stations=n, sx=[round(float(v), 2) for v in g['Q'][:, 0]], sz=[round(float(v), 2) for v in g['Q'][:, 1]], ox=[round(float(v), 4) for v in g['out'][:, 0]], oz=[round(float(v), 4) for v in g['out'][:, 1]],
                               skin=[int(v) for v in want], tier_a=[int(v) for v in g['tier_a']], y_foot=[round(float(v), 2) for v in g['yF']], y_toe=[round(float(v), 2) for v in g['toe']], y_top=[round(float(v + P['over_m']), 2) for v in g['rim']],
                               wall_offset_by_level=[[round(float(v), 2) for v in row] for row in g['EXT']]))
        proud = np.concatenate(proud) if proud else np.zeros(1); pierce = np.concatenate(pierce) if pierce else np.zeros(1); zig = np.concatenate(zig) if zig else np.zeros(1)
        tris = sum(p_['lod0_tris_cap'] for p_ in panels); total += tris; npan += len(panels); skin_m = float(sum(p_['length_m'] for p_ in panels)); wall_m = float(sum(g['L'] for g in pieces))
        e = dict(id=d['id'], name_ko=d.get('ko'), op_line_m=round(float(seg.L)), wall_contour_m=round(wall_m), wall_pieces=len(pieces), skin_length_m=round(skin_m), instances=len(panels), lod0_tris=tris, lod1_tris=int(tris * 0.3),
                 area_m2=round(sum(p_['area_m2'] for p_ in panels)), tier_a_m=round(float(sum((g['want'] & g['tier_a']).sum() for g in pieces) * step)), tier_b_m=round(float(sum((g['want'] & ~g['tier_a']).sum() for g in pieces) * step)),
                 no_skin_reason=None if panels else 'no S0-reachable ground within %d m of the face (far view only: bare terrain; skin in the stage that opens it)' % P['near_reach_m'],
                 front_of_collider_m_p50_p95_max=[round(float(np.percentile(proud, 50)), 2), round(float(np.percentile(proud, 95)), 2), round(float(proud.max()), 2)] if panels else None,
                 share_front_gt_2p5m=round(float((proud > P['max_front_m']).mean()), 4) if panels else None,
                 wall_pierces_deepest_joint_share=round(float((pierce > 0).mean()), 4) if panels else None, wall_pierce_max_m=round(float(max(0.0, pierce.max())), 2) if panels else None,
                 guide_step_between_stations_m_p95_max=[round(float(np.percentile(zig, 95)), 2), round(float(zig.max()), 2)], panels=panels, guide=guides)
        out['stretches'].append(e)
    out['totals'] = dict(skin_m=round(sum(e['skin_length_m'] for e in out['stretches'])), instances=npan, lod0_tris=total, lod0_cap=cap, within_budget=bool(total <= cap), density_tris_per_m2=dict(A=round(dens_a, 2), B=round(dens_b, 2)),
                         area_m2=dict(A=round(tot_a), B=round(tot_b)),
                         ac_b15=dict(tris_limit=bud['ac_b15_tris'], skins=total, wall_reserved=bud['reserved_wall_tris'], authored_reserved=bud['reserved_authored_tris'], sum=total + bud['reserved_wall_tris'] + bud['reserved_authored_tris'],
                                     batches_limit=150, batches_skins_one_per_panel=npan, batches_note='one mesh per panel; the wall, gate and authored pieces need their own batches inside the same +150 limit (not counted here)'))
    return out


# ------------------------------------------------------------------ readability
def blocked_dirs(obs, reach, new, h, solid, wb, water):
    raised_ = (new - h) > 3; cnt = dict(cliff=0, wall=0, forest_or_seal=0, water=0, steep_or_other=0, map_edge=0, open_gt1500=0); AZ = np.arange(0, 360, 22.5); s = np.arange(4, 1500, 4.0)
    for ox, oz in obs:
        for az in AZ:
            a_ = math.radians(az); xs = ox + math.sin(a_) * s; zs = oz + math.cos(a_) * s; ii = (zs / 4).round().astype(int); jj = (xs / 4).round().astype(int)
            ok = (ii >= 0) & (ii < HH) & (jj >= 0) & (jj < W); ii = ii[ok]; jj = jj[ok]; out_ = ~reach[ii, jj]
            run = out_[:-2] & out_[1:-1] & out_[2:]; k = np.nonzero(run)[0]
            if not len(k): cnt['map_edge' if len(ii) < len(s) else 'open_gt1500'] += 1; continue
            k = int(k[0]); w_ = slice(k, min(k + 6, len(ii)))
            if wb[ii[w_], jj[w_]].any(): cnt['wall'] += 1
            elif raised_[ii[w_], jj[w_]].any(): cnt['cliff'] += 1
            elif solid[ii[w_], jj[w_]].any(): cnt['forest_or_seal'] += 1
            elif water[ii[w_], jj[w_]].any(): cnt['water'] += 1
            else: cnt['steep_or_other'] += 1
    tot = sum(cnt.values()); return dict(rays=tot, share={k: round(v / tot, 3) for k, v in cnt.items()})


def horizon(obs, new, h, tree, wallband=None):
    F = M.surf(h, new, tree).copy(); kind = np.zeros(h.shape, np.int8); kind[tree] = 3; kind[(new - h) > 3] = 1
    if wallband is not None: F[wallband] = new[wallband] + 6.8; kind[wallband] = 2
    AZ = np.arange(0, 360, 5.0); s = np.arange(18, 3000, 6.0); cnt = np.zeros(5); ang_c = []; span = []; low = []
    for ox, oz in obs:
        oy = float(sample(new, ox, oz)) + 1.7; kk = []; aa = []
        for az in AZ:
            a_ = math.radians(az); xs = ox + math.sin(a_) * s; zs = oz + math.cos(a_) * s; ok = (xs > 0) & (xs < 3999) & (zs > 0) & (zs < 5999)
            if not ok.any(): kk.append(4); aa.append(0); continue
            ii = (zs[ok] / 4).round().astype(int); jj = (xs[ok] / 4).round().astype(int); an = (F[ii, jj] - oy) / s[ok]; m_ = int(an.argmax()); kk.append(int(kind[ii[m_], jj[m_]])); aa.append(math.degrees(math.atan(an[m_])))
        kk = np.array(kk); aa = np.array(aa)
        for q in range(5): cnt[q] += (kk == q).sum()
        cw = (kk == 1) | (kk == 2); ang_c += aa[cw].tolist(); span.append(float(cw.mean())); low.append(int((aa < 3.0).sum()) * 5)
    tot = cnt.sum()
    return dict(skyline_formed_by_share=dict(natural_terrain=round(cnt[0] / tot, 3), cliff=round(cnt[1] / tot, 3), wall=round(cnt[2] / tot, 3), tree_canopy=round(cnt[3] / tot, 3)),
                cliff_or_wall_skyline_angle_deg_p50=r1(np.median(ang_c)) if ang_c else None, observers_with_any_cliff_or_wall_skyline=round(float((np.array(span) > 0).mean()), 3),
                low_horizon_lt3deg_total_azimuth_deg_p50_p90=[int(np.percentile(low, 50)), int(np.percentile(low, 90))])


# ------------------------------------------------------------------ evaluation
def evaluate(ops, new, stage, ctx=None, heavy=True, log=print, prev=None):
    ctx = ctx or CL.Ctx(log=log); h = ctx.h; st = ops.get('stages', SC.STAGES_DEFAULT)
    # owner: recomputed (deterministic) so the eval does not depend on a side file
    chk, owner, rep = SC.run(ops, stage, log=lambda *a: None)
    same = float(np.abs(chk.astype(np.float32) - new.astype(np.float32)).max())
    cl, R = CL.run(ctx, ops, new, stage, log=log)
    diff = new - h; ch = np.abs(diff) > 0.05; sl = B.slope_deg(new); wt = B.wet(); pm = B.protected(); masks = rep['_masks']
    E = dict(stage=stage, height_matches_ops_rerun_max_abs_m=round(same, 6))
    s0 = cl['S0_as_built']
    E['closure_as_built'] = dict(config='closed state, forest runs replaced by cliff removed, seam runs kept, Seal308 wall as is, no long wall',
                                 ea_reach_ha=s0['reach_ha'], ea_reach_base_ha=s0['reach_ha_base'], ea_reach_new_ha=s0['new_ha'], ea_reach_lost_ha=s0['lost_ha'],
                                 ea_items=s0['ea_items'], ea_items_missed=s0['ea_items_missed'], late_items_newly_reached=s0['late_items_newly_reached'], late_items_reached_also_in_base=s0['late_items_reached'],
                                 world_edge_reach_m=s0['world_edge_reach_m'], world_edge_reach_base_m=s0['world_edge_reach_base_m'], cliff_top_reach_ha=s0['cliff_top_reach_ha'],
                                 new_area_clusters_xz_cells=s0['new_area_clusters'], lost_area_clusters_xz_cells=s0['lost_area_clusters'],
                                 after_wall_config=dict(ea_reach_ha=cl['S0_after_wall']['reach_ha'], ea_items_missed=cl['S0_after_wall']['ea_items_missed'], late_items_newly_reached=cl['S0_after_wall']['late_items_newly_reached'],
                                                        world_edge_reach_m=cl['S0_after_wall']['world_edge_reach_m'], new_area_clusters_xz_cells=cl['S0_after_wall']['new_area_clusters'][:12]),
                                 S2=dict(items_reached=cl['S2']['items_reached'], items_reached_base=cl['S2']['items_reached_base'], items_lost=cl['S2']['items_lost'], world_edge_reach_m=cl['S2']['world_edge_reach_m'],
                                         world_edge_reach_base_m=cl['S2']['world_edge_reach_base_m'], cliff_top_reach_ha=cl['S2']['cliff_top_reach_ha']),
                                 S1=cl['S1'], S2P=cl['S2P'], AC=cl['AC'], sensitivity=cl['sensitivity'], replaced_band_cells_kept_for_staying_barriers=cl['replaced_band_cells_kept_for_staying_barriers'])
    E['edits'] = dict(changed_ha=round(float(ch.sum() * 16 / 1e4), 2), raised_ha=round(float((diff > .05).sum() * 16 / 1e4), 2), cut_ha=round(float((diff < -.05).sum() * 16 / 1e4), 2), max_fill_m=r1(diff.max()), max_cut_m=r1(-diff.min()),
                      fill_Mm3=round(float(diff[diff > 0].sum() * 16 / 1e6), 2), protected_cells_changed=int((ch & pm).sum()), wet_or_water_cells_changed=int((ch & (wt | ctx.water)).sum()),
                      route_corridor_cells_changed=int((ch & masks['route']).sum()), hard_mask_cells_changed=int((ch & masks['hard']).sum()), ea_reach_cells_changed=int((ch & ctx.rcl).sum()),
                      ea_reach_cells_raised_max_fill_m=r1(diff[ch & ctx.rcl].max()) if (ch & ctx.rcl).any() else 0.0, road_press=rep.get('road_press'), bays=rep.get('bays'), benches=rep.get('benches'))
    tl = B.tiles_of(ch, 8); E['tiles'] = dict(count=len(tl), list=B.tile_names(tl), by_cell_only=B.tile_names(B.tiles_of(ch, 0)),
                                             key='x<col 0-7>_z<row 0-11>: 500 m tiles, origin (col * 500, row * 500), +8 m editor op-box margin; scene object names (Terrain_*) are confirmed by the editor ledger')
    E['forest_runs_replaced'] = dict(count=len(cl['replaced_runs']), list=cl['replaced_runs'], segments305_version=ctx.runs_version, wall_runs_kept_until_1b=cl['wall_replaced_runs'])
    # built structures under the raise (review 2026-10-04): walls buried by the fill, raised ground inside the capital loop
    names = [d_['id'] for d_ in ops['segments']]; buried = []
    for wid, Pw in B.wall_lines():
        Qw, _ = B.resample(Pw, 2.0); fw = sample(new, Qw[:, 0], Qw[:, 1]) - sample(h, Qw[:, 0], Qw[:, 1]); bw = np.nonzero(fw > 0.5)[0]
        for part in (np.split(bw, np.nonzero(np.diff(bw) > 1)[0] + 1) if len(bw) else []):
            mid = Qw[part[len(part) // 2]]; ow = int(owner[int(round(mid[1] / 4)), int(round(mid[0] / 4))])
            buried.append(dict(wall=wid, from_xz=[r1(Qw[part[0], 0]), r1(Qw[part[0], 1])], to_xz=[r1(Qw[part[-1], 0]), r1(Qw[part[-1], 1])], length_m=int(len(part) * 2), max_fill_m=r1(fw[part].max()), by=names[ow] if ow >= 0 else None))
    cap_loop = [Pw for wid, Pw in B.wall_lines() if wid == 'capital_wall'][0][:-1]; incap = B.poly_mask(cap_loop, ctx.X, ctx.Z) & (diff > 3)
    E['structures_under_raise'] = dict(doc='wall centre lines (capital loop, 철옹, 황경 궁성) under more than 0.5 m of fill, and raised (> 3 m) cells inside the capital loop. Not a hard mask of the Spec: a design decision '
                                           '(let the wall die into the rock, or trim the stretch) to take before the scene work',
                                       walls_buried=buried, walls_buried_m=int(sum(b_['length_m'] for b_ in buried)), raised_cells_inside_capital_loop=int(incap.sum()), raised_ha_inside_capital_loop=round(float(incap.sum() * 16 / 1e4), 2),
                                       raised_inside_capital_by={names[k_]: int((incap & (owner == k_)).sum()) for k_ in sorted(set(owner[incap].tolist())) if k_ >= 0}, clusters_xz_cells=B.clusters(incap, 80, 6),
                                       ok=bool(not buried and not incap.any()))
    # per stretch
    per = {}; tree = None; obs = None
    if heavy: obs, tree = obs_and_trees(ctx); F_tree = M.surf(h, new, tree)
    back_faces = {}
    for k, d in enumerate(ops['segments']):
        if d.get('reference_only') or SC.stage_rank(st, SC.seg_stage(d)) > SC.stage_rank(st, stage): continue
        d = SC.seg_for_stage(d, st, stage); seg = SC.Seg(d); outer = d['mode'] == 'outer'; own = owner == k
        if d.get('back_close'):      # v6: the far side of a closed plateau is a face too (D308-9e)
            stb = M.perimeter_stations(new, own & (diff > 0.05), seg, float(d['back_close'].get('front_keep_m', 24.0)) - 8.0)
            back_faces[d['id']] = dict(M.back_face_report(stb), back_cut_m=d.get('back_cut_m'), crest=rep.get('back_close', {}).get(d['id']),
                                       top_reached_S2_wall_ha=round(float((own & (diff > 3) & R['S2_after_wall']).sum() * 16 / 1e4), 2))
        win = [(b['at'][0], b['at'][1], b.get('window_half_m', 14.0) + b.get('station_tolerance_m', 12.0)) for b in ops.get('bays', []) if b['segment'] == d['id'] and SC.stage_rank(st, b['stage']) <= SC.stage_rank(st, stage)]
        f, stn = M.face_report(seg, new, 28.0, win, joint=((new - h) > 3) & ~own)
        e = dict(name_ko=d.get('ko'), role=d.get('role'), mode=d['mode'], stage=SC.seg_stage(d), raster=d.get('raster', 'ramp'), control_points=len(d['points']), op_line_m=round(float(seg.L)),
                 face_ac_8m_window=dict(min_m=f['face_min_m'], p10_m=f['face_p10_m'], median_m=f['face_median_m'], p90_m=f['face_p90_m'], max_m=f['face_max_m'], stations=f['stations'], joint_stations_excluded=f['joint_stations_excluded'],
                                        joint_face_min_m=f['joint_face_min_m'], joint_runs=f['joint_runs'], all_stations_face_min_m=f['all_stations_face_min_m'],
                                        joint_note='joint = the player side of the station lies inside ANOTHER cliff mass; min / median leave these out. joint_face_min_m is the step from that mass onto this one: harmless only while that top is unreachable (the neighbour top_reached_S0_ha / S2_ha = 0)'),
                 face_plan_window_min_p10_p50_p90_max=f['plan_window_min_p10_p50_p90_max'],
                 grammar=dict(m_lt4=f['m_lt4'], m_3p45_8=f['m_3p45_8'], m_8_24_outside_windows=f['m_8_24_outside_windows'], m_8_24_inside_windows=f['m_8_24_inside_windows'], m_24_to_28_outside_flanks=f['m_24_to_floor_outside_flanks'],
                              m_24_to_28_in_flanks=f['m_24_to_floor_in_flanks'], runs_8_24=f['runs_8_24'], runs_24_to_28=f['runs_24_to_floor'], runs_lt4=f['runs_lt4'], runs_3p45_8=f['runs_3p45_8'],
                              ok=bool(f['m_lt4'] == 0 and f['m_3p45_8'] == 0 and f['m_8_24_outside_windows'] == 0 and f['m_24_to_floor_outside_flanks'] == 0 and (win or f['face_min_m'] >= 28.0) and f['median_excluding_windows_m'] >= 40.0)),
                 edited_ha=round(float((own & ch).sum() * 16 / 1e4), 2), max_fill_m=r1(diff[own].max()) if own.any() else 0.0, fill_Mm3=round(float(diff[own & (diff > 0)].sum() * 16 / 1e6), 2),
                 top_walkable_lt45_ha=round(float((own & (diff > 3) & (sl < 45)).sum() * 16 / 1e4), 2), top_reached_S0_ha=round(float((own & (diff > 3) & R['S0_as_built']).sum() * 16 / 1e4), 2),
                 top_reached_S2_ha=round(float((own & (diff > 3) & R['S2']).sum() * 16 / 1e4), 2), tiles=B.tile_names(B.tiles_of(own & ch, 8)), plan_chord_600m_deviation_min_max_n=list(M.chord_deviation(seg.P)),
                 replaces=d.get('replaces', []))      # d = the segment as it applies at this stage (stage_overrides merged)
        if outer:
            o, crest, cxz = M.outer_rows(seg, new, R['S0_as_built'], R['S2'], own); e['outer_crest'] = o
            e['outer_crest']['rule_ok'] = bool(o['void_check']['S0']['stations_crest_below_eye'] == 0 and o['void_check']['S2']['stations_crest_below_eye'] == 0 and o['stations_flat_top_gt_8m'] == 0 and e['plan_chord_600m_deviation_min_max_n'][0] >= 30.0)
        if heavy:
            e['seen_from_ea_path_bare'], _ = M.visibility(seg, h, new, obs, new); e['seen_from_ea_path_trees'], _ = M.visibility(seg, h, new, obs, F_tree)
        per[d['id']] = e
    E['stretches'] = per
    if back_faces: E['back_faces'] = back_faces
    E['reference_only'] = [dict(id=d['id'], stage=SC.seg_stage(d), reason=d.get('reference_reason')) for d in ops['segments'] if d.get('reference_only')]
    # vegetation per sheet
    veg = {}
    for name, Pp in B.sheets().items():
        i = np.clip((Pp[:, 2] / 4).round().astype(int), 0, HH - 1); j = np.clip((Pp[:, 0] / 4).round().astype(int), 0, W - 1)
        veg[name] = dict(total=len(Pp), remove_on_faces=int((ch[i, j] & (sl[i, j] >= 40)).sum()), reseat_on_lifted=int((ch[i, j] & (sl[i, j] < 40)).sum()))
    E['vegetation'] = veg
    if prev is not None:      # v6: the sheets on disk stand on the PREVIOUS stage (its veg run is applied): what moves between the two fields
        chp = np.abs(new - prev) > 0.05; vp = {}
        for name, Pp in B.sheets().items():
            i = np.clip((Pp[:, 2] / 4).round().astype(int), 0, HH - 1); j = np.clip((Pp[:, 0] / 4).round().astype(int), 0, W - 1); m = chp[i, j]
            dy = (new - prev)[i, j]
            vp[name] = dict(total=len(Pp), on_changed_cells=int(m.sum()), remove_on_faces=int((m & (sl[i, j] >= 40)).sum()), reseat=int((m & (sl[i, j] < 40)).sum()),
                            reseat_down=int((m & (sl[i, j] < 40) & (dy < 0)).sum()), reseat_up=int((m & (sl[i, j] < 40) & (dy > 0)).sum()),
                            reseat_dy_min_max_m=[r1(dy[m & (sl[i, j] < 40)].min()), r1(dy[m & (sl[i, j] < 40)].max())] if (m & (sl[i, j] < 40)).any() else None)
        E['vegetation_from_previous_stage'] = dict(doc='nearest-cell rule of the plan (the editor uses the bilinear field at the placement: Offline/cb308_expect.py gives its exact numbers). '
                                                       'Forest305 is rebuilt by Enclosure305 build-scene, not by veg', changed_cells_between_stages=int(chp.sum()), sheets=vp)
    # routes: road height unchanged, walls near roads
    tr = []
    for r in ctx.lat['routes']:
        Q, _ = B.resample(np.asarray(r['pts'], float), 4.0); ii = np.clip((Q[:, 1] / 4).round().astype(int), 0, HH - 1); jj = np.clip((Q[:, 0] / 4).round().astype(int), 0, W - 1); mx = np.zeros(len(Q))
        for di in range(-5, 6):
            for dj in range(-5, 6):
                if di * di + dj * dj <= 25:
                    a_ = np.clip(ii + di, 0, HH - 1); b_ = np.clip(jj + dj, 0, W - 1); mx = np.maximum(mx, np.where(ch[a_, b_], new[a_, b_] - new[ii, jj], 0))
        dz = np.abs(new[ii, jj] - h[ii, jj])
        if (mx > 2).any() or (dz > .05).any(): tr.append(dict(route=r['id'], wall_gt2m_within_20m_length_m=int((mx > 2).sum() * 4), wall_above_route_max_m=r1(mx.max()), road_dz_max_m=round(float(dz.max()), 2)))
    E['routes'] = dict(routes_with_road_dz_or_wall_gt2m_within_20m=tr, rule='AC-B12: road dz 0, no wall > 2 m within 20 m of a route in stage 1')
    # EA rim by cause + first blocker + horizon + sightlines
    repl, _ = CL.replaced_mask(ctx, ops, stage); nob = np.zeros(h.shape, bool); wband, wrepl, _ = CL.wall_bands(ctx, ops, stage)
    E['ea_rim_by_cause'] = dict(doc='4 m lattice edges between the S0 reach and the first node outside it (wall band > raised cliff > Seal308 solid > water > natural >= 45 deg)',
                                base=M.envelope(h, ctx.rcl, h, ctx.scl, nob, ctx.water), as_built=M.envelope(h, R['S0_as_built'], new, ctx.scl & ~repl, nob, ctx.water),
                                after_wall=M.envelope(h, R['S0_after_wall'], new, (ctx.scl & ~repl & ~wrepl) | wband, wband, ctx.water))
    if heavy:
        E['first_blocker_from_ea_path'] = dict(doc='16 azimuths per EA path observer: walk the S0 reach in a straight line until 12 m of non-reach; what stops the ray', observers=len(obs),
                                               base=blocked_dirs(obs, ctx.rcl, h, h, ctx.scl, nob, ctx.water), as_built=blocked_dirs(obs, R['S0_as_built'], new, h, ctx.scl & ~repl, nob, ctx.water),
                                               after_wall=blocked_dirs(obs, R['S0_after_wall'], new, h, (ctx.scl & ~repl & ~wrepl) | wband, wband, ctx.water))
        E['horizon_from_ea_path'] = dict(doc='72 azimuths per EA path observer, 3 km; which surface forms the skyline; trees = +12 m crown layer (INFERRED)', base_trees=horizon(obs, h, h, tree), as_built_trees=horizon(obs, new, h, tree),
                                         after_wall_trees=horizon(obs, new, h, tree, wband))
        E['landmark_sightlines_trees'] = M.landmark_loss(h, new, obs, tree)
        ox, oz = 3670.0, 3230.0; oy = float(sample(h, ox, oz)) + 1.7; sm = math.degrees(math.atan2(float(sample(h, 3710, 3255)) + 30 - oy, math.hypot(40, 25))); best = -90.0
        for az in np.arange(40, 141, 2.0):
            a_ = math.radians(az); s = np.arange(12, 600, 4.0); xs = ox + np.sin(a_) * s; zs = oz - np.cos(a_) * s; ok = (xs < 4000) & (xs > 0)
            ang = np.degrees(np.arctan2(sample(new, xs[ok], zs[ok]) - oy, s[ok])); msk = diff[np.clip((zs[ok] / 4).round().astype(int), 0, HH - 1), np.clip((xs[ok] / 4).round().astype(int), 0, W - 1)] > 3
            if msk.any(): best = max(best, float(ang[msk].max()))
        E['r5_sinmok_vs_crest'] = dict(at=[ox, oz], sinmok_crown_angle_deg=r1(sm), P1_OEg_crest_angle_deg_max=r1(best), margin_deg=r1(sm - best), rule='margin >= 10 deg', ok=bool(sm - best >= 10.0))
    # AC-B20 (offline part): catalogue items / checkpoints / encounters / path points within 8 m of a changed cell
    nearc = B.dilate(ch, 2); C = ctx.lat['content']
    nc = lambda x, z: bool(nearc[min(HH - 1, max(0, int(round(z / 4)))), min(W - 1, max(0, int(round(x / 4))))])
    act = [[it['kind'], it['id'], round(it['x']), round(it['z'])] for it in ctx.items if nc(it['x'], it['z'])]
    for key in ('Checkpoints', 'Encounters'):
        for p in C.get(key) or []:
            pos = p.get('Position') or p.get('Feet') or p.get('Center')
            if isinstance(pos, dict) and nc(pos['x'], pos['z']): act.append([key, p.get('Id'), round(pos['x']), round(pos['z'])])
    for nm in ('StartFeet', 'InnCheckpointFeet'):
        p = C.get(nm)
        if isinstance(p, dict) and nc(p['x'], p['z']): act.append([nm, nm, round(p['x']), round(p['z'])])
    pathpts = [(p['x'], p['z']) for key in ('MainPath', 'BranchPath') for p in (C.get(key) or [])]
    E['actors_on_changed_cells'] = dict(catalogue_items_points_checkpoints_encounters_within_8m=act, path_points_within_8m=int(sum(nc(x, z) for x, z in pathpts)), path_points=len(pathpts),
                                        not_checked='enemy homes, NPC job points, commission points, NavMesh links, vehicle / summon positions, saves: editor-side check (AC-B20)')
    E['border_face_totals'] = dict(built_line_m=sum(v['op_line_m'] for v in per.values()), by_stretch={k_: v['op_line_m'] for k_, v in per.items()})
    return E, cl, R, owner, ctx


# ------------------------------------------------------------------ maps
def draw_maps(ops, new, h, R, owner, ctx, E, stage, plan_version):
    from PIL import Image, ImageDraw, ImageFont
    F11 = ImageFont.truetype('C:/Windows/Fonts/malgun.ttf', 11); F12 = ImageFont.truetype('C:/Windows/Fonts/malgun.ttf', 13); F15 = ImageFont.truetype('C:/Windows/Fonts/malgunbd.ttf', 16); F20 = ImageFont.truetype('C:/Windows/Fonts/malgunbd.ttf', 22)
    st = ops.get('stages', SC.STAGES_DEFAULT); sl = B.slope_deg(new); d_ = new - h; built = [dd for dd in ops['segments'] if not dd.get('reference_only') and SC.stage_rank(st, SC.seg_stage(dd)) <= SC.stage_rank(st, stage)]
    outer_k = [k for k, dd in enumerate(ops['segments']) if dd['mode'] == 'outer']; is_outer = np.isin(owner, outer_k)
    top = np.zeros(h.shape, bool)
    for k_, v in R.items():
        if k_.startswith('top_'): top |= v
    repl_ids = set(r.split(' ')[0] for r in E['forest_runs_replaced']['list']); wall = ops.get('wall') or {}
    PASS = [('상경 고개 (지금 열림)', 2497, 1899, 'open'), ('고개 검문', 2266, 1889, 'open'), ('관문 (남문 사실)', 1898, 1739, 'fact'), ('P03 적로 고개', 1666, 1862, 'later'), ('P07 철옹 다리목', 1175, 2585, 'later'), ('P08 현강 길목', 2316, 4109, 'later'), ('P09 삼경 고개', 1140, 4184, 'later')]
    PLACES = (('신목', 3710, 3255), ('금표 주막', 3069, 2339), ('폐광', 3300, 1911), ('남문', 2000, 2530), ('도성', 1876, 3318), ('청룡', 3210, 3550), ('마을 역참', 2760, 2114))
    LAB = {'P1_S1': (2800, 1560), 'P1_CW': (1560, 1990), 'P1_M1': (2760, 2700), 'P1_OEg': (3400, 3440), 'P1_R1w': (2010, 2330), 'P1_R1e': (2560, 2400), 'P1_R1n': (2200, 3200), 'P1_S3': (1250, 1650)}

    def hill(f):
        gz, gx = np.gradient(f * 1.5, 4.0); s_ = np.arctan(np.hypot(gx, gz)); asp = np.arctan2(-gx, gz); a = math.radians(315); e = math.radians(40)
        return np.clip(np.sin(e) * np.cos(s_) + np.cos(e) * np.sin(s_) * np.cos(a - asp), 0, 1)
    HS = hill(new)

    def draw(name, x0, z0, x1, z1, sc, title, ea):
        j0, j1 = int(x0 // 4), int(math.ceil(x1 / 4)); i0, i1 = int(z0 // 4), int(math.ceil(z1 / 4)); sub = new[i0:i1 + 1, j0:j1 + 1]; e = np.clip((sub - 30) / 340.0, 0, 1)
        rgb = np.stack([0.96 - 0.10 * e, 0.94 - 0.12 * e, 0.88 - 0.14 * e], -1) * (0.62 + 0.38 * HS[i0:i1 + 1, j0:j1 + 1])[..., None]

        def tint(mask, col, a):
            nonlocal rgb
            rgb = np.where(mask[i0:i1 + 1, j0:j1 + 1][..., None], rgb * (1 - a) + np.array(col) / 255 * a, rgb)
        tint(R['S0_as_built'], (250, 222, 110), .34); tint(ctx.water, (70, 120, 180), .85); chm = np.abs(d_) > 3
        tint(chm & (sl < 40) & ~is_outer, (235, 150, 60), .5); tint(chm & (sl < 40) & is_outer, (110, 105, 105), .6); tint(chm & (sl >= 40) & ~is_outer, (125, 45, 10), .9); tint(chm & (sl >= 40) & is_outer, (20, 18, 18), .92); tint(top, (60, 165, 85), .65)
        img = Image.fromarray((np.clip(rgb, 0, 1) * 255).astype(np.uint8)[::-1]).resize((int((x1 - x0) * sc), int((z1 - z0) * sc)), Image.BILINEAR); dr = ImageDraw.Draw(img); px = lambda x, z: ((x - x0) * sc, (z1 - z) * sc)

        def line(P, col, w, dash=None):
            pts = [px(x, z) for x, z in P]
            if not dash: dr.line(pts, fill=col, width=w); return
            for a, b in zip(pts[:-1], pts[1:]):
                L = math.hypot(b[0] - a[0], b[1] - a[1]); n = max(1, int(L / dash))
                for k in range(0, n, 2): dr.line([(a[0] + (b[0] - a[0]) * k / n, a[1] + (b[1] - a[1]) * k / n), (a[0] + (b[0] - a[0]) * min(1, (k + 1) / n), a[1] + (b[1] - a[1]) * min(1, (k + 1) / n))], fill=col, width=w)

        def label(x, z, s, col, font):
            p = px(x, z); w = dr.textlength(s, font=font); dr.rectangle([p[0] - 3, p[1] - 2, p[0] + w + 3, p[1] + font.size + 4], fill=(255, 255, 255), outline=col); dr.text(p, s, fill=col, font=font)
        step = 500 if (x1 - x0) > 2600 else 200
        for x in range(int(math.ceil(x0 / step)) * step, int(x1) + 1, step): dr.line([px(x, z0), px(x, z1)], fill=(205, 205, 205)); dr.text((px(x, z0)[0] + 2, (z1 - z0) * sc - 14), str(x), fill=(90, 90, 90), font=F11)
        for z in range(int(math.ceil(z0 / step)) * step, int(z1) + 1, step): dr.line([px(x0, z), px(x1, z)], fill=(205, 205, 205)); dr.text((2, px(x0, z)[1] + 2), str(z), fill=(90, 90, 90), font=F11)
        for nm, poly in B.REALMS.items(): line(poly + [poly[0]], (200, 40, 40), 1, 6)
        for r in ctx.lat['routes']: line(r['pts'], (120, 80, 35), 2 if ea else 1)
        for rid, s_ in ctx.runs.items():
            if rid in repl_ids: line(s_['points'], (150, 150, 150), 2, 4)
            else: line(s_['points'], (20, 125, 45), 3 if ea else 2)
        for dd in ops['segments']:
            if dd.get('reference_only'): line(dd['points'], (20, 18, 18) if dd['mode'] == 'outer' else (125, 45, 10), 1, 5)
        for ln in wall.get('lines', []): line(ln['line'], (125, 35, 165), 5 if ea else 4)
        if wall.get('gate'): line(wall['gate'], (125, 35, 165), 5 if ea else 4)
        for lab, x, z, kind in PASS:
            if x0 <= x <= x1 and z0 <= z <= z1:
                p = px(x, z); col = {'open': (255, 255, 255), 'fact': (250, 205, 40), 'later': (90, 150, 240)}[kind]; r_ = 9 if ea else 7
                dr.ellipse([p[0] - r_, p[1] - r_, p[0] + r_, p[1] + r_], fill=col, outline=(0, 0, 0), width=2); label(x + 14 / sc, z + 10 / sc, lab, (0, 0, 0), F12)
        for b in ops.get('bays', []):
            p = px(b['foot'][0], b['foot'][1]); dr.polygon([(p[0], p[1] - 10), (p[0] - 9, p[1] + 7), (p[0] + 9, p[1] + 7)], fill=(40, 150, 70), outline=(0, 60, 20))
            if ea: label(b['foot'][0] + 20, b['foot'][1] - 22, f"국 턱 (1b): 오름 {E['closure_as_built']['S1'][b['id']]['declared']['rise_m']:.1f} m + 일방 낙차 하산", (0, 90, 30), F12)
        for dd in built:
            if dd['id'] in LAB and (x0 <= LAB[dd['id']][0] <= x1 - 100 / sc and z0 <= LAB[dd['id']][1] <= z1):
                f = E['stretches'][dd['id']]['face_ac_8m_window']; label(*LAB[dd['id']], f"{dd.get('ko')} [{SC.seg_stage(dd)}] 면 {f['min_m']:.0f}/{f['median_m']:.0f} m", (0, 0, 0) if dd['mode'] == 'outer' else (110, 30, 0), F15 if ea else F12)
        for nm, (x, z) in {'청림': (3350, 3120), '황경': (1850, 3000), '적로': (2400, 800), '철옹': (520, 2700), '현강': (2000, 5150)}.items():
            if x0 <= x <= x1 and z0 <= z <= z1: dr.text(px(x, z), nm, fill=(170, 30, 30), font=F20)
        for lab, x, z in PLACES:
            if x0 <= x <= x1 and z0 <= z <= z1: p = px(x, z); dr.ellipse([p[0] - 4, p[1] - 4, p[0] + 4, p[1] + 4], fill=(0, 0, 170)); dr.text((p[0] + 8, p[1] - 6), lab, fill=(0, 0, 140), font=F12)
        c = E['closure_as_built']; rim = E['ea_rim_by_cause']['as_built']['by_cause_share']
        L = [('바깥 테두리 연봉 = 영원히 막힘 (뾰족한 마루)', (20, 18, 18)), ('안쪽 경계 단애 = 나중에 열림', (125, 45, 10)), ('절벽 위 고원 (도달 불가 / 후반)', (235, 150, 60)), ('절벽 위 들 UP-1 (1b 국 턱으로)', (60, 165, 85)),
             ('관문 + 장성 선 (1b, Q3 검토 중)', (125, 35, 165)), ('남는 금표 숲 (N6)', (20, 125, 45)), ('절벽으로 바뀌는 숲 (점선)', (150, 150, 150)), ('남문 전 EA 도달 범위 (지은 뒤)', (250, 222, 110)), ('강토 다각형 (이름·하늘 전용, 점선)', (200, 40, 40))]
        bw = 392; bh = 22 * (len(L) + 5) + 14; bx, by = (int((x1 - x0) * sc) - bw - 10, 44) if not ea else (10, 48); dr.rectangle([bx, by, bx + bw, by + bh], fill=(255, 255, 255), outline=(0, 0, 0))
        for k, (t, cc) in enumerate(L): dr.rectangle([bx + 8, by + 8 + 22 * k, bx + 30, by + 22 + 22 * k], fill=cc, outline=(0, 0, 0)); dr.text((bx + 38, by + 5 + 22 * k), t, fill=(0, 0, 0), font=F12)
        yk = by + 8 + 22 * len(L)
        for k, t in enumerate((f"[{stage}] 지은 그대로: EA {c['ea_reach_ha']} ha (기준 {c['ea_reach_base_ha']}), 미도달 {len(c['ea_items_missed'])}, 월드 끝 {c['world_edge_reach_m']} m",
                               f"EA 테두리: 절벽 {rim['cliff'] * 100:.0f}% · 숲 {rim['forest_or_seal'] * 100:.0f}% · 급경사 {rim['steep_natural'] * 100:.0f}% · 물 {rim['water'] * 100:.0f}%",
                               f"타일 {E['tiles']['count']}개 · 숲 런 교체 {E['forest_runs_replaced']['count']} · 올림 {E['edits']['raised_ha']} ha",
                               '점선 갈색/검정 = 참고선(재정렬 전, 짓지 않음)', f'{plan_version} · 오프라인 4 m 격자 · 엔진 실측 0')):
            dr.text((bx + 8, yk + 22 * k), t, fill=(150, 0, 0) if k < 3 else (0, 0, 0), font=F12)
        tw = dr.textlength(title, font=F20); dr.rectangle([4, 4, 20 + tw, 38], fill=(255, 255, 255), outline=(0, 0, 0)); dr.text((10, 8), title, fill=(0, 0, 0), font=F20)
        img.save(B.PL / f'{name}.png'); return img.size
    a = draw('plan_overview', 0, 0, 4000, 6000, 0.42, f'SPEC-WORLD-CLIFF-BOUNDARY-308 — 월드 전체 경계 계획 [{plan_version}, ops v5 1a 지은 그대로]', False)
    b = draw('plan_ea', 1000, 1300, 4000, 3900, 0.70, f'1단계 (EA) — 절벽 산 · 금표 서릉 · 적로 북단애 서 · 관문 선 [{plan_version}, ops v5]', True)
    return dict(plan_overview=list(a), plan_ea=list(b))


def main():
    B.utf8()
    ap = argparse.ArgumentParser(description='cliff308 evaluation + skin placement data + maps')
    ap.add_argument('ops'); ap.add_argument('--height', required=True); ap.add_argument('--stage', default='1a'); ap.add_argument('--out'); ap.add_argument('--skins'); ap.add_argument('--maps', action='store_true')
    ap.add_argument('--fast', action='store_true', help='skip sightline / horizon work'); ap.add_argument('--plan-version', default='308.plan.3')
    ap.add_argument('--skin-rules', help='skin rules data file (default plan/skins308_rules.json)')
    ap.add_argument('--prev-height', help='height of the stage the vegetation sheets stand on now (v6: vegetation_from_previous_stage)')
    a = ap.parse_args()
    ops = json.loads(Path(a.ops).read_text(encoding='utf-8')); hp = Path(a.height); new = B.load_height(hp)
    print(f'height {B.rel(hp)} sha256 {B.sha256(hp)}'); print(f'base   {B.rel(B.BASE_HEIGHT)} sha256 {B.sha256(B.BASE_HEIGHT)}')
    minor = str(ops.get('version', '308.ops.5')).split('.')[-1]
    E, cl, R, owner, ctx = evaluate(ops, new, a.stage, heavy=not a.fast, prev=B.load_height(a.prev_height) if a.prev_height else None)
    head = dict(id='eval308', version='308.eval.' + minor, date=ops.get('date'), status='IMPLEMENTED offline (4 m lattice + tile triangulation); nothing measured in the engine', height=B.rel(hp), height_sha256=B.sha256(hp), base_sha256=B.sha256(B.BASE_HEIGHT),
                ops=B.rel(a.ops), ops_sha256=B.sha256(a.ops), ops_version=ops.get('version'), items_source=ctx.items_source,
                face_definition='AC: largest rise inside any 8 m window across the op line, every 4 m along it, on the triangulated tile surface, searched from 12 m on the player side to 8 m on the upper side; '
                                'stations whose player side is inside another cliff mass (joints) are excluded and counted')
    head.update(E)
    if a.skins or a.maps:
        if a.skins:
            SKIN, rules_path = skin_rules(a.skin_rules); S = skins(ops, new, ctx.h, R, owner, ctx, a.stage, SKIN)
            S = dict(id='skins308', version='308.skins.1', date=ops.get('date'), status='TEST placement data for the `skins:<scene>` editor command (Spec: no colliders on skins, front <= 2.5 m from the terrain face, foot buried 1 m)',
                     height=B.rel(hp), height_sha256=B.sha256(hp), ops=B.rel(a.ops), ops_sha256=B.sha256(a.ops), rules=SKIN, rules_file=B.rel(rules_path), rules_sha256=B.sha256(rules_path),
                     how_to_place='per stretch, per wall piece: guide stations every station_m along the AS-BUILT mid-height contour of the triangulated wall (sx, sz) with the outward unit vector (ox, oz) - a 4 m lattice jog is a corner of this '
                                  'polyline, not a jump in the offsets. wall_offset_by_level[q][l] = distance (m, along outward) of the true wall at height y_toe + levels[l] * (y_top - over_m - y_toe). Skin surface: deepest joint at '
                                  'wall_offset + clearance_m, proudest block at wall_offset + clearance_m + relief_m (2.0 m), interpolated between stations and levels; vertical range y_foot .. y_top. Build only where skin[q] = 1. '
                                  'One mesh per panel (piece, s0..s1 = arclength along that piece), LOD0 triangles <= lod0_tris_cap. No collider.',
                     recompute='the P1_M1 bay window changes in stage 1b (bay recess): re-run this tool with the 1b height before the skins command', **S)
            B.write_json(Path(a.skins), S, indent=None)
            head['skins'] = dict(file=B.rel(a.skins), totals=S['totals'], per_stretch=[{k: v for k, v in e.items() if k not in ('panels', 'guide')} for e in S['stretches']])
            print('skins', json.dumps(S['totals'], ensure_ascii=False, default=B._js))
        if a.maps: head['maps'] = draw_maps(ops, new, ctx.h, R, owner, ctx, E, a.stage, a.plan_version)
    if a.prev_height: head['previous_stage_height'] = B.rel(a.prev_height); head['previous_stage_height_sha256'] = B.sha256(a.prev_height)
    outp = Path(a.out) if a.out else B.PL / f'eval_v{minor}.json'; B.write_json(outp, head)
    c = E['closure_as_built']
    print(f"closure as built: EA {c['ea_reach_ha']} ha (base {c['ea_reach_base_ha']}), EA items missed {len(c['ea_items_missed'])}, late newly reached {len(c['late_items_newly_reached'])}, world edge {c['world_edge_reach_m']} m")
    for k, v in E['stretches'].items(): print(f"  {k:8s} {v['op_line_m']:5d} m face min/median/max {v['face_ac_8m_window']['min_m']}/{v['face_ac_8m_window']['median_m']}/{v['face_ac_8m_window']['max_m']} grammar ok {v['grammar']['ok']} | joint stations {v['face_ac_8m_window']['joint_stations_excluded']} (joint face min {v['face_ac_8m_window']['joint_face_min_m']})")
    su = E['structures_under_raise']; print(f"sensitivity ok {c['sensitivity']['ok']} | structures under the raise: walls buried {su['walls_buried_m']} m {[(b_['wall'], b_['by'], b_['length_m'], b_['max_fill_m']) for b_ in su['walls_buried']]}, raised inside the capital loop {su['raised_ha_inside_capital_loop']} ha, ok {su['ok']}")
    print('tiles', E['tiles']['count'], ' '.join(E['tiles']['list'])); print('->', B.rel(outp))
    return 0


if __name__ == '__main__':
    sys.exit(main())
