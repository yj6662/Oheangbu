#!/usr/bin/env python3
"""NB20 judge calibration (coordinator 2026-10-25): the same rubric as vol2vfx9a_judge.py on
   AJ_lab  - the approved 앞잡 one stroke in the vol.2 capture rig (lab look: LookProfile308_PBZInk, realm weights of the lab = Hwanggyeong)
   AJ_main - the same stroke under the main world's look (LookProfile308_PBZInkWash, the look stepped at a point of 적로 / 청림)
   P0_main - the NB20 r6 P0 (wood, fire) at 0.18 s under the main world's look
   P0_lab  - the same r6 P0 (wood, fire) frames of round r6 (lab look), cut the same way, for an apples-to-apples pair
python Tools/Art/SpellVFX308/nb20/calib_judge.py [sheets] [judge]"""
import importlib.util
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

REPO = Path(__file__).resolve().parents[4]
NB = REPO / 'Art/SpellVFX308/Vol2Vfx/nb20'
OUT = NB / 'calib'
J9A = REPO / 'Tools/Art/SpellVFX308/vol2vfx9a_judge.py'
CW, CH = 960, 540


def key(folder, tag, pitch=15):
    ps = sorted(folder.glob('key_%s_p%02d_*.png' % (tag, pitch)))
    if not ps:
        return None
    want = {'r025': 0.25, 'r070': 0.70, 'b018': 0.183, 'a008': 0.083}.get(tag, 0)
    return min(ps, key=lambda p: abs(float(p.stem.split('_')[-1]) - want))


def sheet(name, cells, crop):
    img = Image.new('RGB', (2 * CW, 2 * CH), (24, 22, 20)); d = ImageDraw.Draw(img)
    for i, (p, label) in enumerate(cells):
        x, y = (i % 2) * CW, (i // 2) * CH
        if p is None:
            d.text((x + 10, y + 10), 'missing ' + label, fill=(220, 120, 120)); continue
        im = Image.open(p).convert('RGB').crop((crop[0], crop[1], crop[0] + CW, crop[1] + CH))
        img.paste(im, (x, y))
        f = ImageFont.truetype('C:/Windows/Fonts/malgun.ttf', 20); l, t_, r, b = d.textbbox((x + 6, y + 4), label, font=f); d.rectangle((l - 6, t_ - 3, r + 6, b + 3), fill=(0, 0, 0)); d.text((x + 6, y + 4), label, font=f, fill=(240, 235, 225))
    OUT.mkdir(parents=True, exist_ok=True)
    p = OUT / (name + '.png'); img.save(p)
    img.convert('L').convert('RGB').save(OUT / (name + '_grey.png'))
    return p


def build():
    lab, main, r6 = NB / 'calib_lab/frames', NB / 'calib_main/frames', NB / 'r6/frames'
    aj = (300, 200)
    sheet('AJ_lab', [(key(lab / 'AJ_main_Wood_red', 'r025'), 'AJ lab look | 적로 soil | 0.25 s'), (key(lab / 'AJ_main_Wood_red', 'r070'), 'AJ lab look | 적로 soil | 0.70 s'),
                     (key(lab / 'AJ_main_Wood_green', 'r025'), 'AJ lab look | 청림 soil | 0.25 s'), (key(lab / 'AJ_main_Wood_green', 'r070'), 'AJ lab look | 청림 soil | 0.70 s')], aj)
    sheet('AJ_main', [(key(main / 'AJ_main_Wood_red', 'r025'), 'AJ main look (적로) | 0.25 s'), (key(main / 'AJ_main_Wood_red', 'r070'), 'AJ main look (적로) | 0.70 s'),
                      (key(main / 'AJ_main_Wood_green', 'r025'), 'AJ main look (청림) | 0.25 s'), (key(main / 'AJ_main_Wood_green', 'r070'), 'AJ main look (청림) | 0.70 s')], aj)
    p0 = (430, 330)
    sheet('P0_main', [(key(main / 'P0_main_Wood_red', 'b018'), 'P0 NB20 r6 가 wood | main look 적로 | 0.18 s'), (key(main / 'P0_main_Fire_red', 'b018'), 'P0 NB20 r6 나 fire | main look 적로 | 0.18 s'),
                      (key(main / 'P0_main_Wood_green', 'b018'), 'P0 wood | main look 청림 | 0.18 s'), (key(main / 'P0_main_Fire_green', 'b018'), 'P0 fire | main look 청림 | 0.18 s')], p0)
    sheet('P0_lab', [(key(r6 / 'P0_main_Wood_red', 'b018'), 'P0 NB20 r6 가 wood | lab look 적로 soil | 0.18 s'), (key(r6 / 'P0_main_Fire_red', 'b018'), 'P0 NB20 r6 나 fire | lab look 적로 soil | 0.18 s'),
                     (key(r6 / 'P0_main_Wood_green', 'b018'), 'P0 wood | lab look 청림 soil | 0.18 s'), (key(r6 / 'P0_main_Fire_green', 'b018'), 'P0 fire | lab look 청림 soil | 0.18 s')], p0)
    print('sheets ->', OUT)


def judge():
    spec = importlib.util.spec_from_file_location('j9a', J9A)
    j = importlib.util.module_from_spec(spec); spec.loader.exec_module(j)
    j.ROOT = OUT
    # the same rubric; the template line names what is on the sheet (the 앞잡 is not a 24-template row: the judge is told so)
    j.VERB.update({'AJ_lab': '앞잡 일필휘지 한 획(사용자 승인 D308-148, 24템플릿 표 밖 - 표 행 대신 D308-148을 기준으로)', 'AJ_main': '앞잡 일필휘지 한 획(사용자 승인 D308-148, 24템플릿 표 밖 - 표 행 대신 D308-148을 기준으로)',
                   'P0_main': '한 번 (P0 행)', 'P0_lab': '한 번 (P0 행)'})
    j.ROWS.update({'AJ_lab': '(앞잡: 몸 구분 없음)', 'AJ_main': '(앞잡: 몸 구분 없음)', 'P0_main': '가 나 (목 · 화 두 몸만)', 'P0_lab': '가 나 (목 · 화 두 몸만)'})
    lines = []
    for t in ('AJ_lab', 'AJ_main', 'P0_main', 'P0_lab'):
        imgs = [OUT / (t + '.png'), OUT / (t + '_grey.png')]
        score, _ = j.judge(t, 'calib', imgs)
        lines.append('%s: %s' % (t, score)); print(lines[-1], flush=True)
    (OUT / 'judge' / 'calib' / 'summary.txt').write_text('\n'.join(lines) + '\n', encoding='utf-8')


if __name__ == '__main__':
    args = sys.argv[1:] or ['sheets', 'judge']
    if 'sheets' in args:
        build()
    if 'judge' in args:
        judge()
