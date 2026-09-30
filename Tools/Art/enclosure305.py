"""#305 field enclosure, phase 1 (EA outer edge, dense forest + hidden shell). Offline driver; writes only Enclosure305 folders.

  python Tools/Art/enclosure305.py segments   # lines305.json -> segments305.json (phase-1 selection, hand-curated overrides)
  python Tools/Art/enclosure305.py forest     # segments305.json -> Out/forest305_candidates.json (tree/shrub candidates)
  python Tools/Art/enclosure305.py all

Contract: Docs/Specs/SPEC-WORLD-ENCLOSURE-305.md. Phase 1 builds only N6 (dense forest with a collision shell hidden in the
thicket) on the EA outer edge (청림 · 상경 가도 · 황경 성저). W1 wall lines become N6 (사산금표 숲); R rock rows, realm seams,
new gates and shortcuts are out of scope (D305). Every number is TEST.
Conventions: points are world (x, z); block = +1 when the walled-off side is right of travel (n = (dz, -dx)), -1 when left.
"""
import json, math, sys
from pathlib import Path
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
ENC = ROOT / 'Art/World/Compact/Rebuild/Enclosure305'
OUT = ENC / 'Out'
A = ROOT / 'Oheangbu/Assets/_Project'
H4, W4, C4 = 1501, 1001, 4.0

# Hand-curated overrides (id -> dict). exclude=True drops a run; kit/params override. Reasons go in 'why'.
OVERRIDES = {}

REALM_PALETTE = {
    # prototype ids copied from the DryLandscape sheet (BUILD_PLAN §3); Meshy_Pinus / *_SM_Bush / Detail261_ excluded
    'Cheongrim': dict(trees=['Cheongrim_SM_PinusDensiflora_Spring_2', 'Cheongrim_SM_UlmusDavidiana_Summer_2'],
                      shrubs=['Cheongrim_SM_Deparia_1', 'Cheongrim_SM_Deparia_3'], logs=['Cheongrim_SM_M_WoodLog']),
    'Hwanggyeong': dict(trees=['Hwanggyeong_SM_PinusDensiflora_Spring_2', 'Hwanggyeong_SM_UlmusDavidiana_Summer_2'],
                        shrubs=['Hwanggyeong_SM_Deparia_1', 'Hwanggyeong_SM_Deparia_3'], logs=['Hwanggyeong_SM_M_WoodLog']),
    # 적로 is burned land (LDB-REALMS "불이 꺼지지 않는 땅"): no green 밀림 — a burned deadfall of leafless trunks and fallen logs
    'Jeokro': dict(trees=['Jeokro_SM_MeliaAzedarach_Winter_1', 'Jeokro_SM_MeliaAzedarach_Winter_1', 'Jeokro_SM_PinusDensiflora_Spring_2'],
                   shrubs=['Jeokro_SM_Deparia_1', 'Jeokro_SM_Deparia_3'], logs=['Jeokro_SM_M_WoodLog'], log_step=3.0),
    'Cheolong': dict(trees=['Cheolong_SM_PinusDensiflora_Spring_2', 'Cheolong_SM_MeliaAzedarach_Winter_1'],
                     shrubs=['Cheolong_SM_Deparia_1', 'Cheolong_SM_Deparia_3'], logs=['Cheolong_SM_M_WoodLog']),
}


def height_field():
    return np.fromfile(A / 'Art/World/Finish297/Surface/height.bytes', '<f4').reshape(H4, W4).astype(np.float64)


def sampler(h):
    def hs(x, z):
        j = np.clip(np.asarray(x, float) / C4, 0, W4 - 1.001); i = np.clip(np.asarray(z, float) / C4, 0, H4 - 1.001)
        j0 = np.floor(j).astype(int); i0 = np.floor(i).astype(int); fj = j - j0; fi = i - i0
        return h[i0, j0] * (1 - fj) * (1 - fi) + h[i0, j0 + 1] * fj * (1 - fi) + h[i0 + 1, j0] * (1 - fj) * fi + h[i0 + 1, j0 + 1] * fj * fi
    return hs


