"""amneung308_look_dry.py - offline proof of the LOOK pass of the 금표 암릉 rock band (#308 relayout fix 2, item L1).

Reads   Tools/Unity/Stage308_relayout_fix2/amneung/Data/amneung308_look.json   (or --data <file>: the deployed copy
        Art/World/Compact/Rebuild/CliffBoundary308/amneung308_look.json)
        Art/World/Compact/Rebuild/CliffBoundary308/amneung308.json               (the base build data: cores, stone, pads, the 25 present rocks; --base)
        the height field / routes / vegetation sheet the base dry reads, the real Unity mesh assets named in the look data
Writes  <out>/amneung308_look_dry.json + .txt  (default out = Tools/Unity/Stage308_relayout_fix2/amneung/Dry)
        --picture <png>: the silhouette sheet, before / after, from the stills' eyes (default Art/Playtest308/Relayout/fix2/amneung_silhouette_before_after.png;
        --no-picture skips it). Nothing else is written.

What is proven here, and what is not
  K  the look pass cannot have changed the collision: the look data carries no core / stone / pad; the base data it was derived from is
     the deployed one (sha), its cores and stone hash to the pinned values. The reach proofs of the base dry (A1-A9, B1-B10) depend on
     the cores, the stone, the pads and the ground only - run Tools/Art/amneung308_dry.py for them (--with-base runs it here).
  C  core-fit of the NEW rocks, on the real LOD0 and LOD1 meshes: V10 with a margin and +-ground uncertainty, face points, eye depth,
     and the stronger rule the fit used (no rock top that a 국 deck + running jump reaches is an invisible wall).
  S  silhouette from the stills' eyes: spread of the top line, no run of equal neighbours, spacing, breaks, tors, size classes.
  P  placement: nothing over the deck middle / in front of the stone / on the cut / in the tail lanes / on a tree; undersides buried;
     tails and talus as asked; triangles.
Every number is [O] (computed here on the 1b height field); the editor's verify:<scene> (V2 / V3 / V10 on the opened scene) is the judge.

Line by line against the editor (Amneung308.Look.cs LoadLook / SurveyLook / LookVerifyRocks / LookVerifyFit, Amneung308.Verify.cs):
  editor                                              here
  LoadLook refusals, SurveyLook blocks                K1, K4, K7, K9 (same fields, same conditions)
  V2 transform / LOD pair / material, no collider     K5, K7, P12c, P13 (the transform is the data itself; float32 round trip in K7)
  V3 lowest LOD1 vertex >= checks.sunk_under_m        V3s (replica gives the editor's number on look.2 first), V3x (LOD1 AND LOD0, + offline margin)
  V10 cells 3473 (LOD1, LOD0), limit fit_low_cells    C1v (exact triangles, the editor's cells), K8 (the limits are the base data's, read by the editor)
  V10 face points 740 (LOD1, LOD0)                    C2v (exact triangles, the editor's points); C2 is the raster twin
  V9 no Light / glow / text under the root            P12c (material), K5; the tool builds nothing else
Look revision 3 (data with a rev block): N1 - N8 judge the reworked stretches against look.2 (--r2, default Rev2/amneung308_look.r2.json).
Second build of revision 3 (review of the first build): N7s = the long faces of the cores that stand on the deck lie inside rock (review S2),
N9a - N9c = the invisible core against the visible top line, per column of the editor's cells: see-over from a 국 lift, core 1 m / 2 m over
the rock (review S1). Both are the reviewer's own measures; they first reproduce the reviewer's numbers on look.2 (N7r, N9r). N3b = the rocks the second build kept
from the first build (Rev3a) are transform-identical to it.
"""
import os, sys, io, json, math, hashlib, argparse, subprocess, pickle
os.environ.setdefault('OMP_NUM_THREADS', '4'); os.environ.setdefault('OPENBLAS_NUM_THREADS', '4'); os.environ.setdefault('MKL_NUM_THREADS', '4')
import numpy as np
HERE = os.path.dirname(os.path.abspath(__file__)).replace('\\', '/')
sys.path.insert(0, HERE)
import amneung308_dry as dry
import amneung308_look_lib as lib

ROOT = lib.ROOT
STAGE = ROOT + '/Tools/Unity/Stage308_relayout_fix2/amneung'
DATA = STAGE + '/Data/amneung308_look.json'
BASE = ROOT + '/Art/World/Compact/Rebuild/CliffBoundary308/amneung308.json'
OUT = STAGE + '/Dry'
PICTURE = ROOT + '/Art/Playtest308/Relayout/fix2/amneung_silhouette_before_after.png'
R2 = STAGE + '/Rev2/amneung308_look.r2.json'            # look.2: the 137 rocks the editor run of 2026-10-05 judged (V3 0.14 m at LookRock_69 [M])
PROTECTED = ('Watershed295', 'Reworld292', 'MountainTrail285')
BAND = ('spine', 'tor', 'plug', 'buttress', 'cheek', 'tail')          # the rocks of the band line (talus is not a neighbour of anything)


def sha(p):
    return hashlib.sha256(open(p, 'rb').read()).hexdigest()


def jsha(o):
    return hashlib.sha256(json.dumps(o, sort_keys=True, separators=(',', ':')).encode()).hexdigest()


class Scene:
    """the unchanged part: ground, cores, stone, pads, reach, masks"""
    def __init__(s, D, L):
        s.D = D; s.L = L; s.h4 = dry.load_height(); s.line = lib.Line(D['line']); st = D['stone']; cl = L['clear']
        xs = [c['x'] for c in D['cores']]; zs = [c['z'] for c in D['cores']]
        s.g = g = dry.Grid(s.h4, math.floor(min(xs)) - 26, math.ceil(max(xs)) + 26, math.floor(min(zs)) - 30, math.ceil(max(zs)) + 30, L['checks']['raster_cell_m'])
        s.H, s.core, s.stone, s.pad, s.built, s.stand = dry.build(g, D)
        s.cast = dry.castable_rule(g, s.H)
        s.reach = lib.reach_height(g, s.H, s.cast & s.stand & ~s.core)
        s.core_g = np.zeros(s.core.shape, bool)
        for c in D['cores']: s.core_g |= dry.in_box(g.X, g.Z, c['x'], c['z'], c['yaw'], c['size'][0], c['size'][2], g.c)
        s.su, s.sv = dry.box_uv(g.X, g.Z, st['x'], st['z'], st['yaw_box'])
        ex = L['cover']['reach_exempt_stone_m']; s.exempt = (np.abs(s.su) <= st['half_m'] + ex) & (np.abs(s.sv) <= st['half_m'] + ex)
        s.shoulder = (np.abs(s.su) > cl['deck_half_u_m']) & (np.abs(s.su) <= st['half_m']) & (np.abs(s.sv) <= st['half_m']) & ~s.core_g
        s.deck = (np.abs(s.su) <= cl['deck_half_u_m']) & (np.abs(s.sv) <= st['half_m']) & ~s.core_g
        s.corridor = (np.abs(s.su) <= st['half_m']) & (np.abs(s.sv) > st['half_m']) & (np.abs(s.sv) <= st['half_m'] + cl['corridor_m']) & ~s.core_g
        RT = dry.routes(); cut = dry.resample(RT[D['checks']['cut_route']], 0.25)
        m = (cut[:, 0] > g.x0) & (cut[:, 0] < g.x0 + g.nx * g.c) & (cut[:, 1] > g.z0) & (cut[:, 1] < g.z0 + g.nz * g.c); s.cut = cut[m]
        s.route = np.zeros(s.core.shape, bool); k = int(cl['route_clear_m'] / g.c) + 1
        for x, z in s.cut[::2]:
            i, j = g.cell(x, z); sub = (slice(max(0, i - k), i + k + 1), slice(max(0, j - k), j + k + 1))
            s.route[sub] |= np.hypot(g.X[sub] - x, g.Z[sub] - z) <= cl['route_clear_m']
        s.route &= ~s.core_g
        s.trees = []
        for sp in D['vegetation']['sheets']:
            if sp['role'] != 'rendered': continue
            gone = {(v['id'], round(v['x'], 2), round(v['z'], 2)) for v in D['vegetation']['rows']}
            for rid, proto, pos in dry.sheet_rows(ROOT + '/Oheangbu/' + sp['path']):
                if not (g.x0 < pos[0] < g.x0 + g.nx * g.c and g.z0 < pos[2] < g.z0 + g.nz * g.c): continue
                if (rid, round(pos[0], 2), round(pos[2], 2)) in gone: continue
                if any(t in proto for t in D['vegetation']['tree_tokens']): s.trees.append((pos[0], pos[2], rid, proto))
        s.end_w = min(s.line.station(c['x'], c['z'])[0] - c['size'][0] * 0.5 for c in D['cores'])
        s.end_e = max(s.line.station(c['x'], c['z'])[0] + c['size'][0] * 0.5 for c in D['cores'])
        s.stone_s = s.line.station(st['x'], st['z'])[0]
        s.lane = np.zeros(s.core.shape, bool)
        for l in L['lanes']:
            a = np.array(l['a']); b = np.array(l['b']); ab = b - a; t = np.clip(((g.X - a[0]) * ab[0] + (g.Z - a[1]) * ab[1]) / (ab @ ab), 0, 1)
            s.lane |= np.hypot(g.X - (a[0] + ab[0] * t), g.Z - (a[1] + ab[1] * t)) <= l['half_width_m']


def look1_sha(rocks):
    """hash of the transforms of the rocks that are not of the look-review pass (they must stay the proven look.1 set)"""
    rows = [[r['mesh'], r['role'], r['x'], r['y'], r['z'], r['q'], r['scale']] for r in rocks if not r.get('capped')]
    return jsha(rows), len(rows)


def verify_cells(S):
    """the cells of the EDITOR verify V10 (shared with the derive: amneung308_look_lib.verify_cells) - 3473 cells, not the 3073 of the world grid"""
    return lib.verify_cells(S.D, S.h4, S.L['checks']['raster_cell_m'])


def exact_top(pts, rocks, meshes, lod):
    """highest point of the rocks' real triangles straight over every point (the editor's point-in-triangle test, no raster)"""
    return lib.exact_top(pts, rocks, meshes, lod)


def verify_faces(S):
    """the face points of the EDITOR verify V10 (second line; shared with the derive)"""
    return lib.verify_faces(S.D, S.h4, S.L['checks']['raster_cell_m'])


def see_numbers(S, pts, col, top, ck):
    """review S1 (the reviewer's measure): per column of the editor's cells the visible top line against the invisible core. Lengths in
    metres of core (columns x cell), east / west of the stone box, within near_m / path_m of it"""
    sk = ck['see_over']; R = lib.top_line(S.D, pts, col, top); cell = ck['raster_cell_m']; eye = dry.GUK + dry.EYE_M; out = {}
    see = (R[:, 0] < eye) & (R[:, 1] > sk['core_over_min_m']); w1 = R[:, 1] > sk['wall1_m']; w2 = R[:, 1] > sk['wall2_m']
    for side, sg in (('east', 1), ('west', -1)):
        for zone in ('near', 'path'):
            m = (R[:, 3] <= sk[zone + '_m']) & (R[:, 4] * sg > 0)
            out['%s_%s' % (side, zone)] = dict(columns=int(m.sum()), see_m=round(float(cell * (see & m).sum()), 2), wall1_m=round(float(cell * (w1 & m).sum()), 2), wall2_m=round(float(cell * (w2 & m).sum()), 2),
                                               top_min_m=round(float(R[m, 0].min()), 2) if m.any() else None, core_over_max_m=round(float(R[m, 1].max()), 2) if m.any() else None)
    return out


def deck_faces(S, rocks, meshes, ck):
    """review S2 (the reviewer's measure): share of the long faces of the cores that stand on the deck that lies inside a visible rock"""
    dk = ck['deck_faces']; out = {}
    for cid, side, P in lib.deck_face_points(S.D, dk['along_m'], dk['step_m'], dk['over_m']):
        out['%s %s' % (cid, side)] = dict(points=int(len(P)), lod0=round(float(lib.inside_any(P, rocks, meshes, 0).mean()), 3), lod1=round(float(lib.inside_any(P, rocks, meshes, 1).mean()), 3))
    return out


def v10_exact(S, rocks, meshes, ck):
    pts = verify_cells(S); fpts = verify_faces(S); out = {}; col = lib.verify_columns(S.D, ck['raster_cell_m'])[0] if 'see_over' in ck else None; tops = {}
    for lod in (1, 0):
        top = exact_top(pts, rocks, meshes, lod); want = np.minimum(pts[:, 3], pts[:, 2] + ck['v10_low_rock_m']); tops[lod] = top
        low = (top < pts[:, 3] - 1e-3) & (top < pts[:, 2] + ck['v10_low_rock_m'])
        slack = np.where(np.isfinite(top), top, pts[:, 2] - 9.0) - want
        ftop = exact_top(fpts, rocks, meshes, lod); bare = ~(ftop >= fpts[:, 2] + ck['face_h_m']); fs = np.where(np.isfinite(ftop), ftop, fpts[:, 2] - 9.0) - (fpts[:, 2] + ck['face_h_m'])
        out['lod%d' % lod] = dict(cells=int(len(pts)), low=int(low.sum()), slack_min_m=round(float(slack.min()), 3), face_points=int(len(fpts)), bare=int(bare.sum()), face_slack_min_m=round(float(fs.min()), 3))
        if col is not None: out['lod%d' % lod]['see'] = see_numbers(S, pts, col, top, ck)
    if 'deck_reach' in ck:
        idx = lib.deck_reach_cells(S.D, pts, ck['deck_reach']['within_m']); low = np.minimum(tops[0], tops[1])
        out['deck_reach'] = {k_: dict(cells=int(len(i_)), both=lib.deck_reach_count(S.D, pts[i_], low[i_]), lod0=lib.deck_reach_count(S.D, pts[i_], tops[0][i_])) for k_, i_ in idx.items()}
    return out


