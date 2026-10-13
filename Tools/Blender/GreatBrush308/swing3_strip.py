"""#308 swing3: 판정용 띠 그림(내가 보는 용). python swing3_strip.py <이름> <sub> <f,f,f,...> [--cams q34,shoulder]  → motion2/stills/<sub>/strip_<이름>.png (가로 ≤ 1280)"""
import os, sys
from PIL import Image, ImageDraw, ImageFont
HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.abspath(os.path.join(HERE, '..', '..', '..', 'Art', 'Characters308', 'GreatBrush308', 'motion2')); n, sub, fr = sys.argv[1], sys.argv[2], [int(x) for x in sys.argv[3].split(',')]
cams = sys.argv[sys.argv.index('--cams') + 1].split(',') if '--cams' in sys.argv else ['q34', 'shoulder']
ST = OUT + '/stills/' + sub; font = ImageFont.truetype('C:/Windows/Fonts/malgun.ttf', 13); per = min(len(fr), 6); rows = (len(fr) + per - 1) // per; cw = 1280 // per; TOP = .20
def cell(cam, f):
    im = Image.open('%s/%s_%s_%03d.png' % (ST, n, cam, f)).convert('RGB')
    if cam == 'shoulder': im = im.crop((int(im.width * .10), int(im.height * TOP), int(im.width * .90), im.height))
    return im.resize((cw, round(cw * im.height / im.width)), Image.LANCZOS)
hs = {c: cell(c, fr[0]).height for c in cams}; H = rows * sum(hs.values()); sh = Image.new('RGB', (1280, H), (38, 38, 38)); dr = ImageDraw.Draw(sh); y = 0
for r in range(rows):
    for c in cams:
        for i, f in enumerate(fr[r * per:(r + 1) * per]):
            sh.paste(cell(c, f), (i * cw, y)); dr.rectangle((i * cw, y, i * cw + 62, y + 17), fill=(20, 20, 20)); dr.text((i * cw + 3, y), 'f%d %.2f' % (f, f / 30), font=font, fill=(255, 220, 120))
        y += hs[c]
p = '%s/strip_%s.png' % (ST, n); sh.save(p); print('STRIP', p, sh.size)
