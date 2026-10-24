"""D308-148 앞잡 (일필휘지 한 획, step 5) textures of the one stroke, procedural, fixed seed (re-running gives the same pixels).
  RipStroke308_Bristle.png  1024 x 256, 8 bit, R = ink coverage. Long dry-brush bristle streaks along x (tiles seamlessly along the stroke),
                            gaps are long and uneven (no stripes, no repeat inside the tile). The bright grain of the stroke is the
                            background showing through a gap - this mask is never a light colour.
  RipStroke308_Crack.png    512 x 512, 8 bit, R = 0 on the crack network .. 1 at the centre of each flake. Periodic (tiles both ways),
                            warped Voronoi cells stretched along the stroke. The shader raises a threshold through it: the cracks open, then
                            the flakes shrink (a cutout, never a translucent film).
  RipStroke308_Flakes.png   1024 x 512 RGBA, 4 x 2 cells of 256 (InkFlipbook308 sheet: R heat = 0, G erosion field, B = 0, A coverage):
                            cells 0-3 angular dry flakes, cells 4-7 irregular matte blots with dry specks. No round glossy drops, no highlight.
usage: python Tools/Art/riposte308_stroke_textures.py [--out <dir>] [--preview <png>]
default --out = Oheangbu/Assets/_Project/Art/SpellVFX308/RiposteStroke (RiposteStroke308Build imports from there)."""
import argparse, math
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage as ndi
from scipy.spatial import cKDTree

ROOT = Path(__file__).resolve().parents[2]
DEFAULT_OUT = ROOT / 'Oheangbu/Assets/_Project/Art/SpellVFX308/RiposteStroke'
SEED = 308148


def smooth_noise(rng, shape, sigma):
    n = ndi.gaussian_filter(rng.standard_normal(shape).astype(np.float32), sigma, mode='wrap')
    return n / (n.std() + 1e-6)


def bristle(rng, w=1024, h=256):
    """Rows are bristles: each row gets its own long, uneven run of ink and gaps (wrap along x so it tiles along the stroke)."""
    row_gain = ndi.gaussian_filter1d(rng.random(h).astype(np.float32), 1.4)
    row_gain = (row_gain - row_gain.mean()) / (row_gain.std() + 1e-6)
    field = np.empty((h, w), np.float32)
    for r in range(h):
        sigma = rng.uniform(26.0, 210.0)                       # each bristle has its own run length
        n = ndi.gaussian_filter1d(rng.standard_normal(w).astype(np.float32), sigma, mode='wrap')
        field[r] = n / (n.std() + 1e-6)
    field = ndi.gaussian_filter1d(field, 1.1, axis=0, mode='nearest')   # a streak is a few rows thick, not one pixel
    field /= field.std() + 1e-6
    blotch = ndi.gaussian_filter1d(rng.standard_normal(w).astype(np.float32), 120.0, mode='wrap')
    blotch /= blotch.std() + 1e-6                               # whole sections that carry more or less ink
    v = field * .9 + row_gain[:, None] * .5 + blotch[None, :] * .35
    thr = np.quantile(v, .27)                                   # about a quarter of the area is gap
    cov = np.clip((v - (thr - .28)) / .34, 0.0, 1.0)            # soft edge: survives the mip chain and the runtime threshold
    cov = cov * cov * (3 - 2 * cov)
    return cov


