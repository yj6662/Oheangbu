#!/usr/bin/env python3
"""stroke20 (D308-150 V7): review media for the USER (they judge the look by eye) + the machine rule report, from Vol2VfxCapture frames.

  python Tools/Art/SpellVFX308/stroke20/stroke20_media.py r6            all: videos, contact sheets, rules
  python Tools/Art/SpellVFX308/stroke20/stroke20_media.py r6 --only rules

Input : Art/SpellVFX308/Vol2Vfx/stroke20/<round>/frames/<T>_<variant>_<Body>_<back>/ (f_NNNNN.jpg 1280x720, one per game frame at 1/60 s of the SCOPE clock,
        index.csv, rules.csv + m_NNNNN.png masks: R = pixels the VFX changed, G = the enemy body's pixels; capture option rules=1)
Output: Art/SpellVFX308/Vol2Vfx/stroke20/
          videos/<T>_stroke20.mp4   five bodies, 적로 | 청림 side by side, first at game speed (60 fps = 1 x), then the same at 0.25 x
          sheets/<T>_sheet.png      five bodies x two grounds x five moments (native crops of the 1280x720 game frames, never upscaled)
          rules/stroke20_rules.md + .json   first cue, contact, dry-out, residue, enemy visibility, sizes at 3 / 6 / 12 m (P0)
The camera is the in-game shoulder camera (pivot 1.7 m, local (0.7, 0.55, -2.8), FOV 60) at pitch 0 (the game's default), main-world look (LookProfile308_PBZInkWash)
stepped at the realm of the ground (적로 / 청림 soil), a real enemy (Creatures308 forest bear)."""
import argparse
import csv
import json
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont

REPO = Path(__file__).resolve().parents[4]
BASE = REPO / 'Art/SpellVFX308/Vol2Vfx/stroke20'
BODIES = ['Wood', 'Fire', 'Earth', 'Metal', 'Water']
BODY_KO = {'Wood': '목', 'Fire': '화', 'Earth': '토', 'Metal': '금', 'Water': '수'}
ROWS = {'P0': '가나마사아', 'A0': '거너머서어', 'F0': '고노모소오', 'R0': '구누무수우'}
NAME = {'P0': 'ㅏ 한 번 (경로)', 'A0': 'ㅓ 한 번 (패링)', 'F0': 'ㅗ 한 번 (부채)', 'R0': 'ㅜ 한 번 (띠)'}
BACK_KO = {'red': '적로', 'green': '청림'}
FONT = 'C:/Windows/Fonts/malgun.ttf'
# crop of the 1280x720 game frame around the action (x0, y0, x1, y1), per template
CROP = {'P0': (260, 260, 1020, 688), 'A0': (300, 220, 1060, 648), 'F0': (120, 280, 880, 708), 'R0': (220, 292, 980, 720)}
MOMENTS = {'P0': [0.05, 0.10, 0.15, 0.22, 0.30], 'A0': [0.10, 0.25, 0.30, 0.40, 0.55], 'F0': [0.033, 0.083, 0.15, 0.20, 0.25],
           'R0': [0.033, 0.083, 0.15, 0.20, 0.25]}


def font(n):
    try:
        return ImageFont.truetype(FONT, n)
    except OSError:
        return ImageFont.load_default()


def label(d, xy, text, f, fill=(240, 235, 225)):
    l, t, r, b = d.textbbox(xy, text, font=f)
    d.rectangle((l - 5, t - 3, r + 5, b + 3), fill=(0, 0, 0))
    d.text(xy, text, font=f, fill=fill)


def job(frames, t, body, back, variant='main'):
    return frames / ('%s_%s_%s_%s' % (t, variant, body, back))


def video_rows(folder):
    if not (folder / 'index.csv').exists():
        return []
    rows = [r for r in csv.DictReader(open(folder / 'index.csv', encoding='utf-8')) if r['video'] == '1']
    return [r for r in rows if (folder / ('f_%05d.jpg' % int(r['frame']))).exists()]


