"""#308 PackLite: roof skin + far proxy.

Pack roofs are thousands of separate tile strips: edge-collapse shreds them. So the tile material is replaced by a clean
two-sided height-field SKIN (top = highest tile surface per cell, bottom = lowest, joined by a rim) that decimates well,
textured by a top-down bake of the original tiles (albedo x sky-facing term) + a detail normal map from the tile relief.
Two-storey roofs are split into tiers at the empty band of the roof-height histogram.
LOD2 body = stacked prisms of the footprint per height band (box-and-roof proxy).
"""
import bpy, math, re
import numpy as np
import pl_view as V

ROOF_RE = r'(?i)(^|_)(mi_)?r?_?roof(?!.*(wood|frame|found|board|rafter))|giwa|rooftile|tile_?roof|concretes?_black|thatch|straw|re_roof_0|jibung|choga'


def roof_slots(me, A, pattern=None, log=print):
    """indices of material slots treated as roof covering."""
    H = float(A['co'][:, 2].max() - A['co'][:, 2].min()) or 1.0
    z0 = float(A['co'][:, 2].min())
    zc = A['co'][A['tri']][:, :, 2].mean(1)
    tot = len(A['tri'])
    out = []
    for i, m in enumerate(me.materials):
        k = A['mat'] == i
        if not k.any(): continue
        ar = A['area'][k]; up = float((A['normal'][k][:, 2] * ar).sum() / max(ar.sum(), 1e-9))
        aup = float((np.abs(A['normal'][k][:, 2]) * ar).sum() / max(ar.sum(), 1e-9))
        zmin = float(np.percentile(zc[k], 2))
        name = m.name if m else ''
        sane = (zmin - z0) > 0.22 * H and aup > 0.25           # a roof covering sits high and is not a wall
        hit = bool(re.search(pattern or ROOF_RE, name)) and (sane or pattern is not None)
        auto = (k.sum() > 0.12 * tot and aup > 0.3 and up > -0.05 and (zmin - z0) > 0.3 * H and ar.sum() > 20)
        if pattern is None and auto and not hit: log('roof auto-detected slot', name)
        if hit or (pattern is None and auto): out.append(i)
        log('  mat %-30s tris %7d area %7.1f up %+.2f z %.1f..%.1f %s' % (name[:30], k.sum(), ar.sum(), up, zmin - z0, float(zc[k].max()) - z0, 'ROOF' if i in out else ''))
    return out


def tiers(z, w):
    """split heights at an empty band >= 0.5 m of the area histogram -> list of (lo, hi)."""
    lo, hi = float(z.min()), float(z.max())
    if hi - lo < 3.0: return [(lo - 1, hi + 1)]
    bins = np.arange(lo, hi + 0.25, 0.25)
    h, _ = np.histogram(z, bins=bins, weights=w)
    empty = h < 1e-4 * h.sum()
    cuts = []; i = 0
    while i < len(empty):
        if empty[i]:
            j = i
            while j < len(empty) and empty[j]: j += 1
            if j - i >= 2 and i > 0 and j < len(empty): cuts.append(float(bins[i] + bins[j]) / 2)
            i = j
        else: i += 1
    edges = [lo - 1] + cuts + [hi + 1]
    return [(edges[i], edges[i + 1]) for i in range(len(edges) - 1)]


def layer_split(P, cell=0.4, gap=1.0):
    """two-storey roofs: per plan cell find a vertical gap > `gap` between roof samples; spread the gap height to all cells.
    -> bool array (True = upper tier) or None when the roof is single-layered."""
    x0, y0 = P[:, 0].min(), P[:, 1].min()
    nx = int((P[:, 0].max() - x0) / cell) + 1; ny = int((P[:, 1].max() - y0) / cell) + 1
    c = ((P[:, 1] - y0) / cell).astype(np.int64) * nx + ((P[:, 0] - x0) / cell).astype(np.int64)
    order = np.lexsort((P[:, 2], c))
    cs, zs = c[order], P[order, 2]
    dz = np.diff(zs); same = cs[1:] == cs[:-1]
    cand = same & (dz > gap)
    if not cand.any(): return None
    mid = (zs[1:] + zs[:-1]) / 2
    best = np.zeros(nx * ny, dtype=np.float32); bestgap = np.zeros(nx * ny, dtype=np.float32)
    ci = cs[1:][cand]; g = dz[cand]; m = mid[cand]
    o2 = np.argsort(g)                      # the largest gap of a cell wins (written last)
    best[ci[o2]] = m[o2]; bestgap[ci[o2]] = g[o2]
    have = (bestgap > 0).reshape(ny, nx)
    occ = np.zeros(nx * ny, dtype=bool); occ[c] = True
    if have.sum() < max(8, 0.01 * occ.sum()): return None
    field = _fill_values(best.reshape(ny, nx), have, np.ones((ny, nx), dtype=bool))
    return P[:, 2] > field.reshape(-1)[c]