def crack(rng, n=512, cells=(8, 5), stretch=.55):
    """Periodic warped Voronoi; value = distance to the nearest crack, normalised per cell (1 at the flake centre)."""
    cx, cy = cells
    gx, gy = np.meshgrid(np.arange(cx), np.arange(cy))
    seeds = np.stack([(gx.ravel() + rng.uniform(.12, .88, gx.size)) / cx, (gy.ravel() + rng.uniform(.12, .88, gy.size)) / cy], 1)
    m = len(seeds)
    tiled = np.concatenate([seeds + np.array([dx, dy]) for dx in (-1, 0, 1) for dy in (-1, 0, 1)])
    ids = np.tile(np.arange(m), 9)
    scale = np.array([stretch, 1.0])
    tree = cKDTree(tiled * scale)
    ys, xs = np.mgrid[0:n, 0:n]
    px = (xs + .5) / n; py = (ys + .5) / n
    # domain warp: jagged, not ruler-straight cracks (the noise is periodic, so the tile stays seamless)
    wx = smooth_noise(rng, (n, n), 4.0) * .010 + smooth_noise(rng, (n, n), 1.2) * .0035
    wy = smooth_noise(rng, (n, n), 4.0) * .010 + smooth_noise(rng, (n, n), 1.2) * .0035
    pts = np.stack([(px + wx).ravel(), (py + wy).ravel()], 1) * scale
    d, idx = tree.query(pts, k=2)
    b = (d[:, 1] - d[:, 0]).reshape(n, n).astype(np.float32)
    cell = ids[idx[:, 0]].reshape(n, n)
    top = ndi.maximum(b, labels=cell, index=np.arange(m)).astype(np.float32)
    f = np.clip(b / np.maximum(top[cell], 1e-6), 0.0, 1.0) ** .85
    life = .78 + .22 * rng.random(m).astype(np.float32)         # some flakes go earlier than others (continuous at the cracks)
    f = f * life[cell]
    f *= .94 + .06 * smooth_noise(rng, (n, n), 1.0)             # fine dry specks in the surface of a flake
    return np.clip(f, 0.0, 1.0)


def flake_shape(rng, size=512):
    """An angular dry flake, drawn 2x then roughened: a jittered polygon, a few chips bitten out of its rim."""
    img = Image.new('L', (size, size), 0); d = ImageDraw.Draw(img)
    verts = int(rng.integers(5, 8)); c = size / 2; base = size * .5 * .82
    ang = np.sort(rng.uniform(0, 2 * math.pi, verts)); ang += np.linspace(0, 1, verts, endpoint=False) * 0   # sorted: a simple polygon
    sx, sy = 1.0, rng.uniform(.45, .8); rot = rng.uniform(0, math.pi)
    pts = []
    for a in ang:
        r = base * rng.uniform(.55, 1.0)
        x, y = math.cos(a) * r * sx, math.sin(a) * r * sy
        pts.append((c + x * math.cos(rot) - y * math.sin(rot), c + x * math.sin(rot) + y * math.cos(rot)))
    d.polygon(pts, fill=255)
    for _ in range(int(rng.integers(2, 5))):                     # chips
        px, py = pts[int(rng.integers(0, verts))]; r = rng.uniform(size * .02, size * .05)
        d.ellipse((px - r, py - r, px + r, py + r), fill=0)
    return img


def blot_shape(rng, size=512):
    """An irregular matte blot: several overlapping ovals, satellite dry points around it. No round drop."""
    img = Image.new('L', (size, size), 0); d = ImageDraw.Draw(img); c = size / 2
    for _ in range(int(rng.integers(4, 7))):
        r = size * rng.uniform(.07, .17); ox, oy = rng.normal(0, size * .08, 2)
        sx, sy = r * rng.uniform(.8, 1.5), r * rng.uniform(.6, 1.2)
        d.ellipse((c + ox - sx, c + oy - sy, c + ox + sx, c + oy + sy), fill=255)
    for _ in range(int(rng.integers(3, 8))):                     # satellites
        a = rng.uniform(0, 2 * math.pi); dist = size * rng.uniform(.22, .38); r = size * rng.uniform(.012, .035)
        x, y = c + math.cos(a) * dist, c + math.sin(a) * dist
        d.ellipse((x - r, y - r, x + r, y + r), fill=255)
    return img


