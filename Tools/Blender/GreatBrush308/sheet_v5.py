"""#308 GreatBrush v5: 붓 스틸을 한 장으로.  python sheet_v5.py [--sub iterN]  → v5/sheet.png (또는 v5/stills/iterN/sheet.png)"""
import os, sys
from PIL import Image, ImageDraw, ImageFont
HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.abspath(os.path.join(HERE, '..', '..', '..', 'Art', 'Characters308', 'GreatBrush308', 'v5'))
SUB = sys.argv[sys.argv.index('--sub') + 1] if '--sub' in sys.argv else None
ST = os.path.join(OUT, 'stills', SUB) if SUB else os.path.join(OUT, 'stills')
CELLS = [('rest', '쉬는 모양 — 전체 1.55 m'), ('compare', '위: 1차 통짜 붓머리 / 아래: 5차'), ('head_side', '붓머리 — 옆'), ('head_tip', '붓머리 — 붓끝 쪽'),
         ('head_back', '붓머리 — 자루 쪽 위'), ('splay', 'BristleSplay 1.0'), ('press', 'BristlePress 1.0 + 바닥에 끌림'), ('press_top', '눌림 — 위에서'),
         ('bend', '옆으로 꺾임 + 고리 사슬 제각각'), ('vs_rest', '위: v4 / 아래: v5 — 쉬는 모양'), ('vs_tip', '위: v4 / 아래: v5 — 붓끝')]
CELLS = [(f, t) for f, t in CELLS if os.path.exists(os.path.join(ST, 'brush_%s.png' % f))]
CW, CH, PAD, TOP, COLS = 640, 360, 6, 34, 3
rows = (len(CELLS) + COLS - 1) // COLS
sheet = Image.new('RGB', (PAD * (COLS + 1) + CW * COLS, TOP + rows * (CH + PAD) + PAD), (38, 38, 38)); dr = ImageDraw.Draw(sheet)
font = ImageFont.truetype('C:/Windows/Fonts/malgunbd.ttf', 18); small = ImageFont.truetype('C:/Windows/Fonts/malgun.ttf', 15)
dr.text((PAD + 2, 6), 'SM_GreatBrush308 v5 — 젖은 다발 (윤기는 렌더 전용 재질 분리: Col.A > 0.03)   Workbench' + ('   [%s]' % SUB if SUB else ''), font=small, fill=(225, 225, 225))
for i, (f, t) in enumerate(CELLS):
    x = PAD + (i % COLS) * (CW + PAD); y = TOP + (i // COLS) * (CH + PAD)
    sheet.paste(Image.open(os.path.join(ST, 'brush_%s.png' % f)).convert('RGB').resize((CW, CH), Image.LANCZOS), (x, y))
    w = dr.textlength(t, font=font); dr.rectangle((x, y, x + w + 14, y + 27), fill=(20, 20, 20)); dr.text((x + 7, y + 2), t, font=font, fill=(240, 240, 240))
out = os.path.join(ST if SUB else OUT, 'sheet.png'); sheet.save(out); print('SHEET', out, sheet.size)
