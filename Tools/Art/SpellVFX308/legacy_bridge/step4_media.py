#!/usr/bin/env python3
"""legacy bridge step 4 (D308-150 V9): the 100 batchim glyphs as the bridge plays them - one mp4 per template (five bodies) and one index sheet of the 20.

  python Tools/Art/SpellVFX308/legacy_bridge/step4_media.py [T ...]        (default: every template that has frames)

Input : Art/SpellVFX308/Vol2Vfx/legacy_bridge/step4/frames/<T>_bridge4_<Body>_red/  f_NNNNN.jpg (game camera: shoulder, pitch 0) + g_NNNNN.jpg (reading view: the same rig
        pulled back x2, pitch 35 - NOT the game camera), index.csv (scope-clock age, note = LAB-SIMULATED steps and JUMPs of the scope clock)
Output: .../step4/videos/<T>_bridge4.mp4   five bodies: every frame at game speed (1x, 60 fps), then the first 2.0 s of each body again at 0.25x
        .../step4/sheets/<T>_bridge4.png   five bodies x six moments (game camera above, reading view below)
        .../step4/index_step4.png          the 20 templates, one key moment each (Wood body unless no frames), game camera + reading view
"""
import csv
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont

REPO = Path(__file__).resolve().parents[4]
BASE = REPO / 'Art/SpellVFX308/Vol2Vfx/legacy_bridge/step4'
BODIES = ['Wood', 'Fire', 'Earth', 'Metal', 'Water']
BODY_KO = {'Wood': '목', 'Fire': '화', 'Earth': '토', 'Metal': '금', 'Water': '수'}
ROWS = {
    'P1': '각낙막삭악', 'P2': '간난만산안', 'P3': '감남맘삼암', 'P4': '갓낫맛삿앗', 'P5': '강낭망상앙',
    'A1': '걱넉먹석억', 'A2': '건넌먼선언', 'A3': '검넘멈섬엄', 'A4': '것넛멋섯엇', 'A5': '겅넝멍성엉',
    'F1': '곡녹목속옥', 'F2': '곤논몬손온', 'F3': '곰놈몸솜옴', 'F4': '곳놋못솟옷', 'F5': '공농몽송옹',
    'R1': '국눅묵숙욱', 'R2': '군눈문순운', 'R3': '굼눔뭄숨움', 'R4': '굿눗뭇숫웃', 'R5': '궁눙뭉숭웅',
}
FRAME = {'P': 'ㅏ 경로', 'A': 'ㅓ 몸에 붙음', 'F': 'ㅗ 부채', 'R': 'ㅜ 띠'}
VERB = {'1': '남긴다', '2': '번진다', '3': '심는다', '4': '가른다', '5': '돌아온다'}
WHAT = {
    '1': '잔흔이 서 있다가(10 s, 몸 6 s) 만료 경고 뒤 마른다',
    '2': '맞은 몸에서 이웃으로 파생탄이 가고, 이웃 발밑 자국(3 s)이 남았다 사라진다',
    '3': '씨가 기다리다(8 s) 조건이 오면 터진다',
    '4': '벗긴 몸에 X 획과 선 획이 벗김 끝(3 s)까지 선다',
    '5': '나갔다 돌아온다 (돌아오는 먹은 붓으로 풀려 들어감)',
}
KEY = {'P1': 2.5, 'P2': 0.9, 'P3': 1.9, 'P4': 1.0, 'P5': 0.6, 'A1': 0.6, 'A2': 0.7, 'A3': 1.5, 'A4': 1.0, 'A5': 1.5,
       'F1': 1.5, 'F2': 1.0, 'F3': 1.6, 'F4': 1.0, 'F5': 0.6, 'R1': 1.5, 'R2': 1.3, 'R3': 1.6, 'R4': 1.5, 'R5': 0.6}
FONT = 'C:/Windows/Fonts/malgun.ttf'
SLOW_SECONDS = 2.0


def font(n):
    try:
        return ImageFont.truetype(FONT, n)
    except OSError:
        return ImageFont.load_default()


def label(d, xy, text, f, fill=(240, 235, 225)):
    l, t, r, b = d.textbbox(xy, text, font=f)
    d.rectangle((l - 5, t - 3, r + 5, b + 3), fill=(0, 0, 0))
    d.text(xy, text, font=f, fill=fill)


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


