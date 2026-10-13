"""#308 nearhand 스틸 여섯 장을 한 장으로: python nearhand_sheet.py [tag ...]"""
import os, sys
from PIL import Image, ImageDraw
D = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', '..', 'Art', 'Characters308', 'GreatBrush308', 'nearhand')
for tag in (sys.argv[1:] or ['']):
    n = ['top', 'eye', 'near', 'far', 'end', 'under']; s = None
    for i, k in enumerate(n):
        im = Image.open(os.path.join(D, 'stills', tag + k + '.png')).convert('RGB'); w = im.size[0]
        if s is None: s = Image.new('RGB', (w * 3, w * 2))
        ImageDraw.Draw(im).text((10, 10), tag + k, fill=(255, 255, 0)); s.paste(im, ((i % 3) * w, (i // 3) * w))
    s.save(os.path.join(D, 'sheet%s.png' % tag)); print('sheet', tag)
