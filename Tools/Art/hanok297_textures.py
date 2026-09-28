"""#297 procedural textures the owned KCISA packs lack: Korean roof tiles, lattice doors/windows, rafter soffit.

Pure numpy -> PNG (sRGB colour, linear-space tangent normals, roughness). Staged outside Assets while the Editor is shared; copied to Assets/_Project/Art/World/Finish297/Textures (candidate-only). Deterministic (fixed seeds).

Roof tile module (TILE_COLS x TILE_ROWS per texture, square 2048px covering TILE_W x TILE_H metres):
  columns = convex 수키와 (round cover tiles) over concave 암키와 (pan tiles); courses overlap along the slope.
  u runs along the eave (across columns), v runs up the slope.
"""
from pathlib import Path
import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Art/World/Compact/Rebuild/Finish297/Stage/Textures'   # staged; copied into Assets/_Project/Art/World/Finish297 when the shared Editor is free
N = 2048
TILE_COLS, TILE_ROWS = 4, 6          # 4 columns of 수키와 (0.30 m pitch), 6 courses (0.28 m) per texture
TILE_W, TILE_H = 1.20, 1.68          # metres covered by one texture repeat (UV scale in the mesh builder)


def smooth_noise(shape, scale, seed, octaves=4):
    rng = np.random.default_rng(seed); out = np.zeros(shape, np.float32); amp = 1.0; tot = 0
    for o in range(octaves):
        s = max(2, int(scale * 2 ** o)); g = rng.random((s + 1, s + 1)).astype(np.float32)
        y = np.linspace(0, s, shape[0], endpoint=False); x = np.linspace(0, s, shape[1], endpoint=False)
        yi, xi = np.floor(y).astype(int), np.floor(x).astype(int); fy, fx = y - yi, x - xi
        fy = fy * fy * (3 - 2 * fy); fx = fx * fx * (3 - 2 * fx)
        a = g[yi][:, xi]; b = g[yi][:, (xi + 1) % (s + 1)]; c = g[(yi + 1) % (s + 1)][:, xi]; d = g[(yi + 1) % (s + 1)][:, (xi + 1) % (s + 1)]
        v = (a * (1 - fx) + b * fx) * (1 - fy[:, None]) + (c * (1 - fx) + d * fx) * fy[:, None]
        out += amp * v; tot += amp; amp *= .5
    return out / tot


