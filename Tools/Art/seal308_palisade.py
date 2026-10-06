"""#308 D308-3 Seal308 palisade (계엄 목책) - procedural meshes + placement manifest (offline, numpy only, no paid service).

  python Tools/Art/seal308_palisade.py            # -> Art/World/Compact/Rebuild/Enclosure305/Out/Seal308/
                                                  #    Meshes/P308_{W0,W1,gate}_LOD{0,1,2}.json + *_Collision.json, unity.json,
                                                  #    footprint308.json, plan308.png, palisade308.txt (ledger + offline checks)

Contract: SPEC-WORLD-ENCLOSURE-305 §1b′ 목책 (Seal308); hand ledger Art/World/Compact/Rebuild/Enclosure305/seal308.json (every
value TEST, nothing tunable lives here). Meshes use Tools/Art/hanok297.py MB (JSON = CompactFinish297 KitMesh297 format:
{"name", "v", "n", "uv", "sub": [{"m": slot, "t": [...]}]}); material slots map to the #297 wood ink materials listed in
seal308.json (wood_dark / wood_board / iron: URP Lit, emission 0). Matte, zero emission, no light, no interaction.

Layout along the line A -> B (anchors = E305_084 start / E305_056 start; t = unit A->B, n = left normal = 상경 가도 side):
  * two rows of sharpened logs at u = +-row_offset (north row = EA face) from each end bastion to the panel; the rows touch, so the
    wing _col (u +-col_half) stays inside the post mass and the slot between the rows (where the open panel parks) is not walkable;
  * 2 rails on the north face, spear racks (3 spears leaning on the upper rail, iron blades) every ~6 m on the north face;
  * end bastions (말뚝 다발) at A and B, centred 0.5 m toward the blocked side: an octagonal _col prism inside the post ring
    overlaps the seal run's shell end (shell = 1 m to the blocked side) and the wing _col;
  * the panel P308_gate: one row of the same logs on u = 0 over the road (clear width = road + margin), 1 m into each wing's slot,
    2 iron straps; its _col (u +-0.08) is the CompactMountainGate Blocker; OpenOffset = -t * open_slide parks it inside the A wing;
  * heights: _col top = highest ground (+-3 m across, 4.5 / 6 m on the north side) + 5.5 m (phase-1 shell rule); tips = _col top +
    0.8 m + jitter; bottoms = lowest ground - 1.2 m. Ground = offline height.bytes (seal-scene re-measures the physics ground).
Object frames: pivot = anchor (wings) / closed panel centre (gate) at offline ground; local x = t, local z = n; Unity yaw =
atan2(-t.z, t.x) (Quaternion.Euler(0, yaw, 0) maps local +x to t and +z to n).
"""
import json, math, sys, time, hashlib
from pathlib import Path
import numpy as np

TOOLS = Path(__file__).resolve().parent
sys.path.insert(0, str(TOOLS))
from hanok297 import MB, beam                                         # noqa: E402

ROOT = TOOLS.parents[1]
ENC = ROOT / 'Art/World/Compact/Rebuild/Enclosure305'
OUTD = ENC / 'Out/Seal308'
A_ = ROOT / 'Oheangbu/Assets/_Project'
H4, W4, C4 = 1501, 1001, 4.0
SLOTS = ('wood_dark', 'wood_board', 'iron')


def height_sampler():
    h = np.fromfile(A_ / 'Art/World/Finish297/Surface/height.bytes', '<f4').reshape(H4, W4).astype(np.float64)

    def hs(x, z):
        j = np.clip(np.asarray(x, float) / C4, 0, W4 - 1.001); i = np.clip(np.asarray(z, float) / C4, 0, H4 - 1.001)
        j0 = np.floor(j).astype(int); i0 = np.floor(i).astype(int); fj = j - j0; fi = i - i0
        return h[i0, j0] * (1 - fj) * (1 - fi) + h[i0, j0 + 1] * fj * (1 - fi) + h[i0 + 1, j0] * (1 - fj) * fi + h[i0 + 1, j0 + 1] * fj * fi
    return hs


