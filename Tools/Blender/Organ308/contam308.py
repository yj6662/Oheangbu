# #308 D308-4d — 마석 결정 둘레 마석 오염: per-species body-UV0 R8 mask (plain Python 3 + numpy + Pillow, no Blender).
#   python Tools/Blender/Organ308/contam308.py bake [species...]      (after shard308e.py prep <species> wrote Analysis/Contam/<s>_body.npz)
# Per species:
#   1. geodesic distance on the bind-pose skin from the crystal pivot: Dijkstra on the position-welded vertex graph (triangle edges + the
#      diagonal across every shared edge, metres) — the contamination follows the surface, it never jumps across a gap to a nearby limb
#      (a euclidean sphere would; reported as sphereBleedVertices).
#   2. UV0 raster at CONTAM_RES (texel centres, barycentric): every texel of a triangle near the crystal gets its 3D point and its
#      interpolated geodesic distance t = g / radius. UV overlap check: texels shared by two triangles whose t differ by > .25 while one
#      of them is inside the radius = conflict; conflict > 3 % of the region -> Sphere fallback (world distance in the shader), reported.
#      Under the threshold, a conflict texel whose other surface lies OUTSIDE the radius (t_max >= 1) is dropped from the mask (value 0,
#      not refilled by the dilation): one texel cannot be vein here and clean skin there, and a vein painted on a far surface (#308 retry
#      audit: fox tail-tip texels shared with a patch ~1.9 m away, dokkaebi chest texels shared with a fold just past the radius) is
#      exactly the bleed D308-4d forbids. The near side loses those few texels (reads as one more break in the veins).
#   3. vein / crack pattern on the 3D point (seam-free: a function of the surface point and t, not of UV): solid mineral crust next to
#      the crystal, a crack network (Worley F2-F1, domain-warped) whose lines thin outward, radial sectors that drop out with distance
#      (veins reach out in fingers), noise-broken outer edge, cut at t = 1.
#   4. encoding (R8 linear): value = coverage x (1 - .96 t)  ->  0 = clean skin; the shader reads presence = smoothstep(.02, .06, v) and
#      the geodesic closeness v (t ~ 1 - v) for the time-progressive tint flow along the veins (InkOrganSurface _ContamTex). Dilated 4 px
#      into empty UV space (no seam bleed of zeros under bilinear / mips). PNG row 0 = UV v 1 (Unity / FBX UV origin bottom-left).
#   5. review approximation textures (NOT the shader): base colour -> ink-wash approximation -> contamination darkening (idle) and the
#      element tint flowing to 60 % of the radius (telegraph window) — Analysis/Contam/<s>_approx_{idle,tele}.png for shard308e.py render.
#   6. `mipbleed` (read-only): the body UV0 is an atlas of many small islands, so a coarse mip of the mask bleeds a contaminated island into
#      its UV neighbours (clean skin elsewhere). Estimates the share of clean texels that would read contamination per mip level ->
#      Analysis/Contam/contam308_mipbleed.json; the shader clamps the mip level it samples (_ContamMaxLod, SO ContaminationMaxMip 2).
# Outputs: _ProjectAssets/.../Organs308/Contam308/T_Organ308_Contam_<s>.png, Analysis/Contam/<s>_bake.json. Source files are read-only.
import heapq
import json
import math
import sys
from pathlib import Path

import numpy as np
from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parent))
import shard308e_data as D  # noqa: E402

# encoding / look constants shared with the shader (InkOrganSurface.shader contamination block) and the SO (EnemyTelegraphTimingSO)
CLOSE_SCALE = 0.96
PRESENCE_LO, PRESENCE_HI = 0.02, 0.06
DARK_NEAR, DARK_FAR, INK_MIX = 0.72, 0.30, 0.35       # EnemyTelegraphTimingSO ContaminationDarkNear / Far / InkMix (TEST)
FLOW_SOFT, TINT_ALPHA, BRIGHT = 0.15, 0.75, 0.85
BRIDGE_GAP_M = 0.025
FLOW_DEMO = 0.55              # review: the tint front at ~ mid window (t_on .. t_peak)
FOLKLORE_TEX = {
    "dokkaebi": "dokkaebi/dokkaebi_T_0_base_color.png", "agwi": "agwi/agwi_T_0_base_color.png",
    "changgui": "changgui/changgui_T_0_base_color.png", "bulgasari": "bulgasari/bulgasari-v2_T_0_base_color.png",
    "fox_spirit": "fox_spirit/fox_spirit-v2_T_0_base_color.png", "imugi": "imugi/imugi_T_0_base_color.png"}


