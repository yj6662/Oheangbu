"""#308 PackLite: CPU point-splat views (no GPU): orthographic previews, silhouette masks, IoU.  numpy only."""
import numpy as np

# views in Blender work space (Z up; Blender +Y == Unity -Z, Blender -X == Unity +X).  (forward, up)
VIEWS = {
    'fromNegZ': ((0.0, -1.0, 0.0), (0.0, 0.0, 1.0)),    # viewer stands on the Unity -Z side; screen right = Unity +X
    'fromPosX': ((1.0, 0.0, 0.0), (0.0, 0.0, 1.0)),     # viewer stands on the Unity +X side; screen right = Unity +Z
    'top': ((0.0, 0.0, -1.0), (0.0, -1.0, 0.0)),        # screen up = Unity +Z, screen right = Unity +X
    'q34': ((0.5, -0.74, -0.45), (0.0, 0.0, 1.0)),      # from Unity (+X, up, -Z)
}


def basis(f, u):
    f = np.array(f, dtype=np.float64); f /= np.linalg.norm(f)
    u = np.array(u, dtype=np.float64); u = u - f * (u @ f); u /= np.linalg.norm(u)
    r = np.cross(f, u)
    return r, u, f


def arrays(me):
    """numpy arrays of a triangulated-on-the-fly Blender mesh."""
    me.calc_loop_triangles()
    nv, nt, nl = len(me.vertices), len(me.loop_triangles), len(me.loops)
    co = np.empty(nv * 3, dtype=np.float32); me.vertices.foreach_get('co', co); co = co.reshape(-1, 3)
    tri = np.empty(nt * 3, dtype=np.int32); me.loop_triangles.foreach_get('vertices', tri); tri = tri.reshape(-1, 3)
    tl = np.empty(nt * 3, dtype=np.int32); me.loop_triangles.foreach_get('loops', tl); tl = tl.reshape(-1, 3)
    mat = np.empty(nt, dtype=np.int32); me.loop_triangles.foreach_get('material_index', mat)
    uv = np.zeros((nl, 2), dtype=np.float32)
    if me.uv_layers:
        raw = np.empty(nl * 2, dtype=np.float32); me.uv_layers[0].data.foreach_get('uv', raw); uv = raw.reshape(-1, 2)
    p = co[tri]
    n = np.cross(p[:, 1] - p[:, 0], p[:, 2] - p[:, 0])
    a2 = np.linalg.norm(n, axis=1)
    nn = n / np.maximum(a2, 1e-20)[:, None]
    return {'co': co, 'tri': tri, 'tl': tl, 'mat': mat, 'uv': uv, 'normal': nn.astype(np.float32), 'area': (a2 * 0.5).astype(np.float64)}


def sample(A, px, mats, seed=7, cap=9_000_000, density=4.0):
    """Area-proportional surface samples -> (P (N,3), colour (N,3), normal (N,3)).  mats: list of {'tex': (h,w,3) or None, 'color': rgb}."""
    rng = np.random.default_rng(seed)
    want = A['area'] / (px * px) * density
    tot = want.sum() + len(want)
    if tot > cap: want = want * (cap - len(want)) / max(want.sum(), 1.0)
    n = np.maximum(1, np.ceil(want)).astype(np.int64)
    idx = np.repeat(np.arange(len(n)), n)
    r1 = np.sqrt(rng.random(len(idx), dtype=np.float32)); r2 = rng.random(len(idx), dtype=np.float32)
    b0, b1, b2 = 1 - r1, r1 * (1 - r2), r1 * r2
    t = A['tri'][idx]
    P = A['co'][t[:, 0]] * b0[:, None] + A['co'][t[:, 1]] * b1[:, None] + A['co'][t[:, 2]] * b2[:, None]
    tl = A['tl'][idx]
    UV = A['uv'][tl[:, 0]] * b0[:, None] + A['uv'][tl[:, 1]] * b1[:, None] + A['uv'][tl[:, 2]] * b2[:, None]
    col = np.empty((len(idx), 3), dtype=np.float32)
    mi = A['mat'][idx]
    for k, m in enumerate(mats):
        sel = np.nonzero(mi == k)[0]
        if not len(sel): continue
        tint = np.array(m['color'] if m.get('color') is not None else (0.7, 0.7, 0.7), dtype=np.float32)
        tex = m.get('tex')
        if tex is None: col[sel] = tint
        else:
            h, w = tex.shape[:2]
            u = np.mod(UV[sel, 0], 1.0); v = np.mod(UV[sel, 1], 1.0)
            col[sel] = tex[np.minimum((v * h).astype(np.int32), h - 1), np.minimum((u * w).astype(np.int32), w - 1)] * tint
    bad = mi >= len(mats)
    if bad.any(): col[bad] = (0.9, 0.1, 0.9)
    return P, col, A['normal'][idx]


