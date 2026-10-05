# -*- coding: utf-8 -*-
"""SPEC-MAP-OVERHAUL-308 section 1 - ridge lines (sanjulgi) from the 4 m height lattice. Helper of cartography308.py.

The extraction is the direction sheet's (map308_mock.py, the look the user was shown), kept here as the ONE copy:

    crest        smoothed height (three box passes); across the direction of the strongest negative curvature a cell is a
                 crest when it is not lower than the ground CREST_REACH_M to both sides
    hysteresis   strong crest = drops >= STRONG_DROP at DROP_REACH_M on both sides and stands STRONG_TPI above the 100 m
                 mean; weak crest = WEAK_DROP / WEAK_TPI. Weak crests survive where they touch a strong one (saddles and
                 the low shoulders of a range are bridged) - this is what the first bake lacked
    skeleton     Zhang-Suen thinning -> one-cell lines; short spurs and knots pruned
    chains       at every junction the two most collinear branches are joined into one stroke, so a range runs through its
                 branch points unbroken (the "continuous dark range" of the old maps)
    rank         the sheet's rule: prominence of a PATH (skeleton edge between two junctions) = its mean height above the
                 100 m mean; paths sorted by it, by length: top 15 % = 1, next 35 % = 2, rest 3 - a range keeps one weight
                 between its branch points. The joined stroke is split where the rank changes. A rank 3 piece of a ridge shorter
                 than SHORT_M is rank 4: same drawing, hidden one zoom band earlier (the sheet's rule for the region band)
    gaps         a ridge stops ROAD_GAP_M before a road (the pass of the old maps: the road goes through the break) and
                 CLIFF_GAP_M before a built cliff line (the cliff draws its own crest)
    teeth        towards the steeper side (mean slope TEETH_PROBE m across), one side per piece

Deterministic: no RNG; every traversal runs in sorted pixel order. Temporary exception of the Spec: 4 m lattice extraction,
thresholds are TEST values tuned on the concept sheets.
"""
import math

import numpy as np

import map308_lib as M

P = dict(GAUSS_R=2, CREST_REACH_M=5.0, DROP_REACH_M=24.0, STRONG_DROP=1.5, WEAK_DROP=0.5, TPI_R=25, STRONG_TPI=4.0, WEAK_TPI=1.5, GROW=40,
         SPUR_M=90.0, MIN_M=90.0, LOOP_M=260.0, JOIN_DOT=-0.45, STEP_M=12.0, RANK1_SHARE=.15, RANK2_SHARE=.35, RANK_WIN=9,
         RANK_MIN_RUN_M=160.0, SHORT_M=220.0, TEETH_PROBE=28.0, ROAD_GAP_M=11.0, CLIFF_GAP_M=30.0, EDGE_CELLS=3, MIN_PIECE_M=30.0)


def gauss(a, r):
    for _ in range(3): a = M.box(a, r)
    return a


def _grow8(m):
    """8-neighbour dilation (no wrap)."""
    p = np.pad(m, 1)
    out = m.copy()
    for di in (0, 1, 2):
        for dj in (0, 1, 2):
            if di == 1 and dj == 1: continue
            out |= p[di:di + m.shape[0], dj:dj + m.shape[1]]
    return out


def crest_mask(h, wet):
    """-> (band bool, hs smoothed height, tpi, counts). The sheet's rule: principal-curvature crests with hysteresis."""
    hs = gauss(h, P['GAUSS_R'])
    gz, gx = np.gradient(hs, M.CELL)
    hxx = np.gradient(gx, M.CELL, axis=1); hzz = np.gradient(gz, M.CELL, axis=0); hxz = np.gradient(gx, M.CELL, axis=0)
    ang = 0.5 * np.arctan2(2 * hxz, hxx - hzz) + math.pi / 2
    vx, vz = np.cos(ang), np.sin(ang)
    X, Z = np.meshgrid(np.arange(M.WW) * M.CELL, np.arange(M.HH) * M.CELL)
    at = lambda d, s: M.sample_grid(hs, X + s * vx * d, Z + s * vz * d)
    c = P['CREST_REACH_M']; d = P['DROP_REACH_M']
    crest = (hs >= at(c, 1)) & (hs >= at(c, -1))
    drop = np.minimum(hs - at(d, 1), hs - at(d, -1))
    tpi = hs - M.box(hs, P['TPI_R'])
    strong = crest & (drop >= P['STRONG_DROP']) & (tpi > P['STRONG_TPI']) & ~wet
    weak = crest & (drop >= P['WEAK_DROP']) & (tpi > P['WEAK_TPI']) & ~wet
    cur = strong.copy()
    for _ in range(P['GROW']):
        n = (_grow8(cur) & weak) | cur
        if (n == cur).all(): break
        cur = n
    e = P['EDGE_CELLS']; cur[:e] = cur[-e:] = False; cur[:, :e] = cur[:, -e:] = False
    return cur, hs, tpi, dict(strong=int(strong.sum()), weak=int(weak.sum()))


