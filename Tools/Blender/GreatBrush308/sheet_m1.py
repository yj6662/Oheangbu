"""#308 GreatBrush motion1: 스틸 → 클립별 시트(sheet_<clip>.png, 1280 폭: 앞 3/4 8장 + 어깨 카메라 8장) + 전체 시트(sheet.png, 클립당 어깨 카메라 6장).
    python sheet_m1.py [clip ...]"""
import json, os, sys
from PIL import Image, ImageDraw, ImageFont
HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.abspath(os.path.join(HERE, '..', '..', '..', 'Art', 'Characters308', 'GreatBrush308', 'motion1')); ST = OUT + '/stills'
W = json.load(open(HERE + '/windows_m1.json', encoding='utf-8'))
font = ImageFont.truetype('C:/Windows/Fonts/malgunbd.ttf', 17); small = ImageFont.truetype('C:/Windows/Fonts/malgun.ttf', 14)
CW = 320; TOPCROP = .22       # 어깨 카메라는 위 22 % 가 빈 하늘이라 잘라 낸다


def cell(clip, cam, f, w):
    im = Image.open('%s/%s_%s_%03d.png' % (ST, clip, cam, f)).convert('RGB')
    if cam == 'shoulder': im = im.crop((0, int(im.height * TOPCROP), im.width, im.height))
    return im.resize((w, round(w * im.height / im.width)), Image.LANCZOS)


def label(dr, x, y, t, f=small, fill=(240, 240, 240), bg=(20, 20, 20)):
    w = dr.textlength(t, font=f); dr.rectangle((x, y, x + w + 10, y + 21), fill=bg); dr.text((x + 5, y + 1), t, font=f, fill=fill)


def tlabel(w, f):
    t = (f - w['frames'][0]) / 30; a = ' 정점' if (f == w['apex'] and w['kind'] in ('attack', 'slam', 'guard')) else ''
    return 'f%d  %.2f s%s' % (f, t, a), bool(a)


def clip_sheet(clip):
    w = W[clip]; fr = w['frames']; hq = CW; hs = round(CW * (1 - TOPCROP)); TOP = 58
    sh = Image.new('RGB', (CW * 4, TOP + 2 * hq + 2 * hs + 3 * 26), (38, 38, 38)); dr = ImageDraw.Draw(sh)
    dr.text((8, 5), '%s — %s' % (clip, w['use']), font=font, fill=(235, 235, 235))
    dr.text((8, 31), w.get('note', ''), font=small, fill=(200, 200, 200))
    y = TOP
    for cam, h, name in (('q34', hq, '앞 3/4(뿌리를 따라감)'), ('shoulder', hs, '게임 어깨 카메라(뒤 2.8 m · 위 0.55 m · 아래 8도 · FOV 60, 위쪽 잘라 냄)')):
        dr.text((8, y + 3), name, font=small, fill=(210, 210, 210)); y += 26
        for i, f in enumerate(fr):
            x = (i % 4) * CW; yy = y + (i // 4) * h; sh.paste(cell(clip, cam, f, CW), (x, yy)); t, ap = tlabel(w, f); label(dr, x, yy, t, fill=(255, 210, 90) if ap else (240, 240, 240))
        y += 2 * h
    p = '%s/sheet_%s.png' % (OUT, clip); sh.save(p); print('SHEET', p, sh.size)


def overview():
    cw = 176; hs = round(cw * (1 - TOPCROP)); LW = 1280 - cw * 6; rows = [c for c in W if os.path.exists('%s/%s_shoulder_%03d.png' % (ST, c, W[c]['frames'][0]))]
    sh = Image.new('RGB', (1280, 30 + len(rows) * hs), (38, 38, 38)); dr = ImageDraw.Draw(sh)
    dr.text((8, 5), 'GreatBrush308 motion1 — Mixamo 모캡을 플레이어 몸 + 큰 붓에 옮긴 것. 줄마다 어깨 카메라 6장(노란 글자 = 정점). Workbench', font=small, fill=(225, 225, 225))
    for r, c in enumerate(rows):
        w = W[c]; fr = w['frames']; pick = [fr[i] for i in (0, 2, 3, 4, 5, 7)] if len(fr) == 8 else list(fr)
        if w['apex'] in fr and w['apex'] not in pick: pick[2] = w['apex']; pick.sort()
        y = 30 + r * hs
        dr.text((6, y + 6), c.replace('_', ' ').replace('Standing ', 'St. ').replace('Great Sword', 'GS'), font=small, fill=(235, 235, 235)); dr.text((6, y + 28), w['use'], font=small, fill=(255, 210, 90))
        for k, t in enumerate(w.get('rowtext', [])): dr.text((6, y + 52 + k * 19), t, font=small, fill=(190, 190, 190))
        for i, f in enumerate(pick):
            x = LW + i * cw; sh.paste(cell(c, 'shoulder', f, cw), (x, y)); t, ap = tlabel(w, f); label(dr, x, y, t.split('  ')[1], fill=(255, 210, 90) if ap else (240, 240, 240))
    p = OUT + '/sheet.png'; sh.save(p); print('SHEET', p, sh.size)


names = [a for a in sys.argv[1:] if a in W] or list(W)
for c in names:
    if os.path.exists('%s/%s_q34_%03d.png' % (ST, c, W[c]['frames'][0])): clip_sheet(c)
overview()
