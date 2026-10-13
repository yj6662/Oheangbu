"""#308 GreatBrush motion2: 최종 클립 확인 시트. 클립마다 sheet_<이름>.png(앞 3/4 6장 + 어깨 카메라 6장) + 개요 sheet.png(어깨 카메라).   python sheet_m2.py [이름 ...]"""
import json, os, sys
from PIL import Image, ImageDraw, ImageFont
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
import clips_m2
OUT = os.path.abspath(os.path.join(HERE, '..', '..', '..', 'Art', 'Characters308', 'GreatBrush308', 'motion2')); ST = OUT + '/stills'
font = ImageFont.truetype('C:/Windows/Fonts/malgunbd.ttf', 17); small = ImageFont.truetype('C:/Windows/Fonts/malgun.ttf', 14); TOPCROP = .22


def cell(n, cam, f, w):
    im = Image.open('%s/%s_%s_%03d.png' % (ST, n, cam, f)).convert('RGB')
    if cam == 'shoulder': im = im.crop((0, int(im.height * TOPCROP), im.width, im.height))
    return im.resize((w, round(w * im.height / im.width)), Image.LANCZOS)


def label(dr, x, y, t, fill=(240, 240, 240)):
    w = dr.textlength(t, font=small); dr.rectangle((x, y, x + w + 10, y + 21), fill=(20, 20, 20)); dr.text((x + 5, y + 1), t, font=small, fill=fill)


def is_apex(n, f): a = clips_m2.CLIPS[n].get('apex'); return a is not None and f == min(clips_m2.SHOTS[n], key=lambda x: (round(abs(x / 30 - a), 6), -x))


def clip_sheet(n):
    fr = clips_m2.SHOTS[n]; S = json.load(open('%s/metrics/%s.json' % (OUT, n), encoding='utf-8'))['summary']; cw = 1280 // 6; hq = cw; hs = round(cw * (1 - TOPCROP)); TOP = 58
    sh = Image.new('RGB', (1280, TOP + hq + hs + 2 * 26), (38, 38, 38)); dr = ImageDraw.Draw(sh)
    dr.text((8, 5), 'A_Brush308_%s — %s   %.2f s (%d 프레임, 30 fps)' % (n, clips_m2.CLIPS[n]['use'], S['seconds'], S['frames']), font=font, fill=(235, 235, 235))
    dr.text((8, 31), '원본 %s   속도 배율 %s   왼손 쥠 %.0f %%   자루 관통 %d 프레임' % (S['src'], S.get('speed_factors'), 100 * S['l_on_share'], S['shaft_in_frames']), font=small, fill=(200, 200, 200))
    y = TOP
    for cam, h, name in (('q34', hq, '앞 3/4'), ('shoulder', hs, '게임 어깨 카메라(뒤 2.8 m · 위 0.55 m · 아래 8도 · FOV 60, 위쪽 잘라 냄)')):
        dr.text((8, y + 3), name, font=small, fill=(210, 210, 210)); y += 26
        for i, f in enumerate(fr):
            sh.paste(cell(n, cam, f, cw), (i * cw, y)); ap = is_apex(n, f); label(dr, i * cw, y, '%.2f s%s' % (f / 30, ' 정점' if ap else ''), (255, 210, 90) if ap else (240, 240, 240))
        y += h
    sh.save('%s/sheet_%s.png' % (OUT, n)); print('SHEET', n, sh.size)


def overview():
    cw = 176; hs = round(cw * (1 - TOPCROP)); LW = 1280 - cw * 6; rows = [n for n in clips_m2.CLIPS if os.path.exists('%s/%s_shoulder_%03d.png' % (ST, n, clips_m2.SHOTS[n][0]))]
    sh = Image.new('RGB', (1280, 30 + len(rows) * hs), (38, 38, 38)); dr = ImageDraw.Draw(sh)
    dr.text((8, 5), 'GreatBrush308 motion2 — 게임용 클립(v5 붓, 전투 태세에서 시작 · 끝, 제자리). 줄마다 어깨 카메라 6장(노란 글자 = 정점). Workbench', font=small, fill=(225, 225, 225))
    for r, n in enumerate(rows):
        S = json.load(open('%s/metrics/%s.json' % (OUT, n), encoding='utf-8'))['summary']; y = 30 + r * hs
        dr.text((6, y + 6), 'A_Brush308_' + n, font=small, fill=(235, 235, 235)); dr.text((6, y + 28), clips_m2.CLIPS[n]['use'][:22], font=small, fill=(255, 210, 90)); dr.text((6, y + 50), '%.2f s  배율 %s' % (S['seconds'], S.get('speed_factors') or '-'), font=small, fill=(190, 190, 190))
        for i, f in enumerate(clips_m2.SHOTS[n]):
            x = LW + i * cw; sh.paste(cell(n, 'shoulder', f, cw), (x, y)); ap = is_apex(n, f); label(dr, x, y, '%.2f s%s' % (f / 30, ' 정점' if ap else ''), (255, 210, 90) if ap else (240, 240, 240))
    sh.save(OUT + '/sheet.png'); print('SHEET overview', sh.size)


for n in ([a for a in sys.argv[1:] if a in clips_m2.CLIPS] or list(clips_m2.CLIPS)):
    if os.path.exists('%s/%s_q34_%03d.png' % (ST, n, clips_m2.SHOTS[n][0])): clip_sheet(n)
overview()
