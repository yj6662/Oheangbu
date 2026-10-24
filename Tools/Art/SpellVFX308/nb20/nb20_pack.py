#!/usr/bin/env python3
"""NB20 (D308-150 V6): packs the Blender + After Effects ink elements into the two atlases the engine plays.
  python Tools/Art/SpellVFX308/nb20/nb20_pack.py [--preview]

In : Art/SpellVFX308/Vol2Vfx/nb20/src/ae/bleed/<element>_0000.tif (After Effects: roughened / bled RGBA, premultiplied on black)
     Art/SpellVFX308/Vol2Vfx/nb20/src/ae/dry/{plates,cracks,fibre}_0000.tif (After Effects drying maps)
     Art/SpellVFX308/Vol2Vfx/nb20/src/blender/elements.json (sizes in metres)
Out: Oheangbu/Assets/_Project/Art/SpellVFX308/Vol2Vfx/NB20/NB20_Sheet.png  (linear RGBA8, 4096 x 4096, 4 x 16 cells of 1024 x 256, 8 px gutter)
        R ink tone (0 wet black .. 1 driest ink) | G dry pigment mask | B outside distance sqrt(d / 160 texels) (the paper rim) | A coverage
     .../NB20_SheetTime.png  (linear RGBA8, same layout)
        R reveal order (0 first .. 1 last laid) | G drying order (0 dries first: the cracks, then the edges, then fragment by fragment; spread evenly over the covered area)
        B fragment id (random per fragment) | A 1
     Art/SpellVFX308/Vol2Vfx/nb20/src/pack/atlas.json + preview_*.png
Cell index = element * 5 + body (Wood Fire Earth Metal Water), elements in the order of ELEMENTS below; then fleck0..3 = 50..53, ignite = 54."""
import json
import sys
from pathlib import Path

import numpy as np
from PIL import Image
from scipy import ndimage

REPO = Path(__file__).resolve().parents[4]
SRC = REPO / 'Art/SpellVFX308/Vol2Vfx/nb20/src'
DST = REPO / 'Oheangbu/Assets/_Project/Art/SpellVFX308/Vol2Vfx/NB20'
PACK = SRC / 'pack'
ELEMENTS = ['head', 'tail', 'contact', 'supportA', 'supportB', 'fanRadial', 'fanArc', 'ringArc', 'standing', 'pulse']
SHARED = ['fleck0', 'fleck1', 'fleck2', 'fleck3', 'ignite']
BODIES = ['Wood', 'Fire', 'Earth', 'Metal', 'Water']
COLS, ROWS, CW, CH, GUT = 4, 16, 1024, 256, 8
FRAG_PX_PER_M = 467.0       # the plates map: a 70 px cell = 15 cm of ink, on every element alike (r2: 6 cm fragments read as pixel noise at play distance)


def load_tif(p):
    im = Image.open(p)
    tags = im.tag_v2
    if tags.get(277) == 4 and tags.get(259) == 1 and len(tags.get(273)) == 1:
        # After Effects writes RGBA with ExtraSamples = 0 ("unspecified"), which PIL drops: read the uncompressed strip directly
        w, h = tags[256], tags[257]
        raw = Path(p).read_bytes()[tags[273][0]:tags[273][0] + w * h * 4]
        a = np.frombuffer(raw, np.uint8).reshape(h, w, 4).astype(np.float32) / 255.0
    else:
        a = np.asarray(im.convert('RGBA')).astype(np.float32) / 255.0
    al = a[..., 3:4]
    rgb = np.where(al > 0.004, np.clip(a[..., :3] / np.maximum(al, 1e-4), 0, 1), 0.0)       # After Effects wrote premultiplied (matted on black)
    return np.concatenate([rgb, al], 2)


def resize(a, w, h):
    out = []
    for c in range(a.shape[2]):
        im = Image.fromarray((np.clip(a[..., c], 0, 1) * 65535).astype(np.uint16).astype(np.int32), mode='I')
        im = im.resize((w, h), Image.LANCZOS)
        out.append(np.asarray(im).astype(np.float32) / 65535.0)
    return np.clip(np.stack(out, 2), 0, 1)


def fill_out(v, mask):
    """push the values of the covered texels outward (nearest) so mips and the bilinear edge never read the empty black outside"""
    if mask.all() or not mask.any():
        return v
    idx = ndimage.distance_transform_edt(~mask, return_distances=False, return_indices=True)
    return v[tuple(idx)]


