"""#308 cliff boundary - closure proof on the 4 m lattice for a stage height field. SPEC-WORLD-CLIFF-BOUNDARY-308 design 9, AC-B3..B6.

  python Tools/Art/cliff308_closure.py <ops.json> --height <height.bytes> --stage 1a [--out <json>] [--save-reach]

The height path is an argument (never a constant): the tool prints the sha256 of the height it used and refuses a file of the
wrong size. Lattice result, not a physics proof: slope < 45 deg walkable, 4-neighbour flood from the Seal308 new-game node,
solids = Seal308 closure lattice (Enclosure305/Out/Seal308/closure/_work). Configurations:
  S0_as_built   closed state, forest runs replaced by cliff segments of stages <= --stage removed, seam runs KEPT, no long wall
  S0_after_wall closed state, + wall.replaces runs removed + a 3 m solid band on the wall lines (the state after stage 1b)
  S1            after the 국 lift: the cliff-top component at each `bays[].landing`; must not join S0 nor S2; the DECLARED bay
                coordinates are tested with the GukLiftSite rules (rise 8-24 m, top within 2 m sideways of the foot column)
  S2            open state (south-gate fact): previously reached items stay reached; cliff tops reached are reported per segment
  S2P           open state + pass discs blocked: compartments by anchor (AC-B6 wants 5 from stage 2 on; indicative before)

Stage 1b (ops v6), `--full`: the same four keys + `states` (wall + closed / open gate with the seam runs kept or removed), `one_way_descent`
(from the cliff-top component: which ground a survivable descent reaches, under the fall rule as coded and under "free fall >= 6 m is
lethal"), the UP-1 rim audit, `dispositions` (what the stage-1a check failures stand on and lead to), and the two data files of the other
packages: --lips (lips308, pieces) and --beyond (beyond308 schema, wall / save migration). Without --full the output is the stage-1a format.
  python Tools/Art/cliff308_closure.py plan/ops308_v6.json --height plan/Stage/height_p1b.bytes --stage 1b --full --tag v6 --save-reach \
         --h1a plan/Stage/height_p1a.bytes --lips plan/lips308_1b.json --beyond plan/beyond308_1b.json
"""
import argparse, json, math, sys
from collections import deque
from pathlib import Path
import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
import cliff308_base as B                                                # noqa: E402
import scarp308 as SC                                                    # noqa: E402
from cliff308_base import HH, W                                          # noqa: E402

PASSES = dict(P03=(1666, 1862), G=(1898, 1739), P07=(1175, 2585), P08=(2316, 4109), P09=(1140, 4184))
ANCHORS = {'청림': (3364, 2020), '상경 가도': (2400, 1900), '성저': (1930, 2400), '도성': (1876, 3318), '황경 외곽 서': (1500, 2600), '적로': (2000, 900), '철옹': (600, 3000), '현강': (2000, 5200)}
GUK_RISE = (8.0, 24.0); GUK_LATERAL = 2.0        # GukLiftSite.cs:19-31
WALK_DEG = 45.0                                  # lattice walk limit of the closure proof (Spec design 9; Seal308 precedent)


class Ctx:
    def __init__(self, log=print):
        self.h = B.load_height(); self.lat = B.lattice(log=log); self.X, self.Z = B.grid_xz()
        self.rcl, self.scl = B.seal('closed'); self.rop, self.sop = B.seal('open')
        self.water = self.lat['water4']; self.inside = self.lat['inside4']; self.route = self.lat['route4']
        self.runs, self.runs_version = B.runs305(); self.structure = B.structure_mask(self.X, self.Z)
        src = B.ENC / 'Out/Seal308/closure/seal308-closed.json'
        items = json.loads(src.read_text(encoding='utf-8'))['items']; known = {(i['kind'], i['id']) for i in items}
        for p in self.lat['content']['Points']:
            if ('interaction', p['Id']) not in known and ('rest', p['Id']) not in known:
                x, z = p['Position']['x'], p['Position']['z']; i, j = int(round(z / 4)), int(round(x / 4))
                items.append(dict(kind='point308', id=p['Id'], x=x, z=z, r=12.0, ea=bool(self.rcl[max(0, i - 3):i + 4, max(0, j - 3):j + 4].any()), underground=False))
        self.items = [it for it in items if not it.get('underground')]
        self.items_source = dict(seal308_closed=B.rel(src), seal308_closed_sha256=B.sha256(src), plus='WorldContent Points not in the Seal308 list (EA = Seal308 closed reach within 12 m)',
                                 count=len(self.items), ea=sum(1 for i in self.items if i['ea']))

    def band(self, P, r):
        return B.band(P, r, self.X, self.Z)

    def walkable(self, field, solid, sl):
        w = (sl < WALK_DEG) & ~self.water & self.inside & ~solid
        w |= self.route & self.inside & ~solid & (np.abs(field - self.h) < 0.5)
        return w


def reached(R, x, z, rad=12):
    i, j = int(round(z / 4)), int(round(x / 4)); r = max(1, int(rad / 4))
    return bool(R[max(0, i - r):i + r + 1, max(0, j - r):j + r + 1].any())


def edge_m(R):
    return int(((R[:6, :].any(0)).sum() + (R[-6:, :].any(0)).sum() + (R[:, :6].any(1)).sum() + (R[:, -6:].any(1)).sum()) * 4)


def active_segments(ops, stage):
    st = ops.get('stages', SC.STAGES_DEFAULT)
    # v6: stage_overrides (e.g. the forest runs a segment replaces from stage 1b on) are merged for the stage asked for
    return [SC.seg_for_stage(d, st, stage) for d in ops['segments'] if not d.get('skip') and not d.get('reference_only') and SC.stage_rank(st, SC.seg_stage(d)) <= SC.stage_rank(st, stage)]


CLIPS = (('(x>=', 0, 1), ('(x<=', 0, -1), ('(z>=', 1, 1), ('(z<=', 1, -1))


def clip_run(P, spec, kept=False):
    """the part of run polyline P a `replaces` entry names ('E305_054 (x>=2160)', +-8 m margin) - or, kept=True, the part it leaves."""
    P = np.asarray(P, float); m = np.ones(len(P), bool)
    for key, col, sg in CLIPS:
        if key in spec: m &= (P[:, col] - float(spec.split(key)[1].split(')')[0])) * sg >= -8
    return P[~m] if kept else P[m]


def kept_barriers(ctx, ops, stage, also_replaced=()):
    """lattice cells of the barriers that STAY after `stage`: every forest run no built cliff segment replaces (for a partly
    replaced run: the part outside the clip; `also_replaced` = run ids the long wall replaces) and every built structure (walls,
    Seal308 gate footprint), at the Seal308 half band. replaced_mask() / wall_bands() never remove these.
    (review 2026-10-04: E305_069c032 ends ON the capital wall at (2282, 2672); the 4.6 m band around it also deleted the wall's
    own cells, and the model stayed shut only because the cliff-foot vertex next to the hole reads as a >= 45 deg node. The same
    band around the seam runs deleted two Seal308 gate cells at (1920, 1744) and (1872, 1752).)"""
    st = ops.get('stages', SC.STAGES_DEFAULT); full = set(also_replaced); part = {}; within = np.zeros(ctx.h.shape, bool)
    for d in active_segments(ops, stage):
        for r in d.get('replaces', []):
            rid = r.split(' ')[0]
            if any(key in r for key, _, _ in CLIPS): part.setdefault(rid, []).append(r)
            else: full.add(rid)
        for rw in d.get('replaces_within', []):
            if SC.stage_rank(st, rw.get('stage', SC.seg_stage(d))) <= SC.stage_rank(st, stage): within |= np.hypot(ctx.X - rw['at'][0], ctx.Z - rw['at'][1]) <= rw['radius']
    keep = np.zeros(ctx.h.shape, bool)
    for rid, s in ctx.runs.items():
        if rid in full: continue
        P = np.asarray(s['points'], float)
        for r in part.get(rid, []): P = clip_run(P, r, kept=True)
        if len(P) >= 2: keep |= B.stamp(P, B.SEAL_BAND, ctx.X, ctx.Z)
    keep &= ~within
    return keep | ctx.structure


def replaced_mask(ctx, ops, stage):
    """forest runs removed by the cliff segments built up to `stage` (4.6 m half band = the run's lattice footprint), never the cells
    of a barrier that stays (kept forest runs, built walls)."""
    m = np.zeros(ctx.h.shape, bool); ids = []; st = ops.get('stages', SC.STAGES_DEFAULT)
    for d in active_segments(ops, stage):
        for r in d.get('replaces', []):
            rid = r.split(' ')[0]
            if rid not in ctx.runs: ids.append(rid + ' (MISSING in segments305.json)'); continue
            P = clip_run(ctx.runs[rid]['points'], r)
            if len(P) >= 2: m |= ctx.band(P, 4.6); ids.append(r)
        for rw in d.get('replaces_within', []):
            if SC.stage_rank(st, rw.get('stage', SC.seg_stage(d))) > SC.stage_rank(st, stage): continue
            m |= ctx.band(ctx.runs[rw['run']]['points'], 4.6) & (np.hypot(ctx.X - rw['at'][0], ctx.Z - rw['at'][1]) <= rw['radius'])
            ids.append(f"{rw['run']} (within {rw['radius']} m of {rw['at']})")
    keep = kept_barriers(ctx, ops, stage); ctx.kept_cells_protected = int((m & keep & ctx.scl).sum())
    return m & ~keep, ids