def v3_rows(S, rocks, meshes):
    """the editor's V3 for look rocks, per rock: sink of the lowest LOD1 vertex and of the lowest LOD0 vertex"""
    out = []
    for r in rocks:
        ls = lib.low_sink(S.h4, r, meshes); out.append(dict(id=r['id'], role=r.get('role', ''), mesh=r['mesh'], lod1=round(ls['lod1'][0], 3), lod0=round(ls['lod0'][0], 3)))
    return out


def teeth(prof, s0, a, b, prom_min, width_max):
    """narrow teeth of a top line (heights every 0.5 m from station s0) between stations a and b: local tops that stand at least prom_min over the
    higher of their two valleys and are narrower than width_max where they are prom_min under their top"""
    S_ = s0 + 0.5 * np.arange(len(prof)); idx = [k for k in range(len(prof)) if a <= S_[k] <= b]; out = []
    if len(idx) < 3: return out
    h = np.array([prof[k] for k in idx]); n = len(h)
    for k in range(n):
        if (k > 0 and h[k] < h[k - 1]) or (k < n - 1 and h[k] <= h[k + 1]): continue
        l = k
        while l > 0 and h[l - 1] <= h[k]: l -= 1
        r_ = k
        while r_ < n - 1 and h[r_ + 1] <= h[k]: r_ += 1
        left = h[l:k + 1].min() if l > 0 else h[:k + 1].min(); right = h[k:r_ + 1].min() if r_ < n - 1 else h[k:].min()
        prom = h[k] - max(left, right)
        if prom < prom_min: continue
        wl = k
        while wl > 0 and h[wl - 1] > h[k] - prom_min: wl -= 1
        wr = k
        while wr < n - 1 and h[wr + 1] > h[k] - prom_min: wr += 1
        width = 0.5 * (wr - wl + 1)
        if width <= width_max: out.append((round(float(S_[idx[k]]), 1), round(float(h[k]), 2), round(float(prom), 2), width))
    return out


def connected_band(S, top0, ck):
    """the band as ONE footprint from above: rock higher than plan_rock_over_m, the part that hangs together with the rock on the cores
    (8 neighbours). Returns the mask, its width per metre of station (north reach + south reach), and the reach arrays"""
    g = S.g; F = np.isfinite(top0) & (top0 > g.ground + ck['plan_rock_over_m']); comp = F & S.core
    for _ in range(4000):
        grow = comp.copy()
        grow[1:, :] |= comp[:-1, :]; grow[:-1, :] |= comp[1:, :]; grow[:, 1:] |= comp[:, :-1]; grow[:, :-1] |= comp[:, 1:]
        grow[1:, 1:] |= comp[:-1, :-1]; grow[:-1, :-1] |= comp[1:, 1:]; grow[1:, :-1] |= comp[:-1, 1:]; grow[:-1, 1:] |= comp[1:, :-1]
        grow &= F
        if (grow == comp).all(): break
        comp = grow
    s0 = S.end_w; n = int(S.end_e - S.end_w) + 1; north = np.zeros(n); south = np.zeros(n)
    for i, j in np.argwhere(comp):
        s_, off = S.line.station(g.X[i, j], g.Z[i, j]); k = int(s_ - s0)
        if 0 <= k < n:
            if off > 0: north[k] = max(north[k], off)
            else: south[k] = max(south[k], -off)
    Ss = s0 + 0.5 + np.arange(n); keep = np.abs(Ss - S.stone_s) > S.D['stone']['half_m'] + 0.5; w = (north + south)[keep]
    return comp, dict(width_mean_m=round(float(w.mean()), 2), width_cv=round(float(w.std() / w.mean()), 3), width_min_m=round(float(w.min()), 2), width_max_m=round(float(w.max()), 2),
                      wide_stations=int((w > w.mean() * 1.3).sum()), north_std_m=round(float(north[keep].std()), 2), south_std_m=round(float(south[keep].std()), 2))


def deck_side(S, top0, top1):
    """how the rock stands beside the deck (where the cores are an invisible wall for who stands on the stone): per side, the strip of core inside
    the stone box and the first metre of core outside it - rock top over the deck top (the lower of LOD0 / LOD1)"""
    st = S.D['stone']; eff = np.minimum(top0, top1); d = np.where(np.isfinite(eff), eff, -1e3) - st['top_y']; out = {}
    for sg, name in ((-1, 'west'), (1, 'east')):
        strip = S.core & (S.su * sg > 0) & (np.abs(S.su) <= st['half_m']) & (np.abs(S.sv) <= st['half_m'])
        first = S.core & (S.su * sg > st['half_m']) & (S.su * sg <= st['half_m'] + 1.0) & (np.abs(S.sv) <= st['half_m'])
        out[name] = dict(strip_cells=int(strip.sum()), strip_under_eye=int((d[strip] < dry.EYE_M).sum()), strip_median_m=round(float(np.median(d[strip])), 2),
                         first_cells=int(first.sum()), first_min_m=round(float(d[first].min()), 2), first_p10_m=round(float(np.percentile(d[first], 10)), 2), first_median_m=round(float(np.median(d[first])), 2), first_under_eye=int((d[first] < dry.EYE_M).sum()))
    return out


def near_tops(S, top0, reach):
    """the highest rock top (world y) within `reach` of the stone box, west and east of it"""
    st = S.D['stone']; near = dry.near_box(S.g.X, S.g.Z, st['x'], st['z'], st['yaw_box'], st['half_m'] * 2, st['half_m'] * 2, reach) & np.isfinite(top0)
    side = lambda m: round(float(top0[m].max()), 2) if m.any() else st['top_y'] - 99.0
    return dict(west=side(near & (S.su < 0)), east=side(near & (S.su > 0)))


def plan_outline(S, top0, ck):
    """the band seen from straight above (verifier V4): per metre of station the farthest rock taller than plan_rock_over_m on the
    north and on the south side of the line; a ribbon has edges that do not move"""
    g = S.g; s0 = S.end_w; n = int(S.end_e - S.end_w) + 1; north = np.zeros(n); south = np.zeros(n)
    for i, j in np.argwhere(np.isfinite(top0) & (top0 > g.ground + ck['plan_rock_over_m'])):
        s_, off = S.line.station(g.X[i, j], g.Z[i, j])
        if abs(off) > ck['plan_off_max_m']: continue
        k = int(s_ - s0)
        if 0 <= k < n:
            if off > 0: north[k] = max(north[k], off)
            else: south[k] = max(south[k], -off)
    Ss = s0 + 0.5 + np.arange(n); keep = np.abs(Ss - S.stone_s) > S.D['stone']['half_m'] + 0.5
    w = (north + south)[keep]
    return dict(stations=int(keep.sum()), north_std_m=round(float(north[keep].std()), 2), south_std_m=round(float(south[keep].std()), 2), north_max_m=round(float(north[keep].max()), 2), south_max_m=round(float(south[keep].max()), 2),
                width_mean_m=round(float(w.mean()), 2), width_cv=round(float(w.std() / w.mean()), 2), north=[round(float(v), 2) for v in north], south=[round(float(v), 2) for v in south])


def end_drops(S, top0, ck):
    """the biggest fall of the top line over one metre within end_drop_len_m outside each core end"""
    g = S.g; out = {}
    prof = {}
    for i, j in np.argwhere(np.isfinite(top0)):
        s_, off = S.line.station(g.X[i, j], g.Z[i, j])
        if abs(off) > 4.0: continue
        k = int(math.floor(s_)); prof[k] = max(prof.get(k, 0.0), float(top0[i, j] - g.ground[i, j]))
    for side, e, sg in (('west', S.end_w, -1), ('east', S.end_e, 1)):
        k0 = int(math.floor(e)) - (1 if sg > 0 else 0); hs = [prof.get(k0 + sg * q, 0.0) for q in range(0, int(ck['end_drop_len_m']) + 2)]
        out[side] = dict(profile=[round(h, 2) for h in hs], max_drop_m=round(max([hs[q] - hs[q + 1] for q in range(len(hs) - 1)] + [0.0]), 2))
    return out


CACHE = dict(dir=None, measures=0, hits=0)          # --cache <dir>: whole measures of unchanged rock sets are read back (the mutation suite runs the dry dozens of times)


def rasters(S, rocks, meshes):
    return [(lib.rock_raster(S.g, r, meshes, 0), lib.rock_raster(S.g, r, meshes, 1)) for r in rocks]


def crest_of(S, r, sub):
    """height of a rock's highest LOD0 vertex over the ground under its own anchor"""
    V, _ = lib.unity_mesh(lib.mesh_table(S.L)[r['mesh']]['lod0'])
    return float(lib.world_verts(r, V)[:, 1].max() - r['ground_y'])


def fit_numbers(S, top0, top1, ck):
    """core-fit of a rock set against the unchanged cores: V10 (with and without margin), the reach rule, faces, eye depth"""
    g = S.g; D = S.D; out = {}
    for name, top in (('lod0', top0), ('lod1', top1)):
        v10 = S.core & (top < S.H - 1e-3) & (top < g.ground + ck['v10_low_rock_m'])
        want = np.minimum(S.H + ck['v10_margin_m'], g.ground + ck['v10_low_rock_m'] + ck['ground_unc_m'] + ck['v10_margin_m'])
        v10m = S.core & ~(top >= want)
        reach_all = S.core & (top < S.H - 1e-3) & ~(top >= S.reach + dry.STEP + ck['ground_unc_m'] + ck['reach_margin_m'])
        reach = reach_all & ~S.exempt
        cells = int(S.core.sum()); fin = S.core & np.isfinite(top); cr = top[fin] - g.ground[fin]
        slack = np.where(S.core, np.where(np.isfinite(top), top, g.ground - 9) - np.minimum(S.H, g.ground + ck['v10_low_rock_m']), np.inf)
        out[name] = dict(core_cells=cells, v10_low_cells=int(v10.sum()), v10_margin_fail_cells=int(v10m.sum()), v10_slack_min_m=round(float(slack.min()), 2),
                         reach_wall_cells=int(reach.sum()), reach_wall_exempt_cells=int((reach_all & S.exempt).sum()), exempt_core_cells=int((S.core & S.exempt).sum()), below_core_top_cells=int((S.core & ~(top >= S.H)).sum()),
                         crest_over_ground_m=dict(min=round(float(cr.min()), 2), median=round(float(np.median(cr)), 2), max=round(float(cr.max()), 2)) if cr.size else None)
        bare = 0; n = 0
        for c in D['cores']:
            a = math.radians(c['yaw']); ux, uz = math.cos(a), -math.sin(a); vx, vz = math.sin(a), math.cos(a)
            for side in (-1, 1):
                for t in np.arange(-c['size'][0] * 0.5, c['size'][0] * 0.5 + 1e-6, 0.25):
                    px = c['x'] + ux * t + vx * side * c['size'][2] * 0.5; pz = c['z'] + uz * t + vz * side * c['size'][2] * 0.5
                    i, j = g.cell(px, pz)
                    n += 1
                    if not (top[i, j] >= g.ground[i, j] + ck['face_h_m']): bare += 1
        out[name].update(face_points=n, bare_face_points=bare)
        ed = dry.eye_depth(g, D, top)
        out[name]['eye_depth'] = dict(mean_m=round(float(np.mean([e['mean_m'] for e in ed])), 2), max_m=max(e['max_m'] for e in ed), faces=len(ed),
                                      worst=sorted([(e['mean_m'], e['id'], e['side']) for e in ed], reverse=True)[:4])
    return out


def clear_numbers(S, top0):
    """what the rocks do where a body stands or walks: deck, pad corridor, cut, lanes; and how much rock a first-person eye can walk into"""
    g = S.g; st = S.D['stone']; a2 = g.c * g.c; fin = np.isfinite(top0)
    surf = np.maximum(S.H, g.ground)
    out = dict(shoulder_cells=int(S.shoulder.sum()), shoulder_rock_over_top_cells=int((S.shoulder & (top0 > st['top_y'])).sum()), deck_cells=int(S.deck.sum()), deck_rock_over_top_cells=int((S.deck & (top0 > st['top_y'])).sum()),
               corridor_rock_cells=int((S.corridor & fin & (top0 > surf + 0.02)).sum()), pad_rock_cells=int((S.pad & fin & (top0 > surf + 0.02)).sum()),
               route_rock_cells=int((S.route & fin & (top0 > surf + 0.02)).sum()), lane_rock_cells=int((S.lane & fin).sum()),
               lane_cells=int(S.lane.sum()), lane_core_cells=int((S.lane & S.core).sum()), lane_steep_cells=int((S.lane & (g.slope > dry.SLOPE_MAX)).sum()))
    core_g1 = np.zeros(S.core.shape, bool)
    for c in S.D['cores']: core_g1 |= dry.in_box(g.X, g.Z, c['x'], c['z'], c['yaw'], c['size'][0], c['size'][2], 1.0)
    ghost = fin & (top0 > g.ground + dry.EYE_M) & ~core_g1 & ~S.stone
    out['ghost_eye_m2'] = round(float(ghost.sum() * a2), 1)             # rock higher than the eye, more than 1 m outside every core: nothing stops a body there
    ends = np.zeros(S.core.shape, bool)
    for i in np.argwhere(ghost):
        s_, _ = S.line.station(g.X[i[0], i[1]], g.Z[i[0], i[1]])
        if s_ < S.end_w or s_ > S.end_e: ends[i[0], i[1]] = True
    out['ghost_eye_beyond_ends_m2'] = round(float(ends.sum() * a2), 1)
    return out


