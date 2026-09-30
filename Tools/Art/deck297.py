"""#297 pitch deck: slot-exact game captures for the 7-page deck, and a preview PDF with them placed.

python Tools/Art/deck297.py <deck.pdf>

Each slot is found by page + position (not xref), so a re-exported deck with the same layout still works.
Outputs go to Art/Presentation297/Deck/:
  <slot>.jpg            the slot image at the slot's exact aspect ratio (about 4 px per pt)
  p1_표지_*_페이드.png   the cover with the deck's left fade baked into alpha (same ramp as the original)
  오행부_발표자료_게임화면적용_미리보기.pdf
  review.jpg            old slot | new slot, for checking

Grade: luminance-only black point + gentle S-curve so thin ink reads on a projector. Hue, tint and
content are the render's own; nothing is painted in (the deck says every frame is a real game scene).
"""
import sys
from pathlib import Path

import fitz
import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = Path(__file__).resolve().parents[2]
ART = ROOT / 'Art' / 'Presentation297'
OUT = ART / 'Deck'
SHOTS = ART / 'Screenshots'
FILMS = ART / 'Films'
STILLS = ART / 'Stills'          # composed stills rendered at the slot's aspect (Presentation297 shot)
PX_PER_PT = 4.0
LW = np.array([.299, .587, .114], np.float32)
COVER_FADE = .585            # the original cover's alpha ramps 0 -> 1 over the left 58.5 % of the image

# page, slot rect in pt (x0, y0, x1, y1), output stem, source, crop centre x/y and width (fractions of the source)
SLOTS = [
    (1, (587.0, 197.6, 1401.9, 632.6), 'p1_표지_황경삼홍예정문', STILLS / 'deck_cover_j_final.png', .5, .5, 1.0),
    (3, (939.6, 247.6, 1195.9, 391.6), 'p3_실존문자의마법_작도나', FILMS / 'op_cast_na' / 'f00100.png', .56, .52, .62),
    (3, (343.4, 629.4, 604.1, 711.5), 'p3_아트_수묵담채_홍예돌다리', STILLS / 'deck_bridge_b.png', .5, .5, 1.0),
    (3, (614.9, 629.4, 875.5, 711.5), 'p3_아트_마석자동차', FILMS / 'wood_car' / 'f00000.png', .5, .64, 1.0),
    (4, (72.0, 253.4, 328.3, 363.6), 'p4_전투1_대치_여우귀물', FILMS / 'climax_gom' / 'f00048.png', .5, .6, .95),
    (4, (361.4, 252.1, 617.8, 360.5), 'p4_전투2_작도_곰', FILMS / 'climax_gom' / 'f00116.png', .5, .46, 1.0),
    (4, (650.9, 252.1, 910.4, 362.3), 'p4_전투3_발동_나무사슴소환', FILMS / 'climax_gom' / 'f00168.png', .52, .55, 1.0),
    (4, (72.0, 414.7, 328.3, 524.9), 'p4_필드1_조우', FILMS / 'water_mum' / 'f00024.png', .5, .5, 1.0),
    (4, (360.7, 414.7, 617.0, 524.9), 'p4_필드2_작도_뭄', FILMS / 'water_mum' / 'f00150.png', .5, .45, 1.0),
    (4, (650.2, 414.7, 906.5, 524.9), 'p4_필드3_돌다리생성', FILMS / 'water_mum' / 'f00200.png', .5, .55, 1.0),
]

ALTERNATES = [  # stem, source, fade (cover-shaped)
    ('대안_p1_표지_황경정문_원경봉수', STILLS / 'deck_cover_l_final.png', True),
]


def grade(im, contrast=.22, black=.4, white=99.8):
    a = np.asarray(im.convert('RGB'), np.float32) / 255.0
    L = a @ LW
    lo, hi = np.percentile(L, black), np.percentile(L, white)
    n = np.clip((L - lo) / max(1e-3, hi - lo), 0, 1)
    s = n * n * (3 - 2 * n)
    n = (n + (s - n) * contrast) * .97 + .015
    return Image.fromarray((np.clip(n[..., None] + (a - L[..., None]), 0, 1) * 255 + .5).astype(np.uint8))


def crop(im, cx, cy, width, ar):
    W, H = im.size
    w = width * W
    h = w / ar
    if h > H:
        h = H
        w = h * ar
    x0 = min(max(0, cx * W - w / 2), W - w)
    y0 = min(max(0, cy * H - h / 2), H - h)
    return im.crop((int(round(x0)), int(round(y0)), int(round(x0 + w)), int(round(y0 + h))))