def wall_bands(ctx, ops, stage=None):
    """(3 m lattice band on the long-wall lines, cells of the seam runs the wall replaces, their ids). The seam-run cells never
    include a barrier that stays (other runs, built walls, the Seal308 gate footprint)."""
    wl = ops.get('wall') or {}; band = np.zeros(ctx.h.shape, bool)
    for ln in wl.get('lines', []): band |= ctx.band(ln['line'], float(wl.get('closure_band_m', 3.0)))
    repl = np.zeros(ctx.h.shape, bool)
    for rid in wl.get('replaces', []):
        if rid in ctx.runs: repl |= ctx.band(ctx.runs[rid]['points'], 4.6)
    if stage is not None and repl.any(): repl &= ~kept_barriers(ctx, ops, stage, also_replaced=wl.get('replaces', []))
    return band, repl, list(wl.get('replaces', []))


def state_report(ctx, R, base, new, solid_used, closed):
    a = {(it['kind'], it['id']): reached(R, it['x'], it['z'], min(it.get('r', 12), 30)) for it in ctx.items}
    b = {(it['kind'], it['id']): reached(base, it['x'], it['z'], min(it.get('r', 12), 30)) for it in ctx.items}
    diff = new - ctx.h
    o = dict(reach_ha_base=round(base.sum() * 16 / 1e4, 1), reach_ha=round(R.sum() * 16 / 1e4, 1), new_ha=round((R & ~base).sum() * 16 / 1e4, 2), lost_ha=round((base & ~R).sum() * 16 / 1e4, 2),
             items=len(ctx.items), items_reached=sum(a.values()), items_reached_base=sum(b.values()),
             items_lost=[list(k) for k in b if b[k] and not a[k]], items_gained=[list(k) for k in b if a[k] and not b[k]],
             world_edge_reach_m=edge_m(R), world_edge_reach_base_m=edge_m(base), cliff_top_reach_ha=round(float(((diff > 3) & R).sum() * 16 / 1e4), 2),
             new_area_clusters=B.clusters(R & ~base, 60, 24), lost_area_clusters=B.clusters(base & ~R, 120, 16))
    if closed:
        ea = [it for it in ctx.items if it['ea']]
        o['ea_items'] = len(ea); o['ea_items_missed'] = [[it['kind'], it['id']] for it in ea if not a[(it['kind'], it['id'])]]
        late = [[it['kind'], it['id']] for it in ctx.items if not it['ea'] and a[(it['kind'], it['id'])]]
        o['late_items_reached'] = late; o['late_items_newly_reached'] = [k for k in late if not b[tuple(k)]]
    return o


def guk_test(new, bay):
    """declared coordinates under the GukLiftSite rules + the terrain must really hold the declared shelf."""
    fx, fz = bay['foot'][:2]; sx, sz = bay['shelf_edge'][:2]; ox, oz = bay.get('outward', [1.0, 0.0])
    fy = float(B.sample_tri(new, fx, fz)); sy = float(bay['shelf_edge'][2]) if len(bay['shelf_edge']) > 2 else float(B.sample_tri(new, sx, sz))
    back = float(bay.get('authored_overhang_m', 0.0)) + 3.0; ty = float(B.sample_tri(new, sx - ox * back, sz - oz * back))      # terrain 3 m behind the terrain shelf edge
    rise = sy - fy; lat = math.hypot(fx - sx, fz - sz); sup = abs(ty - sy) <= 0.5
    return dict(foot=[fx, fz, round(fy, 2)], shelf_edge=[sx, sz, round(sy, 2)], terrain_y_behind_shelf_edge=round(ty, 2), shelf_supported_by_terrain=bool(sup), rise_m=round(rise, 2), lateral_m=round(lat, 2),
                rise_ok=bool(GUK_RISE[0] <= rise <= GUK_RISE[1]), lateral_ok=bool(lat <= GUK_LATERAL), valid=bool(GUK_RISE[0] <= rise <= GUK_RISE[1] and lat <= GUK_LATERAL and sup),
                authored_overhang_m=round(float(bay.get('authored_overhang_m', 0.0)), 2),
                note='shelf_edge = edge of the AUTHORED shelf piece (1b guk command) overhanging the lattice wall; valid needs rise 8-24 m, lateral <= 2 m AND the terrain behind at the declared shelf height (+-0.5 m)')


def sensitivity(ctx, new, sl, solids, log=print):
    """the closed-state floods again in three less forgiving lattice models. The AC model (node slope, 4-neighbour) marks the
    unraised foot vertex of a one-cell face as >= 45 deg, so a hole next to a face can hide behind it; these models do not.
      node_slope_8n  the AC model with diagonal steps
      edge_slope_4n  a step is allowed when |dy| < 4 m * tan(WALK_DEG) between two nodes (foot vertices are standable)
      edge_slope_8n  the same with the quad diagonals
    Each is compared with ITS OWN base flood (base height, Seal308 closed solid). ok = no late item newly reached, no cliff top
    reached, world-edge reach 0, in every model. Still a lattice result, not a physics proof."""
    step = B.CELL * math.tan(math.radians(WALK_DEG)); sl0 = B.slope_deg(ctx.h); free = ~ctx.water & ctx.inside; diff = new - ctx.h
    models = dict(node_slope_8n=lambda h, solid, s: B.flood8(ctx.walkable(h, solid, s), B.SEED_NODE),
                  edge_slope_4n=lambda h, solid, s: B.flood_edges(h, free & ~solid, B.SEED_NODE, step),
                  edge_slope_8n=lambda h, solid, s: B.flood_edges(h, free & ~solid, B.SEED_NODE, step, diag=True))
    near = lambda R, it: reached(R, it['x'], it['z'], min(it.get('r', 12), 30)); res = {}; ok = True
    for mn, fn in models.items():
        base = fn(ctx.h, ctx.scl, sl0); res[mn] = dict(base_reach_ha=round(base.sum() * 16 / 1e4, 1))
        for cfg, solid in solids.items():
            R = fn(new, solid, sl)
            e = dict(reach_ha=round(R.sum() * 16 / 1e4, 1), new_ha=round((R & ~base).sum() * 16 / 1e4, 2), lost_ha=round((base & ~R).sum() * 16 / 1e4, 2), world_edge_reach_m=edge_m(R),
                     cliff_top_reach_ha=round(float(((diff > 3) & R).sum() * 16 / 1e4), 2),
                     late_items_newly_reached=[[it['kind'], it['id']] for it in ctx.items if not it['ea'] and near(R, it) and not near(base, it)],
                     ea_items_missed=[[it['kind'], it['id']] for it in ctx.items if it['ea'] and near(base, it) and not near(R, it)], new_area_clusters=B.clusters(R & ~base, 120, 8))
            e['ok'] = not e['late_items_newly_reached'] and not e['ea_items_missed'] and e['cliff_top_reach_ha'] == 0 and e['world_edge_reach_m'] == 0
            ok = ok and e['ok']; res[mn][cfg] = e
            log(f"  sensitivity {mn:14s} {cfg:14s} reach {e['reach_ha']} ha (base {res[mn]['base_reach_ha']}), new {e['new_ha']} lost {e['lost_ha']}, late newly {len(e['late_items_newly_reached'])}, ok {e['ok']}")
    res['ok'] = bool(ok)
    return res


