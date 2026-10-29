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
shot = sys.argv[2] if len(sys.argv) > 2 and not sys.argv[2].startswith('--') else '우_K'
fs = sorted(glob.glob(os.path.join(ROOT, 'shots', shot, 'f_*.jpg')))
out = os.path.join(ROOT, 'judge', tag); os.makedirs(out, exist_ok=True)
font = ImageFont.truetype(r'C:\Windows\Fonts\malgunbd.ttf', 22)
W, H = Image.open(fs[0]).size


def lab(im, t):
    d = ImageDraw.Draw(im); d.rectangle((0, 0, 12 + int(d.textlength(t, font=font)), 30), fill=(0, 0, 0)); d.text((6, 2), t, font=font, fill=(255, 220, 0))


moments = (0.04, 0.12, 0.2, 0.3, 0.42, 0.55, 0.7, 0.85)
if '--path' in sys.argv:      # 초성 ㄱ (Fable g r1: put one moment in the unravel, f150-f175 of 240)
    moments = (0.08, 0.13, 0.18, 0.25, 0.37, 0.55, 0.67, 0.75)
if '--short' in sys.argv:     # 거 (a 0.5 s parry): every moment inside the first second
    moments = (0.01, 0.02, 0.035, 0.06, 0.12, 0.2, 0.23, 0.26)
cw, ch = 640, 360
sheet = Image.new('RGB', (cw * 4, ch * 2))
for j, m in enumerate(moments):
    k = int(len(fs) * m); im = Image.open(fs[k]).convert('RGB').resize((cw, ch)); lab(im, 'f%03d (%d%%)' % (k, m * 100))
    sheet.paste(im, ((j % 4) * cw, (j // 4) * ch))
sheet.save(os.path.join(out, 'sheet.jpg'), quality=88)

boxes = {'A 왼쪽 앞 · 시전자 다리': (0.0, 0.45, 0.45, 1.0), 'B 뒤 오른쪽(옛 곰 자리)': (0.62, 0.3, 0.98, 0.8), 'C 오른쪽 앞 물결': (0.55, 0.5, 1.0, 1.0)}
if '--dome' in sys.argv:      # H (2026-10-29 r8): the dome sits centre-left - crops on its walls, the caster / bear and its crown
    boxes = {'A 시전자 · 앞벽': (0.25, 0.4, 0.6, 1.0), 'B 곰 쪽 벽(타격)': (0.33, 0.3, 0.68, 0.9), 'C 왼쪽 벽 · 꼭대기': (0.0, 0.22, 0.38, 0.82)}
cm = (0.12, 0.3, 0.5, 0.7)
if '--self' in sys.argv:      # 초성 ㄱ 부착 ㅓ (2026-10-30): the effect is on the caster - the body, above the brush, the ground round the caster
    boxes = {'A 시전자 몸': (0.15, 0.25, 0.6, 1.0), 'B 붓 · 머리 위': (0.336, 0.347, 0.57, 0.764), 'C 둘레 · 바닥': (0.0, 0.45, 0.85, 1.0)}
    cm = (0.1, 0.2, 0.45, 0.8)
    if '--short' in sys.argv:      # 거 (0.5 s): the moments all inside the first second
        moments = (0.01, 0.02, 0.035, 0.06, 0.12, 0.2, 0.23, 0.26); cm = (0.02, 0.06, 0.2, 0.25)
if '--fan' in sys.argv:       # 면 ㅗ · 둘레 ㅜ (Fable 면 r1: a left / right ground pair, and the body)
    boxes = {'A 왼쪽 땅': (0.08, 0.48, 0.47, 0.97), 'B 오른쪽 땅': (0.55, 0.48, 0.94, 0.97), 'C 과녁': (0.36, 0.45, 0.62, 0.8)}
    cm = (0.1, 0.2, 0.45, 0.8)
if '--path' in sys.argv and '--fan' not in sys.argv:      # 초성 ㄱ 경로 ㅏ (2026-10-30): caster at lower left-centre, the bear about 5 m ahead in the middle
    # g r2: tight on the action (bear about x .46, y .66 of the frame; brush hand about x .4, y .72)
    boxes = {'A 시전자 · 붓끝': (0.3, 0.5, 0.56, 0.95), 'B 과녁(물림)': (0.38, 0.52, 0.58, 0.8), 'C 경로 · 땅': (0.33, 0.55, 0.62, 0.85)}
    cm = (0.1, 0.2, 0.45, 0.8)
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
