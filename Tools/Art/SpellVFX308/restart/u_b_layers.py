"""D308-153 route B for 우: the concept u_a's water ring as two painted cards (far arc behind the bodies, near arc in front).
Inputs: the concept and the Adobe masks in Art/SpellVFX308/Restart/u_B/src (mask_ring = water + ground, mask_bear).
- The bear and the caster are cut out of the ring; their holes are filled from the surrounding water (push-pull fill), alpha included,
  so the ring is whole behind them.
- The flat ground inside the ring is removed with a feathered ellipse (Adobe could not select it on its own).
- far = the upper part of the ring (behind the caster and the target), near = the lower front part (in front of the caster's legs).
  python Tools/Art/SpellVFX308/restart/u_b_layers.py"""
import numpy as np
from PIL import Image
from scipy import ndimage

ROOT = r'C:\Users\yj666\Oheangbu\Art\SpellVFX308\Restart\u_B'
SRC = r'C:\Users\yj666\Oheangbu\Art\SpellVFX308\Vol2Vfx\free_showcase\batch2\concepts\u_a.png'
L = lambda n: np.asarray(Image.open(ROOT + '\\src\\' + n).convert('L')).astype(np.float32) / 255

rgb = np.asarray(Image.open(SRC).convert('RGB')).astype(np.float32) / 255
H, W = rgb.shape[:2]
ring, bear = L('mask_ring.png'), L('mask_bear.png')
yy, xx = np.mgrid[0:H, 0:W].astype(np.float32)

# the caster: the hole the ring mask left in the lower middle (ring < .5 inside his box)
box = (xx > 560) & (xx < 1000) & (yy > 470)
caster = ((ring < 0.5) & box).astype(np.float32)
caster = ndimage.binary_dilation(caster > 0.5, iterations=6).astype(np.float32)
bearh = ndimage.binary_dilation(bear > 0.5, iterations=8).astype(np.float32)
hole = np.clip(caster + bearh, 0, 1)

# the ground inside the ring: a feathered ellipse
ell = ((xx - 905) / 585) ** 2 + ((yy - 520) / 170) ** 2
ground = np.clip((1.0 - ell) / 0.12, 0, 1)

alpha = np.clip(ring, 0, 1) * (1 - hole)
alpha = alpha * (1 - ground)

def fill(img, known):
    """push-pull: fill unknown pixels from the known ones, coarse to fine (normalised Gaussian at growing scales)."""
    out = img.copy(); k = known.astype(np.float32)
    acc = np.zeros_like(img); wsum = np.zeros_like(k)
    for s in (2, 4, 8, 16, 32, 64):
        w = ndimage.gaussian_filter(k, s)
        if img.ndim == 3:
            v = np.stack([ndimage.gaussian_filter(img[..., c] * k, s) for c in range(img.shape[2])], -1)
            est = v / np.maximum(w[..., None], 1e-4)
            take = (wsum < 0.5) & (w > 0.02)
            acc[take] = est[take]
        else:
            v = ndimage.gaussian_filter(img * k, s); est = v / np.maximum(w, 1e-4)
            take = (wsum < 0.5) & (w > 0.02); acc[take] = est[take]
        wsum[take] = 1
    unk = known < 0.5
    out[unk] = acc[unk]
    return out

# r2: clone-stamp the holes from the waves beside them (r1's push-pull fill was a grey smear): the bear's hole from the waves 330 px to its left,
# the caster's from the waves 430 px to his right; feathered seams
ring_c = np.clip(ring, 0, 1)
rgb_f = rgb.copy(); a_fill = ring_c.copy()
for m, dx in ((bearh, -330), (caster, 430)):
    soft = ndimage.gaussian_filter(m, 6)
    src_rgb = np.roll(rgb, -dx, axis=1) if dx < 0 else np.roll(rgb, -dx, axis=1)
    src_a = np.roll(ring_c * (1 - np.roll(hole, 0, axis=1)), -dx, axis=1)
    rgb_f = rgb_f * (1 - soft[..., None]) + src_rgb * soft[..., None]
    a_fill = a_fill * (1 - soft) + src_a * soft
alpha = np.where(hole > 0.02, a_fill, ring_c) * (1 - ground)

def save(name, a):
    Image.fromarray((np.dstack([rgb_f, a]) * 255 + 0.5).astype(np.uint8), 'RGBA').save(ROOT + '\\' + name)

# far / near split along a soft line under the ring's middle (the near arc starts below y ~ 600 in the middle, higher at the sides)
split = 600 - 120 * np.clip(np.abs(xx - 836) / 836, 0, 1) ** 2
near_w = np.clip((yy - split) / 60 + 0.5, 0, 1)
save('u_b_far.png', alpha * (1 - near_w))
save('u_b_near.png', alpha * near_w)
save('u_b_whole.png', alpha)
prev = Image.new('RGB', (W, H), (255, 0, 255)); w_ = Image.open(ROOT + '\\u_b_whole.png'); prev.paste(w_, (0, 0), w_)
f = Image.open(ROOT + '\\u_b_far.png'); n = Image.open(ROOT + '\\u_b_near.png')
pf = Image.new('RGB', (W, H), (40, 40, 40)); pf.paste(f, (0, 0), f)
pn = Image.new('RGB', (W, H), (40, 40, 40)); pn.paste(n, (0, 0), n)
sheet = Image.new('RGB', (W, H + H // 2))
sheet.paste(prev, (0, 0)); sheet.paste(pf.resize((W // 2, H // 2)), (0, H)); sheet.paste(pn.resize((W // 2, H // 2)), (W // 2, H))
sheet.resize((W // 2, (H + H // 2) // 2)).save(ROOT + '\\u_b_layers_preview.jpg', quality=85)
print('ok', W, H)