def run(ctx, ops, new, stage, log=print):
    st = ops.get('stages', SC.STAGES_DEFAULT); sl = B.slope_deg(new); out = dict(stage=stage)
    repl, ids = replaced_mask(ctx, ops, stage); wband, wrepl, wids = wall_bands(ctx, ops, stage)
    out['replaced_runs'] = ids; out['wall_replaced_runs'] = wids
    R = {}
    for name, solid, base, closed in (('S0_as_built', ctx.scl & ~repl, ctx.rcl, True), ('S0_after_wall', (ctx.scl & ~repl & ~wrepl) | wband, ctx.rcl, True),
                                      ('S2', ctx.sop & ~repl, ctx.rop, False), ('S2_after_wall', (ctx.sop & ~repl & ~wrepl) | wband, ctx.rop, False)):
        Rm = B.flood(ctx.walkable(new, solid, sl), B.SEED_NODE); R[name] = Rm; out[name] = state_report(ctx, Rm, base, new, solid, closed)
        log(f"  {name:14s} reach {out[name]['reach_ha']} ha (base {out[name]['reach_ha_base']}), new {out[name]['new_ha']} lost {out[name]['lost_ha']}, edge {out[name]['world_edge_reach_m']} m, cliff top {out[name]['cliff_top_reach_ha']} ha")
    out['replaced_band_cells_kept_for_staying_barriers'] = getattr(ctx, 'kept_cells_protected', 0)
    out['sensitivity'] = sensitivity(ctx, new, sl, {'S0_as_built': ctx.scl & ~repl, 'S0_after_wall': (ctx.scl & ~repl & ~wrepl) | wband}, log)
    # S1: cliff-top components at the declared landings
    out['S1'] = {}
    wk = ctx.walkable(new, ctx.scl & ~repl, sl); wk2 = ctx.walkable(new, ctx.sop & ~repl, sl)
    for bay in ops.get('bays', []):
        built = SC.stage_rank(st, bay['stage']) <= SC.stage_rank(st, stage)
        i0, j0 = int(round(bay['landing'][1] / 4)), int(round(bay['landing'][0] / 4)); land = None
        seg = [d for d in ops['segments'] if d['id'] == bay['segment']][0]; ring = B.poly_mask(seg['points'], ctx.X, ctx.Z)
        for r in range(0, 14):
            cand = [(i, j) for i in range(i0 - r, i0 + r + 1) for j in range(j0 - r, j0 + r + 1) if wk[i, j] and ring[i, j] and not R['S0_as_built'][i, j]]
            if cand: land = min(cand, key=lambda c: abs(c[0] - i0) + abs(c[1] - j0)); break
        T = B.flood(wk, land) if land else np.zeros_like(wk); T2 = B.flood(wk2, land) if land else T
        e = dict(bay_built_in_this_stage=built, landing=[land[1] * 4, land[0] * 4] if land else None, landing_y=round(float(new[land]), 1) if land else None,
                 top_component_ha=round(T.sum() * 16 / 1e4, 2), top_inside_ring_ha=round((T & ring).sum() * 16 / 1e4, 2), top_outside_ring_ha=round((T & ~ring).sum() * 16 / 1e4, 2),
                 top_gentle_lt22_ha=round((T & (sl < 22)).sum() * 16 / 1e4, 2), top_y_min_max=[round(float(new[T].min()), 1), round(float(new[T].max()), 1)] if T.any() else None,
                 joins_S0=bool((T & R['S0_as_built']).any()), joins_S0_after_wall=bool((T & R['S0_after_wall']).any()), joins_S2=bool((T2 & R['S2']).any()),
                 items_on_top=[[it['kind'], it['id']] for it in ctx.items if reached(T, it['x'], it['z'], 12)], declared=guk_test(new, bay))
        if not built:
            e['declared']['note_stage'] = f"the bay recess is a stage-{bay['stage']} op: on this height the declared rise is the full face - the lift must NOT be valid yet (AC-B23: no reachable empty top in S0/S1)"
        R['top_' + bay['id']] = T; out['S1'][bay['id']] = e
        log(f"  S1 {bay['id']}: top {e['top_component_ha']} ha joins S0 {e['joins_S0']} S2 {e['joins_S2']}; declared rise {e['declared']['rise_m']} m lateral {e['declared']['lateral_m']} m valid {e['declared']['valid']} (built {built})")
    # S2P: passes blocked (open state)
    blk = np.zeros(ctx.h.shape, bool)
    for nm, (x, z) in PASSES.items(): blk |= np.hypot(ctx.X - x, ctx.Z - z) <= 70
    wkp = ctx.walkable(new, (ctx.sop & ~repl & ~wrepl) | wband | blk, sl); comps = []; groups = {}
    for nm, (x, z) in ANCHORS.items():
        a0, b0 = int(z / 4), int(x / 4); hit = None
        for ci_, R_ in enumerate(comps):
            if R_[a0 - 25:a0 + 26, b0 - 25:b0 + 26].any(): hit = ci_; break
        if hit is None:
            cand = np.argwhere(wkp[a0 - 25:a0 + 26, b0 - 25:b0 + 26])
            if not len(cand): groups.setdefault(-1, []).append(nm); continue
            c0 = cand[np.abs(cand - 25).sum(1).argmin()]; R_ = B.flood(wkp, (a0 - 25 + int(c0[0]), b0 - 25 + int(c0[1]))); comps.append(R_); hit = len(comps) - 1
        groups.setdefault(hit, []).append(nm)
    out['S2P'] = dict(pass_discs={k: list(v) for k, v in PASSES.items()}, radius_m=70, compartments=[dict(members=v, ha=round(float(comps[c].sum() * 16 / 1e4), 1) if c >= 0 else 0.0) for c, v in groups.items()],
                      count=len(groups), target=5, verdict='INDICATIVE before stage 2 (AC-B6 applies from stage 2): the stage-2 lines are not built' if SC.stage_rank(st, stage) < SC.stage_rank(st, '2a') else ('OK' if len(groups) == 5 else 'FAIL'))
    log(f"  S2P compartments {len(groups)}: {[g['members'] for g in out['S2P']['compartments']]}")
    s0 = out['S0_as_built']; s0w = out['S0_after_wall']
    out['AC'] = dict(
        B3_S0_as_built=dict(ea_missed=len(s0['ea_items_missed']), late_newly_reached=len(s0['late_items_newly_reached']), ea_reach_delta_ha=round(s0['reach_ha'] - s0['reach_ha_base'], 2), world_edge_reach_m=s0['world_edge_reach_m'],
                            ok_items=not s0['ea_items_missed'] and not s0['late_items_newly_reached'], ok_area=abs(s0['reach_ha'] - s0['reach_ha_base']) <= 3.0, ok_edge=s0['world_edge_reach_m'] == 0),
        B3_S0_after_wall=dict(ea_missed=len(s0w['ea_items_missed']), late_newly_reached=len(s0w['late_items_newly_reached']), ea_reach_delta_ha=round(s0w['reach_ha'] - s0w['reach_ha_base'], 2), world_edge_reach_m=s0w['world_edge_reach_m'],
                              ok_items=not s0w['ea_items_missed'] and not s0w['late_items_newly_reached'], ok_area=abs(s0w['reach_ha'] - s0w['reach_ha_base']) <= 3.0, ok_edge=s0w['world_edge_reach_m'] == 0),
        B4_S1={k: dict(isolated=not v['joins_S0'] and not v['joins_S2'], lift_valid=v['declared']['valid'], built=v['bay_built_in_this_stage']) for k, v in out['S1'].items()},
        B5_S2=dict(items_lost=len(out['S2']['items_lost']), reached=out['S2']['items_reached'], reached_base=out['S2']['items_reached_base'], ok=not out['S2']['items_lost']),
        B23_cliff_top_reach_ha=dict(S0=s0['cliff_top_reach_ha'], S2=out['S2']['cliff_top_reach_ha']),
        B3_sensitivity=dict(ok=out['sensitivity']['ok'], doc='closed-state floods in the node-8n / edge-4n / edge-8n lattice models (see `sensitivity`); not part of the Spec AC, a guard against a hole hidden behind a cliff-foot vertex'))
    return out, R


# ------------------------------------------------------------------ stage 1b (ops v6, --full): states, one-way descent, rim audit, lips, beyond
STEP_UP = 0.3                                    # CharacterController step offset (cb308.json probe.capsuleStep)


def wall_solid(ctx, ops, wall_json=None):
    """(band of the long wall, band of the closed gate panel, source text). CliffBoundary308/wall_1b/jangseong308.json wins when it is
    there (modules[] a / b as [x, z] + halfDepth, gate{} a / b): the lattice band is never thinner than wall.closure_band_m (a 1.1 m
    half depth does not close a 4 m lattice). Else the ops wall.lines at closure_band_m, and no gate band (the Seal308 gate footprint
    in the Seal308 closed solid is the closed gate)."""
    wl = ops.get('wall') or {}; r0 = float(wl.get('closure_band_m', 3.0)); band = np.zeros(ctx.h.shape, bool); gate = np.zeros(ctx.h.shape, bool)
    p = Path(wall_json) if wall_json else B.CB / 'wall_1b/jangseong308.json'
    if p.exists():
        try:
            J = json.loads(p.read_text(encoding='utf-8')); mods = [m for m in J.get('modules', []) if isinstance(m.get('a'), list) and isinstance(m.get('b'), list)]
        except Exception: mods = []; J = {}
        if mods:
            for m in mods: band |= ctx.band([m['a'][:2], m['b'][:2]], max(r0, float(m.get('halfDepth', 0.0))))
            g = J.get('gate') or {}
            if isinstance(g.get('a'), list) and isinstance(g.get('b'), list): gate = ctx.band([g['a'][:2], g['b'][:2]], max(r0, float(g.get('halfDepth', 0.0))))
            return band, gate, f"{B.rel(p)} sha256 {B.sha256(p)[:12]}: {len(mods)} modules, band = max(halfDepth, {r0:g} m)" + (', gate{} as the closed panel' if gate.any() else ', no gate{} line (Seal308 footprint = the closed gate)')
    for ln in wl.get('lines', []): band |= ctx.band(ln['line'], r0)
    return band, gate, f"ops wall.lines at closure_band_m {r0:g} m (no wall_1b/jangseong308.json with modules[]); closed gate = the Seal308 gate footprint"


def descent_flood(ctx, new, solid, wk, seeds, lethal=None):
    """directed flood from the seed nodes. Moves between 4-neighbours:
         walk     both nodes walkable in the closure model (node slope < 45 deg)
         descend  onto a steep node: from a walkable one when it is not higher than + STEP_UP, from a steep one only strictly down;
                  onto a walkable node from a steep one when it is not higher than + STEP_UP
       lethal = None: every descent is survivable (rule as coded: any ground contact refreshes the fall reference).
       lethal = L:    the reference is refreshed on walkable ground only; a node more than L below the last walkable stance is not
                      reached (free fall >= L is lethal). Steep nodes keep the LOWEST reference they were reached with."""
    Wn = B.W; N = new.size; y = new.ravel().tolist(); free = (~ctx.water & ctx.inside & ~solid).ravel().tolist(); w = wk.ravel().tolist()
    reached = bytearray(N); ref = [float('inf')] * N; dq = deque()
    for k in np.flatnonzero((seeds & wk).ravel()).tolist(): reached[k] = 1; ref[k] = y[k]; dq.append(k)
    while dq:
        u = dq.popleft(); yu = y[u]; wu = w[u]; ru = yu if wu else ref[u]; j = u % Wn
        for v in ((u - 1) if j > 0 else -1, (u + 1) if j < Wn - 1 else -1, u - Wn, u + Wn):
            if v < 0 or v >= N or not free[v]: continue
            yv = y[v]
            if w[v]:
                if wu or (yv <= yu + STEP_UP and (lethal is None or ru - yv < lethal)):
                    if not reached[v]: reached[v] = 1; ref[v] = yv; dq.append(v)
                continue
            if (wu and yv <= yu + STEP_UP) or ((not wu) and yv < yu):
                if lethal is not None and ru - yv >= lethal: continue
                if not reached[v] or ru < ref[v] - 1e-9:
                    reached[v] = 1; ref[v] = min(ref[v], ru); dq.append(v)
    return np.frombuffer(bytes(reached), np.uint8).reshape(new.shape) > 0


