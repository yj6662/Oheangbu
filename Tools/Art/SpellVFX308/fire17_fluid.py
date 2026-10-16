#!/usr/bin/env python3
"""#308 노 fire, 17th pass (Spec 11.34; user 2026-10-16 "네가 자유롭게 만든다면 어떻게 진행할거야? 그렇게 진행해줘").

  python Tools/Art/SpellVFX308/fire17_fluid.py [band] [stain] [--reuse DIR] [--save DIR] [--ref-band PNG] [--ref-stain PNG]

Every earlier fire sheet painted one flame shape per cel, and a ring of them piled into a bed of leaves, candles or lava whatever the
graph did. Here the fire is simulated, not drawn: a 2-D stable-fluids burn (buoyant heat + smoke + an advected material coordinate)
over a ragged fuel line of several lumps, captured as one flipbook life (rises out of the ground -> steady flicker -> the source dies
-> burns out), then painted as red pigment: vermilion core, deep-red body, maroon pockets, a maroon-black smoke mass behind the
ribbons, bristle streaks smeared along the flow and the outline (line integral convolution), a ragged ink root. Nothing is brighter
than the pigment (ART-INK: the fire gives no light).

  band  -> T_V308b_fire_band_vfx.png   4 variants x 16 cels, 8 x 8, cel 384 x 440 (the ring round the target, BankVfx)
  stain -> T_V308b_fire_stain_vfx.png  1024^2 top-down ground stain over [-5.4, 5.4] m, ring at r = 2.55 (StainVfx): K splash
                                       origins on the ring, strokes flung radially from each, ragged islands, flat soot inside

--reuse DIR repaints the band from DIR/fields_vN.npz (skips the ~minutes of simulation); --save DIR keeps the simulated fields.
--ref-band / --ref-stain compare the output with a reference PNG (the scratchpad prototypes b6c/sheet.png and stain3s.png)."""
import argparse
from pathlib import Path
from types import SimpleNamespace
import numpy as np
import scipy.sparse as sp
import scipy.sparse.linalg as spla
from scipy.ndimage import map_coordinates, gaussian_filter, zoom, sobel
from PIL import Image

REPO = Path(__file__).resolve().parents[3]
GAME = REPO / 'Oheangbu/Assets/_Project/Art/SpellVFX308/V2/Textures'

VERMILION = np.array([.72, .26, .18]); DEEP = np.array([.43, .16, .12]); MAROON = np.array([.26, .09, .07])
SOOT = np.array([.165, .15, .135]); INK = np.array([.10, .09, .08]); ASH = np.array([.30, .27, .24])
HL = np.array([.90, .62, .50])

# band = the scratchpad prototype band.py run "b6c" (17th pass, chosen over b1-b6b: dense lumps, wind-leaned tongues, broad
# bristle streaks, thin maroon rim, light haze)
BAND = SimpleNamespace(nvar=4, fpv=16, w=192, h=220, up=2, warm=90, every=3, off=11, buoy=2.0, vort=5.0, seed=53, core=.38,
                       base_ink=.12, cool=.011, on_steps=0, wind=2.5, cool_sub=.0014, crop=0, lumps=7, lump_w=.035, mass=.85,
                       reveal=3, fb_lo=.20, root_fade=.07, brush=1.0, contour=.15, lic_L=24, edge_dir=.6, bristle=1.4, taper=.25,
                       haze=.3)
# stain = stain3.py splash mode run "stain3s" (the woven-wreath look of the swirl mode is gone: strokes fly out of 6 blots)
STAIN = SimpleNamespace(n=1024, R=5.4, seed=5, splash=6, red=.5, broad=2.5, L=60, drops=70, soot=.8)


def ss(a, b, x):
    t = np.clip((x - a) / (b - a), 0, 1)
    return t * t * (3 - 2 * t)


# ---- line integral convolution (sign-free: each step keeps the previous heading) + line-field helpers ----

