"""D308-120 look check: crop each Creatures308 still to its bodies (idle .. stun, death dropped) and stack them, enlarged.
  python grot_sheet.py <out.png> key ...
"""
import sys
from pathlib import Path
from PIL import Image, ImageChops

STILLS = Path(__file__).resolve().parents[3] / 'Oheangbu/Screenshots/Creatures308'
out, keys = sys.argv[1], sys.argv[2:]
rows = []
for k in keys:
    im = Image.open(STILLS / (k + '.png')).convert('RGB')
    w, h = im.size
    bg = Image.new('RGB', im.size, im.getpixel((w - 2, h // 2)))
    diff = ImageChops.difference(im, bg).convert('L').point(lambda v: 255 if v > 28 else 0)
    x0, y0, x1, y1 = diff.getbbox()
    x0 = int(w * .13)                      # past the reference capsule
    x1 = int(x0 + (x1 - x0) * .78)          # idle, walk, attack, stun
    c = im.crop((x0, max(0, y0 - 8), x1, min(h, y1 + 8)))
    k2 = 1100 / c.size[0]
    rows.append(c.resize((1100, int(c.size[1] * k2))))
sheet = Image.new('RGB', (1100, sum(r.size[1] for r in rows)), (255, 255, 255))
y = 0
for r in rows:
    sheet.paste(r, (0, y)); y += r.size[1]
sheet.save(out)
print(out, sheet.size)
