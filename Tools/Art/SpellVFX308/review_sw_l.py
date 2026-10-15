"""#308 D308-84: the motion-by-place and ink-bridge Play check (steps sw_l) on one sheet.

  python Tools/Art/SpellVFX308/review_sw_l.py

Rows = one cast each (free aim / near / far / high target), columns = the shots asked at later moments.
Output: Art/SpellVFX308/Review/swingplay_sw_l.png
"""
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[3]
SHOTS = ROOT / 'Art/Playtest306/Checks'
ROWS = [('free_ga', '가 · 대상 없음 → 내려 던지기'), ('free_go', '고 · 대상 없음 → 멀리 찍기 + 바닥 먹'), ('ward_gu', '구 · 방벽 짧은 내리찍기'),
        ('path_mo', '모 · 경로 땅 찍기'), ('near_ga', '가 · 5 m → 쓸기 + 줄기'), ('near_go', '고 · 4 m → 땅 찍기 + 바닥 먹'),
        ('far_ga', '가 · 12 m → 내려 던지기'), ('far_go', '고 · 12 m → 멀리 찍기 + 바닥 먹'), ('high_ga', '가 · 6 m 높이 4 m → 상체 조준')]
ROWS_M = [('near_ga', '가 · 5 m 락온 → 쓸기 + 줄기'), ('far_ga', '가 · 12 m 락온 → 내려 던지기 + 줄기'), ('near_go', '고 · 4 m → 땅 찍기 + 바닥 먹'),
          ('far_go', '고 · 12 m → 멀리 찍기 + 바닥 먹'), ('ward_gu', '구 · 방벽 짧은 내리찍기'), ('high_ga', '가 · 6 m 높이 4 m → 상체 조준')]
ROWS_O = [('near_go', '고 · 4 m → 땅 찍기 + 바닥 먹 얼룩'), ('far_go', '고 · 12 m → 멀리 찍기 + 바닥 먹 얼룩'), ('near_ga', '가 · 5 m → 줄기(얼룩 없어야 함)')]
ROWS_P = [('near_ga', '가 · 5 m → 줄기 → 먹으로 맺힘 → 색 스밈 → 잔먹'), ('far_ga', '가 · 12 m'), ('near_go', '고 · 4 m → 바닥 먹 → 새 원 몸'), ('mid_ma', '마 · 8 m')]
ROWS_X = [('ga', '가 · 5 m 단발'), ('go', '고 · 5 m 원'), ('no', '노 · 6 m 부채'), ('so', '소 · 10 m 연발'), ('mo', '모 · 8 m 경로'), ('geo', '거 · 방어'), ('gu', '구 · 결계'), ('geok', '걱 · 강화'), ('gom', '곰 · 소환')]
ROWS_Y = [('no', '노 · 6 m 부채 (정점 뒤)'), ('so', '소 · 10 m 연발 (정점 뒤)'), ('mo', '모 · 8 m 경로 (정점 뒤)')]
ROWS_Z = [('so', '소 · 10 m 연발'), ('mo', '모 · 8 m 경로 (머리 축소)'), ('geo', '거 · 방어 먹 막'), ('meo', '머 · 방어 먹 막'), ('gak', '각 · ㄱ받침 #308 볼트'), ('sak', '삭 · ㄱ받침 #308 볼트'), ('gu', '구 · 결계 먹 막 루프'), ('geok', '걱 · 강화 먹 띠'), ('gom', '곰 · 소환 먹 몸')]
ROWS_Z2 = [('geo', '거 · 방어 먹 막'), ('meo', '머 · 방어 먹 막'), ('gak', '각 · ㄱ받침'), ('sak', '삭 · ㄱ받침'), ('gu', '구 · 결계'), ('geok', '걱 · 강화')]
ROWS_Z3 = [('geo', '거 · 방어 먹 막 (키움)'), ('meo', '머 · 방어 먹 막 (키움)'), ('geok', '걱 · 강화 (보통 속도)')]
ROWS_H = [('geok', '걱 · 강화 (배율 고침)')]
ROWS_H3 = [('neo', '너 · 방어 (옛 불 방패 뺌)'), ('geok', '걱 · 강화 (띠 .9-1.3)'), ('neok', '넉 · 강화')]
ROWS_H2 = [('geok', '걱 · 강화 (띠 반경 .62 · 진하게)'), ('gu', '구 · 결계')]
ROWS_CAM = [('c0_ga', '가 12 m · 카메라 x 0 (지금)'), ('c5_ga', '가 · x 0.5'), ('c7_ga', '가 · x 0.7'), ('c0_no', '노 6 m · x 0'), ('c5_no', '노 · x 0.5'), ('c7_no', '노 · x 0.7'),
            ('c0_so', '소 10 m · x 0'), ('c5_so', '소 · x 0.5'), ('c7_so', '소 · x 0.7'),
            ('f_ga', '가 · 받침 없음 (궤적)'), ('f_gan', '간 · ㄴ 두 번째 번짐'), ('f_gat', '갓 · ㅅ 가는 궤적 + X'), ('f_gang', '강 · ㅇ 먹 고리'),
            ('f_na', '나 · 받침 없음'), ('f_nan', '난 · ㄴ'), ('f_nat', '낫 · ㅅ'), ('f_nang', '낭 · ㅇ')]