def walk_around(S, top0, step_m):
    """shortest walk (8 neighbours, slope <= 45 deg) between the two cut points 20 m either side of the stone, around the west end and
    around the east end, with cores + stone solid. With `top0`: every cell with visible rock higher than step_m is solid too."""
    import heapq
    g = S.g; solid = np.zeros(S.core.shape, bool)
    for c in S.D['cores']: solid |= dry.in_box(g.X, g.Z, c['x'], c['z'], c['yaw'], c['size'][0], c['size'][2], 0.3)
    st = S.D['stone']; solid |= dry.in_box(g.X, g.Z, st['x'], st['z'], st['yaw_box'], st['half_m'] * 2, st['half_m'] * 2, 0.3)
    if top0 is not None: solid |= np.isfinite(top0) & (top0 > g.ground + step_m)
    d = np.hypot(S.cut[:, 0] - st['x'], S.cut[:, 1] - st['z']); k = int(d.argmin())
    A = S.cut[max(0, k - 80)]; Bp = S.cut[min(len(S.cut) - 1, k + 80)]
    res = {}
    for name, cap_end in (('west', 1), ('east', 0)):
        cap = np.zeros(S.core.shape, bool); P = S.line.P
        end, nxt = (P[-1], P[-2]) if cap_end else (P[0], P[1]); dd = (end - nxt) / np.hypot(*(end - nxt))
        for t in np.arange(0, 70, 0.1):
            p = end + dd * t; i, j = g.cell(p[0], p[1]); cap[max(0, i - 2):i + 3, max(0, j - 2):j + 3] = True
        bl = solid | cap; si, sj = g.cell(*A); ti, tj = g.cell(*Bp)
        Dm = np.full(S.core.shape, np.inf); Dm[si, sj] = 0; pq = [(0.0, si, sj)]
        while pq:
            c0, i, j = heapq.heappop(pq)
            if c0 > Dm[i, j]: continue
            if (i, j) == (ti, tj): break
            for a in (-1, 0, 1):
                for b in (-1, 0, 1):
                    if not (a or b): continue
                    ni, nj = i + a, j + b
                    if ni < 0 or nj < 0 or ni >= g.nz or nj >= g.nx or bl[ni, nj]: continue
                    if a and b and (bl[i + a, j] or bl[i, j + b]): continue
                    run = g.c * math.hypot(a, b); dh = g.ground[ni, nj] - g.ground[i, j]
                    if g.slope[ni, nj] > dry.SLOPE_MAX or dh > run * dry.TAN + 0.02: continue
                    nd = c0 + math.hypot(run, dh)
                    if nd < Dm[ni, nj]: Dm[ni, nj] = nd; heapq.heappush(pq, (nd, ni, nj))
        res[name] = round(float(Dm[ti, tj]), 1) if np.isfinite(Dm[ti, tj]) else None
    return res


def band_stats(S, L, rocks, subs, ck):
    """per-rock numbers along the band: crest, spacing, size classes"""
    rows = []
    for r, sb in zip(rocks, subs):
        if r.get('role', 'base') not in BAND + ('base',): continue
        s_, off = S.line.station(r.get('ground_x', r['x']), r.get('ground_z', r['z']))
        rows.append(dict(id=r['id'], role=r.get('role', 'base'), station=round(float(s_), 2), off=round(float(off), 2), crest_m=round(crest_of(S, r, sb), 2)))
    rows.sort(key=lambda a: a['station'])
    hs = [a['crest_m'] for a in rows]; n = ck['run_len']; runs = []
    for k in range(len(hs) - n + 1):
        w = hs[k:k + n]
        if (max(w) - min(w)) / max(w) <= ck['run_within']: runs.append([rows[k + q]['id'] for q in range(n)])
    sp = np.diff([a['station'] for a in rows if a['role'] != 'tail'])          # the tails fade out on purpose: their gaps are not the band's rhythm
    return dict(rocks=rows, runs=runs, spacing_mean_m=round(float(sp.mean()), 2), spacing_cv=round(float(sp.std() / sp.mean()), 2), spacing_min_m=round(float(sp.min()), 2), spacing_max_m=round(float(sp.max()), 2))


def plan_profile(S, top0, ck):
    """the top line seen square on from far away: highest rock over the ground, per 0.5 m of station (the stone gap left out)"""
    g = S.g; s0 = S.end_w - 12.0; n = int((S.end_e + 12.0 - s0) / 0.5) + 1; prof = np.zeros(n)
    for i, j in np.argwhere(np.isfinite(top0)):
        s_, off = S.line.station(g.X[i, j], g.Z[i, j])
        if abs(off) > 4.0: continue
        k = int(round((s_ - s0) / 0.5))
        if 0 <= k < n: prof[k] = max(prof[k], top0[i, j] - g.ground[i, j])
    Ss = s0 + 0.5 * np.arange(n); span = (Ss >= S.end_w) & (Ss <= S.end_e) & ~(np.abs(Ss - S.stone_s) <= S.D['stone']['half_m'] + 0.25)

    def stretches(mask):
        runs = []; k = 0
        while k < n:
            if span[k] and mask[k]:
                q = k
                while q + 1 < n and span[q + 1] and mask[q + 1]: q += 1
                runs.append([float(Ss[k]), float(Ss[q])]); k = q + 1
            else: k += 1
        return runs
    v = prof[span]
    return dict(std_m=round(float(v.std()), 2), min_m=round(float(v.min()), 2), max_m=round(float(v.max()), 2), mean_m=round(float(v.mean()), 2),
                breaks=[r for r in stretches(prof < ck['break_under_m']) if r[1] - r[0] >= ck['break_min_len_m'] - 1e-6], tors=stretches(prof > ck['tor_over_m']),
                profile=[round(float(h), 2) for h in prof], s0=float(s0))


def skyline_stats(S, L, top0, ck):
    """the top line of the band from every eye: spread over the core span (stone gap left out), breaks and tors of the broadside profile"""
    out = {}
    st_half = S.D['stone']['half_m']
    for e in L['eyes']:
        Ss, Hh = lib.skyline(S.g, S.h4, top0, S.line, e['pos'], S.end_w - 12.0, S.end_e + 12.0, 0.5)
        if Ss is None: out[e['id']] = dict(judge=False, note='the eye looks along the band: no profile'); continue
        span = (Ss >= S.end_w) & (Ss <= S.end_e) & ~(np.abs(Ss - S.stone_s) <= st_half + 0.25)
        vis = span & np.isfinite(Hh); v = Hh[vis]
        row = dict(judge=bool(e.get('judge')), stations=int(span.sum()), visible=int(vis.sum()), std_m=round(float(v.std()), 2) if v.size else None,
                   min_m=round(float(v.min()), 2) if v.size else None, max_m=round(float(v.max()), 2) if v.size else None, mean_m=round(float(v.mean()), 2) if v.size else None)
        # breaks: stretches of the span at least break_min_len_m long where the top line is under break_under_m; tors: stretches over tor_over_m
        def stretches(mask):
            runs = []; k = 0; idx = np.nonzero(span)[0]
            while k < len(idx):
                if mask[idx[k]]:
                    q = k
                    while q + 1 < len(idx) and mask[idx[q + 1]] and idx[q + 1] == idx[q] + 1: q += 1
                    runs.append((float(Ss[idx[k]]), float(Ss[idx[q]]))); k = q + 1
                else: k += 1
            return runs
        fin = np.isfinite(Hh)
        row['breaks'] = [[a, b] for a, b in stretches(fin & (Hh < ck['break_under_m'])) if b - a >= ck['break_min_len_m'] - 1e-6]
        row['tors'] = [[a, b] for a, b in stretches(fin & (Hh > ck['tor_over_m']))]
        row['profile'] = [None if not np.isfinite(h) else round(float(h), 2) for h in Hh]; row['s0'] = float(Ss[0])
        out[e['id']] = row
    return out


def measure(S, L, rocks, meshes, ck, tag):
    key = None
    if CACHE['dir']:
        sig = dict(rocks=[[r['id'], r.get('role'), r['mesh'], r['x'], r['y'], r['z'], r['q'], r['scale'], r.get('ground_y'), r.get('ground_x'), r.get('ground_z'), r.get('crest_m'), r.get('along_m'), r.get('across_m')] for r in rocks],
                   meshes=sorted([m['key'], m['lod0'], m['lod1']] for m in meshes.values()), checks=ck, eyes=L['eyes'], lanes=L['lanes'], clear=L['clear'], cover=L['cover'], cores=S.D['cores'], stone=S.D['stone'], line=S.D['line'], height=sha(dry.HEIGHT), code=sha(__file__) + sha(lib.__file__))
        key = os.path.join(CACHE['dir'], jsha(sig) + '.pkl'); CACHE['measures'] += 1
        if os.path.isfile(key):
            M, subs, top0, top1 = pickle.load(open(key, 'rb')); M['tag'] = tag; CACHE['hits'] += 1
            return M, subs, top0, top1
    out = measure_now(S, L, rocks, meshes, ck, tag)
    if key: os.makedirs(CACHE['dir'], exist_ok=True); pickle.dump(out, open(key, 'wb'), protocol=4)
    return out


def measure_now(S, L, rocks, meshes, ck, tag):
    subs = rasters(S, rocks, meshes)
    top0 = lib.combine(S.g, [a for a, _ in subs]); top1 = lib.combine(S.g, [b for _, b in subs])
    M = dict(tag=tag, rocks=len(rocks), fit=fit_numbers(S, top0, top1, ck), clear=clear_numbers(S, top0), band=band_stats(S, L, rocks, subs, ck), skyline=skyline_stats(S, L, top0, ck), plan=plan_profile(S, top0, ck))
    M['triangles'] = dict(lod0=int(sum(meshes[r['mesh']]['tris0'] for r in rocks)), lod1=int(sum(meshes[r['mesh']]['tris1'] for r in rocks)))
    M['v10_exact'] = v10_exact(S, rocks, meshes, ck); M['outline'] = plan_outline(S, top0, ck); M['end_drops'] = end_drops(S, top0, ck)
    M['connected'] = connected_band(S, top0, ck)[1]; M['deck_side'] = deck_side(S, top0, top1)
    if 'near' in ck: M['near_tops'] = near_tops(S, top0, ck['near']['platform_m'])
    if 'deck_faces' in ck: M['deck_faces'] = deck_faces(S, rocks, meshes, ck)
    M['walk_around_cores_only_m'] = walk_around(S, None, 0.0)
    M['walk_around_rock_solid_m'] = walk_around(S, top0, dry.STEP)
    return M, subs, top0, top1