def thin(img):
    """Zhang-Suen thinning (bool array, border must be False)."""
    img = img.copy()
    while True:
        changed = False
        for step in (0, 1):
            p = np.pad(img, 1)
            P2 = p[:-2, 1:-1]; P3 = p[:-2, 2:]; P4 = p[1:-1, 2:]; P5 = p[2:, 2:]; P6 = p[2:, 1:-1]; P7 = p[2:, :-2]; P8 = p[1:-1, :-2]; P9 = p[:-2, :-2]
            seq = (P2, P3, P4, P5, P6, P7, P8, P9, P2)
            B = sum(s.astype(np.int8) for s in seq[:8])
            Atr = sum((~seq[k] & seq[k + 1]).astype(np.int8) for k in range(8))
            c = img & (B >= 2) & (B <= 6) & (Atr == 1)
            if step == 0: c &= ~(P2 & P4 & P6) & ~(P4 & P6 & P8)
            else: c &= ~(P2 & P4 & P8) & ~(P2 & P6 & P8)
            if c.any():
                img[c] = False; changed = True
        if not changed: return img


_ORTH = ((-1, 0), (1, 0), (0, -1), (0, 1))
_DIAG = ((-1, -1), (-1, 1), (1, -1), (1, 1))


def graph(skel):
    """pixel graph: {pixel (i, j): sorted neighbour list}; a diagonal link is kept only when neither orthogonal detour exists."""
    pts = sorted(map(tuple, np.argwhere(skel)))
    S = set(pts); adj = {}
    for (i, j) in pts:
        n = [(i + di, j + dj) for di, dj in _ORTH if (i + di, j + dj) in S]
        for di, dj in _DIAG:
            q = (i + di, j + dj)
            if q in S and (i + di, j) not in S and (i, j + dj) not in S: n.append(q)
        adj[(i, j)] = sorted(n)
    return adj


def trace(adj):
    """edges between nodes (degree != 2): [pixel path]. Pure cycles become one closed path."""
    nodes = {p for p, n in adj.items() if len(n) != 2}
    seen = set(); edges = []
    for a in sorted(nodes):
        for b in adj[a]:
            if (a, b) in seen: continue
            path = [a, b]; seen.add((a, b)); prev, cur = a, b
            while cur not in nodes:
                nx = [q for q in adj[cur] if q != prev]
                if not nx: break
                prev, cur = cur, nx[0]; path.append(cur)
            seen.add((path[-1], path[-2]))
            edges.append(path)
    used = {p for e in edges for p in e}
    for a in sorted(adj):
        if a in used or a in nodes: continue
        path = [a]; prev, cur = None, a
        while True:
            nx = [q for q in adj[cur] if q != prev and q not in path[1:]]
            used.add(cur)
            if not nx or nx[0] == a: break
            prev, cur = cur, nx[0]; path.append(cur)
        if len(path) > 3: edges.append(path)
    return edges, nodes


def path_m(path):
    a = np.asarray(path, float)
    return float(np.hypot(*np.diff(a, axis=0).T).sum() * M.CELL) if len(a) > 1 else 0.0


def prune(skel):
    """remove spurs (an edge with a free end, shorter than SPUR_M) and isolated short pieces; up to three passes."""
    for _ in range(3):
        adj = graph(skel); edges, nodes = trace(adj); removed = 0
        for e in edges:
            a, b = e[0], e[-1]; da, db = len(adj[a]), len(adj[b]); L = path_m(e)
            free = (da == 1) + (db == 1)
            if a == b and L < P['LOOP_M']:                      # a small ring hanging on a junction (or standing alone)
                for p in e[1:-1]: skel[p] = False
                removed += 1
            elif free == 2 and L < P['MIN_M']:
                for p in e: skel[p] = False
                removed += 1
            elif free == 1 and L < P['SPUR_M']:
                for p in (e[1:] if da >= 3 else e[:-1] if db >= 3 else e): skel[p] = False
                removed += 1
        skel = thin(skel)
        if not removed: break
    return skel


