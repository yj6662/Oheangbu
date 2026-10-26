#!/usr/bin/env python3
"""D308-150 V10-V11 free showcase: procedural texture sheets for the 나 / 고 / 각 showcase VFX (no generated art, no licence question).
  python Tools/Art/SpellVFX308/free_showcase_textures308.py
Writes Oheangbu/Assets/_Project/Art/SpellVFX308/Vol2Vfx/FreeShowcase/Resources/FreeShowcase/*.png (loaded at runtime with Resources.Load)."""
from pathlib import Path
import numpy as np
from PIL import Image

REPO = Path(__file__).resolve().parents[3]
OUT = REPO / 'Oheangbu/Assets/_Project/Art/SpellVFX308/Vol2Vfx/FreeShowcase/Resources/FreeShowcase'
rng = np.random.default_rng(308)


def value_noise(size, cells, seed):
    """Tileable value noise in [0,1] (smoothstep interpolation on a wrapped grid)."""
    r = np.random.default_rng(seed).random((cells, cells))
    x = np.arange(size) * cells / size
    i0 = np.floor(x).astype(int); f = x - i0; i1 = (i0 + 1) % cells; i0 %= cells
    f = f * f * (3 - 2 * f)
    a = r[i0][:, i0]; b = r[i0][:, i1]; c = r[i1][:, i0]; d = r[i1][:, i1]
    fy = f[:, None]; fx = f[None, :]
    return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy


def fbm(size, base, octaves, seed, gain=0.5):
    acc = np.zeros((size, size)); amp = 1.0; tot = 0.0
    for o in range(octaves):
        acc += amp * value_noise(size, base * 2 ** o, seed + o * 17); tot += amp; amp *= gain
    return acc / tot


def save(name, arr):
    OUT.mkdir(parents=True, exist_ok=True)
    arr = np.clip(arr, 0, 1)
    Image.fromarray((arr * 255 + 0.5).astype(np.uint8)).save(OUT / name)
    print('wrote', name, arr.shape)


def rgba(mask, rgb=(1, 1, 1)):
    h, w = mask.shape
    out = np.zeros((h, w, 4)); out[..., 0], out[..., 1], out[..., 2] = rgb; out[..., 3] = mask
    return out


def grid(n):
    y, x = np.mgrid[0:n, 0:n] / (n - 1)
    return x * 2 - 1, y * 2 - 1


def sheet(cells):
    """2x2 sheet from four square cells (row 0 = top)."""
    top = np.concatenate([cells[0], cells[1]], axis=1); bot = np.concatenate([cells[2], cells[3]], axis=1)
    return np.concatenate([top, bot], axis=0)