def videos(rnd, frames, out):
    import imageio_ffmpeg
    out.mkdir(parents=True, exist_ok=True)
    f1, f2 = font(26), font(20)
    made = []
    for t in ROWS:
        W, H = 1920, 544
        seq = []
        for body in BODIES:
            a = video_rows(job(frames, t, body, 'red')); b = video_rows(job(frames, t, body, 'green'))
            n = max(len(a), len(b))
            if n == 0:
                continue
            for i in range(n):
                seq.append((body, a[min(i, len(a) - 1)] if a else None, b[min(i, len(b) - 1)] if b else None, i == n - 1))
        if not seq:
            continue
        p = out / ('%s_stroke20.mp4' % t)
        gen = imageio_ffmpeg.write_frames(str(p), (W, H), fps=60, codec='libx264', pix_fmt_out='yuv420p', output_params=['-crf', '18', '-preset', 'medium'], macro_block_size=8)
        gen.send(None)

        def frame(body, ra, rb, speed):
            img = Image.new('RGB', (W, H), (16, 16, 16)); d = ImageDraw.Draw(img)
            for k, (r, back) in enumerate(((ra, 'red'), (rb, 'green'))):
                if r is None:
                    continue
                im = Image.open(job(frames, t, body, back) / ('f_%05d.jpg' % int(r['frame']))).convert('RGB').resize((960, 540), Image.LANCZOS)
                img.paste(im, (k * 960, 2))
                label(d, (k * 960 + 12, 10), '%s  %s %s  |  %s  |  %.3f s  |  %s' % (t, ROWS[t][BODIES.index(body)], BODY_KO[body], BACK_KO[back], float(r['ageSec']), speed), f2)
            label(d, (12, H - 40), NAME[t] + '  — stroke20 (오의 한 획을 작게)  · 게임 어깨 카메라 피치 0°', f2)
            return np.asarray(img).tobytes()

        for speed, rep in (('1×', 1), ('0.25×', 4)):
            for body, ra, rb, last in seq:
                fb = frame(body, ra, rb, speed)
                for _ in range(rep * (12 if last else 1)):           # a short hold on the last frame of each body
                    gen.send(fb)
        gen.close()
        made.append(str(p))
    return made


def sheets(rnd, frames, out):
    out.mkdir(parents=True, exist_ok=True)
    f = font(20)
    made = []
    for t in ROWS:
        x0, y0, x1, y1 = CROP[t]; cw, ch = x1 - x0, y1 - y0
        ms = MOMENTS[t]
        img = Image.new('RGB', (cw * len(ms), ch * 10 + 50), (16, 16, 16)); d = ImageDraw.Draw(img)
        label(d, (12, 12), '%s %s — stroke20 · 5몸 × 적로/청림 · 게임 프레임 원본 크기 자르기 (확대 없음) · 시각 = 시전 시계' % (t, NAME[t]), font(24))
        for bi, body in enumerate(BODIES):
            for gi, back in enumerate(('red', 'green')):
                rows = video_rows(job(frames, t, body, back))
                y = 50 + (bi * 2 + gi) * ch
                for mi, m in enumerate(ms):
                    if not rows:
                        continue
                    r = min(rows, key=lambda x: abs(float(x['ageSec']) - m))
                    im = Image.open(job(frames, t, body, back) / ('f_%05d.jpg' % int(r['frame']))).convert('RGB').crop(CROP[t])
                    img.paste(im, (mi * cw, y))
                    label(d, (mi * cw + 8, y + 6), '%s %s %s %.3f s' % (ROWS[t][bi], BODY_KO[body], BACK_KO[back], float(r['ageSec'])), f)
        p = out / ('%s_sheet.png' % t)
        img.save(p)
        made.append(str(p))
    return made