def video(t, f2, f3):
    import imageio_ffmpeg
    W, H = 1920, 624
    out = BASE / 'videos'; out.mkdir(parents=True, exist_ok=True)
    seq = []
    for body in BODIES:
        folder = BASE / 'frames' / ('%s_bridge4_%s_red' % (t, body))
        rr = rows_of(folder)
        if rr:
            seq.append((body, folder, rr))
    if not seq:
        print('no frames for', t); return False
    p = out / ('%s_bridge4.mp4' % t)
    gen = imageio_ffmpeg.write_frames(str(p), (W, H), fps=60, codec='libx264', pix_fmt_out='yuv420p', output_params=['-crf', '20', '-preset', 'medium'], macro_block_size=8)
    gen.send(None)
    title = '%s %s × %s — %s' % (t, FRAME[t[0]], VERB[t[1]], WHAT[t[1]])
    passes = (('1×', 1, None), ('0.25× (처음 %.0f s)' % SLOW_SECONDS, 4, SLOW_SECONDS))
    for speed, rep, cut in passes:
        for body, folder, rr in seq:
            letter = ROWS[t][BODIES.index(body)]
            use = [r for r in rr if cut is None or float(r['ageSec']) <= cut]
            last_note = ''
            for i, r in enumerate(use):
                a, b = pair(folder, r)
                img = Image.new('RGB', (W, H), (16, 16, 16)); d = ImageDraw.Draw(img)
                img.paste(a, (0, 40)); img.paste(b, (960, 40))
                label(d, (10, 6), '%s   |   %s %s   |   %s' % (title, letter, BODY_KO[body], speed), f3)
                label(d, (12, 50), '게임 카메라 (어깨 · 피치 0°)   %.3f s' % float(r['ageSec']), f2)
                label(d, (972, 50), '판독용 높은 시점 (게임 카메라 아님)', f2)
                if r['note']:
                    last_note = r['note']
                if last_note:
                    label(d, (12, H - 36), last_note[:110], f2, fill=(255, 220, 120))
                fb = np.asarray(img).tobytes()
                for _ in range(rep * (20 if i == len(use) - 1 else 1)):
                    gen.send(fb)
    gen.close(); print('video', p)
    return True


def sheet(t, f2):
    ms = [0.1, 0.4, 0.9, 1.6, 2.5, 99.0]
    cw, ch = 400, 225
    out = BASE / 'sheets'; out.mkdir(parents=True, exist_ok=True)
    img = Image.new('RGB', (cw * len(ms), ch * 10 + 50), (16, 16, 16)); d = ImageDraw.Draw(img)
    label(d, (10, 12), '%s %s × %s — %s (위: 게임 카메라, 아래: 판독용 높은 시점 · 마지막 칸 = 기록 끝)' % (t, FRAME[t[0]], VERB[t[1]], WHAT[t[1]]), font(20))
    for bi, body in enumerate(BODIES):
        folder = BASE / 'frames' / ('%s_bridge4_%s_red' % (t, body))
        rr = rows_of(folder)
        if not rr:
            continue
        for mi, m in enumerate(ms):
            r = min(rr, key=lambda x: abs(float(x['ageSec']) - m)) if m < 90 else rr[-1]
            a, b = pair(folder, r, cw, ch)
            y = 50 + bi * 2 * ch
            img.paste(a, (mi * cw, y)); img.paste(b, (mi * cw, y + ch))
            label(d, (mi * cw + 6, y + 4), '%s %s %.2f s' % (ROWS[t][bi], BODY_KO[body], float(r['ageSec'])), f2)
    p = out / ('%s_bridge4.png' % t); img.save(p); print('sheet', p)