W, H = 560, 350


def main():
    import sys
    run = sys.argv[1] if len(sys.argv) > 1 else 'sw_l'
    rows = ROWS_CAM if run == 'sw_cam' else ROWS_H3 if run == 'sw_hitch3' else ROWS_H2 if run == 'sw_hitch2' else ROWS_H if run == 'sw_hitch' else ROWS_Z3 if run == 'sw_z3' else ROWS_Z2 if run == 'sw_z2' else ROWS_Z if run == 'sw_z' else ROWS_Y if run == 'sw_y' else ROWS_X if run == 'sw_x' else ROWS_P if run in ('sw_p', 'sw_q', 'sw_r', 'sw_s', 'sw_t', 'sw_u', 'sw_v', 'sw_w', 'sw_x', 'sw_y', 'sw_z', 'sw_z2', 'sw_z3', 'sw_hitch') else ROWS_O if run == 'sw_o' else ROWS_M if run in ('sw_m', 'sw_n') else ROWS
    cols = 4 if run in ('sw_m', 'sw_n', 'sw_o', 'sw_p', 'sw_q', 'sw_r', 'sw_s', 'sw_t', 'sw_u', 'sw_v', 'sw_w', 'sw_x', 'sw_y', 'sw_z', 'sw_z2', 'sw_z3', 'sw_hitch', 'sw_hitch2', 'sw_hitch3', 'sw_cam') else 3
    font = ImageFont.truetype('C:/Windows/Fonts/malgunbd.ttf', 20)
    sheet = Image.new('RGB', (W * cols, H * len(rows)), (20, 20, 20))
    d = ImageDraw.Draw(sheet)
    missing = []
    for y, (key, label) in enumerate(rows):
        for x in range(cols):
            path = SHOTS / f'{run}_{key}_{x}_2560x1440.png'
            if not path.exists():
                if x == 0:
                    missing.append(path.name)
                continue
            im = Image.open(path).convert('RGB')
            w, h = im.size
            box = (0, 0, w, h) if run == 'sw_cam' and key.startswith('c') else (int(w * .15), int(h * .15), int(w * .85), int(h * .95))
            sheet.paste(im.crop(box).resize((W, H)), (x * W, y * H))
        d.rectangle((0, y * H, d.textlength(label, font=font) + 14, y * H + 30), fill=(0, 0, 0))
        d.text((7, y * H + 2), label, font=font, fill=(255, 225, 120))
    out = ROOT / f'Art/SpellVFX308/Review/swingplay_{run}.png'
    sheet.save(out)
    print(out)
    if missing:
        print('missing:', ', '.join(missing))


if __name__ == '__main__':
    main()