def _shift_or(m):
    d = m.copy()
    d[1:] |= m[:-1]; d[:-1] |= m[1:]; d[:, 1:] |= m[:, :-1]; d[:, :-1] |= m[:, 1:]
    return d


def _close_fill(occ, r=3):
    """morphological closing with radius r cells (fills pin-holes and gaps up to ~2r cells; big openings stay open)."""
    p = np.pad(occ, r + 1)
    d = p
    for _ in range(r): d = _shift_or(d)
    e = ~d
    for _ in range(r): e = _shift_or(e)
    out = (~e) | p
    return out[r + 1:-r - 1, r + 1:-r - 1]


def _fill_values(val, have, want):
    """iteratively give `want & ~have` cells the mean of their filled neighbours."""
    val = val.copy(); have = have.copy()
    for _ in range(200):
        need = want & ~have
        if not need.any(): break
        acc = np.zeros_like(val); cnt = np.zeros(val.shape, dtype=np.float32)
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            v = np.roll(np.roll(np.where(have, val, 0), dy, 0), dx, 1); h = np.roll(np.roll(have, dy, 0), dx, 1)
            acc += v; cnt += h
        ok = need & (cnt > 0)
        if not ok.any(): break
        val[ok] = acc[ok] / cnt[ok]; have |= ok
    return val


def skin_mesh(P, cell, uvrect, name):
    """P: (N,3) dense samples of one roof tier.  -> Blender object (closed two-sided shell, planar UV into uvrect=(u0,v0,u1,v1, x0,y0,x1,y1))."""
    x0, y0 = P[:, 0].min() - cell, P[:, 1].min() - cell
    nx = int(math.ceil((P[:, 0].max() - x0) / cell)) + 2; ny = int(math.ceil((P[:, 1].max() - y0) / cell)) + 2
    ix = ((P[:, 0] - x0) / cell).astype(np.int32); iy = ((P[:, 1] - y0) / cell).astype(np.int32)
    flat = iy * nx + ix
    top = np.full(nx * ny, -np.inf, dtype=np.float32); bot = np.full(nx * ny, np.inf, dtype=np.float32)
    np.maximum.at(top, flat, P[:, 2]); np.minimum.at(bot, flat, P[:, 2])
    occ = np.isfinite(top).reshape(ny, nx)
    full = _close_fill(occ)
    top = _fill_values(np.where(occ, top.reshape(ny, nx), 0), occ, full)
    bot = _fill_values(np.where(occ, bot.reshape(ny, nx), 0), occ, full)
    # light smoothing of the top (tile rows are carried by the texture/normal map) and a minimum thickness
    for _ in range(1):
        acc = top.copy(); cnt = np.ones_like(top)
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            h = np.roll(np.roll(full, dy, 0), dx, 1)
            acc += np.where(h, np.roll(np.roll(top, dy, 0), dx, 1), 0); cnt += h
        top = np.where(full, np.maximum(top, acc / cnt), top)
    bot = np.minimum(bot, top - 0.06)
    vid = -np.ones((ny, nx), dtype=np.int64); n = int(full.sum()); vid[full] = np.arange(n)
    jj, ii = np.nonzero(full)
    xs = x0 + (ii + 0.5) * cell; ys = y0 + (jj + 0.5) * cell
    verts = np.concatenate([np.stack([xs, ys, top[full]], 1), np.stack([xs, ys, bot[full]], 1)]).astype(np.float32)
    a, b, c, d = vid[:-1, :-1], vid[:-1, 1:], vid[1:, 1:], vid[1:, :-1]    # (j,i) (j,i+1) (j+1,i+1) (j+1,i)
    tris = []
    q = (a >= 0) & (b >= 0) & (c >= 0) & (d >= 0)
    tris.append(np.stack([a[q], b[q], c[q]], 1)); tris.append(np.stack([a[q], c[q], d[q]], 1))
    for m_, t in (((a >= 0) & (b >= 0) & (c >= 0) & (d < 0), (a, b, c)), ((a >= 0) & (c >= 0) & (d >= 0) & (b < 0), (a, c, d)),
                  ((a >= 0) & (b >= 0) & (d >= 0) & (c < 0), (a, b, d)), ((b >= 0) & (c >= 0) & (d >= 0) & (a < 0), (b, c, d))):
        tris.append(np.stack([t[0][m_], t[1][m_], t[2][m_]], 1))
    T = np.concatenate(tris)            # counter-clockwise seen from +Z (x right, y up)
    # boundary edges of the top sheet -> rim
    e = np.concatenate([T[:, [0, 1]], T[:, [1, 2]], T[:, [2, 0]]])
    key = np.minimum(e[:, 0], e[:, 1]) * (2 * n + 1) + np.maximum(e[:, 0], e[:, 1])
    _, first, counts = np.unique(key, return_index=True, return_counts=True)
    be = e[first[counts == 1]]
    rim = np.concatenate([np.stack([be[:, 1], be[:, 0], be[:, 0] + n], 1), np.stack([be[:, 1], be[:, 0] + n, be[:, 1] + n], 1)])
    faces = np.concatenate([T, T[:, ::-1] + n, rim])
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts.tolist(), [], faces.tolist()); me.update()
    u0, v0, u1, v1, bx0, by0, bx1, by1 = uvrect
    lv = np.empty(len(me.loops), dtype=np.int32); me.loops.foreach_get('vertex_index', lv)
    # image x = Unity +X = -Blender x ; image y(up) = Unity +Z = -Blender y   (same frame as pl_view 'top')
    uu = u0 + (bx1 - verts[lv, 0]) / (bx1 - bx0) * (u1 - u0)
    vv = v0 + (by1 - verts[lv, 1]) / (by1 - by0) * (v1 - v0)
    uvl = me.uv_layers.new(name='UVMap'); uvl.data.foreach_set('uv', np.stack([uu, vv], 1).astype(np.float32).reshape(-1))
    o = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(o)
    return o, float(top[full].min()), float(np.percentile(bot[full], 8))     # 8th percentile: a tile cap on a low wall must not count as the eave


