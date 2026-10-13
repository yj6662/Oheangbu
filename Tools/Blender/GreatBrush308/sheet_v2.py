"""#308 GreatBrush 2차: v2/sheet.png.  python sheet_v2.py
위: 자세 a–g(3/4 앞 · 게임 어깨 카메라). 가운데: 휘두르기 c · d · e 의 준비 → 정점 → 여운(어깨 카메라 · 3/4). 아래: 붓 단독 · 두 손."""
import os, sys
from PIL import Image, ImageDraw, ImageFont
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
import poses_v2 as PD
OUT = os.path.abspath(os.path.join(HERE, '..', '..', '..', 'Art', 'Characters308', 'GreatBrush308', 'v2')); ST = os.path.join(OUT, 'stills')
PAD, TOP = 6, 34; W = 1280
font = ImageFont.truetype('C:/Windows/Fonts/malgunbd.ttf', 19); small = ImageFont.truetype('C:/Windows/Fonts/malgun.ttf', 15)
MAIN = ['a_carry', 'b_write', 'c_sweep', 'd_arc', 'e_slam', 'f_guard', 'g_drawin']
SEQ = [('c1_windup', 'c_sweep', 'c3_follow'), ('d1_windup', 'd_arc', 'd3_follow'), ('e1_windup', 'e_slam', 'e3_follow')]
EXTRA = [('brush_rest.png', '붓 — 쉬는 모양'), ('brush_splay.png', '붓 — 퍼짐(BristleSplay 1)'), ('brush_press.png', '붓 — 눌림(BristlePress 1) + 눕는 꺾임'),
         ('hands_closeup.png', '두 손 쥐기(b)'), ('a_carry_hands.png', '한 손 쥐기(a)'), ('brush_compare.png', '1차(위) · 2차(아래) 비례')]
EXTRA = [(f, t) for f, t in EXTRA if os.path.exists(os.path.join(ST, f))]
c2 = (W - PAD * 3) // 2; h2 = c2 * 9 // 16; c3 = (W - PAD * 4) // 3; h3 = c3 * 9 // 16
H = TOP + len(MAIN) * (h2 + PAD) + 2 * len(SEQ) * (h3 + PAD) + 2 * 30 + ((len(EXTRA) + 2) // 3) * (h3 + PAD) + PAD
sheet = Image.new('RGB', (W, H), (38, 38, 38)); dr = ImageDraw.Draw(sheet)
dr.text((PAD + 2, 5), 'SM_GreatBrush308 2차(1.25 m) — 왼쪽: 3/4 앞   오른쪽: 게임 어깨 카메라(뒤 2.8 m · 위 0.55 m · 아래로 8°)', font=small, fill=(225, 225, 225))


def cell(name, x, y, w, h, label):
    p = os.path.join(ST, name)
    if os.path.exists(p): sheet.paste(Image.open(p).convert('RGB').resize((w, h), Image.LANCZOS), (x, y))
    tw = dr.textlength(label, font=font); dr.rectangle((x, y, x + tw + 14, y + 28), fill=(20, 20, 20)); dr.text((x + 7, y + 2), label, font=font, fill=(240, 240, 240))


y = TOP
for n in MAIN:
    cell(n + '_q34.png', PAD, y, c2, h2, PD.POSES[n]['label']); cell(n + '_shoulder.png', PAD * 2 + c2, y, c2, h2, '어깨 카메라'); y += h2 + PAD
for cam, title in (('shoulder', '휘두르기 연속 컷 — 어깨 카메라: 준비 → 정점 → 여운'), ('q34', '휘두르기 연속 컷 — 3/4 앞')):
    dr.text((PAD + 2, y + 5), title, font=small, fill=(225, 225, 225)); y += 30
    for row in SEQ:
        for i, n in enumerate(row): cell(n + '_' + cam + '.png', PAD + i * (c3 + PAD), y, c3, h3, PD.POSES[n]['label'].split('(')[0])
        y += h3 + PAD
for i, (f, t) in enumerate(EXTRA): cell(f, PAD + (i % 3) * (c3 + PAD), y + (i // 3) * (h3 + PAD), c3, h3, t)
sheet.save(os.path.join(OUT, 'sheet.png')); print('SHEET', os.path.join(OUT, 'sheet.png'), sheet.size)