def tileable(img):
    """Blend a noise field with its half-offset copy so it repeats without seams."""
    h, w = img.shape[:2]; r = np.roll(np.roll(img, h // 2, 0), w // 2, 1)
    wy = np.abs(np.linspace(-1, 1, h))[:, None]; wx = np.abs(np.linspace(-1, 1, w))[None, :]
    m = np.clip(np.maximum(wy, wx) * 1.4 - .4, 0, 1)
    if img.ndim == 3: m = m[..., None]
    return img * (1 - m) + r * m


def normal_from_height(hgt, strength):
    gy, gx = np.gradient(hgt)
    n = np.stack([-gx * strength, gy * strength, np.ones_like(hgt)], -1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    return n


def save_rgb(a, name):
    OUT.mkdir(parents=True, exist_ok=True)
    Image.fromarray(np.clip(a * 255 + .5, 0, 255).astype(np.uint8)).save(OUT / name)


def save_normal(n, name):
    save_rgb(n * .5 + .5, name)


def roof_tiles():
    """Grey fired clay. 수키와 = half-cylinders (r 6.5cm) on a 30cm pitch over concave 암키와 pans; courses step every 28cm.
    Heights are in metres and the normal map uses the true texel size, so relief reads at game scale."""
    px = TILE_W / N                                               # metres per texel (square texels: TILE_H/N ~ same)
    y, x = np.mgrid[0:N, 0:N].astype(np.float32)
    xm = x * px; ym = y * (TILE_H / N)
    pitch = TILE_W / TILE_COLS; course_len = TILE_H / TILE_ROWS
    col = np.floor(xm / pitch).astype(int); cu = xm / pitch - col - .5          # -0.5..0.5 across one column
    course = np.floor(ym / course_len).astype(int); cv = ym / course_len - course  # 0 = upper end of a course
    rng = np.random.default_rng(29701)
    jit = rng.normal(0, .010, (TILE_ROWS, TILE_COLS)).astype(np.float32)          # tiles are hand-laid: small lateral drift
    lift = rng.normal(0, .004, (TILE_ROWS, TILE_COLS)).astype(np.float32)
    tint = rng.normal(0, .06, (TILE_ROWS, TILE_COLS)).astype(np.float32)
    J = jit[course % TILE_ROWS, col % TILE_COLS]; L = lift[course % TILE_ROWS, col % TILE_COLS]
    rc = .065                                                     # cover tile radius (m)
    dx = (cu + J) * pitch                                         # metres from the cover-row centre line
    inside = np.abs(dx) < rc
    cover_h = np.sqrt(np.clip(rc * rc - dx * dx, 0, None))        # half-cylinder
    half = pitch / 2
    pan_t = np.clip((np.abs(dx) - rc) / (half - rc), 0, 1)        # 0 at the cover edge, 1 midway between covers
    pan_h = -.030 * np.sin(np.pi * pan_t)                         # concave pan, deepest midway
    lap = np.clip((cv - .88) / .12, 0, 1)                         # lower lip of each course sits on the next one
    h = np.where(inside, .012 + cover_h, pan_h) + L + .010 * (1 - lap) - .006 * lap * lap
    h = h + .0015 * smooth_noise((N, N), 30, 29702)
    h = tileable(h.astype(np.float32))
    gy, gx = np.gradient(h, TILE_H / N, px)
    n = np.stack([-gx, gy, np.ones_like(h)], -1); n /= np.linalg.norm(n, axis=-1, keepdims=True)
    save_normal(n, 'T_RoofTile297_N.png')
    # colour (linear): grey clay; crowns a touch lighter, pan troughs and the laps darker (baked cavity), lichen in troughs
    base = np.array([.060, .064, .070], np.float32)
    T = tint[course % TILE_ROWS, col % TILE_COLS][..., None]
    img = np.broadcast_to(base, (N, N, 3)) * (1 + T)
    crown = np.where(inside, cover_h / rc, 0)
    cavity = np.clip(1 - (h - h.min()) / (h.max() - h.min()), 0, 1)
    img = img * (1 + .35 * crown[..., None]) * (1 - .45 * (cavity ** 2)[..., None])
    edge = np.clip(1 - np.abs(np.abs(dx) - rc) / .012, 0, 1)      # dark contact line at the cover edges
    img *= (1 - .45 * edge)[..., None]
    img *= (1 - .35 * (lap ** 1.5))[..., None]
    lichen = np.clip(smooth_noise((N, N), 9, 29703) * 1.8 - .95, 0, 1) * (1 - crown)
    img = img * (1 - lichen[..., None] * .5) + np.array([.045, .050, .032], np.float32) * lichen[..., None] * .6
    img *= (.9 + .2 * smooth_noise((N, N), 5, 29704))[..., None]
    img = tileable(img.astype(np.float32))
    save_rgb(np.clip(img, 0, 1) ** (1 / 2.2), 'T_RoofTile297_BC.png')
    rough = np.clip(.58 - .20 * crown + .22 * cavity + .06 * smooth_noise((N, N), 16, 29705), 0, 1)
    save_rgb(np.repeat(tileable(rough.astype(np.float32))[..., None], 3, -1), 'T_RoofTile297_R.png')


def lattice(kind, name, frame_rgb=(.085, .060, .045), paper_rgb=(.70, .66, .57)):   # linear colours
    """Door/window panel covering one bay opening (u 0..1 across, v 0..1 up). 띠살 / 빗살 / 정자살."""
    y, x = np.mgrid[0:N, 0:N].astype(np.float32) / N
    fw = .045                                                         # frame border
    frame = (x < fw) | (x > 1 - fw) | (y < fw) | (y > 1 - fw)
    s = .018                                                          # muntin half-width in UV
    if kind == 'ttisal':        # vertical slats, horizontal 띠 bands at 1/6, 1/2, 5/6
        slat = (np.abs(((x - fw) / (1 - 2 * fw) * 14) % 1 - .5) < .14)
        bands = np.zeros_like(x, bool)
        for c in (.2, .5, .8):
            bands |= (np.abs(y - c) < .06) & (np.abs(((y - c) / .12 * 4) % 1 - .5) < .2)
        bar = slat | bands
    elif kind == 'bitsal':      # diagonal lattice
        a = ((x + y) * 16) % 1; b = ((x - y) * 16) % 1
        bar = (np.abs(a - .5) < .12) | (np.abs(b - .5) < .12)
    else:                       # 정자살 grid
        bar = (np.abs((x * 10) % 1 - .5) < .1) | (np.abs((y * 14) % 1 - .5) < .1)
    wood = np.array(frame_rgb, np.float32); paper = np.array(paper_rgb, np.float32)
    grain = .85 + .3 * smooth_noise((N, N), 40, 29710)
    paper_tone = .93 + .1 * smooth_noise((N, N), 8, 29711)
    img = np.where((frame | bar)[..., None], wood * grain[..., None], paper * paper_tone[..., None])
    # paper darkens slightly toward the frame (dust)
    edge = np.clip(np.minimum(np.minimum(x, 1 - x), np.minimum(y, 1 - y)) / .12, 0, 1)
    img = np.where((frame | bar)[..., None], img, img * (.88 + .12 * edge)[..., None])
    save_rgb(np.clip(img, 0, 1) ** (1 / 2.2), name + '_BC.png')
    hgt = (frame | bar).astype(np.float32); hgt = np.minimum(hgt, 1)
    n = normal_from_height(hgt * 1.0 + .02 * smooth_noise((N, N), 30, 29712), 6.0)
    save_normal(n, name + '_N.png')


def soffit():
    """Underside of the eaves: round rafters (서까래) on a board ceiling. u across rafters (8 per repeat), v outward."""
    y, x = np.mgrid[0:N, 0:N].astype(np.float32) / N
    cu = (x * 8) % 1 - .5; r = np.abs(cu) / .32
    rafter = np.clip(1 - r * r, 0, None) ** .5 * (np.abs(cu) < .32)
    hgt = rafter + .02 * smooth_noise((N, N), 20, 29720)
    save_normal(normal_from_height(hgt, 10.0), 'T_Soffit297_N.png')
    # dancheong 가칠 (green rafters, red boards between) — the ink world keeps them muted
    green = np.array([.045, .085, .060], np.float32); red = np.array([.110, .035, .025], np.float32)   # linear, muted 가칠
    img = np.where((np.abs(cu) < .32)[..., None], green * (.8 + .25 * rafter)[..., None], red)
    img *= (.9 + .12 * smooth_noise((N, N), 12, 29721))[..., None]
    save_rgb(np.clip(img, 0, 1) ** (1 / 2.2), 'T_Soffit297_BC.png')


if __name__ == '__main__':
    roof_tiles(); lattice('ttisal', 'T_DoorTtisal297'); lattice('bitsal', 'T_DoorBitsal297'); lattice('grid', 'T_WindowGrid297')
    lattice('ttisal', 'T_DoorTtisalRed297', frame_rgb=(.16, .035, .025))
    soffit()
    print('textures ->', OUT)
