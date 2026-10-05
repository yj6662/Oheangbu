"""#308 D308-3 seal before the south gate - 4 m closure proof (offline). SPEC-WORLD-ENCLOSURE-305 §1b′ AC-S2 / AC-S3 / AC-S4.

  python Tools/Art/seal308_closure.py closed          # palisade + 4 capital doors closed, new-game seed
  python Tools/Art/seal308_closure.py open            # south-gate fact: palisade panel + capital doors open
  python Tools/Art/seal308_closure.py baseline        # no seal runs / palisade, doors open (the world before #308)
  python Tools/Art/seal308_closure.py all             # closed + open + baseline + Out/beyond308.json + Seal308/closure308.txt
  options: --seed newgame|routes   (default newgame = first MainPath node outside the mine portal; routes = every route node)
           --draft                 (seal runs from lines305.json + OVERRIDES before `enclosure305.py segments` wrote them)
           --omit panel,doors,E305_130,...   sensitivity: closed runs with one piece removed must report leaks
                                   (writes Seal308/seal308-closed-omit-<x>.json only, never the main results)

Lattice = emptiness305.py up to its walkability step (4 m nodes: slope < 45, no deep water, inside the terrain, route corridors
forced walkable), re-used exactly like closure305.py. On top of it (every number TEST):
  * barrier runs (segments305.json, phase 1 + seal308) are 6 m solid bands (half 3.0 m >= lattice diagonal); a route corridor
    keeps a band open (= a pass stays open, reported as a cut: AC-S4 wants 0 for the seal runs);
  * walls (capital loop, 철옹, 황경 궁성) are solid 3.0 m half bands without route override - except at their apertures; the
    four capital doors that read the south-gate fact (layout.json apertures `fact`) are solid in `closed`, open in `open`;
  * the Seal308 palisade (Out/Seal308/footprint308.json from seal308_palisade.py): wings + end bastions are barrier bands
    (route override = cut report), the panel is solid over the road in `closed` and gone in `open` (it parks inside a wing).
Content inventory (layout Places, checkpoints, content Points, encounters, mountain-trail entries, architecture arenas, 철옹/
황경 markers) is classified EA / late: EA = prologue mine + 청림 + 황경 성저·남문 outside the wall (LDB-EA), i.e. layout realm
cheongrim, or the options-B zone 청림/상경 가도 outside the capital loop for everything else; jeokro/cheolong/hyeongang places
and the capital interior are late. An item is reached when a flooded node lies within its radius (12 m, places up to 30 m).
Underground items (> 6 m below the height field: the mine) are reported apart: the 2-D flood cannot see them.
Outputs: Enclosure305/Seal308/seal308-<mode>.json + .png, closure308.txt; Enclosure305/Out/beyond308.json (seal-scene input:
beyond = open reach - closed reach, eroded 8 m, as rings; EA mine boxes; EA rest ids).
"""
import json, math, sys, hashlib, time
from collections import deque
from pathlib import Path
import numpy as np

TOOLS = Path(__file__).resolve().parent
sys.path.insert(0, str(TOOLS))
import enclosure305 as E305                                            # noqa: E402  (SEAL308 ids, OVERRIDES, REALM data)

ROOT = TOOLS.parents[1]
ENC = ROOT / 'Art/World/Compact/Rebuild/Enclosure305'
REP = ENC / 'Seal308'
OUT = ENC / 'Out'
FOOT = OUT / 'Seal308/footprint308.json'
SEAL_JSON = ENC / 'seal308.json'
A = ROOT / 'Oheangbu/Assets/_Project'
FACT = 'hwanggyeong_south_gate'
BAND = 3.0                 # barrier half width on the lattice (closure305 precedent)
REACH_R = 12.0             # item reached = a flooded node within this radius (places: GroundRadius clamped to 12..30)
UNDER = 6.0                # deeper than this under the height field = underground (mine)
ERODE = 8.0                # beyond polygon shrinks inward by this (Spec: 8 m TEST)
PASSES = [(1905, 1755), (1685, 1855), (2325, 4115), (1145, 4185), (1175, 2565)]   # Spec AC-S2 통로 5곳
LATE_REALMS = ('jeokro', 'cheolong', 'hyeongang')
ZONES = ['청림', '상경 가도', '황경 도성', '황경 외곽', '적로', '철옹', '현강', '고산']
t0 = time.time()


def log(*a):
    print(f'[{time.time() - t0:6.1f}s]', *a, flush=True)


# ------------------------------------------------------------------ lattice (emptiness305 up to its reachability step)
def lattice():
    src = (ENC / 'emptiness305.py').read_text(encoding='utf-8')
    cut = src.index('# ------------------------------------------------------------------ reachability')
    g = {'__file__': str(ENC / 'emptiness305.py'), '__name__': 'seal308_import'}
    argv = sys.argv; sys.argv = ['emptiness305.py']
    try:
        exec(compile(src[:cut], 'emptiness305.py', 'exec'), g)
    finally:
        sys.argv = argv
    return g


def in_poly(P, x, z):
    P = np.asarray(P, float); inside = np.zeros(np.shape(x), bool); n = len(P)
    for k in range(n):
        (x1, z1), (x2, z2) = P[k], P[(k + 1) % n]
        cond = ((z1 > z) != (z2 > z)) & (x < (x2 - x1) * (z - z1) / (z2 - z1 + 1e-12) + x1); inside ^= cond
    return inside


def seg_dist(p, a, b):
    d = b - a; L2 = float(d @ d) or 1e-12; t = np.clip(((p - a) @ d) / L2, 0, 1); return float(np.hypot(*(p - (a + d * t))))


def poly_dist(p, P):
    P = np.asarray(P, float); p = np.asarray(p, float)
    return min(seg_dist(p, a, b) for a, b in zip(P[:-1], P[1:])) if len(P) > 1 else float(np.hypot(*(p - P[0])))


# ------------------------------------------------------------------ inputs
def barrier_runs(draft):
    """(phase1, seal) lists of dict(id, points). Seal = segments flagged seal308 (or, --draft, raw lines305 + OVERRIDES)."""
    S = json.loads((ENC / 'segments305.json').read_text(encoding='utf-8'))
    p1 = [s for s in S['segments'] if not s.get('seal308')]
    seal = [s for s in S['segments'] if s.get('seal308')]
    if draft or not seal:
        L = {r['id']: r for r in json.loads((ENC / 'lines305.json').read_text(encoding='utf-8'))['runs']}
        inc = E305.OVERRIDES.get('_include', {})
        seal = [dict(id=i, points=L[i]['points'], realm=L[i]['realm']) for i in inc if i in L]
        seal += [dict(id=r['id'], points=r['points'], realm=r['realm']) for r in E305.OVERRIDES.get('_extra_runs', [])]
    return p1, seal, S.get('version', '?')