def compare(rnd, frames, legacy_frames, out_v, out_s):
    """the old main-game spells (Legacy308 route: the same letters, 적로, the same camera / look / bear) left, stroke20 right: per template one mp4 (five bodies,
    1 x then 0.25 x) and one sheet (five bodies x old / new x six moments)"""
    import imageio_ffmpeg
    out_v.mkdir(parents=True, exist_ok=True); out_s.mkdir(parents=True, exist_ok=True)
    f2 = font(20); made = []
    for t in ROWS:
        W, H = 1920, 544
        seq = []
        for body in BODIES:
            a = video_rows(legacy_frames / ('%s_legacy_%s_red' % (t, body))); b = video_rows(job(frames, t, body, 'red'))
            n = max(len(a), len(b))
            for i in range(n):
                seq.append((body, a[min(i, len(a) - 1)] if a else None, b[min(i, len(b) - 1)] if b else None, i == n - 1))
        if not seq:
            continue
        p = out_v / ('%s_old_vs_stroke20.mp4' % t)
        gen = imageio_ffmpeg.write_frames(str(p), (W, H), fps=60, codec='libx264', pix_fmt_out='yuv420p', output_params=['-crf', '18', '-preset', 'medium'], macro_block_size=8)
        gen.send(None)
        for speed, rep in (('1×', 1), ('0.25×', 4)):
            for body, ra, rb, last in seq:
                img = Image.new('RGB', (W, H), (16, 16, 16)); d = ImageDraw.Draw(img)
                for k, (r, folder, tag) in enumerate(((ra, legacy_frames / ('%s_legacy_%s_red' % (t, body)), '옛 본편 (Legacy308)'), (rb, job(frames, t, body, 'red'), 'stroke20 (vol.2)'))):
                    if r is None:
                        continue
                    im = Image.open(folder / ('f_%05d.jpg' % int(r['frame']))).convert('RGB').resize((960, 540), Image.LANCZOS)
                    img.paste(im, (k * 960, 2))
                    label(d, (k * 960 + 12, 10), '%s  |  %s %s %s  |  적로  |  %.3f s  |  %s' % (tag, t, ROWS[t][BODIES.index(body)], BODY_KO[body], float(r['ageSec']), speed), f2)
                fb = np.asarray(img).tobytes()
                for _ in range(rep * (12 if last else 1)):
                    gen.send(fb)
        gen.close(); made.append(str(p))
        # the sheet
        ms = [0.1, 0.2, 0.3, 0.45, 0.7, 1.0]
        cw, ch = 640, 360
        img = Image.new('RGB', (cw * len(ms), ch * 10 + 50), (16, 16, 16)); d = ImageDraw.Draw(img)
        label(d, (12, 12), '%s %s — 옛 본편(위) 대 stroke20(아래) · 적로 · 같은 카메라·룩·곰 · 시각 = 시전부터' % (t, NAME[t]), font(24))
        for bi, body in enumerate(BODIES):
            for gi, (folder, tag) in enumerate(((legacy_frames / ('%s_legacy_%s_red' % (t, body)), '옛'), (job(frames, t, body, 'red'), '새'))):
                rows = video_rows(folder); y = 50 + (bi * 2 + gi) * ch
                for mi, m in enumerate(ms):
                    if not rows:
                        continue
                    r = min(rows, key=lambda x: abs(float(x['ageSec']) - m))
                    im = Image.open(folder / ('f_%05d.jpg' % int(r['frame']))).convert('RGB').resize((cw, ch), Image.LANCZOS)
                    img.paste(im, (mi * cw, y))
                    label(d, (mi * cw + 6, y + 4), '%s %s %s %.2f s' % (tag, ROWS[t][bi], BODY_KO[body], float(r['ageSec'])), f2)
        sp = out_s / ('%s_old_vs_stroke20.png' % t); img.save(sp); made.append(str(sp))
    return made


def thickness(mask_png, region=None):
    """the widest stroke in a VFX mask: 2 x the largest inscribed radius (scipy distance transform), px of the 1280x720 frame"""
    from scipy import ndimage
    m = np.asarray(Image.open(mask_png).convert('RGB'))[:, :, 0] > 127
    # V8: a body-coloured stroke whose fill is close to the ground's value is still read by its ink rim: the silhouette (holes filled) is the stroke's size
    m = ndimage.binary_fill_holes(m)
    if region is not None:
        x0, y0, x1, y1 = region; mm = np.zeros_like(m); mm[y0:y1, x0:x1] = m[y0:y1, x0:x1]; m = mm
    if not m.any():
        return 0.0
    return float(ndimage.distance_transform_edt(m).max() * 2.0)


