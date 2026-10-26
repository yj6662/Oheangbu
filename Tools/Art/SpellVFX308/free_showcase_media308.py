#!/usr/bin/env python3
"""D308-150 V10-V11 free showcase media: engine frames (Vol2VfxCapture, out=free_showcase/<round>) -> per-spell mp4 (1x then 0.5x),
a side-by-side still (concept | engine frame at the matching moment), a frame contact sheet, and one combined mp4 of all three.

  python Tools/Art/SpellVFX308/free_showcase_media308.py r1                     # sheets only (to pick the moments)
  python Tools/Art/SpellVFX308/free_showcase_media308.py r1 --video na=0.40 go=0.70 gak=1.40   # + videos and side-by-sides at those ages (s)
Out: Art/SpellVFX308/Vol2Vfx/free_showcase/<round>/media/"""
import csv, sys
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

REPO = Path(__file__).resolve().parents[3]
BASE = REPO / 'Art/SpellVFX308/Vol2Vfx/free_showcase'
CONCEPTS = BASE / 'concepts'
SPELLS = [  # key, template prefix of the job folder, concept(s), label
    ('na', 'P0_', ['na_b.png', 'na_c.png'], '나 · 화 · ㅏ 경로 · 한 번 — 붓끝에서 불화살이 첫 적에게 날아가 한 번 맞고 2초 태운다'),
    ('go', 'F0_', ['go_b.png'], '고 · 목 · ㅗ 면 · 한 번 — 앞 부채꼴(5 m)을 덩굴·죽순이 한 번 쓸고 안의 적을 붙든다'),
    ('gak', 'P1_', ['gak_a.png', 'gak_c.png'], '각 · 목 · ㅏ 경로 · 남긴다 — 맞은 적의 다리를 가지 새장이 1.5초 붙들고, 풀린 자리에 덤불이 남는다'),
]
# batch 1 (2026-10-26, D308-150 V12): --batch b1 -> free_showcase/batch1/<round>/, the concepts in batch1/concepts, keys by the job folder's template + body
B1_SPELLS = [
    ('ga', 'P0_bridge_Wood_', ['ga_a.png'], '가 · 목 · ㅏ 경로 · 한 번 — 가시 덩굴 작살이 첫 적을 꿰어 감고 2초 붙든다, 끝에 잔뿌리로 풀림'),
    ('ma', 'P0_bridge_Earth_', ['ma_a.png'], '마 · 토 · ㅏ 경로 · 한 번 — 바위덩이가 첫 적을 들이받아 민다, 둘레 땅이 판으로 들린다'),
    ('sa', 'P0_bridge_Metal_', ['sa_b.png'], '사 · 금 · ㅏ 경로 · 한 번 — 던진 검이 첫 적을 꿰어 베고 쨍 하고 파편이 옆으로 튄다'),
    ('a', 'P0_bridge_Water_', ['a_b.png'], '아 · 수 · ㅏ 경로 · 한 번 — 물 구슬이 첫 적 위에서 터져 덮어 적신다(4초), 물이 흘러내린다'),
    ('no', 'F0_bridge_Fire_', ['no_a.png'], '노 · 화 · ㅗ 면 · 한 번 — 앞 부채꼴에 불길이 바깥으로 번져 안의 적을 태운다(2초), 가장자리부터 재로 날림'),
    ('mo', 'F0_bridge_Earth_', ['mo_b.png', 'mo_a.png'], '모 · 토 · ㅗ 면 · 한 번 — 부채꼴 땅이 줄줄이 판으로 솟아 안의 적을 민다, 가라앉으며 둘레가 들린다'),
    ('so', 'F0_bridge_Metal_', ['so_a.png'], '소 · 금 · ㅗ 면 · 한 번 — 부챗살 칼날 다발이 부채꼴을 훑어 안의 적을 벤다, 끝에서 쨍 하고 부서진다'),
    ('o', 'F0_bridge_Water_', ['o_a.png'], '오 · 수 · ㅗ 면 · 한 번 — 말려 오르는 물결이 부채꼴을 덮쳐 안의 적을 적신다(4초), 땅이 젖는다'),
]
# batch 2 (2026-10-26, D308-150 V12): --batch b2 -> free_showcase/batch2/<round>/ (A0 ㅓ guard with the lab-scripted enemy blow, R0 ㅜ ring)
B2_SPELLS = [
    ('geo', 'A0_bridge_Wood_', ['geo_b.png'], '거 · 목 · ㅓ 부착 · 한 번 — 붓대에 덩굴과 대나무 십자가 서서 곰의 앞발을 받아치고 덩굴로 붙든다, 끝에 잔뿌리로 풀림'),
    ('neo', 'A0_bridge_Fire_', ['neo_a.png'], '너 · 화 · ㅓ 부착 · 한 번 — 붓대에 불 바퀴가 돌다 받아치는 순간 불꽃 모란이 핀다, 꽃잎이 끝부터 재로 날림'),
    ('meo', 'A0_bridge_Earth_', ['meo_a.png'], '머 · 토 · ㅓ 부착 · 한 번 — 앞에 돌 판벽이 솟아 앞발을 받고 흙먼지 고리, 가라앉으며 둘레 땅이 판으로 들림'),
    ('seo', 'A0_bridge_Metal_', ['seo_a.png'], '서 · 금 · ㅓ 부착 · 한 번 — 붓대에 강철 초승달 날이 서서 받아치면 불꽃이 옆으로 솟고, 쨍 하고 부서져 파편이 옆으로'),
    ('eo', 'A0_bridge_Water_', ['eo_a.png'], '어 · 수 · ㅓ 부착 · 한 번 — 붓 앞에 물 소용돌이 원반이 돌다 받아치면 물보라 왕관, 물이 아래로 흘러내림'),
    ('gu', 'R0_bridge_Wood_', ['gu_a.png'], '구 · 목 · ㅜ 둘레 · 한 번 — 2 m 고리의 빛나는 균열에서 가시 덩굴 관이 솟아 고리 위 적을 붙든다, 잔뿌리로 풀림'),
    ('nu', 'R0_bridge_Fire_', ['nu_a.png'], '누 · 화 · ㅜ 둘레 · 한 번 — 달아오른 고리 선에서 불의 벽이 솟고 먹 연기가 오른다, 고리 위 적이 탄다, 끝부터 재로'),
    ('mu', 'R0_bridge_Earth_', ['mu_a.png'], '무 · 토 · ㅜ 둘레 · 한 번 — 땅이 둥근 물결 둔덕으로 솟아 굴러 나가며 고리 위 적을 민다, 흙먼지 도넛, 둘레 땅이 판으로 들림'),
    ('su', 'R0_bridge_Metal_', ['su_a.png'], '수 · 금 · ㅜ 둘레 · 한 번 — 칼날 고리가 톱처럼 돌며 고리 위 적을 벤다(바닥에 KCISA 톱니 고리), 쨍 하고 부서져 파편이 옆으로'),
    ('u', 'R0_bridge_Water_', ['u_b.png'], '우 · 수 · ㅜ 둘레 · 한 번 — 고리를 따라 물기둥이 솟아 먹 리본이 감기고, 고리 위 적에게 쏟아져 적신다, 땅으로 흘러내림'),
]
if '--batch' in sys.argv and sys.argv[sys.argv.index('--batch') + 1] == 'b1':
    BASE = BASE / 'batch1'
    CONCEPTS = BASE / 'concepts'
    SPELLS = B1_SPELLS