def bake_top(P, C, N, rect, W, H):
    """top-down bake of roof samples into a (size,size,3) albedo and a detail normal map.  rect = (bx0,by0,bx1,by1) Blender xy."""
    bx0, by0, bx1, by1 = rect
    x = ((bx1 - P[:, 0]) / (bx1 - bx0) * W).astype(np.int32); y = ((by1 - P[:, 1]) / (by1 - by0) * H).astype(np.int32)
    ok = (x >= 0) & (x < W) & (y >= 0) & (y < H)
    pix = (H - 1 - y[ok]) * W + x[ok]
    order = np.argsort(P[ok, 2], kind='stable')
    img = np.zeros((H * W, 3), dtype=np.float32); hgt = np.full(H * W, np.nan, dtype=np.float32); mask = np.zeros(H * W, dtype=bool)
    sky = 0.84 + 0.16 * np.clip(np.abs(N[ok, 2]), 0, 1) ** 1.5       # direction-neutral: surfaces facing the sky are lighter
    img[pix[order]] = (C[ok] * sky[:, None])[order]; hgt[pix[order]] = P[ok, 2][order]; mask[pix] = True
    img = img.reshape(H, W, 3); hgt = hgt.reshape(H, W); mask = mask.reshape(H, W)
    have = mask.copy()
    for _ in range(24):   # pad outwards / fill pin-holes
        if have.all(): break
        acc = np.zeros_like(img); acch = np.zeros((H, W), dtype=np.float32); cnt = np.zeros((H, W), dtype=np.float32)
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            h = np.roll(np.roll(have, dy, 0), dx, 1)
            acc += np.roll(np.roll(img, dy, 0), dx, 1) * h[:, :, None]; acch += np.where(h, np.roll(np.roll(np.nan_to_num(hgt), dy, 0), dx, 1), 0); cnt += h
        new = ~have & (cnt > 0)
        img[new] = acc[new] / cnt[new][:, None]; hgt[new] = acch[new] / cnt[new]; have |= new
    img[~have] = img[have].mean(0) if have.any() else 0.2
    hgt = np.nan_to_num(hgt, nan=float(np.nanmean(hgt)) if have.any() else 0.0)
    # detail height = height - blurred height (box blur ~0.35 m)
    k = max(2, int(0.35 / ((bx1 - bx0) / W)))
    cs = np.cumsum(np.pad(hgt, ((k, k), (k, k)), mode='edge'), 0); bl = (cs[2 * k:] - cs[:-2 * k]) / (2 * k)
    cs = np.cumsum(bl, 1); bl = (cs[:, 2 * k:] - cs[:, :-2 * k]) / (2 * k)
    det = hgt - bl[:H, :W]
    mpp = (bx1 - bx0) / W; mpy = (by1 - by0) / H
    gx = (np.roll(det, -1, 1) - np.roll(det, 1, 1)) / (2 * mpp); gy = (np.roll(det, 1, 0) - np.roll(det, -1, 0)) / (2 * mpy)
    gx = np.clip(gx, -1.5, 1.5); gy = np.clip(gy, -1.5, 1.5)
    nrm = np.stack([-gx, -gy, np.ones_like(gx)], 2); nrm /= np.linalg.norm(nrm, axis=2, keepdims=True)
    return img, nrm * 0.5 + 0.5, mask


