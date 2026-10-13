"""#308 PeopleLite: CPU 점 뿌리기 미리보기(GPU 없음) - PackLite308/pl_view 의 틀을 쓰고 알파 버림 · 임의 시점만 더했다. numpy 만."""
import os, sys
import numpy as np
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'PackLite308'))
import pl_view

# Blender 작업 공간(Z 위). 몸은 -Y 를 본다(가져온 그대로) -> 'front' 는 -Y 쪽에서 본다.
VIEWS = {'front': ((0, 1, 0), (0, 0, 1)), 'back': ((0, -1, 0), (0, 0, 1)), 'side': ((-1, 0, 0), (0, 0, 1)), 'q34': ((-0.62, 0.74, -0.25), (0, 0, 1)), 'q34b': ((0.62, 0.74, -0.25), (0, 0, 1)), 'top': ((0, 0.3, -1), (0, 1, 0))}


def image_array(img, max_side=1024):
    """Blender image -> (h, w, 4) float32, 위가 v=1 쪽(행 0 = v 0)."""
    w, h = img.size
    if w == 0 or h == 0: return None
    a = np.empty(w * h * 4, dtype=np.float32); img.pixels.foreach_get(a); a = a.reshape(h, w, 4)
    k = max(1, int(np.ceil(max(w, h) / max_side)))
    return a[::k, ::k].copy() if k > 1 else a


def mesh_arrays(me, co=None):
    A = pl_view.arrays(me)
    if co is not None:
        A['co'] = np.asarray(co, dtype=np.float32)
        p = A['co'][A['tri']]; n = np.cross(p[:, 1] - p[:, 0], p[:, 2] - p[:, 0]); a2 = np.linalg.norm(n, axis=1)
        A['normal'] = (n / np.maximum(a2, 1e-20)[:, None]).astype(np.float32); A['area'] = (a2 * .5).astype(np.float64)
    return A


def sample(A, px, mats, seed=7, cap=2_500_000, density=5.0, clip=.35):
    """mats: [{'tex': (h,w,4) | None, 'color': rgb, 'alpha': bool}] -> P, colour, normal (알파 < clip 은 버린다)."""
    rng = np.random.default_rng(seed)
    want = A['area'] / (px * px) * density
    if want.sum() + len(want) > cap: want = want * (cap - len(want)) / max(want.sum(), 1.0)
    n = np.maximum(1, np.ceil(want)).astype(np.int64); idx = np.repeat(np.arange(len(n)), n)
    r1 = np.sqrt(rng.random(len(idx), dtype=np.float32)); r2 = rng.random(len(idx), dtype=np.float32)
    b0, b1, b2 = 1 - r1, r1 * (1 - r2), r1 * r2
    t = A['tri'][idx]; P = A['co'][t[:, 0]] * b0[:, None] + A['co'][t[:, 1]] * b1[:, None] + A['co'][t[:, 2]] * b2[:, None]
    tl = A['tl'][idx]; UV = A['uv'][tl[:, 0]] * b0[:, None] + A['uv'][tl[:, 1]] * b1[:, None] + A['uv'][tl[:, 2]] * b2[:, None]
    col = np.full((len(idx), 3), .6, dtype=np.float32); keep = np.ones(len(idx), dtype=bool); mi = A['mat'][idx]
    for k, m in enumerate(mats):
        sel = np.nonzero(mi == k)[0]
        if not len(sel): continue
        tex = m.get('tex'); tint = np.array(m.get('color') or (1, 1, 1), dtype=np.float32)
        if tex is None: col[sel] = tint * .6; continue
        h, w = tex.shape[:2]; u = np.mod(UV[sel, 0], 1.0); v = np.mod(UV[sel, 1], 1.0)
        px_ = tex[np.minimum((v * h).astype(np.int32), h - 1), np.minimum((u * w).astype(np.int32), w - 1)]
        col[sel] = px_[:, :3] * tint
        if m.get('alpha'): keep[sel] = px_[:, 3] >= clip
    return P[keep], col[keep], A['normal'][idx][keep]


def render(P, col, nrm, view, size, lo=None, hi=None, bg=(.86, .85, .82)):
    """view: VIEWS 이름. lo/hi = 틀을 잡을 상자(없으면 점 구름)."""
    lo = P.min(0) if lo is None else np.asarray(lo, dtype=np.float64); hi = P.max(0) if hi is None else np.asarray(hi, dtype=np.float64)
    c = np.array([[x, y, z] for x in (lo[0], hi[0]) for y in (lo[1], hi[1]) for z in (lo[2], hi[2])], dtype=np.float64)
    pl_view.VIEWS['_v'] = VIEWS[view]
    fr = pl_view.frame(c, '_v', size)
    img, mask, depth = pl_view.splat(P.astype(np.float32), col, nrm, fr, bg)
    return img


def grid(rows, pad=6, bg=(.2, .2, .2)):
    """rows: [[img, ...], ...] 같은 크기 -> 한 장."""
    h, w = rows[0][0].shape[:2]; n = max(len(r) for r in rows)
    out = np.empty((len(rows) * (h + pad) + pad, n * (w + pad) + pad, 3), dtype=np.float32); out[:] = bg
    for j, r in enumerate(rows):
        for i, im in enumerate(r): out[pad + j * (h + pad): pad + j * (h + pad) + h, pad + i * (w + pad): pad + i * (w + pad) + w] = im
    return out


save_png = pl_view.save_png
