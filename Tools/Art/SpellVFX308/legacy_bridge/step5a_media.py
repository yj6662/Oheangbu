#!/usr/bin/env python3
"""legacy bridge step 5a (D308-150 V9, task B): the lasting verb states before / after the vol.2 variants (Vol2LegacyLastingSet) - one mp4 per template and an index sheet.

  python Tools/Art/SpellVFX308/legacy_bridge/step5a_media.py [T ...]        (default: the 14 templates of step 5a)

Before = the step 4 frames (Art/SpellVFX308/Vol2Vfx/legacy_bridge/step4/frames/<T>_bridge4_<Body>_red/: the same rig - look=main realm=auto 적로, the bear, pitch 0
         shoulder camera + pitch2=35 reading view, apex=1; the stage with the variants off draws exactly that).
After  = .../step5a/frames/<T>_lasting1_<Body>_red/ (lasting=1).
Output: .../step5a/videos/<T>_before_after.mp4  five bodies at game speed (1x, 60 fps): before above, after below; left = the GAME camera (judge this one),
        right = the higher reading view (not the game camera)
        .../step5a/sheets/<T>_before_after.png   five bodies x (before | after) at the template's key moment, game camera
        .../step5a/index_step5a.png              the 14 templates, wood body (else the first with frames): before | after, game camera
"""
import csv
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont

REPO = Path(__file__).resolve().parents[4]
B4 = REPO / 'Art/SpellVFX308/Vol2Vfx/legacy_bridge/step4/frames'
BASE = REPO / 'Art/SpellVFX308/Vol2Vfx/legacy_bridge/step5a'
BODIES = ['Wood', 'Fire', 'Earth', 'Metal', 'Water']
BODY_KO = {'Wood': '목', 'Fire': '화', 'Earth': '토', 'Metal': '금', 'Water': '수'}
ROWS = {
    'P1': '각낙막삭악', 'P3': '감남맘삼암',
    'A1': '걱넉먹석억', 'A2': '건넌먼선언', 'A3': '검넘멈섬엄', 'A4': '것넛멋섯엇', 'A5': '겅넝멍성엉',
    'F1': '곡녹목속옥', 'F3': '곰놈몸솜옴',
    'R1': '국눅묵숙욱', 'R2': '군눈문순운', 'R3': '굼눔뭄숨움', 'R4': '굿눗뭇숫웃', 'R5': '궁눙뭉숭웅',
}
FRAME = {'P': 'ㅏ 경로', 'A': 'ㅓ 몸에 붙음', 'F': 'ㅗ 부채', 'R': 'ㅜ 띠'}
VERB = {'1': '남긴다', '2': '번진다', '3': '심는다', '4': '가른다', '5': '돌아온다'}
CHANGE = {
    'P': {'1': '잔흔 = 선 먹 덤불(결계 가닥 굵고 높게 + 가지) + 문양 고리', '3': '씨 = 몸 옆에 꽂힌 먹 말뚝(옛 막 획 셋) + 몸 위 回 인장 문양'},
    'F': {'1': '잔흔 = 선 먹 덤불 + 부채로 잘린 문양', '3': '씨 = 진한 띠 + 부채로 잘린 문양'},
    'R': {'1': '잔흔 = 선 먹 덤불 + 문양 고리', '2': '띠 = 진한 띠 + 문양 고리', '3': '씨 = 진한 띠 + 문양 고리', '4': '10 s 줄 = 진한 띠 + 문양 고리', '5': '오가는 띠 = 진한 띠 + 문양 고리'},
    'A': {'*': '띠 = 붓 · 위 소매에 감긴 먹 + 발치 테두리 진하게 · 가닥 높게 + 발치 문양 고리'},
}
KEY = {'P1': 2.5, 'P3': 1.2, 'F1': 2.5, 'F3': 1.1, 'R1': 2.5, 'R2': 1.6, 'R3': 1.1, 'R4': 2.2, 'R5': 1.0,
       'A1': 1.2, 'A2': 1.2, 'A3': 1.1, 'A4': 1.2, 'A5': 1.6}
FONT = 'C:/Windows/Fonts/malgun.ttf'


def font(n):
    try:
        return ImageFont.truetype(FONT, n)
    except OSError:
        return ImageFont.load_default()


def label(d, xy, text, f, fill=(240, 235, 225)):
    l, t, r, b = d.textbbox(xy, text, font=f)
    d.rectangle((l - 5, t - 3, r + 5, b + 3), fill=(0, 0, 0))
    d.text(xy, text, font=f, fill=fill)


def change(t):
    c = CHANGE[t[0]]
    return c.get(t[1], c.get('*', ''))


def rows_of(folder):
    if not (folder / 'index.csv').exists():
        return []
    rows = [r for r in csv.DictReader(open(folder / 'index.csv', encoding='utf-8')) if r['video'] == '1']
    return [r for r in rows if (folder / ('f_%05d.jpg' % int(r['frame']))).exists()]


def pair(folder, r, w=960, h=540):
    a = Image.open(folder / ('f_%05d.jpg' % int(r['frame']))).convert('RGB').resize((w, h), Image.LANCZOS)
    g = folder / ('g_%05d.jpg' % int(r['frame']))
    b = Image.open(g).convert('RGB').resize((w, h), Image.LANCZOS) if g.exists() else Image.new('RGB', (w, h), (16, 16, 16))
    return a, b


def folders(t, body):
    return B4 / ('%s_bridge4_%s_red' % (t, body)), BASE / 'frames' / ('%s_lasting1_%s_red' % (t, body))