def prism_proxy(P, C, ztop, cell, name):
    """stacked footprint prisms of the body samples below ztop.  -> (verts, faces, face colour (F,3))"""
    z0 = float(P[:, 2].min())
    x0, y0 = P[:, 0].min() - cell, P[:, 1].min() - cell
    nx = int(math.ceil((P[:, 0].max() - x0) / cell)) + 2; ny = int(math.ceil((P[:, 1].max() - y0) / cell)) + 2
    ix = ((P[:, 0] - x0) / cell).astype(np.int32); iy = ((P[:, 1] - y0) / cell).astype(np.int32)
    slab = 0.5
    ns = max(1, int(math.ceil((ztop - z0) / slab)))
    iz = np.clip(((P[:, 2] - z0) / slab).astype(np.int32), 0, ns)
    keep = P[:, 2] < ztop
    foot = np.zeros((ns, ny, nx), dtype=bool)
    foot[iz[keep].clip(0, ns - 1), iy[keep], ix[keep]] = True
    col = np.zeros((ns, 3), dtype=np.float64); cnt = np.zeros(ns)
    np.add.at(col, iz[keep].clip(0, ns - 1), C[keep]); np.add.at(cnt, iz[keep].clip(0, ns - 1), 1)
    foot = np.stack([_close_fill(f) if f.any() else f for f in foot])
    segs = []   # (k0, k1, footprint, colour)
    k = 0
    while k < ns:
        f = foot[k].copy(); c = col[k].copy(); n = cnt[k]; j = k + 1
        while j < ns:
            inter = (f & foot[j]).sum(); uni = (f | foot[j]).sum()
            if uni == 0 or inter / uni < 0.72: break
            f |= foot[j]; c += col[j]; n += cnt[j]; j += 1
        if f.any(): segs.append((k, j, f, c / max(n, 1)))
        k = j
    verts, faces, fcol = [], [], []

    def quad(p, c):
        b = len(verts); verts.extend(p); faces.append((b, b + 1, b + 2)); faces.append((b, b + 2, b + 3)); fcol.extend([c, c])
    for si, (k0, k1, f, c) in enumerate(segs):
        za, zb = z0 + k0 * slab, min(z0 + k1 * slab, ztop)
        pf = np.pad(f, 1)
        # walls: runs of boundary edges
        for (dy, dx) in ((0, 1), (0, -1), (1, 0), (-1, 0)):
            bnd = pf[1:-1, 1:-1] & ~np.roll(np.roll(pf, -dy, 0), -dx, 1)[1:-1, 1:-1]
            if dx != 0:
                for i in range(nx):
                    colm = bnd[:, i]; j = 0
                    while j < ny:
                        if colm[j]:
                            j2 = j
                            while j2 < ny and colm[j2]: j2 += 1
                            x = x0 + (i + (1 if dx > 0 else 0)) * cell; ya, yb = y0 + j * cell, y0 + j2 * cell
                            p = [(x, ya, za), (x, yb, za), (x, yb, zb), (x, ya, zb)]
                            quad(p if dx > 0 else p[::-1], c); j = j2
                        else: j += 1
            else:
                for j in range(ny):
                    row = bnd[j, :]; i = 0
                    while i < nx:
                        if row[i]:
                            i2 = i
                            while i2 < nx and row[i2]: i2 += 1
                            y = y0 + (j + (1 if dy > 0 else 0)) * cell; xa, xb = x0 + i * cell, x0 + i2 * cell
                            p = [(xb, y, za), (xa, y, za), (xa, y, zb), (xb, y, zb)]
                            quad(p if dy > 0 else p[::-1], c); i = i2
                        else: i += 1
        # top cap where the next segment does not cover (merged row runs)
        nxt = segs[si + 1][2] if si + 1 < len(segs) and segs[si + 1][0] == k1 else np.zeros_like(f)
        cap = f & ~nxt
        prev = {}
        for j in range(ny + 1):
            runs = set()
            if j < ny:
                row = cap[j]; i = 0
                while i < nx:
                    if row[i]:
                        i2 = i
                        while i2 < nx and row[i2]: i2 += 1
                        runs.add((i, i2)); i = i2
                    else: i += 1
            for r in list(prev):
                if r not in runs:
                    j0 = prev.pop(r)
                    xa, xb, ya, yb = x0 + r[0] * cell, x0 + r[1] * cell, y0 + j0 * cell, y0 + j * cell
                    quad([(xa, ya, zb), (xb, ya, zb), (xb, yb, zb), (xa, yb, zb)], c)
            for r in runs: prev.setdefault(r, j)
    return verts, faces, np.array(fcol, dtype=np.float32).reshape(-1, 3)
