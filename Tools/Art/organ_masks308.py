"""#308 organ telegraph masks (SPEC-TELEGRAPH-ORGAN-308, D308-4) - offline, free, no editor.

MineBoss306 "버력 장사" crystal mask: the teal crystal colour key on the body base colour (FBX unchanged).
  key (sRGB 0..255):  G - R > 30  and  G >= B - 5  and  G > 60          (the investigation's key, 3.2 % of texels)
  then dilate N px (default 4, square kernel) so bilinear / mip sampling at island edges stays inside the key,
  written as an 8-bit single-channel PNG (L). Unity import (OrganSurface308 masks): Single Channel R8, sRGB off.

Usage:
  python Tools/Art/organ_masks308.py [--src <base colour png>] [--out <mask png>] [--dilate 4] [--report <json>]
Defaults read Oheangbu/Assets/_Project/Art/Characters/MineBoss306/Textures/T_MineBoss306_BaseColor.png and write
  Tools/Unity/Stage308_organ/_ProjectAssets/Art/Characters/MineBoss306/Textures/T_MineBoss306_CrystalMask.png
  (the stage; the main agent deploys it into Assets). The source texture is only read.
AC-T8 (offline part): coverage before and after dilation are both printed and written to the report; after > before,
  and before must equal the investigation value (3.2 %) because the key is the same.
"""
from pathlib import Path
import argparse, hashlib, json, sys

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
SRC = ROOT / 'Oheangbu/Assets/_Project/Art/Characters/MineBoss306/Textures/T_MineBoss306_BaseColor.png'
STAGE = ROOT / 'Tools/Unity/Stage308_organ/_ProjectAssets/Art/Characters/MineBoss306/Textures'
OUT = STAGE / 'T_MineBoss306_CrystalMask.png'
REPORT = ROOT / 'Tools/Unity/Stage308_organ/organ_masks308_report.json'
EXPECTED_BEFORE = 3.2   # % of texels, investigation (D308 understand workflow), 1 decimal truncated - same key


def crystal_key(rgb):
    r = rgb[..., 0].astype(np.int16); g = rgb[..., 1].astype(np.int16); b = rgb[..., 2].astype(np.int16)
    return (g - r > 30) & (g >= b - 5) & (g > 60)


def dilate(mask, px):
    """Square-kernel binary dilation by px texels (separable max filter, zero padding: UV islands do not tile)."""
    if px <= 0:
        return mask.copy()
    h, w = mask.shape
    pad = np.pad(mask, ((px, px), (0, 0)), mode='constant', constant_values=False)
    rows = np.zeros_like(mask)
    for k in range(2 * px + 1):
        rows |= pad[k:k + h, :]
    pad = np.pad(rows, ((0, 0), (px, px)), mode='constant', constant_values=False)
    out = np.zeros_like(mask)
    for k in range(2 * px + 1):
        out |= pad[:, k:k + w]
    return out


def main():
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
    ap = argparse.ArgumentParser()
    ap.add_argument('--src', default=str(SRC)); ap.add_argument('--out', default=str(OUT))
    ap.add_argument('--dilate', type=int, default=4); ap.add_argument('--report', default=str(REPORT))
    a = ap.parse_args()
    src = Path(a.src); out = Path(a.out)
    if not src.exists():
        print('REFUSED missing source ' + str(src)); sys.exit(1)
    src_hash = hashlib.sha256(src.read_bytes()).hexdigest()
    im = Image.open(src).convert('RGB'); rgb = np.asarray(im)
    key = crystal_key(rgb)
    before = 100.0 * float(key.mean())
    grown = dilate(key, a.dilate)
    after = 100.0 * float(grown.mean())
    out.parent.mkdir(parents=True, exist_ok=True)
    Image.fromarray((grown.astype(np.uint8) * 255), mode='L').save(out, optimize=True)
    # the source is read only: prove it
    unchanged = hashlib.sha256(src.read_bytes()).hexdigest() == src_hash
    report = dict(source=str(src.relative_to(ROOT)) if src.is_relative_to(ROOT) else str(src), sourceSha256=src_hash,
                  sourceUnchanged=unchanged, size=list(im.size), key='G-R>30, G>=B-5, G>60 (sRGB 0..255)',
                  dilatePx=a.dilate, coverageBeforePct=round(before, 3), coverageAfterPct=round(after, 3),
                  expectedBeforePct=EXPECTED_BEFORE, beforeMatchesInvestigation=bool(int(before * 10) == int(EXPECTED_BEFORE * 10)),  # the investigation figure is the 1-decimal truncation
                  afterGreater=bool(after > before), output=str(out.relative_to(ROOT)) if out.is_relative_to(ROOT) else str(out),
                  outputMode='L (8-bit single channel; import R8, sRGB off)')
    Path(a.report).parent.mkdir(parents=True, exist_ok=True)
    Path(a.report).write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding='utf-8')
    print('crystal mask %dx%d: before %.3f %% (investigation %.1f %%: %s), after %dpx dilation %.3f %% (%s) -> %s; source unchanged %s'
          % (im.size[0], im.size[1], before, EXPECTED_BEFORE, 'match' if report['beforeMatchesInvestigation'] else 'MISMATCH',
             a.dilate, after, 'grew' if after > before else 'DID NOT GROW', out, unchanged))
    sys.exit(0 if report['beforeMatchesInvestigation'] and report['afterGreater'] and unchanged else 2)


if __name__ == '__main__':
    main()
