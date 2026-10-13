"""#308 GreatBrush: 자세 스틸을 한 장으로(sheet.png). python sheet.py [--draft]  — 행 = 자세 a–g, 열 = 3/4 앞 · 게임 어깨 카메라."""
import os, sys
from PIL import Image, ImageDraw, ImageFont
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
import poses as PD
OUT = os.path.abspath(os.path.join(HERE, '..', '..', '..', 'Art', 'Characters308', 'GreatBrush308')); ST = os.path.join(OUT, 'stills')
CW, CH, PAD, TOP = 631, 355, 6, 34
font = ImageFont.truetype('C:/Windows/Fonts/malgunbd.ttf', 20); small = ImageFont.truetype('C:/Windows/Fonts/malgun.ttf', 15)
names = [n for n in PD.POSES if os.path.exists(os.path.join(ST, n + '_q34.png'))]
extra = [(f, t) for f, t in (('brush_rest.png', '붓 — 쉬는 모양'), ('brush_press.png', '붓 — 눌림(BristlePress) + 꺾임'), ('hands_closeup.png', '두 손 쥐기')) if os.path.exists(os.path.join(ST, f))]
rows = len(names) + (len(extra) + 1) // 2
W = PAD * 3 + CW * 2; H = TOP + rows * (CH + PAD) + PAD
sheet = Image.new('RGB', (W, H), (38, 38, 38)); dr = ImageDraw.Draw(sheet)
dr.text((PAD + 2, 5), 'SM_GreatBrush308 — 양손 큰 붓 쥐기 · 자세 스틸   (왼쪽: 3/4 앞   오른쪽: 게임 어깨 카메라)', font=small, fill=(225, 225, 225))


def cell(path, x, y, label):
    im = Image.open(path).convert('RGB').resize((CW, CH), Image.LANCZOS); sheet.paste(im, (x, y))
    w = dr.textlength(label, font=font); dr.rectangle((x, y, x + w + 16, y + 30), fill=(20, 20, 20)); dr.text((x + 8, y + 2), label, font=font, fill=(240, 240, 240))


for r, n in enumerate(names):
    y = TOP + r * (CH + PAD)
    cell(os.path.join(ST, n + '_q34.png'), PAD, y, PD.POSES[n]['label']); cell(os.path.join(ST, n + '_shoulder.png'), PAD * 2 + CW, y, PD.POSES[n]['label'] + ' · 어깨 카메라')
for i, (f, t) in enumerate(extra):
    cell(os.path.join(ST, f), PAD + (i % 2) * (CW + PAD), TOP + (len(names) + i // 2) * (CH + PAD), t)
out = os.path.join(OUT, 'sheet_draft.png' if '--draft' in sys.argv else 'sheet.png'); sheet.save(out); print('SHEET', out, sheet.size)