def lic(tex, dx, dy, L, h=1.0):
    H, W = tex.shape
    yy, xx = np.mgrid[0:H, 0:W].astype(np.float32)
    tex = tex.astype(np.float32); dx = dx.astype(np.float32); dy = dy.astype(np.float32)
    acc = tex.copy(); wsum = np.ones_like(acc)
    for sgn in (1., -1.):
        py, px = yy.copy(), xx.copy()
        pdx, pdy = dx * sgn, dy * sgn
        for i in range(1, L + 1):
            px += pdx * h; py += pdy * h
            np.clip(px, 0, W - 1, out=px); np.clip(py, 0, H - 1, out=py)
            w = 1 - i / (L + 1)
            acc += w * map_coordinates(tex, [py, px], order=1, mode='nearest'); wsum += w
            ndx = map_coordinates(dx, [py, px], order=1, mode='nearest')
            ndy = map_coordinates(dy, [py, px], order=1, mode='nearest')
            flip = (ndx * pdx + ndy * pdy) < 0
            pdx = np.where(flip, -ndx, ndx); pdy = np.where(flip, -ndy, ndy)
    return acc / wsum


def iso_tangent(f, sigma):
    """unit tangent of the iso-lines of f (rotated gradient) and the gradient magnitude"""
    g = gaussian_filter(f, sigma)
    gy, gx = np.gradient(g)
    m = np.hypot(gx, gy) + 1e-9
    return -gy / m, gx / m, m


def blend_lines(fields, weights):
    """blend line fields (dx, dy) with per-pixel weights in doubled-angle space -> unit (dx, dy)"""
    c = 0; s = 0
    for (dx, dy), w in zip(fields, weights):
        th = np.arctan2(dy, dx)
        c = c + w * np.cos(2 * th); s = s + w * np.sin(2 * th)
    th = .5 * np.arctan2(s, c)
    return np.cos(th), np.sin(th)


def stretch(x, lo=2, hi=98):
    a, b = np.percentile(x, [lo, hi])
    return np.clip((x - a) / (b - a + 1e-9), 0, 1)


# ---- band: simulated fire band, flipbook ----