# ---------------------------------------------------------------- noise (deterministic, vectorised)
def _hash(ix, iy, iz, seed):
    h = (ix * 73856093) ^ (iy * 19349663) ^ (iz * 83492791) ^ (seed * 2654435761)
    h = h & 0xFFFFFFFF
    h = h ^ (h >> 13)
    h = (h * 1274126177) & 0xFFFFFFFF
    h = h ^ (h >> 16)
    return h.astype(np.float64) / 4294967295.0


def vnoise(q, seed=0):
    f = np.floor(q)
    i = f.astype(np.int64)
    w = q - f
    w = w * w * (3.0 - 2.0 * w)
    out = 0.0
    for dx in (0, 1):
        for dy in (0, 1):
            for dz in (0, 1):
                h = _hash(i[:, 0] + dx, i[:, 1] + dy, i[:, 2] + dz, seed)
                wx = w[:, 0] if dx else 1 - w[:, 0]
                wy = w[:, 1] if dy else 1 - w[:, 1]
                wz = w[:, 2] if dz else 1 - w[:, 2]
                out = out + h * wx * wy * wz
    return out


def fbm(q, seed=0, octaves=3):
    s, a, tot = 0.0, 1.0, 0.0
    for o in range(octaves):
        s = s + a * vnoise(q * (2.0 ** o), seed + 17 * o)
        tot += a
        a *= 0.5
    return s / tot


def worley(q, seed=0):
    """F1, F2 (cell units) of a 3D jittered-grid point set."""
    f = np.floor(q).astype(np.int64)
    best1 = np.full(len(q), 1e9)
    best2 = np.full(len(q), 1e9)
    for dx in (-1, 0, 1):
        for dy in (-1, 0, 1):
            for dz in (-1, 0, 1):
                cx, cy, cz = f[:, 0] + dx, f[:, 1] + dy, f[:, 2] + dz
                fp = np.stack([cx + _hash(cx, cy, cz, seed), cy + _hash(cx, cy, cz, seed + 1), cz + _hash(cx, cy, cz, seed + 2)], 1)
                d = np.linalg.norm(q - fp, axis=1)
                nb2 = np.where(d < best1, best1, np.minimum(best2, d))
                best1 = np.minimum(best1, d)
                best2 = nb2
    return best1, best2