if '--batch' in sys.argv and sys.argv[sys.argv.index('--batch') + 1] == 'b2':
    BASE = BASE / 'batch2'
    CONCEPTS = BASE / 'concepts'
    SPELLS = B2_SPELLS
W, H = 1280, 720
FONT = 'C:/Windows/Fonts/malgunbd.ttf'


def font(size):
    try:
        return ImageFont.truetype(FONT, size)
    except OSError:
        return ImageFont.load_default()


def job_dir(rnd, prefix):
    frames = BASE / rnd / 'frames'
    c = sorted([d for d in frames.iterdir() if d.is_dir() and d.name.startswith(prefix)]) if frames.exists() else []
    return c[0] if c else None


def rows(d):
    with open(d / 'index.csv', encoding='utf-8', errors='replace') as f:
        return [r for r in csv.DictReader(f) if r.get('video') == '1']


def frame_path(d, r):
    return d / ('f_%05d.jpg' % int(r['frame']))


def label(img, text, sub=None):
    img = img.convert('RGB'); dr = ImageDraw.Draw(img)
    f = font(26); x, y = 18, 14
    bb = dr.textbbox((x, y), text, font=f)
    dr.rectangle((bb[0] - 8, bb[1] - 6, bb[2] + 8, bb[3] + 6), fill=(0, 0, 0))
    dr.text((x, y), text, font=f, fill=(255, 255, 255))
    if sub:
        f2 = font(20); bb = dr.textbbox((x, H - 44), sub, font=f2)
        dr.rectangle((bb[0] - 8, bb[1] - 6, bb[2] + 8, bb[3] + 6), fill=(0, 0, 0))
        dr.text((x, H - 44), sub, font=f2, fill=(255, 230, 200))
    return img