def video(t, f2):
    import imageio_ffmpeg
    W, H = 1920, 1128
    out = BASE / 'videos'; out.mkdir(parents=True, exist_ok=True)
    seq = []
    for body in BODIES:
        fb, fa = folders(t, body)
        rb, ra = rows_of(fb), rows_of(fa)
        if rb and ra:
            seq.append((body, fb, fa, rb, ra))
    if not seq:
        print('no before/after pair for', t); return False
    p = out / ('%s_before_after.mp4' % t)
    gen = imageio_ffmpeg.write_frames(str(p), (W, H), fps=60, codec='libx264', pix_fmt_out='yuv420p', output_params=['-crf', '22', '-preset', 'medium'], macro_block_size=8)
    gen.send(None)
    for body, fb, fa, rb, ra in seq:
        n = max(len(rb), len(ra)); letter = ROWS[t][BODIES.index(body)]
        lb = la = ''
        for i in range(n):
            b = rb[min(i, len(rb) - 1)]; a = ra[min(i, len(ra) - 1)]
            img = Image.new('RGB', (W, H), (16, 16, 16)); d = ImageDraw.Draw(img)
            bg, br = pair(fb, b); ag, ar = pair(fa, a)
            img.paste(bg, (0, 40)); img.paste(br, (960, 40)); img.paste(ag, (0, 586)); img.paste(ar, (960, 586))
            label(d, (10, 6), '%s %s × %s  |  %s %s  |  위: 4단계(옛 부품 그대로)  ·  아래: 5a 변형 — %s  |  왼쪽 = 게임 카메라(피치 0°, 판정) · 오른쪽 = 판독용 높은 시점' % (t, FRAME[t[0]], VERB[t[1]], letter, BODY_KO[body], change(t)), f2)
            label(d, (12, 50), '전 %.2f s' % float(b['ageSec']), f2); label(d, (12, 596), '후 %.2f s' % float(a['ageSec']), f2)
            if b['note']: lb = b['note']
            if a['note']: la = a['note']
            if la:
                label(d, (12, H - 34), la[:110], f2, fill=(255, 220, 120))
            fbytes = np.asarray(img).tobytes()
            for _ in range(20 if i == n - 1 else 1):
                gen.send(fbytes)
    gen.close(); print('video', p)
    return True


def sheet(t, f2):
    cw, ch = 480, 270
    out = BASE / 'sheets'; out.mkdir(parents=True, exist_ok=True)
    img = Image.new('RGB', (cw * 2, ch * 5 + 50), (16, 16, 16)); d = ImageDraw.Draw(img)
    label(d, (8, 12), '%s %s×%s @%.1f s — 왼쪽 전(4단계) · 오른쪽 후(5a), 게임 카메라' % (t, FRAME[t[0]], VERB[t[1]], KEY[t]), font(18))
    for bi, body in enumerate(BODIES):
        fb, fa = folders(t, body)
        for ci, fo in enumerate((fb, fa)):
            rr = rows_of(fo)
            if not rr:
                continue
            r = min(rr, key=lambda q: abs(float(q['ageSec']) - KEY[t]))
            a, _ = pair(fo, r, cw, ch)
            img.paste(a, (ci * cw, 50 + bi * ch))
            label(d, (ci * cw + 6, 54 + bi * ch), '%s %s %s %.2f s' % (ROWS[t][bi], BODY_KO[body], '전' if ci == 0 else '후', float(r['ageSec'])), f2)
    p = out / ('%s_before_after.png' % t); img.save(p); print('sheet', p)


def index(f2):
    cw, ch = 480, 270
    cols = 2  # (before | after) pairs, two templates per row
    ts = list(ROWS)
    rows = (len(ts) + 1) // 2
    img = Image.new('RGB', (cw * 4, (ch + 30) * rows + 60), (16, 16, 16)); d = ImageDraw.Draw(img)
    label(d, (10, 14), 'vol.2 받침 — 오래가는 동사 상태 5a (D308-150 V9) · 칸마다 왼쪽 전(4단계 옛 부품) · 오른쪽 후(5a 변형), 게임 카메라 피치 0° · 목 글자', font(22))
    for i, t in enumerate(ts):
        x, y = (i % cols) * cw * 2, 60 + (i // cols) * (ch + 30)
        body = next((b for b in BODIES if rows_of(folders(t, b)[0]) and rows_of(folders(t, b)[1])), None)
        label(d, (x + 6, y + 2), '%s %s×%s  %s — %s' % (t, FRAME[t[0]][2:], VERB[t[1]], ROWS[t], change(t)), f2)
        if body is None:
            continue
        for ci, fo in enumerate(folders(t, body)):
            rr = rows_of(fo)
            r = min(rr, key=lambda q: abs(float(q['ageSec']) - KEY[t]))
            a, _ = pair(fo, r, cw, ch)
            img.paste(a, (x + ci * cw, y + 30))
            label(d, (x + ci * cw + 6, y + 36), '%s %s %.2f s' % ('전' if ci == 0 else '후', ROWS[t][BODIES.index(body)], float(r['ageSec'])), f2)
    p = BASE / 'index_step5a.png'; img.save(p); print('index', p)


def main():
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
    want = sys.argv[1:] or list(ROWS)
    f2 = font(18)
    for t in want:
        if t == 'index':
            continue
        if video(t, f2):
            sheet(t, f2)
    index(f2)


if __name__ == '__main__':
    main()