def smooth(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def pattern(p, t, texel, R):
    """coverage 0..1 of mineral veins at surface points p (metres, relative to the crystal pivot), t = geodesic / radius."""
    cell = 0.11 * R
    o1 = np.array([3.1, 7.7, 1.3])
    warp = np.stack([vnoise(p / (0.9 * cell) + o1 * k, 11 + k) - 0.5 for k in range(3)], 1) * (0.55 * cell)
    q = (p + warp) / cell
    tn = t + 0.12 * (fbm(p / (0.45 * R), 5) - 0.5)
    f1, f2 = worley(q, 23)
    crack = f2 - f1                                                   # 0 on the cell borders
    aa = np.maximum(texel / cell, 0.02)
    w = 0.20 + (0.035 - 0.20) * np.clip(tn, 0, 1) ** 0.7              # line half-width in cell units: wide near, thin far
    vein = np.clip((w - crack) / aa + 0.5, 0.0, 1.0)
    g1, g2 = worley(q * 2.4 + 5.1, 41)                                 # fine cracks near the crystal only
    w2 = 0.13 * (1.0 - smooth(0.10, 0.45, tn))
    fine = np.clip((w2 - (g2 - g1)) / (aa * 2.4) + 0.5, 0.0, 1.0)
    r = np.linalg.norm(p, axis=1, keepdims=True)
    rhat = p / np.maximum(r, 1e-6)
    sector = vnoise(rhat * 5.5 + p / (0.8 * R), 59)                    # radial fingers: constant along rays from the crystal
    lo = 0.55 * np.clip(tn, 0, 1)
    keep_s = smooth(lo, lo + 0.15, sector)
    brk = fbm(p / (0.30 * R), 71)
    keep_b = smooth(0.70 * tn - 0.08, 0.70 * tn + 0.08, brk)           # noise-broken: more breaks outward
    crust = 1.0 - smooth(0.06, 0.16, tn)
    cov = np.maximum(crust, np.maximum(vein, fine) * keep_s * keep_b)
    cov = cov * (1.0 - smooth(0.86, 1.0, tn))
    return np.clip(cov, 0.0, 1.0)


# ---------------------------------------------------------------- geometry
def weld(P, T):
    key = np.round(P * 1e5).astype(np.int64)
    _, inv = np.unique(key, axis=0, return_inverse=True)
    inv = inv.reshape(-1)
    return inv, inv[T]


def graph(Pw, Tw):
    """adjacency: triangle edges + the diagonal across every interior edge (shortens the edge-graph zigzag)."""
    opp = {}
    for a, b, c in Tw:
        for u, v, o in ((a, b, c), (b, c, a), (c, a, b)):
            if u == v:
                continue
            k = (u, v) if u < v else (v, u)
            opp.setdefault(k, []).append(o)
    adj = [dict() for _ in range(len(Pw))]

    def add(u, v):
        if u == v:
            return
        d = float(np.linalg.norm(Pw[u] - Pw[v]))
        if v not in adj[u] or d < adj[u][v]:
            adj[u][v] = d
            adj[v][u] = d
    for (u, v), os_ in opp.items():
        add(u, v)
        if len(os_) == 2:
            add(os_[0], os_[1])
    return adj, opp


def components(n, adj):
    comp = np.full(n, -1)
    c = 0
    for s in range(n):
        if comp[s] >= 0:
            continue
        stack = [s]
        comp[s] = c
        while stack:
            u = stack.pop()
            for v in adj[u]:
                if comp[v] < 0:
                    comp[v] = c
                    stack.append(v)
        c += 1
    return comp, c


def closest_on_tri(p, a, b, c):
    """closest point on triangle abc to p (Ericson)."""
    ab, ac, ap = b - a, c - a, p - a
    d1, d2 = ab @ ap, ac @ ap
    if d1 <= 0 and d2 <= 0:
        return a
    bp = p - b
    d3, d4 = ab @ bp, ac @ bp
    if d3 >= 0 and d4 <= d3:
        return b
    vc = d1 * d4 - d3 * d2
    if vc <= 0 and d1 >= 0 and d3 <= 0:
        return a + ab * (d1 / (d1 - d3))
    cp = p - c
    d5, d6 = ab @ cp, ac @ cp
    if d6 >= 0 and d5 <= d6:
        return c
    vb = d5 * d2 - d1 * d6
    if vb <= 0 and d2 >= 0 and d6 <= 0:
        return a + ac * (d2 / (d2 - d6))
    va = d3 * d6 - d5 * d4
    if va <= 0 and (d4 - d3) >= 0 and (d5 - d6) >= 0:
        return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)))
    den = 1.0 / (va + vb + vc)
    return a + ab * (vb * den) + ac * (vc * den)


def dijkstra(adj, seeds):
    n = len(adj)
    dist = np.full(n, np.inf)
    h = []
    for v, d in seeds:
        if d < dist[v]:
            dist[v] = d
            heapq.heappush(h, (d, v))
    while h:
        d, u = heapq.heappop(h)
        if d > dist[u]:
            continue
        for v, w in adj[u].items():
            nd = d + w
            if nd < dist[v]:
                dist[v] = nd
                heapq.heappush(h, (nd, v))
    return dist


def bridge(Pw, comp, adj, centre, reach, gap, penalty=2.0, toll=0.003):
    """Meshy bodies are several shells (cloth pieces, belts, ornaments lying on the skin). Let the contamination cross between DIFFERENT
    shells where they touch (vertex gap < `gap`), at a cost (gap x penalty + toll). Never inside one shell: a fold of the same skin (armpit,
    jaw over throat) stays a fold — the geodesic goes round it. Only vertices within `reach` of the crystal matter (geodesic >= euclidean)."""
    cand = np.nonzero(np.linalg.norm(Pw - centre, axis=1) < reach)[0]
    cells = {}
    keys = np.floor(Pw[cand] / gap).astype(np.int64)
    for i, k in zip(cand, map(tuple, keys)):
        cells.setdefault(k, []).append(i)
    added = 0
    for i, k in zip(cand, map(tuple, keys)):
        near = []
        for dx in (-1, 0, 1):
            for dy in (-1, 0, 1):
                for dz in (-1, 0, 1):
                    near.extend(cells.get((k[0] + dx, k[1] + dy, k[2] + dz), ()))
        near = np.array([j for j in near if j > i and comp[j] != comp[i]], dtype=np.int64)
        if not len(near):
            continue
        d = np.linalg.norm(Pw[near] - Pw[i], axis=1)
        for j, dd in zip(near[d < gap], d[d < gap]):
            w = float(dd * penalty + toll)
            if j not in adj[i] or w < adj[i][j]:
                adj[i][j] = w
                adj[j][i] = w
                added += 1
    return added


