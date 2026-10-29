"""Jujak308 texture grade (system Python 3 + numpy + Pillow; called by build_jujak308.py, runnable alone).

Reads the untouched Meshy PBR textures from Art/Characters/EnemyBodies308/Source/jujak_<part>/ and writes NEW 2048 px files
(never overwrites the originals):
    T_Jujak308_<Part>_base.png    graded base colour (dull vermilion / rust-brown, ink tips from the UV ink masks, celadon plates)
    T_Jujak308_<Part>_normal.png  Meshy normal map, resized
    T_Jujak308_<Part>_rough.png   Meshy roughness map, resized, single channel
Metallic and emission maps are NOT carried over (metallic 0, emission 0 - ART-INK).

  python grade_jujak308.py --out <dir> [--masks <dir with ink_wing.png / ink_tail.png>]
"""
import argparse
import colorsys
import os
import sys
from pathlib import Path

import numpy as np
from PIL import Image

Image.MAX_IMAGE_PIXELS = None
ROOT = Path(__file__).resolve().parents[3]
SRC = ROOT / "Art" / "Characters" / "EnemyBodies308" / "Source"
SIZE = 2048

# Target colour clusters measured from jj_side4 / jj_wingpose3 / jj_front4 (see build report):
#   red  ~ rgb(118,76,56) hsv(19,.52,.46)   dark ~ rgb(64,53,44) hsv(27,.30,.25)   pale ~ rgb(171,155,137) hsv(33,.20,.67)
TARGET_RED_HUE = 18.0 / 360.0
# per-part gains: (saturation gain on warm pixels, value gain, pale compression, ink strength)
PARAMS = {
    "body": dict(sat=1.0, val=1.08, pale_comp=0.20, dark_gain=1.0, ink=0.0),
    "wing": dict(sat=0.88, val=0.82, pale_comp=0.35, dark_gain=0.80, ink=0.88),
    "tail": dict(sat=0.92, val=0.92, pale_comp=0.45, dark_gain=0.88, ink=0.90),
}


def rgb2hsv(a):
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    mx = a.max(-1)
    mn = a.min(-1)
    d = mx - mn
    h = np.zeros_like(mx)
    m = d > 1e-6
    rc = np.where(m, (mx - r) / np.where(m, d, 1), 0)
    gc = np.where(m, (mx - g) / np.where(m, d, 1), 0)
    bc = np.where(m, (mx - b) / np.where(m, d, 1), 0)
    h = np.where(mx == r, bc - gc, np.where(mx == g, 2.0 + rc - bc, 4.0 + gc - rc))
    h = (h / 6.0) % 1.0
    h = np.where(m, h, 0)
    s = np.where(mx > 1e-6, d / np.where(mx > 1e-6, mx, 1), 0)
    return np.stack([h, s, mx], -1)


def hsv2rgb(a):
    h, s, v = a[..., 0], a[..., 1], a[..., 2]
    i = np.floor(h * 6.0)
    f = h * 6.0 - i
    p = v * (1 - s)
    q = v * (1 - s * f)
    t = v * (1 - s * (1 - f))
    i = i.astype(int) % 6
    r = np.choose(i, [v, q, p, p, t, v])
    g = np.choose(i, [t, v, v, q, p, p])
    b = np.choose(i, [p, p, t, v, v, q])
    return np.stack([r, g, b], -1)


def load_rgb(path):
    im = Image.open(path).convert("RGB").resize((SIZE, SIZE), Image.LANCZOS)
    return np.asarray(im, dtype=np.float32) / 255.0


def grade_feather(rgb, prm, ink):
    hsv = rgb2hsv(rgb)
    h, s, v = hsv[..., 0], hsv[..., 1], hsv[..., 2]
    warm = np.clip((s - 0.18) / 0.2, 0, 1)            # 0 for greys / cream, 1 for saturated rust
    dh = ((TARGET_RED_HUE - h + 0.5) % 1.0) - 0.5
    h = (h + dh * 0.35 * warm) % 1.0
    s = s * (prm["sat"] * warm + 0.93 * (1 - warm))
    v = v - np.clip(v - 0.55, 0, None) * prm["pale_comp"]  # pale cream / white tips come down toward warm grey
    dark = np.clip((0.36 - v) / 0.2, 0, 1)
    v = v * (prm["val"] * (1 - dark) + prm["dark_gain"] * dark)
    if ink is not None and prm["ink"] > 0:
        k = np.clip(ink, 0, 1) ** 0.85 * prm["ink"]
        v = v * (1 - 0.82 * k) + 0.07 * k                # black ink, a touch of brown remains
        s = s * (1 - 0.45 * k)
        h = h * (1 - k) + (26.0 / 360.0) * k
    return hsv2rgb(np.stack([h, np.clip(s, 0, 1), np.clip(v, 0, 1)], -1))