def rules(rnd, frames, out):
    out.mkdir(parents=True, exist_ok=True)
    res = []
    for folder in sorted(frames.iterdir()):
        if not (folder / 'rules.csv').exists():
            continue
        t, variant, body, back = folder.name.split('_')
        rr = list(csv.DictReader(open(folder / 'rules.csv', encoding='utf-8')))
        if not rr:
            continue
        age = [float(x['ageSec']) for x in rr]; vfx = [int(x['vfxPx']) for x in rr]; en = [int(x['enemyPx']) for x in rr]
        vis = [float(x['enemyVisible']) for x in rr]; con = [int(x['contacts']) for x in rr]
        first = next((a for a, v in zip(age, vfx) if v >= 150), None)
        contact = next((a for a, c in zip(age, con) if c > 0), None)
        if t == 'A0':
            contact = next((a for a, x in zip(age, rr) if a >= 0.22), None)       # the lab-simulated parry at 0.22 s
        last = max((a for a, v in zip(age, vfx) if v >= 60), default=None)
        seen = [v for v, e in zip(vis, en) if e >= 1500]
        minvis = min(seen) if seen else None
        rec = dict(job=folder.name, template=t, variant=variant, body=body, back=back, frames=len(rr), first_cue=first, contact=contact, last_visible=last,
                   gone_after_contact=(None if last is None or contact is None else round(last - contact, 3)), min_enemy_visible=minvis,
                   max_vfx_px=max(vfx))
        if t == 'P0' and variant.startswith('d') and contact is not None:
            # the contact stroke's head 0.08 s after the contact; the flying stroke at half way to the body
            i_c = min(range(len(age)), key=lambda i: abs(age[i] - (contact + 0.08)))
            i_f = min(range(len(age)), key=lambda i: abs(age[i] - (0.06 + (contact - 0.06) * 0.6)))      # V8: the head leaves the tip at the swing's apex (0.06 s)
            rec['contact_head_px_720'] = thickness(folder / ('m_%05d.png' % int(rr[i_c]['frame'])))
            rec['flying_px_720'] = thickness(folder / ('m_%05d.png' % int(rr[i_f]['frame'])))
            rec['contact_head_px_1080'] = round(rec['contact_head_px_720'] * 1.5, 1); rec['flying_px_1080'] = round(rec['flying_px_720'] * 1.5, 1)
        res.append(rec)
    # verdicts (the rules of D308-150 V7 / plan B; the look itself is the user's)
    lines = ['# stroke20 rule checks (machine) — round %s' % rnd, '',
             'Judging = the user by eye (D308-150 V7 / V8). These are only the rules: first cue <= 0.08 s, the contact mark visible 0.6 - 1 s then gone (V8, visual only), '
             'no residue, enemy body >= 70 % visible in every frame where it is on screen, sizes at 3 / 6 / 12 m. Pixels are of the 1280x720 video frame (x 1.5 for 1080p).', '',
             '| job | first cue s | contact s | last VFX s | gone after contact s | min enemy visible | sizes (1080p px) | verdict |', '|---|---:|---:|---:|---:|---:|---|---|']
    ok_all = True
    for r in res:
        bad = []
        if r['first_cue'] is None or r['first_cue'] > 0.0834:
            bad.append('first cue')
        # D308-150 V8: the contact mark stays 0.6 - 1 s (visual only), then nothing; the swing's apex (0.06 s) is the earliest visual contact
        if r['gone_after_contact'] is None or r['gone_after_contact'] > 1.05 or r['gone_after_contact'] < 0.55:
            bad.append('mark 0.6-1 s')
        if r['min_enemy_visible'] is not None and r['min_enemy_visible'] < 0.70:
            bad.append('enemy visible < 70 %')
        size = ''
        if 'contact_head_px_1080' in r:
            size = 'flying %.0f, contact head %.0f' % (r['flying_px_1080'], r['contact_head_px_1080'])
            if r['flying_px_1080'] < 12 or r['contact_head_px_1080'] < 20:
                bad.append('size')
        r['verdict'] = 'PASS' if not bad else 'FAIL ' + ', '.join(bad)
        ok_all &= not bad
        fmt = lambda v: '-' if v is None else ('%.3f' % v if isinstance(v, float) else str(v))
        lines.append('| %s | %s | %s | %s | %s | %s | %s | %s |' % (r['job'], fmt(r['first_cue']), fmt(r['contact']), fmt(r['last_visible']), fmt(r['gone_after_contact']),
                                                              fmt(r['min_enemy_visible']), size or '-', r['verdict']))
    lines.insert(2, '**%s** — %d jobs, %d pass' % ('ALL PASS' if ok_all else 'SOME FAIL', len(res), sum(1 for r in res if r['verdict'] == 'PASS')))
    lines.insert(3, '')
    (out / 'stroke20_rules.md').write_text('\n'.join(lines) + '\n', encoding='utf-8')
    (out / 'stroke20_rules.json').write_text(json.dumps(res, ensure_ascii=False, indent=1), encoding='utf-8')
    return [str(out / 'stroke20_rules.md')], ok_all


def main():
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
    ap = argparse.ArgumentParser(); ap.add_argument('round'); ap.add_argument('--only', default='')
    a = ap.parse_args()
    frames = BASE / a.round / 'frames'
    if a.only in ('', 'rules'):
        made, ok = rules(a.round, frames, BASE / 'rules'); print('rules', 'ALL PASS' if ok else 'SOME FAIL', made)
    if a.only in ('', 'sheets'):
        print('sheets', sheets(a.round, frames, BASE / 'sheets'))
    if a.only in ('', 'videos'):
        print('videos', videos(a.round, frames, BASE / 'videos'))
    if a.only in ('', 'compare') and (BASE / 'legacy' / 'frames').exists():
        print('compare', compare(a.round, frames, BASE / 'legacy' / 'frames', BASE / 'videos', BASE / 'sheets'))


if __name__ == '__main__':
    main()