def frame(corners, view, size):
    """Common camera frame from the 8 bbox corners -> (r,u,f, x0, y0, scale, W, H)."""
    r, u, f = basis(*VIEWS[view])
    sx, sy = corners @ r, corners @ u
    pad = 0.03 * max(sx.max() - sx.min(), sy.max() - sy.min())
    x0, x1, y0, y1 = sx.min() - pad, sx.max() + pad, sy.min() - pad, sy.max() + pad
    W, H = size
    sc = min(W / (x1 - x0), H / (y1 - y0))
    # centre
    x0 -= (W / sc - (x1 - x0)) / 2; y0 -= (H / sc - (y1 - y0)) / 2
    return r, u, f, x0, y0, sc, W, H


def splat(P, col, nrm, fr, bg=(0.93, 0.93, 0.90)):
    """-> (image (H,W,3) float, mask (H,W) bool, depth (H,W))."""
    r, u, f, x0, y0, sc, W, H = fr
    x = ((P @ r.astype(np.float32)) - x0) * sc; y = ((P @ u.astype(np.float32)) - y0) * sc
    ok = (x >= 0) & (x < W) & (y >= 0) & (y < H)
    xi = x[ok].astype(np.int32); yi = (H - 1 - y[ok]).astype(np.int32)
    d = (P[ok] @ f.astype(np.float32))
    pix = yi * W + xi
    order = np.argsort(-d, kind='stable')
    img = np.empty((H * W, 3), dtype=np.float32); img[:] = bg
    mask = np.zeros(H * W, dtype=bool); depth = np.full(H * W, np.inf, dtype=np.float32)
    if col is not None:
        L = np.array((-0.35, 0.5, 0.78), dtype=np.float32); L = L - 0.3 * f.astype(np.float32); L /= np.linalg.norm(L)
        sh = 0.38 + 0.62 * np.abs(nrm[ok] @ L)
        c = col[ok] * sh[:, None]
        img[pix[order]] = c[order]
    mask[pix] = True
    depth[pix[order]] = d[order]
    img = img.reshape(H, W, 3); mask = mask.reshape(H, W); depth = depth.reshape(H, W)
    if col is not None:   # fill pin-holes from neighbours (one pass)
        hole = ~mask
        acc = np.zeros_like(img); cnt = np.zeros((H, W), dtype=np.float32)
        for dy in (-1, 0, 1):
            for dx in (-1, 0, 1):
                if dx == 0 and dy == 0: continue
                m2 = np.roll(np.roll(mask, dy, 0), dx, 1)
                acc += np.roll(np.roll(img, dy, 0), dx, 1) * m2[:, :, None]; cnt += m2
        fill = hole & (cnt >= 5)
        img[fill] = acc[fill] / cnt[fill][:, None]
    return img, mask, depth


def dilate(m):
    o = m.copy()
    o[1:] |= m[:-1]; o[:-1] |= m[1:]; o[:, 1:] |= m[:, :-1]; o[:, :-1] |= m[:, 1:]
    return o


def iou(a, b):
    a, b = dilate(a), dilate(b)
    return float((a & b).sum()) / max(1, int((a | b).sum()))


def save_png(path, img):
    """img (H,W,3) float 0..1 -> PNG, pure Python (zlib)."""
    import zlib, struct
    a = (np.clip(img, 0, 1) * 255 + 0.5).astype(np.uint8)
    H, W = a.shape[:2]
    raw = np.concatenate([np.zeros((H, 1), dtype=np.uint8), a.reshape(H, W * 3)], axis=1).tobytes()

    def chunk(t, d): return struct.pack('>I', len(d)) + t + d + struct.pack('>I', zlib.crc32(t + d) & 0xffffffff)
    with open(path, 'wb') as f:
        f.write(b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', W, H, 8, 2, 0, 0, 0)) + chunk(b'IDAT', zlib.compress(raw, 6)) + chunk(b'IEND', b''))
