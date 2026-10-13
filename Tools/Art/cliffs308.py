"""#308 cliffs - `cliffs308`: stage n6 -> stage n7. Offline, deterministic. SPEC-WORLD-CLIFFS-308.
The n6 cliff bodies are one cross-section swept along a smooth line (an extruded sheet). n7 breaks the plan line with
buttresses and shoulders, softens the knife-line foot with a concave toe, fills the one-vertex teeth of the crest band, adds
ribs to listed natural faces, and builds the climbing cliff ways (stone shelf = terrain, raise only + wood gallery = meshes).

  python Tools/Art/cliffs308.py faces <cfg.json>    # -> plan/Stage/height_p<stage>_faces.bytes (no ways)
  python Tools/Art/cliffs308.py route <cfg.json>    # faces field -> plan/cliffs308_route.json (way centrelines, sections)
  python Tools/Art/cliffs308.py final <cfg.json>    # faces + stone shelves -> plan/Stage/height_p<stage>.bytes (+ sha, report)
  python Tools/Art/cliffs308.py build <cfg.json>    # final field -> Art/World/Compact/Rebuild/CliffWays308/{Meshes/*.json, layout.json}, rocks -> Tools/Unity/Stage308_cliffs/Out/rocks.json
  python Tools/Art/cliffs308.py all <cfg.json>      # the four in order

Every edit is RAISE ONLY against the previous stage (new >= prev everywhere): a seal face can only get thicker. Walkability is
judged by the tile-triangle 45 deg model from the new-game node (rugged308.walk_reach), before and after.
The next stage (n8) takes height_p<this stage>.bytes + its sha as `prev` in the same way.
"""
import hashlib, json, math, sys
from pathlib import Path
import numpy as np
from scipy import ndimage as ndi

sys.path.insert(0, str(Path(__file__).resolve().parent))
import cliff308_base as B                                                # noqa: E402
from cliff308_base import CELL                                             # noqa: E402
from rugged308 import noise2d, walk_reach, smoothstep                      # noqa: E402

OUT = B.RB / 'CliffWays308'


def stage_path(stage, faces=False): return B.PL / f"Stage/height_p{stage}{'_faces' if faces else ''}.bytes"
def route_path(cfg): return B.PL / f"cliffs308_route_{cfg['stage']}.json"


def load_inputs(cfg):
    inp = cfg['inputs']; ps = {}
    for key in ('base', 'prev', 'scarp'):
        p = B.ROOT / inp[key + '_height']; sha = B.sha256(p)
        if sha != inp[key + '_sha256']: raise SystemExit(f'REFUSED: {B.rel(p)} sha {sha} != cfg.inputs.{key}_sha256 {inp[key + "_sha256"]}')
        ps[key] = B.load_height(p)
    return ps['base'], ps['prev'], ps['scarp']


def realm_of(x, z):
    for nm, poly in B.REALMS.items():
        if B.poly_mask(poly, np.array([[float(x)]]), np.array([[float(z)]]))[0, 0]: return nm
    return ''