def b1():
    """batch 1 (2026-10-26: 가 마 사 아 노 모 소 오): stone, steel, water, light-streak and splash sheets. Only these are written (--b1)."""
    # ---- rock albedo (tileable): grey-brown stone, darker ink-filled cracks ----
    R = 512
    base = fbm(R, 6, 5, 2001); det = fbm(R, 24, 3, 2002)
    cr = 1 - np.abs(fbm(R, 5, 4, 2003) * 2 - 1); cr = np.clip((cr - 0.88) * 12, 0, 1)
    tone = (0.30 + 0.28 * base + 0.12 * det) * (1 - 0.8 * cr)
    save('fs_rock.png', np.dstack([tone * 1.0, tone * 0.86, tone * 0.72]))
    save('fs_rock_crack.png', np.dstack([cr, cr, cr]))
    # ---- brushed steel (u along the blade): fine lengthwise streaks ----
    S = 256
    lanes = value_noise(S, 128, 2011)[:, :1].repeat(S, 1) * 0.6 + fbm(S, 4, 3, 2012) * 0.4
    st = 0.62 + 0.3 * lanes
    save('fs_steel.png', np.dstack([st * 0.95, st * 0.97, st * 1.0]))
    # ---- light streak (u along, v across): a razor core with a soft halo, tapered both ends ----
    W, H = 512, 64
    u = np.linspace(0, 1, W)[None, :].repeat(H, 0); v = np.linspace(-1, 1, H)[:, None].repeat(W, 1)
    taper = np.clip(np.sin(np.clip(u, 0, 1) * np.pi) ** 0.5, 0, 1)
    core = np.exp(-(v / 0.06) ** 2); halo = np.exp(-(v / 0.35) ** 2) * 0.35
    save('fs_streak.png', rgba(np.clip((core + halo) * taper, 0, 1)))
    # ---- water body (u along the flow, v across): flowing streaks, foamy broken edges, denser core ----
    W, H = 1024, 256
    u = np.linspace(0, 1, W)[None, :].repeat(H, 0); v = np.linspace(-1, 1, H)[:, None].repeat(W, 1)
    flow = value_noise(W, 64, 2021)[:H, :] * 0.5 + value_noise(W, 16, 2022)[:H, :] * 0.5
    lanes = value_noise(W, 32, 2023)[:H, :1].repeat(W, 1)
    edge = 0.85 + 0.25 * (value_noise(W, 24, 2024)[:H, :] - 0.5)
    body = np.clip((edge - np.abs(v)) * 6, 0, 1)
    streak = 0.6 + 0.4 * np.sin((v * 4 + flow * 2 + lanes * 1.5) * 3.1) ** 2      # r2: calmer (r1 zig-zag read graphic)
    save('fs_water.png', rgba(np.clip(body * streak, 0, 1)))
    # ---- splash 2x2 (crowns / spray blobs: lumpy rim, holes, satellite drops) ----
    C = 256
    cells = []
    for k in range(4):
        x, y = grid(C); r = np.sqrt(x * x + y * y); ang = np.arctan2(y, x)
        nz = fbm(C, 4, 5, 2030 + k)
        rim = 0.55 + 0.25 * (nz - 0.5) + 0.12 * np.maximum(0, np.sin(ang * (7 + k) + k)) ** 3
        m = np.clip((rim - r) * 10, 0, 1) * (0.45 + 0.55 * np.clip(r / rim, 0, 1) ** 2)      # thin middle, dense rim (a sheet of water seen through)
        holes = np.clip((fbm(C, 10, 3, 2040 + k) - 0.38) * 5, 0, 1)
        m *= holes
        rr = np.random.default_rng(2050 + k); drops = np.zeros_like(r)
        for _ in range(14):
            a = rr.random() * 6.283; d = 0.6 + rr.random() * 0.35; s = 0.02 + rr.random() * 0.04
            drops = np.maximum(drops, np.clip((s - np.sqrt((x - np.cos(a) * d) ** 2 + (y - np.sin(a) * d) ** 2)) * 50, 0, 1))
        cells.append(rgba(np.clip(np.maximum(m, drops), 0, 1)))
    save('fs_splash.png', sheet(cells))
    # ---- water ramp (u: 0 = thin edge .. 1 = thick body): white foam -> pale aqua -> deep blue-green ----
    # r2: less cyan (r1 read as ice): foam -> pale grey-green water -> deep ink-blue-green
    stops = [(0.0, (0.92, 0.95, 0.94)), (0.22, (0.62, 0.74, 0.74)), (0.5, (0.22, 0.38, 0.40)), (0.8, (0.07, 0.15, 0.18)), (1.0, (0.03, 0.07, 0.09))]
    uu = np.linspace(0, 1, 256)
    ramp = np.stack([np.interp(uu, [s[0] for s in stops], [s[1][c] for s in stops]) for c in range(3)], -1)
    save('fs_ramp_water.png', np.dstack([np.repeat(ramp[None], 4, 0), np.ones((4, 256))]))
    # ---- ground wet stain (dark, soft, lobed) ----
    G = 512
    x, y = grid(G); r = np.sqrt(x * x + y * y); ang = np.arctan2(y, x)
    edge = 0.7 + 0.18 * (fbm(G, 4, 5, 2060) - 0.5) + 0.05 * np.sin(ang * 5)
    save('fs_wet.png', rgba(np.clip((edge - r) * 4, 0, 1) * (0.75 + 0.25 * fbm(G, 12, 3, 2061))))
    # ---- thorn vine skin (dark green-black bark with lengthwise ridges) ----
    V = 256
    rid = value_noise(V, 32, 2071)[0][None, :].repeat(V, 0)
    tone = 0.16 + 0.16 * rid + 0.08 * fbm(V, 6, 3, 2072)
    save('fs_vine.png', np.dstack([tone * 0.62, tone * 0.85, tone * 0.45]))