def picture(path, S, L, D, before, after, Mb, Ma):
    from PIL import Image, ImageDraw
    eyes = [e for e in L['eyes'] if e['id'] in ('A2', 'A3', 'A7')]
    VW, VH = 800, 450; PW = 800; pad = 10; head = 26
    Wd = pad + (VW + pad) * 2; Hd = head + (VH + pad + 14) * len(eyes) + 330 + 250 + pad
    img = Image.new('RGB', (Wd, Hd), (245, 243, 236)); dr = ImageDraw.Draw(img)
    dr.text((pad, 6), 'amneung308 look - silhouette BEFORE (25 x Massif, yaw only)        |        AFTER (%d rocks, 5 meshes)   [offline render from the real meshes on the 1b height field; no trees, no shader]' % len(after), fill=(0, 0, 0))
    g = S.g; GT = lib.ground_tris(S.h4, g.x0 - 60, g.x0 + g.nx * g.c + 60, g.z0 - 80, g.z0 + g.nz * g.c + 80)
    mt = lib.mesh_table(L)

    def tris(rocks):
        out = []
        for r in rocks:
            V, T = lib.unity_mesh(mt[r['mesh']]['lod1']); out.append(lib.world_verts(r, V)[T])
        return np.concatenate(out)
    TB, TA = tris(before), tris(after); st = D['stone']
    # the stone as a grey box (so the gap reads)
    a = math.radians(st['yaw_box']); ux, uz, vx, vz = math.cos(a), -math.sin(a), math.sin(a), math.cos(a); hm = st['half_m']
    cs = [(st['x'] + ux * p + vx * q, st['z'] + uz * p + vz * q) for p, q in ((-hm, -hm), (hm, -hm), (hm, hm), (-hm, hm))]
    yb, yt = st['top_y'] - 2.6, st['top_y']; sbox = []
    for k in range(4):
        (x0, z0), (x1, z1) = cs[k], cs[(k + 1) % 4]
        sbox += [[(x0, yb, z0), (x1, yb, z1), (x1, yt, z1)], [(x0, yb, z0), (x1, yt, z1), (x0, yt, z0)]]
    sbox += [[(cs[0][0], yt, cs[0][1]), (cs[1][0], yt, cs[1][1]), (cs[2][0], yt, cs[2][1])], [(cs[0][0], yt, cs[0][1]), (cs[2][0], yt, cs[2][1]), (cs[3][0], yt, cs[3][1])]]
    sbox = np.array(sbox, float)
    y = head
    for e in eyes:
        for col, T in enumerate((TB, TA)):
            x = pad + col * (VW + pad)
            lib.render_view(dr, (x, y, VW, VH), e['pos'], e['look'], e['fov'] * 1.0, GT, np.concatenate([T, sbox]))
            dr.text((x + 6, y + 4), '%s  %s  eye (%.1f, %.1f, %.1f)' % (e['id'], e['name'], e['pos'][0], e['pos'][1], e['pos'][2]), fill=(0, 0, 0))
        y += VH + pad + 14
    # plan (top view), before | after
    PH = 320
    for col, (rocks, top) in enumerate(((before, None), (after, None))):
        x = pad + col * (VW + pad); dr.rectangle([x, y, x + VW, y + PH], fill=(236, 233, 222), outline=(40, 40, 40))
        x0w, x1w = g.x0 + 12, g.x0 + g.nx * g.c - 12; sc = VW / (x1w - x0w); zc = (g.z0 + g.nz * g.c * 0.5)

        def pt(wx, wz): return (x + (wx - x0w) * sc, y + PH * 0.5 - (wz - zc) * sc)
        subs = rasters(S, rocks, mt); tp = lib.combine(g, [s0 for s0, _ in subs]); hh = np.where(np.isfinite(tp), tp - g.ground, np.nan)
        for i in range(0, g.nz):
            for j in range(0, g.nx):
                if np.isfinite(hh[i, j]):
                    v = int(max(40, 215 - 17 * max(0.0, hh[i, j]))); p = pt(g.X[i, j], g.Z[i, j])
                    if x < p[0] < x + VW - 2 and y < p[1] < y + PH - 2: dr.rectangle([p[0], p[1], p[0] + sc * g.c, p[1] + sc * g.c], fill=(v, v, v - 6))
        for c in D['cores']:
            a = math.radians(c['yaw']); cx, cz_ = math.cos(a), -math.sin(a); wx, wz = math.sin(a), math.cos(a); l2, t2 = c['size'][0] * 0.5, c['size'][2] * 0.5
            dr.polygon([pt(c['x'] + cx * p + wx * q, c['z'] + cz_ * p + wz * q) for p, q in ((-l2, -t2), (l2, -t2), (l2, t2), (-l2, t2))], outline=(200, 40, 40))
        dr.polygon([pt(*c_) for c_ in cs], outline=(30, 60, 200))
        for pd in D['stone']['pads']:
            a = math.radians(pd['yaw']); h2 = pd['size'][0] * 0.5
            dr.polygon([pt(pd['x'] + math.cos(a) * p + math.sin(a) * q, pd['z'] - math.sin(a) * p + math.cos(a) * q) for p, q in ((-h2, -h2), (h2, -h2), (h2, h2), (-h2, h2))], outline=(30, 140, 200))
        for l in L['lanes']: dr.line([pt(*l['a']), pt(*l['b'])], fill=(20, 150, 60), width=2)
        for k in range(0, len(S.cut), 8): p = pt(*S.cut[k]); dr.point(p, fill=(120, 80, 20))
        for e in L['eyes']:
            p = pt(e['pos'][0], e['pos'][2])
            if x < p[0] < x + VW and y < p[1] < y + PH: dr.text(p, e['id'], fill=(0, 0, 160))
        dr.text((x + 6, y + 4), 'plan (north up): rock height over the ground (darker = higher), red = the 20 cores (unchanged), blue = stone / pads, green = tail lanes, brown = the cut', fill=(0, 0, 0))
    y += PH + pad
    # skyline profiles
    GH = 240; cols = (('A2', (200, 60, 40)), ('A3', (40, 90, 200)), ('A4', (30, 150, 70)))
    for col, M in enumerate((Mb, Ma)):
        x = pad + col * (VW + pad); dr.rectangle([x, y, x + VW, y + GH], fill=(255, 255, 255), outline=(40, 40, 40))
        s0 = S.end_w - 12.0; s1 = S.end_e + 12.0; hmax = 11.0

        def gp(s_, h_): return (x + (s_ - s0) / (s1 - s0) * VW, y + GH - 16 - h_ / hmax * (GH - 30))
        for h_ in range(0, 11, 2): dr.line([gp(s0, h_), gp(s1, h_)], fill=(225, 225, 225)); dr.text((x + 2, gp(s0, h_)[1] - 10), '%d m' % h_, fill=(120, 120, 120))
        for s_ in (S.end_w, S.end_e, S.stone_s - 2, S.stone_s + 2): dr.line([gp(s_, 0), gp(s_, hmax)], fill=(200, 200, 200))
        txt = []
        for eid, c_ in cols:
            row = M['skyline'].get(eid)
            if not row or 'profile' not in row: continue
            seg = []
            for k, h_ in enumerate(row['profile'] + [None]):
                if h_ is None:
                    if len(seg) > 1: dr.line(seg, fill=c_, width=2)
                    seg = []
                else: seg.append(gp(row['s0'] + 0.5 * k, h_))
            txt.append('%s std %.2f m (min %.2f, max %.2f)' % (eid, row['std_m'] or 0, row['min_m'] or 0, row['max_m'] or 0))
        dr.text((x + 44, y + 4), 'top line over the ground of the band line, by station W -> E (0.5 m): ' + ';  '.join(txt), fill=(0, 0, 0))
        dr.line([gp(M['plan']['s0'] + 0.5 * k, h_) for k, h_ in enumerate(M['plan']['profile'])], fill=(0, 0, 0), width=1)
        dr.text((x + 44, y + 32), 'black = square-on profile: std %.2f m, breaks under %.1f m: %d, anchors over %.1f m: %d   (a gap in a coloured line = hidden behind the slope from that eye)' % (M['plan']['std_m'], L['checks']['break_under_m'], len(M['plan']['breaks']), L['checks']['tor_over_m'], len(M['plan']['tors'])), fill=(0, 0, 0))
        dr.text((x + 44, y + 18), 'spacing CV %.2f, runs of %d within %d %%: %d, LOD0 triangles %d' % (M['band']['spacing_cv'], L['checks']['run_len'], round(L['checks']['run_within'] * 100), len(M['band']['runs']), M['triangles']['lod0']), fill=(0, 0, 0))
    os.makedirs(os.path.dirname(path), exist_ok=True); img.save(path)


