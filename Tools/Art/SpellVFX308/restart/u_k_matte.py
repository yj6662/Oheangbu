"""D308-153 route K for 우: the Kling AI video (ai_video/u_kling_1.mp4, animated from the concept uks_1) matted into RGBA frames for the game.
Alpha = the painted waves, ink strokes and foam only; the red ground, the beige sky, the caster (robe, hair, arm, brush) and the bear are removed.
Method (per frame, 1280x720, 30 fps):
  1. decode with ffmpeg (fps 30, lanczos 1280x720).
  2. colour classes: BLUE (water, b > r), CREAM (the bright outline / white foam, V high, low saturation), INK (neutral near-black).
     CREAM is kept only within CREAM_NEAR px of BLUE, INK only when its component is big (a stroke) or touches BLUE; both must also differ from the clean plate
     (the median of the last frames: ground + caster) so the sky haze and the standing caster never leak in.
  3. caster exclusion: a hand-tuned soft zone around the caster's body and head (only strongly BLUE pixels survive inside it, so waves in front of his legs stay)
     plus the brush, found per frame as the dark thin component that starts at his hand.
  4. bear: brown fur is neither blue, cream nor near-black, so the key drops it; a small BEAR zone also drops dark fur specks.
  5. soft edge (gauss ~0.8 px), straight alpha; the colour under a thin edge is taken from the nearest core pixel (no brown halo), and the colour under
     alpha 0 is extended from the nearest opaque pixel (DXT5 / bilinear safe).
  6. the dead tail (nothing left) is trimmed.
  python Tools/Art/SpellVFX308/restart/u_k_matte.py [--sheet-only]
Output: Art/SpellVFX308/Restart/u_K/frames/f_0000.png ... , Art/SpellVFX308/Restart/u_K/matte_check.jpg"""
import os, sys, glob, shutil, subprocess
import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage as ndi

REPO = r'C:\Users\yj666\Oheangbu'
VIDEO = REPO + r'\Art\SpellVFX308\Restart\ai_video\u_kling_1.mp4'
OUT = REPO + r'\Art\SpellVFX308\Restart\u_K'
FRAMES = OUT + r'\frames'
RAW = os.environ.get('U_K_RAW', os.path.join(os.environ.get('TEMP', '.'), 'u_k_raw'))
FFMPEG = r'C:\Users\yj666\AppData\Roaming\Python\Python314\site-packages\imageio_ffmpeg\binaries\ffmpeg-win-x86_64-v7.1.exe'
W, H = 1280, 720

# ---- tunables ----
CREAM_NEAR = 16        # px: cream / white is kept this close to blue
INK_AREA = 260         # px: a near-black component at least this big is a stroke
INK_NEAR = 10
PLATE_FRAMES = 6       # last N frames -> the clean plate
TAIL_AREA = 25000      # px of mask below which a frame counts as dead (the sunk ring's last wave tops); a short fade follows
TAIL_FADE = 6


def decode():
    if len(glob.glob(RAW + r'\r_*.png')) >= 140:
        return
    shutil.rmtree(RAW, ignore_errors=True); os.makedirs(RAW)
    subprocess.check_call([FFMPEG, '-hide_banner', '-loglevel', 'error', '-y', '-i', VIDEO, '-vf', 'fps=30,scale=%d:%d:flags=lanczos:in_color_matrix=bt709:in_range=tv' % (W, H), '-an', RAW + r'\r_%04d.png'])


def load(i):
    return np.asarray(Image.open(RAW + r'\r_%04d.png' % i).convert('RGB')).astype(np.float32) / 255.0


yy, xx = np.mgrid[0:H, 0:W].astype(np.float32)


def ellipse(cx, cy, rx, ry, soft=0.15):
    e = np.sqrt(((xx - cx) / rx) ** 2 + ((yy - cy) / ry) ** 2)
    return np.clip((1.0 + soft - e) / (2 * soft), 0, 1)


def poly(pts, blur=6):
    im = Image.new('L', (W, H), 0); ImageDraw.Draw(im).polygon(pts, fill=255)
    return ndi.gaussian_filter(np.asarray(im).astype(np.float32) / 255.0, blur)


# the caster: a tight hand-drawn silhouette (robe, sleeves, head, raised arm) with a few px of margin, traced on the clean plate (grid view of the last frame);
# the caster barely moves - only the right arm and the brush do, so the arm gets a wider berth
def caster_zone():
    body = poly([(560, 352), (612, 358), (624, 395), (676, 372), (688, 400), (680, 452), (662, 488), (642, 506), (652, 530), (678, 590), (702, 650), (718, 720),
                 (418, 720), (438, 670), (468, 625), (498, 600), (518, 565), (516, 548), (494, 560), (466, 548), (450, 500), (476, 458), (508, 424), (538, 408), (546, 374)], 5)
    return np.clip(body, 0, 1)

CASTER = caster_zone()
# the bear's start place (it is swallowed, so a loose box is enough; dark fur specks only)
BEAR = poly([(900, 150), (1190, 150), (1210, 360), (900, 380)], 10)