def resample(P, step):
    P = np.asarray(P, float); seg = np.hypot(*np.diff(P, axis=0).T); s = np.concatenate([[0], np.cumsum(seg)])
    n = max(2, int(math.ceil(s[-1] / step)) + 1); t = np.linspace(0, s[-1], n)
    return np.stack([np.interp(t, s, P[:, 0]), np.interp(t, s, P[:, 1])], 1), t


def blocked_components(blocked):
    """4-connected labels of the option-B blocked mask (10 m grid)."""
    lab = np.zeros(blocked.shape, np.int32); n = 0
    for i0, j0 in zip(*np.nonzero(blocked)):
        if lab[i0, j0]: continue
        n += 1; st = [(i0, j0)]; lab[i0, j0] = n
        while st:
            i, j = st.pop()
            for a, b in ((i + 1, j), (i - 1, j), (i, j + 1), (i, j - 1)):
                if 0 <= a < blocked.shape[0] and 0 <= b < blocked.shape[1] and blocked[a, b] and not lab[a, b]:
                    lab[a, b] = n; st.append((a, b))
    return lab, n


def run_components(r, lab):
    """Blocked components on the walled-off side of a run (probe 8 m off the line at 9 stations)."""
    P = np.asarray(r['points'], float); seg = np.hypot(*np.diff(P, axis=0).T); s = np.concatenate([[0], np.cumsum(seg)])
    side = 1 if r['blocked_side'] == 'right' else -1; out = set()
    for t in np.linspace(0.05, 0.95, 9) * s[-1]:
        k = min(int(np.searchsorted(s, t)), len(P) - 1); k0 = max(k - 1, 0)
        d = P[k] - P[k0]; n = np.hypot(*d) or 1.0; nr = np.array([d[1], -d[0]]) / n * side
        x, z = P[k] + nr * 8
        c = int(lab[int(np.clip(z // 10, 0, lab.shape[0] - 1)), int(np.clip(x // 10, 0, lab.shape[1] - 1))])
        if c: out.add(c)
    return out


def existing_walls():
    """Existing wall polylines (x, z): #297 capital loop, 철옹 walls, 궁성 wall lines."""
    FIN = ROOT / 'Art/World/Compact/Rebuild/Finish297'
    jl = lambda p: json.loads(p.read_text(encoding='utf-8-sig'))
    out = []
    cap = jl(FIN / 'Capital/layout.json'); cc = cap['loop']['corners']; out.append(np.asarray(cc + [cc[0]], float))
    che = jl(FIN / 'Cheolong/layout.json')
    for w in che['walls']: out.append(np.asarray([(q[0], q[2]) for q in w['walk']], float))
    hwa = jl(FIN / 'Hwanggyeong/layout.json')
    for w in hwa['wallLines']: out.append(np.asarray([w['a'], w['b']], float))
    return out


def nearest_on(polys, p):
    best = (1e18, None)
    for Q in polys:
        for a, b in zip(Q[:-1], Q[1:]):
            d = b - a; L2 = float(d @ d) or 1e-9; t = np.clip(((p - a) @ d) / L2, 0, 1); q = a + d * t
            dd = float(np.hypot(*(p - q)))
            if dd < best[0]: best = (dd, q)
    return best


def solid4_map():
    """4 m node lattice: True where the player cannot stand (slope >= 45, deep water, within 8 m of the terrain edge,
    existing walls stamped 3 m half width). Same rules as emptiness305 walk4, minus the forced route corridors."""
    h = height_field()
    gz, gx = np.gradient(h, C4); slope = np.degrees(np.arctan(np.hypot(gx, gz)))
    wl = np.fromfile(A / 'Art/World/Watershed295/Surface/waterlevel.bytes', '<f4').reshape(H4, W4).astype(np.float64)
    wet = np.fromfile(ROOT / 'Art/World/Compact/Rebuild/Watershed295/Generated/wet.bytes', np.uint8).reshape(H4, W4) > 0
    water = wet & (np.where(wl > -1000, wl - h, -1) > 0.5)
    X4 = np.arange(W4)[None, :] * C4; Z4 = np.arange(H4)[:, None] * C4
    inside = (X4 >= 8) & (X4 <= 4000 - 8) & (Z4 >= 8) & (Z4 <= 6000 - 8)
    solid = (slope >= 45) | water | ~inside
    for P in existing_walls():
        for a, b in zip(P[:-1], P[1:]):
            n = max(1, int(np.hypot(*(b - a)) / 2))
            for t in np.linspace(0, 1, n + 1):
                x, z = a + (b - a) * t; j0, i0 = int(round(x / C4)), int(round(z / C4))
                solid[max(0, i0 - 1): i0 + 2, max(0, j0 - 1): j0 + 2] = True
    return solid


def near_solid(solid, e, reach):
    i0, j0 = int(np.clip(e[1] / C4, 0, H4 - 1)), int(np.clip(e[0] / C4, 0, W4 - 1)); r = int(reach / C4) + 1
    win = solid[max(0, i0 - r): i0 + r + 1, max(0, j0 - r): j0 + r + 1]
    if not win.any(): return None
    ii, jj = np.nonzero(win); q = np.stack([(jj + max(0, j0 - r)) * C4, (ii + max(0, i0 - r)) * C4], 1)
    d = np.hypot(q[:, 0] - e[0], q[:, 1] - e[1]); return q, d


def snap_ends(sel, solid, reach=25.0, overshoot=2.0, cone=75.0):
    """Push every end into solid ground: the nearest solid node (<= reach m) inside a +-cone deg cone around the end's outward
    tangent (check 305: an unconstrained nearest node could lie behind the end and fold the shell back on itself). Ends near
    the terrain boundary get one more point 10 m beyond it (the 8 m edge strip is walkable up to x/z = 0/4000/6000)."""
    for s in sel:
        if s['closed']: continue
        P = [list(p) for p in s['points']]
        for end in (0, -1):
            Q = np.asarray(P, float); e = Q[end]
            tan = (Q[0] - Q[min(3, len(Q) - 1)]) if end == 0 else (Q[-1] - Q[max(-4, -len(Q))]); tan = tan / (np.hypot(*tan) or 1.0)
            hit = near_solid(solid, e, reach)
            if hit is not None:
                q, d = hit; v = q - e; cosang = (v @ tan) / np.maximum(d, 1e-6)
                ok = (d <= reach) & ((cosang >= math.cos(math.radians(cone))) | (d < 3.0))
                if ok.any():
                    k = int(np.argmin(np.where(ok, d, 1e9)))
                    if d[k] >= 0.5:
                        tgt = q[k] + v[k] / d[k] * overshoot
                        if end == 0: P.insert(0, [round(float(tgt[0]), 2), round(float(tgt[1]), 2)])
                        else: P.append([round(float(tgt[0]), 2), round(float(tgt[1]), 2)])
                        s['notes'].append(f"end {'0' if end == 0 else '1'} snapped {d[k]:.1f} m into solid ground")
            e = np.asarray(P[end], float)
            edge = min((e[0], np.array([-10.0, e[1]])), (4000 - e[0], np.array([4010.0, e[1]])), (e[1], np.array([e[0], -10.0])),
                       (6000 - e[1], np.array([e[0], 6010.0])), key=lambda t: t[0])
            if edge[0] <= 20:
                if end == 0: P.insert(0, [round(float(edge[1][0]), 2), round(float(edge[1][1]), 2)])
                else: P.append([round(float(edge[1][0]), 2), round(float(edge[1][1]), 2)])
                s['notes'].append(f"end {'0' if end == 0 else '1'} carried 10 m past the terrain edge")
        s['points'] = P
        s['length_m'] = round(float(np.hypot(*np.diff(np.asarray(P), axis=0).T).sum()), 1)


def tie_ends(sel, runs, zone, solid):
    """Close open run ends (see segments()). Order:
      a) continue along the neighbouring unselected frontier run; after it leaves the EA zones keep following it until a point
         lies within 20 m of solid ground (or the run ends; <= 800 m past EA) - check 305: a 30 m stub left walk-round leaks;
      b) straight to another selected run <= 80 m;  c) to an existing wall <= 60 m;  d) to the map edge <= 250 m;
      e) along the end tangent up to 150 m until solid ground. Extensions are recorded in notes."""
    walls = existing_walls()
    chosen = {s['id'] for s in sel}
    def is_ea(x, z):
        return int(zone[int(np.clip(z // 10, 0, 599)), int(np.clip(x // 10, 0, 399))]) in (0, 1)
    def solid_near(x, z, r):
        h = near_solid(solid, (x, z), r); return h is not None and bool((h[1] <= r).any())
    def near_sel(e, skip, r=12):
        for t in sel:
            if t is skip: continue
            Q = np.asarray(t['points'])
            if np.min(np.hypot(Q[:, 0] - e[0], Q[:, 1] - e[1])) <= r: return True
        return False
    added = []
    for s in list(sel):
        for end in (0, 1):
            P = np.asarray(s['points'], float)
            e = P[0] if end == 0 else P[-1]
            if s['closed'] or near_sel(e, s) or solid_near(e[0], e[1], 6): continue
            done = False
            for r in runs:
                if r['id'] in chosen or r['kind'] != 'frontier' or r['kit1'] == 'EX': continue
                Q = np.asarray(r['points'], float)
                for qe, Qo, flip in ((Q[0], Q, 1), (Q[-1], Q[::-1], -1)):   # reversing the points swaps left/right
                    if np.hypot(*(qe - e)) <= 12:
                        keep = [Qo[0]]; out_len = None; run_len = 0.0
                        for a_, b_ in zip(Qo[:-1], Qo[1:]):
                            keep.append(b_); run_len += float(np.hypot(*(b_ - a_)))
                            if out_len is None and not is_ea(*b_): out_len = run_len
                            if out_len is not None and (solid_near(b_[0], b_[1], 20) or run_len - out_len >= 800): break
                        cid = f"{r['id']}c{s['id'][5:]}"
                        sel.append(dict(id=cid, kit='N6', source_kit=r['kit1'], realm=r['realm'], zone=r['zone'], ea=r['ea'], closed=False,
                                        block=(1 if r['blocked_side'] == 'right' else -1) * flip,
                                        points=[[round(float(x), 2), round(float(z), 2)] for x, z in keep],
                                        length_m=round(run_len, 1), components=[], params={},
                                        notes=[f"connector: first {run_len:.0f} m of {r['id']} ({r['zone']}) to close the open end of {s['id']} (TestOnly past EA)"]))
                        chosen.add(r['id']); added.append(cid); done = True; break
                if done: break
            if done: continue
            tan = (P[0] - P[min(3, len(P) - 1)]) if end == 0 else (P[-1] - P[max(-4, -len(P))])
            tan = tan / (np.hypot(*tan) or 1.0)
            ext, why = None, ''
            edges = [(e[0], np.array([-10.0, e[1]])), (4000 - e[0], np.array([4010.0, e[1]])), (e[1], np.array([e[0], -10.0])), (6000 - e[1], np.array([e[0], 6010.0]))]
            de, qe = min(edges, key=lambda t: t[0])
            if de <= 30: ext = qe; why = f'to map edge ({de:.0f} m)'
            best = (1e9, None)
            for t in sel:
                if ext is not None: break
                if t is s or t['id'].endswith('c' + s['id'][5:]): continue      # never loop back onto this segment's own connector
                Q = np.asarray(t['points'], float)
                for a_, b_ in zip(Q[:-1], Q[1:]):
                    dd = b_ - a_; L2 = float(dd @ dd) or 1e-9; tt = np.clip(((e - a_) @ dd) / L2, 0, 1); qq = a_ + dd * tt
                    dist = float(np.hypot(*(e - qq)))
                    ahead = dist < 1e-6 or float((qq - e) @ tan) / dist >= 0.3                # target must lie ahead of the end
                    if ahead and dist < best[0]: best = (dist, qq)
            if ext is None and best[0] <= 80: ext = best[1]; why = f'straight {best[0]:.0f} m to another run'
            if ext is None:
                dw, q = nearest_on(walls, e)
                if dw <= 60: ext = q; why = f'joint to existing wall ({dw:.0f} m)'
            if ext is None and de <= 250: ext = qe; why = f'to map edge ({de:.0f} m)'
            if ext is None:
                for k in range(1, 31):
                    x, z = e + tan * 5 * k
                    if solid_near(x, z, 3): ext = np.array([x, z]); why = f'straight {5*k} m to solid ground'; break
            if ext is None:
                s['notes'].append(f'end {end} still open after tie pass'); continue
            seg_pts = [list(map(float, ext))] + s['points'] if end == 0 else s['points'] + [list(map(float, ext))]
            s['points'] = [[round(x, 2), round(z, 2)] for x, z in seg_pts]
            s['length_m'] = round(float(np.hypot(*np.diff(np.asarray(s['points']), axis=0).T).sum()), 1)
            s['notes'].append(f'end {end}: {why}')
    return added


def segments():
    """Phase-1 set: option-B frontier runs in the EA zones (청림 · 상경 가도 · 황경 성저). Goal: from the EA corridors the
    blocked EA land is unreachable without first leaving EA through a road (closure305.py --segments floods EA only).
    The blocked land behind is still reachable through the late realms until their own phase (LDB-REALMS: 구체 배치 = EA
    한정). Components listed in OVERRIDES['_return_components'] go back to walkable land (content candidates)."""
    L = json.loads((ENC / 'lines305.json').read_text(encoding='utf-8'))
    G = np.load(ENC / '_work/grids.npz'); walkable = G['walkable']
    B = np.load(ENC / '_work/optB.npz'); blocked = B['blocked']; zone = B['zone']
    lab, ncomp = blocked_components(blocked)
    ea_cells = np.isin(zone, [0, 1])
    ea_comp = set(np.unique(lab[blocked & ea_cells])) - {0}
    returned = set(OVERRIDES.get('_return_components', []))
    comp_of = {}
    for r in L['runs']:
        comp_of[r['id']] = run_components(r, lab) if r['kind'] == 'frontier' and r['blocked_side'] in ('left', 'right') else set()
    sel = []
    for r in L['runs']:
        o = OVERRIDES.get(r['id'], {})
        why = []
        if r['kind'] != 'frontier':
            continue                                   # realm seams wait for D305 follow-up (passes, 관문)
        comps = comp_of[r['id']]
        if not r['ea']:
            continue                                   # phase 1 = EA corridors sealed inside EA (late realms: own phase, LDB-REALMS)
        if comps and comps <= returned:
            continue                                   # component returned to walkable land
        if o.get('exclude'):
            continue
        kit = o.get('kit', 'N6')
        if r['kit1'] == 'W1':
            why.append('W1 line on a hill foot -> N6 사산금표 숲 (W1 wing walls wait for a ridge line + art gate)')
        if r['kit1'] == 'R':
            why.append('R rock row (reads as a 숫 gate) -> N6 wooded steep slope; bare cliff face waits for phase 2')
        sel.append(dict(id=r['id'], kit=kit, source_kit=r['kit1'], realm=r['realm'], zone=r['zone'], ea=r['ea'], closed=r['closed'],
                        block=1 if r['blocked_side'] == 'right' else -1, points=r['points'], length_m=r['length_m'],
                        components=sorted(int(c) for c in comps), params=o.get('params', {}), notes=why + ([o['why']] if 'why' in o else [])))
    comp_ha = {int(c): round(float((lab == c).sum()) / 100, 1) for c in ea_comp}
    SOLID = solid4_map()
    tie_ends(sel, L['runs'], zone, SOLID)
    snap_ends(sel, SOLID)
    # ends: tie = within 12 m of another selected run, or of non-walkable ground (deep water / steep) within 15 m
    ends_all = []
    for s in sel:
        P = np.asarray(s['points'])
        for k, e in enumerate((P[0], P[-1])):
            tie = s['closed']
            if not tie:
                for t in sel:
                    if t is s: continue
                    Q = np.asarray(t['points'])
                    if np.min(np.hypot(Q[:, 0] - e[0], Q[:, 1] - e[1])) <= 12: tie = True; break
            if not tie:
                i0, j0 = int(np.clip(e[1] / C4, 0, H4 - 1)), int(np.clip(e[0] / C4, 0, W4 - 1))
                tie = bool(SOLID[max(0, i0 - 1): i0 + 2, max(0, j0 - 1): j0 + 2].any())
            ends_all.append(tie)
        s['ends'] = ['tie' if ends_all[-2] else 'open', 'tie' if ends_all[-1] else 'open']
    doc = dict(version='305.1', source='lines305.json', phase=1,
               rule='EA frontier only; kit N6 (dense forest + hidden shell); W1->N6; R, seams, gates, shortcuts out (D305)',
               components=dict(ea=sorted(int(c) for c in ea_comp), ha=comp_ha, returned=sorted(returned)),
               summary=dict(segments=len(sel), km=round(sum(s['length_m'] for s in sel) / 1000, 2),
                            ea_km=round(sum(s['length_m'] for s in sel if s['ea']) / 1000, 2),
                            by_zone={z: round(sum(s['length_m'] for s in sel if s['zone'] == z) / 1000, 2) for z in sorted({s['zone'] for s in sel})},
                            open_ends=sum(e == 'open' for s in sel for e in s['ends'])),
               segments=sel)
    (ENC / 'segments305.json').write_text(json.dumps(doc, ensure_ascii=False, indent=1), encoding='utf-8')
    print(json.dumps(doc['summary'], ensure_ascii=False))


def stable(*keys):
    """Deterministic 32-bit hash -> [0,1)."""
    h = 2166136261
    for k in keys:
        for ch in str(k):
            h = ((h ^ ord(ch)) * 16777619) & 0xFFFFFFFF
    return h / 4294967296.0


def forest():
    """Candidates in a band along each segment. Band axis u: 0 = barrier line, + = blocked side.
    shrubs: u in [-2.4, +0.8] every ~1.6 m (thicket that the shell hides in); trees: u in [-2, +BAND] with density falling off.
    Offline filters: route corridors, deep water, protected cells, slope > 40 deg. Scene filters (existing trees, preserved
    areas, content points, physics ground) run in the editor (CompactEnclosure305)."""
    S = json.loads((ENC / 'segments305.json').read_text(encoding='utf-8'))
    h = height_field(); hs = sampler(h)
    gz, gx = np.gradient(h, C4); slope = np.degrees(np.arctan(np.hypot(gx, gz)))
    wl = np.fromfile(A / 'Art/World/Watershed295/Surface/waterlevel.bytes', '<f4').reshape(H4, W4).astype(np.float64)
    prot = np.fromfile(A / 'Art/World/Watershed295/Surface/protected.bytes', np.uint8).reshape(H4, W4) > 0
    G = np.load(ENC / '_work/grids.npz'); d_route = G['d_route']          # 10 m grid, metres to nearest route centre line
    band = float(S.get('band_m', 24.0))
    cands = []
    stats = dict(tree=0, shrub=0, dropped_route=0, dropped_water=0, dropped_prot=0, dropped_slope=0)

    def ok(x, z):
        i4, j4 = int(np.clip(z / C4, 0, H4 - 1)), int(np.clip(x / C4, 0, W4 - 1))
        if d_route[int(np.clip(z // 10, 0, 599)), int(np.clip(x // 10, 0, 399))] < 12: stats['dropped_route'] += 1; return False
        if wl[i4, j4] > -1000 and wl[i4, j4] - h[i4, j4] > 0.3: stats['dropped_water'] += 1; return False
        if prot[i4, j4]: stats['dropped_prot'] += 1; return False
        if slope[i4, j4] > 40: stats['dropped_slope'] += 1; return False
        return True

    for s in S['segments']:
        pal = REALM_PALETTE.get(s['realm'], REALM_PALETTE['Cheongrim'])
        P, t = resample(s['points'], 1.0)
        d = np.gradient(P, axis=0); d /= np.maximum(np.hypot(d[:, 0], d[:, 1])[:, None], 1e-6)
        nrm = np.stack([d[:, 1], -d[:, 0]], 1) * s['block']            # unit normal pointing into the blocked side
        L = t[-1]
        # jittered lattice in (arc s, offset u)
        # check 305 close-up: 4.5 m trunks + knee-high ferns read as see-through -> denser front rank, big fern clumps at the shell
        rows = [('shrub', -2.4, 0.8, 1.6, 1.0), ('shrub', -3.8, -2.4, 2.2, 1.0)] + [('tree', u0, u1, sp, 1.0) for u0, u1, sp in ((-2.0, 6.0, 3.4), (6.0, 16.0, 5.0), (16.0, band, 7.0))]
        n = 0
        # deadfall: fallen trunks lying roughly along the shell (visible reason the thicket cannot be crossed); not gate vocabulary
        for a in np.arange(2.0, L, pal.get('log_step', 4.0)):
            ja = (stable(s['id'], 'log', a, 'a') - 0.5) * 3.0; aa = min(max(a + ja, 0), L); k = min(int(round(aa)), len(P) - 1)
            u = -0.3 + stable(s['id'], 'log', a, 'u') * 0.9
            x, z = P[k] + nrm[k] * u
            if not ok(x, z): continue
            tang = math.degrees(math.atan2(d[k][0], d[k][1]))           # Unity yaw of the travel direction (x, z)
            yaw = (tang + (stable(s['id'], 'log', a, 'y') - 0.5) * 50.0) % 360.0
            proto = pal['logs'][0]
            cands.append(dict(Id=f"enclosure305_{s['id'][5:]}_{n}", ClusterId=s['id'], PrototypeId=proto,
                              Position=[round(float(x), 2), round(float(hs(x, z)), 2), round(float(z), 2)],
                              Euler=[0, round(yaw, 1), 0], Scale=round(1.3 + 0.8 * stable(s['id'], 'log', a, 's'), 3), kind='log', u=round(float(u), 2)))
            stats['log'] = stats.get('log', 0) + 1; n += 1
        for kind, u0, u1, sp, _ in rows:
            for a in np.arange(0, L, sp):
                for u in np.arange(u0, u1, sp):
                    ja = (stable(s['id'], kind, a, u, 'a') - 0.5) * sp * 0.9
                    ju = (stable(s['id'], kind, a, u, 'u') - 0.5) * sp * 0.9
                    aa = min(max(a + ja, 0), L); k = int(round(aa)); k = min(k, len(P) - 1)
                    x, z = P[k] + nrm[k] * (u + ju)
                    if not ok(x, z): continue
                    ids = pal['trees'] if kind == 'tree' else pal['shrubs']
                    proto = ids[int(stable(s['id'], kind, a, u, 'p') * len(ids)) % len(ids)]
                    yaw = stable(s['id'], kind, a, u, 'y') * 360.0
                    sc = (0.85 + 0.35 * stable(s['id'], kind, a, u, 's')) if kind == 'tree' else ((1.6 + 1.0 * stable(s['id'], kind, a, u, 's')) if u > -2.5 else (1.1 + 0.5 * stable(s['id'], kind, a, u, 's')))
                    cands.append(dict(Id=f"enclosure305_{s['id'][5:]}_{n}", ClusterId=s['id'], PrototypeId=proto,
                                      Position=[round(float(x), 2), round(float(hs(x, z)), 2), round(float(z), 2)],
                                      Euler=[0, round(yaw, 1), 0], Scale=round(sc, 3), kind=kind, u=round(float(u + ju), 2)))
                    stats[kind] += 1; n += 1
    OUT.mkdir(parents=True, exist_ok=True)
    doc = dict(version='305.1', band_m=band, stats=stats, prototypes=sorted({c['PrototypeId'] for c in cands}), candidates=cands)
    (OUT / 'forest305_candidates.json').write_text(json.dumps(doc, ensure_ascii=False), encoding='utf-8')
    print(json.dumps(dict(stats=stats, prototypes=doc['prototypes']), ensure_ascii=False))


if __name__ == '__main__':
    cmd = sys.argv[1] if len(sys.argv) > 1 else 'all'
    if cmd in ('segments', 'all'): segments()
    if cmd in ('forest', 'all'): forest()