def palisade(draft):
    """Footprint of Seal308: wings / bastions / panel_closed / panel_open polylines (world x, z). Falls back to seal308.json's
    line (straight wings + panel) when the generator has not run yet."""
    if FOOT.exists() and not draft:
        F = json.loads(FOOT.read_text(encoding='utf-8')); F['source'] = str(FOOT.relative_to(ROOT)); return F
    S = json.loads(SEAL_JSON.read_text(encoding='utf-8'))
    a, b = np.asarray(S['anchors']['A'], float), np.asarray(S['anchors']['B'], float)
    c = np.asarray(S['road']['crossing'], float); t = (b - a) / np.hypot(*(b - a)); half = S['panel']['clear_width_m'] / 2
    pa, pb = c - t * half, c + t * half
    return dict(source='seal308.json (draft line)', wings=[[a.tolist(), pa.tolist()], [pb.tolist(), b.tolist()]], bastions=[],
                panel_closed=[pa.tolist(), pb.tolist()], panel_open=[])


def doors(CAP):
    out = []
    for a in CAP['apertures']:
        if a.get('fact') != FACT: continue
        c = np.array([a['centre']['x'], a['centre']['z']]); inward = np.array([a['inward']['x'], a['inward']['z']])
        along = np.array([inward[1], -inward[0]]); w = a['width'] / 2 + 4.0
        out.append(dict(id=a['id'], centre=c.tolist(), line=[(c - along * w).tolist(), (c + along * w).tolist()], width=a['width']))
    return out


