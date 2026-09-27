"""#293 candidate leaf-card fix: colour bleeding into transparent texels (pull-push), alpha unchanged.

The candidate vegetation leaf atlases store black RGB in their transparent area (72-91% of texels). With alpha
clipping, bilinear filtering and mipmaps pull that black into leaf edges, so near cards get dark halos and far
LODs darken into black dots. Copies with the nearest leaf colour bled outward keep every opaque texel identical.
Sources are read only; copies go to Reworld292/Highlands293/Textures/Leaves (candidate only) with leaves.json.
"""
from pathlib import Path
import json, re, os, hashlib
import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
PROJECT = ROOT / 'Oheangbu'
MATS = PROJECT / 'Assets/_Project/Art/World/Reworld292/Materials'
OUT = PROJECT / 'Assets/_Project/Art/World/Reworld292/Highlands293/Textures/Leaves'


def guid_index():
    index = {}
    for dp, _, files in os.walk(PROJECT / 'Assets'):
        for f in files:
            if f.lower().endswith(('.png.meta', '.tga.meta', '.psd.meta', '.tif.meta', '.tiff.meta')):
                p = Path(dp) / f
                m = re.search(r'guid: (\w+)', p.read_text(encoding='utf-8', errors='ignore')[:400])
                if m: index[m.group(1)] = p.with_suffix('')
    return index


def pull_push(rgb, weight):
    """Fill zero-weight texels from progressively coarser weighted averages."""
    levels = [(rgb * weight[..., None], weight)]
    while min(levels[-1][1].shape) > 1:
        c, w = levels[-1]
        h, wd = w.shape; h2, w2 = (h + 1) // 2, (wd + 1) // 2
        cp = np.zeros((h2 * 2, w2 * 2, 3)); cp[:h, :wd] = c
        wp = np.zeros((h2 * 2, w2 * 2)); wp[:h, :wd] = w
        levels.append((cp.reshape(h2, 2, w2, 2, 3).sum((1, 3)), wp.reshape(h2, 2, w2, 2).sum((1, 3))))
    colour = levels[-1][0] / np.maximum(levels[-1][1], 1e-6)[..., None]
    for c, w in reversed(levels[:-1]):
        h, wd = w.shape
        up = np.repeat(np.repeat(colour, 2, 0), 2, 1)[:h, :wd]
        own = c / np.maximum(w, 1e-6)[..., None]
        colour = np.where((w > 1e-6)[..., None], own, up)
    return colour


def main():
    index = guid_index(); OUT.mkdir(parents=True, exist_ok=True)
    by_texture = {}
    for mat in sorted(MATS.glob('Vegetation_*.mat')):
        text = mat.read_text(encoding='utf-8')
        clip = re.search(r'- _AlphaClip: ([\d.]+)', text)
        base = re.search(r'- _BaseMap:\s*\n\s*m_Texture: \{fileID: \d+, guid: (\w+)', text)
        if not clip or float(clip.group(1)) < .5 or not base or base.group(1) not in index: continue
        by_texture.setdefault(index[base.group(1)], []).append(mat)
    manifest = []
    for source, mats in sorted(by_texture.items()):
        image = Image.open(source)
        if image.mode not in ('RGBA', 'LA', 'P'): continue
        a = np.asarray(image.convert('RGBA')).astype(np.float64)
        alpha = a[..., 3]
        if (alpha < 8).mean() < .02: continue  # no meaningful transparent area
        # seeds = fully opaque leaf texels; semi-transparent fringe texels (often dark, premultiplied-looking)
        # take the neighbouring leaf colour too, so filtering never pulls black into the silhouette
        keep = alpha >= min(250, alpha.max() * .95)
        colour = pull_push(a[..., :3], keep.astype(np.float64))
        out = a.copy()
        out[..., :3] = np.where(keep[..., None], a[..., :3], colour)
        name = hashlib.md5(str(source.relative_to(PROJECT)).encode()).hexdigest()[:8] + '_' + source.stem + '.png'
        Image.fromarray(np.clip(np.round(out), 0, 255).astype(np.uint8), 'RGBA').save(OUT / name, optimize=False)
        src_rel = str(source.relative_to(PROJECT)).replace('\\', '/')
        manifest.append(dict(source=src_rel, dilated='Assets/_Project/Art/World/Reworld292/Highlands293/Textures/Leaves/' + name,
                             materials=[str(m.relative_to(PROJECT)).replace('\\', '/') for m in mats],
                             transparent=round(float((alpha < 8).mean()), 3)))
        print(src_rel, '->', name, len(mats), 'materials')
    (OUT / 'leaves.json').write_text(json.dumps(dict(items=manifest), indent=1), encoding='utf-8')  # JsonUtility-readable
    print(len(manifest), 'textures')


if __name__ == '__main__':
    main()