def render(slot):
    page, rect, stem, src, cx, cy, width = slot
    w_pt, h_pt = rect[2] - rect[0], rect[3] - rect[1]
    ar = w_pt / h_pt
    im = crop(Image.open(src).convert('RGB'), cx, cy, width, ar)
    out_w = min(int(round(w_pt * PX_PER_PT)), im.width)
    im = im.resize((out_w, int(round(out_w / ar))), Image.LANCZOS)
    im = grade(im).filter(ImageFilter.UnsharpMask(radius=1.0, percent=35, threshold=2))
    return im


def faded(im):
    x = np.linspace(0, 1, im.width, dtype=np.float32)
    a = np.clip(x / COVER_FADE, 0, 1)
    alpha = Image.fromarray((np.broadcast_to(a, (im.height, im.width)) * 255 + .5).astype(np.uint8))
    rgba = im.copy()
    rgba.putalpha(alpha)
    return rgba


def placement(page, rect):
    """the image placed at (about) this rect on the page -> xref"""
    best, err = None, 1e9
    for info in page.get_image_info(xrefs=True):
        b = info['bbox']
        e = sum(abs(b[i] - rect[i]) for i in range(4))
        if e < err:
            best, err = info, e
    if best is None or err > 24:
        raise SystemExit(f'page {page.number + 1}: no image near {rect} (closest error {err:.1f} pt) — layout changed?')
    return best['xref']


def main(deck):
    OUT.mkdir(parents=True, exist_ok=True)
    doc = fitz.open(deck)
    font = ImageFont.truetype(r'C:\Windows\Fonts\malgun.ttf', 20)
    rows = []
    jobs = []
    for slot in SLOTS:
        page_no, rect, stem = slot[0], slot[1], slot[2]
        page = doc[page_no - 1]
        xref = placement(page, rect)
        old = Image.open(__import__('io').BytesIO(doc.extract_image(xref)['image'])).convert('RGB')
        new = render(slot)
        jpg = OUT / f'{stem}.jpg'
        new.save(jpg, quality=93, subsampling=0)
        placed = jpg
        if page_no == 1:
            png = OUT / f'{stem}_페이드.png'
            faded(new).save(png)
            placed = png
        jobs.append((page_no, xref, placed))
        rows.append((stem, old, new))
        print(f'{stem}: {new.width}x{new.height}  <- {slot[3].name}')
    for page_no, xref, path in jobs:            # replace after every placement was found (xrefs change on replace)
        page = doc[page_no - 1]
        if path.suffix == '.png':               # the cover: JPEG + soft mask like the original, not a lossless RGBA
            rgba = Image.open(path)
            jpg, mask = __import__('io').BytesIO(), __import__('io').BytesIO()
            rgba.convert('RGB').save(jpg, 'JPEG', quality=92, subsampling=0)
            rgba.getchannel('A').save(mask, 'PNG')
            # what Page.replace_image does, with a mask: insert, copy the image object over the old xref, drop the new draw
            new_xref = page.insert_image(page.rect, stream=jpg.getvalue(), mask=mask.getvalue())
            doc.xref_copy(new_xref, xref)
            doc.update_stream(page.get_contents()[-1], b' ')
        else:
            page.replace_image(xref, filename=str(path))
    for stem, src, fade in ALTERNATES:
        im = grade(Image.open(src).convert('RGB')).filter(ImageFilter.UnsharpMask(radius=1.0, percent=35, threshold=2))
        im.save(OUT / f'{stem}.jpg', quality=93, subsampling=0)
        if fade:
            faded(im).save(OUT / f'{stem}_페이드.png')
        print('alternate', stem, im.size)
    pdf = OUT / '오행부_발표자료_게임화면적용_미리보기.pdf'
    doc.save(pdf, garbage=3, deflate=True)
    print(pdf, f'{pdf.stat().st_size / 1048576:.1f} MB')
    cw, ch = 720, 300
    sheet = Image.new('RGB', (cw * 2 + 30, len(rows) * (ch + 34)), 'white')
    d = ImageDraw.Draw(sheet)
    for k, (stem, old, new) in enumerate(rows):
        y = k * (ch + 34)
        for j, im in enumerate((old, new)):
            t = im.copy()
            t.thumbnail((cw, ch))
            sheet.paste(t, (j * (cw + 30), y))
        d.text((4, y + ch + 4), stem, fill=(0, 0, 0), font=font)
    sheet.save(OUT / 'review.jpg', quality=85)


if __name__ == '__main__':
    main(sys.argv[1])