def roughen(rng, img, sigma=1.6, amount=.22):
    """Warp the rim with noise so the edge is torn, not drawn (blur, add noise, threshold)."""
    a = ndi.gaussian_filter(np.asarray(img, np.float32) / 255.0, sigma)
    a = a + amount * smooth_noise(rng, a.shape, 2.2) * (a * (1 - a) * 4)
    return np.clip((a - .5) * 6 + .5, 0, 1)


def flakes(rng, cell=256):
    sheet = np.zeros((cell * 2, cell * 4, 4), np.float32)
    pad = 16
    for c in range(8):
        shape = flake_shape(rng) if c < 4 else blot_shape(rng)
        a = roughen(rng, shape)
        a = np.asarray(Image.fromarray((a * 255).astype(np.uint8)).resize((cell, cell), Image.LANCZOS), np.float32) / 255.0
        a[:pad, :] = 0; a[-pad:, :] = 0; a[:, :pad] = 0; a[:, -pad:] = 0   # a clear margin: the mips never bleed into the next cell
        dt = ndi.distance_transform_edt(a > .5).astype(np.float32)
        g = np.clip(dt / max(dt.max(), 1e-6), 0, 1) ** .7
        g = np.clip(g * (.92 + .08 * smooth_noise(rng, g.shape, 1.5)) + .02 * (a > .5), 0, 1)
        r, k = divmod(c, 4)
        sheet[r * cell:(r + 1) * cell, k * cell:(k + 1) * cell, 1] = g
        sheet[r * cell:(r + 1) * cell, k * cell:(k + 1) * cell, 3] = a
    return sheet


def save_gray(arr, path):
    Image.fromarray((np.clip(arr, 0, 1) * 255 + .5).astype(np.uint8), 'L').save(path)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--out', default=str(DEFAULT_OUT)); ap.add_argument('--preview', default='')
    a = ap.parse_args()
    out = Path(a.out); out.mkdir(parents=True, exist_ok=True)
    b = bristle(np.random.default_rng(SEED + 1)); c = crack(np.random.default_rng(SEED + 2)); f = flakes(np.random.default_rng(SEED + 3))
    save_gray(b, out / 'RipStroke308_Bristle.png'); save_gray(c, out / 'RipStroke308_Crack.png')
    Image.fromarray((np.clip(f, 0, 1) * 255 + .5).astype(np.uint8), 'RGBA').save(out / 'RipStroke308_Flakes.png')
    print('bristle coverage %.3f  crack mean %.3f  flake alpha %.3f' % (b.mean(), c.mean(), f[..., 3].mean()))
    if a.preview:
        # contact sheet for the eye only: bristle tiled twice, crack tiled 2 x 2, flake alpha on paper
        sheet = Image.new('RGB', (1024 + 16 + 1024, 256 + 8 + 512 + 8 + 512), (200, 190, 170))
        bt = np.tile(b, (1, 2))[:, :1024]
        sheet.paste(Image.fromarray((bt * 255).astype(np.uint8), 'L').convert('RGB'), (0, 0))
        sheet.paste(Image.fromarray((np.tile(c, (2, 2))[:512, :1024] * 255).astype(np.uint8), 'L').convert('RGB'), (0, 264))
        sheet.paste(Image.fromarray((np.tile(c, (2, 2))[:512, :512] * 255).astype(np.uint8), 'L').convert('RGB'), (1040, 0))
        paper = np.zeros((512, 1024, 3), np.float32) + np.array([200, 190, 170], np.float32)
        al = f[..., 3:4]; ink = np.array([40, 37, 34], np.float32)
        paper = paper * (1 - al) + ink * al
        sheet.paste(Image.fromarray(paper.astype(np.uint8), 'RGB'), (0, 784))
        g = np.stack([f[..., 1]] * 3, -1) * 255
        sheet.paste(Image.fromarray(g.astype(np.uint8), 'RGB'), (1040, 784))
        Path(a.preview).parent.mkdir(parents=True, exist_ok=True); sheet.save(a.preview)
    print(out)


if __name__ == '__main__':
    main()
