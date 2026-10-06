"""#305 field enclosure, phase 1 (EA outer edge, dense forest + hidden shell). Offline driver; writes only Enclosure305 folders.

  python Tools/Art/enclosure305.py segments   # lines305.json -> segments305.json (phase-1 selection, hand-curated overrides)
  python Tools/Art/enclosure305.py forest     # segments305.json -> Out/forest305_candidates.json (tree/shrub candidates)
  python Tools/Art/enclosure305.py all

Contract: Docs/Specs/SPEC-WORLD-ENCLOSURE-305.md. Phase 1 builds only N6 (dense forest with a collision shell hidden in the
thicket) on the EA outer edge (청림 · 상경 가도 · 황경 성저). W1 wall lines become N6 (사산금표 숲); R rock rows, realm seams,
new gates and shortcuts are out of scope (D305). Every number is TEST.
#308 (D308-3, Spec §1b′, segments305.json 305.2): OVERRIDES['_include'] / ['_extra_runs'] add the 11 seal runs that close the
outer loop before the south gate (seams 034·036·037·038·041·051·055·056·084, 고산 130, joint J308) as N6 with an explicit
blocked side and end treatment (seal308_segments). The phase-1 selection runs first and is never touched: its 49 entries stay
byte-identical (check: python Tools/Art/enclosure305.py verify-phase1, against Seal308/backup/segments305.v305_1.json). The
palisade between E305_084 and E305_056 is not a run: seal308.json + Tools/Art/seal308_palisade.py. Pre-#308 copy:
Tools/Art/enclosure305.pre308.py.
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
#   '_return_components': option-B blocked components given back to walkable land (phase 1, unused)
#   '_include': #308 seal runs (D308-3, Spec §1b′) taken from lines305.json although they are seams / late-realm frontier.
#       block      +1 right / -1 left of the run's point order (lines305 'both' for seams): the late-realm side, from the
#                  new-game flood of seal308_closure.py --draft (player side = EA, Spec "막는 쪽")
#       trim       (start_m, end_m) cut off the line before the end treatment (start: the Seal308 palisade takes over)
#       ends       (end0, end1): ('palisade', 'A'|'B') ties to a Seal308 end bastion (seal308.json anchors);
#                  ('joint', run_id) moves the end onto that run's line and cuts the joined seal run there, so both ends coincide
#                  (a build-scene joint + plug); ('t', run_id, overshoot_m) carries the end across a phase-1 run's line by
#                  overshoot_m (T joint; phase-1 lines are never edited); ('wall', offset_m) carries it along its tangent to
#                  the nearest existing wall line (walk centre) + offset_m (negative = stop on the outer face);
#                  ('keep',) leaves a coincident end as it is (lines305 already meets the neighbour)
#       palette    REALM_PALETTE key for the forest band (Jeokro = burned deadfall, never green 밀림)
#   '_extra_runs': runs that are not in lines305.json (hand points, same keys + id/points/realm/zone/ea/kind)
SEAL308 = ('E305_034', 'E305_036', 'E305_037', 'E305_038', 'E305_041', 'E305_051', 'E305_055', 'E305_056', 'E305_084', 'E305_130', 'E305_J308')
_WHY_SEAM_HW = ('D308-3 seal: seam 상경 가도/성저 | 황경 외곽·적로 (lines kit {kit}/{kit1}) -> N6 황경 숲 + shell; final kit N1 waits for a '
                'terrain-op tool (Spec Temporary Exception). World reason: 국상 계엄 - 성저 서쪽 산자락을 봉산 금표 숲으로 막아 남문 밖 길만 남겼다')
_WHY_SEAM_JR = ('D308-3 seal: seam 상경 고개 | 적로 (lines kit {kit}/{kit1}, R rejected = reads as a 숫 gate) -> N6 적로 불탄 쓰러진 숲 + shell '
                '(no green 밀림 in 적로, LORE_CHECK 116 tension follows the phase-1 precedent). World reason: 꺼지지 않는 불에 쓰러진 옛 전장 숲이 고개 옆 비탈을 덮었다')
OVERRIDES = {
    '_include': {
        # block: player side = EA from the closed new-game flood (seal308_closure.py closed --draft, 2026-10-02): reach on the right
        # side 63-75/75 probes vs left 0-5/75 for the seams (block left = west / south), the reverse for 055 / 056 / 130
        'E305_084': dict(block=-1, trim=(12.0, 0.0), ends=(('palisade', 'A'), ('joint', 'E305_038')), palette='Jeokro', why=_WHY_SEAM_JR),
        'E305_038': dict(block=-1, ends=(('keep',), ('keep',)), palette='Hwanggyeong', why=_WHY_SEAM_HW),
        'E305_036': dict(block=-1, ends=(('keep',), ('keep',)), palette='Hwanggyeong', why=_WHY_SEAM_HW),
        'E305_041': dict(block=-1, ends=(('keep',), ('keep',)), palette='Hwanggyeong', why=_WHY_SEAM_HW),
        'E305_051': dict(block=-1, ends=(('keep',), ('keep',)), palette='Hwanggyeong', why=_WHY_SEAM_HW),
        'E305_037': dict(block=-1, ends=(('keep',), ('keep',)), palette='Hwanggyeong', why=_WHY_SEAM_HW),
        'E305_034': dict(block=-1, ends=(('keep',), ('keep',)), palette='Hwanggyeong', why=_WHY_SEAM_HW),
        'E305_056': dict(block=1, ends=(('palisade', 'B'), ('keep',)), palette='Jeokro', why=_WHY_SEAM_JR),
        'E305_055': dict(block=1, ends=(('keep',), ('t', 'E305_054', 3.0)), palette='Jeokro', why=_WHY_SEAM_JR),
        'E305_130': dict(block=1, ends=(('keep',), ('keep',)), palette='Cheongrim',
                         why='D308-3 seal: 고산 frontier gap between the open ends of E305_128c001 and E305_133c020 (check-scene LEAK x2) -> N6 청림 숲; '
                             'permanent world edge (Spec 남은 일: 남문 뒤 현강 쪽 산길 도달은 열림 폐합으로 확인). World reason (PROPOSED, lore review): '
                             '청림 동쪽 고산 능선의 틈을 묵은 금표 숲이 메워, 능선 너머로 넘는 산길이 없다'),
    },
    '_extra_runs': [
        # Spec (1490,2570)->(1652,2556): the end is moved onto the straight west wall 6 m north of the 14 m corner fillet tangent
        # (capital loop corner (1650,2555), Finish297 Capital layout fillet 14 m). The loop line is the wall-walk centre (walk
        # +-1.6 m, parapet .55, batter .12 over >= 10 m: outer foot ~3.4 m out), so the end stops 2.6 m short of it, on the battered
        # outer face: the shell end overlaps the wall foot up to ~6 m, and no shell station samples the wall walk (build-scene's
        # Ground305 would otherwise lift that column 5.5 m above the walk - an invisible wall on the rampart)
        dict(id='E305_J308', kind='joint308', realm='Hwanggyeong', zone='상경 가도', ea=True, points=[[1490.0, 2570.0], [1648.0, 2575.0]],
             block=-1, ends=(('keep',), ('wall', -2.6)), palette='Hwanggyeong',
             why='D308-3 seal: new joint (no lines305 run) from the north end of E305_034 to the capital west wall just north of its south-west '
                 'corner fillet (Spec E305_J308, ~160 m) -> N6 황경 숲. World reason: 도성 남서 성벽 밑까지 금표 숲을 이어 성저 서쪽 산길을 끊었다'),
    ],
}

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
    summary1 = dict(segments=len(sel), km=round(sum(s['length_m'] for s in sel) / 1000, 2),
                    ea_km=round(sum(s['length_m'] for s in sel if s['ea']) / 1000, 2),
                    by_zone={z: round(sum(s['length_m'] for s in sel if s['zone'] == z) / 1000, 2) for z in sorted({s['zone'] for s in sel})},
                    open_ends=sum(e == 'open' for s in sel for e in s['ends']))
    seal = seal308_segments(L, sel, SOLID) if OVERRIDES.get('_include') or OVERRIDES.get('_extra_runs') else []
    if not seal:
        doc = dict(version='305.1', source='lines305.json', phase=1,
                   rule='EA frontier only; kit N6 (dense forest + hidden shell); W1->N6; R, seams, gates, shortcuts out (D305)',
                   components=dict(ea=sorted(int(c) for c in ea_comp), ha=comp_ha, returned=sorted(returned)), summary=summary1, segments=sel)
    else:
        doc = dict(version='305.2', source='lines305.json', phase=1,
                   rule='EA frontier only; kit N6 (dense forest + hidden shell); W1->N6; R, seams, gates, shortcuts out (D305). '
                        '305.2 adds the D308-3 seal (Spec §1b′): 9 seams + 고산 E305_130 + joint E305_J308 as N6, the late-realm side blocked; '
                        'the Seal308 palisade (seal308.json) closes the 고개 gap between E305_084 and E305_056',
                   components=dict(ea=sorted(int(c) for c in ea_comp), ha=comp_ha, returned=sorted(returned)),
                   summary=summary1,
                   summary_seal308=dict(segments=len(seal), km=round(sum(s['length_m'] for s in seal) / 1000, 2), ids=[s['id'] for s in seal],
                                        open_ends=sum(e == 'open' for s in seal for e in s['ends']),
                                        note='phase-1 entries are byte-identical to 305.1 (their ends too: E305_128c001 / E305_133c020 '
                                             'are now closed by E305_130 joints - check-scene open-end probes there test it)'),
                   summary_all=dict(segments=len(sel) + len(seal), km=round(sum(s['length_m'] for s in sel + seal) / 1000, 2)),
                   segments=sel + seal)
    (ENC / 'segments305.json').write_text(json.dumps(doc, ensure_ascii=False, indent=1), encoding='utf-8')
    print(json.dumps(doc['summary'], ensure_ascii=False))
    if seal: print(json.dumps(doc['summary_seal308'], ensure_ascii=False))


# ------------------------------------------------------------------ #308 seal runs (D308-3, Spec §1b′)
def _arclen(P):
    P = np.asarray(P, float); return np.concatenate([[0], np.cumsum(np.hypot(*np.diff(P, axis=0).T))])


def _trim(P, a, b):
    """Cut a metres off the start and b off the end (arc length)."""
    P = np.asarray(P, float); s = _arclen(P); lo, hi = a, s[-1] - b
    pt = lambda t: [float(np.interp(t, s, P[:, 0])), float(np.interp(t, s, P[:, 1]))]
    return [pt(lo)] + [P[k].tolist() for k in range(len(P)) if lo + 1e-6 < s[k] < hi - 1e-6] + [pt(hi)]


def _nearest(P, e):
    P = np.asarray(P, float); best = (1e18, None, -1)
    for k, (a, b) in enumerate(zip(P[:-1], P[1:])):
        d = b - a; L2 = float(d @ d) or 1e-12; t = float(np.clip(((e - a) @ d) / L2, 0, 1)); q = a + d * t; dd = float(np.hypot(*(e - q)))
        if dd < best[0]: best = (dd, q, k)
    return best


def _cross(a, b, c, d):
    r = b - a; s = d - c; den = r[0] * s[1] - r[1] * s[0]
    if abs(den) < 1e-12: return None
    w = c - a; t = (w[0] * s[1] - w[1] * s[0]) / den; u = (w[0] * r[1] - w[1] * r[0]) / den
    return a + r * t if 0 <= t <= 1 and 0 <= u <= 1 else None


def _r2(P):
    return [[round(float(x), 2), round(float(z), 2)] for x, z in P]


def seal308_segments(L, sel, SOLID):
    """OVERRIDES['_include'] + ['_extra_runs'] -> N6 seal runs with explicit end treatment. Phase-1 entries (sel) are read only."""
    runs = {r['id']: r for r in L['runs']}
    seal = []
    for rid, o in OVERRIDES.get('_include', {}).items():
        r = runs[rid]; P = r['points']
        if o.get('trim'): P = _trim(P, *o['trim'])
        notes = [o['why'].format(kit=r['kit'], kit1=r['kit1'])]
        if o.get('trim'): notes.append(f"trimmed {o['trim'][0]:.1f} m at the start / {o['trim'][1]:.1f} m at the end of the lines305 run")
        if o.get('block') not in (1, -1): raise SystemExit(f'{rid}: OVERRIDES._include block must be +1 / -1 (got {o.get("block")})')
        seal.append(dict(id=rid, kit='N6', source_kit=r['kit1'], line_kit=r['kit'], kind=r['kind'], realm=o.get('palette') or r['realm'], line_realm=r['realm'],
                         zone=r['zone'], ea=r['ea'], closed=r['closed'], block=int(o['block']), points=_r2(P), length_m=0.0, components=[], params={},
                         notes=notes, seal308=True, group='seal308', _ends=o.get('ends', (('keep',), ('keep',)))))
    for x in OVERRIDES.get('_extra_runs', []):
        if x.get('block') not in (1, -1): raise SystemExit(f"{x['id']}: _extra_runs block must be +1 / -1")
        seal.append(dict(id=x['id'], kit='N6', source_kit='-', line_kit='-', kind=x.get('kind', 'extra'), realm=x.get('palette') or x['realm'], line_realm=x['realm'],
                         zone=x.get('zone', ''), ea=bool(x.get('ea', True)), closed=False, block=int(x['block']), points=_r2(x['points']), length_m=0.0,
                         components=[], params={}, notes=[x['why']], seal308=True, group='seal308', _ends=x.get('ends', (('keep',), ('keep',)))))
    by = {s['id']: s for s in seal}; ph1 = {s['id']: s for s in sel}
    seal_doc = json.loads((ENC / 'seal308.json').read_text(encoding='utf-8')) if (ENC / 'seal308.json').exists() else None

    def end_of(s, k): return np.asarray(s['points'][0 if k == 0 else -1], float)

    def set_end(s, k, pts_new):
        P = s['points']
        s['points'] = _r2(pts_new[::-1] + P[1:]) if k == 0 else _r2(P[:-1] + pts_new)
    # 1. joints between two seal runs: cut the target where this end meets it, so both ends coincide (build-scene joint + plug)
    for s in seal:
        for k, spec in enumerate(s['_ends']):
            if spec[0] != 'joint': continue
            t = by[spec[1]]; e = end_of(s, k); d, q, i = _nearest(t['points'], e); T = np.asarray(t['points'], float)
            sq = _arclen(T)[i] + float(np.hypot(*(q - T[i]))); Lt = _arclen(T)[-1]
            if sq < Lt / 2: t['points'] = _r2([q] + T[i + 1:].tolist()); t['notes'].append(f"start cut {sq:.1f} m: joint with {s['id']} end {k}")
            else: t['points'] = _r2(T[:i + 1].tolist() + [q]); t['notes'].append(f"end cut {Lt - sq:.1f} m: joint with {s['id']} end {k}")
            set_end(s, k, [q.tolist()]); s['notes'].append(f"end {k}: moved {d:.1f} m onto {t['id']} (joint, both ends coincide)")
    # 2. T joints onto phase-1 runs (never edited): cut this run where it first crosses the target walking in from the end and carry it
    #    `over` metres past the crossing; else go to the nearest target point and past it
    for s in seal:
        for k, spec in enumerate(s['_ends']):
            if spec[0] != 't': continue
            tgt = np.asarray((ph1.get(spec[1]) or by[spec[1]])['points'], float); over = float(spec[2])
            P = np.asarray(s['points'], float); Q = P if k == 1 else P[::-1]; s_ = _arclen(Q); hit = None
            for i in range(len(Q) - 2, -1, -1):
                if s_[-1] - s_[i + 1] > 40: break
                for c, dd in zip(tgt[:-1], tgt[1:]):
                    X = _cross(Q[i], Q[i + 1], c, dd)
                    if X is not None: hit = (i, X); break
                if hit: break
            if hit:
                i, X = hit; dvec = (Q[i + 1] - Q[i]) / (np.hypot(*(Q[i + 1] - Q[i])) or 1); newQ = Q[:i + 1].tolist() + [X.tolist(), (X + dvec * over).tolist()]
                cut = s_[-1] - (s_[i] + float(np.hypot(*(X - Q[i]))))
                s['notes'].append(f"end {k}: T joint across {spec[1]} at ({X[0]:.1f}, {X[1]:.1f}), {cut:.1f} m beyond it cut, carried {over:.1f} m past its line")
            else:
                e = Q[-1]; d, q, _ = _nearest(tgt, e); dvec = (q - e) / (d or 1)
                newQ = Q.tolist() + [q.tolist(), (q + dvec * over).tolist()]
                s['notes'].append(f"end {k}: T joint {d:.1f} m to {spec[1]}, carried {over:.1f} m past its line")
            s['points'] = _r2(newQ if k == 1 else newQ[::-1])
    # 3. into an existing wall: extend along the end tangent to the first wall crossing (<= 40 m) and `over` metres into it
    walls = existing_walls()
    for s in seal:
        for k, spec in enumerate(s['_ends']):
            if spec[0] != 'wall': continue
            P = np.asarray(s['points'], float); Q = P if k == 1 else P[::-1]; e = Q[-1]; dvec = (Q[-1] - Q[-2]) / (np.hypot(*(Q[-1] - Q[-2])) or 1)
            best = None
            for W in walls:
                for c, dd in zip(W[:-1], W[1:]):
                    X = _cross(e - dvec * 5, e + dvec * 40, c, dd)
                    if X is not None and (best is None or np.hypot(*(X - e)) < np.hypot(*(best - e))): best = X
            if best is None: s['notes'].append(f'end {k}: NO wall within 40 m (left as is)'); continue
            newQ = Q[:-1].tolist() + [(best + dvec * float(spec[1])).tolist()]
            s['points'] = _r2(newQ if k == 1 else newQ[::-1])
            s['notes'].append(f"end {k}: carried to the existing wall line at ({best[0]:.1f}, {best[1]:.1f}) " +
                              (f"and {float(spec[1]):.1f} m past it" if float(spec[1]) >= 0 else f"stopping {-float(spec[1]):.1f} m short of it (battered outer face)"))
    # 4. palisade ends: must sit on the seal308.json anchor (the bastion hides the shell end)
    for s in seal:
        for k, spec in enumerate(s['_ends']):
            if spec[0] != 'palisade': continue
            if seal_doc is None: raise SystemExit('seal308.json missing (palisade anchors)')
            a = np.asarray(seal_doc['anchors'][spec[1]], float); e = end_of(s, k); d = float(np.hypot(*(a - e)))
            if d > 1.0: raise SystemExit(f"{s['id']} end {k} is {d:.2f} m from Seal308 anchor {spec[1]} {a.tolist()} (trim / anchors disagree)")
            set_end(s, k, [a.tolist()]); s['notes'].append(f"end {k}: Seal308 palisade anchor {spec[1]} (post bastion overlaps the shell end; seal308.json)")
    # ends: palisade = tie; coincident ends (<= 5 cm) = joint; else the phase-1 rule (12 m to another run, or solid ground)
    allruns = sel + seal
    for s in seal:
        s['length_m'] = round(float(_arclen(s['points'])[-1]), 1); ends = []
        for k in (0, 1):
            e = end_of(s, k); spec = s['_ends'][k]
            if spec[0] == 'palisade': ends.append('tie'); continue
            mates = [t['id'] for t in allruns if t is not s for kk in (0, 1) if np.hypot(*(end_of(t, kk) - e)) <= .05]
            if mates: s['notes'].append(f"end {k}: joint with {', '.join(mates)}"); ends.append('tie'); continue
            tie = any(np.min(np.hypot(np.asarray(t['points'])[:, 0] - e[0], np.asarray(t['points'])[:, 1] - e[1])) <= 12 for t in allruns if t is not s)
            if not tie:
                i0, j0 = int(np.clip(e[1] / C4, 0, H4 - 1)), int(np.clip(e[0] / C4, 0, W4 - 1)); tie = bool(SOLID[max(0, i0 - 1): i0 + 2, max(0, j0 - 1): j0 + 2].any())
            ends.append('tie' if tie else 'open')
        s['ends'] = ends; s['ends_spec'] = [list(map(str, e)) for e in s.pop('_ends')]
        # AC-S1 '막는 쪽' in words (block alone is a sign): side of the point order + mean compass of the blocked normal (x = E, z = N)
        P = np.asarray(s['points'], float); d = np.diff(P, axis=0)
        m = (np.stack([d[:, 1], -d[:, 0]], 1) * s['block']).sum(0); mm = .38 * float(np.hypot(*m))   # length-weighted blocked normal
        comp = ('N' if m[1] > mm else 'S' if m[1] < -mm else '') + ('E' if m[0] > mm else 'W' if m[0] < -mm else '')
        s['blocked_side'] = 'right' if s['block'] > 0 else 'left'
        s['notes'].append(f"blocked side: {s['blocked_side']} of the point order, facing {comp or '-'} on average = the late side "
                          f"(new-game closed flood); player side = EA")
    return seal


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
    # #308: keep the seal runs' forest off the Seal308 palisade (rows, rails, spear racks and end bastions: 2.5 m around the A-B
    # line, bastion ring + 2.5 m around A / B). Phase-1 candidates are never filtered here (305.1 output stays identical).
    palis = None; cur = dict(seal=False)
    if any(s.get('seal308') for s in S['segments']) and (ENC / 'seal308.json').exists():
        sd = json.loads((ENC / 'seal308.json').read_text(encoding='utf-8'))
        palis = (np.asarray(sd['anchors']['A'], float), np.asarray(sd['anchors']['B'], float), 2.5 + float(sd['bastion']['post_ring_radius_m']))
        stats['dropped_palisade'] = 0; stats['dropped_wall'] = 0
    walls = existing_walls() if palis is not None else []

    def off_palisade(x, z):
        if palis is None or not cur['seal']: return True
        if walls and nearest_on(walls, np.array([x, z]))[0] < 4.0: stats['dropped_wall'] += 1; return False   # J308 ends in the capital wall
        a, b, r = palis; d = b - a; t = float(np.clip(((np.array([x, z]) - a) @ d) / float(d @ d), 0, 1)); q = a + d * t
        near_line = float(np.hypot(x - q[0], z - q[1])) < 2.5; near_end = min(np.hypot(x - a[0], z - a[1]), np.hypot(x - b[0], z - b[1])) < r
        if near_line or near_end: stats['dropped_palisade'] += 1; return False
        return True

    def ok(x, z):
        i4, j4 = int(np.clip(z / C4, 0, H4 - 1)), int(np.clip(x / C4, 0, W4 - 1))
        if not off_palisade(x, z): return False
        if d_route[int(np.clip(z // 10, 0, 599)), int(np.clip(x // 10, 0, 399))] < 12: stats['dropped_route'] += 1; return False
        if wl[i4, j4] > -1000 and wl[i4, j4] - h[i4, j4] > 0.3: stats['dropped_water'] += 1; return False
        if prot[i4, j4]: stats['dropped_prot'] += 1; return False
        if slope[i4, j4] > 40: stats['dropped_slope'] += 1; return False
        return True

    for s in S['segments']:
        cur['seal'] = bool(s.get('seal308'))
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
    doc = dict(version=S.get('version', '305.1'), band_m=band, stats=stats, prototypes=sorted({c['PrototypeId'] for c in cands}), candidates=cands)
    (OUT / 'forest305_candidates.json').write_text(json.dumps(doc, ensure_ascii=False), encoding='utf-8')
    print(json.dumps(dict(stats=stats, prototypes=doc['prototypes']), ensure_ascii=False))


def verify_phase1():
    """#308 guard (Spec AC-S12 '1단계 49구간의 입력 변경 0'): the phase-1 entries of segments305.json and their forest candidates
    must equal the 305.1 backups in Enclosure305/Seal308/backup/."""
    bk = ENC / 'Seal308/backup'
    cur = json.loads((ENC / 'segments305.json').read_text(encoding='utf-8')); old = json.loads((bk / 'segments305.v305_1.json').read_text(encoding='utf-8'))
    p1 = [s for s in cur['segments'] if not s.get('seal308')]
    seg_ok = p1 == old['segments'] and cur.get('summary') == old.get('summary') and cur.get('components') == old.get('components')
    fc = json.loads((OUT / 'forest305_candidates.json').read_text(encoding='utf-8')); fo = json.loads((bk / 'forest305_candidates.v305_1.json').read_text(encoding='utf-8'))
    ids1 = {s['id'] for s in old['segments']}
    c1 = [c for c in fc['candidates'] if c['ClusterId'] in ids1]
    cand_ok = c1 == fo['candidates']
    print(json.dumps(dict(phase1_segments_identical=seg_ok, phase1_segments=len(p1), seal308_segments=len(cur['segments']) - len(p1),
                          phase1_candidates_identical=cand_ok, phase1_candidates=len(c1), all_candidates=len(fc['candidates'])), ensure_ascii=False))
    return seg_ok and cand_ok


def _guard_cliff308():
    """#308 cliff stage 1a: from 305.3 on segments305.json / forest305_candidates.json are DERIVED files (Tools/Art/
    enclosure305_cliff308.py: 305.2 frozen copies + cliff ops + stage height + cliff mask). This driver would write 305.2 over
    them on the base height. Refuse instead; `python Tools/Art/enclosure305_cliff308.py revert` puts 305.2 back first."""
    p = ENC / 'segments305.json'
    if not p.exists(): return
    d = json.loads(p.read_text(encoding='utf-8'))
    if 'cliff308' in d or str(d.get('version', '')) not in ('305.1', '305.2'):
        raise SystemExit(f"REFUSED: {p.name} is version {d.get('version')} (derived by Tools/Art/enclosure305_cliff308.py). This tool writes 305.2 on the base height "
                         f"and would overwrite it. Run `python Tools/Art/enclosure305_cliff308.py revert` first if that is what you want; nothing changed")


if __name__ == '__main__':
    cmd = sys.argv[1] if len(sys.argv) > 1 else 'all'
    _guard_cliff308()
    if cmd in ('segments', 'all'): segments()
    if cmd in ('forest', 'all'): forest()
    if cmd in ('verify-phase1', 'all'): sys.exit(0 if verify_phase1() else 1)
