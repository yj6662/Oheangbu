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