def make_band(A, reuse=None, save=None):
    W, H = A.w, A.h
    yy, xx = np.mgrid[0:H, 0:W].astype(np.float64)
    N = W * H
    idx = np.arange(N).reshape(H, W)
    rows, cols, vals = [], [], []
    diag = np.zeros((H, W))
    for (dy, dx) in ((0, 1), (0, -1), (1, 0), (-1, 0)):
        ny, nx = yy.astype(int) + dy, xx.astype(int) + dx
        ok = (nx >= 0) & (nx < W) & (ny >= 0) & (ny < H)
        rows.append(idx[ok]); cols.append(idx[ny[ok], nx[ok]]); vals.append(np.ones(ok.sum()))
        diag += ok + (ny >= H)     # open top: pressure 0 above the domain
    rows.append(idx.ravel()); cols.append(idx.ravel()); vals.append(-diag.ravel())
    solve = spla.factorized(sp.csc_matrix((np.concatenate(vals), (np.concatenate(rows), np.concatenate(cols))), shape=(N, N)))

    def advect(f, u, v):
        return map_coordinates(f, [np.clip(yy - v, 0, H - 1), np.clip(xx - u, 0, W - 1)], order=1, mode='nearest')

    def project(u, v):
        div = np.zeros((H, W))
        div[:, 1:-1] += (u[:, 2:] - u[:, :-2]) * .5
        div[1:-1, :] += (v[2:, :] - v[:-2, :]) * .5
        p = solve(div.ravel()).reshape(H, W)
        gx = np.zeros((H, W)); gy = np.zeros((H, W))
        gx[:, 1:-1] = (p[:, 2:] - p[:, :-2]) * .5
        gy[1:-1, :] = (p[2:, :] - p[:-2, :]) * .5
        gy[-1, :] = -p[-2, :] * .5
        u = u - gx; v = v - gy
        u[:, 0] = 0; u[:, -1] = 0; v[0, :] = 0
        return u, v

    def vort(u, v, eps):
        w = np.zeros((H, W))
        w[1:-1, 1:-1] = (v[1:-1, 2:] - v[1:-1, :-2]) * .5 - (u[2:, 1:-1] - u[:-2, 1:-1]) * .5
        aw = np.abs(w); nx = np.zeros((H, W)); ny = np.zeros((H, W))
        nx[:, 1:-1] = (aw[:, 2:] - aw[:, :-2]) * .5
        ny[1:-1, :] = (aw[2:, :] - aw[:-2, :]) * .5
        m = np.hypot(nx, ny) + 1e-6
        return eps * ny / m * w, -eps * nx / m * w

    def simulate(seed):
        rng = np.random.default_rng(seed)
        u = np.zeros((H, W)); v = np.zeros((H, W)); T = np.zeros((H, W)); S = np.zeros((H, W)); D = xx / W
        ph = rng.uniform(0, 6.28, 3); wind = rng.uniform(-.25, .25)   # noqa: F841 (ph kept: it advances the stream)
        nl = A.lumps
        # stratified along the floor, clear of the side walls (a lump hugging a wall paints a flat red block)
        lx = (np.linspace(.2, .8, nl) + rng.uniform(-.45, .45, nl) * (.6 / max(nl - 1, 1))) * W
        lw = rng.uniform(.6, 1.4, nl) * A.lump_w * W
        ls = rng.uniform(.6, 1.05, nl)
        lp = rng.uniform(0, 6.28, (nl, 2))
        total = A.warm + A.fpv * A.every
        off_step = A.warm + A.off * A.every
        frames = []
        for step in range(total):
            on = 1.0 if step < off_step else max(0., 1 - (step - off_step) / (2.5 * A.every))
            if A.on_steps:
                on = 1.0 if step < A.on_steps else max(0., 1 - (step - A.on_steps) / 10)
            x = np.arange(W)
            src = np.zeros(W)
            for k in range(nl):
                fl = .7 + .3 * np.sin(step * .21 + lp[k, 0]) * np.sin(step * .053 + lp[k, 1])
                wob = 2.5 * np.sin(step * .07 + lp[k, 1])
                src += ls[k] * fl * np.exp(-((x - lx[k] - wob) / lw[k]) ** 2)
            src += .25 * gaussian_filter(rng.random(W), 3) * (src > .05)
            if on > 0:
                T[:4] = np.maximum(T[:4], on * src); S[:4] = np.maximum(S[:4], on * src * .55)
                D[:4] = x / W + .002 * step
                v[:4] += on * src * .6
                u[:4] += on * (rng.random((4, W)) - .5) * .9 * src
            v += A.buoy * .06 * (T - .25 * S)
            u += .02 * A.wind * wind * (yy / H) * (T > .05)
            fx, fy = vort(u, v, A.vort * .06); u += fx; v += fy
            u, v = project(u, v)
            u2 = advect(u, u, v); v2 = advect(v, u, v)
            T = advect(T, u, v); S = advect(S, u, v); D = advect(D, u, v)
            u, v = u2, v2
            T = np.clip(T * (1 - A.cool) - A.cool_sub, 0, None); S *= .996
            if step >= A.warm and (step - A.warm) % A.every == 0:
                frames.append(np.stack([T, S, D])[:, :(A.crop or H)].astype(np.float32))
        return np.stack(frames)

    def noise(h, w, sigma, seed):
        n = gaussian_filter(np.random.default_rng(seed).random((h, w)), sigma)
        return (n - n.min()) / (n.max() - n.min() + 1e-9)

    def paint(T, S, D, seed, fade, reveal=1.0):
        up = A.up
        T = zoom(T[::-1], up, order=3); S = zoom(S[::-1], up, order=3); D = zoom(D[::-1], up, order=1)
        Hh, Ww = T.shape
        hgt = (Hh - 1 - np.arange(Hh))[:, None] / Hh * np.ones((1, Ww))
        n1 = noise(Hh, Ww, 2.5, seed); n2 = noise(Hh, Ww, 9, seed + 1); n3 = noise(Hh, Ww, 1.2, seed + 2)
        t = T / 1.1; s = S / .75
        haze_a = ss(.10, .55, s + .25 * (n2 - .5)) * A.haze
        haze_c = ASH * (1 - ss(.3, .9, s))[..., None] + SOOT * ss(.3, .9, s)[..., None]
        # bristle field: white noise smeared along a line field that follows the outline near edges and the advected
        # material lines (flow) inside -> streaks that trace each tongue and curl into its hook
        fdx, fdy, _ = iso_tangent(D, 1.5 * up)
        edx, edy, gm = iso_tangent(t, 2.0 * up)
        we = A.edge_dir * ss(0, np.percentile(gm, 80), gm)
        ldx, ldy = blend_lines([(fdx, fdy), (edx, edy)], [1 - we, we])
        wn = np.random.default_rng(seed + 99).random((Hh, Ww))
        wn = gaussian_filter(wn, A.bristle)
        bt = stretch(lic(wn, ldx, ldy, A.lic_L * up // 2))
        lo = A.fb_lo + A.taper * hgt          # cooler air higher up: only the hot cores survive -> tapered, pointed tongues
        fb = ss(lo, lo + .06, t + .18 * (n1 - .5) + .08 * (n3 - .5) + .22 * A.brush * (bt - .5))
        pocket = ss(.15, .45, s - .85 * t + .2 * (n2 - .5))
        # dark smoke mass: dense smoke near the heat reads as a maroon-black body behind the red ribbons (the concept's dark gaps)
        heat_n = gaussian_filter(t, 4 * up)
        mass = ss(.05, .2, heat_n + .1 * (n2 - .5)) * ss(.08, .35, s + .3 * t + .15 * (n1 - .5)) * A.mass
        mk = ss(.2, .7, s - .6 * t + .2 * (n2 - .5))
        mass_c = MAROON * (1 - mk)[..., None] + INK * mk[..., None]
        core = ss(A.core, A.core + .07, t + .12 * (n1 - .5))
        c = DEEP * np.ones((Hh, Ww, 1))
        c = c * (1 - core[..., None]) + VERMILION * core[..., None]
        c = c * (1 - pocket[..., None]) + MAROON * pocket[..., None]
        # bristle colour: light strokes in the hot body, dark strokes toward the cool edge, pale highlight hairs
        hot = ss(.3, .7, t)
        lite = ss(.6, .8, bt) * A.brush
        dark = ss(.4, .2, bt) * A.brush
        c = c * (1 - (lite * hot * .55)[..., None]) + (VERMILION * .55 + HL * .45) * (lite * hot * .55)[..., None]
        c = c * (1 - (dark * .6)[..., None]) + (DEEP * .4 + MAROON * .6) * (dark * .6)[..., None]
        hair = ss(.86, .95, bt) * ss(.45, .8, t) * (n2 > .3) * A.brush
        c = c * (1 - (.7 * hair)[..., None]) + HL * (.7 * hair)[..., None]
        mass = mass * (.75 + .5 * (bt - .5))
        base = ss(A.base_ink, 0, hgt + .15 * (n2 - .5) + .05 * (n3 - .5))   # ragged ink root, never a ruler line
        c = c * (1 - base[..., None]) + INK * base[..., None]
        g = np.hypot(sobel(fb, 0), sobel(fb, 1))
        contour = np.clip(g * 1.2, 0, 1) * (n3 > .3)
        c = c * (1 - A.contour * contour[..., None]) + MAROON * A.contour * contour[..., None]
        # haze (back) -> dark mass -> flame body (front), straight alpha
        a1 = haze_a
        c1 = haze_c
        a2 = mass + a1 * (1 - mass)
        c2 = (mass_c * mass[..., None] + c1 * (a1 * (1 - mass))[..., None]) / np.maximum(a2, 1e-6)[..., None]
        a = fb + a2 * (1 - fb)
        rgb = (c * fb[..., None] + c2 * (a2 * (1 - fb))[..., None]) / np.maximum(a, 1e-6)[..., None]
        # soft side fade so the domain walls never show as a hard cut
        side = ss(0, .1, np.minimum(np.arange(Ww), Ww - 1 - np.arange(Ww)) / Ww)[None, :]
        a = a * side * fade
        a = a * ss(0, A.root_fade, hgt + .04 * (n3 - .5))   # the root melts into the ground (no flat cut at the cel floor)
        if reveal < 1:   # birth: the band rises out of the ground (ragged reveal line)
            a = a * ss(reveal, reveal - .08, hgt + .06 * (n2 - .5))
        return np.dstack([np.clip(rgb, 0, 1), np.clip(a, 0, 1)])

    cw, chh = W * A.up, (A.crop or H) * A.up
    ncel = A.nvar * A.fpv
    gcols = 8; grows = (ncel + gcols - 1) // gcols
    sheet = np.zeros((grows * chh, gcols * cw, 4), np.float32)
    for vi in range(A.nvar):
        fp = Path(reuse) / f'fields_v{vi}.npz' if reuse else None
        if fp is not None and fp.exists():
            fr = np.load(fp)['frames']
        else:
            fr = simulate(A.seed + 101 * vi)
            if save:
                Path(save).mkdir(parents=True, exist_ok=True)
                np.savez_compressed(Path(save) / f'fields_v{vi}.npz', frames=fr)
        for fi in range(A.fpv):
            rv = (fi + 1) / (A.reveal + .5) if fi < A.reveal else 1.0
            rgba = paint(*fr[fi], seed=A.seed + 7 * vi + fi, fade=1.0, reveal=rv)
            k = vi * A.fpv + fi; R, C = divmod(k, gcols)
            sheet[R * chh:(R + 1) * chh, C * cw:(C + 1) * cw] = rgba
        print('  band variant', vi, 'done', flush=True)
    return (np.clip(sheet, 0, 1) * 255).astype(np.uint8)


# ---- stain: top-down ground stain, splash mode ----

def make_stain(A):
    N = A.n

    def noise(sigma, seed):
        n = gaussian_filter(np.random.default_rng(seed).random((N, N)), sigma)
        return (n - n.min()) / (n.max() - n.min() + 1e-9)

    def mix(c, col, w):
        return c * (1 - w[..., None]) + col * w[..., None]

    yy, xx = np.mgrid[0:N, 0:N].astype(np.float64)
    X = (xx / (N - 1) * 2 - 1) * A.R
    Z = (yy / (N - 1) * 2 - 1) * A.R
    w1 = noise(N / 30, A.seed + 1) - .5; w2 = noise(N / 30, A.seed + 2) - .5
    Xw = X + .5 * w1; Zw = Z + .5 * w2
    r = np.hypot(Xw, Zw)
    n_lo = noise(N / 26, A.seed + 3); n_md = noise(N / 80, A.seed + 4); n_hi = noise(N / 260, A.seed + 5)
    px_per_m = (N - 1) / (2 * A.R)

    # nearest of K origins near the ring wins (hard patch borders), strokes run radially from it
    rng = np.random.default_rng(A.seed)
    K = A.splash
    oa = np.sort(rng.uniform(0, 2 * np.pi, K)); orr = rng.uniform(2.2, 2.9, K); oR = rng.uniform(1.1, 1.9, K)
    ox, oz = orr * np.cos(oa), orr * np.sin(oa)
    d = np.stack([np.hypot(Xw - ox[k], Zw - oz[k]) / oR[k] for k in range(K)])
    near = np.argmin(d, 0); dn = np.min(d, 0)
    vx = Xw - ox[near]; vz = Zw - oz[near]; m = np.hypot(vx, vz) + 1e-6
    ldx, ldy = vx / m, vz / m
    bt = stretch(lic(gaussian_filter(np.random.default_rng(A.seed + 9).random((N, N)), A.broad), ldx, ldy, A.L))
    bf = stretch(lic(gaussian_filter(np.random.default_rng(A.seed + 11).random((N, N)), 1.2), ldx, ldy, A.L // 2))
    n_isl = noise(N / 18, A.seed + 7)
    ray = ss(.6, .92, bt) * ss(1.9, 1.0, dn)                    # bright streaks reach past the blot edge = flung rays
    blot = ss(1.0, .55, dn + .55 * (n_lo - .5) - .7 * ray)
    isl = ss(.38, .5, n_isl + .35 * blot)                        # break the band into ragged islands
    band = ss(.9, .3, np.abs(r - 2.55) + .5 * (n_lo - .5)) * isl
    cov = np.maximum(blot * (.55 + .45 * isl), band)
    alpha = ss(.3, .5, cov + .2 * (bt - .5) + .12 * (n_md - .5)) * (.8 + .2 * bt)
    inside = ss(2.4, 1.5, r + .6 * (n_lo - .5))
    soot_a = inside * A.soot * ss(.25, .55, n_isl + .2) * (.8 + .2 * n_md)   # flat ragged soot, no streaks
    alpha = np.maximum(alpha, soot_a)
    dots = np.zeros((N, N))
    for _ in range(A.drops):
        k = int(rng.integers(0, K)); t = oa[k] + rng.uniform(-.9, .9); rr = rng.uniform(1.3, 2.3) * oR[k]
        cx, cz = ox[k] + rr * np.cos(t) * .7, oz[k] + rr * np.sin(t) * .7; s = rng.uniform(.025, .06); el = rng.uniform(1.5, 3.0)
        hd = np.arctan2(cz - oz[k], cx - ox[k])
        i0 = int((cz / A.R + 1) / 2 * (N - 1)); j0 = int((cx / A.R + 1) / 2 * (N - 1)); h = int(s * el * px_per_m) + 3
        if not (0 <= i0 < N and 0 <= j0 < N):
            continue
        sl = np.s_[max(i0 - h, 0):i0 + h, max(j0 - h, 0):j0 + h]
        dxl, dzl = X[sl] - cx, Z[sl] - cz
        ra = dxl * np.cos(hd) + dzl * np.sin(hd); ta = -dxl * np.sin(hd) + dzl * np.cos(hd)
        dots[sl] = np.maximum(dots[sl], ss(s, s * .5, np.hypot(ra / el, ta)))
    alpha = np.maximum(alpha, dots * .8)
    c = MAROON * np.ones((N, N, 1))
    c = mix(c, INK, ss(.5, .2, bt) * .9)
    red = ss(.6, .85, bt) * ss(.25, .6, n_lo) * A.red * (1 - inside * .8)
    c = mix(c, DEEP * .3 + VERMILION * .7, red)
    c = mix(c, VERMILION * .5 + HL * .5, .5 * ss(.82, .95, bf) * red * (n_hi > .35))
    c = mix(c, SOOT * (.8 + .4 * n_md[..., None]), inside * (1 - cov))
    c = mix(c, MAROON, dots * .6)
    rgba = np.dstack([np.clip(c, 0, 1), np.clip(alpha, 0, 1)])
    return (rgba * 255).astype(np.uint8)


def compare(img, ref):
    r = np.asarray(Image.open(ref).convert('RGBA')).astype(np.int16)
    if r.shape != img.shape:
        print('  COMPARE shape mismatch', img.shape, r.shape, ref)
        return
    d = np.abs(img.astype(np.int16) - r)
    print('  COMPARE max abs %d, differing values %d / %d, %s' % (d.max(), int((d > 0).sum()), d.size, ref))


def write(name, img, ref=None):
    dst = GAME / f'T_V308b_fire_{name}_vfx.png'
    Image.fromarray(img, 'RGBA').save(dst)
    a = img[..., 3].astype(np.float32) / 255
    print('SHEET fire', name, img.shape[1], 'x', img.shape[0], 'alpha mean %.3f' % a.mean(),
          'max rgb %.2f' % (img[..., :3].max() / 255), dst.name)
    if ref:
        compare(img, ref)


if __name__ == '__main__':
    ap = argparse.ArgumentParser()
    ap.add_argument('names', nargs='*', help='band and/or stain (default: both)')
    ap.add_argument('--reuse', help='band: load DIR/fields_vN.npz instead of simulating')
    ap.add_argument('--save', help='band: keep the simulated fields in DIR')
    ap.add_argument('--ref-band')
    ap.add_argument('--ref-stain')
    a = ap.parse_args()
    names = a.names or ['band', 'stain']
    bad = [n for n in names if n not in ('band', 'stain')]
    if bad:
        ap.error('unknown sheet: ' + ' '.join(bad))
    if 'band' in names:
        write('band', make_band(BAND, a.reuse, a.save), a.ref_band)
    if 'stain' in names:
        write('stain', make_stain(STAIN), a.ref_stain)