def index(f2):
    cw, ch = 480, 270
    cols = 5
    ts = list(ROWS)
    img = Image.new('RGB', (cw * cols, (ch * 2 + 30) * 4 + 60), (16, 16, 16)); d = ImageDraw.Draw(img)
    label(d, (10, 14), 'vol.2 받침 100자 — 옛 본편 부품 다리 (D308-150 V9 4단계) · 틀 20개, 한 칸 = 목 글자의 대표 순간 (위 게임 카메라 · 아래 판독용 높은 시점)', font(22))
    for i, t in enumerate(ts):
        x, y = (i % cols) * cw, 60 + (i // cols) * (ch * 2 + 30)
        folder = None; body = None
        for b in BODIES:
            fo = BASE / 'frames' / ('%s_bridge4_%s_red' % (t, b))
            if rows_of(fo):
                folder, body = fo, b; break
        label(d, (x + 6, y + 2), '%s %s×%s  %s' % (t, FRAME[t[0]][2:], VERB[t[1]], ROWS[t]), f2)
        if folder is None:
            continue
        rr = rows_of(folder)
        r = min(rr, key=lambda q: abs(float(q['ageSec']) - KEY[t]))
        a, b = pair(folder, r, cw, ch)
        img.paste(a, (x, y + 30)); img.paste(b, (x, y + 30 + ch))
        label(d, (x + 6, y + 36), '%s %s %.2f s' % (ROWS[t][BODIES.index(body)], BODY_KO[body], float(r['ageSec'])), f2)
    p = BASE / 'index_step4.png'; img.save(p); print('index', p)


def compare(t, f2, f3, upto=4.5):
    """<T>_old_vs_bridge4.mp4: the old main game (Legacy308 route, capture legacy=2) above, the bridge below, each game camera | reading view; the bridge cut at
    `upto` s (the old capture's length). Bodies that have both."""
    import imageio_ffmpeg
    W, H = 1920, 1128
    seq = []
    for body in BODIES:
        fo = BASE / 'frames' / ('%s_old_%s_red' % (t, body)); fn = BASE / 'frames' / ('%s_bridge4_%s_red' % (t, body))
        ra, rb = rows_of(fo), [r for r in rows_of(fn) if float(r['ageSec']) <= upto]
        if ra and rb:
            seq.append((body, fo, fn, ra, rb))
    if not seq:
        print('no old/bridge pair for', t); return
    p = BASE / 'videos' / ('%s_old_vs_bridge4.mp4' % t)
    gen = imageio_ffmpeg.write_frames(str(p), (W, H), fps=60, codec='libx264', pix_fmt_out='yuv420p', output_params=['-crf', '20', '-preset', 'medium'], macro_block_size=8)
    gen.send(None)
    for speed, rep in (('1×', 1), ('0.25×', 4)):
        for body, fo, fn, ra, rb in seq:
            n = max(len(ra), len(rb))
            for i in range(n):
                a = ra[min(i, len(ra) - 1)]; b = rb[min(i, len(rb) - 1)]
                img = Image.new('RGB', (W, H), (16, 16, 16)); d = ImageDraw.Draw(img)
                oa, og = pair(fo, a); ba, bg = pair(fn, b)
                img.paste(oa, (0, 40)); img.paste(og, (960, 40)); img.paste(ba, (0, 586)); img.paste(bg, (960, 586))
                label(d, (10, 6), '%s %s × %s  |  %s %s  |  %s  |  위: 옛 본편(Legacy308, legacy=2) · 아래: vol.2 다리  |  왼쪽 게임 카메라 · 오른쪽 판독용 시점' % (t, FRAME[t[0]], VERB[t[1]], ROWS[t][BODIES.index(body)], BODY_KO[body], speed), f2)
                label(d, (12, 50), '옛 본편 %.2f s' % float(a['ageSec']), f2); label(d, (12, 596), 'vol.2 다리 %.2f s' % float(b['ageSec']), f2)
                if b['note']:
                    label(d, (12, H - 34), b['note'][:110], f2, fill=(255, 220, 120))
                fb = np.asarray(img).tobytes()
                for _ in range(rep * (20 if i == n - 1 else 1)):
                    gen.send(fb)
    gen.close(); print('compare', p)


def main():
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
    if len(sys.argv) > 1 and sys.argv[1] == '--old':
        f2, f3 = font(18), font(22)
        for t in sys.argv[2:]:
            compare(t, f2, f3)
        return
    want = sys.argv[1:] or list(ROWS)
    f2, f3 = font(18), font(22)
    for t in want:
        if t == 'index':
            continue
        if video(t, f2, f3):
            sheet(t, f2)
    index(f2)


if __name__ == '__main__':
    main()