def chains(skel):
    """join edges at junctions (most collinear pair) -> list of pixel chains."""
    adj = graph(skel); edges, nodes = trace(adj)
    ends = {}                                         # node -> [(edge index, end 0|1)]
    for k, e in enumerate(edges):
        if len(e) < 2: continue
        ends.setdefault(e[0], []).append((k, 0)); ends.setdefault(e[-1], []).append((k, 1))

    def direction(k, end):
        e = edges[k]; n = min(6, len(e) - 1)
        a = np.asarray(e[0] if end == 0 else e[-1], float); b = np.asarray(e[n] if end == 0 else e[-1 - n], float)
        v = b - a; L = math.hypot(*v) or 1.0
        return v / L
    link = {}
    for node in sorted(ends):
        inc = [x for x in ends[node] if x not in link]
        while len(inc) >= 2:
            best = None
            for a in range(len(inc)):
                for b in range(a + 1, len(inc)):
                    if inc[a][0] == inc[b][0]: continue
                    d = float(direction(*inc[a]) @ direction(*inc[b]))
                    if best is None or d < best[0]: best = (d, a, b)
            if best is None or best[0] > P['JOIN_DOT']: break
            _, a, b = best
            link[inc[a]] = inc[b]; link[inc[b]] = inc[a]
            inc = [x for i, x in enumerate(inc) if i not in (a, b)]
    done = set(); out = []
    for k in range(len(edges)):
        if k in done or len(edges[k]) < 2: continue
        # walk to one end of the chain
        cur, end = k, 0; guard = 0
        while (cur, end) in link and guard < len(edges) + 2:
            nk, nend = link[(cur, end)]; cur, end = nk, 1 - nend; guard += 1
            if cur == k: break
        chain = []
        # now traverse from (cur, end) outward through the other end
        while True:
            e = edges[cur] if end == 0 else edges[cur][::-1]
            done.add(cur)
            chain.extend(e if not chain else e[1:])
            other = (cur, 1 - end)
            if other not in link: break
            nk, nend = link[other]
            if nk in done: break
            cur, end = nk, nend
        out.append(chain)
    return out