def sheet(rnd, key, d):
    rs = rows(d)
    step = max(1, len(rs) // 40)
    pick = rs[::step][:40]
    tw, th = 320, 180
    out = Image.new('RGB', (tw * 8, th * ((len(pick) + 7) // 8)), (0, 0, 0))
    dr = ImageDraw.Draw(out)
    for i, r in enumerate(pick):
        p = frame_path(d, r)
        if not p.exists():
            continue
        im = Image.open(p).resize((tw, th))
        out.paste(im, ((i % 8) * tw, (i // 8) * th))
        dr.text(((i % 8) * tw + 6, (i // 8) * th + 4), '%s f%s' % (r['ageSec'], r['frame']), font=font(16), fill=(255, 255, 0))
    dst = BASE / rnd / 'media' / f'{key}_sheet.jpg'
    dst.parent.mkdir(parents=True, exist_ok=True)
    out.save(dst, quality=88)
    print('sheet', dst)


def at_age(d, age):
    rs = rows(d)
    best = min(rs, key=lambda r: abs(float(r['ageSec']) - age))
    return frame_path(d, best), best


def side_by_side(rnd, key, d, ages, concepts):
    outs = []
    for i, age in enumerate(ages):
        p, r = at_age(d, age)
        eng = label(Image.open(p).resize((W, H)), 'ENGINE (실제 게임 카메라) · %.2f s' % float(r['ageSec']))
        con = label(Image.open(CONCEPTS / concepts[min(i, len(concepts) - 1)]).resize((W, H)), 'CONCEPT ' + concepts[min(i, len(concepts) - 1)][:-4])
        sbs = Image.new('RGB', (W * 2 + 16, H), (20, 20, 20))
        sbs.paste(con, (0, 0)); sbs.paste(eng, (W + 16, 0))
        dst = BASE / rnd / f'{key}_side_by_side{"" if i == 0 else "_" + str(i + 1)}.jpg'
        sbs.save(dst, quality=92)
        outs.append(dst)
        print('side', dst)
    return outs


def frames_for(d, text):
    rs = rows(d)
    one = [label(Image.open(frame_path(d, r)).resize((W, H)), text + ' · 1×', 't = %s s' % r['ageSec']) for r in rs if frame_path(d, r).exists()]
    half = []
    for r in rs:
        p = frame_path(d, r)
        if p.exists():
            im = label(Image.open(p).resize((W, H)), text + ' · 0.5×', 't = %s s' % r['ageSec'])
            half += [im, im]
    return one, half


def write_mp4(dst, frames, fps=60):
    import imageio_ffmpeg
    gen = imageio_ffmpeg.write_frames(str(dst), (W, H), fps=fps, codec='libx264', pix_fmt_out='yuv420p', output_params=['-crf', '19', '-preset', 'medium'], macro_block_size=8)
    gen.send(None)
    for im in frames:
        gen.send(im.convert('RGB').tobytes())
    gen.close()
    print('mp4', dst, len(frames))


def title(concept, text, seconds=1.5, fps=60):
    im = Image.open(CONCEPTS / concept).resize((W, H))
    im = label(im, 'CONCEPT (자유 시안) · ' + concept[:-4], text)
    return [im] * int(seconds * fps)


def main():
    rnd = sys.argv[1]
    ages = {}
    if '--video' in sys.argv:
        for a in sys.argv[sys.argv.index('--video') + 1:]:
            if a.startswith('--'):
                break
            k, v = a.split('='); ages[k] = [float(x) for x in v.split('/')]
    combined = []
    for key, prefix, concepts, text in SPELLS:
        d = job_dir(rnd, prefix)
        if d is None:
            print('NO FRAMES', key); continue
        sheet(rnd, key, d)
        if key not in ages:
            continue
        side_by_side(rnd, key, d, ages[key], concepts)
        one, half = frames_for(d, text.split(' — ')[0])
        write_mp4(BASE / rnd / f'{key}_1x_then_half.mp4', one + half)
        combined += title(concepts[0], text) + one + half
    if combined:
        write_mp4(BASE / rnd / ('free_showcase_batch2_all.mp4' if SPELLS is B2_SPELLS else 'free_showcase_batch1_all.mp4' if SPELLS is B1_SPELLS else 'free_showcase_all.mp4'), combined)


if __name__ == '__main__':
    main()