# ---------------------------------------------------------------- raster
def raster(UVpx, Tvals, res, tri_ids, want_points, P3, T, tvert):
    """Rasterise triangles (texel centres). Returns per-texel min t, max t, count, and (for want_points) the 3D point of the min-t
    triangle + texel size. UVpx: (F,3,2) in pixels (x = u res, y = v res)."""
    tmin = np.full((res, res), np.inf)
    tmax = np.full((res, res), -np.inf)
    count = np.zeros((res, res), np.int32)
    pts = np.zeros((res, res, 3)) if want_points else None
    tsz = np.zeros((res, res)) if want_points else None
    for f in tri_ids:
        uv = UVpx[f]
        x0, y0 = int(math.floor(uv[:, 0].min() - 0.5)), int(math.floor(uv[:, 1].min() - 0.5))
        x1, y1 = int(math.ceil(uv[:, 0].max() - 0.5)), int(math.ceil(uv[:, 1].max() - 0.5))
        x0, y0, x1, y1 = max(x0, 0), max(y0, 0), min(x1, res - 1), min(y1, res - 1)
        if x1 < x0 or y1 < y0:
            continue
        xs, ys = np.meshgrid(np.arange(x0, x1 + 1) + 0.5, np.arange(y0, y1 + 1) + 0.5)
        (ax, ay), (bx, by), (cx, cy) = uv
        den = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy)
        if abs(den) < 1e-12:
            continue
        l0 = ((by - cy) * (xs - cx) + (cx - bx) * (ys - cy)) / den
        l1 = ((cy - ay) * (xs - cx) + (ax - cx) * (ys - cy)) / den
        l2 = 1.0 - l0 - l1
        m = (l0 >= -1e-9) & (l1 >= -1e-9) & (l2 >= -1e-9)
        if not m.any():
            continue
        tv = tvert[T[f]]
        tt = l0 * tv[0] + l1 * tv[1] + l2 * tv[2]
        iy, ix = np.nonzero(m)
        gy, gx = iy + y0, ix + x0
        tval = tt[m]
        count[gy, gx] += 1
        tmax[gy, gx] = np.maximum(tmax[gy, gx], tval)
        if want_points:
            better = tval < tmin[gy, gx]
            if better.any():
                a, b, c = P3[T[f]]
                p = l0[m][better, None] * a + l1[m][better, None] * b + l2[m][better, None] * c
                pts[gy[better], gx[better]] = p
                area3 = 0.5 * np.linalg.norm(np.cross(b - a, c - a))
                areau = 0.5 * abs(den)
                tsz[gy[better], gx[better]] = math.sqrt(area3 / max(areau, 1e-12))
        tmin[gy, gx] = np.minimum(tmin[gy, gx], tval)
    return tmin, tmax, count, pts, tsz


def dilate(img, covered, px):
    out = img.copy()
    have = covered.copy()
    for _ in range(px):
        acc = np.zeros_like(out)
        got = np.zeros_like(have)
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1)):
            sh = np.roll(np.roll(out, dy, 0), dx, 1)
            hv = np.roll(np.roll(have, dy, 0), dx, 1)
            acc = np.where(hv & (sh > acc), sh, acc)
            got |= hv
        new = got & ~have
        out = np.where(new, acc, out)
        have = have | new
    return out