# ------------------------------------------------------------------ content inventory
def inventory(g, zone10, realm10, realm_ids):
    L, C, ARCH, CHE, HWA, hs = g['L'], g['C'], g['ARCH'], g['CHE'], g['HWA'], g['sample_h']
    cap = np.asarray(g['CAP']['loop']['corners'], float)
    items = []

    def add(kind, iid, label, x, z, y=None, realm=None, r=REACH_R, source=''):
        if x is None or z is None or not (0 <= x <= 4000 and 0 <= z <= 6000): return
        items.append(dict(kind=kind, id=str(iid), label=str(label or iid), x=round(float(x), 1), z=round(float(z), 1),
                          y=None if y is None else round(float(y), 1), realm=(realm or '').lower(), r=float(r), source=source))
    for p in L['Places']:
        pid = p['Id']; kind = 'arena' if pid.startswith('arena_') else 'trail_entry' if pid.startswith('mountain_') and pid.endswith('_foot') else 'place'
        add(kind, pid, p.get('Label'), p['XZ']['x'], p['XZ']['y'], None, p.get('Realm'), float(np.clip(p.get('GroundRadius') or 0, REACH_R, 30)), 'layout.Places')
    for m in L['Mountains']:
        if m.get('MainPath'): add('trail_entry', m['Id'] + '.MainPath[0]', m['Id'] + ' 산길 입구', m['MainPath'][0]['x'], m['MainPath'][0]['z'], m['MainPath'][0].get('y'), m['Id'].split('_')[-1], REACH_R, 'layout.Mountains')
    for c in C['Checkpoints']: add('rest', c['Id'], c.get('Label'), c['Feet']['x'], c['Feet']['z'], c['Feet']['y'], None, REACH_R, 'content.Checkpoints')
    for p in C['Points']:
        kind = {2: 'rest_point', 3: 'npc', 1: 'bar', 4: 'preview'}.get(p['Kind'], 'interaction')
        add(kind, p['Id'], p.get('Prompt'), p['Position']['x'], p['Position']['z'], p['Position']['y'], None, REACH_R, 'content.Points')
    for e in C['Encounters']: add('encounter', e['Id'], e.get('ContentId') or e['Id'], e['Feet']['x'], e['Feet']['z'], e['Feet']['y'], None, 15.0, 'content.Encounters')
    for a in ARCH['Arenas']: add('arena', a['Id'], a['Label'], a['Centre']['x'], a['Centre']['z'], a['Centre']['y'], None, 20.0, 'architecture.Arenas')
    for comp, D in (('cheolong', CHE), ('hwanggyeong', HWA)):
        mk = D.get('markers', {})
        for i, s in enumerate(mk.get('enemySlots', [])): add('encounter_slot', f'{comp}.enemySlot{i}', f'{comp} 적 슬롯', s[0], s[2], s[1], comp, 15.0, f'Finish297/{comp}')
        if 'rest' in mk: add('rest_marker', f'{comp}.rest', mk['rest'].get('note', 'rest')[:24], mk['rest']['position'][0], mk['rest']['position'][2], mk['rest']['position'][1], comp, REACH_R, f'Finish297/{comp}')
    for it in items:
        x, z = it['x'], it['z']; i, j = int(np.clip(z // 10, 0, 599)), int(np.clip(x // 10, 0, 399))
        it['zone'] = ZONES[zone10[i, j]] if zone10[i, j] >= 0 else '-'
        it['realm_poly'] = realm_ids[realm10[i, j]] if realm10[i, j] >= 0 else '-'
        it['in_capital'] = bool(in_poly(cap, np.array(x), np.array(z)))
        it['underground'] = it['y'] is not None and it['y'] < float(hs(x, z)) - UNDER
        if it['in_capital']: ea = False
        elif it['source'] == 'layout.Places' or it['source'] == 'layout.Mountains':
            ea = it['realm'] == 'cheongrim' or (it['realm'] == 'hwanggyeong' and zone10[i, j] in (0, 1))
        elif it['realm'] in LATE_REALMS: ea = False
        else:   # 청림 high ground (zone 고산 inside the Cheongrim polygon: 신목, 금표 암릉) is EA; other 고산 is late
            ea = int(zone10[i, j]) in (0, 1) or (int(zone10[i, j]) == 7 and it['realm_poly'] == 'Cheongrim')
        it['ea'] = bool(ea)
    return items


# ------------------------------------------------------------------ flood
def flood(walk, seeds, W4):
    reach = np.zeros(walk.size, bool); parent = np.full(walk.size, -1, np.int32); fw = walk.ravel()
    seeds = [s for s in seeds if fw[s]]; reach[seeds] = True; dq = deque(seeds)
    while dq:
        k = dq.popleft(); j = k % W4
        for n in (k - 1 if j > 0 else -1, k + 1 if j < W4 - 1 else -1, k - W4, k + W4):
            if 0 <= n < fw.size and fw[n] and not reach[n]:
                reach[n] = True; parent[n] = k; dq.append(n)
    return reach.reshape(walk.shape), parent


def near_nodes(mask, x, z, r, C4, solid=None):
    """A flooded node within r of (x, z) - and, with `solid`, one the straight line from (x, z) reaches without crossing a
    solid band (an item 12 m inside a closed door is not reached by the nodes outside it)."""
    H4, W4 = mask.shape; i0, j0 = int(round(z / C4)), int(round(x / C4)); k = int(math.ceil(r / C4))
    sub = mask[max(0, i0 - k): i0 + k + 1, max(0, j0 - k): j0 + k + 1]
    if not sub.any(): return False
    ii, jj = np.nonzero(sub); ii = ii + max(0, i0 - k); jj = jj + max(0, j0 - k)
    d = np.hypot(jj * C4 - x, ii * C4 - z); order = np.argsort(d)
    for q in order:
        if d[q] > r: break
        if solid is None: return True
        n = max(2, int(d[q] / 1.0) + 1); t = np.linspace(0, 1, n)
        si = np.clip(np.round((z + (ii[q] * C4 - z) * t) / C4).astype(int), 0, H4 - 1); sj = np.clip(np.round((x + (jj[q] * C4 - x) * t) / C4).astype(int), 0, W4 - 1)
        if not solid[si, sj].any(): return True
    return False


def run(mode, seed_kind='newgame', draft=False, G=None, omit=None, write=True):
    g = G or lattice()
    W4, H4, C4, X4, Z4 = g['W4'], g['H4'], g['C4'], g['X4'], g['Z4']
    walk4, route4, stamp4 = g['walk4'], g['route4'], g['stamp4']
    B = np.load(ENC / '_work/optB.npz'); zone10 = B['zone']; realm10 = B['realm']; realm_ids = [r['Id'] for r in g['L']['Realms']]
    p1, seal, version = barrier_runs(draft)
    pal = palisade(draft); drs = doors(g['CAP'])
    if omit and omit not in ('panel', 'doors'): seal = [r for r in seal if r['id'] != omit]      # sensitivity runs (--omit)
    if mode == 'baseline':   # no seal: phase-1 runs only, no palisade, doors open (= the world before #308) - attributes AC-S3 misses
        seal = []; pal = dict(source='none (baseline: no seal runs, no palisade, doors open)', wings=[], bastions=[], panel_closed=None, panel_open=[])
    log(mode, 'segments', version, 'phase1', len(p1), 'seal', len(seal), 'palisade', pal['source'], 'doors', [d['id'] for d in drs])

    def band(P, half=BAND):
        m = np.zeros((H4, W4), bool); stamp4(m, np.asarray(P, float), half); return m
    # route ids for cut reports
    rpts, rids = [], []
    for k, r in enumerate(g['routes']):
        D = g['densify'](r['pts'], 4.0); rpts.append(D); rids.extend([k] * len(D))
    rpts = np.concatenate(rpts); rids = np.asarray(rids)

    def route_at(x, z, r=8.0):
        d = np.hypot(rpts[:, 0] - x, rpts[:, 1] - z); ok = np.nonzero(d <= r)[0]
        return sorted({g['routes'][rids[i]]['id'] for i in ok})
    cuts = []
    bar_p1 = np.zeros((H4, W4), bool); bar_seal = np.zeros((H4, W4), bool)
    for group, runs, acc in (('phase1', p1, bar_p1), ('seal308', seal, bar_seal)):
        for r in runs:
            m = band(r['points']); hit = m & route4
            if hit.any():
                ii, jj = np.nonzero(hit); x, z = float(jj.mean() * C4), float(ii.mean() * C4)
                cuts.append(dict(group=group, id=r['id'], cells=int(hit.sum()), x=round(x), z=round(z), routes=route_at(x, z)))
            acc |= m
    # walls: solid except apertures (closed doors re-solidified below)
    wall_hard = np.zeros((H4, W4), bool); wall_route = []
    for wid, P, aps in g['walls']:
        m = band(P)
        for ax, az, hw in aps: m &= ~((np.abs(X4 - ax) < hw + 2) & (np.abs(Z4 - az) < hw + 2))
        hit = m & route4
        if hit.any():
            ii, jj = np.nonzero(hit); used = np.zeros(len(ii), bool)
            for k in range(len(ii)):
                if used[k]: continue
                d = np.hypot(ii - ii[k], jj - jj[k]) <= 6; used |= d; x, z = float(jj[d].mean() * C4), float(ii[d].mean() * C4)
                wall_route.append(dict(wall=wid, cells=int(d.sum()), x=round(x), z=round(z), routes=route_at(x, z)))
        wall_hard |= m
    door_hard = np.zeros((H4, W4), bool)
    if mode == 'closed' and omit != 'doors':
        for d in drs: door_hard |= band(d['line'])
    pal_soft = np.zeros((H4, W4), bool); pal_hard = np.zeros((H4, W4), bool)
    for P in pal['wings']: pal_soft |= band(P)
    for bx, bz, br in pal.get('bastions', []): pal_soft |= (np.hypot(X4 - bx, Z4 - bz) <= max(br, BAND))
    hit = pal_soft & route4
    if hit.any():
        ii, jj = np.nonzero(hit); x, z = float(jj.mean() * C4), float(ii.mean() * C4)
        cuts.append(dict(group='palisade_wings', id='Seal308', cells=int(hit.sum()), x=round(x), z=round(z), routes=route_at(x, z)))
    if mode == 'closed' and omit != 'panel': pal_hard |= band(pal['panel_closed'])
    walkB = walk4 & ~((bar_p1 | bar_seal | pal_soft) & ~route4) & ~wall_hard & ~door_hard & ~pal_hard

    # seeds
    hs = g['sample_h']; MP = g['C']['MainPath']; seed_note = ''
    if seed_kind == 'newgame':
        k0 = next(k for k, p in enumerate(MP) if abs(p['y'] - float(hs(p['x'], p['z']))) <= 1.5)
        sx, sz = MP[k0]['x'], MP[k0]['z']
        ii, jj = np.nonzero(walkB[int(sz / C4) - 4: int(sz / C4) + 5, int(sx / C4) - 4: int(sx / C4) + 5])
        ii += int(sz / C4) - 4; jj += int(sx / C4) - 4; d = np.hypot(jj * C4 - sx, ii * C4 - sz); q = int(np.argmin(d))
        seeds = [int(ii[q] * W4 + jj[q])]
        seed_note = f'new game: MainPath[{k0}] ({sx:.1f}, {sz:.1f}) = first node within 1.5 m of the height field after the mine (start {MP[0]["x"]:.0f},{MP[0]["z"]:.0f} is underground); lattice node ({jj[q] * C4:.0f}, {ii[q] * C4:.0f}) {d[q]:.1f} m away'
        seed_xz = (float(jj[q] * C4), float(ii[q] * C4))
    else:
        seeds = np.flatnonzero((route4 & walkB).ravel()).tolist(); seed_note = 'every route node'; seed_xz = None
    reach, parent = flood(walkB, seeds, W4)
    log(mode, 'flood', f'{reach.sum() * 16 / 1e4:.0f} ha')

    # areas
    ci = np.minimum((np.arange(H4) * C4 // 10).astype(int), 599); cj = np.minimum((np.arange(W4) * C4 // 10).astype(int), 399)
    z4 = zone10[ci[:, None], cj[None, :]]; r4 = realm10[ci[:, None], cj[None, :]]
    cap4 = in_poly(np.asarray(g['CAP']['loop']['corners'], float), X4, Z4)
    by_zone = {ZONES[k]: round(float((reach & (z4 == k)).sum()) * 16 / 1e4, 1) for k in range(len(ZONES))}
    by_realm = {realm_ids[k]: round(float((reach & (r4 == k) & ~cap4).sum()) * 16 / 1e4, 1) for k in range(len(realm_ids))}
    by_realm['Hwanggyeong(capital)'] = round(float((reach & cap4).sum()) * 16 / 1e4, 1)

    # content
    items = inventory(g, zone10, realm10, realm_ids)
    solid = ((bar_p1 | bar_seal | pal_soft) & ~route4) | wall_hard | door_hard | pal_hard
    for it in items: it['reached'] = near_nodes(reach, it['x'], it['z'], it['r'], C4, solid)
    (REP / '_work').mkdir(parents=True, exist_ok=True)
    if not omit: np.save(REP / f'_work/reach-{mode}.npy', reach); np.save(REP / f'_work/solid-{mode}.npy', solid)
    late_reached = [it for it in items if not it['ea'] and it['reached'] and not it['underground']]
    ea_items = [it for it in items if it['ea'] and not it['underground']]
    ea_missed = [it for it in ea_items if not it['reached']]
    ea_places = [it for it in ea_items if it['kind'] in ('place', 'trail_entry', 'arena')]
    late_places = [it for it in items if not it['ea'] and it['kind'] in ('place', 'trail_entry', 'arena', 'rest', 'rest_point', 'rest_marker', 'encounter', 'encounter_slot')]
    # path of each reached late item back to the seed: the node on it nearest to a seal / palisade / door band (= where it crosses)
    sealband = bar_seal | pal_soft | pal_hard | door_hard

    def crossing(it):
        i0, j0 = int(round(it['z'] / C4)), int(round(it['x'] / C4)); best = None
        for di in range(-3, 4):
            for dj in range(-3, 4):
                if 0 <= i0 + di < H4 and 0 <= j0 + dj < W4 and reach[i0 + di, j0 + dj]: best = (i0 + di) * W4 + j0 + dj; break
            if best is not None: break
        if best is None: return None
        k = best; on, near = [], []
        while k >= 0:
            i, j = divmod(k, W4)
            if sealband[i, j]: on.append((round(j * C4), round(i * C4)))                 # walkable inside a band = route cut
            elif sealband[max(0, i - 2): i + 3, max(0, j - 2): j + 3].any(): near.append((round(j * C4), round(i * C4)))
            k = int(parent[k])
        if on: return dict(through_band=[on[0], on[-1]])
        return dict(gap_near=[near[0], near[-1]]) if near else 'path never comes within 8 m of a seal band'
    for it in late_reached: it['crossing'] = crossing(it)
    passes = []
    meta = {(p['x'], p['z']): p for p in json.loads((ENC / '_work/optB_meta.json').read_text(encoding='utf-8'))['passes']}
    pal_lines = [np.asarray(P, float) for P in pal['wings'] + ([pal['panel_closed']] if pal['panel_closed'] else [])]
    for x, z in PASSES:
        m = meta.get((x, z), {}); i, j = int(z // 10), int(x // 10)
        at_pal = bool(pal_lines) and min(poly_dist((x, z), P) for P in pal_lines) <= 30.0
        # side: the palisade pass is the seal itself - report each side of it; other passes: reached = on the EA side
        sides = None
        if at_pal:
            P = np.asarray(pal['panel_closed'], float); t = (P[-1] - P[0]) / np.hypot(*(P[-1] - P[0])); n = np.array([-t[1], t[0]])
            c = (P[0] + P[-1]) / 2; sides = dict(north_8m=near_nodes(reach, *(c + n * 8), 6.0, C4, solid), south_8m=near_nodes(reach, *(c - n * 8), 6.0, C4, solid))
        passes.append(dict(x=x, z=z, kind=m.get('kind', ''), realms=m.get('realms', []), route=m.get('route', ''), zone=ZONES[zone10[i, j]],
                           at_palisade=at_pal, sides=sides, reached=near_nodes(reach, x, z, REACH_R, C4, solid)))
    seal_cuts = [c for c in cuts if c['group'] != 'phase1']
    # offline pre-check for check-scene / seal-check distances (AC-S5 국 지점 20 m, AC-10 예약지): items within 30 m of a seal run or the palisade
    near_seal = []
    lines_ = [(r['id'], np.asarray(r['points'], float)) for r in seal] + [('Seal308', np.asarray(P, float)) for P in pal['wings'] + ([pal['panel_closed']] if pal['panel_closed'] else [])]
    for it in items:
        if not lines_: break
        dmin, lid = min((poly_dist((it['x'], it['z']), P), i) for i, P in lines_)
        if dmin < 30.0: near_seal.append(dict(kind=it['kind'], id=it['id'], x=it['x'], z=it['z'], line=lid, d=round(dmin, 1), ea=it['ea']))
    if mode == 'closed':
        verdict = dict(late_content_reached=len(late_reached), ea_items_missed=len(ea_missed), ea_places=f"{sum(i['reached'] for i in ea_places)}/{len(ea_places)}",
                       seal_route_cuts=len(seal_cuts))
        verdict['AC-S2'] = 'PASS' if not late_reached and not [i for i in ea_places if not i['reached']] else 'FAIL'
    else:
        miss = [it for it in late_places + ea_items if not it['reached'] and not it['underground']]
        req = [it for it in items if it['kind'] in ('place', 'trail_entry', 'arena', 'rest', 'rest_point', 'rest_marker') and not it['underground']]
        verdict = dict(places_rests_trails=f"{sum(i['reached'] for i in req)}/{len(req)}", missed=len(miss), seal_route_cuts=len(seal_cuts))
        verdict['AC-S3'] = 'PASS' if all(i['reached'] for i in req) else 'see baseline'   # finalised against the baseline in `all`
    verdict['AC-S4'] = 'PASS' if not seal_cuts else 'FAIL'
    doc = dict(version='308.1', mode=mode, generated=time.strftime('%Y-%m-%dT%H:%M:%S'), segments=version, palisade=pal['source'],
               seed=seed_note, seed_xz=seed_xz, lattice='4 m (emptiness305 walk4), barrier half width 3.0 m', reach_ha=round(float(reach.sum()) * 16 / 1e4, 1),
               reach_by_zone_ha=by_zone, reach_by_realm_ha=by_realm, verdict=verdict, passes=passes,
               route_cuts=cuts, wall_route_crossings_outside_apertures=wall_route, content_within_30m_of_seal=near_seal, doors=[dict(id=d['id'], closed=mode == 'closed') for d in drs],
               late_reached=late_reached, ea_missed=ea_missed,
               items=[{k: v for k, v in it.items() if k != 'crossing'} for it in items])
    REP.mkdir(parents=True, exist_ok=True)
    tag = mode + (f'-omit-{omit}' if omit else ''); doc['omit'] = omit
    if write: (REP / f'seal308-{tag}.json').write_text(json.dumps(doc, ensure_ascii=False, indent=1), encoding='utf-8')
    if not omit: picture(mode, g, reach, walkB, bar_p1, bar_seal, pal_soft | pal_hard, wall_hard | door_hard, items, seed_xz)
    log(mode, json.dumps(verdict, ensure_ascii=False))
    return doc, reach, g


def picture(mode, g, reach, walk, bar_p1, bar_seal, pal, wall, items, seed_xz):
    from PIL import Image, ImageDraw
    H4, W4, C4 = g['H4'], g['W4'], g['C4']
    img = np.zeros((H4, W4, 3), np.uint8); img[:] = (236, 232, 222)
    img[walk & ~reach] = (196, 190, 178)
    img[reach] = (150, 196, 150) if mode == 'closed' else (150, 176, 214)
    img[wall] = (90, 90, 90); img[bar_p1] = (30, 30, 30); img[bar_seal] = (190, 30, 30); img[pal] = (230, 120, 0)
    im = Image.fromarray(img[::-1]); d = ImageDraw.Draw(im)

    def P(x, z): return (x / C4, H4 - 1 - z / C4)
    for it in items:
        if it['underground']: continue
        col = (0, 120, 0) if it['ea'] and it['reached'] else (255, 0, 255) if it['ea'] else (220, 0, 0) if it['reached'] else (0, 60, 200)
        x, y = P(it['x'], it['z']); r = 4 if it['kind'] in ('place', 'trail_entry', 'arena') else 2
        d.ellipse([x - r, y - r, x + r, y + r], outline=col, width=2)
    for x, z in PASSES:
        X, Y = P(x, z); d.rectangle([X - 6, Y - 6, X + 6, Y + 6], outline=(0, 0, 0), width=2)
    if seed_xz:
        X, Y = P(*seed_xz); d.polygon([(X, Y - 9), (X - 7, Y + 6), (X + 7, Y + 6)], outline=(0, 0, 0), fill=(255, 220, 0))
    im.save(REP / f'seal308-{mode}.png')


# ------------------------------------------------------------------ beyond polygons (4 m mask -> rings)
def rings_contain(rings, X, Z):
    """Even-odd test of many points against each ring; True where any ring contains the point."""
    out = np.zeros(len(X), bool)
    for R in rings:
        R = np.asarray(R, float); x1, z1 = R[:, 0][:, None], R[:, 1][:, None]; x2, z2 = np.roll(R[:, 0], -1)[:, None], np.roll(R[:, 1], -1)[:, None]
        par = np.zeros(len(X), bool)
        for k0 in range(0, len(R), 512):   # chunk the edges (memory)
            a, b, c, d = x1[k0:k0 + 512], z1[k0:k0 + 512], x2[k0:k0 + 512], z2[k0:k0 + 512]
            cond = ((b > Z[None]) != (d > Z[None])) & (X[None] < (c - a) * (Z[None] - b) / (d - b + 1e-12) + a)
            par ^= (cond.sum(0) % 2).astype(bool)
        out |= par
    return out


def erode(mask, r_nodes):
    out = mask.copy(); H, W = mask.shape
    for di in range(-r_nodes, r_nodes + 1):
        for dj in range(-r_nodes, r_nodes + 1):
            if di * di + dj * dj > r_nodes * r_nodes: continue
            sh = np.zeros_like(mask)
            sh[max(0, di): H + min(0, di), max(0, dj): W + min(0, dj)] = mask[max(0, -di): H + min(0, -di), max(0, -dj): W + min(0, -dj)]
            out &= sh
    return out


def trace_rings(mask, C4):
    """Boundary loops of the node pixels (node (i, j) = pixel [x-2, x+2] x [z-2, z+2]); every loop keeps the mask on its left,
    so outer loops are counter-clockwise (x right, z up, positive area) and holes clockwise (negative area). 4-connected: at a
    saddle corner the walk turns left, keeping diagonal pixels apart. Corner (a, b) of the padded grid = lower-left corner of
    padded pixel (a, b) = world (x, z) = ((b - 1) * C4 - C4 / 2, (a - 1) * C4 - C4 / 2)."""
    m = np.pad(mask, 1); edges = {}

    def add(a, b):
        edges.setdefault(a, []).append(b)
    inner = m[1:-1, 1:-1]
    for (di, dj), (fa, fb) in (((-1, 0), ((0, 0), (0, 1))), ((1, 0), ((1, 1), (1, 0))), ((0, -1), ((1, 0), (0, 0))), ((0, 1), ((0, 1), (1, 1)))):
        # bottom (+x), top (-x), left (-z), right (+z) edges: mask pixel whose neighbour (di, dj) is empty; mask on the left
        nb = m[1 + di: m.shape[0] - 1 + di, 1 + dj: m.shape[1] - 1 + dj]
        for i, j in zip(*np.nonzero(inner & ~nb)):
            i += 1; j += 1; add((i + fa[0], j + fa[1]), (i + fb[0], j + fb[1]))
    rings = []
    while edges:
        start = next(iter(edges)); cur = start; ring = [start]; prev = None
        while True:
            outs = edges[cur]
            if len(outs) == 1 or prev is None: nxt = outs[0]
            else:   # (x, z) = (db, da); left turn = largest cross(prev, d)
                nxt = max(outs, key=lambda o: prev[0] * (o[0] - cur[0]) - prev[1] * (o[1] - cur[1]))
            outs.remove(nxt)
            if not outs: del edges[cur]
            prev = (nxt[1] - cur[1], nxt[0] - cur[0]); cur = nxt
            if cur == start: break
            ring.append(cur)
        pts = [((b - 1) * C4 - C4 / 2, (a - 1) * C4 - C4 / 2) for a, b in ring]
        n = len(pts)
        keep = [p for k, p in enumerate(pts) if not ((pts[k - 1][0] == p[0] == pts[(k + 1) % n][0]) or (pts[k - 1][1] == p[1] == pts[(k + 1) % n][1]))]
        rings.append(keep)
    return rings


def area(R):
    R = np.asarray(R, float); x, z = R[:, 0], R[:, 1]; return 0.5 * float(np.dot(x, np.roll(z, -1)) - np.dot(np.roll(x, -1), z))


def probe_pt(R, C4, inside_mask=True):
    """A pixel centre next to the ring's first edge: on the mask side (left) or the other side (right)."""
    (x0, z0), (x1, z1) = R[0], R[1]; L = math.hypot(x1 - x0, z1 - z0) or 1.0; dx, dz = (x1 - x0) / L, (z1 - z0) / L
    nx, nz = (-dz, dx) if inside_mask else (dz, -dx)
    return np.array((x0 + x1) / 2 + nx * C4 / 2), np.array((z0 + z1) / 2 + nz * C4 / 2)


def simplify(R, tol):
    """Douglas-Peucker on a closed ring (split at the two farthest-apart corners)."""
    P = np.asarray(R, float)
    if len(P) < 8: return P.tolist()

    def dp(Q):
        if len(Q) < 3: return Q.tolist()
        a, b = Q[0], Q[-1]; d = b - a; L = np.hypot(*d) or 1e-9
        dist = np.abs(d[0] * (Q[:, 1] - a[1]) - d[1] * (Q[:, 0] - a[0])) / L; k = int(np.argmax(dist))
        if dist[k] <= tol: return [a.tolist(), b.tolist()]
        return dp(Q[:k + 1])[:-1] + dp(Q[k:])
    k = int(np.argmax(np.hypot(P[:, 0] - P[0, 0], P[:, 1] - P[0, 1])))
    out = dp(P[:k + 1])[:-1] + dp(np.vstack([P[k:], P[:1]]))[:-1]
    return out


def keyhole(outer, holes):
    """Merge holes into the outer ring with zero-width bridges (rightmost hole vertex -> nearest outer vertex to its right that
    the bridge reaches without crossing). Even-odd point-in-polygon on the result equals outer minus holes."""
    O = [tuple(p) for p in outer]
    for H in sorted(holes, key=lambda h: -max(p[0] for p in h)):
        Hh = [tuple(p) for p in H]; k = max(range(len(Hh)), key=lambda i: Hh[i][0]); hp = Hh[k]
        best, bi = None, None
        for i, q in enumerate(O):
            if q[0] < hp[0]: continue
            d = (q[0] - hp[0]) ** 2 + (q[1] - hp[1]) ** 2
            if best is None or d < best: best, bi = d, i
        if bi is None: bi = min(range(len(O)), key=lambda i: (O[i][0] - hp[0]) ** 2 + (O[i][1] - hp[1]) ** 2)
        loop = Hh[k:] + Hh[:k] + [hp]
        O = O[:bi + 1] + loop + O[bi:]
    return [list(p) for p in O]


def beyond(reach_closed, reach_open, g, items, closed_doc, open_doc):
    open_items = {(i['kind'], i['id']): i['reached'] for i in open_doc['items']}
    C4 = g['C4']; hs = g['sample_h']
    raw = reach_open & ~reach_closed
    keep = erode(raw, int(round(ERODE / C4)))
    rings = trace_rings(keep, C4)
    # specks: outer rings < 0.04 ha are dropped below (with any hole inside them); holes < 0.04 ha are filled
    outers = [r for r in rings if area(r) >= 400.0]; holes = [r for r in rings if area(r) <= -400.0]
    specks = [r for r in rings if 0 < area(r) < 400.0]
    polys = []
    hole_pt = [probe_pt(h, C4, inside_mask=False) for h in holes]          # a non-mask pixel centre inside each hole
    for o in outers:
        Op = np.asarray(o, float)
        # holes are assigned to every outer containing them, then dropped from outers that contain a nested island which
        # itself contains them (outer > hole > island > hole ...)
        polys.append(dict(outer=o, Op=Op, pt=probe_pt(o, C4, True), holes=[k for k in range(len(holes)) if bool(in_poly(Op, *hole_pt[k]))]))
    for P in polys:
        inner = [Q for Q in polys if Q is not P and bool(in_poly(P['Op'], *Q['pt']))]
        P['holes'] = [holes[k] for k in P['holes'] if not any(bool(in_poly(Q['Op'], *hole_pt[k])) for Q in inner)]
    out_rings = []
    for P in polys:
        o = simplify(P['outer'], 2.0); hl = [simplify(h, 2.0) for h in P['holes'] if abs(area(h)) >= 400.0]
        out_rings.append(keyhole(o, hl))
    # EA underground volumes: underground EA content (the mine) + the underground MainPath, padded
    # clusters (60 m single linkage) of underground MainPath nodes + underground EA content; a cluster becomes a box only when it
    # holds content (the mine). MainPath-only clusters are stale path heights under the re-shaped terrain, not tunnels (listed).
    MP = g['C']['MainPath']
    pts = [((p['x'], p['y'], p['z']), f'MainPath[{k}]') for k, p in enumerate(MP) if p['y'] < float(hs(p['x'], p['z'])) - UNDER]
    pts += [((it['x'], it['y'], it['z']), f"{it['kind']} {it['id']}") for it in items if it['underground'] and it['ea']]
    boxes, skipped = [], []
    if pts:
        U = np.asarray([p for p, _ in pts], float); lab = -np.ones(len(U), int); nlab = 0
        for s in range(len(U)):
            if lab[s] >= 0: continue
            lab[s] = nlab; st = [s]
            while st:
                q = st.pop(); d = np.hypot(U[:, 0] - U[q, 0], U[:, 2] - U[q, 2]); nb = np.nonzero((d <= 60.0) & (lab < 0))[0]; lab[nb] = nlab; st.extend(nb.tolist())
            nlab += 1
        pad = np.array([30.0, 12.0, 30.0])
        for c in range(nlab):
            idx = np.nonzero(lab == c)[0]; names = [pts[i][1] for i in idx]; content = [x for x in names if not x.startswith('MainPath')]
            lo, hi = U[idx].min(0) - pad, U[idx].max(0) + pad
            if not content:
                skipped.append(dict(nodes=len(idx), first=names[0], last=names[-1], bbox=[lo.round(1).tolist(), hi.round(1).tolist()])); continue
            boxes.append(dict(id='mine' if any('mine' in x for x in content) else f'ea_underground_{c}', centre=((lo + hi) / 2).round(2).tolist(), size=(hi - lo).round(2).tolist(),
                              content=content, note='AABB of the cluster (underground MainPath + EA content), padded 30 m / 12 m; TEST - seal-scene may refine it from the root bounds'))
    ea_rests = sorted({it['id'] for it in closed_doc['items'] if it['kind'] == 'rest' and it['ea'] and (it['reached'] or it['underground'])})
    src = hashlib.sha256()
    for p in (ENC / 'segments305.json', FOOT, A / 'Art/World/Finish297/Surface/height.bytes', A / 'Scenes/World/Main/WorldLayout_Main.asset', A / 'Scenes/World/Main/WorldContent_Main.asset'):
        if p.exists(): src.update(p.read_bytes())
    nverts = sum(len(r) for r in out_rings)
    # item check: EA items (surface) are never inside a ring; late items the open flood reaches are
    Rr = [np.asarray(r, float) for r in out_rings]
    surf = [it for it in closed_doc['items'] if not it['underground']]
    inside_any = rings_contain(Rr, np.array([it['x'] for it in surf]), np.array([it['z'] for it in surf]))
    item_check = dict(ea_inside=[], late_reached_outside=[])
    for it, b in zip(surf, inside_any):
        if it['ea'] and b: item_check['ea_inside'].append(f"{it['kind']} {it['id']}")
        if not it['ea'] and not b and open_items.get((it['kind'], it['id'])): item_check['late_reached_outside'].append(f"{it['kind']} {it['id']}")
    doc = dict(version='308.1', generated=time.strftime('%Y-%m-%dT%H:%M:%S'), required_fact=FACT,
               rule='beyond = reach(open) - reach(closed) on the 4 m lattice, eroded %.0f m, specks < 0.04 ha dropped; rings in world (x, z); '
                    'a point is beyond when it lies inside a ring by the even-odd rule (holes are merged into their outer ring with zero-width bridges, '
                    'so testing each ring on its own and taking any() is exact)' % ERODE,
               beyond_rings=[[[round(x, 1), round(z, 1)] for x, z in r] for r in out_rings],
               ring_vertices=nverts, beyond_ha=round(float(keep.sum()) * C4 * C4 / 1e4 - sum(area(r) for r in specks) / 1e4, 1), raw_beyond_ha=round(float(raw.sum()) * C4 * C4 / 1e4, 1),
               dropped_specks=len(specks), item_check=item_check,
               ea_underground_volumes=boxes, underground_clusters_without_content=skipped, underground_depth_m=4.0,
               ea_rest_ids=ea_rests, fallback_rest_ids=['geumpyo_inn', 'mine_start'],
               move_drops=True, move_vehicle=True, source_hash=src.hexdigest()[:16],
               inputs=['segments305.json', 'Out/Seal308/footprint308.json', 'Finish297/Surface/height.bytes', 'WorldLayout_Main.asset', 'WorldContent_Main.asset'])
    OUT.mkdir(parents=True, exist_ok=True)
    (OUT / 'beyond308.json').write_text(json.dumps(doc, ensure_ascii=False), encoding='utf-8')
    # self-test: every ring test agrees with the eroded mask on a sample of nodes
    rng = np.random.default_rng(308); H4, W4 = keep.shape
    ii = rng.integers(1, H4 - 1, 20000); jj = rng.integers(1, W4 - 1, 20000); ok = reach_open[ii, jj]; ii, jj = ii[ok], jj[ok]
    # simplification moves an edge <= 2 m: compare only nodes whose 3x3 window is all in or all out of the mask (and not in a speck)
    win = np.stack([keep[ii + a, jj + b] for a in (-1, 0, 1) for b in (-1, 0, 1)], 0); clean = win.all(0) | ~win.any(0)
    X = jj * C4 + .37; Z = ii * C4 + .41
    inside = rings_contain(Rr, X, Z); in_speck = rings_contain([np.asarray(sp, float) for sp in specks], X, Z) if specks else np.zeros(len(X), bool)
    sel = clean & ~in_speck; bad = int((inside[sel] != keep[ii, jj][sel]).sum()); n_s = int(sel.sum())
    log('beyond', doc['beyond_ha'], 'ha', len(out_rings), 'rings', nverts, 'vertices; ring test vs mask disagreements', bad, '/', n_s)
    return doc, bad, n_s


def ledger(closed, opened, bey, base=None, sens=None):
    L = []
    L.append(f"#308 seal closure proof (SPEC-WORLD-ENCLOSURE-305 §1b′) | {time.strftime('%Y-%m-%dT%H:%M:%S')} | segments305 {closed['segments']} | palisade {closed['palisade']}")
    L.append('Offline 4 m lattice (emptiness305 walk4). Automated, not in-engine; editor probes (check-scene / seal-check) and real-input play are separate.')
    L.append('seed: ' + closed['seed'])
    for name, d in (('closed', closed), ('open', opened)):
        L.append(f"[{name}] reach {d['reach_ha']} ha; verdict {json.dumps(d['verdict'], ensure_ascii=False)}")
        L.append('  by zone ha: ' + json.dumps(d['reach_by_zone_ha'], ensure_ascii=False))
        L.append('  by realm ha (capital apart): ' + json.dumps(d['reach_by_realm_ha'], ensure_ascii=False))
        for p in d['passes']:
            side = (f"palisade pass: north side {'reached' if p['sides']['north_8m'] else 'not reached'}, south side {'reached' if p['sides']['south_8m'] else 'not reached'}"
                    if p['at_palisade'] else (('reached' if p['reached'] else 'not reached') + ((' - EA side of the seal' if p['reached'] else ' - beyond the seal') if name == 'closed' else '')))
            L.append(f"  pass ({p['x']},{p['z']}) {p['kind']} {'/'.join(p['realms'])} route {p['route']} zone {p['zone']}: {side}")
        for c in d['route_cuts']: L.append(f"  route cut {c['group']} {c['id']} cells {c['cells']} at ({c['x']},{c['z']}) routes {c['routes']}")
        for w in d['wall_route_crossings_outside_apertures']: L.append(f"  wall/route crossing outside an aperture (wall kept solid) {w['wall']} at ({w['x']},{w['z']}) routes {w['routes']}")
    L.append('content within 30 m of a seal run / the palisade (editor check-scene / seal-check measure the real distances; FLAG < 20 m): ' +
             (', '.join(f"{'FLAG ' if c['d'] < 20 else ''}{c['kind']} {c['id']} -> {c['line']} {c['d']} m" for c in closed['content_within_30m_of_seal']) or 'none'))
    L.append('[closed] late content reached (must be 0):')
    for it in closed['late_reached']: L.append(f"  REACHED {it['kind']} {it['id']} '{it['label']}' ({it['x']},{it['z']}) zone {it['zone']} realm {it['realm'] or it['realm_poly']} crossing {it.get('crossing')}")
    L.append('[closed] EA items not reached (places must be n/n):')
    for it in closed['ea_missed']: L.append(f"  MISSED {it['kind']} {it['id']} '{it['label']}' ({it['x']},{it['z']}) zone {it['zone']}")
    if base:
        L.append(f"[baseline: no seal, doors open] reach {base['reach_ha']} ha; open vs baseline {json.dumps(opened.get('open_vs_baseline'), ensure_ascii=False)}")
    if sens:
        red = [x['omit'] for x in sens if x['late_reached'] == 0]
        L.append('[sensitivity] closed runs with one piece removed (each must leak): ' + ', '.join(f"-{x['omit']}: late {x['late_reached']} {x['ac_s2']}" for x in sens) +
                 (f' | REDUNDANT (no leak without it): {red}' if red else ' | every piece is load-bearing'))
    L.append('[open] items not reached:')
    for it in opened['items']:
        if not it['reached'] and not it['underground']: L.append(f"  MISSED {it['kind']} {it['id']} '{it['label']}' ({it['x']},{it['z']}) zone {it['zone']} ea={it['ea']}")
    L.append('[both] underground items (2-D flood cannot judge; EA mine = reached through the start): ' + ', '.join(f"{it['id']}{'(EA)' if it['ea'] else '(late)'}" for it in closed['items'] if it['underground']))
    L.append('content table: kind id | EA/late | zone | realm | closed | open')
    oi = {(i['kind'], i['id']): i for i in opened['items']}
    for it in closed['items']:
        o = oi.get((it['kind'], it['id']), {})
        L.append(f"  {it['kind']} {it['id']} | {'EA' if it['ea'] else 'late'} | {it['zone']} | {it['realm'] or it['realm_poly']} | {'reach' if it['reached'] else '-'}{' (underground)' if it['underground'] else ''} | {'reach' if o.get('reached') else '-'}")
    if bey:
        b, bad, n_s = bey
        L.append(f"beyond308.json: {b['beyond_ha']} ha (raw {b['raw_beyond_ha']} ha) in {len(b['beyond_rings'])} rings, {b['ring_vertices']} vertices, eroded {ERODE} m, specks dropped {b['dropped_specks']}; ring test vs mask disagreements {bad}/{n_s} sampled open-reach nodes")
        L.append(f"  item check: EA items inside a ring {b['item_check']['ea_inside']}; late items reached in open but outside every ring {b['item_check']['late_reached_outside']}")
        L.append(f"  EA rests {b['ea_rest_ids']}; EA underground boxes {[(x['id'], x['centre'], x['size']) for x in b['ea_underground_volumes']]}; MainPath-only underground clusters skipped {len(b['underground_clusters_without_content'])}; hash {b['source_hash']}")
    (REP / 'closure308.txt').write_text('\n'.join(L) + '\n', encoding='utf-8')
    print('\n'.join(L[:14]))


if __name__ == '__main__':
    args = [a for a in sys.argv[1:] if not a.startswith('--')]; cmd = args[0] if args else 'all'
    seed = 'routes' if '--seed' in sys.argv and sys.argv[sys.argv.index('--seed') + 1] == 'routes' else 'newgame'
    draft = '--draft' in sys.argv
    G = lattice(); log('lattice ready')
    if '--omit' in sys.argv:   # sensitivity: the proof must FAIL when a piece of the seal is taken away (writes seal308-closed-omit-<x>.json only)
        for x in sys.argv[sys.argv.index('--omit') + 1].split(','):
            d, _, _ = run('closed', seed, draft, G, omit=x)
            print(x, json.dumps(d['verdict'], ensure_ascii=False), 'late reached:', ', '.join(f"{i['id']}@{i.get('crossing')}" for i in d['late_reached'][:6]))
        sys.exit(0)
    if cmd in ('closed', 'all'): dc, rc, _ = run('closed', seed, draft, G)
    if cmd in ('open', 'all'): do, ro, _ = run('open', seed, draft, G)
    if cmd in ('baseline', 'all'): db, rb, _ = run('baseline', seed, draft, G)
    if cmd == 'all':
        # AC-S3: every place / rest / trail entry the lattice reaches without any seal must be reached in the open state
        reqk = ('place', 'trail_entry', 'arena', 'rest', 'rest_point', 'rest_marker')
        base = {(i['kind'], i['id']): i['reached'] for i in db['items']}
        req = [i for i in do['items'] if i['kind'] in reqk and not i['underground']]
        caused = [i for i in req if base.get((i['kind'], i['id'])) and not i['reached']]
        lattice_out = [i for i in req if not base.get((i['kind'], i['id']))]
        do['verdict'].update({'AC-S3': 'PASS' if not caused else 'FAIL', 'missed_caused_by_seal': len(caused),
                              'lattice_unreachable_without_seal': [f"{i['kind']} {i['id']}" for i in lattice_out],
                              'n_of_n': f"{sum(i['reached'] for i in req if base.get((i['kind'], i['id'])))}/{sum(1 for i in req if base.get((i['kind'], i['id'])))} (items the lattice reaches at all)"})
        do['open_vs_baseline'] = dict(open_reach_ha=do['reach_ha'], baseline_reach_ha=db['reach_ha'],
                                      open_minus_baseline_ha=round(float((ro & ~rb).sum()) * 16 / 1e4, 1), baseline_minus_open_ha=round(float((rb & ~ro).sum()) * 16 / 1e4, 1))
        (REP / 'seal308-open.json').write_text(json.dumps(do, ensure_ascii=False, indent=1), encoding='utf-8')
        log('open', json.dumps(do['verdict'], ensure_ascii=False))
        bey = beyond(rc, ro, G, dc['items'], dc, do) if seed == 'newgame' else None
        # sensitivity: every piece of the seal must matter (closed run with it removed reports late content)
        sens = []
        for x in ['panel', 'doors'] + list(E305.SEAL308):
            d, _, _ = run('closed', seed, draft, G, omit=x, write=False)
            sens.append(dict(omit=x, late_reached=d['verdict']['late_content_reached'], ac_s2=d['verdict']['AC-S2']))
        ledger(dc, do, bey, db, sens)