def extract(h, wet, road_mask, log=print, cut_mask=None):
    """-> list of dict(pts Nx2 world metres, rank 1..4, teeth_left bool, prom float, cap_start, cap_end). Deterministic order."""
    band, hs, tpi, band_counts = crest_mask(h, wet)
    skel = prune(thin(band))
    ch = chains(skel)
    # prominence per skeleton path (edge), written on its pixels; a junction pixel takes the most prominent of its paths
    edges, _ = trace(graph(skel))
    ep = np.full(skel.shape, -np.inf)
    for e in edges:
        ii = np.array([q[0] for q in e]); jj = np.array([q[1] for q in e])
        ep[ii, jj] = np.maximum(ep[ii, jj], float(tpi[ii, jj].mean()))
    for _ in range(2):                                                    # the smoothed line may sit a cell off its pixels
        pad = np.pad(ep, 1, constant_values=-np.inf)
        nb = np.max([pad[di:di + ep.shape[0], dj:dj + ep.shape[1]] for di in range(3) for dj in range(3)], axis=0)
        ep = np.where(np.isfinite(ep), ep, nb)
    gz, gx = np.gradient(hs, M.CELL)
    slope = np.degrees(np.arctan(np.hypot(gx, gz)))
    lines = []
    for c in ch:
        a = np.asarray(c, float)
        pts = np.stack([a[:, 1] * M.CELL, a[:, 0] * M.CELL], 1)
        if M.poly_len(pts) < P['MIN_M']: continue
        if math.hypot(*(pts[0] - pts[-1])) < 3 * M.CELL and M.poly_len(pts) < P['LOOP_M']: continue      # a closed knot is not a range
        pts = M.resample(M.smooth_poly(M.smooth_poly(pts, 5), 5), P['STEP_M'])
        lines.append(pts)
    # rank thresholds by length (the points are evenly spaced, so point quantiles are length quantiles)
    def along(pts):
        v = ep[np.clip(np.round(pts[:, 1] / M.CELL).astype(int), 0, M.HH - 1), np.clip(np.round(pts[:, 0] / M.CELL).astype(int), 0, M.WW - 1)]
        return np.where(np.isfinite(v), v, M.sample_grid(tpi, pts[:, 0], pts[:, 1]))
    proms = [along(pts) for pts in lines]
    prom = np.concatenate(proms) if proms else np.zeros(0)
    q1 = float(np.quantile(prom, 1 - P['RANK1_SHARE'])) if len(prom) else 0.0
    q2 = float(np.quantile(prom, 1 - P['RANK1_SHARE'] - P['RANK2_SHARE'])) if len(prom) else 0.0
    near_road = M.near_dist(road_mask, int(math.ceil(P['ROAD_GAP_M'] / M.CELL)) + 1) * M.CELL
    near_cut = M.near_dist(cut_mask, int(math.ceil(P['CLIFF_GAP_M'] / M.CELL)) + 1) * M.CELL if cut_mask is not None and cut_mask.any() else None
    out = []; whole_m = 0.0
    for pts, pr in zip(lines, proms):
        whole = M.poly_len(pts); whole_m += whole
        rk = np.where(pr >= q1, 1, np.where(pr >= q2, 2, 3))
        rk = _mode_filter(rk, P['RANK_WIN'])
        rk = _merge_short_runs(rk, pts, P['RANK_MIN_RUN_M'])
        gap = M.sample_grid(near_road, pts[:, 0], pts[:, 1]) <= P['ROAD_GAP_M']
        if near_cut is not None: gap |= M.sample_grid(near_cut, pts[:, 0], pts[:, 1]) <= P['CLIFF_GAP_M']    # a built cliff line draws its own crest
        # split into runs of one rank, cut at road gaps; neighbouring rank runs share their boundary point
        i = 0; n = len(pts); last_end = -1
        while i < n:
            if gap[i]: i += 1; continue
            j = i
            while j + 1 < n and not gap[j + 1] and rk[j + 1] == rk[i]: j += 1
            e = j + 1 if (j + 1 < n and not gap[j + 1]) else j        # share the boundary point with the next rank run
            seg = pts[i:e + 1]
            if len(seg) >= 2 and M.poly_len(seg) >= P['MIN_PIECE_M']:
                if last_end == i and out: out[-1]['cap_end'] = False  # the range runs on at another rank: no brush cap between
                join = last_end == i; last_end = e
                nr = M.normals(seg)
                sl = M.sample_grid(slope, seg[:, 0] + nr[:, 0] * P['TEETH_PROBE'], seg[:, 1] + nr[:, 1] * P['TEETH_PROBE']).mean()
                sr = M.sample_grid(slope, seg[:, 0] - nr[:, 0] * P['TEETH_PROBE'], seg[:, 1] - nr[:, 1] * P['TEETH_PROBE']).mean()
                rank = int(rk[i])
                if rank == 3 and whole < P['SHORT_M']: rank = 4
                out.append(dict(pts=seg, rank=rank, teeth_left=bool(sl >= sr), prom=float(pr[i:e + 1].mean()), cap_start=not join, cap_end=True))
            i = j + 1
    out.sort(key=lambda s: (s['rank'], round(float(s['pts'][0, 1]), 2), round(float(s['pts'][0, 0]), 2), len(s['pts'])))
    km = lambda r: round(sum(M.poly_len(s['pts']) for s in out if s['rank'] == r) / 1000, 2)
    stats = dict(method='principal-curvature crest + hysteresis (the direction sheet rule)', strong_cells=band_counts['strong'], weak_cells=band_counts['weak'],
                 band_cells=int(band.sum()), skeleton_cells=int(skel.sum()), chains=len(ch), lines=len(lines), km_before_gaps=round(whole_m / 1000, 2),
                 strokes=len(out), km_by_rank={'1': km(1), '2': km(2), '3': km(3), '4': km(4)}, km=round(sum(km(r) for r in (1, 2, 3, 4)), 2),
                 prominence_thresholds_m=[round(q1, 2), round(q2, 2)], short_rank3_below_m=P['SHORT_M'])
    log(f'  ridges: {stats}')
    return out, stats


def _mode_filter(rk, win):
    n = len(rk); k = win // 2; out = rk.copy()
    for i in range(n):
        w = rk[max(0, i - k):min(n, i + k + 1)]
        c = np.bincount(w, minlength=4)
        out[i] = int(np.argmax(c[1:]) + 1)
    return out


def _merge_short_runs(rk, pts, min_m):
    rk = rk.copy(); s = M.cum_len(pts)
    for _ in range(4):
        runs = []; i = 0
        while i < len(rk):
            j = i
            while j + 1 < len(rk) and rk[j + 1] == rk[i]: j += 1
            runs.append((i, j)); i = j + 1
        if len(runs) < 2: break
        changed = False
        for k, (a, b) in enumerate(runs):
            if s[b] - s[a] < min_m:
                nb = [rk[runs[k - 1][0]]] if k > 0 else []
                if k + 1 < len(runs): nb.append(rk[runs[k + 1][0]])
                rk[a:b + 1] = min(nb, key=lambda v: abs(int(v) - int(rk[a])))
                changed = True
        if not changed: break
    return rk