# ---------------------------------------------------------------- per species
def bake(entry):
    meta = json.loads((D.CONTAM_DIR / ("%s_body.json" % entry)).read_text(encoding="utf-8"))
    z = np.load(D.CONTAM_DIR / ("%s_body.npz" % entry))
    s = float(meta["scale"])
    P = z["P"] * s                                     # metres (species space scaled; orientation irrelevant here)
    T = z["T"]
    UV = z["UV"]
    pivot = z["pivot"] * s
    src = z["source"] * s
    R = D.CONTAM_FACTOR[entry] * float(meta["visibleLengthM"])
    report = dict(entry=entry, radiusM=round(R, 4), factor=D.CONTAM_FACTOR[entry], visibleLengthM=meta["visibleLengthM"],
                  vertices=int(len(P)), triangles=int(len(T)), meshes=meta["meshes"], uvLayers=meta["uvLayers"])
    # UV0 presence / range
    has_uv = bool(len(UV)) and not np.all(UV < -0.5)
    report["uv0Present"] = has_uv
    report["uv0Range"] = [round(float(UV[..., 0].min()), 4), round(float(UV[..., 0].max()), 4), round(float(UV[..., 1].min()), 4),
                          round(float(UV[..., 1].max()), 4)]
    # geodesic on the welded graph
    inv, Tw = weld(P, T)
    nW = int(inv.max()) + 1
    Pw = np.zeros((nW, 3))
    Pw[inv] = P
    adj, opp = graph(Pw, Tw)
    comp, ncomp = components(nW, adj)
    bridges = bridge(Pw, comp, adj, src, R * 1.05, BRIDGE_GAP_M)
    near = np.nonzero(np.linalg.norm(P[T].mean(1) - src, axis=1) < max(0.08, R * 0.1))[0]
    best, bd = None, np.inf
    for f in near:
        q = closest_on_tri(src, *P[T[f]])
        d = float(np.linalg.norm(q - src))
        if d < bd:
            best, bd, bq = f, d, q
    seeds = [(int(inv[v]), float(np.linalg.norm(P[v] - bq))) for v in T[best]]
    gW = dijkstra(adj, seeds)
    g = gW[inv]
    src_comp = int(comp[inv[T[best][0]]])
    comp_v = comp[inv]
    report.update(weldedVertices=nW, components=ncomp, sourceComponentVertices=int((comp == src_comp).sum()), shellBridges=bridges,
                  bridgeRule="different shells touching within %.1f cm: cost = gap x 2 + 3 mm (never inside one shell)" % (BRIDGE_GAP_M * 100),
                  sourceTriangleDistM=round(bd, 5), boundaryEdges=int(sum(1 for v in opp.values() if len(v) == 1)))
    # what a euclidean sphere would add (bleed onto other limbs / other side of a fold)
    eu = np.linalg.norm(P - pivot, axis=1)
    inside_eu = eu < R
    bleed = inside_eu & ~(g < R)
    report["geodesicVsSphere"] = dict(sphereVertices=int(inside_eu.sum()), geodesicVertices=int((g < R).sum()),
                                      sphereBleedVertices=int(bleed.sum()),
                                      sphereBleedOtherComponent=int((bleed & (comp_v != src_comp)).sum()),
                                      note="sphereBleed = inside a euclidean ball of the radius but geodesically farther (limbs / folds the "
                                           "contamination must not jump to)")
    if not has_uv:
        report.update(mode="Sphere", reason="no UV0 on the body mesh")
        (D.CONTAM_DIR / ("%s_bake.json" % entry)).write_text(json.dumps(report, ensure_ascii=False, indent=1), encoding="utf-8")
        return report
    tvert = np.minimum(g / R, 10.0)
    res = D.CONTAM_RES
    for attempt in range(2):
        UVpx = np.clip(UV, 0.0, 1.0) * res
        region_tris = np.nonzero(tvert[T].min(1) < 1.15)[0]
        tmin, tmax, count, pts, tsz = raster(UVpx, None, res, range(len(T)), True, P, T, tvert)
        region = tmin < 1.0
        texel_m = float(np.median(tsz[region])) if region.any() else 0.0
        if texel_m > D.CONTAM_MAX_TEXEL_M and res < 2048:
            res = 2048
            continue
        break
    conflict = region & (count >= 2) & ((tmax - tmin) > 0.25)
    covered = count > 0
    report.update(resolution=res, regionTriangles=int(len(region_tris)), regionTexels=int(region.sum()),
                  uvCoveragePct=round(100.0 * covered.mean(), 3), regionTexelMedianM=round(texel_m, 5),
                  uvOverlapTexelsPct=round(100.0 * ((count >= 2).sum() / max(1, covered.sum())), 3),
                  regionConflictPct=round(100.0 * conflict.sum() / max(1, region.sum()), 3))
    if conflict.sum() > D.CONTAM_CONFLICT_MAX * max(1, region.sum()):
        report.update(mode="Sphere", reason="UV0 overlaps: %.2f %% of the region texels are shared with surface farther than .25 R"
                      % report["regionConflictPct"])
    else:
        report.update(mode="Texture", reason="UV0 present, region conflict %.2f %% <= %.0f %%" % (report["regionConflictPct"],
                                                                                             100 * D.CONTAM_CONFLICT_MAX))
    # pattern on the region texels
    iy, ix = np.nonzero(tmin < 1.05)
    p = pts[iy, ix] - pivot
    t = tmin[iy, ix]
    cov = pattern(p, t, tsz[iy, ix], R)                    # tsz = metres per texel
    val = cov * (1.0 - CLOSE_SCALE * np.clip(t, 0, 1))
    mask = np.zeros((res, res))
    mask[iy, ix] = val
    # UV-shared texels whose other surface is outside the radius: drop (no vein painted on a far surface — see header, step 2)
    drop = (tmin < 1.05) & (count >= 2) & (tmax >= 1.0) & ((tmax - tmin) > 0.25)
    report.update(conflictDroppedTexels=int(drop.sum()), conflictDroppedVeinTexels=int((drop & (np.round(mask * 255.0) >= 6)).sum()),
                  conflictRule="texel shared with surface outside the radius (t_max >= 1, |dt| > .25) -> 0 (kept by no surface)")
    mask[drop] = 0.0
    mask = dilate(mask, covered, D.CONTAM_DILATE_PX)
    img8 = np.clip(np.round(mask * 255.0), 0, 255).astype(np.uint8)
    report["outsideVeinTexelsAfter"] = int((drop & (img8 >= 6)).sum())   # must be 0 (dilation never refills a covered texel)
    report.update(veinTexels=int((img8[region] >= 6).sum()), veinCoverageOfRegionPct=round(100.0 * (img8[region] >= 6).mean(), 2),
                  nearCoveragePct=round(100.0 * (img8[region & (tmin < 0.3)] >= 6).mean(), 2) if (region & (tmin < 0.3)).any() else 0.0,
                  farCoveragePct=round(100.0 * (img8[region & (tmin > 0.7)] >= 6).mean(), 2) if (region & (tmin > 0.7)).any() else 0.0)
    D.CONTAM_DEPLOY.mkdir(parents=True, exist_ok=True)
    out = D.CONTAM_DEPLOY / D.contam_mask_name(entry)
    Image.fromarray(img8[::-1], mode="L").save(out, optimize=True)    # row 0 = v 1
    report["mask"] = str(out.relative_to(D.ROOT)).replace("\\", "/")
    report["unityMask"] = D.UNITY_CONTAM + "/" + D.contam_mask_name(entry)
    report["encoding"] = "R8 linear: v = coverage x (1 - %.2f t); presence = smoothstep(%.2f, %.2f, v); t ~ 1 - v" % (CLOSE_SCALE, PRESENCE_LO, PRESENCE_HI)
    approx(entry, img8, res, report)
    (D.CONTAM_DIR / ("%s_bake.json" % entry)).write_text(json.dumps(report, ensure_ascii=False, indent=1), encoding="utf-8")
    print("CONTAM", entry, json.dumps({k: report[k] for k in ("radiusM", "mode", "resolution", "regionTexels", "regionConflictPct",
                                                               "regionTexelMedianM", "veinCoverageOfRegionPct", "nearCoveragePct",
                                                               "farCoveragePct", "geodesicVsSphere")}, ensure_ascii=False))
    return report


