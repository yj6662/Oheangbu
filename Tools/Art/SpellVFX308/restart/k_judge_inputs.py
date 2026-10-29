"""D308-153 우 K score loop (2026-10-29, the user: "점수 97점될 때까지 자동으로 계속 돌리길 바랬는데"): the judge's pictures for one round.
  python Tools/Art/SpellVFX308/restart/k_judge_inputs.py <round tag> [shot folder, default 우_K]
Out: Art/SpellVFX308/Restart/judge/<tag>/
  sheet.jpg  8 moments of the game shot (2 x 4, frame index printed)
  crops.jpg  3 fixed crops x 4 moments: A ring foot left / caster legs, B back-right (the bear's old place), C right front wave
The concept (kcisa/concepts/uks_1.png) and the Kling strip (ai_video/u_kling_1_strip.jpg) are given to the judge as they are."""
import glob, os, sys
from PIL import Image, ImageDraw, ImageFont

ROOT = r'C:\Users\yj666\Oheangbu\Art\SpellVFX308\Restart'
tag = sys.argv[1]
shot = sys.argv[2] if len(sys.argv) > 2 else '우_K'
fs = sorted(glob.glob(os.path.join(ROOT, 'shots', shot, 'f_*.jpg')))
out = os.path.join(ROOT, 'judge', tag); os.makedirs(out, exist_ok=True)
font = ImageFont.truetype(r'C:\Windows\Fonts\malgunbd.ttf', 22)
W, H = Image.open(fs[0]).size


def lab(im, t):
    d = ImageDraw.Draw(im); d.rectangle((0, 0, 12 + int(d.textlength(t, font=font)), 30), fill=(0, 0, 0)); d.text((6, 2), t, font=font, fill=(255, 220, 0))


moments = (0.04, 0.12, 0.2, 0.3, 0.42, 0.55, 0.7, 0.85)
cw, ch = 640, 360
sheet = Image.new('RGB', (cw * 4, ch * 2))
for j, m in enumerate(moments):
    k = int(len(fs) * m); im = Image.open(fs[k]).convert('RGB').resize((cw, ch)); lab(im, 'f%03d (%d%%)' % (k, m * 100))
    sheet.paste(im, ((j % 4) * cw, (j // 4) * ch))
sheet.save(os.path.join(out, 'sheet.jpg'), quality=88)

boxes = {'A 왼쪽 앞 · 시전자 다리': (0.0, 0.45, 0.45, 1.0), 'B 뒤 오른쪽(옛 곰 자리)': (0.62, 0.3, 0.98, 0.8), 'C 오른쪽 앞 물결': (0.55, 0.5, 1.0, 1.0)}
cm = (0.12, 0.3, 0.5, 0.7)
tw, th = 480, 300
crops = Image.new('RGB', (tw * 4, th * 3))
for r, (name, b) in enumerate(boxes.items()):
    for j, m in enumerate(cm):
        k = int(len(fs) * m)
        im = Image.open(fs[k]).convert('RGB').crop((int(W * b[0]), int(H * b[1]), int(W * b[2]), int(H * b[3]))).resize((tw, th))
        if j == 0: lab(im, name)
        crops.paste(im, (j * tw, r * th))
crops.save(os.path.join(out, 'crops.jpg'), quality=88)
print(out, len(fs), 'frames')
