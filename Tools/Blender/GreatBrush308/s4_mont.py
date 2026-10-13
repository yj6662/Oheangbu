"""#308 s4: 클립 확인 시트(넓게). 앞 3/4 + 게임 어깨 카메라, 한 줄에 네 장.  python s4_mont.py <이름> [--sub 폴더] [--out 파일]   기본 출력 = motion2/sheet_<이름>.png (≤ 1280 px)"""
import json, os, sys
from PIL import Image, ImageDraw, ImageFont
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
import clips_m2
OUT = os.path.abspath(os.path.join(HERE, '..', '..', '..', 'Art', 'Characters308', 'GreatBrush308', 'motion2'))
A = sys.argv[1:]; n = A[0]; SUB = A[A.index('--sub') + 1] if '--sub' in A else ''; ST = OUT + '/stills' + ('/' + SUB if SUB else '')
font = ImageFont.truetype('C:/Windows/Fonts/malgunbd.ttf', 17); small = ImageFont.truetype('C:/Windows/Fonts/malgun.ttf', 14)
fr = [f for f in clips_m2.SHOTS[n] if os.path.exists('%s/%s_q34_%03d.png' % (ST, n, f))]; S = json.load(open('%s/metrics/%s.json' % (OUT, n), encoding='utf-8'))['summary']
COLS = 4 if len(fr) > 6 else len(fr); cw = 1280 // COLS; apex = clips_m2.CLIPS[n].get('apex'); fa = min(fr, key=lambda x: abs(x / 30 - apex)) if apex is not None else None
def cell(cam, f):
    im = Image.open('%s/%s_%s_%03d.png' % (ST, n, cam, f)).convert('RGB')
    if cam == 'shoulder': im = im.crop((int(im.width * .12), int(im.height * .30), int(im.width * .88), int(im.height * .92)))
    else: im = im.crop((0, int(im.height * .06), im.width, int(im.height * .90)))
    return im.resize((cw, round(cw * im.height / im.width)), Image.LANCZOS)
rows = []
for cam, title in (('q34', '앞 3/4'), ('shoulder', '게임 어깨 카메라(뒤 2.8 m · 위 0.55 m · 아래 8도 · FOV 60, 가운데만)')):
    for r0 in range(0, len(fr), COLS): rows.append((cam, title if r0 == 0 else None, fr[r0:r0 + COLS]))
cells = {(cam, f): cell(cam, f) for cam, _, fs in rows for f in fs}; TOP = 54; H = TOP + sum(cells[(cam, fs[0])].height + (22 if t else 0) for cam, t, fs in rows)
sh = Image.new('RGB', (1280, H), (38, 38, 38)); dr = ImageDraw.Draw(sh)
dr.text((8, 4), 'A_Brush308_%s — %s   %.2f s (%d 프레임, 30 fps)' % (n, clips_m2.CLIPS[n]['use'], S['seconds'], S['frames']), font=font, fill=(235, 235, 235))
dr.text((8, 29), '오른손 = 앞손(조종) · 왼손 = 꼬리 축   왼손 쥠 %.0f %%   자루 관통 %d 프레임   붓끝 최고 %.1f m/s @ %.2f s' % (100 * S['l_on_share'], S['shaft_in_frames'], S['peak_tip_speed'], S['peak_tip_speed_t']), font=small, fill=(200, 200, 200))
y = TOP
for cam, t, fs in rows:
    if t: dr.text((8, y + 2), t, font=small, fill=(210, 210, 210)); y += 22
    for k, f in enumerate(fs):
        im = cells[(cam, f)]; sh.paste(im, (k * cw, y)); lab = '%.2f s' % (f / 30) + ('  정점' if f == fa else ''); w = dr.textlength(lab, font=small)
        dr.rectangle((k * cw, y, k * cw + w + 10, y + 21), fill=(20, 20, 20)); dr.text((k * cw + 5, y + 1), lab, font=small, fill=(255, 215, 60) if f == fa else (240, 240, 240))
    y += cells[(cam, fs[0])].height
out = A[A.index('--out') + 1] if '--out' in A else '%s/sheet_%s.png' % (OUT, n); sh.save(out); print('MONT', out, sh.size)