def coarse_components(mask, block=8, min_cells=12, cap=16):
    """components of a large mask on a block x block coarse grid (32 m): [(fine cells, fine bool mask)], largest first."""
    Hc, Wc = -(-mask.shape[0] // block), -(-mask.shape[1] // block); pad = np.zeros((Hc * block, Wc * block), bool); pad[:mask.shape[0], :mask.shape[1]] = mask
    coarse = pad.reshape(Hc, block, Wc, block).any((1, 3)); lab = -np.ones((Hc, Wc), int); n = 0
    for i0, j0 in np.argwhere(coarse):
        if lab[i0, j0] >= 0: continue
        lab[i0, j0] = n; stack = [(int(i0), int(j0))]
        while stack:
            i, j = stack.pop()
            for a_, b_ in ((i + 1, j), (i - 1, j), (i, j + 1), (i, j - 1), (i + 1, j + 1), (i + 1, j - 1), (i - 1, j + 1), (i - 1, j - 1)):
                if 0 <= a_ < Hc and 0 <= b_ < Wc and coarse[a_, b_] and lab[a_, b_] < 0: lab[a_, b_] = n; stack.append((a_, b_))
        n += 1
    fine = np.repeat(np.repeat(lab, block, 0), block, 1)[:mask.shape[0], :mask.shape[1]]; out = []
    for k in range(n):
        c = mask & (fine == k)
        if c.sum() >= min_cells: out.append((int(c.sum()), c))
    out.sort(key=lambda t: -t[0]); return out[:cap]


def descent_report(ctx, new, R, expected, items_base, name, wk_only):
    """where a survivable descent set leaves the expected set (top + EA / state reach)."""
    outside = R & ~expected; outw = outside & wk_only; rows = []
    for n, c in coarse_components(outw):
        ii, jj = np.nonzero(c); rows.append(dict(ha=round(n * 16 / 1e4, 2), centre_xz=[int(jj.mean() * 4), int(ii.mean() * 4)], bbox_x=[int(jj.min() * 4), int(jj.max() * 4)], bbox_z=[int(ii.min() * 4), int(ii.max() * 4)],
                                               y_min_max=[round(float(new[c].min()), 1), round(float(new[c].max()), 1)]))
    late = [[it['kind'], it['id']] for it in ctx.items if not it['ea'] and reached(R, it['x'], it['z'], min(it.get('r', 12), 30)) and not reached(items_base, it['x'], it['z'], min(it.get('r', 12), 30))]
    return dict(reach_ha=round(R.sum() * 16 / 1e4, 1), walkable_outside_expected_ha=round(float(outw.sum() * 16 / 1e4), 2), outside_components=rows, late_items_newly_reached=late,
                world_edge_reach_m=edge_m(R & wk_only), doc=name)


def rim_audit(new, h, seg, T, bay, sl):
    """UP-1 rim, one row per 4 m station of the ring line: drop = lowest tile-surface point 0..10 m outside the line under the top
    2..6 m inside it; slip = top cells (in T) within 6 m inside the line whose slope is 30-45 deg (walkable, leaning to the rim)."""
    P = seg.P; nU = seg.nU; rows = []
    for k in range(len(P)):
        top = float(np.median(B.sample_tri(new, P[k, 0] + nU[k, 0] * np.array([2.0, 4.0, 6.0]), P[k, 1] + nU[k, 1] * np.array([2.0, 4.0, 6.0]))))
        low = float(B.sample_tri(new, P[k, 0] - nU[k, 0] * np.arange(0.0, 10.1, 1.0), P[k, 1] - nU[k, 1] * np.arange(0.0, 10.1, 1.0)).min())
        ins = [(int(round((P[k, 1] + nU[k, 1] * o) / 4)), int(round((P[k, 0] + nU[k, 0] * o) / 4))) for o in (2.0, 6.0)]
        slip = max((float(sl[i, j]) for i, j in ins if T[i, j]), default=0.0)
        rows.append((float(P[k, 0]), float(P[k, 1]), top - low, slip, float(seg.s[k])))
    A = np.array(rows); inbay = np.hypot(A[:, 0] - bay['at'][0], A[:, 1] - bay['at'][1]) <= bay.get('window_half_m', 14.0) + bay.get('station_tolerance_m', 12.0) if bay else np.zeros(len(A), bool)
    slipm = (A[:, 3] >= 30.0) & (A[:, 3] < 45.0)
    return A, dict(stations=len(A), station_step_m=4, rim_drop_min_m=round(float(A[:, 2].min()), 1), rim_drop_min_at=[round(float(A[int(A[:, 2].argmin()), 0]), 1), round(float(A[int(A[:, 2].argmin()), 1]), 1)],
                   rim_drop_p10_p50_m=[round(float(v), 1) for v in np.percentile(A[:, 2], [10, 50])], rim_drop_min_outside_bay_window_m=round(float(A[~inbay, 2].min()), 1) if (~inbay).any() else None,
                   stations_drop_lt_6m=int((A[:, 2] < 6.0).sum()), stations_drop_lt_lethal_outside_bay=int(((A[:, 2] < 6.0) & ~inbay).sum()),
                   slip_band_stations=int(slipm.sum()), slip_band_runs=M_runs(A, slipm), slip_band_doc='top cell 2 / 6 m inside the rim with slope 30-45 deg (walkable, leaning): AC-B4 wants 0 slide-off bands in the editor rim walk'), inbay


def M_runs(A, cond):
    idx = np.nonzero(cond)[0]
    if not len(idx): return []
    parts = np.split(idx, np.nonzero(np.diff(idx) > 1)[0] + 1)
    return [dict(from_xz=[round(float(A[p[0], 0]), 1), round(float(A[p[0], 1]), 1)], to_xz=[round(float(A[p[-1], 0]), 1), round(float(A[p[-1], 1]), 1)], length_m=int(len(p) * 4), slope_max_deg=round(float(A[p, 3].max()), 1)) for p in parts]


def landing_side(new, wk, expect, x, z, nx, nz):
    """the first walkable lattice node met going outward (away from the top) from a rim station: is it in the expected reach?"""
    for o in np.arange(2.0, 60.1, 2.0):
        i, j = int(round((z - nz * o) / 4)), int(round((x - nx * o) / 4))
        if not (0 <= i < B.HH and 0 <= j < B.W): return None, None
        if wk[i, j] and new[i, j] < float(B.sample_tri(new, x + nx * 4, z + nz * 4)) - 3.0: return bool(expect[i, j]), [j * 4, i * 4]
    return None, None


def lip_lines(ops, new, seg, A, inbay, wk, expect, T, cfg):
    """lips308: one run per reason along the ring rim. 'E305_005 joint' = the recorded crossing (required); 'survivable descent' = a
    slide off the rim (model i) lands outside the expected reach; 'slip band' = the top leans 30-45 deg at the rim. Line = inset_m
    inside the ring line (on the top). The declared bay window is left open (shelf, ramp, descent chain)."""
    P = seg.P; nU = seg.nU; n = len(P); why = [''] * n; land = [None] * n; Td = B.dilate(T, 2)
    for k in range(n):
        ok, at = landing_side(new, wk, expect, P[k, 0], P[k, 1], nU[k, 0], nU[k, 1]); land[k] = (ok, at)
        if 30.0 <= A[k, 3] < 45.0: why[k] = 'slip band'
        if ok is False: why[k] = 'survivable descent'
    for k in range(n):                                                    # a 1-station hole inside a run takes the run's reason
        if not why[k] and why[k - 1] and why[k - 1] == why[(k + 1) % n]: why[k] = why[k - 1]
    for j in cfg.get('joints', []):
        for k in np.nonzero(np.hypot(P[:, 0] - j['at'][0], P[:, 1] - j['at'][1]) <= j['half_m'])[0]: why[k] = j['reason']
    for k in range(n):
        if inbay[k]: why[k] = ''
    runs = []; k = 0; parts = []
    while k < n:
        if not why[k]: k += 1; continue
        e = k
        while e + 1 < n and why[e + 1] == why[k]: e += 1
        parts.append(list(range(k, e + 1))); k = e + 1
    if len(parts) > 1 and seg.closed and parts[0][0] == 0 and parts[-1][-1] == n - 1 and why[0] == why[n - 1]: parts = [parts[-1] + parts[0]] + parts[1:-1]
    cond = {'E305_005 joint': 'REQUIRED before the lift is switched on (exit condition of the stage-1a exception, Spec 진행 기록 2026-10-04)',
            'survivable descent': 'needed only if the editor rim probe (CliffBoundary308 rim:<scene>) or Play shows SURVIVES here: a slide down the face under the fall rule as coded lands on a blocked side',
            'slip band': 'needed only if the editor rim walk shows a slide-off here (AC-B4)'}
    for q, p in enumerate(parts):
        pts = [[round(float(P[i, 0] + nU[i, 0] * cfg['inset_m']), 2), round(float(P[i, 1] + nU[i, 1] * cfg['inset_m']), 2)] for i in p]
        gy = [round(float(B.sample_tri(new, x, z)), 2) for x, z in pts]; reason = why[p[0]]
        runs.append(dict(id=f"UP1_lip{q + 1}", segment=seg.id, reason=reason, required=bool(reason == 'E305_005 joint'), condition=cond.get(reason, ''), line=pts, ground_y=gy, block_m=cfg['block_m'], max_gap_m=cfg['max_gap_m'],
                         length_m=round((float(np.hypot(*np.diff(np.array(pts), axis=0).T).sum()) if len(pts) > 1 else 0.0) + 4.0, 1), stations=int(len(p)), s_from_to_m=[round(float(seg.s[p[0]]), 1), round(float(seg.s[p[-1]]), 1)],
                         rim_drop_min_m=round(float(A[p, 2].min()), 1), points_within_8m_of_the_walkable_top=int(sum(bool(Td[int(round(z / 4)), int(round(x / 4))]) for x, z in pts)),
                         lands_outside_expected_stations=int(sum(1 for i in p if land[i][0] is False)), length_doc='polyline length + one 4 m station (a run of one station is 4 m long)'))
    return runs, land


# ---- ring tracing (port of seal308_closure.py trace_rings / simplify / keyhole: same output convention)
def _area(R):
    R = np.asarray(R, float); x, z = R[:, 0], R[:, 1]; return 0.5 * float(np.dot(x, np.roll(z, -1)) - np.dot(np.roll(x, -1), z))


def _erode(mask, r_nodes):
    out = mask.copy(); H_, W_ = mask.shape
    for di in range(-r_nodes, r_nodes + 1):
        for dj in range(-r_nodes, r_nodes + 1):
            if di * di + dj * dj > r_nodes * r_nodes: continue
            sh = np.zeros_like(mask); sh[max(0, di): H_ + min(0, di), max(0, dj): W_ + min(0, dj)] = mask[max(0, -di): H_ + min(0, -di), max(0, -dj): W_ + min(0, -dj)]
            out &= sh
    return out


def _trace_rings(mask, C4):
    m = np.pad(mask, 1); edges = {}; inner = m[1:-1, 1:-1]
    for (di, dj), (fa, fb) in (((-1, 0), ((0, 0), (0, 1))), ((1, 0), ((1, 1), (1, 0))), ((0, -1), ((1, 0), (0, 0))), ((0, 1), ((0, 1), (1, 1)))):
        nb = m[1 + di: m.shape[0] - 1 + di, 1 + dj: m.shape[1] - 1 + dj]
        for i, j in zip(*np.nonzero(inner & ~nb)):
            i += 1; j += 1; edges.setdefault((i + fa[0], j + fa[1]), []).append((i + fb[0], j + fb[1]))
    rings = []
    while edges:
        start = next(iter(edges)); cur = start; ring = [start]; prev = None
        while True:
            outs = edges[cur]
            nxt = outs[0] if len(outs) == 1 or prev is None else max(outs, key=lambda o: prev[0] * (o[0] - cur[0]) - prev[1] * (o[1] - cur[1]))
            outs.remove(nxt)
            if not outs: del edges[cur]
            prev = (nxt[1] - cur[1], nxt[0] - cur[0]); cur = nxt
            if cur == start: break
            ring.append(cur)
        pts = [((b - 1) * C4 - C4 / 2, (a - 1) * C4 - C4 / 2) for a, b in ring]; n = len(pts)
        rings.append([p for k, p in enumerate(pts) if not ((pts[k - 1][0] == p[0] == pts[(k + 1) % n][0]) or (pts[k - 1][1] == p[1] == pts[(k + 1) % n][1]))])
    return rings


def _in_poly(P, x, z):
    P = np.asarray(P, float); inside = np.zeros(np.shape(x), bool); n = len(P)
    for k in range(n):
        (x1, z1), (x2, z2) = P[k], P[(k + 1) % n]
        inside ^= ((z1 > z) != (z2 > z)) & (x < (x2 - x1) * (z - z1) / (z2 - z1 + 1e-12) + x1)
    return inside


def _probe_pt(R, C4, inside_mask=True):
    (x0, z0), (x1, z1) = R[0], R[1]; L = math.hypot(x1 - x0, z1 - z0) or 1.0; dx, dz = (x1 - x0) / L, (z1 - z0) / L
    nx, nz = (-dz, dx) if inside_mask else (dz, -dx)
    return np.array((x0 + x1) / 2 + nx * C4 / 2), np.array((z0 + z1) / 2 + nz * C4 / 2)


def _simplify(R, tol):
    P = np.asarray(R, float)
    if len(P) < 8: return P.tolist()

    def dp(Q):
        st = [(0, len(Q) - 1)]; keep = {0, len(Q) - 1}
        while st:
            a, b = st.pop()
            if b - a < 2: continue
            d = Q[b] - Q[a]; L = np.hypot(*d) or 1e-9; dist = np.abs(d[0] * (Q[a + 1:b, 1] - Q[a, 1]) - d[1] * (Q[a + 1:b, 0] - Q[a, 0])) / L; k = int(np.argmax(dist))
            if dist[k] > tol: keep.add(a + 1 + k); st += [(a, a + 1 + k), (a + 1 + k, b)]
        return [Q[i].tolist() for i in sorted(keep)]
    k = int(np.argmax(np.hypot(P[:, 0] - P[0, 0], P[:, 1] - P[0, 1])))
    return dp(P[:k + 1])[:-1] + dp(np.vstack([P[k:], P[:1]]))[:-1]


def _keyhole(outer, holes):
    O = [tuple(p) for p in outer]
    for H_ in sorted(holes, key=lambda h_: -max(p[0] for p in h_)):
        Hh = [tuple(p) for p in H_]; k = max(range(len(Hh)), key=lambda i: Hh[i][0]); hp = Hh[k]; best, bi = None, None
        for i, q in enumerate(O):
            if q[0] < hp[0]: continue
            d = (q[0] - hp[0]) ** 2 + (q[1] - hp[1]) ** 2
            if best is None or d < best: best, bi = d, i
        if bi is None: bi = min(range(len(O)), key=lambda i: (O[i][0] - hp[0]) ** 2 + (O[i][1] - hp[1]) ** 2)
        O = O[:bi + 1] + Hh[k:] + Hh[:k] + [hp] + O[bi:]
    return [list(p) for p in O]


def beyond_doc(ctx, reach_closed, reach_open, cfg, extra):
    """beyond308 in the schema of Enclosure305/Out/Seal308/closure/beyond308.json (the save-migration volume of the gate fact)."""
    C4 = B.CELL; raw = reach_open & ~reach_closed; keep = _erode(raw, int(round(cfg['erode_m'] / C4))); rings = _trace_rings(keep, C4)
    outers = [r for r in rings if _area(r) >= cfg['speck_m2']]; holes = [r for r in rings if _area(r) <= -cfg['speck_m2']]; specks = [r for r in rings if 0 < _area(r) < cfg['speck_m2']]
    hole_pt = [_probe_pt(h_, C4, False) for h_ in holes]; polys = []
    for o in outers:
        Op = np.asarray(o, float); polys.append(dict(outer=o, Op=Op, pt=_probe_pt(o, C4, True), holes=[k for k in range(len(holes)) if bool(_in_poly(Op, *hole_pt[k]))]))
    for P in polys:
        inner = [Q for Q in polys if Q is not P and bool(_in_poly(P['Op'], *Q['pt']))]
        P['holes'] = [holes[k] for k in P['holes'] if not any(bool(_in_poly(Q['Op'], *hole_pt[k])) for Q in inner)]
    out_rings = [_keyhole(_simplify(P['outer'], cfg['simplify_m']), [_simplify(h_, cfg['simplify_m']) for h_ in P['holes'] if abs(_area(h_)) >= cfg['speck_m2']]) for P in polys]
    Rr = [np.asarray(r, float) for r in out_rings]

    def inside_any(x, z):
        return any(bool(_in_poly(r, np.array(x), np.array(z))) for r in Rr)
    item_check = dict(ea_inside=[f"{it['kind']} {it['id']}" for it in ctx.items if it['ea'] and inside_any(it['x'], it['z'])],
                      late_reached_outside=[f"{it['kind']} {it['id']}" for it in ctx.items if not it['ea'] and reached(reach_open, it['x'], it['z'], 4) and not inside_any(it['x'], it['z'])
                                            and not reached(reach_closed, it['x'], it['z'], min(it.get('r', 12), 30))],
                      doc='ea_inside: EA items whose position lies in a ring (must be empty). late_reached_outside: late items the open flood reaches within 4 m of their position that lie in no ring '
                          '(the ring is eroded 8 m: an item right at the edge of the open reach is listed here)')
    rng = np.random.default_rng(308); ii = rng.integers(1, B.HH - 1, 20000); jj = rng.integers(1, B.W - 1, 20000); ok = reach_open[ii, jj]; ii, jj = ii[ok], jj[ok]
    win = np.stack([keep[ii + a, jj + b] for a in (-1, 0, 1) for b in (-1, 0, 1)], 0); clean = win.all(0) | ~win.any(0)
    X = jj * C4 + .37; Z = ii * C4 + .41; ins = np.zeros(len(X), bool)
    for r in Rr: ins |= _in_poly(r, X, Z)
    insp = np.zeros(len(X), bool)
    for sp in specks: insp |= _in_poly(np.asarray(sp, float), X, Z)
    sel = clean & ~insp; bad = int((ins[sel] != keep[ii, jj][sel]).sum())
    doc = dict(version=cfg['version'], generated=extra.pop('generated'), required_fact=cfg['required_fact'],
               rule='beyond = reach(open) - reach(closed) on the 4 m lattice, eroded %.0f m, specks < %.2f ha dropped; rings in world (x, z); a point is beyond when it lies inside a ring by the even-odd rule '
                    '(holes are merged into their outer ring with zero-width bridges, so testing each ring on its own and taking any() is exact)' % (cfg['erode_m'], cfg['speck_m2'] / 1e4),
               beyond_rings=[[[round(x, 1), round(z, 1)] for x, z in r] for r in out_rings], ring_vertices=sum(len(r) for r in out_rings),
               beyond_ha=round(float(keep.sum()) * C4 * C4 / 1e4 - sum(_area(r) for r in specks) / 1e4, 1), raw_beyond_ha=round(float(raw.sum()) * C4 * C4 / 1e4, 1), dropped_specks=len(specks), item_check=item_check)
    doc.update(extra); doc['ring_test_vs_mask'] = dict(disagreements=bad, nodes=int(sel.sum()))
    return doc


# ---- fine local flood on the tile triangles: what a stance leads to (dispositions of the stage-1a check failures)
def _tri_slope(new, X, Z):
    x = np.clip(X, 0, 3999.99); z = np.clip(Z, 0, 5999.99); j = np.floor(x / 4).astype(int); i = np.floor(z / 4).astype(int); low = (x / 4 - j) + (z / 4 - i) <= 1
    h00 = new[i, j]; h10 = new[i, j + 1]; h01 = new[i + 1, j]; h11 = new[i + 1, j + 1]
    return np.degrees(np.arctan(np.where(low, np.hypot(h10 - h00, h01 - h00), np.hypot(h11 - h01, h11 - h10)) / 4.0))


def fine_reach(new, cx, cz, half, cell, seed_xz, rise, reach_m, walk_deg=WALK_DEG, descend=True):
    """0.5 m cells on the tile triangles around (cx, cz). From the stance cell: walk (triangle slope < walk_deg, 4-neighbour, dy <= cell + step),
    slide / fall down to any lower neighbour (8-neighbour), and - from WALKABLE support only (D308-9d) - a 국 + jump to any cell within
    reach_m whose surface is not more than `rise` above the support (flight path not checked: optimistic). A perch on a steep cell starts nothing."""
    n = int(2 * half / cell); xs = cx - half + (np.arange(n) + .5) * cell; zs = cz - half + (np.arange(n) + .5) * cell; X, Z = np.meshgrid(xs, zs)
    Y = B.sample_tri(new, X, Z); walk = _tri_slope(new, X, Z) < walk_deg
    si = int(np.clip((seed_xz[1] - zs[0]) / cell + .5, 0, n - 1)); sj = int(np.clip((seed_xz[0] - xs[0]) / cell + .5, 0, n - 1))
    R = np.zeros((n, n), bool); R[si, sj] = True; offs = [(di, dj) for di in range(-int(reach_m / cell), int(reach_m / cell) + 1) for dj in range(-int(reach_m / cell), int(reach_m / cell) + 1) if (di * di + dj * dj) * cell * cell <= reach_m * reach_m]
    for _ in range(400):
        before = int(R.sum())
        for _ in range(4 * n):                                           # walk + slide to a fixed point
            b2 = int(R.sum()); Rw = R & walk
            for di, dj in ((0, 1), (0, -1), (1, 0), (-1, 0), (1, 1), (1, -1), (-1, 1), (-1, -1)):
                src = SC.shifted(R, di, dj, False); srcw = SC.shifted(Rw, di, dj, False); ys = SC.shifted(Y, di, dj, np.inf)
                if di == 0 or dj == 0: R |= srcw & walk & (np.abs(Y - ys) <= cell + STEP_UP)
                if descend: R |= src & (Y < ys)
            if int(R.sum()) == b2: break
        best = np.full((n, n), -np.inf); A_ = np.where(R & walk, Y, -np.inf)
        for di, dj in offs: np.maximum(best, SC.shifted(A_, di, dj, -np.inf), out=best)
        R |= Y <= best + rise
        if int(R.sum()) == before: break
    return dict(X=X, Z=Z, Y=Y, walk=walk, R=R, seed=(si, sj))


def disposition_rows(ctx, ops, new, h1a, stage, rule, reach_any):
    """what each recorded check failure stands on and whether that stance leads anywhere. kind 'perch': the capsule rests against a
    face above walkable ground - the flood starts on the walkable ground nearest to the recorded end position (a perch itself starts
    nothing: jump and 국 need walkable support) and the recorded end height is compared with ground + 국 + jump. kind 'crest': the
    flood starts on the surface cell nearest to the recorded end height."""
    st = ops.get('stages', SC.STAGES_DEFAULT); rows = []; rise = float(rule.get('guk_plus_jump_m', 3.45)); segs = {d['id']: d for d in ops['segments']}
    for D in ops.get('check_dispositions', []):
        d = SC.seg_for_stage(segs[D['segment']], st, stage); seg = SC.Seg(d); k = int(np.argmin(np.abs(seg.s - D['s']))); n = seg.nU[k]; S = seg.P[k]; hx, hz = D['stance']['xz_hint']; ywant = float(D['stance']['y'])
        gx, gz = np.meshgrid(hx + np.arange(-3.0, 3.01, 0.25), hz + np.arange(-3.0, 3.01, 0.25)); gy = B.sample_tri(new, gx, gz)
        if D['stance'].get('kind') == 'perch':
            cost = np.where(_tri_slope(new, gx, gz) < WALK_DEG, np.hypot(gx - hx, gz - hz), np.inf); kk = np.unravel_index(int(cost.argmin()), gy.shape)
        else: kk = np.unravel_index(int(np.abs(gy - ywant).argmin()), gy.shape)
        stance = (float(gx[kk]), float(gz[kk])); F = fine_reach(new, stance[0], stance[1], 44.0, 0.5, stance, rise, 6.0); R = F['R']; Y = F['Y']; walk = F['walk']; y0 = float(gy[kk])
        raise_tri = B.sample_tri(new, F['X'], F['Z']) - B.sample_tri(ctx.h, F['X'], F['Z']); q = (F['X'] - S[0]) * n[0] + (F['Z'] - S[1]) * n[1]
        top = R & walk & (raise_tri > rise); perch = R & ~walk & (Y > y0 + 0.5)
        tris = []
        for ox, oz in ((0, 0), (.28, 0), (-.28, 0), (0, .28), (0, -.28)):
            px, pz = stance[0] + ox, stance[1] + oz; j = int(px // 4); i = int(pz // 4); low = (px / 4 - j) + (pz / 4 - i) <= 1
            vs = [(i, j), (i, j + 1), (i + 1, j)] if low else [(i + 1, j + 1), (i + 1, j), (i, j + 1)]
            t = dict(quad_xz=[j * 4, i * 4], triangle='A (00,10,01)' if low else 'B (11,01,10)', vertex_y=[round(float(new[v]), 1) for v in vs], raised_vertices=int(sum(1 for v in vs if (new - ctx.h)[v] > 0.05)),
                     slope_deg=round(float(_tri_slope(new, np.array(px), np.array(pz))), 1))
            if t not in tris: tris.append(t)
        ci, cj = int(hz // 4), int(hx // 4); win = np.abs(new - h1a)[max(0, ci - 3):ci + 5, max(0, cj - 3):cj + 5]
        wk_above = R & walk & (Y > y0 + 0.5)
        row = dict(id=D['id'], segment=D['segment'], kind=D['kind'], variant=D['variant'], station_xz=[round(float(S[0]), 1), round(float(S[1]), 1)], stance_xz=[round(stance[0], 2), round(stance[1], 2)], stance_y=round(y0, 2),
                   end_height_recorded=ywant, end_height_above_the_stance_ground_m=round(ywant - y0, 2), within_one_guk_plus_jump=bool(ywant - y0 <= rise + 0.3) if D['stance'].get('kind') == 'perch' else None,
                   stance_across_the_line_m=round(float((stance[0] - S[0]) * n[0] + (stance[1] - S[1]) * n[1]), 2), stance_is_walkable=bool(walk[F['seed']]), triangles_under_the_capsule=tris,
                   fine_flood=dict(cell_m=0.5, window_m=88, rise_m=rise, jump_reach_m=6.0, rules='walk < 45 deg; slide / fall to any lower neighbour; 국 + jump only from walkable support, landing anywhere not more than rise_m above it (flight path not checked)',
                                   reached_m2=round(float(R.sum()) * 0.25, 1), walkable_reached_above_the_stance_m2=round(float(wk_above.sum()) * 0.25, 1),
                                   highest_walkable_reached_above_the_stance_m=round(float((Y[R & walk] - y0).max()), 2) if (R & walk).any() else None,
                                   highest_perch_reached_above_the_stance_m=round(float((Y[perch] - y0).max()), 2) if perch.any() else 0.0,
                                   raised_walkable_ground_reached_m2=round(float(top.sum()) * 0.25, 1), raised_walkable_ground_reached_beyond_the_line_m2=round(float((top & (q > 0)).sum()) * 0.25, 1)),
                   terrain_16m_window_equals_stage_1a=bool(float(win.max()) <= 0.05), disposition=D['disposition'], allow=D['allow'], reason=D['reason'], editor=D.get('editor'))
        if D['stance'].get('kind') == 'crest':
            i_, j_ = int(round(stance[1] / 4)), int(round(stance[0] / 4))
            row['stance_reachable_in_a_closure_state'] = {k_: bool(v[max(0, i_ - 2):i_ + 3, max(0, j_ - 2):j_ + 3].any()) for k_, v in reach_any.items()}
            row['leads_anywhere'] = 'the crest cap and the two slopes of the outer ridge only; the stance itself is in no reach of any state (see stance_reachable_in_a_closure_state)'
        else:
            row['leads_anywhere'] = bool(top.any())
        rows.append(row)
    return rows


def run_full(ctx, ops, new, stage, out, R, wall_json=None, tag=None, h1a_path=None, log=print):
    """ops v6 / stage 1b additions to run(): every gate state, the one-way descent model under two fall rules, the UP-1 rim audit,
    the lips and beyond data, the dispositions. Adds keys to `out`; returns the reach grids to save."""
    st = ops.get('stages', SC.STAGES_DEFAULT); sl = B.slope_deg(new); repl, _ = replaced_mask(ctx, ops, stage); _, wrepl, wids = wall_bands(ctx, ops, stage)
    wband, gband, wsrc = wall_solid(ctx, ops, wall_json); rule = (ops.get('drop_chain') or {}).get('rule', {}); lethal = float(rule.get('lethal_fall_m', 6.0)); diff = new - ctx.h
    solids = dict(S0_wall_seam_kept=(ctx.scl & ~repl) | wband | gband, S0_wall_seam_removed=(ctx.scl & ~repl & ~wrepl) | wband | gband,
                  S2_wall_seam_kept=(ctx.sop & ~repl) | wband, S2_wall_seam_removed=(ctx.sop & ~repl & ~wrepl) | wband)
    S = {}; G = {}; realm = {k: B.poly_mask(v, ctx.X, ctx.Z) for k, v in B.REALMS.items()}; C1 = ops.get('closure_1b') or {}
    for name, solid in solids.items():
        closed = name.startswith('S0'); base = ctx.rcl if closed else ctx.rop; wk = ctx.walkable(new, solid, sl)
        Rm = B.flood(wk, B.SEED_NODE); G[name] = Rm; S[name] = state_report(ctx, Rm, base, new, solid, closed)
        top = (diff > 3) & Rm; S[name]['cliff_top_reach_by_realm_ha'] = {k: round(float((top & v).sum() * 16 / 1e4), 2) for k, v in realm.items()}
        log(f"  {name:22s} reach {S[name]['reach_ha']} ha (base {S[name]['reach_ha_base']}), new {S[name]['new_ha']} lost {S[name]['lost_ha']}, edge {S[name]['world_edge_reach_m']} m, cliff top {S[name]['cliff_top_reach_ha']} ha")
    out['wall_band_source'] = wsrc; out['seam_runs'] = wids
    out['states'] = dict(doc='S0 = gate closed, S2 = gate open (fact hwanggyeong_south_gate). wall = the long wall band; seam kept / removed = forest runs ' + ', '.join(wids) +
                             ' kept (Enclosure305 v305.4a) or removed (v305.4b). The four keys S0_as_built / S0_after_wall / S2 / S2_after_wall above keep their stage-1a meaning (no wall + seam kept / wall + seam removed)', **S)
    # ---- S1 / S2 + 국: the lift at the declared coordinates and the one-way descent from the top
    one = {}
    for bay in ops.get('bays', []):
        if SC.stage_rank(st, bay['stage']) > SC.stage_rank(st, stage): continue
        T = R['top_' + bay['id']]; seg_d = [d for d in ops['segments'] if d['id'] == bay['segment']][0]; seg = SC.Seg(SC.seg_for_stage(seg_d, st, stage)); e = {}
        chain = ops.get('drop_chain') or {}; land = chain.get('landing', {}).get('xz'); e['declared_descent'] = dict(id=chain.get('id'), landing_xz=land, drops_m=chain.get('all_drops_m'), rule=rule)
        for cfgname in ('S0_wall_seam_kept', 'S0_wall_seam_removed', 'S2_wall_seam_kept'):
            solid = solids[cfgname]; wk = ctx.walkable(new, solid, sl); expect = G[cfgname] | T
            li, lj = (int(round(land[1] / 4)), int(round(land[0] / 4))) if land else (None, None)
            e.setdefault('descent_landing_in_state_reach', {})[cfgname] = bool(land and G[cfgname][max(0, li - 1):li + 2, max(0, lj - 1):lj + 2].any())
            for rn, L in (('i_as_coded_any_contact_refreshes', None), ('ii_free_fall_6m_lethal', lethal)):
                Dm = descent_flood(ctx, new, solid, wk, T, L); nm = f"{'S1' if cfgname.startswith('S0') else 'S2_guk'}__{cfgname}__{rn}"
                rep_ = descent_report(ctx, new, Dm, expect, G[cfgname], nm, wk); rep_['leaves_top_ha'] = round(float((Dm & ~T & wk).sum() * 16 / 1e4), 2)
                rep_['lands_inside_state_reach_ha'] = round(float((Dm & ~T & G[cfgname]).sum() * 16 / 1e4), 2); e[nm] = rep_; G['descent_' + bay['id'] + '_' + nm] = Dm
                log(f"  descent {nm}: leaves the top {rep_['leaves_top_ha']} ha, outside expected {rep_['walkable_outside_expected_ha']} ha in {len(rep_['outside_components'])} component(s), late newly {len(rep_['late_items_newly_reached'])}")
        # rim audit on this height and on stage 1a
        A, aud, inbay = rim_audit(new, ctx.h, seg, T, bay, sl); e['rim_audit'] = aud
        if h1a_path and Path(h1a_path).exists():
            h1a = B.load_height(h1a_path); sl1 = B.slope_deg(h1a); wk1 = ctx.walkable(h1a, ctx.scl & ~repl, sl1)
            ring = B.poly_mask(seg_d['points'], ctx.X, ctx.Z); li0, lj0 = int(round(bay['landing'][1] / 4)), int(round(bay['landing'][0] / 4)); T1 = B.flood(wk1, (li0, lj0)) if wk1[li0, lj0] else np.zeros_like(wk1)
            _, aud1, _ = rim_audit(h1a, ctx.h, seg, T1, None, sl1)
            # lattice-edge form of the 1a statement (post_1a_verify 1-3): lowest step from a top node to a lower neighbour outside the top (edge-slope flood, |dy| < 4 m)
            T1e = B.flood_edges(h1a, ~ctx.water & ctx.inside, (li0, lj0), 4.0); steps = []
            for di, dj in ((0, 1), (0, -1), (1, 0), (-1, 0)):
                nb = SC.shifted(T1e, -di, -dj, True); hn = SC.shifted(h1a, -di, -dj, np.nan); m = T1e & ~nb; steps += (h1a[m] - hn[m]).tolist()
            steps = np.array(steps); e['rim_audit_stage_1a'] = dict(aud1, edge_flood_top_ha=round(float(T1e.sum() * 16 / 1e4), 2), boundary_edges=int(len(steps)), boundary_step_min_m=round(float(steps.min()), 1) if len(steps) else None,
                                                                boundary_steps_up=int((steps < 0).sum()), expect='post_1a_verify 1-3: 4.24 ha island, 264 edges, min 11.2 m')
        lipcfg = dict(C1.get('lips') or {}); lipcfg.setdefault('block_m', float(rule.get('drop_m', [3.6])[0]))
        if 'inset_m' not in lipcfg or 'max_gap_m' not in lipcfg: raise SystemExit('REFUSED: ops closure_1b.lips needs inset_m / max_gap_m (data, not code)')
        wk0 = ctx.walkable(new, solids['S0_wall_seam_kept'], sl); runs, land_ = lip_lines(ops, new, seg, A, inbay, wk0, G['S0_wall_seam_kept'], T, lipcfg)
        e['lips'] = dict(count=len(runs), total_length_m=round(sum(r['length_m'] for r in runs), 1), required_length_m=round(sum(r['length_m'] for r in runs if r['required']), 1),
                         by_reason_m={k: round(sum(r['length_m'] for r in runs if k == r['reason']), 1) for k in ('E305_005 joint', 'survivable descent', 'slip band')},
                         rim_stations=len(A), stations_landing_inside_ea=int(sum(1 for l in land_ if l[0] is True)), stations_landing_outside_ea=int(sum(1 for l in land_ if l[0] is False)), stations_no_landing_found=int(sum(1 for l in land_ if l[0] is None)))
        # the rim as data for the editor rim probe (CliffBoundary308 rim:<scene>): one station per 4 m, inward normal, where a slide would land
        r2 = lambda v: [round(float(x), 2) for x in v]; lipped = np.zeros(len(A), bool)
        for run_ in runs:
            for k_ in range(len(A)):
                if run_['s_from_to_m'][0] - 0.1 <= float(seg.s[k_]) <= run_['s_from_to_m'][1] + 0.1 or (run_['s_from_to_m'][0] > run_['s_from_to_m'][1] and (float(seg.s[k_]) >= run_['s_from_to_m'][0] - 0.1 or float(seg.s[k_]) <= run_['s_from_to_m'][1] + 0.1)):
                    if run_['required']: lipped[k_] = True
        e['rim_stations'] = dict(segment=seg.id, step_m=4.0, closed=bool(seg.closed), x=r2(seg.P[:, 0]), z=r2(seg.P[:, 1]), nx=[round(float(v), 4) for v in seg.nU[:, 0]], nz=[round(float(v), 4) for v in seg.nU[:, 1]], drop_m=[round(float(v), 1) for v in A[:, 2]],
                                 in_bay_window=[int(v) for v in inbay], lip_required=[int(v) for v in lipped], foot_in_state_reach=[(-1 if l[0] is None else int(l[0])) for l in land_],
                                 foot_xz=[l[1] if l[1] is not None else [0, 0] for l in land_], state='S0_wall_seam_kept',
                                 doc='normal (nx, nz) points INTO the top. foot_in_state_reach: 1 = the first walkable ground outside the rim lies in the closed-state reach (EA side), 0 = it does not (blocked side), -1 = none found within 60 m')
        e['_lips'] = runs; e['_lipcfg'] = lipcfg; one[bay['id']] = e
    out['one_way_descent'] = dict(doc='from the reachable cliff-top component: walk + survivable descents (see descent_flood). Rule i = the session rule as coded (WorldMacroPlaytestSession.Terrain.cs:54-55: the fall '
                                      'reference is refreshed whenever the motor is grounded, and PlayerMotor.Locomotion.cs:235-239 calls any contact below grounded, steep or not) - a slide down an 84 deg face is '
                                      'survivable: worst case. Rule ii = only walkable ground refreshes it: a descent 6.0 m below the last walkable stance is lethal. expected = the top + the reach of that state '
                                      '(the declared descent chain lands inside it). Lattice result; the editor rim probe (CliffBoundary308 rim:<scene>) and Play decide', **{k: {kk: vv for kk, vv in v.items() if not kk.startswith('_')} for k, v in one.items()})
    if h1a_path and Path(h1a_path).exists(): out['dispositions'] = disposition_rows(ctx, ops, new, B.load_height(h1a_path), stage, rule, {k: v for k, v in G.items() if k.startswith('S')})
    # ---- acceptance summary
    s0k, s0r, s2k = S['S0_wall_seam_kept'], S['S0_wall_seam_removed'], S['S2_wall_seam_kept']
    b3 = lambda s: dict(ea_missed=len(s['ea_items_missed']), ea_items=s['ea_items'], late_newly_reached=len(s['late_items_newly_reached']), ea_reach_delta_ha=round(s['reach_ha'] - s['reach_ha_base'], 2), world_edge_reach_m=s['world_edge_reach_m'],
                        ok=bool(not s['ea_items_missed'] and not s['late_items_newly_reached'] and abs(s['reach_ha'] - s['reach_ha_base']) <= 3.0 and s['world_edge_reach_m'] == 0))
    out['AC_1b'] = dict(B3_S0_wall_seam_kept=b3(s0k), B3_S0_wall_seam_removed=b3(s0r),
                        B5_S2=dict(items_lost=len(s2k['items_lost']), reached=s2k['items_reached'], reached_base=s2k['items_reached_base'], ok=not s2k['items_lost']),
                        B23=dict(S0_cliff_top_reach_ha=s0k['cliff_top_reach_ha'], S0_seam_removed_cliff_top_reach_ha=s0r['cliff_top_reach_ha'], S2_cliff_top_reach_ha=s2k['cliff_top_reach_ha'],
                                 S2_seam_removed_cliff_top_reach_ha=S['S2_wall_seam_removed']['cliff_top_reach_ha'], S2_cliff_top_reach_by_realm_ha=s2k['cliff_top_reach_by_realm_ha'],
                                 S1_reachable_top_without_content='UP-1 only (the lift): see S1 and one_way_descent',
                                 ok=bool(s0k['cliff_top_reach_ha'] == 0 and s0r['cliff_top_reach_ha'] == 0 and s2k['cliff_top_reach_ha'] == 0)))
    return G, one


def main():
    B.utf8()
    ap = argparse.ArgumentParser(description='cliff308 closure (4 m lattice) for a stage height field')
    ap.add_argument('ops'); ap.add_argument('--height', required=True); ap.add_argument('--stage', default='1a'); ap.add_argument('--out'); ap.add_argument('--save-reach', action='store_true')
    ap.add_argument('--full', action='store_true', help='stage 1b additions (states, one-way descent, rim audit, dispositions)'); ap.add_argument('--wall-json'); ap.add_argument('--h1a', help='stage-1a height (rim audit of 1a, dispositions)')
    ap.add_argument('--tag', help='name part of the saved reach grids: reach_p<stage>_<tag>_<state>.npy'); ap.add_argument('--lips'); ap.add_argument('--beyond')
    a = ap.parse_args()
    ops = json.loads(Path(a.ops).read_text(encoding='utf-8')); hp = Path(a.height); new = B.load_height(hp)
    print(f'height {B.rel(hp)} sha256 {B.sha256(hp)}'); print(f'base   {B.rel(B.BASE_HEIGHT)} sha256 {B.sha256(B.BASE_HEIGHT)}')
    ctx = Ctx(); out, R = run(ctx, ops, new, a.stage)
    out.update(height=B.rel(hp), height_sha256=B.sha256(hp), base_sha256=B.sha256(B.BASE_HEIGHT), ops=B.rel(a.ops), ops_sha256=B.sha256(a.ops), items_source=ctx.items_source, segments305_version=ctx.runs_version,
               method='4 m lattice, 4-neighbour flood from the Seal308 new-game node (3364, 2020), walkable < 45 deg; NOT a physics proof (no mesh colliders, trees, buildings)')
    outp = Path(a.out) if a.out else B.PL / f'closure_p{a.stage}.json'
    if a.full:
        G, one = run_full(ctx, ops, new, a.stage, out, R, a.wall_json, a.tag, a.h1a); R.update(G); C1 = ops.get('closure_1b') or {}
        for bid, e in one.items():
            if a.lips:
                B.write_json(Path(a.lips), dict(version='308.lips.1', date=ops.get('date'), status='TEST data for the pieces package (CliffGuk308): where the UP-1 rim needs an authored lip (visible rock, collider inside it)',
                                                height=B.rel(hp), height_sha256=B.sha256(hp), ops=B.rel(a.ops), ops_sha256=B.sha256(a.ops), bay=bid, rules=e['_lipcfg'], summary=out['one_way_descent'][bid]['lips'],
                                                doc='line = points every 4 m, inset_m inside the ring op line on the top side of the rim; ground_y = tile-surface height there (stage 1b field); block_m = least height of the lip above that ground '
                                                    '(3.6 m > 국 2.4 + jump 1.05); max_gap_m = widest opening between two lip pieces (< the capsule diameter 0.56 m). The declared lift window (bay centre +- window) stays open: '
                                                    'shelf, ramp and the one-way descent chain. reason: "E305_005 joint" = the recorded exit condition of the stage-1a exception; "survivable descent" = under the fall rule AS CODED a slide '
                                                    'down the face there lands outside the EA reach (blocked side); "slip band" = the top leans 30-45 deg at the rim',
                                                lips=e['_lips']))
        if a.beyond:
            src = B.ENC / 'Out/Seal308/closure/beyond308.json'; S0 = json.loads(src.read_text(encoding='utf-8')); bc = C1.get('beyond') or {}
            for k in ('version', 'erode_m', 'speck_m2', 'simplify_m', 'required_fact'):
                if k not in bc: raise SystemExit(f'REFUSED: ops closure_1b.beyond.{k} missing (data, not code)')
            Rc, Ro = R['S0_wall_seam_kept'], R['S2_wall_seam_kept']; rests = sorted(set(S0.get('ea_rest_ids', [])) | set(bc.get('ea_rest_ids_add', [])))
            not_ea = [r for r in bc.get('ea_rest_ids_add', []) if not any(it['id'] == r and reached(Rc, it['x'], it['z'], 12) for it in ctx.items)]
            if not_ea: raise SystemExit(f'REFUSED: ea_rest_ids_add {not_ea} are not reached in the closed state')
            extra = dict(generated=ops.get('date'), ea_underground_volumes=S0.get('ea_underground_volumes'), underground_clusters_without_content=S0.get('underground_clusters_without_content'), underground_depth_m=S0.get('underground_depth_m'),
                         ea_rest_ids=rests, fallback_rest_ids=S0.get('fallback_rest_ids'), move_drops=S0.get('move_drops'), move_vehicle=S0.get('move_vehicle'),
                         stage='1b', states=dict(closed='S0_wall_seam_kept', open='S2_wall_seam_kept'), height=B.rel(hp), height_sha256=B.sha256(hp), ops=B.rel(a.ops), ops_sha256=B.sha256(a.ops), wall_band_source=out['wall_band_source'],
                         copied_from=dict(file=B.rel(src), sha256=B.sha256(src), keys=['ea_underground_volumes', 'underground_clusters_without_content', 'underground_depth_m', 'fallback_rest_ids', 'move_drops', 'move_vehicle', 'ea_rest_ids (+ ea_rest_ids_add)']),
                         seam_removed_variant=dict(raw_beyond_ha=round(float((R['S2_wall_seam_removed'] & ~R['S0_wall_seam_removed']).sum()) * 16 / 1e4, 1),
                                                   cells_differing_from_this_file=int(((R['S2_wall_seam_removed'] & ~R['S0_wall_seam_removed']) ^ (Ro & ~Rc)).sum())))
            B.write_json(Path(a.beyond), beyond_doc(ctx, Rc, Ro, bc, extra), indent=None)
    B.write_json(outp, out)
    if a.save_reach:
        for k, v in R.items(): np.save(B.WORK / f"reach_p{a.stage}_{a.tag + '_' if a.tag else ''}{k}.npy", v)
    ac = out['AC']; print(json.dumps(ac, ensure_ascii=False, default=B._js)); print('->', B.rel(outp))
    return 0


if __name__ == '__main__':
    sys.exit(main())