def b2():
    """batch 2 (2026-10-26: 거 너 머 서 어 · 구 누 무 수 우): the KCISA saw-toothed ring (Pattern_79, read only - its inner signs masked away: only the toothed band
    under 수's blade whirl), a water swirl (어's vortex disc), a flame petal (너's peony). Only these are written (--b2)."""
    from PIL import Image as _I
    _I.MAX_IMAGE_PIXELS = None
    src = REPO / 'Oheangbu/Assets/KoreanTraditionalPattern_Effect/Textures/TraditionalTexture/Pattern_79.png'
    im = _I.open(src).convert('RGBA').resize((1024, 1024), _I.LANCZOS)
    a = np.asarray(im).astype(np.float64)[..., 3] / 255.0
    x, y = grid(1024); r = np.sqrt(x * x + y * y)
    band = np.clip((r - 0.74) * 40, 0, 1)            # the toothed ring only (the pattern's ring sits at ~.78 - 1 of its radius)
    save('fs_kcisa79.png', rgba(np.clip(a * band, 0, 1)))
    # ---- water swirl (a disc: spiral arms of water, denser rim, a calm eye) ----
    S = 512
    x, y = grid(S); r = np.sqrt(x * x + y * y); ang = np.arctan2(y, x)
    arms = 0.5 + 0.5 * np.sin(ang * 3 + r * 9 + fbm(S, 6, 3, 2101) * 3)
    rim = np.clip((1 - r) * 6, 0, 1) * np.clip((r - 0.08) * 5, 0, 1)
    save('fs_swirl.png', rgba(np.clip(rim * (0.35 + 0.65 * arms ** 2) * (0.8 + 0.4 * fbm(S, 16, 3, 2102)), 0, 1)))
    # ---- flame petal (u across, v along: a teardrop petal, hot core line, frayed tip) ----
    P = 256
    x, y = grid(P); v = (y + 1) * 0.5
    width = np.sin(np.clip(v, 0, 1) * np.pi) ** 0.8 * (0.85 - 0.35 * v)
    fray = 0.08 * (fbm(P, 8, 3, 2111) - 0.5)
    body = np.clip((width + fray - np.abs(x)) * 8, 0, 1)
    core = np.exp(-(x / (0.12 + 0.2 * width)) ** 2) * 0.6
    save('fs_petal.png', rgba(np.clip(body * (0.55 + core) * (0.75 + 0.5 * fbm(P, 10, 3, 2112)), 0, 1)))


def noise3(x, y, z, seed, L=32):
    """Value noise in [0,1] on a periodic L^3 lattice (trilinear, smoothstep), x / y / z float arrays of one shape (lattice units)."""
    lat = np.random.default_rng(seed).random((L, L, L))
    xi = np.floor(x).astype(int); yi = np.floor(y).astype(int); zi = np.floor(z).astype(int)
    fx = x - xi; fy = y - yi; fz = z - zi
    fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy); fz = fz * fz * (3 - 2 * fz)
    x0 = xi % L; y0 = yi % L; z0 = zi % L; x1 = (x0 + 1) % L; y1 = (y0 + 1) % L; z1 = (z0 + 1) % L
    def g(a, b, c): return lat[a, b, c]
    c00 = g(x0, y0, z0) * (1 - fx) + g(x1, y0, z0) * fx; c10 = g(x0, y1, z0) * (1 - fx) + g(x1, y1, z0) * fx
    c01 = g(x0, y0, z1) * (1 - fx) + g(x1, y0, z1) * fx; c11 = g(x0, y1, z1) * (1 - fx) + g(x1, y1, z1) * fx
    return (c00 * (1 - fy) + c10 * fy) * (1 - fz) + (c01 * (1 - fy) + c11 * fy) * fz


