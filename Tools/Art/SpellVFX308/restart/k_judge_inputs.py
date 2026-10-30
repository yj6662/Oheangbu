"""D308-153 우 K score loop (2026-10-29, the user: "점수 97점될 때까지 자동으로 계속 돌리길 바랬는데"): the judge's pictures for one round.
  python Tools/Art/SpellVFX308/restart/k_judge_inputs.py <round tag> [shot folder, default 우_K]
Out: Art/SpellVFX308/Restart/judge/<tag>/
  sheet.jpg  8 moments of the game shot (2 x 4, frame index printed)
  crops.jpg  3 fixed crops x 4 moments: A ring foot left / caster legs, B back-right (the bear's old place), C right front wave
The concept (kcisa/concepts/uks_1.png) and the Kling strip (ai_video/u_kling_1_strip.jpg) are given to the judge as they are.

Modes (one of): --path  --short  --dome  --self  --fan  --ring
  --ring (2026-10-30, lane F; the ㅜ 둘레 letters 국 군 굼 굿 궁 - the 2 m ring round the caster): three crops of the ring itself instead of --fan's two ground boxes
    (the old --fan boxes only looked at the bear's side of the ground and missed the right-hand cells): A the left arc, B the front arc with the bear's side (where a walking
    body comes in: shoot.ps1 walk=), C the right arc down to the bottom edge. Placed from a real 굼_F / 군_F frame of the shoulder camera (pitch 4, caster at x .33 - .45): the ring's
    cells ran x .03 - .70, y .66 - .97 of the frame; the right side of the ring sits right on the bottom edge, so box C reaches y = 1.0.
Moments: --moments=a,b,c,...   frame fractions 0 - 1 for the sheet instead of the 8 fixed ones (any count; the grid is 4 wide). Also --moments a,b,c.
  The crops then use those moments too (up to 4: the list itself; more: four spread evenly through it); --cmoments=a,b,c,d sets the crop moments on their own.
  With custom moments every crop also carries its frame number. Example: frames=360 and a body that is touched at 3.7 s (frame 222 = 0.62):
  --moments=0.1,0.3,0.5,0.58,0.62,0.66,0.72,0.85"""
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


def opt(name):
    """--name=value or --name value (a value never starts with --); None when absent."""
    for i, a in enumerate(sys.argv):
        if a == name and i + 1 < len(sys.argv) and not sys.argv[i + 1].startswith('--'): return sys.argv[i + 1]
        if a.startswith(name + '='): return a.split('=', 1)[1]
    return None


def fractions(text, what):
    try: v = [float(x) for x in text.replace(' ', '').split(',') if x]
    except ValueError: sys.exit('%s: not numbers: %r' % (what, text))
    if not v or any(not 0.0 <= x <= 1.0 for x in v): sys.exit('%s: need one or more fractions from 0 to 1, comma separated: %r' % (what, text))
    return tuple(v)


def pct(m):
    return '%g' % round(m * 100, 1)


def at(m):
    return min(len(fs) - 1, max(0, int(len(fs) * m)))


moments = (0.04, 0.12, 0.2, 0.3, 0.42, 0.55, 0.7, 0.85)
if '--path' in sys.argv:      # 초성 ㄱ (Fable g r1: put one moment in the unravel, f150-f175 of 240)
    moments = (0.08, 0.13, 0.18, 0.25, 0.37, 0.55, 0.67, 0.75)
if '--short' in sys.argv:     # 거 (a 0.5 s parry): every moment inside the first second
    moments = (0.01, 0.02, 0.035, 0.06, 0.12, 0.2, 0.23, 0.26)
custom = opt('--moments')     # lane F: frame fractions chosen by hand (an "작동" / "고리 넘음" instant is not at the fixed spots)
if custom: moments = fractions(custom, '--moments')
cw, ch = 640, 360
cols = min(4, len(moments)); rows = -(-len(moments) // cols)
sheet = Image.new('RGB', (cw * cols, ch * rows))
for j, m in enumerate(moments):
    k = at(m); im = Image.open(fs[k]).convert('RGB').resize((cw, ch)); lab(im, 'f%03d (%s%%)' % (k, pct(m)))
    sheet.paste(im, ((j % cols) * cw, (j // cols) * ch))
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
if '--ring' in sys.argv:      # 둘레 ㅜ (국 군 굼 굿 궁), 2026-10-30 lane F: the ring round the caster, left / front / right arcs (read off 굼_F f060 + 군_F f060, shoulder camera pitch 4)
    boxes = {'A 왼쪽 호': (0.0, 0.58, 0.4, 1.0), 'B 앞 호 · 곰 입장': (0.25, 0.46, 0.68, 0.94), 'C 오른쪽 호': (0.44, 0.58, 0.84, 1.0)}
    cm = (0.1, 0.2, 0.45, 0.8)
cust_cm = opt('--cmoments')
if cust_cm: cm = fractions(cust_cm, '--cmoments')
elif custom: cm = moments if len(moments) <= 4 else tuple(moments[int(round(i * (len(moments) - 1) / 3.0))] for i in range(4))
tw, th = 480, 300
crops = Image.new('RGB', (tw * len(cm), th * len(boxes)))
for r, (name, b) in enumerate(boxes.items()):
    for j, m in enumerate(cm):
        k = at(m)
        im = Image.open(fs[k]).convert('RGB').crop((int(W * b[0]), int(H * b[1]), int(W * b[2]), int(H * b[3]))).resize((tw, th))
        if custom or cust_cm: lab(im, (name + ' ' if j == 0 else '') + 'f%03d' % k)     # hand-picked moments: say which frame each crop is
        elif j == 0: lab(im, name)
        crops.paste(im, (j * tw, r * th))
crops.save(os.path.join(out, 'crops.jpg'), quality=88)
print(out, len(fs), 'frames | sheet', ' '.join('f%03d' % at(m) for m in moments), '| crops', ' '.join('f%03d' % at(m) for m in cm))