def grade_plates(rgb):
    """Pale crackled celadon-grey (not white); rust stains and cord marks stay as a muted rust."""
    hsv = rgb2hsv(rgb)
    s = hsv[..., 1]
    y = rgb @ np.array([0.299, 0.587, 0.114], dtype=np.float32)
    lo, hi = np.percentile(y, 2), np.percentile(y, 99)
    t = np.clip((y - lo) / (hi - lo + 1e-6), 0, 1)
    t = t ** 0.9
    dark = np.array([92, 94, 82], dtype=np.float32) / 255
    mid = np.array([160, 162, 146], dtype=np.float32) / 255
    light = np.array([206, 205, 188], dtype=np.float32) / 255
    a = np.clip(t * 2, 0, 1)[..., None]
    b = np.clip(t * 2 - 1, 0, 1)[..., None]
    base = dark * (1 - a) + mid * a
    base = base * (1 - b) + light * b
    rust_col = np.array([128, 88, 66], dtype=np.float32) / 255
    rust = np.clip((s - 0.34) / 0.25, 0, 1)[..., None] * 0.6
    out = base * (1 - rust) + (rust_col * (0.6 + 0.5 * t[..., None])) * rust
    return np.clip(out, 0, 1)


def save_rgb(a, path):
    Image.fromarray((np.clip(a, 0, 1) * 255 + 0.5).astype(np.uint8), "RGB").save(path, optimize=False)


def stats(a, name):
    px = (a.reshape(-1, 3) * 255)
    mx = px.max(1)
    mn = px.min(1)
    sat = (mx - mn) / (mx + 1e-6)
    r, g, b = px[:, 0], px[:, 1], px[:, 2]
    red = (r > g * 1.3) & (r > b * 1.3) & (mx > 80)
    dark = (mx < 95) & ~red
    pale = (mx > 150) & (sat < 0.25)
    out = [f"{name:8s}"]
    for nm, mk in (("red", red), ("dark", dark), ("pale", pale)):
        if mk.sum():
            m = px[mk].mean(0)
            h, s, v = colorsys.rgb_to_hsv(*(m / 255))
            out.append(f"{nm} {mk.mean():.2f} rgb({m[0]:.0f},{m[1]:.0f},{m[2]:.0f}) hsv({h * 360:.0f},{s:.2f},{v:.2f})")
    m = px.mean(0)
    out.append(f"mean rgb({m[0]:.0f},{m[1]:.0f},{m[2]:.0f})")
    print("GRADE", " | ".join(out), flush=True)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    ap.add_argument("--masks", default="")
    args = ap.parse_args()
    out = Path(args.out)
    out.mkdir(parents=True, exist_ok=True)
    masks = Path(args.masks) if args.masks else None
    for part in ("body", "wing", "tail", "plates"):
        src = SRC / f"jujak_{part}"
        rgb = load_rgb(src / "T_0_base_color.png")
        stats(rgb, part + " src")
        if part == "plates":
            g = grade_plates(rgb)
        else:
            ink = None
            if masks is not None and (masks / f"ink_{part}.png").exists():
                ink = np.asarray(Image.open(masks / f"ink_{part}.png").convert("L").resize((SIZE, SIZE), Image.BILINEAR), dtype=np.float32) / 255.0
            g = grade_feather(rgb, PARAMS[part], ink)
        stats(g, part + " out")
        P = part.capitalize()
        save_rgb(g, out / f"T_Jujak308_{P}_base.png")
        nrm = Image.open(src / "T_0_normal.png").convert("RGB").resize((SIZE, SIZE), Image.LANCZOS)
        nrm.save(out / f"T_Jujak308_{P}_normal.png")
        rough = Image.open(src / "T_0_roughness.png").convert("L").resize((SIZE, SIZE), Image.LANCZOS)
        rough.save(out / f"T_Jujak308_{P}_rough.png")
    print("GRADE done", flush=True)


if __name__ == "__main__":
    main()