def fbm3(x, y, z, octaves, seed, gain=0.5):
    acc = np.zeros_like(x); amp = 1.0; tot = 0.0
    for o in range(octaves):
        s = 2 ** o
        acc += amp * noise3(x * s, y * s, z * s, seed + o * 31); tot += amp; amp *= gain
    return acc / tot


def lin2srgb(v):
    """the textures import as sRGB: store the sRGB code of the wanted LINEAR value, so the sampler returns that value."""
    v = np.clip(v, 0, 1)
    return np.where(v <= 0.0031308, v * 12.92, 1.055 * np.power(v, 1 / 2.4) - 0.055)


def mat_pass():
    """2026-10-27 common material pass (showcase120_plan.md step 2, the user: "묶음 4 끝나면 공통 재질(불 · 강철 · 물 · 돌 · 나무) 다듬기부터 하고 ..."):
    fs_fire_flip = one fire / smoke 'lick' over its life as an 8 x 8 flipbook (256 px cells, 2048 px), procedural (domain-warped 3D value noise rising
    through a teardrop flame body that lifts off, thins into wisps and cools into smoke). Channels (LINEAR after the sRGB import): R = heat (flame
    emission, 0 cold .. 1 white core), G = smoke / body density (coverage), B = 0, A = max(R, G) (the generic mask). Frame order: row 0 left -> right = birth.
    Only this is written (--mat)."""
    C, N = 256, 8
    y, x = np.mgrid[0:C, 0:C] / (C - 1)
    X = x * 2 - 1; Y = 1 - y * 2                      # Y up
    win = np.clip((0.985 - np.maximum(np.abs(X), np.abs(Y))) * 9, 0, 1)        # keep every cell's content inside its cell (no mip bleed)
    atlas = np.zeros((C * N, C * N, 4))
    preview = []
    for f in range(N * N):
        t = f / (N * N - 1)
        z = np.full_like(X, t * 7.0)                   # noise time (lattice units: a fresh pattern every ~1/7 of the life)
        rise = t * 5.5                                 # features rise (noise scrolls down in noise space)
        # domain warp, growing with height and age
        a = 0.16 + 0.26 * t + 0.30 * np.clip((Y + 1) / 2, 0, 1)
        wx = fbm3(X * 1.6 + 11, Y * 1.3 - rise * 0.6, z, 3, 4100) - 0.5
        wx2 = fbm3(X * 3.4 + 19, Y * 2.6 - rise * 1.1, z + 3, 3, 4150) - 0.5
        Xw = X + a * (wx * 1.7 + wx2 * 0.6)
        # the flame body: wide at the base, narrowing to the tip; the base lifts off and the tip shortens as it burns out
        yb = -0.9 + 1.25 * t ** 1.6
        yt = min(0.96, -0.35 + 1.4 * min(1, t * 3.2) ** 0.7) - 0.25 * t ** 2
        span = max(0.15, yt - yb)
        h = (Y - yb) / span                            # 0 base .. 1 tip
        hc = np.clip(h, 0, 1)
        w = (0.62 - 0.12 * t) * (1 - hc ** 1.3) ** 0.75 + 0.02
        env = np.clip(1 - np.abs(Xw) / w, 0, 1) * np.clip(h * 4 + 1, 0, 1) * np.clip((1.12 - h) * 3, 0, 1)
        # tongues: vertically stretched noise rising through the body (licks break off near the tip)
        n = fbm3(Xw * 3.2 + 3, Y * 1.15 - rise, z + 9, 4, 4300)
        nr = 1 - np.abs(fbm3(Xw * 2.1 + 7, Y * 0.9 - rise * 0.8, z * 0.8 + 21, 3, 4400) * 2 - 1)      # ridged: sharp lick edges
        thr = 0.15 + 0.75 * hc ** 1.2
        body = np.clip((env * 1.25 + (n - 0.5) * 1.1 + (nr - 0.6) * 0.5 - thr) * 1.9, 0, 1) * np.clip(env * 4, 0, 1)
        # late life: the body breaks into wisps and dissolves
        eat = np.clip((t - 0.5) / 0.5, 0, 1) ** 1.3
        body = np.clip(body - eat * (1.3 - n * 1.2), 0, 1)
        core = np.clip(env * 1.5 - hc * 0.55 + (n - 0.5) * 0.9 + (nr - 0.5) * 0.4, 0, 1)
        heat = body * core ** 1.4 * (1 - 0.55 * hc) * (1 - np.clip((t - 0.28) / 0.6, 0, 1)) ** 1.3
        heat = np.clip(heat * (0.35 + 0.9 * nr) * (0.7 + 0.5 * n), 0, 1)      # r2: pockets and licks inside the body (r1 saturated to a flat white core)
        smoke = body * np.clip(0.3 + 0.8 * (hc * 0.6 + t * 0.9), 0, 1)
        dens = np.clip(np.maximum(smoke, heat * 0.55) * 1.15, 0, 1) * win
        heat = heat * win
        # r2 (material pass r1: hard torn-paper silhouettes): a soft edge - blur every channel a little inside the cell
        from PIL import ImageFilter
        soft = lambda m: np.asarray(Image.fromarray((np.clip(m, 0, 1) * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(3.0)), dtype=np.float64) / 255
        heat = soft(heat); dens = soft(dens)
        cell = np.dstack([lin2srgb(heat), lin2srgb(dens), np.zeros_like(heat), np.maximum(heat, dens)])
        r0, c0 = (f // N) * C, (f % N) * C
        atlas[r0:r0 + C, c0:c0 + C] = cell
    save('fs_fire_flip.png', atlas)
    # a preview strip for the eye (not under Assets): heat on red, smoke as grey over a mid ground
    pv = np.zeros((C * N, C * N, 3)) + 0.42
    h = atlas[..., 0] ** 2.2; d = atlas[..., 1] ** 2.2
    fire = np.dstack([np.clip(h * 3.0, 0, 1), np.clip(h * 1.6 - 0.15, 0, 1), np.clip(h * 1.2 - 0.5, 0, 1)])
    pv = pv * (1 - d[..., None] * 0.85) + 0.05 * d[..., None]
    pv = np.clip(pv + fire, 0, 1)
    import sys as _s
    out = Path(_s.argv[_s.argv.index('--preview') + 1]) if '--preview' in _s.argv else None
    if out is not None:
        Image.fromarray((pv * 255).astype(np.uint8)).resize((1024, 1024)).save(out)
        print('preview', out)


def b5():
    """batch 5 (2026-10-27: 심는다): two KCISA seal stamps for the waiting seeds (read only, whole pattern - both are bold solid motifs that read as a
    seal / 낙관): Pattern_25 (a square fret 亞-like seal in a circle) -> fs_seal25, Pattern_8 (a bold round floral seal) -> fs_seal8. Only these are written (--b5)."""
    from PIL import Image as _I
    _I.MAX_IMAGE_PIXELS = None
    for n in (25, 8):
        src = REPO / f'Oheangbu/Assets/KoreanTraditionalPattern_Effect/Textures/TraditionalTexture/Pattern_{n}.png'
        im = _I.open(src).convert('RGBA').resize((512, 512), _I.LANCZOS)
        a = np.asarray(im).astype(np.float64)[..., 3] / 255.0
        save(f'fs_seal{n}.png', rgba(np.clip(a, 0, 1)))


if '--b5' in __import__('sys').argv:
    b5()
    raise SystemExit(0)
if '--mat' in __import__('sys').argv:
    mat_pass()
    raise SystemExit(0)
if '--b1' in __import__('sys').argv:
    b1()
    raise SystemExit(0)
if '--b2' in __import__('sys').argv:
    b2()
    raise SystemExit(0)

# ---- noise (R: fbm low, G: fbm high, B: ridged) ----
N = 256
n1 = fbm(N, 4, 5, 1); n2 = fbm(N, 8, 4, 50); rid = 1 - np.abs(fbm(N, 6, 4, 90) * 2 - 1)
save('fs_noise.png', np.dstack([n1, n2, rid]))

# ---- soft dot (glow / ember) ----
x, y = grid(128); r = np.sqrt(x * x + y * y)
dot = np.exp(-(r * 2.6) ** 2) * 0.85 + np.exp(-(r * 7) ** 2) * 0.6
save('fs_dot.png', rgba(np.clip(dot, 0, 1)))

# ---- flame tongues 2x2 (alpha = flame body, brighter core = higher alpha) ----
C = 256
cells = []
for k in range(4):
    x, y = grid(C)
    yy = (y + 1) / 2                       # 0 top .. 1 bottom
    nz = fbm(C, 4, 4, 200 + k) - 0.5
    xs = x + nz * 0.55 * (1 - yy) + 0.12 * np.sin(yy * 9 + k * 2) * (1 - yy)
    width = 0.08 + 0.62 * yy ** 0.8      # narrow tip at the top, wide base
    body = np.clip(1 - np.abs(xs) / width, 0, 1) * np.clip((yy - 0.02) * 3, 0, 1) * np.clip((1 - yy) * 5, 0, 1)
    body = body ** 0.7 * (0.75 + 0.5 * fbm(C, 8, 3, 300 + k))
    cells.append(rgba(np.clip(body, 0, 1)))
save('fs_flame.png', sheet(cells))

# ---- smoke puffs 2x2 ----
cells = []
for k in range(4):
    x, y = grid(C); r = np.sqrt(x * x + y * y)
    nz = fbm(C, 4, 5, 400 + k)
    m = np.clip(1 - r * (0.95 + 0.6 * (nz - 0.5)) * 1.25, 0, 1)
    m = (m ** 1.2) * (0.55 + 0.6 * fbm(C, 6, 4, 500 + k))
    cells.append(rgba(np.clip(m * 1.2, 0, 1)))
save('fs_puff.png', sheet(cells))

# ---- ink splats 2x2 (sharp-edged blot + satellite drops; alpha = ink density) ----
cells = []
for k in range(4):
    x, y = grid(C); r = np.sqrt(x * x + y * y); ang = np.arctan2(y, x)
    nz = fbm(C, 3, 5, 600 + k)
    spikes = 0.12 * np.maximum(0, np.sin(ang * (5 + k) + k)) ** 6
    edge = 0.45 + 0.25 * (nz - 0.5) + spikes
    blot = np.clip((edge - r) * 14, 0, 1)
    drops = np.zeros_like(r)
    rr = np.random.default_rng(700 + k)
    for _ in range(10):
        a = rr.random() * 6.283; d = 0.55 + rr.random() * 0.35; s = 0.025 + rr.random() * 0.05
        cx, cy = np.cos(a) * d, np.sin(a) * d
        drops = np.maximum(drops, np.clip((s - np.sqrt((x - cx) ** 2 + (y - cy) ** 2)) * 40, 0, 1))
    dens = np.maximum(blot * (0.8 + 0.2 * fbm(C, 10, 3, 800 + k)), drops)
    cells.append(rgba(dens))
save('fs_ink.png', sheet(cells))

# ---- brush stroke (horizontal: u = along the stroke; fat head on the left, dry-brush tail to the right) ----
W, H = 1024, 256
u = np.linspace(0, 1, W)[None, :].repeat(H, 0); v = np.linspace(-1, 1, H)[:, None].repeat(W, 1)
width = 0.92 * np.clip(u * 12, 0, 1) ** 0.5 * (1 - u) ** 0.55
fib = np.random.default_rng(9).random(H)                                  # bristle lanes
lanes = np.interp(np.linspace(0, H - 1, H), np.arange(H), fib)[:, None].repeat(W, 1)
dry = np.clip((lanes - u * 0.9 + 0.35) * 3, 0, 1)                           # lanes break up toward the tail
body = np.clip((width - np.abs(v)) * 18, 0, 1) * (0.35 + 0.65 * dry)
wob = value_noise(W, 16, 11)[:H, :]
save('fs_stroke.png', rgba(np.clip(body * (0.85 + 0.3 * wob), 0, 1)))

# ---- fire ramp (u: 0 = cold edge .. 1 = hot core), 256x4, RGB only ----
stops = [(0.0, (0.02, 0.01, 0.01)), (0.18, (0.25, 0.03, 0.01)), (0.38, (0.85, 0.16, 0.02)), (0.62, (1.0, 0.45, 0.06)), (0.85, (1.0, 0.78, 0.30)), (1.0, (1.0, 0.95, 0.80))]
uu = np.linspace(0, 1, 256)
ramp = np.stack([np.interp(uu, [s[0] for s in stops], [s[1][c] for s in stops]) for c in range(3)], -1)
save('fs_ramp_fire.png', np.dstack([np.repeat(ramp[None], 4, 0), np.ones((4, 256))]))

# ---- jade/gold wood energy ramp ----
stops = [(0.0, (0.0, 0.0, 0.0)), (0.3, (0.05, 0.12, 0.03)), (0.6, (0.35, 0.62, 0.12)), (0.85, (0.80, 0.90, 0.35)), (1.0, (1.0, 1.0, 0.75))]
ramp = np.stack([np.interp(uu, [s[0] for s in stops], [s[1][c] for s in stops]) for c in range(3)], -1)
save('fs_ramp_wood.png', np.dstack([np.repeat(ramp[None], 4, 0), np.ones((4, 256))]))

# ---- bark (tileable, vertical fibres) + glowing crack mask ----
B = 512
fib = value_noise(B, 64, 21)[0][None, :].repeat(B, 0)
streak = fbm(B, 4, 3, 22)
yy = np.arange(B)[:, None] / B
grain = 0.5 + 0.5 * np.sin((np.arange(B)[None, :] / B * 40 + streak * 6) * 6.283)
tone = 0.18 + 0.22 * (0.6 * fib + 0.4 * grain) * (0.7 + 0.6 * fbm(B, 8, 4, 23))
bark = np.dstack([tone * 1.0, tone * 0.82, tone * 0.62])
save('fs_bark.png', bark)
crack = 1 - np.abs(fbm(B, 3, 4, 24) * 2 - 1)
crack = np.clip((crack - 0.86) * 9, 0, 1)
save('fs_bark_glow.png', np.dstack([crack, crack, crack]))

# ---- leaves 2x2 (alpha = leaf; rgb = dark leaf green with light vein) ----
cells = []
for k in range(4):
    x, y = grid(C)
    yy = (y + 1) / 2
    half = 0.48 * np.sin(np.clip(yy, 0, 1) * np.pi) ** (0.8 + 0.3 * k) * (1 - 0.3 * yy)
    xs = x + 0.15 * np.sin(yy * 3.1 + k)
    leaf = np.clip((half - np.abs(xs)) * 30, 0, 1) * (yy > 0.03)
    vein = np.clip(1 - np.abs(xs) * 40, 0, 1) * leaf
    side = np.clip(1 - np.abs(np.sin((yy * 10 + np.abs(xs) * 6) * 3.14)) * 6, 0, 1) * leaf * 0.4
    g = 0.30 + 0.25 * fbm(C, 6, 3, 900 + k)
    rgb = np.dstack([g * 0.45 + vein * 0.3 + side * 0.2, g * 1.0 + vein * 0.35 + side * 0.25, g * 0.25 + vein * 0.15])
    cells.append(np.dstack([rgb, leaf]))
save('fs_leaf.png', sheet(cells))

# ---- ground scorch (radial burnt ink stain with a burning rim) ----
G = 512
x, y = grid(G); r = np.sqrt(x * x + y * y); ang = np.arctan2(y, x)
nz = fbm(G, 4, 5, 1000)
edge = 0.62 + 0.22 * (nz - 0.5) + 0.06 * np.sin(ang * 7)
scorch = np.clip((edge - r) * 6, 0, 1) * (0.7 + 0.3 * fbm(G, 12, 3, 1001))
save('fs_scorch.png', rgba(scorch))

# ---- shock ring ----
ring = np.exp(-((r - 0.78) * 9) ** 2) * (0.6 + 0.4 * fbm(G, 8, 3, 1100))
save('fs_ring.png', rgba(np.clip(ring * 1.2, 0, 1)))

# ---- moss / growth patch (green, lumpy) ----
moss = np.clip((0.75 + 0.25 * (fbm(G, 5, 5, 1200) - 0.5) - r) * 5, 0, 1) * np.clip(fbm(G, 16, 3, 1201) * 1.6 - 0.2, 0, 1)
g2 = fbm(G, 10, 4, 1202)
save('fs_moss.png', np.dstack([0.12 + 0.12 * g2, 0.26 + 0.25 * g2, 0.07 + 0.08 * g2, moss]))

# ---- crack burst (radial fissures on the ground, alpha) ----
fis = np.zeros_like(r)
rr = np.random.default_rng(1300)
for k in range(9):
    a0 = rr.random() * 6.283
    da = np.angle(np.exp(1j * (ang - a0)))
    wig = 0.05 * np.sin(r * 18 + k)
    fis = np.maximum(fis, np.clip(1 - np.abs(da - wig) * (40 + 60 * r), 0, 1) * np.clip((0.95 - r) * 4, 0, 1) * (r > 0.08))
save('fs_crack.png', rgba(np.clip(fis, 0, 1)))

# ---- r2: a bolder brush stroke (u along: round wet head, full belly, ragged dry tail; organic edges, few bristle gaps) ----
W, H = 1024, 256
u = np.linspace(0, 1, W)[None, :].repeat(H, 0); v = np.linspace(-1, 1, H)[:, None].repeat(W, 1)
edge_n = value_noise(W, 24, 31)[:H, :] - 0.5
width = (np.clip(u / 0.08, 0, 1) ** 0.6) * (1 - u) ** 0.35 * (0.95 + 0.25 * edge_n)
gaps = value_noise(W, 48, 33)[:H, :1].repeat(W, 1)                       # bristle lanes (constant along u)
dry = np.clip(1.0 - np.clip((u - 0.55) / 0.45, 0, 1) * (1.4 - gaps * 1.6), 0, 1)
body = np.clip((width - np.abs(v)) * 14, 0, 1) * dry
dens = 0.75 + 0.25 * fbm(W, 8, 3, 34)[:H, :]
save('fs_stroke2.png', rgba(np.clip(body * dens * 1.15, 0, 1)))

# ---- r2: thin glowing veins along a tube (u around, v along): a few wavy lengthwise lines ----
V = 256
uu, vv = np.meshgrid(np.linspace(0, 1, V), np.linspace(0, 1, V))
vein = np.zeros((V, V))
for k, c in enumerate([0.13, 0.41, 0.72]):
    x = c + 0.04 * np.sin(vv * 6.283 * (1 + k) + k)
    d = np.minimum(np.abs(uu - x), 1 - np.abs(uu - x))
    vein = np.maximum(vein, np.clip(1 - d / 0.012, 0, 1) * (0.5 + 0.5 * np.sin(vv * 6.283 * 3 + k * 2) ** 2))
save('fs_vein.png', np.dstack([vein, vein, vein]))

# ---- r2: bamboo skin (olive-gold, lengthwise fibres) ----
fib2 = value_noise(V, 64, 41)[0][None, :].repeat(V, 0)
tone = 0.55 + 0.25 * fib2 + 0.1 * fbm(V, 4, 3, 42)
save('fs_bamboo.png', np.dstack([tone * 0.62, tone * 0.6, tone * 0.32]))