def approx(entry, img8, res, report):
    """Review only (NOT the shader): ink-wash approximation of the base colour, contamination darkening, telegraph tint flow to 60 %."""
    src = D.ASSETS / "Art/Characters/Folklore298/Textures" / FOLKLORE_TEX[entry]
    with Image.open(src) as im:
        base = np.asarray(im.convert("RGB").resize((res, res), Image.LANCZOS), dtype=np.float64) / 255.0
    lum = base @ np.array([0.299, 0.587, 0.114])
    ink, paper = np.array(D.INK), np.array(D.PAPER)
    k = smooth(0.05, 0.85, lum)[..., None]
    washed = ink + (paper - ink) * k
    washed = washed * 0.70 + base * 0.30
    v = img8[::-1].astype(np.float64) / 255.0           # img8 rows = UV v up; [::-1] = file orientation (row 0 = v 1), like the base texture
    pres = smooth(PRESENCE_LO, PRESENCE_HI, v)
    dark = pres * (DARK_FAR + (DARK_NEAR - DARK_FAR) * v)
    idle = washed * (1 - dark[..., None]) + np.array(D.CORRUPT_INK) * INK_MIX * dark[..., None]
    demo = "Fire"
    tint = np.array(D.PALETTE[demo]) * BRIGHT
    tt = 1.0 - v
    flow = pres * (1.0 - smooth(FLOW_DEMO - FLOW_SOFT, FLOW_DEMO, tt)) * TINT_ALPHA
    tele = idle * (1 - flow[..., None]) + tint * flow[..., None]
    Image.fromarray(np.clip(idle * 255, 0, 255).astype(np.uint8)).save(D.CONTAM_DIR / ("%s_approx_idle.png" % entry))
    Image.fromarray(np.clip(tele * 255, 0, 255).astype(np.uint8)).save(D.CONTAM_DIR / ("%s_approx_tele.png" % entry))
    report["approx"] = dict(idle="Analysis/Contam/%s_approx_idle.png" % entry, tele="Analysis/Contam/%s_approx_tele.png" % entry,
                            demoTint=demo, flowFront=FLOW_DEMO,
                            note="review approximation in sRGB space (the shader composites in linear after InkWash297); tint = 시험용 속성 가정")