def classes(rgb):
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    mx = rgb.max(2); mn = rgb.min(2); sat = (mx - mn) / np.maximum(mx, 1e-4)
    blue = ((b - r) > 0.075) & (b >= g - 0.03)
    cream = (mx > 0.84) & (sat < 0.30)
    ink = (mx < 0.17) & ((b - r) > -0.035)
    return blue, cream, ink, mx


def disk(r):
    y, x = np.ogrid[-r:r + 1, -r:r + 1]
    return (x * x + y * y) <= r * r


# where the caster's hand and brush are (x0, y0, x1, y1): the brush swings up from the hip to the right of his head and stays there
HAND_BOX = (596, 250, 740, 440)


def matte(rgb, plate, hand=None):
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    blue, cream, ink, mx = classes(rgb)
    pd = np.abs(rgb - plate).sum(2)
    # the plate only rules in the sky band (the last frames still hold the front waves at the bottom, so it is no clean plate there)
    differs = (pd > 0.16) | (yy > 170)
    # clean the blue: drop speckle
    blue = blue & ndi.binary_opening(blue, structure=disk(1))
    blue_c = ndi.binary_closing(blue, structure=disk(2))
    near = ndi.distance_transform_edt(~blue_c)
    # below the sky band a bright pixel is foam or an outline wherever it is (big white foam fields are far from any blue); in the sky band it has to hug blue and differ from the plate
    keep_cream = cream & differs & np.where(yy > 170, near <= 70, near <= CREAM_NEAR)
    # ink: big components, or touching blue
    lab, n = ndi.label(ink, structure=np.ones((3, 3)))
    ink_keep = np.zeros_like(ink)
    if n:
        area = ndi.sum(ink, lab, np.arange(1, n + 1))
        big = np.zeros(n + 1, bool); big[1:] = area >= INK_AREA
        ink_keep = big[lab]
    ink_keep |= ink & (near <= INK_NEAR)
    ink_keep &= differs
    # the brush: the near-black component(s) that reach into the hand box (the hair is in it too - both are the game's, not the spell's)
    brush = np.zeros_like(ink)
    seed_ids = np.unique(lab[HAND_BOX[1]:HAND_BOX[3], HAND_BOX[0]:HAND_BOX[2]]); seed_ids = seed_ids[seed_ids > 0]
    for s in seed_ids:
        comp = lab == s
        if comp.sum() < 4500:       # a stroke of ink is not the brush: only the small components in the box
            brush |= comp
    # the bristle head is a separate dark blob beyond the tan ferrule: everything dark within 48 px of the handle goes too
    brush_head = (ndi.distance_transform_edt(~brush) <= 48) & (mx < 0.32) if brush.any() else np.zeros_like(brush)
    brush = ndi.binary_dilation(brush, structure=disk(12))
    core = blue | keep_cream | (ink_keep & ~brush)
    # the water silhouette: close the thin light lines inside the waves (anti-aliased, they fail every class), fill small holes
    # the picture's left / right / bottom edge counts as solid, so the lines between arcs that run off the frame are still holes (top stays open: the sky)
    P = 12
    cp = np.pad(core, ((0, P), (P, P)), constant_values=True); cp = np.pad(cp, ((P, 0), (0, 0)), constant_values=False)
    sil = ndi.binary_closing(cp, structure=disk(3))
    holes = ndi.binary_fill_holes(sil) & ~sil
    hl, hn = ndi.label(holes)
    if hn:
        ha = ndi.sum(holes, hl, np.arange(1, hn + 1))
        okh = np.zeros(hn + 1, bool); okh[1:] = ha < 2500
        sil |= okh[hl]
    sil = sil[P:P + H, P:P + W]
    # what is not water inside it: warm brown / dark fur (ground, robe, bear) - never blue, never ink
    warm = ((r - b) > 0.035) & (mx < 0.62) & ~blue & ~(ink_keep & ~brush)
    sil &= ~warm
    # the caster: only strongly blue pixels (and the cream line hugging them) survive in his zone
    strong = blue | (keep_cream & (near <= 6))
    inzone = CASTER > 0.5
    sil = np.where(inzone, sil & strong, sil)
    sil &= ~((brush | brush_head) & ~blue)
    # the bear: dark fur specks in its zone are not ink unless they sit in water
    big_ink = np.zeros_like(ink)
    if n:
        bigger = np.zeros(n + 1, bool); bigger[1:] = area >= 1400; big_ink = bigger[lab]
    sil &= ~((BEAR > 0.5) & ~blue & (near > 4) & (mx < 0.5) & ~big_ink)
    # dust: tiny islands
    lab2, n2 = ndi.label(sil, structure=np.ones((3, 3)))
    if n2:
        a2 = ndi.sum(sil, lab2, np.arange(1, n2 + 1))
        small = np.zeros(n2 + 1, bool); small[1:] = a2 < 30
        sil &= ~small[lab2]
    return sil