def keepout(cfg, X, Z):
    """cells nothing may be raised on: wet, protected, route corridors, drawn objects, content coordinates, declared polygons."""
    ko = cfg['keepout']; hard = B.wet() | B.protected(); K = hard.copy(); parts = dict(hard=int(hard.sum()))
    lat = json.loads((B.ROOT / ko['routes']).read_text(encoding='utf-8')); R = np.zeros(K.shape, bool)
    for r_ in lat['routes']: R |= B.stamp(np.asarray(r_['pts'], float), r_['width'] / 2 + ko['route_margin_m'], X, Z)
    parts['routes'] = int(R.sum()); K |= R
    sc = json.loads((B.ROOT / ko['scan']).read_text(encoding='utf-8')); occ = np.zeros(sc['cols'] * sc['rows'], bool); occ[sc['occupied']] = True; occ = occ.reshape(sc['rows'], sc['cols'])
    # the foot boulders of the earlier stage (RuggedRocks308) are in that scan; they are re-seated by this stage, so their cells are freed
    if ko.get('scan_minus_rocks'):
        for r_ in json.loads((B.ROOT / ko['scan_minus_rocks']).read_text(encoding='utf-8'))['rocks']:
            h_ = r_['size'] * 0.75; c0 = int((r_['x'] - h_ - sc['x0']) // sc['cell']); c1 = int((r_['x'] + h_ - sc['x0']) // sc['cell']); r0 = int((r_['z'] - h_ - sc['z0']) // sc['cell']); r1 = int((r_['z'] + h_ - sc['z0']) // sc['cell'])
            occ[max(r0, 0):r1 + 1, max(c0, 0):c1 + 1] = False
    ci = np.clip(((Z - sc['z0']) / sc['cell']).astype(int), 0, sc['rows'] - 1); cj = np.clip(((X - sc['x0']) / sc['cell']).astype(int), 0, sc['cols'] - 1)
    O = occ[ci, cj] & (X >= sc['x0']) & (Z >= sc['z0']) & (X < sc['x0'] + sc['cols'] * sc['cell']) & (Z < sc['z0'] + sc['rows'] * sc['cell'])
    parts['scan'] = int(O.sum()); K |= O
    P = np.zeros(K.shape, bool)
    for x, z, rad, _tag in json.loads((B.ROOT / ko['points']).read_text(encoding='utf-8'))['points']:
        j0, i0 = int(round(x / 4)), int(round(z / 4)); r = int(math.ceil(rad / 4)) + 1; js = slice(max(j0 - r, 0), j0 + r + 1); is_ = slice(max(i0 - r, 0), i0 + r + 1)
        P[is_, js] |= np.hypot(X[is_, js] - x, Z[is_, js] - z) <= rad
    parts['points'] = int(P.sum()); K |= P
    for pg in ko.get('polygons', []):
        m = B.poly_mask(pg['poly'], X, Z); m = ndi.binary_dilation(m, iterations=int(round(pg['margin_m'] / 4))); parts['poly_' + pg['id']] = int(m.sum()); K |= m
    for ln in ko.get('lines', []):
        Rt = json.loads((B.ROOT / ln['route']).read_text(encoding='utf-8')); m = B.stamp(np.stack([Rt['x'], Rt['z']], 1), ln['margin_m'], X, Z); parts['line_' + ln['id']] = int(m.sum()); K |= m
    for kp in ko.get('discs', []):
        K |= np.hypot(X - kp['at'][0], Z - kp['at'][1]) <= kp['radius_m']
    return K, hard, parts


def footprint(cfg, base, scarp):
    fp = cfg['footprint']; raised = (scarp - base) > fp['min_fill_m']; lab, n = ndi.label(raised); size = ndi.sum(raised, lab, np.arange(1, n + 1))
    return np.isin(lab, 1 + np.flatnonzero(size >= fp['min_cells']))


PLAY = B.PL / '_work/reach_p1a_S0_as_built.npy'          # Seal308 as-built reach of the sealed EA play area (4 m lattice, bool)


def _links(f, limit=45.0):
    n, m = f.shape; idx = np.arange(n * m).reshape(n, m); h00 = f[:-1, :-1]; h10 = f[:-1, 1:]; h01 = f[1:, :-1]; h11 = f[1:, 1:]; t = math.tan(math.radians(limit))
    wa = np.hypot(h10 - h00, h01 - h00) / CELL < t; wb = np.hypot(h11 - h01, h11 - h10) / CELL < t
    i00 = idx[:-1, :-1]; i10 = idx[:-1, 1:]; i01 = idx[1:, :-1]; i11 = idx[1:, 1:]
    return np.concatenate([i00[wa], i00[wa], i10[wa], i11[wb], i11[wb], i10[wb]]), np.concatenate([i10[wa], i01[wa], i01[wa], i01[wb], i10[wb], i01[wb]])


def seal_state(f, band, play):
    """what a walker who starts in the sealed play area can do with the cliff bands (tile-triangle 45 deg links):
    R = nodes reached from the play area through play + band nodes only; exits = reached band nodes with a walkable link to ground
    that is neither play area nor band (a way out through or over a cliff)."""
    from scipy.sparse import coo_matrix
    from scipy.sparse.csgraph import connected_components
    r, c = _links(f); N = f.size; dom = (play | band).ravel(); ok = dom[r] & dom[c]
    _, lab = connected_components(coo_matrix((np.ones(int(ok.sum()), bool), (r[ok], c[ok])), shape=(N, N)), directed=False)
    R = (lab == lab[B.SEED_NODE[0] * f.shape[1] + B.SEED_NODE[1]]) & dom      # one walker, from the new-game node: a play cell buried under a buttress is not a start
    bf = band.ravel(); ex = np.zeros(N, bool)
    out_r = R[r] & bf[r] & ~dom[c]; out_c = R[c] & bf[c] & ~dom[r]; ex[r[out_r]] = True; ex[c[out_c]] = True
    return R.reshape(f.shape), ex.reshape(f.shape)


def seal_report(prev, new, band, foot_ref, allow_mask=None, high_m=6.0):
    play = np.load(PLAY); Rp, Ep = seal_state(prev, band, play); Rn, En = seal_state(new, band, play)
    new_exit = En & ~ndi.binary_dilation(Ep, iterations=3)
    hp = Rp & band & ((prev - foot_ref) >= high_m); hn = Rn & band & ((new - foot_ref) >= high_m); new_high = hn & ~ndi.binary_dilation(hp, iterations=2)
    if allow_mask is not None: allowed = int((new_high & allow_mask).sum()); new_high = new_high & ~allow_mask; allowed_exit = int((new_exit & allow_mask).sum()); new_exit = new_exit & ~allow_mask
    else: allowed = 0; allowed_exit = 0
    lost_play = play & Rp & ~Rn
    return dict(model='walker starts in the sealed EA play area (Seal308 as-built reach, plan/_work/reach_p1a_S0_as_built.npy) and may step on play + cliff-band nodes; links = tile triangles under 45 deg',
                play_cells=int(play.sum()), band_cells=int(band.sum()), exits_prev=int(Ep.sum()), exits_new=int(En.sum()), new_exit_cells=int(new_exit.sum()), new_exit_clusters=B.clusters(new_exit, 60, 12),
                on_body_over_6m_prev=int(hp.sum()), on_body_over_6m_new=int(hn.sum()), new_on_body_cells=int(new_high.sum()), new_on_body_clusters=B.clusters(new_high, 60, 12),
                new_on_body_max_m=round(float((new - foot_ref)[new_high].max()), 1) if new_high.any() else 0.0, allowed_on_way_cells=allowed, allowed_exit_cells=allowed_exit,
                play_cells_lost=int(lost_play.sum()), play_lost_clusters=B.clusters(lost_play, 100, 12)), dict(Rp=Rp, Rn=Rn, new_high=new_high, new_exit=new_exit, lost_play=lost_play, play=play)


def faces(cfg, log=print):
    base, prev, scarp = load_inputs(cfg); X, Z = B.grid_xz(); seed = cfg['seed']; F = footprint(cfg, base, scarp)
    K, hard, kparts = keepout(cfg, X, Z); reach_prev = walk_reach(prev, B.SEED_NODE, 45.0)
    n01 = lambda wl, salt: 0.5 + 0.5 * noise2d(X, Z, wl, seed, salt)
    stretch = lambda a, g=2.6: np.clip((a - 0.5) * g + 0.5, 0, 1)             # value noise crowds 0.5: spread it
    ridged = lambda wl, salt: 1 - np.abs(noise2d(X, Z, wl, seed, salt))
    # ---- smooth signed distance to the body outline (+ inside), sub-cell: the staircase of the mask is filtered out
    din, iin = ndi.distance_transform_edt(F, sampling=CELL, return_indices=True); dout, iout = ndi.distance_transform_edt(~F, sampling=CELL, return_indices=True)
    sd = ndi.gaussian_filter(np.where(F, din - 2.0, -(dout - 2.0)), cfg['outline']['sigma_cells'])
    foot_in = prev[iin[0], iin[1]]                                         # inside: height of the nearest ground cell
    near_body = iout                                                       # outside: nearest body cell
    # local crest height above the foot (48 m window), read at the nearest body cell
    rr = int(round(cfg['lobes']['crest_window_m'] / CELL)); crest = ndi.maximum_filter(np.where(F, prev, -np.inf), size=2 * rr + 1)
    crestH = np.where(F, crest - foot_in, 0.0); crestH_out = crestH[near_body[0], near_body[1]]; foot_out = foot_in[near_body[0], near_body[1]]
    # ---- where the plan line may move out: distance to the keep-out, the walked cliff tops are never touched
    dk = ndi.distance_transform_edt(~K, sampling=CELL); ko = cfg['keepout']; allow = smoothstep((dk - ko['from_m']) / ko['len_m'])
    held = ndi.binary_dilation(reach_prev & F & ((prev - foot_in) > 4.0), iterations=4) if cfg['lobes'].get('hold_bare_reach', False) else np.zeros(F.shape, bool)
    # ---- lobes: buttresses (big, sparse) + ribs (small) + the minimum toe everywhere it is allowed
    lb = cfg['lobes']; big = stretch(n01(lb['big_wl_m'], 'lobe')); rib = stretch(n01(lb['rib_wl_m'], 'rib'))
    b_big = lb['big_m'] * smoothstep((big - lb['big_from']) / (1 - lb['big_from']))
    b = np.maximum(b_big + lb['rib_m'] * smoothstep((rib - lb['rib_from']) / (1 - lb['rib_from'])), lb['min_m'])
    # a buttress is read on the outline: every cell takes the reach of its nearest outline point (no islands, no thin wedges), and that
    # reach is never more than the outline point's clearance to the keep-out
    onb = lambda a_: np.where(F, a_[iin[0], iin[1]], a_[iout[0], iout[1]])
    clear = np.maximum(0.0, onb(dk) - ko['from_m'] - CELL); b = np.minimum(onb(b), clear); b_big = np.minimum(onb(b_big), clear)
    # only in front of a face that is steep already: raising the foot of a lazy back slope would turn it into a ramp
    sl0 = B.slope_deg(prev); wF = ndi.gaussian_filter(F.astype(float), lb['steep_sigma_cells']); steep_share = ndi.gaussian_filter((F & (sl0 > lb['steep_deg'])).astype(float), lb['steep_sigma_cells']) / np.maximum(wF, 1e-6)
    front = smoothstep((onb(np.where(F, steep_share, 0.0) if False else steep_share) - lb['steep_from']) / (lb['steep_full'] - lb['steep_from'])); b = b * front; b_big = b_big * front
    # the thicker body = {sd + b > 0}; its own distance field is taken again so the face is as steep on the flanks of a buttress as on its front
    Fn0 = ((sd + b) > 0) | F; dn_i = ndi.distance_transform_edt(Fn0, sampling=CELL); dn_o = ndi.distance_transform_edt(~Fn0, sampling=CELL)
    s = ndi.gaussian_filter(np.where(Fn0, dn_i - 2.0, -(dn_o - 2.0)), cfg['outline']['sigma_out_cells'])
    ang = np.clip(lb['deg_base'] + lb['deg_var'] * noise2d(X, Z, lb['deg_wl_m'], seed, 'ang'), lb['deg_min'], lb['deg_max']); k = np.tan(np.radians(ang))
    toe = lb['toe_min_m'] + (lb['toe_max_m'] - lb['toe_min_m']) * stretch(n01(lb['toe_wl_m'], 'toe'))
    sp = np.maximum(s, 0.0); P = np.where(sp < toe, k * sp * sp / (2 * toe), k * (sp - toe / 2))      # concave toe of length `toe`, then the face angle: walkable only for the first toe / k metres (under ~1.5 m high)
    Hc = np.where(F, crestH, crestH_out); ground = np.where(F, foot_in, prev); ref = np.where(F, foot_in, foot_out)
    # never above the crest that stands within near_m: a thin wall keeps its own skyline
    rn = int(round(lb['near_m'] / CELL)); near_crest = ndi.maximum_filter(np.where(F, prev, -np.inf), size=2 * rn + 1); near_crest = np.where(np.isfinite(near_crest), near_crest, ref + Hc)
    cap = near_crest - lb['cap_below_crest_m']                              # no buttress top closer than this under the rim: the rim line, the skyline and every fall edge stay as they are
    # shoulders: each buttress (one connected patch of b_big) takes ONE height, a share of the local crest height; a patch is a flat
    # terrace with a steep face on every side (a height that drifts along the wall would be a ramp to the crest)
    lab, nl = ndi.label((b_big > lb['shoulder_from_m']) & (sd > -(lb['big_m'] + 12.0)) & (sd < lb['big_m'])); rng = np.random.default_rng(int.from_bytes(hashlib.sha256(f'{seed}:shoulder'.encode()).digest()[:4], 'little'))
    shoulders = 0; full = 0; ref_s = ndi.gaussian_filter(ref, 2.0)
    if nl:
        share = rng.uniform(lb['cap_min'], lb['cap_max'], nl + 1); idx = np.arange(1, nl + 1)
        hc_l = ndi.median(Hc, lab, idx); ref_l = ndi.median(ref, lab, idx)
        for n_ in idx:
            if share[n_] > lb['cap_full_from'] or hc_l[n_ - 1] * share[n_] < lb['cap_floor_m'] or hc_l[n_ - 1] * (1 - share[n_]) < lb['cap_below_crest_m']: full += 1; continue
            m = lab == n_; cap[m] = np.minimum(cap[m], ref_s[m] + hc_l[n_ - 1] * share[n_]); shoulders += 1      # a fixed height over the foot line: on a hillside the terrace climbs with the foot and never meets the ground
    cand = np.minimum(ground + P, np.maximum(cap, ground))
    within = (sd > -(lb['big_m'] + lb['rib_m'] + 12.0)) & (Hc >= lb['min_crest_m'])
    # cliff tops the walker already stands on in the previous stage (from the play area, 4 m over the foot) are not touched, nor the ring around them: rims and fall edges stay
    Rp0, _ = seal_state(prev, F, np.load(PLAY)); tops = ndi.binary_dilation(Rp0 & F & ((prev - foot_in) > 4.0), iterations=lb['top_ring_cells'])
    new = np.where(within & ~held & ~tops & ~hard & (allow > 0), np.maximum(prev, cand), prev)
    lobe_mask = new > prev + 0.05
    # ---- crest band: fill one-vertex teeth (raise only) so a face is not a row of triangle facets
    fl = cfg['fill']; band = F & (din <= fl['band_m']) & ~held & ~K & ~tops & (B.slope_deg(prev) > fl['from_deg'])
    sm = ndi.gaussian_filter(new, fl['sigma_cells']); filled = np.where(band, np.maximum(new, np.minimum(sm, new + fl['max_m'])), new)
    fill_mask = filled > new + 0.05; new = filled
    # ---- face relief: ridged noise on the steep cells of the bodies (raise only), so a face is not one plane from foot to rim
    fr = cfg['relief']; steep_now = B.slope_deg(new) > fr['from_deg']; onface = ndi.gaussian_filter(((Fn0 | F) & steep_now).astype(float), 1.0)
    rel = fr['rib_m'] * ridged(fr['rib_wl_m'], 'frib') ** fr['rib_power'] + fr['mass_m'] * ridged(fr['mass_wl_m'], 'fmass') ** 2 - fr['cut_m']
    up = np.maximum(0.0, rel) * smoothstep((onface - 0.35) / 0.4) * smoothstep((s - fr['above_toe_m']) / 6.0) * (~tops) * (~held) * (~K) * (Hc >= lb['min_crest_m'])
    relief_mask = up > 0.05; new = new + np.where(relief_mask, up, 0.0); lobe_mask |= relief_mask
    # ---- fins and slots (fix pass, 2026-10-12): from the ground a blade one cell thick, or a cleft a few cells wide between a buttress and
    # the wall, shows the sky behind it below the rim and reads as a torn mesh. Fins are thickened (their neighbours are raised to fin_drop_m
    # under them), then every cleft narrower than the closing disc and deeper than min_depth_m is filled. Raise only; rounds until nothing is left.
    cl = cfg['slots']; rr_ = cl['radius_cells']; yy_, xx_ = np.mgrid[-int(rr_ + 1):int(rr_ + 1) + 1, -int(rr_ + 1):int(rr_ + 1) + 1]; disc = (yy_ ** 2 + xx_ ** 2) <= rr_ * rr_
    okc = ndi.binary_dilation(new > prev + 0.05, iterations=cl['near_cells']) & ~K & ~tops & ~held; slot_cells = 0; fin_cells = 0
    ll_ = int(cl.get('line_cells', 0)); lines_ = [np.ones((1, ll_), bool), np.ones((ll_, 1), bool), np.eye(ll_, dtype=bool), np.fliplr(np.eye(ll_, dtype=bool))] if ll_ >= 2 else []      # 0 = off (see the config note)
    def fin_mask(f_): return ((f_ - ndi.grey_opening(f_, size=(3, 3))) > cl['fin_m'])
    def slot_mask(f_):
        c_ = ndi.grey_closing(f_, footprint=disc)
        for fp_ in lines_: c_ = np.maximum(c_, ndi.grey_closing(f_, footprint=fp_))
        return (c_ - f_) > cl['min_depth_m']
    def close_pass(f_, ok_):
        nonlocal_counts = [0, 0]
        for rnd in range(cl['rounds']):
            fm_ = fin_mask(f_) & ndi.binary_dilation(ok_, iterations=1); thick = ndi.grey_dilation(np.where(fm_, f_, -1e9), size=(3, 3)) - cl['fin_drop_m']; m1 = ok_ & (thick > f_ + 0.05); f_ = np.where(m1, thick, f_); nonlocal_counts[1] += int(m1.sum())
            closed = ndi.grey_closing(f_, footprint=disc)
            for fp_ in (lines_ if rnd == 0 else []): closed = np.maximum(closed, ndi.grey_closing(f_, footprint=fp_))      # a channel open at both ends is not closed by a disc: a line across it does (four bearings)
            m2 = ok_ & ((closed - f_) > cl['min_depth_m']); f_ = np.where(m2, closed, f_); nonlocal_counts[0] += int(m2.sum())
            if not m1.any() and not m2.any(): break
        return f_, nonlocal_counts
    new, cnt_ = close_pass(new, okc); slot_cells, fin_cells = cnt_; lobe_mask |= new > prev + 0.05
    slot = np.zeros(F.shape, bool)
    # ---- natural faces: ribs on listed steep slopes that are not scarp bodies
    spur = {}; spur_mask = np.zeros(F.shape, bool); sl_prev = B.slope_deg(prev)
    for S_ in cfg.get('spurs', []):
        x0, z0, x1, z1 = S_['box']; inbox = (X >= x0) & (X <= x1) & (Z >= z0) & (Z <= z1); edge_d = np.minimum.reduce([X - x0, x1 - X, Z - z0, z1 - Z])
        steep = ndi.gaussian_filter(((sl_prev > S_['from_deg']) & inbox).astype(float), S_['sigma_cells'])
        w = smoothstep((steep - S_['dens_from']) / (S_['dens_full'] - S_['dens_from'])) * smoothstep(edge_d / S_['edge_len_m']) * allow * (~F) * (~held) * (~K)
        gz, gx = np.gradient(ndi.gaussian_filter(prev, 6.0), CELL); mx = float(gx[inbox & (sl_prev > S_['from_deg'])].mean()); mz = float(gz[inbox & (sl_prev > S_['from_deg'])].mean()); ln = math.hypot(mx, mz) + 1e-9
        u = (-(X - x0) * mz + (Z - z0) * mx) / ln                           # coordinate across the mean fall line: a rib keeps its height all the way down
        wob = S_.get('wobble_m', 0.0) * noise2d(X, Z, S_.get('wobble_wl_m', 60.0), seed, 'spw_' + S_['id'])
        r1 = 1 - np.abs(noise2d(np.clip(u + wob + 2000.0, 0, 3900), np.zeros_like(u) + 37.0, S_['wl_m'], seed, 'spur_' + S_['id'])); r2 = 1 - np.abs(noise2d(np.clip(u + wob + 2000.0, 0, 3900), np.zeros_like(u) + 91.0, S_.get('knob_wl_m', 30.0), seed, 'spk_' + S_['id']))
        rel = S_['amp_m'] * r1 ** S_['power'] + S_.get('knob_m', 0.0) * r2 - S_['cut_m']
        up = np.maximum(0.0, rel) * w; m = up > 0.05; new = new + np.where(m, up, 0.0); spur_mask |= m
        spur[S_['id']] = dict(cells=int(m.sum()), raise_max_m=round(float(up.max()), 1), raise_mean_m=round(float(up[m].mean()), 1) if m.any() else 0.0)
    assert (new >= prev - 1e-9).all()
    # ---- repair: wherever a body became walkable above its toe (from anywhere on the bare terrain, or from the play area), the edit is undone around it
    band = Fn0 | F | spur_mask; repairs = []; undone = np.zeros(F.shape, bool)
    for tag in ('first', 'after_closing'):
        if tag == 'after_closing':      # the undo leaves pits and blades of its own: close what is not next to an undone entry, then prove again
            new, cnt2_ = close_pass(new, okc & ~ndi.binary_dilation(undone, iterations=3)); slot_cells += cnt2_[0]; fin_cells += cnt2_[1]
        for it in range(cfg['repair']['max_rounds']):
            reach_new = walk_reach(new, B.SEED_NODE, 45.0); bad = reach_new & ~reach_prev & band & ((new - ref) > cfg['repair']['above_foot_m'])
            bad |= reach_new & ~reach_prev & ndi.binary_dilation(spur_mask, iterations=1)      # a rib on a natural face must not become a ridge walk
            _, sx = seal_report(prev, new, band, ref); bad |= sx['new_high'] | sx['new_exit']
            if not bad.any(): break
            # undo only where the walker gets on: the bad cells within entry_cells of ground reached before (cutting the first steps of a ramp strands the rest)
            entry = bad & ndi.binary_dilation(reach_prev & ~bad, iterations=cfg['repair']['entry_cells']); entry = entry if entry.any() else bad
            zone = ndi.binary_dilation(entry, iterations=cfg['repair']['ring_cells']) & (new > prev); repairs.append(dict(stage=tag, round=it, bad_cells=int(bad.sum()), entry_cells=int(entry.sum()), undone_cells=int(zone.sum()), entries=B.clusters(entry, 40, 8))); steepen = it < cfg['repair']['steepen_rounds']
            if steepen:      # first make the first steps of the ramp a riser (the entry cells take the highest ground within two cells): no pit, no slot
                up_ = ndi.grey_dilation(new, size=(5, 5)); zone = entry & ~K & ~tops; new = np.where(zone, np.maximum(new, up_), new); repairs[-1]['how'] = 'steepened'
            else: new = np.where(zone, prev, new); undone |= zone; repairs[-1]['how'] = 'undone'
    lobe_mask &= new > prev + 0.05; fill_mask &= new > prev + 0.05; spur_mask &= new > prev + 0.05
    reach_new = walk_reach(new, B.SEED_NODE, 45.0); gained = reach_new & ~reach_prev; lost = reach_prev & ~reach_new
    okb_ = okc & ~ndi.binary_dilation(undone, iterations=3); left = dict(model='slot = closing(disc r 2.5 cells) - height > 5 m; fin = height - opening(3x3) > 6 m; counted on the body cells this stage may edit, outside the repair undo zones', slot_cells_left=int((slot_mask(new) & okb_).sum()), fin_cells_left=int((fin_mask(new) & okb_).sum()), slot_cells_prev=int((slot_mask(prev) & okb_).sum()), fin_cells_prev=int((fin_mask(prev) & okb_).sum()), in_undo_zones=int((slot_mask(new) & okc & ~okb_).sum()))
    seal, sx = seal_report(prev, new, band, ref)
    changed = lobe_mask | fill_mask | spur_mask; lost_else = lost & ~ndi.binary_dilation(changed, iterations=2)
    sl_new = B.slope_deg(new); q = lambda a, p: round(float(np.percentile(a, p)), 1) if a.size else 0.0
    lap = lambda f: np.abs(4 * f[1:-1, 1:-1] - f[:-2, 1:-1] - f[2:, 1:-1] - f[1:-1, :-2] - f[1:-1, 2:])
    fm = (F & (sl_prev > 55))[1:-1, 1:-1]; ring = F & (din <= CELL + 0.01)
    # plan-line straightness: over 96 m windows of the foot line, how far the foot wanders from its own smoothed line (m)
    def wander(field_sd):
        g = ndi.gaussian_filter(field_sd, 6.0); edge = np.abs(field_sd) < 2.0; return q(np.abs(field_sd - g)[edge], 50), q(np.abs(field_sd - g)[edge], 90)
    sd_new = None
    Fn = F | (lobe_mask & (new - prev > 6.0)); dn_in = ndi.distance_transform_edt(Fn, sampling=CELL); dn_out = ndi.distance_transform_edt(~Fn, sampling=CELL); sd_new = np.where(Fn, dn_in - 2.0, -(dn_out - 2.0))
    rep = dict(footprint_cells=int(F.sum()), keepout_cells=kparts, held_cells=int(held.sum()),
               lobes=dict(cells=int(lobe_mask.sum()), ha=round(float(lobe_mask.sum() * 16 / 1e4), 2), outside_body_cells=int((lobe_mask & ~F).sum()), raise_max_m=round(float((new - prev)[lobe_mask].max()), 1) if lobe_mask.any() else 0.0,
                          raise_p50_m=q((new - prev)[lobe_mask], 50), body_grew_cells=int((Fn & ~F).sum()), buttress_patches=int(nl), shoulders=shoulders, full_height=full),
               fill=dict(cells=int(fill_mask.sum()), raise_max_m=round(float(fl['max_m']), 1)), relief=dict(cells=int((relief_mask & (new > prev + 0.05)).sum()), raise_max_m=round(float(up.max()), 1)), slots_closed_cells=slot_cells, fins_thickened_cells=fin_cells, spurs=spur,
               foot=dict(first_ring_rise_prev_p50_m=q((prev - foot_in)[ring], 50), first_ring_rise_prev_p90_m=q((prev - foot_in)[ring], 90),
                         first_step_new_p50_m=q((new - prev)[(~F) & (dout <= CELL + 0.01) & lobe_mask], 50)),
               plan_wander_m=dict(prev_p50_p90=wander(np.where(F, din - 2.0, -(dout - 2.0))), new_p50_p90=wander(sd_new)),
               face_roughness_m=dict(prev_lap_p50=q(lap(prev)[fm], 50), new_lap_p50=q(lap(new)[fm], 50), prev_lap_p90=q(lap(prev)[fm], 90), new_lap_p90=q(lap(new)[fm], 90)),
               walk=dict(model='tile triangle slope < 45 deg from the new-game node', reach_prev=int(reach_prev.sum()), reach_new=int(reach_new.sum()), gained_cells=int(gained.sum()), gained_clusters=B.clusters(gained, 100, 12),
                         lost_cells=int(lost.sum()), lost_under_or_beside_change=int((lost & ~lost_else).sum()), lost_elsewhere=int(lost_else.sum()), lost_elsewhere_clusters=B.clusters(lost_else, 100, 12)),
               seal=seal, slots_and_fins=left, repairs=repairs, gained_on_bodies_over_toe=int((gained & band & ((new - ref) > cfg['repair']['above_foot_m'])).sum()), below_prev_cells=int((new < prev - 1e-6).sum()), hard_cells_changed=int(((np.abs(new - prev) > 0.05) & hard).sum()), keepout_cells_changed=int(((np.abs(new - prev) > 0.05) & K).sum()))
    return new, rep, dict(F=F, K=K, hard=hard, held=held, reach_prev=reach_prev, prev=prev, base=base, changed=changed, X=X, Z=Z, band=band, Fn=Fn0, ref=ref, lobe_mask=lobe_mask)


def write_stage(cfg, cfg_p, new, rep, faces_only, extra):
    out = stage_path(cfg['stage'], faces_only); B.guard_out(out); sha = B.save_height(out, new); B.write_text(str(out) + '.sha256', f'{sha}  {out.name}\n')
    base = extra['base']; prev = extra['prev']; ch = np.abs(new - prev) > 0.05
    rep = dict(stage=cfg['stage'], cfg_version=cfg['version'], base=cfg['inputs']['base_height'], base_sha256=cfg['inputs']['base_sha256'], prev=cfg['inputs']['prev_height'], prev_sha256=cfg['inputs']['prev_sha256'],
               prev_stage=cfg['inputs']['prev_stage'], changed_cells_vs_prev=int(ch.sum()), raised_max_m=round(float((new - prev).max()), 1), lowered_max_m=round(float((prev - new).max()), 1), **rep,
               tiles=B.tile_names(B.tiles_of((np.abs(new - base) > 0.05), 8)), tiles_changed_vs_prev=B.tile_names(B.tiles_of(ch, 8)),
               cfg=B.rel(cfg_p), cfg_sha256=B.sha256(cfg_p), out=B.rel(out), out_sha256=sha, ops=B.rel(cfg_p), ops_sha256=B.sha256(cfg_p), ops_version=cfg['version'],
               ops_note='stage n7 = cliffs308(stage n6): the ledger ops sha is this config. The next stage takes this file + out_sha256 as its prev.')
    B.write_json(str(out.with_suffix('')) + '.report.json', rep)
    print(f"cliffs308 {cfg['version']} stage {cfg['stage']}{' (faces)' if faces_only else ''}: {rep['changed_cells_vs_prev']} cells vs {cfg['inputs']['prev_stage']} -> {B.rel(out)} sha256 {sha}")
    return rep


def main():
    B.utf8()
    if len(sys.argv) != 3 or sys.argv[1] not in ('faces', 'route', 'final', 'build', 'all'): print(__doc__); return 2
    cfg_p = Path(sys.argv[2]); cfg = json.loads(cfg_p.read_text(encoding='utf-8')); what = sys.argv[1]
    if what in ('faces', 'all'):
        new, rep, extra = faces(cfg); r = write_stage(cfg, cfg_p, new, rep, True, extra)
        print(json.dumps({k_: v for k_, v in r.items() if k_ not in ('tiles', 'tiles_changed_vs_prev')}, ensure_ascii=False, indent=1))
    if what in ('route', 'all'):
        import cliffs308_ways as Wy; Wy.route(cfg, cfg_p)
    if what in ('final', 'all'):
        import cliffs308_ways as Wy; new, rep, extra = faces(cfg, log=lambda *a: None); new, wrep = Wy.shelves(cfg, new, extra); rep['ways'] = wrep; r = write_stage(cfg, cfg_p, new, rep, False, extra)
        print(json.dumps(r['ways'], ensure_ascii=False, indent=1))
    if what in ('build', 'all'):
        import cliffs308_ways as Wy; Wy.build(cfg, cfg_p)
    return 0


if __name__ == '__main__':
    sys.exit(main())