def verify():
    """deploy contract of the masks + the rows that point at them (read-only) -> Analysis/Contam/contam308_verify.json"""
    doc = json.loads((D.SHARD_DEPLOY / "shard308_placements.json").read_text(encoding="utf-8"))
    rows = {r["owner"]: r for r in doc["placements"]}
    ck = {}
    for e in D.SPECIES:
        rep = json.loads((D.CONTAM_DIR / ("%s_bake.json" % e)).read_text(encoding="utf-8"))
        row = rows.get(e, {})
        f = D.CONTAM_DEPLOY / D.contam_mask_name(e)
        ck[e + ":rowMode=bake"] = row.get("contamMode") == rep["mode"]
        ck[e + ":radius2.5-4xVisible"] = 2.5 - 1e-6 <= rep["radiusM"] / rep["visibleLengthM"] <= 4.0 + 1e-6 and abs(row.get("contamRadius", 0) - rep["radiusM"]) < 1e-6
        ck[e + ":geodesicNotSphere(bleed avoided > 0 or none near)"] = rep["geodesicVsSphere"]["geodesicVertices"] <= rep["geodesicVsSphere"]["sphereVertices"]
        if rep["mode"] == "Texture":
            ok = f.exists()
            if ok:
                with Image.open(f) as im:
                    a = np.asarray(im)
                    ok = im.mode == "L" and im.size == (rep["resolution"], rep["resolution"])
                ck[e + ":maskL8"] = ok
                ck[e + ":maskHasVeins"] = int((a >= 6).sum()) > 1000
                ck[e + ":maskZeroOutside"] = float((a == 0).mean()) > 0.5
            else:
                ck[e + ":maskL8"] = False
            ck[e + ":rowMaskPath"] = row.get("contamMask") == D.UNITY_CONTAM + "/" + D.contam_mask_name(e)
            ck[e + ":uvConflict<=3%"] = rep["regionConflictPct"] <= 100 * D.CONTAM_CONFLICT_MAX
            ck[e + ":noVeinOnFarSharedTexel"] = "outsideVeinTexelsAfter" in rep and rep["outsideVeinTexelsAfter"] == 0
        else:
            ck[e + ":sphereNoMask"] = row.get("contamMask", "") == ""
    have = sorted(p.name for p in D.CONTAM_DEPLOY.iterdir())
    want = sorted(D.contam_mask_name(e) for e in D.SPECIES
                  if json.loads((D.CONTAM_DIR / ("%s_bake.json" % e)).read_text(encoding="utf-8"))["mode"] == "Texture")
    ck["deployFolderExact"] = have == want
    fails = [k for k, v in ck.items() if not v]
    (D.CONTAM_DIR / "contam308_verify.json").write_text(json.dumps(dict(tool="contam308.py verify", checks=ck, failures=len(fails)),
                                                                  ensure_ascii=False, indent=1), encoding="utf-8")
    print("CONTAM_VERIFY failures=%d %s" % (len(fails), json.dumps(fails)))