def finish(rgb, m):
    """soft straight alpha + colour decontamination + colour extension under alpha 0."""
    a = ndi.gaussian_filter(m.astype(np.float32), 0.8)
    a = np.clip((a - 0.15) / 0.7, 0, 1)
    core = ndi.binary_erosion(m, iterations=1)
    core = core if core.any() else m
    # nearest core pixel colour (for edge pixels and for the transparent area)
    idx = ndi.distance_transform_edt(~core, return_distances=False, return_indices=True)
    ext = rgb[idx[0], idx[1]]
    use_ext = (~core)
    out = np.where(core[..., None], rgb, ext)
    # keep the original colour where the alpha is solid, extension where it is not
    out = np.where((a > 0.98)[..., None] & m[..., None], rgb, out)
    return out, a


def encode(rgb, a):
    return Image.fromarray(np.dstack([np.clip(rgb * 255 + 0.5, 0, 255), np.clip(a * 255 + 0.5, 0, 255)]).astype(np.uint8), 'RGBA')


# per-frame hand positions (the brush grip), measured by eye on the clip and interpolated; (frame, x, y)
HAND = [(1, 632, 425), (8, 628, 410), (16, 625, 390), (30, 620, 350), (45, 618, 335), (60, 618, 335), (151, 618, 335)]


def hand_at(i):
    xs = [h[0] for h in HAND]
    return (float(np.interp(i, xs, [h[1] for h in HAND])), float(np.interp(i, xs, [h[2] for h in HAND])))


def process(i, plate):
    rgb = load(i)
    hx, hy = hand_at(i)
    return rgb, matte(rgb, plate, (int(hx), int(hy)))


def main():
    decode()
    n_raw = len(glob.glob(RAW + r'\r_*.png'))
    plate = np.median(np.stack([load(i) for i in range(n_raw - PLATE_FRAMES + 1, n_raw + 1)]), 0)
    os.makedirs(FRAMES, exist_ok=True)
    for f in glob.glob(FRAMES + r'\f_*.png'):
        os.remove(f)
    alive = []
    results = []
    for i in range(1, n_raw + 1):
        rgb = load(i)
        hx, hy = hand_at(i)
        m = matte(rgb, plate, (int(hx), int(hy)))
        results.append((rgb, m))
        alive.append(int(m.sum()))
    # trim the dead tail: the last frame whose area is still above the floor, plus a short fade is already in the video
    live = max([k for k, v in enumerate(alive) if v > TAIL_AREA] or [len(alive) - 1])
    count = min(len(alive), live + 1 + TAIL_FADE)
    print('frames', n_raw, 'live until', live + 1, 'written', count, 'area first/live', alive[0], alive[live])
    for k in range(count):
        rgb, m = results[k]
        out, a = finish(rgb, m)
        if k > live:
            a = a * (1.0 - (k - live) / float(TAIL_FADE))        # the last frames fade out (the last written one is 0) so the card never pops off
        encode(out, a).save(FRAMES + r'\f_%04d.png' % k)
    sheet(count)


def sheet(count, picks=None):
    picks = picks or [int(round(t * (count - 1))) for t in (0.0, 0.12, 0.28, 0.45, 0.62, 0.8)]
    cw, ch = 640, 360
    img = Image.new('RGB', (cw * 3, ch * 2), (255, 0, 255))
    for j, k in enumerate(picks):
        f = Image.open(FRAMES + r'\f_%04d.png' % k).convert('RGBA')
        bg = Image.new('RGB', (W, H), (255, 0, 255)); bg.paste(f, (0, 0), f)
        img.paste(bg.resize((cw, ch), Image.LANCZOS), ((j % 3) * cw, (j // 3) * ch))
        ImageDraw.Draw(img).text(((j % 3) * cw + 6, (j // 3) * ch + 4), 'f%04d' % k, fill=(255, 255, 255))
    img.save(OUT + r'\matte_check.jpg', quality=88)


def test(ids, name='matte_test.jpg'):
    """quick look: the given frame indices (0-based, = video frame - 1) over magenta, no files written but the sheet."""
    decode()
    n_raw = len(glob.glob(RAW + r'\r_*.png'))
    plate = np.median(np.stack([load(i) for i in range(n_raw - PLATE_FRAMES + 1, n_raw + 1)]), 0)
    cw, ch = 960, 540
    cols = 2
    img = Image.new('RGB', (cw * cols, ch * ((len(ids) + cols - 1) // cols)), (255, 0, 255))
    for j, k in enumerate(ids):
        rgb, m = process(k + 1, plate)
        out, a = finish(rgb, m)
        f = encode(out, a)
        bg = Image.new('RGB', (W, H), (255, 0, 255)); bg.paste(f, (0, 0), f)
        img.paste(bg.resize((cw, ch), Image.LANCZOS), ((j % cols) * cw, (j // cols) * ch))
    img.save(os.environ.get('U_K_TEST', OUT + r'\\' + name), quality=88)


if __name__ == '__main__':
    if '--test' in sys.argv:
        test([int(x) for x in sys.argv[sys.argv.index('--test') + 1].split(',')])
    elif '--sheet-only' in sys.argv:
        n = len(glob.glob(FRAMES + r'\f_*.png')); sheet(n)
    else:
        main()