def main():
    ap = argparse.ArgumentParser(); ap.add_argument('--data', default=DATA); ap.add_argument('--base', default=BASE); ap.add_argument('--out', default=OUT)
    ap.add_argument('--picture', default=PICTURE); ap.add_argument('--no-picture', action='store_true'); ap.add_argument('--with-base', action='store_true'); ap.add_argument('--r2', default=R2); ap.add_argument('--cache', default='')
    args = ap.parse_args(); sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')
    L = json.load(open(args.data, encoding='utf-8')); D = json.load(open(args.base, encoding='utf-8')); ck = L['checks']; mt = lib.mesh_table(L)
    L2 = json.load(open(args.r2, encoding='utf-8')) if os.path.isfile(args.r2) else None; rv = L.get('rev'); CACHE['dir'] = args.cache or None
    R = dict(data=args.data.replace('\\', '/'), data_sha256=sha(args.data), base=args.base.replace('\\', '/'), base_sha256=sha(args.base), height_sha256=sha(dry.HEIGHT))
    fails = []; T = []

    def say(s): T.append(s); print(s)

    def check(ok, text):
        say(('PASS ' if ok else 'FAIL ') + text)
        if not ok: fails.append(text)

    say('amneung308_look_dry  data ' + R['data'] + ' sha ' + R['data_sha256'][:12] + ' version ' + L['version'] + '  base sha ' + R['base_sha256'][:12] + '  height sha ' + R['height_sha256'][:12])
    # ---------------- K. the collision is not in this data ----------------
    say('-- K. cores / stone / pads are not part of the look pass [O]')
    b = L['base']
    check(R['base_sha256'] == b['sha256'], 'K1 the base data read here is the one the look was derived from (sha %s; pinned %s)' % (R['base_sha256'][:12], b['sha256'][:12]))
    check(jsha(D['cores']) == b['cores_sha256'] and len(D['cores']) == 20, 'K2 the %d cores of the base data hash to the pinned value %s (positions, yaw, sizes, tops: byte-identical input of the NavMesh bake)' % (len(D['cores']), b['cores_sha256'][:12]))
    check(jsha(D['stone']) == b['stone_sha256'], 'K3 the stone / pads / site of the base data hash to the pinned value %s' % b['stone_sha256'][:12])
    keys = set(L.keys()); bad_keys = sorted(keys & {'cores', 'stone', 'vegetation', 'assets', 'line'})
    check(not bad_keys and L['holder'] == 'Rocks' and L['root'] == D['root'] and L['group'] != D['group'], 'K4 the look data carries no core / stone / vegetation block (%s); it writes %s/%s only, ledger group %s (base group %s)' % (bad_keys or 'none', L['root'], L['holder'], L['group'], D['group']))
    coll = [r['id'] for r in L['rocks'] if r.get('collider')]
    check(not coll, 'K5 no rock asks for a collider (%d)' % len(coll))
    refuse = []
    for k_ in ('root', 'holder', 'group', 'material'):
        if not L.get(k_): refuse.append(k_ + ' missing')
    if not L.get('rocks') or not L.get('meshes') or len(L.get('lod_screen') or []) != 2: refuse.append('rocks / meshes / lod_screen incomplete')
    elif not (0 < L['lod_screen'][1] < L['lod_screen'][0] < 1): refuse.append('lod_screen must be 0 < far < near < 1')
    if len({m['key'] for m in L['meshes']}) != len(L['meshes']) or len({r['id'] for r in L['rocks']}) != len(L['rocks']): refuse.append('mesh keys / rock ids not unique')
    for r in L['rocks']:
        q = r.get('q') or []; sc = r.get('scale') or []
        if len(q) != 4 or len(sc) != 3 or any(not v > 0 for v in sc): refuse.append('%s: needs q [x, y, z, w] and scale [x, y, z] > 0' % r['id']); continue
        n = math.sqrt(sum(float(np.float32(v)) ** 2 for v in q))
        if abs(n - 1) > 1e-3: refuse.append('%s: quaternion is not unit (%.4f)' % (r['id'], n))
        if r['mesh'] not in mt: refuse.append('%s: unknown mesh %s' % (r['id'], r['mesh']))
        if max(abs(float(np.float32(r[k_])) - r[k_]) for k_ in ('x', 'y', 'z')) > ck['trs_tol_m'] * 0.1: refuse.append('%s: position does not survive float32 within a tenth of trs_tol_m' % r['id'])
    for k_ in ('trs_tol_m', 'rot_tol_deg', 'raster_cell_m', 'v10_low_rock_m', 'face_h_m', 'sunk_under_m'):
        if not ck.get(k_, 0) > 0: refuse.append('checks.%s is not > 0' % k_)
    for m in L['meshes']:
        for p_ in (m['lod0'], m['lod1']):
            if any(t_ in p_ for t_ in PROTECTED): refuse.append('mesh under a protected tree: ' + p_)
            if not os.path.isfile(lib.PROJECT + p_) or not os.path.isfile(lib.PROJECT + p_ + '.meta'): refuse.append('mesh asset or its .meta missing: ' + p_)
    R['refusals'] = refuse
    check(not refuse, 'K7 the refusals of the editor\'s LoadLook / SurveyLook on this data (root / holder / group / material; lod_screen; unique mesh keys and rock ids; per rock q unit within 1e-3 as float32, scale > 0, a known mesh, a position that survives float32; checks{} complete incl. sunk_under_m; both LOD assets of every mesh exist with their .meta outside the protected trees): %d%s' % (len(refuse), '' if not refuse else ' ' + str(refuse[:4])))
    dk = D['checks']
    same = dk['fit_low_rock_m'] == ck['v10_low_rock_m'] and dk['fit_face_h_m'] == ck['face_h_m'] and dk['raster_cell_m'] == ck['raster_cell_m'] and dk['fit_low_cells_max'] == 0 and dk['fit_bare_points_max'] == 0
    check(same, 'K8 the editor\'s V10 reads its numbers from the BASE data checks{}: fit_low_rock_m %s / fit_face_h_m %s / raster_cell_m %s / limits %s low cells, %s bare points - the look data judges here with v10_low_rock_m %s / face_h_m %s / raster_cell_m %s / limits 0, 0 (must be the same)' %
          (dk['fit_low_rock_m'], dk['fit_face_h_m'], dk['raster_cell_m'], dk['fit_low_cells_max'], dk['fit_bare_points_max'], ck['v10_low_rock_m'], ck['face_h_m'], ck['raster_cell_m']))
    check(len(D['rocks']) == b['rocks'], 'K9 the base data has %d rocks, the look expects %d (SurveyLook blocks otherwise: rocks-apply replaces exactly that set, rocks-revert rebuilds it)' % (len(D['rocks']), b['rocks']))
    if any(x.startswith('checks.') or x.startswith('mesh asset') or 'incomplete' in x or 'unknown mesh' in x or 'needs q' in x or x.endswith(' missing') for x in refuse):
        say('INFO the editor could not load or judge this data (K7): nothing else is measured here')
        R['fails'] = fails; R['verdict'] = 'DRY FAIL'; say('== %s: %d fail(s)' % (R['verdict'], len(fails)))
        os.makedirs(args.out, exist_ok=True)
        json.dump(json.loads(json.dumps(R)), open(os.path.join(args.out, 'amneung308_look_dry.json'), 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
        open(os.path.join(args.out, 'amneung308_look_dry.txt'), 'w', encoding='utf-8').write('\n'.join(T) + '\n')
        return 1
    if args.with_base:
        p = subprocess.run([sys.executable, HERE + '/amneung308_dry.py', '--data', args.base, '--out', args.out + '/base'], capture_output=True, text=True, encoding='utf-8')
        tail = [l for l in p.stdout.splitlines() if l.startswith(('PASS B9', 'PASS B10', 'FAIL', '== '))]
        bf = [l for l in tail if l.startswith('FAIL')]; veg = [l for l in bf if l.startswith('FAIL D2 ')]
        R['base_dry'] = dict(exit=p.returncode, lines=tail)
        check(bool(tail) and len(bf) == len(veg), 'K6 the base dry on the unchanged base data (reach A1-A9, B1-B8, ballistic B9, 국 cap B10, EA E1-E4): %s; fails that are not D2 %d%s' %
              (tail[-1] if tail else 'no output', len(bf) - len(veg), '' if not veg else ' (D2 = the 14 tree rows are no longer in the sheet: Amneung308 veg-apply already ran in the editor - the expected state after deploy)'))
        for l in tail[:-1]: say('     ' + l)
    S = Scene(D, L)
    before = [lib.base_rock_as_look(r) for r in D['rocks']]; after = L['rocks']
    Mb, _, _, _ = measure(S, L, before, mt, ck, 'before')
    Ma, subs, top0, top1 = measure(S, L, after, mt, ck, 'after')
    R['before'] = Mb; R['after'] = Ma
    Mr = None
    if L2 is not None and (rv or 'v3_exact' in ck):
        mt2 = lib.mesh_table(L2); Mr, subs2, top0r, top1r = measure(S, L, L2['rocks'], mt2, ck, 'look.2'); R['look2'] = Mr; R['look2_sha256'] = sha(args.r2)
    g = S.g
    # ---------------- C. core-fit ----------------
    say('-- C. core-fit of the new rocks against the unchanged cores (real LOD0 and LOD1 meshes, %.2f m raster) [O]' % g.c)
    for lod in ('lod1', 'lod0'):
        f = Ma['fit'][lod]; fb = Mb['fit'][lod]
        check(f['v10_low_cells'] == 0, 'C1 %s V10: core cells where the rock is lower than the core AND lower than ground + %.2f m: %d of %d (before %d)' % (lod, ck['v10_low_rock_m'], f['v10_low_cells'], f['core_cells'], fb['v10_low_cells']))
        check(f['v10_margin_fail_cells'] == 0, 'C1m %s V10 with margin: rock >= min(core top, ground + %.2f + %.2f ground uncertainty) + %.2f at every core cell: %d failing (before %d); smallest slack over the plain rule %.2f m (before %.2f)' %
              (lod, ck['v10_low_rock_m'], ck['ground_unc_m'], ck['v10_margin_m'], f['v10_margin_fail_cells'], fb['v10_margin_fail_cells'], f['v10_slack_min_m'], fb['v10_slack_min_m']))
        check(f['bare_face_points'] == 0, 'C2 %s core face points with no rock %.1f m above the ground: %d of %d (before %d)' % (lod, ck['face_h_m'], f['bare_face_points'], f['face_points'], fb['bare_face_points']))
        if L['cover']['reach_rule']:
            check(f['reach_wall_cells'] == 0, 'C3 %s reach rule: core cells where the rock is lower than the core AND within what a 국 deck + running jump + step reaches (+ %.2f): %d (before %d) - an invisible wall over a top that looks reachable' %
                  (lod, ck['ground_unc_m'] + ck['reach_margin_m'], f['reach_wall_cells'], fb['reach_wall_cells']))
            say('INFO C3x %s next to the stone (within %.1f m of its box, %d core cells) the reach rule is not asked - a rock face cannot both clear the deck and stand full height 0.3 m beside it: %d cells there are lower than the reach (before %d); V10 holds there (C1)' %
                (lod, L['cover']['reach_exempt_stone_m'], f['exempt_core_cells'], f['reach_wall_exempt_cells'], fb['reach_wall_exempt_cells']))
        else: say('INFO C3 %s reach rule is off in the data: %d core cells are lower than the core and inside the 국 + jump reach (before %d)' % (lod, f['reach_wall_cells'], fb['reach_wall_cells']))
        e = f['eye_depth']; eb = fb['eye_depth']
        check(e['mean_m'] <= ck['eye_depth_mean_max_m'], 'C0 %s eye (ground + %.1f m) under the visible rock outside the collider face: mean %.2f m over %d faces <= %.2f (before %.2f)' % (lod, dry.EYE_M, e['mean_m'], e['faces'], ck['eye_depth_mean_max_m'], eb['mean_m']))
        check(e['max_m'] <= ck['eye_depth_face_max_m'], 'C0 %s deepest face %.2f m <= %.2f (before %.2f); worst faces %s' % (lod, e['max_m'], ck['eye_depth_face_max_m'], eb['max_m'], e['worst']))
        say('INFO   %s rock crest over the ground on the core cells: min %.2f / median %.2f / max %.2f m (before %.2f / %.2f / %.2f); core cells where the rock is lower than the core top: %d of %d (before %d) - the stricter reading "max(core top, ...)" of the brief is not met by either and is not what verify V10 asks' %
            (lod, f['crest_over_ground_m']['min'], f['crest_over_ground_m']['median'], f['crest_over_ground_m']['max'], fb['crest_over_ground_m']['min'], fb['crest_over_ground_m']['median'], fb['crest_over_ground_m']['max'], f['below_core_top_cells'], f['core_cells'], fb['below_core_top_cells']))
    ve = ck['v10_exact']; xb = Mb['v10_exact']; xa = Ma['v10_exact']
    check(xb['lod1']['cells'] == ve['editor_before_cells'] and xb['lod1']['low'] == ve['editor_before_low'], 'C1v self-check: the editor\'s own V10 sampling on the 25 present rocks gives %d of %d low cells (LOD1) - the editor measured %d of %d [M]: the offline model reproduces the editor failure (review F5)' %
          (xb['lod1']['low'], xb['lod1']['cells'], ve['editor_before_low'], ve['editor_before_cells']))
    for lod in ('lod1', 'lod0'):
        check(xa[lod]['low'] == 0 and xa[lod]['slack_min_m'] >= ve['slack_min_m'] - 1e-9, 'C1v %s V10 by the editor\'s sampling (%d cells, exact triangles): low cells %d (before %d%s), smallest slack %+.2f m >= %.2f (before %+.2f%s)' %
              (lod, xa[lod]['cells'], xa[lod]['low'], xb[lod]['low'], '' if Mr is None else ', look.2 %d' % Mr['v10_exact'][lod]['low'], xa[lod]['slack_min_m'], ve['slack_min_m'], xb[lod]['slack_min_m'], '' if Mr is None else ', look.2 %+.2f - the editor measured %s [M]' % (Mr['v10_exact'][lod]['slack_min_m'], ve.get('editor_r2_slack_m', {}).get(lod, '-'))))
    if 'face_points' in ve:
        check(xb['lod1']['face_points'] == ve['face_points'] and xb['lod1']['bare'] == ve['editor_before_bare'], 'C2v self-check: the editor\'s V10 face points on the 25 present rocks: %d bare of %d (LOD1) - the editor measured %d of %d [M]' % (xb['lod1']['bare'], xb['lod1']['face_points'], ve['editor_before_bare'], ve['face_points']))
        for lod in ('lod1', 'lod0'):
            check(xa[lod]['bare'] == 0 and xa[lod]['face_slack_min_m'] >= ve['face_slack_min_m'] - 1e-9, 'C2v %s V10 face points by the editor\'s sampling (%d points, exact triangles): no rock %.1f m above the ground at %d (before %d%s); smallest slack %+.2f m >= %.2f' %
                  (lod, xa[lod]['face_points'], ck['face_h_m'], xa[lod]['bare'], xb[lod]['bare'], '' if Mr is None else ', look.2 %d' % Mr['v10_exact'][lod]['bare'], xa[lod]['face_slack_min_m'], ve['face_slack_min_m']))
    # ---------------- S. silhouette ----------------
    say('-- S. silhouette [O]')
    judged = [e['id'] for e in L['eyes'] if e.get('judge')]
    for eid in [e['id'] for e in L['eyes']]:
        a = Ma['skyline'][eid]; b_ = Mb['skyline'][eid]
        if 'std_m' not in a: say('INFO S1 eye %s: %s' % (eid, a.get('note'))); continue
        text = 'S1 eye %s: top line over the band ground, %d of %d stations seen: std %.2f m (before %.2f), min %.2f, max %.2f (before %.2f .. %.2f)' % (eid, a['visible'], a['stations'], a['std_m'] or 0, b_['std_m'] or 0, a['min_m'] or 0, a['max_m'] or 0, b_['min_m'] or 0, b_['max_m'] or 0)
        if eid in judged:
            check(a['std_m'] is not None and a['std_m'] >= ck['skyline_std_min_m'], text + ' >= %.2f' % ck['skyline_std_min_m'])
            check(len(a['breaks']) >= ck['breaks_eye_min'], 'S2 eye %s: breaks seen (top line under %.1f m for at least %.1f m): %d %s (need %d from an eye - an oblique eye hides some; before %d)' % (eid, ck['break_under_m'], ck['break_min_len_m'], len(a['breaks']), [[round(x, 1) for x in q] for q in a['breaks']], ck['breaks_eye_min'], len(b_['breaks'])))
            check(len(a['tors']) >= 2, 'S3 eye %s: anchors (top line over %.1f m): %d stretches %s' % (eid, ck['tor_over_m'], len(a['tors']), a['tors']))
        else: say('INFO ' + text)
    pa, pb = Ma['plan'], Mb['plan']
    check(len(pa['breaks']) >= ck['breaks_min'] and len(pa['tors']) >= 3 and pa['std_m'] >= ck['skyline_std_min_m'],
          'S2 square-on profile (highest rock over the ground per 0.5 m of station): %d breaks under %.1f m %s, %d anchors over %.1f m, std %.2f m, %.2f .. %.2f m (need >= %d breaks, >= 3 anchors, std >= %.1f; before: %d breaks, %d anchor stretches, std %.2f, %.2f .. %.2f)' %
          (len(pa['breaks']), ck['break_under_m'], [[round(x, 1) for x in q] for q in pa['breaks']], len(pa['tors']), ck['tor_over_m'], pa['std_m'], pa['min_m'], pa['max_m'], ck['breaks_min'], ck['skyline_std_min_m'], len(pb['breaks']), len(pb['tors']), pb['std_m'], pb['min_m'], pb['max_m']))
    bd = Ma['band']; bb = Mb['band']
    check(not bd['runs'], 'S4 no run of %d neighbouring rocks with crests within %d %%: %d run(s) %s (before %d of %d windows)' % (ck['run_len'], round(ck['run_within'] * 100), len(bd['runs']), bd['runs'][:3], len(bb['runs']), max(0, len(bb['rocks']) - ck['run_len'] + 1)))
    check(bd['spacing_cv'] >= ck['spacing_cv_min'], 'S5 spacing of the %d band rocks along the line (tails left out): mean %.2f m, %.2f .. %.2f m, CV %.2f >= %.2f (before: %d rocks, mean %.2f m, CV %.2f)' %
          (len(bd['rocks']), bd['spacing_mean_m'], bd['spacing_min_m'], bd['spacing_max_m'], bd['spacing_cv'], ck['spacing_cv_min'], len(bb['rocks']), bb['spacing_mean_m'], bb['spacing_cv']))
    size = lambda r: (r['along_m'] * r['across_m'] * r['crest_m']) ** (1 / 3.0)            # what shows: along x across x crest
    med = float(np.median([size(r) for r in after if r['role'] == 'spine'])); vc = np.array([size(r) for r in after]) / med
    bsz = np.array([(r['scale'][0] * r['scale'][2] * (r['y'] + r['scale'][1] - r['ground_y'])) ** (1 / 3.0) for r in D['rocks']]); bsz = bsz / np.median(bsz)
    cls = dict(anchor=int((vc >= 1.3).sum()), medium=int(((vc < 1.3) & (vc >= 0.75)).sum()), small=int(((vc < 0.75) & (vc >= 0.3)).sum()), foot=int((vc < 0.3).sum()))
    R['size_classes'] = dict(median_spine_m=round(med, 2), min_x=round(float(vc.min()), 2), max_x=round(float(vc.max()), 2), classes=cls, before_min_x=round(float(bsz.min()), 2), before_max_x=round(float(bsz.max()), 2))
    check(vc.min() <= ck['scale_class_spread'][0] + 1e-9 and vc.max() >= ck['scale_class_spread'][1] - 1e-9 and cls['anchor'] >= 3 and cls['small'] + cls['foot'] >= 8,
          'S6 size spread (cube root of along x across x crest, over the median spine block %.2f m): %.2fx .. %.2fx (need <= %.1fx and >= %.1fx); anchors (>= 1.3x) %d, medium %d, small %d, at the feet (< 0.3x) %d (the 25 present rocks: %.2fx .. %.2fx)' %
          (med, vc.min(), vc.max(), ck['scale_class_spread'][0], ck['scale_class_spread'][1], cls['anchor'], cls['medium'], cls['small'], cls['foot'], bsz.min(), bsz.max()))
    used = {}
    for r in after: used[r['mesh']] = used.get(r['mesh'], 0) + 1
    R['meshes_used'] = used
    check(len(used) >= 4, 'S7 meshes used: %s (before: massif x %d)' % (', '.join('%s x %d' % kv for kv in sorted(used.items())), len(before)))
    dips = [r['dip_deg'] for r in after if r['role'] in ('spine', 'tor', 'plug', 'buttress')]; az = [r['dip_az_deg'] for r in after if r['role'] in ('spine', 'tor', 'plug', 'buttress')]
    R['dip'] = dict(az_min=min(az), az_max=max(az), dip_min=min(dips), dip_max=max(dips))
    check(max(az) - min(az) <= 30.0 + 1e-6 and min(dips) > 0, 'S8 common dip: lean %.1f .. %.1f deg toward azimuth %.0f .. %.0f deg (a %.0f deg fan; before: 0 lean, yaw only)' % (min(dips), max(dips), min(az), max(az), max(az) - min(az)))
    # ---------------- T. look review (fix 2) ----------------
    say('-- T. look review: plan outline, stretch, ends [O]')
    oa, ob = Ma['outline'], Mb['outline']
    check(oa['north_std_m'] >= ck['plan_edge_std_min_m'] and oa['south_std_m'] >= ck['plan_edge_std_min_m'] and oa['width_cv'] >= ck['plan_width_cv_min'],
          'T1 from above (rock over %.1f m, per metre of station, %d stations): north edge std %.2f m / south edge std %.2f m (need >= %.1f; before %.2f / %.2f), reaching %.1f / %.1f m off the line; footprint width mean %.2f m, CV %.2f (need >= %.2f; before %.2f m, CV %.2f)' %
          (ck['plan_rock_over_m'], oa['stations'], oa['north_std_m'], oa['south_std_m'], ck['plan_edge_std_min_m'], ob['north_std_m'], ob['south_std_m'], oa['north_max_m'], oa['south_max_m'], oa['width_mean_m'], oa['width_cv'], ck['plan_width_cv_min'], ob['width_mean_m'], ob['width_cv']))
    def stretch_of(r):
        m = mt[r['mesh']]; size = np.array(m['bounds_max']) - np.array(m['bounds_min']); return float((r['scale'][1]) / math.sqrt(r['scale'][0] * r['scale'][2]) * 1.0) if True else 0
    def rel_stretch(r):
        m = mt[r['mesh']]; size = np.array(m['bounds_max']) - np.array(m['bounds_min']); nat = size[1] / math.sqrt(size[0] * size[2])
        return float(r['scale'][1] / math.sqrt(r['scale'][0] * r['scale'][2]))          # the scale triple is already relative to the mesh
    capped = [r for r in after if r.get('capped')]; old = [r for r in after if not r.get('capped')]
    over = [(r['id'], r['mesh'], round(rel_stretch(r), 2)) for r in capped if rel_stretch(r) > L['stretch_max'][r['mesh']] + 0.02]
    check(not over and len(capped) > 0, 'T2 stretch (scale y over the mean horizontal scale) of the %d rocks added by the review pass: every one within its mesh cap %s; %d over %s' % (len(capped), L['stretch_max'], len(over), over[:3]))
    old = [r for r in old if not r.get('r3')]
    so = np.array([rel_stretch(r) for r in old]); n3 = int((so > 3.0).sum()); n23 = int((so > 2.3).sum())
    R['stretch'] = dict(added=len(capped), look1=len(old), look1_over_2_3=n23, look1_over_3=n3, look1_median=round(float(np.median(so)), 2), look1_max=round(float(so.max()), 2))
    if rv:
        s2 = np.array([rel_stretch(r) for r in L2['rocks'] if not r.get('capped')]) if L2 is not None else np.zeros(1)
        say('OPEN T2x stretch of the %d look.1 rocks that are still in the band: %d over 2.3, %d over 3.0 (median %.2f, max %.2f); look.2 had %d over 2.3, %d over 3.0 of %d. Revision 3 took the upright ones out only where the eye is close (N2); the others are reported, not hidden' %
            (len(old), n23, n3, float(np.median(so)), float(so.max()), int((s2 > 2.3).sum()), int((s2 > 3.0).sum()), len(s2)))
        say('INFO T3 is replaced by N3 for a revision (rev block): the kept rocks are compared with look.2 itself, rock by rock')
    else:
        say('OPEN T2x stretch of the %d look.1 rocks is NOT reduced: %d over 2.3, %d over 3.0 (median %.2f, max %.2f) - the upright fillers the look review named. Four other builds were tried and failed V10 / eye depth / budget (params stretch_open); reported, not hidden' % (len(old), n23, n3, float(np.median(so)), float(so.max())))
        hsh, nold = look1_sha(after)
        check(nold == ck['look1_rocks'] and hsh == ck['look1_rocks_sha256'], 'T3 the %d rocks of look.1 (the proven core fit) are byte-identical: transform hash %s (pinned %s) - the review pass only appended' % (nold, hsh[:12], ck['look1_rocks_sha256'][:12]))
    aps = [r for r in after if r['role'] == 'apron']; ah = max([r['crest_m'] for r in aps] or [0]); sides = [sum(1 for r in aps if r.get('side') == k_ and not r.get('companion')) for k_ in ('north', 'south')]
    check(len(aps) > 0 and ah <= ck['apron_h_max_m'] and ah < dry.EYE_M and min(sides) >= 4, 'T4 aprons: %d rocks (ledges north %d / south %d), highest %.2f m <= %.2f and under the eye (%.1f m): a body is not expected to be stopped by them (no collider, like the talus)' % (len(aps), sides[0], sides[1], ah, ck['apron_h_max_m'], dry.EYE_M))
    ea, eb_ = Ma['end_drops'], Mb['end_drops']
    check(all(ea[k_]['max_drop_m'] <= ck['end_drop_max_m'] for k_ in ea), 'T5 ends: biggest fall of the top line over one metre within %.0f m outside a core end: west %.2f m %s / east %.2f m %s (<= %.1f; before %.2f / %.2f)' %
          (ck['end_drop_len_m'], ea['west']['max_drop_m'], ea['west']['profile'], ea['east']['max_drop_m'], ea['east']['profile'], ck['end_drop_max_m'], eb_['west']['max_drop_m'], eb_['east']['max_drop_m']))
    # ---------------- N. look revision 3 ----------------
    if rv:
        say('-- N. look revision 3 (%s edits %s): the reworked stretches, judged against look.2 [O]' % (L['version'], rv.get('of')))
        nr = ck['near']; st = D['stone']; num = lambda i: int(i.split('_')[1])
        if L2 is None or Mr is None: check(False, 'N0 look.2 (%s) is missing: a revision cannot be judged without the revision it edits' % args.r2.replace('\\', '/'))
        else:
            check(sha(args.r2) == ck['r2']['sha256'] and len(L2['rocks']) == ck['r2']['rocks'] and L2['version'] == rv['of'],
                  'N0 look.2 read from %s: sha %s (pinned %s = the data the editor run of 2026-10-05 applied and judged [M]), %d rocks, version %s' % (os.path.basename(args.r2), sha(args.r2)[:12], ck['r2']['sha256'][:12], len(L2['rocks']), L2['version']))
            r2 = {r['id']: r for r in L2['rocks']}; now = {r['id']: r for r in after}
            gone = sorted(set(r2) - set(now)); new_ = sorted(set(now) - set(r2)); diff = sorted(i for i in r2 if i in now and now[i] != r2[i])
            ok3 = gone == sorted(rv['removed']) and diff == sorted(rv['changed']) and new_ == sorted(rv['added']) and all(now[i].get('r3') for i in new_) and not any(now[i].get('r3') for i in r2 if i in now) and len(now) - len(new_) - len(diff) == rv['kept']
            check(ok3, 'N3 against look.2 rock by rock (every field): %d rocks byte-identical, id included; %d taken out, %d changed %s, %d new - exactly what the rev block says (%d kept / %d removed / %d changed / %d added)' %
                  (len(now) - len(new_) - len(diff), len(gone), len(diff), sorted(diff, key=num), len(new_), rv['kept'], len(rv['removed']), len(rv['changed']), len(rv['added'])))
            zone = lambda r: lib.box_distance(r['ground_x'], r['ground_z'], st)
            west_keep = lambda r: zone(r)[1] < 0 and zone(r)[0] > nr['west_keep_beyond_m']
            keepers = [i for i, r in r2.items() if west_keep(r)]; touched = sorted([i for i in keepers if i in gone or i in diff], key=num)
            new_west = sorted([i for i in new_ if west_keep(now[i])], key=num)
            check(not touched, 'N3k west keep zone (rocks more than %.0f m west of the stone box): %d look.2 rocks, changed or removed %d %s%s' %
                  (nr['west_keep_beyond_m'], len(keepers), len(touched), touched, '' if not new_west else '; NEW rocks set down there (no kept rock touched): %s' % ['%s %s' % (i, now[i].get('key', '')) for i in new_west]))
            R['rev_lists'] = dict(removed=sorted(gone, key=num), changed=sorted(diff, key=num), added=sorted(new_, key=num), west_keep=len(keepers), new_in_west_keep=new_west)
            fb = rv.get('first_build')
            if fb:
                fp = STAGE + '/' + fb['file']; ok_f = os.path.isfile(fp) and sha(fp) == fb['sha256']; rows_a = {r_['id']: r_ for r_ in json.load(open(fp, encoding='utf-8'))['rocks']} if ok_f else {}
                flagged = {r['id']: r.get('first_build') for r in after if r.get('first_build')}
                off_ = sorted([i for i, j in fb['kept'].items() if i not in now or j not in rows_a or any(now[i][k_] != rows_a[j][k_] for k_ in ('mesh', 'role', 'x', 'y', 'z', 'q', 'scale'))], key=num)
                check(ok_f and not off_ and flagged == fb['kept'] and len(fb['kept']) > 0, 'N3b the rocks kept from the first build of revision 3 (%s, sha %s; pinned %s): %d rocks, mesh / role / position / rotation / scale identical to it: %d off %s (the review judged that build; only the east stretch behind the deck was built anew)' %
                      (fb['file'], sha(fp)[:12] if os.path.isfile(fp) else 'missing', fb['sha256'][:12], len(fb['kept']), len(off_), off_[:4] if off_ else ''))
            # N1: no narrow spike near the platform
            def spikes(rocks):
                return sorted([(r['id'], r['role'], r['mesh'], round(r['crest_m'], 2), round(r['along_m'], 2), round(r['crest_m'] / r['along_m'], 2)) for r in rocks if zone(r)[0] <= nr['platform_m'] and r['crest_m'] > nr['spike_crest_over_m'] and r['crest_m'] / r['along_m'] > nr['spike_aspect_max'] + 1e-9], key=lambda a: -a[5])
            sp, sp2 = spikes(after), spikes(L2['rocks'])
            check(not sp, 'N1 within %.0f m of the stone box no rock that stands over the eye (%.1f m) is a narrow spike (crest over length along the band > %.2f): %d %s (look.2: %d %s)' %
                  (nr['platform_m'], nr['spike_crest_over_m'], nr['spike_aspect_max'], len(sp), sp[:3] if sp else '', len(sp2), [(a[0], a[5]) for a in sp2]))
            # N2: stretch cap where the eye is close
            def in_zone(r):
                d_, u_ = zone(r); return d_ <= nr['path_m'] and not (u_ < 0 and d_ > nr['west_keep_beyond_m'])
            def shape(r):
                """(measure, cap): scale y over the mean horizontal scale against stretch_max; a stone that stands on its long axis (stand:
                true, mesh z up) has no 'upright' scale - its largest over its smallest scale against stand_scale_ratio_max"""
                if r.get('stand'): return float(max(r['scale']) / min(r['scale'])), nr.get('stand_scale_ratio_max', nr['stretch_max'])
                return lib.scale_stretch(r), nr['stretch_max']

            def over_cap(rocks, pick):
                return sorted([(r['id'], r['mesh'], round(shape(r)[0], 2)) for r in rocks if pick(r) and shape(r)[0] > shape(r)[1] + 1e-6], key=lambda a: -a[2])
            stands = [r for r in after if r.get('stand')]; lying = [r['id'] for r in stands if float(lib.qmat(r['q'])[1, 2]) < 0.9]
            oc, oc2 = over_cap(after, in_zone), over_cap(L2['rocks'], in_zone)
            nz = sum(1 for r in after if in_zone(r))
            check(not oc and not lying, 'N2 stretch cap where the eye is close (within %.0f m of the stone box = of the cut through it; the west keep zone left out): scale y over the mean horizontal scale <= %.2f on all %d rocks: %d over %s (look.2: %d of %d over, up to %.2f); of them %d stand on their long axis (mesh z up: largest over smallest scale <= %.2f, up to %.2f; not really upright: %d %s)' %
                  (nr['path_m'], nr['stretch_max'], nz, len(oc), oc[:3] if oc else '', len(oc2), sum(1 for r in L2['rocks'] if in_zone(r)), oc2[0][2] if oc2 else 0.0,
                   len(stands), nr.get('stand_scale_ratio_max', nr['stretch_max']), max([shape(r)[0] for r in stands] or [0.0]), len(lying), lying or ''))
            wk = over_cap(after, lambda r: zone(r)[0] <= nr['path_m'] and west_keep(r))
            say('OPEN N2x west keep zone, %.0f - %.0f m from the stone box: %d kept look.2 rocks stand more upright than %.2f (up to %.2f) - kept byte-identical on purpose (that half reads well in the stills); reported, not hidden' % (nr['west_keep_beyond_m'], nr['path_m'], len(wk), nr['stretch_max'], wk[0][2] if wk else 0.0))
            R['near'] = dict(spikes=sp, spikes_look2=sp2, over_cap=oc, over_cap_look2=len(oc2), west_keep_over_cap=len(wk))
            # N4: the platform is not dwarfed more than in look.2
            ta, t2 = Ma['near_tops'], Mr['near_tops']
            check(ta['west'] <= t2['west'] + 1e-6 and ta['east'] <= t2['east'] + 1e-6, 'N4 highest rock top within %.0f m of the stone box (stone top y %.2f): west y %.2f (look.2 %.2f), east y %.2f (look.2 %.2f) - not higher than look.2 on either side' % (nr['platform_m'], st['top_y'], ta['west'], t2['west'], ta['east'], t2['east']))
            # N5: the teeth of the top line (near the platform, and the east stretch), heights and the gap of the east stretch
            ea = ck['east']; pa_, p2_ = Ma['plan'], Mr['plan']; a0, a1 = S.stone_s - st['half_m'] - nr['platform_m'], S.stone_s + st['half_m'] + nr['platform_m']
            tn, tn2 = teeth(pa_['profile'], pa_['s0'], a0, a1, nr['tooth_prominence_m'], nr['tooth_width_max_m']), teeth(p2_['profile'], p2_['s0'], a0, a1, nr['tooth_prominence_m'], nr['tooth_width_max_m'])
            te, te2 = teeth(pa_['profile'], pa_['s0'], ea['from_s'], ea['to_s'], nr['tooth_prominence_m'], nr['tooth_width_max_m']), teeth(p2_['profile'], p2_['s0'], ea['from_s'], ea['to_s'], nr['tooth_prominence_m'], nr['tooth_width_max_m'])
            check(len(tn) <= nr['teeth_max'] and len(tn) < max(1, len(tn2)), 'N5a narrow teeth of the square-on top line within %.0f m of the stone box (a top at least %.1f m over its higher valley and under %.1f m wide there): %d %s <= %d and fewer than look.2 (%d %s)' %
                  (nr['platform_m'], nr['tooth_prominence_m'], nr['tooth_width_max_m'], len(tn), [(a[0], a[1]) for a in tn], nr['teeth_max'], len(tn2), [(a[0], a[1]) for a in tn2]))
            check(len(te) <= ea['teeth_max'] and len(te) < max(1, len(te2)), 'N5b east stretch (stations %.1f - %.1f): narrow teeth %d %s <= %d and fewer than look.2 (%d %s)' % (ea['from_s'], ea['to_s'], len(te), [(a[0], a[1]) for a in te], ea['teeth_max'], len(te2), [(a[0], a[1]) for a in te2]))
            def east_shape(pp):
                S_ = pp['s0'] + 0.5 * np.arange(len(pp['profile'])); h = np.array(pp['profile']); m = (S_ >= ea['from_s']) & (S_ <= ea['to_s']); hs = h[m]; ss = S_[m]; gaps = []; k = 0
                while k < len(hs):
                    if hs[k] < ea['gap_under_m']:
                        q = k
                        while q + 1 < len(hs) and hs[q + 1] < ea['gap_under_m']: q += 1
                        if ss[q] - ss[k] >= ea['gap_len_min_m'] - 1e-6:
                            lo = float(hs[k:q + 1].min()); left = h[(S_ >= ss[k] - ea['gap_reach_m']) & (S_ < ss[k])]; right = h[(S_ > ss[q]) & (S_ <= ss[q] + ea['gap_reach_m'])]
                            if left.size and right.size and left.max() >= lo + ea['gap_step_m'] and right.max() >= lo + ea['gap_step_m']: gaps.append([float(ss[k]), float(ss[q]), round(lo, 2), round(float(left.max()), 2), round(float(right.max()), 2)])
                        k = q + 1
                    else: k += 1
                return dict(min_m=round(float(hs.min()), 2), max_m=round(float(hs.max()), 2), factor=round(float(hs.max() / hs.min()), 2), gaps=gaps)
            ev, e2 = east_shape(pa_), east_shape(p2_); R['east'] = dict(after=ev, look2=e2, teeth=te, teeth_look2=te2, near_teeth=tn, near_teeth_look2=tn2)
            check(ev['factor'] >= ea['height_factor_min'] and len(ev['gaps']) >= 1, 'N5c east stretch: top line %.2f .. %.2f m = a factor %.2f (need >= %.2f; look.2 %.2f .. %.2f = %.2f); real gaps (under %.1f m for at least %.1f m, both neighbours within %.0f m at least %.1f m higher): %d %s (need >= 1; look.2 %d %s)' %
                  (ev['min_m'], ev['max_m'], ev['factor'], ea['height_factor_min'], e2['min_m'], e2['max_m'], e2['factor'], ea['gap_under_m'], ea['gap_len_min_m'], ea['gap_reach_m'], ea['gap_step_m'], len(ev['gaps']), ev['gaps'], len(e2['gaps']), e2['gaps']))
            # N6: from above the band's own footprint varies (ledges that touch it)
            ca, c2 = Ma['connected'], Mr['connected']; pt = ck['plan_touch']; comp = connected_band(S, top0, ck)[0]
            touch = [r['id'] for r, (s0_, s1_) in zip(after, subs) if r['role'] == 'apron' and s0_.top.size and (comp[s0_.i0:s0_.i1, s0_.j0:s0_.j1] & np.isfinite(s0_.top)).any()]
            touch2 = 0
            if True:
                comp2 = connected_band(S, top0r, ck)[0]; touch2 = sum(1 for r, (s0_, s1_) in zip(L2['rocks'], subs2) if r['role'] == 'apron' and s0_.top.size and (comp2[s0_.i0:s0_.i1, s0_.j0:s0_.j1] & np.isfinite(s0_.top)).any())
            check(len(touch) >= touch2 + pt['more_min'] and ca['width_cv'] >= c2['width_cv'] * pt['width_cv_gain_min'], 'N6 from above, the footprint that hangs together with the rock on the cores (rock over %.1f m): low ledges that touch it %d (look.2 %d; need %d more); its width per metre of station: mean %.2f m, %.2f .. %.2f m, CV %.3f (look.2 mean %.2f m, CV %.3f; need >= %.2f x), stations wider than 1.3 x the mean %d (look.2 %d)' %
                  (ck['plan_rock_over_m'], len(touch), touch2, pt['more_min'], ca['width_mean_m'], ca['width_min_m'], ca['width_max_m'], ca['width_cv'], c2['width_mean_m'], c2['width_cv'], pt['width_cv_gain_min'], ca['wide_stations'], c2['wide_stations']))
            R['touching_aprons'] = touch
            # N7: beside the deck
            da, d2 = Ma['deck_side'], Mr['deck_side']; dk_ = ck['deck_side']
            check(all(da[k_]['first_min_m'] >= dk_['first_min_over_deck_m'] and da[k_]['first_p10_m'] >= dk_['first_p10_over_deck_m'] and da[k_]['strip_under_eye'] <= d2[k_]['strip_under_eye'] for k_ in da),
                  'N7 beside the deck (the cores are an invisible wall for who stands on the stone): first metre of core outside the stone box - lowest rock top over the deck top west %+.2f / east %+.2f m (need >= %+.2f = more than a jump + a step, so no cell of it can be hopped on; look.2 %+.2f / %+.2f), 10th percentile %+.2f / %+.2f (need >= %+.2f = the eye height; look.2 %+.2f / %+.2f), median %+.2f / %+.2f (look.2 %+.2f / %+.2f); core strip inside the box - cells with rock under deck + %.1f m: west %d / east %d of %d / %d (not more than look.2: %d / %d)' %
                  (da['west']['first_min_m'], da['east']['first_min_m'], dk_['first_min_over_deck_m'], d2['west']['first_min_m'], d2['east']['first_min_m'], da['west']['first_p10_m'], da['east']['first_p10_m'], dk_['first_p10_over_deck_m'], d2['west']['first_p10_m'], d2['east']['first_p10_m'], da['west']['first_median_m'], da['east']['first_median_m'], d2['west']['first_median_m'], d2['east']['first_median_m'],
                   dry.EYE_M, da['west']['strip_under_eye'], da['east']['strip_under_eye'], da['west']['strip_cells'], da['east']['strip_cells'], d2['west']['strip_under_eye'], d2['east']['strip_under_eye']))
            # N7s: the long faces of the cores that stand on the deck (review S2)
            if 'deck_faces' in ck:
                fk = ck['deck_faces']; fa_, f2_ = Ma['deck_faces'], Mr['deck_faces']; names = sorted(fa_); rv2 = fk.get('review_r2')
                if rv2: check(sorted(rv2['lod0']) == names and all(abs(f2_[k_]['lod0'] - rv2['lod0'][k_]) <= rv2['tol'] for k_ in names),
                              'N7r self-check: this measure on look.2 (LOD0) gives %s - the review measured %s with its own code (within %.3f)' % ([f2_[k_]['lod0'] for k_ in names], [rv2['lod0'][k_] for k_ in names], rv2['tol']))
                check(all(fa_[k_][l_] >= fk['inside_share_min'] - 1e-9 for k_ in names for l_ in ('lod0', 'lod1')),
                      'N7s the long faces of the cores that stand on the deck, first %.1f m from the deck end, deck top .. + %.2f m (what a body on the deck or jumping off its corner can touch): share inside a visible rock, LOD0 / LOD1 - %s (need >= %.2f on every face and LOD; look.2: %s) - outside the rock = the invisible face stands before the rock' %
                      (fk['along_m'], fk['over_m'], '; '.join('%s %.2f / %.2f' % (k_.replace('RibCore_', 'core '), fa_[k_]['lod0'], fa_[k_]['lod1']) for k_ in names), fk['inside_share_min'], '; '.join('%s %.2f / %.2f' % (k_.replace('RibCore_', 'core '), f2_[k_]['lod0'], f2_[k_]['lod1']) for k_ in names)))
            # N9: the invisible core against the visible top line (review S1)
            if 'see_over' in ck:
                sk = ck['see_over']; zones = ('east_near', 'east_path', 'west_near', 'west_path'); se = {l_: Ma['v10_exact'][l_]['see'] for l_ in ('lod0', 'lod1')}; s2 = {l_: Mr['v10_exact'][l_]['see'] for l_ in ('lod0', 'lod1')}
                txt = lambda key: '; '.join('%s %s' % (z_.replace('_', ' '), ' / '.join('%.2f (look.2 %.2f)' % (se[l_][z_][key], s2[l_][z_][key]) for l_ in ('lod0', 'lod1'))) for z_ in zones)
                not_more = lambda key: all(se[l_][z_][key] <= s2[l_][z_][key] + 1e-9 for l_ in se for z_ in zones); rv2 = sk.get('review_r2')
                if rv2: check(all(abs(s2['lod0']['east_near'][k_] - rv2[k_]) < 1e-6 for k_ in ('see_m', 'wall1_m', 'wall2_m')),
                              'N9r self-check: this measure on look.2 (LOD0, east near) gives see-over %.2f m / core 1 m over %.2f m / 2 m over %.2f m - the review measured %.2f / %.2f / %.2f with its own code' %
                              (s2['lod0']['east_near']['see_m'], s2['lod0']['east_near']['wall1_m'], s2['lod0']['east_near']['wall2_m'], rv2['see_m'], rv2['wall1_m'], rv2['wall2_m']))
                check(all(se[l_]['east_near']['see_m'] <= sk['east_near_see_max_m'] + 1e-9 for l_ in se) and not_more('see_m'),
                      'N9a see-over from a 국 lift (eye = ground + %.1f m): metres of core where the visible top line is under that eye while the invisible core goes more than %.2f m higher, LOD0 / LOD1 - %s (near = within %.0f m of the stone box, path = within %.0f m; need: east near <= %.1f m and nowhere more than look.2); lowest top line east near %.2f m over the ground (look.2 %.2f)' %
                      (dry.GUK + dry.EYE_M, sk['core_over_min_m'], txt('see_m'), sk['near_m'], sk['path_m'], sk['east_near_see_max_m'], se['lod0']['east_near']['top_min_m'], s2['lod0']['east_near']['top_min_m']))
                check(all(se[l_]['east_near']['wall1_m'] <= sk['east_near_wall1_max_m'] + 1e-9 for l_ in se) and not_more('wall1_m'),
                      'N9b the invisible core more than %.1f m over the visible top line, metres of core, LOD0 / LOD1 - %s (need: east near <= %.1f m and nowhere more than look.2)' % (sk['wall1_m'], txt('wall1_m'), sk['east_near_wall1_max_m']))
                check(not_more('wall2_m'), 'N9c the invisible core more than %.1f m over the visible top line, metres of core, LOD0 / LOD1 - %s (need: nowhere more than look.2); highest core over the top line east near %.2f m (look.2 %.2f)' %
                      (sk['wall2_m'], txt('wall2_m'), se['lod0']['east_near']['core_over_max_m'], s2['lod0']['east_near']['core_over_max_m']))
            # N9d: off the deck (review S1)
            if 'deck_reach' in ck:
                dk9 = ck['deck_reach']; da9, d29 = Ma['v10_exact']['deck_reach'], Mr['v10_exact']['deck_reach']; rv9 = dk9.get('review_r2')
                if rv9: check(d29['west']['lod0'] + d29['east']['lod0'] == rv9['lod0'], 'N9q self-check: off the deck on look.2 (LOD0, both sides) gives %d cells - the review counted %d with its own code' % (d29['west']['lod0'] + d29['east']['lod0'], rv9['lod0']))
                check(da9['west']['both'] <= d29['west']['both'], 'N9d off the deck, west side: core cells within %.1f m of the stone box whose rock top is under the core top and under deck + %.2f m (국 cast on the deck + jump + step; the lower of LOD0 / LOD1): %d of %d (look.2 %d) - not more than look.2' %
                      (dk9['within_m'], dry.GUK + dry.JUMP + dry.STEP, da9['west']['both'], da9['west']['cells'], d29['west']['both']))
                say('%s N9dx off the deck, east side: %d of %d (look.2 %d); both sides on LOD0 %d (look.2 %d%s)%s' % ('INFO' if da9['east']['both'] <= d29['east']['both'] else 'OPEN', da9['east']['both'], da9['east']['cells'], d29['east']['both'], da9['west']['lod0'] + da9['east']['lod0'], d29['west']['lod0'] + d29['east']['lod0'],
                    '' if not rv9 else '; the first build of revision 3: %d' % rv9['first_build_lod0'], '' if da9['east']['both'] <= d29['east']['both'] else ' - the east side is NOT back to look.2: the round stone and the crag beside the deck stand lower than the two upright boulders of look.2 did; reported, not hidden'))
            # N8: what the rework did to the numbers that were not asked to change
            fa, f2 = Ma['fit'], Mr['fit']
            check(all(fa[l_]['eye_depth']['mean_m'] <= ck['eye_depth_mean_max_m'] and fa[l_]['eye_depth']['max_m'] <= ck['eye_depth_face_max_m'] for l_ in ('lod0', 'lod1')) and Ma['triangles']['lod0'] <= ck['tris_lod0_max'] and Ma['triangles']['lod1'] <= ck['tris_lod1_max'],
                  'N8 the budgets hold (the limits are the ones of look.2, not raised): eye depth mean LOD1 %.2f / LOD0 %.2f m (look.2 %.2f / %.2f; limit %.2f), deepest face %.2f m (limit %.2f); triangles LOD0 %d / LOD1 %d (look.2 %d / %d; limits %d / %d); rocks %d (look.2 %d)' %
                  (fa['lod1']['eye_depth']['mean_m'], fa['lod0']['eye_depth']['mean_m'], f2['lod1']['eye_depth']['mean_m'], f2['lod0']['eye_depth']['mean_m'], ck['eye_depth_mean_max_m'], max(fa['lod1']['eye_depth']['max_m'], fa['lod0']['eye_depth']['max_m']), ck['eye_depth_face_max_m'],
                   Ma['triangles']['lod0'], Ma['triangles']['lod1'], Mr['triangles']['lod0'], Mr['triangles']['lod1'], ck['tris_lod0_max'], ck['tris_lod1_max'], len(after), len(L2['rocks'])))
            say('OPEN N8x the price of lower, broader masses, reported not hidden: core cells where the rock is lower than the core AND inside what a 국 deck + running jump + step reaches (an invisible wall over a top that looks reachable; V10 allows it, the reach rule is off in the data): LOD1 %d / LOD0 %d (look.2 %d / %d), of them within %.1f m of the stone box %d / %d (look.2 %d / %d)' %
                (fa['lod1']['reach_wall_cells'] + fa['lod1']['reach_wall_exempt_cells'], fa['lod0']['reach_wall_cells'] + fa['lod0']['reach_wall_exempt_cells'], f2['lod1']['reach_wall_cells'] + f2['lod1']['reach_wall_exempt_cells'], f2['lod0']['reach_wall_cells'] + f2['lod0']['reach_wall_exempt_cells'],
                 L['cover']['reach_exempt_stone_m'], fa['lod1']['reach_wall_exempt_cells'], fa['lod0']['reach_wall_exempt_cells'], f2['lod1']['reach_wall_exempt_cells'], f2['lod0']['reach_wall_exempt_cells']))
    # ---------------- P. placement ----------------
    say('-- P. placement [O]')
    c = Ma['clear']; cb = Mb['clear']
    check(c['deck_rock_over_top_cells'] == 0, 'P1 stone deck (|u| <= %.1f m of the stone box, outside the cores): cells with rock above the stone top: %d of %d (before %d)' % (L['clear']['deck_half_u_m'], c['deck_rock_over_top_cells'], c['deck_cells'], cb['deck_rock_over_top_cells']))
    say('INFO P1x deck shoulders (the %.1f m strips of the deck beside the cores: the body cannot stand closer than its radius to a core face): cells with rock above the stone top %d of %d (before %d)' % (S.D['stone']['half_m'] - L['clear']['deck_half_u_m'], c['shoulder_rock_over_top_cells'], c['shoulder_cells'], cb['shoulder_rock_over_top_cells']))
    check(c['corridor_rock_cells'] == 0 and c['pad_rock_cells'] == 0, 'P2 in front of the stone, both pad sides, %.1f m: cells with any rock %d, on the pads %d (before %d / %d) - the stone face is open from both pads' % (L['clear']['corridor_m'], c['corridor_rock_cells'], c['pad_rock_cells'], cb['corridor_rock_cells'], cb['pad_rock_cells']))
    check(c['route_rock_cells'] == 0, 'P3 within %.1f m of the cut %s (outside the cores): cells with any rock %d (before %d)' % (L['clear']['route_clear_m'], D['checks']['cut_route'], c['route_rock_cells'], cb['route_rock_cells']))
    check(c['lane_rock_cells'] == 0 and c['lane_core_cells'] == 0 and c['lane_steep_cells'] == 0 and c['lane_cells'] > 0, 'P4 tail lanes (%s; half width %.2f m): cells with any rock %d, core %d, steeper than %.0f deg %d of %d' %
          (', '.join(l['id'] for l in L['lanes']), L['lanes'][0]['half_width_m'], c['lane_rock_cells'], c['lane_core_cells'], dry.SLOPE_MAX, c['lane_steep_cells'], c['lane_cells']))
    wa, wb = Ma['walk_around_rock_solid_m'], Mb['walk_around_rock_solid_m']; w0 = Ma['walk_around_cores_only_m']
    check(all(wa[k] is not None for k in wa), 'P5 walk-around between the cut points 20 m either side of the stone: cores only west %s / east %s m; with every visible rock over %.2f m solid: west %s / east %s m (before, same rule: %s / %s)' %
          (w0['west'], w0['east'], dry.STEP, wa['west'], wa['east'], wb['west'], wb['east']))
    say('INFO P6 rock higher than the eye more than 1 m outside every core (a body is not stopped there): %.1f m2 (before %.1f); of it beyond the two core ends: %.1f m2 (before %.1f)' % (c['ghost_eye_m2'], cb['ghost_eye_m2'], c['ghost_eye_beyond_ends_m2'], cb['ghost_eye_beyond_ends_m2']))
    check(c['ghost_eye_beyond_ends_m2'] <= cb['ghost_eye_beyond_ends_m2'], 'P6 eye-high rock beyond the core ends is not more than before (%.1f <= %.1f m2)' % (c['ghost_eye_beyond_ends_m2'], cb['ghost_eye_beyond_ends_m2']))
    # undersides
    worst = {}; rows = []
    for r, (s0, s1) in zip(after, subs):
        kind = 'boulder' if r['role'] in ('talus', 'tail', 'apron') else mt[r['mesh']]['kind']         # a loose stone lies half sunk whatever its mesh
        m = np.isfinite(s1.top); gr = g.ground[s1.i0:s1.i1, s1.j0:s1.j1]
        share = float((m & (s1.bot <= gr - ck['sunk_under_m'])).sum()) / max(1, int(m.sum()))
        rows.append(dict(id=r['id'], kind=kind, sunk_share=round(share, 2), crest_m=round(crest_of(S, r, (s0, s1)), 2)))
        if r['role'] == 'cheek':
            # the two deck-edge boulders stand against the stone: their girth is over the deck on purpose; only the lowest point is asked
            low = float((gr - s1.bot)[m].max()) if m.any() else -9.0
            check(low >= ck['cheek_bottom_under_m'], 'P7c %s (deck-edge boulder): its lowest point is %.2f m under the ground (need %.2f)' % (r['id'], low, ck['cheek_bottom_under_m']))
            continue
        if kind not in worst or share < worst[kind][0]: worst[kind] = (share, r['id'])
    R['undersides'] = rows
    check(all(worst[k][0] >= ck['sunk_share_min'][k] for k in worst), 'P7 undersides: share of each footprint whose underside is >= %.2f m under the ground: lowest block %s, lowest boulder %s (need %.2f / %.2f) - nothing floats' %
          (ck['sunk_under_m'], '%.2f (%s)' % worst['block'] if 'block' in worst else '-', '%.2f (%s)' % worst['boulder'] if 'boulder' in worst else '-', ck['sunk_share_min']['block'], ck['sunk_share_min']['boulder']))
    # the editor's V3 for look rocks: the LOWEST vertex of LOD1 against the ground under that vertex (here also LOD0, and with a margin)
    if 'v3_exact' in ck:
        vx = ck['v3_exact']; v3_need = ck['sunk_under_m'] + vx['offline_margin_m']
        if L2 is not None:
            r2v = v3_rows(S, L2['rocks'], lib.mesh_table(L2)); w_ = min(r2v, key=lambda a: a['lod1']); under = [a['id'] for a in r2v if a['lod1'] < ck['sunk_under_m']]
            R['v3_look2'] = sorted(r2v, key=lambda a: a['lod1'])[:8]
            check(w_['id'] == vx['editor_r2_rock'] and abs(w_['lod1'] - vx['editor_r2_min_m']) <= vx['replica_tol_m'] and under == [vx['editor_r2_rock']],
                  'V3s self-check: the replica of the editor\'s V3 on look.2 gives min %.3f m at %s, rocks under %.2f: %s - the editor measured %.2f m at %s, FAIL [M 2026-10-05] (within %.2f m: the replica reproduces the editor)' %
                  (w_['lod1'], w_['id'], ck['sunk_under_m'], under, vx['editor_r2_min_m'], vx['editor_r2_rock'], vx['replica_tol_m']))
        else: check(False, 'V3s self-check not run: %s is missing (the replica must reproduce the editor\'s number on look.2 before it judges)' % args.r2.replace('\\', '/'))
        av = v3_rows(S, after, mt); R['v3_exact'] = sorted(av, key=lambda a: min(a['lod1'], a['lod0']))[:12]
        for lod in ('lod1', 'lod0'):
            w_ = min(av, key=lambda a: a[lod]); low = sorted([(a[lod], a['id']) for a in av if a[lod] < v3_need])
            check(not low, 'V3x %s the editor\'s V3 (lowest vertex of every rock against the ground under that vertex): min %.3f m at %s (%s, %s); need %.2f (the editor limit, unchanged) + %.2f (offline margin: height field vs physical ground) = %.2f; under it: %d %s' %
                  (lod, w_[lod], w_['id'], w_['role'], w_['mesh'], ck['sunk_under_m'], vx['offline_margin_m'], v3_need, len(low), low[:4] if low else ''))
    # crest of every rock = the design value
    off = [(r['id'], r['crest_m'], q['crest_m']) for r, q in zip(after, rows) if abs(r['crest_m'] - q['crest_m']) > 0.05]
    check(not off, 'P8 crest of every rock over its ground anchor equals its design value within 0.05 m (%d off%s)' % (len(off), '' if not off else ': ' + str(off[:3])))
    # tails
    for side in ('west', 'east'):
        tl = sorted([r for r in after if r['role'] == 'tail' and (r['station'] < S.end_w if side == 'west' else r['station'] > S.end_e)], key=lambda r: abs(r['station'] - (S.end_w if side == 'west' else S.end_e)))
        far = max([abs(r['station'] - (S.end_w if side == 'west' else S.end_e)) + r['along_m'] * 0.5 for r in tl] or [0]); hs = [r['crest_m'] for r in tl]
        check(len(tl) >= 3 and ck['tail_len_m'][0] <= far <= ck['tail_len_m'][1] + 0.6 and all(hs[k] > hs[k + 1] for k in range(len(hs) - 1)) and hs[0] < dry.EYE_M,
              'P9 %s tail: %d rocks reach %.1f m beyond the core end (asked %.0f - %.0f), crests %s m getting smaller, all under the eye (%.1f m)' % (side, len(tl), far, ck['tail_len_m'][0], ck['tail_len_m'][1], hs, dry.EYE_M))
    tal = [r for r in after if r['role'] == 'talus']; north = sum(1 for r in tal if S.line.station(r['ground_x'], r['ground_z'])[1] > 0)
    check(ck['talus_count'][0] <= len(tal) <= ck['talus_count'][1] and max(r['crest_m'] for r in tal) <= ck['talus_h_max_m'] and min(north, len(tal) - north) >= 3,
          'P10 talus: %d boulders (asked %d - %d), %d north / %d south of the line, crests %.2f .. %.2f m (<= %.2f: stepped over, no collider needed to read right)' % (len(tal), ck['talus_count'][0], ck['talus_count'][1], north, len(tal) - north, min(r['crest_m'] for r in tal), max(r['crest_m'] for r in tal), ck['talus_h_max_m']))
    # trees
    hits = []
    for tx, tz, rid, proto in S.trees:
        i, j = g.cell(tx, tz); k = int(ck['tree_clear_m'] / g.c) + 1; sub = top0[max(0, i - k):i + k + 1, max(0, j - k):j + k + 1]
        if np.isfinite(sub).any(): hits.append((rid, proto, round(tx, 1), round(tz, 1), round(float(np.nanmax(np.where(np.isfinite(sub), sub, np.nan)) - g.ground[i, j]), 2)))
    R['trees_in_rock'] = hits
    check(not hits, 'P11 tree placements of the rendered sheet within %.1f m of a rock footprint: %d of %d trees in the window %s' % (ck['tree_clear_m'], len(hits), len(S.trees), hits[:4] if hits else ''))
    t = Ma['triangles']; tb = Mb['triangles']
    check(t['lod0'] <= ck['tris_lod0_max'] and t['lod1'] <= ck['tris_lod1_max'], 'P12 triangles: LOD0 %d (before %d; limit %d), LOD1 %d (before %d; limit %d); every rock keeps a 2-level LODGroup at %s' % (t['lod0'], tb['lod0'], ck['tris_lod0_max'], t['lod1'], tb['lod1'], ck['tris_lod1_max'], L['lod_screen']))
    check(0 < L['lod_screen'][1] < L['lod_screen'][0] < 1, 'P12b lod_screen %s: 0 < far < near < 1 (review X6)' % L['lod_screen'])
    mat_text = open(lib.PROJECT + L['material'], encoding='utf-8', errors='replace').read() if os.path.exists(lib.PROJECT + L['material']) else ''
    check(bool(mat_text) and L['material'] == D['assets']['rock_material'] and '_EMISSION' not in mat_text.split('m_SavedProperties')[0], 'P12c material %s = the base rock material, exists, no _EMISSION keyword (발광 상한; review X3)' % L['material'])
    ids = [r['id'] for r in after]
    check(len(set(ids)) == len(ids) and all(r['mesh'] in mt for r in after) and all(abs(math.sqrt(sum(v * v for v in r['q'])) - 1) < 1e-4 for r in after) and all(min(r['scale']) > 0 for r in after),
          'P13 data shape: %d rocks, unique ids, known meshes, unit quaternions, positive scales' % len(after))
    for mkey, m in mt.items():
        V, _ = lib.unity_mesh(m['lod0'])
        if np.abs(V.min(0) - np.array(m['bounds_min'])).max() > 1e-3 or np.abs(V.max(0) - np.array(m['bounds_max'])).max() > 1e-3: check(False, 'P13 mesh %s: bounds in the data differ from the asset' % mkey)
    R['fails'] = fails; R['verdict'] = 'DRY OK (offline)' if not fails else 'DRY FAIL'
    say('== %s: %d fail(s)' % (R['verdict'], len(fails)))
    os.makedirs(args.out, exist_ok=True)
    slim = json.loads(json.dumps(R))
    json.dump(slim, open(os.path.join(args.out, 'amneung308_look_dry.json'), 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
    open(os.path.join(args.out, 'amneung308_look_dry.txt'), 'w', encoding='utf-8').write('\n'.join(T) + '\n')
    if not args.no_picture:
        picture(args.picture, S, L, D, before, after, Mb, Ma); print('picture ' + args.picture.replace('\\', '/'))
    return 1 if fails else 0


if __name__ == '__main__':
    sys.exit(main())