def mipbleed(entries):
    """Read-only estimate (no output texture changes): how much of the CLEAN body surface (geodesic t >= 1.05) reads contamination when the
    mask is sampled at a coarse mip. The body UV0 is an atlas of many small islands (Meshy unwrap), so a coarse mip averages a contaminated
    island into its UV neighbours — skin somewhere else on the body. Worst case per level = the box-filtered mip texel a clean texel falls
    in (trilinear / bilinear only soften it). Feeds the shader's mip clamp (InkOrganSurface _ContamMaxLod = EnemyTelegraphTimingSO
    ContaminationMaxMip). -> Analysis/Contam/contam308_mipbleed.json"""
    out = {}
    for entry in entries:
        rep = json.loads((D.CONTAM_DIR / ("%s_bake.json" % entry)).read_text(encoding="utf-8"))
        if rep.get("mode") != "Texture":
            continue
        meta = json.loads((D.CONTAM_DIR / ("%s_body.json" % entry)).read_text(encoding="utf-8"))
        z = np.load(D.CONTAM_DIR / ("%s_body.npz" % entry))
        s = float(meta["scale"])
        P, T, UV, src = z["P"] * s, z["T"], z["UV"], z["source"] * s
        R = float(rep["radiusM"])
        res = int(rep["resolution"])
        inv, Tw = weld(P, T)
        nW = int(inv.max()) + 1
        Pw = np.zeros((nW, 3))
        Pw[inv] = P
        adj, _ = graph(Pw, Tw)
        comp, _ = components(nW, adj)
        bridge(Pw, comp, adj, src, R * 1.05, BRIDGE_GAP_M)
        near = np.nonzero(np.linalg.norm(P[T].mean(1) - src, axis=1) < max(0.08, R * 0.1))[0]
        best, bd, bq = None, np.inf, None
        for f in near:
            q = closest_on_tri(src, *P[T[f]])
            d = float(np.linalg.norm(q - src))
            if d < bd:
                best, bd, bq = f, d, q
        g = dijkstra(adj, [(int(inv[v]), float(np.linalg.norm(P[v] - bq))) for v in T[best]])[inv]
        tvert = np.minimum(g / R, 10.0)
        tmin, _, count, _, _ = raster(np.clip(UV, 0.0, 1.0) * res, None, res, range(len(T)), False, P, T, tvert)
        with Image.open(D.CONTAM_DEPLOY / D.contam_mask_name(entry)) as im:
            mask = np.asarray(im, dtype=np.float64)[::-1] / 255.0          # rows = UV v up, like the raster
        clean = (count > 0) & (tmin >= 1.05)
        row = dict(resolution=res, cleanTexels=int(clean.sum()), mip0Over02=int((clean & (mask >= PRESENCE_LO)).sum()), levels={})
        for lvl in (1, 2, 3, 4):
            k = 2 ** lvl
            up = np.repeat(np.repeat(mask.reshape(res // k, k, res // k, k).mean((1, 3)), k, 0), k, 1)
            row["levels"][str(lvl)] = dict(
                over02Pct=round(100.0 * (clean & (up >= PRESENCE_LO)).sum() / max(1, clean.sum()), 3),
                over06Pct=round(100.0 * (clean & (up >= PRESENCE_HI)).sum() / max(1, clean.sum()), 3),
                maxValue=round(float(up[clean].max()), 3) if clean.any() else 0.0)
        out[entry] = row
        print("CONTAM_MIPBLEED", entry, json.dumps(row), flush=True)
    worst = {l: max((r["levels"][l]["over02Pct"] for r in out.values()), default=0.0) for l in ("1", "2", "3", "4")}
    (D.CONTAM_DIR / "contam308_mipbleed.json").write_text(json.dumps(dict(
        tool="contam308.py mipbleed", rule="clean = texels drawn only by surface with geodesic t >= 1.05; value = box-filtered mip texel "
        "(worst case); over02 / over06 = presence smoothstep edges (%.2f / %.2f)" % (PRESENCE_LO, PRESENCE_HI),
        worstOver02PctByLevel=worst, species=out), ensure_ascii=False, indent=1), encoding="utf-8")
    print("CONTAM_MIPBLEED worst over02 % by mip level", json.dumps(worst))


def main():
    a = sys.argv[1:]
    if a and a[0] == "verify":
        verify()
        return
    if a and a[0] == "mipbleed":
        mipbleed(a[1:] or D.SPECIES)
        return
    if not a or a[0] != "bake":
        raise SystemExit("usage: contam308.py bake [species...] | verify | mipbleed [species...]")
    ents = a[1:] or D.SPECIES
    summary = {}
    for e in ents:
        summary[e] = bake(e)
    (D.CONTAM_DIR / "contam308_summary.json").write_text(json.dumps(
        {k: {x: v.get(x) for x in ("radiusM", "mode", "reason", "resolution", "regionConflictPct", "uvOverlapTexelsPct", "regionTexelMedianM",
                                    "veinCoverageOfRegionPct", "conflictDroppedTexels", "conflictDroppedVeinTexels", "components",
                                    "geodesicVsSphere", "mask")} for k, v in summary.items()},
        ensure_ascii=False, indent=1), encoding="utf-8")


if __name__ == "__main__":
    main()
