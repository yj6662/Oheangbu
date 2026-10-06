# -*- coding: utf-8 -*-
"""SPEC-UI-THEME-308 (D308-15) - the CONSUMER side of the kit, in numpy: read the staged atlas + its json + the lattice
tiles and lay a sprite out the way Unity's Image does (Simple / Sliced / Tiled with borders, tiles laid from the bottom-left,
2 texels per design px). Used by the sample sheet and by the checks; a mock of another Spec can import it the same way.

    kit = Kit()
    kit.simple('pip_petal', s)                  Image.Type.Simple at s x (s = sheet px per design px)
    kit.rect('frame_mini', 312, 228, s)         Sliced or Tiled, whichever the atlas json says, at w x h design px
    kit.strip('line_najeon', 1792, s)           a horizontally tiled line of that length
    kit.lattice('bit', 88, 88, s)               a lattice tile as a Tiled Image (coverage mask; tint it with a token)
All return straight-alpha float RGBA (or a float mask). Nothing is written.
"""
import json
import os

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, '..', '..'))
KIT = os.path.join(ROOT, 'Tools', 'Unity', 'Stage308_theme', '_ProjectAssets', 'Art', 'UI', 'UI308', 'Theme')
FAMILIES = ('tti', 'yongja', 'aja', 'wanja', 'jeongja', 'bit', 'sutdae', 'gwigap')


def resample(img, f):
    """texel-res straight-alpha RGBA (or a 2-D mask) -> the same picture at f x (premultiplied filtering)"""
    if abs(f - 1) < 1e-9:
        return img
    mask = img.ndim == 2
    a = img[..., None] if mask else np.dstack([img[..., :3] * img[..., 3:4], img[..., 3]])
    h, w = a.shape[:2]
    nw, nh = max(1, int(round(w * f))), max(1, int(round(h * f)))
    if abs(f - .5) < 1e-9 and w % 2 == 0 and h % 2 == 0:
        out = a.reshape(h // 2, 2, w // 2, 2, a.shape[2]).mean(axis=(1, 3))
    else:
        flt = Image.BILINEAR if f > 1 else Image.BOX if abs(1 / f - round(1 / f)) < 1e-9 else Image.BILINEAR
        out = np.dstack([np.asarray(Image.fromarray(a[..., i].astype(np.float32), 'F').resize((nw, nh), flt), float)
                         for i in range(a.shape[2])])
    if mask:
        return np.clip(out[..., 0], 0, 1)
    out[..., :3] = out[..., :3] / np.maximum(out[..., 3:4], 1e-6)
    return np.clip(out, 0, 1)


def index(n, a, b, n_src, tiled, from_end):
    """dest texel -> source texel along one axis of a bordered sprite; a = border at index 0, b = border at the far end.
    from_end: Unity lays tiles from the bottom, the picture's rows run from the top."""
    i = np.arange(n)
    mid_src, mid_dst = n_src - a - b, n - a - b
    if a + b == 0:
        return (n_src - 1 - ((n - 1 - i) % n_src)) if (tiled and from_end) else i % n_src if tiled else np.clip(i, 0, n_src - 1)
    if tiled:
        mid = (n_src - b - 1 - ((n - b - 1 - i) % mid_src)) if from_end else a + (i - a) % mid_src
    else:
        mid = a + np.clip(((i - a + .5) * mid_src / max(1, mid_dst)).astype(int), 0, mid_src - 1)
    return np.where(i < a, i, np.where(i >= n - b, n_src - (n - i), mid))


class Kit:
    def __init__(self, root=KIT):
        with open(os.path.join(root, 'theme308_atlas.json'), encoding='utf-8') as f:
            self.meta = json.load(f)
        self.at8 = np.asarray(Image.open(os.path.join(root, 'theme308_atlas.png')).convert('RGBA'))
        self.at = self.at8.astype(float) / 255.0
        self.cells = {c['name']: c for c in self.meta['cells']}
        self.by_kit_name = {c['kit']: c for c in self.meta['cells']}
        self.tiles = {n: np.asarray(Image.open(os.path.join(root, 'Lattice', 'lat308_%s.png' % n)).convert('LA'), float)[..., 1] / 255.0
                      for n in FAMILIES}

    def tex(self, name):
        x, y, w, h = self.cells[name]['rect']
        return self.at[y:y + h, x:x + w].copy()

    def simple(self, name, s=1.0, tint=None):
        t = self.tex(name)
        if tint is not None:
            t[..., :3] = tint
        return resample(t, s / 2.0)

    def rect(self, name, w, h, s=1.0, tint=None):
        c = self.cells[name]
        t = self.tex(name)
        l, b, r, tp = c['border_texels']
        W, H = int(round(w * 2)), int(round(h * 2))
        tiled = c['use'] == 'Tiled'
        out = t[index(H, tp, b, t.shape[0], tiled, True)][:, index(W, l, r, t.shape[1], tiled, False)].copy()
        if c.get('fill_center') is False:
            out[tp:H - b, l:W - r, 3] = 0.0
        if tint is not None:
            out[..., :3] = tint
        return resample(out, s / 2.0)

    def strip(self, name, length, s=1.0, tint=None):
        return self.rect(name, length, self.cells[name]['design_px'][1], s, tint)

    def lattice(self, fam, w, h, s=1.0):
        t = self.tiles[fam]
        W, H = int(round(w * 2)), int(round(h * 2))
        return resample(t[index(H, 0, 0, t.shape[0], True, True)][:, index(W, 0, 0, t.shape[1], True, False)], s / 2.0)