def tile_sample(img, w, h, L, W, seed):
    """sample a shared 2048^2 drying map at a fixed world scale over an element L x W metres, with a per-element offset"""
    S = img.shape[0]
    xs = (np.arange(w) / w * L * FRAG_PX_PER_M + seed * 397) % S
    ys = (np.arange(h) / h * W * FRAG_PX_PER_M + seed * 211) % S
    return img[np.ix_(ys.astype(int), xs.astype(int))]


def main():
    meta = json.loads((SRC / 'blender/elements.json').read_text(encoding='utf-8'))
    plates = np.asarray(Image.open(SRC / 'ae/dry/plates_0000.tif').convert('L')).astype(np.float32) / 255.0
    cracks = np.asarray(Image.open(SRC / 'ae/dry/cracks_0000.tif').convert('L')).astype(np.float32) / 255.0
    fibre = np.asarray(Image.open(SRC / 'ae/dry/fibre_0000.tif').convert('L')).astype(np.float32) / 255.0
    sheet = np.zeros((ROWS * CH, COLS * CW, 4), np.float32)
    time = np.zeros((ROWS * CH, COLS * CW, 4), np.float32); time[..., 3] = 1
    cells = {}
    jobs = []
    for e, name in enumerate(ELEMENTS):
        for b, body in enumerate(BODIES):
            jobs.append((e * 5 + b, '%s_%s' % (name, body), meta['elements'][name]))
    for k, name in enumerate(SHARED):
        jobs.append((50 + k, name, meta['shared'][name]))
    cw, ch = CW - 2 * GUT, CH - 2 * GUT
    for cell, name, spec in jobs:
        p = SRC / 'ae/bleed' / (name + '_0000.tif')
        if not p.exists():
            print('MISSING', p); continue
        a = resize(load_tif(p), cw, ch)
        cov = a[..., 3]
        inside = cov > 0.5
        # B: the paper rim field - distance OUT of the ink in texels of the packed cell, sqrt-encoded: B = sqrt(d / 160) (fine near the edge, reaches 160 texels
        # so a 3.5 px rim still exists when the element is small on screen; the shader decodes d = B^2 x 160)
        # r2b: only the OUTER silhouette gets the rim (the reference's "black body, white rim"); the hollow channels and dry streaks INSIDE the stroke stay open ground
        # (a rim in every gap turned the whole stroke into salt-and-pepper). Silhouette = the ink closed over gaps up to ~14 texels, holes filled.
        yy, xx = np.mgrid[-7:8, -7:8]
        disk = (xx * xx + yy * yy) <= 49
        sil = ndimage.binary_fill_holes(ndimage.binary_closing(np.pad(inside, 8), structure=disk))[8:-8, 8:-8] | inside
        dout = ndimage.distance_transform_edt(~sil)
        rimf = np.where(sil & ~inside, 1.0, np.sqrt(np.clip(dout / 160.0, 0, 1)))
        tone = fill_out(a[..., 0], inside); pig = fill_out(a[..., 1], inside); rev = fill_out(a[..., 2], inside)
        # drying order: the crack lines go first, then the thin edges, then the fragments one by one (the AE plates' random value), with the paper fibre in it
        L, W = spec['L'], spec['W']
        pl = tile_sample(plates, cw, ch, L, W, cell); cr = tile_sample(cracks, cw, ch, L, W, cell); fb = tile_sample(fibre, cw, ch, L, W, cell)
        din = ndimage.distance_transform_edt(inside)
        thick = max(2.0, np.percentile(din[inside], 90)) if inside.any() else 4.0
        edge = np.clip(din / thick, 0, 1)
        crack = np.clip((0.30 - cr) / 0.30, 0, 1)                    # the dark borders of the crystals = the crack lines
        # r2: the ink recedes from its thin edges first (edge 0.55), then breaks along a few cracks into big fragments that go one by one
        # r3: drying = the ink RECEDES from its edges along the paper fibre (edge 0.70 + fibre 0.20), a few long cracks open first (0.25) and the last bits
        # go as big fragments (plates 0.10) - fragment-first dissolve read as digital blocks at play distance
        order = 0.70 * edge + 0.20 * fb + 0.10 * pl - 0.25 * crack
        if inside.any():
            v = order[inside]
            ranks = np.argsort(np.argsort(v)).astype(np.float32) / max(1, v.size - 1)     # spread evenly over the covered area: dry 0.5 = half the ink gone
            o2 = np.zeros_like(order); o2[inside] = ranks
            order = fill_out(o2, inside)
        # r4: B of the order map = the distance INTO the ink from its edge, sqrt(texels / 160): the shader lays the body's dry pigment (L* 60) as a thin broken edge band of
        # ~2.5 SCREEN pixels on every stroke (the bright layer absorbed into the stroke, not a paper outline around it)
        frag = np.sqrt(np.clip(din / 160.0, 0, 1))
        r0, c0 = divmod(cell, COLS)
        y0, x0 = r0 * CH + GUT, c0 * CW + GUT
        sheet[y0:y0 + ch, x0:x0 + cw] = np.stack([tone, pig, rimf, cov], 2)
        time[y0:y0 + ch, x0:x0 + cw, 0] = rev
        time[y0:y0 + ch, x0:x0 + cw, 1] = order
        time[y0:y0 + ch, x0:x0 + cw, 2] = frag
        # gutters: copy the cell's border texels outward (mips), with coverage 0
        for arr in (sheet, time):
            blk = arr[y0:y0 + ch, x0:x0 + cw]
            pad = np.pad(blk, ((GUT, GUT), (GUT, GUT), (0, 0)), mode='edge')
            arr[y0 - GUT:y0 + ch + GUT, x0 - GUT:x0 + cw + GUT] = pad
        sheet[y0 - GUT:y0, x0 - GUT:x0 + cw + GUT, 3] = 0; sheet[y0 + ch:y0 + ch + GUT, x0 - GUT:x0 + cw + GUT, 3] = 0
        sheet[y0 - GUT:y0 + ch + GUT, x0 - GUT:x0, 3] = 0; sheet[y0 - GUT:y0 + ch + GUT, x0 + cw:x0 + cw + GUT, 3] = 0
        drawn = inside.sum()
        cells[name] = dict(cell=cell, L=L, W=W, ink=float(((pig < 0.5) & inside).sum() / max(1, drawn)), coverage=float(drawn / inside.size))
    DST.mkdir(parents=True, exist_ok=True); PACK.mkdir(parents=True, exist_ok=True)
    # Unity reads PNG rows top-down as the texture's TOP: cell row 0 is the top row of the image; the shader maps it
    Image.fromarray((np.clip(sheet, 0, 1) * 255 + 0.5).astype(np.uint8), 'RGBA').save(DST / 'NB20_Sheet.png', optimize=False)
    Image.fromarray((np.clip(time, 0, 1) * 255 + 0.5).astype(np.uint8), 'RGBA').save(DST / 'NB20_SheetTime.png', optimize=False)
    (PACK / 'atlas.json').write_text(json.dumps(dict(cols=COLS, rows=ROWS, cw=CW, ch=CH, gutter=GUT, cells=cells), indent=1), encoding='utf-8')
    inkshare = [v['ink'] for v in cells.values()]
    print('packed %d cells -> %s | ink : pigment of drawn pixels = %.0f : %.0f' % (len(cells), DST, 100 * np.mean(inkshare), 100 - 100 * np.mean(inkshare)))
    if '--preview' in sys.argv:
        prev = np.zeros((ROWS * CH, COLS * CW, 3), np.float32) + np.array([88, 62, 52]) / 255.0
        ink = (12 + 40 * sheet[..., 0:1]) / 255.0
        hue = np.array([0.6, 0.6, 0.6])
        col = np.where(sheet[..., 1:2] > 0.5, hue, ink)
        rim = (sheet[..., 3:4] < 0.5) & (sheet[..., 2:3] * 24 < 3.5) & (sheet[..., 2:3] > 0)
        prev = np.where(sheet[..., 3:4] > 0.5, col, prev); prev = np.where(rim, np.array([0.86, 0.83, 0.75]), prev)
        Image.fromarray((prev * 255).astype(np.uint8)).resize((2048, 2048), Image.LANCZOS).save(PACK / 'preview_sheet.png')
        Image.fromarray((time[..., :3] * 255).astype(np.uint8)).resize((2048, 2048), Image.LANCZOS).save(PACK / 'preview_time.png')


if __name__ == '__main__':
    main()