def main():
    S = json.loads((ENC / 'seal308.json').read_text(encoding='utf-8'))
    hs = height_sampler(); rng = np.random.default_rng(308)
    A = np.asarray(S['anchors']['A'], float); B = np.asarray(S['anchors']['B'], float)
    Lp = float(np.hypot(*(B - A))); t = (B - A) / Lp; n = np.array([-t[1], t[0]])
    yaw = math.degrees(math.atan2(-t[1], t[0]))
    po, ht, pn, bs, rl, sp = S['posts'], S['height'], S['panel'], S['bastion'], S['rails'], S['spear_racks']
    r0, gap, row, colh = po['radius_m'], po['spacing_m'], po['row_offset_m'], po['col_half_m']
    if n[1] <= 0: raise SystemExit('left normal of A->B does not point north (상경 가도 side): anchors swapped?')

    # blocked normals of the two seal runs at their palisade ends (segments305.json), for the bastion offset and the check
    segs = {s['id']: s for s in json.loads((ENC / 'segments305.json').read_text(encoding='utf-8'))['segments']}

    def run_nb(rid, at_start=True):
        P = np.asarray(segs[rid]['points'], float); d = (P[1] - P[0]) if at_start else (P[-1] - P[-2]); d /= np.hypot(*d)
        return np.array([d[1], -d[0]]) * segs[rid]['block']
    nbA = run_nb(S['anchors']['A_run']); nbB = run_nb(S['anchors']['B_run'])

    def W(s, u):               # world xz on the line
        return A + t * s + n * u

    def gmax_col(s):
        """_col top and floor at station s (phase-1 shell rule) + the player-side support it is measured against."""
        across = [W(s, u) for u in (-ht['across_m'], 0.0, ht['across_m'])]; player = [W(s, u) for u in ht['player_side_m']]
        ga = [float(hs(*p)) for p in across]; gp = [float(hs(*p)) for p in player]
        hi = max(ga + gp); lo = min(ga)
        return hi + ht['col_above_m'], lo, max(gp + [ga[2]])

    # ---- panel span (s) from the road crossing
    c = np.asarray(S['road']['crossing'], float); sc = float((c - A) @ t)
    s0 = sc - pn['clear_width_m'] / 2 + pn['shift_toward_B_m']; s1 = sc + pn['clear_width_m'] / 2 + pn['shift_toward_B_m']
    ov = pn['overlap_m']; slide = pn['open_slide_m']
    off = -t * slide; oo = np.array([off[0], 0.0, off[1]])
    if np.abs(oo - np.asarray(pn['open_offset'], float)).max() > 0.01:
        raise SystemExit(f"seal308.json panel.open_offset {pn['open_offset']} != -t * open_slide_m = {oo.round(3).tolist()} (fix the ledger)")
    bast_A = A + nbA * bs['centre_toward_blocked_m']; bast_B = B + nbB * bs['centre_toward_blocked_m']
    slot_start = bs['post_ring_radius_m'] + r0 + 0.25                 # first s the parked panel may occupy (clear of bastion A posts)
    p_closed = (s0 - ov, s1 + ov); p_open = (s0 - ov - slide, s1 + ov - slide)

    # ---- stations (every station_m) with ground / _col top
    def stations(a, b):
        k = max(1, int(math.ceil((b - a) / ht['station_m']))); return np.linspace(a, b, k + 1)
    st_all = stations(0.0, Lp)
    col = {round(float(s), 3): gmax_col(s) for s in st_all}

    def col_at(s):             # interpolate the station table
        ks = np.array(sorted(col)); v = np.array([col[k] for k in ks])
        return tuple(float(np.interp(s, ks, v[:, i])) for i in range(3))

    def ground(x, z): return float(hs(x, z))

    # ---- objects: P308_W0 (A wing + bastion A), P308_W1 (B wing + bastion B), P308_gate (panel)
    objs = {}

    def frame(origin_xz, origin_y):
        O = np.array([origin_xz[0], origin_y, origin_xz[1]])
        return lambda p: [float((np.array([p[0], p[2]]) - origin_xz) @ t), float(p[1] - origin_y), float((np.array([p[0], p[2]]) - origin_xz) @ n)]

    pivots = dict(P308_W0=(A, ground(*A)), P308_W1=(B, ground(*B)))
    pc = W((s0 + s1) / 2, 0.0); pivots['P308_gate'] = (pc, ground(*pc))
    loc = {k: frame(*v) for k, v in pivots.items()}
    mbs = {k: [MB(f'{k}_LOD{l}') for l in range(3)] for k in pivots}
    colmb = {k: MB(f'{k}_Collision') for k in pivots}
    stats = dict(posts={k: 0 for k in pivots}, spears=0, racks=0)
    tips_minus_col = []; post_records = []

    def post(obj, xz, bottom, top, rad, lean=(0.0, 0.0)):
        """Sharpened log from world y bottom to the tip apex `top`, in object `obj` (all LODs)."""
        tip = po['tip_m']; L = loc[obj]
        p0 = np.array([xz[0], bottom, xz[1]]); p1 = np.array([xz[0] + lean[0] * (top - tip - bottom), top - tip, xz[1] + lean[1] * (top - tip - bottom)])
        apex = p1 + np.array([lean[0] * tip, tip, lean[1] * tip])
        for l, sides in enumerate(po['sides_lod']):
            mbs[obj][l].cyl(L(p0), L(p1), rad, rad * 0.96, sides, 'wood_dark', uvscale=1.0)
            mbs[obj][l].cyl(L(p1), L(apex), rad * 0.96, 0.012, sides, 'wood_dark', uvscale=1.0, caps=True)
        stats['posts'][obj] += 1

    def row_posts(obj, a, b, u, extra=0.0):
        """Posts along s in [a, b] at offset u (spacing po.spacing), tips over the local _col top."""
        k = max(1, int(round((b - a) / gap))); ss = np.linspace(a, b, k + 1)
        for s in ss:
            xz = W(s, u); top_col, lo, _ = col_at(s)
            g_here = ground(*xz); bottom = min(lo, g_here) - ht['bury_m']
            top = top_col + ht['tip_over_col_m'] + extra + rng.uniform(0, po['height_jitter_m'])
            lean = math.tan(math.radians(rng.uniform(-po['lean_deg'], po['lean_deg'])))     # along the row only: the cross section stays put
            post(obj, xz, bottom, top, r0 * rng.uniform(*po['radius_jitter']), (float(t[0] * lean), float(t[1] * lean)))
            tips_minus_col.append((top - top_col, top - po['tip_m'] - top_col)); post_records.append((obj, float(s), float(u)))

    def strip_col(obj, a, b, half, extra_end=0.0):
        """Closed box strip (renderer-less _col) along s in [a, b], u in [-half, +half], bottom = lowest ground - 1, top = _col top."""
        L = loc[obj]; ss = stations(a, b); mb = colmb[obj]
        V = []
        for s in ss:
            top_col, lo, _ = col_at(s); y0 = lo - 1.0
            V.append([W(s, -half), W(s, half), y0, top_col])

        def P(xz, y): return np.array(L(np.array([xz[0], y, xz[1]])))
        for i in range(len(V) - 1):
            (a0, a1, y0, y1), (b0, b1, z0, z1) = V[i], V[i + 1]
            mb.quad(P(a1, y0), P(b1, z0), P(b1, z1), P(a1, y1), 'col', np.array([0, 0, 1.0]))      # north face (+z local)
            mb.quad(P(a0, y0), P(a0, y1), P(b0, z1), P(b0, z0), 'col', np.array([0, 0, -1.0]))     # south face
            mb.quad(P(a0, y1), P(a1, y1), P(b1, z1), P(b0, z1), 'col', np.array([0, 1.0, 0]))      # top
            mb.quad(P(a0, y0), P(b0, z0), P(b1, z0), P(a1, y0), 'col', np.array([0, -1.0, 0]))     # bottom (buried)
        (a0, a1, y0, y1) = V[0]; mb.quad(P(a0, y0), P(a1, y0), P(a1, y1), P(a0, y1), 'col', np.array([-1.0, 0, 0]))
        (b0, b1, z0, z1) = V[-1]; mb.quad(P(b0, z0), P(b0, z1), P(b1, z1), P(b1, z0), 'col', np.array([1.0, 0, 0]))
        return [(float(s), V[k][2], V[k][3]) for k, s in enumerate(ss)]

    def prism_col(obj, cxz, rad, y0, y1, sides=8):
        L = loc[obj]; mb = colmb[obj]; ang = np.linspace(0, 2 * math.pi, sides + 1)[:-1] + math.pi / sides
        ring = lambda y: [np.array(L(np.array([cxz[0] + rad * math.cos(a), y, cxz[1] + rad * math.sin(a)]))) for a in ang]
        lo_, hi_ = ring(y0), ring(y1); ctr = np.array(L(np.array([cxz[0], 0, cxz[1]]))); ctr[1] = 0
        for i in range(sides):
            j = (i + 1) % sides; o = (lo_[i] + lo_[j]) / 2 - np.array([ctr[0], lo_[i][1], ctr[2]]); o[1] = 0
            mb.quad(lo_[i], lo_[j], hi_[j], hi_[i], 'col', o)
        mb.poly(hi_, 'col', np.array([0, 1.0, 0])); mb.poly(lo_[::-1], 'col', np.array([0, -1.0, 0]))

    # ---- wings
    rows = (row, -row)
    for obj, a, b in (('P308_W0', 0.0, s0), ('P308_W1', s1, Lp)):
        for u in rows: row_posts(obj, a + (bs['post_ring_radius_m'] if obj == 'P308_W0' else 0.0), b - (bs['post_ring_radius_m'] if obj == 'P308_W1' else 0.0), u)
    wing_col = {'P308_W0': strip_col('P308_W0', 0.0, s0, colh), 'P308_W1': strip_col('P308_W1', s1, Lp, colh)}
    # bastions
    bast = {}
    for obj, cxz, sa in (('P308_W0', bast_A, 0.0), ('P308_W1', bast_B, Lp)):
        top_col, lo, _ = col_at(sa); gl = [ground(*(cxz + np.array([math.cos(a), math.sin(a)]) * bs['post_ring_radius_m'])) for a in np.linspace(0, 2 * math.pi, 9)[:-1]]
        lo_b = min([lo] + gl); top_b = max(top_col, max(gl) + ht['col_above_m'])
        for k in range(22):
            a = 2 * math.pi * k / 22; xz = cxz + np.array([math.cos(a), math.sin(a)]) * bs['post_ring_radius_m']
            post(obj, xz, lo_b - ht['bury_m'], top_b + ht['tip_over_col_m'] + bs['extra_height_m'] + rng.uniform(0, po['height_jitter_m']), r0 * 1.08)
        for k in range(8):
            a = 2 * math.pi * (k + .5) / 8; xz = cxz + np.array([math.cos(a), math.sin(a)]) * 0.8
            post(obj, xz, lo_b - ht['bury_m'], top_b + ht['tip_over_col_m'] + bs['extra_height_m'] + 0.3 + rng.uniform(0, po['height_jitter_m']), r0 * 1.1)
        post(obj, cxz, lo_b - ht['bury_m'], top_b + ht['tip_over_col_m'] + bs['extra_height_m'] + 0.6, r0 * 1.2)
        prism_col(obj, cxz, bs['col_radius_m'], lo_b - 1.0, top_b)
        bast[obj] = dict(centre=[round(float(cxz[0]), 3), round(float(cxz[1]), 3)], col_radius=bs['col_radius_m'], col_bottom=round(lo_b - 1.0, 2), col_top=round(top_b, 2),
                         post_ring=bs['post_ring_radius_m'])
    # rails (north face) + spear racks
    ur = row + r0 + rl['gap_from_posts_m'] + rl['section_m'][0] / 2
    for obj, a, b in (('P308_W0', bs['post_ring_radius_m'] + r0 + 0.1, s0 - 0.1), ('P308_W1', s1 + 0.1, Lp - bs['post_ring_radius_m'] - r0 - 0.1)):
        ss = stations(a, b); L = loc[obj]
        for hgt in rl['heights_m']:
            for s_a, s_b in zip(ss[:-1], ss[1:]):
                pa = W(s_a, ur); pb = W(s_b, ur)
                P0 = np.array([pa[0], ground(*W(s_a, row)) + hgt, pa[1]]); P1 = np.array([pb[0], ground(*W(s_b, row)) + hgt, pb[1]])
                for l in range(3): beam(mbs[obj][l], L(P0), L(P1), rl['section_m'][0], rl['section_m'][1], 'wood_board')
        # racks: every_m, clear of the panel and the bastions
        lo_s = (bs['post_ring_radius_m'] + sp['keep_clear_of_bastion_m']) if obj == 'P308_W0' else (s1 + sp['keep_clear_of_panel_m'])
        hi_s = (s0 - sp['keep_clear_of_panel_m']) if obj == 'P308_W0' else (Lp - bs['post_ring_radius_m'] - sp['keep_clear_of_bastion_m'])
        for sr in np.arange(lo_s + sp['every_m'] / 2, hi_s, sp['every_m']):
            stats['racks'] += 1
            for q in range(sp['spears']):
                s_q = sr + (q - (sp['spears'] - 1) / 2) * 0.38
                g = ground(*W(s_q, row)); top_rail = g + rl['heights_m'][1]
                base = W(s_q + rng.uniform(-.05, .05), ur + rl['section_m'][0] / 2 + sp['lean_base_m']); lean_top = W(s_q, ur + rl['section_m'][0] / 2 + 0.03)
                P0 = np.array([base[0], ground(*base) - 0.05, base[1]]); P1 = np.array([lean_top[0], top_rail + 0.55, lean_top[1]])
                d = (P1 - P0) / np.linalg.norm(P1 - P0); P2 = P1 + d * sp['blade_m']
                for l in range(2):   # LOD2: no spears
                    beam(mbs[obj][l], L(P0), L(P1), 0.045, 0.045, 'wood_board')
                    mbs[obj][l].cyl(L(P1), L(P2), 0.035, 0.003, 4, 'iron', uvscale=1.0, caps=True)
                stats['spears'] += 1
    # ---- panel (u = 0), straps; bottoms cover the closed AND the parked span
    lo_panel = min(min(col_at(s)[1] for s in stations(*p_closed)), min(ground(*W(s, 0)) for s in stations(*p_open)))
    k = max(1, int(round((p_closed[1] - p_closed[0]) / gap))); ss = np.linspace(p_closed[0] + r0 * .5, p_closed[1] - r0 * .5, k)
    for s in ss:
        xz = W(s, 0.0); top_col, lo, _ = col_at(s)
        top = top_col + ht['tip_over_col_m'] + rng.uniform(0, po['height_jitter_m'] * .5)
        post('P308_gate', xz, lo_panel - ht['bury_m'], top, r0)
        tips_minus_col.append((top - top_col, top - po['tip_m'] - top_col)); post_records.append(('P308_gate', float(s), 0.0))
        for hstrap in pn['straps_m']:
            gy = ground(*xz); L = loc['P308_gate']
            for l, sides in enumerate(po['sides_lod']):
                if l == 2: continue
                mbs['P308_gate'][l].cyl(L(np.array([xz[0], gy + hstrap, xz[1]])), L(np.array([xz[0], gy + hstrap + 0.08, xz[1]])), r0 + 0.015, r0 + 0.015, sides, 'iron', uvscale=1.0)
    panel_col = strip_col('P308_gate', p_closed[0], p_closed[1], pn['col_half_m'])

    # ---- export meshes
    OUTD.mkdir(parents=True, exist_ok=True); (OUTD / 'Meshes').mkdir(exist_ok=True)
    tris = {}
    for obj in pivots:
        for l in range(3): mbs[obj][l].export(OUTD / f'Meshes/{obj}_LOD{l}.json')
        colmb[obj].export(OUTD / f'Meshes/{obj}_Collision.json')
        tris[obj] = [mbs[obj][l].tris() for l in range(3)] + [colmb[obj].tris()]

    # ---- checks (offline)
    gen = json.loads((ROOT / 'Art/World/Compact/Rebuild/Architecture296/Generated/routes.json').read_text(encoding='utf-8'))['routes']
    road = [r for r in gen if r['id'] == S['route'].split(' ')[0]][0]; RP = np.array([(p['x'], p['z']) for p in road['points']], float)
    dense = np.concatenate([np.linspace(a, b, max(2, int(np.hypot(*(b - a)) / .5) + 1)) for a, b in zip(RP[:-1], RP[1:])])
    rel = dense - A; ds = rel @ t; du = rel @ n; near = np.abs(du) <= 6.0
    road_s = (float(ds[near].min()), float(ds[near].max())) if near.any() else None
    half_w = S['road']['width_m'] / 2
    # the road (centreline +- half width, measured along the line at the crossing angle) + 1 m must lie inside the clear span
    k = int(np.argmin(np.abs(du))); kd = dense[min(k + 4, len(dense) - 1)] - dense[max(k - 4, 0)]; kd = kd / np.hypot(*kd)
    sin_a = abs(kd @ n); edge = half_w / max(sin_a, 0.2); sx = float(ds[k])
    road_edges_s = (sx - edge, sx + edge); road_in_span = bool(s0 <= road_edges_s[0] - 1.0 and road_edges_s[1] + 1.0 <= s1)
    wing_pts = [W(s, u) for obj, s, u in post_records if obj != 'P308_gate']
    wing_route = min(float(np.min(np.hypot(dense[:, 0] - p[0], dense[:, 1] - p[1]))) for p in wing_pts)
    shellA = A + nbA * 1.0; shellB = B + nbB * 1.0
    checks = dict(
        tips_over_col_min=round(min(a for a, _ in tips_minus_col), 3), shoulders_over_col_min=round(min(b for _, b in tips_minus_col), 3),
        tips_rule='apex >= _col top + tip_over_col; the full-width log (below the 0.55 m point) still reaches above the _col top',
        col_over_player_ground_min=round(min(col[k][0] - col[k][2] for k in col), 3),
        col_over_player_ground_rule=f">= {ht['min_block_m']} m (Spec D305 floor) and = col_above {ht['col_above_m']} m by construction",
        slot_clearance_m=round(row - r0 * po['radius_jitter'][1] - r0 - 0.015, 3),
        wing_col_inside_rows=bool(colh <= row + math.sqrt(max(0.0, (r0 * po['radius_jitter'][0]) ** 2 - (gap / 2) ** 2)) - 1e-6),
        wing_col_cover_m=round(row + math.sqrt(max(0.0, (r0 * po['radius_jitter'][0]) ** 2 - (gap / 2) ** 2)), 3),
        panel_closed_s=[round(p_closed[0], 2), round(p_closed[1], 2)], panel_open_s=[round(p_open[0], 2), round(p_open[1], 2)],
        clear_span_s=[round(s0, 2), round(s1, 2)], slot_s=[round(slot_start, 2), round(s0, 2)],
        open_panel_inside_slot=bool(p_open[0] >= slot_start and p_open[1] <= s0 - 0.3),
        open_panel_clears_span=bool(p_open[1] <= s0 - 0.3),
        road_in_clear_span_1m=road_in_span, road_edges_s=[round(road_edges_s[0], 2), round(road_edges_s[1], 2)], road_crossing_angle_deg=round(math.degrees(math.asin(min(1.0, sin_a))), 1),
        road_centreline_s_within_6m=None if road_s is None else [round(road_s[0], 2), round(road_s[1], 2)],
        wing_post_to_road_centreline_min_m=round(wing_route, 2), wing_route_rule=f'>= road half width {half_w} m + 1 m',
        bastion_A_to_shell_end_m=round(float(np.hypot(*(shellA - bast_A))), 3), bastion_B_to_shell_end_m=round(float(np.hypot(*(shellB - bast_B))), 3),
        bastion_rule=f"shell end centre within col radius {bs['col_radius_m']} - 0.35 m (half shell thickness + 0.05)",
        emission='0 (materials reused, _EmissionColor 0; no light, no interaction)')
    ok = (checks['tips_over_col_min'] >= ht['tip_over_col_m'] - 1e-6 and checks['shoulders_over_col_min'] > 0 and checks['col_over_player_ground_min'] >= ht['min_block_m'] and checks['slot_clearance_m'] > 0
          and checks['wing_col_inside_rows'] and checks['open_panel_inside_slot'] and checks['road_in_clear_span_1m']
          and wing_route >= half_w + 1.0 and max(checks['bastion_A_to_shell_end_m'], checks['bastion_B_to_shell_end_m']) <= bs['col_radius_m'] - 0.35)
    checks['ALL'] = 'PASS' if ok else 'FAIL'

    # ---- manifest + footprint
    def V3(xz, y): return [round(float(xz[0]), 3), round(float(y), 3), round(float(xz[1]), 3)]
    ob_lo = min(col_at(s)[1] for s in stations(*p_closed)) - 1.5; ob_hi = max(col_at(s)[0] for s in stations(*p_closed))
    obc = W((p_closed[0] + p_closed[1]) / 2, 0.0)
    mats = S['materials']
    src = hashlib.sha256((ENC / 'seal308.json').read_bytes() + (ENC / 'segments305.json').read_bytes() + (A_ / 'Art/World/Finish297/Surface/height.bytes').read_bytes()).hexdigest()[:16]
    manifest = dict(
        version='308.1', compound='Seal308', root='Seal308', generated=time.strftime('%Y-%m-%dT%H:%M:%S'), source_hash=src,
        spec='SPEC-WORLD-ENCLOSURE-305 §1b′ 씬 구성: Seal308 (identity, layer 0) > Wings/P308_W* (MeshRenderer + child *_col MeshCollider, static) + '
             'Panel/P308_gate (MeshRenderer + CompactMountainGate) + child P308_gate_col (Blocker) ; Panel/P308_gate_ob (NavMeshObstacle, static)',
        frame='mesh vertices are object-local: x = t (A->B), y up, z = n (상경 가도 side); place each object at `position` with Quaternion.Euler(0, yaw, 0)',
        mesh_dir='Art/World/Compact/Rebuild/Enclosure305/Out/Seal308/Meshes', asset_dir='Assets/_Project/Art/World/Enclosure305/Seal308',
        materials={'wood_dark': mats['post'], 'wood_board': mats['rail'], 'iron': mats['iron']}, collision_slot='col (no material, MeshCollider only)',
        lod_screen_heights=[.16, .045, .007],
        meshes=[dict(name='P308_W0', kind='wing', parent='Wings', world=False, position=V3(*pivots['P308_W0']), yaw=round(yaw, 4), collider='P308_W0_col',
                     static=['BatchingStatic', 'OccludeeStatic', 'NavigationStatic'], lods=3, note='A wing (holds the open panel between its rows) + bastion A'),
                dict(name='P308_W1', kind='wing', parent='Wings', world=False, position=V3(*pivots['P308_W1']), yaw=round(yaw, 4), collider='P308_W1_col',
                     static=['BatchingStatic', 'OccludeeStatic', 'NavigationStatic'], lods=3, note='B wing + bastion B'),
                dict(name='P308_gate', kind='panel', parent='Panel', world=False, position=V3(*pivots['P308_gate']), yaw=round(yaw, 4), collider='P308_gate_col',
                     static=[], lods=3, note='moves: not static; its _col is the only Blocker')],
        gate=dict(component='Oheangbu.App.World.CompactMountainGate', on='Seal308/Panel/P308_gate', Session='the scene WorldMacroPlaytestSession',
                  RequiredCompleted='WorldMacroPlaytestSession.SouthGateOpenedId (= hwanggyeong_south_gate, copy the constant)',
                  Panel='Seal308/Panel/P308_gate', OpenOffset=[round(float(x), 3) for x in oo], Blockers=['Seal308/Panel/P308_gate/P308_gate_col'],
                  Obstacle='Seal308/Panel/P308_gate_ob', open_speed='0.7/s fixed in CompactMountainGate (Spec Temporary Exception)',
                  parent_note='Seal308 and Seal308/Panel stay identity: OpenOffset is then a world vector'),
        obstacle=dict(name='P308_gate_ob', parent='Panel', shape='Box', carving=True, carveOnlyStationary=True, position=V3(obc, (ob_lo + ob_hi) / 2), yaw=round(yaw, 4),
                      size=[round(p_closed[1] - p_closed[0], 3), round(ob_hi - ob_lo, 3), 1.2], note='size = (x along t, y, z across); static, not under P308_gate'),
        bastions=bast,
        stations=[dict(s=round(float(s), 2), xz=[round(float(W(s, 0)[0]), 2), round(float(W(s, 0)[1]), 2)], col_top=round(col[k][0], 2), ground_lo=round(col[k][1], 2),
                       player_support=round(col[k][2], 2), part='wing_A' if s < s0 else 'panel' if s <= s1 else 'wing_B') for k, s in zip(sorted(col), sorted(col))],
        wing_col=dict({k: [[round(a, 2), round(b, 2), round(c_, 2)] for a, b, c_ in v] for k, v in wing_col.items()}, half=colh, note='[s, bottom y, top y] per station'),
        panel_col=dict(stations=[[round(a, 2), round(b, 2), round(c_, 2)] for a, b, c_ in panel_col], half=pn['col_half_m']),
        counts=dict(posts=stats['posts'], spear_racks=stats['racks'], spears=stats['spears'], tris={k: dict(LOD0=v[0], LOD1=v[1], LOD2=v[2], collision=v[3]) for k, v in tris.items()}),
        checks=checks)
    (OUTD / 'unity.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=1), encoding='utf-8')
    cut = 2.5   # lattice wings stop 2.5 m short of the panel: the 3 m lattice band must not touch the road corridor (closure)
    foot = dict(version='308.1', line=[A.round(3).tolist(), B.round(3).tolist()],
                wings=[[W(0.0, 0).round(3).tolist(), W(s0 - cut, 0).round(3).tolist()], [W(s1 + cut, 0).round(3).tolist(), W(Lp, 0).round(3).tolist()]],
                wings_physical=[[W(0.0, 0).round(3).tolist(), W(s0, 0).round(3).tolist()], [W(s1, 0).round(3).tolist(), W(Lp, 0).round(3).tolist()]],
                bastions=[[*bast_A.round(3).tolist(), bs['post_ring_radius_m'] + r0], [*bast_B.round(3).tolist(), bs['post_ring_radius_m'] + r0]],
                panel_closed=[W(p_closed[0], 0).round(3).tolist(), W(p_closed[1], 0).round(3).tolist()],
                panel_open=[W(p_open[0], 0).round(3).tolist(), W(p_open[1], 0).round(3).tolist()],
                note='world (x, z). wings = lattice wings (2.5 m short of the clear span; the closed panel band covers the rest); wings_physical = posts')
    (OUTD / 'footprint308.json').write_text(json.dumps(foot), encoding='utf-8')
    plan(A, B, t, n, s0, s1, p_closed, p_open, bast_A, bast_B, post_records, W, dense, nbA, nbB)
    lines = [f"#308 Seal308 palisade | {manifest['generated']} | hash {src} | TEST, offline height.bytes (seal-scene re-measures)",
             f"line A {A.round(2).tolist()} -> B {B.round(2).tolist()} length {Lp:.2f} m yaw {yaw:.3f}; road crossing s={sc:.2f}; clear span s {s0:.2f}..{s1:.2f}; panel closed {p_closed[0]:.2f}..{p_closed[1]:.2f}, open {p_open[0]:.2f}..{p_open[1]:.2f} (slot {slot_start:.2f}..{s0:.2f})",
             f"OpenOffset {oo.round(3).tolist()} ; obstacle centre {manifest['obstacle']['position']} size {manifest['obstacle']['size']}",
             f"posts {stats['posts']} spear racks {stats['racks']} spears {stats['spears']}; tris {json.dumps(manifest['counts']['tris'])}",
             f"_col top - player-side ground min {checks['col_over_player_ground_min']} m; tips - _col top min {checks['tips_over_col_min']} m (shoulders {checks['shoulders_over_col_min']} m); col top range {min(v[0] for v in col.values()):.2f}..{max(v[0] for v in col.values()):.2f}",
             'checks ' + json.dumps(checks, ensure_ascii=False)]
    (OUTD / 'palisade308.txt').write_text('\n'.join(lines) + '\n', encoding='utf-8')
    print('\n'.join(lines))
    return ok


def plan(A, B, t, n, s0, s1, pc, po_, bA, bB, posts, W, road, nbA, nbB):
    from PIL import Image, ImageDraw, ImageFont
    sc = 12.0; ctr = (A + B) / 2; size = 900
    img = Image.new('RGB', (size, 560), (240, 236, 226)); d = ImageDraw.Draw(img)
    try: font = ImageFont.truetype('malgun.ttf', 12)
    except OSError: font = None

    def P(xz): return (size / 2 + (xz[0] - ctr[0]) * sc, 280 - (xz[1] - ctr[1]) * sc)
    rd = road[np.hypot(road[:, 0] - ctr[0], road[:, 1] - ctr[1]) < 45]
    for p in rd: x, y = P(p); d.ellipse([x - 36, y - 36, x + 36, y + 36], fill=(222, 214, 196))
    for p in rd: x, y = P(p); d.point((x, y), fill=(120, 100, 70))
    segs = json.loads((ENC / 'segments305.json').read_text(encoding='utf-8'))['segments']
    for s in segs:
        Q = np.asarray(s['points'], float)
        if np.min(np.hypot(Q[:, 0] - ctr[0], Q[:, 1] - ctr[1])) > 60: continue
        d.line([P(q) for q in Q], fill=(170, 30, 30), width=2)
        nb = np.array([Q[1, 1] - Q[0, 1], -(Q[1, 0] - Q[0, 0])]); nb = nb / np.hypot(*nb) * s['block']
        d.line([P(q + nb) for q in Q], fill=(60, 60, 60), width=1)
    for obj, s, u in posts:
        x, y = P(W(s, u)); r = 0.19 * sc; d.ellipse([x - r, y - r, x + r, y + r], outline=(80, 50, 30) if obj != 'P308_gate' else (30, 80, 160))
    for b in (bA, bB):
        x, y = P(b); r = 1.45 * sc; d.ellipse([x - r, y - r, x + r, y + r], outline=(80, 50, 30), width=2)
    d.line([P(W(po_[0], 0)), P(W(po_[1], 0))], fill=(30, 120, 200), width=3)
    d.text((10, 10), 'Seal308 plan (12 px/m): red = seal run lines, grey = shell (1 m to the blocked side), blue line = parked (open) panel, '
                     'brown = wing posts, blue circles = panel posts (closed)', fill=(0, 0, 0), font=font)
    d.text(P(A + n * 4), 'A (E305_084)', fill=(0, 0, 0), font=font); d.text(P(B + n * 4), 'B (E305_056)', fill=(0, 0, 0), font=font)
    d.text(P(W((s0 + s1) / 2, 6)), 'N: 상경 가도 (EA)', fill=(0, 0, 0), font=font); d.text(P(W((s0 + s1) / 2, -8)), 'S: 적로', fill=(0, 0, 0), font=font)
    img.save(OUTD / 'plan308.png')


if __name__ == '__main__':
    sys.exit(0 if main() else 1)
